using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Via reconhecida num piso: eixo, largura típica da pista e as larguras medidas ao longo do eixo.</summary>
public sealed record FloorRoad(Polyline2 Axis, double Width, IReadOnlyList<(double Station, double Width)> Widths)
{
    /// <summary>A largura varia mais de 0,5 m ao longo do eixo (fora dos encontros).</summary>
    public bool VariableWidth => Widths.Count > 1 && Widths.Max(w => w.Width) - Widths.Min(w => w.Width) > 0.5;
}

/// <summary>Resultado do reconhecimento: vias e pontos de encontro (interseções).</summary>
public sealed record FloorRoadNetwork(List<FloorRoad> Roads, List<Vec2> Junctions, List<string> Warnings);

/// <summary>
/// Reconhece vias em pisos modelados à parte (projetos antigos, pistas feitas à mão): o contorno do piso é triangulado
/// (Delaunay) e o eixo cordal liga os meios das cordas de bordo a bordo; os ramos curtos das quinas são podados, os
/// encontros viram interseções, os trechos são reunidos em vias (seguem retos nos cruzamentos) e a largura sai da
/// distância ao bordo ao longo do eixo.
/// Serve para transformar a pista existente numa via do plugin sem refazer o pavimento.
/// </summary>
public static class FloorRoads
{
    /// <param name="shapes">Contornos dos pisos (m), com furos (canteiros, ilhas).</param>
    /// <param name="minWidth">Largura mínima de uma pista (m): faixas mais estreitas (ilhas, sobras) são ignoradas.</param>
    public static FloorRoadNetwork Read(IReadOnlyList<Polygon2> shapes, double minWidth = 2.5)
    {
        var warnings = new List<string>();
        var polys = shapes.Where(p => p.IsValid).ToList();
        if (polys.Count == 0) return new FloorRoadNetwork(new(), new(), new() { "Nenhum contorno de piso válido." });
        // Pisos encostados ou sobrepostos (pista feita em partes) viram uma área só – os bordos internos sumiriam do eixo.
        if (polys.Count > 1) polys = PolygonOps.Union(polys).Where(p => p.IsValid).ToList();

        // 1) Pontos do contorno (anéis externos e furos) a cada ~1/3 da largura mínima, com os vértices.
        var perimeter = polys.Sum(p => RingLength(p.Outer) + p.Holes.Sum(RingLength));
        var step = Math.Max(minWidth / 3, perimeter / 5000);
        var pts = new List<Vec2>();
        var ringOf = new List<(int Ring, int Idx, int Count)>();
        var ringId = 0;
        foreach (var poly in polys)
            foreach (var ring in new[] { poly.Outer }.Concat(poly.Holes))
            {
                var sampled = SampleRing(ring, step);
                for (int k = 0; k < sampled.Count; k++) { pts.Add(sampled[k]); ringOf.Add((ringId, k, sampled.Count)); }
                ringId++;
            }
        bool Inside(Vec2 q) => polys.Any(p => p.Contains(q));

        // 2) Triangulação de Delaunay e só os triângulos dentro do piso.
        var tris = Delaunay(pts).Where(t => Inside((pts[t.A] + pts[t.B] + pts[t.C]) / 3)).ToList();
        bool Boundary(int a, int b)
        {
            var (ra, ia, n) = ringOf[a];
            var (rb, ib, _) = ringOf[b];
            return ra == rb && ((ia + 1) % n == ib || (ib + 1) % n == ia);
        }
        // Arestas internas (compartilhadas por dois triângulos do piso).
        var edgeTris = new Dictionary<(int, int), List<int>>();
        for (int t = 0; t < tris.Count; t++)
            foreach (var e in Edges(tris[t]))
            {
                if (!edgeTris.TryGetValue(e, out var l)) edgeTris[e] = l = new List<int>();
                l.Add(t);
            }

        // 3) Eixo cordal: nós nos meios das arestas internas; triângulos de "manga" ligam dois meios, os de junção ligam os
        //    três ao centro, os de ponta encerram.
        var nodes = new List<Vec2>();
        var nodeOfEdge = new Dictionary<(int, int), int>();
        var adj = new List<HashSet<int>>();
        int Node(Vec2 p) { nodes.Add(p); adj.Add(new HashSet<int>()); return nodes.Count - 1; }
        void Link(int a, int b) { if (a == b) return; adj[a].Add(b); adj[b].Add(a); }
        int Mid((int, int) e)
        {
            if (nodeOfEdge.TryGetValue(e, out var id)) return id;
            return nodeOfEdge[e] = Node((pts[e.Item1] + pts[e.Item2]) / 2);
        }
        var junctionNodes = new HashSet<int>();
        foreach (var t in tris)
        {
            var internalEdges = Edges(t).Where(e => edgeTris[e].Count == 2 && !Boundary(e.Item1, e.Item2)).ToList();
            if (internalEdges.Count == 2) Link(Mid(internalEdges[0]), Mid(internalEdges[1]));
            else if (internalEdges.Count == 3)
            {
                var c = Node((pts[t.A] + pts[t.B] + pts[t.C]) / 3);
                junctionNodes.Add(c);
                foreach (var e in internalEdges) Link(c, Mid(e));
            }
        }

        // 4) Poda: ramos que terminam soltos e são curtos perto de um encontro (cantos, alargamentos, bocas).
        double HalfW(Vec2 q) => polys.Min(p => p.DistanceTo(q));
        for (int pass = 0; pass < 8; pass++)
        {
            var removedAny = false;
            foreach (var leaf in Enumerable.Range(0, nodes.Count).Where(i => adj[i].Count == 1).ToList())
            {
                // Caminha até um nó de grau ≠ 2.
                var path = new List<int> { leaf };
                var prev = -1;
                var cur = leaf;
                while (true)
                {
                    var next = adj[cur].FirstOrDefault(x => x != prev);
                    if (adj[cur].Count == 0 || (adj[cur].Count == 1 && prev >= 0)) break;
                    if (path.Count > 1 && adj[cur].Count != 2) break;
                    prev = cur;
                    cur = next;
                    path.Add(cur);
                    if (adj[cur].Count != 2) break;
                }
                var end = path[^1];
                if (adj[end].Count < 3) continue;                 // via isolada: fica
                var len = 0.0;
                for (int k = 1; k < path.Count; k++) len += nodes[path[k - 1]].DistanceTo(nodes[path[k]]);
                if (len > 2.2 * HalfW(nodes[end]) + 1.5) continue;
                for (int k = 0; k + 1 < path.Count; k++) { foreach (var x in adj[path[k]].ToList()) { adj[x].Remove(path[k]); } adj[path[k]].Clear(); }
                removedAny = true;
            }
            if (!removedAny) break;
        }

        // 5) Encontros: nós de grau ≥ 3 próximos (um cruzamento gera dois triângulos de junção) viram um só.
        var jn = Enumerable.Range(0, nodes.Count).Where(i => adj[i].Count >= 3).ToList();
        var cluster = new Dictionary<int, int>();
        var junctions = new List<Vec2>();
        foreach (var i in jn)
        {
            if (cluster.ContainsKey(i)) continue;
            var group = new List<int> { i };
            var r = HalfW(nodes[i]) * 2.2 + 1;
            // Os nós do grupo se ligam por um caminho curto (a "ponte" entre os dois triângulos de junção).
            var q = new Queue<int>();
            q.Enqueue(i);
            var seen = new HashSet<int> { i };
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var nb in adj[c])
                    if (seen.Add(nb) && nodes[nb].DistanceTo(nodes[i]) < r) { q.Enqueue(nb); if (adj[nb].Count >= 3) group.Add(nb); }
            }
            var center = new Vec2(group.Average(g => nodes[g].X), group.Average(g => nodes[g].Y));
            var id = junctions.Count;
            junctions.Add(center);
            foreach (var g in group) cluster[g] = id;
        }

        // 6) Trechos entre pontas/encontros; cada trecho sai do centro do encontro, reto até deixar a área do encontro.
        var lines = new List<List<Vec2>>();
        var used = new HashSet<(int, int)>();
        bool IsStop(int i) => adj[i].Count != 2 || cluster.ContainsKey(i);
        foreach (var s0 in Enumerable.Range(0, nodes.Count).Where(i => adj[i].Count > 0 && IsStop(i)))
            foreach (var first in adj[s0])
            {
                if (used.Contains((s0, first))) continue;
                var path = new List<int> { s0 };
                var prev = s0;
                var cur = first;
                used.Add((s0, first));
                used.Add((first, s0));
                while (true)
                {
                    path.Add(cur);
                    if (IsStop(cur)) break;
                    var next = adj[cur].First(x => x != prev);
                    used.Add((cur, next));
                    used.Add((next, cur));
                    prev = cur;
                    cur = next;
                }
                // Trecho todo dentro de um encontro (ponte entre triângulos de junção): some.
                if (cluster.TryGetValue(path[0], out var ca) && cluster.TryGetValue(path[^1], out var cb) && ca == cb) continue;
                var pl = path.Select(i => nodes[i]).ToList();
                foreach (var atStart in new[] { true, false })
                {
                    var ni = atStart ? path[0] : path[^1];
                    if (!cluster.TryGetValue(ni, out var cj)) continue;
                    var jc = junctions[cj];
                    var rr = HalfW(jc) * 1.3 + 0.5;
                    if (atStart)
                    {
                        var k = 0;
                        while (k < pl.Count - 2 && pl[k].DistanceTo(jc) < rr) k++;
                        pl = new[] { jc }.Concat(pl.Skip(k)).ToList();
                    }
                    else
                    {
                        var k = pl.Count - 1;
                        while (k > 1 && pl[k].DistanceTo(jc) < rr) k--;
                        pl = pl.Take(k + 1).Concat(new[] { jc }).ToList();
                    }
                }
                if (pl.Count >= 2) lines.Add(Simplify(pl, Math.Min(0.25, step / 3)));
            }
        // Anéis sem pontas nem encontros (pista em volta de um canteiro): partidos nos dois pontos de maior curvatura.
        var ringLines = new List<List<Vec2>>();
        foreach (var s0 in Enumerable.Range(0, nodes.Count).Where(i => adj[i].Count == 2))
        {
            var first = adj[s0].First();
            if (used.Contains((s0, first))) continue;
            var ring = new List<int> { s0 };
            var prev = s0;
            var cur = first;
            while (cur != s0 && ring.Count < nodes.Count)
            {
                ring.Add(cur);
                used.Add((prev, cur));
                used.Add((cur, prev));
                var next = adj[cur].FirstOrDefault(x => x != prev);
                prev = cur;
                cur = next;
            }
            used.Add((prev, s0));
            used.Add((s0, prev));
            if (ring.Count < 8) continue;
            var pl = ring.Select(i => nodes[i]).ToList();
            double Turn(int k)
            {
                var a = pl[(k - 3 + pl.Count) % pl.Count];
                var b = pl[k];
                var c = pl[(k + 3) % pl.Count];
                return Math.Abs((b - a).Normalized().Cross((c - b).Normalized()));
            }
            var k1 = Enumerable.Range(0, pl.Count).OrderByDescending(Turn).First();
            var k2 = Enumerable.Range(0, pl.Count).Where(k => Math.Min(Math.Abs(k - k1), pl.Count - Math.Abs(k - k1)) > pl.Count / 4).OrderByDescending(Turn).FirstOrDefault(k1);
            var (lo, hi) = (Math.Min(k1, k2), Math.Max(k1, k2));
            if (hi > lo)
            {
                ringLines.Add(Simplify(pl.Skip(lo).Take(hi - lo + 1).ToList(), Math.Min(0.25, step / 3)));
                ringLines.Add(Simplify(pl.Skip(hi).Concat(pl.Take(lo + 1)).ToList(), Math.Min(0.25, step / 3)));
                warnings.Add("Pista em volta de um canteiro fechado: cada lado foi reconhecido como uma via (confira o sentido).");
            }
        }
        if (lines.Count == 0 && ringLines.Count == 0) return new FloorRoadNetwork(new(), junctions, new() { "Não foi possível reconhecer uma pista: o piso é estreito demais ou não é alongado." });

        // 7) Vias: trechos reunidos (seguem retos nos cruzamentos, curvas entre dois trechos).
        // Encontros em T/cruz: a via que atravessa segue reta (sem passar pelo centro do encontro, que fica deslocado para a
        // boca da transversal); o encontro vai para o eixo dela e as transversais terminam nele.
        var groups0 = RoadChains.Split(lines.Select(l => (IReadOnlyList<Vec2>)l).ToList(), 0.05);
        for (int j = 0; j < junctions.Count; j++)
        {
            var jc = junctions[j];
            foreach (var g in groups0)
            {
                var ins = g.Where(i => lines[i][0].DistanceTo(jc) < 1e-6 || lines[i][^1].DistanceTo(jc) < 1e-6).ToList();
                if (ins.Count != 2) continue;
                // Pontos das duas linhas logo antes do encontro.
                // Ponto a ~15 m do encontro (fora da deformação do eixo cordal na boca da transversal).
                Vec2 Near(List<Vec2> l)
                {
                    var pl = new Polyline2(l[0].DistanceTo(jc) < 1e-6 ? l : Enumerable.Reverse(l).ToList());
                    return pl.PointAt(Math.Min(pl.Length, 15));
                }
                var a = Near(lines[ins[0]]);
                var b = Near(lines[ins[1]]);
                var ab = b - a;
                if (ab.Length < 1e-6) continue;
                var t = Math.Clamp((jc - a).Dot(ab) / ab.Dot(ab), 0, 1);
                var nj = a + ab * t;
                junctions[j] = nj;
                foreach (var l in lines)
                {
                    if (l[0].DistanceTo(jc) < 1e-6) l[0] = nj;
                    if (l[^1].DistanceTo(jc) < 1e-6) l[^1] = nj;
                }
                break;
            }
        }
        var groups = RoadChains.Split(lines.Select(l => (IReadOnlyList<Vec2>)l).ToList(), 0.05);
        var chains = groups.Select(g => Chain(g.Select(i => lines[i]).ToList(), 0.05)).Concat(ringLines).ToList();
        var roads = new List<FloorRoad>();
        foreach (var chain0 in chains)
        {
            var chain = chain0;
            if (chain.Count < 2) continue;
            // Pontas livres: o eixo cordal "escorrega" para o canto no fim da pista – corta onde a pista estreita (o bordo
            // transversal) e prolonga reto até o bordo.
            // Referência: a meia largura a ~12 m da ponta (dentro da pista, fora da quina).
            var cl = new Polyline2(chain);
            var refStart = HalfW(cl.PointAt(Math.Min(cl.Length / 2, 12)));
            var refEnd = HalfW(cl.PointAt(Math.Max(cl.Length / 2, cl.Length - 12)));
            chain = TrimEnd(chain, true, junctions, q => HalfW(q) < 0.8 * refStart);
            chain = TrimEnd(chain, false, junctions, q => HalfW(q) < 0.8 * refEnd);
            chain = Extend(chain, polys, true, junctions);
            chain = Extend(chain, polys, false, junctions);
            // Eixo no meio da pista: cada ponto (fora dos encontros e das pontas) vai para o meio entre os dois bordos.
            chain = Recenter(chain, polys, junctions);
            var axis = new Polyline2(chain);
            if (axis.Length < minWidth * 2) continue;
            var samples = new List<(double, double)>();
            var ds = Math.Max(1, axis.Length / 200);
            for (var s = ds / 2; s <= axis.Length - ds / 2; s += ds)
            {
                var p = axis.PointAt(s);
                if (junctions.Any(jc => jc.DistanceTo(p) < HalfW(jc) * 2.5 + 3)) continue;
                var w = CrossWidth(axis, s, polys);
                if (w >= minWidth * 0.8) samples.Add((s, w));
            }
            // Pontas livres: a meia largura final é afetada pelo bordo transversal.
            var trimmed = samples.Where(x => x.Item1 > 4 && x.Item1 < axis.Length - 4).ToList();
            if (trimmed.Count > 0) samples = trimmed;
            if (samples.Count == 0) samples.Add((axis.Length / 2, 2 * HalfW(axis.PointAt(axis.Length / 2))));
            var width = Median(samples.Select(x => x.Item2).ToList());
            roads.Add(new FloorRoad(axis, Math.Round(width * 20) / 20, samples));
        }
        if (roads.Count == 0) warnings.Add("Nenhuma pista longa o bastante foi reconhecida.");
        return new FloorRoadNetwork(roads, junctions, warnings);
    }

    private static double RingLength(IReadOnlyList<Vec2> r)
    {
        var l = 0.0;
        for (int k = 0; k < r.Count; k++) l += r[k].DistanceTo(r[(k + 1) % r.Count]);
        return l;
    }

    private static List<Vec2> SampleRing(IReadOnlyList<Vec2> r, double step)
    {
        var res = new List<Vec2>();
        for (int k = 0; k < r.Count; k++)
        {
            var a = r[k];
            var b = r[(k + 1) % r.Count];
            var n = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / step));
            for (int i = 0; i < n; i++) res.Add(a + (b - a) * ((double)i / n));
        }
        // Pontos repetidos (vértices colados) quebrariam a triangulação.
        var clean = new List<Vec2>();
        foreach (var p in res) if (clean.Count == 0 || clean[^1].DistanceTo(p) > 1e-4) clean.Add(p);
        if (clean.Count > 1 && clean[0].DistanceTo(clean[^1]) < 1e-4) clean.RemoveAt(clean.Count - 1);
        return clean;
    }

    private readonly record struct Tri(int A, int B, int C);

    private static IEnumerable<(int, int)> Edges(Tri t)
    {
        yield return t.A < t.B ? (t.A, t.B) : (t.B, t.A);
        yield return t.B < t.C ? (t.B, t.C) : (t.C, t.B);
        yield return t.A < t.C ? (t.A, t.C) : (t.C, t.A);
    }

    /// <summary>Delaunay incremental (Bowyer–Watson) com grade de triângulos para achar os "ruins" depressa.</summary>
    private static List<Tri> Delaunay(List<Vec2> input)
    {
        var n = input.Count;
        var min = new Vec2(input.Min(p => p.X), input.Min(p => p.Y));
        var max = new Vec2(input.Max(p => p.X), input.Max(p => p.Y));
        var size = Math.Max(max.X - min.X, max.Y - min.Y) + 1;
        var c = (min + max) / 2;
        var pts = input.ToList();
        pts.Add(c + new Vec2(-20 * size, -20 * size));
        pts.Add(c + new Vec2(20 * size, -20 * size));
        pts.Add(c + new Vec2(0, 20 * size));
        var tris = new List<(Tri T, Vec2 Cc, double R2, bool Alive)>();
        (Vec2, double) Circ(int a, int b, int d)
        {
            var A = pts[a]; var B = pts[b]; var C = pts[d];
            var den = 2 * (A.X * (B.Y - C.Y) + B.X * (C.Y - A.Y) + C.X * (A.Y - B.Y));
            if (Math.Abs(den) < 1e-12) return (A, double.PositiveInfinity);
            var a2 = A.X * A.X + A.Y * A.Y; var b2 = B.X * B.X + B.Y * B.Y; var c2 = C.X * C.X + C.Y * C.Y;
            var ux = (a2 * (B.Y - C.Y) + b2 * (C.Y - A.Y) + c2 * (A.Y - B.Y)) / den;
            var uy = (a2 * (C.X - B.X) + b2 * (A.X - C.X) + c2 * (B.X - A.X)) / den;
            var cc = new Vec2(ux, uy);
            var dx = A.X - ux; var dy = A.Y - uy;
            return (cc, dx * dx + dy * dy);
        }
        void Add(int a, int b, int d)
        {
            var (cc, r2) = Circ(a, b, d);
            tris.Add((new Tri(a, b, d), cc, r2, true));
        }
        Add(n, n + 1, n + 2);
        // Ordem espacial (faixas em x) para os triângulos novos ficarem perto: busca começando do fim da lista.
        var order = Enumerable.Range(0, n).OrderBy(i => Math.Floor((input[i].Y - min.Y) / Math.Max(1, size / 60))).ThenBy(i => input[i].X).ToList();
        var bad = new List<int>();
        var boundary = new Dictionary<(int, int), int>();
        foreach (var i in order)
        {
            var p = pts[i];
            bad.Clear();
            for (int t = 0; t < tris.Count; t++)
            {
                var tr = tris[t];
                if (!tr.Alive) continue;
                var dx = p.X - tr.Cc.X; var dy = p.Y - tr.Cc.Y;
                if (dx * dx + dy * dy < tr.R2) bad.Add(t);
            }
            boundary.Clear();
            foreach (var t in bad)
            {
                var tr = tris[t].T;
                foreach (var (a, b) in new[] { (tr.A, tr.B), (tr.B, tr.C), (tr.C, tr.A) })
                {
                    var key = a < b ? (a, b) : (b, a);
                    boundary[key] = boundary.GetValueOrDefault(key) + 1;
                }
                tris[t] = (tris[t].T, tris[t].Cc, tris[t].R2, false);
            }
            foreach (var (e, cnt) in boundary)
                if (cnt == 1) Add(e.Item1, e.Item2, i);
            // Compacta de tempos em tempos.
            if (tris.Count > 4 * n + 64 && tris.Count(x => !x.Alive) > tris.Count / 2) tris.RemoveAll(x => !x.Alive);
        }
        return tris.Where(t => t.Alive && t.T.A < n && t.T.B < n && t.T.C < n).Select(t => t.T).ToList();
    }

    /// <summary>
    /// Ajusta a seção do modelo à pista reconhecida: as faixas (tráfego, estacionamento, ciclofaixa, acostamento) de
    /// cada lado são escaladas para a largura medida entre os bordos; com largura variável, as medidas entram como
    /// estacas de largura variável (bordo esquerdo/direito a partir do eixo).
    /// </summary>
    public static RoadSetup Fit(RoadSetup template, FloorRoad road)
    {
        var s = template.Clone();
        var lanes = s.Right.Concat(s.Left).Where(e => e.Tipo is not (TipoElementoSecao.Calcada or TipoElementoSecao.CanteiroFisico)).ToList();
        var current = lanes.Sum(e => e.Largura);
        if (current > 0.5 && road.Width > 1)
        {
            var k = road.Width / current;
            foreach (var e in lanes) e.Largura = Math.Round(e.Largura * k * 100) / 100;
        }
        s.LargurasVariaveis.Clear();
        if (road.VariableWidth)
        {
            // Uma estaca a cada ~10 m onde a largura se afasta da típica (e as vizinhas, para a transição).
            var medianHalf = s.TwoWay && s.Center == CenterTreatment.Canteiro ? s.MedianWidth / 2 : 0;
            var last = double.NegativeInfinity;
            foreach (var (st, w) in road.Widths)
            {
                if (st - last < 10) continue;
                last = st;
                var b = Math.Round((w / 2 + medianHalf) * 100) / 100;
                s.LargurasVariaveis.Add(new PontoLargura { Estaca = Math.Round(st, 2), BordoEsquerdo = b, BordoDireito = b });
            }
        }
        return s;
    }

    private static void Inc(Dictionary<(int, int), int> d, (int, int) k) => d[k] = d.GetValueOrDefault(k) + 1;

    private static double Median(List<double> v)
    {
        v.Sort();
        return v.Count == 0 ? 0 : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    private static List<Vec2> Chain(List<List<Vec2>> parts, double tol)
    {
        var rest = parts.Select(p => p.ToList()).ToList();
        var chain = rest[0];
        rest.RemoveAt(0);
        while (rest.Count > 0)
        {
            var best = -1;
            var bestD = double.MaxValue;
            var mode = 0;
            for (int k = 0; k < rest.Count; k++)
            {
                var r = rest[k];
                var cands = new[] { chain[^1].DistanceTo(r[0]), chain[^1].DistanceTo(r[^1]), chain[0].DistanceTo(r[^1]), chain[0].DistanceTo(r[0]) };
                for (int m = 0; m < 4; m++) if (cands[m] < bestD) { bestD = cands[m]; best = k; mode = m; }
            }
            var p = rest[best];
            rest.RemoveAt(best);
            chain = mode switch
            {
                0 => chain.Concat(p.Skip(1)).ToList(),
                1 => chain.Concat(Enumerable.Reverse(p).Skip(1)).ToList(),
                2 => p.Concat(chain.Skip(1)).ToList(),
                _ => Enumerable.Reverse(p).Concat(chain.Skip(1)).ToList(),
            };
        }
        return chain;
    }

    /// <summary>Distância, na direção dada, até sair do piso (m; máx. 80).</summary>
    private static double Ray(Vec2 p, Vec2 dir, List<Polygon2> polys)
    {
        bool In(Vec2 q) => polys.Any(pl => pl.Contains(q));
        if (!In(p)) return 0;
        var lo = 0.0;
        var hi = 0.25;
        while (hi < 80 && In(p + dir * hi)) { lo = hi; hi *= 1.6; }
        for (int k = 0; k < 20; k++)
        {
            var m = (lo + hi) / 2;
            if (In(p + dir * m)) lo = m; else hi = m;
        }
        return lo;
    }

    /// <summary>Largura de bordo a bordo, perpendicular ao eixo na estaca.</summary>
    public static double CrossWidth(Polyline2 axis, double s, List<Polygon2> polys)
    {
        var p = axis.PointAt(s);
        var d = axis.TangentAt(s);
        var n = new Vec2(-d.Y, d.X);
        return Ray(p, n, polys) + Ray(p, n * -1, polys);
    }

    private static List<Vec2> Recenter(List<Vec2> pts, List<Polygon2> polys, List<Vec2> junctions)
    {
        if (pts.Count < 3) return pts;
        var line = new Polyline2(pts);
        var res = new List<Vec2> { pts[0] };
        // Pontos a cada ~2 m (curvas bem descritas), mantendo os extremos.
        var step = Math.Max(2, line.Length / 400);
        for (var s = step; s < line.Length - step / 2; s += step)
        {
            var p = line.PointAt(s);
            if (junctions.Any(j => j.DistanceTo(p) < 15)) { res.Add(p); continue; }
            var d = line.TangentAt(s);
            var n = new Vec2(-d.Y, d.X);
            var l = Ray(p, n, polys);
            var r = Ray(p, n * -1, polys);
            res.Add(l > 0 && r > 0 ? p + n * ((l - r) / 2) : p);
        }
        res.Add(pts[^1]);
        return Simplify(res, 0.05);
    }

    /// <summary>Remove os pontos da ponta livre enquanto <paramref name="narrow"/> (fica ao menos um segmento).</summary>
    private static List<Vec2> TrimEnd(List<Vec2> pts, bool atStart, List<Vec2> junctions, Func<Vec2, bool> narrow)
    {
        var l = pts.ToList();
        if (junctions.Any(j => j.DistanceTo(atStart ? l[0] : l[^1]) < 0.5)) return l;
        // Pontos densos para cortar com precisão.
        var line = new Polyline2(l);
        var dense = new List<Vec2>();
        for (var s = 0.0; s < line.Length; s += 0.5) dense.Add(line.PointAt(s));
        dense.Add(line.PointAt(line.Length));
        var a = 0;
        var b = dense.Count - 1;
        if (atStart) while (a < b - 2 && narrow(dense[a])) a++;
        else while (b > a + 2 && narrow(dense[b])) b--;
        if (atStart && a == 0 || !atStart && b == dense.Count - 1) return l;
        var cut = atStart ? line.Project(dense[a]).Station : line.Project(dense[b]).Station;
        var sub = atStart ? line.SubPoints(cut, line.Length) : line.SubPoints(0, cut);
        return sub.Count >= 2 ? sub.ToList() : l;
    }

    /// <summary>Prolonga a ponta livre do eixo, na direção da ponta, até o bordo do piso.</summary>
    private static List<Vec2> Extend(List<Vec2> pts, List<Polygon2> polys, bool atStart, List<Vec2> junctions)
    {
        var p = atStart ? pts[0] : pts[^1];
        if (junctions.Any(j => j.DistanceTo(p) < 0.5)) return pts;
        var line = new Polyline2(pts);
        var back = Math.Min(6, line.Length * 0.3);
        var q = atStart ? line.PointAt(back) : line.PointAt(line.Length - back);
        var dir = (p - q);
        if (dir.Length < 1e-6) return pts;
        dir = dir.Normalized();
        bool In(Vec2 x) => polys.Any(pl => pl.Contains(x));
        if (!In(p)) return pts;
        var s = 0.0;
        while (s < 60 && In(p + dir * (s + 0.25))) s += 0.25;
        if (s < 0.3) return pts;
        var np = p + dir * s;
        return atStart ? new[] { np }.Concat(pts).ToList() : pts.Concat(new[] { np }).ToList();
    }

    /// <summary>Douglas–Peucker.</summary>
    public static List<Vec2> Simplify(List<Vec2> pts, double tol)
    {
        if (pts.Count < 3) return pts.ToList();
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            var best = -1;
            var bestD = tol;
            for (int k = a + 1; k < b; k++)
            {
                var d = SegDist(pts[k], pts[a], pts[b]);
                if (d > bestD) { bestD = d; best = k; }
            }
            if (best < 0) continue;
            keep[best] = true;
            stack.Push((a, best));
            stack.Push((best, b));
        }
        return pts.Where((_, k) => keep[k]).ToList();
    }

    private static double SegDist(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var t = ab.Length < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / ab.Dot(ab), 0, 1);
        return p.DistanceTo(a + ab * t);
    }
}
