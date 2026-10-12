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
    Polyline2 Path, Polygon2 Footprint, CurbExtensionDefinition Template)
{
    /// <summary>Largura da sarjeta que contorna a frente da orelha (a orelha avança Depth − Gutter; a sarjeta ocupa o resto).</summary>
    public double Gutter { get; init; }
    /// <summary>O que a orelha substitui nas marcas vizinhas: o contorno e a faixa do meio-fio antigo atrás da face.</summary>
    public List<Polygon2> CutZone { get; init; } = new();
    /// <summary>Eixo da sarjeta nova, na frente da face da orelha (nulo sem sarjeta).</summary>
    public Polyline2? GutterPath { get; init; }
    /// <summary>Extensão no lado contínuo (não esquina) da travessia do ramo A: só ao longo dele.</summary>
    public bool Straight { get; init; }
}

/// <summary>Trecho de um lado de ramo coberto por orelha: avanço e estacas (t) onde a orelha tem a largura toda.</summary>
public sealed record EarSide(double Depth, double TFullStart, double TFullEnd, double TStart, double TEnd)
{
    /// <summary>Rampa da travessia na borda da extensão (falso = sem rampa deste lado).</summary>
    public bool Ramp { get; init; } = true;
    /// <summary>Piso tátil de alerta na rampa (nulo = o geral da interseção).</summary>
    public bool? Tactile { get; init; }
}

public static partial class IntersectionGenerator
{
    /// <summary>
    /// Largura da faixa de estacionamento junto ao meio-fio de um lado da via (com a sarjeta somada), lida da seção gravada
    /// na via. Nulo = aquele lado não tem estacionamento junto ao meio-fio (a orelha invadiria uma faixa de rolamento).
    /// </summary>
    public static double? ParkingDepth(RoadPavementDefinition r, bool left) => Parking(r, left)?.Depth;

    /// <summary>Largura da sarjeta junto ao meio-fio de um lado da via com estacionamento (0 = sem sarjeta).</summary>
    public static double ParkingGutter(RoadPavementDefinition r, bool left) => Parking(r, left)?.Gutter ?? 0;

    private static (double Depth, double Gutter)? Parking(RoadPavementDefinition r, bool left)
    {
        var setup = RoadTemplates.FromJson(r.SetupJson);
        if (setup == null) return null;
        var side = left ? setup.Left : setup.Right;
        var k = side.FindIndex(e => e.Tipo == TipoElementoSecao.Calcada);
        var last = k < 0 ? side.Count - 1 : k - 1;
        if (last < 0 || side[last].Tipo != TipoElementoSecao.Estacionamento) return null;
        var gutter = k >= 0 && setup.SarjetaSomada == true && setup.PhysicalElements && !side[last].Elevado ? Math.Max(0, side[k].Sarjeta) : 0;
        return (side[last].Largura + gutter, gutter);
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

    /// <summary>
    /// Área que a orelha tira de <paramref name="m"/>: o pavimento, a sarjeta e o meio-fio saem também da faixa atrás da face
    /// original (vira piso da orelha) e da nova sarjeta em frente; a pintura (vagas, linhas) sai só de baixo da orelha.
    /// </summary>
    public static List<Polygon2> EarCut(EarPlan e, MarkingDefinition m) =>
        e.CutZone.Count > 0 && (m is RoadPavementDefinition || m is LinearMarkingDefinition l && (l.Code.StartsWith("SARJETA") || l.Code.StartsWith("MEIO-FIO")))
            ? e.CutZone
            : new List<Polygon2> { e.Footprint };

    /// <summary>
    /// Marcas da via recortadas pelas orelhas: pavimento, sarjeta, meio-fio (o antigo, atrás da face – a orelha tem o seu na
    /// frente) e toda a pintura (vagas, linhas, símbolos).
    /// </summary>
    public static bool CutByEars(MarkingDefinition m) => m switch
    {
        RoadPavementDefinition => true,
        LinearMarkingDefinition l => l.Code.StartsWith("SARJETA") || l.Code.StartsWith("MEIO-FIO") || !IsPhysical(l),
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
    /// Largura junto ao meio-fio de um lado da via que não é faixa de rolamento – estacionamento, acostamento, faixa de
    /// segurança e canteiro pintado encostados no meio-fio, com a sarjeta somada – e a sarjeta. Nulo = via sem a seção gravada
    /// (a largura não pode ser conferida).
    /// </summary>
    public static (double Width, double Gutter)? CurbRoom(RoadPavementDefinition r, bool left)
    {
        var setup = RoadTemplates.FromJson(r.SetupJson);
        if (setup == null) return null;
        var side = left ? setup.Left : setup.Right;
        var k = side.FindIndex(e => e.Tipo == TipoElementoSecao.Calcada);
        var last = k < 0 ? side.Count - 1 : k - 1;
        var w = 0.0;
        for (var i = last; i >= 0 && side[i].Tipo is TipoElementoSecao.Estacionamento or TipoElementoSecao.Acostamento
                 or TipoElementoSecao.FaixaSeguranca or TipoElementoSecao.CanteiroPintado; i--)
            w += side[i].Largura;
        var gutter = k >= 0 && setup.SarjetaSomada == true && setup.PhysicalElements && !(last >= 0 && side[last].Elevado) ? Math.Max(0, side[k].Sarjeta) : 0;
        return (w + gutter, gutter);
    }

    /// <summary>Transição automática das pontas: comprimento ao longo do meio-fio = este fator × a largura do avanço [a confirmar].</summary>
    public const double AutoTaperFactor = 2.0;

    /// <summary>Folga padrão da extensão além da faixa de pedestres e das rampas (m).</summary>
    public const double DefaultCrosswalkMargin = 0.30;

    /// <summary>
    /// Raio das curvas reversas (ou comprimento do chanfro) da ponta: pelo comprimento de transição informado, pelo raio
    /// informado na esquina (ajuste antigo) ou automático – transição de <see cref="AutoTaperFactor"/> × a largura (curva
    /// reversa tangente ao meio-fio e à face, ~53° no meio).
    /// </summary>
    public static double EndParam(TipoTransicao kind, double? taper, double? radius, double face)
    {
        var D = Math.Max(0.1, face);
        if (kind == TipoTransicao.Reta) return Math.Max(0.1, radius ?? 1.0);
        if (taper is not > 0.05 && radius is > 0.05) return radius.Value;
        var lt = taper is > 0.05 ? taper.Value : AutoTaperFactor * D;
        return kind == TipoTransicao.Chanfro ? lt : (lt * lt + D * D) / (4 * D);
    }

    /// <summary>Lado de uma esquina com extensão: largura (até a face), sarjeta em frente, pontas e o comprimento pedido.</summary>
    private sealed record EarSidePlan(bool On, double Depth, double Gutter, TipoTransicao Kind, double Param, double? Requested)
    {
        public double Face => Depth - Gutter;
        public double Lt => On ? EarTransition(Kind, Param, Face) : 0;
    }

    /// <summary>
    /// Planeja as orelhas das esquinas: lados com estacionamento (ou avanço informado, até a largura junto ao meio-fio sem faixa
    /// de rolamento), avanço de cada lado, comprimento (pedido, até o início do estacionamento, e sempre cobrindo a travessia e as
    /// rampas daquele lado, que passam para a borda da orelha, e indo além do prolongamento do alinhamento predial) e a face:
    /// as faces deslocadas de cada lado ligadas pela curva da esquina com o raio dela (veículo de projeto), tangente às duas, e a
    /// transição suave de volta ao meio-fio nas pontas. <paramref name="provisional"/>: comprimentos com folga (antes das rampas finais).
    /// </summary>
    private static void PlanEars(IntersectionDefinition d, IntersectionLayout L, List<Polygon2> pav, bool provisional)
    {
        L.Ears.Clear();
        L.EarSides.Clear();
        // Cada passada parte dos fins de curva sem as extensões (que deslocam a curva da esquina).
        L.ClearCap.Clear();
        if (L.BaseSideTangentT.Count == 0)
            foreach (var kv in L.SideTangentT) L.BaseSideTangentT[kv.Key] = kv.Value;
        else
        {
            foreach (var kv in L.BaseSideTangentT) L.SideTangentT[kv.Key] = kv.Value;
            SyncTangents(L);
        }
        var notes = new List<string>();
        // Duas passadas: a primeira desloca as curvas de todas as esquinas (a travessia de cada ramo depende das duas esquinas
        // dele); a segunda planeja os comprimentos com as travessias já no lugar definitivo.
        for (var pass = 0; pass < 2; pass++)
        {
        L.Ears.Clear();
        L.EarSides.Clear();
        notes.Clear();
        foreach (var (la, lb) in EarCorners(d, L).ToList())
        {
            if (d.RightTurnIslands != TipoIlha.Nenhuma)
            {
                notes.Add("esquinas canalizadas (faixa de conversão livre) não recebem extensão de calçada");
                break;
            }
            var ls = LegSet(d, L, la);
            var ra = L.Roads[la.Road].Def;
            var rb = L.Roads[lb.Road].Def;
            // Lado alto do ramo A e lado baixo do ramo B (a esquerda da via para Sign > 0 é o lado alto).
            var leftA = la.Sign > 0;
            var leftB = lb.Sign < 0;
            static double? Req(double? v) => v is > 0.1 ? v : null;
            var reqA = Req(ls?.CurbExtensionDepth) ?? Req(d.CurbExtensionDepth);
            var reqB = Req(ls?.CurbExtensionDepthOther) ?? reqA;
            var parkA = ParkingDepth(ra, leftA);
            var parkB = ParkingDepth(rb, leftB);
            var onA = parkA != null || reqA != null && (leftA ? ra.LeftSidewalk : ra.RightSidewalk) > 0.5;
            var onB = parkB != null || reqB != null && (leftB ? rb.LeftSidewalk : rb.RightSidewalk) > 0.5;
            // Avanço 0 informado num lado: a orelha fica só ao longo da outra via.
            if (ls?.CurbExtensionLength is < 0.05) onA = false;
            if (ls?.CurbExtensionLengthOther is < 0.05) onB = false;
            if (!onA && !onB)
            {
                notes.Add("esquina sem faixa de estacionamento junto ao meio-fio não recebe extensão de calçada (informe a largura do avanço para forçar)");
                continue;
            }
            var endsA = ls?.CurbExtensionEnds ?? d.CurbExtensionEnds;
            var endsB = ls?.CurbExtensionEndsOther ?? endsA;
            var taperA = ls?.CurbExtensionTaper ?? d.CurbExtensionTaper;
            var taperB = ls?.CurbExtensionTaperOther ?? ls?.CurbExtensionTaper ?? d.CurbExtensionTaper;
            // Largura de cada lado: a pedida ou a do estacionamento, nunca além da faixa junto ao meio-fio sem tráfego.
            EarSidePlan Side(bool on, double? req, double? park, RoadPavementDefinition r, bool left, TipoTransicao kind, double? taper, double? requested)
            {
                if (!on) return new EarSidePlan(false, 0, 0, kind, 0, null);
                var room = CurbRoom(r, left);
                var depth = req ?? park!.Value;
                var gutter = park != null ? ParkingGutter(r, left) : room?.Gutter ?? 0;
                if (room is { } rm)
                {
                    if (rm.Width - rm.Gutter < 0.3)
                    {
                        notes.Add("lado da esquina sem estacionamento ou acostamento junto ao meio-fio: a extensão de calçada invadiria a faixa de rolamento e não foi feita");
                        return new EarSidePlan(false, 0, 0, kind, 0, null);
                    }
                    if (depth > rm.Width + 0.005)
                    {
                        notes.Add($"largura da extensão de calçada reduzida de {depth:0.00} m para {rm.Width:0.00} m para não invadir a faixa de rolamento");
                        depth = rm.Width;
                    }
                    gutter = rm.Gutter;
                }
                else if (req != null && park == null)
                    notes.Add("via sem a seção gravada: a largura informada da extensão de calçada não pôde ser conferida com as faixas de rolamento");
                depth = Math.Clamp(depth, 0.3, 10);
                if (depth - gutter < 0.6) gutter = 0;
                var face = depth - gutter;
                return new EarSidePlan(true, depth, gutter, kind, EndParam(kind, taper, ls?.CurbExtensionEndRadius, face), requested);
            }
            var A = Side(onA, reqA, parkA, ra, leftA, endsA, taperA, ls?.CurbExtensionLength);
            var B = Side(onB, reqB, parkB, rb, leftB, endsB, taperB, ls?.CurbExtensionLengthOther ?? (ls?.CurbExtensionLength is > 0.05 ? ls.CurbExtensionLength : null));
            if (!A.On && !B.On) continue;
            // Sarjeta da via: continua na frente da orelha (a face do meio-fio da orelha fica a Depth − sarjeta da original).
            var gutter = Math.Max(A.On ? A.Gutter : 0, B.On ? B.Gutter : 0);
            var toParking = ls?.CurbExtensionToParking ?? d.CurbExtensionToParking;
            var margin = Math.Max(0, ls?.CurbExtensionCrosswalkMargin ?? d.CurbExtensionCrosswalkMargin ?? DefaultCrosswalkMargin);
            // A curva da esquina passa a ligar as faces deslocadas (a de cada lado com extensão; o meio-fio da outra via no lado
            // sem ela), com o raio da esquina, tangente às duas: a extensão nasce da curva, sem ponta nem degrau, e as travessias
            // acompanham o novo fim da curva.
            var cf0 = L.CornerFillets.TryGetValue((la.Road, la.Sign), out var cfx) ? cfx : null;
            var rx = ls?.CurbExtensionCornerRadius ?? d.CurbExtensionCornerRadius;
            var shifted = ShiftCorner(L, pav, la, lb, A.On ? A.Face : 0, B.On ? B.Face : 0, rx is > 0.05 ? rx.Value : cf0?.R ?? 0, notes);
            // Estação até onde a largura toda precisa ir para a travessia e as rampas deste lado ficarem sobre a extensão.
            double? Cover(IntersectionLeg leg, int side)
            {
                if (!CrosswalkOn(d, L, leg)) return null;
                var plan = RampPlanOf(d, L, leg);
                var rp = side > 0 ? plan.Hi : plan.Lo;
                return CrosswalkT(d, L, leg) + Math.Max(CrosswalkWidthOf(d, L, leg) / 2, rp == null ? 0 : RampHalfExtent(rp)) + margin;
            }
            double Length(IntersectionLeg leg, EarSidePlan sp, int side)
            {
                var requested = sp.Requested;
                var len = toParking ? StopFar(d, L, leg) + 5.0 : Math.Max(0.5, requested is > 0.05 ? requested.Value : d.CurbExtensionLength);
                // Comprimento fixo (informado ou até o estacionamento): a travessia é coberta com uma transição curta (a ponta
                // encurta até caber, ver Fit); senão, com a transição pedida.
                var fixedLen = requested is > 0.05 || toParking;
                var lt = fixedLen && sp.Kind != TipoTransicao.Reta ? Math.Min(sp.Lt, sp.Face) : sp.Lt;
                // A travessia deste lado fica sobre a orelha: a orelha vai além da faixa e das rampas (com a ponta).
                if (CrosswalkOn(d, L, leg))
                {
                    var plan = RampPlanOf(d, L, leg);
                    var rp = side > 0 ? plan.Hi : plan.Lo;
                    var ext = Math.Max(CrosswalkWidthOf(d, L, leg) / 2, rp == null ? 0 : RampHalfExtent(rp));
                    var need = CrosswalkT(d, L, leg) - leg.Clear + ext + lt + margin + (provisional ? 2.5 : 0);
                    if (len < need - 1e-6)
                    {
                        if (!provisional) notes.Add($"extensão de calçada estendida para {need:0.00} m ao longo da via para a travessia e as rampas ficarem sobre ela");
                        len = need;
                    }
                }
                // A largura toda vai pelo menos 1,5 m além do prolongamento do alinhamento predial da outra via [a confirmar].
                if (PropertyLineT(L, la, lb, side > 0) is { } prop)
                {
                    var need = prop + MinBeyondPropertyLine - leg.Clear + lt;
                    if (len < need - 1e-6)
                    {
                        if (requested is > 0.05 || toParking) { if (!provisional) notes.Add("extensão de calçada mais curta que 1,5 m além do alinhamento predial da esquina (comprimento informado mantido)"); }
                        else len = need;
                    }
                }
                // Com a curva deslocada, a face reta começa no novo fim da curva (comprimento fixo: a transição encurta).
                if (shifted != null && !(requested is > 0.05 || toParking)) len = Math.Max(len, (side > 0 ? shifted.TA : shifted.TB) - leg.Clear + lt + 0.5);
                return Math.Min(len, Math.Max(1.0, MaxT(L, leg) - leg.Clear - 2));
            }
            var lenA = A.On ? Length(la, A, 1) : 0;
            var lenB = B.On ? Length(lb, B, -1) : 0;
            // Comprimento fixo (informado ou até o estacionamento) curto para a transição: ela encurta até caber.
            EarSidePlan Fit(EarSidePlan sp, IntersectionLeg leg, int side, double len, double tFull)
            {
                if (!sp.On || shifted == null || sp.Kind == TipoTransicao.Reta) return sp;
                var room = leg.Clear + len - Math.Max(tFull + 0.3, Cover(leg, side) ?? double.MinValue);
                if (sp.Lt <= room + 1e-6) return sp;
                var lt = Math.Max(0.3, room);
                if (!provisional) notes.Add($"transição da ponta da extensão de calçada encurtada para {lt:0.00} m para caber no comprimento informado");
                return sp with { Param = EndParam(sp.Kind, lt, null, sp.Face) };
            }
            if (shifted != null)
            {
                A = Fit(A, la, 1, lenA, shifted.TA);
                B = Fit(B, lb, -1, lenB, shifted.TB);
            }
            Polyline2? path;
            CurbExtensionDefinition tpl;
            if (shifted != null)
            {
                path = ShiftedPath(L, pav, la, lb, shifted, lenA, lenB);
                tpl = new CurbExtensionDefinition
                {
                    Depth = Math.Max(A.On ? A.Face : 0, B.On ? B.Face : 0), Transition = A.On ? A.Kind : B.Kind, EndTransition = B.On ? B.Kind : A.Kind,
                    Radius = A.On ? A.Param : B.Param, EndRadius = B.On ? B.Param : A.Param, SidewalkOnLeft = false,
                    Height = L.CurbHeight, CurbWidth = L.CurbWidth, CutRoadMarkings = true,
                };
                if (path != null) tpl.OuterFace = ShiftedFace(L, la, lb, shifted, A, la.Clear + lenA, B, lb.Clear + lenB);
            }
            else
            {
                // Esquina fora do modelo dos bordos retos: a face acompanha o meio-fio (só nos dois lados), com a menor largura.
                if (A.On != B.On)
                {
                    notes.Add("esquina sem curva de bordos retos: a extensão de calçada de um lado só fica ao longo da via, sem contornar a esquina");
                }
                var face = new[] { A.On ? A.Face : double.MaxValue, B.On ? B.Face : double.MaxValue }.Min();
                var sp = A.On ? A : B;
                path = CornerPath(L, pav, la, lb, lenA, lenB, A.On && B.On);
                tpl = new CurbExtensionDefinition
                {
                    Depth = face, Transition = sp.Kind, EndTransition = sp.Kind,
                    Radius = EndParam(sp.Kind, ls?.CurbExtensionTaper ?? d.CurbExtensionTaper, ls?.CurbExtensionEndRadius, face),
                    SidewalkOnLeft = false, Height = L.CurbHeight, CurbWidth = L.CurbWidth, CutRoadMarkings = true,
                };
                tpl.EndRadius = tpl.Radius;
            }
            if (path == null || path.Length < 1)
            {
                notes.Add("não foi possível seguir o meio-fio de uma esquina para a extensão de calçada");
                continue;
            }
            // Centros exatos das curvas que a extensão segue: a da esquina e a curva nova.
            if (cf0 is { Span: < 179, R: > 0.01 } cfe)
            {
                tpl.ArcCenters = new List<Vec2> { cfe.V + (cfe.Ua + cfe.Ub).Normalized() * (cfe.R / Math.Sin(cfe.Span * Math.PI / 360)) };
                if (shifted != null) tpl.ArcCenters.Add(shifted.Center);
            }
            // Trechos de cada lado com a largura toda.
            var ramp = ls?.CurbExtensionRamp ?? true;
            var tactile = ls?.CurbExtensionTactile;
            var sides = new List<((int, int, int) Key, EarSide Side)>();
            if (A.On)
                sides.Add(((la.Road, la.Sign, 1), (shifted != null
                    ? new EarSide(A.Face, shifted.TA, la.Clear + lenA - A.Lt, shifted.TA, la.Clear + lenA)
                    : B.On ? new EarSide(tpl.Depth, 0, la.Clear + lenA - A.Lt, 0, la.Clear + lenA)
                           : new EarSide(tpl.Depth, la.Clear + A.Lt, la.Clear + lenA - A.Lt, la.Clear, la.Clear + lenA)) with { Ramp = ramp, Tactile = tactile }));
            if (B.On)
                sides.Add(((lb.Road, lb.Sign, -1), (shifted != null
                    ? new EarSide(B.Face, shifted.TB, lb.Clear + lenB - B.Lt, shifted.TB, lb.Clear + lenB)
                    : A.On ? new EarSide(tpl.Depth, 0, lb.Clear + lenB - B.Lt, 0, lb.Clear + lenB)
                           : new EarSide(tpl.Depth, lb.Clear + B.Lt, lb.Clear + lenB - B.Lt, lb.Clear, lb.Clear + lenB)) with { Ramp = ramp, Tactile = tactile }));
            // Contorno livre desta esquina: substitui a face padrão quando é válido.
            if (ls?.CurbExtensionFace is { Count: >= 2 } free)
            {
                var ff = FreeFace(L, pav, la, lb, free, ls.CurbExtensionFaceRadius ?? 1.0);
                if (ff.Error != null)
                {
                    if (!provisional) notes.Add($"contorno livre da esquina {LegLabel(L, la)} recusado – {ff.Error}; ficou a forma padrão");
                }
                else
                {
                    // Sem curva deslocada: os fins de curva e as travessias voltam ao lugar de antes da extensão.
                    if (shifted != null)
                    {
                        foreach (var kv in L.BaseSideTangentT.Where(kv => kv.Key == (la.Road, la.Sign, 1) || kv.Key == (lb.Road, lb.Sign, -1)))
                            L.SideTangentT[kv.Key] = kv.Value;
                        SyncTangents(L);
                        L.ClearCap.Remove((la.Road, la.Sign));
                        L.ClearCap.Remove((lb.Road, lb.Sign));
                    }
                    path = ff.Path!;
                    tpl.OuterFace = ff.Face;
                    if (tpl.ArcCenters is { Count: > 1 }) tpl.ArcCenters.RemoveAt(1);
                    sides = FreeSides(L, ff.Face!, la, lb).Select(x => (x.Key, x.Side with { Ramp = ramp, Tactile = tactile })).ToList();
                }
            }
            var fp = SidewalkGenerator.EarFootprint(tpl, path);
            if (fp == null) continue;
            Polyline2? gutterPath = null;
            if (gutter > 0.01)
            {
                var edge = new Polyline2(CleanFacePath(SidewalkGenerator.EarOuterEdge(tpl, path)));
                if (edge.Length > 1) gutterPath = edge.Offset(gutter / 2);
            }
            // A sarjeta da via sai de onde passa a sarjeta nova (nas pontas da orelha as duas se encontram sem sobrepor).
            var cut = SidewalkGenerator.EarCutZone(tpl, path);
            if (gutterPath != null) cut = PolygonOps.Union(cut.Concat(PolygonOps.Strip(gutterPath.Points, gutter, roundJoins: true)));
            L.Ears.Add(new EarPlan(la, lb, A.On, B.On, lenA, lenB, Math.Max(A.Depth, B.Depth), path, fp, tpl)
            {
                Gutter = gutter, GutterPath = gutterPath, CutZone = cut,
            });
            foreach (var (key, es) in sides) L.EarSides[key] = es;
        }
        }
        PlanStraightEars(d, L, notes, provisional);
        if (!provisional)
            foreach (var n in notes.Distinct()) L.Warnings.Add($"Extensões de calçada: {n}.");
    }

    /// <summary>Distância mínima (m) da largura toda da extensão além do prolongamento do alinhamento predial (5 pés – NACTO) [a confirmar].</summary>
    public const double MinBeyondPropertyLine = 1.5;

    /// <summary>
    /// Estação, no ramo A (<paramref name="onA"/>) ou no B, onde o prolongamento do alinhamento predial da outra via da esquina
    /// (o fundo da calçada dela) cruza o meio-fio. Nulo = esquina sem curva de bordos retos.
    /// </summary>
    public static double? PropertyLineT(IntersectionLayout L, IntersectionLeg la, IntersectionLeg lb, bool onA)
    {
        if (!L.CornerFillets.TryGetValue((la.Road, la.Sign), out var cf) || cf.Span >= 179 || cf.Span < 1) return null;
        var (leg, u, other) = onA ? (la, cf.Ua, lb) : (lb, cf.Ub, la);
        var r = L.Roads[other.Road].Def;
        // Calçada da outra via voltada para a esquina: lado baixo do ramo B ou lado alto do ramo A.
        var walk = onA ? (lb.Sign > 0 ? r.RightSidewalk : r.LeftSidewalk) : (la.Sign > 0 ? r.LeftSidewalk : r.RightSidewalk);
        if (walk <= 0.01) return null;
        var p = cf.V + u * ((walk + L.CurbWidth) / Math.Sin(cf.Span * Math.PI / 180));
        return (p - L.Node).Dot(leg.Dir);
    }

    /// <summary>Curva da esquina deslocada pelas extensões: face nova (arco), os novos fins da curva e os de antes, centro e raio.</summary>
    private sealed record ShiftedCorner(List<Vec2> Arc, double TA, double TB, double TA0, double TB0, Vec2 Center, double R);

    /// <summary><see cref="IntersectionLayout.TangentT"/> = o maior dos dois lados de cada ramo.</summary>
    private static void SyncTangents(IntersectionLayout L)
    {
        foreach (var g in L.SideTangentT.GroupBy(x => (x.Key.Road, x.Key.Sign)))
            L.TangentT[g.Key] = g.Max(x => x.Value);
    }

    /// <summary>
    /// Extensões da esquina: o bordo do ramo A avança <paramref name="fA"/> sobre a pista e o do ramo B, <paramref name="fB"/>
    /// (0 = sem extensão daquele lado) e a curva da esquina, com o raio <paramref name="r"/> (o da esquina – veículo de projeto
    /// – por padrão), passa a ser tangente aos dois bordos deslocados. Atualiza os fins de curva dos dois ramos (as travessias
    /// ficam mais perto do nó). Raio maior que o que cabe é reduzido (a curva nova não pode entrar na calçada nem passar do fim
    /// da curva antiga no lado sem extensão). Nulo quando a esquina não segue o modelo dos bordos retos.
    /// </summary>
    private static ShiftedCorner? ShiftCorner(IntersectionLayout L, List<Polygon2> pav, IntersectionLeg la, IntersectionLeg lb, double fA, double fB, double r,
        List<string> notes)
    {
        if (!L.CornerFillets.TryGetValue((la.Road, la.Sign), out var cf) || cf.Span >= 179 || cf.R <= 0.01 || r <= 0.01) return null;
        var (ua, ub, V) = (cf.Ua, cf.Ub, cf.V);
        var half = cf.Span * Math.PI / 360;
        var T0 = cf.R / Math.Tan(half);
        var node = L.Node;
        // Só quando o fim da curva calculado pelo vértice vale (sem correção pela medida).
        var keyA = (la.Road, la.Sign, 1);
        var keyB = (lb.Road, lb.Sign, -1);
        if (!L.BaseSideTangentT.TryGetValue(keyA, out var tA0) || !L.BaseSideTangentT.TryGetValue(keyB, out var tB0)) return null;
        if (Math.Abs(tA0 - (V + ua * T0 - node).Dot(la.Dir)) > 0.05 || Math.Abs(tB0 - (V + ub * T0 - node).Dot(lb.Dir)) > 0.05) return null;
        // Normal de cada bordo para o lado da pista (fora da cunha da esquina); vértice novo a fA do bordo A e fB do B.
        var nA = -(ub - ua * ub.Dot(ua)).Normalized();
        var nB = -(ua - ub * ua.Dot(ub)).Normalized();
        var V2 = V + ua * (fB / ua.Dot(nB)) + ub * (fA / ub.Dot(nA));
        var bis = (ua + ub).Normalized();
        // Raio que cabe: no lado sem extensão a curva nova não passa do fim da curva antiga; e o arco fica na pista.
        double TOf(double rr) => rr / Math.Tan(half);
        var rMax = double.MaxValue;
        if (fA < 1e-6) rMax = Math.Min(rMax, ((V + ua * T0 - node).Dot(la.Dir) - (V2 - node).Dot(la.Dir)) * Math.Tan(half) / ua.Dot(la.Dir));
        if (fB < 1e-6) rMax = Math.Min(rMax, ((V + ub * T0 - node).Dot(lb.Dir) - (V2 - node).Dot(lb.Dir)) * Math.Tan(half) / ub.Dot(lb.Dir));
        // Arco novo fora da calçada: o ponto do meio dele não passa do ponto do meio do arco antigo (ao longo da bissetriz).
        var mid0 = (cf.R / Math.Sin(half) - cf.R);
        var shift = (V2 - V).Dot(bis);
        rMax = Math.Min(rMax, (mid0 - shift) / (1 / Math.Sin(half) - 1));
        if (r > rMax + 1e-6)
        {
            if (rMax < 0.3) return null;
            notes.Add($"raio da curva da extensão de calçada reduzido de {r:0.00} m para {rMax:0.00} m (a curva nova não pode entrar na calçada)");
            r = rMax;
        }
        var T = TOf(r);
        var ta = V2 + ua * T;
        var tb = V2 + ub * T;
        var c = V2 + bis * (r / Math.Sin(half));
        // Arco novo do fim da curva no ramo A até o do ramo B (passo ≤ 1°).
        var pts = new List<Vec2>();
        var a0 = Math.Atan2(ta.Y - c.Y, ta.X - c.X);
        var a1 = Math.Atan2(tb.Y - c.Y, tb.X - c.X);
        var da = a1 - a0;
        while (da > Math.PI) da -= 2 * Math.PI;
        while (da < -Math.PI) da += 2 * Math.PI;
        var n = Math.Max(4, (int)Math.Ceiling(Math.Abs(da) / (Math.PI / 180)));
        for (int i = 0; i <= n; i++) pts.Add(c + Vec2.FromAngle(a0 + da * i / n) * r);
        var tA = (ta - node).Dot(la.Dir);
        var tB = (tb - node).Dot(lb.Dir);
        L.SideTangentT[keyA] = tA;
        L.SideTangentT[keyB] = tB;
        SyncTangents(L);
        // Ramo com esquina medida dos dois lados: a travessia pode começar no novo fim da curva, mesmo antes do fim do ramo
        // calculado pela pista antiga (a curva antiga virou extensão).
        foreach (var leg in new[] { la, lb })
            if (L.SideTangentT.ContainsKey((leg.Road, leg.Sign, 1)) && L.SideTangentT.ContainsKey((leg.Road, leg.Sign, -1)))
                L.ClearCap[(leg.Road, leg.Sign)] = L.TangentT[(leg.Road, leg.Sign)];
        return new ShiftedCorner(pts, tA, tB, tA0, tB0, c, r);
    }

    /// <summary>
    /// Face antiga do meio-fio das extensões da esquina: do fim da extensão no ramo A (ou, sem extensão ali, 0,5 m além do fim
    /// da curva) até o fim da curva antigo, a curva antiga inteira e o ramo B até o fim da extensão dele (ou 0,5 m além).
    /// </summary>
    private static Polyline2? ShiftedPath(IntersectionLayout L, List<Polygon2> pav, IntersectionLeg la, IntersectionLeg lb, ShiftedCorner sc, double lenA, double lenB)
    {
        var (tA, tB) = (sc.TA0, sc.TB0);
        var pts = new List<Vec2>();
        void Add(Vec2 p)
        {
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > 0.01) pts.Add(p);
        }
        var a1 = lenA > 0 ? la.Clear + lenA : Math.Max(tA, sc.TA) + 0.5;
        var nA = Math.Max(2, (int)Math.Ceiling((a1 - tA) / 0.5));
        for (int k = 0; k <= nA; k++)
        {
            var t = a1 - (a1 - tA) * k / (double)nA;
            Add(L.At(la, t, L.HiEdge(la, t)));
        }
        var curve = RingPath(pav, L.At(la, tA, L.HiEdge(la, tA)), L.At(lb, tB, -L.LoEdge(lb, tB)));
        if (curve == null) return null;
        foreach (var p in curve) Add(p);
        var b1 = lenB > 0 ? lb.Clear + lenB : Math.Max(tB, sc.TB) + 0.5;
        var nB = Math.Max(2, (int)Math.Ceiling((b1 - tB) / 0.5));
        for (int k = 0; k <= nB; k++)
        {
            var t = tB + (b1 - tB) * k / (double)nB;
            Add(L.At(lb, t, -L.LoEdge(lb, t)));
        }
        var clean = CleanFacePath(pts);
        return clean.Count >= 2 ? new Polyline2(clean) : null;
    }

    /// <summary>
    /// Face nova das extensões da esquina, no sentido do caminho (<see cref="ShiftedPath"/>): no ramo A, a transição da ponta
    /// (curva reversa tangente ao meio-fio antigo e à face) e a face até o novo fim da curva – ou, sem extensão ali, o meio-fio
    /// do ramo –; o arco novo; e no ramo B o mesmo, do fim da curva para fora.
    /// </summary>
    private static List<Vec2> ShiftedFace(IntersectionLayout L, IntersectionLeg la, IntersectionLeg lb, ShiftedCorner sc,
        EarSidePlan A, double farA, EarSidePlan B, double farB)
    {
        // Transição da ponta: perfil (x a partir da ponta, avanço) da transição pedida.
        static List<Vec2> Head(EarSidePlan sp)
        {
            var std = new CurbExtensionDefinition { Depth = sp.Face, Transition = sp.Kind, EndTransition = TipoTransicao.Reta, Radius = sp.Param };
            return SidewalkGenerator.EarProfile(std, Math.Max(2 * sp.Lt + 2, 4)).TakeWhile(v => v.Y < sp.Face - 1e-6).ToList();
        }
        var pts = new List<Vec2>();
        void Add(Vec2 p)
        {
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > 0.005) pts.Add(p);
        }
        if (A.On)
        {
            foreach (var v in Head(A)) Add(L.At(la, farA - v.X, L.HiEdge(la, farA - v.X) - v.Y));
            var t0 = farA - A.Lt;
            var n = Math.Max(1, (int)Math.Ceiling((t0 - sc.TA) / 0.5));
            for (int k = 0; k <= n; k++) { var t = t0 - (t0 - sc.TA) * k / (double)n; Add(L.At(la, t, L.HiEdge(la, t) - A.Face)); }
        }
        else
        {
            var a1 = Math.Max(sc.TA0, sc.TA) + 0.5;
            var m = Math.Max(1, (int)Math.Ceiling((a1 - sc.TA) / 0.5));
            for (int k = 0; k <= m; k++) { var t = a1 - (a1 - sc.TA) * k / (double)m; Add(L.At(la, t, L.HiEdge(la, t))); }
        }
        foreach (var p in sc.Arc) Add(p);
        if (B.On)
        {
            var t1 = farB - B.Lt;
            var n = Math.Max(1, (int)Math.Ceiling((t1 - sc.TB) / 0.5));
            for (int k = 0; k <= n; k++) { var t = sc.TB + (t1 - sc.TB) * k / (double)n; Add(L.At(lb, t, -(L.LoEdge(lb, t) - B.Face))); }
            foreach (var v in Enumerable.Reverse(Head(B))) Add(L.At(lb, farB - v.X, -(L.LoEdge(lb, farB - v.X) - v.Y)));
            Add(L.At(lb, farB, -L.LoEdge(lb, farB)));
        }
        else
        {
            var b1 = Math.Max(sc.TB0, sc.TB) + 0.5;
            var m = Math.Max(1, (int)Math.Ceiling((b1 - sc.TB) / 0.5));
            for (int k = 0; k <= m; k++) { var t = sc.TB + (b1 - sc.TB) * k / (double)m; Add(L.At(lb, t, -L.LoEdge(lb, t))); }
        }
        return pts;
    }

    /// <summary>
    /// Faixas de rolamento das vias do arranjo: a pista menos a faixa junto a cada meio-fio sem tráfego (estacionamento,
    /// acostamento, faixa de segurança, canteiro pintado, sarjeta somada), ao longo do eixo inteiro (inclusive no cruzamento).
    /// Vias sem a seção gravada ficam de fora.
    /// </summary>
    public static List<Polygon2> TravelBands(IntersectionLayout L)
    {
        var res = new List<Polygon2>();
        foreach (var r in L.Roads)
        {
            if (CurbRoom(r.Def, true) is not { } left || CurbRoom(r.Def, false) is not { } right) continue;
            res.AddRange(RoadGenerator.Band(r.Axis, -(r.Def.RightWidth - right.Width), r.Def.LeftWidth - left.Width));
        }
        return PolygonOps.Union(res);
    }

    /// <summary>
    /// Contorno livre da face da extensão de uma esquina: pontos de controle em coordenadas do ramo A (x ao longo dele a partir
    /// do nó, y à esquerda), da ponta num meio-fio à ponta no outro, com os vértices arredondados pelo raio. Devolve o caminho no
    /// meio-fio antigo entre as pontas, a face e, se o contorno não serve, o motivo (pontas fora do meio-fio, contorno cruzado,
    /// fora da pista ou sobre faixa de rolamento).
    /// </summary>
    public static (Polyline2? Path, List<Vec2>? Face, string? Error) FreeFace(IntersectionLayout L, List<Polygon2> pav, IntersectionLeg la, IntersectionLeg lb,
        IReadOnlyList<Vec2> local, double radius)
    {
        if (local.Count < 2) return (null, null, "informe pelo menos dois pontos");
        var world = local.Select(v => ToWorld(L, la, v)).ToList();
        // Meio-fio antigo inteiro da esquina: ramo A (de longe) → curva → ramo B (para longe).
        var reachA = Math.Max(1.0, MaxT(L, la) - la.Clear - 2);
        var reachB = Math.Max(1.0, MaxT(L, lb) - lb.Clear - 2);
        if (CornerPath(L, pav, la, lb, reachA, reachB, true) is not { } curb) return (null, null, "não foi possível seguir o meio-fio desta esquina");
        var (s0, d0) = curb.Project(world[0]);
        var (s1, d1) = curb.Project(world[^1]);
        if (Math.Abs(d0) > 1.0 || Math.Abs(d1) > 1.0) return (null, null, "as duas pontas do contorno precisam ficar no meio-fio (a até 1 m dele)");
        if (s0 > s1)
        {
            world.Reverse();
            (s0, s1) = (s1, s0);
        }
        if (s1 - s0 < 1.0) return (null, null, "contorno curto demais (menos de 1 m de meio-fio entre as pontas)");
        var path = new Polyline2(CleanFacePath(curb.SubPoints(s0, s1)));
        var raw = new List<Vec2> { path.Points[0] };
        raw.AddRange(world.Skip(1).Take(world.Count - 2));
        raw.Add(path.Points[^1]);
        var face = CurveTools.FilletCorners(CleanFacePath(raw), Math.Max(0, radius));
        // Contorno fechado (face + meio-fio antigo de volta) sem cruzar.
        var ring = face.Concat(Enumerable.Reverse(path.Points).Skip(1).Take(path.Points.Count - 2)).ToList();
        var signed = Math.Abs(Polygon2.SignedArea(ring));
        var u = PolygonOps.Union(new[] { new Polygon2(ring) });
        if (signed < 0.05 || u.Count != 1 || Math.Abs(PolygonOps.TotalArea(u) - signed) > Math.Max(0.02, 0.01 * signed))
            return (null, null, "o contorno se cruza ou cruza o meio-fio – os pontos devem seguir de uma ponta à outra pelo lado da pista");
        // Só sobre a pista (não entra na calçada nem no lote) e fora das faixas de rolamento.
        var roadway = PolygonOps.Union(pav.Concat(L.Roads.SelectMany(r => RoadGenerator.Band(r.Axis, -r.Def.RightWidth, r.Def.LeftWidth))));
        var outside = PolygonOps.TotalArea(PolygonOps.Difference(u, PolygonOps.Offset(roadway, 0.03)));
        if (outside > 0.02) return (null, null, $"o contorno sai da pista ({outside:0.00} m² sobre a calçada ou o lote)");
        var inv = PolygonOps.TotalArea(PolygonOps.Intersect(u, TravelBands(L)));
        if (inv > 0.02) return (null, null, $"o contorno avança {inv:0.00} m² sobre faixa de rolamento");
        return (path, face, null);
    }

    /// <summary>Ponto em coordenadas do ramo (x ao longo dele a partir do nó, y à esquerda) → coordenadas do projeto.</summary>
    public static Vec2 ToWorld(IntersectionLayout L, IntersectionLeg leg, Vec2 v) => L.Node + leg.Dir * v.X + leg.Dir.PerpLeft * v.Y;

    /// <summary>Ponto do projeto → coordenadas do ramo (x ao longo dele a partir do nó, y à esquerda).</summary>
    public static Vec2 ToLeg(IntersectionLayout L, IntersectionLeg leg, Vec2 p) => new((p - L.Node).Dot(leg.Dir), (p - L.Node).Dot(leg.Dir.PerpLeft));

    /// <summary>Ponto de cruzamento dos segmentos ab e cd (nulo = não se cruzam).</summary>
    private static Vec2? Cross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        var r = b - a;
        var q = d - c;
        var den = r.Cross(q);
        if (Math.Abs(den) < 1e-12) return null;
        var t = (c - a).Cross(q) / den;
        var u = (c - a).Cross(r) / den;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? a + r * t : null;
    }

    /// <summary>
    /// Trechos de cada lado cobertos por um contorno livre: avanço da face sobre o meio-fio de cada ramo (estações onde ela tem
    /// a largura toda e onde começa/termina), para a travessia, as rampas e as placas acompanharem a borda dela.
    /// </summary>
    private static IEnumerable<((int, int, int) Key, EarSide Side)> FreeSides(IntersectionLayout L, List<Vec2> face, IntersectionLeg la, IntersectionLeg lb)
    {
        var line = new Polyline2(face);
        foreach (var (leg, side) in new[] { (la, 1), (lb, -1) })
        {
            var max = MaxT(L, leg);
            var samples = new List<(double T, double Inset)>();
            for (var t = leg.Clear; t <= max - 0.5; t += 0.25)
            {
                var edge = side > 0 ? L.HiEdge(leg, t) : L.LoEdge(leg, t);
                var a = L.At(leg, t, side * edge);
                var b = L.At(leg, t, 0);
                // Ponto da face na seção t: do meio-fio para o eixo.
                var best = double.NaN;
                for (int i = 0; i + 1 < face.Count; i++)
                    if (Cross(a, b, face[i], face[i + 1]) is { } x)
                    {
                        var o = a.DistanceTo(x);
                        if (double.IsNaN(best) || o > best) best = o;
                    }
                samples.Add((t, double.IsNaN(best) ? 0 : best));
            }
            var on = samples.Where(x => x.Inset > 0.05).ToList();
            if (on.Count == 0) continue;
            var depth = on.Max(x => x.Inset);
            var full = on.Where(x => x.Inset >= depth - 0.05).ToList();
            yield return ((leg.Road, leg.Sign, side), new EarSide(depth, full[0].T, full[^1].T, on[0].T - 0.25, on[^1].T + 0.25));
        }
    }

    /// <summary>Lados de ramo voltados para um lado contínuo (dois ramos alinhados, sem esquina entre eles): (ramo, lado).</summary>
    public static IEnumerable<(IntersectionLeg Leg, int Side)> StraightSides(IntersectionLayout L)
    {
        if (L.IsBend || L.Legs.Count < 3) yield break;
        var sorted = L.Legs.Select(l => (Leg: l, A: AngleOf(l.Dir))).OrderBy(x => x.A).ToList();
        for (int k = 0; k < sorted.Count; k++)
        {
            var (la, aa) = sorted[k];
            var (lb, ab) = sorted[(k + 1) % sorted.Count];
            if (!Straight(la, lb, Ccw(aa, ab))) continue;
            yield return (la, 1);
            yield return (lb, -1);
        }
    }

    /// <summary>Extensões pedidas nos lados contínuos das travessias (opção geral ou ajuste do ramo).</summary>
    private static IEnumerable<(IntersectionLeg Leg, int Side)> StraightEarSides(IntersectionDefinition d, IntersectionLayout L) =>
        StraightSides(L).Where(x => CrosswalkOn(d, L, x.Leg) && (LegSet(d, L, x.Leg)?.OppositeExtension ?? (d.CurbExtensions && d.CurbExtensionsOpposite)));

    /// <summary>
    /// Extensão no lado contínuo de uma travessia (ex.: lado oposto de um T): ao longo do meio-fio reto, cobrindo a faixa e as
    /// rampas (+ as pontas e o comprimento a mais pedido); a travessia e a rampa daquele lado vão para a borda dela.
    /// </summary>
    private static void PlanStraightEars(IntersectionDefinition d, IntersectionLayout L, List<string> notes, bool provisional)
    {
        if (d.RightTurnIslands != TipoIlha.Nenhuma) return;
        foreach (var (leg, side) in StraightEarSides(d, L).ToList())
        {
            var ls = LegSet(d, L, leg);
            var r = L.Roads[leg.Road].Def;
            var left = side > 0 ? leg.Sign > 0 : leg.Sign < 0;
            if ((left ? r.LeftSidewalk : r.RightSidewalk) <= 0.5) continue;
            var park = ParkingDepth(r, left);
            var explicitDepth = ls?.OppositeExtensionDepth is > 0.1 ? ls.OppositeExtensionDepth : d.CurbExtensionDepth is > 0.1 ? d.CurbExtensionDepth : null;
            if (park == null && explicitDepth == null)
            {
                notes.Add("lado contínuo sem faixa de estacionamento junto ao meio-fio não recebe extensão (informe a largura do avanço para forçar)");
                continue;
            }
            var depth = explicitDepth ?? park!.Value;
            var gutter = park != null ? ParkingGutter(r, left) : 0;
            // Nunca além da faixa junto ao meio-fio sem tráfego (estacionamento, acostamento...).
            if (CurbRoom(r, left) is { } room)
            {
                if (room.Width - room.Gutter < 0.3)
                {
                    notes.Add("lado contínuo sem estacionamento ou acostamento junto ao meio-fio: a extensão invadiria a faixa de rolamento e não foi feita");
                    continue;
                }
                if (depth > room.Width + 0.005)
                {
                    notes.Add($"largura da extensão do lado contínuo reduzida de {depth:0.00} m para {room.Width:0.00} m para não invadir a faixa de rolamento");
                    depth = room.Width;
                }
                gutter = room.Gutter;
            }
            depth = Math.Clamp(depth, 0.3, 10);
            if (depth - gutter < 0.6) gutter = 0;
            var face = depth - gutter;
            var ends = ls?.OppositeExtensionEnds ?? d.CurbExtensionEnds;
            var endR = EndParam(ends, ls?.OppositeExtensionTaper ?? d.CurbExtensionTaper, ls?.OppositeExtensionEndRadius, face);
            var lt = EarTransition(ends, endR, face);
            var tc = CrosswalkT(d, L, leg);
            var plan = RampPlanOf(d, L, leg);
            var rp = side > 0 ? plan.Hi : plan.Lo;
            var margin = Math.Max(0, ls?.CurbExtensionCrosswalkMargin ?? d.CurbExtensionCrosswalkMargin ?? DefaultCrosswalkMargin);
            var half = Math.Max(CrosswalkWidthOf(d, L, leg) / 2, rp == null ? 0 : RampHalfExtent(rp)) + margin + (provisional ? 1.0 : 0);
            var t0 = Math.Max(0.5, tc - half - lt - Math.Max(0, ls?.OppositeExtensionBefore ?? 0));
            var t1 = Math.Min(MaxT(L, leg) - 2, tc + half + lt + Math.Max(0, ls?.OppositeExtensionAfter ?? 0));
            if (t1 - t0 < 2 * lt + 1)
            {
                notes.Add("ramo curto para a extensão do lado contínuo");
                continue;
            }
            var pts = new List<Vec2>();
            var n = Math.Max(2, (int)Math.Ceiling((t1 - t0) / 0.5));
            for (int k = 0; k <= n; k++)
            {
                var t = t0 + (t1 - t0) * k / (double)n;
                pts.Add(L.At(leg, t, side > 0 ? L.HiEdge(leg, t) : -L.LoEdge(leg, t)));
            }
            var path = new Polyline2(CleanFacePath(pts));
            // Lado alto (+o) fica à esquerda de quem segue o ramo para fora do nó.
            var tpl = new CurbExtensionDefinition
            {
                Depth = face, Transition = ends, EndTransition = ends, Radius = endR, EndRadius = endR, SidewalkOnLeft = side > 0,
                Height = L.CurbHeight, CurbWidth = L.CurbWidth, CutRoadMarkings = true,
            };
            var fp = SidewalkGenerator.EarFootprint(tpl, path);
            if (fp == null) continue;
            Polyline2? gutterPath = null;
            if (gutter > 0.01)
            {
                var edge = new Polyline2(CleanFacePath(SidewalkGenerator.EarOuterEdge(tpl, path)));
                if (edge.Length > 1) gutterPath = edge.Offset(side > 0 ? -gutter / 2 : gutter / 2);
            }
            var cut = SidewalkGenerator.EarCutZone(tpl, path);
            if (gutterPath != null) cut = PolygonOps.Union(cut.Concat(PolygonOps.Strip(gutterPath.Points, gutter, roundJoins: true)));
            L.Ears.Add(new EarPlan(leg, leg, side > 0, side < 0, t1 - t0, 0, depth, path, fp, tpl)
            {
                Gutter = gutter, GutterPath = gutterPath, CutZone = cut, Straight = true,
            });
            L.EarSides[(leg.Road, leg.Sign, side)] = new EarSide(face, t0 + lt, t1 - lt, t0, t1)
            {
                Ramp = ls?.OppositeExtensionRamp ?? true, Tactile = ls?.OppositeExtensionTactile,
            };
        }
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
        var clean = CleanFacePath(pts);
        return clean.Count >= 2 ? new Polyline2(clean) : null;
    }

    /// <summary>
    /// Caminho da face do meio-fio sem idas e voltas: nas emendas do trecho reto com a curva da esquina o ponto do fim da
    /// curva e o do contorno da pista não coincidem e o caminho dava um degrau de ~3 cm para dentro da calçada – o contorno
    /// externo da orelha contornava esse degrau com um arco de 90° do raio do avanço (o "lóbulo" na pista) e voltava (o
    /// dente). Tira os degraus curtos com giro forte, as voltas e os pontos colados.
    /// </summary>
    public static List<Vec2> CleanFacePath(IReadOnlyList<Vec2> input)
    {
        var pts = input.ToList();
        var changed = true;
        while (changed && pts.Count > 2)
        {
            changed = false;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                var c = pts[i + 1];
                var ab = b - a;
                var bc = c - b;
                var la = ab.Length;
                var lc = bc.Length;
                var cos = la > 1e-9 && lc > 1e-9 ? ab.Dot(bc) / (la * lc) : 1;
                // Degrau curto (até 0,15 m) com giro de mais de 45°, ou volta (mais de 100°) num trecho de até 1 m.
                var step = Math.Min(la, lc) < 0.15 && cos < 0.7 || Math.Min(la, lc) < 1.0 && cos < -0.17;
                if (la < 0.02 || step)
                {
                    pts.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
        if (pts.Count >= 2 && pts[^1].DistanceTo(pts[^2]) < 0.02) pts.RemoveAt(pts.Count - 2);
        return pts;
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
