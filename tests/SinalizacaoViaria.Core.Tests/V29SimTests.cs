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

        public TrafficNetwork Network() =>
            TrafficNetworkBuilder.Build(Defs, d => Paths.TryGetValue(d.Id, out var p) ? (p, 0.0) : d.Path is { Points.Count: >= 2 } pr ? (new Polyline2(pr.Points, pr.Closed), 0.0) : null);
    }

    /// <summary>Cruzamento de duas coletoras de duas faixas por sentido: 0 PARE, 1 semáforo, 2 sem controle, 3 rotatória de uma faixa.</summary>
    private static TrafficNetwork Crossing(int kind)
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
        });
        return s.Network();
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
}
