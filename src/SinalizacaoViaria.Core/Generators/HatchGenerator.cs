using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Parâmetros de instância de uma marcação de área (zebrado).</summary>
public sealed class HatchOptions
{
    public double? BarWidth { get; set; }
    public double? Gap { get; set; }
    public double? AngleDeg { get; set; }
    public double? BorderWidth { get; set; }
    public bool? Chevron { get; set; }
    public bool? Crossed { get; set; }
    public MarkingColor? BarColor { get; set; }
    public MarkingColor? BorderColor { get; set; }

    /// <summary>Direção de referência (em geral o sentido do tráfego). Nulo = maior lado da área.</summary>
    public Vec2? ReferenceDirection { get; set; }

    /// <summary>Ponto por onde passa o eixo do chevron. Nulo = centróide.</summary>
    public Vec2? AxisPoint { get; set; }

    /// <summary>Deslocamento das barras perpendicularmente à sua direção (m).</summary>
    public double Phase { get; set; }
}

/// <summary>
/// Gera zebrados (ZPA), quadriculados (área de conflito), faixas de lombada e faixas adicionais PcD
/// a partir de um contorno fechado qualquer (inclusive côncavo e com furos).
/// </summary>
public static class HatchGenerator
{
    public static MarkingGeometry Generate(Polygon2 region, HachuraDef preset, HatchOptions? opt = null)
    {
        opt ??= new HatchOptions();
        var geo = new MarkingGeometry();
        if (!region.IsValid)
        {
            geo.Warnings.Add("Contorno da área inválido.");
            return geo;
        }

        var barW = opt.BarWidth ?? preset.LarguraBarra;
        var gap = opt.Gap ?? preset.Espacamento;
        var angle = Angles.ToRad(opt.AngleDeg ?? preset.Angulo);
        var border = opt.BorderWidth ?? preset.LarguraBorda;
        var chevron = opt.Chevron ?? preset.Chevron;
        var crossed = opt.Crossed ?? preset.Cruzado;
        var barColor = opt.BarColor ?? preset.CorBarras;
        var borderColor = opt.BorderColor ?? preset.CorBorda;

        var reference = (opt.ReferenceDirection ?? LongestEdgeDirection(region)).Normalized();
        if (reference.Length < 0.5) reference = Vec2.UnitX;

        List<Polygon2> inner = new() { region };
        if (border > 0)
        {
            inner = PolygonOps.Offset(new[] { region }, -border);
            var ring = PolygonOps.Difference(new[] { region }, inner);
            geo.AddRange(ring, borderColor);
            geo.PaintedLength += Perimeter(region);
        }
        if (inner.Count == 0)
        {
            geo.Warnings.Add("A área é estreita demais para o contorno informado.");
            return geo;
        }

        if (barW <= 0) return geo;
        var pitch = barW + Math.Max(0, gap);
        var anchor = opt.AxisPoint ?? region.Centroid;

        if (chevron)
        {
            var axisDir = reference;
            var (left, right) = SplitByLine(inner, anchor, axisDir);
            geo.AddRange(Bars(left, axisDir.Rotate(angle), barW, pitch, anchor, opt.Phase), barColor);
            geo.AddRange(Bars(right, axisDir.Rotate(-angle), barW, pitch, anchor, opt.Phase), barColor);
        }
        else
        {
            var dir = reference.Rotate(angle);
            geo.AddRange(Bars(inner, dir, barW, pitch, anchor, opt.Phase), barColor);
            if (crossed)
            {
                var bars2 = Bars(inner, dir.Rotate(Math.PI / 2), barW, pitch, anchor, opt.Phase);
                // Evita sobreposição de áreas pintadas nos cruzamentos (quantitativo correto).
                var existing = geo.Pieces.Where(pc => pc.Color == barColor).Select(pc => pc.Shape).ToList();
                geo.AddRange(PolygonOps.Difference(bars2, existing), barColor);
            }
        }
        return geo;
    }

    /// <summary>Barras paralelas à direção <paramref name="dir"/>, recortadas pela região.</summary>
    public static List<Polygon2> Bars(IReadOnlyCollection<Polygon2> region, Vec2 dir, double width, double pitch, Vec2 anchor, double phase = 0)
    {
        if (region.Count == 0) return new();
        dir = dir.Normalized();
        var n = dir.PerpLeft;
        double uMin = double.MaxValue, uMax = double.MinValue, vMin = double.MaxValue, vMax = double.MinValue;
        foreach (var poly in region)
            foreach (var p in poly.Outer)
            {
                var d = p - anchor;
                uMin = Math.Min(uMin, d.Dot(dir)); uMax = Math.Max(uMax, d.Dot(dir));
                vMin = Math.Min(vMin, d.Dot(n)); vMax = Math.Max(vMax, d.Dot(n));
            }
        var bars = new List<Polygon2>();
        var k0 = (int)Math.Floor((vMin - phase) / pitch) - 1;
        var k1 = (int)Math.Ceiling((vMax - phase) / pitch) + 1;
        if (k1 - k0 > 20000) return new();
        for (int k = k0; k <= k1; k++)
        {
            var v = phase + k * pitch;
            var start = anchor + n * v + dir * (uMin - 1);
            bars.Add(Polygon2.OrientedRectangle(start, dir, uMax - uMin + 2, width));
        }
        // Recorta cada barra individualmente para manter as peças separadas.
        var res = new List<Polygon2>();
        foreach (var bar in bars) res.AddRange(PolygonOps.Intersect(new[] { bar }, region));
        return res;
    }

    /// <summary>Divide as regiões pela reta (ponto, direção): lado esquerdo e lado direito.</summary>
    public static (List<Polygon2> Left, List<Polygon2> Right) SplitByLine(IReadOnlyCollection<Polygon2> region, Vec2 point, Vec2 dir)
    {
        var (mn, mx) = BoundsOf(region);
        var big = (mx - mn).Length * 2 + 10;
        dir = dir.Normalized();
        var n = dir.PerpLeft;
        var p0 = point - dir * big;
        var p1 = point + dir * big;
        var leftHalf = new Polygon2(new[] { p0, p1, p1 + n * big, p0 + n * big });
        var rightHalf = new Polygon2(new[] { p0 - n * big, p1 - n * big, p1, p0 });
        return (PolygonOps.Intersect(region, new[] { leftHalf }), PolygonOps.Intersect(region, new[] { rightHalf }));
    }

    public static Vec2 LongestEdgeDirection(Polygon2 poly)
    {
        Vec2 best = Vec2.UnitX;
        double bestLen = 0;
        for (int i = 0; i < poly.Outer.Count; i++)
        {
            var e = poly.Outer[(i + 1) % poly.Outer.Count] - poly.Outer[i];
            if (e.Length > bestLen) { bestLen = e.Length; best = e; }
        }
        return best.Normalized();
    }

    public static double Perimeter(Polygon2 p)
    {
        double sum = 0;
        void Ring(IReadOnlyList<Vec2> r) { for (int i = 0; i < r.Count; i++) sum += r[i].DistanceTo(r[(i + 1) % r.Count]); }
        Ring(p.Outer);
        foreach (var h in p.Holes) Ring(h);
        return sum;
    }

    private static (Vec2, Vec2) BoundsOf(IEnumerable<Polygon2> polys)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var poly in polys)
        {
            var (mn, mx) = poly.Bounds;
            minX = Math.Min(minX, mn.X); minY = Math.Min(minY, mn.Y);
            maxX = Math.Max(maxX, mx.X); maxY = Math.Max(maxY, mx.Y);
        }
        return (new Vec2(minX, minY), new Vec2(maxX, maxY));
    }
}
