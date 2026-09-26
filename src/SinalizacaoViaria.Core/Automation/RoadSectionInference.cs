using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Reconstrói a seção transversal de uma via a partir das marcas do grupo gerado pelo "Sinalizar via"
/// (meios-fios, calçadas, gramados, sarjetas e linhas). Usado para vias criadas antes do registro da seção
/// no pavimento, para que interseções e rotatórias funcionem com qualquer via existente.
/// </summary>
public static class RoadSectionInference
{
    private static readonly string[] Physical = { "CALCADA", "GRAMADO", "SARJETA" };
    private static readonly string[] Longitudinal = { "LBO", "CALCADA", "GRAMADO" };

    public static bool IsPhysical(string code) => Physical.Contains(code) || code.StartsWith("MEIO-FIO");

    /// <summary>Assinatura do caminho (marcas do mesmo eixo têm a mesma assinatura).</summary>
    public static string PathKey(PathReference? p) =>
        p == null ? "" : p.ElementIds.Count > 0 ? string.Join("|", p.ElementIds) : string.Join("|", p.Points.Select(v => $"{v.X:0.###},{v.Y:0.###}"));

    /// <summary>Verdadeiro quando o grupo parece uma via (várias marcas longitudinais no mesmo eixo).</summary>
    public static bool LooksLikeRoad(IReadOnlyCollection<MarkingDefinition> group)
    {
        var lin = group.OfType<LinearMarkingDefinition>().ToList();
        if (lin.Count < 2) return false;
        if (group.Any(d => d is RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or RampDefinition)) return false;
        if (lin.Any(l => l.Code.StartsWith("FTP") || l.Code is "LRE" or "LDP")) return false;
        if (!lin.Any(l => Longitudinal.Contains(l.Code) || l.Code.StartsWith("LFO") || l.Code.StartsWith("LMS") || l.Code.StartsWith("MEIO-FIO"))) return false;
        return lin.Select(l => PathKey(l.Path)).Distinct().Count() == 1;
    }

    public static double Width(LinearMarkingDefinition l, Catalogo cat)
    {
        if (l.WidthOverride is > 0) return l.WidthOverride.Value;
        var t = cat.Linear(l.Code);
        var v = t == null ? null : MarkingBuilder.ResolveVariant(t, l.Variant, l.Speed);
        if (v == null || v.Faixas.Count == 0) return 0.10;
        var lo = v.Faixas.Min(f => f.Deslocamento - f.Largura / 2);
        var hi = v.Faixas.Max(f => f.Deslocamento + f.Largura / 2);
        return hi - lo;
    }

    /// <summary>Seção inferida (pavimento em asfalto) ou nulo quando o grupo não é uma via.</summary>
    public static RoadPavementDefinition? Infer(IReadOnlyCollection<MarkingDefinition> group, Catalogo cat)
    {
        if (!LooksLikeRoad(group)) return null;
        var lin = group.OfType<LinearMarkingDefinition>().ToList();
        var items = lin.Select(l => (Lo: l.Offset - Width(l, cat) / 2, Hi: l.Offset + Width(l, cat) / 2, Phys: IsPhysical(l.Code), l.Code)).ToList();

        // Blocos físicos contíguos (meio-fio + gramado + calçada...).
        var blocks = new List<(double Lo, double Hi, bool Walk)>();
        // A sarjeta faz parte da pista (junto ao meio-fio): não entra nos blocos de calçada/canteiro.
        foreach (var it in items.Where(i => i.Phys && i.Code != "SARJETA").OrderBy(i => i.Lo))
        {
            if (blocks.Count > 0 && it.Lo <= blocks[^1].Hi + 0.05)
                blocks[^1] = (blocks[^1].Lo, Math.Max(blocks[^1].Hi, it.Hi), blocks[^1].Walk || it.Code == "CALCADA");
            else blocks.Add((it.Lo, it.Hi, it.Code == "CALCADA"));
        }
        var paint = items.Where(i => !i.Phys).ToList();
        var paintLo = paint.Count > 0 ? paint.Min(p => p.Lo) : 0;
        var paintHi = paint.Count > 0 ? paint.Max(p => p.Hi) : 0;

        var d = new RoadPavementDefinition { Material = TipoPavimento.Asfalto };
        // Lado esquerdo (+): bloco mais externo além da pintura = calçada.
        var left = blocks.Where(b => b.Lo > 0 && b.Lo >= paintHi - 0.05).OrderByDescending(b => b.Hi).FirstOrDefault();
        var right = blocks.Where(b => b.Hi < 0 && b.Hi <= paintLo + 0.05).OrderBy(b => b.Lo).FirstOrDefault();
        var hasLeft = left.Hi > left.Lo;
        var hasRight = right.Hi > right.Lo;
        d.LeftWidth = hasLeft ? left.Lo : Math.Max(1.0, paintHi + 0.10);
        d.LeftSidewalk = hasLeft ? left.Hi - left.Lo : 0;
        d.RightWidth = hasRight ? -right.Hi : Math.Max(1.0, -paintLo + 0.10);
        d.RightSidewalk = hasRight ? right.Hi - right.Lo : 0;

        // Sarjetas junto à calçada ficam na pista (sem pavimento).
        foreach (var s in items.Where(i => i.Code == "SARJETA"))
        {
            var c = (s.Lo + s.Hi) / 2;
            if (c > 0 && hasLeft && s.Hi >= left.Lo - 0.05) d.LeftWidth = Math.Max(d.LeftWidth, s.Hi);
            if (c < 0 && hasRight && s.Lo <= right.Hi + 0.05) d.RightWidth = Math.Max(d.RightWidth, -s.Lo);
            d.Gaps.Add(new PavementGap(c, s.Hi - s.Lo, Median: false));
        }
        // Demais blocos físicos dentro da pista = canteiros.
        foreach (var b in blocks)
        {
            if (hasLeft && Math.Abs(b.Lo - left.Lo) < 1e-6 && Math.Abs(b.Hi - left.Hi) < 1e-6) continue;
            if (hasRight && Math.Abs(b.Lo - right.Lo) < 1e-6 && Math.Abs(b.Hi - right.Hi) < 1e-6) continue;
            if (b.Lo < -d.RightWidth - 0.01 || b.Hi > d.LeftWidth + 0.01) continue;
            d.Gaps.Add(new PavementGap((b.Lo + b.Hi) / 2, b.Hi - b.Lo));
        }
        d.TwoWay = lin.Any(l => l.Code.StartsWith("LFO")) || d.Gaps.Any(g => g.Median && g.Offset - g.Width / 2 < 0 && g.Offset + g.Width / 2 > 0)
                   || group.OfType<DeviceMarkingDefinition>().Any(dv => Math.Abs(dv.Offset) < 0.01);
        var first = lin[0];
        d.GroupId = first.GroupId;
        d.Hierarchy = group.Select(m => m.Hierarchy).FirstOrDefault(h => h != null);
        d.Output = first.Output.Clone();
        var path = first.PathRef.Clone();
        path.Closed = false;
        d.SetPath(path);
        return d;
    }
}
