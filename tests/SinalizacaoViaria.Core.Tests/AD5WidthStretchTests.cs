using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AD (itens 19 e 20): alterar a largura por trecho – entre A e B o elemento escolhido tem a nova largura, com uma
/// transição em cada ponta; o resto da seção mantém a largura.
/// </summary>
public class AD5WidthStretchTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static readonly Polyline2 Axis = new(new[] { new Vec2(0, 0), new Vec2(150, 0) });

    private static (List<MarkingDefinition> Defs, RoadPavementDefinition Pav) Build(RoadSetup s)
    {
        var defs = s.Build(PathReference.FromPoints(Axis.Points, 0), new OutputSettings(), Cat, axis: Axis);
        return (defs, defs.OfType<RoadPavementDefinition>().Single());
    }

    /// <summary>Faixa transversal (y mín. e máx.) das peças na estaca x.</summary>
    private static (double Lo, double Hi) Span(IEnumerable<MarkingPiece> pieces, double x)
    {
        var cut = new[] { Polygon2.Rectangle(new Vec2(x - 0.05, -100), new Vec2(x + 0.05, 100)) };
        var ys = PolygonOps.Intersect(pieces.Select(p => p.Shape), cut).SelectMany(p => p.Outer).Select(v => v.Y).ToList();
        return (ys.Min(), ys.Max());
    }

    [Fact]
    public void Calcada_alargada_entre_A_e_B_com_transicoes()
    {
        var s = RoadTemplates.All[0].Create();
        var w0 = s.LarguraAtual(ElementoTrecho.CalcadaDireita)!.Value;
        var (nb, na, _) = s.Nominal(false);
        s.TrechosLargura.Add(new TrechoLargura
        {
            Elemento = ElementoTrecho.CalcadaDireita, EstacaA = 50, EstacaB = 90, NovaLargura = w0 + 1.5, TransicaoA = 10, TransicaoB = 20,
        });
        var (defs, pav) = Build(s);
        var (at, _) = pav.EdgeFunctions(Axis);
        // Em A e em B (e entre eles): a calçada com a nova largura – meio-fio no lugar, alinhamento 1,50 m mais para fora.
        foreach (var x in new[] { 50.0, 70.0, 90.0 })
        {
            var e = at(x);
            Assert.InRange(e.CurbR, -1e-6, 1e-6);
            Assert.InRange(e.LotR, 1.5 - 1e-6, 1.5 + 1e-6);
        }
        // Transições: metade no meio delas, nada antes do início e depois do fim.
        Assert.InRange(at(45).LotR, 0.75 - 0.01, 0.75 + 0.01);
        Assert.InRange(at(100).LotR, 0.75 - 0.01, 0.75 + 0.01);
        Assert.InRange(at(39).LotR, -1e-6, 1e-6);
        Assert.InRange(at(111).LotR, -1e-6, 1e-6);
        Assert.All(new[] { 0.0, 45.0, 70.0, 100.0, 140.0 }, x => Assert.InRange(at(x).LotL, -1e-6, 1e-6));
        // Geometria: a calçada direita vai do meio-fio ao alinhamento deslocado.
        var walk = defs.Where(d => d is LinearMarkingDefinition { Code: "CALCADA" or "GRAMADO" or "MEIO-FIO" } l && l.Offset < 0)
            .SelectMany(d => MarkingBuilder.Build(d, Axis, Ctx).Pieces).ToList();
        Assert.InRange(Span(walk, 70).Lo, -(na + 1.5) - 0.02, -(na + 1.5) + 0.02);
        Assert.InRange(Span(walk, 20).Lo, -na - 0.02, -na + 0.02);
        Assert.InRange(Span(walk, 70).Hi, -nb - 0.02, -nb + 0.02);
        Assert.InRange(Span(walk, 70).Hi - Span(walk, 70).Lo, w0 + 1.5 - 0.03, w0 + 1.5 + 0.03);
        // A seção guarda o trecho (a via é refeita a partir do eixo).
        Assert.Single(RoadTemplates.FromJson(pav.SetupJson)!.TrechosLargura);
        Assert.Single(s.Clone().TrechosLargura);
    }

    [Fact]
    public void Pista_alargada_entre_A_e_B()
    {
        var s = RoadTemplates.All[0].Create();
        var p0 = s.LarguraAtual(ElementoTrecho.Pista)!.Value;
        s.TrechosLargura.Add(new TrechoLargura { Elemento = ElementoTrecho.Pista, EstacaA = 40, EstacaB = 80, NovaLargura = p0 + 2, TransicaoA = 20, TransicaoB = 10 });
        var (defs, pav) = Build(s);
        var g = MarkingBuilder.Build(pav, Axis, Ctx);
        double W(double x) { var (lo, hi) = Span(g.Pieces, x); return hi - lo; }
        var w0 = W(10);
        Assert.InRange(W(40), w0 + 2 - 0.03, w0 + 2 + 0.03);
        Assert.InRange(W(80), w0 + 2 - 0.03, w0 + 2 + 0.03);
        Assert.InRange(W(30), w0 + 1 - 0.05, w0 + 1 + 0.05);
        Assert.InRange(W(85), w0 + 1 - 0.05, w0 + 1 + 0.05);
        Assert.InRange(W(130), w0 - 0.03, w0 + 0.03);
        // Metade para cada lado; as calçadas mantêm a largura.
        var (at, _) = pav.EdgeFunctions(Axis);
        Assert.InRange(at(60).CurbL, 1 - 1e-6, 1 + 1e-6);
        Assert.InRange(at(60).CurbR, 1 - 1e-6, 1 + 1e-6);
        Assert.InRange(at(60).LotR - at(60).CurbR, -1e-6, 1e-6);
    }

    [Fact]
    public void Canteiro_central_alargado_entre_A_e_B()
    {
        var s = RoadTemplates.All[2].Create();
        var m0 = s.LarguraAtual(ElementoTrecho.CanteiroCentral)!.Value;
        Assert.Contains(ElementoTrecho.CanteiroCentral, s.ElementosTrecho());
        s.TrechosLargura.Add(new TrechoLargura { Elemento = ElementoTrecho.CanteiroCentral, EstacaA = 60, EstacaB = 100, NovaLargura = m0 + 2, TransicaoA = 15, TransicaoB = 15 });
        var (defs, pav) = Build(s);
        var g = MarkingBuilder.Build(pav, Axis, Ctx);
        // Vão do canteiro no pavimento: largura nova entre A e B, a da seção longe do trecho.
        double Gap(double x)
        {
            var cut = new[] { Polygon2.Rectangle(new Vec2(x - 0.05, -0.6), new Vec2(x + 0.05, 0.6)) };
            Assert.Empty(PolygonOps.Intersect(g.Pieces.Select(p => p.Shape), cut));
            var ys = PolygonOps.Intersect(g.Pieces.Select(p => p.Shape), new[] { Polygon2.Rectangle(new Vec2(x - 0.05, -10), new Vec2(x + 0.05, 10)) })
                .SelectMany(p => p.Outer).Select(v => v.Y).ToList();
            return ys.Where(y => y > 0).Min() - ys.Where(y => y < 0).Max();
        }
        Assert.InRange(Gap(80), m0 + 2 - 0.05, m0 + 2 + 0.05);
        Assert.InRange(Gap(60), m0 + 2 - 0.05, m0 + 2 + 0.05);
        Assert.InRange(Gap(100), m0 + 2 - 0.05, m0 + 2 + 0.05);
        Assert.InRange(Gap(20), m0 - 0.05, m0 + 0.05);
        // A grama do canteiro acompanha.
        var grass = defs.OfType<LinearMarkingDefinition>().Single(l => l.Code == "GRAMADO" && Math.Abs(l.Offset) < 0.01);
        var gg = MarkingBuilder.Build(grass, Axis, Ctx).Pieces;
        var (lo, hi) = Span(gg, 80);
        Assert.InRange(hi - lo, grass.WidthOverride!.Value + 2 - 0.05, grass.WidthOverride.Value + 2 + 0.05);
    }
}
