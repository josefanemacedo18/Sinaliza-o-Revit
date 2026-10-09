using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Revit.Commands;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Pranchas no Revit: folhas com o carimbo do projeto, uma vista por trecho com a região de corte girada ao longo do eixo e,
/// na coluna da direita, a legenda de placas e o quadro de quantidades do trecho (vistas de desenho próprias).
/// </summary>
internal static class SheetBuilder
{
    private const double MmToFt = 1 / 304.8;

    /// <summary>Famílias de folha (carimbos) carregadas no projeto.</summary>
    public static List<FamilySymbol> TitleBlocks(Document doc) =>
        new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().OfType<FamilySymbol>()
            .OrderBy(s => s.FamilyName).ThenBy(s => s.Name).ToList();

    /// <summary>Contorno do carimbo na folha (pés), ou nulo.</summary>
    public static BoundingBoxXYZ? TitleBlockBox(Document doc, ViewSheet sheet)
    {
        var tb = new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstOrDefault();
        return tb?.get_BoundingBox(sheet);
    }

    public static string UniqueSheetNumber(Document doc, string wanted)
    {
        var used = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = wanted;
        for (int i = 2; used.Contains(n); i++) n = $"{wanted}-{i}";
        return n;
    }

    public static string UniqueViewName(Document doc, string wanted)
    {
        var used = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = wanted;
        for (int i = 2; used.Contains(n); i++) n = $"{wanted} ({i})";
        return n;
    }

    /// <summary>Elemento da região de corte da vista (o que tem o ID_PARAM igual ao da vista, além dela).</summary>
    public static Element? CropElement(Document doc, View view)
    {
        try
        {
            var rule = ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(BuiltInParameter.ID_PARAM), view.Id);
            return new FilteredElementCollector(doc).WherePasses(new ElementParameterFilter(rule)).ToElements().FirstOrDefault(e => e.Id != view.Id);
        }
        catch (Exception ex)
        {
            Log.Error("Região de corte da vista", ex);
            return null;
        }
    }

    private static void TryRegenerate(Document doc)
    {
        try { doc.Regenerate(); } catch { /* dentro do atualizador não é permitido */ }
    }

    /// <summary>
    /// Ajusta a vista do trecho ao eixo atual: escala, região de corte (tamanho da área da planta, centrada no trecho e girada
    /// na direção dele) e posição na folha. Chamado a cada geração do trecho (criação, edição e mudança da via).
    /// </summary>
    public static void Sync(Document doc, SheetSegmentDefinition d, View view, RenderResult result)
    {
        try
        {
            if (MarkingStorage.ById(doc, d.RoadId).FirstOrDefault()?.Definition is not RoadPavementDefinition pav
                || PathResolver.Resolve(doc, pav.Path)?.Main is not { } axis) return;
            var scale = (int)Math.Round(Math.Max(1, d.Scale));
            if (view.Scale != scale) view.Scale = scale;
            var seg = d.Segment(axis);
            d.AppliedAngleDeg = ApplyCrop(doc, view, seg, d.AppliedAngleDeg, result);
            PlaceViewport(doc, d, view);
        }
        catch (Exception ex)
        {
            Log.Error($"Prancha {d.SheetNumber}", ex);
            result.Warnings.Add($"Prancha {d.SheetNumber}: a vista não pôde ser ajustada ({ex.Message}).");
        }
    }

    private static double ApplyCrop(Document doc, View view, SheetSegment seg, double appliedDeg, RenderResult result)
    {
        view.CropBoxActive = true;
        view.CropBoxVisible = true;
        var crop = CropElement(doc, view);
        var box = view.CropBox;
        var center = box.Transform.OfPoint((box.Min + box.Max) / 2);
        // Desfaz o giro anterior (a região volta a ficar alinhada aos eixos) antes de redimensionar.
        if (crop != null && Math.Abs(appliedDeg) > 1e-9)
        {
            ElementTransformUtils.RotateElement(doc, crop.Id, Line.CreateBound(center, center + XYZ.BasisZ), -appliedDeg * Math.PI / 180);
            TryRegenerate(doc);
            box = view.CropBox;
        }
        var tf = box.Transform;
        var target = new XYZ(UnitConv.Ft(seg.Center.X), UnitConv.Ft(seg.Center.Y), center.Z);
        var lc = tf.Inverse.OfPoint(target);
        var hw = UnitConv.Ft(seg.Width / 2);
        var hh = UnitConv.Ft(seg.Height / 2);
        view.CropBox = new BoundingBoxXYZ
        {
            Transform = tf,
            Min = new XYZ(lc.X - hw, lc.Y - hh, box.Min.Z),
            Max = new XYZ(lc.X + hw, lc.Y + hh, box.Max.Z),
        };
        double applied = 0;
        if (Math.Abs(seg.AngleRad) > 1e-9)
        {
            if (crop != null)
            {
                TryRegenerate(doc);
                ElementTransformUtils.RotateElement(doc, crop.Id, Line.CreateBound(target, target + XYZ.BasisZ), seg.AngleRad);
                applied = seg.AngleDeg;
            }
            else result.Warnings.Add("A região de corte não pôde ser girada nesta vista: a planta fica alinhada ao norte do projeto.");
        }
        view.CropBoxVisible = false;
        try { view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(1); } catch { /* opcional */ }
        return applied;
    }

    /// <summary>Vista do trecho centrada na área da planta da folha (à esquerda da coluna da legenda).</summary>
    private static void PlaceViewport(Document doc, SheetSegmentDefinition d, View view)
    {
        if (string.IsNullOrEmpty(d.SheetId) || doc.GetElement(d.SheetId) is not ViewSheet sheet) return;
        var tb = TitleBlockBox(doc, sheet);
        var min = tb?.Min ?? XYZ.Zero;
        var center = new XYZ(min.X + (d.MarginMm + d.FrameWidthMm / 2) * MmToFt, min.Y + (d.MarginMm + d.FrameHeightMm / 2) * MmToFt, 0);
        var vp = Viewport(doc, sheet, view.Id);
        if (vp == null)
        {
            if (Autodesk.Revit.DB.Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id)) Autodesk.Revit.DB.Viewport.Create(doc, sheet.Id, view.Id, center);
        }
        else vp.SetBoxCenter(center);
    }

    private static Viewport? Viewport(Document doc, ViewSheet sheet, ElementId viewId) =>
        sheet.GetAllViewports().Select(doc.GetElement).OfType<Viewport>().FirstOrDefault(v => v.ViewId == viewId);

    private static ViewDrafting? Drafting(Document doc, string? uid) => string.IsNullOrEmpty(uid) ? null : doc.GetElement(uid) as ViewDrafting;

    /// <summary>
    /// Legenda de placas e quadro de quantidades do trecho: cada um numa vista de desenho própria, colocada na coluna da
    /// direita da folha (legenda em cima, quadro logo abaixo). Desligados, saem da folha. Exige estar fora de transação.
    /// </summary>
    public static List<RenderResult> EnsureContent(UIDocument uidoc, SheetSegmentDefinition d)
    {
        var doc = uidoc.Document;
        var res = new List<RenderResult>();
        var defs = new List<MarkingDefinition>();
        using (var t = new Transaction(doc, "SV - Prancha: vistas da legenda e do quadro"))
        {
            t.Start();
            var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting);
            ViewDrafting? Ensure(bool on, string? uid, string name, out string? newUid)
            {
                var v = Drafting(doc, uid);
                if (!on)
                {
                    if (v != null) doc.Delete(v.Id);
                    newUid = null;
                    return null;
                }
                if (v == null && type != null)
                {
                    v = ViewDrafting.Create(doc, type.Id);
                    v.Name = UniqueViewName(doc, name);
                    v.Scale = 100;
                }
                newUid = v?.UniqueId;
                return v;
            }
            var lv = Ensure(d.Legend, d.LegendViewId, $"SV - {d.SheetNumber} Legenda de placas", out var lUid);
            var qv = Ensure(d.Quantities, d.TableViewId, $"SV - {d.SheetNumber} Quadro de quantidades", out var qUid);
            d.LegendViewId = lUid;
            d.TableViewId = qUid;
            var stored = MarkingStorage.Definitions(doc);
            if (lv != null)
            {
                var lg = stored.FirstOrDefault(x => x.Id == d.LegendId) as LegendDefinition ?? new LegendDefinition { Title = "LEGENDA – SINALIZAÇÃO VERTICAL" };
                lg.Position = Core.Geometry.Vec2.Zero;
                lg.SheetSegmentId = d.ContentOfSegment ? d.Id : null;
                DetailHelpers.PrepareOutput(lg, lv);
                d.LegendId = lg.Id;
                defs.Add(lg);
            }
            else d.LegendId = null;
            if (qv != null)
            {
                var qt = stored.FirstOrDefault(x => x.Id == d.TableId) as QuantityTableDefinition ?? new QuantityTableDefinition();
                qt.Position = Core.Geometry.Vec2.Zero;
                qt.SheetSegmentId = d.ContentOfSegment ? d.Id : null;
                qt.Title = d.ContentOfSegment ? $"QUADRO DE QUANTIDADES – PRANCHA {d.SheetNumber}" : "QUADRO DE QUANTITATIVOS – SINALIZAÇÃO VIÁRIA";
                DetailHelpers.PrepareOutput(qt, qv);
                d.TableId = qt.Id;
                defs.Add(qt);
            }
            else d.TableId = null;
            t.Commit();
        }
        if (defs.Count > 0) res.AddRange(MarkingCreator.Commit(uidoc, defs, "SV - Prancha: legenda e quadro"));
        using (var t = new Transaction(doc, "SV - Prancha: coluna da legenda"))
        {
            t.Start();
            PlaceColumn(doc, d);
            t.Commit();
        }
        return res;
    }

    /// <summary>Coluna da direita: legenda no alto, quadro abaixo dela (contornos lidos depois de gerar).</summary>
    private static void PlaceColumn(Document doc, SheetSegmentDefinition d)
    {
        if (string.IsNullOrEmpty(d.SheetId) || doc.GetElement(d.SheetId) is not ViewSheet sheet) return;
        var tb = TitleBlockBox(doc, sheet);
        if (tb == null) return;
        var left = tb.Min.X + (d.MarginMm + d.FrameWidthMm + 6) * MmToFt;
        var top = tb.Max.Y - d.MarginMm * MmToFt;
        foreach (var uid in new[] { d.LegendViewId, d.TableViewId })
        {
            if (Drafting(doc, uid) is not { } v) continue;
            var vp = Viewport(doc, sheet, v.Id);
            if (vp == null)
            {
                if (!Autodesk.Revit.DB.Viewport.CanAddViewToSheet(doc, sheet.Id, v.Id)) continue;
                vp = Autodesk.Revit.DB.Viewport.Create(doc, sheet.Id, v.Id, new XYZ(left, top, 0));
            }
            TryRegenerate(doc);
            var o = vp.GetBoxOutline();
            vp.SetBoxCenter(vp.GetBoxCenter() + new XYZ(left - o.MinimumPoint.X, top - o.MaximumPoint.Y, 0));
            TryRegenerate(doc);
            top = vp.GetBoxOutline().MinimumPoint.Y - 6 * MmToFt;
        }
    }

    /// <summary>
    /// Depois de editar um trecho: religa o conjunto (linhas de corte e números das vizinhas), refaz os trechos, a legenda e o
    /// quadro e grava os vínculos.
    /// </summary>
    public static List<RenderResult> AfterEdit(UIDocument uidoc, SheetSegmentDefinition edited)
    {
        var doc = uidoc.Document;
        var set = MarkingStorage.Definitions(doc).OfType<SheetSegmentDefinition>().Where(s => s.SetId == edited.SetId && s.Id != edited.Id).ToList();
        set.Add(edited);
        SheetSegmentDefinition.Link(set);
        var res = new List<RenderResult>();
        res.AddRange(EnsureContent(uidoc, edited));
        res.AddRange(MarkingCreator.Commit(uidoc, set, "SV - Pranchas: trechos"));
        return res;
    }
}
