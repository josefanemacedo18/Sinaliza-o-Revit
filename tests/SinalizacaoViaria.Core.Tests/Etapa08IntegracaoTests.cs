using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 8 do plano – integração associativa (Prompt Master §14; Motores §14, §15): origem dos valores lidos do modelo,
/// estado de cada resultado (em dia, desatualizado, editado à mão), atualização seletiva que preserva as edições, impacto de
/// uma edição e o estado dos resultados no Validar Modelo (V17 e a seção do relatório).
/// </summary>
public class Etapa08IntegracaoTests
{
    private static readonly DateTime T0 = new(2026, 10, 26, 10, 0, 0);

    private static List<CopiaGravada> Copias(params MarkingDefinition[] defs) => defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e-" + d.Id)).ToList();

    /// <summary>Via de 200 m em vale (2 % até a estaca 100, 2 % depois), pistas de 3,5 m e calçadas de 3 m, moldando o terreno.</summary>
    private static RoadPavementDefinition Via(double fundo = 100)
    {
        var v = new RoadPavementDefinition
        {
            PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(200, 0) }, 100), GroupId = "g-" + Guid.NewGuid().ToString("N"),
            LeftWidth = 3.5, RightWidth = 3.5, LeftSidewalk = 3, RightSidewalk = 3,
        };
        Greide(v, fundo);
        return v;
    }

    private static void Greide(RoadPavementDefinition v, double fundo)
    {
        v.Output.Grade = new RoadGrade { DefaultCurve = 60, AdjustTerrain = true };
        foreach (var (s, z) in new[] { (0.0, 2.0), (fundo, 0.0), (200.0, 2.0) }) v.Output.Grade.Points.Add(new GradePoint(s, z));
    }

    private static DrainageDefinition Boca(double x) => new() { Type = TipoDrenagem.BocaDeLoboSimples, Position = new Vec2(x, 3.5), Z = 100 };

    private static EstadoLeitura E(MarkingDefinition[] defs, DrainageDefinition d) =>
        DrenagemDoModelo.Estado(DrenagemDoModelo.Captacoes(defs).Single(c => c.Dispositivo == d));

    [Fact]
    public void Leitura_RegistraOrigem_EmDiaAteOModeloMudar()
    {
        var via = Via();
        var rampa = Boca(40);
        var vale = Boca(100);
        var defs = new MarkingDefinition[] { via, rampa, vale };
        Assert.Equal(EstadoLeitura.NuncaLida, E(defs, vale));
        DrenagemDoModelo.Aplicar(DrenagemDoModelo.Captacoes(defs), quando: T0);
        Assert.NotNull(vale.Lida);
        Assert.Equal(via.Id, vale.Lida!.ViaId);
        Assert.Equal(1040, vale.Lida.Area);
        Assert.Equal(T0, vale.Lida.Quando);
        Assert.Equal(EstadoLeitura.EmDia, E(defs, vale));
        Assert.Equal(EstadoLeitura.EmDia, E(defs, rampa));
        // A leitura vai junto com a definição (JSON).
        Assert.Equal(1040, ((DrainageDefinition)MarkingDefinition.FromJson(vale.ToJson())!).Lida!.Area);

        // Outra captação entra entre as duas: a área do vale muda sem a via mudar – desatualizada; a de cima continua em dia.
        var meio = Boca(70);
        var depois = new MarkingDefinition[] { via, rampa, vale, meio };
        Assert.Equal(EstadoLeitura.Desatualizada, E(depois, vale));
        Assert.Equal(EstadoLeitura.EmDia, E(depois, rampa));
        Assert.Equal(EstadoLeitura.NuncaLida, E(depois, meio));
        // O greide muda e o vale sai do fundo: a declividade lida também fica velha.
        Greide(via, 130);
        Assert.Equal(EstadoLeitura.Desatualizada, E(defs, vale));
        // A captação sai da sarjeta (movida para longe da via): desatualizada, sem como reler.
        vale.Position = new Vec2(100, 40);
        Assert.Equal(EstadoLeitura.Desatualizada, E(defs, vale));
    }

    [Fact]
    public void Editada_AMao_FicaPreservada_EAAtualizacaoSeletivaSoTocaOLido()
    {
        var via = Via();
        var rampa = Boca(40);
        var vale = Boca(100);
        var defs = new MarkingDefinition[] { via, rampa, vale };
        DrenagemDoModelo.Aplicar(DrenagemDoModelo.Captacoes(defs), quando: T0);
        rampa.AreaContribuicao = 900;                                          // o projetista somou o lote
        rampa.DeclividadeSarjeta = 1.8;
        Assert.Equal(EstadoLeitura.Editada, E(defs, rampa));
        // Preencher de novo só os vazios não mexe no que foi editado.
        DrenagemDoModelo.Aplicar(DrenagemDoModelo.Captacoes(defs), quando: T0);
        Assert.Equal(900, rampa.AreaContribuicao);

        var meio = Boca(70);
        var depois = new MarkingDefinition[] { via, rampa, vale, meio };
        Greide(via, 40);                                                       // fundo do vale na estaca 40: muda a rampa e o vale
        var mudou = DrenagemDoModelo.AtualizarDesatualizadas(DrenagemDoModelo.Captacoes(depois), T0.AddDays(1));
        Assert.DoesNotContain(mudou, m => m.Dispositivo == rampa);             // editada à mão: fica
        Assert.DoesNotContain(mudou, m => m.Dispositivo == meio);              // nunca lida: não é "atualizar"
        var v = Assert.Single(mudou, m => m.Dispositivo == vale);
        Assert.Contains("área 1.040,0 →", v.Texto);
        Assert.Equal(900, rampa.AreaContribuicao);
        Assert.Equal(1.8, rampa.DeclividadeSarjeta);
        Assert.Equal(T0.AddDays(1), vale.Lida!.Quando);
        Assert.Equal(EstadoLeitura.EmDia, E(depois, vale));
        Assert.Empty(DrenagemDoModelo.AtualizarDesatualizadas(DrenagemDoModelo.Captacoes(depois)));
    }

    [Fact]
    public void EstadoDosResultados_TerraplenagemECaptacoes_ComOQueORevitRefazSozinho()
    {
        var via = Via();
        var vale = Boca(100);
        var no = new IntersectionDefinition { RoadIds = { via.Id } };
        var defs = new MarkingDefinition[] { via, vale, no };
        var semRegistro = EstadoDosResultados.Calcular(defs, null);
        Assert.Equal(SituacaoResultado.NaoVerificavel, semRegistro.Single(e => e.Modulo == "Terraplenagem").Situacao);
        Assert.Equal(SituacaoResultado.Informativo, semRegistro.Single(e => e.Modulo == "Drenagem").Situacao);   // nunca lida
        Assert.Contains(semRegistro, e => e.Modulo == "Interseções" && e.Situacao == SituacaoResultado.Informativo);
        Assert.Contains(semRegistro, e => e.Modulo == "Quantitativos" && e.Detalhe.Contains("na hora"));

        var reg = EarthworkRecord.Gravar(EarthworkRecord.Participantes(defs), 0.4, 1, 1.5, T0, "2.14.0");
        DrenagemDoModelo.Aplicar(DrenagemDoModelo.Captacoes(defs), quando: T0);
        var emDia = EstadoDosResultados.Calcular(defs, reg);
        Assert.Equal(SituacaoResultado.Atualizado, emDia.Single(e => e.Modulo == "Terraplenagem").Situacao);
        Assert.Equal(SituacaoResultado.Atualizado, emDia.Single(e => e.Modulo == "Drenagem").Situacao);

        Greide(via, 130);
        var velho = EstadoDosResultados.Calcular(defs, reg);
        var t = velho.Single(e => e.Modulo == "Terraplenagem");
        Assert.Equal(SituacaoResultado.Desatualizado, t.Situacao);
        Assert.Contains(via.Id, t.Ids);
        var d = velho.Single(e => e.Modulo == "Drenagem");
        Assert.Equal(SituacaoResultado.Desatualizado, d.Situacao);
        Assert.Equal(new[] { vale.Id }, d.Ids);
        Assert.Contains("1 desatualizada(s)", d.Detalhe);
    }

    [Fact]
    public void Assinatura_DaCaixa_IgnoraOsDadosHidraulicos_MasNaoAGeometria()
    {
        // Regressão: preencher a área pelo Ler do Modelo mudava a assinatura da caixa e a V15 acusava a terraplenagem velha.
        var caixa = Boca(100);
        var a = EarthworkRecord.Assinatura(caixa);
        caixa.AreaContribuicao = 1040;
        caixa.DeclividadeSarjeta = 0.5;
        caixa.PontoBaixo = true;
        caixa.VazaoExterna = 12;
        caixa.Lida = new ContribuicaoLida { ViaId = "v", Area = 1040 };
        Assert.Equal(a, EarthworkRecord.Assinatura(caixa));
        caixa.BoxDepth = 1.6;                                                  // a escavação muda
        Assert.NotEqual(a, EarthworkRecord.Assinatura(caixa));
    }

    [Fact]
    public void Impacto_DaEdicaoDeUmaVia_ConexoesDependentesTerraplenagemECaptacoes()
    {
        var via = Via();
        var outra = Via();
        var vale = Boca(100);
        var no = new IntersectionDefinition { RoadIds = { via.Id, outra.Id } };
        var defs = new MarkingDefinition[] { via, outra, vale, no };
        var reg = EarthworkRecord.Gravar(EarthworkRecord.Participantes(defs), 0.4, 1, 1.5, T0, "2.14.0");
        DrenagemDoModelo.Aplicar(DrenagemDoModelo.Captacoes(defs), quando: T0);
        Assert.Empty(EstadoDosResultados.Impacto(new[] { via }, defs, reg).Where(i => i.Resultado is "Terraplenagem" or "Drenagem"));

        Greide(via, 130);
        var imp = EstadoDosResultados.Impacto(new[] { via }, defs, reg);
        Assert.Contains(imp, i => i.Resultado == "Interseções" && i.Ids.Contains(no.Id));
        Assert.Contains(imp, i => i.Resultado == "Dependências" && i.Ids.Contains(no.Id));
        Assert.Contains(imp, i => i.Resultado == "Terraplenagem" && i.Ids.SequenceEqual(new[] { via.Id }));
        Assert.Contains(imp, i => i.Resultado == "Drenagem" && i.Ids.SequenceEqual(new[] { vale.Id }));
        // A terraplenagem reaplicada (registro novo): não fica pendente.
        var reg2 = EarthworkRecord.Gravar(EarthworkRecord.Participantes(defs), 0.4, 1, 1.5, T0.AddHours(1), "2.14.0");
        Assert.DoesNotContain(EstadoDosResultados.Impacto(new[] { via }, defs, reg2), i => i.Resultado == "Terraplenagem");
        // Editar a outra via não deixa a captação desta desatualizada.
        Assert.DoesNotContain(EstadoDosResultados.Impacto(new[] { outra }, defs, reg2), i => i.Resultado == "Drenagem");
    }

    [Fact]
    public void V17_LeituraDesatualizada_NoValidarModelo_ESecaoDoRelatorio()
    {
        var via = Via();
        var vale = Boca(100);
        var editada = Boca(40);
        var defs = new MarkingDefinition[] { via, vale, editada };
        DrenagemDoModelo.Aplicar(DrenagemDoModelo.Captacoes(defs), quando: T0);
        editada.AreaContribuicao = 999;
        editada.DeclividadeSarjeta = 9;
        Assert.DoesNotContain(ModelValidator.Validate(Copias(defs)), a => a.Codigo == "V17");
        Greide(via, 130);
        var v17 = Assert.Single(ModelValidator.Validate(Copias(defs)), a => a.Codigo == "V17");
        Assert.Equal(Severidade.Aviso, v17.Severidade);
        Assert.Equal(new[] { vale.Id }, v17.Ids);                               // a editada à mão não entra
        Assert.Equal("Integração", v17.Modulo);
        var txt = ModelValidator.Report(new[] { v17 }, 3, 3, "P", T0, "2.14.0", null, EstadoDosResultados.Calcular(defs, null));
        Assert.Contains("Estado dos resultados:", txt);
        Assert.Contains("[DESATUALIZADO] Drenagem · Área e declividade das captações lidas da via", txt);
        Assert.Contains("[não verificado] Terraplenagem", txt);
        Assert.DoesNotContain("Estado dos resultados:", ModelValidator.Report(new[] { v17 }, 3, 3, "P", T0));
    }
}
