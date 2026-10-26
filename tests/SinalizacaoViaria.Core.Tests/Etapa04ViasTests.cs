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
    // ------------------------------------------------------------------ estaqueamento (D-023)

    [Fact]
    public void Estaqueamento_PadraoIgualAoDeSempre_EEstacaInicial()
    {
        var p = new Estaqueamento();
        Assert.True(p.EhPadrao);
        foreach (var s in new[] { 0, 7.25, 20, 312.4, 1999.99 }) Assert.Equal(AlignmentElements.Estaca(s), p.Rotulo(s));
        var e = new Estaqueamento { Inicio = 2000 };
        Assert.False(e.EhPadrao);
        Assert.Equal("100+0,00", e.Rotulo(0));
        Assert.Equal("101+5,40", e.Rotulo(25.4));
        Assert.Equal(2025.4, e.Ler("101+5,40")!.Value, 9);
        Assert.Equal(2025.4, e.Ler("2025,4")!.Value, 9);
        Assert.Null(e.Ler("abc"));
        Assert.Equal(25.4, e.Distancia(2025.4, 100)!.Value, 9);
        Assert.Null(e.Distancia(1999, 100));                        // antes do início do eixo
        var dez = new Estaqueamento { Intervalo = 10 };
        Assert.Equal("3+2,00", dez.Rotulo(32));
    }

    [Fact]
    public void Equacao_ComLacuna_EOutraQueRepeteEstacas()
    {
        var e = new Estaqueamento();
        Assert.Empty(e.LerEquacoes("15+12,40 = 16+0,00; 40+0,00 = 39+15,00", 1000));
        Assert.Equal(2, e.Equacoes.Count);
        Assert.Equal(312.4, e.Equacoes[0].Distancia, 9);
        Assert.Equal("15+12,39", e.Rotulo(312.39));              // atrás da equação
        Assert.Equal("16+0,00", e.Rotulo(312.4));                // na equação vale a estaca à frente
        Assert.Equal("20+7,60", e.Rotulo(400));
        Assert.Null(e.Distancia(315, 1000));                     // 15+15,00 caiu na lacuna da primeira equação
        Assert.Equal(400, e.Distancia(407.6, 1000)!.Value, 9);
        // A segunda volta 5 m (40+0,00 → 39+15,00): estacas repetidas, lidas no primeiro trecho, e isso é dito.
        Assert.Equal(792.4, e.Equacoes[1].Distancia, 9);
        Assert.Equal(789.4, e.Distancia(797, 1000)!.Value, 9);
        Assert.Contains(e.Problemas(1000), x => x.Contains("repete estacas"));
        Assert.Equal("15+12,40 = 16+0,00; 40+0,00 = 39+15,00", e.EquacoesTexto());
        // Ida e volta pelo texto.
        var f = new Estaqueamento();
        Assert.Empty(f.LerEquacoes(e.EquacoesTexto(), 1000));
        Assert.Equal(e.Equacoes.Select(x => (Math.Round(x.Distancia, 6), x.Frente)), f.Equacoes.Select(x => (Math.Round(x.Distancia, 6), x.Frente)));
    }

    [Fact]
    public void Estaqueamento_ProblemasDitosAntesDeAplicar()
    {
        var e = new Estaqueamento();
        Assert.Contains(e.LerEquacoes("15+12,40 16+0,00", 1000), x => x.Contains("estaca atrás = estaca à frente"));
        Assert.Contains(e.LerEquacoes("30+0,00 = 31+0,00; 10+0,00 = 11+0,00", 1000), x => x.Contains("depois da equação anterior"));
        Assert.Contains(e.LerEquacoes("80+0,00 = 81+0,00", 1000), x => x.Contains("não está no eixo"));
        Assert.Contains(new Estaqueamento { Intervalo = 0 }.Problemas(100), x => x.Contains("intervalo"));
        var fora = new Estaqueamento { Equacoes = { new EquacaoEstaca { Distancia = 150, Frente = 200 } } };
        Assert.Contains(fora.Problemas(100), x => x.Contains("fora do eixo"));
        Assert.Empty(new Estaqueamento { Inicio = 500, Equacoes = { new EquacaoEstaca { Distancia = 50, Frente = 600 } } }.Problemas(100));
    }

    [Fact]
    public void Relatorios_UsamOEstaqueamentoDaVia_EAVersaoAntigaAbreIgual()
    {
        var eixo = new Polyline2(new[] { new Vec2(0, 0), new Vec2(100, 0) });
        var est = new Estaqueamento { Inicio = 2000 };
        var padrao = AlignmentElements.AxisCsv(AlignmentElements.FromAxis(eixo));
        Assert.Contains("Início (m);Fim (m)", padrao);
        Assert.Contains("0+0,00", padrao);
        var proprio = AlignmentElements.AxisCsv(AlignmentElements.FromAxis(eixo), est);
        Assert.Contains("100+0,00;105+0,00", proprio);
        Assert.Contains("Início (m, distância no eixo)", proprio);
        var v = new Violacao("Curva 1", 312.4, 400, "raio", 50, 80, "m") { Estacas = new Estaqueamento { Equacoes = { new EquacaoEstaca { Distancia = 312.4, Frente = 320 } } } };
        Assert.Contains("estacas 16+0,00 a 20+7,60", v.Texto);
        Assert.Contains("estacas 15+12,40 a 20+0,00", (v with { Estacas = null }).Texto);

        var via = new RoadPavementDefinition { Estaqueamento = new Estaqueamento { Inicio = 2000, Intervalo = 10, Equacoes = { new EquacaoEstaca { Distancia = 50, Frente = 2100 } } } };
        var lida = (RoadPavementDefinition)MarkingDefinition.FromJson(via.ToJson())!;
        Assert.Equal(2000, lida.Estaqueamento!.Inicio);
        Assert.Equal(10, lida.Estaqueamento.Intervalo);
        Assert.Equal(2100, Assert.Single(lida.Estaqueamento.Equacoes).Frente);
        Assert.Null(((RoadPavementDefinition)MarkingDefinition.FromJson(new RoadPavementDefinition().ToJson())!).Estaqueamento);
    }
}
