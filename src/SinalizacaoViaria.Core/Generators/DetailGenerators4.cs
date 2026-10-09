using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Pranchas: linhas de corte e identificação de cada trecho; legenda e quadro restritos ao trecho.</summary>
public static partial class DetailGenerator
{
    /// <summary>Eixo da via de um trecho de prancha (nulo se a via ou o eixo não existem mais).</summary>
    public static Polyline2? SheetAxis(SheetSegmentDefinition d, BuildContext ctx) =>
        ctx.Lookup?.Invoke(d.RoadId) is RoadPavementDefinition pav ? ctx.PathOf?.Invoke(pav) : null;

    /// <summary>Região de corte de um trecho de prancha no eixo atual (nula se a via não existe mais).</summary>
    public static Polygon2? SheetRegion(string? segmentId, BuildContext ctx)
    {
        if (string.IsNullOrEmpty(segmentId) || ctx.Lookup?.Invoke(segmentId) is not SheetSegmentDefinition d) return null;
        return SheetAxis(d, ctx) is { } axis ? new Polygon2(d.Segment(axis).Crop) : null;
    }

    /// <summary>
    /// Vista do trecho: linha de corte (traço longo-curto, atravessando a faixa da via) em cada sobreposição com a folha
    /// vizinha, com "CONTINUA NA PRANCHA X" e a estaca; e o título do trecho no canto da região de corte.
    /// </summary>
    public static MarkingGeometry SheetSegment(SheetSegmentDefinition d, BuildContext ctx)
    {
        if (ctx.Lookup?.Invoke(d.RoadId) is not RoadPavementDefinition) return Missing(MissingTarget + " (via excluída).");
        if (SheetAxis(d, ctx) is not { } axis || axis.Length < 0.1) return Missing("Eixo da via não encontrado.");
        var geo = new MarkingGeometry();
        var seg = d.Segment(axis);
        var t = d.TextMm;
        var u = seg.U;
        var v = seg.V;
        if (d.MatchLines)
            foreach (var (st, other) in new[] { (d.PrevMatch, d.PrevSheet), (d.NextMatch, d.NextSheet) })
            {
                if (st is not { } s || string.IsNullOrWhiteSpace(other)) continue;
                var p = axis.PointAt(Math.Clamp(s, 0, axis.Length));
                var n = axis.TangentAt(Math.Clamp(s, 0, axis.Length)).PerpLeft;
                var half = Math.Min(d.HalfWidth, seg.Height / 2 - ctx.Mm(t * 3));
                var a = p - n * half;
                var b = p + n * half;
                // Traço longo – curto (linha de corte), traços de 8 mm e 1,5 mm no papel.
                var len = a.DistanceTo(b);
                var dir = (b - a) / Math.Max(1e-9, len);
                for (double x = 0; x < len; x += ctx.Mm(12))
                {
                    geo.Annotations.Add(new AnnotationLine(new[] { a + dir * x, a + dir * Math.Min(len, x + ctx.Mm(8)) }, MarkingColor.Preta));
                    var y = x + ctx.Mm(9.5);
                    if (y < len) geo.Annotations.Add(new AnnotationLine(new[] { a + dir * y, a + dir * Math.Min(len, y + ctx.Mm(1.5)) }, MarkingColor.Preta));
                }
                // Texto paralelo à linha de corte, legível na vista girada.
                var rot = Readable(n);
                var along = Vec2.FromAngle(rot);
                var side = along.PerpLeft.Dot(u) * (s >= (d.Start + d.End) / 2 ? -1 : 1) >= 0 ? along.PerpLeft : -along.PerpLeft;
                geo.Annotations.Add(new AnnotationText(p + side * ctx.Mm(t * 0.8), $"CONTINUA NA PRANCHA {other}\nLINHA DE CORTE – EST. {SheetPlanner.Station(s)}", t)
                    { Rotation = rot });
            }
        // Título do trecho no canto superior esquerdo da região de corte (como a vista é mostrada).
        var corner = seg.Center - u * (seg.Width / 2) + v * (seg.Height / 2) + u * ctx.Mm(5) - v * ctx.Mm(4);
        geo.Annotations.Add(new AnnotationText(corner,
            $"PRANCHA {d.SheetNumber} – TRECHO {d.Index}: EST. {SheetPlanner.Station(d.Start)} A {SheetPlanner.Station(d.End)} – ESCALA 1:{d.Scale:0}",
            t * 1.2, TextAlign.Left) { Rotation = seg.AngleRad });
        if (!seg.Fits) geo.Warnings.Add($"Prancha {d.SheetNumber}: o trecho não cabe na folha a 1:{d.Scale:0} – reduza o trecho ou a escala.");
        geo.UnitCount = 1;
        return geo;
    }

    /// <summary>
    /// Geometria de uma marca recortada pela região de um trecho: peças cortadas pelo contorno; extensão, extensão de eixo e
    /// unidades proporcionais à área que ficou dentro (sem área: pelo ponto de referência).
    /// </summary>
    public static MarkingGeometry ClipToRegion(MarkingGeometry g, Polygon2 region, Vec2? anchor = null)
    {
        var res = new MarkingGeometry();
        double a0 = 0, a1 = 0;
        foreach (var p in g.Pieces)
        {
            a0 += p.Shape.Area;
            foreach (var part in PolygonOps.Intersect(new[] { p.Shape }, new[] { region }))
            {
                res.Pieces.Add(p with { Shape = part });
                a1 += part.Area;
            }
        }
        var ratio = a0 > 1e-9 ? a1 / a0 : anchor is { } at && region.Contains(at) ? 1 : 0;
        res.PaintedLength = g.PaintedLength * ratio;
        res.PathLength = g.PathLength * ratio;
        res.UnitCount = (int)Math.Round(g.UnitCount * ratio);
        foreach (var (c, area) in g.AreaOverrides) res.AreaOverrides[c] = area * ratio;
        return res;
    }
}
