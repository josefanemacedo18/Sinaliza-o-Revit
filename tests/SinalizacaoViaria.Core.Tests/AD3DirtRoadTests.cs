using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AD (item 15): pavimento "Terra" ao lado de asfalto, bloquete e concreto – material e cor próprios, quantitativo em m²,
/// sem pintura automática e com aviso quando há pintura sobre a terra.
/// </summary>
public class AD3DirtRoadTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static readonly Polyline2 Axis = new(new[] { new Vec2(0, 0), new Vec2(100, 0) });

    private static RoadSetup Local(TipoPavimento p)
    {
        var s = RoadTemplates.All[0].Create();
        s.Pavement = p;
        return s;
    }

    [Fact]
    public void Terra_entra_no_quantitativo_em_m2_sem_pintura_automatica()
    {
        var s = Local(TipoPavimento.Terra);
        var defs = s.Build(PathReference.FromPoints(Axis.Points, 0), new OutputSettings(), Cat, axis: Axis);
        var pav = defs.OfType<RoadPavementDefinition>().Single();
        Assert.Equal("PAV-TER", pav.DisplayCode);
        Assert.Equal(MarkingColor.PavimentoTerra, pav.Color);
        Assert.True(MarkingColors.IsPavement(MarkingColor.PavimentoTerra));
        Assert.False(MarkingColors.IsPaint(MarkingColor.PavimentoTerra));
        Assert.NotEqual(MarkingColors.Display(MarkingColor.Asfalto), MarkingColors.Display(MarkingColor.PavimentoTerra));
        // Sem pintura automática; meio-fio e calçada continuam.
        Assert.DoesNotContain(defs, TerraPaint.IsPaint);
        Assert.Contains(defs, d => d is LinearMarkingDefinition { Code: "MEIO-FIO" });
        Assert.Contains(s.Warnings, w => w.Contains("terra", StringComparison.OrdinalIgnoreCase));
        // Quantitativo: linha do pavimento de terra em m², com a área da pista.
        var rows = QuantityCalculator.Compute(defs.Select(d => (d, MarkingBuilder.Build(d, Axis, Ctx))), Cat);
        var row = Assert.Single(rows, r => r.Code == "PAV-TER");
        Assert.Equal("m²", row.Unit);
        Assert.Equal(MarkingColor.PavimentoTerra, row.Color);
        Assert.Equal(CategoriaQuantitativo.PavimentacaoGeometria, row.Category);
        var expected = 100 * (pav.LeftWidth + pav.RightWidth);
        Assert.InRange(row.MainQuantity, expected * 0.9, expected * 1.01);
        Assert.Contains("terra", row.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pintura_sobre_terra_gera_aviso()
    {
        var terra = Local(TipoPavimento.Terra).Build(PathReference.FromPoints(Axis.Points, 0), new OutputSettings(), Cat, axis: Axis);
        var asphaltAxis = new Polyline2(new[] { new Vec2(0, 40), new Vec2(100, 40) });
        var asphalt = Local(TipoPavimento.Asfalto).Build(PathReference.FromPoints(asphaltAxis.Points, 0), new OutputSettings(), Cat, axis: asphaltAxis);
        var roads = new List<IntersectionRoad>
        {
            new(terra.OfType<RoadPavementDefinition>().Single(), Axis), new(asphalt.OfType<RoadPavementDefinition>().Single(), asphaltAxis),
        };
        var onDirt = new LinearMarkingDefinition { Code = "LFO-2", PathRef = PathReference.FromPoints(new[] { new Vec2(10, 0), new Vec2(90, 0) }, 0) };
        var onAsphalt = new LinearMarkingDefinition { Code = "LFO-2", PathRef = PathReference.FromPoints(new[] { new Vec2(10, 40), new Vec2(90, 40) }, 0) };
        var arrow = new SymbolMarkingDefinition { Code = "PEM-F", Position = new Vec2(50, -1.5) };
        Assert.NotEmpty(TerraPaint.Warnings(new MarkingDefinition[] { onDirt }, roads, TerraPaint.StoredPath));
        Assert.NotEmpty(TerraPaint.Warnings(new MarkingDefinition[] { arrow }, roads, TerraPaint.StoredPath));
        Assert.Empty(TerraPaint.Warnings(new MarkingDefinition[] { onAsphalt }, roads, TerraPaint.StoredPath));
        // Meio-fio (elemento físico) não é pintura.
        Assert.Empty(TerraPaint.Warnings(terra.Where(d => d is LinearMarkingDefinition { Code: "MEIO-FIO" }), roads, TerraPaint.StoredPath));
    }

    [Fact]
    public void Cruzamento_de_terra_com_asfalto_so_pinta_o_asfalto()
    {
        var w = new Y34TipTests.World();
        var axes = new[] { new[] { new Vec2(-80, 0), new Vec2(80, 0) }, new[] { new Vec2(0, -80), new Vec2(0, 80) } };
        var pavs = new[] { TipoPavimento.Asfalto, TipoPavimento.Terra };
        for (int i = 0; i < 2; i++)
        {
            var pr = PathReference.FromPoints(axes[i], 0);
            var axis = new Polyline2(pr.Points);
            var g = Local(pavs[i]).Build(pr, new OutputSettings(), Cat, axis: axis);
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
        var it = w.Nodes.Single().Layout.Definition!;
        var children = w.Defs.Where(d => d.GroupId == it.Id).ToList();
        // Travessias na via asfaltada continuam; nenhuma pintura sobre a pista de terra.
        Assert.Contains(children, d => d is LinearMarkingDefinition { Code: "FTP-1" or "FTP-2" });
        Assert.Empty(TerraPaint.OnTerra(children, w.Roads, TerraPaint.StoredPath));
        var parts = Y34TipTests.Geometry(w);
        Assert.Contains(parts, p => p.Piece.Color == MarkingColor.PavimentoTerra);
    }
}
