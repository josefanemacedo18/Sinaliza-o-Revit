using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Veículo da microssimulação.</summary>
internal sealed class SimVehicle
{
    public int Id;
    public int Type;                 // 0 automóvel, 1 caminhão, 2 ônibus
    public double Length = 4.5;
    public double A = 1.3, B = 2.0, T = 1.3;
    public double SpeedFactor = 1.0;
    public List<int> Route = new();
    public int Step;                 // índice do trecho atual na rota
    public int Lane;
    public int NextLane = -1;        // faixa escolhida no próximo trecho
    public double S;                 // posição no trecho (m)
    public double V;                 // m/s
    public bool InNode;
    public List<Vec2>? NodePath;
    public double NodeS, NodeLen;
    public int NodeIndex = -1;
    /// <summary>Movimento no nó (atual, ou o planejado ao se aproximar) e o anterior (a traseira ainda ocupa o nó ao sair).</summary>
    public SimMove? Mov;
    public SimMove? PrevMov;
    public double PrevLen;
    /// <summary>Tempo parado dentro do nó (quem espera muito passa a ter a vez – sem impasse).</summary>
    public double StuckFor;
    public int FromLink = -1, ToLink = -1;
    public double StoppedFor;        // s parado na linha de retenção
    public double Born, LinkEnter, FreeTime;
    public double RingAngle = double.NaN;   // rotatória: ângulo atual no anel
    public int Stops;
    public bool WasStopped;
    // Ônibus: próximo ponto no trecho, fim do embarque, parado na baia.
    public int NextStop;
    public double DwellUntil = -1;
    public bool InBay;
    // Troca de faixa: deslocamento lateral ainda por percorrer (animação) e instante da última troca.
    public double LaneShift;
    public double LastLaneChange = -100;
    /// <summary>Faixa de onde veio na troca em andamento (ainda ocupa parte dela enquanto desloca).</summary>
    public int LaneFrom = -1;
    /// <summary>Troca de faixa obrigatória pendente (sem brecha): quem vem atrás na faixa de destino abre espaço.</summary>
    public int WantsLane = -1;
    public int Link => Route[Step];
}

/// <summary>Movimento num nó: faixa de chegada → faixa de saída.</summary>
internal readonly record struct SimMove(int Node, int From, int FromLane, int To, int ToLane);

/// <summary>Trajeto de um movimento dentro do nó, amostrado a cada 0,5 m, e a velocidade confortável na curva.</summary>
internal sealed class SimPath
{
    public required List<Vec2> Pts { get; init; }
    public required Polyline2 Line { get; init; }
    public double Length { get; init; }
    public double V0 { get; init; }
    public required Vec2[] Samples { get; init; }
    public required double[] St { get; init; }
    public Vec2 Min { get; init; }
    public Vec2 Max { get; init; }
}

/// <summary>
/// Trecho em que dois trajetos passam a menos de ~2 m um do outro: zona de conflito (cruzamento) ou trecho compartilhado
/// (mesma faixa de origem, fusão na mesma faixa de saída, anel da rotatória). Estacas em cada trajeto.
/// </summary>
internal readonly record struct SimRun(double AIn, double AOut, double BIn, double BOut, bool Parallel);

/// <summary>
/// Quadro da animação: posições do CENTRO de cada veículo (x, y), rumo, tipo, velocidade, número e seta (−1 esquerda,
/// +1 direita, 0 desligada), e as fases dos semáforos.
/// </summary>
public sealed class SimFrame
{
    public const int Stride = 7;
    public double Time { get; init; }
    public float[] Data { get; init; } = Array.Empty<float>();   // Stride valores por veículo
    /// <summary>Nó → fase em verde (k ≥ 0) ou entreverdes depois da fase k (−(k + 2)).</summary>
    public Dictionary<int, int> SignalPhase { get; init; } = new();
    /// <summary>Estado de cada veículo (só com <see cref="TrafficSimulation.Debug"/>): trecho/nó, posição, obstáculo.</summary>
    public Dictionary<int, string>? Info { get; init; }
    public int Count => Data.Length / Stride;
}

/// <summary>Estatística de uma aproximação na microssimulação.</summary>
public sealed class SimApproach
{
    public int Link { get; init; }
    public int Vehicles { get; set; }
    public double DelaySum { get; set; }
    public double MeanDelay => Vehicles > 0 ? DelaySum / Vehicles : 0;
    public double MaxQueue { get; set; }      // m
    public int Stops { get; set; }
}

/// <summary>Resultado da microssimulação.</summary>
public sealed class SimResult
{
    public List<SimFrame> Frames { get; } = new();
    public Dictionary<int, SimApproach> Approaches { get; } = new();
    public Dictionary<int, (double Dist, double Time, int N)> LinkSpeed { get; } = new();
    public int Spawned { get; set; }
    public int Completed { get; set; }
    public int InNetwork { get; set; }
    public int Backlog { get; set; }
    public double MeanTravelTime { get; set; }
    public double MeanDelay { get; set; }
    public double StopsPerTrip { get; set; }
    public double Duration { get; set; }
    public int MaxVehicles { get; set; }
    public int LaneChanges { get; set; }
    public int BusStopsServed { get; set; }
    public List<string> Events { get; } = new();

    public double MeanSpeedKmh(int link) => LinkSpeed.TryGetValue(link, out var x) && x.Time > 0 ? x.Dist / x.Time * 3.6 : double.NaN;
}

/// <summary>
/// Microssimulação da rede: veículos com seguimento IDM em cada faixa, rotas da alocação, retenção e aceitação de brechas
/// nos nós (PARE, Dê a preferência, preferência à direita), semáforos com o plano calculado, rotatórias com cessão ao
/// fluxo circulante, pedestres nas travessias do meio da quadra e moderação de tráfego.
/// </summary>
public static class TrafficSimulation
{
    private const double Dt = 0.5;
    public static bool Debug { get; set; }

    public static SimResult Run(TrafficResult macro, CancellationToken cancel = default, IProgress<double>? progress = null)
    {
        var net = macro.Network;
        var opt = macro.Options;
        var res = new SimResult();
        var rnd = new Random(opt.Seed);
        var total = opt.WarmupSeconds + opt.SimSeconds;
        res.Duration = opt.SimSeconds;
        if (net.Links.Count == 0 || macro.Routes.Count == 0) return res;

        // Chegadas (Poisson) de cada par OD; rota sorteada pelos pesos da alocação.
        var arrivals = new List<(double T, (int O, int D) Od)>();
        foreach (var (od, q) in macro.OD)
        {
            if (q <= 0 || !macro.Routes.ContainsKey(od)) continue;
            var t = 0.0;
            while (true)
            {
                t += -Math.Log(1 - rnd.NextDouble()) * 3600 / q;
                if (t >= total) break;
                arrivals.Add((t, od));
            }
        }
        arrivals.Sort((a, b) => a.T.CompareTo(b.T));
        var next = 0;

        var vehicles = new List<SimVehicle>();
        var shifting = new List<SimVehicle>();
        var wanting = new List<SimVehicle>();
        var queues = new Dictionary<int, Queue<SimVehicle>>();         // espera para entrar na rede (por trecho de origem)
        var lanes = new Dictionary<(int Link, int Lane), List<SimVehicle>>();
        List<SimVehicle> LaneList(int link, int lane) => lanes.TryGetValue((link, lane), out var l) ? l : lanes[(link, lane)] = new List<SimVehicle>();
        var none = new List<SimVehicle>();
        List<SimVehicle> Tail(int link, int lane) => lanes.TryGetValue((link, lane), out var l) ? l : none;
        // Todas as faixas existem desde o início (a lista de faixas não muda durante as varreduras).
        foreach (var l in net.Links) for (int k = 0; k < l.Lanes; k++) LaneList(l.Index, k);
        var inNode = net.Nodes.Select(_ => new List<SimVehicle>()).ToArray();
        foreach (var l in net.Links) res.Approaches[l.Index] = new SimApproach { Link = l.Index };

        // Linha de parada e início de cada trecho recuados para a borda do cruzamento: os trechos da rede vão de centro a
        // centro dos nós, mas o veículo para antes da via transversal (e da faixa de pedestres) e atravessa o miolo pelo
        // trajeto do movimento – as filas de aproximações diferentes não se encontram no meio do cruzamento.
        double EdgeClear(TrafficLink l, int node)
        {
            var nd = net.Nodes[node];
            if (nd.IsZone || nd.Kind is not (TipoNo.Intersecao or TipoNo.CruzamentoSemControle)) return 0;
            var w = nd.In.Concat(nd.Out).Select(i => net.Links[i].Road).Where(r => r.Id != l.Road.Id).Select(r => r.CarriageWidth / 2)
                .DefaultIfEmpty(3.5).Max();
            return Math.Min(w + 1.2 + (nd.Crosswalks ? 4.5 : 0), l.Length * 0.35);
        }
        var endClear = net.Links.Select(l => EdgeClear(l, l.To)).ToArray();
        var startClear = net.Links.Select(l => EdgeClear(l, l.From)).ToArray();
        double EndOf(TrafficLink l) => l.Length - endClear[l.Index];

        // Plano semafórico: início de cada fase no ciclo (fases em sequência, 4 s de amarelo + vermelho geral).
        var plans = new Dictionary<int, (double C, List<(double Start, double G, HashSet<int> Links, SignalPhase Phase)> Phases, double Offset)>();
        foreach (var nr in macro.Nodes.Values.Where(n => n.Control == ControleNo.Semaforo && n.Phases.Count > 0))
        {
            var list = new List<(double, double, HashSet<int>, SignalPhase)>();
            var t0 = 0.0;
            foreach (var ph in nr.Phases) { list.Add((t0, ph.Green, ph.Links.ToHashSet(), ph)); t0 += ph.Green + nr.Intergreen; }
            plans[nr.Node.Index] = (Math.Max(nr.Cycle, t0), list, nr.Offset);
        }
        // Tempo dentro do ciclo, com a defasagem da coordenação.
        static double InCycle((double C, List<(double Start, double G, HashSet<int> Links, SignalPhase Phase)> Phases, double Offset) p, double t) =>
            ((t - p.Offset) % p.C + p.C) % p.C;
        var protectedLeft = macro.Nodes.Values.SelectMany(n => n.Approaches).Where(a => a.ProtectedLeft).Select(a => a.Link).ToHashSet();
        int GreenPhase(int node, double t)
        {
            if (!plans.TryGetValue(node, out var p)) return -1;
            var tc = InCycle(p, t);
            for (int k = 0; k < p.Phases.Count; k++)
                if (tc >= p.Phases[k].Start && tc < p.Phases[k].Start + p.Phases[k].G) return k;
            return -1;
        }
        // Estado para a animação: fase em verde ou entreverdes (amarelo + vermelho geral) depois dela.
        int SignalState(int node, double t)
        {
            var k = GreenPhase(node, t);
            if (k >= 0) return k;
            var p = plans[node];
            var tc = InCycle(p, t);
            for (int j = p.Phases.Count - 1; j >= 0; j--)
                if (tc >= p.Phases[j].Start + p.Phases[j].G) return -(j + 2);
            return -(p.Phases.Count - 1 + 2);
        }
        // Verde para o movimento (0 = não, 1 = permitido, 2 = protegido/sem conflito).
        int GreenFor(int node, int link, Giro g, double t, double margin)
        {
            var k = GreenPhase(node, t);
            if (k < 0) return 0;
            var ph = plans[node].Phases[k];
            if (!ph.Links.Contains(link) || !ph.Phase.Allows(g)) return 0;
            if (InCycle(plans[node], t) >= ph.Start + ph.G - margin) return 0;
            // Protegido: fase só de esquerdas ou verde antecipado (o sentido oposto está no vermelho).
            return ph.Phase.LeftOnly || !OpposingThrough(net.Nodes[node], link).Any(ph.Links.Contains) ? 2 : 1;
        }
        bool IsGreen(int node, int link, double t, double margin) => GreenFor(node, link, Giro.Frente, t, margin) > 0;
        // Entreverdes logo após o verde desta aproximação (quem já está na linha termina a conversão).
        bool JustEnded(int node, int link, Giro g, double t, double within)
        {
            if (!plans.TryGetValue(node, out var p)) return false;
            var tc = InCycle(p, t);
            foreach (var ph in p.Phases)
                if (ph.Links.Contains(link) && ph.Phase.Allows(g) && tc >= ph.Start + ph.G && tc < ph.Start + ph.G + within) return true;
            return false;
        }

        // Preferência em cada nó: aproximações principais (PARE/Dê a preferência) ou quem vem pela direita.
        var major = new Dictionary<int, HashSet<int>>();
        foreach (var nr in macro.Nodes.Values) major[nr.Node.Index] = nr.Approaches.Where(a => a.Major).Select(a => a.Link).ToHashSet();
        Vec2 EndDir(int link) { var l = net.Links[link]; return l.Path.TangentAt(l.Length); }

        // Pedestres nas travessias do meio da quadra: intervalos ocupados.
        var peds = new Dictionary<(int Link, int K), List<(double From, double To)>>();
        List<CrossingPlan> PlansOf(TrafficLink l) => macro.CrossingPlans.GetValueOrDefault(l.Index) ?? new List<CrossingPlan>();
        foreach (var l in net.Links)
            foreach (var cp in PlansOf(l))
                if (!l.Crosswalks.Any(c => Math.Abs(c.At - cp.At) < 3)) l.Crosswalks.Add((cp.At, l.Road.CarriageWidth));
        foreach (var l in net.Links)
            for (int k = 0; k < l.Crosswalks.Count; k++)
            {
                var list = new List<(double, double)>();
                var t = 0.0;
                var dur = l.Crosswalks[k].Width / 1.2 + 2;
                var cwAt = l.Crosswalks[k].At;
                if (PlansOf(l).FirstOrDefault(cp => Math.Abs(cp.At - cwAt) < 3) is { } plan)
                {
                    // Travessia semaforizada: vermelho dos veículos (verde de pedestres + entreverdes) a cada ciclo, com a defasagem.
                    for (var c0 = plan.Offset - plan.Cycle * Math.Ceiling((plan.Offset + 1) / plan.Cycle); c0 < total; c0 += plan.Cycle) list.Add((c0, c0 + plan.Red));
                    peds[(l.Index, k)] = list;
                    continue;
                }
                while (opt.PedestriansPerHour > 0)
                {
                    t += -Math.Log(1 - rnd.NextDouble()) * 3600 / opt.PedestriansPerHour;
                    if (t >= total) break;
                    list.Add((t, t + dur));
                    t += dur * 0.3;
                }
                peds[(l.Index, k)] = list;
            }
        bool PedOn(int link, int k, double t) => peds.TryGetValue((link, k), out var l) && l.Any(x => t >= x.From && t < x.To);

        var why = new Dictionary<string, int>();
        var lastEntry = new Dictionary<(int, int), double>();
        var headways = new List<double>();
        int nextId = 0;
        double sumTT = 0, sumDelay = 0;
        var lastFrame = -1.0;

        // Pontos de ônibus de cada trecho em ordem (afastados do fim para não travar a entrada no nó).
        var stops = net.Links.ToDictionary(l => l.Index, l => l.BusStops.Select(b => (At: Math.Clamp(b.At, 6 + startClear[l.Index], Math.Max(6 + startClear[l.Index], EndOf(l) - 8)), b.Bay)).OrderBy(b => b.At).ToList());
        double V0(SimVehicle x, TrafficLink l) => l.FreeSpeed * x.SpeedFactor;
        // Aceleração IDM de x atrás de lead (nulo = via livre).
        double Idm(SimVehicle x, SimVehicle? lead, TrafficLink l)
        {
            var v0 = Math.Max(0.5, V0(x, l));
            var free = 1 - Math.Pow(x.V / v0, 4);
            if (lead == null) return x.A * free;
            var gap = Math.Max(0.1, lead.S - lead.Length - x.S);
            var sStar = 2.0 + Math.Max(0, x.V * x.T + x.V * (x.V - lead.V) / (2 * Math.Sqrt(x.A * x.B)));
            return Math.Max(-9, x.A * (free - Math.Pow(sStar / gap, 2)));
        }
        // Faixas aceitáveis no trecho: conversão no fim do trecho e ponto de ônibus pela frente.
        HashSet<int> Allowed(SimVehicle v, TrafficLink l)
        {
            var all = Enumerable.Range(0, l.Lanes).ToHashSet();
            if (v.Type == 2 && v.NextStop < stops[l.Index].Count) return new HashSet<int> { 0 };
            HashSet<int> res;
            var ndTo = net.Nodes[l.To];
            if (v.Step + 1 >= v.Route.Count) res = all;
            // Rotatória de uma faixa: as faixas de entrada se fundem no anel – qualquer uma serve para qualquer saída.
            else if (ndTo.Kind == TipoNo.Rotatoria && ndTo.RoundaboutLanes <= 1) res = all;
            else
            {
                var g = TrafficAnalysis.TurnOf(net, l.Index, v.Route[v.Step + 1]);
                if (l.LaneTurns.Count > 0)
                {
                    // Setas pintadas: só as faixas que permitem o movimento.
                    res = all.Where(k => l.TurnsOf(k) is not { } set || set.Contains(g) || (g == Giro.Retorno && set.Contains(Giro.Esquerda))).ToHashSet();
                    if (res.Count == 0) res = all;
                }
                else if (g == Giro.Direita) res = new HashSet<int> { 0 };
                else if (g is Giro.Esquerda or Giro.Retorno) res = new HashSet<int> { l.Lanes - 1 };
                else
                {
                    res = all;
                    if (protectedLeft.Contains(l.Index) && l.Lanes >= 3) res.Remove(l.Lanes - 1);
                }
            }
            // Faixa fechada à frente (zebrado, cones, exclusiva): sai dela antes.
            if (l.ClosedLanes.Count > 0)
            {
                var open = res.Where(k => !l.ClosedLanes.Any(c => c.Lane == k && c.S1 > v.S && c.S0 - v.S < 150)).ToHashSet();
                if (open.Count > 0) res = open;
                else
                {
                    var any = all.Where(k => !l.ClosedLanes.Any(c => c.Lane == k && c.S1 > v.S && c.S0 - v.S < 150)).ToHashSet();
                    if (any.Count > 0) res = any;
                }
            }
            return res;
        }

        int LaneFor(TrafficLink l, int? nextLink)
        {
            if (l.Lanes > 1 && (l.LaneTurns.Count > 0 || l.ClosedLanes.Count > 0))
            {
                // Faixas permitidas pelas setas e abertas no início do trecho; a menos carregada.
                var g0 = nextLink == null ? Giro.Frente : TrafficAnalysis.TurnOf(net, l.Index, nextLink.Value);
                var cand = Enumerable.Range(0, l.Lanes).Where(k => nextLink == null || l.TurnsOf(k) is not { } set || set.Contains(g0) || (g0 == Giro.Retorno && set.Contains(Giro.Esquerda)))
                    .Where(k => !l.ClosedLanes.Any(c => c.Lane == k && c.S0 < 150)).ToList();
                if (cand.Count == 0) cand = Enumerable.Range(0, l.Lanes).Where(k => !l.ClosedLanes.Any(c => c.Lane == k && c.S0 < 20)).ToList();
                if (cand.Count > 0) return cand.OrderBy(k => Tail(l.Index, k).Count + rnd.NextDouble() * 0.9).First();
            }
            if (l.Lanes <= 1 || nextLink == null) return rnd.Next(l.Lanes);
            var g = TrafficAnalysis.TurnOf(net, l.Index, nextLink.Value);
            if (g == Giro.Direita) return 0;
            if (g is Giro.Esquerda or Giro.Retorno) return l.Lanes - 1;
            // Em frente: a faixa com menos veículos (com três ou mais faixas e esquerda protegida, a da esquerda fica para quem converte).
            var n = protectedLeft.Contains(l.Index) && l.Lanes >= 3 ? l.Lanes - 1 : l.Lanes;
            var best = 0;
            var bestScore = double.MaxValue;
            for (int k = 0; k < n; k++)
            {
                var score = Tail(l.Index, k).Count + rnd.NextDouble() * 0.9;
                if (score < bestScore) { bestScore = score; best = k; }
            }
            return best;
        }

        // ---------------------------------------------------- trajetos e conflitos nos nós
        var movePaths = new Dictionary<SimMove, SimPath>();
        var pathCache = new Dictionary<(int, int), List<int>?>();
        SimPath PathOf(SimMove m)
        {
            if (movePaths.TryGetValue(m, out var p)) return p;
            var nd = net.Nodes[m.Node];
            var from = net.Links[m.From];
            var to = net.Links[m.To];
            var g = TrafficAnalysis.TurnOf(net, m.From, m.To);
            var pts = NodePath(net, nd, from, m.FromLane, to, m.ToLane, g, endClear[from.Index], startClear[to.Index]);
            var line = new Polyline2(pts);
            var len = Math.Max(0.5, line.Length);
            var n = Math.Max(2, (int)Math.Ceiling(len / 0.5));
            var samples = new Vec2[n + 1];
            var st = new double[n + 1];
            for (int i = 0; i <= n; i++) { st[i] = len * i / n; samples[i] = line.PointAt(st[i]); }
            // Velocidade na curva: raio médio pelo desvio total, com aceleração lateral confortável (2,5 m/s²).
            var d0 = from.Path.TangentAt(from.Length);
            var d1 = to.Path.TangentAt(0);
            var turn = Math.Abs(Math.Atan2(d0.Cross(d1), d0.Dot(d1)));
            var v0 = Math.Min(from.FreeSpeed, to.FreeSpeed);
            if (nd.Kind == TipoNo.Rotatoria) v0 = Math.Min(v0, Math.Sqrt(2.5 * RingRadius(nd)));
            else if (turn > 0.25) v0 = Math.Min(v0, Math.Sqrt(2.5 * Math.Max(4, len / turn)));
            p = new SimPath
            {
                Pts = pts, Line = line, Length = len, V0 = Math.Max(3.0, v0), Samples = samples, St = st,
                Min = new Vec2(samples.Min(q => q.X), samples.Min(q => q.Y)), Max = new Vec2(samples.Max(q => q.X), samples.Max(q => q.Y)),
            };
            movePaths[m] = p;
            return p;
        }
        var runsCache = new Dictionary<(SimMove, SimMove), List<SimRun>>();
        List<SimRun> RunsOf(SimMove a, SimMove b)
        {
            if (runsCache.TryGetValue((a, b), out var r)) return r;
            // Simétrico: o trecho visto por b é o mesmo visto por a (estacas trocadas) – os dois concordam sobre quem está à frente.
            if (runsCache.TryGetValue((b, a), out var rb))
            {
                r = rb.Select(x => new SimRun(x.BIn, x.BOut, x.AIn, x.AOut, x.Parallel)).ToList();
                runsCache[(a, b)] = r;
                return r;
            }
            r = new List<SimRun>();
            runsCache[(a, b)] = r;
            var pa = PathOf(a);
            var pb = PathOf(b);
            const double W = 2.1;
            if (pa.Max.X + W < pb.Min.X || pb.Max.X + W < pa.Min.X || pa.Max.Y + W < pb.Min.Y || pb.Max.Y + W < pa.Min.Y) return r;
            int i0 = -1, jFirst = 0;
            double bMin = 0, bMax = 0;
            for (int i = 0; i < pa.Samples.Length; i++)
            {
                var best = double.MaxValue;
                var bj = -1;
                for (int j = 0; j < pb.Samples.Length; j++)
                {
                    var dx = pa.Samples[i].X - pb.Samples[j].X;
                    var dy = pa.Samples[i].Y - pb.Samples[j].Y;
                    var d = dx * dx + dy * dy;
                    if (d < best) { best = d; bj = j; }
                }
                var contact = best < W * W;
                if (contact)
                {
                    if (i0 < 0) { i0 = i; jFirst = bj; bMin = bMax = pb.St[bj]; }
                    else { bMin = Math.Min(bMin, pb.St[bj]); bMax = Math.Max(bMax, pb.St[bj]); }
                }
                if ((!contact || i == pa.Samples.Length - 1) && i0 >= 0)
                {
                    var i1 = contact ? i : i - 1;
                    var aIn = pa.St[i0];
                    var aOut = pa.St[i1];
                    // Trecho compartilhado (fusão, anel, mesma faixa de origem) × cruzamento: direção no meio do trecho.
                    var im = (i0 + i1) / 2;
                    var jm = 0;
                    var bestM = double.MaxValue;
                    for (int j = 0; j < pb.Samples.Length; j++)
                    {
                        var d = pa.Samples[im].DistanceTo(pb.Samples[j]);
                        if (d < bestM) { bestM = d; jm = j; }
                    }
                    // Faixa em b pelo mesmo critério de contato (num cruzamento o ponto mais próximo é sempre o mesmo e a
                    // faixa colapsaria no ponto de cruzamento – a traseira de b "liberaria" a zona ainda sobre a faixa de a).
                    double Near(int j)
                    {
                        var m = double.MaxValue;
                        for (int i = i0; i <= i1; i++) m = Math.Min(m, pa.Samples[i].DistanceTo(pb.Samples[j]));
                        return m;
                    }
                    var jl = jm;
                    while (jl > 0 && Near(jl - 1) < W) jl--;
                    var jr = jm;
                    while (jr < pb.Samples.Length - 1 && Near(jr + 1) < W) jr++;
                    bMin = Math.Min(bMin, pb.St[jl]);
                    bMax = Math.Max(bMax, pb.St[jr]);
                    var da = pa.Line.TangentAt(Math.Min(pa.Length, pa.St[im]));
                    var db = pb.Line.TangentAt(Math.Min(pb.Length, pb.St[jm]));
                    r.Add(new SimRun(aIn, aOut, bMin, bMax, da.Dot(db) > 0.6 && aOut - aIn > 3.0));
                    i0 = -1;
                }
            }
            return r;
        }
        // Estaca do trajeto mais próxima do ponto, entre s0 e s1.
        static double Project(SimPath p, Vec2 q, double s0, double s1)
        {
            var best = double.MaxValue;
            var st = s0;
            for (int i = 0; i < p.Samples.Length; i++)
            {
                if (p.St[i] < s0 - 0.5 || p.St[i] > s1 + 0.5) continue;
                var dx = p.Samples[i].X - q.X;
                var dy = p.Samples[i].Y - q.Y;
                var d = dx * dx + dy * dy;
                if (d < best) { best = d; st = p.St[i]; }
            }
            return st;
        }
        // Seta (CTB art. 196): na troca de faixa, ao se aproximar da conversão (últimos 45 m) e durante ela; na rotatória,
        // à direita para sair.
        float Blinker(SimVehicle v)
        {
            if (!v.InNode && v.LaneFrom >= 0 && Math.Abs(v.LaneShift) > 0.3) return v.Lane < v.LaneFrom ? 1 : -1;
            int from, to;
            if (v.InNode && v.Mov is { } m)
            {
                if (net.Nodes[m.Node].Kind == TipoNo.Rotatoria) return v.NodeLen - v.NodeS < 18 ? 1 : 0;
                (from, to) = (m.From, m.To);
            }
            else
            {
                if (v.Step + 1 >= v.Route.Count || EndOf(net.Links[v.Link]) - v.S > 45) return 0;
                if (net.Nodes[net.Links[v.Link].To].Kind == TipoNo.Rotatoria) return 0;
                (from, to) = (v.Link, v.Route[v.Step + 1]);
            }
            return TrafficAnalysis.TurnOf(net, from, to) switch { Giro.Esquerda or Giro.Retorno => -1, Giro.Direita => 1, _ => 0 };
        }
        SimMove Planned(SimVehicle v)
        {
            var l = net.Links[v.Link];
            var to = net.Links[v.Route[v.Step + 1]];
            return new SimMove(l.To, l.Index, v.Lane, to.Index, Math.Clamp(PlanLane(v), 0, to.Lanes - 1));
        }

        // Atores de cada nó (quem está nele, quem chega na frente de cada faixa e a traseira de quem acabou de sair) e o
        // obstáculo à frente de cada um no seu trajeto: trecho compartilhado (seguir), zona de conflito (quem chega antes
        // passa; quem está dentro termina) e espaço na faixa de saída. Assim ninguém se sobrepõe – no máximo para e espera.
        var obstacle = new Dictionary<int, (double Gap, double LeadV)>();
        var obstacleWhy = new Dictionary<int, string>();
        var actors = new List<(SimVehicle V, SimMove M, double S, bool Claims, int Rank, bool Tail, bool Inside)>();
        void Obstacles(double t)
        {
            obstacle.Clear();
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                var nd = net.Nodes[n];
                if (nd.IsZone) continue;
                actors.Clear();
                // "Dentro" é quem já avançou de fato além da linha (ou vem embalado): quem parou rente a ela ainda espera como os demais.
                foreach (var v in inNode[n]) actors.Add((v, v.Mov!.Value, v.NodeS, true, v.StuckFor > 6 ? -1 : 0, false, v.NodeS > 1.5 || v.V > 4));
                foreach (var li in nd.In)
                {
                    var l = net.Links[li];
                    for (int k = 0; k < l.Lanes; k++)
                    {
                        var list = Tail(li, k);
                        if (list.Count == 0) continue;
                        SimVehicle? f = null;
                        foreach (var x in list) if (!x.InBay && (f == null || x.S > f.S)) f = x;
                        if (f == null || f.DwellUntil > 0 || f.Step + 1 >= f.Route.Count) continue;
                        var dist = EndOf(l) - f.S;
                        if (dist > 45) continue;
                        var m = f.Mov is { } pm && pm.From == li && pm.FromLane == f.Lane ? pm : Planned(f);
                        f.Mov = m;
                        var claims = CanProceed(f, t, dist < 1.5);
                        actors.Add((f, m, -dist, claims, major[n].Contains(li) ? 1 : 2, false, false));
                    }
                }
                foreach (var lo in nd.Out)
                {
                    var l = net.Links[lo];
                    for (int k = 0; k < l.Lanes; k++)
                        foreach (var x in Tail(lo, k))
                            if (x.PrevMov is { } pm && pm.Node == n && x.S - startClear[lo] < x.Length + 0.5)
                                actors.Add((x, pm, x.PrevLen + x.S - startClear[lo], true, 0, true, true));
                }
                if (actors.Count == 0) continue;
                for (int ia = 0; ia < actors.Count; ia++)
                {
                    var a = actors[ia];
                    if (a.Tail) continue;
                    var pa = PathOf(a.M);
                    var gap = double.PositiveInfinity;
                    var leadV = 0.0;
                    var whyO = "";
                    // Espaço na faixa de saída.
                    SimVehicle? last = null;
                    foreach (var x in Tail(a.M.To, a.M.ToLane)) if (last == null || x.S < last.S) last = x;
                    if (last != null)
                    {
                        var g = pa.Length - a.S + last.S - startClear[a.M.To] - last.Length - 0.3;
                        if (g < gap) { gap = g; leadV = last.V; whyO = $"saida:{last.Id}"; }
                    }
                    for (int ib = 0; ib < actors.Count; ib++)
                    {
                        if (ib == ia) continue;
                        var b = actors[ib];
                        if (ReferenceEquals(b.V, a.V)) continue;
                        foreach (var run in RunsOf(a.M, b.M))
                        {
                            var bFront = b.S - run.BIn;
                            if (b.S - b.V.Length - run.BOut > 0.3) continue;      // b já liberou o trecho
                            var aIn = a.S - run.AIn;
                            if (a.S - a.V.Length > run.AOut + 0.3) continue;      // a já passou
                            // Mesma faixa de origem (a ordem é a da fila, inclusive quem entrou no nó e ainda está antes da linha
                            // ou tem a traseira na aproximação) ou mesma faixa de destino (fusão): seguir, não cruzar.
                            var sameStart = a.M.From == b.M.From && a.M.FromLane == b.M.FromLane && run.AIn < 0.6 && run.BIn < 0.6;
                            var sameEnd = a.M.To == b.M.To && a.M.ToLane == b.M.ToLane
                                          && run.AOut > pa.Length - 0.8 && run.BOut > PathOf(b.M).Length - 0.8;
                            if (run.Parallel || sameStart || sameEnd)
                            {
                                // Quem está à frente: a frente de b projetada no trajeto de a (no anel e nas fusões os trajetos se
                                // sobrepõem de verdade); na mesma faixa de origem, pela ordem da fila.
                                var bOnA = sameStart ? run.AIn + bFront : Project(pa, TrajPoint(b.V, 0), run.AIn - 4, run.AOut + 1);
                                // Quem ainda está na aproximação só é líder para quem vem da mesma faixa.
                                if (((bFront > 0 && b.Inside) || sameStart) && bOnA > a.S + 0.05)
                                {
                                    // b à frente no trecho compartilhado: segue a traseira dele (na fusão, sem passar do começo do
                                    // trecho enquanto a traseira dele ainda está no outro ramo).
                                    var g = (sameStart ? bOnA - b.V.Length : Math.Max(run.AIn - 0.8, bOnA - b.V.Length)) - a.S;
                                    if (g < gap) { gap = g; leadV = b.V.V; whyO = $"segue:{b.V.Id}"; }
                                }
                                else if (bFront <= 0 && aIn <= 0 && b.Claims && Yields(a, b, run.AIn - a.S, run.BIn - b.S))
                                {
                                    var g = run.AIn - 0.8 - a.S;
                                    if (g < gap) { gap = g; leadV = 0; whyO = $"fusao:{b.V.Id}"; }
                                }
                            }
                            else
                            {
                                // Quem espera na linha ocupa a boca do próprio trajeto (a frente fica até ~1,5 m antes dela).
                                var bInside = b.Inside ? bFront > -0.3 : run.BIn < 0.6 ? bFront > -1.6 : b.Claims && bFront > -0.3;
                                if (aIn > -0.3)
                                {
                                    // Os dois dentro da zona (entrada forçada): o que está mais adiante termina, o outro para.
                                    if (bInside && bFront > aIn && b.Claims) { if (0 < gap) { gap = 0; leadV = 0; whyO = $"dentro:{b.V.Id}"; } }
                                    continue;
                                }
                                if (bInside || (b.Claims && Yields(a, b, run.AIn - a.S, run.BIn - b.S)))
                                {
                                    // Não entra no cruzamento para ficar parado dentro dele (CTB art. 45): com a zona ocupada (ou
                                    // prestes a ser) por quem está parado lá dentro, espera na linha – senão três ou quatro veículos se travam no meio do nó.
                                    var hold = !a.Inside && b.Inside && b.V.V < 2.0;
                                    var g = (hold ? Math.Min(run.AIn - 0.8, 0.5) : run.AIn - 0.8) - a.S;
                                    if (g < gap) { gap = g; leadV = 0; whyO = $"{(bInside ? "ocupa" : "cede")}:{b.V.Id}"; }
                                }
                            }
                        }
                    }
                    if (!double.IsInfinity(gap)) { obstacle[a.V.Id] = (gap, leadV); if (Debug) obstacleWhy[a.V.Id] = whyO; }
                }
            }
        }
        // Ordem de passagem numa zona: quem não consegue mais parar, depois quem chega antes, a prioridade (no nó >
        // principal > secundária, quem espera muito vira prioridade) e o número do veículo (desempate sem impasse).
        static bool Yields((SimVehicle V, SimMove M, double S, bool Claims, int Rank, bool Tail, bool Inside) a,
            (SimVehicle V, SimMove M, double S, bool Claims, int Rank, bool Tail, bool Inside) b, double da, double db)
        {
            static (int C, int In, double T, int R, int Id) Key((SimVehicle V, SimMove M, double S, bool Claims, int Rank, bool Tail, bool Inside) x, double d)
            {
                var v = x.V.V;
                var committed = v * v / (2 * 4.0) > d - 0.3 ? 0 : 1;
                var ta = d <= 0 ? 0 : d / Math.Max(v, 1.5) + (v < 0.5 ? 1.2 : 0);
                // Quem já passou da linha termina a travessia: não para no meio do nó para ceder a quem ainda chega.
                return (committed, x.Inside ? 0 : 1, Math.Round(ta, 1), x.Rank, x.V.Id);
            }
            return Key(b, db).CompareTo(Key(a, da)) < 0;
        }

        for (var time = 0.0; time < total; time += Dt)
        {
            if (cancel.IsCancellationRequested) break;
            var measuring = time >= opt.WarmupSeconds;
            // ---------------------------------------------------- chegadas
            while (next < arrivals.Count && arrivals[next].T <= time)
            {
                var od = arrivals[next++].Od;
                var routes = macro.Routes[od];
                var r = rnd.NextDouble() * routes.Sum(x => x.Weight);
                var route = routes[^1].Links;
                foreach (var (w, links) in routes) { r -= w; if (r <= 0) { route = links; break; } }
                var type = rnd.NextDouble() < opt.HeavyVehicles ? 1 : rnd.NextDouble() < opt.Buses / Math.Max(1e-6, 1 - opt.HeavyVehicles) ? 2 : 0;
                var v = new SimVehicle
                {
                    Id = nextId++, Type = type, Length = type == 0 ? 4.5 : 12, A = type == 0 ? 2.5 : 1.2, B = type == 0 ? 2.5 : 2.0, T = type == 0 ? 1.0 : 1.4,
                    SpeedFactor = Math.Clamp(1 + (rnd.NextDouble() + rnd.NextDouble() - 1) * 0.12, 0.85, 1.12), Route = route, Born = time,
                };
                if (!queues.TryGetValue(route[0], out var q)) queues[route[0]] = q = new Queue<SimVehicle>();
                q.Enqueue(v);
                if (measuring) res.Spawned++;
            }
            foreach (var (link, q) in queues)
            {
                if (q.Count == 0) continue;
                var l = net.Links[link];
                var v = q.Peek();
                var lane = LaneFor(l, v.Route.Count > 1 ? v.Route[1] : null);
                var list = LaneList(link, lane);
                var last = list.Count == 0 ? null : list.MinBy(x => x.S);
                if (last != null && last.S - last.Length - startClear[link] < 4) continue;
                // Quem acabou de trocar de faixa ainda ocupa a de origem (a lista "shifting" é do passo anterior).
                var busy = false;
                for (int ol = 0; ol < l.Lanes && !busy; ol++)
                    if (ol != lane)
                        foreach (var x in LaneList(link, ol))
                            if (x.LaneFrom == lane && Math.Abs(x.LaneShift) > 0.9 && x.S - x.Length - startClear[link] < 4) { busy = true; break; }
                if (busy) continue;
                q.Dequeue();
                v.Lane = lane;
                v.S = startClear[link];
                v.V = Math.Min(l.FreeSpeed * 0.7, last == null ? l.FreeSpeed * 0.7 : Math.Max(0, last.V));
                v.LinkEnter = time;
                list.Add(v);
                vehicles.Add(v);
            }

            // Veículos à espera de brecha para a troca obrigatória.
            wanting.Clear();
            foreach (var x in vehicles) if (!x.InNode && x.WantsLane >= 0) wanting.Add(x);
            // Veículos trocando de faixa (ainda sobre a faixa de origem).
            shifting.Clear();
            foreach (var x in vehicles) if (!x.InNode && x.LaneFrom >= 0 && Math.Abs(x.LaneShift) > 0.9) shifting.Add(x); else x.LaneFrom = x.InNode ? -1 : x.LaneFrom;

            // ---------------------------------------------------- obstáculos nos nós (estado atual)
            foreach (var (_, list) in lanes) if (list.Count > 1) list.Sort((a, b) => b.S.CompareTo(a.S));
            Obstacles(time);

            // ---------------------------------------------------- trechos
            foreach (var ((link, _), list) in lanes)
            {
                if (list.Count == 0) continue;
                var l = net.Links[link];
                list.Sort((a, b) => b.S.CompareTo(a.S));   // do mais à frente para o de trás
                var st = stops[link];
                for (int i = 0; i < list.Count; i++)
                {
                    var v = list[i];
                    // Ônibus no ponto: embarque; na baia, só volta à faixa com brecha para o veículo de trás.
                    if (v.DwellUntil > 0)
                    {
                        if (time < v.DwellUntil) { v.V = 0; continue; }
                        if (v.InBay)
                        {
                            SimVehicle? fol = null;
                            for (int j = i + 1; j < list.Count; j++) if (!list[j].InBay) { fol = list[j]; break; }
                            if (fol != null && v.S - v.Length - fol.S < 6 + fol.V * 1.6) { v.V = 0; continue; }
                            v.InBay = false;
                        }
                        v.DwellUntil = -1;
                        v.NextStop++;
                        if (measuring) res.BusStopsServed++;
                    }
                    var v0 = l.FreeSpeed * v.SpeedFactor;
                    foreach (var (at, sp, _) in l.SlowPoints)
                        if (v.S > at - 30 && v.S < at + 10) v0 = Math.Min(v0, sp);
                    // Obstáculo: veículo à frente, linha de retenção (se não puder seguir) ou pedestres na travessia.
                    var gap = double.PositiveInfinity;
                    var dv = 0.0;
                    SimVehicle? lead = null;
                    for (int j = i - 1; j >= 0; j--) if (!list[j].InBay) { lead = list[j]; break; }
                    if (lead != null)
                    {
                        gap = lead.S - lead.Length - v.S;
                        dv = v.V - lead.V;
                    }
                    // Cortesia: quem precisa entrar nesta faixa (conversão adiante) e está logo à frente, na faixa ao lado.
                    foreach (var x in wanting)
                        if (x.Link == link && x.WantsLane == v.Lane && !ReferenceEquals(x, v) && x.S > v.S && x.S - v.S < 40
                            && x.S - x.Length - v.S > -0.5 && x.S - x.Length - v.S < gap)
                        {
                            gap = x.S - x.Length - v.S;
                            dv = v.V - x.V;
                        }
                    // Quem está saindo desta faixa (troca em andamento) ainda ocupa parte dela.
                    foreach (var x in shifting)
                        if (x.Link == link && x.LaneFrom == v.Lane && !ReferenceEquals(x, v) && x.S > v.S && x.S - x.Length - v.S < gap)
                        {
                            gap = x.S - x.Length - v.S;
                            dv = v.V - x.V;
                        }
                    // Faixa fechada adiante: quem não conseguiu sair para no começo do fechamento.
                    foreach (var c in l.ClosedLanes)
                        if (c.Lane == v.Lane && c.S0 > v.S - 0.5)
                        {
                            var gc = c.S0 - v.S + 1.5;
                            if (gc < gap) { gap = gc; dv = v.V; }
                        }
                    if (v.Type == 2 && v.Lane == 0)
                    {
                        while (v.NextStop < st.Count && st[v.NextStop].At < v.S - 2) v.NextStop++;
                        if (v.NextStop < st.Count)
                        {
                            var gs = st[v.NextStop].At - v.S;
                            // Obstáculo virtual 2 m além do ponto: com a distância mínima do IDM o ônibus para rente a ele.
                            if (gs + 2 < gap) { gap = gs + 2; dv = v.V; }
                            if (gs < 1.5 && v.V < 0.6)
                            {
                                v.DwellUntil = time + opt.BusDwell * (0.6 + 0.8 * rnd.NextDouble());
                                v.InBay = st[v.NextStop].Bay;
                                v.V = 0;
                                continue;
                            }
                        }
                    }
                    for (int k = 0; k < l.Crosswalks.Count; k++)
                    {
                        var at = l.Crosswalks[k].At - 1.5;
                        if (v.S < at && PedOn(link, k, time) && at - v.S > v.V * v.V / (2 * 4.0))
                        {
                            var gp = at - v.S;
                            if (gp < gap) { gap = gp; dv = v.V; }
                        }
                    }
                    if (lead == null)
                    {
                        // Obstáculo virtual 1 m além da linha: com a distância mínima do IDM (2 m) o veículo para 1 m antes dela, sem avançar sobre o cruzamento.
                        var stop = EndOf(l) + 1.0;
                        var can = CanProceed(v, time, false);
                        if (!can)
                        {
                            var gp = stop - v.S;
                            if (gp < gap) { gap = gp; dv = v.V; }
                        }
                        // E sempre: quem está no nó à frente (inclusive a traseira de quem acabou de entrar), no conflito ou
                        // na fila da saída.
                        if (obstacle.TryGetValue(v.Id, out var ob) && ob.Gap < gap) { gap = Math.Max(0, ob.Gap); dv = v.V - ob.LeadV; }
                    }
                    var sStar = 2.0 + Math.Max(0, v.V * v.T + v.V * dv / (2 * Math.Sqrt(v.A * v.B)));
                    var acc = v.A * (1 - Math.Pow(v.V / Math.Max(0.5, v0), 4) - (double.IsInfinity(gap) ? 0 : Math.Pow(sStar / Math.Max(0.1, gap), 2)));
                    acc = Math.Max(acc, -8);
                    var nv = Math.Max(0, v.V + acc * Dt);
                    var ds = Math.Max(0, (v.V + nv) / 2 * Dt);
                    if (!double.IsInfinity(gap)) ds = Math.Min(ds, Math.Max(0, gap - 0.2 + (lead == null ? 0.2 : 0)));
                    v.V = nv;
                    v.S += ds;
                    if (v.LaneShift != 0) v.LaneShift = Math.Sign(v.LaneShift) * Math.Max(0, Math.Abs(v.LaneShift) - 1.4 * Dt);
                    if (v.V < 0.3) { if (!v.WasStopped) { v.Stops++; v.WasStopped = true; } v.StoppedFor += Dt; }
                    else if (v.V > 3) v.WasStopped = false;
                }
            }

            // ---------------------------------------------------- troca de faixa (obrigatória para a conversão / o ponto; e para ultrapassar)
            var changes = new List<(SimVehicle V, int Link, int From, int To)>();
            foreach (var l in net.Links.Where(x => x.Lanes > 1))
            {
                for (int k = 0; k < l.Lanes; k++)
                {
                    var list = Tail(l.Index, k);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var v = list[i];
                        if (v.InBay || v.DwellUntil > 0 || time - v.LastLaneChange < 3 || v.S < 5 + startClear[l.Index] || v.S > EndOf(l) + 0.5) continue;
                        var ok = Allowed(v, l);
                        if (v.S > EndOf(l) - 12 && ok.Contains(k)) continue;     // perto da linha: só a troca obrigatória
                        var toEnd = EndOf(l) - v.S;
                        SimVehicle? curLead = null;
                        for (int j = i - 1; j >= 0; j--) if (!list[j].InBay) { curLead = list[j]; break; }
                        var aCur = Idm(v, curLead, l);
                        var best = -1;
                        var bestGain = double.NegativeInfinity;
                        foreach (var tk in new[] { k - 1, k + 1 })
                        {
                            if (tk < 0 || tk >= l.Lanes) continue;
                            var nearOk = ok.Count == 0 ? k : ok.OrderBy(x => Math.Abs(x - k)).First();
                            var mandatory = !ok.Contains(k) && Math.Abs(tk - nearOk) < Math.Abs(k - nearOk);
                            if (!mandatory && !ok.Contains(tk) && toEnd < 150) continue;
                            if (!mandatory && v.Type == 2 && ok.Count == 1) continue;
                            if (!mandatory && l.LaneChangeForbidden) continue;       // LMS-1, R-8, tachões entre faixas
                            if (l.LaneClosedAt(tk, v.S + 5)) continue;
                            var target = Tail(l.Index, tk);
                            SimVehicle? nl = null, nf = null;
                            foreach (var x in target)
                            {
                                if (x.InBay) continue;
                                if (x.S > v.S) { if (nl == null || x.S < nl.S) nl = x; }
                                else if (nf == null || x.S > nf.S) nf = x;
                            }
                            if (nl != null && nl.S - nl.Length - v.S < 1.5) continue;
                            if (nf != null && v.S - v.Length - nf.S < 1.5) continue;
                            // Quem acabou de entrar no nó por essa faixa ainda ocupa o fim dela com a traseira; quem está saindo
                            // dela numa troca ainda ocupa parte dela.
                            if (RearOnApproach(l, tk) is { } rear && v.S > rear - 1.5) continue;
                            if (shifting.Any(x => x.Link == l.Index && x.LaneFrom == tk && !ReferenceEquals(x, v)
                                                  && x.S - x.Length - 1.5 < v.S && v.S - v.Length - 1.5 < x.S)) continue;
                            // Segurança (MOBIL): o novo seguidor não precisa frear forte.
                            if (nf != null && Idm(nf, v, l) < (mandatory ? -4.0 : -2.5)) continue;
                            var aNew = Idm(v, nl, l);
                            // Mantenha a direita (CTB art. 29, IV): à direita basta um ganho pequeno; à esquerda, só para ultrapassar.
                            var threshold = mandatory ? -3.0 : tk < k ? 0.1 : 0.6;
                            var gain = aNew - aCur - threshold;
                            if (gain > 0 && gain > bestGain) { bestGain = gain; best = tk; }
                        }
                        if (best >= 0) { changes.Add((v, l.Index, k, best)); v.WantsLane = -1; }
                        else if (!ok.Contains(k) && ok.Count > 0 && toEnd < 80)
                        {
                            // Obrigatória sem brecha: sinaliza a vontade (seta) – quem vem atrás na faixa ao lado abre espaço.
                            var nearOk = ok.OrderBy(x => Math.Abs(x - k)).First();
                            v.WantsLane = k + Math.Sign(nearOk - k);
                        }
                        else if (ok.Contains(k)) v.WantsLane = -1;
                    }
                }
            }
            foreach (var (v, link, from, to) in changes)
            {
                var l = net.Links[link];
                if (!Tail(link, from).Remove(v)) continue;
                LaneList(link, to).Add(v);
                v.LaneShift += l.LaneOffset(from) - l.LaneOffset(to);
                v.LaneFrom = from;
                v.WantsLane = -1;
                v.Lane = to;
                v.LastLaneChange = time;
                if (measuring) res.LaneChanges++;
            }

            // ---------------------------------------------------- entrada nos nós
            foreach (var ((link, lane), list) in lanes)
            {
                if (list.Count == 0) continue;
                var l = net.Links[link];
                var v = list.MaxBy(x => x.S)!;
                if (v.S < EndOf(l) - 1.3 || v.DwellUntil > 0 || v.InBay) continue;
                if (!CanProceed(v, time, true)) continue;
                list.Remove(v);
                if (Debug)
                {
                    if (lastEntry.TryGetValue((link, lane), out var le) && time - le < 6) headways.Add(time - le);
                    lastEntry[(link, lane)] = time;
                }
                var nd = net.Nodes[l.To];
                var sa = res.Approaches[link];
                var free = l.Length / l.FreeSpeed;
                if (measuring)
                {
                    sa.Vehicles++;
                    sa.DelaySum += Math.Max(0, time - v.LinkEnter - free);
                    sa.Stops += v.Stops;
                    var ls = res.LinkSpeed.GetValueOrDefault(link);
                    res.LinkSpeed[link] = (ls.Dist + l.Length, ls.Time + (time - v.LinkEnter), ls.N + 1);
                }
                v.FreeTime += free;
                v.NextStop = 0;
                v.LaneShift = 0;
                v.Stops = 0;
                v.StoppedFor = 0;
                if (v.Step + 1 >= v.Route.Count)
                {
                    // Chegou ao destino.
                    vehicles.Remove(v);
                    if (measuring)
                    {
                        res.Completed++;
                        sumTT += time - v.Born;
                        sumDelay += Math.Max(0, time - v.Born - v.FreeTime);
                    }
                    continue;
                }
                var to = net.Links[v.Route[v.Step + 1]];
                var mov = v.Mov is { } pm && pm.From == link && pm.FromLane == v.Lane && pm.To == to.Index ? pm : Planned(v);
                var path = PathOf(mov);
                v.Mov = mov;
                v.InNode = true;
                v.NodeIndex = nd.Index;
                v.FromLink = link;
                v.ToLink = to.Index;
                v.NextLane = -1;
                v.NodePath = path.Pts;
                v.Lane = mov.ToLane;
                // Continua de onde está (até 1,3 m antes da linha ou já além dela): sem saltos.
                v.NodeS = v.S - EndOf(l);
                v.NodeLen = path.Length;
                v.StuckFor = 0;
                if (nd.Kind == TipoNo.Rotatoria) v.RingAngle = Math.Atan2(v.NodePath[0].Y - nd.Pos.Y, v.NodePath[0].X - nd.Pos.X);
                inNode[nd.Index].Add(v);
            }

            // ---------------------------------------------------- dentro dos nós
            for (int n = 0; n < inNode.Length; n++)
            {
                var list = inNode[n];
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var v = list[i];
                    var path = PathOf(v.Mov!.Value);
                    // IDM também dentro do nó: velocidade de curva e o obstáculo à frente no trajeto (nunca atravessa ninguém).
                    var (gap, leadV) = obstacle.TryGetValue(v.Id, out var ob) ? ob : (double.PositiveInfinity, 0.0);
                    var v0 = v.NodeS < 0 ? Math.Max(path.V0, v.V) : path.V0;
                    var free = 1 - Math.Pow(v.V / Math.Max(0.5, v0), 4);
                    var sStar = 2.0 + Math.Max(0, v.V * v.T + v.V * (v.V - leadV) / (2 * Math.Sqrt(v.A * v.B)));
                    var acc = double.IsInfinity(gap) ? v.A * free : v.A * (free - Math.Pow(sStar / Math.Max(0.1, gap), 2));
                    acc = Math.Max(acc, -9);
                    var nv = Math.Max(0, v.V + acc * Dt);
                    var ds = Math.Max(0, (v.V + nv) / 2 * Dt);
                    if (!double.IsInfinity(gap)) ds = Math.Min(ds, Math.Max(0, gap - 0.2));
                    v.V = ds < 1e-6 && nv > 0 && !double.IsInfinity(gap) && gap < 0.4 ? 0 : nv;
                    v.NodeS += ds;
                    if (v.V < 0.3) { v.StuckFor += Dt; if (!v.WasStopped) { v.Stops++; v.WasStopped = true; } }
                    else { v.StuckFor = 0; if (v.V > 3) v.WasStopped = false; }
                    if (net.Nodes[n].Kind == TipoNo.Rotatoria && v.NodeS >= 0)
                    {
                        var p = path.Line.PointAt(Math.Min(v.NodeS, path.Length));
                        v.RingAngle = Math.Atan2(p.Y - net.Nodes[n].Pos.Y, p.X - net.Nodes[n].Pos.X);
                    }
                    if (v.NodeS < path.Length) continue;
                    var to = net.Links[v.ToLink];
                    var target = LaneList(to.Index, v.Lane);
                    var over = v.NodeS - path.Length + startClear[to.Index];
                    var last = target.Count == 0 ? null : target.MinBy(x => x.S);
                    if (last != null && last.S - last.Length < over + 0.3) { v.NodeS = path.Length; v.V = 0; continue; }   // sem espaço: espera
                    list.RemoveAt(i);
                    v.InNode = false;
                    v.RingAngle = double.NaN;
                    v.PrevMov = v.Mov;
                    v.PrevLen = path.Length;
                    v.Mov = null;
                    v.StuckFor = 0;
                    v.Step++;
                    v.S = over;
                    v.LinkEnter = time;
                    target.Add(v);
                }
            }

            // ---------------------------------------------------- filas e quadros
            if (measuring)
                foreach (var ((link, _), list) in lanes)
                {
                    if (list.Count == 0) continue;
                    var l = net.Links[link];
                    // Fila: pelotão contínuo de veículos parados (ou quase) a partir da linha de retenção.
                    var front = list[0];
                    if (front.V > 1.5 || front.S < EndOf(l) - 12) continue;
                    var back = front;
                    var n = 1;
                    for (int j = 1; j < list.Count; j++)
                    {
                        var x = list[j];
                        if (x.V > 3 || back.S - back.Length - x.S > 12) break;
                        back = x;
                        n++;
                    }
                    var q = EndOf(l) - back.S + back.Length;
                    if (n >= 2 && q > res.Approaches[link].MaxQueue) res.Approaches[link].MaxQueue = q;
                }
            res.MaxVehicles = Math.Max(res.MaxVehicles, vehicles.Count);
            if (time - lastFrame >= 1.0 - 1e-6)
            {
                lastFrame = time;
                var data = new float[vehicles.Count * SimFrame.Stride];
                var k = 0;
                foreach (var v in vehicles)
                {
                    // Corpo pela corda entre a frente e a traseira sobre a trajetória (como os eixos de um veículo real na
                    // curva): o mapa desenha centrado; ônibus e caminhões não "saem" pela tangente nas curvas e no anel.
                    var fp = TrajPoint(v, 0);
                    var rp = TrajPoint(v, v.Length);
                    var p = (fp + rp) / 2;
                    var chord = fp - rp;
                    Vec2 d;
                    if (chord.Length > 0.5) d = chord.Normalized();
                    else
                    {
                        var q = TrajPoint(v, 0.5);
                        d = fp.DistanceTo(q) > 1e-6 ? (fp - q).Normalized() : Vec2.UnitX;
                    }
                    data[k++] = (float)p.X;
                    data[k++] = (float)p.Y;
                    data[k++] = (float)Math.Atan2(d.Y, d.X);
                    data[k++] = v.Type;
                    data[k++] = (float)v.V;
                    data[k++] = v.Id;
                    data[k++] = Blinker(v);
                }
                var sig = plans.Keys.ToDictionary(nd => nd, nd => SignalState(nd, time));
                Dictionary<int, string>? info = null;
                if (Debug)
                {
                    info = new Dictionary<int, string>();
                    foreach (var v in vehicles)
                    {
                        var ob = obstacle.TryGetValue(v.Id, out var o) ? $" obst={o.Gap:0.0} {obstacleWhy.GetValueOrDefault(v.Id)}" : "";
                        info[v.Id] = v.InNode && v.Mov is { } m
                            ? $"NÓ{m.Node} {m.From}/{m.FromLane}->{m.To}/{m.ToLane} s={v.NodeS:0.0}/{v.NodeLen:0.0} V={v.V:0.0}{ob}"
                            : $"L{v.Link}/{v.Lane} S={v.S:0.0}/{EndOf(net.Links[v.Link]):0.0} V={v.V:0.0}{ob}{(v.Mov is { } pm ? $" plan {pm.From}/{pm.FromLane}->{pm.To}/{pm.ToLane}" : "")}";
                    }
                }
                if (time >= opt.WarmupSeconds - 60) res.Frames.Add(new SimFrame { Time = time - opt.WarmupSeconds, Data = data, SignalPhase = sig, Info = info });
            }
            progress?.Report(time / total);
        }
        if (Debug)
            foreach (var ((link, lane), list) in lanes.Where(x => x.Value.Count > 0))
            {
                var front = list.MaxBy(x => x.S)!;
                var nxt = front.Step + 1 < front.Route.Count ? front.Route[front.Step + 1] : -1;
                res.Events.Add($"DBG link {link} ({net.Links[link].Name}) lane {lane}: {list.Count} veh, frente S={front.S:0.0}/{net.Links[link].Length:0.0} V={front.V:0.0} " +
                    $"próximo {(nxt >= 0 ? TrafficAnalysis.TurnOf(net, link, nxt).ToString() : "fim")} pode={CanProceed(front, total, true)}");
            }
        if (Debug)
            for (int n = 0; n < inNode.Length; n++)
                foreach (var v in inNode[n])
                    res.Events.Add($"DBG nó {net.Nodes[n].Label} veh {v.Id} de {v.FromLink} para {v.ToLink} faixa {v.Lane} s={v.NodeS:0.0}/{v.NodeLen:0.0} V={v.V:0.0} ang={v.RingAngle:0.00}");
        if (Debug)
            for (int n = 0; n < inNode.Length; n++)
                foreach (var x in inNode[n])
                    foreach (var y in inNode[n])
                        if (x.Id < y.Id && TrajPoint(x, 0).DistanceTo(TrajPoint(y, 0)) < 5)
                            res.Events.Add($"DBG par {x.Id}×{y.Id} ({x.NodeS:0.0} / {y.NodeS:0.0}): " +
                                string.Join("; ", RunsOf(x.Mov!.Value, y.Mov!.Value).Select(r => $"A {r.AIn:0.0}-{r.AOut:0.0} B {r.BIn:0.0}-{r.BOut:0.0} {(r.Parallel ? "par" : "cruz")}")));
        if (Debug) foreach (var (k, n) in why.OrderBy(x => x.Key)) res.Events.Add($"DBG motivo {k}: {n}");
        if (Debug && headways.Count > 0)
        {
            headways.Sort();
            res.Events.Add($"DBG intervalo de entrada no nó (fila): mediana {headways[headways.Count / 2]:0.00} s, p25 {headways[headways.Count / 4]:0.00} s, n={headways.Count}");
        }
        res.InNetwork = vehicles.Count;
        res.Backlog = queues.Values.Sum(q => q.Count);
        res.MeanTravelTime = res.Completed > 0 ? sumTT / res.Completed : 0;
        res.MeanDelay = res.Completed > 0 ? sumDelay / res.Completed : 0;
        var appr = res.Approaches.Values.Where(a => a.Vehicles > 0).ToList();
        res.StopsPerTrip = res.Completed > 0 ? appr.Sum(a => a.Stops) / (double)res.Completed : 0;
        if (res.LaneChanges > 0) res.Events.Add($"{res.LaneChanges} troca(s) de faixa durante a medição.");
        if (res.BusStopsServed > 0) res.Events.Add($"{res.BusStopsServed} parada(s) de ônibus atendidas (embarque de {opt.BusDwell:0} s em média).");
        if (res.Backlog > 20) res.Events.Add($"{res.Backlog} veículo(s) não conseguiram entrar na rede (entradas bloqueadas por filas).");
        return res;

        void Why(SimVehicle v, string r) { if (Debug) { var k = $"{v.Link}:{r}"; why[k] = why.GetValueOrDefault(k) + 1; } }

        // Faixa de saída do movimento pela geometria (sem trajetos que se cruzam dentro do nó): à direita para a faixa da
        // direita, à esquerda/retorno para a da esquerda, em frente na faixa correspondente; rotatória sai pela da direita.
        // A preparação para a conversão seguinte é feita depois, com troca de faixa no trecho.
        int PlanLane(SimVehicle v)
        {
            if (v.NextLane >= 0 || v.Step + 1 >= v.Route.Count) return Math.Max(0, v.NextLane);
            var from = net.Links[v.Link];
            var to = net.Links[v.Route[v.Step + 1]];
            var g = TrafficAnalysis.TurnOf(net, from.Index, to.Index);
            var lane = net.Nodes[from.To].Kind == TipoNo.Rotatoria ? 0 : g switch
            {
                Giro.Direita => 0,
                Giro.Esquerda or Giro.Retorno => to.Lanes - 1,
                _ => Math.Min(v.Lane, to.Lanes - 1),
            };
            lane = Math.Clamp(lane, 0, to.Lanes - 1);
            if (to.ClosedLanes.Any(c => c.Lane == lane && c.S0 < 30))
            {
                var open = Enumerable.Range(0, to.Lanes).Where(k => !to.ClosedLanes.Any(c => c.Lane == k && c.S0 < 30)).OrderBy(k => Math.Abs(k - lane)).ToList();
                if (open.Count > 0) lane = open[0];
            }
            v.NextLane = lane;
            return lane;
        }

        // Menor caminho (tempo livre) entre dois trechos, respeitando as conversões permitidas.
        List<int>? PathFrom(int start, int dest)
        {
            if (pathCache.TryGetValue((start, dest), out var cached)) return cached;
            var dist = new Dictionary<int, double> { [start] = 0 };
            var prev = new Dictionary<int, int>();
            var pq = new PriorityQueue<int, double>();
            pq.Enqueue(start, 0);
            while (pq.TryDequeue(out var a, out var da))
            {
                if (a == dest) break;
                if (da > dist[a] + 1e-9) continue;
                var nd = net.Nodes[net.Links[a].To];
                if (nd.IsZone) continue;
                foreach (var b in nd.Out)
                {
                    var lb = net.Links[b];
                    if (lb.Closed || !TrafficAnalysis.Allowed(net, nd, a, b)) continue;
                    var nb = da + lb.Length / Math.Max(1, lb.FreeSpeed) + 3;
                    if (!dist.TryGetValue(b, out var db) || nb < db - 1e-9) { dist[b] = nb; prev[b] = a; pq.Enqueue(b, nb); }
                }
            }
            List<int>? res = null;
            if (dist.ContainsKey(dest))
            {
                res = new List<int> { dest };
                while (res[^1] != start) res.Add(prev[res[^1]]);
                res.Reverse();
            }
            pathCache[(start, dest)] = res;
            return res;
        }

        // Faixa aceita o movimento? (setas pintadas; sem setas: direita pela da direita, esquerda pela da esquerda).
        bool LaneOk(TrafficLink l, int k, Giro g)
        {
            if (l.LaneTurns.Count > 0) return l.TurnsOf(k) is not { } set || set.Contains(g) || (g == Giro.Retorno && set.Contains(Giro.Esquerda));
            if (l.Lanes <= 1) return true;
            return g switch { Giro.Direita => k == 0, Giro.Esquerda or Giro.Retorno => k == l.Lanes - 1, _ => true };
        }

        bool Reroute(SimVehicle v, TrafficLink l, TrafficNode nd)
        {
            var dest = v.Route[^1];
            List<int>? best = null;
            var bestLen = double.MaxValue;
            foreach (var o in nd.Out)
            {
                if (o == v.Route[v.Step + 1] || net.Links[o].Closed || !TrafficAnalysis.Allowed(net, nd, l.Index, o)) continue;
                if (!LaneOk(l, v.Lane, TrafficAnalysis.TurnOf(net, l.Index, o))) continue;
                var p = PathFrom(o, dest);
                if (p == null) continue;
                var len = p.Sum(x => net.Links[x].Length);
                if (len < bestLen) { bestLen = len; best = p; }
            }
            if (best == null) return false;
            v.Route = v.Route.Take(v.Step + 1).Concat(best).ToList();
            v.NextLane = -1;
            v.Mov = null;
            v.WantsLane = -1;
            return true;
        }

        // Traseira (estaca no trecho) do veículo que entrou no nó por esta faixa e ainda não saiu totalmente dela.
        double? RearOnApproach(TrafficLink l, int lane)
        {
            double? rear = null;
            foreach (var x in inNode[l.To])
                if (x.Mov is { } m && m.From == l.Index && m.FromLane == lane && x.NodeS - x.Length < 0)
                {
                    var r = EndOf(l) + x.NodeS - x.Length;
                    if (rear == null || r < rear) rear = r;
                }
            return rear;
        }

        // Ponto da trajetória do veículo a uma distância para trás da frente (nó, aproximação, trajeto anterior ou trecho).
        Vec2 TrajPoint(SimVehicle v, double back)
        {
            Vec2 OnLink(TrafficLink l, double t, double off)
            {
                // Antes do início do trecho (veículo que acabou de entrar na rede): prolonga pela tangente.
                var extra = Math.Min(0, t);
                t = Math.Clamp(t, 0, l.Length);
                var d = l.Path.TangentAt(t);
                return l.Path.PointAt(t) + d * extra + new Vec2(d.Y, -d.X) * off;
            }
            if (v.InNode && v.Mov is { } m)
            {
                var s = v.NodeS - back;
                if (s >= 0) { var pl = PathOf(m).Line; return pl.PointAt(Math.Min(s, pl.Length)); }
                var l = net.Links[m.From];
                return OnLink(l, EndOf(l) + s, l.LaneOffset(m.FromLane));
            }
            var lk = net.Links[v.Link];
            var sl = v.S - back;
            if (sl < startClear[lk.Index] && v.PrevMov is { } pm)
            {
                var pl = PathOf(pm).Line;
                var t = pl.Length + sl - startClear[lk.Index];
                if (t >= 0) return pl.PointAt(Math.Min(t, pl.Length));
                var fl = net.Links[pm.From];
                return OnLink(fl, EndOf(fl) + t, fl.LaneOffset(pm.FromLane));
            }
            return OnLink(lk, sl, lk.LaneOffset(v.Lane) + v.LaneShift + (v.InBay ? 3.0 : 0));
        }

        // ---------------------------------------------------- regras de passagem no nó
        bool CanProceed(SimVehicle v, double t, bool atLine)
        {
            var link = v.Link;
            var l = net.Links[link];
            var nd = net.Nodes[l.To];
            if (nd.IsZone && v.Step + 1 >= v.Route.Count) return true;
            if (v.Step + 1 >= v.Route.Count) return true;
            var to = v.Route[v.Step + 1];
            var g = TrafficAnalysis.TurnOf(net, link, to);
            // Na faixa errada para o movimento (não conseguiu trocar antes): espera na linha pela troca – nunca converte
            // cruzando a faixa ao lado.
            if (l.Lanes > 1 && v.Type != 2 && !Allowed(v, l).Contains(v.Lane))
            {
                // Parado há muito na faixa errada: desiste da conversão, segue pela faixa em que está e recalcula a rota.
                if (atLine && v.StoppedFor > 15 && Reroute(v, l, nd)) return false;
                // Sem rota alternativa: depois de muito tempo converte com cuidado (os conflitos no nó impedem o choque).
                if (!(atLine && v.StoppedFor > 30)) { Why(v, "faixa"); return false; }
            }
            // Espaço no trecho de saída para quem já está no nó e para este veículo (CTB art. 45: não bloquear o cruzamento).
            var outL = net.Links[to];
            var outLane = Math.Clamp(PlanLane(v), 0, outL.Lanes - 1);
            var tail = Tail(to, outLane);
            // Só a fila parada (ou lenta) na saída conta: atrás de quem está andando sempre há lugar (o seguimento no nó
            // mantém a distância).
            var room = EndOf(outL) - startClear[outL.Index];
            SimVehicle? jam = null;
            foreach (var x in tail) if (x.V < 3 && (jam == null || x.S < jam.S)) jam = x;
            if (jam != null) room = jam.S - jam.Length - startClear[outL.Index];
            if (jam != null) foreach (var x in inNode[nd.Index]) if (x.ToLink == to && x.Lane == outLane) room -= x.Length + 2;
            if (room < v.Length + 2.0) { Why(v, "espaco"); return false; }
            switch (nd.Control)
            {
                case ControleNo.Livre:
                    return true;
                case ControleNo.Semaforo:
                {
                    // Aproximando: só segue se estiver verde com folga para cruzar; à esquerda cede ao sentido oposto.
                    var left = g is Giro.Esquerda or Giro.Retorno;
                    var green = GreenFor(nd.Index, link, g, t, atLine ? 0 : 2.5);
                    if (green == 0)
                    {
                        // Conversão à esquerda que esperava na linha sai no começo do entreverdes (o oposto já está parando).
                        if (atLine && left && v.StoppedFor > 2 && JustEnded(nd.Index, link, g, t, 4.0)) return true;
                        Why(v, "vermelho");
                        return false;
                    }
                    // Conversão à direita cede aos pedestres que atravessam no começo do verde (faixas de pedestres no cruzamento).
                    if (g == Giro.Direita && nd.Crosswalks && opt.PedestriansPerHour > 0 && GreenPhase(nd.Index, t) is var kp && kp >= 0)
                    {
                        var p = plans[nd.Index];
                        var sinceStart = InCycle(p, t) - p.Phases[kp].Start;
                        var block = Math.Min(p.Phases[kp].G * 0.6, 2 + opt.PedestriansPerHour / 60.0);
                        if (sinceStart < block) { Why(v, "pedestre"); return false; }
                    }
                    // Conversão à esquerda permitida: cede ao sentido oposto que está com verde (quem espera muito aceita brechas menores).
                    if (left && green == 1 && !Clear(v, nd, link, OpposingThrough(nd, link).Where(b => IsGreen(nd.Index, b, t, 0)).ToList(), v.StoppedFor > 20 ? 2.5 : 4.5))
                    { Why(v, "esq"); return false; }
                    return true;
                }
                case ControleNo.Rotatoria:
                {
                    // Entrada "rolando": perto da linha de "Dê a preferência" segue sem parar se o anel estiver livre
                    // pelo tempo de chegar à linha mais a brecha crítica.
                    var toLine = Math.Max(0, EndOf(l) - v.S);
                    if (!atLine && toLine > 25) return false;
                    var tcR = 3.2 + (atLine ? 0 : toLine / Math.Max(3, v.V));
                    var ang = Math.Atan2(l.Path.Points[^1].Y - nd.Pos.Y, l.Path.Points[^1].X - nd.Pos.X);
                    var R = RingRadius(nd);
                    var ringLanes = Math.Max(1, nd.RoundaboutLanes);
                    // Anel cheio: com os veículos parados à distância mínima em toda a volta ninguém mais sai (travamento
                    // circular). Quem entra respeita a ocupação – no máximo ~80 % da volta por faixa do anel.
                    var occupied = inNode[nd.Index].Sum(o => o.Length + 2.5);
                    if (occupied + v.Length + 2.5 > 0.8 * 2 * Math.PI * R * ringLanes) { Why(v, "anel cheio"); return false; }
                    foreach (var o in inNode[nd.Index])
                    {
                        if (double.IsNaN(o.RingAngle)) continue;
                        if (o.FromLink == link)
                        {
                            // Anel de uma faixa: as duas faixas da mesma entrada entram alternadas (afunilamento), nunca lado a lado.
                            if (ringLanes <= 1 && o.NodeS < o.Length + 3) { Why(v, "anel"); return false; }
                            continue;
                        }
                        var d = ang - o.RingAngle;                          // quanto falta para ele passar em frente (anti-horário)
                        while (d < 0) d += 2 * Math.PI;
                        while (d >= 2 * Math.PI) d -= 2 * Math.PI;
                        // Passando pela entrada agora – ou parado logo depois dela sem deixar lugar para este veículo inteiro.
                        var past = (2 * Math.PI - d) * R;
                        if (past < o.Length + 1 || o.V < 3 && past < o.Length + v.Length + 3) { Why(v, "anel"); return false; }
                        // Sai do anel antes de passar por esta entrada (ângulo da saída dele antes do desta entrada).
                        var e = Math.Atan2(o.NodePath![^1].Y - nd.Pos.Y, o.NodePath[^1].X - nd.Pos.X) - o.RingAngle;
                        while (e < 0) e += 2 * Math.PI;
                        while (e >= 2 * Math.PI) e -= 2 * Math.PI;
                        if (o.NodeLen - o.NodeS < 15 && e > Math.PI) e = 0;   // já no ramo de saída
                        if (e < d) continue;
                        if (d * R / Math.Max(3, o.V) < tcR) { Why(v, "anel"); return false; }
                    }
                    return true;
                }
                default:
                {
                    var isMajor = major[nd.Index].Contains(link);
                    var toLine = Math.Max(0, EndOf(l) - v.S);
                    // PARE: parada obrigatória na linha; nos demais, segue sem parar se houver brecha até chegar à linha.
                    var mustStop = nd.StopApproaches.Contains(link) || (nd.Control == ControleNo.Pare && !isMajor && !nd.YieldApproaches.Contains(link));
                    if (mustStop && (!atLine || v.StoppedFor < 1.0)) return false;
                    if (!atLine && !isMajor && toLine > 25) return false;
                    List<int> pri;
                    if (isMajor)
                    {
                        if (g is not (Giro.Esquerda or Giro.Retorno)) return true;
                        pri = OpposingThrough(nd, link);
                    }
                    else if (major[nd.Index].Count > 0) pri = major[nd.Index].ToList();
                    else
                    {
                        var d = EndDir(link);
                        pri = nd.In.Where(b => b != link && d.Cross(EndDir(b)) > 0.3).ToList();
                        if (g == Giro.Esquerda) pri.AddRange(nd.In.Where(b => b != link && d.Dot(EndDir(b)) < -0.5));
                    }
                    var tc = g == Giro.Direita ? 5.2 : g == Giro.Frente ? 6.0 : 6.5;     // brechas críticas próximas das do HCM
                    // Espera longa: alguém cede (evita o impasse da preferência à direita nos quatro ramos).
                    if (v.StoppedFor > 15) tc = 1.5;
                    if (!atLine) tc += toLine / Math.Max(3, v.V);
                    return Clear(v, nd, link, pri, tc);
                }
            }
        }

        List<int> OpposingThrough(TrafficNode nd, int link)
        {
            var d = EndDir(link);
            return nd.In.Where(b => b != link && d.Dot(EndDir(b)) < -0.7).ToList();
        }

        // Nenhum veículo das aproximações prioritárias chegando em menos de tc segundos, nem cruzando o nó vindo delas.
        bool Clear(SimVehicle me, TrafficNode nd, int myLink, List<int> priority, double tc)
        {
            foreach (var o in inNode[nd.Index])
                if (priority.Contains(o.FromLink) && o.NodeS < o.NodeLen * 0.7) return false;
            foreach (var b in priority)
            {
                var lb = net.Links[b];
                for (int lane = 0; lane < lb.Lanes; lane++)
                {
                    if (!lanes.TryGetValue((b, lane), out var list) || list.Count == 0) continue;
                    foreach (var o in list)
                    {
                        var dist = EndOf(lb) - o.S;
                        if (dist > 120) continue;
                        var eta = dist / Math.Max(0.5, o.V);
                        if (o.V > 1.0 && eta < tc) return false;
                        // Preferência à direita (sem controle): quem está parado na linha à direita tem a vez – quem espera
                        // há mais tempo passa primeiro; empate decidido pelo número do veículo (sem impasse).
                        if (o.V <= 1.0 && dist < 2 && nd.Control == ControleNo.PreferenciaDireita
                            && (o.StoppedFor > me.StoppedFor + 0.25 || (Math.Abs(o.StoppedFor - me.StoppedFor) <= 0.25 && o.Id < me.Id))) return false;
                    }
                }
            }
            return true;
        }
    }

    private static double RingRadius(TrafficNode nd) => Math.Max(4, nd.RoundaboutRadius - Math.Max(3, nd.RoundaboutLanes * 2.5));

    /// <summary>Trajeto dentro do nó: curva de Bézier entre as faixas (ou arco anti-horário no anel da rotatória).</summary>
    private static List<Vec2> NodePath(TrafficNetwork net, TrafficNode nd, TrafficLink from, int fromLane, TrafficLink to, int toLane, Giro g,
        double endClear = 0, double startClear = 0)
    {
        var e0 = from.Length - endClear;
        var d0 = from.Path.TangentAt(e0);
        var d1 = to.Path.TangentAt(startClear);
        var p0 = from.Path.PointAt(e0) + new Vec2(d0.Y, -d0.X) * from.LaneOffset(fromLane);
        var p1 = to.Path.PointAt(startClear) + new Vec2(d1.Y, -d1.X) * to.LaneOffset(toLane);
        var pts = new List<Vec2>();
        if (nd.Kind == TipoNo.Rotatoria && nd.RoundaboutRadius > 3)
        {
            var R = RingRadius(nd);
            var a0 = Math.Atan2(p0.Y - nd.Pos.Y, p0.X - nd.Pos.X);
            var a1 = Math.Atan2(p1.Y - nd.Pos.Y, p1.X - nd.Pos.X);
            var span = a1 - a0;
            while (span <= 0.15) span += 2 * Math.PI;           // sempre anti-horário (retorno = volta completa)
            pts.Add(p0);
            var n = Math.Max(4, (int)(span * R / 2));
            for (int i = 0; i <= n; i++)
            {
                var a = a0 + span * i / n;
                pts.Add(nd.Pos + new Vec2(Math.Cos(a), Math.Sin(a)) * R);
            }
            pts.Add(p1);
            return pts;
        }
        // Controle da Bézier: encontro das tangentes (ou meio do caminho).
        var c = (p0 + p1) / 2;
        var den = d0.Cross(d1);
        if (Math.Abs(den) > 0.1)
        {
            var t = (p1 - p0).Cross(d1) / den;
            if (t > 0 && t < p0.DistanceTo(p1) * 2) c = p0 + d0 * t;
        }
        for (int i = 0; i <= 12; i++)
        {
            var u = i / 12.0;
            pts.Add(p0 * ((1 - u) * (1 - u)) + c * (2 * u * (1 - u)) + p1 * (u * u));
        }
        return pts;
    }
}
