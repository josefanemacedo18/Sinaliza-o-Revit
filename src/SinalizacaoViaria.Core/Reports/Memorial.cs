using System.Globalization;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Reports;

/// <summary>Bloco do memorial (título, parágrafo, lista, campo a preencher ou tabela).</summary>
public abstract record MemorialBlock;

public sealed record MemHeading(int Level, string Text) : MemorialBlock;

public sealed record MemParagraph(string Text, bool Italic = false) : MemorialBlock;

public sealed record MemBullets(IReadOnlyList<string> Items) : MemorialBlock;

/// <summary>Campo editável (controle de conteúdo do Word) – em branco, para o usuário preencher.</summary>
public sealed record MemField(string Label, string Tag) : MemorialBlock;

/// <summary>Tabela: larguras relativas das colunas, colunas numéricas (alinhadas à direita) e última linha de total.</summary>
public sealed record MemTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<double> Widths,
    IReadOnlyList<int>? Numeric = null, bool TotalRow = false) : MemorialBlock;

public sealed class MemorialDocument
{
    public string Title { get; init; } = "MEMORIAL DESCRITIVO";
    public List<MemorialBlock> Blocks { get; } = new();

    /// <summary>Títulos de nível 1 (seções), na ordem.</summary>
    public IEnumerable<string> Sections => Blocks.OfType<MemHeading>().Where(h => h.Level == 1).Select(h => h.Text);
}

/// <summary>Dados do projeto para o memorial: as marcas e o quadro de quantidades (o mesmo da ferramenta Quantitativos).</summary>
public sealed class MemorialInput
{
    public required IReadOnlyList<MarkingDefinition> Definitions { get; init; }
    public required IReadOnlyList<QuantityRow> Rows { get; init; }
    /// <summary>Eixo resolvido de uma marca (extensão das vias). Nulo = sem extensão.</summary>
    public Func<MarkingDefinition, Polyline2?>? PathOf { get; init; }
    /// <summary>Arquivo do modelo (informativo).</summary>
    public string? ModelName { get; init; }
    public DateTime Date { get; init; } = DateTime.Now;
}

/// <summary>
/// Memorial descritivo gerado do modelo: objetivo e normas, vias, interseções e rotatórias, sinalização horizontal e
/// vertical por código (quantidades do quantitativo), acessibilidade, drenagem e obras, resumo e responsabilidade técnica,
/// com os campos de identificação em branco para preencher.
/// </summary>
public static class Memorial
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public const string SecIdentificacao = "1. Identificação";
    public const string SecObjetivo = "2. Objetivo";
    public const string SecNormas = "3. Normas e referências";
    public const string SecVias = "4. Vias";
    public const string SecIntersecoes = "5. Interseções e rotatórias";
    public const string SecHorizontal = "6. Sinalização horizontal";
    public const string SecVertical = "7. Sinalização vertical";
    public const string SecAcessibilidade = "8. Acessibilidade";
    public const string SecDrenagem = "9. Drenagem e obras";
    public const string SecResumo = "10. Resumo do quantitativo";
    public const string SecResponsabilidade = "11. Responsabilidade técnica";

    public static readonly string[] AllSections =
    {
        SecIdentificacao, SecObjetivo, SecNormas, SecVias, SecIntersecoes, SecHorizontal, SecVertical, SecAcessibilidade, SecDrenagem,
        SecResumo, SecResponsabilidade,
    };

    /// <summary>Campos em branco (rótulo, etiqueta do controle de conteúdo).</summary>
    public static readonly (string Label, string Tag)[] Fields =
    {
        ("Nome do projeto", "nome_projeto"), ("Responsável técnico", "responsavel_tecnico"), ("ART nº", "art"),
    };

    public static string N(double v, int dec = 2) => v.ToString("N" + dec, Pt);

    /// <summary>Nome da via no memorial (observação da via ou número de ordem).</summary>
    public static string RoadName(RoadPavementDefinition p, int index) =>
        !string.IsNullOrWhiteSpace(p.Notes) ? p.Notes!.Trim() : $"Via {index:00}";

    public static string PavementName(TipoPavimento m) => m switch
    {
        TipoPavimento.Bloquete => "Blocos intertravados de concreto",
        TipoPavimento.Concreto => "Pavimento de concreto",
        TipoPavimento.Terra => "Terra (leito natural / revestimento primário)",
        TipoPavimento.Nenhum => "Sem pavimento",
        _ => "Revestimento asfáltico (CBUQ)",
    };

    public static string ControlName(ControleIntersecao c) => c switch
    {
        ControleIntersecao.Pare => "PARE (R-1) nas vias secundárias",
        ControleIntersecao.DePreferencia => "Dê a preferência (R-2) nas vias secundárias",
        ControleIntersecao.Semaforo => "Semafórica",
        _ => "Sem sinalização de controle",
    };

    /// <summary>Tipos (I a IV) presentes na interseção.</summary>
    public static string IntersectionType(IntersectionDefinition it)
    {
        var t = new List<string>();
        if (it.SplitterIslands != TipoIlha.Nenhuma) t.Add("II (ilha separadora)");
        if (it.RightTurnIslands != TipoIlha.Nenhuma || it.LegSettings.Any(l => l.RightTurnChannel is { } ch && ch != TipoIlha.Nenhuma)) t.Add("III (conversão livre à direita)");
        if (it.LeftTurnPockets || it.LegSettings.Any(l => l.Treatment == true)) t.Add("IV (bolsão de conversão à esquerda)");
        return t.Count == 0 ? "I (sem refúgio)" : string.Join(" + ", t);
    }

    /// <summary>Seção transversal (do bordo esquerdo ao direito) com as larguras de cada elemento.</summary>
    public static string SectionText(RoadPavementDefinition p)
    {
        var s = RoadTemplates.FromJson(p.SetupJson);
        if (s == null)
            return $"pista {N(p.LeftWidth + p.RightWidth)} m" + (p.LeftSidewalk + p.RightSidewalk > 0.01
                ? $"; calçadas {N(p.LeftSidewalk)} m (esq.) e {N(p.RightSidewalk)} m (dir.)" : "");
        string Side(IEnumerable<ElementoSecao> els) => string.Join(" · ", els.Select(e => $"{ElementoSecao.Rotulo(e.Tipo)} {N(e.Largura)}"));
        var center = s.TwoWay && s.Center == CenterTreatment.Canteiro ? $" | canteiro central {N(s.MedianWidth)} | " : " | eixo | ";
        var left = Side(Enumerable.Reverse(s.Left));
        var right = Side(s.Right);
        return (left.Length > 0 ? left : "—") + center + (right.Length > 0 ? right : "—");
    }

    public static MemorialDocument Build(MemorialInput input)
    {
        var doc = new MemorialDocument { Title = "MEMORIAL DESCRITIVO – SINALIZAÇÃO VIÁRIA E ACESSIBILIDADE" };
        var b = doc.Blocks;
        var defs = input.Definitions;
        var rows = input.Rows;
        var roads = defs.OfType<RoadPavementDefinition>().ToList();
        var names = new Dictionary<string, string>();
        for (int i = 0; i < roads.Count; i++) names[roads[i].Id] = RoadName(roads[i], i + 1);
        double Length(MarkingDefinition d) => input.PathOf?.Invoke(d)?.Length ?? 0;
        var totalLength = roads.Sum(Length);
        var intersections = defs.OfType<IntersectionDefinition>().ToList();
        var roundabouts = defs.OfType<RoundaboutDefinition>().ToList();

        // 1. Identificação – campos em branco.
        b.Add(new MemHeading(1, SecIdentificacao));
        foreach (var (label, tag) in Fields) b.Add(new MemField(label, tag));
        b.Add(new MemField("Local / município", "local"));
        if (!string.IsNullOrWhiteSpace(input.ModelName)) b.Add(new MemParagraph($"Modelo: {input.ModelName}", true));
        b.Add(new MemParagraph($"Gerado em {input.Date:dd/MM/yyyy} a partir do modelo BIM (Revit) com o SinalizaBIM.", true));

        // 2. Objetivo.
        b.Add(new MemHeading(1, SecObjetivo));
        b.Add(new MemParagraph(
            $"Este memorial descreve o projeto de geometria viária, sinalização horizontal e vertical e acessibilidade de {roads.Count} via(s), " +
            $"com {N(totalLength)} m de eixo, {intersections.Count} interseção(ões) e {roundabouts.Count} rotatória(s). As descrições e as " +
            "quantidades foram extraídas do modelo e coincidem com o quadro de quantidades do projeto (ferramenta Quantitativos)."));
        b.Add(new MemField("Complemento do objetivo (opcional)", "objetivo_complemento"));

        // 3. Normas e referências (as citadas pelo plugin; edições a confirmar com o órgão).
        b.Add(new MemHeading(1, SecNormas));
        b.Add(new MemBullets(new[]
        {
            "Código de Trânsito Brasileiro (CTB) – art. 60 (classificação das vias) e art. 207 (conversões).",
            "Manual Brasileiro de Sinalização de Trânsito (MBST/CONTRAN) – Vol. I (regulamentação), Vol. II (advertência), Vol. III (indicação), " +
            "Vol. IV (sinalização horizontal), Vol. VI (dispositivos auxiliares) e Vol. VII (sinalização temporária).",
            "ABNT NBR 9050 – acessibilidade a edificações, mobiliário, espaços e equipamentos urbanos.",
            "ABNT NBR 16537 – sinalização tátil no piso.",
            "ABNT NBR 11862 (tinta) e NBR 13132 (termoplástico) – materiais de demarcação; ABNT NBR 16184 – microesferas de vidro.",
            "ABNT NBR 11904 e NBR 14644 – placas e películas retrorrefletivas.",
            "DNIT – manual de projeto de interseções [a confirmar].",
        }));
        b.Add(new MemParagraph("Edições das normas e manuais: [a confirmar] com o órgão com circunscrição sobre a via.", true));

        // 4. Vias.
        b.Add(new MemHeading(1, SecVias));
        if (roads.Count == 0) b.Add(new MemParagraph("Nenhuma via no modelo."));
        else
        {
            b.Add(new MemParagraph($"{roads.Count} via(s), {N(totalLength)} m de eixo. Seção transversal do bordo esquerdo ao direito, no sentido do eixo (m)."));
            var tr = new List<IReadOnlyList<string>>();
            for (int i = 0; i < roads.Count; i++)
            {
                var p = roads[i];
                var s = RoadTemplates.FromJson(p.SetupJson);
                var way = (s?.TwoWay ?? p.TwoWay) ? "mão dupla" : "mão única";
                var speed = s != null ? $", {s.Speed:0} km/h" : "";
                tr.Add(new[]
                {
                    names[p.Id], Hierarquia.Label(p.Hierarchy), SectionText(p), N(p.TotalLeft + p.TotalRight), N(Length(p)),
                    $"{PavementName(p.Material)} – e = {N(p.ActualThickness * 100, 0)} cm", way + speed,
                });
            }
            tr.Add(new[] { "Total", "", "", "", N(totalLength), "", "" });
            b.Add(new MemTable(new[] { "Via", "Hierarquia (CTB art. 60)", "Seção transversal (m)", "Largura total (m)", "Extensão (m)", "Pavimento", "Operação" },
                tr, new[] { 1.0, 1.3, 3.2, 0.9, 0.9, 1.6, 1.1 }, new[] { 3, 4 }, true));
        }

        // 5. Interseções e rotatórias.
        b.Add(new MemHeading(1, SecIntersecoes));
        if (intersections.Count == 0 && roundabouts.Count == 0) b.Add(new MemParagraph("Nenhuma interseção ou rotatória no modelo."));
        if (intersections.Count > 0)
        {
            b.Add(new MemHeading(2, "5.1 Interseções"));
            var ir = intersections.Select((it, i) => (IReadOnlyList<string>)new[]
            {
                $"I-{i + 1:00}", string.Join(" × ", it.RoadIds.Select(id => names.GetValueOrDefault(id) ?? "via")),
                IntersectionType(it), ControlName(it.Control), N(it.CornerRadius, 1), it.Crosswalks ? $"sim ({N(it.CrosswalkWidth, 1)} m)" : "não",
                it.Ramps ? "sim" : "não",
            }).ToList();
            b.Add(new MemTable(new[] { "Nº", "Vias", "Tipo", "Controle", "Raio das esquinas (m)", "Faixas de pedestres", "Rampas" },
                ir, new[] { 0.6, 2.2, 2.0, 2.0, 0.9, 1.1, 0.7 }, new[] { 4 }));
        }
        if (roundabouts.Count > 0)
        {
            b.Add(new MemHeading(2, "5.2 Rotatórias"));
            var rr = roundabouts.Select((r, i) => (IReadOnlyList<string>)new[]
            {
                $"R-{i + 1:00}", r.Type.ToString(), N(2 * r.OuterRadius, 1), r.Lanes.ToString(Pt), r.Legs.Count.ToString(Pt), r.IslandType.ToString(),
            }).ToList();
            b.Add(new MemTable(new[] { "Nº", "Tipo", "Diâmetro inscrito (m)", "Faixas no anel", "Ramos", "Ilha central" }, rr,
                new[] { 0.6, 1.4, 1.1, 0.9, 0.7, 1.3 }, new[] { 2, 3, 4 }));
        }

        // 6. Sinalização horizontal – por código, cor e material (somadas as hierarquias), como no quantitativo.
        b.Add(new MemHeading(1, SecHorizontal));
        b.Add(new MemParagraph("Marcas viárias conforme o MBST Vol. IV. Áreas pintadas, extensões e consumo estimado extraídos do modelo."));
        var hor = HorizontalLines(rows);
        if (hor.Count == 0) b.Add(new MemParagraph("Nenhuma marca horizontal no modelo."));
        else
        {
            var hr = hor.Select(x => (IReadOnlyList<string>)new[] { x.Code, x.Name, x.Color, x.Material, N(x.Area), N(x.Length), x.Units.ToString(Pt), x.Consumption }).ToList();
            hr.Add(new[] { "Total", "", "", "", N(hor.Sum(x => x.Area)), N(hor.Sum(x => x.Length)), hor.Sum(x => x.Units).ToString(Pt), "" });
            b.Add(new MemTable(new[] { "Código", "Descrição", "Cor", "Material", "Área (m²)", "Extensão (m)", "Unid.", "Consumo est." }, hr,
                new[] { 0.9, 2.6, 0.8, 1.6, 0.9, 0.9, 0.6, 1.0 }, new[] { 4, 5, 6 }, true));
        }

        // 7. Sinalização vertical – placas por código; dispositivos auxiliares.
        b.Add(new MemHeading(1, SecVertical));
        var signs = UnitLines(rows, CategoriaQuantitativo.SinalizacaoVertical);
        if (signs.Count == 0) b.Add(new MemParagraph("Nenhuma placa no modelo."));
        else
        {
            b.Add(new MemParagraph("Placas conforme o MBST Vol. I, II e III, com suportes e películas conforme a especificação do projeto."));
            var sr = signs.Select(x => (IReadOnlyList<string>)new[] { x.Code, x.Name, x.Units.ToString(Pt) }).ToList();
            sr.Add(new[] { "Total", "", signs.Sum(x => x.Units).ToString(Pt) });
            b.Add(new MemTable(new[] { "Código", "Descrição", "Quantidade (un)" }, sr, new[] { 1.0, 4.5, 1.2 }, new[] { 2 }, true));
        }
        var devices = UnitLines(rows, CategoriaQuantitativo.DispositivosSegregacao);
        if (devices.Count > 0)
        {
            b.Add(new MemHeading(2, "7.1 Dispositivos auxiliares e segregação física"));
            b.Add(new MemTable(new[] { "Código", "Descrição", "Quantidade", "Un." },
                devices.Select(x => (IReadOnlyList<string>)new[] { x.Code, x.Name, N(x.Quantity), x.Unit }).ToList(), new[] { 1.0, 4.0, 1.0, 0.6 }, new[] { 2 }));
        }

        // 8. Acessibilidade.
        b.Add(new MemHeading(1, SecAcessibilidade));
        var ramps = defs.OfType<RampDefinition>().ToList();
        var walkRamps = ramps.Where(r => r.Type != TipoRampa.AcessoVeiculos).ToList();
        var exts = defs.OfType<CurbExtensionDefinition>().ToList();
        var extLen = exts.Sum(e => input.PathOf?.Invoke(e)?.Length ?? 0);
        var parts = new List<string>
        {
            $"{walkRamps.Count} rebaixamento(s) de calçada" + (walkRamps.Count > 0
                ? $" (inclinação máxima {N(walkRamps.Max(r => r.Slope) * 100)} %, largura mínima {N(walkRamps.Min(r => r.Width))} m)" : ""),
            $"{ramps.Count - walkRamps.Count} guia(s) rebaixada(s) para veículos",
            $"{exts.Count} extensão(ões) de calçada" + (extLen > 0.01 ? $" ({N(extLen)} m de meio-fio avançado)" : ""),
        };
        b.Add(new MemParagraph("Rebaixamentos conforme a ABNT NBR 9050 e piso tátil de alerta e direcional conforme a ABNT NBR 16537: " +
                               string.Join("; ", parts) + "."));
        var acc = UnitLines(rows, CategoriaQuantitativo.Acessibilidade);
        if (acc.Count > 0)
            b.Add(new MemTable(new[] { "Código", "Descrição", "Quantidade", "Un.", "Área (m²)" },
                acc.Select(x => (IReadOnlyList<string>)new[] { x.Code, x.Name, N(x.Quantity), x.Unit, N(x.Area) }).ToList(), new[] { 1.0, 3.6, 1.0, 0.6, 1.0 }, new[] { 2, 4 }));

        // 9. Drenagem e obras.
        b.Add(new MemHeading(1, SecDrenagem));
        var dren = UnitLines(rows, CategoriaQuantitativo.Drenagem).Concat(UnitLines(rows, CategoriaQuantitativo.ObrasDeArteTerraplenagem)).ToList();
        if (dren.Count == 0) b.Add(new MemParagraph("Nenhum dispositivo de drenagem, obra de arte ou contenção no modelo."));
        else
            b.Add(new MemTable(new[] { "Código", "Descrição", "Quantidade", "Un." },
                dren.Select(x => (IReadOnlyList<string>)new[] { x.Code, x.Name, N(x.Quantity), x.Unit }).ToList(), new[] { 1.0, 4.0, 1.0, 0.6 }, new[] { 2 }));

        // 10. Resumo do quantitativo (por categoria).
        b.Add(new MemHeading(1, SecResumo));
        var cs = QuantityCalculator.CategorySummary(rows);
        b.Add(new MemTable(new[] { "Categoria", "Área (m²)", "Extensão (m)", "Unidades", "Elementos" },
            cs.Select(c => (IReadOnlyList<string>)new[] { c.Name, N(c.Area), N(c.PaintedLength), c.Units.ToString(Pt), c.Elements.ToString(Pt) }).ToList(),
            new[] { 3.4, 1.0, 1.0, 0.9, 0.9 }, new[] { 1, 2, 3, 4 }));

        // 11. Responsabilidade técnica – campos em branco.
        b.Add(new MemHeading(1, SecResponsabilidade));
        b.Add(new MemField("Responsável técnico", "responsavel_tecnico_assinatura"));
        b.Add(new MemField("Registro profissional (CREA/CAU)", "registro"));
        b.Add(new MemField("ART nº", "art_assinatura"));
        b.Add(new MemField("Local e data", "local_data"));
        b.Add(new MemParagraph("\n\n______________________________________________\nAssinatura do responsável técnico"));
        return doc;
    }

    /// <summary>Linha da sinalização horizontal no memorial (soma das hierarquias do quantitativo).</summary>
    public sealed record HorizontalLine(string Code, string Name, string Color, string Material, double Area, double Length, int Units, string Consumption);

    public static List<HorizontalLine> HorizontalLines(IEnumerable<QuantityRow> rows) =>
        rows.Where(r => r.Category == CategoriaQuantitativo.SinalizacaoHorizontal)
            .GroupBy(r => (r.Code, r.Color, r.Material))
            .Select(g =>
            {
                var c = g.Sum(r => r.MaterialConsumption);
                return new HorizontalLine(g.Key.Code, g.First().Name, g.First().ColorLabel, g.Key.Material, g.Sum(r => r.Area), g.Sum(r => r.PaintedLength),
                    g.Sum(r => r.Units), c > 1e-6 ? $"{N(c)} {g.First().ConsumptionUnit}".Trim() : "");
            })
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Color).ToList();

    /// <summary>Linha por código (memorial: modelos contados) – soma das hierarquias.</summary>
    public sealed record UnitLine(string Code, string Name, int Units, double Quantity, string Unit, double Area);

    public static List<UnitLine> UnitLines(IEnumerable<QuantityRow> rows, CategoriaQuantitativo category) =>
        rows.Where(r => r.Category == category)
            .GroupBy(r => r.Code)
            .Select(g => new UnitLine(g.Key, g.First().Name, g.Sum(r => r.Units), g.Sum(r => r.MainQuantity), g.First().Unit, g.Sum(r => r.Area)))
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
}
