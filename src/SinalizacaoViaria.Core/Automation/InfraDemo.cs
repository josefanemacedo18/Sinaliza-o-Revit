using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Prévias e testes: monta a via do plugin (todas as marcas da seção) com o greide e a obra hospedada nela – o mesmo que o Revit
/// gera, com os pisos e as pinturas levantados em trechos curtos para acompanhar o greide.
/// </summary>
public static class InfraDemo
{
    public const string DemoGroup = "demo-via";

    /// <summary>Geometria da via (seção <paramref name="setup"/>, greide) + obras hospedadas no mesmo eixo.</summary>
    public static MarkingGeometry RoadWith(RoadSetup setup, Polyline2 axis, RoadGrade grade, IEnumerable<MarkingDefinition> hosted, Catalog.Catalogo catalog,
        Func<Vec2, double?>? ground = null, double chunk = 3, IReadOnlyList<Polygon2>? cuts = null, bool cutPavement = true)
    {
        var output = new OutputSettings { Grade = grade.Clone() };
        var defs = setup.Build(PathReference.FromPoints(axis.Points, 0), output, catalog, DemoGroup);
        if (cuts != null)
            foreach (var d in defs.Where(x => cutPavement || x is not (RoadPavementDefinition or DeviceMarkingDefinition)))
                foreach (var c in cuts) d.Exclusions.Add(new ExclusionZone { SourceId = "demo", Points = c.Outer.ToList() });
        var works = hosted.ToList();
        foreach (var w in works)
        {
            if (w is IHostedStructure h) h.HostRoad = DemoGroup;
            w.Output.Grade = grade.Clone();
        }
        var all = defs.Concat(works).ToList();
        var ctx = new BuildContext { Catalog = catalog, AllDefinitions = () => all, Ground = ground, NativeTerrain = true };
        var surf = new GradeSurface(axis, grade);
        var geo = new MarkingGeometry();
        var dense = new Polyline2(CurveTools.Densify(axis.Points, 2.0), axis.Closed);
        foreach (var d in defs)
        {
            var g = MarkingBuilder.Build(d, dense, ctx);
            GradeLift.Apply(g, surf);
            LiftFlat(g, surf, axis, chunk);
            geo.Merge(g);
        }
        foreach (var w in works) geo.Merge(MarkingBuilder.Build(w, axis, ctx));
        return geo;
    }

    /// <summary>
    /// Prévia completa: via + obras + terreno já terraplenado (como fica o Toposolid), em malha de triângulos finos.
    /// </summary>
    public static MarkingGeometry Preview(RoadSetup setup, Polyline2 axis, RoadGrade grade, IEnumerable<MarkingDefinition> works, Catalog.Catalogo catalog,
        Func<Vec2, double> ground, double margin = 35)
    {
        var list = works.ToList();
        var geo = RoadWith(setup, axis, grade, list, catalog, p => ground(p), 6);
        var design = Grade(setup, axis, grade, list, geo, p => ground(p));
        geo.Corridors.Clear();
        if (geo.Bounds is { } b)
        {
            var step = Math.Max(4, Math.Max(b.Max.X - b.Min.X, b.Max.Y - b.Min.Y) / 70);
            geo.Pieces.InsertRange(0, TerrainPieces(p => (design.DesignZ(p) ?? ground(p)) - 0.03, b.Min - new Vec2(margin, margin), b.Max + new Vec2(margin, margin), step));
        }
        return geo;
    }

    /// <summary>Superfície do terreno como triângulos finos (prévias).</summary>
    public static IEnumerable<MarkingPiece> TerrainPieces(Func<Vec2, double> z, Vec2 min, Vec2 max, double step)
    {
        for (var x = min.X; x < max.X - 1e-9; x += step)
            for (var y = min.Y; y < max.Y - 1e-9; y += step)
            {
                var q = new[] { new Vec2(x, y), new Vec2(x + step, y), new Vec2(x + step, y + step), new Vec2(x, y + step) };
                foreach (var tri in new[] { new[] { q[0], q[1], q[2] }, new[] { q[0], q[2], q[3] } })
                {
                    var top = tri.Select(p => Vec3.At(p, z(p))).ToList();
                    if (Polyhedron.Normal(top).Z < 0) top.Reverse();
                    var faces = new List<List<Vec3>> { top };
                    var poly = new Polyhedron(faces);
                    yield return new MarkingPiece(new Polygon2(tri), MarkingColor.Grama) { Solid = poly, Thickness = poly.MaxZ, Layer = "TERRENO" };
                }
            }
    }

    /// <summary>
    /// Terraplenagem da via com as obras (prévias): plataforma da via no greide fora dos trechos em obra + faixas das obras,
    /// taludes até o terreno natural – o que o Revit aplica ao Toposolid.
    /// </summary>
    public static GradingResult Grade(RoadSetup setup, Polyline2 axis, RoadGrade grade, IEnumerable<MarkingDefinition> works, MarkingGeometry worksGeo,
        Func<Vec2, double?> ground, double subgrade = 0.4) =>
        Grading.Design(Corridors(setup, axis, grade, works, worksGeo, subgrade), worksGeo.Pads, ground, 0, 80, 0.5, 4, 2);

    /// <summary>Faixas de terraplenagem da via (fora dos trechos em obra) + as das obras.</summary>
    public static List<GradeCorridor> Corridors(RoadSetup setup, Polyline2 axis, RoadGrade grade, IEnumerable<MarkingDefinition> works, MarkingGeometry worksGeo,
        double subgrade = 0.4)
    {
        var pav = setup.PavementDefinition();
        var prof = grade.ToProfile(2);
        var gaps = works.OfType<IHostedStructure>().Select(h => (A: Math.Min(h.HostStart, h.HostEnd), B: Math.Max(h.HostStart, h.HostEnd))).OrderBy(x => x.A).ToList();
        var corridors = new List<GradeCorridor>();
        var cursor = 0.0;
        var L = axis.Length;
        foreach (var (a, b) in gaps.Append((L + 1, L + 2)))
        {
            var e = Math.Min(a, L);
            if (e - cursor > 2)
                corridors.Add(Generators.Infra.Corridor(axis, prof, -(pav.TotalRight + 0.3), pav.TotalLeft + 0.3, cursor, e, subgrade, grade.CutSlope, grade.FillSlope));
            cursor = Math.Max(cursor, b);
            if (cursor >= L) break;
        }
        corridors.AddRange(worksGeo.Corridors);
        return corridors;
    }

    /// <summary>Peças planas (pisos, pinturas) cortadas em trechos ao longo do eixo e levantadas até o greide.</summary>
    public static void LiftFlat(MarkingGeometry geo, GradeSurface surf, Polyline2 axis, double step)
    {
        var strips = new List<(Polygon2 Strip, double S)>();
        var L = axis.Length;
        for (var s = 0.0; s < L - 1e-6; s += step)
        {
            var e = Math.Min(L, s + step);
            var a = axis.PointAt(s);
            var b = axis.PointAt(e);
            var na = axis.TangentAt(s).PerpLeft * 80;
            var nb = axis.TangentAt(e).PerpLeft * 80;
            strips.Add((new Polygon2(new[] { a - na, b - nb, b + nb, a + na }), (s + e) / 2));
        }
        var res = new List<MarkingPiece>();
        foreach (var p in geo.Pieces)
        {
            if (p.Solid != null || p.Profile != null) { res.Add(p); continue; }
            var (mn, mx) = p.Shape.Bounds;
            if (Math.Max(mx.X - mn.X, mx.Y - mn.Y) <= step * 1.2)
            {
                res.Add(p with { Elevation = p.Elevation + surf.Z(p.Shape.Centroid) });
                continue;
            }
            foreach (var (strip, _) in strips)
                foreach (var part in PolygonOps.Intersect(new[] { p.Shape }, new[] { strip }).Where(x => x.Area > 1e-4))
                    res.Add(p with { Shape = part, Elevation = p.Elevation + surf.Z(part.Centroid) });
        }
        geo.Pieces.Clear();
        geo.Pieces.AddRange(res);
    }
}
