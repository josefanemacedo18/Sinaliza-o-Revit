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
        if (!withGaps || d.Gaps.Count == 0) return Band(a, -d.RightWidth, d.LeftWidth);
        if (d.MedianOpenings.Count > 0)
        {
            // Retornos: o vão do canteiro (e o bolsão) é pista – faixas entre os vãos + preenchimento das aberturas.
            var fill = MedianOpenings(d, axis).Fill;
            var plain = Carriageway(new RoadPavementDefinition
            {
                RightWidth = d.RightWidth, LeftWidth = d.LeftWidth, Gaps = d.Gaps, StartSetback = d.StartSetback, EndSetback = d.EndSetback,
            }, axis);
            return fill.Count == 0 ? plain : PolygonOps.Union(plain.Concat(fill));
        }
        // Pista faixa a faixa entre os vãos (canteiros, sarjetas), sem operação booleana: numa via fora dos eixos X/Y a
        // diferença "faixa − canteiro" deixava o canteiro como FURO encostado nas pontas (a 0,1 mm do contorno) em vez de
        // duas pistas – piso frágil que, depois dos recortes da interseção, virava contorno com autointerseção e cobria o
        // canteiro.
        var res = new List<Polygon2>();
        var at = -d.RightWidth;
        foreach (var (lo, hi) in d.Gaps.Select(g => (Lo: g.Offset - g.Width / 2, Hi: g.Offset + g.Width / 2)).Where(g => g.Hi > g.Lo).OrderBy(g => g.Lo))
        {
            if (Math.Min(lo, d.LeftWidth) > at + 1e-4) res.AddRange(Band(a, at, Math.Min(lo, d.LeftWidth)));
            at = Math.Max(at, hi);
        }
        if (d.LeftWidth > at + 1e-4) res.AddRange(Band(a, at, d.LeftWidth));
        return res;
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
        if (d.MedianOpenings.Count > 0) gaps = PolygonOps.Difference(gaps, MedianOpenings(d, axis).Fill);
        return PolygonOps.Difference(band, gaps);
    }

    /// <summary>
    /// Aberturas do canteiro central (retornos): preenchimento de pista no vão e no bolsão, e o canteiro refeito no trecho –
    /// pontas em semicírculo voltadas para o vão, meio-fio contínuo com o do canteiro da via e grama.
    /// </summary>
    public static (List<Polygon2> Fill, List<Polygon2> Curb, List<Polygon2> Core) MedianOpenings(RoadPavementDefinition d, Polyline2 axis)
    {
        var fill = new List<Polygon2>();
        var curb = new List<Polygon2>();
        var core = new List<Polygon2>();
        var cw = Math.Max(0.05, d.CurbWidth);
        Vec2 P(double s, double o)
        {
            var ss = Math.Clamp(s, 0, axis.Length);
            return axis.PointAt(ss) + axis.TangentAt(ss).PerpLeft * o;
        }
        foreach (var g in d.Gaps.Where(g => g.Median && g.Width > 0.3))
        {
            var (lo, hi) = (g.Offset - g.Width / 2, g.Offset + g.Width / 2);
            foreach (var op in d.MedianOpenings)
            {
                var (a, b) = (Math.Min(op.Start, op.End), Math.Max(op.Start, op.End));
                if (b - a < 0.5) continue;
                var pw = Math.Clamp(op.PocketWidth, 0, g.Width - 0.6);
                var pocket = pw > 0.3 && Math.Abs(op.PocketFull - op.PocketTaper) + Math.Abs(op.PocketFull - a) > 0.5;
                // Bolsão do lado de a (estacas menores) ou de b.
                var pocketAtA = pocket && op.PocketFull <= a + 1e-6;
                var ext = new List<double> { a, b };
                if (pocket) ext.AddRange(new[] { op.PocketTaper, op.PocketFull });
                var r0 = Math.Max(0, ext.Min() - g.Width / 2 - 0.5);
                var r1 = Math.Min(axis.Length, ext.Max() + g.Width / 2 + 0.5);
                var band = VariableBand(axis, r0, r1, _ => lo, _ => hi);
                var remove = VariableBand(axis, a, b, _ => lo - 0.02, _ => hi + 0.02);
                // Bolsão: faixa do lado de chegada, com taper linear.
                (double Lo, double Hi) KeptAt(bool atA) => pocket && pocketAtA == atA ? (op.PocketSide > 0 ? (lo, hi - pw) : (lo + pw, hi)) : (lo, hi);
                if (pocket)
                {
                    var (p0, p1) = pocketAtA ? (Math.Min(op.PocketTaper, a), a) : (b, Math.Max(op.PocketTaper, b));
                    double Wd(double s)
                    {
                        var full = pocketAtA ? s >= op.PocketFull : s <= op.PocketFull;
                        if (full) return pw;
                        var len = Math.Abs(op.PocketFull - op.PocketTaper);
                        return len < 1e-6 ? pw : pw * Math.Clamp(Math.Abs(s - op.PocketTaper) / len, 0, 1);
                    }
                    var samples = Enumerable.Range(0, 41).Select(i => p0 + (p1 - p0) * i / 40.0).ToList();
                    remove.AddRange(op.PocketSide > 0
                        ? VariableBand(axis, p0, p1 + (pocketAtA ? 0.02 : 0), s => hi - Wd(s), _ => hi + 0.02, samples)
                        : VariableBand(axis, p0 - (pocketAtA ? 0 : 0.02), p1, _ => lo - 0.02, s => lo + Wd(s), samples));
                }
                var kept = PolygonOps.Difference(band, remove);
                // Pontas do canteiro em semicírculo voltadas para o vão.
                foreach (var (e, atA) in new[] { (a, true), (b, false) })
                {
                    var (k0, k1) = KeptAt(atA);
                    var r = (k1 - k0) / 2;
                    if (r < 0.1) continue;
                    var sc = atA ? e - r : e + r;
                    var cut = VariableBand(axis, atA ? e - r : e - 0.02, atA ? e + 0.02 : e + r, _ => k0 - 0.02, _ => k1 + 0.02);
                    var disk = new Polygon2(CurveTools.Circle(P(sc, (k0 + k1) / 2), r, 0.01));
                    kept = PolygonOps.Difference(kept, PolygonOps.Difference(cut, new[] { disk }));
                }
                kept = kept.Where(p => p.Area > 0.05).ToList();
                fill.AddRange(PolygonOps.Difference(band, kept).Where(p => p.Area > 1e-3));
                // Meio-fio contínuo com o do canteiro da via: o anel não fecha nas emendas do trecho refeito.
                var extended = PolygonOps.Union(kept.Concat(VariableBand(axis, r0 - 1, r0 + 0.01, _ => lo, _ => hi))
                    .Concat(VariableBand(axis, r1 - 0.01, r1 + 1, _ => lo, _ => hi)));
                var inner = PolygonOps.Offset(extended, -cw);
                var ring = PolygonOps.Difference(kept, inner).Where(p => p.Area > 0.005).ToList();
                curb.AddRange(ring);
                core.AddRange(PolygonOps.Difference(kept, ring).Where(p => p.Area > 0.01));
            }
        }
        return (fill, curb, core);
    }

    /// <summary>Trecho [início, fim] do canteiro refeito por uma abertura (os elementos do canteiro da via são interrompidos nele).</summary>
    public static (double S0, double S1) MedianOpeningSpan(MedianOpening op, double medianWidth)
    {
        var ext = new List<double> { op.Start, op.End };
        if (op.PocketWidth > 0.3) ext.AddRange(new[] { op.PocketTaper, op.PocketFull });
        return (Math.Max(0, ext.Min() - medianWidth / 2 - 0.5), ext.Max() + medianWidth / 2 + 0.5);
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
        if (d.MedianOpenings.Count > 0)
        {
            // Canteiro refeito junto às aberturas (pontas arredondadas, bolsão): meio-fio e grama.
            var (_, curb, core) = MedianOpenings(d, axis);
            foreach (var c in curb) geo.Pieces.Add(new MarkingPiece(c, MarkingColor.Concreto) { Thickness = d.CurbHeight, Layer = "MEIO-FIO" });
            foreach (var c in core) geo.Pieces.Add(new MarkingPiece(c, MarkingColor.Grama) { Thickness = d.CurbHeight });
        }
        geo.PathLength = axis.Length;
        geo.UnitCount = 0;
        return geo;
    }
}
