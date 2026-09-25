using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Tipos de interseção: controle, gota, canalização das esquinas, bolsões e ângulos quaisquer.</summary>
public class V6Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static List<MarkingDefinition> Children(IntersectionDemo.Scene s) =>
        s.Definitions.Where(d => d.GroupId == s.Definitions.OfType<IntersectionDefinition>().Single().Id).ToList();

    [Fact]
    public void Pare_OnlyOnSecondaryApproaches()
    {
        var s = IntersectionDemo.Create(new IntersectionDefinition(), Cat, 1, 0);
        Assert.Equal(0, s.Layout.Main);                       // coletora (mais larga) é a principal
        var ch = Children(s);
        Assert.Equal(4, ch.Count(c => c is LinearMarkingDefinition { Code: "FTP-1" }));
        Assert.Equal(2, ch.Count(c => c is LinearMarkingDefinition { Code: "LRE" }));
        Assert.Equal(2, ch.Count(c => c is SignDefinition { Code: "R-1" }));
        Assert.Equal(2, ch.Count(c => c is TextMarkingDefinition { Text: "PARE" }));
        // A placa fica do lado direito de quem chega e voltada para ele.
        foreach (var sg in ch.OfType<SignDefinition>())
            Assert.True(sg.Position.Length > 8 && sg.Direction.Dot(-sg.Position.Normalized()) > 0.9);

        var dp = Children(IntersectionDemo.Create(new IntersectionDefinition { Control = ControleIntersecao.DePreferencia }, Cat, 1, 0));
        Assert.Equal(2, dp.Count(c => c is LinearMarkingDefinition { Code: "LDP" }));
        Assert.Equal(2, dp.Count(c => c is SignDefinition { Code: "R-2" }));
        Assert.DoesNotContain(dp, c => c is LinearMarkingDefinition { Code: "LRE" });
    }

    [Fact]
    public void TeeJunction_ThroughRoadIsMain()
    {
        // Mesmo modelo: a via que atravessa o nó (dois ramos) é a principal.
        var s = IntersectionDemo.Create(new IntersectionDefinition(), Cat, 0, 0, 90, tee: true);
        Assert.Equal(3, s.Layout.Legs.Count);
        Assert.Equal(0, s.Layout.Main);
        Assert.Single(Children(s), c => c is LinearMarkingDefinition { Code: "LRE" });
    }

    [Fact]
    public void TypeII_SplitterIslandWithFlareAndRefuge()
    {
        var d = new IntersectionDefinition { SplitterIslands = TipoIlha.Fisica };
        var s = IntersectionDemo.Create(d, Cat, 1, 0, 90, tee: true);
        var L = s.Layout;
        var f = Assert.Single(L.Features);
        Assert.Equal(TipoRamo.Gota, f.Kind);
        Assert.NotEqual(L.Main, f.Leg.Road);
        // Ilha cortada pela travessia (refúgio no nível da pista): duas partes, no eixo da secundária.
        Assert.Equal(2, L.Islands.Count);
        Assert.All(L.Islands, i => Assert.True(Math.Abs(i.Centroid.X) < 0.5));
        // Pista alargada ao lado da ilha.
        var t = f.IslandStart + 3;
        Assert.Contains(L.Pavement, p => p.Contains(L.At(f.Leg, t, L.Roads[f.Leg.Road].Def.LeftWidth + 0.5)));
        // A retenção vai do bordo até a ilha.
        var lre = Children(s).OfType<LinearMarkingDefinition>().Single(c => c.Code == "LRE");
        var len = lre.PathRef.Points[0].DistanceTo(lre.PathRef.Points[1]);
        Assert.True(len is > 2.0 and < 6.5, $"LRE {len:0.00} m");
        // A pintura da secundária é recortada até o fim do alargamento e refeita deslocada.
        Assert.NotEmpty(L.PaintCuts[f.Leg.Road]);
        var geo = IntersectionDemo.Build(s, Ctx);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Grama || p.Color == MarkingColor.Concreto);
    }

    [Fact]
    public void TypeIII_ChannelizedCornersHaveIslands()
    {
        var s = IntersectionDemo.Create(new IntersectionDefinition { RightTurnIslands = TipoIlha.Fisica }, Cat, 1, 1);
        Assert.Equal(4, s.Layout.Islands.Count);
        // Ilhas nos quatro quadrantes, fora das pistas.
        Assert.Equal(4, s.Layout.Islands.Select(i => (Math.Sign(i.Centroid.X), Math.Sign(i.Centroid.Y))).Distinct().Count());
        var painted = IntersectionDemo.Create(new IntersectionDefinition { RightTurnIslands = TipoIlha.Pintada }, Cat, 1, 1);
        Assert.Empty(painted.Layout.Islands);
        Assert.Equal(4, Children(painted).Count(c => c is HatchMarkingDefinition { Code: "ZPA" }));
        // Só as esquinas agudas num cruzamento a 60°.
        var acute = IntersectionDemo.Create(new IntersectionDefinition { RightTurnIslands = TipoIlha.Fisica, RightTurnCorners = EsquinasCanalizadas.Agudas }, Cat, 1, 1, 60);
        Assert.InRange(acute.Layout.Islands.Count, 0, 2);
    }

    [Fact]
    public void TypeIV_PocketByWideningOrInMedian()
    {
        var w = IntersectionDemo.Create(new IntersectionDefinition { LeftTurnPockets = true }, Cat, 1, 0, 90, tee: true);
        Assert.Equal(2, w.Layout.Features.Count(f => f.Kind == TipoRamo.BolsaoAlargado));
        var ch = Children(w);
        Assert.Equal(2, ch.Count(c => c is SymbolMarkingDefinition { Code: "PEM-E" }));
        Assert.Equal(2, ch.Count(c => c is HatchMarkingDefinition { Code: "ZPA-A" }));
        // As linhas de faixa da via principal continuam, deslocadas pelo alargamento.
        Assert.Contains(ch, c => c is LinearMarkingDefinition { Code: "LMS-2" } l && l.PathRef.Points.Count > 5);
        // Seta para a esquerda de quem chega: aponta para o nó.
        foreach (var a in ch.OfType<SymbolMarkingDefinition>())
            Assert.True(a.Direction.Dot(-a.Position.Normalized()) > 0.9);

        var m = IntersectionDemo.Create(new IntersectionDefinition { LeftTurnPockets = true }, Cat, 2, 0);
        Assert.Equal(2, m.Layout.Features.Count(f => f.Kind == TipoRamo.BolsaoCanteiro));
        // O canteiro some no trecho do bolsão (pista), mas continua depois do teiper.
        var f0 = m.Layout.Features.First();
        Assert.Contains(m.Layout.Pavement, p => p.Contains(m.Layout.At(f0.Leg, f0.PocketStart + 5, f0.PocketHi - 1.2)));
        Assert.DoesNotContain(m.Layout.Pavement, p => p.Contains(m.Layout.At(f0.Leg, f0.TaperEnd + 2, f0.PocketHi - 1.2)));
    }

    [Theory]
    [InlineData(90, false)]
    [InlineData(60, false)]
    [InlineData(35, false)]
    [InlineData(90, true)]
    [InlineData(45, true)]
    [InlineData(25, true)]
    public void AnyAngle_ProducesAllLegsAndKeepsFarPaint(double angle, bool tee)
    {
        var all = new IntersectionDefinition
        {
            SplitterIslands = TipoIlha.Fisica, RightTurnIslands = TipoIlha.Pintada, LeftTurnPockets = true,
        };
        var s = IntersectionDemo.Create(all, Cat, 1, 0, angle, tee, 160);
        Assert.Equal(tee ? 3 : 4, s.Layout.Legs.Count);
        Assert.NotEmpty(s.Layout.Pavement);
        // Longe do nó a sinalização da via principal é mantida (a zona do nó não engole os ramos).
        var geo = IntersectionDemo.Build(s, Ctx);
        var far = new Vec2(-130, 0);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Amarela && p.Shape.Centroid.DistanceTo(far) < 15);
    }

    [Fact]
    public void Build_CutsRampsFromRebuiltSidewalks()
    {
        var s = IntersectionDemo.Create(new IntersectionDefinition { SplitterIslands = TipoIlha.Fisica }, Cat, 1, 0);
        var it = s.Definitions.OfType<IntersectionDefinition>().Single();
        var geo = MarkingBuilder.Build(it, null, new BuildContext
        {
            Catalog = Cat, Lookup = id => s.Definitions.FirstOrDefault(x => x.Id == id), PathOf = x => s.Paths.GetValueOrDefault(x.Id),
        });
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Asfalto);
        var ramp = Children(s).OfType<RampDefinition>().First();
        var c = ramp.PathRef.Points[0] + (ramp.PathRef.Points[1] - ramp.PathRef.Points[0]) * 0.5;
        Assert.DoesNotContain(geo.Pieces, p => p.Color == MarkingColor.Concreto && p.Thickness > 0.1 && p.Shape.Contains(c));
    }
}
