using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Extrai contornos de caracteres de qualquer fonte TrueType/OpenType instalada no Windows (WPF),
/// normalizados pela altura das maiúsculas. Recorre à fonte de blocos embutida se a fonte falhar.
/// </summary>
public sealed class WpfGlyphProvider : IGlyphOutlineProvider
{
    private const double EmSize = 100;
    private readonly Dictionary<(char, string, bool), GlyphOutline> _cache = new();
    private readonly BlockFontProvider _fallback = new();

    public GlyphOutline GetGlyph(char c, string fontFamily, bool bold)
    {
        var key = (c, fontFamily, bold);
        if (_cache.TryGetValue(key, out var g)) return g;
        try
        {
            g = Build(c, fontFamily, bold) ?? _fallback.GetGlyph(c, fontFamily, bold);
        }
        catch (Exception ex)
        {
            Log.Error("Glyph", ex);
            g = _fallback.GetGlyph(c, fontFamily, bold);
        }
        _cache[key] = g;
        return g;
    }

    private static GlyphOutline? Build(char c, string fontFamily, bool bold)
    {
        var typeface = new Typeface(new FontFamily(string.IsNullOrWhiteSpace(fontFamily) ? "Arial" : fontFamily),
            FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        if (!typeface.TryGetGlyphTypeface(out var gt)) return null;
        var capH = gt.CapsHeight > 0.3 ? gt.CapsHeight : 0.716;
        var scale = 1.0 / (EmSize * capH);

        var ft = new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, EmSize, Brushes.Black, 1.0);
        var advance = ft.WidthIncludingTrailingWhitespace * scale;
        if (char.IsWhiteSpace(c)) return new GlyphOutline(new(), advance);

        var geometry = ft.BuildGeometry(new Point(0, 0));
        var flat = geometry.GetFlattenedPathGeometry(0.15, ToleranceType.Absolute);
        var baseline = ft.Baseline;
        var contours = new List<IReadOnlyList<Vec2>>();
        foreach (var fig in flat.Figures)
        {
            var pts = new List<Vec2> { Map(fig.StartPoint) };
            foreach (var seg in fig.Segments)
            {
                switch (seg)
                {
                    case LineSegment ls: pts.Add(Map(ls.Point)); break;
                    case PolyLineSegment pl: foreach (var p in pl.Points) pts.Add(Map(p)); break;
                }
            }
            if (pts.Count >= 3) contours.Add(pts);
        }
        return new GlyphOutline(contours, advance);

        Vec2 Map(Point p) => new(p.X * scale, (baseline - p.Y) * scale);
    }
}
