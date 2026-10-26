using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// O que uma edição deixou por refazer (Etapa 8): lido do projeto depois de gravar – a terraplenagem já reaplicada não
/// aparece; as captações lidas do modelo que ficaram desatualizadas, sim. Vira um aviso no relatório da edição.
/// </summary>
internal static class EditImpact
{
    public static RenderResult? Of(Document doc, IEnumerable<MarkingDefinition> alteradas)
    {
        try
        {
            var all = MarkingStorage.Definitions(doc);
            var ids = alteradas.Select(d => d.Id).ToHashSet();
            var atuais = all.Where(d => ids.Contains(d.Id)).ToList();
            if (atuais.Count == 0) return null;
            var impacto = EstadoDosResultados.Impacto(atuais, all, EarthworkRecordStore.Load(doc))
                .Where(i => i.Resultado is "Terraplenagem" or "Drenagem").ToList();
            if (impacto.Count == 0) return null;
            var r = new RenderResult();
            r.Warnings.Add("Ficou por refazer depois desta edição:\n" + string.Join("\n", impacto.Select(i => "• " + i.Texto)));
            return r;
        }
        catch (Exception ex)
        {
            Log.Error("Impacto da edição", ex);
            return null;
        }
    }
}
