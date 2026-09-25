using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

public class NewFeatureTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    [Fact]
    public void Catalog_StillValid() => Assert.Empty(CatalogService.Validate(Cat));

    [Theory]
    [InlineData("R-1")] [InlineData("R-2")] [InlineData("R-19")] [InlineData("A-18")] [InlineData("IND-DEST")] [InlineData("A-24")]
    public void Signs_BuildPlatePostAndLegend(string code)
    {
        var d = new SignDefinition { Code = code, Position = new Vec2(10, 5), Direction = Vec2.UnitY };
        var geo = MarkingBuilder.Build(d, null, Ctx);
        Assert.Equal(1, geo.UnitCount);
        var plates = geo.Pieces.Where(p => p.Profile != null).ToList();
        Assert.NotEmpty(plates);
        Assert.All(plates, p => Assert.True(p.Profile!.Profile.Bounds.Min.Y >= 2.10 - 1e-6));   // altura livre
        Assert.Contains(geo.Pieces, p => p.Profile == null && p.Color == MarkingColor.Metal);   // poste
        // A face fica voltada para o condutor que chega (−Y): as chapas ficam ao sul do poste.
        Assert.All(plates, p => Assert.True(p.Shape.Centroid.Y < 5));
    }

    [Fact]
    public void Sign_StopHasWhiteLegend()
    {
        var geo = MarkingBuilder.Build(new SignDefinition { Code = "R-1" }, null, Ctx);
        Assert.Contains(geo.Pieces, p => p.Profile != null && p.Color == MarkingColor.Branca && p.Profile.Depth < 0.0015);
    }

    [Fact]
    public void UrbanElements_AllBuild_PointAndPath()
    {
        foreach (var m in Cat.Mobiliario)
        {
            var geo = MarkingBuilder.Build(new UrbanElementDefinition { Code = m.Codigo, Position = Vec2.Zero }, null, Ctx);
            Assert.True(geo.Pieces.Count > 0, m.Codigo);
            Assert.Equal(1, geo.UnitCount);
        }
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(40, 0) });
        var trees = MarkingBuilder.Build(new UrbanElementDefinition { Code = "ARVORE", UsePath = true, Spacing = 8, StartOffset = 4 }, path, Ctx);
        Assert.Equal(5, trees.UnitCount);
    }

    [Fact]
    public void Ramp_Nbr9050_GeometryAndWarnings()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(0, 1) });
        var d = new RampDefinition();
        var geo = MarkingBuilder.Build(d, path, Ctx);
        var (w, len, flare) = RampGenerator.Dimensions(d);
        Assert.Equal(0.15 / 0.0833, len, 3);
        Assert.Equal(1.5, flare, 6);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Amarela);   // piso tátil
        Assert.Empty(geo.Warnings);
        var steep = MarkingBuilder.Build(new RampDefinition { Slope = 0.12, Width = 1.0 }, path, Ctx);
        Assert.Equal(2, steep.Warnings.Count);
        var fp = RampGenerator.Footprint(d, path);
        Assert.True(fp.Contains(new Vec2(0, 0.5)));
    }

    [Fact]
    public void Exclusions_CutSidewalk()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(20, 0) });
        var walk = new LinearMarkingDefinition { Code = "CALCADA", WidthOverride = 3 };
        var full = MarkingBuilder.Build(walk, path, Ctx).TotalArea;
        walk.Exclusions.Add(new ExclusionZone { Points = Polygon2.Rectangle(new Vec2(8, -2), new Vec2(10, 2)).Outer.ToList() });
        var cut = MarkingBuilder.Build(walk, path, Ctx);
        Assert.Equal(full - 2 * 3, cut.TotalArea, 3);
        Assert.Equal(2, cut.Pieces.Count);
        var back = (LinearMarkingDefinition)MarkingDefinition.FromJson(walk.ToJson())!;
        Assert.Single(back.Exclusions);
    }

    [Theory]
    [InlineData(TipoModeracao.OndulacaoA, 0.08)]
    [InlineData(TipoModeracao.OndulacaoB, 0.06)]
    [InlineData(TipoModeracao.FaixaElevada, 0.15)]
    public void TrafficCalming_VolumesAndDrapedPaint(TipoModeracao type, double height)
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(0, 7) });
        var geo = MarkingBuilder.Build(new TrafficCalmingDefinition { Type = type }, path, Ctx);
        var volume = geo.Pieces.Where(p => p.Profile != null).ToList();
        Assert.NotEmpty(volume);
        Assert.Equal(height, volume.Max(p => p.Profile!.Profile.Bounds.Max.Y), 3);
        var paint = geo.Pieces.Where(p => p.Profile == null).ToList();
        Assert.NotEmpty(paint);
        Assert.All(paint, p => Assert.InRange(p.Elevation, 0, height + 0.01));
    }

    [Fact]
    public void InvertedHump_IsBelowPavement()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(0, 7) });
        var geo = MarkingBuilder.Build(new TrafficCalmingDefinition { Type = TipoModeracao.LombadaInvertida }, path, Ctx);
        Assert.All(geo.Pieces.Where(p => p.Profile != null), p => Assert.True(p.Profile!.Profile.Bounds.Max.Y <= 1e-9));
    }

    [Fact]
    public void Road_WalkingLane_BikeParams_Gutter()
    {
        var s = new RoadSetup
        {
            Right =
            {
                new() { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 },
                new() { Tipo = TipoElementoSecao.Ciclofaixa, Largura = 2.4, Bidirecional = true, LarguraLinha = 0.15, LinhaSeccionada = true, TracoLinha = 0.5, EspacoLinha = 0.5, TamanhoSimbolo = 1.2 },
                new() { Tipo = TipoElementoSecao.FaixaCaminhada, Largura = 1.5, CorCaminhada = MarkingColor.Verde },
                new() { Tipo = TipoElementoSecao.Calcada, Largura = 3, Sarjeta = 0.30 },
            },
            Left = { new() { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 } },
        };
        var defs = s.Build(new PathReference(), new OutputSettings(), Cat);
        var lin = defs.OfType<LinearMarkingDefinition>().ToList();
        var ld = lin.Single(d => d.Code == "CIC-LD");
        Assert.Equal(0.15, ld.WidthOverride);
        Assert.Equal(new[] { 0.5, 0.5 }, ld.PatternOverride);
        Assert.Contains(lin, d => d.Code == "CIC-LC");
        Assert.Contains(lin, d => d.Code == "FCA" && d.ColorOverride == MarkingColor.Verde);
        Assert.Equal(2, lin.Count(d => d.Code == "FCA-BD"));
        Assert.Contains(lin, d => d.Code == "SARJETA" && Math.Abs(d.Offset + (3.5 + 2.4 + 1.5 - 0.15)) < 1e-9);
        Assert.Contains(defs.OfType<RepeatedMarkingDefinition>(), d => d.SymbolCode == "SIC" && Math.Abs(d.Length - 1.2) < 1e-9);
        Assert.DoesNotContain(defs.OfType<RepeatedMarkingDefinition>(), d => d.SymbolCode == "CIC-SETA"); // bidirecional
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(60, 0) });
        foreach (var d in defs) Assert.True(MarkingBuilder.Build(d, path, Ctx).Pieces.Count > 0, d.DisplayCode);
    }

    [Fact]
    public void Quantities_SignsAreUnitsWithoutPaint()
    {
        var items = new MarkingDefinition[] { new SignDefinition { Code = "R-19" }, new SignDefinition { Code = "R-19" } }
            .Select(d => (d, MarkingBuilder.Build(d, null, Ctx)));
        var rows = QuantityCalculator.Compute(items, Cat);
        Assert.All(rows, r => Assert.Equal(0, r.MaterialConsumption));
        Assert.Equal(2, rows.Where(r => r.Code == "R-19").Sum(r => r.Units));
    }

    [Fact]
    public void NewDefinitions_RoundTripJson()
    {
        MarkingDefinition[] defs =
        {
            new SignDefinition { Code = "R-19", Legend = "60", Support = TipoSuporte.Duplo },
            new UrbanElementDefinition { Code = "POSTE", UsePath = true, Spacing = 30 },
            new RampDefinition { Type = TipoRampa.AcessoVeiculos, Width = 3 },
            new TrafficCalmingDefinition { Type = TipoModeracao.FaixaElevada, Crosswalk = false },
        };
        foreach (var d in defs) Assert.Equal(d.ToJson(), MarkingDefinition.FromJson(d.ToJson())!.ToJson());
    }

    [Fact]
    public void GuardRail_Double_HasTwoRails()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(20, 0) });
        var geo = MarkingBuilder.Build(new DeviceMarkingDefinition { Code = "DEF-DUPLA" }, path, Ctx);
        var rails = geo.Pieces.Where(p => !p.IsUnit && p.Elevation > 0.2).ToList();
        Assert.True(rails.Count >= 2);
        Assert.Contains(rails, r => r.Shape.Centroid.Y > 0);
        Assert.Contains(rails, r => r.Shape.Centroid.Y < 0);
    }
}
