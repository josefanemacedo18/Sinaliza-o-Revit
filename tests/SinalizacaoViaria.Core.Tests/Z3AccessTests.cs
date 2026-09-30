using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada Z: abrir acesso numa via existente – retorno em U (abertura de canteiro, bolsão, alargamento) com o raio mínimo do
/// veículo de projeto, e nova via em T ligada à existente, sem falhas de pavimento, meio-fio ou calçada.
/// </summary>
public class Z3AccessTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    /// <summary>Via reta de −L a +L no eixo X com os retornos pedidos (a via gerada como o plugin faz).</summary>
    private static (Y34TipTests.World W, RoadSetup Setup, List<RetornoPlan> Plans) Road(int tpl, params RetornoVia[] rets) => Road(tpl, 0, rets);

    private static (Y34TipTests.World W, RoadSetup Setup, List<RetornoPlan> Plans) Road(int tpl, double median, params RetornoVia[] rets)
    {
        var w = new Y34TipTests.World();
        var setup = RoadTemplates.All[tpl].Create();
        if (median > 0) setup.MedianWidth = median;
        setup.Retornos.AddRange(rets);
        var pr = PathReference.FromPoints(new[] { new Vec2(-120, 0), new Vec2(120, 0) }, 0);
        var axis = new Polyline2(pr.Points);
        var g = setup.Build(pr, new OutputSettings(), Cat, axis: axis);
        foreach (var m in g)
            if (m.Path is { } p && p.Points.Count == pr.Points.Count && p.Points.SequenceEqual(pr.Points)) w.Paths[m.Id] = axis;
        w.Defs.AddRange(g);
        w.Groups.Add(g);
        w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        return (w, setup, setup.Retornos.Select(setup.PlanRetorno).ToList());
    }

    /// <summary>Faixa varrida no giro: meio anel entre Ri e Ro em volta do centro, a partir do início do giro.</summary>
    private static Polygon2 Swept(RetornoPlan p)
    {
        var pts = new List<Vec2>();
        Vec2 At(double t, double u) => new(p.Station(t) - 120, p.Offset(u));
        for (int i = 0; i <= 60; i++)
        {
            var a = -Math.PI / 2 + Math.PI * i / 60;
            pts.Add(At(p.Ro * Math.Cos(a), p.CenterU + p.Ro * Math.Sin(a)));
        }
        for (int i = 60; i >= 0; i--)
        {
            var a = -Math.PI / 2 + Math.PI * i / 60;
            pts.Add(At(p.Ri * Math.Cos(a), p.CenterU + p.Ri * Math.Sin(a)));
        }
        return PolygonOps.Union(new[] { new Polygon2(pts) }).OrderByDescending(x => x.Area).First();
    }

    public static IEnumerable<object[]> Cases()
    {
        // Avenida com canteiro central (3 m): abertura, bolsão no canteiro, alargamento; via local (sem canteiro): bolsão lateral e alargamento.
        foreach (var v in new[] { VeiculoProjeto.VP, VeiculoProjeto.CO, VeiculoProjeto.SR })
        {
            yield return new object[] { 2, TipoRetorno.AberturaCanteiro, v, true, 0.0 };
            yield return new object[] { 2, TipoRetorno.Bolsao, v, true, 6.0 };   // bolsão recortado do canteiro largo
            yield return new object[] { 2, TipoRetorno.Bolsao, v, false, 0.0 };
            yield return new object[] { 0, TipoRetorno.Bolsao, v, true, 0.0 };
            yield return new object[] { 0, TipoRetorno.Alargamento, v, false, 0.0 };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Retorno_com_raio_minimo_do_veiculo_de_projeto(int tpl, TipoRetorno tipo, VeiculoProjeto v, bool sentidoDoEixo, double median)
    {
        var r = new RetornoVia { Estaca = 120, Tipo = tipo, Veiculo = v, SentidoDoEixo = sentidoDoEixo };
        var (w, _, plans) = Road(tpl, median, r);
        var p = Assert.Single(plans);
        var (ro, ri, _, _) = RetornoVia.Dimensoes(v);
        Assert.True(p.Ro >= ro - 1e-9 && p.Ri <= ri + 1e-9, "raios do veículo de projeto");
        var parts = Y34TipTests.Geometry(w);
        // Pista = pavimento + sarjeta (no nível da pista).
        bool Pav(MarkingPiece x) => x.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto
                                    || x.Color == MarkingColor.Concreto && x.Thickness <= 0.03;
        bool Raised(MarkingPiece x) => x.Color is MarkingColor.Concreto or MarkingColor.Grama && x.Thickness > 0.03;
        var pav = PolygonOps.Union(parts.Where(x => Pav(x.Piece)).Select(x => x.Piece.Shape));
        var swept = Swept(p);
        var outside = PolygonOps.TotalArea(PolygonOps.Difference(new[] { swept }, pav));
        Assert.True(outside < 0.05, $"faixa varrida fora da pista: {outside:0.000} m²");
        var hit = PolygonOps.TotalArea(PolygonOps.Intersect(new[] { swept }, parts.Where(x => Raised(x.Piece)).Select(x => x.Piece.Shape)));
        Assert.True(hit < 0.05, $"meio-fio/canteiro/calçada na faixa varrida: {hit:0.000} m²");
        var f = Y34TipTests.FaultsAt(parts, new Vec2(0, 0), 60, walks: true);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
        // Sinalização do retorno.
        Assert.Contains(w.Defs.OfType<SymbolMarkingDefinition>(), s => s.Code == "PEM-RE");
        if (p.Opening != null) Assert.Contains(w.Defs.OfType<SignDefinition>(), s => s.Code == "R-3");
        if (median > 4) Assert.True(p.Opening?.PocketWidth > 2.5, "bolsão no canteiro");
    }

    // ------------------------------------------------------------------ nova via em T

    [Theory]
    [InlineData(0, 0, 90.0, 1)]
    [InlineData(0, 0, 60.0, -1)]
    [InlineData(1, 0, 120.0, 1)]
    [InlineData(2, 0, 90.0, 1)]
    [InlineData(2, 1, 75.0, -1)]
    public void Nova_via_em_T_conectada_sem_falhas(int host, int minor, double ang, int side)
    {
        var hostAxis = new Polyline2(new[] { new Vec2(-100, 0), new Vec2(100, 0) });
        var branch = AcessoVia.BranchAxis(hostAxis, new Vec2(12, side * 20), ang, 70);
        Assert.Equal(0, branch[0].Y, 6);
        var dir = (branch[1] - branch[0]).Normalized();
        Assert.Equal(ang, Math.Acos(Math.Clamp(dir.X, -1, 1)) * 180 / Math.PI, 3);   // ângulo com o eixo existente
        Assert.Equal(side, Math.Sign(dir.Y));                                        // para o lado do clique
        var w = Y34TipTests.Make(null, (host, hostAxis.Points.ToArray()), (minor, branch.ToArray()));
        var node = Assert.Single(w.Nodes);
        Assert.Equal(3, node.Layout.Legs.Count);
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
        // Canteiro central da via existente aberto na boca da nova via (conversões à esquerda).
        if (host == 2)
        {
            var parts = Y34TipTests.Geometry(w);
            var pav = PolygonOps.Union(parts.Where(x => x.Piece.Color == MarkingColor.Asfalto).Select(x => x.Piece.Shape));
            Assert.Contains(pav, p => p.Contains(new Vec2(branch[0].X, 0)));
        }
    }
}
