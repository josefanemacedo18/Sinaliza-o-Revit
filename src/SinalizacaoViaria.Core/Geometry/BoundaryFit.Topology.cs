namespace SinalizacaoViaria.Core.Geometry;

public static partial class BoundaryFit
{
    /// <summary>Distância (m) abaixo da qual dois vértices de anéis diferentes são o mesmo: a grade das operações booleanas é 0,1 mm.</summary>
    private const double SameVertex = 1.5e-4;

    /// <summary>Fração mínima do contato entre dois anéis com os mesmos vértices para a borda ser ajustada em comum.</summary>
    private const double CleanShare = 0.95;

    /// <summary>
    /// Ajuste topológico: os trechos de contorno comuns a dois anéis (a mesma sequência de vértices, nos dois sentidos) são
    /// ajustados uma vez só, como trechos abertos com as pontas fixas, e os dois anéis usam o mesmo resultado. As pontas
    /// são os vértices onde muda o vizinho do outro lado (o encontro de três pisos). Antes, cada piso ajustava sozinho a
    /// borda comum (outras quebras, outros raios) e os dois ficavam sobrepostos numa lasca de milímetros. Nulo para o anel
    /// sem trecho comum ou cujo contorno montado não passa nas conferências (fica com o ajuste próprio).
    /// </summary>
    internal static List<List<BoundarySegment>?> Topology(IReadOnlyList<IReadOnlyList<Vec2>> rings, double tol, double minEdge,
        IReadOnlyList<Vec2>? centers, double snapOffset)
    {
        var res = new List<List<BoundarySegment>?>(rings.Select(_ => (List<BoundarySegment>?)null));
        var rs = rings.Select(r => Dedupe(r, Math.Max(1e-6, minEdge * 0.5))).ToList();
        if (rs.Count(r => r.Count >= 3) < 2) return res;
        Imprint(rs);

        // Vértices: posição única por id (a primeira vista), para os dois anéis usarem exatamente os mesmos pontos.
        var pos = new List<Vec2>();
        var grid = new Dictionary<(long, long), List<int>>();
        int Id(Vec2 v)
        {
            var (cx, cy) = ((long)Math.Floor(v.X / SameVertex), (long)Math.Floor(v.Y / SameVertex));
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (grid.TryGetValue((cx + dx, cy + dy), out var l))
                        foreach (var k in l)
                            if (pos[k].DistanceTo(v) <= SameVertex) return k;
            pos.Add(v);
            if (!grid.TryGetValue((cx, cy), out var cell)) grid[(cx, cy)] = cell = new List<int>();
            cell.Add(pos.Count - 1);
            return pos.Count - 1;
        }
        var ids = new List<List<int>>();
        foreach (var r in rs)
        {
            var l = new List<int>();
            foreach (var v in r)
            {
                var k = Id(v);
                if (l.Count == 0 || l[^1] != k) l.Add(k);
            }
            while (l.Count > 1 && l[0] == l[^1]) l.RemoveAt(l.Count - 1);
            ids.Add(l);
        }
        Unify(ids, pos, tol);
        var edges = new Dictionary<(int, int), List<int>>();
        for (int r = 0; r < ids.Count; r++)
        {
            var l = ids[r];
            if (l.Count < 3) continue;
            for (int i = 0; i < l.Count; i++)
            {
                var key = Key(l[i], l[(i + 1) % l.Count]);
                if (!edges.TryGetValue(key, out var who)) edges[key] = who = new List<int>();
                if (!who.Contains(r)) who.Add(r);
            }
        }
        // Pares de anéis cuja borda comum tem os mesmos vértices em (quase) toda a extensão em que eles se encostam. Bordas
        // que coincidem só na geometria (cada piso com a própria discretização da curva, vértices a milímetros um do outro)
        // dividiam o trecho comum em pedaços alternados – esses pares ficam com o ajuste próprio.
        var clean = CleanPairs(rs, ids, pos, edges);
        // Anel cujo contorno montado não passa nas conferências sai da topologia junto com os pares dele (os vizinhos não
        // podem ficar com a borda comum enquanto ele fica com o ajuste próprio) e tudo é refeito.
        for (int round = 0; round < 4; round++)
        {
            var (rings2, failed) = Assemble(rs, ids, pos, edges, clean, tol, minEdge, centers, snapOffset);
            if (failed.Count == 0) return rings2;
            clean.RemoveWhere(p => failed.Contains(p.Item1) || failed.Contains(p.Item2));
        }
        return res;
    }

    /// <summary>Uma rodada do ajuste topológico com os pares dados (ver <see cref="Topology"/>).</summary>
    private static (List<List<BoundarySegment>?> Rings, HashSet<int> Failed) Assemble(List<List<Vec2>> rs, List<List<int>> ids, List<Vec2> pos0,
        Dictionary<(int, int), List<int>> edges, HashSet<(int, int)> clean, double tol, double minEdge, IReadOnlyList<Vec2>? centers, double snapOffset)
    {
        var res = new List<List<BoundarySegment>?>(ids.Select(_ => (List<BoundarySegment>?)null));
        var failed = new HashSet<int>();
        var pos = pos0.ToList();
        string Sig(int r, int i)
        {
            var l = ids[r];
            return string.Join(",", edges[Key(l[i], l[(i + 1) % l.Count])].Where(x => x != r && clean.Contains(Key(r, x))).OrderBy(x => x));
        }
        // Trechos de cada anel (entre os vértices onde muda o vizinho) ou o anel inteiro comum.
        var plan = new List<(int Ring, List<(string Key, List<int> Canon, bool Rev)>? Chains, int Owner)>();
        for (int r = 0; r < ids.Count; r++)
        {
            var l = ids[r];
            var n = l.Count;
            if (n < 3) continue;
            var sig = Enumerable.Range(0, n).Select(i => Sig(r, i)).ToList();
            if (sig.All(x => x.Length == 0)) continue;
            var breaks = Enumerable.Range(0, n).Where(i => sig[(i - 1 + n) % n] != sig[i]).ToList();
            if (breaks.Count == 0)
            {
                // O anel inteiro é comum (ilha dentro do furo de outro piso): ajustado uma vez, no anel de menor índice.
                plan.Add((r, null, Math.Min(r, sig[0].Split(',').Select(int.Parse).Min())));
                continue;
            }
            var chains = new List<(string, List<int>, bool)>();
            for (int b = 0; b < breaks.Count; b++)
            {
                var (from, to) = (breaks[b], breaks[(b + 1) % breaks.Count]);
                var seq = new List<int>();
                for (int i = from; ; i = (i + 1) % n)
                {
                    seq.Add(l[i]);
                    if (seq.Count > 1 && i == to) break;
                }
                var rev = seq[0] > seq[^1] || seq[0] == seq[^1] && seq.Count > 2 && seq[1] > seq[^2];
                var canon = rev ? Enumerable.Reverse(seq).ToList() : seq;
                chains.Add((string.Join(",", canon), canon, rev));
            }
            plan.Add((r, chains, -1));
        }
        var unique = plan.Where(p => p.Chains != null).SelectMany(p => p.Chains!).GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First().Canon);
        var fits = new Dictionary<string, ChainFit>();
        foreach (var (key, canon) in unique) fits[key] = FitChain(canon.Select(k => pos[k]).ToList(), tol, minEdge, centers, snapOffset);
        // Ponta de trecho que cai dentro de um arco exato (corte sobre a corda, até a flecha dela) vai para a circunferência
        // – uma vez, para todos os trechos e anéis que a usam; antes o arco que passava por ela saía de outra circunferência.
        var ends = new Dictionary<int, List<(Vec2 C, double R)>>();
        foreach (var (key, canon) in unique)
        {
            var fc = fits[key];
            if (fc.StartCircle is { } c0) (ends.TryGetValue(canon[0], out var l0) ? l0 : ends[canon[0]] = new()).Add(c0);
            if (fc.EndCircle is { } c1) (ends.TryGetValue(canon[^1], out var l1) ? l1 : ends[canon[^1]] = new()).Add(c1);
        }
        var moved = new HashSet<int>();
        foreach (var (v, cs) in ends)
        {
            var c = cs[0];
            if (cs.Any(x => x.C.DistanceTo(c.C) > 1e-9 || Math.Abs(x.R - c.R) > 1e-9)) continue;
            var q = Project(c, pos[v]);
            var d = q.DistanceTo(pos[v]);
            if (d < 1e-7 || d > Inward(c.R, tol)) continue;
            pos[v] = q;
            moved.Add(v);
        }
        if (moved.Count > 0)
            foreach (var (key, canon) in unique)
                if (moved.Contains(canon[0]) || moved.Contains(canon[^1]))
                    fits[key] = FitChain(canon.Select(k => pos[k]).ToList(), tol, minEdge, centers, snapOffset);

        var loops = new Dictionary<int, List<BoundarySegment>?>();
        foreach (var (r, chains, owner) in plan)
        {
            List<BoundarySegment>? ring;
            if (chains == null)
            {
                if (!loops.TryGetValue(owner, out var lf))
                    loops[owner] = lf = FitRing(ids[owner].Select(k => pos[k]).ToList(), tol, minEdge, centers, snapOffset);
                if (lf == null) { failed.Add(r); continue; }
                var o = ids[owner];
                var l = ids[r];
                var at = o.IndexOf(l[0]);
                ring = at >= 0 && o[(at + 1) % o.Count] == l[1] ? lf.ToList() : Reversed(lf);
            }
            else
            {
                ring = new List<BoundarySegment>();
                foreach (var (key, _, rev) in chains)
                {
                    if (fits[key].Segments is not { } fit) { ring = null; break; }
                    ring.AddRange(rev ? Reversed(fit) : fit);
                }
            }
            // Conferências: trechos de tamanho aceito pelo Revit, sem autointerseção e perto do contorno original nos dois
            // sentidos (o arco exato fica até a flecha das cordas para fora delas).
            var ok = ring != null && (ring.Count > 2 || ring.Any(x => x.IsArc)) && ring.All(x => x.Start.DistanceTo(x.End) > minEdge)
                && IsSimple(new[] { ring }, 1e-5)
                && Math.Max(OneWay(Significant(rs[r], tol), Sample(ring, 0.0002)), OneWay(Sample(ring, 0.0002), rs[r])) <= Math.Max(2 * tol, 0.035);
            if (!ok) { failed.Add(r); continue; }
            res[r] = ring;
        }
        return (res, failed);
    }

    /// <summary>Trecho ajustado e as circunferências exatas (centro do traçado) do primeiro e do último arco, se houver.</summary>
    private readonly record struct ChainFit(List<BoundarySegment>? Segments, (Vec2 C, double R)? StartCircle, (Vec2 C, double R)? EndCircle);

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>
    /// Trechos da borda comum com vértices próprios em cada anel (cada piso com a sua discretização da curva, ou um dente de
    /// recorte, a menos da tolerância do ajuste um do outro) entre dois vértices comuns: o anel de índice maior passa a usar os vértices do outro. Sem isso o
    /// trecho comum ficava partido nesses pedaços e cada parte era ajustada separada, com arcos piores que os do anel inteiro.
    /// </summary>
    private static void Unify(List<List<int>> ids, List<Vec2> pos, double touch)
    {
        (Vec2 Min, Vec2 Max) Box(List<int> l) => (new Vec2(l.Min(k => pos[k].X), l.Min(k => pos[k].Y)), new Vec2(l.Max(k => pos[k].X), l.Max(k => pos[k].Y)));
        double ToRing(Vec2 v, List<int> l)
        {
            var best = double.MaxValue;
            for (int i = 0; i < l.Count; i++) best = Math.Min(best, SegDist(v, pos[l[i]], pos[l[(i + 1) % l.Count]]));
            return best;
        }
        for (int a = 0; a < ids.Count; a++)
        {
            if (ids[a].Count < 3) continue;
            var ba = Box(ids[a]);
            for (int b = a + 1; b < ids.Count; b++)
            {
                var B = ids[b];
                if (B.Count < 3) continue;
                var bb = Box(B);
                if (bb.Min.X > ba.Max.X + touch || bb.Max.X < ba.Min.X - touch || bb.Min.Y > ba.Max.Y + touch || bb.Max.Y < ba.Min.Y - touch) continue;
                var A = ids[a];
                var inB = new Dictionary<int, int>();
                for (int j = 0; j < B.Count; j++) inB.TryAdd(B[j], j);
                var n = A.Count;
                for (int s0 = 0; s0 < n; s0++)
                {
                    if (!inB.TryGetValue(A[s0], out var js)) continue;
                    // Corrida de A a partir de A[s0]: arestas em contato com B que não são comuns, até o próximo vértice comum.
                    var run = new List<int> { A[s0] };
                    var e = -1;
                    for (int k = 1; k < Math.Min(n, 60); k++)
                    {
                        var v = A[(s0 + k) % n];
                        var mid = (pos[run[^1]] + pos[v]) * 0.5;
                        if (ToRing(mid, B) > touch) break;
                        run.Add(v);
                        if (inB.ContainsKey(v)) { e = k; break; }
                    }
                    if (e < 1) continue;
                    var je = inB[run[^1]];
                    // Caminho de B entre os dois vértices comuns (no sentido em que ele encosta na corrida de A).
                    List<int>? Path(int from, int to)
                    {
                        var p = new List<int>();
                        for (int j = from; p.Count <= 60; j = (j + 1) % B.Count)
                        {
                            p.Add(j);
                            if (j == to) return p;
                        }
                        return null;
                    }
                    var runPts = run.Select(k => pos[k]).ToList();
                    bool Along(List<int> p) => p.All(j => runPts.Zip(runPts.Skip(1)).Min(q => SegDist(pos[B[j]], q.First, q.Second)) <= touch);
                    List<int>? path = null;
                    bool reversed = false;
                    if (Path(je, js) is { } p1 && Along(p1)) { path = p1; reversed = true; }
                    else if (Path(js, je) is { } p2 && Along(p2)) path = p2;
                    // Nada a trocar: a mesma aresta nos dois (já é comum).
                    if (path == null || path.Count < 2 || path.Count == 2 && run.Count == 2) continue;
                    // Troca os vértices internos do caminho de B pelos internos da corrida de A (no sentido de B).
                    var inner = run.Skip(1).Take(run.Count - 2).ToList();
                    if (reversed) inner.Reverse();
                    var keep = new List<int>();
                    var drop = new HashSet<int>(path.Skip(1).Take(path.Count - 2));
                    for (int j = 0; j < B.Count; j++)
                    {
                        if (drop.Contains(j)) continue;
                        keep.Add(B[j]);
                        if (j == path[0]) keep.AddRange(inner);
                    }
                    if (keep.Distinct().Count() != keep.Count) continue;
                    ids[b] = B = keep;
                    inB.Clear();
                    for (int j = 0; j < B.Count; j++) inB.TryAdd(B[j], j);
                }
            }
        }
    }

    /// <summary>
    /// Pares (a, b) em que o trecho com vértices comuns cobre ≥ 95 % do contato geométrico (arestas de um a até 0,5 mm do
    /// contorno do outro), olhando dos dois lados.
    /// </summary>
    private static HashSet<(int, int)> CleanPairs(List<List<Vec2>> rs, List<List<int>> ids, List<Vec2> pos, Dictionary<(int, int), List<int>> edges)
    {
        const double touch = 5e-4;
        const double cell = 2.0;
        var index = new Dictionary<(long, long), List<(int R, Vec2 A, Vec2 B)>>();
        for (int r = 0; r < ids.Count; r++)
        {
            var l = ids[r];
            if (l.Count < 3) continue;
            for (int i = 0; i < l.Count; i++)
            {
                var (a, b) = (pos[l[i]], pos[l[(i + 1) % l.Count]]);
                var (x0, x1) = ((long)Math.Floor((Math.Min(a.X, b.X) - touch) / cell), (long)Math.Floor((Math.Max(a.X, b.X) + touch) / cell));
                var (y0, y1) = ((long)Math.Floor((Math.Min(a.Y, b.Y) - touch) / cell), (long)Math.Floor((Math.Max(a.Y, b.Y) + touch) / cell));
                for (var cx = x0; cx <= x1; cx++)
                    for (var cy = y0; cy <= y1; cy++)
                    {
                        if (!index.TryGetValue((cx, cy), out var c)) index[(cx, cy)] = c = new List<(int, Vec2, Vec2)>();
                        c.Add((r, a, b));
                    }
            }
        }
        var shared = new Dictionary<(int, int), double>();
        var contact = new Dictionary<(int, int), double>();
        for (int r = 0; r < ids.Count; r++)
        {
            var l = ids[r];
            if (l.Count < 3) continue;
            for (int i = 0; i < l.Count; i++)
            {
                var (a, b) = (pos[l[i]], pos[l[(i + 1) % l.Count]]);
                var len = a.DistanceTo(b);
                var who = edges[Key(l[i], l[(i + 1) % l.Count])];
                foreach (var o in who.Where(x => x != r)) shared[(r, o)] = shared.GetValueOrDefault((r, o)) + len;
                var m = (a + b) * 0.5;
                if (!index.TryGetValue(((long)Math.Floor(m.X / cell), (long)Math.Floor(m.Y / cell)), out var near)) continue;
                foreach (var o in near.Where(e => e.R != r && !who.Contains(e.R) && SegDist(m, e.A, e.B) <= touch).Select(e => e.R).Distinct())
                    contact[(r, o)] = contact.GetValueOrDefault((r, o)) + len;
            }
        }
        var res = new HashSet<(int, int)>();
        foreach (var ((r, o), sh) in shared)
        {
            if (r > o) continue;
            var ok = sh >= CleanShare * (sh + contact.GetValueOrDefault((r, o))) && shared.GetValueOrDefault((o, r)) is var sh2
                && sh2 >= CleanShare * (sh2 + contact.GetValueOrDefault((o, r)));
            if (ok) res.Add((r, o));
        }
        return res;
    }

    private static double SegDist(Vec2 v, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var l2 = ab.Dot(ab);
        var t = l2 < 1e-18 ? 0 : Math.Clamp((v - a).Dot(ab) / l2, 0, 1);
        return v.DistanceTo(a + ab * t);
    }

    private static List<BoundarySegment> Reversed(IReadOnlyList<BoundarySegment> segs) =>
        segs.Reverse().Select(s => new BoundarySegment(s.End, s.Start, s.Mid)).ToList();

    /// <summary>
    /// Junções em T: o vértice de um anel que cai sobre a aresta de outro (a até a grade das operações booleanas) entra nessa
    /// aresta – a borda comum fica com a mesma sequência de vértices nos dois anéis.
    /// </summary>
    private static void Imprint(List<List<Vec2>> rs)
    {
        const double cell = 2.0;
        var index = new Dictionary<(long, long), List<(int R, Vec2 V)>>();
        for (int r = 0; r < rs.Count; r++)
            foreach (var v in rs[r])
            {
                var key = ((long)Math.Floor(v.X / cell), (long)Math.Floor(v.Y / cell));
                if (!index.TryGetValue(key, out var l)) index[key] = l = new List<(int, Vec2)>();
                l.Add((r, v));
            }
        for (int r = 0; r < rs.Count; r++)
        {
            var ring = rs[r];
            if (ring.Count < 3) continue;
            var outRing = new List<Vec2>(ring.Count);
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                outRing.Add(a);
                var ab = b - a;
                var len = ab.Length;
                if (len < 2 * SameVertex) continue;
                var u = ab / len;
                var found = new List<(double T, Vec2 V)>();
                var (x0, x1) = ((long)Math.Floor((Math.Min(a.X, b.X) - SameVertex) / cell), (long)Math.Floor((Math.Max(a.X, b.X) + SameVertex) / cell));
                var (y0, y1) = ((long)Math.Floor((Math.Min(a.Y, b.Y) - SameVertex) / cell), (long)Math.Floor((Math.Max(a.Y, b.Y) + SameVertex) / cell));
                for (var cx = x0; cx <= x1; cx++)
                    for (var cy = y0; cy <= y1; cy++)
                    {
                        if (!index.TryGetValue((cx, cy), out var l)) continue;
                        foreach (var (or, v) in l)
                        {
                            if (or == r) continue;
                            var t = (v - a).Dot(u);
                            if (t <= SameVertex || t >= len - SameVertex) continue;
                            if (Math.Abs((v - a).Cross(u)) > SameVertex) continue;
                            if (found.All(f => f.V.DistanceTo(v) > SameVertex)) found.Add((t, v));
                        }
                    }
                foreach (var f in found.OrderBy(f => f.T)) outRing.Add(f.V);
            }
            rs[r] = outRing;
        }
    }

    /// <summary>
    /// Ajuste de um trecho aberto (pontas fixas) em retas e arcos – o mesmo do <see cref="Fit"/>, sem a volta do anel: os
    /// trechos de <see cref="Spans"/>, os centros exatos do traçado, as junções nos encontros e a tangência das curvas às
    /// retas vizinhas; depois as retas quase alinhadas aos eixos ficam exatas sem mexer nas pontas. Nulo se o resultado tem
    /// trecho menor que a aresta mínima.
    /// </summary>
    private static ChainFit FitChain(IReadOnlyList<Vec2> chain, double tolerance, double minEdge, IReadOnlyList<Vec2>? centers,
        double snapOffset)
    {
        (Vec2 C, double R)? first = null, last = null;
        var dd = Math.Max(1e-6, minEdge * 0.5);
        var pts = new List<Vec2> { chain[0] };
        for (int i = 1; i < chain.Count - 1; i++)
            if (pts[^1].DistanceTo(chain[i]) > dd && chain[i].DistanceTo(chain[^1]) > dd) pts.Add(chain[i]);
        pts.Add(chain[^1]);
        if (pts[0].DistanceTo(pts[^1]) <= minEdge && pts.Count < 3) return new ChainFit(null, null, null);
        pts = SignificantOpen(pts, tolerance);
        List<BoundarySegment> res;
        if (pts.Count < 3) res = new List<BoundarySegment> { new(pts[0], pts[^1]) };
        else
        {
            var p = pts.ToArray();
            var n = p.Length - 1;
            var spans = Spans(p, n, tolerance, minEdge, centers);
            var circles = spans.Select(sp => sp.Arc ? ArcCircle(p, sp.A, sp.B, centers, tolerance) : null).ToList();
            var hinted = circles.Where(c => c != null && centers != null && centers.Any(h => h.DistanceTo(c.Value.Center) < 1e-9)).Select(c => c!.Value.Center).ToList();
            bool Exact((Vec2 Center, double Radius)? c) => c is { } x && hinted.Any(h => h.DistanceTo(x.Center) < 1e-9);
            for (int i = 0; i + 1 < spans.Count; i++)
            {
                var k = i + 1;
                if (!spans[k].Arc && k + 1 < spans.Count && p[spans[k].A].DistanceTo(p[spans[k].B]) < 0.05) k++;
                if (circles[i] is not { } c1 || circles[k] is not { } c2 || !Exact(c1) || c1.Center.DistanceTo(c2.Center) > 1e-9) continue;
                if (c1.Radius == c2.Radius || Math.Abs(c1.Radius - c2.Radius) > tolerance) continue;
                var ds = new List<double>();
                foreach (var sp in new[] { spans[i], spans[k] })
                    for (int q = sp.B - sp.A >= 2 ? sp.A + 1 : sp.A; q <= (sp.B - sp.A >= 2 ? sp.B - 1 : sp.B); q++) ds.Add(p[q].DistanceTo(c1.Center));
                var r = EnvelopeRadius(ds);
                if (ds.All(d => d <= r + tolerance && d >= r - Inward(r, tolerance))) circles[i] = circles[k] = (c1.Center, r);
            }
            var m = spans.Count;
            var joints = new Vec2[m + 1];
            joints[0] = p[0];
            joints[m] = p[n];
            for (int i = 1; i < m; i++)
            {
                var v = p[spans[i].A];
                var prev = circles[i - 1];
                var cur = circles[i];
                var lineBefore = p[spans[i - 1].A];
                var lineAfter = p[spans[i].B];
                joints[i] = (prev, cur) switch
                {
                    (null, { } c) => (Exact(cur) ? LineMeet(c, lineBefore, v) : null) ?? Project(c, v),
                    ({ } c, null) => (Exact(prev) ? LineMeet(c, lineAfter, v) : null) ?? Project(c, v),
                    ({ } c1, { } c2) => Meet(c1, c2, v) is { } mt && mt.DistanceTo(v) <= tolerance ? mt
                        : Exact(cur) ? Project(c2, v) : Exact(prev) ? Project(c1, v) : v,
                    _ => v,
                };
                var ec = Exact(cur) ? cur : Exact(prev) ? prev : null;
                var lim = ec is { } e ? Inward(e.Radius, tolerance) : tolerance;
                var onLine = ec is { } e2 && (prev == null || cur == null) && v.DistanceTo(e2.Center) is var dv
                    && dv <= e2.Radius + tolerance && dv >= e2.Radius - lim;
                if (joints[i].DistanceTo(v) > (onLine ? 4 * lim : lim))
                    joints[i] = ec is { } e3 && Project(e3, v) is var pj && pj.DistanceTo(v) <= lim ? pj : v;
            }
            for (int i = 0; i < m; i++)
            {
                if (circles[i] != null) continue;
                var (j0, j1) = (joints[i], joints[i + 1]);
                var seg = new BoundarySegment(j0, j1);
                var ok = true;
                for (int k = spans[i].A; k <= spans[i].B && ok; k++)
                    ok = (k == spans[i].A || k == spans[i].B) && j0.DistanceTo(j1) > 1e-9
                        ? Math.Abs((p[k] - j0).Cross((j1 - j0).Normalized())) <= tolerance
                        : Distance(seg, p[k]) <= tolerance;
                if (!ok)
                {
                    if (i > 0) joints[i] = p[spans[i].A];
                    if (i + 1 < m) joints[i + 1] = p[spans[i].B];
                }
            }
            if (circles[0] is { } cf && Exact(cf)) first = cf;
            if (circles[m - 1] is { } cl && Exact(cl)) last = cl;
            res = new List<BoundarySegment>();
            for (int i = 0; i < m; i++)
            {
                var (a, b, _) = spans[i];
                var s0 = joints[i];
                var s1 = joints[i + 1];
                if (circles[i] is not { } cr) { res.Add(new BoundarySegment(s0, s1)); continue; }
                var (c, rr) = cr;
                var sweep = Sweep(c, s0, p[(a + b) / 2], s1);
                var a0 = Math.Atan2(s0.Y - c.Y, s0.X - c.X);
                res.Add(new BoundarySegment(s0, s1, c + Vec2.FromAngle(a0 + sweep / 2) * rr));
            }
            res = TangentOpen(AbsorbOpen(MergeOpen(res, minEdge), tolerance), pts, tolerance, minEdge, hinted);
        }
        res = AxisSnap.SnapChain(res, Math.Max(AxisSnap.MaxOffset, Math.Min(snapOffset, Math.Max(tolerance, AxisSnap.MaxOffset))));
        return new ChainFit(res.Any(s => s.Start.DistanceTo(s.End) <= minEdge) ? null : res, first, last);
    }

    /// <summary><see cref="Significant"/> num trecho aberto: as pontas sempre ficam.</summary>
    private static List<Vec2> SignificantOpen(List<Vec2> pts, double tolerance)
    {
        var n = pts.Count;
        if (n <= 4) return pts;
        var lim = Math.Min(0.001, 0.2 * tolerance);
        var turn = new double[n];
        for (int i = 1; i < n - 1; i++) turn[i] = Turn(pts[i - 1], pts[i], pts[i + 1]);
        var local = new double[n];
        for (int i = 1; i < n - 1; i++)
            for (int k = -6; k <= 6; k++)
                if (k != 0 && i + k > 0 && i + k < n - 1) local[i] = Math.Max(local[i], turn[i + k]);
        var keep = new List<Vec2> { pts[0] };
        for (int i = 1; i < n - 1; i++)
        {
            var a = keep[^1];
            var b = pts[i];
            var c = pts[i + 1];
            var ac = c - a;
            var len = ac.Length;
            var isolated = turn[i] <= 0.35 * local[i] || turn[i] < 1e-6;
            if (isolated && len > 1e-9 && Math.Abs((b - a).Cross(ac)) / len <= lim && (b - a).Dot(ac) > 0 && (c - b).Dot(ac) > 0) continue;
            keep.Add(b);
        }
        keep.Add(pts[^1]);
        return keep;
    }

    /// <summary><see cref="Merge"/> num trecho aberto (sem o fecho; as pontas ficam).</summary>
    private static List<BoundarySegment> MergeOpen(List<BoundarySegment> segs, double minEdge)
    {
        var res = new List<BoundarySegment>();
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            if (res.Count > 0 && !s.IsArc && !res[^1].IsArc && Collinear(res[^1].Start, res[^1].End, s.End))
            {
                res[^1] = new BoundarySegment(res[^1].Start, s.End);
                continue;
            }
            if (res.Count > 0 && s.Start.DistanceTo(s.End) < minEdge && !s.IsArc)
            {
                var last = res[^1];
                if (!last.IsArc || i + 1 >= segs.Count) { res[^1] = Retarget(last, last.Start, s.End); continue; }
                var next = segs[i + 1];
                var j = Joint(last, next, s.End);
                res[^1] = Retarget(last, last.Start, j);
                segs[i + 1] = Retarget(next, j, next.End);
                continue;
            }
            res.Add(s);
        }
        return res;
    }

    /// <summary><see cref="Absorb"/> num trecho aberto: só retas entre dois vizinhos.</summary>
    private static List<BoundarySegment> AbsorbOpen(List<BoundarySegment> segs, double tol)
    {
        var changed = true;
        while (changed && segs.Count > 2)
        {
            changed = false;
            for (int i = 1; i + 1 < segs.Count; i++)
            {
                var s = segs[i];
                if (s.IsArc) continue;
                var (ip, inx) = (i - 1, i + 1);
                if (segs[ip].IsArc && CanExtend(segs[ip], segs[ip].Start, s.End, s.End, tol))
                {
                    var j = Joint(segs[ip], segs[inx], s.End);
                    segs[ip] = Retarget(segs[ip], segs[ip].Start, j);
                    segs[inx] = Retarget(segs[inx], j, segs[inx].End);
                    segs.RemoveAt(i);
                    changed = true;
                    break;
                }
                if (segs[inx].IsArc && CanExtend(segs[inx], s.Start, segs[inx].End, s.Start, tol))
                {
                    var j = Joint(segs[ip], segs[inx], s.Start);
                    segs[inx] = Retarget(segs[inx], j, segs[inx].End);
                    segs[ip] = Retarget(segs[ip], segs[ip].Start, j);
                    segs.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
        return segs;
    }

    /// <summary><see cref="Tangent"/> num trecho aberto: só arcos com as duas vizinhas dentro do trecho (as pontas ficam).</summary>
    private static List<BoundarySegment> TangentOpen(List<BoundarySegment> segs, IReadOnlyList<Vec2> pts, double tol, double minEdge, List<Vec2> exact)
    {
        if (segs.Count < 3) return segs;
        // Anel fechado provisório com uma reta de volta; o Tangent só mexe nos trechos vizinhos de cada arco, que ficam todos
        // dentro do trecho (o primeiro e o último nunca são o arco do meio).
        var closed = segs.Append(new BoundarySegment(segs[^1].End, segs[0].Start)).ToList();
        var n = closed.Count;
        var fixedClose = closed[^1];
        var t = Tangent(closed, pts, tol, minEdge, exact);
        // Arcos vizinhos da reta de volta (primeiro e último) não podem ter mudado a ponta.
        if (t[^1] != fixedClose || t[0].Start != segs[0].Start || t[n - 2].End != segs[^1].End) return segs;
        return t.Take(n - 1).ToList();
    }
}
