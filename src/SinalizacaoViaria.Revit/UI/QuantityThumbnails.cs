using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Miniaturas da própria sinalização para o quantitativo e a planilha: face da placa, trecho da linha, símbolo,
/// legenda, unidade do dispositivo em perspectiva ou amostra do material – nunca o trecho do projeto.
/// </summary>
public static class QuantityThumbnails
{
    public const int W = 120, H = 72;

    public static string Key(string code, MarkingColor color) => $"{code}|{color}";

    /// <summary>Monta as miniaturas de todos os itens (chave: código|cor e código).</summary>
    public static Dictionary<string, BitmapSource> Build(IEnumerable<(MarkingDefinition Def, MarkingGeometry Geo)> items)
    {
        var res = new Dictionary<string, BitmapSource>();
        var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
        foreach (var (def, geo) in items)
        {
            if (def is IAnnotationDefinition) continue;
            var code = MarkingBuilder.Describe(def, PluginContext.Catalog).Code;
            var colors = geo.AreaByColor.Keys.DefaultIfEmpty(MarkingColor.Branca).ToList();
            foreach (var color in colors)
            {
                var key = Key(code, color);
                if (res.ContainsKey(key)) continue;
                try
                {
                    var bmp = Make(def, color, ctx);
                    if (bmp == null) continue;
                    res[key] = bmp;
                    res.TryAdd(code, bmp);
                }
                catch (Exception ex)
                {
                    Log.Error("Miniatura " + code, ex);
                }
            }
        }
        return res;
    }

    private static BitmapSource? Make(MarkingDefinition def, MarkingColor color, BuildContext ctx)
    {
        var (geo, kind) = QuantitySamples.Sample(def, color, ctx);
        switch (kind)
        {
            case TipoMiniatura.Placa when def is SignDefinition s && PluginContext.Catalog.Placa(s.Code) is { } placa:
            {
                var face = SignThumbConverter.Thumbnail(placa, H - 4) as BitmapSource;
                if (face == null) return null;
                // Face da placa centralizada numa moldura clara.
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(246, 247, 249)), null, new System.Windows.Rect(0, 0, W, H));
                    dc.DrawImage(face, new System.Windows.Rect((W - face.PixelWidth) / 2.0, (H - face.PixelHeight) / 2.0, face.PixelWidth, face.PixelHeight));
                }
                var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(dv);
                bmp.Freeze();
                return bmp;
            }
            case TipoMiniatura.Material:
                return geo == null ? null : GeometryPreview.Snapshot(geo, W, H, false, paper: true);
            default:
                return geo == null || geo.Pieces.Count == 0 ? null : GeometryPreview.Snapshot(geo, W, H, kind == TipoMiniatura.Perspectiva);
        }
    }
}

/// <summary>Miniatura do item (linha do quantitativo) para a coluna "Imagem".</summary>
public sealed class ThumbnailConverter : IValueConverter
{
    public static ThumbnailConverter Instance { get; } = new();
    public static Dictionary<string, BitmapSource> Images { get; set; } = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is QuantityRow r ? Find(r) : null;

    public static BitmapSource? Find(QuantityRow r) =>
        Images.TryGetValue(QuantityThumbnails.Key(r.Code, r.Color), out var a) ? a : Images.TryGetValue(r.Code, out var b) ? b : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
