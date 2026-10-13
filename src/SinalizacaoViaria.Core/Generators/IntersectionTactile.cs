using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Generators;

public static partial class IntersectionGenerator
{
    /// <summary>Alguma via do nó pede piso tátil nas calçadas: a interseção completa a rede nas esquinas e liga as rampas dela.</summary>
    public static bool TactileNetworkOn(IntersectionLayout L) =>
        L.Roads.Any(r => RoadTemplates.FromJson(r.Def.SetupJson)?.PisoTatil == true);

    /// <summary>Medidas do lado de um ramo (+1 alto, −1 baixo): calçada e o meio da faixa livre a partir da face do meio-fio.</summary>
    private static RoadSetup.LadoVia? SideOf(IntersectionLayout L, IntersectionLeg leg, int side)
    {
        var setup = RoadTemplates.FromJson(L.Roads[leg.Road].Def.SetupJson);
        if (setup == null) return null;
        var left = side > 0 ? leg.Sign > 0 : leg.Sign < 0;
        var lado = setup.Lado(left);
        return lado.HasSidewalk && lado.Free >= 0.6 ? lado : null;
    }

    /// <summary>
    /// Estaca do ramo onde a faixa tátil da via (no meio da faixa livre daquele lado) sai da área refeita pela interseção: a rede
    /// da interseção vai até ali e emenda na da via.
    /// </summary>
    private static double TactileJoin(IntersectionLayout L, IntersectionLeg leg, int side, double o)
    {
        var cuts = L.PhysicalCuts.GetValueOrDefault(leg.Road) ?? new List<Polygon2> { L.Zone };
        Vec2 P(double t) => L.At(leg, t, side > 0 ? L.HiEdge(leg, t) + o : -(L.LoEdge(leg, t) + o));
        bool Inside(double t) => cuts.Any(c => c.Contains(P(t)));
        var max = MaxT(L, leg) - 1;
        var t0 = Math.Max(0, leg.Clear);
        var t = t0;
        while (t < max && !Inside(t)) t += 0.25;      // primeiro ponto dentro (a faixa pode começar fora junto ao nó)
        while (t < max && Inside(t)) t += 0.25;       // e a saída
        if (t >= max) return max;
        var (a, b) = (t - 0.25, t);
        for (int i = 0; i < 12; i++)
        {
            var m = (a + b) / 2;
            if (Inside(m)) a = m; else b = m;
        }
        return b;
    }

    /// <summary>
    /// Estaca até onde a rede da interseção vai no ramo: a saída da área refeita pelo nó ou, com rampa da travessia daquele lado,
    /// além da área de alerta da junção do ramal dela – a rampa fica no trecho reto do meio-fio, fora da área do nó, e o ramal
    /// precisa de uma faixa da rede para se ligar. A faixa da via é recortada até ali (<see cref="TactileOverlap"/>).
    /// </summary>
    private static double TactileEnd(IntersectionLayout L, IntersectionLeg leg, int side, double o)
    {
        var t = TactileJoin(L, leg, side, o);
        if (L.Definition is { } d && RampsOn(d, L, leg))
        {
            var plan = RampPlanOf(d, L, leg);
            if ((side > 0 ? plan.Hi : plan.Lo) is { } rp && rp.Tactile && rp.Type != TipoRampa.RebaixamentoTotal)
                t = Math.Max(t, CrosswalkT(d, L, leg) + Math.Max(RampHalfExtent(rp), TactileGenerator.JunctionAlert(0.25) / 2) + 0.5);
        }
        return Math.Min(t, MaxT(L, leg) - 1);
    }

    /// <summary>
    /// Trechos da faixa tátil da via cobertos pela rede da interseção além da área refeita pelo nó (até as junções dos ramais das
    /// rampas): a faixa da via sai ali, sem placas sobrepostas.
    /// </summary>
    private static List<Polygon2> TactileOverlap(IntersectionLayout L, int road)
    {
        var res = new List<Polygon2>();
        if (!TactileNetworkOn(L) || L.IsBend || L.Legs.Count < 3) return res;
        foreach (var leg in L.Legs.Where(l => l.Road == road))
            foreach (var side in new[] { 1, -1 })
            {
                if (SideOf(L, leg, side) is not { } lado) continue;
                var o = lado.TactileCenter();
                var t0 = TactileJoin(L, leg, side, o);
                var t1 = TactileEnd(L, leg, side, o);
                if (t1 <= t0 + 0.01) continue;
                double Off(double t, double d) => side * ((side > 0 ? L.HiEdge(leg, t) : L.LoEdge(leg, t)) + o + d);
                var pts = new List<Vec2>();
                var ts = new List<double>();
                for (var t = Math.Max(0, t0 - 0.05); t < t1; t += 0.5) ts.Add(t);
                ts.Add(t1);
                foreach (var t in ts) pts.Add(L.At(leg, t, Off(t, -0.2)));
                for (int i = ts.Count - 1; i >= 0; i--) pts.Add(L.At(leg, ts[i], Off(ts[i], 0.2)));
                res.Add(new Polygon2(pts));
            }
        return res;
    }

    /// <summary>
    /// Rede tátil da interseção: faixa direcional na faixa livre (posição pela largura dela, NBR 16537 7.8.2) em volta de cada
    /// esquina e nos lados contínuos, emendada na das vias, com um ramal no eixo de cada rebaixamento das travessias (7.8.5 b).
    /// </summary>
    private static List<MarkingDefinition> TactileNetwork(IntersectionLayout L, double z, IReadOnlyList<RampDefinition> ramps)
    {
        var res = new List<MarkingDefinition>();
        var chains = new List<Polyline2>();
        var top = L.CurbHeight;
        if (L.IsBend || L.Legs.Count < 3) return res;
        var sorted = L.Legs.Select(l => (Leg: l, A: AngleOf(l.Dir))).OrderBy(x => x.A).ToList();
        for (int k = 0; k < sorted.Count; k++)
        {
            var (la, aa) = sorted[k];
            var (lb, ab) = sorted[(k + 1) % sorted.Count];
            var sa = SideOf(L, la, 1);
            var sb = SideOf(L, lb, -1);
            if (sa == null || sb == null) continue;
            var oa = sa.TactileCenter();
            var ob = sb.TactileCenter();
            var ta = TactileEnd(L, la, 1, oa);
            var tb = TactileEnd(L, lb, -1, ob);
            if (Straight(la, lb, Ccw(aa, ab)))
            {
                // Lado contínuo: a faixa segue reta de um ramo ao outro.
                var pts = new List<Vec2>();
                for (var t = ta; t > 0; t -= 0.5) pts.Add(L.At(la, t, L.HiEdge(la, t) + oa));
                for (var t = 0.0; t < tb; t += 0.5) pts.Add(L.At(lb, t, -(L.LoEdge(lb, t) + ob)));
                pts.Add(L.At(lb, tb, -(L.LoEdge(lb, tb) + ob)));
                var clean = CleanFacePath(pts);
                if (clean.Count >= 2) chains.Add(new Polyline2(clean));
                continue;
            }
            // Esquina: o meio-fio (reto de A, curva, reto de B) deslocado para dentro da calçada (à direita do caminho).
            var curb = CornerPath(L, L.Pavement, la, lb, Math.Max(0.5, ta - la.Clear), Math.Max(0.5, tb - lb.Clear), true);
            if (curb == null) continue;
            var lenA = Math.Max(0.5, ta - la.Clear);
            var total = curb.Length;
            var lenB = Math.Max(0.5, tb - lb.Clear);
            double Off(double s) => s <= lenA ? oa : s >= total - lenB ? ob : oa + (ob - oa) * (s - lenA) / Math.Max(1e-6, total - lenA - lenB);
            var line = curb.OffsetVariable(s => -Off(s));
            var cl = CleanFacePath(line.Points);
            if (cl.Count >= 2) chains.Add(new Polyline2(cl));
        }
        if (chains.Count == 0) return res;

        // Ramais no eixo de cada rebaixamento (7.8.5 b, Figura 69): do fim da subida até a faixa mais próxima, na direção da
        // subida – sem alerta no topo da rampa (o alerta é a faixa a 0,50 m da guia, 6.6); área de alerta na junção.
        var branches = chains.Select(_ => new List<List<Vec2>>()).ToList();
        foreach (var r in ramps)
        {
            if (RoadFeatures.RampTop(r) is not { } tp) continue;
            var (a, b, dir) = tp;
            var mid = (a + b) / 2;
            var best = -1;
            var bestD = double.MaxValue;
            for (int c = 0; c < chains.Count; c++)
                if (RayHitPath(mid, dir, chains[c].Points, 10) is { } hit && hit.DistanceTo(mid) < bestD)
                {
                    bestD = hit.DistanceTo(mid);
                    best = c;
                }
            if (best < 0 || bestD <= 0.05) continue;
            branches[best].Add(new List<Vec2> { mid, mid + dir * bestD });
        }
        for (int c = 0; c < chains.Count; c++)
        {
            var route = new TactileRouteDefinition
            {
                PathRef = PathReference.FromPoints(chains[c].Points, z),
                Module = 0.25, Rows = 1, Elevation = top, AlertAtEnds = false, AlertAtJunctions = true, AlertAtTurns = true,
                Branches = branches[c].Count > 0 ? branches[c] : null,
            };
            foreach (var r in ramps)
                route.Exclusions.Add(new ExclusionZone { Points = RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points)).Outer.ToList() });
            res.Add(route);
        }
        return res;
    }

    /// <summary>
    /// Sem rede tátil: faixa direcional transversal no eixo do rebaixamento do lado <paramref name="side"/> do ramo, do fim da
    /// subida até o alinhamento do lote (NBR 16537, 7.8.5 b, Figura 69 a).
    /// </summary>
    private static TactileRouteDefinition? CrossingDirectional(IntersectionLayout L, IntersectionLeg leg, int side, double t, RampDefinition ramp, double z)
    {
        var setup = RoadTemplates.FromJson(L.Roads[leg.Road].Def.SetupJson);
        if (setup == null) return null;
        var lado = setup.Lado(side > 0 ? leg.Sign > 0 : leg.Sign < 0);
        if (!lado.HasSidewalk) return null;
        var lot = (side > 0 ? L.HiEdge(leg, t) : L.LoEdge(leg, t)) + lado.SidewalkWidth;
        var origin = L.At(leg, t, 0);
        var across = (L.At(leg, t, side) - origin).Normalized();
        return RoadFeatures.CrossingDirectional(ramp, p => (p - origin).Dot(across), lot, z, L.CurbHeight);
    }

    /// <summary>Primeiro ponto em que o raio p + d·u (0 ≤ u ≤ <paramref name="max"/>) cruza a polilinha.</summary>
    public static Vec2? RayHitPath(Vec2 p, Vec2 d, IReadOnlyList<Vec2> path, double max)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < path.Count; i++)
            if (RayHit(p, d, path[i], path[i + 1], out var u) && u <= max && u < best) best = u;
        return best < double.MaxValue ? p + d * best : null;
    }

    /// <summary>Raio p + d·u (u ≥ 0) cruza o segmento a–b: devolve u.</summary>
    private static bool RayHit(Vec2 p, Vec2 d, Vec2 a, Vec2 b, out double u)
    {
        u = 0;
        var e = b - a;
        var den = d.Cross(e);
        if (Math.Abs(den) < 1e-12) return false;
        var w = a - p;
        u = w.Cross(e) / den;
        var v = w.Cross(d) / den;
        return u >= -1e-6 && v >= -1e-6 && v <= 1 + 1e-6;
    }
}
