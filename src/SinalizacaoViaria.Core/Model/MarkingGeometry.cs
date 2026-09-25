using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Model;

/// <summary>Uma peça pintada (traço, barra, seta...) com sua cor.</summary>
public sealed record MarkingPiece(Polygon2 Shape, MarkingColor Color)
{
    /// <summary>Espessura específica da peça (m). Zero = usar a espessura da marca.</summary>
    public double Thickness { get; init; }

    /// <summary>Peça conta como unidade (ex.: tacha) nos quantitativos.</summary>
    public bool IsUnit { get; init; }

    /// <summary>Altura da base da peça acima do pavimento (m) – permite empilhar volumes (barreiras, balizadores).</summary>
    public double Elevation { get; init; }

    /// <summary>
    /// Sólido de perfil vertical (placas, quebra-molas, rampas). Quando presente, o 3D usa o perfil;
    /// <see cref="Shape"/> é apenas a projeção em planta (2D e quantitativos).
    /// </summary>
    public ProfileSolid? Profile { get; init; }
}

/// <summary>
/// Perfil desenhado em um plano vertical e extrudado horizontalmente.
/// Coordenadas do perfil: X ao longo de <see cref="XDir"/> (a partir de <see cref="Origin"/>) e Y = altura (m).
/// </summary>
public sealed record ProfileSolid(Vec2 Origin, Vec2 XDir, Polygon2 Profile, Vec2 ExtrudeDir, double Depth)
{
    /// <summary>Converte um ponto do perfil (x, z) em planta.</summary>
    public Vec2 PlanPoint(double x) => Origin + XDir.Normalized() * x;

    /// <summary>Projeção em planta (retângulo ocupado).</summary>
    public Polygon2 Footprint()
    {
        var (mn, mx) = Profile.Bounds;
        var x = XDir.Normalized();
        var e = ExtrudeDir.Normalized() * Depth;
        var a = Origin + x * mn.X;
        var b = Origin + x * mx.X;
        return new Polygon2(new[] { a, b, b + e, a + e });
    }

    public static MarkingPiece Piece(ProfileSolid p, MarkingColor color, bool isUnit = false) =>
        new(p.Footprint(), color) { Profile = p, IsUnit = isUnit, Thickness = Math.Max(0.001, p.Profile.Bounds.Max.Y) };
}

/// <summary>Resultado de um gerador: peças + grandezas para quantitativos.</summary>
public sealed class MarkingGeometry
{
    public List<MarkingPiece> Pieces { get; } = new();

    /// <summary>Extensão linear pintada (soma dos traços), em metros.</summary>
    public double PaintedLength { get; set; }

    /// <summary>Extensão do eixo/caminho utilizado (m).</summary>
    public double PathLength { get; set; }

    /// <summary>Avisos de norma ou de geometria gerados durante a construção.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Quantidade de unidades (tachas, vagas...).</summary>
    public int UnitCount { get; set; }

    public double TotalArea => Pieces.Sum(p => p.Shape.Area);

    public IReadOnlyDictionary<MarkingColor, double> AreaByColor =>
        Pieces.GroupBy(p => p.Color).ToDictionary(g => g.Key, g => g.Sum(p => p.Shape.Area));

    public IEnumerable<MarkingColor> Colors => Pieces.Select(p => p.Color).Distinct();

    public void Add(Polygon2 shape, MarkingColor color, double thickness = 0, bool isUnit = false)
    {
        var s = shape.Simplified();
        if (s != null) Pieces.Add(new MarkingPiece(s, color) { Thickness = thickness, IsUnit = isUnit });
    }

    public void AddRange(IEnumerable<Polygon2> shapes, MarkingColor color, double thickness = 0, bool isUnit = false)
    {
        foreach (var s in shapes) Add(s, color, thickness, isUnit);
    }

    public void Merge(MarkingGeometry other)
    {
        Pieces.AddRange(other.Pieces);
        PaintedLength += other.PaintedLength;
        UnitCount += other.UnitCount;
        PathLength = Math.Max(PathLength, other.PathLength);
        Warnings.AddRange(other.Warnings);
    }

    public (Vec2 Min, Vec2 Max)? Bounds
    {
        get
        {
            if (Pieces.Count == 0) return null;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in Pieces)
            {
                var (mn, mx) = p.Shape.Bounds;
                minX = Math.Min(minX, mn.X); minY = Math.Min(minY, mn.Y);
                maxX = Math.Max(maxX, mx.X); maxY = Math.Max(maxY, mx.Y);
            }
            return (new Vec2(minX, minY), new Vec2(maxX, maxY));
        }
    }
}
