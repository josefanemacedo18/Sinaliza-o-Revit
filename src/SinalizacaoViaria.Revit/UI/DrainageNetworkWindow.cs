using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Tela da rede de drenagem (Etapa 11; Prompt de Interface §16): o resumo e uma aba para cada parte – consistência geométrica
/// (com os trechos medidos), conectividade, verificação de parâmetros, cálculo hidráulico (com a planilha e as captações),
/// dimensionamento (diâmetro do modelo × calculado) e drenagem × vias e terreno – mais o perfil longitudinal por ramo e os
/// dispositivos. Cada problema traz o elemento, a explicação e o que fazer; "Selecionar no modelo" fecha a janela, seleciona e
/// aproxima a vista. O conteúdo vem de <see cref="RedeDrenagemTela"/>; a janela não grava nada (a escolha volta ao comando).
/// </summary>
public sealed class DrainageNetworkWindow : Window
{
    public enum Acao { Nenhuma, Selecionar, AplicarDiametros, ProjetoDrenagem }

    public sealed record LinhaProblema(string Nivel, string Elemento, string Problema, string Correcao, IReadOnlyList<string> Ids);
    public sealed record LinhaTrecho(string Trecho, string Montante, string Jusante, string Tipo, string Extensao, string FundoMontante, string FundoJusante,
        string Declividade, string Diametro, string Recobrimento, IReadOnlyList<string> Ids);
    public sealed record LinhaHidraulica(string Trecho, string Area, string Coeficiente, string Intensidade, string Vazao, string Diametro, string Capacidade,
        string Velocidade, string Ocupacao, IReadOnlyList<string> Ids);
    public sealed record LinhaCaptacao(string Captacao, string Vazao, string Sarjeta, string Lamina, string Velocidade, string Engolimento, IReadOnlyList<string> Ids);
    public sealed record LinhaDiametro(string Trecho, string Percurso, string Vazao, string Declividade, string Modelo, string Calculado, string Situacao,
        IReadOnlyList<string> Ids);
    public sealed record LinhaDispositivo(string Rotulo, string Tipo, string Tampa, string Fundo, string Profundidade, string Entradas, string Saidas,
        IReadOnlyList<string> Ids);
    public sealed record LinhaPerfil(string Dispositivo, string Distancia, string Tampa, string FundoChegada, string FundoSaida, string Trecho, string Extensao,
        string Declividade, string Diametro, string Recobrimento, string Nivel);
    public sealed record LinhaTubo(string Trecho, string Tubo, string Extensao, string ExtensaoTubo, string Pecas, string Juntas, string Largura, string Profundidade,
        string Escavacao, string Reaterro, IReadOnlyList<string> Ids);

    private readonly TelaRedeDrenagem _tela;
    private readonly string _texto;
    private readonly TabControl _tabs = new();
    private readonly Canvas _perfil = new() { Height = 240, ClipToBounds = true, Background = UiTokens.Surface };
    private readonly ComboBox _ramos = new() { MinWidth = 120 };
    private readonly DataGrid _perfilGrid;

    /// <summary>O que o usuário escolheu (o comando executa depois que a janela fecha).</summary>
    public Acao Escolha { get; private set; }
    /// <summary>Marcas a selecionar (Escolha = Selecionar).</summary>
    public IReadOnlyList<string> Ids { get; private set; } = Array.Empty<string>();

    /// <param name="relatorio">CSV gravado (nulo = não gravado).</param>
    public DrainageNetworkWindow(TelaRedeDrenagem tela, string? relatorio, DateTime quando)
    {
        _tela = tela;
        _texto = RedeDrenagemTela.Texto(tela, quando);
        Title = "Rede de drenagem – SinalizaBIM";
        Width = 1180;
        Height = 800;
        MinWidth = 820;
        MinHeight = 520;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;

        var root = new DockPanel { Margin = new Thickness(UiTokens.SpaceL) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(new TextBlock { Text = "Rede de drenagem", Style = S("Title") });
        head.Children.Add(new TextBlock { Text = tela.Resumo, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = "Conferência geométrica, de conectividade e de parâmetros não é dimensionamento hidráulico: as vazões e os diâmetros só saem com a equação de chuva " +
                   "do Projeto de Drenagem (partes 4 e 5).",
        });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        // ---------------------------------------------------------------- rodapé
        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var projeto = new Button { Content = "Projeto de Drenagem…", Style = S("Secondary"), ToolTip = "Fecha esta janela e abre a equação de chuva e os critérios." };
        projeto.Click += (_, _) => Fechar(Acao.ProjetoDrenagem);
        botoes.Children.Add(projeto);
        if (relatorio != null)
        {
            var abrir = new Button { Content = "Abrir o relatório (CSV)", Style = S("Secondary"), ToolTip = relatorio };
            abrir.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(relatorio) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Error("Rede de drenagem – abrir o relatório", ex); UiHelpers.Error("Não foi possível abrir o relatório: " + ex.Message); }
            };
            botoes.Children.Add(abrir);
        }
        var copiar = new Button { Content = "Copiar resumo", Style = S("Secondary"), ToolTip = "Copia o resumo, as partes e os problemas como texto." };
        copiar.Click += (_, _) =>
        {
            try { Clipboard.SetText(_texto); copiar.Content = "Copiado ✓"; }
            catch { copiar.Content = "Não foi possível copiar"; }
        };
        botoes.Children.Add(copiar);
        botoes.Children.Add(new Button { Content = "Fechar", IsCancel = true, Style = S("Primary") });
        DockPanel.SetDock(botoes, Dock.Right);
        foot.Children.Add(botoes);
        foot.Children.Add(new TextBlock
        {
            Style = S("Hint"), VerticalAlignment = VerticalAlignment.Center,
            Text = relatorio != null ? $"Relatório: {relatorio}" : "O relatório não pôde ser gravado (ver o log).",
        });
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        // ---------------------------------------------------------------- abas
        _tabs.Items.Add(new TabItem { Header = "Resumo", Content = UiHelpers.Scroller(Resumo()) });
        foreach (var s in tela.Secoes)
        {
            var n = s.Itens.Count;
            _tabs.Items.Add(new TabItem { Header = s.Titulo + (n > 0 ? $" ({n})" : ""), Content = UiHelpers.Scroller(Secao(s)) });
        }
        _perfilGrid = Tabela(Array.Empty<LinhaPerfil>(), 220, false,
            ("Dispositivo", nameof(LinhaPerfil.Dispositivo), 1.0), ("Distância (m)", nameof(LinhaPerfil.Distancia), 0.8), ("Tampa (m)", nameof(LinhaPerfil.Tampa), 0.8),
            ("Fundo chegada (m)", nameof(LinhaPerfil.FundoChegada), 0.9), ("Fundo saída (m)", nameof(LinhaPerfil.FundoSaida), 0.9), ("Trecho", nameof(LinhaPerfil.Trecho), 0.6),
            ("Extensão (m)", nameof(LinhaPerfil.Extensao), 0.8), ("Declividade (%)", nameof(LinhaPerfil.Declividade), 0.8), ("Ø (mm)", nameof(LinhaPerfil.Diametro), 0.6),
            ("Recobrimento (m)", nameof(LinhaPerfil.Recobrimento), 0.9), ("Nível d'água (m)", nameof(LinhaPerfil.Nivel), 0.9));
        _tabs.Items.Add(new TabItem { Header = "Perfil", Content = UiHelpers.Scroller(Perfil()) });
        _tabs.Items.Add(new TabItem { Header = "Tubos e valas", Content = UiHelpers.Scroller(Tubos()) });
        _tabs.Items.Add(new TabItem { Header = "Dispositivos", Content = UiHelpers.Scroller(Dispositivos()) });
        root.Children.Add(_tabs);
        Content = root;
    }

    private Style? S(string key) => TryFindResource(key) as Style;

    private void Fechar(Acao a, IReadOnlyList<string>? ids = null)
    {
        Escolha = a;
        Ids = ids ?? Array.Empty<string>();
        DialogResult = true;
    }

    private static string F(double? v, string fmt = "0.00") => v is { } x && double.IsFinite(x) ? UiHelpers.F(x, fmt) : "—";

    // ---------------------------------------------------------------- resumo
    private StackPanel Resumo()
    {
        var sp = new StackPanel();
        foreach (var s in _tela.Secoes)
        {
            var card = new StackPanel();
            var topo = new DockPanel();
            var ver = new Button { Content = "Abrir", Style = S("Secondary"), MinWidth = 0, Padding = new Thickness(10, 2, 10, 2), ToolTip = $"Abre a aba {s.Titulo}." };
            var aba = _tela.Secoes.ToList().IndexOf(s) + 1;
            ver.Click += (_, _) => _tabs.SelectedIndex = aba;
            DockPanel.SetDock(ver, Dock.Right);
            topo.Children.Add(ver);
            topo.Children.Add(new TextBlock { Text = s.Titulo, Style = S("Subtitle"), Margin = new Thickness(0) });
            card.Children.Add(topo);
            card.Children.Add(ContextPanelPage.Linha(s.Situacao.Nivel, s.Situacao.Texto));
            sp.Children.Add(new Border { Style = S("Card"), Margin = new Thickness(0, 0, 0, 8), Child = card });
        }
        return sp;
    }

    // ---------------------------------------------------------------- uma parte
    private StackPanel Secao(SecaoRede s)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = s.Titulo, Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(new TextBlock { Text = s.Escopo, Style = S("Hint") });
        sp.Children.Add(ContextPanelPage.Linha(s.Situacao.Nivel, s.Situacao.Texto));
        foreach (var n in s.Notas) sp.Children.Add(ContextPanelPage.Linha(n.Nivel, n.Texto));

        if (s.Categoria == CategoriaRede.Dimensionamento)
        {
            Dimensionamento(sp);
            return sp;
        }
        if (s.Itens.Count > 0)
        {
            sp.Children.Add(new TextBlock { Text = "Problemas", Style = S("SectionTitle") });
            var linhas = s.Itens.Select(i => new LinhaProblema(CentralDoProjeto.Nome(i.Nivel), i.Elemento, i.Texto, i.Correcao, i.Ids)).ToList();
            var g = Tabela(linhas, 260, true, ("Nível", nameof(LinhaProblema.Nivel), 0.5), ("Elemento", nameof(LinhaProblema.Elemento), 0.9),
                ("Problema", nameof(LinhaProblema.Problema), 2.6), ("O que fazer", nameof(LinhaProblema.Correcao), 2.0));
            sp.Children.Add(g);
            sp.Children.Add(Selecionar(g, r => ((LinhaProblema)r).Ids));
        }
        if (s.Categoria == CategoriaRede.Geometria && _tela.Trechos.Count > 0)
        {
            sp.Children.Add(new TextBlock { Text = "Trechos medidos", Style = S("SectionTitle") });
            var linhas = _tela.Trechos.Select(t => new LinhaTrecho(t.Nome, t.Montante, t.Jusante, t.Ligacao ? "ligação" : "galeria", F(t.Comprimento), F(t.FundoMontante, "0.000"),
                F(t.FundoJusante, "0.000"), F(t.Declividade, "0.000"), F(t.Diametro * 1000, "0"), F(t.CobrimentoMinimo), new[] { t.Id })).ToList();
            var g = Tabela(linhas, 280, true, ("Trecho", nameof(LinhaTrecho.Trecho), 0.5), ("Montante", nameof(LinhaTrecho.Montante), 0.7),
                ("Jusante", nameof(LinhaTrecho.Jusante), 0.7), ("Tipo", nameof(LinhaTrecho.Tipo), 0.6), ("Extensão (m)", nameof(LinhaTrecho.Extensao), 0.7),
                ("Fundo mont. (m)", nameof(LinhaTrecho.FundoMontante), 0.8), ("Fundo jus. (m)", nameof(LinhaTrecho.FundoJusante), 0.8),
                ("Declividade (%)", nameof(LinhaTrecho.Declividade), 0.8), ("Ø (mm)", nameof(LinhaTrecho.Diametro), 0.5), ("Menor recobrimento (m)", nameof(LinhaTrecho.Recobrimento), 1.0));
            sp.Children.Add(g);
            sp.Children.Add(Selecionar(g, r => ((LinhaTrecho)r).Ids));
        }
        if (s.Categoria == CategoriaRede.Hidraulica && _tela.Hidraulica.Trechos.Count > 0)
        {
            sp.Children.Add(new TextBlock { Text = "Planilha das galerias (diâmetros do modelo)", Style = S("SectionTitle") });
            var linhas = _tela.Hidraulica.Trechos.Select(t => new LinhaHidraulica($"{t.Nome} ({t.Montante} → {t.Jusante})", F(t.AreaHa, "0.0000"), F(t.Coeficiente),
                F(t.Intensidade, "0.0"), F(t.Vazao, "0.0"), F(t.Diametro * 1000, "0"), F(t.VazaoMaxima, "0.0"), F(t.Velocidade),
                double.IsFinite(t.Ocupacao) ? F(t.Ocupacao * 100, "0") : "—", new[] { t.Id })).ToList();
            var g = Tabela(linhas, 280, true, ("Trecho", nameof(LinhaHidraulica.Trecho), 1.3), ("Área acumulada (ha)", nameof(LinhaHidraulica.Area), 0.9),
                ("C", nameof(LinhaHidraulica.Coeficiente), 0.4), ("i (mm/h)", nameof(LinhaHidraulica.Intensidade), 0.6), ("Vazão (L/s)", nameof(LinhaHidraulica.Vazao), 0.7),
                ("Ø (mm)", nameof(LinhaHidraulica.Diametro), 0.5), ("Capacidade plena (L/s)", nameof(LinhaHidraulica.Capacidade), 0.9),
                ("Velocidade (m/s)", nameof(LinhaHidraulica.Velocidade), 0.7), ("Ocupação (%)", nameof(LinhaHidraulica.Ocupacao), 0.6));
            sp.Children.Add(g);
            sp.Children.Add(Selecionar(g, r => ((LinhaHidraulica)r).Ids));
        }
        if (s.Categoria == CategoriaRede.Hidraulica && _tela.Hidraulica.Captacoes.Count > 0)
        {
            sp.Children.Add(new TextBlock { Text = "Captações (área local)", Style = S("SectionTitle") });
            var linhas = _tela.Hidraulica.Captacoes.Select(c => new LinhaCaptacao(c.Nome, F(c.Vazao, "0.0"), F(c.CapacidadeSarjeta, "0.0"),
                c.Lamina is { } y ? F(y * 100, "0.0") : "—", F(c.VelocidadeSarjeta), F(c.Engolimento, "0.0"), new[] { c.Id })).ToList();
            var g = Tabela(linhas, 220, true, ("Captação", nameof(LinhaCaptacao.Captacao), 0.8), ("Vazão (L/s)", nameof(LinhaCaptacao.Vazao), 0.7),
                ("Capacidade da sarjeta (L/s)", nameof(LinhaCaptacao.Sarjeta), 1.0), ("Lâmina (cm)", nameof(LinhaCaptacao.Lamina), 0.6),
                ("Velocidade na sarjeta (m/s)", nameof(LinhaCaptacao.Velocidade), 1.0), ("Engolimento (L/s)", nameof(LinhaCaptacao.Engolimento), 0.8));
            sp.Children.Add(g);
            sp.Children.Add(Selecionar(g, r => ((LinhaCaptacao)r).Ids));
        }
        return sp;
    }

    private void Dimensionamento(StackPanel sp)
    {
        if (_tela.Dimensionamento.Count == 0) return;
        sp.Children.Add(new TextBlock { Text = "Diâmetro do modelo × calculado", Style = S("SectionTitle") });
        var linhas = _tela.Dimensionamento.Select(l => new LinhaDiametro(l.Trecho, $"{l.Montante} → {l.Jusante}", F(l.Vazao, "0.0"), F(l.Declividade, "0.000"),
            F(l.DiametroModelo * 1000, "0"), l.DiametroCalculado is { } d ? F(d * 1000, "0") : "—", RedeDrenagemTela.Nome(l.Situacao), new[] { l.Id })).ToList();
        var g = Tabela(linhas, 300, true, ("Trecho", nameof(LinhaDiametro.Trecho), 0.5), ("Percurso", nameof(LinhaDiametro.Percurso), 1.0),
            ("Vazão (L/s)", nameof(LinhaDiametro.Vazao), 0.7), ("Declividade (%)", nameof(LinhaDiametro.Declividade), 0.8), ("Ø modelo (mm)", nameof(LinhaDiametro.Modelo), 0.7),
            ("Ø calculado (mm)", nameof(LinhaDiametro.Calculado), 0.8), ("Situação", nameof(LinhaDiametro.Situacao), 1.6));
        sp.Children.Add(g);
        var acoes = Selecionar(g, r => ((LinhaDiametro)r).Ids);
        var sobe = _tela.Dimensionamento.Count(l => l.Situacao == SituacaoDiametro.Aumentar);
        var desce = _tela.Dimensionamento.Count(l => l.Situacao == SituacaoDiametro.Reduzir);
        var aplicar = new Button
        {
            Content = $"Aplicar os diâmetros calculados ({_tela.Mudam} trecho(s))", Style = S("Primary"), IsEnabled = _tela.Mudam > 0, Margin = new Thickness(6, 0, 0, 0),
            ToolTip = _tela.Mudam > 0
                ? $"Grava o diâmetro calculado em {_tela.Mudam} trecho(s) – aumenta {sobe}, reduz {desce} – e redesenha os tubos numa operação (Desfazer do Revit)."
                : "Os diâmetros do modelo já são os calculados.",
        };
        aplicar.Click += (_, _) =>
        {
            var r = MessageBox.Show(this, $"Gravar o diâmetro calculado em {_tela.Mudam} trecho(s)?\n\nAumenta {sobe} e reduz {desce}. Os tubos são redesenhados numa " +
                                         "operação só (Desfazer do Revit). Depois, rode o Relatório da Rede de novo para conferir recobrimento, velocidades e o alinhamento nos PVs.",
                "SinalizaBIM", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) Fechar(Acao.AplicarDiametros);
        };
        acoes.Children.Add(aplicar);
        sp.Children.Add(acoes);
    }

    // ---------------------------------------------------------------- perfil
    private StackPanel Perfil()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Perfil longitudinal por ramo", Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = "De montante para jusante; o primeiro ramo é o mais longo até o lançamento e cada ramo seguinte para no dispositivo em que encontra um ramo já listado. " +
                   "Linha escura: tampas; linhas azuis: fundo e geratriz superior interna do tubo; tracejado verde: linha piezométrica (nível d'água); pontilhado: " +
                   "linha de energia – com a equação de chuva. Escala vertical exagerada para caber.",
        });
        var ramos = _tela.Perfil.Select(l => l.Ramo).Distinct().ToList();
        if (ramos.Count == 0)
        {
            sp.Children.Add(ContextPanelPage.Linha(NivelSituacao.NaoVerificado, "Nenhum trecho medido: sem perfil."));
            return sp;
        }
        var sel = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
        sel.Children.Add(new TextBlock { Text = "Ramo", Style = S("FieldLabel"), VerticalAlignment = VerticalAlignment.Center });
        foreach (var r in ramos)
        {
            var linhas = _tela.Perfil.Where(l => l.Ramo == r).ToList();
            _ramos.Items.Add(new ComboBoxItem { Content = $"Ramo {r}: {linhas[0].Dispositivo} → {linhas[^1].Dispositivo}", Tag = r });
        }
        _ramos.SelectionChanged += (_, _) => Ramo();
        sel.Children.Add(_ramos);
        sp.Children.Add(sel);
        sp.Children.Add(new Border { BorderBrush = UiTokens.Border, BorderThickness = new Thickness(1), Child = _perfil });
        sp.Children.Add(_perfilGrid);
        _perfil.SizeChanged += (_, _) => Desenhar();
        _ramos.SelectedIndex = 0;
        return sp;
    }

    private List<LinhaPerfilRede> LinhasDoRamo() =>
        _ramos.SelectedItem is ComboBoxItem { Tag: int r } ? _tela.Perfil.Where(l => l.Ramo == r).ToList() : new List<LinhaPerfilRede>();

    private void Ramo()
    {
        var niveis = _tela.Piezometrica.Dispositivos.GroupBy(d => d.Rotulo).ToDictionary(g => g.Key, g => g.First());
        _perfilGrid.ItemsSource = LinhasDoRamo().Select(l => new LinhaPerfil(l.Dispositivo, F(l.Distancia), F(l.Tampa, "0.000"), F(l.FundoChegada, "0.000"),
            F(l.FundoSaida, "0.000"), l.Trecho ?? "", F(l.Comprimento), F(l.Declividade, "0.000"), l.Diametro is { } d ? F(d * 1000, "0") : "—", F(l.Recobrimento),
            niveis.TryGetValue(l.Dispositivo, out var nv) ? F(nv.Nivel, "0.000") + (nv.Extravasa ? " (extravasa)" : "") : "—")).ToList();
        Desenhar();
    }

    /// <summary>Tampas, fundo e geratriz superior interna de cada trecho do ramo, com o rótulo de cada dispositivo.</summary>
    private void Desenhar()
    {
        _perfil.Children.Clear();
        var linhas = LinhasDoRamo();
        var (w, h) = (_perfil.ActualWidth, _perfil.ActualHeight);
        if (linhas.Count < 2 || w < 40 || h < 40) return;
        var hgl = _tela.Piezometrica.Trechos.GroupBy(t => t.Nome).ToDictionary(g => g.Key, g => g.First());
        var cotas = linhas.SelectMany(l => new[] { l.Tampa, l.FundoChegada ?? double.NaN, l.FundoSaida ?? double.NaN, (l.FundoSaida ?? double.NaN) + (l.Diametro ?? 0) })
            .Concat(linhas.Where(l => l.Trecho != null && hgl.ContainsKey(l.Trecho)).SelectMany(l => new[] { hgl[l.Trecho!].EnergiaMontante, hgl[l.Trecho!].EnergiaJusante }))
            .Where(double.IsFinite).ToList();
        if (cotas.Count == 0) return;
        var (zmin, zmax) = (cotas.Min(), cotas.Max());
        var dmax = Math.Max(1e-6, linhas[^1].Distancia);
        const double mx = 48, my = 24;
        double X(double d) => mx + (w - 2 * mx) * d / dmax;
        double Y(double z) => h - my - (h - 2 * my) * (z - zmin) / Math.Max(1e-6, zmax - zmin);

        var tampa = new Polyline { Stroke = UiTokens.Text, StrokeThickness = 2 };
        foreach (var l in linhas.Where(l => double.IsFinite(l.Tampa))) tampa.Points.Add(new Point(X(l.Distancia), Y(l.Tampa)));
        _perfil.Children.Add(tampa);
        for (var i = 0; i < linhas.Count - 1; i++)
        {
            var (a, b) = (linhas[i], linhas[i + 1]);
            if (a.FundoSaida is not { } f0 || b.FundoChegada is not { } f1) continue;
            var d = a.Diametro ?? 0;
            _perfil.Children.Add(new Line { X1 = X(a.Distancia), Y1 = Y(f0), X2 = X(b.Distancia), Y2 = Y(f1), Stroke = UiTokens.Accent, StrokeThickness = 2 });
            _perfil.Children.Add(new Line { X1 = X(a.Distancia), Y1 = Y(f0 + d), X2 = X(b.Distancia), Y2 = Y(f1 + d), Stroke = UiTokens.Accent, StrokeThickness = 1 });
            // Linha piezométrica (nível d'água) e de energia do trecho (Etapa 22).
            if (a.Trecho != null && hgl.TryGetValue(a.Trecho, out var hp))
            {
                _perfil.Children.Add(new Line { X1 = X(a.Distancia), Y1 = Y(hp.NivelMontante), X2 = X(b.Distancia), Y2 = Y(hp.NivelJusante), Stroke = UiTokens.Success,
                    StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 5, 3 } });
                _perfil.Children.Add(new Line { X1 = X(a.Distancia), Y1 = Y(hp.EnergiaMontante), X2 = X(b.Distancia), Y2 = Y(hp.EnergiaJusante), Stroke = UiTokens.Warning,
                    StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 1, 3 } });
            }
        }
        foreach (var l in linhas)
        {
            var fundo = new[] { l.FundoChegada, l.FundoSaida }.Where(z => z != null).Select(z => z!.Value).DefaultIfEmpty(l.Tampa).Min();
            if (double.IsFinite(l.Tampa))
                _perfil.Children.Add(new Line { X1 = X(l.Distancia), Y1 = Y(l.Tampa), X2 = X(l.Distancia), Y2 = Y(fundo), Stroke = UiTokens.TextSecondary, StrokeThickness = 1 });
            var t = new TextBlock { Text = l.Dispositivo, FontSize = UiTokens.FontCaption, Foreground = UiTokens.Text };
            Canvas.SetLeft(t, X(l.Distancia) - 16);
            Canvas.SetTop(t, 2);
            _perfil.Children.Add(t);
        }
        var escala = new TextBlock { Text = $"{UiHelpers.F(zmin)} a {UiHelpers.F(zmax)} m · {UiHelpers.F(dmax, "0.0")} m", FontSize = UiTokens.FontCaption, Foreground = UiTokens.TextSecondary };
        Canvas.SetLeft(escala, 4);
        Canvas.SetTop(escala, h - 18);
        _perfil.Children.Add(escala);
    }

    // ---------------------------------------------------------------- tubos e valas
    private StackPanel Tubos()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Tubos, peças e valas por trecho", Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = "Tubo do catálogo do Projeto de Drenagem (ou, sem um com o diâmetro do trecho, o concreto do padrão do plugin). Extensão entre os centros dos " +
                   "dispositivos; peças e juntas pelo tubo entre as paredes das caixas; vala até o fundo externo do tubo mais o berço. Estimativas, como no memorial.",
        });
        if (_tela.Tubos.Count == 0)
        {
            sp.Children.Add(ContextPanelPage.Linha(NivelSituacao.NaoVerificado, "Nenhum trecho medido."));
            return sp;
        }
        var semCatalogo = _tela.Tubos.Count(t => !t.DoCatalogo);
        if (semCatalogo > 0)
            sp.Children.Add(ContextPanelPage.Linha(NivelSituacao.Informacao, $"{semCatalogo} trecho(s) sem tubo do catálogo com o diâmetro deles: concreto do padrão do plugin."));
        var linhas = _tela.Tubos.Select(t => new LinhaTubo($"{t.Nome} ({t.Montante} → {t.Jusante})", t.Tubo.Nome, F(t.Extensao), F(t.ExtensaoTubo),
            t.Pecas.ToString(UiHelpers.PtBr), t.Juntas.ToString(UiHelpers.PtBr), F(t.Vala.Largura), F(t.Vala.ProfundidadeMedia), F(t.Vala.Escavacao, "0.0"),
            F(t.Vala.Reaterro, "0.0"), new[] { t.Id })).ToList();
        var g = Tabela(linhas, 420, true, ("Trecho", nameof(LinhaTubo.Trecho), 1.2), ("Tubo", nameof(LinhaTubo.Tubo), 2.0), ("Extensão (m)", nameof(LinhaTubo.Extensao), 0.7),
            ("Entre caixas (m)", nameof(LinhaTubo.ExtensaoTubo), 0.8), ("Peças", nameof(LinhaTubo.Pecas), 0.5), ("Juntas", nameof(LinhaTubo.Juntas), 0.5),
            ("Vala (m)", nameof(LinhaTubo.Largura), 0.6), ("Prof. média (m)", nameof(LinhaTubo.Profundidade), 0.8), ("Escavação (m³)", nameof(LinhaTubo.Escavacao), 0.8),
            ("Reaterro (m³)", nameof(LinhaTubo.Reaterro), 0.8));
        sp.Children.Add(g);
        sp.Children.Add(Selecionar(g, r => ((LinhaTubo)r).Ids));
        return sp;
    }

    // ---------------------------------------------------------------- dispositivos
    private StackPanel Dispositivos()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Dispositivos", Style = S("SectionTitle"), Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(new TextBlock { Style = S("Hint"), Text = "Os ligados por trechos e as captações soltas, na ordem dos rótulos; fundo = o mais baixo dos trechos que chegam ou saem." });
        var linhas = _tela.Dispositivos.Select(d => new LinhaDispositivo(d.Rotulo, d.Tipo, F(d.Tampa, "0.000"), F(d.Fundo, "0.000"), F(d.Profundidade),
            d.Entradas.ToString(UiHelpers.PtBr), d.Saidas.ToString(UiHelpers.PtBr), new[] { d.Id })).ToList();
        var g = Tabela(linhas, 420, true, ("Dispositivo", nameof(LinhaDispositivo.Rotulo), 0.7), ("Tipo", nameof(LinhaDispositivo.Tipo), 1.4),
            ("Tampa (m)", nameof(LinhaDispositivo.Tampa), 0.7), ("Fundo (m)", nameof(LinhaDispositivo.Fundo), 0.7), ("Profundidade (m)", nameof(LinhaDispositivo.Profundidade), 0.8),
            ("Chegam", nameof(LinhaDispositivo.Entradas), 0.5), ("Saem", nameof(LinhaDispositivo.Saidas), 0.5));
        sp.Children.Add(g);
        sp.Children.Add(Selecionar(g, r => ((LinhaDispositivo)r).Ids));
        return sp;
    }

    // ---------------------------------------------------------------- tabelas
    private static DataGrid Tabela<T>(IReadOnlyList<T> linhas, double altura, bool multipla, params (string Header, string Path, double Peso)[] colunas) =>
        UiTables.Grid(linhas, altura, multipla, colunas);

    /// <summary>Botão "Selecionar no modelo" para as linhas escolhidas (nenhuma escolhida = todas); duplo clique numa linha faz o mesmo.</summary>
    private StackPanel Selecionar(DataGrid g, Func<object, IReadOnlyList<string>> ids)
    {
        IReadOnlyList<string> Escolhidas()
        {
            var linhas = g.SelectedItems.Count > 0 ? g.SelectedItems.Cast<object>() : g.Items.Cast<object>();
            return linhas.SelectMany(ids).Distinct().ToList();
        }
        var b = new Button
        {
            Content = "Selecionar no modelo", Style = S("Secondary"), MinWidth = 0, Padding = new Thickness(10, 3, 10, 3),
            ToolTip = "Fecha a janela, seleciona os elementos das linhas escolhidas (nenhuma escolhida = todas) e aproxima a vista deles.",
        };
        b.Click += (_, _) =>
        {
            var x = Escolhidas();
            if (x.Count > 0) Fechar(Acao.Selecionar, x);
        };
        g.MouseDoubleClick += (_, _) =>
        {
            if (g.SelectedItem is { } r && ids(r) is { Count: > 0 } x) Fechar(Acao.Selecionar, x);
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(b);
        return sp;
    }
}
