using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada U: emenda pela ponta, várias linhas → várias vias, cotar seção, simulador lendo a sinalização.</summary>
public class V28Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static IntersectionDemo.Scene BendScene(int a, int b, double angleDeg)
    {
        var defs = new List<MarkingDefinition>();
        var paths = new Dictionary<string, Polyline2>();
        var roads = new List<IntersectionRoad>();
        var groups = new List<List<MarkingDefinition>>();
        void Road(int tpl, Vec2 p, Vec2 q)
        {
            var pr = PathReference.FromPoints(new[] { p, q }, 0);
            var g = RoadTemplates.All[tpl].Create().Build(pr, new OutputSettings(), Cat);
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) paths[m.Id] = axis;
            defs.AddRange(g);
            groups.Add(g);
            roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        Road(a, new Vec2(0, 80), Vec2.Zero);
        var u = new Vec2(Math.Cos(angleDeg * Math.PI / 180), Math.Sin(angleDeg * Math.PI / 180));
        Road(b, Vec2.Zero, u * 80);
        return IntersectionDemo.Create(new IntersectionDefinition { Node = Vec2.Zero, CornerRadius = 6 }, roads, groups, paths, defs, Cat);
    }

    [Theory]
    [InlineData(0, 0, -45)]
    [InlineData(1, 0, -80)]
    [InlineData(2, 2, -110)]
    public void Bend_ContinuationIsASmoothJointWithoutGaps(int a, int b, double ang)
    {
        var s = BendScene(a, b, ang);
        Assert.True(s.Layout.IsBend);
        var geo = IntersectionDemo.Build(s, new BuildContext { Catalog = Cat }, false);
        var asphalt = geo.Pieces.Where(p => p.Color == MarkingColor.Asfalto).Select(p => p.Shape).ToList();
        // Pista contínua: pontos nas faixas dos dois lados, ao longo de toda a emenda, estão sobre asfalto (sem cunha vazia).
        var bend = s.Layout.BendAxis!;
        for (var t = 0.0; t <= bend.Length; t += 0.5)
            foreach (var o in new[] { -2.8, 2.8 })
            {
                var p = bend.PointAt(t) + bend.TangentAt(t).PerpLeft * o;
                Assert.Contains(asphalt, x => x.Contains(p) || x.DistanceTo(p) < 0.05);
            }
        // Linhas pintadas continuam pela curva (filhos da emenda), sem travessias nem placas.
        var children = s.Definitions.Where(x => x.GroupId == s.Definitions.OfType<IntersectionDefinition>().First().Id).ToList();
        Assert.NotEmpty(children.OfType<LinearMarkingDefinition>());
        Assert.DoesNotContain(children, c => c is SignDefinition or RampDefinition);
    }

    [Fact]
    public void Bend_StraightContinuationOnlyWhenSectionsDiffer()
    {
        var same = new List<IntersectionRoad>
        {
            new(RoadTemplates.All[0].Create().Build(PathReference.FromPoints(new[] { new Vec2(-50, 0), Vec2.Zero }, 0), new OutputSettings(), Cat).OfType<RoadPavementDefinition>().First(), new Polyline2(new[] { new Vec2(-50, 0), Vec2.Zero })),
            new(RoadTemplates.All[0].Create().Build(PathReference.FromPoints(new[] { Vec2.Zero, new Vec2(50, 0) }, 0), new OutputSettings(), Cat).OfType<RoadPavementDefinition>().First(), new Polyline2(new[] { Vec2.Zero, new Vec2(50, 0) })),
        };
        Assert.False(IntersectionGenerator.NeedsIntersection(same, Vec2.Zero));
        var diff = new List<IntersectionRoad> { same[0], same[1] with { Def = RoadTemplates.All[1].Create().Build(PathReference.FromPoints(new[] { Vec2.Zero, new Vec2(50, 0) }, 0), new OutputSettings(), Cat).OfType<RoadPavementDefinition>().First() } };
        Assert.True(IntersectionGenerator.NeedsIntersection(diff, Vec2.Zero));
    }

    [Fact]
    public void RoadChains_GridOfLinesBecomesSeparateRoads()
    {
        IReadOnlyList<Vec2> L(params (double X, double Y)[] p) => p.Select(q => new Vec2(q.X, q.Y)).ToList();
        var curves = new List<IReadOnlyList<Vec2>>
        {
            L((0, 0), (100, 0)), L((100, 0), (200, 0)),          // avenida desenhada em dois trechos (cruzamento no meio)
            L((100, -80), (100, 0)), L((100, 0), (100, 80)),     // transversal em dois trechos
            L((200, 0), (260, 60)),                              // continuação em ângulo da avenida (deflexão de 45°)
            L((0, 0), (0, 90)),                                  // rua que sai em L da ponta da avenida... com a avenida já seguindo: nó de grau 2 → mesma via
            L((50, 0), (50, 60)),                                // T no meio de um trecho (a ponta não é nó de linhas)
        };
        var groups = RoadChains.Split(curves);
        // Avenida: 0 + 1 (alinhadas no nó de grau 4) + 4 (encontro só das duas) + 5 (L na outra ponta).
        Assert.Contains(groups, g => g.OrderBy(x => x).SequenceEqual(new[] { 0, 1, 4, 5 }));
        Assert.Contains(groups, g => g.OrderBy(x => x).SequenceEqual(new[] { 2, 3 }));
        Assert.Contains(groups, g => g.SequenceEqual(new[] { 6 }));
        Assert.Equal(3, groups.Count);
    }

    // ================================================================== simulador lendo a sinalização

    private sealed class TScene
    {
        public readonly List<MarkingDefinition> Defs = new();
        private readonly Dictionary<string, Polyline2> _paths = new();
        public RoadPavementDefinition Road(int tpl, params Vec2[] pts)
        {
            var axis = new Polyline2(pts);
            var g = RoadTemplates.All[tpl].Create().Build(PathReference.FromPoints(pts, 0), new OutputSettings(), Cat, axis: axis);
            foreach (var m in g) _paths[m.Id] = axis;
            Defs.AddRange(g);
            return g.OfType<RoadPavementDefinition>().First();
        }
        public void Sign(string code, Vec2 at, Vec2 dir, string? legend = null) => Defs.Add(new SignDefinition { Code = code, Position = at, Direction = dir, Legend = legend });
        public Traffic.TrafficNetwork Net() => Traffic.TrafficNetworkBuilder.Build(Defs,
            d => _paths.TryGetValue(d.Id, out var p) ? (p, 0.0) : d.Path is { Points.Count: >= 2 } pr ? (new Polyline2(pr.Points, pr.Closed), 0.0) : null,
            d => { try { return MarkingBuilder.Build(d, d.Path is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points, pr.Closed) : null, new BuildContext { Catalog = Cat }); } catch { return null; } });
    }

    /// <summary>Cruzamento sem interseção modelada: via E-O (coletora) × via N-S (local).</summary>
    private static (TScene S, RoadPavementDefinition Main, RoadPavementDefinition Minor) Cross()
    {
        var s = new TScene();
        var a = s.Road(1, new Vec2(-300, 0), new Vec2(300, 0));
        var b = s.Road(0, new Vec2(0, -300), new Vec2(0, 300));
        return (s, a, b);
    }

    [Fact]
    public void Regulations_SpeedLimitSignSlowsTheRoadDownstream()
    {
        var (s, a, _) = Cross();
        s.Sign("R-19", new Vec2(-200, -a.RightWidth - 2), Vec2.UnitX, "30");
        var net = s.Net();
        var fwd = net.Links.Where(l => l.Road.Id == a.Id && l.Forward).OrderBy(l => l.S0).ToList();
        Assert.All(fwd, l => Assert.Equal(30, l.SpeedLimitKmh));
        Assert.All(fwd, l => Assert.True(l.FreeSpeed <= 30 / 3.6 + 1e-6));
        Assert.All(net.Links.Where(l => l.Road.Id == a.Id && !l.Forward), l => Assert.Null(l.SpeedLimitKmh));
        Assert.Contains(net.Regulations, r => r.Code == "R-19" && r.Applied);
    }

    [Fact]
    public void Regulations_StopSignsControlAnUncontrolledCrossingAndTurnBansAreObeyed()
    {
        var (s, a, b) = Cross();
        // R-1 nas duas aproximações da via N-S e R-4a (proibido virar à esquerda) na aproximação oeste da via E-O.
        s.Sign("R-1", new Vec2(b.RightWidth + 2, -25), Vec2.UnitY);
        s.Sign("R-1", new Vec2(-b.RightWidth - 2, 25), -Vec2.UnitY);
        s.Sign("R-4a", new Vec2(-40, -a.RightWidth - 2), Vec2.UnitX);
        var net = s.Net();
        var nd = net.Nodes.First(n => !n.IsZone && n.Kind != Traffic.TipoNo.Continuacao);
        Assert.Equal(Traffic.ControleNo.Pare, nd.Control);
        Assert.Equal(2, nd.StopApproaches.Count);
        var west = nd.In.First(i => net.Links[i].Road.Id == a.Id && net.Links[i].Forward);
        foreach (var o in nd.Out)
            if (Traffic.TrafficAnalysis.TurnOf(net, west, o) == Traffic.Giro.Esquerda) Assert.False(Traffic.TrafficAnalysis.Allowed(net, nd, west, o));
        var r = Traffic.TrafficAnalysis.Run(net, new Traffic.TrafficOptions());
        var ap = r.Nodes[nd.Index].Approaches.First(x => x.Link == west);
        Assert.True(ap.Major);
        Assert.Equal(0, ap.Movements.GetValueOrDefault(Traffic.Giro.Esquerda), 3);
        Assert.All(r.Nodes[nd.Index].Approaches.Where(x => net.Links[x.Link].Road.Id == b.Id), x => Assert.False(x.Major));
        var sim = Traffic.TrafficSimulation.Run(Traffic.TrafficAnalysis.Run(net, new Traffic.TrafficOptions { Demand = Traffic.NivelDemanda.Baixa, SimSeconds = 300, WarmupSeconds = 60 }));
        Assert.True(sim.Completed > 0.6 * sim.Spawned);
    }

    [Fact]
    public void Regulations_WrongWaySignMakesItOneWayAndBarriersBlockOrSeparate()
    {
        var (s, a, b) = Cross();
        // R-3 lido por quem viria para o sul na via N-S, ao norte do cruzamento: sentido sul proibido.
        s.Sign("R-3", new Vec2(-b.RightWidth - 2, 60), -Vec2.UnitY);
        // New Jersey sobre o eixo da via E-O atravessando o cruzamento.
        s.Defs.Add(new DeviceMarkingDefinition { Code = "NJ", PathRef = PathReference.FromPoints(new[] { new Vec2(-120, 0), new Vec2(120, 0) }, 0) });
        var net = s.Net();
        Assert.Contains(net.Links, l => l.Road.Id == b.Id && l.Closed);
        var nd = net.Nodes.First(n => !n.IsZone && n.Kind != Traffic.TipoNo.Continuacao);
        Assert.Contains(a.Id, nd.MedianRoads);
        var r = Traffic.TrafficAnalysis.Run(net, new Traffic.TrafficOptions());
        // Ninguém atravessa a via E-O (seguindo a N-S) nem converte à esquerda a partir dela.
        foreach (var ap in r.Nodes[nd.Index].Approaches)
        {
            if (net.Links[ap.Link].Road.Id == b.Id) Assert.Equal(0, ap.Movements.GetValueOrDefault(Traffic.Giro.Frente), 3);
            Assert.Equal(0, ap.Movements.GetValueOrDefault(Traffic.Giro.Esquerda), 3);
        }
        Assert.All(net.Links.Where(l => l.Closed), l => Assert.True(r.Links[l.Index].Volume < 1));
        // Bloqueio transversal (prismas) atravessando toda a via N-S ao sul: os dois sentidos fechados ali.
        var (s2, _, b2) = Cross();
        s2.Defs.Add(new DeviceMarkingDefinition { Code = "PRI", PathRef = PathReference.FromPoints(new[] { new Vec2(-8, -150), new Vec2(8, -150) }, 0) });
        var net2 = s2.Net();
        Assert.Equal(2, net2.Links.Count(l => l.Road.Id == b2.Id && l.Closed));
    }

    [Fact]
    public void Regulations_HatchClosesALaneAndArrowsAssignLanes()
    {
        var (s, a, _) = Cross();
        // Zebrado cobrindo a faixa da direita do sentido do eixo entre x = -200 e -150 (coletora: 2 faixas por sentido).
        s.Defs.Add(new HatchMarkingDefinition { Code = "ZPA", Boundary = PathReference.FromPoints(new[] { new Vec2(-200, -6.8), new Vec2(-150, -6.8), new Vec2(-150, -3.6), new Vec2(-200, -3.6) }, 0, true) });
        // Setas na aproximação oeste: faixa da direita só em frente, faixa da esquerda só à esquerda (sem direita).
        s.Defs.Add(new SymbolMarkingDefinition { Code = "PEM-F", Position = new Vec2(-30, -5.2), Direction = Vec2.UnitX });
        s.Defs.Add(new SymbolMarkingDefinition { Code = "PEM-E", Position = new Vec2(-30, -1.7), Direction = Vec2.UnitX });
        var net = s.Net();
        var west = net.Links.First(l => l.Road.Id == a.Id && l.Forward && l.S1 <= 301 && l.To == net.Nodes.First(n => !n.IsZone && n.Kind != Traffic.TipoNo.Continuacao).Index);
        Assert.NotEmpty(west.ClosedLanes);
        Assert.Equal(1, west.MinOpenLanes);
        Assert.Equal(2, west.LaneTurns.Count);
        var nd = net.Nodes[west.To];
        foreach (var o in nd.Out)
            if (Traffic.TrafficAnalysis.TurnOf(net, west.Index, o) == Traffic.Giro.Direita) Assert.False(Traffic.TrafficAnalysis.Allowed(net, nd, west.Index, o));
        var sim = Traffic.TrafficSimulation.Run(Traffic.TrafficAnalysis.Run(net, new Traffic.TrafficOptions { Demand = Traffic.NivelDemanda.Baixa, SimSeconds = 300, WarmupSeconds = 60 }));
        Assert.True(sim.Completed > 0.6 * sim.Spawned);
    }

    [Fact]
    public void Regulations_SignalHeadsMakeTheCrossingSignalizedAndBendIsContinuation()
    {
        var (s, _, _) = Cross();
        s.Defs.Add(new UrbanElementDefinition { Code = "SEMAFORO", Position = new Vec2(10, -10) });
        var net = s.Net();
        var nd = net.Nodes.First(n => !n.IsZone && n.Kind != Traffic.TipoNo.Continuacao);
        Assert.Equal(Traffic.ControleNo.Semaforo, nd.Control);
        Assert.True(nd.SignalHeads);
        var r = Traffic.TrafficAnalysis.Run(net, new Traffic.TrafficOptions());
        Assert.True(r.Nodes[nd.Index].Cycle > 0);
        Assert.Contains("Sinalização interpretada", Traffic.TrafficReport.Build(r));
        // Emenda pela ponta (interseção de duas vias que terminam no nó): continuação livre.
        var bs = BendScene(0, 0, -45);
        var bnet = Traffic.TrafficNetworkBuilder.Build(bs.Definitions, d => bs.Paths.TryGetValue(d.Id, out var p) ? (p, 0.0) : null);
        Assert.Contains(bnet.Nodes, n => n.Kind == Traffic.TipoNo.Continuacao && n.SourceId != null);
        Assert.DoesNotContain(bnet.Nodes, n => n.Kind == Traffic.TipoNo.Intersecao);
    }
}
