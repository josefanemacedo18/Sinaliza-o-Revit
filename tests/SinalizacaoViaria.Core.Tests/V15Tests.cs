using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada de correções: tachas/tachões em 3D, rotatória elevada e ilha em calota, quantitativo memorial, seção guardada na via.</summary>
public class V15Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static Polyline2 Straight(double len) => new(new[] { new Vec2(0, 0), new Vec2(len, 0) });

    [Fact]
    public void Studs_AreSolidsWithReflectiveFaces()
    {
        var t = Cat.Linear("TACHAO")!;
        var geo = LinearPatternGenerator.Generate(Straight(40), t, t.Variantes[0]);
        Assert.Equal(10, geo.UnitCount);
        Assert.True(geo.Pieces.Count > geo.UnitCount, "cada tachão tem corpo + refletivos");
        Assert.All(geo.Pieces, p => Assert.NotNull(p.Solid));
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Branca);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Amarela && p.Thickness > 0.04);
        var body = geo.Pieces.First(p => p.Color == MarkingColor.Amarela);
        Assert.InRange(body.Shape.Bounds.Max.X - body.Shape.Bounds.Min.X, 0.24, 0.26);   // 0,25 m ao longo do caminho
        Assert.InRange(body.Shape.Bounds.Max.Y - body.Shape.Bounds.Min.Y, 0.14, 0.16);   // 0,15 m de largura
    }

    [Fact]
    public void StudPieces_TachaIsLowAndTachaoIsTrapezoid()
    {
        var tacha = DeviceGenerator.StudPieces(0.10, 0.10, 0.02, MarkingColor.Branca, false).ToList();
        Assert.True(tacha.Count >= 2);
        Assert.All(tacha, p => Assert.True(p.Solid!.MaxZ <= 0.02 + 1e-9));
        var tachao = DeviceGenerator.StudPieces(0.25, 0.15, 0.05, MarkingColor.Amarela, true).ToList();
        Assert.Equal(3, tachao.Count);   // corpo + 2 refletivos
        Assert.Equal(0.05, tachao[0].Solid!.MaxZ, 6);
    }

    private static RoundaboutDefinition Rb(Action<RoundaboutDefinition>? setup = null)
    {
        var d = new RoundaboutDefinition();
        d.ApplyPreset(TipoRotatoria.Compacta);
        setup?.Invoke(d);
        foreach (var a in new[] { 0.0, 90, 180, 270 }) d.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = 2.5 });
        return d;
    }

    [Fact]
    public void Roundabout_Raised_RingHigherWithRamps()
    {
        var flat = MarkingBuilder.Build(Rb(), null, Ctx);
        var d = Rb(x => { x.Raised = true; x.RaisedHeight = 0.10; x.RampLength = 1.5; });
        var geo = MarkingBuilder.Build(d, null, Ctx);
        var pavFlat = flat.Pieces.Where(p => p.Color == MarkingColor.Asfalto && p.Profile == null).Max(p => p.Elevation + p.Thickness);
        var pavRing = geo.Pieces.Where(p => p.Color == MarkingColor.Asfalto && p.Profile == null).Max(p => p.Elevation + p.Thickness);
        Assert.Equal(0.0, pavFlat, 6);
        Assert.Equal(0.10, pavRing, 6);
        var ramps = geo.Pieces.Where(p => p.Profile != null && p.Color == MarkingColor.Asfalto).ToList();
        Assert.Equal(4, ramps.Count);
        Assert.All(ramps, r => Assert.Equal(7.0, r.Profile!.Depth, 6));
        Assert.Contains(geo.Warnings, w => w.Contains("elevada"));
        var back = (RoundaboutDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.True(back.Raised);
    }

    [Fact]
    public void Roundabout_DomeIsland_IsLoftedCone()
    {
        var d = Rb(x => { x.IslandType = TipoIlhaCentral.Calota; x.DomeHeight = 0.2; });
        var L = RoundaboutGenerator.Layout(d);
        Assert.Empty(L.IslandCurb);
        var geo = MarkingBuilder.Build(d, null, Ctx);
        var dome = geo.Pieces.Where(p => p.Solid != null && p.Color == MarkingColor.PavimentoConcreto).ToList();
        Assert.Single(dome);
        Assert.Equal(0.2, dome[0].Solid!.MaxZ, 6);
        Assert.True(dome[0].Shape.Contains(d.Center));
    }

    [Fact]
    public void Quantities_MemorialRows_OnePerSignModel()
    {
        var items = new List<(MarkingDefinition, MarkingGeometry)>();
        foreach (var i in Enumerable.Range(0, 3))
        {
            var sign = new SignDefinition { Code = "R-1", Position = new Vec2(i * 10, 0), Direction = new Vec2(1, 0) };
            items.Add((sign, MarkingBuilder.Build(sign, null, Ctx)));
        }
        var line = new LinearMarkingDefinition { Code = "LFO-1" };
        items.Add((line, MarkingBuilder.Build(line, Straight(20), Ctx)));
        var rows = QuantityCalculator.Compute(items, Cat);
        var r1 = rows.Where(r => r.Code == "R-1").ToList();
        Assert.Single(r1);
        Assert.True(r1[0].Memorial);
        Assert.Equal(3, r1[0].Units);
        Assert.Equal(3, r1[0].Elements);
        Assert.Equal("", r1[0].Material);
        Assert.Equal(0, r1[0].MaterialConsumption);
        Assert.Equal("total", r1[0].AreaKind);
        var lfo = rows.Single(r => r.Code == "LFO-1");
        Assert.False(lfo.Memorial);
        Assert.NotEqual("", lfo.Material);
    }

    [Fact]
    public void RoadSetup_StoresSectionAndReusesIds()
    {
        var setup = RoadTemplates.All[0].Create();
        setup.Hierarchy = HierarquiaViaria.Local;
        var path = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(50, 0) }, 0);
        var defs = setup.Build(path, new OutputSettings(), Cat, "grupo-x", "pav-x");
        var pav = defs.OfType<RoadPavementDefinition>().Single();
        Assert.Equal("pav-x", pav.Id);
        Assert.All(defs, d => Assert.Equal("grupo-x", d.GroupId));
        Assert.NotNull(pav.SetupJson);
        var again = RoadTemplates.FromJson(pav.SetupJson);
        Assert.NotNull(again);
        Assert.Equal(setup.Right.Count, again!.Right.Count);
        Assert.Equal(setup.TotalWidth, again.TotalWidth, 6);
    }
}

public class V15SignTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    [Theory]
    [InlineData(TipoSuporte.BracoProjetado, 1)]
    [InlineData(TipoSuporte.SemiPortico, 1)]
    [InlineData(TipoSuporte.Portico, 2)]
    public void OverheadSupports_ColumnsAndBeam(TipoSuporte support, int columns)
    {
        var d = new SignDefinition { Code = "R-1", Position = Vec2.Zero, Direction = new Vec2(0, 1), Support = support, MountHeight = 5.5, LateralOffset = 6, StructureSpan = 12 };
        var geo = MarkingBuilder.Build(d, null, Ctx);
        Assert.True(d.Overhead);
        var cols = geo.Pieces.Where(p => p.Color == MarkingColor.Metal && p.Profile == null && p.Solid == null && p.Elevation < 0.5 && p.Thickness > 6).ToList();
        Assert.Equal(columns, cols.Count);
        Assert.Contains(geo.Pieces, p => p.Profile != null && p.Color == MarkingColor.Metal && Math.Abs(p.Profile!.Depth - 12.3) < 1e-6);
        Assert.DoesNotContain(geo.Warnings, w => w.Contains("5,50"));
        d.MountHeight = 4.0;
        Assert.Contains(MarkingBuilder.Build(d, null, Ctx).Warnings, w => w.Contains("5,50"));
        if (support == TipoSuporte.Portico) Assert.Contains(cols, c => c.Shape.Centroid.X > 11.5);
    }
}

public class V16Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    [Fact]
    public void SectionMatch_ReadsServiceAndGutterFromRoad()
    {
        var setup = RoadTemplates.All[1].Create();
        setup.Hierarchy = HierarquiaViaria.Local;
        var defs = setup.Build(PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(50, 0) }, 0), new OutputSettings(), Cat);
        var pav = defs.OfType<RoadPavementDefinition>().Single();
        var sec = SectionMatch.From(pav, RoadSetup.CurbWidth);
        Assert.NotNull(sec);
        Assert.True(sec!.Value.HasService);
        Assert.True(sec.Value.Grass);
        Assert.True(sec.Value.HasGutter);
        Assert.Null(SectionMatch.From(new RoadPavementDefinition(), 0.15));
    }

    [Fact]
    public void Roundabout_And_CulDeSac_UseRoadComposition()
    {
        var rb = new RoundaboutDefinition();
        rb.ApplyPreset(TipoRotatoria.Compacta);
        rb.ServiceStripWidth = 0.7; rb.ServiceStripGrass = true; rb.GutterWidth = 0.3;
        foreach (var a in new[] { 0.0, 90, 180, 270 }) rb.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = 2.5 });
        var L = RoundaboutGenerator.Layout(rb);
        Assert.NotEmpty(L.SidewalkService);
        Assert.NotEmpty(L.Gutter);
        var geo = MarkingBuilder.Build(rb, null, Ctx);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Grama);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Concreto && Math.Abs(p.Thickness - 0.005) < 1e-9);   // sarjeta
        Assert.Contains(geo.Pieces, p => p.Layer == "MEIO-FIO");   // meio-fio como piso próprio

        var c = new CulDeSacDefinition { ServiceStripWidth = 0.7, GutterWidth = 0.3, SidewalkWidth = 2.5 };
        var cg = MarkingBuilder.Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 25) }), Ctx);
        Assert.Contains(cg.Pieces, p => p.Color == MarkingColor.Grama);
        Assert.Contains(cg.Pieces, p => p.Color == MarkingColor.Concreto && Math.Abs(p.Thickness - 0.005) < 1e-9);
    }

    [Fact]
    public void Roundabout_IslandPavedRingAndRaisedGrass()
    {
        var rb = new RoundaboutDefinition();
        rb.ApplyPreset(TipoRotatoria.UmaFaixa);
        rb.IslandPavedRing = 1.0; rb.GrassRaise = 0.3;
        foreach (var a in new[] { 0.0, 180 }) rb.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = 2.5 });
        var geo = MarkingBuilder.Build(rb, null, Ctx);
        var grass = geo.Pieces.Where(p => p.Color == MarkingColor.Grama).ToList();
        Assert.NotEmpty(grass);
        Assert.Contains(grass, g => g.Elevation < 1e-9 && Math.Abs(g.Thickness - (rb.CurbHeight + 0.3)) < 1e-6);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Concreto && p.Shape.Contains(new Vec2(rb.IslandRadius - 0.5, 0)));
    }

    [Fact]
    public void Workbook_IsValidPackageWithSheetStylesAndImages()
    {
        var sign = new SignDefinition { Code = "R-1", Position = Vec2.Zero, Direction = new Vec2(1, 0) };
        var line = new LinearMarkingDefinition { Code = "LFO-1" };
        var rows = QuantityCalculator.Compute(new List<(MarkingDefinition, MarkingGeometry)>
        {
            (sign, MarkingBuilder.Build(sign, null, Ctx)),
            (line, MarkingBuilder.Build(line, new Polyline2(new[] { new Vec2(0, 0), new Vec2(20, 0) }), Ctx)),
        }, Cat);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0 };
        var bytes = QuantityWorkbook.Build(rows, QuantityCalculator.Summary(rows), "Projeto X", _ => png);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);
        Assert.Contains("xl/styles.xml", names);
        Assert.Contains("xl/drawings/drawing1.xml", names);
        Assert.Contains("xl/media/image1.png", names);
        using var sr = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = sr.ReadToEnd();
        Assert.Contains("Projeto: Projeto X", sheet);
        Assert.Contains("SUBTOTAL", sheet);
        Assert.Contains("<drawing r:id=\"rId1\"/>", sheet);
        foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
        {
            using var s = e.Open();
            var doc = new System.Xml.XmlDocument();
            doc.Load(s);   // XML bem formado
        }
        var csv = QuantityCalculator.ToCsv(rows, null, "Projeto X");
        Assert.Contains("Item;Código;Descrição;Quantidade", csv);
    }
}

public class V17Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static (RoadPavementDefinition Pav, List<MarkingDefinition> Defs) Road()
    {
        var setup = RoadTemplates.All[1].Create();
        setup.Hierarchy = HierarquiaViaria.Local;
        var defs = setup.Build(PathReference.FromPoints(new[] { new Vec2(-60, 0), new Vec2(60, 0) }, 0), new OutputSettings(), Cat);
        return (defs.OfType<RoadPavementDefinition>().Single(), defs);
    }

    [Fact]
    public void EdgeProfile_ReadsRealElementsOfTheRoad()
    {
        var (pav, defs) = Road();
        var prof = EdgeProfile.FromRoad(pav, defs, Ctx);
        Assert.Contains(prof, b => b.Code == "SARJETA" && b.Inside);
        var curb = prof.Single(b => b.Code == "MEIO-FIO");
        Assert.Equal(0, curb.D0, 3);
        Assert.Equal(0.15, curb.D1, 3);
        Assert.Contains(prof, b => b.Code == "GRAMADO" && b.Color == MarkingColor.Grama);
        Assert.Contains(prof, b => b.Code == "CALCADA");
        Assert.Equal(pav.RightSidewalk, EdgeProfile.OuterWidth(prof), 2);
        // Bandas contíguas, sem buracos, do meio-fio ao fim da calçada
        var outer = prof.Where(b => !b.Inside).OrderBy(b => b.D0).ToList();
        for (int i = 1; i < outer.Count; i++) Assert.Equal(outer[i - 1].D1, outer[i].D0, 2);
    }

    [Fact]
    public void Roundabout_WithRoadProfile_HasCurbGutterGrassAndWalkLayers()
    {
        var (pav, defs) = Road();
        var d = new RoundaboutDefinition();
        d.ApplyPreset(TipoRotatoria.UmaFaixa);
        foreach (var a in new[] { 45.0, -45, 225 }) d.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = 3 });
        d.EdgeProfile = EdgeProfile.FromRoad(pav, defs, Ctx);
        d.SidewalkWidth = EdgeProfile.OuterWidth(d.EdgeProfile) - d.CurbWidth;
        var geo = RoundaboutGenerator.Build(d, Ctx);
        var layers = geo.Pieces.Select(p => p.Layer).Where(l => l != null).Distinct().ToList();
        Assert.Contains("MEIO-FIO", layers);
        Assert.Contains("SARJETA", layers);
        Assert.Contains("GRAMADO", layers);
        Assert.Contains("CALCADA", layers);
        // A sarjeta não se sobrepõe ao pavimento (sem pisos sobrepostos no Revit)
        var gutter = geo.Pieces.Where(p => p.Layer == "SARJETA").Select(p => p.Shape).ToList();
        var asphalt = geo.Pieces.Where(p => p.Color == MarkingColor.Asfalto && p.Profile == null).Select(p => p.Shape).ToList();
        Assert.True(PolygonOps.Intersect(gutter, asphalt).Sum(p => p.Area) < 0.05);
    }

    [Fact]
    public void Roundabout_CornersHaveNoNotch()
    {
        var d = new RoundaboutDefinition();
        d.ApplyPreset(TipoRotatoria.UmaFaixa);
        foreach (var a in new[] { 45.0, -45, 225 }) d.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = 3 });
        var L = RoundaboutGenerator.Layout(d);
        // Entre os ramos a 45° e −45°, a pista deve alcançar o círculo externo do anel (sem dente para dentro).
        var probe = d.Center + new Vec2(d.OuterRadius - 0.3, 0);
        Assert.Contains(L.Pavement, p => p.Contains(probe));
    }

    [Fact]
    public void CulDeSac_WithRoadProfile_NoLineAcrossTheStart()
    {
        var (pav, defs) = Road();
        var c = new CulDeSacDefinition { EdgeProfile = EdgeProfile.FromRoad(pav, defs, Ctx), Island = true };
        c.SidewalkWidth = EdgeProfile.OuterWidth(c.EdgeProfile) - c.CurbWidth;
        var geo = MarkingBuilder.Build(c, new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 30) }), Ctx);
        Assert.Contains(geo.Pieces, p => p.Layer == "MEIO-FIO");
        Assert.Contains(geo.Pieces, p => p.Layer == "SARJETA");
        Assert.Contains(geo.Pieces, p => p.Layer == "GRAMADO");
        Assert.DoesNotContain(geo.Pieces, p => p.Color == MarkingColor.Branca && p.Shape.Bounds.Min.Y < 0.45);
    }

    [Fact]
    public void QuantitySamples_ShowTheSignItself()
    {
        var (g1, k1) = QuantitySamples.Sample(new LinearMarkingDefinition { Code = "LFO-2", Offset = 3.5 }, MarkingColor.Amarela, Ctx);
        Assert.Equal(TipoMiniatura.Planta, k1);
        Assert.All(g1!.Pieces, p => Assert.InRange(p.Shape.Centroid.Y, -0.5, 0.5));   // sem o deslocamento do projeto
        Assert.Equal(TipoMiniatura.Placa, QuantitySamples.Sample(new SignDefinition { Code = "R-1" }, MarkingColor.Branca, Ctx).Kind);
        Assert.Equal(TipoMiniatura.Material, QuantitySamples.Sample(new RoadPavementDefinition(), MarkingColor.Asfalto, Ctx).Kind);
        var (g4, k4) = QuantitySamples.Sample(new LinearMarkingDefinition { Code = "TACHAO" }, MarkingColor.Amarela, Ctx);
        Assert.Equal(TipoMiniatura.Perspectiva, k4);
        Assert.InRange(g4!.UnitCount, 1, 2);
        var (g5, _) = QuantitySamples.Sample(new SymbolMarkingDefinition { Code = "SDP", Length = 3.6, Position = new Vec2(500, 500) }, MarkingColor.Branca, Ctx);
        Assert.True(g5!.Pieces[0].Shape.Centroid.Length < 5);
    }

    [Fact]
    public void Workbook_HasNoColorColumn()
    {
        var line = new LinearMarkingDefinition { Code = "LFO-1" };
        var rows = QuantityCalculator.Compute(new List<(MarkingDefinition, MarkingGeometry)> { (line, MarkingBuilder.Build(line, new Polyline2(new[] { new Vec2(0, 0), new Vec2(20, 0) }), Ctx)) }, Cat);
        var bytes = QuantityWorkbook.Build(rows, null, "P", null);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using var sr = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = sr.ReadToEnd();
        Assert.Contains(">Imagem<", sheet);
        Assert.DoesNotContain(">Cor<", sheet);
    }
}
