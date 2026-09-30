using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Orelha de uma esquina da interseção: ramo A (lado +o, dono da esquina – a esquina à direita de quem chega por ele) e
/// ramo B (lado −o, o ramo seguinte no sentido anti-horário), avanço ao longo de cada um a partir do fim da curva, largura e
/// o caminho na face do meio-fio (de A para B, contornando a curva; a calçada fica à direita do caminho).
/// </summary>
public sealed record EarPlan(IntersectionLeg LegA, IntersectionLeg LegB, bool SideA, bool SideB, double LenA, double LenB, double Depth,
    Polyline2 Path, Polygon2 Footprint, CurbExtensionDefinition Template);

/// <summary>Trecho de um lado de ramo coberto por orelha: avanço e estacas (t) onde a orelha tem a largura toda.</summary>
public sealed record EarSide(double Depth, double TFullStart, double TFullEnd, double TStart, double TEnd);

public static partial class IntersectionGenerator
{
    /// <summary>
    /// Largura da faixa de estacionamento junto ao meio-fio de um lado da via (com a sarjeta somada), lida da seção gravada
    /// na via. Nulo = aquele lado não tem estacionamento junto ao meio-fio (a orelha invadiria uma faixa de rolamento).
    /// </summary>
    public static double? ParkingDepth(RoadPavementDefinition r, bool left)
    {
        var setup = RoadTemplates.FromJson(r.SetupJson);
        if (setup == null) return null;
        var side = left ? setup.Left : setup.Right;
        var k = side.FindIndex(e => e.Tipo == TipoElementoSecao.Calcada);
        var last = k < 0 ? side.Count - 1 : k - 1;
        if (last < 0 || side[last].Tipo != TipoElementoSecao.Estacionamento) return null;
        var gutter = k >= 0 && setup.SarjetaSomada == true && setup.PhysicalElements && !side[last].Elevado ? Math.Max(0, side[k].Sarjeta) : 0;
        return side[last].Largura + gutter;
    }

    /// <summary>Comprimento da ponta da orelha ao longo do meio-fio (da face até o avanço inteiro).</summary>
    public static double EarTransition(TipoTransicao kind, double radius, double depth) => kind switch
    {
        TipoTransicao.Reta => 0.001,
        TipoTransicao.Chanfro => Math.Max(0.05, radius),
        _ => depth <= 2 * Math.Max(0.1, radius) ? Math.Sqrt(Math.Max(0, 4 * Math.Max(0.1, radius) * depth - depth * depth)) : 2 * Math.Max(0.1, radius),
    };

    /// <summary>Largura da orelha num lado do ramo (+1 alto, −1 baixo) na estaca t (0 fora dela; afina nas pontas).</summary>
    public static double EarInset(IntersectionLayout L, IntersectionLeg leg, int side, double t)
    {
        if (!L.EarSides.TryGetValue((leg.Road, leg.Sign, side), out var e)) return 0;
        if (t < e.TStart || t > e.TEnd) return 0;
        if (t >= e.TFullStart && t <= e.TFullEnd) return e.Depth;
        return t < e.TFullStart
            ? e.Depth * (t - e.TStart) / Math.Max(1e-6, e.TFullStart - e.TStart)
            : e.Depth * (e.TEnd - t) / Math.Max(1e-6, e.TEnd - e.TFullEnd);
    }

    /// <summary>Largura da orelha que cobre a travessia do ramo, no lado indicado (0 = sem orelha na travessia).</summary>
    public static double CrosswalkInset(IntersectionLayout L, IntersectionLeg leg, int side) =>
        L.EarSides.TryGetValue((leg.Road, leg.Sign, side), out var e) && e.TFullEnd > e.TFullStart ? e.Depth : 0;

    /// <summary>Marcas da via recortadas pelas orelhas: pavimento, sarjeta e toda a pintura (vagas, linhas, símbolos).</summary>
    public static bool CutByEars(MarkingDefinition m) => m switch
    {
        RoadPavementDefinition => true,
        LinearMarkingDefinition l => l.Code.StartsWith("SARJETA") || !IsPhysical(l),
        HatchMarkingDefinition or ParkingMarkingDefinition or RepeatedMarkingDefinition or SymbolMarkingDefinition or TextMarkingDefinition => true,
        _ => false,
    };

    /// <summary>Esquinas (ramo dono A, ramo seguinte B) com orelha, conforme a opção geral e os ajustes por esquina.</summary>
    private static IEnumerable<(IntersectionLeg A, IntersectionLeg B)> EarCorners(IntersectionDefinition d, IntersectionLayout L)
    {
        if (L.IsBend || L.Legs.Count < 3) yield break;
        var sorted = L.Legs.Select(l => (Leg: l, A: AngleOf(l.Dir))).OrderBy(x => x.A).ToList();
        for (int k = 0; k < sorted.Count; k++)
        {
            var (la, aa) = sorted[k];
            var (lb, ab) = sorted[(k + 1) % sorted.Count];
            if (Straight(la, lb, Ccw(aa, ab))) continue;
            if (!(LegSet(d, L, la)?.CurbExtension ?? d.CurbExtensions)) continue;
            yield return (la, lb);
        }
    }

    /// <summary>
    /// Planeja as orelhas das esquinas: lados com estacionamento (ou avanço informado), avanço, comprimento (pedido, até o
    /// início do estacionamento, e sempre cobrindo a travessia e as rampas daquele lado, que passam para a borda da orelha) e
    /// o caminho na face do meio-fio. <paramref name="provisional"/>: comprimentos com folga (antes das rampas finais).
    /// </summary>
    private static void PlanEars(IntersectionDefinition d, IntersectionLayout L, List<Polygon2> pav, bool provisional)
    {
        L.Ears.Clear();
        L.EarSides.Clear();
        var notes = new List<string>();
        foreach (var (la, lb) in EarCorners(d, L).ToList())
        {
            if (d.RightTurnIslands != TipoIlha.Nenhuma)
            {
                notes.Add("esquinas canalizadas (faixa de conversão livre) não recebem orelha");
                break;
            }
            var ls = LegSet(d, L, la);
            var ra = L.Roads[la.Road].Def;
            var rb = L.Roads[lb.Road].Def;
            // Lado alto do ramo A e lado baixo do ramo B (a esquerda da via para Sign > 0 é o lado alto).
            var parkA = ParkingDepth(ra, la.Sign > 0);
            var parkB = ParkingDepth(rb, lb.Sign < 0);
            var explicitDepth = d.CurbExtensionDepth is > 0.1 ? d.CurbExtensionDepth : null;
            var sideA = parkA != null || explicitDepth != null && (la.Sign > 0 ? ra.LeftSidewalk : ra.RightSidewalk) > 0.5;
            var sideB = parkB != null || explicitDepth != null && (lb.Sign > 0 ? rb.RightSidewalk : rb.LeftSidewalk) > 0.5;
            // Avanço 0 informado num lado: a orelha fica só ao longo da outra via (não contorna a esquina).
            if (ls?.CurbExtensionLength is < 0.05) sideA = false;
            if (ls?.CurbExtensionLengthOther is < 0.05) sideB = false;
            if (!sideA && !sideB)
            {
                notes.Add("esquina sem faixa de estacionamento junto ao meio-fio não recebe orelha (informe a largura do avanço para forçar)");
                continue;
            }
            var depth = explicitDepth ?? new[] { sideA ? parkA : null, sideB ? parkB : null }.Where(x => x != null).Min()!.Value;
            depth = Math.Clamp(depth, 0.3, 10);
            var toParking = ls?.CurbExtensionToParking ?? d.CurbExtensionToParking;
            var lt = EarTransition(d.CurbExtensionEnds, d.CurbExtensionEndRadius, depth);
            double Length(IntersectionLeg leg, double? requested, int side)
            {
                var len = toParking ? StopFar(d, L, leg) + 5.0 : Math.Max(0.5, requested is > 0.05 ? requested.Value : d.CurbExtensionLength);
                // A travessia deste lado fica sobre a orelha: a orelha vai além da faixa e das rampas (com a ponta).
                if (CrosswalkOn(d, L, leg))
                {
                    var plan = RampPlanOf(d, L, leg);
                    var rp = side > 0 ? plan.Hi : plan.Lo;
                    var ext = Math.Max(CrosswalkWidthOf(d, L, leg) / 2, rp == null ? 0 : RampHalfExtent(rp));
                    var need = CrosswalkT(d, L, leg) - leg.Clear + ext + lt + 0.3 + (provisional ? 2.5 : 0);
                    if (len < need - 1e-6)
                    {
                        if (!provisional) notes.Add($"orelha estendida para {need:0.00} m ao longo da via para a travessia e as rampas ficarem sobre ela");
                        len = need;
                    }
                }
                return Math.Min(len, Math.Max(1.0, MaxT(L, leg) - leg.Clear - 2));
            }
            var lenA = sideA ? Length(la, ls?.CurbExtensionLength, 1) : 0;
            var lenB = sideB ? Length(lb, ls?.CurbExtensionLengthOther ?? (ls?.CurbExtensionLength is > 0.05 ? ls.CurbExtensionLength : null), -1) : 0;
            var path = CornerPath(L, pav, la, lb, sideA ? lenA : 0, sideB ? lenB : 0, sideA && sideB);
            if (path == null || path.Length < 1)
            {
                notes.Add("não foi possível seguir o meio-fio de uma esquina para a orelha");
                continue;
            }
            var tpl = new CurbExtensionDefinition
            {
                Depth = depth, Transition = d.CurbExtensionEnds, EndTransition = d.CurbExtensionEnds,
                Radius = d.CurbExtensionEndRadius, EndRadius = d.CurbExtensionEndRadius, SidewalkOnLeft = false,
                Height = L.CurbHeight, CurbWidth = L.CurbWidth, CutRoadMarkings = true,
            };
            var fp = SidewalkGenerator.EarFootprint(tpl, path);
            if (fp == null) continue;
            L.Ears.Add(new EarPlan(la, lb, sideA, sideB, lenA, lenB, depth, path, fp, tpl));
            // Trechos de cada lado com a largura toda (a ponta da curva da esquina fica cheia quando a orelha contorna a esquina).
            if (sideA)
                L.EarSides[(la.Road, la.Sign, 1)] = sideB
                    ? new EarSide(depth, 0, la.Clear + lenA - lt, 0, la.Clear + lenA)
                    : new EarSide(depth, la.Clear + lt, la.Clear + lenA - lt, la.Clear, la.Clear + lenA);
            if (sideB)
                L.EarSides[(lb.Road, lb.Sign, -1)] = sideA
                    ? new EarSide(depth, 0, lb.Clear + lenB - lt, 0, lb.Clear + lenB)
                    : new EarSide(depth, lb.Clear + lt, lb.Clear + lenB - lt, lb.Clear, lb.Clear + lenB);
        }
        if (!provisional)
            foreach (var n in notes.Distinct()) L.Warnings.Add($"Orelhas: {n}.");
    }

    /// <summary>
    /// Face do meio-fio de uma esquina para a orelha: ao longo do ramo A (de longe até o fim da curva), a curva da esquina (o
    /// contorno da pista entre os fins da curva) e ao longo do ramo B até o comprimento pedido.
    /// </summary>
    private static Polyline2? CornerPath(IntersectionLayout L, List<Polygon2> pav, IntersectionLeg la, IntersectionLeg lb, double lenA, double lenB, bool wrap)
    {
        var pts = new List<Vec2>();
        void Add(Vec2 p)
        {
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > 0.01) pts.Add(p);
        }
        if (lenA > 0)
        {
            var n = Math.Max(2, (int)Math.Ceiling(lenA / 0.5));
            for (int k = 0; k <= n; k++)
            {
                var t = la.Clear + lenA * (1 - k / (double)n);
                Add(L.At(la, t, L.HiEdge(la, t)));
            }
        }
        if (wrap)
        {
            var pa = L.At(la, la.Clear, L.HiEdge(la, la.Clear));
            var pb = L.At(lb, lb.Clear, -L.LoEdge(lb, lb.Clear));
            var curve = RingPath(pav, pa, pb);
            if (curve == null) return null;
            foreach (var p in curve) Add(p);
        }
        if (lenB > 0)
        {
            var n = Math.Max(2, (int)Math.Ceiling(lenB / 0.5));
            for (int k = 0; k <= n; k++)
            {
                var t = lb.Clear + lenB * k / (double)n;
                Add(L.At(lb, t, -L.LoEdge(lb, t)));
            }
        }
        return pts.Count >= 2 ? new Polyline2(pts) : null;
    }

    /// <summary>
    /// Esquina da interseção mais próxima do ponto (orelha por clique): ramo dono A, ramo seguinte B e se o ponto está mais
    /// perto do meio-fio de A (o avanço informado vale para a via clicada). Nulo = sem esquina (emenda, dois ramos).
    /// </summary>
    public static (IntersectionLeg A, IntersectionLeg B, bool NearA)? CornerAt(IntersectionLayout L, Vec2 p)
    {
        if (L.IsBend || L.Legs.Count < 3) return null;
        var sorted = L.Legs.Select(l => (Leg: l, A: AngleOf(l.Dir))).OrderBy(x => x.A).ToList();
        var ap = AngleOf(p - L.Node);
        (IntersectionLeg, IntersectionLeg, bool)? best = null;
        var bestD = double.MaxValue;
        for (int k = 0; k < sorted.Count; k++)
        {
            var (la, aa) = sorted[k];
            var (lb, ab) = sorted[(k + 1) % sorted.Count];
            var span = Ccw(aa, ab);
            if (Straight(la, lb, span)) continue;
            var within = Ccw(aa, ap) <= span;
            // Distância aos meios-fios dos dois ramos (fora do setor: penalizada).
            double DistTo(IntersectionLeg leg, int side)
            {
                var axis = L.Roads[leg.Road].Axis;
                var (st, signed) = axis.Project(p);
                var t = Math.Max(leg.Clear, (st - leg.NodeStation) * leg.Sign);
                var face = side > 0 ? L.HiEdge(leg, t) : -L.LoEdge(leg, t);
                return L.At(leg, t, face).DistanceTo(p);
            }
            var da = DistTo(la, 1);
            var db = DistTo(lb, -1);
            var dist = Math.Min(da, db) + (within ? 0 : 1000);
            if (dist < bestD) { bestD = dist; best = (la, lb, da <= db); }
        }
        return best;
    }

    /// <summary>
    /// Orelha numa esquina por clique: avanço <paramref name="along"/> ao longo da via clicada e <paramref name="other"/> ao
    /// longo da outra (0 = só na via clicada), ou até o início do estacionamento. Grava o ajuste da esquina.
    /// </summary>
    public static IntersectionLegSettings SetCornerEar(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg a, bool nearA,
        double along, double other, bool toParking)
    {
        var s = LegSetOrNew(d, L, a);
        s.CurbExtension = true;
        var (la, lb) = nearA ? (along, other) : (other, along);
        s.CurbExtensionLength = la < 0.05 ? 0 : Math.Max(0.5, la);
        s.CurbExtensionLengthOther = lb < 0.05 ? 0 : Math.Max(0.5, lb);
        s.CurbExtensionToParking = toParking;
        return s;
    }

    /// <summary>Trecho mais curto do contorno da pista entre dois pontos dele (a curva da esquina).</summary>
    private static List<Vec2>? RingPath(List<Polygon2> pav, Vec2 a, Vec2 b)
    {
        List<Vec2>? best = null;
        var bestLen = double.MaxValue;
        foreach (var poly in pav)
            foreach (var ring in new[] { poly.Outer }.Concat(poly.Holes))
            {
                if (ring.Count < 3) continue;
                var loop = new Polyline2(ring.Append(ring[0]));
                var (sa, da) = loop.Project(a);
                var (sb, db) = loop.Project(b);
                if (Math.Abs(da) > 0.5 || Math.Abs(db) > 0.5) continue;
                var total = loop.Length;
                var fwd = ((sb - sa) % total + total) % total;
                var (s0, len, rev) = fwd <= total - fwd ? (sa, fwd, false) : (sb, total - fwd, true);
                if (len >= bestLen) continue;
                var pts = s0 + len <= total + 1e-9
                    ? loop.SubPoints(s0, Math.Min(total, s0 + len))
                    : loop.SubPoints(s0, total).Concat(loop.SubPoints(0, s0 + len - total).Skip(1)).ToList();
                if (rev) pts.Reverse();
                best = pts;
                bestLen = len;
            }
        return best;
    }
}
