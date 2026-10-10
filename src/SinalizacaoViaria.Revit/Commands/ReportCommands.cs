using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
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
        var families = FamilyClassifier.Collect(doc);
        if (defs.Count == 0 && families.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhuma marca de sinalização encontrada neste projeto.");
            return Result.Cancelled;
        }
        var (items, rows) = Collect(doc, uidoc.ActiveView, defs, families);
        // Miniaturas da própria sinalização (face da placa, trecho da linha, unidade do dispositivo, amostra de material).
        var thumbs = QuantityThumbnails.Build(items);
        var w = new QuantitiesWindow(rows, doc.Title, thumbs);
        if (UiHelpers.ShowModal(w) == true && (w.CreateSchedule || w.SchedulesByCategory))
        {
            // Tabela nativa: roda com qualquer vista ativa (inclusive folha) e vai direto para a folha escolhida.
            var place = TablePlacementWindow.Ask(doc, uidoc.ActiveView, "tabela", ScheduleName, schedule: true);
            if (place == null) return Result.Cancelled;
            CreateSchedules(uidoc, rows, thumbs, w.CreateSchedule, w.SchedulesByCategory ? w.Categories : Array.Empty<CategoriaQuantitativo>(), place);
        }
        return Result.Succeeded;
    }

    /// <summary>
    /// Itens e quadro de quantidades do projeto (marcas do plugin, pisos com hierarquia e famílias classificadas) – o mesmo
    /// quadro da janela, das tabelas do Revit, do memorial descritivo e das pranchas.
    /// </summary>
    internal static (List<(MarkingDefinition Def, MarkingGeometry Geo)> Items, List<QuantityRow> Rows) Collect(Document doc, View? view,
        IReadOnlyList<MarkingDefinition>? defs = null, List<FamilyItem>? families = null)
    {
        defs ??= MarkingStorage.Definitions(doc);
        families ??= FamilyClassifier.Collect(doc);
        var service = new MarkingService(doc, view);
        var items = new List<(MarkingDefinition, MarkingGeometry)>();
        foreach (var d in defs)
        {
            try { items.Add((d, service.QuantityGeometry(d))); }
            catch (Exception ex) { Log.Error($"Quantitativos {d.DisplayCode}", ex); }
        }
        // Pisos desenhados à mão com hierarquia definida (Hierarquia e Esquinas): entram como pavimento da via.
        foreach (var f in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().Where(f => !MarkingStorage.IsMarking(f)))
        {
            var label = f.get_Parameter(SharedParameters.Hierarquia.Guid)?.AsString();
            if (string.IsNullOrWhiteSpace(label)) continue;
            var h = Hierarquia.Definidas.Cast<HierarquiaViaria?>().FirstOrDefault(x => Hierarquia.Label(x) == label);
            var pv = new RoadPavementDefinition { Hierarchy = h };
            var g = new MarkingGeometry();
            g.AreaOverrides[MarkingColor.Asfalto] = MarkingService.FloorArea(f);
            items.Add((pv, g));
        }
        var rows = QuantityCalculator.Compute(items, PluginContext.Catalog, PluginContext.Settings.DefaultMaterial);
        // Famílias do Revit do usuário classificadas como elementos urbanos (Elementos Urbanos → Famílias do Revit).
        if (families.Count > 0) rows = QuantityCalculator.AddFamilies(rows, families);
        return (items, rows);
    }

    private static void CreateSchedules(UIDocument uidoc, IReadOnlyList<QuantityRow> rows, IReadOnlyDictionary<string, System.Windows.Media.Imaging.BitmapSource> thumbs,
        bool general, IEnumerable<CategoriaQuantitativo> categories, TablePlacement place)
    {
        var doc = uidoc.Document;
        var target = place.SheetId != null ? doc.GetElement(place.SheetId) as ViewSheet : null;
        XYZ? at = null;
        if (place.PickOnSheet && target != null && uidoc.ActiveView.Id == target.Id)
        {
            try
            {
                at = Picking.PickPoint(uidoc, "Clique na folha o canto superior esquerdo da tabela");
                if (at == null) return;
            }
            catch (UserMessageException)
            {
                // Folha sem plano para o clique: lugar livre no alto à direita.
            }
        }
        var created = new List<ViewSchedule>();
        var errors = new List<string>();
        var notes = new List<string>();
        ViewSheet? sheet = null;
        (int Elements, int Images) prep = (0, 0);

        // 1) Parâmetros das colunas e imagens nos elementos (transação própria: um erro aqui não impede a tabela).
        try
        {
            using var t = new Transaction(doc, "SV - Preparar quantitativos");
            t.Start();
            prep = QuantitySchedule.Prepare(doc, rows, thumbs);
            t.Commit();
        }
        catch (Exception ex)
        {
            Log.Error("Preparar quantitativos", ex);
            notes.Add("preparação dos elementos: " + ex.Message);
        }

        // 2) Uma transação por tabela.
        void Try(CategoriaQuantitativo? c)
        {
            try
            {
                using var t = new Transaction(doc, "SV - Tabela de quantitativos");
                t.Start();
                var v = QuantitySchedule.Create(doc, c, notes);
                t.Commit();
                created.Add(v);
            }
            catch (Exception ex)
            {
                Log.Error("Tabela de quantitativos", ex);
                errors.Add((c is { } cc ? QuantityRow.CategoryLabel(cc) : "Quantitativos") + ": " + ex.Message);
            }
        }
        if (general) Try(null);
        foreach (var c in categories) Try(c);
        // 3) Na folha escolhida (ou numa nova): cada tabela num lugar livre, a primeira no ponto clicado.
        if (place.OnSheet && created.Count > 0)
        {
            try
            {
                using var t = new Transaction(doc, "SV - Tabela na folha");
                t.Start();
                sheet = target ?? ProjectTableHost.NewSheet(doc, "SV-Q01", "Quantitativos de sinalização");
                for (int i = 0; i < created.Count; i++) ProjectTableHost.Place(doc, created[i], sheet, i == 0 ? at : null, notes);
                t.Commit();
            }
            catch (Exception ex)
            {
                Log.Error("Tabela na folha", ex);
                notes.Add("folha: " + ex.Message);
                sheet = null;
            }
        }
        if (created.Count > 0)
        {
            try { if (sheet == null) uidoc.ActiveView = created[0]; else if (uidoc.ActiveView.Id != sheet.Id) uidoc.ActiveView = sheet; }
            catch (Exception ex) { Log.Error("Abrir tabela", ex); }
        }
        var lines = created.Select(v => $"• {v.Name} – {QuantitySchedule.Rows(v)} linha(s)").ToList();
        var msg = created.Count > 0
            ? "Tabela(s) criada(s) no Revit (Navegador de projeto → Tabelas/Quantidades):\n" + string.Join("\n", lines) +
              $"\n\n{prep.Elements} elemento(s) de sinalização; {prep.Images} com imagem." +
              (sheet != null ? $"\nFolha {sheet.SheetNumber} – {sheet.Name}: a coluna Imagem mostra as miniaturas quando a tabela está na folha (limitação do Revit)." : "")
            : "Nenhuma tabela foi criada.";
        if (errors.Count > 0) msg += "\n\nProblemas:\n" + string.Join("\n", errors);
        if (notes.Count > 0) msg += "\n\nObservações:\n• " + string.Join("\n• ", notes.Distinct().Take(12));
        TaskDialog.Show(AppTitle, msg);
    }
}

/// <summary>Configurações do plugin.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdConfiguracoes : CommandBase
{
    protected override bool RequiresDocument => false;

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new SettingsWindow();
        var ok = UiHelpers.ShowModal(w) == true;
        if (w.RunAutoTest)
        {
            if (uidoc?.Document == null) { UiHelpers.Error("Abra um projeto (de preferência uma cópia ou um arquivo vazio) para rodar o Autoteste."); return Result.Cancelled; }
            return CmdAutoteste.Start(app, uidoc);
        }
        return ok ? Result.Succeeded : Result.Cancelled;
    }
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
