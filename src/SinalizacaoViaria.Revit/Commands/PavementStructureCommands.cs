using System.Globalization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Estrutura do pavimento de uma via (Etapa 5; Prompt Master §10; Motores §9): o revestimento (espessura do piso da pista) e
/// as camadas sob ele, com material, espessura, função e fonte. A profundidade total é o fundo da caixa na terraplenagem, as
/// camadas vão ao corte transversal e ao quantitativo por camada. O plugin não dimensiona: os valores são do projeto.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdEstruturaPavimento : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var road = RoadWorks.PickRoad(uidoc, "Estrutura do pavimento: clique a VIA (pista, calçada ou linha)");
        if (road == null) return Result.Cancelled;
        var pav = road.Pavement;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        // Refazer a terraplenagem só faz sentido quando a via molda um terreno que já existe.
        var molda = pav.Output.Grade is { AdjustTerrain: true } && TerrainModel.Hosts(doc).Count > 0;
        // Área em planta da pista desta via, medida nas peças (para o quantitativo previsto); falhou = não mostra.
        double? area = null;
        try
        {
            var (a, falhas) = CmdCentralProjeto.MedirPavimento(doc, uidoc.ActiveView, new MarkingDefinition[] { pav });
            if (falhas == 0 && a > 0) area = a;
        }
        catch (Exception ex) { Log.Error("Estrutura do pavimento – área da via", ex); }
        var materiais = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var w = new PavementManagerWindow(pav.DisplayCode, PavementLayerQuantities.Revestimento(pav.Material), pav.ActualThickness * 100, pav.Estrutura?.Clone(),
            area, materiais.Contains, materiais, molda, PluginContext.Settings.ModelosEstruturaPavimento);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;

        var lida = w.Resultado ?? new EstruturaPavimento();
        var revestimento = w.RevestimentoCm;
        var refazer = molda && w.RefazerTerraplenagem;
        pav.Estrutura = w.Resultado;
        // Espessura própria só quando muda (sem mudança fica a padrão do material).
        if (Math.Abs(revestimento / 100 - pav.ActualThickness) > 1e-6) pav.Thickness = revestimento / 100;
        var res = MarkingCreator.Commit(uidoc, new MarkingDefinition[] { pav }, "SV - Estrutura do pavimento");
        if (refazer) TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { pav }, quiet: true);
        var resumo = new RenderResult();
        resumo.Warnings.Add(pav.ProfundidadeEstrutura is { } prof
            ? $"{pav.DisplayCode}: revestimento {(pav.ActualThickness * 100).ToString("0.#", pt)} cm + {lida.Camadas.Count} camada(s) = " +
              $"{prof.ToString("0.00", pt)} m até o fundo da estrutura{(lida.Fonte != null ? $" (fonte: {lida.Fonte})" : " – sem fonte informada")}." +
              (molda ? refazer ? " Terraplenagem refeita." : " Terraplenagem NÃO refeita: rode Terraplenagem para o terreno acompanhar." : "")
            : $"{pav.DisplayCode}: sem estrutura informada – a terraplenagem usa o subleito das opções dela.");
        res.Insert(0, resumo);
        if (EditImpact.Of(doc, new[] { pav }) is { } impact) res.Add(impact);
        Report("Estrutura do pavimento", res.Where(r => r.Warnings.Count > 0).ToList(), alwaysShow: true);
        return Result.Succeeded;
    }
}
