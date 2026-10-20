using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Via do plugin escolhida pelo usuário: pavimento (seção + greide), eixo e cota da base.</summary>
internal sealed record PickedRoad(RoadPavementDefinition Pavement, Polyline2 Axis, double BaseZ)
{
    public string GroupId => Pavement.GroupId!;
    public RoadGrade Grade => Pavement.Output.Grade ?? RoadGrade.Flat(Axis.Length);
}

/// <summary>
/// Vias e obras como um só sistema: escolher uma via, mudar o greide dela (todas as marcas da via acompanham), criar uma via
/// nova com a ferramenta de vias e hospedar pontes, viadutos, túneis e trincheiras num trecho do eixo.
/// </summary>
internal static class RoadWorks
{
    /// <summary>Via clicada (qualquer elemento dela: pista, calçada, linha, obra hospedada). Nulo = cancelado.</summary>
    public static PickedRoad? PickRoad(UIDocument uidoc, string prompt)
    {
        var doc = uidoc.Document;
        while (true)
        {
            var stored = MarkingPicker.PickOne(uidoc, prompt);
            if (stored == null) return null;
            if (Road(doc, stored.Definition) is { } r) return r;
            TaskDialog.Show(CommandBase.AppTitle, "O elemento escolhido não pertence a uma via do plugin (Via / Pista). Clique a pista, uma calçada ou uma linha da via.");
            uidoc.Selection.SetElementIds(new List<ElementId>());
        }
    }

    public static PickedRoad? Road(Document doc, MarkingDefinition d)
    {
        var gid = d is IHostedStructure { HostRoad: { } h } ? h : d.GroupId;
        if (gid == null) return null;
        var pav = d as RoadPavementDefinition ?? MarkingStorage.Definitions<RoadPavementDefinition>(doc).FirstOrDefault(p => p.GroupId == gid);
        if (pav?.GroupId == null) return null;
        var path = PathResolver.Resolve(doc, pav.Path);
        return path?.Main == null ? null : new PickedRoad(pav, path.Main, path.Z);
    }

    public static PickedRoad? RoadByGroup(Document doc, string gid) =>
        MarkingStorage.Definitions<RoadPavementDefinition>(doc).FirstOrDefault(p => p.GroupId == gid) is { } pav ? Road(doc, pav) : null;

    public static List<MarkingDefinition> Members(Document doc, string gid) =>
        MarkingStorage.InGroup(doc, gid);

    public static List<MarkingDefinition> Hosted(Document doc, string? gid) =>
        gid == null ? new() : MarkingStorage.Mentioning(doc, gid).Where(d => d is IHostedStructure h && h.HostRoad == gid).ToList();

    /// <summary>Estações do trecho clicado (dois pontos sobre a via). Nulo = cancelado.</summary>
    public static (double S0, double S1)? PickStretch(UIDocument uidoc, PickedRoad road, string what)
    {
        var a = Picking.PickPoint(uidoc, $"{what}: clique o INÍCIO do trecho sobre a via – ESC cancela");
        if (a == null) return null;
        var b = Picking.PickPoint(uidoc, $"{what}: clique o FIM do trecho sobre a via");
        if (b == null) return null;
        var sa = road.Axis.Project(UnitConv.ToVec2(a)).Station;
        var sb = road.Axis.Project(UnitConv.ToVec2(b)).Station;
        return (Math.Min(sa, sb), Math.Max(sa, sb));
    }

    /// <summary>Terreno natural relativo à base da via, ao longo do eixo (sem terreno: o próprio greide).</summary>
    public static Func<double, double> GroundAlong(Document doc, PickedRoad road)
    {
        var g = TerrainModel.Ground(doc);
        var grade = road.Grade;
        return s => g?.Invoke(road.Axis.PointAt(s)) is { } z ? z - road.BaseZ : grade.Z(s);
    }

    /// <summary>Terreno natural (m, relativo a <paramref name="baseZ"/>) num ponto; nulo sem Toposolid.</summary>
    public static Func<Vec2, double?> Ground(Document doc, double baseZ)
    {
        var g = TerrainModel.Ground(doc);
        return p => g?.Invoke(p) is { } z ? z - baseZ : null;
    }

    /// <summary>
    /// Grava o greide em todas as marcas da via e as regenera (pisos e pinturas acompanham), refaz as conexões e as obras
    /// hospedadas.
    /// </summary>
    public static List<RenderResult> SetGrade(UIDocument uidoc, string gid, RoadGrade grade, IEnumerable<MarkingDefinition>? extra = null)
    {
        var doc = uidoc.Document;
        var members = Members(doc, gid);
        foreach (var m in members) m.Output.Grade = grade.Clone();
        var hosted = Hosted(doc, gid);
        foreach (var h in hosted) h.Output.Grade = grade.Clone();
        var all = members.Concat(hosted).Concat(extra ?? Array.Empty<MarkingDefinition>()).GroupBy(d => d.Id).Select(g => g.Last()).ToList();
        var results = MarkingCreator.Commit(uidoc, all, "SV - Greide da via");
        try { results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões da via", sv => sv.RefreshDependents(members)).Where(r => r.Warnings.Count > 0)); }
        catch (Exception ex) { Log.Error("Greide – conexões", ex); }
        return results;
    }

    /// <summary>
    /// Greide da via com as obras hospedadas: parte do greide de projeto sem obras e aplica cada obra no seu trecho (pontes
    /// elevam com rampas, trincheiras rebaixam, túneis em rampa constante), na ordem do eixo.
    /// </summary>
    public static RoadGrade Compose(Document doc, PickedRoad road, IEnumerable<MarkingDefinition> hosted, RoadGrade? baseGrade = null)
    {
        var b = (baseGrade ?? road.Grade.WithoutWorks ?? road.Grade).Clone();
        b.WithoutWorks = null;
        var ground = GroundAlong(doc, road);
        var L = road.Axis.Length;
        var g = b.Clone();
        foreach (var def in hosted.Where(d => d is IHostedStructure).OrderBy(d => ((IHostedStructure)d).HostStart))
        {
            var h = (IHostedStructure)def;
            var (s0, s1) = (Math.Min(h.HostStart, h.HostEnd), Math.Max(h.HostStart, h.HostEnd));
            g = def switch
            {
                BridgeDefinition br => InfraRoads.BridgeOnRoad(g, br, s0, s1, L, ground),
                TrenchDefinition { KeepRoadGrade: true } => g,
                TrenchDefinition tr => InfraRoads.TrenchOnRoad(g, s0, s1, tr.Depth, tr.MaxGrade, tr.VerticalCurve, ground),
                TunnelDefinition tn => InfraRoads.TunnelOnRoad(g, s0, s1, tn.StraightAxis),
                _ => g,
            };
            g.WithoutWorks = null;
        }
        g.AdjustTerrain = true;
        g.WithoutWorks = b;
        return g;
    }

    /// <summary>Greide da via com a obra (nova ou editada) e as demais obras dela.</summary>
    public static RoadGrade GradeWith(Document doc, PickedRoad road, MarkingDefinition def) =>
        Compose(doc, road, Hosted(doc, road.GroupId).Where(d => d.Id != def.Id).Append(def));

    /// <summary>
    /// Obra hospedada editada: o greide da via é recomposto (greide sem obras + todas as obras) e a via, as conexões, as obras e
    /// o terreno são regenerados.
    /// </summary>
    public static List<RenderResult> AfterHostedEdit(UIDocument uidoc, MarkingDefinition def)
    {
        if (def is not IHostedStructure { HostRoad: { } gid } || RoadByGroup(uidoc.Document, gid) is not { } road) return new();
        var grade = GradeWith(uidoc.Document, road, def);
        def.Output.Grade = grade.Clone();
        return SetGrade(uidoc, gid, grade, new[] { def }).Where(r => r.Warnings.Count > 0).ToList();
    }

    /// <summary>Regenera as obras hospedadas numa via (depois de mudar a seção ou o greide).</summary>
    public static List<RenderResult> RefreshHosted(UIDocument uidoc, string? gid)
    {
        var hosted = Hosted(uidoc.Document, gid);
        if (hosted.Count == 0) return new();
        var road = gid == null ? null : RoadByGroup(uidoc.Document, gid);
        foreach (var h in hosted)
        {
            if (road != null) h.Output.Grade = road.Pavement.Output.Grade?.Clone();
            if (h is IHostedStructure hs && road != null) Snapshot(hs, road.Pavement);
        }
        return MarkingCreator.Commit(uidoc, hosted, "SV - Obras da via").Where(r => r.Warnings.Count > 0).ToList();
    }

    /// <summary>Prepara a obra como trecho [s0, s1] da via: mesmo eixo, greide e seção.</summary>
    public static T Host<T>(T def, RoadPavementDefinition pav, double s0, double s1) where T : MarkingDefinition, IHostedStructure
    {
        def.HostRoad = pav.GroupId;
        def.HostStart = s0;
        def.HostEnd = s1;
        def.SetPath(pav.PathRef.Clone());
        def.Output = InfraRunner.Output3D();
        def.Output.Grade = pav.Output.Grade?.Clone();
        def.Hierarchy ??= pav.Hierarchy;
        def.GroupId = null;
        Snapshot(def, pav);
        if (def is ITerrainAware ta) ta.GroundLine = null;
        return def;
    }

    private static void Snapshot(IHostedStructure h, RoadPavementDefinition pav)
    {
        var info = HostRoads.Of(pav);
        h.HostHalf = info.Half;
        h.HostWear = info.Wear;
        h.HostEdgeRise = info.EdgeRise;
    }

    /// <summary>
    /// Cria uma via do plugin (mesma ferramenta Via/Pista) no caminho: pista, linhas, calçadas e conexões às vias existentes,
    /// já com o greide. Devolve o pavimento (nulo se falhar).
    /// </summary>
    public static (RoadPavementDefinition? Pavement, List<RenderResult> Results) CreateRoad(UIDocument uidoc, RoadSetup setup, PathReference path, RoadGrade? grade,
        bool connect = true, string name = "Via", IntersectionDefinition? intersectionTemplate = null)
    {
        var output = PluginContext.Settings.NewOutput();
        output.Mode = OutputMode.Modelo3D;
        output.Drape = false;
        output.SurfaceIds.Clear();
        output.Grade = grade?.Clone();
        var defs = setup.Build(path, output, PluginContext.Catalog);
        var outer = defs.Where(d => d is PlanterDefinition || Core.Generators.IntersectionGenerator.IsPhysical(d)).ToList();
        var core = defs.Where(d => !outer.Contains(d)).ToList();
        var results = MarkingCreator.Commit(uidoc, core, $"SV - {name}");
        var pav = defs.OfType<RoadPavementDefinition>().FirstOrDefault();
        if (connect && pav != null)
        {
            try
            {
                var template = intersectionTemplate ?? IntersectionService.AutoTemplate();
                results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões da via", sv =>
                    sv.Connect(pav, TipoConexao.Intersecao, FimLivre.Nenhum, template, new RoundaboutDefinition(), new CulDeSacDefinition(), radiusByHierarchy: true)));
            }
            catch (Exception ex) { Log.Error("Conexões da via da obra", ex); }
        }
        if (outer.Count > 0)
        {
            results.AddRange(MarkingCreator.Commit(uidoc, outer, $"SV - {name} (calçadas e meios-fios)"));
            try { results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões da via", sv => sv.RefreshDependents(outer))); }
            catch (Exception ex) { Log.Error("Conexões da via da obra (bordo)", ex); }
        }
        return (pav, results.Where(r => r.Warnings.Count > 0).ToList());
    }

    /// <summary>
    /// Relevo da via recém-criada sobre o Toposolid: greide pelo terreno (colado ou suavizado) em todas as marcas da via e
    /// terraplenagem no terreno nativo (corte, aterro e taludes). Sem Toposolid (ou relevo plano) nada muda.
    /// </summary>
    public static List<RenderResult> ApplyRelief(UIDocument uidoc, RoadPavementDefinition pav, RelevoVia relief)
    {
        var res = new List<RenderResult>();
        var doc = uidoc.Document;
        if (relief == RelevoVia.Plana || TerrainModel.Hosts(doc).Count == 0 || Road(doc, pav) is not { } road) return res;
        var st = PluginContext.Settings;
        var ground = Ground(doc, road.BaseZ);
        var axis = road.Axis;
        // Sem terreno sob a maior parte do eixo: fica plana.
        var covered = Enumerable.Range(0, 21).Count(i => ground(axis.PointAt(axis.Length * i / 20)) != null);
        if (covered < 5) return res;
        var opt = RoadProfileDesigner.ForRelief(relief, st.RoadReliefMaxGrade, st.RoadCutSlope, st.RoadFillSlope);
        // Em cada nó a via PRINCIPAL (a mesma da superfície do nó: hierarquia, empate = a mais larga) atravessa com o próprio
        // greide e as secundárias ficam em nível, na cota dela, só no alcance da interseção. Antes as duas vias ganhavam patamar
        // (~60 m em nível mesmo em rampa constante: cortes e aterros à toa e mais curvas verticais).
        var platforms = new List<(Core.Generators.IntersectionRoad Road, RoadGrade Grade)>();
        var roads = new List<Core.Generators.IntersectionRoad>();
        var nodes = new List<(Vec2 Node, List<int> Roads)>();
        var touched = new HashSet<int>();
        var mine = -1;
        var its = new List<IntersectionDefinition>();
        // Alcance da área do nó ao longo de cada via (recortes da interseção que contêm o nó): o patamar cobre a área toda.
        double Reach(Core.Generators.IntersectionRoad r, Vec2 node) => RoadProfileDesigner.NodeReach(r.Axis, node,
            r.Def.Exclusions.Where(e => e.SourceId != null).Select(e => (IReadOnlyList<Vec2>)e.Points));
        // Via principal do nó, na ordem das vias da interseção (a da superfície do nó), quando ela já existe.
        int Major(Vec2 node, List<int> ids)
        {
            var it = its.Where(d => ids.Any(i => d.RoadIds.Contains(roads[i].Def.Id)))
                .Where(d => d.Node.DistanceTo(node) < Core.Generators.IntersectionGenerator.NodeMergeDistance * 4)
                .OrderBy(d => d.Node.DistanceTo(node)).FirstOrDefault();
            var order = it == null ? ids : ids.OrderBy(i => it.RoadIds.IndexOf(roads[i].Def.Id) is var k && k >= 0 ? k : int.MaxValue).ToList();
            var m = RoadProfileDesigner.MajorOf(order.Select(i => roads[i].Def).ToList());
            return m < 0 ? -1 : order[m];
        }
        double Clamp(Polyline2 ax, Vec2 p) => Math.Clamp(ax.Project(p).Station, 0, ax.Length);
        // Cruzamentos e entroncamentos com as vias existentes: interseção no mesmo nível.
        try
        {
            var sv = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
            roads = sv.Roads();
            its = MarkingStorage.Definitions<IntersectionDefinition>(doc).ToList();
            mine = roads.FindIndex(r => r.Def.Id == road.Pavement.Id);
            if (mine >= 0)
            {
                nodes = Core.Generators.IntersectionGenerator.FindNodes(roads).ToList();
                foreach (var (node, ids) in nodes.Where(n => n.Roads.Contains(mine)))
                {
                    var others = ids.Where(i => i != mine).ToList();
                    if (others.Count == 0) continue;
                    var major = Major(node, ids);
                    if (major == mine)
                    {
                        // A via nova é a principal: passa com a própria rampa; as secundárias ficam em nível na cota dela.
                        foreach (var i in others.Where(i => roads[i].Def.Output.Grade != null)) touched.Add(i);
                        continue;
                    }
                    // Secundária: na cota da boca do nó (a da principal menos o abaulamento) e com a rampa do nó ao longo dela (em
                    // nível num cruzamento em ângulo reto), no alcance do nó.
                    var sm = major >= 0 ? Clamp(roads[major].Axis, node) : 0;
                    var gm = major >= 0 ? roads[major].Def.Output.Grade : null;
                    var zAbs = gm != null
                        ? sv.RoadZ(roads[major], node) - gm.Z(sm) + RoadProfileDesigner.MouthZ(gm, sm, roads[major].Def)
                        : major >= 0 ? sv.RoadZ(roads[major], node) : others.Average(o => sv.RoadZ(roads[o], node));
                    opt.Fixed.Add(new Vec2(Clamp(axis, node), zAbs - road.BaseZ));
                    opt.FixedHalf.Add(RoadProfileDesigner.PlatformFor(Reach(roads[mine], node), opt.VerticalCurve));
                    opt.FixedSlope.Add(gm != null ? RoadProfileDesigner.NodeSlope(gm, sm, roads[major].Axis.Length, roads[major].Axis.TangentAt(sm), axis.TangentAt(Clamp(axis, node))) : 0);
                }
            }
            // Pontas coladas a outra via: a cota da ponta é a dela.
            if (opt.Fixed.FirstOrDefault(f => f.X < 1) is { } f0 && f0 != default) opt.StartZ = f0.Y;
            if (opt.Fixed.FirstOrDefault(f => f.X > axis.Length - 1) is { } f1 && f1 != default) opt.EndZ = f1.Y;
        }
        catch (Exception ex) { Log.Error("Relevo – cruzamentos", ex); }
        var perfil = RoadProfileDesigner.Design(axis.Length, s => ground(axis.PointAt(s)), opt);
        var grade = perfil.Grade;
        grade.AdjustTerrain = true;
        grade.CutSlope = st.RoadCutSlope;
        grade.FillSlope = st.RoadFillSlope;
        // Vias existentes secundárias num nó em que a via nova é a principal: em nível na cota da via nova ali; nos outros nós
        // delas a cota fica (patamar onde são secundárias, a própria rampa onde são as principais).
        try
        {
            foreach (var i in touched)
            {
                var o = roads[i];
                var g0 = o.Def.Output.Grade!;
                var oBase = PathResolver.Resolve(doc, o.Def.Path)?.Z ?? o.Def.Path?.Z ?? 0;
                var pins = new List<Vec2>();
                var halves = new List<double>();
                var slopes = new List<double>();
                foreach (var (node, ids) in nodes.Where(n => n.Roads.Contains(i)))
                {
                    var so = Clamp(o.Axis, node);
                    var m = Major(node, ids);
                    if (m == i) { pins.Add(new Vec2(so, g0.Z(so))); halves.Add(RoadProfileDesigner.PassThrough); slopes.Add(0); continue; }
                    var mineMajor = m == mine && ids.Contains(mine);
                    var sm = Clamp(axis, node);
                    var z = mineMajor ? road.BaseZ + RoadProfileDesigner.MouthZ(grade, sm, road.Pavement) - oBase : g0.Z(so);
                    pins.Add(new Vec2(so, z));
                    halves.Add(RoadProfileDesigner.PlatformFor(Reach(o, node), g0.DefaultCurve));
                    // Nos outros nós a rampa atual dela no nó (o patamar que ela já tinha).
                    slopes.Add(mineMajor ? RoadProfileDesigner.NodeSlope(grade, sm, axis.Length, axis.TangentAt(sm), o.Axis.TangentAt(so)) : g0.GradeAt(so));
                }
                platforms.Add((o, RoadProfileDesigner.WithPlatforms(g0, o.Axis.Length, pins, st.RoadReliefMaxGrade, halves, slopes)));
            }
        }
        catch (Exception ex) { Log.Error("Relevo – patamar das vias secundárias", ex); }
        res.AddRange(SetGrade(uidoc, road.GroupId, grade));
        foreach (var (o, g) in platforms)
            if (o.Def.GroupId != null)
            {
                try { res.AddRange(SetGrade(uidoc, o.Def.GroupId, g)); }
                catch (Exception ex) { Log.Error("Patamar da via existente no nó", ex); }
            }
        TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { road.Pavement }, quiet: true);
        var r = new RenderResult();
        r.Warnings.Add((relief == RelevoVia.AcompanharTerreno ? "Relevo: a via acompanha o terreno" : "Relevo: greide suavizado") +
                       $" – corte {perfil.CutM2:0} m² / aterro {perfil.FillM2:0} m² no eixo; Toposolid ajustado (corte, aterro e taludes).");
        res.Insert(0, r);
        return res;
    }

    /// <summary>
    /// Eixo de uma via nova: desenhado por pontos (encaixa nas vias existentes, curvas concordadas) ou linhas selecionadas;
    /// com <paramref name="straight"/> só as pontas valem (obra em tangente).
    /// </summary>
    public static PathReference? AxisFor(UIDocument uidoc, bool draw, double curveRadius, bool straight, List<string> snapped)
    {
        var path = RoadAxisInput.Get(uidoc, draw, true, straight ? 0 : curveRadius, snapped);
        if (path == null || !straight || !draw) return path;
        var doc = uidoc.Document;
        var resolved = PathResolver.Resolve(doc, path);
        if (resolved?.Main is not { } main || main.Points.Count <= 2) return path;
        using var t = new Transaction(doc, "SV - Eixo reto");
        t.Start();
        var zf = UnitConv.Ft(resolved.Z);
        var id = Picking.CreateLine(doc, uidoc.ActiveView, new XYZ(UnitConv.Ft(main.Points[0].X), UnitConv.Ft(main.Points[0].Y), zf),
            new XYZ(UnitConv.Ft(main.Points[^1].X), UnitConv.Ft(main.Points[^1].Y), zf), new StyleService(doc));
        foreach (var uid in path.ElementIds) if (doc.GetElement(uid) is { } e) try { doc.Delete(e.Id); } catch { /* já removida */ }
        t.Commit();
        return PathReference.FromElements(new[] { doc.GetElement(id).UniqueId });
    }
}
