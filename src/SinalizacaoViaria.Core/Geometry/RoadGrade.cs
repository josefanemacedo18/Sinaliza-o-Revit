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
            var lv = Math.Min(p[i].Curve ?? DefaultCurve, 1.8 * Math.Min(p[i].S - p[i - 1].S, p[i + 1].S - p[i].S));
            if (lv < 0.01) continue;
            var x0 = p[i].S - lv / 2;
            if (s < x0 || s > p[i].S + lv / 2) continue;
            var g1 = Slope(p[i - 1], p[i]);
            var g2 = Slope(p[i], p[i + 1]);
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
    public void RaiseBetween(double s0, double s1, double height, double maxGrade, double length, double? curve = null)
    {
        maxGrade = Math.Max(0.005, maxGrade);
        var old = Clone();
        var run0 = Math.Abs(height - old.Z(s0)) / maxGrade;
        var run1 = Math.Abs(height - old.Z(s1)) / maxGrade;
        var a = Math.Max(0, s0 - run0);
        var b = Math.Min(length, s1 + run1);
        var keep = Points.Where(q => q.S < a - 1 || q.S > b + 1).ToList();
        Points.Clear();
        Points.AddRange(keep);
        if (!Points.Any(q => q.S < a - 1)) Points.Add(new GradePoint(0, a > 1 ? old.Z(0) : height));
        if (a > 1) Points.Add(new GradePoint(a, old.Z(a), curve));
        Points.Add(new GradePoint(Math.Max(0, s0), height, curve));
        Points.Add(new GradePoint(Math.Min(length, s1), height, curve));
        if (b < length - 1) Points.Add(new GradePoint(b, old.Z(b), curve));
        if (!Points.Any(q => q.S > b + 1)) Points.Add(new GradePoint(length, b < length - 1 ? old.Z(length) : height));
        Normalize();
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
