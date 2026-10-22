using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Quantities;

/// <summary>Uma linha do resumo por componente da seção: componente, material e hierarquia viária.</summary>
public sealed class ComponentRow
{
    public string Code { get; set; } = "";
    public string Name => SectionComponent.Label(Code);
    public MarkingColor Color { get; set; }
    public string ColorLabel => QuantityRow.ColorText(Color);
    public HierarquiaViaria? Hierarchy { get; set; }
    public string HierarchyName => Hierarquia.Label(Hierarchy);
    /// <summary>Área em planta (m²) das peças geradas – ou a medida no modelo, quando o piso foi editado.</summary>
    public double Area { get; set; }
    /// <summary>Extensão (m) pelo eixo das peças – só no meio-fio.</summary>
    public double Length { get; set; }
    /// <summary>Marcas que têm este componente.</summary>
    public int Elements { get; set; }
    /// <summary>Origem: código das marcas e quantas de cada ("SARJETA (4), INTERSECAO (1)").</summary>
    public string Sources { get; set; } = "";
}

/// <summary>
/// Quantitativo por componente da seção (Fase VI): pavimento, meio-fio, sarjeta, calçada, gramado e ilhas somados em todas as
/// marcas que os geram – vias e seus elementos de bordo, interseções, rotatórias, extensões de calçada, cul-de-sac e áreas de
/// calçada. No quadro por marca a interseção e a rotatória saem como uma unidade, com o concreto de meio-fio, sarjeta e
/// calçada junto numa cor só; aqui cada componente tem a sua linha. Medido nas peças geradas, depois dos recortes.
/// </summary>
public static class ComponentQuantities
{
    /// <summary>Marcas que fazem parte da seção da via (as demais – sinalização, mobiliário, obras – não entram).</summary>
    public static bool InScope(MarkingDefinition d) => d switch
    {
        RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CurbExtensionDefinition or CulDeSacDefinition => true,
        SidewalkAreaDefinition s => s.Type is TipoAreaCalcada.Calcada or TipoAreaCalcada.Canteiro or TipoAreaCalcada.FaixaServico or TipoAreaCalcada.Pavimento,
        LinearMarkingDefinition l => SectionComponent.FromCode(l.Code) != null,
        _ => false,
    };

    /// <summary>
    /// Componente de uma peça de uma marca da seção: a marcação do gerador, a camada, o código da marca linear, o pavimento pela
    /// cor; o resto do que é físico vai para "outros". Pintura não é componente (nulo).
    /// </summary>
    public static string? Of(MarkingDefinition def, MarkingPiece p)
    {
        if (MarkingColors.IsPaint(p.Color)) return null;
        if (p.Component is { Length: > 0 } c) return c;
        if (SectionComponent.FromCode(p.Layer) is { } byLayer) return byLayer;
        if (def is LinearMarkingDefinition l && SectionComponent.FromCode(l.Code) is { } byCode) return byCode;
        if (MarkingColors.IsPavement(p.Color)) return SectionComponent.Pavimento;
        return SectionComponent.Outros;
    }

    /// <summary>
    /// Extensão de uma faixa (meio-fio) pelo eixo: com perímetro P e área A, uma faixa aberta de largura constante tem
    /// P = 2L + 2w e A = w·L, logo L = (P + √(P² − 16A)) / 4 – exato no retângulo e no setor de coroa circular; um anel fechado
    /// (com furo) não tem pontas: L = P / 2.
    /// </summary>
    public static double StripLength(Polygon2 p)
    {
        var per = Perimeter(p.Outer) + p.Holes.Sum(Perimeter);
        if (p.Holes.Count > 0) return per / 2;
        var disc = per * per - 16 * p.Area;
        return (per + Math.Sqrt(Math.Max(0, disc))) / 4;
    }

    private static double Perimeter(IReadOnlyList<Vec2> ring)
    {
        var s = 0.0;
        for (int i = 0; i < ring.Count; i++) s += ring[i].DistanceTo(ring[(i + 1) % ring.Count]);
        return s;
    }

    /// <summary>
    /// Soma por componente, material e hierarquia. Pisos medidos no modelo (<see cref="MarkingGeometry.AreaOverrides"/>, por
    /// cor) repartem a área medida entre os componentes daquela cor na proporção das peças geradas; sem peças da cor (piso
    /// desenhado à mão com hierarquia), a área toda vai para o pavimento.
    /// </summary>
    public static List<ComponentRow> Compute(IEnumerable<(MarkingDefinition Def, MarkingGeometry Geo)> items)
    {
        var rows = new Dictionary<(string, MarkingColor, HierarquiaViaria?), (ComponentRow Row, Dictionary<string, int> Src)>();
        void Add(MarkingDefinition def, string comp, MarkingColor color, double area, double length, HashSet<(string, MarkingColor, HierarquiaViaria?)> seen)
        {
            var key = (comp, color, def.Hierarchy);
            if (!rows.TryGetValue(key, out var r))
                rows[key] = r = (new ComponentRow { Code = comp, Color = color, Hierarchy = def.Hierarchy }, new Dictionary<string, int>());
            r.Row.Area += area;
            r.Row.Length += length;
            if (seen.Add(key))
            {
                r.Row.Elements++;
                r.Src[def.DisplayCode] = r.Src.GetValueOrDefault(def.DisplayCode) + 1;
            }
        }
        foreach (var (def, geo) in items)
        {
            if (!InScope(def)) continue;
            var seen = new HashSet<(string, MarkingColor, HierarquiaViaria?)>();
            var byColor = geo.Pieces.GroupBy(p => p.Color).ToDictionary(g => g.Key, g => g.Sum(p => p.Shape.Area));
            foreach (var g in geo.Pieces.Select(p => (P: p, C: Of(def, p))).Where(x => x.C != null).GroupBy(x => (x.C!, x.P.Color)))
            {
                var (comp, color) = g.Key;
                var area = g.Sum(x => x.P.Shape.Area);
                var length = comp == SectionComponent.MeioFio ? g.Sum(x => StripLength(x.P.Shape)) : 0;
                // Piso editado no modelo: a área medida, repartida na proporção do que foi gerado.
                if (geo.AreaOverrides.TryGetValue(color, out var measured) && byColor.GetValueOrDefault(color) > 1e-9)
                {
                    var k = measured / byColor[color];
                    area *= k;
                    length *= k;
                }
                if (area > 1e-6) Add(def, comp, color, area, length, seen);
            }
            // Piso medido sem peça gerada da mesma cor (pavimento desenhado à mão com hierarquia).
            foreach (var (color, measured) in geo.AreaOverrides)
                if (byColor.GetValueOrDefault(color) <= 1e-9 && measured > 1e-6 && !MarkingColors.IsPaint(color))
                    Add(def, MarkingColors.IsPavement(color) ? SectionComponent.Pavimento : SectionComponent.Outros, color, measured, 0, seen);
        }
        return rows.Values
            .Select(r =>
            {
                r.Row.Sources = string.Join(", ", r.Src.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key} ({kv.Value})"));
                return r.Row;
            })
            .OrderBy(r => Array.IndexOf(SectionComponent.All, r.Code)).ThenByDescending(r => Hierarquia.Rank(r.Hierarchy)).ThenBy(r => r.Color)
            .ToList();
    }

    /// <summary>Bloco do CSV (";" e vírgula decimal).</summary>
    public static string Csv(IEnumerable<ComponentRow> rows)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("RESUMO POR COMPONENTE DA SEÇÃO (medido nas peças geradas, depois dos recortes)");
        sb.AppendLine("Componente;Material;Hierarquia viária;Área (m²);Extensão (m);Elementos;Origem");
        foreach (var r in rows)
            sb.AppendLine(string.Join(";", r.Name, r.ColorLabel, r.HierarchyName, r.Area.ToString("0.00", pt),
                r.Length > 1e-6 ? r.Length.ToString("0.00", pt) : "", r.Elements, r.Sources.Contains(';') ? "\"" + r.Sources + "\"" : r.Sources));
        return sb.ToString();
    }
}
