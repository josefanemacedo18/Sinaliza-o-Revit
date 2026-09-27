using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada dos manuais (DER/SP Vol. I, MBST Vol. II/IV/VI/IX, CET Vol. 13): zebrado em anel e faixa ao longo de contorno,
/// rotatórias por modo de integração e por ramo, regras de projeto, LRV pelo método do MBST, canalização em transição,
/// cruzamento rodoferroviário, almofada e ciclofaixa padrão II.
/// </summary>
public class V14Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static Polyline2 Circle(double r) => new(CurveTools.Circle(Vec2.Zero, r, 0.02), true);
    private static Polyline2 Straight(double len, double y = 0) => new(new[] { new Vec2(0, y), new Vec2(len, y) });

    // ------------------------------------------------------------------ zebrado

    [Fact]
    public void Hatch_RingWithHole_LeavesCenterEmpty()
    {
        var h = new HatchMarkingDefinition { Code = "ZPA" };
        var geo = MarkingBuilder.BuildHatch(h, new[] { Circle(10), Circle(6) }, Ctx);
        Assert.NotEmpty(geo.Pieces);
        Assert.InRange(geo.Pieces.Sum(p => p.Shape.Area), 1, Math.PI * (100 - 36));
        Assert.All(geo.Pieces, p => Assert.All(p.Shape.Outer, v => Assert.True(v.Length >= 5.8, $"vértice dentro do furo: {v}")));
        Assert.All(geo.Pieces, p => Assert.All(p.Shape.Outer, v => Assert.True(v.Length <= 10.05)));
    }

    [Fact]
    public void Hatch_StripAlongClosedContour_StaysInBand()
    {
        var inner = new HatchMarkingDefinition { Code = "ZPA-A", StripWidth = 2.5, StripSide = LadoFaixa.Interno };
        Assert.True(inner.IsStrip);
        var geo = MarkingBuilder.Build(inner, Circle(10), Ctx);
        Assert.NotEmpty(geo.Pieces);
        Assert.All(geo.Pieces, p => Assert.All(p.Shape.Outer, v => Assert.InRange(v.Length, 7.3, 10.2)));

        var outer = new HatchMarkingDefinition { Code = "ZPA", StripWidth = 2.0, StripSide = LadoFaixa.Externo };
        var geo2 = MarkingBuilder.Build(outer, Circle(6), Ctx);
        Assert.NotEmpty(geo2.Pieces);
        Assert.All(geo2.Pieces, p => Assert.All(p.Shape.Outer, v => Assert.InRange(v.Length, 5.8, 8.2)));
    }

    [Fact]
    public void Hatch_StripSide_LeftRightOnOpenPath()
    {
        var left = new HatchMarkingDefinition { Code = "ZPA", StripWidth = 2.0, StripSide = LadoFaixa.Esquerda };
        var gl = MarkingBuilder.Build(left, Straight(30), Ctx);
        Assert.All(gl.Pieces, p => Assert.InRange(p.Shape.Bounds.Min.Y, -0.05, 2.05));
        var right = new HatchMarkingDefinition { Code = "ZPA", StripWidth = 2.0, StripSide = LadoFaixa.Direita };
        var gr = MarkingBuilder.Build(right, Straight(30), Ctx);
        Assert.All(gr.Pieces, p => Assert.InRange(p.Shape.Bounds.Max.Y, -2.05, 0.05));
    }

    // ------------------------------------------------------------------ rotatórias

    private static RoundaboutDefinition Rb(Action<RoundaboutDefinition>? setup = null, int legs = 4)
    {
        var d = new RoundaboutDefinition();
        d.ApplyPreset(TipoRotatoria.UmaFaixa);
        setup?.Invoke(d);
        for (int i = 0; i < legs; i++) d.Legs.Add(new RoundaboutLeg { AngleDeg = i * 360.0 / legs, Width = 7, Sidewalk = 2.5 });
        return d;
    }

    [Fact]
    public void Roundabout_IntegrationModes_CutZonesShrink()
    {
        var full = RoundaboutGenerator.Layout(Rb(d => d.Integration = IntegracaoRotatoria.Completa));
        var ring = RoundaboutGenerator.Layout(Rb(d => d.Integration = IntegracaoRotatoria.Anel));
        var island = RoundaboutGenerator.Layout(Rb(d => d.Integration = IntegracaoRotatoria.SomenteIlha));
        Assert.True(full.Zone.Area > ring.Zone.Area, "modo completo recorta mais que o anel");
        Assert.True(ring.Zone.Area > island.Zone.Area, "modo anel recorta mais que somente ilha");
        Assert.Equal(ring.Zone.Area, ring.PaintZone.Area, 3);
        Assert.NotEmpty(full.Pavement);
        Assert.True(island.Pavement.Sum(p => p.Area) <= ring.Pavement.Sum(p => p.Area) + 1e-6);
        foreach (var L in new[] { full, ring, island })
            Assert.DoesNotContain(L.Pavement, p => p.Contains(Vec2.Zero));
    }

    [Fact]
    public void Roundabout_MiniPainted_HasLcaStudsAndArrows()
    {
        var d = new RoundaboutDefinition();
        d.ApplyPreset(TipoRotatoria.Mini);
        foreach (var a in new[] { 0.0, 90, 180, 270 }) d.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = 2.5 });
        Assert.Equal(TipoIlhaCentral.Pintada, d.IslandType);
        Assert.True(d.RingArrows);
        var L = RoundaboutGenerator.Layout(d);
        Assert.True(L.PaintedIsland);
        var kids = RoundaboutGenerator.Children(d, L, new OutputSettings(), 0);
        Assert.Contains(kids, k => k.DisplayCode == "LCA");
        Assert.Contains(kids, k => k.DisplayCode == "TACHAO-SEG");
        Assert.Contains(kids, k => k is SymbolMarkingDefinition { Code: "IMC" });
        Assert.Contains(kids, k => k is HatchMarkingDefinition { Code: "ZPA" });   // ilhas separadoras pintadas
        Assert.Contains(kids, k => k is SignDefinition { Code: "R-33" });
        var geo = MarkingBuilder.Build(d, null, Ctx);
        Assert.NotEmpty(geo.Pieces);
        foreach (var k in kids)
        {
            var p = k.Path;
            var g = MarkingBuilder.Build(k, p == null ? null : new Polyline2(p.Points, p.Closed), Ctx);
            Assert.True(g.Pieces.Count > 0 || g.Warnings.Count > 0, k.DisplayCode);
        }
    }

    [Fact]
    public void Roundabout_PerLegSettings_ControlSplitterCrosswalk()
    {
        var d = Rb();
        d.Legs[1].Control = ControleRamo.Pare;
        d.Legs[2].Splitter = IlhaSeparadora.Pintada;
        d.Legs[3].Crosswalk = false;
        d.Legs[0].EntryRadius = 8;
        var L = RoundaboutGenerator.Layout(d);
        var kids = RoundaboutGenerator.Children(d, L, new OutputSettings(), 0);
        Assert.Contains(kids, k => k is TextMarkingDefinition { Text: "PARE" });
        Assert.Contains(kids, k => k is LinearMarkingDefinition { Code: "LRE" });
        Assert.Equal(3, kids.Count(k => k is LinearMarkingDefinition { Code: "LDP" }));
        Assert.Equal(3, kids.Count(k => k is SymbolMarkingDefinition { Code: "SDP" }));
        Assert.Contains(kids, k => k is HatchMarkingDefinition { Code: "ZPA" });
        Assert.Equal("Ramo a 90°", d.Legs[1].Label);
    }

    [Fact]
    public void Roundabout_MergeLegSettings_KeepsCustomizationByRoad()
    {
        var old = new List<RoundaboutLeg>
        {
            new() { RoadId = "r1", AngleDeg = 0, Control = ControleRamo.Pare, Crosswalk = false, Width = 9 },
            new() { RoadId = "r2", AngleDeg = 180, Splitter = IlhaSeparadora.Pintada },
        };
        var fresh = new List<RoundaboutLeg>
        {
            new() { RoadId = "r2", AngleDeg = 175, Width = 7 },
            new() { RoadId = "r1", AngleDeg = 8, Width = 7 },
            new() { RoadId = "r3", AngleDeg = 90, Width = 7 },
        };
        var merged = RoundaboutGenerator.MergeLegSettings(old, fresh);
        Assert.Equal(3, merged.Count);
        var r1 = merged.Single(l => l.RoadId == "r1");
        Assert.Equal(ControleRamo.Pare, r1.Control);
        Assert.Equal(false, r1.Crosswalk);
        Assert.Equal(IlhaSeparadora.Pintada, merged.Single(l => l.RoadId == "r2").Splitter);
        Assert.Equal(ControleRamo.Padrao, merged.Single(l => l.RoadId == "r3").Control);
    }

    [Fact]
    public void Roundabout_Json_RoundTripsNewFields()
    {
        var d = Rb(x => { x.Integration = IntegracaoRotatoria.Anel; x.CutRoads = false; x.SplitterStyle = IlhaSeparadora.Pintada; x.DirectionSigns = PlacaSentidoRotatoria.R24aNaIlha; });
        d.Legs[0].Control = ControleRamo.Pare;
        var back = (RoundaboutDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.Equal(IntegracaoRotatoria.Anel, back.Integration);
        Assert.False(back.CutRoads);
        Assert.Equal(IlhaSeparadora.Pintada, back.SplitterStyle);
        Assert.Equal(PlacaSentidoRotatoria.R24aNaIlha, back.DirectionSigns);
        Assert.Equal(ControleRamo.Pare, back.Legs[0].Control);
    }

    // ------------------------------------------------------------------ regras de projeto

    [Theory]
    [InlineData(60, 0, 69)]
    [InlineData(80, 30, 106)]
    [InlineData(40, 20, 23)]
    public void DesignRules_DecelerationDistance(double v0, double v1, double expected) =>
        Assert.Equal(expected, DesignRules.DecelerationDistance(v0, v1), 0);

    [Fact]
    public void DesignRules_Lrv_MonotonicDecreasingGaps()
    {
        var st = DesignRules.LrvStations(60, 30);
        Assert.True(st.Count >= 5);
        var gaps = st.Zip(st.Skip(1), (a, b) => b - a).ToList();
        for (int i = 1; i < gaps.Count; i++) Assert.True(gaps[i] <= gaps[i - 1] + 1e-9, "espaçamento decrescente");
        Assert.Equal(0.20, DesignRules.LrvLineWidth(60));
        Assert.Equal(0.30, DesignRules.LrvLineWidth(80));
        Assert.Equal(0.40, DesignRules.LrvLineWidth(100));
        var pat = DesignRules.LrvPattern(60, 30, 0.2);
        Assert.Equal(st.Count * 2 - 1, pat.Length);
        Assert.All(pat.Where((_, i) => i % 2 == 0), w => Assert.Equal(0.2, w, 6));
    }

    [Fact]
    public void DesignRules_TablesFromManuals()
    {
        Assert.Equal(105, DesignRules.TaperLength(60, 3.5), 6);
        Assert.Equal(60, DesignRules.TaperLength(60, 1.0, rural: true, obstacle: true), 6);
        Assert.Equal(30, DesignRules.TaperLength(40, 1.0, rural: false, obstacle: true), 6);
        Assert.Equal(1.5, DesignRules.ChannelHatchGap(60));
        Assert.Equal(2.5, DesignRules.ChannelHatchGap(80));
        Assert.Equal(8, DesignRules.StudSpacing(60));
        Assert.Equal(12, DesignRules.StudSpacing(90));
        Assert.Equal(16, DesignRules.StudSpacing(100));
        Assert.Equal(6, DesignRules.StudSpacing(60, special: true));
        Assert.Equal(3.60, DesignRules.YieldSymbolLength(60));
        Assert.Equal(6.00, DesignRules.YieldSymbolLength(80));
        Assert.Equal(1.60, DesignRules.LegendHeight(60));
        Assert.Equal(2.40, DesignRules.LegendHeight(100));
        Assert.Equal(4.00, DesignRules.LegendHeight(80, rural: true));
        Assert.Equal(30, DesignRules.ShoulderTransition(50));
        Assert.Equal(40, DesignRules.ShoulderTransition(70));
        Assert.Equal(50, DesignRules.ShoulderTransition(100));
        Assert.Equal(20, DesignRules.WarningDistance(30));
        Assert.Equal(10, DesignRules.DelineatorSpacing(40));
        Assert.Equal(60, DesignRules.DelineatorSpacing(0));
        Assert.Equal(0.10, DesignRules.LongitudinalLineWidth(60));
        Assert.Equal(0.15, DesignRules.LongitudinalLineWidth(80));
    }

    // ------------------------------------------------------------------ LRV paramétrica

    [Fact]
    public void Lrv_ParametricByMethod_OneLinePerStation()
    {
        var d = new LinearMarkingDefinition { Code = "LRV", LrvFromKmh = 60, LrvToKmh = 30, WidthOverride = 3.3 };
        var geo = MarkingBuilder.Build(d, Straight(120), Ctx);
        var st = DesignRules.LrvStations(60, 30);
        Assert.Equal(st.Count, geo.Pieces.Count);
        Assert.All(geo.Pieces, p => Assert.Equal(0.20 * 3.3, p.Shape.Area, 2));
        var xs = geo.Pieces.Select(p => p.Shape.Bounds.Min.X).OrderBy(x => x).ToList();
        var gaps = xs.Zip(xs.Skip(1), (a, b) => b - a).ToList();
        for (int i = 1; i < gaps.Count; i++) Assert.True(gaps[i] <= gaps[i - 1] + 1e-6, "os espaçamentos decrescem no sentido do tráfego");

        // sem velocidades: comportamento antigo (variante do catálogo)
        var plain = MarkingBuilder.Build(new LinearMarkingDefinition { Code = "LRV", WidthOverride = 3.3 }, Straight(120), Ctx);
        Assert.NotEmpty(plain.Pieces);
        var back = (LinearMarkingDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.Equal(60, back.LrvFromKmh);
    }

    // ------------------------------------------------------------------ canalização

    [Theory]
    [InlineData(TipoCanalizacao.TransicaoLargura)]
    [InlineData(TipoCanalizacao.Obstaculo)]
    [InlineData(TipoCanalizacao.Acostamento)]
    public void Channelization_AllTypesBuildInsideOutline(TipoCanalizacao type)
    {
        var d = new ChannelizationDefinition { Type = type, Speed = 60, WidthChange = 3.0, StartStation = 5, ObstacleLength = 10 };
        var l = ChannelizationGenerator.EntryLength(d);
        Assert.Equal(type == TipoCanalizacao.Acostamento ? 40 : 90, l, 6);
        var geo = ChannelizationGenerator.Generate(d, Straight(400), Ctx);
        Assert.NotEmpty(geo.Pieces);
        var maxS = ChannelizationGenerator.LocalOutline(d).Max(c => c.Max(p => p.S));
        Assert.True(maxS >= 5 + l - 1e-9);
        Assert.All(geo.Pieces, p => Assert.All(p.Shape.Outer, v =>
        {
            Assert.InRange(v.X, 4.9, maxS + 0.1);
            Assert.InRange(v.Y, -0.1, 3.0 + d.Clearance + 0.1);
        }));
        Assert.Equal(type switch { TipoCanalizacao.Obstaculo => "MAO", TipoCanalizacao.Acostamento => "MAP", _ => "MTL" }, d.DisplayCode);
    }

    [Fact]
    public void Channelization_BothSides_YellowOnBothSidesOfAxis()
    {
        var d = new ChannelizationDefinition { Type = TipoCanalizacao.Obstaculo, Speed = 60, WidthChange = 2.0, BothSides = true, Color = MarkingColor.Amarela };
        var geo = ChannelizationGenerator.Generate(d, Straight(300), Ctx);
        Assert.Contains(geo.Pieces, p => p.Shape.Bounds.Min.Y < -1.5);
        Assert.Contains(geo.Pieces, p => p.Shape.Bounds.Max.Y > 1.5);
        Assert.All(geo.Pieces, p => Assert.Equal(MarkingColor.Amarela, p.Color));
        Assert.IsType<ChannelizationDefinition>(MarkingDefinition.FromJson(d.ToJson()));
        // caminho curto: avisa
        var shortGeo = ChannelizationGenerator.Generate(d, Straight(20), Ctx);
        Assert.Contains(shortGeo.Warnings, w => w.Contains("prolongue"));
    }

    // ------------------------------------------------------------------ cruzamento rodoferroviário

    [Fact]
    public void RailCrossing_BuildsFullSet()
    {
        var axis = Straight(300);
        var s = new RailCrossingSetup { Station = 150, Speed = 60, Lrv = true, ConflictArea = true };
        var defs = s.Build(PathReference.FromPoints(axis.Points, 0), axis, 0, new OutputSettings());
        Assert.Equal(2, defs.Count(d => d is LinearMarkingDefinition { Code: "MCF" }));
        Assert.Equal(1, defs.Count(d => d is LinearMarkingDefinition { Code: "LFO-3" }));
        Assert.Equal(4, defs.Count(d => d is LinearMarkingDefinition { Code: "LRE" }));
        Assert.Equal(2, defs.Count(d => d is SymbolMarkingDefinition { Code: "CSA" }));
        Assert.Equal(2, defs.Count(d => d is TextMarkingDefinition { Text: "PARE" }));
        Assert.Equal(2, defs.Count(d => d is LinearMarkingDefinition { Code: "LRV" }));
        Assert.Equal(2, defs.Count(d => d is HatchMarkingDefinition { Code: "MAC" }));
        Assert.Equal(2, defs.Count(d => d is SignDefinition { Code: "A-41" }));
        Assert.Equal(2, defs.Count(d => d is SignDefinition { Code: "A-39" }));
        Assert.Equal(2, defs.Count(d => d is SignDefinition { Code: "R-1" }));
        Assert.Equal(2, defs.Count(d => d is SignDefinition { Code: "R-19" }));
        Assert.Single(defs.Select(d => d.GroupId).Distinct());
        var mcf = defs.OfType<LinearMarkingDefinition>().First(d => d.Code == "MCF");
        Assert.Equal(mcf.PathRef.Points[0].X, mcf.PathRef.Points[1].X, 6);   // paralela ao trilho = perpendicular à via
        Assert.Equal(150 - 0.8 - 3.0, mcf.PathRef.Points[0].X, 6);          // 3,0 m do trilho externo
        var geo = MarkingBuilder.Build(mcf, new Polyline2(mcf.PathRef.Points), Ctx);
        Assert.Equal(2, geo.Pieces.Count);
        // ferrovia esconsa: linha de retenção acompanha o trilho
        s.RailDirection = new Vec2(0.5, 0.866);
        var skew = s.Build(PathReference.FromPoints(axis.Points, 0), axis, 0, new OutputSettings()).OfType<LinearMarkingDefinition>().First(d => d.Code == "MCF");
        var dir = (skew.PathRef.Points[1] - skew.PathRef.Points[0]).Normalized();
        Assert.Equal(0.5, Math.Abs(dir.X), 2);
        // com cancela: A-40
        s.Barrier = true;
        Assert.Contains(s.Build(PathReference.FromPoints(axis.Points, 0), axis, 0, new OutputSettings()), d => d is SignDefinition { Code: "A-40" });
    }

    // ------------------------------------------------------------------ almofada e ciclofaixa

    [Fact]
    public void Cushion_OnePerLane_NarrowerThanLane()
    {
        var d = new TrafficCalmingDefinition { Type = TipoModeracao.Almofada };
        var geo = MarkingBuilder.Build(d, new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 7) }), Ctx);
        Assert.Equal(2, geo.UnitCount);
        var solids = geo.Pieces.Where(p => p.Profile != null && p.Color == MarkingColor.Asfalto).ToList();
        Assert.Equal(2, solids.Count);
        Assert.All(solids, p => Assert.Equal(1.70, p.Profile!.Depth, 6));
        Assert.Equal("ALMOFADA", d.DisplayCode);
        var (l, h, _) = TrafficCalmingGenerator.Defaults(TipoModeracao.Almofada);
        Assert.Equal(3.0, l);
        Assert.Equal(0.075, h);
        d.CushionCount = 3;
        d.CushionWidth = 1.9;
        var g3 = MarkingBuilder.Build(d, new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 7) }), Ctx);
        Assert.Equal(3, g3.UnitCount);
    }

    [Fact]
    public void BikeLane_CetPatternII_RedContrastLineOnlyWithoutBackground()
    {
        var axis = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(30, 0) }, 0);
        var s = new BikeLaneSetup { Type = TipoCiclo.CiclofaixaUnidirecional, Background = false, RedContrastLines = true, Lines = LadoLinha.Ambos };
        var defs = s.Build(axis, new OutputSettings());
        var contrast = defs.OfType<LinearMarkingDefinition>().Where(d => d.Code == "CIC-FD" && Math.Abs((d.WidthOverride ?? 0) - s.ContrastWidth) < 1e-9).ToList();
        Assert.Equal(2, contrast.Count);
        Assert.All(contrast, c => Assert.True(Math.Abs(c.Offset) < s.Width / 2 - s.LineWidth, "linha vermelha por dentro da linha branca"));
        s.RedContrastLines = false;
        Assert.DoesNotContain(s.Build(axis, new OutputSettings()).OfType<LinearMarkingDefinition>(), d => d.Code == "CIC-FD" && Math.Abs((d.WidthOverride ?? 0) - s.ContrastWidth) < 1e-9);
    }

    // ------------------------------------------------------------------ catálogo

    [Fact]
    public void Catalog_ManualAdditions()
    {
        Assert.Equal(6, Cat.Linear("LFO-5")!.Variantes.Count);
        Assert.Equal(2, Cat.Linear("MCF")!.Variantes[0].Faixas.Count);
        Assert.True(Cat.Hachura("MAE")!.Cruzado);
        Assert.Equal(MarkingColor.Amarela, Cat.Hachura("MAE-A")!.CorBarras);
        Assert.Equal(8, Cat.Linear("TAC-A")!.Variantes.Count);
        Assert.Equal(5, Cat.Linear("TACHAO")!.Variantes.Count);
        Assert.Equal(FormaSimbolo.SetaCurvaEsquerda, Cat.Simbolo("IMC")!.Forma);
        Assert.Equal(FormaSimbolo.SetaCurvaDireita, Cat.Simbolo("IMC-D")!.Forma);
        Assert.StartsWith("0,40 m (0,50", Cat.Linear("LDP")!.Variantes[0].Nome);
        var v = MarkingBuilder.ResolveVariant(Cat.Linear("TAC-B")!, null, 85);
        Assert.Contains("12 m", v!.Nome);
        var lfo5 = MarkingBuilder.Build(new LinearMarkingDefinition { Code = "LFO-5", Speed = 60 }, Straight(100), Ctx);
        Assert.Equal(3, lfo5.Pieces.Count);
        var imc = MarkingBuilder.Build(new SymbolMarkingDefinition { Code = "IMC", Length = 4.5, Position = Vec2.Zero, Direction = new Vec2(1, 0) }, null, Ctx);
        Assert.NotEmpty(imc.Pieces);
    }
}
