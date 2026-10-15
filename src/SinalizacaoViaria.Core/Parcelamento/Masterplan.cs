using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>
/// Masterplan (layout limpo): ficam os lotes por categoria, as vias, calçadas, canteiros, rampas, árvores e mobiliário, os nomes
/// das quadras e das vias e os números dos lotes; saem a sinalização, os dispositivos, o piso tátil, a drenagem, as cotas e os
/// textos técnicos.
/// </summary>
public static class Masterplan
{
    public const string NomeModelo = "SV - Masterplan";
    public const string PrefixoVista = "SV - Masterplan";

    /// <summary>Categorias do plugin (parâmetro SV_Categoria) que o masterplan oculta inteiras.</summary>
    public static readonly CategoriaQuantitativo[] Ocultas =
    {
        CategoriaQuantitativo.SinalizacaoHorizontal, CategoriaQuantitativo.SinalizacaoVertical, CategoriaQuantitativo.DispositivosSegregacao,
        CategoriaQuantitativo.Drenagem,
    };

    /// <summary>
    /// Acessibilidade sai só em parte: o piso tátil (rotas e alertas, grupo Acessibilidade) sai; as rampas (grupo Urbanização, mesma
    /// categoria) ficam – fazem parte da calçada desenhada.
    /// </summary>
    public static readonly (CategoriaQuantitativo Categoria, GrupoMarca Grupo) Tatil = (CategoriaQuantitativo.Acessibilidade, GrupoMarca.Acessibilidade);

    /// <summary>Valor de SV_Categoria dos detalhes do plugin (cotas, quadros, legendas) – também ocultos.</summary>
    public const string Detalhamento = "Detalhamento";

    /// <summary>
    /// Valor de SV_Categoria do detalhamento dos lotes (números, letra da quadra no círculo, cores): fica visível no masterplan.
    /// </summary>
    public const string Loteamento = "Loteamento";

    public static bool Oculta(CategoriaQuantitativo c) => Ocultas.Contains(c);

    /// <summary>A marca sai do masterplan: categoria oculta inteira ou piso tátil (as rampas ficam).</summary>
    public static bool Oculta(MarkingDefinition def, MarkingInfo info)
    {
        var c = QuantityRow.Categorize(def, info.Group);
        return Oculta(c) || c == Tatil.Categoria && info.Group == Tatil.Grupo;
    }

    /// <summary>Valores de SV_Categoria ocultos (regras do filtro do modelo de vista).</summary>
    public static IEnumerable<string> ValoresOcultos => Ocultas.Select(QuantityRow.CategoryLabel).Append(Detalhamento);

    /// <summary>Regra do piso tátil no filtro: SV_Categoria igual à categoria E SV_Grupo igual ao grupo.</summary>
    public static (string Categoria, string Grupo) RegraTatil => (QuantityRow.CategoryLabel(Tatil.Categoria), QuantityRow.GroupLabel(Tatil.Grupo));

    /// <summary>Detalhamento do masterplan: letra da quadra no círculo, nomes das vias e números dos lotes, sem áreas, cotas nem categorias.</summary>
    public static Definitions.LoteamentoDetalheDefinition Detalhe(string loteamentoId) => new()
    {
        LoteamentoId = loteamentoId,
        Numero = true,
        Area = false,
        Cotas = false,
        Categoria = false,
        Quadra = true,
        Vias = true,
        Divisas = false,
        Cores = false,
    };
}
