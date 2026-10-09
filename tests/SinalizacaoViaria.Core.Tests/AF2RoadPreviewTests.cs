using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using Xunit;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada 6, item 8: pré-visualização ao desenhar o eixo. A prévia é a própria via gerada sobre o eixo concordado (mesmo raio
/// e mesma rotina da criação) – os contornos de pista, meio-fio, calçada, canteiro e linhas são os mesmos da via criada.
/// </summary>
public class AF2RoadPreviewTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly Vec2[] Clicks = { new(0, 0), new(80, 0), new(150, 55), new(150, 120) };

    /// <summary>Classificação independente da usada pela prévia (pelo código da marca gerada).</summary>
    private static ClassePrevia? Expected(MarkingDefinition d, MarkingPiece p) => d switch
    {
        RoadPavementDefinition => ClassePrevia.Pista,
        LinearMarkingDefinition l when l.Code.StartsWith("MEIO-FIO") || l.Code.StartsWith("SARJETA") || p.Layer?.StartsWith("MEIO-FIO") == true => ClassePrevia.MeioFio,
        LinearMarkingDefinition { Code: "CALCADA" or "PASSEIO" or "PLATAFORMA" } => ClassePrevia.Calcada,
        LinearMarkingDefinition { Code: "GRAMADO" } => ClassePrevia.Canteiro,
        LinearMarkingDefinition l when !RoadSectionInference.IsPhysical(l.Code) && p.Color is MarkingColor.Branca or MarkingColor.Amarela => ClassePrevia.Linha,
        _ => null,
    };

    /// <summary>A via como o comando cria: eixo concordado (linhas e arcos), seção gerada sobre ele e cada marca desenhada.</summary>
    private static List<(ClassePrevia C, MarkingPiece P)> Generated(RoadSetup setup, IReadOnlyList<Vec2> clicks, double radius)
    {
        var pts = RoadConnection.Densify(RoadConnection.Fillet(clicks, radius), 1.0);
        var axis = new Polyline2(pts);
        var defs = setup.Clone().Build(PathReference.FromPoints(pts, 0), new OutputSettings(), Cat, axis: axis);
        var ctx = new BuildContext { Catalog = Cat };
        var res = new List<(ClassePrevia, MarkingPiece)>();
        foreach (var d in defs)
            foreach (var p in MarkingBuilder.Build(d, axis, ctx).Pieces)
                if (Expected(d, p) is { } c) res.Add((c, p));
        return res;
    }

    private static void SameOutlines(IReadOnlyList<Polygon2> a, IReadOnlyList<Polygon2> b)
    {
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Outer.Count, b[i].Outer.Count);
            for (int k = 0; k < a[i].Outer.Count; k++) Assert.True(a[i].Outer[k].DistanceTo(b[i].Outer[k]) < 1e-9);
            Assert.Equal(a[i].Holes.Count, b[i].Holes.Count);
            Assert.Equal(a[i].Area, b[i].Area, 6);
        }
    }

    [Fact]
    public void Previa_tem_os_mesmos_contornos_da_via_gerada()
    {
        var setup = RoadTemplates.All[2].Create();   // avenida com canteiro central, calçadas e linhas
        var radius = Math.Max(30, RoadSetup.MinAxisRadius(setup.MaxHalfWidth));
        var previa = RoadPreview.Build(setup, Clicks, radius, Cat)!;
        Assert.NotNull(previa);
        var axis = RoadConnection.Densify(RoadConnection.Fillet(Clicks, radius), 1.0);
        Assert.Equal(axis.Count, previa.Eixo.Count);
        Assert.All(axis.Zip(previa.Eixo), p => Assert.True(p.First.DistanceTo(p.Second) < 1e-12));

        var gen = Generated(setup, Clicks, radius);
        foreach (var c in Enum.GetValues<ClassePrevia>())
        {
            var want = gen.Where(x => x.C == c).Select(x => x.P.Shape).ToList();
            var got = previa.Da(c).Select(x => x.Contorno).ToList();
            Assert.True(want.Count > 0, $"a via gerada não tem peças de {c}");
            SameOutlines(want, got);
        }
        // Cores da via gerada (pavimento, concreto, grama, tinta).
        Assert.All(previa.Da(ClassePrevia.Pista), p => Assert.Equal(MarkingColor.Asfalto, p.Cor));
        Assert.Contains(previa.Da(ClassePrevia.Linha), p => p.Cor == MarkingColor.Amarela || p.Cor == MarkingColor.Branca);
    }

    [Fact]
    public void Previa_da_pista_usa_a_mesma_geracao_do_comando()
    {
        // Pista (só pavimento + linhas), pela mesma função de geração que o comando Pista passa à prévia.
        var tpl = new RoadPavementDefinition { RightWidth = 3.5, LeftWidth = 3.5, TwoWay = true };
        IEnumerable<MarkingDefinition> Gen(PathReference pr, Polyline2 ax) => RoadConnection.BuildCarriageway(tpl, pr, new OutputSettings(), "LFO-2", true, 40);
        var ctx = new BuildContext { Catalog = Cat };
        var previa = RoadPreview.Build(Clicks.Take(3).ToList(), 25, Gen, ctx)!;
        var pts = RoadConnection.Densify(RoadConnection.Fillet(Clicks.Take(3).ToList(), 25), 1.0);
        var axis = new Polyline2(pts);
        var want = Gen(PathReference.FromPoints(pts, 0), axis).SelectMany(d => MarkingBuilder.Build(d, axis, ctx).Pieces.Select(p => (d, p)))
            .Where(x => Expected(x.d, x.p) is ClassePrevia.Pista).Select(x => x.p.Shape).ToList();
        SameOutlines(want, previa.Da(ClassePrevia.Pista).Select(p => p.Contorno).ToList());
        Assert.NotEmpty(previa.Da(ClassePrevia.Linha));
    }

    [Fact]
    public void Previa_cresce_a_cada_clique_e_precisa_de_dois_pontos()
    {
        var setup = RoadTemplates.All[0].Create();
        Assert.Null(RoadPreview.Build(setup, Clicks.Take(1).ToList(), 20, Cat));
        double last = 0;
        for (int n = 2; n <= Clicks.Length; n++)
        {
            var p = RoadPreview.Build(setup, Clicks.Take(n).ToList(), 20, Cat)!;
            var area = p.Da(ClassePrevia.Pista).Sum(x => x.Contorno.Area);
            Assert.True(area > last + 100, $"{n} pontos: {area:0} m² (antes {last:0} m²)");
            last = area;
            // A ponta da prévia é o último clique (o trecho até ele já aparece).
            Assert.True(p.Eixo[^1].DistanceTo(Clicks[n - 1]) < 1e-9);
        }
    }
}
