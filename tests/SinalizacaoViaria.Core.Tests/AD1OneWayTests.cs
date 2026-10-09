using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AD (item 13): via de mão única – nunca amarelo; 1 faixa: só os bordos e as setas; 2 ou mais: divisórias brancas de
/// mesmo sentido (LMS) entre as faixas, bordos e setas.
/// </summary>
public class AD1OneWayTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static Polyline2 Straight(double len) => new(new[] { new Vec2(0, 0), new Vec2(len, 0) });
    private static ElementoSecao Lane() => new() { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 };
    private static ElementoSecao Walk() => new() { Tipo = TipoElementoSecao.Calcada, Largura = 3.0 };

    /// <summary>Mão única (como a janela da via cria, setas ligadas): 1 faixa à direita do eixo (calçada logo à esquerda) ou 1 de cada lado.</summary>
    public static RoadSetup OneWay(int lanes, bool gutterAdds)
    {
        var s = new RoadSetup { TwoWay = false, SarjetaSomada = gutterAdds, Speed = 40, Hierarchy = HierarquiaViaria.Local, SetasSentido = true };
        s.Right.Add(Lane());
        if (lanes >= 2) s.Left.Add(Lane());
        s.Right.Add(Walk());
        s.Left.Add(Walk());
        return s;
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public void Mao_unica_sem_amarelo_com_bordos_divisorias_e_setas(int lanes, bool gutterAdds)
    {
        var s = OneWay(lanes, gutterAdds);
        var axis = Straight(120);
        var defs = s.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, axis: axis);
        var pieces = defs.SelectMany(d => MarkingBuilder.Build(d, axis, Ctx).Pieces).ToList();
        Assert.DoesNotContain(pieces, p => p.Color == MarkingColor.Amarela);
        var lines = defs.OfType<LinearMarkingDefinition>().ToList();
        Assert.DoesNotContain(lines, l => l.Code.StartsWith("LFO"));
        // Bordos: um em cada borda da pista, dentro das faixas.
        var lo = -3.5;
        var hi = lanes >= 2 ? 3.5 : 0.0;
        var lbo = lines.Where(l => l.Code == "LBO").Select(l => l.Offset).OrderBy(o => o).ToList();
        Assert.True(lbo.Count == 2, $"bordos em {string.Join("; ", lbo)}");
        Assert.InRange(lbo[0], lo, lo + 0.6);
        Assert.InRange(lbo[1], hi - 0.6, hi);
        // Divisórias brancas de mesmo sentido só entre faixas.
        var dividers = lines.Where(l => l.Code.StartsWith("LMS")).ToList();
        if (lanes == 1) Assert.Empty(dividers);
        else Assert.Single(dividers, l => Math.Abs(l.Offset) < 1e-6);
        // Setas de sentido (no sentido do eixo) no meio de cada faixa.
        var arrows = defs.OfType<RepeatedMarkingDefinition>().Where(r => r.SymbolCode == "PEM-F").ToList();
        Assert.Equal(lanes, arrows.Count);
        Assert.All(arrows, a => Assert.False(a.Reverse));
        Assert.Contains(arrows, a => Math.Abs(a.Offset + 1.75) < 1e-6);
        if (lanes >= 2) Assert.Contains(arrows, a => Math.Abs(a.Offset - 1.75) < 1e-6);
        Assert.Contains(pieces, p => p.Color == MarkingColor.Branca && p.Layer == null && p.Shape.Area > 0.5 && p.Shape.Centroid.Y < 0 && p.Shape.Centroid.Y > -3.5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Mao_unica_cruzando_via_local_sem_amarelo(int lanes)
    {
        var w = new Y34TipTests.World();
        Func<RoadSetup>[] setups = { () => OneWay(lanes, true), () => RoadTemplates.All[0].Create() };
        var axes = new[] { new[] { new Vec2(-80, 0), new Vec2(80, 0) }, new[] { new Vec2(0, -80), new Vec2(0, 80) } };
        for (int i = 0; i < 2; i++)
        {
            var pr = PathReference.FromPoints(axes[i], 0);
            var axis = new Polyline2(pr.Points);
            var g = setups[i]().Build(pr, new OutputSettings(), Cat, axis: axis);
            foreach (var m in g) if (m.Path is { } p && p.Points.Count == pr.Points.Count && p.Points[0].DistanceTo(pr.Points[0]) < 1e-9) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            var d = new IntersectionDefinition { Control = ControleIntersecao.Pare, Node = node };
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, Cat));
        }
        var parts = Y34TipTests.Geometry(w);
        var oneWayIds = w.Groups[0].Select(d => d.Id).ToHashSet();
        // Nada amarelo na via de mão única nem na pintura que a interseção faz sobre ela (fora do cruzamento).
        var bad = parts.Where(p => p.Piece.Color == MarkingColor.Amarela && p.Def is not RampDefinition && p.Def is not TactileRouteDefinition
                                   && (oneWayIds.Contains(p.Def.Id) || Math.Abs(p.Piece.Shape.Centroid.Y) < 4 && Math.Abs(p.Piece.Shape.Centroid.X) > 14)).ToList();
        Assert.True(bad.Count == 0, string.Join(" | ", bad.Select(b => $"{b.Def.DisplayCode}@{b.Piece.Shape.Centroid}")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Obra_de_mao_unica_sem_eixo_amarelo(int lanes)
    {
        foreach (MarkingDefinition obra in new MarkingDefinition[]
                 {
                     new TunnelDefinition { Lanes = lanes, TwoWay = false }, new TrenchDefinition { Lanes = lanes, TwoWay = false },
                     new BridgeDefinition { Lanes = lanes, TwoWay = false },
                 })
        {
            var s = obra switch
            {
                TunnelDefinition t => InfraRoads.Setup(t),
                TrenchDefinition t => InfraRoads.Setup(t),
                BridgeDefinition b => InfraRoads.Setup(b),
                _ => throw new InvalidOperationException(),
            };
            Assert.False(s.TwoWay);
            var axis = Straight(200);
            var defs = s.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, axis: axis);
            Assert.DoesNotContain(defs.SelectMany(d => MarkingBuilder.Build(d, axis, Ctx).Pieces), p => p.Color == MarkingColor.Amarela);
            // Uma faixa centrada no eixo é uma faixa só: sem divisória no meio dela.
            var dividers = defs.OfType<LinearMarkingDefinition>().Count(l => l.Code.StartsWith("LMS"));
            Assert.Equal(lanes - 1, dividers);
            // Pintura própria da obra (sem via hospedeira): também sem amarelo.
            var g = MarkingBuilder.Build(obra, axis, Ctx);
            Assert.DoesNotContain(g.Pieces, p => p.Color == MarkingColor.Amarela && p.Layer == "PINTURA");
        }
    }
}
