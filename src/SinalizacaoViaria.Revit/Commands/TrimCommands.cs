using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Apagar Trecho (só sinalização horizontal): escolhe-se a marca (linha, faixa de pedestres, zebrado, seta, legenda...) e
/// uma janela mostra a planta com as peças reais dela – clique em cada traço/peça para apagar, Shift + arrastar para apagar
/// o que está numa janela. Os trechos viram recortes guardados na própria marca (podem ser devolvidos ou desativados depois);
/// regerar/editar a marca mantém os recortes.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdApagarTrecho : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var stored = Preselected(uidoc) ?? Pick(uidoc);
        if (stored == null) return Result.Cancelled;
        var def = stored.Definition;

        // Geometria da marca SEM os recortes manuais (para poder devolver peças apagadas antes).
        var service = new MarkingService(doc, uidoc.ActiveView);
        var manual = def.Exclusions.Where(z => z.Manual).ToList();
        def.Exclusions.RemoveAll(z => z.Manual);
        MarkingGeometry geo;
        var warnings = new List<string>();
        try { geo = service.BuildGeometry(def, out _, warnings); }
        catch (Exception ex)
        {
            Log.Error("Apagar trecho – geometria", ex);
            warnings.Add(ex.Message);
            geo = new MarkingGeometry();
        }
        finally { def.Exclusions.AddRange(manual); }
        // Sem peças calculadas (caminho não resolvido, marca antiga...): as peças são lidas dos elementos do modelo – cada
        // traço/seta é um sólido próprio na forma direta (ou uma região preenchida no 2D).
        if (geo.Pieces.Count == 0)
        {
            Log.Info($"Apagar trecho: geometria vazia para {def.DisplayCode} ({string.Join("; ", warnings)}) – lendo os elementos do modelo.");
            geo = ModelPieces(doc, stored.MarkingId);
        }
        if (geo.Pieces.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Não foi possível ler as peças dessa marca." + (warnings.Count > 0 ? "\n\n" + string.Join("\n", warnings.Distinct()) : ""));
            return Result.Cancelled;
        }
        var info = MarkingBuilder.Describe(def, PluginContext.Catalog);
        var existing = manual.Where(z => z.Points.Count >= 3).Select(z => new Polygon2(z.Points)).ToList();
        var active = manual.Count == 0 || manual.Any(z => z.Enabled);
        var w = new TrimWindow($"{info.Code} – {info.Name}", geo, existing, active);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;

        def.Exclusions.RemoveAll(z => z.Manual);
        foreach (var z in w.Zones) def.Exclusions.Add(new ExclusionZone { Manual = true, Enabled = w.Active, Points = z.Outer.ToList() });
        ReportResults("Apagar trecho", MarkingCreator.Commit(uidoc, new[] { def }, "SV - Apagar trecho"));
        return Result.Succeeded;
    }

    /// <summary>Peças da marca lidas dos elementos do Revit: pegada de cada sólido (faces para cima) e regiões preenchidas.</summary>
    internal static MarkingGeometry ModelPieces(Document doc, string markingId)
    {
        var geo = new MarkingGeometry();
        foreach (var r in MarkingStorage.ById(doc, markingId))
        {
            try
            {
                if (r.Element is FilledRegion fr)
                {
                    foreach (var loop in fr.GetBoundaries())
                        if (Ring(loop) is { Count: >= 3 } ring) geo.Add(new Polygon2(ring), r.Color);
                    continue;
                }
                var ge = r.Element.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });
                if (ge == null) continue;
                foreach (var solid in Solids(ge))
                    if (Footprint(solid) is { } f && f.Area > 1e-4) geo.Add(f, r.Color);
            }
            catch (Exception ex) { Log.Error("Apagar trecho – peças do modelo", ex); }
        }
        return geo;
    }

    private static List<Vec2>? Ring(CurveLoop loop)
    {
        var pts = new List<Vec2>();
        foreach (var c in loop)
        {
            var t = c.Tessellate();
            for (int i = 0; i + 1 < t.Count; i++) pts.Add(UnitConv.ToVec2(t[i]));
        }
        return pts;
    }

    private static IEnumerable<Solid> Solids(GeometryElement ge)
    {
        foreach (var o in ge)
        {
            if (o is Solid s && s.Faces.Size > 0 && s.Volume > 1e-9) yield return s;
            else if (o is GeometryInstance gi)
                foreach (var x in Solids(gi.GetInstanceGeometry())) yield return x;
        }
    }

    /// <summary>Pegada em planta (m) de um sólido: união das faces voltadas para cima.</summary>
    private static Polygon2? Footprint(Solid s)
    {
        var polys = new List<Polygon2>();
        foreach (Face f in s.Faces)
        {
            try
            {
                var bb = f.GetBoundingBox();
                if (f.ComputeNormal((bb.Min + bb.Max) / 2).Z < 0.3) continue;
                var loop = f.GetEdgesAsCurveLoops().FirstOrDefault();
                if (loop == null || Ring(loop) is not { Count: >= 3 } ring) continue;
                polys.Add(new Polygon2(ring));
            }
            catch { /* face sem contorno */ }
        }
        if (polys.Count == 0) return null;
        try { return PolygonOps.Union(polys).OrderByDescending(x => x.Area).FirstOrDefault(); }
        catch { return polys.OrderByDescending(x => x.Area).First(); }
    }

    /// <summary>Sinalização horizontal (pinturas, legendas, zebrados, faixas) – o que a ferramenta aceita.</summary>
    internal static bool IsHorizontal(MarkingDefinition def)
    {
        if (def is IAnnotationDefinition) return false;
        try
        {
            var info = MarkingBuilder.Describe(def, PluginContext.Catalog);
            return QuantityRow.Categorize(def, info.Group) == CategoriaQuantitativo.SinalizacaoHorizontal;
        }
        catch
        {
            return false;
        }
    }

    private static StoredMarking? Preselected(UIDocument uidoc)
    {
        var ids = uidoc.Selection.GetElementIds();
        if (ids.Count != 1) return null;
        var st = MarkingStorage.Read(uidoc.Document.GetElement(ids.First()));
        return st != null && IsHorizontal(st.Definition) ? st : null;
    }

    private static StoredMarking? Pick(UIDocument uidoc)
    {
        try
        {
            var r = uidoc.Selection.PickObject(ObjectType.Element, new HorizontalFilter(),
                "Apagar trecho: clique a sinalização horizontal (linha, faixa de pedestres, zebrado, seta, legenda...)");
            return MarkingStorage.Read(uidoc.Document.GetElement(r));
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }

    private sealed class HorizontalFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) =>
            new MarkingSelectionFilter().AllowElement(elem) && MarkingStorage.Read(elem) is { } st && IsHorizontal(st.Definition);
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
