using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada K: corte e aterro no terreno nativo – o Toposolid (simulado pela mesma triangulação) reproduz o projeto.</summary>
public class V22Tests
{
    private static Polyline2 Straight(double len) => new(new[] { new Vec2(0, 0), new Vec2(len, 0) });

    /// <summary>Colina: sobe até 12 m no meio (x = 150).</summary>
    private static double Hill(Vec2 p) => 12 * Math.Exp(-Math.Pow((p.X - 150) / 70, 2)) + 0.02 * p.Y;

    private static TerrainMesh Terrain() => TerrainMesh.FromFunction(new Vec2(-60, -80), new Vec2(360, 80), 5, Hill);

    [Fact]
    public void Tin_ReproducesPlane()
    {
        var rnd = new Random(3);
        var pts = Enumerable.Range(0, 800).Select(_ => { var x = rnd.NextDouble() * 100; var y = rnd.NextDouble() * 60; return new Vec3(x, y, 2 + 0.1 * x - 0.05 * y); }).ToList();
        var tris = Tin.Triangulate(pts);
        Assert.InRange(tris.Count, 1400, 1600);             // ~2n triângulos
        var m = Tin.Mesh(pts);
        for (var x = 10.0; x < 90; x += 7.3)
            for (var y = 10.0; y < 50; y += 5.1)
                Assert.Equal(2 + 0.1 * x - 0.05 * y, m.Z(new Vec2(x, y)) ?? double.NaN, 6);
    }

    [Fact]
    public void Tin_IsDelaunay()
    {
        var rnd = new Random(7);
        var pts = Enumerable.Range(0, 300).Select(_ => new Vec3(rnd.NextDouble() * 50, rnd.NextDouble() * 50, 0)).ToList();
        foreach (var (a, b, c) in Tin.Triangulate(pts))
        {
            // Nenhum ponto dentro do círculo circunscrito.
            var (ax, ay, bx, by, cx, cy) = (pts[a].X, pts[a].Y, pts[b].X, pts[b].Y, pts[c].X, pts[c].Y);
            var d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            var ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
            var uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
            var r2 = (ax - ux) * (ax - ux) + (ay - uy) * (ay - uy);
            foreach (var p in pts) Assert.True((p.X - ux) * (p.X - ux) + (p.Y - uy) * (p.Y - uy) >= r2 - 1e-6);
        }
    }

    [Fact]
    public void RoadInCut_ToposolidFollowsPlatformAndSlopes()
    {
        var axis = Straight(300);
        var prof = VerticalProfile.Flat(2.0);               // greide a 2 m: corte de até ~10 m no alto da colina
        var c = Infra.Corridor(axis, prof, -7, 7, 0, 300, 0.4, 1.0, 1.5);
        var design = Grading.Design(new[] { c }, Array.Empty<GradePad>(), p => Hill(p));
        var topo = TerrainRebuild.Simulate(Terrain(), design);
        // Plataforma no eixo e nas bordas (cota de projeto).
        foreach (var x in new[] { 60.0, 120, 150, 190, 240 })
            foreach (var y in new[] { -6.5, 0, 6.5 })
                Assert.Equal(1.6, topo.Z(new Vec2(x, y)) ?? double.NaN, 1);
        // Talude de corte 1:1 no alto da colina: a 3 m da borda sobe ~3 m.
        Assert.Equal(1.6 + 3, topo.Z(new Vec2(150, 10)) ?? double.NaN, 0);
        // Longe da obra, terreno natural intacto.
        Assert.Equal(Hill(new Vec2(150, 60)), topo.Z(new Vec2(150, 60)) ?? double.NaN, 1);
        Assert.True(design.CutM3 > 5000);
    }

    [Fact]
    public void Trench_WallsKeepNaturalGroundBehind()
    {
        var axis = Straight(300);
        var prof = VerticalProfile.Flat(-2.0);
        var c = Infra.Corridor(axis, prof, -8, 8, 40, 260, 0.4, 1.0, 1.5, false, false, 3);
        c.WallLeft = c.WallRight = true;
        var design = Grading.Design(new[] { c }, Array.Empty<GradePad>(), p => Hill(p));
        var topo = TerrainRebuild.Simulate(Terrain(), design);
        foreach (var x in new[] { 80.0, 150, 220 })
        {
            Assert.Equal(-2.4, topo.Z(new Vec2(x, 0)) ?? double.NaN, 1);                    // fundo da trincheira
            var behind = new Vec2(x, 8.5);                                                  // 0,5 m atrás do muro
            Assert.Equal(Hill(behind), topo.Z(behind) ?? double.NaN, 0);                    // terreno natural, sem rampa
        }
    }

    [Fact]
    public void Trench_WithoutWallStep_WouldSlope()
    {
        // Sem o degrau do muro o terreno escorre até a borda (o defeito que aparecia no Revit).
        var axis = Straight(300);
        var c = Infra.Corridor(axis, VerticalProfile.Flat(-2.0), -8, 8, 40, 260, 0.4, 1.0, 1.5, false, false, 3);
        var design = Grading.Design(new[] { c }, Array.Empty<GradePad>(), p => Hill(p));
        var topo = TerrainRebuild.Simulate(Terrain(), design);
        var behind = new Vec2(150, 8.5);
        Assert.True(Hill(behind) - (topo.Z(behind) ?? 0) > 1.0);
    }

    [Fact]
    public void TunnelPortal_EndWallMakesVerticalFace()
    {
        var axis = Straight(150);
        var c = Infra.Corridor(axis, VerticalProfile.Flat(0), -6, 6, 0, 150, 0.4, 1.0, 1.5);
        c.WallEnd = true;
        var design = Grading.Design(new[] { c }, Array.Empty<GradePad>(), p => Hill(p));
        var topo = TerrainRebuild.Simulate(Terrain(), design);
        Assert.Equal(-0.4, topo.Z(new Vec2(149.5, 0)) ?? double.NaN, 1);                    // pista no emboque
        Assert.Equal(Hill(new Vec2(150.5, 0)), topo.Z(new Vec2(150.5, 0)) ?? double.NaN, 0); // terreno sobre o túnel
    }

    [Fact]
    public void Fill_EmbankmentRisesAboveValley()
    {
        double Valley(Vec2 p) => -8 * Math.Exp(-Math.Pow((p.X - 150) / 60, 2));
        var terrain = TerrainMesh.FromFunction(new Vec2(-60, -80), new Vec2(360, 80), 5, Valley);
        var c = Infra.Corridor(Straight(300), VerticalProfile.Flat(0.4), -6, 6, 0, 300, 0.4, 1.0, 1.5);
        var design = Grading.Design(new[] { c }, Array.Empty<GradePad>(), p => Valley(p));
        var topo = TerrainRebuild.Simulate(terrain, design);
        Assert.Equal(0.0, topo.Z(new Vec2(150, 0)) ?? double.NaN, 1);
        Assert.Equal(-1.5 * 0 - 2.0, topo.Z(new Vec2(150, 9)) ?? double.NaN, 0);          // aterro 1,5:1 → a 3 m da borda desce 2 m
        Assert.True(design.FillM3 > 3000);
    }

    [Fact]
    public void TunnelPortal_CapLimitsFaceAndSlopesAbove()
    {
        var axis = Straight(150);
        var c = Infra.Corridor(axis, VerticalProfile.Flat(0), -6, 6, 0, 150, 0.4, 1.0, 1.5);
        c.WallEnd = true;
        c.WallCap = 9;
        var design = Grading.Design(new[] { c }, Array.Empty<GradePad>(), p => Hill(p));
        var topo = TerrainRebuild.Simulate(Terrain(), design);
        var justIn = topo.Z(new Vec2(150.3, 0)) ?? double.NaN;
        Assert.InRange(justIn, 8.0, 9.5);                                     // face só até a testa (cota 8,6)
        Assert.True(Hill(new Vec2(150.3, 0)) > justIn + 1);                     // acima: talude recortado na encosta
    }

    [Fact]
    public void TrenchGenerator_HasWallSteps()
    {
        var ctx = new BuildContext { Catalog = CatalogService.LoadDefault(), Ground = p => Hill(p), NativeTerrain = true };
        var geo = MarkingBuilder.Build(new TrenchDefinition(), Straight(300), ctx);
        Assert.Contains(geo.Corridors, c => c.Label == "Trincheira" && c.WallLeft && c.WallRight);
    }

    [Fact]
    public void TunnelGenerator_BoreForNativeTerrain()
    {
        var ctx = new BuildContext { Catalog = CatalogService.LoadDefault(), NativeTerrain = true };
        var geo = MarkingBuilder.Build(new TunnelDefinition(), Straight(200), ctx);
        var bore = geo.Pieces.Where(p => p.Layer == EarthworksGenerator.BoreLayer).ToList();
        Assert.NotEmpty(bore);
        var env = EarthworksGenerator.Envelope(EarthworksGenerator.TunnelSection(new TunnelDefinition()).Lining);
        Assert.True(env.Count >= 8);
    }

    [Fact]
    public void CutSlope_ExcavatesIntoHill()
    {
        double T(Vec2 p) => 0.2 * p.Y;
        var ctx = new BuildContext { Catalog = CatalogService.LoadDefault(), Ground = p => T(p), NativeTerrain = true };
        var sl = new SlopeDefinition { Type = TipoTalude.Corte, Height = 6, UphillLeft = true };
        var geo = MarkingBuilder.Build(sl, Straight(60), ctx);
        var design = Grading.Design(geo.Corridors, geo.Pads, p => T(p));
        Assert.True(design.CutM3 > design.FillM3 * 2);
    }

    [Fact]
    public void GradeFloors_ChunksAndSupports()
    {
        var axis = Straight(200);
        var surf = new GradeSurface(axis, new RoadGrade { Crossfall = 0.02, Points = { new(0, 0), new(200, 10) } });
        var shape = Polygon2.Rectangle(new Vec2(0, -7), new Vec2(200, 7));
        var chunks = GradeFloors.Chunks(shape, surf, 40);
        Assert.Equal(5, chunks.Count);
        Assert.Equal(shape.Area, chunks.Sum(c => c.Area), 1);
        var sup = GradeFloors.Supports(shape, surf);
        Assert.All(sup, p => Assert.Equal(0, p.Y, 6));                         // só a crista do abaulamento
        Assert.InRange(sup.Count, 45, 55);                                     // uma por estaca (4 m)
    }

    [Fact]
    public void GradeFloors_ResampleAlignsBothEdgesOnCurve()
    {
        // Via em curva (R = 60 m): o contorno do piso tem vértices a cada 0,5 m, diferentes dos dois lados.
        var axis = new Polyline2(Enumerable.Range(0, 121).Select(i => { var a = i / 120.0 * Math.PI / 2; return new Vec2(60 * Math.Sin(a), 60 - 60 * Math.Cos(a)); }).ToList());
        var surf = new GradeSurface(axis, new RoadGrade { Points = { new(0, 0), new(axis.Length, 4) } });
        var left = CurveTools.Densify(axis.Offset(7).Points.ToList(), 0.5);
        var right = CurveTools.Densify(axis.Offset(-7).Points.ToList(), 0.37);
        var shape = new Polygon2(left.Concat(Enumerable.Reverse(right)).ToList());
        var grid = GradeFloors.Grid(surf);
        Assert.True(grid.Zip(grid.Skip(1), (a, b) => b - a).Max() < 3.6);     // passo menor na curva
        var r = GradeFloors.Resample(shape, surf, grid);
        Assert.True(r.Outer.Count < shape.Outer.Count / 2);                    // bem menos vértices
        Assert.Equal(shape.Area, r.Area, 0);
        // Cada vértice interno das bordas está numa estaca da grade, dos dois lados.
        foreach (var g in grid.Where(g => g > 1 && g < axis.Length - 1))
        {
            var c = axis.PointAt(g);
            var n = axis.TangentAt(g).PerpLeft;
            Assert.Contains(r.Outer, v => v.DistanceTo(c + n * 7) < 0.02);
            Assert.Contains(r.Outer, v => v.DistanceTo(c - n * 7) < 0.02);
        }
    }

    [Fact]
    public void Grade_CloseVerticalCurvesDoNotOverlap()
    {
        // PIVs próximos com curvas longas: antes as parábolas se sobrepunham e o greide dava degraus (calombos no piso).
        var g = new RoadGrade { DefaultCurve = 120, Points = { new(0, 0), new(60, 4), new(90, 4), new(150, 0), new(200, 0) } };
        for (var s = 0.0; s < 200; s += 0.25) Assert.True(Math.Abs(g.Z(s + 0.25) - g.Z(s)) < 0.05, $"salto em {s}");
        // Sem curva pedida, a mínima (K = 10 m/%) evita quinas.
        var k = new RoadGrade { DefaultCurve = 0, Points = { new(0, 0), new(100, 6), new(200, 0) } };
        Assert.True(k.Z(100) < 5.8);
    }

    [Fact]
    public void NodeSurface_MatchesEachRoadAtItsPavement()
    {
        // Via A (x) em rampa de 4 %, via B (y) em nível 1 m acima da base de A; cruzam em (100, 0).
        var a = new GradeSurface(Straight(200), new RoadGrade { Crossfall = 0.02, Points = { new(0, -3), new(200, 5) } });
        var b = new GradeSurface(new Polyline2(new[] { new Vec2(100, -100), new Vec2(100, 100) }), new RoadGrade { Points = { new(0, 1), new(200, 1) } });
        var node = new NodeSurface(new[] { new NodeSurface.Leg(a, 0, 7), new NodeSurface.Leg(b, 0, 7) });
        // Na pista de A, longe de B: a cota é a de A (com o abaulamento).
        Assert.Equal(a.Z(new Vec2(60, 3)), node.Z(new Vec2(60, 3)), 1);
        // Na pista de B, longe de A: a cota é a de B.
        Assert.Equal(1, node.Z(new Vec2(100, 40)), 1);
        // No centro: entre as duas (as duas passam por 1 m ali).
        Assert.Equal(1, node.Z(new Vec2(100, 0)), 1);
        // Contínua (sem degraus) em toda a área do nó.
        for (var x = 70.0; x < 130; x += 1)
            for (var y = -30.0; y < 30; y += 1)
                Assert.True(Math.Abs(node.Z(new Vec2(x + 0.5, y)) - node.Z(new Vec2(x, y))) < 0.12);
    }

    [Fact]
    public void GradedPad_FollowsNodeSurface()
    {
        var pad = new GradePad(Polygon2.Rectangle(new Vec2(-10, -10), new Vec2(10, 10)), 0) { ZAt = p => 0.05 * p.X };
        var design = Grading.Design(Array.Empty<GradeCorridor>(), new[] { pad }, _ => 0);
        Assert.Equal(0.25, design.DesignZ(new Vec2(5, 0)) ?? double.NaN, 6);
        Assert.Equal(-0.25, design.DesignZ(new Vec2(-5, 3)) ?? double.NaN, 6);
    }

    [Fact]
    public void Profile_FixedPointsPinCrossings()
    {
        var o = RoadProfileDesigner.ForRelief(RelevoVia.GreideSuavizado, 0.06, 1, 1.5);
        o.Fixed.Add(new Vec2(150, 7.5));
        var r = RoadProfileDesigner.Design(300, s => 3 * Math.Sin(s / 50), o);
        Assert.Equal(7.5, r.Grade.Z(150), 1);
    }

    [Fact]
    public void ManualExclusion_CanBeDisabled()
    {
        var cat = CatalogService.LoadDefault();
        var d = new LinearMarkingDefinition { Code = "LFO-2" };
        var path = Straight(100);
        var ctx = new BuildContext { Catalog = cat };
        var full = MarkingBuilder.Build(d, path, ctx).Pieces.Sum(p => p.Shape.Area);
        d.Exclusions.Add(new ExclusionZone { Manual = true, Points = Polygon2.Rectangle(new Vec2(40, -1), new Vec2(60, 1)).Outer.ToList() });
        var cut = MarkingBuilder.Build(d, path, ctx).Pieces.Sum(p => p.Shape.Area);
        Assert.True(cut < full * 0.85);
        d.Exclusions[0].Enabled = false;
        Assert.Equal(full, MarkingBuilder.Build(d, path, ctx).Pieces.Sum(p => p.Shape.Area), 6);
    }

    [Fact]
    public void BridgeProfiles_InclinedConcaveCustom()
    {
        var axis = Straight(300);
        double G(Vec2 p) => -10 * Math.Exp(-Math.Pow((p.X - 150) / 55, 2));
        var b = new BridgeDefinition { ProfileKind = PerfilObra.Inclinado, Height = 4, EndHeight = 9, MaxGrade = 0.06 };
        var (g, s0, s1, _) = InfraRoads.BridgeGrade(b, axis, G);
        Assert.True(g.Z(148) > g.Z(70) + 2 && s0 <= 70);                    // tabuleiro sobe da altura do início à do fim
        b.ProfileKind = PerfilObra.Concavo; b.Height = -3;
        (g, _, _, _) = InfraRoads.BridgeGrade(b, axis, G);
        Assert.True(g.Z(150) < g.Z(20) && g.Z(150) < g.Z(280));
        b.ProfileKind = PerfilObra.Personalizado; b.ProfilePvis = "0; 0\n150; 6; 80\n300; 0";
        (g, _, _, _) = InfraRoads.BridgeGrade(b, axis, G);
        Assert.True(g.Z(150) > 4);
        var hosted = InfraRoads.BridgeOnRoad(RoadGrade.Flat(300), b, 100, 200, 300, s => 0);
        Assert.True(hosted.Z(175) > 2.5 && hosted.Z(199) > 3);                                        // PIVs relativos ao início da obra
        Assert.Equal(3, RoadGrade.ParsePvis("0;1\n10,5;2;30\nlixo\n20;3").Count);
    }

    [Fact]
    public void Relief_FollowTerrainTracksGround()
    {
        double T(double s) => 5 * Math.Sin(s / 40);
        var o = RoadProfileDesigner.ForRelief(RelevoVia.AcompanharTerreno, 0.08, 1, 1.5);
        var r = RoadProfileDesigner.Design(300, s => T(s), o);
        for (var s = 20.0; s < 280; s += 20) Assert.True(Math.Abs(r.Grade.Z(s) - T(s)) < 1.2);
        var o2 = RoadProfileDesigner.ForRelief(RelevoVia.GreideSuavizado, 0.03, 1, 1.5);
        var r2 = RoadProfileDesigner.Design(300, s => T(s), o2);
        for (var s = 0.0; s < 300; s += 5) Assert.True(Math.Abs(r2.Grade.GradeAt(s)) <= 0.031);
    }
}
