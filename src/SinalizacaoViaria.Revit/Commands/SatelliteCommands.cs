using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Imagem Aérea: Google Earth Pro com calibração automática pelos marcadores, ou mapa no plugin (OpenStreetMap, fonte
/// própria XYZ, WMS). A imagem entra reprojetada na escala métrica real, medida no modelo (&lt; 1 mm), atrás de tudo na
/// planta, com escala gráfica e créditos.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdImagemSatelite : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;
        if (view is not ViewPlan || view.IsTemplate)
            throw new UserMessageException("Abra uma vista de planta (piso, forro ou implantação) para colocar a imagem aérea.");

        var stored = GeoReferenceStore.Load(doc);
        double siteLat = 0, siteLon = 0, siteAlt = 0, north = 0;
        try
        {
            var site = doc.SiteLocation;
            siteLat = site.Latitude * 180 / Math.PI;
            siteLon = site.Longitude * 180 / Math.PI;
            siteAlt = UnitConv.M(site.Elevation);
        }
        catch { /* sem localização definida */ }
        try { north = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle; }
        catch { /* Norte verdadeiro = Norte do projeto */ }

        var w = new AerialImageWindow(new SatelliteContext
        {
            Stored = stored, SiteLatitude = siteLat, SiteLongitude = siteLon, SiteAltitude = siteAlt, NorthAngle = north,
            ViewCenter = CmdImportarImagem.ViewCenter(uidoc, view),
        });
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        var res = w.Result;
        var (ids, report) = Place(doc, view, res, w.ReplacePrevious);

        var sb = new StringBuilder();
        sb.AppendLine($"{res.SourceName}: {res.Blocks.Count} bloco(s), {UiHelpers.F(res.Max.X - res.Min.X, "0")} × {UiHelpers.F(res.Max.Y - res.Min.Y, "0")} m, {UiHelpers.F(res.OutputMpp, "0.000")} m/px.");
        if (res.ReprojectionError > 0) sb.AppendLine($"Erro máximo da reprojeção: {UiHelpers.F(res.ReprojectionError * 1000, "0.000")} mm.");
        sb.AppendLine($"Origem {res.Geo.Latitude:0.000000}, {res.Geo.Longitude:0.000000} · altitude {UiHelpers.F(res.Geo.Altitude, "0")} m.");
        foreach (var n in res.Notes) sb.AppendLine("• " + n);
        foreach (var l in report) sb.AppendLine(l);
        sb.AppendLine();
        sb.AppendLine("Confira: meça a escala gráfica e uma distância conhecida (Anotar → Alinhada).");
        new TaskDialog(AppTitle) { MainInstruction = $"Imagem aérea colocada ({ids.Count} elementos).", MainContent = sb.ToString() }.Show();

        // Sem os marcadores do Google Earth: confirma a escala por 2 pontos.
        if (w.NeedsCalibration && ids.FirstOrDefault() is { } first && doc.GetElement(first) is ImageInstance img && res.Blocks.Count == 1)
            return CmdCalibrarImagem.Calibrate(uidoc, img, null);
        return Result.Succeeded;
    }

    /// <summary>Cria as imagens (medidas e corrigidas), a escala gráfica e os créditos e grava a georreferência.</summary>
    private static (List<ElementId> Ids, List<string> Report) Place(Document doc, View view, AerialResult res, bool replace)
    {
        var geo = res.Geo;
        var z = view is ViewPlan vp && vp.GenLevel != null ? vp.GenLevel.ProjectElevation : 0;
        var ids = new List<ElementId>();
        using var t = new Transaction(doc, "Imagem Aérea");
        t.Start();
        if (replace)
            foreach (var old in geo.LastImageElements)
            {
                var id = new ElementId(old);
                try { if (doc.GetElement(id) != null) doc.Delete(id); }
                catch { /* já apagado */ }
            }
        var (imgIds, report) = ImagePlacer.Place(doc, view, res.Blocks, z);
        ids.AddRange(imgIds);
        ids.AddRange(ScaleBar(doc, view, res, z));
        geo.LastImageElements = ids.Select(i => i.Value).ToList();
        geo.LastImageInfo = $"{res.SourceName}, {res.OutputMpp:0.000} m/px, {DateTime.Now:dd/MM/yyyy}";
        GeoReferenceStore.Save(doc, geo);
        t.Commit();
        return (ids, report);
    }

    /// <summary>Escala gráfica (0–10–50–100 m ou menor) e créditos no canto inferior esquerdo da imagem.</summary>
    private static List<ElementId> ScaleBar(Document doc, View view, AerialResult res, double z)
    {
        var list = new List<ElementId>();
        try
        {
            var min = res.Min;
            var width = res.Max.X - min.X;
            var height = res.Max.Y - min.Y;
            var (len, ticks) = width >= 250 ? (100.0, new[] { 0.0, 10, 50, 100 })
                : width >= 80 ? (50.0, new[] { 0.0, 10, 50 }) : (10.0, new[] { 0.0, 5, 10 });
            var o = min + new Vec2(width * 0.04, height * 0.08);
            XYZ P(Vec2 v) => new(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), z);
            list.Add(doc.Create.NewDetailCurve(view, Line.CreateBound(P(o), P(o + new Vec2(len, 0)))).Id);
            var tick = len * 0.04;
            var textType = new StyleService(doc).TextType(2.5);
            foreach (var k in ticks)
            {
                list.Add(doc.Create.NewDetailCurve(view, Line.CreateBound(P(o + new Vec2(k, 0)), P(o + new Vec2(k, tick)))).Id);
                var opt = new TextNoteOptions(textType) { HorizontalAlignment = HorizontalTextAlignment.Center, VerticalAlignment = VerticalTextAlignment.Bottom };
                list.Add(TextNote.Create(doc, view.Id, P(o + new Vec2(k, tick * 1.5)), k == 0 ? "0" : $"{k:0} m", opt).Id);
            }
            var credit = $"{res.Attribution} · {res.SourceName}, {UiHelpers.F(res.OutputMpp, "0.00")} m/px, {DateTime.Now:dd/MM/yyyy} · escala métrica real (TM local SIRGAS 2000)";
            var copt = new TextNoteOptions(textType) { HorizontalAlignment = HorizontalTextAlignment.Left, VerticalAlignment = VerticalTextAlignment.Top };
            list.Add(TextNote.Create(doc, view.Id, P(o - new Vec2(0, tick)), credit, copt).Id);
        }
        catch (Exception ex)
        {
            Log.Error("Imagem aérea – escala gráfica", ex);
        }
        return list;
    }
}
