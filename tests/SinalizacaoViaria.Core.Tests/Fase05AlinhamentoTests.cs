using System.Text.Json;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Serialization;
using SinalizacaoViaria.Core.Settings;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Fase V – limites geométricos do traçado e do greide informados pelo usuário (K8): sem valores normativos no plugin, campo
/// vazio não é conferido, a fonte sai no relatório como foi escrita.
/// </summary>
public class Fase05AlinhamentoTests
{
    /// <summary>Eixo com uma curva de R = 20 m (concordância do próprio plugin), uma tangente de 10 m e outra curva de R = 20 m.</summary>
    private static Polyline2 TwoCurves() =>
        new(CurveTools.FilletCorners(new List<Vec2> { new(0, 0), new(60, 0), new(80, 20), new(140, 20) }, 20));

    [Fact]
    public void SemLimites_NadaConferido()
    {
        var c = new GeometricCriteria();
        Assert.True(c.IsEmpty);
        Assert.Empty(GeometricCheck.Axis(AlignmentElements.FromAxis(TwoCurves()), c));
        var grade = new RoadGrade { Points = { new(0, 0), new(100, 12), new(200, 0) } };
        Assert.Empty(GeometricCheck.Vertical(AlignmentElements.Vertical(grade), c));
        // Só a fonte, sem nenhum limite: continua vazio.
        Assert.True(new GeometricCriteria { Fonte = "qualquer" }.IsEmpty);
    }

    [Fact]
    public void Eixo_RaioETangenteAbaixoDoInformado()
    {
        var els = AlignmentElements.FromAxis(TwoCurves());
        var curvas = els.Where(e => e.Tipo == TipoElementoEixo.Curva).ToList();
        Assert.Equal(2, curvas.Count);
        Assert.All(curvas, e => Assert.InRange(e.Raio, 19.5, 20.5));
        var tangente = els.Single(e => e.Tipo == TipoElementoEixo.Tangente && e.Inicio > curvas[0].Inicio && e.Fim < curvas[1].Fim);

        var v = GeometricCheck.Axis(els, new GeometricCriteria { RaioMinimo = 30, TangenteMinima = tangente.Comprimento + 5 });
        Assert.Equal(2, v.Count(x => x.Criterio == "raio" && x.Limite == 30));
        var t = Assert.Single(v, x => x.Criterio == "comprimento");
        Assert.Equal(tangente.Comprimento, t.Valor, 6);
        // Dentro dos limites: nada.
        Assert.Empty(GeometricCheck.Axis(els, new GeometricCriteria { RaioMinimo = 15, TangenteMinima = tangente.Comprimento - 1 }));
    }

    [Fact]
    public void Eixo_VerticeSemCurva()
    {
        // Canto vivo de 5° (abaixo do ângulo em que o plugin arredonda os cantos).
        var a = 5 * Math.PI / 180;
        var axis = new Polyline2(new List<Vec2> { new(0, 0), new(50, 0), new(50 + 50 * Math.Cos(a), 50 * Math.Sin(a)) });
        var els = AlignmentElements.FromAxis(axis);
        Assert.Contains(els, e => e.Tipo == TipoElementoEixo.Vertice);
        // Com raio mínimo e sem deflexão máxima: o vértice conta como raio zero.
        var v = Assert.Single(GeometricCheck.Axis(els, new GeometricCriteria { RaioMinimo = 30 }));
        Assert.Equal("deflexão", v.Criterio);
        Assert.Equal(5, v.Valor, 3);
        // Deflexão máxima sem curva informada: 5° passa com 6°, não com 4°.
        Assert.Empty(GeometricCheck.Axis(els, new GeometricCriteria { RaioMinimo = 30, DeflexaoMaximaSemCurva = 6 }));
        Assert.Single(GeometricCheck.Axis(els, new GeometricCriteria { DeflexaoMaximaSemCurva = 4 }));
    }

    [Fact]
    public void Greide_RampasECurvasVerticais()
    {
        // 5 % de subida, −1 % (convexa, A = −6 %), +0,5 % (côncava, A = +1,5 %).
        var grade = new RoadGrade { DefaultCurve = 60, Points = { new(0, 0), new(100, 5), new(200, 4), new(300, 4.5) } };
        var els = AlignmentElements.Vertical(grade);
        var convexa = els[1];
        var concava = els[2];
        Assert.True(convexa.Convexa);
        Assert.False(concava.Convexa);
        var c = new GeometricCriteria
        {
            RampaMaxima = 4, RampaMinima = 0.6, KMinimoConvexa = convexa.K!.Value + 1, KMinimoConcava = concava.K!.Value - 1,
            CurvaVerticalMinima = concava.Comprimento + 1,
        };
        var v = GeometricCheck.Vertical(els, c);
        // Rampa de 5 % acima de 4 %; rampa de 0,5 % abaixo de 0,6 %; a de 1 % passa.
        Assert.Single(v, x => x.Criterio == "rampa" && x.Valor == 5 && x.Limite == 4);
        Assert.Single(v, x => x.Criterio == "rampa" && Math.Abs(x.Valor - 0.5) < 1e-9 && x.Limite == 0.6);
        Assert.DoesNotContain(v, x => x.Criterio == "rampa" && Math.Abs(x.Valor - 1) < 1e-9);
        // K da convexa abaixo do informado; o da côncava acima.
        var k = Assert.Single(v, x => x.Criterio == "K");
        Assert.Contains("convexa", k.Elemento);
        Assert.Equal(convexa.EstacaPcv!.Value, k.Inicio, 6);
        // Curvas mais curtas que o comprimento mínimo.
        Assert.Contains(v, x => x.Criterio == "comprimento da curva" && x.Elemento.Contains("côncava"));
    }

    [Fact]
    public void Relatorio_TrazAFonteComoFoiEscrita()
    {
        var vs = new List<Violacao> { new("Curva 1", 10, 30, "raio", 20, 30, "m") };
        var csv = GeometricCheck.Csv(vs, new GeometricCriteria { Fonte = "Norma X; tabela 5", RaioMinimo = 30 });
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Norma X, tabela 5", lines[0]);
        Assert.StartsWith("Elemento;", lines[1]);
        Assert.Contains("Curva 1;0+10,00;1+10,00;raio;20;30;m", lines[2]);
        Assert.Contains("fonte não informada", GeometricCheck.Csv(vs, new GeometricCriteria { RaioMinimo = 30 }));
        Assert.Contains("1 elemento(s) fora", GeometricCheck.Summary(vs, new GeometricCriteria { RaioMinimo = 30 }));
    }

    [Fact]
    public void ValidarModelo_V12SoComLimitesInformados()
    {
        var road = new RoadPavementDefinition
        {
            Id = "via-1",
            PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(60, 0), new Vec2(80, 20), new Vec2(140, 20) }, 0),
        };
        road.PathRef.SmoothRadius = 20;
        road.Output.Grade = new RoadGrade { Points = { new(0, 0), new(80, 6), new(160, 6) } };
        var copias = new List<CopiaGravada> { new(road.Id, road.ToJson(), "e1") };
        Assert.DoesNotContain(ModelValidator.Validate(copias), a => a.Codigo == "V12");
        Assert.DoesNotContain(ModelValidator.Validate(copias, null, new GeometricCriteria()), a => a.Codigo == "V12");
        var achado = Assert.Single(ModelValidator.Validate(copias, null, new GeometricCriteria { Fonte = "minha norma", RaioMinimo = 30, RampaMaxima = 6 }),
            a => a.Codigo == "V12");
        Assert.Contains("minha norma", achado.Mensagem);
        Assert.Contains("raio", achado.Mensagem);
        Assert.Contains("rampa 7,5 %", achado.Mensagem);
        Assert.True(achado.RevisaoHumana);
        Assert.True(ModelValidator.Codigos.ContainsKey("V12"));
    }

    [Fact]
    public void Configuracoes_GuardamOsLimites()
    {
        var s = new PluginSettings { CriteriosGeometricos = new GeometricCriteria { Fonte = "F", RaioMinimo = 45, KMinimoConcava = 12.5 } };
        var back = JsonSerializer.Deserialize<PluginSettings>(JsonSerializer.Serialize(s, JsonConfig.Options), JsonConfig.Options)!;
        Assert.Equal("F", back.CriteriosGeometricos.Fonte);
        Assert.Equal(45, back.CriteriosGeometricos.RaioMinimo);
        Assert.Equal(12.5, back.CriteriosGeometricos.KMinimoConcava);
        Assert.Null(back.CriteriosGeometricos.RampaMaxima);
        // Configuração antiga (sem o campo): limites vazios.
        var old = JsonSerializer.Deserialize<PluginSettings>("{}", JsonConfig.Options)!;
        Assert.True(old.CriteriosGeometricos.IsEmpty);
    }
}
