using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Triangulação de Delaunay (Bowyer–Watson incremental com vizinhança) – a mesma regra que o Toposolid do Revit usa para ligar
/// os pontos da superfície. Serve para simular o terreno que o Revit vai montar com os pontos da terraplenagem (prévias e
/// testes): se a triangulação dos pontos reproduz o projeto, o Toposolid também reproduz.
/// </summary>
public static class Tin
{
    private struct Tri
    {
        public int A, B, C;          // vértices (anti-horário)
        public int NA, NB, NC;       // vizinho oposto a A, B, C (-1 = nenhum)
        public bool Dead;
    }

    /// <summary>Triângulos (índices em <paramref name="pts"/>) da triangulação de Delaunay em planta.</summary>
    public static List<(int A, int B, int C)> Triangulate(IReadOnlyList<Vec3> pts)
    {
        var res = new List<(int, int, int)>();
        if (pts.Count < 3) return res;
        double minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y), maxX = pts.Max(p => p.X), maxY = pts.Max(p => p.Y);
        var ox = (minX + maxX) / 2;
        var oy = (minY + maxY) / 2;
        var span = Math.Max(1, Math.Max(maxX - minX, maxY - minY));
        var n = pts.Count;
        var xs = new double[n + 3];
        var ys = new double[n + 3];
        for (int i = 0; i < n; i++) { xs[i] = pts[i].X - ox; ys[i] = pts[i].Y - oy; }
        // Supertriângulo.
        var big = span * 50;
        xs[n] = -big; ys[n] = -big;
        xs[n + 1] = big; ys[n + 1] = -big;
        xs[n + 2] = 0; ys[n + 2] = big;
        var tris = new List<Tri> { new() { A = n, B = n + 1, C = n + 2, NA = -1, NB = -1, NC = -1 } };
        var last = 0;

        double Orient(int a, int b, double px, double py) => (xs[b] - xs[a]) * (py - ys[a]) - (ys[b] - ys[a]) * (px - xs[a]);

        bool InCircle(in Tri t, double px, double py)
        {
            double ax = xs[t.A] - px, ay = ys[t.A] - py, bx = xs[t.B] - px, by = ys[t.B] - py, cx = xs[t.C] - px, cy = ys[t.C] - py;
            var det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
            return det > 1e-12;
        }

        // Ordem de inserção por faixas (localidade): o passeio até o triângulo que contém o ponto fica curto.
        var cell = span / Math.Max(1, Math.Sqrt(n / 4.0));
        var order = Enumerable.Range(0, n).OrderBy(i =>
        {
            var row = (int)Math.Floor((ys[i] + span) / cell);
            var col = (xs[i] + span) / cell;
            return row * 1e7 + (row % 2 == 0 ? col : -col);
        }).ToList();
        var seen = new HashSet<(long, long)>();
        var cavity = new List<int>();
        var inCavity = new HashSet<int>();
        var stack = new Stack<int>();
        var bnd = new List<(int A, int B, int Out)>();

        foreach (var pi in order)
        {
            if (!seen.Add(((long)Math.Round(pts[pi].X * 1000), (long)Math.Round(pts[pi].Y * 1000)))) continue;   // ponto repetido
            double px = xs[pi], py = ys[pi];
            // Localiza o triângulo que contém o ponto.
            var t = last;
            if (tris[t].Dead) t = tris.FindLastIndex(x => !x.Dead);
            for (int guard = 0; guard < tris.Count + 10; guard++)
            {
                var tr = tris[t];
                if (Orient(tr.B, tr.C, px, py) < -1e-12 && tr.NA >= 0) { t = tr.NA; continue; }
                if (Orient(tr.C, tr.A, px, py) < -1e-12 && tr.NB >= 0) { t = tr.NB; continue; }
                if (Orient(tr.A, tr.B, px, py) < -1e-12 && tr.NC >= 0) { t = tr.NC; continue; }
                break;
            }
            // Cavidade: triângulos cujo círculo circunscrito contém o ponto (conexa a partir do que o contém).
            cavity.Clear(); inCavity.Clear(); stack.Clear(); bnd.Clear();
            stack.Push(t); inCavity.Add(t);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                cavity.Add(c);
                var tr = tris[c];
                foreach (var nb in new[] { tr.NA, tr.NB, tr.NC })
                    if (nb >= 0 && !inCavity.Contains(nb) && InCircle(tris[nb], px, py)) { inCavity.Add(nb); stack.Push(nb); }
            }
            foreach (var c in cavity)
            {
                var tr = tris[c];
                if (tr.NA < 0 || !inCavity.Contains(tr.NA)) bnd.Add((tr.B, tr.C, tr.NA));
                if (tr.NB < 0 || !inCavity.Contains(tr.NB)) bnd.Add((tr.C, tr.A, tr.NB));
                if (tr.NC < 0 || !inCavity.Contains(tr.NC)) bnd.Add((tr.A, tr.B, tr.NC));
            }
            foreach (var c in cavity) { var tr = tris[c]; tr.Dead = true; tris[c] = tr; }
            var byStart = new Dictionary<int, int>();
            var byEnd = new Dictionary<int, int>();
            var created = new List<int>();
            foreach (var (a, b, outT) in bnd)
            {
                var id = tris.Count;
                tris.Add(new Tri { A = a, B = b, C = pi, NA = -1, NB = -1, NC = outT });
                created.Add(id);
                byStart[a] = id;
                byEnd[b] = id;
                if (outT >= 0)
                {
                    var o = tris[outT];
                    if (o.NA >= 0 && inCavity.Contains(o.NA) && o.B == b && o.C == a) o.NA = id;
                    else if (o.NB >= 0 && inCavity.Contains(o.NB) && o.C == b && o.A == a) o.NB = id;
                    else if (o.NC >= 0 && inCavity.Contains(o.NC) && o.A == b && o.B == a) o.NC = id;
                    tris[outT] = o;
                }
            }
            foreach (var id in created)
            {
                var tr = tris[id];
                tr.NA = byStart.TryGetValue(tr.B, out var x) ? x : -1;     // aresta B–P
                tr.NB = byEnd.TryGetValue(tr.A, out var y) ? y : -1;       // aresta P–A
                tris[id] = tr;
            }
            last = created.Count > 0 ? created[^1] : last;
        }
        foreach (var tr in tris)
            if (!tr.Dead && tr.A < n && tr.B < n && tr.C < n) res.Add((tr.A, tr.B, tr.C));
        return res;
    }

    /// <summary>Malha de terreno (TIN) dos pontos.</summary>
    public static TerrainMesh Mesh(IReadOnlyList<Vec3> pts)
    {
        var m = new TerrainMesh();
        foreach (var (a, b, c) in Triangulate(pts)) m.Add(pts[a], pts[b], pts[c]);
        return m;
    }
}
