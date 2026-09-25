using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Contornos de um caractere em unidades normalizadas (altura de maiúscula = 1, linha de base em y = 0).</summary>
public sealed record GlyphOutline(List<IReadOnlyList<Vec2>> Contours, double Advance);

/// <summary>Fornece contornos de caracteres. No Revit é implementado com as fontes TrueType do Windows (WPF).</summary>
public interface IGlyphOutlineProvider
{
    GlyphOutline GetGlyph(char c, string fontFamily, bool bold);
}

/// <summary>Parâmetros de uma legenda no pavimento.</summary>
public sealed class TextOptions
{
    /// <summary>Altura das letras maiúsculas (m).</summary>
    public double Height { get; set; } = 1.60;

    /// <summary>Fator de largura: largura das letras = altura × fator × proporção natural da fonte.</summary>
    public double WidthFactor { get; set; } = 0.40;

    /// <summary>Espaço adicional entre letras (m).</summary>
    public double LetterSpacing { get; set; } = 0.08;

    /// <summary>Espaço livre entre linhas (m).</summary>
    public double LineSpacing { get; set; } = 1.00;

    public string FontFamily { get; set; } = "Arial";
    public bool Bold { get; set; } = true;

    /// <summary>
    /// Ordem de leitura de baixo para cima: a primeira linha fica mais próxima do condutor
    /// (prática do MBST para legendas em mais de uma linha).
    /// </summary>
    public bool BottomToTop { get; set; } = true;
}

/// <summary>Gera legendas (inscrições) alongadas no pavimento a partir de qualquer fonte.</summary>
public static class TextGenerator
{
    public static MarkingGeometry Generate(string text, TextOptions opt, LocalFrame frame, MarkingColor color, IGlyphOutlineProvider provider)
    {
        var geo = new MarkingGeometry();
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return geo;

        var ordered = opt.BottomToTop ? lines : Enumerable.Reverse(lines).ToList();
        var sx = opt.Height * opt.WidthFactor;
        var sy = opt.Height;
        double y = 0;
        double maxWidth = 0;
        int letters = 0;

        foreach (var line in ordered)
        {
            var contours = new List<IReadOnlyList<Vec2>>();
            double x = 0;
            foreach (var ch in line)
            {
                var g = provider.GetGlyph(ch, opt.FontFamily, opt.Bold);
                var ox = x;
                foreach (var c in g.Contours)
                    contours.Add(c.Select(p => new Vec2(ox + p.X * sx, y + p.Y * sy)).ToList());
                x += g.Advance * sx + opt.LetterSpacing;
                if (!char.IsWhiteSpace(ch)) letters++;
            }
            // Centraliza pela tinta (contornos reais), não pelo avanço tipográfico.
            double inkMin = 0, inkMax = 0;
            if (contours.Count > 0)
            {
                inkMin = contours.Min(c => c.Min(p => p.X));
                inkMax = contours.Max(c => c.Max(p => p.X));
            }
            var width = inkMax - inkMin;
            maxWidth = Math.Max(maxWidth, width);
            var shift = -(inkMin + inkMax) / 2;
            var centered = contours.Select(c => (IReadOnlyList<Vec2>)c.Select(p => new Vec2(p.X + shift, p.Y)).ToList());
            var polys = PolygonOps.FromContours(centered);
            geo.AddRange(polys.Select(frame.ToWorld), color);
            y += opt.Height + opt.LineSpacing;
        }

        geo.PathLength = y - opt.LineSpacing;
        geo.UnitCount = letters;
        if (maxWidth > 3.5)
            geo.Warnings.Add($"Legenda com {maxWidth:0.00} m de largura – verifique se cabe na faixa de rolamento.");
        return geo;
    }
}

/// <summary>
/// Fonte de blocos embutida (5×7), usada como alternativa quando nenhuma fonte do sistema está
/// disponível e nos testes automatizados. Suporta A–Z, 0–9, acentos do português e pontuação básica.
/// </summary>
public sealed class BlockFontProvider : IGlyphOutlineProvider
{
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
        ['B'] = new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " },
        ['C'] = new[] { " ####", "#    ", "#    ", "#    ", "#    ", "#    ", " ####" },
        ['D'] = new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " },
        ['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
        ['F'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    " },
        ['G'] = new[] { " ####", "#    ", "#    ", "#  ##", "#   #", "#   #", " ### " },
        ['H'] = new[] { "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
        ['I'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "#####" },
        ['J'] = new[] { "  ###", "    #", "    #", "    #", "    #", "#   #", " ### " },
        ['K'] = new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" },
        ['L'] = new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" },
        ['M'] = new[] { "#   #", "## ##", "# # #", "#   #", "#   #", "#   #", "#   #" },
        ['N'] = new[] { "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #" },
        ['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['P'] = new[] { "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    " },
        ['Q'] = new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" },
        ['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
        ['S'] = new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " },
        ['T'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['U'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['V'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  " },
        ['W'] = new[] { "#   #", "#   #", "#   #", "#   #", "# # #", "## ##", "#   #" },
        ['X'] = new[] { "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #" },
        ['Y'] = new[] { "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['Z'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####" },
        ['0'] = new[] { " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### " },
        ['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
        ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
        ['3'] = new[] { "#### ", "    #", "    #", " ### ", "    #", "    #", "#### " },
        ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
        ['5'] = new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },
        ['6'] = new[] { " ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### " },
        ['7'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },
        ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
        ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "    #", " ### " },
        ['-'] = new[] { "     ", "     ", "     ", "#####", "     ", "     ", "     " },
        ['.'] = new[] { "     ", "     ", "     ", "     ", "     ", "     ", "  #  " },
        ['/'] = new[] { "    #", "    #", "   # ", "  #  ", " #   ", "#    ", "#    " },
    };

    private static readonly Dictionary<char, (char Base, string Accent)> Accents = new()
    {
        ['Á'] = ('A', "acute"), ['À'] = ('A', "grave"), ['Â'] = ('A', "circ"), ['Ã'] = ('A', "tilde"),
        ['É'] = ('E', "acute"), ['Ê'] = ('E', "circ"),
        ['Í'] = ('I', "acute"),
        ['Ó'] = ('O', "acute"), ['Ô'] = ('O', "circ"), ['Õ'] = ('O', "tilde"),
        ['Ú'] = ('U', "acute"), ['Ü'] = ('U', "uml"),
    };

    private const double Cell = 1.0 / 7.0;

    public GlyphOutline GetGlyph(char c, string fontFamily, bool bold)
    {
        c = char.ToUpperInvariant(c);
        if (c == ' ') return new GlyphOutline(new(), 3 * Cell);
        var contours = new List<IReadOnlyList<Vec2>>();

        if (c == 'Ç')
        {
            contours.AddRange(Cells(Glyphs['C']));
            contours.Add(Square(2, -1));
            contours.Add(Square(2, -2));
            contours.Add(Square(1, -2));
            return new GlyphOutline(contours, 6 * Cell);
        }
        if (Accents.TryGetValue(c, out var acc))
        {
            contours.AddRange(Cells(Glyphs[acc.Base]));
            switch (acc.Accent)
            {
                case "acute": contours.Add(Square(3, 8)); contours.Add(Square(2, 7.5)); break;
                case "grave": contours.Add(Square(1, 8)); contours.Add(Square(2, 7.5)); break;
                case "circ": contours.Add(Square(1, 7.5)); contours.Add(Square(2, 8.2)); contours.Add(Square(3, 7.5)); break;
                case "tilde": contours.Add(Square(0.5, 7.6)); contours.Add(Square(1.5, 8.2)); contours.Add(Square(2.5, 7.6)); contours.Add(Square(3.5, 8.2)); break;
                case "uml": contours.Add(Square(1, 8)); contours.Add(Square(3, 8)); break;
            }
            return new GlyphOutline(contours, 6 * Cell);
        }
        if (!Glyphs.TryGetValue(c, out var rows)) rows = Glyphs['-'];
        contours.AddRange(Cells(rows));
        return new GlyphOutline(contours, 6 * Cell);
    }

    private static IEnumerable<IReadOnlyList<Vec2>> Cells(string[] rows)
    {
        for (int r = 0; r < rows.Length; r++)
            for (int col = 0; col < rows[r].Length; col++)
                if (rows[r][col] == '#') yield return Square(col, 6 - r);
    }

    private static IReadOnlyList<Vec2> Square(double col, double row)
    {
        // Pequena sobreposição (1%) garante a união das células vizinhas.
        const double e = 0.01;
        var x0 = (col - e) * Cell; var x1 = (col + 1 + e) * Cell;
        var y0 = (row - e) * Cell; var y1 = (row + 1 + e) * Cell;
        return new[] { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) };
    }
}
