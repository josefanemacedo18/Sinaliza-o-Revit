using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// EZ – 3º Autoteste completo no Revit 2027 (2026-10-20 09:44, docs/evidencias/2026-10-20-autoteste-completo-2.txt):
/// 178 etapas, 0 erros; 85 retas fora do eixo (eram 1 385) e 622 pares de pisos que continuam sobrepostos, agora listados
/// por tipo no relatório.
/// </summary>
public class EZRevitFullRun2Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static List<BoundarySegment> Ring(params (double X, double Y)[] p) =>
        p.Select((a, i) => new BoundarySegment(new Vec2(a.X, a.Y), new Vec2(p[(i + 1) % p.Length].X, p[(i + 1) % p.Length].Y))).ToList();

    /// <summary>
    /// Reprodução do "Canalização – TransicaoLargura/Acostamento" (MTL 14 e MAP 13 retas a 0,0002°): numa barra de zebrado a
    /// 45° com a ponta cortada na horizontal, alinhar a reta de 45° movia o vértice da quina só na direção dela e entortava a
    /// horizontal, que já era exata. Agora a reta exata entra no grupo e o vértice vai ao encontro das duas.
    /// </summary>
    [Fact]
    public void AxisSnap_QuinaComRetaExata_NaoEntortaAVizinha()
    {
        // Barra a ~45° (resíduo de 0,07 mm) entre duas horizontais exatas.
        var r = Ring((0, 0), (2.121, 0), (4.68907, 2.568), (2.568, 2.568));
        Assert.Equal(1, AxisSnap.CountNearAxis(r));
        var s = AxisSnap.Snap(r, 0.005);
        Assert.Equal(0, AxisSnap.CountNearAxis(s));
        Assert.Equal(s[0].Start.Y, s[0].End.Y);
        Assert.Equal(s[2].Start.Y, s[2].End.Y);
    }

    [Fact]
    public void AxisSnap_ContornoJaExato_FicaIdentico()
    {
        var r = Ring((0, 0), (20, 0), (20, 10), (10, 20), (0, 10));
        Assert.True(r.SequenceEqual(AxisSnap.Snap(r, 0.005)));
    }

    /// <summary>Canalizações (zebrados de transição de largura, acostamento, obstáculo): nenhuma reta que o Revit avisaria depois do ajuste e do alinhamento.</summary>
    [Fact]
    public void Canalizacoes_SemRetasForaDoEixo()
    {
        var ctx = new BuildContext { Catalog = Cat };
        int before = 0, after = 0;
        foreach (var v in Enum.GetValues<TipoCanalizacao>())
        {
            var d = new ChannelizationDefinition { Type = v, PathRef = PathReference.FromPoints(new[] { new Vec2(3800, 300), new Vec2(4020, 300) }, 0) };
            var g = MarkingBuilder.Build(d, new Polyline2(d.PathRef.Points), ctx);
            foreach (var pe in ElementPlan.Plan(d, g).Where(p => p.IsFloor))
                foreach (var sh in pe.Shapes)
                    foreach (var ring in BoundaryFit.FitPolygon(ElementPlan.Prepare(sh), BoundaryFit.DefaultTolerance, 0.0018, g.ArcCenters))
                    {
                        before += AxisSnap.CountNearAxis(ring);
                        after += AxisSnap.CountNearAxis(AxisSnap.Snap(ring, BoundaryFit.DefaultTolerance));
                    }
        }
        Assert.True(before > 10, $"reprodução: só {before} reta(s) fora do eixo depois do ajuste – revise o teste");
        Assert.Equal(0, after);
    }

    private static List<(string A, string B)> RoundaboutPaintOverlaps(TipoRotatoria type)
    {
        // Como o Autoteste: duas vias locais atravessando o centro, ramos lidos das vias, recortes e sinalização dos ramos.
        var w = new Y34TipTests.World();
        foreach (var (a, b) in new[] { (new Vec2(-100, 0), new Vec2(100, 0)), (new Vec2(0, -100), new Vec2(0, 100)) })
        {
            var pr = PathReference.FromPoints(new[] { a, b }, 0);
            var g = RoadTemplates.All[0].Create().Build(pr, new OutputSettings(), Cat);
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        var d = new RoundaboutDefinition { Center = Vec2.Zero };
        d.ApplyPreset(type);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, w.Roads, d.OuterRadius + 25);
        var L = RoundaboutGenerator.Layout(d);
        foreach (var g in w.Groups)
            foreach (var m in g)
                foreach (var cut in RoundaboutGenerator.RoadCuts(d, L, m))
                    m.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = cut.Outer.ToList() });
        w.Defs.Add(d);
        foreach (var c in RoundaboutGenerator.Children(d, L, new OutputSettings(), 0)) { c.GroupId = d.Id; w.Defs.Add(c); }
        var all = w.Defs.ToList();
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => all.FirstOrDefault(x => x.Id == id), PathOf = x => w.Paths.GetValueOrDefault(x.Id), AllDefinitions = () => all };
        var pieces = new List<MarkingPiece>();
        var codes = new List<string>();
        foreach (var def in all)
        {
            var path = w.Paths.GetValueOrDefault(def.Id) ?? (def.Path is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points, pr.Closed) : null);
            foreach (var p in ElementPlan.RevitPieces(def, MarkingBuilder.Build(def, path, ctx)))
            {
                pieces.Add(p);
                codes.Add(def.DisplayCode);
            }
        }
        return PieceOverlaps.Find(pieces).Where(o => MarkingColors.IsPaint(pieces[o.A].Color) && MarkingColors.IsPaint(pieces[o.B].Color))
            .Select(o => (codes[o.A], codes[o.B])).ToList();
    }

    /// <summary>
    /// Entradas das rotatórias: o triângulo "dê a preferência" (SDP) saía da posição no sentido do anel e entrava nos dentes da
    /// linha (LDP × SDP em todas as entradas no Autoteste); na minirrotatória, com a travessia a 5 m, passava sobre a faixa de
    /// pedestres, e a faixa sobre o zebrado da gota pintada. Nenhuma pintura sobre pintura nas entradas.
    /// </summary>
    [Theory]
    [InlineData(TipoRotatoria.UmaFaixa)]
    [InlineData(TipoRotatoria.Mini)]
    [InlineData(TipoRotatoria.Compacta)]
    [InlineData(TipoRotatoria.DuasFaixas)]
    public void Rotatorias_SinalizacaoDasEntradasSemPinturaSobrePintura(TipoRotatoria type)
    {
        var ov = RoundaboutPaintOverlaps(type);
        Assert.True(ov.Count == 0, string.Join("; ", ov.Select(o => $"{o.A} × {o.B}")));
    }

    // ------------------------------------------------------------------ retas longas fora do eixo (rotatórias, via reconhecida)

    /// <summary>
    /// Contornos dos pisos das rotatórias (pista com os ramos), ajustados e alinhados como no plugin: o Autoteste teve 2 a 9
    /// retas de 17 a 28 m por rotatória a 0,03°–0,12° do eixo (a ponta a 10–44 mm) – o bordo do ramo pegava o começo da
    /// curva de entrada (pontos removidos na volta do anel em <see cref="BoundaryFit.Significant"/>) ou a quina do bordo com
    /// o círculo da zona (9 mm) saía na simplificação e ficava o ponto do círculo.
    /// </summary>
    [Theory]
    [MemberData(nameof(RoundaboutTypes))]
    public void Rotatorias_ContornoDaPistaSemRetasForaDoEixo(TipoRotatoria type)
    {
        var w = new Y34TipTests.World();
        foreach (var (a, b) in new[] { (new Vec2(-100, 0), new Vec2(100, 0)), (new Vec2(0, -100), new Vec2(0, 100)) })
        {
            var pr = PathReference.FromPoints(new[] { a, b }, 0);
            var g = RoadTemplates.All[0].Create().Build(pr, new OutputSettings(), Cat);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), new Polyline2(pr.Points)));
        }
        var d = new RoundaboutDefinition { Center = Vec2.Zero };
        d.ApplyPreset(type);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, w.Roads, d.OuterRadius + 25);
        var geo = MarkingBuilder.Build(d, null, new BuildContext { Catalog = Cat });
        var bad = new List<string>();
        foreach (var pe in ElementPlan.Plan(d, geo).Where(p => p.IsFloor))
            foreach (var sh in pe.Shapes)
                foreach (var ring in BoundaryFit.FitPolygon(ElementPlan.Prepare(sh), BoundaryFit.DefaultTolerance, 0.0018, geo.ArcCenters))
                    foreach (var s in AxisSnap.Snap(ring, BoundaryFit.DefaultTolerance))
                        if (AxisSnap.OffAxisDeg(s) is > 1e-9 and <= AxisSnap.MaxAngleDeg)
                            bad.Add($"{s.Start} → {s.End} ({AxisSnap.OffAxisDeg(s):0.0000}°)");
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    public static IEnumerable<object[]> RoundaboutTypes() => Enum.GetValues<TipoRotatoria>().Select(t => new object[] { t });

    /// <summary>
    /// Faixa de 15 cm do bordo do ramo da rotatória 1 faixa (pontos do Autoteste): reta de 17 m em y = 5,5 e o começo da
    /// curva de entrada a 2 cm. Os dois pontos da volta do anel saíam juntos e a reta ia até a curva (ponta a 19 mm).
    /// </summary>
    [Fact]
    public void AjusteDoContorno_FimDaRetaNaVoltaDoAnel_ContinuaExato()
    {
        var ring = new List<Vec2>
        {
            new(-26.7871, 5.5002), new(-26.0687, 5.514), new(-25.9863, 5.519), new(-25.9983, 5.6684), new(-26.0734, 5.6639),
            new(-26.79, 5.6502), new(-26.8079, 5.65), new(-43.6242, 5.65), new(-43.6424, 5.5), new(-26.8063, 5.5),
        };
        var fit = BoundaryFit.Fit(ring, BoundaryFit.DefaultTolerance, 0.0018);
        Assert.True(BoundaryFit.VertexDeviation(ring, fit) <= BoundaryFit.DefaultTolerance + 1e-6);
        Assert.Equal(0, AxisSnap.CountNearAxis(AxisSnap.Snap(fit, BoundaryFit.DefaultTolerance)));
    }

    /// <summary>Vértices a 9 mm (menos que a aresta mínima): fica a quina da reta longa, não o ponto da curva ao lado.</summary>
    [Fact]
    public void Simplificacao_VerticesColados_FicaAQuinaDaRetaLonga()
    {
        var p = new Polygon2(new List<Vec2> { new(28.0202, 0), new(28.0202, 5.1828), new(27.9614, 5.4912), new(27.9594, 5.5), new(9.5076, 5.5), new(9.5076, 0) });
        var s = p.Simplified(0.01, 1e-4)!;
        Assert.Contains(new Vec2(27.9594, 5.5), s.Outer);
        var segs = s.Outer.Select((v, i) => new BoundarySegment(v, s.Outer[(i + 1) % s.Outer.Count])).ToList();
        Assert.Equal(0, AxisSnap.CountNearAxis(segs));
    }

    /// <summary>
    /// "Via reconhecida em pisos comuns" do Autoteste (T de dois pisos retangulares): a reta do eixo de 200 m saía a 0,014°
    /// da horizontal (18 avisos nos pisos da via e um na própria linha do eixo) – as pontas livres do eixo cordal escorregam
    /// para a quina e o prolongamento seguia daí.
    /// </summary>
    [Fact]
    public void ViaReconhecida_PisosRetangulares_EixoExato()
    {
        static Polygon2 R(double x0, double y0, double x1, double y1) => new(new List<Vec2> { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) });
        const double X = 1000, Y = 2000;
        var net = FloorRoads.Read(new[] { R(X + 1200, Y - 3.5, X + 1400, Y + 3.5), R(X + 1297, Y + 3, X + 1303, Y + 90) });
        Assert.Equal(2, net.Roads.Count);
        foreach (var r in net.Roads)
        {
            var segs = r.Axis.Points.Zip(r.Axis.Points.Skip(1), (a, b) => new BoundarySegment(a, b)).ToList();
            Assert.Equal(0, AxisSnap.CountNearAxis(segs));
        }
        var main = net.Roads.OrderByDescending(r => r.Axis.Length).First();
        Assert.All(main.Axis.Points, q => Assert.Equal(Y, q.Y, 4));
        Assert.True(main.Axis.Length > 199, $"eixo de {main.Axis.Length:0.00} m");
    }

    [Fact]
    public void AxisSnap_LinhaAberta_PontasExatas()
    {
        var path = new List<Vec2> { new(0, 0), new(100, 0.0003), new(100.0002, 50) };
        var s = AxisSnap.SnapPath(path);
        Assert.Equal(s[0].Y, s[1].Y);
        Assert.Equal(s[1].X, s[2].X);
        Assert.True(s[0].DistanceTo(path[0]) <= AxisSnap.MaxOffset && s[2].DistanceTo(path[2]) <= AxisSnap.MaxOffset);
        // Inclinação de projeto (1°) não é mexida.
        var slope = new List<Vec2> { new(0, 0), new(100, 1.75) };
        Assert.True(slope.SequenceEqual(AxisSnap.SnapPath(slope)));
    }

    // ------------------------------------------------------------------ tipos de piso do plugin

    [Theory]
    [InlineData("SV - Grama 8 cm", 0.08)]
    [InlineData("SV - Concreto 0,5 cm", 0.005)]
    [InlineData("SV - Asfalto 5 cm", 0.05)]
    [InlineData("SV - Pintura branca 0.3 cm", 0.003)]
    public void TipoDePiso_EspessuraDoNome(string name, double expected) =>
        Assert.Equal(expected, FloorTypeNames.Thickness(name)!.Value, 9);

    [Fact]
    public void TipoDePiso_NomeDeOutroTipo_NaoConta()
    {
        Assert.Null(FloorTypeNames.Thickness("Piso genérico 300 mm"));
        Assert.Null(FloorTypeNames.Thickness(null));
        Assert.False(FloorTypeNames.Mismatch("Piso genérico 300 mm", 0.3));
    }

    /// <summary>Tipo criado por versão antiga com a estrutura do tipo base (30 cm) e o nome "8 cm": acusado; o certo, não.</summary>
    [Fact]
    public void TipoDePiso_EstruturaDiferenteDoNome_EAcusada()
    {
        Assert.True(FloorTypeNames.Mismatch("SV - Grama 8 cm", 0.30));
        Assert.True(FloorTypeNames.Mismatch("SV - Grama 8 cm", 0.0898));
        Assert.False(FloorTypeNames.Mismatch("SV - Grama 8 cm", 0.0802));
    }
}
