using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada S3: Simulador de Tráfego (rede, análise HCM, diagnóstico, microssimulação e relatório).</summary>
public class V24Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private sealed class Scene
    {
        public readonly List<MarkingDefinition> Defs = new();
        private readonly Dictionary<string, Polyline2> _paths = new();

        public RoadPavementDefinition Road(int template, params Vec2[] pts)
        {
            var g = RoadTemplates.All[template].Create().Build(PathReference.FromPoints(pts, 0), new OutputSettings(), Cat);
            foreach (var m in g) _paths[m.Id] = new Polyline2(pts);
            Defs.AddRange(g);
            return g.OfType<RoadPavementDefinition>().First();
        }

        public void Intersection(Vec2 at, ControleIntersecao c, params RoadPavementDefinition[] roads) =>
            Defs.Add(new IntersectionDefinition { Node = at, Control = c, RoadIds = roads.Select(r => r.Id).ToList(), MainRoadId = roads[0].Id, Crosswalks = true });

        public TrafficNetwork Network() =>
            TrafficNetworkBuilder.Build(Defs, d => _paths.TryGetValue(d.Id, out var p) ? (p, 0.0) : d.Path is { Points.Count: >= 2 } pr ? (new Polyline2(pr.Points), 0.0) : null);
    }

    /// <summary>Malha: avenida E-O com semáforo, PARE, rotatória; coletoras N-S; locais com PARE, Dê a preferência e sem controle.</summary>
    private static Scene Grid()
    {
        var s = new Scene();
        var av = s.Road(2, new Vec2(-250, 0), new Vec2(550, 0));
        var c1 = s.Road(1, new Vec2(0, -250), new Vec2(0, 400));
        var c2 = s.Road(1, new Vec2(300, -250), new Vec2(300, 400));
        var l1 = s.Road(0, new Vec2(150, 0), new Vec2(150, 200));
        var l2 = s.Road(0, new Vec2(0, 200), new Vec2(300, 200));
        s.Intersection(new Vec2(0, 0), ControleIntersecao.Semaforo, av, c1);
        s.Intersection(new Vec2(150, 0), ControleIntersecao.Pare, av, l1);
        s.Intersection(new Vec2(0, 200), ControleIntersecao.Pare, c1, l2);
        s.Intersection(new Vec2(150, 200), ControleIntersecao.Nenhum, l2, l1);
        var rb = new RoundaboutDefinition { Center = new Vec2(300, 0) };
        rb.ApplyPreset(TipoRotatoria.DuasFaixas);
        s.Defs.Add(rb);
        s.Intersection(new Vec2(300, 200), ControleIntersecao.DePreferencia, c2, l2);
        return s;
    }

    /// <summary>Cruzamento isolado de duas coletoras (ou coletora × local no PARE).</summary>
    private static TrafficNetwork Isolated(string kind)
    {
        var s = new Scene();
        var a = s.Road(kind == "rb2" ? 2 : 1, new Vec2(-300, 0), new Vec2(300, 0));
        var b = s.Road(kind == "stop" ? 0 : kind == "rb2" ? 2 : 1, new Vec2(0, -300), new Vec2(0, 300));
        switch (kind)
        {
            case "rb2":
                var rb = new RoundaboutDefinition { Center = Vec2.Zero };
                rb.ApplyPreset(TipoRotatoria.DuasFaixas);
                s.Defs.Add(rb);
                break;
            case "none":
                break;
            default:
                s.Intersection(Vec2.Zero, kind == "sig" ? ControleIntersecao.Semaforo : ControleIntersecao.Pare, a, b);
                break;
        }
        return s.Network();
    }

    [Fact]
    public void Network_ReadsRoadsNodesAndControls()
    {
        var net = Grid().Network();
        Assert.Equal(5, net.Roads.Count);
        Assert.Equal(6, net.Nodes.Count(n => n.IsZone));
        Assert.Equal(6, net.Nodes.Count(n => !n.IsZone));
        Assert.Contains(net.Nodes, n => n.Control == ControleNo.Semaforo && n.Pos.DistanceTo(Vec2.Zero) < 1);
        Assert.Contains(net.Nodes, n => n.Kind == TipoNo.Rotatoria && n.RoundaboutLanes == 2);
        Assert.Contains(net.Nodes, n => n.Control == ControleNo.PreferenciaDireita);
        Assert.Contains(net.Nodes, n => n.Control == ControleNo.DePreferencia);
        // Todos os trechos ligam dois nós diferentes; em cada nó chega e sai tráfego.
        Assert.All(net.Links, l => Assert.NotEqual(l.From, l.To));
        Assert.All(net.Nodes, n => Assert.True(n.In.Count > 0 && n.Out.Count > 0, n.Label));
        // Mão direita: a faixa do sentido fica à direita do eixo no sentido do trecho.
        Assert.All(net.Links.Where(l => l.Road.TwoWay), l => Assert.True(l.LaneOffset(0) > 0));
    }

    [Fact]
    public void Analysis_LowDemandFlowsWell()
    {
        var net = Grid().Network();
        var res = TrafficAnalysis.Run(net, new TrafficOptions { Demand = NivelDemanda.Baixa });
        Assert.True(res.TotalDemand > 1000);
        Assert.True(res.Unserved < 1);
        Assert.True(res.AvgSpeed > 20, $"velocidade média {res.AvgSpeed:0.0}");
        var sig = res.Nodes.Values.First(n => n.Node.Control == ControleNo.Semaforo);
        Assert.InRange(sig.Cycle, 45, 120);
        Assert.True(string.CompareOrdinal(sig.LOS, "C") <= 0, $"semáforo em nível {sig.LOS}");
        Assert.All(sig.Approaches, a => Assert.True(a.X < 1));
        // Verde de pedestres: nenhuma fase principal menor que o tempo de travessia.
        Assert.All(sig.Phases.Where(p => !p.LeftOnly && !p.Name.Contains("antecipado")), p => Assert.True(p.Green >= 7 + 6.5 / 1.2));
    }

    [Fact]
    public void Analysis_HeavyLeftTurnsGetAProtectedPhase()
    {
        var net = Grid().Network();
        var res = TrafficAnalysis.Run(net, new TrafficOptions { Demand = NivelDemanda.Media });
        var sig = res.Nodes.Values.First(n => n.Node.Control == ControleNo.Semaforo);
        Assert.Contains(sig.Approaches, a => a.ProtectedLeft);
        Assert.True(sig.Phases.Count > 2);
        Assert.Contains(res.Diagnostics, d => d.Title.Contains("fase exclusiva para a conversão à esquerda"));
    }

    [Fact]
    public void Analysis_MinorStreetAtStopHasLessCapacityThanMajor()
    {
        var res = TrafficAnalysis.Run(Isolated("stop"), new TrafficOptions { Demand = NivelDemanda.Media });
        var nr = res.Nodes.Values.First(n => n.Node.Kind == TipoNo.Intersecao);
        var major = nr.Approaches.Where(a => a.Major).ToList();
        var minor = nr.Approaches.Where(a => !a.Major).ToList();
        Assert.Equal(2, major.Count);
        Assert.Equal(2, minor.Count);
        Assert.True(minor.Max(a => a.Capacity) < major.Min(a => a.Capacity));
        Assert.True(minor.Min(a => a.Delay) > major.Max(a => a.Delay));
    }

    [Fact]
    public void Analysis_RoundaboutCapacityFollowsHcm()
    {
        var res = TrafficAnalysis.Run(Isolated("rb2"), new TrafficOptions { Demand = NivelDemanda.Baixa });
        var nr = res.Nodes.Values.First(n => n.Node.Kind == TipoNo.Rotatoria);
        Assert.Equal(4, nr.Approaches.Count);
        // Duas faixas de entrada com fluxo circulante moderado: entre 1 000 e 2 300 veh/h por entrada (HCM 6).
        Assert.All(nr.Approaches, a => Assert.InRange(a.Capacity, 1000, 2300));
        Assert.Equal("A", nr.LOS);
    }

    [Fact]
    public void Diagnostics_FlagUncontrolledCrossingAndSpeedLimit()
    {
        // Coletora e local se cruzando sem interseção do plugin; local a 40 km/h (acima dos 30 km/h do CTB, art. 61).
        var net = Isolated("none");
        var res = TrafficAnalysis.Run(net, new TrafficOptions { Demand = NivelDemanda.Media });
        Assert.Contains(res.Diagnostics, d => d.Title.Contains("sem interseção modelada"));
        var grid = TrafficAnalysis.Run(Grid().Network(), new TrafficOptions { Demand = NivelDemanda.Media });
        Assert.Contains(grid.Diagnostics, d => d.Category == "Legislação" && d.Title.Contains("acima da máxima"));
        // Diagnósticos ordenados por gravidade e sem repetição.
        Assert.True(grid.Diagnostics.Zip(grid.Diagnostics.Skip(1)).All(x => x.First.Severity >= x.Second.Severity));
        Assert.Equal(grid.Diagnostics.Count, grid.Diagnostics.Select(d => (d.Title, d.Node, d.Link)).Distinct().Count());
    }

    [Fact]
    public void Simulation_SignalServesTheDemandWithoutGridlock()
    {
        var res = TrafficAnalysis.Run(Isolated("sig"), new TrafficOptions { Demand = NivelDemanda.Media, SimSeconds = 600, WarmupSeconds = 120 });
        var sim = TrafficSimulation.Run(res);
        Assert.True(sim.Spawned > 300);
        Assert.True(sim.Completed >= 0.85 * sim.Spawned, $"{sim.Completed} de {sim.Spawned}");
        Assert.True(sim.Backlog < 10);
        Assert.True(sim.MeanDelay < 90);
        // Quadros da animação: 6 valores por veículo, semáforo com fase registrada.
        Assert.True(sim.Frames.Count > 600);
        Assert.All(sim.Frames.Take(50), f => Assert.Equal(0, f.Data.Length % SimFrame.Stride));
        Assert.Contains(sim.Frames, f => f.SignalPhase.Values.Any(v => v >= 0));
        Assert.Contains(sim.Frames, f => f.SignalPhase.Values.Any(v => v < 0));
    }

    [Fact]
    public void Simulation_GridRunsAndIsDeterministic()
    {
        var net = Grid().Network();
        var opt = new TrafficOptions { Demand = NivelDemanda.Baixa, SimSeconds = 420, WarmupSeconds = 120, Seed = 3 };
        var a = TrafficSimulation.Run(TrafficAnalysis.Run(net, opt));
        var b = TrafficSimulation.Run(TrafficAnalysis.Run(net, opt));
        Assert.Equal(a.Completed, b.Completed);
        Assert.Equal(a.Spawned, b.Spawned);
        Assert.True(a.Completed >= 0.8 * a.Spawned, $"{a.Completed} de {a.Spawned}");
        // Veículos andam pelo lado direito da via (mão direita).
        var net2 = net;
        var f = a.Frames[^1];
        var right = 0;
        var total = 0;
        for (int k = 0; k < f.Count; k++)
        {
            var p = new Vec2(f.Data[k * SimFrame.Stride], f.Data[k * SimFrame.Stride + 1]);
            var h = f.Data[k * SimFrame.Stride + 2];
            var d = new Vec2(Math.Cos(h), Math.Sin(h));
            if (net2.Nodes.Any(n => !n.IsZone && n.Pos.DistanceTo(p) < 15)) continue;      // dentro do cruzamento: a via mais próxima é ambígua
            var road = net2.Roads.MinBy(r => Math.Abs(r.Axis.Project(p).Signed))!;
            var (st, signed) = road.Axis.Project(p);
            if (st < 5 || st > road.Axis.Length - 5 || Math.Abs(signed) > 12) continue;
            total++;
            var t = road.Axis.TangentAt(st);
            var sameDir = t.Dot(d) > 0;
            if (sameDir ? signed < 0 : signed > 0) right++;
        }
        Assert.True(total > 10);
        Assert.True(right >= 0.95 * total, $"{right} de {total} à direita");
    }

    [Fact]
    public void Report_ListsNetworkNodesDiagnosticsAndMethod()
    {
        var res = TrafficAnalysis.Run(Grid().Network(), new TrafficOptions { Demand = NivelDemanda.Media });
        var txt = TrafficReport.Build(res, null, "Teste");
        Assert.Contains("SIMULADOR DE TRÁFEGO", txt);
        Assert.Contains("I01", txt);
        Assert.Contains("R01", txt);
        Assert.Contains("Plano semafórico", txt);
        Assert.Contains("7. DIAGNÓSTICO", txt);
        Assert.Contains("SEGURANÇA VIÁRIA E CUSTOS", txt);
        Assert.Contains("HCM", txt);
        var csv = TrafficReport.Csv(res);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 15);
        Assert.All(lines, l => Assert.Equal(lines[0].Count(c => c == ';'), l.Count(c => c == ';')));
    }
}
