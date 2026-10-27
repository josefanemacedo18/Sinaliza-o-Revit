using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using SinalizacaoViaria.Core.Automation;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Janela de progresso das operações demoradas (Etapa 13; Prompt de Interface §21): a etapa, o que está sendo feito, quantos de
/// quantos, a barra só quando o total é conhecido (senão o indicador de atividade, sem percentual inventado) e Cancelar enquanto
/// for seguro. A operação roda no fio do Revit (a API não aceita outro): a cada atualização a fila de mensagens é processada para
/// a janela redesenhar e receber o clique em Cancelar; enquanto isso a janela principal do Revit fica desabilitada, como num
/// diálogo modal, e volta ao fechar (<see cref="Dispose"/>, mesmo com erro).
/// </summary>
public sealed class ProgressWindow : Window, IProgressoOperacao, IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool enable);

    private readonly TextBlock _etapa = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _descricao = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _contagem = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly ProgressBar _barra = new() { Height = 10, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 8, 0, 4), IsIndeterminate = true };
    private readonly Button _cancelar = new() { Content = "Cancelar", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
    private readonly string _unidade;
    private bool _revitDesabilitado;
    private bool _fechando;

    /// <summary>O usuário pediu para cancelar (botão ou fechar a janela); a operação para no próximo ponto seguro.</summary>
    public bool Cancelado { get; private set; }

    private ProgressWindow(string titulo, string unidade)
    {
        _unidade = unidade;
        Title = titulo + " – SinalizaBIM";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;
        _cancelar.Style = TryFindResource("Secondary") as Style;
        _descricao.Style = TryFindResource("Hint") as Style;
        _cancelar.Click += (_, _) => PedirCancelar();
        // Fechar pela janela é pedir para cancelar: ela só fecha quando a operação termina.
        Closing += (_, e) =>
        {
            if (_fechando) return;
            e.Cancel = true;
            PedirCancelar();
        };
        var sp = new StackPanel { Margin = new Thickness(UiTokens.SpaceL) };
        sp.Children.Add(new TextBlock { Text = titulo, Style = TryFindResource("Title") as Style });
        sp.Children.Add(_etapa);
        sp.Children.Add(_descricao);
        sp.Children.Add(_barra);
        sp.Children.Add(_contagem);
        sp.Children.Add(_cancelar);
        Content = sp;
    }

    /// <summary>Abre a janela (sem bloquear) com a etapa inicial; feche com <see cref="Dispose"/> (using).</summary>
    /// <param name="unidade">O que é contado ("marcas", "elementos").</param>
    public static ProgressWindow Abrir(string titulo, string unidade, string etapa = "Preparando…")
    {
        var w = new ProgressWindow(titulo, unidade);
        if (UiHelpers.RevitHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(w).Owner = UiHelpers.RevitHandle;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            EnableWindow(UiHelpers.RevitHandle, false);
            w._revitDesabilitado = true;
        }
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.Show();
        w.Informar(new EstadoProgresso(etapa));
        return w;
    }

    private void PedirCancelar()
    {
        if (!_cancelar.IsEnabled) return;
        Cancelado = true;
        _cancelar.IsEnabled = false;
        _cancelar.Content = "Cancelando…";
    }

    public void Informar(EstadoProgresso e)
    {
        _etapa.Text = e.Etapa;
        _descricao.Text = e.Descricao ?? "";
        _contagem.Text = e.Contagem(_unidade);
        if (e.Fracao is { } f)
        {
            _barra.IsIndeterminate = false;
            _barra.Value = f;
        }
        else _barra.IsIndeterminate = true;
        if (!Cancelado)
        {
            _cancelar.IsEnabled = e.Cancelavel;
            _cancelar.ToolTip = e.Cancelavel ? "Para no próximo ponto seguro, sem alterar o modelo." : "Nesta etapa não dá mais para cancelar com segurança.";
        }
        Bombear();
    }

    /// <summary>Processa a fila de mensagens desta janela: redesenha e recebe o clique em Cancelar.</summary>
    private void Bombear()
    {
        try { Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })); }
        catch (Exception) { /* janela já fechada */ }
    }

    public void Dispose()
    {
        if (_revitDesabilitado)
        {
            EnableWindow(UiHelpers.RevitHandle, true);
            _revitDesabilitado = false;
        }
        _fechando = true;
        try { Close(); }
        catch (Exception) { /* já fechada */ }
    }
}
