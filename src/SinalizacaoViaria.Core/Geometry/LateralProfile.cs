using System.Text.Json.Serialization;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Ponto de um perfil lateral: na estaca <see cref="Station"/> (m ao longo do eixo) a marca é deslocada lateralmente de
/// <see cref="Shift"/> (m, + à esquerda do sentido do caminho) e fica <see cref="Widen"/> m mais larga (faixas contínuas:
/// calçadas, pavimento, fundos pintados). <see cref="Anchor"/> é o ponto do eixo na estaca quando a via foi gerada: se o
/// eixo for prolongado ou aparado depois (conexões), a estaca é recalculada pela projeção desse ponto.
/// </summary>
public sealed class LateralPoint
{
    public double Station { get; set; }
    public double Shift { get; set; }
    public double Widen { get; set; }
    public Vec2? Anchor { get; set; }

    public LateralPoint() { }

    public LateralPoint(double station, double shift, double widen = 0, Vec2? anchor = null)
    {
        Station = station;
        Shift = shift;
        Widen = widen;
        Anchor = anchor;
    }

    public LateralPoint Clone() => (LateralPoint)MemberwiseClone();
}

/// <summary>
/// Variação lateral de uma marca ao longo do caminho (via de largura variável): deslocamento e alargamento interpolados
/// entre as estacas (linear ou em curva S). Antes do primeiro e depois do último ponto os valores ficam constantes.
/// </summary>
public sealed class LateralProfile
{
    public List<LateralPoint> Points { get; set; } = new();

    /// <summary>Transição em curva S (suave) entre os pontos; falso = linear.</summary>
    public bool Smooth { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Points.Count == 0 || Points.All(p => Math.Abs(p.Shift) < 1e-6 && Math.Abs(p.Widen) < 1e-6);

    [JsonIgnore]
    public bool HasWiden => Points.Any(p => Math.Abs(p.Widen) > 1e-6);

    public LateralProfile Clone() => new() { Points = Points.Select(p => p.Clone()).ToList(), Smooth = Smooth };

    /// <summary>Pontos com as estacas recalculadas no caminho dado (âncoras projetadas), em ordem de estaca.</summary>
    public List<LateralPoint> Resolved(Polyline2? path)
    {
        var res = Points.Select(p =>
        {
            var c = p.Clone();
            if (path != null && p.Anchor is { } a && path.Points.Count >= 2) c.Station = path.Project(a).Station;
            return c;
        }).OrderBy(p => p.Station).ToList();
        return res;
    }

    /// <summary>Funções deslocamento(s) e alargamento(s) já resolvidas para o caminho.</summary>
    public (Func<double, double> Shift, Func<double, double> Widen, List<double> Breaks) For(Polyline2? path)
    {
        var pts = Resolved(path);
        double Eval(double s, Func<LateralPoint, double> f)
        {
            if (pts.Count == 0) return 0;
            if (s <= pts[0].Station) return f(pts[0]);
            if (s >= pts[^1].Station) return f(pts[^1]);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                if (s > b.Station) continue;
                var len = b.Station - a.Station;
                if (len < 1e-9) return f(b);
                var u = (s - a.Station) / len;
                if (Smooth) u = u * u * (3 - 2 * u);
                return f(a) + (f(b) - f(a)) * u;
            }
            return f(pts[^1]);
        }
        var breaks = pts.Select(p => p.Station).ToList();
        return (s => Eval(s, p => p.Shift), s => Eval(s, p => p.Widen), breaks);
    }

    public double ShiftAt(double s, Polyline2? path = null) => For(path).Shift(s);
    public double WidenAt(double s, Polyline2? path = null) => For(path).Widen(s);

    /// <summary>
    /// Estacas de amostragem num caminho de comprimento <paramref name="length"/>: as quebras do perfil e, onde o valor
    /// varia, pontos a cada <paramref name="step"/> m (curva S ou caminho curvo acompanham suavemente).
    /// </summary>
    public static List<double> Samples(IReadOnlyList<double> breaks, double length, Func<double, double> f, bool smooth, double step = 1.0)
    {
        var res = new List<double>();
        foreach (var b in breaks) if (b > 1e-6 && b < length - 1e-6) res.Add(b);
        if (smooth)
        {
            var sorted = breaks.OrderBy(x => x).ToList();
            for (int i = 0; i + 1 < sorted.Count; i++)
            {
                var a = Math.Max(0, sorted[i]);
                var b = Math.Min(length, sorted[i + 1]);
                if (b - a < 2 * step || Math.Abs(f(a) - f(b)) < 1e-4) continue;
                var n = (int)Math.Ceiling((b - a) / step);
                for (int k = 1; k < n; k++) res.Add(a + (b - a) * k / n);
            }
        }
        return res.Distinct().OrderBy(x => x).ToList();
    }

    /// <summary>Caminho deslocado pelo perfil (+ um deslocamento constante opcional).</summary>
    public Polyline2 Apply(Polyline2 path, double extra = 0)
    {
        var (shift, _, breaks) = For(path);
        var samples = Samples(breaks, path.Length, shift, Smooth);
        return path.OffsetVariable(s => shift(s) + extra, samples);
    }
}
