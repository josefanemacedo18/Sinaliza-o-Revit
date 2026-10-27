using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Central de Qualidade (Etapa 12; Prompt de Interface §17), aberta pelo Validar Modelo: o diagnóstico do modelo numa tabela com
/// filtros (categoria, gravidade, estado, texto), ordem, detalhes do achado, seleção no modelo e exportação em CSV; as
/// contagens por categoria; os resolvidos desde a validação anterior; o estado dos resultados derivados; e, numa aba própria,
/// o último Autoteste – testes do plugin, que não são o diagnóstico deste modelo. Só lê: a seleção (<see cref="Ids"/>) volta ao
/// comando. O conteúdo vem de <see cref="CentralQualidade"/> e <see cref="RelatorioAutoteste"/>.
/// </summary>
public sealed class QualityCenterWindow : Window
{
    public sealed record LinhaTabela(string Gravidade, string Categoria, string Codigo, string Elementos, string Descricao, string Estado, string Acao, LinhaQualidade Linha);
    public sealed record LinhaCategoria(string Categoria, string Erros, string Avisos, string Infos, string NaoVerificados);
    public sealed record LinhaResolvido(string Codigo, string Gravidade, string Mensagem);
    public sealed record LinhaEstado(string Modulo, string Resultado, string Situacao, string Detalhe, string Acao);
    public sealed record LinhaEtapa(string Bloco, string Etapa, string Situacao, string Tempo, string Mensagens);

    private readonly ResumoQualidade _r;
    private readonly string? _relatorio;
    private readonly ComboBox _categoria = new() { MinWidth = 160 };
    private readonly ComboBox _gravidade = new() { MinWidth = 130 };
    private readonly ComboBox _estado = new() { MinWidth = 140 };
    private readonly ComboBox _ordem = new() { MinWidth = 120 };
    private readonly TextBox _busca = new() { MinWidth = 180 };
    private readonly TextBlock _contagem = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly StackPanel _detalhe = new();
    private readonly DataGrid _grid;
    private List<LinhaQualidade> _filtradas = new();

    /// <summary>Marcas a selecionar no modelo.</summary>
    public IReadOnlyList<string> Ids { get; private set; } = Array.Empty<string>();

    /// <param name="relatorio">TXT da verificação gravado (nulo = não gravado).</param>
    /// <param name="estados">Estado dos resultados derivados (Etapa 8); nulo = não calculado.</param>
    /// <param name="autoteste">O último Autoteste deste computador; nulo = nenhum.</param>
    public QualityCenterWindow(ResumoQualidade r, string? relatorio, IReadOnlyList<EstadoResultado>? estados, ResumoAutoteste? autoteste)
    {
        _r = r;
        _relatorio = relatorio;
        Title = "Central de Qualidade – SinalizaBIM";
        Width = 1200;
        Height = 800;
        MinWidth = 820;
        MinHeight = 520;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;

        var root = new DockPanel { Margin = new Thickness(UiTokens.SpaceL) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(new TextBlock { Text = "Central de Qualidade", Style = S("Title") });
        head.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold,
            Text = $"{r.Projeto}: {r.Conta(Severidade.Erro)} erro(s), {r.Conta(Severidade.Aviso)} aviso(s), {r.Conta(Severidade.Info)} informação(ões) e " +
                   $"{r.Conta(Severidade.NaoVerificavel)} verificação(ões) não executada(s) em {r.Marcas} marca(s).",
        });
        head.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = $"Verificação de {r.Quando:dd/MM/yyyy HH:mm}, SinalizaBIM {r.Versao ?? "—"}; " +
                   (r.Anterior is { } a ? $"comparada com a de {a:dd/MM/yyyy HH:mm}." : "primeira validação deste projeto neste computador.") +
                   " Os verificadores são os do Validar Modelo; nada foi alterado no projeto.",
        });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (relatorio != null)
        {
            var abrir = new Button { Content = "Abrir o relatório (TXT)", Style = S("Secondary"), ToolTip = relatorio };
            abrir.Click += (_, _) => Abrir(relatorio);
            botoes.Children.Add(abrir);
        }
        botoes.Children.Add(new Button { Content = "Fechar", IsCancel = true, Style = S("Primary") });
        DockPanel.SetDock(botoes, Dock.Right);
        foot.Children.Add(botoes);
        foot.Children.Add(new TextBlock
        {
            Style = S("Hint"), VerticalAlignment = VerticalAlignment.Center,
            Text = "Depois de corrigir, rode o Validar Modelo de novo: a comparação mostra o que é novo e o que foi resolvido.",
        });
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        _grid = UiTables.Grid(Array.Empty<LinhaTabela>(), 420, true, ("Gravidade", nameof(LinhaTabela.Gravidade), 0.6), ("Categoria", nameof(LinhaTabela.Categoria), 0.9),
            ("Código", nameof(LinhaTabela.Codigo), 0.4), ("Elementos", nameof(LinhaTabela.Elementos), 1.0), ("Descrição", nameof(LinhaTabela.Descricao), 2.6),
            ("Estado", nameof(LinhaTabela.Estado), 0.7), ("Ação recomendada", nameof(LinhaTabela.Acao), 1.6));
        _grid.SelectionChanged += (_, _) => Detalhe();
        _grid.MouseDoubleClick += (_, _) => { if (_grid.SelectedItem is LinhaTabela t && t.Linha.Ids.Count > 0) Selecionar(t.Linha.Ids); };

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Diagnóstico do modelo", Content = UiHelpers.Scroller(Diagnostico()) });
        tabs.Items.Add(new TabItem { Header = "Por categoria", Content = UiHelpers.Scroller(PorCategoria()) });
        tabs.Items.Add(new TabItem { Header = $"Resolvidos ({r.Resolvidos.Count})", Content = UiHelpers.Scroller(Resolvidos()) });
        tabs.Items.Add(new TabItem { Header = "Estado dos resultados", Content = UiHelpers.Scroller(Estados(estados)) });
        tabs.Items.Add(new TabItem { Header = "Autoteste (testes do plugin)", Content = UiHelpers.Scroller(Autoteste(autoteste)) });
        root.Children.Add(tabs);
        Content = root;
        Filtrar();
    }

    private Style? S(string key) => TryFindResource(key) as Style;

    private static void Abrir(string arquivo)
    {
        try { Process.Start(new ProcessStartInfo(arquivo) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Central de Qualidade – abrir arquivo", ex); UiHelpers.Error("Não foi possível abrir o arquivo: " + ex.Message); }
    }

    private void Selecionar(IReadOnlyList<string> ids)
    {
        Ids = ids.Distinct().ToList();
        if (Ids.Count > 0) DialogResult = true;
    }

    // ---------------------------------------------------------------- diagnóstico
    private StackPanel Diagnostico()
    {
        var sp = new StackPanel();
        var filtros = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        void Campo(string rotulo, Control c)
        {
            filtros.Children.Add(new TextBlock { Text = rotulo, Style = S("FieldLabel"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            c.Margin = new Thickness(0, 0, 12, 4);
            filtros.Children.Add(c);
        }
        Opcoes(_categoria, new (object?, string)[] { (null, "Todas") }
            .Concat(_r.PorCategoria.Where(c => c.Total > 0).Select(c => ((object?)c.Categoria, c.Categoria))).ToArray());
        Opcoes(_gravidade, (null, "Todas"), (Severidade.Erro, "Erro"), (Severidade.Aviso, "Aviso"), (Severidade.Info, "Informação"),
            (Severidade.NaoVerificavel, "Não verificado"));
        Opcoes(_estado, (null, "Todos"), (EstadoAchado.Novo, "Novo"), (EstadoAchado.Persistente, "Persistente"),
            (EstadoAchado.PrimeiraValidacao, "Primeira validação"), (EstadoAchado.NaoVerificado, "Não verificado"));
        Opcoes(_ordem, (OrdemQualidade.Gravidade, "Gravidade"), (OrdemQualidade.Categoria, "Categoria"), (OrdemQualidade.Codigo, "Código"),
            (OrdemQualidade.Estado, "Estado"));
        Campo("Categoria", _categoria);
        Campo("Gravidade", _gravidade);
        Campo("Estado", _estado);
        Campo("Ordenar por", _ordem);
        Campo("Buscar", _busca);
        filtros.Children.Add(_contagem);
        foreach (var cb in new[] { _categoria, _gravidade, _estado, _ordem }) cb.SelectionChanged += (_, _) => Filtrar();
        _busca.TextChanged += (_, _) => Filtrar();
        sp.Children.Add(filtros);
        sp.Children.Add(_grid);

        var acoes = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var sel = new Button
        {
            Content = "Selecionar no modelo", Style = S("Secondary"), MinWidth = 0, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Fecha a janela, seleciona os elementos das linhas escolhidas (nenhuma escolhida = as do filtro) e aproxima a vista deles.",
        };
        sel.Click += (_, _) =>
        {
            var linhas = _grid.SelectedItems.Count > 0 ? _grid.SelectedItems.Cast<LinhaTabela>().Select(t => t.Linha) : _filtradas;
            Selecionar(linhas.SelectMany(l => l.Ids).ToList());
        };
        acoes.Children.Add(sel);
        var csv = new Button { Content = "Exportar CSV", Style = S("Secondary"), MinWidth = 0, Padding = new Thickness(10, 3, 10, 3), ToolTip = "Grava as linhas do filtro em CSV e abre o arquivo." };
        csv.Click += (_, _) => Exportar();
        acoes.Children.Add(csv);
        sp.Children.Add(acoes);
        sp.Children.Add(new TextBlock { Text = "Detalhes", Style = S("SectionTitle") });
        sp.Children.Add(new Border { Style = S("Card"), Child = _detalhe });
        return sp;
    }

    /// <summary>Opções de um filtro: o valor vai no Tag de cada item (nulo = todos), sem depender da posição na lista.</summary>
    private static void Opcoes(ComboBox cb, params (object? Valor, string Texto)[] opcoes)
    {
        foreach (var (v, t) in opcoes) cb.Items.Add(new ComboBoxItem { Content = t, Tag = v });
        cb.SelectedIndex = 0;
    }

    private static object? Valor(ComboBox cb) => (cb.SelectedItem as ComboBoxItem)?.Tag;

    private FiltroQualidade Filtro() =>
        new(Valor(_categoria) as string, Valor(_gravidade) as Severidade?, Valor(_estado) as EstadoAchado?, _busca.Text);

    private void Filtrar()
    {
        var ordem = Valor(_ordem) is OrdemQualidade o ? o : OrdemQualidade.Gravidade;
        _filtradas = CentralQualidade.Ordenar(CentralQualidade.Filtrar(_r.Linhas, Filtro()), ordem);
        _grid.ItemsSource = _filtradas.Select(l => new LinhaTabela(ModelValidator.Nome(l.Gravidade), l.Categoria, l.Codigo, l.Elementos, l.Descricao,
            CentralQualidade.Nome(l.Estado), l.Acao, l)).ToList();
        _contagem.Text = $"{_filtradas.Count} de {_r.Linhas.Count} linha(s)";
        Detalhe();
    }

    private void Detalhe()
    {
        _detalhe.Children.Clear();
        if (_grid.SelectedItem is not LinhaTabela t)
        {
            _detalhe.Children.Add(new TextBlock { Style = S("Hint"), Text = "Escolha uma linha para ver o achado inteiro, a regra, a confiança e as marcas." });
            return;
        }
        var l = t.Linha;
        TextBlock Linha(string rotulo, string valor) => new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3), Text = $"{rotulo}: {valor}" };
        _detalhe.Children.Add(ContextPanelPage.Linha(l.Gravidade switch
        {
            Severidade.Erro => NivelSituacao.Erro, Severidade.Aviso => NivelSituacao.Aviso, Severidade.Info => NivelSituacao.Informacao, _ => NivelSituacao.NaoVerificado,
        }, l.Descricao));
        _detalhe.Children.Add(Linha("Regra", $"{l.Codigo} – {(ModelValidator.Codigos.TryGetValue(l.Codigo, out var v) ? v : "—")} ({l.Categoria})"));
        _detalhe.Children.Add(Linha("Confiança", CentralQualidade.Nome(l.Confianca) + (l.RevisaoHumana ? "; pede revisão humana – o plugin não corrige sozinho" : "")));
        _detalhe.Children.Add(Linha("Estado", CentralQualidade.Nome(l.Estado)));
        _detalhe.Children.Add(Linha("O que fazer", l.Acao));
        _detalhe.Children.Add(Linha("Elementos", l.Elementos));
    }

    private void Exportar()
    {
        try
        {
            Directory.CreateDirectory(PluginPaths.Root);
            var path = Path.Combine(PluginPaths.Root, $"qualidade-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            File.WriteAllText(path, CentralQualidade.Csv(_filtradas, _r), new UTF8Encoding(true));
            Abrir(path);
        }
        catch (Exception ex)
        {
            Log.Error("Central de Qualidade – exportar", ex);
            UiHelpers.Error("Não foi possível gravar o CSV: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- demais abas
    private StackPanel PorCategoria()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Achados por categoria", Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(new TextBlock { Style = S("Hint"), Text = "Todas as categorias que têm verificador no plugin; zero = conferida sem achado (a que não pôde ser conferida conta em \"não verificadas\"). " +
            "Pavimentação não tem verificador próprio: a mudança da estrutura de uma via que molda o terreno deixa a terraplenagem desatualizada (V15)." });
        var linhas = _r.PorCategoria.Select(c => new LinhaCategoria(c.Categoria, N(c.Erros), N(c.Avisos), N(c.Infos), N(c.NaoVerificados))).ToList();
        sp.Children.Add(UiTables.Grid(linhas, 420, false, ("Categoria", nameof(LinhaCategoria.Categoria), 1.4), ("Erros", nameof(LinhaCategoria.Erros), 0.5),
            ("Avisos", nameof(LinhaCategoria.Avisos), 0.5), ("Informações", nameof(LinhaCategoria.Infos), 0.6), ("Não verificadas", nameof(LinhaCategoria.NaoVerificados), 0.7)));
        return sp;
    }

    private static string N(int n) => n.ToString(UiHelpers.PtBr);

    private StackPanel Resolvidos()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Resolvidos desde a validação anterior", Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        if (_r.Anterior == null)
        {
            sp.Children.Add(ContextPanelPage.Linha(NivelSituacao.NaoVerificado, "Primeira validação deste projeto neste computador: não há com o que comparar."));
            return sp;
        }
        if (_r.Resolvidos.Count == 0)
        {
            sp.Children.Add(ContextPanelPage.Linha(NivelSituacao.Informacao, $"Nenhum achado da validação de {_r.Anterior:dd/MM/yyyy HH:mm} sumiu."));
            return sp;
        }
        var linhas = _r.Resolvidos.Select(x => new LinhaResolvido(x.Codigo, ModelValidator.Nome(x.Severidade), x.Mensagem)).ToList();
        sp.Children.Add(UiTables.Grid(linhas, 420, false, ("Código", nameof(LinhaResolvido.Codigo), 0.4), ("Gravidade", nameof(LinhaResolvido.Gravidade), 0.6),
            ("Achado da validação anterior", nameof(LinhaResolvido.Mensagem), 3.0)));
        return sp;
    }

    private StackPanel Estados(IReadOnlyList<EstadoResultado>? estados)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Estado dos resultados derivados", Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(new TextBlock { Style = S("Hint"), Text = "O que foi lido ou calculado a partir do modelo e se ainda confere com ele (terraplenagem aplicada, captações lidas das vias)." });
        if (estados == null)
        {
            sp.Children.Add(ContextPanelPage.Linha(NivelSituacao.NaoVerificado, "O estado dos resultados não pôde ser calculado (ver o log)."));
            return sp;
        }
        static string Nome(SituacaoResultado s) => s switch
        {
            SituacaoResultado.Atualizado => "atualizado", SituacaoResultado.Desatualizado => "DESATUALIZADO", SituacaoResultado.NaoVerificavel => "não verificado",
            _ => "informação",
        };
        var linhas = estados.Select(e => new LinhaEstado(e.Modulo, e.Resultado, Nome(e.Situacao), e.Detalhe, e.Acao ?? "—")).ToList();
        sp.Children.Add(UiTables.Grid(linhas, 420, false, ("Módulo", nameof(LinhaEstado.Modulo), 0.8), ("Resultado", nameof(LinhaEstado.Resultado), 1.0),
            ("Situação", nameof(LinhaEstado.Situacao), 0.6), ("Detalhe", nameof(LinhaEstado.Detalhe), 2.4), ("O que fazer", nameof(LinhaEstado.Acao), 1.4)));
        return sp;
    }

    private StackPanel Autoteste(ResumoAutoteste? a)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Último Autoteste (testes do plugin)", Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = "O Autoteste confere as ferramentas do plugin numa área de teste e desfaz tudo no fim: são testes do plugin, não o diagnóstico deste modelo.",
        });
        if (a == null)
        {
            sp.Children.Add(ContextPanelPage.Linha(NivelSituacao.NaoVerificado, "Nenhum relatório de Autoteste neste computador (Configurações › Autoteste)."));
            return sp;
        }
        sp.Children.Add(ContextPanelPage.Linha(a.ComErro > 0 ? NivelSituacao.Erro : a.ComAviso > 0 ? NivelSituacao.Aviso : NivelSituacao.Ok,
            $"{a.Etapas.Count} etapa(s) executada(s): {a.Ok} aprovada(s), {a.ComAviso} com aviso, {a.ComErro} com erro" +
            (a.NaoExecutadas is { } n ? $"; {n} não executada(s)." : ".")));
        sp.Children.Add(ContextPanelPage.Linha(NivelSituacao.Informacao,
            $"{(a.Data is { } d ? d.ToString("dd/MM/yyyy HH:mm") : "data não lida")}, duração {a.Duracao ?? "—"}, modo {a.Modo}; plugin {a.Plugin ?? "—"}; " +
            $"{a.Revit ?? "Revit não informado"}; projeto {a.Projeto ?? "—"}."));
        var com = a.Etapas.Where(e => e.Situacao != "OK").ToList();
        if (com.Count > 0)
        {
            sp.Children.Add(new TextBlock { Text = "Etapas com erro ou aviso", Style = S("SectionTitle") });
            var linhas = com.OrderBy(e => e.Situacao == "ERRO" ? 0 : 1).Select(e => new LinhaEtapa(e.Bloco, e.Nome, e.Situacao,
                $"{(e.Ms / 1000.0).ToString("0.0", UiHelpers.PtBr)} s", string.Join(" | ", e.Erros.Concat(e.Avisos).Take(3)) +
                (e.Erros.Count + e.Avisos.Count > 3 ? $" (+{e.Erros.Count + e.Avisos.Count - 3})" : ""))).ToList();
            sp.Children.Add(UiTables.Grid(linhas, 420, false, ("Bloco", nameof(LinhaEtapa.Bloco), 0.8), ("Etapa", nameof(LinhaEtapa.Etapa), 1.6),
                ("Situação", nameof(LinhaEtapa.Situacao), 0.5), ("Tempo", nameof(LinhaEtapa.Tempo), 0.5), ("Mensagens", nameof(LinhaEtapa.Mensagens), 3.0)));
        }
        var abrir = new Button { Content = "Abrir o relatório do Autoteste", Style = S("Secondary"), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = a.Arquivo };
        abrir.Click += (_, _) => Abrir(a.Arquivo);
        sp.Children.Add(abrir);
        return sp;
    }
}
