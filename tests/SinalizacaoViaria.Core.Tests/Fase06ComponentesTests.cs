using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Fase VI – quantitativo por componente da seção: pavimento, meio-fio, sarjeta, calçada, gramado e ilhas somados em todas as
/// marcas (vias, interseções, rotatórias, extensões de calçada, cul-de-sac). No quadro por marca a interseção e a rotatória
/// juntavam meio-fio, sarjeta e calçada numa linha "Concreto".
/// </summary>
public class Fase06ComponentesTests(ITestOutputHelper output)
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static double Physical(MarkingGeometry g) => g.Pieces.Where(p => !MarkingColors.IsPaint(p.Color)).Sum(p => p.Shape.Area);

    [Fact]
    public void ExtensaoDoMeioFio_ExataNaFaixaRetaNaCurvaENoAnel()
    {
        // Retângulo 10 × 0,15 m.
        Assert.Equal(10, ComponentQuantities.StripLength(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(10, 0.15))), 9);
        // Setor de coroa circular: raio interno 6 m, 0,15 m de largura, 90° – eixo no raio 6,075 m.
        var inner = Enumerable.Range(0, 91).Select(i => i * Math.PI / 180).Select(a => new Vec2(6 * Math.Cos(a), 6 * Math.Sin(a))).ToList();
        var outer = inner.Select(v => v * (6.15 / 6)).Reverse().ToList();
        var arc = new Polygon2(inner.Concat(outer));
        var mid = inner.Zip(inner.Skip(1)).Sum(s => s.First.DistanceTo(s.Second)) * (6.075 / 6);
        // O polígono do setor (cordas) difere do setor exato em ~1e-6 do comprimento.
        Assert.Equal(mid, ComponentQuantities.StripLength(arc), 4);
        // Anel fechado (meio-fio em volta de uma ilha): sem pontas, metade do perímetro.
        List<Vec2> Circle(double r) => Enumerable.Range(0, 360).Select(i => i * Math.PI / 180).Select(a => new Vec2(r * Math.Cos(a), r * Math.Sin(a))).ToList();
        var ring = new Polygon2(Circle(5.15), new[] { Circle(5.0) });
        var expected = (Circle(5.15).Append(Circle(5.15)[0]).Zip(Circle(5.15).Skip(1).Append(Circle(5.15)[0])).Sum(s => s.First.DistanceTo(s.Second))
                        + Circle(5.0).Append(Circle(5.0)[0]).Zip(Circle(5.0).Skip(1).Append(Circle(5.0)[0])).Sum(s => s.First.DistanceTo(s.Second))) / 2;
        Assert.Equal(expected, ComponentQuantities.StripLength(ring), 6);
    }

    [Fact]
    public void Via_ComponentesDosModelos_SomamOFisicoGerado()
    {
        foreach (var t in RoadTemplates.All)
        {
            var pr = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(100, 0) }, 0);
            var defs = t.Create().Build(pr, new OutputSettings(), Cat);
            var axis = new Polyline2(pr.Points);
            var ctx = new BuildContext { Catalog = Cat, Lookup = id => defs.FirstOrDefault(x => x.Id == id), PathOf = _ => axis, AllDefinitions = () => defs };
            var items = defs.Select(d => (d, MarkingBuilder.Build(d, d.Path != null ? axis : null, ctx))).ToList();
            var rows = ComponentQuantities.Compute(items);
            var physical = items.Where(x => ComponentQuantities.InScope(x.d)).Sum(x => Physical(x.Item2));
            Assert.Equal(physical, rows.Sum(r => r.Area), 6);
            Assert.DoesNotContain(rows, r => r.Code == SectionComponent.Outros);
            // Pavimento: o mesmo do quadro por marca.
            var pav = items.Where(x => x.d is RoadPavementDefinition).Sum(x => x.Item2.Pieces.Where(p => MarkingColors.IsPavement(p.Color)).Sum(p => p.Shape.Area));
            Assert.Equal(pav, rows.Where(r => r.Code == SectionComponent.Pavimento).Sum(r => r.Area), 6);
            // Meio-fio de 100 m de via: 100 m por lado com meio-fio.
            var curbs = defs.OfType<LinearMarkingDefinition>().Count(l => SectionComponent.FromCode(l.Code) == SectionComponent.MeioFio);
            var len = rows.Where(r => r.Code == SectionComponent.MeioFio).Sum(r => r.Length);
            output.WriteLine($"{t.Name}: {curbs} meio(s)-fio(s), {len:0.00} m; " + string.Join("; ", rows.Select(r => $"{r.Code} {r.ColorLabel} {r.Area:0.0} m²")));
            Assert.InRange(len, curbs * 99.9, curbs * 100.1);
        }
    }

    [Fact]
    public void Intersecoes_DoAutoteste4_CadaPecaFisicaNumComponente()
    {
        var fx = FCRevitFullRun4Tests.IntersectionFixture();
        var items = fx.OfType<IntersectionDefinition>().Select(d => ((MarkingDefinition)d, FCRevitFullRun4Tests.BuildIntersection(fx, d).Geo)).ToList();
        Assert.NotEmpty(items);
        var rows = ComponentQuantities.Compute(items);
        foreach (var r in rows) output.WriteLine($"{r.Code} {r.ColorLabel} {r.HierarchyName}: {r.Area:0.00} m², {r.Length:0.00} m – {r.Sources}");
        Assert.Equal(items.Sum(x => Physical(x.Item2)), rows.Sum(r => r.Area), 6);
        // Antes: canteiro e ilhas sem camada iam junto com a calçada no "Concreto" da interseção.
        Assert.DoesNotContain(rows, r => r.Code == SectionComponent.Outros);
        foreach (var c in new[] { SectionComponent.Pavimento, SectionComponent.MeioFio, SectionComponent.Sarjeta, SectionComponent.Calcada })
            Assert.Contains(rows, r => r.Code == c && r.Area > 1);
        Assert.True(rows.Where(r => r.Code == SectionComponent.MeioFio).Sum(r => r.Length) > 50);
    }

    [Theory]
    [MemberData(nameof(FCRevitFullRun4Tests.Types), MemberType = typeof(FCRevitFullRun4Tests))]
    public void Rotatorias_CadaPecaFisicaNumComponente(TipoRotatoria type)
    {
        (MarkingGeometry Geo, List<ComponentRow> Rows) Build(bool trees)
        {
            var d = new RoundaboutDefinition { Center = new Vec2(0, 0) };
            d.ApplyPreset(type);
            d.Landscaping = trees;
            var geo = RoundaboutGenerator.Build(d, new BuildContext { Catalog = Cat });
            return (geo, ComponentQuantities.Compute(new[] { ((MarkingDefinition)d, geo) }));
        }
        var (bare, rows) = Build(false);
        foreach (var r in rows) output.WriteLine($"{r.Code} {r.ColorLabel}: {r.Area:0.00} m², {r.Length:0.00} m");
        Assert.Equal(Physical(bare), rows.Sum(r => r.Area), 6);
        // Antes: ilha, divisores, ilhas separadoras e faixa galgável sem camada – nada fica sem componente.
        Assert.DoesNotContain(rows, r => r.Code == SectionComponent.Outros);
        Assert.Contains(rows, r => r.Code == SectionComponent.Pavimento);
        Assert.Contains(rows, r => r.Code == SectionComponent.MeioFio && r.Length > 1);
        // Árvores da ilha (copa, tronco, grama e proteção em volta do tronco) não são componentes da seção: "outros".
        var (treed, rowsT) = Build(true);
        Assert.Equal(Physical(treed) - Physical(bare), rowsT.Where(r => r.Code == SectionComponent.Outros).Sum(r => r.Area), 6);
    }

    [Fact]
    public void PisoEditadoNoModelo_AreaMedidaRepartidaNaProporcao()
    {
        var d = new IntersectionDefinition { Hierarchy = HierarquiaViaria.Coletora };
        var g = new MarkingGeometry();
        g.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(10, 0.1)), MarkingColor.Concreto) { Layer = "MEIO-FIO" });   // 1 m², 10 m
        g.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, 0.1), new Vec2(10, 0.4)), MarkingColor.Concreto) { Component = SectionComponent.Calcada });   // 3 m²
        g.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, -5), new Vec2(10, 0)), MarkingColor.Asfalto));   // 50 m²
        g.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, -2), new Vec2(10, -1.9)), MarkingColor.Branca));   // pintura: fora
        g.AreaOverrides[MarkingColor.Concreto] = 8;
        var rows = ComponentQuantities.Compute(new[] { ((MarkingDefinition)d, g) });
        var curb = Assert.Single(rows, r => r.Code == SectionComponent.MeioFio);
        Assert.Equal(2, curb.Area, 9);
        Assert.Equal(20, curb.Length, 6);
        Assert.Equal(6, Assert.Single(rows, r => r.Code == SectionComponent.Calcada).Area, 9);
        Assert.Equal(50, Assert.Single(rows, r => r.Code == SectionComponent.Pavimento).Area, 9);
        Assert.All(rows, r => Assert.Equal(HierarquiaViaria.Coletora, r.Hierarchy));
        Assert.Equal(3, rows.Count);

        // Piso desenhado à mão com hierarquia (sem peças): a área medida é pavimento.
        var hand = new MarkingGeometry();
        hand.AreaOverrides[MarkingColor.Asfalto] = 120;
        var h = Assert.Single(ComponentQuantities.Compute(new[] { ((MarkingDefinition)new RoadPavementDefinition { Hierarchy = HierarquiaViaria.Local }, hand) }));
        Assert.Equal(SectionComponent.Pavimento, h.Code);
        Assert.Equal(120, h.Area, 9);
    }

    [Fact]
    public void ForaDaSecao_SinalizacaoEMobiliarioNaoEntram()
    {
        var lbo = new LinearMarkingDefinition { Code = "LBO" };
        var g = new MarkingGeometry();
        g.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(10, 0.1)), MarkingColor.Branca));
        var nj = new DeviceMarkingDefinition { Code = "NJ" };
        var gn = new MarkingGeometry();
        gn.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(10, 0.6)), MarkingColor.Concreto));
        Assert.Empty(ComponentQuantities.Compute(new[] { ((MarkingDefinition)lbo, g), (nj, gn) }));
        Assert.False(ComponentQuantities.InScope(nj));
        Assert.True(ComponentQuantities.InScope(new LinearMarkingDefinition { Code = "MEIO-FIO-12" }));
        Assert.True(ComponentQuantities.InScope(new LinearMarkingDefinition { Code = "SARJETAO" }));
    }

    [Fact]
    public void MarcacaoDoComponente_NaoMudaOsPisosDoRevit()
    {
        // A camada separa pisos no Revit; o componente não (FloorKey igual).
        var a = new MarkingPiece(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(1, 1)), MarkingColor.Concreto) { Thickness = 0.15, Component = SectionComponent.Calcada };
        var b = a with { Component = SectionComponent.MeioFio };
        Assert.Equal(ElementPlan.FloorKey(a), ElementPlan.FloorKey(b));
        Assert.NotEqual(ElementPlan.FloorKey(a), ElementPlan.FloorKey(a with { Layer = "MEIO-FIO" }));
    }

    [Fact]
    public void Csv_UmaLinhaPorComponenteComOrigem()
    {
        var d1 = new LinearMarkingDefinition { Code = "SARJETA", Hierarchy = HierarquiaViaria.Local };
        var g1 = new MarkingGeometry();
        g1.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(10, 0.3)), MarkingColor.Concreto));
        var rows = ComponentQuantities.Compute(new[] { ((MarkingDefinition)d1, g1), (d1, g1) });
        var r = Assert.Single(rows);
        Assert.Equal(2, r.Elements);
        Assert.Equal("SARJETA (2)", r.Sources);
        var csv = ComponentQuantities.Csv(rows).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("RESUMO POR COMPONENTE", csv[0]);
        Assert.Contains("Sarjeta;Concreto;", csv[2]);
        Assert.Contains(";6,00;;2;SARJETA (2)", csv[2]);
    }

    [Fact]
    public void PlanilhaECsvDoQuantitativo_TrazemOResumoPorComponente()
    {
        var d = new LinearMarkingDefinition { Code = "MEIO-FIO" };
        var g = new MarkingGeometry();
        g.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, 0), new Vec2(20, 0.15)), MarkingColor.Concreto));
        var items = new List<(MarkingDefinition, MarkingGeometry)> { (d, g) };
        var rows = QuantityCalculator.Compute(items, Cat);
        var comps = ComponentQuantities.Compute(items);
        Assert.Equal(20, Assert.Single(comps).Length, 9);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(QuantityWorkbook.Build(rows, null, "P", null, comps)));
        using var sr = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = sr.ReadToEnd();
        Assert.Contains("RESUMO POR COMPONENTE DA SEÇÃO", sheet);
        Assert.Contains(">Meio-fio (guia)<", sheet);
        var csv = QuantityCalculator.ToCsv(rows, null, "P", comps);
        Assert.Contains("RESUMO POR COMPONENTE DA SEÇÃO", csv);
        Assert.Contains("Meio-fio (guia);Concreto;", csv);
        // Sem componentes (chamadas antigas): nada muda.
        Assert.DoesNotContain("COMPONENTE", QuantityCalculator.ToCsv(rows, null, "P"));
    }
}
