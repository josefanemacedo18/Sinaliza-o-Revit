using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SinalizacaoViaria.Core.Geo;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Dados do projeto para a janela da imagem aérea.</summary>
public sealed class SatelliteContext
{
    public GeoReference? Stored { get; init; }
    public double SiteLatitude { get; init; }
    public double SiteLongitude { get; init; }
    public double SiteAltitude { get; init; }
    /// <summary>Ângulo do Norte de projeto para o Norte verdadeiro (rad) – ProjectPosition.Angle.</summary>
    public double NorthAngle { get; init; }
    public Vec2 ViewCenter { get; init; }
}

/// <summary>
/// Imagem Aérea sem chave de API, em duas opções:
/// 1) Google Earth Pro – o plugin acompanha a vista por um NetworkLink (KML), põe 2 marcadores de controle, e a imagem
///    salva é calibrada sozinha pelos marcadores (escala, posição e Norte verdadeiro);
/// 2) Mapa no plugin – OpenStreetMap, fonte própria XYZ ou WMS (ortofotos oficiais), com busca, zoom pela roda do mouse e
///    recorte em metros reais.
/// </summary>
public sealed class AerialImageWindow : Window
{
    private const string PrecisionText =
        "As medidas dentro da imagem ficam na escala correta (tamanho medido no modelo e corrigido até < 1 mm). A posição absoluta de " +
        "imagens aéreas tem erro de alguns metros; para projeto executivo, ortofoto oficial ou levantamento topográfico prevalecem.";

    private readonly SatelliteContext _ctx;
    private readonly TabControl _tabs = new();
    // Comuns
    private readonly TextBox _alt = new();
    private readonly TextBlock _altWarn = Warn();
    private readonly TextBox _north = new();
    private readonly CheckBox _useProjectNorth = new() { Content = "Usar o Norte do projeto (0°)" };
    private readonly ComboBox _anchor = new();
    private readonly CheckBox _resetOrigin = new() { Content = "Redefinir a origem geográfica (as imagens anteriores deixam de encaixar)" };
    private readonly CheckBox _replace = new() { Content = "Substituir a imagem anterior", IsChecked = true };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ProgressBar _bar = new() { Height = 8, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button _go = new() { Content = "Gerar e colocar na vista", IsDefault = true };
    private readonly Button _cancel = new() { Content = "Cancelar", IsCancel = true };
    // Google Earth
    private GoogleEarthServer? _ge;
    private readonly TextBlock _geLive = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _geWarn = Warn();
    private readonly TextBox _geFolder = new();
    private readonly TextBlock _geResult = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image _gePreview = new() { Height = 220, Stretch = Stretch.Uniform };
    private FileSystemWatcher? _watcher;
    private GeView? _geView;
    private ((double Lat, double Lon) Nw, (double Lat, double Lon) Se)? _geMarkers;
    private AerialResult? _geReady;
    private bool _geNeedsCalibration;
    // Mapa
    private readonly TileMapControl _map = new() { MinHeight = 360 };
    private readonly TextBox _search = new();
    private readonly ListBox _places = new() { MaxHeight = 90 };
    private readonly ComboBox _source = new();
    private readonly TextBox _wmsUrl = new();
    private readonly ComboBox _wmsLayer = new();
    private readonly TextBox _res = new() { Text = "0,30" };
    private readonly TextBlock _mapInfo = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image _mapPreview = new() { Height = 150, Stretch = Stretch.Uniform };
    private WmsCapabilities? _caps;
    private CancellationTokenSource? _cts;

    /// <summary>Imagem pronta (nula se cancelado).</summary>
    public AerialResult? Result { get; private set; }
    public bool ReplacePrevious => _replace.IsChecked == true;
    /// <summary>A imagem veio sem marcadores: calibrar por 2 pontos depois de colocar.</summary>
    public bool NeedsCalibration { get; private set; }

    public AerialImageWindow(SatelliteContext ctx)
    {
        _ctx = ctx;
        Title = "Imagem Aérea – SinalizaBIM";
        Width = 1100;
        Height = 820;
        MinWidth = 900;
        MinHeight = 640;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;

        var stored = ctx.Stored;
        _alt.Text = UiHelpers.F(GeoReference.SanitizeAltitude(stored?.Altitude ?? ctx.SiteAltitude, out var aw), "0");
        _altWarn.Text = aw ?? "";
        _north.Text = UiHelpers.F(GeoReference.NorthAngleToDegrees(stored?.NorthAngle ?? ctx.NorthAngle), "0.00");
        _north.ToolTip = $"Lido do projeto (Gerenciar → Local → Posição): {UiHelpers.F(GeoReference.NorthAngleToDegrees(ctx.NorthAngle), "0.00")}°.";
        if (stored != null) _north.IsEnabled = _useProjectNorth.IsEnabled = false;
        _anchor.Items.Add("Centro da vista atual");
        _anchor.Items.Add("Origem interna do projeto (0, 0)");
        _anchor.SelectedIndex = 0;

        _tabs.Items.Add(new TabItem { Header = "1 · Google Earth Pro (calibração automática)", Content = GoogleEarthTab() });
        _tabs.Items.Add(new TabItem { Header = "2 · Mapa no plugin (OpenStreetMap / fonte própria / WMS)", Content = MapTab() });
        _tabs.SelectedIndex = PluginContext.Settings.LastUsed.TryGetValue("aerea:aba", out var tab) && tab == "mapa" ? 1 : 0;

        var root = new DockPanel { Margin = new Thickness(10) };
        var common = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var row = new WrapPanel();
        row.Children.Add(Labeled("Altitude média (m)", _alt, 90));
        row.Children.Add(Labeled("Norte verdadeiro (°)", _north, 90));
        row.Children.Add(_useProjectNorth);
        if (stored == null) row.Children.Add(Labeled("Posição da 1ª imagem", _anchor, 200));
        else { row.Children.Add(_resetOrigin); if (stored.LastImageElements.Count > 0) row.Children.Add(_replace); }
        common.Children.Add(row);
        common.Children.Add(_altWarn);
        common.Children.Add(new TextBlock { Text = PrecisionText, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0x56, 0x1F)), Margin = new Thickness(0, 4, 0, 0) });
        common.Children.Add(_bar);
        common.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        if (Resources["Primary"] is Style prim) _go.Style = prim;
        buttons.Children.Add(_go);
        buttons.Children.Add(_cancel);
        common.Children.Add(buttons);
        DockPanel.SetDock(common, Dock.Bottom);
        root.Children.Add(common);
        root.Children.Add(_tabs);
        Content = root;

        _go.Click += async (_, _) => await GoAsync();
        _cancel.Click += (_, _) => { if (_cts != null) { _cts.Cancel(); return; } DialogResult = false; };
        _useProjectNorth.Checked += (_, _) => _north.IsEnabled = false;
        _useProjectNorth.Unchecked += (_, _) => _north.IsEnabled = _ctx.Stored == null;
        Closed += (_, _) => { _watcher?.Dispose(); _ge?.Dispose(); };
    }

    private static TextBlock Warn() => new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0x56, 0x1F)) };

    private static FrameworkElement Labeled(string label, FrameworkElement input, double width)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 16, 2) };
        sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        input.Width = width;
        sp.Children.Add(input);
        return sp;
    }

    // ------------------------------------------------------------------ georreferência comum

    private GeoReference MakeGeo(double lat, double lon)
    {
        var alt = GeoReference.SanitizeAltitude(UiHelpers.ParseOpt(_alt.Text), out var w);
        _altWarn.Text = w ?? "";
        var stored = _ctx.Stored;
        if (stored != null && _resetOrigin.IsChecked != true)
            return new GeoReference
            {
                Latitude = stored.Latitude, Longitude = stored.Longitude, Altitude = alt, NorthAngle = stored.NorthAngle,
                OriginX = stored.OriginX, OriginY = stored.OriginY, Created = stored.Created, LastImageElements = stored.LastImageElements,
            };
        var north = _useProjectNorth.IsChecked == true ? 0
            : UiHelpers.ParseOpt(_north.Text) is { } nd ? GeoReference.NorthAngleFromDegrees(nd) : _ctx.NorthAngle;
        var origin = stored == null && _anchor.SelectedIndex == 1 ? Vec2.Zero : _ctx.ViewCenter;
        return new GeoReference
        {
            Latitude = lat, Longitude = lon, Altitude = alt, NorthAngle = north, OriginX = origin.X, OriginY = origin.Y,
            LastImageElements = stored?.LastImageElements ?? new(),
        };
    }

    // ------------------------------------------------------------------ opção 1: Google Earth Pro

    private FrameworkElement GoogleEarthTab()
    {
        var sp = new StackPanel { Margin = new Thickness(8) };
        sp.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "1. Clique em \"Abrir no Google Earth Pro\" – o plugin passa a acompanhar a vista e mostra 2 miras magenta no mapa.\n" +
                   "2. No Google Earth, aproxime com a roda do mouse e arraste até a área; aperte N (Norte para cima) e U (vista de cima).\n" +
                   "3. Com as 2 miras visíveis, salve: Arquivo → Salvar → Salvar imagem (na pasta abaixo).\n" +
                   "4. O plugin encontra as miras na imagem e calibra sozinho a escala, a posição e o Norte verdadeiro.\n" +
                   "5. Clique em \"Gerar e colocar na vista\".",
        });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var open = new Button { Content = "Abrir no Google Earth Pro", Padding = new Thickness(12, 4, 12, 4) };
        open.Click += (_, _) => OpenGoogleEarth();
        var pick = new Button { Content = "Usar a imagem salva…", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        pick.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp", InitialDirectory = Directory.Exists(_geFolder.Text) ? _geFolder.Text : null };
            if (dlg.ShowDialog(this) == true) _ = ProcessGeImageAsync(dlg.FileName);
        };
        bar.Children.Add(open);
        bar.Children.Add(pick);
        sp.Children.Add(bar);
        _geFolder.Text = PluginContext.Settings.LastUsed.TryGetValue("aerea:pasta", out var f) && Directory.Exists(f)
            ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var folderRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var browse = new Button { Content = "…", MinWidth = 30 };
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _geFolder.Text };
            if (dlg.ShowDialog(this) == true) { _geFolder.Text = dlg.FolderName; Watch(); }
        };
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(new TextBlock { Text = "Pasta onde você salva a imagem (o plugin detecta sozinho): ", VerticalAlignment = VerticalAlignment.Center });
        folderRow.Children.Add(_geFolder);
        sp.Children.Add(folderRow);
        sp.Children.Add(new Border { Child = _geLive, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF9)) });
        _geLive.Text = "Google Earth ainda não conectado.";
        sp.Children.Add(_geWarn);
        sp.Children.Add(_geResult);
        sp.Children.Add(_gePreview);
        sp.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 0),
            Text = "O plugin usa só recursos do próprio Google Earth Pro (arquivo KML e \"Salvar imagem\"); não baixa imagens do Google. " +
                   "O uso das imagens segue os termos do Google Earth; os créditos (© Google) acompanham a imagem colocada. " +
                   "Precisa do Google Earth Pro para computador (gratuito). O vínculo usa só o próprio computador (127.0.0.1).",
        });
        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static byte[] MarkerIcon()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var mag = new SolidColorBrush(Color.FromRgb(255, 0, 255));
            var pen = new Pen(mag, 4);
            dc.DrawEllipse(null, pen, new Point(24, 24), 15, 15);
            dc.DrawLine(pen, new Point(24, 2), new Point(24, 46));
            dc.DrawLine(pen, new Point(2, 24), new Point(46, 24));
        }
        var bmp = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private void OpenGoogleEarth()
    {
        try
        {
            if (_ge == null)
            {
                _ge = new GoogleEarthServer(MarkerIcon());
                _ge.ViewChanged += v => Dispatcher.BeginInvoke(() => OnGeView(v));
            }
            var dir = Path.Combine(PluginPaths.Root, "google-earth");
            Directory.CreateDirectory(dir);
            var kml = Path.Combine(dir, "SinalizaBIM-recorte.kml");
            File.WriteAllText(kml, GoogleEarthKml.NetworkLink(_ge.ViewUrl));
            var exe = FindGoogleEarth();
            if (exe != null) Process.Start(new ProcessStartInfo(exe, $"\"{kml}\"") { UseShellExecute = false });
            else
            {
                try { Process.Start(new ProcessStartInfo(kml) { UseShellExecute = true }); }
                catch
                {
                    UiHelpers.Error("Google Earth Pro não encontrado.\n\nInstale o Google Earth Pro para computador (gratuito) em google.com/earth/versions → \"Google Earth Pro para computador\" e clique de novo.\n\n" +
                                    $"O arquivo do vínculo está em:\n{kml}");
                    return;
                }
            }
            Watch();
            _geLive.Text = "Aguardando o Google Earth… (mova o mapa uma vez para ele enviar a vista)";
        }
        catch (Exception ex)
        {
            Log.Error("Imagem aérea – Google Earth", ex);
            UiHelpers.Error("Não foi possível abrir o vínculo com o Google Earth: " + ex.Message);
        }
    }

    private static string? FindGoogleEarth()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var p = Path.Combine(root, "Google", "Google Earth Pro", "client", "googleearth.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private void OnGeView(GeView v)
    {
        _geView = v;
        _geMarkers = _ge?.LastMarkers;
        var (w, h) = v.SizeMeters();
        _geLive.Text = $"Área visível: {UiHelpers.F(w, "0")} × {UiHelpers.F(h, "0")} m · rumo {UiHelpers.F(GeView.NormalizeHeading(v.Heading), "0.0")}° · inclinação {UiHelpers.F(v.Tilt, "0.0")}°" +
                       (v.PixelsX > 0 ? $" · janela {v.PixelsX} × {v.PixelsY} px (~{UiHelpers.F(w / v.PixelsX, "0.00")} m/px)" : "");
        _geWarn.Text = v.IsTopDown ? "" : "⚠ Gire para o Norte (tecla N) e olhe de cima (tecla U) antes de salvar – só assim a imagem vale como planta.";
    }

    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (!Directory.Exists(_geFolder.Text)) return;
        PluginContext.Settings.LastUsed["aerea:pasta"] = _geFolder.Text;
        _watcher = new FileSystemWatcher(_geFolder.Text) { EnableRaisingEvents = true, IncludeSubdirectories = false };
        _watcher.Created += (_, e) =>
        {
            var ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".bmp")
                Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(1500);                 // espera o Google Earth terminar de gravar
                    await ProcessGeImageAsync(e.FullPath);
                });
        };
    }

    /// <summary>Calibra a imagem salva: pelas miras detectadas (exato) ou pela caixa da vista (aproximado).</summary>
    private async Task ProcessGeImageAsync(string file)
    {
        if (_cts != null) return;
        _geResult.Text = "Lendo a imagem…";
        _geReady = null;
        try
        {
            var view = _geView;
            var markers = _geMarkers;
            var (px, sw, sh) = await Task.Run(() => SatelliteTiles.DecodeBgr32(File.ReadAllBytes(file)));
            var found = await Task.Run(() => MarkerDetector.Find(px, sw, sh));
            var notes = new List<string> { $"Imagem: {Path.GetFileName(file)} ({sw} × {sh} px)." };
            var centerLat = view != null ? (view.North + view.South) / 2 : _ctx.Stored?.Latitude ?? _ctx.SiteLatitude;
            var centerLon = view != null ? (view.East + view.West) / 2 : _ctx.Stored?.Longitude ?? _ctx.SiteLongitude;
            var geo = MakeGeo(centerLat, centerLon);
            Similarity sim;
            _geNeedsCalibration = false;
            if (markers is { } mk && found.Count >= 2)
            {
                // As 2 maiores manchas magenta; a de noroeste é a mais perto do canto superior esquerdo.
                var two = found.Take(2).OrderBy(m => m.U + m.V).ToList();
                var nw = two[0];
                var se = two[1];
                sim = GoogleEarthCalibration.FromMarkers(geo, (nw.U, nw.V), (se.U, se.V), mk.Nw, mk.Se);
                notes.Add($"Calibração automática pelas 2 miras: ({nw.U:0.0}; {nw.V:0.0}) e ({se.U:0.0}; {se.V:0.0}) px → {1 / sim.Scale:0.0000} m/px, rotação {sim.Rotation * 180 / Math.PI:0.00}°.");
                if (view != null)
                {
                    var bb = GoogleEarthCalibration.FromBbox(geo, view, sw, sh);
                    notes.Add($"Coerência com a área visível do Google Earth: escala {(bb.Scale / sim.Scale - 1) * 100:+0.0;-0.0} % (a vista em perspectiva não é exata; as miras mandam).");
                    if (!view.IsTopDown) notes.Add("⚠ A vista não estava de cima/Norte para cima: a imagem tem distorção de perspectiva.");
                }
            }
            else if (view != null)
            {
                sim = GoogleEarthCalibration.FromBbox(geo, view, sw, sh);
                _geNeedsCalibration = true;
                notes.Add(found.Count < 2
                    ? "⚠ As 2 miras não foram encontradas na imagem (estavam fora da tela ou cobertas?). Escala APROXIMADA pela área visível – confirme com a calibração por 2 pontos que abre em seguida."
                    : "⚠ Sem os marcadores da vista atual: escala aproximada.");
            }
            else throw new SatelliteException("Abra o vínculo com o Google Earth antes (a vista e as miras vêm dele), ou use Importar Imagem Aérea + Calibrar.");
            var result = await Task.Run(() => SatelliteImageBuilder.FromCalibratedImage(geo, px, sw, sh, sim, "Imagem © Google (Google Earth)", "Google Earth Pro", notes, CancellationToken.None));
            _geReady = result;
            _gePreview.Source = SatelliteImageBuilder.Frozen(px, sw, sh);
            _geResult.Text = string.Join("\n", notes) + $"\nÁrea no projeto: {UiHelpers.F(result.Max.X - result.Min.X, "0.0")} × {UiHelpers.F(result.Max.Y - result.Min.Y, "0.0")} m. Clique em \"Gerar e colocar na vista\".";
        }
        catch (SatelliteException ex) { _geResult.Text = ex.Message; }
        catch (Exception ex)
        {
            Log.Error("Imagem aérea – imagem do Google Earth", ex);
            _geResult.Text = "Não foi possível ler a imagem: " + ex.Message;
        }
    }

    // ------------------------------------------------------------------ opção 2: mapa

    private FrameworkElement MapTab()
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        left.Children.Add(new TextBlock { Text = "Localizar (endereço ou \"lat, lon\")", FontWeight = FontWeights.SemiBold });
        var searchRow = new DockPanel();
        var go = new Button { Content = "Buscar", MinWidth = 60 };
        DockPanel.SetDock(go, Dock.Right);
        searchRow.Children.Add(go);
        searchRow.Children.Add(_search);
        left.Children.Add(searchRow);
        left.Children.Add(_places);
        go.Click += async (_, _) => await SearchAsync();
        _search.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await SearchAsync(); } };
        _places.SelectionChanged += (_, _) =>
        {
            if (_places.SelectedItem is ListBoxItem { Tag: GeoPlace p })
            {
                _map.CenterOn(p.Lat, p.Lon, 17);
                _map.SetCrop(p.Lat, p.Lon, 600, 400);
            }
        };

        left.Children.Add(new TextBlock { Text = "Fonte da imagem", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        _source.Items.Add("OpenStreetMap (mapa das ruas – não é foto)");
        var st = PluginContext.Settings;
        _source.Items.Add(SatelliteSource.IsValidTemplate(st.CustomTilesUrl)
            ? SatelliteSource.Custom(st.CustomTilesName, st.CustomTilesAttribution, st.CustomTilesMaxZoom).Nome + " (fonte própria)"
            : "Fonte própria (XYZ) – configure em Configurações");
        _source.Items.Add("WMS – ortofoto (prefeitura, estado, IBGE…)");
        _source.SelectedIndex = st.LastUsed.TryGetValue("aerea:fonte", out var fs) && int.TryParse(fs, out var fi) && fi is >= 0 and <= 2 ? fi : 0;
        left.Children.Add(_source);
        var wmsPanel = new StackPanel();
        wmsPanel.Children.Add(new TextBlock { Text = "Endereço do WMS", Margin = new Thickness(0, 4, 0, 0) });
        _wmsUrl.Text = st.LastUsed.GetValueOrDefault("aerea:wms") ?? "";
        wmsPanel.Children.Add(_wmsUrl);
        var loadLayers = new Button { Content = "Carregar camadas", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        loadLayers.Click += async (_, _) => await LoadLayersAsync();
        wmsPanel.Children.Add(loadLayers);
        wmsPanel.Children.Add(_wmsLayer);
        left.Children.Add(wmsPanel);
        void SourceChanged()
        {
            wmsPanel.Visibility = _source.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            _map.Source = _source.SelectedIndex == 1 && SatelliteSource.IsValidTemplate(st.CustomTilesUrl) ? FonteImagem.Personalizada : FonteImagem.OpenStreetMap;
            _map.MaxZoom = _map.Source == FonteImagem.Personalizada ? st.CustomTilesMaxZoom : 19;
            _map.ReloadTiles();
            UpdateMapInfo();
        }
        _source.SelectionChanged += (_, _) => SourceChanged();

        left.Children.Add(new TextBlock { Text = "Resolução (m/px)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        left.Children.Add(_res);
        _res.TextChanged += (_, _) => UpdateMapInfo();
        var cropBtn = new Button { Content = "Recorte no centro da tela", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        cropBtn.Click += (_, _) => _map.SetCrop(_map.CenterLat, _map.CenterLon, 600, 400);
        left.Children.Add(cropBtn);
        var prev = new Button { Content = "Prévia do recorte", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        prev.Click += async (_, _) => await PreviewAsync();
        left.Children.Add(prev);
        left.Children.Add(_mapInfo);
        left.Children.Add(_mapPreview);
        left.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0),
            Text = "Arraste para mover, roda do mouse para aproximar, clique duplo centraliza. Ajuste o recorte pelos cantos ou arraste-o pelo meio.",
        });
        grid.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var mapBorder = new Border { BorderBrush = Brushes.Silver, BorderThickness = new Thickness(1), Child = _map };
        Grid.SetColumn(mapBorder, 1);
        grid.Children.Add(mapBorder);

        var stored = _ctx.Stored;
        var (lat0, lon0) = stored != null ? stored.ModelToGeo(_ctx.ViewCenter) : (_ctx.SiteLatitude, _ctx.SiteLongitude);
        _map.CenterOn(lat0, lon0, 16);
        _map.SetCrop(lat0, lon0, 600, 400);
        _map.CropChanged += UpdateMapInfo;
        SourceChanged();
        return grid;
    }

    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(_search.Text)) return;
        _places.Items.Clear();
        try
        {
            var list = await SatelliteTiles.SearchAsync(_search.Text, CancellationToken.None);
            foreach (var p in list) _places.Items.Add(new ListBoxItem { Content = p.Name, Tag = p });
            if (list.Count == 0) _places.Items.Add(new ListBoxItem { Content = "Nada encontrado.", IsEnabled = false });
            else _places.SelectedIndex = 0;
        }
        catch (SatelliteException ex) { _places.Items.Add(new ListBoxItem { Content = ex.Message, IsEnabled = false }); }
    }

    private async Task LoadLayersAsync()
    {
        _wmsLayer.Items.Clear();
        try
        {
            var xml = await SatelliteTiles.GetTextAsync(Wms.CapabilitiesUrl(_wmsUrl.Text), "WMS (GetCapabilities)", CancellationToken.None);
            _caps = Wms.ParseCapabilities(xml);
            foreach (var l in _caps.Layers) _wmsLayer.Items.Add(new ComboBoxItem { Content = $"{l.Title} ({l.Name})", Tag = l });
            if (_wmsLayer.Items.Count > 0) _wmsLayer.SelectedIndex = 0;
            PluginContext.Settings.LastUsed["aerea:wms"] = _wmsUrl.Text.Trim();
            _status.Text = $"WMS {_caps.Version}: {_caps.Layers.Count} camada(s).";
        }
        catch (Exception ex) when (ex is SatelliteException or FormatException or System.Xml.XmlException)
        {
            _status.Text = "WMS: " + ex.Message;
        }
    }

    private double Resolution() => Math.Clamp(UiHelpers.ParseOpt(_res.Text) ?? 0.3, 0.05, 20);

    private (GeoReference Geo, Vec2 Center, double W, double H)? MapArea()
    {
        if (_map.CropMeters() is not { } c) return null;
        var geo = MakeGeo(c.Lat, c.Lon);
        return (geo, geo.GeoToModel(c.Lat, c.Lon), Math.Min(c.Width, SatellitePlan.MaxSide), Math.Min(c.Height, SatellitePlan.MaxSide));
    }

    private SatelliteSource? TileSource() => _source.SelectedIndex switch
    {
        0 => SatelliteSource.Osm,
        1 when SatelliteSource.IsValidTemplate(PluginContext.Settings.CustomTilesUrl) =>
            SatelliteSource.Custom(PluginContext.Settings.CustomTilesName, PluginContext.Settings.CustomTilesAttribution, PluginContext.Settings.CustomTilesMaxZoom),
        _ => null,
    };

    private void UpdateMapInfo()
    {
        if (MapArea() is not { } a) { _mapInfo.Text = ""; return; }
        if (TileSource() is { } src)
        {
            var p = SatellitePlan.Create(a.Geo, src, a.Center, a.W, a.H, Resolution());
            _mapInfo.Text = $"{UiHelpers.F(p.Width, "0")} × {UiHelpers.F(p.Height, "0")} m · zoom {p.Zoom} · {UiHelpers.F(p.OutputMpp, "0.00")} m/px · {p.PixelWidth} × {p.PixelHeight} px · {p.TileCount} tiles" +
                            (p.Notes.Count > 0 ? "\n• " + string.Join("\n• ", p.Notes) : "");
        }
        else _mapInfo.Text = _source.SelectedIndex == 2 ? $"{UiHelpers.F(a.W, "0")} × {UiHelpers.F(a.H, "0")} m · {UiHelpers.F(Resolution(), "0.00")} m/px pedidos ao WMS" : "Configure a fonte própria em Configurações.";
    }

    private async Task PreviewAsync()
    {
        if (MapArea() is not { } a || _cts != null) return;
        try
        {
            if (TileSource() is { } src)
            {
                var small = SatellitePlan.Create(a.Geo, src, a.Center, a.W, a.H, Math.Max(a.W, a.H) / 320);
                _mapPreview.Source = await SatelliteImageBuilder.PreviewAsync(small, CancellationToken.None);
            }
            else if (_source.SelectedIndex == 2 && _caps != null && _wmsLayer.SelectedItem is ComboBoxItem { Tag: WmsLayer layer })
            {
                var half = new Vec2(a.W / 2, a.H / 2);
                var r = await SatelliteImageBuilder.FromWmsAsync(a.Geo, a.Center - half, a.Center + half, Math.Max(a.W, a.H) / 320, _wmsUrl.Text, _caps, layer, null, CancellationToken.None);
                _mapPreview.Source = new BitmapImage(new Uri(r.Blocks[0].File));
            }
        }
        catch (SatelliteException ex) { _status.Text = ex.Message; }
    }

    // ------------------------------------------------------------------ gerar

    private async Task GoAsync()
    {
        if (_cts != null) return;
        var mapTab = _tabs.SelectedIndex == 1;
        PluginContext.Settings.LastUsed["aerea:aba"] = mapTab ? "mapa" : "ge";
        PluginContext.Settings.LastUsed["aerea:fonte"] = _source.SelectedIndex.ToString();
        PluginContext.SaveSettings();
        if (!mapTab)
        {
            if (_geReady == null) { UiHelpers.Error("Salve a imagem no Google Earth (ou use \"Usar a imagem salva…\") antes de colocar."); return; }
            Result = _geReady;
            NeedsCalibration = _geNeedsCalibration;
            DialogResult = true;
            return;
        }
        if (MapArea() is not { } a) { UiHelpers.Error("Defina o recorte no mapa."); return; }
        _cts = new CancellationTokenSource();
        _go.IsEnabled = false;
        _cancel.Content = "Parar";
        _bar.Visibility = Visibility.Visible;
        var progress = new Progress<(string Stage, double Fraction)>(p => { _status.Text = p.Stage; _bar.Value = p.Fraction; });
        try
        {
            if (TileSource() is { } src)
                Result = await SatelliteImageBuilder.FromTilesAsync(SatellitePlan.Create(a.Geo, src, a.Center, a.W, a.H, Resolution()), progress, _cts.Token);
            else if (_source.SelectedIndex == 2)
            {
                if (_caps == null || _wmsLayer.SelectedItem is not ComboBoxItem { Tag: WmsLayer layer }) throw new SatelliteException("Carregue as camadas do WMS e escolha uma.");
                var half = new Vec2(a.W / 2, a.H / 2);
                Result = await SatelliteImageBuilder.FromWmsAsync(a.Geo, a.Center - half, a.Center + half, Resolution(), _wmsUrl.Text, _caps, layer, progress, _cts.Token);
            }
            else throw new SatelliteException("Configure a fonte própria em SinalizaBIM → Configurações → Imagem aérea.");
            DialogResult = true;
        }
        catch (OperationCanceledException) { _status.Text = "Cancelado – nada foi criado no projeto."; }
        catch (SatelliteException ex) { _status.Text = ex.Message + " Nada foi criado no projeto."; }
        catch (Exception ex)
        {
            Log.Error("Imagem aérea", ex);
            _status.Text = "Erro: " + ex.Message + " Nada foi criado no projeto.";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (IsLoaded && DialogResult != true)
            {
                _go.IsEnabled = true;
                _cancel.Content = "Cancelar";
                _bar.Visibility = Visibility.Collapsed;
            }
        }
    }
}
