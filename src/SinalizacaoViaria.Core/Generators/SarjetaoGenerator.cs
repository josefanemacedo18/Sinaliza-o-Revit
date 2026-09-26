using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Sarjetão (valeta de concreto que atravessa a via para conduzir a água de uma sarjeta à outra): faixa de concreto
/// rente ao pavimento nas bordas, com a superfície côncava (flecha no centro) e laje de 15 cm, que substitui o
/// pavimento e interrompe a sinalização sob ela.
/// </summary>
public static class SarjetaoGenerator
{
    public const double SlabThickness = 0.15;

    /// <summary>Perfil transversal (x = −W/2..W/2, z ≤ 0): superfície parabólica com flecha <paramref name="depth"/>.</summary>
    public static Polygon2 Profile(double width, double depth)
    {
        var hw = width / 2;
        var f = Math.Clamp(depth, 0.0, Math.Min(0.25, width / 4));
        var pts = new List<Vec2>();
        const int n = 16;
        for (int i = 0; i <= n; i++)
        {
            var x = -hw + width * i / n;
            var k = x / hw;
            pts.Add(new Vec2(x, -f * (1 - k * k)));
        }
        pts.Add(new Vec2(hw, -SlabThickness));
        pts.Add(new Vec2(-hw, -SlabThickness));
        return new Polygon2(pts);
    }

    public static MarkingGeometry Build(Polyline2 path, double width, double depth)
    {
        var geo = new MarkingGeometry();
        var w = Math.Max(0.3, width);
        if (path.Length < 0.1) { geo.Warnings.Add("Sarjetão: caminho muito curto."); return geo; }
        if (depth > 0.12) geo.Warnings.Add("Sarjetão com flecha acima de 12 cm – desconfortável para os veículos; usual 3 a 8 cm.");
        var profile = Profile(w, depth);
        var pts = path.Points;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            var len = a.DistanceTo(b);
            if (len < 0.01) continue;
            var t = (b - a) / len;
            var n = t.PerpLeft;
            // Perfil com x para a esquerda do caminho, extrudado ao longo do trecho.
            var ps = new ProfileSolid(a, n, profile, t, len);
            geo.Pieces.Add(ProfileSolid.Piece(ps, MarkingColor.Concreto) with { Thickness = SlabThickness, Elevation = 0 });
        }
        // Linha d'água (eixo) destacada na planta.
        geo.PathLength = path.Length;
        geo.PaintedLength = path.Length;
        geo.UnitCount = 1;
        return geo;
    }

    /// <summary>Contorno em planta (recorte do pavimento e da sinalização).</summary>
    public static List<Polygon2> Footprint(Polyline2 path, double width) =>
        PolygonOps.Strip(path.Points, Math.Max(0.3, width) + 0.02);
}
