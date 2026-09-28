using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Apagar Trecho: apaga só uma parte de uma marca (um traço da linha de eixo, um trecho entre dois pontos, tudo numa janela)
/// sem desfazer a marca – o trecho vira um recorte guardado na própria marca, que pode ser desativado (a marca volta inteira),
/// reativado ou removido depois. Regerar/editar a marca mantém os recortes.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdApagarTrecho : CommandBase
{
    private enum Modo { Tracos, DoisPontos, Janela, Gerenciar }

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = "Apagar trecho de sinalização",
            MainContent = "A marca continua a mesma (edição, quantitativos, conexões); o trecho apagado fica guardado nela e pode ser " +
                          "desativado, reativado ou removido quando quiser.",
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Clicar nas peças a apagar", "Cada clique apaga o traço, seta, símbolo ou faixa sob o cursor. ESC termina.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Trecho entre dois pontos", "Clique a marca e depois o início e o fim do trecho ao longo dela (ex.: parte da linha de eixo).");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Janela", "Clique a marca e dois cantos: apaga tudo dela dentro do retângulo.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Desativar / reativar / remover trechos apagados", "Clique a marca e escolha o que fazer com os recortes dela.");
        td.CommonButtons = TaskDialogCommonButtons.Cancel;
        var modo = td.Show() switch
        {
            TaskDialogResult.CommandLink1 => Modo.Tracos,
            TaskDialogResult.CommandLink2 => Modo.DoisPontos,
            TaskDialogResult.CommandLink3 => Modo.Janela,
            TaskDialogResult.CommandLink4 => Modo.Gerenciar,
            _ => (Modo?)null,
        };
        if (modo == null) return Result.Cancelled;
        return modo switch
        {
            Modo.Tracos => Pieces(uidoc),
            Modo.DoisPontos => Stretch(uidoc),
            Modo.Janela => Window(uidoc),
            _ => Manage(uidoc),
        };
    }

    /// <summary>Marca clicada e o ponto do clique (m, planta).</summary>
    private static (StoredMarking Stored, Vec2 Point)? PickAt(UIDocument uidoc, string prompt)
    {
        try
        {
            var r = uidoc.Selection.PickObject(ObjectType.Element, new MarkingSelectionFilter(), prompt);
            var st = MarkingStorage.Read(uidoc.Document.GetElement(r));
            return st == null ? null : (st, UnitConv.ToVec2(r.GlobalPoint ?? XYZ.Zero));
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }

    private static Result Pieces(UIDocument uidoc)
    {
        var changed = new Dictionary<string, MarkingDefinition>();
        var count = 0;
        while (true)
        {
            var hit = PickAt(uidoc, "Apagar trecho: clique no traço/peça a apagar – ESC termina");
            if (hit == null) break;
            var (stored, p) = hit.Value;
            var def = changed.TryGetValue(stored.MarkingId, out var d0) ? d0 : stored.Definition;
            var zone = PieceZone(uidoc, def, p);
            if (zone == null)
            {
                TaskDialog.Show(AppTitle, "Nenhuma peça da marca sob o ponto clicado (clique sobre a pintura/peça).");
                continue;
            }
            def.Exclusions.Add(zone);
            changed[def.Id] = def;
            count++;
            // Regenera já, para o traço sumir antes do próximo clique.
            MarkingCreator.Commit(uidoc, new[] { def }, "SV - Apagar trecho");
        }
        return count > 0 ? Result.Succeeded : Result.Cancelled;
    }

    /// <summary>Recorte da peça sob o ponto: o contorno dela com 3 cm de folga.</summary>
    private static ExclusionZone? PieceZone(UIDocument uidoc, MarkingDefinition def, Vec2 p)
    {
        var service = new MarkingService(uidoc.Document, uidoc.ActiveView);
        MarkingGeometry geo;
        try { geo = service.BuildGeometry(def, out _); }
        catch (Exception ex) { Log.Error("Apagar trecho – geometria", ex); return null; }
        // A peça que contém o ponto; senão a mais próxima até 0,5 m (peças finas como linhas de bordo).
        var piece = geo.Pieces.Where(x => x.Shape.Contains(p)).OrderBy(x => x.Shape.Area).FirstOrDefault()
                    ?? geo.Pieces.Select(x => (P: x, D: x.Shape.DistanceTo(p))).Where(t => t.D < 0.5).OrderBy(t => t.D).Select(t => t.P).FirstOrDefault();
        if (piece == null) return null;
        var shape = piece.Shape;
        // Peça muito longa (linha contínua): apaga só 3 m em volta do clique, ao longo da peça.
        var (mn, mx) = shape.Bounds;
        if (Math.Max(mx.X - mn.X, mx.Y - mn.Y) > 12)
        {
            var box = Polygon2.Rectangle(p - new Vec2(1.5, 1.5), p + new Vec2(1.5, 1.5));
            var part = PolygonOps.Intersect(new[] { shape }, new[] { box }).OrderByDescending(x => x.Area).FirstOrDefault();
            if (part != null) shape = part;
        }
        var grown = PolygonOps.Offset(new[] { shape }, 0.03).OrderByDescending(x => x.Area).FirstOrDefault() ?? shape;
        return new ExclusionZone { Manual = true, Points = grown.Outer.ToList() };
    }

    private static Result Stretch(UIDocument uidoc)
    {
        var hit = PickAt(uidoc, "Apagar trecho: clique a MARCA (linha, bordo, zebrado...)");
        if (hit == null) return Result.Cancelled;
        var def = hit.Value.Stored.Definition;
        var main = def.Path != null ? PathResolver.Resolve(uidoc.Document, def.Path)?.Main : null;
        if (main == null)
        {
            TaskDialog.Show(AppTitle, "Essa marca não tem caminho (linha) – use Clicar nas peças ou Janela.");
            return Result.Cancelled;
        }
        var a = Picking.PickPoint(uidoc, "Apagar trecho: clique o INÍCIO do trecho sobre a marca");
        if (a == null) return Result.Cancelled;
        var b = Picking.PickPoint(uidoc, "Apagar trecho: clique o FIM do trecho");
        if (b == null) return Result.Cancelled;
        var sa = main.Project(UnitConv.ToVec2(a)).Station;
        var sb = main.Project(UnitConv.ToVec2(b)).Station;
        if (Math.Abs(sb - sa) < 0.05) return Result.Cancelled;
        // Faixa ao longo do caminho entre as duas estacas, larga o bastante para a marca (e só ela, pois o recorte é da marca).
        var pts = main.SubPoints(Math.Min(sa, sb), Math.Max(sa, sb));
        var geo = new MarkingService(uidoc.Document, uidoc.ActiveView).BuildGeometry(def, out _);
        var width = 2 * Math.Max(0.5, geo.Pieces.Count == 0 ? 1 : geo.Pieces.SelectMany(x => x.Shape.Outer).Max(q => Math.Abs(main.SignedDistance(q))) + 0.2);
        var strip = PolygonOps.Strip(pts, width).OrderByDescending(x => x.Area).FirstOrDefault();
        if (strip == null) return Result.Cancelled;
        def.Exclusions.Add(new ExclusionZone { Manual = true, Points = strip.Outer.ToList() });
        ReportResults("Apagar trecho", MarkingCreator.Commit(uidoc, new[] { def }, "SV - Apagar trecho"));
        return Result.Succeeded;
    }

    private static Result Window(UIDocument uidoc)
    {
        var hit = PickAt(uidoc, "Apagar trecho: clique a MARCA");
        if (hit == null) return Result.Cancelled;
        var def = hit.Value.Stored.Definition;
        var a = Picking.PickPoint(uidoc, "Apagar trecho: primeiro canto da janela");
        if (a == null) return Result.Cancelled;
        var b = Picking.PickPoint(uidoc, "Apagar trecho: canto oposto");
        if (b == null) return Result.Cancelled;
        var p = UnitConv.ToVec2(a);
        var q = UnitConv.ToVec2(b);
        var rect = Polygon2.Rectangle(new Vec2(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y)), new Vec2(Math.Max(p.X, q.X), Math.Max(p.Y, q.Y)));
        if (rect.Area < 0.01) return Result.Cancelled;
        def.Exclusions.Add(new ExclusionZone { Manual = true, Points = rect.Outer.ToList() });
        ReportResults("Apagar trecho", MarkingCreator.Commit(uidoc, new[] { def }, "SV - Apagar trecho"));
        return Result.Succeeded;
    }

    private static Result Manage(UIDocument uidoc)
    {
        var hit = PickAt(uidoc, "Trechos apagados: clique a MARCA");
        if (hit == null) return Result.Cancelled;
        var def = hit.Value.Stored.Definition;
        var manual = def.Exclusions.Where(z => z.Manual).ToList();
        if (manual.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Essa marca não tem trechos apagados com a ferramenta Apagar Trecho.");
            return Result.Cancelled;
        }
        var on = manual.Count(z => z.Enabled);
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = $"{def.DisplayCode}: {manual.Count} trecho(s) apagado(s), {on} ativo(s)",
            MainContent = "Desativar mostra a marca inteira de novo sem perder os recortes; reativar volta a apagar.",
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Desativar (mostrar a marca inteira)");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Reativar (apagar de novo)");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Remover todos os recortes desta marca");
        td.CommonButtons = TaskDialogCommonButtons.Cancel;
        switch (td.Show())
        {
            case TaskDialogResult.CommandLink1: foreach (var z in manual) z.Enabled = false; break;
            case TaskDialogResult.CommandLink2: foreach (var z in manual) z.Enabled = true; break;
            case TaskDialogResult.CommandLink3: def.Exclusions.RemoveAll(z => z.Manual); break;
            default: return Result.Cancelled;
        }
        ReportResults("Trechos apagados", MarkingCreator.Commit(uidoc, new[] { def }, "SV - Trechos apagados"));
        return Result.Succeeded;
    }
}
