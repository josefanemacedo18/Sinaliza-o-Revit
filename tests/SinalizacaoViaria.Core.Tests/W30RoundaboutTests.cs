using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada W: rotatória sobre vias existentes – a seção das vias continua e as opções não se sobrepõem.</summary>
public class W30RoundaboutTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    /// <summary>Avenida com canteiro e estacionamento (E-O) × coletora (N-S).</summary>
    private static (RoundaboutDefinition D, RoundaboutLayout L, List<List<MarkingDefinition>> Roads) Scene(Action<RoundaboutDefinition> cfg, TipoRotatoria type = TipoRotatoria.UmaFaixa)
    {
        var roads = new List<IntersectionRoad>();
        var groups = new List<List<MarkingDefinition>>();
        foreach (var (a, b, t) in new[] { (new Vec2(-150, 0), new Vec2(150, 0), 2), (new Vec2(0, -150), new Vec2(0, 150), 1) })
        {
            var pr = PathReference.FromPoints(new[] { a, b }, 0);
            var g = RoadTemplates.All[t].Create().Build(pr, new OutputSettings(), Cat);
            groups.Add(g);
            roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), new Polyline2(pr.Points)));
        }
        var d = new RoundaboutDefinition { Center = Vec2.Zero };
        d.ApplyPreset(type);
        cfg(d);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, roads, d.OuterRadius + 25);
        return (d, RoundaboutGenerator.Layout(d), groups);
    }

    [Fact]
    public void Bypass_DoesNotWipeTheRoadSectionForAHundredMeters()
    {
        var (d, L, _) = Scene(x => x.Bypass = true);
        var (min, max) = L.Zone.Bounds;
        // Antes: círculo de ~100 m de raio (canteiro, estacionamento e linhas sumiam); agora só o miolo e os by-pass.
        Assert.True(max.X < 60 && -min.X < 60, $"zona até {max.X:0} m");
        Assert.NotEmpty(L.Refuges);
    }

    [Fact]
    public void AvenueLegs_KnowTheirMedian_AndTheIslandContinuesIt()
    {
        var (d, L, _) = Scene(_ => { });
        var ew = d.Legs.Where(l => Math.Abs(Math.Sin(l.AngleDeg * Math.PI / 180)) < 0.1).ToList();
        Assert.Equal(2, ew.Count);
        Assert.All(ew, l => Assert.True(l.MedianWidth >= 1));
        // A ilha separadora vai até o fim da zona remodelada (encosta no canteiro da via).
        foreach (var g in L.Legs.Where(g => g.Leg.MedianWidth > 0))
        {
            Assert.NotNull(g.Splitter);
            var far = g.Splitter!.Outer.Max(p => p.DistanceTo(Vec2.Zero));
            Assert.True(far >= RoundaboutGenerator.Ray(L.Zone, Vec2.Zero, g.Dir) - 0.5, $"ilha até {far:0.0} m");
        }
    }

    [Fact]
    public void RingOnly_UsesTheMedianAsSplitter_CutsARefuge_AndStopsParkingBeforeTheCrosswalk()
    {
        var (d, L, roads) = Scene(x => { x.Integration = IntegracaoRotatoria.Anel; x.Crosswalks = true; });
        Assert.All(L.Legs.Where(g => g.Leg.MedianWidth > 0), g => { Assert.Null(g.Splitter); Assert.Null(g.PaintedSplitter); });
        Assert.Equal(2, L.MedianPassages.Count);
        var rou = L.ToOuter(Vec2.Zero, new Vec2(1, 0));
        // Pintura das vias interrompida até depois da travessia; estacionamento 5 m antes dela.
        Assert.True(RoundaboutGenerator.Ray(L.RoadPaintZone, Vec2.Zero, new Vec2(1, 0)) >= rou + d.CrosswalkDistance + d.CrosswalkWidth);
        Assert.True(RoundaboutGenerator.Ray(L.ParkingZone, Vec2.Zero, new Vec2(1, 0)) >= RoundaboutGenerator.Ray(L.RoadPaintZone, Vec2.Zero, new Vec2(1, 0)) + 4.9);
        var parking = roads.SelectMany(g => g).OfType<ParkingMarkingDefinition>().First();
        var cuts = RoundaboutGenerator.RoadCuts(d, L, parking);
        Assert.Contains(cuts, c => ReferenceEquals(c, L.ParkingZone));
        var median = roads.SelectMany(g => g).First(m => IntersectionGenerator.IsPhysical(m) && m is not RoadPavementDefinition);
        Assert.True(RoundaboutGenerator.RoadCuts(d, L, median).Count >= 1 + L.MedianPassages.Count);
    }

    [Fact]
    public void MiniOnAWideAvenue_Warns_AndYieldLinesStayOnTheirOwnEntry()
    {
        var (d, L, _) = Scene(_ => { }, TipoRotatoria.Mini);
        Assert.Contains(L.Warnings, w => w.Contains("Diâmetro inscrito"));
        var kids = RoundaboutGenerator.Children(d, L, new OutputSettings(), 0);
        var rou = L.ToOuter(Vec2.Zero, new Vec2(1, 0));
        foreach (var ldp in kids.OfType<LinearMarkingDefinition>().Where(k => k.Code == "LDP"))
            Assert.True(new Polyline2(ldp.PathRef.Points).Length < 1.2 * rou + 1, "linha de dê a preferência atravessando o anel");
    }
}
