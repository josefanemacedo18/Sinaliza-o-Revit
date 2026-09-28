using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Traffic;

public enum NivelDemanda { Baixa, Media, Pico, Saturada }

/// <summary>Opções da análise e da simulação.</summary>
public sealed class TrafficOptions
{
    public NivelDemanda Demand { get; set; } = NivelDemanda.Pico;
    /// <summary>Crescimento da demanda (horizonte de projeto), 0 = hoje; 0,3 = +30 %.</summary>
    public double Growth { get; set; }
    public double HeavyVehicles { get; set; } = 0.08;
    public double Buses { get; set; } = 0.02;
    public double PeakHourFactor { get; set; } = 0.92;
    /// <summary>Pedestres por hora em cada travessia.</summary>
    public double PedestriansPerHour { get; set; } = 150;
    public bool OptimizeSignals { get; set; } = true;
    public double FixedCycle { get; set; } = 90;
    public int SimSeconds { get; set; } = 900;
    public int WarmupSeconds { get; set; } = 180;
    public int Seed { get; set; } = 7;
    /// <summary>Volume (veh/h) que entra por uma extremidade, por nó (sobrepõe a estimativa).</summary>
    public Dictionary<int, double> ZoneVolumes { get; set; } = new();

    public double DemandFactor => (Demand switch
    {
        NivelDemanda.Baixa => 0.40,
        NivelDemanda.Media => 0.70,
        NivelDemanda.Pico => 1.00,
        _ => 1.40,
    }) * (1 + Growth);

    public string DemandLabel => Demand switch
    {
        NivelDemanda.Baixa => "baixa (entrepico)",
        NivelDemanda.Media => "média",
        NivelDemanda.Pico => "hora de pico",
        _ => "saturada (demanda acima da capacidade)",
    } + (Growth > 0 ? $" +{Growth * 100:0}% de crescimento" : "");
}

public enum Giro { Direita, Frente, Esquerda, Retorno }

/// <summary>Resultado de uma aproximação (trecho que chega ao nó).</summary>
public sealed class ApproachResult
{
    public int Link { get; init; }
    public string Name { get; init; } = "";
    public double Volume { get; set; }       // veh/h
    public double Capacity { get; set; }     // veh/h
    public double X => Capacity > 0 ? Volume / Capacity : 0;
    public double Delay { get; set; }        // s/veh
    public string LOS { get; set; } = "A";
    public double Queue95 { get; set; }      // m
    public double Green { get; set; }        // s (semáforo)
    public bool Major { get; set; }
    public bool ProtectedLeft { get; set; }  // semáforo com fase exclusiva de conversão à esquerda
    public double LeftVolume => Movements.GetValueOrDefault(Giro.Esquerda) + Movements.GetValueOrDefault(Giro.Retorno);
    public Dictionary<Giro, double> Movements { get; } = new();
}

/// <summary>Fase do plano semafórico: aproximações com verde; "só esquerda" = conversão à esquerda protegida.</summary>
public sealed class SignalPhase
{
    public string Name { get; init; } = "";
    public double Green { get; set; }
    public List<int> Links { get; init; } = new();
    public bool LeftOnly { get; init; }
    public bool Allows(Giro g) => !LeftOnly || g is Giro.Esquerda or Giro.Retorno;
}

/// <summary>Resultado de um nó.</summary>
public sealed class NodeResult
{
    public required TrafficNode Node { get; init; }
    public List<ApproachResult> Approaches { get; } = new();
    public double Volume => Approaches.Sum(a => a.Volume);
    public double Delay { get; set; }
    public string LOS { get; set; } = "A";
    public double Cycle { get; set; }
    public List<SignalPhase> Phases { get; } = new();
    public double MaxX => Approaches.Count == 0 ? 0 : Approaches.Max(a => a.X);
    public List<string> Notes { get; } = new();
}

/// <summary>Resultado de um trecho.</summary>
public sealed class LinkResult
{
    public required TrafficLink Link { get; init; }
    public double Volume { get; set; }
    public double X => Link.Capacity > 0 ? Volume / Link.Capacity : 0;
    public double TravelTime { get; set; }   // s (trecho + atraso no nó de jusante)
    public double Speed { get; set; }        // km/h média
    public string LOS { get; set; } = "A";
    public double Density { get; set; }      // veh/km/faixa
}

/// <summary>Resultado da análise macroscópica da rede.</summary>
public sealed class TrafficResult
{
    public required TrafficNetwork Network { get; init; }
    public required TrafficOptions Options { get; init; }
    public Dictionary<int, LinkResult> Links { get; } = new();
    public Dictionary<int, NodeResult> Nodes { get; } = new();
    /// <summary>Volumes de conversão: (nó, trecho de entrada, trecho de saída) → veh/h.</summary>
    public Dictionary<(int Node, int In, int Out), double> Turns { get; } = new();
    /// <summary>Matriz origem-destino entre as zonas (nós de extremidade e balões), veh/h.</summary>
    public Dictionary<(int O, int D), double> OD { get; } = new();
    /// <summary>Rotas de cada par OD (uma por incremento da alocação, com o peso do incremento).</summary>
    public Dictionary<(int O, int D), List<(double Weight, List<int> Links)>> Routes { get; } = new();
    public double TotalDemand => OD.Values.Sum();
    public double Unserved { get; set; }
    public double VKT { get; set; }
    public double VHT { get; set; }
    public double TotalDelayH { get; set; }
    public double CO2kg { get; set; }
    public double AvgSpeed => VHT > 0 ? VKT / VHT : 0;
    public List<TrafficDiagnostic> Diagnostics { get; } = new();

    public static string LosColorName(string los) => los switch
    {
        "A" => "verde-escuro", "B" => "verde", "C" => "amarelo", "D" => "laranja", "E" => "vermelho", _ => "vinho",
    };
}

/// <summary>
/// Análise de tráfego da rede (macroscópica): demanda nas extremidades, matriz OD gravitacional, alocação incremental com
/// restrição de capacidade (BPR), conversões nos nós e capacidade/atraso/nível de serviço pelos métodos do HCM (semáforo
/// com plano de Webster, PARE/Dê a preferência por aceitação de brechas, rotatórias HCM 6, trechos por velocidade/densidade).
/// </summary>
public static class TrafficAnalysis
{
    public static TrafficResult Run(TrafficNetwork net, TrafficOptions opt)
    {
        var res = new TrafficResult { Network = net, Options = opt };
        foreach (var l in net.Links) res.Links[l.Index] = new LinkResult { Link = l };
        if (net.Links.Count == 0) return res;
        BuildOD(net, opt, res);
        Assign(net, opt, res);
        foreach (var nd in net.Nodes) res.Nodes[nd.Index] = AnalyzeNode(net, opt, res, nd);
        foreach (var lr in res.Links.Values) AnalyzeLink(res, lr);
        Totals(res);
        TrafficDiagnostics.Run(res);
        return res;
    }

    // ------------------------------------------------------------------ demanda

    /// <summary>Volume típico de hora de pico por faixa que entra/sai da área pela via (veh/h/faixa).</summary>
    public static double PeakPerLane(HierarquiaViaria? h) => h switch
    {
        HierarquiaViaria.TransitoRapido => 1300,
        HierarquiaViaria.Rodovia => 1000,
        HierarquiaViaria.Arterial => 650,
        HierarquiaViaria.Coletora => 450,
        HierarquiaViaria.Estrada => 350,
        HierarquiaViaria.Local => 180,
        _ => 350,
    };

    /// <summary>Produções e atrações das zonas e matriz OD gravitacional (decaimento pelo tempo de viagem livre).</summary>
    private static void BuildOD(TrafficNetwork net, TrafficOptions opt, TrafficResult res)
    {
        var zones = net.Nodes.Where(n => n.IsZone).ToList();
        if (zones.Count < 2) return;
        var prod = new Dictionary<int, double>();
        var attr = new Dictionary<int, double>();
        foreach (var z in zones)
        {
            double p, a;
            if (z.Kind == TipoNo.CulDeSac)
            {
                p = 60 * opt.DemandFactor;
                a = p;
            }
            else
            {
                p = z.Out.Sum(i => net.Links[i].Lanes * PeakPerLane(net.Links[i].Road.Hierarchy)) * opt.DemandFactor;
                a = z.In.Sum(i => net.Links[i].Lanes * PeakPerLane(net.Links[i].Road.Hierarchy)) * opt.DemandFactor;
            }
            if (opt.ZoneVolumes.TryGetValue(z.Index, out var ov)) p = ov;
            prod[z.Index] = p;
            attr[z.Index] = a;
        }
        // Tempos livres entre zonas (para o decaimento).
        var free = new Dictionary<(int, int), double>();
        foreach (var o in zones)
        {
            var (dist, _) = Dijkstra(net, o.Index, l => l.Length / l.FreeSpeed, (_, _, _) => 0);
            foreach (var d in zones)
            {
                if (d.Index == o.Index) continue;
                var best = double.PositiveInfinity;
                foreach (var li in d.In) if (dist.TryGetValue(li, out var t)) best = Math.Min(best, t);
                free[(o.Index, d.Index)] = best;
            }
        }
        foreach (var o in zones)
        {
            var weights = zones.Where(d => d.Index != o.Index && !double.IsInfinity(free[(o.Index, d.Index)]))
                .Select(d => (d.Index, W: attr[d.Index] * Math.Exp(-free[(o.Index, d.Index)] / 900.0))).ToList();
            var sum = weights.Sum(w => w.W);
            if (sum <= 0 || prod[o.Index] <= 0)
            {
                if (prod[o.Index] > 0) res.Unserved += prod[o.Index];
                continue;
            }
            foreach (var (d, w) in weights) res.OD[(o.Index, d)] = prod[o.Index] * w / sum;
        }
    }

    // ------------------------------------------------------------------ alocação

    public static Giro TurnOf(TrafficNetwork net, int inLink, int outLink)
    {
        var a = net.Links[inLink];
        var b = net.Links[outLink];
        var din = a.Path.TangentAt(a.Path.Length);
        var dout = b.Path.TangentAt(0);
        var ang = Math.Atan2(din.Cross(dout), din.Dot(dout)) * 180 / Math.PI;
        if (Math.Abs(ang) > 150 && ReferenceEquals(a.Road, b.Road)) return Giro.Retorno;
        if (Math.Abs(ang) > 155) return Giro.Retorno;
        return ang < -30 ? Giro.Direita : ang > 30 ? Giro.Esquerda : Giro.Frente;
    }

    /// <summary>Conversão permitida no nó (retorno só em rotatórias, balões e emendas).</summary>
    public static bool Allowed(TrafficNetwork net, TrafficNode nd, int inLink, int outLink)
    {
        if (inLink == outLink) return false;
        var t = TurnOf(net, inLink, outLink);
        if (t == Giro.Retorno) return nd.Kind is TipoNo.Rotatoria or TipoNo.CulDeSac;
        return true;
    }

    private static double ControlPenalty(TrafficNode nd, Giro g) => nd.Control switch
    {
        ControleNo.Semaforo => 18,
        ControleNo.Pare => 9,
        ControleNo.DePreferencia => 6,
        ControleNo.Rotatoria => 7,
        ControleNo.PreferenciaDireita => 8,
        _ => 0,
    } + (g == Giro.Esquerda ? 4 : g == Giro.Retorno ? 10 : 0);

    /// <summary>Menor custo até o fim de cada trecho a partir de um nó de origem (zonas só como origem/destino).</summary>
    public static (Dictionary<int, double> Dist, Dictionary<int, int> Prev) Dijkstra(TrafficNetwork net, int origin, Func<TrafficLink, double> cost,
        Func<TrafficNode, int, int, double> turnCost)
    {
        var dist = new Dictionary<int, double>();
        var prev = new Dictionary<int, int>();
        var pq = new PriorityQueue<int, double>();
        foreach (var li in net.Nodes[origin].Out)
        {
            var c = cost(net.Links[li]);
            dist[li] = c;
            pq.Enqueue(li, c);
        }
        while (pq.TryDequeue(out var a, out var da))
        {
            if (da > dist[a] + 1e-9) continue;
            var nd = net.Nodes[net.Links[a].To];
            if (nd.IsZone) continue;                        // não atravessa zonas
            foreach (var b in nd.Out)
            {
                if (!Allowed(net, nd, a, b)) continue;
                var nb = da + turnCost(nd, a, b) + cost(net.Links[b]);
                if (!dist.TryGetValue(b, out var db) || nb < db - 1e-9)
                {
                    dist[b] = nb;
                    prev[b] = a;
                    pq.Enqueue(b, nb);
                }
            }
        }
        return (dist, prev);
    }

    /// <summary>
    /// Alocação com restrição de capacidade pelo método das médias sucessivas (MSA): a cada iteração todas as viagens seguem
    /// o menor custo (tempo no trecho pela curva BPR + atraso de cada aproximação calculado pelo HCM com a carga atual) e o
    /// carregamento é a média das iterações – o tráfego evita os nós saturados e se distribui entre rotas equivalentes.
    /// </summary>
    private static void Assign(TrafficNetwork net, TrafficOptions opt, TrafficResult res)
    {
        var vol = net.Links.Select(_ => 0.0).ToArray();
        var turns = new Dictionary<(int, int, int), double>();
        var appDelay = new Dictionary<int, double>();
        double Cost(TrafficLink l)
        {
            var x = l.Capacity > 0 ? vol[l.Index] / l.Capacity : 0;
            var slow = l.SlowPoints.Sum(sp => Math.Max(0, 30 / Math.Max(1, sp.Speed) - 30 / l.FreeSpeed) * 0.5);
            return l.Length / l.FreeSpeed * (1 + 0.15 * Math.Pow(Math.Min(x, 2.5), 4)) + slow;
        }
        double TurnC(TrafficNode nd, int a, int b)
        {
            var g = TurnOf(net, a, b);
            var d = appDelay.TryGetValue(a, out var dd) ? dd : ControlPenalty(nd, g) * 0.5;
            return d + (g == Giro.Esquerda ? 3 : g == Giro.Retorno ? 8 : 0);
        }
        var byOrigin = res.OD.GroupBy(k => k.Key.O).ToList();
        const int iterations = 10;
        for (int k = 1; k <= iterations; k++)
        {
            var lambda = 1.0 / k;
            var aon = new double[vol.Length];
            var aonTurns = new Dictionary<(int, int, int), double>();
            var aonRoutes = new List<((int, int) Od, List<int> Links)>();
            foreach (var g in byOrigin)
            {
                var (dist, prev) = Dijkstra(net, g.Key, Cost, TurnC);
                foreach (var ((_, d), v) in g)
                {
                    int? last = null;
                    var best = double.PositiveInfinity;
                    foreach (var li in net.Nodes[d].In)
                        if (dist.TryGetValue(li, out var t) && t < best) { best = t; last = li; }
                    if (last == null) { if (k == 1) res.Unserved += v; continue; }
                    var cur = last.Value;
                    var route = new List<int>();
                    while (true)
                    {
                        aon[cur] += v;
                        route.Add(cur);
                        if (!prev.TryGetValue(cur, out var p)) break;
                        var key = (net.Links[p].To, p, cur);
                        aonTurns[key] = aonTurns.GetValueOrDefault(key) + v;
                        cur = p;
                    }
                    route.Reverse();
                    aonRoutes.Add(((g.Key, d), route));
                }
            }
            for (int i = 0; i < vol.Length; i++) vol[i] = (1 - lambda) * vol[i] + lambda * aon[i];
            foreach (var key in turns.Keys.ToList()) turns[key] *= 1 - lambda;
            foreach (var (key, v) in aonTurns) turns[key] = turns.GetValueOrDefault(key) + lambda * v;
            foreach (var key in res.Routes.Keys.ToList())
                res.Routes[key] = res.Routes[key].Select(x => (x.Weight * (1 - lambda), x.Links)).ToList();
            foreach (var (od, route) in aonRoutes)
            {
                if (!res.Routes.TryGetValue(od, out var rl)) res.Routes[od] = rl = new List<(double, List<int>)>();
                var same = rl.FindIndex(x => x.Links.SequenceEqual(route));
                if (same >= 0) rl[same] = (rl[same].Weight + lambda, rl[same].Links);
                else rl.Add((lambda, route));
            }
            // Atrasos dos nós com a carga atual (para a próxima iteração).
            foreach (var l in net.Links) res.Links[l.Index].Volume = vol[l.Index];
            res.Turns.Clear();
            foreach (var (key, v) in turns) res.Turns[key] = v;
            appDelay.Clear();
            foreach (var nd in net.Nodes)
                foreach (var a in AnalyzeNode(net, opt, res, nd).Approaches)
                    appDelay[a.Link] = Math.Min(a.Delay, 240);
        }
        // Rotas com peso desprezível saem.
        foreach (var key in res.Routes.Keys.ToList()) res.Routes[key] = res.Routes[key].Where(x => x.Weight > 0.02).ToList();
    }

    // ------------------------------------------------------------------ nós

    private static string LosUnsignalized(double d, double x) => x > 1 ? "F" : d <= 10 ? "A" : d <= 15 ? "B" : d <= 25 ? "C" : d <= 35 ? "D" : d <= 50 ? "E" : "F";
    private static string LosSignal(double d, double x) => x > 1 ? "F" : d <= 10 ? "A" : d <= 20 ? "B" : d <= 35 ? "C" : d <= 55 ? "D" : d <= 80 ? "E" : "F";

    private static double Pcu(TrafficOptions o) => 1 + o.HeavyVehicles * (2.0 - 1) + o.Buses * (2.0 - 1);

    /// <summary>Atraso de controle HCM (brechas / rotatória), s/veh.</summary>
    private static double GapDelay(double v, double c, double T = 0.25, double extra = 5)
    {
        if (c <= 1) return 300;
        var x = v / c;
        var d = 3600 / c + 900 * T * ((x - 1) + Math.Sqrt((x - 1) * (x - 1) + 3600 / c * x / (450 * T))) + extra * Math.Min(1, x);
        return Math.Min(300, d);
    }

    /// <summary>Capacidade potencial por aceitação de brechas (HCM), veh/h.</summary>
    private static double GapCapacity(double vc, double tc, double tf)
    {
        if (vc < 1) return 3600 / tf;
        return vc * Math.Exp(-vc * tc / 3600) / (1 - Math.Exp(-vc * tf / 3600));
    }

    private static NodeResult AnalyzeNode(TrafficNetwork net, TrafficOptions opt, TrafficResult res, TrafficNode nd)
    {
        var nr = new NodeResult { Node = nd };
        foreach (var li in nd.In)
        {
            var a = new ApproachResult { Link = li, Name = net.Links[li].Name, Volume = res.Links[li].Volume };
            foreach (var ((n, i, o), v) in res.Turns.Where(t => t.Key.Node == nd.Index && t.Key.In == li))
            {
                var g = TurnOf(net, i, o);
                a.Movements[g] = a.Movements.GetValueOrDefault(g) + v;
            }
            nr.Approaches.Add(a);
        }
        if (nr.Approaches.Count == 0 || nd.IsZone || nd.Kind == TipoNo.Continuacao)
        {
            foreach (var a in nr.Approaches) { a.Capacity = net.Links[a.Link].Capacity; a.LOS = "A"; }
            return nr;
        }
        switch (nd.Control)
        {
            case ControleNo.Semaforo: Signal(net, opt, res, nd, nr); break;
            case ControleNo.Rotatoria: Roundabout(net, opt, res, nd, nr); break;
            default: Unsignalized(net, opt, res, nd, nr); break;
        }
        foreach (var a in nr.Approaches.Where(a => a.Volume < 1)) { a.Delay = 0; a.Queue95 = 0; a.LOS = "-"; }
        var tot = nr.Approaches.Sum(a => a.Volume);
        nr.Delay = tot > 0 ? nr.Approaches.Sum(a => a.Volume * a.Delay) / tot : 0;
        var loaded = nr.Approaches.Where(a => a.Volume >= 1).ToList();
        nr.LOS = loaded.Count == 0 ? "-" : nd.Control == ControleNo.Semaforo ? LosSignal(nr.Delay, loaded.Max(a => a.X))
            : LosUnsignalized(nr.Delay, loaded.Where(a => !a.Major).Select(a => a.X).DefaultIfEmpty(0).Max());
        return nr;
    }

    /// <summary>
    /// Semáforo: uma fase por via e, quando o produto conversão à esquerda × fluxo oposto passa do critério usual
    /// (Manual Brasileiro de Sinalização – Vol. V / FHWA: 50 000, 90 000 ou 110 000 para 1, 2 ou 3 faixas opostas),
    /// conversão protegida + permitida: verde antecipado para a aproximação (se só ela precisa) ou fase dupla de esquerdas.
    /// Ciclo ótimo de Webster, verdes proporcionais às razões de fluxo críticas, atraso HCM (d1 + d2) e filas.
    /// </summary>
    private static void Signal(TrafficNetwork net, TrafficOptions opt, TrafficResult res, TrafficNode nd, NodeResult nr)
    {
        var pcu = Pcu(opt);
        var groups = nr.Approaches.GroupBy(a => net.Links[a.Link].Road.Id).Select(g => g.ToList()).ToList();
        Vec2 Dir(int link) { var l = net.Links[link]; return l.Path.TangentAt(l.Length); }
        ApproachResult? Opposing(List<ApproachResult> grp, ApproachResult a) => grp.FirstOrDefault(b => b != a && Dir(a.Link).Dot(Dir(b.Link)) < -0.7);
        double V(double veh) => veh * pcu / opt.PeakHourFactor;
        // Capacidade da conversão permitida (brechas no fluxo oposto durante o verde + ~2 veículos no entreverdes), veh/h.
        double Permitted(double vo, double cycle) => 7200 / cycle + (vo < 1 ? 1800 : vo * Math.Exp(-vo * 4.5 / 3600) / (1 - Math.Exp(-vo * 2.5 / 3600))) * 0.30;

        var phases = new List<(string Name, List<ApproachResult> Apps, bool LeftOnly, double Y)>();
        var satTR = new Dictionary<int, double>();
        var satL = new Dictionary<int, double>();
        var perm = new Dictionary<int, double>();
        foreach (var grp in groups)
        {
            var road = net.Links[grp[0].Link].Road.Name;
            foreach (var a in grp)
            {
                var o = Opposing(grp, a);
                var vo = o == null ? 0 : o.Volume - o.LeftVolume;
                var no = o == null ? 1 : Math.Max(1, net.Links[o.Link].Lanes);
                var limit = no == 1 ? 50000 : no == 2 ? 90000 : 110000;
                a.ProtectedLeft = o != null && a.LeftVolume >= 90 && a.LeftVolume * vo >= limit;
                perm[a.Link] = o == null ? 1e9 : Permitted(V(vo), 90);
                satL[a.Link] = net.Links[a.Link].SaturationPerLane * 0.95;
            }
            var prot = grp.Where(a => a.ProtectedLeft).ToList();
            if (prot.Count > 0)
            {
                // Parte da esquerda que a fase permitida não atende vai para a fase protegida.
                var yl = prot.Max(a => Math.Max(0.02, V(a.LeftVolume) - perm[a.Link]) / satL[a.Link]);
                phases.Add(prot.Count == 1 ? ($"verde antecipado – {prot[0].Name}", prot, false, yl) : ($"conversões à esquerda – {road}", prot, true, yl));
            }
            var y = 0.0;
            foreach (var a in grp)
            {
                var lk = net.Links[a.Link];
                var tr = a.Volume;
                var pl = tr > 0 ? a.LeftVolume / tr : 0;
                var pr = tr > 0 ? a.Movements.GetValueOrDefault(Giro.Direita) / tr : 0;
                var sTR = lk.SaturationPerLane * lk.Lanes * (1 - (a.ProtectedLeft ? 0.15 : 0.35) * pl) * (1 - 0.15 * pr);
                satTR[a.Link] = sTR;
                var vMain = V(tr) - (a.ProtectedLeft ? Math.Max(0, V(a.LeftVolume) - perm[a.Link]) : 0);
                y = Math.Max(y, sTR > 0 ? vMain / sTR : 1);
            }
            phases.Add((road, grp, false, y));
        }
        var lost = 4.0 * Math.Max(2, phases.Count);
        var Y = phases.Sum(p => p.Y);
        double C;
        if (opt.OptimizeSignals)
        {
            C = Y < 0.92 ? (1.5 * lost + 5) / (1 - Y) : 150;
            C = Math.Clamp(C, 45, 150);
        }
        else C = Math.Clamp(opt.FixedCycle, 30, 240);
        // Verde mínimo de pedestres: atravessam a outra via durante a fase principal desta.
        var greens = new List<double>();
        for (int k = 0; k < phases.Count; k++)
        {
            var share = Y > 0 ? phases[k].Y / Y : 1.0 / phases.Count;
            var main = !phases[k].LeftOnly && !phases[k].Name.StartsWith("verde antecipado");
            var g = Math.Max(main ? 7 : 5, (C - lost) * share);
            if (nd.Crosswalks && main)
            {
                var mine = phases[k].Apps.Select(a => net.Links[a.Link].Road.Id).ToHashSet();
                var crossW = nr.Approaches.Where(a => !mine.Contains(net.Links[a.Link].Road.Id)).Select(x => net.Links[x.Link].Road.CarriageWidth).DefaultIfEmpty(7).Max();
                var gp = 7 + crossW / 1.2;
                if (g < gp) { nr.Notes.Add($"Fase {k + 1}: verde aumentado para {gp:0} s (travessia de {crossW:0.0} m a 1,2 m/s)."); g = gp; }
            }
            greens.Add(g);
        }
        var sumG = greens.Sum();
        if (sumG + lost > C) C = sumG + lost;
        nr.Cycle = C;
        for (int k = 0; k < phases.Count; k++)
            nr.Phases.Add(new SignalPhase { Name = $"Fase {k + 1} – {phases[k].Name}", Green = greens[k], Links = phases[k].Apps.Select(a => a.Link).ToList(), LeftOnly = phases[k].LeftOnly });

        (double Delay, double X, double Cap) Hcm(double v, double c, double g)
        {
            var x = c > 0 ? v / c : 2;
            var gC = Math.Min(0.95, g / C);
            var d1 = 0.5 * C * (1 - gC) * (1 - gC) / Math.Max(0.05, 1 - Math.Min(1, x) * gC);
            const double T = 0.25;
            var d2 = c > 0 ? 900 * T * ((x - 1) + Math.Sqrt((x - 1) * (x - 1) + 8 * 0.5 * x / (c * T))) : 300;
            return (Math.Min(300, d1 + Math.Max(0, d2)), x, c);
        }
        foreach (var a in nr.Approaches)
        {
            var kMain = phases.FindIndex(p => !p.LeftOnly && !p.Name.StartsWith("verde antecipado") && p.Apps.Contains(a));
            var kProt = phases.FindIndex(p => (p.LeftOnly || p.Name.StartsWith("verde antecipado")) && p.Apps.Contains(a));
            var lead = kProt >= 0 && !phases[kProt].LeftOnly;
            var g = kMain >= 0 ? greens[kMain] : 7;
            double delay, x, cap;
            if (kProt < 0)
            {
                var (dd, xx, cc) = Hcm(V(a.Volume), satTR[a.Link] * g / C, g);
                (delay, x, cap) = (dd, xx, cc);
            }
            else
            {
                // Esquerda: fase protegida + permitida; demais movimentos na fase principal (e no verde antecipado).
                var gp = greens[kProt];
                var vL = V(a.LeftVolume);
                var cL = satL[a.Link] * gp / C + Math.Min(perm[a.Link], vL);
                var vTR = V(a.Volume) - vL;
                var gTR = g + (lead ? gp : 0);
                var cTR = satTR[a.Link] * gTR / C * Math.Max(0.1, 1 - a.LeftVolume / Math.Max(1, a.Volume));
                var (dL, xL, _) = Hcm(vL, cL, gp + g * 0.3);
                var (dT, xT, _) = Hcm(vTR, cTR, gTR);
                delay = (vL * dL + vTR * dT) / Math.Max(1e-6, vL + vTR);
                x = Math.Max(xL, xT);
                cap = cL + cTR;
                g = gTR;
            }
            a.Green = g;
            a.Capacity = cap / pcu;
            a.Delay = delay;
            a.LOS = LosSignal(a.Delay, x);
            var lanes = Math.Max(1, net.Links[a.Link].Lanes);
            var v = V(a.Volume);
            var arrivals = v / lanes * (C - g) / 3600;
            var overflow = Math.Max(0, x - 1) * cap / lanes * 0.25 / 2;
            a.Queue95 = (arrivals * 1.65 + overflow) * 7.0;
        }
    }

    /// <summary>PARE / Dê a preferência (via principal com prioridade) e cruzamentos sem controle (preferência de quem vem pela direita, CTB art. 29).</summary>
    private static void Unsignalized(TrafficNetwork net, TrafficOptions opt, TrafficResult res, TrafficNode nd, NodeResult nr)
    {
        var pcu = Pcu(opt);
        var roads = nr.Approaches.Select(a => net.Links[a.Link].Road).Distinct().ToList();
        TrafficRoad? major = null;
        if (nd.Control is ControleNo.Pare or ControleNo.DePreferencia)
        {
            major = roads.FirstOrDefault(r => r.Id == nd.MainRoadId)
                    ?? roads.OrderByDescending(r => Hierarquia.Rank(r.Hierarchy)).ThenByDescending(r => nr.Approaches.Where(a => ReferenceEquals(net.Links[a.Link].Road, r)).Sum(a => a.Volume)).First();
        }
        var multilane = nr.Approaches.Any(a => net.Links[a.Link].Lanes >= 2);
        Vec2 Dir(ApproachResult a) { var l = net.Links[a.Link]; return l.Path.TangentAt(l.Path.Length); }
        foreach (var a in nr.Approaches)
            a.Major = major != null && ReferenceEquals(net.Links[a.Link].Road, major);
        var vMaj = nr.Approaches.Where(a => a.Major).Sum(a => a.Volume * pcu);
        // Conversão à esquerda da principal (cede ao sentido oposto): probabilidade de fila vazia afeta as secundárias.
        var p0 = 1.0;
        foreach (var a in nr.Approaches.Where(a => a.Major))
        {
            var opposing = nr.Approaches.Where(b => b.Major && b != a).Sum(b => b.Volume * pcu);
            var vl = a.Movements.GetValueOrDefault(Giro.Esquerda) * pcu;
            var cl = GapCapacity(opposing, 4.1, 2.2);
            p0 *= Math.Max(0, 1 - vl / Math.Max(1, cl));
            a.Capacity = net.Links[a.Link].Capacity;
            a.Delay = vl > 0 ? Math.Min(60, GapDelay(vl, cl) * vl / Math.Max(1, a.Volume * pcu)) + 0.5 : 0.5;
            a.LOS = LosUnsignalized(a.Delay, a.X);
        }
        foreach (var a in nr.Approaches.Where(a => !a.Major))
        {
            double vc(Giro g)
            {
                if (major != null)
                {
                    var oppMinor = nr.Approaches.Where(b => !b.Major && b != a).Sum(b => b.Volume * pcu);
                    return g switch { Giro.Direita => 0.5 * vMaj / 2, Giro.Frente => vMaj, _ => vMaj + 0.5 * oppMinor };
                }
                // Sem controle: cede a quem vem pela direita (aproximação girada 90° no sentido horário).
                var d = Dir(a);
                var right = nr.Approaches.Where(b => b != a && d.Cross(Dir(b)) > 0.3 * Dir(b).Length).Sum(b => b.Volume * pcu);
                var opposite = nr.Approaches.Where(b => b != a && d.Dot(Dir(b)) < -0.5).Sum(b => b.Volume * pcu);
                return g switch { Giro.Direita => 0.5 * right, Giro.Frente => right, _ => right + 0.6 * opposite };
            }
            var mult = multilane ? 1 : 0;
            var yieldAdj = nd.Control == ControleNo.DePreferencia ? -0.2 : 0;
            double cap(Giro g) => g switch
            {
                Giro.Direita => GapCapacity(vc(g), 6.2 + 0.7 * mult + yieldAdj, 3.3),
                Giro.Frente => GapCapacity(vc(g), 6.5 + 1.0 * mult + yieldAdj, 4.0) * p0,
                _ => GapCapacity(vc(g), 7.1 + 0.4 * mult + yieldAdj, 3.5) * p0,
            };
            var mv = a.Movements.Count > 0 ? a.Movements : new Dictionary<Giro, double> { [Giro.Frente] = a.Volume };
            var sumV = mv.Values.Sum() * pcu;
            var sumVc = mv.Sum(m => m.Value * pcu / Math.Max(1, cap(m.Key)));
            var cSh = sumVc > 0 ? sumV / sumVc : cap(Giro.Frente);
            cSh *= Math.Min(2, net.Links[a.Link].Lanes) > 1 ? 1.6 : 1.0;
            a.Capacity = cSh / pcu;
            var v = a.Volume * pcu / opt.PeakHourFactor;
            a.Delay = GapDelay(v, cSh);
            a.LOS = LosUnsignalized(a.Delay, v / Math.Max(1, cSh));
            a.Queue95 = QueueGap(v, cSh) * 7.0;
        }
    }

    /// <summary>Fila 95% em aproximação sem semáforo (HCM), veículos.</summary>
    private static double QueueGap(double v, double c)
    {
        if (c <= 1) return 30;
        var x = v / c;
        var T = 0.25;
        var q = 900 * T * c / 3600 * ((x - 1) + Math.Sqrt((x - 1) * (x - 1) + 3600 / c * x / (150 * T)));
        return Math.Clamp(q, 0, 200);
    }

    /// <summary>Rotatória (HCM 6): fluxo circulante em frente a cada entrada, capacidade exponencial por faixa, atraso e fila.</summary>
    private static void Roundabout(TrafficNetwork net, TrafficOptions opt, TrafficResult res, TrafficNode nd, NodeResult nr)
    {
        var pcu = Pcu(opt);
        double Ang(Vec2 p) => Math.Atan2(p.Y - nd.Pos.Y, p.X - nd.Pos.X);
        // Entradas e saídas em ordem anti-horária (circulação no Brasil).
        var entries = nd.In.Select(i => (Link: i, A: Ang(net.Links[i].Path.Points[^1]))).ToList();
        var exits = nd.Out.Select(o => (Link: o, A: Ang(net.Links[o].Path.Points[0]))).ToList();
        static double Ccw(double from, double to) { var d = to - from; while (d < 0) d += 2 * Math.PI; while (d >= 2 * Math.PI) d -= 2 * Math.PI; return d; }
        foreach (var a in nr.Approaches)
        {
            var ai = entries.First(e => e.Link == a.Link).A;
            var vc = 0.0;
            foreach (var ((n, i, o), v) in res.Turns.Where(t => t.Key.Node == nd.Index && t.Key.In != a.Link))
            {
                var aj = entries.FirstOrDefault(e => e.Link == i).A;
                var ak = exits.FirstOrDefault(e => e.Link == o).A;
                // Passa em frente à entrada i se i está entre a entrada j e a saída k no sentido anti-horário.
                var span = Ccw(aj, ak);
                if (span < 0.05) span = 2 * Math.PI;           // retorno
                if (Ccw(aj, ai) < span) vc += v * pcu;
            }
            var entryLanes = Math.Min(net.Links[a.Link].Lanes, Math.Max(1, nd.RoundaboutLanes));
            double cap;
            if (nd.RoundaboutLanes <= 1) cap = 1380 * Math.Exp(-1.02e-3 * vc) * (entryLanes >= 2 ? 1.6 : 1.0);
            else cap = entryLanes >= 2 ? 1420 * Math.Exp(-0.85e-3 * vc) + 1350 * Math.Exp(-0.92e-3 * vc) : 1420 * Math.Exp(-0.85e-3 * vc);
            if (nd.RoundaboutRadius < 13) cap *= 0.9;          // minirrotatória
            a.Capacity = cap / pcu;
            var v2 = a.Volume * pcu / opt.PeakHourFactor;
            a.Delay = GapDelay(v2, cap);
            a.LOS = LosUnsignalized(a.Delay, v2 / Math.Max(1, cap));
            a.Queue95 = QueueGap(v2, cap) * 7.0;
            nr.Notes.Add($"{a.Name}: fluxo circulante {vc:0} ucp/h.");
        }
    }

    // ------------------------------------------------------------------ trechos e totais

    private static void AnalyzeLink(TrafficResult res, LinkResult lr)
    {
        var l = lr.Link;
        var x = lr.X;
        var t0 = l.Length / l.FreeSpeed;
        var slow = l.SlowPoints.Sum(sp => Math.Max(0, 30 / Math.Max(1, sp.Speed) - 30 / l.FreeSpeed) * 0.5);
        var t = t0 * (1 + 0.15 * Math.Pow(Math.Min(x, 1.6), 4)) + slow;
        var node = res.Nodes.GetValueOrDefault(l.To);
        var d = node?.Approaches.FirstOrDefault(a => a.Link == l.Index)?.Delay ?? 0;
        lr.TravelTime = t + d;
        lr.Speed = l.Length / Math.Max(0.1, lr.TravelTime) * 3.6;
        var pcu = Pcu(res.Options);
        lr.Density = lr.Volume * pcu / Math.Max(1, l.Lanes) / Math.Max(5, lr.Speed);
        var h = l.Road.Hierarchy;
        if (h is HierarquiaViaria.TransitoRapido or HierarquiaViaria.Rodovia)
        {
            var D = lr.Density;
            lr.LOS = x > 1 ? "F" : D <= 7 ? "A" : D <= 11 ? "B" : D <= 16 ? "C" : D <= 22 ? "D" : D <= 28 ? "E" : "F";
        }
        else
        {
            var r = lr.Speed / Math.Max(1, l.FreeSpeed * 3.6);
            lr.LOS = x > 1 ? "F" : r > 0.85 ? "A" : r > 0.67 ? "B" : r > 0.50 ? "C" : r > 0.40 ? "D" : r > 0.30 ? "E" : "F";
        }
    }

    private static void Totals(TrafficResult res)
    {
        double vkt = 0, vht = 0, delay = 0, co2 = 0;
        foreach (var lr in res.Links.Values)
        {
            var km = lr.Link.Length / 1000 * lr.Volume;
            vkt += km;
            vht += lr.Volume * lr.TravelTime / 3600;
            delay += lr.Volume * Math.Max(0, lr.TravelTime - lr.Link.Length / lr.Link.FreeSpeed) / 3600;
            // Emissão de CO₂ (g/km) em função da velocidade média – estimativa (curva típica de automóvel).
            co2 += km * (110 + 2900 / Math.Max(5, lr.Speed)) / 1000;
        }
        res.VKT = vkt;
        res.VHT = vht;
        res.TotalDelayH = delay;
        res.CO2kg = co2;
    }
}
