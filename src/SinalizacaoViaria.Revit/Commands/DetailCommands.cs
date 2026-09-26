using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

internal static class DetailHelpers
{
    /// <summary>Vista ativa válida para detalhamento (planta ou vista de desenho).</summary>
    public static View RequireDetailView(UIDocument uidoc)
    {
        var v = uidoc.ActiveView;
        if (!MarkingService.SupportsDetail(v))
            throw new UserMessageException("As ferramentas de detalhamento trabalham na vista ativa: abra uma planta de piso/implantação (ou vista de desenho).");
        return v;
    }

    public static void PrepareOutput(MarkingDefinition d, View view)
    {
        d.Output.Mode = OutputMode.Detalhe2D;
        d.Output.ViewId = view.UniqueId;
    }

    public static Vec2 ToCore(XYZ p) => new(UnitConv.M(p.X), UnitConv.M(p.Y));
}

/// <summary>Desenha as placas ampliadas na planta, com linha de chamada até o suporte e o código.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdDetalharPlacas : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = DetailHelpers.RequireDetailView(uidoc);
        var all = MarkingStorage.Definitions(doc);
        var signs = all.OfType<SignDefinition>().ToList();
        if (signs.Count == 0) throw new UserMessageException("Nenhuma placa no projeto. Insira placas com o comando 'Placas' e depois detalhe-as.");

        var selected = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => e != null)
            .Select(MarkingStorage.Read).Where(r => r?.Definition is SignDefinition).Select(r => (SignDefinition)r!.Definition)
            .GroupBy(s => s.Id).Select(g => g.First()).ToList();

        var w = new SignDetailWindow(view.Scale, selected.FirstOrDefault() ?? signs[0], null, selected.Count);
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();

        List<SignDefinition> targets = w.SelectedScope switch
        {
            SignDetailWindow.Scope.Selecionadas => selected,
            SignDetailWindow.Scope.Todas => signs,
            _ => SignsInView(doc, view),
        };
        if (targets.Count == 0) throw new UserMessageException("Nenhuma placa visível nesta vista.");

        // Placas já detalhadas nesta vista: atualiza o detalhe existente em vez de duplicar.
        var existing = all.OfType<SignPlanDetailDefinition>().Where(d => d.Output.ViewId == view.UniqueId)
            .GroupBy(d => d.SignId).ToDictionary(g => g.Key, g => g.First());
        var defs = new List<MarkingDefinition>();
        // Numeração em ordem de leitura da planta: de cima para baixo, da esquerda para a direita (faixas de 10 m).
        var ordered = targets.OrderByDescending(s => Math.Round(s.Position.Y / 10)).ThenBy(s => s.Position.X).ToList();
        var number = w.NumberStart;
        foreach (var s in ordered)
        {
            var d = (SignPlanDetailDefinition)w.Result.CloneWithNewId();
            if (existing.TryGetValue(s.Id, out var old))
            {
                d.Id = old.Id;
                if (!w.Numbering) d.Number = old.Number;
            }
            d.Number = w.Numbering ? $"{w.NumberPrefix}{number++:00}" : d.Number;
            d.SignId = s.Id;
            DetailHelpers.PrepareOutput(d, view);
            defs.Add(d);
        }
        var results = MarkingCreator.Commit(uidoc, defs, "SV - Detalhar placas");
        Report($"Detalhe de {defs.Count} placa(s)", results);
        return Result.Succeeded;
    }

    private static List<SignDefinition> SignsInView(Document doc, View view)
    {
        var ids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(DirectShape)).ToElementIds().ToHashSet();
        return MarkingStorage.All(doc).Where(r => ids.Contains(r.Element.Id) && r.Definition is SignDefinition)
            .GroupBy(r => r.MarkingId).Select(g => (SignDefinition)g.First().Definition).ToList();
    }
}

/// <summary>
/// Leva o símbolo de uma placa detalhada para qualquer posição e desenha a linha de chamada por onde quiser
/// (vértices clicados), ou muda a ponta da chamada. Tudo fica relativo ao suporte: se a placa for movida, o detalhe acompanha.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdMoverChamadaPlaca : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = DetailHelpers.RequireDetailView(uidoc);
        var mm = view.Scale / 1000.0;
        var all = MarkingStorage.Definitions(doc);
        var details = all.OfType<SignPlanDetailDefinition>().Where(d => d.Output.ViewId == view.UniqueId).ToList();
        if (details.Count == 0) throw new UserMessageException("Nenhum detalhe de placa nesta vista. Use 'Detalhar Placas' primeiro.");

        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = "Mover chamada de placa",
            MainContent = "Clique depois no símbolo detalhado (ou na própria placa). A posição e os vértices ficam relativos ao suporte.",
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Mover o símbolo e desenhar a chamada",
            "Clique a nova posição do símbolo e, em seguida, os vértices da linha de chamada (ESC termina; sem vértices = reta).");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Só redesenhar a chamada", "Mantém o símbolo e clica os vértices da linha (ESC termina).");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Mudar a ponta da chamada", "Clique o ponto para onde a chamada deve apontar (ex.: face da placa, poste).");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Voltar à chamada automática", "Chamada reta, ponta no suporte.");
        var choice = td.Show();
        if (choice is not (TaskDialogResult.CommandLink1 or TaskDialogResult.CommandLink2 or TaskDialogResult.CommandLink3 or TaskDialogResult.CommandLink4))
            return Result.Cancelled;

        var results = new List<RenderResult>();
        while (true)
        {
            Reference picked;
            try
            {
                picked = uidoc.Selection.PickObject(ObjectType.Element, new MarkingSelectionFilter(), "Clique no símbolo detalhado (ou na placa) – ESC encerra");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }
            var rec = MarkingStorage.Read(doc.GetElement(picked));
            var detail = rec?.Definition switch
            {
                SignPlanDetailDefinition sd => details.FirstOrDefault(d => d.Id == sd.Id),
                SignDefinition sign => details.FirstOrDefault(d => d.SignId == sign.Id),
                _ => null,
            };
            if (detail == null) { TaskDialog.Show(AppTitle, "Esse elemento não é uma placa detalhada nesta vista."); continue; }
            if (all.FirstOrDefault(x => x.Id == detail.SignId) is not SignDefinition post) continue;
            Vec2 Rel(XYZ p) => (DetailHelpers.ToCore(p) - post.Position) / mm;

            var d = (SignPlanDetailDefinition)MarkingDefinition.FromJson(detail.ToJson())!;
            switch (choice)
            {
                case TaskDialogResult.CommandLink1:
                {
                    var p = Picking.PickPoint(uidoc, $"{d.Number ?? "Placa"}: clique a nova posição do centro do símbolo");
                    if (p == null) continue;
                    d.OffsetMm = Rel(p);
                    d.ElbowsMm = PickElbows(uidoc, Rel);
                    d.LeaderStyle = d.ElbowsMm.Count > 0 ? EstiloChamada.Livre : d.LeaderStyle == EstiloChamada.Livre ? EstiloChamada.Reta : d.LeaderStyle;
                    d.Leader = true;
                    break;
                }
                case TaskDialogResult.CommandLink2:
                    d.ElbowsMm = PickElbows(uidoc, Rel);
                    d.LeaderStyle = d.ElbowsMm.Count > 0 ? EstiloChamada.Livre : EstiloChamada.Reta;
                    d.Leader = true;
                    break;
                case TaskDialogResult.CommandLink3:
                {
                    var p = Picking.PickPoint(uidoc, "Clique a ponta da chamada");
                    if (p == null) continue;
                    d.AnchorMm = Rel(p);
                    d.Leader = true;
                    break;
                }
                default:
                    d.ElbowsMm.Clear();
                    d.AnchorMm = Vec2.Zero;
                    d.LeaderStyle = EstiloChamada.Reta;
                    d.Leader = true;
                    break;
            }
            results.AddRange(MarkingCreator.Commit(uidoc, new[] { d }, "SV - Mover chamada de placa"));
            var i = details.FindIndex(x => x.Id == d.Id);
            if (i >= 0) details[i] = d;
        }
        if (results.Count == 0) return Result.Cancelled;
        Report("Chamadas de placa", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }

    private static List<Vec2> PickElbows(UIDocument uidoc, Func<XYZ, Vec2> rel)
    {
        var res = new List<Vec2>();
        while (res.Count < 12)
        {
            var p = Picking.PickPoint(uidoc, $"Vértice {res.Count + 1} da chamada, do suporte para o símbolo (ESC termina)");
            if (p == null) break;
            res.Add(rel(p));
        }
        return res;
    }
}

/// <summary>Anotação com linha de chamada e texto automático para qualquer sinalização.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdAnotar : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = DetailHelpers.RequireDetailView(uidoc);
        var w = new LabelWindow();
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();

        var results = new List<RenderResult>();
        while (true)
        {
            Reference picked;
            try
            {
                picked = uidoc.Selection.PickObject(ObjectType.Element, new MarkingSelectionFilter(),
                    "Clique sobre a sinalização a anotar (o ponto clicado recebe a chamada) – ESC encerra");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }
            var element = doc.GetElement(picked);
            var target = element == null ? null : MarkingStorage.Read(element);
            if (target == null) continue;
            if (target.Definition is IAnnotationDefinition)
            {
                TaskDialog.Show(AppTitle, "Selecione uma sinalização, não um detalhe ou anotação.");
                continue;
            }
            var anchor = picked.GlobalPoint ?? Center(element!, view);
            if (anchor == null) continue;
            var at = Picking.PickPoint(uidoc, "Clique onde o texto da anotação deve ficar");
            if (at == null) break;

            var d = (LabelDefinition)w.Result.CloneWithNewId();
            d.MarkingTargetId = target.MarkingId;
            d.Anchor = DetailHelpers.ToCore(anchor);
            d.LabelPosition = DetailHelpers.ToCore(at);
            DetailHelpers.PrepareOutput(d, view);
            results.AddRange(MarkingCreator.Commit(uidoc, new[] { d }, "SV - Anotar"));
        }
        if (results.Count == 0) return Result.Cancelled;
        Report("Anotação", results);
        return Result.Succeeded;
    }

    private static XYZ? Center(Element e, View v)
    {
        var bb = e.get_BoundingBox(v) ?? e.get_BoundingBox(null);
        return bb == null ? null : (bb.Min + bb.Max) / 2;
    }
}

/// <summary>Quadro de legenda automático com amostras dos tipos usados no projeto.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdQuadroLegenda : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = DetailHelpers.RequireDetailView(uidoc);
        var all = MarkingStorage.Definitions(doc);
        var w = new LegendWindow(view.Scale, all);
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        var at = Picking.PickPoint(uidoc, "Clique o canto superior esquerdo do quadro de legenda");
        if (at == null) return Result.Cancelled;
        var d = (LegendDefinition)w.Result.CloneWithNewId();
        d.Position = DetailHelpers.ToCore(at);
        DetailHelpers.PrepareOutput(d, view);
        var results = MarkingCreator.Commit(uidoc, new[] { d }, "SV - Quadro de legenda");
        Report("Quadro de legenda", results);
        return Result.Succeeded;
    }
}

/// <summary>Mostra/oculta na vista ativa as linhas de eixo usadas como caminho.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdEixos : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;
        var sub = new StyleService(doc).LineSubcategory(StyleService.AxisLineStyleName);
        if (sub == null) throw new UserMessageException("O projeto ainda não tem linhas 'SV - Eixo de sinalização'.");
        if (view.ViewTemplateId != ElementId.InvalidElementId)
            throw new UserMessageException("A visibilidade desta vista é controlada por um modelo de vista. Altere o modelo (V/G → Linhas → SV - Eixo de sinalização) ou desvincule-o.");
        if (!view.CanCategoryBeHidden(sub.Id))
            throw new UserMessageException("Esta vista não permite ocultar a subcategoria de linhas.");
        var hide = !view.GetCategoryHidden(sub.Id);
        using var t = new Transaction(doc, hide ? "SV - Ocultar eixos" : "SV - Mostrar eixos");
        t.Start();
        view.SetCategoryHidden(sub.Id, hide);
        t.Commit();
        return Result.Succeeded;
    }
}
