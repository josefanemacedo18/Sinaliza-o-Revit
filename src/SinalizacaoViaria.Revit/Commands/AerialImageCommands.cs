using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SinalizacaoViaria.Core.Geo;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Um bloco de imagem já na grade métrica do projeto: arquivo, extensão no modelo (m) e pixels.</summary>
internal sealed record ImageBlock(string File, Vec2 Min, Vec2 Max, int PixelWidth, int PixelHeight);

/// <summary>
/// Coloca imagens na escala exata. O tamanho que o Revit dá a uma imagem depende da resolução (DPI) do arquivo e o
/// parâmetro "Largura" é o da imagem "na vista, depois de escalar" (RASTER_SHEETWIDTH) – não dá para confiar que um
/// valor em pés gravado nele vire a mesma medida no modelo. Por isso o tamanho é MEDIDO no modelo pelos cantos da imagem
/// (GetLocation) depois de cada ajuste e corrigido até a diferença ficar abaixo de 1 mm; cada etapa vai para o
/// diagnóstico (esperado × obtido).
/// </summary>
internal static class ImagePlacer
{
    public const double ToleranceM = 0.001;

    public static (List<ElementId> Ids, List<string> Report) Place(Document doc, View view, IEnumerable<ImageBlock> blocks, double z)
    {
        var ids = new List<ElementId>();
        var report = new List<string>();
        var diag = new StringBuilder();
        diag.AppendLine($"Imagem aérea – diagnóstico da escala ({DateTime.Now:dd/MM/yyyy HH:mm}) · vista \"{view.Name}\" escala 1:{view.Scale}");
        int n = 0;
        foreach (var b in blocks)
        {
            n++;
            var wM = b.Max.X - b.Min.X;
            var hM = b.Max.Y - b.Min.Y;
            diag.AppendLine($"Bloco {n}: {Path.GetFileName(b.File)}");
            diag.AppendLine($"  a) esperado: {wM:0.0000} × {hM:0.0000} m ({b.PixelWidth} × {b.PixelHeight} px, {wM / Math.Max(1, b.PixelWidth):0.00000} m/px)");
            var type = ImageType.Create(doc, new ImageTypeOptions(b.File, false, ImageTypeSource.Import));
            var dpi = type.get_Parameter(BuiltInParameter.RASTER_SYMBOL_RESOLUTION)?.AsValueString() ?? "?";
            diag.AppendLine($"  b) arquivo: {b.PixelWidth} × {b.PixelHeight} px, resolução no Revit {dpi}");
            var topLeft = new XYZ(UnitConv.Ft(b.Min.X), UnitConv.Ft(b.Max.Y), z);
            var inst = ImageInstance.Create(doc, view, type.Id, new ImagePlacementOptions(topLeft, BoxPlacement.TopLeft));
            doc.Regenerate();
            diag.AppendLine($"  c) após criar: {Measure(inst)} (Largura = {UnitConv.M(inst.Width):0.0000} m, escala H {inst.WidthScale:0.######})");
            try { inst.LockProportions = false; } catch { /* segue travado: pixels quadrados mantêm a proporção */ }
            inst.Width = UnitConv.Ft(wM);
            try { inst.Height = UnitConv.Ft(hM); } catch { /* travado */ }
            doc.Regenerate();
            diag.AppendLine($"  d) após Largura/Altura = {wM:0.0000}/{hM:0.0000} m: {Measure(inst)}");
            inst.SetLocation(topLeft, BoxPlacement.TopLeft);
            doc.Regenerate();
            diag.AppendLine($"  e) após posicionar: {Measure(inst)}");
            // Leitura de volta e correção pelo tamanho REAL no modelo.
            for (int it = 0; it < 4; it++)
            {
                var (aw, ah) = Size(inst);
                if (Math.Abs(aw - wM) <= ToleranceM && Math.Abs(ah - hM) <= ToleranceM) break;
                if (aw > 1e-9) inst.Width *= wM / aw;
                doc.Regenerate();
                var (_, ah2) = Size(inst);
                if (Math.Abs(ah2 - hM) > ToleranceM && ah2 > 1e-9)
                    try { inst.Height *= hM / ah2; } catch { /* travado */ }
                inst.SetLocation(topLeft, BoxPlacement.TopLeft);
                doc.Regenerate();
                diag.AppendLine($"     correção {it + 1}: {Measure(inst)}");
            }
            var bb = inst.get_BoundingBox(view);
            if (bb != null) diag.AppendLine($"  f) caixa envolvente: {UnitConv.M(bb.Max.X - bb.Min.X):0.0000} × {UnitConv.M(bb.Max.Y - bb.Min.Y):0.0000} m");
            inst.DrawLayer = DrawLayer.Background;
            inst.Pinned = true;
            var (fw, fh) = Size(inst);
            var tl = inst.GetLocation(BoxPlacement.TopLeft);
            var dpos = Math.Sqrt(Math.Pow(UnitConv.M(tl.X) - b.Min.X, 2) + Math.Pow(UnitConv.M(tl.Y) - b.Max.Y, 2));
            var ok = Math.Abs(fw - wM) <= ToleranceM && Math.Abs(fh - hM) <= ToleranceM && dpos <= ToleranceM;
            report.Add($"Bloco {n}: esperado {wM:0.000} × {hM:0.000} m · obtido {fw:0.000} × {fh:0.000} m · canto {dpos * 1000:0.0} mm {(ok ? "✓" : "⚠ fora da tolerância de 1 mm")}");
            diag.AppendLine($"  final: {fw:0.0000} × {fh:0.0000} m, canto a {dpos * 1000:0.00} mm {(ok ? "OK" : "FORA DA TOLERÂNCIA")}");
            ids.Add(inst.Id);
        }
        try { File.WriteAllText(Path.Combine(PluginPaths.Root, "imagem-aerea-diagnostico.txt"), diag.ToString()); }
        catch { /* opcional */ }
        Log.Info(diag.ToString());
        return (ids, report);
    }

    /// <summary>Largura × altura reais no modelo (m) pelos cantos da imagem.</summary>
    public static (double W, double H) Size(ImageInstance inst)
    {
        var tl = inst.GetLocation(BoxPlacement.TopLeft);
        var br = inst.GetLocation(BoxPlacement.BottomRight);
        return (UnitConv.M(Math.Abs(br.X - tl.X)), UnitConv.M(Math.Abs(tl.Y - br.Y)));
    }

    private static string Measure(ImageInstance inst)
    {
        var (w, h) = Size(inst);
        return $"{w:0.0000} × {h:0.0000} m no modelo";
    }

    /// <summary>JPEG de um bloco BGR32.</summary>
    public static void SaveJpeg(byte[] bgr32, int w, int h, string file)
    {
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgr32, w * 4);
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}

/// <summary>
/// Importar imagem aérea de qualquer origem (Google Earth Pro, ortofoto, drone): com world file ou GeoTIFF é reprojetada
/// para o plano do projeto e colocada na escala e posição corretas; sem georreferência é colocada e calibrada por 2 pontos.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdImportarImagem : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;
        if (view is not ViewPlan || view.IsTemplate) throw new UserMessageException("Abra uma vista de planta para importar a imagem aérea.");
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Imagem aérea (a licença da imagem é de quem a obteve)",
            Filter = "Imagens|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp|Todos|*.*",
        };
        if (dlg.ShowDialog() != true) return Result.Cancelled;
        var file = dlg.FileName;

        var (pixels, sw, sh, pixelScale, tiepoint, geoKeys) = LoadImage(file);
        var wf = FindWorldFile(file) ?? WorldFile.FromGeoTiff(pixelScale, tiepoint);
        var z = view is ViewPlan vp && vp.GenLevel != null ? vp.GenLevel.ProjectElevation : 0;

        if (wf == null)
        {
            // Sem georreferência: coloca com um tamanho aproximado de pixel e calibra por 2 pontos já em seguida.
            var mpp = 0.3;
            var w = new FormWindow("Importar imagem aérea", "Imagem sem georreferência",
                    "O arquivo não tem world file (.jgw/.pgw/.tfw) nem GeoTIFF. A imagem será colocada no centro da vista com o tamanho de pixel " +
                    "abaixo e, em seguida, você calibra a escala clicando 2 pontos de distância conhecida (ex.: uma quadra medida no Google Earth).",
                    null, null, false, "Colocar e calibrar", 620, 420)
                .Number("Tamanho aproximado do pixel (m)", () => mpp, v => mpp = v, 0.01, 50, "0.000", "Só para o primeiro posicionamento; a calibração corrige a escala.");
            if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
            var c = ViewCenter(uidoc, view);
            var half = new Vec2(sw * mpp / 2, sh * mpp / 2);
            ElementId id;
            List<string> rep;
            using (var t = new Transaction(doc, "Importar imagem aérea"))
            {
                t.Start();
                var (ids, r) = ImagePlacer.Place(doc, view, new[] { new ImageBlock(file, c - half, c + half, sw, sh) }, z);
                id = ids[0];
                rep = r;
                t.Commit();
            }
            return CmdCalibrarImagem.Calibrate(uidoc, (ImageInstance)doc.GetElement(id), rep);
        }

        // Com georreferência: sistema de coordenadas (GeoTIFF, .prj ou escolhido).
        var crs = GeoTiffKeys.Epsg(geoKeys) is { } epsg ? ImageCrs.FromEpsg(epsg) : null;
        var prj = Path.ChangeExtension(file, ".prj");
        if (crs == null && File.Exists(prj)) crs = ImageCrs.FromPrj(File.ReadAllText(prj));
        var stored = GeoReferenceStore.Load(doc);
        if (crs == null)
        {
            var looksGeo = Math.Abs(wf.A) < 0.01 && Math.Abs(wf.C) <= 180 && Math.Abs(wf.F) <= 90;
            var tipo = looksGeo ? TipoCrs.Geografico : TipoCrs.Utm;
            var zone = stored != null ? Utm.ZoneOf(stored.Longitude) : 23;
            var south = true;
            var w = new FormWindow("Importar imagem aérea", "Sistema de coordenadas do world file",
                    "O world file não diz o sistema de coordenadas. Informe-o (no Brasil, ortofotos costumam estar em UTM SIRGAS 2000; " +
                    "exportações com coordenadas em graus são geográficas).", null, null, false, "Continuar", 620, 460)
                .Choice("Sistema", new[] { ("Geográficas (lat/lon)", TipoCrs.Geografico), ("UTM SIRGAS 2000", TipoCrs.Utm), ("Metros do projeto", TipoCrs.PlanoDoProjeto) }, () => tipo, v => tipo = v)
                .Integer("Fuso UTM", () => zone, v => zone = v, 18, 25)
                .Check("Hemisfério sul", () => south, v => south = v);
            if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
            crs = new ImageCrs(tipo, zone, south);
        }

        // Georreferência do projeto: a gravada ou uma nova no centro da imagem.
        var geo = stored;
        if (geo == null)
        {
            var (cx, cy) = wf.At(sw / 2.0, sh / 2.0);
            var (lat, lon) = crs.Tipo switch
            {
                TipoCrs.Geografico => (cy, cx),
                TipoCrs.Utm => new Utm(crs.Zone, crs.South).Inverse(cx, cy),
                _ => throw new UserMessageException("Imagem em metros do projeto precisa de uma origem geográfica: use antes a Imagem de Satélite (ou calibre por 2 pontos)."),
            };
            double north = 0;
            try { north = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle; } catch { }
            geo = new GeoReference { Latitude = lat, Longitude = lon, NorthAngle = north, OriginX = ViewCenter(uidoc, view).X, OriginY = ViewCenter(uidoc, view).Y };
        }

        // Extensão no modelo e resolução de saída (a do arquivo, limitada a 36 Mpx).
        var corners = new[] { (0.0, 0.0), (sw, 0.0), (0.0, sh), (sw, sh) }.Select(q => { var (x, y) = wf.At(q.Item1, q.Item2); return crs.ToModel(geo, x, y); }).ToList();
        var min = new Vec2(corners.Min(p => p.X), corners.Min(p => p.Y));
        var max = new Vec2(corners.Max(p => p.X), corners.Max(p => p.Y));
        var srcMpp = corners[0].DistanceTo(corners[1]) / sw;
        var outMpp = Math.Max(srcMpp, Math.Sqrt((max.X - min.X) * (max.Y - min.Y) / SatellitePlan.MaxPixels));
        var pw = (int)Math.Ceiling((max.X - min.X) / outMpp);
        var ph = (int)Math.Ceiling((max.Y - min.Y) / outMpp);
        max = min + new Vec2(pw * outMpp, ph * outMpp);
        var folder = Path.Combine(PluginPaths.Root, "imagens-importadas", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var blocks = new List<ImageBlock>();
        var nx = (pw + SatellitePlan.BlockSize - 1) / SatellitePlan.BlockSize;
        var ny = (ph + SatellitePlan.BlockSize - 1) / SatellitePlan.BlockSize;
        for (int r = 0; r < ny; r++)
            for (int c = 0; c < nx; c++)
            {
                int px0 = c * pw / nx, px1 = (c + 1) * pw / nx, py0 = r * ph / ny, py1 = (r + 1) * ph / ny;
                var bmin = new Vec2(min.X + px0 * outMpp, max.Y - py1 * outMpp);
                var bmax = new Vec2(min.X + px1 * outMpp, max.Y - py0 * outMpp);
                var geoRef = geo;
                var crsRef = crs;
                var px = ImageReprojection.Resample(bmin, bmax, outMpp, px1 - px0, py1 - py0,
                    p => { var (x, y) = crsRef.FromModel(geoRef, p); return wf.Inverse(x, y); }, pixels, sw, sh);
                var f = Path.Combine(folder, $"bloco-{r + 1}-{c + 1}.jpg");
                ImagePlacer.SaveJpeg(px, px1 - px0, py1 - py0, f);
                blocks.Add(new ImageBlock(f, bmin, bmax, px1 - px0, py1 - py0));
            }

        List<string> report;
        using (var t = new Transaction(doc, "Importar imagem aérea"))
        {
            t.Start();
            var (ids, rep) = ImagePlacer.Place(doc, view, blocks, z);
            report = rep;
            if (stored == null) { geo.LastImageElements = ids.Select(i => i.Value).ToList(); GeoReferenceStore.Save(doc, geo); }
            t.Commit();
        }
        new TaskDialog(AppTitle)
        {
            MainInstruction = $"Imagem aérea importada na escala do projeto ({blocks.Count} bloco(s), {outMpp:0.000} m/px).",
            MainContent = $"Arquivo: {Path.GetFileName(file)} · sistema: {crs}\n" + string.Join("\n", report) +
                          "\n\nConfira medindo uma distância conhecida (Anotar → Alinhada). Diagnóstico completo em " +
                          Path.Combine(PluginPaths.Root, "imagem-aerea-diagnostico.txt") + ".",
        }.Show();
        return Result.Succeeded;
    }

    private static WorldFile? FindWorldFile(string file)
    {
        var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
        var cands = new List<string> { "wld" };
        if (ext.Length >= 3) { cands.Insert(0, $"{ext[0]}{ext[^1]}w"); cands.Insert(1, ext + "w"); }
        foreach (var c in cands)
        {
            var p = Path.ChangeExtension(file, "." + c);
            if (File.Exists(p) && WorldFile.Parse(File.ReadAllText(p)) is { } wf) return wf;
        }
        return null;
    }

    /// <summary>Pixels BGR32 e, em TIFF, as tags GeoTIFF (33550, 33922, 34735).</summary>
    private static (byte[] Pixels, int W, int H, double[]? Scale, double[]? Tie, ushort[]? Keys) LoadImage(string file)
    {
        using var fs = File.OpenRead(file);
        var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = dec.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 150_000_000)
            throw new UserMessageException("Imagem grande demais (mais de 150 milhões de pixels). Recorte-a antes de importar.");
        double[]? scale = null, tie = null;
        ushort[]? keys = null;
        if (frame.Metadata is BitmapMetadata md)
        {
            try { scale = md.GetQuery("/ifd/{ushort=33550}") as double[]; } catch { }
            try { tie = md.GetQuery("/ifd/{ushort=33922}") as double[]; } catch { }
            try { keys = md.GetQuery("/ifd/{ushort=34735}") as ushort[]; } catch { }
        }
        var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
        var buf = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        conv.CopyPixels(buf, frame.PixelWidth * 4, 0);
        return (buf, frame.PixelWidth, frame.PixelHeight, scale, tie, keys);
    }

    internal static Vec2 ViewCenter(UIDocument uidoc, View view)
    {
        try
        {
            var uv = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
            if (uv != null) { var c = uv.GetZoomCorners(); return UnitConv.ToVec2((c[0] + c[1]) / 2); }
        }
        catch { }
        return Vec2.Zero;
    }
}

/// <summary>
/// Calibrar escala da imagem: 2 pontos clicados na imagem + distância real (escala em torno do 1º ponto) ou lat/lon dos
/// dois pontos (escala, posição e Norte verdadeiro).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCalibrarImagem : CommandBase
{
    private sealed class ImageFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is ImageInstance;
        public bool AllowReference(Reference r, XYZ p) => false;
    }

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var img = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<ImageInstance>().FirstOrDefault();
        if (img == null)
        {
            var r = uidoc.Selection.PickObject(ObjectType.Element, new ImageFilter(), "Selecione a imagem a calibrar");
            img = (ImageInstance)doc.GetElement(r);
        }
        return Calibrate(uidoc, img, null);
    }

    internal static Result Calibrate(UIDocument uidoc, ImageInstance img, List<string>? previous)
    {
        var doc = uidoc.Document;
        var p1x = uidoc.Selection.PickPoint(ObjectSnapTypes.None, "Calibrar: clique o 1º ponto de referência NA IMAGEM (ex.: esquina de uma quadra)");
        var p2x = uidoc.Selection.PickPoint(ObjectSnapTypes.None, "Calibrar: clique o 2º ponto NA IMAGEM (o mais longe possível do 1º)");
        var p1 = UnitConv.ToVec2(p1x);
        var p2 = UnitConv.ToVec2(p2x);
        var measured = p1.DistanceTo(p2);
        if (measured < 0.01) throw new UserMessageException("Os dois pontos precisam ser diferentes.");

        var byGeo = false;
        var dist = Math.Round(measured, 2);
        string? g1 = null, g2 = null;
        var rotate = true;
        var w = new FormWindow("Calibrar escala da imagem", "Distância ou coordenadas reais dos 2 pontos",
                $"Distância entre os pontos clicados, na escala atual da imagem: {measured:0.00} m.\n" +
                "Informe a distância REAL entre eles (medida no Google Earth, na planta do loteamento ou no campo), ou as coordenadas " +
                "lat/lon dos dois pontos (também posiciona a imagem no lugar certo e gira para o Norte verdadeiro).", null, null, false, "Calibrar", 640, 520)
            .Check("Usar coordenadas (lat, lon) em vez da distância", () => byGeo, v => byGeo = v)
            .Number("Distância real (m)", () => dist, v => dist = v, 0.01, 100000, "0.00")
            .Text("1º ponto (lat, lon)", () => g1, v => g1 = v)
            .Text("2º ponto (lat, lon)", () => g2, v => g2 = v)
            .Check("Girar para o Norte verdadeiro (com coordenadas)", () => rotate, v => rotate = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;

        Similarity sim;
        GeoReference? geo = GeoReferenceStore.Load(doc);
        if (byGeo)
        {
            if (!GeoReference.TryParseLatLon(g1, out var la1, out var lo1) || !GeoReference.TryParseLatLon(g2, out var la2, out var lo2))
                throw new UserMessageException("Coordenadas inválidas: use \"latitude, longitude\" em graus decimais.");
            if (geo == null)
            {
                double north = 0;
                try { north = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle; } catch { }
                geo = new GeoReference { Latitude = la1, Longitude = lo1, NorthAngle = north, OriginX = p1.X, OriginY = p1.Y };
            }
            sim = Similarity.FromGeo(geo, p1, p2, (la1, lo1), (la2, lo2), rotate);
        }
        else sim = Similarity.FromDistance(p1, p2, dist);

        var (w0, h0) = ImagePlacer.Size(img);
        using (var t = new Transaction(doc, "Calibrar escala da imagem"))
        {
            t.Start();
            var wasPinned = img.Pinned;
            img.Pinned = false;
            // Escala em torno do 1º ponto: o canto superior esquerdo vai para Pivô + (canto − Pivô)·k.
            var tl = UnitConv.ToVec2(img.GetLocation(BoxPlacement.TopLeft));
            var z = img.GetLocation(BoxPlacement.TopLeft).Z;
            var target = sim.Pivot + (tl - sim.Pivot) * sim.Scale;
            img.Width *= sim.Scale;
            doc.Regenerate();
            // Corrige pelo tamanho medido (o parâmetro Largura não é necessariamente em metros do modelo).
            for (int i = 0; i < 4; i++)
            {
                var (aw, _) = ImagePlacer.Size(img);
                var want = w0 * sim.Scale;
                if (Math.Abs(aw - want) <= ImagePlacer.ToleranceM || aw < 1e-9) break;
                img.Width *= want / aw;
                doc.Regenerate();
            }
            img.SetLocation(new XYZ(UnitConv.Ft(target.X), UnitConv.Ft(target.Y), z), BoxPlacement.TopLeft);
            // Posição (lat/lon): leva o 1º ponto ao lugar real e gira em torno dele.
            var shift = sim.Target - sim.Pivot;
            if (shift.Length > 1e-6) ElementTransformUtils.MoveElement(doc, img.Id, UnitConv.ToXyz(shift, 0));
            string? rotNote = null;
            if (Math.Abs(sim.Rotation) > 1e-6)
            {
                try
                {
                    var c = UnitConv.ToXyz(sim.Target, UnitConv.M(z));
                    ElementTransformUtils.RotateElement(doc, img.Id, Line.CreateBound(c, c + XYZ.BasisZ), sim.Rotation);
                }
                catch (Exception ex)
                {
                    rotNote = $"A imagem não pôde ser girada {sim.Rotation * 180 / Math.PI:0.00}° pelo Revit ({ex.Message}); a escala e a posição foram aplicadas.";
                }
            }
            if (geo != null && GeoReferenceStore.Load(doc) == null && byGeo) GeoReferenceStore.Save(doc, geo);
            img.Pinned = wasPinned;
            t.Commit();

            var (w1, h1) = ImagePlacer.Size(img);
            var sb = new StringBuilder();
            if (previous != null) foreach (var l in previous) sb.AppendLine(l);
            sb.AppendLine($"Fator aplicado: {sim.Scale:0.00000} (medido {measured:0.000} m → real {measured * sim.Scale:0.000} m).");
            sb.AppendLine($"Tamanho da imagem: {w0:0.000} × {h0:0.000} m → {w1:0.000} × {h1:0.000} m (esperado {w0 * sim.Scale:0.000} × {h0 * sim.Scale:0.000} m).");
            if (Math.Abs(sim.Rotation) > 1e-6) sb.AppendLine($"Rotação para o Norte verdadeiro: {sim.Rotation * 180 / Math.PI:0.00}°.");
            if (rotNote != null) sb.AppendLine("⚠ " + rotNote);
            sb.AppendLine("\nConfira: meça de novo os dois pontos com Anotar → Alinhada.");
            new TaskDialog(AppTitle) { MainInstruction = "Imagem calibrada.", MainContent = sb.ToString() }.Show();
        }
        return Result.Succeeded;
    }
}
