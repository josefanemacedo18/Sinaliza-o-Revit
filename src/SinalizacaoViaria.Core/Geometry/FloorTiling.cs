using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Retalhamento COMUM dos pisos sobre o greide: cada via tem uma única malha de juntas – estacas transversais só onde a
/// superfície deixa de caber num plano (tolerância de 2 cm) e as linhas de quebra da seção (crista do abaulamento e bordas da
/// pista) – e TODOS os pisos dela (pista, sarjeta, meio-fio, calçada, grama e pintura) são recortados por essa malha e usam o
/// plano da célula em que estão. O nó usa a malha da via principal (a superfície dele é a dela); na área do nó a via
/// secundária também. As células vizinhas têm os mesmos vértices, na superfície: as faces se encontram sem degrau, dentro de
/// um piso e entre pisos diferentes, e a pintura fica sobre o pavimento com folga constante. Onde a superfície é torcida
/// (concordância das secundárias) a célula vira dois triângulos planos; na borda do nó, a primeira junta da secundária pega
/// as cotas das faces da principal (e os vincos delas) – sem degrau na emenda. Antes cada piso era dividido por conta própria:
/// juntas desencontradas, degraus de até 4 cm entre vizinhos e pintura afundada no pavimento.
/// </summary>
public sealed class FloorTiling
{
    /// <summary>Nó em que a via é secundária: estação do nó no eixo dela e alcance (até ele a via segue a superfície do nó).</summary>
    public sealed record Zone(string NodeId, double Station, double Start);

    /// <summary>
    /// Via: identificador, superfície do greide (com as concordâncias), cota da base (m), larguras da seção à esquerda e à direita
    /// do eixo (m) e os nós em que ela é secundária.
    /// </summary>
    public sealed record Road(string Id, GradeSurface Surface, double BaseZ, double Left, double Right, IReadOnlyList<Zone> Zones);

    /// <summary>Nó: via principal (a da superfície do nó) e as vias ligadas.</summary>
    public sealed record Node(string MajorId, IReadOnlyList<string> Roads);

    /// <summary>Faixas além das pontas do eixo (m): a superfície segue a rampa final.</summary>
    public const double Extension = 60;
    /// <summary>Menor distância entre juntas (m).</summary>
    public const double MinStrip = 0.5;
    /// <summary>Quatro cantos a menos que isto de um plano: a célula fica inteira (m).</summary>
    public const double Coplanar = 0.0005;
    /// <summary>Diferença máxima entre faces vizinhas ao longo de uma linha de quebra curva (m).</summary>
    public const double SeamTol = 0.001;

    private readonly Func<string, Road?> _road;
    private readonly Func<string, Node?> _node;
    private readonly Dictionary<string, Frame?> _frames = new();

    public double Tolerance { get; }

    /// <param name="road">Dados da via pelo identificador (nulo = via sem greide: fica fora do retalhamento).</param>
    /// <param name="node">Dados do nó pelo identificador (nulo = sem nó).</param>
    public FloorTiling(Func<string, Road?> road, Func<string, Node?> node, double tolerance = 0.02)
    {
        _road = road;
        _node = node;
        Tolerance = tolerance;
    }

    /// <summary>Retalhamento de um conjunto fixo de vias e nós.</summary>
    public FloorTiling(IEnumerable<Road> roads, IReadOnlyDictionary<string, Node> nodes, double tolerance = 0.02)
        : this(Lookup(roads), id => nodes.TryGetValue(id, out var n) ? n : null, tolerance) { }

    private static Func<string, Road?> Lookup(IEnumerable<Road> roads)
    {
        var d = roads.ToDictionary(r => r.Id);
        return id => d.TryGetValue(id, out var r) ? r : null;
    }

    /// <summary>Estacas das juntas da via (início e fim de cada faixa, com as faixas além das pontas).</summary>
    public IReadOnlyList<double> Joints(string roadId) =>
        FrameOf(roadId) is { } f ? f.Strips.Select(s => s.S0).Append(f.Strips[^1].S1).ToList() : Array.Empty<double>();

    /// <summary>
    /// Partes planas de um piso de uma via (pista, calçada, linha...): a malha da via e, na área dos nós em que ela é
    /// secundária, a da via principal. Nulo = a via não tem greide (fica a divisão própria do piso).
    /// </summary>
    public List<(Polygon2 Part, Plane3 Plane)>? ForRoad(string roadId, Polygon2 shape)
    {
        if (FrameOf(roadId) is not { } f) return null;
        var regions = new List<(Polygon2 Region, Frame Frame)>();
        foreach (var z in f.In.Zones)
            if (_node(z.NodeId) is { } n && FrameOf(n.MajorId) is { } m && !ReferenceEquals(m, f))
                regions.Add((FloorPlanes.Band(f.Surf, z.Station - z.Start, z.Station + z.Start, -f.W, f.W), m));
        return Split(shape, regions, f);
    }

    /// <summary>
    /// Partes planas de um piso de um nó (pavimento do nó, calçadas e guias das esquinas, faixas, rampas): a malha da via
    /// principal e, ao longo de cada secundária além do alcance do nó, a dela. Nulo = nó sem via principal com greide.
    /// </summary>
    public List<(Polygon2 Part, Plane3 Plane)>? ForNode(string nodeId, Polygon2 shape)
    {
        if (_node(nodeId) is not { } n || FrameOf(n.MajorId) is not { } m) return null;
        var regions = new List<(Polygon2 Region, Frame Frame)>();
        foreach (var id in n.Roads.Distinct())
        {
            if (id == n.MajorId || FrameOf(id) is not { } g) continue;
            // Só ao longo da própria via: se a área do nó cobre a ponta dela (ramo que nasce no nó), atrás da ponta é o nó.
            foreach (var z in g.In.Zones.Where(z => z.NodeId == nodeId))
            {
                if (z.Station + z.Start < g.L)
                    regions.Add((FloorPlanes.Band(g.Surf, z.Station + z.Start, g.L + Extension, -g.W, g.W), g));
                if (z.Station - z.Start > 0)
                    regions.Add((FloorPlanes.Band(g.Surf, -Extension, z.Station - z.Start, -g.W, g.W), g));
            }
        }
        return Split(shape, regions, m);
    }

    /// <summary>Cota (m) das faces dos pisos de uma via no ponto (a malha da via; na área dos nós em que ela é secundária, a da principal).</summary>
    public double? RoadZ(string roadId, Vec2 p)
    {
        if (FrameOf(roadId) is not { } f) return null;
        foreach (var z in f.In.Zones)
            if (_node(z.NodeId) is { } n && FrameOf(n.MajorId) is { } m && !ReferenceEquals(m, f))
            {
                var (s, y) = f.Surf.LocateExtended(p);
                if (Math.Abs(s - z.Station) <= z.Start && Math.Abs(y) <= f.W) return FaceZ(m, p);
            }
        return FaceZ(f, p);
    }

    /// <summary>Cota (m) das faces dos pisos de um nó no ponto.</summary>
    public double? NodeZ(string nodeId, Vec2 p)
    {
        if (_node(nodeId) is not { } n || FrameOf(n.MajorId) is not { } m) return null;
        foreach (var id in n.Roads.Distinct())
        {
            if (id == n.MajorId || FrameOf(id) is not { } g) continue;
            var (s, y) = g.Surf.LocateExtended(p);
            if (Math.Abs(y) > g.W) continue;
            if (g.In.Zones.Any(z => z.NodeId == nodeId && (s > z.Station + z.Start && z.Station + z.Start < g.L || s < z.Station - z.Start && z.Station - z.Start > 0)))
                return FaceZ(g, p);
        }
        return FaceZ(m, p);
    }

    /// <summary>Diagnóstico: juntas, vértices e planos das faixas de uma via entre duas estacas.</summary>
    internal string Dump(string roadId, double s0, double s1)
    {
        if (FrameOf(roadId) is not { } f) return "sem malha";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Ys " + string.Join(",", f.Ys.Select(y => y.ToString("0.00"))));
        foreach (var st in f.Strips.Where(x => x.S1 > s0 && x.S0 < s1))
        {
            sb.AppendLine($"trecho {st.S0:0.00}–{st.S1:0.00} início " + string.Join(" ", st.Start.Select(v => $"{v.Y:0.00}:{v.Z:0.000}")) + " | fim " + string.Join(" ", st.End.Select(v => $"{v.Y:0.00}:{v.Z:0.000}")));
            for (int j = 0; j < st.Cells.Length; j++)
                sb.AppendLine($"   faixa {j}: {st.Cells[j].Planes.Count} plano(s), diagonais " + string.Join(" ", st.Cells[j].Diags.Select(d => $"{d.P}->{d.Q}")));
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ malha de uma via

    private sealed class Frame
    {
        public required Road In { get; init; }
        public GradeSurface Surf => In.Surface;
        public double L;
        /// <summary>Linhas das faixas: extremos da seção e as quebras; a primeira e a última faixa seguem para fora sem limite.</summary>
        public double[] Ys = Array.Empty<double>();
        public readonly List<Strip> Strips = new();
        public int State;
        public double W => Math.Max(In.Left, In.Right) + 3;
        public int Bands => Ys.Length - 1;

        public Vec2 P(double s, double y) => FloorPlanes.AxisPoint(Surf, s, y);

        /// <summary>Cota absoluta da superfície: o greide (com as concordâncias) no eixo; além das pontas, a rampa final prolongada.</summary>
        public double Z(Vec2 p)
        {
            var (s, _) = Surf.LocateExtended(p);
            if (s >= 0 && s <= L) return In.BaseZ + Surf.Z(p);
            return NodeSurface.LegZ(new NodeSurface.Leg(Surf, In.BaseZ, Math.Max(1, Surf.Grade.CrossfallWidth)), p, out _);
        }

        public Strip StripAt(double s)
        {
            int lo = 0, hi = Strips.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (Strips[mid].S0 <= s) lo = mid; else hi = mid - 1;
            }
            return Strips[lo];
        }

        public int BandAt(double y)
        {
            var j = 0;
            for (int k = 1; k < Ys.Length - 1; k++) if (y > Ys[k]) j = k;
            return j;
        }
    }

    private sealed class Strip
    {
        public double S0, S1;
        public Cell[] Cells = Array.Empty<Cell>();
        public List<(double Y, double Z)> Start = new(), End = new();
    }

    /// <summary>Célula (faixa × trecho entre juntas): planos separados por diagonais retas, da linha de baixo para a de cima.</summary>
    private sealed class Cell
    {
        public readonly List<(Vec2 P, Vec2 Q)> Diags = new();
        public readonly List<Plane3> Planes = new();
        /// <summary>Diagonal do canto de baixo no início ao de cima no fim (nulo = célula inteira ou em zíper).</summary>
        public bool? Rising;

        public Plane3 At(Vec2 x)
        {
            var t = 0;
            foreach (var (p, q) in Diags) if ((q - p).Cross(x - p) > 0) t++;
            return Planes[Math.Min(t, Planes.Count - 1)];
        }
    }

    private Frame? FrameOf(string id)
    {
        if (_frames.TryGetValue(id, out var f))
        {
            // Em montagem (ciclo entre nós): usa o que já existe; sem faixas ainda, fica sem retalhamento.
            return f != null && f.Strips.Count > 0 ? f : null;
        }
        if (_road(id) is not { } r || r.Surface.Axis.Points.Count < 2) { _frames[id] = null; return null; }
        f = new Frame { In = r, L = r.Surface.Axis.Length };
        _frames[id] = f;
        try { Build(f); }
        catch (Exception) { f.Strips.Clear(); }
        return f.Strips.Count > 0 ? f : null;
    }

    private void Build(Frame f)
    {
        f.State = 1;
        var g = f.Surf.Grade;
        var cw = g.CrossfallWidth;
        var crown = Math.Abs(g.Crossfall) > 1e-6 || g.Superelevation.Count > 0;
        var left = Math.Max(f.In.Left, (crown ? cw : 0) + 1.5);
        var right = Math.Max(f.In.Right, (crown ? cw : 0) + 1.5);
        var ys = new List<double> { -right, left };
        if (crown)
            foreach (var b in new[] { 0, cw, -cw })
                if (b > -right + 0.5 && b < left - 0.5 && ys.All(y => Math.Abs(y - b) > 1e-6)) ys.Add(b);
        f.Ys = ys.OrderBy(y => y).ToArray();

        // Juntas obrigatórias: pontas do eixo e limites das áreas dos nós em que a via é secundária.
        var zones = new List<(double A, double B, Frame? Major)>();
        foreach (var z in f.In.Zones)
        {
            var m = _node(z.NodeId) is { } n ? FrameOf(n.MajorId) : null;
            zones.Add((z.Station - z.Start, z.Station + z.Start, ReferenceEquals(m, f) ? null : m));
        }
        var cuts = new SortedSet<double> { -Extension, 0, f.L, f.L + Extension };
        foreach (var (a, b, _) in zones)
        {
            if (a > -Extension && a < f.L + Extension) cuts.Add(a);
            if (b > -Extension && b < f.L + Extension) cuts.Add(b);
        }
        var cl = cuts.ToList();
        for (int i = 0; i + 1 < cl.Count; i++)
        {
            double a = cl[i], b = cl[i + 1];
            if (b - a < 1e-6) continue;
            var mid = (a + b) / 2;
            var inZone = zones.Any(z => mid > z.A && mid < z.B);
            // Na área do nó os pisos usam a malha da principal: um trecho só.
            if (inZone)
            {
                f.Strips.Add(MakeStrip(f, a, b, Line(f, a, null), Line(f, b, null)));
                continue;
            }
            // Borda de um nó: a junta pega as cotas das faces da principal.
            var fromA = zones.Where(z => Math.Abs(z.B - a) < 1e-6).Select(z => z.Major).FirstOrDefault(m => m != null);
            var fromB = zones.Where(z => Math.Abs(z.A - b) < 1e-6).Select(z => z.Major).FirstOrDefault(m => m != null);
            Sweep(f, a, b, fromA, fromB);
        }
        f.State = 2;
    }

    /// <summary>
    /// Juntas entre <paramref name="a"/> e <paramref name="b"/>: cada trecho o mais longo possível dentro da tolerância. Onde a
    /// seção é curva na transversal (a boca de um nó com a principal em curva vertical, que a concordância da secundária leva
    /// adiante), a junta ganha vértices entre as linhas das faixas – os da junta anterior e, se preciso, mais no meio delas –,
    /// enquanto eles forem necessários.
    /// </summary>
    private void Sweep(Frame f, double a, double b, Frame? fromA, Frame? fromB)
    {
        var start = Line(f, a, fromA);
        var endB = Line(f, b, fromB);
        var s = a;
        for (int guard = 0; guard < 4000 && b - s > 1e-6; guard++)
        {
            double Tol(double e) => (fromA != null && Math.Abs(s - a) < 1e-9) || (fromB != null && Math.Abs(e - b) < 1e-9) ? 1.2 * Tolerance : Tolerance;
            var extras = start.Select(v => v.Y).Where(y => f.Ys.All(q => Math.Abs(q - y) > 1e-6)).ToList();
            if (b - s <= 2 * MinStrip + 1e-9)
            {
                f.Strips.Add(MakeStrip(f, s, b, start, endB));
                break;
            }
            var plain = Longest(f, s, b, start, endB, new List<double>(), Tol);
            Strip? best = plain;
            if (extras.Count > 0)
            {
                var kept = Longest(f, s, b, start, endB, extras, Tol);
                if (kept != null && (plain == null || plain.S1 - s < 0.75 * (kept.S1 - s))) best = kept;
            }
            // Nem o trecho mínimo cabe: mais vértices no meio das faixas.
            var refine = extras;
            for (int lvl = 0; best == null && lvl < 4; lvl++)
            {
                refine = Subdivide(f.Ys, refine);
                best = Longest(f, s, b, start, endB, refine, Tol);
            }
            if (best == null)
            {
                var e = Math.Min(b, s + MinStrip);
                best = MakeStrip(f, s, e, start, Math.Abs(e - b) < 1e-9 ? endB : Line(f, e, null, refine));
            }
            f.Strips.Add(best);
            if (Math.Abs(best.S1 - b) < 1e-9) break;
            s = best.S1;
            start = best.End;
        }
    }

    /// <summary>
    /// Trecho mais longo a partir de <paramref name="s"/> que cabe na tolerância com os vértices <paramref name="extras"/> (além das
    /// linhas das faixas) na junta do fim; nulo se nem o mínimo cabe. Não deixa resto menor que o mínimo antes de <paramref name="b"/>.
    /// </summary>
    private Strip? Longest(Frame f, double s, double b, List<(double Y, double Z)> start, List<(double Y, double Z)> endB, List<double> extras,
        Func<double, double> tol)
    {
        var whole = MakeStrip(f, s, b, start, endB);
        if (Passes(f, whole, tol(b))) return whole;
        double lo = s + MinStrip, hi = b;
        var best = MakeStrip(f, s, lo, start, Line(f, lo, null, extras));
        if (!Passes(f, best, tol(lo))) return null;
        while (hi - lo > 0.1)
        {
            var m = (lo + hi) / 2;
            var c = MakeStrip(f, s, m, start, Line(f, m, null, extras));
            if (Passes(f, c, tol(m))) { lo = m; best = c; } else hi = m;
        }
        if (b - best.S1 < MinStrip)
        {
            var e = Math.Max(s + MinStrip, b - MinStrip);
            best = MakeStrip(f, s, e, start, Line(f, e, null, extras));
        }
        return best;
    }

    /// <summary>Vértices extras com mais um ponto no meio de cada intervalo entre as linhas das faixas e os extras atuais.</summary>
    private static List<double> Subdivide(double[] ys, List<double> extras)
    {
        var all = ys.Concat(extras).OrderBy(y => y).ToList();
        var res = extras.ToList();
        for (int i = 0; i + 1 < all.Count; i++)
            if (all[i + 1] - all[i] > 0.4) res.Add((all[i] + all[i + 1]) / 2);
        return res;
    }

    /// <summary>
    /// Vértices de uma junta (afastamento, cota): nas linhas das faixas, na superfície – ou, na borda de um nó, nas faces da
    /// via principal, com vértices extras onde elas dobram ao longo da junta.
    /// </summary>
    private List<(double Y, double Z)> Line(Frame f, double s, Frame? from, List<double>? extras = null)
    {
        var res = f.Ys.Concat(extras ?? new List<double>()).Distinct().OrderBy(y => y)
            .Select(y => (Y: y, Z: from == null ? f.Z(f.P(s, y)) : FaceZ(from, f.P(s, y)))).ToList();
        if (from == null) return res;
        double y0 = f.Ys[0], y1 = f.Ys[^1];
        var n = Math.Max(2, (int)Math.Ceiling((y1 - y0) / 0.05));
        var prof = new List<Vec2>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            var y = y0 + (y1 - y0) * i / n;
            prof.Add(new Vec2(y, FaceZ(from, f.P(s, y))));
        }
        foreach (var k in Kinks(prof, 2e-4))
        {
            // Vinco exato: interseção das retas dos dois lados.
            var y = prof[k].X;
            if (k >= 2 && k + 2 < prof.Count)
            {
                var (l0, l1, r0, r1) = (prof[k - 2], prof[k - 1], prof[k + 1], prof[k + 2]);
                var gl = (l1.Y - l0.Y) / (l1.X - l0.X);
                var gr = (r1.Y - r0.Y) / (r1.X - r0.X);
                if (Math.Abs(gl - gr) > 1e-6)
                    y = Math.Clamp((r0.Y - l1.Y + gl * l1.X - gr * r0.X) / (gl - gr), prof[k - 1].X, prof[k + 1].X);
            }
            if (res.Any(v => Math.Abs(v.Y - y) < 0.02)) continue;
            res.Add((y, FaceZ(from, f.P(s, y))));
        }
        return res.OrderBy(v => v.Y).ToList();
    }

    /// <summary>Índices dos vincos de um perfil (Douglas–Peucker).</summary>
    private static List<int> Kinks(List<Vec2> p, double tol)
    {
        var keep = new List<int>();
        var stack = new Stack<(int, int)>();
        stack.Push((0, p.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double best = 0;
            var idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                var t = (p[i].X - p[a].X) / Math.Max(1e-12, p[b].X - p[a].X);
                var d = Math.Abs(p[i].Y - (p[a].Y + (p[b].Y - p[a].Y) * t));
                if (d > best) { best = d; idx = i; }
            }
            if (idx < 0 || best <= tol) continue;
            keep.Add(idx);
            stack.Push((a, idx));
            stack.Push((idx, b));
        }
        return keep;
    }

    private Strip MakeStrip(Frame f, double s0, double s1, List<(double Y, double Z)> start, List<(double Y, double Z)> end)
    {
        var st = new Strip { S0 = s0, S1 = s1, Start = start, End = end, Cells = new Cell[f.Bands] };
        // Diagonais na mesma direção do trecho anterior (juntas paralelas, sem zigue-zague) quando as duas servem.
        var prev = f.Strips.Count > 0 && Math.Abs(f.Strips[^1].S1 - s0) < 1e-9 ? f.Strips[^1] : null;
        for (int j = 0; j < f.Bands; j++)
        {
            double y0 = f.Ys[j], y1 = f.Ys[j + 1];
            var a = start.Where(v => v.Y >= y0 - 1e-9 && v.Y <= y1 + 1e-9).ToList();
            var b = end.Where(v => v.Y >= y0 - 1e-9 && v.Y <= y1 + 1e-9).ToList();
            st.Cells[j] = MakeCell(f, s0, s1, a, b, prev?.Cells[j].Rising);
        }
        return st;
    }

    private static Cell MakeCell(Frame f, double s0, double s1, List<(double Y, double Z)> a, List<(double Y, double Z)> b, bool? rising)
    {
        var A = a.Select(v => Vec3.At(f.P(s0, v.Y), v.Z)).ToList();
        var B = b.Select(v => Vec3.At(f.P(s1, v.Y), v.Z)).ToList();
        var cell = new Cell();
        if (A.Count == 2 && B.Count == 2)
        {
            var pl = Through(A[0], A[1], B[0]);
            if (Math.Abs(pl.Z(B[1].XY) - B[1].Z) <= Coplanar)
            {
                cell.Planes.Add(FloorPlanes.Fit(new[] { A[0], A[1], B[0], B[1] }).Plane);
                return cell;
            }
            // Dois triângulos: a diagonal que fica mais perto da superfície no miolo da célula.
            double Dev(Plane3 lo, Plane3 hi, Vec2 p, Vec2 q)
            {
                var worst = 0.0;
                foreach (var (ts, ty) in new[] { (0.5, 0.5), (0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75) })
                {
                    var x = f.P(s0 + (s1 - s0) * ts, a[0].Y + (a[1].Y - a[0].Y) * ty);
                    var pl2 = (q - p).Cross(x - p) > 0 ? hi : lo;
                    worst = Math.Max(worst, Math.Abs(pl2.Z(x) - f.Z(x)));
                }
                return worst;
            }
            var lo1 = Through(A[0], B[0], B[1]);
            var hi1 = Through(A[0], B[1], A[1]);
            var lo2 = Through(A[0], B[0], A[1]);
            var hi2 = Through(A[1], B[0], B[1]);
            var d1 = Dev(lo1, hi1, A[0].XY, B[1].XY);
            var d2 = Dev(lo2, hi2, A[1].XY, B[0].XY);
            // Numa superfície torcida as duas diagonais dão quase o mesmo desvio: fica a direção do trecho anterior.
            var up = Math.Abs(d1 - d2) <= 0.002 ? rising ?? true : d1 <= d2;
            cell.Rising = up;
            if (up)
            {
                cell.Diags.Add((A[0].XY, B[1].XY));
                cell.Planes.Add(lo1);
                cell.Planes.Add(hi1);
            }
            else
            {
                cell.Diags.Add((A[1].XY, B[0].XY));
                cell.Planes.Add(lo2);
                cell.Planes.Add(hi2);
            }
            return cell;
        }
        // Lados com vértices diferentes (borda do nó): triângulos em "zíper", de baixo para cima.
        int i = 0, k = 0;
        while (i < A.Count - 1 || k < B.Count - 1)
        {
            var advA = i < A.Count - 1 && (k == B.Count - 1 || a[i + 1].Y <= b[k + 1].Y);
            if (advA) { cell.Planes.Add(Through(A[i], A[i + 1], B[k])); i++; }
            else { cell.Planes.Add(Through(A[i], B[k], B[k + 1])); k++; }
            if (i < A.Count - 1 || k < B.Count - 1) cell.Diags.Add((A[i].XY, B[k].XY));
        }
        if (cell.Planes.Count == 0) cell.Planes.Add(new Plane3(A.Concat(B).Average(v => v.Z), 0, 0));
        return cell;
    }

    private static Plane3 Through(Vec3 a, Vec3 b, Vec3 c)
    {
        var n = (b - a).Cross(c - a);
        if (Math.Abs(n.Z) < 1e-12) return new Plane3((a.Z + b.Z + c.Z) / 3, 0, 0);
        var bx = -n.X / n.Z;
        var by = -n.Y / n.Z;
        return new Plane3(a.Z - bx * a.X - by * a.Y, bx, by);
    }

    /// <summary>
    /// O trecho cabe na tolerância: faces a até <paramref name="tol"/> da superfície em todo ponto e, nas linhas de quebra
    /// curvas (eixo em curva), faces vizinhas a até 1 mm uma da outra.
    /// </summary>
    private static bool Passes(Frame f, Strip st, double tol)
    {
        // Amostras a cada 0,75 m (no máximo): o desvio de uma face pode passar do medido entre amostras mais espaçadas.
        var len = st.S1 - st.S0;
        var ns = Math.Max(4, (int)Math.Ceiling(len / 0.75));
        for (int j = 0; j < f.Bands; j++)
        {
            double y0 = f.Ys[j], y1 = f.Ys[j + 1];
            var ny = Math.Max(2, (int)Math.Ceiling((y1 - y0) / 0.75));
            var cell = st.Cells[j];
            for (int u = 0; u <= ns; u++)
            {
                var s = st.S0 + len * u / ns;
                for (int v = 0; v <= ny; v++)
                {
                    var x = f.P(s, y0 + (y1 - y0) * v / ny);
                    if (Math.Abs(cell.At(x).Z(x) - f.Z(x)) > tol) return false;
                }
                if (j > 0 && u > 0 && u < ns)
                {
                    var x = f.P(s, y0);
                    if (Math.Abs(cell.At(x).Z(x) - st.Cells[j - 1].At(x).Z(x)) > SeamTol) return false;
                }
            }
        }
        return true;
    }

    private static double FaceZ(Frame f, Vec2 x)
    {
        var (s, y) = f.Surf.LocateExtended(x);
        var st = f.StripAt(s);
        return st.Cells[f.BandAt(y)].At(x).Z(x);
    }

    // ------------------------------------------------------------------ recorte dos pisos

    private List<(Polygon2 Part, Plane3 Plane)> Split(Polygon2 shape, List<(Polygon2 Region, Frame Frame)> regions, Frame main)
    {
        var parts = new List<(Polygon2, Plane3)>();
        var rest = new List<Polygon2> { shape };
        foreach (var (region, fr) in regions)
        {
            if (rest.Count == 0) break;
            var inside = PolygonOps.Intersect(rest, new[] { region }).Where(p => p.Area > 1e-7).ToList();
            if (inside.Count == 0) continue;
            foreach (var p in inside) parts.AddRange(Tile(fr, p));
            rest = PolygonOps.Difference(rest, new[] { region }).Where(p => p.Area > 1e-7).ToList();
        }
        foreach (var p in rest) parts.AddRange(Tile(main, p));
        return MergeSlivers(parts);
    }

    /// <summary>Peça recortada pelas células da malha: cada parte com o plano da face em que está.</summary>
    private static List<(Polygon2 Part, Plane3 Plane)> Tile(Frame f, Polygon2 shape)
    {
        var res = new List<(Polygon2, Plane3)>();
        var ring = CurveTools.Densify(shape.Outer.Append(shape.Outer[0]).ToList(), 1.0);
        var locs = ring.Select(f.Surf.LocateExtended).ToList();
        double smin = locs.Min(l => l.S) - 0.5, smax = locs.Max(l => l.S) + 0.5;
        double ymin = locs.Min(l => l.Y) - 1, ymax = locs.Max(l => l.Y) + 1;
        var covered = 0.0;
        foreach (var st in f.Strips)
        {
            if (st.S1 <= smin || st.S0 >= smax) continue;
            var lo = Math.Min(ymin, f.Ys[0]);
            var hi = Math.Max(ymax, f.Ys[^1]);
            // Faixa larga num eixo em curva: sem passar do centro da curva (o contorno se cruzaria).
            var r = Radius(f, st.S0, st.S1);
            lo = Math.Max(lo, -0.9 * r);
            hi = Math.Min(hi, 0.9 * r);
            List<Polygon2> inStrip;
            try { inStrip = PolygonOps.Intersect(new[] { shape }, new[] { FloorPlanes.Band(f.Surf, st.S0, st.S1, lo, hi) }).Where(p => p.Area > 1e-7).ToList(); }
            catch { continue; }
            if (inStrip.Count == 0) continue;
            for (int j = 0; j < f.Bands; j++)
            {
                var y0 = j == 0 ? lo : f.Ys[j];
                var y1 = j == f.Bands - 1 ? hi : f.Ys[j + 1];
                if (y1 <= y0) continue;
                var cur = PolygonOps.Intersect(inStrip, new[] { FloorPlanes.Band(f.Surf, st.S0, st.S1, y0, y1) }).Where(p => p.Area > 1e-7).ToList();
                if (cur.Count == 0) continue;
                var cell = st.Cells[j];
                for (int t = 0; t < cell.Diags.Count && cur.Count > 0; t++)
                {
                    var (p, q) = cell.Diags[t];
                    var below = PolygonOps.Intersect(cur, new[] { HalfPlane(p, q, false) }).Where(x => x.Area > 1e-7).ToList();
                    foreach (var x in below) { res.Add((x, cell.Planes[t])); covered += x.Area; }
                    cur = PolygonOps.Intersect(cur, new[] { HalfPlane(p, q, true) }).Where(x => x.Area > 1e-7).ToList();
                }
                foreach (var x in cur) { res.Add((x, cell.Planes[cell.Diags.Count])); covered += x.Area; }
            }
        }
        // O que ficou fora da malha (além das faixas das pontas): plano de mínimos quadrados da superfície. Frestas do
        // arredondamento do recorte (0,1 mm) ficam de fora – sem área, o plano delas sairia qualquer.
        if (covered < shape.Area - 1e-4)
        {
            var left = PolygonOps.Difference(new[] { shape }, res.Select(x => x.Item1)).Where(x => x.Area > 1e-4 && 2 * x.Area / Perimeter(x) > 0.005).ToList();
            foreach (var x in left) res.Add((x, FloorPlanes.Fit(FloorPlanes.Samples(x, f.Z)).Plane));
        }
        return res;
    }

    /// <summary>Raio de curvatura (m) do eixo no trecho (infinito na reta).</summary>
    private static double Radius(Frame f, double s0, double s1)
    {
        var axis = f.Surf.Axis;
        double a = Math.Clamp(s0, 0, f.L), b = Math.Clamp(s1, 0, f.L);
        if (b - a < 1e-6) return double.PositiveInfinity;
        var worst = double.PositiveInfinity;
        var n = Math.Max(1, (int)Math.Ceiling((b - a) / 2));
        for (int i = 0; i < n; i++)
        {
            double u = a + (b - a) * i / n, v = a + (b - a) * (i + 1) / n;
            var t0 = axis.TangentAt(u);
            var t1 = axis.TangentAt(v);
            var ang = Math.Abs(Math.Atan2(t0.Cross(t1), t0.Dot(t1)));
            if (ang > 1e-6) worst = Math.Min(worst, Math.Max(v - u, 0.5) / ang);
        }
        return worst;
    }

    /// <summary>Semiplano à esquerda (ou à direita) da reta p→q, como polígono grande.</summary>
    private static Polygon2 HalfPlane(Vec2 p, Vec2 q, bool left)
    {
        const double M = 2000;
        var d = (q - p).Normalized();
        var n = left ? d.PerpLeft : d.PerpRight;
        var a = p - d * M;
        var b = q + d * M;
        return new Polygon2(left ? new[] { a, b, b + n * M, a + n * M } : new[] { a, a + n * M, b + n * M, b });
    }

    /// <summary>
    /// Lascas (&lt; 0,01 m² ou com menos de 2 cm de largura média) juntadas à parte vizinha com a maior borda em comum – onde o
    /// contorno da peça passa rente a uma junta. A lasca fica no plano da vizinha (diferença ≤ largura × diferença de rampa).
    /// </summary>
    private static double Perimeter(Polygon2 p)
    {
        static double Ring(IReadOnlyList<Vec2> r) { var s = 0.0; for (int i = 0; i < r.Count; i++) s += r[i].DistanceTo(r[(i + 1) % r.Count]); return s; }
        return Math.Max(1e-9, Ring(p.Outer) + p.Holes.Sum(Ring));
    }

    private static List<(Polygon2 Part, Plane3 Plane)> MergeSlivers(List<(Polygon2 Part, Plane3 Plane)> parts)
    {
        static double Per(Polygon2 p) => Perimeter(p);
        static bool Thin(Polygon2 p) => p.Area < 0.01 || 2 * p.Area / Math.Max(1e-9, Per(p)) < 0.02;
        var list = parts.ToList();
        var stuck = new HashSet<Polygon2>();
        for (int guard = 0; guard < 2000; guard++)
        {
            var i = list.FindIndex(x => Thin(x.Part) && !stuck.Contains(x.Part));
            if (i < 0) break;
            var sl = list[i].Part;
            var (mn, mx) = sl.Bounds;
            var best = -1;
            var bestShared = 1e-3;
            Polygon2? bestUnion = null;
            for (int k = 0; k < list.Count; k++)
            {
                if (k == i) continue;
                var (an, ax) = list[k].Part.Bounds;
                if (an.X > mx.X + 1e-3 || ax.X < mn.X - 1e-3 || an.Y > mx.Y + 1e-3 || ax.Y < mn.Y - 1e-3) continue;
                List<Polygon2> u;
                try
                {
                    u = PolygonOps.Union(new[] { sl, list[k].Part });
                    // Fresta do arredondamento do recorte (0,1 mm) entre as duas: fechada.
                    if (u.Count != 1) u = PolygonOps.Heal(u, 0.0005);
                }
                catch { continue; }
                if (u.Count != 1) continue;
                var shared = (Per(sl) + Per(list[k].Part) - Per(u[0])) / 2;
                if (shared > bestShared) { bestShared = shared; best = k; bestUnion = u[0]; }
            }
            if (best < 0) { stuck.Add(sl); continue; }
            list[best] = (bestUnion!, list[best].Plane);
            list.RemoveAt(i);
        }
        return list;
    }
}
