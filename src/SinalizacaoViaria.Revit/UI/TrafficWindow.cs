using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Janela do Simulador de Tráfego: cenário (demanda, frota, pedestres, semáforos, volumes nas entradas), mapa da rede com
/// níveis de serviço e animação da microssimulação, diagnóstico com recomendações, indicadores, nós, trechos e relatório.
/// </summary>
public sealed class TrafficWindow : Window
{
    private readonly TrafficNetwork _net;
    private readonly string _project;
    private readonly Func<TrafficResult, SimResult?, string>? _draw;
    private TrafficResult? _res;
    private SimResult? _sim;
    private CancellationTokenSource? _cts;

    private readonly TrafficMap _map = new();
    private readonly ComboBox _demand = new();
    private readonly TextBox _growth = new() { Text = "0" };
    private readonly TextBox _heavy = new() { Text = "8" };
    private readonly TextBox _bus = new() { Text = "2" };
    private readonly TextBox _phf = new() { Text = "0,92" };
    private readonly TextBox _peds = new() { Text = "150" };
    private readonly CheckBox _optimize = new() { Content = "Otimizar os ciclos semafóricos (Webster)", IsChecked = true };
    private readonly TextBox _cycle = new() { Text = "90", IsEnabled = false };
    private readonly TextBox _duration = new() { Text = "15" };
    private readonly TextBox _warmup = new() { Text = "3" };
    private readonly TextBox _seed = new() { Text = "7" };
    private readonly Dictionary<int, (TextBox Box, TextBlock Hint)> _zones = new();
    private readonly Button _run = new() { Content = "▶  Analisar e simular" };
    private readonly Button _quick = new() { Content = "Só a análise (rápida)" };
    private readonly Button _cancel = new() { Content = "Parar", IsEnabled = false };
    private readonly ProgressBar _progress = new() { Height = 8, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 8, 0, 2) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)) };

    private readonly ComboBox _colorBy = new() { Width = 190 };
    private readonly CheckBox _showVeh = new() { Content = "Veículos", IsChecked = true, Margin = new Thickness(12, 0, 0, 0) };
    private readonly CheckBox _showLbl = new() { Content = "Rótulos", IsChecked = true, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button _play = new() { Content = "▶", MinWidth = 44, IsEnabled = false };
    private readonly ComboBox _speed = new() { Width = 70, Margin = new Thickness(6, 0, 0, 0) };
    private readonly Slider _time = new() { Minimum = -60, Maximum = 900, Margin = new Thickness(10, 0, 10, 0), IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _timeLbl = new() { Width = 120, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private bool _syncSlider;

    private readonly TabControl _tabs = new();
    private readonly TabItem _tabDiag = new() { Header = "Diagnóstico" };
    private readonly ComboBox _diagFilter = new();
    private readonly ListBox _diagList = new();
    private readonly TextBlock _diagDetail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private readonly Button _diagMap = new() { Content = "Ver no mapa", IsEnabled = false, Margin = new Thickness(0) };
    private readonly Button _diagModel = new() { Content = "Selecionar no modelo", IsEnabled = false };
    private readonly StackPanel _kpis = new();
    private readonly ListBox _nodeList = new();
    private readonly TextBox _nodeDetail = Mono();
    private readonly ListView _linkList = new();
    private readonly TextBox _report = Mono();
    private readonly Button _drawBtn = new() { Content = "Desenhar na vista ativa", IsEnabled = false, Margin = new Thickness(0) };
    private readonly Button _export = new() { Content = "Exportar relatório (TXT + CSV)", IsEnabled = false };
    private readonly Button _copy = new() { Content = "Copiar relatório", IsEnabled = false };

    /// <summary>Marcas a selecionar no modelo quando a janela fecha (botão "Selecionar no modelo").</summary>
    public List<string> SelectIds { get; private set; } = new();
    public Core.Geometry.Vec2? ShowAt { get; private set; }

    public TrafficWindow(TrafficNetwork net, string project, Func<TrafficResult, SimResult?, string>? draw)
    {
        _net = net;
        _project = project;
        _draw = draw;
        Title = "Simulador de Tráfego – SinalizaBIM";
        Width = 1480;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 640;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;
        if (Resources["Primary"] is Style ps) _run.Style = ps;

        var root = new Grid { Margin = new Thickness(10) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var left = BuildOptions();
        root.Children.Add(left);
        var center = BuildCenter();
        Grid.SetColumn(center, 1);
        root.Children.Add(center);
        var right = BuildTabs();
        Grid.SetColumn(right, 2);
        root.Children.Add(right);

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        var hint = new TextBlock
        {
            Text = "A demanda é estimada pela hierarquia das vias: informe as contagens nas entradas para resultados de projeto. Botão direito no mapa = enquadrar.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 700,
        };
        DockPanel.SetDock(hint, Dock.Left);
        bottom.Children.Add(hint);
        var close = new Button { Content = "Fechar", IsCancel = true };
        close.Click += (_, _) => Close();
        foreach (var b in new[] { close, _copy, _export, _drawBtn }) { DockPanel.SetDock(b, Dock.Right); bottom.Children.Add(b); }
        _drawBtn.Margin = new Thickness(6, 0, 0, 0);
        _drawBtn.Visibility = draw == null ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 3);
        root.Children.Add(bottom);
        Content = root;

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { _map.SetData(null, null); Run(false); };
        Closing += (_, _) => { _cts?.Cancel(); _timer.Stop(); };
    }

    private static TextBox Mono() => new()
    {
        IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static TextBlock Head(string s) => new() { Text = s, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x4A, 0x83)), Margin = new Thickness(0, 10, 0, 2) };

    private static Grid Field(string label, FrameworkElement input, string? hint = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 6, 2) };
        if (hint != null) t.ToolTip = hint;
        g.Children.Add(t);
        Grid.SetColumn(input, 1);
        g.Children.Add(input);
        return g;
    }

    // ------------------------------------------------------------------------------------------------ painel de opções
    private FrameworkElement BuildOptions()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        sp.Children.Add(new TextBlock { Text = "Simulador de Tráfego", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x4A, 0x83)) });
        var zones = _net.Nodes.Where(n => n.IsZone).ToList();
        var inter = _net.Nodes.Count(n => !n.IsZone && n.Kind != TipoNo.Continuacao);
        sp.Children.Add(new TextBlock
        {
            Text = $"Rede lida do projeto: {_net.Roads.Count} via(s), {inter} cruzamento(s)/rotatória(s), {zones.Count} entrada(s)/saída(s), {_net.Signs.Count} placa(s), {_net.Crosswalks.Count} faixa(s) de pedestres.",
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)), Margin = new Thickness(0, 2, 0, 4),
        });

        sp.Children.Add(Head("Demanda"));
        foreach (var s in new[] { "Baixa (entrepico) – 40 %", "Média – 70 %", "Hora de pico – 100 %", "Saturada – 140 %" }) _demand.Items.Add(s);
        _demand.SelectedIndex = 2;
        _demand.ToolTip = "Fração dos volumes de pico típicos de cada hierarquia viária.";
        sp.Children.Add(_demand);
        sp.Children.Add(Field("Crescimento (%)", _growth, "Horizonte de projeto: +30 % = demanda daqui a alguns anos."));
        sp.Children.Add(Field("Fator de hora de pico", _phf, "FHP do HCM: pico de 15 minutos dentro da hora (0,85 a 0,98)."));

        sp.Children.Add(Head("Frota e pedestres"));
        sp.Children.Add(Field("Caminhões (%)", _heavy));
        sp.Children.Add(Field("Ônibus (%)", _bus));
        sp.Children.Add(Field("Pedestres/h por travessia", _peds));

        sp.Children.Add(Head("Semáforos"));
        sp.Children.Add(_optimize);
        _optimize.Checked += (_, _) => _cycle.IsEnabled = false;
        _optimize.Unchecked += (_, _) => _cycle.IsEnabled = true;
        sp.Children.Add(Field("Ciclo fixo (s)", _cycle));

        if (zones.Count > 0)
        {
            sp.Children.Add(Head("Volumes nas entradas (veh/h)"));
            sp.Children.Add(new TextBlock { Text = "Em branco = estimado pela hierarquia. Informe as contagens para resultados de projeto.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)) });
            foreach (var z in zones)
            {
                var road = z.RoadIds.Select(_net.Road).FirstOrDefault(r => r != null);
                var box = new TextBox();
                var hint = new TextBlock { FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)) };
                var g = Field($"{z.Label} – {road?.Name ?? ""}", box);
                var wrap = new StackPanel();
                wrap.Children.Add(g);
                wrap.Children.Add(hint);
                sp.Children.Add(wrap);
                _zones[z.Index] = (box, hint);
            }
        }

        sp.Children.Add(Head("Microssimulação"));
        sp.Children.Add(Field("Duração (min)", _duration));
        sp.Children.Add(Field("Aquecimento (min)", _warmup, "Tempo para a rede encher antes de medir."));
        sp.Children.Add(Field("Semente aleatória", _seed, "Mude para ver outra realização das chegadas."));

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _run.Margin = new Thickness(0, 0, 6, 6);
        _quick.Margin = new Thickness(0, 0, 6, 6);
        _cancel.Margin = new Thickness(0, 0, 6, 6);
        buttons.Children.Add(_run);
        buttons.Children.Add(_quick);
        buttons.Children.Add(_cancel);
        sp.Children.Add(buttons);
        sp.Children.Add(_progress);
        sp.Children.Add(_status);
        _run.Click += (_, _) => Run(true);
        _quick.Click += (_, _) => Run(false);
        _cancel.Click += (_, _) => _cts?.Cancel();
        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ------------------------------------------------------------------------------------------------ mapa e animação
    private FrameworkElement BuildCenter()
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "Colorir os trechos por:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        foreach (var s in new[] { "Nível de serviço", "Volume / capacidade", "Velocidade (análise)", "Volume", "Velocidade (simulação)" }) _colorBy.Items.Add(s);
        _colorBy.SelectedIndex = 0;
        _colorBy.SelectionChanged += (_, _) => { _map.Mode = (MapaCor)_colorBy.SelectedIndex; _map.InvalidateVisual(); };
        bar.Children.Add(_colorBy);
        bar.Children.Add(_showVeh);
        bar.Children.Add(_showLbl);
        _showVeh.Checked += (_, _) => { _map.ShowVehicles = true; _map.InvalidateVisual(); };
        _showVeh.Unchecked += (_, _) => { _map.ShowVehicles = false; _map.InvalidateVisual(); };
        _showLbl.Checked += (_, _) => { _map.ShowLabels = true; _map.InvalidateVisual(); };
        _showLbl.Unchecked += (_, _) => { _map.ShowLabels = false; _map.InvalidateVisual(); };
        var fit = new Button { Content = "Enquadrar", Margin = new Thickness(12, 0, 0, 0), MinWidth = 70 };
        fit.Click += (_, _) => { _map.Marker = null; _map.FitAll(); };
        bar.Children.Add(fit);
        g.Children.Add(bar);

        var border = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xD5, 0xDB, 0xE3)), BorderThickness = new Thickness(1), Child = _map };
        Grid.SetRow(border, 1);
        g.Children.Add(border);
        _map.SelectionChanged += OnMapSelection;

        var anim = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var s in new[] { "1×", "2×", "5×", "10×", "20×" }) _speed.Items.Add(s);
        _speed.SelectedIndex = 2;
        _play.Margin = new Thickness(0);
        _play.Click += (_, _) => { if (_timer.IsEnabled) Pause(); else Play(); };
        DockPanel.SetDock(_play, Dock.Left);
        DockPanel.SetDock(_speed, Dock.Left);
        DockPanel.SetDock(_timeLbl, Dock.Right);
        anim.Children.Add(_play);
        anim.Children.Add(_speed);
        anim.Children.Add(_timeLbl);
        anim.Children.Add(_time);
        _time.ValueChanged += (_, _) =>
        {
            if (_syncSlider) return;
            _map.SimTime = _time.Value;
            UpdateTimeLabel();
            _map.InvalidateVisual();
        };
        Grid.SetRow(anim, 2);
        g.Children.Add(anim);
        return g;
    }

    private void Play()
    {
        if (_sim == null || _sim.Frames.Count == 0) return;
        if (_map.SimTime >= _sim.Duration - 1) _map.SimTime = _sim.Frames[0].Time;
        _timer.Start();
        _play.Content = "❚❚";
    }

    private void Pause()
    {
        _timer.Stop();
        _play.Content = "▶";
    }

    private void Tick()
    {
        if (_sim == null) { Pause(); return; }
        var mult = _speed.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 5, 3 => 10, _ => 20 };
        var t = (double.IsNaN(_map.SimTime) ? _sim.Frames[0].Time : _map.SimTime) + _timer.Interval.TotalSeconds * mult;
        var end = _sim.Frames[^1].Time;
        if (t >= end) { t = end; Pause(); }
        _map.SimTime = t;
        _syncSlider = true;
        _time.Value = t;
        _syncSlider = false;
        UpdateTimeLabel();
        _map.InvalidateVisual();
    }

    private void UpdateTimeLabel()
    {
        var t = _map.SimTime;
        if (double.IsNaN(t)) { _timeLbl.Text = ""; return; }
        var ts = TimeSpan.FromSeconds(Math.Abs(t));
        _timeLbl.Text = t < 0 ? $"aquecimento −{ts.TotalSeconds:0} s" : $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00} de {(int)(_sim?.Duration ?? 0) / 60:00}:00";
    }

    // ------------------------------------------------------------------------------------------------ abas
    private FrameworkElement BuildTabs()
    {
        _tabs.Margin = new Thickness(10, 0, 0, 0);

        // Diagnóstico
        var dg = new Grid();
        dg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        dg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var s in new[] { "Todos", "Só críticos", "Críticos e atenção", "Capacidade", "Segurança", "Sinalização", "Legislação", "Pedestres e acessibilidade", "Geometria", "Rede" }) _diagFilter.Items.Add(s);
        _diagFilter.SelectedIndex = 0;
        _diagFilter.SelectionChanged += (_, _) => FillDiagnostics();
        dg.Children.Add(_diagFilter);
        _diagList.Margin = new Thickness(0, 4, 0, 0);
        ScrollViewer.SetHorizontalScrollBarVisibility(_diagList, ScrollBarVisibility.Disabled);
        _diagList.SelectionChanged += (_, _) => ShowDiagnostic();
        _diagList.MouseDoubleClick += (_, _) => ZoomDiagnostic();
        Grid.SetRow(_diagList, 1);
        dg.Children.Add(_diagList);
        var det = new StackPanel();
        det.Children.Add(new ScrollViewer { Content = _diagDetail, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var db = new StackPanel { Orientation = Orientation.Horizontal };
        db.Children.Add(_diagMap);
        db.Children.Add(_diagModel);
        det.Children.Add(db);
        _diagMap.Click += (_, _) => ZoomDiagnostic();
        _diagModel.Click += (_, _) =>
        {
            if (_diagList.SelectedItem is not ListBoxItem { Tag: TrafficDiagnostic d }) return;
            SelectIds = d.SourceIds.ToList();
            ShowAt = d.Location;
            DialogResult = true;
        };
        Grid.SetRow(det, 2);
        dg.Children.Add(det);
        _tabDiag.Content = dg;
        _tabs.Items.Add(_tabDiag);

        // Indicadores
        _tabs.Items.Add(new TabItem { Header = "Indicadores", Content = new ScrollViewer { Content = _kpis, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        // Nós
        var ng = new Grid();
        ng.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        ng.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        ng.Children.Add(_nodeList);
        _nodeDetail.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(_nodeDetail, 1);
        ng.Children.Add(_nodeDetail);
        _nodeList.SelectionChanged += (_, _) =>
        {
            if (_nodeList.SelectedItem is ListBoxItem { Tag: NodeResult nr })
            {
                _nodeDetail.Text = NodeText(nr);
                _map.Select(nr.Node.Index, null);
            }
        };
        _nodeList.MouseDoubleClick += (_, _) => { if (_nodeList.SelectedItem is ListBoxItem { Tag: NodeResult nr }) _map.ZoomTo(nr.Node.Pos, 70); };
        _tabs.Items.Add(new TabItem { Header = "Cruzamentos", Content = ng });

        // Trechos
        var gv = new GridView();
        foreach (var (h, p, w) in new[] { ("Trecho", "Name", 190.0), ("veh/h", "Vol", 50.0), ("cap.", "Cap", 50.0), ("v/c", "X", 42.0), ("km/h", "Speed", 42.0), ("nível", "Los", 38.0) })
            gv.Columns.Add(new GridViewColumn { Header = h, DisplayMemberBinding = new System.Windows.Data.Binding(p), Width = w });
        _linkList.View = gv;
        _linkList.SelectionChanged += (_, _) =>
        {
            if (_linkList.SelectedItem is LinkRow r) _map.Select(null, r.Index);
        };
        _linkList.MouseDoubleClick += (_, _) =>
        {
            if (_linkList.SelectedItem is LinkRow r) { var l = _net.Links[r.Index]; _map.ZoomTo(l.Path.PointAt(l.Length / 2), Math.Max(60, l.Length * 0.7)); }
        };
        _tabs.Items.Add(new TabItem { Header = "Trechos", Content = _linkList });

        // Relatório
        _tabs.Items.Add(new TabItem { Header = "Relatório", Content = _report });

        _drawBtn.Click += (_, _) =>
        {
            if (_res == null || _draw == null) return;
            try { _status.Text = _draw(_res, _sim); }
            catch (Exception ex) { UiHelpers.Error("Não foi possível desenhar na vista: " + ex.Message); }
        };
        _export.Click += (_, _) => Export();
        _copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_report.Text); _copy.Content = "Copiado ✓"; }
            catch { _copy.Content = "Não foi possível copiar"; }
        };
        return _tabs;
    }

    public sealed class LinkRow
    {
        public int Index { get; init; }
        public string Name { get; init; } = "";
        public string Vol { get; init; } = "";
        public string Cap { get; init; } = "";
        public string X { get; init; } = "";
        public string Speed { get; init; } = "";
        public string Los { get; init; } = "";
    }

    // ------------------------------------------------------------------------------------------------ execução
    private TrafficOptions ReadOptions()
    {
        var o = new TrafficOptions
        {
            Demand = (NivelDemanda)Math.Max(0, _demand.SelectedIndex),
            Growth = UiHelpers.Parse(_growth, 0, "Crescimento", -90, 500) / 100,
            HeavyVehicles = UiHelpers.Parse(_heavy, 8, "Caminhões", 0, 60) / 100,
            Buses = UiHelpers.Parse(_bus, 2, "Ônibus", 0, 40) / 100,
            PeakHourFactor = UiHelpers.Parse(_phf, 0.92, "Fator de hora de pico", 0.5, 1.0),
            PedestriansPerHour = UiHelpers.Parse(_peds, 150, "Pedestres", 0, 5000),
            OptimizeSignals = _optimize.IsChecked == true,
            FixedCycle = UiHelpers.Parse(_cycle, 90, "Ciclo fixo", 30, 240),
            SimSeconds = (int)(UiHelpers.Parse(_duration, 15, "Duração", 2, 120) * 60),
            WarmupSeconds = (int)(UiHelpers.Parse(_warmup, 3, "Aquecimento", 1, 30) * 60),
            Seed = (int)UiHelpers.Parse(_seed, 7, "Semente", 0, 1e9),
        };
        foreach (var (node, (box, _)) in _zones)
        {
            var v = UiHelpers.ParseNullable(box, "Volume da entrada", 0, 20000);
            if (v != null) o.ZoneVolumes[node] = v.Value;
        }
        return o;
    }

    private async void Run(bool simulate)
    {
        TrafficOptions opt;
        try { opt = ReadOptions(); }
        catch (FormatException ex) { UiHelpers.Error(ex.Message); return; }
        Pause();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        _status.Text = "Analisando a rede (demanda, alocação, capacidade dos cruzamentos)…";
        _progress.IsIndeterminate = true;
        try
        {
            var res = await Task.Run(() => TrafficAnalysis.Run(_net, opt), token);
            _res = res;
            _sim = null;
            ShowResults();
            if (simulate && !token.IsCancellationRequested)
            {
                _progress.IsIndeterminate = false;
                _progress.Value = 0;
                _status.Text = "Microssimulação em andamento…";
                var prog = new Progress<double>(p => { _progress.Value = p; _status.Text = $"Microssimulação: {p * 100:0}%"; });
                var sim = await Task.Run(() => TrafficSimulation.Run(res, token, prog), token);
                if (!token.IsCancellationRequested)
                {
                    _sim = sim;
                    ShowResults();
                    _map.SimTime = sim.Frames.Count > 0 ? Math.Max(sim.Frames[0].Time, 0) : double.NaN;
                    if (sim.Frames.Count > 0)
                    {
                        _time.Minimum = sim.Frames[0].Time;
                        _time.Maximum = sim.Frames[^1].Time;
                        _time.Value = _map.SimTime;
                        _time.IsEnabled = true;
                        _play.IsEnabled = true;
                        Play();
                    }
                }
            }
            _status.Text = StatusText();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Interrompido.";
        }
        catch (Exception ex)
        {
            _status.Text = "Erro: " + ex.Message;
            Infrastructure.Log.Error("TrafficWindow", ex);
        }
        finally
        {
            _progress.IsIndeterminate = false;
            _progress.Value = _sim != null ? 1 : 0;
            SetBusy(false);
        }
    }

    private string StatusText()
    {
        if (_res == null) return "";
        var s = $"Análise: {_res.TotalDemand:0} veh/h, velocidade média {_res.AvgSpeed:0.0} km/h, {_res.Diagnostics.Count(d => d.Severity == Gravidade.Critico)} ponto(s) crítico(s).";
        if (_sim != null) s += $"\nSimulação: {_sim.Completed} viagens, tempo médio {_sim.MeanTravelTime:0} s, atraso médio {_sim.MeanDelay:0} s.";
        return s;
    }

    private void SetBusy(bool busy)
    {
        _run.IsEnabled = !busy;
        _quick.IsEnabled = !busy;
        _cancel.IsEnabled = busy;
        _drawBtn.IsEnabled = !busy && _res != null;
        _export.IsEnabled = !busy && _res != null;
        _copy.IsEnabled = !busy && _res != null;
    }

    private void ShowResults()
    {
        if (_res == null) return;
        _map.SetData(_res, _sim);
        if (_sim == null) { _map.SimTime = double.NaN; _time.IsEnabled = false; _play.IsEnabled = false; }
        if (_sim != null && _colorBy.SelectedIndex == 0) { /* mantém o nível de serviço */ }
        foreach (var (node, (_, hint)) in _zones)
        {
            var v = _res.OD.Where(x => x.Key.O == node).Sum(x => x.Value);
            hint.Text = _res.Options.ZoneVolumes.ContainsKey(node) ? $"informado: entram {v:0} veh/h" : $"estimado: entram {v:0} veh/h";
        }
        FillDiagnostics();
        FillKpis();
        _nodeList.Items.Clear();
        foreach (var nr in _res.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind != TipoNo.Continuacao).OrderBy(n => n.Node.Label))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border
            {
                Width = 26, Height = 20, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(TrafficMap.LosColor(nr.LOS)), Margin = new Thickness(0, 1, 8, 1),
                Child = new TextBlock { Text = nr.LOS, Foreground = nr.LOS == "C" ? Brushes.Black : Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            row.Children.Add(new TextBlock
            {
                Text = $"{nr.Node.Label} – {TrafficReport.KindLabel(nr.Node.Kind)}, {TrafficReport.ControlLabel(nr.Node.Control)}\n{nr.Volume:0} veh/h, atraso {nr.Delay:0.0} s" + (nr.Cycle > 0 ? $", ciclo {nr.Cycle:0} s" : ""),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _nodeList.Items.Add(new ListBoxItem { Content = row, Tag = nr });
        }
        _linkList.Items.Clear();
        foreach (var lr in _res.Links.Values.OrderBy(x => x.Link.Road.Name).ThenBy(x => x.Link.S0))
        {
            var l = lr.Link;
            _linkList.Items.Add(new LinkRow
            {
                Index = l.Index, Name = $"{l.Name} {_net.Nodes[l.From].Label}→{_net.Nodes[l.To].Label}", Vol = lr.Volume.ToString("0"), Cap = l.Capacity.ToString("0"),
                X = lr.X.ToString("0.00", UiHelpers.PtBr), Speed = lr.Speed.ToString("0"), Los = lr.LOS,
            });
        }
        _report.Text = TrafficReport.Build(_res, _sim, _project);
        _tabDiag.Header = $"Diagnóstico ({_res.Diagnostics.Count})";
    }

    private void FillDiagnostics()
    {
        _diagList.Items.Clear();
        if (_res == null) return;
        var f = _diagFilter.SelectedIndex;
        foreach (var d in _res.Diagnostics)
        {
            var ok = f switch
            {
                1 => d.Severity == Gravidade.Critico,
                2 => d.Severity != Gravidade.Informacao,
                3 => d.Category == "Capacidade",
                4 => d.Category == "Segurança",
                5 => d.Category == "Sinalização",
                6 => d.Category == "Legislação",
                7 => d.Category is "Pedestres" or "Acessibilidade",
                8 => d.Category == "Geometria",
                9 => d.Category == "Rede",
                _ => true,
            };
            if (!ok) continue;
            var color = d.Severity switch { Gravidade.Critico => Color.FromRgb(0xC6, 0x28, 0x28), Gravidade.Atencao => Color.FromRgb(0xE0, 0x7B, 0x00), _ => Color.FromRgb(0x1F, 0x5F, 0xA8) };
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var icon = new Border { Width = 6, Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(2) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = d.Title, TextWrapping = TextWrapping.Wrap, FontWeight = d.Severity == Gravidade.Critico ? FontWeights.SemiBold : FontWeights.Normal });
            txt.Children.Add(new TextBlock { Text = $"{d.Icon} {d.Category}", FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)) });
            row.Children.Add(txt);
            _diagList.Items.Add(new ListBoxItem { Content = row, Tag = d, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        if (_diagList.Items.Count == 0) _diagList.Items.Add(new ListBoxItem { Content = "Nenhum item neste filtro.", IsEnabled = false });
        ShowDiagnostic();
    }

    private void ShowDiagnostic()
    {
        if (_diagList.SelectedItem is not ListBoxItem { Tag: TrafficDiagnostic d })
        {
            _diagDetail.Text = "Selecione um item para ver o detalhe e a recomendação. Duplo clique = ver no mapa.";
            _diagMap.IsEnabled = false;
            _diagModel.IsEnabled = false;
            return;
        }
        _diagDetail.Inlines.Clear();
        _diagDetail.Inlines.Add(new System.Windows.Documents.Run(d.Title + "\n") { FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(d.Detail)) _diagDetail.Inlines.Add(new System.Windows.Documents.Run(d.Detail + "\n"));
        if (!string.IsNullOrWhiteSpace(d.Recommendation))
            _diagDetail.Inlines.Add(new System.Windows.Documents.Run("\nO que fazer: " + d.Recommendation) { Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x4A, 0x83)) });
        _diagMap.IsEnabled = d.Location != null || d.Node != null || d.Link != null;
        _diagModel.IsEnabled = d.SourceIds.Count > 0;
        if (d.Node is { } n) _map.Select(n, d.Link);
        else if (d.Link is { } l) _map.Select(null, l);
    }

    private void ZoomDiagnostic()
    {
        if (_diagList.SelectedItem is not ListBoxItem { Tag: TrafficDiagnostic d }) return;
        var p = d.Location ?? (d.Node is { } n ? _net.Nodes[n].Pos : d.Link is { } l ? _net.Links[l].Path.PointAt(_net.Links[l].Length / 2) : (Core.Geometry.Vec2?)null);
        if (p != null) _map.ZoomTo(p.Value, 70);
    }

    private void OnMapSelection(int? node, int? link)
    {
        if (_res == null) return;
        if (node is { } n && _res.Nodes.TryGetValue(n, out var nr) && !nr.Node.IsZone)
        {
            _tabs.SelectedIndex = 2;
            foreach (ListBoxItem it in _nodeList.Items)
                if (it.Tag == nr) { _nodeList.SelectedItem = it; _nodeList.ScrollIntoView(it); }
        }
        else if (link is { } l)
        {
            _tabs.SelectedIndex = 3;
            foreach (var it in _linkList.Items)
                if (it is LinkRow r && r.Index == l) { _linkList.SelectedItem = it; _linkList.ScrollIntoView(it); }
        }
    }

    private void FillKpis()
    {
        _kpis.Children.Clear();
        if (_res == null) return;
        var r = _res;
        void Card(string title, string value, string sub, Color? accent = null)
        {
            var b = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD5, 0xDB, 0xE3)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFB)), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)) });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(accent ?? Color.FromRgb(0x17, 0x4A, 0x83)) });
            if (!string.IsNullOrEmpty(sub)) sp.Children.Add(new TextBlock { Text = sub, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)) });
            b.Child = sp;
            _kpis.Children.Add(b);
        }
        var nodes = r.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind != TipoNo.Continuacao && n.Volume > 0).ToList();
        var worst = nodes.OrderByDescending(n => n.Delay).FirstOrDefault();
        Card("Demanda na hora analisada", $"{r.TotalDemand:0} veh/h", r.Options.DemandLabel + (r.Unserved > 0 ? $" – {r.Unserved:0} veh/h sem caminho" : ""));
        Card("Velocidade média na rede", $"{r.AvgSpeed:0.0} km/h", $"{r.VKT:0} veh·km/h percorridos em {r.VHT:0.0} veh·h/h");
        Card("Atraso total", $"{r.TotalDelayH:0.0} veh·h/h", "Tempo perdido em filas e cruzamentos, somado para todos os veículos.");
        if (worst != null) Card("Cruzamento mais crítico", $"{worst.Node.Label} – nível {worst.LOS}", $"{worst.Delay:0.0} s/veh de atraso médio ({TrafficReport.LosMeaning(worst.LOS)})", TrafficMap.LosColor(worst.LOS));
        var dist = string.Join("   ", new[] { "A", "B", "C", "D", "E", "F" }.Select(l => $"{l}: {nodes.Count(n => n.LOS == l)}"));
        Card("Níveis de serviço dos cruzamentos", $"{nodes.Count} cruzamento(s)", dist);
        Card("Emissões estimadas", $"{r.CO2kg:0} kg CO₂/h", "Pelo consumo médio de combustível conforme a velocidade de cada trecho.");
        if (_sim != null)
        {
            Card("Microssimulação – viagens concluídas", $"{_sim.Completed}", $"em {_sim.Duration / 60:0} min; {_sim.InNetwork} na rede no fim" + (_sim.Backlog > 0 ? $"; {_sim.Backlog} não conseguiram entrar" : ""));
            Card("Tempo médio de viagem", $"{_sim.MeanTravelTime:0} s", $"atraso médio {_sim.MeanDelay:0} s por viagem, {_sim.StopsPerTrip:0.0} parada(s) por viagem");
            Card("Pico de veículos simultâneos", $"{_sim.MaxVehicles}", "");
        }
        var crit = r.Diagnostics.Count(d => d.Severity == Gravidade.Critico);
        var warn = r.Diagnostics.Count(d => d.Severity == Gravidade.Atencao);
        Card("Diagnóstico", $"{crit} crítico(s), {warn} atenção", $"{r.Diagnostics.Count - crit - warn} informação(ões) – veja a aba Diagnóstico.", crit > 0 ? Color.FromRgb(0xC6, 0x28, 0x28) : null);
    }

    private static string NodeText(NodeResult nr)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{nr.Node.Label} – {TrafficReport.KindLabel(nr.Node.Kind)}");
        sb.AppendLine($"Controle: {TrafficReport.ControlLabel(nr.Node.Control)}");
        sb.AppendLine($"Nível {nr.LOS} ({TrafficReport.LosMeaning(nr.LOS)}), atraso médio {nr.Delay:0.0} s/veh, {nr.Volume:0} veh/h");
        if (nr.Phases.Count > 0)
        {
            sb.AppendLine($"Ciclo {nr.Cycle:0} s:");
            foreach (var ph in nr.Phases) sb.AppendLine($"  {ph.Name}: verde {ph.Green:0} s + 4 s de entreverdes");
        }
        sb.AppendLine();
        foreach (var a in nr.Approaches)
        {
            sb.AppendLine(a.Name + (a.Major ? " (principal)" : "") + (a.ProtectedLeft ? " (esquerda protegida)" : ""));
            sb.AppendLine($"  {a.Volume:0} veh/h, capacidade {a.Capacity:0}, v/c {a.X:0.00}, atraso {a.Delay:0.0} s, nível {a.LOS}, fila 95% {a.Queue95:0} m");
            if (a.Movements.Count > 0)
                sb.AppendLine("  " + string.Join(", ", a.Movements.Where(m => m.Value >= 0.5).Select(m => $"{GiroLabel(m.Key)} {m.Value:0}")));
        }
        foreach (var n in nr.Notes) sb.AppendLine("ℹ " + n);
        return sb.ToString();
    }

    private static string GiroLabel(Giro g) => g switch { Core.Traffic.Giro.Direita => "à direita", Core.Traffic.Giro.Frente => "em frente", Core.Traffic.Giro.Esquerda => "à esquerda", _ => "retorno" };

    private void Export()
    {
        if (_res == null) return;
        try
        {
            var dir = Infrastructure.PluginPaths.Root;
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var txt = Path.Combine(dir, $"trafego-{stamp}.txt");
            var csv = Path.Combine(dir, $"trafego-{stamp}.csv");
            File.WriteAllText(txt, _report.Text, new UTF8Encoding(true));
            File.WriteAllText(csv, TrafficReport.Csv(_res, _sim), new UTF8Encoding(true));
            _status.Text = $"Relatório salvo em:\n{txt}\n{csv}";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{txt}\"") { UseShellExecute = true }); }
            catch { /* opcional */ }
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Não foi possível salvar o relatório: " + ex.Message);
        }
    }
}
