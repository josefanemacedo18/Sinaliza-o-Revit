using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada Y: rotatórias e vias com os mesmos critérios dos cruzamentos – pavimento sem furos nem sobreposição, nenhum piso
/// &lt; 0,01 m² nem autointerseção, pintura dentro da pista.
/// </summary>
public class Y36RoundaboutTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    /// <summary>Vias pelos pontos e uma rotatória no nó (como o plugin: ramos lidos das vias, recortes pela regra da rotatória).</summary>
    private static (Y34TipTests.World W, RoundaboutDefinition D, RoundaboutLayout L) Make(TipoRotatoria type, params (int Tpl, Vec2[] Pts)[] roads)
    {
        var w = new Y34TipTests.World();
        foreach (var (tpl, pts) in roads)
        {
            var pr = PathReference.FromPoints(pts, 0);
            var g = RoadTemplates.All[tpl].Create().Build(pr, new OutputSettings(), Cat);
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        var d = new RoundaboutDefinition { Center = Vec2.Zero };
        d.ApplyPreset(type);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, w.Roads, d.OuterRadius + 25);
        var L = RoundaboutGenerator.Layout(d);
        foreach (var g in w.Groups)
            foreach (var m in g)
                foreach (var cut in RoundaboutGenerator.RoadCuts(d, L, m))
                    m.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = cut.Outer.ToList() });
        w.Defs.Add(d);
        foreach (var c in RoundaboutGenerator.Children(d, L, new OutputSettings(), 0)) { c.GroupId = d.Id; w.Defs.Add(c); }
        return (w, d, L);
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var type in new[] { TipoRotatoria.Compacta, TipoRotatoria.UmaFaixa, TipoRotatoria.DuasFaixas, TipoRotatoria.ComBypass })
            foreach (var (a, b) in new[] { (0, 0), (1, 2), (2, 1) })
                yield return new object[] { type, a, b };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Rotatoria_sem_falhas_de_modelagem(TipoRotatoria type, int a, int b)
    {
        var (w, d, L) = Make(type, (a, new[] { new Vec2(-120, 0), new Vec2(120, 0) }), (b, new[] { new Vec2(0, -120), new Vec2(0, 120) }));
        Assert.True(d.Legs.Count >= 4);
        var parts = Y34TipTests.Geometry(w);
        var R = L.Zone.Outer.Max(p => p.DistanceTo(Vec2.Zero)) + 6;
        var f = Y34TipTests.FaultsAt(parts, Vec2.Zero, R, walks: false);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Theory]
    [InlineData(TipoRotatoria.UmaFaixa, 0)]
    [InlineData(TipoRotatoria.Compacta, 1)]
    public void Rotatoria_na_ponta_das_vias_sem_falhas(TipoRotatoria type, int tpl)
    {
        // Três vias que terminam no centro (rotatória criada na ponta dos eixos).
        var (w, d, L) = Make(type, (tpl, new[] { new Vec2(-110, 0), Vec2.Zero }), (tpl, new[] { Vec2.Zero, new Vec2(110, 0) }),
            (tpl, new[] { Vec2.Zero, Y34TipTests.Dir(100) * 110 }));
        Assert.Equal(3, d.Legs.Count);
        var parts = Y34TipTests.Geometry(w);
        var R = L.Zone.Outer.Max(p => p.DistanceTo(Vec2.Zero)) + 6;
        var f = Y34TipTests.FaultsAt(parts, Vec2.Zero, R, walks: false);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Theory]
    [InlineData(2, 0.0)]
    [InlineData(2, 30.0)]
    [InlineData(2, 60.0)]
    [InlineData(6, 45.0)]
    public void Via_com_canteiro_em_qualquer_direcao_tem_duas_pistas_sem_furo(int tpl, double ang)
    {
        var u = Y34TipTests.Dir(ang);
        var pr = PathReference.FromPoints(new[] { u * -60, u * 60 }, 0);
        var g = RoadTemplates.All[tpl].Create().Build(pr, new OutputSettings(), Cat);
        var pav = g.OfType<RoadPavementDefinition>().First();
        var parts = RoadGenerator.Carriageway(pav, new Polyline2(pr.Points));
        Assert.All(parts, p => Assert.Empty(p.Holes));
        var band = RoadGenerator.Band(new Polyline2(pr.Points), -pav.RightWidth, pav.LeftWidth).Sum(p => p.Area);
        var gaps = pav.Gaps.Sum(x => x.Width) * 120;
        Assert.Equal(band - gaps, parts.Sum(p => p.Area), 1);
        // Os canteiros (central ou laterais) não são pista.
        foreach (var gap in pav.Gaps.Where(x => x.Median))
            Assert.DoesNotContain(parts, p => p.Contains(u.PerpLeft * gap.Offset));
    }
}
