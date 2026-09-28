using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada S: larguras com sarjeta e meio-fio, interseções entre vias diferentes.</summary>
public class V23Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static RoadSetup Coletora() => RoadTemplates.All[1].Create();

    [Fact]
    public void Gutter_IsAddedToTheCarriagewayWidth()
    {
        var s = Coletora();                                  // 3,30 + 3,50 por lado, sarjeta 0,30, calçada 3,00
        Assert.True(s.SarjetaSomada);
        var pav = s.PavementDefinition();
        Assert.Equal(7.10, pav.RightWidth, 3);                // faixas + sarjeta até a face do meio-fio
        var w = s.Widths();
        Assert.Equal(14.20, w.EntreMeiosFios, 3);
        Assert.Equal(14.50, w.ComMeiosFios, 3);
        Assert.Equal(13.60, w.Faixas, 3);
        Assert.Equal(0.60, w.Sarjetas, 3);
        Assert.Equal(20.20, w.Total, 3);
        // A linha de bordo fica na faixa (a 0,10 m do bordo da faixa), e a sarjeta entre a faixa e o meio-fio.
        var defs = s.Build(PathReference.FromPoints(new[] { Vec2.Zero, new Vec2(50, 0) }, 0), new OutputSettings(), Cat);
        var gutter = defs.OfType<LinearMarkingDefinition>().First(l => l.Code == "SARJETA" && l.Offset < 0);
        Assert.Equal(-6.95, gutter.Offset, 3);
        var edge = defs.OfType<LinearMarkingDefinition>().First(l => l.Code == "LBO" && l.Offset < 0);
        Assert.Equal(-6.70, edge.Offset, 3);
    }

    [Fact]
    public void Gutter_LegacyRoadsKeepTheGutterInsideTheLane()
    {
        var s = Coletora();
        s.SarjetaSomada = null;                              // vias criadas antes da versão
        Assert.Equal(6.80, s.PavementDefinition().RightWidth, 3);
        Assert.Equal(13.60, s.Widths().EntreMeiosFios, 3);
    }

    [Fact]
    public void Curb_WidthIsCustomizable()
    {
        var s = Coletora();
        foreach (var e in s.Right.Concat(s.Left).Where(e => e.Tipo == TipoElementoSecao.Calcada)) e.LarguraMeioFio = 0.20;
        var defs = s.Build(PathReference.FromPoints(new[] { Vec2.Zero, new Vec2(50, 0) }, 0), new OutputSettings(), Cat);
        var curb = defs.OfType<LinearMarkingDefinition>().First(l => l.Code == "MEIO-FIO" && l.Offset < 0);
        Assert.Equal(0.20, curb.WidthOverride!.Value, 3);
        Assert.Equal(-7.20, curb.Offset, 3);                  // face na borda da pista, eixo da guia a 0,10 m
        Assert.Equal(0.20, s.PavementDefinition().CurbWidth, 3);
        Assert.Equal(0.40, s.Widths().MeiosFios, 3);
    }

    [Fact]
    public void EdgeProfile_LateralPlanterIsNotReadAsAGutter()
    {
        // "Via com canteiros laterais": canteiro físico entre a pista e o estacionamento – não é da calçada.
        var s = RoadTemplates.All[6].Create();
        var defs = s.Build(PathReference.FromPoints(new[] { Vec2.Zero, new Vec2(60, 0) }, 0), new OutputSettings(), Cat);
        var pav = defs.OfType<RoadPavementDefinition>().First();
        var p = EdgeProfile.FromRoadSides(pav, defs, Ctx);
        foreach (var side in new[] { p.Left, p.Right })
        {
            Assert.DoesNotContain(side, b => b.Inside && b.Code != "SARJETA" && b.Code != "SARJETAO");
            Assert.DoesNotContain(side, b => b.Inside && b.D1 < -0.1);
            Assert.Contains(side, b => b.Code.StartsWith("MEIO-FIO") && Math.Abs(b.D0) < 0.02);
        }
    }

    [Fact]
    public void EdgeProfile_BlendTapersMissingBandsButKeepsTheCurb()
    {
        var a = new List<EdgeBand>
        {
            new() { D0 = 0, D1 = 0.15, Code = "MEIO-FIO" }, new() { D0 = 0.15, D1 = 0.70, Code = "GRAMADO" }, new() { D0 = 0.70, D1 = 3.5, Code = "CALCADA" },
        };
        var b = new List<EdgeBand> { new() { D0 = 0, D1 = 0.15, Code = "MEIO-FIO" }, new() { D0 = 0.15, D1 = 2.5, Code = "CALCADA" } };
        var mid = EdgeProfile.Blend(a, b, 0.5);
        Assert.Equal(0.15, mid.First(x => x.Code == "MEIO-FIO").Width, 3);
        Assert.Equal(0.275, mid.First(x => x.Code == "GRAMADO").Width, 3);
        Assert.Equal((3.5 + 2.5) / 2, mid.Max(x => x.D1), 3);
        var end = EdgeProfile.Blend(a, new List<EdgeBand>(), 1.0);
        Assert.Contains(end, x => x.Code == "MEIO-FIO" && Math.Abs(x.Width - 0.15) < 1e-6);
        Assert.DoesNotContain(end, x => x.Code == "CALCADA");
    }

    private static IntersectionDemo.Scene Mixed(int main, int minor) =>
        IntersectionDemo.Create(new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = true, StopLines = true, Signs = false, CornerRadius = 8 },
            Cat, main, minor, 90, false, 70);

    [Fact]
    public void Intersection_LateralPlantersEndBehindTheCrosswalk()
    {
        var s = Mixed(2, 6);                                  // avenida × via com canteiros laterais
        var L = s.Layout;
        var r = L.Roads[1];
        var lateral = r.Def.Gaps.Where(g => g.Median && Math.Abs(g.Offset) > g.Width / 2 + 0.1).ToList();
        Assert.NotEmpty(lateral);
        var solids = L.MedianCore.Concat(L.MedianCurb).ToList();
        foreach (var leg in L.Legs.Where(l => l.Road == 1))
        {
            var tEnd = leg.Clear + 1.0 + 4.0;                 // recuo + largura da faixa de pedestres
            foreach (var g in lateral)
                for (var t = 0.5; t < tEnd - 0.3; t += 0.5)
                {
                    var p = r.Axis.PointAt(leg.StationAt(t)) + r.Axis.TangentAt(leg.StationAt(t)).PerpLeft * g.Offset;
                    Assert.DoesNotContain(solids, x => x.Contains(p));
                }
        }
    }

    [Fact]
    public void Intersection_CornersBetweenDifferentRoadsHaveNoGaps()
    {
        foreach (var (a, b) in new[] { (2, 6), (8, 0), (2, 5), (3, 9) })
        {
            var s = Mixed(a, b);
            var geo = IntersectionDemo.Build(s, Ctx, false);
            var it = s.Definitions.OfType<IntersectionDefinition>().Last();
            Assert.NotEmpty(it.RoadProfiles);
            // Tudo o que a interseção refaz como calçada fica fora da pista.
            var walks = geo.Pieces.Where(p => p.Thickness >= 0.1 && p.Color is MarkingColor.Concreto or MarkingColor.Grama).Select(p => p.Shape).ToList();
            var carriage = PolygonOps.Difference(
                PolygonOps.Union(s.Layout.Roads.SelectMany(x => RoadGenerator.Band(x.Axis, -x.Def.RightWidth + 0.05, x.Def.LeftWidth - 0.05))),
                PolygonOps.Union(s.Layout.Roads.SelectMany(x => x.Def.Gaps.Where(g => g.Median)
                    .SelectMany(g => RoadGenerator.Band(x.Axis, g.Offset - g.Width / 2 - 0.2, g.Offset + g.Width / 2 + 0.2)))));
            Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(walks, carriage)) < 0.3, $"{a}×{b}: calçada sobre a pista");
            // Cada esquina com calçada nas duas vias tem calçada contínua ao longo da curva (sem buracos no meio).
            foreach (var c in s.Layout.Corners)
            {
                var ra = s.Layout.Roads[c.RoadA].Def;
                var rb = s.Layout.Roads[c.RoadB].Def;
                if ((c.LeftA ? ra.LeftSidewalk : ra.RightSidewalk) < 0.5 || (c.LeftB ? rb.LeftSidewalk : rb.RightSidewalk) < 0.5) continue;
                var inTri = PolygonOps.Intersect(PolygonOps.Union(walks), new[] { c.Tri });
                Assert.True(inTri.Count <= 2, $"{a}×{b}: calçada da esquina em {inTri.Count} pedaços");
            }
        }
    }
}
