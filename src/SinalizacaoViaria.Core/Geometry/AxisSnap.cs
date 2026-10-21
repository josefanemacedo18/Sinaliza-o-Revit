namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Deixa exatas as retas de contorno que o Revit avisaria com "A linha no croqui está ligeiramente fora da linha central".
/// Pela ajuda do Revit 2027 ("Element Is Slightly Off Axis"), o aviso vale para retas desalinhadas de 1×10⁻⁹ a 0,2° da
/// horizontal, da vertical ou das diagonais de 45°. Aparecem por resíduo dos recortes (frações de milímetro, Autoteste de
/// 2026-10-19) e pelo ajuste do contorno em retas e arcos, que desloca as pontas das retas dentro da tolerância do ajuste
/// (1 a 4 mm em retas de 15 m nas rotatórias, Autoteste completo de 2026-10-20). Só mexe quando nenhum vértice anda mais
/// que o limite dado: retas inclinadas pelo projeto ficam como estão.
/// </summary>
public static class AxisSnap
{
    /// <summary>Deslocamento máximo padrão de um vértice (m): 0,5 mm – os resíduos de recorte chegam a 0,17 mm.</summary>
    public const double MaxOffset = 5e-4;

    /// <summary>Desalinhamento máximo (°) que o Revit avisa – acima disso a reta é inclinada de propósito e não é mexida.</summary>
    public const double MaxAngleDeg = 0.2;

    /// <summary>Direções que o Revit confere (°): horizontal, diagonais e vertical.</summary>
    public static readonly double[] Directions = { 0, 45, 90, 135 };

    /// <summary>
    /// Contorno fechado (o fim de cada trecho é o início do seguinte) com as retas quase alinhadas a uma das
    /// <see cref="Directions"/> exatamente alinhadas. Vértices ligados por retas quase paralelas a uma direção ficam todos na
    /// mesma reta dessa direção (a da reta mais longa do grupo); numa quina entre duas direções, o vértice vai ao encontro
    /// das duas. A ponta de um arco desliza pela reta alinhada até a circunferência dele (ou, longe dela, o arco fica com o mesmo
    /// raio). Grupo cujo ajuste passaria de <paramref name="maxOffset"/> num vértice não é
    /// mexido. Repete até 3 vezes (um ajuste pode inclinar uma reta vizinha curta para dentro da janela). Um ajuste que faria
    /// um contorno válido se tocar (peça estreita, como o nariz de um meio-fio) não é aplicado.
    /// </summary>
    public static List<BoundarySegment> Snap(IReadOnlyList<BoundarySegment> ring, double maxOffset = MaxOffset, double maxAngleDeg = MaxAngleDeg)
    {
        var cur = ring.ToList();
        bool? inputSimple = null;
        for (var pass = 0; pass < 3; pass++)
        {
            var next = SnapOnce(cur, maxOffset, maxAngleDeg, true, out var changed);
            if (!changed) break;
            if (!Simple(next) && (inputSimple ??= Simple(ring))) break;
            cur = next;
        }
        return cur;
    }

    private static bool Simple(IReadOnlyList<BoundarySegment> ring) => BoundaryFit.IsSimple(new[] { ring }, 1e-5);

    /// <summary>Mesmo ajuste de <see cref="Snap"/> para um anel de pontos (só retas): devolve os vértices ajustados.</summary>
    public static List<Vec2> SnapRing(IReadOnlyList<Vec2> ring, double maxOffset = MaxOffset, double maxAngleDeg = MaxAngleDeg)
    {
        if (ring.Count < 3) return ring.ToList();
        var segs = ring.Select((v, i) => new BoundarySegment(v, ring[(i + 1) % ring.Count])).ToList();
        return Snap(segs, maxOffset, maxAngleDeg).Select(s => s.Start).ToList();
    }

    /// <summary>
    /// Mesmo ajuste para uma linha aberta (eixo de via, caminho): as pontas ficam presas só às próprias retas. Serve para os
    /// eixos lidos de pisos ou de linhas, que saem com resíduo de micrômetros nas pontas e virariam linhas de modelo e
    /// contornos que o Revit avisa.
    /// </summary>
    public static List<Vec2> SnapPath(IReadOnlyList<Vec2> path, double maxOffset = MaxOffset, double maxAngleDeg = MaxAngleDeg)
    {
        if (path.Count < 2) return path.ToList();
        var segs = path.Select((v, i) => new BoundarySegment(v, path[(i + 1) % path.Count])).ToList();
        for (var pass = 0; pass < 3; pass++)
        {
            segs = SnapOnce(segs, maxOffset, maxAngleDeg, false, out var changed);
            if (!changed) break;
        }
        return segs.Select(s => s.Start).ToList();
    }

    /// <summary>Retas que o Revit avisaria: desalinhadas de <paramref name="exactDeg"/> a <paramref name="maxAngleDeg"/> de uma das <see cref="Directions"/>.</summary>
    public static int CountNearAxis(IEnumerable<BoundarySegment> ring, double maxAngleDeg = MaxAngleDeg, double exactDeg = 1e-9) =>
        ring.Count(s => OffAxisDeg(s) is { } off && off > exactDeg && off <= maxAngleDeg);

    /// <summary>Desalinhamento (°) da reta em relação à direção conferida mais próxima; nulo em arcos e trechos nulos.</summary>
    public static double? OffAxisDeg(BoundarySegment s)
    {
        if (s.IsArc) return null;
        var d = s.End - s.Start;
        if (d.Length < 1e-12) return null;
        var a = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
        a = (a % 45 + 45) % 45;
        return Math.Min(a, 45 - a);
    }

    private static List<BoundarySegment> SnapOnce(List<BoundarySegment> ring, double maxOffset, double maxAngleDeg, bool closed, out bool changed)
    {
        changed = false;
        var n = ring.Count;
        if (n < (closed ? 3 : 2)) return ring;
        var v = ring.Select(s => s.Start).ToArray();
        // Restrições de cada vértice: normal da direção e valor (n·p) da reta alvo do grupo.
        var cons = new List<(Vec2 N, double D)>[n];
        var h = Math.Sqrt(0.5);
        // Direções exatas (sem o resíduo de cos/sin): uma reta já alinhada não pode andar nem um fio.
        foreach (var u in new[] { new Vec2(1, 0), new Vec2(h, h), new Vec2(0, 1), new Vec2(-h, h) })
        {
            var nrm = new Vec2(-u.Y, u.X);
            var g = new Groups(n);
            for (var i = 0; i < n; i++)
            {
                if (ring[i].IsArc || !closed && i == n - 1) continue;
                var j = (i + 1) % n;
                var d = v[j] - v[i];
                var len = d.Length;
                if (len < 1e-12) continue;
                var sin = Math.Abs(d.Dot(nrm)) / len;
                var off = Math.Asin(Math.Min(1, sin)) * 180 / Math.PI;
                // As retas já exatas entram também: o vértice de uma quina entre uma reta exata e outra que vai ser alinhada
                // fica no encontro das duas – antes andava só na direção da segunda e entortava a primeira (zebrados a 45°).
                if (Math.Abs(d.Dot(u)) > 0 && off <= maxAngleDeg) g.Union(i, j, i, len);
            }
            if (g.Segments.Count == 0) continue;
            var target = new Dictionary<int, (double Len, double Value)>();
            foreach (var (seg, len) in g.Segments)
            {
                var root = g.Find(seg);
                var mid = (v[seg] + v[(seg + 1) % n]) * 0.5;
                if (!target.TryGetValue(root, out var t) || len > t.Len) target[root] = (len, mid.Dot(nrm));
            }
            var ok = target.Keys.ToDictionary(r => r, _ => true);
            for (var i = 0; i < n; i++)
                if (g.InGroup(i) && Math.Abs(v[i].Dot(nrm) - target[g.Find(i)].Value) > maxOffset) ok[g.Find(i)] = false;
            for (var i = 0; i < n; i++)
                if (g.InGroup(i) && ok[g.Find(i)]) (cons[i] ??= new()).Add((nrm, target[g.Find(i)].Value));
        }
        var res = new Vec2[n];
        for (var i = 0; i < n; i++)
        {
            res[i] = v[i];
            if (cons[i] is not { Count: > 0 } c) continue;
            Vec2 p;
            if (c.Count == 1)
            {
                p = v[i] + c[0].N * (c[0].D - v[i].Dot(c[0].N));
                // Ponta de arco: desliza pela reta alinhada até a circunferência dele (o arco fica com o mesmo centro e raio; o
                // piso vizinho com o mesmo arco continua encostado). Reta quase tangente (encontro longe) fica como antes.
                BoundarySegment? arc = ring[i].IsArc ? ring[i] : closed || i > 0 ? ring[(i - 1 + n) % n] : null;
                if (arc is { IsArc: true } a && (!ring[i].IsArc || !ring[(i - 1 + n) % n].IsArc) && OnCircle(a.Circle, c[0].N, c[0].D, p) is { } q
                    && q.DistanceTo(p) <= 4 * p.DistanceTo(v[i]) + 1e-12)
                {
                    var perp = p.DistanceTo(v[i]);
                    if (perp <= maxOffset * 1.5 && q.DistanceTo(v[i]) > 1e-12) { res[i] = q; changed = true; }
                    continue;
                }
            }
            else
            {
                // Quina entre duas direções: o encontro das duas retas alvo.
                var (n1, d1) = c[0];
                var (n2, d2) = c[1];
                var det = n1.X * n2.Y - n1.Y * n2.X;
                if (Math.Abs(det) < 1e-9) p = v[i] + n1 * (d1 - v[i].Dot(n1));
                else p = new Vec2((d1 * n2.Y - n1.Y * d2) / det, (n1.X * d2 - d1 * n2.X) / det);
            }
            // Numa quina de 45° o encontro pode andar até √2 × o limite; além disso, não mexe.
            var moved = p.DistanceTo(v[i]);
            if (moved <= maxOffset * 1.5 && moved > 1e-12) { res[i] = p; changed = true; }
        }
        var outRing = new List<BoundarySegment>(n);
        for (var i = 0; i < n; i++)
        {
            var s = ring[i];
            var (a, b) = (res[i], res[(i + 1) % n]);
            outRing.Add(s.Mid is { } m && (a != s.Start || b != s.End) ? Moved(s, m, a, b) : new BoundarySegment(a, b, s.Mid));
        }
        return outRing;
    }

    /// <summary>Encontro da reta n·x = d com a circunferência, o mais perto de <paramref name="near"/>.</summary>
    private static Vec2? OnCircle((Vec2 Center, double Radius) c, Vec2 nrm, double d, Vec2 near)
    {
        var u = new Vec2(nrm.Y, -nrm.X);
        var x0 = nrm * d - c.Center;
        var b = u.Dot(x0);
        var disc = b * b - (x0.Dot(x0) - c.Radius * c.Radius);
        if (disc < 0) return null;
        var sq = Math.Sqrt(disc);
        var (q1, q2) = (c.Center + x0 + u * (-b + sq), c.Center + x0 + u * (-b - sq));
        return q1.DistanceTo(near) <= q2.DistanceTo(near) ? q1 : q2;
    }

    /// <summary>
    /// Arco com as pontas movidas pelo alinhamento das retas vizinhas: o mesmo raio, com o centro andando o mínimo para passar
    /// pelas pontas novas. Antes o arco era refeito pelas pontas novas e pelo meio antigo – 3 mm numa ponta tiravam o centro
    /// 3 cm do lugar (arco curto de raio grande) e o piso vizinho, com o mesmo arco intacto, ficava sobreposto ao longo da curva.
    /// </summary>
    private static BoundarySegment Moved(BoundarySegment s, Vec2 m, Vec2 a, Vec2 b)
    {
        var (c, r) = s.Circle;
        var moved = Math.Max(a.DistanceTo(s.Start), b.DistanceTo(s.End));
        var half = a.DistanceTo(b) / 2;
        var mid = (a + b) * 0.5;
        var w = (b - a).Normalized().PerpLeft;
        // Centro sobre a mediatriz das pontas novas: com o raio de antes (o mais perto do centro antigo) ou, se isso o
        // levaria longe (arco quase reto), o ponto da mediatriz mais perto do centro antigo.
        var k = (c - mid).Dot(w);
        var nc = mid + w * k;
        if (r > half)
        {
            var h = Math.Sqrt(r * r - half * half) * (k >= 0 ? 1 : -1);
            var keep = mid + w * h;
            if (keep.DistanceTo(c) <= 3 * moved + 1e-9) nc = keep;
        }
        var nr = a.DistanceTo(nc);
        var sw = BoundaryFit.Sweep(c, s.Start, m, s.End);
        var a0 = Math.Atan2(a.Y - nc.Y, a.X - nc.X);
        var d = Math.Atan2(b.Y - nc.Y, b.X - nc.X) - a0;
        while (d - sw > Math.PI) d -= 2 * Math.PI;
        while (sw - d > Math.PI) d += 2 * Math.PI;
        return Math.Sign(d) == Math.Sign(sw) ? new BoundarySegment(a, b, nc + Vec2.FromAngle(a0 + d / 2) * nr) : new BoundarySegment(a, b, m);
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
