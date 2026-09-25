using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Base de todos os comandos: validações, tratamento de erros e relatórios.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public abstract class CommandBase : IExternalCommand
{
    public const string AppTitle = "Sinalização Viária";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UiHelpers.RevitHandle = commandData.Application.MainWindowHandle;
        var uidoc = commandData.Application.ActiveUIDocument;
        if (RequiresDocument && uidoc == null)
        {
            TaskDialog.Show(AppTitle, "Abra um projeto do Revit antes de usar este comando.");
            return Result.Cancelled;
        }
        if (RequiresDocument && uidoc!.Document.IsFamilyDocument)
        {
            TaskDialog.Show(AppTitle, "Os comandos de sinalização funcionam em projetos (.rvt), não em famílias.");
            return Result.Cancelled;
        }
        try
        {
            return Run(commandData.Application, uidoc!);
        }
        catch (UserMessageException ex)
        {
            TaskDialog.Show(AppTitle, ex.Message);
            return Result.Cancelled;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            Log.Error(GetType().Name, ex);
            var td = new TaskDialog(AppTitle)
            {
                MainInstruction = "Ocorreu um erro inesperado.",
                MainContent = ex.Message,
                ExpandedContent = ex.ToString(),
                FooterText = $"Detalhes gravados em {PluginPaths.Root}\\log.txt",
            };
            td.Show();
            return Result.Failed;
        }
    }

    protected virtual bool RequiresDocument => true;

    protected abstract Result Run(UIApplication app, UIDocument uidoc);

    /// <summary>Mostra um resumo quando há avisos (sem interromper o fluxo quando está tudo certo).</summary>
    protected static void Report(string action, IReadOnlyCollection<RenderResult> results, bool alwaysShow = false)
    {
        var warnings = results.SelectMany(r => r.Warnings).Distinct().ToList();
        if (warnings.Count == 0 && !alwaysShow) return;
        var created = results.Sum(r => r.Elements.Count);
        var area = results.Sum(r => r.Geometry?.TotalArea ?? 0);
        var sb = new StringBuilder();
        foreach (var w in warnings.Take(25)) sb.AppendLine("• " + w);
        if (warnings.Count > 25) sb.AppendLine($"… e mais {warnings.Count - 25} aviso(s).");
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = $"{action}: {results.Count} marca(s), {created} elemento(s), {UiHelpers.F(area)} m² pintados.",
            MainContent = warnings.Count > 0 ? "Avisos:\n" + sb : "Concluído sem avisos.",
        };
        td.Show();
    }

    protected static void EnsureDetailView(UIDocument uidoc, Core.Definitions.OutputSettings output) => EnsureDetailViewPublic(uidoc, output);

    internal static void EnsureDetailViewPublic(UIDocument uidoc, Core.Definitions.OutputSettings output)
    {
        if (output.Mode != Core.Definitions.OutputMode.Detalhe2D) return;
        if (!MarkingService.SupportsDetail(uidoc.ActiveView))
            throw new UserMessageException("A representação 2D é criada na vista ativa: abra uma vista em planta ou vista de desenho.");
        output.ViewId = uidoc.ActiveView.UniqueId;
    }
}
