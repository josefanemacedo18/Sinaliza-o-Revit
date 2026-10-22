using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Fase IV – geometria: arcos exatos da extensão de calçada e da borda que ela recorta nas marcas vizinhas (K11, par
/// MER-0 × ORELHA do 4º Autoteste completo: 0 cm² no núcleo, 13,95 cm² depois do ajuste de arcos do contorno do Revit); zebrado
/// por contorno com os recortes da marca também no caminho do Revit (FTP-1 × ZPA na minirrotatória); ajuste topológico dos
/// contornos (K13): a borda comum a dois pisos da mesma marca é ajustada uma vez só (<see cref="BoundaryFit.FitRings"/>).
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
    // ------------------------------------------------------------------ ajuste topológico (K13)

    private const double Tol = 0.00256 * 0.3048 * 1.5;

    /// <summary>Faixa entre dois raios, como as operações booleanas a deixam: arcos em cordas (flecha de 5 mm).</summary>
    private static List<Vec2> Band(Vec2 c, double r0, double r1, double a0, double sweep)
    {
        var outer = CurveTools.Arc(c, r1, a0, sweep);
        var inner = CurveTools.Arc(c, r0, a0 + sweep, -sweep);
        return outer.Concat(inner).ToList();
    }

    private static double Overlap(List<List<BoundarySegment>?> fits)
    {
        var shapes = fits.Select(f => new Polygon2(BoundaryFit.Sample(f!, 0.0002))).ToList();
        var total = 0.0;
        for (int i = 0; i < shapes.Count; i++)
            for (int j = i + 1; j < shapes.Count; j++)
                total += PolygonOps.Intersect(new[] { shapes[i] }, new[] { shapes[j] }).Sum(q => q.Area);
        return total;
    }

    [Fact]
    public void Topologia_BordaCurvaComum_MesmoArcoNosDoisPisos()
    {
        // Meio-fio (6,00–6,15 m) e calçada (6,15–8,65 m) numa esquina de 90°, sem centro exato informado: cada piso ajustado
        // sozinho escolhia o próprio arco para a borda comum.
        var c = new Vec2(10, 10);
        var rings = new List<IReadOnlyList<Vec2>> { Band(c, 6.0, 6.15, Math.PI, Math.PI / 2), Band(c, 6.15, 8.65, Math.PI, Math.PI / 2) };
        var fits = BoundaryFit.FitRings(rings, BoundaryFit.DefaultTolerance, Tol, null, BoundaryFit.DefaultTolerance);
        Assert.All(fits, f => Assert.NotNull(f));
        // O arco comum (r = 6,15) é o mesmo nos dois anéis, percorrido em sentidos opostos.
        static bool On(BoundarySegment s, double r) => s.IsArc && Math.Abs(s.Circle.Radius - r) < 0.01;
        var a = Assert.Single(fits[0]!, s => On(s, 6.15));
        var b = Assert.Single(fits[1]!, s => On(s, 6.15));
        Assert.Equal(a.Start, b.End);
        Assert.Equal(a.End, b.Start);
        Assert.Equal(a.Circle.Center, b.Circle.Center);
        Assert.True(Overlap(fits) < 1e-6, $"{Overlap(fits) * 1e4:0.000} cm²");
    }

    [Fact]
    public void Topologia_JuncaoEmT_VerticeDoVizinhoEntraNaBorda()
    {
        // Pista com duas calçadas encostadas: o vértice onde elas se encontram não existe na borda da pista.
        var rings = new List<IReadOnlyList<Vec2>>
        {
            new List<Vec2> { new(0, 0), new(10, 0), new(10, 2), new(0, 2) },
            new List<Vec2> { new(0, 2), new(5, 2), new(5, 4), new(0, 4) },
            new List<Vec2> { new(5, 2), new(10, 2), new(10, 4), new(5, 4) },
        };
        var topo = BoundaryFit.Topology(rings, BoundaryFit.DefaultTolerance, Tol, null, BoundaryFit.DefaultTolerance);
        var road = Assert.IsType<List<BoundarySegment>>(topo[0]);
        Assert.Contains(road, s => s.Start == new Vec2(5, 2) || s.End == new Vec2(5, 2));
        Assert.True(Overlap(topo) < 1e-9);
    }

    [Fact]
    public void Topologia_TrechoComVerticesProprios_UnificadoDentroDaTolerancia()
    {
        // A borda comum tem, só no segundo anel, um dente de 3 mm entre dois vértices comuns: os dois passam a ter o mesmo
        // trecho (o do primeiro) e o ajuste da borda sai igual.
        var rings = new List<IReadOnlyList<Vec2>>
        {
            new List<Vec2> { new(0, 0), new(10, 0), new(10, 2), new(0, 2) },
            new List<Vec2> { new(0, 2), new(10, 2), new(10, 4), new(5.1, 4), new(4.9, 4), new(0, 4) },
            new List<Vec2> { new(0, 4), new(4.9, 4), new(5, 4.003), new(5.1, 4), new(10, 4), new(10, 6), new(0, 6) },
        };
        var topo = BoundaryFit.Topology(rings, BoundaryFit.DefaultTolerance, Tol, null, BoundaryFit.DefaultTolerance);
        Assert.All(topo, f => Assert.NotNull(f));
        var top = topo[1]!.Where(s => s.Start.Y > 3.9 && s.End.Y > 3.9).SelectMany(s => new[] { s.Start, s.End }).ToHashSet();
        var bottom = topo[2]!.Where(s => s.Start.Y < 4.1 && s.End.Y < 4.1).SelectMany(s => new[] { s.Start, s.End }).ToHashSet();
        Assert.True(top.SetEquals(bottom), string.Join(" ", top) + " | " + string.Join(" ", bottom));
        Assert.True(Overlap(topo) < 1e-9);
    }

    [Fact]
    public void Topologia_TrechoAberto_PontasPresasNoAlinhamentoAosEixos()
    {
        // Reta quase horizontal (resíduo de 1 mm no meio): o vértice do meio vai para a reta das pontas, que não andam.
        var chain = new List<BoundarySegment> { new(new Vec2(0, 0), new Vec2(5, 0.0004)), new(new Vec2(5, 0.0004), new Vec2(10, 0)) };
        var snapped = AxisSnap.SnapChain(chain, 0.005);
        Assert.Equal(new Vec2(0, 0), snapped[0].Start);
        Assert.Equal(new Vec2(10, 0), snapped[^1].End);
        Assert.Equal(0, snapped[0].End.Y, 12);
        // Pontas presas fora da mesma reta: nada muda (mover uma delas desencontraria o piso vizinho).
        var tilted = new List<BoundarySegment> { new(new Vec2(0, 0), new Vec2(10, 0.003)) };
        Assert.Equal(tilted, AxisSnap.SnapChain(tilted, 0.005));
    }

    [Fact]
    public void Autoteste4_Rotatorias_PisosDaMesmaMarcaSemLascas()
    {
        // 4º Autoteste completo: ROTATORIA × ROTATORIA [mesma marca] 36 pares. Na emulação do ajuste piso a piso eram 9,6 a
        // 67 L por rotatória; com o ajuste topológico, até 40 cm³. A com by-pass entrou na Fase VII (K16: a tira de meio-fio
        // de 2 cm colada na ilha do by-pass).
        foreach (var d in FCRevitFullRun4Tests.Fixture())
        {
            var geo = MarkingBuilder.Build(d, null, new BuildContext { Catalog = Cat });
            var raw = ElementPlan.RevitPieces(d, geo).Where(PieceOverlaps.IsFloorPiece).ToList();
            var fitted = PieceOverlaps.Find(FCRevitFullRun4Tests.Fitted(raw, geo.ArcCenters)).Sum(o => o.Volume) * 1e6;
            Assert.True(fitted < 100, $"{d.Type} {d.Id[..6]}: {fitted:0} cm³");
        }
    }
}
