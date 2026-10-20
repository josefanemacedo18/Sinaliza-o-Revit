using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Model;

/// <summary>Duas peças que viram pisos e ocupam o mesmo lugar: área em comum em planta (m²) e trecho de altura em comum (m).</summary>
public sealed record PieceOverlap(int A, int B, double Area, double Height)
{
    /// <summary>Volume em comum (m³) – o que o Revit acusa como "Pisos realçados se sobrepõem".</summary>
    public double Volume => Area * Height;
}

/// <summary>
/// Sobreposição de pisos no núcleo, pelo mesmo critério do Autoteste no Revit (volume em comum dos sólidos). Cada peça
/// plana vira um piso de <see cref="MarkingPiece.Elevation"/> a <see cref="MarkingPiece.Elevation"/> +
/// <see cref="MarkingPiece.Thickness"/> acima da cota da marca (como no <c>MarkingService</c>): pintura sobre o pavimento
/// encosta e não sobrepõe; pintura sobre o meio-fio, ou duas peças lado a lado que invadem uma à outra, sobrepõem.
/// Peças com sólido próprio (perfil, poliedro, sólido redondo) não são pisos e ficam de fora.
/// </summary>
public static class PieceOverlaps
{
    /// <summary>Área mínima em planta (m²) – 1 cm²; abaixo disso é ruído da grade das booleanas.</summary>
    public const double MinArea = 1e-4;

    /// <summary>Altura mínima em comum (m) – 1 mm; peças empilhadas só encostam.</summary>
    public const double MinHeight = 1e-3;

    public static bool IsFloorPiece(MarkingPiece p) => p.Profile == null && p.Solid == null && p.Round == null && p.Shape.Outer.Count >= 3;

    /// <summary>Pares (índices em <paramref name="pieces"/>) com área e altura em comum acima dos mínimos; <paramref name="baseZ"/> = cota da marca de cada peça.</summary>
    public static List<PieceOverlap> Find(IReadOnlyList<MarkingPiece> pieces, IReadOnlyList<double>? baseZ = null, double minArea = MinArea, double minHeight = MinHeight)
    {
        var res = new List<PieceOverlap>();
        var idx = Enumerable.Range(0, pieces.Count).Where(i => IsFloorPiece(pieces[i])).ToList();
        var boxes = idx.ToDictionary(i => i, i => pieces[i].Shape.Bounds);
        double Bottom(int i) => (baseZ?[i] ?? 0) + pieces[i].Elevation;
        double Top(int i) => Bottom(i) + Math.Max(0.001, pieces[i].Thickness);
        for (var a = 0; a < idx.Count; a++)
            for (var b = a + 1; b < idx.Count; b++)
            {
                int i = idx[a], j = idx[b];
                var h = Math.Min(Top(i), Top(j)) - Math.Max(Bottom(i), Bottom(j));
                if (h < minHeight) continue;
                var (bi, bj) = (boxes[i], boxes[j]);
                if (bi.Max.X < bj.Min.X || bj.Max.X < bi.Min.X || bi.Max.Y < bj.Min.Y || bj.Max.Y < bi.Min.Y) continue;
                var area = PolygonOps.Intersect(new[] { pieces[i].Shape }, new[] { pieces[j].Shape }).Sum(x => x.Area);
                if (area >= minArea) res.Add(new PieceOverlap(i, j, area, h));
            }
        return res;
    }
}
