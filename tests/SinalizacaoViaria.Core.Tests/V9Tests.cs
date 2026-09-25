using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Modelos personalizados, pista passo a passo, interseção simples e orelha com pontas diferentes.</summary>
public class V9Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    [Fact]
    public void CustomTemplate_RoundTripsWholeSection()
    {
        var s = RoadTemplates.All[2].Create();
        s.Hierarchy = HierarquiaViaria.Arterial;
        s.Right[0].Largura = 3.15;
        var back = RoadTemplates.FromJson(RoadTemplates.ToJson(s))!;
        Assert.Equal(s.Right.Count, back.Right.Count);
        Assert.Equal(3.15, back.Right[0].Largura, 6);
        Assert.Equal(HierarquiaViaria.Arterial, back.Hierarchy);
        Assert.Equal(s.Center, back.Center);
        Assert.Equal(s.TotalWidth, back.TotalWidth, 6);
        Assert.Null(RoadTemplates.FromJson("{ inválido"));
    }

    [Fact]
    public void Carriageway_ThenStepByStepEdgeElements()
    {
        var pr = PathReference.FromPoints(new[] { Vec2.Zero, new Vec2(100, 0) }, 0);
        var tpl = new RoadPavementDefinition { RightWidth = 3.5, LeftWidth = 3.5, Material = TipoPavimento.Bloquete, Hierarchy = HierarquiaViaria.Local };
        var defs = RoadConnection.BuildCarriageway(tpl, pr, new OutputSettings(), "LFO-2", false, 30);
        var pav = defs.OfType<RoadPavementDefinition>().Single();
        Assert.Equal(2, defs.Count);
        Assert.All(defs, d => Assert.Equal(pav.GroupId, d.GroupId));
        Assert.Equal(0, pav.RightSidewalk);

        // Meio-fio e calçada à esquerda: empilhados a partir do bordo; a seção do pavimento acompanha.
        var curb = new LinearMarkingDefinition { Code = "MEIO-FIO" };
        var o1 = RoadConnection.PlaceAtEdge(pav, true, curb, Cat);
        Assert.Equal(3.5 + 0.075, o1, 3);
        var walk = new LinearMarkingDefinition { Code = "CALCADA", WidthOverride = 2.5 };
        var o2 = RoadConnection.PlaceAtEdge(pav, true, walk, Cat);
        Assert.Equal(3.5 + 0.15 + 1.25, o2, 3);
        Assert.Equal(2.65, pav.LeftSidewalk, 3);
        Assert.Equal(pav.GroupId, walk.GroupId);
        Assert.Equal(HierarquiaViaria.Local, walk.Hierarchy);
        // Sarjeta à direita: dentro da pista, junto ao bordo, e registrada como faixa sem pavimento.
        var gut = new LinearMarkingDefinition { Code = "SARJETA", WidthOverride = 0.3 };
        Assert.Equal(-(3.5 - 0.15), RoadConnection.PlaceAtEdge(pav, false, gut, Cat), 3);
        Assert.Contains(pav.Gaps, g => !g.Median && g.Offset < 0);
        // Linha de bordo depois da sarjeta: mais para dentro.
        var lbo = new LinearMarkingDefinition { Code = "LBO" };
        Assert.True(RoadConnection.PlaceAtEdge(pav, false, lbo, Cat) > -(3.5 - 0.3));

        // A interseção passa a refazer a calçada acrescentada.
        var road = new IntersectionRoad(pav, new Polyline2(pr.Points));
        var other = new IntersectionRoad(new RoadPavementDefinition { RightWidth = 3.5, LeftWidth = 3.5, LeftSidewalk = 2.5, RightSidewalk = 2.5 },
            new Polyline2(new[] { new Vec2(50, 60), new Vec2(50, 0) }));
        var L = IntersectionGenerator.Layout(new IntersectionDefinition { Node = new Vec2(50, 0) }, new[] { road, other });
        Assert.NotEmpty(L.Sidewalk);
    }

    [Fact]
    public void SimpleTee_MainRoadLinesContinue()
    {
        var s = IntersectionDemo.Create(new IntersectionDefinition { Crosswalks = false }, Cat, 1, 0, 90, tee: true);
        var L = s.Layout;
        // Eixo e bordo oposto da principal ficam fora do recorte; a boca da secundária é recortada.
        Assert.DoesNotContain(L.PaintCuts[L.Main], p => p.Contains(new Vec2(0, 0)));
        var far = -(L.Roads[L.Main].Def.RightWidth - 0.3);
        Assert.DoesNotContain(L.PaintCuts[L.Main], p => p.Contains(new Vec2(0, far)));
        var near = L.Roads[L.Main].Def.LeftWidth - 0.3;
        Assert.Contains(L.PaintCuts[L.Main], p => p.Contains(new Vec2(0, near)));
        // Sem faixas: só retenção + PARE + R-1 na secundária.
        var ch = s.Definitions.Where(d => d.GroupId == s.Definitions.OfType<IntersectionDefinition>().Single().Id).ToList();
        Assert.DoesNotContain(ch, c => c is LinearMarkingDefinition { Code: "FTP-1" });
        Assert.Single(ch, c => c is LinearMarkingDefinition { Code: "LRE" });
        Assert.Single(ch, c => c is SignDefinition { Code: "R-1" });

        // Semáforo: a principal também é interrompida.
        var sem = IntersectionDemo.Create(new IntersectionDefinition { Crosswalks = false, Control = ControleIntersecao.Semaforo }, Cat, 1, 0, 90, tee: true);
        Assert.Contains(sem.Layout.PaintCuts[sem.Layout.Main], p => p.Contains(new Vec2(0, 0)));
    }

    [Fact]
    public void CurbExtension_EachEndItsOwnTransition()
    {
        var d = new CurbExtensionDefinition { Depth = 2.2, Transition = TipoTransicao.Curva, Radius = 1.5, EndTransition = TipoTransicao.Reta };
        var prof = SidewalkGenerator.EarProfile(d, 12);
        Assert.Equal(0, prof[0].Y, 6);
        // Início em curva (transição com comprimento), fim reto (perpendicular).
        Assert.True(prof.First(p => p.Y >= 2.2 - 1e-6).X > 1.0);
        Assert.True(prof[^1].X >= 12 - 1e-6 && prof[^1].Y < 1e-6);
        Assert.True(prof[^2].X > 11.99 && prof[^2].Y > 2.19);
        var axis = new Polyline2(new[] { Vec2.Zero, new Vec2(12, 0) });
        var geo = MarkingBuilder.Build(d, axis, Ctx);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Concreto);
        var json = MarkingDefinition.FromJson(d.ToJson()) as CurbExtensionDefinition;
        Assert.Equal(TipoTransicao.Reta, json!.EndTransition);
    }
}
