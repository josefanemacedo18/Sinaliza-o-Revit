namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Alinha aos eixos X/Y as retas de contorno que estão alinhadas "quase exatamente" – inclinadas por resíduo numérico das
/// operações de recorte (frações de milímetro), não por projeto. O Revit avisa "A linha no croqui está ligeiramente fora da
/// linha central" para cada uma dessas retas (relatório do Autoteste de 2026-10-19: centenas de avisos nos pisos das
/// interseções). Só mexe quando o deslocamento de cada vértice fica abaixo de <see cref="MaxOffset"/>: retas realmente
/// inclinadas pelo projeto ficam como estão.
/// </summary>
public static class AxisSnap
{
    /// <summary>Deslocamento máximo de um vértice (m): 0,5 mm – os resíduos medidos chegam a 0,17 mm.</summary>
    public const double MaxOffset = 5e-4;

    /// <summary>Inclinação máxima (°) para uma reta contar como "quase alinhada" (retas curtas com 0,5 mm de desvio ficam de fora).</summary>
    public const double MaxAngleDeg = 1.0;

    /// <summary>
    /// Contorno fechado (o fim de cada trecho é o início do seguinte) com as retas quase alinhadas exatamente horizontais ou
    /// verticais. Vértices ligados por retas quase horizontais recebem todos a mesma cota Y (a da reta mais longa do grupo);
    /// idem X para as quase verticais. Arcos mantêm o ponto do meio. Grupo cujo ajuste passaria de
    /// <paramref name="maxOffset"/> num vértice não é mexido.
    /// </summary>
    public static List<BoundarySegment> Snap(IReadOnlyList<BoundarySegment> ring, double maxOffset = MaxOffset, double maxAngleDeg = MaxAngleDeg)
    {
        var n = ring.Count;
        if (n < 3) return ring.ToList();
        var v = ring.Select(s => s.Start).ToArray();
        var tan = Math.Tan(maxAngleDeg * Math.PI / 180);
        var gy = new Groups(n);
        var gx = new Groups(n);
        for (var i = 0; i < n; i++)
        {
            if (ring[i].IsArc) continue;
            var j = (i + 1) % n;
            var d = v[j] - v[i];
            var ax = Math.Abs(d.X);
            var ay = Math.Abs(d.Y);
            if (ax > 1e-12 && ay <= maxOffset && ay <= tan * ax) gy.Union(i, j, i, ax);
            if (ay > 1e-12 && ax <= maxOffset && ax <= tan * ay) gx.Union(i, j, i, ay);
        }
        var y = v.Select(p => p.Y).ToArray();
        var x = v.Select(p => p.X).ToArray();
        Apply(gy, n, i => (v[i].Y + v[(i + 1) % n].Y) / 2, y, maxOffset);
        Apply(gx, n, i => (v[i].X + v[(i + 1) % n].X) / 2, x, maxOffset);
        var res = new List<BoundarySegment>(n);
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            res.Add(new BoundarySegment(new Vec2(x[i], y[i]), new Vec2(x[j], y[j]), ring[i].Mid));
        }
        return res;
    }

    /// <summary>Retas do contorno inclinadas menos que <paramref name="maxAngleDeg"/> em relação a X ou Y, mas não alinhadas.</summary>
    public static int CountNearAxis(IEnumerable<BoundarySegment> ring, double maxAngleDeg = MaxAngleDeg, double exact = 1e-9)
    {
        var tan = Math.Tan(maxAngleDeg * Math.PI / 180);
        var count = 0;
        foreach (var s in ring)
        {
            if (s.IsArc) continue;
            var d = s.End - s.Start;
            var ax = Math.Abs(d.X);
            var ay = Math.Abs(d.Y);
            var lo = Math.Min(ax, ay);
            var hi = Math.Max(ax, ay);
            if (hi < 1e-9) continue;
            if (lo / hi > exact && lo <= tan * hi) count++;
        }
        return count;
    }

    /// <summary>Leva cada grupo à coordenada da reta mais longa dele (a reta longa e exata continua exata).</summary>
    private static void Apply(Groups g, int n, Func<int, double> coordOfSegment, double[] coord, double maxOffset)
    {
        var target = new Dictionary<int, (double Len, double Value)>();
        foreach (var (seg, len) in g.Segments)
        {
            var root = g.Find(seg);
            if (!target.TryGetValue(root, out var t) || len > t.Len) target[root] = (len, coordOfSegment(seg));
        }
        var ok = target.Keys.ToDictionary(r => r, r => true);
        for (var i = 0; i < n; i++)
            if (g.InGroup(i) && Math.Abs(coord[i] - target[g.Find(i)].Value) > maxOffset) ok[g.Find(i)] = false;
        for (var i = 0; i < n; i++)
            if (g.InGroup(i) && ok[g.Find(i)]) coord[i] = target[g.Find(i)].Value;
    }

    /// <summary>União de vértices ligados por retas quase alinhadas (e as retas de cada grupo, com o comprimento).</summary>
    private sealed class Groups(int n)
    {
        private readonly int[] _parent = Enumerable.Range(0, n).ToArray();
        private readonly bool[] _member = new bool[n];
        public List<(int Seg, double Len)> Segments { get; } = new();

        public int Find(int i)
        {
            while (_parent[i] != i) i = _parent[i] = _parent[_parent[i]];
            return i;
        }

        public bool InGroup(int i) => _member[i];

        public void Union(int a, int b, int seg, double len)
        {
            _member[a] = _member[b] = true;
            Segments.Add((seg, len));
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) _parent[ra] = rb;
        }
    }
}
