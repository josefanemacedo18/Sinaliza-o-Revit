using Autodesk.Revit.DB;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Core.Definitions;

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
        // Bordas de pisos, lajes, topografia e paredes usadas como referência: a marca acompanha a edição do elemento.
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new LogicalOrFilter(new List<ElementFilter>
        {
            new ElementClassFilter(typeof(Floor)), new ElementClassFilter(typeof(Toposolid)),
            new ElementClassFilter(typeof(Wall)), new ElementClassFilter(typeof(RoofBase)),
        }), Element.GetChangeTypeGeometry());
        // Cópias de elementos de sinalização (copiar/colar) tornam-se marcas independentes.
        var markingClasses = new LogicalOrFilter(new List<ElementFilter>
        {
            new ElementClassFilter(typeof(DirectShape)), new ElementClassFilter(typeof(FilledRegion)),
            new ElementClassFilter(typeof(TextNote)), new ElementClassFilter(typeof(CurveElement)), new ElementClassFilter(typeof(Floor)),
        });
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), markingClasses, Element.GetChangeTypeElementAddition());
        // Escala da vista alterada: os detalhes dela (mm de papel × escala) são refeitos na nova escala.
        foreach (var bip in new[] { BuiltInParameter.VIEW_SCALE, BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC, BuiltInParameter.VIEW_SCALE_PULLDOWN_IMPERIAL })
            UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(View)), Element.GetChangeTypeParameter(new ElementId(bip)));
        // Placa/marca apagada: remove os detalhes e anotações que apontavam para ela.
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(DirectShape)), Element.GetChangeTypeElementDeletion());
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(FilledRegion)), Element.GetChangeTypeElementDeletion());
    }

    public static void Unregister(AddInId addInId)
    {
        var id = new UpdaterId(addInId, UpdaterGuid);
        if (UpdaterRegistry.IsUpdaterRegistered(id)) UpdaterRegistry.UnregisterUpdater(id);
    }

    /// <summary>
    /// Desliga o atualizador enquanto um comando faz a regeneração por conta própria (ex.: Mover via), para não refazer
    /// tudo de novo no fim da transação. Use com "using".
    /// </summary>
    public static IDisposable Pause() => new PauseScope();
    private static int _paused;

    private sealed class PauseScope : IDisposable
    {
        public PauseScope() => _paused++;
        public void Dispose() => _paused--;
    }

    public void Execute(UpdaterData data)
    {
        if (_paused > 0) return;
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
            if (data.GetDeletedElementIds().Count > 0 && !MarkingService.IsRendering)
            {
                RemoveOrphanDetails(doc);
                if (PluginContext.Settings.AutoUpdate) RefreshProjectTables(doc);
            }
        }
        catch (Exception ex)
        {
            Log.Error("RemoveOrphanDetails", ex);
        }

        if (!PluginContext.Settings.AutoUpdate) return;
        try
        {
            var modified = data.GetModifiedElementIds().Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
            var views = modified.OfType<View>().ToList();
            if (views.Count > 0)
            {
                Rescale(doc, views);
                modified.RemoveAll(e => e is View);
                if (modified.Count == 0) return;
            }
            // Pisos/paredes editados só interessam se alguma marca usa uma borda deles (evita varrer o projeto à toa).
            if (!modified.Any(e => e is CurveElement))
            {
                var owners = EdgeOwners(doc);
                if (!modified.Any(e => owners.Contains(e.UniqueId))) return;
            }
            var changed = new HashSet<string>(modified.Select(e => e.UniqueId));
            if (changed.Count == 0) return;

            var affected = MarkingStorage.Definitions(doc)
                .Where(d => d.Path?.ElementIds.Any(id => changed.Contains(Core.Definitions.PathReference.OwnerOf(id))) == true)
                .ToList();
            if (affected.Count == 0) return;
            Regenerate(doc, affected, PluginContext.Settings.AutoConnect && !MarkingService.IsRendering);
        }
        catch (Exception ex)
        {
            Log.Error("MarkingUpdater", ex);
        }
    }

    /// <summary>
    /// Regenera as marcas cujo eixo mudou (<paramref name="affected"/>) e tudo o que depende delas: ímã e vias ligadas
    /// (<paramref name="connect"/>), extensões de calçada e piso tátil, interseções (novas e existentes), rotatórias,
    /// balões e detalhes. <paramref name="beforeIntersections"/> roda depois das vias e antes das interseções (ex.: levar os
    /// nós para o novo cruzamento). Exige transação aberta.
    /// </summary>
    public static void Regenerate(Document doc, List<MarkingDefinition> affected, bool connect, Action<IntersectionService>? beforeIntersections = null)
    {
        try
        {
            var service = new MarkingService(doc, null, interactive: false);
            if (connect)
            {
                // Ímã de conexão (InfraWorks): ponta solta sobre outra via encaixa no eixo dela; vias ligadas a uma
                // via movida acompanham. Os eixos alterados entram na regeneração.
                try
                {
                    var conn = new IntersectionService(doc, service);
                    var groups = affected.Select(d => d.GroupId).Where(g => g != null).Cast<string>().ToHashSet();
                    var moved = conn.MagnetGroups(groups);
                    moved.UnionWith(conn.FollowConnections(groups));
                    if (moved.Count > 0)
                    {
                        var ids = affected.Select(d => d.Id).ToHashSet();
                        affected.AddRange(MarkingStorage.Definitions(doc).Where(d => d.GroupId != null && moved.Contains(d.GroupId) && !ids.Contains(d.Id)));
                        service.Invalidate();
                    }
                }
                catch (Exception ex) { Log.Error("Updater – ímã de conexão", ex); }
            }
            foreach (var def in affected)
            {
                try { service.Render(def); }
                catch (Exception ex) { Log.Error($"Updater {def.DisplayCode}", ex); }
            }
            var inter = new IntersectionService(doc, service);
            // Extensões de calçada, travessias no meio da quadra e piso tátil das vias cujo eixo mudou: refeitos no lugar.
            foreach (var pv in affected.OfType<RoadPavementDefinition>().ToList())
            {
                try { inter.RefreshRoadFeatures(pv); }
                catch (Exception ex) { Log.Error("Updater – extensões da via", ex); }
            }
            if (beforeIntersections != null)
            {
                service.Invalidate();
                beforeIntersections(inter);
            }
            var processed = new HashSet<string>();
            if (PluginContext.Settings.AutoIntersect)
            {
                // Eixo movido/editado: cruzamentos novos viram interseções automaticamente.
                try
                {
                    var template = IntersectionService.AutoTemplate();
                    inter.AutoIntersectGroups(affected.Select(d => d.GroupId ?? ""), template, out processed, radiusByHierarchy: true);
                    // Via puxada até uma rotatória: ela ganha o ramo.
                    var roads = inter.Roads();
                    foreach (var g in affected.Select(d => d.GroupId).Where(g => g != null).Distinct())
                        foreach (var r in roads.Where(r => r.Def.GroupId == g))
                            foreach (var rb in inter.RoundaboutsTouching(r)) inter.Refresh(rb);
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
            Log.Error("MarkingUpdater – regeneração", ex);
        }
    }

    /// <summary>
    /// Escala da vista mudou: cotas de seção, perfis, detalhes de placa, legendas, quadros, notas e norte da vista são refeitos
    /// com a nova escala – textos e símbolos mantêm o tamanho no papel, em volta do mesmo ponto de inserção.
    /// </summary>
    private static void Rescale(Document doc, IEnumerable<View> views)
    {
        // Escala trocada pelo próprio plugin (ao gerar uma prancha): a geração já usou a escala nova.
        if (MarkingService.IsRendering) return;
        var defs = MarkingStorage.Definitions(doc);
        foreach (var v in views)
        {
            var list = Core.Definitions.AnnotationScale.ToRescale(defs, v.UniqueId);
            if (list.Count == 0) continue;
            using var scope = MarkingService.RenderScope();
            var service = new MarkingService(doc, v, interactive: false);
            foreach (var d in list)
            {
                // Trecho de prancha: a escala mudada à mão na vista passa a ser a do trecho.
                if (d is Core.Definitions.SheetSegmentDefinition ss) ss.Scale = v.Scale;
                try { service.Render(d); }
                catch (Exception ex) { Log.Error($"Escala da vista – {d.DisplayCode}", ex); }
            }
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Document, HashSet<string>> Owners = new();

    /// <summary>Elementos (UniqueId) cujas arestas servem de caminho para alguma marca.</summary>
    private static HashSet<string> EdgeOwners(Document doc)
    {
        if (Owners.TryGetValue(doc, out var set)) return set;
        set = MarkingStorage.Definitions(doc).SelectMany(d => d.Path?.ElementIds ?? new List<string>())
            .Where(Core.Definitions.PathReference.IsEdge).Select(Core.Definitions.PathReference.OwnerOf).ToHashSet();
        Owners.AddOrUpdate(doc, set);
        return set;
    }

    /// <summary>Registra novas referências a arestas (marcas criadas por bordas de elementos).</summary>
    public static void NoteEdgeOwners(Document doc, IEnumerable<string> pathIds)
    {
        var set = EdgeOwners(doc);
        foreach (var id in pathIds.Where(Core.Definitions.PathReference.IsEdge)) set.Add(Core.Definitions.PathReference.OwnerOf(id));
    }

    private static void RemoveOrphanDetails(Document doc)
    {
        var all = MarkingStorage.All(doc);
        // Detalhes podem apontar para marcas ou para outros detalhes (perfil transversal → cota de seção).
        var ids = all.Select(r => r.MarkingId).ToHashSet();
        var orphans = all.Where(r => r.Definition is Core.Definitions.IAnnotationDefinition { TargetId: { } t } && !ids.Contains(t))
            .Select(r => r.Element.Id).ToList();
        foreach (var id in orphans)
            try { doc.Delete(id); } catch { /* já removido */ }
        // Recortes de uma marca apagada (boca de lobo, grelha, rampa...): o piso/meio-fio/calçada volta a ser contínuo.
        var stale = all.Select(r => r.Definition).GroupBy(d => d.Id).Select(g => g.First())
            .Where(d => d.Exclusions.Any(z => !string.IsNullOrEmpty(z.SourceId) && !z.SourceId!.Contains(':') && !ids.Contains(z.SourceId!)))
            .ToList();
        if (stale.Count == 0) return;
        var service = new MarkingService(doc, null, interactive: false);
        foreach (var d in stale)
        {
            d.Exclusions.RemoveAll(z => !string.IsNullOrEmpty(z.SourceId) && !z.SourceId!.Contains(':') && !ids.Contains(z.SourceId!));
            try { service.Render(d); }
            catch (Exception ex) { Log.Error($"Recortes órfãos {d.DisplayCode}", ex); }
        }
    }

    /// <summary>
    /// Marca apagada: legenda de placas e quadros (que leem o projeto inteiro) refeitos sem ela – na vista própria deles
    /// (Legenda/desenho) ou na planta onde foram desenhados.
    /// </summary>
    private static void RefreshProjectTables(Document doc)
    {
        if (!MarkingStorage.Definitions(doc).Any(d => d is Core.Definitions.IProjectWideAnnotation { TargetId: null })) return;
        try { new MarkingService(doc, null, interactive: false).RenderDependents(Array.Empty<string>(), includeLegends: true); }
        catch (Exception ex) { Log.Error("Quadros após apagar marcas", ex); }
    }

    public UpdaterId GetUpdaterId() => _id;
    public ChangePriority GetChangePriority() => ChangePriority.FreeStandingComponents;
    public string GetUpdaterName() => "Sinalização Viária – atualização automática";
    public string GetAdditionalInformation() => "Regenera a sinalização horizontal quando as linhas de referência são alteradas.";
}
