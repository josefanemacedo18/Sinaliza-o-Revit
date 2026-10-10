using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Relatório do Simulador de Tráfego (texto para arquivo/cópia e tabela CSV das aproximações).</summary>
public static class TrafficReport
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string F(double v, string fmt = "0") => double.IsNaN(v) || double.IsInfinity(v) ? "–" : v.ToString(fmt, Pt);

    public static string KindLabel(TipoNo k) => k switch
    {
        TipoNo.Intersecao => "interseção",
        TipoNo.Rotatoria => "rotatória",
        TipoNo.Extremidade => "extremidade (entrada/saída)",
        TipoNo.CulDeSac => "balão de retorno",
        TipoNo.Continuacao => "emenda de vias",
        _ => "cruzamento sem interseção",
    };

    public static string ControlLabel(ControleNo c) => c switch
    {
        ControleNo.Pare => "PARE (R-1) na secundária",
        ControleNo.DePreferencia => "Dê a preferência (R-2) na secundária",
        ControleNo.Semaforo => "semáforo",
        ControleNo.Rotatoria => "rotatória (preferência de quem circula)",
        ControleNo.PreferenciaDireita => "sem sinalização (preferência de quem vem pela direita)",
        _ => "livre",
    };

    public static string ControlShort(ControleNo c) => c switch
    {
        ControleNo.Semaforo => "sem.",
        ControleNo.Pare => "PARE",
        ControleNo.DePreferencia => "DP",
        ControleNo.Rotatoria => "rot.",
        ControleNo.PreferenciaDireita => "dir.",
        _ => "livre",
    };

    public static string LosMeaning(string los) => los switch
    {
        "A" => "fluxo livre",
        "B" => "estável, pouca espera",
        "C" => "estável, espera aceitável",
        "D" => "próximo do limite, filas frequentes",
        "E" => "no limite da capacidade",
        "F" => "saturado, filas crescentes",
        _ => "sem tráfego",
    };

    /// <summary>De onde veio a via (relatório e mapa).</summary>
    public static string OriginLabel(OrigemTrecho o) => o switch
    {
        OrigemTrecho.Piso => "inferida de piso do Revit",
        OrigemTrecho.LinhaDeEixo => "inferida de linha de eixo",
        OrigemTrecho.Ambiguo => "inferida com suposições (ambígua)",
        _ => "via do plugin",
    };

    public static string Build(TrafficResult r, SimResult? sim = null, string? project = null)
    {
        var net = r.Network;
        var o = r.Options;
        var sb = new StringBuilder();
        void L(string s = "") => sb.AppendLine(s);
        void H(string s) { L(); L(s.ToUpperInvariant()); L(new string('─', Math.Min(100, s.Length + 4))); }

        L("SIMULADOR DE TRÁFEGO – SinalizaBIM");
        if (!string.IsNullOrWhiteSpace(project)) L($"Projeto: {project}");
        L($"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm}");
        L($"Cenário: demanda {o.DemandLabel}; veículos pesados {F(o.HeavyVehicles * 100)}%, ônibus {F(o.Buses * 100)}%, FHP {F(o.PeakHourFactor, "0.00")}, " +
          $"{F(o.PedestriansPerHour)} pedestres/h por travessia; semáforos {(o.OptimizeSignals ? "com ciclo otimizado (Webster)" : $"com ciclo fixo de {F(o.FixedCycle)} s")}" +
          (o.UseStoredPlans ? ", planos gravados nas interseções respeitados" : "") + (o.Coordinate ? $", coordenados (ciclo comum {F(r.CommonCycle)} s)" : "") + ".");
        var ovs = o.Nodes.Where(x => !x.IsEmpty).ToList();
        if (ovs.Count > 0)
            L($"Ajustes do cenário em {ovs.Count} cruzamento(s): " + string.Join("; ", ovs.Select(x =>
            {
                var nd = net.Nodes.FirstOrDefault(n => n.Key == x.Node);
                var parts = new List<string>();
                if (x.Control is { } c) parts.Add(ControlLabel(c));
                if (x.Cycle is { } cy) parts.Add($"ciclo {F(cy)} s");
                if (x.Greens is { Count: > 0 } gs) parts.Add("verdes " + string.Join("/", gs.Select(g => F(g))));
                if (x.Turns.Count > 0) parts.Add($"{x.Turns.Count} contagem(ns) de conversão");
                return $"{nd?.Label ?? x.Node} – {string.Join(", ", parts)}";
            })) + ".");

        // ------------------------------------------------------------------ resumo
        H("1. Resumo");
        var crit = r.Diagnostics.Count(d => d.Severity == Gravidade.Critico);
        var warn = r.Diagnostics.Count(d => d.Severity == Gravidade.Atencao);
        var nodes = r.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind != TipoNo.Continuacao && n.Volume > 0).ToList();
        var worst = nodes.OrderByDescending(n => n.Delay).FirstOrDefault();
        L($"Demanda total: {F(r.TotalDemand)} veículos/h entre {net.Nodes.Count(n => n.IsZone)} entradas/saídas" + (r.Unserved > 0 ? $" ({F(r.Unserved)} veh/h sem caminho)" : ""));
        L($"Percurso na rede: {F(r.VKT)} veh·km/h, {F(r.VHT, "0.0")} veh·h/h; velocidade média {F(r.AvgSpeed, "0.0")} km/h; atraso total {F(r.TotalDelayH, "0.0")} veh·h/h.");
        L($"Emissões estimadas: {F(r.CO2kg)} kg de CO₂ por hora.");
        if (worst != null) L($"Nó mais carregado: {worst.Node.Label} – nível {worst.LOS} ({LosMeaning(worst.LOS)}), atraso médio {F(worst.Delay, "0.0")} s/veh.");
        L($"Diagnóstico: {crit} crítico(s), {warn} ponto(s) de atenção, {r.Diagnostics.Count - crit - warn} informação(ões).");
        if (sim != null)
            L($"Microssimulação ({F(sim.Duration / 60.0)} min após {F(o.WarmupSeconds / 60.0)} min de aquecimento): {sim.Completed} viagens concluídas, " +
              $"tempo médio {F(sim.MeanTravelTime)} s, atraso médio {F(sim.MeanDelay)} s, {F(sim.StopsPerTrip, "0.0")} paradas por viagem" +
              (sim.Backlog > 0 ? $"; {sim.Backlog} veículo(s) não conseguiram entrar na rede." : "."));

        // ------------------------------------------------------------------ rede
        H("2. Rede lida do projeto");
        L($"{net.Roads.Count} via(s), {nodes.Count} nó(s) com tráfego, {net.Links.Count} trecho(s) direcionais, {net.Signs.Count} placa(s), {net.Crosswalks.Count} faixa(s) de pedestres.");
        foreach (var rd in net.Roads)
            L($"  • {rd.Name}: {Hierarquia.Label(rd.Hierarchy)}, {(rd.TwoWay ? $"mão dupla ({rd.LanesForward} + {rd.LanesBackward} faixas)" : $"mão única ({rd.LanesForward} faixa(s))")}, " +
              $"{F(rd.SpeedKmh)} km/h, pista de {F(rd.CarriageWidth, "0.00")} m, {F(rd.Axis.Length)} m" +
              (rd.ParkingForward || rd.ParkingBackward ? ", com estacionamento" : "") + (rd.BikeLane ? ", com ciclovia" : "") + (rd.Median ? ", com canteiro central" : "") +
              (rd.Origin == OrigemTrecho.Plugin ? "" : $" – {OriginLabel(rd.Origin)}: {rd.OriginNote}"));
        foreach (var nd in net.Nodes.Where(n => n.Kind != TipoNo.Continuacao))
            L($"  • {nd.Label}: {KindLabel(nd.Kind)}{(nd.IsZone ? "" : " – " + ControlLabel(nd.Control))} em ({F(nd.Pos.X, "0.0")}; {F(nd.Pos.Y, "0.0")})");
        foreach (var n in net.Notes) L($"  ℹ {n}");
        if (net.Uninterpreted.Count > 0)
        {
            L($"  Não interpretado ({net.Uninterpreted.Count}):");
            foreach (var u in net.Uninterpreted) L($"    ✗ {u.What}: {u.Why}");
        }

        // ------------------------------------------------------------------ sinalização
        if (net.Regulations.Count > 0)
        {
            L();
            L($"Sinalização interpretada ({net.Regulations.Count(x => x.Applied)} aplicada(s), {net.Regulations.Count(x => !x.Applied && x.Kind != TipoRegra.Informativa)} não associada(s)):");
            foreach (var g in net.Regulations.GroupBy(x => x.Kind).OrderBy(g => g.Key))
            {
                L($"  {TrafficRegulation.KindLabel(g.Key)} ({g.Count()}):");
                foreach (var x in g.Take(40)) L($"    {(x.Applied ? "✓" : x.Kind == TipoRegra.Informativa ? "·" : "✗")} {x.Source}: {x.Effect}");
                if (g.Count() > 40) L($"    … e mais {g.Count() - 40}.");
            }
        }

        // ------------------------------------------------------------------ demanda
        H("3. Demanda (estimada)");
        L("Volumes de pico por faixa conforme a hierarquia viária de cada entrada, multiplicados pelo nível de demanda; viagens distribuídas");
        L("entre as entradas por modelo gravitacional (impedância pelo tempo de viagem) e alocadas na rede por equilíbrio (MSA) com atrasos nos nós.");
        foreach (var z in net.Nodes.Where(n => n.IsZone))
        {
            var outV = r.OD.Where(x => x.Key.O == z.Index).Sum(x => x.Value);
            var inV = r.OD.Where(x => x.Key.D == z.Index).Sum(x => x.Value);
            L($"  • {z.Label}: entram {F(outV)} veh/h, saem {F(inV)} veh/h" + (o.ZoneVolumes.ContainsKey(z.Index) ? " (volume informado)" : ""));
        }
        var top = r.OD.OrderByDescending(x => x.Value).Take(8).ToList();
        if (top.Count > 0)
        {
            L("  Maiores pares origem → destino:");
            foreach (var ((a, b), v) in top) L($"    {net.Nodes[a].Label} → {net.Nodes[b].Label}: {F(v)} veh/h");
        }

        // ------------------------------------------------------------------ nós
        H("4. Interseções e rotatórias (HCM)");
        foreach (var nr in nodes.OrderBy(n => n.Node.Label))
        {
            var nd = nr.Node;
            L($"{nd.Label} – {KindLabel(nd.Kind)}, {ControlLabel(nr.Control)}: nível {nr.LOS} ({LosMeaning(nr.LOS)}), atraso médio {F(nr.Delay, "0.0")} s/veh, {F(nr.Volume)} veh/h");
            if (nr.Phases.Count > 0)
            {
                L($"    Plano semafórico ({nr.PlanSource}): ciclo {F(nr.Cycle)} s, {nr.Phases.Count} fases (entreverdes de {F(nr.Intergreen)} s cada)" +
                  (nr.Offset > 0 ? $", defasagem {F(nr.Offset)} s" : "") + ":");
                foreach (var ph in nr.Phases) L($"      {ph.Name}: verde {F(ph.Green)} s");
            }
            L($"    {"Aproximação",-44} {"veh/h",6} {"cap.",6} {"v/c",5} {"atraso",7} {"nível",5} {"fila95",7}");
            foreach (var a in nr.Approaches)
            {
                var sa = sim != null && sim.Approaches.TryGetValue(a.Link, out var x) && x.Vehicles > 0 ? x : null;
                L($"    {Trim(a.Name, 44),-44} {F(a.Volume),6} {F(a.Capacity),6} {F(a.X, "0.00"),5} {F(a.Delay, "0.0"),7} {a.LOS,5} {F(a.Queue95) + " m",7}" +
                  (a.Major ? "  principal" : "") + (a.ProtectedLeft ? "  esquerda protegida" : "") +
                  (sa != null ? $"  | simulação: atraso {F(sa.MeanDelay, "0.0")} s, fila máx. {F(sa.MaxQueue)} m" : ""));
            }
            foreach (var n in nr.Notes) L($"    ℹ {n}");
        }

        // ------------------------------------------------------------------ trechos
        H("5. Trechos");
        L($"  {"Trecho",-48} {"veh/h",6} {"cap.",6} {"v/c",5} {"km/h",5} {"nível",5}" + (sim != null ? "  simulação km/h" : ""));
        foreach (var lr in r.Links.Values.OrderBy(x => x.Link.Road.Name).ThenBy(x => x.Link.S0))
        {
            var l = lr.Link;
            var from = net.Nodes[l.From].Label;
            var to = net.Nodes[l.To].Label;
            var simV = sim?.MeanSpeedKmh(l.Index) ?? double.NaN;
            L($"  {Trim($"{l.Name} {from}→{to}", 48),-48} {F(lr.Volume),6} {F(l.Capacity),6} {F(lr.X, "0.00"),5} {F(lr.Speed),5} {lr.LOS,5}" +
              (sim != null ? $"  {F(simV)}" : ""));
        }

        // ------------------------------------------------------------------ segurança e custos
        H("6. Segurança viária e custos");
        L("Pontos de conflito (FHWA) e acidentes previstos pelas funções de desempenho do HSM (EUA, sem calibração local) –");
        L("números de referência para COMPARAR alternativas (rotatória × semáforo × PARE), não para prever acidentes reais.");
        L($"  {"Nó",-6} {"ramos",5} {"cruz.",5} {"conv.",5} {"div.",5} {"pedestre",8} {"VDM princ.",10} {"VDM sec.",9} {"acid./ano",9} {"c/ vítimas",10}");
        foreach (var nr in nodes.OrderBy(n => n.Node.Label))
        {
            if (!r.Safety.TryGetValue(nr.Node.Index, out var sf) || sf.Legs < 3) continue;
            L($"  {nr.Node.Label,-6} {sf.Legs,5} {sf.Crossing,5} {sf.Merging,5} {sf.Diverging,5} {sf.Pedestrian,8} {F(sf.AadtMajor),10} {F(sf.AadtMinor),9} {F(sf.CrashesPerYear, "0.00"),9} {F(sf.InjuryCrashesPerYear, "0.00"),10}");
        }
        var ec = r.Economics;
        L();
        L($"Custos anuais estimados ({F(o.AnnualHours)} h/ano nesta situação; tempo R$ {F(o.ValueOfTime, "0.00")}/h × {F(o.Occupancy, "0.0")} pessoa/veículo;");
        L($"combustível R$ {F(o.FuelPrice, "0.00")}/L; CO₂ R$ {F(o.Co2Price)}/t; acidente R$ {F(o.CrashCost)}):");
        L($"  Tempo perdido (atrasos):  R$ {F(ec.DelayCost)}");
        L($"  Combustível ({F(ec.FuelLitersPerHour)} L/h): R$ {F(ec.FuelCost)}");
        L($"  Emissões de CO₂:          R$ {F(ec.Co2Cost)}");
        L($"  Acidentes:                R$ {F(ec.CrashCost)}");
        L($"  TOTAL:                    R$ {F(ec.Total)} por ano");

        // ------------------------------------------------------------------ diagnóstico
        H("7. Diagnóstico e recomendações");
        if (r.Diagnostics.Count == 0) L("Nenhum problema encontrado.");
        var i = 0;
        foreach (var d in r.Diagnostics)
        {
            i++;
            var sev = d.Severity switch { Gravidade.Critico => "CRÍTICO", Gravidade.Atencao => "ATENÇÃO", _ => "INFO" };
            L($"{i,3}. [{sev}] [{d.Category}] {d.Title}");
            if (!string.IsNullOrWhiteSpace(d.Detail)) L($"      {d.Detail}");
            if (!string.IsNullOrWhiteSpace(d.Recommendation)) L($"      → {d.Recommendation}");
            if (d.Location is { } p) L($"      Local: ({F(p.X, "0.0")}; {F(p.Y, "0.0")})");
        }
        if (sim != null && sim.Events.Count > 0)
        {
            L();
            L("Ocorrências da microssimulação:");
            foreach (var e in sim.Events.Where(e => !e.StartsWith("DBG"))) L($"  • {e}");
        }

        // ------------------------------------------------------------------ método
        H("8. Método e limitações");
        L("• Capacidade e nível de serviço: HCM (Highway Capacity Manual, 7ª ed.) – semáforos pelo atraso de controle d1 + d2 com plano de");
        L("  Webster (fase protegida à esquerda pelo critério do produto de volumes); PARE/Dê a preferência e preferência à direita por");
        L("  aceitação de brechas; rotatórias pelo modelo exponencial do HCM 6; trechos pela relação de velocidades (urbano) ou densidade.");
        L("• Critérios brasileiros: CTB (arts. 29, 45, 61, 181), Manual Brasileiro de Sinalização de Trânsito (Vol. I, IV e V),");
        L("  Resolução CONTRAN 600/2016 (lombadas), NBR 9050 (rebaixamento de calçadas).");
        L("• Microssimulação: seguimento de veículo IDM, chegadas de Poisson, rotas da alocação, aceitação de brechas nos nós, plano");
        L("  semafórico calculado, travessias de pedestres no meio da quadra. Mostra bloqueios entre cruzamentos (filas que alcançam o");
        L("  nó anterior) que a análise nó a nó não vê.");
        L("• Fatores do HCM no fluxo de saturação: largura de faixa (inclusive estreitamentos da largura variável), rampa, veículos");
        L("  pesados (equivalente maior nos aclives), estacionamento, bloqueio por ônibus parados na faixa, pedestres nas conversões à");
        L("  direita, bolsões de esquerda e faixas de desaceleração/ilhas de giro livre (lidos do projeto).");
        L("• Coordenação: ciclo comum e defasagens pela progressão ao longo dos trechos; fator de progressão do HCM nas aproximações.");
        L("• Segurança: HSM (Highway Safety Manual, cap. 12) e CMF de rotatórias/bolsões – referência internacional sem calibração.");
        L("• A demanda é ESTIMADA pela hierarquia das vias. Para decisões de projeto, informe os volumes contados nas entradas");
        L("  (contagens classificadas na hora de pico) e confirme os critérios de semáforo com contagens de 8 horas (MBST Vol. V).");
        return sb.ToString();
    }

    /// <summary>Tabela das aproximações (separador ";", decimal com vírgula – abre direto no Excel em português).</summary>
    public static string Csv(TrafficResult r, SimResult? sim = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Nó;Tipo;Controle;Nível do nó;Aproximação;Volume (veh/h);Capacidade (veh/h);v/c;Atraso (s/veh);Nível;Fila 95% (m);Verde (s);Ciclo (s);Principal;Esquerda protegida;Atraso simulado (s/veh);Fila máx. simulada (m)");
        foreach (var nr in r.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind != TipoNo.Continuacao).OrderBy(n => n.Node.Label))
            foreach (var a in nr.Approaches)
            {
                var sa = sim != null && sim.Approaches.TryGetValue(a.Link, out var x) ? x : null;
                sb.AppendLine(string.Join(";", nr.Node.Label, KindLabel(nr.Node.Kind), ControlLabel(nr.Control), nr.LOS, a.Name,
                    F(a.Volume), F(a.Capacity), F(a.X, "0.00"), F(a.Delay, "0.0"), a.LOS, F(a.Queue95), F(a.Green), F(nr.Cycle),
                    a.Major ? "sim" : "não", a.ProtectedLeft ? "sim" : "não", sa is { Vehicles: > 0 } ? F(sa.MeanDelay, "0.0") : "", sa != null ? F(sa.MaxQueue) : ""));
            }
        return sb.ToString();
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
