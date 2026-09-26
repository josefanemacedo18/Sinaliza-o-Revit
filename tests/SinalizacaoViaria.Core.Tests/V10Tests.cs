using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Raio de esquina na criação da via, ciclovia composta, escala, sarjetão real e dispositivos com forma realista.</summary>
public class V10Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static readonly Polyline2 Line20 = new(new[] { Vec2.Zero, new Vec2(20, 0) });

    [Fact]
    public void NodeRadius_UsesUserRadiusOrHierarchyDefault()
    {
        var art = new RoadPavementDefinition { Hierarchy = HierarquiaViaria.Arterial };
        var loc = new RoadPavementDefinition { Hierarchy = HierarquiaViaria.Local };
        Assert.Equal(Hierarquia.CornerRadius(HierarquiaViaria.Arterial, HierarquiaViaria.Local), Hierarquia.NodeRadius(new[] { art, loc }), 6);
        loc.CornerRadius = 4;
        Assert.Equal(4, Hierarquia.NodeRadius(new[] { art, loc }), 6);
        art.CornerRadius = 12;
        Assert.Equal(4, Hierarquia.NodeRadius(new[] { art, loc }), 6);   // prevalece a via de menor hierarquia
        loc.CornerRadius = null;
        Assert.Equal(12, Hierarquia.NodeRadius(new[] { art, loc }), 6);
    }

    [Fact]
    public void RoadSetup_PassesCornerRadiusToPavement()
    {
        var s = RoadTemplates.All[0].Create();
        s.CornerRadius = 3.5;
        var defs = s.Build(PathReference.FromPoints(Line20.Points, 0), new OutputSettings());
        Assert.Equal(3.5, defs.OfType<RoadPavementDefinition>().Single().CornerRadius);
    }

    [Theory]
    [InlineData(TipoCiclo.CiclofaixaUnidirecional)]
    [InlineData(TipoCiclo.CiclofaixaBidirecional)]
    [InlineData(TipoCiclo.Ciclovia)]
    [InlineData(TipoCiclo.FaixaCaminhada)]
    public void BikeLane_BuildsSingleGroupWithoutOverlap(TipoCiclo t)
    {
        var s = new BikeLaneSetup { Type = t, Width = BikeLaneSetup.DefaultWidth(t) };
        var defs = s.Build(PathReference.FromPoints(Line20.Points, 0), new OutputSettings());
        Assert.NotEmpty(defs);
        Assert.Single(defs.Select(d => d.GroupId).Distinct());
        foreach (var d in defs) Assert.DoesNotContain(MarkingBuilder.Build(d, Line20, Ctx).Warnings, w => w.Contains("não existe"));
    }

    [Fact]
    public void Scale_EnlargesSymbolAndText()
    {
        var s1 = MarkingBuilder.Build(new SymbolMarkingDefinition { Code = "PEM-F", Length = 5 }, null, Ctx);
        var s2 = MarkingBuilder.Build(new SymbolMarkingDefinition { Code = "PEM-F", Length = 5, Scale = 0.5 }, null, Ctx);
        Assert.Equal(0.25, s2.Pieces.Sum(p => p.Shape.Area) / s1.Pieces.Sum(p => p.Shape.Area), 2);
        var t1 = MarkingBuilder.Build(new TextMarkingDefinition { Text = "PARE" }, null, Ctx);
        var t2 = MarkingBuilder.Build(new TextMarkingDefinition { Text = "PARE", Scale = 2 }, null, Ctx);
        double H(MarkingGeometry g) { var ys = g.Pieces.SelectMany(p => p.Shape.Outer).Select(v => v.Y).ToList(); return ys.Max() - ys.Min(); }
        if (t1.Pieces.Count > 0) Assert.Equal(2, H(t2) / H(t1), 1);
    }

    [Fact]
    public void Sarjetao_IsConcaveConcreteProfile()
    {
        var prof = SarjetaoGenerator.Profile(1.2, 0.05);
        Assert.Equal(-0.05, prof.Outer.Where(v => Math.Abs(v.X) < 1e-6).Max(v => v.Y), 3);   // flecha no centro
        Assert.Equal(0, prof.Bounds.Max.Y, 6);                                                  // bordas no nível da pista
        var geo = SarjetaoGenerator.Build(new Polyline2(new[] { Vec2.Zero, new Vec2(0, 7) }), 1.2, 0.05);
        Assert.All(geo.Pieces, p => { Assert.NotNull(p.Profile); Assert.Equal(MarkingColor.Concreto, p.Color); });
        Assert.Contains(SarjetaoGenerator.Build(Line20, 1.2, 0.2).Warnings, w => w.Length > 0);
        var d = MarkingBuilder.Build(new LinearMarkingDefinition { Code = "SARJETAO", Depth = 0.04 }, new Polyline2(new[] { Vec2.Zero, new Vec2(0, 7) }), Ctx);
        Assert.NotEmpty(d.Pieces);
    }

    [Theory]
    [InlineData("SEG-CIC")] [InlineData("TACHAO-SEG")] [InlineData("BAL-FLEX")] [InlineData("CIL")] [InlineData("PIL")]
    [InlineData("PRI")] [InlineData("SEP-CONC")] [InlineData("NJ")] [InlineData("NJ-MOD")] [InlineData("BAR-PLAST")]
    [InlineData("DEF")] [InlineData("DEF-DUPLA")] [InlineData("DEF-CABO")]
    public void Devices_HaveRealisticSolidsWithinHeight(string code)
    {
        var dv = Cat.Dispositivo(code)!;
        var geo = MarkingBuilder.Build(new DeviceMarkingDefinition { Code = code }, Line20, Ctx);
        Assert.NotEmpty(geo.Pieces);
        Assert.Empty(geo.Warnings);
        Assert.All(geo.Pieces, p => Assert.True(p.Solid != null || p.Profile != null));
        var top = geo.Pieces.Max(p => p.Solid?.MaxZ ?? p.Thickness);
        Assert.InRange(top, dv.Altura * 0.9, dv.Altura + 1e-6);
        Assert.All(geo.Pieces, p => Assert.InRange(p.Shape.Centroid.X, -0.5, 20.5));
    }

    [Fact]
    public void NewJersey_ProfileHasStandardShape()
    {
        var p = DeviceGenerator.NewJerseyProfile(0.60, 0.81, plastic: false);
        Assert.Equal(0.60, p.Bounds.Max.X - p.Bounds.Min.X, 6);
        Assert.Equal(0.81, p.Bounds.Max.Y, 6);
        Assert.Contains(p.Outer, v => Math.Abs(v.X - 0.15 / 2) < 1e-6 && Math.Abs(v.Y - 0.81) < 1e-6);   // topo com 15 cm
        Assert.True(p.Area > 0);
        var mod = MarkingBuilder.Build(new DeviceMarkingDefinition { Code = "BAR-PLAST" }, Line20, Ctx);
        Assert.Contains(mod.Pieces, x => x.Color == MarkingColor.Vermelha);
        Assert.Contains(mod.Pieces, x => x.Color == MarkingColor.Branca);
    }
}
