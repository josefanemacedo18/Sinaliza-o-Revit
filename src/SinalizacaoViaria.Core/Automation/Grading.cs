using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Superfície de projeto da terraplenagem e volumes de corte/aterro.</summary>
public sealed class GradingResult
{
    /// <summary>Triângulos da superfície de projeto (faixas e taludes), cotas absolutas (m).</summary>
    public List<(Vec3 A, Vec3 B, Vec3 C)> Triangles { get; } = new();
    /// <summary>Plataformas (polígono, cota absoluta no ponto – plana ou inclinada pelo greide das vias).</summary>
    public List<(Polygon2 Area, Func<Vec2, double> Z)> Pads { get; } = new();
    /// <summary>Pontos a inserir no terreno (bordas, pés/cristas de talude, pontos internos das plataformas).</summary>
    public List<Vec3> Points { get; } = new();
    /// <summary>Contorno de tudo o que é alterado (plataformas + taludes).</summary>
    public List<Polygon2> Footprint { get; } = new();
    public double CutM3 { get; set; }
    public double FillM3 { get; set; }
    public double AreaM2 { get; set; }
    public List<string> Warnings { get; } = new();

    /// <summary>Triângulo de talude (falso = plataforma/faixa de projeto, que tem prioridade).</summary>
    public List<bool> TriangleIsSlope { get; } = new();

    /// <summary>Terreno natural usado no projeto (para combinar taludes sobrepostos).</summary>
    public Func<Vec2, double?>? Ground { get; set; }

    private const double Cell = 6.0;
    private Dictionary<(int, int), List<int>>? _index;
    private int _indexed;

    private static (int, int) Key(double x, double y) => ((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell));

    private void Index()
    {
        if (_index != null && _indexed == Triangles.Count) return;
        _index = new();
        for (int i = 0; i < Triangles.Count; i++)
        {
            var (a, b, c) = Triangles[i];
            var (x0, y0) = Key(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)));
            var (x1, y1) = Key(Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                {
                    if (!_index.TryGetValue((x, y), out var l)) _index[(x, y)] = l = new List<int>();
                    l.Add(i);
                }
        }
        _indexed = Triangles.Count;
    }

    /// <summary>
    /// Cota de projeto no ponto (nulo = fora da área terraplenada). Plataformas têm prioridade; onde taludes de obras
    /// vizinhas se sobrepõem vale o mais alto nos aterros e o mais baixo nos cortes (superfície única, sem "degraus").
    /// </summary>
    public double? DesignZ(Vec2 p)
    {
        foreach (var (area, z) in Pads) if (area.Contains(p)) return z(p);
        Index();
        if (!_index!.TryGetValue(Key(p.X, p.Y), out var cand)) return null;
        double? platform = null;
        List<double>? slopes = null;
        foreach (var i in cand)
        {
            var (a, b, c) = Triangles[i];
            var d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(d) < 1e-12) continue;
            var l1 = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / d;
            var l2 = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / d;
            var l3 = 1 - l1 - l2;
            if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
            var z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
            var slope = i < TriangleIsSlope.Count && TriangleIsSlope[i];
            if (!slope) platform = platform == null ? z : Math.Max(platform.Value, z);
            else (slopes ??= new()).Add(z);
        }
        if (platform != null) return platform;
        if (slopes == null) return null;
        if (slopes.Count == 1) return slopes[0];
        var g = Ground?.Invoke(p);
        if (g == null) return slopes.Max();
        var fills = slopes.Where(z => z > g.Value).ToList();
        return fills.Count > 0 ? fills.Max() : slopes.Min();
    }
}

/// <summary>
/// Terraplenagem: a partir das faixas e plataformas de projeto (bordas cotadas), encontra o encontro dos taludes de corte
/// (H:V) e de aterro com o terreno natural ("offsets"), monta a superfície de projeto e calcula os volumes. O Revit aplica
/// o resultado ao Toposolid (Massa e terreno) – vias, conexões e obras ficam planas/no greide e o terreno se ajusta a elas.
/// </summary>
public static class Grading
{
    /// <summary>
    /// Mapa de corte e aterro: regiões (células de <paramref name="step"/> m unidas) onde o projeto fica mais de
    /// <paramref name="min"/> m abaixo do terreno natural (corte) ou acima dele (aterro).
    /// </summary>
    public static (List<Polygon2> Cut, List<Polygon2> Fill) CutFillRegions(GradingResult design, Func<Vec2, double?> ground, double step = 2, double min = 0.10)
    {
        var cut = new List<Polygon2>();
        var fill = new List<Polygon2>();
        if (design.Footprint.Count == 0) return (cut, fill);
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var f in design.Footprint)
        {
            var (mn, mx) = f.Bounds;
            x0 = Math.Min(x0, mn.X); y0 = Math.Min(y0, mn.Y); x1 = Math.Max(x1, mx.X); y1 = Math.Max(y1, mx.Y);
        }
        var nx = (int)Math.Ceiling((x1 - x0) / step);
        var ny = (int)Math.Ceiling((y1 - y0) / step);
        if ((long)nx * ny > 400_000) { step *= Math.Sqrt((double)nx * ny / 400_000); nx = (int)Math.Ceiling((x1 - x0) / step); ny = (int)Math.Ceiling((y1 - y0) / step); }
        var cutCells = new List<Polygon2>();
        var fillCells = new List<Polygon2>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var c = new Vec2(x0 + (i + 0.5) * step, y0 + (j + 0.5) * step);
                if (design.DesignZ(c) is not { } zd || ground(c) is not { } zg) continue;
                var dz = zd - zg;
                if (Math.Abs(dz) < min) continue;
                var cell = Polygon2.Rectangle(new Vec2(x0 + i * step, y0 + j * step), new Vec2(x0 + (i + 1) * step, y0 + (j + 1) * step));
                (dz < 0 ? cutCells : fillCells).Add(cell);
            }
        List<Polygon2> U(List<Polygon2> cells)
        {
            if (cells.Count == 0) return new List<Polygon2>();
            try
            {
                // União das células e contorno suavizado (abre/fecha com arredondamento) – sem "escadinha" de células.
                var u = PolygonOps.Union(cells);
                var smooth = PolygonOps.Offset(PolygonOps.Offset(u, -step * 0.4, true), step * 0.4, true);
                return smooth.Where(p => p.Area > step * step * 1.5).Select(p => p.Simplified() ?? p).ToList();
            }
            catch { return new List<Polygon2>(); }
        }
        return (U(cutCells), U(fillCells));
    }

    /// <param name="ground">Cota do terreno natural (m, absoluta) – nulo fora do terreno.</param>
    /// <param name="baseZ">Cota da base das marcas (somada às cotas relativas das faixas/plataformas).</param>
    public static GradingResult Design(IEnumerable<GradeCorridor> corridors, IEnumerable<GradePad> pads, Func<Vec2, double?> ground, double baseZ = 0,
        double maxDaylight = 60, double step = 0.5, double padGrid = 4.0, double sampling = 1.0)
    {
        var res = new GradingResult { Ground = ground };
        foreach (var c in corridors)
        {
            var n = Math.Min(c.Left.Count, c.Right.Count);
            if (n < 2) continue;
            var left = c.Left.Take(n).Select(p => p with { Z = p.Z + baseZ }).ToList();
            var right = c.Right.Take(n).Select(p => p with { Z = p.Z + baseZ }).ToList();
            // Faixa: quadriláteros entre as bordas.
            for (int i = 0; i + 1 < n; i++)
            {
                res.Triangles.Add((left[i], left[i + 1], right[i + 1]));
                res.Triangles.Add((left[i], right[i + 1], right[i]));
                res.TriangleIsSlope.Add(false);
                res.TriangleIsSlope.Add(false);
                res.Footprint.Add(new Polygon2(new[] { left[i].XY, left[i + 1].XY, right[i + 1].XY, right[i].XY }));
            }
            res.Points.AddRange(left);
            res.Points.AddRange(right);
            for (int i = 0; i < n; i++) res.Points.Add(new Vec3((left[i].X + right[i].X) / 2, (left[i].Y + right[i].Y) / 2, (left[i].Z + right[i].Z) / 2));
            // Taludes: do lado de fora de cada borda; muros: degrau até o terreno natural logo atrás da borda.
            var dayL = c.DaylightLeft ? SlopeBand(res, left, right, c.CutSlope, c.FillSlope, ground, maxDaylight, step)
                : c.WallLeft ? WallBand(res, left, right, ground) : left;
            var dayR = c.DaylightRight ? SlopeBand(res, right, left, c.CutSlope, c.FillSlope, ground, maxDaylight, step, reverse: true)
                : c.WallRight ? WallBand(res, right, left, ground) : right;
            if (c.WallStart) EndWall(res, dayL[0], left[0], right[0], dayR[0], (left[0].XY + right[0].XY) * 0.5 - (left[1].XY + right[1].XY) * 0.5, ground, c.WallCap, c.CutSlope, maxDaylight, step, c.EndSpill ? c.FillSlope : null);
            if (c.WallEnd) EndWall(res, dayL[n - 1], left[n - 1], right[n - 1], dayR[n - 1], (left[n - 1].XY + right[n - 1].XY) * 0.5 - (left[n - 2].XY + right[n - 2].XY) * 0.5, ground,
                c.WallCap, c.CutSlope, maxDaylight, step, c.EndSpill ? c.FillSlope : null);
        }
        foreach (var pad in pads)
        {
            var outer = pad.Area.Outer.ToList();
            if (outer.Count < 3) continue;
            var pd = pad;
            Func<Vec2, double> z = p => pd.At(p) + baseZ;
            res.Pads.Add((pad.Area, z));
            res.Footprint.Add(pad.Area);
            var ring = CurveTools.Densify(outer, 2.0);
            if (ring.Count > 1 && ring[0].AlmostEquals(ring[^1], 1e-6)) ring.RemoveAt(ring.Count - 1);
            res.Points.AddRange(ring.Select(p => Vec3.At(p, z(p))));
            // Pontos internos: a plataforma segue a sua superfície também no miolo.
            var (mn, mx) = pad.Area.Bounds;
            for (var x = mn.X + padGrid / 2; x < mx.X; x += padGrid)
                for (var y = mn.Y + padGrid / 2; y < mx.Y; y += padGrid)
                {
                    var p = new Vec2(x, y);
                    if (pad.Area.Contains(p) && Distance(ring, p) > 0.5) res.Points.Add(Vec3.At(p, z(p)));
                }
            if (pad.Daylight) PadSlopes(res, ring, z, pad.CutSlope, pad.FillSlope, ground, maxDaylight, step);
        }
        res.Points.RemoveAll(p => double.IsNaN(p.Z));
        Volumes(res, ground, sampling);
        return res;
    }

    private static double Distance(IReadOnlyList<Vec2> ring, Vec2 p)
    {
        var best = double.MaxValue;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            var ab = b - a;
            var t = ab.Dot(ab) < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / ab.Dot(ab), 0, 1);
            best = Math.Min(best, p.DistanceTo(a + ab * t));
        }
        return best;
    }

    /// <summary>
    /// Ponto em que o talude que sai da borda (cota <paramref name="z"/>) encontra o terreno, andando na direção <paramref name="n"/>:
    /// corte sobe (1 : <paramref name="cut"/>), aterro desce (1 : <paramref name="fill"/>).
    /// </summary>
    public static Vec3 Daylight(Vec2 p, double z, Vec2 n, double cut, double fill, Func<Vec2, double?> ground, double maxDist = 60, double step = 0.5)
    {
        var g0 = ground(p);
        if (g0 == null) return Vec3.At(p, z);
        var dz0 = g0.Value - z;
        if (Math.Abs(dz0) < 0.01) return Vec3.At(p, z);
        var isCut = dz0 > 0;
        var ratio = Math.Max(0.1, isCut ? cut : fill);
        double prevD = 0, prevDiff = dz0;
        for (var d = step; d <= maxDist + 1e-9; d += step)
        {
            var q = p + n * d;
            var g = ground(q);
            if (g == null) return Vec3.At(p + n * prevD, z + (isCut ? 1 : -1) * prevD / ratio);
            var s = z + (isCut ? 1 : -1) * d / ratio;
            var diff = g.Value - s;
            if (isCut ? diff <= 0 : diff >= 0)
            {
                // Interpolação linear entre as duas últimas amostras.
                var t = Math.Abs(prevDiff - diff) < 1e-12 ? 1 : prevDiff / (prevDiff - diff);
                var dd = prevD + (d - prevD) * t;
                return Vec3.At(p + n * dd, z + (isCut ? 1 : -1) * dd / ratio);
            }
            prevD = d;
            prevDiff = diff;
        }
        var far = p + n * maxDist;
        return Vec3.At(far, ground(far) ?? z + (isCut ? 1 : -1) * maxDist / ratio);
    }

    /// <summary>Afastamento do degrau de muro/emboque (m): face quase vertical do terreno, escondida pela estrutura.</summary>
    public const double WallGap = 0.10;

    /// <summary>
    /// Degrau de muro: linha de pontos a <see cref="WallGap"/> para fora da borda, no terreno natural. A faixa estreita entre a
    /// borda (no projeto) e essa linha vira a face do corte/aterro contido pelo muro.
    /// </summary>
    private static List<Vec3> WallBand(GradingResult res, List<Vec3> edge, List<Vec3> other, Func<Vec2, double?> ground)
    {
        var outer = new List<Vec3>();
        for (int i = 0; i < edge.Count; i++)
        {
            var e = edge[i].XY;
            var n = (e - other[i].XY).Length < 1e-9 ? Vec2.UnitY : (e - other[i].XY).Normalized();
            var q = e + n * WallGap;
            outer.Add(Vec3.At(q, ground(q) ?? edge[i].Z));
        }
        Strip(res, edge, outer);
        res.Points.AddRange(outer);
        return outer;
    }

    /// <summary>Face vertical transversal no início/fim de uma faixa (emboque, encontro): de uma crista de talude à outra.</summary>
    private static void EndWall(GradingResult res, Vec3 dayL, Vec3 left, Vec3 right, Vec3 dayR, Vec2 outward, Func<Vec2, double?> ground,
        double? cap = null, double cut = 1.0, double maxDist = 60, double step = 0.5, double? spillFill = null)
    {
        if (outward.Length < 1e-9) return;
        var dir = outward.Normalized();
        if (spillFill is { } fs)
        {
            // Saia: da ponta, talude à frente (aterro na razão do aterro, corte na do corte) até encontrar o terreno.
            var edgeLine = new List<Vec3>();
            foreach (var (a, b) in new[] { (dayL, left), (left, right), (right, dayR) })
            {
                var len = a.XY.DistanceTo(b.XY);
                var k = Math.Max(1, (int)Math.Ceiling(len / 1.5));
                for (int i = 0; i < k; i++) edgeLine.Add(a + (b - a) * ((double)i / k));
            }
            edgeLine.Add(dayR);
            var toe = edgeLine.Select(p => Daylight(p.XY, p.Z, dir, cut, fs, ground, maxDist, step)).ToList();
            for (int i = 0; i + 1 < edgeLine.Count; i++)
            {
                res.Triangles.Add((edgeLine[i], edgeLine[i + 1], toe[i + 1]));
                res.Triangles.Add((edgeLine[i], toe[i + 1], toe[i]));
                res.TriangleIsSlope.Add(true);
                res.TriangleIsSlope.Add(true);
                var quad = new Polygon2(new[] { edgeLine[i].XY, edgeLine[i + 1].XY, toe[i + 1].XY, toe[i].XY });
                if (Math.Abs(quad.Area) > 1e-4) res.Footprint.Add(quad);
            }
            res.Points.AddRange(edgeLine);
            for (int i = 0; i < toe.Count; i++)
            {
                var w = toe[i].XY.DistanceTo(edgeLine[i].XY);
                if (w < 0.05) continue;
                res.Points.Add(toe[i]);
                var k = (int)Math.Floor(w / 3.0);
                for (int j = 1; j <= k; j++) res.Points.Add(edgeLine[i] + (toe[i] - edgeLine[i]) * ((double)j / (k + 1)));
            }
            return;
        }
        var o = dir * WallGap;
        var capZ = cap is { } h ? Math.Max(left.Z, right.Z) + h : double.MaxValue;
        var line = new List<Vec3>();
        foreach (var (a, b) in new[] { (dayL, left), (left, right), (right, dayR) })
        {
            var len = a.XY.DistanceTo(b.XY);
            var k = Math.Max(1, (int)Math.Ceiling(len / 1.0));
            for (int i = 0; i < k; i++) line.Add(a + (b - a) * ((double)i / k));
        }
        line.Add(dayR);
        var outer = line.Select(p => { var q = p.XY + o; return Vec3.At(q, Math.Min(capZ, ground(q) ?? p.Z)); }).ToList();
        // Pontos da borda de projeto também (a crista/base do corte fica marcada).
        res.Points.AddRange(line);
        Strip(res, line, outer);
        res.Points.AddRange(outer);
        if (cap == null) return;
        // Acima da face (testa do emboque): talude de corte para dentro do maciço até reencontrar o terreno.
        var day = outer.Select(p => (ground(p.XY) ?? p.Z) > p.Z + 0.01 ? Daylight(p.XY, p.Z, dir, cut, cut, ground, maxDist, step) : p).ToList();
        for (int i = 0; i + 1 < outer.Count; i++)
        {
            res.Triangles.Add((outer[i], outer[i + 1], day[i + 1]));
            res.Triangles.Add((outer[i], day[i + 1], day[i]));
            res.TriangleIsSlope.Add(true);
            res.TriangleIsSlope.Add(true);
            var quad = new Polygon2(new[] { outer[i].XY, outer[i + 1].XY, day[i + 1].XY, day[i].XY });
            if (Math.Abs(quad.Area) > 1e-4) res.Footprint.Add(quad);
        }
        for (int i = 0; i < outer.Count; i++)
        {
            var w = day[i].XY.DistanceTo(outer[i].XY);
            if (w < 0.05) continue;
            res.Points.Add(day[i]);
            var k = (int)Math.Floor(w / 3.0);
            for (int j = 1; j <= k; j++) res.Points.Add(outer[i] + (day[i] - outer[i]) * ((double)j / (k + 1)));
        }
    }

    /// <summary>Faixa estreita (degrau) entre duas linhas paralelas: triângulos de talude e contorno.</summary>
    private static void Strip(GradingResult res, List<Vec3> a, List<Vec3> b)
    {
        for (int i = 0; i + 1 < a.Count; i++)
        {
            res.Triangles.Add((a[i], a[i + 1], b[i + 1]));
            res.Triangles.Add((a[i], b[i + 1], b[i]));
            res.TriangleIsSlope.Add(true);
            res.TriangleIsSlope.Add(true);
            var quad = new Polygon2(new[] { a[i].XY, a[i + 1].XY, b[i + 1].XY, b[i].XY });
            if (Math.Abs(quad.Area) > 1e-5) res.Footprint.Add(quad);
        }
    }

    private static List<Vec3> SlopeBand(GradingResult res, List<Vec3> edge, List<Vec3> other, double cut, double fill, Func<Vec2, double?> ground,
        double maxDist, double step, bool reverse = false)
    {
        var day = new List<Vec3>();
        for (int i = 0; i < edge.Count; i++)
        {
            var e = edge[i].XY;
            var o = other[i].XY;
            var n = (e - o).Length < 1e-9 ? Vec2.UnitY : (e - o).Normalized();
            day.Add(Daylight(e, edge[i].Z, n, cut, fill, ground, maxDist, step));
        }
        for (int i = 0; i + 1 < edge.Count; i++)
        {
            res.Triangles.Add((edge[i], edge[i + 1], day[i + 1]));
            res.Triangles.Add((edge[i], day[i + 1], day[i]));
            res.TriangleIsSlope.Add(true);
            res.TriangleIsSlope.Add(true);
            var quad = new Polygon2(new[] { edge[i].XY, edge[i + 1].XY, day[i + 1].XY, day[i].XY });
            if (quad.Area > 1e-4) res.Footprint.Add(quad);
        }
        res.Points.AddRange(day.Where((d, i) => d.XY.DistanceTo(edge[i].XY) > 0.05));
        // Pontos no meio da face do talude: a triangulação do Toposolid acompanha o plano do talude.
        for (int i = 0; i < edge.Count; i++)
        {
            var w = day[i].XY.DistanceTo(edge[i].XY);
            // Taludes altos: pontos a cada ~3 m na face (a triangulação do Toposolid fica no plano do talude).
            var k = (int)Math.Floor(w / 3.0);
            for (int j = 1; j <= k; j++) res.Points.Add(edge[i] + (day[i] - edge[i]) * ((double)j / (k + 1)));
        }
        return day;
    }

    private static void PadSlopes(GradingResult res, List<Vec2> ring, Func<Vec2, double> zAt, double cut, double fill, Func<Vec2, double?> ground, double maxDist, double step)
    {
        var ccw = Polygon2.SignedArea(ring) > 0;
        var day = new List<Vec3>();
        for (int i = 0; i < ring.Count; i++)
        {
            var prev = ring[(i - 1 + ring.Count) % ring.Count];
            var next = ring[(i + 1) % ring.Count];
            var e1 = (ring[i] - prev).Normalized();
            var e2 = (next - ring[i]).Normalized();
            var n1 = ccw ? e1.PerpRight : e1.PerpLeft;
            var n2 = ccw ? e2.PerpRight : e2.PerpLeft;
            var n = (n1 + n2).Length < 1e-9 ? n1 : (n1 + n2).Normalized();
            day.Add(Daylight(ring[i], zAt(ring[i]), n, cut, fill, ground, maxDist, step));
        }
        for (int i = 0; i < ring.Count; i++)
        {
            var j = (i + 1) % ring.Count;
            var a = Vec3.At(ring[i], zAt(ring[i]));
            var b = Vec3.At(ring[j], zAt(ring[j]));
            res.Triangles.Add((a, b, day[j]));
            res.Triangles.Add((a, day[j], day[i]));
            res.TriangleIsSlope.Add(true);
            res.TriangleIsSlope.Add(true);
            var quad = new Polygon2(new[] { ring[i], ring[j], day[j].XY, day[i].XY });
            if (quad.Area > 1e-4) res.Footprint.Add(quad);
        }
        res.Points.AddRange(day.Where((d, i) => d.XY.DistanceTo(ring[i]) > 0.05));
    }

    /// <summary>Volumes de corte e aterro por malha de amostragem (m³).</summary>
    public static void Volumes(GradingResult res, Func<Vec2, double?> ground, double cell)
    {
        if (res.Footprint.Count == 0) return;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var f in res.Footprint)
        {
            var (mn, mx) = f.Bounds;
            minX = Math.Min(minX, mn.X); minY = Math.Min(minY, mn.Y);
            maxX = Math.Max(maxX, mx.X); maxY = Math.Max(maxY, mx.Y);
        }
        cell = Math.Max(0.25, cell);
        // Malha limitada (projetos grandes): no máximo ~250 mil amostras.
        var n = (maxX - minX) * (maxY - minY) / (cell * cell);
        if (n > 250_000) cell *= Math.Sqrt(n / 250_000);
        double cut = 0, fill = 0, area = 0;
        for (var x = minX + cell / 2; x < maxX; x += cell)
            for (var y = minY + cell / 2; y < maxY; y += cell)
            {
                var p = new Vec2(x, y);
                var dz = res.DesignZ(p);
                if (dz == null) continue;
                var g = ground(p);
                if (g == null) continue;
                area += cell * cell;
                var d = g.Value - dz.Value;
                if (d > 0) cut += d * cell * cell; else fill -= d * cell * cell;
            }
        res.CutM3 = cut;
        res.FillM3 = fill;
        res.AreaM2 = area;
    }
}
