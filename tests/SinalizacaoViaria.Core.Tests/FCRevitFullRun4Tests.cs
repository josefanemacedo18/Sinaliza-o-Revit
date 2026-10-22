using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// FC – 4º Autoteste completo no Revit 2027 (2026-10-21 09:35, docs/evidencias/2026-10-21-autoteste-completo-4.txt e o arquivo
/// de dados): 180 etapas em 36 min (eram 1 h 41 min), 1 erro (rotatória oval), 323 pares de pisos sobrepostos no modelo final
/// e 1 reta fora do eixo. Reproduzido aqui com as definições e os contornos gravados no arquivo de dados.
/// </summary>
public class FCRevitFullRun4Tests(ITestOutputHelper output)
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    /// <summary>Rotatória sobre duas vias locais, como o Autoteste cria (perfil de borda das vias, sinalização dos ramos).</summary>
    internal static (List<MarkingPiece> Pieces, List<string> Codes, RoundaboutDefinition D) RoundaboutScene(TipoRotatoria type)
    {
        var k = (int)type;
        var c = new Vec2(1600 + 440 + 230 * (k % 3), -200 + 650 + 230 * (k / 3));
        var w = new Y34TipTests.World();
        var ctx0 = new BuildContext { Catalog = Cat };
        var profiles = new List<List<EdgeBand>>();
        foreach (var (a, b) in new[] { (c + new Vec2(-100, 0), c + new Vec2(100, 0)), (c + new Vec2(0, -100), c + new Vec2(0, 100)) })
        {
            var pr = PathReference.FromPoints(new[] { a, b }, 0);
            var g = RoadTemplates.All[0].Create().Build(pr, new OutputSettings(), Cat);
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            var pav = g.OfType<RoadPavementDefinition>().First();
            w.Roads.Add(new IntersectionRoad(pav, axis));
            profiles.Add(EdgeProfile.FromRoad(pav, g.Where(m => m.Id != pav.Id).ToList(), ctx0));
        }
        var d = new RoundaboutDefinition { Center = c };
        d.ApplyPreset(type);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, w.Roads, d.OuterRadius + 25);
        d.EdgeProfile = EdgeProfile.Best(profiles);
        if (d.EdgeProfile.Count > 0) d.SidewalkWidth = Math.Max(0, EdgeProfile.OuterWidth(d.EdgeProfile) - d.CurbWidth);
        var L = RoundaboutGenerator.Layout(d);
        foreach (var g in w.Groups)
            foreach (var m in g)
                foreach (var cut in RoundaboutGenerator.RoadCuts(d, L, m))
                    m.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = cut.Outer.ToList() });
        w.Defs.Add(d);
        foreach (var ch in RoundaboutGenerator.Children(d, L, new OutputSettings(), 0)) { ch.GroupId = d.Id; w.Defs.Add(ch); }
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
        return (pieces, codes, d);
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Rotatorias_PinturaForaDaSarjetaEPisosSemSobreposicao(TipoRotatoria type)
    {
        // 4º Autoteste: FTP-1 × ROTATORIA 108 pares e LBO × ROTATORIA 44 (pintura sobre a sarjeta de 0,30 m por dentro da pista
        // que o perfil das vias leva para a rotatória), PAV-ASF × ROTATORIA (recorte da via 0,1–0,5 mm aquém da zona).
        var (pieces, codes, _) = RoundaboutScene(type);
        var ov = PieceOverlaps.Find(pieces);
        foreach (var o in ov) output.WriteLine($"{codes[o.A]}/{pieces[o.A].Color} × {codes[o.B]}/{pieces[o.B].Color}: {o.Area * 1e4:0.0} cm²");
        Assert.DoesNotContain(ov, o => o.Area >= 0.001);
        Assert.True(ov.Sum(o => o.Area) < 0.002, $"{ov.Count} pares, {ov.Sum(o => o.Area) * 1e4:0.0} cm²");
        Assert.DoesNotContain(ov, o => codes[o.A] == "PAV-ASF" || codes[o.B] == "PAV-ASF");
    }

    public static IEnumerable<object[]> Types() => Enum.GetValues<TipoRotatoria>().Select(t => new object[] { t });

    private static Polygon2 Rect(double x0, double y0, double x1, double y1) => Polygon2.Rectangle(new Vec2(x0, y0), new Vec2(x1, y1));

    /// <summary>
    /// "Via reconhecida em pisos comuns" (T de dois pisos, pista já modelada): com pavimento "Nenhum" a via não registrava a
    /// definição do pavimento, então não se ligava à outra – sem interseção, as calçadas, meios-fios e linhas das duas vias se
    /// cruzavam (61 pares no Revit). Agora o pavimento fica como dado (sem piso) e o T é formado.
    /// </summary>
    internal static (List<MarkingPiece> Pieces, List<string> Codes, List<IntersectionRoad> Roads, IntersectionLayout L, List<IReadOnlyList<Vec2>> Centers)
        RecognizedTee(TipoPavimento material = TipoPavimento.Nenhum)
    {
        // Os mesmos pisos do Autoteste (o ramo encosta no piso da via principal).
        var fr = FloorRoads.Read(new[] { Rect(1200, -3.5, 1400, 3.5), Rect(1297, 3.5, 1303, 90) });
        Assert.Equal(2, fr.Roads.Count);
        var roads = new List<IntersectionRoad>();
        var groups = new List<List<MarkingDefinition>>();
        var paths = new Dictionary<string, Polyline2>();
        var ctx0 = new BuildContext { Catalog = Cat };
        foreach (var road in fr.Roads)
        {
            var setup = FloorRoads.Fit(RoadTemplates.All[0].Create(), road);
            setup.Pavement = material;
            var pr = PathReference.FromPoints(road.Axis.Points, 0);
            var g = setup.Build(pr, new OutputSettings(), Cat);
            var axis = new Polyline2(pr.Points);
            var pav = Assert.Single(g.OfType<RoadPavementDefinition>());
            Assert.Equal(material, pav.Material);
            Assert.NotNull(RoadTemplates.FromJson(pav.SetupJson));
            if (material == TipoPavimento.Nenhum) Assert.Empty(MarkingBuilder.Build(pav, axis, ctx0).Pieces);
            foreach (var m in g) if (m.Path != null) paths[m.Id] = axis;
            groups.Add(g);
            roads.Add(new IntersectionRoad(pav, axis));
        }
        var node = Assert.Single(IntersectionGenerator.FindNodes(roads));
        Assert.True(node.Node.DistanceTo(new Vec2(1300, 0)) < 1, $"nó em {node.Node}");
        var d = new IntersectionDefinition { Node = node.Node };
        var scene = IntersectionDemo.Create(d, node.Roads.Select(i => roads[i]).ToList(), node.Roads.Select(i => groups[i]).ToList(), paths, null, Cat);
        var all = scene.Definitions;
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => all.FirstOrDefault(x => x.Id == id), PathOf = x => paths.GetValueOrDefault(x.Id), AllDefinitions = () => all };
        var pieces = new List<MarkingPiece>();
        var codes = new List<string>();
        var centers = new List<IReadOnlyList<Vec2>>();
        foreach (var def in all)
        {
            var path = paths.GetValueOrDefault(def.Id) ?? (def.Path is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points, pr.Closed) : null);
            var geo = MarkingBuilder.Build(def, path, ctx);
            foreach (var p in ElementPlan.RevitPieces(def, geo))
            {
                pieces.Add(p);
                codes.Add(def.DisplayCode);
                centers.Add(geo.ArcCenters);
            }
        }
        // Os pisos comuns do usuário (tipo de 23 cm com o topo no nível), que são a pista.
        foreach (var f in new[] { Rect(1200, -3.5, 1400, 3.5), Rect(1297, 3.5, 1303, 90) })
        {
            pieces.Add(new MarkingPiece(f, MarkingColor.Asfalto) { Thickness = 0.23, Elevation = -0.23 });
            codes.Add("(piso comum)");
            centers.Add(Array.Empty<Vec2>());
        }
        return (pieces, codes, roads, scene.Layout, centers);
    }

    [Fact]
    public void ViaReconhecidaEmPisos_PavimentoNenhum_FormaOTeSemPisosSobrepostos()
    {
        var (pieces, codes, _, L, centers) = RecognizedTee();
        Assert.Equal(3, L.Legs.Count);
        // Pista já modelada: a interseção não cria pavimento por cima dos pisos do usuário.
        Assert.DoesNotContain(pieces.Where((p, i) => codes[i] != "(piso comum)"), p => p.Color == MarkingColor.Asfalto && p.Elevation < 0);
        var ov = PieceOverlaps.Find(pieces);
        foreach (var o in ov) output.WriteLine($"{codes[o.A]}/{pieces[o.A].Color}/{pieces[o.A].Layer} {pieces[o.A].Shape.Area:0.###} × {codes[o.B]}/{pieces[o.B].Color}/{pieces[o.B].Layer} {pieces[o.B].Shape.Area:0.###}: {o.Area:0.#####} m² × {o.Height:0.###} m em {PolygonOps.Intersect(new[] { pieces[o.A].Shape }, new[] { pieces[o.B].Shape }).OrderByDescending(x => x.Area).First().Centroid}");
        // Entre marcas diferentes (vias, interseção, pisos do usuário): nada. Dentro da interseção, lascas de até 1 mm entre
        // faixas vizinhas da mesma curva (cortes sobre as cordas): o contorno que vai ao Revit (retas e arcos em volta do centro
        // da esquina) as leva para a mesma curva.
        Assert.DoesNotContain(ov, o => codes[o.A] != codes[o.B]);
        var fit = pieces.Select((p, i) => PieceOverlaps.IsFloorPiece(p) ? Fitted(new[] { p }, centers[i])[0] : p).ToList();
        var fov = PieceOverlaps.Find(fit);
        foreach (var o in fov) output.WriteLine($"ajustado: {fit[o.A].Layer} × {fit[o.B].Layer}: {o.Area * 1e4:0.00} cm²");
        Assert.True(fov.Sum(o => o.Area) < 1e-4, $"{fov.Count} pares, {fov.Sum(o => o.Area) * 1e4:0.0} cm² depois do ajuste");
    }

    [Fact]
    public void CruzamentoRodoferroviario_TransversaisSoNasFaixasDeRolamento()
    {
        // 4º Autoteste: LRE/MCF/CSA da passagem em nível sobre as vagas (MER-0), a sarjeta e as linhas centrais (LFO-2/LFO-3)
        // da via – ~30 pares. As transversais iam do eixo ao bordo da pista inteira.
        var axis = new Polyline2(new[] { new Vec2(350, 1600), new Vec2(650, 1600) });
        var pr = PathReference.FromPoints(axis.Points, 0);
        var road = RoadTemplates.All[0].Create().Build(pr, new OutputSettings(), Cat);
        var pav = road.OfType<RoadPavementDefinition>().Single();
        var rc = new RailCrossingSetup { Station = axis.Length / 2 };
        rc.FitTo(pav);
        Assert.NotNull(rc.RightLanesOuter);
        var crossing = rc.Build(pr, axis, 0, new OutputSettings());
        var all = road.Concat(crossing).ToList();
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => all.FirstOrDefault(x => x.Id == id), PathOf = _ => axis, AllDefinitions = () => all };
        var pieces = new List<MarkingPiece>();
        var codes = new List<string>();
        var mine = new List<bool>();
        foreach (var def in all.Where(d => d is not SignDefinition))
        {
            var path = def.Path is { Points.Count: >= 2 } p ? new Polyline2(p.Points, p.Closed) : axis;
            foreach (var pc in ElementPlan.RevitPieces(def, MarkingBuilder.Build(def, path, ctx)))
            {
                pieces.Add(pc);
                codes.Add(def.DisplayCode);
                mine.Add(crossing.Contains(def) && def is not LinearMarkingDefinition { Code: "LFO-3" });
            }
        }
        var ov = PieceOverlaps.Find(pieces).Where(o => mine[o.A] != mine[o.B]).ToList();
        Assert.True(ov.Count == 0, string.Join("; ", ov.Take(10).Select(o => $"{codes[o.A]}/{pieces[o.A].Layer} × {codes[o.B]}/{pieces[o.B].Layer}: {o.Area * 1e4:0.0} cm² em {pieces[o.A].Shape.Centroid}")));
        // As transversais continuam cobrindo as faixas de rolamento inteiras (do lado de fora da linha central ao bordo da faixa).
        var mcf = crossing.OfType<LinearMarkingDefinition>().Where(d => d.Code == "MCF").ToList();
        Assert.Equal(2, mcf.Count);
        foreach (var m in mcf)
        {
            var len = m.PathRef.Points[0].DistanceTo(m.PathRef.Points[1]);
            Assert.InRange(len, (rc.RightLanesOuter ?? 0) - RailCrossingSetup.CenterGap - 0.01, rc.RightLanesOuter ?? 0);
        }
    }

    /// <summary>Largura média (m) da parte em comum de duas peças: 2 × área / perímetro.</summary>
    internal static double SliverWidth(Polygon2 a, Polygon2 b) =>
        PolygonOps.Intersect(new[] { a }, new[] { b }).Sum(x => 2 * x.Area / Math.Max(1e-9, x.Outer.Select((v, k) => v.DistanceTo(x.Outer[(k + 1) % x.Outer.Count])).Sum()));

    private const double Tol = 0.00256 * 0.3048 * 1.5;   // pontos colados no Revit (1,5 × ShortCurveTolerance)

    /// <summary>As rotatórias do 4º Autoteste completo, como foram gravadas (coordenadas internas do projeto).</summary>
    internal static List<RoundaboutDefinition> Fixture() =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fc-autoteste4-rotatorias.json")))
            .RootElement.EnumerateArray().Select(e => (RoundaboutDefinition)MarkingDefinition.FromJson(e.GetRawText())!).ToList();

    /// <summary>O anel como o plugin manda ao Revit: pontos colados e espinhos fora, ajuste conferido (MarkingService.ToCurveLoop).</summary>
    internal static List<BoundarySegment> RevitRing(IReadOnlyList<Vec2> ring, IReadOnlyList<Vec2> centers)
    {
        var pts = SketchPoints(ring);
        if (BoundaryFit.FitRing(pts, BoundaryFit.DefaultTolerance, Tol, centers, BoundaryFit.DefaultTolerance) is { } segs) return segs;
        return Lines(pts);
    }

    /// <summary>Pontos do esboço (MarkingService.SketchPoints): sem pontos colados nem espinhos.</summary>
    private static List<Vec2> SketchPoints(IReadOnlyList<Vec2> ring)
    {
        var pts = new List<Vec2>();
        foreach (var v in ring) if (pts.Count == 0 || pts[^1].DistanceTo(v) > Tol) pts.Add(v);
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= Tol) pts.RemoveAt(pts.Count - 1);
        return ProfileCleanup.Prepare(pts, Tol);
    }

    /// <summary>Anel sem ajuste (retas do esboço, alinhadas aos eixos) – o que o plugin faz quando o ajuste não passa.</summary>
    private static List<BoundarySegment> Lines(List<Vec2> pts)
    {
        var lines = AxisSnap.SnapRing(pts, BoundaryFit.DefaultTolerance);
        return lines.Select((p, i) => new BoundarySegment(p, lines[(i + 1) % lines.Count])).ToList();
    }

    /// <summary>Interseções do 4º Autoteste completo com pisos sobrepostos no Revit (e as vias delas), como foram gravadas.</summary>
    internal static List<MarkingDefinition> IntersectionFixture() =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fc-autoteste4-intersecoes.json")))
            .RootElement.EnumerateArray().Select(e => MarkingDefinition.FromJson(e.GetRawText())!).ToList();

    /// <summary>
    /// Peças de piso de UMA marca como o Revit as recebe: o contorno ajustado (retas e arcos) amostrado de volta em pontos. Os
    /// pisos físicos são ajustados juntos (MarkingService.PrepareSharedFit, ajuste topológico); a pintura, um a um.
    /// </summary>
    internal static List<MarkingPiece> Fitted(IEnumerable<MarkingPiece> pieces, IReadOnlyList<Vec2> centers)
    {
        var list = pieces.Where(PieceOverlaps.IsFloorPiece).ToList();
        var preps = list.Select(p => ElementPlan.Prepare(p.Shape)).Select(p => new[] { p.Outer }.Concat(p.Holes).Select(SketchPoints).ToList()).ToList();
        var shared = new Dictionary<(int, int), List<BoundarySegment>>();
        var keys = new List<(int, int)>();
        var rings = new List<IReadOnlyList<Vec2>>();
        for (int i = 0; i < list.Count; i++)
            if (!MarkingColors.IsPaint(list[i].Color))
                for (int k = 0; k < preps[i].Count; k++) { keys.Add((i, k)); rings.Add(preps[i][k]); }
        var fits = BoundaryFit.FitRings(rings, BoundaryFit.DefaultTolerance, Tol, centers, BoundaryFit.DefaultTolerance);
        for (int j = 0; j < keys.Count; j++) if (fits[j] is { } f) shared[keys[j]] = f;
        var res = new List<MarkingPiece>();
        for (int i = 0; i < list.Count; i++)
        {
            var rr = preps[i].Select((r, k) => BoundaryFit.Sample(shared.TryGetValue((i, k), out var f) ? f
                : BoundaryFit.FitRing(r, BoundaryFit.DefaultTolerance, Tol, centers, BoundaryFit.DefaultTolerance) ?? Lines(r), 0.0002)).ToList();
            res.Add(list[i] with { Shape = new Polygon2(rr[0], rr.Skip(1)) });
        }
        return res;
    }

    internal static (MarkingGeometry Geo, IntersectionDefinition D) BuildIntersection(List<MarkingDefinition> fx, IntersectionDefinition d)
    {
        var ctx = new BuildContext
        {
            Catalog = Cat, Lookup = id => fx.FirstOrDefault(x => x.Id == id), AllDefinitions = () => fx,
            // Eixo pelo traçado guardado na marca (as linhas do Revit não existem aqui).
            PathOf = x => x.Path is { } pr ? (pr.Points.Count >= 2 ? new Polyline2(pr.Points, pr.Closed)
                : pr.Cache is { Count: > 0 } c && c[0].Count >= 2 ? new Polyline2(c.SelectMany((q, k) => k == 0 ? q : q.Skip(1)).ToList()) : null) : null,
        };
        return (MarkingBuilder.Build(d, null, ctx), d);
    }

    /// <summary>Volume sobreposto (cm³) entre os pisos da interseção antes e depois do ajuste de arcos do Revit.</summary>
    private static (double Raw, double Fitted, int Centers) IntersectionOverlap(List<MarkingDefinition> fx, IntersectionDefinition d)
    {
        var (geo, _) = BuildIntersection(fx, d);
        var raw = ElementPlan.RevitPieces(d, geo).Where(PieceOverlaps.IsFloorPiece).ToList();
        var fit = Fitted(raw, geo.ArcCenters);
        return (PieceOverlaps.Find(raw).Sum(o => o.Volume) * 1e6, PieceOverlaps.Find(fit).Sum(o => o.Volume) * 1e6, geo.ArcCenters.Count);
    }

    [Fact]
    public void Intersecoes_DoAutoteste4_PisosDaMesmaCurvaNoMesmoArco()
    {
        // 13 pares INTERSECAO × INTERSECAO no Revit (meio-fio × calçada de concreto 15 cm, 0,6–4,6 L): o canto da pista saía
        // com flecha de 0,2 % do raio (12 mm num raio de 6 m), as faixas recortadas sobre essas cordas não cabiam nos 5 mm do
        // ajuste de arcos e cada piso caía num círculo próprio (centro a até 9 cm do da esquina) – ~2 mm sobrepostos ao longo
        // da curva inteira. Antes do FC esta emulação dava 2,7–11,5 L por interseção; agora 0–31 cm³ nos cruzamentos e T e
        // 2,8 L na emenda em curva.
        var fx = IntersectionFixture();
        foreach (var d in fx.OfType<IntersectionDefinition>())
        {
            var (raw, fitted, centers) = IntersectionOverlap(fx, d);
            output.WriteLine($"{d.Id[..6]} R={d.CornerRadius}: {raw:0} cm³ antes do ajuste, {fitted:0} cm³ ajustados, {centers} centros");
            // Antes do ajuste: só lascas de décimos de mm entre faixas vizinhas.
            Assert.True(raw < 400, $"{d.Id[..6]}: {raw:0.0} cm³ entre as peças da interseção");
            Assert.True(centers > 0, $"{d.Id[..6]}: sem centros de arco");
            // Com o ajuste topológico (Fase IV, K13) as bordas comuns saem iguais nos pisos vizinhos: nem as junções com as
            // rampas (até 31 cm³) nem a emenda em curva entre duas vias pela ponta (fd4594, 2,8 L) sobram.
            Assert.True(fitted < 5, $"{d.Id[..6]}: {fitted:0} cm³ depois do ajuste");
        }
    }

    [Fact]
    public void AxisSnap_ArcoVizinhoDeRetaAlinhada_MantemORaio()
    {
        // Emenda: a reta vertical ao lado do arco de 22,2 m andava 3 mm para ficar exata e o arco, refeito pelas pontas novas e
        // pelo meio antigo, ia para r = 22,055 m com o centro 15 cm deslocado.
        var c = new Vec2(0, 0);
        var a = new Vec2(-22.2, -0.037);
        var b = Vec2.FromAngle(Math.PI + 0.47) * 22.2;
        var mid = Vec2.FromAngle(Math.Atan2(a.Y, a.X) + (Math.Atan2(b.Y, b.X) - Math.Atan2(a.Y, a.X)) / 2) * 22.2;
        var ring = new List<BoundarySegment>
        {
            new(new Vec2(-22.2003, 1.92), a),
            new(a, b, mid),
            new(b, new Vec2(-30, -15)),
            new(new Vec2(-30, -15), new Vec2(-30, 1.92)),
            new(new Vec2(-30, 1.92), new Vec2(-22.2003, 1.92)),
        };
        var s = AxisSnap.Snap(ring, 0.005);
        Assert.Equal(s[0].Start.X, s[0].End.X);
        Assert.Equal(s[0].End, s[1].Start);
        // A ponta anda 0,15 mm: o arco continua o mesmo a menos de 1 mm (antes o centro ia 15 cm para o lado).
        Assert.True(Math.Abs(s[1].Circle.Radius - 22.2) < 0.001, $"raio {s[1].Circle.Radius}");
        Assert.True(s[1].Circle.Center.DistanceTo(c) < 0.001, $"centro em {s[1].Circle.Center}");
    }

    [Fact]
    public void RotatoriasDoAutoteste4_FormasDiretasQueORevitAceita()
    {
        // Oval: CreateExtrusionGeometry recusou um contorno – peças de asfalto de área nula sob a sarjeta interna.
        var bad = new List<string>();
        foreach (var d in Fixture())
        {
            var geo = MarkingBuilder.Build(d, null, new BuildContext { Catalog = Cat });
            foreach (var pe in ElementPlan.Plan(d, geo).Where(p => !p.IsFloor))
                foreach (var sh in pe.Shapes)
                {
                    var prep = ElementPlan.Prepare(sh);
                    var rings = new[] { prep.Outer }.Concat(prep.Holes).Select(r => (IReadOnlyList<BoundarySegment>)RevitRing(r, geo.ArcCenters)).ToList();
                    var simple = BoundaryFit.IsSimple(rings, 1e-5);
                    var shortEdge = rings.SelectMany(r => r).Any(x => x.Start.DistanceTo(x.End) <= Tol);
                    if (!simple || shortEdge || rings.Any(r => r.Count < 2))
                        bad.Add($"{d.Type} {pe.Color}: peça {sh.Area:0.###} m² em {sh.Centroid.X:0.00};{sh.Centroid.Y:0.00} – simples {simple}, aresta curta {shortEdge}, anéis {string.Join(",", rings.Select(r => r.Count))}");
                }
        }
        Assert.True(bad.Count == 0, string.Join(" | ", bad));
    }
}
