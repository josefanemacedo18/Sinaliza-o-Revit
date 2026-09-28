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
        try { geo = service.BuildGeometry(def, out _); }
        finally { def.Exclusions.AddRange(manual); }
        if (geo.Pieces.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Essa marca não tem peças para apagar.");
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
