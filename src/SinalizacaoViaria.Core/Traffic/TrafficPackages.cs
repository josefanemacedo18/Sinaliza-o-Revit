using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Item de um pacote de intervenção: o que entra no projeto, onde, quanto e com que base normativa.</summary>
public sealed record PackageItem(string Group, string Item, string Quantity, string Norm);

/// <summary>Elemento pontual a criar no projeto (placa ou grupo focal), já posicionado e orientado para o tráfego.</summary>
public sealed record PackagePoint(string Kind, string Code, Vec2 Position, Vec2 Direction, string Note);

/// <summary>Linha transversal a pintar (linha de retenção da travessia semaforizada).</summary>
public sealed record PackageLine(string Code, Vec2 A, Vec2 B);

/// <summary>
/// Pacote de intervenção completo e aplicável: a mudança na interseção (controle, rotatória, conversões, bolsões, plano
/// semafórico) – cujas placas, marcas, travessias e rampas o próprio gerador da interseção/rotatória refaz de forma
/// coesa – mais os elementos avulsos que a medida exige (grupos focais, placas de advertência, retenções da travessia),
/// cada item com a referência normativa.
/// </summary>
public sealed class ProjectPackage
{
    public string Title { get; init; } = "";
    public string Justification { get; set; } = "";
    public List<PackageItem> Items { get; } = new();
    public List<PackagePoint> Points { get; } = new();
    public List<PackageLine> Lines { get; } = new();
    /// <summary>Interseção do projeto a alterar (chave do nó = id da IntersectionDefinition).</summary>
    public string? NodeKey { get; init; }
    public ControleIntersecao? Control { get; set; }
    public bool? LeftTurns { get; set; }
    public bool? LeftTurnPockets { get; set; }
    public bool? Crosswalks { get; set; }
    public bool? ApproachLines { get; set; }
    public SignalPlanDef? Plan { get; set; }
    /// <summary>Planos de outros semáforos (onda verde): chave do nó → plano.</summary>
    public Dictionary<string, SignalPlanDef> OtherPlans { get; } = new();
    /// <summary>Transformar o cruzamento em rotatória deste tipo.</summary>
    public TipoRotatoria? Roundabout { get; set; }
    /// <summary>Remover elementos (grupos focais) num raio deste ponto – retirada do semáforo da travessia.</summary>
    public (Vec2 At, double Radius, string Code)? RemoveNear { get; set; }
    /// <summary>O pacote só muda o cenário (nada a aplicar no modelo) – ex.: nó que não é interseção do plugin.</summary>
    public bool ScenarioOnly { get; set; }

    public string Text()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Title);
        if (!string.IsNullOrWhiteSpace(Justification)) sb.AppendLine(Justification);
        foreach (var g in Items.GroupBy(i => i.Group))
        {
            sb.AppendLine($"  {g.Key}:");
            foreach (var i in g) sb.AppendLine($"    • {i.Item} – {i.Quantity}  [{i.Norm}]");
        }
        return sb.ToString();
    }
}

/// <summary>Monta o pacote de intervenção de cada solução testada (TrafficSolutions), seguindo CTB, MBST e NBR 9050.</summary>
public static class TrafficPackages
{
    /// <summary>Afastamento da linha de retenção ao centro do nó (m), como na microssimulação.</summary>
    public static double StopClear(TrafficNetwork net, TrafficLink l)
    {
        var nd = net.Nodes[l.To];
        if (nd.IsZone || nd.Kind is not (TipoNo.Intersecao or TipoNo.CruzamentoSemControle)) return 0;
        var w = nd.In.Concat(nd.Out).Select(i => net.Links[i].Road).Where(r => r.Id != l.Road.Id).Select(r => r.CarriageWidth / 2).DefaultIfEmpty(3.5).Max();
        return Math.Min(w + 1.2 + (nd.Crosswalks ? 4.5 : 0), l.Length * 0.35);
    }

    /// <summary>Ponto junto ao bordo direito do sentido, na estaca <paramref name="s"/> do trecho, afastado <paramref name="extra"/> m.</summary>
    public static Vec2 RightEdge(TrafficLink l, double s, double extra)
    {
        s = Math.Clamp(s, 0, l.Length);
        var p = l.Path.PointAt(s);
        var d = l.Path.TangentAt(s);
        var right = new Vec2(d.Y, -d.X);
        var inner = l.Road.TwoWay ? (l.Road.Median ? Math.Max(1.0, l.Road.MedianWidth / 2) : 0.0) : -l.Lanes * l.Road.LaneWidth / 2;
        return p + right * (inner + l.Lanes * l.Road.LaneWidth + extra);
    }

    /// <summary>Distância de visibilidade da placa de advertência antes do ponto (MBST Vol. II – tabela por velocidade).</summary>
    public static double WarningDistance(double kmh) => kmh <= 40 ? 50 : kmh <= 60 ? 80 : kmh <= 80 ? 120 : 160;

    /// <summary>Tipo da rotatória para o volume do cruzamento (duas faixas acima de 2 200 veic/h).</summary>
    public static TipoRotatoria RoundaboutTypeFor(double volume) => volume > 2200 ? TipoRotatoria.DuasFaixas : TipoRotatoria.UmaFaixa;

    public static ProjectPackage For(TrafficNetwork net, TrafficResult after, SolutionTrial t)
    {
        var nd = t.Node is { } ni ? net.Nodes[ni] : null;
        var lk = t.Link is { } li ? net.Links[li] : null;
        var key = nd?.SourceId;
        var p = new ProjectPackage { Title = t.Title, NodeKey = key, ScenarioOnly = nd != null && (nd.SourceId == null || nd.DesignKind != TipoNo.Intersecao) && t.Kind is not ("travessia-ciclo" or "travessia-sem") };
        var nr = nd != null ? after.Nodes[nd.Index] : null;
        void I(string g, string item, string q, string norm) => p.Items.Add(new PackageItem(g, item, q, norm));
        var approaches = nd?.In.Select(i => net.Links[i]).Where(l => !net.Nodes[l.From].IsZone || l.Length > 20).ToList() ?? new();

        switch (t.Kind)
        {
            case "semaforo":
            {
                p.Control = ControleIntersecao.Semaforo;
                p.Crosswalks = true;
                p.ApproachLines = true;
                if (nr != null && nr.Phases.Count > 0) p.Plan = TrafficAnalysis.PlanOf(after, nr, "Solução testada – semaforizar");
                p.Justification = $"Semáforo com {nr?.Phases.Count ?? 2} fases e ciclo de {nr?.Cycle ?? 0:0} s (Webster/HCM): separa no tempo os fluxos em conflito e dá travessia protegida aos pedestres.";
                I("Controle", "Plano semafórico (ciclo, fases, verdes, amarelo, vermelho geral, defasagem) gravado na interseção", "1 plano", "MBST Vol. V; CTB art. 89");
                foreach (var l in approaches)
                {
                    var clear = StopClear(net, l);
                    var at = RightEdge(l, l.Length - clear + 0.5, 0.8);
                    p.Points.Add(new PackagePoint("Semaforo", "SEMAFORO", at, -l.Path.TangentAt(l.Length), $"grupo focal veicular + pedestres – {l.Name}"));
                    if (l.Lanes >= 3 || l.Road.Median)
                        p.Points.Add(new PackagePoint("Semaforo", "SEMAFORO", RightEdge(l, l.Length - clear + 0.5, -(l.Lanes * l.Road.LaneWidth) - 1.0), -l.Path.TangentAt(l.Length), $"repetidor à esquerda – {l.Name}"));
                    if (l.Road.SpeedKmh >= 60 || l.FreeSpeed * 3.6 >= 60)
                    {
                        var dist = WarningDistance(l.Road.SpeedKmh);
                        if (l.Length - clear - dist > 5)
                            p.Points.Add(new PackagePoint("Placa", "A-14", RightEdge(l, l.Length - clear - dist, 1.0), -l.Path.TangentAt(l.Length - clear - dist), $"semáforo à frente a {dist:0} m – {l.Name}"));
                    }
                }
                I("Semáforo", "Grupos focais veiculares (3 focos, 200 mm) com grupo focal de pedestres na mesma coluna, junto à retenção de cada aproximação", $"{p.Points.Count(x => x.Kind == "Semaforo")} un", "MBST Vol. V – Sinalização semafórica");
                if (p.Points.Any(x => x.Code == "A-14")) I("Sinalização vertical", "A-14 Semáforo à frente nas aproximações a 60 km/h ou mais", $"{p.Points.Count(x => x.Code == "A-14")} un", "MBST Vol. II; CTB art. 90");
                I("Sinalização horizontal", "LRE (0,40 m) em todas as aproximações; FTP-1 em todas as travessias; LMS-1 nos 20 m antes da retenção", "refeitas pela interseção", "MBST Vol. IV; CTB art. 80");
                I("Sinalização vertical", "Retirada de R-1/R-2 e da legenda PARE (substituídos pelo semáforo)", "refeitas pela interseção", "CTB art. 89 (semáforo prevalece)");
                I("Acessibilidade", "Rampas de calçada nas travessias e piso tátil de alerta", "refeitas pela interseção", "NBR 9050; NBR 16537");
                break;
            }
            case "pare":
            case "preferencia":
            {
                var pare = t.Kind == "pare";
                p.Control = pare ? ControleIntersecao.Pare : ControleIntersecao.DePreferencia;
                p.ApproachLines = true;
                p.Justification = pare
                    ? "Define a preferência da via principal: a secundária para e só entra com brecha (CTB art. 44)."
                    : "A secundária cede a preferência sem parar quando há visibilidade e brecha (CTB art. 44).";
                I("Sinalização vertical", pare ? "R-1 Parada obrigatória nas aproximações secundárias" : "R-2 Dê a preferência nas aproximações secundárias", "1 por aproximação", "MBST Vol. I; CTB art. 44");
                I("Sinalização horizontal", pare ? "LRE (0,40 m) + legenda PARE (1,60 m) antes da retenção" : "LDP + símbolo SDP", "1 por aproximação", "MBST Vol. IV");
                I("Sinalização horizontal", "LFO-3 no eixo da principal nos 15–30 m antes do cruzamento; LCO na boca da transversal", "refeitas pela interseção", "MBST Vol. IV; CTB art. 207");
                I("Visibilidade", "Triângulo de visibilidade livre nas esquinas (sem estacionamento a 5 m do alinhamento transversal)", "conferir no local", "CTB art. 181, VIII");
                break;
            }
            case "rotatoria":
            {
                var vol = nr?.Volume ?? 0;
                p.Roundabout = t.RoundaboutType ?? RoundaboutTypeFor(vol);
                p.Justification = $"Rotatória {(p.Roundabout == TipoRotatoria.DuasFaixas ? "de duas faixas" : "de uma faixa")} ({vol:0} veic/h): elimina os conflitos de cruzamento e reduz a velocidade na entrada.";
                I("Geometria", "Ilha central, pista giratória, ilhas separadoras (gota) em cada ramo, calçada contornando", "1 rotatória", "MBST Vol. IV; DNIT – Manual de Projeto de Interseções");
                I("Sinalização vertical", "R-2 Dê a preferência + R-33 sentido de circulação em cada entrada; A-12 \"Interseção em círculo\" antecipada; marcadores de alinhamento na ilha", "1 conjunto por ramo", "MBST Vol. I/II; DER-SP projetos-tipo");
                I("Sinalização horizontal", "LDP + SDP nas entradas, LBO no anel, setas IMC, LFO-3 nas aproximações", "gerado pela rotatória", "MBST Vol. IV");
                I("Travessias", "FTP-1 a ~7 m do anel, com refúgio no nível da pista nas ilhas e rampas", "1 por ramo", "MBST Vol. IV; NBR 9050");
                I("Retirada", "Sinalização e semáforos do cruzamento anterior (PARE, retenções, faixas no miolo)", "removidos", "—");
                break;
            }
            case "retemporizar":
            case "ondaverde":
            {
                if (nr != null && nr.Phases.Count > 0) p.Plan = TrafficAnalysis.PlanOf(after, nr, t.Kind == "ondaverde" ? "Solução testada – onda verde" : "Solução testada – retemporização");
                if (t.Kind == "ondaverde")
                    foreach (var o in after.Nodes.Values.Where(x => x.Control == ControleNo.Semaforo && x.Phases.Count > 0 && x.Node.SourceId != null && x.Node != nd && x.Node.DesignKind == TipoNo.Intersecao))
                        if (t.RelatedKeys.Contains(o.Node.Key)) p.OtherPlans[o.Node.SourceId!] = TrafficAnalysis.PlanOf(after, o, "Solução testada – onda verde");
                p.Justification = t.Kind == "ondaverde"
                    ? $"Ciclo comum e defasagens: os pelotões chegam no verde ao longo da via ({1 + p.OtherPlans.Count} semáforos)."
                    : $"Ciclo {nr?.Cycle:0} s e verdes proporcionais à demanda de cada fase (Webster/HCM), com verdes mínimos de pedestres.";
                I("Controle", "Plano semafórico gravado em cada interseção (programar o controlador)", $"{1 + p.OtherPlans.Count} plano(s)", "MBST Vol. V");
                break;
            }
            case "proibiresquerda":
            {
                p.LeftTurns = false;
                p.Justification = "Retira as conversões à esquerda (conflito de maior risco e que mais consome capacidade); quem convertia usa a quadra seguinte ou um retorno.";
                I("Sinalização vertical", "R-4a Proibido virar à esquerda em cada aproximação", $"{approaches.Count} un", "MBST Vol. I; CTB art. 207");
                I("Sinalização horizontal", "Eixo contínuo (LFO-3) pela boca das transversais, sem LCO de conversão", "refeito pela interseção", "MBST Vol. IV; CTB art. 207");
                I("Rota alternativa", "Indicar retorno/quadra alternativa com placas de orientação (definir no local)", "a projetar", "MBST Vol. III");
                break;
            }
            case "bolsao":
            {
                p.LeftTurnPockets = true;
                p.Justification = "Faixa exclusiva de conversão à esquerda: quem espera a brecha sai da faixa direta e para de segurar a fila.";
                I("Geometria", "Bolsão de conversão à esquerda (faixa de desaceleração + armazenamento) nas aproximações", "gerado pela interseção", "DNIT – Manual de Projeto de Interseções");
                I("Sinalização horizontal", "Canalização (zebrado + LCA), setas PEM de esquerda, LMS entre as faixas", "gerado pela interseção", "MBST Vol. IV");
                break;
            }
            case "travessia-ciclo":
            case "travessia-sem":
            {
                var cp = after.CrossingPlans.Values.SelectMany(x => x).FirstOrDefault(c => lk != null && c.Pos.DistanceTo(lk.Path.PointAt(c.At)) < 1)
                         ?? after.CrossingPlans.Values.SelectMany(x => x).FirstOrDefault();
                var pos = cp?.Pos ?? lk?.Path.PointAt(lk.Length / 2) ?? Vec2.Zero;
                var links = net.Links.Where(l => l.Path.Project(pos) is var pr && Math.Abs(pr.Signed) < 1 && pr.Station > 3 && pr.Station < l.Length - 3).ToList();
                if (t.Kind == "travessia-ciclo")
                {
                    p.Justification = "Travessia semaforizada com ciclo curto e verde de pedestres pela largura (1,2 m/s + 4 s): menos tempo parado para os veículos, travessia protegida.";
                    foreach (var l in links)
                    {
                        var s = l.Path.Project(pos).Station;
                        p.Points.Add(new PackagePoint("Semaforo", "SEMAFORO", RightEdge(l, s - 3.5, 0.8), -l.Path.TangentAt(s), $"grupo focal veicular + pedestres – {l.Name}"));
                        var a = RightEdge(l, s - 3.6, -(l.Lanes * l.Road.LaneWidth) + 0.1);
                        var b = RightEdge(l, s - 3.6, -0.2);
                        p.Lines.Add(new PackageLine("LRE", a, b));
                    }
                    I("Semáforo", "Grupos focais veiculares e de pedestres (botoeira opcional) nos dois lados da travessia", $"{p.Points.Count} un", "MBST Vol. V; NBR 9050");
                    I("Sinalização horizontal", "LRE a 1,60 m antes da faixa em cada sentido", $"{p.Lines.Count} un", "MBST Vol. IV");
                    I("Controle", "Programar ciclo 60 s e verde de pedestres calculado", "1 plano", "MBST Vol. V");
                }
                else
                {
                    p.RemoveNear = (pos, 12, "SEMAFORO");
                    p.Justification = "Travessia sem semáforo com prioridade do pedestre na faixa (CTB art. 70): só com baixa velocidade e volume de pedestres moderado.";
                    foreach (var l in links)
                    {
                        var s = l.Path.Project(pos).Station;
                        var dist = WarningDistance(Math.Min(60, l.Road.SpeedKmh));
                        if (s - dist > 5) p.Points.Add(new PackagePoint("Placa", "A-32b", RightEdge(l, s - dist, 1.0), -l.Path.TangentAt(s - dist), $"passagem sinalizada de pedestres – {l.Name}"));
                        p.Points.Add(new PackagePoint("Placa", "A-32b", RightEdge(l, s - 2, 1.0), -l.Path.TangentAt(s), $"na travessia – {l.Name}"));
                    }
                    I("Retirada", "Grupos focais da travessia", "removidos", "—");
                    I("Sinalização vertical", "A-32b Passagem sinalizada de pedestres antecipada e na travessia", $"{p.Points.Count} un", "MBST Vol. II; CTB art. 70");
                    I("Moderação", "Recomendado: faixa elevada ou redução de velocidade para 40 km/h (R-19)", "a projetar", "Res. Contran 600/2016; MBST Vol. I");
                }
                p.ScenarioOnly = false;
                break;
            }
        }
        if (p.ScenarioOnly) p.Justification += " (Este nó não é uma interseção do plugin: o efeito vale no cenário; para aplicar no projeto crie a interseção com a ferramenta Interseção.)";
        return p;
    }
}
