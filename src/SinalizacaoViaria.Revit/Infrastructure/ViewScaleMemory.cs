using Autodesk.Revit.DB;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Escala de cada vista quando os detalhes dela foram desenhados (só em memória): na troca de escala, é a escala antiga do
/// ajuste no lugar. Sem registro (projeto aberto depois da última geração), a escala antiga é deduzida pelos textos.
/// </summary>
internal static class ViewScaleMemory
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Document, Dictionary<string, int>> Scales = new();

    /// <summary>Escalas comuns, para deduzir a antiga quando não há registro.</summary>
    public static readonly int[] Candidates =
        { 1, 2, 5, 10, 20, 25, 50, 75, 100, 125, 150, 200, 250, 300, 400, 500, 600, 750, 1000, 1250, 1500, 2000, 2500, 3000, 4000, 5000, 7500, 10000 };

    public static void Remember(Document doc, View view)
    {
        try { Scales.GetOrCreateValue(doc)[view.UniqueId] = view.Scale; }
        catch { /* vista sem escala */ }
    }

    /// <summary>Escala registrada da vista (nula = desconhecida).</summary>
    public static int? Known(Document doc, View view) =>
        Scales.TryGetValue(doc, out var map) && map.TryGetValue(view.UniqueId, out var s) ? s : null;
}
