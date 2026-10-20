using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// FA – 3º Autoteste completo no Revit 2027 (2026-10-20 15:12, docs/evidencias/2026-10-20-autoteste-completo-3.txt): 178
/// etapas, 1 erro (forma direta recusada na rotatória oval), 8 retas fora do eixo (rotatória com by-pass) e 590 pares de pisos
/// sobrepostos, agora com as cotas e a espessura do tipo de cada piso dos exemplos.
/// </summary>
public class FARevitFullRun3Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private const double Tol = 0.00256 * 0.3048 * 1.5;   // pontos colados no Revit (1,5 × ShortCurveTolerance)

    /// <summary>Rotatória sobre duas vias que se cruzam, na posição em que o Autoteste a cria (coordenadas grandes).</summary>
    private static (RoundaboutDefinition D, MarkingGeometry Geo, Vec2 C) Roundabout(TipoRotatoria type)
    {
        var k = (int)type;
        var c = new Vec2(3800 + 440 + 230 * (k % 3), -1200 + 650 + 230 * (k / 3));
        var roads = new List<IntersectionRoad>();
        foreach (var (a, b) in new[] { (c + new Vec2(-100, 0), c + new Vec2(100, 0)), (c + new Vec2(0, -100), c + new Vec2(0, 100)) })
        {
            var pr = PathReference.FromPoints(new[] { a, b }, 0);
            var g = RoadTemplates.All[0].Create().Build(pr, new OutputSettings(), Cat);
            roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), new Polyline2(pr.Points)));
        }
        var d = new RoundaboutDefinition { Center = c };
        d.ApplyPreset(type);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, roads, d.OuterRadius + 25);
        return (d, MarkingBuilder.Build(d, null, new BuildContext { Catalog = Cat }), c);
    }

    /// <summary>O anel como o plugin manda ao Revit (pontos colados e espinhos fora, ajuste conferido).</summary>
    private static List<BoundarySegment> RevitRing(IReadOnlyList<Vec2> ring, IReadOnlyList<Vec2> centers)
    {
        var pts = new List<Vec2>();
        foreach (var v in ring) if (pts.Count == 0 || pts[^1].DistanceTo(v) > Tol) pts.Add(v);
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= Tol) pts.RemoveAt(pts.Count - 1);
        pts = ProfileCleanup.Prepare(pts, Tol);
        if (BoundaryFit.FitRing(pts, BoundaryFit.DefaultTolerance, Tol, centers, BoundaryFit.DefaultTolerance) is { } segs) return segs;
        var lines = AxisSnap.SnapRing(pts, BoundaryFit.DefaultTolerance);
        return lines.Select((p, i) => new BoundarySegment(p, lines[(i + 1) % lines.Count])).ToList();
    }

    public static IEnumerable<object[]> RoundaboutTypes() => Enum.GetValues<TipoRotatoria>().Select(t => new object[] { t });

    /// <summary>
    /// Todos os pisos das rotatórias na posição do Autoteste: contornos que não se tocam e nenhuma reta fora do eixo. Antes:
    /// a gola das árvores da ilha saía como um contorno que se toca (4 vigas encostadas, giradas), um anel da calçada do
    /// by-pass se cruzava depois do ajuste e 8 retas de 22 m ficavam a 0,11° (a ponta de uma lasca era tirada inteira).
    /// </summary>
    [Theory]
    [MemberData(nameof(RoundaboutTypes))]
    public void Rotatorias_PisosValidosESemRetasForaDoEixo(TipoRotatoria type)
    {
        var (d, geo, _) = Roundabout(type);
        var bad = new List<string>();
        var n = 0;
        foreach (var pe in ElementPlan.Plan(d, geo).Where(p => p.IsFloor))
            foreach (var sh in pe.Shapes)
            {
                n++;
                var prep = ElementPlan.Prepare(sh);
                var rings = new[] { prep.Outer }.Concat(prep.Holes).Select(r => (IReadOnlyList<BoundarySegment>)RevitRing(r, geo.ArcCenters)).ToList();
                if (!BoundaryFit.IsSimple(rings, 1e-5)) bad.Add($"piso {n} ({pe.Color}, {sh.Area:0.###} m²) se cruza ou se toca");
                foreach (var s in rings.SelectMany(r => r))
                    if (AxisSnap.OffAxisDeg(s) is > 1e-9 and <= AxisSnap.MaxAngleDeg) bad.Add($"piso {n}: reta de {s.Start.DistanceTo(s.End):0.00} m a {AxisSnap.OffAxisDeg(s):0.0000}°");
            }
        Assert.True(n > 5);
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    /// <summary>A gola da árvore é uma peça só (moldura com furo), em qualquer rotação.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(160)]
    public void Arvore_GolaEmPecaUnicaComFuro(double deg)
    {
        var tree = Cat.Movel("ARVORE")!;
        var pieces = UrbanGenerator.BuildAt(tree, new LocalFrame(new Vec2(4240.3, -550.7), Vec2.FromAngle(deg * Math.PI / 180)), null, null, 5.0, null).Pieces
            .Where(p => p.Solid == null && p.Profile == null && p.Color == MarkingColor.Concreto).ToList();
        Assert.Single(pieces);
        Assert.Single(pieces[0].Shape.Holes);
        Assert.Null(PolygonChecks.Problem(pieces[0].Shape.Outer, 1e-5));
    }

    /// <summary>Peças encostadas que, giradas, não coincidem na grade: a união do piso volta a ter contorno e furo.</summary>
    [Fact]
    public void UniaoDoPiso_PecasEncostadasGiradas_ContornoEFuro()
    {
        var c = new Vec2(4240.3, -550.7);
        var rot = Vec2.FromAngle(40 * Math.PI / 180);
        Polygon2 Box(double x, double y, double w, double h)
        {
            Vec2 R(double px, double py) => c + rot * px + rot.PerpLeft * py;
            return new Polygon2(new List<Vec2> { R(x - w / 2, y - h / 2), R(x + w / 2, y - h / 2), R(x + w / 2, y + h / 2), R(x - w / 2, y + h / 2) });
        }
        const double pit = 1.2, o = pit / 2;
        var boxes = new[] { Box(0, o - 0.04, pit, 0.08), Box(0, -o + 0.04, pit, 0.08), Box(o - 0.04, 0, 0.08, pit - 0.16), Box(-o + 0.04, 0, 0.08, pit - 0.16) };
        Assert.Contains(PolygonOps.Union(boxes), p => PolygonChecks.Problem(p.Outer, 1e-5) != null);   // a reprodução
        var u = ElementPlan.FloorUnion(boxes);
        Assert.Single(u);
        Assert.Single(u[0].Holes);
        Assert.Null(PolygonChecks.Problem(u[0].Outer, 1e-5));
        Assert.Equal(0.3584, u[0].Area, 3);
    }

    /// <summary>Espinho (lasca de 1,7 m da calçada do by-pass): a ponta é cortada e o lado longo continua exato.</summary>
    [Fact]
    public void Espinho_PontaCortadaNoLadoLongo()
    {
        var ring = new List<Vec2> { new(0, 0), new(-24.3444, 0), new(-22.6617, 0.0443), new(-16.8335, 0.7229), new(0, 1) };
        var p = ProfileCleanup.Prepare(ring, Tol);
        Assert.Contains(new Vec2(-22.6617, 0), p);
        var segs = p.Select((v, i) => new BoundarySegment(v, p[(i + 1) % p.Count])).ToList();
        Assert.Equal(0, AxisSnap.CountNearAxis(segs));
    }

    private static List<(string A, string B, double Volume)> FloorOverlaps(RoadSetup setup, Polyline2 axis)
    {
        var all = setup.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, axis: axis).ToList();
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => all.FirstOrDefault(x => x.Id == id), PathOf = x => x.Path != null ? axis : null, AllDefinitions = () => all };
        var pieces = new List<MarkingPiece>();
        var codes = new List<string>();
        foreach (var d in all)
            foreach (var p in ElementPlan.RevitPieces(d, MarkingBuilder.Build(d, d.Path is { Points.Count: >= 2 } ? axis : null, ctx)))
            {
                pieces.Add(p);
                codes.Add(d.DisplayCode);
            }
        return PieceOverlaps.Find(pieces).Where(o => FootprintCutterCodes.Contains(codes[o.A]) && FootprintCutterCodes.Contains(codes[o.B]))
            .Select(o => (codes[o.A], codes[o.B], o.Volume)).ToList();
    }

    private static readonly string[] FootprintCutterCodes = { "PAV-ASF", "SARJETA", "MEIO-FIO", "GRAMADO", "CALCADA" };

    /// <summary>
    /// "Baia de ônibus, faixa de desaceleração e de aceleração" do Autoteste: a calçada (2,8 m livres) estreitava com
    /// largura negativa sob recuos de 3,0 e 3,5 m e a grama passava por cima dela e do lote; no fim sem transição da faixa de
    /// desaceleração cada faixa fazia uma perna transversal sobre as vizinhas (no Revit: 120 991 cm³ de meio-fio × grama).
    /// </summary>
    [Fact]
    public void Recuos_CalcadaMeioFioGramaSarjetaSemSobreposicao()
    {
        var setup = RoadTemplates.All[2].Create();
        setup.Recuos.Add(RecuoVia.Padrao(TipoRecuo.BaiaOnibus, setup.Speed, 3.5)); setup.Recuos[^1].Estaca = 40;
        setup.Recuos.Add(RecuoVia.Padrao(TipoRecuo.FaixaDesaceleracao, setup.Speed, 3.5)); setup.Recuos[^1].Estaca = 140;
        setup.Recuos.Add(RecuoVia.Padrao(TipoRecuo.FaixaAceleracao, setup.Speed, 3.5)); setup.Recuos[^1].Estaca = 260; setup.Recuos[^1].LadoEsquerdo = true;
        var ov = FloorOverlaps(setup, new Polyline2(new[] { new Vec2(4120, -950), new Vec2(4500, -950) }));
        Assert.True(ov.Count == 0, string.Join("; ", ov.Select(o => $"{o.A} × {o.B} {o.Volume * 1e6:0} cm³")));
        Assert.Contains(setup.Warnings, w => w.Contains("alinhamento avança"));
    }

    /// <summary>"Recuo (baia) acrescentado numa via já criada": calçada inclinada e com níveis no alinhamento, baia de 2 m.</summary>
    [Fact]
    public void Recuo_CalcadaComNiveis_SemSobreposicao()
    {
        var setup = RoadTemplates.All[0].Create();
        foreach (var e in setup.Right.Concat(setup.Left).Where(e => e.Tipo == TipoElementoSecao.Calcada))
        {
            e.InclinacaoTransversal = 2.0;
            e.NiveisAlinhamento = new List<NivelAlinhamento> { new() { Estaca = 0, Nivel = 0 }, new() { Estaca = 60, Nivel = 0.35 }, new() { Estaca = 120, Nivel = 0.10 } };
        }
        var r = RecuoVia.Padrao(TipoRecuo.BaiaOnibus, setup.Speed, 3.3);
        r.Estaca = 60;
        r.Profundidade = 2.0;
        setup.Recuos.Add(r);
        var ov = FloorOverlaps(setup, new Polyline2(new[] { new Vec2(3800, -1080), new Vec2(4000, -1080) }));
        Assert.True(ov.Count == 0, string.Join("; ", ov.Select(o => $"{o.A} × {o.B} {o.Volume * 1e6:0} cm³")));
    }

    [Theory]
    [InlineData(0.12, 0.12)]
    [InlineData(0.1504, 0.15)]
    [InlineData(0.0049, 0.005)]
    public void TipoDePiso_EspessuraArredondadaComoNoNome(double t, double expected) => Assert.Equal(expected, FloorTypeNames.Rounded(t), 9);
}
