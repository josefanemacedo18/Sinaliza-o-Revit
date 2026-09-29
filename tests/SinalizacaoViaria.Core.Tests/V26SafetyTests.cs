using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada T: sonorizador longitudinal e área de escape.</summary>
public class V26SafetyTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static Polyline2 Line(double l) => new(new[] { Vec2.Zero, new Vec2(l, 0) });

    [Fact]
    public void RumbleStrip_CountsElementsAndRespectsGaps()
    {
        var d = new RumbleStripDefinition { Offset = -0.4 };
        d.ApplyPreset(TipoSonorizador.Fresado);
        var g = MarkingBuilder.Build(d, Line(30), Ctx);
        Assert.Equal(100, g.UnitCount);                     // 30 m / 0,30 m
        Assert.All(g.Pieces, p => Assert.InRange(p.Shape.Centroid.Y, -0.41, -0.39));

        d.SegmentLength = 12; d.GapLength = 3.6;
        var g2 = MarkingBuilder.Build(d, Line(30), Ctx);
        Assert.True(g2.UnitCount < g.UnitCount);
        Assert.DoesNotContain(g2.Pieces, p => p.Shape.Centroid.X is > 12.2 and < 15.4);
    }

    [Fact]
    public void RumbleStrip_RaisedThermoplasticHasBaseLineAndRoundTrips()
    {
        var d = new RumbleStripDefinition();
        d.ApplyPreset(TipoSonorizador.TermoplasticoRelevo);
        var g = MarkingBuilder.Build(d, Line(10), Ctx);
        Assert.Contains(g.Pieces, p => !p.IsUnit);
        Assert.Contains(g.Pieces, p => p.IsUnit && p.Elevation > 0);
        var back = (RumbleStripDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.Equal(TipoSonorizador.TermoplasticoRelevo, back.Type);
        Assert.Equal("SON-TER", back.DisplayCode);
    }

    [Fact]
    public void EscapeRamp_LengthFromSpeedGradeAndMaterial()
    {
        var d = new EscapeRampDefinition { EntrySpeed = 130, Grade = 5, Material = MaterialRetencao.Seixo };
        Assert.Equal(130.0 * 130 / (254 * 0.30), d.RequiredLength, 3);   // ≈ 222 m
        Assert.Equal(225, d.ActualLength);
        d.Material = MaterialRetencao.Brita;
        Assert.True(d.RequiredLength > 600);
    }

    [Fact]
    public void EscapeRamp_BuildsBedServiceLaneAndWarnsWhenShort()
    {
        var d = new EscapeRampDefinition { EntrySpeed = 110, Grade = 8, Length = 100 };
        var g = MarkingBuilder.Build(d, Line(120), Ctx);
        Assert.Contains(g.Pieces, p => p.Color == MarkingColor.Brita && p.Elevation < 0);
        Assert.Contains(g.Pieces, p => p.Color == MarkingColor.Asfalto);
        Assert.Contains(g.Pieces, p => p.Color == MarkingColor.Terra);
        Assert.Contains(g.Warnings, w => w.Contains("necessários"));
        var bed = g.Pieces.Where(p => p.Color == MarkingColor.Brita).ToList();
        Assert.True(bed.Min(p => p.Thickness) < 0.3 && bed.Max(p => p.Thickness) > 0.99);   // transição de profundidade
        var back = MarkingDefinition.FromJson(d.ToJson());
        Assert.IsType<EscapeRampDefinition>(back);
    }
}
