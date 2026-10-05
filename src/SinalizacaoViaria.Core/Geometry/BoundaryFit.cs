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
    public static List<BoundarySegment> Fit(IReadOnlyList<Vec2> ring, double tolerance = DefaultTolerance, double minEdge = 0.002)
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

        var res = new List<BoundarySegment>();
        var k = 0;
        while (k < n)
        {
            var lineEnd = Longest(k, n, j => FitsLine(p, k, j, tolerance), 1);
            var arcEnd = k + 2 <= n && FitsArc(p, k, k + 2, tolerance, minEdge) ? Longest(k, n, j => FitsArc(p, k, j, tolerance, minEdge), 2) : -1;
            if (arcEnd > lineEnd)
            {
                var (c, r) = CircleThrough(p[k], p[(k + arcEnd) / 2], p[arcEnd])!.Value;
                var sweep = Sweep(c, p[k], p[(k + arcEnd) / 2], p[arcEnd]);
                var a0 = Math.Atan2(p[k].Y - c.Y, p[k].X - c.X);
                res.Add(new BoundarySegment(p[k], p[arcEnd], c + Vec2.FromAngle(a0 + sweep / 2) * r));
                k = arcEnd;
            }
            else
            {
                res.Add(new BoundarySegment(p[k], p[lineEnd]));
                k = lineEnd;
            }
        }
        return Absorb(Merge(res, minEdge), tolerance);
    }

    /// <summary>
    /// Ajusta o polígono inteiro (contorno externo e furos). Se o ajuste criar autointerseção ou toque entre anéis (peças
    /// muito estreitas), tenta com tolerância menor e, por fim, devolve as retas originais.
    /// </summary>
    public static List<List<BoundarySegment>> FitPolygon(Polygon2 poly, double tolerance = DefaultTolerance, double minEdge = 0.002)
    {
        foreach (var tol in new[] { tolerance, tolerance / 4 })
        {
            var rings = new List<List<BoundarySegment>> { Fit(poly.Outer, tol, minEdge) };
            rings.AddRange(poly.Holes.Select(h => Fit(h, tol, minEdge)));
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
        var keep = new List<Vec2>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            var a = keep.Count > 0 ? keep[^1] : pts[^1];
            var b = pts[i];
            var c = pts[(i + 1) % pts.Count];
            var ac = c - a;
            var len = ac.Length;
            // Só pontos entre os vizinhos (sem ida e volta) e quase sobre a reta deles.
            if (len > 1e-9 && Math.Abs((b - a).Cross(ac)) / len <= lim && (b - a).Dot(ac) > 0 && (c - b).Dot(ac) > 0 && pts.Count - (i - keep.Count) > 3)
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

    private static bool FitsArc(Vec2[] p, int i, int j, double tol, double minEdge)
    {
        if (j - i < 2) return false;
        var m = (i + j) / 2;
        if (CircleThrough(p[i], p[m], p[j]) is not { } circle) return false;
        var (c, r) = circle;
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
            if (r * (1 - Math.Cos(Math.Abs(d) / 2)) > ChordSagittaFactor * tol) return false;
            total += d;
            prev = a;
        }
        return Math.Abs(total - sweep) < 1e-6;
    }

    /// <summary>Junta retas consecutivas colineares e absorve trechos menores que a aresta mínima.</summary>
    private static List<BoundarySegment> Merge(List<BoundarySegment> segs, double minEdge)
    {
        var res = new List<BoundarySegment>();
        foreach (var s in segs)
        {
            if (res.Count > 0 && !s.IsArc && !res[^1].IsArc && Collinear(res[^1].Start, res[^1].End, s.End))
            {
                res[^1] = new BoundarySegment(res[^1].Start, s.End);
                continue;
            }
            if (res.Count > 0 && s.Start.DistanceTo(s.End) < minEdge && !s.IsArc)
            {
                var last = res[^1];
                res[^1] = last.IsArc ? last with { End = s.End } : new BoundarySegment(last.Start, s.End);
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
    /// mesma circunferência do arco vizinho entra nele.
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
                if (segs[ip].IsArc && Extend(segs[ip], segs[ip].Start, s.End, s.End, tol) is { } a)
                {
                    segs[ip] = a;
                    segs.RemoveAt(i);
                    changed = true;
                    break;
                }
                if (segs[inx].IsArc && Extend(segs[inx], s.Start, segs[inx].End, s.Start, tol) is { } b)
                {
                    segs[inx] = b;
                    segs.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
        return segs;
    }

    /// <summary>Arco <paramref name="arc"/> estendido até as novas pontas, se o ponto novo estiver na mesma circunferência.</summary>
    private static BoundarySegment? Extend(BoundarySegment arc, Vec2 start, Vec2 end, Vec2 added, double tol)
    {
        var (c, r) = arc.Circle;
        if (Math.Abs(added.DistanceTo(c) - r) > tol) return null;
        var sweep = Sweep(c, start, arc.Mid!.Value, end);
        var old = Sweep(c, arc.Start, arc.Mid!.Value, arc.End);
        // Mesmo sentido, crescendo pouco (uma corda), sem passar de 270°.
        if (Math.Sign(sweep) != Math.Sign(old) || Math.Abs(sweep) <= Math.Abs(old) || Math.Abs(sweep) > 1.5 * Math.PI) return null;
        var extra = Math.Abs(sweep) - Math.Abs(old);
        if (extra > MaxChordTurn || r * (1 - Math.Cos(extra / 2)) > ChordSagittaFactor * tol) return null;
        var a0 = Math.Atan2(start.Y - c.Y, start.X - c.X);
        return new BoundarySegment(start, end, c + Vec2.FromAngle(a0 + sweep / 2) * r);
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
