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
        var path = RoadAxisInput.Get(uidoc, w.DrawPath, w.Snap, axisRadius, snapped);
        if (path == null) return Result.Cancelled;

        var defs = w.BuildDefinitions(path);
        RoadSetup.ApplyAxisRadius(defs, w.CurveRadius);
        var results = MarkingCreator.Commit(uidoc, defs, "SV - Sinalizar via");
        if (w.Setup.Warnings.Count > 0 && results.Count > 0) results[0].Warnings.InsertRange(0, w.Setup.Warnings);
        if (defs.OfType<RoadPavementDefinition>().FirstOrDefault() is { } pav && (w.AutoIntersect || w.FreeEnds != Core.Automation.FimLivre.Nenhum))
        {
            var template = IntersectionService.AutoTemplate(w.IntersectionCrosswalks);
            template.CornerRadius = w.CornerRadius ?? template.CornerRadius;
            template.Ramps = w.IntersectionRamps && w.IntersectionCrosswalks;
            template.Output = w.OutputSettings.Clone();
            var rb = UiHelpers.Remembered<RoundaboutDefinition>("Rotatoria") ?? new RoundaboutDefinition();
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
        if (snapped.Count > 0 && results.Count > 0)
            results[0].Warnings.Insert(0, "Conexões: " + string.Join("; ", snapped.Distinct()) + ".");
        Report("Sinalizar via", results);
        return Result.Succeeded;
    }
}

/// <summary>Obtém o eixo de uma via: desenhado por pontos (com encaixe e curvas) ou linhas selecionadas.</summary>
internal static class RoadAxisInput
{
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
                    foreach (var m in RoadConnection.BuildCarriageway(c, PathReference.FromPoints(axis.Points, 0), new OutputSettings(), center == "-" ? null : center, edges, Hierarquia.DefaultSpeed(h)))
                        geo.Merge(MarkingBuilder.Build(m, axis, ctx));
                    return new FormPreview(geo, null, new[] { axis.Points }, $"Largura da pista: {UiHelpers.F(c.LeftWidth + c.RightWidth)} m");
                }, true, "Criar", 1000, 700)
            .Choice("Hierarquia viária (CTB art. 60)", Hierarquia.Definidas.Select(x => (Hierarquia.Label(x), x)), () => h, v => h = v)
            .Choice("Pavimento", new[] { ("Asfalto (CBUQ)", TipoPavimento.Asfalto), ("Bloquete / intertravado", TipoPavimento.Bloquete), ("Concreto", TipoPavimento.Concreto) },
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
            .Check("Faixas de pedestres nas interseções", () => crosswalks, v => crosswalks = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        d.Hierarchy = h;
        UiHelpers.Remember("Pista", d);
        st.Set("pista:eixo", center);
        st.Set("pista:bordos", edges ? "1" : "0");
        st.Set("pista:conectar", connect ? "1" : "0");
        st.LastDrawRoad = draw;
        st.LastCurveRadius = radius;
        PluginContext.SaveSettings();
        var output = d.Output;
        EnsureDetailView(uidoc, output);

        var snapped = new List<string>();
        var path = RoadAxisInput.Get(uidoc, draw, snap, Math.Max(radius, RoadSetup.MinAxisRadius(Math.Max(d.LeftWidth, d.RightWidth))), snapped);
        if (path == null) return Result.Cancelled;
        var defs = RoadConnection.BuildCarriageway(d, path, output, center == "-" ? null : center, edges, Hierarquia.DefaultSpeed(h));
        RoadSetup.ApplyAxisRadius(defs, radius);
        var results = MarkingCreator.Commit(uidoc, defs, "SV - Pista");
        if (connect && defs.OfType<RoadPavementDefinition>().FirstOrDefault() is { } pav)
        {
            var template = IntersectionService.AutoTemplate(crosswalks);
            results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões da pista", sv =>
                sv.Connect(pav, TipoConexao.Intersecao, FimLivre.Nenhum, template, new RoundaboutDefinition(), new CulDeSacDefinition(), radiusByHierarchy: true))
                .Where(r => r.Warnings.Count > 0));
        }
        Report("Pista", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
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

        var boundary = MarkingCreator.PickPath(uidoc, w.PathMode, "Contorno FECHADO do zebrado", closed: true);
        if (boundary == null) return Result.Cancelled;
        boundary.Closed = true;
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
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        EnsureDetailView(uidoc, w.Result.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) w.Result.Output.SurfaceIds = s;
        return MarkingCreator.CreateAlongPath(uidoc, w.Result, w.PathMode, w.Result.Code);
    }
}
