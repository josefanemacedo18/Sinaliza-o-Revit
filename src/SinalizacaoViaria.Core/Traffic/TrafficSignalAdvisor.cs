using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Recomendação de tempos para um semáforo: ciclo, verdes, entreverdes, defasagem e o efeito esperado.</summary>
public sealed class SignalAdvice
{
    public string NodeKey { get; init; } = "";
    public string Label { get; init; } = "";
    public double Cycle { get; init; }
    public List<double> Greens { get; init; } = new();
    public List<string> PhaseNames { get; init; } = new();
    public double Intergreen { get; init; }
    public double Offset { get; init; }
    public double DelayBefore { get; init; }
    public double DelayAfter { get; init; }
    public string LosBefore { get; init; } = "";
    public string LosAfter { get; init; } = "";
    public double CycleBefore { get; init; }
    /// <summary>Justificativa (Webster, razões de fluxo críticas, verdes mínimos de pedestres, coordenação).</summary>
    public string Why { get; init; } = "";
}

/// <summary>
/// Recomendação de tempos semafóricos (Webster/HCM) para um semáforo, para todos ou para todos os cruzamentos
/// semaforizados de uma via – com ciclo comum e defasagens de onda verde ao longo dela. Os demais semáforos da rede ficam
/// como estão; o resultado vem com o antes × depois de cada cruzamento para decidir antes de aplicar.
/// </summary>
public static class TrafficSignalAdvisor
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Semáforos (no cenário) ao longo de uma via, na ordem do eixo.</summary>
    public static List<TrafficNode> SignalsOnRoad(TrafficResult res, string roadId)
    {
        var road = res.Network.Road(roadId);
        if (road == null) return new();
        return res.Nodes.Values.Where(n => n.Control == ControleNo.Semaforo && n.Phases.Count > 0 && n.Node.RoadIds.Contains(roadId))
            .Select(n => n.Node).OrderBy(n => road.Axis.Project(n.Pos).Station).ToList();
    }

    public static List<SignalAdvice> Recommend(TrafficNetwork net, TrafficOptions opt, IReadOnlyCollection<string> nodeKeys, string? coordinateRoadId = null)
    {
        var before = TrafficAnalysis.Run(net, opt);
        var targets = before.Nodes.Values.Where(n => nodeKeys.Contains(n.Node.Key) && n.Control == ControleNo.Semaforo && n.Phases.Count > 0).ToList();
        if (targets.Count == 0) return new();
        var keys = targets.Select(t => t.Node.Key).ToHashSet();

        // Cenário de trabalho: os alvos voltam a ser otimizados (sem ciclo/verdes/defasagem fixos nem plano gravado); os
        // demais semáforos ficam presos aos tempos atuais para não mudarem junto.
        var work = new TrafficScenario { Options = opt }.Clone().Options;
        work.OptimizeSignals = true;
        work.Coordinate = false;
        foreach (var nr in before.Nodes.Values.Where(n => n.Control == ControleNo.Semaforo && n.Phases.Count > 0))
        {
            var ov = work.Nodes.FirstOrDefault(o => o.Node == nr.Node.Key);
            if (ov == null) work.Nodes.Add(ov = new NodeOverride { Node = nr.Node.Key });
            if (keys.Contains(nr.Node.Key))
            {
                ov.Control ??= ControleNo.Semaforo;     // qualquer ajuste desliga o plano gravado deste nó
                ov.Cycle = null;
                ov.Greens = null;
                ov.Offset = null;
            }
            else
            {
                ov.Cycle ??= Math.Round(nr.Cycle);
                ov.Greens ??= nr.Phases.Select(p => Math.Round(p.Green)).ToList();
                ov.Offset ??= Math.Round(nr.Offset);
                ov.Intergreen ??= nr.Intergreen;
            }
        }
        var after = TrafficAnalysis.Run(net, work);

        // Coordenação ao longo da via: ciclo comum (o maior recomendado) e defasagens pela progressão no sentido de maior
        // volume (tempo de percurso entre as retenções à velocidade de progressão).
        var offsets = new Dictionary<string, double>();
        var note = "";
        if (coordinateRoadId != null && net.Road(coordinateRoadId) is { } road)
        {
            var chain = targets.Where(t => t.Node.RoadIds.Contains(road.Id)).Select(t => t.Node).OrderBy(n => road.Axis.Project(n.Pos).Station).ToList();
            if (chain.Count >= 2)
            {
                var C = Math.Clamp(chain.Max(n => after.Nodes[n.Index].Cycle), 45, 150);
                foreach (var n in chain) work.Nodes.First(o => o.Node == n.Key).Cycle = Math.Round(C);
                // Sentido de maior volume na via.
                var fwd = net.LinksOf(road).Where(l => l.Forward).Sum(l => after.Links[l.Index].Volume);
                var bwd = net.LinksOf(road).Where(l => !l.Forward).Sum(l => after.Links[l.Index].Volume);
                if (bwd > fwd) chain.Reverse();
                var v = opt.ProgressionSpeed > 5 ? opt.ProgressionSpeed / 3.6 : road.SpeedKmh / 3.6 * 0.9;
                var t = 0.0;
                for (int i = 0; i < chain.Count; i++)
                {
                    if (i > 0) t += chain[i - 1].Pos.DistanceTo(chain[i].Pos) / Math.Max(3, v);
                    offsets[chain[i].Key] = Math.Round(t % C);
                    work.Nodes.First(o => o.Node == chain[i].Key).Offset = offsets[chain[i].Key];
                }
                after = TrafficAnalysis.Run(net, work);
                note = $"Onda verde na {road.Name}: ciclo comum {C:0} s, progressão a {v * 3.6:0} km/h no sentido {(bwd > fwd ? "contrário ao" : "do")} eixo (maior volume).";
            }
        }

        var list = new List<SignalAdvice>();
        foreach (var b in targets)
        {
            var a = after.Nodes[b.Node.Index];
            var why = new StringBuilder();
            why.Append(CultureInfo.InvariantCulture, $"Webster: C₀ = (1,5·L + 5)/(1 − Y), com L = {a.Intergreen * Math.Max(2, a.Phases.Count):0} s de tempo perdido ");
            why.Append("e Y a soma das razões de fluxo críticas; verdes proporcionais à demanda de cada fase, respeitando os mínimos ");
            why.Append("(7 s de veículos e a travessia de pedestres a 1,2 m/s). ");
            foreach (var nt in a.Notes) why.Append(nt).Append(' ');
            if (offsets.ContainsKey(b.Node.Key)) why.Append(note);
            list.Add(new SignalAdvice
            {
                NodeKey = b.Node.Key, Label = b.Node.Label,
                Cycle = Math.Round(a.Cycle), Greens = a.Phases.Select(p => Math.Round(p.Green)).ToList(), PhaseNames = a.Phases.Select(p => p.Name).ToList(),
                Intergreen = a.Intergreen, Offset = offsets.GetValueOrDefault(b.Node.Key, 0),
                DelayBefore = b.Delay, DelayAfter = a.Delay, LosBefore = b.LOS, LosAfter = a.LOS, CycleBefore = b.Cycle,
                Why = why.ToString().Trim(),
            });
        }
        return list;
    }

    /// <summary>Aplica as recomendações no cenário (ciclo, verdes, entreverdes e defasagem fixos por semáforo).</summary>
    public static void Apply(TrafficOptions opt, IEnumerable<SignalAdvice> advice)
    {
        foreach (var a in advice)
        {
            var ov = opt.Nodes.FirstOrDefault(o => o.Node == a.NodeKey);
            if (ov == null) opt.Nodes.Add(ov = new NodeOverride { Node = a.NodeKey });
            ov.Cycle = a.Cycle;
            ov.Greens = a.Greens.ToList();
            ov.Intergreen = a.Intergreen;
            ov.Offset = a.Offset;
        }
    }

    public static string Text(IReadOnlyList<SignalAdvice> list)
    {
        if (list.Count == 0) return "Nenhum semáforo no escopo escolhido (o cruzamento precisa estar com controle \"Semáforo\" no cenário).";
        var sb = new StringBuilder();
        foreach (var a in list)
        {
            sb.AppendLine($"{a.Label}: ciclo {a.CycleBefore:0} → {a.Cycle:0} s · atraso {a.DelayBefore.ToString("0.0", Pt)} → {a.DelayAfter.ToString("0.0", Pt)} s/veh · nível {a.LosBefore} → {a.LosAfter}" +
                          (a.Offset > 0 ? $" · defasagem {a.Offset:0} s" : ""));
            for (int k = 0; k < a.Greens.Count; k++) sb.AppendLine($"   {a.PhaseNames[k]}: verde {a.Greens[k]:0} s + {a.Intergreen:0} s de entreverdes");
        }
        sb.AppendLine();
        sb.AppendLine(list[0].Why.Split("Onda verde")[0].Trim());
        var wave = list.Select(a => a.Why).FirstOrDefault(w => w.Contains("Onda verde"));
        if (wave != null) sb.AppendLine("Onda verde" + wave.Split("Onda verde")[1]);
        return sb.ToString();
    }
}
