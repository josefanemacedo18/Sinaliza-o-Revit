using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Veículo de projeto do retorno (DNIT – Manual de Projeto de Interseções, raios mínimos de giro).</summary>
public enum VeiculoProjeto
{
    /// <summary>Veículo de passeio.</summary>
    VP,
    /// <summary>Caminhão / ônibus convencional (unidade simples).</summary>
    CO,
    /// <summary>Semirreboque.</summary>
    SR,
}

/// <summary>Forma do retorno em U numa via existente.</summary>
public enum TipoRetorno
{
    /// <summary>Abertura no canteiro central (com alargamento da pista oposta se o veículo não couber).</summary>
    AberturaCanteiro,
    /// <summary>Bolsão de espera: no canteiro (se couber) ou lateral, à direita, de onde o veículo cruza a via.</summary>
    Bolsao,
    /// <summary>Só o alargamento da pista oposta para o giro (o veículo espera na faixa junto ao eixo/canteiro).</summary>
    Alargamento,
}

/// <summary>
/// Retorno em U numa via existente (gravado na seção da via): estaca do início do giro, sentido de quem retorna, veículo de
/// projeto e forma. A via é gerada com a abertura do canteiro, os alargamentos/bolsões (recuos derivados) e a sinalização.
/// </summary>
public sealed class RetornoVia
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Estaca do início do giro (m) – o veículo começa a virar aqui.</summary>
    public double Estaca { get; set; }
    /// <summary>Quem retorna segue no sentido do eixo (pista da direita); falso = no sentido contrário.</summary>
    public bool SentidoDoEixo { get; set; } = true;
    public VeiculoProjeto Veiculo { get; set; } = VeiculoProjeto.VP;
    /// <summary>Raio externo (roda dianteira externa) e interno (roda traseira interna) – nulo = o do veículo de projeto.</summary>
    public double? RaioExterno { get; set; }
    public double? RaioInterno { get; set; }
    public TipoRetorno Tipo { get; set; } = TipoRetorno.AberturaCanteiro;
    /// <summary>Comprimento de espera do bolsão (m). Nulo = pelo veículo (2 veículos).</summary>
    public double? Espera { get; set; }
    /// <summary>Setas, placas (R-3, R-5a, R-6a) e linhas do retorno.</summary>
    public bool Sinalizacao { get; set; } = true;

    public RetornoVia Clone() => (RetornoVia)MemberwiseClone();

    /// <summary>Raio externo e interno mínimos, largura e comprimento do veículo de projeto (m).</summary>
    public static (double Ro, double Ri, double Largura, double Comprimento) Dimensoes(VeiculoProjeto v) => v switch
    {
        VeiculoProjeto.CO => (12.80, 8.70, 2.60, 9.10),
        VeiculoProjeto.SR => (13.70, 6.00, 2.60, 16.80),
        _ => (7.30, 4.70, 2.10, 5.80),
    };

    public static string Rotulo(VeiculoProjeto v) => v switch
    {
        VeiculoProjeto.CO => "CO – caminhão/ônibus (R ext. 12,80 m, int. 8,70 m)",
        VeiculoProjeto.SR => "SR – semirreboque (R ext. 13,70 m, int. 6,00 m)",
        _ => "VP – automóvel (R ext. 7,30 m, int. 4,70 m)",
    };

    [JsonIgnore] public double Ro => Math.Max(3, RaioExterno ?? Dimensoes(Veiculo).Ro);
    [JsonIgnore] public double Ri => Math.Clamp(RaioInterno ?? Dimensoes(Veiculo).Ri, 0.5, Ro - 1);
}

/// <summary>
/// Retorno calculado no sistema local (t ao longo do sentido de quem retorna a partir do início do giro; u lateral, com o lado
/// de chegada em u &lt; 0): centro do giro, faixas, abertura do canteiro, bolsão e alargamentos (como recuos da via).
/// </summary>
public sealed record RetornoPlan(RetornoVia Spec, double Ro, double Ri, double VehicleWidth, double LaneU, double CenterU,
    double NearEdge, double FarEdge, double FarWidening, bool Median, double MedianLo, double MedianHi,
    MedianOpening? Opening, List<RecuoVia> Recesses, double TurnEnd, double WaitStart, List<string> Warnings)
{
    /// <summary>Estaca do eixo na distância t (sentido de quem retorna).</summary>
    public double Station(double t) => Spec.Estaca + (Spec.SentidoDoEixo ? t : -t);
    /// <summary>Deslocamento no eixo (+ à esquerda) do lateral u.</summary>
    public double Offset(double u) => Spec.SentidoDoEixo ? u : -u;
}

public sealed partial class RoadSetup
{
    /// <summary>Retornos em U (abertura de canteiro, bolsão, alargamento) – gerados com a via.</summary>
    public List<RetornoVia> Retornos { get; set; } = new();

    /// <summary>Largura da faixa de tráfego junto ao eixo/canteiro de um lado.</summary>
    private static double InnerLane(List<ElementoSecao> side) =>
        side.FirstOrDefault(e => ElementoSecao.EhFaixaDeTrafego(e.Tipo))?.Largura ?? 3.5;

    /// <summary>
    /// Geometria do retorno: o veículo começa o giro na faixa junto ao canteiro (ou no bolsão), a roda dianteira externa descreve
    /// um semicírculo de raio Ro e a traseira interna, de raio Ri. O canteiro abre onde a faixa varrida (Ri a Ro, + 0,50 m) o
    /// cruza; se a roda externa não couber na pista oposta (0,50 m da face do meio-fio), ela é alargada (recuo derivado).
    /// </summary>
    public RetornoPlan PlanRetorno(RetornoVia r)
    {
        var warn = new List<string>();
        var ro = r.Ro;
        var ri = r.Ri;
        var (_, _, vw, vl) = RetornoVia.Dimensoes(r.Veiculo);
        var near = r.SentidoDoEixo ? Right : Left;
        var far = r.SentidoDoEixo ? Left : Right;
        var (nearEdge, _, nearWalk) = Nominal(!r.SentidoDoEixo);
        var (farEdge, farLot, farWalk) = Nominal(r.SentidoDoEixo);
        var median = TwoWay && Center == CenterTreatment.Canteiro && MedianType == TipoCanteiro.Fisico && PhysicalElements && MedianWidth > 0.3;
        var half = MedianHalf;
        var lane = InnerLane(near);
        if (!TwoWay) warn.Add("via de mão única: o retorno em U precisa de pista nos dois sentidos");
        var mode = r.Tipo;
        if (mode == TipoRetorno.AberturaCanteiro && !median) mode = TipoRetorno.Alargamento;
        var wait = Math.Max(6, r.Espera ?? 2 * (vl + 1.5));
        var ratio = Speed <= 60 ? 8 : 15;
        // Transições de pelo menos 1:12 (as faixas da calçada acompanham sem se sobrepor).
        double Taper(double depth) => Math.Max(8, Math.Ceiling(depth * Math.Max(12, ratio)));

        // Onde o veículo espera e começa o giro.
        double laneU;
        MedianOpening? opening = null;
        var recesses = new List<RecuoVia>();
        var pocketW = 0.0;
        if (mode == TipoRetorno.Bolsao && median && MedianWidth >= Math.Min(3.0, lane) + 1.0)
        {
            pocketW = Math.Min(lane, MedianWidth - 1.0);
            laneU = -half + pocketW / 2;
        }
        else if (mode == TipoRetorno.Bolsao)
        {
            // Bolsão lateral (à direita): o veículo sai da faixa, espera junto ao meio-fio e cruza a via inteira.
            var depth = Math.Max(2.8, Math.Min(3.5, lane));
            laneU = -(nearEdge + depth - 0.5) + vw / 2;   // roda externa a 0,50 m da face do meio-fio do bolsão
            // Largura total na espera e até o início do giro; taper de entrada antes e curta saída depois.
            var (_, nearLot, _) = Nominal(!r.SentidoDoEixo);
            var keep = depth <= nearLot - nearEdge - 1.20 + 1e-6;
            if (!keep && nearWalk) warn.Add($"o bolsão de {depth:0.00} m não deixa 1,20 m de calçada – a calçada inteira se desloca (desapropriação)");
            recesses.Add(ToRecess(r, !r.SentidoDoEixo, -wait - Taper(depth), -wait, 0.5, 0.5 + Taper(depth), depth, keep));
            if (!nearWalk) warn.Add("lado sem calçada: o bolsão lateral avança sobre o acostamento/lote");
        }
        else laneU = -(half + lane / 2);
        var us = laneU - vw / 2;               // roda externa (à direita de quem vira à esquerda)
        var cu = us + ro;                      // centro do giro
        var tc = 0.0;

        // Alargamento da pista oposta.
        var need = us + 2 * ro - (farEdge - 0.5);
        var widen = need > 0.01 ? Math.Ceiling(need * 20) / 20 : 0;
        var k = Math.Clamp((farEdge - 0.5 - cu) / ro, -1, 1);
        var turnEnd = tc + ro * Math.Sqrt(Math.Max(0, 1 - k * k));
        if (widen > 0)
        {
            {
                var room = farWalk ? farLot - farEdge - 1.20 : 0;
                var keepLot = widen <= room + 1e-6;
                if (!keepLot)
                    warn.Add($"o giro do {r.Veiculo} pede {widen:0.00} m além do meio-fio da pista oposta – a calçada inteira se desloca (desapropriação); " +
                             "considere rotatória ou retorno em quadra");
                // Recuo derivado na pista oposta: largura total do centro do giro até a saída; transições no sentido dela.
                var full0 = tc - 2;
                var full1 = Math.Max(turnEnd, tc + 1) + 1;
                var tIn = Taper(widen);                                // entrada (quem segue na pista oposta chega por t maior)
                var tOut = Taper(widen);                               // saída/convergência (t menor)
                recesses.Add(ToRecess(r, r.SentidoDoEixo, full0 - tOut, full0, full1, full1 + tIn, widen, keepLot));
            }
        }

        // Abertura do canteiro onde a faixa varrida o cruza.
        if (median)
        {
            var d = Math.Max(0, Math.Max(cu - half, -half - cu));
            var reach = ro + 0.5;
            var tHi = tc + Math.Sqrt(Math.Max(0, reach * reach - d * d));
            var a = tc - 1.0;
            opening = new MedianOpening { Start = Sta(r, a), End = Sta(r, tHi), Source = r.Id };
            if (pocketW > 0)
            {
                var taper = Math.Max(10, Math.Round(pocketW * ratio));
                opening.PocketWidth = pocketW;
                opening.PocketSide = r.SentidoDoEixo ? -1 : 1;
                opening.PocketFull = Sta(r, a - wait);
                opening.PocketTaper = Sta(r, a - wait - taper);
            }
        }
        else if (Center == CenterTreatment.Canteiro && TwoWay && !median)
            warn.Add("canteiro pintado ou com barreira: interrompa o dispositivo/barreira no vão do retorno");

        var waitStart = pocketW > 0 ? -1.0 - wait : -wait;
        return new RetornoPlan(r, ro, ri, vw, laneU, cu, -nearEdge, farEdge, widen, median, -half, half, opening, recesses, turnEnd, waitStart, warn);
    }

    private static double Sta(RetornoVia r, double t) => r.Estaca + (r.SentidoDoEixo ? t : -t);

    /// <summary>Recuo (estacas crescentes) a partir de um trecho em t: [t0 (taper), t1 (total), t2 (total), t3 (taper)].</summary>
    private static RecuoVia ToRecess(RetornoVia r, bool left, double t0, double t1, double t2, double t3, double depth, bool keepLot)
    {
        var s = new[] { Sta(r, t0), Sta(r, t1), Sta(r, t2), Sta(r, t3) };
        if (!r.SentidoDoEixo) Array.Reverse(s);
        return new RecuoVia
        {
            Tipo = TipoRecuo.Retorno, LadoEsquerdo = left, Estaca = s[0], TaperEntrada = s[1] - s[0], Comprimento = s[2] - s[1], TaperSaida = s[3] - s[2],
            Profundidade = depth, CalcadaRecua = keepLot, Sinalizacao = false, PlacaEAbrigo = false,
        };
    }

    /// <summary>Planos dos retornos e os recuos derivados (alargamentos e bolsões laterais) com as estacas no eixo.</summary>
    private List<RetornoPlan> RetornoPlans() => TwoWay ? Retornos.Select(PlanRetorno).ToList() : new List<RetornoPlan>();

    /// <summary>
    /// Retornos na geração da via: abertura/bolsão no pavimento, interrupção do canteiro e das linhas junto a ele, vagas
    /// interrompidas no giro e a sinalização (setas PEM-RE, R-3 nas pontas do canteiro, R-5a para o sentido oposto, R-6a nos
    /// alargamentos e bolsões, LMS no bolsão do canteiro).
    /// </summary>
    private void ApplyRetornos(List<MarkingDefinition> res, RoadPavementDefinition? pav, List<RetornoPlan> plans, Polyline2? axis,
        Func<MarkingDefinition, MarkingDefinition> add)
    {
        if (plans.Count == 0) return;
        foreach (var p in plans)
        {
            foreach (var w in p.Warnings) Warnings.Add($"Retorno na estaca {PontoLargura.FormatEstaca(p.Spec.Estaca)}: {w}.");
            if (p.Opening != null && pav != null) pav.MedianOpenings.Add(p.Opening);
            var sigmaFar = p.Spec.SentidoDoEixo ? 1 : -1;
            // Canteiro e linhas junto a ele interrompidos no trecho refeito pela abertura.
            if (p.Opening != null)
            {
                var (s0, s1) = Generators.RoadGenerator.MedianOpeningSpan(p.Opening, MedianWidth);
                var (a, b) = (Math.Min(p.Opening.Start, p.Opening.End), Math.Max(p.Opening.Start, p.Opening.End));
                foreach (var l in res.OfType<LinearMarkingDefinition>())
                {
                    var o = Math.Abs(l.Offset);
                    if (o < MedianHalf + 1e-6 && Generators.IntersectionGenerator.IsPhysical(l))
                        l.Breaks.Add(new StationRange { Start = s0, End = s1 });
                    else if (Math.Abs(o - (MedianHalf + EdgeInset)) < 0.3 && !Generators.IntersectionGenerator.IsPhysical(l))
                    {
                        // Linha junto ao canteiro: interrompida no vão; do lado do bolsão, no bolsão inteiro.
                        var pocketSide = p.Opening.PocketWidth > 0 && Math.Sign(l.Offset) == p.Opening.PocketSide;
                        var (c0, c1) = pocketSide ? (Math.Min(a, p.Opening.PocketTaper), Math.Max(b, p.Opening.PocketTaper)) : (a - 0.5, b + 0.5);
                        l.Breaks.Add(new StationRange { Start = c0, End = c1 });
                    }
                }
            }
            // Vagas da pista oposta no giro (o veículo usa a faixa de estacionamento) – CTB art. 181.
            var farRange = (Math.Min(p.Station(-3), p.Station(p.TurnEnd + 3)), Math.Max(p.Station(-3), p.Station(p.TurnEnd + 3)));
            foreach (var pk in res.OfType<ParkingMarkingDefinition>().Where(x => ParkingSideOf(x) == sigmaFar))
                pk.Breaks.Add(new StationRange { Start = farRange.Item1, End = farRange.Item2 });
            if (!p.Spec.Sinalizacao) continue;
            var ax = axis ?? (pav?.PathRef is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points) : null);
            if (ax == null) continue;
            Vec2 At(double t, double u)
            {
                var s = Math.Clamp(p.Station(t), 0, ax.Length);
                return ax.PointAt(s) + ax.TangentAt(s).PerpLeft * p.Offset(u);
            }
            Vec2 Dir(double t) => ax.TangentAt(Math.Clamp(p.Station(t), 0, ax.Length)) * (p.Spec.SentidoDoEixo ? 1 : -1);
            // Setas de retorno na faixa de espera.
            foreach (var t in new[] { -6.0, -21.0 })
                if (t > p.WaitStart - 1 || t == -6.0)
                    add(new SymbolMarkingDefinition { Code = "PEM-RE", Length = 5.0, Position = At(t - 4.0, p.LaneU - 1.0), Direction = Dir(t) });
            if (p.Opening is { } op)
            {
                var (a, b) = (Math.Min(op.Start, op.End), Math.Max(op.Start, op.End));
                var tA = p.Spec.SentidoDoEixo ? a - p.Spec.Estaca : p.Spec.Estaca - b;
                var tB = p.Spec.SentidoDoEixo ? b - p.Spec.Estaca : p.Spec.Estaca - a;
                var mid = (p.MedianLo + p.MedianHi) / 2;
                var noseR = (p.MedianHi - p.MedianLo) / 2;
                // R-3 nas pontas, voltadas para o vão: ninguém entra na contramão ao cruzar o canteiro.
                add(new SignDefinition { Code = "R-3", Position = At(tB + noseR + 0.4, mid), Direction = Dir(0) });
                add(new SignDefinition { Code = "R-3", Position = At(tA - noseR - 0.4, mid), Direction = -Dir(0) });
                // R-5a: o sentido oposto não retorna aqui (o giro foi dimensionado para um sentido).
                add(new SignDefinition { Code = "R-5a", Position = At(tB + noseR + 1.6, mid), Direction = -Dir(0) });
                if (op.PocketWidth > 0.3)
                {
                    var tF = p.Spec.SentidoDoEixo ? op.PocketFull - p.Spec.Estaca : p.Spec.Estaca - op.PocketFull;
                    var tT = p.Spec.SentidoDoEixo ? op.PocketTaper - p.Spec.Estaca : p.Spec.Estaca - op.PocketTaper;
                    var edgeU = p.MedianLo - 0.10;
                    add(new LinearMarkingDefinition { Code = "LMS-1", PathRef = PathReference.FromPoints(new[] { At(tF, edgeU), At(tA, edgeU) }, pav?.PathRef.Z ?? 0) });
                    add(new LinearMarkingDefinition { Code = "LMS-2", PathRef = PathReference.FromPoints(new[] { At(tT, edgeU), At(tF, edgeU) }, pav?.PathRef.Z ?? 0) });
                }
            }
            // R-6a nos alargamentos e bolsões laterais.
            foreach (var rc in p.Recesses)
            {
                var farSide = rc.LadoEsquerdo == p.Spec.SentidoDoEixo;
                var (edge, dir) = farSide ? (p.FarEdge + p.FarWidening, -Dir(0)) : (-p.NearEdge, Dir(0));
                var t0 = farSide ? p.TurnEnd + 3 : p.WaitStart;
                var u = farSide ? edge + 0.6 : -(edge + rc.Profundidade + 0.6);
                add(new SignDefinition { Code = "R-6a", Position = At(t0, u), Direction = dir });
            }
        }
    }

    private static int ParkingSideOf(ParkingMarkingDefinition p)
    {
        var sideSign = p.RightSide ? -1.0 : 1.0;
        return Math.Abs(p.CurbOffset) < 1e-9 ? (int)sideSign : Math.Sign(sideSign * p.CurbOffset);
    }
}

/// <summary>Nova via menor saindo de um ponto de uma via existente (acesso em T).</summary>
public static class AcessoVia
{
    /// <summary>
    /// Eixo da nova via: do eixo da via existente (projeção do ponto clicado) para o lado do clique, com o ângulo pedido em
    /// relação ao sentido do eixo existente (90° = perpendicular) e o comprimento informado.
    /// </summary>
    public static List<Vec2> BranchAxis(Polyline2 host, Vec2 click, double angleDeg, double length)
    {
        var (st, signed) = host.Project(click);
        var p0 = host.PointAt(st);
        var t = host.TangentAt(st);
        var side = signed >= 0 ? 1.0 : -1.0;
        var a = Math.Clamp(angleDeg, 30, 150) * Math.PI / 180;
        // Ângulo medido a partir do sentido do eixo existente, girando para o lado do clique.
        var dir = (t * Math.Cos(a) + t.PerpLeft * (side * Math.Sin(a))).Normalized();
        return new List<Vec2> { p0, p0 + dir * Math.Max(10, length) };
    }
}

