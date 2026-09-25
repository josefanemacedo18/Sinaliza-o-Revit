using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

public class DetailTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static BuildContext Ctx(IEnumerable<MarkingDefinition> defs, double scale = 200)
    {
        var list = defs.ToList();
        return new BuildContext
        {
            Catalog = Cat,
            ViewScale = scale,
            Lookup = id => list.FirstOrDefault(d => d.Id == id),
            AllDefinitions = () => list,
        };
    }

    [Fact]
    public void SignDetail_ScaledSymbolLeaderAndLabel()
    {
        var sign = new SignDefinition { Code = "R-1", Position = new Vec2(50, 20) };
        var det = new SignPlanDetailDefinition { SignId = sign.Id, SymbolMm = 12, OffsetMm = new Vec2(0, 20) };
        var geo = MarkingBuilder.Build(det, null, Ctx(new MarkingDefinition[] { sign }, 200));

        // 12 mm a 1:200 = 2,40 m de largura; centro 20 mm (4 m) ao norte do suporte.
        var face = geo.Pieces.Where(p => p.Color != MarkingColor.Vermelha || p.Shape.Area > 0.1).Select(p => p.Shape).ToList();
        var minX = face.Min(s => s.Bounds.Min.X);
        var maxX = face.Max(s => s.Bounds.Max.X);
        Assert.Equal(2.40, maxX - minX, 2);
        Assert.Equal(50, (minX + maxX) / 2, 2);
        Assert.Contains(geo.Annotations, a => a is AnnotationLine);
        var text = geo.Annotations.OfType<AnnotationText>().Single();
        Assert.StartsWith("R-1", text.Text);
        Assert.True(text.Position.X > 51.2);                      // ao lado, sem cruzar a chamada
        Assert.Equal(TextAlign.Left, text.Align);

        // Face plana sem sobreposição entre cores (regiões 2D).
        var red = PolygonOps.Union(geo.Pieces.Where(p => p.Color == MarkingColor.Vermelha).Select(p => p.Shape));
        var white = PolygonOps.Union(geo.Pieces.Where(p => p.Color == MarkingColor.Branca).Select(p => p.Shape));
        Assert.True(PolygonOps.Intersect(red, white).Sum(p => p.Area) < 1e-4);

        Assert.Equal(GrupoMarca.Detalhamento, MarkingBuilder.Describe(det, Cat).Group);
    }

    [Fact]
    public void SignDetail_MissingSign_Warns()
    {
        var det = new SignPlanDetailDefinition { SignId = "nao-existe" };
        var geo = MarkingBuilder.Build(det, null, Ctx(Array.Empty<MarkingDefinition>()));
        Assert.Empty(geo.Pieces);
        Assert.Contains(geo.Warnings, w => w.StartsWith(DetailGenerator.MissingTarget));
    }

    [Fact]
    public void Label_AutoTextAndLeader()
    {
        var line = new LinearMarkingDefinition { Code = "LFO-1" };
        var lb = new LabelDefinition { MarkingTargetId = line.Id, Anchor = new Vec2(0, 0), LabelPosition = new Vec2(5, 3) };
        var geo = MarkingBuilder.Build(lb, null, Ctx(new MarkingDefinition[] { line }, 100));
        var text = geo.Annotations.OfType<AnnotationText>().Single();
        Assert.StartsWith("LFO-1", text.Text);
        Assert.Equal(TextAlign.Left, text.Align);
        var leader = geo.Annotations.OfType<AnnotationLine>().Single();
        Assert.Equal(3, leader.Points.Count);

        lb.CustomText = "Texto livre";
        lb.LabelPosition = new Vec2(-5, 3);
        var custom = MarkingBuilder.Build(lb, null, Ctx(new MarkingDefinition[] { line }));
        var t2 = custom.Annotations.OfType<AnnotationText>().Single();
        Assert.Equal("Texto livre", t2.Text);
        Assert.Equal(TextAlign.Right, t2.Align);
    }

    [Fact]
    public void Legend_OneRowPerType_WithSamples()
    {
        var defs = new List<MarkingDefinition>
        {
            new LinearMarkingDefinition { Code = "LFO-1" },
            new LinearMarkingDefinition { Code = "LFO-1" },
            new HatchMarkingDefinition { Code = "ZPA" },
            new SignDefinition { Code = "R-1" },
            new SignDefinition { Code = "A-18" },
            new RampDefinition(),
            new DeviceMarkingDefinition { Code = "BAL-FLEX" },
        };
        var lg = new LegendDefinition { Position = new Vec2(0, 0) };
        defs.Add(lg);
        var geo = MarkingBuilder.Build(lg, null, Ctx(defs, 100));
        Assert.Equal(6, geo.UnitCount);
        var texts = geo.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("R-1"));
        Assert.Contains(texts, t => t.StartsWith("ZPA"));
        Assert.DoesNotContain(texts, t => t.StartsWith("LEGENDA –") && t != lg.Title);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Vermelha);   // amostra da placa R-1
        // Tudo dentro do quadro (canto superior esquerdo na origem).
        var width = (lg.SampleMm + lg.TextColumnMm) / 10.0;
        Assert.All(geo.Pieces, p => Assert.True(p.Shape.Bounds.Min.X >= -1e-6 && p.Shape.Bounds.Max.X <= width + 1e-6 && p.Shape.Bounds.Max.Y <= 1e-6));

        var onlySigns = new LegendDefinition { Horizontal = false, Physical = false };
        var g2 = MarkingBuilder.Build(onlySigns, null, Ctx(defs));
        Assert.Equal(2, g2.UnitCount);
    }

    [Fact]
    public void Annotations_ExcludedFromQuantities_AndRoundTrip()
    {
        var lg = new LegendDefinition { Title = "Q", Position = new Vec2(1, 2) };
        var back = (LegendDefinition)MarkingDefinition.FromJson(lg.ToJson())!;
        Assert.Equal("Q", back.Title);
        var det = new SignPlanDetailDefinition { SignId = "x", OffsetMm = new Vec2(5, -3) };
        var back2 = (SignPlanDetailDefinition)MarkingDefinition.FromJson(det.ToJson())!;
        Assert.Equal(new Vec2(5, -3), back2.OffsetMm);
        var lb = new LabelDefinition { Anchor = new Vec2(1, 1), LabelPosition = new Vec2(2, 2) };
        lb.Translate(new Vec2(10, 0), 0);
        Assert.Equal(11, lb.Anchor.X, 6);
    }

    [Fact]
    public void Ramp_TotalLowering_SolidsFollowSlope()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(0, 1) });
        var d = new RampDefinition { Type = TipoRampa.RebaixamentoTotal, Width = 2.4, SidewalkDepth = 2.0 };
        var geo = MarkingBuilder.Build(d, path, new BuildContext { Catalog = Cat });
        var solids = geo.Pieces.Where(p => p.Solid != null).ToList();
        Assert.True(solids.Count >= 3);                          // plataforma + 2 rampas laterais
        Assert.All(solids, s => Assert.True(s.Solid!.MaxZ <= d.Height + 0.02));
        var fp = RampGenerator.Footprint(d, path);
        Assert.True(fp.Contains(new Vec2(0, 1.0)));
        Assert.True(fp.Contains(new Vec2(1.2 + 0.5, 1.0)));      // rampa lateral
    }
}
