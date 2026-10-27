using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Resultados do parcelamento (Etapa 11; Prompt de Interface §13), aberta pelo Loteamento: uma aba para cada parte – área de
/// intervenção e limites, quadras, lotes, sistema viário associado, áreas públicas, parâmetros dimensionais, áreas residuais e
/// verificação –, com as tabelas das quadras, dos lotes e das categorias. Só lê: a seleção (<see cref="Selecao"/>) ou o comando
/// escolhido (<see cref="Comando"/>) voltam ao Loteamento. O conteúdo vem de <see cref="ResultadosParcelamento"/>.
/// </summary>
public sealed class LotResultsWindow : Window
{
    public sealed record LinhaQuadra(string Quadra, string Lotes, string Area, string Categorias, string Id);
    public sealed record LinhaLote(string Quadra, string Lote, string Categoria, string Area, string Frente, string Problema, string QuadraId, string Chave);
    public sealed record LinhaCat(string Categoria, string Quantidade, string Area, string Percentual, string Publica, string Minimo);

    private readonly TelaParcelamento _tela;

    /// <summary>Comando da faixa escolhido (classe Cmd*); nulo = nenhum.</summary>
    public string? Comando { get; private set; }
    /// <summary>Quadras (e, quando há, o lote pela chave) a selecionar no modelo.</summary>
    public IReadOnlyList<(string QuadraId, string? Chave)> Selecao { get; private set; } = Array.Empty<(string, string?)>();

    public LotResultsWindow(TelaParcelamento tela)
    {
        _tela = tela;
        Title = "Resultados do parcelamento – SinalizaBIM";
        Width = 1120;
        Height = 780;
        MinWidth = 780;
        MinHeight = 500;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;

        var root = new DockPanel { Margin = new Thickness(UiTokens.SpaceL) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(new TextBlock { Text = "Resultados do parcelamento", Style = S("Title") });
        head.Children.Add(new TextBlock { Text = tela.Resumo, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = "Os números são os do mesmo resumo dos quadros e do memorial. Conformidade legal ou urbanística não é afirmada: só os mínimos e o lote mínimo " +
                   "informados no loteamento são conferidos.",
        });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        botoes.Children.Add(Botao("Quadros do Loteamento", "CmdQuadrosLoteamento"));
        botoes.Children.Add(Botao("Memorial do Loteamento", "CmdMemorialLoteamento"));
        var copiar = new Button { Content = "Copiar resumo", Style = S("Secondary"), ToolTip = "Copia o resumo e cada parte como texto." };
        copiar.Click += (_, _) =>
        {
            try { Clipboard.SetText(ResultadosParcelamento.Texto(_tela)); copiar.Content = "Copiado ✓"; }
            catch { copiar.Content = "Não foi possível copiar"; }
        };
        botoes.Children.Add(copiar);
        botoes.Children.Add(new Button { Content = "Fechar", IsCancel = true, Style = S("Primary") });
        DockPanel.SetDock(botoes, Dock.Right);
        foot.Children.Add(botoes);
        foot.Children.Add(new TextBlock { Style = S("Hint"), VerticalAlignment = VerticalAlignment.Center, Text = "Somente leitura: nada aqui altera o projeto." });
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        var tabs = new TabControl();
        foreach (var s in tela.Secoes)
        {
            var n = s.Titulo == ResultadosParcelamento.Verificacao ? tela.ComProblema.Count() : 0;
            tabs.Items.Add(new TabItem { Header = s.Titulo + (n > 0 ? $" ({n})" : ""), Content = UiHelpers.Scroller(Secao(s)) });
        }
        root.Children.Add(tabs);
        Content = root;
    }

    private Style? S(string key) => TryFindResource(key) as Style;

    private Button Botao(string texto, string comando)
    {
        var b = new Button { Content = texto, Style = S("Secondary"), ToolTip = $"Fecha esta janela e abre {texto}." };
        b.Click += (_, _) =>
        {
            Comando = comando;
            DialogResult = true;
        };
        return b;
    }

    private static string F(double v, string fmt = "N2") => UiHelpers.F(v, fmt);

    private StackPanel Secao(SecaoParcelamento s)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = s.Titulo, Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        foreach (var l in s.Linhas) sp.Children.Add(ContextPanelPage.Linha(l.Nivel, l.Texto));
        switch (s.Titulo)
        {
            case ResultadosParcelamento.Quadras when _tela.Quadras.Count > 0:
            {
                var linhas = _tela.Quadras.Select(q => new LinhaQuadra(q.Nome, q.Lotes.ToString(UiHelpers.PtBr), F(q.Area), q.Categorias, q.Id)).ToList();
                var g = UiTables.Grid(linhas, 420, true, ("Quadra", nameof(LinhaQuadra.Quadra), 0.5), ("Lotes", nameof(LinhaQuadra.Lotes), 0.5),
                    ("Área (m²)", nameof(LinhaQuadra.Area), 0.8), ("Área por categoria", nameof(LinhaQuadra.Categorias), 2.4));
                sp.Children.Add(g);
                sp.Children.Add(Selecionar(g, r => new[] { (((LinhaQuadra)r).Id, (string?)null) }, "as quadras"));
                break;
            }
            case ResultadosParcelamento.Lotes when _tela.Lotes.Count > 0:
                sp.Children.Add(new TextBlock { Text = "Parcelas (lotes e demais categorias)", Style = S("SectionTitle") });
                Lotes(sp, _tela.Lotes);
                break;
            case ResultadosParcelamento.Publicas when _tela.Categorias.Count > 0:
            {
                sp.Children.Add(new TextBlock { Text = "Quadro de áreas por categoria", Style = S("SectionTitle") });
                var linhas = _tela.Categorias.Select(c => new LinhaCat(c.Nome, c.Quantidade.ToString(UiHelpers.PtBr), F(c.Area), LotReport.P(c.Percentual),
                    c.Publica ? "pública" : "privada", c.Minimo is { } m ? LotReport.P(m) : "—")).ToList();
                sp.Children.Add(UiTables.Grid(linhas, 300, false, ("Categoria", nameof(LinhaCat.Categoria), 1.2), ("Quantidade", nameof(LinhaCat.Quantidade), 0.6),
                    ("Área (m²)", nameof(LinhaCat.Area), 0.8), ("% da gleba", nameof(LinhaCat.Percentual), 0.6), ("Tipo", nameof(LinhaCat.Publica), 0.6),
                    ("Mínimo informado", nameof(LinhaCat.Minimo), 0.8)));
                break;
            }
            case ResultadosParcelamento.Verificacao when _tela.ComProblema.Any():
                sp.Children.Add(new TextBlock { Text = "Lotes abaixo do lote mínimo informado", Style = S("SectionTitle") });
                Lotes(sp, _tela.ComProblema.ToList());
                break;
        }
        return sp;
    }

    private void Lotes(StackPanel sp, IReadOnlyList<LoteNaTela> lotes)
    {
        var linhas = lotes.Select(l => new LinhaLote(l.Quadra, l.Numero, l.Categoria, F(l.Area), l.Frente > 1e-6 ? F(l.Frente) : "—", l.Problema, l.QuadraId, l.Chave)).ToList();
        var g = UiTables.Grid(linhas, 460, true, ("Quadra", nameof(LinhaLote.Quadra), 0.5), ("Lote", nameof(LinhaLote.Lote), 0.5),
            ("Categoria", nameof(LinhaLote.Categoria), 0.9), ("Área (m²)", nameof(LinhaLote.Area), 0.7), ("Frente (m)", nameof(LinhaLote.Frente), 0.6),
            ("Problema", nameof(LinhaLote.Problema), 2.2));
        sp.Children.Add(g);
        sp.Children.Add(Selecionar(g, r => new[] { (((LinhaLote)r).QuadraId, (string?)((LinhaLote)r).Chave) }, "os lotes"));
    }

    /// <summary>"Selecionar no modelo" para as linhas escolhidas (nenhuma escolhida = todas); duplo clique numa linha faz o mesmo.</summary>
    private StackPanel Selecionar(DataGrid g, Func<object, IEnumerable<(string, string?)>> chaves, string o_que)
    {
        void Ir(IEnumerable<object> linhas)
        {
            var x = linhas.SelectMany(chaves).Distinct().ToList();
            if (x.Count == 0) return;
            Selecao = x;
            DialogResult = true;
        }
        var b = new Button
        {
            Content = "Selecionar no modelo", Style = S("Secondary"), MinWidth = 0, Padding = new Thickness(10, 3, 10, 3),
            ToolTip = $"Fecha a janela e seleciona {o_que} das linhas escolhidas (nenhuma escolhida = todas), aproximando a vista.",
        };
        b.Click += (_, _) => Ir(g.SelectedItems.Count > 0 ? g.SelectedItems.Cast<object>() : g.Items.Cast<object>());
        g.MouseDoubleClick += (_, _) => { if (g.SelectedItem is { } r) Ir(new[] { r }); };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(b);
        return sp;
    }
}
