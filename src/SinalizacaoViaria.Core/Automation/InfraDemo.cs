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
        Func<Vec2, double?>? ground = null, double chunk = 3, IReadOnlyList<Polygon2>? cuts = null, bool cutPavement = true,
        IReadOnlyList<Polygon2>? sideCuts = null)
    {
        var output = new OutputSettings { Grade = grade.Clone() };
        var defs = setup.Build(PathReference.FromPoints(axis.Points, 0), output, catalog, DemoGroup);
        if (cuts != null)
            foreach (var d in defs.Where(x => cutPavement || x is not (RoadPavementDefinition or DeviceMarkingDefinition)))
                foreach (var c in cuts) d.Exclusions.Add(new ExclusionZone { SourceId = "demo", Points = c.Outer.ToList() });
        // Elementos laterais (calçada, meio-fio, sarjeta) fora da área de outras vias – como no Revit.
        if (sideCuts != null)
            foreach (var d in defs.Where(x => Generators.IntersectionGenerator.IsPhysical(x) || x is PlanterDefinition))
                foreach (var c in sideCuts) d.Exclusions.Add(new ExclusionZone { SourceId = "demo-vias", Points = c.Outer.ToList() });
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
            var step = Math.Max(3, Math.Max(b.Max.X - b.Min.X, b.Max.Y - b.Min.Y) / 90);
            var bores = geo.Pieces.Where(p => p.Layer == Generators.EarthworksGenerator.BoreLayer).ToList();
            geo.Pieces.InsertRange(0, Excavate(Toposolid(ground, design, b.Min - new Vec2(margin, margin), b.Max + new Vec2(margin, margin), step), bores));
        }
        return geo;
    }

    /// <summary>
    /// Terreno como o Revit vai montar o Toposolid: levantamento regular (<paramref name="step"/>) + pontos da terraplenagem,
    /// triangulados por Delaunay (mesma regra do Toposolid) – não a superfície de projeto idealizada.
    /// </summary>
    public static IEnumerable<MarkingPiece> Toposolid(Func<Vec2, double> ground, GradingResult design, Vec2 min, Vec2 max, double step)
    {
        var survey = TerrainMesh.FromFunction(min, max, step, ground);
        var topo = TerrainRebuild.Simulate(survey, design);
        return MeshPieces(topo, -0.03);
    }

    /// <summary>Tira do terreno (prévia) os triângulos dentro do volume de escavação dos túneis – como o Revit escava o Toposolid.</summary>
    public static IEnumerable<MarkingPiece> Excavate(IEnumerable<MarkingPiece> terrain, IReadOnlyList<MarkingPiece> bores)
    {
        if (bores.Count == 0) { foreach (var t in terrain) yield return t; yield break; }
        var boxes = bores.Where(b => b.Solid != null).Select(b => (Shape: b.Shape, Top: b.Solid!.MaxZ + b.Elevation)).ToList();
        foreach (var t in terrain)
        {
            var c = t.Shape.Centroid;
            var z = t.Solid?.Faces[0].Average(v => v.Z) ?? 0;
            if (boxes.Any(b => z < b.Top - 0.05 && b.Shape.Contains(c))) continue;
            yield return t;
        }
    }

    /// <summary>Triângulos de uma malha de terreno como peças (prévias).</summary>
    public static IEnumerable<MarkingPiece> MeshPieces(TerrainMesh m, double dz = 0)
    {
        foreach (var (ia, ib, ic) in m.Triangles)
        {
            var top = new List<Vec3> { m.Vertices[ia], m.Vertices[ib], m.Vertices[ic] }.Select(v => v with { Z = v.Z + dz }).ToList();
            if (Polyhedron.Normal(top).Z < 0) top.Reverse();
            var tri = new Polygon2(top.Select(v => v.XY));
            if (Math.Abs(tri.Area) < 1e-6) continue;
            var poly = new Polyhedron(new List<List<Vec3>> { top });
            yield return new MarkingPiece(tri, MarkingColor.Grama) { Solid = poly, Thickness = poly.MaxZ, Layer = "TERRENO" };
        }
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
        var corridors = InfraRoads.RoadCorridors(axis, prof, -(pav.TotalRight + 0.3), pav.TotalLeft + 0.3, 0, axis.Length, subgrade, grade.CutSlope, grade.FillSlope, works);
        corridors.AddRange(worksGeo.Corridors);
        return corridors;
    }

    /// <summary>
    /// Peça plana deformada pelo greide vértice a vértice (como o piso do Revit com edição de forma): face superior contínua,
    /// sem degraus entre os trechos.
    /// </summary>
    private static MarkingPiece Draped(MarkingPiece p, Polygon2 part, GradeSurface surf, List<double> grid)
    {
        if (part.Holes.Count > 0) return p with { Shape = part, Elevation = p.Elevation + surf.Z(part.Centroid) };
        // Mesma malha do Revit: vértices do contorno nas estacas da grade + pontos na crista do abaulamento.
        var ring = GradeFloors.Resample(part, surf, grid).Outer.ToList();
        ring.AddRange(GradeFloors.Supports(part, surf, grid));
        var top = ring.Select(v => Vec3.At(v, surf.Z(v) + p.Elevation + Math.Max(0.001, p.Thickness))).ToList();
        // Triângulos (Delaunay dos vértices do contorno, só os de dentro): contornos côncavos nas curvas ficam certos.
        var faces = new List<List<Vec3>>();
        foreach (var (a, b, c) in Tin.Triangulate(top))
        {
            var tri = new List<Vec3> { top[a], top[b], top[c] };
            var cen = new Vec2((tri[0].X + tri[1].X + tri[2].X) / 3, (tri[0].Y + tri[1].Y + tri[2].Y) / 3);
            if (!part.Contains(cen)) continue;
            if (Polyhedron.Normal(tri).Z < 0) tri.Reverse();
            faces.Add(tri);
        }
        if (faces.Count == 0) return p with { Shape = part, Elevation = p.Elevation + surf.Z(part.Centroid) };
        var poly = new Polyhedron(faces);
        return p with { Shape = part, Solid = poly, Elevation = 0, Thickness = poly.MaxZ };
    }

    /// <summary>Piso empenado (edição de forma em malha regular de <paramref name="step"/> m), para superfícies torcidas.</summary>
    public static MarkingPiece Warped(MarkingPiece p, Polygon2 part, Func<Vec2, double> z, double step = 1.5)
    {
        var ring = CurveTools.Densify(part.Outer.Append(part.Outer[0]).ToList(), step).SkipLast(1).ToList();
        var inner = PolygonOps.Offset(new[] { part }, -0.3);
        var (mn, mx) = part.Bounds;
        for (var x = mn.X + step / 2; x < mx.X; x += step)
            for (var y = mn.Y + step / 2; y < mx.Y; y += step)
                if (inner.Any(e => e.Contains(new Vec2(x, y)))) ring.Add(new Vec2(x, y));
        var up = p.Elevation + Math.Max(0.001, p.Thickness);
        var top = ring.Select(v => Vec3.At(v, z(v) + up)).ToList();
        var faces = new List<List<Vec3>>();
        foreach (var (a, b, c) in Tin.Triangulate(top))
        {
            var tri = new List<Vec3> { top[a], top[b], top[c] };
            var cen = new Vec2((tri[0].X + tri[1].X + tri[2].X) / 3, (tri[0].Y + tri[1].Y + tri[2].Y) / 3);
            if (!part.Contains(cen)) continue;
            if (Polyhedron.Normal(tri).Z < 0) tri.Reverse();
            faces.Add(tri);
        }
        if (faces.Count == 0) return p with { Shape = part, Elevation = p.Elevation + z(part.Centroid) };
        var poly = new Polyhedron(faces);
        return p with { Shape = part, Solid = poly, Elevation = 0, Thickness = poly.MaxZ };
    }

    /// <summary>Piso plano inclinado (topo = plano da parte), com espessura constante na vertical.</summary>
    public static MarkingPiece Tilted(MarkingPiece p, Polygon2 part, Plane3 plane)
    {
        var up = p.Elevation + Math.Max(0.001, p.Thickness);
        if (part.Holes.Count > 0 || plane.IsLevel)
            return p with { Shape = part, Elevation = p.Elevation + plane.Z(part.Centroid) };
        var ring = part.Outer.ToList();
        if (Polygon2.SignedArea(ring) < 0) ring.Reverse();
        if (Polyhedron.Prism(ring, v => plane.Z(v) + p.Elevation, v => plane.Z(v) + up) is not { } poly) return p with { Shape = part, Elevation = p.Elevation + plane.Z(part.Centroid) };
        return p with { Shape = part, Solid = poly, Elevation = 0, Thickness = poly.MaxZ };
    }

    /// <summary>Peças planas (pisos, pinturas) cortadas em trechos ao longo do eixo e levantadas até o greide.</summary>
    public static void LiftFlat(MarkingGeometry geo, GradeSurface surf, Polyline2 axis, double step)
    {
        _ = axis; _ = step;
        var grid = GradeFloors.Grid(surf);
        var res = new List<MarkingPiece>();
        foreach (var p in geo.Pieces)
        {
            if (p.Solid != null || p.Profile != null) { res.Add(p); continue; }
            // Como no Revit: o piso é dividido em partes PLANAS (desvio ≤ 12 mm) e cada parte é um piso plano inclinado.
            // Pintura 1,5 cm acima da superfície teórica (como no Revit): nunca some sob os pisos planos.
            var q = MarkingColors.IsPaint(p.Color) ? p with { Elevation = p.Elevation + 0.015 } : p;
            foreach (var (part, plane, warped) in FloorPlanes.Split(p.Shape, surf.Z, 0.012, surf))
                if (warped) res.Add(Draped(q, part, surf, grid));
                else
                    foreach (var solid in part.Holes.Count > 0 ? PolygonOps.SplitHoles(part) : new List<Polygon2> { part })
                        res.Add(Tilted(q, solid, plane));
        }
        geo.Pieces.Clear();
        geo.Pieces.AddRange(res);
    }
}
