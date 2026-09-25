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

        PathReference path;
        var snapped = new List<string>();
        if (w.DrawPath)
        {
            var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
            var existing = w.Snap ? svc.Roads() : new List<IntersectionRoad>();
            var rbs = w.Snap ? MarkingStorage.Definitions(doc).OfType<RoundaboutDefinition>().Select(r => (r.Center, r.OuterRadius)).ToList() : new();
            (XYZ, string?) Snap(XYZ p)
            {
                if (!w.Snap) return (p, null);
                var sn = RoadConnection.SnapPoint(UnitConv.ToVec2(p), existing, 4.0, rbs);
                return sn.Kind == TipoEncaixe.Livre ? (p, null) : (new XYZ(UnitConv.Ft(sn.Point.X), UnitConv.Ft(sn.Point.Y), p.Z), sn.Describe);
            }
            var picked = Picking.PickRoadAxis(uidoc, Snap);
            if (picked == null) return Result.Cancelled;
            var (pts, info) = picked.Value;
            snapped.AddRange(info.Where(i => i != null)!);
            var pieces = RoadConnection.Fillet(pts.Select(UnitConv.ToVec2).ToList(), w.CurveRadius);
            List<ElementId> ids;
            using (var t = new Transaction(doc, "SV - Eixo da via"))
            {
                t.Start();
                ids = Picking.CreateAxis(doc, uidoc.ActiveView, pieces, pts[0].Z);
                t.Commit();
            }
            path = PathReference.FromElements(ids.Select(id => doc.GetElement(id).UniqueId));
        }
        else
        {
            var curves = Picking.PickCurves(uidoc, "Selecione as linhas do EIXO da via (no sentido de referência) e clique em Concluir");
            if (curves == null) return Result.Cancelled;
            path = PathReference.FromElements(curves.Select(c => c.UniqueId));
        }

        var defs = w.BuildDefinitions(path);
        var results = MarkingCreator.Commit(uidoc, defs, "SV - Sinalizar via");
        if (w.Setup.Warnings.Count > 0 && results.Count > 0) results[0].Warnings.InsertRange(0, w.Setup.Warnings);
        if (defs.OfType<RoadPavementDefinition>().FirstOrDefault() is { } pav && (w.AutoIntersect || w.FreeEnds != Core.Automation.FimLivre.Nenhum))
        {
            var template = UiHelpers.Remembered<IntersectionDefinition>("Intersecao") ?? new IntersectionDefinition();
            template.CornerRadius = w.CornerRadius ?? template.CornerRadius;
            template.Crosswalks = w.IntersectionCrosswalks;
            template.StopLines = w.IntersectionCrosswalks;
            template.Ramps = w.IntersectionRamps && w.IntersectionCrosswalks;
            template.Output = w.OutputSettings.Clone();
            var rb = UiHelpers.Remembered<RoundaboutDefinition>("Rotatoria") ?? new RoundaboutDefinition();
            var cds = UiHelpers.Remembered<CulDeSacDefinition>(nameof(CulDeSacDefinition)) ?? new CulDeSacDefinition();
            try
            {
                var extra = IntersectionRunner.Run(uidoc, "SV - Conexões da via", sv =>
                    sv.Connect(pav, w.Connection, w.FreeEnds, template, rb, cds, radiusByHierarchy: w.CornerRadius == null));
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

/// <summary>Nova via desenhada por pontos, conectada naturalmente às vias existentes.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdNovaVia : CmdSinalizarVia
{
    protected override bool DrawByDefault => true;
}

/// <summary>Desenha um eixo/caminho de referência por pontos.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdDesenharEixo : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var pl = Picking.PickPolyline(uidoc, "Desenhar eixo", false, keepAsModelLines: true);
        if (pl == null) return Result.Cancelled;
        uidoc.Selection.SetElementIds(pl.Value.Lines);
        TaskDialog.Show(AppTitle, $"Eixo criado com {pl.Value.Lines.Count} segmento(s) no estilo \"{StyleService.AxisLineStyleName}\".\n" +
                                  "Use-o como caminho das marcas; editar o eixo atualiza a sinalização automaticamente.\n" +
                                  "Dica: arcos e splines podem ser desenhados com a ferramenta Linha de modelo do Revit.");
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

        PathReference boundary;
        if (w.PathMode == PathMode.Desenhar)
        {
            var pl = Picking.PickPolyline(uidoc, "Contorno do zebrado", true, keepAsModelLines: true);
            if (pl == null) return Result.Cancelled;
            boundary = PathReference.FromElements(pl.Value.Lines.Select(id => uidoc.Document.GetElement(id).UniqueId));
        }
        else
        {
            var curves = Picking.PickCurves(uidoc, "Selecione as linhas que formam o CONTORNO FECHADO e clique em Concluir");
            if (curves == null) return Result.Cancelled;
            boundary = PathReference.FromElements(curves.Select(c => c.UniqueId));
        }
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
