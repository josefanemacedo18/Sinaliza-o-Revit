using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Pavimento das vias e utilidades de seção transversal (faixas ao longo do eixo).</summary>
public static class RoadGenerator
{
    /// <summary>Faixa entre os deslocamentos <paramref name="o0"/> e <paramref name="o1"/> (+ à esquerda) ao longo do eixo.</summary>
    public static List<Polygon2> Band(Polyline2 axis, double o0, double o1)
    {
        var lo = Math.Min(o0, o1);
        var hi = Math.Max(o0, o1);
        if (hi - lo < 1e-4 || axis.Points.Count < 2) return new();
        var mid = (lo + hi) / 2;
        var line = Math.Abs(mid) > 1e-6 ? axis.Offset(mid) : axis;
        return PolygonOps.Strip(line.Points, hi - lo);
    }

    /// <summary>Trecho do eixo considerando os recuos.</summary>
    public static Polyline2 Trimmed(Polyline2 axis, double startSetback, double endSetback)
    {
        var L = axis.Length;
        if (startSetback <= 0 && endSetback <= 0) return axis;
        var s0 = Math.Clamp(startSetback, 0, L);
        var s1 = Math.Clamp(L - endSetback, s0, L);
        return new Polyline2(axis.SubPoints(s0, s1));
    }

    /// <summary>Área de pista (sem as faixas de canteiros/sarjetas).</summary>
    public static List<Polygon2> Carriageway(RoadPavementDefinition d, Polyline2 axis, bool withGaps = true)
    {
        var a = Trimmed(axis, d.StartSetback, d.EndSetback);
        var band = Band(a, -d.RightWidth, d.LeftWidth);
        if (!withGaps || d.Gaps.Count == 0) return band;
        var gaps = d.Gaps.SelectMany(g => Band(a, g.Offset - g.Width / 2, g.Offset + g.Width / 2));
        return PolygonOps.Difference(band, gaps);
    }

    /// <summary>Corredor completo (pista + calçadas).</summary>
    public static List<Polygon2> Corridor(RoadPavementDefinition d, Polyline2 axis) =>
        Band(Trimmed(axis, d.StartSetback, d.EndSetback), -d.TotalRight, d.TotalLeft);

    public static MarkingGeometry Pavement(RoadPavementDefinition d, Polyline2 axis)
    {
        var geo = new MarkingGeometry();
        if (d.Material == TipoPavimento.Nenhum) return geo;
        var t = d.ActualThickness;
        foreach (var p in Carriageway(d, axis))
            geo.Pieces.Add(new MarkingPiece(p, d.Color) { Thickness = t, Elevation = -t });
        geo.PathLength = axis.Length;
        geo.UnitCount = 0;
        return geo;
    }
}
