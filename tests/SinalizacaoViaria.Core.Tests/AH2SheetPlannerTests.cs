using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using Xunit;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada 8, item 23: pranchas. O eixo é dividido em trechos que cabem na folha escolhida, na escala escolhida, cobrindo o
/// eixo inteiro com a sobreposição pedida; cada vista é girada na direção do trecho; linhas de corte "continua na prancha X".
/// </summary>
public class AH2SheetPlannerTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly SheetFrame A1 = SheetFrame.PlanArea(841, 594, 10, 190);   // 631 × 574 mm de planta
    private const double Overlap = 20, Half = 15;

    private static Polyline2 Straight(double length, double deg = 0) =>
        new(new[] { Vec2.Zero, Vec2.FromAngle(deg * Math.PI / 180) * length });

    /// <summary>Tangente, curva de 150 m de raio (90°) e tangente.</summary>
    private static Polyline2 Curved()
    {
        var pts = new List<Vec2> { new(-400, 0), new(0, 0) };
        for (int i = 1; i <= 90; i++)
        {
            var a = -Math.PI / 2 + i * Math.PI / 180;
            pts.Add(new Vec2(0, 150) + Vec2.FromAngle(a) * 150);
        }
        pts.Add(new Vec2(150, 550));
        return new Polyline2(pts);
    }

    private static void Covers(List<SheetSegment> segs, Polyline2 axis, double overlap)
    {
        Assert.Equal(0, segs[0].Start, 9);
        Assert.Equal(axis.Length, segs[^1].End, 6);
        for (int i = 0; i < segs.Count; i++)
        {
            Assert.Equal(i + 1, segs[i].Index);
            Assert.True(segs[i].Fits, $"trecho {i + 1} não cabe na folha");
            Assert.True(segs[i].End > segs[i].Start);
            if (i == 0) continue;
            Assert.Equal(overlap, segs[i - 1].End - segs[i].Start, 6);                      // sobreposição pedida
            Assert.Equal((segs[i - 1].End + segs[i].Start) / 2, segs[i].PrevMatch!.Value, 9);  // linha de corte no meio
            Assert.Equal(segs[i].PrevMatch, segs[i - 1].NextMatch);
        }
        Assert.Null(segs[0].PrevMatch);
        Assert.Null(segs[^1].NextMatch);
    }

    [Theory]
    [InlineData(250)]
    [InlineData(500)]
    [InlineData(1000)]
    public void Trechos_cobrem_o_eixo_com_a_sobreposicao_e_o_numero_esperado_de_folhas(double scale)
    {
        var axis = Straight(1500, 25);
        var segs = SheetPlanner.Auto(axis, A1, scale, Overlap, Half);
        Covers(segs, axis, Overlap);
        // Folha A1, planta de 631 mm: 157,75 m a 1:250, 315,5 m a 1:500, 631 m a 1:1000.
        var w = A1.WidthMm * scale / 1000;
        var expected = (int)Math.Ceiling((1500 - w) / (w - Overlap)) + 1;
        Assert.Equal(expected, segs.Count);
        Assert.Equal(expected, SheetPlanner.ExpectedCount(1500, w, Overlap));
        Assert.Equal(scale switch { 250 => 11, 500 => 6, _ => 3 }, segs.Count);
        Assert.All(segs.Take(segs.Count - 1), s => Assert.Equal(w, s.End - s.Start, 6));   // cada folha cheia, a última com o resto
    }

    [Theory]
    [InlineData(250)]
    [InlineData(500)]
    [InlineData(1000)]
    public void Eixo_em_curva_tambem_fica_coberto_com_a_sobreposicao(double scale)
    {
        var axis = Curved();
        var segs = SheetPlanner.Auto(axis, A1, scale, Overlap, Half);
        Covers(segs, axis, Overlap);
        // A curva não aumenta o comprimento coberto por folha além da largura da planta.
        Assert.True(segs.Count >= SheetPlanner.ExpectedCount(axis.Length, A1.Width(scale), Overlap));
        // O eixo de cada trecho fica dentro da região de corte (com a faixa lateral).
        foreach (var s in segs)
        {
            var crop = new Polygon2(s.Crop);
            foreach (var p in axis.SubPoints(s.Start, s.End)) Assert.True(crop.Contains(p) || crop.DistanceTo(p) < 1e-6, $"trecho {s.Index}: {p} fora");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(200)]
    [InlineData(-60)]
    public void Angulo_da_vista_igual_a_direcao_do_trecho(double deg)
    {
        var axis = Straight(900, deg);
        foreach (var s in SheetPlanner.Auto(axis, A1, 500, Overlap, Half))
        {
            // Mesma direção do trecho (a menos de meia volta, para o texto ficar legível): entre −90° e 90°.
            var want = Math.Atan2(Math.Sin(deg * Math.PI / 180), Math.Cos(deg * Math.PI / 180));
            var diff = Math.IEEERemainder(s.AngleRad - want, Math.PI);
            Assert.True(Math.Abs(diff) < 1e-9, $"{deg}°: vista a {s.AngleDeg:0.###}°");
            Assert.InRange(s.AngleDeg, -90 + 1e-9, 90 + 1e-9);
            Assert.True(Math.Abs(s.U.Cross(axis.TangentAt((s.Start + s.End) / 2))) < 1e-9);
        }
        // Trechos de direções diferentes (curva em L): cada vista na direção da sua corda.
        var l = new Polyline2(new[] { new Vec2(0, 0), new Vec2(300, 0), new Vec2(300, 300) });
        var segs = SheetPlanner.FromRanges(l, new[] { (0.0, 300.0), (300.0, 600.0) }, A1, 1000, Half);
        Assert.Equal(0, segs[0].AngleDeg, 9);
        Assert.Equal(90, segs[1].AngleDeg, 9);
        // Giro pedido pelo usuário prevalece.
        Assert.Equal(15, SheetPlanner.Segment(l, 1, 0, 300, A1, 1000, Half, 15).AngleDeg, 9);
    }

    [Fact]
    public void Trechos_por_estacas_e_por_cortes_clicados()
    {
        var r = SheetPlanner.ParseRanges("0-200; 9+0,00 a 20+0,00\n390 – 512,5");
        Assert.Equal(new[] { (0.0, 200.0), (180.0, 400.0), (390.0, 512.5) }, r);
        Assert.Equal(205, SheetPlanner.ParseStation("10+5,00"), 9);
        Assert.Equal("10+5,00", SheetPlanner.Station(205));
        Assert.Throws<FormatException>(() => SheetPlanner.ParseRanges("0 200 300"));

        var axis = Straight(1000);
        var cuts = SheetPlanner.RangesFromCuts(new[] { 650.0, 300.0 }, 1000, Overlap);
        var segs = SheetPlanner.FromRanges(axis, cuts, A1, 1000, Half);
        Covers(segs, axis, Overlap);
        Assert.Equal(300, segs[0].NextMatch!.Value, 9);   // a linha de corte fica onde o usuário clicou
        Assert.Equal(650, segs[1].NextMatch!.Value, 9);
        // Trecho que não cabe na escala pedida é sinalizado.
        Assert.False(SheetPlanner.Segment(axis, 1, 0, 1000, A1, 500, Half).Fits);
        Assert.Throws<InvalidOperationException>(() => SheetPlanner.Auto(axis, A1, 500, 400, Half));   // sobreposição > folha
    }

    private static (List<MarkingDefinition> Defs, BuildContext Ctx, List<SheetSegmentDefinition> Set) SheetSet()
    {
        var axis = Straight(1500, 25);
        var pav = new RoadPavementDefinition { PathRef = PathReference.FromPoints(axis.Points, 0) };
        var set = SheetPlanner.Auto(axis, A1, 500, Overlap, Half).Select(s => new SheetSegmentDefinition
        {
            RoadId = pav.Id, SetId = "s1", Index = s.Index, SheetNumber = $"P{s.Index:00}", Start = s.Start, End = s.End, Scale = 500,
            HalfWidth = Half, FrameWidthMm = A1.WidthMm, FrameHeightMm = A1.HeightMm,
        }).ToList();
        SheetSegmentDefinition.Link(set);
        var defs = new List<MarkingDefinition> { pav };
        defs.AddRange(set);
        var ctx = new BuildContext
        {
            Catalog = Cat, ViewScale = 500, Lookup = id => defs.FirstOrDefault(d => d.Id == id), AllDefinitions = () => defs,
            PathOf = d => d.Path is { Points.Count: >= 2 } p ? new Polyline2(p.Points) : null,
        };
        return (defs, ctx, set);
    }

    [Fact]
    public void Linhas_de_corte_continua_na_prancha_X_e_titulo_do_trecho()
    {
        var (_, ctx, set) = SheetSet();
        Assert.Equal(6, set.Count);
        Assert.Equal("P02", set[0].NextSheet);
        Assert.Null(set[0].PrevSheet);
        Assert.Equal("P01", set[1].PrevSheet);
        var mid = MarkingBuilder.Build(set[1], null, ctx);
        var texts = mid.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("CONTINUA NA PRANCHA P01"));
        Assert.Contains(texts, t => t.StartsWith("CONTINUA NA PRANCHA P03"));
        Assert.Contains(texts, t => t.StartsWith("PRANCHA P02 – TRECHO 2"));
        // A linha de corte atravessa o eixo na estaca da sobreposição, perpendicular a ele.
        var axis = new Polyline2(((RoadPavementDefinition)ctx.Lookup!(set[1].RoadId)!).PathRef.Points);
        var dashes = mid.Annotations.OfType<AnnotationLine>().ToList();
        Assert.Contains(dashes, l => Math.Abs(axis.Project(l.Points[0]).Station - set[1].PrevMatch!.Value) < 1e-6);
        Assert.Contains(dashes, l => Math.Abs(axis.Project(l.Points[0]).Station - set[1].NextMatch!.Value) < 1e-6);
        // Primeira e última: uma linha de corte só.
        var first = MarkingBuilder.Build(set[0], null, ctx).Annotations.OfType<AnnotationText>().Count(t => t.Text.StartsWith("CONTINUA"));
        var last = MarkingBuilder.Build(set[^1], null, ctx).Annotations.OfType<AnnotationText>().Count(t => t.Text.StartsWith("CONTINUA"));
        Assert.Equal(1, first);
        Assert.Equal(1, last);
        // Re-editar um trecho (estacas, escala, giro) e religar o conjunto atualiza as vizinhas.
        set[1].End += 50;
        set[1].Scale = 1000;
        set[1].RotationDeg = 0;
        SheetSegmentDefinition.Link(set);
        Assert.Equal((set[1].End + set[2].Start) / 2, set[2].PrevMatch!.Value, 9);
        Assert.Equal(0, set[1].Segment(axis).AngleDeg, 9);
        Assert.Equal(A1.Width(1000), set[1].Segment(axis).Width, 9);
    }

    [Fact]
    public void Legenda_e_quadro_da_prancha_so_com_o_que_esta_no_trecho()
    {
        var (defs, baseCtx, set) = SheetSet();
        var axis = new Polyline2(((RoadPavementDefinition)baseCtx.Lookup!(set[0].RoadId)!).PathRef.Points);
        var inFirst = new SignDefinition { Code = "R-1", Position = axis.PointAt(100) + axis.TangentAt(100).PerpLeft * 8 };
        var inThird = new SignDefinition { Code = "A-18", Position = axis.PointAt(700) + axis.TangentAt(700).PerpLeft * 8 };
        var line = new LinearMarkingDefinition { Code = "LFO-2", PathRef = PathReference.FromPoints(axis.Points, 0) };
        defs.AddRange(new MarkingDefinition[] { inFirst, inThird, line });
        BuildContext? ctx = null;
        ctx = new BuildContext
        {
            Catalog = Cat, ViewScale = 100, Lookup = baseCtx.Lookup, AllDefinitions = baseCtx.AllDefinitions, PathOf = baseCtx.PathOf,
            GeometryOf = d => d is IAnnotationDefinition ? null : MarkingBuilder.Build(d, d.Path is { } p ? new Polyline2(p.Points) : null, ctx!),
        };
        var lg = new LegendDefinition { SheetSegmentId = set[0].Id };
        var texts = MarkingBuilder.Build(lg, null, ctx).Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("R-1"));
        Assert.DoesNotContain(texts, t => t.StartsWith("A-18"));
        var all = MarkingBuilder.Build(new LegendDefinition(), null, ctx).Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains(all, t => t.StartsWith("A-18"));

        // Quadro: a LFO-2 da folha 1 tem a extensão do trecho (recortada pela região), não a do eixo inteiro.
        var full = MarkingBuilder.Build(line, axis, ctx);
        var region = new Polygon2(set[0].Segment(axis).Crop);
        var clipped = DetailGenerator.ClipToRegion(full, region);
        Assert.Equal(full.PaintedLength * clipped.TotalArea / full.TotalArea, clipped.PaintedLength, 6);
        Assert.True(clipped.TotalArea < full.TotalArea * 0.3);
        var qt = MarkingBuilder.Build(new QuantityTableDefinition { SheetSegmentId = set[0].Id }, null, ctx).Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("R-1", qt);
        Assert.DoesNotContain("A-18", qt);
    }
}
