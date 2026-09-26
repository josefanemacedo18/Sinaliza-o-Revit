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
    public static Result CreateAlongPath(UIDocument uidoc, MarkingDefinition template, PathMode mode, string action, bool closed = false,
        Action<MarkingDefinition>? afterEach = null)
    {
        var results = new List<RenderResult>();
        var doc = uidoc.Document;
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
                if (!ResolveSide(uidoc, def)) return Result.Cancelled;
                results.AddRange(Commit(uidoc, new[] { def }, $"SV - {action}"));
                afterEach?.Invoke(def);
                break;
            }
            case PathMode.Bordas:
            {
                var edges = Picking.PickEdges(uidoc, closed
                    ? "Selecione as BORDAS (arestas) que formam o contorno fechado – pisos, calçadas, lajes, topografia – e clique em Concluir"
                    : "Selecione as BORDAS (arestas) de pisos, calçadas, lajes ou topografia que formam o caminho e clique em Concluir");
                if (edges == null) return Result.Cancelled;
                var def = template.CloneWithNewId();
                var pr = PathReference.FromElements(edges.Select(e => e.Id));
                pr.Cache = edges.Select(e => e.Points).ToList();
                pr.Z = edges.Average(e => e.Z);
                SetPath(def, pr);
                Updater.MarkingUpdater.NoteEdgeOwners(doc, pr.ElementIds);
                if (!ResolveSide(uidoc, def)) return Result.Cancelled;
                results.AddRange(Commit(uidoc, new[] { def }, $"SV - {action}"));
                afterEach?.Invoke(def);
                break;
            }
            case PathMode.Desenhar:
            {
                var pl = Picking.PickPolyline(uidoc, action, closed, keepAsModelLines: true);
                if (pl == null) return Result.Cancelled;
                var uids = pl.Value.Lines.Select(id => doc.GetElement(id).UniqueId);
                var def = template.CloneWithNewId();
                SetPath(def, PathReference.FromElements(uids));
                if (!ResolveSide(uidoc, def)) return Result.Cancelled;
                results.AddRange(Commit(uidoc, new[] { def }, $"SV - {action}"));
                afterEach?.Invoke(def);
                break;
            }
            case PathMode.DoisPontos:
            {
                while (true)
                {
                    var a = Picking.PickPoint(uidoc, $"{action}: clique o 1º ponto – ESC encerra");
                    if (a == null) break;
                    var b = Picking.PickPoint(uidoc, $"{action}: clique o 2º ponto");
                    if (b == null) break;
                    var (pts, z) = Picking.ToCore(new[] { a, b });
                    var def = template.CloneWithNewId();
                    SetPath(def, PathReference.FromPoints(pts, z));
                    if (!ResolveSide(uidoc, def)) break;
                    results.AddRange(Commit(uidoc, new[] { def }, $"SV - {action}"));
                    afterEach?.Invoke(def);
                }
                if (results.Count == 0) return Result.Cancelled;
                break;
            }
            case PathMode.BordoDaVia:
            {
                // Montagem passo a passo: cada clique ao lado de uma via acrescenta o elemento junto ao bordo dela.
                var any = false;
                while (true)
                {
                    var p = Picking.PickPoint(uidoc, $"{action}: clique AO LADO da via, no lado em que o elemento será colocado – ESC encerra");
                    if (p == null) break;
                    var pt = UnitConv.ToVec2(p);
                    var r = IntersectionRunner.Run(uidoc, $"SV - {action}", s => s.AddAtRoadEdge(template, pt));
                    if (r.Count == 0)
                    {
                        TaskDialog.Show(CommandBase.AppTitle, "Nenhuma via (Sinalizar via / Pista) perto do ponto clicado.");
                        continue;
                    }
                    any = true;
                    results.AddRange(r.Where(x => x.Warnings.Count > 0));
                }
                if (!any) return Result.Cancelled;
                break;
            }
        }
        CommandBase_Report(action, results);
        return Result.Succeeded;
    }

    /// <summary>
    /// "Borda na linha – indicar o lado": pede um clique ao lado do caminho e grava o lado (esquerda/direita da linha).
    /// Falso se o usuário cancelar.
    /// </summary>
    public static bool ResolveSide(UIDocument uidoc, MarkingDefinition def)
    {
        if (def.Justify != Justificacao.Clique) return true;
        var side = PickSide(uidoc, def.Path);
        if (side == null) return false;
        def.Justify = side.Value;
        return true;
    }

    /// <summary>Lado (esquerda/direita da linha) indicado com um clique. Nulo = cancelado.</summary>
    public static Justificacao? PickSide(UIDocument uidoc, PathReference? pathRef)
    {
        var path = PathResolver.Resolve(uidoc.Document, pathRef)?.Main;
        if (path == null || path.Points.Count < 2) return Justificacao.Centro;
        var p = Picking.PickPoint(uidoc, "Clique AO LADO da linha, no lado em que o elemento deve ficar (a borda fica sobre a linha)");
        if (p == null) return null;
        return path.SignedDistance(UnitConv.ToVec2(p)) >= 0 ? Justificacao.Esquerda : Justificacao.Direita;
    }

    /// <summary>Caminho por linhas, bordas de elementos ou desenho (para comandos que montam várias marcas). Nulo = cancelado.</summary>
    public static PathReference? PickPath(UIDocument uidoc, PathMode mode, string what, bool closed = false)
    {
        var doc = uidoc.Document;
        switch (mode)
        {
            case PathMode.Desenhar:
            {
                var pl = Picking.PickPolyline(uidoc, what, closed, keepAsModelLines: true);
                return pl == null ? null : PathReference.FromElements(pl.Value.Lines.Select(id => doc.GetElement(id).UniqueId));
            }
            case PathMode.Bordas:
            {
                var edges = Picking.PickEdges(uidoc, $"{what}: selecione as BORDAS (arestas) de pisos, calçadas, lajes ou topografia e clique em Concluir");
                if (edges == null) return null;
                var pr = PathReference.FromElements(edges.Select(e => e.Id));
                pr.Cache = edges.Select(e => e.Points).ToList();
                pr.Z = edges.Average(e => e.Z);
                Updater.MarkingUpdater.NoteEdgeOwners(doc, pr.ElementIds);
                return pr;
            }
            default:
            {
                var curves = Picking.PickCurves(uidoc, $"{what}: selecione as linhas e clique em Concluir");
                return curves == null ? null : PathReference.FromElements(curves.Select(c => c.UniqueId));
            }
        }
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
