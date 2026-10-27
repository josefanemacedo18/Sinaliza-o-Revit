using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Diagnóstico do Terreno (Etapa 9; painel Terreno) – somente leitura. Desde a Etapa 11 abre a tela do terreno e da topografia
/// (Prompt de Interface §14): os Toposolids lidos, o terreno original guardado pela terraplenagem e a superfície projetada (com
/// a diferença entre elas por amostragem), limites e parâmetros, o V14 (o mesmo do Validar Modelo), qual superfície cada cálculo
/// lê e o que muda com o terreno. Não altera nada: seleciona os elementos dos achados ou pede o comando escolhido para depois
/// (pelo ExternalEvent do painel contextual, porque este comando não abre transação).
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class CmdDiagnosticoTerreno : CommandBase
{
    private const string CriadoPeloPlugin = "SV - terreno criado pelo plugin";

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var hosts = TerrainModel.Hosts(doc);
        if (hosts.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhum Toposolid no projeto: não há terreno para diagnosticar.\n\n" +
                                      "Crie ou importe o Toposolid (Massa e terreno) e rode de novo. Nada foi alterado.");
            return Result.Cancelled;
        }
        // Cada Toposolid lido uma vez: o topo atual (projetada) e, se houver, o original guardado (natural).
        var lives = hosts.ToDictionary(t => t.Id, TerrainModel.Live);
        var projetado = TerrainModel.Join(lives.Values);
        var guardados = hosts.Where(TerrainModel.HasOriginal).ToList();
        var originais = guardados.ToDictionary(t => t.Id, TerrainModel.Original);
        var original = guardados.Count > 0 ? TerrainModel.Join(originais.Values) ?? new TerrainMesh() : null;
        var criterios = PluginContext.Settings.CriteriosGeometricos;
        var defs = MarkingStorage.Definitions(doc);
        // Sem faces lidas, a malha vazia dá o mesmo achado do Validar Modelo ("não há superfície do terreno").
        var achados = ModelValidator.Topografia(defs, projetado ?? new TerrainMesh(), criterios);
        var texto = TerrainDiagnostics.Relatorio(hosts.Count, projetado != null ? TerrainDiagnostics.Resumir(projetado) : null, guardados.Count,
            original != null ? TerrainDiagnostics.Resumir(original) : null, criterios.DeclividadeMaximaTerreno, criterios.FonteTexto, achados);

        // Cada Toposolid: a superfície atual, o original guardado e a área terraplenada da última vez; e o par natural × projetada.
        var lidos = new List<ToposolidLido>();
        var pares = new List<(TerrainMesh, TerrainMesh)>();
        foreach (var t in hosts)
        {
            var live = lives[t.Id];
            var orig = originais.GetValueOrDefault(t.Id);
            var tem = orig != null;
            if (orig != null) pares.Add((orig, live));
            string comentario;
            try { comentario = t.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? ""; }
            catch { comentario = ""; }
            lidos.Add(new ToposolidLido(t.UniqueId, $"Toposolid {t.Id.Value}" + (string.IsNullOrWhiteSpace(t.Name) ? "" : $" – {t.Name}"), tem,
                comentario == CriadoPeloPlugin, TerrainDiagnostics.Resumir(live), orig != null ? TerrainDiagnostics.Resumir(orig) : null,
                TerrainModel.LastFootprint(t).Sum(p => p.Area)));
        }
        var tela = TerrenoTela.Montar(lidos, projetado != null ? TerrainDiagnostics.Resumir(projetado) : null, original != null ? TerrainDiagnostics.Resumir(original) : null,
            TerrenoTela.Diferenca(pares), defs, criterios, EarthworkRecordStore.Load(doc), achados);

        var w = new TerrainWindow(tela, texto);
        if (UiHelpers.ShowModal(w) != true) return Result.Succeeded;
        if (w.Ids.Count > 0)
        {
            MarkingStorage.Copies(doc, out var elements);
            var ids = w.Ids.SelectMany(id => elements.TryGetValue(id, out var l) ? l : new List<ElementId>())
                .Where(id => doc.GetElement(id) != null).Distinct().ToList();
            if (ids.Count == 0)
            {
                TaskDialog.Show(AppTitle, "Os elementos desses achados não estão no modelo.");
                return Result.Succeeded;
            }
            uidoc.Selection.SetElementIds(ids);
            try { uidoc.ShowElements(ids); } catch { /* vista sem os elementos */ }
        }
        else if (w.Comando is { } c && !ContextPanelService.RunLater(c))
            TaskDialog.Show(AppTitle, $"Abra {RibbonBuilder.LabelOf(c) ?? c} pelo botão da faixa (o pedido não pôde ser feito agora).");
        return Result.Succeeded;
    }
}
