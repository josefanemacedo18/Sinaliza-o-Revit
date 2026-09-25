using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

public class LinearTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static Polyline2 Straight(double len) => new(new[] { new Vec2(0, 0), new Vec2(len, 0) });

    [Fact]
    public void Intervals_Start()
    {
        var iv = LinearPatternGenerator.Intervals(0, 20, new[] { 2.0, 4.0 }, AlinhamentoPadrao.Inicio);
        Assert.Equal(new[] { (0.0, 2.0), (6.0, 8.0), (12.0, 14.0), (18.0, 20.0) }, iv);
    }

    [Fact]
    public void Intervals_End_MirrorsStart()
    {
        var iv = LinearPatternGenerator.Intervals(0, 21, new[] { 2.0, 4.0 }, AlinhamentoPadrao.Fim);
        Assert.Equal(21.0, iv[^1].S1, 6);
        Assert.Equal(19.0, iv[^1].S0, 6);
    }

    [Fact]
    public void Intervals_Center_IsSymmetric()
    {
        var iv = LinearPatternGenerator.Intervals(0, 10, new[] { 0.4, 0.6 }, AlinhamentoPadrao.Centro, dropPartials: true);
        var first = iv[0].S0 - 0;
        var last = 10 - iv[^1].S1;
        Assert.Equal(first, last, 6);
        Assert.All(iv, x => Assert.Equal(0.4, x.S1 - x.S0, 6));
    }

    [Fact]
    public void Intervals_Fit_DashesAtBothEnds()
    {
        var iv = LinearPatternGenerator.Intervals(0, 10, new[] { 0.5, 0.5 }, AlinhamentoPadrao.Ajustar);
        Assert.Equal(0.0, iv[0].S0, 6);
        Assert.Equal(10.0, iv[^1].S1, 6);
    }

    [Fact]
    public void Intervals_NonRepeating_Sequence()
    {
        var iv = LinearPatternGenerator.Intervals(0, 100, new[] { 0.4, 5.0, 0.4, 4.0, 0.4 }, AlinhamentoPadrao.Inicio, repeat: false);
        Assert.Equal(3, iv.Count);
        Assert.Equal(9.8, iv[2].S0, 6);
    }

    [Fact]
    public void Lfo2_AreaMatchesPaintedLength()
    {
        var t = Cat.Linear("LFO-2")!;
        var geo = LinearPatternGenerator.Generate(Straight(60), t, t.VariantePorVelocidade(50)!);
        Assert.Equal(10, geo.Pieces.Count);
        Assert.Equal(20.0, geo.PaintedLength, 6);
        Assert.Equal(20.0 * 0.10, geo.TotalArea, 3);
        Assert.All(geo.Pieces, p => Assert.Equal(MarkingColor.Amarela, p.Color));
    }

    [Fact]
    public void Lfo3_TwoParallelStripesWithGap()
    {
        var t = Cat.Linear("LFO-3")!;
        var geo = LinearPatternGenerator.Generate(Straight(10), t, t.Variantes[0]);
        Assert.Equal(2, geo.Pieces.Count);
        var ys = geo.Pieces.Select(p => p.Shape.Centroid.Y).OrderBy(y => y).ToArray();
        Assert.Equal(-0.10, ys[0], 4);
        Assert.Equal(0.10, ys[1], 4);
    }

    [Fact]
    public void WidthOverride_KeepsInnerGapOfDoubleLine()
    {
        var t = Cat.Linear("LFO-3")!;
        var geo = LinearPatternGenerator.Generate(Straight(10), t, t.Variantes[0], new LinearOptions { WidthOverride = 0.15 });
        var top = geo.Pieces.Select(p => p.Shape).OrderBy(p => p.Centroid.Y).Last();
        Assert.Equal(0.05, top.Bounds.Min.Y, 4);   // borda interna preservada
        Assert.Equal(0.20, top.Bounds.Max.Y, 4);
    }

    [Fact]
    public void Lfo4_InvertSides_SwapsContinuousLine()
    {
        var t = Cat.Linear("LFO-4")!;
        var v = t.Variantes[0];
        var normal = LinearPatternGenerator.Generate(Straight(30), t, v);
        var inverted = LinearPatternGenerator.Generate(Straight(30), t, v, new LinearOptions { InvertSides = true });
        double LongestY(MarkingGeometry g) => g.Pieces.OrderByDescending(p => p.Shape.Area).First().Shape.Centroid.Y;
        Assert.True(LongestY(normal) > 0);
        Assert.True(LongestY(inverted) < 0);
    }

    [Fact]
    public void Offset_MovesLineLaterally()
    {
        var t = Cat.Linear("LBO")!;
        var geo = LinearPatternGenerator.Generate(Straight(10), t, t.Variantes[0], new LinearOptions { Offset = -3.5 });
        Assert.Equal(-3.5, geo.Pieces[0].Shape.Centroid.Y, 4);
    }

    [Fact]
    public void Crosswalk_Ftp1_BarsAreFullAndCentered()
    {
        var t = Cat.Linear("FTP-1")!;
        var geo = LinearPatternGenerator.Generate(Straight(10), t, t.Variantes[0]);
        Assert.True(geo.Pieces.Count >= 9);
        Assert.All(geo.Pieces, p => Assert.Equal(0.40 * 4.00, p.Shape.Area, 3));
        var xs = geo.Pieces.Select(p => p.Shape.Bounds.Min.X).Min();
        var xe = geo.Pieces.Select(p => p.Shape.Bounds.Max.X).Max();
        Assert.Equal(xs, 10 - xe, 3);
    }

    [Fact]
    public void Studs_CountUnits()
    {
        var t = Cat.Linear("TAC-A")!;
        var geo = LinearPatternGenerator.Generate(Straight(160), t, t.Variantes[0]);
        Assert.Equal(10, geo.UnitCount);
        Assert.All(geo.Pieces, p => Assert.Equal(0.02, p.Thickness, 6));
    }

    [Fact]
    public void CurvedPath_ProducesValidPieces_AndSplitsForDrape()
    {
        var arc = new Polyline2(CurveTools.Arc(Vec2.Zero, 30, 0, Math.PI / 2));
        var t = Cat.Linear("LBO")!;
        var geo = LinearPatternGenerator.Generate(arc, t, t.Variantes[0], new LinearOptions { MaxPieceLength = 2 });
        Assert.True(geo.Pieces.Count >= 23);
        Assert.Equal(arc.Length * 0.10, geo.TotalArea, 2);
    }

    [Fact]
    public void Setbacks_ReducePaintedLength()
    {
        var t = Cat.Linear("LBO")!;
        var geo = LinearPatternGenerator.Generate(Straight(50), t, t.Variantes[0], new LinearOptions { StartSetback = 5, EndSetback = 10 });
        Assert.Equal(35, geo.PaintedLength, 6);
    }

    [Fact]
    public void WidthOutsideRange_ProducesWarning()
    {
        var t = Cat.Linear("LBO")!;
        var geo = LinearPatternGenerator.Generate(Straight(10), t, t.Variantes[0], new LinearOptions { WidthOverride = 0.5 });
        Assert.NotEmpty(geo.Warnings);
    }

    [Fact]
    public void Footprint_OfParallelCrosswalk()
    {
        var t = Cat.Linear("FTP-2")!;
        Assert.Equal(4.8, LinearPatternGenerator.Footprint(t.Variantes[0]), 6);
    }
}
