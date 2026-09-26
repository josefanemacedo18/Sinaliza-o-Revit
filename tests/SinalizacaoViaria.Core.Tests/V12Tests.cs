using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Curvas do eixo, rampas personalizadas, sobreposição, novos bloqueios.</summary>
public class V12Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    [Fact]
    public void FilletCorners_RoundsSharpCornersAndKeepsArcs()
    {
        var pts = new[] { new Vec2(0, 0), new Vec2(30, 0), new Vec2(30, 30) };
        var f = CurveTools.FilletCorners(pts, 10);
        Assert.Equal(pts[0], f[0]);
        Assert.Equal(pts[^1], f[^1]);
        Assert.DoesNotContain(f, p => p.AlmostEquals(new Vec2(30, 0), 1e-3));   // canto vivo sumiu
        var mid = f[f.Count / 2];
        Assert.Equal(10, mid.DistanceTo(new Vec2(20, 10)), 1);                  // arco de raio 10 centrado em (20,10)
        // Arco já discretizado (deflexões pequenas) fica igual.
        var arc = CurveTools.Arc(Vec2.Zero, 50, 0, Math.PI / 2);
        Assert.Equal(arc.Count, CurveTools.FilletCorners(arc, 10).Count);
        // Trecho curto: raio reduzido para caber.
        var shortSeg = CurveTools.FilletCorners(new[] { new Vec2(0, 0), new Vec2(2, 0), new Vec2(2, 30) }, 50);
        Assert.True(shortSeg[0].AlmostEquals(Vec2.Zero, 1e-9));
    }

    [Fact]
    public void RoadAxisRadius_IsAtLeastHalfWidthAndInnerEdgeCurves()
    {
        var d = new RoadPavementDefinition { RightWidth = 3.5, LeftWidth = 3.5 };
        var pr = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(40, 0), new Vec2(40, 40) }, 0);
        var defs = RoadConnection.BuildCarriageway(d, pr, new OutputSettings(), "LFO-2", true, 40);
        var r = RoadSetup.ApplyAxisRadius(defs, 0);
        Assert.Equal(5.0, r, 6);
        Assert.All(defs.Where(x => x.Path != null), x => Assert.Equal(5.0, x.Path!.SmoothRadius));
        var axis = new Polyline2(PathReference.Smooth(pr.Points, r, false));
        var pav = MarkingBuilder.Build(defs.OfType<RoadPavementDefinition>().Single(), axis, Ctx);
        // O canto interno (40-3,5 ; 3,5) não é mais um vértice vivo: a pista contorna com raio 1,5 m.
        // Borda externa em arco: o canto (43,5 ; -3,5) do "L" vivo não é mais pavimentado.
        Assert.DoesNotContain(pav.Pieces, p => p.Shape.Contains(new Vec2(43.3, -3.3)));
        Assert.Contains(pav.Pieces, p => p.Shape.Contains(new Vec2(40, 0)) || p.Shape.Contains(new Vec2(38, 1)));
        Assert.True(r >= RoadSetup.MinAxisRadius(3.5));
        Assert.Equal(pr.SmoothRadius, pr.Clone().SmoothRadius);
    }

    [Fact]
    public void Ramp_CustomLengthsLipAndLanding()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(0, 1) });
        var d = new RampDefinition { Width = 1.5, Height = 0.15, Length = 2.0, FlareLength = 0.9, LipHeight = 0.01, LandingDepth = 0.6 };
        var (w, len, flare) = RampGenerator.Dimensions(d);
        Assert.Equal(2.0, len, 6);
        Assert.Equal(0.9, flare, 6);
        var fp = RampGenerator.Footprint(d, path);
        Assert.True(fp.Contains(new Vec2(0, 2.4)));                          // patamar
        var geo = RampGenerator.Generate(d, path);
        Assert.Contains(geo.Warnings, x => x.Contains("5 mm"));
        Assert.DoesNotContain(geo.Warnings, x => x.Contains("8,33"));        // (0,15-0,01)/2 = 7 %
        var steep = RampGenerator.Generate(new RampDefinition { Length = 1.0 }, path);
        Assert.Contains(steep.Warnings, x => x.Contains("8,33"));
        var mat = RampGenerator.Generate(new RampDefinition { RampColor = MarkingColor.Bloquete }, path);
        Assert.Contains(mat.Pieces, p => p.Color == MarkingColor.Bloquete);
    }

    [Theory]
    [InlineData("ESF")] [InlineData("FLOR-BLOQ")] [InlineData("CONE")] [InlineData("CAVALETE")] [InlineData("TAMBOR")] [InlineData("GRADIL")]
    public void NewDevices_AreModelled(string code)
    {
        var dv = Cat.Dispositivo(code)!;
        var line = new Polyline2(new[] { Vec2.Zero, new Vec2(10, 0) });
        var geo = MarkingBuilder.Build(new DeviceMarkingDefinition { Code = code }, line, Ctx);
        Assert.True(geo.Pieces.Count >= 3);
        Assert.All(geo.Pieces, p => Assert.True(p.Solid != null || p.Profile != null));
        var top = geo.Pieces.Max(p => p.Solid?.MaxZ ?? p.Thickness);
        Assert.InRange(top, dv.Altura * 0.85, dv.Altura * (code == "FLOR-BLOQ" ? 1.7 : 1.25));   // floreira: plantas acima da borda
    }

    [Fact]
    public void Overlay_IsSerializedAndSetOnCrosswalks()
    {
        var defs = new CrosswalkSetup().Build(Vec2.Zero, new Vec2(0, 7), 0, new OutputSettings(), 4, 0.4);
        Assert.True(defs[0].Overlay);
        Assert.All(defs.Skip(1), d => Assert.False(d.Overlay));
        Assert.False(new CrosswalkSetup { Overlay = false }.Build(Vec2.Zero, new Vec2(0, 7), 0, new OutputSettings(), 4, 0.4)[0].Overlay);
        var back = MarkingDefinition.FromJson(defs[0].ToJson())!;
        Assert.True(back.Overlay);
    }
}
