using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>
/// "Marca" (parâmetro do Revit) dos lotes e das áreas remanescentes. Com um loteamento só no projeto continua "B-3"
/// (quadra-lote). Com dois ou mais, as marcas se repetiam entre os loteamentos e o Revit avisava "Os elementos têm valores
/// de 'Marca' duplicados" em cada par (Autoteste de 2026-10-19 em docs/evidencias: 65 avisos numa etapa) – então a marca
/// ganha o nome do loteamento na frente ("Jardim B-3") e, se dois loteamentos têm o mesmo nome, também o início do id
/// ("Jardim-23ef B-3"). O código do lote (SV_Codigo, quadros e memorial) não muda.
/// </summary>
public static class LotMarks
{
    /// <summary>Prefixo das marcas de <paramref name="lot"/> entre <paramref name="todos"/> os loteamentos do projeto ("" se é o único).</summary>
    public static string Prefixo(LoteamentoDefinition lot, IEnumerable<LoteamentoDefinition> todos)
    {
        var outros = todos.Where(l => l.Id != lot.Id).ToList();
        if (outros.Count == 0) return "";
        var nome = Nome(lot);
        var homonimos = outros.Where(o => string.Equals(Nome(o), nome, StringComparison.OrdinalIgnoreCase)).ToList();
        if (homonimos.Count == 0) return nome;
        for (var k = 4; k < lot.Id.Length; k += 4)
            if (homonimos.All(o => !o.Id.StartsWith(lot.Id[..k], StringComparison.OrdinalIgnoreCase))) return $"{nome}-{lot.Id[..k]}";
        return $"{nome}-{lot.Id}";
    }

    /// <summary>Marca do lote: "B-3" ou "Jardim B-3".</summary>
    public static string Lote(string prefixo, string quadra, string numero) => Junta(prefixo, $"{quadra}-{numero}");

    /// <summary>Marca da área remanescente: "AV-R" ou "Jardim AV-R".</summary>
    public static string Remanescente(string prefixo, string numero) => Junta(prefixo, numero);

    private static string Junta(string prefixo, string marca) => prefixo.Length == 0 ? marca : $"{prefixo} {marca}";

    private static string Nome(LoteamentoDefinition l) => string.IsNullOrWhiteSpace(l.Nome) ? "Loteamento" : l.Nome.Trim();
}
