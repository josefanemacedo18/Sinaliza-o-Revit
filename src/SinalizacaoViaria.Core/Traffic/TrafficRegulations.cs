using System.Globalization;
using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Tipo de regra de trânsito lida da sinalização do projeto.</summary>
public enum TipoRegra
{
    Velocidade, Parada, Preferencia, ConversaoProibida, MovimentoObrigatorio, SentidoProibido, ProibidoUltrapassar,
    ProibidoMudarFaixa, Estacionamento, Caminhoes, Exclusiva, Semaforo, SemaforoPedestres, SetaDeFaixa, SeparacaoCentral,
    Bloqueio, FaixaFechada, Moderacao, Informativa,
}

/// <summary>Uma sinalização do projeto e o que ela significa para o tráfego.</summary>
public sealed class TrafficRegulation
{
    public TipoRegra Kind { get; init; }
    public string Code { get; init; } = "";
    public string Source { get; init; } = "";
    public Vec2 Position { get; init; }
    public string? SourceId { get; init; }
    public int? Node { get; set; }
    public int? Link { get; set; }
    /// <summary>Foi associada a um trecho/cruzamento e aplicada na análise e na simulação.</summary>
    public bool Applied { get; set; }
    /// <summary>O que mudou no tráfego (ou por que não foi aplicada).</summary>
    public string Effect { get; set; } = "";

    public static string KindLabel(TipoRegra k) => k switch
    {
        TipoRegra.Velocidade => "Velocidade máxima",
        TipoRegra.Parada => "Parada obrigatória",
        TipoRegra.Preferencia => "Dê a preferência",
        TipoRegra.ConversaoProibida => "Conversão proibida",
        TipoRegra.MovimentoObrigatorio => "Movimento obrigatório",
        TipoRegra.SentidoProibido => "Sentido proibido",
        TipoRegra.ProibidoUltrapassar => "Ultrapassagem proibida",
        TipoRegra.ProibidoMudarFaixa => "Mudança de faixa proibida",
        TipoRegra.Estacionamento => "Estacionamento",
        TipoRegra.Caminhoes => "Restrição a caminhões",
        TipoRegra.Exclusiva => "Circulação exclusiva / proibida",
        TipoRegra.Semaforo => "Semáforo",
        TipoRegra.SemaforoPedestres => "Semáforo de pedestres",
        TipoRegra.SetaDeFaixa => "Seta de faixa",
        TipoRegra.SeparacaoCentral => "Separação física central",
        TipoRegra.Bloqueio => "Bloqueio da via",
        TipoRegra.FaixaFechada => "Faixa fechada",
        TipoRegra.Moderacao => "Redução de velocidade",
        _ => "Informativa",
    };
}

/// <summary>
/// Lê toda a sinalização e os dispositivos do projeto e traduz para regras do simulador: placas de regulamentação
/// (R-1, R-2, R-3, R-4, R-5, R-6, R-7, R-8, R-9, R-10, R-19, R-25, R-26, R-32…), marcas horizontais (LFO, LMS, LRE,
/// LDP, legendas PARE/DEVAGAR/ESCOLA/SÓ ÔNIBUS, setas PEM por faixa, LRV, zebrados e canalizações que fecham faixas,
/// vagas pintadas), dispositivos (barreiras e balizadores no eixo = separação central; transversais = bloqueio; entre
/// faixas = troca de faixa proibida; cones/cavaletes numa faixa = faixa fechada) e semáforos físicos (elementos
/// urbanos) nos cruzamentos e nas travessias do meio da quadra. Cada item fica registrado em
/// <see cref="TrafficNetwork.Regulations"/> com o efeito aplicado – ou o motivo de não ter sido aplicado.
/// </summary>
public static class TrafficRegulations
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] BarrierCodes = { "NJ", "NJ-MOD", "SEP-CONC", "DEF", "DEF-DUPLA", "DEF-CABO", "BAR-PLAST" };
    private static readonly string[] DelineatorCodes = { "BAL-FLEX", "CIL", "TACHAO-SEG", "SEG-CIC", "PIL" };
    private static readonly string[] BlockCodes = { "PRI", "ESF", "FLOR-BLOQ", "PIL", "CONE", "CAVALETE", "TAMBOR", "NJ", "NJ-MOD", "BAR-PLAST", "SEP-CONC", "GRADIL" };
    private static readonly string[] WorkZoneCodes = { "CONE", "CAVALETE", "TAMBOR", "BAR-PLAST", "NJ-MOD" };

    public static void Read(TrafficNetwork net, IReadOnlyCollection<MarkingDefinition> defs, Func<MarkingDefinition, (Polyline2 Axis, double Z)?> axisOf,
        Func<MarkingDefinition, MarkingGeometry?>? geometryOf)
    {
        if (net.Links.Count == 0) return;
        var R = net.Regulations;
        TrafficRegulation Reg(TipoRegra k, string code, string source, Vec2 p, string? id) { var r = new TrafficRegulation { Kind = k, Code = code, Source = source, Position = p, SourceId = id }; R.Add(r); return r; }
        IEnumerable<T> Unique<T>() where T : MarkingDefinition => defs.OfType<T>().GroupBy(d => d.Id).Select(g => g.First());

        // ============================================================ placas
        var speedSigns = new List<(TrafficLink Link, double At, double Kmh, TrafficRegulation Reg)>();
        foreach (var sg in Unique<SignDefinition>())
        {
            var code = (sg.Code ?? "").ToUpperInvariant();
            if (!code.StartsWith("R-")) continue;                  // advertência e indicação não mudam o direito de passagem
            var dir = sg.Direction.Length > 1e-6 ? sg.Direction.Normalized() : Vec2.UnitY;
            var hit = LinkAt(net, sg.Position, dir, 9);
            string Src() => $"Placa {sg.Code}";
            switch (code)
            {
                case "R-1":
                case "R-2":
                {
                    var stop = code == "R-1";
                    var r = Reg(stop ? TipoRegra.Parada : TipoRegra.Preferencia, sg.Code!, Src(), sg.Position, sg.Id);
                    if (Approach(net, hit, 70) is { } ap)
                    {
                        (stop ? ap.Node.StopApproaches : ap.Node.YieldApproaches).Add(ap.Link.Index);
                        Apply(r, ap.Node, ap.Link, $"{ap.Link.Name} {(stop ? "para (PARE)" : "cede a preferência")} em {ap.Node.Label}");
                    }
                    else NotApplied(r, "sem cruzamento a menos de 70 m à frente, no sentido da placa");
                    break;
                }
                case "R-3":
                {
                    var r = Reg(TipoRegra.SentidoProibido, sg.Code!, Src(), sg.Position, sg.Id);
                    if (hit is { } h)
                    {
                        h.Link.Closed = true;
                        h.Link.ClosedWhy = "R-3 – sentido proibido";
                        Apply(r, null, h.Link, $"{h.Link.Name}: circulação proibida neste sentido (a via passa a ser de mão única)");
                    }
                    else NotApplied(r, "nenhum trecho no sentido da placa");
                    break;
                }
                case "R-4A": case "R-4B": case "R-5A": case "R-5B":
                case "R-25A": case "R-25B": case "R-25C": case "R-25D": case "R-26":
                {
                    var forbid = code switch
                    {
                        "R-4A" => new[] { Giro.Esquerda },
                        "R-4B" => new[] { Giro.Direita },
                        "R-5A" or "R-5B" => new[] { Giro.Retorno },
                        "R-25A" => new[] { Giro.Frente, Giro.Direita, Giro.Retorno },
                        "R-25B" => new[] { Giro.Frente, Giro.Esquerda, Giro.Retorno },
                        "R-25C" => new[] { Giro.Direita, Giro.Retorno },
                        "R-25D" => new[] { Giro.Esquerda, Giro.Retorno },
                        _ => new[] { Giro.Esquerda, Giro.Direita, Giro.Retorno },
                    };
                    var r = Reg(code.StartsWith("R-4") || code.StartsWith("R-5") ? TipoRegra.ConversaoProibida : TipoRegra.MovimentoObrigatorio, sg.Code!, Src(), sg.Position, sg.Id);
                    if (Approach(net, hit, 90) is { } ap)
                    {
                        foreach (var g in forbid) ap.Node.ProhibitedTurns.Add((ap.Link.Index, g));
                        Apply(r, ap.Node, ap.Link, $"{ap.Link.Name} em {ap.Node.Label}: proibido {string.Join(", ", forbid.Select(GiroLabel))}");
                    }
                    else NotApplied(r, "sem cruzamento a menos de 90 m à frente, no sentido da placa");
                    break;
                }
                case "R-19":
                {
                    var r = Reg(TipoRegra.Velocidade, sg.Code!, Src(), sg.Position, sg.Id);
                    var kmh = ParseSpeed(sg.Legend);
                    if (kmh == null) { NotApplied(r, "sem o valor da velocidade na legenda da placa"); break; }
                    if (hit is { } h) speedSigns.Add((h.Link, h.At, kmh.Value, r));
                    else NotApplied(r, "nenhum trecho no sentido da placa");
                    break;
                }
                case "R-7":
                {
                    var r = Reg(TipoRegra.ProibidoUltrapassar, sg.Code!, Src(), sg.Position, sg.Id);
                    if (hit is { } h) { h.Link.NoPassing = 1; Apply(r, null, h.Link, $"{h.Link.Name}: ultrapassagem proibida"); }
                    else NotApplied(r, "nenhum trecho no sentido da placa");
                    break;
                }
                case "R-8A": case "R-8B":
                {
                    var r = Reg(TipoRegra.ProibidoMudarFaixa, sg.Code!, Src(), sg.Position, sg.Id);
                    if (hit is { } h && h.Link.Lanes > 1) { h.Link.LaneChangeForbidden = true; Apply(r, null, h.Link, $"{h.Link.Name}: troca de faixa proibida"); }
                    else NotApplied(r, hit == null ? "nenhum trecho no sentido da placa" : "o trecho tem uma faixa só");
                    break;
                }
                case "R-6A": case "R-6B": case "R-6C":
                {
                    var r = Reg(TipoRegra.Estacionamento, sg.Code!, Src(), sg.Position, sg.Id);
                    if (hit is { } h)
                    {
                        h.Link.ParkingOverride = code == "R-6B";
                        Apply(r, null, h.Link, code == "R-6B" ? $"{h.Link.Name}: estacionamento regulamentado (manobras reduzem a capacidade)"
                                                               : $"{h.Link.Name}: sem estacionamento (sem manobras de vaga)");
                    }
                    else NotApplied(r, "nenhum trecho junto à placa");
                    break;
                }
                case "R-9": case "R-14": case "R-15": case "R-16": case "R-17": case "R-18":
                {
                    var r = Reg(TipoRegra.Caminhoes, sg.Code!, Src(), sg.Position, sg.Id);
                    if (hit is { } h) { h.Link.TrucksForbidden = true; Apply(r, null, h.Link, $"{h.Link.Name}: caminhões fora do fluxo do trecho"); }
                    else NotApplied(r, "nenhum trecho no sentido da placa");
                    break;
                }
                case "R-10": case "R-32": case "R-39":
                {
                    var r = Reg(TipoRegra.Exclusiva, sg.Code!, Src(), sg.Position, sg.Id);
                    if (hit is { } h)
                    {
                        h.Link.Closed = true;
                        h.Link.ClosedWhy = code == "R-32" ? "R-32 – circulação exclusiva de ônibus" : code == "R-39" ? "R-39 – exclusiva de caminhões" : "R-10 – automotores proibidos";
                        Apply(r, null, h.Link, $"{h.Link.Name}: fechado ao tráfego geral ({h.Link.ClosedWhy})");
                    }
                    else NotApplied(r, "nenhum trecho no sentido da placa");
                    break;
                }
                default:
                    Reg(TipoRegra.Informativa, sg.Code!, Src(), sg.Position, sg.Id).Effect = "sem efeito sobre a circulação dos veículos no modelo";
                    break;
            }
        }
        ApplySpeedLimits(net, speedSigns);

        // ============================================================ marcas horizontais
        foreach (var l in Unique<LinearMarkingDefinition>())
        {
            var code = (l.Code ?? "").ToUpperInvariant();
            if (code is not ("LFO-1" or "LFO-3" or "LFO-4" or "LMS-1" or "LRE" or "LDP" or "LRV" or "MCF")) continue;
            if (axisOf(l) is not { } ax) continue;
            var line = Math.Abs(l.Offset) > 1e-6 ? ax.Axis.Offset(l.Offset) : ax.Axis;
            if (line.Points.Count < 2) continue;
            var mid = line.PointAt(line.Length / 2);
            switch (code)
            {
                case "LFO-1": case "LFO-3": case "LFO-4":
                {
                    var r = Reg(TipoRegra.ProibidoUltrapassar, l.Code!, $"Linha {l.Code}", mid, l.Id);
                    var hits = CenterCoverage(net, line);
                    if (hits.Count == 0) { NotApplied(r, "não está sobre o eixo de uma via de mão dupla"); break; }
                    var w = code == "LFO-4" ? 0.5 : 1.0;
                    foreach (var (lk, frac) in hits) lk.NoPassing = Math.Max(lk.NoPassing, w * frac);
                    Apply(r, null, hits[0].Link, $"ultrapassagem proibida em {hits.Count} trecho(s) direcional(is)" + (code == "LFO-4" ? " (um dos sentidos)" : ""));
                    break;
                }
                case "LMS-1":
                {
                    var r = Reg(TipoRegra.ProibidoMudarFaixa, l.Code!, "Linha LMS-1 (contínua entre faixas)", mid, l.Id);
                    var done = 0;
                    foreach (var lk in net.Links.Where(k => k.Lanes > 1))
                        if (Coverage(lk, line, (o, _) => IsLaneBoundary(lk, o)) > 0.4) { lk.LaneChangeForbidden = true; done++; }
                    if (done > 0) Apply(r, null, null, $"troca de faixa proibida em {done} trecho(s)"); else NotApplied(r, "não separa faixas de um mesmo sentido");
                    break;
                }
                case "LRE": case "LDP":
                {
                    var stop = code == "LRE";
                    var r = Reg(stop ? TipoRegra.Parada : TipoRegra.Preferencia, l.Code!, stop ? "Linha de retenção (LRE)" : "Linha de dê a preferência (LDP)", mid, l.Id);
                    if (Crossing(net, line) is { } c && Approach(net, c, 40) is { } ap)
                    {
                        // A LRE sozinha só define PARE num cruzamento sem controle; no semáforo e no PARE/Dê a preferência já existentes é a linha de parada deles.
                        if (stop && ap.Node.Control is ControleNo.Semaforo or ControleNo.Pare or ControleNo.DePreferencia or ControleNo.Rotatoria)
                            { r.Applied = true; r.Node = ap.Node.Index; r.Link = ap.Link.Index; r.Effect = $"linha de parada de {ap.Node.Label} ({TrafficReport.ControlLabel(ap.Node.Control)})"; }
                        else
                        {
                            (stop ? ap.Node.StopApproaches : ap.Node.YieldApproaches).Add(ap.Link.Index);
                            Apply(r, ap.Node, ap.Link, $"{ap.Link.Name} {(stop ? "para na linha" : "cede a preferência")} em {ap.Node.Label}");
                        }
                    }
                    else NotApplied(r, "não atravessa a aproximação de um cruzamento");
                    break;
                }
                case "LRV": case "MCF":
                {
                    var r = Reg(TipoRegra.Moderacao, l.Code!, code == "LRV" ? "Linhas de redução de velocidade (LRV)" : "Cruzamento rodoferroviário (MCF)", mid, l.Id);
                    if (Crossing(net, line) is { } c)
                    {
                        var v = code == "MCF" ? 20 / 3.6 : c.Link.FreeSpeed * 0.85;
                        foreach (var lk in SameRoadBothWays(net, c.Link, c.At))
                            lk.Link.SlowPoints.Add((lk.At, v, code == "MCF" ? "passagem em nível" : "LRV"));
                        Apply(r, null, c.Link, $"redução de velocidade para {v * 3.6:0} km/h no ponto");
                    }
                    else NotApplied(r, "não atravessa um trecho de via");
                    break;
                }
            }
        }
        foreach (var t in Unique<TextMarkingDefinition>())
        {
            var txt = (t.Text ?? "").ToUpperInvariant().Replace("\n", " ").Trim();
            var dir = t.Direction.Length > 1e-6 ? t.Direction.Normalized() : Vec2.UnitY;
            var hit = LinkAt(net, t.Position, dir, 1);
            if (txt == "PARE")
            {
                var r = Reg(TipoRegra.Parada, "PARE", "Legenda PARE", t.Position, t.Id);
                if (Approach(net, hit, 40) is { } ap)
                {
                    if (ap.Node.Control is ControleNo.Pare or ControleNo.Semaforo) { r.Applied = true; r.Node = ap.Node.Index; r.Effect = $"reforça o controle de {ap.Node.Label}"; }
                    else { ap.Node.StopApproaches.Add(ap.Link.Index); Apply(r, ap.Node, ap.Link, $"{ap.Link.Name} para em {ap.Node.Label}"); }
                }
                else NotApplied(r, "não está na aproximação de um cruzamento");
            }
            else if (txt is "DEVAGAR" or "ESCOLA" or "LENTO" or "CUIDADO" or "PEDESTRE" or "HOSPITAL")
            {
                var r = Reg(TipoRegra.Moderacao, txt, $"Legenda {txt}", t.Position, t.Id);
                if (hit is { } h)
                {
                    var v = txt == "ESCOLA" ? Math.Min(h.Link.FreeSpeed, 30 / 3.6) : h.Link.FreeSpeed * 0.8;
                    h.Link.SlowPoints.Add((h.At, v, txt.ToLowerInvariant()));
                    Apply(r, null, h.Link, $"{h.Link.Name}: motoristas reduzem para {v * 3.6:0} km/h junto à legenda");
                }
                else NotApplied(r, "fora da pista");
            }
            else if (txt.Contains("ÔNIBUS") || txt.Contains("ONIBUS") || txt.Contains("EXCLUSIVA"))
            {
                var r = Reg(TipoRegra.Exclusiva, "ÔNIBUS", $"Legenda {txt}", t.Position, t.Id);
                if (hit is { } h && h.Link.Lanes > 1 && LaneAt(h.Link, t.Position) is { } lane && !IsSectionBusLane(h.Link, lane))
                {
                    h.Link.ClosedLanes.Add((0, h.Link.Length, lane, "faixa exclusiva de ônibus (legenda)"));
                    Apply(r, null, h.Link, $"{h.Link.Name}: faixa {LaneName(h.Link, lane)} só para ônibus");
                }
                else { r.Applied = hit != null; r.Effect = hit == null ? "fora da pista" : "faixa de ônibus da seção (já considerada) ou via de uma faixa"; }
            }
        }
        foreach (var sm in Unique<SymbolMarkingDefinition>())
        {
            var code = (sm.Code ?? "").ToUpperInvariant();
            var dir = sm.Direction.Length > 1e-6 ? sm.Direction.Normalized() : Vec2.UnitY;
            if (code == "SDP")
            {
                var r = Reg(TipoRegra.Preferencia, "SDP", "Símbolo Dê a preferência", sm.Position, sm.Id);
                if (Approach(net, LinkAt(net, sm.Position, dir, 1), 40) is { } ap)
                {
                    if (ap.Node.Control is ControleNo.DePreferencia or ControleNo.Rotatoria or ControleNo.Semaforo) { r.Applied = true; r.Node = ap.Node.Index; r.Effect = $"reforça o controle de {ap.Node.Label}"; }
                    else { ap.Node.YieldApproaches.Add(ap.Link.Index); Apply(r, ap.Node, ap.Link, $"{ap.Link.Name} cede a preferência em {ap.Node.Label}"); }
                }
                else NotApplied(r, "não está na aproximação de um cruzamento");
                continue;
            }
            if (!code.StartsWith("PEM-")) continue;
            var turns = code switch
            {
                "PEM-F" => new[] { Giro.Frente },
                "PEM-D" => new[] { Giro.Direita },
                "PEM-E" => new[] { Giro.Esquerda },
                "PEM-FD" => new[] { Giro.Frente, Giro.Direita },
                "PEM-FE" => new[] { Giro.Frente, Giro.Esquerda },
                "PEM-DE" => new[] { Giro.Direita, Giro.Esquerda },
                "PEM-RE" or "PEM-RD" => new[] { Giro.Retorno },
                _ => Array.Empty<Giro>(),
            };
            if (turns.Length == 0) continue;
            var rg = Reg(TipoRegra.SetaDeFaixa, sm.Code!, $"Seta {sm.Code}", sm.Position, sm.Id);
            var h2 = LinkAt(net, sm.Position, dir, 1);
            // Seta de esquerda no bolsão da interseção: é a faixa a mais do bolsão (TrafficLink.PocketLength), não uma
            // faixa direta que passa a ser só de conversão.
            if (h2 is { } hp && hp.Link.PocketLength > 0 && code == "PEM-E" && hp.Link.Length - hp.At <= hp.Link.PocketLength + 10
                && -hp.Link.Path.Project(sm.Position).Signed < hp.Link.LaneOffset(hp.Link.Lanes - 1))
            {
                Apply(rg, net.Nodes[hp.Link.To], hp.Link, $"{hp.Link.Name}: bolsão de conversão à esquerda ({hp.Link.PocketLength:0} m)");
                continue;
            }
            if (h2 is { } hh && hh.Link.Length - hh.At <= 90 && LaneAt(hh.Link, sm.Position) is { } ln)
            {
                if (!hh.Link.LaneTurns.TryGetValue(ln, out var set)) hh.Link.LaneTurns[ln] = set = new HashSet<Giro>();
                foreach (var g in turns) set.Add(g);
                Apply(rg, net.Nodes[hh.Link.To], hh.Link, $"{hh.Link.Name}, faixa {LaneName(hh.Link, ln)}: {string.Join(" ou ", turns.Select(GiroLabel))}");
            }
            else NotApplied(rg, "fora de uma faixa a menos de 90 m do cruzamento");
        }
        // Zebrados e canalizações sobre faixas de tráfego (fora dos cruzamentos): faixas fechadas no trecho.
        foreach (var d in defs.Where(d => d is HatchMarkingDefinition or ChannelizationDefinition).GroupBy(d => d.Id).Select(g => g.First()))
        {
            if (net.Nodes.Any(n => n.SourceId != null && n.SourceId == d.GroupId)) continue;     // ilhas pintadas das interseções
            var polys = new List<Polygon2>();
            if (geometryOf?.Invoke(d) is { } g) polys.AddRange(g.Pieces.Where(p => p.Thickness < 0.05).Select(p => p.Shape));
            if (polys.Count == 0 && d is HatchMarkingDefinition h && axisOf(h) is { } hb && hb.Axis.Points.Count >= 3) polys.Add(new Polygon2(hb.Axis.Points));
            if (polys.Count == 0) continue;
            var hull = PolygonOps.Union(polys.Select(p => new Polygon2(p.Outer)).Select(p => PolygonOps.Offset(new[] { p }, 0.8, true)).SelectMany(x => x));
            var c = hull.Count > 0 ? hull[0].Centroid : polys[0].Centroid;
            var name = d is ChannelizationDefinition ? "Canalização" : $"Zebrado {((HatchMarkingDefinition)d).Code}";
            var r = Reg(TipoRegra.FaixaFechada, d.DisplayCode, name, c, d.Id);
            var n0 = ClosePolygons(net, hull, name);
            if (n0 > 0) Apply(r, null, null, $"{n0} faixa(s) fechada(s) no trecho – o tráfego converge antes (gargalo)"); else NotApplied(r, "não cobre o centro de nenhuma faixa de tráfego");
        }
        // Vagas pintadas avulsas ao longo da pista: manobras de estacionamento no trecho.
        foreach (var pk in Unique<ParkingMarkingDefinition>().Where(p => p.GroupId == null || !net.Roads.Any(r => r.GroupId == p.GroupId)))
        {
            if (axisOf(pk) is not { } ax || ax.Axis.Points.Count < 2) continue;
            var mid = ax.Axis.PointAt(ax.Axis.Length / 2);
            var r = Reg(TipoRegra.Estacionamento, pk.Code, $"Vagas {pk.Code}", mid, pk.Id);
            var best = NearestEdgeLink(net, mid);
            if (best != null) { best.ParkingOverride = true; Apply(r, null, best, $"{best.Name}: estacionamento junto à faixa da direita"); }
            else NotApplied(r, "longe de qualquer bordo de pista");
        }

        // ============================================================ dispositivos
        foreach (var dv in Unique<DeviceMarkingDefinition>())
        {
            var code = (dv.Code ?? "").ToUpperInvariant();
            if (axisOf(dv) is not { } ax || ax.Axis.Points.Count < 2) continue;
            var line = Math.Abs(dv.Offset) > 1e-6 ? ax.Axis.Offset(dv.Offset) : ax.Axis;
            ReadDevice(net, dv, code, line, Reg, geometryOf);
        }

        // ============================================================ semáforos físicos (elementos urbanos)
        foreach (var ue in Unique<UrbanElementDefinition>())
        {
            var code = (ue.Code ?? "").ToUpperInvariant();
            if (!code.StartsWith("SEMAFORO")) continue;
            var pts = new List<Vec2>();
            if (ue.UsePath && axisOf(ue) is { } ax) pts.AddRange(ax.Axis.Points); else pts.Add(ue.Position);
            foreach (var p in pts)
            {
                var nd = net.Nodes.Where(n => !n.IsZone && n.Kind != TipoNo.Continuacao)
                    .Select(n => (N: n, D: n.Pos.DistanceTo(p))).Where(x => x.D < Math.Max(25, x.N.RoundaboutRadius + 12)).OrderBy(x => x.D).Select(x => x.N).FirstOrDefault();
                if (nd != null)
                {
                    var r = Reg(TipoRegra.Semaforo, "SEMÁFORO", "Semáforo (elemento urbano)", p, ue.Id);
                    nd.SignalHeads = true;
                    if (nd.Kind != TipoNo.Rotatoria && nd.Control != ControleNo.Semaforo)
                    {
                        var before = nd.Control;
                        nd.Control = nd.DesignControl = ControleNo.Semaforo;
                        Apply(r, nd, null, $"{nd.Label} passa a ser semaforizado (antes: {TrafficReport.ControlLabel(before)})");
                    }
                    else { r.Applied = true; r.Node = nd.Index; r.Effect = $"{nd.Label}: semáforo confirmado"; }
                    continue;
                }
                // Travessia do meio da quadra com semáforo de pedestres.
                var cross = net.Links.SelectMany(l => l.Crosswalks.Select(c => (L: l, c.At, P: l.Path.PointAt(c.At)))).Where(x => x.P.DistanceTo(p) < 18)
                    .OrderBy(x => x.P.DistanceTo(p)).ToList();
                var rp = Reg(TipoRegra.SemaforoPedestres, "SEMÁFORO", "Semáforo de travessia", p, ue.Id);
                if (cross.Count > 0)
                {
                    foreach (var x in cross.GroupBy(x => x.L.Index).Select(g => g.First()))
                        if (!x.L.SignalizedCrossings.Any(s => Math.Abs(s - x.At) < 3)) x.L.SignalizedCrossings.Add(x.At);
                    Apply(rp, null, cross[0].L, "travessia semaforizada: veículos param no vermelho a cada ciclo (75 s, 18 s de pedestres)");
                }
                else NotApplied(rp, "longe de cruzamentos e de faixas de pedestres");
            }
        }

        // ============================================================ controle dos cruzamentos pela sinalização
        foreach (var nd in net.Nodes.Where(n => !n.IsZone && n.Kind is TipoNo.Intersecao or TipoNo.CruzamentoSemControle))
        {
            if (nd.StopApproaches.Count + nd.YieldApproaches.Count == 0) continue;
            if (nd.Control is ControleNo.PreferenciaDireita or ControleNo.Livre)
            {
                nd.Control = nd.DesignControl = nd.StopApproaches.Count > 0 ? ControleNo.Pare : ControleNo.DePreferencia;
                R.Add(new TrafficRegulation
                {
                    Kind = nd.Control == ControleNo.Pare ? TipoRegra.Parada : TipoRegra.Preferencia, Code = "—", Source = "Sinalização do cruzamento", Position = nd.Pos,
                    Node = nd.Index, Applied = true,
                    Effect = $"{nd.Label}: controlado por {TrafficReport.ControlLabel(nd.Control)} nas aproximações sinalizadas (o cruzamento não tinha controle modelado)",
                });
            }
        }
    }

    // ------------------------------------------------------------------ dispositivos

    private static void ReadDevice(TrafficNetwork net, DeviceMarkingDefinition dv, string code, Polyline2 line,
        Func<TipoRegra, string, string, Vec2, string?, TrafficRegulation> Reg, Func<MarkingDefinition, MarkingGeometry?>? geometryOf)
    {
        var mid = line.PointAt(line.Length / 2);
        var name = $"Dispositivo {dv.Code}";
        var barrier = BarrierCodes.Contains(code);
        var delineator = DelineatorCodes.Contains(code);
        var blocks = BlockCodes.Contains(code);
        var work = WorkZoneCodes.Contains(code);
        var handled = false;
        foreach (var road in net.Roads)
        {
            var samples = Enumerable.Range(0, 21).Select(i => line.PointAt(line.Length * i / 20)).Select(p => (P: p, Pr: road.Axis.Project(p))).ToList();
            var onRoad = samples.Where(x => x.Pr.Station > 0.5 && x.Pr.Station < road.Axis.Length - 0.5 && x.Pr.Signed <= road.LeftWidth + 0.3 && x.Pr.Signed >= -road.RightWidth - 0.3).ToList();
            if (onRoad.Count < 3) continue;
            var tan = road.Axis.TangentAt(onRoad[onRoad.Count / 2].Pr.Station);
            var dirDev = line.TangentAt(line.Length / 2);
            var transversal = Math.Abs(dirDev.Dot(tan)) < 0.64;          // mais de 50° com o eixo
            if (transversal && blocks)
            {
                // Bloqueio atravessando a pista: fecha o(s) sentido(s) cujo meio da pista ele cobre.
                var los = onRoad.Min(x => x.Pr.Signed);
                var his = onRoad.Max(x => x.Pr.Signed);
                var st = onRoad.Average(x => x.Pr.Station);
                var closed = new List<TrafficLink>();
                foreach (var lk in net.LinksOf(road).Where(l => st >= l.S0 - 0.5 && st <= l.S1 + 0.5))
                {
                    var (a, b) = road.TwoWay ? (lk.Forward ? (-road.RightWidth, 0.0) : (0.0, road.LeftWidth)) : (-road.RightWidth, road.LeftWidth);
                    var covered = Math.Max(0, Math.Min(his, b) - Math.Max(los, a)) / Math.Max(0.1, b - a);
                    if (covered < 0.6) continue;
                    lk.Closed = true;
                    lk.ClosedWhy = $"bloqueio físico ({dv.Code})";
                    closed.Add(lk);
                }
                var r = Reg(TipoRegra.Bloqueio, dv.Code, name, mid, dv.Id);
                if (closed.Count > 0) Apply(r, null, closed[0], $"{road.Name}: {closed.Count} sentido(s) bloqueado(s) – o tráfego procura outro caminho");
                else NotApplied(r, "não cobre a largura de um sentido da pista (passagem parcial)");
                handled = true;
                break;
            }
            if (transversal) continue;
            var offs = onRoad.Select(x => x.Pr.Signed).ToList();
            var meanOff = offs.Average();
            var along = onRoad.Count / 21.0;
            // Separação central (barreira, separador, balizadores/tachões sobre o eixo de via de mão dupla).
            if (road.TwoWay && Math.Abs(meanOff) < 1.2 && (barrier || delineator))
            {
                var r = Reg(TipoRegra.SeparacaoCentral, dv.Code, name, mid, dv.Id);
                var nodes = net.Nodes.Where(n => !n.IsZone && n.Kind is TipoNo.Intersecao or TipoNo.CruzamentoSemControle && n.RoadIds.Contains(road.Id)
                                                 && line.Project(n.Pos) is var pr && Math.Abs(pr.Signed) < 6 && pr.Station > 0.5 && pr.Station < line.Length - 0.5).ToList();
                foreach (var n in nodes) n.MedianRoads.Add(road.Id);
                foreach (var lk in net.LinksOf(road)) if (Coverage(lk, line, (o, _) => Math.Abs(o) < 1.5) > 0.3) lk.NoPassing = 1;
                Apply(r, nodes.FirstOrDefault(), null, nodes.Count > 0
                    ? $"{road.Name}: sem travessia nem conversão à esquerda em {string.Join(", ", nodes.Select(n => n.Label))} (só à direita)"
                    : $"{road.Name}: sentidos separados fisicamente (sem ultrapassagem nem retorno no trecho)");
                handled = true;
                break;
            }
            // Entre faixas do mesmo sentido: troca de faixa proibida.
            if (delineator && !work)
            {
                var hits = 0;
                foreach (var lk in net.LinksOf(road).Where(l => l.Lanes > 1))
                    if (Coverage(lk, line, (o, _) => IsLaneBoundary(lk, o)) > 0.3) { lk.LaneChangeForbidden = true; hits++; }
                if (hits > 0)
                {
                    var r = Reg(TipoRegra.ProibidoMudarFaixa, dv.Code, name, mid, dv.Id);
                    Apply(r, null, null, $"{road.Name}: troca de faixa impedida fisicamente em {hits} trecho(s)");
                    handled = true;
                    break;
                }
            }
            // Cones, cavaletes, tambores ou barreiras dentro de uma faixa: faixa fechada (obra/interdição).
            if ((work || barrier || delineator) && along > 0.3)
            {
                // A linha do dispositivo é a extensão interditada: cones e cavaletes espaçados, cada um com a sua peça, deixavam
                // vãos entre as peças e a faixa não chegava a fechar (o trecho fechado precisa de 3 m seguidos).
                var poly = geometryOf?.Invoke(dv)?.Pieces.Select(p => p.Shape).ToList() ?? new List<Polygon2>();
                poly.AddRange(RoadGenerator_Band(line, 0.6));
                var n0 = ClosePolygons(net, PolygonOps.Union(poly.Select(p => PolygonOps.Offset(new[] { p }, 1.0, true)).SelectMany(x => x)), $"{dv.Code} na faixa");
                if (n0 > 0)
                {
                    var r = Reg(TipoRegra.FaixaFechada, dv.Code, name, mid, dv.Id);
                    Apply(r, null, null, $"{n0} faixa(s) interditada(s) no trecho");
                    handled = true;
                    break;
                }
            }
        }
        if (!handled)
            Reg(TipoRegra.Informativa, dv.Code, name, mid, dv.Id).Effect = "fora da pista (proteção lateral, calçada ou ciclovia) – sem efeito sobre as faixas de tráfego";
    }

    private static List<Polygon2> RoadGenerator_Band(Polyline2 line, double half)
    {
        var l = line.Offset(half).Points;
        var r = line.Offset(-half).Points.Reverse();
        return new List<Polygon2> { new(l.Concat(r).ToList()) };
    }

    // ------------------------------------------------------------------ velocidade

    /// <summary>R-19 vale a partir da placa, no sentido de quem a lê, até a próxima R-19 da mesma via e sentido.</summary>
    private static void ApplySpeedLimits(TrafficNetwork net, List<(TrafficLink Link, double At, double Kmh, TrafficRegulation Reg)> signs)
    {
        foreach (var grp in signs.GroupBy(s => (s.Link.Road.Id, s.Link.Forward)))
        {
            var road = grp.First().Link.Road;
            var fwd = grp.Key.Forward;
            // Estação na via (no sentido do tráfego: crescente no sentido do eixo, decrescente no contrário).
            double Pos(TrafficLink l, double at) => fwd ? l.S0 + at : -(l.S1 - at);
            var ordered = grp.Select(s => (P: Pos(s.Link, s.At), s.Kmh, s.Reg)).OrderBy(s => s.P).ToList();
            var links = net.LinksOf(road).Where(l => l.Forward == fwd).ToList();
            foreach (var lk in links)
            {
                var mid = fwd ? (lk.S0 + lk.S1) / 2 : -(lk.S0 + lk.S1) / 2;
                var rule = ordered.LastOrDefault(s => s.P <= mid + 1e-6);
                if (rule.Reg == null) continue;
                lk.SpeedLimitKmh = rule.Kmh;
            }
            foreach (var (_, kmh, reg) in ordered)
            {
                reg.Applied = true;
                reg.Link = grp.First().Link.Index;
                reg.Effect = $"{road.Name} ({(fwd || !road.TwoWay ? "sentido do eixo" : "sentido contrário")}): {kmh:0} km/h a partir da placa" +
                             (kmh > road.SpeedKmh + 0.5 ? $" (acima dos {road.SpeedKmh:0} km/h da via)" : "");
            }
        }
    }

    private static double? ParseSpeed(string? legend)
    {
        if (string.IsNullOrWhiteSpace(legend)) return null;
        var m = Regex.Match(legend, @"\d{2,3}");
        return m.Success && double.TryParse(m.Value, NumberStyles.Integer, Pt, out var v) && v is >= 5 and <= 150 ? v : null;
    }

    // ------------------------------------------------------------------ geometria

    /// <summary>Trecho direcional cuja pista passa junto ao ponto e cujo sentido acompanha <paramref name="dir"/>.</summary>
    public static (TrafficLink Link, double At)? LinkAt(TrafficNetwork net, Vec2 p, Vec2 dir, double margin)
    {
        (TrafficLink, double)? best = null;
        var bd = double.MaxValue;
        foreach (var lk in net.Links)
        {
            var (st, signed) = lk.Path.Project(p);
            if (st < -1 || st > lk.Length + 1) continue;
            var r = lk.Road;
            var reach = (signed > 0 ? r.LeftWidth + (r.SidewalkLeft ? 4 : 0) : r.RightWidth + (r.SidewalkRight ? 4 : 0)) + margin;
            // Mão dupla: a placa/marca fica do lado direito de quem a lê (ou sobre a própria meia pista).
            if (Math.Abs(signed) > reach) continue;
            var t = lk.Path.TangentAt(Math.Clamp(st, 0, lk.Length));
            var align = t.Dot(dir);
            if (align < 0.5) continue;
            var side = r.TwoWay ? (signed <= 0.5 ? 0 : 3) : 0;           // prefere o lado direito do sentido
            var score = Math.Abs(signed) + side - align;
            if (score < bd) { bd = score; best = (lk, Math.Clamp(st, 0, lk.Length)); }
        }
        return best;
    }

    /// <summary>Cruzamento à frente no trecho, a no máximo <paramref name="maxDist"/> m.</summary>
    private static (TrafficNode Node, TrafficLink Link)? Approach(TrafficNetwork net, (TrafficLink Link, double At)? hit, double maxDist)
    {
        if (hit is not { } h) return null;
        var nd = net.Nodes[h.Link.To];
        if (nd.IsZone || nd.Kind == TipoNo.Continuacao) return null;
        return h.Link.Length - h.At <= maxDist ? (nd, h.Link) : null;
    }

    /// <summary>
    /// Trecho cuja meia pista (o lado do tráfego dele) é atravessada pela linha transversal – retenção, LDP, LRV. Numa via
    /// de mão dupla, a retenção cobrindo só a meia pista de chegada vale só para aquele sentido.
    /// </summary>
    private static (TrafficLink Link, double At)? Crossing(TrafficNetwork net, Polyline2 line)
    {
        (TrafficLink, double)? best = null;
        var bestN = 1;
        var ld = line.TangentAt(line.Length / 2);
        foreach (var lk in net.Links)
        {
            var r = lk.Road;
            var half = r.TwoWay ? (r.Median ? 1.0 : 0.0) + lk.Lanes * r.LaneWidth + 0.5 : Math.Max(r.LeftWidth, r.RightWidth) + 0.5;
            var n = 0;
            var sum = 0.0;
            for (int i = 0; i <= 10; i++)
            {
                var (st, signed) = lk.Path.Project(line.PointAt(line.Length * i / 10));
                if (st < 0 || st > lk.Length) continue;
                var inHalf = r.TwoWay ? -signed >= -0.3 && -signed <= half : Math.Abs(signed) <= half;
                if (!inHalf) continue;
                n++;
                sum += st;
            }
            if (n <= bestN) continue;
            var at = sum / n;
            if (Math.Abs(ld.Dot(lk.Path.TangentAt(at))) > 0.6) continue;         // linha longitudinal, não transversal
            bestN = n;
            best = (lk, at);
        }
        return best;
    }

    /// <summary>Os dois sentidos da via no ponto (para marcas que valem para toda a pista).</summary>
    private static IEnumerable<(TrafficLink Link, double At)> SameRoadBothWays(TrafficNetwork net, TrafficLink lk, double at)
    {
        var p = lk.Path.PointAt(at);
        foreach (var l in net.LinksOf(lk.Road))
        {
            var (st, _) = l.Path.Project(p);
            if (st >= 0 && st <= l.Length) yield return (l, st);
        }
    }

    /// <summary>Fração do trecho coberta pela linha, onde o afastamento dela ao caminho satisfaz <paramref name="ok"/>.</summary>
    private static double Coverage(TrafficLink lk, Polyline2 line, Func<double, double, bool> ok)
    {
        var n = Math.Max(4, (int)(lk.Length / 4));
        var hits = 0;
        for (int i = 0; i <= n; i++)
        {
            var s = lk.Length * i / n;
            var p = lk.Path.PointAt(s);
            var (st, dist) = line.Project(p);
            if (st <= 0.01 || st >= line.Length - 0.01 || Math.Abs(dist) > 2.5) continue;
            // Afastamento da linha à direita do trecho (positivo = à direita).
            var q = line.PointAt(st);
            var off = -(q - p).Dot(lk.Path.TangentAt(s).PerpLeft);
            if (ok(off, s)) hits++;
        }
        return hits / (double)(n + 1);
    }

    /// <summary>Linhas no eixo de vias de mão dupla (proibição de ultrapassagem nos dois sentidos).</summary>
    private static List<(TrafficLink Link, double Frac)> CenterCoverage(TrafficNetwork net, Polyline2 line)
    {
        var res = new List<(TrafficLink, double)>();
        foreach (var lk in net.Links.Where(l => l.Road.TwoWay))
        {
            var c = Coverage(lk, line, (o, _) => Math.Abs(o - (lk.Road.Median ? 1.0 : 0.0)) < 0.6 || Math.Abs(o) < 0.6);
            if (c > 0.15) res.Add((lk, Math.Min(1, c * 1.1)));
        }
        return res;
    }

    /// <summary>O afastamento (à direita do sentido) cai numa divisa entre faixas do trecho.</summary>
    private static bool IsLaneBoundary(TrafficLink lk, double off)
    {
        for (int k = 0; k + 1 < lk.Lanes; k++)
        {
            var b = (lk.LaneOffset(k) + lk.LaneOffset(k + 1)) / 2;
            if (Math.Abs(off - b) < 0.6) return true;
        }
        return false;
    }

    /// <summary>Faixa (0 = junto ao meio-fio) em que o ponto está.</summary>
    private static int? LaneAt(TrafficLink lk, Vec2 p)
    {
        var (st, signed) = lk.Path.Project(p);
        if (st < -2 || st > lk.Length + 2) return null;
        var right = -signed;
        var best = -1;
        var bd = double.MaxValue;
        for (int k = 0; k < lk.Lanes; k++)
        {
            var d = Math.Abs(lk.LaneOffset(k) - right);
            if (d < bd) { bd = d; best = k; }
        }
        return best >= 0 && bd <= lk.Road.LaneWidth * 0.75 ? best : null;
    }

    private static bool IsSectionBusLane(TrafficLink lk, int lane) => false;

    private static string LaneName(TrafficLink lk, int lane) =>
        lk.Lanes == 1 ? "única" : lane == 0 ? "da direita" : lane == lk.Lanes - 1 ? "da esquerda" : $"{lane + 1}ª da direita";

    /// <summary>Faixas cujo centro cai dentro dos polígonos (amostrado a cada 2 m, fora dos 8 m junto aos nós).</summary>
    private static int ClosePolygons(TrafficNetwork net, List<Polygon2> polys, string why)
    {
        if (polys.Count == 0) return 0;
        var (mn, mx) = (new Vec2(polys.Min(p => p.Bounds.Min.X), polys.Min(p => p.Bounds.Min.Y)), new Vec2(polys.Max(p => p.Bounds.Max.X), polys.Max(p => p.Bounds.Max.Y)));
        var count = 0;
        foreach (var lk in net.Links)
        {
            var (bmn, bmx) = lk.Path.Points.Aggregate((Min: lk.Path.Points[0], Max: lk.Path.Points[0]),
                (a, p) => (new Vec2(Math.Min(a.Min.X, p.X), Math.Min(a.Min.Y, p.Y)), new Vec2(Math.Max(a.Max.X, p.X), Math.Max(a.Max.Y, p.Y))));
            if (bmx.X + 15 < mn.X || bmn.X - 15 > mx.X || bmx.Y + 15 < mn.Y || bmn.Y - 15 > mx.Y) continue;
            for (int lane = 0; lane < lk.Lanes; lane++)
            {
                double? s0 = null;
                double last = 0;
                for (var s = 8.0; s <= lk.Length - 8 + 1e-6; s += 2)
                {
                    var t = lk.Path.TangentAt(s);
                    var p = lk.Path.PointAt(s) + new Vec2(t.Y, -t.X) * lk.LaneOffset(lane);
                    var inside = polys.Any(q => q.Contains(p));
                    if (inside) { s0 ??= s; last = s; }
                    else if (s0 != null) { Add(lk, lane, s0.Value, last); s0 = null; }
                }
                if (s0 != null) Add(lk, lane, s0.Value, last);
            }
        }
        return count;

        void Add(TrafficLink lk, int lane, double a, double b)
        {
            if (b - a < 3) return;
            lk.ClosedLanes.Add((Math.Max(0, a - 1), Math.Min(lk.Length, b + 1), lane, why));
            count++;
        }
    }

    /// <summary>Trecho cujo bordo direito (lado do meio-fio) passa junto ao ponto.</summary>
    private static TrafficLink? NearestEdgeLink(TrafficNetwork net, Vec2 p)
    {
        TrafficLink? best = null;
        var bd = 4.0;
        foreach (var lk in net.Links)
        {
            var (st, signed) = lk.Path.Project(p);
            if (st < 0 || st > lk.Length) continue;
            var edge = lk.LaneOffset(0) + lk.Road.LaneWidth / 2;
            var d = Math.Abs(-signed - edge);
            if (d < bd) { bd = d; best = lk; }
        }
        return best;
    }

    private static void Apply(TrafficRegulation r, TrafficNode? nd, TrafficLink? lk, string effect)
    {
        r.Applied = true;
        r.Node = nd?.Index;
        r.Link = lk?.Index;
        r.Effect = effect;
    }

    private static void NotApplied(TrafficRegulation r, string why)
    {
        r.Applied = false;
        r.Effect = "não aplicada: " + why;
    }

    public static string GiroLabel(Giro g) => g switch { Giro.Direita => "à direita", Giro.Frente => "em frente", Giro.Esquerda => "à esquerda", _ => "retorno" };
}
