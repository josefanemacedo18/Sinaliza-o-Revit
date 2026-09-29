using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SinalizacaoViaria.Core.Geo;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Dados do projeto para a janela da imagem de satélite.</summary>
public sealed class SatelliteContext
{
    /// <summary>Georreferência já gravada no projeto (nula na primeira imagem).</summary>
    public GeoReference? Stored { get; init; }
    public double SiteLatitude { get; init; }
    public double SiteLongitude { get; init; }
    public double SiteAltitude { get; init; }
    /// <summary>Ângulo do Norte de projeto para o Norte verdadeiro (rad) – ProjectPosition.Angle.</summary>
    public double NorthAngle { get; init; }
    /// <summary>Centro da vista ativa (m, modelo).</summary>
    public Vec2 ViewCenter { get; init; }
}

/// <summary>
/// Imagem de Satélite: fonte, centro (lat, lon), área, resolução e altitude; prévia; baixa e reprojeta com progresso e
/// cancelamento. O resultado é colocado no Revit pelo comando, depois que a janela fecha.
/// </summary>
public sealed class SatelliteWindow : Window
{
    private const string PrecisionText =
        "Precisão: as medidas dentro da imagem ficam na escala correta (1 m na imagem = 1 m no Revit). A posição absoluta de " +
        "imagens de satélite tem erro típico de alguns metros e pode variar entre fontes – para projeto executivo, ortofoto " +
        "oficial ou levantamento topográfico prevalecem.";

    private readonly SatelliteContext _ctx;
    private readonly ComboBox _source = new() { MinWidth = 260 };
    private readonly TextBox _center = new();
    private readonly TextBox _width = new() { Text = "800" };
    private readonly TextBox _height = new() { Text = "600" };
    private readonly TextBox _res = new() { Text = "0,30" };
    private readonly TextBox _alt = new();
    private readonly TextBox _north = new();
    private readonly CheckBox _useProjectNorth = new() { Content = "Usar o Norte do projeto (0°) – ignorar o ângulo lido" };
    private readonly TextBlock _altWarn = Warn();
    private readonly TextBlock _northWarn = Warn();
    private readonly TextBlock _centerWarn = Warn();
    private readonly ComboBox _anchor = new();
    private readonly CheckBox _resetOrigin = new() { Content = "Redefinir a origem geográfica (as imagens anteriores deixam de encaixar)" };
    private readonly CheckBox _replace = new() { Content = "Substituir a imagem anterior", IsChecked = true };
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ProgressBar _bar = new() { Height = 10, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Image _preview = new() { Width = 360, Height = 270, Stretch = Stretch.Uniform };
    private readonly Button _go = new() { Content = "Baixar e colocar na vista", IsDefault = true };
    private readonly Button _previewBtn = new() { Content = "Prévia" };
    private readonly Button _cancel = new() { Content = "Cancelar", IsCancel = true };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private CancellationTokenSource? _cts;

    /// <summary>Imagem pronta (nula se cancelado).</summary>
    public SatelliteImage? Result { get; private set; }
    public bool ReplacePrevious => _replace.IsChecked == true;

    public SatelliteWindow(SatelliteContext ctx)
    {
        _ctx = ctx;
        Title = "Imagem de Satélite – SinalizaBIM";
        Width = 820;
        Height = 700;
        MinWidth = 700;
        MinHeight = 560;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;

        _source.Items.Add(SatelliteSource.Google.Nome + " (foto aérea – chave própria)");
        _source.Items.Add(SatelliteSource.Osm.Nome + " – é mapa, não foto de satélite");
        // Sem chave do Google gravada, abre no OpenStreetMap (não exige chave).
        _source.SelectedIndex = string.IsNullOrWhiteSpace(GoogleKeyStore.Key) || PluginContext.Settings.LastUsed.TryGetValue("satelite:fonte", out var f) && f == "osm" ? 1 : 0;
        var stored = ctx.Stored;
        var lat = stored?.Latitude ?? ctx.SiteLatitude;
        var lon = stored?.Longitude ?? ctx.SiteLongitude;
        if (stored != null)
        {
            // Centro padrão: o da vista atual, já na geografia do projeto.
            (lat, lon) = stored.ModelToGeo(ctx.ViewCenter);
        }
        _center.Text = FormattableString.Invariant($"{lat:0.000000}, {lon:0.000000}");
        // A elevação do local do Revit pode vir sem significado (ex.: 8633): fora de −500…6000 m vira 0 com aviso.
        _alt.Text = UiHelpers.F(GeoReference.SanitizeAltitude(stored?.Altitude ?? ctx.SiteAltitude, out var altWarn), "0");
        _altWarn.Text = altWarn ?? "";
        var northDeg = GeoReference.NorthAngleToDegrees(stored?.NorthAngle ?? ctx.NorthAngle);
        _north.Text = UiHelpers.F(northDeg, "0.00");
        _north.ToolTip = $"Ângulo lido do projeto (Gerenciar → Local → Posição): {UiHelpers.F(GeoReference.NorthAngleToDegrees(ctx.NorthAngle), "0.00")}°. " +
                         "Positivo = anti-horário. Confira com o valor do Revit ou digite o correto.";
        if (stored != null) _north.IsEnabled = _useProjectNorth.IsEnabled = false;   // fixo pela origem gravada
        if (PluginContext.Settings.LastUsed.TryGetValue("satelite:area", out var area) && area.Split('x') is { Length: 3 } a)
        {
            _width.Text = a[0];
            _height.Text = a[1];
            _res.Text = a[2];
        }
        _anchor.Items.Add("Centro da vista atual");
        _anchor.Items.Add("Origem interna do projeto (0, 0)");
        _anchor.SelectedIndex = 0;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var form = new StackPanel();
        form.Children.Add(new TextBlock
        {
            Text = "Traz a imagem aérea para a vista de planta na escala métrica real e na posição geográfica do projeto: desenhe o eixo por cima com as medidas certas.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });
        form.Children.Add(Row("Fonte", _source));
        form.Children.Add(Row("Centro (lat, lon)", _center, "Cole o texto copiado do Google Maps (clique com o botão direito no local → copiar as coordenadas), ex.: -23.550520, -46.633308"));
        form.Children.Add(_centerWarn);
        form.Children.Add(Row("Largura (m)", _width, "Até 3000 m."));
        form.Children.Add(Row("Altura (m)", _height, "Até 3000 m."));
        form.Children.Add(Row("Resolução (m/px)", _res, "0,30 m/px por padrão; o zoom da fonte é escolhido por ela."));
        form.Children.Add(Row("Altitude média (m)", _alt, "Altitude do local: só afeta o fator de escala (a 800 m, 12,5 cm/km a mais que no elipsoide) – nunca impede a imagem."));
        form.Children.Add(_altWarn);
        if (stored == null) form.Children.Add(Row("Posição no projeto", _anchor, "Ponto do modelo que recebe o centro da primeira imagem (a origem geográfica do projeto)."));
        else
        {
            form.Children.Add(new TextBlock
            {
                Text = $"Origem geográfica do projeto: {stored.Latitude:0.000000}, {stored.Longitude:0.000000} (definida em {stored.Created:dd/MM/yyyy}). " +
                       "As novas imagens encaixam exatamente nas anteriores e nos eixos já desenhados.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2), Foreground = Brushes.DimGray,
            });
            form.Children.Add(_resetOrigin);
            if (stored.LastImageElements.Count > 0) form.Children.Add(_replace);
        }
        form.Children.Add(Row("Norte verdadeiro (°)", _north));
        form.Children.Add(new TextBlock
        {
            Text = $"Lido do projeto: {UiHelpers.F(GeoReference.NorthAngleToDegrees(ctx.NorthAngle), "0.00")}° (Gerenciar → Local → Posição → Ângulo para o Norte verdadeiro). " +
                   (stored != null ? "Fixado pela origem geográfica gravada." : "A imagem sai girada por este ângulo; 0° = Norte verdadeiro igual ao Norte do projeto."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(150, 0, 0, 2), Foreground = Brushes.DimGray,
        });
        _useProjectNorth.Margin = new Thickness(150, 0, 0, 0);
        form.Children.Add(_useProjectNorth);
        form.Children.Add(_northWarn);
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var cfg = new Button { Content = "Chave do Google…", Margin = new Thickness(0) };
        cfg.Click += (_, _) => { UiHelpers.ShowModal(new SettingsWindow()); Refresh(); };
        keyRow.Children.Add(cfg);
        form.Children.Add(keyRow);
        form.Children.Add(_info);
        form.Children.Add(new TextBlock { Text = PrecisionText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0x56, 0x1F)) });
        form.Children.Add(_bar);
        form.Children.Add(_status);
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);

        var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        right.Children.Add(new Border { BorderBrush = Brushes.Silver, BorderThickness = new Thickness(1), Child = _preview, Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)) });
        right.Children.Add(new TextBlock { Text = "Prévia da área (reprojetada, Norte do projeto para cima).", TextWrapping = TextWrapping.Wrap, Width = 360, Foreground = Brushes.DimGray });
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        if (Resources["Primary"] is Style prim) _go.Style = prim;
        buttons.Children.Add(_previewBtn);
        buttons.Children.Add(_go);
        buttons.Children.Add(_cancel);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);
        Content = root;

        _useProjectNorth.Checked += (_, _) => { _north.IsEnabled = false; Refresh(); };
        _useProjectNorth.Unchecked += (_, _) => { _north.IsEnabled = _ctx.Stored == null; Refresh(); };
        foreach (var tb in new[] { _center, _width, _height, _res, _alt, _north }) tb.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _source.SelectionChanged += (_, _) => Refresh();
        _anchor.SelectionChanged += (_, _) => Refresh();
        _resetOrigin.Checked += (_, _) => Refresh();
        _resetOrigin.Unchecked += (_, _) => Refresh();
        _debounce.Tick += (_, _) => { _debounce.Stop(); Refresh(); };
        _go.Click += async (_, _) => await RunAsync(preview: false);
        _previewBtn.Click += async (_, _) => await RunAsync(preview: true);
        _cancel.Click += (_, _) =>
        {
            if (_cts != null) { _cts.Cancel(); return; }
            DialogResult = false;
        };
        Loaded += (_, _) => Refresh();
    }

    private static TextBlock Warn() => new()
    {
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(150, 0, 0, 2), Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0x56, 0x1F)),
    };

    private static FrameworkElement Row(string label, FrameworkElement input, string? hint = null)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(input, 1);
        g.Children.Add(input);
        if (hint != null) input.ToolTip = hint;
        return g;
    }

    private SatelliteSource Source => _source.SelectedIndex == 1 ? SatelliteSource.Osm : SatelliteSource.Google;

    /// <summary>Georreferência e centro (modelo) com os valores da janela.</summary>
    private (GeoReference Geo, Vec2 Center) Inputs()
    {
        _centerWarn.Text = "";
        if (!GeoReference.TryParseLatLon(_center.Text, out var lat, out var lon))
        {
            _centerWarn.Text = "Centro inválido: use \"latitude, longitude\" em graus decimais (ex.: -23.550520, -46.633308).";
            throw new FormatException(_centerWarn.Text);
        }
        // Altitude e Norte nunca bloqueiam: valor inválido vira o padrão, com aviso ao lado do campo.
        var alt = GeoReference.SanitizeAltitude(UiHelpers.ParseOpt(_alt.Text), out var altWarn);
        _altWarn.Text = altWarn ?? "";
        _northWarn.Text = "";
        var northAngle = _ctx.NorthAngle;
        if (_useProjectNorth.IsChecked == true) northAngle = 0;
        else if (UiHelpers.ParseOpt(_north.Text) is { } nd) northAngle = GeoReference.NorthAngleFromDegrees(nd);
        else _northWarn.Text = $"Ângulo inválido: usando o lido do projeto ({UiHelpers.F(GeoReference.NorthAngleToDegrees(_ctx.NorthAngle), "0.00")}°).";
        var stored = _ctx.Stored;
        if (stored != null && _resetOrigin.IsChecked != true)
        {
            var geo = new GeoReference
            {
                Latitude = stored.Latitude, Longitude = stored.Longitude, Altitude = alt, NorthAngle = stored.NorthAngle,
                OriginX = stored.OriginX, OriginY = stored.OriginY, Created = stored.Created, LastImageElements = stored.LastImageElements,
            };
            // A altitude muda só o fator (a origem continua a mesma).
            return (geo, geo.GeoToModel(lat, lon));
        }
        // Primeira imagem: centro da vista ou origem interna; origem redefinida: centro da vista.
        var origin = stored == null && _anchor.SelectedIndex == 1 ? Vec2.Zero : _ctx.ViewCenter;
        var g2 = new GeoReference
        {
            Latitude = lat, Longitude = lon, Altitude = alt, NorthAngle = northAngle, OriginX = origin.X, OriginY = origin.Y,
            LastImageElements = stored?.LastImageElements ?? new(),
        };
        return (g2, origin);
    }

    private SatellitePlan MakePlan(SatelliteSource src)
    {
        var (geo, center) = Inputs();
        var w = UiHelpers.Parse(_width, 800, "Largura", 20, SatellitePlan.MaxSide);
        var h = UiHelpers.Parse(_height, 600, "Altura", 20, SatellitePlan.MaxSide);
        var r = UiHelpers.Parse(_res, 0.3, "Resolução", 0.05, 20);
        return SatellitePlan.Create(geo, src, center, w, h, r);
    }

    private void Refresh()
    {
        if (_cts != null) return;
        try
        {
            var p = MakePlan(Source);
            var err = SatelliteReprojection.MaxInterpolationError(p);
            var key = Source.Fonte == FonteImagem.GoogleSatelite && string.IsNullOrWhiteSpace(GoogleKeyStore.Key)
                ? "\n⚠ Falta a chave da Google Maps Platform (botão \"Chave do Google…\")." : "";
            _info.Text =
                $"Zoom {p.Zoom} ({p.Source.Nome}) · pixel da fonte ≈ {UiHelpers.F(p.SourceMpp, "0.000")} m · imagem com {UiHelpers.F(p.OutputMpp, "0.000")} m/px efetivos\n" +
                $"{p.PixelWidth} × {p.PixelHeight} px em {p.Blocks.Count} bloco(s) · área {UiHelpers.F(p.Width, "0")} × {UiHelpers.F(p.Height, "0")} m\n" +
                $"{p.TileCount} tiles, ~{UiHelpers.F(p.EstimatedMB, "0.0")} MB (os já baixados vêm do cache)\n" +
                $"Erro máximo estimado da reprojeção: {UiHelpers.F(err * 1000, "0.000")} mm · fator de altitude 1 − {UiHelpers.F((1 - p.Geo.ElevationFactor) * 1e5, "0.0")}×10⁻⁵" +
                (p.Notes.Count > 0 ? "\n• " + string.Join("\n• ", p.Notes) : "") + key;
            _go.IsEnabled = _previewBtn.IsEnabled = true;
        }
        catch (FormatException ex)
        {
            // Só centro/área/resolução inválidos impedem a imagem – a mensagem fica também ao lado do campo.
            _info.Text = ex.Message;
            _go.IsEnabled = _previewBtn.IsEnabled = false;
        }
    }

    private async Task RunAsync(bool preview)
    {
        SatellitePlan plan;
        try { plan = MakePlan(Source); }
        catch (FormatException ex) { UiHelpers.Error(ex.Message); return; }
        var key = GoogleKeyStore.Key;
        if (plan.Source.Fonte == FonteImagem.GoogleSatelite && string.IsNullOrWhiteSpace(key))
        {
            UiHelpers.Error("Para o Google Satélite informe sua chave da Google Maps Platform (botão \"Chave do Google…\" ou SinalizaBIM → Configurações).\n\nSem chave, use o OpenStreetMap (mapa).");
            return;
        }
        _cts = new CancellationTokenSource();
        _go.IsEnabled = _previewBtn.IsEnabled = false;
        _cancel.Content = "Parar";
        _bar.Visibility = Visibility.Visible;
        _bar.Value = 0;
        var progress = new Progress<(string Stage, double Fraction)>(p => { _status.Text = p.Stage; _bar.Value = p.Fraction; });
        try
        {
            if (preview)
            {
                _status.Text = "Baixando a prévia…";
                var side = Math.Max(plan.Width, plan.Height);
                var small = SatellitePlan.Create(plan.Geo, plan.Source, (plan.Min + plan.Max) / 2, plan.Width, plan.Height, side / 360);
                _preview.Source = await SatelliteImageBuilder.PreviewAsync(small, key, _cts.Token);
                _status.Text = "Prévia pronta.";
            }
            else
            {
                PluginContext.Settings.LastUsed["satelite:fonte"] = plan.Source.Fonte == FonteImagem.OpenStreetMap ? "osm" : "google";
                PluginContext.Settings.LastUsed["satelite:area"] = $"{_width.Text}x{_height.Text}x{_res.Text}";
                PluginContext.SaveSettings();
                var geo = plan.Geo;
                var center = (plan.Min + plan.Max) / 2;
                // Lido aqui, na thread da janela (o replanejamento roda depois de um await, fora dela).
                var res = UiHelpers.Parse(_res, 0.3, "Resolução", 0.05, 20);
                var (resolved, attribution) = await SatelliteImageBuilder.ResolveAsync(plan, src => SatellitePlan.Create(geo, src, center, plan.Width, plan.Height, res), key, _cts.Token);
                Result = await SatelliteImageBuilder.BuildAsync(resolved, attribution, key, progress, _cts.Token);
                DialogResult = true;
            }
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelado – nada foi criado no projeto.";
        }
        catch (SatelliteException ex)
        {
            _status.Text = ex.Message + " Nada foi criado no projeto.";
            UiHelpers.Error(ex.Message + "\n\nNada foi criado no projeto.");
        }
        catch (Exception ex)
        {
            Log.Error("Imagem de satélite", ex);
            _status.Text = "Erro: " + ex.Message + " Nada foi criado no projeto.";
            UiHelpers.Error("Não foi possível montar a imagem: " + ex.Message + "\n\nNada foi criado no projeto.");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (IsLoaded && DialogResult != true)
            {
                _cancel.Content = "Cancelar";
                _bar.Visibility = Visibility.Collapsed;
                Refresh();
            }
        }
    }
}
