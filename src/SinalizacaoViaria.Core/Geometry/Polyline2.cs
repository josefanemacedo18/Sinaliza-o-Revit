namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Parâmetro topológico ao longo de uma polilinha: índice do segmento e fração [0,1] dentro dele.
/// Permite mapear estacas do eixo-base para polilinhas deslocadas (que têm o mesmo número de vértices).
/// </summary>
public readonly record struct PolyParam(int Segment, double T);

/// <summary>
/// Polilinha aberta ou fechada, parametrizada por comprimento de arco (estaqueamento), em metros.
/// Arcos, splines e elipses do Revit chegam aqui já discretizados.
/// </summary>
public sealed class Polyline2
{
    private readonly double[] _stations;

    public IReadOnlyList<Vec2> Points { get; }
    public bool Closed { get; }

    public Polyline2(IEnumerable<Vec2> points, bool closed = false)
    {
        var pts = new List<Vec2>();
        foreach (var p in points)
        {
            if (pts.Count == 0 || !pts[^1].AlmostEquals(p, 1e-6)) pts.Add(p);
        }
        if (closed && pts.Count > 2 && pts[0].AlmostEquals(pts[^1], 1e-6)) pts.RemoveAt(pts.Count - 1);
        if (closed && pts.Count >= 3) pts.Add(pts[0]);
        Points = pts;
        Closed = closed && pts.Count >= 4;
        _stations = ComputeStations(pts);
    }

    /// <summary>Construtor "cru": preserva exatamente os vértices (usado pelo deslocamento).</summary>
    private Polyline2(Vec2[] raw, bool closed)
    {
        Points = raw;
        Closed = closed;
        _stations = ComputeStations(raw);
    }

    private static double[] ComputeStations(IReadOnlyList<Vec2> pts)
    {
        var st = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            st[i] = st[i - 1] + pts[i].DistanceTo(pts[i - 1]);
        return st;
    }

    public double Length => _stations.Length == 0 ? 0 : _stations[^1];
    public int SegmentCount => Math.Max(0, Points.Count - 1);
    public double StationOfVertex(int i) => _stations[i];

    public PolyParam ParamAt(double s)
    {
        if (Points.Count < 2) return new PolyParam(0, 0);
        if (s <= 0) return new PolyParam(0, 0);
        if (s >= Length) return new PolyParam(SegmentCount - 1, 1);
        int lo = 0, hi = _stations.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (_stations[mid] <= s) lo = mid; else hi = mid;
        }
        var segLen = _stations[lo + 1] - _stations[lo];
        var t = segLen < Vec2.Eps ? 0 : (s - _stations[lo]) / segLen;
        return new PolyParam(lo, t);
    }

    public Vec2 PointAtParam(PolyParam p)
    {
        if (Points.Count == 0) return Vec2.Zero;
        if (Points.Count == 1) return Points[0];
        var i = Math.Clamp(p.Segment, 0, SegmentCount - 1);
        return Vec2.Lerp(Points[i], Points[i + 1], p.T);
    }

    public Vec2 PointAt(double s) => PointAtParam(ParamAt(s));

    public Vec2 TangentAtParam(PolyParam p)
    {
        if (Points.Count < 2) return Vec2.UnitX;
        var i = Math.Clamp(p.Segment, 0, SegmentCount - 1);
        return (Points[i + 1] - Points[i]).Normalized();
    }

    public Vec2 TangentAt(double s) => TangentAtParam(ParamAt(s));

    /// <summary>Trecho entre dois parâmetros topológicos (s0 &lt; s1).</summary>
    public List<Vec2> SubPoints(PolyParam a, PolyParam b)
    {
        var res = new List<Vec2> { PointAtParam(a) };
        for (int i = a.Segment + 1; i <= b.Segment; i++) res.Add(Points[i]);
        var end = PointAtParam(b);
        if (!res[^1].AlmostEquals(end, 1e-7)) res.Add(end);
        return res;
    }

    public List<Vec2> SubPoints(double s0, double s1) => SubPoints(ParamAt(s0), ParamAt(s1));

    public Polyline2 Reversed() => new(Points.Reverse(), Closed);

    /// <summary>
    /// Deslocamento lateral (positivo = à esquerda do sentido do caminho), preservando o número de
    /// vértices (junções em esquadria com limite). Mantém a correspondência de parâmetros com a original.
    /// </summary>
    public Polyline2 Offset(double d, double miterLimit = 4.0)
    {
        if (Math.Abs(d) < Vec2.Eps || Points.Count < 2) return this;
        var n = Points.Count;
        var res = new Vec2[n];
        for (int i = 0; i < n; i++)
        {
            Vec2? tPrev = null, tNext = null;
            if (i > 0) tPrev = (Points[i] - Points[i - 1]).Normalized();
            else if (Closed) tPrev = (Points[n - 1] - Points[n - 2]).Normalized();
            if (i < n - 1) tNext = (Points[i + 1] - Points[i]).Normalized();
            else if (Closed) tNext = (Points[1] - Points[0]).Normalized();

            if (tPrev == null) { res[i] = Points[i] + tNext!.Value.PerpLeft * d; continue; }
            if (tNext == null) { res[i] = Points[i] + tPrev.Value.PerpLeft * d; continue; }

            var n1 = tPrev.Value.PerpLeft;
            var n2 = tNext.Value.PerpLeft;
            var bis = (n1 + n2);
            var bl = bis.Length;
            if (bl < 1e-9) { res[i] = Points[i] + n1 * d; continue; }
            bis /= bl;
            var cosHalf = bis.Dot(n1);
            var k = cosHalf < 1.0 / miterLimit ? miterLimit : 1.0 / cosHalf;
            res[i] = Points[i] + bis * (d * k);
        }
        return new Polyline2(res, Closed);
    }

    /// <summary>Polilinha simplificada (remove vértices colineares), útil para exibição.</summary>
    public static Polyline2 FromSegment(Vec2 a, Vec2 b) => new(new[] { a, b });
}
