using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>Ponto de inflexão vertical (PIV) do greide: estação ao longo do eixo, cota e curva vertical nesse PIV.</summary>
public sealed class GradePoint
{
    public double S { get; set; }
    /// <summary>Cota relativa à base do eixo (m).</summary>
    public double Z { get; set; }
    /// <summary>Comprimento da curva vertical (parábola) neste PIV – nulo = padrão do greide.</summary>
    public double? Curve { get; set; }

    public GradePoint() { }
    public GradePoint(double s, double z, double? curve = null) { S = s; Z = z; Curve = curve; }
}

/// <summary>
/// Greide da via (perfil longitudinal): PIVs com curvas verticais parabólicas, caimento transversal (abaulamento) e
/// superelevação ao longo do eixo. Fica gravado nas saídas de todas as marcas da via – pista, meios-fios, calçadas,
/// linhas e dispositivos acompanham o mesmo greide; obras de arte, túneis e trincheiras hospedados na via também.
/// </summary>
public sealed class RoadGrade
{
    public List<GradePoint> Points { get; set; } = new();
    /// <summary>Curva vertical padrão nos PIVs internos (m).</summary>
    public double DefaultCurve { get; set; } = 60;
    /// <summary>Caimento transversal da pista (m/m) em duas águas a partir do eixo – 0 = pista plana.</summary>
    public double Crossfall { get; set; }
    /// <summary>Meia largura em que vale o abaulamento (m): além dela a cota segue a do bordo (calçadas, canteiros).</summary>
    public double CrossfallWidth { get; set; } = 7;
    /// <summary>Superelevação (estação, m/m) interpolada linearmente; positivo = lado esquerdo mais alto.</summary>
    public List<Vec2> Superelevation { get; set; } = new();
    /// <summary>A via molda o terreno (Toposolid): plataforma e taludes de corte e aterro.</summary>
    public bool AdjustTerrain { get; set; } = true;
    public double CutSlope { get; set; } = 1.0;
    public double FillSlope { get; set; } = 1.5;
    /// <summary>
    /// Greide de projeto da via SEM as obras hospedadas (pontes elevam, trincheiras rebaixam): as obras são reaplicadas sobre ele
    /// sempre que uma delas muda – editar ou apagar uma obra não bagunça o greide das outras.
    /// </summary>
    public RoadGrade? WithoutWorks { get; set; }

    [JsonIgnore]
    public bool IsFlat => Points.Count == 0 || (Points.All(p => Math.Abs(p.Z) < 1e-6) && Superelevation.All(e => Math.Abs(e.Y) < 1e-9) && Crossfall < 1e-9);

    public static RoadGrade Flat(double length) => new() { Points = { new(0, 0), new(Math.Max(1, length), 0) } };

    public RoadGrade Clone() => new()
    {
        Points = Points.Select(p => new GradePoint(p.S, p.Z, p.Curve)).ToList(),
        DefaultCurve = DefaultCurve, Crossfall = Crossfall, CrossfallWidth = CrossfallWidth,
        Superelevation = new List<Vec2>(Superelevation), AdjustTerrain = AdjustTerrain, CutSlope = CutSlope, FillSlope = FillSlope,
        WithoutWorks = WithoutWorks?.Clone(),
    };

    private List<GradePoint> Sorted()
    {
        if (_sorted != null && _sortedCount == Points.Count) return _sorted;
        _sorted = Points.OrderBy(p => p.S).ToList();
        _sortedCount = Points.Count;
        return _sorted;
    }
    private List<GradePoint>? _sorted;
    private int _sortedCount = -1;

    /// <summary>Esquece o cache de ordenação depois de alterar os PIVs.</summary>
    public void Touch() => _sorted = null;

    private static double Slope(GradePoint a, GradePoint b) => b.S - a.S > 1e-9 ? (b.Z - a.Z) / (b.S - a.S) : 0;

    /// <summary>Cota do greide (m, relativa) na estação <paramref name="s"/> – tangentes e parábolas nos PIVs.</summary>
    public double Z(double s)
    {
        var p = Sorted();
        if (p.Count == 0) return 0;
        if (p.Count == 1) return p[0].Z;
        if (s <= p[0].S) return p[0].Z;
        if (s >= p[^1].S) return p[^1].Z;
        for (int i = 1; i + 1 < p.Count; i++)
        {
            var g1 = Slope(p[i - 1], p[i]);
            var g2 = Slope(p[i], p[i + 1]);
            // Curva pedida ou, no mínimo, K = 10 m por % de variação de rampa (sem "quinas" no greide); limitada para que as
            // curvas de PIVs vizinhos nunca se sobreponham (a sobreposição criava degraus e calombos na pista).
            var want = Math.Max(p[i].Curve ?? DefaultCurve, 10 * Math.Abs(g2 - g1) * 100);
            var lv = Math.Min(want, 0.98 * Math.Min(p[i].S - p[i - 1].S, p[i + 1].S - p[i].S));
            if (lv < 0.01) continue;
            var x0 = p[i].S - lv / 2;
            if (s < x0 || s > p[i].S + lv / 2) continue;
            var x = s - x0;
            return p[i].Z - g1 * lv / 2 + g1 * x + (g2 - g1) / (2 * lv) * x * x;
        }
        for (int i = 0; i + 1 < p.Count; i++)
            if (s <= p[i + 1].S) return p[i].Z + Slope(p[i], p[i + 1]) * (s - p[i].S);
        return p[^1].Z;
    }

    /// <summary>Rampa (m/m) na estação.</summary>
    public double GradeAt(double s) => (Z(s + 0.5) - Z(s - 0.5));

    /// <summary>Superelevação (m/m) na estação.</summary>
    public double E(double s)
    {
        var e = Superelevation;
        if (e.Count == 0) return 0;
        if (s <= e[0].X) return e[0].Y;
        for (int i = 0; i + 1 < e.Count; i++)
            if (s <= e[i + 1].X)
                return e[i + 1].X - e[i].X < 1e-9 ? e[i + 1].Y : e[i].Y + (e[i + 1].Y - e[i].Y) * (s - e[i].X) / (e[i + 1].X - e[i].X);
        return e[^1].Y;
    }

    /// <summary>Cota na estação <paramref name="s"/> e afastamento <paramref name="y"/> (positivo à esquerda do eixo).</summary>
    public double Z(double s, double y)
    {
        var z = Z(s);
        var yc = Math.Clamp(y, -CrossfallWidth, CrossfallWidth);
        var e = E(s);
        if (Math.Abs(e) > 1e-9) return z + e * yc;
        return z - Crossfall * Math.Abs(yc);
    }

    /// <summary>Greide equivalente para os geradores (curva vertical única).</summary>
    public VerticalProfile ToProfile(double step = 5)
    {
        var p = Sorted();
        var prof = new VerticalProfile();
        if (p.Count == 0) { prof.Pvis.Add((0, 0)); prof.Pvis.Add((1e9, 0)); return prof; }
        var a = p[0].S;
        var b = p[^1].S;
        for (var s = a; s < b; s += step) prof.Pvis.Add((s, Z(s)));
        prof.Pvis.Add((b, Z(b)));
        if (prof.Pvis.Count < 2) prof.Pvis.Add((a + 1, prof.Pvis[0].Z));
        return prof;
    }

    /// <summary>
    /// Eleva o greide num trecho: cota <paramref name="height"/> (relativa) entre <paramref name="s0"/> e <paramref name="s1"/>,
    /// com rampas de <paramref name="maxGrade"/> fora do trecho até reencontrar o greide atual. Mantém o que já existe fora.
    /// </summary>
    public void RaiseBetween(double s0, double s1, double height, double maxGrade, double length, double? curve = null, double? heightEnd = null)
    {
        maxGrade = Math.Max(0.005, maxGrade);
        var old = Clone();
        var h1 = heightEnd ?? height;
        var run0 = Math.Abs(height - old.Z(s0)) / maxGrade;
        var run1 = Math.Abs(h1 - old.Z(s1)) / maxGrade;
        var a = Math.Max(0, s0 - run0);
        var b = Math.Min(length, s1 + run1);
        var keep = Points.Where(q => q.S < a - 1 || q.S > b + 1).ToList();
        Points.Clear();
        Points.AddRange(keep);
        if (!Points.Any(q => q.S < a - 1)) Points.Add(new GradePoint(0, a > 1 ? old.Z(0) : height));
        if (a > 1) Points.Add(new GradePoint(a, old.Z(a), curve));
        Points.Add(new GradePoint(Math.Max(0, s0), height, curve));
        Points.Add(new GradePoint(Math.Min(length, s1), h1, curve));
        if (b < length - 1) Points.Add(new GradePoint(b, old.Z(b), curve));
        if (!Points.Any(q => q.S > b + 1)) Points.Add(new GradePoint(length, b < length - 1 ? old.Z(length) : h1));
        Normalize();
    }

    /// <summary>PIVs digitados, um por linha: "estaca; cota; curva (opcional)" – vírgula ou ponto decimal.</summary>
    public static List<GradePoint> ParsePvis(string? text, double stationOffset = 0)
    {
        var res = new List<GradePoint>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var line in (text ?? "").Replace("\\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(new[] { ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().Replace(',', '.')).ToList();
            if (parts.Count < 2) continue;
            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out var s)) continue;
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out var z)) continue;
            double? c = parts.Count > 2 && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out var cv) ? cv : null;
            res.Add(new GradePoint(s + stationOffset, z, c));
        }
        return res.OrderBy(q => q.S).ToList();
    }

    /// <summary>Ordena, remove PIVs coincidentes e intermediários colineares.</summary>
    public void Normalize()
    {
        var p = Points.OrderBy(q => q.S).ToList();
        var res = new List<GradePoint>();
        foreach (var q in p)
        {
            if (res.Count > 0 && q.S - res[^1].S < 0.5) { res[^1] = q; continue; }
            res.Add(q);
        }
        for (int i = res.Count - 2; i >= 1; i--)
            if (Math.Abs(Slope(res[i - 1], res[i]) - Slope(res[i], res[i + 1])) < 1e-6) res.RemoveAt(i);
        Points = res;
        Touch();
    }
}

/// <summary>
/// Superfície do greide em planta: projeta o ponto no eixo (estação e afastamento) e devolve a cota do greide, com
/// abaulamento e superelevação. Índice espacial dos segmentos – rápido para milhares de consultas.
/// </summary>
public sealed class GradeSurface
{
    private readonly Polyline2 _axis;
    private readonly RoadGrade _grade;
    private readonly double[] _stations;
    private readonly Dictionary<(int, int), List<int>> _grid = new();
    private const double Cell = 10;

    public GradeSurface(Polyline2 axis, RoadGrade grade)
    {
        _axis = axis;
        _grade = grade;
        var pts = axis.Points;
        _stations = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) _stations[i] = _stations[i - 1] + pts[i].DistanceTo(pts[i - 1]);
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var (a, b) = (pts[i], pts[i + 1]);
            int x0 = K(Math.Min(a.X, b.X)), x1 = K(Math.Max(a.X, b.X)), y0 = K(Math.Min(a.Y, b.Y)), y1 = K(Math.Max(a.Y, b.Y));
            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                {
                    if (!_grid.TryGetValue((x, y), out var l)) _grid[(x, y)] = l = new List<int>();
                    l.Add(i);
                }
        }
    }

    private static int K(double v) => (int)Math.Floor(v / Cell);

    public Polyline2 Axis => _axis;
    public RoadGrade Grade => _grade;

    /// <summary>Estação e afastamento (positivo à esquerda) do ponto em relação ao eixo.</summary>
    public (double S, double Y) Locate(Vec2 p)
    {
        var pts = _axis.Points;
        if (pts.Count < 2) return (0, 0);
        IEnumerable<int> cand = Enumerable.Empty<int>();
        for (var r = 0; r <= 12; r++)
        {
            var set = new HashSet<int>();
            int cx = K(p.X), cy = K(p.Y);
            for (var x = cx - r; x <= cx + r; x++)
                for (var y = cy - r; y <= cy + r; y++)
                    if (_grid.TryGetValue((x, y), out var l)) foreach (var i in l) set.Add(i);
            if (set.Count > 0 && r >= 1) { cand = set; break; }
        }
        var list = cand.ToList();
        if (list.Count == 0) list = Enumerable.Range(0, pts.Count - 1).ToList();
        double best = double.MaxValue, bs = 0, by = 0;
        foreach (var i in list)
        {
            var a = pts[i];
            var ab = pts[i + 1] - a;
            var len2 = ab.Dot(ab);
            var t = len2 < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / len2, 0, 1);
            var q = a + ab * t;
            var d = q.DistanceTo(p);
            if (d < best - 1e-9)
            {
                best = d;
                bs = _stations[i] + t * Math.Sqrt(len2);
                var side = ab.Cross(p - a);
                by = side >= 0 ? d : -d;
            }
        }
        return (bs, by);
    }

    /// <summary>Cota do greide (m, relativa à base do eixo) sob o ponto.</summary>
    public double Z(Vec2 p)
    {
        var (s, y) = Locate(p);
        return _grade.Z(s, y);
    }
}

/// <summary>Aplica o greide da via às peças 3D (sólidos, perfis, primitivas curvas) e às faixas de terraplenagem.</summary>
public static class GradeLift
{
    /// <summary>
    /// Sólidos poliédricos são deformados vértice a vértice (barreiras e dispositivos acompanham curvas verticais); perfis
    /// e primitivas curvas sobem pela cota do centro. Peças planas (pisos, pinturas) ficam para o Revit, que as deforma.
    /// </summary>
    public static void Apply(MarkingGeometry geo, GradeSurface surf)
    {
        for (int i = 0; i < geo.Pieces.Count; i++)
        {
            var p = geo.Pieces[i];
            if (p.Solid is { } poly)
            {
                if (p.Round is { } r)
                {
                    var dz = surf.Z(r.A.XY);
                    geo.Pieces[i] = p with
                    {
                        Solid = poly.Transform(v => v with { Z = v.Z + dz }),
                        Round = r with { A = r.A with { Z = r.A.Z + dz }, B = r.B with { Z = r.B.Z + surf.Z(r.B.XY) } },
                    };
                }
                else geo.Pieces[i] = p with { Solid = Planarize(poly.Transform(v => v with { Z = v.Z + surf.Z(v.XY) })) };
            }
            else if (p.Profile != null)
                geo.Pieces[i] = p with { Elevation = p.Elevation + surf.Z(p.Shape.Centroid) };
        }
        foreach (var c in geo.Corridors)
        {
            for (int k = 0; k < c.Left.Count; k++) c.Left[k] = c.Left[k] with { Z = c.Left[k].Z + surf.Z(c.Left[k].XY) };
            for (int k = 0; k < c.Right.Count; k++) c.Right[k] = c.Right[k] with { Z = c.Right[k].Z + surf.Z(c.Right[k].XY) };
        }
    }

    /// <summary>Faces que deixaram de ser planas viram leques de triângulos.</summary>
    public static Polyhedron Planarize(Polyhedron poly)
    {
        var faces = new List<List<Vec3>>();
        var changed = false;
        foreach (var f in poly.Faces)
        {
            if (f.Count <= 3 || IsPlanar(f)) { faces.Add(f); continue; }
            changed = true;
            for (int i = 1; i + 1 < f.Count; i++) faces.Add(new List<Vec3> { f[0], f[i], f[i + 1] });
        }
        return changed ? Polyhedron.Shell(faces, poly.Plan) : poly;
    }

    private static bool IsPlanar(List<Vec3> f)
    {
        var n = Polyhedron.Normal(f);
        var len = Math.Sqrt(n.Dot(n));
        if (len < 1e-12) return true;
        n = n * (1 / len);
        var d = n.Dot(f[0]);
        return f.All(v => Math.Abs(n.Dot(v) - d) < 1e-4);
    }
}

/// <summary>
/// Pisos que acompanham o greide: divididos em trechos ao longo do eixo (cada piso com poucas dezenas de metros) e com os
/// pontos de apoio da edição de forma alinhados ao eixo – bordas, eixo (crista do abaulamento) e linhas intermediárias.
/// </summary>
public static class GradeFloors
{
    /// <summary>Corta o contorno em trechos de até <paramref name="length"/> m ao longo do eixo (cortes perpendiculares).</summary>
    public static List<Polygon2> Chunks(Polygon2 shape, GradeSurface surf, double length = 40, List<double>? grid = null)
    {
        var axis = surf.Axis;
        var loc = shape.Outer.Select(surf.Locate).ToList();
        if (loc.Count == 0) return new List<Polygon2> { shape };
        var s0 = loc.Min(l => l.S);
        var s1 = loc.Max(l => l.S);
        var w = loc.Max(l => Math.Abs(l.Y)) + 2;
        if (s1 - s0 <= length * 1.25) return new List<Polygon2> { shape };
        grid ??= Grid(surf);
        var n = (int)Math.Ceiling((s1 - s0) / length);
        // Cortes sempre em estacas da grade: os pisos vizinhos compartilham os vértices da junta.
        var bounds = new List<double> { s0 - 5 };
        for (int k = 1; k < n; k++)
        {
            var target = s0 + (s1 - s0) * k / n;
            bounds.Add(grid.OrderBy(x => Math.Abs(x - target)).First());
        }
        bounds.Add(s1 + 5);
        bounds = bounds.Distinct().OrderBy(x => x).ToList();
        var res = new List<Polygon2>();
        for (int k = 0; k + 1 < bounds.Count; k++)
        {
            var a2 = bounds[k];
            var b2 = bounds[k + 1];
            var st = new List<double> { a2 };
            st.AddRange(grid.Where(x => x > a2 + 1e-6 && x < b2 - 1e-6));
            st.Add(b2);
            Vec2 P(double s, double y)
            {
                var sc = Math.Clamp(s, 0, axis.Length);
                var p = axis.PointAt(sc) + axis.TangentAt(sc) * (s - sc);
                return p + axis.TangentAt(sc).PerpLeft * y;
            }
            var band = st.Select(s => P(s, w)).Concat(st.AsEnumerable().Reverse().Select(s => P(s, -w))).ToList();
            try { res.AddRange(PolygonOps.Intersect(new[] { shape }, new[] { new Polygon2(band) }).Where(p => p.Area > 0.05)); }
            catch { return new List<Polygon2> { shape }; }
        }
        return res.Count > 0 ? res : new List<Polygon2> { shape };
    }

    /// <summary>Estacas da grade (m): passo até <paramref name="maxStep"/>, menor nas curvas (flecha ≤ 1 cm).</summary>
    public static List<double> Grid(GradeSurface surf, double maxStep = 4.0)
    {
        var axis = surf.Axis;
        var L = axis.Length;
        var res = new List<double> { 0 };
        var s = 0.0;
        while (s < L - 1e-6)
        {
            // Raio local pela variação da tangente em ±2 m.
            var a = axis.TangentAt(Math.Max(0, s - 2));
            var b = axis.TangentAt(Math.Min(L, s + 2));
            var ang = Math.Acos(Math.Clamp(a.Dot(b), -1, 1));
            var r = ang < 1e-6 ? double.MaxValue : (Math.Min(L, s + 2) - Math.Max(0, s - 2)) / ang;
            var ds = Math.Clamp(Math.Sqrt(8 * Math.Min(r, 1e6) * 0.01), 0.75, maxStep);
            s = Math.Min(L, s + ds);
            if (L - s < ds * 0.3) s = L;
            res.Add(s);
        }
        return res;
    }

    /// <summary>
    /// Contorno com os trechos longitudinais (bordas ao longo do eixo) reamostrados nas estacas da grade: os vértices dos dois
    /// lados ficam emparelhados. Cantos (quebra &gt; 3°) e trechos transversais são mantidos.
    /// </summary>
    public static Polygon2 Resample(Polygon2 shape, GradeSurface surf, List<double>? grid = null)
    {
        grid ??= Grid(surf);
        var g = grid;
        List<Vec2> Ring(IReadOnlyList<Vec2> ring)
        {
            var n = ring.Count;
            if (n < 3) return ring.ToList();
            var loc = ring.Select(surf.Locate).ToList();
            bool Long(int i)
            {
                var j = (i + 1) % n;
                var ds = loc[j].S - loc[i].S;
                var dy = loc[j].Y - loc[i].Y;
                return Math.Abs(ds) > 0.05 && Math.Abs(ds) > 1.5 * Math.Abs(dy);
            }
            int Dir(int i) => Math.Sign(loc[(i + 1) % n].S - loc[i].S);
            bool Corner(int i)
            {
                var u = (ring[i] - ring[(i - 1 + n) % n]).Normalized();
                var v = (ring[(i + 1) % n] - ring[i]).Normalized();
                return Math.Acos(Math.Clamp(u.Dot(v), -1, 1)) > 3 * Math.PI / 180;
            }
            // Vértice "de passagem": dentro de uma corrida longitudinal, mesmo sentido e sem canto – sai e dá lugar às estacas.
            var drop = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var prev = (i - 1 + n) % n;
                drop[i] = Long(prev) && Long(i) && Dir(prev) == Dir(i) && !Corner(i);
            }
            if (drop.All(d => d)) return ring.ToList();
            var start = Enumerable.Range(0, n).First(i => !drop[i]);
            var res = new List<Vec2>();
            var k = start;
            do
            {
                res.Add(ring[k]);
                if (Long(k))
                {
                    var run = new List<int> { k };
                    var m = (k + 1) % n;
                    while (drop[m]) { run.Add(m); m = (m + 1) % n; }
                    run.Add(m);
                    var sa = loc[k].S;
                    var sb = loc[m].S;
                    var lo = Math.Min(sa, sb) + 0.3;
                    var hi = Math.Max(sa, sb) - 0.3;
                    var gs = g.Where(x => x > lo && x < hi).ToList();
                    if (sb < sa) gs.Reverse();
                    foreach (var st in gs)
                        for (int q = 0; q + 1 < run.Count; q++)
                        {
                            var s1 = loc[run[q]].S;
                            var s2 = loc[run[q + 1]].S;
                            if ((st - s1) * (st - s2) > 0 || Math.Abs(s2 - s1) < 1e-9) continue;
                            // Ponto exatamente na normal da estaca, no afastamento da borda ali: os dois lados ficam emparelhados.
                            var y = loc[run[q]].Y + (loc[run[q + 1]].Y - loc[run[q]].Y) * ((st - s1) / (s2 - s1));
                            var sc = Math.Clamp(st, 0, surf.Axis.Length);
                            res.Add(surf.Axis.PointAt(sc) + surf.Axis.TangentAt(sc).PerpLeft * y);
                            break;
                        }
                    k = m;
                }
                else k = (k + 1) % n;
            }
            while (k != start);
            return res;
        }
        try
        {
            var outer = Ring(shape.Outer);
            var holes = shape.Holes.Select(h => (IEnumerable<Vec2>)Ring(h)).ToList();
            var r = new Polygon2(outer, holes);
            return Math.Abs(r.Area - shape.Area) < Math.Max(0.02, shape.Area * 0.01) ? r : shape;
        }
        catch { return shape; }
    }

    /// <summary>
    /// Pontos internos de apoio: só onde a seção quebra – eixo do abaulamento e o fim do caimento – nas estacas da grade,
    /// afastados <paramref name="margin"/> m do contorno. Entre as bordas a seção é plana e não precisa de pontos.
    /// </summary>
    public static List<Vec2> Supports(Polygon2 shape, GradeSurface surf, List<double>? grid = null, double margin = 0.3, int max = 1500)
    {
        var axis = surf.Axis;
        var gr = surf.Grade;
        var loc = shape.Outer.Select(surf.Locate).ToList();
        if (loc.Count == 0) return new();
        double s0 = loc.Min(l => l.S), s1 = loc.Max(l => l.S);
        double y0 = loc.Min(l => l.Y), y1 = loc.Max(l => l.Y);
        var ys = new List<double>();
        if (Math.Abs(gr.Crossfall) > 1e-6 || gr.Superelevation.Count > 0)
        {
            ys.Add(0);
            if (gr.CrossfallWidth > 0.5) { ys.Add(gr.CrossfallWidth); ys.Add(-gr.CrossfallWidth); }
        }
        ys = ys.Where(y => y > y0 + margin && y < y1 - margin).ToList();
        if (ys.Count == 0) return new();
        grid ??= Grid(surf);
        var inner = PolygonOps.Offset(new[] { shape }, -margin);
        var res = new List<Vec2>();
        foreach (var s in grid.Where(x => x > s0 && x < s1))
        {
            var p = axis.PointAt(s);
            var nrm = axis.TangentAt(s).PerpLeft;
            foreach (var y in ys)
            {
                var q = p + nrm * y;
                if (inner.Any(e => e.Contains(q))) res.Add(q);
                if (res.Count >= max) return res;
            }
        }
        return res;
    }
}
