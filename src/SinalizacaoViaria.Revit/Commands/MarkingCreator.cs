using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Fluxos de criação compartilhados: obtenção de caminho, transação e geração.</summary>
public static class MarkingCreator
{
    /// <summary>Gera (em uma transação) todas as definições e devolve os resultados.</summary>
    public static List<RenderResult> Commit(UIDocument uidoc, IEnumerable<MarkingDefinition> defs, string transactionName)
    {
        var doc = uidoc.Document;
        var results = new List<RenderResult>();
        using var scope = MarkingService.RenderScope();
        using var t = new Transaction(doc, transactionName);
        t.Start();
        var service = new MarkingService(doc, uidoc.ActiveView);
        var ordered = MarkingService.DependencyOrder(defs);
        foreach (var d in ordered)
        {
            try
            {
                results.Add(service.Render(d));
            }
            catch (Exception ex)
            {
                Log.Error($"Render {d.DisplayCode}", ex);
                var failed = new RenderResult();
                failed.Warnings.Add($"{d.DisplayCode}: falha ao gerar – {ex.Message}");
                results.Add(failed);
            }
        }
        // Detalhes de placas e anotações acompanham as marcas editadas; legendas acompanham tipos novos.
        try
        {
            var targets = ordered.Where(d => d is not IAnnotationDefinition).Select(d => d.Id).ToList();
            if (targets.Count > 0) results.AddRange(service.RenderDependents(targets, includeLegends: true).Where(r => r.Warnings.Count > 0));
        }
        catch (Exception ex)
        {
            Log.Error("RenderDependents", ex);
        }
        t.Commit();
        var ids = results.SelectMany(r => r.Elements).Where(id => doc.GetElement(id) != null).ToList();
        if (ids.Count > 0) uidoc.Selection.SetElementIds(ids);
        return results;
    }

    public static void SetPath(MarkingDefinition def, PathReference path) => def.SetPath(path);

    /// <summary>Obtém o caminho conforme o modo escolhido e gera uma (ou várias) marcas a partir do modelo.</summary>
    public static Result CreateAlongPath(UIDocument uidoc, MarkingDefinition template, PathMode mode, string action, bool closed = false)
    {
        var results = new List<RenderResult>();
        switch (mode)
        {
            case PathMode.Linhas:
            {
                var curves = Picking.PickCurves(uidoc, closed
                    ? "Selecione as linhas que formam o contorno fechado e clique em Concluir"
                    : "Selecione as linhas do caminho (em sequência) e clique em Concluir");
                if (curves == null) return Result.Cancelled;
                var def = template.CloneWithNewId();
                SetPath(def, PathReference.FromElements(curves.Select(c => c.UniqueId)));
                results.AddRange(Commit(uidoc, new[] { def }, $"SV - {action}"));
                break;
            }
            case PathMode.Desenhar:
            {
                var pl = Picking.PickPolyline(uidoc, action, closed, keepAsModelLines: true);
                if (pl == null) return Result.Cancelled;
                var uids = pl.Value.Lines.Select(id => uidoc.Document.GetElement(id).UniqueId);
                var def = template.CloneWithNewId();
                SetPath(def, PathReference.FromElements(uids));
                results.AddRange(Commit(uidoc, new[] { def }, $"SV - {action}"));
                break;
            }
            case PathMode.DoisPontos:
            {
                while (true)
                {
                    var a = Picking.PickPoint(uidoc, $"{action}: clique o 1º ponto (bordo da pista) – ESC encerra");
                    if (a == null) break;
                    var b = Picking.PickPoint(uidoc, $"{action}: clique o 2º ponto");
                    if (b == null) break;
                    var (pts, z) = Picking.ToCore(new[] { a, b });
                    var def = template.CloneWithNewId();
                    SetPath(def, PathReference.FromPoints(pts, z));
                    results.AddRange(Commit(uidoc, new[] { def }, $"SV - {action}"));
                }
                if (results.Count == 0) return Result.Cancelled;
                break;
            }
        }
        CommandBase_Report(action, results);
        return Result.Succeeded;
    }

    /// <summary>Permite escolher as superfícies de projeção (Toposolid/pisos).</summary>
    public static List<string>? PickSurfaces(UIDocument uidoc)
    {
        try
        {
            var refs = uidoc.Selection.PickObjects(ObjectType.Element, new SurfaceSelectionFilter(),
                "Selecione as superfícies (Toposolid, pisos, topografia) sobre as quais a sinalização será projetada");
            return refs.Select(r => uidoc.Document.GetElement(r).UniqueId).ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }

    private static void CommandBase_Report(string action, List<RenderResult> results)
    {
        var warnings = results.SelectMany(r => r.Warnings).Distinct().ToList();
        if (warnings.Count == 0) return;
        TaskDialog.Show(CommandBase.AppTitle, $"{action} – avisos:\n\n" + string.Join("\n", warnings.Take(20).Select(w => "• " + w)));
    }
}
