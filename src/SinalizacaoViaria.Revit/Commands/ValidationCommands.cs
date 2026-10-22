using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Validar Modelo: confere as definições gravadas no projeto (ModelValidator do núcleo) sem alterar nada – modo somente
/// leitura. Mostra o resumo, grava o relatório em TXT e pode selecionar os elementos dos achados.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class CmdValidarModelo : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var copias = MarkingStorage.Copies(doc, out var elements);
        if (copias.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhuma marca do SinalizaBIM neste projeto – nada a validar.");
            return Result.Cancelled;
        }
        var achados = ModelValidator.Validate(copias, uid => !string.IsNullOrWhiteSpace(uid) && doc.GetElement(uid) != null,
            PluginContext.Settings.CriteriosGeometricos, DrainageDesignStore.Load(doc));
        var marcas = copias.Select(c => c.MarkingId).Distinct().Count();
        var text = ModelValidator.Report(achados, marcas, copias.Count, doc.Title, DateTime.Now);
        string? path = null;
        try
        {
            Directory.CreateDirectory(PluginPaths.Root);
            path = Path.Combine(PluginPaths.Root, $"validacao-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, text);
        }
        catch (Exception ex)
        {
            Log.Error("Validar Modelo – relatório", ex);
            path = null;
        }

        int Count(Severidade s) => achados.Count(a => a.Severidade == s);
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = achados.Count == 0
                ? $"Modelo validado: nenhum problema em {marcas} marca(s)."
                : $"Validação: {Count(Severidade.Erro)} erro(s), {Count(Severidade.Aviso)} aviso(s), {Count(Severidade.Info)} informação(ões).",
            MainContent = string.Join("\n", achados.Take(12).Select(a => $"• [{a.Codigo}] {a.Mensagem}")) +
                          (achados.Count > 12 ? $"\n… e mais {achados.Count - 12} no relatório." : "") +
                          "\n\nNada foi alterado no projeto.",
            ExpandedContent = text,
            FooterText = path != null ? $"Relatório: {path}" : "O relatório não pôde ser gravado (ver log).",
        };
        var ids = achados.Where(a => a.Severidade != Severidade.Info)
            .SelectMany(a => a.Ids).Distinct()
            .SelectMany(id => elements.TryGetValue(id, out var l) ? l : new List<ElementId>())
            .Where(id => doc.GetElement(id) != null).Distinct().ToList();
        if (ids.Count > 0) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Selecionar os {ids.Count} elemento(s) com erro ou aviso");
        if (path != null) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Abrir o relatório");
        td.CommonButtons = TaskDialogCommonButtons.Close;
        var r = td.Show();
        if (r == TaskDialogResult.CommandLink1) uidoc.Selection.SetElementIds(ids);
        else if (r == TaskDialogResult.CommandLink2 && path != null)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Validar Modelo – abrir relatório", ex); }
        }
        return Result.Succeeded;
    }
}
