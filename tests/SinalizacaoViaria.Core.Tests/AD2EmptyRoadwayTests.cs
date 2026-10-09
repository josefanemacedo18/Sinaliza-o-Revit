using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AD (item 14): pista vazia – a ferramenta Pista no modo "só pavimento" gera apenas a superfície (largura fixa ou
/// variável), sem pintura, meio-fio ou calçada; os elementos entram depois pela edição da seção (ou Sinalizar via).
/// </summary>
public class AD2EmptyRoadwayTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static bool IsPavement(MarkingPiece p) => p.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto or MarkingColor.PavimentoTerra;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pista_vazia_so_tem_o_pavimento(bool variable)
    {
        var s = RoadSetup.PistaVazia(3.5, 3.5, true, TipoPavimento.Asfalto, HierarquiaViaria.Local);
        if (variable)
        {
            s.LargurasVariaveis.Add(new PontoLargura { Estaca = 0, BordoEsquerdo = 3.5, BordoDireito = 3.5 });
            s.LargurasVariaveis.Add(new PontoLargura { Estaca = 60, BordoEsquerdo = 5.0, BordoDireito = 3.5 });
        }
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(100, 0) });
        var defs = s.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, axis: axis);
        var pav = Assert.Single(defs);
        var road = Assert.IsType<RoadPavementDefinition>(pav);
        Assert.NotNull(road.SetupJson);
        Assert.Equal(variable, road.HasEdgeVariation);
        var g = MarkingBuilder.Build(road, axis, Ctx);
        Assert.NotEmpty(g.Pieces);
        Assert.All(g.Pieces, p => Assert.True(IsPavement(p), $"peça {p.Color} {p.Layer}"));
        if (variable)
        {
            // Mais larga no fim (esquerda 5,0 m) que no início (3,5 m).
            var ys = g.Pieces.SelectMany(p => p.Shape.Outer).ToList();
            Assert.InRange(ys.Where(v => v.X > 99).Max(v => v.Y), 4.9, 5.1);
            Assert.InRange(ys.Where(v => v.X < 1).Max(v => v.Y), 3.4, 3.6);
        }
    }

    [Fact]
    public void Cruzamento_de_pistas_vazias_sem_pintura_meio_fio_ou_calcada()
    {
        var w = new Y34TipTests.World();
        foreach (var pts in new[] { new[] { new Vec2(-60, 0), new Vec2(60, 0) }, new[] { new Vec2(0, -60), new Vec2(0, 60) } })
        {
            var pr = PathReference.FromPoints(pts, 0);
            var axis = new Polyline2(pr.Points);
            var g = RoadSetup.PistaVazia(3.5, 3.5, true, TipoPavimento.Asfalto, HierarquiaViaria.Local).Build(pr, new OutputSettings(), Cat, axis: axis);
            foreach (var m in g) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            var d = new IntersectionDefinition { Node = node }.SemSinalizacao();
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, Cat));
        }
        Assert.Single(w.Nodes);
        var parts = Y34TipTests.Geometry(w);
        Assert.NotEmpty(parts);
        var other = parts.Where(p => !IsPavement(p.Piece)).ToList();
        Assert.True(other.Count == 0, string.Join(" | ", other.Select(o => $"{o.Def.DisplayCode}:{o.Piece.Color}:{o.Piece.Layer}").Distinct()));
        Assert.DoesNotContain(w.Defs, d => d is SignDefinition or LinearMarkingDefinition or TextMarkingDefinition or SymbolMarkingDefinition or RampDefinition);
    }

    [Fact]
    public void Pista_vazia_recebe_os_elementos_pela_edicao_da_secao()
    {
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(100, 0) });
        var path = PathReference.FromPoints(axis.Points, 0);
        var empty = RoadSetup.PistaVazia(3.5, 3.5, true, TipoPavimento.Asfalto, HierarquiaViaria.Local).Build(path, new OutputSettings(), Cat, axis: axis);
        var pav = (RoadPavementDefinition)empty.Single();
        // Edição da seção: a seção guardada abre com as faixas da pista; desliga-se o "só pavimento" e entram as calçadas.
        var s = RoadTemplates.FromJson(pav.SetupJson)!;
        Assert.True(s.SoPavimento);
        Assert.Equal(1, s.Right.Count(e => e.Tipo == TipoElementoSecao.FaixaRolamento));
        s.SoPavimento = false;
        s.Right.Add(new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = 3 });
        s.Left.Add(new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = 3 });
        var full = s.Build(path, new OutputSettings(), Cat, pav.GroupId, pav.Id, axis);
        var pav2 = full.OfType<RoadPavementDefinition>().Single();
        Assert.Equal(pav.Id, pav2.Id);
        Assert.Contains(full, d => d is LinearMarkingDefinition { Code: "LFO-2" });
        Assert.Contains(full, d => d is LinearMarkingDefinition { Code: "MEIO-FIO" });
        Assert.Contains(full, d => d is LinearMarkingDefinition { Code: "CALCADA" });
    }
}
