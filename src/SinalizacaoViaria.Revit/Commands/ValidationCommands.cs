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
            PluginContext.Settings.CriteriosGeometricos, DrainageDesignStore.Load(doc), incluirNaoVerificados: true,
            terreno: TerrainModel.CurrentMesh(doc), terraplenagem: EarthworkRecordStore.Load(doc), terrenoNatural: TerrainModel.Ground(doc));
        var marcas = copias.Select(c => c.MarkingId).Distinct().Count();
        var now = DateTime.Now;
        var version = typeof(CmdValidarModelo).Assembly.GetName().Version?.ToString(3);
        // Histórico: a validação anterior deste projeto (arquivo do usuário, fora do modelo – o comando é somente leitura).
        var historyPath = HistoryPath(doc);
        ValidacaoGravada? previous = null;
        try { if (File.Exists(historyPath)) previous = ValidationHistory.FromJson(File.ReadAllText(historyPath)); }
        catch (Exception ex) { Log.Error("Validar Modelo – histórico", ex); }
        var cmp = ValidationHistory.Comparar(achados, previous);
        var text = ModelValidator.Report(achados, marcas, copias.Count, doc.Title, now, version, cmp);
        string? path = null;
        try
        {
            Directory.CreateDirectory(PluginPaths.Root);
            path = Path.Combine(PluginPaths.Root, $"validacao-{now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, text);
            File.WriteAllText(historyPath, ValidationHistory.ToJson(ValidationHistory.Gravar(achados, now, version)));
        }
        catch (Exception ex)
        {
            Log.Error("Validar Modelo – relatório", ex);
            path = null;
        }

        var problems = achados.Where(a => a.Severidade != Severidade.NaoVerificavel).ToList();
        int Count(Severidade s) => achados.Count(a => a.Severidade == s);
        var since = cmp.Anterior != null ? $" Desde a anterior: {cmp.Novos.Count} novo(s), {cmp.Resolvidos.Count} resolvido(s)." : "";
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = problems.Count == 0
                ? $"Nenhum problema em {marcas} marca(s) nas verificações que rodaram ({Count(Severidade.NaoVerificavel)} não verificada(s))."
                : $"Validação: {Count(Severidade.Erro)} erro(s), {Count(Severidade.Aviso)} aviso(s), {Count(Severidade.Info)} informação(ões), " +
                  $"{Count(Severidade.NaoVerificavel)} não verificada(s).{since}",
            MainContent = string.Join("\n", problems.Take(12).Select(a => $"• [{a.Codigo} · {a.Modulo}] {a.Mensagem}")) +
                          (problems.Count > 12 ? $"\n… e mais {problems.Count - 12} no relatório." : "") +
                          (Count(Severidade.NaoVerificavel) > 0 ? "\n\nNão verificado: " + string.Join(" ", achados.Where(a => a.Severidade == Severidade.NaoVerificavel).Select(a => a.Mensagem)) : "") +
                          "\n\nNada foi alterado no projeto.",
            ExpandedContent = text,
            FooterText = path != null ? $"Relatório: {path}" : "O relatório não pôde ser gravado (ver log).",
        };
        var ids = achados.Where(a => a.Severidade is Severidade.Erro or Severidade.Aviso)
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

    /// <summary>Arquivo da última validação do projeto, pelo caminho do arquivo (ou pelo título, se ainda não foi salvo).</summary>
    private static string HistoryPath(Document doc)
    {
        var key = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.ToLowerInvariant())))[..16];
        return Path.Combine(PluginPaths.Root, $"validacao-ultima-{hash}.json");
    }
}
