using System.Text.Json;
using System.Text.Json.Serialization;

namespace SinalizacaoViaria.Core.Model;

/// <summary>Achado de uma validação anterior, como foi gravado: a chave de comparação, o código, a gravidade e a mensagem.</summary>
public sealed record AchadoGravado(string Chave, string Codigo, Severidade Severidade, string Mensagem);

/// <summary>Validação gravada: quando, com que versão do plugin e os achados (sem os "não verificado").</summary>
public sealed record ValidacaoGravada(DateTime Quando, string? Versao, List<AchadoGravado> Achados);

/// <summary>Atual × anterior: o que apareceu, o que continua e o que sumiu. <c>Anterior</c> nulo = primeira validação.</summary>
public sealed record ComparacaoValidacao(DateTime? Anterior, List<Achado> Novos, List<Achado> Persistentes, List<AchadoGravado> Resolvidos);

/// <summary>
/// Histórico da validação do modelo (Prompt Master §17 e §19: comparar antes e depois das correções; problemas novos ×
/// conhecidos). O achado é identificado pelo código, pelos Ids das marcas e pela ordem entre os de mesmo código e Ids – não
/// pela mensagem, que traz valores que mudam com a correção. Os "não verificado" não entram: não são problemas.
/// </summary>
public static class ValidationHistory
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Chave de cada achado, na ordem da lista (achados com o mesmo código e os mesmos Ids recebem #2, #3…).</summary>
    public static List<string> Chaves(IEnumerable<Achado> achados)
    {
        var vistos = new Dictionary<string, int>();
        var res = new List<string>();
        foreach (var a in achados)
        {
            var baseKey = a.Codigo + ":" + string.Join(",", a.Ids.OrderBy(x => x, StringComparer.Ordinal));
            var n = vistos[baseKey] = vistos.GetValueOrDefault(baseKey) + 1;
            res.Add(n == 1 ? baseKey : $"{baseKey}#{n}");
        }
        return res;
    }

    public static ValidacaoGravada Gravar(IReadOnlyList<Achado> achados, DateTime quando, string? versao)
    {
        var problemas = achados.Where(a => a.Severidade != Severidade.NaoVerificavel).ToList();
        var chaves = Chaves(problemas);
        return new ValidacaoGravada(quando, versao, problemas.Select((a, i) => new AchadoGravado(chaves[i], a.Codigo, a.Severidade, a.Mensagem)).ToList());
    }

    public static string ToJson(ValidacaoGravada v) => JsonSerializer.Serialize(v, Json);

    /// <summary>Lê uma validação gravada; texto vazio ou ilegível = nenhuma anterior (a comparação diz "primeira").</summary>
    public static ValidacaoGravada? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ValidacaoGravada>(json, Json); }
        catch (JsonException) { return null; }
    }

    public static ComparacaoValidacao Comparar(IReadOnlyList<Achado> atuais, ValidacaoGravada? anterior)
    {
        var problemas = atuais.Where(a => a.Severidade != Severidade.NaoVerificavel).ToList();
        if (anterior == null) return new ComparacaoValidacao(null, problemas, new List<Achado>(), new List<AchadoGravado>());
        var chaves = Chaves(problemas);
        var antes = anterior.Achados.Select(x => x.Chave).ToHashSet();
        var agora = chaves.ToHashSet();
        var novos = problemas.Where((a, i) => !antes.Contains(chaves[i])).ToList();
        var persistentes = problemas.Where((a, i) => antes.Contains(chaves[i])).ToList();
        var resolvidos = anterior.Achados.Where(x => !agora.Contains(x.Chave)).ToList();
        return new ComparacaoValidacao(anterior.Quando, novos, persistentes, resolvidos);
    }
}
