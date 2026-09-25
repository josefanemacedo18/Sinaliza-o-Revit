using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Ímã de conexão das pontas dos eixos (estilo InfraWorks).</summary>
public class V8Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static IntersectionRoad Road(int template, params Vec2[] axis)
    {
        var defs = RoadTemplates.All[template].Create().Build(PathReference.FromPoints(axis, 0), new OutputSettings(), Cat);
        return new IntersectionRoad(defs.OfType<RoadPavementDefinition>().Single(), new Polyline2(axis));
    }

    private static readonly IntersectionRoad Main = Road(1, new Vec2(-100, 0), new Vec2(100, 0));   // coletora, ~10 m de meia largura

    [Fact]
    public void EndDroppedOnRoad_GoesToAxisKeepingItsAngle()
    {
        // Via a 60° que termina a 4 m do eixo (sobre a pista/calçada da coletora).
        var dir = new Vec2(Math.Cos(Math.PI / 3), Math.Sin(Math.PI / 3));
        var end = new Vec2(10, 4);
        var axis = new Polyline2(new[] { end + dir * 80, end });
        var (s, e) = RoadConnection.MagnetEnds(axis, new[] { Main });
        Assert.Null(s);
        var m = Assert.NotNull(e);
        Assert.Equal(TipoEncaixe.EixoDeVia, m.Kind);
        Assert.Equal(0, m.Point.Y, 6);
        // Ao longo da própria direção (não é a projeção perpendicular).
        Assert.Equal(10 - 4 / Math.Tan(Math.PI / 3), m.Point.X, 3);
    }

    [Fact]
    public void Overshoot_IsTrimmedToAxis()
    {
        var axis = new Polyline2(new[] { new Vec2(20, 80), new Vec2(20, -5) });     // passou 5 m do eixo
        var (_, e) = RoadConnection.MagnetEnds(axis, new[] { Main });
        var m = Assert.NotNull(e);
        Assert.True(m.Point.DistanceTo(new Vec2(20, 0)) < 1e-6);
    }

    [Fact]
    public void NearOtherEnd_Continues_AndFarOrOnAxis_Unchanged()
    {
        var axis = new Polyline2(new[] { new Vec2(200, 30), new Vec2(104, 2) });
        var (_, e) = RoadConnection.MagnetEnds(axis, new[] { Main });
        Assert.Equal(TipoEncaixe.PontaDeVia, e!.Value.Kind);
        Assert.Equal(new Vec2(100, 0), e.Value.Point);

        var far = new Polyline2(new[] { new Vec2(0, 80), new Vec2(0, 25) });
        Assert.Equal((null, null), RoadConnection.MagnetEnds(far, new[] { Main }));
        var onAxis = new Polyline2(new[] { new Vec2(0, 80), Vec2.Zero });
        Assert.Null(RoadConnection.MagnetEnds(onAxis, new[] { Main }).End);
    }

    [Fact]
    public void EndInsideRoundabout_GoesToCenter()
    {
        var axis = new Polyline2(new[] { new Vec2(0, 200), new Vec2(3, 110) });
        var (_, e) = RoadConnection.MagnetEnds(axis, new[] { Main }, new[] { (new Vec2(0, 100), 18.0) });
        Assert.Equal(TipoEncaixe.Rotatoria, e!.Value.Kind);
        Assert.Equal(new Vec2(0, 100), e.Value.Point);
    }

    [Fact]
    public void MagnetThenLayout_FormsTeeAtAnyAngle()
    {
        foreach (var deg in new[] { 90.0, 60, 35 })
        {
            var dir = new Vec2(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180));
            var sloppy = new Vec2(5, 6);                                         // soltou "mais ou menos" sobre a via
            var pts = new[] { sloppy + dir * 90, sloppy };
            var (_, e) = RoadConnection.MagnetEnds(new Polyline2(pts), new[] { Main });
            var tee = Road(0, pts[0], e!.Value.Point);
            var nodes = IntersectionGenerator.FindNodes(new[] { Main, tee });
            var n = Assert.Single(nodes);
            var L = IntersectionGenerator.Layout(new IntersectionDefinition { Node = n.Node }, new[] { Main, tee });
            Assert.Equal(3, L.Legs.Count);
            Assert.NotEmpty(L.Pavement);
        }
    }
}
