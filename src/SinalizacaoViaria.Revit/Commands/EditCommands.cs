using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

internal static class MarkingPicker
{
    /// <summary>Marcas da seleção atual ou escolhidas pelo usuário (uma definição por marca).</summary>
    public static List<StoredMarking> PickMany(UIDocument uidoc, string prompt)
    {
        var doc = uidoc.Document;
        var pre = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => e != null)
            .Select(MarkingStorage.Read).Where(r => r != null).Cast<StoredMarking>().ToList();
        if (pre.Count > 0) return pre.GroupBy(r => r.MarkingId).Select(g => g.First()).ToList();
        try
        {
            var refs = uidoc.Selection.PickObjects(ObjectType.Element, new MarkingSelectionFilter(), prompt);
            return refs.Select(r => MarkingStorage.Read(doc.GetElement(r))).Where(r => r != null).Cast<StoredMarking>()
                .GroupBy(r => r.MarkingId).Select(g => g.First()).ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return new();
        }
    }

    public static StoredMarking? PickOne(UIDocument uidoc, string prompt)
    {
        var doc = uidoc.Document;
        var pre = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => e != null)
            .Select(MarkingStorage.Read).Where(r => r != null).Cast<StoredMarking>().GroupBy(r => r.MarkingId).ToList();
        if (pre.Count == 1) return pre[0].First();
        try
        {
            var r = uidoc.Selection.PickObject(ObjectType.Element, new MarkingSelectionFilter(), prompt);
            return MarkingStorage.Read(doc.GetElement(r));
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>Edita os parâmetros de uma marca existente.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdEditar : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var stored = MarkingPicker.PickOne(uidoc, "Selecione a marca a editar");
        if (stored == null) return Result.Cancelled;
        MarkingDefinition? edited = stored.Definition switch
        {
            LinearMarkingDefinition l => Show(new LinearWindow("Editar marca linear", Enum.GetValues<GrupoMarca>(), l), w => w.Result),
            HatchMarkingDefinition h => Show(new HatchWindow(h), w => w.Result),
            SymbolMarkingDefinition s => Show(new SymbolWindow(s), w => w.Result),
            TextMarkingDefinition t => Show(new TextWindow(t), w => w.Result),
            ParkingMarkingDefinition p => Show(new ParkingWindow(p), w => w.Result),
            _ => null,
        };
        if (edited == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        edited.Id = stored.MarkingId;
        EnsureDetailView(uidoc, edited.Output, keepExistingView: true);
        var results = MarkingCreator.Commit(uidoc, new[] { edited }, $"SV - Editar {edited.DisplayCode}");
        Report("Edição", results);
        return Result.Succeeded;
    }

    private static MarkingDefinition? Show<TW>(TW w, Func<TW, MarkingDefinition?> result) where TW : System.Windows.Window =>
        UiHelpers.ShowModal(w) == true ? result(w) : null;

    private static void EnsureDetailView(UIDocument uidoc, OutputSettings o, bool keepExistingView)
    {
        if (o.Mode != OutputMode.Detalhe2D) return;
        if (keepExistingView && !string.IsNullOrEmpty(o.ViewId) && uidoc.Document.GetElement(o.ViewId) is View) return;
        CommandBase.EnsureDetailViewPublic(uidoc, o);
    }
}

/// <summary>Regenera todas as marcas do projeto.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdAtualizarTodas : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        PluginContext.ReloadCatalog();
        var defs = MarkingStorage.Definitions(doc);
        if (defs.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhuma marca de sinalização encontrada neste projeto.");
            return Result.Cancelled;
        }
        var results = new List<RenderResult>();
        using (MarkingService.RenderScope())
        using (var t = new Transaction(doc, "SV - Atualizar todas"))
        {
            t.Start();
            var service = new MarkingService(doc, uidoc.ActiveView);
            foreach (var d in defs) results.Add(service.Render(d));
            t.Commit();
        }
        Report("Atualização", results, alwaysShow: true);
        return Result.Succeeded;
    }
}

/// <summary>Converte marcas entre representação 3D e 2D.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdAlternar2D3D : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var picked = MarkingPicker.PickMany(uidoc, "Selecione as marcas a converter entre 3D e 2D e clique em Concluir");
        if (picked.Count == 0) return Result.Cancelled;
        var defs = picked.Select(p => p.Definition).ToList();
        var to2D = defs.Count(d => d.Output.Mode == OutputMode.Modelo3D) >= defs.Count / 2.0;
        if (to2D && !MarkingService.SupportsDetail(uidoc.ActiveView))
            throw new UserMessageException("Para converter em 2D, abra a vista em planta onde as regiões preenchidas devem ser criadas.");
        foreach (var d in defs)
        {
            d.Output.Mode = to2D ? OutputMode.Detalhe2D : OutputMode.Modelo3D;
            if (to2D) d.Output.ViewId = uidoc.ActiveView.UniqueId;
        }
        var results = MarkingCreator.Commit(uidoc, defs, to2D ? "SV - Converter para 2D" : "SV - Converter para 3D");
        Report(to2D ? "Conversão para 2D" : "Conversão para 3D", results);
        return Result.Succeeded;
    }
}

/// <summary>Seleciona todos os elementos da mesma marca ou do mesmo grupo.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdSelecionarConjunto : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var stored = MarkingPicker.PickOne(uidoc, "Selecione uma marca");
        if (stored == null) return Result.Cancelled;
        var all = MarkingStorage.All(uidoc.Document);
        var selection = all.Where(r => r.MarkingId == stored.MarkingId).ToList();
        if (!string.IsNullOrEmpty(stored.Definition.GroupId))
        {
            var td = new TaskDialog(AppTitle) { MainInstruction = "Selecionar o quê?" };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Somente esta marca");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Todo o grupo (ex.: todas as linhas da via ou da travessia)");
            if (td.Show() == TaskDialogResult.CommandLink2)
                selection = all.Where(r => r.Definition.GroupId == stored.Definition.GroupId).ToList();
        }
        uidoc.Selection.SetElementIds(selection.Select(r => r.Element.Id).ToList());
        return Result.Succeeded;
    }
}
