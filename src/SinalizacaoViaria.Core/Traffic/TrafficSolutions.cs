using System.Globalization;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Uma solução testada: o que muda no cenário e o efeito medido no nó/trecho e na rede.</summary>
public sealed class SolutionTrial
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>O que fazer no projeto para implantar (ferramenta do plugin, sinalização a colocar).</summary>
    public string InProject { get; init; } = "";
    /// <summary>Aplica a solução nas opções do cenário.</summary>
    public required Action<TrafficOptions> Apply { get; init; }
    public double LocalBefore { get; set; }
    public double LocalAfter { get; set; }
    public string LosBefore { get; set; } = "";
    public string LosAfter { get; set; } = "";
    public double NetDelayBefore { get; set; }
    public double NetDelayAfter { get; set; }
    public double SpeedBefore { get; set; }
    public double SpeedAfter { get; set; }
    public int CriticalBefore { get; set; }
    public int CriticalAfter { get; set; }
    public bool Failed { get; set; }
    public string Error { get; set; } = "";

    /// <summary>Melhora de verdade: o local melhora ≥ 10 % sem piorar a rede mais de 3 %.</summary>
    public bool Improves => !Failed && LocalAfter < LocalBefore * 0.9 && NetDelayAfter <= NetDelayBefore * 1.03 + 0.05;
    /// <summary>Ganho para ordenar (atraso total da rede evitado, veh·h/h, mais o ganho local ponderado).</summary>
    public double Score => Failed ? double.NegativeInfinity : (NetDelayBefore - NetDelayAfter) + (LocalBefore - LocalAfter) / 100.0;

    public string Summary(CultureInfo? ci = null)
    {
        ci ??= CultureInfo.GetCultureInfo("pt-BR");
        if (Failed) return $"✖ {Title}: não foi possível testar ({Error}).";
        var mark = Improves ? "✔" : LocalAfter <= LocalBefore ? "≈" : "✖";
        return $"{mark} {Title}: atraso local {LocalBefore.ToString("0.0", ci)} → {LocalAfter.ToString("0.0", ci)} s/veh ({LosBefore} → {LosAfter}); " +
               $"rede {NetDelayBefore.ToString("0.0", ci)} → {NetDelayAfter.ToString("0.0", ci)} veh·h/h, {SpeedBefore:0} → {SpeedAfter:0} km/h; " +
               $"críticos {CriticalBefore} → {CriticalAfter}.";
    }
}

/// <summary>
/// Soluções testadas para um problema do diagnóstico: gera alternativas aplicáveis ao cenário (controle do cruzamento,
/// retemporização, coordenação, proibição de conversões, tempos da travessia) e roda a análise com cada uma, medindo o
/// efeito no local e na rede inteira – para recomendar só o que de fato limpa o tráfego.
/// </summary>
public static class TrafficSolutions
{
    public static List<SolutionTrial> For(TrafficNetwork net, TrafficOptions opt, TrafficResult baseRes, int? nodeIndex, int? linkIndex)
    {
        var list = new List<SolutionTrial>();
        TrafficNode? nd = nodeIndex is { } ni ? net.Nodes[ni] : null;
        TrafficLink? lk = linkIndex is { } li ? net.Links[li] : null;
        // Trecho saturado: quase sempre é o cruzamento de jusante que segura a fila.
        if (nd == null && lk != null && !net.Nodes[lk.To].IsZone) nd = net.Nodes[lk.To];
        if (nd != null && !nd.IsZone && nd.Kind != TipoNo.Continuacao) list.AddRange(NodeCandidates(net, opt, baseRes, nd));
        if (lk != null) list.AddRange(LinkCandidates(net, opt, lk));

        double Local(TrafficResult r) => nd != null ? r.Nodes[nd.Index].Delay : lk != null ? r.Links[lk.Index].TravelTime : r.TotalDelayH;
        string Los(TrafficResult r) => nd != null ? r.Nodes[nd.Index].LOS : lk != null ? r.Links[lk.Index].LOS : "-";
        int Crit(TrafficResult r) => r.Diagnostics.Count(d => d.Severity == Gravidade.Critico);
        foreach (var s in list)
        {
            s.LocalBefore = Local(baseRes);
            s.LosBefore = Los(baseRes);
            s.NetDelayBefore = baseRes.TotalDelayH;
            s.SpeedBefore = baseRes.AvgSpeed;
            s.CriticalBefore = Crit(baseRes);
            try
            {
                var o = Clone(opt);
                s.Apply(o);
                var r = TrafficAnalysis.Run(net, o);
                s.LocalAfter = Local(r);
                s.LosAfter = Los(r);
                s.NetDelayAfter = r.TotalDelayH;
                s.SpeedAfter = r.AvgSpeed;
                s.CriticalAfter = Crit(r);
            }
            catch (Exception ex) { s.Failed = true; s.Error = ex.Message; }
        }
        // A rede é compartilhada: volta ao cenário original (controles e travessias do cenário atual).
        TrafficAnalysis.Run(net, opt);
        return list.OrderByDescending(s => s.Improves).ThenByDescending(s => s.Score).ToList();
    }

    public static TrafficOptions Clone(TrafficOptions o)
    {
        var c = new TrafficScenario { Options = o }.Clone().Options;
        foreach (var (k, v) in o.ZoneVolumes) c.ZoneVolumes[k] = v;
        return c;
    }

    private static NodeOverride Ov(TrafficOptions o, TrafficNode nd)
    {
        var ov = o.Nodes.FirstOrDefault(x => x.Node == nd.Key);
        if (ov == null) o.Nodes.Add(ov = new NodeOverride { Node = nd.Key });
        return ov;
    }

    private static IEnumerable<SolutionTrial> NodeCandidates(TrafficNetwork net, TrafficOptions opt, TrafficResult res, TrafficNode nd)
    {
        var nr = res.Nodes[nd.Index];
        var c = nr.Control;
        var cross = nd.Kind is TipoNo.Intersecao or TipoNo.CruzamentoSemControle;
        if (cross && c != ControleNo.Semaforo)
            yield return new SolutionTrial
            {
                Title = "Semaforizar o cruzamento",
                Description = "Semáforo com tempos otimizados (Webster) e fase protegida de esquerda quando o volume pede.",
                InProject = "Interseção → controle \"Semáforo\" (ou \"Aplicar este controle no projeto\" na aba Cruzamentos) e gravar o plano.",
                Apply = o => { var ov = Ov(o, nd); ov.Control = ControleNo.Semaforo; ov.Cycle = null; ov.Greens = null; ov.Offset = null; },
            };
        if (cross && c is ControleNo.Livre or ControleNo.PreferenciaDireita && nd.MainRoadId != null)
        {
            yield return new SolutionTrial
            {
                Title = "PARE na via secundária",
                Description = "Define a preferência da via principal (R-1 e linha de retenção na secundária).",
                InProject = "Interseção → controle \"PARE\".",
                Apply = o => Ov(o, nd).Control = ControleNo.Pare,
            };
            yield return new SolutionTrial
            {
                Title = "Dê a preferência na via secundária",
                Description = "R-2 e linha de dê a preferência: quem chega pela secundária não precisa parar se houver brecha.",
                InProject = "Interseção → controle \"Dê a preferência\".",
                Apply = o => Ov(o, nd).Control = ControleNo.DePreferencia,
            };
        }
        if (cross && c != ControleNo.Rotatoria && nd.RoadIds.Count >= 2)
            yield return new SolutionTrial
            {
                Title = "Rotatória",
                Description = "Rotatória moderna (preferência de quem circula): reduz conflitos e atrasos com volumes equilibrados.",
                InProject = "Ferramenta Rotatória, clicando no cruzamento (a interseção é substituída).",
                Apply = o => Ov(o, nd).Control = ControleNo.Rotatoria,
            };
        if (c == ControleNo.Semaforo)
        {
            yield return new SolutionTrial
            {
                Title = "Retemporizar o semáforo",
                Description = "Ciclo e verdes recalculados pela demanda atual (Webster/HCM).",
                InProject = "Aba Cruzamentos → Recomendação de tempos → \"Aplicar e gravar no projeto\".",
                Apply = o =>
                {
                    var adv = TrafficSignalAdvisor.Recommend(net, o, new[] { nd.Key });
                    TrafficSignalAdvisor.Apply(o, adv);
                },
            };
            var main = nd.MainRoadId ?? nd.RoadIds.FirstOrDefault();
            if (main != null && TrafficSignalAdvisor.SignalsOnRoad(res, main) is { Count: >= 2 } chain)
                yield return new SolutionTrial
                {
                    Title = $"Onda verde na {net.Road(main)?.Name ?? "via principal"}",
                    Description = $"Ciclo comum e defasagens nos {chain.Count} semáforos da via: os pelotões chegam no verde.",
                    InProject = "Aba Cruzamentos → Recomendação de tempos → \"Todos os cruzamentos de uma via\" + onda verde → gravar no projeto.",
                    Apply = o =>
                    {
                        var adv = TrafficSignalAdvisor.Recommend(net, o, chain.Select(n => n.Key).ToList(), main);
                        TrafficSignalAdvisor.Apply(o, adv);
                    },
                };
        }
        if (cross && nr.Approaches.Any(a => a.LeftVolume >= 30))
            yield return new SolutionTrial
            {
                Title = "Proibir as conversões à esquerda",
                Description = "Quem convertia à esquerda passa a usar outro caminho (quadra ao lado, retorno, rotatória): menos conflitos e menos fases.",
                InProject = "Interseção → desmarcar \"Conversões à esquerda permitidas\" (eixo contínuo pela boca e R-4a em cada aproximação).",
                Apply = o => Ov(o, nd).NoLeft = true,
            };
    }

    private static IEnumerable<SolutionTrial> LinkCandidates(TrafficNetwork net, TrafficOptions opt, TrafficLink lk)
    {
        foreach (var cp in lk.CrossingPlans)
        {
            var width = lk.Road.CarriageWidth;
            var ped = Math.Ceiling(width / 1.2 + 4);
            yield return new SolutionTrial
            {
                Title = "Travessia: ciclo curto e verde de pedestres mínimo",
                Description = $"Ciclo 60 s com {ped:0} s de verde de pedestres ({width:0.0} m a 1,2 m/s + 4 s): menos tempo parado para os veículos.",
                InProject = "Grupo focal da travessia: programar o controlador com os tempos do cenário.",
                Apply = o => Set(o, cp, 60, ped, true),
            };
            yield return new SolutionTrial
            {
                Title = "Travessia sem semáforo (teste)",
                Description = "Faixa de pedestres com prioridade do pedestre (CTB art. 70) – só com volume de pedestres e velocidade baixos.",
                InProject = "Remover o grupo focal; manter a faixa, a placa A-32b e, se preciso, faixa elevada.",
                Apply = o => Set(o, cp, cp.Cycle, Math.Max(5, cp.Red - 4), false),
            };
        }
    }

    private static void Set(TrafficOptions o, CrossingPlan cp, double cycle, double ped, bool on)
    {
        var cs = o.Crossings.FirstOrDefault(c => new Geometry.Vec2(c.X, c.Y).DistanceTo(cp.Pos) < 8);
        if (cs == null) o.Crossings.Add(cs = new CrossingSignal { X = cp.Pos.X, Y = cp.Pos.Y, Offset = cp.Offset });
        cs.Cycle = cycle;
        cs.PedGreen = ped;
        cs.Clearance = 4;
        cs.Enabled = on;
    }
}
