using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AD (item 18): estacionamento só delimitado – só a linha tracejada que delimita a faixa de estacionamento, sem
/// divisórias entre vagas; traço, espaço e largura configuráveis.
/// </summary>
public class AD4ParkingLineTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static RoadSetup Local(bool onlyLine)
    {
        var s = RoadTemplates.All[0].Create();
        foreach (var e in s.Right.Concat(s.Left).Where(e => e.Tipo == TipoElementoSecao.Estacionamento))
        {
            e.SoDelimitado = onlyLine;
            e.TracoLinha = 0.5;
            e.EspacoLinha = 0.5;
            e.LarguraDelimitacao = 0.12;
        }
        return s;
    }

    [Fact]
    public void Estacionamento_so_com_a_linha_delimitadora()
    {
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(60, 0) });
        var s = Local(true);
        Assert.Contains(s.Right, e => e.Tipo == TipoElementoSecao.Estacionamento);
        var defs = s.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, axis: axis);
        Assert.Empty(defs.OfType<ParkingMarkingDefinition>());
        Assert.NotNull(Cat.Linear(RoadSetup.ParkingLineCode));
        var lines = defs.OfType<LinearMarkingDefinition>().Where(l => l.Code == RoadSetup.ParkingLineCode).ToList();
        Assert.Equal(2, lines.Count);
        foreach (var (side, sigma) in new[] { (s.Right, -1), (s.Left, 1) })
        {
            // Na divisa com a pista: borda interna da faixa de estacionamento (+ meia largura da linha).
            var inner = side.TakeWhile(e => e.Tipo != TipoElementoSecao.Estacionamento).Sum(e => e.Largura);
            var line = Assert.Single(lines, l => Math.Sign(l.Offset) == sigma);
            Assert.InRange(Math.Abs(line.Offset), inner + 0.06 - 1e-6, inner + 0.06 + 1e-6);
            Assert.Equal(0.12, line.WidthOverride!.Value, 6);
            Assert.Equal(new[] { 0.5, 0.5 }, line.PatternOverride);
            // Só traços ao longo da linha (0,50 m × 0,12 m), nenhuma divisória atravessando a faixa.
            var g = MarkingBuilder.Build(line, axis, Ctx);
            Assert.True(g.Pieces.Count > 30);
            Assert.All(g.Pieces, p =>
            {
                Assert.Equal(MarkingColor.Branca, p.Color);
                var ys = p.Shape.Outer.Select(v => v.Y).ToList();
                var xs = p.Shape.Outer.Select(v => v.X).ToList();
                Assert.InRange(ys.Max() - ys.Min(), 0.11, 0.13);
                Assert.InRange(xs.Max() - xs.Min(), 0.0, 0.51);
            });
        }
        // Sem a opção: vagas demarcadas como antes.
        var before = Local(false).Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, axis: axis);
        Assert.Equal(2, before.OfType<ParkingMarkingDefinition>().Count());
        Assert.DoesNotContain(before, d => d is LinearMarkingDefinition { Code: RoadSetup.ParkingLineCode });
    }

    [Fact]
    public void Linha_do_estacionamento_para_antes_da_travessia()
    {
        var w = Y34TipTests.Make(new IntersectionDefinition { Control = ControleIntersecao.Pare }, (d, rs) => { },
            (0, new[] { new Vec2(-80, 0), new Vec2(80, 0) }), (0, new[] { new Vec2(0, -80), new Vec2(0, 80) }));
        // Refaz a via horizontal só delimitada e reaplica a interseção como o plugin faz.
        var w2 = new Y34TipTests.World();
        var setups = new Func<RoadSetup>[] { () => Local(true), () => RoadTemplates.All[0].Create() };
        var axes = new[] { new[] { new Vec2(-80, 0), new Vec2(80, 0) }, new[] { new Vec2(0, -80), new Vec2(0, 80) } };
        for (int i = 0; i < 2; i++)
        {
            var pr = PathReference.FromPoints(axes[i], 0);
            var axis = new Polyline2(pr.Points);
            var g = setups[i]().Build(pr, new OutputSettings(), Cat, axis: axis);
            foreach (var m in g) if (m.Path is { } p && p.Points.Count == pr.Points.Count && p.Points[0].DistanceTo(pr.Points[0]) < 1e-9) w2.Paths[m.Id] = axis;
            w2.Defs.AddRange(g);
            w2.Groups.Add(g);
            w2.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w2.Roads))
        {
            var rs = ids.Select(i => w2.Roads[i]).ToList();
            var d = new IntersectionDefinition { Control = ControleIntersecao.Pare, Node = node };
            w2.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w2.Groups[i]).ToList(), w2.Paths, w2.Defs, Cat));
        }
        var parts = Y34TipTests.Geometry(w2);
        var line = parts.Where(p => p.Def is LinearMarkingDefinition { Code: RoadSetup.ParkingLineCode }).Select(p => p.Piece.Shape).ToList();
        var stalls0 = Y34TipTests.Geometry(w).Where(p => p.Def is ParkingMarkingDefinition && Math.Abs(p.Piece.Shape.Centroid.Y) < 8).Select(p => p.Piece.Shape).ToList();
        Assert.NotEmpty(line);
        Assert.NotEmpty(stalls0);
        // A linha começa onde as vagas começavam (fim do trecho sem estacionamento junto ao cruzamento – travessia + 5 m).
        double Near(List<Polygon2> ps) => ps.SelectMany(p => p.Outer).Where(v => v.X > 0).Min(v => v.X);
        Assert.InRange(Near(line), Near(stalls0) - 0.6, Near(stalls0) + 0.6);
        var crosswalks = parts.Where(p => p.Def is LinearMarkingDefinition { Code: "FTP-1" or "FTP-2" }).Select(p => p.Piece.Shape).ToList();
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(line, crosswalks)) < 1e-3, "linha do estacionamento sobre a faixa de pedestres");
    }
}
