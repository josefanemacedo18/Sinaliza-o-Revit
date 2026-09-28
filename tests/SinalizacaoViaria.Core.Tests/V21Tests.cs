using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada J: vias com greide, obras hospedadas nas vias, perfil da via, terreno original e nós sobre vias do plugin.</summary>
public class V21Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static Polyline2 Straight(double len) => new(new[] { new Vec2(0, 0), new Vec2(len, 0) });

    // ------------------------------------------------------------------ greide

    [Fact]
    public void RoadGrade_TangentsAndParabola()
    {
        var g = new RoadGrade { DefaultCurve = 40, Points = { new(0, 0), new(100, 5), new(200, 5) } };
        Assert.Equal(0, g.Z(0), 6);
        Assert.Equal(2.5, g.Z(50), 6);                       // tangente
        Assert.Equal(5, g.Z(150), 6);
        Assert.True(g.Z(100) < 5 && g.Z(100) > 4.4);         // curva convexa arredonda o PIV
        // Contínuo: sem saltos ao longo do greide.
        for (var s = 0.0; s < 200; s += 0.5) Assert.True(Math.Abs(g.Z(s + 0.5) - g.Z(s)) < 0.05);
    }

    [Fact]
    public void RoadGrade_CrossfallAndSuperelevation()
    {
        var g = new RoadGrade { Crossfall = 0.02, CrossfallWidth = 5, Points = { new(0, 0), new(100, 0) } };
        Assert.Equal(-0.06, g.Z(50, 3), 6);
        Assert.Equal(-0.10, g.Z(50, -8), 6);                 // além da meia largura, segue o bordo
        g.Superelevation.AddRange(new[] { new Vec2(0, 0.06), new Vec2(100, 0.06) });
        Assert.True(g.Z(50, 3) > g.Z(50, -3));               // esquerda mais alta
    }

    [Fact]
    public void RoadGrade_RaiseBetween_KeepsRampsAndTop()
    {
        var g = RoadGrade.Flat(600);
        g.RaiseBetween(250, 350, 7.5, 0.05, 600);
        Assert.Equal(7.5, g.Z(300), 3);
        Assert.Equal(0, g.Z(10), 3);
        Assert.Equal(0, g.Z(590), 3);
        for (var s = 0.0; s < 600; s += 2) Assert.True(Math.Abs(g.GradeAt(s)) <= 0.051);
    }

    [Fact]
    public void GradeSurface_LocatesStationAndSide()
    {
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(100, 0), new Vec2(100, 100) });
        var surf = new GradeSurface(axis, new RoadGrade { Points = { new(0, 0), new(200, 10) } });
        var (s, y) = surf.Locate(new Vec2(50, 3));
        Assert.Equal(50, s, 3);
        Assert.Equal(3, y, 3);
        Assert.Equal(7.5, surf.Z(new Vec2(97, 50)), 1);      // estaca 150 no segundo trecho
    }

    [Fact]
    public void GradeLift_ShearsSolidsVertexByVertex()
    {
        var geo = new MarkingGeometry();
        var path = new Polyline2(CurveTools.Densify(new[] { new Vec2(0, 0), new Vec2(40, 0) }, 2));
        SolidSweep.Along(geo, path, _ => SolidSweep.Rect(-0.2, 0.2, 0, 0.8), MarkingColor.Concreto, 0, 40, 2, "BARREIRA");
        var surf = new GradeSurface(path, new RoadGrade { Points = { new(0, 0), new(40, 4) } });
        GradeLift.Apply(geo, surf);
        var solid = geo.Pieces.Single().Solid!;
        var end = solid.Faces.SelectMany(f => f).Where(v => v.X > 39.9).Min(v => v.Z);
        Assert.Equal(4, end, 2);
        Assert.All(solid.Faces, f => Assert.True(f.Count >= 3));
    }

    // ------------------------------------------------------------------ obras hospedadas

    private static (MarkingGeometry Geo, RoadGrade Grade, double S0, double S1) HostedBridge(BridgeDefinition b, RoadSetup setup, double len, Func<Vec2, double> ground)
    {
        var axis = Straight(len);
        var (grade, s0, s1, _) = InfraRoads.BridgeGrade(b, axis, ground);
        b.HostStart = s0; b.HostEnd = s1;
        return (InfraDemo.RoadWith(setup, axis, grade, new MarkingDefinition[] { b }, Cat, p => ground(p)), grade, s0, s1);
    }

    [Fact]
    public void HostedBridge_StructureOnly_RoadProvidesPavement()
    {
        var b = new BridgeDefinition();
        b.ApplyKindDefaults();
        var setup = RoadTemplates.All[1].Create();
        var (geo, grade, s0, s1) = HostedBridge(b, setup, 360, p => -9 * Math.Exp(-Math.Pow((p.X - 180) / 60, 2)));
        Assert.True(s1 - s0 > 40);
        // O tabuleiro fica sob a pista da via (topo da laje = greide menos o pavimento) e não há pavimento próprio.
        var deck = geo.Pieces.Where(p => p.Layer == "TABULEIRO").ToList();
        Assert.NotEmpty(deck);
        Assert.DoesNotContain(geo.Pieces, p => p.Layer == "PISTA" && p.Solid != null);
        var mid = (s0 + s1) / 2;
        var near = deck.SelectMany(p => p.Solid!.Faces.SelectMany(f => f)).Where(v => Math.Abs(v.X - mid) < 6 && Math.Abs(v.Y) < 3).ToList();
        Assert.True(near.Count > 0, $"{deck.Count} peças; x {deck.SelectMany(p => p.Solid!.Faces.SelectMany(f => f)).Min(v => v.X):0}..{deck.SelectMany(p => p.Solid!.Faces.SelectMany(f => f)).Max(v => v.X):0}, mid {mid:0}");
        var top = near.Max(v => v.Z);
        Assert.InRange(top, grade.Z(mid) - 0.2, grade.Z(mid) + 0.01);
        Assert.Contains(geo.Pieces, p => p.Layer == "MURETA");            // via com calçada: mureta + guarda-corpo
        Assert.Contains(geo.Pieces, p => p.Layer == "PILAR");
    }

    [Fact]
    public void HostedBridge_ProfileKinds()
    {
        double Valley(Vec2 p) => -8 * Math.Exp(-Math.Pow((p.X - 150) / 50, 2)) + 0.01 * p.X;
        var axis = Straight(300);
        foreach (var k in new[] { PerfilObra.Horizontal, PerfilObra.EntreMargens, PerfilObra.Convexo, PerfilObra.RampasDeAcesso })
        {
            var b = new BridgeDefinition { Kind = TipoObraDeArte.Ponte, ProfileKind = k, Height = 6 };
            var (g, s0, s1, _) = InfraRoads.BridgeGrade(b, axis, Valley);
            Assert.True(s1 > s0);
            switch (k)
            {
                case PerfilObra.Horizontal: Assert.Equal(6, g.Z(40), 3); Assert.Equal(6, g.Z(260), 3); break;
                case PerfilObra.EntreMargens: Assert.Equal(Valley(new Vec2(0, 0)), g.Z(0), 3); Assert.Equal(Valley(new Vec2(300, 0)), g.Z(300), 3); break;
                case PerfilObra.Convexo: Assert.True(g.Z(150) > g.Z(20) && g.Z(150) > g.Z(280)); break;
            }
        }
    }

    [Fact]
    public void HostedBridge_SkewRotatesPiers()
    {
        var b = new BridgeDefinition { Skew = 30, PierType = TipoPilar.Parede, SpanLength = 30 };
        var (geo, _, _, _) = HostedBridge(b, InfraRoads.Setup(2, 3.5, 1, 0), 360, p => -10 * Math.Exp(-Math.Pow((p.X - 180) / 70, 2)));
        var pier = geo.Pieces.Where(p => p.Layer == "PILAR").Select(p => p.Shape).First();
        var (mn, mx) = pier.Bounds;
        // Pilar-parede esconso: projeção em planta mais "longa" em X do que um pilar reto (espessura ≈ dimensão).
        Assert.True(mx.X - mn.X > 3);
    }

    [Fact]
    public void HostedTunnelAndTrench_NoOwnPavement()
    {
        var tn = new TunnelDefinition { HostStart = 100, HostEnd = 260 };
        var tg = new RoadGrade { Points = { new(0, 0), new(360, 0) } };
        var geo = InfraDemo.RoadWith(InfraRoads.Setup(tn), Straight(360), tg, new MarkingDefinition[] { tn }, Cat);
        Assert.Contains(geo.Pieces, p => p.Layer == "REVESTIMENTO");
        var tr = new TrenchDefinition { HostStart = 0, HostEnd = 300, KeepRoadGrade = true };
        var grade = InfraRoads.TrenchOnRoad(RoadGrade.Flat(300), 0, 300, 6.5, 0.06, 40, _ => 0);
        Assert.True(grade.Z(150) < -6);
        var g2 = InfraDemo.RoadWith(InfraRoads.Setup(tr), Straight(300), grade, new MarkingDefinition[] { tr }, Cat, _ => 0);
        Assert.Contains(g2.Pieces, p => p.Layer == "MURO");
        Assert.NotEmpty(g2.Corridors);
    }

    [Fact]
    public void InfraRoads_SetupsMatchStructureWidth()
    {
        var s = InfraRoads.Setup(3, 3.5, 1.0, 1.5);
        var pav = s.PavementDefinition();
        Assert.Equal(3 * 3.5 + 2 * 1.0 + 2 * 1.5, pav.TotalLeft + pav.TotalRight, 3);
        var host = HostRoads.Of(pav);
        Assert.True(host.EdgeRise > 0.1);
        var p = InfraRoads.PedestrianSetup(3);
        Assert.Equal(TipoPavimento.Concreto, p.Pavement);
    }

    // ------------------------------------------------------------------ perfil da via

    [Fact]
    public void RoadProfile_ValleyAndHillBecomeStructures()
    {
        double T(double s) => -16 * Math.Exp(-Math.Pow((s - 260) / 70, 2)) + 30 * Math.Exp(-Math.Pow((s - 620) / 110, 2));
        var r = RoadProfileDesigner.Design(820, s => T(s), new PerfilOpcoes { BridgeFill = 7, TunnelCut = 16, TrenchCut = 7 });
        Assert.Contains(r.Segments, x => x.Kind == TipoTrecho.Viaduto && x.S0 < 260 && x.S1 > 260);
        Assert.Contains(r.Segments, x => x.Kind == TipoTrecho.Tunel && x.S0 < 620 && x.S1 > 620);
        for (var s = 0.0; s < 820; s += 5) Assert.True(Math.Abs(r.Grade.GradeAt(s)) <= 0.061);
        Assert.Equal(T(0), r.Grade.Z(0), 2);
        var chart = RoadProfileDesigner.Chart(r);
        Assert.Contains(chart.Pieces, p => p.Color == MarkingColor.Terra);
    }

    [Fact]
    public void RoadProfile_ManualAndLevelModes()
    {
        var m = RoadProfileDesigner.Design(300, _ => 0, new PerfilOpcoes { Mode = ModoGreide.Manual, ManualPvis = "0; 0\n150; 6; 80\n300; 0", AutoStructures = false });
        Assert.True(m.Grade.Z(150) > 4);
        var l = RoadProfileDesigner.Design(300, s => s / 50, new PerfilOpcoes { Mode = ModoGreide.Nivelado, LevelZ = 3 });
        Assert.Equal(3, l.Grade.Z(10), 6);
        Assert.Equal(3, l.Grade.Z(290), 6);
    }

    // ------------------------------------------------------------------ terreno original

    [Fact]
    public void TerrainMesh_SamplesAndRoundTrips()
    {
        var m = TerrainMesh.FromFunction(new Vec2(1000, 2000), new Vec2(1100, 2100), 5, p => 0.1 * (p.X - 1000) + 0.05 * (p.Y - 2000) + 700);
        Assert.Equal(700 + 0.1 * 42 + 0.05 * 13, m.Z(new Vec2(1042, 2013))!.Value, 4);
        Assert.Null(m.Z(new Vec2(0, 0)));
        var back = TerrainMesh.Deserialize(m.Serialize())!;
        Assert.Equal(m.Count, back.Count);
        Assert.Equal(m.Z(new Vec2(1077.3, 2055.1))!.Value, back.Z(new Vec2(1077.3, 2055.1))!.Value, 3);
    }

    [Fact]
    public void TerrainRebuild_ReplacesOnlyTheDesignArea()
    {
        var orig = TerrainMesh.FromFunction(new Vec2(-100, -100), new Vec2(100, 100), 10, p => 0);
        var c = new GradeCorridor();
        for (var x = -40.0; x <= 40; x += 5) { c.Left.Add(new Vec3(x, 5, 2)); c.Right.Add(new Vec3(x, -5, 2)); }
        var design = Grading.Design(new[] { c }, Array.Empty<GradePad>(), p => orig.Z(p), 0, 30);
        var region = TerrainRebuild.Mask(design.Footprint, 0.5);
        var pts = TerrainRebuild.Points(orig, design, region);
        Assert.Contains(pts, p => Math.Abs(p.Z - 2) < 1e-6);                       // plataforma
        Assert.DoesNotContain(pts, p => Math.Abs(p.X) < 30 && Math.Abs(p.Y) < 4 && Math.Abs(p.Z) < 1e-6);   // nada do original sob a via
        Assert.True(region(new Vec2(0, 0)));
        Assert.False(region(new Vec2(90, 90)));
    }

    // ------------------------------------------------------------------ nós sobre vias

    [Theory]
    [InlineData(TipoNoViario.Diamante, 4)]
    [InlineData(TipoNoViario.DiamanteRotatorias, 4)]
    [InlineData(TipoNoViario.TrevoCompleto, 8)]
    [InlineData(TipoNoViario.TrevoParcial, 6)]
    [InlineData(TipoNoViario.Trombeta, 4)]
    [InlineData(TipoNoViario.RotatoriaElevada, 4)]
    public void Interchange_PlansRampsAsRoads(TipoNoViario t, int ramps)
    {
        var d = new InterchangeDefinition { Type = t, MainLength = 1600, CrossLength = 1400 };
        var (ms, cs) = InterchangeDemo.Setups(d);
        var (ma, ca) = InterchangeDemo.Axes(d);
        var main = InterchangeDemo.Road("m", ms, ma);
        var cross = InterchangeDemo.Road("c", cs, ca);
        var plan = InterchangePlanner.Plan(d, main, cross, Vec2.Zero)!;
        Assert.Equal(ramps, plan.Ramps.Count);
        // A via de cima passa com gabarito sobre a de baixo.
        var over = plan.CrossOver ? cross : main;
        var so = over.Axis.Project(plan.Node).Station;
        Assert.True(plan.OverGrade.Z(so) >= d.Clearance);
        Assert.True(plan.BridgeS1 - plan.BridgeS0 > 2 * main.TotalHalf);
        foreach (var r in plan.Ramps)
        {
            // Ramos com greide contínuo; faixas paralelas coladas ao bordo da via (eixo do ramo = bordo esquerdo da faixa).
            var L = r.Axis.Length;
            for (var s = 0.0; s < L - 1; s += 2) Assert.True(Math.Abs(r.Grade.Z(s + 1) - r.Grade.Z(s)) < 0.2, $"{r.Name} salto em {s}");
            if (r.StartLink == LigacaoRamo.Paralela && r.StartRoad == "m")
            {
                var side = main.Axis.Project(r.Axis.Points[0]).Signed;
                Assert.Equal(side > 0 ? main.HalfLeft : main.HalfRight, Math.Abs(side), 1);
            }
        }
    }

    [Fact]
    public void Interchange_DemoBuildsEverything()
    {
        var d = new InterchangeDefinition { Type = TipoNoViario.TrevoCompleto, MainLength = 1400, CrossLength = 1200 };
        var geo = InterchangeDemo.Build(d, Cat);
        Assert.Contains(geo.Pieces, p => p.Layer == "ZEBRADO");
        Assert.Contains(geo.Pieces, p => p.Layer == "TABULEIRO");
        Assert.Contains(geo.Pieces, p => p.Layer == "PORTICO");
        Assert.Contains(geo.TerrainFinishes, f => f.Label.Contains("laço"));
        var ring = InterchangeDemo.Build(new InterchangeDefinition { Type = TipoNoViario.RotatoriaElevada, MainLength = 1400, CrossLength = 1200 }, Cat);
        Assert.NotEmpty(ring.Pieces);
    }

    [Fact]
    public void RampEnds_MergeFlagsSkipIntersections()
    {
        var mainSetup = RoadTemplates.All[8].Create();
        var mp = mainSetup.PavementDefinition(); mp.Id = "m";
        var ramp = new RoadSetup { TwoWay = false, Center = CenterTreatment.Nenhum };
        ramp.Right.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 4 });
        var rp = ramp.PavementDefinition(); rp.Id = "r";
        var mainAxis = Straight(400);
        var rampAxis = new Polyline2(new[] { new Vec2(100, -mp.RightWidth), new Vec2(250, -mp.RightWidth - 40) });
        var roads = new List<IntersectionRoad> { new(mp, mainAxis), new(rp, rampAxis) };
        Assert.NotEmpty(IntersectionGenerator.FindNodes(roads));
        rp.MergeStart = true;
        Assert.Empty(IntersectionGenerator.FindNodes(roads));
    }
}
