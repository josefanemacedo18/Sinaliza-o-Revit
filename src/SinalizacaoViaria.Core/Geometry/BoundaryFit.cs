namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Trecho de contorno pronto para o Revit: reta (<see cref="Mid"/> nulo) ou arco de circunferência que passa por
/// <see cref="Start"/>, <see cref="Mid"/> e <see cref="End"/>.
/// </summary>
public readonly record struct BoundarySegment(Vec2 Start, Vec2 End, Vec2? Mid = null)
{
    public bool IsArc => Mid != null;

    /// <summary>Centro e raio do arco (só para arcos).</summary>
    public (Vec2 Center, double Radius) Circle => BoundaryFit.CircleThrough(Start, Mid!.Value, End) ?? ((Start + End) * 0.5, Start.DistanceTo(End) / 2);

    /// <summary>Pontos do trecho (sem o ponto final) com flecha máxima <paramref name="maxChordError"/> nos arcos.</summary>
    public IEnumerable<Vec2> Sample(double maxChordError)
    {
        yield return Start;
        if (Mid is not { } m) yield break;
        var (c, r) = Circle;
        var a0 = Math.Atan2(Start.Y - c.Y, Start.X - c.X);
        var sweep = BoundaryFit.Sweep(c, Start, m, End);
        var n = CurveTools.SegmentsForArc(r, sweep, maxChordError);
        for (int i = 1; i < n; i++) yield return c + Vec2.FromAngle(a0 + sweep * i / n) * r;
    }

    public double Length
    {
        get
        {
            if (Mid is not { } m) return Start.DistanceTo(End);
            var (c, r) = Circle;
            return Math.Abs(BoundaryFit.Sweep(c, Start, m, End)) * r;
        }
    }
}

/// <summary>
/// Converte o contorno discretizado (polígono com muitos vértices) em trechos inteiros: cada reta vira UMA linha e cada
/// curva vira arcos de circunferência, com desvio máximo dado em relação ao contorno original. É o que o usuário vê ao
/// editar o esboço do piso no Revit – linhas e arcos inteiros, sem dezenas de "tracinhos".
/// </summary>
public static class BoundaryFit
{
    /// <summary>Desvio máximo padrão entre o contorno ajustado e o original (m).</summary>
    public const double DefaultTolerance = 0.005;

    /// <summary>Raio acima do qual um arco é tratado como reta (m).</summary>
    public const double MaxRadius = 5000;

    /// <summary>Varredura máxima de um arco (rad) – círculos completos viram 2 ou 3 arcos.</summary>
    public const double MaxSweep = Math.PI;

    /// <summary>Giro máximo entre duas cordas seguidas de uma curva discretizada (rad).</summary>
    public const double MaxChordTurn = 12 * Math.PI / 180;

    /// <summary>Flecha máxima de cada corda antiga sob o arco, em múltiplos da tolerância.</summary>
    public const double ChordSagittaFactor = 5;

    /// <summary>
    /// Ajusta um anel fechado (sem repetir o primeiro ponto no fim). Cada vértice fica a no máximo
    /// <paramref name="tolerance"/> do contorno ajustado. Trechos menores que <paramref name="minEdge"/> não são
    /// criados: o Revit recusa curvas abaixo da tolerância de curva curta.
    /// </summary>
    public static List<BoundarySegment> Fit(IReadOnlyList<Vec2> ring, double tolerance = DefaultTolerance, double minEdge = 0.002,
        IReadOnlyList<Vec2>? centers = null)
    {
        var pts = Dedupe(ring, Math.Max(1e-6, minEdge * 0.5));
        if (pts.Count < 3 || tolerance <= 0) return Lines(pts);
        pts = Significant(pts, tolerance);
        var n = pts.Count;
        if (n < 3) return Lines(pts);
        // Começa no canto mais vivo: assim nenhuma reta ou curva fica partida no ponto de partida do anel.
        var start = 0;
        var best = -1.0;
        for (int i = 0; i < n; i++)
        {
            var t = Turn(pts[(i - 1 + n) % n], pts[i], pts[(i + 1) % n]);
            if (t > best) { best = t; start = i; }
        }
        var p = new Vec2[n + 1];
        for (int i = 0; i <= n; i++) p[i] = pts[(start + i) % n];
        var spans = Spans(p, n, tolerance, minEdge, centers);
        // Anel sem cantos (só curvas e retas tangentes): a partida pode cair no meio de uma curva e partir o arco em dois –
        // recomeça do início do último trecho.
        if (spans.Count >= 2 && spans[0].Arc && spans[^1].Arc)
        {
            var shift = spans[^1].A;
            var q = new Vec2[n + 1];
            for (int i = 0; i <= n; i++) q[i] = p[(shift + i) % n];
            var alt = Spans(q, n, tolerance, minEdge, centers);
            if (alt.Count < spans.Count) { p = q; spans = alt; }
        }
        // Circunferência de cada curva por todos os pontos dela; as junções vão para cima da circunferência (o ponto de corte
        // de um recorte cai sobre a corda, até a flecha dela para dentro da curva).
        var circles = spans.Select(sp => sp.Arc ? ArcCircle(p, sp.A, sp.B, centers, tolerance) : null).ToList();
        var hinted = circles.Where(c => c != null && centers != null && centers.Any(h => h.DistanceTo(c.Value.Center) < 1e-9)).Select(c => c!.Value.Center).ToList();
        bool Exact((Vec2 Center, double Radius)? c) => c is { } x && hinted.Any(h => h.DistanceTo(x.Center) < 1e-9);
        var joints = new Vec2[spans.Count];
        for (int i = 0; i < spans.Count; i++)
        {
            var v = p[spans[i].A];
            var prev = circles[(i - 1 + spans.Count) % spans.Count];
            var cur = circles[i];
            joints[i] = (prev, cur) switch
            {
                (null, { } c) => Project(c, v),
                ({ } c, null) => Project(c, v),
                // Dois arcos: no encontro das circunferências; se elas quase se tangenciam (encontro longe), sobre a do traçado.
                ({ } c1, { } c2) => Meet(c1, c2, v) is { } mt && mt.DistanceTo(v) <= tolerance ? mt
                    : Exact(cur) ? Project(c2, v) : Exact(prev) ? Project(c1, v) : v,
                _ => v,
            };
            if (joints[i].DistanceTo(v) > tolerance) joints[i] = v;
        }
        // Reta com a ponta movida: todos os pontos dela continuam a até a tolerância (senão a junção fica no vértice).
        for (int i = 0; i < spans.Count; i++)
        {
            if (circles[i] != null) continue;
            var seg = new BoundarySegment(joints[i], joints[(i + 1) % spans.Count]);
            var ok = true;
            for (int k = spans[i].A; k <= spans[i].B && ok; k++) ok = Distance(seg, p[k]) <= tolerance;
            if (!ok) { joints[i] = p[spans[i].A]; joints[(i + 1) % spans.Count] = p[spans[i].B % n]; }
        }
        var res = new List<BoundarySegment>();
        for (int i = 0; i < spans.Count; i++)
        {
            var (a, b, arc) = spans[i];
            var s0 = joints[i];
            var s1 = joints[(i + 1) % spans.Count];
            if (circles[i] is not { } cr) { res.Add(new BoundarySegment(s0, s1)); continue; }
            var (c, r) = cr;
            var sweep = Sweep(c, s0, p[(a + b) / 2], s1);
            var a0 = Math.Atan2(s0.Y - c.Y, s0.X - c.X);
            res.Add(new BoundarySegment(s0, s1, c + Vec2.FromAngle(a0 + sweep / 2) * r));
        }
        var merged = Absorb(Merge(res, minEdge), tolerance);
        return Tangent(merged, pts, tolerance, minEdge, hinted);
    }

    /// <summary>
    /// Trechos (índices em <paramref name="p"/>): o maior trecho reto ou em arco a partir de cada ponto; depois a junção
    /// reta→arco volta até o último ponto realmente reto (a reta não fica com os primeiros pontos da curva, que entortariam a
    /// tangente) e arcos seguidos que cabem numa circunferência só viram um.
    /// </summary>
    private static List<(int A, int B, bool Arc)> Spans(Vec2[] p, int n, double tolerance, double minEdge, IReadOnlyList<Vec2>? centers)
    {
        var spans = new List<(int A, int B, bool Arc)>();
        var k = 0;
        while (k < n)
        {
            var lineEnd = Longest(k, n, j => FitsLine(p, k, j, tolerance), 1);
            var arcEnd = k + 2 <= n && FitsArc(p, k, k + 2, tolerance, minEdge, centers) ? Longest(k, n, j => FitsArc(p, k, j, tolerance, minEdge, centers), 2) : -1;
            // Arco na circunferência do traçado em vez de um mais comprido que entorta para pegar pontos de transição.
            if (arcEnd > k && centers is { Count: > 0 } && ArcCircle(p, k, arcEnd, centers, tolerance) is { } ac && !centers.Any(h => h.DistanceTo(ac.Center) < 1e-9)
                && FitsArc(p, k, k + 2, tolerance, minEdge, centers, true))
            {
                var hintEnd = Longest(k, n, j => FitsArc(p, k, j, tolerance, minEdge, centers, true), 2);
                if (hintEnd - k >= 0.6 * (arcEnd - k)) arcEnd = hintEnd;
            }
            if (arcEnd > lineEnd) { spans.Add((k, arcEnd, true)); k = arcEnd; }
            else { spans.Add((k, lineEnd, false)); k = lineEnd; }
        }
        var tight = Math.Max(0.0002, 0.05 * tolerance);
        for (int i = 0; i + 1 < spans.Count; i++)
        {
            var (a, b, arc) = spans[i];
            var nx = spans[i + 1];
            if (!arc && nx.Arc && b - a >= 2)
            {
                // Reta → arco: fim da reta no último ponto alinhado (±0,25 mm) com o começo dela.
                var j = b;
                while (j - 1 > a && !FitsLine(p, a, j, tight)) j--;
                if (j < b && FitsArc(p, j, nx.B, tolerance, minEdge, centers)) { spans[i] = (a, j, false); spans[i + 1] = (j, nx.B, true); }
            }
            else if (arc && !nx.Arc && nx.B - nx.A >= 2)
            {
                // Arco → reta: começo da reta no primeiro ponto alinhado com o fim dela.
                var j = nx.A;
                while (j + 1 < nx.B && !FitsLine(p, j, nx.B, tight)) j++;
                if (j > nx.A && FitsArc(p, a, j, tolerance, minEdge, centers)) { spans[i] = (a, j, true); spans[i + 1] = (j, nx.B, false); }
            }
        }
        // Arco ajustado que contém um trecho na circunferência do traçado (≥ 60 % dos pontos): esse trecho vira o arco exato
        // e as sobras das pontas (transições) ficam como arco ou reta.
        if (centers is { Count: > 0 })
            for (int i = 0; i < spans.Count; i++)
            {
                var (a, b, arc) = spans[i];
                if (!arc || b - a < 3 || ArcCircle(p, a, b, centers, tolerance) is not { } ac || centers.Any(h => h.DistanceTo(ac.Center) < 1e-9)) continue;
                var (ba, bb) = (-1, -1);
                for (int a2 = a; a2 <= a + (b - a) / 2 && b - a >= 4; a2++)
                {
                    if (!FitsArc(p, a2, a2 + 2, tolerance, minEdge, centers, true)) continue;
                    var b2 = Longest(a2, b, j => FitsArc(p, a2, j, tolerance, minEdge, centers, true), 2);
                    if (b2 - a2 > bb - ba) (ba, bb) = (a2, b2);
                }
                if (ba < 0 || bb - ba < 0.6 * (b - a))
                {
                    // Começo (ou fim) reto e o resto na circunferência do traçado: reta + arco exato (o arco ajustado engolia a
                    // reta e a junção ficava sem tangência).
                    var tightL = Math.Max(0.0002, 0.05 * tolerance);
                    var ja = a + 1;
                    while (ja + 1 < b && FitsLine(p, a, ja + 1, tightL)) ja++;
                    var jb = b - 1;
                    while (jb - 1 > a && FitsLine(p, jb - 1, b, tightL)) jb--;
                    if (p[a].DistanceTo(p[ja]) >= 0.2 && b - ja >= 2 && FitsArc(p, ja, b, tolerance, minEdge, centers, true))
                    {
                        spans[i] = (a, ja, false);
                        spans.Insert(i + 1, (ja, b, true));
                        i++;
                    }
                    else if (p[jb].DistanceTo(p[b]) >= 0.2 && jb - a >= 2 && FitsArc(p, a, jb, tolerance, minEdge, centers, true))
                    {
                        spans[i] = (a, jb, true);
                        spans.Insert(i + 1, (jb, b, false));
                        i++;
                    }
                    continue;
                }
                bool Piece(int x, int y, out (int, int, bool) sp)
                {
                    sp = (x, y, false);
                    if (y - x >= 2 && FitsArc(p, x, y, tolerance, minEdge, centers)) { sp = (x, y, true); return true; }
                    return FitsLine(p, x, y, tolerance);
                }
                var parts = new List<(int, int, bool)>();
                var ok = true;
                if (ba > a) { ok &= Piece(a, ba, out var sp); parts.Add(sp); }
                parts.Add((ba, bb, true));
                if (bb < b) { ok &= Piece(bb, b, out var sp); parts.Add(sp); }
                if (!ok) continue;
                spans.RemoveAt(i);
                spans.InsertRange(i, parts);
                i += parts.Count - 1;
            }
        // Uma curva = um arco: arcos seguidos que cabem numa circunferência só (a busca por bissecção às vezes para antes do
        // fim da curva) viram um.
        for (int i = 0; i + 1 < spans.Count; i++)
            if (spans[i].Arc && spans[i + 1].Arc && FitsArc(p, spans[i].A, spans[i + 1].B, tolerance, minEdge, centers))
            {
                spans[i] = (spans[i].A, spans[i + 1].B, true);
                spans.RemoveAt(i + 1);
                i = Math.Max(-1, i - 2);
            }
        return spans;
    }

    /// <summary>
    /// Circunferência de mínimos quadrados (distância geométrica) pelos vértices <c>p[i..j]</c>: o centro e o raio saem de
    /// todos os pontos da curva (Gauss–Newton a partir da circunferência pelas pontas e o meio).
    /// </summary>
    private static (Vec2 Center, double Radius)? CircleFit(Vec2[] p, int i, int j)
    {
        if (CircleThrough(p[i], p[(i + j) / 2], p[j]) is not { } c0) return null;
        if (j - i < 3) return c0;
        // Com pontos de dentro bastantes, as pontas ficam de fora: costumam ser cortes de recorte sobre a corda (até a flecha
        // dela para dentro da curva) ou junções – num arco de poucas dezenas de graus elas puxariam o centro alguns milímetros.
        var (k0, k1) = j - i >= 4 ? (i + 1, j - 1) : (i, j);
        if (k0 != i && CircleThrough(p[k0], p[(k0 + k1) / 2], p[k1]) is { } c1) c0 = c1;
        double cx = c0.Center.X, cy = c0.Center.Y, r = c0.Radius;
        for (int it = 0; it < 20; it++)
        {
            double a11 = 0, a12 = 0, a13 = 0, a22 = 0, a23 = 0, a33 = 0, b1 = 0, b2 = 0, b3 = 0;
            for (int k = k0; k <= k1; k++)
            {
                var dx = p[k].X - cx;
                var dy = p[k].Y - cy;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-12) continue;
                var (jx, jy, jr) = (-dx / d, -dy / d, -1.0);
                var f = d - r;
                a11 += jx * jx; a12 += jx * jy; a13 += jx * jr; a22 += jy * jy; a23 += jy * jr; a33 += jr * jr;
                b1 -= jx * f; b2 -= jy * f; b3 -= jr * f;
            }
            var det = a11 * (a22 * a33 - a23 * a23) - a12 * (a12 * a33 - a23 * a13) + a13 * (a12 * a23 - a22 * a13);
            if (Math.Abs(det) < 1e-18) break;
            var x1 = (b1 * (a22 * a33 - a23 * a23) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a23 - a22 * b3)) / det;
            var x2 = (a11 * (b2 * a33 - a23 * b3) - b1 * (a12 * a33 - a23 * a13) + a13 * (a12 * b3 - b2 * a13)) / det;
            var x3 = (a11 * (a22 * b3 - b2 * a23) - a12 * (a12 * b3 - b2 * a13) + b1 * (a12 * a23 - a22 * a13)) / det;
            cx += x1; cy += x2; r += x3;
            if (!double.IsFinite(r) || r <= 0 || r > 10 * MaxRadius) return c0;
            if (Math.Abs(x1) + Math.Abs(x2) + Math.Abs(x3) < 1e-11) break;
        }
        return (new Vec2(cx, cy), r);
    }

    /// <summary>
    /// Circunferência do trecho: a de um centro exato do traçado (curva da esquina, pelo eixo) quando todos os pontos ficam a
    /// até a tolerância de um raio em volta dele e o traçado gira o mesmo que o arco; senão, a de mínimos quadrados. As
    /// faixas concêntricas (meio-fio, sarjeta, calçada) saem com o mesmo centro, mesmo em trechos curtos ou serrilhados.
    /// </summary>
    private static (Vec2 Center, double Radius)? ArcCircle(Vec2[] p, int i, int j, IReadOnlyList<Vec2>? centers, double tol, bool hintOnly = false)
    {
        var cf = hintOnly ? null : CircleFit(p, i, j);
        if (centers is not { Count: > 0 } || j - i < 2) return cf;
        // Giro do traçado de ponta a ponta (direção da primeira corda → última corda).
        var d0 = p[i + 1] - p[i];
        var d1 = p[j] - p[j - 1];
        var turn = Math.Atan2(d0.Cross(d1), d0.Dot(d1));
        (Vec2, double)? best = null;
        var bestDev = double.MaxValue;
        foreach (var hc in centers)
        {
            // Raio que minimiza o maior desvio dos pontos de dentro (as pontas costumam ser cortes sobre a corda); as pontas
            // também ficam na tolerância.
            var (k0, k1) = j - i >= 4 ? (i + 1, j - 1) : (i, j);
            double lo = double.MaxValue, hi = 0;
            for (int k = k0; k <= k1; k++) { var dk = p[k].DistanceTo(hc); lo = Math.Min(lo, dk); hi = Math.Max(hi, dk); }
            var rr = (lo + hi) / 2;
            var dev = Math.Max(Math.Abs(p[i].DistanceTo(hc) - rr), Math.Abs(p[j].DistanceTo(hc) - rr));
            dev = Math.Max(dev, (hi - lo) / 2);
            if (rr > MaxRadius || dev > tol) continue;
            // O arco em volta do centro do traçado gira o mesmo que o traçado (uma reta não "cabe" num círculo distante).
            // Giro esperado entre a primeira e a última corda: o ângulo, em volta do centro, entre os meios delas.
            var sweep = Sweep(hc, p[i], p[(i + j) / 2], p[j]);
            var inner = Math.Abs(Math.Atan2((p[i + 1] - hc).Cross(p[j - 1] - hc), (p[i + 1] - hc).Dot(p[j - 1] - hc)));
            var want = (Math.Abs(sweep) + inner) / 2;
            if (Math.Sign(turn) != Math.Sign(sweep) || Math.Abs(Math.Abs(turn) - want) > Math.Max(3 * Math.PI / 180, 0.35 * want)) continue;
            if (dev < bestDev) { bestDev = dev; best = (hc, rr); }
        }
        return best ?? cf;
    }

    /// <summary>Ponto da circunferência mais perto de <paramref name="v"/>.</summary>
    private static Vec2 Project((Vec2 Center, double Radius) c, Vec2 v)
    {
        var d = v - c.Center;
        return d.Length < 1e-12 ? v : c.Center + d.Normalized() * c.Radius;
    }

    /// <summary>Encontro das duas circunferências mais perto de <paramref name="v"/> (nulo se não se cruzam).</summary>
    private static Vec2? Meet((Vec2 Center, double Radius) a, (Vec2 Center, double Radius) b, Vec2 v)
    {
        var d = b.Center - a.Center;
        var L = d.Length;
        if (L < 1e-9 || L > a.Radius + b.Radius || L < Math.Abs(a.Radius - b.Radius)) return null;
        var x = (L * L + a.Radius * a.Radius - b.Radius * b.Radius) / (2 * L);
        var h = Math.Sqrt(Math.Max(0, a.Radius * a.Radius - x * x));
        var u = d / L;
        var m = a.Center + u * x;
        var p1 = m + u.PerpLeft * h;
        var p2 = m - u.PerpLeft * h;
        return p1.DistanceTo(v) <= p2.DistanceTo(v) ? p1 : p2;
    }

    /// <summary>
    /// Arcos tangentes às retas vizinhas onde o traçado já é tangente (giro ≤ 3° na junção): entre duas retas, o arco vira a
    /// concordância exata (raio de mínimos quadrados, pontos de tangência sobre as retas); com uma reta só, o arco fica
    /// tangente a ela na ponta. Só vale se todos os vértices originais continuarem a até <paramref name="tol"/> do contorno.
    /// </summary>
    private static List<BoundarySegment> Tangent(List<BoundarySegment> segs, IReadOnlyList<Vec2> pts, double tol, double minEdge, List<Vec2> exact)
    {
        const double snap = 3 * Math.PI / 180;
        var n = segs.Count;
        if (n < 3) return segs;
        for (int i = 0; i < n; i++)
        {
            var a = segs[i];
            if (!a.IsArc) continue;
            var ip = (i - 1 + n) % n;
            var inx = (i + 1) % n;
            var P = segs[ip];
            var Q = segs[inx];
            var (c0, r0) = a.Circle;
            // Arco com o centro do traçado: já é o certo.
            if (exact.Any(h => h.DistanceTo(c0) < 1e-6)) continue;
            var sweep = Sweep(c0, a.Start, a.Mid!.Value, a.End);
            // Só retas de fato tangentes à curva: giro pequeno na junção e a reta (prolongada) a um raio do centro (±0,5 mm).
            var near = Math.Max(0.0005, 0.1 * tol);
            bool Touches(BoundarySegment l) => Math.Abs(Math.Abs((c0 - l.Start).Cross((l.End - l.Start).Normalized())) - r0) <= near;
            var tP = !P.IsArc && Angle(Dir(P, false), Dir(a, true)) is var gp && gp > 1e-9 && gp < snap && Touches(P);
            var tQ = !Q.IsArc && Angle(Dir(a, false), Dir(Q, true)) is var gq && gq > 1e-9 && gq < snap && Touches(Q);
            if (!tP && !tQ) continue;
            // Vértices originais deste trecho (mais perto do trio reta–arco–reta que de qualquer outro trecho).
            var mine = new List<Vec2>();
            foreach (var v in pts)
            {
                var d = Math.Min(Distance(P, v), Math.Min(Distance(a, v), Distance(Q, v)));
                if (d > tol * 1.01) continue;
                var other = false;
                for (int k = 0; k < n && !other; k++)
                    if (k != i && k != ip && k != inx && Distance(segs[k], v) < d) other = true;
                if (!other) mine.Add(v);
            }
            var arcPts = mine.Where(v => Distance(a, v) <= Math.Min(Distance(P, v), Distance(Q, v))).ToList();
            var tries = new List<(BoundarySegment P, BoundarySegment A, BoundarySegment Q)>();
            if (tP && tQ && Fillet(P, Q, sweep, r0, arcPts, minEdge) is { } f) tries.Add(f);
            if (tP && OneSided(a, Dir(P, false), sweep, true) is { } s1) tries.Add((P, s1, Q));
            if (tQ && OneSided(a, Dir(Q, true), sweep, false) is { } s2) tries.Add((P, s2, Q));
            foreach (var (nP, nA, nQ) in tries)
            {
                var (nc, nr) = nA.Circle;
                var nsw = Sweep(nc, nA.Start, nA.Mid!.Value, nA.End);
                if (Math.Sign(nsw) != Math.Sign(sweep) || Math.Abs(nsw) > MaxSweep + 1e-9 || nr > MaxRadius) continue;
                if (nP.Start.DistanceTo(nP.End) < minEdge || nQ.Start.DistanceTo(nQ.End) < minEdge || nA.Start.DistanceTo(nA.End) < minEdge) continue;
                if (mine.Any(v => Math.Min(Distance(nP, v), Math.Min(Distance(nA, v), Distance(nQ, v))) > tol)) continue;
                segs[ip] = nP;
                segs[i] = nA;
                segs[inx] = nQ;
                break;
            }
        }
        return segs;
    }

    /// <summary>Concordância exata entre as retas <paramref name="P"/> e <paramref name="Q"/> com o raio que melhor passa pelos pontos.</summary>
    private static (BoundarySegment P, BoundarySegment A, BoundarySegment Q)? Fillet(BoundarySegment P, BoundarySegment Q, double sweep, double r0,
        List<Vec2> arcPts, double minEdge)
    {
        var dP = Dir(P, false);
        var dQ = Dir(Q, true);
        var cr = dP.Cross(dQ);
        if (Math.Abs(cr) < 1e-9) return null;
        var delta = Math.Atan2(cr, dP.Dot(dQ));
        if (Math.Sign(delta) != Math.Sign(sweep) || Math.Abs(Math.Abs(delta) - Math.Abs(sweep)) > 6 * Math.PI / 180) return null;
        var V = P.End + dP * ((Q.Start - P.End).Cross(dQ) / cr);
        var tanH = Math.Tan(Math.Abs(delta) / 2);
        var side = delta > 0 ? dP.PerpLeft : dP.PerpLeft * -1;
        var w = side - dP * tanH;                                 // centro = V + r·w
        var r = r0;
        for (int it = 0; it < 12 && arcPts.Count > 0; it++)
        {
            double num = 0, den = 0;
            var c = V + w * r;
            foreach (var q in arcPts)
            {
                var d = q - c;
                var dl = d.Length;
                if (dl < 1e-12) continue;
                var df = -d.Dot(w) / dl - 1;
                num += (dl - r) * df;
                den += df * df;
            }
            if (den < 1e-18) break;
            var step = num / den;
            r -= step;
            if (r <= 0) return null;
            if (Math.Abs(step) < 1e-11) break;
        }
        var t1 = V - dP * (r * tanH);
        var t2 = V + dQ * (r * tanH);
        if ((t1 - P.Start).Dot(dP) < minEdge || (Q.End - t2).Dot(dQ) < minEdge) return null;
        var cc = V + w * r;
        var a0 = Math.Atan2(t1.Y - cc.Y, t1.X - cc.X);
        var arc = new BoundarySegment(t1, t2, cc + Vec2.FromAngle(a0 + delta / 2) * r);
        return (new BoundarySegment(P.Start, t1), arc, new BoundarySegment(t2, Q.End));
    }

    /// <summary>Arco pelas mesmas pontas, tangente à direção <paramref name="d"/> na ponta inicial (ou final).</summary>
    private static BoundarySegment? OneSided(BoundarySegment a, Vec2 d, double sweep, bool atStart)
    {
        var fixedP = atStart ? a.Start : a.End;
        var otherP = atStart ? a.End : a.Start;
        var nrm = sweep > 0 ? d.PerpLeft : d.PerpLeft * -1;
        var ch = otherP - fixedP;
        var den = 2 * ch.Dot(nrm);
        if (den <= 1e-12) return null;
        var r = ch.Dot(ch) / den;
        var c = fixedP + nrm * r;
        var a0 = Math.Atan2(a.Start.Y - c.Y, a.Start.X - c.X);
        var a1 = Math.Atan2(a.End.Y - c.Y, a.End.X - c.X);
        var sw = a1 - a0;
        while (sw <= -Math.PI) sw += 2 * Math.PI;
        while (sw > Math.PI) sw -= 2 * Math.PI;
        if (Math.Sign(sw) != Math.Sign(sweep)) sw += sweep > 0 ? 2 * Math.PI : -2 * Math.PI;
        return new BoundarySegment(a.Start, a.End, c + Vec2.FromAngle(a0 + sw / 2) * r);
    }

    /// <summary>Direção do trecho na ponta inicial ou final (tangente, nos arcos).</summary>
    private static Vec2 Dir(BoundarySegment s, bool atStart)
    {
        if (!s.IsArc) return (s.End - s.Start).Normalized();
        var (c, _) = s.Circle;
        var sweep = Sweep(c, s.Start, s.Mid!.Value, s.End);
        var rad = (atStart ? s.Start : s.End) - c;
        return (sweep > 0 ? rad.PerpLeft : rad.PerpLeft * -1).Normalized();
    }

    private static double Angle(Vec2 u, Vec2 v) => Math.Abs(Math.Atan2(u.Cross(v), u.Dot(v)));

    /// <summary>Distância do ponto ao trecho (reta ou arco).</summary>
    private static double Distance(BoundarySegment s, Vec2 v)
    {
        if (!s.IsArc)
        {
            var ab = s.End - s.Start;
            var l2 = ab.Dot(ab);
            var t = l2 < 1e-18 ? 0 : Math.Clamp((v - s.Start).Dot(ab) / l2, 0, 1);
            return v.DistanceTo(s.Start + ab * t);
        }
        var (c, r) = s.Circle;
        var sweep = Sweep(c, s.Start, s.Mid!.Value, s.End);
        var d = Math.Atan2(v.Y - c.Y, v.X - c.X) - Math.Atan2(s.Start.Y - c.Y, s.Start.X - c.X);
        while (d <= -Math.PI) d += 2 * Math.PI;
        while (d > Math.PI) d -= 2 * Math.PI;
        if (sweep > 0 && d < 0) d += 2 * Math.PI;
        if (sweep < 0 && d > 0) d -= 2 * Math.PI;
        if (Math.Abs(d) <= Math.Abs(sweep) + 1e-12) return Math.Abs(v.DistanceTo(c) - r);
        return Math.Min(v.DistanceTo(s.Start), v.DistanceTo(s.End));
    }

    /// <summary>
    /// Ajusta o polígono inteiro (contorno externo e furos). Se o ajuste criar autointerseção ou toque entre anéis (peças
    /// muito estreitas), tenta com tolerância menor e, por fim, devolve as retas originais.
    /// </summary>
    public static List<List<BoundarySegment>> FitPolygon(Polygon2 poly, double tolerance = DefaultTolerance, double minEdge = 0.002,
        IReadOnlyList<Vec2>? centers = null)
    {
        foreach (var tol in new[] { tolerance, tolerance / 4 })
        {
            var rings = new List<List<BoundarySegment>> { Fit(poly.Outer, tol, minEdge, centers) };
            rings.AddRange(poly.Holes.Select(h => Fit(h, tol, minEdge, centers)));
            if (rings.All(r => r.Count >= 2 || r.Count == 1 && r[0].IsArc) && IsSimple(rings, tol)) return rings;
        }
        var raw = new List<List<BoundarySegment>> { Lines(Dedupe(poly.Outer, minEdge * 0.5)) };
        raw.AddRange(poly.Holes.Select(h => Lines(Dedupe(h, minEdge * 0.5))));
        return raw;
    }

    /// <summary>Contorno ajustado convertido de volta em pontos (arcos com flecha máxima dada) – para testes e prévias.</summary>
    public static List<Vec2> Sample(IReadOnlyList<BoundarySegment> segments, double maxChordError = 0.001) =>
        segments.SelectMany(s => s.Sample(maxChordError)).ToList();

    /// <summary>
    /// Maior distância dos vértices do contorno original (pontos reais do traçado – sem os pontos intermediários que as
    /// operações booleanas deixam sobre as cordas, ver <see cref="Significant"/>) ao contorno ajustado.
    /// </summary>
    public static double VertexDeviation(IReadOnlyList<Vec2> original, IReadOnlyList<BoundarySegment> fitted, double tolerance = DefaultTolerance) =>
        OneWay(Significant(Dedupe(original, 1e-6), tolerance), Sample(fitted, 0.0002));

    /// <summary>
    /// Pontos do traçado: remove os vértices que ficam sobre a reta entre os vizinhos (≤ 1 mm, ou 20 % da tolerância) –
    /// cortes de recortes e uniões no meio das cordas de uma curva discretizada. Eles não são pontos da curva e
    /// impediriam o arco.
    /// </summary>
    public static List<Vec2> Significant(IReadOnlyList<Vec2> ring, double tolerance = DefaultTolerance)
    {
        var lim = Math.Min(0.001, 0.2 * tolerance);
        var pts = ring.ToList();
        if (pts.Count <= 4) return pts;
        var n = pts.Count;
        var turn = new double[n];
        for (int i = 0; i < n; i++) turn[i] = Turn(pts[(i - 1 + n) % n], pts[i], pts[(i + 1) % n]);
        // Maior giro na vizinhança (±6 vértices): numa corda com vários pontos de corte, os vértices de verdade da curva.
        var local = new double[n];
        for (int i = 0; i < n; i++)
            for (int k = -6; k <= 6; k++)
                if (k != 0) local[i] = Math.Max(local[i], turn[((i + k) % n + n) % n]);
        var keep = new List<Vec2>(n);
        for (int i = 0; i < n; i++)
        {
            var a = keep.Count > 0 ? keep[^1] : pts[^1];
            var b = pts[i];
            var c = pts[(i + 1) % n];
            var ac = c - a;
            var len = ac.Length;
            // Só pontos entre os vizinhos (sem ida e volta), quase sobre a reta deles e que giram bem menos que os vizinhos:
            // ponto de corte no meio de uma corda. Os pontos de uma curva fina (todos girando igual) ficam.
            var isolated = turn[i] <= 0.35 * local[i] || turn[i] < 1e-6;
            if (isolated && len > 1e-9 && Math.Abs((b - a).Cross(ac)) / len <= lim && (b - a).Dot(ac) > 0 && (c - b).Dot(ac) > 0 && n - (i - keep.Count) > 3)
                continue;
            keep.Add(b);
        }
        return keep.Count >= 3 ? keep : pts;
    }

    /// <summary>
    /// Maior distância entre o contorno ajustado e o original (nos dois sentidos, amostrando arcos e retas a cada 2 cm).
    /// Inclui a flecha das cordas antigas sobre a curva (≤ 2 × tolerância).
    /// </summary>
    public static double Deviation(IReadOnlyList<Vec2> original, IReadOnlyList<BoundarySegment> fitted)
    {
        var fit = Sample(fitted, 0.0002);
        var fromOriginal = Dense(original.Append(original[0]).ToList(), 0.02);
        var fromFitted = Dense(fit.Append(fit[0]).ToList(), 0.02);
        return Math.Max(OneWay(fromOriginal, fit), OneWay(fromFitted, original));
    }

    // ------------------------------------------------------------------ geometria auxiliar

    /// <summary>Circunferência pelos três pontos (nula se forem colineares).</summary>
    public static (Vec2 Center, double Radius)? CircleThrough(Vec2 a, Vec2 b, Vec2 c)
    {
        var d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-12) return null;
        double a2 = a.X * a.X + a.Y * a.Y, b2 = b.X * b.X + b.Y * b.Y, c2 = c.X * c.X + c.Y * c.Y;
        var ux = (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d;
        var uy = (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d;
        var center = new Vec2(ux, uy);
        return (center, center.DistanceTo(a));
    }

    /// <summary>Varredura com sinal (rad) do arco que vai de <paramref name="a"/> a <paramref name="c"/> passando por <paramref name="b"/>.</summary>
    public static double Sweep(Vec2 center, Vec2 a, Vec2 b, Vec2 c)
    {
        double Ang(Vec2 p) => Math.Atan2(p.Y - center.Y, p.X - center.X);
        double Norm(double x) { while (x <= -Math.PI) x += 2 * Math.PI; while (x > Math.PI) x -= 2 * Math.PI; return x; }
        var ab = Norm(Ang(b) - Ang(a));
        var bc = Norm(Ang(c) - Ang(b));
        // Mesmo sentido nos dois trechos (o ponto do meio está no arco).
        if (ab * bc < 0) { if (Math.Abs(ab) > Math.Abs(bc)) bc += bc < 0 ? 2 * Math.PI : -2 * Math.PI; else ab += ab < 0 ? 2 * Math.PI : -2 * Math.PI; }
        return ab + bc;
    }

    /// <summary>Verifica se os anéis (amostrados) não se cruzam nem se tocam – a peça continua válida para o Revit.</summary>
    public static bool IsSimple(IReadOnlyList<IReadOnlyList<BoundarySegment>> rings, double tolerance)
    {
        var all = new List<(Vec2 A, Vec2 B, int Ring, int Index, int Count)>();
        for (int r = 0; r < rings.Count; r++)
        {
            var pts = Sample(rings[r], Math.Max(1e-4, tolerance / 5));
            for (int i = 0; i < pts.Count; i++) all.Add((pts[i], pts[(i + 1) % pts.Count], r, i, pts.Count));
        }
        if (all.Count == 0) return true;
        // Grade uniforme: só segmentos da mesma célula são comparados.
        var minX = all.Min(s => Math.Min(s.A.X, s.B.X));
        var minY = all.Min(s => Math.Min(s.A.Y, s.B.Y));
        var maxX = all.Max(s => Math.Max(s.A.X, s.B.X));
        var maxY = all.Max(s => Math.Max(s.A.Y, s.B.Y));
        var cell = Math.Max(0.05, Math.Max(maxX - minX, maxY - minY) / Math.Max(1, Math.Sqrt(all.Count)));
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            int x0 = (int)Math.Floor((Math.Min(s.A.X, s.B.X) - minX) / cell), x1 = (int)Math.Floor((Math.Max(s.A.X, s.B.X) - minX) / cell);
            int y0 = (int)Math.Floor((Math.Min(s.A.Y, s.B.Y) - minY) / cell), y1 = (int)Math.Floor((Math.Max(s.A.Y, s.B.Y) - minY) / cell);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!grid.TryGetValue((x, y), out var l)) grid[(x, y)] = l = new List<int>();
                    l.Add(i);
                }
        }
        var seen = new HashSet<(int, int)>();
        foreach (var l in grid.Values)
            for (int a = 0; a < l.Count; a++)
                for (int b = a + 1; b < l.Count; b++)
                {
                    var i = l[a];
                    var j = l[b];
                    var si = all[i];
                    var sj = all[j];
                    if (si.Ring == sj.Ring)
                    {
                        var diff = Math.Abs(si.Index - sj.Index);
                        if (diff <= 1 || diff == si.Count - 1) continue;   // vizinhos compartilham um vértice
                    }
                    if (!seen.Add((Math.Min(i, j), Math.Max(i, j)))) continue;
                    if (SegmentsTouch(si.A, si.B, sj.A, sj.B)) return false;
                }
        return true;
    }

    private static bool SegmentsTouch(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        double O(Vec2 p, Vec2 q, Vec2 r) => (q - p).Cross(r - p);
        var d1 = O(c, d, a);
        var d2 = O(c, d, b);
        var d3 = O(a, b, c);
        var d4 = O(a, b, d);
        if ((d1 > 0 && d2 < 0 || d1 < 0 && d2 > 0) && (d3 > 0 && d4 < 0 || d3 < 0 && d4 > 0)) return true;
        bool On(Vec2 p, Vec2 q, Vec2 r) => Math.Min(p.X, q.X) - 1e-12 <= r.X && r.X <= Math.Max(p.X, q.X) + 1e-12
                                            && Math.Min(p.Y, q.Y) - 1e-12 <= r.Y && r.Y <= Math.Max(p.Y, q.Y) + 1e-12;
        return d1 == 0 && On(c, d, a) || d2 == 0 && On(c, d, b) || d3 == 0 && On(a, b, c) || d4 == 0 && On(a, b, d);
    }

    // ------------------------------------------------------------------ ajuste

    /// <summary>Maior <c>j</c> em (k, n] com o teste verdadeiro: avança dobrando o passo e refina por bissecção.</summary>
    private static int Longest(int k, int n, Func<int, bool> fits, int minStep)
    {
        var good = k + minStep;
        if (good > n) return n;
        var step = 1;
        int bad = -1;
        while (true)
        {
            var j = Math.Min(n, good + step);
            if (j == good) break;
            if (fits(j)) { good = j; if (good == n) break; step *= 2; }
            else { bad = j; break; }
        }
        if (bad < 0) return good;
        while (bad - good > 1)
        {
            var m = (good + bad) / 2;
            if (fits(m)) good = m; else bad = m;
        }
        return good;
    }

    private static bool FitsLine(Vec2[] p, int i, int j, double tol)
    {
        if (j - i <= 1) return true;
        var a = p[i];
        var b = p[j];
        var ab = b - a;
        var len = ab.Length;
        if (len < 1e-9) return false;
        var u = ab / len;
        for (int k = i + 1; k < j; k++)
        {
            var v = p[k] - a;
            var t = v.Dot(u);
            // O ponto precisa ficar entre as pontas (nada de ida e volta na mesma reta).
            if (t < -tol || t > len + tol) return false;
            if (Math.Abs(v.Cross(u)) > tol) return false;
        }
        return true;
    }

    private static bool FitsArc(Vec2[] p, int i, int j, double tol, double minEdge, IReadOnlyList<Vec2>? centers = null, bool hintOnly = false)
    {
        if (j - i < 2) return false;
        var m = (i + j) / 2;
        if (ArcCircle(p, i, j, centers, tol, hintOnly) is not { } circle) return false;
        var (c, r) = circle;
        if (Math.Abs(p[i].DistanceTo(c) - r) > tol) return false;
        if (r > MaxRadius || r < Math.Max(0.01, minEdge)) return false;
        if (p[i].DistanceTo(p[j]) < minEdge) return false;
        var sweep = Sweep(c, p[i], p[m], p[j]);
        if (Math.Abs(sweep) > MaxSweep + 1e-9) return false;
        var sign = Math.Sign(sweep);
        var prev = Math.Atan2(p[i].Y - c.Y, p[i].X - c.X);
        double total = 0;
        for (int k = i + 1; k <= j; k++)
        {
            if (Math.Abs(p[k].DistanceTo(c) - r) > tol) return false;
            var a = Math.Atan2(p[k].Y - c.Y, p[k].X - c.X);
            var d = a - prev;
            while (d <= -Math.PI) d += 2 * Math.PI;
            while (d > Math.PI) d -= 2 * Math.PI;
            // Ângulos sempre avançando no mesmo sentido (o arco não volta sobre si).
            if (d * sign <= 0) return false;
            // Curva discretizada × polígono de verdade: cada corda gira pouco (≤ 12°) – um octógono de placa (45°) continua
            // octógono. Os vértices são os pontos reais do traçado (≤ tol do arco); a corda entre eles já se afastava da curva
            // real pela discretização (eixo em arco afastado para a borda externa, círculos com 1 cm de flecha) – limitado
            // a 5 × tol.
            if (Math.Abs(d) > MaxChordTurn) return false;
            // Afastamento entre o meio da corda antiga e o arco (com 0,5 mm de folga sobre o limite).
            if (Math.Abs(r - ((p[k - 1] + p[k]) * 0.5).DistanceTo(c)) > ChordSagittaFactor * tol - 0.0005) return false;
            total += d;
            prev = a;
        }
        return Math.Abs(total - sweep) < 1e-6;
    }

    /// <summary>Junta retas consecutivas colineares e absorve trechos menores que a aresta mínima.</summary>
    private static List<BoundarySegment> Merge(List<BoundarySegment> input, double minEdge)
    {
        var segs = input.ToList();
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
                if (!last.IsArc) { res[^1] = new BoundarySegment(last.Start, s.End); continue; }
                // Arco segue até a ponta da reta curta pela mesma circunferência; o trecho seguinte começa no mesmo ponto.
                var nextIsFirst = i + 1 >= segs.Count;
                var next = nextIsFirst ? res[0] : segs[i + 1];
                var j = Joint(last, next, s.End);
                res[^1] = Retarget(last, last.Start, j);
                if (nextIsFirst) res[0] = Retarget(res[0], j, res[0].End);
                else segs[i + 1] = Retarget(next, j, next.End);
                continue;
            }
            res.Add(s);
        }
        // Fecho: a última reta pode continuar a primeira.
        if (res.Count > 2 && !res[0].IsArc && !res[^1].IsArc && Collinear(res[^1].Start, res[0].Start, res[0].End))
        {
            res[0] = new BoundarySegment(res[^1].Start, res[0].End);
            res.RemoveAt(res.Count - 1);
        }
        return res;
    }

    /// <summary>
    /// Sobra de curva: uma reta curta entre arcos (ex.: o fecho de um círculo, depois de dois arcos de 180°) que está na
    /// mesma circunferência do arco vizinho entra nele – o arco cresce pela mesma circunferência e o vizinho do outro lado
    /// começa (ou termina) no novo fim dele.
    /// </summary>
    private static List<BoundarySegment> Absorb(List<BoundarySegment> segs, double tol)
    {
        var changed = true;
        while (changed && segs.Count > 2)
        {
            changed = false;
            for (int i = 0; i < segs.Count && segs.Count > 2; i++)
            {
                var s = segs[i];
                if (s.IsArc) continue;
                var ip = (i - 1 + segs.Count) % segs.Count;
                var inx = (i + 1) % segs.Count;
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

    /// <summary>O arco pode crescer até as novas pontas: ponto novo na mesma circunferência, mesmo sentido, pouco a mais.</summary>
    private static bool CanExtend(BoundarySegment arc, Vec2 start, Vec2 end, Vec2 added, double tol)
    {
        var (c, r) = arc.Circle;
        if (Math.Abs(added.DistanceTo(c) - r) > tol) return false;
        var sweep = Sweep(c, start, arc.Mid!.Value, end);
        var old = Sweep(c, arc.Start, arc.Mid!.Value, arc.End);
        // Mesmo sentido, crescendo pouco (uma corda), sem passar de 270°.
        if (Math.Sign(sweep) != Math.Sign(old) || Math.Abs(sweep) <= Math.Abs(old) || Math.Abs(sweep) > 1.5 * Math.PI) return false;
        var extra = Math.Abs(sweep) - Math.Abs(old);
        return extra <= MaxChordTurn && r * (1 - Math.Cos(extra / 2)) <= ChordSagittaFactor * tol;
    }

    /// <summary>Ponto de junção perto de <paramref name="v"/> sobre as circunferências dos trechos vizinhos que são arcos.</summary>
    private static Vec2 Joint(BoundarySegment a, BoundarySegment b, Vec2 v) => (a.IsArc, b.IsArc) switch
    {
        (true, true) => Meet(a.Circle, b.Circle, v) is { } m && m.DistanceTo(v) < 0.01 ? m : v,
        (true, false) => Project(a.Circle, v),
        (false, true) => Project(b.Circle, v),
        _ => v,
    };

    /// <summary>O trecho com novas pontas; o arco continua na mesma circunferência e no mesmo sentido.</summary>
    private static BoundarySegment Retarget(BoundarySegment seg, Vec2 start, Vec2 end)
    {
        if (!seg.IsArc) return new BoundarySegment(start, end);
        var (c, r) = seg.Circle;
        var old = Sweep(c, seg.Start, seg.Mid!.Value, seg.End);
        var a0 = Math.Atan2(start.Y - c.Y, start.X - c.X);
        var sw = Math.Atan2(end.Y - c.Y, end.X - c.X) - a0;
        while (sw <= -Math.PI) sw += 2 * Math.PI;
        while (sw > Math.PI) sw -= 2 * Math.PI;
        if (old > 0 && sw <= 0) sw += 2 * Math.PI;
        if (old < 0 && sw >= 0) sw -= 2 * Math.PI;
        return new BoundarySegment(start, end, c + Vec2.FromAngle(a0 + sw / 2) * r);
    }

    private static bool Collinear(Vec2 a, Vec2 b, Vec2 c)
    {
        var u = b - a;
        var v = c - b;
        var lu = u.Length;
        var lv = v.Length;
        if (lu < 1e-12 || lv < 1e-12) return true;
        return Math.Abs(u.Cross(v)) / (lu * lv) < 1e-9 && u.Dot(v) > 0;
    }

    private static List<BoundarySegment> Lines(IReadOnlyList<Vec2> pts)
    {
        var res = new List<BoundarySegment>(pts.Count);
        for (int i = 0; i < pts.Count; i++) res.Add(new BoundarySegment(pts[i], pts[(i + 1) % pts.Count]));
        return res;
    }

    private static List<Vec2> Dedupe(IReadOnlyList<Vec2> ring, double tol)
    {
        var pts = new List<Vec2>(ring.Count);
        foreach (var v in ring)
            if (pts.Count == 0 || pts[^1].DistanceTo(v) > tol) pts.Add(v);
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= tol) pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    private static double Turn(Vec2 a, Vec2 b, Vec2 c)
    {
        var u = b - a;
        var v = c - b;
        var lu = u.Length;
        var lv = v.Length;
        if (lu < 1e-12 || lv < 1e-12) return 0;
        return Math.Acos(Math.Clamp(u.Dot(v) / (lu * lv), -1, 1));
    }

    private static List<Vec2> Dense(List<Vec2> pts, double step)
    {
        var res = new List<Vec2>();
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            var n = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / step));
            for (int k = 0; k < n; k++) res.Add(a + (b - a) * ((double)k / n));
        }
        return res;
    }

    /// <summary>Maior distância dos pontos <paramref name="pts"/> ao anel fechado <paramref name="ring"/>.</summary>
    private static double OneWay(List<Vec2> pts, IReadOnlyList<Vec2> ring)
    {
        var max = 0.0;
        foreach (var q in pts)
        {
            var best = double.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var ab = b - a;
                var l2 = ab.Dot(ab);
                var t = l2 < 1e-18 ? 0 : Math.Clamp((q - a).Dot(ab) / l2, 0, 1);
                var d = q.DistanceTo(a + ab * t);
                if (d < best) best = d;
            }
            if (best > max) max = best;
        }
        return max;
    }
}
