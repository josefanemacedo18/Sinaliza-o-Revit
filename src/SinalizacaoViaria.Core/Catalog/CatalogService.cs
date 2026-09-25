using System.Reflection;
using System.Text.Json;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Core.Catalog;

/// <summary>
/// Carrega o catálogo normativo: o padrão embutido no plugin mesclado com o arquivo do usuário
/// (itens do usuário com o mesmo código substituem os padrões; itens novos são acrescentados).
/// </summary>
public static class CatalogService
{
    public const string ResourceName = "SinalizacaoViaria.Core.catalogo-padrao.json";

    public static string DefaultJson()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Catálogo padrão não encontrado nos recursos do assembly.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static Catalogo LoadDefault() => Parse(DefaultJson());

    public static Catalogo Parse(string json) =>
        JsonSerializer.Deserialize<Catalogo>(json, JsonConfig.Options) ?? new Catalogo();

    public static string Serialize(Catalogo c) => JsonSerializer.Serialize(c, JsonConfig.Options);

    /// <summary>Carrega o padrão e aplica o arquivo do usuário, se existir. Erros do arquivo do usuário são reportados em <paramref name="error"/>.</summary>
    public static Catalogo Load(string? userFile, out string? error)
    {
        error = null;
        var cat = LoadDefault();
        if (string.IsNullOrWhiteSpace(userFile) || !File.Exists(userFile)) return cat;
        try
        {
            var user = Parse(File.ReadAllText(userFile));
            Merge(cat, user);
        }
        catch (Exception ex)
        {
            error = $"Erro ao ler o catálogo do usuário ({userFile}): {ex.Message}. O catálogo padrão foi utilizado.";
        }
        return cat;
    }

    /// <summary>Grava uma cópia do catálogo padrão para edição pelo usuário (não sobrescreve).</summary>
    public static void EnsureUserCopy(string userFile)
    {
        if (File.Exists(userFile)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
        File.WriteAllText(userFile, DefaultJson());
    }

    public static void Merge(Catalogo target, Catalogo user)
    {
        if (!string.IsNullOrWhiteSpace(user.Aviso)) target.Aviso = user.Aviso;
        MergeList(target.Normas, user.Normas, n => n.Sigla);
        MergeList(target.Lineares, user.Lineares, l => l.Codigo);
        MergeList(target.Hachuras, user.Hachuras, h => h.Codigo);
        MergeList(target.Simbolos, user.Simbolos, s => s.Codigo);
        MergeList(target.Legendas, user.Legendas, l => l.Texto);
        MergeList(target.TamanhosLetra, user.TamanhosLetra, t => t.Nome);
        MergeList(target.Vagas, user.Vagas, v => v.Codigo);
        MergeList(target.Materiais, user.Materiais, m => m.Nome);
        MergeList(target.Dispositivos, user.Dispositivos, d => d.Codigo);
    }

    private static void MergeList<T>(List<T> target, List<T>? user, Func<T, string> key)
    {
        if (user == null) return;
        foreach (var item in user)
        {
            var k = key(item);
            var idx = target.FindIndex(t => string.Equals(key(t), k, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) target[idx] = item; else target.Add(item);
        }
    }

    /// <summary>Validação básica do catálogo (retorna mensagens de problema).</summary>
    public static List<string> Validate(Catalogo c)
    {
        var msgs = new List<string>();
        foreach (var l in c.Lineares)
        {
            if (string.IsNullOrWhiteSpace(l.Codigo)) msgs.Add("Tipo linear sem código.");
            if (l.Variantes.Count == 0) msgs.Add($"{l.Codigo}: nenhuma variante definida.");
            foreach (var v in l.Variantes)
            {
                if (v.Faixas.Count == 0) msgs.Add($"{l.Codigo}/{v.Nome}: nenhuma faixa definida.");
                foreach (var f in v.Faixas)
                {
                    if (f.Largura <= 0) msgs.Add($"{l.Codigo}/{v.Nome}: largura inválida.");
                    if (f.Padrao.Any(x => x < 0)) msgs.Add($"{l.Codigo}/{v.Nome}: padrão com valor negativo.");
                    if (f.Padrao.Length > 0 && f.Padrao.Sum() <= 0) msgs.Add($"{l.Codigo}/{v.Nome}: padrão com soma zero.");
                }
            }
        }
        foreach (var h in c.Hachuras)
        {
            if (h.LarguraBarra <= 0 || h.Espacamento < 0) msgs.Add($"{h.Codigo}: dimensões de barra inválidas.");
        }
        foreach (var d in c.Dispositivos)
        {
            if (d.Largura <= 0 || d.Altura <= 0 || d.Comprimento <= 0) msgs.Add($"{d.Codigo}: dimensões inválidas.");
            if (!d.Continuo && d.Espacamento < d.Comprimento) msgs.Add($"{d.Codigo}: espaçamento menor que o comprimento.");
        }
        var dups = c.Lineares.GroupBy(l => l.Codigo, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
        msgs.AddRange(dups.Select(d => $"Código duplicado: {d}"));
        return msgs;
    }
}
