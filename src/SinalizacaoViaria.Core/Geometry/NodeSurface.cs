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
        var m = MajorIndex(_legs.Select(l => (l.Rank, l.Half)).ToList());
        _major = m >= 0 ? _legs[m] : null;
    }

    /// <summary>
    /// Índice da via principal entre as vias do nó (prioridade, meia largura da pista; na ordem do nó): menor prioridade, empate =
    /// mais larga, depois a primeira. −1 = nenhuma via. É a regra da superfície do nó – o greide usa a mesma para decidir qual via
    /// atravessa o cruzamento com a própria rampa.
    /// </summary>
    public static int MajorIndex(IReadOnlyList<(int Rank, double Half)> legs) =>
        legs.Count == 0 ? -1 : Enumerable.Range(0, legs.Count).OrderBy(i => legs[i].Rank).ThenByDescending(i => legs[i].Half).ThenBy(i => i).First();

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

    /// <summary>
    /// Via do nó para as marcas dele: superfície (com a concordância), cota da base, identificador, trecho do eixo ocupado pelo
    /// nó (estações <see cref="S0"/>–<see cref="S1"/>, o recorte do nó na via) e meia largura da faixa da via.
    /// </summary>
    public sealed record MemberRoad(GradeSurface Surface, double BaseZ, string? Id, double S0, double S1, double Width);

    /// <summary>
    /// Superfície das marcas do nó (rampas, extensões de calçada, guias e calçadas das esquinas, faixas...): a do nó dentro
    /// dele e, ao longo de cada via secundária além do trecho ocupado pelo nó, a da via – com a concordância, que parte da
    /// superfície do nó ali. Antes as peças que seguem pela via secundária ficavam na superfície do nó (que prolonga a da via
    /// principal) por cima da via: até 28 cm acima ou abaixo da calçada e da plataforma da via ao lado.
    /// </summary>
    public Func<Vec2, double> ForMembers(IReadOnlyList<MemberRoad> roads)
    {
        if (roads.Count == 0 || _major == null) return Z;
        return p => RoadAt(roads, p) is { } r ? r.BaseZ + r.Surface.Z(p) : Z(p);
    }

    /// <summary>Eixo de referência da divisão em planos das marcas do nó no ponto: o da via secundária fora do nó, senão o da principal.</summary>
    public GradeSurface? MemberReference(IReadOnlyList<MemberRoad> roads, Vec2 p) => RoadAt(roads, p)?.Surface ?? _major?.Surface;

    /// <summary>Faixas de cada via secundária além do trecho ocupado pelo nó (onde as marcas do nó seguem a via) e o eixo dela.</summary>
    public List<(Polygon2 Zone, GradeSurface Reference)> MemberZones(IReadOnlyList<MemberRoad> roads)
    {
        var res = new List<(Polygon2, GradeSurface)>();
        foreach (var r in roads)
        {
            if (_major != null && r.Id != null && r.Id == _major.Id) continue;
            var L = r.Surface.Axis.Length;
            if (r.S0 > 0.5) res.Add((FloorPlanes.Band(r.Surface, -0.5, r.S0, -r.Width, r.Width), r.Surface));
            if (r.S1 < L - 0.5) res.Add((FloorPlanes.Band(r.Surface, r.S1, L + 0.5, -r.Width, r.Width), r.Surface));
        }
        return res;
    }

    private MemberRoad? RoadAt(IReadOnlyList<MemberRoad> roads, Vec2 p)
    {
        foreach (var r in roads)
        {
            if (_major != null && r.Id != null && r.Id == _major.Id) continue;
            var (s, y) = r.Surface.Locate(p);
            if ((s < r.S0 || s > r.S1) && Math.Abs(y) <= r.Width) return r;
        }
        return null;
    }

    /// <summary>Todas as vias no mesmo plano horizontal (sem greide): o nó fica plano na cota comum.</summary>
    public bool IsFlat => _legs.All(l => l.Surface.Grade.IsFlat) && _legs.Select(l => Math.Round(l.BaseZ, 3)).Distinct().Count() <= 1;
}
