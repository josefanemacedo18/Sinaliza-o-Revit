namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Superfície de um nó em nível (interseção, rotatória, cul-de-sac), como se projeta na prática: a via PRINCIPAL (maior
/// hierarquia; empate = mais larga) atravessa o nó com o seu greide e o seu abaulamento, e além da pista dela a superfície
/// segue em nível na direção transversal (a cota da borda da pista, como as calçadas da própria via). O nó inteiro fica
/// formado por poucos planos – pisos planos grandes, esquinas e calçadas sem "leques". As vias secundárias fazem a
/// concordância FORA do nó, nos seus primeiros metros (<see cref="NodeBlend"/>), partindo exatamente desta superfície.
/// </summary>
public sealed class NodeSurface
{
    /// <summary>
    /// Via ligada ao nó: superfície do greide, cota da base do eixo (m, absoluta), meia largura da pista, prioridade
    /// (menor = mais importante) e identificador da via.
    /// </summary>
    public sealed record Leg(GradeSurface Surface, double BaseZ, double Half, int Rank = 0, string? Id = null);

    private readonly List<Leg> _legs;
    private readonly Leg? _major;

    public NodeSurface(IEnumerable<Leg> legs)
    {
        _legs = legs.ToList();
        _major = _legs.Select((l, i) => (l, i)).OrderBy(t => t.l.Rank).ThenByDescending(t => t.l.Half).ThenBy(t => t.i).Select(t => t.l).FirstOrDefault();
    }

    public int Count => _legs.Count;
    public Leg? Major => _major;
    public IReadOnlyList<Leg> Legs => _legs;

    /// <summary>Cota absoluta (m) no ponto: a superfície da via principal (plana na transversal além da pista).</summary>
    public double Z(Vec2 p) => _major == null ? 0 : LegZ(_major, p, out _);

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
