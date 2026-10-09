using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Traffic;

public enum Gravidade { Informacao, Atencao, Critico }

/// <summary>Diagnóstico da rede: o problema, onde ocorre, por quê e o que fazer.</summary>
public sealed class TrafficDiagnostic
{
    public Gravidade Severity { get; init; }
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Recommendation { get; init; } = "";
    public Vec2? Location { get; init; }
    public int? Node { get; init; }
    public int? Link { get; init; }
    /// <summary>Marcas do projeto envolvidas (para selecionar no modelo).</summary>
    public List<string> SourceIds { get; init; } = new();

    public string Icon => Severity switch { Gravidade.Critico => "⛔", Gravidade.Atencao => "⚠", _ => "ℹ" };
    public override string ToString() => $"{Icon} [{Category}] {Title}";
}

/// <summary>Regras de diagnóstico: capacidade, rede, segurança, sinalização, geometria, acessibilidade e legislação.</summary>
public static class TrafficDiagnostics
{
    public static void Run(TrafficResult res)
    {
        var net = res.Network;
        var d = res.Diagnostics;
        void Add(Gravidade g, string cat, string title, string detail, string rec, Vec2? at = null, int? node = null, int? link = null, IEnumerable<string?>? ids = null) =>
            d.Add(new TrafficDiagnostic
            {
                Severity = g, Category = cat, Title = title, Detail = detail, Recommendation = rec, Location = at, Node = node, Link = link,
                SourceIds = (ids ?? Array.Empty<string?>()).Where(x => x != null).Cast<string>().Distinct().ToList(),
            });
        string Pos(Vec2 p) => $"({p.X:0}; {p.Y:0})";
        Vec2 Mid(TrafficLink l) => l.Path.PointAt(l.Length / 2);
        IEnumerable<string?> RoadIds(TrafficRoad r) => new[] { r.Id };

        foreach (var note in net.Notes) Add(Gravidade.Informacao, "Rede", note, "", "");

        // ------------------------------------------------------------ rede
        var zones = net.Nodes.Count(n => n.IsZone);
        if (zones < 2)
            Add(Gravidade.Atencao, "Rede", "Rede sem entradas e saídas suficientes",
                "O tráfego entra e sai pelas pontas livres das vias (e pelos balões). Com menos de duas, não há viagens para simular.",
                "Deixe ao menos duas pontas de via livres (limites da área de projeto) ou prolongue as vias até a malha existente.");
        if (res.TotalDemand > 0 && res.Unserved > 0.05 * (res.TotalDemand + res.Unserved))
            Add(Gravidade.Atencao, "Rede", $"{res.Unserved / (res.TotalDemand + res.Unserved) * 100:0}% das viagens não têm caminho",
                "Há pontas de via que não se alcançam: sentido único sem retorno, vias desconectadas ou conversões impossíveis.",
                "Confira os sentidos de circulação e as conexões (Interseção / Conexão).");
        foreach (var nd in net.Nodes.Where(n => !n.IsZone))
        {
            if (nd.In.Count > 0 && nd.Out.Count == 0)
                Add(Gravidade.Critico, "Rede", $"{nd.Label}: tráfego chega e não tem saída",
                    "Todas as vias que saem deste nó são de mão única no sentido contrário (ou não existem).", "Revise os sentidos de circulação.", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            if (nd.Out.Count > 0 && nd.In.Count == 0)
                Add(Gravidade.Atencao, "Rede", $"{nd.Label}: vias saem mas nenhuma chega", "Nó sem tráfego de entrada – confira os sentidos.", "", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
        }
        foreach (var nd in net.Nodes.Where(n => n.Kind == TipoNo.CruzamentoSemControle))
        {
            var ranks = nd.RoadIds.Select(net.Road).Where(r => r != null).Select(r => Hierarquia.Rank(r!.Hierarchy)).ToList();
            var sev = ranks.Count(r => r >= 4) >= 2 ? Gravidade.Critico : Gravidade.Atencao;
            Add(sev, "Segurança", $"{nd.Label}: vias se cruzam sem interseção modelada",
                "Os eixos se cruzam em nível, mas não há interseção do plugin: sem esquinas, faixas, retenção nem controle. No trânsito vale a preferência de quem vem pela direita (CTB art. 29, III).",
                "Crie a interseção (Interseção → a do cruzamento) e defina o controle (PARE, Dê a preferência ou semáforo).", nd.Pos, nd.Index, ids: nd.RoadIds);
        }
        foreach (var (p, a, b, dz) in net.GradeSeparations)
            Add(Gravidade.Informacao, "Rede", $"Cruzamento em desnível em {Pos(p)}", $"As vias passam uma sobre a outra ({dz:0.0} m): sem conflito.", "", p, ids: new[] { a, b });
        foreach (var r in net.Roads.Where(r => r.Hierarchy == null))
            Add(Gravidade.Informacao, "Rede", $"{r.Name}: hierarquia viária não definida", "A capacidade e a velocidade usam valores médios.",
                "Defina a hierarquia (CTB art. 60) na ferramenta Hierarquia ou editando a via.", r.Axis.PointAt(r.Axis.Length / 2), ids: RoadIds(r));

        // ------------------------------------------------------------ capacidade
        foreach (var lr in res.Links.Values)
        {
            var l = lr.Link;
            if (lr.X > 1.0)
                Add(Gravidade.Critico, "Capacidade", $"{l.Name}: saturado (v/c {lr.X:0.00})",
                    $"{lr.Volume:0} veh/h para uma capacidade de {l.Capacity:0} veh/h em {l.Lanes} faixa(s); velocidade média {lr.Speed:0} km/h (nível {lr.LOS}).",
                    "Aumente o número de faixas, retire o estacionamento no pico, crie rota alternativa ou reduza a demanda (transporte coletivo).", Mid(l), link: l.Index, ids: RoadIds(l.Road));
            else if (lr.X > 0.85)
                Add(Gravidade.Atencao, "Capacidade", $"{l.Name}: próximo da capacidade (v/c {lr.X:0.00})",
                    $"{lr.Volume:0} veh/h de {l.Capacity:0} veh/h; pequenas variações da demanda formam filas.", "Reserve folga de capacidade (faixa adicional nas aproximações).",
                    Mid(l), link: l.Index, ids: RoadIds(l.Road));
        }
        foreach (var nr in res.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind != TipoNo.Continuacao))
        {
            var nd = nr.Node;
            foreach (var a in nr.Approaches.Where(a => a.Volume >= 1 && a.LOS is "E" or "F"))
                Add(a.LOS == "F" ? Gravidade.Critico : Gravidade.Atencao, "Capacidade", $"{nd.Label}: aproximação {a.Name} em nível {a.LOS}",
                    $"Atraso médio {a.Delay:0} s/veh, v/c {a.X:0.00}, fila (95%) {a.Queue95:0} m.",
                    nd.Control switch
                    {
                        ControleNo.Semaforo => "Redistribua os verdes, crie faixa exclusiva de conversão ou fase protegida.",
                        ControleNo.Rotatoria => "Entrada com duas faixas, faixa de conversão livre (bypass) ou rotatória maior.",
                        _ => "Avalie semáforo ou rotatória; proíba a conversão à esquerda da secundária ou crie bolsão.",
                    }, nd.Pos, nd.Index, a.Link, ids: new[] { nd.SourceId });
            foreach (var a in nr.Approaches)
            {
                var up = net.Links[a.Link];
                if (a.Queue95 > up.Length * 0.8 && a.Queue95 > 25 && !net.Nodes[up.From].IsZone)
                    Add(Gravidade.Critico, "Capacidade", $"{nd.Label}: fila alcança o cruzamento anterior",
                        $"Fila de {a.Queue95:0} m na aproximação {a.Name}, que tem só {up.Length:0} m até {net.Nodes[up.From].Label}: bloqueio da interseção de montante.",
                        "Coordene os semáforos (onda verde), aumente a capacidade da aproximação ou reduza o ciclo.", nd.Pos, nd.Index, a.Link, ids: new[] { nd.SourceId });
            }
            if (nd.Control == ControleNo.Semaforo)
            {
                if (nr.Cycle > 120)
                    Add(Gravidade.Atencao, "Capacidade", $"{nd.Label}: ciclo semafórico longo ({nr.Cycle:0} s)",
                        "Ciclos acima de 120 s aumentam a espera dos pedestres e o desrespeito ao vermelho.", "Reduza as fases (conversões proibidas) ou amplie a capacidade.", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
                if (nr.Volume < 500)
                    Add(Gravidade.Informacao, "Capacidade", $"{nd.Label}: semáforo com pouco tráfego ({nr.Volume:0} veh/h)",
                        "Com volumes baixos o semáforo gera atraso sem ganho de segurança.", "Avalie PARE/Dê a preferência ou minirrotatória (MBST Vol. V – critérios de implantação).", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
                foreach (var note in nr.Notes) Add(Gravidade.Informacao, "Pedestres", $"{nd.Label}: {note}", "", "", nd.Pos, nd.Index);
                foreach (var a in nr.Approaches.Where(a => a.ProtectedLeft))
                {
                    var lk = net.Links[a.Link];
                    Add(lk.Lanes >= 2 ? Gravidade.Informacao : Gravidade.Atencao, "Capacidade", $"{nd.Label}: fase exclusiva para a conversão à esquerda de {a.Name}",
                        $"{a.LeftVolume:0} veh/h convertem à esquerda contra o fluxo oposto – o produto dos volumes passa do critério de fase protegida. O plano calculado já inclui a fase.",
                        lk.Lanes >= 2
                            ? "Crie faixa exclusiva de conversão à esquerda (bolsão) na aproximação, com extensão para a fila do ciclo, e grupo focal com seta."
                            : "Com uma só faixa, quem segue em frente espera atrás de quem converte: alargue a aproximação para uma faixa exclusiva de conversão.",
                        nd.Pos, nd.Index, a.Link, ids: new[] { nd.SourceId });
                }
            }
            if (nd.Control is ControleNo.Pare or ControleNo.DePreferencia or ControleNo.PreferenciaDireita && nd.Kind != TipoNo.Rotatoria)
            {
                var major = nr.Approaches.Where(a => a.Major).ToList();
                var minor = nr.Approaches.Where(a => !a.Major).ToList();
                var vMaj = major.Sum(a => a.Volume);
                var vMin = minor.Select(a => a.Volume).DefaultIfEmpty(0).Max();
                var lanes = major.Select(a => net.Links[a.Link].Lanes).DefaultIfEmpty(1).Max();
                if (major.Count > 0 && vMaj >= (lanes >= 2 ? 720 : 600) && vMin >= (lanes >= 2 ? 200 : 150))
                    Add(Gravidade.Atencao, "Sinalização", $"{nd.Label}: volumes atendem o critério de semáforo",
                        $"Via principal {vMaj:0} veh/h (ambos os sentidos), maior aproximação secundária {vMin:0} veh/h – acima dos volumes mínimos do critério 1 (MBST Vol. V, na hora de pico).",
                        "Estude semáforo ou rotatória para esta interseção (confirme com contagens de 8 horas).", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            }
        }

        // ------------------------------------------------------------ segurança e sinalização nos nós
        foreach (var nd in net.Nodes.Where(n => n.Kind == TipoNo.Intersecao))
        {
            var roads = nd.RoadIds.Select(net.Road).Where(r => r != null).Cast<TrafficRoad>().ToList();
            var ranks = roads.Select(r => Hierarquia.Rank(r.Hierarchy)).ToList();
            if (nd.Control == ControleNo.PreferenciaDireita && ranks.Count(r => r >= 3) >= 1 && ranks.Distinct().Count() > 1)
                Add(Gravidade.Atencao, "Sinalização", $"{nd.Label}: interseção sem controle entre vias de hierarquias diferentes",
                    "Sem PARE nem Dê a preferência vale a preferência de quem vem pela direita – inclusive sobre a via de maior hierarquia.",
                    "Defina o controle da interseção (PARE ou Dê a preferência na via secundária, R-1/R-2).", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            if (nd.Control is ControleNo.Pare or ControleNo.DePreferencia && nd.MainRoadId != null)
            {
                var main = roads.FirstOrDefault(r => r.Id == nd.MainRoadId);
                if (main != null && roads.Any(r => Hierarquia.Rank(r.Hierarchy) > Hierarquia.Rank(main.Hierarchy)))
                    Add(Gravidade.Atencao, "Sinalização", $"{nd.Label}: a via preferencial tem hierarquia menor",
                        $"A preferência está com {main.Name}, mas chega aqui uma via de hierarquia maior – a sinalização contraria a expectativa do motorista.",
                        "Inverta a preferência (a via de maior hierarquia é a principal) ou revise as hierarquias.", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            }
            if (nd.Control is ControleNo.Pare or ControleNo.DePreferencia && !nd.Signs)
            {
                var code = nd.Control == ControleNo.Pare ? "R-1" : "R-2";
                var near = net.Signs.Any(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && s.Position.DistanceTo(nd.Pos) < 35);
                if (!near)
                    Add(Gravidade.Atencao, "Sinalização", $"{nd.Label}: falta a placa {code} na via secundária",
                        $"A interseção é de {(code == "R-1" ? "PARE" : "Dê a preferência")}, mas não há placa {code} a menos de 35 m do nó.",
                        $"Ative as placas da interseção ou insira {code} junto à linha de retenção (lado direito).", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            }
            if (nd.Control == ControleNo.Semaforo)
                foreach (var li in nd.In.Where(i => net.Links[i].Road.SpeedKmh >= 60))
                {
                    var l = net.Links[li];
                    var hasA14 = net.Signs.Any(s => s.Code.StartsWith("A-14", StringComparison.OrdinalIgnoreCase) && l.Path.Project(s.Position) is var pj && Math.Abs(pj.Signed) < 20 && pj.Station > l.Length - 250);
                    if (!hasA14)
                        Add(Gravidade.Informacao, "Sinalização", $"{nd.Label}: aproximação a {l.Road.SpeedKmh:0} km/h sem A-14 (semáforo à frente)",
                            "Em aproximações rápidas o semáforo deve ser advertido com antecedência.", "Insira A-14 antes da aproximação (MBST Vol. II).", l.Path.PointAt(Math.Max(0, l.Length - 60)), nd.Index, li);
                }
            if (roads.Any(r => r.SidewalkLeft || r.SidewalkRight) && !nd.Crosswalks)
                Add(Gravidade.Informacao, "Pedestres", $"{nd.Label}: interseção sem faixas de pedestres",
                    "As vias têm calçadas, mas a interseção não tem travessias demarcadas.", "Ative as faixas de pedestres da interseção (e os rebaixamentos, NBR 9050).", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            // Ângulo agudo entre as vias.
            var dirs = nd.In.Select(i => net.Links[i].Path.TangentAt(net.Links[i].Length)).ToList();
            var minAng = 90.0;
            for (int i = 0; i < dirs.Count; i++)
                for (int j = i + 1; j < dirs.Count; j++)
                {
                    var ang = Math.Abs(Math.Atan2(dirs[i].Cross(dirs[j]), dirs[i].Dot(dirs[j])) * 180 / Math.PI);
                    var acute = Math.Min(ang, 180 - ang);
                    if (acute > 1) minAng = Math.Min(minAng, acute);
                }
            if (minAng < 60)
                Add(Gravidade.Atencao, "Geometria", $"{nd.Label}: vias se cruzam em ângulo agudo ({minAng:0}°)",
                    "Ângulos abaixo de 60° reduzem a visibilidade e alongam as travessias.", "Realinhe o ramo secundário (entre 75° e 105°) ou canalize a interseção.", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            // Conexão direta de via local com trânsito rápido / rodovia.
            if (ranks.Contains(1) && ranks.Any(r => r >= 5))
                Add(Gravidade.Atencao, "Rede", $"{nd.Label}: via local ligada direto a via de trânsito rápido / rodovia",
                    "A hierarquia salta níveis: o tráfego local entra no fluxo rápido sem transição.", "Ligue a via local a uma coletora ou use ramos/faixas de aceleração.", nd.Pos, nd.Index, ids: nd.RoadIds);
            // Estacionamento junto à esquina (CTB art. 181, I).
            foreach (var (pid, path, code) in net.Parking)
            {
                // Vagas da própria via já recortadas pela interseção (esquina tratada): não há vaga junto ao nó.
                var cuts = net.ParkingCuts.GetValueOrDefault(pid) ?? new List<Polygon2>();
                if (cuts.Any(c => c.Contains(nd.Pos) || c.Outer.Any(v => v.DistanceTo(nd.Pos) < 25))) continue;
                var st = path.Project(nd.Pos);
                if (st.Station <= 0.5 || st.Station >= path.Length - 0.5) continue;       // vagas terminam antes do nó
                var clear = roads.Select(r => Math.Max(r.RightWidth, r.LeftWidth)).DefaultIfEmpty(5).Max() + 5;
                if (Math.Abs(st.Signed) < clear)
                    Add(Gravidade.Atencao, "Legislação", $"{nd.Label}: vagas de estacionamento atravessam o cruzamento",
                        $"As vagas ({code}) seguem pela esquina sem o recuo de 5 m – o CTB (art. 181, I) proíbe estacionar a menos de 5 m do bordo da via transversal.",
                        "Recorte as vagas junto à esquina (Interseção refeita, ou Apagar Trecho) mantendo 5 m além do alinhamento transversal.", nd.Pos, nd.Index, ids: new[] { pid });
            }
        }
        // Nós muito próximos.
        for (int i = 0; i < net.Nodes.Count; i++)
            for (int j = i + 1; j < net.Nodes.Count; j++)
            {
                var a = net.Nodes[i];
                var b = net.Nodes[j];
                if (a.Kind is not (TipoNo.Intersecao or TipoNo.Rotatoria) || b.Kind is not (TipoNo.Intersecao or TipoNo.Rotatoria)) continue;
                var shared = a.RoadIds.Intersect(b.RoadIds).Select(net.Road).Where(r => r != null).Cast<TrafficRoad>().ToList();
                if (shared.Count == 0) continue;
                var dist = a.Pos.DistanceTo(b.Pos);
                var min = shared.Any(r => Hierarquia.Rank(r.Hierarchy) >= 4) ? 60 : 30;
                if (dist < min)
                    Add(Gravidade.Atencao, "Geometria", $"{a.Label} e {b.Label} muito próximos ({dist:0} m)",
                        $"Na {shared[0].Name} os nós estão a {dist:0} m (mínimo recomendado {min} m): filas de um bloqueiam o outro e as conversões se entrelaçam.",
                        "Afaste as interseções, una-as numa só ou proíba conversões em uma delas.", (a.Pos + b.Pos) / 2, a.Index, ids: new[] { a.SourceId, b.SourceId });
            }

        // ------------------------------------------------------------ vias: velocidade, curvas, rampas, moderação
        foreach (var r in net.Roads)
        {
            if (r.Hierarchy is { } h && h != HierarquiaViaria.NaoDefinida && r.SpeedKmh > Hierarquia.DefaultSpeed(h) + 0.5)
                Add(Gravidade.Atencao, "Legislação", $"{r.Name}: velocidade {r.SpeedKmh:0} km/h acima da máxima da hierarquia",
                    $"Para {Hierarquia.Label(h).ToLowerInvariant()} o CTB (art. 61) fixa {Hierarquia.DefaultSpeed(h):0} km/h, salvo sinalização que a regulamente.",
                    "Regulamente com R-19 (e estudo técnico) ou ajuste a velocidade da via.", r.Axis.PointAt(r.Axis.Length / 2), ids: RoadIds(r));
            var rank = Hierarquia.Rank(r.Hierarchy);
            if (rank >= 3 && r.Axis.Length > 200 && !net.Signs.Any(s => s.Code.StartsWith("R-19", StringComparison.OrdinalIgnoreCase) && Math.Abs(r.Axis.Project(s.Position).Signed) < 25))
                Add(Gravidade.Informacao, "Sinalização", $"{r.Name}: sem placa de velocidade máxima (R-19)",
                    "Vias coletoras e arteriais devem informar a velocidade regulamentada no início e após cada interseção importante.", "Insira R-19 com a velocidade da via.",
                    r.Axis.PointAt(Math.Min(30, r.Axis.Length / 2)), ids: RoadIds(r));
            var maxGrade = rank >= 5 ? 6 : rank == 4 ? 8 : rank == 3 ? 12 : 15;
            foreach (var l in net.LinksOf(r).Where(l => l.Forward || !r.TwoWay))
            {
                if (!double.IsInfinity(l.MinRadius))
                {
                    var v = r.SpeedKmh;
                    var f = v <= 40 ? 0.18 : v <= 60 ? 0.16 : v <= 80 ? 0.14 : 0.12;
                    var rmin = v * v / (127 * (0.06 + f));
                    if (l.MinRadius < rmin * 0.95)
                        Add(Gravidade.Atencao, "Geometria", $"{r.Name}: curva de raio {l.MinRadius:0} m para {v:0} km/h",
                            $"O raio mínimo para {v:0} km/h (superelevação 6%, atrito {f:0.00}) é {rmin:0} m. No simulador os veículos reduzem para {l.FreeSpeed * 3.6:0} km/h.",
                            "Aumente o raio, reduza a velocidade regulamentada ou sinalize a curva (A-1/A-2, marcadores de alinhamento).", l.Path.PointAt(l.Length / 2), link: l.Index, ids: RoadIds(r));
                }
                if (Math.Abs(l.GradePct) > maxGrade)
                    Add(Gravidade.Atencao, "Geometria", $"{r.Name}: rampa de {Math.Abs(l.GradePct):0.0}% (máx. {maxGrade}% para a hierarquia)",
                        "Rampas acentuadas reduzem a capacidade e a velocidade dos veículos pesados.", "Suavize o greide (Perfil da Via) ou sinalize (A-15/A-16).", l.Path.PointAt(l.Length / 2), link: l.Index, ids: RoadIds(r));
                if (rank >= 4 && l.SlowPoints.Count > 0)
                    Add(Gravidade.Atencao, "Legislação", $"{r.Name}: moderação de tráfego em via {Hierarquia.Label(r.Hierarchy).ToLowerInvariant()}",
                        "Ondulações transversais e faixas elevadas em vias arteriais, de trânsito rápido e rodovias são restritas pela Res. CONTRAN 600/2016 – confira o enquadramento.",
                        "Prefira fiscalização eletrônica, estreitamento de faixas ou semáforo de travessia.", l.Path.PointAt(l.SlowPoints[0].At), link: l.Index, ids: RoadIds(r));
                foreach (var (at, w) in l.Crosswalks)
                {
                    if (r.SpeedKmh > 60)
                        Add(Gravidade.Critico, "Pedestres", $"{r.Name}: travessia no meio da quadra a {r.SpeedKmh:0} km/h",
                            "Faixa de pedestres sem semáforo em via acima de 60 km/h expõe o pedestre (distância de parada longa).",
                            "Semáforo de travessia, redução de velocidade ou passarela.", l.Path.PointAt(at), link: l.Index, ids: RoadIds(r));
                    var perDir = Math.Max(r.LanesForward, r.LanesBackward);
                    if ((perDir >= 3 || w > 10.5) && !r.Median)
                        Add(Gravidade.Atencao, "Pedestres", $"{r.Name}: travessia longa sem refúgio ({w:0.0} m)",
                            "Travessias de mais de 3 faixas (ou 10,5 m) sem canteiro central deixam o pedestre exposto.", "Crie refúgio central (ilha) ou extensão de calçada.",
                            l.Path.PointAt(at), link: l.Index, ids: RoadIds(r));
                }
            }
        }

        // ------------------------------------------------------------ acessibilidade das travessias
        foreach (var (id, code, path) in net.Crosswalks)
        {
            if (path.Points.Count < 2) continue;
            var ends = new[] { path.Points[0], path.Points[^1] };
            var nearRoad = net.Roads.Any(r => Math.Abs(r.Axis.Project(path.PointAt(path.Length / 2)).Signed) < Math.Max(r.RightWidth, r.LeftWidth) + 2 && (r.SidewalkLeft || r.SidewalkRight));
            if (!nearRoad) continue;
            var missing = ends.Count(e => !net.Ramps.Any(rp => rp.Pos.DistanceTo(e) < 5));
            if (missing > 0 && net.Ramps.Count > 0 || missing == 2 && net.Ramps.Count == 0)
                Add(Gravidade.Atencao, "Acessibilidade", $"Faixa de pedestres {code} sem rebaixamento de calçada",
                    $"{missing} ponta(s) da travessia sem rampa a menos de 5 m – a NBR 9050 exige rebaixamento (ou faixa elevada) em toda travessia.",
                    "Insira rampas (Rampa) nas duas pontas ou use faixa elevada.", path.PointAt(path.Length / 2), ids: new[] { id });
        }

        // ------------------------------------------------------------ descidas longas: área de escape e sonorizadores
        foreach (var r in net.Roads)
        {
            if (r.Grade == null || r.Axis.Length < 300) continue;
            foreach (var forward in r.TwoWay ? new[] { true, false } : new[] { true })
            {
                // Maior sequência contínua de declive no sentido do tráfego (≥ 3 %).
                var step = 20.0;
                double runStart = -1, drop = 0, bestLen = 0, bestDrop = 0, bestEnd = 0;
                for (var s0 = 0.0; s0 + step <= r.Axis.Length + 1e-6; s0 += step)
                {
                    var dz = r.Z(s0 + step) - r.Z(s0);
                    var g = (forward ? dz : -dz) / step;
                    if (g <= -0.03)
                    {
                        if (runStart < 0) { runStart = s0; drop = 0; }
                        drop += -g * step;
                        var len = s0 + step - runStart;
                        if (len > bestLen) { bestLen = len; bestDrop = drop; bestEnd = forward ? s0 + step : runStart; }
                    }
                    else runStart = -1;
                }
                if (bestLen < 500) continue;
                var mean = bestDrop / bestLen * 100;
                // Critério usual (AASHTO/DNIT): declive ≥ 5 % em mais de 1 km, ou ≥ 3 % com mais de 60 m de desnível, com caminhões.
                var need = (mean >= 5 && bestLen >= 1000) || bestDrop >= 60;
                if (!need || res.Options.HeavyVehicles <= 0) continue;
                var endP = r.Axis.PointAt(Math.Clamp(bestEnd, 0, r.Axis.Length));
                var hasEscape = net.EscapeRamps.Any(e => e.Pos.DistanceTo(endP) < bestLen);
                if (!hasEscape)
                    Add(Gravidade.Critico, "Segurança", $"{r.Name}: descida de {bestLen:0} m a {mean:0.0}% sem área de escape",
                        $"Desnível de {bestDrop:0} m no sentido {(forward ? "do eixo" : "contrário")}: caminhões podem perder o freio (superaquecimento). " +
                        "AASHTO/DNIT recomendam área de escape (caixa de retenção) antes do ponto crítico – curva fechada, interseção ou área urbana no pé da descida.",
                        "Insira Área de Escape (Bloqueios Físicos) à direita, tangente à pista, nos últimos 2/3 da descida; sinalize com A-40/A-45 e placa de escape a 1 km, 500 m e na entrada.",
                        endP, ids: RoadIds(r));
                else
                    Add(Gravidade.Informacao, "Segurança", $"{r.Name}: descida de {bestLen:0} m a {mean:0.0}% com área de escape",
                        "A caixa de retenção atende a descida – confira o comprimento pela velocidade de entrada no relatório da área de escape.", "", endP, ids: RoadIds(r));
            }
        }
        foreach (var r in net.Roads.Where(r => Hierarquia.Rank(r.Hierarchy) >= 5 || r.Hierarchy == HierarquiaViaria.Estrada))
        {
            if (r.Axis.Length < 1000 || r.SpeedKmh < 70) continue;
            var covered = net.RumbleStrips.Any(x => x.Path.Points.Any(p => Math.Abs(r.Axis.Project(p).Signed) < Math.Max(r.RightWidth, r.LeftWidth) + 4));
            if (!covered)
                Add(Gravidade.Informacao, "Segurança", $"{r.Name}: via de {r.SpeedKmh:0} km/h sem sonorizador longitudinal",
                    "Saídas de pista por sono ou distração são o principal tipo de acidente fatal em rodovias; sonorizadores no bordo/acostamento reduzem 20 a 40 % delas (FHWA).",
                    "Insira Sonorizador Longitudinal no acostamento (fresado) ou linha de bordo perfilada; no eixo de pistas simples, contra colisões frontais.",
                    r.Axis.PointAt(r.Axis.Length / 2), ids: RoadIds(r));
        }

        // ------------------------------------------------------------ largura variável, ônibus e faixas auxiliares
        foreach (var r in net.Roads.Where(r => r.NarrowestAt != null))
        {
            if (r.MinLaneWidth < 2.7)
                Add(Gravidade.Atencao, "Geometria", $"{r.Name}: estreitamento deixa faixas de {r.MinLaneWidth:0.00} m",
                    $"No ponto mais estreito a pista tem {r.MinCarriageWidth:0.00} m entre meios-fios. Faixas abaixo de 2,70 m reduzem a capacidade e não comportam ônibus e caminhões lado a lado.",
                    "Reduza o número de faixas no trecho (com transição e LBO/zebrado) ou recupere a largura.", r.Axis.PointAt(r.NarrowestAt!.Value), ids: RoadIds(r));
            else if (r.MinCarriageWidth < r.CarriageWidth - 0.3)
                Add(Gravidade.Informacao, "Capacidade", $"{r.Name}: largura variável ({r.MinCarriageWidth:0.00} a {r.CarriageWidth:0.00} m)",
                    $"O simulador usa a faixa mais estreita ({r.MinLaneWidth:0.00} m) nos trechos em que ela ocorre.", "", r.Axis.PointAt(r.NarrowestAt!.Value), ids: RoadIds(r));
        }
        foreach (var lr in res.Links.Values)
        {
            var l = lr.Link;
            foreach (var (at, bay, id) in l.BusStops.Where(b => !b.Bay))
            {
                if (l.Lanes == 1 && Hierarquia.Rank(l.Road.Hierarchy) >= 3 && lr.Volume > 400)
                    Add(Gravidade.Atencao, "Capacidade", $"{l.Name}: ponto de ônibus na única faixa ({lr.Volume:0} veh/h)",
                        $"Com {res.Options.BusesPerHourPerStop:0} ônibus/h e {res.Options.BusDwell:0} s de embarque, a faixa fica bloqueada {res.Options.BusesPerHourPerStop * res.Options.BusDwell / 36:0.0}% do tempo e forma fila atrás do ônibus.",
                        "Crie baia de ônibus (Nova Via / Editar via → Recuos) ou faixa exclusiva.", l.Path.PointAt(at), link: l.Index, ids: new[] { id });
                var toEnd = l.Length - at;
                if (toEnd < 30 && toEnd >= 0)
                    Add(Gravidade.Informacao, "Segurança", $"{l.Name}: ponto de ônibus a {toEnd:0} m do cruzamento",
                        "Ponto junto à esquina, antes do cruzamento, esconde o pedestre que atravessa na frente do ônibus e bloqueia a conversão à direita.",
                        "Prefira o ponto depois do cruzamento (far-side) ou afaste 30 m da esquina.", l.Path.PointAt(at), link: l.Index, ids: new[] { id });
            }
        }
        foreach (var nr in res.Nodes.Values.Where(n => !n.Node.IsZone && n.Node.Kind is TipoNo.Intersecao or TipoNo.CruzamentoSemControle))
        {
            var nd = nr.Node;
            foreach (var a in nr.Approaches)
            {
                var l = net.Links[a.Link];
                var right = a.Movements.GetValueOrDefault(Giro.Direita);
                if (right >= 300 && l.DecelLane <= 0 && !nd.RightTurnIslands && l.Road.SpeedKmh >= 60)
                    Add(Gravidade.Atencao, "Capacidade", $"{nd.Label}: {right:0} veh/h convertem à direita vindo de {a.Name}",
                        $"A {l.Road.SpeedKmh:0} km/h, quem desacelera para converter freia na faixa direta (colisão traseira) e reduz a capacidade.",
                        "Crie faixa de desaceleração (Recuos da via) ou ilha de conversão à direita.", nd.Pos, nd.Index, a.Link, ids: new[] { nd.SourceId });
                if (a.LeftVolume >= 100 && !nd.LeftPockets && l.Lanes == 1 && (l.Road.TwoWay))
                    Add(Gravidade.Atencao, "Capacidade", $"{nd.Label}: conversão à esquerda sem bolsão ({a.LeftVolume:0} veh/h de {a.Name})",
                        "Quem espera para converter à esquerda bloqueia a única faixa – filas e colisões traseiras.",
                        "Ative os bolsões de conversão à esquerda na interseção (Editar interseção).", nd.Pos, nd.Index, a.Link, ids: new[] { nd.SourceId });
            }
        }

        // ------------------------------------------------------------ segurança: conflitos e acidentes previstos
        foreach (var (idx, sf) in res.Safety.Where(x => x.Value.CrashesPerYear > 0))
        {
            var nr = res.Nodes[idx];
            var nd = nr.Node;
            if (sf.CrashesPerYear >= 4)
                Add(sf.CrashesPerYear >= 8 ? Gravidade.Critico : Gravidade.Atencao, "Segurança",
                    $"{nd.Label}: {sf.CrashesPerYear:0.0} acidentes/ano previstos ({sf.Total} pontos de conflito)",
                    $"Modelo {sf.Model}, VDM principal {sf.AadtMajor:0} e secundária {sf.AadtMinor:0} (volume da hora ÷ K = {res.Options.KFactor:0.00}). " +
                    $"≈ {sf.InjuryCrashesPerYear:0.0}/ano com vítimas. Estimativa sem calibração local – use para comparar alternativas.",
                    nr.Control == ControleNo.Rotatoria ? "Reforce a deflexão das entradas e a sinalização de preferência."
                        : nr.Control == ControleNo.Semaforo ? "Proteja as conversões à esquerda, avalie rotatória ou reduza a velocidade de aproximação."
                        : "Avalie rotatória (menos pontos de conflito e menor velocidade) ou semáforo, conforme os volumes.",
                    nd.Pos, nd.Index, ids: new[] { nd.SourceId });
        }

        // ------------------------------------------------------------ sinalização interpretada: conflitos e efeitos
        foreach (var nd in net.Nodes.Where(n => !n.IsZone && n.Kind != TipoNo.Continuacao))
        {
            var nr = res.Nodes.GetValueOrDefault(nd.Index);
            var ctl = nr?.Control ?? nd.Control;
            if (ctl == ControleNo.Semaforo && nd.StopApproaches.Count > 0)
                Add(Gravidade.Atencao, "Sinalização", $"{nd.Label}: PARE (R-1/legenda) em cruzamento semaforizado",
                    "A parada obrigatória só vale com o semáforo apagado ou em amarelo intermitente – com o semáforo operando ela confunde o condutor.",
                    "Retire o R-1/PARE das aproximações semaforizadas ou use-o só como sinalização de contingência.", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            var ins = nd.In.Select(i => net.Links[i]).ToList();
            if (ins.Count >= 3 && nd.StopApproaches.Count >= ins.Count)
                Add(Gravidade.Atencao, "Sinalização", $"{nd.Label}: PARE em todas as aproximações",
                    "Parada obrigatória em todos os ramos não define quem tem a preferência (o CTB não prevê 'all-way stop'): todos param e seguem pela direita.",
                    "Defina a via preferencial (sem R-1) ou implante rotatória/semáforo.", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            // Parada sinalizada na via de maior hierarquia enquanto a menor segue livre.
            if (nd.StopApproaches.Count > 0 && ctl is ControleNo.Pare or ControleNo.DePreferencia)
            {
                var stopRank = nd.StopApproaches.Select(i => Hierarquia.Rank(net.Links[i].Road.Hierarchy)).Max();
                var freeRank = ins.Where(l => !nd.MinorApproaches.Contains(l.Index)).Select(l => Hierarquia.Rank(l.Road.Hierarchy)).DefaultIfEmpty(0).Max();
                if (stopRank > freeRank && freeRank > 0)
                    Add(Gravidade.Atencao, "Sinalização", $"{nd.Label}: a via de maior hierarquia tem o PARE",
                        "A sinalização dá a preferência à via de menor hierarquia – contraria o fluxo natural e costuma ser desrespeitada.",
                        "Inverta a sinalização (R-1/R-2 e retenção na via secundária).", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            }
            foreach (var l in ins)
                foreach (var (k, set) in l.LaneTurns)
                    foreach (var g in set.Where(g => nd.ProhibitedTurns.Contains((l.Index, g))))
                        Add(Gravidade.Atencao, "Sinalização", $"{nd.Label}: seta permite {TrafficRegulations.GiroLabel(g)} mas a placa proíbe",
                            $"Na aproximação {l.Name} a seta pintada na faixa indica {TrafficRegulations.GiroLabel(g)}, proibida por placa (R-4/R-5/R-25/R-26).",
                            "Compatibilize setas e placas.", nd.Pos, nd.Index, l.Index, ids: new[] { nd.SourceId });
            if (nd.MedianRoads.Count > 0)
                Add(Gravidade.Informacao, "Rede", $"{nd.Label}: separação central contínua – só entradas e saídas pela direita",
                    "A barreira/separador atravessa o cruzamento: travessias e conversões à esquerda pela via separada são impossíveis e o tráfego busca retornos e outras rotas.",
                    "Se a travessia for necessária, abra o canteiro com uma interseção canalizada ou retorno.", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
            if (nd.SignalHeads && nd.SourceId != null && net.Regulations.Any(r => r.Kind == TipoRegra.Semaforo && r.Node == nd.Index && r.Effect.Contains("passa a ser")))
                Add(Gravidade.Informacao, "Sinalização", $"{nd.Label}: semáforo do projeto, interseção sem controle semafórico",
                    "Há semáforos (elementos urbanos) no cruzamento: o simulador trata o nó como semaforizado, mas a interseção foi criada com outro controle (placas e retenções dela não batem).",
                    "Edite a interseção e escolha o controle Semáforo – ou use 'Aplicar este controle no projeto' na aba Cruzamentos.", nd.Pos, nd.Index, ids: new[] { nd.SourceId });
        }
        foreach (var lk in net.Links.Where(l => l.Closed))
            Add(Gravidade.Informacao, "Rede", $"{lk.Name}: fechado ao tráfego geral ({lk.ClosedWhy})",
                "O trecho não recebe viagens de automóveis: o tráfego é redistribuído pelas rotas possíveis.", "", Mid(lk), link: lk.Index, ids: RoadIds(lk.Road));
        foreach (var lr in res.Links.Values.Where(x => x.Link.ClosedLanes.Count > 0 && !x.Link.Closed))
        {
            var lk = lr.Link;
            var why = string.Join(", ", lk.ClosedLanes.Select(c => c.Why).Distinct());
            Add(lr.X > 0.85 ? Gravidade.Atencao : Gravidade.Informacao, "Capacidade", $"{lk.Name}: {lk.Lanes - lk.MinOpenLanes} faixa(s) fechada(s) ({why})",
                $"O trecho escoa por {lk.MinOpenLanes} de {lk.Lanes} faixa(s) no ponto mais restrito (v/c {lr.X:0.00}) e os veículos convergem antes do fechamento.",
                lr.X > 0.85 ? "Reduza a extensão do fechamento, sinalize a transição com antecedência (MBST Vol. VII – obras) ou desvie parte do tráfego." : "",
                lk.Path.PointAt(lk.ClosedLanes.Average(c => (c.S0 + c.S1) / 2)), link: lk.Index, ids: RoadIds(lk.Road));
        }
        foreach (var lk in net.Links.Where(l => l.SpeedLimitKmh is { } v && l.Road.Hierarchy is { } h && h != HierarquiaViaria.NaoDefinida && v > Hierarquia.DefaultSpeed(h) + 0.5))
            Add(Gravidade.Atencao, "Legislação", $"{lk.Name}: R-19 de {lk.SpeedLimitKmh:0} km/h acima da máxima da hierarquia",
                $"Para {Hierarquia.Label(lk.Road.Hierarchy).ToLowerInvariant()} o CTB (art. 61) fixa {Hierarquia.DefaultSpeed(lk.Road.Hierarchy!.Value):0} km/h; velocidade maior exige estudo técnico.",
                "Confirme o estudo que justifica a velocidade ou ajuste a placa.", Mid(lk), link: lk.Index, ids: RoadIds(lk.Road));
        var loose = net.Regulations.Where(r => !r.Applied && r.Kind != TipoRegra.Informativa).ToList();
        foreach (var g in loose.GroupBy(r => r.Source))
            Add(Gravidade.Informacao, "Sinalização", $"{g.Key}: {g.Count()} item(ns) não associado(s) ao tráfego",
                string.Join(" ", g.Select(r => r.Effect).Distinct().Take(3)) + ".",
                "Confira a posição e o sentido (a placa lê o sentido do tráfego que se aproxima; setas e legendas ficam dentro da faixa).",
                g.First().Position, ids: g.Select(r => r.SourceId));

        // Junta os repetidos (mesmo problema no mesmo lugar, p. ex. os dois lados da via) e ordena: críticos primeiro.
        var sorted = d.GroupBy(x => (x.Title, x.Node, x.Link))
            .Select(g => g.Count() == 1 ? g.First() : new TrafficDiagnostic
            {
                Severity = g.Max(x => x.Severity), Category = g.First().Category, Title = g.Key.Title, Detail = g.First().Detail,
                Recommendation = g.First().Recommendation, Location = g.First().Location, Node = g.Key.Node, Link = g.Key.Link,
                SourceIds = g.SelectMany(x => x.SourceIds).Distinct().ToList(),
            })
            .OrderByDescending(x => x.Severity).ThenBy(x => x.Category).ToList();
        d.Clear();
        d.AddRange(sorted);
    }
}
