using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Quadro de quantidades, exportação CSV e tabela nativa do Revit.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdQuantitativos : CommandBase
{
    public const string ScheduleName = "SV - Quantitativos de sinalização";

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var defs = MarkingStorage.Definitions(doc);
        if (defs.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhuma marca de sinalização encontrada neste projeto.");
            return Result.Cancelled;
        }
        var service = new MarkingService(doc, uidoc.ActiveView);
        var items = new List<(MarkingDefinition, MarkingGeometry)>();
        foreach (var d in defs)
        {
            try { items.Add((d, service.BuildGeometry(d, out _))); }
            catch (Exception ex) { Log.Error($"Quantitativos {d.DisplayCode}", ex); }
        }
        var rows = QuantityCalculator.Compute(items, PluginContext.Catalog, PluginContext.Settings.DefaultMaterial);
        var w = new QuantitiesWindow(rows, doc.Title);
        if (UiHelpers.ShowModal(w) == true && w.CreateSchedule)
        {
            var view = CreateSchedule(doc);
            if (view != null) uidoc.ActiveView = view;
        }
        return Result.Succeeded;
    }

    private static ViewSchedule? CreateSchedule(Document doc)
    {
        using var t = new Transaction(doc, "SV - Tabela de quantitativos");
        t.Start();
        SharedParameters.Ensure(doc);
        var sched = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_GenericModel));
        var name = ScheduleName;
        var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Select(v => v.Name).ToHashSet();
        for (int i = 2; existing.Contains(name); i++) name = $"{ScheduleName} ({i})";
        sched.Name = name;

        var def = sched.Definition;
        var fields = def.GetSchedulableFields();
        ScheduleField? Add(SharedParameters.Def d, bool total = false)
        {
            var spe = SharedParameterElement.Lookup(doc, d.Guid);
            if (spe == null) return null;
            var sf = fields.FirstOrDefault(f => f.ParameterId == spe.Id);
            if (sf == null) return null;
            var field = def.AddField(sf);
            if (total) field.DisplayType = ScheduleFieldDisplayType.Totals;
            return field;
        }

        var grupo = Add(SharedParameters.Grupo);
        var codigo = Add(SharedParameters.Codigo);
        Add(SharedParameters.Descricao);
        var cor = Add(SharedParameters.Cor);
        Add(SharedParameters.Material);
        Add(SharedParameters.Area, true);
        Add(SharedParameters.Extensao, true);
        Add(SharedParameters.Quantidade, true);

        if (codigo != null)
        {
            def.AddFilter(new ScheduleFilter(codigo.FieldId, ScheduleFilterType.HasValue));
            if (grupo != null) def.AddSortGroupField(new ScheduleSortGroupField(grupo.FieldId));
            def.AddSortGroupField(new ScheduleSortGroupField(codigo.FieldId));
            if (cor != null) def.AddSortGroupField(new ScheduleSortGroupField(cor.FieldId));
        }
        def.IsItemized = false;
        def.ShowGrandTotal = true;
        def.ShowGrandTotalCount = false;
        def.ShowGrandTotalTitle = true;
        t.Commit();
        return sched;
    }
}

/// <summary>Configurações do plugin.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdConfiguracoes : CommandBase
{
    protected override bool RequiresDocument => false;

    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        UiHelpers.ShowModal(new SettingsWindow()) == true ? Result.Succeeded : Result.Cancelled;
}

/// <summary>Abre, recarrega ou restaura o catálogo normativo.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCatalogo : CommandBase
{
    protected override bool RequiresDocument => false;

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var path = PluginContext.UserCatalogPath;
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = "Catálogo normativo",
            MainContent = $"Arquivo do usuário:\n{path}\n\n{PluginContext.Catalog.Lineares.Count} marcas lineares, {PluginContext.Catalog.Hachuras.Count} zebrados, " +
                          $"{PluginContext.Catalog.Simbolos.Count} símbolos, {PluginContext.Catalog.Vagas.Count} tipos de vaga, {PluginContext.Catalog.Materiais.Count} materiais.",
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir o catálogo para edição", "Cria uma cópia editável (se ainda não existir) e abre no editor padrão de JSON.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Recarregar o catálogo", "Após salvar as alterações. Use 'Atualizar todas' para aplicar às marcas existentes.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Restaurar o catálogo padrão", "Renomeia o arquivo do usuário para .bak e volta aos valores do plugin.");
        switch (td.Show())
        {
            case TaskDialogResult.CommandLink1:
                CatalogService.EnsureUserCopy(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return Result.Succeeded;
            case TaskDialogResult.CommandLink2:
                PluginContext.ReloadCatalog();
                var problems = CatalogService.Validate(PluginContext.Catalog);
                var msg = PluginContext.CatalogError ?? (problems.Count == 0 ? "Catálogo recarregado sem problemas." : "Catálogo recarregado com avisos:\n" + string.Join("\n", problems.Take(20)));
                TaskDialog.Show(AppTitle, msg);
                return Result.Succeeded;
            case TaskDialogResult.CommandLink3:
                if (File.Exists(path)) File.Move(path, path + $".{DateTime.Now:yyyyMMddHHmmss}.bak");
                PluginContext.ReloadCatalog();
                TaskDialog.Show(AppTitle, "Catálogo padrão restaurado.");
                return Result.Succeeded;
            default:
                return Result.Cancelled;
        }
    }
}

/// <summary>Normas de referência e informações do plugin.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdSobre : CommandBase
{
    protected override bool RequiresDocument => false;

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var cat = PluginContext.Catalog;
        var sb = new StringBuilder();
        foreach (var n in cat.Normas) sb.AppendLine($"• {n.Sigla} – {n.Titulo}\n   {n.Aplicacao}");
        var colors = string.Join("\n", MarkingColors.All.Select(c => $"• {c} (Munsell {MarkingColors.Munsell(c)}): {MarkingColors.Usage(c)}"));
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = $"SinalizaBIM para Revit 2027 – versão {version}",
            MainContent = "Projeto paramétrico de sinalização viária horizontal e vertical, urbanização, moderação de tráfego, dispositivos físicos e mobiliário urbano, " +
                          "inscrições, estacionamento, dispositivos e acessibilidade, com atualização automática e quantitativos.\n\n" + cat.Aviso,
            ExpandedContent = "NORMAS DE REFERÊNCIA\n" + sb + "\nCORES (MBST)\n" + colors,
            FooterText = $"Configurações e registro: {PluginPaths.Root}",
        };
        td.Show();
        return Result.Succeeded;
    }
}
