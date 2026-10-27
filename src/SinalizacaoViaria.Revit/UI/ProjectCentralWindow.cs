using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Central do Projeto (Etapa 10; Prompt de Interface §8): cabeçalho com o projeto, o documento e a situação geral; a situação
/// do projeto; os indicadores (valor, situação escrita e detalhe – sem zero para o que não se sabe) com o atalho para o módulo
/// ou relatório; e os módulos com a operação principal. O comando escolhido roda depois que a janela fecha
/// (<see cref="Comando"/>). Medir o pavimento percorre as peças e só acontece no "Calcular".
/// </summary>
public sealed class ProjectCentralWindow : Window
{
    private readonly string _projeto;
    private readonly Func<ResumoDoProjeto> _medirPavimento;
    private ResumoDoProjeto _resumo;
    private readonly StackPanel _body = new();
    private readonly TextBlock _geral = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };

    /// <summary>Comando da faixa escolhido na Central (classe Cmd*); nulo = só fechou.</summary>
    public string? Comando { get; private set; }

    public ProjectCentralWindow(string projeto, string documento, string versao, ResumoDoProjeto resumo, Func<ResumoDoProjeto> medirPavimento)
    {
        _projeto = projeto;
        _resumo = resumo;
        _medirPavimento = medirPavimento;
        Title = "Central do Projeto – SinalizaBIM";
        Width = 1180;
        Height = 820;
        MinWidth = 760;
        MinHeight = 480;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;
        Background = UiTokens.Panel;

        var root = new DockPanel { Margin = new Thickness(UiTokens.SpaceL) };

        // ---------------------------------------------------------------- cabeçalho
        var head = new Border { Background = UiTokens.Accent, CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10) };
        var headDock = new DockPanel();
        var acoes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        acoes.Children.Add(Botao("Configurações", "CmdConfiguracoes", "Preferências do plugin, limites geométricos e Autoteste."));
        acoes.Children.Add(Botao("Normas / Sobre", "CmdSobre", "Normas de referência e informações do plugin."));
        DockPanel.SetDock(acoes, Dock.Right);
        headDock.Children.Add(acoes);
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = "CENTRAL DO PROJETO", Foreground = UiTokens.TextOnAccentMuted, FontSize = UiTokens.FontCaption, FontWeight = FontWeights.SemiBold });
        info.Children.Add(new TextBlock { Text = projeto, Foreground = UiTokens.TextOnAccent, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        info.Children.Add(new TextBlock { Text = $"{documento} · SinalizaBIM {versao}", Foreground = UiTokens.TextOnAccent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });
        info.Children.Add(new Border { Background = UiTokens.Surface, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, Child = _geral });
        headDock.Children.Add(info);
        head.Child = headDock;
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        // ---------------------------------------------------------------- rodapé
        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copiar = new Button { Content = "Copiar resumo", Style = TryFindResource("Secondary") as Style, ToolTip = "Copia a situação, os indicadores e os módulos como texto." };
        copiar.Click += (_, _) =>
        {
            try { Clipboard.SetText(CentralDoProjeto.Texto(_resumo, _projeto, DateTime.Now)); copiar.Content = "Copiado ✓"; }
            catch { copiar.Content = "Não foi possível copiar"; }
        };
        var fechar = new Button { Content = "Fechar", IsCancel = true, Style = TryFindResource("Primary") as Style };
        botoes.Children.Add(copiar);
        botoes.Children.Add(fechar);
        DockPanel.SetDock(botoes, Dock.Right);
        foot.Children.Add(botoes);
        foot.Children.Add(new TextBlock
        {
            Style = TryFindResource("Hint") as Style, VerticalAlignment = VerticalAlignment.Center,
            Text = "Valores do modelo como está e dos resultados gravados (terraplenagem aplicada, última verificação), com a data. Nada é alterado aqui. " +
                   "Operações recentes não aparecem: o plugin não guarda esse histórico.",
        });
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        root.Children.Add(UiHelpers.Scroller(_body));
        Content = root;
        Render();
    }

    private Button Botao(string texto, string comando, string dica)
    {
        var b = new Button { Content = texto, Style = TryFindResource("Secondary") as Style, ToolTip = dica, Margin = new Thickness(6, 0, 0, 0) };
        b.Click += (_, _) => Abrir(comando);
        return b;
    }

    private void Abrir(string comando)
    {
        Comando = comando;
        DialogResult = true;
    }

    private static string Rotulo(string comando) => RibbonBuilder.LabelOf(comando) ?? comando;

    private static Brush Cor(SituacaoValor s) => s switch
    {
        SituacaoValor.Calculado => UiTokens.Success, SituacaoValor.Desatualizado => UiTokens.Warning, SituacaoValor.Invalido => UiTokens.Error,
        _ => UiTokens.TextSecondary,
    };

    private void Render()
    {
        _geral.Text = "Situação: " + _resumo.SituacaoGeral;
        _geral.Foreground = ContextPanelPage.Cor(_resumo.NivelGeral);
        _body.Children.Clear();

        // ---------------------------------------------------------------- situação (§8.4)
        _body.Children.Add(new TextBlock { Text = "Situação do projeto", Style = TryFindResource("SectionTitle") as Style, Margin = new Thickness(0, 0, 0, 4) });
        var sit = new StackPanel();
        foreach (var i in _resumo.Situacao)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            if (i.Comando != null && i.Acao != null)
            {
                var b = new Button { Content = Rotulo(i.Comando), Style = TryFindResource("Secondary") as Style, ToolTip = i.Acao, MinWidth = 0, Padding = new Thickness(8, 2, 8, 2) };
                var c = i.Comando;
                b.Click += (_, _) => Abrir(c);
                DockPanel.SetDock(b, Dock.Right);
                linha.Children.Add(b);
            }
            var t = ContextPanelPage.Linha(i.Nivel, $"{i.Tema}: {i.Texto}" + (i.Acao != null ? $" → {i.Acao}" : ""));
            t.VerticalAlignment = VerticalAlignment.Center;
            linha.Children.Add(t);
            sit.Children.Add(linha);
        }
        _body.Children.Add(new Border { Style = TryFindResource("Card") as Style, Child = sit });

        // ---------------------------------------------------------------- indicadores (§8.2)
        _body.Children.Add(new TextBlock { Text = "Resumo do projeto", Style = TryFindResource("SectionTitle") as Style });
        var cards = new WrapPanel();
        foreach (var i in _resumo.Indicadores) cards.Children.Add(Cartao(i));
        _body.Children.Add(cards);

        // ---------------------------------------------------------------- módulos (§8.3)
        _body.Children.Add(new TextBlock { Text = "Módulos", Style = TryFindResource("SectionTitle") as Style });
        var mods = new WrapPanel();
        foreach (var m in _resumo.Modulos) mods.Children.Add(Modulo(m));
        _body.Children.Add(mods);
    }

    private Border Cartao(IndicadorProjeto i)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = i.Nome, Style = TryFindResource("Caption") as Style });
        sp.Children.Add(new TextBlock { Text = i.ValorTexto, Style = TryFindResource("ValueLarge") as Style });
        sp.Children.Add(new TextBlock { Text = CentralDoProjeto.Nome(i.Situacao), Foreground = Cor(i.Situacao), FontWeight = FontWeights.SemiBold, FontSize = UiTokens.FontCaption });
        sp.Children.Add(new TextBlock { Text = i.Detalhe, Style = TryFindResource("Caption") as Style, Margin = new Thickness(0, 2, 0, 4) });
        var acoes = new WrapPanel();
        if (i.Chave == "pavimento" && i.Situacao == SituacaoValor.NaoCalculado)
        {
            var calc = new Button { Content = "Calcular", Style = TryFindResource("Secondary") as Style, MinWidth = 0, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Mede agora as peças das pistas e dos nós (pode levar alguns segundos em projetos grandes)." };
            calc.Click += (_, _) => Medir();
            acoes.Children.Add(calc);
        }
        if (i.Comando is { } c)
        {
            var abrir = new Button { Content = Rotulo(c), Style = TryFindResource("Secondary") as Style, MinWidth = 0, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0),
                ToolTip = $"Abre {Rotulo(c)}." };
            abrir.Click += (_, _) => Abrir(c);
            acoes.Children.Add(abrir);
        }
        sp.Children.Add(acoes);
        return new Border { Style = TryFindResource("Card") as Style, Width = 262, Margin = new Thickness(0, 0, 8, 8), Child = sp };
    }

    private Border Modulo(ModuloProjeto m)
    {
        var dock = new DockPanel();
        var icone = new Image { Source = IconFactory.Large(m.Icone), Width = 32, Height = 32, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(icone, Dock.Left);
        dock.Children.Add(icone);
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = m.Nome, Style = TryFindResource("Subtitle") as Style, Margin = new Thickness(0) });
        sp.Children.Add(new TextBlock { Text = m.Descricao, Style = TryFindResource("Caption") as Style });
        sp.Children.Add(new TextBlock { Text = m.Estado, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        var b = new Button { Content = m.Operacao, Style = TryFindResource("Secondary") as Style, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0),
            ToolTip = $"Abre {Rotulo(m.Comando)}." };
        var c = m.Comando;
        b.Click += (_, _) => Abrir(c);
        sp.Children.Add(b);
        dock.Children.Add(sp);
        return new Border { Style = TryFindResource("Card") as Style, Width = 262, Margin = new Thickness(0, 0, 8, 8), Child = dock };
    }

    private void Medir()
    {
        var antes = Mouse.OverrideCursor;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            _resumo = _medirPavimento();
            Render();
        }
        catch (Exception ex)
        {
            Log.Error("Central do Projeto – medir o pavimento", ex);
            UiHelpers.Error("Não foi possível medir o pavimento: " + ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = antes;
        }
    }
}
