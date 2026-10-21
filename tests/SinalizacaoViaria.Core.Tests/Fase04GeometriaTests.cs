using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Fase IV – geometria: arcos exatos da extensão de calçada e da borda que ela recorta nas marcas vizinhas (K11, par
/// MER-0 × ORELHA do 4º Autoteste completo: 0 cm² no núcleo, 13,95 cm² depois do ajuste de arcos do contorno do Revit); zebrado
/// por contorno com os recortes da marca também no caminho do Revit (FTP-1 × ZPA na minirrotatória).
/// </summary>
public class Fase04GeometriaTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static CurbExtensionDefinition Ear(TipoTransicao start, TipoTransicao? end = null, params Vec2[] path) => new()
    {
        Depth = 2.0,
        Radius = 1.5,
        Transition = start,
        EndTransition = end,
        PathRef = new PathReference { Points = path.Length > 0 ? path.ToList() : new List<Vec2> { new(0, 0), new(15, 0) } },
    };

    private static int OnCircle(MarkingGeometry geo, Vec2 c, double r) =>
        geo.Pieces.SelectMany(p => p.Shape.Outer).Count(v => Math.Abs(v.DistanceTo(c) - r) < 1e-3);

    [Fact]
    public void Extensao_CentrosDasCurvas_SaoOsDoContorno()
    {
        var d = Ear(TipoTransicao.Curva);
        var path = new Polyline2(d.PathRef.Points);
        var geo = MarkingBuilder.Build(d, path, new BuildContext { Catalog = Cat });
        // Duas curvas reversas em cada ponta.
        Assert.Equal(4, geo.ArcCenters.Count);
        // Cada centro é de fato o centro de um arco do contorno: vários vértices à distância R dele.
        foreach (var c in geo.ArcCenters)
            Assert.True(OnCircle(geo, c, d.Radius) >= 5, $"centro {c.X:0.000};{c.Y:0.000}: {OnCircle(geo, c, d.Radius)} vértices no raio");
    }

    [Fact]
    public void Extensao_PontasDiferentes_SoAsCurvasTemCentro()
    {
        var path = new Polyline2(new List<Vec2> { new(0, 0), new(15, 0) });
        Assert.Equal(2, SidewalkGenerator.EarArcCenters(Ear(TipoTransicao.Curva, TipoTransicao.Reta), path).Count);
        Assert.Empty(SidewalkGenerator.EarArcCenters(Ear(TipoTransicao.Chanfro), path));
        Assert.Empty(SidewalkGenerator.EarArcCenters(Ear(TipoTransicao.Reta), path));
    }

    [Fact]
    public void Extensao_MeioFioCurvoOuContornoLivre_SemCentrosInventados()
    {
        // Meio-fio curvo: o perfil se deforma na planta e os arcos deixam de ter centro fixo.
        var bent = new List<Vec2>();
        for (int i = 0; i <= 20; i++)
        {
            var a = i * Math.PI / 80;
            bent.Add(new Vec2(30 * Math.Sin(a), 30 - 30 * Math.Cos(a)));
        }
        Assert.Empty(SidewalkGenerator.EarArcCenters(Ear(TipoTransicao.Curva, null, bent.ToArray()), new Polyline2(bent)));
        // Contorno livre desenhado pelo usuário: os centros guardados (se houver) mandam.
        var free = Ear(TipoTransicao.Curva);
        free.OuterFace = new List<Vec2> { new(0, 0), new(2, 2), new(13, 2), new(15, 0) };
        Assert.Empty(SidewalkGenerator.EarArcCenters(free, new Polyline2(free.PathRef.Points)));
    }

    [Fact]
    public void Recorte_MantemOsCentrosDaMarca_EAcrescentaOsDeQuemRecortou()
    {
        var geo = new MarkingGeometry();
        geo.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(10, 4)), MarkingColor.Branca));
        geo.ArcCenters.Add(new Vec2(1, 1));
        var zones = new[]
        {
            new ExclusionZone { Points = Polygon2.Rectangle(new Vec2(4, -1), new Vec2(6, 5)).Outer.ToList(), ArcCenters = new List<Vec2> { new(5, 8), new(1, 1) } },
            new ExclusionZone { Points = Polygon2.Rectangle(new Vec2(8, -1), new Vec2(9, 5)).Outer.ToList(), ArcCenters = new List<Vec2> { new(9, 9) }, Enabled = false },
        };
        var res = MarkingBuilder.ApplyExclusions(geo, zones);
        Assert.Equal(2, res.Pieces.Count);
        // O da marca continua (antes se perdia), o da zona entra uma vez só, o da zona desligada não entra.
        Assert.Equal(new[] { new Vec2(1, 1), new Vec2(5, 8) }, res.ArcCenters);
    }

    private static List<MarkingDefinition> MerFixture() =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fase04-mer-orelha.json")))
            .RootElement.EnumerateArray().Select(e => MarkingDefinition.FromJson(e.GetRawText())!).ToList();

    [Fact]
    public void Autoteste4_VagaXExtensao_SemSobreposicaoNoContornoDoRevit()
    {
        var defs = MerFixture();
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => defs.FirstOrDefault(x => x.Id == id), AllDefinitions = () => defs };
        var mer = defs.OfType<ParkingMarkingDefinition>().Single();
        var ear = defs.OfType<CurbExtensionDefinition>().Single();
        Polyline2 PathOf(MarkingDefinition d) => d.Path!.Points.Count >= 2 ? new Polyline2(d.Path.Points) : new Polyline2(d.Path.Cache![0]);
        var gEar = MarkingBuilder.Build(ear, PathOf(ear), ctx);
        Assert.Equal(4, gEar.ArcCenters.Count);
        var re = ElementPlan.RevitPieces(ear, gEar).Where(PieceOverlaps.IsFloorPiece).ToList();
        var fe = FCRevitFullRun4Tests.Fitted(re, gEar.ArcCenters);
        double Ov(List<MarkingPiece> a, List<MarkingPiece> b) => a.Sum(x => b.Sum(y => PolygonOps.Intersect(new[] { x.Shape }, new[] { y.Shape }).Sum(q => q.Area)));

        // Recorte gravado antes da correção (zona sem centros): o contorno da extensão já usa os arcos exatos.
        var gMer = MarkingBuilder.Build(mer, PathOf(mer), ctx);
        var rm = ElementPlan.RevitPieces(mer, gMer).Where(PieceOverlaps.IsFloorPiece).ToList();
        Assert.True(Ov(rm, re) < 1e-6);
        Assert.True(Ov(FCRevitFullRun4Tests.Fitted(rm, gMer.ArcCenters), fe) < 1e-4, $"{Ov(FCRevitFullRun4Tests.Fitted(rm, gMer.ArcCenters), fe) * 1e4:0.00} cm²");

        // Recorte feito agora (FootprintCutter): a zona leva os centros da extensão e a borda da vaga usa os mesmos arcos.
        foreach (var z in mer.Exclusions.Where(z => z.SourceId == ear.Id)) z.ArcCenters = gEar.ArcCenters.ToList();
        var gMer2 = MarkingBuilder.Build(mer, PathOf(mer), ctx);
        Assert.All(gEar.ArcCenters, c => Assert.Contains(gMer2.ArcCenters, x => x.DistanceTo(c) < 1e-9));
        var rm2 = ElementPlan.RevitPieces(mer, gMer2).Where(PieceOverlaps.IsFloorPiece).ToList();
        Assert.True(Ov(FCRevitFullRun4Tests.Fitted(rm2, gMer2.ArcCenters), fe) < 1e-4);
    }
    [Fact]
    public void Autoteste4_ZebradoDaGota_RecortadoSobAFaixaNoCaminhoDoRevit()
    {
        var defs = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fase04-ftp-zpa.json")))
            .RootElement.EnumerateArray().Select(e => MarkingDefinition.FromJson(e.GetRawText())!).ToList();
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => defs.FirstOrDefault(x => x.Id == id), AllDefinitions = () => defs };
        var zpa = defs.OfType<HatchMarkingDefinition>().Single();
        var ftp = defs.OfType<LinearMarkingDefinition>().Single();
        Assert.Single(zpa.Exclusions);
        var contour = new Polyline2(zpa.Path!.Points);
        var rf = ElementPlan.RevitPieces(ftp, MarkingBuilder.Build(ftp, new Polyline2(ftp.Path!.Points), ctx)).Where(PieceOverlaps.IsFloorPiece).ToList();
        double Ov(MarkingGeometry g) => ElementPlan.RevitPieces(zpa, g).Where(PieceOverlaps.IsFloorPiece)
            .Sum(x => rf.Sum(y => PolygonOps.Intersect(new[] { x.Shape }, new[] { y.Shape }).Sum(q => q.Area)));
        // Sem o recorte (o que o Revit gerava: só os contornos, sem as zonas da marca) o zebrado passa sob a faixa.
        Assert.True(Ov(MarkingBuilder.BuildHatch(zpa, new[] { contour }, ctx)) > 0.1);
        // O caminho do Revit agora recorta como o núcleo.
        var area = MarkingBuilder.BuildHatchArea(zpa, new[] { contour }, ctx);
        Assert.True(Ov(area) < 1e-6, $"{Ov(area) * 1e4:0.00} cm²");
        Assert.Equal(MarkingBuilder.Build(zpa, contour, ctx).Pieces.Sum(p => p.Shape.Area), area.Pieces.Sum(p => p.Shape.Area), 6);
        // Zona desligada: o zebrado volta inteiro.
        zpa.Exclusions[0].Enabled = false;
        Assert.Equal(MarkingBuilder.BuildHatch(zpa, new[] { contour }, ctx).Pieces.Sum(p => p.Shape.Area),
            MarkingBuilder.BuildHatchArea(zpa, new[] { contour }, ctx).Pieces.Sum(p => p.Shape.Area), 6);
    }
}
