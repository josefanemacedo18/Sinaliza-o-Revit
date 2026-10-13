using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Borda de projeto que concorda com o terreno em talude: a superfície do talude é contínua pela distância à borda (corte sobe
/// 1 : <see cref="Cut"/>, aterro desce 1 : <see cref="Fill"/>), sem depender de faixas de triângulos entre a borda e o pé.
/// </summary>
public sealed class SlopeEdge
{
    public List<Vec3> Line { get; } = new();
    public double Cut { get; set; } = 1.0;
    public double Fill { get; set; } = 1.5;
    /// <summary>Contorno fechado (plataforma) – sem pontas.</summary>
    public bool Closed { get; set; }
    /// <summary>Além das pontas da linha o talude continua em cone (falso = face/estrutura na ponta: nada além dela).</summary>
    public bool OpenStart { get; set; } = true;
    public bool OpenEnd { get; set; } = true;
    /// <summary>Alcance (m) até onde a borda pode alterar o terreno.</summary>
    public double Reach { get; set; } = 60;
    /// <summary>Distância até o pé/crista na normal de cada vértice (mesma contagem de <see cref="Line"/>).</summary>
    public List<double> Day { get; } = new();

    private const double DayMargin = 2.0;

    internal void Finish(double maxDist)
    {
        // Provisório – Grading.Design fixa o alcance pela faixa de cotas do terreno (SetReach).
        Reach = Math.Min(maxDist + DayMargin, Day.DefaultIfEmpty(0).Max() * 2 + 5);
    }

    /// <summary>
    /// Alcance da borda: além dele o plano de corte passa acima do terreno mais alto e o de aterro abaixo do mais baixo – a
    /// borda deixa de contar sem salto na superfície (o alcance pelo pé na normal cortava o cone de uma ponta ainda ativo).
    /// </summary>
    internal void SetReach(double groundMin, double groundMax, double maxDist)
    {
        var r = 0.0;
        foreach (var v in Line) r = Math.Max(r, Math.Max(Math.Max(0.1, Cut) * (groundMax - v.Z), Math.Max(0.1, Fill) * (v.Z - groundMin)));
        Reach = Math.Min(maxDist + DayMargin, r + 1);
    }
}

/// <summary>Superfície de projeto da terraplenagem e volumes de corte/aterro.</summary>
public sealed class GradingResult
{
    /// <summary>
    /// Bordas com talude (faixas, plataformas, pontas livres). Onde taludes vizinhos se sobrepõem a superfície é uma só:
    /// cota final = máx(maior aterro, mín(terreno, menor corte)) – contínua, sem buracos nem paredões entre os taludes.
    /// </summary>
    public List<SlopeEdge> SlopeEdges { get; } = new();

    /// <summary>Triângulos da superfície de projeto (faixas e taludes), cotas absolutas (m).</summary>
    public List<(Vec3 A, Vec3 B, Vec3 C)> Triangles { get; } = new();
    /// <summary>Plataformas (polígono, cota absoluta no ponto – plana ou inclinada pelo greide das vias).</summary>
    public List<(Polygon2 Area, Func<Vec2, double> Z)> Pads { get; } = new();
    /// <summary>Pontos a inserir no terreno (bordas, pés/cristas de talude, pontos internos das plataformas).</summary>
    public List<Vec3> Points { get; } = new();
    /// <summary>Contorno de tudo o que é alterado (plataformas + taludes).</summary>
    public List<Polygon2> Footprint { get; } = new();
    public double CutM3 { get; set; }
    public double FillM3 { get; set; }
    public double AreaM2 { get; set; }
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Pontos do terreno natural colados na área alterada (junto ao pé dos taludes perto das plataformas): entram no Toposolid
    /// com os de projeto – o pé do talude fica marcado dos dois lados, sem triângulos até os pontos do levantamento.
    /// </summary>
    public List<Vec3> GroundPoints { get; } = new();

    /// <summary>Áreas das faixas de projeto (plataformas das vias), para separar plataforma e talude.</summary>
    public List<Polygon2> PlatformAreas { get; } = new();

    /// <summary>Trechos de plataforma com a cota exata da superfície da via (faixas com <see cref="GradeCorridor.Surface"/>).</summary>
    public List<(Polygon2 Area, Func<Vec2, double> Z)> Platforms { get; } = new();
    private Dictionary<(int, int), List<int>>? _platIndex;
    private int _platIndexed = -1;

    /// <summary>Cota da plataforma exata no ponto (a mais alta das que o contêm) – nula fora delas.</summary>
    private double? PlatformZ(Vec2 p)
    {
        if (Platforms.Count == 0) return null;
        if (_platIndex == null || _platIndexed != Platforms.Count)
        {
            _platIndex = new();
            for (int i = 0; i < Platforms.Count; i++)
            {
                var (mn, mx) = Platforms[i].Area.Bounds;
                var (x0, y0) = Key(mn.X, mn.Y);
                var (x1, y1) = Key(mx.X, mx.Y);
                for (var x = x0; x <= x1; x++)
                    for (var y = y0; y <= y1; y++)
                    {
                        if (!_platIndex.TryGetValue((x, y), out var l)) _platIndex[(x, y)] = l = new List<int>();
                        l.Add(i);
                    }
            }
            _platIndexed = Platforms.Count;
        }
        if (!_platIndex.TryGetValue(Key(p.X, p.Y), out var cand)) return null;
        double? best = null;
        foreach (var i in cand)
            // Borda inclusiva: os pontos das linhas da seção são vértices dos trechos (fora de todos, caíam no talude).
            if (Platforms[i].Area.Contains(p) || Platforms[i].Area.DistanceTo(p) <= 1e-6)
            {
                var z = Platforms[i].Z(p);
                if (best == null || z > best) best = z;
            }
        return best;
    }

    /// <summary>Triângulo de talude (falso = plataforma/faixa de projeto, que tem prioridade).</summary>
    public List<bool> TriangleIsSlope { get; } = new();

    /// <summary>Terreno natural usado no projeto (para combinar taludes sobrepostos).</summary>
    public Func<Vec2, double?>? Ground { get; set; }

    private const double Cell = 6.0;
    private Dictionary<(int, int), List<int>>? _index;
    private int _indexed;

    private static (int, int) Key(double x, double y) => ((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell));

    private void Index()
    {
        if (_index != null && _indexed == Triangles.Count) return;
        _index = new();
        for (int i = 0; i < Triangles.Count; i++)
        {
            var (a, b, c) = Triangles[i];
            var (x0, y0) = Key(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)));
            var (x1, y1) = Key(Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                {
                    if (!_index.TryGetValue((x, y), out var l)) _index[(x, y)] = l = new List<int>();
                    l.Add(i);
                }
        }
        _indexed = Triangles.Count;
    }

    /// <summary>
    /// Cota de projeto no ponto (nulo = fora da área terraplenada). Plataformas têm prioridade; onde taludes de obras
    /// vizinhas se sobrepõem vale o mais alto nos aterros e o mais baixo nos cortes (superfície única, sem "degraus").
    /// </summary>
    public double? DesignZ(Vec2 p)
    {
        foreach (var (area, z) in Pads) if (area.Contains(p)) return z(p);
        if (PlatformZ(p) is { } pz) return pz;
        Index();
        if (!_index!.TryGetValue(Key(p.X, p.Y), out var cand)) return SlopeZ(p);
        double? platform = null;
        List<double>? slopes = null;
        foreach (var i in cand)
        {
            var (a, b, c) = Triangles[i];
            var d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(d) < 1e-12) continue;
            var l1 = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / d;
            var l2 = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / d;
            var l3 = 1 - l1 - l2;
            if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
            var z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
            var slope = i < TriangleIsSlope.Count && TriangleIsSlope[i];
            if (!slope) platform = platform == null ? z : Math.Max(platform.Value, z);
            else (slopes ??= new()).Add(z);
        }
        if (platform != null) return platform;
        if (slopes == null) return SlopeZ(p);
        if (SlopeZ(p) is { } sf) slopes.Add(sf);
        if (slopes.Count == 1) return slopes[0];
        var g = Ground?.Invoke(p);
        if (g == null) return slopes.Max();
        var fills = slopes.Where(z => z > g.Value).ToList();
        return fills.Count > 0 ? fills.Max() : slopes.Min();
    }

    private const double EdgeCell = 8.0;
    private Dictionary<(int, int), List<(int E, int S)>>? _edgeIndex;
    private int _edgesIndexed = -1;

    private List<double[]>? _edgeGround;

    private void IndexEdges()
    {
        if (_edgeIndex != null && _edgesIndexed == SlopeEdges.Count) return;
        _edgeIndex = new();
        // Cota do terreno nos vértices das bordas (interpolada ao longo de cada segmento – sem consultar o terreno por candidato).
        _edgeGround = SlopeEdges.Select(ed => ed.Line.Select(v => Ground?.Invoke(v.XY) ?? v.Z).ToArray()).ToList();
        for (int e = 0; e < SlopeEdges.Count; e++)
        {
            var ed = SlopeEdges[e];
            var n = ed.Line.Count;
            var segs = ed.Closed ? n : n - 1;
            for (int k = 0; k < segs; k++)
            {
                var a = ed.Line[k];
                var b = ed.Line[(k + 1) % n];
                var r = ed.Reach;
                var x0 = (int)Math.Floor((Math.Min(a.X, b.X) - r) / EdgeCell);
                var x1 = (int)Math.Floor((Math.Max(a.X, b.X) + r) / EdgeCell);
                var y0 = (int)Math.Floor((Math.Min(a.Y, b.Y) - r) / EdgeCell);
                var y1 = (int)Math.Floor((Math.Max(a.Y, b.Y) + r) / EdgeCell);
                for (var x = x0; x <= x1; x++)
                    for (var y = y0; y <= y1; y++)
                    {
                        if (!_edgeIndex.TryGetValue((x, y), out var l)) _edgeIndex[(x, y)] = l = new();
                        l.Add((e, k));
                    }
            }
        }
        _edgesIndexed = SlopeEdges.Count;
    }

    /// <summary>
    /// Cota dos taludes no ponto (nula = o terreno fica natural ali): cada borda abaixo do terreno dá um corte (sobe com a
    /// distância), cada borda acima um aterro (desce); o corte vale na região contínua de terreno acima do plano de corte
    /// que encosta na borda (o morro do outro lado de um vale não é cortado), o aterro na região de terreno abaixo do plano
    /// de aterro que encosta na borda. Cota final = máx(maior aterro, mín(terreno, menor corte)) – contínua: na divisa das
    /// regiões o plano encontra o terreno.
    /// </summary>
    public double? SlopeZ(Vec2 p)
    {
        if (SlopeEdges.Count == 0 || Ground?.Invoke(p) is not { } t) return null;
        IndexEdges();
        var (cut, fill, _, _) = Planes(p);
        if (cut == double.MaxValue && fill == double.MinValue) return null;
        var reach = Reach(p);
        var cutMin = cut < t && (reach & 1) != 0 ? cut : double.MaxValue;
        var fillMax = fill > t && (reach & 2) != 0 ? fill : double.MinValue;
        if (cutMin == double.MaxValue && fillMax == double.MinValue) return null;
        var f = Math.Max(fillMax, Math.Min(t, cutMin));
        return Math.Abs(f - t) < 1e-3 ? null : f;
    }

    /// <summary>
    /// Planos de corte e de aterro no ponto (o mais baixo dos cortes, o mais alto dos aterros, das bordas ao alcance) e a
    /// distância ao trecho de borda abaixo/acima do terreno mais próximo (sementes das regiões).
    /// </summary>
    private (double Cut, double Fill, double CutDist, double FillDist) Planes(Vec2 p)
    {
        double cut = double.MaxValue, fill = double.MinValue, dc = double.MaxValue, df = double.MaxValue;
        if (!_edgeIndex!.TryGetValue(((int)Math.Floor(p.X / EdgeCell), (int)Math.Floor(p.Y / EdgeCell)), out var cand)) return (cut, fill, dc, df);
        foreach (var (e, k) in cand)
        {
            var ed = SlopeEdges[e];
            var n = ed.Line.Count;
            var a = ed.Line[k];
            var b = ed.Line[(k + 1) % n];
            // Além das pontas fechadas (face/estrutura) a borda inteira não vale (o semiplano à frente da ponta).
            if (!ed.Closed && n >= 2)
            {
                if (!ed.OpenStart && (p - ed.Line[0].XY).Dot(ed.Line[1].XY - ed.Line[0].XY) < 0) continue;
                if (!ed.OpenEnd && (p - ed.Line[n - 1].XY).Dot(ed.Line[n - 1].XY - ed.Line[n - 2].XY) > 0) continue;
            }
            var ab = b.XY - a.XY;
            var len2 = ab.Dot(ab);
            var u = len2 < 1e-12 ? 0 : (p - a.XY).Dot(ab) / len2;
            u = Math.Clamp(u, 0, 1);
            var d = p.DistanceTo(a.XY + ab * u);
            if (d > ed.Reach) continue;
            var z = a.Z + (b.Z - a.Z) * u;
            // Todo trecho de borda dá os dois planos (separar por corte/aterro fazia a superfície saltar onde a borda cruza o
            // terreno); onde cada um vale decidem as regiões ligadas às bordas (Reach).
            cut = Math.Min(cut, z + d / Math.Max(0.1, ed.Cut));
            fill = Math.Max(fill, z - d / Math.Max(0.1, ed.Fill));
            var eg = _edgeGround![e];
            var tq = eg[k] + (eg[(k + 1) % n] - eg[k]) * u;
            if (z <= tq) dc = Math.Min(dc, d);
            else df = Math.Min(df, d);
        }
        return (cut, fill, dc, df);
    }

    /// <summary>Malha (m) das regiões de corte/aterro ligadas às bordas.</summary>
    private const double ReachCell = 1.0;
    private byte[]? _reach;
    private double _rx0, _ry0, _rh;
    private int _rnx, _rny;
    private int _reachEdges = -1;

    /// <summary>Bits da célula do ponto: 1 = região de corte ligada a uma borda, 2 = de aterro (com uma célula de folga).</summary>
    private int Reach(Vec2 p)
    {
        if (_reach == null || _reachEdges != SlopeEdges.Count) BuildReach();
        var i = (int)Math.Round((p.X - _rx0) / _rh);
        var j = (int)Math.Round((p.Y - _ry0) / _rh);
        if (i < 0 || j < 0 || i >= _rnx || j >= _rny) return 0;
        return _reach![j * _rnx + i];
    }

    /// <summary>
    /// Regiões contínuas de corte (terreno acima do plano de corte) e de aterro (terreno abaixo do de aterro) que encostam nas
    /// bordas: preenchimento a partir das células junto às bordas, numa malha de <see cref="ReachCell"/> m.
    /// </summary>
    private void BuildReach()
    {
        _reachEdges = SlopeEdges.Count;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var e in SlopeEdges)
            foreach (var v in e.Line)
            {
                x0 = Math.Min(x0, v.X - e.Reach); y0 = Math.Min(y0, v.Y - e.Reach);
                x1 = Math.Max(x1, v.X + e.Reach); y1 = Math.Max(y1, v.Y + e.Reach);
            }
        var h = ReachCell;
        if ((x1 - x0) * (y1 - y0) / (h * h) > 2_000_000) h = Math.Sqrt((x1 - x0) * (y1 - y0) / 2_000_000);
        _rh = h;
        _rx0 = Math.Floor(x0 / h) * h;
        _ry0 = Math.Floor(y0 / h) * h;
        _rnx = (int)Math.Ceiling((x1 - _rx0) / h) + 1;
        _rny = (int)Math.Ceiling((y1 - _ry0) / h) + 1;
        var n = _rnx * _rny;
        var inCut = new bool[n];
        var inFill = new bool[n];
        var qc = new Queue<int>();
        var qf = new Queue<int>();
        var cutSeen = new bool[n];
        var fillSeen = new bool[n];
        for (int j = 0; j < _rny; j++)
            for (int i = 0; i < _rnx; i++)
            {
                var c = new Vec2(_rx0 + i * h, _ry0 + j * h);
                if (Ground?.Invoke(c) is not { } t) continue;
                var (cut, fill, dc, df) = Planes(c);
                var k = j * _rnx + i;
                inCut[k] = cut < t;
                inFill[k] = fill > t;
                // Sementes: junto às bordas (a região começa nelas).
                if (inCut[k] && dc <= h * 1.5) { cutSeen[k] = true; qc.Enqueue(k); }
                if (inFill[k] && df <= h * 1.5) { fillSeen[k] = true; qf.Enqueue(k); }
            }
        void Flood(Queue<int> q, bool[] inside, bool[] seen)
        {
            while (q.Count > 0)
            {
                var k = q.Dequeue();
                int i = k % _rnx, j = k / _rnx;
                foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int a = i + di, b = j + dj;
                    if (a < 0 || b < 0 || a >= _rnx || b >= _rny) continue;
                    var kk = b * _rnx + a;
                    if (seen[kk] || !inside[kk]) continue;
                    seen[kk] = true;
                    q.Enqueue(kk);
                }
            }
        }
        Flood(qc, inCut, cutSeen);
        Flood(qf, inFill, fillSeen);
        // Folga de uma célula: perto da divisa a cota já é a do terreno (mín/máx), e o ponto pode cair na célula vizinha.
        _reach = new byte[n];
        for (int j = 0; j < _rny; j++)
            for (int i = 0; i < _rnx; i++)
            {
                byte bits = 0;
                for (int dj = -1; dj <= 1 && bits != 3; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int a = i + di, b = j + dj;
                        if (a < 0 || b < 0 || a >= _rnx || b >= _rny) continue;
                        var kk = b * _rnx + a;
                        if (cutSeen[kk]) bits |= 1;
                        if (fillSeen[kk]) bits |= 2;
                    }
                _reach[j * _rnx + i] = bits;
            }
    }
}

/// <summary>
/// Terraplenagem: a partir das faixas e plataformas de projeto (bordas cotadas), encontra o encontro dos taludes de corte
/// (H:V) e de aterro com o terreno natural ("offsets"), monta a superfície de projeto e calcula os volumes. O Revit aplica
/// o resultado ao Toposolid (Massa e terreno) – vias, conexões e obras ficam planas/no greide e o terreno se ajusta a elas.
/// </summary>
public static class Grading
{
    /// <summary>
    /// Mapa de corte e aterro: regiões (células de <paramref name="step"/> m unidas) onde o projeto fica mais de
    /// <paramref name="min"/> m abaixo do terreno natural (corte) ou acima dele (aterro).
    /// </summary>
    public static (List<Polygon2> Cut, List<Polygon2> Fill) CutFillRegions(GradingResult design, Func<Vec2, double?> ground, double step = 2, double min = 0.10)
    {
        var cut = new List<Polygon2>();
        var fill = new List<Polygon2>();
        if (design.Footprint.Count == 0) return (cut, fill);
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var f in design.Footprint)
        {
            var (mn, mx) = f.Bounds;
            x0 = Math.Min(x0, mn.X); y0 = Math.Min(y0, mn.Y); x1 = Math.Max(x1, mx.X); y1 = Math.Max(y1, mx.Y);
        }
        var nx = (int)Math.Ceiling((x1 - x0) / step);
        var ny = (int)Math.Ceiling((y1 - y0) / step);
        if ((long)nx * ny > 400_000) { step *= Math.Sqrt((double)nx * ny / 400_000); nx = (int)Math.Ceiling((x1 - x0) / step); ny = (int)Math.Ceiling((y1 - y0) / step); }
        var cutCells = new List<Polygon2>();
        var fillCells = new List<Polygon2>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var c = new Vec2(x0 + (i + 0.5) * step, y0 + (j + 0.5) * step);
                if (design.DesignZ(c) is not { } zd || ground(c) is not { } zg) continue;
                var dz = zd - zg;
                if (Math.Abs(dz) < min) continue;
                var cell = Polygon2.Rectangle(new Vec2(x0 + i * step, y0 + j * step), new Vec2(x0 + (i + 1) * step, y0 + (j + 1) * step));
                (dz < 0 ? cutCells : fillCells).Add(cell);
            }
        List<Polygon2> U(List<Polygon2> cells)
        {
            if (cells.Count == 0) return new List<Polygon2>();
            try
            {
                // União das células e contorno suavizado (abre/fecha com arredondamento) – sem "escadinha" de células.
                var u = PolygonOps.Union(cells);
                var smooth = PolygonOps.Offset(PolygonOps.Offset(u, -step * 0.4, true), step * 0.4, true);
                return smooth.Where(p => p.Area > step * step * 1.5).Select(p => p.Simplified() ?? p).ToList();
            }
            catch { return new List<Polygon2>(); }
        }
        return (U(cutCells), U(fillCells));
    }

    /// <param name="ground">Cota do terreno natural (m, absoluta) – nulo fora do terreno.</param>
    /// <param name="baseZ">Cota da base das marcas (somada às cotas relativas das faixas/plataformas).</param>
    public static GradingResult Design(IEnumerable<GradeCorridor> corridors, IEnumerable<GradePad> pads, Func<Vec2, double?> ground, double baseZ = 0,
        double maxDaylight = 60, double step = 0.5, double padGrid = 4.0, double sampling = 1.0)
    {
        var res = new GradingResult { Ground = ground };
        foreach (var c in corridors)
        {
            var n = Math.Min(c.Left.Count, c.Right.Count);
            if (n < 2) continue;
            var left = c.Left.Take(n).Select(p => p with { Z = p.Z + baseZ }).ToList();
            var right = c.Right.Take(n).Select(p => p with { Z = p.Z + baseZ }).ToList();
            // Faixa: tiras entre as linhas da seção (bordas e linhas internas, quando há) – quadriláteros em dois triângulos.
            var lines = new List<List<Vec3>> { left };
            lines.AddRange(c.Inner.Where(l => l.Count >= n).Select(l => l.Take(n).Select(p => p with { Z = p.Z + baseZ }).ToList()));
            lines.Add(right);
            Func<Vec2, double>? exact = c.Surface is { } sf ? p => sf(p) + baseZ : null;
            for (int j = 0; exact == null && j + 1 < lines.Count; j++)
            {
                var la = lines[j];
                var lb = lines[j + 1];
                for (int i = 0; i + 1 < n; i++)
                {
                    res.Triangles.Add((la[i], la[i + 1], lb[i + 1]));
                    res.Triangles.Add((la[i], lb[i + 1], lb[i]));
                    res.TriangleIsSlope.Add(false);
                    res.TriangleIsSlope.Add(false);
                }
            }
            for (int i = 0; i + 1 < n; i++)
            {
                var plat = new Polygon2(new[] { left[i].XY, left[i + 1].XY, right[i + 1].XY, right[i].XY });
                res.Footprint.Add(plat);
                res.PlatformAreas.Add(plat);
                if (exact != null) res.Platforms.Add((plat, exact));
            }
            // Quebras da superfície que cruzam a faixa (crista e fim do caimento da via principal de um nó): pontos sobre elas.
            if (exact != null && c.Breaklines.Count > 0)
            {
                var inPlat = TerrainRebuild.Mask(Enumerable.Range(0, n - 1).Select(i => new Polygon2(new[] { left[i].XY, left[i + 1].XY, right[i + 1].XY, right[i].XY })), 0);
                foreach (var bl in c.Breaklines)
                    foreach (var q in CurveTools.Densify(bl, 0.5))
                        if (inPlat(q)) res.Points.Add(Vec3.At(q, exact(q)));
            }
            // Pontos do terreno nas linhas, a no máximo 1 m um do outro: sem quebras de linha no Toposolid, a triangulação só
            // respeita a borda da plataforma e as quebras da seção se os pontos nelas forem densos.
            if (lines.Count == 2) lines.Insert(1, Enumerable.Range(0, n).Select(i => (left[i] + right[i]) * 0.5).ToList());
            foreach (var l in lines)
            {
                var dense = new List<Vec3>();
                // Bordas a cada 0,5 m (contornos de pontos a 0,3 m delas, fora), linhas internas a cada 1 m.
                var spacing = ReferenceEquals(l, left) || ReferenceEquals(l, right) ? 0.5 : 1.0;
                for (int i = 0; i + 1 < n; i++)
                {
                    var k = Math.Max(1, (int)Math.Ceiling(l[i].XY.DistanceTo(l[i + 1].XY) / spacing));
                    for (int j = 0; j < k; j++) dense.Add(l[i] + (l[i + 1] - l[i]) * ((double)j / k));
                }
                dense.Add(l[n - 1]);
                // Todos os pontos: as linhas são as quebras da superfície (bordas, crista, fim do caimento) – sem eles a
                // triangulação do Toposolid cortaria a crista e a borda da plataforma.
                res.Points.AddRange(dense);
            }
            // Pontas: face vertical (obra) ou talude à frente (ponta livre, saia de encontro).
            var spillStart = c.SpillStart || c.WallStart && c.EndSpill;
            var spillEnd = c.SpillEnd || c.WallEnd && c.EndSpill;
            var faceStart = c.WallStart && !c.EndSpill;
            var faceEnd = c.WallEnd && !c.EndSpill;
            // Taludes: do lado de fora de cada borda (superfície contínua pela distância); muros: degrau até o terreno natural.
            var dayL = c.DaylightLeft ? SlopeLine(res, left, right, c.CutSlope, c.FillSlope, ground, maxDaylight, step, !faceStart, !faceEnd)
                : c.WallLeft ? WallBand(res, left, right, ground) : left;
            var dayR = c.DaylightRight ? SlopeLine(res, right, left, c.CutSlope, c.FillSlope, ground, maxDaylight, step, !faceStart, !faceEnd)
                : c.WallRight ? WallBand(res, right, left, ground) : right;
            if (faceStart) EndWall(res, dayL[0], left[0], right[0], dayR[0], (left[0].XY + right[0].XY) * 0.5 - (left[1].XY + right[1].XY) * 0.5, ground,
                c.WallCap, c.CutSlope, maxDaylight, step);
            if (faceEnd) EndWall(res, dayL[n - 1], left[n - 1], right[n - 1], dayR[n - 1], (left[n - 1].XY + right[n - 1].XY) * 0.5 - (left[n - 2].XY + right[n - 2].XY) * 0.5, ground,
                c.WallCap, c.CutSlope, maxDaylight, step);
            if (spillStart) EndSlope(res, right[0], left[0], (left[0].XY + right[0].XY) * 0.5 - (left[1].XY + right[1].XY) * 0.5, c.CutSlope, c.FillSlope, ground, maxDaylight, step);
            if (spillEnd) EndSlope(res, left[n - 1], right[n - 1], (left[n - 1].XY + right[n - 1].XY) * 0.5 - (left[n - 2].XY + right[n - 2].XY) * 0.5, c.CutSlope, c.FillSlope, ground, maxDaylight, step);
        }
        foreach (var pad in pads)
        {
            var outer = pad.Area.Outer.ToList();
            if (outer.Count < 3) continue;
            var pd = pad;
            Func<Vec2, double> z = p => pd.At(p) + baseZ;
            res.Pads.Add((pad.Area, z));
            res.Footprint.Add(pad.Area);
            var ring = CurveTools.Densify(outer, 1.0);
            if (ring.Count > 1 && ring[0].AlmostEquals(ring[^1], 1e-6)) ring.RemoveAt(ring.Count - 1);
            res.Points.AddRange(CurveTools.Densify(outer, 0.5).Select(p => Vec3.At(p, z(p))));
            // Quebras da superfície (crista e fim do caimento) dentro da plataforma.
            var breaks = new List<List<Vec2>>();
            foreach (var bl in pad.Breaklines)
            {
                var line = CurveTools.Densify(bl, 1.0).Where(p => pad.Area.Contains(p) && Distance(ring, p) > 0.3).ToList();
                res.Points.AddRange(line.Select(p => Vec3.At(p, z(p))));
                if (line.Count > 1) breaks.Add(line);
            }
            // Pontos internos: a plataforma segue a sua superfície também no miolo (onde ela é plana bastam os de 4 m).
            var (mn, mx) = pad.Area.Bounds;
            var lattice = Math.Max(1, (int)Math.Round(4.0 / padGrid));
            int ix = 0;
            for (var x = mn.X + padGrid / 2; x < mx.X; x += padGrid, ix++)
            {
                int iy = 0;
                for (var y = mn.Y + padGrid / 2; y < mx.Y; y += padGrid, iy++)
                {
                    var p = new Vec2(x, y);
                    if (!pad.Area.Contains(p) || Distance(ring, p) <= 0.5 || breaks.Any(bl => Distance(bl, p, false) <= 0.5)) continue;
                    var zp = z(p);
                    var curved = Math.Abs(z(p + new Vec2(padGrid, 0)) - 2 * zp + z(p - new Vec2(padGrid, 0))) > 0.002
                                 || Math.Abs(z(p + new Vec2(0, padGrid)) - 2 * zp + z(p - new Vec2(0, padGrid))) > 0.002;
                    if (curved || ix % lattice == 0 && iy % lattice == 0) res.Points.Add(Vec3.At(p, zp));
                }
            }
            if (pad.Daylight) PadSlopes(res, ring, z, pad.CutSlope, pad.FillSlope, ground, maxDaylight, step);
        }
        SetReach(res, ground, maxDaylight);
        SlopePoints(res, ground);
        res.Points.RemoveAll(p => double.IsNaN(p.Z));
        Volumes(res, ground, sampling);
        return res;
    }

    /// <summary>Ponto mais próximo do contorno dos polígonos (externos e furos).</summary>
    private static Vec2? Nearest(IEnumerable<Polygon2> polys, Vec2 p)
    {
        Vec2? best = null;
        var bd = double.MaxValue;
        foreach (var g in polys)
            foreach (var ring in g.Holes.Prepend(g.Outer))
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Count];
                    var ab = b - a;
                    var t = ab.Dot(ab) < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / ab.Dot(ab), 0, 1);
                    var q = a + ab * t;
                    var d = p.DistanceTo(q);
                    if (d < bd) { bd = d; best = q; }
                }
        return best;
    }

    /// <summary>Pé do talude entre um ponto alterado e um natural (bissecção, ~1 cm): ponto no terreno natural.</summary>
    private static Vec3? Toe(GradingResult res, Func<Vec2, double?> ground, Vec2 altered, Vec2 natural)
    {
        var a = altered;
        var b = natural;
        for (int i = 0; i < 6; i++)
        {
            var m = (a + b) * 0.5;
            if (res.DesignZ(m) == null) b = m; else a = m;
        }
        return ground(b) is { } g ? Vec3.At(b, g) : null;
    }

    /// <summary>Alcance das bordas pela faixa de cotas do terreno em volta delas (amostras a cada 4 m).</summary>
    private static void SetReach(GradingResult res, Func<Vec2, double?> ground, double maxDist)
    {
        if (res.SlopeEdges.Count == 0) return;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var v in res.SlopeEdges.SelectMany(e => e.Line))
        {
            x0 = Math.Min(x0, v.X); y0 = Math.Min(y0, v.Y); x1 = Math.Max(x1, v.X); y1 = Math.Max(y1, v.Y);
        }
        x0 -= maxDist; y0 -= maxDist; x1 += maxDist; y1 += maxDist;
        var h = Math.Max(4.0, Math.Sqrt((x1 - x0) * (y1 - y0) / 250_000));
        double gMin = double.MaxValue, gMax = double.MinValue;
        for (var x = x0; x <= x1; x += h)
            for (var y = y0; y <= y1; y += h)
                if (ground(new Vec2(x, y)) is { } g) { gMin = Math.Min(gMin, g); gMax = Math.Max(gMax, g); }
        if (gMin > gMax) return;
        foreach (var e in res.SlopeEdges) e.SetReach(gMin - 0.5, gMax + 0.5, maxDist);
    }

    /// <summary>Espaçamento (m) dos pontos dos taludes no terreno (malha regular sobre a área dos taludes).</summary>
    public const double SlopeGrid = 1.0;

    /// <summary>
    /// Pontos dos taludes para o Toposolid: malha regular sobre a área alterada dos taludes (cota da superfície contínua), mais
    /// os pés/cristas nas normais das bordas – a triangulação do Toposolid reproduz a superfície de projeto (antes eram só os pés
    /// e alguns pontos na face, e a triangulação cruzava os taludes).
    /// </summary>
    private static void SlopePoints(GradingResult res, Func<Vec2, double?> ground)
    {
        if (res.SlopeEdges.Count == 0) return;
        var inFootprint = TerrainRebuild.Mask(res.Footprint.Where(f => Math.Abs(f.Area) > 1e-4), 0);
        // Sem pontos da malha a menos de 0,5 m das plataformas: as bordas delas (pontos a cada 1 m) ficam como arestas da
        // triangulação do Toposolid, que não tem linhas de quebra.
        var platform = TerrainRebuild.Mask(res.Pads.Select(p => p.Area).Concat(res.PlatformAreas), 0.5);
        // Varredura sobre o alcance das bordas: a área alterada é onde a superfície de projeto difere do terreno.
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var e in res.SlopeEdges)
            foreach (var v in e.Line)
            {
                x0 = Math.Min(x0, v.X - e.Reach); y0 = Math.Min(y0, v.Y - e.Reach);
                x1 = Math.Max(x1, v.X + e.Reach); y1 = Math.Max(y1, v.Y + e.Reach);
            }
        var h = SlopeGrid;
        if ((x1 - x0) * (y1 - y0) / (h * h) > 600_000) h = Math.Sqrt((x1 - x0) * (y1 - y0) / 600_000);
        x0 = Math.Floor(x0 / h) * h;
        y0 = Math.Floor(y0 / h) * h;
        var extra = new List<Polygon2>();
        var half = h * 0.55;
        var nx = (int)Math.Floor((x1 - x0) / h) + 1;
        var ny = (int)Math.Floor((y1 - y0) / h) + 1;
        var zs = new double?[nx, ny];
        var plat = new bool[nx, ny];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var p = new Vec2(x0 + i * h, y0 + j * h);
                if (platform(p)) { plat[i, j] = true; continue; }
                zs[i, j] = res.DesignZ(p);
            }
        // Pontos da malha: onde a superfície dobra (pé, crista, encontro de taludes), na divisa da área alterada e a cada 4 m;
        // nas faces planas dos taludes os pontos intermediários não mudam a triangulação.
        var lattice = Math.Max(1, (int)Math.Round(4.0 / h));
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                if (zs[i, j] is not { } z) continue;
                var p = new Vec2(x0 + i * h, y0 + j * h);
                if (!inFootprint(p)) extra.Add(Polygon2.Rectangle(p - new Vec2(half, half), p + new Vec2(half, half)));
                var keep = i % lattice == 0 && j % lattice == 0 || i == 0 || j == 0 || i == nx - 1 || j == ny - 1;
                if (!keep)
                {
                    var zl = zs[i - 1, j]; var zr = zs[i + 1, j]; var zd = zs[i, j - 1]; var zu = zs[i, j + 1];
                    keep = zl == null || zr == null || zd == null || zu == null || plat[i - 1, j] || plat[i + 1, j] || plat[i, j - 1] || plat[i, j + 1]
                           || Math.Abs(zl.Value - 2 * z + zr.Value) > 0.005 || Math.Abs(zd.Value - 2 * z + zu.Value) > 0.005;
                    if (!keep)
                    {
                        // Diagonais também (encontro de taludes em diagonal).
                        var a1 = zs[i - 1, j - 1]; var a2 = zs[i + 1, j + 1]; var b1 = zs[i - 1, j + 1]; var b2 = zs[i + 1, j - 1];
                        keep = a1 == null || a2 == null || b1 == null || b2 == null
                               || Math.Abs(a1.Value - 2 * z + a2.Value) > 0.005 || Math.Abs(b1.Value - 2 * z + b2.Value) > 0.005;
                    }
                }
                if (keep) res.Points.Add(Vec3.At(p, z));
            }
        if (extra.Count > 0) res.Footprint.AddRange(PolygonOps.Union(extra));
        // Junto às plataformas (até 2,5 m) a malha fica mais fina (0,5 m) onde o talude dobra – encontro de dois taludes num
        // canto, cone de uma ponta de via.
        var near = TerrainRebuild.Mask(res.Pads.Select(p => p.Area).Concat(res.PlatformAreas), 2.5);
        var hf = h / 2;
        for (int i = 0; i < 2 * nx; i++)
            for (int j = 0; j < 2 * ny; j++)
            {
                if (i % 2 == 0 && j % 2 == 0) continue;
                var p = new Vec2(x0 + i * hf, y0 + j * hf);
                if (!near(p) || platform(p)) continue;
                if (res.DesignZ(p) is not { } z)
                {
                    // Terreno natural colado na área alterada (o pé do talude): ponto natural também e o pé exato entre ele e
                    // o vizinho alterado – senão a triangulação ligava o pé aos pontos do levantamento, a metros dali.
                    if (ground(p) is not { } gz) continue;
                    var touches = false;
                    foreach (var dv in new[] { new Vec2(hf, 0), new Vec2(-hf, 0), new Vec2(0, hf), new Vec2(0, -hf) })
                    {
                        if (res.DesignZ(p + dv) == null || platform(p + dv)) continue;
                        touches = true;
                        if (Toe(res, ground, p + dv, p) is { } toe) res.GroundPoints.Add(toe);
                    }
                    if (touches) res.GroundPoints.Add(Vec3.At(p, gz));
                    continue;
                }
                var bend = false;
                foreach (var dv in new[] { new Vec2(hf, 0), new Vec2(0, hf), new Vec2(hf, hf), new Vec2(hf, -hf) })
                {
                    if (res.DesignZ(p + dv) is not { } za || res.DesignZ(p - dv) is not { } zb) { bend = true; break; }
                    if (Math.Abs(za - 2 * z + zb) > 0.003) { bend = true; break; }
                }
                if (bend) res.Points.Add(Vec3.At(p, z));
            }
        // Contornos a 0,3 e 0,6 m das plataformas (a malha começa a 0,5 m): o talude logo junto à borda – pé a poucos
        // centímetros dela – fica no Toposolid.
        var inside = TerrainRebuild.Mask(res.Pads.Select(p => p.Area).Concat(res.PlatformAreas), 0.5);
        try
        {
            var plats = PolygonOps.Union(res.Pads.Select(p => p.Area).Concat(res.PlatformAreas).Where(a => a.Area > 1e-4));
            var inside3 = TerrainRebuild.Mask(res.Pads.Select(p => p.Area).Concat(res.PlatformAreas), 0.25);
            foreach (var (off, step, msk) in new[] { (0.3, 0.25, inside3), (0.6, 1.0, inside) })
                foreach (var g in PolygonOps.Offset(plats, off))
                    foreach (var ring in g.Holes.Prepend(g.Outer))
                        foreach (var q in CurveTools.Densify(ring.Append(ring[0]).ToList(), step))
                        {
                            if (msk(q)) continue;
                            var dq = res.DesignZ(q);
                            if (dq is { } z) res.Points.Add(Vec3.At(q, z));
                            else if (ground(q) is { } gz) res.GroundPoints.Add(Vec3.At(q, gz));
                            if (off > 0.4) continue;
                            // Pé do talude perto da borda: procurado na normal, entre a borda e este contorno (vale natural colado na
                            // pista) ou daqui até 3 m adiante.
                            var bp = Nearest(plats, q);
                            if (bp == null || q.DistanceTo(bp.Value) < 1e-6) continue;
                            var nrm = (q - bp.Value).Normalized();
                            var q0 = bp.Value + nrm * 0.02;
                            if (dq == null)
                            {
                                if (res.DesignZ(q0) != null && Toe(res, ground, q0, q) is { } t0) res.GroundPoints.Add(t0);
                            }
                            else
                            {
                                // Pé até 3 m adiante, a cada 0,25 m do contorno: a linha do pé vira aresta do Toposolid (com os pés só
                                // a cada 1 m, a triangulação ligava o talude ao terreno natural cruzando o pé – 10 cm de erro).
                                var prev = q;
                                foreach (var dd in new[] { 0.3, 0.7, 1.2, 2.0, 3.0 })
                                {
                                    var qn = q + nrm * dd;
                                    if (inside(qn)) { prev = qn; continue; }
                                    if (res.DesignZ(qn) != null) { prev = qn; continue; }
                                    if (Toe(res, ground, prev, qn) is { } tn)
                                    {
                                        res.GroundPoints.Add(tn);
                                        if (ground(qn) is { } gn) res.GroundPoints.Add(Vec3.At(qn, gn));
                                    }
                                    break;
                                }
                            }
                        }
            // Cantos convexos das plataformas (pontas das vias, esquinas): pontos na bissetriz para fora – a aresta onde o
            // talude lateral encontra o da ponta (ou o cone do canto) fica no Toposolid.
            foreach (var g in plats)
                foreach (var ring in g.Holes.Prepend(g.Outer))
                    for (int i = 0; i < ring.Count; i++)
                    {
                        var a = ring[(i - 1 + ring.Count) % ring.Count];
                        var b = ring[i];
                        var c = ring[(i + 1) % ring.Count];
                        if (a.DistanceTo(b) < 1e-6 || c.DistanceTo(b) < 1e-6) continue;
                        var u1 = (b - a).Normalized();
                        var u2 = (c - b).Normalized();
                        if (u1.Cross(u2) < Math.Sin(20 * Math.PI / 180)) continue;            // só cantos convexos (≥ 20°)
                        // Leque de raios (a cada ≤ 15°) entre as normais dos dois lados: o cone do canto é curvo.
                        var a0 = Math.Atan2(u1.PerpRight.Y, u1.PerpRight.X);
                        var sweep = Math.Acos(Math.Clamp(u1.PerpRight.Dot(u2.PerpRight), -1, 1));
                        var nRays = Math.Max(1, (int)Math.Ceiling(sweep / (15 * Math.PI / 180)));
                        for (int r = 0; r <= nRays; r++)
                        {
                            var ang = a0 + sweep * r / nRays;
                            var dir = new Vec2(Math.Cos(ang), Math.Sin(ang));
                            var misses = 0;
                            foreach (var d in new[] { 0.3, 0.6, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 11.0, 15.0, 20.0, 30.0 })
                            {
                                var q = b + dir * d;
                                if (inside3(q)) continue;
                                if (res.DesignZ(q) is { } z) { res.Points.Add(Vec3.At(q, z)); misses = 0; }
                                else if (++misses >= 2) break;
                            }
                        }
                    }
        }
        catch (Exception) { /* contorno opcional */ }
    }

    private static double Distance(IReadOnlyList<Vec2> ring, Vec2 p, bool closed = true)
    {
        var best = double.MaxValue;
        for (int i = 0; i < (closed ? ring.Count : ring.Count - 1); i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            var ab = b - a;
            var t = ab.Dot(ab) < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / ab.Dot(ab), 0, 1);
            best = Math.Min(best, p.DistanceTo(a + ab * t));
        }
        return best;
    }

    /// <summary>
    /// Ponto em que o talude que sai da borda (cota <paramref name="z"/>) encontra o terreno, andando na direção <paramref name="n"/>:
    /// corte sobe (1 : <paramref name="cut"/>), aterro desce (1 : <paramref name="fill"/>).
    /// </summary>
    public static Vec3 Daylight(Vec2 p, double z, Vec2 n, double cut, double fill, Func<Vec2, double?> ground, double maxDist = 60, double step = 0.5)
    {
        var g0 = ground(p);
        if (g0 == null) return Vec3.At(p, z);
        var dz0 = g0.Value - z;
        if (Math.Abs(dz0) < 0.01) return Vec3.At(p, z);
        var isCut = dz0 > 0;
        var ratio = Math.Max(0.1, isCut ? cut : fill);
        double prevD = 0, prevDiff = dz0;
        for (var d = step; d <= maxDist + 1e-9; d += step)
        {
            var q = p + n * d;
            var g = ground(q);
            if (g == null) return Vec3.At(p + n * prevD, z + (isCut ? 1 : -1) * prevD / ratio);
            var s = z + (isCut ? 1 : -1) * d / ratio;
            var diff = g.Value - s;
            if (isCut ? diff <= 0 : diff >= 0)
            {
                // Interpolação linear entre as duas últimas amostras.
                var t = Math.Abs(prevDiff - diff) < 1e-12 ? 1 : prevDiff / (prevDiff - diff);
                var dd = prevD + (d - prevD) * t;
                return Vec3.At(p + n * dd, z + (isCut ? 1 : -1) * dd / ratio);
            }
            prevD = d;
            prevDiff = diff;
        }
        var far = p + n * maxDist;
        return Vec3.At(far, ground(far) ?? z + (isCut ? 1 : -1) * maxDist / ratio);
    }

    /// <summary>Afastamento do degrau de muro/emboque (m): face quase vertical do terreno, escondida pela estrutura.</summary>
    public const double WallGap = 0.10;

    /// <summary>
    /// Degrau de muro: linha de pontos a <see cref="WallGap"/> para fora da borda, no terreno natural. A faixa estreita entre a
    /// borda (no projeto) e essa linha vira a face do corte/aterro contido pelo muro.
    /// </summary>
    private static List<Vec3> WallBand(GradingResult res, List<Vec3> edge, List<Vec3> other, Func<Vec2, double?> ground)
    {
        var outer = new List<Vec3>();
        for (int i = 0; i < edge.Count; i++)
        {
            var e = edge[i].XY;
            var n = (e - other[i].XY).Length < 1e-9 ? Vec2.UnitY : (e - other[i].XY).Normalized();
            var q = e + n * WallGap;
            outer.Add(Vec3.At(q, ground(q) ?? edge[i].Z));
        }
        Strip(res, edge, outer);
        res.Points.AddRange(outer);
        // Pontos atrás do muro tão densos quanto os da borda da plataforma: senão a triangulação passava entre eles e ligava
        // a borda da pista ao terreno natural mais longe (rampa de terra atrás do muro).
        for (int i = 0; i + 1 < edge.Count; i++)
        {
            var k = (int)Math.Ceiling(edge[i].XY.DistanceTo(edge[i + 1].XY) / 0.5);
            for (int j = 1; j < k; j++)
            {
                var q = outer[i].XY + (outer[i + 1].XY - outer[i].XY) * ((double)j / k);
                var z = ground(q) ?? outer[i].Z + (outer[i + 1].Z - outer[i].Z) * j / k;
                res.Points.Add(Vec3.At(q, z));
            }
        }
        return outer;
    }

    /// <summary>Face vertical transversal no início/fim de uma faixa (emboque, encontro): de uma crista de talude à outra.</summary>
    private static void EndWall(GradingResult res, Vec3 dayL, Vec3 left, Vec3 right, Vec3 dayR, Vec2 outward, Func<Vec2, double?> ground,
        double? cap = null, double cut = 1.0, double maxDist = 60, double step = 0.5, double? spillFill = null)
    {
        if (outward.Length < 1e-9) return;
        var dir = outward.Normalized();
        if (spillFill is { } fs)
        {
            // Saia: da ponta, talude à frente (aterro na razão do aterro, corte na do corte) até encontrar o terreno.
            var edgeLine = new List<Vec3>();
            foreach (var (a, b) in new[] { (dayL, left), (left, right), (right, dayR) })
            {
                var len = a.XY.DistanceTo(b.XY);
                var k = Math.Max(1, (int)Math.Ceiling(len / 1.5));
                for (int i = 0; i < k; i++) edgeLine.Add(a + (b - a) * ((double)i / k));
            }
            edgeLine.Add(dayR);
            var toe = edgeLine.Select(p => Daylight(p.XY, p.Z, dir, cut, fs, ground, maxDist, step)).ToList();
            for (int i = 0; i + 1 < edgeLine.Count; i++)
            {
                res.Triangles.Add((edgeLine[i], edgeLine[i + 1], toe[i + 1]));
                res.Triangles.Add((edgeLine[i], toe[i + 1], toe[i]));
                res.TriangleIsSlope.Add(true);
                res.TriangleIsSlope.Add(true);
                var quad = new Polygon2(new[] { edgeLine[i].XY, edgeLine[i + 1].XY, toe[i + 1].XY, toe[i].XY });
                if (Math.Abs(quad.Area) > 1e-4) res.Footprint.Add(quad);
            }
            res.Points.AddRange(edgeLine);
            for (int i = 0; i < toe.Count; i++)
            {
                var w = toe[i].XY.DistanceTo(edgeLine[i].XY);
                if (w < 0.05) continue;
                res.Points.Add(toe[i]);
                var k = (int)Math.Floor(w / 3.0);
                for (int j = 1; j <= k; j++) res.Points.Add(edgeLine[i] + (toe[i] - edgeLine[i]) * ((double)j / (k + 1)));
            }
            return;
        }
        var o = dir * WallGap;
        var capZ = cap is { } h ? Math.Max(left.Z, right.Z) + h : double.MaxValue;
        var line = new List<Vec3>();
        foreach (var (a, b) in new[] { (dayL, left), (left, right), (right, dayR) })
        {
            var len = a.XY.DistanceTo(b.XY);
            var k = Math.Max(1, (int)Math.Ceiling(len / 1.0));
            for (int i = 0; i < k; i++) line.Add(a + (b - a) * ((double)i / k));
        }
        line.Add(dayR);
        var outer = line.Select(p => { var q = p.XY + o; return Vec3.At(q, Math.Min(capZ, ground(q) ?? p.Z)); }).ToList();
        // Pontos da borda de projeto também (a crista/base do corte fica marcada).
        res.Points.AddRange(line);
        Strip(res, line, outer);
        res.Points.AddRange(outer);
        if (cap == null) return;
        // Acima da face (testa do emboque): talude de corte para dentro do maciço até reencontrar o terreno.
        var day = outer.Select(p => (ground(p.XY) ?? p.Z) > p.Z + 0.01 ? Daylight(p.XY, p.Z, dir, cut, cut, ground, maxDist, step) : p).ToList();
        for (int i = 0; i + 1 < outer.Count; i++)
        {
            res.Triangles.Add((outer[i], outer[i + 1], day[i + 1]));
            res.Triangles.Add((outer[i], day[i + 1], day[i]));
            res.TriangleIsSlope.Add(true);
            res.TriangleIsSlope.Add(true);
            var quad = new Polygon2(new[] { outer[i].XY, outer[i + 1].XY, day[i + 1].XY, day[i].XY });
            if (Math.Abs(quad.Area) > 1e-4) res.Footprint.Add(quad);
        }
        for (int i = 0; i < outer.Count; i++)
        {
            var w = day[i].XY.DistanceTo(outer[i].XY);
            if (w < 0.05) continue;
            res.Points.Add(day[i]);
            var k = (int)Math.Floor(w / 3.0);
            for (int j = 1; j <= k; j++) res.Points.Add(outer[i] + (day[i] - outer[i]) * ((double)j / (k + 1)));
        }
    }

    /// <summary>Faixa estreita (degrau) entre duas linhas paralelas: triângulos de talude e contorno.</summary>
    private static void Strip(GradingResult res, List<Vec3> a, List<Vec3> b)
    {
        for (int i = 0; i + 1 < a.Count; i++)
        {
            res.Triangles.Add((a[i], a[i + 1], b[i + 1]));
            res.Triangles.Add((a[i], b[i + 1], b[i]));
            res.TriangleIsSlope.Add(true);
            res.TriangleIsSlope.Add(true);
            var quad = new Polygon2(new[] { a[i].XY, a[i + 1].XY, b[i + 1].XY, b[i].XY });
            if (Math.Abs(quad.Area) > 1e-5) res.Footprint.Add(quad);
        }
    }

    /// <summary>
    /// Borda com talude: registra a borda na superfície contínua dos taludes e devolve os pés/cristas nas normais (contorno da
    /// área alterada e pontos do terreno).
    /// </summary>
    private static List<Vec3> SlopeLine(GradingResult res, List<Vec3> edge, List<Vec3> other, double cut, double fill, Func<Vec2, double?> ground,
        double maxDist, double step, bool openStart, bool openEnd)
    {
        var day = new List<Vec3>();
        for (int i = 0; i < edge.Count; i++)
        {
            var e = edge[i].XY;
            var o = other[i].XY;
            var n = (e - o).Length < 1e-9 ? Vec2.UnitY : (e - o).Normalized();
            day.Add(Daylight(e, edge[i].Z, n, cut, fill, ground, maxDist, step));
        }
        var se = new SlopeEdge { Cut = cut, Fill = fill, OpenStart = openStart, OpenEnd = openEnd };
        se.Line.AddRange(edge);
        se.Day.AddRange(day.Select((d, i) => d.XY.DistanceTo(edge[i].XY)));
        se.Finish(maxDist);
        res.SlopeEdges.Add(se);
        for (int i = 0; i + 1 < edge.Count; i++)
        {
            var quad = new Polygon2(new[] { edge[i].XY, edge[i + 1].XY, day[i + 1].XY, day[i].XY });
            if (Math.Abs(quad.Area) > 1e-4) res.Footprint.Add(quad);
        }
        // Pontas abertas: o talude contorna a ponta em cone (o contorno cobre o círculo da ponta).
        if (edge.Count > 0 && openStart && se.Day[0] > 0.1) res.Footprint.Add(new Polygon2(CurveTools.Circle(edge[0].XY, se.Day[0], 0.1)));
        if (edge.Count > 1 && openEnd && se.Day[^1] > 0.1) res.Footprint.Add(new Polygon2(CurveTools.Circle(edge[^1].XY, se.Day[^1], 0.1)));
        res.Points.AddRange(day.Where((d, i) => d.XY.DistanceTo(edge[i].XY) > 0.05));
        return day;
    }

    /// <summary>Ponta livre da faixa: talude à frente da ponta, de <paramref name="a"/> a <paramref name="b"/>, até o terreno.</summary>
    private static void EndSlope(GradingResult res, Vec3 a, Vec3 b, Vec2 outward, double cut, double fill, Func<Vec2, double?> ground, double maxDist, double step)
    {
        if (outward.Length < 1e-9) return;
        var dir = outward.Normalized();
        var line = new List<Vec3>();
        var k = Math.Max(1, (int)Math.Ceiling(a.XY.DistanceTo(b.XY) / 1.5));
        for (int i = 0; i <= k; i++) line.Add(a + (b - a) * ((double)i / k));
        var day = line.Select(p => Daylight(p.XY, p.Z, dir, cut, fill, ground, maxDist, step)).ToList();
        var se = new SlopeEdge { Cut = cut, Fill = fill };
        se.Line.AddRange(line);
        se.Day.AddRange(day.Select((d, i) => d.XY.DistanceTo(line[i].XY)));
        se.Finish(maxDist);
        res.SlopeEdges.Add(se);
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var quad = new Polygon2(new[] { line[i].XY, line[i + 1].XY, day[i + 1].XY, day[i].XY });
            if (Math.Abs(quad.Area) > 1e-4) res.Footprint.Add(quad);
        }
        // Cones dos cantos: o contorno cobre o quarto de círculo entre o talude lateral e o da ponta.
        foreach (var (p, d) in new[] { (line[0], day[0]), (line[^1], day[^1]) })
        {
            var r = p.XY.DistanceTo(d.XY);
            if (r > 0.1) res.Footprint.Add(new Polygon2(CurveTools.Circle(p.XY, r, 0.05)));
        }
        res.Points.AddRange(line);
        res.Points.AddRange(day.Where((d, i) => d.XY.DistanceTo(line[i].XY) > 0.05));
    }

    private static void PadSlopes(GradingResult res, List<Vec2> ring, Func<Vec2, double> zAt, double cut, double fill, Func<Vec2, double?> ground, double maxDist, double step)
    {
        var ccw = Polygon2.SignedArea(ring) > 0;
        var day = new List<Vec3>();
        for (int i = 0; i < ring.Count; i++)
        {
            var prev = ring[(i - 1 + ring.Count) % ring.Count];
            var next = ring[(i + 1) % ring.Count];
            var e1 = (ring[i] - prev).Normalized();
            var e2 = (next - ring[i]).Normalized();
            var n1 = ccw ? e1.PerpRight : e1.PerpLeft;
            var n2 = ccw ? e2.PerpRight : e2.PerpLeft;
            var n = (n1 + n2).Length < 1e-9 ? n1 : (n1 + n2).Normalized();
            day.Add(Daylight(ring[i], zAt(ring[i]), n, cut, fill, ground, maxDist, step));
        }
        var se = new SlopeEdge { Cut = cut, Fill = fill, Closed = true };
        se.Line.AddRange(ring.Select(r => Vec3.At(r, zAt(r))));
        se.Day.AddRange(day.Select((d, i) => d.XY.DistanceTo(ring[i])));
        se.Finish(maxDist);
        res.SlopeEdges.Add(se);
        for (int i = 0; i < ring.Count; i++)
        {
            var j = (i + 1) % ring.Count;
            var quad = new Polygon2(new[] { ring[i], ring[j], day[j].XY, day[i].XY });
            if (Math.Abs(quad.Area) > 1e-4) res.Footprint.Add(quad);
        }
        res.Points.AddRange(day.Where((d, i) => d.XY.DistanceTo(ring[i]) > 0.05));
    }

    /// <summary>Volumes de corte e aterro por malha de amostragem (m³).</summary>
    public static void Volumes(GradingResult res, Func<Vec2, double?> ground, double cell)
    {
        if (res.Footprint.Count == 0) return;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var f in res.Footprint)
        {
            var (mn, mx) = f.Bounds;
            minX = Math.Min(minX, mn.X); minY = Math.Min(minY, mn.Y);
            maxX = Math.Max(maxX, mx.X); maxY = Math.Max(maxY, mx.Y);
        }
        cell = Math.Max(0.25, cell);
        // Malha limitada (projetos grandes): no máximo ~250 mil amostras.
        var n = (maxX - minX) * (maxY - minY) / (cell * cell);
        if (n > 250_000) cell *= Math.Sqrt(n / 250_000);
        double cut = 0, fill = 0, area = 0;
        for (var x = minX + cell / 2; x < maxX; x += cell)
            for (var y = minY + cell / 2; y < maxY; y += cell)
            {
                var p = new Vec2(x, y);
                var dz = res.DesignZ(p);
                if (dz == null) continue;
                var g = ground(p);
                if (g == null) continue;
                area += cell * cell;
                var d = g.Value - dz.Value;
                if (d > 0) cut += d * cell * cell; else fill -= d * cell * cell;
            }
        res.CutM3 = cut;
        res.FillM3 = fill;
        res.AreaM2 = area;
    }
}
