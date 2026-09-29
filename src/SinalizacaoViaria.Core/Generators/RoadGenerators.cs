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

    /// <summary>
    /// Faixa de largura VARIÁVEL entre as estacas <paramref name="s0"/> e <paramref name="s1"/>: bordas nos afastamentos
    /// <paramref name="lo"/>(s) e <paramref name="hi"/>(s) (+ à esquerda), com vértices extras em <paramref name="samples"/>.
    /// </summary>
    public static List<Polygon2> VariableBand(Polyline2 axis, double s0, double s1, Func<double, double> lo, Func<double, double> hi,
        IEnumerable<double>? samples = null)
    {
        s0 = Math.Clamp(s0, 0, axis.Length);
        s1 = Math.Clamp(s1, s0, axis.Length);
        if (s1 - s0 < 1e-3 || axis.Points.Count < 2) return new();
        var sub = new Polyline2(axis.SubPoints(s0, s1));
        if (sub.Points.Count < 2) return new();
        var extra = (samples ?? Array.Empty<double>()).Where(x => x > s0 + 1e-6 && x < s1 - 1e-6).Select(x => x - s0).ToList();
        double Lo(double t) => Math.Min(lo(t + s0), hi(t + s0));
        double Hi(double t) => Math.Max(hi(t + s0), Lo(t) + 0.005);
        var left = sub.OffsetVariable(Hi, extra).Points;
        var right = sub.OffsetVariable(Lo, extra).Points;
        var ring = left.Concat(right.Reverse()).ToList();
        if (ring.Count < 3) return new();
        return PolygonOps.FromContours(new[] { (IReadOnlyList<Vec2>)ring }).Where(p => p.Area > 1e-5).ToList();
    }

    /// <summary>Estacas de amostragem de um perfil lateral num caminho (quebras + pontos a cada 1 m nas transições).</summary>
    public static List<double> LateralSamples(LateralProfile? p, Polyline2 path)
    {
        if (p == null || p.IsEmpty) return new();
        var (shift, widen, breaks) = p.For(path);
        return LateralProfile.Samples(breaks, path.Length, s => shift(s) + widen(s), p.Smooth);
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
        if (d.HasEdgeVariation) return VariableCarriageway(d, axis, withGaps);
        var a = Trimmed(axis, d.StartSetback, d.EndSetback);
        var band = Band(a, -d.RightWidth, d.LeftWidth);
        if (!withGaps || d.Gaps.Count == 0) return band;
        var gaps = d.Gaps.SelectMany(g => Band(a, g.Offset - g.Width / 2, g.Offset + g.Width / 2));
        return PolygonOps.Difference(band, gaps);
    }

    /// <summary>Pista de largura variável: bordos (faces dos meios-fios) e faixas sem pavimento acompanham a variação.</summary>
    private static List<Polygon2> VariableCarriageway(RoadPavementDefinition d, Polyline2 axis, bool withGaps)
    {
        var s0 = Math.Clamp(d.StartSetback, 0, axis.Length);
        var s1 = Math.Clamp(axis.Length - d.EndSetback, s0, axis.Length);
        var (fn, samples) = d.EdgeFunctions(axis);
        var band = VariableBand(axis, s0, s1, s => -(d.RightWidth + fn(s).CurbR), s => d.LeftWidth + fn(s).CurbL, samples);
        if (!withGaps || d.Gaps.Count == 0) return band;
        var gaps = new List<Polygon2>();
        foreach (var g in d.Gaps)
        {
            if (g.Lateral is { IsEmpty: false } gl)
            {
                var (sh, _, br) = gl.For(axis);
                var gs = samples.Concat(LateralProfile.Samples(br, axis.Length, sh, gl.Smooth)).Distinct().ToList();
                gaps.AddRange(VariableBand(axis, s0, s1, s => g.Offset - g.Width / 2 + sh(s), s => g.Offset + g.Width / 2 + sh(s), gs));
            }
            else gaps.AddRange(VariableBand(axis, s0, s1, _ => g.Offset - g.Width / 2, _ => g.Offset + g.Width / 2, samples));
        }
        return PolygonOps.Difference(band, gaps);
    }

    /// <summary>Corredor completo (pista + calçadas).</summary>
    public static List<Polygon2> Corridor(RoadPavementDefinition d, Polyline2 axis)
    {
        if (!d.HasEdgeVariation) return Band(Trimmed(axis, d.StartSetback, d.EndSetback), -d.TotalRight, d.TotalLeft);
        var s0 = Math.Clamp(d.StartSetback, 0, axis.Length);
        var s1 = Math.Clamp(axis.Length - d.EndSetback, s0, axis.Length);
        var (fn, samples) = d.EdgeFunctions(axis);
        return VariableBand(axis, s0, s1, s => -(d.TotalRight + fn(s).LotR), s => d.TotalLeft + fn(s).LotL, samples);
    }

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
