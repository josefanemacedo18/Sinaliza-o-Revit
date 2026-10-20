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

    /// <summary>
    /// Resumo das retas dos avisos "ligeiramente fora da linha central" lidas no Revit (código da marca, desalinhamento em
    /// graus e comprimento em metros): quantas, em que marcas, faixa do desalinhamento e quanto a ponta teria de andar para
    /// a reta ficar exata (comprimento × seno do desalinhamento) – o dado para calibrar o <see cref="Geometry.AxisSnap"/>.
    /// </summary>
    public static string? OffAxisNote(IReadOnlyCollection<(string Code, double OffDeg, double Length)> lines)
    {
        if (lines.Count == 0) return null;
        var mm = lines.Select(l => l.Length * Math.Sin(l.OffDeg * Math.PI / 180) * 1000).OrderBy(x => x).ToList();
        var deg = lines.Select(l => l.OffDeg).OrderBy(x => x).ToList();
        var len = lines.Select(l => l.Length).OrderBy(x => x).ToList();
        static double Med(List<double> v) => v[v.Count / 2];
        var codes = lines.GroupBy(l => l.Code).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(8)
            .Select(g => $"{g.Key} {g.Count()}");
        var ci = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        return string.Format(ci, "Retas fora do eixo lidas no Revit: {0} – em {1}; desalinhamento {2:0.####}° a {3:0.####}° (mediana {4:0.####}°); "
            + "comprimento mediano {5:0.00} m; a ponta andaria {6:0.0} mm na mediana e {7:0.0} mm no máximo; {8} até 0,5 mm e {9} até 5 mm.",
            lines.Count, string.Join(", ", codes), deg[0], deg[^1], Med(deg), Med(len), Med(mm), mm[^1], mm.Count(x => x <= 0.5), mm.Count(x => x <= 5));
    }

    /// <summary>
    /// Pares de pisos que continuam sobrepostos no fim de uma etapa, agrupados por par de códigos (com "[mesma marca]"):
    /// quantos e o volume em comum – todos os pares, não só os exemplos.
    /// </summary>
    public static string? PersistentPairsNote(IReadOnlyCollection<(string A, string B, bool Same, double VolumeM3)> pairs)
    {
        if (pairs.Count == 0) return null;
        var ci = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var groups = pairs.GroupBy(p => string.Join(" × ", new[] { p.A, p.B }.OrderBy(x => x, StringComparer.Ordinal)) + (p.Same ? " [mesma marca]" : ""))
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Sum(p => p.VolumeM3))
            .Select(g => string.Format(ci, "{0} {1} ({2:0.####} m³)", g.Key, g.Count(), g.Sum(p => p.VolumeM3)));
        return "Pares que continuam sobrepostos, por tipo: " + string.Join("; ", groups) + ".";
    }
}
