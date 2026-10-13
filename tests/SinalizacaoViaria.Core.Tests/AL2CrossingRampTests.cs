using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AL-2: rampas do eixo viário (travessia no meio da quadra, esquina, extensão de calçada e canteiro) conforme os itens
/// da ABNT NBR 16537:2024 que tratam de rebaixamentos de calçada. As medidas da rampa em si (8,33 %, abas de 10 %, largura de
/// 1,50 m) são da NBR 9050, que não veio anexada – ficam como estavam, marcadas [a confirmar].
/// </summary>
public class AL2CrossingRampTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static bool IsDome(Polygon2 p) => p.Outer.Count == 8;

    /// <summary>Rampas de cada cena: travessia no meio da quadra, esquina, extensão de calçada e canteiro central.</summary>
    public static List<RampDefinition> Scene(string kind)
    {
        switch (kind)
        {
            case "travessia":
                return AC3SidewalkExtensionTests.Road(false, 0, s =>
                    s.ExtensoesCalcada.Add(new ExtensaoCalcada { LadoEsquerdo = false, Estaca = 40, Comprimento = 20, Travessia = true })).Defs
                    .OfType<RampDefinition>().ToList();
            case "esquina":
            {
                var w = Y34TipTests.Make(AB2Matrix.Template(), (2, new[] { new Vec2(-80, 0), new Vec2(80, 0) }), (2, new[] { new Vec2(0, -80), new Vec2(0, 80) }));
                var L = w.Nodes[0].Layout;
                return w.Defs.OfType<RampDefinition>().Where(r => !OnMedian(L, r)).ToList();
            }
            case "extensao":
            {
                var t = AB2Matrix.Template();
                t.CurbExtensions = true;
                var w = Y34TipTests.Make(t, (0, new[] { new Vec2(-80, 0), new Vec2(80, 0) }), (0, new[] { new Vec2(0, -80), new Vec2(0, 80) }));
                return w.Defs.OfType<RampDefinition>().Where(r => r.Type != TipoRampa.RebaixamentoTotal).ToList();
            }
            default:
            {
                var s = RoadTemplates.All[2].Create();
                s.MedianWidth = 8.0;
                s.TravessiasCanteiro.Add(new TravessiaCanteiro { Estaca = 50, Largura = 4.0, Tipo = TipoTravessiaCanteiro.Rampas });
                return s.Build(PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(100, 0) }, 0), new OutputSettings(), Cat)
                    .OfType<RampDefinition>().ToList();
            }
        }
    }

    private static bool OnMedian(IntersectionLayout L, RampDefinition r) =>
        L.Obstacles.Any(o => o.Contains(RampGenerator.FrameOf(new Polyline2(r.PathRef.Points)).P(0, 0.3)));

    /// <summary>Faixa de alerta da rampa: do início ao fim (a partir da guia, na direção da subida) e a extensão ao longo da guia.</summary>
    public static (double From, double To, double Along) AlertSpan(RampDefinition r)
    {
        var path = new Polyline2(r.PathRef.Points);
        var f = RampGenerator.FrameOf(path);
        var geo = RampGenerator.Generate(r, path);
        var domes = geo.Pieces.Where(p => p.Color == MarkingColor.RelevoTatil && IsDome(p.Shape)).Select(p => p.Shape.Centroid - f.Curb).ToList();
        Assert.NotEmpty(domes);
        var sp = TactileGenerator.DomeSpacing;
        var up = domes.Select(d => d.Dot(f.Up)).ToList();
        var side = domes.Select(d => d.Dot(f.Side)).ToList();
        return (up.Min() - sp / 2, up.Max() + sp / 2, side.Max() - side.Min() + sp);
    }

    [Theory]
    [InlineData("travessia")]
    [InlineData("esquina")]
    [InlineData("extensao")]
    [InlineData("canteiro")]
    public void Item6_6_Figuras22a26_AlertaA050DaGuiaNaLarguraDoRebaixamento(string kind)
    {
        var ramps = Scene(kind);
        Assert.NotEmpty(ramps);
        foreach (var r in ramps)
        {
            Assert.True(r.Tactile);
            Assert.Equal(0.50, r.TactileSetback, 6);
            Assert.InRange(r.TactileWidth, 0.40, 0.60);
            var (from, to, along) = AlertSpan(r);
            Assert.True(Math.Abs(from - 0.50) < 0.01, $"{kind}: alerta a {from:0.000} m da guia (0,50 m)");
            Assert.InRange(to - from, 0.40 - 0.01, 0.60 + 0.01);
            // Na largura do rebaixamento (Figura 22: a faixa de alerta ocupa o fundo plano da rampa).
            if (r.Type != TipoRampa.RebaixamentoTotal)
                Assert.InRange(along, r.Width - 0.10, r.Width + 0.01);
        }
    }

    [Theory]
    [InlineData("travessia")]
    [InlineData("esquina")]
    [InlineData("extensao")]
    public void Item7_8_5b_Figura69_DirecionalNoEixoDaRampaAteOAlerta(string kind)
    {
        foreach (var r in Scene(kind).Where(r => r.Type != TipoRampa.RebaixamentoTotal))
        {
            Assert.True(r.DirectionalTactile, $"{kind}: rampa sem direcional no eixo");
            var path = new Polyline2(r.PathRef.Points);
            var f = RampGenerator.FrameOf(path);
            var geo = RampGenerator.Generate(r, path);
            var bars = geo.Pieces.Where(p => p.Color == MarkingColor.RelevoTatil && p.Shape.Outer.Count == 4)
                .SelectMany(p => p.Shape.Outer).Select(v => v - f.Curb).ToList();
            Assert.NotEmpty(bars);
            // No eixo da rampa (faixa de 0,25 m centrada) e do fim do alerta até o fim da subida.
            Assert.All(bars, b => Assert.InRange(Math.Abs(b.Dot(f.Side)), 0, 0.125));
            var (_, run, _) = RampGenerator.Dimensions(r);
            Assert.InRange(bars.Min(b => b.Dot(f.Up)), r.TactileSetback + r.TactileWidth, r.TactileSetback + r.TactileWidth + 0.05);
            Assert.InRange(bars.Max(b => b.Dot(f.Up)), run - 0.30, run);
        }
    }

    [Fact]
    public void Figura23_RebaixamentoTotal_RampasComplementaresAte5porcento()
    {
        // Via local (calçada de 2,50 m): a interseção faz o rebaixamento total da calçada.
        var w = Y34TipTests.Make(AB2Matrix.Template(), (0, new[] { new Vec2(-80, 0), new Vec2(80, 0) }), (0, new[] { new Vec2(0, -80), new Vec2(0, 80) }));
        var total = w.Defs.OfType<RampDefinition>().Where(r => r.Type == TipoRampa.RebaixamentoTotal).ToList();
        Assert.NotEmpty(total);
        foreach (var r in total)
        {
            var (_, _, flare) = RampGenerator.Dimensions(r);
            var real = (r.Height - Math.Clamp(r.LipHeight, 0, r.Height * 0.5)) / flare;
            Assert.True(real <= 0.05 + 1e-6, $"rampa complementar com {real:P1}");
            var (from, to, _) = AlertSpan(r);
            Assert.Equal(0.50, from, 2);
            Assert.InRange(to - from, 0.40 - 0.01, 0.60 + 0.01);
        }
    }

    [Fact]
    public void Figuras22a26e69_SemAlertaNoTopoDaRampa()
    {
        foreach (var kind in new[] { "travessia", "esquina", "extensao" })
            foreach (var r in Scene(kind).Where(r => r.Type != TipoRampa.RebaixamentoTotal))
            {
                var (from, to, _) = AlertSpan(r);
                var (_, run, _) = RampGenerator.Dimensions(r);
                Assert.True(to <= TactileGenerator.RampAlertSetback + 0.60 + 0.01 && to < run - 0.5, $"{kind}: alerta indo até {to:0.00} m (rampa de {run:0.00} m)");
            }
    }

    /// <summary>
    /// Na topografia: a planta das rampas (e da faixa de alerta a 0,50 m da guia) é a mesma do terreno plano – o relevo só muda
    /// as cotas, pela superfície das marcas do nó.
    /// </summary>
    [Theory]
    [InlineData("ondulado", "cruz", "arterial", "local2")]
    [InlineData("degrau", "T", "coletora4", "local2")]
    [InlineData("crista", "Y", "coletora4", "local2")]
    public void NaTopografia_RampasComAMesmaPlantaEAlertaA050(string terrain, string kind, string a, string b)
    {
        var sc = TopoScene.Make(terrain, kind, a, b, 90);
        var flat = TopoScene.Make("plano", kind, a, b, 90);
        var ramps = sc.World.Defs.OfType<RampDefinition>().OrderBy(r => r.PathRef.Points[0].X).ThenBy(r => r.PathRef.Points[0].Y).ToList();
        var ramps0 = flat.World.Defs.OfType<RampDefinition>().OrderBy(r => r.PathRef.Points[0].X).ThenBy(r => r.PathRef.Points[0].Y).ToList();
        Assert.NotEmpty(ramps);
        Assert.Equal(ramps0.Count, ramps.Count);
        foreach (var (r, r0) in ramps.Zip(ramps0))
        {
            Assert.True(r.PathRef.Points[0].DistanceTo(r0.PathRef.Points[0]) < 1e-3);
            Assert.Equal(r0.Width, r.Width, 6);
            var (from, to, _) = AlertSpan(r);
            Assert.True(Math.Abs(from - 0.50) < 0.01);
            Assert.InRange(to - from, 0.39, 0.61);
        }
        // A rampa fica sobre a superfície das marcas do nó (sem degrau na guia nem no topo): a cota da superfície é contínua ali.
        // A superfície pode ser inclinada – o nó segue a rampa da via principal, até 30 % nestes terrenos –, mas sem salto: a
        // variação de cota entre amostras vizinhas (a cada 0,25 m) muda menos de 2 cm de uma para a outra.
        foreach (var r in ramps)
        {
            var z = TopoScene.SurfaceOf(sc, r);
            if (z == null) continue;
            var f = RampGenerator.FrameOf(new Polyline2(r.PathRef.Points));
            var (_, run, _) = RampGenerator.Dimensions(r);
            double? prev = null;
            for (var u = 0.0; u < run; u += 0.25)
            {
                var dz = z(f.P(0, u + 0.25)) - z(f.P(0, u));
                Assert.True(Math.Abs(dz) < 0.10, "superfície sob a rampa com degrau");
                if (prev is { } pd) Assert.True(Math.Abs(dz - pd) < 0.02, "superfície sob a rampa com degrau");
                prev = dz;
            }
        }
    }
}
