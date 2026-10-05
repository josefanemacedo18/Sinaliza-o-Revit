using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Model;

/// <summary>
/// Referência de posição e giro de uma placa em planta: centro (por volume) do suporte metálico e centro da chapa com a face.
/// Gravada quando o plugin gera a placa; se o usuário mover ou girar a placa no Revit, os dois pontos mudam e o
/// deslocamento rígido entre eles é levado para a definição – a regeneração não desfaz o giro.
/// </summary>
public readonly record struct SignFrame(Vec2 Support, Vec2 Face)
{
    /// <summary>Menor distância suporte–face (m) para medir o giro com segurança.</summary>
    public const double MinLever = 0.005;

    /// <summary>Referência da geometria gerada (nula sem suporte metálico ou sem chapa).</summary>
    public static SignFrame? Of(MarkingGeometry geo)
    {
        var metal = Centroid(geo.Pieces.Where(p => p.Color == MarkingColor.Metal));
        var face = Centroid(geo.Pieces.Where(p => p.Color != MarkingColor.Metal));
        if (metal is not { } m || face is not { } f || m.DistanceTo(f) < MinLever) return null;
        return new SignFrame(m, f);
    }

    /// <summary>
    /// Aplica à placa o deslocamento rígido (giro + translação) que leva <paramref name="generated"/> a
    /// <paramref name="current"/>. Falso se não houve giro nem movimento (ou se não dá para medir).
    /// </summary>
    public static bool Follow(SignDefinition d, SignFrame generated, SignFrame current, double minMove = 1e-4, double minAngle = 1e-4)
    {
        var g = generated.Face - generated.Support;
        var c = current.Face - current.Support;
        if (g.Length < MinLever || c.Length < MinLever) return false;
        // Corpo rígido: a distância suporte–face não muda; se mudou, não foi um giro/movimento da placa inteira.
        if (Math.Abs(g.Length - c.Length) > Math.Max(0.002, 0.05 * g.Length)) return false;
        var angle = Math.Atan2(g.Cross(c), g.Dot(c));
        var move = current.Support - generated.Support;
        if (Math.Abs(angle) < minAngle && move.Length < minMove) return false;
        Vec2 Rot(Vec2 v) => new(v.X * Math.Cos(angle) - v.Y * Math.Sin(angle), v.X * Math.Sin(angle) + v.Y * Math.Cos(angle));
        d.Position = current.Support + Rot(d.Position - generated.Support);
        var dir = d.Direction.Length < 1e-9 ? Vec2.UnitY : d.Direction;
        d.Direction = Rot(dir);
        return true;
    }

    private static Vec2? Centroid(IEnumerable<MarkingPiece> pieces)
    {
        double w = 0, x = 0, y = 0;
        foreach (var p in pieces)
        {
            Vec2 c;
            double v;
            if (p.Profile is { } ps)
            {
                v = Math.Abs(ps.Profile.Area) * ps.Depth;
                c = ps.PlanPoint(ps.Profile.Centroid.X) + ps.ExtrudeDir.Normalized() * (ps.Depth / 2);
            }
            else
            {
                v = Math.Abs(p.Shape.Area) * Math.Max(1e-4, p.Thickness);
                c = p.Shape.Centroid;
            }
            if (v <= 0 || double.IsNaN(c.X)) continue;
            w += v;
            x += c.X * v;
            y += c.Y * v;
        }
        return w > 0 ? new Vec2(x / w, y / w) : null;
    }
}
