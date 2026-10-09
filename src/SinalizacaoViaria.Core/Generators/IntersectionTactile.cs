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
    /// Rede tátil da interseção: faixa direcional no meio da faixa livre em volta de cada esquina e nos lados contínuos, emendada
    /// na das vias, com um ramal perpendicular até o alerta no topo de cada rampa das travessias (NBR 16537 – posições [a
    /// confirmar]).
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
            var oa = sa.FreeCenter;
            var ob = sb.FreeCenter;
            var ta = TactileJoin(L, la, 1, oa);
            var tb = TactileJoin(L, lb, -1, ob);
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

        // Ramais: do alerta no topo de cada rampa até a faixa mais próxima, na direção da subida.
        var branches = chains.Select(_ => new List<List<Vec2>>()).ToList();
        var cover = chains.Select(_ => new List<Polygon2>()).ToList();
        foreach (var r in ramps)
        {
            if (RoadFeatures.RampTop(r) is not { } tp) continue;
            var (a, b, dir) = tp;
            var mid = (a + b) / 2;
            var end = mid + dir * RoadFeatures.RampTopAlert;
            // Faixa mais próxima ao longo da subida (até 6 m).
            var best = -1;
            Vec2 hit = default;
            var bestD = double.MaxValue;
            for (int c = 0; c < chains.Count; c++)
            {
                var pts = chains[c].Points;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    if (!RayHit(end, dir, pts[i], pts[i + 1], out var u) || u > 6 || u >= bestD) continue;
                    bestD = u;
                    best = c;
                    hit = end + dir * u;
                }
            }
            if (best < 0) continue;
            res.Add(new TactileRouteDefinition
            {
                PathRef = PathReference.FromPoints(new[] { a + dir * (RoadFeatures.RampTopAlert / 2), b + dir * (RoadFeatures.RampTopAlert / 2) }, z),
                AlertOnly = true, Module = 0.25, Rows = (int)Math.Round(RoadFeatures.RampTopAlert / 0.25), Elevation = top,
            });
            cover[best].Add(new Polygon2(new[] { a, b, b + dir * RoadFeatures.RampTopAlert, a + dir * RoadFeatures.RampTopAlert }));
            if (bestD > 0.05) branches[best].Add(new List<Vec2> { end, hit });
        }
        for (int c = 0; c < chains.Count; c++)
        {
            var route = new TactileRouteDefinition
            {
                PathRef = PathReference.FromPoints(chains[c].Points, z),
                Module = 0.25, Rows = 1, Elevation = top, AlertAtEnds = false, AlertAtJunctions = true, AlertAtTurns = true,
                Branches = branches[c].Count > 0 ? branches[c] : null,
            };
            foreach (var cv in cover[c]) route.Exclusions.Add(new ExclusionZone { Points = cv.Outer.ToList() });
            foreach (var r in ramps)
                route.Exclusions.Add(new ExclusionZone { Points = RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points)).Outer.ToList() });
            res.Add(route);
        }
        return res;
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
