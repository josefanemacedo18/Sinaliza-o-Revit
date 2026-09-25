using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

public class ShapeTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    [Fact]
    public void Hatch_SquareWithBorder_StaysInsideRegion()
    {
        var region = Polygon2.Rectangle(new Vec2(0, 0), new Vec2(10, 4));
        var geo = HatchGenerator.Generate(region, Cat.Hachura("ZPA")!);
        Assert.True(geo.Pieces.Count > 3);
        Assert.True(geo.TotalArea < region.Area);
        foreach (var p in geo.Pieces)
            foreach (var v in p.Shape.Outer)
                Assert.True(v.X >= -1e-3 && v.X <= 10 + 1e-3 && v.Y >= -1e-3 && v.Y <= 4 + 1e-3);
        // contorno: anel de 0,15 m
        var border = geo.Pieces.OrderByDescending(p => p.Shape.Holes.Count).First();
        Assert.Single(border.Shape.Holes);
        Assert.Equal(10 * 4 - 9.7 * 3.7, border.Shape.Area, 2);
    }

    [Fact]
    public void Hatch_ConcaveRegion_ProducesSeparatePieces()
    {
        // Região em "U"
        var u = new Polygon2(new[] { new Vec2(0, 0), new Vec2(9, 0), new Vec2(9, 6), new Vec2(6, 6), new Vec2(6, 2), new Vec2(3, 2), new Vec2(3, 6), new Vec2(0, 6) });
        var geo = HatchGenerator.Generate(u, Cat.Hachura("ZPA")!, new HatchOptions { BorderWidth = 0, AngleDeg = 90, ReferenceDirection = Vec2.UnitX });
        Assert.True(geo.TotalArea < u.Area);
        Assert.All(geo.Pieces, p => Assert.True(u.Contains(p.Shape.Centroid)));
    }

    [Fact]
    public void Hatch_Chevron_And_Crossed()
    {
        var region = Polygon2.Rectangle(new Vec2(0, 0), new Vec2(20, 6));
        var chev = HatchGenerator.Generate(region, Cat.Hachura("ZPA-V")!);
        var cross = HatchGenerator.Generate(region, Cat.Hachura("MAC")!);
        Assert.NotEmpty(chev.Pieces);
        Assert.NotEmpty(cross.Pieces);
        Assert.True(cross.TotalArea < region.Area);
    }

    [Theory]
    [MemberData(nameof(AllShapes))]
    public void Symbols_AreValid(FormaSimbolo forma)
    {
        var local = SymbolBuilder.BuildLocal(forma, 5.0);
        Assert.NotEmpty(local.Figure);
        Assert.All(local.Figure, p => Assert.True(p.IsValid));
        var area = local.Figure.Sum(p => p.Area);
        Assert.True(area > 0.1, $"{forma}: área {area}");
    }

    public static IEnumerable<object[]> AllShapes() => Enum.GetValues<FormaSimbolo>().Select(f => new object[] { f });

    [Fact]
    public void Sia_BackgroundIsSquareMinusFigure()
    {
        var sia = Cat.Simbolo("SIA")!;
        var geo = SymbolBuilder.Build(sia, 1.2, new LocalFrame(Vec2.Zero, Vec2.UnitY));
        var blue = geo.AreaByColor[MarkingColor.Azul];
        var white = geo.AreaByColor[MarkingColor.Branca];
        Assert.Equal(1.44, blue + white, 3);
    }

    [Fact]
    public void Arrow_IsOrientedByFrame()
    {
        var def = Cat.Simbolo("PEM-F")!;
        var geo = SymbolBuilder.Build(def, 5, new LocalFrame(new Vec2(10, 10), Vec2.UnitX));
        var (min, max) = geo.Bounds!.Value;
        Assert.Equal(10, min.X, 2);
        Assert.Equal(15, max.X, 2);
    }

    [Fact]
    public void Text_BlockFont_BuildsLettersWithHoles()
    {
        var geo = TextGenerator.Generate("PARE", new TextOptions { Height = 1.6 }, new LocalFrame(Vec2.Zero, Vec2.UnitY), MarkingColor.Branca, new BlockFontProvider());
        Assert.Equal(4, geo.UnitCount);
        Assert.Contains(geo.Pieces, p => p.Shape.Holes.Count > 0);
        var (min, max) = geo.Bounds!.Value;
        Assert.Equal(1.6, max.Y - min.Y, 1);
        Assert.Equal(-(max.X), min.X, 2); // centralizada
    }

    [Fact]
    public void Text_MultiLine_FirstLineNearestDriver()
    {
        var geo = TextGenerator.Generate("SÓ\nÔNIBUS", new TextOptions { Height = 1.6, LineSpacing = 1.0 }, new LocalFrame(Vec2.Zero, Vec2.UnitY), MarkingColor.Branca, new BlockFontProvider());
        // "SÓ" (2 letras, estreita) embaixo; "ÔNIBUS" (larga) em cima
        var bottom = geo.Pieces.Where(p => p.Shape.Centroid.Y < 1.6).ToList();
        var top = geo.Pieces.Where(p => p.Shape.Centroid.Y > 2.6).ToList();
        Assert.True(bottom.Max(p => p.Shape.Bounds.Max.X) < top.Max(p => p.Shape.Bounds.Max.X));
    }

    [Fact]
    public void Parking_90_FillsCurb()
    {
        var curb = new Polyline2(new[] { new Vec2(0, 0), new Vec2(25, 0) });
        var geo = ParkingGenerator.Generate(curb, Cat.Vaga("MER-90")!, new ParkingOptions(), Cat, new BlockFontProvider());
        Assert.Equal(10, geo.UnitCount);
        // vagas à direita do caminho (y negativo)
        Assert.True(geo.Bounds!.Value.Min.Y < -4.9);
    }

    [Fact]
    public void Parking_Pcd_HasAisleHatchAndSymbol()
    {
        var curb = new Polyline2(new[] { new Vec2(0, 0), new Vec2(20, 0) });
        var geo = ParkingGenerator.Generate(curb, Cat.Vaga("PCD-90")!, new ParkingOptions { Count = 2 }, Cat, new BlockFontProvider());
        Assert.Equal(2, geo.UnitCount);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Azul);
    }

    [Fact]
    public void Parking_FillDoesNotOverlapSymbol()
    {
        var curb = new Polyline2(new[] { new Vec2(0, 0), new Vec2(10, 0) });
        var geo = ParkingGenerator.Generate(curb, Cat.Vaga("PCD-90")!, new ParkingOptions { Count = 1, FillColor = MarkingColor.Azul }, Cat, new BlockFontProvider());
        var shapes = geo.Pieces.Select(p => p.Shape).ToList();
        var union = PolygonOps.Union(shapes).Sum(p => p.Area);
        Assert.Equal(shapes.Sum(p => p.Area), union, 3);
    }

    [Fact]
    public void Parking_Angled_And_Parallel()
    {
        var curb = new Polyline2(new[] { new Vec2(0, 0), new Vec2(30, 0) });
        var g45 = ParkingGenerator.Generate(curb, Cat.Vaga("MER-45")!, new ParkingOptions(), Cat, null);
        var g0 = ParkingGenerator.Generate(curb, Cat.Vaga("MER-0")!, new ParkingOptions { RightSide = false }, Cat, null);
        Assert.True(g45.UnitCount >= 7);
        Assert.Equal(5, g0.UnitCount);
        Assert.True(g0.Bounds!.Value.Max.Y > 2.1);
    }
}
