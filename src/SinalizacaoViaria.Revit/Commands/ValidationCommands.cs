using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Validar Modelo: confere as definições gravadas no projeto (ModelValidator do núcleo) sem alterar nada – modo somente
/// leitura. Grava o relatório em TXT e a validação no histórico e abre a Central de Qualidade (Etapa 12): a tabela dos achados
/// com filtros, os resolvidos desde a anterior, o estado dos resultados e o último Autoteste; a seleção escolhida lá volta ao
/// modelo.
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
        // Estado dos resultados derivados (Etapa 8): terraplenagem, captações lidas do modelo e o que é refeito ou calculado na hora.
        var defs = new List<MarkingDefinition>();
        IReadOnlyList<EstadoResultado>? estados = null;
        try
        {
            defs = MarkingStorage.Definitions(doc);
            estados = EstadoDosResultados.Calcular(defs, EarthworkRecordStore.Load(doc));
        }
        catch (Exception ex) { Log.Error("Estado dos resultados", ex); }
        var text = ModelValidator.Report(achados, marcas, copias.Count, doc.Title, now, version, cmp, estados);
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

        // Central de Qualidade: as marcas pelo nome (vias e nós pela ordem, as demais pelo tipo e código); a que tem elementos mas não
        // se lê é dita ilegível, não apagada.
        var nomes = NomesDasMarcas.Montar(defs);
        string? Nome(string id) => nomes.TryGetValue(id, out var n) ? n
            : elements.ContainsKey(id) ? $"{(id.Length > 8 ? id[..8] : id)} (dados ilegíveis)" : null;
        var resumo = CentralQualidade.Montar(achados, previous, Nome, doc.Title, now, version, marcas, copias.Count);
        var w = new QualityCenterWindow(resumo, path, estados, UltimoAutoteste());
        if (UiHelpers.ShowModal(w) != true || w.Ids.Count == 0) return Result.Succeeded;
        var ids = w.Ids.SelectMany(id => elements.TryGetValue(id, out var l) ? l : new List<ElementId>())
            .Where(id => doc.GetElement(id) != null).Distinct().ToList();
        if (ids.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Os elementos dessas linhas não estão no modelo (a marca foi apagada ou não tem elementos).");
            return Result.Succeeded;
        }
        uidoc.Selection.SetElementIds(ids);
        try { uidoc.ShowElements(ids); } catch { /* vista sem os elementos */ }
        return Result.Succeeded;
    }

    /// <summary>O relatório mais recente do Autoteste neste computador (testes do plugin); nulo se não há ou não se lê.</summary>
    internal static ResumoAutoteste? UltimoAutoteste()
    {
        try
        {
            if (!Directory.Exists(PluginPaths.Root)) return null;
            var arquivo = RelatorioAutoteste.MaisRecente(Directory.GetFiles(PluginPaths.Root, "autoteste-*.txt"));
            return arquivo == null ? null : RelatorioAutoteste.Ler(arquivo, File.ReadAllText(arquivo));
        }
        catch (Exception ex)
        {
            Log.Error("Validar Modelo – Autoteste", ex);
            return null;
        }
    }

    /// <summary>Arquivo da última validação do projeto, pelo caminho do arquivo (ou pelo título, se ainda não foi salvo).</summary>
    internal static string HistoryPath(Document doc)
    {
        var key = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.ToLowerInvariant())))[..16];
        return Path.Combine(PluginPaths.Root, $"validacao-ultima-{hash}.json");
    }
}
