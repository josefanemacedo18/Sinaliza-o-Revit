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
