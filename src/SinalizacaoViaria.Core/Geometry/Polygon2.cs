namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Polígono simples com furos opcionais. O contorno externo é mantido em sentido anti-horário
/// e os furos em sentido horário (convenção usada na conversão para CurveLoops do Revit).
/// </summary>
public sealed class Polygon2
{
    public IReadOnlyList<Vec2> Outer { get; }
    public IReadOnlyList<IReadOnlyList<Vec2>> Holes { get; }

    public Polygon2(IEnumerable<Vec2> outer, IEnumerable<IEnumerable<Vec2>>? holes = null)
    {
        var o = outer.ToList();
        if (SignedArea(o) < 0) o.Reverse();
        Outer = o;

        var hs = new List<IReadOnlyList<Vec2>>();
        if (holes != null)
        {
            foreach (var h in holes)
            {
                var hl = h.ToList();
                if (hl.Count < 3) continue;
                if (SignedArea(hl) > 0) hl.Reverse();
                hs.Add(hl);
            }
        }
        Holes = hs;
    }

    /// <summary>Área líquida (contorno menos furos), em m².</summary>
    public double Area => Math.Abs(SignedArea(Outer)) - Holes.Sum(h => Math.Abs(SignedArea(h)));

    public bool IsValid => Outer.Count >= 3 && Area > 1e-8;

    public (Vec2 Min, Vec2 Max) Bounds
    {
        get
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in Outer)
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }
            return (new Vec2(minX, minY), new Vec2(maxX, maxY));
        }
    }

    /// <summary>Centróide de área do contorno externo.</summary>
    public Vec2 Centroid => CentroidOf(Outer);

    public Polygon2 Transform(Func<Vec2, Vec2> f) =>
        new(Outer.Select(f), Holes.Select(h => h.Select(f)));

    public bool Contains(Vec2 p)
    {
        if (!PointInRing(Outer, p)) return false;
        foreach (var h in Holes)
            if (PointInRing(h, p)) return false;
        return true;
    }

    /// <summary>
    /// Remove vértices duplicados/colineares e arestas menores que <paramref name="minEdge"/>.
    /// Necessário porque o Revit rejeita curvas menores que a tolerância de curva curta (~0,8 mm).
    /// </summary>
    public Polygon2? Simplified(double minEdge = 0.002, double collinearTol = 1e-5)
    {
        var o = SimplifyRing(Outer, minEdge, collinearTol);
        if (o == null) return null;
        var hs = Holes.Select(h => SimplifyRing(h, minEdge, collinearTol)).Where(h => h != null).Cast<IReadOnlyList<Vec2>>();
        var p = new Polygon2(o, hs);
        return p.IsValid ? p : null;
    }

    public static double SignedArea(IReadOnlyList<Vec2> ring)
    {
        double a = 0;
        for (int i = 0, n = ring.Count; i < n; i++)
        {
            var p = ring[i];
            var q = ring[(i + 1) % n];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a * 0.5;
    }

    public static Vec2 CentroidOf(IReadOnlyList<Vec2> ring)
    {
        double a = 0, cx = 0, cy = 0;
        for (int i = 0, n = ring.Count; i < n; i++)
        {
            var p = ring[i];
            var q = ring[(i + 1) % n];
            var cr = p.X * q.Y - q.X * p.Y;
            a += cr;
            cx += (p.X + q.X) * cr;
            cy += (p.Y + q.Y) * cr;
        }
        if (Math.Abs(a) < 1e-12)
            return ring.Count == 0 ? Vec2.Zero : new Vec2(ring.Average(p => p.X), ring.Average(p => p.Y));
        a *= 0.5;
        return new Vec2(cx / (6 * a), cy / (6 * a));
    }

    public static bool PointInRing(IReadOnlyList<Vec2> ring, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static IReadOnlyList<Vec2>? SimplifyRing(IReadOnlyList<Vec2> ring, double minEdge, double collinearTol)
    {
        var pts = new List<Vec2>(ring.Count);
        foreach (var p in ring)
        {
            if (pts.Count == 0 || pts[^1].DistanceTo(p) >= minEdge) pts.Add(p);
        }
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) < minEdge) pts.RemoveAt(pts.Count - 1);

        bool changed = true;
        while (changed && pts.Count > 3)
        {
            changed = false;
            for (int i = 0; i < pts.Count && pts.Count > 3; i++)
            {
                var prev = pts[(i - 1 + pts.Count) % pts.Count];
                var cur = pts[i];
                var next = pts[(i + 1) % pts.Count];
                var d1 = cur - prev;
                var d2 = next - cur;
                var len = d1.Length * d2.Length;
                if (len < 1e-14 || Math.Abs(d1.Cross(d2)) / len < collinearTol && d1.Dot(d2) > 0)
                {
                    pts.RemoveAt(i);
                    changed = true;
                    i--;
                }
            }
        }
        return pts.Count >= 3 && Math.Abs(SignedArea(pts)) > 1e-9 ? pts : null;
    }

    public static Polygon2 Rectangle(Vec2 min, Vec2 max) =>
        new(new[] { min, new Vec2(max.X, min.Y), max, new Vec2(min.X, max.Y) });

    /// <summary>Retângulo orientado: centro do lado inicial em <paramref name="start"/>, eixo ao longo de <paramref name="dir"/>.</summary>
    public static Polygon2 OrientedRectangle(Vec2 start, Vec2 dir, double length, double width)
    {
        var d = dir.Normalized();
        var n = d.PerpLeft * (width / 2);
        var e = start + d * length;
        return new Polygon2(new[] { start - n, e - n, e + n, start + n });
    }
}
