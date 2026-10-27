using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SinalizacaoViaria.Core.Automation;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Conteúdo do painel contextual (Etapa 10; Prompt de Interface §9): título e código da marca, o estado (o nível escrito,
/// não só a cor), as propriedades agrupadas, as notas e as operações compatíveis. Só mostra – as operações são comandos
/// do plugin, executados pelo serviço do painel.
/// </summary>
public sealed class ContextPanelPage : Page
{
    private readonly Action<string> _run;
    private readonly StackPanel _root = new() { Margin = new Thickness(10) };

    public ContextPanelPage(Action<string> run)
    {
        _run = run;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        Background = UiTokens.Surface;
        FontFamily = new FontFamily(UiTokens.FontFamily);
        FontSize = UiTokens.FontBody;
        Foreground = UiTokens.Text;
        Content = UiHelpers.Scroller(_root);
        Show(PainelContextual.Vazio());
    }

    private Style? S(string key) => TryFindResource(key) as Style;

    /// <summary>Cor de cada nível – sempre com a palavra do nível ao lado: a cor nunca é o único sinal.</summary>
    internal static Brush Cor(NivelSituacao n) => n switch
    {
        NivelSituacao.Ok => UiTokens.Success, NivelSituacao.Aviso => UiTokens.Warning, NivelSituacao.Erro => UiTokens.Error,
        NivelSituacao.Informacao => UiTokens.Accent, _ => UiTokens.TextSecondary,
    };

    /// <summary>Linha "NÍVEL texto": a palavra do nível em destaque, na cor dele.</summary>
    internal static TextBlock Linha(NivelSituacao nivel, string texto)
    {
        var linha = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 3) };
        linha.Inlines.Add(new System.Windows.Documents.Run(CentralDoProjeto.Nome(nivel).ToUpperInvariant() + "  ") { Foreground = Cor(nivel), FontWeight = FontWeights.SemiBold });
        linha.Inlines.Add(new System.Windows.Documents.Run(texto));
        return linha;
    }

    public void Show(ContextoSelecao c)
    {
        _root.Children.Clear();
        _root.Children.Add(new TextBlock { Text = c.Titulo, Style = S("Title"), TextWrapping = TextWrapping.Wrap });
        if (c.Subtitulo.Length > 0) _root.Children.Add(new TextBlock { Text = c.Subtitulo, Style = S("Hint") });

        if (c.Estado.Count > 0)
        {
            _root.Children.Add(new TextBlock { Text = "Estado", Style = S("SectionTitle") });
            foreach (var e in c.Estado) _root.Children.Add(Linha(e.Nivel, e.Texto));
        }

        foreach (var g in c.Propriedades.GroupBy(p => p.Grupo))
        {
            _root.Children.Add(new TextBlock { Text = g.Key, Style = S("SectionTitle") });
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 170 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = UiHelpers.MinFieldWidth });
            foreach (var p in g)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var nome = new TextBlock { Text = p.Nome, Style = S("Caption"), Margin = new Thickness(0, 2, 10, 2), VerticalAlignment = VerticalAlignment.Top };
                var valor = new TextBlock { Text = p.Valor, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(nome, grid.RowDefinitions.Count - 1);
                Grid.SetRow(valor, grid.RowDefinitions.Count - 1);
                Grid.SetColumn(valor, 1);
                grid.Children.Add(nome);
                grid.Children.Add(valor);
            }
            _root.Children.Add(grid);
        }

        foreach (var n in c.Notas)
            _root.Children.Add(new Border { Style = S("BannerInfo"), Child = new TextBlock { Text = n, TextWrapping = TextWrapping.Wrap } });

        if (c.Operacoes.Count > 0)
        {
            _root.Children.Add(new TextBlock { Text = "Operações", Style = S("SectionTitle") });
            var ops = new WrapPanel();
            foreach (var o in c.Operacoes)
            {
                var b = new Button { Content = o.Rotulo, Style = S("Secondary"), Margin = new Thickness(0, 0, 6, 6), ToolTip = $"Abre {o.Rotulo}, como o botão da faixa de opções." };
                var comando = o.Comando;
                b.Click += (_, _) => _run(comando);
                ops.Children.Add(b);
            }
            _root.Children.Add(ops);
        }
    }
}
