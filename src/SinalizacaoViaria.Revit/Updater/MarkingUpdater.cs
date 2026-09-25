using Autodesk.Revit.DB;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Updater;

/// <summary>
/// Atualizador dinâmico (DMU): quando uma linha de referência é movida ou editada, todas as marcas
/// associadas a ela são regeneradas na mesma transação – a sinalização "acompanha" o eixo.
/// </summary>
public sealed class MarkingUpdater : IUpdater
{
    public static readonly Guid UpdaterGuid = new("2B8F3E61-7D4C-4A9B-8E12-6F5A3C0D9B72");
    private readonly UpdaterId _id;

    public MarkingUpdater(AddInId addInId) => _id = new UpdaterId(addInId, UpdaterGuid);

    public static void Register(AddInId addInId)
    {
        var updater = new MarkingUpdater(addInId);
        if (UpdaterRegistry.IsUpdaterRegistered(updater.GetUpdaterId())) return;
        UpdaterRegistry.RegisterUpdater(updater, true);
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(CurveElement)), Element.GetChangeTypeGeometry());
        // Cópias de elementos de sinalização (copiar/colar) tornam-se marcas independentes.
        var markingClasses = new LogicalOrFilter(new List<ElementFilter>
        {
            new ElementClassFilter(typeof(DirectShape)), new ElementClassFilter(typeof(FilledRegion)),
            new ElementClassFilter(typeof(TextNote)), new ElementClassFilter(typeof(CurveElement)),
        });
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), markingClasses, Element.GetChangeTypeElementAddition());
        // Placa/marca apagada: remove os detalhes e anotações que apontavam para ela.
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(DirectShape)), Element.GetChangeTypeElementDeletion());
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(FilledRegion)), Element.GetChangeTypeElementDeletion());
    }

    public static void Unregister(AddInId addInId)
    {
        var id = new UpdaterId(addInId, UpdaterGuid);
        if (UpdaterRegistry.IsUpdaterRegistered(id)) UpdaterRegistry.UnregisterUpdater(id);
    }

    public void Execute(UpdaterData data)
    {
        var doc = data.GetDocument();
        try
        {
            var added = data.GetAddedElementIds();
            if (added.Count > 0 && !MarkingService.IsRendering) CopyHandler.Handle(doc, added);
        }
        catch (Exception ex)
        {
            Log.Error("CopyHandler", ex);
        }

        try
        {
            if (data.GetDeletedElementIds().Count > 0 && !MarkingService.IsRendering) RemoveOrphanDetails(doc);
        }
        catch (Exception ex)
        {
            Log.Error("RemoveOrphanDetails", ex);
        }

        if (!PluginContext.Settings.AutoUpdate) return;
        try
        {
            var changed = new HashSet<string>(data.GetModifiedElementIds()
                .Select(id => doc.GetElement(id)?.UniqueId)
                .Where(u => u != null)!
                .Cast<string>());
            if (changed.Count == 0) return;

            var affected = MarkingStorage.Definitions(doc)
                .Where(d => d.Path?.ElementIds.Any(changed.Contains) == true)
                .ToList();
            if (affected.Count == 0) return;

            var service = new MarkingService(doc, null, interactive: false);
            foreach (var def in affected)
            {
                try { service.Render(def); }
                catch (Exception ex) { Log.Error($"Updater {def.DisplayCode}", ex); }
            }
            var inter = new IntersectionService(doc, service);
            var processed = new HashSet<string>();
            if (PluginContext.Settings.AutoIntersect)
            {
                // Eixo movido/editado: cruzamentos novos viram interseções automaticamente.
                try
                {
                    var template = UI.UiHelpers.Remembered<Core.Definitions.IntersectionDefinition>("Intersecao") ?? new Core.Definitions.IntersectionDefinition();
                    inter.AutoIntersectGroups(affected.Select(d => d.GroupId ?? ""), template, out processed);
                }
                catch (Exception ex) { Log.Error("Updater – interseções automáticas", ex); }
            }
            foreach (var it in inter.DependentOn(affected).Where(i => !processed.Contains(i.Id)))
            {
                try { inter.Refresh(it); }
                catch (Exception ex) { Log.Error("Updater interseção", ex); }
            }
            foreach (var rb in inter.RoundaboutsDependentOn(affected))
            {
                try { inter.Refresh(rb); }
                catch (Exception ex) { Log.Error("Updater rotatória", ex); }
            }
            foreach (var cds in inter.CulDeSacsDependentOn(affected))
            {
                try { inter.Refresh(cds); }
                catch (Exception ex) { Log.Error("Updater cul-de-sac", ex); }
            }
            service.RenderDependents(affected.Select(d => d.Id), includeLegends: true);
        }
        catch (Exception ex)
        {
            Log.Error("MarkingUpdater", ex);
        }
    }

    private static void RemoveOrphanDetails(Document doc)
    {
        var all = MarkingStorage.All(doc);
        var ids = all.Where(r => r.Definition is not Core.Definitions.IAnnotationDefinition).Select(r => r.MarkingId).ToHashSet();
        var orphans = all.Where(r => r.Definition is Core.Definitions.IAnnotationDefinition { TargetId: { } t } && !ids.Contains(t))
            .Select(r => r.Element.Id).ToList();
        foreach (var id in orphans)
            try { doc.Delete(id); } catch { /* já removido */ }
    }

    public UpdaterId GetUpdaterId() => _id;
    public ChangePriority GetChangePriority() => ChangePriority.FreeStandingComponents;
    public string GetUpdaterName() => "Sinalização Viária – atualização automática";
    public string GetAdditionalInformation() => "Regenera a sinalização horizontal quando as linhas de referência são alteradas.";
}
