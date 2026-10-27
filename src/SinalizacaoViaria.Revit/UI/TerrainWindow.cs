using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Tela do terreno e da topografia (Etapa 11; Prompt de Interface §14), aberta pelo Diagnóstico do Terreno: origem dos dados
/// (os Toposolids), superfície existente e projetada, limites e parâmetros, diagnóstico (V14), qual superfície cada cálculo lê e
/// o que pode precisar de atualização quando o terreno muda. Somente leitura: o comando escolhido (<see cref="Comando"/>) ou a
/// seleção (<see cref="Ids"/>) voltam ao comando, que roda depois que a janela fecha. O conteúdo vem de <see cref="TerrenoTela"/>.
/// </summary>
public sealed class TerrainWindow : Window
{
    public sealed record LinhaToposolid(string Nome, string Pontos, string Area, string Cotas, string Original, string Terraplenada);
    public sealed record LinhaUso(string Calculo, string Superficie, string Detalhe, string Comando, string Cmd);
    public sealed record LinhaAchado(string Nivel, string Mensagem, string Acao, IReadOnlyList<string> Ids);

    private readonly TelaTerreno _tela;

    /// <summary>Comando da faixa escolhido (classe Cmd*); nulo = nenhum.</summary>
    public string? Comando { get; private set; }
    /// <summary>Marcas a selecionar no modelo.</summary>
    public IReadOnlyList<string> Ids { get; private set; } = Array.Empty<string>();

    /// <param name="relatorio">Texto do Diagnóstico do Terreno (o mesmo de antes) para copiar com o resto.</param>
    public TerrainWindow(TelaTerreno tela, string relatorio)
    {
        _tela = tela;
        Title = "Terreno e topografia – SinalizaBIM";
        Width = 1100;
        Height = 760;
        MinWidth = 780;
        MinHeight = 500;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;

        var root = new DockPanel { Margin = new Thickness(UiTokens.SpaceL) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(new TextBlock { Text = "Terreno e topografia", Style = S("Title") });
        head.Children.Add(new TextBlock { Text = tela.Resumo, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = "Natural = o terreno antes da terraplenagem (o original guardado); projetada = o Toposolid como está agora. Cada cálculo lê uma delas – veja " +
                   "«Superfície de cada cálculo».",
        });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copiar = new Button { Content = "Copiar relatório", Style = S("Secondary"), ToolTip = "Copia o diagnóstico, a superfície de cada cálculo e o que muda com o terreno." };
        var texto = relatorio + "\n\n" + TerrenoTela.Texto(tela);
        copiar.Click += (_, _) =>
        {
            try { Clipboard.SetText(texto); copiar.Content = "Copiado ✓"; }
            catch { copiar.Content = "Não foi possível copiar"; }
        };
        botoes.Children.Add(copiar);
        botoes.Children.Add(new Button { Content = "Fechar", IsCancel = true, Style = S("Primary") });
        DockPanel.SetDock(botoes, Dock.Right);
        foot.Children.Add(botoes);
        foot.Children.Add(new TextBlock
        {
            Style = S("Hint"), VerticalAlignment = VerticalAlignment.Center,
            Text = "Somente leitura: nada aqui altera o projeto. O comando escolhido abre depois que esta janela fecha.",
        });
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        var tabs = new TabControl();
        foreach (var s in tela.Secoes) tabs.Items.Add(new TabItem { Header = s.Titulo, Content = UiHelpers.Scroller(Secao(s)) });
        tabs.Items.Add(new TabItem { Header = "Superfície de cada cálculo", Content = UiHelpers.Scroller(Usos()) });
        tabs.Items.Add(new TabItem { Header = "Se o terreno mudar", Content = UiHelpers.Scroller(Impactos()) });
        root.Children.Add(tabs);
        Content = root;
    }

    private Style? S(string key) => TryFindResource(key) as Style;

    private void Abrir(string comando)
    {
        Comando = comando;
        DialogResult = true;
    }

    private Button Botao(string comando, string? texto = null)
    {
        var rotulo = RibbonBuilder.LabelOf(comando) ?? comando;
        var b = new Button { Content = texto ?? rotulo, Style = S("Secondary"), MinWidth = 0, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 4),
            ToolTip = $"Fecha esta janela e abre {rotulo}." };
        b.Click += (_, _) => Abrir(comando);
        return b;
    }

    private StackPanel Secao(SecaoTerreno s)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = s.Titulo, Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        foreach (var l in s.Linhas) sp.Children.Add(ContextPanelPage.Linha(l.Nivel, l.Texto));
        if (s.Titulo == TerrenoTela.Origem && _tela.Toposolids.Count > 0)
        {
            sp.Children.Add(new TextBlock { Text = "Toposolids", Style = S("SectionTitle") });
            static string Cotas(ResumoSuperficie r) => r is { CotaMinima: { } a, CotaMaxima: { } b } ? $"{UiHelpers.F(a)} a {UiHelpers.F(b)}" : "—";
            var linhas = _tela.Toposolids.Select(t => new LinhaToposolid(t.Nome, t.Atual.Pontos.ToString("N0", UiHelpers.PtBr), UiHelpers.F(t.Atual.AreaPlanta, "N1"),
                Cotas(t.Atual), t.Original is { } o ? $"guardado: {o.Pontos.ToString("N0", UiHelpers.PtBr)} ponto(s), cotas {Cotas(o)}" : "não guardado (o atual é o natural)",
                t.AreaTerraplenada > 0 ? UiHelpers.F(t.AreaTerraplenada, "N0") : "—")).ToList();
            sp.Children.Add(UiTables.Grid(linhas, 300, false, ("Toposolid", nameof(LinhaToposolid.Nome), 1.4), ("Pontos", nameof(LinhaToposolid.Pontos), 0.6),
                ("Área (m²)", nameof(LinhaToposolid.Area), 0.7), ("Cotas (m)", nameof(LinhaToposolid.Cotas), 0.9), ("Terreno original", nameof(LinhaToposolid.Original), 1.8),
                ("Terraplenada (m²)", nameof(LinhaToposolid.Terraplenada), 0.8)));
        }
        if (s.Titulo == TerrenoTela.Limites)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            w.Children.Add(Botao("CmdConfiguracoes", "Configurações (declividade máxima)"));
            w.Children.Add(Botao("CmdTerraplenagem"));
            sp.Children.Add(w);
        }
        if (s.Titulo == TerrenoTela.Diagnostico)
        {
            var problemas = _tela.Diagnostico.Where(a => a.Ids.Count > 0).ToList();
            if (problemas.Count > 0)
            {
                sp.Children.Add(new TextBlock { Text = "Achados com elementos", Style = S("SectionTitle") });
                var linhas = problemas.Select(a => new LinhaAchado(ModelValidator.Nome(a.Severidade), a.Mensagem, a.AcaoRecomendada, a.Ids)).ToList();
                var g = UiTables.Grid(linhas, 260, true, ("Nível", nameof(LinhaAchado.Nivel), 0.5), ("Achado", nameof(LinhaAchado.Mensagem), 2.6),
                    ("O que fazer", nameof(LinhaAchado.Acao), 1.6));
                sp.Children.Add(g);
                var sel = new Button
                {
                    Content = "Selecionar no modelo", Style = S("Secondary"), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 0, Padding = new Thickness(10, 3, 10, 3),
                    ToolTip = "Fecha a janela, seleciona os elementos das linhas escolhidas (nenhuma escolhida = todas) e aproxima a vista deles.",
                };
                sel.Click += (_, _) =>
                {
                    var rows = g.SelectedItems.Count > 0 ? g.SelectedItems.Cast<LinhaAchado>() : linhas;
                    Ids = rows.SelectMany(r => r.Ids).Distinct().ToList();
                    if (Ids.Count > 0) DialogResult = true;
                };
                sp.Children.Add(sel);
            }
            var w = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            w.Children.Add(Botao("CmdValidarModelo"));
            sp.Children.Add(w);
        }
        return sp;
    }

    private StackPanel Usos()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Superfície de cada cálculo", Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = "Natural: o original guardado na primeira terraplenagem (sem ele, o Toposolid atual). Projetada: o Toposolid como está. Acabada: as vias do plugin " +
                   "e, fora delas, a projetada. Nenhum cálculo mistura duas superfícies sem dizer.",
        });
        var linhas = _tela.Usos.Select(u => new LinhaUso(u.Calculo, TerrenoTela.Nome(u.Superficie), u.Detalhe, RibbonBuilder.LabelOf(u.Comando) ?? u.Comando, u.Comando)).ToList();
        var g = UiTables.Grid(linhas, 420, false, ("Cálculo", nameof(LinhaUso.Calculo), 2.0), ("Superfície", nameof(LinhaUso.Superficie), 1.1),
            ("Detalhe", nameof(LinhaUso.Detalhe), 1.8), ("Onde", nameof(LinhaUso.Comando), 0.9));
        sp.Children.Add(g);
        var abrir = new Button { Content = "Abrir o comando da linha", Style = S("Secondary"), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 0,
            Padding = new Thickness(10, 3, 10, 3), ToolTip = "Fecha esta janela e abre o comando da linha escolhida (ou dê um duplo clique na linha)." };
        // Pela linha escolhida (o objeto), não pela posição na tabela.
        void Linha()
        {
            if (g.SelectedItem is LinhaUso u) Abrir(u.Cmd);
        }
        abrir.Click += (_, _) => Linha();
        g.MouseDoubleClick += (_, _) => Linha();
        sp.Children.Add(abrir);
        return sp;
    }

    private StackPanel Impactos()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Se o terreno mudar", Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = "O que pode precisar de atualização quando o Toposolid é editado ou um novo levantamento é adotado. Nada é refeito sozinho.",
        });
        foreach (var i in _tela.Impactos)
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = i.Marcas > 0 ? $"{i.Modulo} – {i.Marcas} marca(s)" : i.Modulo, Style = S("Subtitle"), Margin = new Thickness(0) });
            card.Children.Add(new TextBlock { Text = i.Texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
            if (i.Comando is { } c)
            {
                var w = new WrapPanel();
                w.Children.Add(Botao(c));
                card.Children.Add(w);
            }
            sp.Children.Add(new Border { Style = S("Card"), Margin = new Thickness(0, 0, 0, 8), Child = card });
        }
        return sp;
    }
}
