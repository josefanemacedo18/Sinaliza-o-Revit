using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Geo;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Imagem de Satélite: imagem aérea (Google Satélite ou OpenStreetMap) reprojetada para a escala métrica real e colocada
/// na vista de planta, em blocos fixados atrás de tudo, com escala gráfica e créditos – referência para desenhar o eixo.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdImagemSatelite : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;
        if (view is not ViewPlan || view.IsTemplate)
            throw new UserMessageException("Abra uma vista de planta (piso, forro ou implantação) para colocar a imagem de satélite.");

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

        var ctx = new SatelliteContext
        {
            Stored = stored, SiteLatitude = siteLat, SiteLongitude = siteLon, SiteAltitude = siteAlt, NorthAngle = north,
            ViewCenter = ViewCenter(uidoc, view),
        };
        var w = new SatelliteWindow(ctx);
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        var img = w.Result;
        var count = Place(doc, view, img, w.ReplacePrevious);

        var p = img.Plan;
        var sb = new StringBuilder();
        sb.AppendLine($"{p.Source.Nome}: {img.Files.Count} bloco(s), {p.PixelWidth} × {p.PixelHeight} px, {UiHelpers.F(p.Width, "0")} × {UiHelpers.F(p.Height, "0")} m.");
        sb.AppendLine($"Zoom {p.Zoom} · {UiHelpers.F(p.OutputMpp, "0.000")} m/px efetivos · erro máximo da reprojeção {UiHelpers.F(img.ReprojectionError * 1000, "0.000")} mm.");
        sb.AppendLine($"Altitude {UiHelpers.F(p.Geo.Altitude, "0")} m (fator 1 − {UiHelpers.F((1 - p.Geo.ElevationFactor) * 1e5, "0.0")}×10⁻⁵) · origem {p.Geo.Latitude:0.000000}, {p.Geo.Longitude:0.000000}.");
        foreach (var n in p.Notes) sb.AppendLine("• " + n);
        sb.AppendLine();
        sb.AppendLine("Confira a escala: meça a escala gráfica (Anotar → Alinhada) e uma distância conhecida da imagem.");
        sb.AppendLine("A posição absoluta de imagens de satélite tem erro típico de alguns metros; ortofoto oficial ou levantamento topográfico prevalecem.");
        new TaskDialog(AppTitle) { MainInstruction = $"Imagem de satélite colocada ({count} elementos).", MainContent = sb.ToString() }.Show();
        return Result.Succeeded;
    }

    private static Vec2 ViewCenter(UIDocument uidoc, View view)
    {
        try
        {
            var uv = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
            if (uv != null)
            {
                var c = uv.GetZoomCorners();
                return UnitConv.ToVec2((c[0] + c[1]) / 2);
            }
        }
        catch { /* segue na origem */ }
        return Vec2.Zero;
    }

    /// <summary>Cria as imagens, a escala gráfica e os créditos numa transação e grava a georreferência.</summary>
    private static int Place(Document doc, View view, SatelliteImage img, bool replace)
    {
        var plan = img.Plan;
        var geo = plan.Geo;
        var z = view is ViewPlan vp && vp.GenLevel != null ? vp.GenLevel.ProjectElevation : 0;
        var ids = new List<ElementId>();
        using var t = new Transaction(doc, "Imagem de Satélite");
        t.Start();
        if (replace)
            foreach (var old in geo.LastImageElements)
            {
                var id = new ElementId(old);
                try { if (doc.GetElement(id) != null) doc.Delete(id); }
                catch { /* já apagado */ }
            }

        foreach (var (b, file) in img.Files)
        {
            var type = ImageType.Create(doc, new ImageTypeOptions(file, false, ImageTypeSource.Import));
            var topLeft = new XYZ(UnitConv.Ft(b.Min.X), UnitConv.Ft(b.Max.Y), z);
            var inst = ImageInstance.Create(doc, view, type.Id, new ImagePlacementOptions(topLeft, BoxPlacement.TopLeft));
            // Tamanho exato no modelo (pés) a partir dos metros – e o canto de volta ao lugar depois de redimensionar.
            inst.Width = UnitConv.Ft(b.Max.X - b.Min.X);
            var h = UnitConv.Ft(b.Max.Y - b.Min.Y);
            if (Math.Abs(inst.Height - h) > 1e-6)
                try { inst.Height = h; } catch { /* proporção travada: já correta pelos pixels quadrados */ }
            inst.SetLocation(topLeft, BoxPlacement.TopLeft);
            inst.DrawLayer = DrawLayer.Background;
            inst.Pinned = true;
            ids.Add(inst.Id);
        }

        ids.AddRange(ScaleBar(doc, view, plan, img.Attribution, z));

        geo.LastImageElements = ids.Select(i => i.Value).ToList();
        geo.LastImageInfo = $"{plan.Source.Nome}, zoom {plan.Zoom}, {plan.OutputMpp:0.000} m/px, {DateTime.Now:dd/MM/yyyy}";
        GeoReferenceStore.Save(doc, geo);
        t.Commit();
        return ids.Count;
    }

    /// <summary>Escala gráfica (0–10–50–100 m ou menor) e créditos no canto inferior esquerdo da imagem.</summary>
    private static List<ElementId> ScaleBar(Document doc, View view, SatellitePlan plan, string attribution, double z)
    {
        var res = new List<ElementId>();
        try
        {
            var (len, ticks) = plan.Width >= 250 ? (100.0, new[] { 0.0, 10, 50, 100 })
                : plan.Width >= 80 ? (50.0, new[] { 0.0, 10, 50 }) : (10.0, new[] { 0.0, 5, 10 });
            var o = plan.Min + new Vec2(plan.Width * 0.04, plan.Height * 0.08);
            XYZ P(Vec2 v) => new(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), z);
            res.Add(doc.Create.NewDetailCurve(view, Line.CreateBound(P(o), P(o + new Vec2(len, 0)))).Id);
            var tick = len * 0.04;
            var styles = new StyleService(doc);
            var textType = styles.TextType(2.5);
            foreach (var k in ticks)
            {
                res.Add(doc.Create.NewDetailCurve(view, Line.CreateBound(P(o + new Vec2(k, 0)), P(o + new Vec2(k, tick)))).Id);
                var opt = new TextNoteOptions(textType) { HorizontalAlignment = HorizontalTextAlignment.Center, VerticalAlignment = VerticalTextAlignment.Bottom };
                res.Add(TextNote.Create(doc, view.Id, P(o + new Vec2(k, tick * 1.5)), k == 0 ? "0" : $"{k:0} m", opt).Id);
            }
            var credit = $"{attribution} · {plan.Source.Nome}, zoom {plan.Zoom}, {UiHelpers.F(plan.OutputMpp, "0.00")} m/px, {DateTime.Now:dd/MM/yyyy} · escala métrica real (TM local SIRGAS 2000)";
            var copt = new TextNoteOptions(textType) { HorizontalAlignment = HorizontalTextAlignment.Left, VerticalAlignment = VerticalTextAlignment.Top };
            res.Add(TextNote.Create(doc, view.Id, P(o - new Vec2(0, tick)), credit, copt).Id);
        }
        catch (Exception ex)
        {
            Log.Error("Imagem de satélite – escala gráfica", ex);
        }
        return res;
    }
}
