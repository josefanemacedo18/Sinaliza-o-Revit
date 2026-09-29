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
    /// <summary>Veículos coloridos pela velocidade (senão, cores reais de carros, ônibus e caminhões).</summary>
    public bool ColorBySpeed { get; set; }
    /// <summary>Mostra os ícones da sinalização lida (PARE, dê a preferência, velocidade, bloqueios).</summary>
    public bool ShowSigns { get; set; } = true;
    public int? SelectedNode { get; private set; }
    public int? SelectedLink { get; private set; }
    public Vec2? Marker { get; set; }

    public event Action<int?, int?>? SelectionChanged;
    /// <summary>Modo "escolher ponto": o clique devolve o ponto do projeto em vez de selecionar.</summary>
    public bool PickMode { get; set; }
    public event Action<Vec2>? PointPicked;

    /// <summary>Desenha a planta real do projeto (pisos e pintura gerados pelo plugin); senão, o esquema pelo eixo.</summary>
    public bool RealPlan { get; set; } = true;
    private TrafficNetwork? _planFor;
    private DrawingGroup? _planBase, _planPaint;

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

    // ------------------------------------------------------------------------------------------------ planta real
    /// <summary>
    /// Monta (uma vez por rede) os desenhos da planta em coordenadas do projeto: base (pisos, calçadas, canteiros e elementos
    /// físicos) e pintura. Peças seguidas da mesma cor viram uma só geometria (ordem de pintura preservada).
    /// </summary>
    private void EnsurePlan(TrafficNetwork net)
    {
        if (ReferenceEquals(_planFor, net)) return;
        _planFor = net;
        _planBase = _planPaint = null;
        if (net.Backdrop.Count == 0) return;
        var baseG = new DrawingGroup();
        var paintG = new DrawingGroup();
        using (var bc = baseG.Open())
        using (var pc = paintG.Open())
        {
            var i = 0;
            var list = net.Backdrop;
            while (i < list.Count)
            {
                var first = list[i];
                var g = new StreamGeometry { FillRule = FillRule.Nonzero };
                var j = i;
                using (var ctx = g.Open())
                {
                    for (; j < list.Count && list[j].Layer == first.Layer && list[j].Color == first.Color && j - i < 4000; j++)
                    {
                        var sh = list[j].Shape;
                        Ring(ctx, sh.Outer, true);
                        foreach (var h in sh.Holes) Ring(ctx, h, false);
                    }
                }
                g.Freeze();
                var brush = B(Color.FromRgb(first.Color.R, first.Color.G, first.Color.B));
                // Meio-fio e bordas de calçada/canteiro com um contorno fino (leitura do desnível, como numa foto aérea).
                Pen? pen = first.Layer is CamadaMapa.Calcada or CamadaMapa.Canteiro or CamadaMapa.Fisico ? P(Color.FromRgb(0x9A, 0x95, 0x8A), 0.08) : null;
                (first.Layer == CamadaMapa.Pintura ? pc : bc).DrawGeometry(brush, pen, g);
                i = j;
            }
        }
        baseG.Freeze();
        paintG.Freeze();
        _planBase = baseG;
        _planPaint = paintG;

        static void Ring(StreamGeometryContext ctx, IReadOnlyList<Vec2> r, bool outer)
        {
            if (r.Count < 3) return;
            double a = 0;
            for (int k = 0; k < r.Count; k++) { var p = r[k]; var q = r[(k + 1) % r.Count]; a += p.X * q.Y - q.X * p.Y; }
            // Contorno anti-horário e furos horários: com a regra "nonzero" os furos ficam vazios e peças vizinhas da mesma
            // cor não se anulam.
            var ccw = a > 0;
            IEnumerable<Vec2> pts = ccw == outer ? r : r.Reverse();
            var l = pts.ToList();
            ctx.BeginFigure(new Point(l[0].X, l[0].Y), true, true);
            ctx.PolyLineTo(l.Skip(1).Select(v => new Point(v.X, v.Y)).ToList(), false, true);
        }
    }

    /// <summary>Transformação projeto → tela (y para cima no projeto).</summary>
    private MatrixTransform WorldTransform() =>
        new(new Matrix(_scale, 0, 0, -_scale, ActualWidth / 2 - _cx * _scale, ActualHeight / 2 + _cy * _scale));

    // ------------------------------------------------------------------------------------------------ desenho
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(B(Color.FromRgb(0xE6, 0xEA, 0xE0)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_res == null) return;
        var net = _res.Network;
        var animating = !double.IsNaN(SimTime) && ShowVehicles && _sim != null;
        var detail = _scale > 0.9;                               // marcas no pavimento só com zoom suficiente

        // Afastamento livre de cada nó (as marcas de faixa param antes do cruzamento).
        var clear = net.Nodes.ToDictionary(n => n.Index, n => n.IsZone || n.Kind == TipoNo.Continuacao ? 0.0
            : n.Kind == TipoNo.Rotatoria ? n.RoundaboutRadius + 2 : n.RoadIds.Select(id => net.Road(id)).Where(r => r != null).Select(r => Math.Max(r!.TotalLeft, r.TotalRight)).DefaultIfEmpty(8).Max() + 1.5);
        List<Vec2> Poly(Polyline2 path, double rightLo, double rightHi, double trim0 = 0, double trim1 = 0)
        {
            var a = OffsetRight(path, rightLo, trim0, trim1);
            var b = OffsetRight(path, rightHi, trim0, trim1);
            b.Reverse();
            a.AddRange(b);
            return a;
        }
        void Fill(List<Vec2> pts, Color c, Pen? pen = null)
        {
            if (pts.Count < 3) return;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(S(pts[0]), true, true);
                ctx.PolyLineTo(pts.Skip(1).Select(S).ToList(), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(B(c), pen, g);
        }

        EnsurePlan(net);
        var real = RealPlan && _planBase != null;
        if (real)
        {
            dc.PushTransform(WorldTransform());
            dc.DrawDrawing(_planBase);
            if (_scale > 0.35) dc.DrawDrawing(_planPaint);
            dc.Pop();
        }

        // Esquema (projetos sem a geometria das peças): calçadas (com o meio-fio como contorno), pista e canteiro de cada via.
        if (!real)
        {
        var walk = Color.FromRgb(0xD8, 0xD5, 0xCD);
        var curb = P(Color.FromRgb(0xA9, 0xA4, 0x98), Math.Max(0.6, 0.15 * _scale));
        var asphalt = Color.FromRgb(0x3E, 0x42, 0x48);
        foreach (var r in net.Roads)
        {
            var tr = Math.Max(r.TotalRight, r.RightWidth);
            var tl = Math.Max(r.TotalLeft, r.LeftWidth);
            Fill(Poly(r.Axis, -tl, tr), walk);
        }
        foreach (var nd in net.Nodes.Where(n => n.Kind == TipoNo.Rotatoria && n.RoundaboutRadius > 2))
            dc.DrawEllipse(B(walk), null, S(nd.Pos), (nd.RoundaboutRadius + 3) * _scale, (nd.RoundaboutRadius + 3) * _scale);
        foreach (var r in net.Roads) Fill(Poly(r.Axis, -r.LeftWidth, r.RightWidth), asphalt, curb);
        foreach (var nd in net.Nodes.Where(n => !n.IsZone && n.Kind is TipoNo.Intersecao or TipoNo.CruzamentoSemControle))
        {
            // Miolo do cruzamento sem os contornos das pistas por cima: cada via recebe asfalto (sem meio-fio) na largura das outras.
            var rs = nd.RoadIds.Select(id => net.Road(id)).Where(x => x != null).Cast<TrafficRoad>().ToList();
            foreach (var r in rs)
            {
                var reach = rs.Where(o => o != r).Select(o => Math.Max(o.LeftWidth, o.RightWidth)).DefaultIfEmpty(0).Max() + 0.3;
                if (reach < 0.5) continue;
                var st = r.Axis.Project(nd.Pos).Station;
                var sub = r.Axis.SubPoints(Math.Max(0, st - reach), Math.Min(r.Axis.Length, st + reach));
                if (sub.Count >= 2) Fill(Poly(new Polyline2(sub), -r.LeftWidth + 0.05, r.RightWidth - 0.05), asphalt);
            }
        }
        foreach (var nd in net.Nodes.Where(n => n.Kind == TipoNo.Rotatoria && n.RoundaboutRadius > 2))
        {
            var R = nd.RoundaboutRadius * _scale;
            var ring = Math.Max(3, nd.RoundaboutLanes * 4.5) * _scale;
            dc.DrawEllipse(B(asphalt), curb, S(nd.Pos), R, R);
            dc.DrawEllipse(B(Color.FromRgb(0x86, 0xB0, 0x6A)), curb, S(nd.Pos), Math.Max(1, R - ring), Math.Max(1, R - ring));
            if (detail) dc.DrawEllipse(null, new Pen(B(Colors.White, 200), Math.Max(0.6, 0.12 * _scale)) { DashStyle = new DashStyle(new double[] { 10, 12 }, 0) }, S(nd.Pos), R - ring / 2, R - ring / 2);
        }
        foreach (var r in net.Roads.Where(r => r.MedianWidth > 0.2 && r.TwoWay))
        {
            var first = net.LinksOf(r).FirstOrDefault();
            foreach (var lk in net.LinksOf(r).Where(l => l.Forward))
                Fill(Poly(lk.Path, -r.MedianWidth / 2, r.MedianWidth / 2, clear[lk.From], clear[lk.To]), Color.FromRgb(0x86, 0xB0, 0x6A), curb);
            _ = first;
        }
        }

        // Nível de serviço: faixa colorida sobre as faixas de cada sentido (na animação, só uma fita junto ao bordo).
        foreach (var lr in _res.Links.Values)
        {
            var l = lr.Link;
            if (l.Length < 1) continue;
            var inner = l.Road.TwoWay ? (l.Road.Median ? Math.Max(1.0, l.Road.MedianWidth / 2) : 0.0) : -l.Lanes * l.Road.LaneWidth / 2;
            var outer = inner + l.Lanes * l.Road.LaneWidth;
            var color = LinkColor(lr);
            var sel = SelectedLink == l.Index;
            var t0 = clear[l.From] * 0.6;
            var t1 = clear[l.To] * 0.6;
            if (animating)
            {
                Fill(Poly(l.Path, outer - 0.9, outer - 0.25, t0, t1), color);
                if (sel) Fill(Poly(l.Path, inner + 0.2, outer - 0.2, t0, t1), Color.FromArgb(70, 0x1F, 0x5F, 0xA8));
            }
            else
            {
                var c = Color.FromArgb(sel ? (byte)235 : real ? (byte)120 : (byte)190, color.R, color.G, color.B);
                Fill(Poly(l.Path, inner + 0.2, outer - 0.2, t0, t1), c, sel ? P(Color.FromRgb(0x1F, 0x5F, 0xA8), 2.5) : null);
            }
            if (l.Closed)
            {
                // Trecho fechado: hachura vermelha.
                var mid = l.Path.PointAt(l.Length / 2);
                var d = l.Path.TangentAt(l.Length / 2);
                var n = new Vec2(d.Y, -d.X);
                var cm = mid + n * ((inner + outer) / 2);
                var h = Math.Max(5, (outer - inner) * _scale / 2);
                var pc = S(cm);
                var red = P(Color.FromRgb(0xD3, 0x2F, 0x2F), Math.Max(2, h * 0.35));
                dc.DrawLine(red, new Point(pc.X - h, pc.Y - h), new Point(pc.X + h, pc.Y + h));
                dc.DrawLine(red, new Point(pc.X - h, pc.Y + h), new Point(pc.X + h, pc.Y - h));
            }
        }

        // Marcas no pavimento: eixo (amarelo, contínuo onde a ultrapassagem é proibida), divisórias entre faixas, bordos,
        // faixas fechadas (zebrado), retenções e faixas de pedestres.
        if (detail && !real)
        {
            var mw = Math.Max(0.8, 0.13 * _scale);
            Pen Dashed(Color c, double dash, double gap) => new(B(c, 230), mw) { DashStyle = new DashStyle(new[] { dash * _scale / mw, gap * _scale / mw }, 0), DashCap = PenLineCap.Flat };
            var yellow = Color.FromRgb(0xF2, 0xC2, 0x1B);
            foreach (var l in net.Links)
            {
                var t0 = clear[l.From];
                var t1 = clear[l.To];
                if (l.Length - t0 - t1 < 2) continue;
                var r = l.Road;
                // Eixo (uma vez por via, no trecho do sentido do eixo).
                if (r.TwoWay && l.Forward && r.MedianWidth < 0.2)
                {
                    var pts = OffsetRight(l.Path, 0, t0, t1);
                    if (l.NoPassing > 0.5) { dc.DrawGeometry(null, P(yellow, mw), Line(OffsetRight(l.Path, -0.12, t0, t1))); dc.DrawGeometry(null, P(yellow, mw), Line(OffsetRight(l.Path, 0.12, t0, t1))); }
                    else dc.DrawGeometry(null, Dashed(yellow, 3, 6), Line(pts));
                }
                var inner = r.TwoWay ? (r.Median ? Math.Max(1.0, r.MedianWidth / 2) : 0.0) : -l.Lanes * r.LaneWidth / 2;
                for (int k = 1; k < l.Lanes; k++)
                {
                    var off = inner + k * r.LaneWidth;
                    var solid = l.LaneChangeForbidden;
                    dc.DrawGeometry(null, solid ? P(Colors.White, mw, 230) : Dashed(Colors.White, 2, 4), Line(OffsetRight(l.Path, off, t0, t1 + (solid ? 0 : 0))));
                }
                // Bordo externo do sentido.
                dc.DrawGeometry(null, P(Colors.White, mw, 200), Line(OffsetRight(l.Path, inner + l.Lanes * r.LaneWidth - 0.2, t0, t1)));
                // Faixas fechadas: zebrado amarelo/branco.
                foreach (var (a0, a1, lane, _) in l.ClosedLanes)
                {
                    var off = l.LaneOffset(lane);
                    var band = new List<Vec2>();
                    for (var s0 = a0; s0 < a1; s0 += 3)
                    {
                        var p = l.Path.PointAt(s0);
                        var d = l.Path.TangentAt(s0);
                        var n = new Vec2(d.Y, -d.X);
                        dc.DrawLine(P(Color.FromRgb(0xF5, 0xF5, 0xF5), mw * 1.3), S(p + n * (off - r.LaneWidth / 2 + 0.2)), S(l.Path.PointAt(Math.Min(a1, s0 + 2.5)) + n * (off + r.LaneWidth / 2 - 0.2)));
                    }
                    _ = band;
                }
                // Linha de retenção nas aproximações de PARE e semáforo.
                var nd = net.Nodes[l.To];
                var nrs = _res.Nodes.GetValueOrDefault(nd.Index);
                var ctl = nrs?.Control ?? nd.Control;
                var stopHere = ctl == ControleNo.Semaforo || nd.StopApproaches.Contains(l.Index) || (ctl == ControleNo.Pare && nrs?.Approaches.FirstOrDefault(a => a.Link == l.Index) is { Major: false });
                if (stopHere && l.Length > t1 + 2)
                {
                    var s0 = l.Length - t1 + 0.6;
                    var p = l.Path.PointAt(s0);
                    var d = l.Path.TangentAt(s0);
                    var n = new Vec2(d.Y, -d.X);
                    dc.DrawLine(P(Colors.White, Math.Max(1.2, 0.4 * _scale), 235), S(p + n * (inner + 0.1)), S(p + n * (inner + l.Lanes * r.LaneWidth - 0.2)));
                }
                // Setas por faixa (setas pintadas lidas do projeto).
                foreach (var (lane, set) in l.LaneTurns)
                {
                    var s0 = Math.Max(0, l.Length - t1 - 8);
                    var p = l.Path.PointAt(s0) + new Vec2(l.Path.TangentAt(s0).Y, -l.Path.TangentAt(s0).X) * l.LaneOffset(lane);
                    var glyph = string.Concat(set.OrderBy(g => g).Select(g => g switch { Giro.Esquerda => "↰", Giro.Direita => "↱", Giro.Retorno => "↶", _ => "↑" }));
                    Text(dc, glyph, S(p), Math.Max(9, 2.2 * _scale), Colors.White, true, true);
                }
            }
            // Faixas de pedestres zebradas.
            foreach (var (_, _, path) in net.Crosswalks)
            {
                if (path.Points.Count < 2 || path.Length < 1) continue;
                for (var s0 = 0.4; s0 < path.Length - 0.2; s0 += 1.0)
                {
                    var p = path.PointAt(s0);
                    var d = path.TangentAt(s0);
                    var n = new Vec2(-d.Y, d.X);
                    dc.DrawLine(P(Colors.White, Math.Max(1, 0.45 * _scale), 235), S(p - n * 1.5), S(p + n * 1.5));
                }
            }
        }
        else if (!real)
            foreach (var (_, _, path) in net.Crosswalks)
                if (path.Points.Count >= 2) dc.DrawGeometry(null, P(Colors.White, Math.Max(1.5, 3 * _scale), 200), Line(path.Points));

        // Setas do sentido (sem animação).
        if (!animating)
            foreach (var l in net.Links.Where(l => l.Length * _scale > 60 && _scale > 0.4))
            {
                var inner = l.Road.TwoWay ? (l.Road.Median ? Math.Max(1.0, l.Road.MedianWidth / 2) : 0.0) : -l.Lanes * l.Road.LaneWidth / 2;
                var off = inner + l.Lanes * l.Road.LaneWidth / 2;
                var s0 = l.Length * 0.5;
                var p = l.Path.PointAt(s0);
                var d = l.Path.TangentAt(s0);
                var c = p + new Vec2(d.Y, -d.X) * off;
                var a = 1.6 * Math.Max(1.5, l.Road.LaneWidth * 0.5);
                var tip = c + d * a;
                var b1 = c - d * a + new Vec2(-d.Y, d.X) * a * 0.7;
                var b2 = c - d * a - new Vec2(-d.Y, d.X) * a * 0.7;
                Fill(new List<Vec2> { tip, b1, b2 }, Color.FromArgb(210, 255, 255, 255));
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
                // Na linha de retenção da própria aproximação (antes ficava no centro do nó e parecia de outra via).
                var p = TrafficSimulation.SignalHeadPosition(net, l);
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
                var r = Math.Max(3, 1.1 * _scale);
                if (state != int.MinValue) dc.DrawEllipse(B(c, 70), null, S(p), r * 2.2, r * 2.2);           // brilho
                dc.DrawRoundedRectangle(B(Color.FromRgb(0x1A, 0x1A, 0x1A)), null, new Rect(S(p).X - r - 1.5, S(p).Y - r - 1.5, 2 * r + 3, 2 * r + 3), 2, 2);
                dc.DrawEllipse(B(c), null, S(p), r, r);
            }
        }

        // Semáforos de travessia no meio da quadra (um ícone por travessia; vermelho/verde dos veículos no instante).
        var drawnX = new List<Vec2>();
        foreach (var (li, plans) in _res.CrossingPlans)
            foreach (var cp in plans)
            {
                var l = net.Links[li];
                if (drawnX.Any(q => q.DistanceTo(cp.Pos) < 3)) continue;
                drawnX.Add(cp.Pos);
                var d = l.Path.TangentAt(cp.At);
                var side = new Vec2(d.Y, -d.X) * (Math.Max(l.Road.RightWidth, l.Road.LeftWidth) + 0.8);
                Color c;
                if (frame == null) c = Color.FromRgb(0x44, 0x44, 0x44);
                else
                {
                    var t = SimTime + _res.Options.WarmupSeconds;
                    var tc = ((t - cp.Offset) % cp.Cycle + cp.Cycle) % cp.Cycle;
                    c = tc < cp.Red ? Color.FromRgb(0xE5, 0x39, 0x35) : tc > cp.Cycle - 3 ? Color.FromRgb(0xFF, 0xB3, 0x00) : Color.FromRgb(0x00, 0xC8, 0x53);
                }
                foreach (var sgn in new[] { 1.0, -1.0 })
                {
                    var p = S(cp.Pos + side * sgn);
                    var r = Math.Max(3, 1.0 * _scale);
                    dc.DrawRoundedRectangle(B(Color.FromRgb(0x1A, 0x1A, 0x1A)), null, new Rect(p.X - r - 1.5, p.Y - r - 1.5, 2 * r + 3, 2 * r + 3), 2, 2);
                    dc.DrawEllipse(B(c), null, p, r, r);
                }
            }

        // Veículos e sinalização.
        (int Count, int Stopped, double MeanKmh) hud = (0, 0, 0);
        if (ShowVehicles && _sim != null && !double.IsNaN(SimTime) && _sim.Frames.Count > 0) hud = DrawVehicles(dc);
        DrawSigns(dc);

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
            // Painel da animação: tempo, veículos, parados e velocidade média instantânea, com barra de progresso.
            var w = 330.0;
            dc.DrawRoundedRectangle(B(Color.FromRgb(0x17, 0x1C, 0x22), 215), null, new Rect(10, 10, w, 52), 6, 6);
            Text(dc, $"⏱ {label}", new Point(20, 16), 14, Colors.White, true);
            Text(dc, $"{hud.Count} veículos · {hud.Stopped} parados · média {hud.MeanKmh:0} km/h", new Point(20, 36), 11.5, Color.FromRgb(0xCF, 0xD6, 0xDE), true);
            if (_sim != null && _sim.Frames.Count > 1)
            {
                var f = Math.Clamp((SimTime - _sim.Frames[0].Time) / Math.Max(1, _sim.Frames[^1].Time - _sim.Frames[0].Time), 0, 1);
                dc.DrawRoundedRectangle(B(Color.FromRgb(0x3A, 0x44, 0x52)), null, new Rect(150, 22, w - 150, 6), 3, 3);
                dc.DrawRoundedRectangle(B(Color.FromRgb(0x4F, 0xC3, 0xF7)), null, new Rect(150, 22, (w - 150) * f, 6), 3, 3);
            }
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

    // Cores reais da frota (proporções típicas) e da carroceria de ônibus e caminhões.
    private static readonly Color[] CarColors =
    {
        Color.FromRgb(0xF4, 0xF4, 0xF2), Color.FromRgb(0xF4, 0xF4, 0xF2), Color.FromRgb(0xB9, 0xBF, 0xC7), Color.FromRgb(0xB9, 0xBF, 0xC7),
        Color.FromRgb(0x2B, 0x2E, 0x33), Color.FromRgb(0x6C, 0x72, 0x7B), Color.FromRgb(0xB3, 0x26, 0x1E), Color.FromRgb(0x1F, 0x4E, 0x8C),
        Color.FromRgb(0xF4, 0xF4, 0xF2), Color.FromRgb(0x8C, 0x7B, 0x62), Color.FromRgb(0x2E, 0x5E, 0x3E), Color.FromRgb(0x6C, 0x72, 0x7B),
    };
    private static readonly Color[] BusColors = { Color.FromRgb(0x15, 0x65, 0xC0), Color.FromRgb(0xF2, 0xB7, 0x05), Color.FromRgb(0xC6, 0x28, 0x28) };
    private static readonly Color[] CabColors = { Color.FromRgb(0xC6, 0x28, 0x28), Color.FromRgb(0x1F, 0x4E, 0x8C), Color.FromRgb(0xF4, 0xF4, 0xF2), Color.FromRgb(0x2E, 0x5E, 0x3E) };

    private static Color SpeedColor(double v) => v < 1.0 ? Color.FromRgb(0xD3, 0x2F, 0x2F) : v < 4 ? Color.FromRgb(0xF5, 0x7C, 0x00) : v < 9 ? Color.FromRgb(0xFB, 0xC0, 0x2D) : Color.FromRgb(0x43, 0xA0, 0x47);

    /// <summary>
    /// Veículos em planta: carro (carroceria com cantos chanfrados, para-brisa, vidro traseiro, luzes de freio acesas quando
    /// freia ou está parado), ônibus (teto com ar-condicionado e faixa de janelas) e caminhão (cavalo + carreta), com sombra.
    /// Sem zoom suficiente, cada veículo vira um ponto colorido. As cores são reais (frota) ou pela velocidade.
    /// </summary>
    private (int Count, int Stopped, double MeanKmh) DrawVehicles(DrawingContext dc)
    {
        var (a, b, u) = FrameAt(SimTime);
        var next = new Dictionary<int, int>(b.Count);
        for (int k = 0; k < b.Count; k++) next[(int)b.Data[k * SimFrame.Stride + 5]] = k;
        const int st = SimFrame.Stride;
        var groups = new Dictionary<Color, StreamGeometry>();
        var ctxs = new Dictionary<Color, StreamGeometryContext>();
        StreamGeometryContext Ctx(Color c)
        {
            if (!ctxs.TryGetValue(c, out var x)) { var g = new StreamGeometry(); groups[c] = g; ctxs[c] = x = g.Open(); }
            return x;
        }
        var shadow = new StreamGeometry();
        var sh = shadow.Open();
        var glass = new StreamGeometry();
        var gl = glass.Open();
        var brake = new StreamGeometry();
        var br = brake.Open();
        var tail = new StreamGeometry();
        var tl = tail.Open();
        var blinkG = new StreamGeometry();
        var bk = blinkG.Open();
        var blinkOn = SimTime - Math.Floor(SimTime) < 0.55;   // pisca-pisca ~1 Hz
        var detailed = _scale * 4.4 >= 9;
        var stopped = 0;
        var sumV = 0.0;
        var dotR = Math.Clamp(_scale * 1.1, 1.6, 4.5);
        for (int k = 0; k < a.Count; k++)
        {
            var x = a.Data[k * st];
            var y = a.Data[k * st + 1];
            var h = a.Data[k * st + 2];
            var type = (int)a.Data[k * st + 3];
            var v = a.Data[k * st + 4];
            var id = (int)a.Data[k * st + 5];
            var blink = a.Data[k * st + 6];
            var braking = v < 0.5;
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
                    var v2 = b.Data[j * st + 4];
                    braking |= v2 < v - 0.4;
                    v += (float)((v2 - v) * u);
                }
            }
            if (v < 0.5) stopped++;
            sumV += v;
            var d = new Vec2(Math.Cos(h), Math.Sin(h));
            var n = new Vec2(-d.Y, d.X);
            var c0 = new Vec2(x, y);
            var len = type == 0 ? 4.4 : type == 2 ? 12.0 : 11.5;
            var wid = type == 0 ? 1.8 : 2.5;
            var body = ColorBySpeed ? SpeedColor(v) : type switch
            {
                2 => BusColors[id % BusColors.Length],
                1 => CabColors[id % CabColors.Length],
                _ => CarColors[(id * 7 + 3) % CarColors.Length],
            };
            if (!detailed)
            {
                var ctxd = Ctx(body);
                var p = S(c0);
                var rr = type == 0 ? dotR : dotR * 1.5;
                ctxd.BeginFigure(new Point(p.X - rr, p.Y), true, true);
                ctxd.ArcTo(new Point(p.X + rr, p.Y), new Size(rr, rr), 0, false, SweepDirection.Clockwise, true, false);
                ctxd.ArcTo(new Point(p.X - rr, p.Y), new Size(rr, rr), 0, false, SweepDirection.Clockwise, true, false);
                continue;
            }
            // Pontos no sistema do veículo (x à frente, y à esquerda), origem no centro.
            Point L(double fx, double fy) => S(c0 + d * fx + n * fy);
            void Quad(StreamGeometryContext g, params (double, double)[] pts)
            {
                g.BeginFigure(L(pts[0].Item1, pts[0].Item2), true, true);
                g.PolyLineTo(pts.Skip(1).Select(q => L(q.Item1, q.Item2)).ToList(), true, false);
            }
            var hl = len / 2;
            var hw = wid / 2;
            // Sombra (deslocada para sudeste).
            var so = new Vec2(0.35, -0.35);
            sh.BeginFigure(S(c0 + so + d * hl + n * hw), true, true);
            sh.PolyLineTo(new[] { S(c0 + so + d * hl - n * hw), S(c0 + so - d * hl - n * hw), S(c0 + so - d * hl + n * hw) }, true, false);
            if (type == 1)
            {
                // Caminhão: cavalo (2,4 m) + carreta.
                Quad(Ctx(body), (hl, hw - 0.1), (hl, -hw + 0.1), (hl - 2.4, -hw + 0.1), (hl - 2.4, hw - 0.1));
                Quad(gl, (hl - 0.25, hw - 0.3), (hl - 0.25, -hw + 0.3), (hl - 0.8, -hw + 0.3), (hl - 0.8, hw - 0.3));
                Quad(Ctx(Color.FromRgb(0xE3, 0xE5, 0xE8)), (hl - 2.7, hw), (hl - 2.7, -hw), (-hl, -hw), (-hl, hw));
                Quad(Ctx(Color.FromRgb(0xC9, 0xCD, 0xD2)), (hl - 3.2, hw - 0.35), (hl - 3.2, -hw + 0.35), (-hl + 0.4, -hw + 0.35), (-hl + 0.4, hw - 0.35));
            }
            else if (type == 2)
            {
                // Ônibus: carroceria, faixa de janelas nas laterais, para-brisa e ar-condicionado no teto.
                Quad(Ctx(body), (hl, hw - 0.15), (hl - 0.15, hw), (-hl + 0.1, hw), (-hl, hw - 0.1), (-hl, -hw + 0.1), (-hl + 0.1, -hw), (hl - 0.15, -hw), (hl, -hw + 0.15));
                Quad(gl, (hl - 0.1, hw - 0.25), (hl - 0.1, -hw + 0.25), (hl - 0.7, -hw + 0.25), (hl - 0.7, hw - 0.25));
                Quad(gl, (hl - 1.0, hw - 0.05), (-hl + 0.6, hw - 0.05), (-hl + 0.6, hw - 0.35), (hl - 1.0, hw - 0.35));
                Quad(gl, (hl - 1.0, -hw + 0.05), (-hl + 0.6, -hw + 0.05), (-hl + 0.6, -hw + 0.35), (hl - 1.0, -hw + 0.35));
                Quad(Ctx(Color.FromRgb(0xEC, 0xEE, 0xF0)), (1.5, 0.7), (1.5, -0.7), (-1.5, -0.7), (-1.5, 0.7));
            }
            else
            {
                // Carro: cantos chanfrados, para-brisa (frente) e vidro traseiro.
                Quad(Ctx(body), (hl, hw - 0.35), (hl - 0.4, hw), (-hl + 0.3, hw), (-hl, hw - 0.25), (-hl, -hw + 0.25), (-hl + 0.3, -hw), (hl - 0.4, -hw), (hl, -hw + 0.35));
                Quad(gl, (hl - 0.75, hw - 0.2), (hl - 0.75, -hw + 0.2), (hl - 1.35, -hw + 0.28), (hl - 1.35, hw - 0.28));
                Quad(gl, (-hl + 0.95, hw - 0.28), (-hl + 0.95, -hw + 0.28), (-hl + 0.5, -hw + 0.22), (-hl + 0.5, hw - 0.22));
            }
            // Lanternas: acesas (vermelho vivo) quando freia ou está parado.
            var lg = braking ? br : tl;
            Quad(lg, (-hl + 0.18, hw - 0.1), (-hl + 0.18, hw - 0.45), (-hl - 0.02, hw - 0.45), (-hl - 0.02, hw - 0.1));
            Quad(lg, (-hl + 0.18, -hw + 0.1), (-hl + 0.18, -hw + 0.45), (-hl - 0.02, -hw + 0.45), (-hl - 0.02, -hw + 0.1));
            // Seta (pisca) no lado da conversão ou da troca de faixa: cantos dianteiro e traseiro.
            if (blink != 0 && blinkOn)
            {
                var sy = blink < 0 ? 1.0 : -1.0;
                Quad(bk, (hl + 0.05, sy * (hw - 0.05)), (hl + 0.05, sy * (hw - 0.4)), (hl - 0.3, sy * (hw - 0.4)), (hl - 0.3, sy * (hw + 0.05)));
                Quad(bk, (-hl + 0.3, sy * (hw + 0.05)), (-hl + 0.3, sy * (hw - 0.4)), (-hl - 0.05, sy * (hw - 0.4)), (-hl - 0.05, sy * (hw - 0.05)));
            }
        }
        bk.Close(); blinkG.Freeze();
        // Pedestres atravessando (círculo com a cabeça, visto de cima).
        if (a.Peds.Length > 0)
        {
            var pr = Math.Max(1.5, 0.35 * _scale);
            for (int k = 0; k + 1 < a.Peds.Length; k += 2)
            {
                var pt = S(new Vec2(a.Peds[k], a.Peds[k + 1]));
                dc.DrawEllipse(B(Color.FromRgb(0x2E, 0x4A, 0x7A)), P(Colors.White, Math.Max(0.5, pr * 0.3)), pt, pr, pr);
                dc.DrawEllipse(B(Color.FromRgb(0xF1, 0xC2, 0x8E)), null, pt, pr * 0.45, pr * 0.45);
            }
        }
        sh.Close(); shadow.Freeze();
        if (detailed) dc.DrawGeometry(B(Colors.Black, 55), null, shadow);
        var outline = P(Color.FromRgb(0x1B, 0x1F, 0x24), Math.Clamp(_scale * 0.06, 0.4, 1.0), 200);
        foreach (var (c, g) in groups)
        {
            ctxs[c].Close();
            g.Freeze();
            dc.DrawGeometry(B(c), detailed ? outline : null, g);
        }
        gl.Close(); glass.Freeze();
        tl.Close(); tail.Freeze();
        br.Close(); brake.Freeze();
        if (detailed)
        {
            dc.DrawGeometry(B(Color.FromRgb(0x2A, 0x3A, 0x4A), 230), null, glass);
            dc.DrawGeometry(B(Color.FromRgb(0x8B, 0x1A, 0x1A)), null, tail);
            dc.DrawGeometry(B(Color.FromRgb(0xFF, 0x2D, 0x2D)), P(Color.FromRgb(0xFF, 0x6B, 0x6B), Math.Max(0.5, _scale * 0.1), 120), brake);
            dc.DrawGeometry(B(Color.FromRgb(0xFF, 0xA0, 0x00)), P(Color.FromRgb(0xFF, 0xD0, 0x60), Math.Max(0.5, _scale * 0.12), 140), blinkG);
        }
        return (a.Count, stopped, a.Count > 0 ? sumV / a.Count * 3.6 : 0);
    }

    /// <summary>Ícones da sinalização lida: PARE (octógono), dê a preferência (triângulo), velocidade (círculo) e semáforo.</summary>
    private void DrawSigns(DrawingContext dc)
    {
        if (_res == null || !ShowSigns || _scale < 0.8) return;
        var sz = Math.Clamp(_scale * 1.6, 6, 13);
        foreach (var r in _res.Network.Regulations.Where(x => x.Applied))
        {
            var p = S(r.Position);
            if (p.X < -20 || p.Y < -20 || p.X > ActualWidth + 20 || p.Y > ActualHeight + 20) continue;
            switch (r.Kind)
            {
                case TipoRegra.Parada when r.Source.StartsWith("Placa"):
                {
                    var pts = Enumerable.Range(0, 8).Select(k => new Point(p.X + sz * Math.Cos(Math.PI / 8 + k * Math.PI / 4), p.Y + sz * Math.Sin(Math.PI / 8 + k * Math.PI / 4))).ToList();
                    var g = new StreamGeometry();
                    using (var c = g.Open()) { c.BeginFigure(pts[0], true, true); c.PolyLineTo(pts.Skip(1).ToList(), true, false); }
                    g.Freeze();
                    dc.DrawGeometry(B(Color.FromRgb(0xC6, 0x28, 0x28)), P(Colors.White, 1.2), g);
                    Text(dc, "PARE", p, sz * 0.62, Colors.White, true, true);
                    break;
                }
                case TipoRegra.Preferencia when r.Source.StartsWith("Placa"):
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(p.X, p.Y + sz), true, true);
                        c.PolyLineTo(new[] { new Point(p.X - sz, p.Y - sz * 0.75), new Point(p.X + sz, p.Y - sz * 0.75) }, true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(B(Colors.White), P(Color.FromRgb(0xC6, 0x28, 0x28), Math.Max(1.5, sz * 0.25)), g);
                    break;
                }
                case TipoRegra.Velocidade:
                {
                    dc.DrawEllipse(B(Colors.White), P(Color.FromRgb(0xC6, 0x28, 0x28), Math.Max(1.5, sz * 0.22)), p, sz, sz);
                    var num = System.Text.RegularExpressions.Regex.Match(r.Effect, @"(\d{2,3}) km/h").Groups[1].Value;
                    Text(dc, num, p, sz * 0.8, Colors.Black, true, true);
                    break;
                }
                case TipoRegra.Semaforo:
                {
                    var w = sz * 0.7;
                    dc.DrawRoundedRectangle(B(Color.FromRgb(0x22, 0x22, 0x22)), null, new Rect(p.X - w / 2, p.Y - w * 1.3, w, w * 2.6), 2, 2);
                    foreach (var (k, c) in new[] { (-1, Color.FromRgb(0xE5, 0x39, 0x35)), (0, Color.FromRgb(0xFF, 0xB3, 0x00)), (1, Color.FromRgb(0x00, 0xC8, 0x53)) })
                        dc.DrawEllipse(B(c), null, new Point(p.X, p.Y + k * w * 0.8), w * 0.32, w * 0.32);
                    break;
                }
                case TipoRegra.Bloqueio or TipoRegra.SentidoProibido:
                {
                    dc.DrawEllipse(B(Color.FromRgb(0xC6, 0x28, 0x28)), P(Colors.White, 1.2), p, sz, sz);
                    dc.DrawRectangle(B(Colors.White), null, new Rect(p.X - sz * 0.65, p.Y - sz * 0.18, sz * 1.3, sz * 0.36));
                    break;
                }
            }
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
        if (_moved) return;
        if (PickMode) { PointPicked?.Invoke(W(p)); return; }
        HitTest(p);
    }

    /// <summary>Botão direito: ponto do projeto e o cruzamento/trecho sob o cursor (a janela monta o menu de semáforos).</summary>
    public event Action<Vec2, int?, int?>? ContextRequested;

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_res == null || ContextRequested == null) { FitAll(); return; }
        var p = e.GetPosition(this);
        var net = _res.Network;
        var nd = net.Nodes.Where(n => n.Kind != TipoNo.Continuacao && !n.IsZone).Select(n => (n, d: (S(n.Pos) - p).Length)).Where(x => x.d < 18).OrderBy(x => x.d).FirstOrDefault().n;
        var w = W(p);
        int? link = null;
        if (nd == null)
        {
            var best = net.Links.Select(l => (l, pr: l.Path.Project(w))).Where(x => x.pr.Station > 0 && x.pr.Station < x.l.Length && Math.Abs(x.pr.Signed) < Math.Max(x.l.Road.LeftWidth, x.l.Road.RightWidth) + 2)
                .OrderBy(x => Math.Abs(x.pr.Signed)).FirstOrDefault();
            link = best.l?.Index;
        }
        ContextRequested(w, nd?.Index, link);
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
