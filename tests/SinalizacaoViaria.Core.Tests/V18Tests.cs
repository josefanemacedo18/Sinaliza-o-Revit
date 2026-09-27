using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada G: níveis por elemento da seção, perfil transversal na Cotar Seção e via férrea.</summary>
public class V18Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static Polyline2 Straight(double len) => new(new[] { new Vec2(0, 0), new Vec2(len, 0) });

    private static RoadSetup Setup(params ElementoSecao[] right)
    {
        var s = new RoadSetup { TwoWay = false, EdgeLines = false, Inscriptions = true };
        s.Right.AddRange(right);
        s.Left.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 });
        return s;
    }

    [Fact]
    public void DefaultSection_WritesNoLevels()
    {
        var s = Setup(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 }, new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = 3 });
        var defs = s.Build(new PathReference(), new OutputSettings(), Cat);
        Assert.All(defs.OfType<LinearMarkingDefinition>(), l => Assert.Null(l.Height));
        Assert.DoesNotContain(defs.OfType<LinearMarkingDefinition>(), l => l.Code == "PLATAFORMA");
        Assert.Single(defs.OfType<LinearMarkingDefinition>(), l => l.Code == "MEIO-FIO");
    }

    [Fact]
    public void RaisedBikeLane_AtSidewalkLevel_BecomesPlatformWithCurbOnlyAtTheStep()
    {
        var s = Setup(
            new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 },
            new ElementoSecao { Tipo = TipoElementoSecao.Ciclofaixa, Largura = 2.0, Altura = 0.15, Espacamento = 20 },
            new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = 3 });
        var defs = s.Build(new PathReference(), new OutputSettings(), Cat);
        var lin = defs.OfType<LinearMarkingDefinition>().ToList();
        var plat = Assert.Single(lin, l => l.Code == "PLATAFORMA");
        Assert.Equal(MarkingColor.Vermelha, plat.ColorOverride);
        // Um único meio-fio: entre a pista (0) e a ciclovia (+0,15); nenhum entre a ciclovia e a calçada, no mesmo nível.
        var curb = Assert.Single(lin, l => l.Code == "MEIO-FIO");
        Assert.InRange(Math.Abs(curb.Offset), 3.5, 3.5 + RoadSetup.CurbWidth);
        Assert.DoesNotContain(lin, l => l.Code == "SARJETA");
        Assert.DoesNotContain(lin, l => l.Code == "CIC-LD");
        // Pintura e símbolos da ciclovia assentados sobre a laje.
        Assert.All(defs.OfType<RepeatedMarkingDefinition>(), r => Assert.InRange(r.Output.ElevationOffset, 0.15, 0.16));
        var fd = lin.Single(l => l.Code == "CIC-FD");
        Assert.InRange(fd.Output.ElevationOffset, 0.15, 0.16);
        // A laje ocupa a ciclovia sem sobrepor o meio-fio.
        var g = MarkingBuilder.Build(plat, Straight(20), Ctx);
        Assert.All(g.Pieces, p => Assert.Equal(0.15, p.Thickness, 3));
        var ys = g.Pieces.SelectMany(p => p.Shape.Outer).Select(v => -v.Y).ToList();
        Assert.InRange(ys.Min(), 3.5 + RoadSetup.CurbWidth - 1e-6, 3.5 + RoadSetup.CurbWidth + 0.01);
        Assert.InRange(ys.Max(), 5.49, 5.51);
    }

    [Fact]
    public void Sidewalk_CustomLevelAndLoweredVegetation()
    {
        var s = Setup(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 },
            new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = 3, Altura = 0.20, AlturaVegetacao = -0.10, FaixaServico = 1.0 });
        var defs = s.Build(new PathReference(), new OutputSettings(), Cat).OfType<LinearMarkingDefinition>().ToList();
        Assert.Equal(0.20, defs.Single(l => l.Code == "MEIO-FIO").Height);
        Assert.All(defs.Where(l => l.Code == "CALCADA"), l => Assert.Equal(0.20, l.Height));
        var grass = defs.Single(l => l.Code == "GRAMADO");
        Assert.Equal(-0.10, grass.Height);
        var g = MarkingBuilder.Build(grass, Straight(10), Ctx);
        Assert.All(g.Pieces, p => Assert.Equal(-0.10, p.Elevation + p.Thickness, 3));
        var walk = MarkingBuilder.Build(defs.First(l => l.Code == "CALCADA"), Straight(10), Ctx);
        Assert.All(walk.Pieces, p => Assert.Equal(0.20, p.Elevation + p.Thickness, 3));
    }

    [Fact]
    public void Median_LoweredVegetation_KeepsCurbs()
    {
        var s = new RoadSetup { TwoWay = true, Center = CenterTreatment.Canteiro, MedianWidth = 3, MedianHeight = 0.15, MedianVegetationHeight = -0.2 };
        s.Right.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 });
        s.Left.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 });
        var defs = s.Build(new PathReference(), new OutputSettings(), Cat).OfType<LinearMarkingDefinition>().ToList();
        Assert.Equal(2, defs.Count(l => l.Code == "MEIO-FIO" && l.Height == null));
        Assert.Equal(-0.2, defs.Single(l => l.Code == "GRAMADO").Height);
    }

    [Fact]
    public void SectionLevels_SurviveJsonRoundTrip()
    {
        var s = Setup(new ElementoSecao { Tipo = TipoElementoSecao.Ciclofaixa, Largura = 2, Altura = 0.10 });
        s.MedianVegetationHeight = 0.3;
        var back = RoadTemplates.FromJson(RoadTemplates.ToJson(s))!;
        Assert.Equal(0.10, back.Right[0].Altura);
        Assert.True(back.Right[0].Elevado);
        Assert.Equal(0.3, back.MedianVegetationHeight);
    }

    [Fact]
    public void SectionProfile_DrawsLayersLevelsHeightsAndTitle()
    {
        var (defs, geo, path) = DetailGenerator.SectionSample();
        var sd = new SectionDimensionDefinition { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45), ProfilePosition = new Vec2(25, 10) };
        var c = new BuildContext { Catalog = Cat, ViewScale = 100, AllDefinitions = () => defs, GeometryOf = geo, PathOf = path };
        var g = DetailGenerator.SectionDimensions(sd, c);
        var texts = g.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("SEÇÃO TRANSVERSAL A–A", texts);
        Assert.Contains("+0,15", texts);
        Assert.Contains("±0,00", texts);
        Assert.Contains(texts, t => t.StartsWith("i = 2,0"));
        Assert.Contains("0,15", texts);                     // cota vertical do meio-fio
        Assert.Contains("MATERIAIS", texts);
        Assert.Contains(texts, t => t.StartsWith("Meio-fio de concreto"));
        // Camadas do perfil desenhadas à direita (fora da planta), incluindo o asfalto.
        Assert.Contains(g.Pieces, p => p.Color == MarkingColor.Asfalto && p.Shape.Bounds.Min.X > 24);
        // Sem posição = sem perfil (só a cota em planta, como antes).
        sd.ProfilePosition = null;
        var plan = DetailGenerator.SectionDimensions(sd, c);
        Assert.DoesNotContain(plan.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith("SEÇÃO TRANSVERSAL"));
    }

    [Fact]
    public void ProfileSegments_ReadElevationAndThickness()
    {
        var (defs, geo, _) = DetailGenerator.SectionSample();
        var segs = DetailGenerator.ProfileSegments(new Vec2(15, -8.45), new Vec2(15, 8.45), defs.Select(d => (d, geo(d)!)));
        Assert.Contains(segs, s => s.Color == MarkingColor.Asfalto && Math.Abs(s.Z0 + 0.05) < 1e-6 && Math.Abs(s.Z1) < 1e-6);
        Assert.Equal(0.15, DetailGenerator.SurfaceTop(segs, 1.0)!.Value, 3);        // calçada
        Assert.Equal(0.0, DetailGenerator.SurfaceTop(segs, 8.45)!.Value, 3);        // pista no eixo
    }

    [Fact]
    public void Railway_Ballasted_HasAllLayersAndCounts()
    {
        var d = new RailwayDefinition { Tracks = 2 };
        var g = MarkingBuilder.Build(d, Straight(12), Ctx);
        Assert.Empty(g.Warnings);
        Assert.Contains(g.Pieces, p => p.Color == MarkingColor.Brita && p.Solid != null);
        Assert.Contains(g.Pieces, p => p.Color == MarkingColor.Terra);
        Assert.Contains(g.Pieces, p => p.Color == MarkingColor.Metal);
        Assert.Equal(40, g.UnitCount);                       // 12 m / 0,60 = 20 dormentes × 2 linhas
        var (_, _, _, _, railTop) = RailwayGenerator.Levels(d);
        Assert.Equal(railTop, g.Pieces.Where(p => p.Color == MarkingColor.Metal).Max(p => p.Elevation + p.Thickness), 3);
        var q = RailwayGenerator.Quantities(d, 1000);
        Assert.Equal(4000, q.RailLength, 3);
        Assert.Equal(1666 * 2, q.Sleepers);
        Assert.True(q.BallastM3 > 1000 && q.SubBallastM3 > 500);
        Assert.False(MarkingColors.IsPaint(MarkingColor.Brita));
        Assert.False(MarkingColors.IsPaint(MarkingColor.Terra));
    }

    [Fact]
    public void Railway_MixedGauge_HasThreeRails()
    {
        var d = new RailwayDefinition { Gauge = BitolaFerroviaria.Mista };
        var r = RailwayGenerator.RailCenters(d);
        Assert.Equal(3, r.Count);
        var hw = RailwayGenerator.Profile(d.Rail).Head;
        Assert.Equal(1.600, r[1] - r[0] - hw, 3);
        Assert.Equal(1.000, r[2] - r[0] - hw, 3);
    }

    [Theory]
    [InlineData(TipoViaFerrea.Laje)]
    [InlineData(TipoViaFerrea.Embutida)]
    public void Railway_SlabAndEmbedded_Build(TipoViaFerrea type)
    {
        var d = new RailwayDefinition { Type = type, Rail = PerfilTrilho.Ri60, Gauge = BitolaFerroviaria.Padrao };
        var g = MarkingBuilder.Build(d, Straight(10), Ctx);
        Assert.NotEmpty(g.Pieces);
        Assert.DoesNotContain(g.Pieces, p => p.Color == MarkingColor.Brita);
        if (type == TipoViaFerrea.Embutida)
        {
            // Trilho rente ao pavimento.
            Assert.Equal(0, g.Pieces.Where(p => p.Color == MarkingColor.Metal).Max(p => p.Elevation + p.Thickness), 3);
            Assert.Equal(0, g.Pieces.Where(p => p.Color == MarkingColor.PavimentoConcreto).Max(p => p.Elevation + p.Thickness), 3);
        }
    }

    [Fact]
    public void Railway_RoundTripAndQuantities()
    {
        var d = new RailwayDefinition { Tracks = 3, TrackSpacing = 5, Sleeper = TipoDormente.Madeira, Rail = PerfilTrilho.TR68 };
        var back = (RailwayDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.Equal(3, back.Tracks);
        Assert.Equal(PerfilTrilho.TR68, back.Rail);
        var info = MarkingBuilder.Describe(back, Cat);
        Assert.Equal("m", info.Unit);
        Assert.Contains("madeira", info.Name);
        var rows = QuantityCalculator.Compute(new[] { ((MarkingDefinition)back, MarkingBuilder.Build(back, Straight(30), Ctx)) }, Cat);
        var row = Assert.Single(rows.Where(r => r.Code == "FERROVIA").GroupBy(r => r.Code)).First();
        Assert.Equal(CategoriaQuantitativo.PavimentacaoGeometria, row.Category);
        Assert.Equal(30, rows.Where(r => r.Code == "FERROVIA").Sum(r => r.PaintedLength), 3);
    }

    [Fact]
    public void Railway_ThumbnailSample()
    {
        var (geo, kind) = QuantitySamples.Sample(new RailwayDefinition { Tracks = 2 }, MarkingColor.Brita, Ctx);
        Assert.Equal(TipoMiniatura.Perspectiva, kind);
        Assert.NotNull(geo);
        Assert.NotEmpty(geo!.Pieces);
    }
}
