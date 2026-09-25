namespace SinalizacaoViaria.Core.Model;

/// <summary>Cores da sinalização horizontal (MBST Vol. IV, item "Cores").</summary>
public enum MarkingColor
{
    Branca,
    Amarela,
    Vermelha,
    Azul,
    Preta,
    /// <summary>Concreto (calçadas, meios-fios, barreiras) – elemento físico, não é tinta.</summary>
    Concreto,
    /// <summary>Grama/ajardinamento de canteiros – elemento físico.</summary>
    Grama,
    /// <summary>Metal (defensas, balizadores metálicos) – elemento físico.</summary>
    Metal,
}

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

public static class MarkingColors
{
    public static IReadOnlyList<MarkingColor> All { get; } = Enum.GetValues<MarkingColor>();

    /// <summary>Referência Munsell indicada no MBST para cada cor.</summary>
    public static string Munsell(MarkingColor c) => c switch
    {
        MarkingColor.Amarela => "10 YR 7,5/14",
        MarkingColor.Branca => "N 9,5",
        MarkingColor.Vermelha => "7,5 R 4/14",
        MarkingColor.Azul => "5 PB 2/8",
        MarkingColor.Preta => "N 0,5",
        _ => "",
    };

    /// <summary>Verdadeiro para cores de demarcação (tinta); falso para materiais físicos.</summary>
    public static bool IsPaint(MarkingColor c) => c <= MarkingColor.Preta;

    /// <summary>Cor RGB aproximada para exibição no Revit (materiais e regiões preenchidas).</summary>
    public static Rgb Display(MarkingColor c) => c switch
    {
        MarkingColor.Amarela => new Rgb(255, 184, 28),
        MarkingColor.Branca => new Rgb(245, 245, 245),
        MarkingColor.Vermelha => new Rgb(196, 30, 36),
        MarkingColor.Azul => new Rgb(0, 72, 150),
        MarkingColor.Preta => new Rgb(25, 25, 25),
        MarkingColor.Concreto => new Rgb(188, 186, 180),
        MarkingColor.Grama => new Rgb(98, 158, 74),
        MarkingColor.Metal => new Rgb(148, 156, 166),
        _ => new Rgb(128, 128, 128),
    };

    /// <summary>Uso típico da cor segundo o MBST (texto de ajuda da interface).</summary>
    public static string Usage(MarkingColor c) => c switch
    {
        MarkingColor.Amarela => "Separação de fluxos opostos, proibição de estacionamento/parada, obstáculos.",
        MarkingColor.Branca => "Fluxos de mesmo sentido, bordos, transversais, canalização, inscrições.",
        MarkingColor.Vermelha => "Ciclovias/ciclofaixas e símbolo de serviços de saúde.",
        MarkingColor.Azul => "Inscrições em áreas de estacionamento para pessoas com deficiência.",
        MarkingColor.Preta => "Contraste/realce entre a marca e o pavimento.",
        MarkingColor.Concreto => "Calçadas, meios-fios, barreiras e dispositivos de concreto.",
        MarkingColor.Grama => "Canteiros e faixas de serviço ajardinadas.",
        MarkingColor.Metal => "Defensas e dispositivos metálicos.",
        _ => "",
    };

    public static bool TryParse(string? s, out MarkingColor c)
    {
        c = MarkingColor.Branca;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var norm = s.Trim().ToLowerInvariant();
        foreach (var v in All)
        {
            var name = v.ToString().ToLowerInvariant();
            if (norm == name || norm.StartsWith(name[..Math.Min(4, name.Length)]))
            {
                c = v;
                return true;
            }
        }
        return false;
    }
}
