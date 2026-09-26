using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Formulários das ferramentas de detalhamento (criação e edição).</summary>
internal static class DetailForms
{
    private static BuildContext Ctx(double scale, IReadOnlyList<MarkingDefinition> all, Func<MarkingDefinition, MarkingGeometry?>? geometry) => new()
    {
        Catalog = PluginContext.Catalog,
        Glyphs = PluginContext.Glyphs,
        ViewScale = scale,
        Lookup = id => all.FirstOrDefault(d => d.Id == id),
        AllDefinitions = () => all,
        GeometryOf = geometry,
    };

    private static FormPreview Paper(MarkingGeometry g, double scale) => new(g, Paper: true, ViewScale: scale);

    private static readonly (string, TerminalCota)[] Terminals =
        { ("Traço a 45° (padrão ABNT)", TerminalCota.Traco), ("Seta cheia", TerminalCota.Seta), ("Ponto", TerminalCota.Ponto) };

    public static FormWindow Section(SectionDimensionDefinition d, bool edit) =>
        new FormWindow(edit ? "Editar cota de seção" : "Cotar seção transversal", "Cota automática da seção",
                "Clique dois pontos atravessando a via (ex.: de alinhamento a alinhamento). O plugin encontra as linhas, faixas, canteiros, " +
                "meios-fios e calçadas cortados e cria a cadeia de cotas (larguras eixo a eixo das linhas), os nomes de cada trecho, o eixo, " +
                "a cota total e as marcas do corte (SEÇÃO A–A). A cota se atualiza quando a sinalização muda. Repete até ESC.",
                d, () =>
                {
                    // Pré-visualização numa via de exemplo (calçadas, meios-fios, ciclofaixa, linhas e eixo).
                    var (defs, geo, path) = DetailGenerator.SectionSample();
                    var c = (SectionDimensionDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                    c.Start = new Vec2(15, -8.45);
                    c.End = new Vec2(15, 8.45);
                    var ctx = new BuildContext
                    {
                        Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs, ViewScale = 100,
                        AllDefinitions = () => defs, GeometryOf = geo, PathOf = path,
                    };
                    var g = DetailGenerator.SectionDimensions(c, ctx);
                    foreach (var x in defs) if (geo(x) is { } gx) g.Pieces.InsertRange(0, gx.Pieces);
                    return new FormPreview(g, Paper: true, ViewScale: 100, Info: "Exemplo: via com calçadas de 3,00 m, ciclofaixa e duas faixas por sentido.");
                }, false, edit ? "Aplicar" : "Iniciar", 1100, 720)
            .Section("Cadeia de cotas")
            .Number("Afastamento da linha de cota (mm)", () => d.OffsetMm, v => d.OffsetMm = v, -100, 100, "0.#", "+ à esquerda do sentido 1º→2º clique.")
            .Number("Altura do texto (mm)", () => d.TextMm, v => d.TextMm = v, 0.8, 10, "0.#")
            .Choice("Terminal da cota", Terminals, () => d.Terminal, v => d.Terminal = v)
            .Choice("Casas decimais", new[] { ("0,00 (centímetros)", 2), ("0,0", 1), ("0,000 (milímetros)", 3) }, () => d.Decimals, v => d.Decimals = v)
            .Check("Cotar linhas pintadas pelo eixo (larguras de faixa eixo a eixo)", () => d.LineAxes, v => d.LineAxes = v)
            .Number("Largura máxima de \"linha\" (m)", () => d.AxisMaxWidth, v => d.AxisMaxWidth = v, 0.05, 2)
            .Check("Incluir os pontos clicados (extremos)", () => d.IncludeEnds, v => d.IncludeEnds = v)
            .Check("Cota total", () => d.Total, v => d.Total = v)
            .Section("O que cotar")
            .Check("Considerar a sinalização horizontal", () => d.Horizontal, v => d.Horizontal = v)
            .Check("Considerar calçadas, meios-fios, canteiros e dispositivos", () => d.Physical, v => d.Physical = v)
            .Section("Identificação")
            .Check("Nome de cada trecho (calçada, faixa de rolamento, ciclofaixa, canteiro...)", () => d.Labels, v => d.Labels = v)
            .Check("Marcar o eixo da via (traço-ponto)", () => d.AxisMarker, v => d.AxisMarker = v)
            .Check("Dividir as cotas no eixo (meias-larguras)", () => d.SplitAtAxis, v => d.SplitAtAxis = v)
            .Text("Letra do corte (vazio = sem marcas/título)", () => d.SectionLetter, v => d.SectionLetter = (v ?? "").Trim().ToUpperInvariant());

    public static FormWindow Typical(TypicalDetailDefinition d, MarkingDefinition target, double scale, bool edit)
    {
        var all = new List<MarkingDefinition> { target };
        d.MarkingTargetId = target.Id;
        return new FormWindow(edit ? "Editar detalhe típico" : "Detalhe típico", "Detalhe típico cotado",
                "Desenha a marca em escala ampliada com as cotas (traço/espaço, larguras, afastamentos; placa em elevação com altura livre). " +
                "Depois de confirmar, clique o canto superior esquerdo do detalhe.",
                d, () => { var c = (TypicalDetailDefinition)MarkingDefinition.FromJson(d.ToJson())!; c.Position = Vec2.Zero; return Paper(DetailGenerator.TypicalDetail(c, Ctx(scale, all, null)), scale); },
                false, edit ? "Aplicar" : "Inserir")
            .Number("Escala do detalhe 1:", () => d.DetailScale, v => d.DetailScale = v, 1, 500, "0", $"A vista está em 1:{scale:0}; o detalhe é ampliado nessa proporção.")
            .Number("Altura do texto (mm)", () => d.TextMm, v => d.TextMm = v, 0.8, 10, "0.#")
            .Text("Título (vazio = automático)", () => d.Title, v => d.Title = string.IsNullOrWhiteSpace(v) ? null : v)
            .Choice("Terminal das cotas", Terminals, () => d.Terminal, v => d.Terminal = v)
            .Check("Moldura em volta do detalhe", () => d.FrameBox, v => d.FrameBox = v);
    }

    public static FormWindow Table(QuantityTableDefinition d, double scale, IReadOnlyList<MarkingDefinition> all, Func<MarkingDefinition, MarkingGeometry?> geometry, bool edit)
    {
        var w = new FormWindow(edit ? "Editar quadro" : d.SignsOnly ? "Quadro de placas" : "Quadro de quantitativos",
            d.SignsOnly ? "Quadro de placas" : "Quadro de quantitativos na prancha",
            d.SignsOnly
                ? "Lista as placas do projeto com símbolo, numeração (Detalhar Placas → Numerar), código, descrição, dimensões e quantidade."
                : "Quantidades por categoria (m², m ou unidades conforme o item). O quadro se atualiza quando a sinalização muda. Depois de confirmar, clique o canto superior esquerdo.",
            d, () => { var c = (QuantityTableDefinition)MarkingDefinition.FromJson(d.ToJson())!; c.Position = Vec2.Zero; return Paper(DetailGenerator.QuantityTable(c, Ctx(scale, all, geometry)), scale); },
            false, edit ? "Aplicar" : "Inserir");
        w.Text("Título", () => d.Title, v => d.Title = v ?? "");
        if (!d.SignsOnly)
            w.Choice("Categoria", new (string, string?)[] { ("Todas as categorias", null) }
                    .Concat(Enum.GetValues<CategoriaQuantitativo>().Select(c => (QuantityRow.CategoryLabel(c), (string?)c.ToString()))),
                () => d.Category, v => d.Category = v);
        w.Number("Altura da linha (mm)", () => d.RowMm, v => d.RowMm = v, 3, 40, "0.#")
         .Number("Altura do texto (mm)", () => d.TextMm, v => d.TextMm = v, 0.8, 10, "0.#");
        return w;
    }

    public static FormWindow Notes(NotesDefinition d, double scale, bool edit) =>
        new FormWindow(edit ? "Editar notas" : "Notas gerais", "Notas gerais do projeto",
                "Uma nota por linha (numeradas automaticamente). O texto padrão traz as notas usuais de projeto de sinalização – adapte ao seu caso. " +
                "Depois de confirmar, clique o canto superior esquerdo.",
                d, () => { var c = (NotesDefinition)MarkingDefinition.FromJson(d.ToJson())!; c.Position = Vec2.Zero; return Paper(DetailGenerator.Notes(c, Ctx(scale, Array.Empty<MarkingDefinition>(), null)), scale); },
                false, edit ? "Aplicar" : "Inserir", 1000, 680)
            .Choice("Modelo de notas", NotesPresets.All.Select(p => (p.Label, p.Label)), () => NotesPresets.Match(d.Text), _ => { },
                "Escolha um modelo para preencher título e notas (depois edite à vontade).",
                preset: v => { if (NotesPresets.Get(v) is { } p) { d.Title = p.Title; d.Text = p.Text; } })
            .Text("Título", () => d.Title, v => d.Title = v ?? "")
            .Text("Notas", () => d.Text, v => d.Text = v ?? "", multiline: true)
            .Number("Largura do quadro (mm)", () => d.WidthMm, v => d.WidthMm = v, 40, 600, "0")
            .Number("Altura do texto (mm)", () => d.TextMm, v => d.TextMm = v, 0.8, 10, "0.#")
            .Check("Numerar as notas", () => d.Numbered, v => d.Numbered = v);

    public static FormWindow North(NorthArrowDefinition d, double scale, bool edit) =>
        new FormWindow(edit ? "Editar norte" : "Norte", "Indicação de norte", "Depois de confirmar, clique onde o norte deve ficar.",
                d, () => { var c = (NorthArrowDefinition)MarkingDefinition.FromJson(d.ToJson())!; c.Position = Vec2.Zero; return Paper(DetailGenerator.NorthArrow(c, Ctx(scale, Array.Empty<MarkingDefinition>(), null)), scale); },
                false, edit ? "Aplicar" : "Inserir", 760, 480)
            .Choice("Estilo", new[] { ("Clássico (meia seta)", EstiloNorte.Classico), ("Rosa dos ventos (N, S, L, O)", EstiloNorte.RosaDosVentos), ("Seta simples", EstiloNorte.Seta) },
                () => d.Style, v => d.Style = v)
            .Number("Tamanho (mm)", () => d.SizeMm, v => d.SizeMm = v, 4, 100, "0")
            .Number("Ângulo do norte (°, anti-horário)", () => d.AngleDeg, v => d.AngleDeg = v, -360, 360, "0.#");

    /// <summary>Formulário de edição para os detalhes desta família (null se não for daqui).</summary>
    public static (FormWindow Window, MarkingDefinition Working)? ForEdit(UIDocument uidoc, MarkingDefinition def)
    {
        var doc = uidoc.Document;
        var working = MarkingDefinition.FromJson(def.ToJson())!;
        var view = !string.IsNullOrEmpty(def.Output.ViewId) ? doc.GetElement(def.Output.ViewId) as View : null;
        var scale = (view ?? uidoc.ActiveView).Scale;
        FormWindow? w = working switch
        {
            SectionDimensionDefinition sd => Section(sd, true),
            TypicalDetailDefinition td when MarkingStorage.ById(doc, td.MarkingTargetId).FirstOrDefault()?.Definition is { } t => Typical(td, t, scale, true),
            QuantityTableDefinition qt => Table(qt, scale, MarkingStorage.Definitions(doc), new MarkingService(doc, uidoc.ActiveView).BuildGeometryOrNull, true),
            NotesDefinition nt => Notes(nt, scale, true),
            NorthArrowDefinition na => North(na, scale, true),
            _ => null,
        };
        return w == null ? null : (w, working);
    }
}

/// <summary>Modelos de notas gerais (sinalização viária, acessibilidade, obras, urbanização).</summary>
internal static class NotesPresets
{
    public sealed record Preset(string Label, string Title, string Text);

    public static readonly Preset[] All =
    {
        new("Personalizado (texto atual)", "", ""),
        new("Geral – sinalização viária", "NOTAS GERAIS", NotesDefinition.DefaultText),
        new("Sinalização horizontal", "NOTAS – SINALIZAÇÃO HORIZONTAL",
            "Cotas em metros, salvo indicação em contrário.\n" +
            "Marcas conforme o MBST – Volume IV (Sinalização Horizontal) do CONTRAN.\n" +
            "Linhas de divisão de fluxos opostos na cor amarela; de mesmo sentido, bordos e marcas transversais na cor branca.\n" +
            "Material: tinta à base de resina acrílica (NBR 11862) ou termoplástico (NBR 13132), espessura úmida conforme especificação.\n" +
            "Microesferas de vidro tipo I-B (premix) e II-A (drop-on) conforme ABNT NBR 16184.\n" +
            "Retrorrefletância inicial mínima de 250 mcd/lx/m² (branca) e 150 mcd/lx/m² (amarela).\n" +
            "Remover a sinalização existente conflitante antes da execução (fresagem ou jateamento – não pintar de preto).\n" +
            "Aplicar sobre pavimento limpo, seco e com temperatura entre 10 °C e 40 °C."),
        new("Sinalização vertical", "NOTAS – SINALIZAÇÃO VERTICAL",
            "Placas conforme o MBST – Volumes I (Regulamentação), II (Advertência) e III (Indicação) do CONTRAN.\n" +
            "Chapa de aço galvanizado nº 16 ou alumínio 2 mm, com película retrorrefletiva tipo I/III conforme ABNT NBR 14644.\n" +
            "Suportes em tubo de aço galvanizado Ø 2 1/2\", engastados em base de concreto com profundidade mínima de 0,60 m.\n" +
            "Altura livre de 2,10 m sob a placa em áreas com circulação de pedestres; 1,20 m em áreas rurais.\n" +
            "Afastamento lateral mínimo de 0,30 m entre a borda da placa e o meio-fio.\n" +
            "Placas levemente inclinadas (3° a 5°) em relação à perpendicular ao eixo, para evitar reflexo especular."),
        new("Acessibilidade (NBR 9050 / NBR 16537)", "NOTAS – ACESSIBILIDADE",
            "Rebaixamentos de calçada conforme ABNT NBR 9050: inclinação máxima de 8,33 % e largura mínima de 1,20 m (1,50 m recomendado).\n" +
            "Abas laterais com inclinação máxima de 10 % (ou canteiro/obstáculo que impeça a circulação lateral).\n" +
            "Desnível entre o rebaixamento e a pista: 0 cm (máximo tolerado de 1,5 cm, chanfrado).\n" +
            "Piso tátil de alerta e direcional conforme ABNT NBR 16537, em cor contrastante com o piso adjacente.\n" +
            "Faixa livre da calçada com largura mínima de 1,20 m, sem obstáculos, inclinação transversal máxima de 3 %.\n" +
            "Rebaixamentos alinhados entre si e com a faixa de pedestres."),
        new("Obras e desvios (sinalização temporária)", "NOTAS – SINALIZAÇÃO DE OBRAS",
            "Sinalização temporária conforme o MBST – Volume VII (Sinalização Temporária) do CONTRAN.\n" +
            "Placas de obras com fundo laranja e película retrorrefletiva; dispositivos (cones, cavaletes, tambores) com faixas retrorrefletivas.\n" +
            "Cones a cada 5 m nas transições e a cada 10 m nos trechos paralelos, salvo indicação.\n" +
            "Manter iluminação e sinalização luminosa noturna durante todo o período da obra.\n" +
            "Remover toda a sinalização temporária ao término dos serviços e restabelecer a sinalização definitiva."),
        new("Urbanização e paisagismo", "NOTAS – URBANIZAÇÃO",
            "Calçadas em concreto desempenado (fck ≥ 20 MPa, e = 7 cm) sobre base de brita compactada, com juntas a cada 2,00 m.\n" +
            "Meio-fio pré-moldado de concreto (100 × 15 × 13 × 30 cm), rejuntado com argamassa 1:3.\n" +
            "Mobiliário urbano fora da faixa livre, na faixa de serviço, a no mínimo 0,50 m do meio-fio.\n" +
            "Árvores em canteiros com área permeável mínima de 1,00 m², espécies conforme o plano municipal de arborização.\n" +
            "Iluminação pública conforme ABNT NBR 5101."),
    };

    public static Preset? Get(string label) => All.FirstOrDefault(p => p.Label == label && p.Text.Length > 0);

    public static string Match(string text) => All.FirstOrDefault(p => p.Text.Length > 0 && p.Text == text)?.Label ?? All[0].Label;
}

/// <summary>Cotagem automática de seções transversais.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCotarSecao : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var view = DetailHelpers.RequireDetailView(uidoc);
        var template = UiHelpers.Remembered<SectionDimensionDefinition>("CotaSecao") ?? new SectionDimensionDefinition();
        if (UiHelpers.ShowModal(DetailForms.Section(template, false)) != true) return Result.Cancelled;
        UiHelpers.Remember("CotaSecao", template);
        PluginContext.SaveSettings();
        var results = new List<RenderResult>();
        while (true)
        {
            var a = Picking.PickPoint(uidoc, "Cotar seção: clique o 1º ponto (ex.: alinhamento/borda da calçada) – ESC encerra");
            if (a == null) break;
            var b = Picking.PickPoint(uidoc, "Cotar seção: clique o 2º ponto do outro lado da via");
            if (b == null) break;
            var d = (SectionDimensionDefinition)template.CloneWithNewId();
            d.Start = DetailHelpers.ToCore(a);
            d.End = DetailHelpers.ToCore(b);
            DetailHelpers.PrepareOutput(d, view);
            results.AddRange(MarkingCreator.Commit(uidoc, new[] { d }, "SV - Cotar seção"));
        }
        if (results.Count == 0) return Result.Cancelled;
        Report("Cota de seção", results);
        return Result.Succeeded;
    }
}

/// <summary>Detalhe típico cotado de uma marca existente.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdDetalheTipico : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var view = DetailHelpers.RequireDetailView(uidoc);
        var stored = MarkingPicker.PickOne(uidoc, "Selecione a sinalização a detalhar (linha, zebrado, vaga, placa...)");
        if (stored == null) return Result.Cancelled;
        if (stored.Definition is IAnnotationDefinition) throw new UserMessageException("Selecione uma sinalização, não um detalhe.");
        var d = UiHelpers.Remembered<TypicalDetailDefinition>("DetalheTipico") ?? new TypicalDetailDefinition();
        d.Title = null;
        if (UiHelpers.ShowModal(DetailForms.Typical(d, stored.Definition, view.Scale, false)) != true) return Result.Cancelled;
        UiHelpers.Remember("DetalheTipico", d);
        PluginContext.SaveSettings();
        var at = Picking.PickPoint(uidoc, "Clique o canto superior esquerdo do detalhe típico");
        if (at == null) return Result.Cancelled;
        var def = (TypicalDetailDefinition)d.CloneWithNewId();
        def.MarkingTargetId = stored.MarkingId;
        def.Position = DetailHelpers.ToCore(at);
        DetailHelpers.PrepareOutput(def, view);
        Report("Detalhe típico", MarkingCreator.Commit(uidoc, new[] { def }, "SV - Detalhe típico"));
        return Result.Succeeded;
    }
}

internal static class TableCommand
{
    public static Result Run(UIDocument uidoc, bool signsOnly)
    {
        var doc = uidoc.Document;
        var view = DetailHelpers.RequireDetailView(uidoc);
        var key = signsOnly ? "QuadroPlacas" : "QuadroQtd";
        var d = UiHelpers.Remembered<QuantityTableDefinition>(key) ?? new QuantityTableDefinition
        {
            SignsOnly = signsOnly,
            Title = signsOnly ? "QUADRO DE PLACAS – SINALIZAÇÃO VERTICAL" : "QUADRO DE QUANTITATIVOS – SINALIZAÇÃO VIÁRIA",
            RowMm = signsOnly ? 8 : 6,
        };
        var all = MarkingStorage.Definitions(doc);
        var service = new MarkingService(doc, uidoc.ActiveView);
        if (UiHelpers.ShowModal(DetailForms.Table(d, view.Scale, all, service.BuildGeometryOrNull, false)) != true) return Result.Cancelled;
        UiHelpers.Remember(key, d);
        PluginContext.SaveSettings();
        var at = Picking.PickPoint(uidoc, "Clique o canto superior esquerdo do quadro");
        if (at == null) return Result.Cancelled;
        var def = (QuantityTableDefinition)d.CloneWithNewId();
        def.Position = DetailHelpers.ToCore(at);
        DetailHelpers.PrepareOutput(def, view);
        CommandBase_Report(MarkingCreator.Commit(uidoc, new[] { def }, signsOnly ? "SV - Quadro de placas" : "SV - Quadro de quantitativos"));
        return Result.Succeeded;
    }

    private static void CommandBase_Report(List<RenderResult> results)
    {
        var w = results.SelectMany(r => r.Warnings).Distinct().ToList();
        if (w.Count > 0) TaskDialog.Show(CommandBase.AppTitle, string.Join("\n", w.Take(15).Select(x => "• " + x)));
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdQuadroQuantitativos : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => TableCommand.Run(uidoc, false);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdQuadroPlacas : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => TableCommand.Run(uidoc, true);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdNotas : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var view = DetailHelpers.RequireDetailView(uidoc);
        var d = UiHelpers.Remembered<NotesDefinition>("Notas") ?? new NotesDefinition();
        if (UiHelpers.ShowModal(DetailForms.Notes(d, view.Scale, false)) != true) return Result.Cancelled;
        UiHelpers.Remember("Notas", d);
        PluginContext.SaveSettings();
        var at = Picking.PickPoint(uidoc, "Clique o canto superior esquerdo das notas");
        if (at == null) return Result.Cancelled;
        var def = (NotesDefinition)d.CloneWithNewId();
        def.Position = DetailHelpers.ToCore(at);
        DetailHelpers.PrepareOutput(def, view);
        Report("Notas", MarkingCreator.Commit(uidoc, new[] { def }, "SV - Notas gerais"));
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdNorte : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var view = DetailHelpers.RequireDetailView(uidoc);
        var d = UiHelpers.Remembered<NorthArrowDefinition>("Norte") ?? new NorthArrowDefinition();
        if (UiHelpers.ShowModal(DetailForms.North(d, view.Scale, false)) != true) return Result.Cancelled;
        UiHelpers.Remember("Norte", d);
        PluginContext.SaveSettings();
        var at = Picking.PickPoint(uidoc, "Clique onde o norte deve ficar");
        if (at == null) return Result.Cancelled;
        var def = (NorthArrowDefinition)d.CloneWithNewId();
        def.Position = DetailHelpers.ToCore(at);
        DetailHelpers.PrepareOutput(def, view);
        Report("Norte", MarkingCreator.Commit(uidoc, new[] { def }, "SV - Norte"));
        return Result.Succeeded;
    }
}
