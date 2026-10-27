using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Central do Projeto (Etapa 10; Prompt de Interface §8): o resumo do projeto calculado no núcleo
/// (<see cref="CentralDoProjeto"/>) com as definições, o registro da terraplenagem, a última validação gravada e o Projeto de
/// Drenagem. Não altera nada. O comando escolhido na janela roda depois que ela fecha, dentro deste comando – por isso o modo
/// Manual: o comando escolhido abre as transações dele.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCentralProjeto : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var defs = MarkingStorage.Definitions(doc);
        var registro = EarthworkRecordStore.Load(doc);
        ValidacaoGravada? ultima = null;
        try
        {
            var path = CmdValidarModelo.HistoryPath(doc);
            if (File.Exists(path)) ultima = ValidationHistory.FromJson(File.ReadAllText(path));
        }
        catch (Exception ex) { Log.Error("Central do Projeto – última validação", ex); }
        var drenagem = DrainageDesignStore.Load(doc) != null;
        ResumoDoProjeto Resumo(double? area, int falhas) => CentralDoProjeto.Resumir(defs, registro, ultima, drenagem, area, falhas);

        var versao = typeof(CmdCentralProjeto).Assembly.GetName().Version?.ToString(3) ?? "";
        var w = new ProjectCentralWindow(NomeDoProjeto(doc), Documento(doc), versao, Resumo(null, 0), () =>
        {
            var (area, falhas) = MedirPavimento(doc, uidoc.ActiveView, defs);
            return Resumo(area, falhas);
        });
        UiHelpers.ShowModal(w);
        return w.Comando is { } c ? CommandCatalog.Run(c, app) : Result.Succeeded;
    }

    /// <summary>Nome das Informações do projeto; vazio ou o texto de exemplo do modelo, o título do arquivo.</summary>
    internal static string NomeDoProjeto(Document doc)
    {
        var nome = doc.ProjectInformation?.Name?.Trim();
        return string.IsNullOrEmpty(nome) || nome.Equals("Project Name", StringComparison.OrdinalIgnoreCase) || nome.Equals("Nome do projeto", StringComparison.OrdinalIgnoreCase)
            ? doc.Title
            : nome;
    }

    internal static string Documento(Document doc) =>
        (string.IsNullOrEmpty(doc.PathName) ? $"{doc.Title} (arquivo ainda não salvo)" : doc.PathName) + (doc.IsWorkshared ? " · compartilhado" : "");

    /// <summary>
    /// Área do pavimento (m²) medida nas peças das marcas da seção da via (pistas, nós, calçadas), como o resumo por componente
    /// dos Quantitativos; e quantas marcas não puderam ser medidas.
    /// </summary>
    internal static (double Area, int Falhas) MedirPavimento(Document doc, View? view, IReadOnlyList<MarkingDefinition> defs)
    {
        var service = new MarkingService(doc, view);
        var items = new List<(MarkingDefinition, MarkingGeometry)>();
        var falhas = 0;
        foreach (var d in defs.Where(ComponentQuantities.InScope))
        {
            try { items.Add((d, service.QuantityGeometry(d))); }
            catch (Exception ex)
            {
                falhas++;
                Log.Error($"Central do Projeto – pavimento {d.DisplayCode}", ex);
            }
        }
        var area = ComponentQuantities.Compute(items).Where(r => r.Code == SectionComponent.Pavimento).Sum(r => r.Area);
        return (area, falhas);
    }
}

/// <summary>
/// Mostra ou oculta o painel contextual do SinalizaBIM (Etapa 10; Prompt de Interface §9). Ao mostrar, o painel já vem com a
/// seleção atual; depois acompanha cada mudança de seleção.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class CmdPainelContextual : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        if (ContextPanelService.Pane(app) is not { } pane)
        {
            TaskDialog.Show(AppTitle, "O painel contextual não foi registrado ao iniciar o Revit. O log tem o motivo: " + PluginPaths.Root);
            return Result.Failed;
        }
        if (pane.IsShown()) pane.Hide();
        else
        {
            pane.Show();
            ContextPanelService.Refresh(uidoc);
        }
        return Result.Succeeded;
    }
}
