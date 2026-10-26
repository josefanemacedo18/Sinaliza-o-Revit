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
        var est = pav.Estrutura?.Clone() ?? new EstruturaPavimento();
        var revestimento = pav.ActualThickness * 100;
        var camadasTxt = est.Texto();
        var fonte = est.Fonte ?? "";
        // Refazer a terraplenagem só faz sentido quando a via molda um terreno que já existe.
        var molda = pav.Output.Grade is { AdjustTerrain: true } && TerrainModel.Hosts(doc).Count > 0;
        var refazer = molda;
        var w = new FormWindow("Estrutura do pavimento", $"Estrutura do pavimento – {pav.DisplayCode}",
                "Camadas da pista, de cima para baixo. O revestimento é o próprio piso da via; abaixo dele, base, sub-base, reforço ou regularização. " +
                "A profundidade total (revestimento + camadas) é o fundo da caixa da terraplenagem sob a pista desta via – e das interseções, rotatórias " +
                "e balões ligados a ela, pela via mais funda –; as camadas vão ao corte transversal e ao quantitativo por camada.\n" +
                "O plugin não dimensiona pavimento: informe as espessuras do projeto de pavimentação e a fonte. Sem camadas, a terraplenagem usa o " +
                "subleito das opções dela (0,40 m por padrão).",
                null, null, false, "Aplicar", 820, 600)
            .Section("Revestimento (o piso da pista)")
            .Number("Espessura do revestimento (cm)", () => revestimento, v => revestimento = v, 1, 50, "0.#",
                $"Material da via: {PavementLayerQuantities.Revestimento(pav.Material)}. Muda a espessura do piso da pista.")
            .Section("Camadas sob o revestimento",
                "Uma por linha: nome; espessura (cm); material; função (base, sub-base, reforço do subleito, regularização, outra). Vazio = sem estrutura informada.")
            .Text("Camadas", () => camadasTxt, v => camadasTxt = v ?? "", true,
                "Ex.:\nBase; 15; brita graduada simples; base\nSub-base; 20; solo-brita; sub-base")
            .Text("Fonte (projeto de pavimentação, dimensionamento)", () => fonte, v => fonte = v ?? "")
            .If(() => molda, x => x.Check("Refazer a terraplenagem com a nova profundidade", () => refazer, v => refazer = v,
                "Sem refazer, o Toposolid fica com a caixa antiga e a Validação acusa a terraplenagem desatualizada (V15)."));
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;

        var (lida, problemas) = EstruturaPavimento.Ler(camadasTxt);
        if (!(revestimento >= 1 && revestimento <= 50)) problemas.Add("espessura do revestimento fora de 1 a 50 cm");
        if (problemas.Count > 0)
        {
            TaskDialog.Show(AppTitle, "Estrutura do pavimento não aplicada – nada foi alterado:\n• " + string.Join("\n• ", problemas));
            return Result.Cancelled;
        }
        lida.Fonte = string.IsNullOrWhiteSpace(fonte) ? null : fonte.Trim();
        pav.Estrutura = lida.Camadas.Count > 0 ? lida : null;
        // Espessura própria só quando muda (sem mudança fica a padrão do material).
        if (Math.Abs(revestimento / 100 - pav.ActualThickness) > 1e-6) pav.Thickness = revestimento / 100;
        var res = MarkingCreator.Commit(uidoc, new MarkingDefinition[] { pav }, "SV - Estrutura do pavimento");
        if (molda && refazer) TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { pav }, quiet: true);
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
