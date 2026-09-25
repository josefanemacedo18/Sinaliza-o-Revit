using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Quantities;

/// <summary>Uma linha do quadro de quantidades.</summary>
public sealed class QuantityRow
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public GrupoMarca Group { get; set; }
    public MarkingColor Color { get; set; }
    public string Material { get; set; } = "";
    public double Area { get; set; }
    public double PaintedLength { get; set; }
    public int Units { get; set; }
    public int Elements { get; set; }
    public string Unit { get; set; } = "";
    public double MaterialConsumption { get; set; }
    public string ConsumptionUnit { get; set; } = "";
    public double GlassBeadsKg { get; set; }
    public string Reference { get; set; } = "";

    public string GroupName => GroupLabel(Group);

    public static string GroupLabel(GrupoMarca g) => g switch
    {
        GrupoMarca.Longitudinal => "Marcas longitudinais",
        GrupoMarca.Transversal => "Marcas transversais",
        GrupoMarca.Canalizacao => "Marcas de canalização",
        GrupoMarca.Estacionamento => "Estacionamento e parada",
        GrupoMarca.Inscricao => "Inscrições no pavimento",
        GrupoMarca.Dispositivo => "Dispositivos auxiliares e físicos",
        GrupoMarca.Acessibilidade => "Acessibilidade",
        GrupoMarca.Ciclovia => "Ciclovias e ciclofaixas",
        GrupoMarca.Urbanizacao => "Calçadas, meios-fios e canteiros",
        _ => g.ToString(),
    };
}

/// <summary>Consolida áreas pintadas, extensões e unidades por código, cor e material.</summary>
public static class QuantityCalculator
{
    public static List<QuantityRow> Compute(IEnumerable<(MarkingDefinition Def, MarkingGeometry Geo)> items, Catalogo catalog, string? defaultMaterial = null)
    {
        var rows = new Dictionary<(string, MarkingColor, string), QuantityRow>();
        foreach (var (def, geo) in items)
        {
            var info = MarkingBuilder.Describe(def, catalog);
            var matName = def.Output.Material ?? defaultMaterial ?? catalog.Materiais.FirstOrDefault()?.Nome ?? "";
            var mat = catalog.Material(matName);
            var byColor = geo.AreaByColor;
            var first = true;
            foreach (var (color, area) in byColor.OrderByDescending(kv => kv.Value))
            {
                var key = (info.Code, color, matName);
                if (!rows.TryGetValue(key, out var row))
                {
                    row = new QuantityRow
                    {
                        Code = info.Code, Name = info.Name, Group = info.Group, Color = color, Material = matName,
                        Unit = info.Unit, Reference = info.Reference, ConsumptionUnit = mat?.UnidadeConsumo ?? "",
                    };
                    rows[key] = row;
                }
                row.Area += area;
                // Consumo de tinta apenas para demarcação (não para calçadas, canteiros e dispositivos físicos).
                if (mat != null && MarkingColors.IsPaint(color) && def is not DeviceMarkingDefinition)
                {
                    row.MaterialConsumption += area * mat.Consumo;
                    row.GlassBeadsKg += area * mat.MicroesferasKgM2;
                }
                if (first)
                {
                    // Extensão e unidades contabilizadas uma única vez (na cor predominante).
                    row.PaintedLength += geo.PaintedLength;
                    row.Units += geo.UnitCount;
                    row.Elements += 1;
                    first = false;
                }
            }
        }
        return rows.Values
            .OrderBy(r => r.Group).ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Color)
            .ToList();
    }

    /// <summary>Totais por cor e material (resumo de compra).</summary>
    public static List<QuantityRow> Summary(IEnumerable<QuantityRow> rows) =>
        rows.GroupBy(r => (r.Color, r.Material))
            .Select(g => new QuantityRow
            {
                Code = "TOTAL",
                Name = $"Total {g.Key.Color.ToString().ToLowerInvariant()} – {g.Key.Material}",
                Color = g.Key.Color,
                Material = g.Key.Material,
                Area = g.Sum(r => r.Area),
                PaintedLength = g.Sum(r => r.PaintedLength),
                Units = g.Sum(r => r.Units),
                Elements = g.Sum(r => r.Elements),
                MaterialConsumption = g.Sum(r => r.MaterialConsumption),
                ConsumptionUnit = g.First().ConsumptionUnit,
                GlassBeadsKg = g.Sum(r => r.GlassBeadsKg),
                Unit = "m²",
            })
            .OrderBy(r => r.Color).ToList();

    /// <summary>CSV no padrão brasileiro (separador ";" e vírgula decimal), pronto para o Excel.</summary>
    public static string ToCsv(IEnumerable<QuantityRow> rows, IEnumerable<QuantityRow>? summary = null)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var sb = new StringBuilder();
        sb.AppendLine("Grupo;Código;Descrição;Cor;Material;Área pintada (m²);Extensão (m);Unidades;Elementos;Consumo estimado;Unidade consumo;Microesferas (kg);Referência");
        void Row(QuantityRow r) => sb.AppendLine(string.Join(";",
            Esc(r.GroupName), Esc(r.Code), Esc(r.Name), r.Color, Esc(r.Material),
            r.Area.ToString("0.00", pt), r.PaintedLength.ToString("0.00", pt), r.Units, r.Elements,
            r.MaterialConsumption.ToString("0.00", pt), Esc(r.ConsumptionUnit), r.GlassBeadsKg.ToString("0.00", pt), Esc(r.Reference)));
        foreach (var r in rows) Row(r);
        if (summary != null)
        {
            sb.AppendLine();
            sb.AppendLine("RESUMO POR COR E MATERIAL");
            foreach (var r in summary) Row(r);
        }
        return sb.ToString();
    }

    private static string Esc(string s) =>
        s.Contains(';') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"").Replace("\n", " ") + "\"" : s;
}
