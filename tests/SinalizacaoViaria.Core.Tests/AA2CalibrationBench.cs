using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Bancada de calibração da microssimulação: cenas controladas (só os movimentos medidos, demanda imposta) e as medidas
/// comparáveis às referências – fluxo de saturação e tempo perdido no semáforo, brechas crítica e de seguimento no PARE e
/// na rotatória (HCM 7, cap. 20 e 22), equivalência dos veículos pesados, pedestres nas conversões, trocas de faixa
/// antecipadas e fila que transborda para o cruzamento anterior.
/// </summary>
public static class AA2CalibrationBench
{
    public sealed record Discharge(double SatHeadway, double SatFlow, double LostTime, double[] ByPosition, double HeavyPce);

    /// <summary>Cruzamento de duas vias do modelo <paramref name="tpl"/> com o controle pedido.</summary>
    public static (AA1SolutionReproTests.Model M, IntersectionDefinition? I, RoundaboutDefinition? R, TrafficNetwork Net) Cross(int tpl, ControleIntersecao c, bool roundabout = false, TipoRotatoria rbType = TipoRotatoria.UmaFaixa)
    {
        var m = new AA1SolutionReproTests.Model();
        var a = m.Road(tpl, new Vec2(-320, 0), new Vec2(320, 0));
        var b = m.Road(tpl, new Vec2(0, -300), new Vec2(0, 300));
        if (roundabout)
        {
            var rb = new RoundaboutDefinition { Center = Vec2.Zero };
            rb.ApplyPreset(rbType);
            rb.Legs = RoundaboutGenerator.LegsFromRoads(Vec2.Zero, new[] { new IntersectionRoad(a, m.Paths[a.Id]), new IntersectionRoad(b, m.Paths[b.Id]) }, rb.OuterRadius + 25);
            m.Defs.Add(rb);
            return (m, null, rb, m.Network());
        }
        var it = m.Intersection(Vec2.Zero, c, a, b);
        return (m, it, null, m.Network());
    }

    /// <summary>Movimento de um par OD no nó (em frente, esquerda…) pela rota da alocação.</summary>
    public static Giro? TurnAt(TrafficNetwork net, List<int> route, int node)
    {
        var i = route.FindIndex(l => net.Links[l].To == node);
        return i >= 0 && i + 1 < route.Count ? TrafficAnalysis.TurnOf(net, route[i], route[i + 1]) : null;
    }

    /// <summary>Demanda imposta: cada par OD recebe o volume que <paramref name="volume"/> devolver (0 = sem viagens).</summary>
    public static void Impose(TrafficResult macro, Func<List<int>, double> volume)
    {
        foreach (var k in macro.OD.Keys.ToList())
        {
            var r = macro.Routes.GetValueOrDefault(k);
            macro.OD[k] = r == null || r.Count == 0 ? 0 : volume(r[0].Links);
            if (r != null && r.Count > 1) macro.Routes[k] = new List<(double, List<int>)> { (1.0, r[0].Links) };
        }
    }

    public static TrafficOptions Options(double heavy = 0, double buses = 0, double peds = 0, int seconds = 1500, int seed = 7) =>
        new() { Demand = NivelDemanda.Pico, SimSeconds = seconds, WarmupSeconds = 150, HeavyVehicles = heavy, Buses = buses, PedestriansPerHour = peds, Seed = seed };

    // ------------------------------------------------------------------ semáforo: saturação e tempo perdido

    /// <summary>Descarga das filas em frente numa aproximação saturada (tpl 1: coletora, faixas de 3,3–3,5 m, sem estacionamento).</summary>
    public static Discharge SignalDischarge(int tpl = 1, double heavy = 0, int seed = 7)
    {
        var (_, it, _, net) = Cross(tpl, ControleIntersecao.Semaforo);
        var nd = net.Nodes.First(n => n.Key == it!.Id);
        var macro = TrafficAnalysis.Run(net, Options(heavy, 0, 0, seed: seed));
        Impose(macro, r => TurnAt(net, r, nd.Index) == Giro.Frente ? 1100 * net.Links[r[0]].Lanes : 0);
        var sim = TrafficSimulation.Run(macro);
        var d = sim.Discharge.Where(x => x.Turn == Giro.Frente).ToList();
        var cars = d.Where(x => x.Pos >= 5 && x.Type == 0).Select(x => x.Headway).ToList();
        var all = d.Where(x => x.Pos >= 5).Select(x => x.Headway).ToList();
        var heavyH = d.Where(x => x.Pos >= 5 && x.Type != 0).Select(x => x.Headway).ToList();
        var hs = (heavy > 0 ? all : cars).DefaultIfEmpty(double.NaN).Average();
        var hc = cars.DefaultIfEmpty(double.NaN).Average();
        var byPos = Enumerable.Range(1, 8).Select(p => d.Where(x => x.Pos == p).Select(x => x.Headway).DefaultIfEmpty(double.NaN).Average()).ToArray();
        var lost = byPos.Take(4).Sum(h => h - hc);
        // Equivalência pelo HCM: s_misto = s_base / (1 + P_T (E_T − 1)).
        var pce = heavy > 0 ? (SignalDischarge(tpl, 0, seed).SatFlow / (3600 / hs) - 1) / heavy + 1 : double.NaN;
        return new Discharge(hs, 3600 / hs, lost, byPos, pce);
    }

    // ------------------------------------------------------------------ PARE e rotatória: capacidade × fluxo conflitante

    /// <summary>
    /// Capacidade da aproximação secundária (PARE, em frente, saturada) com o fluxo principal <paramref name="major"/> (veh/h,
    /// dois sentidos, em frente). Vias locais (principal de 2 faixas).
    /// </summary>
    public static double StopCapacity(double major, int seed = 7, Giro minorTurn = Giro.Frente) => Stop(major, seed, minorTurn).Cap;

    /// <summary>Capacidade e brechas (Siegloch) da aproximação secundária com PARE.</summary>
    public static (double Cap, GapFit Fit) Stop(double major, int seed = 7, Giro minorTurn = Giro.Frente)
    {
        var (_, it, _, net) = Cross(0, ControleIntersecao.Pare);
        var nd = net.Nodes.First(n => n.Key == it!.Id);
        var mainRoad = nd.MainRoadId;
        var macro = TrafficAnalysis.Run(net, Options(seed: seed));
        int? minorIn = null;
        Impose(macro, r =>
        {
            var g = TurnAt(net, r, nd.Index);
            var inLink = r.First(l => net.Links[l].To == nd.Index);
            var onMain = net.Links[inLink].Road.Id == mainRoad;
            if (onMain) return g == Giro.Frente ? major / 2 : 0;
            // Secundária: só o sentido do eixo, saturada.
            if (!net.Links[inLink].Forward || g != minorTurn) return 0;
            minorIn = inLink;
            return 1200;
        });
        var sim = TrafficSimulation.Run(macro);
        return minorIn is { } li ? (sim.Approaches[li].Vehicles / (macro.Options.SimSeconds / 3600.0), Siegloch(sim, nd.Index, -1, li)) : (0, GapFit.None);
    }

    /// <summary>Capacidade de entrada da rotatória (uma faixa) com o fluxo circulante <paramref name="circ"/> passando em frente a ela.</summary>
    public static double RoundaboutCapacity(double circ, int seed = 7, TipoRotatoria type = TipoRotatoria.UmaFaixa) => Roundabout(circ, seed, type).Cap;

    /// <summary>Capacidade e brechas (Siegloch) da entrada da rotatória.</summary>
    public static (double Cap, GapFit Fit) Roundabout(double circ, int seed = 7, TipoRotatoria type = TipoRotatoria.UmaFaixa)
    {
        var (_, _, rb, net) = Cross(0, ControleIntersecao.Nenhum, true, type);
        var nd = net.Nodes.First(n => n.Kind == TipoNo.Rotatoria);
        var macro = TrafficAnalysis.Run(net, Options(seed: seed));
        int? entry = null;
        Impose(macro, r =>
        {
            var inLink = r.FirstOrDefault(l => net.Links[l].To == nd.Index, -1);
            var i = r.IndexOf(inLink);
            if (inLink < 0 || i + 1 >= r.Count) return 0;
            var a = net.Links[inLink].Path.Points[^1] - nd.Pos;
            var o = net.Links[r[i + 1]].Path.Points[0] - nd.Pos;
            // Entrada sul (medida) em frente para o norte; circulante oeste → leste passando em frente a ela.
            if (a.Y < -5 && Math.Abs(a.X) < 5 && o.Y > 5) { entry = inLink; return 1400; }
            if (a.X < -5 && Math.Abs(a.Y) < 5 && o.X > 5) return circ;
            return 0;
        });
        var sim = TrafficSimulation.Run(macro);
        return entry is { } li ? (sim.Approaches[li].Vehicles / (macro.Options.SimSeconds / 3600.0), Siegloch(sim, nd.Index, li, li)) : (0, GapFit.None);
    }

    /// <summary>
    /// Brecha crítica e de seguimento estimadas (s), o número de brechas usadas e a fração de brechas aproveitadas por
    /// comprimento (índice = segundos inteiros da brecha).
    /// </summary>
    public sealed record GapFit(double Tc, double Tf, double TfDirect, int Gaps)
    {
        public static readonly GapFit None = new(double.NaN, double.NaN, double.NaN, 0);
        public double[] Accepted { get; init; } = Array.Empty<double>();
    }

    /// <summary>
    /// Método de Siegloch (entrada sempre com fila): para cada brecha entre duas passagens de quem tem a preferência conta
    /// quantos entraram; a reta brecha média × número de entradas dá tf (inclinação) e t0 (intercepto); tc = t0 + tf/2.
    /// <paramref name="passLink"/>: −1 = veículos da via principal entrando no nó (PARE); senão a entrada da rotatória.
    /// </summary>
    public static GapFit Siegloch(SimResult sim, int node, int passLink, int entryLink)
    {
        var pass = sim.GapEvents.Where(e => e.Node == node && !e.Entry && e.Link == passLink).Select(e => e.Time).OrderBy(t => t).ToList();
        var ent = sim.GapEvents.Where(e => e.Node == node && e.Entry && e.Link == entryLink).Select(e => e.Time).OrderBy(t => t).ToList();
        var byN = new Dictionary<int, List<double>>();
        var follow = new List<double>();
        for (int i = 0; i + 1 < pass.Count; i++)
        {
            var inGap = ent.Where(t => t > pass[i] && t <= pass[i + 1]).ToList();
            var gap = pass[i + 1] - pass[i];
            if (gap < 0.3) continue;   // dois veículos lado a lado (faixas da principal) contam como uma passagem
            (byN.TryGetValue(inGap.Count, out var l) ? l : byN[inGap.Count] = new()).Add(gap);
            for (int k = 1; k < inGap.Count; k++) follow.Add(inGap[k] - inGap[k - 1]);
        }
        // Fração das brechas de cada comprimento (s inteiros) em que ao menos um entrou.
        var accepted = Enumerable.Range(0, 13).Select(s =>
        {
            var inBin = byN.SelectMany(kv => kv.Value.Where(g => (int)g == s).Select(_ => kv.Key)).ToList();
            return inBin.Count < 5 ? double.NaN : inBin.Count(n => n > 0) / (double)inBin.Count;
        }).ToArray();
        var pts = byN.Where(kv => kv.Key >= 1 && kv.Value.Count >= 4).Select(kv => (X: (double)kv.Key, Y: kv.Value.Average(), W: kv.Value.Count)).ToList();
        if (pts.Count < 2) return GapFit.None with { TfDirect = follow.DefaultIfEmpty(double.NaN).Average(), Gaps = byN.Values.Sum(v => v.Count), Accepted = accepted };
        double sw = pts.Sum(p => p.W), mx = pts.Sum(p => p.W * p.X) / sw, my = pts.Sum(p => p.W * p.Y) / sw;
        var tf = pts.Sum(p => p.W * (p.X - mx) * (p.Y - my)) / pts.Sum(p => p.W * (p.X - mx) * (p.X - mx));
        var t0 = my - tf * mx;
        return new GapFit(t0 + tf / 2, tf, follow.DefaultIfEmpty(double.NaN).Average(), byN.Values.Sum(v => v.Count)) { Accepted = accepted };
    }

    /// <summary>Capacidade HCM por aceitação de brechas (veh/h).</summary>
    public static double Hcm(double vc, double tc, double tf) => vc < 1 ? 3600 / tf : vc * Math.Exp(-vc * tc / 3600) / (1 - Math.Exp(-vc * tf / 3600));

    /// <summary>Brecha crítica equivalente (s) que reproduz a capacidade medida com o tf medido.</summary>
    public static double ImpliedTc(double vc, double cap, double tf)
    {
        double lo = 1, hi = 15;
        for (int k = 0; k < 60; k++) { var m = (lo + hi) / 2; if (Hcm(vc, m, tf) > cap) lo = m; else hi = m; }
        return (lo + hi) / 2;
    }

    // ------------------------------------------------------------------ pedestres nas conversões à direita

    /// <summary>Vazão da conversão à direita saturada no semáforo com <paramref name="peds"/> pedestres/h nas travessias (veh/h de verde).</summary>
    public static double RightTurnFlow(double peds, int seed = 7)
    {
        var (_, it, _, net) = Cross(1, ControleIntersecao.Semaforo);
        var nd = net.Nodes.First(n => n.Key == it!.Id);
        var macro = TrafficAnalysis.Run(net, Options(peds: peds, seed: seed));
        int? li0 = null;
        Impose(macro, r =>
        {
            var inLink = r.First(l => net.Links[l].To == nd.Index);
            if (net.Links[inLink].Road.Id != nd.MainRoadId || !net.Links[inLink].Forward) return 0;
            if (TurnAt(net, r, nd.Index) != Giro.Direita) return 0;
            li0 = inLink;
            return 900;
        });
        var sim = TrafficSimulation.Run(macro);
        var nr = macro.Nodes[nd.Index];
        var g = nr.Phases.Where(p => p.Links.Contains(li0!.Value)).Sum(p => p.Green);
        return sim.Approaches[li0!.Value].Vehicles / (macro.Options.SimSeconds / 3600.0) * nr.Cycle / Math.Max(1, g);
    }

    // ------------------------------------------------------------------ trocas de faixa antes das conversões

    /// <summary>Trocas obrigatórias de faixa antes das conversões: distância mediana à retenção (m) e fração feita a menos de 30 m.</summary>
    public static (double Median, double Late, int N) LaneChanges(int seed = 7)
    {
        var (_, it, _, net) = Cross(1, ControleIntersecao.Semaforo);
        var nd = net.Nodes.First(n => n.Key == it!.Id);
        var macro = TrafficAnalysis.Run(net, Options(seed: seed));
        var sim = TrafficSimulation.Run(macro);
        var d = sim.MandatoryChanges.OrderBy(x => x).ToList();
        return d.Count == 0 ? (double.NaN, double.NaN, 0) : (d[d.Count / 2], d.Count(x => x < 30) / (double)d.Count, d.Count);
    }

    // ------------------------------------------------------------------ fila que transborda (spillback)

    /// <summary>
    /// Dois semáforos a 90 m na mesma via, o de jusante com pouco verde para ela: vazão de quem atravessa o de montante (veh/h),
    /// veículos parados dentro do cruzamento de montante (média por segundo) e a maior fila entre eles (m).
    /// </summary>
    public static (double Upstream, double BlockingInBox, double Queue, int Overlaps) Spillback(int seed = 7)
    {
        var m = new AA1SolutionReproTests.Model();
        var a = m.Road(1, new Vec2(-400, 0), new Vec2(400, 0));
        var b = m.Road(0, new Vec2(0, -250), new Vec2(0, 250));
        var c = m.Road(0, new Vec2(90, -250), new Vec2(90, 250));
        var i1 = m.Intersection(Vec2.Zero, ControleIntersecao.Semaforo, a, b);
        var i2 = m.Intersection(new Vec2(90, 0), ControleIntersecao.Semaforo, a, c);
        var net = m.Network();
        var n1 = net.Nodes.First(n => n.Key == i1.Id);
        var n2 = net.Nodes.First(n => n.Key == i2.Id);
        var opt = Options(seed: seed);
        // Jusante: verde curto para a via principal (ciclo 90 s, 15 s para ela).
        opt.Nodes.Add(new NodeOverride { Node = i2.Id, Cycle = 90, Greens = new List<double> { 15, 67 } });
        var macro = TrafficAnalysis.Run(net, opt);
        var mid = net.Links.First(l => l.From == n1.Index && l.To == n2.Index);
        Impose(macro, r => r.Contains(mid.Index) && r.IndexOf(mid.Index) + 1 < r.Count && TrafficAnalysis.TurnOf(net, mid.Index, r[r.IndexOf(mid.Index) + 1]) == Giro.Frente
                              && TurnAt(net, r, n1.Index) == Giro.Frente ? 1500 : 0);
        var sim = TrafficSimulation.Run(macro);
        var up = net.Links.First(l => l.To == n1.Index && l.Road.Id == a.Id && l.Forward);
        var inBox = sim.Frames.Where(f => f.Time >= 0).Average(f =>
        {
            var k = 0;
            for (int i = 0; i < f.Count; i++)
            {
                var p = new Vec2(f.Data[i * SimFrame.Stride], f.Data[i * SimFrame.Stride + 1]);
                if (p.DistanceTo(n1.Pos) < 6 && f.Data[i * SimFrame.Stride + 4] < 0.3) k++;
            }
            return (double)k;
        });
        return (sim.Approaches[up.Index].Vehicles / (opt.SimSeconds / 3600.0), inBox, sim.Approaches[mid.Index].MaxQueue, TrafficSimulation.CountOverlaps(sim));
    }
}
