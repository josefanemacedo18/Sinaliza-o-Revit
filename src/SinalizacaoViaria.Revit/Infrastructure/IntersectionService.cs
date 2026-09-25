using Autodesk.Revit.DB;
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

    /// <summary>Vias com pavimento (seção registrada) e seus eixos.</summary>
    public List<IntersectionRoad> Roads()
    {
        var res = new List<IntersectionRoad>();
        foreach (var d in MarkingStorage.Definitions(_doc).OfType<RoadPavementDefinition>())
        {
            var axis = PathResolver.Resolve(_doc, d.Path)?.Main;
            if (axis != null && axis.Points.Count >= 2) res.Add(new IntersectionRoad(d, axis));
        }
        return res;
    }

    /// <summary>Cria (ou atualiza) as interseções que envolvem a via indicada.</summary>
    public List<RenderResult> AutoIntersect(RoadPavementDefinition road, IntersectionDefinition template)
    {
        var results = new List<RenderResult>();
        var roads = Roads();
        var idx = roads.FindIndex(r => r.Def.Id == road.Id);
        if (idx < 0) return results;
        var existing = MarkingStorage.Definitions(_doc).OfType<IntersectionDefinition>().ToList();
        var roundabouts = MarkingStorage.Definitions(_doc).OfType<RoundaboutDefinition>().ToList();
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(roads).Where(n => n.Roads.Contains(idx)))
        {
            if (roundabouts.Any(r => r.Center.DistanceTo(node) < r.OuterRadius + 5)) continue;   // o nó já é uma rotatória
            var roadIds = ids.Select(i => roads[i].Def.Id).ToList();
            var it = existing.FirstOrDefault(e => e.Node.DistanceTo(node) < IntersectionGenerator.NodeMergeDistance * 2);
            if (it == null)
            {
                it = (IntersectionDefinition)template.CloneWithNewId();
                it.Node = node;
                it.Z = roads[idx].Def.Path?.Z ?? 0;
                it.ChildIds.Clear();
            }
            foreach (var id in roadIds) if (!it.RoadIds.Contains(id)) it.RoadIds.Add(id);
            it.Output.Mode = road.Output.Mode;
            it.Output.ViewId = road.Output.ViewId;
            results.AddRange(Refresh(it));
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
        return all.OfType<RoundaboutDefinition>().Where(r => r.Legs.Any(l => l.RoadId != null && roadIds.Contains(l.RoadId))).ToList();
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
