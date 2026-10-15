using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>Linha de divisa dos lotes: pontos (m) e identificador (UniqueId da linha do Revit).</summary>
public sealed record LinhaDivisa(string Id, IReadOnlyList<Vec2> Pontos);

/// <summary>Região fechada da quadra (um lote): forma, chave estável (linhas que a fecham e o lado de cada uma) e as linhas.</summary>
public sealed record RegiaoLote(string Chave, Polygon2 Forma, IReadOnlyList<string> Linhas);

public sealed class ResultadoDivisao
{
    public List<RegiaoLote> Regioes { get; } = new();
    /// <summary>Linhas que não fecham nenhuma região (soltas, fora da quadra ou só tocando o contorno).</summary>
    public List<string> LinhasSoltas { get; } = new();
    public List<string> Avisos { get; } = new();
}

/// <summary>
/// Quadra dividida por linhas desenhadas (linhas nativas do Revit): o contorno e as linhas formam um grafo plano; cada face
/// fechada dentro da quadra é um lote. Pontas que param a até <c>tolerancia</c> de outra linha ou do contorno são prolongadas
/// até lá; trechos que sobram para fora (ou soltos) são ignorados; lascas menores que <c>areaMinima</c> vão para o vizinho com
/// a maior divisa em comum. As áreas das regiões somam a área da quadra.
/// </summary>
public static class DivisaoPorLinhas
{
    private const double Eps = 1e-6;

    public static ResultadoDivisao Dividir(Polygon2 quadra, IReadOnlyList<LinhaDivisa> linhas, double tolerancia = 0.5, double areaMinima = 1.0)
    {
        var res = new ResultadoDivisao();
        var rings = quadra.Holes.Prepend(quadra.Outer).Where(r => r.Count >= 3).ToList();
        // Segmentos: contorno (id nulo) e linhas (prolongadas até o que estiver perto das pontas).
        var segs = new List<(Vec2 A, Vec2 B, string? Id, int Line)>();
        foreach (var r in rings)
            for (int i = 0; i < r.Count; i++)
                if (r[i].DistanceTo(r[(i + 1) % r.Count]) > Eps) segs.Add((r[i], r[(i + 1) % r.Count], null, -1));
        var boundary = segs.ToList();
        var lines = linhas.Select(l => Limpa(l.Pontos)).ToList();
        for (int k = 0; k < lines.Count; k++)
        {
            var pts = lines[k];
            if (pts.Count < 2) continue;
            var others = boundary.Select(s => (s.A, s.B)).Concat(Enumerable.Range(0, lines.Count).Where(j => j != k && lines[j].Count >= 2)
                .SelectMany(j => Pares(lines[j]))).ToList();
            pts[0] = Prolonga(pts[0], pts[1], others, tolerancia);
            pts[^1] = Prolonga(pts[^1], pts[^2], others, tolerancia);
            foreach (var (a, b) in Pares(pts)) if (a.DistanceTo(b) > Eps) segs.Add((a, b, linhas[k].Id, k));
        }

        // Nós: pontas e cruzamentos de todos os segmentos (fundidos a 0,1 mm).
        var nodes = new List<Vec2>();
        int Node(Vec2 p)
        {
            for (int i = 0; i < nodes.Count; i++) if (nodes[i].DistanceTo(p) < 1e-4) return i;
            nodes.Add(p);
            return nodes.Count - 1;
        }
        var cuts = segs.Select(_ => new List<double> { 0, 1 }).ToList();
        for (int i = 0; i < segs.Count; i++)
            for (int j = i + 1; j < segs.Count; j++)
                foreach (var (ti, tj) in Cruzamentos(segs[i].A, segs[i].B, segs[j].A, segs[j].B))
                {
                    cuts[i].Add(ti);
                    cuts[j].Add(tj);
                }
        // Arestas (sem repetir; a de contorno prevalece sobre uma linha que corre por cima dele).
        var edges = new Dictionary<(int, int), (string? Id, int Line)>();
        for (int i = 0; i < segs.Count; i++)
        {
            var (a, b, id, line) = segs[i];
            var ts = cuts[i].Distinct().OrderBy(t => t).ToList();
            for (int k = 0; k + 1 < ts.Count; k++)
            {
                var u = Node(a + (b - a) * ts[k]);
                var v = Node(a + (b - a) * ts[k + 1]);
                if (u == v) continue;
                var key = u < v ? (u, v) : (v, u);
                if (!edges.TryGetValue(key, out var cur) || cur.Id != null && id == null) edges[key] = (id, line);
            }
        }
        // Pontas soltas fora: tira arestas com nó de grau 1 (repetido até não sobrar).
        var adj = new Dictionary<int, HashSet<int>>();
        foreach (var (u, v) in edges.Keys)
        {
            (adj.TryGetValue(u, out var su) ? su : adj[u] = new()).Add(v);
            (adj.TryGetValue(v, out var sv) ? sv : adj[v] = new()).Add(u);
        }
        var queue = new Queue<int>(adj.Where(kv => kv.Value.Count == 1).Select(kv => kv.Key));
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (!adj.TryGetValue(n, out var nb) || nb.Count != 1) continue;
            var m = nb.First();
            edges.Remove(n < m ? (n, m) : (m, n));
            adj[n].Clear();
            adj[m].Remove(n);
            if (adj[m].Count == 1) queue.Enqueue(m);
        }
        // Faces: em cada nó as arestas em ordem de ângulo; a face à esquerda segue sempre a próxima no sentido horário.
        var order = adj.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(w => Math.Atan2(nodes[w].Y - nodes[kv.Key].Y, nodes[w].X - nodes[kv.Key].X)).ToList());
        var used = new HashSet<(int, int)>();
        var faces = new List<List<int>>();
        foreach (var (u0, v0) in edges.Keys.SelectMany(e => new[] { e, (e.Item2, e.Item1) }))
        {
            if (used.Contains((u0, v0))) continue;
            var cycle = new List<int>();
            var (u, v) = (u0, v0);
            var guard = 0;
            while (used.Add((u, v)) && guard++ < 100000)
            {
                cycle.Add(u);
                var around = order[v];
                var i = around.IndexOf(u);
                var w = around[(i - 1 + around.Count) % around.Count];
                (u, v) = (v, w);
            }
            if (cycle.Count >= 3) faces.Add(cycle);
        }
        var polys = new List<(Polygon2 P, List<int> Cycle)>();
        foreach (var f in faces)
        {
            var ring = f.Select(i => nodes[i]).ToList();
            if (Polygon2.SignedArea(ring) <= 1e-9) continue;
            var p = new Polygon2(ring);
            if (!quadra.Contains(LotDrawing.InteriorPoint(p))) continue;
            polys.Add((p, f));
        }
        // Ilhas (faces dentro de outra face sem ligação com ela) e furos da quadra saem da face de fora.
        var shapes = new List<(Polygon2 P, List<int> Cycle)>();
        foreach (var (p, c) in polys)
        {
            var inner = polys.Where(o => !ReferenceEquals(o.P, p) && o.P.Area < p.Area && p.Contains(LotDrawing.InteriorPoint(o.P))
                                         && o.P.Outer.All(v => p.Contains(v) || p.DistanceTo(v) < 1e-3)).Select(o => o.P).ToList();
            var holes = quadra.Holes.Select(h => new Polygon2(h)).Where(h => p.Contains(LotDrawing.InteriorPoint(h))).ToList();
            var shape = inner.Count + holes.Count > 0
                ? PolygonOps.Difference(new[] { p }, inner.Concat(holes)).OrderByDescending(x => x.Area).FirstOrDefault() ?? p
                : p;
            shapes.Add((shape, c));
        }
        // Chave de cada região: as linhas da borda dela e de que lado de cada uma ela fica.
        var regions = new List<(Polygon2 P, string Key, List<string> Lines)>();
        foreach (var (p, c) in shapes)
        {
            var sides = new SortedSet<string>(StringComparer.Ordinal);
            var ids = new List<string>();
            for (int i = 0; i < c.Count; i++)
            {
                var u = c[i];
                var v = c[(i + 1) % c.Count];
                if (!edges.TryGetValue(u < v ? (u, v) : (v, u), out var e) || e.Id == null) continue;
                var seg = lines[e.Line];
                var mid = (nodes[u] + nodes[v]) / 2;
                var dir = DirecaoEm(seg, mid);
                // A região fica à esquerda do percurso u → v; o lado em relação ao sentido da linha.
                var side = dir.Dot(nodes[v] - nodes[u]) >= 0 ? "+" : "-";
                sides.Add(Hash(e.Id) + side);
                if (!ids.Contains(e.Id)) ids.Add(e.Id);
            }
            regions.Add((p, sides.Count == 0 ? "l:q" : "l:" + string.Join(".", sides), ids));
        }
        // Lascas: para o vizinho com a maior divisa em comum.
        foreach (var small in regions.Where(r => r.P.Area < areaMinima).ToList())
        {
            regions.Remove(small);
            var best = regions.Select(r => (R: r, L: Divisa(r.P, small.P))).Where(x => x.L > 1e-3).OrderByDescending(x => x.L).FirstOrDefault();
            if (best.R.P == null)
            {
                res.Avisos.Add($"Região de {LotReport.M(small.P.Area)} m² entre as linhas não encosta em nenhum lote – descartada.");
                continue;
            }
            var merged = PolygonOps.Union(new[] { best.R.P, small.P }).OrderByDescending(x => x.Area).First();
            regions[regions.IndexOf(best.R)] = (merged.Simplified(0.0005, 1e-8) ?? merged, best.R.Key, best.R.Lines);
            res.Avisos.Add($"Lasca de {LotReport.M(small.P.Area)} m² entre as linhas somada ao lote vizinho (feche melhor as linhas).");
        }
        // Chaves repetidas (raro): numeradas.
        foreach (var g in regions.GroupBy(r => r.Key).Where(g => g.Count() > 1))
        {
            var k = 1;
            foreach (var r in g.OrderBy(r => -r.P.Centroid.Y).ThenBy(r => r.P.Centroid.X).ToList())
                regions[regions.IndexOf(r)] = r with { Key = $"{r.Key}#{k++}" };
        }
        foreach (var r in regions) res.Regioes.Add(new RegiaoLote(r.Key, r.P, r.Lines));
        var usadas = regions.SelectMany(r => r.Lines).ToHashSet();
        foreach (var l in linhas.Where(l => !usadas.Contains(l.Id))) res.LinhasSoltas.Add(l.Id);
        return res;
    }

    /// <summary>Identificador curto e estável (FNV-1a, 8 dígitos hex) do UniqueId da linha.</summary>
    public static string Hash(string id)
    {
        uint h = 2166136261;
        foreach (var ch in id) { h ^= ch; h *= 16777619; }
        return h.ToString("x8");
    }

    private static List<Vec2> Limpa(IReadOnlyList<Vec2> pts)
    {
        var r = new List<Vec2>();
        foreach (var p in pts) if (r.Count == 0 || r[^1].DistanceTo(p) > 1e-4) r.Add(p);
        return r;
    }

    private static IEnumerable<(Vec2 A, Vec2 B)> Pares(IReadOnlyList<Vec2> pts)
    {
        for (int i = 0; i + 1 < pts.Count; i++) yield return (pts[i], pts[i + 1]);
    }

    /// <summary>Ponta <paramref name="end"/> prolongada (sentido de <paramref name="prev"/> para ela) até o primeiro segmento a até <paramref name="tol"/>.</summary>
    private static Vec2 Prolonga(Vec2 end, Vec2 prev, IReadOnlyList<(Vec2 A, Vec2 B)> others, double tol)
    {
        var d = (end - prev).Normalized();
        if (d.Length < 0.5) return end;
        // Já toca outro segmento: fica como está.
        if (others.Any(s => DistSeg(end, s.A, s.B) < 1e-4)) return end;
        double best = double.MaxValue;
        foreach (var (a, b) in others)
        {
            var e = b - a;
            var den = d.Cross(e);
            if (Math.Abs(den) < 1e-12) continue;
            var t = (a - end).Cross(e) / den;
            var u = (a - end).Cross(d) / den;
            if (t > 0 && t <= tol && u >= -1e-9 && u <= 1 + 1e-9) best = Math.Min(best, t);
        }
        return best < double.MaxValue ? end + d * best : end;
    }

    private static double DistSeg(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var l2 = ab.Dot(ab);
        var t = l2 < 1e-18 ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>Parâmetros (t em cada segmento, 0..1) dos cruzamentos e toques entre dois segmentos (inclui sobreposição colinear).</summary>
    private static IEnumerable<(double, double)> Cruzamentos(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        var r = b - a;
        var s = d - c;
        var den = r.Cross(s);
        var lr = r.Length;
        var ls = s.Length;
        if (Math.Abs(den) > 1e-12 * lr * ls)
        {
            var t = (c - a).Cross(s) / den;
            var u = (c - a).Cross(r) / den;
            var et = 1e-4 / lr;
            var eu = 1e-4 / ls;
            if (t >= -et && t <= 1 + et && u >= -eu && u <= 1 + eu) yield return (Math.Clamp(t, 0, 1), Math.Clamp(u, 0, 1));
            yield break;
        }
        // Paralelos: só os colineares (a menos de 0,1 mm) se tocam – as pontas de um sobre o outro.
        if (DistLine(c, a, b) > 1e-4) yield break;
        double Ta(Vec2 p) => (p - a).Dot(r) / (lr * lr);
        double Tc(Vec2 p) => (p - c).Dot(s) / (ls * ls);
        foreach (var p in new[] { c, d })
        {
            var t = Ta(p);
            if (t > -1e-9 && t < 1 + 1e-9) yield return (Math.Clamp(t, 0, 1), Math.Clamp(Tc(p), 0, 1));
        }
        foreach (var p in new[] { a, b })
        {
            var u = Tc(p);
            if (u > -1e-9 && u < 1 + 1e-9) yield return (Math.Clamp(Ta(p), 0, 1), Math.Clamp(u, 0, 1));
        }
    }

    private static double DistLine(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        return Math.Abs(ab.Cross(p - a)) / Math.Max(ab.Length, 1e-12);
    }

    /// <summary>Sentido da linha no trecho mais próximo de <paramref name="p"/>.</summary>
    private static Vec2 DirecaoEm(IReadOnlyList<Vec2> pts, Vec2 p)
    {
        var best = double.MaxValue;
        var dir = Vec2.UnitX;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var dd = DistSeg(p, pts[i], pts[i + 1]);
            if (dd < best) { best = dd; dir = (pts[i + 1] - pts[i]).Normalized(); }
        }
        return dir;
    }

    /// <summary>Comprimento da divisa comum entre dois polígonos.</summary>
    private static double Divisa(Polygon2 a, Polygon2 b)
    {
        var s = 0.0;
        var r = b.Outer;
        for (int i = 0; i < r.Count; i++)
        {
            var p = r[i];
            var q = r[(i + 1) % r.Count];
            var mid = (p + q) / 2;
            if (a.DistanceTo(mid) < 1e-3) s += p.DistanceTo(q);
        }
        return s;
    }
}
