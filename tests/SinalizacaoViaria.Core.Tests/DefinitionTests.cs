using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
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

    private static ElementoSecao Lane(double w = 3.5) => new() { Tipo = TipoElementoSecao.FaixaRolamento, Largura = w };

    [Fact]
    public void RoadSetup_TwoWayTwoLanesEachSide()
    {
        var setup = new RoadSetup { Right = { Lane(), Lane() }, Left = { Lane(), Lane() }, Center = CenterTreatment.LFO2 };
        var defs = setup.Build(PathReference.FromElements(new[] { "eixo" }), new OutputSettings(), Cat).OfType<LinearMarkingDefinition>().ToList();
        Assert.Equal(5, defs.Count);
        Assert.Single(defs, d => d.Code == "LFO-2");
        Assert.Equal(2, defs.Count(d => d.Code == "LMS-2"));
        Assert.Equal(2, defs.Count(d => d.Code == "LBO"));
        Assert.Contains(defs, d => d.Code == "LMS-2" && Math.Abs(d.Offset + 3.5) < 1e-9);
        Assert.Contains(defs, d => d.Code == "LBO" && Math.Abs(d.Offset - (7.0 - 0.10)) < 1e-9);
        Assert.Single(defs.Select(d => d.GroupId).Distinct());
        Assert.All(defs, d => Assert.Equal("eixo", d.PathRef.ElementIds.Single()));
    }

    [Fact]
    public void RoadSetup_OneWay()
    {
        var setup = new RoadSetup { TwoWay = false, Right = { Lane(), Lane() }, Left = { Lane() } };
        var defs = setup.Build(new PathReference(), new OutputSettings(), Cat).OfType<LinearMarkingDefinition>().ToList();
        Assert.Equal(4, defs.Count); // 2 divisórias + 2 bordos
        Assert.Contains(defs, d => d.Code == "LMS-2" && Math.Abs(d.Offset) < 1e-9);
        Assert.Contains(defs, d => d.Code == "LMS-2" && Math.Abs(d.Offset + 3.5) < 1e-9);
        Assert.DoesNotContain(defs, d => d.Code.StartsWith("LFO"));
    }

    [Fact]
    public void RoadSetup_FullSection_CreatesPhysicalAndSpecialLanes()
    {
        var setup = new RoadSetup
        {
            Center = CenterTreatment.Canteiro, MedianWidth = 2.0, MedianType = TipoCanteiro.Fisico,
            Right =
            {
                new() { Tipo = TipoElementoSecao.FaixaExclusiva, Largura = 3.5 },
                Lane(3.3),
                new() { Tipo = TipoElementoSecao.Ciclofaixa, Largura = 1.5, Dispositivo = "SEG-CIC" },
                new() { Tipo = TipoElementoSecao.Estacionamento, Largura = 2.2 },
                new() { Tipo = TipoElementoSecao.Calcada, Largura = 3.0, FaixaServico = 0.7 },
            },
            Left = { Lane(3.3) },
        };
        var defs = setup.Build(PathReference.FromElements(new[] { "eixo" }), new OutputSettings(), Cat);
        var lin = defs.OfType<LinearMarkingDefinition>().ToList();
        Assert.Contains(lin, d => d.Code == "MFE");                        // exclusiva × rolamento
        Assert.Contains(lin, d => d.Code == "CIC-LD");                     // rolamento × ciclofaixa
        Assert.Contains(lin, d => d.Code == "CIC-FD");                     // pintura vermelha
        Assert.Equal(2 + 1, lin.Count(d => d.Code == "MEIO-FIO"));         // canteiro central (2) + calçada (1)
        Assert.Contains(lin, d => d.Code == "GRAMADO");
        Assert.Contains(lin, d => d.Code == "CALCADA");
        Assert.Single(defs.OfType<ParkingMarkingDefinition>());
        Assert.Single(defs.OfType<DeviceMarkingDefinition>(), d => d.Code == "SEG-CIC" && Math.Abs(d.Offset + (1 + 3.5 + 3.3)) < 1e-9);
        Assert.Contains(defs.OfType<RepeatedMarkingDefinition>(), d => d.Text == "ÔNIBUS");
        Assert.Contains(defs.OfType<RepeatedMarkingDefinition>(), d => d.SymbolCode == "SIC");
        Assert.Empty(setup.Warnings);

        // Toda a seção gera geometria num trecho reto
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(80, 0) });
        foreach (var d in defs)
        {
            var geo = MarkingBuilder.Build(d, path, Ctx);
            Assert.True(geo.Pieces.Count > 0, $"{d.DisplayCode} não gerou peças: {string.Join("; ", geo.Warnings)}");
        }
        // Estacionamento fica entre a ciclofaixa e a calçada: 1,0 (meio canteiro) + 3,5 + 3,3 + 1,5 = 9,3 até 11,5 m
        var park = MarkingBuilder.Build(defs.OfType<ParkingMarkingDefinition>().Single(), path, Ctx);
        var (mn, mx) = park.Bounds!.Value;
        Assert.InRange(mn.Y, -11.56, -11.44);
        Assert.InRange(mx.Y, -9.36, -9.24);
    }

    [Fact]
    public void RoadSetup_NarrowSidewalk_WarnsNbr9050()
    {
        var setup = new RoadSetup { Right = { Lane(), new() { Tipo = TipoElementoSecao.Calcada, Largura = 1.5, FaixaServico = 0.7 } }, Left = { Lane() } };
        setup.Build(new PathReference(), new OutputSettings(), Cat);
        Assert.Contains(setup.Warnings, w => w.Contains("9050"));
    }

    [Fact]
    public void RoadTemplates_AllBuildGeometry()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(120, 0) });
        foreach (var t in RoadTemplates.All)
        {
            var s = t.Create();
            var defs = s.Build(new PathReference(), new OutputSettings(), Cat);
            Assert.NotEmpty(defs);
            var total = defs.Sum(d => MarkingBuilder.Build(d, path, Ctx).Pieces.Count);
            Assert.True(total > 0, t.Name);
        }
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
    public void Devices_UnitsAndContinuous()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(20, 0) });
        var bal = MarkingBuilder.Build(new DeviceMarkingDefinition { Code = "BAL-FLEX" }, path, Ctx);
        Assert.Equal(10, bal.UnitCount);                                   // espaçamento 2 m
        Assert.Contains(bal.Pieces, p => p.Solid != null && p.Solid.MaxZ > 0.7);   // haste sobre a base
        var nj = MarkingBuilder.Build(new DeviceMarkingDefinition { Code = "NJ", Offset = 1 }, path, Ctx);
        Assert.Equal(0, nj.UnitCount);
        Assert.Equal(20, nj.PaintedLength, 6);
        Assert.Equal(0.81, nj.Pieces.Max(p => p.Elevation + p.Thickness), 6);
        Assert.All(nj.Pieces, p => Assert.InRange(p.Shape.Centroid.Y, 0.99, 1.01));
        var def = MarkingBuilder.Build(new DeviceMarkingDefinition { Code = "DEF" }, path, Ctx);
        Assert.True(def.UnitCount >= 5);                                   // postes a cada 4 m
        Assert.Contains(def.Pieces, p => !p.IsUnit && p.Profile != null && p.Profile.Profile.Bounds.Min.Y > 0.2);   // lâmina
    }

    [Fact]
    public void Devices_Info_UsesUnitsOrMeters()
    {
        Assert.Equal("un", MarkingBuilder.Describe(new DeviceMarkingDefinition { Code = "PIL" }, Cat).Unit);
        Assert.Equal("m", MarkingBuilder.Describe(new DeviceMarkingDefinition { Code = "NJ" }, Cat).Unit);
    }

    [Fact]
    public void Repeated_TextAlongPath_FollowsReverseTraffic()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(100, 0) });
        var d = new RepeatedMarkingDefinition { Text = "ÔNIBUS", Spacing = 30, StartOffset = 10, Offset = 1.75, Reverse = true };
        var geo = MarkingBuilder.Build(d, path, Ctx);
        Assert.Equal(3, geo.UnitCount);
        Assert.All(geo.Pieces, p => Assert.True(p.Shape.Centroid.Y > 0)); // continua do lado esquerdo
    }

    [Fact]
    public void HatchStrip_AlongCurvedPath()
    {
        var arc = new Polyline2(CurveTools.Arc(Vec2.Zero, 40, 0, Math.PI / 2));
        var d = new HatchMarkingDefinition { Code = "ZPA-A", StripWidth = 1.2, StripOffset = 0 };
        var geo = MarkingBuilder.Build(d, arc, Ctx);
        Assert.True(geo.Pieces.Count > 20);
        Assert.All(geo.Pieces.SelectMany(p => p.Shape.Outer), v => Assert.InRange(v.Length, 40 - 0.61, 40 + 0.61));
        var back = (HatchMarkingDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.True(back.IsStrip);
    }

    [Fact]
    public void Quantities_NoPaintConsumptionForPhysicalElements()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(10, 0) });
        var items = new MarkingDefinition[]
        {
            new LinearMarkingDefinition { Code = "CALCADA", WidthOverride = 2 },
            new DeviceMarkingDefinition { Code = "PIL" },
        }.Select(d => (d, MarkingBuilder.Build(d, path, Ctx)));
        var rows = QuantityCalculator.Compute(items, Cat);
        Assert.All(rows, r => Assert.Equal(0, r.MaterialConsumption));
        Assert.Equal(20, rows.Single(r => r.Code == "CALCADA").Area, 3);
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
