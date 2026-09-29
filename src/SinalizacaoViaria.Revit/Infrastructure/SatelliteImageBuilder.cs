using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SinalizacaoViaria.Core.Geo;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Imagem pronta para o Revit: blocos em JPEG na grade métrica do projeto, créditos e erro da reprojeção.</summary>
public sealed record SatelliteImage(SatellitePlan Plan, List<(SatelliteBlock Block, string File)> Files, string Attribution, double ReprojectionError);

/// <summary>Baixa os tiles, reprojeta para o plano local e grava os blocos (fora da thread do Revit).</summary>
public static class SatelliteImageBuilder
{
    /// <summary>Créditos e zoom máximo da área (Google); ajusta o plano quando a área não tem o zoom pedido.</summary>
    public static async Task<(SatellitePlan Plan, string Attribution)> ResolveAsync(SatellitePlan plan, Func<SatelliteSource, SatellitePlan> replan, string? key, CancellationToken ct)
    {
        if (plan.Source.Fonte != FonteImagem.GoogleSatelite || string.IsNullOrWhiteSpace(key)) return (plan, plan.Source.Attribution);
        var (n, s, e, w) = Bounds(plan);
        var (copy, maxZ) = await SatelliteTiles.GoogleViewportAsync(key!, n, s, e, w, plan.Zoom, ct).ConfigureAwait(false);
        var attribution = string.IsNullOrWhiteSpace(copy) ? plan.Source.Attribution : $"© Google – {copy}";
        if (maxZ is { } mz && mz >= 1 && mz < plan.Zoom)
        {
            plan = replan(plan.Source with { MaxZoom = mz });
            plan.Notes.Add($"O Google tem imagem até o zoom {mz} nesta área: {plan.OutputMpp:0.00} m/px.");
        }
        return (plan, attribution);
    }

    private static (double N, double S, double E, double W) Bounds(SatellitePlan plan)
    {
        double n = -90, s = 90, e = -180, w = 180;
        foreach (var p in new[] { plan.Min, plan.Max, new Core.Geometry.Vec2(plan.Min.X, plan.Max.Y), new Core.Geometry.Vec2(plan.Max.X, plan.Min.Y) })
        {
            var (la, lo) = plan.Geo.ModelToGeo(p);
            n = Math.Max(n, la); s = Math.Min(s, la); e = Math.Max(e, lo); w = Math.Min(w, lo);
        }
        return (n, s, e, w);
    }

    public static async Task<SatelliteImage> BuildAsync(SatellitePlan plan, string attribution, string? key, IProgress<(string Stage, double Fraction)>? progress, CancellationToken ct)
    {
        progress?.Report(("Baixando tiles…", 0));
        await SatelliteTiles.DownloadAsync(plan, key, new Progress<(int Done, int Total)>(p => progress?.Report(($"Baixando tiles… {p.Done}/{p.Total}", 0.7 * p.Done / Math.Max(1, p.Total)))), ct).ConfigureAwait(false);
        var folder = Path.Combine(PluginPaths.Root, "imagens-satelite", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var files = new List<(SatelliteBlock, string)>();
        var err = 0.0;
        await Task.Run(() =>
        {
            err = SatelliteReprojection.MaxInterpolationError(plan);
            for (int i = 0; i < plan.Blocks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var b = plan.Blocks[i];
                progress?.Report(($"Reprojetando para a escala métrica… bloco {i + 1}/{plan.Blocks.Count}", 0.7 + 0.3 * i / plan.Blocks.Count));
                var pixels = SatelliteReprojection.Resample(plan, b, (x, y) => SatelliteTiles.Load(plan.Source.Fonte, plan.Zoom, x, y), ct);
                var file = Path.Combine(folder, $"bloco-{b.Row + 1}-{b.Col + 1}.jpg");
                SaveJpeg(pixels, b.Width, b.Height, file);
                files.Add((b, file));
            }
        }, ct).ConfigureAwait(false);
        progress?.Report(("Pronto.", 1));
        return new SatelliteImage(plan, files, attribution, err);
    }

    /// <summary>Prévia pequena (~360 px) da área – poucos tiles, zoom baixo.</summary>
    public static async Task<BitmapSource> PreviewAsync(SatellitePlan plan, string? key, CancellationToken ct)
    {
        await SatelliteTiles.DownloadAsync(plan, key, null, ct).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var b = plan.Blocks[0];
            var px = SatelliteReprojection.Resample(plan, b, (x, y) => SatelliteTiles.Load(plan.Source.Fonte, plan.Zoom, x, y), ct);
            var bmp = BitmapSource.Create(b.Width, b.Height, 96, 96, PixelFormats.Bgr32, null, px, b.Width * 4);
            bmp.Freeze();
            return bmp;
        }, ct).ConfigureAwait(false);
    }

    private static void SaveJpeg(byte[] bgr32, int w, int h, string file)
    {
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgr32, w * 4);
        var enc = new JpegBitmapEncoder { QualityLevel = 90 };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
