using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Interseções com esquinas inteiras, borda sobre a linha, referências a arestas e pisos.</summary>
public class V11Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static readonly Polyline2 Line20 = new(new[] { Vec2.Zero, new Vec2(20, 0) });

    private static IntersectionDemo.Scene Cross(double radius, int tpl = 1) =>
        IntersectionDemo.Create(new IntersectionDefinition { CornerRadius = radius, Crosswalks = true }, Cat, tpl, tpl, 90, false, 40);

    [Fact]
    public void Intersection_CornerFilletIsPavedEvenFarFromNode()
    {
        // Coletora (pista 2 × 6,80 m), raio 4 m: a curva vai até 10,8 m do nó ao longo de cada ramo.
        var L = Cross(4).Layout;
        Assert.Contains(L.Pavement, p => p.Contains(new Vec2(6.85, 9.5)));     // dentro da curva, a 11,7 m do nó
        Assert.DoesNotContain(L.Pavement, p => p.Contains(new Vec2(9.5, 9.5))); // canto da quadra
        Assert.Contains(L.Rebuild, p => p.Contains(new Vec2(6.85, 9.5)));
        // A calçada refeita contorna a curva (meio-fio na face da curva).
        Assert.Contains(L.Sidewalk, p => p.Contains(new Vec2(9.9, 9.9)) || p.Contains(new Vec2(9.5, 9.5)));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(3.0)]
    [InlineData(12.0)]
    public void Intersection_AnyRadiusMakesACurveAndNoSidewalkOnTheRoad(double r)
    {
        var s = Cross(r);
        var L = s.Layout;
        Assert.NotEmpty(L.Pavement);
        Assert.NotEmpty(L.Curb);
        // Ponto sobre a curva da esquina (bissetriz): pavimentado com o raio pedido, e não com outro.
        var w = 6.8;
        var c = new Vec2(w + r, w + r);
        var onCurve = c + (Vec2.Zero - c).Normalized() * (r * 0.9);    // um pouco para dentro do pavimento
        var outCurve = c + (Vec2.Zero - c).Normalized() * (r * 1.1 + 0.2);
        Assert.DoesNotContain(L.Pavement, p => p.Contains(onCurve));
        Assert.Contains(L.Pavement, p => p.Contains(outCurve));
        // Nenhuma calçada (da interseção) sobre a pista das duas vias.
        var carriage = PolygonOps.Union(L.Roads.SelectMany(x => RoadGenerator.Band(x.Axis, -x.Def.RightWidth + 0.05, x.Def.LeftWidth - 0.05)));
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(L.Sidewalk, carriage)) < 0.05);
        // Nem as calçadas das próprias vias (recortadas) atravessando a outra pista.
        var geo = IntersectionDemo.Build(s, Ctx, false);
        var walks = geo.Pieces.Where(p => p.Color == MarkingColor.Concreto && p.Thickness >= 0.1).Select(p => p.Shape).ToList();
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(walks, carriage)) < 0.2);
    }

    [Fact]
    public void Intersection_StraightSideKeepsRoadSidewalk()
    {
        // T: do lado sem ramo, a zona vai só até o bordo – a calçada da via principal continua a dela.
        var s = IntersectionDemo.Create(new IntersectionDefinition { CornerRadius = 6 }, Cat, 1, 0, 90, true, 40);
        var L = s.Layout;
        var r0 = L.Roads[0].Def;
        Assert.DoesNotContain(L.Rebuild, p => p.Contains(new Vec2(0, -(r0.RightWidth + 1.0))));
        Assert.Contains(L.Rebuild, p => p.Contains(new Vec2(r0.LeftWidth * 0 + 8, r0.LeftWidth + 1.0)));
    }

    [Fact]
    public void SignedDistance_IsPositiveOnTheLeft()
    {
        Assert.Equal(2, Line20.SignedDistance(new Vec2(5, 2)), 9);
        Assert.Equal(-3, Line20.SignedDistance(new Vec2(5, -3)), 9);
        var (st, d) = Line20.Project(new Vec2(7, 1));
        Assert.Equal(7, st, 9);
        Assert.Equal(1, d, 9);
    }

    [Theory]
    [InlineData(Justificacao.Esquerda, 0.0)]
    [InlineData(Justificacao.Direita, 0.0)]
    [InlineData(Justificacao.Esquerda, 0.5)]
    [InlineData(Justificacao.Direita, -0.4)]
    public void Justify_PutsTheEdgeOnTheLine(Justificacao j, double offset)
    {
        var d = new LinearMarkingDefinition { Code = "CALCADA", WidthOverride = 2.5, Justify = j, Offset = offset };
        var geo = MarkingBuilder.Build(d, Line20, Ctx);
        var (lo, hi) = MarkingBuilder.LateralExtent(geo, Line20);
        Assert.Equal(2.5, hi - lo, 2);
        if (j == Justificacao.Esquerda) Assert.Equal(offset, lo, 2);
        else Assert.Equal(offset, hi, 2);
    }

    [Fact]
    public void Justify_WorksOnCurvesAndDevicesAndCenterIsUnchanged()
    {
        var arc = new Polyline2(CurveTools.Arc(new Vec2(0, 0), 20, 0, Math.PI / 2));
        var geo = MarkingBuilder.Build(new LinearMarkingDefinition { Code = "MEIO-FIO", Justify = Justificacao.Direita }, arc, Ctx);
        var (lo, hi) = MarkingBuilder.LateralExtent(geo, arc);
        Assert.InRange(hi, -0.02, 0.02);
        Assert.True(lo < -0.05);
        var nj = MarkingBuilder.Build(new DeviceMarkingDefinition { Code = "NJ", Justify = Justificacao.Esquerda }, Line20, Ctx);
        Assert.InRange(MarkingBuilder.LateralExtent(nj, Line20).Lo, -0.02, 0.02);
        var c = MarkingBuilder.Build(new LinearMarkingDefinition { Code = "CALCADA", WidthOverride = 2 }, Line20, Ctx);
        var (clo, chi) = MarkingBuilder.LateralExtent(c, Line20);
        Assert.Equal(-1, clo, 2);
        Assert.Equal(1, chi, 2);
        // Símbolos pontuais não usam posição na linha.
        Assert.False(MarkingBuilder.SupportsJustify(new SymbolMarkingDefinition()));
    }

    [Fact]
    public void BikeLane_JustifiedToOneSide()
    {
        var s = new BikeLaneSetup { Type = TipoCiclo.CiclofaixaUnidirecional, Width = 1.5, Justify = Justificacao.Esquerda, Segregation = null };
        var geo = new MarkingGeometry();
        foreach (var d in s.Build(PathReference.FromPoints(Line20.Points, 0), new OutputSettings())) geo.Merge(MarkingBuilder.Build(d, Line20, Ctx));
        var (lo, hi) = MarkingBuilder.LateralExtent(geo, Line20);
        Assert.InRange(lo, -0.02, 0.05);
        Assert.InRange(hi, 1.45, 1.55);
    }

    [Fact]
    public void PathReference_EdgeIdsAndCacheSurviveCloneAndJson()
    {
        var edge = PathReference.EdgePrefix + "3f2a-00000154:0:INSTANCE:x:1:LINEAR";
        Assert.True(PathReference.IsEdge(edge));
        Assert.False(PathReference.IsEdge("3f2a-00000154"));
        Assert.Equal("3f2a-00000154", PathReference.OwnerOf(edge));
        Assert.Equal("abc", PathReference.OwnerOf("abc"));
        var pr = PathReference.FromElements(new[] { edge });
        pr.Cache = new List<List<Vec2>> { new() { Vec2.Zero, new Vec2(5, 0) } };
        var c = pr.Clone();
        c.Cache![0].Add(new Vec2(9, 9));
        Assert.Equal(2, pr.Cache[0].Count);
        var d = new LinearMarkingDefinition { Code = "MEIO-FIO", PathRef = pr, Justify = Justificacao.Direita };
        var back = (LinearMarkingDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.Equal(Justificacao.Direita, back.Justify);
        Assert.Equal(edge, back.PathRef.ElementIds[0]);
        Assert.Equal(new Vec2(5, 0), back.PathRef.Cache![0][1]);
        Assert.Equal(Justificacao.Centro, ((LinearMarkingDefinition)MarkingDefinition.FromJson(new LinearMarkingDefinition().ToJson())!).Justify);
    }

    [Fact]
    public void SplitHoles_RemovesHolesWithoutLosingArea()
    {
        var outer = Polygon2.Rectangle(Vec2.Zero, new Vec2(10, 10));
        var withHoles = PolygonOps.Difference(new[] { outer }, new[]
        {
            Polygon2.Rectangle(new Vec2(2, 2), new Vec2(4, 4)), Polygon2.Rectangle(new Vec2(6, 6), new Vec2(8, 8)),
        }).Single();
        Assert.Equal(2, withHoles.Holes.Count);
        var parts = PolygonOps.SplitHoles(withHoles);
        Assert.All(parts, p => Assert.Empty(p.Holes));
        Assert.Equal(withHoles.Area, parts.Sum(p => p.Area), 6);
        var clean = PolygonOps.Clean(new[] { outer });
        Assert.Equal(100, clean.Sum(p => p.Area), 3);
    }

    [Fact]
    public void AreaOverrides_ReplaceComputedAreaInQuantities()
    {
        var geo = new MarkingGeometry();
        geo.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(Vec2.Zero, new Vec2(10, 7)), MarkingColor.Asfalto) { Thickness = 0.05 });
        Assert.Equal(70, geo.AreaByColor[MarkingColor.Asfalto], 6);
        geo.AreaOverrides[MarkingColor.Asfalto] = 55;
        Assert.Equal(55, geo.AreaByColor[MarkingColor.Asfalto], 6);
        var rows = Quantities.QuantityCalculator.Compute(new[] { ((MarkingDefinition)new RoadPavementDefinition(), geo) }, Cat);
        Assert.Equal(55, rows.Sum(r => r.Area), 6);
    }
}
