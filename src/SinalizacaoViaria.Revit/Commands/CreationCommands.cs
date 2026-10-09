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
/// Cria a via a partir do eixo (selecionado ou desenhado). Desenhando, os pontos se encaixam nas vias existentes e a
/// via nova é ligada ao sistema viário: interseções ou rotatórias nos encontros e cul-de-sac nas pontas livres.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class CmdSinalizarVia : CommandBase
{
    protected virtual bool DrawByDefault => false;

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var w = new RoadWindow(DrawByDefault || PluginContext.Settings.LastDrawRoad);
        if (UiHelpers.ShowModal(w) != true || w.Setup == null || w.OutputSettings == null) return Result.Cancelled;
        PluginContext.Settings.LastDrawRoad = w.DrawPath;
        PluginContext.SaveSettings();
        EnsureDetailView(uidoc, w.OutputSettings);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) w.OutputSettings.SurfaceIds = s;

        var snapped = new List<string>();
        // Curvas desenhadas com raio menor que a meia largura deixariam a borda interna "dobrada": o raio é limitado.
        var axisRadius = Math.Max(w.CurveRadius, RoadSetup.MinAxisRadius(w.Setup.MaxHalfWidth));
        var results0 = new List<RenderResult>();
        // Pisos existentes: eixos e larguras reconhecidos no contorno; cada via recebe a seção ajustada à sua largura.
        Dictionary<PathReference, Core.Automation.FloorRoad>? floorRoads = null;
        List<PathReference>? paths;
        if (w.FromFloors)
        {
            var found = FloorRoadInput.Pick(uidoc, out var floorWarnings);
            if (found == null) return Result.Cancelled;
            if (found.Count == 0)
            {
                UiHelpers.Error("Nenhuma pista reconhecida nos pisos escolhidos." + (floorWarnings.Count > 0 ? "\n\n" + string.Join("\n", floorWarnings) : ""));
                return Result.Cancelled;
            }
            floorRoads = found.ToDictionary(x => x.Path, x => x.Road);
            paths = found.Select(x => x.Path).ToList();
            if (floorWarnings.Count > 0) { var rr = new RenderResult(); rr.Warnings.AddRange(floorWarnings); results0.Add(rr); }
        }
        else paths = RoadAxisInput.GetMany(uidoc, w.DrawPath, w.Snap, axisRadius, snapped);
        if (paths == null || paths.Count == 0) return Result.Cancelled;
        var opt = new RoadCreation(w.AutoIntersect, w.Connection, w.FreeEnds, w.IntersectionCrosswalks, w.CornerRadius, w.IntersectionRamps && w.IntersectionCrosswalks,
            w.Relief, w.OutputSettings);
        var results = new List<RenderResult>(results0);
        var many = paths.Count > 1;
        if (many && (w.Setup.LargurasVariaveis.Count > 0 || w.Setup.Recuos.Count > 0))
        {
            // Estacas de largura variável e recuos valem para UM eixo: com várias vias, ficam para a edição de cada uma.
            w.Setup.LargurasVariaveis.Clear();
            w.Setup.Recuos.Clear();
            results.Add(new RenderResult());
            results[^1].Warnings.Add($"{paths.Count} vias criadas das linhas selecionadas: a largura variável e os recuos não foram aplicados (valem para um eixo só) – use Editar em cada via.");
        }
        else if (many)
        {
            results.Add(new RenderResult());
            results[^1].Warnings.Add($"As linhas selecionadas formam {paths.Count} vias (encontros em T/cruz e ruas que seguem retas nos nós) – as interseções foram criadas entre elas.");
        }
        foreach (var path in paths)
        {
            // Eixo resolvido: as estacas da largura variável ficam amarradas a pontos do eixo (sobrevivem a prolongamentos).
            var axis = PathResolver.Resolve(doc, path)?.Main;
            if (!many && w.ReadSurvey && axis != null)
            {
                var survey = WidthSurvey.Read(uidoc, axis);
                if (survey.Count > 0) w.Setup.LargurasVariaveis = survey;
            }
            // Pista vazia no mesmo eixo: recebe a seção (a pista é refeita com os elementos, sem duplicar o pavimento).
            if (floorRoads == null && EmptyRoadway(doc, path) is { } emptyPav)
            {
                var fill = w.Setup.Clone();
                fill.SoPavimento = false;
                if (fill.LargurasVariaveis.Count == 0 && RoadTemplates.FromJson(emptyPav.SetupJson) is { } old) fill.LargurasVariaveis = old.LargurasVariaveis;
                try { results.AddRange(RoadAccessCommand.Regenerate(uidoc, emptyPav, fill)); }
                catch (Exception ex)
                {
                    Log.Error("Via sobre pista vazia", ex);
                    results.Add(new RenderResult());
                    results[^1].Warnings.Add("A pista vazia não pôde receber a seção: " + ex.Message);
                }
                continue;
            }
            List<MarkingDefinition> defs;
            if (floorRoads != null && floorRoads.TryGetValue(path, out var fr))
            {
                var fitted = Core.Automation.FloorRoads.Fit(w.Setup, fr);
                defs = w.BuildDefinitions(fitted, path, axis);
            }
            else defs = w.BuildDefinitions(path, axis);
            RoadSetup.ApplyAxisRadius(defs, w.CurveRadius);
            try { results.AddRange(Create(uidoc, defs, opt, w.Setup.Warnings)); }
            catch (Exception ex)
            {
                Log.Error("Via (várias linhas)", ex);
                results.Add(new RenderResult());
                results[^1].Warnings.Add("Uma das vias não pôde ser criada: " + ex.Message);
            }
        }
        if (snapped.Count > 0 && results.Count > 0)
            results[0].Warnings.Insert(0, "Conexões: " + string.Join("; ", snapped.Distinct()) + ".");
        Report("Via", results);
        return Result.Succeeded;
    }

    /// <summary>Pista vazia (ferramenta Pista, só pavimento) sobre o mesmo eixo, se houver.</summary>
    private static RoadPavementDefinition? EmptyRoadway(Document doc, PathReference path)
    {
        var key = RoadSectionInference.PathKey(path);
        return MarkingStorage.Definitions(doc).OfType<RoadPavementDefinition>()
            .FirstOrDefault(p => RoadSectionInference.PathKey(p.PathRef) == key && RoadTemplates.FromJson(p.SetupJson)?.SoPavimento == true);
    }

    /// <summary>Opções da criação de uma via (as mesmas da janela Via).</summary>
    internal sealed record RoadCreation(bool AutoIntersect, TipoConexao Connection, FimLivre FreeEnds, bool Crosswalks, double? CornerRadius, bool Ramps,
        RelevoVia Relief, OutputSettings Output);

    /// <summary>
    /// Gera uma via já montada (definições da seção): pista, conexões às vias existentes, elementos junto ao bordo e o relevo.
    /// </summary>
    internal static List<RenderResult> Create(UIDocument uidoc, List<MarkingDefinition> defs, RoadCreation w, IReadOnlyList<string>? setupWarnings = null)
    {
        // Mesma sequência da ferramenta Pista: primeiro a pista (pavimento, linhas, dispositivos), depois as conexões e, por fim,
        // calçadas, meios-fios, sarjetas e canteiros junto ao bordo – assim uma falha num elemento externo não impede a conexão.
        var outer = defs.Where(IsEdgeElement).ToList();
        var core = defs.Where(d => !outer.Contains(d)).ToList();
        var results = MarkingCreator.Commit(uidoc, core, "SV - Via");
        if (setupWarnings is { Count: > 0 } && results.Count > 0) results[0].Warnings.InsertRange(0, setupWarnings);
        if (defs.OfType<RoadPavementDefinition>().FirstOrDefault() is { } pav && (w.AutoIntersect || w.FreeEnds != Core.Automation.FimLivre.Nenhum))
        {
            var template = IntersectionService.AutoTemplate(w.Crosswalks);
            template.CornerRadius = w.CornerRadius ?? template.CornerRadius;
            template.Ramps = w.Ramps;
            template.Output = w.Output.Clone();
            var rb = UiHelpers.Remembered<RoundaboutDefinition>("Rotatoria") ?? RoundaboutDefinition.Nova();
            var cds = UiHelpers.Remembered<CulDeSacDefinition>(nameof(CulDeSacDefinition)) ?? new CulDeSacDefinition();
            try
            {
                var extra = IntersectionRunner.Run(uidoc, "SV - Conexões da via", sv =>
                    sv.Connect(pav, w.Connection, w.FreeEnds, template, rb, cds, radiusByHierarchy: true));
                results.AddRange(extra.Where(r => r.Warnings.Count > 0));
            }
            catch (Exception ex)
            {
                Log.Error("Conexões da via", ex);
                results.Add(new RenderResult { Geometry = null });
                results[^1].Warnings.Add("Não foi possível ligar a via às vias existentes: " + ex.Message);
            }
        }
        if (outer.Count > 0)
        {
            try
            {
                results.AddRange(MarkingCreator.Commit(uidoc, outer, "SV - Via (calçadas, meios-fios e canteiros)"));
                results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões da via", sv => sv.RefreshDependents(outer)).Where(r => r.Warnings.Count > 0));
            }
            catch (Exception ex)
            {
                Log.Error("Elementos junto ao bordo da via", ex);
                results.Add(new RenderResult { Geometry = null });
                results[^1].Warnings.Add("Calçadas/meios-fios da via não puderam ser criados: " + ex.Message);
            }
        }
        // Relevo: com Toposolid sob a via, o greide segue o terreno e o terreno é cortado/aterrado (plataforma e taludes).
        if (w.Relief != Core.Automation.RelevoVia.Plana && defs.OfType<RoadPavementDefinition>().FirstOrDefault() is { } rp)
        {
            try { results.AddRange(RoadWorks.ApplyRelief(uidoc, rp, w.Relief).Where(r => r.Warnings.Count > 0)); }
            catch (Exception ex)
            {
                Log.Error("Relevo da via", ex);
                results.Add(new RenderResult());
                results[^1].Warnings.Add("Não foi possível ajustar a via ao terreno: " + ex.Message);
            }
        }
        return results;
    }

    /// <summary>Elementos da seção fora da pista dos veículos (o que a ferramenta Pista deixa para "junto ao bordo").</summary>
    private static bool IsEdgeElement(MarkingDefinition d) =>
        d is PlanterDefinition || IntersectionGenerator.IsPhysical(d);
}

/// <summary>Obtém o eixo de uma via: desenhado por pontos (com encaixe e curvas) ou linhas selecionadas.</summary>
/// <summary>
/// Pistas modeladas com Piso comum: lê o contorno dos pisos escolhidos (esboço ou face superior), reconhece eixos,
/// larguras e encontros (núcleo: <see cref="Core.Automation.FloorRoads"/>) e cria as linhas de eixo de cada via.
/// </summary>
internal static class FloorRoadInput
{
    private sealed class FloorFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Floor || e.Category?.Id.Value == (long)BuiltInCategory.OST_Floors ||
                                                e.GetType().Name == "Toposolid";
        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    public static List<(PathReference Path, Core.Automation.FloorRoad Road)>? Pick(UIDocument uidoc, out List<string> warnings)
    {
        warnings = new List<string>();
        var doc = uidoc.Document;
        IList<Reference> refs;
        try { refs = uidoc.Selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Element, new FloorFilter(), "Selecione os PISOS da pista (um ou vários) e clique em Concluir"); }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        return FromElements(uidoc, refs.Select(r => doc.GetElement(r)).ToList(), true, warnings);
    }

    /// <summary>Reconhece as vias nos pisos dados e cria os eixos (<paramref name="confirm"/> = perguntar antes).</summary>
    public static List<(PathReference Path, Core.Automation.FloorRoad Road)>? FromElements(UIDocument uidoc, List<Element> elements, bool confirm, List<string> warnings)
    {
        var doc = uidoc.Document;
        var shapes = new List<Polygon2>();
        var zs = new List<double>();
        foreach (var e in elements)
        {
            var loops = Outline(doc, e);
            if (loops.Count == 0) { warnings.Add($"Piso {e.Id}: contorno não lido."); continue; }
            shapes.AddRange(Classify(loops));
            if (e.get_BoundingBox(null) is { } bb) zs.Add(bb.Max.Z);
        }
        if (shapes.Count == 0) return new List<(PathReference, Core.Automation.FloorRoad)>();
        var net = Core.Automation.FloorRoads.Read(shapes);
        warnings.AddRange(net.Warnings);
        if (net.Roads.Count == 0) return new List<(PathReference, Core.Automation.FloorRoad)>();
        var summary = string.Join("\n", net.Roads.Select((x, i) => $"Via {i + 1}: {x.Axis.Length:0} m, pista de {x.Width:0.00} m" + (x.VariableWidth ? " (largura variável)" : "")));
        var td = new TaskDialog("SinalizaBIM – pisos existentes")
        {
            MainInstruction = $"{net.Roads.Count} via(s) e {net.Junctions.Count} encontro(s) reconhecidos",
            MainContent = summary + "\n\nAs linhas de eixo serão criadas (dá para ajustá-las depois como qualquer eixo) e cada via recebe a seção do modelo escolhido com a largura medida.",
            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
        };
        if (confirm && td.Show() != TaskDialogResult.Ok) return null;
        var z = zs.Count > 0 ? zs.Max() : 0;
        var res = new List<(PathReference, Core.Automation.FloorRoad)>();
        using var t = new Transaction(doc, "SV - Eixos dos pisos existentes");
        t.Start();
        foreach (var road in net.Roads)
        {
            var pts = road.Axis.Points;
            var pieces = new List<Core.Automation.RoadConnection.Piece>();
            for (int k = 1; k < pts.Count; k++) if (pts[k - 1].DistanceTo(pts[k]) > 0.01) pieces.Add(new Core.Automation.RoadConnection.Piece(pts[k - 1], pts[k], null));
            if (pieces.Count == 0) continue;
            var ids = Picking.CreateAxis(doc, uidoc.ActiveView, pieces, z);
            res.Add((PathReference.FromElements(ids.Select(id => doc.GetElement(id).UniqueId)), road));
        }
        t.Commit();
        return res;
    }

    /// <summary>Anéis do contorno do piso (m): do esboço (exato) ou das faces superiores.</summary>
    private static List<List<Vec2>> Outline(Document doc, Element e)
    {
        var loops = new List<List<Vec2>>();
        try
        {
            if (e is Floor f && doc.GetElement(f.SketchId) is Sketch sk)
                foreach (CurveArray arr in sk.Profile)
                {
                    var loop = new List<Vec2>();
                    foreach (Curve c in arr)
                    {
                        var tp = c.Tessellate();
                        for (int i = 0; i < tp.Count - 1; i++) loop.Add(UnitConv.ToVec2(tp[i]));
                    }
                    if (loop.Count >= 3) loops.Add(loop);
                }
        }
        catch { /* sem esboço: pela face */ }
        if (loops.Count > 0) return loops;
        try
        {
            var faces = e is HostObject ho ? HostObjectUtils.GetTopFaces(ho) : new List<Reference>();
            foreach (var fr in faces)
                if (e.GetGeometryObjectFromReference(fr) is Face face)
                    foreach (EdgeArray ea in face.EdgeLoops)
                    {
                        var loop = new List<Vec2>();
                        foreach (Edge edge in ea)
                        {
                            var tp = edge.Tessellate();
                            for (int i = 0; i < tp.Count - 1; i++) loop.Add(UnitConv.ToVec2(tp[i]));
                        }
                        if (loop.Count >= 3) loops.Add(loop);
                    }
        }
        catch { /* sem geometria legível */ }
        return loops;
    }

    /// <summary>Anéis → polígonos: o anel contido em outro é furo dele (canteiros, ilhas).</summary>
    private static List<Polygon2> Classify(List<List<Vec2>> loops)
    {
        var ordered = loops.OrderByDescending(l => Math.Abs(Polygon2.SignedArea(l))).ToList();
        var outers = new List<(List<Vec2> Outer, List<List<Vec2>> Holes)>();
        foreach (var l in ordered)
        {
            var host = outers.FirstOrDefault(o => Polygon2.PointInRing(o.Outer, l[0]) && !o.Holes.Any(h => Polygon2.PointInRing(h, l[0])));
            if (host.Outer != null) host.Holes.Add(l);
            else outers.Add((l, new List<List<Vec2>>()));
        }
        return outers.Select(o => new Polygon2(o.Outer, o.Holes)).Where(p => p.IsValid).ToList();
    }
}

internal static class RoadAxisInput
{
    /// <summary>
    /// Eixos de uma ou várias vias: as linhas selecionadas são separadas em vias (encontros em T/cruz, ruas que seguem
    /// retas nos nós, curvas entre duas linhas) – cada grupo vira uma via e as interseções nascem nos encontros.
    /// </summary>
    public static List<PathReference>? GetMany(UIDocument uidoc, bool draw, bool snap, double curveRadius, List<string> snapped)
    {
        if (draw)
        {
            var one = Get(uidoc, true, snap, curveRadius, snapped);
            return one == null ? null : new List<PathReference> { one };
        }
        var doc = uidoc.Document;
        var curves = Picking.PickCurves(uidoc, "Selecione as linhas do(s) EIXO(S) – uma via ou uma malha inteira – e clique em Concluir");
        if (curves == null || curves.Count == 0) return null;
        using (var t = new Transaction(doc, "SV - Estilo de eixo"))
        {
            t.Start();
            var styles = new StyleService(doc);
            foreach (var c in curves) try { c.LineStyle = styles.AxisLineStyle(); } catch { /* opcional */ }
            t.Commit();
        }
        var pts = curves.Select(c =>
        {
            try { return (IReadOnlyList<Vec2>)c.GeometryCurve.Tessellate().Select(UnitConv.ToVec2).ToList(); }
            catch { return (IReadOnlyList<Vec2>)new List<Vec2>(); }
        }).ToList();
        var groups = RoadChains.Split(pts, 0.05);
        return groups.Where(g => g.Count > 0).Select(g => PathReference.FromElements(g.Select(i => curves[i].UniqueId))).ToList();
    }

    public static PathReference? Get(UIDocument uidoc, bool draw, bool snap, double curveRadius, List<string> snapped)
    {
        var doc = uidoc.Document;
        if (!draw)
        {
            var curves = Picking.PickCurves(uidoc, "Selecione as linhas do EIXO da via (no sentido de referência) e clique em Concluir");
            if (curves == null) return null;
            // As linhas escolhidas passam a usar o estilo de eixo (identificação visual).
            using (var t = new Transaction(doc, "SV - Estilo de eixo"))
            {
                t.Start();
                var styles = new StyleService(doc);
                foreach (var c in curves) try { c.LineStyle = styles.AxisLineStyle(); } catch { /* opcional */ }
                t.Commit();
            }
            return PathReference.FromElements(curves.Select(c => c.UniqueId));
        }
        var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        var existing = snap ? svc.Roads() : new List<IntersectionRoad>();
        var rbs = snap ? MarkingStorage.Definitions(doc).OfType<RoundaboutDefinition>().Select(r => (r.Center, r.OuterRadius)).ToList() : new();
        (XYZ, string?) Snap(XYZ p)
        {
            if (!snap) return (p, null);
            var sn = RoadConnection.SnapPoint(UnitConv.ToVec2(p), existing, 4.0, rbs);
            return sn.Kind == TipoEncaixe.Livre ? (p, null) : (new XYZ(UnitConv.Ft(sn.Point.X), UnitConv.Ft(sn.Point.Y), p.Z), sn.Describe);
        }
        var picked = Picking.PickRoadAxis(uidoc, Snap);
        if (picked == null) return null;
        var (pts, info) = picked.Value;
        snapped.AddRange(info.Where(i => i != null)!);
        var pieces = RoadConnection.Fillet(pts.Select(UnitConv.ToVec2).ToList(), curveRadius);
        List<ElementId> ids;
        using (var t = new Transaction(doc, "SV - Eixo da via"))
        {
            t.Start();
            ids = Picking.CreateAxis(doc, uidoc.ActiveView, pieces, pts[0].Z);
            t.Commit();
        }
        return PathReference.FromElements(ids.Select(id => doc.GetElement(id).UniqueId));
    }
}

/// <summary>
/// Pista: só a parte dos veículos (asfalto, bloquete ou concreto), já conectada às vias existentes. Depois a via é
/// montada passo a passo com Meio-fio / Calçada / Sarjeta no modo "Junto ao bordo de uma via".
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdPista : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var st = PluginContext.Settings;
        var d = UiHelpers.Remembered<RoadPavementDefinition>("Pista") ?? new RoadPavementDefinition { Hierarchy = HierarquiaViaria.Local, Output = st.NewOutput() };
        var h = d.Hierarchy ?? HierarquiaViaria.Local;
        var center = st.Get("pista:eixo") ?? "LFO-2";
        var edges = st.Get("pista:bordos") == "1";
        var draw = st.LastDrawRoad;
        var snap = true;
        var radius = st.LastCurveRadius;
        var connect = st.Get("pista:conectar") != "0";
        var crosswalks = st.AutoCrosswalks;
        var survey = false;
        var empty = st.Get("pista:vazia") == "1";
        var w = new FormWindow("Pista", "Pista (parte dos veículos)",
                "Cria só o pavimento da pista, já ligado às vias existentes. Depois monte a via elemento por elemento com " +
                "Meio-fio e Sarjeta / Calçadas no modo \"Junto ao bordo de uma via\" – eles passam a fazer parte da via e das conexões.",
                d, () =>
                {
                    var c = (RoadPavementDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                    c.Hierarchy = h;
                    var axis = new Core.Geometry.Polyline2(new[] { new Core.Geometry.Vec2(0, 0), new Core.Geometry.Vec2(40, 0) });
                    var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
                    var geo = new Core.Model.MarkingGeometry();
                    var sample = PathReference.FromPoints(axis.Points, 0);
                    var defs = empty
                        ? EmptySetup(c, h).Build(sample, new OutputSettings(), PluginContext.Catalog, axis: axis)
                        : RoadConnection.BuildCarriageway(c, sample, new OutputSettings(), center == "-" ? null : center, edges, Hierarquia.DefaultSpeed(h));
                    foreach (var m in defs) geo.Merge(MarkingBuilder.Build(m, axis, ctx));
                    return new FormPreview(geo, null, new[] { axis.Points }, $"Largura da pista: {UiHelpers.F(c.LeftWidth + c.RightWidth)} m");
                }, true, "Criar", 1000, 700)
            .Choice("Conteúdo", new[]
                {
                    ("Só pavimento – pista vazia (sem pintura, meio-fio ou calçada)", true),
                    ("Pavimento + linhas de eixo e de bordo", false),
                }, () => empty, v => empty = v,
                tooltip: "Só pavimento: a superfície com largura fixa ou variável. Os elementos entram depois – Sinalizar Via sobre o mesmo eixo ou Editar a seção da via.")
            .Choice("Hierarquia viária (CTB art. 60)", Hierarquia.Definidas.Select(x => (Hierarquia.Label(x), x)), () => h, v => h = v)
            .Choice("Pavimento", new[] { ("Asfalto (CBUQ)", TipoPavimento.Asfalto), ("Bloquete / intertravado", TipoPavimento.Bloquete), ("Concreto", TipoPavimento.Concreto),
                    ("Terra (leito natural – sem pintura)", TipoPavimento.Terra) },
                () => d.Material, v => d.Material = v)
            .Number("Largura à direita do eixo (m)", () => d.RightWidth, v => d.RightWidth = v, 1, 30)
            .Number("Largura à esquerda do eixo (m)", () => d.LeftWidth, v => d.LeftWidth = v, 0, 30)
            .Check("Mão dupla", () => d.TwoWay, v => d.TwoWay = v)
            .Choice("Linha de eixo", new[] { ("LFO-2 – seccionada", "LFO-2"), ("LFO-1 – contínua", "LFO-1"), ("LFO-3 – dupla contínua", "LFO-3"), ("Sem linha", "-") },
                () => center, v => center = v)
            .Check("Linhas de bordo (LBO)", () => edges, v => edges = v)
            .Section("Caminho e conexões")
            .Number("Raio das esquinas (m, 0 = pela hierarquia)", () => d.CornerRadius ?? 0, v => d.CornerRadius = v > 0.01 ? v : null, 0, 60,
                tooltip: "Raio na face do meio-fio das esquinas criadas quando esta via encontra outra. 0 = 6 m local, 8 m coletora, 10 m arterial, 15 m rodovia.")
            .Choice("Eixo", new[] { ("Desenhar por pontos (encaixa nas vias existentes)", true), ("Selecionar linhas existentes", false) }, () => draw, v => draw = v)
            .Number("Raio das curvas ao desenhar (m)", () => radius, v => radius = v, 0, 5000)
            .Check("Conectar às vias existentes (interseção simples)", () => connect, v => connect = v)
            .Check("Faixas de pedestres nas interseções", () => crosswalks, v => crosswalks = v)
            .Check("Largura variável: ler os bordos existentes do desenho depois do eixo (linhas do levantamento)", () => survey, v => survey = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        d.Hierarchy = h;
        UiHelpers.Remember("Pista", d);
        st.Set("pista:eixo", center);
        st.Set("pista:bordos", edges ? "1" : "0");
        st.Set("pista:conectar", connect ? "1" : "0");
        st.Set("pista:vazia", empty ? "1" : "0");
        st.LastDrawRoad = draw;
        st.LastCurveRadius = radius;
        PluginContext.SaveSettings();
        var output = d.Output;
        EnsureDetailView(uidoc, output);

        var snapped = new List<string>();
        var paths = RoadAxisInput.GetMany(uidoc, draw, snap, Math.Max(radius, RoadSetup.MinAxisRadius(Math.Max(d.LeftWidth, d.RightWidth))), snapped);
        if (paths == null || paths.Count == 0) return Result.Cancelled;
        var results = new List<RenderResult>();
        foreach (var path in paths)
        {
            List<MarkingDefinition> defs;
            if (empty)
            {
                // Pista vazia: a seção (faixas da largura desenhada) fica guardada no pavimento para a edição posterior.
                var setup = EmptySetup(d, h);
                var main = PathResolver.Resolve(uidoc.Document, path)?.Main;
                if (survey && paths.Count == 1 && main != null)
                {
                    var pts = WidthSurvey.Read(uidoc, main);
                    if (pts.Count > 0) setup.LargurasVariaveis = pts;
                }
                defs = setup.Build(path, output, PluginContext.Catalog, axis: main);
            }
            else
            {
                defs = RoadConnection.BuildCarriageway(d, path, output, center == "-" ? null : center, edges, Hierarquia.DefaultSpeed(h));
                if (survey && paths.Count == 1 && PathResolver.Resolve(uidoc.Document, path)?.Main is { } axis)
                {
                    var pts = WidthSurvey.Read(uidoc, axis);
                    if (pts.Count > 0) RoadConnection.ApplySurvey(defs, pts, axis, smooth: false);
                }
            }
            RoadSetup.ApplyAxisRadius(defs, radius);
            results.AddRange(MarkingCreator.Commit(uidoc, defs, "SV - Pista"));
            if (connect && defs.OfType<RoadPavementDefinition>().FirstOrDefault() is { } pav)
            {
                var template = IntersectionService.AutoTemplate(crosswalks && !empty);
                if (empty) template.SemSinalizacao();
                results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões da pista", sv =>
                    sv.Connect(pav, TipoConexao.Intersecao, FimLivre.Nenhum, template, new RoundaboutDefinition(), new CulDeSacDefinition(), radiusByHierarchy: true))
                    .Where(r => r.Warnings.Count > 0));
            }
        }
        Report("Pista", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }

    /// <summary>Seção da pista vazia com as medidas da janela.</summary>
    private static RoadSetup EmptySetup(RoadPavementDefinition d, HierarquiaViaria h)
    {
        var s = RoadSetup.PistaVazia(d.RightWidth, d.LeftWidth, d.TwoWay, d.Material, h, d.CornerRadius);
        s.PavementThickness = d.Thickness;
        return s;
    }
}

/// <summary>Nova via desenhada por pontos, conectada naturalmente às vias existentes.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdNovaVia : CmdSinalizarVia
{
    protected override bool DrawByDefault => true;
}

/// <summary>
/// Desenha eixos/caminhos: com a ferramenta nativa "Linha de modelo" do Revit (reta, arco, spline, cadeia, deslocamento,
/// retângulo... com todos os snaps) ou por pontos com encaixe nas vias existentes e curvas concordadas.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdDesenharEixo : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var st = PluginContext.Settings;
        var native = st.Get("eixo:modo") != "pontos";
        var radius = st.LastCurveRadius;
        var w = new FormWindow("Desenhar eixo", "Desenhar eixo / caminho",
                "Os eixos são linhas de modelo comuns: selecione-os depois no Sinalizar Via, na Pista ou em qualquer ferramenta. " +
                "Editar o eixo (alças, Mover, Deslocar) atualiza a sinalização e as conexões automaticamente.",
                null, null, false, "Desenhar", 620, 320)
            .Choice("Forma de desenho", new[]
                {
                    ("Ferramenta nativa do Revit – reta, arco, spline, cadeia, deslocamento e snaps", true),
                    ("Por pontos – encaixa nas vias existentes e concorda as curvas", false),
                }, () => native, v => native = v)
            .Number("Raio das curvas (por pontos, m)", () => radius, v => radius = v, 0, 5000);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        st.Set("eixo:modo", native ? "nativo" : "pontos");
        st.LastCurveRadius = radius;
        PluginContext.SaveSettings();

        if (native)
        {
            // Garante o estilo "SV - Eixo" para ser escolhido na lista de estilos de linha da ferramenta nativa.
            using (var t = new Transaction(doc, "SV - Estilo de eixo"))
            {
                t.Start();
                new StyleService(doc).AxisLineStyle();
                t.Commit();
            }
            var id = RevitCommandId.LookupPostableCommandId(PostableCommand.ModelLine);
            if (id == null || !app.CanPostCommand(id))
            {
                TaskDialog.Show(AppTitle, "Não foi possível abrir a ferramenta Linha de modelo nesta vista – use uma vista em planta.");
                return Result.Cancelled;
            }
            TaskDialog.Show(AppTitle, "A ferramenta Linha de modelo do Revit será aberta: escolha a forma (reta, arco, spline, cadeia...) e, " +
                                      $"em Estilo de linha, \"{StyleService.AxisLineStyleName}\" (opcional).\n\nDepois use Sinalizar Via ou Pista → Selecionar linhas existentes.");
            app.PostCommand(id);
            return Result.Succeeded;
        }
        var path = RoadAxisInput.Get(uidoc, true, true, radius, new List<string>());
        if (path == null) return Result.Cancelled;
        uidoc.Selection.SetElementIds(path.ElementIds.Select(u => doc.GetElement(u)?.Id).Where(i => i != null).Cast<ElementId>().ToList());
        return Result.Succeeded;
    }
}

/// <summary>Faixa de pedestres com linhas de retenção.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdFaixaPedestres : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new CrosswalkWindow();
        if (UiHelpers.ShowModal(w) != true || w.Setup == null || w.OutputSettings == null) return Result.Cancelled;
        EnsureDetailView(uidoc, w.OutputSettings);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) w.OutputSettings.SurfaceIds = s;

        var all = new List<RenderResult>();
        while (true)
        {
            var a = Picking.PickPoint(uidoc, "Faixa de pedestres: clique o bordo A da pista (ESC encerra)");
            if (a == null) break;
            var b = Picking.PickPoint(uidoc, "Faixa de pedestres: clique o bordo B (lado oposto)");
            if (b == null) break;
            var (pts, z) = Picking.ToCore(new[] { a, b });
            var defs = w.BuildDefinitions(w.Setup, w.OutputSettings, pts[0], pts[1], z);
            all.AddRange(MarkingCreator.Commit(uidoc, defs, "SV - Faixa de pedestres"));
            foreach (var d in defs.Where(d => d.Overlay)) FootprintCutter.ApplyOverlay(uidoc, d);
        }
        if (all.Count == 0) return Result.Cancelled;
        Report("Faixa de pedestres", all);
        return Result.Succeeded;
    }
}

/// <summary>Zebrados e marcações de área.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdZebrado : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => RunWith(uidoc, null);

    internal static Result RunWith(UIDocument uidoc, string? initialCode)
    {
        var w = new HatchWindow(null, initialCode);
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        var def = w.Result;
        EnsureDetailView(uidoc, def.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) def.Output.SurfaceIds = s;

        var boundary = MarkingCreator.PickPath(uidoc, w.PathMode, def.IsStrip ? "Caminho da faixa zebrada (linhas ou contorno)" : "Contorno FECHADO do zebrado (pode ter mais de um: o interno vira furo)", closed: !def.IsStrip);
        if (boundary == null) return Result.Cancelled;
        // Faixa: o caminho pode ser aberto ou fechado (anel); área: sempre fechada.
        boundary.Closed = !def.IsStrip;
        def.Boundary = boundary;

        if (w.PickReferenceDirection)
        {
            var p1 = Picking.PickPoint(uidoc, "Direção do tráfego: clique o 1º ponto (também define o eixo do chevron)");
            var p2 = p1 == null ? null : Picking.PickPoint(uidoc, "Direção do tráfego: clique o 2º ponto");
            if (p1 != null && p2 != null)
            {
                def.ReferenceDirection = (UnitConv.ToVec2(p2) - UnitConv.ToVec2(p1)).Normalized();
                def.AxisPoint = UnitConv.ToVec2(p1);
            }
        }

        var results = MarkingCreator.Commit(uidoc, new[] { def }, $"SV - {def.Code}");
        if (def.Overlay) FootprintCutter.ApplyOverlay(uidoc, def);
        Report("Zebrado", results);
        return Result.Succeeded;
    }
}

/// <summary>Setas e símbolos por inserção.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdSimbolos : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new SymbolWindow();
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        var template = w.Result;
        EnsureDetailView(uidoc, template.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) template.Output.SurfaceIds = s;

        var results = Placement.Loop(uidoc, template.Code, w.FixedAngle, w.AngleDeg, (pos, dir, z) =>
        {
            var d = (SymbolMarkingDefinition)template.CloneWithNewId();
            d.Position = pos;
            d.Direction = dir;
            d.Z = z;
            return d;
        });
        if (results.Count == 0) return Result.Cancelled;
        Report("Setas e símbolos", results);
        return Result.Succeeded;
    }
}

/// <summary>Legendas por inserção.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdLegendas : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new TextWindow();
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        var template = w.Result;
        EnsureDetailView(uidoc, template.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) template.Output.SurfaceIds = s;

        var results = Placement.Loop(uidoc, "Legenda", w.FixedAngle, w.AngleDeg, (pos, dir, z) =>
        {
            var d = (TextMarkingDefinition)template.CloneWithNewId();
            d.Position = pos;
            d.Direction = dir;
            d.Z = z;
            return d;
        });
        if (results.Count == 0) return Result.Cancelled;
        Report("Legendas", results);
        return Result.Succeeded;
    }
}

/// <summary>Vagas de estacionamento ao longo do meio-fio.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdVagas : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new ParkingWindow();
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        EnsureDetailView(uidoc, w.Result.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) w.Result.Output.SurfaceIds = s;
        return MarkingCreator.CreateAlongPath(uidoc, w.Result, w.PathMode, w.Result.Code);
    }
}

/// <summary>Inserção repetitiva de símbolos/legendas: base + sentido.</summary>
internal static class Placement
{
    public static List<RenderResult> Loop(UIDocument uidoc, string label, bool fixedAngle, double angleDeg,
        Func<Vec2, Vec2, double, MarkingDefinition> factory)
    {
        var results = new List<RenderResult>();
        while (true)
        {
            var p = Picking.PickPoint(uidoc, $"{label}: clique o ponto de inserção (base) – ESC encerra");
            if (p == null) break;
            Vec2 dir;
            if (fixedAngle)
            {
                dir = Vec2.FromAngle(Angles.ToRad(90 + angleDeg));
            }
            else
            {
                var q = Picking.PickPoint(uidoc, $"{label}: clique um ponto no SENTIDO do tráfego");
                if (q == null) break;
                dir = (UnitConv.ToVec2(q) - UnitConv.ToVec2(p)).Normalized();
                if (dir.Length < 0.5) continue;
            }
            var def = factory(UnitConv.ToVec2(p), dir, UnitConv.M(p.Z));
            results.AddRange(MarkingCreator.Commit(uidoc, new[] { def }, $"SV - {label}"));
        }
        return results;
    }
}

/// <summary>Dispositivos físicos de bloqueio, segregação e canalização.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdDispositivos : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new DeviceWindow();
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        if (w.StudCode != null)
        {
            // Tachas e tachões (dentro de Bloqueios Físicos): janela de marcas lineares já no tipo escolhido.
            PluginContext.Settings.Set(CmdTachas.SettingsKey, w.StudCode);
            return LinearCommandBase.RunWindow(uidoc, CmdTachas.Title, CmdTachas.StudGroups, PathMode.Linhas);
        }
        if (w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        EnsureDetailView(uidoc, w.Result.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) w.Result.Output.SurfaceIds = s;
        return MarkingCreator.CreateAlongPath(uidoc, w.Result, w.PathMode, w.Result.Code);
    }
}
