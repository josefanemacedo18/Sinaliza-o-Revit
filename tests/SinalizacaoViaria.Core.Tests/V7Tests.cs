using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Hierarquia viária, conexão natural de vias, curvas e cul-de-sac ligado à via.</summary>
public class V7Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static (List<MarkingDefinition> Defs, IntersectionRoad Road) Road(int template, params Vec2[] axis)
    {
        var pr = PathReference.FromPoints(axis, 0);
        var defs = RoadTemplates.All[template].Create().Build(pr, new OutputSettings(), Cat);
        return (defs, new IntersectionRoad(defs.OfType<RoadPavementDefinition>().Single(), new Polyline2(axis)));
    }

    [Fact]
    public void Hierarchy_SetOnEveryRoadElementAndTemplates()
    {
        Assert.All(RoadTemplates.All, t => Assert.NotEqual(HierarquiaViaria.NaoDefinida, t.Create().Hierarchy));
        var (defs, _) = Road(0, Vec2.Zero, new Vec2(100, 0));
        Assert.All(defs, d => Assert.Equal(HierarquiaViaria.Local, d.Hierarchy));
        // Seção inferida (via antiga) herda a hierarquia do grupo.
        var inf = RoadSectionInference.Infer(defs.Where(d => d is not RoadPavementDefinition).ToList(), Cat)!;
        Assert.Equal(HierarquiaViaria.Local, inf.Hierarchy);
        Assert.Equal(30, Hierarquia.DefaultSpeed(HierarquiaViaria.Local));
        Assert.True(Hierarquia.Rank(HierarquiaViaria.Arterial) > Hierarquia.Rank(HierarquiaViaria.Coletora));
        // Round-trip JSON.
        var j = MarkingDefinition.FromJson(defs[1].ToJson())!;
        Assert.Equal(HierarquiaViaria.Local, j.Hierarchy);
    }

    [Fact]
    public void Quantities_SplitByHierarchy()
    {
        var a = Road(0, Vec2.Zero, new Vec2(100, 0));
        var b = Road(1, new Vec2(0, 50), new Vec2(250, 50));
        var items = a.Defs.Concat(b.Defs).Select(d => (d, MarkingBuilder.Build(d, d.Path == null ? null : new Polyline2(d.Path.Points), Ctx))).ToList();
        var rows = QuantityCalculator.Compute(items, Cat);
        Assert.Contains(rows, r => r.Hierarchy == HierarquiaViaria.Local);
        Assert.Contains(rows, r => r.Hierarchy == HierarquiaViaria.Coletora);
        var sum = QuantityCalculator.HierarchySummary(rows);
        Assert.Equal(HierarquiaViaria.Coletora, sum[0].Hierarchy);                // maior hierarquia primeiro
        Assert.Equal(250, sum.Single(s => s.Hierarchy == HierarquiaViaria.Coletora).RoadLength, 1);
        Assert.Equal(100, sum.Single(s => s.Hierarchy == HierarquiaViaria.Local).RoadLength, 1);
        Assert.True(sum.All(s => s.PavementArea > 100 && s.PaintedArea > 1));
        var csv = QuantityCalculator.ToCsv(rows);
        Assert.Contains("Hierarquia viária", csv);
        Assert.Contains("RESUMO POR HIERARQUIA VIÁRIA", csv);
        Assert.Contains("Via coletora", csv);
    }

    [Fact]
    public void Intersection_MainRoadFollowsHierarchy()
    {
        // Mesma seção (via local), mas uma é arterial: ela é a preferencial mesmo terminando no nó.
        var main = Road(0, new Vec2(-80, 0), new Vec2(80, 0));
        var stem = Road(0, new Vec2(0, 80), Vec2.Zero);
        foreach (var d in stem.Defs) d.Hierarchy = HierarquiaViaria.Arterial;
        var roads = new List<IntersectionRoad> { main.Road, stem.Road };
        var L = IntersectionGenerator.Layout(new IntersectionDefinition { Node = Vec2.Zero }, roads);
        Assert.Equal(1, L.Main);
        var ch = IntersectionGenerator.Children(new IntersectionDefinition(), L, new OutputSettings(), 0);
        // PARE nos dois ramos da via local; hierarquia gravada na sinalização.
        Assert.Equal(2, ch.Count(c => c is LinearMarkingDefinition { Code: "LRE" }));
        Assert.All(ch.OfType<SignDefinition>(), s => Assert.Equal(HierarquiaViaria.Local, s.Hierarchy));
        Assert.Equal(15, Hierarquia.CornerRadius(HierarquiaViaria.Rodovia, HierarquiaViaria.Local));
    }

    [Fact]
    public void Snap_ToEndAxisAndRoundabout()
    {
        var r = Road(1, Vec2.Zero, new Vec2(100, 0)).Road;
        var roads = new List<IntersectionRoad> { r };
        var end = RoadConnection.SnapPoint(new Vec2(102, 1.5), roads);
        Assert.Equal(TipoEncaixe.PontaDeVia, end.Kind);
        Assert.Equal(new Vec2(100, 0), end.Point);
        var ax = RoadConnection.SnapPoint(new Vec2(40, 5), roads);        // sobre a pista/calçada
        Assert.Equal(TipoEncaixe.EixoDeVia, ax.Kind);
        Assert.Equal(40, ax.Point.X, 6);
        Assert.Equal(0, ax.Point.Y, 6);
        Assert.Equal(TipoEncaixe.Livre, RoadConnection.SnapPoint(new Vec2(40, 30), roads).Kind);
        var rb = RoadConnection.SnapPoint(new Vec2(40, 60), roads, 4, new[] { (new Vec2(42, 58), 15.0) });
        Assert.Equal(TipoEncaixe.Rotatoria, rb.Kind);
        Assert.Equal(new Vec2(42, 58), rb.Point);
    }

    [Fact]
    public void Fillet_TangentArcsWithRadius()
    {
        var pts = new List<Vec2> { Vec2.Zero, new Vec2(100, 0), new Vec2(100, 100) };
        var pieces = RoadConnection.Fillet(pts, 30);
        Assert.Equal(3, pieces.Count);
        Assert.True(pieces[1].IsArc);
        Assert.True(pieces[1].A.DistanceTo(new Vec2(70, 0)) < 1e-6);
        Assert.True(pieces[1].B.DistanceTo(new Vec2(100, 30)) < 1e-6);
        var dense = RoadConnection.Densify(pieces);
        // Todos os pontos do arco a 30 m do centro (70, 30).
        foreach (var p in dense.Where(p => p.X > 70.01 && p.Y < 29.99)) Assert.Equal(30, p.DistanceTo(new Vec2(70, 30)), 2);
        var len = new Polyline2(dense).Length;
        Assert.Equal(70 + 70 + Math.PI * 30 / 2, len, 0);
        // Raio que não cabe é reduzido; zero mantém os cantos.
        Assert.Equal(2, RoadConnection.Fillet(new[] { Vec2.Zero, new Vec2(10, 0), new Vec2(10, 10) }, 0).Count);
        var tight = RoadConnection.Fillet(new[] { Vec2.Zero, new Vec2(10, 0), new Vec2(10, 10) }, 500);
        Assert.True(tight.Single(p => p.IsArc).A.X >= -1e-6);
    }

    [Fact]
    public void FreeEnds_AndContinuation()
    {
        var main = Road(1, new Vec2(-100, 0), new Vec2(100, 0)).Road;
        var tee = Road(0, new Vec2(0, 0), new Vec2(0, 80)).Road;                    // encaixado no eixo da principal
        var ends = RoadConnection.FreeEnds(tee, new[] { main });
        var e = Assert.Single(ends);
        Assert.True(e.AtEnd);
        Assert.Equal(new Vec2(0, 80), e.Point);
        Assert.True(IntersectionGenerator.NeedsIntersection(new[] { main, tee }, Vec2.Zero));

        // Continuação em linha reta: sem interseção. Em ângulo: geometria sem sinalização de controle.
        var cont = Road(1, new Vec2(100, 0), new Vec2(200, 0)).Road;
        Assert.False(IntersectionGenerator.NeedsIntersection(new[] { main, cont }, new Vec2(100, 0)));
        var bend = Road(1, new Vec2(100, 0), new Vec2(160, 60)).Road;
        Assert.True(IntersectionGenerator.NeedsIntersection(new[] { main, bend }, new Vec2(100, 0)));
        var L = IntersectionGenerator.Layout(new IntersectionDefinition { Node = new Vec2(100, 0) }, new[] { main, bend });
        Assert.Equal(2, L.Legs.Count);
        Assert.Empty(IntersectionGenerator.Children(new IntersectionDefinition(), L, new OutputSettings(), 0));
    }

    [Fact]
    public void CulDeSac_FittedToRoadEnd()
    {
        var (defs, road) = Road(0, Vec2.Zero, new Vec2(0, 120));
        var c = new CulDeSacDefinition { BulbRadius = 11, RoadId = road.Def.Id, AtRoadEnd = true };
        var cut = RoadConnection.FitCulDeSac(c, road, 0);
        Assert.Equal(road.Def.RightWidth + road.Def.LeftWidth, c.RoadWidth, 6);
        Assert.Equal(new Vec2(0, 120), c.PathRef.Points[1]);
        Assert.True(c.PathRef.Points[0].Y < 120 - 11);
        Assert.Equal(HierarquiaViaria.Local, c.Hierarchy);
        // O recorte cobre o fim da via (inclusive calçadas) e não o começo.
        Assert.Contains(cut, p => p.Contains(new Vec2(road.Def.TotalLeft - 0.2, 118)));
        Assert.DoesNotContain(cut, p => p.Contains(new Vec2(0, 60)));
        var geo = MarkingBuilder.Build(c, new Polyline2(c.PathRef.Points), Ctx);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Asfalto && p.Shape.Contains(new Vec2(9, 120)));

        var start = new CulDeSacDefinition { AtRoadEnd = false };
        RoadConnection.FitCulDeSac(start, road, 0);
        Assert.Equal(Vec2.Zero, start.PathRef.Points[1]);
        Assert.True(start.PathRef.Points[0].Y > 5);
    }
}
