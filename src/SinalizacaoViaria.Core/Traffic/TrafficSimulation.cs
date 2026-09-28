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
    public double NodeS, NodeLen, NodeSpeed;
    public int NodeIndex = -1;
    public int FromLink = -1, ToLink = -1;
    public double StoppedFor;        // s parado na linha de retenção
    public double Born, LinkEnter, FreeTime;
    public double RingAngle = double.NaN;   // rotatória: ângulo atual no anel
    public int Stops;
    public bool WasStopped;
    public int Link => Route[Step];
}

/// <summary>Quadro da animação: posições (x, y, rumo, tipo, velocidade, número) de todos os veículos e fases dos semáforos.</summary>
public sealed class SimFrame
{
    public const int Stride = 6;
    public double Time { get; init; }
    public float[] Data { get; init; } = Array.Empty<float>();   // Stride valores por veículo
    /// <summary>Nó → fase em verde (k ≥ 0) ou entreverdes depois da fase k (−(k + 2)).</summary>
    public Dictionary<int, int> SignalPhase { get; init; } = new();
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
        var queues = new Dictionary<int, Queue<SimVehicle>>();         // espera para entrar na rede (por trecho de origem)
        var lanes = new Dictionary<(int Link, int Lane), List<SimVehicle>>();
        List<SimVehicle> LaneList(int link, int lane) => lanes.TryGetValue((link, lane), out var l) ? l : lanes[(link, lane)] = new List<SimVehicle>();
        var none = new List<SimVehicle>();
        List<SimVehicle> Tail(int link, int lane) => lanes.TryGetValue((link, lane), out var l) ? l : none;
        // Todas as faixas existem desde o início (a lista de faixas não muda durante as varreduras).
        foreach (var l in net.Links) for (int k = 0; k < l.Lanes; k++) LaneList(l.Index, k);
        var inNode = net.Nodes.Select(_ => new List<SimVehicle>()).ToArray();
        foreach (var l in net.Links) res.Approaches[l.Index] = new SimApproach { Link = l.Index };

        // Plano semafórico: início de cada fase no ciclo (fases em sequência, 4 s de amarelo + vermelho geral).
        var plans = new Dictionary<int, (double C, List<(double Start, double G, HashSet<int> Links, SignalPhase Phase)> Phases)>();
        foreach (var nr in macro.Nodes.Values.Where(n => n.Node.Control == ControleNo.Semaforo && n.Phases.Count > 0))
        {
            var list = new List<(double, double, HashSet<int>, SignalPhase)>();
            var t0 = 0.0;
            foreach (var ph in nr.Phases) { list.Add((t0, ph.Green, ph.Links.ToHashSet(), ph)); t0 += ph.Green + 4; }
            plans[nr.Node.Index] = (Math.Max(nr.Cycle, t0), list);
        }
        var protectedLeft = macro.Nodes.Values.SelectMany(n => n.Approaches).Where(a => a.ProtectedLeft).Select(a => a.Link).ToHashSet();
        int GreenPhase(int node, double t)
        {
            if (!plans.TryGetValue(node, out var p)) return -1;
            var tc = t % p.C;
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
            var tc = t % p.C;
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
            if ((t % plans[node].C) >= ph.Start + ph.G - margin) return 0;
            // Protegido: fase só de esquerdas ou verde antecipado (o sentido oposto está no vermelho).
            return ph.Phase.LeftOnly || !OpposingThrough(net.Nodes[node], link).Any(ph.Links.Contains) ? 2 : 1;
        }
        bool IsGreen(int node, int link, double t, double margin) => GreenFor(node, link, Giro.Frente, t, margin) > 0;
        // Entreverdes logo após o verde desta aproximação (quem já está na linha termina a conversão).
        bool JustEnded(int node, int link, Giro g, double t, double within)
        {
            if (!plans.TryGetValue(node, out var p)) return false;
            var tc = t % p.C;
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
        foreach (var l in net.Links)
            for (int k = 0; k < l.Crosswalks.Count; k++)
            {
                var list = new List<(double, double)>();
                var t = 0.0;
                var dur = l.Crosswalks[k].Width / 1.2 + 2;
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
        int nextId = 0;
        double sumTT = 0, sumDelay = 0;
        var lastFrame = -1.0;

        int LaneFor(TrafficLink l, int? nextLink)
        {
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
                    Id = nextId++, Type = type, Length = type == 0 ? 4.5 : 12, A = type == 0 ? 1.3 : 0.8, B = 2.0, T = type == 0 ? 1.3 : 1.7,
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
                if (last != null && last.S < v.Length + 6) continue;
                q.Dequeue();
                v.Lane = lane;
                v.S = 0;
                v.V = Math.Min(l.FreeSpeed * 0.7, last == null ? l.FreeSpeed * 0.7 : Math.Max(0, last.V));
                v.LinkEnter = time;
                list.Add(v);
                vehicles.Add(v);
            }

            // ---------------------------------------------------- trechos
            foreach (var ((link, _), list) in lanes)
            {
                if (list.Count == 0) continue;
                var l = net.Links[link];
                list.Sort((a, b) => b.S.CompareTo(a.S));   // do mais à frente para o de trás
                for (int i = 0; i < list.Count; i++)
                {
                    var v = list[i];
                    var v0 = l.FreeSpeed * v.SpeedFactor;
                    foreach (var (at, sp, _) in l.SlowPoints)
                        if (v.S > at - 30 && v.S < at + 10) v0 = Math.Min(v0, sp);
                    // Obstáculo: veículo à frente, linha de retenção (se não puder seguir) ou pedestres na travessia.
                    var gap = double.PositiveInfinity;
                    var dv = 0.0;
                    if (i > 0)
                    {
                        var lead = list[i - 1];
                        gap = lead.S - lead.Length - v.S;
                        dv = v.V - lead.V;
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
                    if (i == 0)
                    {
                        // Obstáculo virtual 1,5 m além do fim: com a distância mínima do IDM (2 m) o veículo para rente à linha.
                        var stop = l.Length + 1.5;
                        var can = CanProceed(v, time, false);
                        if (!can)
                        {
                            var gp = stop - v.S;
                            if (gp < gap) { gap = gp; dv = v.V; }
                        }
                    }
                    var sStar = 2.0 + Math.Max(0, v.V * v.T + v.V * dv / (2 * Math.Sqrt(v.A * v.B)));
                    var acc = v.A * (1 - Math.Pow(v.V / Math.Max(0.5, v0), 4) - (double.IsInfinity(gap) ? 0 : Math.Pow(sStar / Math.Max(0.1, gap), 2)));
                    acc = Math.Max(acc, -8);
                    var nv = Math.Max(0, v.V + acc * Dt);
                    var ds = Math.Max(0, (v.V + nv) / 2 * Dt);
                    if (!double.IsInfinity(gap)) ds = Math.Min(ds, Math.Max(0, gap - 0.2 + (i == 0 ? 0.2 : 0)));
                    v.V = nv;
                    v.S += ds;
                    if (v.V < 0.3) { if (!v.WasStopped) { v.Stops++; v.WasStopped = true; } v.StoppedFor += Dt; }
                    else if (v.V > 3) v.WasStopped = false;
                }
            }

            // ---------------------------------------------------- entrada nos nós
            foreach (var ((link, lane), list) in lanes)
            {
                if (list.Count == 0) continue;
                var l = net.Links[link];
                var v = list.MaxBy(x => x.S)!;
                if (v.S < l.Length - 1.3) continue;
                if (!CanProceed(v, time, true)) continue;
                list.Remove(v);
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
                var g = TrafficAnalysis.TurnOf(net, link, to.Index);
                v.InNode = true;
                v.NodeIndex = nd.Index;
                v.FromLink = link;
                v.ToLink = to.Index;
                var toLane = PlanLane(v);
                v.NextLane = -1;
                v.NodePath = NodePath(net, nd, l, v.Lane, to, toLane, g);
                v.Lane = toLane;
                v.NodeS = 0;
                v.NodeLen = Math.Max(1, new Polyline2(v.NodePath).Length);
                if (nd.Kind == TipoNo.Rotatoria) v.RingAngle = Math.Atan2(v.NodePath[0].Y - nd.Pos.Y, v.NodePath[0].X - nd.Pos.X);
                v.NodeSpeed = nd.Kind == TipoNo.Rotatoria ? 7.5 : g == Giro.Frente ? Math.Min(l.FreeSpeed, to.FreeSpeed) : g == Giro.Direita ? 5.5 : 7.0;
                inNode[nd.Index].Add(v);
            }

            // ---------------------------------------------------- dentro dos nós
            for (int n = 0; n < inNode.Length; n++)
            {
                var list = inNode[n];
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var v = list[i];
                    v.V = Math.Min(v.NodeSpeed, v.V + 1.5 * Dt);
                    v.V = Math.Max(v.V, 2.0);
                    v.NodeS += v.V * Dt;
                    if (net.Nodes[n].Kind == TipoNo.Rotatoria && v.NodePath != null)
                    {
                        var p = new Polyline2(v.NodePath).PointAt(Math.Min(v.NodeS, v.NodeLen));
                        v.RingAngle = Math.Atan2(p.Y - net.Nodes[n].Pos.Y, p.X - net.Nodes[n].Pos.X);
                    }
                    if (v.NodeS < v.NodeLen) continue;
                    var to = net.Links[v.ToLink];
                    var target = LaneList(to.Index, v.Lane);
                    var last = target.Count == 0 ? null : target.MinBy(x => x.S);
                    if (last != null && last.S < last.Length + 1) { v.NodeS = v.NodeLen; v.V = 0; continue; }   // sem espaço: espera
                    list.RemoveAt(i);
                    v.InNode = false;
                    v.RingAngle = double.NaN;
                    v.Step++;
                    v.S = 0;
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
                    if (front.V > 1.5 || front.S < l.Length - 12) continue;
                    var back = front;
                    var n = 1;
                    for (int j = 1; j < list.Count; j++)
                    {
                        var x = list[j];
                        if (x.V > 3 || back.S - back.Length - x.S > 12) break;
                        back = x;
                        n++;
                    }
                    var q = l.Length - back.S + back.Length;
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
                    Vec2 p, d;
                    if (v.InNode && v.NodePath != null)
                    {
                        var pl = new Polyline2(v.NodePath);
                        var s = Math.Min(v.NodeS, pl.Length);
                        p = pl.PointAt(s);
                        d = pl.TangentAt(s);
                    }
                    else
                    {
                        var l = net.Links[v.Link];
                        var s = Math.Min(v.S, l.Length);
                        var c = l.Path.PointAt(s);
                        d = l.Path.TangentAt(s);
                        p = c + new Vec2(d.Y, -d.X) * l.LaneOffset(v.Lane);
                    }
                    data[k++] = (float)p.X;
                    data[k++] = (float)p.Y;
                    data[k++] = (float)Math.Atan2(d.Y, d.X);
                    data[k++] = v.Type;
                    data[k++] = (float)v.V;
                    data[k++] = v.Id;
                }
                var sig = plans.Keys.ToDictionary(nd => nd, nd => SignalState(nd, time));
                if (time >= opt.WarmupSeconds - 60) res.Frames.Add(new SimFrame { Time = time - opt.WarmupSeconds, Data = data, SignalPhase = sig });
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
        if (Debug) foreach (var (k, n) in why.OrderBy(x => x.Key)) res.Events.Add($"DBG motivo {k}: {n}");
        res.InNetwork = vehicles.Count;
        res.Backlog = queues.Values.Sum(q => q.Count);
        res.MeanTravelTime = res.Completed > 0 ? sumTT / res.Completed : 0;
        res.MeanDelay = res.Completed > 0 ? sumDelay / res.Completed : 0;
        var appr = res.Approaches.Values.Where(a => a.Vehicles > 0).ToList();
        res.StopsPerTrip = res.Completed > 0 ? appr.Sum(a => a.Stops) / (double)res.Completed : 0;
        if (res.Backlog > 20) res.Events.Add($"{res.Backlog} veículo(s) não conseguiram entrar na rede (entradas bloqueadas por filas).");
        return res;

        void Why(SimVehicle v, string r) { if (Debug) { var k = $"{v.Link}:{r}"; why[k] = why.GetValueOrDefault(k) + 1; } }

        int PlanLane(SimVehicle v)
        {
            if (v.NextLane < 0 && v.Step + 1 < v.Route.Count)
                v.NextLane = LaneFor(net.Links[v.Route[v.Step + 1]], v.Step + 2 < v.Route.Count ? v.Route[v.Step + 2] : null);
            return Math.Max(0, v.NextLane);
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
            // Espaço no trecho de saída para quem já está no nó e para este veículo (CTB art. 45: não bloquear o cruzamento).
            var outL = net.Links[to];
            var outLane = Math.Clamp(PlanLane(v), 0, outL.Lanes - 1);
            var tail = Tail(to, outLane);
            var room = outL.Length;
            if (tail.Count > 0)
            {
                var last = tail.MinBy(x => x.S)!;
                room = last.S - last.Length + (last.V > 2 ? last.V * 2 : 0);
            }
            foreach (var x in inNode[nd.Index]) if (x.ToLink == to && x.Lane == outLane) room -= x.Length + 2;
            if (room < v.Length + 1.5) { Why(v, "espaco"); return false; }
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
                    // Conversão à esquerda permitida: cede ao sentido oposto que está com verde (quem espera muito aceita brechas menores).
                    if (left && green == 1 && !Clear(v, nd, link, OpposingThrough(nd, link).Where(b => IsGreen(nd.Index, b, t, 0)).ToList(), v.StoppedFor > 20 ? 2.5 : 4.5))
                    { Why(v, "esq"); return false; }
                    return true;
                }
                case ControleNo.Rotatoria:
                {
                    // Entrada "rolando": perto da linha de "Dê a preferência" segue sem parar se o anel estiver livre
                    // pelo tempo de chegar à linha mais a brecha crítica.
                    var toLine = Math.Max(0, l.Length - v.S);
                    if (!atLine && toLine > 25) return false;
                    var tcR = 3.2 + (atLine ? 0 : toLine / Math.Max(3, v.V));
                    var ang = Math.Atan2(l.Path.Points[^1].Y - nd.Pos.Y, l.Path.Points[^1].X - nd.Pos.X);
                    var R = RingRadius(nd);
                    foreach (var o in inNode[nd.Index])
                    {
                        if (double.IsNaN(o.RingAngle) || o.FromLink == link) continue;   // a outra faixa da mesma entrada não conflita
                        var d = ang - o.RingAngle;                          // quanto falta para ele passar em frente (anti-horário)
                        while (d < 0) d += 2 * Math.PI;
                        while (d >= 2 * Math.PI) d -= 2 * Math.PI;
                        if ((2 * Math.PI - d) * R < o.Length + 1) { Why(v, "anel"); return false; }   // passando pela entrada agora
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
                    var toLine = Math.Max(0, l.Length - v.S);
                    // PARE: parada obrigatória na linha; nos demais, segue sem parar se houver brecha até chegar à linha.
                    if (nd.Control == ControleNo.Pare && !isMajor && (!atLine || v.StoppedFor < 1.0)) return false;
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
                        var dist = lb.Length - o.S;
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
    private static List<Vec2> NodePath(TrafficNetwork net, TrafficNode nd, TrafficLink from, int fromLane, TrafficLink to, int toLane, Giro g)
    {
        var d0 = from.Path.TangentAt(from.Length);
        var d1 = to.Path.TangentAt(0);
        var p0 = from.Path.PointAt(from.Length) + new Vec2(d0.Y, -d0.X) * from.LaneOffset(fromLane);
        var p1 = to.Path.PointAt(0) + new Vec2(d1.Y, -d1.X) * to.LaneOffset(toLane);
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
