using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// EY – Autoteste completo no Revit 2027 de 2026-10-20 (docs/evidencias/2026-10-20-autoteste-completo.txt): 178 etapas,
/// 1 com erro. "Placas – catálogo completo (168)": o Revit recusou o perfil de peças das placas ("Curve loops do not
/// satisfy the input requirements" e "Perfil inválido").
/// </summary>
public class EYRevitFullRunTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static IEnumerable<(string Code, int Index, ProfileSolid Profile)> SignProfiles(Func<IEnumerable<MarkingPiece>, IEnumerable<MarkingPiece>>? filter = null)
    {
        var ctx = new BuildContext { Catalog = Cat };
        foreach (var p in Cat.Placas)
        {
            var g = MarkingBuilder.Build(new SignDefinition { Code = p.Codigo, Position = Vec2.Zero, Direction = Vec2.UnitY }, null, ctx);
            var k = 0;
            foreach (var piece in filter?.Invoke(g.Pieces) ?? g.Pieces)
            {
                k++;
                if (piece.Profile is { } ps) yield return (p.Codigo, k, ps);
            }
        }
    }

    /// <summary>
    /// Reprodução: o pictograma do R-13 (e do A-31) passa duas vezes pelo mesmo vértice – um "buraco de fechadura" preso ao
    /// contorno por um ponto. O preparo do contorno no Revit não resolve isso e a extrusão é recusada.
    /// </summary>
    [Fact]
    public void Pictograma_QueSeToca_EApontadoECorrigido()
    {
        // Contorno do pictograma do R-13 (peça 3) como saía do gerador antes da correção.
        var raw = new Polygon2(new Vec2[]
        {
            new(0.1143, 2.3897), new(0.0008, 2.3951), new(-0.0695, 2.3951), new(-0.0695, 2.4600), new(0.0008, 2.4600),
            new(0.0008, 2.3951), new(0.0116, 2.3951), new(0.0116, 2.4654), new(0.0068, 2.4707), new(0.0062, 2.4708),
            new(-0.0749, 2.4708), new(-0.0803, 2.4661), new(-0.0803, 2.4654), new(-0.0803, 2.3951), new(-0.0803, 2.3829),
            new(-0.0714, 2.3858), new(-0.0545, 2.3869), new(-0.0378, 2.3837), new(-0.0225, 2.3765), new(-0.0095, 2.3657),
            new(0.0005, 2.3520), new(0.0016, 2.3492), new(0.1143, 2.3492),
        });
        Assert.NotNull(ProfileCleanup.Problem(raw));
        var parts = ProfileCleanup.Parts(raw);
        Assert.NotEmpty(parts);
        Assert.All(parts, p => Assert.Null(ProfileCleanup.Problem(p)));
        Assert.InRange(parts.Sum(p => p.Area), raw.Area - 0.5e-4, raw.Area + 0.5e-4);   // ± 0,5 cm²
    }

    [Fact]
    public void Placas_CatalogoCompleto_NenhumPerfilQueORevitRecusa()
    {
        var all = SignProfiles().ToList();
        Assert.True(all.Count > 1000);
        var bad = all.Where(x => ProfileCleanup.Problem(x.Profile.Profile) != null)
            .Select(x => $"{x.Code} peça {x.Index}: {ProfileCleanup.Problem(x.Profile.Profile)}").ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Parts_PerfilValido_VoltaIgual()
    {
        var box = Polygon2.Rectangle(new Vec2(0, 0), new Vec2(0.6, 0.6));
        var parts = ProfileCleanup.Parts(box);
        Assert.Single(parts);
        Assert.Same(box, parts[0]);
    }

    [Fact]
    public void Parts_PecaMenorQueATolerancia_Some()
    {
        var dot = Polygon2.Rectangle(new Vec2(0, 0), new Vec2(0.0008, 0.0008));
        Assert.Equal("contorno menor que a tolerância do Revit", ProfileCleanup.Problem(dot));
        Assert.Empty(ProfileCleanup.Parts(dot));
    }

    [Fact]
    public void Parts_FuroEncostadoNoContorno_SaiEmPartesValidas()
    {
        // Quadrado de 10 cm com furo de 4 cm encostado na borda de baixo.
        var p = new Polygon2(
            new[] { new Vec2(0, 0), new Vec2(0.1, 0), new Vec2(0.1, 0.1), new Vec2(0, 0.1) },
            new[] { new[] { new Vec2(0.03, 0), new Vec2(0.03, 0.04), new Vec2(0.07, 0.04), new Vec2(0.07, 0) } });
        Assert.Equal("furo encosta no contorno", ProfileCleanup.Problem(p));
        var parts = ProfileCleanup.Parts(p);
        Assert.NotEmpty(parts);
        Assert.All(parts, x => Assert.Null(ProfileCleanup.Problem(x)));
        Assert.InRange(parts.Sum(x => x.Area), 0.01 - 0.0016 - 1e-5, 0.01 - 0.0016 + 1e-5);
    }

    // ------------------------------------------------------------------ retas fora do eixo (1 385 avisos)

    /// <summary>Cenas do Autoteste no núcleo: cruzamento, T, esconso, rotatórias e cul-de-sac.</summary>
    private static IEnumerable<(string Name, MarkingDefinition Def, Polyline2? Path, BuildContext Ctx)> Scenes()
    {
        foreach (var (nm, ang, tee) in new[] { ("cruz", 90.0, false), ("T", 90.0, true), ("esconso", 52.0, false) })
        {
            var scene = IntersectionDemo.Create(new IntersectionDefinition(), Cat, 0, 0, ang, tee);
            var ctx = new BuildContext { Catalog = Cat, Lookup = id => scene.Definitions.FirstOrDefault(x => x.Id == id), PathOf = x => scene.Paths.GetValueOrDefault(x.Id) };
            foreach (var def in scene.Definitions)
                yield return (nm, def, scene.Paths.GetValueOrDefault(def.Id) ?? (def.Path is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points, pr.Closed) : null), ctx);
        }
        var ctx0 = new BuildContext { Catalog = Cat };
        foreach (var t in new[] { TipoRotatoria.Compacta, TipoRotatoria.UmaFaixa, TipoRotatoria.DuasFaixas })
        {
            var rb = new RoundaboutDefinition();
            rb.ApplyPreset(t);
            foreach (var a in new[] { 0.0, 90, 180, 270 }) rb.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = 2.5 });
            yield return ("rotatória " + t, rb, null, ctx0);
        }
        yield return ("cul-de-sac", new CulDeSacDefinition { ServiceStripWidth = 0.7, GutterWidth = 0.3, SidewalkWidth = 2.5 },
            new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 25) }), ctx0);
    }

    /// <summary>
    /// Reprodução: pela ajuda do Revit 2027 o aviso vale para retas a até 0,2° da horizontal, da vertical ou de 45°. O ajuste
    /// do contorno em retas e arcos desloca as pontas das retas dentro da tolerância dele (5 mm): retas de 15 m nas rotatórias
    /// ficavam a 0,005° do eixo (1 a 4 mm na ponta). Com o limite de 0,5 mm (resíduo de recorte) elas ficam; com o mesmo
    /// desvio aceito no ajuste, sobram no máximo 2 (as que, alinhadas, fariam uma peça estreita se tocar), e os contornos
    /// válidos continuam válidos.
    /// </summary>
    [Fact]
    public void RetasForaDoEixo_ComODesvioDoAjuste_NenhumaSobra()
    {
        int before = 0, after = 0, rings = 0;
        foreach (var (name, def, path, ctx) in Scenes())
        {
            MarkingGeometry g;
            try { g = MarkingBuilder.Build(def, path, ctx); }
            catch (Exception) { continue; }
            foreach (var pe in ElementPlan.Plan(def, g).Where(p => p.IsFloor))
                foreach (var sh in pe.Shapes)
                    foreach (var ring in BoundaryFit.FitPolygon(ElementPlan.Prepare(sh), BoundaryFit.DefaultTolerance, 0.0018, g.ArcCenters))
                    {
                        rings++;
                        before += AxisSnap.CountNearAxis(AxisSnap.Snap(ring));
                        var snapped = AxisSnap.Snap(ring, BoundaryFit.DefaultTolerance);
                        after += AxisSnap.CountNearAxis(snapped);
                        var pts = BoundaryFit.Sample(snapped, 0.0005);
                        var orig = BoundaryFit.Sample(ring, 0.0005);
                        // O alinhamento não estraga contorno válido (os que já saem do ajuste se tocando seguem para a
                        // conferência do MarkingService, que volta às retas).
                        if (PolygonChecks.Problem(orig, 1e-7) == null)
                            Assert.True(PolygonChecks.Problem(pts, 1e-7) == null, $"{name} {def.DisplayCode}: {PolygonChecks.Problem(pts, 1e-7)}");
                        var a0 = Math.Abs(PolygonChecks.SignedArea(orig));
                        Assert.InRange(Math.Abs(PolygonChecks.SignedArea(pts)) - a0, -0.002 * Math.Max(1, a0), 0.002 * Math.Max(1, a0));
                    }
        }
        Assert.True(rings > 100);
        Assert.True(before > 20, $"reprodução: só {before} reta(s) com o limite de 0,5 mm – revise o teste");
        // Só ficam as que, alinhadas, fariam uma peça estreita se tocar (o alinhamento não estraga contorno válido).
        Assert.True(after <= 2 && after * 20 <= before, $"antes {before}, depois {after}");
    }

    [Fact]
    public void AxisSnap_DiagonalDe45QuaseAlinhada_FicaExata()
    {
        // Quadrado girado 45° com um lado a 0,05° da diagonal (resíduo de 4 mm em 4,6 m).
        var r = new List<BoundarySegment>
        {
            new(new Vec2(0, 0), new Vec2(3.2525, 3.2565)),
            new(new Vec2(3.2525, 3.2565), new Vec2(0, 6.5)),
            new(new Vec2(0, 6.5), new Vec2(-3.25, 3.25)),
            new(new Vec2(-3.25, 3.25), new Vec2(0, 0)),
        };
        Assert.Equal(2, AxisSnap.CountNearAxis(r));
        var s = AxisSnap.Snap(r, 0.005);
        Assert.Equal(0, AxisSnap.CountNearAxis(s));
        Assert.All(s.Select((x, i) => (x, i)), t => Assert.Equal(t.x.End, s[(t.i + 1) % s.Count].Start));
    }

    [Fact]
    public void AxisSnap_ForaDaJanelaDoRevit_NaoMexe()
    {
        // 0,3° (mais que os 0,2° que o Revit avisa): inclinação de projeto, mesmo com desvio pequeno numa reta curta.
        var a = 0.3 * Math.PI / 180;
        var r = new List<BoundarySegment>
        {
            new(new Vec2(0, 0), new Vec2(Math.Cos(a) * 0.5, Math.Sin(a) * 0.5)),
            new(new Vec2(Math.Cos(a) * 0.5, Math.Sin(a) * 0.5), new Vec2(0.26, 2)),
            new(new Vec2(0.26, 2), new Vec2(0, 0)),
        };
        Assert.Equal(0, AxisSnap.CountNearAxis(r));
        Assert.True(r.SequenceEqual(AxisSnap.Snap(r, 0.005)));
    }

    [Fact]
    public void AxisSnap_OffAxisDeg_DirecoesDoRevit()
    {
        double Off(double deg) => AxisSnap.OffAxisDeg(new BoundarySegment(Vec2.Zero, new Vec2(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180))))!.Value;
        Assert.Equal(0.1, Off(0.1), 9);
        Assert.Equal(0.1, Off(44.9), 9);
        Assert.Equal(0.1, Off(90.1), 9);
        Assert.Equal(0.1, Off(-135.1), 9);
        Assert.Equal(22.5, Off(22.5), 9);
        Assert.Null(AxisSnap.OffAxisDeg(new BoundarySegment(Vec2.Zero, new Vec2(1, 1), new Vec2(0.7, 0.3))));
    }

    // ------------------------------------------------------------------ pisos sobrepostos que persistem (614 pares)

    private static List<PieceOverlap> Overlaps(IReadOnlyList<MarkingDefinition> defs, Polyline2? axis, out List<string> labels)
    {
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => defs.FirstOrDefault(x => x.Id == id), PathOf = d => d.Path != null ? axis : null, AllDefinitions = () => defs };
        var pieces = new List<MarkingPiece>();
        labels = new List<string>();
        foreach (var d in defs)
        {
            var path = axis != null && d.Path != null ? axis : d.Path is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points, pr.Closed) : null;
            foreach (var p in ElementPlan.RevitPieces(d, MarkingBuilder.Build(d, path, ctx)))
            {
                pieces.Add(p);
                labels.Add($"{d.DisplayCode}:{p.Color}");
            }
        }
        return PieceOverlaps.Find(pieces);
    }

    private static string Describe(List<PieceOverlap> ov, List<string> labels) =>
        string.Join("; ", ov.GroupBy(o => labels[o.A] + " × " + labels[o.B]).Select(g => $"{g.Key} {g.Count()} ({g.Sum(o => o.Volume) * 1e6:0} cm³)"));

    /// <summary>
    /// "Via com canteiros laterais": a linha de fundo das vagas, centrada na divisa do lado da pista, ficava metade dentro
    /// do meio-fio do canteiro (MER-0 × MEIO-FIO, 0,05 m × todo o trecho – 35 475 cm³ por par no Revit). E todos os modelos de
    /// via prontos sem nenhum piso sobreposto.
    /// </summary>
    [Fact]
    public void ModelosDeVia_NenhumPisoSobreposto()
    {
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(200, 0) });
        foreach (var t in RoadTemplates.All)
        {
            var defs = t.Create().Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat);
            var ov = Overlaps(defs, axis, out var labels);
            Assert.True(ov.Count == 0, $"{t.Name}: {Describe(ov, labels)}");
        }
    }

    /// <summary>Ciclofaixa e ciclovia de dois sentidos: o símbolo da bicicleta encostava na linha de bordo (CIC-LD × SIC no Revit).</summary>
    [Fact]
    public void Ciclofaixas_SimbolosEntreAsLinhas()
    {
        foreach (var v in Enum.GetValues<TipoCiclo>())
        {
            var b = new BikeLaneSetup { Type = v, Width = BikeLaneSetup.DefaultWidth(v) };
            var defs = b.Build(PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(80, 0), new Vec2(120, 20) }, 0), new OutputSettings());
            var ov = Overlaps(defs, null, out var labels);
            Assert.True(ov.Count == 0, $"{v}: {Describe(ov, labels)}");
        }
    }

    /// <summary>
    /// Canteiros na calçada com árvore: a grama e a proteção em volta do tronco passavam sobre a borda, mais alta (CANT-GRAMA
    /// 3 328 + 512 cm³ e JARDINEIRA 8 320 + 1 280 cm³ no Revit – os mesmos volumes medidos aqui antes da correção).
    /// </summary>
    [Fact]
    public void CanteirosComArvore_PecasDaArvoreSoSobreATerra()
    {
        foreach (var v in Enum.GetValues<TipoCanteiroCalcada>())
        {
            var d = new PlanterDefinition { Type = v, PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(2, 0) }, 0) };
            var ov = Overlaps(new[] { d }, null, out var labels);
            Assert.True(ov.Count == 0, $"{v}: {Describe(ov, labels)}");
        }
    }

    /// <summary>Faixa de pedestres sobre a via com estacionamento: a exclusão nas vagas (o recorte do Revit agora inclui as vagas) tira a sobreposição.</summary>
    [Fact]
    public void FaixaDePedestres_RecortaAsVagas()
    {
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(200, 0) });
        var road = RoadTemplates.All[0].Create().Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat);
        var cw = new CrosswalkSetup { CrosswalkCode = "FTP-1" }.Build(new Vec2(100, -5.2), new Vec2(100, 5.2), 0, new OutputSettings(), 4.0, 0.4);
        var all = road.Concat(cw).ToList();
        bool Pair(PieceOverlap o, List<string> l) => l[o.A].StartsWith("MER") && l[o.B].StartsWith("FTP") || l[o.B].StartsWith("MER") && l[o.A].StartsWith("FTP");
        var before = Overlaps(all, null, out var lb).Count(o => Pair(o, lb));
        Assert.True(before > 0, "reprodução: a faixa não passou sobre as vagas – revise o teste");
        // Mesmo recorte do FootprintCutter (área da faixa + 10 cm) aplicado às vagas.
        var ctx = new BuildContext { Catalog = Cat };
        var fp = PolygonOps.Offset(PolygonOps.Offset(PolygonOps.Union(cw.Where(d => d.Overlay)
            .SelectMany(d => MarkingBuilder.Build(d, d.Path is { } pr ? new Polyline2(pr.Points) : null, ctx).Pieces.Select(p => p.Shape))), 0.6, true), -0.5, true);
        foreach (var pk in all.OfType<ParkingMarkingDefinition>())
            foreach (var f in fp) pk.Exclusions.Add(new ExclusionZone { SourceId = "faixa", Points = f.Outer.ToList() });
        Assert.Equal(0, Overlaps(all, null, out var la).Count(o => Pair(o, la)));
    }

    // ------------------------------------------------------------------ relatório do Autoteste

    [Fact]
    public void Resumo_RetasForaDoEixo()
    {
        var note = WarningSummary.OffAxisNote(new List<(string, double, double)> { ("ROTATORIA", 0.005, 15.18), ("ROTATORIA", 0.0086, 15.18), ("SARJETA", 0.1, 0.5) });
        Assert.NotNull(note);
        Assert.StartsWith("Retas fora do eixo lidas no Revit: 3 – em ROTATORIA 2, SARJETA 1;", note);
        // Pontas: 15,18 m × sen 0,005° = 1,3 mm; × sen 0,0086° = 2,3 mm; 0,5 m × sen 0,1° = 0,9 mm.
        Assert.EndsWith("a ponta andaria 1,3 mm na mediana e 2,3 mm no máximo; 0 até 0,5 mm e 3 até 5 mm.", note);
        Assert.Null(WarningSummary.OffAxisNote(new List<(string, double, double)>()));
    }

    [Fact]
    public void Resumo_ParesQueContinuamSobrepostos()
    {
        var note = WarningSummary.PersistentPairsNote(new List<(string, string, bool, double)>
        {
            ("MER-0", "MEIO-FIO", false, 0.035475), ("MEIO-FIO", "MER-0", false, 0.035475), ("INTERSECAO", "INTERSECAO", true, 0.00018),
        });
        Assert.Equal("Pares que continuam sobrepostos, por tipo: MEIO-FIO × MER-0 2 (0,071 m³); INTERSECAO × INTERSECAO [mesma marca] 1 (0,0002 m³).", note);
    }
}
