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

    /// <summary>Sólido poliédrico (faces planas quaisquer, ex.: rampas e abas). <see cref="Shape"/> é a projeção em planta.</summary>
    public Polyhedron? Solid { get; init; }
}

public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public Vec3 Cross(Vec3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public Vec2 XY => new(X, Y);
    public static Vec3 At(Vec2 p, double z) => new(p.X, p.Y, z);
}

/// <summary>
/// Sólido convexo definido por faces planas (coordenadas em metros; Z relativo à base da marca).
/// As faces são orientadas automaticamente com a normal para fora.
/// </summary>
public sealed class Polyhedron
{
    public List<List<Vec3>> Faces { get; }

    public Polyhedron(IEnumerable<IEnumerable<Vec3>> faces)
    {
        var list = faces.Select(f => f.ToList()).Where(f => f.Count >= 3).ToList();
        var all = list.SelectMany(f => f).ToList();
        var c = new Vec3(all.Average(v => v.X), all.Average(v => v.Y), all.Average(v => v.Z));
        Faces = new List<List<Vec3>>();
        foreach (var f in list)
        {
            var n = Normal(f);
            var fc = new Vec3(f.Average(v => v.X), f.Average(v => v.Y), f.Average(v => v.Z));
            if (n.Dot(fc - c) < 0) f.Reverse();
            Faces.Add(f);
        }
    }

    /// <summary>Normal (método de Newell).</summary>
    public static Vec3 Normal(IReadOnlyList<Vec3> f)
    {
        double x = 0, y = 0, z = 0;
        for (int i = 0; i < f.Count; i++)
        {
            var a = f[i];
            var b = f[(i + 1) % f.Count];
            x += (a.Y - b.Y) * (a.Z + b.Z);
            y += (a.Z - b.Z) * (a.X + b.X);
            z += (a.X - b.X) * (a.Y + b.Y);
        }
        return new Vec3(x, y, z);
    }

    public double MaxZ => Faces.SelectMany(f => f).Max(v => v.Z);
    public double MinZ => Faces.SelectMany(f => f).Min(v => v.Z);

    /// <summary>Projeção em planta (envoltória convexa dos vértices).</summary>
    public Polygon2 Footprint()
    {
        var pts = Faces.SelectMany(f => f).Select(v => v.XY).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (pts.Count < 3) return new Polygon2(pts);
        double Cross(Vec2 o, Vec2 a, Vec2 b) => (a - o).Cross(b - o);
        var lower = new List<Vec2>();
        foreach (var p in pts) { while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 1e-12) lower.RemoveAt(lower.Count - 1); lower.Add(p); }
        var upper = new List<Vec2>();
        for (int i = pts.Count - 1; i >= 0; i--) { var p = pts[i]; while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 1e-12) upper.RemoveAt(upper.Count - 1); upper.Add(p); }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        return new Polygon2(lower.Concat(upper));
    }

    /// <summary>Prisma de base poligonal convexa com topo definido por uma função de altura em cada vértice.</summary>
    public static Polyhedron Prism(IReadOnlyList<Vec2> basePts, Func<Vec2, double> bottom, Func<Vec2, double> top)
    {
        var faces = new List<List<Vec3>>
        {
            basePts.Select(p => Vec3.At(p, bottom(p))).ToList(),
            basePts.Select(p => Vec3.At(p, top(p))).ToList(),
        };
        for (int i = 0; i < basePts.Count; i++)
        {
            var a = basePts[i];
            var b = basePts[(i + 1) % basePts.Count];
            var side = new List<Vec3> { Vec3.At(a, bottom(a)), Vec3.At(b, bottom(b)) };
            if (top(b) - bottom(b) > 1e-6) side.Add(Vec3.At(b, top(b)));
            if (top(a) - bottom(a) > 1e-6) side.Add(Vec3.At(a, top(a)));
            if (side.Count >= 3) faces.Add(side);
        }
        return new Polyhedron(faces);
    }

    public static MarkingPiece Piece(Polyhedron p, MarkingColor color, bool isUnit = false) =>
        new(p.Footprint(), color) { Solid = p, IsUnit = isUnit, Thickness = Math.Max(0.001, p.MaxZ) };
}

/// <summary>Anotação 2D (somente na representação em vista): linhas de chamada e textos.</summary>
public abstract record Annotation2D;

/// <summary>Polilinha de anotação (linha de chamada, moldura de quadro).</summary>
public sealed record AnnotationLine(IReadOnlyList<Vec2> Points, MarkingColor Color = MarkingColor.Vermelha) : Annotation2D;

public enum TextAlign { Left, Center, Right }

/// <summary>Texto de anotação: posição do topo do bloco de texto; altura em mm de papel.</summary>
public sealed record AnnotationText(Vec2 Position, string Text, double PaperHeightMm = 2.5, TextAlign Align = TextAlign.Center) : Annotation2D;

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

    /// <summary>Anotações 2D (criadas apenas na representação em vista).</summary>
    public List<Annotation2D> Annotations { get; } = new();

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
        Annotations.AddRange(other.Annotations);
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
