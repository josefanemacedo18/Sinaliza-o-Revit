using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Mantém interseções e rotatórias coerentes com as vias: recalcula a geometria, aplica os recortes nas marcas de cada
/// via, recria travessias/rampas e regenera tudo. Todos os métodos exigem uma transação aberta.
/// </summary>
public sealed class IntersectionService
{
    private readonly Document _doc;
    private readonly MarkingService _service;

    public IntersectionService(Document doc, MarkingService service)
    {
        _doc = doc;
        _service = service;
    }

    /// <summary>
    /// Vias do projeto e seus eixos. Vias sem pavimento registrado (criadas em versões anteriores ou com pavimento
    /// "Nenhum") têm a seção reconstruída a partir das suas marcas; com <paramref name="createMissing"/> o pavimento
    /// é criado e gravado (exige transação aberta).
    /// </summary>
    public List<IntersectionRoad> Roads(bool createMissing = false)
    {
        var res = new List<IntersectionRoad>();
        var all = MarkingStorage.Definitions(_doc);
        var pavs = all.OfType<RoadPavementDefinition>().ToList();
        var withPavement = pavs.Select(p => p.GroupId).Where(g => g != null).ToHashSet();
        foreach (var g in all.Where(d => d.GroupId != null && !withPavement.Contains(d.GroupId)).GroupBy(d => d.GroupId!))
        {
            var inferred = RoadSectionInference.Infer(g.ToList(), PluginContext.Catalog);
            if (inferred == null) continue;
            if (createMissing)
            {
                try { _service.Render(inferred); }
                catch (Exception ex) { Log.Error("Pavimento de via existente", ex); continue; }
            }
            pavs.Add(inferred);
        }
        foreach (var d in pavs)
        {
            var axis = PathResolver.Resolve(_doc, d.Path)?.Main;
            if (axis != null && axis.Points.Count >= 2) res.Add(new IntersectionRoad(d, axis));
        }
        if (createMissing) _service.Invalidate();
        return res;
    }

    /// <summary>Cria/atualiza todas as interseções do projeto (ou só a mais próxima de <paramref name="near"/>).</summary>
    public List<RenderResult> IntersectAll(IntersectionDefinition template, Vec2? near = null, double maxDistance = 60)
    {
        var results = new List<RenderResult>();
        var roads = Roads(createMissing: true);
        var nodes = IntersectionGenerator.FindNodes(roads);
        if (near is { } p)
        {
            var best = nodes.Where(n => n.Node.DistanceTo(p) <= maxDistance).OrderBy(n => n.Node.DistanceTo(p)).Take(1).ToList();
            nodes = best;
        }
        var existing = MarkingStorage.Definitions(_doc).OfType<IntersectionDefinition>().ToList();
        var roundabouts = MarkingStorage.Definitions(_doc).OfType<RoundaboutDefinition>().ToList();
        foreach (var (node, ids) in nodes)
        {
            if (roundabouts.Any(r => r.Center.DistanceTo(node) < r.OuterRadius + 5)) continue;
            var it = existing.FirstOrDefault(e => e.Node.DistanceTo(node) < IntersectionGenerator.NodeMergeDistance * 2);
            if (it == null)
            {
                it = (IntersectionDefinition)template.CloneWithNewId();
                it.ChildIds.Clear();
                it.RoadIds.Clear();
                it.Node = node;
                it.Z = roads[ids[0]].Def.Path?.Z ?? 0;
                it.Output = roads[ids[0]].Def.Output.Clone();
            }
            else if (near != null)
            {
                // Ajustes pedidos na janela valem para a interseção existente.
                it.CornerRadius = template.CornerRadius;
                it.Crosswalks = template.Crosswalks;
                it.CrosswalkWidth = template.CrosswalkWidth;
                it.CrosswalkSetback = template.CrosswalkSetback;
                it.StopLines = template.StopLines;
                it.Ramps = template.Ramps;
            }
            foreach (var i in ids) if (!it.RoadIds.Contains(roads[i].Def.Id)) it.RoadIds.Add(roads[i].Def.Id);
            results.AddRange(Refresh(it));
        }
        return results;
    }

    /// <summary>Cria (ou atualiza) as interseções que envolvem a via indicada.</summary>
    public List<RenderResult> AutoIntersect(RoadPavementDefinition road, IntersectionDefinition template) =>
        AutoIntersectGroups(new[] { road.GroupId ?? "" }, template, out _);

    /// <summary>Cria/atualiza as interseções das vias (grupos) indicadas com as demais vias do projeto.</summary>
    public List<RenderResult> AutoIntersectGroups(IEnumerable<string> groupIds, IntersectionDefinition template, out HashSet<string> processed)
    {
        processed = new HashSet<string>();
        var results = new List<RenderResult>();
        var set = groupIds.Where(g => !string.IsNullOrEmpty(g)).ToHashSet();
        if (set.Count == 0) return results;
        var roads = Roads(createMissing: true);
        var idx = roads.Select((r, i) => (r, i)).Where(x => x.r.Def.GroupId != null && set.Contains(x.r.Def.GroupId)).Select(x => x.i).ToHashSet();
        if (idx.Count == 0) return results;
        var existing = MarkingStorage.Definitions(_doc).OfType<IntersectionDefinition>().ToList();
        var roundabouts = MarkingStorage.Definitions(_doc).OfType<RoundaboutDefinition>().ToList();
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(roads).Where(n => n.Roads.Any(idx.Contains)))
        {
            if (roundabouts.Any(r => r.Center.DistanceTo(node) < r.OuterRadius + 5)) continue;   // o nó já é uma rotatória
            var it = existing.FirstOrDefault(e => e.Node.DistanceTo(node) < IntersectionGenerator.NodeMergeDistance * 4);
            if (it == null)
            {
                it = (IntersectionDefinition)template.CloneWithNewId();
                it.Node = node;
                it.Z = roads[ids[0]].Def.Path?.Z ?? 0;
                it.ChildIds.Clear();
                it.RoadIds.Clear();
                it.Output = roads[ids[0]].Def.Output.Clone();
            }
            else it.Node = node;
            foreach (var i in ids) if (!it.RoadIds.Contains(roads[i].Def.Id)) it.RoadIds.Add(roads[i].Def.Id);
            results.AddRange(Refresh(it));
            processed.Add(it.Id);
        }
        return results;
    }

    /// <summary>Recalcula uma interseção: geometria, recortes das vias e travessias.</summary>
    public List<RenderResult> Refresh(IntersectionDefinition it)
    {
        var results = new List<RenderResult>();
        var all = MarkingStorage.Definitions(_doc);
        var roads = new List<IntersectionRoad>();
        foreach (var id in it.RoadIds)
        {
            if (all.FirstOrDefault(d => d.Id == id) is not RoadPavementDefinition pv) continue;
            var axis = PathResolver.Resolve(_doc, pv.Path)?.Main;
            if (axis != null && axis.Points.Count >= 2) roads.Add(new IntersectionRoad(pv, axis));
        }
        it.RoadIds = roads.Select(r => r.Def.Id).ToList();

        // As vias ainda se cruzam aqui? (eixos movidos): acompanha o nó ou remove a interseção.
        var node = roads.Count >= 2 ? IntersectionGenerator.FindNodes(roads).OrderBy(n => n.Node.DistanceTo(it.Node)).FirstOrDefault() : default;
        if (roads.Count < 2 || node.Roads == null || node.Node.DistanceTo(it.Node) > 15)
        {
            Remove(it);
            return results;
        }
        it.Node = node.Node;
        var layout = IntersectionGenerator.Layout(it, roads);

        // 1. A interseção (precisa existir antes dos recortes, que apontam para ela).
        results.Add(_service.Render(it));

        // 2. Travessias e rampas (recriadas).
        foreach (var old in it.ChildIds) _service.Delete(old);
        var z = it.Z;
        var children = layout.Pavement.Count > 0 ? IntersectionGenerator.Children(it, layout, it.Output, z) : new List<MarkingDefinition>();
        foreach (var c in children) results.Add(_service.Render(c));
        it.ChildIds = children.Select(c => c.Id).ToList();
        results.Add(_service.Render(it));   // grava a lista de filhos

        // 3. Recortes nas marcas de cada via (e rampas nas calçadas).
        var ramps = children.OfType<RampDefinition>()
            .Select(r => (r.Id, Fp: RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points)))).ToList();
        _service.Invalidate();
        for (int k = 0; k < roads.Count; k++)
        {
            var gid = roads[k].Def.GroupId;
            if (gid == null) continue;
            foreach (var m in all.Where(d => d.GroupId == gid))
            {
                m.Exclusions.RemoveAll(e => e.SourceId == it.Id || (e.SourceId != null && e.SourceId.StartsWith(it.Id + ":")));
                foreach (var cut in IntersectionGenerator.CutsFor(m, k, layout))
                    m.Exclusions.Add(new ExclusionZone { SourceId = it.Id, Points = cut.Outer.ToList() });
                if (IntersectionGenerator.IsPhysical(m))
                    foreach (var (rid, fp) in ramps)
                        m.Exclusions.Add(new ExclusionZone { SourceId = rid, Points = fp.Outer.ToList() });
                try { results.Add(_service.Render(m)); }
                catch (Exception ex) { Log.Error($"Interseção – via {m.DisplayCode}", ex); }
            }
        }
        return results;
    }

    /// <summary>Recalcula uma rotatória: geometria, sinalização (filhos) e recorte das vias ligadas aos ramos.</summary>
    public List<RenderResult> Refresh(RoundaboutDefinition rb)
    {
        var results = new List<RenderResult>();
        var layout = RoundaboutGenerator.Layout(rb);
        results.Add(_service.Render(rb));
        foreach (var old in rb.ChildIds) _service.Delete(old);
        var children = RoundaboutGenerator.Children(rb, layout, rb.Output, rb.Z);
        foreach (var c in children) results.Add(_service.Render(c));
        rb.ChildIds = children.Select(c => c.Id).ToList();
        results.Add(_service.Render(rb));

        _service.Invalidate();
        var all = MarkingStorage.Definitions(_doc);
        var roadIds = rb.Legs.Select(l => l.RoadId).Where(i => i != null).ToHashSet();
        var groups = all.OfType<RoadPavementDefinition>().Where(p => roadIds.Contains(p.Id)).Select(p => p.GroupId).Where(g => g != null).ToHashSet();
        foreach (var g in rb.Legs.Select(l => l.GroupId)) if (g != null) groups.Add(g);
        foreach (var m in all.Where(d => d.GroupId != null && groups.Contains(d.GroupId) || d.Exclusions.Any(e => e.SourceId == rb.Id)))
        {
            if (m.Id == rb.Id || children.Any(c => c.Id == m.Id)) continue;
            m.Exclusions.RemoveAll(e => e.SourceId == rb.Id);
            if (m.GroupId != null && groups.Contains(m.GroupId))
                m.Exclusions.Add(new ExclusionZone { SourceId = rb.Id, Points = layout.Zone.Outer.ToList() });
            try { results.Add(_service.Render(m)); }
            catch (Exception ex) { Log.Error($"Rotatória – via {m.DisplayCode}", ex); }
        }
        return results;
    }

    public List<RoundaboutDefinition> RoundaboutsDependentOn(IEnumerable<MarkingDefinition> changed)
    {
        var groups = changed.Select(d => d.GroupId).Where(g => g != null).ToHashSet();
        var all = MarkingStorage.Definitions(_doc);
        var roadIds = all.OfType<RoadPavementDefinition>().Where(p => groups.Contains(p.GroupId)).Select(p => p.Id).ToHashSet();
        return all.OfType<RoundaboutDefinition>().Where(r => r.Legs.Any(l => l.RoadId != null && roadIds.Contains(l.RoadId)
            || l.GroupId != null && groups.Contains(l.GroupId))).ToList();
    }

    /// <summary>Apaga a interseção, suas travessias/rampas e os recortes que ela fazia nas vias.</summary>
    public void Remove(IntersectionDefinition it)
    {
        foreach (var c in it.ChildIds) _service.Delete(c);
        _service.Delete(it.Id);
        _service.Invalidate();
        foreach (var m in MarkingStorage.Definitions(_doc).Where(d => d.Exclusions.Any(e => e.SourceId == it.Id)))
        {
            m.Exclusions.RemoveAll(e => e.SourceId == it.Id);
            try { _service.Render(m); }
            catch (Exception ex) { Log.Error("Remover interseção", ex); }
        }
    }

    /// <summary>Interseções que dependem das marcas (grupos de via) indicadas.</summary>
    public List<IntersectionDefinition> DependentOn(IEnumerable<MarkingDefinition> changed)
    {
        var groups = changed.Select(d => d.GroupId).Where(g => g != null).ToHashSet();
        var all = MarkingStorage.Definitions(_doc);
        var roadIds = all.OfType<RoadPavementDefinition>().Where(p => groups.Contains(p.GroupId)).Select(p => p.Id).ToHashSet();
        return all.OfType<IntersectionDefinition>().Where(i => i.RoadIds.Any(roadIds.Contains)).ToList();
    }
}
