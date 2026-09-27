using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada de correções: tachas/tachões em 3D, rotatória elevada e ilha em calota, quantitativo memorial, seção guardada na via.</summary>
public class V15Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static Polyline2 Straight(double len) => new(new[] { new Vec2(0, 0), new Vec2(len, 0) });

    [Fact]
    public void Studs_AreSolidsWithReflectiveFaces()
    {
        var t = Cat.Linear("TACHAO")!;
        var geo = LinearPatternGenerator.Generate(Straight(40), t, t.Variantes[0]);
        Assert.Equal(10, geo.UnitCount);
        Assert.True(geo.Pieces.Count > geo.UnitCount, "cada tachão tem corpo + refletivos");
        Assert.All(geo.Pieces, p => Assert.NotNull(p.Solid));
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Branca);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Amarela && p.Thickness > 0.04);
        var body = geo.Pieces.First(p => p.Color == MarkingColor.Amarela);
        Assert.InRange(body.Shape.Bounds.Max.X - body.Shape.Bounds.Min.X, 0.24, 0.26);   // 0,25 m ao longo do caminho
        Assert.InRange(body.Shape.Bounds.Max.Y - body.Shape.Bounds.Min.Y, 0.14, 0.16);   // 0,15 m de largura
    }

    [Fact]
    public void StudPieces_TachaIsLowAndTachaoIsTrapezoid()
    {
        var tacha = DeviceGenerator.StudPieces(0.10, 0.10, 0.02, MarkingColor.Branca, false).ToList();
        Assert.True(tacha.Count >= 2);
        Assert.All(tacha, p => Assert.True(p.Solid!.MaxZ <= 0.02 + 1e-9));
        var tachao = DeviceGenerator.StudPieces(0.25, 0.15, 0.05, MarkingColor.Amarela, true).ToList();
        Assert.Equal(3, tachao.Count);   // corpo + 2 refletivos
        Assert.Equal(0.05, tachao[0].Solid!.MaxZ, 6);
    }

    private static RoundaboutDefinition Rb(Action<RoundaboutDefinition>? setup = null)
    {
        var d = new RoundaboutDefinition();
        d.ApplyPreset(TipoRotatoria.Compacta);
        setup?.Invoke(d);
        foreach (var a in new[] { 0.0, 90, 180, 270 }) d.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = 2.5 });
        return d;
    }

    [Fact]
    public void Roundabout_Raised_RingHigherWithRamps()
    {
        var flat = MarkingBuilder.Build(Rb(), null, Ctx);
        var d = Rb(x => { x.Raised = true; x.RaisedHeight = 0.10; x.RampLength = 1.5; });
        var geo = MarkingBuilder.Build(d, null, Ctx);
        var pavFlat = flat.Pieces.Where(p => p.Color == MarkingColor.Asfalto && p.Profile == null).Max(p => p.Elevation + p.Thickness);
        var pavRing = geo.Pieces.Where(p => p.Color == MarkingColor.Asfalto && p.Profile == null).Max(p => p.Elevation + p.Thickness);
        Assert.Equal(0.0, pavFlat, 6);
        Assert.Equal(0.10, pavRing, 6);
        var ramps = geo.Pieces.Where(p => p.Profile != null && p.Color == MarkingColor.Asfalto).ToList();
        Assert.Equal(4, ramps.Count);
        Assert.All(ramps, r => Assert.Equal(7.0, r.Profile!.Depth, 6));
        Assert.Contains(geo.Warnings, w => w.Contains("elevada"));
        var back = (RoundaboutDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.True(back.Raised);
    }

    [Fact]
    public void Roundabout_DomeIsland_IsLoftedCone()
    {
        var d = Rb(x => { x.IslandType = TipoIlhaCentral.Calota; x.DomeHeight = 0.2; });
        var L = RoundaboutGenerator.Layout(d);
        Assert.Empty(L.IslandCurb);
        var geo = MarkingBuilder.Build(d, null, Ctx);
        var dome = geo.Pieces.Where(p => p.Solid != null && p.Color == MarkingColor.PavimentoConcreto).ToList();
        Assert.Single(dome);
        Assert.Equal(0.2, dome[0].Solid!.MaxZ, 6);
        Assert.True(dome[0].Shape.Contains(d.Center));
    }

    [Fact]
    public void Quantities_MemorialRows_OnePerSignModel()
    {
        var items = new List<(MarkingDefinition, MarkingGeometry)>();
        foreach (var i in Enumerable.Range(0, 3))
        {
            var sign = new SignDefinition { Code = "R-1", Position = new Vec2(i * 10, 0), Direction = new Vec2(1, 0) };
            items.Add((sign, MarkingBuilder.Build(sign, null, Ctx)));
        }
        var line = new LinearMarkingDefinition { Code = "LFO-1" };
        items.Add((line, MarkingBuilder.Build(line, Straight(20), Ctx)));
        var rows = QuantityCalculator.Compute(items, Cat);
        var r1 = rows.Where(r => r.Code == "R-1").ToList();
        Assert.Single(r1);
        Assert.True(r1[0].Memorial);
        Assert.Equal(3, r1[0].Units);
        Assert.Equal(3, r1[0].Elements);
        Assert.Equal("", r1[0].Material);
        Assert.Equal(0, r1[0].MaterialConsumption);
        Assert.Equal("total", r1[0].AreaKind);
        var lfo = rows.Single(r => r.Code == "LFO-1");
        Assert.False(lfo.Memorial);
        Assert.NotEqual("", lfo.Material);
    }

    [Fact]
    public void RoadSetup_StoresSectionAndReusesIds()
    {
        var setup = RoadTemplates.All[0].Create();
        setup.Hierarchy = HierarquiaViaria.Local;
        var path = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(50, 0) }, 0);
        var defs = setup.Build(path, new OutputSettings(), Cat, "grupo-x", "pav-x");
        var pav = defs.OfType<RoadPavementDefinition>().Single();
        Assert.Equal("pav-x", pav.Id);
        Assert.All(defs, d => Assert.Equal("grupo-x", d.GroupId));
        Assert.NotNull(pav.SetupJson);
        var again = RoadTemplates.FromJson(pav.SetupJson);
        Assert.NotNull(again);
        Assert.Equal(setup.Right.Count, again!.Right.Count);
        Assert.Equal(setup.TotalWidth, again.TotalWidth, 6);
    }
}

public class V15SignTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    [Theory]
    [InlineData(TipoSuporte.BracoProjetado, 1)]
    [InlineData(TipoSuporte.SemiPortico, 1)]
    [InlineData(TipoSuporte.Portico, 2)]
    public void OverheadSupports_ColumnsAndBeam(TipoSuporte support, int columns)
    {
        var d = new SignDefinition { Code = "R-1", Position = Vec2.Zero, Direction = new Vec2(0, 1), Support = support, MountHeight = 5.5, LateralOffset = 6, StructureSpan = 12 };
        var geo = MarkingBuilder.Build(d, null, Ctx);
        Assert.True(d.Overhead);
        var cols = geo.Pieces.Where(p => p.Color == MarkingColor.Metal && p.Profile == null && p.Solid == null && p.Elevation < 0.5 && p.Thickness > 6).ToList();
        Assert.Equal(columns, cols.Count);
        Assert.Contains(geo.Pieces, p => p.Profile != null && p.Color == MarkingColor.Metal && Math.Abs(p.Profile!.Depth - 12.3) < 1e-6);
        Assert.DoesNotContain(geo.Warnings, w => w.Contains("5,50"));
        d.MountHeight = 4.0;
        Assert.Contains(MarkingBuilder.Build(d, null, Ctx).Warnings, w => w.Contains("5,50"));
        if (support == TipoSuporte.Portico) Assert.Contains(cols, c => c.Shape.Centroid.X > 11.5);
    }
}
