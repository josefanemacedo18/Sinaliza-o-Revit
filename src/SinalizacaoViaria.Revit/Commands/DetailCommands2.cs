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

    public static FormWindow Section(SectionDimensionDefinition d, bool edit) =>
        new FormWindow(edit ? "Editar cota de seção" : "Cotar seção transversal", "Cota automática da seção",
                "Clique dois pontos atravessando a via (ex.: de alinhamento a alinhamento). O plugin encontra as linhas, faixas, canteiros, " +
                "meios-fios e calçadas cortados e cria a cadeia de cotas (larguras eixo a eixo das linhas) e a cota total. " +
                "A cota se atualiza quando a sinalização muda. Repete até ESC.", null, null, false, edit ? "Aplicar" : "Iniciar", 560, 560)
            .Number("Afastamento da linha de cota (mm)", () => d.OffsetMm, v => d.OffsetMm = v, -100, 100, "0.#", "+ à esquerda do sentido 1º→2º clique.")
            .Number("Altura do texto (mm)", () => d.TextMm, v => d.TextMm = v, 0.8, 10, "0.#")
            .Check("Cotar linhas pintadas pelo eixo (larguras de faixa eixo a eixo)", () => d.LineAxes, v => d.LineAxes = v)
            .Number("Largura máxima de \"linha\" (m)", () => d.AxisMaxWidth, v => d.AxisMaxWidth = v, 0.05, 2)
            .Check("Incluir os pontos clicados (extremos)", () => d.IncludeEnds, v => d.IncludeEnds = v)
            .Check("Cota total", () => d.Total, v => d.Total = v)
            .Check("Considerar a sinalização horizontal", () => d.Horizontal, v => d.Horizontal = v)
            .Check("Considerar calçadas, meios-fios, canteiros e dispositivos", () => d.Physical, v => d.Physical = v);

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
            .Text("Título (vazio = automático)", () => d.Title, v => d.Title = string.IsNullOrWhiteSpace(v) ? null : v);
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
            .Text("Título", () => d.Title, v => d.Title = v ?? "")
            .Text("Notas", () => d.Text, v => d.Text = v ?? "", multiline: true)
            .Number("Largura do quadro (mm)", () => d.WidthMm, v => d.WidthMm = v, 40, 600, "0")
            .Number("Altura do texto (mm)", () => d.TextMm, v => d.TextMm = v, 0.8, 10, "0.#")
            .Check("Numerar as notas", () => d.Numbered, v => d.Numbered = v);

    public static FormWindow North(NorthArrowDefinition d, double scale, bool edit) =>
        new FormWindow(edit ? "Editar norte" : "Norte", "Indicação de norte", "Depois de confirmar, clique onde o norte deve ficar.",
                d, () => { var c = (NorthArrowDefinition)MarkingDefinition.FromJson(d.ToJson())!; c.Position = Vec2.Zero; return Paper(DetailGenerator.NorthArrow(c, Ctx(scale, Array.Empty<MarkingDefinition>(), null)), scale); },
                false, edit ? "Aplicar" : "Inserir", 760, 480)
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
