using System.Globalization;
using System.Text;

namespace SinalizacaoViaria.Core.Model;

/// <summary>
/// Aparência realista dos materiais do plugin (pedido de 2026-10-27, item 1): para cada material com textura na realidade
/// (asfalto, concreto, bloquete, terra, brita, grama, metal, vidro, madeira, água) o ativo de aparência é escolhido entre os da
/// <b>biblioteca de materiais instalada com o Revit</b> (nada de textura de fora), pelo título – em inglês ou português, sem
/// acento. A escala é a do próprio ativo da biblioteca (escala real). A cor do material (Cores consistentes, Sombreado) não
/// muda. As pinturas (branca, amarela...) continuam na cor lisa do material, que é o aspecto real da tinta.
/// </summary>
public static class MaterialAppearance
{
    /// <summary>Ativo da biblioteca: nome interno, título (o que o navegador de materiais mostra) e biblioteca.</summary>
    public sealed record Candidate(string Name, string Title, string Library);

    /// <summary>
    /// Palavras do título, em ordem de preferência: o primeiro grupo com algum ativo cujo título tenha TODAS as palavras do
    /// grupo vence (palavra inteira, aceito o plural). Vazio = o material fica na cor lisa.
    /// </summary>
    public static IReadOnlyList<string[]> Keywords(MarkingColor c) => c switch
    {
        MarkingColor.Asfalto => new[] { new[] { "asphalt" }, new[] { "asfalto" } },
        MarkingColor.Concreto or MarkingColor.PavimentoConcreto => new[]
        {
            new[] { "concrete", "cast" }, new[] { "concreto", "moldado" }, new[] { "concrete" }, new[] { "concreto" },
        },
        MarkingColor.Bloquete => new[]
        {
            new[] { "paver" }, new[] { "paving" }, new[] { "intertravado" }, new[] { "bloquete" }, new[] { "paralelepipedo" }, new[] { "cobblestone" },
        },
        MarkingColor.PavimentoTerra or MarkingColor.Terra => new[] { new[] { "soil" }, new[] { "dirt" }, new[] { "earth" }, new[] { "solo" }, new[] { "terra" } },
        MarkingColor.Brita => new[] { new[] { "gravel" }, new[] { "brita" }, new[] { "crushed", "stone" }, new[] { "cascalho" } },
        MarkingColor.Grama => new[] { new[] { "grass" }, new[] { "grama" }, new[] { "lawn" }, new[] { "turf" } },
        MarkingColor.Folhagem => new[] { new[] { "foliage" }, new[] { "folhagem" }, new[] { "hedge" } },
        MarkingColor.Metal => new[] { new[] { "galvanized" }, new[] { "galvanizado" }, new[] { "steel" }, new[] { "aco" } },
        MarkingColor.Vidro => new[] { new[] { "glass", "clear" }, new[] { "glass" }, new[] { "vidro" } },
        MarkingColor.Madeira => new[] { new[] { "wood" }, new[] { "madeira" } },
        MarkingColor.Agua => new[] { new[] { "water" }, new[] { "agua" } },
        _ => Array.Empty<string[]>(),
    };

    /// <summary>Palavras que tiram o ativo (outro uso: telhado, bloco de alvenaria, piso cerâmico...).</summary>
    private static readonly string[] Excluded = { "shingle", "shingles", "roof", "roofing", "telha", "telhado", "block", "masonry", "bloco", "alvenaria", "tile", "tiles", "ceramica", "terracotta", "terrazzo" };

    /// <summary>O material tem textura real – recebe ativo da biblioteca quando houver um que sirva.</summary>
    public static bool Textured(MarkingColor c) => Keywords(c).Count > 0;

    /// <summary>Ativo escolhido para a cor (nulo = nenhum serve: o material fica na cor lisa, como antes).</summary>
    public static Candidate? Choose(MarkingColor c, IEnumerable<Candidate> assets)
    {
        var list = assets.Select(a => (A: a, Words: Words(a.Title.Length > 0 ? a.Title : a.Name))).Where(x => !x.Words.Overlaps(Excluded)).ToList();
        foreach (var group in Keywords(c))
        {
            var hits = list.Where(x => group.All(k => x.Words.Contains(k) || x.Words.Contains(k + "s") || x.Words.Contains(k + "es"))).ToList();
            if (hits.Count == 0) continue;
            // O mais genérico (título mais curto), depois a ordem alfabética – a mesma escolha em toda máquina com a mesma biblioteca.
            return hits.OrderBy(x => x.Words.Count).ThenBy(x => x.A.Title.Length).ThenBy(x => x.A.Title, StringComparer.Ordinal)
                .ThenBy(x => x.A.Name, StringComparer.Ordinal).First().A;
        }
        return null;
    }

    /// <summary>Palavras do texto, minúsculas e sem acento.</summary>
    public static HashSet<string> Words(string text)
    {
        var norm = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var ch in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }
}
