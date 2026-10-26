using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Substituição controlada de elementos urbanos (Etapa 7; Motores §12): troca o tipo (código do catálogo) de vários elementos
/// de uma vez, mantendo posição, direção, caminho, afastamento, recuos, rotação e nível. As medidas e a cor próprias ficam ou
/// voltam às do novo tipo; ao longo de um caminho, o espaçamento próprio fica ou passa a ser o do novo tipo.
/// </summary>
public static class SubstituicaoUrbana
{
    /// <summary>
    /// Troca o tipo dos elementos (no lugar: quem chama grava) e devolve os que mudaram com o que mudou. Elementos que já são
    /// do novo tipo ficam como estão.
    /// </summary>
    public static List<(UrbanElementDefinition Elemento, string Texto)> Trocar(IEnumerable<UrbanElementDefinition> elementos, MobiliarioDef novo,
        bool manterMedidas = true, bool espacamentoDoTipo = false)
    {
        var res = new List<(UrbanElementDefinition, string)>();
        foreach (var e in elementos)
        {
            if (string.Equals(e.Code, novo.Codigo, StringComparison.OrdinalIgnoreCase)) continue;
            var antes = e.Code;
            e.Code = novo.Codigo;
            var partes = new List<string> { $"{antes} → {novo.Codigo}" };
            if (!manterMedidas && (e.Length != null || e.Width != null || e.Height != null || e.Color != null))
            {
                e.Length = e.Width = e.Height = null;
                e.Color = null;
                partes.Add("medidas e cor do novo tipo");
            }
            if (espacamentoDoTipo && e.UsePath && e.Spacing != null)
            {
                e.Spacing = null;
                partes.Add("espaçamento do novo tipo");
            }
            res.Add((e, string.Join(", ", partes)));
        }
        return res;
    }
}
