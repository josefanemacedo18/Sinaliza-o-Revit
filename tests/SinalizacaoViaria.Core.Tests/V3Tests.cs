using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

public class V3Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    // ------------------------------------------------------------------ placas

    [Fact]
    public void Catalog_HasCompleteSignSeries()
    {
        foreach (var code in new[] { "R-1", "R-3", "R-6c", "R-9", "R-15", "R-19", "R-24a", "R-25d", "R-33", "R-36b", "R-40",
                     "A-1a", "A-5b", "A-12", "A-21e", "A-26b", "A-33b", "A-41", "A-42c", "A-48", "SAU-HOSP", "OBR-DESVIO", "INF-COMP", "IND-SERV" })
            Assert.NotNull(Cat.Placa(code));
        Assert.True(Cat.Placas.Count(p => p.Codigo.StartsWith("R-")) >= 45);
        Assert.True(Cat.Placas.Count(p => p.Codigo.StartsWith("A-")) >= 65);
        Assert.Equal(Cat.Placas.Count, Cat.Placas.Select(p => p.Codigo).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Cat.Placas, p => Assert.False(string.IsNullOrWhiteSpace(p.Descricao)));
    }

    [Fact]
    public void AllSigns_RenderFaceWithPictogramInsidePlate()
    {
        var glyphs = new BlockFontProvider();
        foreach (var p in Cat.Placas)
        {
            var face = DetailGenerator.FlatFace(p, p.Largura, p.Altura, null, glyphs);
            Assert.True(face.Count >= 1, p.Codigo);
            var outline = SignGenerator.Outline(p.Forma, p.Largura, p.Altura, 0);
            var (omn, omx) = outline.Bounds;
            foreach (var (s, _) in face)
            {
                var (mn, mx) = s.Bounds;
                Assert.True(mn.X >= omn.X - 1e-3 && mx.X <= omx.X + 1e-3 && mn.Y >= omn.Y - 1e-3 && mx.Y <= omx.Y + 1e-3, p.Codigo);
            }
            if (p.Pictograma is { Count: > 0 })
                Assert.True(face.Select(f => f.Color).Distinct().Count() >= 2, p.Codigo + " sem pictograma visível");
        }
    }

    [Fact]
    public void ProhibitionSign_HasRedBarOverPictogram()
    {
        var p = Cat.Placa("R-12")!;
        Assert.True(p.Proibicao);
        var face = SignGenerator.Face(p, 0.5, 0.5, 0, null, new BlockFontProvider());
        var top = face.Max(f => f.Layer);
        Assert.Contains(face, f => f.Layer == top && f.Color == MarkingColor.Vermelha);
        Assert.Contains(face, f => f.Color == MarkingColor.Preta);
    }

    [Fact]
    public void EditableLegend_ReplacesPictogramText()
    {
        var p = Cat.Placa("R-15")!;
        var a = DetailGenerator.FlatFace(p, 0.5, 0.5, "3,0 m", new BlockFontProvider()).Sum(f => f.Shape.Area);
        var b = DetailGenerator.FlatFace(p, 0.5, 0.5, "4,50 m", new BlockFontProvider()).Sum(f => f.Shape.Area);
        Assert.Equal(a, b, 3);   // mesma placa, só muda o texto
        Assert.All(Silhouettes.Names, n => Assert.NotEmpty(Silhouettes.Get(n)));
    }

    [Fact]
    public void SaintAndrewCross_IsSinglePolygon()
    {
        var o = SignGenerator.Outline(FormaPlaca.CruzSantoAndre, 1.2, 0.9, 0);
        Assert.True(o.Area > 0.1 && o.Area < 1.2 * 0.9);
    }

    // ------------------------------------------------------------------ calçadas

    [Fact]
    public void CurbExtension_ReverseCurvesAndDepth()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(12, 0) });
        var d = new CurbExtensionDefinition { Depth = 2.2, Radius = 1.5, Planter = true, Trees = 1 };
        var geo = MarkingBuilder.Build(d, path, Ctx);
        Assert.Empty(geo.Warnings);
        var fp = SidewalkGenerator.EarFootprint(d, path)!;
        var (mn, mx) = fp.Bounds;
        Assert.Equal(-2.2, mn.Y, 2);           // calçada à esquerda → avanço para a direita (−Y)
        Assert.Equal(0, mx.Y, 2);
        Assert.Equal(12, mx.X - mn.X, 2);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Grama);
        Assert.All(geo.Pieces.Where(p => p.Solid == null && p.Profile == null && p.Color == MarkingColor.Concreto && p.Elevation == 0), p => Assert.Equal(0.15, p.Thickness, 3));
        // Área próxima de L×D menos as transições (curvas reversas).
        Assert.InRange(fp.Area, 12 * 2.2 - 2 * 2.2 * 2.6, 12 * 2.2);

        var shortEar = MarkingBuilder.Build(new CurbExtensionDefinition { Radius = 5 }, new Polyline2(new[] { Vec2.Zero, new Vec2(6, 0) }), Ctx);
        Assert.Contains(shortEar.Warnings, w => w.Contains("reduzido"));
    }

    [Fact]
    public void SidewalkArea_RoundsCornersAndCurb()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(6, 0), new Vec2(6, 4), new Vec2(0, 4) }, true);
        var sharp = SidewalkGenerator.AreaOutline(new SidewalkAreaDefinition(), path)!;
        var round = SidewalkGenerator.AreaOutline(new SidewalkAreaDefinition { FilletRadius = 1 }, path)!;
        Assert.Equal(24, sharp.Area, 3);
        Assert.Equal(24 - 4 * (1 - Math.PI / 4), round.Area, 1);
        var geo = MarkingBuilder.Build(new SidewalkAreaDefinition { Type = TipoAreaCalcada.Canteiro }, path, Ctx);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Grama);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Concreto);
        Assert.Equal(24, geo.TotalArea, 2);
        var bike = MarkingBuilder.Build(new SidewalkAreaDefinition { Type = TipoAreaCalcada.Ciclovia, Height = 0.15 }, path, Ctx);
        Assert.All(bike.Pieces, p => { Assert.Equal(MarkingColor.Vermelha, p.Color); Assert.Equal(0.15, p.Elevation, 6); });
    }

    [Fact]
    public void Planters_DistributedAlongLine()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(20, 0) });
        var d = new PlanterDefinition { Length = 2, Width = 1, Spacing = 6, Trees = true };
        var shapes = SidewalkGenerator.PlanterShapes(d, path);
        Assert.Equal(4, shapes.Count);                       // (20 − 2) / 6 + 1
        var geo = MarkingBuilder.Build(d, path, Ctx);
        Assert.Equal(4, geo.UnitCount);
        Assert.Contains(geo.Pieces, p => p.Elevation >= 0.15 - 1e-6 && p.Color != MarkingColor.Concreto);   // árvores sobre o canteiro
        var cont = SidewalkGenerator.PlanterShapes(new PlanterDefinition { Spacing = 0, Width = 1 }, path);
        Assert.Equal(20, PolygonOps.TotalArea(cont), 1);
        var raised = MarkingBuilder.Build(new PlanterDefinition { Type = TipoCanteiroCalcada.Jardineira, Trees = false }, path, Ctx);
        Assert.Contains(raised.Pieces, p => p.Thickness > 0.5);   // mureta 0,15 + 0,40
    }

    [Theory]
    [InlineData(TipoCulDeSac.Circular)]
    [InlineData(TipoCulDeSac.ExcentricoEsquerda)]
    [InlineData(TipoCulDeSac.ExcentricoDireita)]
    [InlineData(TipoCulDeSac.Martelo)]
    [InlineData(TipoCulDeSac.EmY)]
    [InlineData(TipoCulDeSac.EmLEsquerda)]
    [InlineData(TipoCulDeSac.EmLDireita)]
    [InlineData(TipoCulDeSac.Gota)]
    public void CulDeSac_AllTypesBuildOpenEndedPavement(TipoCulDeSac type)
    {
        var path = new Polyline2(new[] { new Vec2(100, 50), new Vec2(100, 70) });
        var d = new CulDeSacDefinition { Type = type };
        var geo = MarkingBuilder.Build(d, path, Ctx);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Asfalto);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Concreto);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Branca);
        // Nada antes do início (a via continua pelo eixo).
        Assert.All(geo.Pieces, p => Assert.True(p.Shape.Bounds.Min.Y >= 50 - 1e-3));
        // A pista de entrada (largura 7 m) chega ao início sem calçada atravessada.
        var pavement = geo.Pieces.Where(p => p.Color == MarkingColor.Asfalto).Select(p => p.Shape).ToList();
        Assert.Contains(pavement, s => s.Contains(new Vec2(100, 50.2)));
        Assert.Contains(pavement, s => s.Contains(new Vec2(103.3, 50.2)));
    }

    [Fact]
    public void CulDeSac_IslandAndTurningWarning()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(0, 20) });
        var geo = MarkingBuilder.Build(new CulDeSacDefinition { Island = true, IslandRadius = 3, BulbRadius = 12 }, path, Ctx);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Grama && p.Shape.Contains(new Vec2(0, 20)));
        Assert.DoesNotContain(geo.Pieces, p => p.Color == MarkingColor.Asfalto && p.Shape.Contains(new Vec2(0, 20)));
        var small = MarkingBuilder.Build(new CulDeSacDefinition { BulbRadius = 7 }, path, Ctx);
        Assert.Contains(small.Warnings, w => w.Contains("9–12 m"));
    }

    [Fact]
    public void Exclusion_CutsParkingUnderCurbExtension()
    {
        var curb = new Polyline2(new[] { Vec2.Zero, new Vec2(30, 0) });
        var ear = new CurbExtensionDefinition();
        var fp = SidewalkGenerator.EarFootprint(ear, new Polyline2(new[] { new Vec2(10, 0), new Vec2(20, 0) }))!;
        var lbo = new LinearMarkingDefinition { Code = "LBO", Offset = -2.2 };
        var full = MarkingBuilder.Build(lbo, curb, Ctx).TotalArea;
        lbo.Exclusions.Add(new ExclusionZone { SourceId = ear.Id, Points = fp.Outer.ToList() });
        Assert.True(MarkingBuilder.Build(lbo, curb, Ctx).TotalArea < full);
    }

    // ------------------------------------------------------------------ quantitativos

    [Fact]
    public void Quantities_SeparatedByCategory()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(20, 0) });
        var items = new List<(MarkingDefinition, MarkingGeometry)>();
        void Add(MarkingDefinition d, Polyline2? p) => items.Add((d, MarkingBuilder.Build(d, p, Ctx)));
        Add(new LinearMarkingDefinition { Code = "LFO-1" }, path);
        Add(new SignDefinition { Code = "R-1" }, null);
        Add(new DeviceMarkingDefinition { Code = "BAL-FLEX" }, path);
        Add(new RampDefinition(), new Polyline2(new[] { Vec2.Zero, new Vec2(0, 1) }));
        Add(new CurbExtensionDefinition(), path);
        Add(new TrafficCalmingDefinition(), new Polyline2(new[] { Vec2.Zero, new Vec2(0, 7) }));
        Add(new UrbanElementDefinition { Code = "BANCO" }, null);
        var rows = QuantityCalculator.Compute(items, Cat);
        Assert.Equal(7, rows.Select(r => r.Category).Distinct().Count());
        Assert.True(rows.Select(r => (int)r.Category).SequenceEqual(rows.Select(r => (int)r.Category).OrderBy(c => c)));
        Assert.Equal(CategoriaQuantitativo.Acessibilidade, rows.First(r => r.Code.StartsWith("RAMPA")).Category);
        var csv = QuantityCalculator.ToCsv(rows, QuantityCalculator.Summary(rows));
        Assert.Contains("1. SINALIZAÇÃO HORIZONTAL", csv);
        Assert.Contains("SUBTOTAL", csv);
        Assert.Contains("RESUMO POR CATEGORIA", csv);
        Assert.DoesNotContain(QuantityCalculator.Summary(rows), r => r.Color == MarkingColor.Concreto);
        Assert.Equal(20, rows.First(r => r.Code == "LFO-1").MainQuantity, 1);
    }

    // ------------------------------------------------------------------ detalhamento

    private static BuildContext DetailCtx(List<MarkingDefinition> defs, Dictionary<string, Polyline2?> paths, double scale = 100) => new()
    {
        Catalog = Cat,
        ViewScale = scale,
        Lookup = id => defs.FirstOrDefault(d => d.Id == id),
        AllDefinitions = () => defs,
        GeometryOf = d => d is IAnnotationDefinition ? null : MarkingBuilder.Build(d, paths.GetValueOrDefault(d.Id), new BuildContext { Catalog = Cat }),
    };

    [Fact]
    public void SectionDimensions_MeasureLanesAxisToAxis()
    {
        // Via de 7 m: LBO nos dois bordos e LFO-1 no eixo; seção de −1 a 8 m.
        var axis = new Polyline2(new[] { new Vec2(0, 3.5), new Vec2(40, 3.5) });
        var edge0 = new Polyline2(new[] { new Vec2(0, 0), new Vec2(40, 0) });
        var edge1 = new Polyline2(new[] { new Vec2(0, 7), new Vec2(40, 7) });
        var d0 = new LinearMarkingDefinition { Code = "LBO" };
        var d1 = new LinearMarkingDefinition { Code = "LFO-1" };
        var d2 = new LinearMarkingDefinition { Code = "LBO" };
        var sec = new SectionDimensionDefinition { Start = new Vec2(20, -1), End = new Vec2(20, 8), IncludeEnds = false };
        var defs = new List<MarkingDefinition> { d0, d1, d2, sec };
        var ctx = DetailCtx(defs, new() { [d0.Id] = edge0, [d1.Id] = axis, [d2.Id] = edge1 });
        var st = DetailGenerator.SectionStations(sec, new[] { edge0, axis, edge1 }.Zip(new[] { d0, d1, d2 }, (p, d) => MarkingBuilder.Build(d, p, Ctx)));
        Assert.Equal(3, st.Count);
        Assert.Equal(3.5, st[1] - st[0], 2);
        Assert.Equal(3.5, st[2] - st[1], 2);
        var geo = MarkingBuilder.Build(sec, null, ctx);
        var texts = geo.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Equal(2, texts.Count(t => t == "3,50"));
        Assert.Contains("7,00", texts);
        Assert.All(geo.Annotations.OfType<AnnotationText>(), t => Assert.Equal(Math.PI / 2, Math.Abs(t.Rotation), 3));
    }

    [Fact]
    public void TypicalDetail_LinearDashAndSignElevation()
    {
        var line = new LinearMarkingDefinition { Code = "LFO-2" };
        var sign = new SignDefinition { Code = "R-19", MountHeight = 2.1 };
        var defs = new List<MarkingDefinition> { line, sign };
        var ctx = DetailCtx(defs, new());
        var td = new TypicalDetailDefinition { MarkingTargetId = line.Id, DetailScale = 25 };
        var geo = MarkingBuilder.Build(td, null, ctx);
        var texts = geo.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("DETALHE TÍPICO – LFO-2"));
        Assert.Contains(texts, t => t.Contains("Cadência"));
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Asfalto);

        var se = MarkingBuilder.Build(new TypicalDetailDefinition { MarkingTargetId = sign.Id }, null, ctx);
        var st = se.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("2,10", st);           // altura livre
        Assert.Contains("0,50", st);           // diâmetro
        Assert.Contains(se.Pieces, p => p.Color == MarkingColor.Metal);
    }

    [Fact]
    public void Tables_SignsAndQuantities()
    {
        var s1 = new SignDefinition { Code = "R-1" };
        var s2 = new SignDefinition { Code = "R-1" };
        var s3 = new SignDefinition { Code = "A-18" };
        var det = new SignPlanDetailDefinition { SignId = s1.Id, Number = "P01" };
        var line = new LinearMarkingDefinition { Code = "LFO-1" };
        var defs = new List<MarkingDefinition> { s1, s2, s3, det, line };
        var ctx = DetailCtx(defs, new() { [line.Id] = new Polyline2(new[] { Vec2.Zero, new Vec2(50, 0) }) });
        var signs = MarkingBuilder.Build(new QuantityTableDefinition { SignsOnly = true }, null, ctx);
        Assert.Equal(2, signs.UnitCount);
        var texts = signs.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("P01", texts);
        Assert.Contains("2", texts);
        Assert.Contains(signs.Pieces, p => p.Color == MarkingColor.Vermelha);   // símbolo da R-1
        var qty = MarkingBuilder.Build(new QuantityTableDefinition(), null, ctx);
        var qt = qty.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("1. SINALIZAÇÃO HORIZONTAL", qt);
        Assert.Contains("2. SINALIZAÇÃO VERTICAL", qt);
        Assert.Contains("50,00", qt);
        Assert.Contains(qty.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith("Parada obrigatória"));
        var only = MarkingBuilder.Build(new QuantityTableDefinition { Category = nameof(CategoriaQuantitativo.SinalizacaoVertical) }, null, ctx);
        Assert.DoesNotContain("1. SINALIZAÇÃO HORIZONTAL", only.Annotations.OfType<AnnotationText>().Select(t => t.Text));
    }

    [Fact]
    public void Notes_WrapAndNumber_NorthArrow()
    {
        var lines = DetailGenerator.Wrap("uma frase bem longa que precisa ser quebrada em várias linhas curtas", 20);
        Assert.True(lines.Count >= 3);
        Assert.All(lines, l => Assert.True(l.Length <= 20));
        var geo = MarkingBuilder.Build(new NotesDefinition(), null, new BuildContext { Catalog = Cat });
        Assert.Contains(geo.Annotations.OfType<AnnotationText>(), t => t.Text == "7.");
        var n = MarkingBuilder.Build(new NorthArrowDefinition { Position = new Vec2(5, 5) }, null, new BuildContext { Catalog = Cat });
        Assert.Contains(n.Annotations.OfType<AnnotationText>(), t => t.Text == "N" && t.Position.Y > 5);
        Assert.Single(n.Pieces);
        var round = (NotesDefinition)MarkingDefinition.FromJson(new NotesDefinition { Title = "X" }.ToJson())!;
        Assert.Equal("X", round.Title);
    }
}
