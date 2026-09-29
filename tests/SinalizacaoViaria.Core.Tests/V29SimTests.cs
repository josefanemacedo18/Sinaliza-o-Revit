using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada V: microssimulação coesa – com muito tráfego há fila e lentidão, nunca veículos sobrepostos nem travamento.</summary>
public class V29SimTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private sealed class Scene
    {
        public readonly List<MarkingDefinition> Defs = new();
        public readonly Dictionary<string, Polyline2> Paths = new();

        public RoadPavementDefinition Road(int template, params Vec2[] pts)
        {
            var axis = new Polyline2(pts);
            var g = RoadTemplates.All[template].Create().Build(PathReference.FromPoints(pts, 0), new OutputSettings(), Cat, axis: axis);
            foreach (var m in g) Paths[m.Id] = axis;
            Defs.AddRange(g);
            return g.OfType<RoadPavementDefinition>().First();
        }

        public TrafficNetwork Network(bool geometry = false)
        {
            (Polyline2, double)? Ax(MarkingDefinition d) => Paths.TryGetValue(d.Id, out var p) ? (p, 0.0) : d.Path is { Points.Count: >= 2 } pr ? (new Polyline2(pr.Points, pr.Closed), 0.0) : null;
            var ctx = new BuildContext { Catalog = Cat };
            return TrafficNetworkBuilder.Build(Defs, Ax, geometry ? d => MarkingBuilder.Build(d, Ax(d)?.Item1, ctx) : null);
        }
    }

    /// <summary>Cruzamento de duas coletoras de duas faixas por sentido: 0 PARE, 1 semáforo, 2 sem controle, 3 rotatória de uma faixa.</summary>
    private static TrafficNetwork Crossing(int kind, bool geometry = false, bool leftTurns = true)
    {
        var s = new Scene();
        var a = s.Road(1, new Vec2(-400, 0), new Vec2(400, 0));
        var b = s.Road(1, new Vec2(0, -350), new Vec2(0, 350));
        if (kind == 3)
        {
            var rb = new RoundaboutDefinition { Center = Vec2.Zero };
            rb.ApplyPreset(TipoRotatoria.UmaFaixa);
            rb.Legs = RoundaboutGenerator.LegsFromRoads(Vec2.Zero, new[] { new IntersectionRoad(a, s.Paths[a.Id]), new IntersectionRoad(b, s.Paths[b.Id]) }, rb.OuterRadius + 25);
            s.Defs.Add(rb);
        }
        else s.Defs.Add(new IntersectionDefinition
        {
            Node = Vec2.Zero, RoadIds = { a.Id, b.Id }, MainRoadId = a.Id,
            Control = kind switch { 0 => ControleIntersecao.Pare, 1 => ControleIntersecao.Semaforo, _ => ControleIntersecao.Nenhum },
            LeftTurns = leftTurns,
        });
        return s.Network(geometry);
    }

    /// <summary>Quadros com dois corpos (segmento frente–traseira, centro do quadro) a menos de <paramref name="tol"/> m.</summary>
    private static int Overlaps(SimResult r, double tol = 1.0)
    {
        var total = 0;
        foreach (var f in r.Frames)
        {
            var seg = new (Vec2 A, Vec2 B)[f.Count];
            for (int i = 0; i < f.Count; i++)
            {
                var o = i * SimFrame.Stride;
                var p = new Vec2(f.Data[o], f.Data[o + 1]);
                var d = new Vec2(Math.Cos(f.Data[o + 2]), Math.Sin(f.Data[o + 2]));
                var half = ((int)f.Data[o + 3] == 0 ? 4.5 : 12) / 2 - 0.3;
                seg[i] = (p + d * half, p - d * half);
            }
            for (int i = 0; i < seg.Length; i++)
                for (int j = i + 1; j < seg.Length; j++)
                    if (seg[i].A.DistanceTo(seg[j].A) < 15 && SegDist(seg[i].A, seg[i].B, seg[j].A, seg[j].B) < tol) total++;
        }
        return total;
    }

    private static double SegDist(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        static double O(Vec2 p, Vec2 q, Vec2 r) => (q - p).Cross(r - p);
        if (O(a, b, c) * O(a, b, d) < 0 && O(c, d, a) * O(c, d, b) < 0) return 0;
        static double Pt(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            var t = ab.Length < 1e-9 ? 0 : Math.Clamp((p - a).Dot(ab) / ab.Dot(ab), 0, 1);
            return p.DistanceTo(a + ab * t);
        }
        return new[] { Pt(a, c, d), Pt(b, c, d), Pt(c, a, b), Pt(d, a, b) }.Min();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PeakDemand_QueuesButNeverOverlapsOrLocksUp(int kind)
    {
        var macro = TrafficAnalysis.Run(Crossing(kind), new TrafficOptions { Demand = NivelDemanda.Pico, SimSeconds = 600, WarmupSeconds = 120 });
        var sim = TrafficSimulation.Run(macro);
        // Nenhum choque: no máximo um quadro isolado de contato de raspão em todo o período.
        Assert.True(Overlaps(sim) <= 1, $"sobreposições: {Overlaps(sim)}");
        // Sem travamento: mesmo acima da capacidade o nó segue escoando (≥ ~1 400 veíc/h).
        Assert.True(sim.Completed > 230, $"{sim.Completed} concluídos de {sim.Spawned}");
    }

    [Fact]
    public void SingleLaneRoundabout_WithTwoLaneApproaches_DoesNotFillTheRing()
    {
        var macro = TrafficAnalysis.Run(Crossing(3), new TrafficOptions { Demand = NivelDemanda.Media, SimSeconds = 600, WarmupSeconds = 120 });
        var sim = TrafficSimulation.Run(macro);
        // Antes o anel enchia por inteiro (todos parados à distância mínima) e a rotatória travava (≈70 concluídos).
        Assert.True(sim.Completed > 250, $"{sim.Completed} de {sim.Spawned}");
        Assert.Equal(0, Overlaps(sim));
    }

    [Fact]
    public void Map_DrawsTheRealPlanOfTheProject()
    {
        var net = Crossing(1, geometry: true);
        Assert.NotEmpty(net.Backdrop);
        Assert.Contains(net.Backdrop, b => b.Layer == CamadaMapa.Pavimento);
        Assert.Contains(net.Backdrop, b => b.Layer == CamadaMapa.Calcada);
        Assert.Contains(net.Backdrop, b => b.Layer == CamadaMapa.Pintura);
        // Ordem de pintura: pavimento antes da pintura.
        var firstPaint = net.Backdrop.FindIndex(b => b.Layer == CamadaMapa.Pintura);
        var lastPave = net.Backdrop.FindLastIndex(b => b.Layer == CamadaMapa.Pavimento);
        Assert.True(lastPave < firstPaint);
        // Sem geometria (chamada antiga) o mapa usa o esquema.
        Assert.Empty(Crossing(1).Backdrop);
    }

    [Fact]
    public void Frames_CarryTheTurnSignal()
    {
        var sim = TrafficSimulation.Run(TrafficAnalysis.Run(Crossing(1), new TrafficOptions { Demand = NivelDemanda.Media, SimSeconds = 300, WarmupSeconds = 60 }));
        Assert.Equal(7, SimFrame.Stride);
        var left = 0;
        var right = 0;
        foreach (var f in sim.Frames)
            for (int k = 0; k < f.Count; k++)
            {
                var b = f.Data[k * SimFrame.Stride + 6];
                if (b < 0) left++;
                else if (b > 0) right++;
            }
        Assert.True(left > 0 && right > 0, $"esq {left} dir {right}");
    }

    [Fact]
    public void Intersection_WithoutLeftTurns_ForbidsThemInTheNetwork()
    {
        var net = Crossing(0, leftTurns: false);
        var nd = net.Nodes.Single(n => n.Kind == TipoNo.Intersecao);
        Assert.True(nd.NoLeftTurns);
        foreach (var li in nd.In)
            foreach (var lo in nd.Out)
                if (TrafficAnalysis.TurnOf(net, li, lo) == Giro.Esquerda)
                    Assert.False(TrafficAnalysis.Allowed(net, nd, li, lo));
        var sim = TrafficSimulation.Run(TrafficAnalysis.Run(net, new TrafficOptions { Demand = NivelDemanda.Baixa, SimSeconds = 300, WarmupSeconds = 60 }));
        Assert.True(sim.Completed > 0);
    }

    /// <summary>Avenida (coletora) com três cruzamentos semaforizados a 250 m.</summary>
    private static TrafficNetwork Corridor(out string avenue)
    {
        var s = new Scene();
        var a = s.Road(1, new Vec2(-500, 0), new Vec2(500, 0));
        avenue = a.Id;
        foreach (var x in new[] { -250.0, 0, 250 })
        {
            var b = s.Road(0, new Vec2(x, -250), new Vec2(x, 250));
            s.Defs.Add(new IntersectionDefinition { Node = new Vec2(x, 0), RoadIds = { a.Id, b.Id }, MainRoadId = a.Id, Control = ControleIntersecao.Semaforo });
        }
        return s.Network();
    }

    [Fact]
    public void Advisor_RecommendsBetterTimingForAPoorlyTimedSignal()
    {
        var net = Crossing(1);
        var opt = new TrafficOptions { Demand = NivelDemanda.Media };
        var key = net.Nodes.Single(n => n.Kind == TipoNo.Intersecao).Key;
        opt.Nodes.Add(new NodeOverride { Node = key, Cycle = 160 });     // ciclo longo demais
        var adv = TrafficSignalAdvisor.Recommend(net, opt, new[] { key });
        var a = Assert.Single(adv);
        Assert.True(a.Cycle < 160, $"ciclo {a.Cycle}");
        Assert.True(a.DelayAfter < a.DelayBefore, $"{a.DelayBefore} → {a.DelayAfter}");
        TrafficSignalAdvisor.Apply(opt, adv);
        var res = TrafficAnalysis.Run(net, opt);
        var nr = res.Nodes.Values.Single(n => n.Node.Key == key);
        Assert.Equal(a.Cycle, nr.Cycle, 0);
        Assert.Contains("verdes fixos", nr.PlanSource);
        Assert.False(string.IsNullOrWhiteSpace(TrafficSignalAdvisor.Text(adv)));
    }

    [Fact]
    public void Advisor_CoordinatesAllSignalsOfARoad()
    {
        var net = Corridor(out var avenue);
        var opt = new TrafficOptions { Demand = NivelDemanda.Pico };
        var res = TrafficAnalysis.Run(net, opt);
        var chain = TrafficSignalAdvisor.SignalsOnRoad(res, avenue);
        Assert.Equal(3, chain.Count);
        var adv = TrafficSignalAdvisor.Recommend(net, opt, chain.Select(n => n.Key).ToList(), avenue);
        Assert.Equal(3, adv.Count);
        Assert.Single(adv.Select(a => a.Cycle).Distinct());                   // ciclo comum
        Assert.Equal(2, adv.Count(a => a.Offset > 0));                         // defasagens da onda verde
        Assert.Contains(adv, a => a.Offset == 0);
        TrafficSignalAdvisor.Apply(opt, adv);
        var after = TrafficAnalysis.Run(net, opt);
        foreach (var a in adv) Assert.Equal(a.Offset, after.Nodes.Values.Single(n => n.Node.Key == a.NodeKey).Offset, 0);
    }

    [Fact]
    public void CrossingSignal_CreatedInTheScenario_StopsTrafficAndCostsCapacity()
    {
        var net = Corridor(out _);
        var opt = new TrafficOptions { Demand = NivelDemanda.Media };
        var baseRes = TrafficAnalysis.Run(net, opt);
        var link = net.Links.First(l => l.Road.Id == net.Roads[0].Id && l.Forward && l.Length > 200);
        var p = link.Path.PointAt(link.Length / 2);
        opt.Crossings.Add(new CrossingSignal { X = p.X, Y = p.Y + 1, Cycle = 60, PedGreen = 15, Clearance = 4, Offset = 10 });
        var res = TrafficAnalysis.Run(net, opt);
        var cp = Assert.Single(link.CrossingPlans);
        Assert.Equal(60, cp.Cycle);
        Assert.Equal(19, cp.Red);
        Assert.True(link.Capacity < link.BaseCapacity * 0.7);
        Assert.True(res.Links[link.Index].TravelTime > baseRes.Links[link.Index].TravelTime);
        // Desligado no cenário: some.
        opt.Crossings[0].Enabled = false;
        TrafficAnalysis.Run(net, opt);
        Assert.Empty(link.CrossingPlans);
    }

    [Fact]
    public void Solutions_AreTestedAndTheBestOneCleansTheCrossing()
    {
        var net = Crossing(0);                                   // PARE entre duas coletoras no pico: nível F
        var opt = new TrafficOptions { Demand = NivelDemanda.Pico };
        var res = TrafficAnalysis.Run(net, opt);
        var nd = net.Nodes.Single(n => n.Kind == TipoNo.Intersecao);
        Assert.Equal("F", res.Nodes[nd.Index].LOS);
        var trials = TrafficSolutions.For(net, opt, res, nd.Index, null);
        Assert.Contains(trials, t => t.Title.StartsWith("Semaforizar"));
        Assert.Contains(trials, t => t.Title == "Rotatória");
        var best = trials[0];
        Assert.True(best.Improves, best.Summary());
        Assert.True(best.LocalAfter < best.LocalBefore / 2, best.Summary());
        Assert.False(string.IsNullOrWhiteSpace(best.InProject));
        // A rede volta ao cenário original depois dos testes.
        Assert.Equal(ControleNo.Pare, nd.Control);
        // Aplicar no cenário reproduz o efeito medido.
        best.Apply(opt);
        var after = TrafficAnalysis.Run(net, opt);
        Assert.Equal(best.LocalAfter, after.Nodes[nd.Index].Delay, 3);
    }

    [Fact]
    public void Solutions_ScenarioLeftTurnBanReroutesTraffic()
    {
        var net = Crossing(1);
        var opt = new TrafficOptions { Demand = NivelDemanda.Media };
        var nd = net.Nodes.Single(n => n.Kind == TipoNo.Intersecao);
        opt.Nodes.Add(new NodeOverride { Node = nd.Key, NoLeft = true });
        var res = TrafficAnalysis.Run(net, opt);
        Assert.All(res.Nodes[nd.Index].Approaches, a => Assert.True(a.LeftVolume < 1));
    }

    [Fact]
    public void SolutionPackage_Signal_ListsEverythingForTheProject()
    {
        var net = Crossing(0);
        var opt = new TrafficOptions { Demand = NivelDemanda.Pico };
        var res = TrafficAnalysis.Run(net, opt);
        var nd = net.Nodes.Single(n => n.Kind == TipoNo.Intersecao);
        var sig = TrafficSolutions.For(net, opt, res, nd.Index, null).Single(t => t.Kind == "semaforo");
        var pk = Assert.IsType<ProjectPackage>(sig.Package);
        Assert.Equal(Definitions.ControleIntersecao.Semaforo, pk.Control);
        Assert.NotNull(pk.Plan);
        Assert.True(pk.Plan!.Phases.Count >= 2);
        // Um grupo focal por aproximação (4), junto à retenção, virado para quem chega.
        var heads = pk.Points.Where(p => p.Kind == "Semaforo").ToList();
        Assert.True(heads.Count >= 4);
        foreach (var h in heads) Assert.InRange(h.Position.Length, 5, 40);
        Assert.Contains(pk.Items, i => i.Norm.Contains("MBST Vol. V"));
        Assert.Contains(pk.Items, i => i.Norm.Contains("NBR 9050"));
        Assert.Null(sig.NormNote);
        Assert.Contains("Semáforo", pk.Text());
    }

    [Fact]
    public void SolutionWarrant_BlocksSignalWhenVolumesDoNotJustifyIt()
    {
        var net = Crossing(2);
        var opt = new TrafficOptions { Demand = NivelDemanda.Baixa, Growth = -0.8 };
        var res = TrafficAnalysis.Run(net, opt);
        var nd = net.Nodes.Single(n => n.Kind == TipoNo.Intersecao);
        var sig = TrafficSolutions.For(net, opt, res, nd.Index, null).Single(t => t.Kind == "semaforo");
        Assert.NotNull(sig.NormNote);
        Assert.False(sig.Improves);
        Assert.Contains("MBST Vol. V", sig.Summary());
    }

    [Fact]
    public void SolutionPackages_RoundaboutAndLeftBan_AreComplete()
    {
        var net = Crossing(0);
        var opt = new TrafficOptions { Demand = NivelDemanda.Pico };
        var res = TrafficAnalysis.Run(net, opt);
        var nd = net.Nodes.Single(n => n.Kind == TipoNo.Intersecao);
        var trials = TrafficSolutions.For(net, opt, res, nd.Index, null);
        var rb = trials.Single(t => t.Kind == "rotatoria").Package!;
        Assert.NotNull(rb.Roundabout);
        Assert.Contains(rb.Items, i => i.Item.Contains("R-33"));
        var left = trials.Single(t => t.Kind == "proibiresquerda").Package!;
        Assert.False(left.LeftTurns);
        Assert.Contains(left.Items, i => i.Item.Contains("R-4a"));
        Assert.Contains(trials, t => t.Kind == "bolsao");
    }
}
