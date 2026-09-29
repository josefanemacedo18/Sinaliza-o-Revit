using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SinalizacaoViaria.Core.Geo;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Mapa de tiles (OpenStreetMap ou fonte própria) para localizar a área: arrastar move, roda do mouse aproxima no ponto do
/// cursor, clique duplo centraliza. O recorte é um retângulo com alças (mover pelo meio, redimensionar pelos cantos) e mostra
/// a largura × altura em metros reais (TM local).
/// </summary>
public sealed class TileMapControl : FrameworkElement
{
    private const int Ts = WebMercator.TileSize;
    private readonly Dictionary<(FonteImagem, int, long, long), ImageSource?> _tiles = new();
    private readonly HashSet<(FonteImagem, int, long, long)> _loading = new();
    private Point? _drag;
    private int _dragMode;              // 0 mover mapa, 1 mover recorte, 2..5 cantos do recorte
    private CancellationTokenSource _cts = new();

    public FonteImagem Source { get; set; } = FonteImagem.OpenStreetMap;
    public double CenterLat { get; private set; } = -15.7939;
    public double CenterLon { get; private set; } = -47.8828;
    public double Zoom { get; private set; } = 16;
    public int MaxZoom { get; set; } = 19;

    /// <summary>Recorte (graus): norte, sul, leste, oeste.</summary>
    public (double N, double S, double E, double W)? Crop { get; private set; }

    /// <summary>Mudou o recorte (fim do arrasto ou novo).</summary>
    public event Action? CropChanged;

    public TileMapControl()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.SizeAll;
    }

    public void CenterOn(double lat, double lon, double? zoom = null)
    {
        CenterLat = lat;
        CenterLon = lon;
        if (zoom is { } z) Zoom = Math.Clamp(z, 2, MaxZoom);
        InvalidateVisual();
    }

    /// <summary>Recorte de largura × altura (m) centrado no ponto.</summary>
    public void SetCrop(double lat, double lon, double widthM, double heightM)
    {
        var tm = new LocalTransverseMercator(lat, lon);
        var (n, _) = tm.Inverse(0, heightM / 2);
        var (s, _) = tm.Inverse(0, -heightM / 2);
        var (_, e) = tm.Inverse(widthM / 2, 0);
        var (_, w) = tm.Inverse(-widthM / 2, 0);
        Crop = (n, s, e, w);
        InvalidateVisual();
        CropChanged?.Invoke();
    }

    /// <summary>Centro e tamanho (m, TM local) do recorte.</summary>
    public (double Lat, double Lon, double Width, double Height)? CropMeters()
    {
        if (Crop is not { } c) return null;
        var lat = (c.N + c.S) / 2;
        var lon = (c.E + c.W) / 2;
        var tm = new LocalTransverseMercator(lat, lon);
        var (xe, _) = tm.Forward(lat, c.E);
        var (xw, _) = tm.Forward(lat, c.W);
        var (_, yn) = tm.Forward(c.N, lon);
        var (_, ys) = tm.Forward(c.S, lon);
        return (lat, lon, Math.Abs(xe - xw), Math.Abs(yn - ys));
    }

    public void ReloadTiles()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _tiles.Clear();
        _loading.Clear();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ coordenadas

    private int Level => (int)Math.Floor(Zoom);
    private double Factor => Math.Pow(2, Zoom - Level);

    private Point ToScreen(double lat, double lon)
    {
        var (cx, cy) = WebMercator.ToPixel(CenterLat, CenterLon, Level);
        var (x, y) = WebMercator.ToPixel(lat, lon, Level);
        return new Point(ActualWidth / 2 + (x - cx) * Factor, ActualHeight / 2 + (y - cy) * Factor);
    }

    private (double Lat, double Lon) ToGeo(Point p)
    {
        var (cx, cy) = WebMercator.ToPixel(CenterLat, CenterLon, Level);
        return WebMercator.FromPixel(cx + (p.X - ActualWidth / 2) / Factor, cy + (p.Y - ActualHeight / 2) / Factor, Level);
    }

    // ------------------------------------------------------------------ desenho

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE4)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var z = Level;
        var (cx, cy) = WebMercator.ToPixel(CenterLat, CenterLon, z);
        var f = Factor;
        var x0 = cx - ActualWidth / 2 / f;
        var y0 = cy - ActualHeight / 2 / f;
        var tx0 = (long)Math.Floor(x0 / Ts);
        var ty0 = (long)Math.Floor(y0 / Ts);
        var tx1 = (long)Math.Floor((x0 + ActualWidth / f) / Ts);
        var ty1 = (long)Math.Floor((y0 + ActualHeight / f) / Ts);
        var n = 1L << z;
        for (var ty = Math.Max(0, ty0); ty <= Math.Min(n - 1, ty1); ty++)
            for (var tx = tx0; tx <= tx1; tx++)
            {
                var wx = ((tx % n) + n) % n;
                var img = Tile(z, wx, ty);
                var r = new Rect((tx * Ts - x0) * f, (ty * Ts - y0) * f, Ts * f + 0.5, Ts * f + 0.5);
                if (img != null) dc.DrawImage(img, r);
            }
        if (Crop is { } c)
        {
            var a = ToScreen(c.N, c.W);
            var b = ToScreen(c.S, c.E);
            var rect = new Rect(a, b);
            var shade = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), new RectangleGeometry(rect));
            dc.DrawGeometry(shade, null, outside);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)), 2), rect);
            foreach (var h in Handles(rect)) dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), new Rect(h.X - 5, h.Y - 5, 10, 10));
            if (CropMeters() is { } m)
            {
                var t = new FormattedText($"{UiHelpers.F(m.Width, "0")} × {UiHelpers.F(m.Height, "0")} m", UiHelpers.PtBr, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 13, Brushes.White, 1.0);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), null, new Rect(rect.Left, rect.Top - 22, t.Width + 10, 20));
                dc.DrawText(t, new Point(rect.Left + 5, rect.Top - 20));
            }
        }
        var credit = new FormattedText(Source == FonteImagem.OpenStreetMap ? "© OpenStreetMap contributors" : "Fonte própria", UiHelpers.PtBr,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Black, 1.0);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), null, new Rect(ActualWidth - credit.Width - 8, ActualHeight - 18, credit.Width + 8, 18));
        dc.DrawText(credit, new Point(ActualWidth - credit.Width - 4, ActualHeight - 17));
    }

    private static Point[] Handles(Rect r) => new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft };

    private ImageSource? Tile(int z, long x, long y)
    {
        var key = (Source, z, x, y);
        if (_tiles.TryGetValue(key, out var img)) return img;
        if (_loading.Add(key))
        {
            var ct = _cts.Token;
            var src = Source;
            _ = Task.Run(async () =>
            {
                string? file = null;
                try { file = await SatelliteTiles.TileAsync(src, z, x, y, ct); }
                catch { /* sem tile: fica vazio */ }
                ImageSource? bmp = null;
                if (file != null)
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.UriSource = new Uri(file);
                        bi.EndInit();
                        bi.Freeze();
                        bmp = bi;
                    }
                    catch { }
                await Dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    _tiles[key] = bmp;
                    InvalidateVisual();
                });
            }, ct);
        }
        // Enquanto carrega: o tile do nível de cima, ampliado, se já estiver na memória.
        return null;
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var before = ToGeo(p);
        Zoom = Math.Clamp(Zoom + (e.Delta > 0 ? 0.5 : -0.5), 2, MaxZoom);
        // Mantém o ponto sob o cursor no mesmo lugar.
        var after = ToGeo(p);
        CenterLat += before.Lat - after.Lat;
        CenterLon += before.Lon - after.Lon;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (e.ClickCount == 2)
        {
            (CenterLat, CenterLon) = ToGeo(p);
            InvalidateVisual();
            return;
        }
        _dragMode = 0;
        if (Crop is { } c)
        {
            var rect = new Rect(ToScreen(c.N, c.W), ToScreen(c.S, c.E));
            var hs = Handles(rect);
            for (int i = 0; i < 4; i++)
                if ((hs[i] - p).Length < 10) _dragMode = 2 + i;
            if (_dragMode == 0 && rect.Contains(p)) _dragMode = 1;
        }
        _drag = p;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        var d = p - start;
        _drag = p;
        if (_dragMode == 0)
        {
            var (cx, cy) = WebMercator.ToPixel(CenterLat, CenterLon, Level);
            (CenterLat, CenterLon) = WebMercator.FromPixel(cx - d.X / Factor, cy - d.Y / Factor, Level);
        }
        else if (Crop is { } c)
        {
            var nw = ToScreen(c.N, c.W);
            var se = ToScreen(c.S, c.E);
            switch (_dragMode)
            {
                case 1: nw += d; se += d; break;
                case 2: nw += d; break;
                case 3: nw.Y += d.Y; se.X += d.X; break;
                case 4: se += d; break;
                case 5: nw.X += d.X; se.Y += d.Y; break;
            }
            if (se.X - nw.X > 10 && se.Y - nw.Y > 10)
            {
                var (n, w) = ToGeo(nw);
                var (s, ea) = ToGeo(se);
                Crop = (n, s, ea, w);
            }
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag != null && _dragMode != 0) CropChanged?.Invoke();
        _drag = null;
        ReleaseMouseCapture();
    }
}
