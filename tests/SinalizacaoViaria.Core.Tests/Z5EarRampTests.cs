using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada Z5: orelhas sem deformação (um contorno limpo, sem lóbulos nem dentes), integradas à calçada (meio-fio e sarjeta
/// contornando a frente, nada entre a calçada e a orelha) e rampas conforme a NBR 9050 – rebaixamento total quando a
/// calçada não comporta a rampa e a faixa livre de 1,20 m; rampa comum com a faixa livre garantida atrás dela.
/// </summary>
public class Z5EarRampTests
{
    private static IntersectionDefinition Def(bool ears, bool toParking = false) => new()
    {
        Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true, CurbExtensions = ears, CurbExtensionLength = 5.0,
        CurbExtensionToParking = toParking,
    };

    public static IEnumerable<object[]> EarCases()
    {
        yield return new object[] { "cruz_locais", 0, 0, 90.0, false };
        yield return new object[] { "cruz_larguras_diferentes", 0, 2, 90.0, false };
        yield return new object[] { "T_60", 0, 0, 60.0, false };
        yield return new object[] { "T_120", 2, 0, 120.0, false };
        yield return new object[] { "cruz_ate_o_estacionamento", 0, 0, 90.0, true };
        yield return new object[] { "clique_na_esquina", 0, 0, 90.0, false };
    }

    private static Y34TipTests.World Scene(string kind, int a, int b, double ang, bool toParking)
    {
        if (kind == "clique_na_esquina")
            // Orelha avulsa: clique na esquina nordeste, 5 m ao longo da via leste-oeste e 8 m ao longo da norte-sul.
            return Y34TipTests.Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true }, (d, rs) =>
            {
                var L0 = IntersectionGenerator.Layout((IntersectionDefinition)d.CloneWithNewId(), rs.ToList());
                var c = IntersectionGenerator.CornerAt(L0, new Vec2(14, 6.0));
                Assert.NotNull(c);
                IntersectionGenerator.SetCornerEar(d, L0, c!.Value.A, c.Value.NearA, 5.0, 8.0, false);
            }, (a, new[] { new Vec2(-90, 0), new Vec2(90, 0) }), (b, new[] { new Vec2(0, -90), new Vec2(0, 90) }));
        var roads = kind.StartsWith("cruz")
            ? new[] { (a, new[] { new Vec2(-90, 0), new Vec2(90, 0) }), (b, new[] { Y34TipTests.Dir(ang) * -90, Y34TipTests.Dir(ang) * 90 }) }
            : Y34TipTests.Scenario("T_ponta_no_meio", a, b, ang);
        return Y34TipTests.Make(Def(true, toParking), roads);
    }

    /// <summary>Maior giro (graus) entre segmentos consecutivos da polilinha.</summary>
    private static double MaxTurn(IReadOnlyList<Vec2> pts)
    {
        var max = 0.0;
        for (int i = 1; i + 1 < pts.Count; i++)
        {
            var u = pts[i] - pts[i - 1];
            var v = pts[i + 1] - pts[i];
            if (u.Length < 1e-6 || v.Length < 1e-6) continue;
            var ang = Math.Acos(Math.Clamp(u.Dot(v) / (u.Length * v.Length), -1, 1)) * 180 / Math.PI;
            max = Math.Max(max, ang);
        }
        return max;
    }

    [Theory]
    [MemberData(nameof(EarCases))]
    public void Orelha_com_contorno_limpo_sem_lobulos_nem_dentes(string kind, int a, int b, double ang, bool toParking)
    {
        var w = Scene(kind, a, b, ang, toParking);
        var ears = w.Nodes.SelectMany(n => n.Layout.Ears).ToList();
        Assert.NotEmpty(ears);
        foreach (var e in ears)
        {
            // Caminho da face do meio-fio sem degraus (o degrau de 3 cm gerava o lóbulo e o dente).
            Assert.True(MaxTurn(e.Path.Points) < 45, $"{kind}: caminho da orelha com giro de {MaxTurn(e.Path.Points):0}°");
            // Bordo externo paralelo à face: sem meia-volta nem dente.
            var outer = SidewalkGenerator.EarOuterEdge(e.Template, e.Path);
            Assert.True(MaxTurn(outer) < 45, $"{kind}: bordo da orelha com giro de {MaxTurn(outer):0}°");
            Assert.False(Y34TipTests.SelfIntersects(e.Footprint), $"{kind}: contorno da orelha com autointerseção");
            Assert.Empty(e.Footprint.Holes);
        }
        // Cada orelha é uma peça só: plataforma, meio-fio e as rampas encaixadas nela formam um piso contínuo, sem pedaços soltos.
        var parts = Y34TipTests.Geometry(w);
        var ramps = parts.Where(p => p.Def is RampDefinition).GroupBy(p => p.Def).Select(g => g.Select(p => p.Piece.Shape).ToList()).ToList();
        foreach (var ce in w.Defs.OfType<CurbExtensionDefinition>())
        {
            var mine = parts.Where(p => p.Def == ce).Select(p => p.Piece.Shape).ToList();
            Assert.All(mine, m => Assert.True(m.Area >= 0.01, $"{kind}: peça solta de {m.Area:0.000} m²"));
            var grown = PolygonOps.Offset(mine, 0.02);
            var near = ramps.Where(r => PolygonOps.TotalArea(PolygonOps.Intersect(r, grown)) > 0.01).SelectMany(r => r);
            var u = PolygonOps.Offset(mine.Concat(near), 0.01).Where(p => p.Area > 0.01).ToList();
            Assert.True(u.Count == 1, $"{kind}: orelha em {u.Count} pedaços");
        }
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Theory]
    [MemberData(nameof(EarCases))]
    public void Meio_fio_e_sarjeta_contornam_a_frente_da_orelha(string kind, int a, int b, double ang, bool toParking)
    {
        var w = Scene(kind, a, b, ang, toParking);
        var parts = Y34TipTests.Geometry(w);
        foreach (var node in w.Nodes)
            foreach (var e in node.Layout.Ears)
            {
                var ce = w.Defs.OfType<CurbExtensionDefinition>().First(c => PolygonOps.TotalArea(PolygonOps.Intersect(parts.Where(p => p.Def == c).Select(p => p.Piece.Shape), new[] { e.Footprint })) > 0.5);
                var earPieces = parts.Where(p => p.Def == ce).ToList();
                // Atrás da face original (onde estava o meio-fio antigo) não sobra meio-fio de ninguém: é piso da orelha.
                var behind = SidewalkGenerator.EarBackFill(ce, e.Path, e.Footprint);
                var oldCurb = parts.Where(p => p.Def != ce && (p.Piece.Layer?.StartsWith("MEIO-FIO") == true || p.Def is LinearMarkingDefinition l && l.Code.StartsWith("MEIO-FIO")))
                    .Select(p => p.Piece.Shape).ToList();
                Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(behind, oldCurb)) < 0.01, $"{kind}: meio-fio entre a calçada e a orelha");
                var ramps = parts.Where(p => p.Def is RampDefinition).Select(p => p.Piece.Shape).ToList();
                var gap = PolygonOps.TotalArea(PolygonOps.Difference(PolygonOps.Difference(behind, earPieces.Select(p => p.Piece.Shape)), ramps));
                Assert.True(gap < 0.02, $"{kind}: vão de {gap:0.000} m² entre a calçada e a orelha");
                // O meio-fio da orelha fica na face externa (voltado para a pista), junto ao bordo.
                var outer = SidewalkGenerator.EarOuterEdge(e.Template, e.Path);
                var band = PolygonOps.Strip(outer, 2 * e.Template.CurbWidth + 0.02, roundJoins: true);
                var curb = earPieces.Where(p => p.Piece.Layer == "MEIO-FIO").Select(p => p.Piece.Shape).ToList();
                Assert.NotEmpty(curb);
                Assert.True(PolygonOps.TotalArea(PolygonOps.Difference(curb, band)) < 0.01, $"{kind}: meio-fio da orelha fora da face externa");
                // A sarjeta da via segue pela frente da orelha, no nível da pista.
                if (e.Gutter > 0.01)
                {
                    var gp = parts.Where(p => p.Def is LinearMarkingDefinition { Code: "SARJETA" }).Select(p => p.Piece.Shape).ToList();
                    var front = PolygonOps.Difference(PolygonOps.Strip(outer, 2 * e.Gutter + 0.1, roundJoins: true), new[] { e.Footprint });
                    Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(gp, front)) > 0.25 * e.Gutter * e.Path.Length, $"{kind}: sem sarjeta na frente da orelha");
                    Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(gp, new[] { e.Footprint })) < 0.01, $"{kind}: sarjeta sob a orelha");
                }
            }
    }

    /// <summary>Piso de calçada (concreto elevado) livre atrás do fim da rampa, na direção da subida (m).</summary>
    public static double FreeBehind(List<Y34TipTests.Part> parts, RampDefinition r)
    {
        var p0 = r.PathRef.Points[0];
        var up = (r.PathRef.Points[^1] - p0).Normalized();
        var (_, run, _) = RampGenerator.Dimensions(r);
        var walk = parts.Where(x => x.Piece.Color == MarkingColor.Concreto && x.Piece.Thickness >= 0.1 && x.Piece.Layer?.StartsWith("MEIO-FIO") != true && x.Def is not RampDefinition)
            .Select(x => x.Piece.Shape).ToList();
        var len = 0.0;
        for (var s = run + r.LandingDepth + 0.03; s < run + r.LandingDepth + 6; s += 0.05)
        {
            if (!walk.Any(w => w.Contains(p0 + up * s))) break;
            len += 0.05;
        }
        return len;
    }

    [Theory]
    [InlineData(0, false, "cruz")]
    [InlineData(0, false, "T")]
    public void Calcada_estreita_recebe_rebaixamento_total_com_rampas_laterais(int tpl, bool ears, string kind)
    {
        var roads = kind == "cruz"
            ? new[] { (tpl, new[] { new Vec2(-90, 0), new Vec2(90, 0) }), (tpl, new[] { new Vec2(0, -90), new Vec2(0, 90) }) }
            : new[] { (tpl, new[] { new Vec2(-90, 0), new Vec2(90, 0) }), (tpl, new[] { Vec2.Zero, new Vec2(0, 90) }) };
        var w = Y34TipTests.Make(Def(ears), roads);
        var road = w.Defs.OfType<RoadPavementDefinition>().First();
        var ramps = w.Defs.OfType<RampDefinition>().ToList();
        Assert.NotEmpty(ramps);
        Assert.All(ramps, r =>
        {
            // Calçada de 2,50 m: 1,80 m de rampa (8,33 %) deixaria só 0,70 m livres → rebaixamento total (NBR 9050, 6.12.7.3.3).
            Assert.Equal(TipoRampa.RebaixamentoTotal, r.Type);
            Assert.Equal(road.LeftSidewalk, r.SidewalkDepth, 2);
            Assert.True(r.Slope <= 0.05 + 1e-9, $"rampas laterais a {r.Slope:P1}");
            Assert.True(r.Tactile);
            Assert.True(r.Width >= 1.50 - 1e-9);
        });
        var parts = Y34TipTests.Geometry(w);
        foreach (var r in ramps)
        {
            var mine = parts.Where(p => p.Def == r).ToList();
            // Platô no nível da pista (placa fina) em toda a profundidade da calçada, rampas laterais até o nível dela e piso tátil.
            Assert.Contains(mine, p => p.Piece.Color == MarkingColor.Concreto && p.Piece.Thickness <= 0.01);
            Assert.Contains(mine, p => p.Piece.Color == r.TactileColor);
        }
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Rampa_comum_so_com_faixa_livre_de_1_20_m_atras(int tpl, bool ears)
    {
        var w = Y34TipTests.Make(Def(ears), new[] { (tpl, new[] { new Vec2(-90, 0), new Vec2(90, 0) }), (0, new[] { new Vec2(0, -90), new Vec2(0, 90) }) });
        var parts = Y34TipTests.Geometry(w);
        var common = w.Defs.OfType<RampDefinition>().Where(r => r.Type != TipoRampa.RebaixamentoTotal).ToList();
        Assert.NotEmpty(common);
        foreach (var r in common)
        {
            var free = FreeBehind(parts, r);
            Assert.True(free >= 1.19, $"rampa em {r.PathRef.Points[0]}: {free:0.00} m livres atrás (patamar {r.LandingDepth:0.00} m)");
        }
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }
}
