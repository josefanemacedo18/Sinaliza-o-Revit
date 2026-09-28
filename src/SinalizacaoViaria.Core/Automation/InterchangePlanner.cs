using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Via do plugin vista pelo planejador de nós: eixo, cota da base, greide e larguras (faces dos meios-fios / bordos).</summary>
public sealed record PlanRoad(string Id, Polyline2 Axis, double BaseZ, RoadGrade Grade, double HalfLeft, double HalfRight, double TotalLeft, double TotalRight)
{
    /// <summary>Cota absoluta da pista no ponto (m).</summary>
    public double Z(Vec2 p) => BaseZ + Grade.Z(Axis.Project(p).Station);
    public double Station(Vec2 p) => Axis.Project(p).Station;
    public double TotalHalf => Math.Max(TotalLeft, TotalRight);

    public static PlanRoad Of(RoadPavementDefinition pav, Polyline2 axis, double baseZ) =>
        new(pav.GroupId ?? pav.Id, axis, baseZ, pav.Output.Grade ?? RoadGrade.Flat(axis.Length), pav.LeftWidth, pav.RightWidth, pav.TotalLeft, pav.TotalRight);
}

/// <summary>Como a ponta de um ramo se liga à via.</summary>
public enum LigacaoRamo
{
    /// <summary>Faixa de mudança de velocidade paralela (saída/entrada em convergência), com taper.</summary>
    Paralela,
    /// <summary>Termina no eixo da via: interseção em nível (PARE / dê a preferência).</summary>
    Terminal,
    /// <summary>Termina no centro de uma rotatória.</summary>
    Rotatoria,
}

/// <summary>Um ramo do nó (via do plugin): eixo no bordo esquerdo da faixa, greide, ligações nas pontas e trechos em ponte.</summary>
public sealed class RampPlan
{
    public string Name { get; init; } = "";
    public PapelRamo Role { get; init; }
    /// <summary>Eixo da via do ramo = bordo esquerdo da faixa de rolamento (a faixa e o acostamento ficam à direita).</summary>
    public required Polyline2 Axis { get; init; }
    public required RoadGrade Grade { get; init; }
    public double BaseZ { get; init; }
    public LigacaoRamo StartLink { get; init; }
    public LigacaoRamo EndLink { get; init; }
    public string? StartRoad { get; init; }
    public string? EndRoad { get; init; }
    public double LaneWidth { get; init; } = 4.0;
    public double Shoulder { get; init; } = 2.0;
    public double Taper { get; init; } = 60;
    public List<(double S0, double S1)> Bridges { get; } = new();
}

/// <summary>Plano do nó viário sobre duas vias do plugin.</summary>
public sealed class InterchangePlan
{
    public Vec2 Node { get; set; }
    /// <summary>A via transversal passa por cima (senão, a principal).</summary>
    public bool CrossOver { get; set; }
    public string OverId { get; set; } = "";
    public string UnderId { get; set; } = "";
    /// <summary>Greide da via de cima com a elevação sobre a outra (relativo à base dela).</summary>
    public RoadGrade OverGrade { get; set; } = new();
    /// <summary>Greide da via de cima antes do nó.</summary>
    public RoadGrade OverBase { get; set; } = new();
    public double BridgeS0 { get; set; }
    public double BridgeS1 { get; set; }
    public double Skew { get; set; }
    /// <summary>Estações dos pilares (relativas ao início da obra).</summary>
    public List<double> PierStations { get; } = new();
    public List<RampPlan> Ramps { get; } = new();
    public List<(Vec2 Center, double Radius, double Z)> Roundabouts { get; } = new();
    /// <summary>Rotatória elevada: trechos do anel sobre a via de baixo (ângulos inicial e final, rad).</summary>
    public List<(double A0, double A1)> RingDecks { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Monta o nó viário a partir de duas vias do plugin que se cruzam: eleva a via de cima (greide com gabarito e viaduto
/// esconso), traça os ramos (diamante, laços, ramos externos, semidireta) como vias novas com clotoides e greide que
/// concorda com as duas vias, e define as ligações (faixas paralelas com taper, interseções ou rotatórias nos terminais).
/// </summary>
public static class InterchangePlanner
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Cruzamento dos eixos mais próximo de <paramref name="near"/> (nulo se não se cruzam).</summary>
    public static Vec2? Crossing(Polyline2 a, Polyline2 b, Vec2 near)
    {
        Vec2? best = null;
        var bd = double.MaxValue;
        for (int i = 0; i + 1 < a.Points.Count; i++)
            for (int j = 0; j + 1 < b.Points.Count; j++)
            {
                var p = a.Points[i];
                var r = a.Points[i + 1] - p;
                var q = b.Points[j];
                var s = b.Points[j + 1] - q;
                var den = r.Cross(s);
                if (Math.Abs(den) < 1e-12) continue;
                var t = (q - p).Cross(s) / den;
                var u = (q - p).Cross(r) / den;
                if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) continue;
                var x = p + r * t;
                var d = x.DistanceTo(near);
                if (d < bd) { bd = d; best = x; }
            }
        return best;
    }

    /// <param name="overBase">Greide da via de cima antes do nó (refazer o nó parte dele); nulo = o atual.</param>
    public static InterchangePlan? Plan(InterchangeDefinition d, PlanRoad main, PlanRoad cross, Vec2 near, RoadGrade? overBase = null)
    {
        var plan = new InterchangePlan();
        if (Crossing(main.Axis, cross.Axis, near) is not { } o)
        {
            plan.Warnings.Add("As duas vias não se cruzam: estenda os eixos até o cruzamento.");
            return null;
        }
        plan.Node = o;
        var sm = main.Station(o);
        var sc = cross.Station(o);
        var ex = main.Axis.TangentAt(sm);
        var ey = ex.PerpLeft;
        var ec = cross.Axis.TangentAt(sc);
        var nc = ec.PerpRight;
        var sinA = Math.Abs(ex.Cross(ec));
        if (sinA < 0.5) plan.Warnings.Add("Vias muito oblíquas (menos de 30°): o nó fica alongado – confira os ramos.");
        sinA = Math.Max(0.4, sinA);
        Vec2 W(double x, double y) => o + ex * x + ey * y;
        Vec2 C(double t, double off) => o + ec * t + nc * off;

        // Via de cima: greide elevado sobre a de baixo (gabarito + altura estrutural).
        var crossOver = d.MainBelow;
        plan.CrossOver = crossOver;
        var over = crossOver ? cross : main;
        var under = crossOver ? main : cross;
        plan.OverId = over.Id;
        plan.UnderId = under.Id;
        var H = d.Height;
        var g = Math.Max(0.01, d.RampGrade);
        var so = over.Station(o);
        var zTopRel = under.Z(o) + H - over.BaseZ;
        var terminalsOnOver = crossOver && d.Type is TipoNoViario.Diamante or TipoNoViario.DiamanteRotatorias or TipoNoViario.TrevoParcial;
        var D = Math.Max(cross.TotalHalf + main.TotalHalf + 60, d.TerminalDistance);
        var underSpan = (under.TotalLeft + under.TotalRight) / 2 / sinA + 8;
        var plateau = d.Type == TipoNoViario.RotatoriaElevada ? Math.Max(d.RoundaboutRadius, main.TotalHalf + 18) + 35
            : terminalsOnOver ? D + 35 : underSpan + 20;
        var baseOver = (overBase ?? over.Grade.WithoutWorks ?? over.Grade).Clone();
        baseOver.WithoutWorks = null;
        var og = baseOver.Clone();
        if (og.Points.Count == 0) og.Points.AddRange(new[] { new GradePoint(0, 0), new GradePoint(over.Axis.Length, 0) });
        og.RaiseBetween(Math.Max(0, so - plateau), Math.Min(over.Axis.Length, so + plateau), zTopRel, g, over.Axis.Length, 70);
        // A elevação do nó faz parte do greide de projeto da via (as obras hospedadas se somam a ele).
        og.WithoutWorks = null;
        og.AdjustTerrain = true;
        plan.OverGrade = og;
        plan.OverBase = baseOver;
        var run = Math.Abs(zTopRel - baseOver.Z(so)) / g;
        if (so - plateau - run < 0 || so + plateau + run > over.Axis.Length)
            plan.Warnings.Add($"A via que passa por cima é curta para as rampas do viaduto (precisa de ~{(plateau + run).ToString("0", Pt)} m para cada lado do cruzamento) – estenda o eixo.");
        double OverZ(Vec2 p) => over.BaseZ + og.Z(over.Station(p));
        double MainZ(Vec2 p) => crossOver ? main.Z(p) : OverZ(p);
        double CrossZ(Vec2 p) => crossOver ? OverZ(p) : cross.Z(p);

        // Viaduto da via de cima: vão sobre a de baixo, esconso conforme o ângulo do cruzamento.
        plan.BridgeS0 = Math.Max(0, so - underSpan);
        plan.BridgeS1 = Math.Min(over.Axis.Length, so + underSpan);
        var oDir = over.Axis.TangentAt(so);
        var uDir = (crossOver ? ex : ec);
        var ang = Math.Atan2(oDir.Cross(uDir), oDir.Dot(uDir)) * 180 / Math.PI;       // ângulo da de baixo em relação à de cima
        var skew = 90 - Math.Abs(ang) % 180;
        if (skew > 90) skew -= 180;
        plan.Skew = Math.Clamp(Math.Abs(skew) < 3 ? 0 : skew * Math.Sign(ang), -60, 60);
        if (plan.BridgeS1 - plan.BridgeS0 > 42) plan.PierStations.Add(so - plan.BridgeS0);

        // ---- ramos
        var lane = 4.0;
        var shoulder = Math.Max(0.5, d.RampWidth - lane);
        double MainEdge(int qy) => qy > 0 ? main.HalfLeft : main.HalfRight;
        double CrossEdge(int qx) => qx > 0 ? cross.HalfRight : cross.HalfLeft;          // nc = direita da transversal
        double Eo(int qy) => MainEdge(qy) + lane / 2;
        double E2(int qx) => CrossEdge(qx) + lane / 2;
        var R = Math.Max(30, d.LoopRadius);
        var Rr = Math.Max(60, d.RampRadius);
        var ls = Math.Max(0, d.SpiralLength);
        var lsc = Math.Max(40, d.SpeedChangeLength);
        int Travel(int qy) => qy < 0 ? 1 : -1;                                           // mão direita na principal
        string Q(int qx, int qy) => (qx > 0 ? "L" : "O") + (qy > 0 ? "N" : "S");

        RampPlan Ramp(string name, PapelRamo role, Polyline2 center, Func<Vec2, double> zStart, Func<Vec2, double> zEnd, double flatStart, double flatEnd,
            LigacaoRamo startLink, LigacaoRamo endLink, string? startRoad, string? endRoad, double super = 0)
        {
            var axis = center.Offset(lane / 2);
            var L = axis.Length;
            var gr = new RoadGrade { DefaultCurve = 0, AdjustTerrain = true, CutSlope = 1.0, FillSlope = d.FillSlope };
            var a = Math.Min(flatStart, L * 0.4);
            var b = Math.Max(a + 1, L - Math.Min(flatEnd, L * 0.4));
            var zs = new List<(double S, double Z)>();
            for (var s = 0.0; s <= L + 1e-6; s += 5)
            {
                var p = center.PointAt(Math.Min(s, center.Length));
                var t = Math.Clamp((s - a) / (b - a), 0, 1);
                var w = t * t * (3 - 2 * t);
                zs.Add((s, zStart(p) * (1 - w) + zEnd(p) * w));
            }
            if (zs[^1].S < L - 0.5) zs.Add((L, zEnd(center.PointAt(center.Length))));
            var baseZ = main.BaseZ;
            foreach (var (s, z) in zs) gr.Points.Add(new GradePoint(s, z - baseZ));
            gr.Normalize();
            var maxG = 0.0;
            for (int i = 1; i < zs.Count; i++) maxG = Math.Max(maxG, Math.Abs(zs[i].Z - zs[i - 1].Z) / Math.Max(0.1, zs[i].S - zs[i - 1].S));
            if (maxG > g * 1.3) plan.Warnings.Add($"{name}: rampa de {(maxG * 100).ToString("0.0", Pt)} % (máx. {(g * 100).ToString("0.0", Pt)} %) – aumente os raios ou a distância dos terminais.");
            if (Math.Abs(super) > 1e-6)
            {
                var lt = Math.Min(Math.Max(20, ls), L / 3);
                gr.Superelevation.AddRange(new[] { new Vec2(0, 0), new Vec2(lt, super), new Vec2(L - lt, super), new Vec2(L, 0) });
                gr.CrossfallWidth = lane + shoulder;
            }
            return new RampPlan
            {
                Name = name, Role = role, Axis = axis, Grade = gr, BaseZ = baseZ, StartLink = startLink, EndLink = endLink, StartRoad = startRoad, EndRoad = endRoad,
                LaneWidth = lane, Shoulder = shoulder, Taper = Math.Min(70, lsc * 0.6),
            };
        }

        // Rampa diagonal (diamante): faixa paralela à principal, curva e terminal perpendicular à transversal.
        RampPlan Diamond(int qx, int qy, double dist, Vec2? node = null, LigacaoRamo link = LigacaoRamo.Terminal)
        {
            var exit = qx * Travel(qy) < 0;
            var T = node ?? C(qy * dist, 0);
            var tz = CrossZ(T);
            var rise = Math.Abs(tz - MainZ(W(0, qy * Eo(qy))));
            var xa = CrossEdge(qx) / sinA + rise / g * 0.8 + lsc + 60;
            var A = W(qx * (xa + lsc), qy * Eo(qy));
            var B = W(qx * xa, qy * Eo(qy));
            var approach = node == null ? nc * qx : (T - o).Normalized() * 0 + (nc * qx + ec * (qy * 0.55)).Normalized();
            var P1 = T + approach * (CrossEdge(qx) + 30);
            var P2 = T + approach * (CrossEdge(qx) + 8);
            var pis = new List<Pi> { new(A), new(B, Rr * 2.5, ls), new(P1, Math.Max(40, Rr * 0.55), ls * 0.6), new(P2), new(T) };
            if (!exit) pis.Reverse();
            var path = Tracado.Build(pis, 0.75, plan.Warnings);
            var name = $"Rampa {(exit ? "de saída" : "de entrada")} {Q(qx, qy)}";
            return exit
                ? Ramp(name, PapelRamo.Rampa, path, MainZ, CrossZ, lsc + 40, 25, LigacaoRamo.Paralela, link, main.Id, cross.Id)
                : Ramp(name, PapelRamo.Rampa, path, CrossZ, MainZ, 25, lsc + 40, link, LigacaoRamo.Paralela, cross.Id, main.Id);
        }

        Vec2 LoopCenter(int qx, int qy)
        {
            var yL = qy * (R + Eo(qy));
            var nx = nc.Dot(ex);
            var ny = nc.Dot(ey);
            var xL = (qx * (R + E2(qx)) - yL * ny) / (Math.Abs(nx) < 1e-6 ? 1e-6 : nx);
            return W(xL, yL);
        }

        // Laço de 270° (horário – mão direita) com faixa de desaceleração na principal e de aceleração na transversal.
        RampPlan Loop(int qx, int qy)
        {
            var c = LoopCenter(qx, qy);
            var tm = c - ey * (qy * R);
            var tc = c - nc * (qx * R);
            var exit = qx * Travel(qy) > 0;
            var a0 = Math.Atan2((exit ? tm : tc).Y - c.Y, (exit ? tm : tc).X - c.X);
            var a1 = Math.Atan2((exit ? tc : tm).Y - c.Y, (exit ? tc : tm).X - c.X);
            var sweep = a1 - a0;
            while (sweep >= 0) sweep -= 2 * Math.PI;
            while (sweep < -2 * Math.PI) sweep += 2 * Math.PI;
            var arc = CurveTools.Arc(c, R, a0, sweep, 0.004);
            var mainTravel = ex * Travel(qy);
            var pts = new List<Vec2>();
            var dirEnd = (arc[^1] - arc[^2]).Normalized();
            var dirStart = (arc[1] - arc[0]).Normalized();
            if (exit)
            {
                pts.Add(tm - mainTravel * lsc);
                pts.AddRange(arc);
                pts.Add(arc[^1] + dirEnd * lsc);
            }
            else
            {
                pts.Add(arc[0] - dirStart * lsc);
                pts.AddRange(arc);
                pts.Add(tm + mainTravel * lsc);
            }
            var path = new Polyline2(pts);
            var name = $"Laço {Q(qx, qy)}";
            return exit
                ? Ramp(name, PapelRamo.Laco, path, MainZ, CrossZ, lsc, lsc, LigacaoRamo.Paralela, LigacaoRamo.Paralela, main.Id, cross.Id, Math.Abs(d.Superelevation))
                : Ramp(name, PapelRamo.Laco, path, CrossZ, MainZ, lsc, lsc, LigacaoRamo.Paralela, LigacaoRamo.Paralela, cross.Id, main.Id, Math.Abs(d.Superelevation));
        }

        // Ramo externo (conversão à direita) contornando o laço do quadrante.
        RampPlan Outer(int qx, int qy, double gap = 24)
        {
            var c = LoopCenter(qx, qy);
            var u = (ey * qy + nc * qx).Normalized();
            var off = R + lane + gap;
            var xA = (off - (o + ey * (qy * Eo(qy)) - c).Dot(u)) / ex.Dot(u);
            var A = o + ex * xA + ey * (qy * Eo(qy));
            var tB = (off - (o + nc * (qx * E2(qx)) - c).Dot(u)) / ec.Dot(u);
            var B = o + nc * (qx * E2(qx)) + ec * tB;
            var A0 = A + ex * (Math.Sign(xA) * (lsc + 60));
            var B0 = B + ec * (Math.Sign(tB) * (lsc + 40));
            var exit = qx * Travel(qy) < 0;
            var pis = new List<Pi> { new(A0), new(A, Rr, ls), new(B, Rr, ls), new(B0) };
            if (!exit) pis.Reverse();
            var path = Tracado.Build(pis, 0.75, plan.Warnings);
            var name = $"Ramo externo {Q(qx, qy)}";
            return exit
                ? Ramp(name, PapelRamo.Externo, path, MainZ, CrossZ, lsc + 20, lsc, LigacaoRamo.Paralela, LigacaoRamo.Paralela, main.Id, cross.Id)
                : Ramp(name, PapelRamo.Externo, path, CrossZ, MainZ, lsc, lsc + 20, LigacaoRamo.Paralela, LigacaoRamo.Paralela, cross.Id, main.Id);
        }

        switch (d.Type)
        {
            case TipoNoViario.Diamante:
                foreach (var (qx, qy) in new[] { (1, 1), (-1, 1), (-1, -1), (1, -1) }) plan.Ramps.Add(Diamond(qx, qy, D));
                break;
            case TipoNoViario.DiamanteRotatorias:
            {
                var rr = Math.Max(15, d.RoundaboutRadius);
                foreach (var qy in new[] { -1, 1 })
                {
                    var center = C(qy * D, 0);
                    plan.Roundabouts.Add((center, rr, CrossZ(center)));
                    foreach (var qx in new[] { -1, 1 }) plan.Ramps.Add(Diamond(qx, qy, D, center, LigacaoRamo.Rotatoria));
                }
                break;
            }
            case TipoNoViario.TrevoCompleto:
                foreach (var (qx, qy) in new[] { (1, 1), (-1, 1), (-1, -1), (1, -1) })
                {
                    plan.Ramps.Add(Loop(qx, qy));
                    plan.Ramps.Add(Outer(qx, qy));
                }
                break;
            case TipoNoViario.TrevoParcial:
                foreach (var (qx, qy) in new[] { (1, -1), (-1, 1) })
                {
                    plan.Ramps.Add(Loop(qx, qy));
                    plan.Ramps.Add(Outer(qx, qy));
                }
                plan.Ramps.Add(Diamond(-1, -1, D));
                plan.Ramps.Add(Diamond(1, 1, D));
                break;
            case TipoNoViario.Trombeta:
            {
                // Entroncamento: a haste (lado da transversal com mais extensão) cruza a principal no viaduto e forma o "sino" do
                // outro lado. Conversões à direita pelos ramos diretos do lado da haste; à esquerda pelos laços do lado oposto (o
                // veículo passa pelo viaduto) – todos os movimentos entre a haste e os dois sentidos da principal.
                var side = cross.Axis.Length - sc >= sc ? 1 : -1;
                var qy = (ec * side).Dot(ey) >= 0 ? 1 : -1;
                if (side > 0 ? sc < underSpan + 2 * R : cross.Axis.Length - sc < underSpan + 2 * R)
                    plan.Warnings.Add("Trombeta: a transversal precisa continuar do outro lado da principal (onde ficam os laços) – estenda o eixo.");
                plan.Ramps.Add(Outer(1, qy));
                plan.Ramps.Add(Outer(-1, qy));
                plan.Ramps.Add(Loop(1, -qy));
                plan.Ramps.Add(Loop(-1, -qy));
                break;
            }
            case TipoNoViario.RotatoriaElevada:
            {
                var rr = Math.Max(d.RoundaboutRadius, main.TotalHalf + 18);
                var zr = CrossZ(o);
                plan.Roundabouts.Add((o, rr, zr));
                foreach (var (qx, qy) in new[] { (1, 1), (-1, 1), (-1, -1), (1, -1) }) plan.Ramps.Add(Diamond(qx, qy, 0, o, LigacaoRamo.Rotatoria));
                // Trechos do anel sobre a principal (a principal passa pelo centro, na direção ex).
                var ringR = rr - 4.5;
                var span = Math.Asin(Math.Min(0.95, (main.TotalHalf + 6) / ringR)) + 0.10;
                foreach (var along in new[] { ex, -ex })
                {
                    var a = Math.Atan2(along.Y, along.X);
                    plan.RingDecks.Add((a - span, a + span));
                }
                break;
            }
        }

        // Trechos em ponte dos ramos: onde passam sobre a principal, a transversal ou outro ramo com gabarito.
        var others = new List<(Polyline2 Axis, Func<Vec2, double> Z, double Half)>
        {
            (main.Axis, MainZ, main.TotalHalf), (cross.Axis, CrossZ, cross.TotalHalf),
        };
        foreach (var r in plan.Ramps)
        {
            var L = r.Axis.Length;
            var marks = new List<double>();
            for (var s = 0.0; s <= L; s += 2)
            {
                var p = r.Axis.PointAt(s);
                var z = r.BaseZ + r.Grade.Z(s);
                // Dentro de uma rotatória (terminal): a pista é da rotatória, sem ponte do ramo.
                if (plan.Roundabouts.Any(rb => rb.Center.DistanceTo(p) < rb.Radius + 6)) continue;
                var overAny = others.Any(t =>
                {
                    var pr = t.Axis.Project(p);
                    return Math.Abs(pr.Signed) < t.Half - 0.3 && z - t.Z(p) > d.Clearance * 0.8;
                }) || plan.Ramps.Any(x =>
                {
                    if (ReferenceEquals(x, r)) return false;
                    var pr = x.Axis.Project(p);
                    return pr.Signed < 0.3 && pr.Signed > -(x.LaneWidth + x.Shoulder) && z - (x.BaseZ + x.Grade.Z(pr.Station)) > d.Clearance * 0.8;
                });
                if (overAny) marks.Add(s);
            }
            foreach (var s in marks)
            {
                if (r.Bridges.Count > 0 && s - r.Bridges[^1].S1 <= 8) r.Bridges[^1] = (r.Bridges[^1].S0, s);
                else r.Bridges.Add((s, s));
            }
            for (int i = 0; i < r.Bridges.Count; i++) r.Bridges[i] = (Math.Max(0, r.Bridges[i].S0 - 12), Math.Min(L, r.Bridges[i].S1 + 12));
        }
        return plan;
    }

    /// <summary>Seção de via de um ramo: faixa única à direita do eixo (bordo esquerdo) e acostamento; mão única.</summary>
    public static RoadSetup RampSetup(RampPlan r, bool guardrails, double speed = 50)
    {
        var s = new RoadSetup
        {
            TwoWay = false, Center = CenterTreatment.Nenhum, Hierarchy = HierarquiaViaria.Arterial, Speed = speed, Inscriptions = true, EdgeLines = true,
        };
        s.Right.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = r.LaneWidth });
        s.Right.Add(new ElementoSecao { Tipo = TipoElementoSecao.Acostamento, Largura = r.Shoulder, Dispositivo = guardrails ? "DEF" : null });
        return s;
    }

    /// <summary>
    /// Áreas do taper de cada ponta paralela do ramo (recortadas da própria pista do ramo): a faixa começa com largura zero
    /// junto à via e se alarga até a largura total ao fim do taper.
    /// </summary>
    public static List<Polygon2> TaperCuts(RampPlan r)
    {
        var res = new List<Polygon2>();
        var L = r.Axis.Length;
        var w = r.LaneWidth + r.Shoulder + 1.5;
        Vec2 P(double s, double y) => r.Axis.PointAt(Math.Clamp(s, 0, L)) + r.Axis.TangentAt(Math.Clamp(s, 0, L)).PerpLeft * y;
        var t = Math.Min(r.Taper, L / 3);
        if (r.StartLink == LigacaoRamo.Paralela)
            res.Add(new Polygon2(new[] { P(-1, 0.05), P(t, -r.LaneWidth), P(t, -w), P(-1, -w) }));
        if (r.EndLink == LigacaoRamo.Paralela)
            res.Add(new Polygon2(new[] { P(L + 1, 0.05), P(L + 1, -w), P(L - t, -w), P(L - t, -r.LaneWidth) }));
        return res;
    }

    /// <summary>Faixa ocupada pelo ramo junto a cada via onde ele corre paralelo (para recortar calçadas, meios-fios e bordos dela).</summary>
    public static List<Polygon2> JoinFootprints(RampPlan r, double length = 400)
    {
        var res = new List<Polygon2>();
        var L = r.Axis.Length;
        var w = r.LaneWidth + r.Shoulder;
        foreach (var (a, b, link) in new[] { (0.0, Math.Min(L, length), r.StartLink), (Math.Max(0, L - length), L, r.EndLink) })
        {
            if (link != LigacaoRamo.Paralela) continue;
            var left = new List<Vec2>();
            var right = new List<Vec2>();
            for (var s = a; s <= b + 1e-6; s += 2)
            {
                var p = r.Axis.PointAt(s);
                var n = r.Axis.TangentAt(s).PerpLeft;
                left.Add(p + n * 0.3);
                right.Add(p - n * (w + 0.2));
            }
            right.Reverse();
            res.Add(new Polygon2(left.Concat(right)));
        }
        return res;
    }
}
