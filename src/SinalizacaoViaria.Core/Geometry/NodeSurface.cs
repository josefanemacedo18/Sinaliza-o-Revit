namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Superfície de um nó em nível (interseção, rotatória, cul-de-sac) costurada às vias que chegam nele, como se projeta na
/// prática: a via PRINCIPAL (maior hierarquia; empate = mais larga) atravessa o nó mantendo o seu greide e o seu abaulamento;
/// as vias secundárias se concordam com ela – partem da cota da borda da pista principal e chegam à própria superfície no
/// limite do nó (<see cref="Leg.Reach"/>), por uma transição suave. Resultado: o miolo é plano como a via principal (pisos
/// planos grandes, sem "leques" nas esquinas) e não há degrau nas emendas com os pisos das vias.
/// </summary>
public sealed class NodeSurface
{
    /// <summary>
    /// Via ligada ao nó: superfície do greide, cota da base do eixo (m, absoluta), meia largura da pista, prioridade
    /// (menor = mais importante) e alcance do nó sobre ela (m, da borda da pista principal até onde começa o piso da via).
    /// </summary>
    public sealed record Leg(GradeSurface Surface, double BaseZ, double Half, int Rank = 0, double Reach = 15);

    private readonly List<Leg> _legs;
    private readonly Leg? _major;

    public NodeSurface(IEnumerable<Leg> legs)
    {
        _legs = legs.ToList();
        _major = _legs.Select((l, i) => (l, i)).OrderBy(t => t.l.Rank).ThenByDescending(t => t.l.Half).ThenBy(t => t.i).Select(t => t.l).FirstOrDefault();
    }

    public int Count => _legs.Count;
    public Leg? Major => _major;

    /// <summary>Cota absoluta (m) no ponto.</summary>
    public double Z(Vec2 p)
    {
        if (_legs.Count == 0 || _major == null) return 0;
        var zMaj = LegZ(_major, p, out var dMaj);
        if (_legs.Count == 1) return zMaj;
        // Secundárias: média pesada pela proximidade (cada uma plena sobre a própria pista).
        double sum = 0, wsum = 0, reach = 0;
        foreach (var l in _legs)
        {
            if (ReferenceEquals(l, _major)) continue;
            var z = LegZ(l, p, out var d);
            var w = 1.0 / Math.Pow(d + 1.0, 3);
            sum += w * z; wsum += w; reach += w * Math.Max(3, l.Reach);
        }
        var zMin = sum / wsum;
        reach /= wsum;
        // Da borda da pista principal (t = 0) ao limite do nó (t = 1): concordância em S (tangente nula nas duas pontas).
        var t = Math.Clamp(dMaj / reach, 0, 1);
        t = t * t * (3 - 2 * t);
        return zMaj + (zMin - zMaj) * t;
    }

    /// <summary>
    /// Cota da via no ponto e distância à borda da pista dela. Além das pontas do eixo o greide segue na rampa final
    /// (até 30 m) e o afastamento é medido na normal da ponta – sem o "cone" que a distância radial à ponta criava.
    /// </summary>
    public static double LegZ(Leg l, Vec2 p, out double dEdge)
    {
        var surf = l.Surface;
        var axis = surf.Axis;
        var L = axis.Length;
        var (s, y) = surf.Locate(p);
        double ext = 0, sc = s;
        if (s <= 1e-6 || s >= L - 1e-6)
        {
            var atEnd = s >= L / 2;
            sc = atEnd ? L : 0;
            var e = axis.PointAt(sc);
            var tan = axis.TangentAt(atEnd ? Math.Max(0, L - 1e-3) : 0);
            var d = p - e;
            ext = d.Dot(tan);
            if (atEnd ? ext > 0 : ext < 0) y = d.Dot(tan.PerpLeft);
            else ext = 0;
        }
        var grade = surf.Grade;
        var z = grade.Z(sc, y);
        if (Math.Abs(ext) > 0)
        {
            var h = Math.Min(2, Math.Max(0.1, L / 4));
            var slope = sc >= L / 2 ? (grade.Z(L) - grade.Z(L - h)) / h : (grade.Z(h) - grade.Z(0)) / h;
            z += slope * Math.Clamp(ext, -30, 30);
        }
        dEdge = Math.Max(0, Math.Abs(y) - l.Half) + Math.Abs(ext);
        return l.BaseZ + z;
    }

    /// <summary>Todas as vias no mesmo plano horizontal (sem greide): o nó fica plano na cota comum.</summary>
    public bool IsFlat => _legs.All(l => l.Surface.Grade.IsFlat) && _legs.Select(l => Math.Round(l.BaseZ, 3)).Distinct().Count() <= 1;
}
