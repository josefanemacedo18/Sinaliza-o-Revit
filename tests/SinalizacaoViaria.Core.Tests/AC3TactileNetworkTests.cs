using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AC-3 (item 11): piso tátil nas calçadas – faixa direcional na faixa livre ligada a cada rampa por um ramal, com
/// alerta no topo das rampas, nas junções e nas mudanças de direção; rede conexa, sem sobrepor rampa, meio-fio ou mobiliário.
/// </summary>
public class AC3TactileNetworkTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    /// <summary>
    /// Verificações da rede tátil num disco em volta de <paramref name="center"/>; lista vazia = sem falhas. Cada calçada (quarteirão)
    /// tem uma rede conexa: <paramref name="blocks"/> partes no disco, sem contar as ilhas do canteiro central.
    /// </summary>
    public static List<string> Check(Y34TipTests.World w, Vec2 center, double R, int blocks = 1)
    {
        var res = new List<string>();
        var parts = Y34TipTests.Geometry(w);
        var disk = new[] { new Polygon2(CurveTools.Circle(center, R, 0.02)) };
        var tiles = parts.Where(p => p.Def is TactileRouteDefinition && p.Piece.Color == MarkingColor.Amarela).Select(p => p.Piece.Shape).ToList();
        if (tiles.Count == 0) return new List<string> { "sem piso tátil" };
        var net = PolygonOps.Intersect(PolygonOps.Union(PolygonOps.Offset(tiles, 0.03)), disk);
        var islands = w.Nodes.SelectMany(n => n.Layout.Obstacles).ToList();
        var comps = net.Where(p => p.Area > 0.05 && !islands.Any(o => o.Contains(p.Centroid))).ToList();
        if (comps.Count != blocks) res.Add($"rede tátil em {comps.Count} partes: " + string.Join(" ", comps.Select(c => $"{c.Centroid}/{c.Area:0.0}")));
        // Cada rampa (das calçadas) ligada à rede: o alerta do topo encosta nela.
        var ramps = w.Defs.OfType<RampDefinition>().Where(r => RoadFeatures.RampTop(r) is { } t && disk[0].Contains((t.A + t.B) / 2)).ToList();
        foreach (var r in ramps)
        {
            var (a, b, dir) = RoadFeatures.RampTop(r)!.Value;
            var probe = (a + b) / 2 + dir * (RoadFeatures.RampTopAlert / 2);
            if (islands.Any(o => o.Contains((a + b) / 2))) continue;   // rampa do canteiro central
            if (!net.Any(p => p.Contains(probe))) res.Add($"rampa em {r.PathRef.Points[0]} sem ligação com a rede tátil");
        }
        // Nada sobre o corpo das rampas, o meio-fio ou o mobiliário.
        double Over(IEnumerable<Polygon2> b) => PolygonOps.TotalArea(PolygonOps.Intersect(PolygonOps.Intersect(tiles, disk), b));
        var bodies = w.Defs.OfType<RampDefinition>().SelectMany(RoadFeatures.RampBody).ToList();
        if (Over(bodies) > 1e-3) res.Add($"piso tátil sobre rampa ({Over(bodies):0.000} m²)");
        var curbs = parts.Where(p => p.Piece.Layer?.StartsWith("MEIO-FIO") == true && p.Def is not RampDefinition).Select(p => p.Piece.Shape).ToList();
        if (Over(curbs) > 1e-3) res.Add($"piso tátil sobre meio-fio ({Over(curbs):0.000} m²)");
        // A rota até a rampa é pavimentada: nada de piso tátil sobre a faixa de serviço gramada.
        var grass = parts.Where(p => p.Piece.Color == MarkingColor.Grama && p.Def is not UrbanElementDefinition).Select(p => p.Piece.Shape).ToList();
        if (Over(grass) > 1e-3) res.Add($"piso tátil sobre grama ({Over(grass):0.000} m²)");
        var furniture = parts.Where(p => p.Def is UrbanElementDefinition).Select(p => p.Piece.Shape).ToList();
        if (Over(furniture) > 1e-3) res.Add($"piso tátil sob mobiliário ({Over(furniture):0.000} m²)");
        // Alerta (domos) em cada junção e mudança de direção.
        var domes = parts.Where(p => p.Def is TactileRouteDefinition && p.Piece.Color == MarkingColor.RelevoTatil && p.Piece.Shape.Outer.Count == 6)
            .Select(p => p.Piece.Shape.Centroid).ToList();
        bool Alert(Vec2 q) => domes.Any(d => d.DistanceTo(q) < 0.45);
        foreach (var t in w.Defs.OfType<TactileRouteDefinition>().Where(t => !t.AlertOnly))
        {
            var main = new Polyline2(t.PathRef.Points);
            foreach (var br in t.Branches ?? new())
            {
                var j = new[] { br[0], br[^1] }.OrderBy(q => main.Project(q).Signed is var sd ? Math.Abs(sd) : 0).First();
                if (disk[0].Contains(j) && !Alert(j)) res.Add($"junção sem alerta em {j}");
            }
            var pts = main.Points;
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                var u = (pts[i] - pts[i - 1]).Normalized();
                var v = (pts[i + 1] - pts[i]).Normalized();
                var turn = Math.Acos(Math.Clamp(u.Dot(v), -1, 1)) * 180 / Math.PI;
                if (turn >= 20 && disk[0].Contains(pts[i]) && !Alert(pts[i])) res.Add($"mudança de direção ({turn:0}°) sem alerta em {pts[i]}");
            }
        }
        return res;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Via_com_extensao_e_travessia_tem_rede_tatil_ligada_as_rampas(bool curved)
    {
        var (w, _, defs, axis) = AC3SidewalkExtensionTests.Road(curved, 0, s =>
        {
            s.PisoTatil = true;
            s.ExtensoesCalcada.Add(new ExtensaoCalcada { LadoEsquerdo = false, Estaca = 40, Comprimento = 20, Travessia = true, Bancos = 1, Paraciclos = 2, Arvores = 2, Canteiro = true });
        });
        Assert.Equal(2, defs.OfType<RampDefinition>().Count());
        // Uma faixa direcional por calçada + o alerta no topo de cada rampa.
        Assert.Equal(2, defs.OfType<TactileRouteDefinition>().Count(t => !t.AlertOnly));
        Assert.Equal(2, defs.OfType<TactileRouteDefinition>().Count(t => t.AlertOnly));
        Assert.All(defs.OfType<TactileRouteDefinition>().Where(t => !t.AlertOnly), t => Assert.Single(t.Branches!));
        // Rede ligada às rampas dos dois lados: cada calçada é uma rede conexa (a travessia liga as duas).
        foreach (var side in new[] { 1, -1 })
        {
            var center = AC3SidewalkExtensionTests_Point(axis, 50, side * 7.5);
            var faults = Check(w, center, 6);
            Assert.True(faults.Count == 0, string.Join(" | ", faults));
        }
        var all = Y34TipTests.FaultsAt(Y34TipTests.Geometry(w), axis.PointAt(50), 16, true);
        Assert.True(all.Count == 0, string.Join(" | ", all));
    }

    private static Vec2 AC3SidewalkExtensionTests_Point(Polyline2 axis, double s, double o) => RoadFeatures.PointAt(axis, s, o);

    [Fact]
    public void Piso_tatil_acompanha_o_eixo()
    {
        // A mesma via em dois eixos: a rede é refeita do eixo (nada fica na posição antiga).
        var (_, _, d0, a0) = AC3SidewalkExtensionTests.Road(false, 0, s => s.PisoTatil = true);
        var (_, _, d1, a1) = AC3SidewalkExtensionTests.Road(true, 0, s => s.PisoTatil = true);
        foreach (var (defs, axis) in new[] { (d0, a0), (d1, a1) })
        {
            var lines = defs.OfType<TactileRouteDefinition>().ToList();
            Assert.Equal(2, lines.Count);
            var s = RoadTemplates.All[0].Create();
            foreach (var t in lines)
            {
                var side = axis.Project(t.PathRef.Points[0]).Signed > 0;
                var lado = s.Lado(side);
                var o = lado.Face + lado.FreeCenter;
                Assert.All(t.PathRef.Points, p => Assert.InRange(Math.Abs(axis.Project(p).Signed), o - 0.02, o + 0.02));
            }
        }
    }

    /// <summary>T de duas avenidas (modelo 2, com canteiro central) com piso tátil nas calçadas.</summary>
    public static Y34TipTests.World TactileT()
    {
        var w = new Y34TipTests.World();
        foreach (var pts in new[] { new[] { new Vec2(-80, 0), new Vec2(80, 0) }, new[] { Vec2.Zero, new Vec2(0, 80) } })
        {
            var s = RoadTemplates.All[2].Create();
            s.PisoTatil = true;
            var pr = PathReference.FromPoints(pts, 0);
            var axis = new Polyline2(pr.Points);
            var g = s.Build(pr, new OutputSettings(), Cat, axis: axis);
            foreach (var m in g)
                if (m.Path is { } p && p.Points.Count == pr.Points.Count && p.Points[0].DistanceTo(pr.Points[0]) < 1e-9) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            if (!IntersectionGenerator.NeedsIntersection(rs, node)) continue;
            var d = new IntersectionDefinition { Control = ControleIntersecao.Pare, Node = node };
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, Cat));
        }
        return w;
    }

    [Fact]
    public void T_com_piso_tatil_liga_a_rede_das_vias_a_todas_as_rampas()
    {
        var w = TactileT();
        var L = w.Nodes.Single().Layout;
        var it = L.Definition!;
        Assert.True(IntersectionGenerator.TactileNetworkOn(L));
        var sideRamps = w.Defs.OfType<RampDefinition>().Where(r => r.GroupId == it.Id && !L.Obstacles.Any(o => o.Contains(RoadFeatures.RampTop(r)!.Value.A))).ToList();
        Assert.Equal(6, sideRamps.Count);
        // Três calçadas: as duas esquinas e o lado contínuo.
        var faults = Check(w, L.Node, 30, 3);
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
        Assert.Empty(Y34TipTests.Faults(w));
    }
}
