using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Miniaturas das placas (face desenhada pelo núcleo) para as listas de seleção.</summary>
public sealed class SignThumbConverter : IValueConverter
{
    public static readonly SignThumbConverter Instance = new();
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PlacaDef p ? Thumbnail(p, 40) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public static ImageSource? Thumbnail(PlacaDef p, int size)
    {
        var key = $"{p.Codigo}|{size}";
        if (Cache.TryGetValue(key, out var img)) return img;
        try
        {
            var face = DetailGenerator.FlatFace(p, p.Largura, p.Altura, p.Legenda, PluginContext.Glyphs);
            if (face.Count == 0) return null;
            double minX = face.Min(f => f.Shape.Bounds.Min.X), maxX = face.Max(f => f.Shape.Bounds.Max.X);
            double minY = face.Min(f => f.Shape.Bounds.Min.Y), maxY = face.Max(f => f.Shape.Bounds.Max.Y);
            var k = (size - 2) / Math.Max(maxX - minX, maxY - minY);
            var ox = (size - (maxX - minX) * k) / 2;
            var oy = (size - (maxY - minY) * k) / 2;
            Point Map(Core.Geometry.Vec2 v) => new(ox + (v.X - minX) * k, size - (oy + (v.Y - minY) * k));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                foreach (var (shape, color) in face)
                {
                    var rgb = MarkingColors.Display(color);
                    var brush = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
                    var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
                    using (var c = g.Open())
                    {
                        void Ring(IReadOnlyList<Core.Geometry.Vec2> r)
                        {
                            c.BeginFigure(Map(r[0]), true, true);
                            c.PolyLineTo(r.Skip(1).Select(Map).ToList(), true, false);
                        }
                        Ring(shape.Outer);
                        foreach (var h in shape.Holes) Ring(h);
                    }
                    dc.DrawGeometry(brush, null, g);
                }
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            Cache[key] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
