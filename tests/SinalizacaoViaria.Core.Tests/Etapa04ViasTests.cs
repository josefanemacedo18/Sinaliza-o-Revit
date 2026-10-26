using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 4 do plano – vias e interseções: os limites citados nos avisos dos geradores estão num registro único, com a
/// fonte, ou dizem que são espaço do próprio plugin ou conferência sem norma citada (regra: nenhuma norma inventada; Prompt
/// Master §13: sem valores mágicos espalhados).
/// </summary>
public class Etapa04ViasTests
{
    [Fact]
    public void Limites_TodosComOrigemDita_ENenhumSemFontePassaPorNorma()
    {
        var t = LimitesDeConferencia.Todos;
        Assert.Equal(t.Count, t.Select(x => x.Nome).Distinct().Count());
        Assert.All(t, x =>
        {
            Assert.True(x.Valor > 0, x.Nome);
            if (x.Origem == OrigemLimite.Geometrico) Assert.Contains("espaço que o plugin precisa", x.Rotulo);
            else if (x.Fonte == null) Assert.Contains("sem norma citada", x.Rotulo);
            else Assert.Contains(x.Fonte, x.Rotulo);
        });
        Assert.Equal("30°: limite de conferência do plugin, sem norma citada – confira com a norma adotada", LimitesDeConferencia.AnguloMinimoEntreVias.Rotulo);
        Assert.Equal("3 m: espaço que o plugin precisa para desenhar a peça", LimitesDeConferencia.CanteiroParaBolsao.Rotulo);
    }

    [Fact]
    public void Rotatoria_AvisosDizemQueOLimiteNaoTemNorma()
    {
        var mini = new RoundaboutDefinition { Center = Vec2.Zero, Type = TipoRotatoria.Mini, IslandRadius = 12, Lanes = 1, LaneWidth = 3.5 };
        mini.Legs.Add(new RoundaboutLeg { AngleDeg = 0, Width = 7 });
        mini.Legs.Add(new RoundaboutLeg { AngleDeg = 180, Width = 7 });
        var w = RoundaboutGenerator.Layout(mini).Warnings;
        var diam = Assert.Single(w, x => x.StartsWith("Minirrotatória com diâmetro inscrito"));
        Assert.Contains("acima de 28 m: limite de conferência do plugin, sem norma citada", diam);
        Assert.DoesNotContain(w, x => x.Contains("13 a 25"));
        var pista = Assert.Single(w, x => x.StartsWith("Pista giratória de 3"));      // 3,50 m (o número segue a cultura do Windows)
        Assert.Contains("abaixo de 4 m: limite de conferência do plugin, sem norma citada", pista);
    }

    [Fact]
    public void Canalizacao_TransicaoJuntoAObstaculo_SemNormaCitada()
    {
        var d = new ChannelizationDefinition { Type = TipoCanalizacao.Obstaculo, Length = 12, Rural = false };
        var path = new Polyline2(new[] { new Vec2(0, 0), new Vec2(200, 0) });
        var w = ChannelizationGenerator.Generate(d, path, new BuildContext { Catalog = CatalogService.LoadDefault() }).Warnings;
        var a = Assert.Single(w, x => x.StartsWith("Transição de 12 m junto a obstáculo"));
        Assert.Contains("abaixo de 30 m: limite de conferência do plugin, sem norma citada", a);
    }
}
