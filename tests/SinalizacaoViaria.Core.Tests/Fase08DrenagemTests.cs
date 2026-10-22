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
/// Fase VIII – rede de drenagem só geométrica (F8): trechos ligando PVs, bocas de lobo e grelhas; extensão, cotas de fundo,
/// declividade e cobrimento; sentido, ligações e ciclos sempre conferidos; limites só quando o usuário informa (sem norma
/// inventada); nada de vazão.
/// </summary>
public class Fase08DrenagemTests(ITestOutputHelper output)
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static DrainageDefinition Pv(double x, double z, double depth = 1.6) =>
        new() { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(x, 0), Z = z, BoxDepth = depth, BoxLength = 1.0, PipeDiameter = 0.4 };

    private static DrainagePipeDefinition Tubo(DrainageDefinition a, DrainageDefinition b, double d = 0.4) =>
        new() { FromId = a.Id, ToId = b.Id, Diameter = d, Z = a.Z };

    private static Func<string, MarkingDefinition?> Lookup(params MarkingDefinition[] defs) => id => defs.FirstOrDefault(x => x.Id == id);

    /// <summary>Projeto sem nenhum critério (só a geometria é conferida).</summary>
    private static ProjetoDrenagem SemCriterios(string? fonte = null) => new()
    {
        Fonte = fonte, DiametroMinimo = null, DiametroLigacao = null, VelocidadeMinima = null, VelocidadeMaxima = null, RecobrimentoMinimo = null,
        EspacamentoPv = false, QuedaPv = null,
    };

    /// <summary>Tubo principal (o mais longo) de uma geometria.</summary>
    private static RoundSolid MainPipe(MarkingGeometry g) =>
        g.Pieces.Where(p => p.Layer == "TUBO" && p.Round != null).Select(p => p.Round!).OrderByDescending(r => (r.B - r.A).XY.Length).First();

    [Theory]
    [InlineData(TipoDrenagem.PocoDeVisita)]
    [InlineData(TipoDrenagem.BocaDeLoboSimples)]
    [InlineData(TipoDrenagem.BocaDeLoboCombinada)]
    [InlineData(TipoDrenagem.GrelhaSarjeta)]
    [InlineData(TipoDrenagem.GrelhaQuadrada)]
    public void CaixaDaRede_MesmoFundoDoTuboDeSaidaDesenhado(TipoDrenagem tipo)
    {
        var d = new DrainageDefinition { Type = tipo, Position = new Vec2(10, 5), Along = new Vec2(1, 0), Z = 0, OutletPipe = true, PipeDiameter = 0.4 };
        d.ApplyDefaults();
        d.OutletPipe = true;
        var box = DrainageGenerator.NetworkBox(d)!.Value;
        var pipe = MainPipe(DrainageGenerator.Build(d, null));
        var r = Math.Clamp(d.PipeDiameter, 0.2, 1.5) / 2;
        output.WriteLine($"{tipo}: fundo −{box.InvertDepth:0.000} m, tubo de saída com o eixo a {pipe.A.Z:0.000} m, recuo {box.Inset:0.00} m");
        Assert.Equal(-box.InvertDepth, pipe.A.Z - r, 3);
        Assert.True(box.Inset > 0.05);
        Assert.Null(DrainageGenerator.NetworkBox(new DrainageDefinition { Type = TipoDrenagem.GrelhaContinua }));
    }

    [Fact]
    public void Trecho_TuboEntreAsCaixasNasCotasDeFundo()
    {
        var a = Pv(0, 100);
        var b = Pv(30, 99.5);
        var t = Tubo(a, b);
        var g = DrainageGenerator.PipeRun(t, new BuildContext { Catalog = Cat, Lookup = Lookup(a, b) });
        Assert.Empty(g.Warnings);
        var pipe = MainPipe(g);
        var (fa, fb) = (100 - (1.6 - 0.12), 99.5 - (1.6 - 0.12));
        // Do recuo de uma caixa ao da outra (parede interna do PV: raio 0,5 m), com o eixo a fundo + raio, relativo a Z.
        Assert.Equal(0.5, pipe.A.XY.X, 6);
        Assert.Equal(29.5, pipe.B.XY.X, 6);
        Assert.Equal(fa + (fb - fa) * 0.5 / 30 + 0.2 - t.Z, pipe.A.Z, 6);
        Assert.Equal(fa + (fb - fa) * 29.5 / 30 + 0.2 - t.Z, pipe.B.Z, 6);
        Assert.Equal(30, g.PaintedLength, 6);
        // Pelo MarkingBuilder (como no Revit) e no quantitativo: categoria Drenagem, em metros.
        var built = MarkingBuilder.Build(t, null, new BuildContext { Catalog = Cat, Lookup = Lookup(a, b, t) });
        Assert.NotEmpty(built.Pieces);
        var row = Assert.Single(QuantityCalculator.Compute(new[] { ((MarkingDefinition)t, built) }, Cat));
        Assert.Equal(CategoriaQuantitativo.Drenagem, row.Category);
        Assert.Equal("m", row.Unit);
        Assert.Equal(30, row.MainQuantity, 6);
    }

    [Fact]
    public void Trecho_SemDispositivoOuEncostado_SoAviso()
    {
        var a = Pv(0, 100);
        Assert.Contains("não existe", Assert.Single(DrainageGenerator.PipeRun(Tubo(a, Pv(30, 99)), new BuildContext { Catalog = Cat, Lookup = Lookup(a) }).Warnings));
        var b = Pv(0.8, 100);
        Assert.Contains("encostados", Assert.Single(DrainageGenerator.PipeRun(Tubo(a, b), new BuildContext { Catalog = Cat, Lookup = Lookup(a, b) }).Warnings));
    }

    [Fact]
    public void Medicao_ExtensaoDeclividadeECobrimento()
    {
        var a = Pv(0, 100);
        var b = Pv(40, 100, 2.0);
        var t = Tubo(a, b, 0.6);
        var tr = Assert.Single(DrainageNetwork.Analyze(new MarkingDefinition[] { a, b, t }));
        var (fa, fb) = (100 - 1.48, 100 - 1.88);
        Assert.Equal(40, tr.Comprimento, 6);
        Assert.Equal(fa, tr.FundoMontante, 6);
        Assert.Equal(fb, tr.FundoJusante, 6);
        Assert.Equal((fa - fb) / 40 * 100, tr.Declividade, 6);
        // Tampas na mesma cota: o menor cobrimento é no montante (tubo mais alto), sobre a geratriz superior externa.
        Assert.Equal(100 - (fa + 0.6 + DrainageNetwork.Wall(0.6)), tr.CobrimentoMinimo, 6);
        Assert.Equal("PV-1", tr.Montante);
        Assert.Equal("PV-2", tr.Jusante);
        // Com a superfície dada (terreno 30 cm acima das tampas no meio), o cobrimento usa ela.
        var tr2 = Assert.Single(DrainageNetwork.Analyze(new MarkingDefinition[] { a, b, t }, _ => 100.3));
        Assert.Equal(tr.CobrimentoMinimo + 0.3, tr2.CobrimentoMinimo, 6);
    }

    [Fact]
    public void SemLimites_SoProblemasGeometricos()
    {
        var a = Pv(0, 100, 2.0);
        var b = Pv(30, 100, 1.2);                       // fundo de jusante mais alto
        var c = Pv(60, 99);
        var defs = new MarkingDefinition[] { a, b, c, Tubo(a, b), Tubo(b, c), Tubo(c, c), Tubo(c, Pv(90, 98)) };
        var p = DrainageNetwork.Check(defs, null);
        foreach (var x in p) output.WriteLine(x.Texto);
        Assert.Contains(p, x => x.Criterio == "sentido" && x.Texto.Contains("contrafluxo"));
        Assert.Contains(p, x => x.Criterio == "ligação" && x.Texto.Contains("mesmo dispositivo"));
        Assert.Contains(p, x => x.Criterio == "dispositivo" && x.Texto.Contains("não existe"));
        Assert.DoesNotContain(p, x => x.PorLimite);
        Assert.Empty(DrainageNetwork.Check(new MarkingDefinition[] { a, c, Tubo(a, c) }, SemCriterios()));
    }

    [Fact]
    public void Ciclo_ApontadoComOsDispositivos()
    {
        var a = Pv(0, 100);
        var b = Pv(30, 100);
        var c = Pv(15, 110);
        var p = DrainageNetwork.Check(new MarkingDefinition[] { a, b, c, Tubo(a, b), Tubo(b, c), Tubo(c, a) }, null);
        var ciclo = Assert.Single(p, x => x.Criterio == "ciclo");
        Assert.Contains("PV-1 → PV-2 → PV-3 → PV-1", ciclo.Texto);
        // As marcas do ciclo vão junto (Validar Modelo seleciona os dispositivos).
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, ciclo.Ids);
    }

    [Fact]
    public void LimitesInformados_DeclividadeCobrimentoEDiametro()
    {
        var a = Pv(0, 100, 1.2);
        var b = Pv(50, 100, 1.3);                      // 0,2 % de declividade
        var c = Pv(80, 97, 1.2);                       // trecho íngreme
        var defs = new MarkingDefinition[] { a, b, c, Tubo(a, b, 0.3), Tubo(b, c, 0.4) };
        var crit = SemCriterios("Manual X; tabela 2");
        (crit.DeclividadeMinima, crit.DeclividadeMaxima, crit.RecobrimentoMinimo, crit.DiametroMinimo) = (0.5, 8, 1.0, 0.4);
        var p = DrainageNetwork.Check(defs, crit);
        foreach (var x in p) output.WriteLine(x.Texto);
        Assert.Contains(p, x => x.Criterio == "declividade" && x.Texto.Contains("mínimo de 0,50 %") && x.Elemento.StartsWith("T1"));
        Assert.Contains(p, x => x.Criterio == "declividade" && x.Texto.Contains("máximo de 8,00 %") && x.Elemento.StartsWith("T2"));
        Assert.Contains(p, x => x.Criterio == "cobrimento" && x.Elemento.StartsWith("T1"));
        Assert.Contains(p, x => x.Criterio == "diâmetro" && x.Texto.Contains("300 mm"));
        Assert.All(p, x => Assert.True(x.PorLimite));
        var csv = DrainageNetwork.Csv(DrainageNetwork.Analyze(defs), p, crit);
        Assert.Contains("Critérios: Manual X, tabela 2", csv);
        Assert.Contains("T1;PV-1;PV-2;galeria;50,00;", csv);
        Assert.Contains("não informados", DrainageNetwork.Csv(DrainageNetwork.Analyze(defs), new List<ProblemaRede>(), null));
    }

    [Fact]
    public void Persistencia_GrafoEValidacao()
    {
        var a = Pv(0, 100);
        var b = Pv(30, 99.5);
        var t = Tubo(a, b, 0.5);
        t.InvertTo = 97.9;
        var back = Assert.IsType<DrainagePipeDefinition>(MarkingDefinition.FromJson(t.ToJson()));
        Assert.Contains("\"tipo\":\"trecho-drenagem\"", t.ToJson().Replace(" ", ""));
        Assert.Equal((a.Id, b.Id, 0.5, 97.9), (back.FromId, back.ToId, back.Diameter, back.InvertTo!.Value));
        // O trecho depende dos dois dispositivos: refeito depois deles.
        var order = ModelGraph.RegenerationSort(new MarkingDefinition[] { t, a, b }).Select(d => d.Id).ToList();
        Assert.True(order.IndexOf(t.Id) > order.IndexOf(a.Id) && order.IndexOf(t.Id) > order.IndexOf(b.Id));
        Assert.Contains(ModelGraph.References(t), r => r.Tipo == TipoDependencia.Rede && r.Destino == b.Id);
        // Dispositivo apagado: V04 (referência quebrada) com revisão.
        var copias = new List<CopiaGravada> { new(a.Id, a.ToJson(), "e1"), new(t.Id, t.ToJson(), "e2") };
        var v = Assert.Single(ModelValidator.Validate(copias), x => x.Codigo == "V04");
        Assert.Contains("dispositivo de drenagem", v.Mensagem);
        Assert.Equal(97.9, DrainageNetwork.Ends(back, Lookup(a, b))!.InvertB, 9);
    }

    [Fact]
    public void Validacao_V13_RedeComAsMarcasEnvolvidas()
    {
        var a = Pv(0, 100, 2.0);
        var b = Pv(30, 100, 1.2);                       // contrafluxo de a para b
        var c = Pv(60, 99);
        var ab = Tubo(a, b);
        var cc = Tubo(c, c);
        var orfao = Tubo(c, Pv(90, 98));                // o dispositivo de jusante não está no projeto
        var defs = new MarkingDefinition[] { a, b, c, ab, Tubo(b, c), cc, orfao };
        List<Achado> Validar(ProjetoDrenagem? crit) =>
            ModelValidator.Validate(defs.Select((d, i) => new CopiaGravada(d.Id, d.ToJson(), $"e{i}")).ToList(), drenagem: crit);
        var v = Validar(null);
        foreach (var x in v) output.WriteLine($"{x.Codigo} {x.Mensagem}");
        var sentido = Assert.Single(v, x => x.Codigo == "V13" && x.Mensagem.Contains("contrafluxo"));
        Assert.Equal(new[] { ab.Id }, sentido.Ids);
        Assert.True(sentido.RevisaoHumana);
        Assert.Contains(v, x => x.Codigo == "V13" && x.Ids.SequenceEqual(new[] { cc.Id }) && x.Mensagem.Contains("mesmo dispositivo"));
        // Dispositivo inexistente: só o V04 (sem repetir no V13).
        Assert.Contains(v, x => x.Codigo == "V04" && x.Ids.Contains(orfao.Id));
        Assert.DoesNotContain(v, x => x.Codigo == "V13" && x.Ids.Contains(orfao.Id));
        // Limites informados: vão para o V13 com a fonte; cobrimento com confiança média (medido sob a reta entre as tampas).
        var crit = SemCriterios("Manual do órgão");
        crit.RecobrimentoMinimo = 5;
        var cob = Validar(crit).Where(x => x.Codigo == "V13" && x.Mensagem.Contains("cobrimento")).ToList();
        Assert.NotEmpty(cob);
        Assert.All(cob, x => Assert.Contains("Critérios: Manual do órgão", x.Mensagem));
        Assert.All(cob, x => Assert.Equal(Confianca.Media, x.Confianca));
        // Sem critérios, só a geometria; projeto sem trechos não tem V13.
        Assert.Equal(v.Count, Validar(SemCriterios("só a fonte")).Count);
        Assert.DoesNotContain(ModelValidator.Validate(new List<CopiaGravada> { new(a.Id, a.ToJson(), "e") }, drenagem: crit), x => x.Codigo == "V13");
        Assert.True(ModelValidator.Codigos.ContainsKey("V13"));
    }

    [Fact]
    public void ProjetoDeDrenagem_GravadoEmJsonComAsPredefinicoes()
    {
        var p = ProjetoDrenagem.MemorialModelo();
        p.Obra = "Loteamento X";
        var back = ProjetoDrenagem.FromJson(p.ToJson());
        Assert.Equal("Loteamento X", back.Obra);
        Assert.Equal(FormaIdf.Logaritmica, back.Chuva!.Forma);
        Assert.Equal(0.849, back.Chuva.ExpoenteDuracao);
        Assert.Equal(MetodoCoeficiente.Horner, back.Coeficiente);
        Assert.Equal(new[] { 0.40, 0.60, 0.80, 1.00, 1.20 }, back.Diametros);
        Assert.Null(back.QuedaPv);          // nulo escolhido (não conferir) sobrevive à gravação
        Assert.Null(ProjetoDrenagem.FromJson(new ProjetoDrenagem { RecobrimentoMinimo = null }.ToJson()).RecobrimentoMinimo);
        // Projeto antigo ou ilegível: vazio (sem chuva, só a geometria).
        Assert.False(ProjetoDrenagem.FromJson("{}").TemChuva);
        Assert.False(ProjetoDrenagem.FromJson("não é json").TemChuva);
        // Trocar a predefinição mantém a identificação do projeto.
        back.AplicarCriterios(ProjetoDrenagem.ManualSuderhsa());
        Assert.Equal("Loteamento X", back.Obra);
        Assert.Equal(FormaIdf.Potencia, back.Chuva!.Forma);
        Assert.Equal(2, back.TempoRetorno);
        Assert.Equal(1.00, back.RecobrimentoMinimo);
    }
}
