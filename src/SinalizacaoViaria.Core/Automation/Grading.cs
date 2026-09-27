using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Superfície de projeto da terraplenagem e volumes de corte/aterro.</summary>
public sealed class GradingResult
{
    /// <summary>Triângulos da superfície de projeto (faixas e taludes), cotas absolutas (m).</summary>
    public List<(Vec3 A, Vec3 B, Vec3 C)> Triangles { get; } = new();
    /// <summary>Plataformas planas (polígono, cota absoluta).</summary>
    public List<(Polygon2 Area, double Z)> Pads { get; } = new();
    /// <summary>Pontos a inserir no terreno (bordas, pés/cristas de talude, pontos internos das plataformas).</summary>
    public List<Vec3> Points { get; } = new();
    /// <summary>Contorno de tudo o que é alterado (plataformas + taludes).</summary>
    public List<Polygon2> Footprint { get; } = new();
    public double CutM3 { get; set; }
    public double FillM3 { get; set; }
    public double AreaM2 { get; set; }
    public List<string> Warnings { get; } = new();

    /// <summary>Cota de projeto no ponto (nulo = fora da área terraplenada).</summary>
    public double? DesignZ(Vec2 p)
    {
        foreach (var (area, z) in Pads) if (area.Contains(p)) return z;
        foreach (var (a, b, c) in Triangles)
        {
            var d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(d) < 1e-12) continue;
            var l1 = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / d;
            var l2 = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / d;
            var l3 = 1 - l1 - l2;
            if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
            return l1 * a.Z + l2 * b.Z + l3 * c.Z;
        }
        return null;
    }
}

/// <summary>
/// Terraplenagem: a partir das faixas e plataformas de projeto (bordas cotadas), encontra o encontro dos taludes de corte
/// (H:V) e de aterro com o terreno natural ("offsets"), monta a superfície de projeto e calcula os volumes. O Revit aplica
/// o resultado ao Toposolid (Massa e terreno) – vias, conexões e obras ficam planas/no greide e o terreno se ajusta a elas.
/// </summary>
public static class Grading
{
    /// <param name="ground">Cota do terreno natural (m, absoluta) – nulo fora do terreno.</param>
    /// <param name="baseZ">Cota da base das marcas (somada às cotas relativas das faixas/plataformas).</param>
    public static GradingResult Design(IEnumerable<GradeCorridor> corridors, IEnumerable<GradePad> pads, Func<Vec2, double?> ground, double baseZ = 0,
        double maxDaylight = 60, double step = 0.5, double padGrid = 4.0, double sampling = 1.0)
    {
        var res = new GradingResult();
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
                res.Footprint.Add(new Polygon2(new[] { left[i].XY, left[i + 1].XY, right[i + 1].XY, right[i].XY }));
            }
            res.Points.AddRange(left);
            res.Points.AddRange(right);
            for (int i = 0; i < n; i++) res.Points.Add(new Vec3((left[i].X + right[i].X) / 2, (left[i].Y + right[i].Y) / 2, (left[i].Z + right[i].Z) / 2));
            // Taludes: do lado de fora de cada borda.
            if (c.DaylightLeft) SlopeBand(res, left, right, c.CutSlope, c.FillSlope, ground, maxDaylight, step);
            if (c.DaylightRight) SlopeBand(res, right, left, c.CutSlope, c.FillSlope, ground, maxDaylight, step, reverse: true);
        }
        foreach (var pad in pads)
        {
            var outer = pad.Area.Outer.ToList();
            if (outer.Count < 3) continue;
            var z = pad.Z + baseZ;
            res.Pads.Add((pad.Area, z));
            res.Footprint.Add(pad.Area);
            var ring = CurveTools.Densify(outer, 2.0);
            if (ring.Count > 1 && ring[0].AlmostEquals(ring[^1], 1e-6)) ring.RemoveAt(ring.Count - 1);
            res.Points.AddRange(ring.Select(p => Vec3.At(p, z)));
            // Pontos internos: a plataforma fica plana também no miolo.
            var (mn, mx) = pad.Area.Bounds;
            for (var x = mn.X + padGrid / 2; x < mx.X; x += padGrid)
                for (var y = mn.Y + padGrid / 2; y < mx.Y; y += padGrid)
                {
                    var p = new Vec2(x, y);
                    if (pad.Area.Contains(p) && Distance(ring, p) > 0.5) res.Points.Add(Vec3.At(p, z));
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

    private static void SlopeBand(GradingResult res, List<Vec3> edge, List<Vec3> other, double cut, double fill, Func<Vec2, double?> ground,
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
            var quad = new Polygon2(new[] { edge[i].XY, edge[i + 1].XY, day[i + 1].XY, day[i].XY });
            if (quad.Area > 1e-4) res.Footprint.Add(quad);
        }
        res.Points.AddRange(day.Where((d, i) => d.XY.DistanceTo(edge[i].XY) > 0.05));
    }

    private static void PadSlopes(GradingResult res, List<Vec2> ring, double z, double cut, double fill, Func<Vec2, double?> ground, double maxDist, double step)
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
            day.Add(Daylight(ring[i], z, n, cut, fill, ground, maxDist, step));
        }
        for (int i = 0; i < ring.Count; i++)
        {
            var j = (i + 1) % ring.Count;
            var a = Vec3.At(ring[i], z);
            var b = Vec3.At(ring[j], z);
            res.Triangles.Add((a, b, day[j]));
            res.Triangles.Add((a, day[j], day[i]));
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
