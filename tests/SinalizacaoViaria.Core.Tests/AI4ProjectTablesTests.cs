using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AI, item 4: legenda de placas, quadro de placas e quadros de quantidades gerados sem vista (sem ViewScale nem
/// ViewId) têm o mesmo conteúdo e as mesmas quantidades que os desenhados numa planta; a vista própria vai para qualquer folha.
/// </summary>
public class AI4ProjectTablesTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private sealed record Scene(List<MarkingDefinition> Defs, Dictionary<string, Polyline2> Paths);

    private static Scene Project()
    {
        CustomSigns.Apply(Cat, new[] { CustomSigns.Create("PP-1", "Rua sem saída", FormaPlaca.Retangulo, 1.20, 0.40,
            MarkingColor.Azul, MarkingColor.Branca, "RUA SEM SAÍDA", MarkingColor.Branca) });
        var defs = new List<MarkingDefinition>
        {
            new SignDefinition { Code = "R-1", Position = new Vec2(3, -4) },
            new SignDefinition { Code = "R-19", Position = new Vec2(40, 4), Legend = "40" },
            new SignDefinition { Code = "A-32b", Position = new Vec2(80, 4) },
            new SignDefinition { Code = "R-1", Position = new Vec2(120, -4), Stack = new List<StackedSign> { new() { Code = "PP-1" } } },
        };
        var paths = new Dictionary<string, Polyline2>();
        void Line(string code, double y, double len)
        {
            var d = new LinearMarkingDefinition { Code = code };
            defs.Add(d);
            paths[d.Id] = new Polyline2(new[] { new Vec2(0, y), new Vec2(len, y) });
        }
        Line("LFO-1", 0, 150);
        Line("LBO", 3.4, 150);
        Line("LBO", -3.4, 150);
        return new Scene(defs, paths);
    }

    private static BuildContext Context(Scene s, double? viewScale)
    {
        MarkingGeometry? Geo(MarkingDefinition d) =>
            d is IAnnotationDefinition ? null : MarkingBuilder.Build(d, s.Paths.GetValueOrDefault(d.Id), new BuildContext { Catalog = Cat });
        var all = s.Defs;
        return viewScale is { } vs
            ? new BuildContext { Catalog = Cat, ViewScale = vs, AllDefinitions = () => all, GeometryOf = Geo, Lookup = id => all.FirstOrDefault(x => x.Id == id) }
            : new BuildContext { Catalog = Cat, AllDefinitions = () => all, GeometryOf = Geo, Lookup = id => all.FirstOrDefault(x => x.Id == id) };
    }

    private static List<(string Text, double Mm, TextAlign Align)> Texts(MarkingGeometry g) =>
        g.Annotations.OfType<AnnotationText>().Select(t => (t.Text, t.PaperHeightMm, t.Align)).ToList();

    /// <summary>Geometria em mm de papel a partir do canto do quadro (independente da escala e do ponto de inserção).</summary>
    private static List<Vec2> Paper(MarkingGeometry g, Vec2 corner, double scale) =>
        g.Annotations.OfType<AnnotationLine>().SelectMany(l => l.Points)
            .Concat(g.Annotations.OfType<AnnotationText>().Select(t => t.Position))
            .Concat(g.Pieces.SelectMany(p => p.Shape.Outer))
            .Select(v => (v - corner) * (1000 / scale)).ToList();

    private static void SameDrawing(MarkingGeometry inView, Vec2 corner, double scale, MarkingGeometry own)
    {
        Assert.Equal(Texts(inView), Texts(own));
        Assert.Equal(inView.UnitCount, own.UnitCount);
        Assert.Equal(inView.Pieces.Count, own.Pieces.Count);
        var a = Paper(inView, corner, scale);
        var b = Paper(own, Vec2.Zero, 100);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) Assert.True(a[i].DistanceTo(b[i]) < 1e-6, $"ponto {i}: {a[i]} × {b[i]}");
    }

    private static T InPlan<T>(T d, Vec2 corner) where T : MarkingDefinition
    {
        d.Output.Mode = OutputMode.Detalhe2D;
        d.Output.ViewId = "planta-terreo-1:250";
        switch (d)
        {
            case LegendDefinition lg: lg.Position = corner; break;
            case QuantityTableDefinition qt: qt.Position = corner; break;
        }
        return d;
    }

    [Fact]
    public void Legenda_sem_vista_tem_o_mesmo_conteudo_da_legenda_na_planta()
    {
        var s = Project();
        var corner = new Vec2(512.3, -88.7);
        var inPlan = InPlan(new LegendDefinition(), corner);
        var geoPlan = MarkingBuilder.Build(inPlan, null, Context(s, 250));

        var own = ProjectTableViews.ForOwnView(inPlan);
        Assert.Null(own.Output.ViewId);
        Assert.Equal(Vec2.Zero, own.Position);
        var geoOwn = MarkingBuilder.Build(own, null, Context(s, null));

        SameDrawing(geoPlan, corner, 250, geoOwn);
        // Uma linha por placa diferente, as empilhadas também.
        Assert.Equal(4, geoOwn.UnitCount);
        foreach (var code in new[] { "R-1", "R-19", "A-32b", "PP-1" })
            Assert.Contains(Texts(geoOwn), t => t.Text.StartsWith(code + " – "));
    }

    [Fact]
    public void Quadro_de_placas_sem_vista_tem_as_mesmas_placas_e_quantidades()
    {
        var s = Project();
        var corner = new Vec2(-40, 260);
        var inPlan = InPlan(new QuantityTableDefinition { SignsOnly = true, RowMm = 8 }, corner);
        var geoPlan = MarkingBuilder.Build(inPlan, null, Context(s, 500));
        var geoOwn = MarkingBuilder.Build(ProjectTableViews.ForOwnView(inPlan), null, Context(s, null));
        SameDrawing(geoPlan, corner, 500, geoOwn);

        // Código seguido da descrição, das dimensões e da quantidade: R-1 aparece 2 vezes (uma delas com a PP-1 embaixo).
        var cells = Texts(geoOwn).Select(t => t.Text).ToList();
        int Qty(string code) => int.Parse(cells[cells.IndexOf(code) + 3]);
        Assert.Equal(2, Qty("R-1"));
        Assert.Equal(1, Qty("R-19"));
        Assert.Equal(1, Qty("A-32b"));
        Assert.Equal(1, Qty("PP-1"));
    }

    [Fact]
    public void Quadro_de_quantitativos_sem_vista_tem_as_mesmas_quantidades_do_calculo()
    {
        var s = Project();
        var corner = new Vec2(10, 10);
        var inPlan = InPlan(new QuantityTableDefinition(), corner);
        var ctxPlan = Context(s, 200);
        var geoPlan = MarkingBuilder.Build(inPlan, null, ctxPlan);
        var ctxOwn = Context(s, null);
        var geoOwn = MarkingBuilder.Build(ProjectTableViews.ForOwnView(inPlan), null, ctxOwn);
        SameDrawing(geoPlan, corner, 200, geoOwn);

        // As quantidades do quadro são as do cálculo de quantitativos (o mesmo da janela e do memorial).
        var items = s.Defs.Select(d => (d, ctxOwn.GeometryOf!(d)!)).ToList();
        var rows = QuantityCalculator.Compute(items, Cat);
        var cells = Texts(geoOwn).Select(t => t.Text).ToList();
        foreach (var g in rows.GroupBy(r => r.Code))
        {
            var first = g.First();
            var q = first.Unit switch { "m²" => g.Sum(r => r.Area), "m" => g.Sum(r => r.PaintedLength), _ => g.Sum(r => r.Units) };
            var i = cells.IndexOf(first.Code);
            Assert.True(i >= 0, first.Code + " fora do quadro");
            Assert.Equal(first.Unit, cells[i + 2]);
            var expected = first.Unit is "m²" or "m" ? q.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) : q.ToString("0");
            Assert.Equal(expected, cells[i + 3]);
        }
        Assert.Contains(rows, r => r.Code == "LBO" && r.PaintedLength > 299);
        Assert.Contains(rows, r => r.Code == "PP-1" && r.Units == 1);

        // Quadro de uma categoria: o nome da vista própria diz qual é.
        var cat = rows.First().Category;
        Assert.Contains(QuantityRow.CategoryLabel(cat), ProjectTableViews.ViewName(new QuantityTableDefinition { Category = cat.ToString() }));
    }

    [Fact]
    public void Vista_propria_nao_herda_o_trecho_de_prancha_e_e_salva_igual()
    {
        var lg = new LegendDefinition { SheetSegmentId = "trecho-1", Position = new Vec2(3, 4), DrawnAnchor = new Vec2(3, 2) };
        lg.Output.ViewId = "x";
        var own = ProjectTableViews.ForOwnView(lg);
        Assert.Null(own.SheetSegmentId);
        Assert.Null(own.DrawnAnchor);
        Assert.NotEqual(lg.Id, own.Id);
        // Nenhum campo novo no formato salvo: a cópia é lida de volta igual.
        var back = (LegendDefinition)MarkingDefinition.FromJson(own.ToJson())!;
        Assert.Equal(own.ToJson(), back.ToJson());
        Assert.Equal("SV - Legenda de placas", ProjectTableViews.ViewName(lg));
        Assert.Equal("SV - Quadro de placas", ProjectTableViews.ViewName(new QuantityTableDefinition { SignsOnly = true }));
    }

    [Fact]
    public void Lugar_na_folha_no_alto_a_direita_sem_cobrir_o_que_ja_esta_la()
    {
        // Folha A1 (841 × 594 mm) com margens de 10 mm; planta à esquerda e o carimbo embaixo à direita.
        var min = new Vec2(0.010, 0.010);
        var max = new Vec2(0.831, 0.584);
        var plan = (new Vec2(0.010, 0.010), new Vec2(0.600, 0.584));
        var stamp = (new Vec2(0.651, 0.010), new Vec2(0.831, 0.090));
        var size = new Vec2(0.180, 0.150);
        var at = ProjectTableViews.SheetSpot(min, max, new[] { plan, stamp }, size);
        Assert.NotNull(at);
        Assert.Equal(max.X - size.X, at!.Value.X, 9);
        Assert.Equal(max.Y - size.Y, at.Value.Y, 9);

        // Um segundo quadro vai para baixo do primeiro, sem sobrepor nada.
        var first = (at.Value, at.Value + size);
        var at2 = ProjectTableViews.SheetSpot(min, max, new[] { plan, stamp, first }, new Vec2(0.200, 0.200));
        Assert.NotNull(at2);
        var box2 = (Min: at2!.Value, Max: at2.Value + new Vec2(0.200, 0.200));
        foreach (var (a, b) in new[] { plan, stamp, first })
            Assert.True(box2.Max.X <= a.X + 1e-9 || box2.Min.X >= b.X - 1e-9 || box2.Max.Y <= a.Y + 1e-9 || box2.Min.Y >= b.Y - 1e-9);
        Assert.True(box2.Min.X >= min.X - 1e-9 && box2.Max.X <= max.X + 1e-9 && box2.Min.Y >= min.Y - 1e-9 && box2.Max.Y <= max.Y + 1e-9);

        // Maior que o espaço livre: nulo (o comando avisa e coloca no canto, para o usuário arrastar).
        Assert.Null(ProjectTableViews.SheetSpot(min, max, new[] { plan, stamp }, new Vec2(0.300, 0.600)));
    }
}
