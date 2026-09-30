using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada Z: orelhas (avanços de calçada) geradas com a interseção – avanço medido ao longo do meio-fio, término no início
/// do estacionamento, sem sobrepor rampas e travessias e sem falhas de modelagem.
/// </summary>
public class Z1EarTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static IntersectionDefinition Ears(double length = 5.0, bool crosswalks = true, bool toParking = false) => new()
    {
        Control = ControleIntersecao.Pare, Crosswalks = crosswalks, CurbExtensions = true, CurbExtensionLength = length,
        CurbExtensionToParking = toParking,
    };

    /// <summary>Cruz de duas vias locais (estacionamento dos dois lados) ou T.</summary>
    private static (int, Vec2[])[] Cross(int a = 0, int b = 0) =>
        new[] { (a, new[] { new Vec2(-90, 0), new Vec2(90, 0) }), (b, new[] { new Vec2(0, -90), new Vec2(0, 90) }) };

    private static (int, Vec2[])[] Tee(int a = 0, int b = 0) =>
        new[] { (a, new[] { new Vec2(-90, 0), new Vec2(90, 0) }), (b, new[] { Vec2.Zero, new Vec2(0, 90) }) };

    private static List<Y34TipTests.Part> Parts(Y34TipTests.World w) => Y34TipTests.Geometry(w);

    /// <summary>Maior distância (ao longo do ramo, a partir do fim da curva) em que a orelha toca a face do meio-fio.</summary>
    private static double ReachAlong(IntersectionLayout L, IntersectionLeg leg, int side, Polygon2 fp)
    {
        var axis = L.Roads[leg.Road].Axis;
        var best = 0.0;
        foreach (var v in fp.Outer)
        {
            var (st, signed) = axis.Project(v);
            var t = (st - leg.NodeStation) * leg.Sign;
            var o = signed * leg.Sign;
            if (t < leg.Clear - 0.01) continue;
            var face = side > 0 ? L.HiEdge(leg, t) : -L.LoEdge(leg, t);
            if (Math.Abs(o - face) < 0.005) best = Math.Max(best, t - leg.Clear);
        }
        return best;
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(10.0)]
    public void Orelha_da_intersecao_avanca_o_pedido_ao_longo_do_meio_fio(double len)
    {
        var w = Y34TipTests.Make(Ears(len, crosswalks: false), Cross());
        var L = w.Nodes[0].Layout;
        Assert.Equal(4, L.Ears.Count);
        foreach (var e in L.Ears)
        {
            Assert.True(e.SideA && e.SideB);
            Assert.Equal(2.5, e.Depth, 2);   // faixa de estacionamento 2,20 m + sarjeta 0,30 m
            Assert.Equal(len, ReachAlong(L, e.LegA, 1, e.Footprint), 2);
            Assert.Equal(len, ReachAlong(L, e.LegB, -1, e.Footprint), 2);
        }
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Theory]
    [InlineData("cruz")]
    [InlineData("T")]
    public void Orelhas_da_intersecao_sem_sobrepor_rampas_e_travessias(string kind)
    {
        var w = Y34TipTests.Make(Ears(5.0), kind == "T" ? Tee() : Cross());
        var L = w.Nodes[0].Layout;
        Assert.Equal(kind == "T" ? 2 : 4, L.Ears.Count);
        var parts = Parts(w);
        var ears = parts.Where(p => p.Def is CurbExtensionDefinition).Select(p => p.Piece.Shape).ToList();
        Assert.NotEmpty(ears);
        var ramps = parts.Where(p => p.Def is RampDefinition).Select(p => p.Piece.Shape).ToList();
        Assert.NotEmpty(ramps);
        var crosswalks = parts.Where(p => p.Def is LinearMarkingDefinition { Code: "FTP-1" or "FTP-2" } || p.Def is HatchMarkingDefinition h && h.Code.StartsWith("FTP"))
            .Select(p => p.Piece.Shape).ToList();
        Assert.NotEmpty(crosswalks);
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(ears, ramps)) < 0.01, "orelha sobre a rampa");
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(ears, crosswalks)) < 0.01, "orelha sobre a travessia");
        // As rampas ficam na borda da orelha (a travessia começa nela).
        foreach (var e in L.Ears)
            Assert.Contains(w.Defs.OfType<RampDefinition>(), r => e.Footprint.Contains(r.PathRef.Points[0] + (r.PathRef.Points[1] - r.PathRef.Points[0]) * -0.05) is false
                                                                    && PolygonOps.Offset(new[] { e.Footprint }, 0.05).Any(p => p.Contains(r.PathRef.Points[0])));
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Fact]
    public void Orelha_termina_no_inicio_do_estacionamento()
    {
        var w = Y34TipTests.Make(Ears(toParking: true), Cross());
        var L = w.Nodes[0].Layout;
        Assert.Equal(4, L.Ears.Count);
        var parts = Parts(w);
        var stalls = parts.Where(p => p.Def is ParkingMarkingDefinition).Select(p => p.Piece.Shape).ToList();
        Assert.NotEmpty(stalls);
        var ears = parts.Where(p => p.Def is CurbExtensionDefinition).Select(p => p.Piece.Shape).ToList();
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(ears, stalls)) < 0.01, "vaga sob a orelha");
        foreach (var e in L.Ears)
            foreach (var (leg, side) in new[] { (e.LegA, 1), (e.LegB, -1) })
            {
                var end = ReachAlong(L, leg, side, e.Footprint) + leg.Clear;
                // Primeira vaga depois da orelha, na faixa de estacionamento deste lado.
                var first = double.MaxValue;
                for (var t = leg.Clear; t < leg.Clear + 40; t += 0.05)
                {
                    var o = side > 0 ? L.HiEdge(leg, t) - 1.2 : -(L.LoEdge(leg, t) - 1.2);
                    if (stalls.Any(s => s.Contains(L.At(leg, t, o)))) { first = t; break; }
                }
                Assert.True(first < double.MaxValue, "sem vagas depois da orelha");
                Assert.InRange(first - end, -0.05, 1.0);
            }
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Fact]
    public void Via_sem_estacionamento_nao_recebe_orelha()
    {
        // Coletora (sem estacionamento) × local: só as esquinas com estacionamento dos dois lados envolvem a esquina.
        var w = Y34TipTests.Make(Ears(5.0), Cross(1, 0));
        var L = w.Nodes[0].Layout;
        Assert.All(L.Ears, e => Assert.False(e.SideA && L.Roads[e.LegA.Road].Def.SetupJson != null
                                             && IntersectionGenerator.ParkingDepth(L.Roads[e.LegA.Road].Def, e.LegA.Sign > 0) == null));
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Theory]
    [InlineData("T_ponta_no_meio", 0, 0, 60.0)]
    [InlineData("T_ponta_no_meio", 0, 0, 120.0)]
    [InlineData("cruz_de_pontas", 0, 0, 90.0)]
    [InlineData("T_ponta_no_meio", 2, 0, 90.0)]
    [InlineData("cruz_de_pontas", 0, 2, 60.0)]
    public void Orelhas_em_varios_cruzamentos_sem_falhas(string kind, int a, int b, double ang)
    {
        var w = Y34TipTests.Make(Ears(5.0), Y34TipTests.Scenario(kind, a, b, ang));
        Assert.Contains(w.Nodes, n => n.Layout.Ears.Count > 0);
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    // ------------------------------------------------------------------ orelha avulsa por clique na esquina (Z2)

    [Theory]
    [InlineData(5.0, 10.0)]
    [InlineData(10.0, 5.0)]
    [InlineData(5.0, 0.0)]
    public void Orelha_por_clique_na_esquina_avanca_X_e_Y_ao_longo_do_meio_fio(double x, double y)
    {
        // Clique na esquina nordeste, junto ao meio-fio da via leste-oeste (y ≈ 5,5 m): X nessa via, Y na norte-sul.
        var click = new Vec2(14, 6.0);
        var w = Y34TipTests.Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = false }, (d, rs) =>
        {
            var L0 = IntersectionGenerator.Layout((IntersectionDefinition)d.CloneWithNewId(), rs.ToList());
            var c = IntersectionGenerator.CornerAt(L0, click);
            Assert.NotNull(c);
            IntersectionGenerator.SetCornerEar(d, L0, c!.Value.A, c.Value.NearA, x, y, false);
        }, Cross());
        var L = w.Nodes[0].Layout;
        var e = Assert.Single(L.Ears);
        var (clicked, cs, other, os) = e.LegA.Dir.X > 0.9 ? (e.LegA, 1, e.LegB, -1) : (e.LegB, -1, e.LegA, 1);
        Assert.True(clicked.Dir.X > 0.9, "a via clicada é a leste-oeste");
        Assert.Equal(x, ReachAlong(L, clicked, cs, e.Footprint), 2);
        if (y > 0) Assert.Equal(y, ReachAlong(L, other, os, e.Footprint), 2);
        else Assert.True(ReachAlong(L, other, os, e.Footprint) < 0.01, "Y = 0: a orelha não avança pela outra via");
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }
}
