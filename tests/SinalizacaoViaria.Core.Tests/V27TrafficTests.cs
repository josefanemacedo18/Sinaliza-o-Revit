using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada T: simulador ampliado – cenários, contagens, planos gravados, coordenação, ônibus, faixas auxiliares, segurança e custos.</summary>
public class V27TrafficTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private sealed class Scene
    {
        public readonly List<MarkingDefinition> Defs = new();
        private readonly Dictionary<string, Polyline2> _paths = new();

        public RoadPavementDefinition Road(RoadSetup setup, params Vec2[] pts)
        {
            var axis = new Polyline2(pts);
            var g = setup.Build(PathReference.FromPoints(pts, 0), new OutputSettings(), Cat, axis: axis);
            foreach (var m in g) _paths[m.Id] = axis;
            Defs.AddRange(g);
            return g.OfType<RoadPavementDefinition>().First();
        }

        public RoadPavementDefinition Road(int template, params Vec2[] pts) => Road(RoadTemplates.All[template].Create(), pts);

        public IntersectionDefinition Intersection(Vec2 at, ControleIntersecao c, params RoadPavementDefinition[] roads)
        {
            var it = new IntersectionDefinition { Node = at, Control = c, RoadIds = roads.Select(r => r.Id).ToList(), MainRoadId = roads[0].Id, Crosswalks = true };
            Defs.Add(it);
            return it;
        }

        public TrafficNetwork Network() =>
            TrafficNetworkBuilder.Build(Defs, d => _paths.TryGetValue(d.Id, out var p) ? (p, 0.0) : d.Path is { Points.Count: >= 2 } pr ? (new Polyline2(pr.Points), 0.0) : null);
    }

    private static (Scene S, RoadPavementDefinition A, RoadPavementDefinition B, IntersectionDefinition I) Cross(ControleIntersecao c = ControleIntersecao.Semaforo)
    {
        var s = new Scene();
        var a = s.Road(2, new Vec2(-300, 0), new Vec2(300, 0));
        var b = s.Road(1, new Vec2(0, -300), new Vec2(0, 300));
        var it = s.Intersection(Vec2.Zero, c, a, b);
        return (s, a, b, it);
    }

    private static NodeResult Node(TrafficResult r) => r.Nodes.Values.First(n => n.Node.DesignKind == TipoNo.Intersecao);

    [Fact]
    public void Scenario_RoundTripsAndOverridesTheControl()
    {
        var (s, _, _, _) = Cross(ControleIntersecao.Pare);
        var net = s.Network();
        var nd = net.Nodes.First(n => n.Kind == TipoNo.Intersecao);
        var sc = new TrafficScenario { Name = "Rotatória?", Options = new TrafficOptions { Demand = NivelDemanda.Pico } };
        sc.Options.Nodes.Add(new NodeOverride { Node = nd.Key, Control = ControleNo.Semaforo });
        sc.Options.ZoneVolumeKeys[net.Nodes.First(n => n.IsZone).Key] = 900;
        var back = TrafficScenario.FromJson(TrafficScenario.ToJson(new[] { sc })).Single();
        Assert.Equal("Rotatória?", back.Name);
        Assert.Equal(ControleNo.Semaforo, back.Options.Nodes[0].Control);
        var r = TrafficAnalysis.Run(net, back.Options);
        Assert.Equal(ControleNo.Semaforo, Node(r).Control);
        Assert.True(Node(r).Cycle > 0);
        Assert.Contains(r.OD, x => net.Nodes[x.Key.O].Key == back.Options.ZoneVolumeKeys.Keys.First());
        // Outro cenário na mesma rede volta ao controle do projeto.
        var r2 = TrafficAnalysis.Run(net, new TrafficOptions());
        Assert.Equal(ControleNo.Pare, Node(r2).Control);
    }

    [Fact]
    public void TurnCounts_RedistributeTheMovements()
    {
        var (s, a, _, _) = Cross();
        var net = s.Network();
        var nd = net.Nodes.First(n => n.Kind == TipoNo.Intersecao);
        var appr = net.Links[nd.In.First(i => net.Links[i].Road.Id == a.Id && net.Links[i].Forward)];
        var opt = new TrafficOptions();
        opt.Nodes.Add(new NodeOverride { Node = nd.Key, Turns = { new TurnCount { Approach = appr.Key, Total = 800, Left = 25, Through = 60, Right = 15 } } });
        var r = TrafficAnalysis.Run(net, opt);
        var ap = Node(r).Approaches.First(x => x.Link == appr.Index);
        Assert.Equal(800, ap.Volume, 0);
        Assert.Equal(200, ap.Movements[Giro.Esquerda], 0);
        Assert.Equal(120, ap.Movements[Giro.Direita], 0);
    }

    [Fact]
    public void StoredPlan_IsUsedAndCanBeProducedFromTheResult()
    {
        var (s, _, _, it) = Cross();
        var r = TrafficAnalysis.Run(s.Network(), new TrafficOptions());
        var plan = TrafficAnalysis.PlanOf(r, Node(r), "teste");
        Assert.Equal(Node(r).Phases.Count, plan.Phases.Count);
        Assert.All(plan.Phases, p => Assert.NotEmpty(p.Approaches));
        Assert.Equal(plan.Cycle, plan.Phases.Sum(p => p.Green + p.Yellow + p.AllRed), 3);
        // Gravado com ciclo longo: a análise respeita o plano.
        plan.Cycle = 140;
        plan.Phases[0].Green += 140 - plan.Phases.Sum(p => p.Green + p.Yellow + p.AllRed);
        it.SignalPlan = plan;
        var back = (IntersectionDefinition)MarkingDefinition.FromJson(it.ToJson())!;
        Assert.Equal(140, back.SignalPlan!.Cycle);
        var r2 = TrafficAnalysis.Run(s.Network(), new TrafficOptions());
        Assert.Equal(140, Node(r2).Cycle, 0);
        Assert.Contains("gravado", Node(r2).PlanSource);
        var r3 = TrafficAnalysis.Run(s.Network(), new TrafficOptions { UseStoredPlans = false });
        Assert.NotEqual(140, Node(r3).Cycle, 0);
    }

    [Fact]
    public void Coordination_UsesACommonCycleAndOffsets()
    {
        var s = new Scene();
        var av = s.Road(2, new Vec2(-200, 0), new Vec2(800, 0));
        var c1 = s.Road(1, new Vec2(0, -200), new Vec2(0, 200));
        var c2 = s.Road(0, new Vec2(400, -200), new Vec2(400, 200));
        s.Intersection(Vec2.Zero, ControleIntersecao.Semaforo, av, c1);
        s.Intersection(new Vec2(400, 0), ControleIntersecao.Semaforo, av, c2);
        var net = s.Network();
        var r = TrafficAnalysis.Run(net, new TrafficOptions { Coordinate = true });
        var sig = r.Nodes.Values.Where(n => n.Control == ControleNo.Semaforo).ToList();
        Assert.Equal(2, sig.Count);
        Assert.True(r.CommonCycle > 0);
        Assert.All(sig, n => Assert.Equal(r.CommonCycle, n.Cycle, 0));
        Assert.Contains(sig, n => n.Offset > 1);
        Assert.NotEmpty(r.Progression);
        var sim = TrafficSimulation.Run(r);
        Assert.True(sim.Completed > 0);
    }

    [Fact]
    public void BusStops_InLaneReducesCapacityAndBayDoesNot()
    {
        TrafficNetwork Net(bool bay)
        {
            var s = new Scene();
            var setup = RoadTemplates.All[1].Create();
            if (bay) setup.Recuos.Add(new RecuoVia { Tipo = TipoRecuo.BaiaOnibus, Estaca = 120, Comprimento = 15, Profundidade = 2.0 });
            var a = s.Road(setup, new Vec2(0, 0), new Vec2(300, 0));
            if (!bay) s.Defs.Add(new SignDefinition { Code = "SAU-ONIBUS", Position = new Vec2(130, -8) });
            return s.Network();
        }
        var inLane = Net(false);
        var withBay = Net(true);
        var l1 = inLane.Links.First(l => l.Forward);
        var l2 = withBay.Links.First(l => l.Forward);
        Assert.Contains(l1.BusStops, b => !b.Bay);
        Assert.Contains(l2.BusStops, b => b.Bay);
        Assert.True(l1.Capacity < l2.Capacity);
        // Microssimulação com ônibus: param e seguem.
        var r = TrafficAnalysis.Run(withBay, new TrafficOptions { Buses = 0.2, SimSeconds = 600, WarmupSeconds = 60 });
        var sim = TrafficSimulation.Run(r);
        Assert.True(sim.BusStopsServed > 0);
    }

    [Fact]
    public void DecelLane_IsReadAndHelpsRightTurns()
    {
        var s = new Scene();
        var setup = RoadTemplates.All[2].Create();
        setup.Recuos.Add(new RecuoVia { Tipo = TipoRecuo.FaixaDesaceleracao, Estaca = 200, Comprimento = 60, Profundidade = 3.3, TaperEntrada = 30, TaperSaida = 0 });
        var a = s.Road(setup, new Vec2(-300, 0), new Vec2(300, 0));
        var b = s.Road(1, new Vec2(0, -300), new Vec2(0, 300));
        s.Intersection(Vec2.Zero, ControleIntersecao.Semaforo, a, b);
        var net = s.Network();
        Assert.Contains(net.Links, l => l.DecelLane > 20);
    }

    [Fact]
    public void Safety_ConflictPointsAndRoundaboutPredictsFewerCrashes()
    {
        var (s, _, _, _) = Cross(ControleIntersecao.Pare);
        var net = s.Network();
        var stop = TrafficAnalysis.Run(net, new TrafficOptions());
        var sf = stop.Safety[Node(stop).Node.Index];
        Assert.Equal(4, sf.Legs);
        Assert.Equal(32, sf.Total);
        Assert.True(sf.CrashesPerYear > 0);
        var opt = new TrafficOptions();
        opt.Nodes.Add(new NodeOverride { Node = Node(stop).Node.Key, Control = ControleNo.Rotatoria });
        var rb = TrafficAnalysis.Run(net, opt);
        var sr = rb.Safety[Node(rb).Node.Index];
        Assert.True(sr.Total < sf.Total);
        Assert.True(sr.CrashesPerYear < sf.CrashesPerYear);
        Assert.True(stop.Economics.Total > 0);
        var cmp = TrafficComparison.Text(new[] { ("PARE", stop), ("Rotatória", rb) });
        Assert.Contains("Acidentes previstos", cmp);
        Assert.Contains("★", cmp);
    }

    [Fact]
    public void Diagnostics_LongDowngradeAsksForEscapeRamp()
    {
        var s = new Scene();
        var setup = RoadTemplates.All[3].Create();
        var a = s.Road(setup, new Vec2(0, 0), new Vec2(2500, 0));
        a.Output.Grade = new RoadGrade { Points = { new GradePoint(0, 150), new GradePoint(2500, 0) } };   // −6 %
        var net = s.Network();
        var r = TrafficAnalysis.Run(net, new TrafficOptions { HeavyVehicles = 0.15 });
        Assert.Contains(r.Diagnostics, d => d.Title.Contains("sem área de escape"));
        s.Defs.Add(new EscapeRampDefinition { PathRef = PathReference.FromPoints(new[] { new Vec2(2000, -12), new Vec2(2200, -40) }, 0) });
        var r2 = TrafficAnalysis.Run(s.Network(), new TrafficOptions { HeavyVehicles = 0.15 });
        Assert.DoesNotContain(r2.Diagnostics, d => d.Title.Contains("sem área de escape"));
    }
}
