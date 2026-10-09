using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Ciclofaixa de uma via no nó: lado (+1 esquerda / −1 direita do eixo), bordas interna (A) e externa (B) e o elemento.</summary>
public sealed record CycleBand(int Road, int Side, double A, double B, ElementoSecao Element)
{
    public double Width => B - A;
    /// <summary>Deslocamento (com sinal) do centro da ciclofaixa em relação ao eixo da via.</summary>
    public double Center => Side * (A + B) / 2;
    /// <summary>Lado dos quadrados da MCC (m): 0,50 nas ciclofaixas largas (bidirecionais), 0,40 nas demais.</summary>
    public double Square => Width >= 2.2 ? 0.5 : 0.4;
}

/// <summary>Tipo de trecho da ciclofaixa dentro da interseção.</summary>
public enum TipoTrechoCiclo
{
    /// <summary>Junto a meio-fio ou calçada (lado sem boca): a ciclofaixa segue com as linhas e o fundo dela.</summary>
    Continuo,
    /// <summary>Atravessando a pista de outra via (boca ou miolo): marcação de cruzamento rodocicloviário (MCC).</summary>
    Cruzamento,
}

/// <summary>
/// Trecho de ciclofaixa na interseção: centro (pontos), tipo e estacas no eixo da própria via (NaN no prolongamento de uma via
/// que termina no nó e atravessa a outra).
/// </summary>
public sealed record CycleRun(CycleBand Band, TipoTrechoCiclo Kind, List<Vec2> Center, double S0, double S1)
{
    public bool Extension => double.IsNaN(S0);

    /// <summary>Faixa ocupada (largura da ciclofaixa + 2 × <paramref name="grow"/>, alongada <paramref name="extend"/> em cada ponta).</summary>
    public List<Polygon2> Area(double grow = 0, double extend = 0)
    {
        var pts = Center.ToList();
        if (pts.Count < 2) return new();
        if (extend > 0)
        {
            pts.Insert(0, pts[0] + (pts[0] - pts[1]).Normalized() * extend);
            pts.Add(pts[^1] + (pts[^1] - pts[^2]).Normalized() * extend);
        }
        return PolygonOps.Strip(pts, Math.Max(0.05, Band.Width + 2 * grow));
    }
}

/// <summary>Trecho (estacas no eixo da via) em que a linha de delimitação fica seccionada antes do cruzamento.</summary>
public sealed record CycleDash(CycleBand Band, double S0, double S1);

/// <summary>Ciclofaixas no nó: trechos e zonas de conflito.</summary>
public sealed record CyclePlan(List<CycleBand> Bands, List<CycleRun> Runs, List<CycleDash> Dashes)
{
    public static CyclePlan Empty { get; } = new(new(), new(), new());
}

/// <summary>
/// Ciclofaixas nas interseções (MBST Vol. IV – marcação de cruzamento rodocicloviário, MCC): a ciclofaixa da seção da via segue
/// pelo nó – contínua junto ao meio-fio (lado sem boca), com as linhas de quadrados da MCC onde atravessa a pista de outra via
/// (pintura colorida opcional) e com a linha de delimitação seccionada na zona de conflito com as conversões. Ciclofaixas que se
/// cruzam formam a interseção entre elas (os quadrados param na borda da outra; a cor é pintada uma vez).
/// </summary>
public static partial class IntersectionGenerator
{
    private const double CycleStep = 0.25;

    /// <summary>Variante de uma linha só de quadrados da MCC.</summary>
    private static string MccLine(double square) => square > 0.45 ? "Linha de quadrados 0,50 m" : "Linha de quadrados 0,40 m";

    private const string DashedCycleLine = "Seccionada 0,20 m (1 × 1 m)";

    private enum CycleCls { Fora, Livre, Continuo, Cruzamento }

    /// <summary>Ciclofaixas no nível da pista das vias do nó, lidas da seção gravada no pavimento de cada via.</summary>
    public static List<CycleBand> CycleBands(IntersectionLayout L)
    {
        var res = new List<CycleBand>();
        for (int k = 0; k < L.Roads.Count; k++)
        {
            var s = RoadTemplates.FromJson(L.Roads[k].Def.SetupJson);
            if (s == null || s.SoPavimento || s.Pavement == TipoPavimento.Terra) continue;
            foreach (var left in new[] { true, false })
                foreach (var (e, a, b) in s.Intervalos(left))
                    if (e.Tipo == TipoElementoSecao.Ciclofaixa && !e.Elevado) res.Add(new CycleBand(k, left ? 1 : -1, a, b, e));
        }
        return res;
    }

    /// <summary>Trechos das ciclofaixas e zonas de conflito do nó (calculados uma vez por leiaute).</summary>
    public static CyclePlan CyclePlanOf(IntersectionLayout L)
    {
        if (L.CyclePlanCache != null) return L.CyclePlanCache;
        L.CyclePlanCache = CyclePlan.Empty;
        var d = L.Definition;
        if (d == null || L.IsBend || L.Legs.Count <= 2 || L.Roads.Count < 2) return L.CyclePlanCache;
        var bands = CycleBands(L);
        if (bands.Count == 0) return L.CyclePlanCache;
        var plan = new CyclePlan(bands, new(), new());
        foreach (var b in bands)
        {
            var legs = L.Legs.Where(l => l.Road == b.Road).ToList();
            if (legs.Count == 0) continue;
            PlanBand(d, L, b, legs, bands, plan);
        }
        L.CyclePlanCache = plan;
        return plan;
    }

    /// <summary>A aproximação tem retenção (PARE, dê a preferência ou semáforo).</summary>
    private static bool Controlled(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg l)
    {
        var own = LegSet(d, L, l)?.Control;
        var control = own ?? d.Control;
        return control == ControleIntersecao.Semaforo
               || (own != null || l.Road != L.Main) && control is ControleIntersecao.Pare or ControleIntersecao.DePreferencia;
    }

    /// <summary>Distância do nó a partir da qual a ciclofaixa fica sem pintura no ramo (faixa de pedestres, retenção).</summary>
    private static double BlankFrom(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg l)
    {
        var t = CrosswalkOn(d, L, l) ? CrosswalkNearT(d, L, l) - 0.3 : Controlled(d, L, l) ? l.Clear : double.PositiveInfinity;
        // Gota/bolsão: as linhas da via são refeitas deslocadas a partir da área do cruzamento.
        return L.FeatureOf(l) != null ? Math.Min(t, l.Clear) : t;
    }

    private static void PlanBand(IntersectionDefinition d, IntersectionLayout L, CycleBand b, List<IntersectionLeg> legs, List<CycleBand> bands, CyclePlan plan)
    {
        var r = L.Roads[b.Road];
        var axis = r.Axis;
        var paint = L.PaintCuts.GetValueOrDefault(b.Road) ?? new();
        var mouth = L.Mouths.GetValueOrDefault(b.Road) ?? new();
        var edge = b.Side > 0 ? r.Def.LeftWidth : r.Def.RightWidth;
        var through = legs.Count >= 2;
        var blank = legs.ToDictionary(l => l, l => BlankFrom(d, L, l));

        // Via que termina no nó: a ciclofaixa atravessa a via que segue até a ciclofaixa do outro lado dela.
        CycleRun? ext = through ? null : Extension(d, L, b, legs[0], bands, blank[legs[0]]);

        CycleCls Classify(double s)
        {
            var p = axis.PointAt(s);
            var n = axis.TangentAt(s).PerpLeft;
            if (!paint.Any(q => q.Contains(p + n * b.Center))) return CycleCls.Fora;
            foreach (var l in legs)
            {
                var t = (s - l.NodeStation) * l.Sign;
                if (t > 0 && t >= blank[l]) return CycleCls.Livre;
            }
            if (!through) return ext != null ? CycleCls.Cruzamento : CycleCls.Livre;
            // Além do bordo, do lado da ciclofaixa: pista de outra via (boca) = cruzamento; meio-fio/calçada = segue contínua.
            return mouth.Any(m => m.Contains(p + n * (b.Side * (edge + 0.4)))) ? CycleCls.Cruzamento : CycleCls.Continuo;
        }

        // Amostras ao longo do eixo, do fim do recorte de um ramo ao do outro; mudanças refinadas por bisseção.
        var ends = legs.Select(l => Math.Clamp(l.StationAt(Math.Min(MaxT(L, l), l.Clear + StopFar(d, L, l) + 2)), 0, axis.Length)).ToList();
        var s0 = Math.Min(ends.Min(), through ? ends.Min() : legs[0].NodeStation);
        var s1 = Math.Max(ends.Max(), through ? ends.Max() : legs[0].NodeStation);
        var samples = new List<(double S, CycleCls C)>();
        for (var s = s0; s < s1 + CycleStep / 2; s += CycleStep) samples.Add((Math.Min(s, s1), Classify(Math.Min(s, s1))));
        if (samples.Count < 2) return;
        var cuts = new List<double> { samples[0].S };
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].C == samples[i - 1].C) continue;
            double a = samples[i - 1].S, c = samples[i].S;
            var ca = samples[i - 1].C;
            for (int it = 0; it < 7; it++)
            {
                var m = (a + c) / 2;
                if (Classify(m) == ca) a = m; else c = m;
            }
            cuts.Add((a + c) / 2);
        }
        cuts.Add(samples[^1].S);
        // Intervalos homogêneos [cuts[i], cuts[i+1]] com a classe do meio.
        var spans = new List<(double S0, double S1, CycleCls C)>();
        for (int i = 0; i + 1 < cuts.Count; i++)
            if (cuts[i + 1] - cuts[i] > 1e-6) spans.Add((cuts[i], cuts[i + 1], Classify((cuts[i] + cuts[i + 1]) / 2)));

        if (through)
            foreach (var (a, c, cls) in spans)
            {
                if (cls is not (CycleCls.Continuo or CycleCls.Cruzamento) || c - a < 0.8) continue;
                var center = new Polyline2(axis.SubPoints(a, c)).Offset(b.Center).Points.ToList();
                plan.Runs.Add(new CycleRun(b, cls == CycleCls.Cruzamento ? TipoTrechoCiclo.Cruzamento : TipoTrechoCiclo.Continuo, center, a, c));
            }
        else if (ext != null)
            plan.Runs.Add(ext);

        // Zona de conflito: aproximação (lado de chegada) com boca de outra via do lado da ciclofaixa – os veículos que
        // convertem cruzam a ciclofaixa; a linha fica seccionada do cruzamento até Lc além do fim da área do cruzamento.
        var lc = b.Element.ZonaConflitoEfetiva;
        if (lc < 0.5) return;
        foreach (var l in legs)
        {
            if (L.FeatureOf(l) != null) continue;
            var inbound = r.Def.TwoWay ? b.Side * l.Sign > 0 : l.Sign < 0;
            if (!inbound) continue;
            var sideVec = l.Dir.PerpLeft * Math.Sign(b.Side * l.Sign);
            if (!L.Legs.Any(o => o.Road != b.Road && o.Dir.Dot(sideVec) > 0.2)) continue;
            double T(double s) => (s - l.NodeStation) * l.Sign;
            var tA = through
                ? spans.Where(x => x.C == CycleCls.Cruzamento).SelectMany(x => new[] { T(x.S0), T(x.S1) }).Where(t => t > 0).DefaultIfEmpty(l.Clear).Max()
                : Math.Min(blank[l], l.Clear);
            var tP = spans.Where(x => x.C == CycleCls.Livre).SelectMany(x => new[] { T(x.S0), T(x.S1) }).Where(t => t >= tA - 1e-6).DefaultIfEmpty(tA).Max();
            var tB = Math.Min(Math.Max(tA, tP) + lc, LegLimit(L, l, d.NeighborNodes));
            if (tB - tA < 1) continue;
            // Só onde a ciclofaixa é pintada (fora da faixa de pedestres e da área da retenção).
            foreach (var (a, c, cls) in spans)
            {
                if (cls is not (CycleCls.Fora or CycleCls.Continuo)) continue;
                var (u0, u1) = (Math.Max(Math.Min(T(a), T(c)), tA), Math.Min(Math.Max(T(a), T(c)), tB));
                if (u1 - u0 > 0.5) plan.Dashes.Add(new CycleDash(b, Math.Min(l.StationAt(u0), l.StationAt(u1)), Math.Max(l.StationAt(u0), l.StationAt(u1))));
            }
            // Depois das amostras (fora do recorte), até o fim da zona.
            var tEnd = spans.Count == 0 ? tA : spans.SelectMany(x => new[] { T(x.S0), T(x.S1) }).Max();
            if (tB - Math.Max(tEnd, tA) > 0.5)
            {
                var (u0, u1) = (Math.Max(tEnd, tA), tB);
                plan.Dashes.Add(new CycleDash(b, Math.Min(l.StationAt(u0), l.StationAt(u1)), Math.Max(l.StationAt(u0), l.StationAt(u1))));
            }
        }
    }

    /// <summary>
    /// Ciclofaixa de uma via que termina no nó: segue em frente pela pista da via que atravessa o nó até a borda interna da
    /// ciclofaixa do outro lado dela (nula quando a via atravessada não tem ciclofaixa daquele lado).
    /// </summary>
    private static CycleRun? Extension(IntersectionDefinition d, IntersectionLayout L, CycleBand b, IntersectionLeg leg, List<CycleBand> bands, double blank)
    {
        var j = L.Legs.Count(l => l.Road == L.Main) >= 2 && L.Main != b.Road ? L.Main
            : Enumerable.Range(0, L.Roads.Count).FirstOrDefault(k => k != b.Road && L.Legs.Count(l => l.Road == k) >= 2, -1);
        if (j < 0) return null;
        var axisJ = L.Roads[j].Axis;
        var o = b.Center * leg.Sign;
        var origin = L.At(leg, 0, o);
        var dir = leg.Dir.Normalized();
        Vec2 P(double t) => t >= 0 ? L.At(leg, t, o) : origin + dir * t;
        var near = Math.Sign(axisJ.Project(L.At(leg, Math.Max(leg.Clear, 5), 0)).Signed);
        if (near == 0) return null;
        var far = bands.Where(x => x.Road == j && x.Side == -near).OrderBy(x => x.A).FirstOrDefault();
        if (far == null) return null;
        double F(double t) => -near * axisJ.Project(P(t)).Signed - far.A;
        var t0 = Math.Min(blank, leg.Clear + StopFar(d, L, leg));
        if (double.IsInfinity(t0)) t0 = leg.Clear;
        var lim = -(far.A + L.Roads[j].Def.LeftWidth + L.Roads[j].Def.RightWidth + 10);
        double? tEnd = null;
        for (var t = t0; t > lim; t -= CycleStep)
        {
            if (F(t) < 0) continue;
            double a = t + CycleStep, c = t;
            for (int it = 0; it < 12; it++)
            {
                var m = (a + c) / 2;
                if (F(m) >= 0) c = m; else a = m;
            }
            tEnd = c;
            break;
        }
        if (tEnd is not { } te || t0 - te < 1) return null;
        var n = Math.Max(1, (int)Math.Ceiling((t0 - te) / 1.0));
        var pts = Enumerable.Range(0, n + 1).Select(i => P(t0 - (t0 - te) * i / n)).ToList();
        return new CycleRun(b, TipoTrechoCiclo.Cruzamento, pts, double.NaN, double.NaN);
    }

    /// <summary>A linha está numa borda da ciclofaixa (delimitação).</summary>
    private static bool OnBandEdge(LinearMarkingDefinition m, CycleBand b) =>
        Math.Abs(m.Offset - b.Side * b.A) < 0.2 || Math.Abs(m.Offset - b.Side * b.B) < 0.2;

    /// <summary>Linhas e fundo da ciclofaixa entre as marcas da via (delimitação, fundo e linha central).</summary>
    private static IEnumerable<LinearMarkingDefinition> BandLines(IEnumerable<MarkingDefinition> members, CycleBand b, IntersectionRoad r)
    {
        var key = RoadSectionInference.PathKey(r.Def.Path);
        foreach (var m in members.OfType<LinearMarkingDefinition>())
        {
            if (m.Code is not ("CIC-LD" or "CIC-FD" or "CIC-LC")) continue;
            if (m.PathRef == null || RoadSectionInference.PathKey(m.PathRef) != key) continue;
            var off = m.Offset * b.Side;
            if (off < b.A - 0.2 || off > b.B + 0.2) continue;
            yield return m;
        }
    }

    /// <summary>A linha de delimitação já é seccionada (configuração da ciclofaixa).</summary>
    private static bool AlreadyDashed(LinearMarkingDefinition m) =>
        m.PatternOverride is { Length: >= 2 } || (m.Variant ?? "").StartsWith("Seccionada", StringComparison.OrdinalIgnoreCase);

    /// <summary>Recortes nas marcas da via <paramref name="road"/> causados pelas ciclofaixas do nó.</summary>
    private static List<Polygon2> CycleCuts(MarkingDefinition member, int road, IntersectionLayout L)
    {
        if (member is IAnnotationDefinition or RoadPavementDefinition or ParkingMarkingDefinition || IsPhysical(member)) return new();
        var plan = CyclePlanOf(L);
        if (plan.Runs.Count == 0 && plan.Dashes.Count == 0) return new();
        var res = new List<Polygon2>();
        var lin = member as LinearMarkingDefinition;
        foreach (var run in plan.Runs.Where(x => x.Kind == TipoTrechoCiclo.Cruzamento))
        {
            var b = run.Band;
            if (b.Road == road)
            {
                if (run.Extension) continue;
                var (lo, hi) = b.Side > 0 ? (b.A - 0.15, b.B + 0.15) : (-(b.B + 0.15), -(b.A - 0.15));
                res.AddRange(RoadGenerator.Band(new Polyline2(L.Roads[road].Axis.SubPoints(run.S0, run.S1)), lo, hi));
            }
            else
                // A travessia de ciclistas interrompe as linhas da via atravessada (como a faixa de pedestres); a ciclofaixa
                // de chegada abre a linha de delimitação, mas o fundo dela segue.
                res.AddRange(run.Area(b.Square / 2 + 0.05, lin is { Code: "CIC-FD" } ? 0 : 0.3));
        }
        if (lin is { Code: "CIC-LD" } && !AlreadyDashed(lin))
            foreach (var z in plan.Dashes.Where(z => z.Band.Road == road && OnBandEdge(lin, z.Band)))
                res.AddRange(RoadGenerator.Band(new Polyline2(L.Roads[road].Axis.SubPoints(z.S0, z.S1)), lin.Offset - 0.3, lin.Offset + 0.3));
        return res;
    }

    /// <summary>Marcas das ciclofaixas no nó: MCC, pintura colorida, trechos contínuos e linhas seccionadas da zona de conflito.</summary>
    private static List<MarkingDefinition> CycleChildren(IntersectionLayout L, double z, IReadOnlyList<IReadOnlyCollection<MarkingDefinition>>? roadMembers)
    {
        var res = new List<MarkingDefinition>();
        var plan = CyclePlanOf(L);
        if (plan.Runs.Count == 0 && plan.Dashes.Count == 0) return res;
        var crossings = plan.Runs.Where(x => x.Kind == TipoTrechoCiclo.Cruzamento).ToList();
        int Prio(CycleRun x) => x.Band.Road == L.Main ? -1 : x.Band.Road;
        void Exclude(MarkingDefinition m, IEnumerable<Polygon2> zones)
        {
            foreach (var p in zones) m.Exclusions.Add(new ExclusionZone { SourceId = L.Definition?.Id, Points = p.Outer.ToList() });
        }
        IEnumerable<Polygon2> Others(CycleBand b, double extend) =>
            crossings.Where(o => o.Band.Road != b.Road).SelectMany(o => o.Area(o.Band.Square / 2 + 0.02, extend));
        List<Polygon2> DashZone(CycleBand b, LinearMarkingDefinition m) =>
            plan.Dashes.Where(x => x.Band == b).SelectMany(x =>
                RoadGenerator.Band(new Polyline2(L.Roads[b.Road].Axis.SubPoints(x.S0, x.S1)), m.Offset - 0.3, m.Offset + 0.3)).ToList();

        foreach (var run in plan.Runs)
        {
            var b = run.Band;
            var e = b.Element;
            if (run.Kind == TipoTrechoCiclo.Cruzamento)
            {
                var others = crossings.Where(o => o.Band.Road != b.Road).ToList();
                foreach (var sg in new[] { 1.0, -1.0 })
                {
                    var m = new LinearMarkingDefinition
                    {
                        Code = "MCC", Variant = MccLine(b.Square), Offset = sg * b.Width / 2, PathRef = PathReference.FromPoints(run.Center, z),
                    };
                    Exclude(m, others.SelectMany(o => o.Area(o.Band.Square / 2 + 0.02)));
                    res.Add(m);
                }
                if (e.CruzamentoColorido && b.Width - b.Square > 0.2)
                {
                    var c = new LinearMarkingDefinition
                    {
                        Code = "CIC-FD", WidthOverride = b.Width - b.Square, ColorOverride = e.CorCruzamento ?? MarkingColor.Vermelha,
                        PathRef = PathReference.FromPoints(run.Center, z),
                    };
                    // Área comum com outra ciclofaixa colorida de maior prioridade: pintada uma vez (pela outra).
                    Exclude(c, others.Where(o => o.Band.Element.CruzamentoColorido && Prio(o) < Prio(run)).SelectMany(o => o.Area(-o.Band.Square / 2)));
                    res.Add(c);
                }
            }
            else if (roadMembers != null && b.Road < roadMembers.Count)
            {
                var r = L.Roads[b.Road];
                foreach (var m in BandLines(roadMembers[b.Road], b, r))
                {
                    var c = (LinearMarkingDefinition)m.CloneWithNewId();
                    c.Exclusions.Clear();
                    c.Breaks.Clear();
                    c.StartSetback = c.EndSetback = 0;
                    c.Phase = 0;
                    c.PathRef = PathReference.FromPoints(r.Axis.SubPoints(run.S0, run.S1), z);
                    if (c.Code == "CIC-LD" && !AlreadyDashed(m)) Exclude(c, DashZone(b, m));
                    Exclude(c, Others(b, c.Code == "CIC-FD" ? 0 : 0.3));
                    res.Add(c);
                }
            }
        }

        // Linha de delimitação seccionada na zona de conflito.
        if (roadMembers != null)
            foreach (var g in plan.Dashes.GroupBy(x => x.Band))
            {
                var b = g.Key;
                if (b.Road >= roadMembers.Count) continue;
                var r = L.Roads[b.Road];
                foreach (var m in BandLines(roadMembers[b.Road], b, r).Where(m => m.Code == "CIC-LD" && OnBandEdge(m, b) && !AlreadyDashed(m)))
                    foreach (var x in g)
                    {
                        var c = (LinearMarkingDefinition)m.CloneWithNewId();
                        c.Exclusions.Clear();
                        c.Breaks.Clear();
                        c.StartSetback = c.EndSetback = 0;
                        c.Phase = 0;
                        c.Variant = DashedCycleLine;
                        c.PatternOverride = null;
                        c.PathRef = PathReference.FromPoints(r.Axis.SubPoints(x.S0, x.S1), z);
                        Exclude(c, Others(b, 0.3));
                        res.Add(c);
                    }
            }
        return res;
    }

    /// <summary>As travessias de ciclistas interrompem as linhas criadas pela interseção (LCO, contínuas das aproximações).</summary>
    private static void CutByCycleCrossings(IntersectionLayout L, IEnumerable<MarkingDefinition> children)
    {
        var crossings = CyclePlanOf(L).Runs.Where(x => x.Kind == TipoTrechoCiclo.Cruzamento).ToList();
        if (crossings.Count == 0) return;
        var zones = crossings.SelectMany(o => o.Area(o.Band.Square / 2 + 0.05, 0.3)).ToList();
        foreach (var c in children.OfType<LinearMarkingDefinition>())
        {
            var code = c.Code ?? "";
            if (code.StartsWith("CIC") || NotShifted.Any(x => code.StartsWith(x)) || RoadSectionInference.IsPhysical(code)) continue;
            foreach (var p in zones) c.Exclusions.Add(new ExclusionZone { SourceId = L.Definition?.Id, Points = p.Outer.ToList() });
        }
    }
}
