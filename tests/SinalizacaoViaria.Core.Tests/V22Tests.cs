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
    public void NodeSurface_IsTheMajorRoadFlatBeyondItsPavement()
    {
        // Via A (x) em rampa de 4 %, abaulamento de 2 % na pista de 7 m; B (y) secundária. O nó é a superfície de A, e além
        // da pista de A segue em nível na transversal (a cota da borda).
        var ga = new RoadGrade { Crossfall = 0.02, CrossfallWidth = 7, Points = { new(0, -3), new(200, 5) } };
        var a = new GradeSurface(Straight(200), ga);
        var b = new GradeSurface(new Polyline2(new[] { new Vec2(100, -100), new Vec2(100, 100) }), new RoadGrade { Points = { new(0, 1), new(200, 1) } });
        var node = new NodeSurface(new[] { new NodeSurface.Leg(a, 0, 7, 0, "a"), new NodeSurface.Leg(b, 0, 7, 1, "b") });
        Assert.Equal("a", node.Major!.Id);
        Assert.Equal(a.Z(new Vec2(100, 3)), node.Z(new Vec2(100, 3)), 6);
        Assert.Equal(ga.Z(100, 7), node.Z(new Vec2(100, 15)), 6);
        Assert.Equal(ga.Z(105, 7), node.Z(new Vec2(105, 12)), 6);
        // Contínua (sem degraus) em toda a área do nó.
        for (var x = 70.0; x < 130; x += 1)
            for (var y = -30.0; y < 30; y += 1)
                Assert.True(Math.Abs(node.Z(new Vec2(x + 0.5, y)) - node.Z(new Vec2(x, y))) < 0.05);
    }

    [Fact]
    public void MinorRoad_BlendsFromNodeSurfaceToItsOwnGrade()
    {
        var ga = new RoadGrade { Crossfall = 0.02, CrossfallWidth = 7, Points = { new(0, -3), new(200, 5) } };
        var a = new GradeSurface(Straight(200), ga);
        var bAxis = new Polyline2(new[] { new Vec2(100, 0), new Vec2(100, 100) });
        var bGrade = new RoadGrade { Crossfall = 0.02, CrossfallWidth = 4, Points = { new(0, 1), new(100, 6) } };
        var node = new NodeSurface(new[] { new NodeSurface.Leg(a, 0, 7, 0, "a"), new NodeSurface.Leg(new GradeSurface(bAxis, bGrade), 0, 4, 1, "b") });
        var b = new GradeSurface(bAxis, bGrade);
        b.Blends.Add(new NodeBlend(node, 0, 15, 20, 0));
        // Até o limite do nó (15 m): exatamente a superfície do nó; depois de 35 m: o greide próprio.
        foreach (var p in new[] { new Vec2(98, 10), new Vec2(102, 14.9) }) Assert.Equal(node.Z(p), b.Z(p), 6);
        foreach (var p in new[] { new Vec2(98, 36), new Vec2(103, 50) }) Assert.Equal(bGrade.Z(p.Y, 100 - p.X), b.Z(p), 6);
        // Suave: sem saltos.
        for (var y = 10.0; y < 40; y += 0.25)
            Assert.True(Math.Abs(b.Z(new Vec2(99, y + 0.25)) - b.Z(new Vec2(99, y))) < 0.05);
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

    [Fact]
    public void FloorPlanes_StraightGradeGivesOnePlanePerCrownSide()
    {
        // Rampa constante de 5 % com abaulamento de 2 %: cada lado da crista é um plano – 2 pisos, nenhum empenado.
        var surf = new GradeSurface(Straight(120), new RoadGrade { Crossfall = 0.02, Points = { new(0, 0), new(120, 6) } });
        var pav = Polygon2.Rectangle(new Vec2(0, -7), new Vec2(120, 7));
        var parts = FloorPlanes.Split(pav, surf.Z, 0.004, surf);
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.False(p.Warped));
        Assert.Equal(pav.Area, parts.Sum(p => p.Part.Area), 1);
        foreach (var (part, plane, _) in parts)
            foreach (var v in part.Outer) Assert.Equal(surf.Z(v), plane.Z(v), 3);
    }

    [Fact]
    public void FloorPlanes_VerticalCurveSplitsAcrossAxisWithinTolerance()
    {
        var surf = new GradeSurface(Straight(200), new RoadGrade { Crossfall = 0.02, DefaultCurve = 80, Points = { new(0, 0), new(100, 5), new(200, 0) } });
        var pav = Polygon2.Rectangle(new Vec2(0, 0.001), new Vec2(200, 7));
        var parts = FloorPlanes.Split(pav, surf.Z, 0.004, surf);
        Assert.True(parts.Count > 3);
        Assert.Equal(pav.Area, parts.Sum(p => p.Part.Area), 0);
        foreach (var (part, plane, warped) in parts.Where(p => !p.Warped))
            foreach (var v in part.Outer) Assert.True(Math.Abs(surf.Z(v) - plane.Z(v)) < 0.009);
    }

    [Fact]
    public void GradeSurface_LocatesFarPointsOnTheRightStation()
    {
        // Eixo curvo em vértices a cada 4 m: pontos a 16 m do eixo caíam na estação de um vértice vizinho.
        var axis = new Polyline2(Enumerable.Range(0, 81).Select(i => new Vec2(i * 4.0, 22 * Math.Sin(i * 4.0 / 95))).ToList());
        var surf = new GradeSurface(axis, RoadGrade.Flat(axis.Length));
        for (var s = 20.0; s < 300; s += 7.3)
            foreach (var off in new[] { -16.0, -9.0, 9.0, 16.0 })
            {
                var p = axis.PointAt(s) + axis.TangentAt(s).PerpLeft * off;
                var (ls, ly) = surf.Locate(p);
                Assert.Equal(s, ls, 0);
                Assert.Equal(off, ly, 0);
            }
    }

    [Fact]
    public void NodeSurface_MajorByRankThenWidth()
    {
        var a = new GradeSurface(new Polyline2(new[] { new Vec2(-80, 0), new Vec2(80, 0) }), new RoadGrade { Crossfall = 0.02, Points = { new(0, -4.8), new(160, 4.8) } });
        var b = new GradeSurface(new Polyline2(new[] { new Vec2(0, -80), new Vec2(0, 80) }), new RoadGrade { Crossfall = 0.02, Points = { new(0, 0), new(160, 0) } });
        Assert.Same(a, new NodeSurface(new[] { new NodeSurface.Leg(b, 0, 5, 3), new NodeSurface.Leg(a, 0, 7, 2) }).Major!.Surface);
        Assert.Same(a, new NodeSurface(new[] { new NodeSurface.Leg(b, 0, 5), new NodeSurface.Leg(a, 0, 7) }).Major!.Surface);
        Assert.Same(b, new NodeSurface(new[] { new NodeSurface.Leg(b, 0, 5, 1), new NodeSurface.Leg(a, 0, 7, 2) }).Major!.Surface);
    }

    [Fact]
    public void FloorPlanes_NodeGuidedByMajorGivesFewPlanes()
    {
        // Cruzamento em rampa: a superfície do nó é plana por partes (caimentos e faixas em nível além das bordas) – a divisão
        // guiada pelo eixo da principal dá poucas partes, todas planas.
        var ga = new RoadGrade { Crossfall = 0.02, CrossfallWidth = 7, Points = { new(0, -4.8), new(160, 4.8) } };
        var a = new GradeSurface(new Polyline2(new[] { new Vec2(-80, 0), new Vec2(80, 0) }), ga);
        var node = new NodeSurface(new[] { new NodeSurface.Leg(a, 0, 7) });
        var area = Polygon2.Rectangle(new Vec2(-16, -16), new Vec2(16, 16));
        var parts = FloorPlanes.Split(area, node.Z, 0.008, a);
        Assert.True(parts.Count <= 4, $"{parts.Count} partes");
        Assert.All(parts, p => Assert.False(p.Warped));
        Assert.Equal(area.Area, parts.Sum(p => p.Part.Area), 1);
    }

    [Fact]
    public void NodeSurface_BeyondRoadEndHasNoCone()
    {
        // Além da ponta do eixo o afastamento é medido na normal da ponta: nada de "cone" do abaulamento em volta da ponta.
        var a = new GradeSurface(Straight(50), new RoadGrade { Crossfall = 0.02, Points = { new(0, 0), new(50, 0) } });
        var leg = new NodeSurface.Leg(a, 0, 7);
        var z1 = NodeSurface.LegZ(leg, new Vec2(55, 0), out _);
        var z2 = NodeSurface.LegZ(leg, new Vec2(60, 0), out _);
        Assert.Equal(0, z1, 6);
        Assert.Equal(0, z2, 6);
        Assert.Equal(NodeSurface.LegZ(leg, new Vec2(45, 3), out _), NodeSurface.LegZ(leg, new Vec2(58, 3), out _), 6);
    }

    [Fact]
    public void TrimTools_ClickPicksTheDashAndLongLinesOnlyAroundTheClick()
    {
        var geo = new MarkingGeometry();
        geo.Add(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(3, 0.12)), MarkingColor.Branca);   // traço
        geo.Add(Polygon2.Rectangle(new Vec2(6, 0), new Vec2(9, 0.12)), MarkingColor.Branca);   // traço
        geo.Add(Polygon2.Rectangle(new Vec2(0, 3), new Vec2(60, 3.12)), MarkingColor.Branca);  // linha contínua
        var dash = TrimTools.PieceAt(geo, new Vec2(7, 0.2));
        Assert.NotNull(dash);
        Assert.Equal(6, dash!.Shape.Bounds.Min.X, 3);
        var z = TrimTools.ZoneFor(dash, new Vec2(7, 0.2))!;
        Assert.True(z.Contains(new Vec2(6.01, 0.06)) && z.Contains(new Vec2(8.99, 0.06)) && !z.Contains(new Vec2(2.9, 0.06)));
        var line = TrimTools.PieceAt(geo, new Vec2(30, 3.05))!;
        var zl = TrimTools.ZoneFor(line, new Vec2(30, 3.05), 3)!;
        var (mn, mx) = zl.Bounds;
        Assert.InRange(mx.X - mn.X, 2.8, 3.2);
        Assert.Null(TrimTools.PieceAt(geo, new Vec2(30, 1.5)));
        Assert.Equal(2, TrimTools.ZonesInWindow(geo, new Vec2(-1, -1), new Vec2(10, 1)).Count);
    }

    [Fact]
    public void CutSlope_EndsCloseToTerrainAndCrestChannelSitsOnGround()
    {
        double T(Vec2 p) => 0.30 * p.Y;
        var c = new BuildContext { Catalog = CatalogService.LoadDefault(), Ground = p => T(p), NativeTerrain = true };
        var sl = new SlopeDefinition { Height = 8, Type = TipoTalude.Corte, Ratio = 1.0, UphillLeft = true, BermEvery = 4, BermWidth = 2 };
        var geo = MarkingBuilder.Build(sl, new Polyline2(new[] { new Vec2(0, 0), new Vec2(60, 0) }), c);
        Assert.All(geo.Corridors, k => Assert.True(k.WallStart && k.WallEnd && k.EndSpill));
        // Canaleta de crista (última varredura "CANALETA" mais afastada): no terreno, não no ar.
        var channels = geo.Pieces.Where(p => p.Layer == "CANALETA" && p.Solid != null).ToList();
        var crest = channels.OrderByDescending(p => p.Shape.Centroid.Y).First();
        var top = crest.Solid!.MaxZ;
        var y = crest.Shape.Centroid.Y;
        Assert.InRange(top, T(new Vec2(30, y)) - 0.3, T(new Vec2(30, y)) + 1.8);
    }

    [Fact]
    public void Profile_CompensatedBalancesCutAndFill()
    {
        double T(double st) => 8 * Math.Sin(st / 60) + 0.02 * st;
        var o = new PerfilOpcoes { Mode = ModoGreide.Compensado, Smoothing = 200, AutoStructures = false, Homogenization = 1.25 };
        var r = RoadProfileDesigner.Design(600, s => T(s), o);
        var bal = r.CutM2 - r.FillM2 * 1.25;
        Assert.True(Math.Abs(bal) < 0.05 * (r.CutM2 + r.FillM2) + 5, $"corte {r.CutM2} aterro {r.FillM2}");
    }

    [Fact]
    public void Profile_CurveKSetsCurveLengths()
    {
        var o = new PerfilOpcoes { Mode = ModoGreide.AcompanharTerreno, Smoothing = 40, VerticalCurve = 10, CurveK = 30, MaxGrade = 0.08, AutoStructures = false };
        var r = RoadProfileDesigner.Design(500, s => 6 * Math.Sin(s / 50), o);
        var pts = r.Grade.Points.OrderBy(q => q.S).ToList();
        for (int i = 1; i + 1 < pts.Count; i++)
        {
            var g0 = (pts[i].Z - pts[i - 1].Z) / (pts[i].S - pts[i - 1].S);
            var g1 = (pts[i + 1].Z - pts[i].Z) / (pts[i + 1].S - pts[i].S);
            Assert.True((pts[i].Curve ?? 0) >= 30 * Math.Abs(g1 - g0) * 100 - 1e-6);
        }
    }

    [Fact]
    public void Grading_CutFillMapSeparatesCutAndFill()
    {
        // Plataforma plana em z = 0 sobre um terreno inclinado: corte de um lado, aterro do outro.
        var pad = new GradePad(Polygon2.Rectangle(new Vec2(-20, -20), new Vec2(20, 20)), 0) { Daylight = false };
        double? G(Vec2 p) => 0.05 * p.X;
        var design = Grading.Design(Array.Empty<GradeCorridor>(), new[] { pad }, G);
        var (cut, fill) = Grading.CutFillRegions(design, G, 2, 0.1);
        Assert.NotEmpty(cut);
        Assert.NotEmpty(fill);
        Assert.True(cut.All(c => c.Centroid.X > 0) && fill.All(f => f.Centroid.X < 0));
    }

    [Fact]
    public void FloorPlanes_MergeKeepsFewPartsOnATangent()
    {
        // Curva vertical só no fim: o trecho em tangente não pode ficar picado em ladrilhos.
        var surf = new GradeSurface(Straight(300), new RoadGrade { Crossfall = 0.02, DefaultCurve = 40, Points = { new(0, 0), new(260, 5.2), new(300, 4.4) } });
        var pav = Polygon2.Rectangle(new Vec2(0, 0.001), new Vec2(300, 7));
        var parts = FloorPlanes.Split(pav, surf.Z, 0.012, surf);
        var tangent = parts.Where(p => p.Part.Bounds.Max.X < 235).ToList();
        Assert.True(tangent.Count <= 2, $"{tangent.Count} partes na tangente");
        Assert.Equal(pav.Area, parts.Sum(p => p.Part.Area), 0);
    }

    [Fact]
    public void BridgeJoint_FollowsDeckCrossfall()
    {
        var b = new BridgeDefinition(); b.ApplyKindDefaults();
        var axis = Straight(200);
        var grade = new RoadGrade { Crossfall = 0.03, Points = { new(0, 5), new(200, 5) } };
        b.Output.Grade = grade;
        b.HostStart = 50; b.HostEnd = 150;
        var geo = MarkingBuilder.Build(b, axis, new BuildContext { Catalog = CatalogService.LoadDefault(), Ground = _ => 0 });
        var joints = geo.Pieces.Where(p => p.Layer == "JUNTA" && p.Solid != null).ToList();
        Assert.NotEmpty(joints);
        // A junta acompanha o abaulamento do tabuleiro: nas bordas ela desce em relação ao eixo – não é uma caixa reta na cota do centro.
        var j0 = joints.First();
        var verts = j0.Solid!.Faces.SelectMany(f => f).ToList();
        var yMax = verts.Max(v => Math.Abs(v.Y));
        var center = verts.Where(v => Math.Abs(v.Y) < 0.6).Max(v => v.Z);
        var edge = verts.Where(v => Math.Abs(v.Y) > yMax - 0.6).Max(v => v.Z);
        Assert.True(center - edge > 0.05, $"{center - edge}");
    }
}
