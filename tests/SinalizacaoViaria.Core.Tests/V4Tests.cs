using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

public class V4Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static (List<MarkingDefinition> Defs, RoadPavementDefinition Pav, Polyline2 Axis) Road(int template, params Vec2[] axis)
    {
        var defs = RoadTemplates.All[template].Create().Build(new PathReference(), new OutputSettings(), Cat);
        return (defs, defs.OfType<RoadPavementDefinition>().Single(), new Polyline2(axis));
    }

    [Fact]
    public void RoadSetup_CreatesPavementWithSection()
    {
        var (defs, pav, axis) = Road(2, Vec2.Zero, new Vec2(50, 0));   // avenida com canteiro central
        Assert.Same(defs[0], pav);
        Assert.True(pav.RightWidth > 5 && pav.LeftWidth > 5);
        Assert.True(pav.RightSidewalk > 1 && pav.LeftSidewalk > 1);
        Assert.Contains(pav.Gaps, g => g.Median && Math.Abs(g.Offset) < 1e-6);
        var geo = MarkingBuilder.Build(pav, axis, Ctx);
        Assert.All(geo.Pieces, p => { Assert.Equal(MarkingColor.Asfalto, p.Color); Assert.Equal(-0.05, p.Elevation, 6); });
        // Pavimento sem o canteiro central.
        Assert.DoesNotContain(geo.Pieces, p => p.Shape.Contains(new Vec2(25, 0)));
        Assert.Contains(geo.Pieces, p => p.Shape.Contains(new Vec2(25, pav.LeftWidth - 1)));

        var s = RoadTemplates.All[0].Create();
        s.Pavement = TipoPavimento.Bloquete;
        var blq = s.Build(new PathReference(), new OutputSettings(), Cat).OfType<RoadPavementDefinition>().Single();
        Assert.Equal(0.08, blq.ActualThickness, 6);
        Assert.All(MarkingBuilder.Build(blq, axis, Ctx).Pieces, p => Assert.Equal(MarkingColor.Bloquete, p.Color));
        s.Pavement = TipoPavimento.Nenhum;
        Assert.Empty(s.Build(new PathReference(), new OutputSettings(), Cat).OfType<RoadPavementDefinition>());
    }

    [Fact]
    public void Intersection_FourWayAndTee()
    {
        var a = Road(0, new Vec2(-60, 0), new Vec2(60, 0));
        var b = Road(1, new Vec2(0, -60), new Vec2(0, 60));
        var t = Road(0, new Vec2(40, 60), new Vec2(40, 6));   // termina junto ao bordo de "a"
        var roads = new[] { a, b, t }.Select(r => new IntersectionRoad(r.Pav, r.Axis)).ToList();
        var nodes = IntersectionGenerator.FindNodes(roads);
        Assert.Equal(2, nodes.Count);
        var cross = nodes.Single(n => n.Node.Length < 1);
        var tee = nodes.Single(n => n.Node.DistanceTo(new Vec2(40, 0)) < 1);

        var it = new IntersectionDefinition { Node = cross.Node, CornerRadius = 6, Control = ControleIntersecao.Semaforo };
        var L = IntersectionGenerator.Layout(it, cross.Roads.Select(i => roads[i]).ToList());
        Assert.Equal(4, L.Legs.Count);
        Assert.NotEmpty(L.Curb);
        Assert.NotEmpty(L.Sidewalk);
        // Esquina arredondada: o canto do cruzamento dos bordos é pista (pavimento), não calçada.
        var ra = a.Pav.RightWidth;
        var rb = b.Pav.LeftWidth;
        Assert.Contains(L.Pavement, p => p.Contains(new Vec2(rb + 0.5, -ra - 0.5)));
        var children = IntersectionGenerator.Children(it, L, new OutputSettings(), 0);
        Assert.Equal(4, children.Count(c => c is LinearMarkingDefinition { Code: "FTP-1" }));
        Assert.Equal(4, children.Count(c => c is LinearMarkingDefinition { Code: "LRE" }));
        Assert.Equal(8, children.Count(c => c is RampDefinition));
        // Recortes: pintura até depois da retenção; calçada e pavimento pela zona do nó.
        var lane = a.Defs.OfType<LinearMarkingDefinition>().First(d => d.Code.StartsWith("LFO"));
        Assert.NotEmpty(IntersectionGenerator.CutsFor(lane, 0, L));
        Assert.Single(IntersectionGenerator.CutsFor(a.Pav, 0, L));
        var walk = a.Defs.OfType<LinearMarkingDefinition>().First(d => d.Code == "CALCADA");
        Assert.Single(IntersectionGenerator.CutsFor(walk, 0, L));

        var Lt = IntersectionGenerator.Layout(new IntersectionDefinition { Node = tee.Node }, tee.Roads.Select(i => roads[i]).ToList());
        Assert.Equal(3, Lt.Legs.Count);
    }

    [Fact]
    public void Intersection_BuildsPavementCurbAndSidewalk()
    {
        var a = Road(0, new Vec2(-60, 0), new Vec2(60, 0));
        var b = Road(0, new Vec2(0, -60), new Vec2(0, 60));
        var all = new List<MarkingDefinition> { a.Pav, b.Pav };
        var paths = new Dictionary<string, Polyline2> { [a.Pav.Id] = a.Axis, [b.Pav.Id] = b.Axis };
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => all.FirstOrDefault(d => d.Id == id), PathOf = d => paths.GetValueOrDefault(d.Id) };
        var it = new IntersectionDefinition { Node = Vec2.Zero, RoadIds = { a.Pav.Id, b.Pav.Id } };
        var geo = MarkingBuilder.Build(it, null, ctx);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Asfalto);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Concreto && p.Thickness > 0.1);
        Assert.Equal(GrupoMarca.Urbanizacao, MarkingBuilder.Describe(it, Cat).Group);
        Assert.Equal(CategoriaQuantitativo.PavimentacaoGeometria, QuantityRow.Categorize(it, GrupoMarca.Urbanizacao));
    }

    [Fact]
    public void Roundabout_LegsFromRoads_AndSignage()
    {
        var a = Road(1, new Vec2(-80, 0), new Vec2(80, 0));
        var b = Road(0, new Vec2(0, -80), new Vec2(0, 80));
        var roads = new[] { a, b }.Select(r => new IntersectionRoad(r.Pav, r.Axis)).ToList();
        var rb = new RoundaboutDefinition { IslandRadius = 9 };
        rb.Legs.AddRange(RoundaboutGenerator.LegsFromRoads(Vec2.Zero, roads, rb.OuterRadius + 25));
        Assert.Equal(4, rb.Legs.Count);
        var L = RoundaboutGenerator.Layout(rb);
        Assert.Equal(4, L.Legs.Count(l => l.Splitter != null));
        Assert.NotEmpty(L.IslandCore);
        Assert.DoesNotContain(L.Pavement, p => p.Contains(Vec2.Zero));
        Assert.Contains(L.Pavement, p => p.Contains(new Vec2(rb.IslandRadius + rb.ApronWidth + 2, 0.5)));
        var ch = RoundaboutGenerator.Children(rb, L, new OutputSettings(), 0);
        Assert.Equal(4, ch.Count(c => c is LinearMarkingDefinition { Code: "LDP" }));
        Assert.Equal(4, ch.Count(c => c is SymbolMarkingDefinition { Code: "SDP" }));
        Assert.Equal(4, ch.Count(c => c is SignDefinition { Code: "R-2" }));
        Assert.Equal(4, ch.Count(c => c is SignDefinition { Code: "R-33" }));
        // Dê a preferência na entrada: à esquerda do ramo (quem chega circula pela direita).
        var east = L.Legs.Single(l => Math.Abs(l.Leg.AngleDeg) < 1);
        var ldp = ch.OfType<LinearMarkingDefinition>().First(c => c.Code == "LDP" && c.PathRef.Points.All(p => p.X > 0));
        Assert.All(ldp.PathRef.Points, p => Assert.True(p.Y > 0));
        var geo = MarkingBuilder.Build(rb, null, Ctx);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Folhagem);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Bloquete);   // faixa galgável
        _ = east;
    }

    [Fact]
    public void TactileRoute_AlertsAtTurnsEndsAndJunctions()
    {
        var main = new Polyline2(new[] { new Vec2(0, 0), new Vec2(6, 0), new Vec2(6, 4) });
        var branch = new Polyline2(new[] { new Vec2(3, -3), new Vec2(3, 0) });
        var d = new TactileRouteDefinition { Module = 0.25, Rows = 1 };
        var geo = TactileGenerator.Route(d, new[] { main, branch }, 0.15);
        var tiles = geo.Pieces.Where(p => p.Color == MarkingColor.Amarela).ToList();
        var relief = geo.Pieces.Where(p => p.Color == MarkingColor.RelevoTatil).ToList();
        Assert.All(tiles, p => Assert.Equal(0.15, p.Elevation, 6));
        Assert.All(relief, p => Assert.True(p.Elevation > 0.15));
        // Domos (hexágonos pequenos) na curva (6,0), na junção (3,0) e nas extremidades livres.
        bool DomesNear(Vec2 c) => relief.Any(r => r.Shape.Outer.Count == 6 && r.Shape.Centroid.DistanceTo(c) < 0.2);
        Assert.True(DomesNear(new Vec2(6, 0)));
        Assert.True(DomesNear(new Vec2(3, 0)));
        Assert.True(DomesNear(new Vec2(0.125, 0)));
        Assert.True(DomesNear(new Vec2(3, -2.875)));
        // Barras direcionais no meio dos trechos, paralelas ao deslocamento.
        var bar = relief.First(r => r.Shape.Outer.Count == 4 && r.Shape.Centroid.DistanceTo(new Vec2(4.6, 0)) < 0.2);
        var (mn, mx) = bar.Shape.Bounds;
        Assert.True(mx.X - mn.X > mx.Y - mn.Y);

        var lin = MarkingBuilder.Build(new LinearMarkingDefinition { Code = "PTA", Variant = "Faixa 0,40 m" }, new Polyline2(new[] { Vec2.Zero, new Vec2(2, 0) }), Ctx);
        Assert.Equal(5, lin.Pieces.Count(p => p.Color == MarkingColor.Amarela));
        Assert.Equal((0.4, 1), TactileGenerator.ForWidth(0.40));
        Assert.Equal((0.30, 2), TactileGenerator.ForWidth(0.60));
    }

    [Fact]
    public void CurbExtension_WrapsCorner()
    {
        var pts = new List<Vec2> { new(-16, 0), new(-6, 0) };
        pts.AddRange(CurveTools.Arc(new Vec2(-6, 6), 6, -Math.PI / 2, Math.PI / 2, 0.02).Skip(1));
        pts.Add(new Vec2(0, 16));
        var path = new Polyline2(pts);
        var d = new CurbExtensionDefinition { Depth = 2.4 };
        var fp = SidewalkGenerator.EarFootprint(d, path)!;
        // Ponto da orelha do lado de fora da esquina: raio 6 + avanço/2 a partir do centro da curva.
        var mid = new Vec2(-6, 6) + new Vec2(1, -1).Normalized() * (6 + 1.2);
        Assert.True(fp.Contains(mid));
        Assert.False(fp.Contains(new Vec2(-6, 6) + new Vec2(1, -1).Normalized() * 5));   // dentro da calçada existente
        var geo = MarkingBuilder.Build(d, path, Ctx);
        Assert.Empty(geo.Warnings);
        Assert.True(geo.TotalArea > 0.8 * d.Depth * path.Length);
    }

    [Fact]
    public void Furniture_AllTypesBuildSolidsAndSitOnSidewalk()
    {
        foreach (var m in Cat.Mobiliario)
        {
            var g = UrbanGenerator.BuildAt(m, new LocalFrame(Vec2.Zero, Vec2.UnitY), null, null, null, null);
            Assert.True(g.Pieces.Count >= 2, m.Codigo);
            Assert.All(g.Pieces.Where(p => p.Solid != null), p => Assert.True(p.Solid!.Faces.Count >= 4, m.Codigo));
            var top = g.Pieces.Max(p => p.Solid != null ? p.Solid.MaxZ : p.Elevation + p.Thickness);
            Assert.True(top > m.Altura * 0.6 && top < m.Altura * 1.6 + 0.5, $"{m.Codigo}: {top}");
        }
        var tree = MarkingBuilder.Build(new UrbanElementDefinition { Code = "ARVORE" }, null, Ctx);
        Assert.Contains(tree.Pieces, p => p.Color == MarkingColor.Folhagem);
        Assert.True(tree.Pieces.Min(p => p.Solid != null ? p.Solid.MinZ + p.Elevation : p.Elevation) >= 0.15 - 1e-6);
        var sign = MarkingBuilder.Build(new SignDefinition { Code = "R-1", BaseElevation = 0 }, null, Ctx);
        Assert.Contains(sign.Pieces, p => p.Color == MarkingColor.Metal && p.Elevation == 0);
    }
}
