using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Nó viário montado sobre vias do plugin: clique o cruzamento de duas vias (ou um ponto livre – as vias são criadas com a
/// ferramenta de vias). A via de cima ganha greide e viaduto hospedado; os ramos e laços são vias novas (pisos, faixas,
/// bordos, defensas) ligadas às duas por faixas de mudança de velocidade, interseções ou rotatórias.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdNoViario : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var template = UiHelpers.Remembered<InterchangeDefinition>("infra:no") ?? new InterchangeDefinition();
        template.Output = InfraRunner.Output3D();
        template.MainRoad = null; template.CrossRoad = null; template.Ramps.Clear(); template.WorkIds.Clear(); template.OverBaseGrade = null;
        var w = InfraForms.Interchange(template, false);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        UiHelpers.Remember("infra:no", template);
        PluginContext.SaveSettings();
        var c = Picking.PickPoint(uidoc, "Nó viário: clique o CRUZAMENTO de duas vias (ou um ponto livre para criar as vias) – ESC cancela");
        if (c == null) return Result.Cancelled;
        var d = (InterchangeDefinition)template.CloneWithNewId();
        var results = InterchangeBuilder.Build(uidoc, d, UnitConv.ToVec2(c), UnitConv.M(c.Z));
        if (results == null) return Result.Cancelled;
        Report("Nó viário", results.Where(r => r.Warnings.Count > 0).ToList(), alwaysShow: true);
        return Result.Succeeded;
    }
}

/// <summary>Monta (ou refaz) o nó viário sobre as vias do plugin.</summary>
internal static class InterchangeBuilder
{
    /// <summary>Monta o nó; nulo = cancelado.</summary>
    public static List<RenderResult>? Build(UIDocument uidoc, InterchangeDefinition d, Vec2 click, double clickZ)
    {
        var doc = uidoc.Document;
        var results = new List<RenderResult>();
        var info = new RenderResult();
        results.Add(info);

        // 1) As duas vias: as do cruzamento clicado (a de maior hierarquia/largura é a principal) ou criadas agora.
        PickedRoad? main = d.MainRoad != null ? RoadWorks.RoadByGroup(doc, d.MainRoad) : null;
        PickedRoad? cross = d.CrossRoad != null ? RoadWorks.RoadByGroup(doc, d.CrossRoad) : null;
        if (main == null || cross == null)
        {
            var pair = FindCrossing(doc, click);
            if (pair == null && Notify.Quiet)
            {
                info.Warnings.Add("Nó viário: nenhum cruzamento de vias no ponto indicado.");
                return results;
            }
            if (pair == null)
            {
                var td = new TaskDialog(CommandBase.AppTitle)
                {
                    MainInstruction = "Nenhum cruzamento de vias no ponto clicado",
                    MainContent = "Criar as duas vias agora (rodovia de pista dupla e via transversal, com a ferramenta de vias) e montar o nó nelas?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                };
                if (td.Show() != TaskDialogResult.Yes) return null;
                var dirPt = Picking.PickPoint(uidoc, "Clique a DIREÇÃO da via principal (a partir do centro) – ESC = eixo X");
                d.Position = click;
                if (dirPt != null && UnitConv.ToVec2(dirPt).DistanceTo(click) > 0.5)
                {
                    var dv = UnitConv.ToVec2(dirPt) - click;
                    d.AngleDeg = Math.Atan2(dv.Y, dv.X) * 180 / Math.PI;
                }
                pair = CreateRoads(uidoc, d, clickZ, results);
                if (pair == null) return results;
            }
            (main, cross) = pair.Value;
        }
        d.MainRoad = main.GroupId;
        d.CrossRoad = cross.GroupId;

        // 2) Refazendo: sai o que o nó criou antes (ramos, viaduto, rotatórias, recortes).
        Teardown(uidoc, d);
        main = RoadWorks.RoadByGroup(doc, d.MainRoad)!;
        cross = RoadWorks.RoadByGroup(doc, d.CrossRoad)!;

        // 3) Plano do nó.
        var pm = PlanRoad.Of(main.Pavement, main.Axis, main.BaseZ);
        var pc = PlanRoad.Of(cross.Pavement, cross.Axis, cross.BaseZ);
        var over = d.MainBelow ? cross : main;
        var plan = InterchangePlanner.Plan(d, pm, pc, click, d.OverBaseGrade);
        if (plan == null)
        {
            info.Warnings.Add("As vias escolhidas não se cruzam: estenda os eixos até o cruzamento e tente de novo.");
            return results;
        }
        info.Warnings.AddRange(plan.Warnings.Distinct());
        d.OverBaseGrade = plan.OverBase;
        d.Position = plan.Node;
        d.Z = main.BaseZ;

        // 4) A via de cima sobe (greide com gabarito) e ganha o viaduto esconso sobre a outra.
        results.AddRange(RoadWorks.SetGrade(uidoc, over.GroupId, plan.OverGrade).Where(r => r.Warnings.Count > 0));
        over = RoadWorks.RoadByGroup(doc, over.GroupId)!;
        var works = new List<MarkingDefinition>();
        var vt = UiHelpers.Remembered<BridgeDefinition>("infra:viaduto") ?? new BridgeDefinition();
        var viaduct = (BridgeDefinition)vt.CloneWithNewId();
        viaduct.Kind = TipoObraDeArte.Viaduto;
        viaduct.ProfileKind = PerfilObra.GreideDaVia;
        viaduct.Skew = plan.Skew;
        viaduct.PierStations = plan.PierStations.Select(x => x + plan.BridgeS0).ToList();
        viaduct.Water = false;
        viaduct.GirderDepth = Math.Max(1.0, d.DeckDepth - 0.45);
        viaduct.Lighting = d.Lighting;
        works.Add(RoadWorks.Host(viaduct, over.Pavement, plan.BridgeS0, plan.BridgeS1));

        // 5) Ramos: vias novas com eixo desenhado (spline), greide e seção de ramo; tapers recortados e pontes onde cruzam.
        var rampDefs = new List<MarkingDefinition>();
        foreach (var r in plan.Ramps)
        {
            try
            {
                var path = CreateAxis(uidoc, r.Axis, r.BaseZ);
                var setup = InterchangePlanner.RampSetup(r, d.Guardrails, 50, d.UrbanSection, d.SidewalkWidth);
                var (pav, created) = RoadWorks.CreateRoad(uidoc, setup, path, r.Grade, connect: false, name: r.Name);
                results.AddRange(created);
                if (pav?.GroupId == null) { info.Warnings.Add($"{r.Name}: a via do ramo não pôde ser criada."); continue; }
                d.Ramps.Add(new RampRecord
                {
                    Group = pav.GroupId, Name = r.Name, Role = r.Role, StartLink = r.StartLink, EndLink = r.EndLink, StartRoad = r.StartRoad, EndRoad = r.EndRoad,
                    LaneWidth = r.LaneWidth, Shoulder = r.Shoulder, Taper = r.Taper,
                });
                var members = RoadWorks.Members(doc, pav.GroupId);
                foreach (var mp in members.OfType<RoadPavementDefinition>())
                {
                    mp.MergeStart = r.StartLink == LigacaoRamo.Paralela;
                    mp.MergeEnd = r.EndLink == LigacaoRamo.Paralela;
                }
                var cuts = InterchangePlanner.TaperCuts(r);
                // Calçadas, meios-fios e sarjetas do ramo não entram na área das vias do nó (encostam no bordo delas).
                var strips = d.UrbanSection ? new[] { InterchangePlanner.RoadStrip(pm, plan.Node), InterchangePlanner.RoadStrip(pc, plan.Node) }
                    .Where(x => x != null).Cast<Polygon2>().ToList() : new List<Polygon2>();
                if (cuts.Count > 0 || strips.Count > 0)
                {
                    foreach (var m in members)
                    {
                        foreach (var cut in cuts) m.Exclusions.Add(new ExclusionZone { SourceId = d.Id + ":taper", Points = cut.Outer.ToList() });
                        if (Core.Generators.IntersectionGenerator.IsPhysical(m) || m is PlanterDefinition)
                            foreach (var st in strips) m.Exclusions.Add(new ExclusionZone { SourceId = d.Id + ":vias", Points = st.Outer.ToList() });
                    }
                    rampDefs.AddRange(members);
                }
                foreach (var (s0, s1) in r.Bridges)
                {
                    var rb = (BridgeDefinition)vt.CloneWithNewId();
                    rb.Kind = TipoObraDeArte.Viaduto;
                    rb.ProfileKind = PerfilObra.GreideDaVia;
                    rb.PierType = TipoPilar.Circular; rb.PierSize = 1.1; rb.SpanLength = 25; rb.PierStations = null; rb.Skew = 0; rb.Lighting = false; rb.Water = false;
                    works.Add(RoadWorks.Host(rb, pav, s0, s1));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Nó viário – {r.Name}", ex);
                info.Warnings.Add($"{r.Name}: {ex.Message}");
            }
        }
        if (rampDefs.Count > 0) results.AddRange(MarkingCreator.Commit(uidoc, rampDefs, "SV - Tapers dos ramos").Where(r => r.Warnings.Count > 0));
        results.AddRange(MarkingCreator.Commit(uidoc, works, "SV - Viadutos do nó").Where(r => r.Warnings.Count > 0));
        d.WorkIds.AddRange(works.Select(x => x.Id));

        // 6) Terminais: interseções em nível no eixo da transversal ou rotatórias (terminais e anel elevado).
        try
        {
            if (plan.Ramps.Any(r => r.StartLink == LigacaoRamo.Terminal || r.EndLink == LigacaoRamo.Terminal))
            {
                var template = IntersectionService.AutoTemplate(false);
                template.Control = ControleIntersecao.Pare;
                foreach (var node in plan.Ramps.SelectMany(r => new[] { r.StartLink == LigacaoRamo.Terminal ? r.Axis.Points[0] : (Vec2?)null,
                                 r.EndLink == LigacaoRamo.Terminal ? r.Axis.Points[^1] : null }).Where(p => p != null).Select(p => p!.Value)
                             .GroupBy(p => ((int)Math.Round(p.X / 5), (int)Math.Round(p.Y / 5))).Select(g => g.First()))
                    results.AddRange(IntersectionRunner.Run(uidoc, "SV - Terminais do nó", sv => sv.IntersectAll(template, node, 30)).Where(r => r.Warnings.Count > 0));
            }
            foreach (var (center, radius, z) in plan.Roundabouts)
            {
                var rt = (RoundaboutDefinition)(UiHelpers.Remembered<RoundaboutDefinition>("Rotatoria") ?? RoundaboutDefinition.Nova()).CloneWithNewId();
                rt.Lanes = d.Type == TipoNoViario.RotatoriaElevada ? 2 : 1;
                rt.LaneWidth = 5.0;
                rt.SidewalkWidth = 0;
                rt.Crosswalks = false;
                rt.IslandRadius = Math.Max(4, radius - rt.ApronWidth - rt.Lanes * rt.LaneWidth);
                RoundaboutDefinition? created = null;
                results.AddRange(IntersectionRunner.Run(uidoc, "SV - Rotatória do nó", sv => sv.AddRoundabout(center, z, rt, out created)).Where(r => r.Warnings.Count > 0));
                if (created != null) d.WorkIds.Add(created.Id);
                d.RoundaboutCenters.Add(center);
                d.RoundaboutData.Add(radius);
                d.RoundaboutData.Add(z);
            }
            d.RingDecks = plan.RingDecks.Select(a => new Vec2(a.A0, a.A1)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Nó viário – terminais", ex);
            info.Warnings.Add("Terminais do nó: " + ex.Message);
        }

        // 7) Calçadas, meios-fios, bordos e defensas das vias sob as faixas paralelas dos ramos são recortados.
        var joins = plan.Ramps.SelectMany(r => InterchangePlanner.JoinFootprints(r)).ToList();
        var groups = new HashSet<string> { d.MainRoad!, d.CrossRoad! };
        bool Cuttable(MarkingDefinition m) =>
            m.GroupId != null && groups.Contains(m.GroupId) && m is not RoadPavementDefinition
            && !(m is DeviceMarkingDefinition dev && Math.Abs(dev.Offset) < 1.0)
            && !(m is LinearMarkingDefinition l && Math.Abs(l.Offset) < 1.0);
        try { FootprintCutter.Apply(uidoc, d, joins, Cuttable, plan.Node, 2000); }
        catch (Exception ex) { Log.Error("Nó viário – recortes", ex); }

        // 8) Acabamentos do nó (zebrados, linhas, pórticos, iluminação, torres, grama dos laços) e terreno.
        results.AddRange(MarkingCreator.Commit(uidoc, new MarkingDefinition[] { d }, "SV - Nó viário").Where(r => r.Warnings.Count > 0));
        var shaped = new List<MarkingDefinition> { main.Pavement, cross.Pavement, d };
        shaped.AddRange(d.Ramps.Select(r => RoadWorks.RoadByGroup(doc, r.Group)?.Pavement).Where(p => p != null).Cast<MarkingDefinition>());
        shaped.AddRange(works);
        TerrainActions.AfterCreate(uidoc, shaped, quiet: true);
        info.Warnings.Insert(0, $"{PlanName(d.Type)}: {d.Ramps.Count} ramo(s) criados como vias, {works.Count} viaduto(s), {plan.Roundabouts.Count} rotatória(s). " +
                                "Cada ramo é uma via do plugin (Editar → a via inteira) e o nó inteiro se refaz em Editar.");
        return results;
    }

    private static string PlanName(TipoNoViario t) => t switch
    {
        TipoNoViario.DiamanteRotatorias => "Diamante com rotatórias",
        TipoNoViario.TrevoCompleto => "Trevo completo",
        TipoNoViario.TrevoParcial => "Trevo parcial",
        TipoNoViario.Trombeta => "Trombeta",
        TipoNoViario.RotatoriaElevada => "Rotatória em dois níveis",
        _ => "Diamante",
    };

    /// <summary>Par de vias do plugin cujos eixos se cruzam mais perto do clique (a principal é a de maior hierarquia/largura).</summary>
    private static (PickedRoad Main, PickedRoad Cross)? FindCrossing(Document doc, Vec2 click)
    {
        var roads = MarkingStorage.Definitions(doc).OfType<RoadPavementDefinition>().Where(p => p.GroupId != null)
            .Select(p => RoadWorks.Road(doc, p)).Where(r => r != null).Cast<PickedRoad>()
            .Where(r => Math.Abs(r.Axis.Project(click).Signed) < r.Pavement.TotalLeft + r.Pavement.TotalRight + 30).ToList();
        (PickedRoad, PickedRoad)? best = null;
        var bd = double.MaxValue;
        for (int i = 0; i < roads.Count; i++)
            for (int j = i + 1; j < roads.Count; j++)
                if (InterchangePlanner.Crossing(roads[i].Axis, roads[j].Axis, click) is { } x && x.DistanceTo(click) < bd)
                {
                    bd = x.DistanceTo(click);
                    best = (roads[i], roads[j]);
                }
        if (best is not { } b || bd > 60) return null;
        double Rank(PickedRoad r) => Hierarquia.Rank(r.Pavement.Hierarchy) * 1000 + r.Pavement.TotalLeft + r.Pavement.TotalRight;
        return Rank(b.Item1) >= Rank(b.Item2) ? (b.Item1, b.Item2) : (b.Item2, b.Item1);
    }

    /// <summary>Cria a principal e a transversal (vias do plugin) cruzando no ponto.</summary>
    private static (PickedRoad Main, PickedRoad Cross)? CreateRoads(UIDocument uidoc, InterchangeDefinition d, double z, List<RenderResult> results)
    {
        var doc = uidoc.Document;
        var (ms, cs) = InterchangeDemo.Setups(d);
        var (ma, ca) = InterchangeDemo.Axes(d);
        PickedRoad? Make(RoadSetup s, Polyline2 axis, string name)
        {
            PathReference path;
            using (var t = new Transaction(doc, $"SV - Eixo da {name}"))
            {
                t.Start();
                var styles = new StyleService(doc);
                var zf = UnitConv.Ft(z);
                var id = Picking.CreateLine(doc, uidoc.ActiveView, new XYZ(UnitConv.Ft(axis.Points[0].X), UnitConv.Ft(axis.Points[0].Y), zf),
                    new XYZ(UnitConv.Ft(axis.Points[^1].X), UnitConv.Ft(axis.Points[^1].Y), zf), styles);
                t.Commit();
                path = PathReference.FromElements(new[] { doc.GetElement(id).UniqueId });
            }
            var (pav, created) = RoadWorks.CreateRoad(uidoc, s, path, RoadGrade.Flat(axis.Length), connect: false, name: name);
            results.AddRange(created);
            return pav == null ? null : RoadWorks.Road(doc, pav);
        }
        var main = Make(ms, ma, "via principal");
        var cross = Make(cs, ca, "via transversal");
        return main == null || cross == null ? null : (main, cross);
    }

    /// <summary>Remove o que o nó criou antes (ramos, obras, rotatórias, recortes) – para refazê-lo.</summary>
    public static void Teardown(UIDocument uidoc, InterchangeDefinition d)
    {
        var doc = uidoc.Document;
        if (d.Ramps.Count == 0 && d.WorkIds.Count == 0) return;
        var all = MarkingStorage.Definitions(doc).ToList();
        var rampGroups = d.Ramps.Select(r => r.Group).ToHashSet();
        using (MarkingService.RenderScope())
        using (var t = new Transaction(doc, "SV - Refazer nó viário"))
        {
            t.Start();
            var service = new MarkingService(doc, uidoc.ActiveView);
            var sv = new IntersectionService(doc, service);
            foreach (var def in all)
            {
                var kill = def.GroupId != null && rampGroups.Contains(def.GroupId)
                           || d.WorkIds.Contains(def.Id)
                           || def is IHostedStructure { HostRoad: { } h } && rampGroups.Contains(h);
                if (!kill) continue;
                try
                {
                    if (def is RoundaboutDefinition rb) sv.Remove(rb);
                    else service.Delete(def.Id);
                }
                catch (Exception ex) { Log.Error("Refazer nó – remover", ex); }
            }
            // Eixos (linhas de modelo) dos ramos.
            foreach (var def in all.OfType<RoadPavementDefinition>().Where(p => p.GroupId != null && rampGroups.Contains(p.GroupId)))
                foreach (var uid in def.PathRef.ElementIds)
                    if (doc.GetElement(uid) is { } e) try { doc.Delete(e.Id); } catch { /* já removida */ }
            // Interseções e rotatórias nos terminais que dependiam dos ramos.
            foreach (var it in MarkingStorage.Definitions(doc).OfType<IntersectionDefinition>().Where(i => i.Node.DistanceTo(d.Position) < 900).ToList())
                try { sv.Refresh(it); } catch { /* removida */ }
            t.Commit();
        }
        FootprintCutter.Apply(uidoc, d, Array.Empty<Polygon2>(), _ => false);
        d.Ramps.Clear();
        d.WorkIds.Clear();
        d.RoundaboutCenters.Clear();
        d.RoundaboutData.Clear();
        d.RingDecks.Clear();
    }

    /// <summary>
    /// Eixo do ramo como linha de modelo: spline suave pelos pontos do traçado (a cada ~6 m); se o Revit recusar, trechos retos.
    /// </summary>
    private static PathReference CreateAxis(UIDocument uidoc, Polyline2 axis, double z)
    {
        var doc = uidoc.Document;
        using var t = new Transaction(doc, "SV - Eixo do ramo");
        t.Start();
        var styles = new StyleService(doc);
        var zf = UnitConv.Ft(z);
        var pts = CurveTools.Densify(axis.Points, 50).ToList();
        var sampled = new List<Vec2>();
        var L = axis.Length;
        var n = Math.Max(2, (int)Math.Ceiling(L / 6));
        for (int i = 0; i <= n; i++) sampled.Add(axis.PointAt(L * i / n));
        _ = pts;
        var ids = new List<ElementId>();
        try
        {
            var xyz = sampled.Select(p => new XYZ(UnitConv.Ft(p.X), UnitConv.Ft(p.Y), zf)).ToList();
            var spline = HermiteSpline.Create(xyz, false);
            var sp = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, xyz[0]));
            var mc = doc.Create.NewModelCurve(spline, sp);
            try { mc.LineStyle = styles.AxisLineStyle(); } catch { /* opcional */ }
            ids.Add(mc.Id);
        }
        catch (Exception ex)
        {
            Log.Error("Eixo do ramo (spline)", ex);
            for (int i = 0; i + 1 < sampled.Count; i++)
                ids.Add(Picking.CreateLine(doc, uidoc.ActiveView, new XYZ(UnitConv.Ft(sampled[i].X), UnitConv.Ft(sampled[i].Y), zf),
                    new XYZ(UnitConv.Ft(sampled[i + 1].X), UnitConv.Ft(sampled[i + 1].Y), zf), styles));
        }
        t.Commit();
        return PathReference.FromElements(ids.Select(id => doc.GetElement(id).UniqueId));
    }
}
