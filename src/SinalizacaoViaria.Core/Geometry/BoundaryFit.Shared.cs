namespace SinalizacaoViaria.Core.Geometry;

public static partial class BoundaryFit
{
    /// <summary>
    /// Ajusta juntos os anéis dos pisos de uma marca (ajuste topológico, K13, D-016). Antes cada piso ajustava a borda comum
    /// por conta própria: as duas versões ficavam até a tolerância uma da outra e os pisos se sobrepunham numa lasca de
    /// milímetros ao longo da curva (emenda de vias em curva, rotatórias, junções com as rampas).
    /// <list type="number">
    /// <item>Cada anel é ajustado como em <see cref="FitRing"/> (o resultado de quem não tem borda comum).</item>
    /// <item>Bordas com os mesmos vértices nos dois anéis (depois das junções em T e dos trechos unificados) são ajustadas uma
    /// vez só, como trechos abertos com as pontas fixas (<see cref="Topology"/>).</item>
    /// <item>Nos demais anéis, retas e circunferências que são a mesma borda de um vizinho (a até a tolerância) passam a usar a
    /// do vizinho e os vértices são refeitos nos encontros (<see cref="Shared"/>).</item>
    /// </list>
    /// Anel cujo contorno montado não passa nas conferências fica com o ajuste próprio.
    /// </summary>
    public static List<List<BoundarySegment>?> FitRings(IReadOnlyList<IReadOnlyList<Vec2>> rings, double tolerance, double minEdge,
        IReadOnlyList<Vec2>? centers, double snapOffset)
    {
        var fits = rings.Select(r => FitRing(r, tolerance, minEdge, centers, snapOffset)).ToList();
        if (tolerance <= 0 || fits.Count(f => f != null) < 2) return fits;
        var topo = Topology(rings, tolerance, minEdge, centers, snapOffset);
        var frozen = new HashSet<int>();
        for (int i = 0; i < fits.Count; i++)
            if (topo[i] != null) { fits[i] = topo[i]; frozen.Add(i); }
        return Shared(fits, tolerance, minEdge, centers, frozen);
    }

    /// <summary>Reta ou circunferência de um trecho ajustado.</summary>
    private sealed class Prim
    {
        public int Ring, Seg, Parent;
        public BoundarySegment S;
        public bool Arc, Exact;
        public Vec2 C, P, U;
        public double R, Len;
        public Vec2 Min, Max;
    }

    /// <summary>
    /// Bordas comuns: trechos de anéis diferentes do mesmo tipo, a até <paramref name="tol"/> um do outro e com o meio do
    /// menor sobre o maior, ficam com um representante – o de centro exato do traçado ou a reta exatamente alinhada aos eixos,
    /// e entre eles o mais longo. Os anéis que usam algum representante são refeitos.
    /// </summary>
    internal static List<List<BoundarySegment>?> Shared(List<List<BoundarySegment>?> fits, double tol, double minEdge, IReadOnlyList<Vec2>? centers,
        IReadOnlySet<int>? frozen = null)
    {
        frozen ??= new HashSet<int>();
        var prims = new List<Prim>();
        var bySeg = new Dictionary<(int, int), Prim>();
        for (int r = 0; r < fits.Count; r++)
        {
            if (fits[r] is not { } ring) continue;
            for (int k = 0; k < ring.Count; k++)
            {
                var s = ring[k];
                var p = new Prim { Ring = r, Seg = k, S = s, Arc = s.IsArc, Len = s.Length, Parent = prims.Count };
                if (p.Len < 1e-9) continue;
                if (s.IsArc)
                {
                    (p.C, p.R) = s.Circle;
                    p.Exact = centers != null && centers.Any(h => h.DistanceTo(p.C) < 1e-6);
                }
                else
                {
                    p.P = s.Start;
                    p.U = (s.End - s.Start).Normalized();
                    p.Exact = AxisSnap.OffAxisDeg(s) is { } off && off < 1e-9;
                }
                var pts = s.Sample(Math.Max(1e-4, tol / 4)).Append(s.End).ToList();
                p.Min = new Vec2(pts.Min(q => q.X) - tol, pts.Min(q => q.Y) - tol);
                p.Max = new Vec2(pts.Max(q => q.X) + tol, pts.Max(q => q.Y) + tol);
                prims.Add(p);
                bySeg[(r, k)] = p;
            }
        }
        int Find(int i)
        {
            while (prims[i].Parent != i) { prims[i].Parent = prims[prims[i].Parent].Parent; i = prims[i].Parent; }
            return i;
        }
        // Pares candidatos pela caixa (varredura em X).
        var order = Enumerable.Range(0, prims.Count).OrderBy(i => prims[i].Min.X).ToList();
        for (int a = 0; a < order.Count; a++)
        {
            var pa = prims[order[a]];
            for (int b = a + 1; b < order.Count && prims[order[b]].Min.X <= pa.Max.X; b++)
            {
                var pb = prims[order[b]];
                if (pa.Ring == pb.Ring || pa.Arc != pb.Arc || pb.Min.Y > pa.Max.Y || pb.Max.Y < pa.Min.Y) continue;
                if (!SameBorder(pa, pb, tol)) continue;
                var (ra, rb) = (Find(order[a]), Find(order[b]));
                if (ra != rb) prims[ra].Parent = rb;
            }
        }
        // Representante de cada grupo; quem ficou longe dele (grupo encadeado) segue com o próprio.
        var groups = Enumerable.Range(0, prims.Count).GroupBy(Find).Where(g => g.Count() > 1);
        var rep = new Dictionary<Prim, Prim>();
        var shared = new HashSet<Prim>();
        foreach (var g in groups)
        {
            var members = g.Select(i => prims[i]).ToList();
            var best = members.OrderByDescending(m => frozen.Contains(m.Ring)).ThenByDescending(m => m.Exact).ThenByDescending(m => m.Len)
                .ThenBy(m => m.Ring).ThenBy(m => m.Seg).First();
            foreach (var m in members)
                if (m != best && Near(m, best, 1.5 * tol)) { rep[m] = best; shared.Add(m); shared.Add(best); }
        }
        if (rep.Count == 0) return fits;
        Prim? Eff(int r, int k) => bySeg.TryGetValue((r, k), out var p) ? (rep.TryGetValue(p, out var q) ? q : p) : null;

        // Reta e curva quase tangentes numa junção sem quina (giro < 3°), com uma delas comum a outro piso: a reta anda paralela
        // até tangenciar a curva (continua alinhada aos eixos se era) – o vértice fica sobre as duas e o arco, na circunferência
        // comum. Uma reta entre duas curvas que pedem deslocamentos diferentes fica onde está.
        var shifts = new Dictionary<Prim, List<Vec2>>();
        for (int r = 0; r < fits.Count; r++)
        {
            if (fits[r] is not { } ring) continue;
            for (int k = 0; k < ring.Count; k++)
            {
                var kn = (k + 1) % ring.Count;
                if (Eff(r, k) is not { } a || Eff(r, kn) is not { } b || a.Arc == b.Arc) continue;
                if (!shared.Contains(bySeg[(r, k)]) && !shared.Contains(bySeg[(r, kn)])) continue;
                if (Angle(Dir(ring[k], false), Dir(ring[kn], true)) > 3 * Math.PI / 180) continue;
                var (l, c) = a.Arc ? (b, a) : (a, b);
                var f = l.P + l.U * (c.C - l.P).Dot(l.U);
                var d = f.DistanceTo(c.C);
                if (d < 1e-9 || Math.Abs(d - c.R) > tol) continue;
                if (!shifts.TryGetValue(l, out var list)) shifts[l] = list = new List<Vec2>();
                list.Add((f - c.C) / d * (c.R - d));
            }
        }
        var moved = new HashSet<Prim>();
        foreach (var (l, list) in shifts)
        {
            if (frozen.Contains(l.Ring)) continue;
            if (list.Any(x => list.Any(y => x.DistanceTo(y) > 2e-4))) continue;
            var mean = list.Aggregate(new Vec2(0, 0), (acc, x) => acc + x) / list.Count;
            if (mean.Length < 1e-9) continue;
            l.P += mean;
            moved.Add(l);
        }

        var res = new List<List<BoundarySegment>?>(fits);
        for (int r = 0; r < fits.Count; r++)
        {
            if (fits[r] is not { } ring || frozen.Contains(r)) continue;
            var rr = r;
            bool Replaced(int k) => bySeg.TryGetValue((rr, k), out var p) && (rep.ContainsKey(p) || moved.Contains(p));
            if (!Enumerable.Range(0, ring.Count).Any(Replaced)) continue;
            if (Rebuild(ring, k => Eff(rr, k), Replaced, tol, minEdge) is { } nr) res[r] = nr;
        }
        return res;
    }

    /// <summary>Mesma borda: mesma reta ou circunferência (a até <paramref name="tol"/>) e o meio do trecho menor sobre o maior.</summary>
    private static bool SameBorder(Prim a, Prim b, double tol)
    {
        var (s, l) = a.Len <= b.Len ? (a, b) : (b, a);
        if (s.Arc)
        {
            if (s.C.DistanceTo(l.C) > tol || Math.Abs(s.R - l.R) > tol) return false;
            return Distance(l.S, s.S.Mid!.Value) <= tol;
        }
        double Off(Vec2 v) => Math.Abs((v - l.P).Cross(l.U));
        if (Off(s.S.Start) > tol || Off(s.S.End) > tol) return false;
        return Distance(l.S, (s.S.Start + s.S.End) * 0.5) <= tol;
    }

    /// <summary>O trecho de <paramref name="m"/> inteiro a até <paramref name="lim"/> da reta ou circunferência de <paramref name="r"/>.</summary>
    private static bool Near(Prim m, Prim r, double lim)
    {
        foreach (var q in m.S.Sample(lim / 4).Append(m.S.End))
        {
            var d = r.Arc ? Math.Abs(q.DistanceTo(r.C) - r.R) : Math.Abs((q - r.P).Cross(r.U));
            if (d > lim) return false;
        }
        return true;
    }

    /// <summary>
    /// Anel refeito com as retas e circunferências dadas: cada vértice no encontro dos dois trechos vizinhos (o mais perto
    /// do vértice antigo); reta e curva quase tangentes (folga até a tolerância) se encontram no ponto de tangência – na reta,
    /// se ela é exatamente alinhada aos eixos; senão no meio da folga. Nulo se o anel refeito não passa nas conferências.
    /// </summary>
    private static List<BoundarySegment>? Rebuild(List<BoundarySegment> ring, Func<int, Prim?> prim, Func<int, bool> replaced, double tol, double minEdge)
    {
        var n = ring.Count;
        if (n < 2) return null;
        var v = new Vec2[n];
        for (int k = 0; k < n; k++)
        {
            var kp = (k - 1 + n) % n;
            v[k] = ring[k].Start;
            if (!replaced(kp) && !replaced(k)) continue;
            if (prim(kp) is not { } a || prim(k) is not { } b) return null;
            if (Joint(a, b, ring[k].Start, tol) is not { } j || j.DistanceTo(ring[k].Start) > 0.5) return null;
            v[k] = j;
        }
        var res = new List<BoundarySegment>(n);
        for (int k = 0; k < n; k++)
        {
            var (s0, s1) = (v[k], v[(k + 1) % n]);
            var old = ring[k];
            if (s0.DistanceTo(s1) < minEdge) return null;
            if (!old.IsArc)
            {
                if ((s1 - s0).Dot(old.End - old.Start) <= 0) return null;
                res.Add(new BoundarySegment(s0, s1));
                continue;
            }
            var (c, r) = prim(k) is { Arc: true } p ? (p.C, p.R) : old.Circle;
            var (oc, _) = old.Circle;
            var oldSweep = Sweep(oc, old.Start, old.Mid!.Value, old.End);
            var a0 = Math.Atan2(s0.Y - c.Y, s0.X - c.X);
            var sw = Math.Atan2(s1.Y - c.Y, s1.X - c.X) - a0;
            while (sw <= -Math.PI) sw += 2 * Math.PI;
            while (sw > Math.PI) sw -= 2 * Math.PI;
            if (Math.Sign(sw) != Math.Sign(oldSweep)) sw += oldSweep > 0 ? 2 * Math.PI : -2 * Math.PI;
            if (Math.Abs(sw - oldSweep) > Math.PI / 4 || Math.Abs(sw) > MaxSweep + 1e-9) return null;
            res.Add(new BoundarySegment(s0, s1, c + Vec2.FromAngle(a0 + sw / 2) * r));
        }
        if (!IsSimple(new[] { res }, 1e-5)) return null;
        var mine = Sample(res, 0.0002);
        var before = Sample(ring, 0.0002);
        if (OneWay(mine, before) > 2 * tol || OneWay(before, mine) > 2 * tol) return null;
        return res;
    }

    /// <summary>Encontro de dois trechos seguidos (ver <see cref="Rebuild"/>); nulo se eles não se encontram perto de <paramref name="near"/>.</summary>
    private static Vec2? Joint(Prim a, Prim b, Vec2 near, double tol)
    {
        if (!a.Arc && !b.Arc)
        {
            var cr = a.U.Cross(b.U);
            if (Math.Abs(cr) < 1e-6)
            {
                // Retas paralelas seguidas: o vértice fica sobre as duas (projeção na do meio).
                var pa = a.P + a.U * (near - a.P).Dot(a.U);
                var pb = b.P + b.U * (near - b.P).Dot(b.U);
                return pa.DistanceTo(pb) <= tol ? (pa + pb) * 0.5 : null;
            }
            return a.P + a.U * ((b.P - a.P).Cross(b.U) / cr);
        }
        if (a.Arc != b.Arc)
        {
            var (l, c) = a.Arc ? (b, a) : (a, b);
            var f = l.P + l.U * (c.C - l.P).Dot(l.U);
            var d = f.DistanceTo(c.C);
            if (d > c.R + tol || d < 1e-9) return null;
            if (Math.Abs(d - c.R) <= tol)
            {
                var fc = c.C + (f - c.C).Normalized() * c.R;
                return l.Exact ? f : (f + fc) * 0.5;
            }
            var h = Math.Sqrt(c.R * c.R - d * d);
            var (q1, q2) = (f + l.U * h, f - l.U * h);
            return q1.DistanceTo(near) <= q2.DistanceTo(near) ? q1 : q2;
        }
        var L = a.C.DistanceTo(b.C);
        if (L < 1e-9) return Math.Abs(a.R - b.R) <= tol ? Project((a.C, (a.R + b.R) / 2), near) : null;
        var u = (b.C - a.C) / L;
        // Quase tangentes (por fora ou por dentro): no meio da folga, sobre a linha dos centros.
        foreach (var (sa, sb) in new[] { (1.0, -1.0), (1.0, 1.0), (-1.0, -1.0) })
        {
            var p1 = a.C + u * (sa * a.R);
            var p2 = b.C + u * (sb * b.R);
            if (p1.DistanceTo(p2) <= tol && Math.Abs(p1.DistanceTo(b.C) - b.R) <= tol && Math.Abs(p2.DistanceTo(a.C) - a.R) <= tol)
                return (p1 + p2) * 0.5;
        }
        return Meet((a.C, a.R), (b.C, b.R), near);
    }
}
