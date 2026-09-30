using System.Globalization;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Resultado de várias rodadas da microssimulação (sementes diferentes): média e faixa (melhor–pior).</summary>
public sealed record MicroStat(double Mean, double Min, double Max, int Runs)
{
    public static MicroStat Of(IReadOnlyCollection<double> xs) => new(xs.Average(), xs.Min(), xs.Max(), xs.Count);
    public string Text(CultureInfo ci) => $"{Mean.ToString("0.0", ci)} [{Min.ToString("0.0", ci)}–{Max.ToString("0.0", ci)}]";
}

/// <summary>Uma solução testada: o que muda no cenário e o efeito medido no nó/trecho e na rede.</summary>
public sealed class SolutionTrial
{
    public string Title { get; init; } = "";
    /// <summary>Tipo da medida (monta o pacote de intervenção): semaforo, pare, preferencia, rotatoria, retemporizar, ondaverde,
    /// proibiresquerda, bolsao, travessia-ciclo, travessia-sem.</summary>
    public string Kind { get; init; } = "";
    public int? Node { get; set; }
    public int? Link { get; set; }
    /// <summary>Outros nós envolvidos (semáforos da onda verde).</summary>
    public List<string> RelatedKeys { get; init; } = new();
    /// <summary>Tipo da rotatória testada (o mesmo que o pacote implanta).</summary>
    public Definitions.TipoRotatoria? RoundaboutType { get; init; }
    /// <summary>Pacote completo a aplicar no projeto (placas, marcas, semáforos, geometria) com as normas de cada item.</summary>
    public ProjectPackage? Package { get; set; }
    public string Description { get; init; } = "";
    /// <summary>O que fazer no projeto para implantar (ferramenta do plugin, sinalização a colocar).</summary>
    public string InProject { get; init; } = "";
    /// <summary>Aplica a solução nas opções do cenário.</summary>
    public required Action<TrafficOptions> Apply { get; init; }
    /// <summary>Atraso do local pela análise macroscópica (HCM), s/veh – referência; quem decide é a microssimulação.</summary>
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
    /// <summary>Microssimulação (s/veh, inclusive quem ficou na fila): atraso no local e na rede, antes e depois.</summary>
    public MicroStat? MicroLocalBefore { get; set; }
    public MicroStat? MicroLocalAfter { get; set; }
    public MicroStat? MicroNetBefore { get; set; }
    public MicroStat? MicroNetAfter { get; set; }
    /// <summary>O "depois" foi medido numa cópia do modelo com o pacote aplicado (a rede relida), e não só no cenário.</summary>
    public bool EvaluatedOnModel { get; set; }
    public bool Failed { get; set; }
    public string Error { get; set; } = "";

    /// <summary>Critério normativo não atendido (ex.: volumes que não justificam semáforo) – aparece, mas não é recomendada.</summary>
    public string? NormNote { get; set; }
    public bool MicroTested => MicroLocalBefore != null && MicroLocalAfter != null && MicroNetBefore != null && MicroNetAfter != null;
    /// <summary>
    /// Melhora de verdade, medida na microssimulação com várias sementes: o local melhora ≥ 10 % na média e não piora no
    /// pior caso, a rede melhora na média e não piora no pior caso, e as normas são atendidas. Sem microssimulação (análise
    /// rápida), vale a análise macroscópica: local ≥ 10 % melhor sem piorar a rede mais de 3 %.
    /// </summary>
    public bool Improves => !Failed && NormNote == null && (MicroTested
        ? MicroLocalAfter!.Mean <= MicroLocalBefore!.Mean * 0.9 && MicroLocalAfter.Max <= MicroLocalBefore.Max
          && MicroNetAfter!.Mean < MicroNetBefore!.Mean && MicroNetAfter.Max <= MicroNetBefore.Max
        : LocalAfter < LocalBefore * 0.9 && NetDelayAfter <= NetDelayBefore * 1.03 + 0.05);
    /// <summary>Ganho para ordenar: atraso médio na rede evitado (micro, s/veh) mais o ganho local ponderado; ou o macro.</summary>
    public double Score => Failed ? double.NegativeInfinity : MicroTested
        ? (MicroNetBefore!.Mean - MicroNetAfter!.Mean) + (MicroLocalBefore!.Mean - MicroLocalAfter!.Mean) / 10.0
        : (NetDelayBefore - NetDelayAfter) + (LocalBefore - LocalAfter) / 100.0;

    public string Summary(CultureInfo? ci = null)
    {
        ci ??= CultureInfo.GetCultureInfo("pt-BR");
        if (Failed) return $"✖ {Title}: não foi possível testar ({Error}).";
        var better = MicroTested ? MicroLocalAfter!.Mean <= MicroLocalBefore!.Mean : LocalAfter <= LocalBefore;
        var mark = Improves ? "✔" : better ? "≈" : "✖";
        var norm = NormNote != null ? $" ⚠ {NormNote}" : "";
        var micro = MicroTested
            ? $"microssimulação{(EvaluatedOnModel ? " no modelo com o pacote aplicado" : "")} ({MicroLocalAfter!.Runs} rodadas, média [melhor–pior]): atraso local {MicroLocalBefore!.Text(ci)} → {MicroLocalAfter.Text(ci)} s/veh; " +
              $"rede {MicroNetBefore!.Text(ci)} → {MicroNetAfter!.Text(ci)} s/veh. HCM: "
            : "";
        return $"{mark} {Title}{norm}: {micro}atraso local {LocalBefore.ToString("0.0", ci)} → {LocalAfter.ToString("0.0", ci)} s/veh ({LosBefore} → {LosAfter}); " +
               $"rede {NetDelayBefore.ToString("0.0", ci)} → {NetDelayAfter.ToString("0.0", ci)} veh·h/h, {SpeedBefore:0} → {SpeedAfter:0} km/h; " +
               $"críticos {CriticalBefore} → {CriticalAfter}.";
    }
}

/// <summary>
/// Soluções testadas para um problema do diagnóstico: gera alternativas aplicáveis ao cenário (controle do cruzamento,
/// retemporização, coordenação, proibição de conversões, tempos da travessia) e roda cada uma na análise (HCM) e na
/// microssimulação com várias sementes, medindo o efeito no local e na rede inteira – para recomendar só o que de fato
/// limpa o tráfego. O cenário de cada solução é o mesmo que o pacote implanta no projeto (rotatória do tipo e raio do
/// pacote, travessias, bolsões, plano gravado com defasagem e entreverdes).
/// </summary>
public static class TrafficSolutions
{
    /// <summary>Rodadas (sementes) da microssimulação por alternativa.</summary>
    public const int DefaultSeeds = 3;

    /// <param name="seeds">Rodadas da microssimulação por alternativa (0 = só a análise macroscópica).</param>
    /// <param name="model">
    /// Cópia do modelo: cada pacote é aplicado nela pela mesma regra do plugin e a rede relida é que é simulada – a solução
    /// avaliada é exatamente a implantada. Sem ela, vale o cenário equivalente (controle, rotatória, bolsões, travessias).
    /// </param>
    public static List<SolutionTrial> For(TrafficNetwork net, TrafficOptions opt, TrafficResult baseRes, int? nodeIndex, int? linkIndex,
        int seeds = DefaultSeeds, ModelSnapshot? model = null, CancellationToken cancel = default)
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
        // Antes: a rede como está (a do modelo copiado, quando há – a mesma leitura do "depois").
        TrafficNetwork? modelBase = model != null && seeds > 0 && list.Count > 0 ? model.Network(null) : null;
        (MicroStat Local, MicroStat Net)? microBefore = null;
        if (seeds > 0 && list.Count > 0)
            microBefore = modelBase != null && Map(modelBase, nd, lk) is var mb && (mb.Node != null || mb.Link != null)
                ? Micro(modelBase, opt, mb.Node, mb.Link, seeds, cancel)
                : Micro(net, opt, nd?.Index, lk?.Index, seeds, cancel);
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
                s.Node = nd?.Index;
                s.Link = lk?.Index;
                s.Package = TrafficPackages.For(net, r, s);
                if (nd != null) s.NormNote = Warrant(net, baseRes, nd, s.Kind);
                s.LocalAfter = Local(r);
                s.LosAfter = Los(r);
                s.NetDelayAfter = r.TotalDelayH;
                s.SpeedAfter = r.AvgSpeed;
                s.CriticalAfter = Crit(r);
                if (microBefore != null)
                {
                    (s.MicroLocalBefore, s.MicroNetBefore) = microBefore.Value;
                    if (modelBase != null && s.Package is { ScenarioOnly: false } pk)
                    {
                        // Depois: o modelo com o pacote aplicado, relido, com o cenário que fica depois de aplicar.
                        var an = model!.Network(pk);
                        var oa = Clone(opt);
                        AfterApply(oa, s);
                        var ma = Map(an, nd, lk);
                        if (ma.Node != null || ma.Link != null)
                        {
                            (s.MicroLocalAfter, s.MicroNetAfter) = Micro(an, oa, ma.Node, ma.Link, seeds, cancel);
                            s.EvaluatedOnModel = true;
                        }
                    }
                    if (!s.EvaluatedOnModel) (s.MicroLocalAfter, s.MicroNetAfter) = Micro(net, o, nd?.Index, lk?.Index, seeds, cancel);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { s.Failed = true; s.Error = ex.Message; }
        }
        // A rede é compartilhada: volta ao cenário original (controles e travessias do cenário atual).
        TrafficAnalysis.Run(net, opt);
        return list.OrderByDescending(s => s.Improves).ThenByDescending(s => s.Score).ToList();
    }

    /// <summary>
    /// Microssimulação do cenário com <paramref name="seeds"/> sementes (a do cenário e as seguintes): atraso médio no local
    /// (aproximações do nó, ou o trecho) e na rede – contando quem ficou na fila e quem não conseguiu entrar.
    /// </summary>
    public static (MicroStat Local, MicroStat Net) Micro(TrafficNetwork net, TrafficOptions opt, int? node, int? link, int seeds = DefaultSeeds,
        CancellationToken cancel = default)
    {
        var loc = new List<double>();
        var all = new List<double>();
        for (int k = 0; k < Math.Max(1, seeds); k++)
        {
            cancel.ThrowIfCancellationRequested();
            var o = Clone(opt);
            o.Seed = opt.Seed + k;
            // A análise e a simulação andam juntas: a rede guarda o estado do cenário da última análise.
            var r = TrafficAnalysis.Run(net, o);
            var sim = TrafficSimulation.Run(r, cancel);
            loc.Add(LocalDelay(net, sim, node, link));
            all.Add(sim.NetworkDelay);
        }
        return (MicroStat.Of(loc), MicroStat.Of(all));
    }

    /// <summary>
    /// O nó e o trecho do problema noutra leitura da rede (cópia do modelo): o nó pela chave (ou, se virou rotatória, o nó
    /// mais próximo do mesmo lugar) e o trecho pela via, sentido e posição.
    /// </summary>
    public static (int? Node, int? Link) Map(TrafficNetwork other, TrafficNode? nd, TrafficLink? lk)
    {
        int? node = null, link = null;
        if (nd != null)
            node = (other.Nodes.FirstOrDefault(n => n.Key == nd.Key)
                    ?? other.Nodes.Where(n => !n.IsZone && n.Pos.DistanceTo(nd.Pos) < 30).OrderBy(n => n.Pos.DistanceTo(nd.Pos)).FirstOrDefault())?.Index;
        if (lk != null)
        {
            var mid = lk.Path.PointAt(lk.Length / 2);
            link = other.Links.Where(l => l.Road.Id == lk.Road.Id && l.Forward == lk.Forward)
                .OrderBy(l => Math.Abs(l.Path.Project(mid).Signed) + Math.Max(0, -l.Path.Project(mid).Station) + Math.Max(0, l.Path.Project(mid).Station - l.Length))
                .FirstOrDefault()?.Index;
        }
        return (node, link);
    }

    /// <summary>Atraso médio (s/veh) nas aproximações do nó (ou no trecho), com a fila que não escoou.</summary>
    public static double LocalDelay(TrafficNetwork net, SimResult sim, int? node, int? link)
    {
        var apps = node is { } n ? net.Nodes[n].In.Select(i => sim.Approaches.GetValueOrDefault(i)).Where(a => a != null).Cast<SimApproach>().ToList()
            : link is { } l && sim.Approaches.TryGetValue(l, out var sa) ? new List<SimApproach> { sa } : new List<SimApproach>();
        var nveh = apps.Sum(a => a.Vehicles + a.PendingVehicles);
        return nveh > 0 ? apps.Sum(a => a.DelaySum + a.PendingDelay) / nveh : 0;
    }

    /// <summary>
    /// O cenário depois que o pacote foi aplicado no projeto (o plugin faz isto ao aplicar): o que o pacote implantou passa
    /// a vir do modelo – saem do ajuste dos nós alterados o controle, os tempos, os bolsões, as proibições e a rotatória (a
    /// contagem de conversões fica) – e a programação da travessia semaforizada, que não é desenho, fica no cenário.
    /// </summary>
    public static void AfterApply(TrafficOptions scenario, SolutionTrial t)
    {
        var p = t.Package;
        if (p == null) return;
        var keys = new HashSet<string>(p.OtherPlans.Keys);
        if (p.NodeKey != null) keys.Add(p.NodeKey);
        foreach (var ov in scenario.Nodes.Where(o => keys.Contains(o.Node))) ov.ClearDesignFields();
        scenario.Nodes.RemoveAll(o => keys.Contains(o.Node) && o.IsEmpty);
        if (t.Kind is "travessia-ciclo" or "travessia-sem")
        {
            var o = Clone(scenario);
            t.Apply(o);
            scenario.Crossings.Clear();
            scenario.Crossings.AddRange(o.Crossings);
        }
    }

    /// <summary>
    /// Critérios de implantação (MBST Vol. V – semáforo por volume veicular; rotatória com volumes equilibrados; PARE só
    /// abaixo da capacidade das brechas). Nulo = atende.
    /// </summary>
    public static string? Warrant(TrafficNetwork net, TrafficResult res, TrafficNode nd, string kind)
    {
        var nr = res.Nodes[nd.Index];
        var byRoad = nr.Approaches.GroupBy(a => net.Links[a.Link].Road.Id).Select(g => g.Sum(a => a.Volume)).OrderByDescending(v => v).ToList();
        var major = byRoad.FirstOrDefault();
        var minor = byRoad.Skip(1).FirstOrDefault();
        var minorApp = nr.Approaches.Where(a => nd.MainRoadId == null || net.Links[a.Link].Road.Id != nd.MainRoadId).Select(a => a.Volume).DefaultIfEmpty(0).Max();
        switch (kind)
        {
            case "semaforo":
                // Critério 1 (volume veicular mínimo): principal ≥ 500 veic/h (2 sentidos) e secundária ≥ 150 veic/h na aproximação mais carregada.
                if (major < 500 || minorApp < 150)
                    return $"volumes abaixo do critério do MBST Vol. V (principal {major:0} de 500 veic/h, secundária {minorApp:0} de 150 veic/h) – semáforo não justificado";
                return null;
            case "rotatoria":
                if (major + minor > 3600) return $"volume total {major + minor:0} veic/h acima da capacidade típica de rotatória urbana – só com duas faixas e estudo";
                return null;
            case "pare":
            case "preferencia":
                if (major > 1500 && minorApp > 250) return "principal muito carregada: a secundária não encontra brechas (considere semáforo ou rotatória)";
                return null;
            default:
                return null;
        }
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
                Title = "Semaforizar o cruzamento", Kind = "semaforo",
                Description = "Semáforo com tempos otimizados (Webster) e fase protegida de esquerda quando o volume pede.",
                InProject = "Interseção → controle \"Semáforo\" (ou \"Aplicar este controle no projeto\" na aba Cruzamentos) e gravar o plano.",
                // O pacote refaz a interseção com as travessias (FTP-1) e as linhas de retenção.
                Apply = o => { var ov = Ov(o, nd); ov.Control = ControleNo.Semaforo; ov.Cycle = null; ov.Greens = null; ov.Offset = null; ov.Crosswalks = true; },
            };
        if (cross && c is ControleNo.Livre or ControleNo.PreferenciaDireita && nd.MainRoadId != null)
        {
            yield return new SolutionTrial
            {
                Title = "PARE na via secundária", Kind = "pare",
                Description = "Define a preferência da via principal (R-1 e linha de retenção na secundária).",
                InProject = "Interseção → controle \"PARE\".",
                Apply = o => Ov(o, nd).Control = ControleNo.Pare,
            };
            yield return new SolutionTrial
            {
                Title = "Dê a preferência na via secundária", Kind = "preferencia",
                Description = "R-2 e linha de dê a preferência: quem chega pela secundária não precisa parar se houver brecha.",
                InProject = "Interseção → controle \"Dê a preferência\".",
                Apply = o => Ov(o, nd).Control = ControleNo.DePreferencia,
            };
        }
        if (cross && c != ControleNo.Rotatoria && nd.RoadIds.Count >= 2)
        {
            var type = TrafficPackages.RoundaboutTypeFor(nr.Volume);
            yield return new SolutionTrial
            {
                Title = "Rotatória", Kind = "rotatoria", RoundaboutType = type,
                Description = $"Rotatória moderna {(type == Definitions.TipoRotatoria.DuasFaixas ? "de duas faixas" : "de uma faixa")} (preferência de quem circula): reduz conflitos e atrasos com volumes equilibrados.",
                InProject = "Ferramenta Rotatória, clicando no cruzamento (a interseção é substituída).",
                Apply = o => { var ov = Ov(o, nd); ov.Control = ControleNo.Rotatoria; ov.RoundaboutType = type; },
            };
        }
        if (c == ControleNo.Semaforo)
        {
            yield return new SolutionTrial
            {
                Title = "Retemporizar o semáforo", Kind = "retemporizar",
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
                    Title = $"Onda verde na {net.Road(main)?.Name ?? "via principal"}", Kind = "ondaverde", RelatedKeys = chain.Select(n => n.Key).ToList(),
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
                Title = "Proibir as conversões à esquerda", Kind = "proibiresquerda",
                Description = "Quem convertia à esquerda passa a usar outro caminho (quadra ao lado, retorno, rotatória): menos conflitos e menos fases.",
                InProject = "Interseção → desmarcar \"Conversões à esquerda permitidas\" (eixo contínuo pela boca e R-4a em cada aproximação).",
                Apply = o => Ov(o, nd).NoLeft = true,
            };
        if (cross && !nd.LeftPockets && nr.Approaches.Any(a => a.LeftVolume >= 60 && net.Links[a.Link].Road.TwoWay))
            yield return new SolutionTrial
            {
                Title = "Bolsões de conversão à esquerda", Kind = "bolsao",
                Description = "Faixa exclusiva para quem converte à esquerda: a espera pela brecha sai da faixa direta.",
                InProject = "Interseção → Tipo IV (bolsão de conversão à esquerda).",
                Apply = o => Ov(o, nd).LeftPockets = true,
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
                Title = "Travessia: ciclo curto e verde de pedestres mínimo", Kind = "travessia-ciclo",
                Description = $"Ciclo 60 s com {ped:0} s de verde de pedestres ({width:0.0} m a 1,2 m/s + 4 s): menos tempo parado para os veículos.",
                InProject = "Grupo focal da travessia: programar o controlador com os tempos do cenário.",
                Apply = o => Set(o, cp, 60, ped, true),
            };
            yield return new SolutionTrial
            {
                Title = "Travessia sem semáforo (teste)", Kind = "travessia-sem",
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
