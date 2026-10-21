using System.Text.Json.Serialization;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>
/// Tipo de uma definição gravada lido do começo do JSON, sem interpretar o resto: o System.Text.Json grava o discriminador
/// (a propriedade "tipo" de <see cref="MarkingDefinition"/>) como primeira propriedade. Serve para varrer o projeto atrás de
/// um tipo (interseções, rotatórias, vias) sem desserializar milhares de marcas de outros tipos – cada desserialização custa
/// ~60 µs e o plugin varre o projeto dezenas de vezes por operação. Na dúvida (texto num formato inesperado, discriminador
/// desconhecido) a resposta é "pode ser": quem chama interpreta o JSON e confere o tipo como antes. Se uma migração
/// (<see cref="DefinitionVersioning"/>) renomear um discriminador, o nome antigo fica desconhecido aqui e cai nesse caminho
/// seguro; só não pode ser reaproveitado para outro tipo.
/// </summary>
public static class DefinitionKinds
{
    /// <summary>Nome da propriedade do discriminador, lido do próprio atributo (JsonPolymorphic) – entre aspas.</summary>
    private static readonly string Key = "\"" + (((JsonPolymorphicAttribute?)typeof(MarkingDefinition)
        .GetCustomAttributes(typeof(JsonPolymorphicAttribute), false).FirstOrDefault())?.TypeDiscriminatorPropertyName ?? "$type") + "\"";

    private static readonly Dictionary<string, Type> ByName = typeof(MarkingDefinition)
        .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), false).Cast<JsonDerivedTypeAttribute>()
        .Where(a => a.TypeDiscriminator is string)
        .ToDictionary(a => (string)a.TypeDiscriminator!, a => a.DerivedType, StringComparer.Ordinal);

    /// <summary>Discriminador gravado no começo do JSON, ou nulo se o texto não começa por ele.</summary>
    public static string? Discriminator(string? json)
    {
        if (json == null) return null;
        var i = Skip(json, 0);
        if (i >= json.Length || json[i] != '{') return null;
        i = Skip(json, i + 1);
        if (string.CompareOrdinal(json, i, Key, 0, Key.Length) != 0) return null;
        i = Skip(json, i + Key.Length);
        if (i >= json.Length || json[i] != ':') return null;
        i = Skip(json, i + 1);
        if (i >= json.Length || json[i] != '"') return null;
        var end = json.IndexOf('"', i + 1);
        if (end < 0) return null;
        var name = json.Substring(i + 1, end - i - 1);
        return name.Contains('\\') ? null : name;
    }

    /// <summary>Tipo da definição gravada, ou nulo quando só interpretando o JSON dá para saber.</summary>
    public static Type? TypeOf(string? json) => Discriminator(json) is { } k && ByName.TryGetValue(k, out var t) ? t : null;

    /// <summary>Falso só quando o começo do JSON garante que a definição não é <typeparamref name="T"/>.</summary>
    public static bool MayBe<T>(string? json) where T : class => TypeOf(json) is not { } t || typeof(T).IsAssignableFrom(t);

    private static int Skip(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i;
    }
}
