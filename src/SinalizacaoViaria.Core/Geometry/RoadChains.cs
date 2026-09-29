namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Separa um conjunto de linhas desenhadas (o "traçado" de uma malha viária) em vias: linhas encadeadas pela ponta
/// formam uma via só quando o encontro é só delas duas (curva/deflexão) ou, num nó com várias linhas, quando seguem
/// alinhadas (a rua que atravessa o cruzamento); as demais viram vias próprias – e as interseções nascem nos encontros.
/// </summary>
public static class RoadChains
{
    /// <param name="curves">Pontos de cada linha (na ordem do desenho).</param>
    /// <param name="tolerance">Distância para considerar duas pontas no mesmo nó (m).</param>
    /// <param name="straightDeg">Desvio máximo para seguir reto num nó com três ou mais linhas (graus).</param>
    /// <returns>Grupos de índices das linhas – cada grupo é uma via.</returns>
    public static List<List<int>> Split(IReadOnlyList<IReadOnlyList<Vec2>> curves, double tolerance = 0.05, double straightDeg = 25)
    {
        var n = curves.Count;
        var valid = Enumerable.Range(0, n).Where(i => curves[i].Count >= 2 && Length(curves[i]) > 1e-6).ToList();
        // Nós: pontas agrupadas por proximidade.
        var nodes = new List<Vec2>();
        int NodeOf(Vec2 p)
        {
            for (int k = 0; k < nodes.Count; k++) if (nodes[k].DistanceTo(p) <= tolerance) return k;
            nodes.Add(p);
            return nodes.Count - 1;
        }
        var endNode = new Dictionary<(int Curve, bool End), int>();
        foreach (var i in valid)
        {
            endNode[(i, false)] = NodeOf(curves[i][0]);
            endNode[(i, true)] = NodeOf(curves[i][^1]);
        }
        // Direção de cada linha saindo do nó pela ponta indicada.
        Vec2 OutDir(int i, bool end)
        {
            var c = curves[i];
            var p = end ? c[^1] : c[0];
            var q = end ? PointFrom(c, true, 3) : PointFrom(c, false, 3);
            return (q - p).Normalized();
        }
        // Ligações: em cada nó, pares de pontas que continuam a mesma via.
        var link = new Dictionary<(int, bool), (int, bool)>();
        foreach (var grp in endNode.GroupBy(x => x.Value))
        {
            var ends = grp.Select(x => x.Key).ToList();
            if (ends.Count == 2)
            {
                if (ends[0].Curve == ends[1].Curve) continue;          // laço fechado de uma linha só
                link[ends[0]] = ends[1];
                link[ends[1]] = ends[0];
                continue;
            }
            // Três ou mais: pares mais alinhados primeiro (desvio pequeno), cada ponta num par só.
            var cos = Math.Cos((180 - straightDeg) * Math.PI / 180);
            var pairs = new List<(double Dot, (int, bool) A, (int, bool) B)>();
            for (int a = 0; a < ends.Count; a++)
                for (int b = a + 1; b < ends.Count; b++)
                {
                    if (ends[a].Curve == ends[b].Curve) continue;
                    var dot = OutDir(ends[a].Curve, ends[a].End).Dot(OutDir(ends[b].Curve, ends[b].End));
                    if (dot <= cos) pairs.Add((dot, ends[a], ends[b]));
                }
            var used = new HashSet<(int, bool)>();
            foreach (var (_, a, b) in pairs.OrderBy(p => p.Dot))
            {
                if (used.Contains(a) || used.Contains(b)) continue;
                used.Add(a);
                used.Add(b);
                link[a] = b;
                link[b] = a;
            }
        }
        // Componentes: segue as ligações pelas duas pontas de cada linha.
        var seen = new HashSet<int>();
        var res = new List<List<int>>();
        foreach (var start in valid)
        {
            if (seen.Contains(start)) continue;
            var group = new List<int>();
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                if (!seen.Add(i)) continue;
                group.Add(i);
                foreach (var e in new[] { false, true })
                    if (link.TryGetValue((i, e), out var o) && !seen.Contains(o.Item1)) stack.Push(o.Item1);
            }
            res.Add(Order(group, link));
        }
        return res;
    }

    /// <summary>Linhas do grupo na ordem do encadeamento (começa por uma ponta livre, quando há).</summary>
    private static List<int> Order(List<int> group, Dictionary<(int, bool), (int, bool)> link)
    {
        if (group.Count <= 1) return group;
        var set = group.ToHashSet();
        var first = group.FirstOrDefault(i => !link.ContainsKey((i, false)) || !link.ContainsKey((i, true)), group[0]);
        var order = new List<int> { first };
        var cur = first;
        var cameFrom = link.ContainsKey((first, false)) && !link.ContainsKey((first, true)) ? true : false;
        var seen = new HashSet<int> { first };
        while (true)
        {
            // Sai pela ponta oposta à de chegada.
            var exit = (cur, !cameFrom);
            if (!link.TryGetValue(exit, out var nxt) || !set.Contains(nxt.Item1) || !seen.Add(nxt.Item1))
            {
                exit = (cur, cameFrom);
                if (!link.TryGetValue(exit, out nxt) || !set.Contains(nxt.Item1) || !seen.Add(nxt.Item1)) break;
            }
            order.Add(nxt.Item1);
            cur = nxt.Item1;
            cameFrom = nxt.Item2;
        }
        foreach (var i in group) if (!order.Contains(i)) order.Add(i);
        return order;
    }

    private static double Length(IReadOnlyList<Vec2> c)
    {
        var l = 0.0;
        for (int i = 0; i + 1 < c.Count; i++) l += c[i].DistanceTo(c[i + 1]);
        return l;
    }

    /// <summary>Ponto a <paramref name="d"/> m da ponta (ou o outro extremo, se a linha for curta).</summary>
    private static Vec2 PointFrom(IReadOnlyList<Vec2> c, bool fromEnd, double d)
    {
        var pts = fromEnd ? c.Reverse().ToList() : c.ToList();
        var acc = 0.0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var seg = pts[i].DistanceTo(pts[i + 1]);
            if (acc + seg >= d && seg > 1e-9) return pts[i] + (pts[i + 1] - pts[i]) * ((d - acc) / seg);
            acc += seg;
        }
        return pts[^1];
    }
}
