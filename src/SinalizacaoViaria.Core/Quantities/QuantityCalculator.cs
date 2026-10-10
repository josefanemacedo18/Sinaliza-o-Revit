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
    Drenagem,
    ObrasDeArteTerraplenagem,
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

    /// <summary>Subcategoria (elementos urbanos: iluminação, arborização...; demais: grupo da marca).</summary>
    public string Subcategory { get; set; } = "";

    /// <summary>Família do Revit inserida pelo usuário (não gerada pelo plugin).</summary>
    public bool IsFamily { get; set; }

    /// <summary>
    /// Linha de memorial: o item é contado por modelo (placas, dispositivos, mobiliário, moderação, rampas), sem
    /// desdobrar por cor nem material da pintura – cores e materiais entram só na sinalização horizontal.
    /// </summary>
    public bool Memorial { get; set; }

    /// <summary>Categorias contadas por modelo (memorial), sem linhas por cor/material.</summary>
    public static bool IsMemorialCategory(CategoriaQuantitativo c) =>
        c is CategoriaQuantitativo.SinalizacaoVertical or CategoriaQuantitativo.DispositivosSegregacao or CategoriaQuantitativo.MobiliarioUrbano
            or CategoriaQuantitativo.ModeracaoTrafego or CategoriaQuantitativo.Acessibilidade or CategoriaQuantitativo.Drenagem
            or CategoriaQuantitativo.ObrasDeArteTerraplenagem;

    /// <summary>Tipos de família do Revit consolidados na linha (famílias do usuário).</summary>
    public string FamilyTypes { get; set; } = "";

    public string SubcategoryName => string.IsNullOrEmpty(Subcategory) ? GroupName : Subcategory;

    /// <summary>Nome da cor para exibição ("—" em famílias do usuário).</summary>
    public string ColorLabel => IsFamily ? "—" : Color switch
    {
        MarkingColor.PavimentoConcreto => "Pav. concreto",
        MarkingColor.PavimentoTerra => "Terra",
        MarkingColor.RelevoTatil => "Relevo tátil",
        _ => Color.ToString(),
    };

    /// <summary>Consumo com unidade ("" quando não se aplica).</summary>
    public string ConsumptionText => MaterialConsumption > 1e-6
        ? MaterialConsumption.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " " + ConsumptionUnit
        : "";

    /// <summary>Detalhes da linha (referência, elementos, tipo de área, famílias).</summary>
    public string DetailText
    {
        get
        {
            var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            var parts = new List<string> { $"{Elements} elemento(s) no modelo" };
            if (Area > 1e-6) parts.Add($"área {AreaKind}: {Area.ToString("N2", pt)} m²");
            if (PaintedLength > 1e-6) parts.Add($"extensão: {PaintedLength.ToString("N2", pt)} m");
            if (RoadLength > 1e-6) parts.Add($"extensão de via (eixo): {RoadLength.ToString("N2", pt)} m");
            if (GlassBeadsKg > 1e-6) parts.Add($"microesferas: {GlassBeadsKg.ToString("N2", pt)} kg");
            var text = string.Join(" · ", parts);
            if (!string.IsNullOrWhiteSpace(FamilyTypes)) text += "\nFamílias/tipos: " + FamilyTypes;
            if (!string.IsNullOrWhiteSpace(Reference)) text += "\nReferência: " + Reference;
            return text;
        }
    }

    /// <summary>Hierarquia viária (CTB art. 60) da via a que os itens pertencem.</summary>
    public HierarquiaViaria? Hierarchy { get; set; }
    public string HierarchyName => Hierarquia.Label(Hierarchy);

    /// <summary>Extensão de via (eixo) – só nas linhas de pavimento da via.</summary>
    public double RoadLength { get; set; }

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
    public string AreaKind => Memorial ? "total" : MarkingColors.IsPaint(Color) ? "pintada" : "em planta";

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
        CategoriaQuantitativo.Drenagem => "9. Drenagem (bocas de lobo, grelhas, PVs)",
        CategoriaQuantitativo.ObrasDeArteTerraplenagem => "10. Obras de arte, contenções e terraplenagem",
        _ => c.ToString(),
    };

    /// <summary>Categoria de uma marca (a partir do grupo e do tipo).</summary>
    public static CategoriaQuantitativo Categorize(MarkingDefinition def, GrupoMarca group) => def switch
    {
        RampDefinition or TactileRouteDefinition => CategoriaQuantitativo.Acessibilidade,
        DrainageDefinition => CategoriaQuantitativo.Drenagem,
        BridgeDefinition or TunnelDefinition or TrenchDefinition or RetainingWallDefinition or SlopeDefinition or InterchangeDefinition => CategoriaQuantitativo.ObrasDeArteTerraplenagem,
        RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition or RailwayDefinition => CategoriaQuantitativo.PavimentacaoGeometria,
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
        var rows = new Dictionary<(string, MarkingColor, string, HierarquiaViaria?), QuantityRow>();
        foreach (var (def, geo) in items)
        {
            if (def is IAnnotationDefinition) continue;
            // Placas empilhadas no mesmo suporte: cada uma conta como uma unidade do seu código (o suporte é da principal).
            if (def is SignDefinition { Stack.Count: > 0 } stacked)
                foreach (var plate in SignStack.Plates(stacked).Skip(1))
                {
                    var pi = MarkingBuilder.Describe(plate, catalog);
                    var pk = (pi.Code, MarkingColor.Preta, "", def.Hierarchy);
                    if (!rows.TryGetValue(pk, out var pr))
                        rows[pk] = pr = new QuantityRow
                        {
                            Code = pi.Code, Name = pi.Name, Group = pi.Group, Color = MarkingColor.Preta, Memorial = true,
                            Category = QuantityRow.Categorize(plate, pi.Group), Hierarchy = def.Hierarchy, Unit = pi.Unit, Reference = pi.Reference,
                        };
                    pr.Units += 1;
                }
            var info = MarkingBuilder.Describe(def, catalog);
            var matName = def.Output.Material ?? defaultMaterial ?? catalog.Materiais.FirstOrDefault()?.Nome ?? "";
            var mat = catalog.Material(matName);
            var byColor = geo.AreaByColor;
            var first = true;
            var category = QuantityRow.Categorize(def, info.Group);
            var memorial = QuantityRow.IsMemorialCategory(category);
            foreach (var (color, area) in byColor.OrderByDescending(kv => kv.Value))
            {
                // Memorial: uma linha por modelo (todas as cores e materiais somados); horizontal: por cor e material.
                var key = memorial ? (info.Code, MarkingColor.Preta, "", def.Hierarchy) : (info.Code, color, matName, def.Hierarchy);
                if (!rows.TryGetValue(key, out var row))
                {
                    row = new QuantityRow
                    {
                        Code = info.Code, Name = info.Name, Group = info.Group, Color = color, Material = !memorial && MarkingColors.IsPaint(color) ? matName : "",
                        Memorial = memorial,
                        Category = category,
                        Subcategory = def is UrbanElementDefinition u && catalog.Movel(u.Code) is { } mv ? UrbanCategories.Label(UrbanCategories.Of(mv.Forma)) : "",
                        Hierarchy = def.Hierarchy,
                        Unit = info.Unit, Reference = info.Reference, ConsumptionUnit = mat?.UnidadeConsumo ?? "",
                    };
                    rows[key] = row;
                }
                row.Area += area;
                // Consumo de tinta apenas para demarcação (não para calçadas, canteiros e dispositivos físicos).
                if (!memorial && mat != null && MarkingColors.IsPaint(color) && def is not (DeviceMarkingDefinition or SignDefinition or UrbanElementDefinition or RampDefinition or TactileRouteDefinition)
                    && !(def is LinearMarkingDefinition { Code: "PTA" or "PTD" or "PLATAFORMA" }))
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
                    if (def is RoadPavementDefinition) row.RoadLength += geo.PathLength;
                    first = false;
                }
            }
        }
        return Sort(rows.Values);
    }

    private static List<QuantityRow> Sort(IEnumerable<QuantityRow> rows) => rows
        .OrderBy(r => r.Category).ThenBy(r => r.SubcategoryName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
        .ThenByDescending(r => Hierarquia.Rank(r.Hierarchy)).ThenBy(r => r.Color)
        .ToList();

    /// <summary>
    /// Acrescenta as famílias do Revit classificadas como elementos urbanos (uma linha por código, subcategoria e hierarquia),
    /// contando unidades e listando os tipos usados.
    /// </summary>
    public static List<QuantityRow> AddFamilies(IEnumerable<QuantityRow> rows, IEnumerable<FamilyItem> families)
    {
        var list = rows.ToList();
        foreach (var g in families.GroupBy(f => (Code: f.Code.Trim().ToUpperInvariant(), f.Category, f.Hierarchy)))
        {
            var first = g.First();
            var types = g.Select(f => f.TypeName).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t).ToList();
            var names = g.Select(f => f.Description).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            list.Add(new QuantityRow
            {
                Code = string.IsNullOrWhiteSpace(first.Code) ? "FAMÍLIA" : first.Code.Trim(),
                Name = names.Count == 1 ? names[0] : names.Count > 1 ? $"{names[0]} (+{names.Count - 1} variação(ões))" : first.TypeName,
                Group = GrupoMarca.Mobiliario,
                Color = MarkingColor.Metal,
                Category = CategoriaQuantitativo.MobiliarioUrbano,
                Subcategory = UrbanCategories.Label(g.Key.Category),
                Hierarchy = g.Key.Hierarchy,
                Units = g.Count(),
                Elements = g.Count(),
                PaintedLength = g.Sum(f => f.Length),
                Unit = "un",
                IsFamily = true,
                FamilyTypes = string.Join(", ", types),
                Reference = "Família do Revit (projeto)",
            });
        }
        return Sort(list);
    }

    /// <summary>Totais por subcategoria dentro de uma categoria (ex.: iluminação, arborização).</summary>
    public static List<QuantityRow> SubcategorySummary(IEnumerable<QuantityRow> rows) =>
        rows.GroupBy(r => (r.Category, r.SubcategoryName))
            .Select(g => new QuantityRow
            {
                Code = "SUBTOTAL",
                Name = g.Key.SubcategoryName,
                Category = g.Key.Category,
                Subcategory = g.Key.SubcategoryName,
                Area = g.Sum(r => r.Area),
                PaintedLength = g.Sum(r => r.PaintedLength),
                Units = g.Sum(r => r.Units),
                Elements = g.Sum(r => r.Elements),
                MaterialConsumption = g.Sum(r => r.MaterialConsumption),
                Unit = "",
            })
            .OrderBy(r => r.Category).ThenBy(r => r.Name).ToList();

    /// <summary>Resumo por hierarquia viária: extensão de vias, pavimento, pintura, placas e elementos.</summary>
    public static List<HierarchySummaryRow> HierarchySummary(IEnumerable<QuantityRow> rows) =>
        rows.GroupBy(r => r.Hierarchy)
            .Select(g => new HierarchySummaryRow
            {
                Hierarchy = g.Key,
                RoadLength = g.Sum(r => r.RoadLength),
                PavementArea = g.Where(r => r.Category == CategoriaQuantitativo.PavimentacaoGeometria && MarkingColors.IsPavement(r.Color)).Sum(r => r.Area),
                PaintedArea = g.Where(r => MarkingColors.IsPaint(r.Color) && r.Category != CategoriaQuantitativo.SinalizacaoVertical).Sum(r => r.Area),
                PaintedLength = g.Where(r => r.Category == CategoriaQuantitativo.SinalizacaoHorizontal).Sum(r => r.PaintedLength),
                Signs = g.Where(r => r.Category == CategoriaQuantitativo.SinalizacaoVertical).Sum(r => r.Units),
                Elements = g.Sum(r => r.Elements),
                PaintConsumption = g.Sum(r => r.MaterialConsumption),
            })
            .OrderByDescending(r => Hierarquia.Rank(r.Hierarchy)).ToList();

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

    /// <summary>
    /// CSV no padrão brasileiro (";" e vírgula decimal, UTF-8 com BOM) organizado como a janela: cabeçalho do projeto,
    /// um bloco por categoria e subcategoria com as colunas que fazem sentido para ela (memorial: modelo e quantidade;
    /// horizontal: cor, material, área, extensão e consumo), subtotais e resumos ao final.
    /// </summary>
    public static string ToCsv(IEnumerable<QuantityRow> rows, IEnumerable<QuantityRow>? summary = null, string? projectName = null)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var sb = new StringBuilder();
        string N(double v) => v.ToString("0.00", pt);
        string Q(double v) => v.ToString("0.##", pt);
        var list = rows.ToList();
        sb.AppendLine("QUANTITATIVO DE SINALIZAÇÃO VIÁRIA E URBANIZAÇÃO");
        if (!string.IsNullOrWhiteSpace(projectName)) sb.AppendLine(Esc("Projeto: " + projectName));
        sb.AppendLine(Esc($"Emitido em {DateTime.Now:dd/MM/yyyy HH:mm} – SinalizaBIM"));
        sb.AppendLine(Esc($"{list.Count} item(ns) em {list.Select(r => r.Category).Distinct().Count()} categoria(s)"));
        sb.AppendLine();
        const string header = "Item;Código;Descrição;Quantidade;Un.;Área (m²);Extensão (m);Unidades;Elementos;Cor;Material;Consumo estimado;Hierarquia viária;Referência";
        var n = 0;
        foreach (var g in list.GroupBy(r => r.Category).OrderBy(g => g.Key))
        {
            var memorial = QuantityRow.IsMemorialCategory(g.Key);
            sb.AppendLine(Esc(QuantityRow.CategoryLabel(g.Key).ToUpperInvariant()));
            sb.AppendLine(header);
            foreach (var sg in g.GroupBy(r => r.SubcategoryName).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(Esc("· " + sg.Key));
                foreach (var r in sg)
                {
                    n++;
                    sb.AppendLine(string.Join(";", n, Esc(r.Code), Esc(r.Name), Q(r.MainQuantity), Esc(r.Unit),
                        memorial ? "" : N(r.Area), memorial ? "" : N(r.PaintedLength), r.Units, r.Elements,
                        memorial || r.IsFamily ? "" : Esc(r.ColorLabel), memorial ? "" : Esc(r.Material),
                        memorial || r.MaterialConsumption <= 1e-6 ? "" : Esc(r.ConsumptionText), Esc(r.HierarchyName), Esc(r.Reference)));
                }
            }
            var sub = CategorySummary(g).First();
            sb.AppendLine(string.Join(";", "", "SUBTOTAL", "", "", "", memorial ? "" : N(sub.Area), memorial ? "" : N(sub.PaintedLength), sub.Units, sub.Elements, "", "",
                memorial || sub.MaterialConsumption <= 1e-6 ? "" : (N(sub.MaterialConsumption) + " " + sub.ConsumptionUnit).Trim(), "", ""));
            sb.AppendLine();
        }
        sb.AppendLine("RESUMO POR CATEGORIA");
        sb.AppendLine("Categoria;Itens;Área (m²);Extensão (m);Unidades;Consumo de tinta");
        foreach (var c in CategorySummary(list))
            sb.AppendLine(string.Join(";", Esc(c.Name), c.Elements, N(c.Area), N(c.PaintedLength), c.Units, N(c.MaterialConsumption)));
        sb.AppendLine();
        sb.AppendLine("RESUMO POR HIERARQUIA VIÁRIA (CTB art. 60)");
        sb.AppendLine("Hierarquia;Extensão de vias (m);Pavimento (m²);Área pintada (m²);Extensão pintada (m);Placas (un);Elementos;Consumo de tinta");
        foreach (var h in HierarchySummary(list))
            sb.AppendLine(string.Join(";", Esc(h.Name), N(h.RoadLength), N(h.PavementArea), N(h.PaintedArea), N(h.PaintedLength), h.Signs, h.Elements, N(h.PaintConsumption)));
        if (summary != null && summary.Any())
        {
            sb.AppendLine();
            sb.AppendLine("PINTURA POR COR E MATERIAL");
            sb.AppendLine("Cor;Material;Área (m²);Consumo estimado;Un. consumo;Microesferas (kg)");
            foreach (var r in summary)
                sb.AppendLine(string.Join(";", Esc(r.ColorLabel), Esc(r.Material), N(r.Area), N(r.MaterialConsumption), Esc(r.ConsumptionUnit), N(r.GlassBeadsKg)));
        }
        return sb.ToString();
    }

    private static string Esc(string s) =>
        s.Contains(';') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"").Replace("\n", " ") + "\"" : s;
}

/// <summary>Família do Revit classificada como elemento urbano (entrada do quantitativo).</summary>
public sealed record FamilyItem(string Code, string Description, string TypeName, CategoriaUrbana Category, HierarquiaViaria? Hierarchy, double Length = 0);

/// <summary>Totais de uma hierarquia viária.</summary>
public sealed class HierarchySummaryRow
{
    public HierarquiaViaria? Hierarchy { get; set; }
    public string Name => Hierarquia.Label(Hierarchy);
    public double RoadLength { get; set; }
    public double PavementArea { get; set; }
    public double PaintedArea { get; set; }
    public double PaintedLength { get; set; }
    public int Signs { get; set; }
    public int Elements { get; set; }
    public double PaintConsumption { get; set; }
}
