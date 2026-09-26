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
    /// Interseção das conexões automáticas (ímã, criação de vias): sempre simples – esquinas com raio pela hierarquia,
    /// PARE na via secundária e linhas da principal contínuas; sem ilhas, bolsões ou alargamentos (esses só pela
    /// ferramenta Conexões, na interseção escolhida).
    /// </summary>
    public static IntersectionDefinition AutoTemplate(bool? crosswalks = null)
    {
        var cw = crosswalks ?? PluginContext.Settings.AutoCrosswalks;
        return new IntersectionDefinition
        {
            Control = ControleIntersecao.Pare,
            StopLines = true,
            Signs = true,
            Crosswalks = cw,
            Ramps = cw,
            Output = PluginContext.Settings.NewOutput(),
        };
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
            if (!IntersectionGenerator.NeedsIntersection(ids.Select(i => roads[i]).ToList(), node)) continue;
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
            else
            {
                // Ajustes pedidos na janela valem também para as interseções existentes.
                it.CornerRadius = template.CornerRadius;
                it.Crosswalks = template.Crosswalks;
                it.CrosswalkWidth = template.CrosswalkWidth;
                it.CrosswalkSetback = template.CrosswalkSetback;
                it.StopLines = template.StopLines;
                it.Ramps = template.Ramps;
                it.Control = template.Control;
                it.Signs = template.Signs;
                it.SplitterIslands = template.SplitterIslands;
                it.SplitterLength = template.SplitterLength;
                it.SplitterWidth = template.SplitterWidth;
                it.RightTurnIslands = template.RightTurnIslands;
                it.RightTurnCorners = template.RightTurnCorners;
                it.RightTurnRadius = template.RightTurnRadius;
                it.RightTurnLaneWidth = template.RightTurnLaneWidth;
                it.LeftTurnPockets = template.LeftTurnPockets;
                it.PocketLength = template.PocketLength;
                it.PocketTaper = template.PocketTaper;
                it.PocketWidth = template.PocketWidth;
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
    public List<RenderResult> AutoIntersectGroups(IEnumerable<string> groupIds, IntersectionDefinition template, out HashSet<string> processed,
        bool radiusByHierarchy = false)
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
            if (!IntersectionGenerator.NeedsIntersection(ids.Select(i => roads[i]).ToList(), node)) continue;
            var it = existing.FirstOrDefault(e => e.Node.DistanceTo(node) < IntersectionGenerator.NodeMergeDistance * 4);
            if (it == null)
            {
                it = (IntersectionDefinition)template.CloneWithNewId();
                it.Node = node;
                it.Z = roads[ids[0]].Def.Path?.Z ?? 0;
                it.ChildIds.Clear();
                it.RoadIds.Clear();
                it.MainRoadId = null;
                it.Output = roads[ids[0]].Def.Output.Clone();
                if (radiusByHierarchy) it.CornerRadius = Hierarquia.NodeRadius(ids.Select(i => roads[i].Def));
            }
            else
            {
                it.Node = node;
                // Raio informado numa das vias (criação/edição da via): vale também para a interseção existente.
                if (radiusByHierarchy && ids.Any(i => roads[i].Def.CornerRadius != null))
                    it.CornerRadius = Hierarquia.NodeRadius(ids.Select(i => roads[i].Def));
            }
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
        if (layout.Roads.Count > 0) it.Hierarchy = layout.Roads[layout.Main].Def.Hierarchy;

        // 1. A interseção (precisa existir antes dos recortes, que apontam para ela).
        results.Add(_service.Render(it));

        // 2. Travessias e rampas (recriadas).
        var oldIds = it.ChildIds.ToHashSet();
        foreach (var old in it.ChildIds) _service.Delete(old);
        var z = it.Z;
        var members = roads.Select(r => (IReadOnlyCollection<MarkingDefinition>)all.Where(d => d.GroupId != null && d.GroupId == r.Def.GroupId).ToList()).ToList();
        var children = layout.Pavement.Count > 0 ? IntersectionGenerator.Children(it, layout, it.Output, z, members) : new List<MarkingDefinition>();
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
                m.Exclusions.RemoveAll(e => e.SourceId == it.Id || (e.SourceId != null && (e.SourceId.StartsWith(it.Id + ":") || oldIds.Contains(e.SourceId))));
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
        // Rotatória ligada às vias: acompanha o nó (eixos movidos) e ganha/perde ramos conforme as vias que chegam.
        if (rb.Legs.Any(l => l.GroupId != null || l.RoadId != null))
        {
            var roads = Roads();
            var near = roads.Where(r => IntersectionGenerator.Project(r.Axis, rb.Center).Distance <= Math.Max(r.Def.TotalLeft, r.Def.TotalRight) + rb.OuterRadius).ToList();
            var node = IntersectionGenerator.FindNodes(near).OrderBy(n => n.Node.DistanceTo(rb.Center)).FirstOrDefault();
            if (node.Roads != null && node.Node.DistanceTo(rb.Center) < 20) rb.Center = node.Node;
            var legs = RoundaboutGenerator.LegsFromRoads(rb.Center, roads, rb.OuterRadius + 25);
            if (legs.Count >= 2) rb.Legs = legs;
            var hs = legs.Select(l => roads.FirstOrDefault(r => r.Def.Id == l.RoadId)?.Def.Hierarchy).OrderByDescending(Hierarquia.Rank).FirstOrDefault();
            if (hs != null) rb.Hierarchy = hs;
        }
        var layout = RoundaboutGenerator.Layout(rb);
        results.Add(_service.Render(rb));
        foreach (var old in rb.ChildIds) _service.Delete(old);
        var children = RoundaboutGenerator.Children(rb, layout, rb.Output, rb.Z);
        foreach (var c in children)
        {
            c.Hierarchy = rb.Hierarchy;
            results.Add(_service.Render(c));
        }
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

    /// <summary>Apaga a rotatória, sua sinalização e os recortes que ela fazia nas vias.</summary>
    public void Remove(RoundaboutDefinition rb)
    {
        foreach (var c in rb.ChildIds) _service.Delete(c);
        _service.Delete(rb.Id);
        _service.Invalidate();
        foreach (var m in MarkingStorage.Definitions(_doc).Where(d => d.Exclusions.Any(e => e.SourceId == rb.Id)))
        {
            m.Exclusions.RemoveAll(e => e.SourceId == rb.Id);
            try { _service.Render(m); }
            catch (Exception ex) { Log.Error("Remover rotatória", ex); }
        }
    }

    /// <summary>Apaga o cul-de-sac e devolve a via ao traçado original.</summary>
    public void Remove(CulDeSacDefinition c)
    {
        _service.Delete(c.Id);
        _service.Invalidate();
        foreach (var m in MarkingStorage.Definitions(_doc).Where(d => d.Exclusions.Any(e => e.SourceId == c.Id)))
        {
            m.Exclusions.RemoveAll(e => e.SourceId == c.Id);
            try { _service.Render(m); }
            catch (Exception ex) { Log.Error("Remover cul-de-sac", ex); }
        }
    }

    /// <summary>
    /// Cul-de-sac ligado à ponta de uma via: acompanha o eixo, a largura e a calçada da via e recorta a via no trecho
    /// do balão. Se a ponta passou a encontrar outra via, o balão é removido.
    /// </summary>
    public List<RenderResult> Refresh(CulDeSacDefinition c)
    {
        var results = new List<RenderResult>();
        var roads = Roads();
        var road = roads.FirstOrDefault(r => r.Def.Id == c.RoadId);
        if (road == null) { Remove(c); return results; }
        var others = roads.Where(r => r.Def.Id != road.Def.Id).ToList();
        if (!RoadConnection.FreeEnds(road, others).Any(e => e.AtEnd == c.AtRoadEnd)) { Remove(c); return results; }
        var cut = RoadConnection.FitCulDeSac(c, road, road.Def.Path?.Z ?? 0);
        results.Add(_service.Render(c));
        _service.Invalidate();
        foreach (var m in MarkingStorage.Definitions(_doc).Where(d => d.GroupId != null && d.GroupId == road.Def.GroupId || d.Exclusions.Any(e => e.SourceId == c.Id)))
        {
            if (m.Id == c.Id || m is IAnnotationDefinition) continue;
            m.Exclusions.RemoveAll(e => e.SourceId == c.Id);
            if (m.GroupId == road.Def.GroupId)
                foreach (var p in cut) m.Exclusions.Add(new ExclusionZone { SourceId = c.Id, Points = p.Outer.ToList() });
            try { results.Add(_service.Render(m)); }
            catch (Exception ex) { Log.Error($"Cul-de-sac – via {m.DisplayCode}", ex); }
        }
        return results;
    }

    public List<CulDeSacDefinition> CulDeSacsDependentOn(IEnumerable<MarkingDefinition> changed)
    {
        var groups = changed.Select(d => d.GroupId).Where(g => g != null).ToHashSet();
        var all = MarkingStorage.Definitions(_doc);
        var roadIds = all.OfType<RoadPavementDefinition>().Where(p => groups.Contains(p.GroupId)).Select(p => p.Id).ToHashSet();
        return all.OfType<CulDeSacDefinition>().Where(c => c.RoadId != null && roadIds.Contains(c.RoadId)).ToList();
    }

    /// <summary>Rotatórias cujo centro está na ponta (ou sobre o eixo) da via indicada.</summary>
    public List<RoundaboutDefinition> RoundaboutsTouching(IntersectionRoad road) =>
        MarkingStorage.Definitions(_doc).OfType<RoundaboutDefinition>()
            .Where(rb => IntersectionGenerator.Project(road.Axis, rb.Center).Distance < 3).ToList();

    /// <summary>
    /// Liga a via (recém-criada ou editada) ao sistema viário: cada encontro com outra via vira interseção ou rotatória,
    /// rotatórias tocadas ganham o novo ramo e as pontas livres recebem cul-de-sac, conforme as opções.
    /// </summary>
    public List<RenderResult> Connect(RoadPavementDefinition road, TipoConexao mode, FimLivre ends,
        IntersectionDefinition itTemplate, RoundaboutDefinition rbTemplate, CulDeSacDefinition cdsTemplate, bool radiusByHierarchy)
    {
        var results = new List<RenderResult>();
        if (PluginContext.Settings.AutoConnect && road.GroupId != null)
        {
            var moved = MagnetGroups(new[] { road.GroupId });
            if (moved.Count > 0) results.AddRange(RenderGroups(moved));
        }
        var roads = Roads(createMissing: true);
        var me = roads.FirstOrDefault(r => r.Def.Id == road.Id);
        if (me == null) return results;

        // Rotatórias existentes na ponta da via: a via vira um novo ramo.
        foreach (var rb in RoundaboutsTouching(me)) results.AddRange(Refresh(rb));

        if (mode == TipoConexao.Intersecao)
            results.AddRange(AutoIntersectGroups(new[] { road.GroupId ?? "" }, itTemplate, out _, radiusByHierarchy));
        else if (mode == TipoConexao.Rotatoria)
        {
            var idx = roads.IndexOf(me);
            var existingRb = MarkingStorage.Definitions(_doc).OfType<RoundaboutDefinition>().ToList();
            foreach (var (node, ids) in IntersectionGenerator.FindNodes(roads).Where(n => n.Roads.Contains(idx)))
            {
                if (existingRb.Any(r => r.Center.DistanceTo(node) < r.OuterRadius + 5)) continue;
                if (!IntersectionGenerator.NeedsIntersection(ids.Select(i => roads[i]).ToList(), node)) continue;
                results.AddRange(ConvertToRoundabout(node, rbTemplate, roads));
                roads = Roads();
            }
        }

        if (ends == FimLivre.CulDeSac)
        {
            roads = Roads();
            me = roads.FirstOrDefault(r => r.Def.Id == road.Id);
            if (me != null)
                foreach (var (atEnd, _, _) in RoadConnection.FreeEnds(me, roads.Where(r => r.Def.Id != road.Id).ToList()))
                    results.AddRange(AddCulDeSac(me, atEnd, cdsTemplate));
        }
        return results;
    }

    /// <summary>Cul-de-sac na ponta indicada da via (substitui um existente na mesma ponta).</summary>
    public List<RenderResult> AddCulDeSac(IntersectionRoad road, bool atEnd, CulDeSacDefinition template)
    {
        foreach (var old in MarkingStorage.Definitions(_doc).OfType<CulDeSacDefinition>().Where(c => c.RoadId == road.Def.Id && c.AtRoadEnd == atEnd).ToList())
            Remove(old);
        var c = (CulDeSacDefinition)template.CloneWithNewId();
        c.RoadId = road.Def.Id;
        c.AtRoadEnd = atEnd;
        c.Exclusions.Clear();
        c.GroupId = null;
        return Refresh(c);
    }

    /// <summary>Troca o que houver no nó (interseção) por uma rotatória ligada às vias.</summary>
    public List<RenderResult> ConvertToRoundabout(Vec2 node, RoundaboutDefinition template, List<IntersectionRoad>? roads = null)
    {
        roads ??= Roads(createMissing: true);
        foreach (var it in MarkingStorage.Definitions(_doc).OfType<IntersectionDefinition>().Where(i => i.Node.DistanceTo(node) < template.OuterRadius + 5).ToList())
            Remove(it);
        var rb = (RoundaboutDefinition)template.CloneWithNewId();
        rb.ChildIds.Clear();
        rb.Center = node;
        var first = roads.FirstOrDefault(r => IntersectionGenerator.Project(r.Axis, node).Distance < 3);
        rb.Z = first?.Def.Path?.Z ?? 0;
        rb.Output = first?.Def.Output.Clone() ?? rb.Output;
        rb.Legs = RoundaboutGenerator.LegsFromRoads(node, roads, rb.OuterRadius + 25);
        if (rb.Legs.Count < 2) return new List<RenderResult>();
        return Refresh(rb);
    }

    /// <summary>
    /// Ímã de conexão (InfraWorks): as pontas dos eixos das vias indicadas que caíram sobre outra via vão para o eixo
    /// dela (ou para a ponta dela / centro da rotatória). Devolve os grupos cujos eixos foram alterados.
    /// </summary>
    public HashSet<string> MagnetGroups(IEnumerable<string> groupIds)
    {
        var moved = new HashSet<string>();
        var set = groupIds.Where(g => !string.IsNullOrEmpty(g)).ToHashSet();
        if (set.Count == 0) return moved;
        var roads = Roads();
        var rbs = MarkingStorage.Definitions(_doc).OfType<RoundaboutDefinition>().Select(r => (r.Center, r.OuterRadius)).ToList();
        foreach (var me in roads.Where(r => r.Def.GroupId != null && set.Contains(r.Def.GroupId)))
        {
            if (me.Def.Path == null) continue;
            var others = roads.Where(r => r.Def.GroupId != me.Def.GroupId).ToList();
            var (a, b) = RoadConnection.MagnetEnds(me.Axis, others, rbs);
            var changed = false;
            if (a is { } ma) changed |= AxisEditor.MoveEnd(_doc, me.Def.Path, me.Axis.Points[0], ma.Point);
            if (b is { } mb) changed |= AxisEditor.MoveEnd(_doc, me.Def.Path, me.Axis.Points[^1], mb.Point);
            if (changed) moved.Add(me.Def.GroupId!);
        }
        return moved;
    }

    /// <summary>
    /// Vias ligadas a uma via que foi movida acompanham a nova posição: a ponta que estava no nó da interseção vai
    /// para o novo eixo (ao longo da própria direção). Devolve os grupos alterados.
    /// </summary>
    public HashSet<string> FollowConnections(IEnumerable<string> movedGroups)
    {
        var res = new HashSet<string>();
        var set = movedGroups.Where(g => !string.IsNullOrEmpty(g)).ToHashSet();
        if (set.Count == 0) return res;
        var roads = Roads();
        var movedRoads = roads.Where(r => r.Def.GroupId != null && set.Contains(r.Def.GroupId)).ToList();
        if (movedRoads.Count == 0) return res;
        var movedIds = movedRoads.Select(r => r.Def.Id).ToHashSet();
        foreach (var it in MarkingStorage.Definitions(_doc).OfType<IntersectionDefinition>().Where(i => i.RoadIds.Any(movedIds.Contains)))
        {
            foreach (var r in roads.Where(r => it.RoadIds.Contains(r.Def.Id) && !movedIds.Contains(r.Def.Id)))
            {
                if (r.Def.Path == null) continue;
                foreach (var atEnd in new[] { false, true })
                {
                    var e = atEnd ? r.Axis.Points[^1] : r.Axis.Points[0];
                    if (e.DistanceTo(it.Node) > 1.0) continue;         // esta ponta não estava ligada ao nó
                    var targets = movedRoads.Where(m => it.RoadIds.Contains(m.Def.Id)).ToList();
                    var (a, b) = RoadConnection.MagnetEnds(r.Axis, targets, null, 0);
                    var m1 = atEnd ? b : a;
                    Vec2? target = m1?.Point;
                    if (target == null)
                    {
                        // Fora do alcance do ímã: prolonga/apara ao longo da própria direção até o novo eixo.
                        var prev = atEnd ? r.Axis.PointAt(Math.Max(0, r.Axis.Length - 5)) : r.Axis.PointAt(Math.Min(r.Axis.Length, 5));
                        var dir = (e - prev).Normalized();
                        var best = double.MaxValue;
                        foreach (var m in targets)
                        {
                            var (_, dist, q) = IntersectionGenerator.Project(m.Axis, e);
                            if (dist < best && dist < 60) { best = dist; target = q; }
                            for (var t = -60.0; t <= 60; t += 0.5)
                            {
                                var p = e + dir * t;
                                var (_, d2, q2) = IntersectionGenerator.Project(m.Axis, p);
                                if (d2 < 0.3 && Math.Abs(t) < best) { best = Math.Abs(t); target = q2; }
                            }
                        }
                    }
                    if (target is { } tg && tg.DistanceTo(e) > 0.01 && AxisEditor.MoveEnd(_doc, r.Def.Path, e, tg)) res.Add(r.Def.GroupId!);
                }
            }
        }
        return res;
    }

    /// <summary>
    /// Acrescenta um elemento linear (meio-fio, calçada, grama, sarjeta, linha) junto ao bordo da via mais próxima do
    /// ponto, do lado clicado. O elemento passa a fazer parte da via (grupo) e a seção do pavimento é atualizada, de
    /// modo que as interseções, rotatórias e cul-de-sacs da via o refaçam também.
    /// </summary>
    public List<RenderResult> AddAtRoadEdge(MarkingDefinition template, Vec2 p)
    {
        var results = new List<RenderResult>();
        if (template is not LinearMarkingDefinition lt) return results;
        var roads = Roads(createMissing: true);
        var best = roads.Select(r => (r, pr: IntersectionGenerator.Project(r.Axis, p)))
            .Where(x => x.pr.Distance <= Math.Max(x.r.Def.TotalLeft, x.r.Def.TotalRight) + 12)
            .OrderBy(x => x.pr.Distance).FirstOrDefault();
        if (best.r == null) return results;
        var road = best.r;
        var left = road.Axis.TangentAt(best.pr.Station).Cross(p - best.pr.Point) > 0;
        var line = (LinearMarkingDefinition)lt.CloneWithNewId();
        line.Exclusions.Clear();
        RoadConnection.PlaceAtEdge(road.Def, left, line, PluginContext.Catalog);
        results.Add(_service.Render(line));
        results.Add(_service.Render(road.Def));
        _service.Invalidate();
        var changed = new List<MarkingDefinition> { road.Def, line };
        foreach (var it in DependentOn(changed)) results.AddRange(Refresh(it));
        foreach (var rb in RoundaboutsDependentOn(changed)) results.AddRange(Refresh(rb));
        foreach (var c in CulDeSacsDependentOn(changed)) results.AddRange(Refresh(c));
        return results;
    }

    /// <summary>Regenera todas as marcas das vias (grupos) indicadas.</summary>
    public List<RenderResult> RenderGroups(IEnumerable<string> groups)
    {
        var set = groups.ToHashSet();
        var results = new List<RenderResult>();
        _service.Invalidate();
        foreach (var d in MarkingService.DependencyOrder(MarkingStorage.Definitions(_doc).Where(d => d.GroupId != null && set.Contains(d.GroupId))))
        {
            try { results.Add(_service.Render(d)); }
            catch (Exception ex) { Log.Error("Regenerar via", ex); }
        }
        return results;
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
