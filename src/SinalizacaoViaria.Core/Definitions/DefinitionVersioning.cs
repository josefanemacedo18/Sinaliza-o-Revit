using System.Text.Json;
using System.Text.Json.Nodes;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>Situação da definição lida em relação à versão de dados deste plugin.</summary>
public enum EstadoVersao
{
    /// <summary>Gravada na versão atual.</summary>
    Atual,
    /// <summary>Gravada numa versão anterior e migrada passo a passo até a atual.</summary>
    Migrada,
    /// <summary>
    /// Gravada por uma versão MAIS NOVA do plugin: lida para consulta, mas não pode ser regravada (os campos que esta versão
    /// não conhece se perderiam).
    /// </summary>
    MaisNova,
    /// <summary>JSON ilegível ou tipo desconhecido.</summary>
    Invalida,
}

/// <summary>Resultado da leitura versionada de uma definição.</summary>
public sealed record DefinicaoLida(MarkingDefinition? Definition, int StoredVersion, EstadoVersao Estado, string? Erro = null);

/// <summary>
/// Versão dos dados das definições (<see cref="MarkingDefinition.Version"/>) e migrações entre versões. Cada passo leva o JSON
/// da versão N para N + 1 (altera o objeto no lugar); a leitura aplica os passos em ordem até
/// <see cref="MarkingDefinition.CurrentVersion"/>. Ao mudar o formato de uma definição: incremente CurrentVersion e registre o
/// passo em <see cref="Passos"/> com um teste de ida e volta.
/// </summary>
public static class DefinitionVersioning
{
    /// <summary>Passos de migração por versão de origem. Versão 1 é a primeira com dados gravados: ainda não há passos.</summary>
    internal static readonly SortedDictionary<int, Action<JsonObject>> Passos = new();

    /// <summary>Gravada por versão mais nova do plugin (não regravar).</summary>
    public static bool IsNewer(MarkingDefinition d) => d.Version > MarkingDefinition.CurrentVersion;

    /// <summary>Mensagem para o usuário quando uma definição de versão mais nova seria regenerada ou regravada.</summary>
    public static string NewerMessage(MarkingDefinition d) =>
        $"{d.DisplayCode}: gravado por uma versão mais nova do SinalizaBIM (dados v{d.Version}, este plugin lê até " +
        $"v{MarkingDefinition.CurrentVersion}). Não foi alterado para não perder informações – atualize o plugin.";

    /// <summary>Lê o JSON gravado, migrando de versões anteriores. Nunca lança exceção.</summary>
    public static DefinicaoLida Load(string? json) => Load(json, Passos, MarkingDefinition.CurrentVersion);

    internal static DefinicaoLida Load(string? json, IReadOnlyDictionary<int, Action<JsonObject>> passos, int atual)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(null, 0, EstadoVersao.Invalida, "vazio");
        try
        {
            var stored = StoredVersion(json);
            if (stored == null) return new(null, 0, EstadoVersao.Invalida, "não é um objeto JSON");
            var v0 = stored.Value;
            MarkingDefinition? def;
            if (v0 >= atual)
                // Caminho comum (leitura de todos os elementos a cada atualização): direto do texto, sem montar a árvore.
                def = JsonSerializer.Deserialize<MarkingDefinition>(json, JsonConfig.Compact);
            else
            {
                if (JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) is not JsonObject obj)
                    return new(null, v0, EstadoVersao.Invalida, "não é um objeto JSON");
                for (var v = v0; v < atual; v++)
                    if (passos.TryGetValue(v, out var passo)) passo(obj);
                SetVersion(obj, atual);
                def = obj.Deserialize<MarkingDefinition>(JsonConfig.Compact);
            }
            if (def == null) return new(null, v0, EstadoVersao.Invalida, "definição nula");
            var estado = v0 == atual ? EstadoVersao.Atual : v0 > atual ? EstadoVersao.MaisNova : EstadoVersao.Migrada;
            // A versão gravada vale mais que o padrão do construtor: é ela que impede regravar dados mais novos.
            def.Version = estado == EstadoVersao.MaisNova ? v0 : atual;
            return new(def, v0, estado);
        }
        catch (Exception ex)
        {
            // Nunca lança: dado ilegível vira estado Inválida (quem chama decide registrar e ignorar o elemento).
            return new(null, 0, EstadoVersao.Invalida, ex.Message);
        }
    }

    /// <summary>
    /// Versão gravada (propriedade "version" do objeto de topo, lida sem montar a árvore); ausente = 1 (dados anteriores ao
    /// campo eram da versão 1). Nulo = o texto não é um objeto JSON.
    /// </summary>
    private static int? StoredVersion(string json)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json),
            new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isVersion = string.Equals(reader.GetString(), "version", StringComparison.OrdinalIgnoreCase);
            reader.Read();
            if (isVersion)
            {
                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var i)) return i;
                if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out i)) return i;
                return 1;
            }
            reader.Skip();
        }
        return 1;
    }

    private static void SetVersion(JsonObject obj, int v)
    {
        var key = obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, "version", StringComparison.OrdinalIgnoreCase));
        obj[key ?? "version"] = v;
    }
}
