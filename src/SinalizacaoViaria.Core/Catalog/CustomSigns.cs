using System.Text.Json;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Core.Catalog;

/// <summary>
/// Placas personalizadas do projeto: criadas pelo usuário (código, nome, forma, tamanho, cores e legenda), guardadas no
/// próprio projeto e acrescentadas ao catálogo – a placa entra no modelo, na legenda, no detalhe, no quadro de placas e nos
/// quantitativos como as do catálogo normativo.
/// </summary>
public static class CustomSigns
{
    /// <summary>Placa personalizada nova (legenda em linhas separadas por "\n"; a orla é 6 % da largura).</summary>
    public static PlacaDef Create(string code, string name, FormaPlaca shape, double width, double height, MarkingColor background,
        MarkingColor border, string? legend, MarkingColor legendColor, CategoriaPlaca category = CategoriaPlaca.Indicacao, string description = "") => new()
    {
        Codigo = code.Trim(),
        Nome = string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim(),
        Categoria = category,
        Forma = shape,
        Largura = width,
        Altura = shape is FormaPlaca.Retangulo ? height : width,
        CorFundo = background,
        CorOrla = border,
        Orla = 0.06,
        Legenda = string.IsNullOrWhiteSpace(legend) ? null : legend,
        CorLegenda = legendColor,
        Descricao = description,
        Referencia = "Placa personalizada do projeto",
        Personalizada = true,
    };

    /// <summary>
    /// Pictograma de uma placa do catálogo (cópia independente, com a tarja de proibição se houver) ou nenhum
    /// (<paramref name="from"/> nulo). As linhas da legenda continuam: o pictograma vai à esquerda (ou em cima) e o texto no resto.
    /// </summary>
    public static PlacaDef WithPictogram(PlacaDef p, PlacaDef? from)
    {
        p.Pictograma = from?.Pictograma is { Count: > 0 } pic
            ? JsonSerializer.Deserialize<List<PictoItem>>(JsonSerializer.Serialize(pic, JsonConfig.Compact), JsonConfig.Compact)
            : null;
        p.Proibicao = from?.Proibicao == true;
        return p;
    }

    /// <summary>Problemas da placa (vazio = pode ser salva). Não pode usar o código de uma placa do catálogo normativo.</summary>
    public static List<string> Validate(PlacaDef p, Catalogo catalog)
    {
        var res = new List<string>();
        if (string.IsNullOrWhiteSpace(p.Codigo)) res.Add("Informe o código da placa.");
        else if (catalog.Placa(p.Codigo) is { Personalizada: false }) res.Add($"O código {p.Codigo} já é de uma placa do catálogo – use outro (ex.: {p.Codigo}-P).");
        if (p.Largura <= 0.05 || p.Largura > 10) res.Add("Largura entre 0,05 e 10 m.");
        if (p.Forma == FormaPlaca.Retangulo && (p.Altura <= 0.05 || p.Altura > 10)) res.Add("Altura entre 0,05 e 10 m.");
        return res;
    }

    public static string Serialize(IEnumerable<PlacaDef> signs) => JsonSerializer.Serialize(signs.ToList(), JsonConfig.Compact);

    public static List<PlacaDef> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        var list = JsonSerializer.Deserialize<List<PlacaDef>>(json, JsonConfig.Compact) ?? new();
        foreach (var p in list) p.Personalizada = true;
        return list;
    }

    /// <summary>Acrescenta (ou atualiza, pelo código) as placas personalizadas no catálogo. Não substitui placas normativas.</summary>
    public static void Apply(Catalogo catalog, IEnumerable<PlacaDef> custom)
    {
        foreach (var p in custom)
        {
            if (string.IsNullOrWhiteSpace(p.Codigo)) continue;
            p.Personalizada = true;
            var i = catalog.Placas.FindIndex(x => string.Equals(x.Codigo, p.Codigo, StringComparison.OrdinalIgnoreCase));
            if (i < 0) catalog.Placas.Add(p);
            else if (catalog.Placas[i].Personalizada) catalog.Placas[i] = p;
        }
    }

    /// <summary>Junta a placa à lista do projeto (substitui a de mesmo código).</summary>
    public static List<PlacaDef> Upsert(IEnumerable<PlacaDef> list, PlacaDef p)
    {
        var res = list.Where(x => !string.Equals(x.Codigo, p.Codigo, StringComparison.OrdinalIgnoreCase)).ToList();
        p.Personalizada = true;
        res.Add(p);
        return res.OrderBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
