using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Ações do Simulador de Tráfego sobre o projeto do Revit (a janela não conhece a API do Revit).</summary>
public interface ITrafficHost
{
    List<TrafficScenario> LoadScenarios();
    void SaveScenarios(List<TrafficScenario> list);
    /// <summary>Desenha o mapa de níveis de serviço na vista ativa.</summary>
    string Draw(TrafficResult res, SimResult? sim);
    /// <summary>Aplica à interseção do projeto (chave do nó) o controle e/ou o plano semafórico.</summary>
    string ApplyToIntersection(string nodeKey, ControleNo? control, SignalPlanDef? plan);
    /// <summary>Grava nível de serviço e resumo nos elementos das interseções, rotatórias e vias.</summary>
    string WriteResults(TrafficResult res, string scenario);
}

/// <summary>
/// Janela do Simulador de Tráfego: cenário (demanda, frota, pedestres, semáforos, volumes nas entradas), mapa da rede com
/// níveis de serviço e animação da microssimulação, diagnóstico com recomendações, indicadores, nós, trechos e relatório.
/// </summary>
public sealed class TrafficWindow : Window
{
    private readonly TrafficNetwork _net;
    private readonly string _project;
    private readonly ITrafficHost? _host;
    private List<TrafficScenario> _scenarios = new();
    private TrafficScenario _current = new() { Name = "Cenário atual" };
    private readonly ComboBox _scenario = new() { Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBox _scName = new();
    private readonly TextBox _scNotes = new() { TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 36 };
    private bool _loadingScenario;
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
    private readonly CheckBox _storedPlans = new() { Content = "Usar os planos gravados nas interseções", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _coordinate = new() { Content = "Coordenar os semáforos (onda verde)", IsChecked = false, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _progSpeed = new() { Text = "0" };
    private readonly TextBox _busFreq = new() { Text = "12" };
    private readonly TextBox _busDwell = new() { Text = "20" };
    private readonly TextBox _k = new() { Text = "0,10" };
    private readonly TextBox _vot = new() { Text = "25" };
    private readonly TextBox _occ = new() { Text = "1,4" };
    private readonly TextBox _fuel = new() { Text = "6,20" };
    private readonly TextBox _co2 = new() { Text = "150" };
    private readonly TextBox _crash = new() { Text = "180000" };
    private readonly TextBox _hours = new() { Text = "750" };
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
    private readonly CheckBox _showSigns = new() { Content = "Sinalização", IsChecked = true, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Ícones da sinalização lida do projeto: PARE, dê a preferência, velocidade máxima, semáforos e bloqueios." };
    private readonly CheckBox _realPlan = new() { Content = "Planta real", IsChecked = true, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Desenha o projeto como ele é (pavimento, calçadas, canteiros, ilhas e toda a pintura gerada pelo plugin). Desmarcado: esquema das faixas pelo eixo." };
    private readonly CheckBox _speedColors = new() { Content = "Veículos pela velocidade", IsChecked = false, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Cor de cada veículo pela velocidade (vermelho parado → verde livre) em vez das cores reais." };
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
    private readonly Canvas _timing = new() { Height = 96, Background = Brushes.White, ClipToBounds = true };
    private readonly ComboBox _ovControl = new() { Width = 170 };
    private readonly TextBox _ovCycle = new() { Width = 60 };
    private readonly TextBox _ovGreens = new() { Width = 150, ToolTip = "Verdes de cada fase, em segundos, na ordem das fases (ex.: 42; 30; 12). Vazio = calculados." };
    private readonly DataGrid _ovTurns = new()
    {
        AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, MaxHeight = 170,
        ToolTip = "Contagem classificada de cada aproximação: total (veh/h, vazio = o da alocação) e percentuais de conversão.",
    };
    private readonly Button _ovApply = new() { Content = "Aplicar ao cenário e recalcular", Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _ovClear = new() { Content = "Limpar ajustes", Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _ovSavePlan = new() { Content = "Gravar plano semafórico no projeto", Margin = new Thickness(0, 4, 6, 0) };
    private readonly Button _ovSetControl = new() { Content = "Aplicar este controle no projeto", Margin = new Thickness(0, 4, 6, 0) };
    private readonly TextBox _compare = Mono();
    private readonly ListView _linkList = new();
    private readonly TextBox _report = Mono();
    private readonly Button _drawBtn = new() { Content = "Desenhar na vista ativa", IsEnabled = false, Margin = new Thickness(0) };
    private readonly Button _export = new() { Content = "Exportar relatório (TXT + CSV)", IsEnabled = false };
    private readonly Button _copy = new() { Content = "Copiar relatório", IsEnabled = false };
    private readonly Button _writeBtn = new() { Content = "Gravar resultados no modelo", IsEnabled = false, Margin = new Thickness(6, 0, 0, 0) };

    /// <summary>Cenário para reabrir a janela com a rede relida do projeto (depois de gravar plano/controle).</summary>
    public TrafficScenario? ReopenWith { get; private set; }
    /// <summary>Cenário carregado ao abrir (reabertura).</summary>
    public TrafficScenario? InitialScenario { get; init; }
    private readonly ListBox _regList = new();
    private readonly ComboBox _regFilter = new();

    /// <summary>Marcas a selecionar no modelo quando a janela fecha (botão "Selecionar no modelo").</summary>
    public List<string> SelectIds { get; private set; } = new();
    public Core.Geometry.Vec2? ShowAt { get; private set; }

    public TrafficWindow(TrafficNetwork net, string project, ITrafficHost? host)
    {
        _net = net;
        _project = project;
        _host = host;
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
        foreach (var b in new[] { close, _copy, _export, _writeBtn, _drawBtn }) { DockPanel.SetDock(b, Dock.Right); bottom.Children.Add(b); }
        _drawBtn.Margin = new Thickness(6, 0, 0, 0);
        _drawBtn.Visibility = host == null ? Visibility.Collapsed : Visibility.Visible;
        _writeBtn.Visibility = host == null ? Visibility.Collapsed : Visibility.Visible;
        _writeBtn.ToolTip = "Grava SV_NivelServico e SV_Trafego (cenário, volume, atraso, controle, ciclo) nos elementos das interseções, rotatórias e vias – para tabelas e filtros do Revit.";
        _writeBtn.Click += (_, _) =>
        {
            if (_res == null || _host == null) return;
            try { _status.Text = _host.WriteResults(_res, _current.Name); }
            catch (Exception ex) { UiHelpers.Error("Não foi possível gravar os resultados: " + ex.Message); }
        };
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 3);
        root.Children.Add(bottom);
        Content = root;

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            _map.SetData(null, null);
            try { _scenarios = _host?.LoadScenarios() ?? new(); }
            catch (Exception ex) { Infrastructure.Log.Error("Cenários", ex); }
            FillScenarioList(InitialScenario?.Name);
            if (InitialScenario != null) LoadScenario(InitialScenario.Clone());
            else if (_scenarios.Count > 0) LoadScenario(_scenarios[0].Clone());
            Run(false);
        };
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

        sp.Children.Add(Head("Cenário"));
        sp.Children.Add(_scenario);
        sp.Children.Add(Field("Nome", _scName));
        sp.Children.Add(new TextBlock { Text = "Notas", Margin = new Thickness(0, 2, 0, 0) });
        sp.Children.Add(_scNotes);
        var scb = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var bSave = new Button { Content = "Salvar", Margin = new Thickness(0, 0, 6, 4), ToolTip = "Salva o cenário (opções, volumes, contagens e ajustes dos cruzamentos) no projeto." };
        var bSaveAs = new Button { Content = "Salvar como novo", Margin = new Thickness(0, 0, 6, 4) };
        var bDel = new Button { Content = "Excluir", Margin = new Thickness(0, 0, 6, 4) };
        scb.Children.Add(bSave);
        scb.Children.Add(bSaveAs);
        scb.Children.Add(bDel);
        sp.Children.Add(scb);
        bSave.Click += (_, _) => SaveScenario(false);
        bSaveAs.Click += (_, _) => SaveScenario(true);
        bDel.Click += (_, _) => DeleteScenario();
        _scenario.SelectionChanged += (_, _) =>
        {
            if (_loadingScenario || _scenario.SelectedItem is not ComboBoxItem { Tag: TrafficScenario sc }) return;
            LoadScenario(sc.Clone());
        };

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
        sp.Children.Add(_storedPlans);
        _storedPlans.ToolTip = "Planos gravados pelo botão \"Gravar plano semafórico no projeto\" (ou pelo projetista) valem na análise; desmarque para otimizar todos.";
        sp.Children.Add(_coordinate);
        _coordinate.ToolTip = "Ciclo comum (o maior da rede) e defasagens para que os pelotões encontrem o verde ao longo dos trechos entre semáforos.";
        sp.Children.Add(Field("Velocidade da onda verde (km/h, 0 = da via)", _progSpeed));

        sp.Children.Add(Head("Transporte coletivo"));
        sp.Children.Add(Field("Ônibus/h que param em cada ponto", _busFreq, "Pontos lidos do projeto: baias (recuos) e placas de ponto/abrigos junto ao meio-fio (parada na faixa)."));
        sp.Children.Add(Field("Embarque por parada (s)", _busDwell));

        sp.Children.Add(Head("Segurança e custos (referência)"));
        sp.Children.Add(Field("Fator K (hora/dia)", _k, "Fração do volume diário que ocorre na hora analisada – converte a hora em VDM para os modelos de acidentes."));
        sp.Children.Add(Field("Valor do tempo (R$/h)", _vot));
        sp.Children.Add(Field("Ocupação (pessoas/veh)", _occ));
        sp.Children.Add(Field("Combustível (R$/L)", _fuel));
        sp.Children.Add(Field("CO₂ (R$/t)", _co2));
        sp.Children.Add(Field("Custo por acidente (R$)", _crash, "Custo médio (danos, atendimento, perda de produção, vítimas) – use o valor de referência do órgão (IPEA/ANTP) atualizado."));
        sp.Children.Add(Field("Horas por ano nesta situação", _hours, "Ex.: 3 h de pico × 250 dias úteis = 750 h."));

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
        bar.Children.Add(_showSigns);
        bar.Children.Add(_speedColors);
        bar.Children.Add(_realPlan);
        _realPlan.Checked += (_, _) => { _map.RealPlan = true; _map.InvalidateVisual(); };
        _realPlan.Unchecked += (_, _) => { _map.RealPlan = false; _map.InvalidateVisual(); };
        _showSigns.Checked += (_, _) => { _map.ShowSigns = true; _map.InvalidateVisual(); };
        _showSigns.Unchecked += (_, _) => { _map.ShowSigns = false; _map.InvalidateVisual(); };
        _speedColors.Checked += (_, _) => { _map.ColorBySpeed = true; _map.InvalidateVisual(); };
        _speedColors.Unchecked += (_, _) => { _map.ColorBySpeed = false; _map.InvalidateVisual(); };
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
        ng.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ng.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ng.Children.Add(_nodeList);
        _nodeDetail.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(_nodeDetail, 1);
        ng.Children.Add(_nodeDetail);
        var tb = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xD5, 0xDB, 0xE3)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 6, 0, 0), Child = _timing };
        Grid.SetRow(tb, 2);
        ng.Children.Add(tb);
        _timing.SizeChanged += (_, _) => DrawTiming();
        var ovPanel = BuildOverridePanel();
        Grid.SetRow(ovPanel, 3);
        ng.Children.Add(ovPanel);
        _nodeList.SelectionChanged += (_, _) =>
        {
            if (_nodeList.SelectedItem is ListBoxItem { Tag: NodeResult nr })
            {
                _nodeDetail.Text = NodeText(nr, _res);
                _map.Select(nr.Node.Index, null);
                FillOverride(nr);
                DrawTiming();
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

        // Sinalização interpretada
        var rg = new DockPanel();
        foreach (var f in new[] { "Todas", "Aplicadas", "Não associadas", "Placas", "Marcas no pavimento", "Dispositivos e semáforos" }) _regFilter.Items.Add(f);
        _regFilter.SelectedIndex = 0;
        _regFilter.SelectionChanged += (_, _) => FillRegulations();
        DockPanel.SetDock(_regFilter, Dock.Top);
        rg.Children.Add(_regFilter);
        var regHint = new TextBlock
        {
            Text = "Tudo o que a sinalização do projeto significa para o tráfego: placas R-1/R-2/R-3/R-4/R-5/R-6/R-7/R-8/R-9/R-10/R-19/R-25/R-26/R-32, " +
                   "linhas (LFO, LMS, LRE, LDP, LRV), legendas, setas por faixa, zebrados e canalizações, barreiras, balizadores, cones e semáforos. Duplo clique = ver no mapa.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)), Margin = new Thickness(0, 4, 0, 4),
        };
        DockPanel.SetDock(regHint, Dock.Top);
        rg.Children.Add(regHint);
        var reload = new Button { Content = "Reler o projeto (depois de mudar a sinalização)", Margin = new Thickness(0, 4, 0, 0) };
        reload.Click += (_, _) => Reload();
        reload.Visibility = _host == null ? Visibility.Collapsed : Visibility.Visible;
        DockPanel.SetDock(reload, Dock.Bottom);
        rg.Children.Add(reload);
        ScrollViewer.SetHorizontalScrollBarVisibility(_regList, ScrollBarVisibility.Disabled);
        _regList.MouseDoubleClick += (_, _) =>
        {
            if (_regList.SelectedItem is ListBoxItem { Tag: TrafficRegulation r })
            {
                if (r.Node is { } n) _map.Select(n, r.Link);
                else if (r.Link is { } l) _map.Select(null, l);
                _map.ZoomTo(r.Position, 60);
            }
        };
        rg.Children.Add(_regList);
        _tabs.Items.Add(new TabItem { Header = $"Sinalização ({_net.Regulations.Count(x => x.Applied)})", Content = rg });

        // Cenários: comparação
        var cg = new DockPanel();
        var cbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var bCmp = new Button { Content = "Comparar os cenários salvos", Margin = new Thickness(0, 0, 6, 0) };
        var bPresets = new Button { Content = "Criar cenários de exemplo" };
        bPresets.ToolTip = "Pico atual, horizonte de 10 anos, entrepico e teste de estresse – copie o que precisar e ajuste.";
        cbar.Children.Add(bCmp);
        cbar.Children.Add(bPresets);
        DockPanel.SetDock(cbar, Dock.Top);
        cg.Children.Add(cbar);
        var cHint = new TextBlock
        {
            Text = "Roda a análise de cada cenário salvo (e do atual) sobre a mesma rede e mostra os indicadores lado a lado. " +
                   "Use para decidir entre alternativas: rotatória × semáforo, com ou sem baia, horizonte de projeto.",
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)), Margin = new Thickness(0, 0, 0, 6),
        };
        DockPanel.SetDock(cHint, Dock.Top);
        cg.Children.Add(cHint);
        cg.Children.Add(_compare);
        bCmp.Click += (_, _) => Compare();
        bPresets.Click += (_, _) =>
        {
            foreach (var p in TrafficScenario.Presets().Where(p => _scenarios.All(x => x.Name != p.Name))) _scenarios.Add(p);
            PersistScenarios();
            FillScenarioList(_current.Name);
        };
        _tabs.Items.Add(new TabItem { Header = "Cenários", Content = cg });

        // Relatório
        _tabs.Items.Add(new TabItem { Header = "Relatório", Content = _report });

        _drawBtn.Click += (_, _) =>
        {
            if (_res == null || _host == null) return;
            try { _status.Text = _host.Draw(_res, _sim); }
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
            UseStoredPlans = _storedPlans.IsChecked == true,
            Coordinate = _coordinate.IsChecked == true,
            ProgressionSpeed = UiHelpers.Parse(_progSpeed, 0, "Velocidade da onda verde", 0, 120),
            BusesPerHourPerStop = UiHelpers.Parse(_busFreq, 12, "Ônibus por hora", 0, 200),
            BusDwell = UiHelpers.Parse(_busDwell, 20, "Embarque", 3, 180),
            KFactor = UiHelpers.Parse(_k, 0.10, "Fator K", 0.05, 0.2),
            ValueOfTime = UiHelpers.Parse(_vot, 25, "Valor do tempo", 0, 1000),
            Occupancy = UiHelpers.Parse(_occ, 1.4, "Ocupação", 1, 80),
            FuelPrice = UiHelpers.Parse(_fuel, 6.2, "Combustível", 0, 50),
            Co2Price = UiHelpers.Parse(_co2, 150, "CO₂", 0, 10000),
            CrashCost = UiHelpers.Parse(_crash, 180000, "Custo por acidente", 0, 1e8),
            AnnualHours = UiHelpers.Parse(_hours, 750, "Horas por ano", 0, 8760),
        };
        foreach (var (node, (box, _)) in _zones)
        {
            var v = UiHelpers.ParseNullable(box, "Volume da entrada", 0, 20000);
            if (v != null)
            {
                o.ZoneVolumes[node] = v.Value;
                o.ZoneVolumeKeys[_net.Nodes[node].Key] = v.Value;
            }
        }
        o.Nodes = _current.Options.Nodes.Where(x => !x.IsEmpty).Select(x => new NodeOverride
        {
            Node = x.Node, Control = x.Control, Cycle = x.Cycle, Greens = x.Greens?.ToList(),
            Turns = x.Turns.Select(t => new TurnCount { Approach = t.Approach, Total = t.Total, Left = t.Left, Through = t.Through, Right = t.Right, UTurn = t.UTurn }).ToList(),
        }).ToList();
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
        _writeBtn.IsEnabled = !busy && _res != null;
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
        FillRegulations();
        FillKpis();
        var keepNode = SelectedNode()?.Node.Key;
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
                Text = $"{nr.Node.Label} – {TrafficReport.KindLabel(nr.Node.Kind)}, {TrafficReport.ControlLabel(nr.Control)}\n{nr.Volume:0} veh/h, atraso {nr.Delay:0.0} s" + (nr.Cycle > 0 ? $", ciclo {nr.Cycle:0} s" : ""),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _nodeList.Items.Add(new ListBoxItem { Content = row, Tag = nr });
        }
        if (keepNode != null)
            foreach (ListBoxItem it in _nodeList.Items)
                if (it.Tag is NodeResult n2 && n2.Node.Key == keepNode) { _nodeList.SelectedItem = it; break; }
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

    private void FillRegulations()
    {
        _regList.Items.Clear();
        var f = _regFilter.SelectedIndex;
        foreach (var r in _net.Regulations.OrderBy(x => x.Kind).ThenBy(x => x.Source))
        {
            var ok = f switch
            {
                1 => r.Applied,
                2 => !r.Applied && r.Kind != TipoRegra.Informativa,
                3 => r.Source.StartsWith("Placa"),
                4 => r.Source.StartsWith("Linha") || r.Source.StartsWith("Legenda") || r.Source.StartsWith("Seta") || r.Source.StartsWith("Zebrado") || r.Source.StartsWith("Canaliza") || r.Source.StartsWith("Vagas") || r.Source.StartsWith("Símbolo"),
                5 => r.Source.StartsWith("Dispositivo") || r.Source.StartsWith("Semáforo"),
                _ => true,
            };
            if (!ok) continue;
            var color = r.Applied ? Color.FromRgb(0x2E, 0x7D, 0x32) : r.Kind == TipoRegra.Informativa ? Color.FromRgb(0x9E, 0xA7, 0xB3) : Color.FromRgb(0xC6, 0x28, 0x28);
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var bar = new Border { Width = 5, Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(2) };
            DockPanel.SetDock(bar, Dock.Left);
            row.Children.Add(bar);
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = $"{r.Source} – {TrafficRegulation.KindLabel(r.Kind)}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            txt.Children.Add(new TextBlock { Text = r.Effect, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x44, 0x52)) });
            row.Children.Add(txt);
            _regList.Items.Add(new ListBoxItem { Content = row, Tag = r, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        if (_regList.Items.Count == 0) _regList.Items.Add(new ListBoxItem { Content = "Nenhuma sinalização neste filtro.", IsEnabled = false });
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
        var crashes = r.Safety.Values.Sum(x => x.CrashesPerYear);
        Card("Segurança (referência HSM)", $"{crashes:0.0} acidentes/ano", $"{r.Safety.Values.Sum(x => x.Total)} pontos de conflito nos cruzamentos; {r.Safety.Values.Sum(x => x.InjuryCrashesPerYear):0.0}/ano com vítimas. Compare alternativas – não é previsão calibrada.",
            crashes > 10 ? Color.FromRgb(0xC6, 0x28, 0x28) : null);
        var e = r.Economics;
        Card("Custo anual da operação", $"R$ {e.Total / 1e6:0.00} mi/ano",
            $"tempo R$ {e.DelayCost / 1e3:0} mil · combustível R$ {e.FuelCost / 1e3:0} mil ({e.FuelLitersPerHour:0} L/h) · CO₂ R$ {e.Co2Cost / 1e3:0} mil · acidentes R$ {e.CrashCost / 1e3:0} mil");
        if (r.CommonCycle > 0) Card("Coordenação semafórica", $"ciclo comum {r.CommonCycle:0} s", $"{r.Nodes.Values.Count(n => n.Control == ControleNo.Semaforo)} semáforos; {r.Progression.Count} aproximação(ões) com progressão.");
        if (_sim != null)
        {
            Card("Microssimulação – viagens concluídas", $"{_sim.Completed}", $"em {_sim.Duration / 60:0} min; {_sim.InNetwork} na rede no fim" + (_sim.Backlog > 0 ? $"; {_sim.Backlog} não conseguiram entrar" : ""));
            Card("Tempo médio de viagem", $"{_sim.MeanTravelTime:0} s", $"atraso médio {_sim.MeanDelay:0} s por viagem, {_sim.StopsPerTrip:0.0} parada(s) por viagem");
            Card("Pico de veículos simultâneos", $"{_sim.MaxVehicles}", $"{_sim.LaneChanges} troca(s) de faixa; {_sim.BusStopsServed} parada(s) de ônibus atendidas.");
        }
        var crit = r.Diagnostics.Count(d => d.Severity == Gravidade.Critico);
        var warn = r.Diagnostics.Count(d => d.Severity == Gravidade.Atencao);
        Card("Diagnóstico", $"{crit} crítico(s), {warn} atenção", $"{r.Diagnostics.Count - crit - warn} informação(ões) – veja a aba Diagnóstico.", crit > 0 ? Color.FromRgb(0xC6, 0x28, 0x28) : null);
    }

    // ------------------------------------------------------------------------------------------------ cenários
    private void FillScenarioList(string? select)
    {
        _loadingScenario = true;
        _scenario.Items.Clear();
        _scenario.Items.Add(new ComboBoxItem { Content = "(cenário não salvo)", Tag = null });
        foreach (var sc in _scenarios) _scenario.Items.Add(new ComboBoxItem { Content = $"{sc.Name}  ·  {sc.Saved:dd/MM HH:mm}", Tag = sc });
        _scenario.SelectedIndex = Math.Max(0, _scenarios.FindIndex(x => x.Name == select) + 1);
        _loadingScenario = false;
    }

    private static string Num(double v, string fmt = "0.##") => v.ToString(fmt, UiHelpers.PtBr);

    private void LoadScenario(TrafficScenario sc)
    {
        _current = sc;
        var o = sc.Options;
        _scName.Text = sc.Name;
        _scNotes.Text = sc.Notes;
        _demand.SelectedIndex = (int)o.Demand;
        _growth.Text = Num(o.Growth * 100);
        _heavy.Text = Num(o.HeavyVehicles * 100);
        _bus.Text = Num(o.Buses * 100);
        _phf.Text = Num(o.PeakHourFactor);
        _peds.Text = Num(o.PedestriansPerHour);
        _optimize.IsChecked = o.OptimizeSignals;
        _cycle.Text = Num(o.FixedCycle);
        _storedPlans.IsChecked = o.UseStoredPlans;
        _coordinate.IsChecked = o.Coordinate;
        _progSpeed.Text = Num(o.ProgressionSpeed);
        _busFreq.Text = Num(o.BusesPerHourPerStop);
        _busDwell.Text = Num(o.BusDwell);
        _k.Text = Num(o.KFactor);
        _vot.Text = Num(o.ValueOfTime);
        _occ.Text = Num(o.Occupancy);
        _fuel.Text = Num(o.FuelPrice);
        _co2.Text = Num(o.Co2Price);
        _crash.Text = Num(o.CrashCost, "0");
        _hours.Text = Num(o.AnnualHours);
        _duration.Text = Num(o.SimSeconds / 60.0);
        _warmup.Text = Num(o.WarmupSeconds / 60.0);
        _seed.Text = o.Seed.ToString();
        foreach (var (node, (box, _)) in _zones)
            box.Text = o.ZoneVolumeKeys.TryGetValue(_net.Nodes[node].Key, out var v) ? Num(v, "0") : "";
    }

    private void SaveScenario(bool asNew)
    {
        TrafficOptions o;
        try { o = ReadOptions(); }
        catch (FormatException ex) { UiHelpers.Error(ex.Message); return; }
        var name = string.IsNullOrWhiteSpace(_scName.Text) ? "Cenário" : _scName.Text.Trim();
        if (asNew)
        {
            var baseName = name;
            var k = 2;
            while (_scenarios.Any(x => x.Name == name)) name = $"{baseName} ({k++})";
        }
        var sc = new TrafficScenario { Name = name, Notes = _scNotes.Text ?? "", Saved = DateTime.Now, Options = o };
        var i = _scenarios.FindIndex(x => x.Name == name);
        if (i >= 0) _scenarios[i] = sc; else _scenarios.Add(sc);
        _current = sc.Clone();
        _scName.Text = name;
        PersistScenarios();
        FillScenarioList(name);
    }

    private void DeleteScenario()
    {
        if (_scenario.SelectedItem is not ComboBoxItem { Tag: TrafficScenario sc }) return;
        if (MessageBox.Show(this, $"Excluir o cenário \"{sc.Name}\" do projeto?", "Simulador de Tráfego", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _scenarios.RemoveAll(x => x.Name == sc.Name);
        PersistScenarios();
        FillScenarioList(null);
    }

    private void PersistScenarios()
    {
        if (_host == null) return;
        try
        {
            _host.SaveScenarios(_scenarios);
            _status.Text = $"{_scenarios.Count} cenário(s) salvo(s) no projeto (Informações do projeto). Salve o arquivo do Revit para guardá-los.";
        }
        catch (Exception ex) { UiHelpers.Error("Não foi possível salvar os cenários no projeto: " + ex.Message); }
    }

    private async void Compare()
    {
        TrafficOptions cur;
        try { cur = ReadOptions(); }
        catch (FormatException ex) { UiHelpers.Error(ex.Message); return; }
        var list = new List<(string, TrafficOptions)> { (string.IsNullOrWhiteSpace(_scName.Text) ? "Atual" : _scName.Text.Trim() + " (atual)", cur) };
        list.AddRange(_scenarios.Select(x => (x.Name, x.Clone().Options)));
        _compare.Text = "Analisando os cenários…";
        try
        {
            var runs = await Task.Run(() => list.Select(x => (x.Item1, TrafficAnalysis.Run(_net, x.Item2))).ToList());
            _compare.Text = TrafficComparison.Text(runs);
        }
        catch (Exception ex) { _compare.Text = "Erro: " + ex.Message; }
        finally
        {
            // A rede é compartilhada: refaz o cenário atual para os controles do mapa voltarem a ele.
            if (_res != null) try { _res = TrafficAnalysis.Run(_net, cur); ShowResults(); } catch { /* mantém */ }
        }
    }

    // ------------------------------------------------------------------------------------------------ ajustes por cruzamento
    public sealed class TurnRow
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Total { get; set; } = "";
        public string Left { get; set; } = "";
        public string Through { get; set; } = "";
        public string Right { get; set; } = "";
        public string UTurn { get; set; } = "";
    }

    private static readonly (string Label, ControleNo? Value)[] Controls =
    {
        ("Do projeto", null), ("PARE na secundária", ControleNo.Pare), ("Dê a preferência", ControleNo.DePreferencia),
        ("Semáforo", ControleNo.Semaforo), ("Rotatória (teste)", ControleNo.Rotatoria), ("Sem controle (direita)", ControleNo.PreferenciaDireita),
    };

    private FrameworkElement BuildOverridePanel()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        sp.Children.Add(new TextBlock { Text = "Ajustes deste cruzamento no cenário", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x4A, 0x83)) });
        foreach (var (l, _) in Controls) _ovControl.Items.Add(l);
        _ovControl.SelectedIndex = 0;
        var row1 = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        row1.Children.Add(new TextBlock { Text = "Controle ", VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(_ovControl);
        row1.Children.Add(new TextBlock { Text = "  Ciclo (s) ", VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(_ovCycle);
        row1.Children.Add(new TextBlock { Text = "  Verdes ", VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(_ovGreens);
        sp.Children.Add(row1);
        foreach (var (h, prop, w) in new[] { ("Aproximação", "Name", 150.0), ("Total veh/h", "Total", 62.0), ("Esq. %", "Left", 44.0), ("Frente %", "Through", 52.0), ("Dir. %", "Right", 44.0), ("Ret. %", "UTurn", 44.0) })
            _ovTurns.Columns.Add(new DataGridTextColumn { Header = h, Binding = new System.Windows.Data.Binding(prop) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, Width = w, IsReadOnly = prop == "Name" });
        sp.Children.Add(new TextBlock { Text = "Contagem classificada (deixe em branco para usar a alocação):", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)), Margin = new Thickness(0, 2, 0, 2) });
        sp.Children.Add(_ovTurns);
        var row2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        row2.Children.Add(_ovApply);
        row2.Children.Add(_ovClear);
        row2.Children.Add(_ovSavePlan);
        row2.Children.Add(_ovSetControl);
        sp.Children.Add(row2);
        _ovApply.Click += (_, _) => { if (StoreOverride()) Run(false); };
        _ovClear.Click += (_, _) =>
        {
            if (SelectedNode() is not { } nr) return;
            _current.Options.Nodes.RemoveAll(x => x.Node == nr.Node.Key);
            FillOverride(nr);
            Run(false);
        };
        _ovSavePlan.ToolTip = "Grava o plano (ciclo, fases, verdes, amarelo, vermelho geral, defasagem) na interseção do projeto: a análise passa a usá-lo e ele acompanha o modelo.";
        _ovSavePlan.Click += (_, _) =>
        {
            if (_host == null || _res == null || SelectedNode() is not { } nr) return;
            if (nr.Phases.Count == 0) { UiHelpers.Error("Este cruzamento não é semaforizado neste cenário."); return; }
            try
            {
                _status.Text = _host.ApplyToIntersection(nr.Node.Key, nr.Control == nr.Node.DesignControl ? null : nr.Control, TrafficAnalysis.PlanOf(_res, nr, $"Simulador – {_current.Name}"));
                AskReload();
            }
            catch (Exception ex) { UiHelpers.Error("Não foi possível gravar o plano: " + ex.Message); }
        };
        _ovSetControl.ToolTip = "Troca o controle da interseção no projeto (placas, linhas de retenção e faixas são refeitas). Rotatória: use a ferramenta Rotatória.";
        _ovSetControl.Click += (_, _) =>
        {
            if (_host == null || SelectedNode() is not { } nr) return;
            var c = Controls[Math.Max(0, _ovControl.SelectedIndex)].Value;
            if (c == null) { UiHelpers.Error("Escolha o controle a aplicar."); return; }
            if (MessageBox.Show(this, $"Trocar o controle de {nr.Node.Label} no projeto para \"{TrafficReport.ControlLabel(c.Value)}\"? A interseção será refeita.",
                    "Simulador de Tráfego", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                _status.Text = _host.ApplyToIntersection(nr.Node.Key, c, null);
                // O controle do projeto mudou: o ajuste do cenário para este nó deixa de ser necessário.
                _current.Options.Nodes.RemoveAll(x => x.Node == nr.Node.Key && x.Control == c && x.Cycle == null && x.Greens == null && x.Turns.Count == 0);
                AskReload();
            }
            catch (Exception ex) { UiHelpers.Error("Não foi possível aplicar: " + ex.Message); }
        };
        return new ScrollViewer { Content = sp, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Relê o projeto (a janela reabre no mesmo cenário, com a rede atualizada).</summary>
    private void AskReload()
    {
        if (MessageBox.Show(this, _status.Text + "\n\nReler o projeto agora? A janela reabre no mesmo cenário com a rede atualizada.", "Simulador de Tráfego",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Reload();
    }

    private void Reload()
    {
        try
        {
            var o = ReadOptions();
            ReopenWith = new TrafficScenario { Name = string.IsNullOrWhiteSpace(_scName.Text) ? _current.Name : _scName.Text.Trim(), Notes = _scNotes.Text ?? "", Options = o };
        }
        catch (FormatException) { ReopenWith = _current.Clone(); }
        DialogResult = false;
    }

    private NodeResult? SelectedNode() => _nodeList.SelectedItem is ListBoxItem { Tag: NodeResult nr } ? nr : null;

    private void FillOverride(NodeResult nr)
    {
        var ov = _current.Options.Nodes.FirstOrDefault(x => x.Node == nr.Node.Key);
        _ovControl.SelectedIndex = Math.Max(0, Array.FindIndex(Controls, c => c.Value == ov?.Control));
        _ovCycle.Text = ov?.Cycle is { } c ? Num(c, "0") : "";
        _ovGreens.Text = ov?.Greens is { Count: > 0 } g ? string.Join("; ", g.Select(x => Num(x, "0"))) : "";
        var rows = new List<TurnRow>();
        foreach (var li in nr.Node.In)
        {
            var l = _net.Links[li];
            var t = ov?.Turns.FirstOrDefault(x => x.Approach == l.Key);
            rows.Add(new TurnRow
            {
                Key = l.Key, Name = l.Name,
                Total = t?.Total is { } tt ? Num(tt, "0") : "",
                Left = t != null ? Num(t.Left) : "", Through = t != null ? Num(t.Through) : "", Right = t != null ? Num(t.Right) : "", UTurn = t != null ? Num(t.UTurn) : "",
            });
        }
        _ovTurns.ItemsSource = rows;
        _ovSavePlan.IsEnabled = _host != null && nr.Phases.Count > 0 && nr.Node.Kind == TipoNo.Intersecao;
        _ovSetControl.IsEnabled = _host != null && nr.Node.Kind == TipoNo.Intersecao;
    }

    private bool StoreOverride()
    {
        if (SelectedNode() is not { } nr) return false;
        double? P(string? s, string what, double min, double max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (!double.TryParse(s.Replace('.', ','), System.Globalization.NumberStyles.Float, UiHelpers.PtBr, out var v) || v < min || v > max)
                throw new FormatException($"{what}: valor inválido \"{s}\" (entre {min} e {max}).");
            return v;
        }
        try
        {
            var ov = new NodeOverride { Node = nr.Node.Key, Control = Controls[Math.Max(0, _ovControl.SelectedIndex)].Value, Cycle = P(_ovCycle.Text, "Ciclo", 30, 240) };
            var greens = (_ovGreens.Text ?? "").Split(new[] { ';', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => P(x, "Verde", 3, 200)!.Value).ToList();
            if (greens.Count > 0) ov.Greens = greens;
            _ovTurns.CommitEdit();
            foreach (var r in (_ovTurns.ItemsSource as IEnumerable<TurnRow>) ?? Array.Empty<TurnRow>())
            {
                var parts = new[] { r.Left, r.Through, r.Right, r.UTurn };
                if (string.IsNullOrWhiteSpace(r.Total) && parts.All(string.IsNullOrWhiteSpace)) continue;
                var t = new TurnCount
                {
                    Approach = r.Key, Total = P(r.Total, "Total", 0, 20000),
                    Left = P(r.Left, "Esquerda %", 0, 100) ?? 0, Through = P(r.Through, "Frente %", 0, 100) ?? 0,
                    Right = P(r.Right, "Direita %", 0, 100) ?? 0, UTurn = P(r.UTurn, "Retorno %", 0, 100) ?? 0,
                };
                if (t.Left + t.Through + t.Right + t.UTurn <= 0) t.Through = 100;
                ov.Turns.Add(t);
            }
            _current.Options.Nodes.RemoveAll(x => x.Node == nr.Node.Key);
            if (!ov.IsEmpty) _current.Options.Nodes.Add(ov);
            return true;
        }
        catch (FormatException ex) { UiHelpers.Error(ex.Message); return false; }
    }

    /// <summary>Diagrama de tempos do semáforo: uma linha por fase (verde, amarelo, vermelho) ao longo do ciclo, com a defasagem.</summary>
    private void DrawTiming()
    {
        _timing.Children.Clear();
        if (SelectedNode() is not { } nr || nr.Phases.Count == 0 || nr.Cycle <= 0)
        {
            _timing.Children.Add(new TextBlock { Text = "Diagrama de tempos: selecione um cruzamento semaforizado.", Margin = new Thickness(6), Foreground = Brushes.Gray });
            return;
        }
        var w = Math.Max(100, _timing.ActualWidth - 110);
        var n = nr.Phases.Count;
        var rowH = Math.Min(20, (_timing.Height - 22) / n);
        var C = nr.Cycle;
        double X(double t) => 100 + t / C * w;
        var green = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        var yellow = new SolidColorBrush(Color.FromRgb(0xF2, 0xC2, 0x1B));
        var red = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        var t0 = 0.0;
        for (int k = 0; k < n; k++)
        {
            var ph = nr.Phases[k];
            var y = 4 + k * rowH;
            _timing.Children.Add(Label($"F{k + 1} {(ph.LeftOnly ? "esq." : "")}", 4, y, 90));
            void Bar(double a, double b, Brush br)
            {
                if (b <= a) return;
                var r = new System.Windows.Shapes.Rectangle { Width = Math.Max(1, X(b) - X(a)), Height = rowH - 4, Fill = br };
                Canvas.SetLeft(r, X(a));
                Canvas.SetTop(r, y);
                _timing.Children.Add(r);
            }
            var yEnd = t0 + ph.Green;
            var amber = Math.Min(3, nr.Intergreen);
            Bar(0, C, red);
            Bar(t0, Math.Min(C, yEnd), green);
            Bar(Math.Min(C, yEnd), Math.Min(C, yEnd + amber), yellow);
            _timing.Children.Add(Label($"{ph.Green:0}s", X(t0) + 2, y - 1, 40, Brushes.White));
            t0 = yEnd + nr.Intergreen;
        }
        var axisY = 6 + n * rowH;
        _timing.Children.Add(Label($"0", 96, axisY, 30));
        _timing.Children.Add(Label($"ciclo {C:0} s" + (nr.Offset > 0 ? $" · defasagem {nr.Offset:0} s" : "") + $" · {nr.PlanSource}", X(0) + 20, axisY, w));
        static TextBlock Label(string s, double x, double y, double w, Brush? fg = null)
        {
            var tb = new TextBlock { Text = s, FontSize = 10.5, Width = w, Foreground = fg ?? Brushes.Black };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            return tb;
        }
    }

    private static string NodeText(NodeResult nr, TrafficResult? res)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{nr.Node.Label} – {TrafficReport.KindLabel(nr.Node.Kind)}");
        sb.AppendLine($"Controle: {TrafficReport.ControlLabel(nr.Control)}" + (nr.Control != nr.Node.DesignControl ? $" (cenário; no projeto: {TrafficReport.ControlLabel(nr.Node.DesignControl)})" : ""));
        if (nr.Node.LeftPockets || nr.Node.RightTurnIslands) sb.AppendLine("Geometria: " + string.Join(", ", new[] { nr.Node.LeftPockets ? "bolsões de conversão à esquerda" : null, nr.Node.RightTurnIslands ? "ilhas de conversão à direita" : null }.Where(x => x != null)));
        sb.AppendLine($"Nível {nr.LOS} ({TrafficReport.LosMeaning(nr.LOS)}), atraso médio {nr.Delay:0.0} s/veh, {nr.Volume:0} veh/h");
        if (nr.Phases.Count > 0)
        {
            sb.AppendLine($"Ciclo {nr.Cycle:0} s ({nr.PlanSource}):");
            foreach (var ph in nr.Phases) sb.AppendLine($"  {ph.Name}: verde {ph.Green:0} s + {nr.Intergreen:0} s de entreverdes");
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
        if (res != null && res.Safety.TryGetValue(nr.Node.Index, out var sf) && sf.Legs >= 3)
        {
            sb.AppendLine();
            sb.AppendLine($"Segurança: {sf.Legs} ramos, {sf.Total} pontos de conflito ({sf.Crossing} cruzamentos, {sf.Merging} convergências, {sf.Diverging} divergências), {sf.Pedestrian} com pedestres.");
            sb.AppendLine($"  Acidentes previstos: {sf.CrashesPerYear:0.00}/ano ({sf.InjuryCrashesPerYear:0.00} com vítimas) – {sf.Model}; VDM {sf.AadtMajor:0} × {sf.AadtMinor:0}.");
        }
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
