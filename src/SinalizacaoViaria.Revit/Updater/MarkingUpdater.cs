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
        var markingClasses = new LogicalOrFilter(new ElementClassFilter(typeof(DirectShape)), new ElementClassFilter(typeof(FilledRegion)));
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), markingClasses, Element.GetChangeTypeElementAddition());
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
        }
        catch (Exception ex)
        {
            Log.Error("MarkingUpdater", ex);
        }
    }

    public UpdaterId GetUpdaterId() => _id;
    public ChangePriority GetChangePriority() => ChangePriority.FreeStandingComponents;
    public string GetUpdaterName() => "Sinalização Viária – atualização automática";
    public string GetAdditionalInformation() => "Regenera a sinalização horizontal quando as linhas de referência são alteradas.";
}
