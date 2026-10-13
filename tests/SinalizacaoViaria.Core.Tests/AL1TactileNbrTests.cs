using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AL-1: piso tátil conforme a ABNT NBR 16537:2024 – uma verificação por regra, com o item, a tabela ou a figura da
/// norma no nome do teste.
/// </summary>
public class AL1TactileNbrTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static bool IsDome(Polygon2 p) => p.Outer.Count == 8;
    private static bool IsBar(Polygon2 p) => p.Outer.Count == 4;

    /// <summary>Relevos de uma placa isolada (módulo × módulo) com origem em (0, 0), eixo u = +X.</summary>
    private static List<Polygon2> Relief(double module, bool alert) =>
        TactileGenerator.Rect(Vec2.Zero, Vec2.UnitX, module, module, module, alert).SelectMany(t => t.Relief).ToList();

    private static double MinGap(List<double> xs) => xs.Zip(xs.Skip(1), (a, b) => b - a).Min();
    private static double MaxGap(List<double> xs) => xs.Zip(xs.Skip(1), (a, b) => b - a).Max();

    // ---------------------------------------------------------------- Tabela 1 (piso tátil de alerta)

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.30)]
    [InlineData(0.40)]
    public void Tabela1_DiametroDaBaseDoRelevoDeAlerta_24a28mm(double module)
    {
        var domes = Relief(module, true);
        Assert.NotEmpty(domes);
        foreach (var d in domes)
        {
            Assert.True(IsDome(d));
            var c = d.Centroid;
            var corners = 2 * d.Outer.Max(v => v.DistanceTo(c));
            var flats = 2 * d.DistanceTo(c);
            Assert.InRange(flats, 0.024 - 1e-9, 0.028);
            Assert.InRange(corners, 0.024, 0.028 + 1e-9);
        }
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.30)]
    [InlineData(0.40)]
    public void Tabela1_DistanciaEntreCentros_42a53mm_Diagonal_60a75mm(double module)
    {
        var c = Relief(module, true).Select(d => d.Centroid).ToList();
        var xs = c.Select(p => Math.Round(p.X, 6)).Distinct().OrderBy(x => x).ToList();
        var ys = c.Select(p => Math.Round(p.Y, 6)).Distinct().OrderBy(x => x).ToList();
        Assert.InRange(MinGap(xs), 0.042, 0.053);
        Assert.InRange(MaxGap(xs), 0.042, 0.053);
        Assert.InRange(MinGap(ys), 0.042, 0.053);
        var diag = Math.Sqrt(Math.Pow(xs[1] - xs[0], 2) + Math.Pow(ys[1] - ys[0], 2));
        Assert.InRange(diag, 0.060, 0.075);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.30)]
    [InlineData(0.40)]
    public void Tabela1_Nota_PrimeiraLinhaAMeiaDistanciaDaBorda(double module)
    {
        var c = Relief(module, true).Select(d => d.Centroid).ToList();
        var xs = c.Select(p => p.X).OrderBy(x => x).ToList();
        var sp = MinGap(xs.Select(x => Math.Round(x, 6)).Distinct().ToList());
        Assert.Equal(sp / 2, xs[0], 4);
        Assert.Equal(sp / 2, module - xs[^1], 4);
    }

    // ---------------------------------------------------------------- Tabela 3 (piso tátil direcional)

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.30)]
    [InlineData(0.40)]
    public void Tabela3_LarguraDaBaseDoRelevoDirecional_30a40mm(double module)
    {
        var bars = Relief(module, false);
        Assert.NotEmpty(bars);
        foreach (var b in bars)
        {
            var (mn, mx) = b.Bounds;
            Assert.True(mx.X - mn.X > mx.Y - mn.Y, "barra paralela ao sentido do deslocamento");
            Assert.InRange(mx.Y - mn.Y, 0.030 - 1e-9, 0.040);
        }
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.30)]
    [InlineData(0.40)]
    public void Tabela3_EntreCentros_70a85mm_EntreBases_45a55mm(double module)
    {
        var bars = Relief(module, false).OrderBy(b => b.Centroid.Y).ToList();
        var ys = bars.Select(b => b.Centroid.Y).ToList();
        Assert.InRange(MinGap(ys), 0.070, 0.085);
        Assert.InRange(MaxGap(ys), 0.070, 0.085);
        for (int i = 0; i + 1 < bars.Count; i++)
        {
            var gap = bars[i + 1].Bounds.Min.Y - bars[i].Bounds.Max.Y;
            Assert.InRange(gap, 0.045 - 1e-9, 0.055 + 1e-9);
        }
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.30)]
    [InlineData(0.40)]
    public void Tabela3_Nota_PrimeiraLinhaAMeiaDistanciaDaBorda(double module)
    {
        var ys = Relief(module, false).Select(b => b.Centroid.Y).OrderBy(y => y).ToList();
        var sp = MinGap(ys);
        Assert.Equal(sp / 2, ys[0], 4);
        Assert.Equal(sp / 2, module - ys[^1], 4);
    }

    // ---------------------------------------------------------------- 8.2 e altura do relevo

    [Fact]
    public void Item8_2_PisoSobreposto_DesnivelAte2mm_AlturaDoRelevo3a5mm()
    {
        var d = new TactileRouteDefinition { Module = 0.25, Rows = 1, AlertAtEnds = true };
        var geo = TactileGenerator.Route(d, new[] { new Polyline2(new[] { Vec2.Zero, new Vec2(3, 0) }) }, 0.15);
        var plates = geo.Pieces.Where(p => p.Color == MarkingColor.Amarela).ToList();
        var relief = geo.Pieces.Where(p => p.Color == MarkingColor.RelevoTatil).ToList();
        Assert.NotEmpty(plates);
        Assert.NotEmpty(relief);
        Assert.All(plates, p => Assert.InRange(p.Thickness, 0.0005, 0.002 + 1e-9));
        // Altura do relevo do piso sobreposto = altura total (placa + relevo), 3 a 5 mm (Tabelas 1 e 3).
        Assert.All(relief, r => Assert.InRange(r.Elevation + r.Thickness - 0.15, 0.003 - 1e-9, 0.005 + 1e-9));
    }

    // ---------------------------------------------------------------- 6.6 – rebaixamentos de calçada (Figuras 22 a 26)

    private static (double From, double To) AlertSpan(RampDefinition r)
    {
        var path = new Polyline2(r.PathRef.Points);
        var f = RampGenerator.FrameOf(path);
        var geo = RampGenerator.Generate(r, path);
        var domes = geo.Pieces.Where(p => p.Color == MarkingColor.RelevoTatil && IsDome(p.Shape)).Select(p => (p.Shape.Centroid - f.Curb).Dot(f.Up)).ToList();
        Assert.NotEmpty(domes);
        var sp = TactileGenerator.DomeSpacing;
        return (domes.Min() - sp / 2, domes.Max() + sp / 2);
    }

    public static IEnumerable<object[]> Ramps()
    {
        RampDefinition P(RampDefinition r, double depth = 3.0)
        {
            r.PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(0, 1) }, 0);
            if (r.Type == TipoRampa.RebaixamentoTotal) r.SidewalkDepth = depth;
            return r;
        }
        yield return new object[] { "padrão", P(new RampDefinition()) };
        yield return new object[] { "via (extensão)", P(RoadFeatures.RampTemplate(4.0, 0.15, true)) };
        var road = RoadTemplates.All[0].Create().Build(PathReference.FromPoints(new[] { new Vec2(-50, 0), new Vec2(50, 0) }, 0), new OutputSettings(), Cat)
            .OfType<RoadPavementDefinition>().First();
        yield return new object[] { "interseção", P(IntersectionGenerator.RampTemplate(new IntersectionDefinition(), road, 4.0)) };
        yield return new object[] { "rebaixamento total", P(new RampDefinition { Type = TipoRampa.RebaixamentoTotal }) };
    }

    [Theory]
    [MemberData(nameof(Ramps))]
    public void Item6_6_Figuras22a26_AlertaA050DaGuia_Largura040a060(string name, RampDefinition ramp)
    {
        var (from, to) = AlertSpan(ramp);
        Assert.True(Math.Abs(from - 0.50) < 0.01, $"{name}: alerta começa a {from:0.000} m da guia (0,50 m)");
        Assert.InRange(to - from, 0.40 - 0.01, 0.60 + 0.01);
    }

    // ---------------------------------------------------------------- Figura 69 / 7.8.5 b – direcional no eixo do rebaixamento

    private static (Y34TipTests.World W, List<MarkingDefinition> Defs, Polyline2 Axis, RoadSetup S) Crossing(bool network) =>
        AC3SidewalkExtensionTests.Road(false, 0, s =>
        {
            s.PisoTatil = network;
            s.ExtensoesCalcada.Add(new ExtensaoCalcada { LadoEsquerdo = false, Estaca = 40, Comprimento = 20, Travessia = true });
        }) is var (w, s, defs, axis) ? (w, defs, axis, s) : default;

    [Fact]
    public void Figura69_SemAlertaNoTopoDaRampa_RamalNoEixoAteAFaixaDaCalcada()
    {
        var (w, defs, axis, _) = Crossing(true);
        var ramps = defs.OfType<RampDefinition>().ToList();
        Assert.Equal(2, ramps.Count);
        // Nenhuma faixa de alerta avulsa no topo das rampas.
        Assert.DoesNotContain(defs.OfType<TactileRouteDefinition>(), t => t.AlertOnly);
        var routes = defs.OfType<TactileRouteDefinition>().ToList();
        foreach (var r in ramps)
        {
            Assert.True(r.DirectionalTactile, "direcional no eixo da rampa até a faixa de alerta");
            var (a, b, dir) = RoadFeatures.RampTop(r)!.Value;
            var mid = (a + b) / 2;
            var branch = routes.SelectMany(t => t.Branches ?? new()).Single(br => br[0].DistanceTo(mid) < 1e-6);
            // No eixo da rampa (na direção da subida) até a faixa principal.
            var u = (branch[^1] - branch[0]).Normalized();
            Assert.True(u.Dot(dir) > 0.999);
        }
        // Na geometria: relevos de alerta só na faixa a 0,50 m da guia (nada de domos logo depois do topo da rampa).
        var parts = Y34TipTests.Geometry(w);
        foreach (var r in ramps)
        {
            var (a, b, dir) = RoadFeatures.RampTop(r)!.Value;
            var probe = (a + b) / 2 + dir * 0.25;
            Assert.DoesNotContain(parts, p => p.Piece.Color == MarkingColor.RelevoTatil && IsDome(p.Piece.Shape) && p.Piece.Shape.Centroid.DistanceTo(probe) < 0.2);
        }
    }

    [Fact]
    public void Item7_8_5b_Figura69a_SemRedeNaCalcada_DirecionalTransversalAteOLote()
    {
        var (_, defs, axis, s) = Crossing(false);
        var ramps = defs.OfType<RampDefinition>().ToList();
        var routes = defs.OfType<TactileRouteDefinition>().Where(t => !t.AlertOnly).ToList();
        Assert.Equal(ramps.Count, routes.Count);
        foreach (var r in ramps)
        {
            var (a, b, dir) = RoadFeatures.RampTop(r)!.Value;
            var mid = (a + b) / 2;
            var tr = routes.Single(t => t.PathRef.Points[0].DistanceTo(mid) < 1e-6);
            var end = tr.PathRef.Points[^1];
            var lado = s.Lado(axis.Project(mid).Signed > 0);
            Assert.InRange(Math.Abs(axis.Project(end).Signed), lado.Face + lado.SidewalkWidth - 0.02, lado.Face + lado.SidewalkWidth + 0.02);
            Assert.True((end - mid).Normalized().Dot(dir) > 0.999);
            Assert.False(tr.AlertAtEnds);
        }
    }

    [Fact]
    public void Item7_8_5b_Intersecao_DirecionalNoEixoDeCadaRebaixamento()
    {
        // Avenidas (calçadas largas – rampas com abas): as rampas das calçadas, fora do canteiro central.
        var w = Y34TipTests.Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true },
            (2, new[] { new Vec2(-80, 0), new Vec2(80, 0) }), (2, new[] { new Vec2(0, -80), new Vec2(0, 80) }));
        var L = w.Nodes[0].Layout;
        var ramps = w.Defs.OfType<RampDefinition>().Where(r => r.Type != TipoRampa.RebaixamentoTotal &&
            !L.Obstacles.Any(o => o.Contains(RampGenerator.FrameOf(new Polyline2(r.PathRef.Points)).P(0, 0.3)))).ToList();
        Assert.NotEmpty(ramps);
        var routes = w.Defs.OfType<TactileRouteDefinition>().Where(t => !t.AlertOnly).ToList();
        foreach (var r in ramps)
        {
            Assert.True(r.DirectionalTactile);
            var (a, b, dir) = RoadFeatures.RampTop(r)!.Value;
            var mid = (a + b) / 2;
            var tr = routes.SingleOrDefault(t => t.PathRef.Points[0].DistanceTo(mid) < 1e-6);
            Assert.NotNull(tr);
            Assert.True((tr!.PathRef.Points[^1] - mid).Normalized().Dot(dir) > 0.999);
        }
    }

    // ---------------------------------------------------------------- Figuras 28 a 30 – rebaixamento de canteiro divisor

    [Theory]
    [InlineData(1.00, new[] { 0.30, 0.70 })]
    [InlineData(1.39, new[] { 0.495, 0.895 })]
    [InlineData(1.40, new[] { 0.50, 0.90 })]
    [InlineData(1.60, new[] { 0.50, 1.10 })]
    [InlineData(1.80, new[] { 0.50, 1.30 })]
    [InlineData(1.81, new[] { 0.50, 0.90, 0.91, 1.31 })]
    [InlineData(3.00, new[] { 0.50, 0.90, 2.10, 2.50 })]
    public void Figuras28a30_FaixasDeAlertaPelaLarguraDoCanteiro(double width, double[] expected)
    {
        var bands = MedianCrossing.AlertBands(width);
        Assert.Equal(expected.Length / 2, bands.Count);
        for (int i = 0; i < bands.Count; i++)
        {
            Assert.Equal(expected[2 * i], bands[i].A, 6);
            Assert.Equal(expected[2 * i + 1], bands[i].B, 6);
        }
        // As faixas geradas (no nível da pista) têm exatamente essas larguras e posições.
        var strips = MedianCrossing.AlertStrips((s, o) => new Vec2(s, o), 0, 4, 0, width, 0, null);
        Assert.Equal(bands.Count, strips.Count);
        foreach (var (t, (a, b)) in strips.Zip(bands))
        {
            Assert.Equal(b - a, t.Module * t.Rows, 6);
            Assert.Equal((a + b) / 2, t.PathRef.Points[0].Y, 6);
            Assert.Equal(0.0, t.Elevation);
        }
    }

    // ---------------------------------------------------------------- Figuras 27 e 75 – faixa elevada

    [Fact]
    public void Figuras27e75_FaixaElevada_AlertaNasCalcadasA050DaGuia()
    {
        var d = new TrafficCalmingDefinition { Type = TipoModeracao.FaixaElevada, Crosswalk = false, Marking = false };
        Assert.True(d.TactileAlert);
        var (len, h, ramp) = TrafficCalmingGenerator.Defaults(d.Type);
        var geo = TrafficCalmingGenerator.Generate(d, new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 7) }), Cat);
        var domes = geo.Pieces.Where(p => p.Color == MarkingColor.RelevoTatil && IsDome(p.Shape)).Select(p => p.Shape.Centroid).ToList();
        Assert.NotEmpty(domes);
        var sp = TactileGenerator.DomeSpacing;
        // Ao longo da travessia (eixo y): de cada lado, a 0,50 m da guia e 0,40 m de largura.
        var below = domes.Where(c => c.Y < 0).ToList();
        var above = domes.Where(c => c.Y > 7).ToList();
        Assert.Equal(0.50, -(below.Max(c => c.Y) + sp / 2), 2);
        Assert.Equal(0.40, below.Max(c => c.Y) - below.Min(c => c.Y) + sp, 2);
        Assert.Equal(0.50, above.Min(c => c.Y) - sp / 2 - 7, 2);
        // Na largura do platô (paralela à faixa de travessia), no nível da calçada.
        var plateau = len - 2 * ramp;
        Assert.InRange(below.Max(c => c.X) - below.Min(c => c.X) + sp, plateau - 0.06, plateau + 0.01);
        Assert.All(geo.Pieces.Where(p => p.Color == MarkingColor.Amarela), p => Assert.Equal(h, p.Elevation, 6));
        // Opção desligada: nada.
        d.TactileAlert = false;
        Assert.DoesNotContain(TrafficCalmingGenerator.Generate(d, new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 7) }), Cat).Pieces, p => p.Color == MarkingColor.RelevoTatil);
    }

    // ---------------------------------------------------------------- 7.4 – mudanças de direção e encontros

    private static List<Vec2> Domes(MarkingGeometry g) => g.Pieces.Where(p => p.Color == MarkingColor.RelevoTatil && IsDome(p.Shape)).Select(p => p.Shape.Centroid).ToList();

    [Theory]
    [InlineData(20, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(90, true)]
    public void Item7_4_2e7_4_3_AlertaNasMudancasDeDirecaoDe30grausOuMais(double deflection, bool alert)
    {
        var dir = Vec2.FromAngle(deflection * Math.PI / 180);
        var path = new Polyline2(new[] { new Vec2(-4, 0), Vec2.Zero, dir * 4 });
        var d = new TactileRouteDefinition { Module = 0.25, Rows = 1, AlertAtEnds = false };
        var domes = Domes(TactileGenerator.Route(d, new[] { path }, 0.15));
        Assert.Equal(alert, domes.Any(c => c.Length < 0.5));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.40)]
    public void Item7_4_3_Figura47_AreaDeAlertaComODobroDaLargura(double module)
    {
        // Curva de 90° à esquerda em (0, 0): o quadrado tem 2 × largura e o canto externo no encontro dos alinhamentos de fora.
        var d = new TactileRouteDefinition { Module = module, Rows = 1, AlertAtEnds = false };
        var geo = TactileGenerator.Route(d, new[] { new Polyline2(new[] { new Vec2(-4, 0), Vec2.Zero, new Vec2(0, 4) }) }, 0.15);
        var near = geo.Pieces.Where(p => p.Color == MarkingColor.Amarela && p.Shape.Centroid.Length < 1.5 &&
                                         geo.Pieces.Any(r => r.Color == MarkingColor.RelevoTatil && IsDome(r.Shape) && p.Shape.Contains(r.Shape.Centroid))).ToList();
        var mn = new Vec2(near.Min(p => p.Shape.Bounds.Min.X), near.Min(p => p.Shape.Bounds.Min.Y));
        var mx = new Vec2(near.Max(p => p.Shape.Bounds.Max.X), near.Max(p => p.Shape.Bounds.Max.Y));
        var w = module;
        Assert.InRange(mx.X - mn.X, 2 * w - 0.005, 2 * w + 0.001);
        Assert.InRange(mx.Y - mn.Y, 2 * w - 0.005, 2 * w + 0.001);
        Assert.InRange(mx.X, w / 2 - 0.003, w / 2 + 0.001);       // alinhamento de fora do trecho de saída
        Assert.InRange(mn.Y, -w / 2 - 0.001, -w / 2 + 0.003);     // alinhamento de fora do trecho de chegada
    }

    private static (Vec2 Min, Vec2 Max) AlertBounds(MarkingGeometry geo, Vec2 at, double r)
    {
        var tiles = geo.Pieces.Where(p => p.Color == MarkingColor.Amarela && p.Shape.Centroid.DistanceTo(at) < r &&
                                          geo.Pieces.Any(q => q.Color == MarkingColor.RelevoTatil && IsDome(q.Shape) && p.Shape.Contains(q.Shape.Centroid))).ToList();
        return (new Vec2(tiles.Min(p => p.Shape.Bounds.Min.X), tiles.Min(p => p.Shape.Bounds.Min.Y)),
                new Vec2(tiles.Max(p => p.Shape.Bounds.Max.X), tiles.Max(p => p.Shape.Bounds.Max.Y)));
    }

    [Theory]
    [InlineData(0.25, 0.75)]
    [InlineData(0.40, 0.90)]
    public void Item7_4_4_Figura48_EncontroDeTresFaixas_TriploDaLarguraAte090(double module, double along)
    {
        var main = new Polyline2(new[] { new Vec2(-5, 0), new Vec2(5, 0) });
        var branch = new Polyline2(new[] { new Vec2(0, 4), new Vec2(0, 0) });
        var d = new TactileRouteDefinition { Module = module, Rows = 1, AlertAtEnds = false, AlertAtTurns = false };
        var geo = TactileGenerator.Route(d, new[] { main, branch }, 0.15);
        var (mn, mx) = AlertBounds(geo, Vec2.Zero, 1.2);
        // Placas desenhadas com a junta (2 mm para dentro de cada borda).
        Assert.InRange(mx.X - mn.X, along - 0.005, along + 0.001);
        Assert.InRange((mx.X + mn.X) / 2, -0.003, 0.003);            // centrada no ramal
        Assert.InRange(mn.Y, -module / 2 - 0.001, -module / 2 + 0.003);  // da borda de lá da faixa contínua
        Assert.InRange(mx.Y, 1.5 * module - 0.003, 1.5 * module + 0.001); // a uma largura além da borda de cá
    }

    [Fact]
    public void Item7_4_5_Figura51_EncontroDeQuatroFaixas_QuadradoDoTriploDaLargura()
    {
        var main = new Polyline2(new[] { new Vec2(-5, 0), new Vec2(5, 0) });
        var up = new Polyline2(new[] { new Vec2(0, 4), new Vec2(0, 0) });
        var down = new Polyline2(new[] { new Vec2(0, -4), new Vec2(0, 0) });
        var d = new TactileRouteDefinition { Module = 0.25, Rows = 1, AlertAtEnds = false, AlertAtTurns = false };
        var geo = TactileGenerator.Route(d, new[] { main, up, down }, 0.15);
        var (mn, mx) = AlertBounds(geo, Vec2.Zero, 1.2);
        Assert.InRange(mx.X - mn.X, 0.75 - 0.005, 0.75 + 0.001);
        Assert.InRange(mx.Y - mn.Y, 0.75 - 0.005, 0.75 + 0.001);
        Assert.InRange((mx.X + mn.X) / 2, -0.003, 0.003);
        Assert.InRange((mx.Y + mn.Y) / 2, -0.003, 0.003);
    }

    // ---------------------------------------------------------------- 7.3 e 7.8.2 – faixa direcional nas calçadas

    [Fact]
    public void Item7_3_5e7_3_7_LarguraECorConstantes_AlertaDaMesmaCor()
    {
        var (_, defs, _, _) = Crossing(true);
        var routes = defs.OfType<TactileRouteDefinition>().ToList();
        Assert.NotEmpty(routes);
        Assert.All(routes, t => Assert.InRange(t.Module * t.Rows, 0.25, 0.40));
        Assert.Single(routes.Select(t => t.Module * t.Rows).Distinct());
        var geo = TactileGenerator.Route(routes[0], new[] { new Polyline2(routes[0].PathRef.Points) }.Concat((routes[0].Branches ?? new()).Select(b => new Polyline2(b))).ToList(), 0.15);
        Assert.Single(geo.Pieces.Where(p => p.Color != MarkingColor.RelevoTatil).Select(p => p.Color).Distinct());
    }

    [Theory]
    [InlineData(2.00, "a")]
    [InlineData(1.45, "a")]
    [InlineData(1.30, "b")]
    [InlineData(1.20, "b")]
    [InlineData(1.00, "c")]
    public void Item7_8_2_PosicaoDaFaixaDirecionalPelaLarguraDaFaixaLivre(double free, string alinea)
    {
        var lado = new RoadSetup.LadoVia(1, 5, true, 3.0, 0.15, 0.70, free, 0, 0.15, null, 0);
        const double w = 0.25;
        var c = lado.TactileCenter(w);
        var start = lado.CurbW + lado.Service;
        var (left, right) = (c - w / 2 - start, start + free - (c + w / 2));
        switch (alinea)
        {
            case "a":   // no eixo da faixa livre, 0,60 m lisos de cada lado
                Assert.Equal(lado.FreeCenter, c, 9);
                Assert.True(left >= 0.60 - 1e-9 && right >= 0.60 - 1e-9);
                break;
            case "b":   // fora do eixo, 0,60 m lisos de um dos lados (o do lote)
                Assert.Equal(0.60, right, 9);
                Assert.True(left >= 0);
                break;
            default:    // no eixo da faixa livre (com alerta de 0,50 m nas pontas)
                Assert.Equal(lado.FreeCenter, c, 9);
                break;
        }
    }

    [Fact]
    public void Item7_8_2c_FaixaLivreMenorQue120_AlertaDe050NasPontasEAviso()
    {
        var s = RoadTemplates.All[0].Create();
        var walk = s.Right.First(e => e.Tipo == TipoElementoSecao.Calcada);
        var lado0 = s.Lado(false);
        // Faixa livre de 1,00 m (aumenta a faixa de acesso).
        walk.FaixaAcesso = Math.Max(0, lado0.Free + lado0.Access - 1.00);
        s.PisoTatil = true;
        var lado = s.Lado(false);
        Assert.InRange(lado.Free, 0.99, 1.01);
        var pr = PathReference.FromPoints(new[] { new Vec2(-50, 0), new Vec2(50, 0) }, 0);
        var defs = s.Build(pr, new OutputSettings(), Cat, axis: new Polyline2(pr.Points));
        var main = defs.OfType<TactileRouteDefinition>().Single(t => t.PathRef.Points.All(p => p.Y < 0));
        Assert.True(main.AlertAtEnds);
        Assert.Equal(0.50, main.EndAlertModules * main.Module, 6);
        Assert.Contains(s.Warnings, x => x.Contains("7.8.2 c"));
    }
}
