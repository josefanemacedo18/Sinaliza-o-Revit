using System.Text.Json.Nodes;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// EV (estabilização): leitura versionada das definições, grafo de dependências, validação do modelo e validade de
/// contornos – tudo sem o Revit.
/// </summary>
public class EVModelIntegrityTests
{
    private static RoadPavementDefinition Road(string id) => new()
    {
        Id = id,
        PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(100, 0) }, 0),
    };

    private static LabelDefinition Label(string id, string target) => new() { Id = id, MarkingTargetId = target };

    private static IntersectionDefinition Node(string id, params string[] roads)
    {
        var i = new IntersectionDefinition { Id = id };
        i.RoadIds.AddRange(roads);
        return i;
    }

    private static List<CopiaGravada> Copies(params MarkingDefinition[] defs) =>
        defs.Select((d, k) => new CopiaGravada(d.Id, d.ToJson(), $"e{k}")).ToList();

    private static List<Vec2> Ring(params double[] xy) =>
        Enumerable.Range(0, xy.Length / 2).Select(i => new Vec2(xy[2 * i], xy[2 * i + 1])).ToList();

    // ------------------------------------------------------------------ versões e migrações

    [Fact]
    public void Versao_AtualELidaComoEsta()
    {
        var r = Road("a");
        var lida = DefinitionVersioning.Load(r.ToJson());
        Assert.Equal(EstadoVersao.Atual, lida.Estado);
        Assert.Equal(MarkingDefinition.CurrentVersion, lida.StoredVersion);
        Assert.Equal(r.ToJson(), lida.Definition!.ToJson());
    }

    [Fact]
    public void Versao_MaisNova_ELidaMasMarcadaParaNaoRegravar()
    {
        var node = JsonNode.Parse(Road("a").ToJson())!.AsObject();
        node["version"] = 99;
        node["campoDoFuturo"] = "x";
        var lida = DefinitionVersioning.Load(node.ToJsonString());
        Assert.Equal(EstadoVersao.MaisNova, lida.Estado);
        Assert.Equal(99, lida.Definition!.Version);
        Assert.True(DefinitionVersioning.IsNewer(lida.Definition));
        Assert.Contains("v99", DefinitionVersioning.NewerMessage(lida.Definition));
    }

    [Fact]
    public void Versao_SemCampo_ContaComoVersao1()
    {
        var node = JsonNode.Parse(Road("a").ToJson())!.AsObject();
        node.Remove("version");
        var lida = DefinitionVersioning.Load(node.ToJsonString());
        Assert.Equal(1, lida.StoredVersion);
        Assert.NotNull(lida.Definition);
        Assert.False(DefinitionVersioning.IsNewer(lida.Definition!));
    }

    [Fact]
    public void Migracao_AplicaOsPassosEmOrdemAteAVersaoAtual()
    {
        var node = JsonNode.Parse(Road("a").ToJson())!.AsObject();
        node["version"] = 1;
        node["obsAntiga"] = "nota de v1";
        var log = new List<int>();
        var passos = new Dictionary<int, Action<JsonObject>>
        {
            // v1 → v2: campo renomeado; v2 → v3: só registra (a ordem importa).
            [1] = o => { log.Add(1); o["notes"] = o["obsAntiga"]!.GetValue<string>(); o.Remove("obsAntiga"); },
            [2] = o => { log.Add(2); o["notes"] = o["notes"]!.GetValue<string>() + " (v3)"; },
        };
        var lida = DefinitionVersioning.Load(node.ToJsonString(), passos, atual: 3);
        Assert.Equal(EstadoVersao.Migrada, lida.Estado);
        Assert.Equal(new[] { 1, 2 }, log);
        Assert.Equal(3, lida.Definition!.Version);
        Assert.Equal("nota de v1 (v3)", lida.Definition.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ quebrado")]
    [InlineData("[1, 2]")]
    [InlineData("{\"tipo\":\"tipo-que-nao-existe\",\"id\":\"x\"}")]
    public void Versao_DadosIlegiveis_NaoLancamExcecao(string json)
    {
        var lida = DefinitionVersioning.Load(json);
        Assert.Equal(EstadoVersao.Invalida, lida.Estado);
        Assert.Null(lida.Definition);
        Assert.False(string.IsNullOrEmpty(lida.Erro));
    }

    [Fact]
    public void Versao_TodosOsTiposConhecidosFazemIdaEVolta()
    {
        // Cada tipo registrado no discriminador: o JSON gravado é lido de volta como o mesmo tipo e o mesmo conteúdo.
        var types = typeof(MarkingDefinition).GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonDerivedTypeAttribute), false)
            .Cast<System.Text.Json.Serialization.JsonDerivedTypeAttribute>().Select(a => a.DerivedType).ToList();
        Assert.True(types.Count > 30);
        foreach (var t in types)
        {
            var d = (MarkingDefinition)Activator.CreateInstance(t)!;
            var lida = DefinitionVersioning.Load(d.ToJson());
            Assert.True(lida.Estado == EstadoVersao.Atual, $"{t.Name}: {lida.Erro}");
            Assert.IsType(t, lida.Definition);
            Assert.Equal(d.ToJson(), lida.Definition!.ToJson());
        }
    }

    // ------------------------------------------------------------------ grafo de dependências

    [Fact]
    public void Grafo_ReferenciasDeInterseccaoEFilhas()
    {
        var n = Node("n", "r1", "r2");
        n.MainRoadId = "r1";
        n.ChildIds.Add("placa");
        var refs = ModelGraph.References(n).ToList();
        Assert.Equal(3, refs.Count);          // r1 (uma vez, mesmo citada duas vezes), r2, placa
        var filha = refs.Single(e => e.Tipo == TipoDependencia.Filha);
        Assert.Equal("placa", filha.Dependente);
        Assert.Equal("n", filha.Referenciado);
    }

    [Fact]
    public void Grafo_OrdemDeRegeneracao_ReferenciadoAntes()
    {
        var g = new ModelGraph(new MarkingDefinition[]
        {
            Label("anot", "n"), Node("n", "r1", "r2"), Road("r2"), Road("r1"),
        });
        var order = g.RegenerationOrder(out var cyc);
        Assert.Empty(cyc);
        Assert.Equal(4, order.Count);
        Assert.True(order.IndexOf("r1") < order.IndexOf("n"));
        Assert.True(order.IndexOf("r2") < order.IndexOf("n"));
        Assert.True(order.IndexOf("n") < order.IndexOf("anot"));
        Assert.Equal(new[] { "n", "anot" }, g.Dependents("r1"));
        Assert.Equal(new[] { "n" }, g.Dependents("r1", transitive: false));
    }

    [Fact]
    public void Grafo_CicloDetectadoEFicaForaDaOrdem()
    {
        var g = new ModelGraph(new MarkingDefinition[] { Label("a", "b"), Label("b", "c"), Label("c", "a"), Road("r"), Label("x", "r") });
        var cycles = g.Cycles();
        Assert.Single(cycles);
        Assert.Equal(new[] { "a", "b", "c" }, cycles[0]);
        var order = g.RegenerationOrder(out var cyc);
        Assert.Equal(new[] { "a", "b", "c" }, cyc);
        Assert.Equal(new[] { "r", "x" }, order);
    }

    [Fact]
    public void Grafo_RecorteNaoFormaCiclo()
    {
        // A interseção cita a via e a via guarda o recorte feito pela interseção: referência fraca, sem ciclo.
        var r = Road("r");
        r.Exclusions.Add(new ExclusionZone { SourceId = "n", Points = Ring(0, 0, 1, 0, 1, 1) });
        var g = new ModelGraph(new MarkingDefinition[] { r, Node("n", "r") });
        Assert.Empty(g.Cycles());
        Assert.Contains(g.Edges, e => e.Tipo == TipoDependencia.Recorte && !e.Forte);
        Assert.Equal(new[] { "r", "n" }, g.RegenerationOrder(out _));
    }

    [Fact]
    public void Grafo_ReferenciasQuebradas()
    {
        var g = new ModelGraph(new MarkingDefinition[] { Node("n", "r1", "sumiu"), Road("r1"), Label("l", "outra") });
        var broken = g.Broken.Select(e => (e.Origem, e.Destino)).OrderBy(x => x.Origem).ToList();
        Assert.Equal(new[] { ("l", "outra"), ("n", "sumiu") }, broken);
    }

    // ------------------------------------------------------------------ validação do modelo

    [Fact]
    public void Validacao_ModeloConsistente_SemAchados()
    {
        var road = Road("r1");
        road.PathRef = PathReference.FromElements(new[] { "linha-1" });
        var res = ModelValidator.Validate(Copies(road, Road("r2"), Node("n", "r1", "r2"), Label("l", "n")), uid => uid == "linha-1");
        Assert.Empty(res);
    }

    [Fact]
    public void Validacao_CopiasDivergentes()
    {
        var a = Road("r");
        var b = Road("r");
        b.RightWidth = 5;
        var res = ModelValidator.Validate(Copies(a, b));
        var v = Assert.Single(res);
        Assert.Equal("V03", v.Codigo);
        Assert.True(v.RevisaoHumana);
        // Cópias iguais (uma por cor) não são problema.
        Assert.Empty(ModelValidator.Validate(Copies(a, Road("r"))));
    }

    [Fact]
    public void Validacao_DadosIlegiveisEVersaoMaisNova()
    {
        var node = JsonNode.Parse(Road("novo").ToJson())!.AsObject();
        node["version"] = MarkingDefinition.CurrentVersion + 1;
        var copias = new List<CopiaGravada>
        {
            new("x", "{ quebrado", "e1"),
            new("novo", node.ToJsonString(), "e2"),
        };
        var res = ModelValidator.Validate(copias);
        Assert.Contains(res, a => a.Codigo == "V01" && a.Severidade == Severidade.Erro && a.Ids.Contains("x"));
        Assert.Contains(res, a => a.Codigo == "V02" && a.Severidade == Severidade.Aviso && a.Ids.Contains("novo"));
    }

    [Fact]
    public void Validacao_ReferenciaQuebradaECiclo()
    {
        var res = ModelValidator.Validate(Copies(Label("a", "b"), Label("b", "a"), Label("orfa", "apagada"), Node("n", "via-apagada")));
        Assert.Contains(res, a => a.Codigo == "V05" && a.Severidade == Severidade.Erro && a.Ids.Count == 2);
        Assert.Contains(res, a => a.Codigo == "V04" && a.Ids.SequenceEqual(new[] { "orfa", "apagada" }) && a.RevisaoHumana);
        Assert.Contains(res, a => a.Codigo == "V04" && a.Ids.SequenceEqual(new[] { "n", "via-apagada" }));
        // Ordem do relatório: erros primeiro.
        Assert.Equal(Severidade.Erro, res[0].Severidade);
    }

    [Fact]
    public void Validacao_LinhaDeReferenciaApagada_ComESemTracadoGuardado()
    {
        var comCache = Road("c");
        comCache.PathRef = PathReference.FromElements(new[] { "linha-apagada" });
        comCache.PathRef.Cache = new List<List<Vec2>> { new() { new Vec2(0, 0), new Vec2(10, 0) } };
        var semCache = Road("s");
        semCache.PathRef = PathReference.FromElements(new[] { "outra-apagada" });
        var res = ModelValidator.Validate(Copies(comCache, semCache), _ => false);
        Assert.Contains(res, a => a.Codigo == "V06" && a.Severidade == Severidade.Info && a.Ids.Contains("c"));
        Assert.Contains(res, a => a.Codigo == "V06" && a.Severidade == Severidade.Aviso && a.Ids.Contains("s"));
        // Sem acesso ao documento (nulo), elementos não são verificados.
        Assert.DoesNotContain(ModelValidator.Validate(Copies(comCache, semCache)), a => a.Codigo == "V06");
    }

    [Fact]
    public void Validacao_QuadraEmGravataEGlebaValida()
    {
        var lot = new LoteamentoDefinition { Id = "lot", Gleba = Ring(0, 0, 100, 0, 100, 100, 0, 100) };
        var ok = new QuadraDefinition { Id = "qa", LoteamentoId = "lot", Contorno = Ring(10, 10, 40, 10, 40, 40, 10, 40) };
        var gravata = new QuadraDefinition { Id = "qb", LoteamentoId = "lot", Nome = "B", Contorno = Ring(50, 10, 80, 40, 80, 10, 50, 40) };
        var res = ModelValidator.Validate(Copies(lot, ok, gravata));
        var v = Assert.Single(res);
        Assert.Equal("V07", v.Codigo);
        Assert.Equal("qb", v.Ids[0]);
        Assert.Contains("se cruzam", v.Mensagem);
    }

    [Fact]
    public void Validacao_QuadrasSobrepostasEForaDaGleba()
    {
        var lot = new LoteamentoDefinition { Id = "lot", Nome = "Jardim", Gleba = Ring(0, 0, 100, 0, 100, 100, 0, 100) };
        var a = new QuadraDefinition { Id = "qa", LoteamentoId = "lot", Nome = "A", Contorno = Ring(10, 10, 50, 10, 50, 50, 10, 50) };
        var b = new QuadraDefinition { Id = "qb", LoteamentoId = "lot", Nome = "B", Contorno = Ring(40, 40, 80, 40, 80, 80, 40, 80) };
        // Encostada em A (lado comum) não é sobreposição; C passa da gleba em 10 × 20 m.
        var c = new QuadraDefinition { Id = "qc", LoteamentoId = "lot", Nome = "C", Contorno = Ring(50, 10, 110, 10, 110, 30, 50, 30) };
        var res = ModelValidator.Validate(Copies(lot, a, b, c));
        var v09 = Assert.Single(res, x => x.Codigo == "V09");
        Assert.Equal(new[] { "qa", "qb" }, v09.Ids);
        Assert.Contains("100 m²", v09.Mensagem);
        Assert.Equal(Severidade.Erro, v09.Severidade);
        var v10 = Assert.Single(res, x => x.Codigo == "V10");
        Assert.Equal("qc", v10.Ids[0]);
        Assert.Contains("200 m²", v10.Mensagem);
        Assert.Equal(Confianca.Media, v10.Confianca);
        // Quadras de outro loteamento não se comparam com estas.
        var outro = new QuadraDefinition { Id = "qx", LoteamentoId = "outro", Nome = "X", Contorno = a.Contorno };
        Assert.Single(ModelValidator.Validate(Copies(lot, a, b, c, outro)), x => x.Codigo == "V09");
    }

    [Fact]
    public void Validacao_IntersecaoSemDuasVias()
    {
        Assert.DoesNotContain(ModelValidator.Validate(Copies(Road("r1"), Road("r2"), Node("n", "r1", "r2"))), a => a.Codigo == "V11");
        var res = ModelValidator.Validate(Copies(Road("r1"), Node("n", "r1", "r2-apagada")));
        Assert.Contains(res, a => a.Codigo == "V11" && a.Ids.Contains("n"));
        // Via que passa duas vezes pelo mesmo nó (laço) conta como duas.
        Assert.DoesNotContain(ModelValidator.Validate(Copies(Road("r1"), Node("n", "r1", "r1"))), a => a.Codigo == "V11");
    }

    [Fact]
    public void Validacao_CorteSoDeDadosSemPerfil()
    {
        var sec = new SectionDimensionDefinition { Id = "sec", Profile = true, PlanChain = false };
        Assert.Contains(ModelValidator.Validate(Copies(sec)), a => a.Codigo == "V08");
        var perfil = new SectionProfileDefinition { Id = "p", SectionId = "sec" };
        Assert.DoesNotContain(ModelValidator.Validate(Copies(sec, perfil)), a => a.Codigo == "V08");
    }

    [Fact]
    public void Validacao_Relatorio()
    {
        var res = ModelValidator.Validate(Copies(Label("orfa", "apagada")));
        var txt = ModelValidator.Report(res, 1, 1, "Projeto X", new DateTime(2026, 10, 19, 10, 0, 0));
        Assert.Contains("VALIDAÇÃO DO MODELO – Projeto X", txt);
        Assert.Contains("nada foi alterado", txt);
        Assert.Contains("Avisos: 1", txt);
        Assert.Contains("[AVISO] V04", txt);
        foreach (var k in ModelValidator.Codigos.Keys) Assert.Contains(k + " –", txt);
        Assert.Contains("Nenhum problema", ModelValidator.Report(new List<Achado>(), 0, 0, "P", DateTime.Now));
    }

    // ------------------------------------------------------------------ contornos

    [Fact]
    public void Contorno_QuadradoECôncavoValidos_NosDoisSentidos()
    {
        var sq = Ring(0, 0, 10, 0, 10, 10, 0, 10);
        Assert.Null(PolygonChecks.Problem(sq));
        Assert.Null(PolygonChecks.Problem(Enumerable.Reverse(sq).ToList()));
        Assert.Null(PolygonChecks.Problem(Ring(0, 0, 10, 0, 10, 4, 4, 4, 4, 10, 0, 10)));      // L
        // Fechado explicitamente (último = primeiro) também vale.
        Assert.Null(PolygonChecks.Problem(Ring(0, 0, 10, 0, 10, 10, 0, 10, 0, 0)));
    }

    [Fact]
    public void Contorno_CasosDegenerados()
    {
        Assert.Contains("vértice", PolygonChecks.Problem(Ring(0, 0, 1, 1))!);
        Assert.Contains("vértice", PolygonChecks.Problem(Ring(0, 0, 0, 0, 0, 0, 1e-9, 0))!);
        Assert.Contains("área nula", PolygonChecks.Problem(Ring(0, 0, 5, 0, 10, 0))!);
        Assert.Contains("NaN", PolygonChecks.Problem(new List<Vec2> { new(0, 0), new(double.NaN, 1), new(1, 1) })!);
        Assert.Equal(2, PolygonChecks.RepeatedVertices(Ring(0, 0, 0, 0, 10, 0, 10, 10, 10, 10, 0, 10)));
    }

    [Fact]
    public void Contorno_GravataEspinhoEToqueNumVertice()
    {
        // Gravata: lados 1 e 3 se cruzam.
        Assert.Equal(new[] { (0, 2) }, PolygonChecks.SelfIntersections(Ring(0, 0, 10, 10, 10, 0, 0, 10)));
        // Espinho: o lado volta sobre o anterior (10,0 → 15,0 → 12,0).
        Assert.NotEmpty(PolygonChecks.SelfIntersections(Ring(0, 0, 15, 0, 12, 0, 10, 10, 0, 10)));
        // Dois quadrados unidos por um vértice no mesmo anel (forma de "8").
        Assert.NotEmpty(PolygonChecks.SelfIntersections(Ring(0, 0, 10, 0, 10, 10, 20, 10, 20, 20, 10, 20, 10, 10, 0, 10)));
        // Vértice a 2 mm de um lado não vizinho: não encosta com a tolerância padrão (1 µm).
        Assert.Empty(PolygonChecks.SelfIntersections(Ring(0, 0, 10, 0, 10, 10, 5, 0.002, 0, 10)));
        Assert.Null(PolygonChecks.Problem(Ring(0, 0, 10, 0, 10, 10, 5, 0.002, 0, 10)));
    }

    [Fact]
    public void Contorno_ToleranciaEmMetros_IndependeDoComprimentoDoLado()
    {
        // Vértice a 0,5 mm de um lado longo (1 km): toca com tolerância de 1 mm, não toca com 0,1 mm.
        var ring = Ring(0, 0, 1000, 0, 1000, 10, 500, 0.0005, 0, 10);
        Assert.NotEmpty(PolygonChecks.SelfIntersections(ring, 1e-3));
        Assert.Empty(PolygonChecks.SelfIntersections(ring, 1e-4));
    }
}
