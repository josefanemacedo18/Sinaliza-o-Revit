namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Resumo dos avisos do Revit no relatório do Autoteste. Cada aviso chega como
/// "Aviso do Revit: &lt;texto&gt; – PAV-ASF (Pisos) #123 [marca 74c332], MER-0 (Pisos) #456 [marca a0c16a]" e o relatório só
/// detalha os 25 primeiros de cada etapa; o resumo conta todos: quantos de cada texto, quantos entre elementos da mesma
/// marca e em que elementos (código da marca, ou a categoria quando o elemento não é do plugin).
/// </summary>
public static class WarningSummary
{
    public const string Prefix = "Aviso do Revit: ";

    /// <summary>Uma linha por texto de aviso, do mais frequente ao menos: "Σ 62 × "texto" – em QUADRA-B 40, LEG 22".</summary>
    public static IEnumerable<string> Lines(IEnumerable<string> warnings) =>
        warnings.Where(w => w.StartsWith(Prefix, StringComparison.Ordinal))
            .GroupBy(Text)
            .OrderByDescending(g => g.Count())
            .Select(g =>
            {
                var same = g.Count(w => w.EndsWith("[mesma marca]", StringComparison.Ordinal));
                var codes = g.SelectMany(w => Codes(w).Distinct()).GroupBy(c => c)
                    .OrderByDescending(c => c.Count()).ThenBy(c => c.Key, StringComparer.Ordinal).Take(8).ToList();
                return $"Σ {g.Count()} × \"{g.Key}\""
                       + (same > 0 ? $" ({same} entre elementos da mesma marca)" : "")
                       + (codes.Count > 0 ? " – em " + string.Join(", ", codes.Select(c => $"{c.Key} {c.Count()}")) : "");
            });

    /// <summary>Texto do aviso, sem o prefixo e sem a lista de elementos.</summary>
    public static string Text(string warning)
    {
        var t = warning.StartsWith(Prefix, StringComparison.Ordinal) ? warning[Prefix.Length..] : warning;
        var k = t.IndexOf(" – ", StringComparison.Ordinal);
        return k < 0 ? t : t[..k];
    }

    /// <summary>Códigos dos elementos citados no aviso ("PAV-ASF", "QUADRA-B"; "Pisos" quando não é marca do plugin; croquis saem).</summary>
    public static IEnumerable<string> Codes(string warning)
    {
        var k = warning.IndexOf(" – ", StringComparison.Ordinal);
        if (k < 0) yield break;
        foreach (var part in warning[(k + 3)..].Split(", "))
        {
            var h = part.IndexOf(" #", StringComparison.Ordinal);
            if (h <= 0) continue;
            var code = part[..h];
            var p = code.IndexOf(" (", StringComparison.Ordinal);
            code = (p > 0 ? code[..p] : code).Trim();
            if (code.Length > 0 && !code.StartsWith('<')) yield return code;
        }
    }
}
