using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Quantities;

/// <summary>Categorias do quantitativo (separação para orçamento e pesquisa).</summary>
public enum CategoriaQuantitativo
{
    SinalizacaoHorizontal,
    SinalizacaoVertical,
    DispositivosSegregacao,
    Acessibilidade,
    CalcadasUrbanizacao,
    ModeracaoTrafego,
    MobiliarioUrbano,
    PavimentacaoGeometria,
}

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

    public CategoriaQuantitativo Category { get; set; }

    public string GroupName => GroupLabel(Group);
    public string CategoryName => CategoryLabel(Category);

    /// <summary>Quantidade principal na unidade de medição do item (m², m ou un).</summary>
    public double MainQuantity => Unit switch
    {
        "m²" => Area,
        "m" => PaintedLength,
        _ => Units,
    };

    /// <summary>Área em planta: pintada (tintas) ou ocupada (concreto, grama, metal...).</summary>
    public string AreaKind => MarkingColors.IsPaint(Color) ? "pintada" : "em planta";

    public static string CategoryLabel(CategoriaQuantitativo c) => c switch
    {
        CategoriaQuantitativo.SinalizacaoHorizontal => "1. Sinalização horizontal",
        CategoriaQuantitativo.SinalizacaoVertical => "2. Sinalização vertical",
        CategoriaQuantitativo.DispositivosSegregacao => "3. Dispositivos auxiliares e segregação física",
        CategoriaQuantitativo.Acessibilidade => "4. Acessibilidade (rampas e piso tátil)",
        CategoriaQuantitativo.CalcadasUrbanizacao => "5. Calçadas, meios-fios e urbanização",
        CategoriaQuantitativo.ModeracaoTrafego => "6. Moderação de tráfego",
        CategoriaQuantitativo.MobiliarioUrbano => "7. Mobiliário e elementos urbanos",
        CategoriaQuantitativo.PavimentacaoGeometria => "8. Pavimentação e geometria viária",
        _ => c.ToString(),
    };

    /// <summary>Categoria de uma marca (a partir do grupo e do tipo).</summary>
    public static CategoriaQuantitativo Categorize(MarkingDefinition def, GrupoMarca group) => def switch
    {
        RampDefinition or TactileRouteDefinition => CategoriaQuantitativo.Acessibilidade,
        RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition => CategoriaQuantitativo.PavimentacaoGeometria,
        _ => group switch
        {
            GrupoMarca.SinalizacaoVertical => CategoriaQuantitativo.SinalizacaoVertical,
            GrupoMarca.Dispositivo => CategoriaQuantitativo.DispositivosSegregacao,
            GrupoMarca.Acessibilidade => CategoriaQuantitativo.Acessibilidade,
            GrupoMarca.Urbanizacao => CategoriaQuantitativo.CalcadasUrbanizacao,
            GrupoMarca.Moderacao => CategoriaQuantitativo.ModeracaoTrafego,
            GrupoMarca.Mobiliario => CategoriaQuantitativo.MobiliarioUrbano,
            _ => CategoriaQuantitativo.SinalizacaoHorizontal,
        },
    };

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
        GrupoMarca.Urbanizacao => "Calçadas, meios-fios, sarjetas e rampas",
        GrupoMarca.SinalizacaoVertical => "Sinalização vertical",
        GrupoMarca.Mobiliario => "Mobiliário e elementos urbanos",
        GrupoMarca.Moderacao => "Moderação de tráfego",
        GrupoMarca.Detalhamento => "Detalhamento",
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
            if (def is IAnnotationDefinition) continue;
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
                        Code = info.Code, Name = info.Name, Group = info.Group, Color = color, Material = MarkingColors.IsPaint(color) ? matName : "",
                        Category = QuantityRow.Categorize(def, info.Group),
                        Unit = info.Unit, Reference = info.Reference, ConsumptionUnit = mat?.UnidadeConsumo ?? "",
                    };
                    rows[key] = row;
                }
                row.Area += area;
                // Consumo de tinta apenas para demarcação (não para calçadas, canteiros e dispositivos físicos).
                if (mat != null && MarkingColors.IsPaint(color) && def is not (DeviceMarkingDefinition or SignDefinition or UrbanElementDefinition or RampDefinition or TactileRouteDefinition)
                    && !(def is LinearMarkingDefinition { Code: "PTA" or "PTD" }))
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
            .OrderBy(r => r.Category).ThenBy(r => r.Group).ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Color)
            .ToList();
    }

    /// <summary>Totais por categoria (área, extensão e unidades).</summary>
    public static List<QuantityRow> CategorySummary(IEnumerable<QuantityRow> rows) =>
        rows.GroupBy(r => r.Category)
            .Select(g => new QuantityRow
            {
                Code = "SUBTOTAL",
                Name = QuantityRow.CategoryLabel(g.Key),
                Category = g.Key,
                Area = g.Sum(r => r.Area),
                PaintedLength = g.Sum(r => r.PaintedLength),
                Units = g.Sum(r => r.Units),
                Elements = g.Sum(r => r.Elements),
                MaterialConsumption = g.Sum(r => r.MaterialConsumption),
                GlassBeadsKg = g.Sum(r => r.GlassBeadsKg),
                Unit = "m²",
            })
            .OrderBy(r => r.Category).ToList();

    /// <summary>Totais por cor e material (resumo de compra de tinta).</summary>
    public static List<QuantityRow> Summary(IEnumerable<QuantityRow> rows) =>
        rows.Where(r => MarkingColors.IsPaint(r.Color) && r.Category is CategoriaQuantitativo.SinalizacaoHorizontal or CategoriaQuantitativo.Acessibilidade
                                                       or CategoriaQuantitativo.ModeracaoTrafego or CategoriaQuantitativo.CalcadasUrbanizacao)
            .GroupBy(r => (r.Color, r.Material))
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

    /// <summary>CSV no padrão brasileiro (separador ";" e vírgula decimal), pronto para o Excel – separado por categoria.</summary>
    public static string ToCsv(IEnumerable<QuantityRow> rows, IEnumerable<QuantityRow>? summary = null)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var sb = new StringBuilder();
        const string header = "Categoria;Grupo;Código;Descrição;Cor;Material;Quantidade;Unidade;Área (m²);Extensão (m);Unidades;Elementos;Consumo estimado;Unidade consumo;Microesferas (kg);Referência";
        void Row(QuantityRow r) => sb.AppendLine(string.Join(";",
            Esc(r.CategoryName), Esc(r.GroupName), Esc(r.Code), Esc(r.Name), r.Color, Esc(r.Material),
            r.MainQuantity.ToString("0.00", pt), Esc(r.Unit),
            r.Area.ToString("0.00", pt), r.PaintedLength.ToString("0.00", pt), r.Units, r.Elements,
            r.MaterialConsumption.ToString("0.00", pt), Esc(r.ConsumptionUnit), r.GlassBeadsKg.ToString("0.00", pt), Esc(r.Reference)));
        var list = rows.ToList();
        foreach (var g in list.GroupBy(r => r.Category).OrderBy(g => g.Key))
        {
            sb.AppendLine(Esc(QuantityRow.CategoryLabel(g.Key).ToUpperInvariant()));
            sb.AppendLine(header);
            foreach (var r in g) Row(r);
            var sub = CategorySummary(g).First();
            sb.AppendLine(string.Join(";", "", "", "SUBTOTAL", "", "", "", "", "", sub.Area.ToString("0.00", pt), sub.PaintedLength.ToString("0.00", pt),
                sub.Units, sub.Elements, sub.MaterialConsumption.ToString("0.00", pt), "", sub.GlassBeadsKg.ToString("0.00", pt), ""));
            sb.AppendLine();
        }
        sb.AppendLine("RESUMO POR CATEGORIA");
        sb.AppendLine("Categoria;Área (m²);Extensão (m);Unidades;Elementos");
        foreach (var c in CategorySummary(list))
            sb.AppendLine(string.Join(";", Esc(c.Name), c.Area.ToString("0.00", pt), c.PaintedLength.ToString("0.00", pt), c.Units, c.Elements));
        if (summary != null)
        {
            sb.AppendLine();
            sb.AppendLine("RESUMO DE PINTURA POR COR E MATERIAL");
            sb.AppendLine("Cor;Material;Área (m²);Consumo estimado;Unidade consumo;Microesferas (kg)");
            foreach (var r in summary)
                sb.AppendLine(string.Join(";", r.Color, Esc(r.Material), r.Area.ToString("0.00", pt), r.MaterialConsumption.ToString("0.00", pt), Esc(r.ConsumptionUnit), r.GlassBeadsKg.ToString("0.00", pt)));
        }
        return sb.ToString();
    }

    private static string Esc(string s) =>
        s.Contains(';') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"").Replace("\n", " ") + "\"" : s;
}
