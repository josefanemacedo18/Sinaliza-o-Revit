namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Superfície de um nó em nível (interseção, rotatória, cul-de-sac) costurada às vias que chegam nele: em cada ponto, a cota é
/// a média das superfícies das vias (greide + abaulamento + superelevação), pesada pela proximidade de cada via. Junto à
/// pista de uma via a cota é a dela (sem degraus na emenda do piso da via com o da interseção); no miolo as vias se misturam
/// suavemente – a interseção inclina com o terreno como as vias.
/// </summary>
public sealed class NodeSurface
{
    /// <summary>Via ligada ao nó: superfície do greide, cota da base do eixo (m, absoluta) e meia largura da pista.</summary>
    public sealed record Leg(GradeSurface Surface, double BaseZ, double Half);

    private readonly List<Leg> _legs;

    public NodeSurface(IEnumerable<Leg> legs) => _legs = legs.ToList();

    public int Count => _legs.Count;

    /// <summary>Cota absoluta (m) no ponto.</summary>
    public double Z(Vec2 p)
    {
        if (_legs.Count == 0) return 0;
        if (_legs.Count == 1) return _legs[0].BaseZ + _legs[0].Surface.Z(p);
        double sum = 0, wsum = 0;
        foreach (var l in _legs)
        {
            var (s, y) = l.Surface.Locate(p);
            // Fora da pista a influência cai com o cubo da distância à borda (dentro da pista ela é plena).
            var d = Math.Max(0, Math.Abs(y) - l.Half);
            // Além das pontas do eixo, idem pela distância à ponta.
            var L = l.Surface.Axis.Length;
            d += Math.Max(0, -s) + Math.Max(0, s - L);
            var w = 1.0 / Math.Pow(d + 1.0, 3);
            sum += w * (l.BaseZ + l.Surface.Grade.Z(Math.Clamp(s, 0, L), y));
            wsum += w;
        }
        return sum / wsum;
    }

    /// <summary>Todas as vias no mesmo plano horizontal (sem greide): o nó fica plano na cota comum.</summary>
    public bool IsFlat => _legs.All(l => l.Surface.Grade.IsFlat) && _legs.Select(l => Math.Round(l.BaseZ, 3)).Distinct().Count() <= 1;
}
