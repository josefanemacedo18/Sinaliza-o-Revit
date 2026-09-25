using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

public class DefinitionTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    [Fact]
    public void Definitions_RoundTripPolymorphicJson()
    {
        MarkingDefinition[] defs =
        {
            new LinearMarkingDefinition { Code = "LFO-4", InvertSides = true, PathRef = PathReference.FromElements(new[] { "abc" }), PatternOverride = new[] { 3.0, 9.0 } },
            new HatchMarkingDefinition { Code = "ZPA", ReferenceDirection = new Vec2(1, 0) },
            new SymbolMarkingDefinition { Code = "PEM-FD", Position = new Vec2(1, 2), Direction = new Vec2(0, 1) },
            new TextMarkingDefinition { Text = "SÓ\nÔNIBUS" },
            new ParkingMarkingDefinition { Code = "PCD-90", Count = 3 },
        };
        foreach (var d in defs)
        {
            var json = d.ToJson();
            var back = MarkingDefinition.FromJson(json)!;
            Assert.Equal(d.GetType(), back.GetType());
            Assert.Equal(d.Id, back.Id);
            Assert.Equal(json, back.ToJson());
        }
    }

    [Fact]
    public void Builder_UsesSpeedToChooseVariant()
    {
        var d = new LinearMarkingDefinition { Code = "LFO-2", Speed = 100 };
        var geo = MarkingBuilder.Build(d, new Polyline2(new[] { Vec2.Zero, new Vec2(32, 0) }), Ctx);
        Assert.Equal(2, geo.Pieces.Count); // 4 × 12 m
        Assert.Equal(0.15 * 8, geo.TotalArea, 3);
    }

    [Fact]
    public void Builder_MissingPath_Warns()
    {
        var geo = MarkingBuilder.Build(new LinearMarkingDefinition { Code = "LBO" }, null, Ctx);
        Assert.Empty(geo.Pieces);
        Assert.NotEmpty(geo.Warnings);
    }

    [Fact]
    public void Builder_HatchFromClosedPath()
    {
        var path = new Polyline2(new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 3), new Vec2(0, 3) }, closed: true);
        var geo = MarkingBuilder.Build(new HatchMarkingDefinition { Code = "ZPA" }, path, Ctx);
        Assert.NotEmpty(geo.Pieces);
    }

    [Fact]
    public void RoadSetup_TwoWayTwoLanesEachSide()
    {
        var setup = new RoadSetup { RightLanes = { 3.5 }, LeftLanes = { 3.5 }, Center = CenterTreatment.LFO2 };
        var defs = setup.Build(PathReference.FromElements(new[] { "eixo" }), new OutputSettings());
        Assert.Equal(5, defs.Count);
        Assert.Single(defs, d => d.Code == "LFO-2");
        Assert.Equal(2, defs.Count(d => d.Code == "LMS-2"));
        Assert.Equal(2, defs.Count(d => d.Code == "LBO"));
        Assert.Contains(defs, d => d.Code == "LMS-2" && Math.Abs(d.Offset + 3.5) < 1e-9);
        Assert.Contains(defs, d => d.Code == "LBO" && Math.Abs(d.Offset - (7.0 - 0.10)) < 1e-9);
        Assert.Single(defs.Select(d => d.GroupId).Distinct());
    }

    [Fact]
    public void RoadSetup_OneWay()
    {
        var setup = new RoadSetup { TwoWay = false, OneWayLanes = new() { 3.5, 3.5, 3.5 } };
        var defs = setup.Build(new PathReference(), new OutputSettings());
        Assert.Equal(4, defs.Count);
        Assert.Contains(defs, d => d.Code == "LMS-2" && Math.Abs(d.Offset - 1.75) < 1e-9);
        Assert.Contains(defs, d => d.Code == "LMS-2" && Math.Abs(d.Offset + 1.75) < 1e-9);
    }

    [Fact]
    public void RoadSetup_ParseWidths()
    {
        Assert.Equal(new[] { 3.5, 3.3, 3.0 }, RoadSetup.ParseWidths("3,50; 3.3  3"));
    }

    [Fact]
    public void Crosswalk_GeneratesStopLinesOnHalfRoad()
    {
        var cw = new CrosswalkSetup();
        var defs = cw.Build(new Vec2(0, 0), new Vec2(0, 7), 0, new OutputSettings(), 4.0, 0.4);
        Assert.Equal(3, defs.Count);
        var stops = defs.OfType<LinearMarkingDefinition>().Where(d => d.Code == "LRE").ToList();
        Assert.Equal(2, stops.Count);
        foreach (var s in stops)
        {
            var len = s.PathRef.Points[0].DistanceTo(s.PathRef.Points[1]);
            Assert.Equal(3.5, len, 6);
            Assert.Equal(2.0 + 1.6 + 0.2, Math.Abs(s.PathRef.Points[0].X), 6);
        }
    }

    [Fact]
    public void Chainer_JoinsReversedPieces()
    {
        var pieces = new List<IReadOnlyList<Vec2>>
        {
            new[] { new Vec2(0, 0), new Vec2(10, 0) },
            new[] { new Vec2(20, 5), new Vec2(10, 0) },
            new[] { new Vec2(-5, 0), new Vec2(0, 0) },
        };
        var r = PathChainer.Chain(pieces);
        Assert.True(r.AllConnected);
        Assert.Equal(4, r.Chains[0].Count);
    }

    [Fact]
    public void Quantities_AggregateByCodeAndColor_AndExportCsv()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(60, 0) });
        var d1 = new LinearMarkingDefinition { Code = "LFO-2", Speed = 50 };
        var d2 = new LinearMarkingDefinition { Code = "LFO-2", Speed = 50 };
        var d3 = new LinearMarkingDefinition { Code = "LBO", Speed = 50 };
        var items = new[] { d1, d2, d3 }.Select(d => ((MarkingDefinition)d, MarkingBuilder.Build(d, path, Ctx)));
        var rows = QuantityCalculator.Compute(items, Cat);
        Assert.Equal(2, rows.Count);
        var lfo = rows.Single(r => r.Code == "LFO-2");
        Assert.Equal(4.0, lfo.Area, 3);
        Assert.Equal(2, lfo.Elements);
        var summary = QuantityCalculator.Summary(rows);
        Assert.Equal(2, summary.Count);
        var csv = QuantityCalculator.ToCsv(rows, summary);
        Assert.Contains("LFO-2", csv);
        Assert.Contains("4,00", csv);
    }

    [Fact]
    public void LocalFrame_FromRotation()
    {
        var f = LocalFrame.FromRotation(Vec2.Zero, 0);
        var p = f.ToWorld(new Vec2(0, 1));
        Assert.Equal(0, p.X, 9);
        Assert.Equal(1, p.Y, 9);
        var r = f.ToWorld(new Vec2(1, 0));
        Assert.Equal(1, r.X, 9); // +X local = direita do condutor
    }
}
