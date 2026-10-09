using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Reports;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Memorial descritivo (.docx) gerado do modelo: objetivo e normas, vias, interseções e rotatórias, sinalização horizontal
/// e vertical por código com as quantidades do quantitativo, acessibilidade, drenagem e obras – com os campos de nome do
/// projeto, responsável técnico e ART em branco para preencher no Word.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdMemorial : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var defs = MarkingStorage.Definitions(doc);
        if (defs.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhuma marca de sinalização encontrada neste projeto – nada a descrever.");
            return Result.Cancelled;
        }
        var title = string.IsNullOrWhiteSpace(doc.Title) ? "Projeto" : doc.Title;
        var dlg = new SaveFileDialog
        {
            Filter = "Documento do Word|*.docx",
            FileName = $"Memorial descritivo - {title}.docx".Replace(":", ""),
            InitialDirectory = !string.IsNullOrEmpty(doc.PathName) ? Path.GetDirectoryName(doc.PathName) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() != true) return Result.Cancelled;

        // Mesmo quadro de quantidades da ferramenta Quantitativos (e das tabelas e pranchas).
        var (_, rows) = CmdQuantitativos.Collect(doc, uidoc.ActiveView, defs);
        var memorial = Memorial.Build(new MemorialInput
        {
            Definitions = defs,
            Rows = rows,
            PathOf = d => d.Path == null ? null : PathResolver.Resolve(doc, d.Path)?.Main,
            ModelName = string.IsNullOrEmpty(doc.PathName) ? doc.Title : Path.GetFileName(doc.PathName),
        });
        try
        {
            File.WriteAllBytes(dlg.FileName, DocxWriter.Write(memorial));
        }
        catch (Exception ex)
        {
            throw new UserMessageException("Não foi possível gravar o memorial (o arquivo está aberto no Word?): " + ex.Message);
        }
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = "Memorial descritivo gerado.",
            MainContent = dlg.FileName + "\n\nPreencha no Word os campos em branco (nome do projeto, responsável técnico, ART, local e data).",
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir no Word");
        if (td.Show() == TaskDialogResult.CommandLink1)
            try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Abrir memorial", ex); }
        return Result.Succeeded;
    }
}
