using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Modo de cor dos trechos no mapa do Simulador de Tráfego.</summary>
public enum MapaCor { NivelServico, VolumeCapacidade, Velocidade, Volume, VelocidadeSimulada }

/// <summary>
/// Mapa da rede do Simulador de Tráfego: vias em planta, trechos coloridos por nível de serviço (ou v/c, velocidade,
/// volume), nós com o nível de serviço, semáforos com a fase de cada instante e os veículos da microssimulação.
/// Roda do mouse = zoom no cursor; arrastar = mover; clique = selecionar nó/trecho.
/// </summary>
public sealed class TrafficMap : FrameworkElement
{
    private TrafficResult? _res;
    private SimResult? _sim;
    private double _cx, _cy, _scale = 1;
    private Point? _drag;
    private Point _dragStart;
    private bool _moved;
    private readonly Typeface _face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private readonly Typeface _faceN = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public MapaCor Mode { get; set; } = MapaCor.NivelServico;
    public double SimTime { get; set; } = double.NaN;
    public bool ShowVehicles { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    public int? SelectedNode { get; private set; }
    public int? SelectedLink { get; private set; }
    public Vec2? Marker { get; set; }

    public event Action<int?, int?>? SelectionChanged;

    public TrafficMap()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public void SetData(TrafficResult? res, SimResult? sim)
    {
        var first = _res == null;
        _res = res;
        _sim = sim;
        if (first) FitAll();
        InvalidateVisual();
    }

    public void FitAll()
    {
        if (_res == null || ActualWidth < 10) return;
        var (min, max) = _res.Network.Bounds();
        _cx = (min.X + max.X) / 2;
        _cy = (min.Y + max.Y) / 2;
        var w = Math.Max(20, max.X - min.X + 60);
        var h = Math.Max(20, max.Y - min.Y + 60);
        _scale = Math.Min(ActualWidth / w, ActualHeight / h);
        InvalidateVisual();
    }

    public void ZoomTo(Vec2 p, double radius = 80)
    {
        _cx = p.X;
        _cy = p.Y;
        _scale = Math.Min(ActualWidth, ActualHeight) / (2 * radius);
        Marker = p;
        InvalidateVisual();
    }

    public void Select(int? node, int? link)
    {
        SelectedNode = node;
        SelectedLink = link;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (info.PreviousSize.Width < 10) FitAll();
    }

    private Point S(Vec2 p) => new((p.X - _cx) * _scale + ActualWidth / 2, ActualHeight / 2 - (p.Y - _cy) * _scale);
    private Vec2 W(Point p) => new((p.X - ActualWidth / 2) / _scale + _cx, (ActualHeight / 2 - p.Y) / _scale + _cy);

    // ------------------------------------------------------------------------------------------------ cores
    public static Color LosColor(string los) => los switch
    {
        "A" => Color.FromRgb(0x2E, 0x7D, 0x32),
        "B" => Color.FromRgb(0x7C, 0xB3, 0x42),
        "C" => Color.FromRgb(0xF2, 0xC2, 0x1B),
        "D" => Color.FromRgb(0xFB, 0x8C, 0x00),
        "E" => Color.FromRgb(0xE5, 0x39, 0x35),
        "F" => Color.FromRgb(0x8E, 0x10, 0x10),
        _ => Color.FromRgb(0x9E, 0xA7, 0xB3),
    };

    /// <summary>Rampa verde → amarelo → vermelho para t em [0, 1].</summary>
    public static Color Ramp(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5
            ? Color.FromRgb((byte)(46 + (242 - 46) * t * 2), (byte)(125 + (194 - 125) * t * 2), (byte)(50 - 23 * t * 2))
            : Color.FromRgb((byte)(242 - (242 - 180) * (t - 0.5) * 2), (byte)(194 - 180 * (t - 0.5) * 2), (byte)(27 - 10 * (t - 0.5) * 2));
    }

    private Color LinkColor(LinkResult lr)
    {
        switch (Mode)
        {
            case MapaCor.VolumeCapacidade: return Ramp(lr.X / 1.1);
            case MapaCor.Velocidade: return Ramp(1 - lr.Speed / Math.Max(1, lr.Link.FreeSpeed * 3.6));
            case MapaCor.Volume:
            {
                var max = _res!.Links.Values.Select(x => x.Volume).DefaultIfEmpty(1).Max();
                return Ramp(lr.Volume / Math.Max(1, max));
            }
            case MapaCor.VelocidadeSimulada:
            {
                var v = _sim?.MeanSpeedKmh(lr.Link.Index) ?? double.NaN;
                return double.IsNaN(v) ? LosColor("-") : Ramp(1 - v / Math.Max(1, lr.Link.FreeSpeed * 3.6));
            }
            default:
            {
                // Nível do trecho piorado pelo nível da aproximação no nó de jusante (o que o motorista sente).
                var nr = _res!.Nodes.GetValueOrDefault(lr.Link.To);
                var ap = nr?.Approaches.FirstOrDefault(a => a.Link == lr.Link.Index);
                var los = ap != null && ap.Volume >= 1 && string.CompareOrdinal(ap.LOS, lr.LOS) > 0 ? ap.LOS : lr.LOS;
                return lr.Volume < 1 ? LosColor("-") : LosColor(los);
            }
        }
    }

    private static SolidColorBrush B(Color c, byte a = 255) { var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze(); return b; }
    private static Pen P(Color c, double w, byte a = 255) { var p = new Pen(B(c, a), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }

    private StreamGeometry Line(IReadOnlyList<Vec2> pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(S(pts[0]), false, false);
            c.PolyLineTo(pts.Skip(1).Select(S).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }

    private static List<Vec2> OffsetRight(Polyline2 path, double off, double trim0 = 0, double trim1 = 0)
    {
        var res = new List<Vec2>();
        var L = path.Length;
        var s0 = Math.Min(trim0, L * 0.3);
        var s1 = Math.Max(L - trim1, L * 0.7);
        var n = Math.Max(2, (int)((s1 - s0) / 4) + 1);
        for (int i = 0; i <= n; i++)
        {
            var s = s0 + (s1 - s0) * i / n;
            var p = path.PointAt(s);
            var d = path.TangentAt(s);
            res.Add(p + new Vec2(d.Y, -d.X) * off);
        }
        return res;
    }

    // ------------------------------------------------------------------------------------------------ desenho
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(B(Color.FromRgb(0xEE, 0xF1, 0xF4)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_res == null) return;
        var net = _res.Network;

        // Vias (pista em cinza) e rotatórias.
        foreach (var r in net.Roads)
        {
            var w = Math.Max(2, r.CarriageWidth * _scale);
            dc.DrawGeometry(null, P(Color.FromRgb(0x5A, 0x60, 0x68), w + 2), Line(r.Axis.Points));
            dc.DrawGeometry(null, P(Color.FromRgb(0x78, 0x7F, 0x88), w), Line(r.Axis.Points));
        }
        foreach (var nd in net.Nodes.Where(n => n.Kind == TipoNo.Rotatoria && n.RoundaboutRadius > 2))
        {
            var R = nd.RoundaboutRadius * _scale;
            var ring = Math.Max(3, nd.RoundaboutLanes * 4.5) * _scale;
            dc.DrawEllipse(null, new Pen(B(Color.FromRgb(0x78, 0x7F, 0x88)), ring), S(nd.Pos), R - ring / 2, R - ring / 2);
            dc.DrawEllipse(B(Color.FromRgb(0x8F, 0xB9, 0x6A)), null, S(nd.Pos), Math.Max(1, R - ring), Math.Max(1, R - ring));
        }
        // Faixas de pedestres.
        foreach (var (_, _, path) in net.Crosswalks)
            if (path.Points.Count >= 2) dc.DrawGeometry(null, P(Colors.White, Math.Max(1.5, 3 * _scale), 220), Line(path.Points));

        // Trechos: faixa colorida sobre as faixas do sentido.
        foreach (var lr in _res.Links.Values)
        {
            var l = lr.Link;
            if (l.Length < 1) continue;
            var off = l.Road.TwoWay ? (l.Road.Median ? 1.0 : 0.0) + l.Lanes * l.Road.LaneWidth / 2 : 0;
            var pts = OffsetRight(l.Path, off);
            var width = Math.Max(3, l.Lanes * l.Road.LaneWidth * _scale * (l.Road.TwoWay ? 0.75 : 0.45));
            var sel = SelectedLink == l.Index;
            if (sel) dc.DrawGeometry(null, P(Color.FromRgb(0x1F, 0x5F, 0xA8), width + 6), Line(pts));
            dc.DrawGeometry(null, P(LinkColor(lr), width, (byte)(double.IsNaN(SimTime) ? 235 : 150)), Line(pts));
            // Seta do sentido.
            if (_scale > 0.6 && l.Length * _scale > 60)
            {
                var s = l.Length * 0.5;
                var p = l.Path.PointAt(s);
                var d = l.Path.TangentAt(s);
                var c = p + new Vec2(d.Y, -d.X) * off;
                var a = 1.6 * Math.Max(1.5, l.Road.LaneWidth * 0.5);
                var tip = c + d * a;
                var b1 = c - d * a + new Vec2(-d.Y, d.X) * a * 0.7;
                var b2 = c - d * a - new Vec2(-d.Y, d.X) * a * 0.7;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(S(tip), true, true);
                    ctx.PolyLineTo(new[] { S(b1), S(b2) }, true, true);
                }
                g.Freeze();
                dc.DrawGeometry(B(Colors.White, 200), null, g);
            }
        }

        // Faixas auxiliares (desaceleração/aceleração) e pontos de ônibus (baia = quadrado vazado; na faixa = cheio).
        foreach (var l in net.Links)
        {
            double Edge() => l.LaneOffset(0) + l.Road.LaneWidth * 0.5 + 1.6;
            List<Vec2> Band(double a, double b) =>
                Enumerable.Range(0, 9).Select(i => { var st = a + (b - a) * i / 8; var d = l.Path.TangentAt(st); return l.Path.PointAt(st) + new Vec2(d.Y, -d.X) * Edge(); }).ToList();
            if (l.DecelLane > 1) dc.DrawGeometry(null, P(Color.FromRgb(0x6A, 0x1B, 0x9A), Math.Max(2, 2.5 * _scale), 200), Line(Band(Math.Max(0, l.Length - l.DecelLane), l.Length)));
            if (l.AccelLane > 1) dc.DrawGeometry(null, P(Color.FromRgb(0x00, 0x83, 0x8F), Math.Max(2, 2.5 * _scale), 200), Line(Band(0, Math.Min(l.Length, l.AccelLane))));
            foreach (var (at, bay, _) in l.BusStops)
            {
                var d = l.Path.TangentAt(at);
                var p = S(l.Path.PointAt(at) + new Vec2(d.Y, -d.X) * (Edge() + (bay ? 1.5 : 0)));
                var h = Math.Max(4, 2.2 * _scale);
                dc.DrawRectangle(bay ? B(Colors.White) : B(Color.FromRgb(0x1F, 0x5F, 0xA8)), P(Color.FromRgb(0x1F, 0x5F, 0xA8), 1.5), new Rect(p.X - h, p.Y - h, 2 * h, 2 * h));
            }
        }
        foreach (var (_, pos) in net.EscapeRamps)
        {
            var p = S(pos);
            dc.DrawRectangle(B(Color.FromRgb(0xB0, 0xA0, 0x80)), P(Color.FromRgb(0xC6, 0x28, 0x28), 2), new Rect(p.X - 6, p.Y - 6, 12, 12));
        }

        // Semáforos (estado no instante da animação ou verde/vermelho estático).
        SimFrame? frame = null;
        if (_sim != null && !double.IsNaN(SimTime) && _sim.Frames.Count > 0) frame = FrameAt(SimTime).A;
        foreach (var nr in _res.Nodes.Values.Where(n => n.Control == ControleNo.Semaforo && n.Phases.Count > 0))
        {
            var state = frame != null && frame.SignalPhase.TryGetValue(nr.Node.Index, out var st) ? st : int.MinValue;
            foreach (var li in nr.Node.In)
            {
                var l = net.Links[li];
                var d = l.Path.TangentAt(l.Length);
                var p = l.Path.PointAt(l.Length) + new Vec2(d.Y, -d.X) * (l.LaneOffset(0) + l.Road.LaneWidth * 0.5 + 0.8);
                Color c;
                if (state == int.MinValue) c = Color.FromRgb(0x44, 0x44, 0x44);
                else if (state >= 0)
                {
                    var ph = nr.Phases[state];
                    c = ph.Links.Contains(li) ? (ph.LeftOnly ? Color.FromRgb(0x00, 0xB8, 0xD4) : Color.FromRgb(0x00, 0xC8, 0x53)) : Color.FromRgb(0xE5, 0x39, 0x35);
                }
                else
                {
                    var k = -state - 2;
                    c = k >= 0 && k < nr.Phases.Count && nr.Phases[k].Links.Contains(li) ? Color.FromRgb(0xFF, 0xB3, 0x00) : Color.FromRgb(0xE5, 0x39, 0x35);
                }
                var r = Math.Max(3, 1.2 * _scale);
                dc.DrawEllipse(B(Color.FromRgb(0x20, 0x20, 0x20)), null, S(p), r + 1.5, r + 1.5);
                dc.DrawEllipse(B(c), null, S(p), r, r);
            }
        }

        // Veículos.
        if (ShowVehicles && _sim != null && !double.IsNaN(SimTime) && _sim.Frames.Count > 0) DrawVehicles(dc);

        // Nós.
        foreach (var nd in net.Nodes.Where(n => n.Kind != TipoNo.Continuacao))
        {
            var nr = _res.Nodes.GetValueOrDefault(nd.Index);
            var pt = S(nd.Pos);
            if (nd.IsZone)
            {
                var z = 5.0;
                dc.DrawRectangle(B(Color.FromRgb(0x3A, 0x44, 0x52)), P(Colors.White, 1.5), new Rect(pt.X - z, pt.Y - z, 2 * z, 2 * z));
                if (ShowLabels) Text(dc, nd.Label, pt + new Vector(8, -8), 11, Color.FromRgb(0x3A, 0x44, 0x52), false);
                continue;
            }
            var los = nr?.LOS ?? "-";
            var rad = SelectedNode == nd.Index ? 13 : 11;
            dc.DrawEllipse(B(LosColor(los)), P(SelectedNode == nd.Index ? Color.FromRgb(0x1F, 0x5F, 0xA8) : Colors.White, SelectedNode == nd.Index ? 3 : 2), pt, rad, rad);
            Text(dc, los, pt, 12, los is "C" ? Colors.Black : Colors.White, true, true);
            if (ShowLabels)
            {
                var sub = nr == null ? "" : nr.Volume < 1 ? "" : $" {nr.Delay:0} s";
                Text(dc, nd.Label + sub, pt + new Vector(rad + 3, -rad - 4), 11, Color.FromRgb(0x22, 0x2A, 0x33), false);
            }
        }

        if (Marker is { } m)
        {
            var p = S(m);
            dc.DrawEllipse(null, P(Color.FromRgb(0x1F, 0x5F, 0xA8), 2.5), p, 20, 20);
            dc.DrawEllipse(null, P(Colors.White, 1), p, 22, 22);
        }

        DrawScaleBar(dc);
        DrawLegend(dc);
        if (!double.IsNaN(SimTime))
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, SimTime));
            var label = SimTime < 0 ? "aquecimento" : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
            var n = frame?.Count ?? 0;
            Text(dc, $"⏱ {label}   🚗 {n} veículos", new Point(12, 12), 13, Color.FromRgb(0x22, 0x2A, 0x33), false);
        }
    }

    private (SimFrame A, SimFrame B, double U) FrameAt(double t)
    {
        var fr = _sim!.Frames;
        // Quadros a cada 1 s, começando em Time0.
        var t0 = fr[0].Time;
        var i = (int)Math.Floor(t - t0);
        i = Math.Clamp(i, 0, fr.Count - 1);
        var j = Math.Min(fr.Count - 1, i + 1);
        var u = Math.Clamp(t - fr[i].Time, 0, 1);
        return (fr[i], fr[j], u);
    }

    private void DrawVehicles(DrawingContext dc)
    {
        var (a, b, u) = FrameAt(SimTime);
        var next = new Dictionary<int, int>(b.Count);
        for (int k = 0; k < b.Count; k++) next[(int)b.Data[k * SimFrame.Stride + 5]] = k;
        var buckets = new StreamGeometry[6];
        var ctxs = new StreamGeometryContext[6];
        for (int k = 0; k < 6; k++) { buckets[k] = new StreamGeometry(); ctxs[k] = buckets[k].Open(); }
        const int st = SimFrame.Stride;
        for (int k = 0; k < a.Count; k++)
        {
            var x = a.Data[k * st];
            var y = a.Data[k * st + 1];
            var h = a.Data[k * st + 2];
            var type = (int)a.Data[k * st + 3];
            var v = a.Data[k * st + 4];
            var id = (int)a.Data[k * st + 5];
            if (next.TryGetValue(id, out var j))
            {
                var x2 = b.Data[j * st];
                var y2 = b.Data[j * st + 1];
                if (Math.Abs(x2 - x) + Math.Abs(y2 - y) < 40)
                {
                    x += (float)((x2 - x) * u);
                    y += (float)((y2 - y) * u);
                    var h2 = b.Data[j * st + 2];
                    var dh = h2 - h;
                    while (dh > Math.PI) dh -= (float)(2 * Math.PI);
                    while (dh < -Math.PI) dh += (float)(2 * Math.PI);
                    h += (float)(dh * u);
                    v += (float)((b.Data[j * st + 4] - v) * u);
                }
            }
            var len = type == 0 ? 4.4 : 11.5;
            var wid = type == 0 ? 1.8 : 2.5;
            var d = new Vec2(Math.Cos(h), Math.Sin(h));
            var n = new Vec2(-d.Y, d.X);
            var c = new Vec2(x, y) - d * (len / 2);
            var p0 = c + n * (wid / 2);
            var p1 = c + d * len + n * (wid / 2);
            var p2 = c + d * len - n * (wid / 2);
            var p3 = c - n * (wid / 2);
            // Cor: carro pela velocidade (parado = vermelho); caminhão cinza-escuro; ônibus azul.
            var bucket = type == 1 ? 4 : type == 2 ? 5 : v < 1.0 ? 0 : v < 4 ? 1 : v < 9 ? 2 : 3;
            var ctx = ctxs[bucket];
            ctx.BeginFigure(S(p0), true, true);
            ctx.PolyLineTo(new[] { S(p1), S(p2), S(p3) }, true, false);
        }
        var colors = new[]
        {
            Color.FromRgb(0xD3, 0x2F, 0x2F), Color.FromRgb(0xF5, 0x7C, 0x00), Color.FromRgb(0xFB, 0xC0, 0x2D), Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0x42, 0x47, 0x4F), Color.FromRgb(0x15, 0x65, 0xC0),
        };
        var outline = P(Color.FromRgb(0x1B, 0x1F, 0x24), Math.Clamp(_scale * 0.15, 0.5, 1.2));
        for (int k = 0; k < 6; k++)
        {
            ctxs[k].Close();
            buckets[k].Freeze();
            dc.DrawGeometry(B(colors[k]), outline, buckets[k]);
        }
    }

    private void DrawScaleBar(DrawingContext dc)
    {
        var target = ActualWidth * 0.18 / _scale;
        var nice = new[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 }.LastOrDefault(v => v <= target);
        if (nice == 0) nice = 1;
        var px = nice * _scale;
        var y = ActualHeight - 18;
        var x0 = 14.0;
        dc.DrawRectangle(B(Colors.White, 200), null, new Rect(x0 - 6, y - 18, px + 60, 26));
        dc.DrawLine(P(Color.FromRgb(0x22, 0x2A, 0x33), 2), new Point(x0, y), new Point(x0 + px, y));
        dc.DrawLine(P(Color.FromRgb(0x22, 0x2A, 0x33), 2), new Point(x0, y - 5), new Point(x0, y + 3));
        dc.DrawLine(P(Color.FromRgb(0x22, 0x2A, 0x33), 2), new Point(x0 + px, y - 5), new Point(x0 + px, y + 3));
        Text(dc, $"{nice} m", new Point(x0 + px + 6, y - 9), 11, Color.FromRgb(0x22, 0x2A, 0x33), false);
    }

    private void DrawLegend(DrawingContext dc)
    {
        var items = Mode == MapaCor.NivelServico
            ? new[] { "A", "B", "C", "D", "E", "F" }.Select(l => (LosColor(l), $"{l} – {TrafficReport.LosMeaning(l)}")).ToList()
            : Mode switch
            {
                MapaCor.VolumeCapacidade => new List<(Color, string)> { (Ramp(0), "v/c < 0,3"), (Ramp(0.45), "v/c 0,5"), (Ramp(0.75), "v/c 0,8"), (Ramp(0.9), "v/c 1,0"), (Ramp(1), "v/c > 1,1") },
                MapaCor.Volume => new List<(Color, string)> { (Ramp(0), "baixo"), (Ramp(0.5), "médio"), (Ramp(1), "maior volume da rede") },
                _ => new List<(Color, string)> { (Ramp(0), "velocidade livre"), (Ramp(0.5), "metade da livre"), (Ramp(1), "parado") },
            };
        var w = 190.0;
        var h = 12 + items.Count * 18;
        var x = ActualWidth - w - 10;
        var y = ActualHeight - h - 10;
        dc.DrawRoundedRectangle(B(Colors.White, 225), P(Color.FromRgb(0xD5, 0xDB, 0xE3), 1), new Rect(x, y, w, h), 4, 4);
        for (int i = 0; i < items.Count; i++)
        {
            dc.DrawRoundedRectangle(B(items[i].Item1), null, new Rect(x + 8, y + 7 + i * 18, 22, 11), 2, 2);
            Text(dc, items[i].Item2, new Point(x + 36, y + 4 + i * 18), 11, Color.FromRgb(0x22, 0x2A, 0x33), false);
        }
    }

    private void Text(DrawingContext dc, string s, Point p, double size, Color c, bool bold, bool center = false)
    {
        var ft = new FormattedText(s, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight, bold ? _face : _faceN, size, B(c),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (center) p = new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2);
        else if (!bold)
        {
            // Fundo claro atrás dos rótulos.
            dc.DrawRoundedRectangle(B(Colors.White, 190), null, new Rect(p.X - 2, p.Y, ft.Width + 4, ft.Height), 2, 2);
        }
        dc.DrawText(ft, p);
    }

    // ------------------------------------------------------------------------------------------------ mouse
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var before = W(p);
        _scale *= e.Delta > 0 ? 1.2 : 1 / 1.2;
        _scale = Math.Clamp(_scale, 0.02, 60);
        var after = W(p);
        _cx += before.X - after.X;
        _cy += before.Y - after.Y;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _drag = e.GetPosition(this);
        _dragStart = _drag.Value;
        _moved = false;
        CaptureMouse();
        Focus();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag is not { } d) return;
        var p = e.GetPosition(this);
        if ((p - _dragStart).Length > 3) _moved = true;
        _cx -= (p.X - d.X) / _scale;
        _cy += (p.Y - d.Y) / _scale;
        _drag = p;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        var p = e.GetPosition(this);
        _drag = null;
        if (!_moved) HitTest(p);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        FitAll();
        e.Handled = true;
    }

    private void HitTest(Point p)
    {
        if (_res == null) return;
        var net = _res.Network;
        var best = net.Nodes.Where(n => n.Kind != TipoNo.Continuacao).Select(n => (n, d: (S(n.Pos) - p).Length)).Where(x => x.d < 14).OrderBy(x => x.d).FirstOrDefault();
        if (best.n != null)
        {
            Select(best.n.Index, null);
            SelectionChanged?.Invoke(best.n.Index, null);
            return;
        }
        var w = W(p);
        TrafficLink? bl = null;
        var bd = double.MaxValue;
        foreach (var l in net.Links)
        {
            var (st, signed) = l.Path.Project(w);
            if (st < 0 || st > l.Length) continue;
            var right = -signed;       // positivo à direita do sentido
            var off = l.Road.TwoWay ? l.Lanes * l.Road.LaneWidth / 2 : 0;
            var dd = Math.Abs(right - off);
            if (l.Road.TwoWay && right < -0.5) continue;
            if (dd < bd) { bd = dd; bl = l; }
        }
        if (bl != null && bd * _scale < 14 + bl.Lanes * bl.Road.LaneWidth * _scale / 2)
        {
            Select(null, bl.Index);
            SelectionChanged?.Invoke(null, bl.Index);
        }
        else
        {
            Select(null, null);
            SelectionChanged?.Invoke(null, null);
        }
    }
}
