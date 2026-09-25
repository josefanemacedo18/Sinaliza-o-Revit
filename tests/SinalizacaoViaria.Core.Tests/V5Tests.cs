using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

public class V5Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(9)]
    public void Inference_RebuildsSectionFromRoadMarkings(int template)
    {
        var defs = RoadTemplates.All[template].Create().Build(new PathReference { ElementIds = { "eixo" } }, new OutputSettings(), Cat);
        var real = defs.OfType<RoadPavementDefinition>().Single();
        var inf = RoadSectionInference.Infer(defs.Where(d => d is not RoadPavementDefinition).ToList(), Cat)!;
        Assert.NotNull(inf);
        Assert.Equal(real.RightWidth, inf.RightWidth, 2);
        Assert.Equal(real.LeftWidth, inf.LeftWidth, 2);
        Assert.Equal(real.RightSidewalk, inf.RightSidewalk, 2);
        Assert.Equal(real.LeftSidewalk, inf.LeftSidewalk, 2);
        Assert.Equal(real.Gaps.Count(g => g.Median), inf.Gaps.Count(g => g.Median));
        Assert.Equal(real.TwoWay, inf.TwoWay);
        Assert.Equal(real.GroupId, inf.GroupId);
        Assert.Equal("eixo", inf.PathRef.ElementIds.Single());
    }

    [Fact]
    public void Inference_IgnoresNonRoadGroups()
    {
        var cw = new CrosswalkSetup().Build(Vec2.Zero, new Vec2(0, 7), 0, new OutputSettings(), 4, 0.4);
        Assert.Null(RoadSectionInference.Infer(cw, Cat));
        Assert.Null(RoadSectionInference.Infer(new List<MarkingDefinition> { new LinearMarkingDefinition { Code = "LBO" } }, Cat));
    }

    [Fact]
    public void Intersection_WorksWithRoadsFromPreviousVersion()
    {
        // Vias antigas: sem o pavimento que registra a seção.
        var a = RoadTemplates.All[0].Create().Build(new PathReference { ElementIds = { "a" } }, new OutputSettings(), Cat).Where(d => d is not RoadPavementDefinition).ToList();
        var b = RoadTemplates.All[1].Create().Build(new PathReference { ElementIds = { "b" } }, new OutputSettings(), Cat).Where(d => d is not RoadPavementDefinition).ToList();
        var pa = RoadSectionInference.Infer(a, Cat)!;
        var pb = RoadSectionInference.Infer(b, Cat)!;
        // Eixos em ângulo (como no projeto do usuário), com vértice.
        var roads = new List<IntersectionRoad>
        {
            new(pa, new Polyline2(new[] { new Vec2(-60, -10), new Vec2(-20, 0), new Vec2(60, 20) })),
            new(pb, new Polyline2(new[] { new Vec2(-30, 60), new Vec2(30, -50) })),
        };
        var nodes = IntersectionGenerator.FindNodes(roads);
        Assert.Single(nodes);
        var L = IntersectionGenerator.Layout(new IntersectionDefinition { Node = nodes[0].Node }, roads);
        Assert.Equal(4, L.Legs.Count);
        Assert.NotEmpty(L.Pavement);
        Assert.NotEmpty(L.Curb);
        var lane = a.OfType<LinearMarkingDefinition>().First(l => l.Code.StartsWith("LFO"));
        Assert.NotEmpty(IntersectionGenerator.CutsFor(lane, 0, L));
    }
}
