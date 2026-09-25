using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Parâmetros de instância de uma marca linear (o que o usuário ajusta em cada elemento).</summary>
public sealed class LinearOptions
{
    /// <summary>Deslocamento lateral adicional do eixo da marca (m, positivo = esquerda do caminho).</summary>
    public double Offset { get; set; }

    /// <summary>Alinhamento do padrão; nulo = o do catálogo.</summary>
    public AlinhamentoPadrao? Alignment { get; set; }

    /// <summary>Deslocamento do padrão ao longo do caminho (m).</summary>
    public double Phase { get; set; }

    /// <summary>Recuo no início / fim do caminho (m) – ex.: afastamento de interseções.</summary>
    public double StartSetback { get; set; }
    public double EndSetback { get; set; }

    /// <summary>Inverte o sentido do caminho.</summary>
    public bool Reverse { get; set; }

    /// <summary>Espelha as faixas lateralmente (ex.: LFO-4 com a contínua do outro lado).</summary>
    public bool InvertSides { get; set; }

    /// <summary>Largura substituta das faixas (m).</summary>
    public double? WidthOverride { get; set; }

    /// <summary>Padrão substituto [traço, espaço] (m).</summary>
    public double[]? PatternOverride { get; set; }

    public MarkingColor? ColorOverride { get; set; }

    /// <summary>Comprimento máximo de cada peça (m). 0 = sem divisão. Usado para acompanhar superfícies.</summary>
    public double MaxPieceLength { get; set; }
}

/// <summary>
/// Gerador universal de marcas lineares: toda marca formada por faixas paralelas a um caminho,
/// contínuas ou tracejadas. Cobre LFO, LMS, LBO, LCO, LRE, LDP, FTP-1 (barras = traços de uma faixa
/// larga), FTP-2, MCC, LRV, tachas e piso tátil.
/// </summary>
public static class LinearPatternGenerator
{
    /// <summary>Peças menores que esta fração do traço (quando cortadas nas extremidades) são descartadas.</summary>
    public const double MinPartialFraction = 0.2;

    public static MarkingGeometry Generate(Polyline2 path, TipoLinearDef type, VarianteDef variant, LinearOptions? opt = null)
    {
        opt ??= new LinearOptions();
        var geo = new MarkingGeometry();
        if (path.Points.Count < 2 || path.Length < 1e-6)
        {
            geo.Warnings.Add("Caminho vazio ou muito curto.");
            return geo;
        }

        var p = opt.Reverse ? path.Reversed() : path;
        geo.PathLength = p.Length;

        var a = Math.Max(0, opt.StartSetback);
        var b = p.Length - Math.Max(0, opt.EndSetback);
        if (b - a <= 1e-6)
        {
            geo.Warnings.Add("Os recuos são maiores que o comprimento do caminho.");
            return geo;
        }

        var alignment = opt.Alignment ?? type.Alinhamento;

        foreach (var stripeDef in variant.Faixas)
        {
            var stripe = ApplyOverrides(stripeDef, opt);
            var color = opt.ColorOverride ?? stripe.Cor ?? type.Cor;
            var lateral = opt.Offset + (opt.InvertSides ? -stripe.Deslocamento : stripe.Deslocamento);
            var offPath = p.Offset(lateral);

            var intervals = stripe.Continua
                ? new List<(double, double)> { (a, b) }
                : Intervals(a, b, stripe.Padrao, alignment, opt.Phase, stripe.Repetir, type.DescartarParciais);

            foreach (var (s0, s1) in intervals)
            {
                geo.PaintedLength += s1 - s0;
                foreach (var (c0, c1) in Chunk(s0, s1, type.Unidades ? 0 : opt.MaxPieceLength))
                {
                    var pts = offPath.SubPoints(p.ParamAt(c0), p.ParamAt(c1));
                    if (pts.Count < 2) continue;
                    var polys = PolygonOps.Strip(pts, stripe.Largura);
                    geo.AddRange(polys, color, type.Espessura, type.Unidades);
                }
            }

            if (type.LarguraMax > 0 && (stripe.Largura < type.LarguraMin - 1e-6 || stripe.Largura > type.LarguraMax + 1e-6))
                geo.Warnings.Add($"{type.Codigo}: largura {stripe.Largura:0.00} m fora do intervalo de referência ({type.LarguraMin:0.00}–{type.LarguraMax:0.00} m).");
        }

        geo.UnitCount = type.Unidades ? geo.Pieces.Count : 0;
        return geo;
    }

    private static FaixaDef ApplyOverrides(FaixaDef src, LinearOptions opt)
    {
        var f = src.Clone();
        if (opt.WidthOverride is > 0 and var w)
        {
            if (Math.Abs(f.Deslocamento) > 1e-9)
            {
                // Mantém a borda interna fixa (preserva o vão entre linhas duplas).
                var sign = Math.Sign(f.Deslocamento);
                var inner = Math.Abs(f.Deslocamento) - f.Largura / 2;
                f.Deslocamento = sign * (inner + w / 2);
            }
            f.Largura = w;
        }
        if (opt.PatternOverride is { Length: >= 2 } po && !f.Continua && f.Repetir)
            f.Padrao = (double[])po.Clone();
        return f;
    }

    /// <summary>
    /// Calcula os intervalos pintados [s0, s1] dentro de [a, b] para um padrão traço/espaço.
    /// </summary>
    public static List<(double S0, double S1)> Intervals(double a, double b, double[] pattern, AlinhamentoPadrao alignment,
        double phase = 0, bool repeat = true, bool dropPartials = false)
    {
        var res = new List<(double, double)>();
        var len = b - a;
        if (len <= 0 || pattern.Length == 0) return res;
        var pat = pattern.Length % 2 == 1 ? pattern.Append(0.0).ToArray() : pattern;
        var period = pat.Sum();
        if (period <= 1e-9) { res.Add((a, b)); return res; }

        if (alignment == AlinhamentoPadrao.Ajustar && repeat && pat.Length == 2)
            return FitIntervals(a, b, pat[0], pat[1]);

        if (alignment == AlinhamentoPadrao.Fim)
        {
            // Espelha: gera com alinhamento no início sobre o caminho invertido.
            var mirrored = Intervals(0, len, pat, AlinhamentoPadrao.Inicio, phase, repeat, dropPartials);
            return mirrored.Select(iv => (b - iv.S1, b - iv.S0)).OrderBy(iv => iv.Item1).ToList();
        }

        double start;
        if (!repeat)
        {
            start = alignment == AlinhamentoPadrao.Centro ? a + (len - period) / 2 : a;
            start += phase;
            double t = start;
            for (int i = 0; i < pat.Length; i++)
            {
                if (i % 2 == 0) AddClipped(res, t, t + pat[i], a, b, dropPartials);
                t += pat[i];
            }
            return res;
        }

        if (alignment == AlinhamentoPadrao.Centro)
        {
            var mid = (a + b) / 2;
            start = mid - pat[0] / 2;
        }
        else start = a;
        start += phase;
        // Recua para antes de 'a' mantendo a fase do padrão.
        if (start > a) start -= Math.Ceiling((start - a) / period) * period;

        var tt = start;
        int guard = 0;
        while (tt < b && guard++ < 200000)
        {
            for (int i = 0; i < pat.Length && tt < b; i++)
            {
                if (i % 2 == 0) AddClipped(res, tt, tt + pat[i], a, b, dropPartials);
                tt += pat[i];
            }
        }
        return res;
    }

    private static void AddClipped(List<(double, double)> res, double s0, double s1, double a, double b, bool dropPartials)
    {
        var full = s1 - s0;
        if (full <= 1e-9) return;
        var c0 = Math.Max(s0, a);
        var c1 = Math.Min(s1, b);
        var len = c1 - c0;
        if (len <= 1e-6) return;
        var partial = len < full - 1e-6;
        if (partial && (dropPartials || len < full * MinPartialFraction)) return;
        res.Add((c0, c1));
    }

    /// <summary>Número inteiro de traços com traço no início e no fim; espaços ajustados.</summary>
    private static List<(double, double)> FitIntervals(double a, double b, double dash, double gap)
    {
        var res = new List<(double, double)>();
        var len = b - a;
        if (dash >= len) { res.Add((a, b)); return res; }
        var n = Math.Max(2, (int)Math.Round((len + gap) / (dash + gap)));
        var g = (len - n * dash) / (n - 1);
        while (g < 0.5 * gap && n > 2) { n--; g = (len - n * dash) / (n - 1); }
        for (int i = 0; i < n; i++)
        {
            var s0 = a + i * (dash + g);
            res.Add((s0, Math.Min(b, s0 + dash)));
        }
        return res;
    }

    /// <summary>Largura total ocupada transversalmente pela marca (m) – ex.: largura da faixa de pedestres.</summary>
    public static double Footprint(VarianteDef variant, double? widthOverride = null)
    {
        double max = 0;
        foreach (var f in variant.Faixas)
        {
            var w = widthOverride is > 0 ? widthOverride.Value : f.Largura;
            var off = Math.Abs(f.Deslocamento);
            if (widthOverride is > 0 && off > 1e-9) off = off - f.Largura / 2 + w / 2;
            max = Math.Max(max, off + w / 2);
        }
        return 2 * max;
    }

    public static IEnumerable<(double, double)> Chunk(double s0, double s1, double maxLen)
    {
        if (maxLen <= 0 || s1 - s0 <= maxLen) { yield return (s0, s1); yield break; }
        var n = (int)Math.Ceiling((s1 - s0) / maxLen);
        var step = (s1 - s0) / n;
        for (int i = 0; i < n; i++) yield return (s0 + i * step, i == n - 1 ? s1 : s0 + (i + 1) * step);
    }
}
