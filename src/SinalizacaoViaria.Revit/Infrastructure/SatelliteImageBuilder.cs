using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SinalizacaoViaria.Core.Geo;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Um bloco de imagem já na grade métrica do projeto: arquivo, extensão no modelo (m) e pixels.</summary>
public sealed record ImageBlock(string File, Vec2 Min, Vec2 Max, int PixelWidth, int PixelHeight);

/// <summary>Imagem pronta para o Revit (qualquer origem): blocos, georreferência, créditos e avisos.</summary>
public sealed record AerialResult(GeoReference Geo, List<ImageBlock> Blocks, string Attribution, string SourceName, double OutputMpp,
    double ReprojectionError, List<string> Notes)
{
    public Vec2 Min => new(Blocks.Min(b => b.Min.X), Blocks.Min(b => b.Min.Y));
    public Vec2 Max => new(Blocks.Max(b => b.Max.X), Blocks.Max(b => b.Max.Y));
}

/// <summary>Monta as imagens na grade métrica do projeto (fora da thread do Revit).</summary>
public static class SatelliteImageBuilder
{
    private static string NewFolder(string kind)
    {
        var f = Path.Combine(PluginPaths.Root, kind, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(f);
        return f;
    }

    /// <summary>Retângulo do modelo com pixels exatos (limite de 36 Mpx) → blocos de até 4000 px.</summary>
    public static (Vec2 Min, Vec2 Max, double Mpp, int W, int H) Grid(Vec2 min, Vec2 max, double mpp)
    {
        mpp = Math.Max(mpp, Math.Sqrt((max.X - min.X) * (max.Y - min.Y) / SatellitePlan.MaxPixels));
        var w = Math.Max(1, (int)Math.Ceiling((max.X - min.X) / mpp));
        var h = Math.Max(1, (int)Math.Ceiling((max.Y - min.Y) / mpp));
        return (min, min + new Vec2(w * mpp, h * mpp), mpp, w, h);
    }

    /// <summary>
    /// Reamostra uma imagem de origem (BGR32) para a grade do projeto em blocos JPEG. <paramref name="toSource"/> leva um
    /// ponto do modelo ao (u, v) contínuo da origem.
    /// </summary>
    public static List<ImageBlock> Blocks(Vec2 min, Vec2 max, double mpp, Func<Vec2, (double U, double V)> toSource, byte[] src, int sw, int sh, string kind, CancellationToken ct)
    {
        var (gmin, gmax, m, pw, ph) = Grid(min, max, mpp);
        var folder = NewFolder(kind);
        var list = new List<ImageBlock>();
        var nx = (pw + SatellitePlan.BlockSize - 1) / SatellitePlan.BlockSize;
        var ny = (ph + SatellitePlan.BlockSize - 1) / SatellitePlan.BlockSize;
        for (int r = 0; r < ny; r++)
            for (int c = 0; c < nx; c++)
            {
                ct.ThrowIfCancellationRequested();
                int px0 = c * pw / nx, px1 = (c + 1) * pw / nx, py0 = r * ph / ny, py1 = (r + 1) * ph / ny;
                var bmin = new Vec2(gmin.X + px0 * m, gmax.Y - py1 * m);
                var bmax = new Vec2(gmin.X + px1 * m, gmax.Y - py0 * m);
                var px = ImageReprojection.Resample(bmin, bmax, m, px1 - px0, py1 - py0, toSource, src, sw, sh);
                var f = Path.Combine(folder, $"bloco-{r + 1}-{c + 1}.jpg");
                SaveJpeg(px, px1 - px0, py1 - py0, f);
                list.Add(new ImageBlock(f, bmin, bmax, px1 - px0, py1 - py0));
            }
        return list;
    }

    // ------------------------------------------------------------------ tiles (OpenStreetMap / fonte própria)

    public static async Task<AerialResult> FromTilesAsync(SatellitePlan plan, IProgress<(string Stage, double Fraction)>? progress, CancellationToken ct)
    {
        progress?.Report(("Baixando tiles…", 0));
        await SatelliteTiles.DownloadAsync(plan, new Progress<(int Done, int Total)>(p => progress?.Report(($"Baixando tiles… {p.Done}/{p.Total}", 0.7 * p.Done / Math.Max(1, p.Total)))), ct).ConfigureAwait(false);
        var folder = NewFolder("imagens-aereas");
        var blocks = new List<ImageBlock>();
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
                blocks.Add(new ImageBlock(file, b.Min, b.Max, b.Width, b.Height));
            }
        }, ct).ConfigureAwait(false);
        var notes = plan.Notes.ToList();
        notes.Insert(0, $"Zoom {plan.Zoom}, pixel da fonte ≈ {plan.SourceMpp:0.000} m.");
        return new AerialResult(plan.Geo, blocks, plan.Source.Attribution, plan.Source.Nome, plan.OutputMpp, err, notes);
    }

    /// <summary>Prévia pequena da área (poucos tiles).</summary>
    public static async Task<BitmapSource> PreviewAsync(SatellitePlan plan, CancellationToken ct)
    {
        await SatelliteTiles.DownloadAsync(plan, null, ct).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var b = plan.Blocks[0];
            var px = SatelliteReprojection.Resample(plan, b, (x, y) => SatelliteTiles.Load(plan.Source.Fonte, plan.Zoom, x, y), ct);
            return Frozen(px, b.Width, b.Height);
        }, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ WMS

    /// <summary>
    /// Pede a ortofoto do retângulo do modelo ao WMS (em pedaços de até 2000 px), no sistema da camada, e reprojeta para a
    /// grade do projeto – a escala vem do próprio pedido (BBOX conhecido).
    /// </summary>
    public static async Task<AerialResult> FromWmsAsync(GeoReference geo, Vec2 min, Vec2 max, double mpp, string url, WmsCapabilities caps, WmsLayer layer,
        IProgress<(string Stage, double Fraction)>? progress, CancellationToken ct)
    {
        var (latC, lonC) = geo.ModelToGeo((min + max) / 2);
        var crsName = Wms.PreferredCrs(layer, Utm.ZoneOf(lonC), latC < 0)
            ?? throw new SatelliteException($"A camada \"{layer.Title}\" não oferece UTM SIRGAS 2000 nem EPSG:4674/4326.");
        var epsg = int.Parse(crsName.Split(':')[1]);
        var crs = ImageCrs.FromEpsg(epsg)!;
        // Caixa do retângulo no sistema do WMS (contorno amostrado) com 2 % de margem.
        var pts = new List<(double X, double Y)>();
        for (int i = 0; i <= 8; i++)
        {
            var t = i / 8.0;
            foreach (var p in new[] { new Vec2(min.X + t * (max.X - min.X), min.Y), new Vec2(min.X + t * (max.X - min.X), max.Y), new Vec2(min.X, min.Y + t * (max.Y - min.Y)), new Vec2(max.X, min.Y + t * (max.Y - min.Y)) })
                pts.Add(crs.FromModel(geo, p));
        }
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        var mx = (x1 - x0) * 0.02; var my = (y1 - y0) * 0.02;
        x0 -= mx; x1 += mx; y0 -= my; y1 += my;
        // Pixels da origem: a resolução pedida no terreno.
        var groundW = (max.X - min.X) * 1.04;
        var groundH = (max.Y - min.Y) * 1.04;
        var (_, _, outMpp, _, _) = Grid(min, max, mpp);
        var sw = (int)Math.Ceiling(groundW / outMpp);
        var sh = (int)Math.Ceiling(groundH / outMpp);
        var format = Wms.PreferredFormat(caps.Formats);
        var src = new byte[(long)sw * sh * 4 <= int.MaxValue ? sw * sh * 4 : throw new SatelliteException("Área grande demais para o WMS nesta resolução.")];
        const int chunk = 2000;
        int nx = (sw + chunk - 1) / chunk, ny = (sh + chunk - 1) / chunk, done = 0;
        for (int r = 0; r < ny; r++)
            for (int c = 0; c < nx; c++)
            {
                ct.ThrowIfCancellationRequested();
                int cx0 = c * sw / nx, cx1 = (c + 1) * sw / nx, cy0 = r * sh / ny, cy1 = (r + 1) * sh / ny;
                double bx0 = x0 + (x1 - x0) * cx0 / sw, bx1 = x0 + (x1 - x0) * cx1 / sw;
                double by1 = y1 - (y1 - y0) * cy0 / sh, by0 = y1 - (y1 - y0) * cy1 / sh;
                progress?.Report(($"Pedindo a ortofoto ao WMS… {++done}/{nx * ny}", 0.8 * done / (nx * ny)));
                var bytes = await SatelliteTiles.GetImageAsync(Wms.GetMapUrl(url, caps.Version, layer.Name, crsName, bx0, by0, bx1, by1, cx1 - cx0, cy1 - cy0, format), "WMS", ct).ConfigureAwait(false);
                var (px, _, _) = SatelliteTiles.DecodeBgr32(bytes, cx1 - cx0, cy1 - cy0);
                for (int y = 0; y < cy1 - cy0; y++)
                    Buffer.BlockCopy(px, y * (cx1 - cx0) * 4, src, ((cy0 + y) * sw + cx0) * 4, (cx1 - cx0) * 4);
            }
        var a = (x1 - x0) / sw;
        var e = -(y1 - y0) / sh;
        var wf = new WorldFile(a, 0, 0, e, x0 + a / 2, y1 + e / 2);
        progress?.Report(("Reprojetando para a escala métrica…", 0.85));
        var blocks = await Task.Run(() => Blocks(min, max, outMpp, p => { var (x, y) = crs.FromModel(geo, p); return wf.Inverse(x, y); }, src, sw, sh, "imagens-wms", ct), ct).ConfigureAwait(false);
        return new AerialResult(geo, blocks, $"Ortofoto: {layer.Title} (WMS)", $"WMS – {layer.Title}", outMpp, 0,
            new List<string> { $"Pedido ao WMS em {crs} ({crsName}), {sw} × {sh} px, formato {format}." });
    }

    // ------------------------------------------------------------------ Google Earth Pro

    /// <summary>Imagem salva do Google Earth calibrada por uma semelhança plano da imagem (u, −v) → modelo.</summary>
    public static AerialResult FromCalibratedImage(GeoReference geo, byte[] src, int sw, int sh, Similarity sim, string attribution, string sourceName, List<string> notes, CancellationToken ct)
    {
        var corners = new[] { new Vec2(0, 0), new Vec2(sw, 0), new Vec2(0, -sh), new Vec2(sw, -sh) }.Select(sim.Apply).ToList();
        var min = new Vec2(corners.Min(p => p.X), corners.Min(p => p.Y));
        var max = new Vec2(corners.Max(p => p.X), corners.Max(p => p.Y));
        var mpp = 1 / sim.Scale;
        var blocks = Blocks(min, max, mpp, p => { var q = sim.Inverse(p); return (q.X, -q.Y); }, src, sw, sh, "imagens-google-earth", ct);
        return new AerialResult(geo, blocks, attribution, sourceName, Grid(min, max, mpp).Mpp, 0, notes);
    }

    public static BitmapSource Frozen(byte[] bgr32, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgr32, w * 4);
        bmp.Freeze();
        return bmp;
    }

    public static void SaveJpeg(byte[] bgr32, int w, int h, string file)
    {
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgr32, w * 4);
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
