using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada U: emenda pela ponta, várias linhas → várias vias, cotar seção, simulador lendo a sinalização.</summary>
public class V28Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static IntersectionDemo.Scene BendScene(int a, int b, double angleDeg)
    {
        var defs = new List<MarkingDefinition>();
        var paths = new Dictionary<string, Polyline2>();
        var roads = new List<IntersectionRoad>();
        var groups = new List<List<MarkingDefinition>>();
        void Road(int tpl, Vec2 p, Vec2 q)
        {
            var pr = PathReference.FromPoints(new[] { p, q }, 0);
            var g = RoadTemplates.All[tpl].Create().Build(pr, new OutputSettings(), Cat);
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) paths[m.Id] = axis;
            defs.AddRange(g);
            groups.Add(g);
            roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        Road(a, new Vec2(0, 80), Vec2.Zero);
        var u = new Vec2(Math.Cos(angleDeg * Math.PI / 180), Math.Sin(angleDeg * Math.PI / 180));
        Road(b, Vec2.Zero, u * 80);
        return IntersectionDemo.Create(new IntersectionDefinition { Node = Vec2.Zero, CornerRadius = 6 }, roads, groups, paths, defs, Cat);
    }

    [Theory]
    [InlineData(0, 0, -45)]
    [InlineData(1, 0, -80)]
    [InlineData(2, 2, -110)]
    public void Bend_ContinuationIsASmoothJointWithoutGaps(int a, int b, double ang)
    {
        var s = BendScene(a, b, ang);
        Assert.True(s.Layout.IsBend);
        var geo = IntersectionDemo.Build(s, new BuildContext { Catalog = Cat }, false);
        var asphalt = geo.Pieces.Where(p => p.Color == MarkingColor.Asfalto).Select(p => p.Shape).ToList();
        // Pista contínua: pontos nas faixas dos dois lados, ao longo de toda a emenda, estão sobre asfalto (sem cunha vazia).
        var bend = s.Layout.BendAxis!;
        for (var t = 0.0; t <= bend.Length; t += 0.5)
            foreach (var o in new[] { -2.8, 2.8 })
            {
                var p = bend.PointAt(t) + bend.TangentAt(t).PerpLeft * o;
                Assert.Contains(asphalt, x => x.Contains(p) || x.DistanceTo(p) < 0.05);
            }
        // Linhas pintadas continuam pela curva (filhos da emenda), sem travessias nem placas.
        var children = s.Definitions.Where(x => x.GroupId == s.Definitions.OfType<IntersectionDefinition>().First().Id).ToList();
        Assert.NotEmpty(children.OfType<LinearMarkingDefinition>());
        Assert.DoesNotContain(children, c => c is SignDefinition or RampDefinition);
    }

    [Fact]
    public void Bend_StraightContinuationOnlyWhenSectionsDiffer()
    {
        var same = new List<IntersectionRoad>
        {
            new(RoadTemplates.All[0].Create().Build(PathReference.FromPoints(new[] { new Vec2(-50, 0), Vec2.Zero }, 0), new OutputSettings(), Cat).OfType<RoadPavementDefinition>().First(), new Polyline2(new[] { new Vec2(-50, 0), Vec2.Zero })),
            new(RoadTemplates.All[0].Create().Build(PathReference.FromPoints(new[] { Vec2.Zero, new Vec2(50, 0) }, 0), new OutputSettings(), Cat).OfType<RoadPavementDefinition>().First(), new Polyline2(new[] { Vec2.Zero, new Vec2(50, 0) })),
        };
        Assert.False(IntersectionGenerator.NeedsIntersection(same, Vec2.Zero));
        var diff = new List<IntersectionRoad> { same[0], same[1] with { Def = RoadTemplates.All[1].Create().Build(PathReference.FromPoints(new[] { Vec2.Zero, new Vec2(50, 0) }, 0), new OutputSettings(), Cat).OfType<RoadPavementDefinition>().First() } };
        Assert.True(IntersectionGenerator.NeedsIntersection(diff, Vec2.Zero));
    }

    [Fact]
    public void RoadChains_GridOfLinesBecomesSeparateRoads()
    {
        IReadOnlyList<Vec2> L(params (double X, double Y)[] p) => p.Select(q => new Vec2(q.X, q.Y)).ToList();
        var curves = new List<IReadOnlyList<Vec2>>
        {
            L((0, 0), (100, 0)), L((100, 0), (200, 0)),          // avenida desenhada em dois trechos (cruzamento no meio)
            L((100, -80), (100, 0)), L((100, 0), (100, 80)),     // transversal em dois trechos
            L((200, 0), (260, 60)),                              // continuação em ângulo da avenida (deflexão de 45°)
            L((0, 0), (0, 90)),                                  // rua que sai em L da ponta da avenida... com a avenida já seguindo: nó de grau 2 → mesma via
            L((50, 0), (50, 60)),                                // T no meio de um trecho (a ponta não é nó de linhas)
        };
        var groups = RoadChains.Split(curves);
        // Avenida: 0 + 1 (alinhadas no nó de grau 4) + 4 (encontro só das duas) + 5 (L na outra ponta).
        Assert.Contains(groups, g => g.OrderBy(x => x).SequenceEqual(new[] { 0, 1, 4, 5 }));
        Assert.Contains(groups, g => g.OrderBy(x => x).SequenceEqual(new[] { 2, 3 }));
        Assert.Contains(groups, g => g.SequenceEqual(new[] { 6 }));
        Assert.Equal(3, groups.Count);
    }
}
