using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 2 do plano – diagnóstico padronizado (Prompt Master §5, §17, §19; Motores §4, §16; Interface §17.2): cada achado com
/// módulo e ação recomendada; o que não pôde ser verificado dito como tal; comparação com a validação anterior (novo,
/// persistente, resolvido); tolerâncias do núcleo num registro único e coerente.
/// </summary>
public class Etapa02DiagnosticoTests
{
    private static List<CopiaGravada> Copias(params MarkingDefinition[] defs) => defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e-" + d.Id)).ToList();

    private static RoadPavementDefinition Via() => new()
    {
        PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(100, 0) }, 0), GroupId = "g",
    };

    [Fact]
    public void Regras_CadaCodigoComModuloEAcao_EAcaoPropriaQuandoHa()
    {
        Assert.Equal(16, ModelValidator.Regras.Count);     // V15 (terraplenagem) na Etapa 5, V16 (parcelamento) na Etapa 7
        Assert.All(ModelValidator.Regras.Values, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Modulo));
            Assert.False(string.IsNullOrWhiteSpace(r.Acao));
            Assert.Equal(r.Verifica, ModelValidator.Codigos[r.Codigo]);
        });
        var a = new Achado("V07", Severidade.Erro, Confianca.Alta, true, "contorno inválido", new[] { "x" });
        Assert.Equal("Geometria", a.Modulo);
        Assert.Equal(ModelValidator.Regras["V07"].Acao, a.AcaoRecomendada);
        Assert.Equal("apagar", (a with { Acao = "apagar" }).AcaoRecomendada);
        Assert.Equal("Outros", new Achado("X99", Severidade.Info, Confianca.Baixa, false, "", Array.Empty<string>()).Modulo);

        // V04 diz a ação de cada tipo de dependência quebrada: detalhe órfão ≠ via inexistente.
        var detalhe = new SignPlanDetailDefinition { SignId = "sumiu" };
        var v04 = Assert.Single(ModelValidator.Validate(Copias(detalhe)), x => x.Codigo == "V04");
        Assert.Contains("detalhe", v04.AcaoRecomendada);
    }

    [Fact]
    public void NaoVerificados_SoQuandoPedidos_EComOQueFalta()
    {
        var via = Via();
        // Sem pedir: o contrato antigo (só problemas).
        Assert.DoesNotContain(ModelValidator.Validate(Copias(via)), x => x.Severidade == Severidade.NaoVerificavel);
        var sem = ModelValidator.Validate(Copias(via), incluirNaoVerificados: true);
        var v12 = Assert.Single(sem, x => x.Severidade == Severidade.NaoVerificavel && x.Codigo == "V12");
        Assert.Contains("1 via(s)", v12.Mensagem);
        Assert.Contains("Configurações", v12.AcaoRecomendada);
        // Com limites informados, o traçado é conferido: não há "não verificado" do V12.
        var limites = new GeometricCriteria { RaioMinimo = 30, Fonte = "projeto" };
        Assert.DoesNotContain(ModelValidator.Validate(Copias(via), criterios: limites, incluirNaoVerificados: true),
            x => x.Severidade == Severidade.NaoVerificavel && x.Codigo == "V12");

        // Rede sem Projeto de Drenagem e com projeto sem chuva.
        var a = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(0, 0), Z = 10 };
        var b = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(40, 0), Z = 9.6 };
        var tubo = new DrainagePipeDefinition { FromId = a.Id, ToId = b.Id, Diameter = 0.4, Z = 10 };
        var semProjeto = ModelValidator.Validate(Copias(a, b, tubo), incluirNaoVerificados: true);
        Assert.Contains(semProjeto, x => x.Severidade == Severidade.NaoVerificavel && x.Codigo == "V13" && x.Mensagem.StartsWith("Sem Projeto de Drenagem"));
        var semChuva = ModelValidator.Validate(Copias(a, b, tubo), drenagem: new ProjetoDrenagem(), incluirNaoVerificados: true);
        Assert.Contains(semChuva, x => x.Severidade == Severidade.NaoVerificavel && x.Codigo == "V13" && x.Mensagem.Contains("equação de chuva"));

        // Sem o documento, os elementos do Revit citados pela marca não são conferidos.
        var ligada = Via();
        ligada.PathRef = PathReference.FromElements(new[] { "linha-1" });
        Assert.Contains(ModelValidator.Validate(Copias(ligada), incluirNaoVerificados: true), x => x.Severidade == Severidade.NaoVerificavel && x.Codigo == "V06");
        Assert.DoesNotContain(ModelValidator.Validate(Copias(ligada), _ => true, incluirNaoVerificados: true), x => x.Severidade == Severidade.NaoVerificavel && x.Codigo == "V06");
    }

    private static Achado A(string codigo, string msg, params string[] ids) => new(codigo, Severidade.Aviso, Confianca.Alta, true, msg, ids);

    [Fact]
    public void Historico_NovoPersistenteResolvido_PelaChaveNaoPelaMensagem()
    {
        var antes = new List<Achado> { A("V09", "sobreposição de 3,2 m²", "q1", "q2"), A("V13", "declividade 0,3 %", "t1"), A("V13", "recobrimento 0,5 m", "t1"),
            new("V12", Severidade.NaoVerificavel, Confianca.Alta, false, "sem limites", Array.Empty<string>()) };
        Assert.Equal(new[] { "V09:q1,q2", "V13:t1", "V13:t1#2", "V12:" }, ValidationHistory.Chaves(antes));
        var gravada = ValidationHistory.Gravar(antes, new DateTime(2026, 10, 26, 9, 0, 0), "2.8.0");
        Assert.Equal(3, gravada.Achados.Count);                     // o "não verificado" não é problema: não entra
        var lida = ValidationHistory.FromJson(ValidationHistory.ToJson(gravada));
        Assert.NotNull(lida);
        Assert.Equal(gravada.Achados, lida!.Achados);
        Assert.Contains("\"Aviso\"", ValidationHistory.ToJson(gravada));
        Assert.Null(ValidationHistory.FromJson("{ não é json"));
        Assert.Null(ValidationHistory.FromJson(""));

        // Primeira validação: tudo é novo e não há "anterior".
        var primeira = ValidationHistory.Comparar(antes, null);
        Assert.Null(primeira.Anterior);
        Assert.Equal(3, primeira.Novos.Count);

        // Corrigida a sobreposição; a declividade mudou de valor (mesmo achado); um achado novo apareceu.
        var depois = new List<Achado> { A("V13", "declividade 0,4 %", "t1"), A("V13", "recobrimento 0,5 m", "t1"), A("V11", "nó sem duas vias", "n1") };
        var c = ValidationHistory.Comparar(depois, lida);
        Assert.Equal(new DateTime(2026, 10, 26, 9, 0, 0), c.Anterior);
        Assert.Equal(new[] { "V11" }, c.Novos.Select(x => x.Codigo));
        Assert.Equal(2, c.Persistentes.Count);
        Assert.Equal("V09", Assert.Single(c.Resolvidos).Codigo);
    }

    [Fact]
    public void Relatorio_ResumoComVersaoModuloAcaoComparacaoENaoVerificado()
    {
        var via = Via();
        var achados = ModelValidator.Validate(Copias(via, new SignPlanDetailDefinition { SignId = "sumiu" }), incluirNaoVerificados: true);
        var anterior = ValidationHistory.Gravar(new List<Achado> { A("V09", "sobreposição", "q1", "q2") }, new DateTime(2026, 10, 25, 18, 0, 0), "2.7.0");
        var cmp = ValidationHistory.Comparar(achados, anterior);
        var txt = ModelValidator.Report(achados, 2, 2, "Projeto X", new DateTime(2026, 10, 26, 10, 30, 0), "2.8.0", cmp);
        Assert.Contains("SinalizaBIM 2.8.0", txt);
        Assert.Contains("Não verificados: 1", txt);
        Assert.Contains("Por módulo: Dependências 1", txt);
        Assert.Contains("Desde a validação de 25/10/2026 18:00: 1 novo(s), 0 persistente(s), 1 resolvido(s).", txt);
        Assert.Contains("[AVISO] [NOVO] V04 · Dependências", txt);
        Assert.Contains("Ação: apagar o detalhe ou refazê-lo sobre outra marca", txt);
        Assert.Contains("Resolvidos desde a validação anterior:", txt);
        Assert.Contains("V12 · Vias (traçado e greide): Limites geométricos não informados", txt);
        foreach (var k in ModelValidator.Codigos.Keys) Assert.Contains(k + " –", txt);
        Assert.Contains("Primeira validação registrada", ModelValidator.Report(achados, 2, 2, "P", DateTime.Now, null, ValidationHistory.Comparar(achados, null)));
    }

    [Fact]
    public void Tolerancias_RegistroUnicoECoerente()
    {
        var t = Tolerancias.Todas;
        Assert.Equal(t.Count, t.Select(x => x.Nome).Distinct().Count());
        Assert.All(t, x => Assert.True(x.Valor > 0 && !string.IsNullOrWhiteSpace(x.Unidade) && !string.IsNullOrWhiteSpace(x.Uso), x.Nome));
        double V(string nome) => t.Single(x => x.Nome == nome).Valor;
        // A grade das booleanas (0,1 mm) cabe no "mesmo vértice", que cabe no ajuste do contorno (5 mm).
        Assert.True(V("Grade das booleanas") < V("Mesmo vértice entre pisos"));
        Assert.True(V("Mesmo vértice entre pisos") < V("Ajuste do contorno"));
        Assert.True(V("Planos coplanares") < V("Costura dos pisos"));
        Assert.True(V("Terreno sob os pisos") < V("Terreno na borda da seção"));
        Assert.True(V("Comparação numérica") < V("Contorno válido"));
    }
}
