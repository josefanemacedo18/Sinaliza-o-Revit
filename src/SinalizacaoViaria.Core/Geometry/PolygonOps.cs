using Clipper2Lib;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Operações booleanas e de deslocamento robustas sobre polígonos (via Clipper2).
/// Precisão de 4 casas decimais = 0,1 mm, suficiente para sinalização viária.
/// </summary>
public static class PolygonOps
{
    public const int Precision = 4;

    /// <summary>Faixa (polígono) de largura <paramref name="width"/> centrada sobre uma polilinha aberta, com pontas retas.</summary>
    public static List<Polygon2> Strip(IReadOnlyList<Vec2> centerline, double width, bool roundJoins = false)
    {
        if (centerline.Count < 2 || width <= 0) return new();
        var path = ToPath(centerline);
        var res = Clipper.InflatePaths(new PathsD { path }, width / 2,
            roundJoins ? JoinType.Round : JoinType.Miter, EndType.Butt, 4.0, Precision, 0.005);
        return ToPolygons(res);
    }

    /// <summary>Faixa fechada (anel) sobre uma polilinha fechada.</summary>
    public static List<Polygon2> ClosedStrip(IReadOnlyList<Vec2> ring, double width)
    {
        if (ring.Count < 3 || width <= 0) return new();
        var res = Clipper.InflatePaths(new PathsD { ToPath(ring) }, width / 2, JoinType.Miter, EndType.Joined, 4.0, Precision);
        return ToPolygons(res);
    }

    /// <summary>Deslocamento de polígonos (positivo expande, negativo contrai).</summary>
    public static List<Polygon2> Offset(IEnumerable<Polygon2> polys, double delta, bool round = false)
    {
        var res = Clipper.InflatePaths(ToPaths(polys), delta, round ? JoinType.Round : JoinType.Miter, EndType.Polygon, 4.0, Precision);
        return ToPolygons(res);
    }

    public static List<Polygon2> Union(IEnumerable<Polygon2> polys) => Boolean(ClipType.Union, polys, Array.Empty<Polygon2>());

    public static List<Polygon2> Intersect(IEnumerable<Polygon2> a, IEnumerable<Polygon2> b) => Boolean(ClipType.Intersection, a, b);

    public static List<Polygon2> Difference(IEnumerable<Polygon2> a, IEnumerable<Polygon2> b) => Boolean(ClipType.Difference, a, b);

    public static List<Polygon2> Boolean(ClipType type, IEnumerable<Polygon2> subject, IEnumerable<Polygon2> clip, FillRule rule = FillRule.NonZero)
    {
        var c = new ClipperD(Precision);
        c.AddSubject(ToPaths(subject));
        var clipPaths = ToPaths(clip);
        if (clipPaths.Count > 0) c.AddClip(clipPaths);
        var tree = new PolyTreeD();
        c.Execute(type, rule, tree);
        return FromTree(tree);
    }

    /// <summary>Resolve contornos soltos (ex.: glifos de fonte) em polígonos com furos.</summary>
    public static List<Polygon2> FromContours(IEnumerable<IReadOnlyList<Vec2>> contours, FillRule rule = FillRule.NonZero)
    {
        var paths = new PathsD();
        foreach (var ct in contours)
            if (ct.Count >= 3) paths.Add(ToPath(ct));
        var c = new ClipperD(Precision);
        c.AddSubject(paths);
        var tree = new PolyTreeD();
        c.Execute(ClipType.Union, rule, tree);
        return FromTree(tree);
    }

    public static double TotalArea(IEnumerable<Polygon2> polys) => polys.Sum(p => p.Area);

    /// <summary>
    /// Divide um polígono com furos em partes sem furos (cortes verticais pelos furos, sem folga entre as partes) –
    /// contornos mais simples para o esboço de pisos.
    /// </summary>
    public static List<Polygon2> SplitHoles(Polygon2 poly, int maxDepth = 32)
    {
        if (poly.Holes.Count == 0 || maxDepth <= 0) return new List<Polygon2> { poly };
        var (mn, mx) = poly.Bounds;
        var hole = poly.Holes.OrderByDescending(h => new Polygon2(h).Area).First();
        var cx = hole.Average(v => v.X);
        var left = Intersect(new[] { poly }, new[] { Polygon2.Rectangle(new Vec2(mn.X - 1, mn.Y - 1), new Vec2(cx, mx.Y + 1)) });
        var right = Intersect(new[] { poly }, new[] { Polygon2.Rectangle(new Vec2(cx, mn.Y - 1), new Vec2(mx.X + 1, mx.Y + 1)) });
        return left.Concat(right).Where(p => p.Area > 1e-6).SelectMany(p => SplitHoles(p, maxDepth - 1)).ToList();
    }

    /// <summary>Limpa lascas e vértices quase coincidentes (fecha e abre com <paramref name="r"/>).</summary>
    public static List<Polygon2> Clean(IEnumerable<Polygon2> polys, double r = 0.003) =>
        Offset(Offset(Offset(Offset(polys, -r), r), r), -r).Select(p => p.Simplified(0.01) ?? p).Where(p => p.Area > 1e-4).ToList();


    // ---------------------------------------------------------------- conversões

    internal static PathD ToPath(IReadOnlyList<Vec2> pts)
    {
        var p = new PathD(pts.Count);
        foreach (var v in pts) p.Add(new PointD(v.X, v.Y));
        return p;
    }

    internal static PathsD ToPaths(IEnumerable<Polygon2> polys)
    {
        var res = new PathsD();
        foreach (var poly in polys)
        {
            res.Add(ToPath(poly.Outer));
            foreach (var h in poly.Holes) res.Add(ToPath(h));
        }
        return res;
    }

    private static List<Vec2> ToList(PathD p) => p.Select(pt => new Vec2(pt.x, pt.y)).ToList();

    /// <summary>Converte um conjunto de caminhos (saída plana do Clipper) em polígonos com furos.</summary>
    internal static List<Polygon2> ToPolygons(PathsD paths)
    {
        var c = new ClipperD(Precision);
        c.AddSubject(paths);
        var tree = new PolyTreeD();
        c.Execute(ClipType.Union, FillRule.NonZero, tree);
        return FromTree(tree);
    }

    private static List<Polygon2> FromTree(PolyTreeD tree)
    {
        var res = new List<Polygon2>();
        for (int i = 0; i < tree.Count; i++) Collect(tree[i], res);
        return res;
    }

    private static void Collect(PolyPathD node, List<Polygon2> acc)
    {
        if (node.Polygon == null || node.Polygon.Count < 3) return;
        var holes = new List<List<Vec2>>();
        for (int i = 0; i < node.Count; i++)
        {
            var hole = node[i];
            if (hole.Polygon != null && hole.Polygon.Count >= 3) holes.Add(ToList(hole.Polygon));
            // ilhas dentro de furos
            for (int j = 0; j < hole.Count; j++) Collect(hole[j], acc);
        }
        var poly = new Polygon2(ToList(node.Polygon), holes);
        if (poly.IsValid) acc.Add(poly);
    }
}
