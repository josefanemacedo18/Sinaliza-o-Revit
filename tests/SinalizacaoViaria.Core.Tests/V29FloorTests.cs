using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada V: vias reconhecidas em pisos comuns do Revit (eixo, largura, encontros).</summary>
public class V29FloorTests
{
    private static Polygon2 Rect(double x0, double y0, double x1, double y1) => Polygon2.Rectangle(new Vec2(x0, y0), new Vec2(x1, y1));

    [Fact]
    public void StraightFloor_GivesOneAxisOnTheCenterWithItsWidth()
    {
        var r = FloorRoads.Read(new[] { Rect(0, -3.5, 120, 3.5) });
        var road = Assert.Single(r.Roads);
        Assert.Empty(r.Junctions);
        Assert.Equal(7.0, road.Width, 1);
        Assert.InRange(road.Axis.Length, 118, 121);
        Assert.All(road.Axis.Points, p => Assert.InRange(p.Y, -0.3, 0.3));
        Assert.False(road.VariableWidth);
    }

    [Fact]
    public void CrossShapedFloor_GivesTwoThroughRoadsAndOneJunction()
    {
        var plus = new Polygon2(new[] { new Vec2(-100,-3.5), new Vec2(-3.5,-3.5), new Vec2(-3.5,-100), new Vec2(3.5,-100), new Vec2(3.5,-3.5), new Vec2(100,-3.5),
            new Vec2(100,3.5), new Vec2(3.5,3.5), new Vec2(3.5,100), new Vec2(-3.5,100), new Vec2(-3.5,3.5), new Vec2(-100,3.5) });
        var r = FloorRoads.Read(new[] { plus });
        Assert.Equal(2, r.Roads.Count);
        var j = Assert.Single(r.Junctions);
        Assert.True(j.Length < 0.5);
        Assert.All(r.Roads, x => Assert.InRange(x.Axis.Length, 195, 201));
    }

    [Fact]
    public void TeeMadeOfTwoOverlappingFloors_StemEndsOnTheMainAxis()
    {
        var r = FloorRoads.Read(new[] { Rect(-100, -3.5, 100, 3.5), Rect(-3, 3, 3, 90) });
        Assert.Equal(2, r.Roads.Count);
        var stem = r.Roads.Single(x => x.Width < 6.5);
        Assert.Equal(6.0, stem.Width, 1);
        var j = Assert.Single(r.Junctions);
        Assert.InRange(j.Y, -0.3, 0.3);
        Assert.True(stem.Axis.Points.Min(p => p.DistanceTo(j)) < 0.5);
    }

    [Fact]
    public void CurvedFloor_AxisFollowsTheMiddleOfTheCurve()
    {
        var ring = Enumerable.Range(0, 41).Select(i => { var a = Math.PI / 2 * i / 40; return new Vec2(50 * Math.Cos(a), 50 * Math.Sin(a)); })
            .Concat(Enumerable.Range(0, 41).Select(i => { var a = Math.PI / 2 * (40 - i) / 40; return new Vec2(42 * Math.Cos(a), 42 * Math.Sin(a)); }));
        var r = FloorRoads.Read(new[] { new Polygon2(ring) });
        var road = Assert.Single(r.Roads);
        Assert.Equal(8.0, road.Width, 1);
        foreach (var p in road.Axis.Points.Skip(1).SkipLast(1)) Assert.InRange(p.Length, 45.5, 46.5);
    }

    [Fact]
    public void Fit_ScalesTheTemplateLanesToTheMeasuredWidth()
    {
        var setup = RoadTemplates.All[1].Create();
        var r = FloorRoads.Read(new[] { new Polygon2(new[] { new Vec2(0, -3), new Vec2(60, -3), new Vec2(80, -5), new Vec2(150, -5), new Vec2(150, 5), new Vec2(80, 5), new Vec2(60, 3), new Vec2(0, 3) }) });
        var road = Assert.Single(r.Roads);
        Assert.True(road.VariableWidth);
        var fitted = FloorRoads.Fit(setup, road);
        Assert.Equal(road.Width, fitted.CarriagewayWidth, 1);
        Assert.NotEmpty(fitted.LargurasVariaveis);
        Assert.Contains(fitted.LargurasVariaveis, p => p.BordoDireito < 3.5);
        Assert.Contains(fitted.LargurasVariaveis, p => p.BordoDireito > 4.5);
        // O modelo original não muda.
        Assert.NotEqual(fitted.CarriagewayWidth, setup.CarriagewayWidth);
    }
}
