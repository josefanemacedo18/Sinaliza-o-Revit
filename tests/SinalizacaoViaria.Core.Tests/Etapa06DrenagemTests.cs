using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 6 do plano – drenagem integrada (Prompt Master §11; Motores §10; Interface §16; RT-1.5, K5): sarjeta e área de
/// contribuição lidas da via, pontos baixos sem captação, talvegues sob aterro sem travessia, recobrimento sob a superfície
/// acabada e perfil longitudinal da rede.
/// </summary>
public class Etapa06DrenagemTests
{
    private static List<CopiaGravada> Copias(params MarkingDefinition[] defs) => defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e-" + d.Id)).ToList();

    /// <summary>Via de 200 m em x, cota 100, pistas de 3,5 m e calçadas de 3 m; greide com PIVs (estação, cota relativa).</summary>
    private static RoadPavementDefinition Via(params (double S, double Z)[] pivs)
    {
        var v = new RoadPavementDefinition
        {
            PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(200, 0) }, 100), GroupId = "g-" + Guid.NewGuid().ToString("N"),
            LeftWidth = 3.5, RightWidth = 3.5, LeftSidewalk = 3, RightSidewalk = 3,
        };
        if (pivs.Length > 0)
        {
            v.Output.Grade = new RoadGrade { DefaultCurve = 60 };
            foreach (var (s, z) in pivs) v.Output.Grade.Points.Add(new GradePoint(s, z));
        }
        return v;
    }

    /// <summary>Greide em vale: 2 % descendo até a estaca 100 e subindo 2 % até a 200 (curva vertical de 60 m).</summary>
    private static RoadPavementDefinition ViaEmVale() => Via((0, 2), (100, 0), (200, 2));

    private static DrainageDefinition Boca(double x, double y, double z) => new() { Type = TipoDrenagem.BocaDeLoboSimples, Position = new Vec2(x, y), Z = z };

    [Fact]
    public void PontoBaixo_DaSarjeta_NoFundoDoVale_ESoComMeioFio()
    {
        var via = DrenagemDoModelo.Vias(new MarkingDefinition[] { ViaEmVale() }).Single();
        var pb = Assert.Single(DrenagemDoModelo.PontosBaixos(via, 1));
        Assert.Equal(100, pb.Estaca, 1);
        Assert.Equal("5+0,00", pb.EstacaTexto);
        Assert.Equal(1.7, pb.Profundidade, 2);                       // 2 m nas pontas − 0,30 m no fundo da curva vertical
        Assert.Equal(100, pb.Ponto.X, 6);
        Assert.Equal(3.5, pb.Ponto.Y, 6);
        // Via plana (greide sem PIV mudando) e lado sem calçada (sem meio-fio): nenhum ponto baixo.
        Assert.Empty(DrenagemDoModelo.PontosBaixos(DrenagemDoModelo.Vias(new MarkingDefinition[] { Via((0, 0), (200, 0)) }).Single(), 1));
        var semCalcada = ViaEmVale();
        semCalcada.RightSidewalk = 0;
        Assert.Empty(DrenagemDoModelo.PontosBaixos(DrenagemDoModelo.Vias(new MarkingDefinition[] { semCalcada }).Single(), -1));
        // Sem greide não há sentido de escoamento.
        Assert.Empty(DrenagemDoModelo.PontosBaixos(DrenagemDoModelo.Vias(new MarkingDefinition[] { Via() }).Single(), 1));
    }

    [Fact]
    public void Sarjeta_LidaDoGreide_DeclividadeSentidoEPontoBaixo()
    {
        var via = ViaEmVale();
        var vias = DrenagemDoModelo.Vias(new MarkingDefinition[] { via });
        var noVale = DrenagemDoModelo.Sarjeta(Boca(100, 3.5, 100.3), vias)!;
        Assert.Equal(1, noVale.Lado);
        Assert.Equal(100, noVale.Estaca, 6);
        Assert.True(noVale.PontoBaixo);
        Assert.True(noVale.Declividade < 0.1);
        var naRampa = DrenagemDoModelo.Sarjeta(Boca(40, -3.6, 101.2), vias)!;
        Assert.Equal(-1, naRampa.Lado);
        Assert.Equal(2.0, naRampa.Declividade!.Value, 3);
        Assert.Equal(1, naRampa.Sentido);                                   // desce no sentido do eixo
        Assert.False(naRampa.PontoBaixo);
        Assert.Equal(0.1, naRampa.Afastamento, 6);
        // Longe do meio-fio (no meio da pista ou além da calçada) ou de canaleta: não é desta sarjeta.
        Assert.Null(DrenagemDoModelo.Sarjeta(Boca(40, 0, 100), vias));
        Assert.Null(DrenagemDoModelo.Sarjeta(Boca(40, 8, 100), vias));
        Assert.Null(DrenagemDoModelo.Sarjeta(new DrainageDefinition { Type = TipoDrenagem.GrelhaContinua, Position = new Vec2(40, 3.5) }, vias));
    }

    [Fact]
    public void ViaSobreLinhasDoRevit_UsaOTracadoGuardadoNoCache()
    {
        // Regressão: a primeira versão lia só os pontos do caminho (TerraPaint.StoredPath) e ignorava as vias por linhas.
        var via = ViaEmVale();
        via.PathRef = new PathReference
        {
            ElementIds = { "linha-1", "linha-2" }, Z = 100,
            Cache = new List<List<Vec2>> { new() { new(0, 0), new(100, 0) }, new() { new(100, 0), new(200, 0) } },
        };
        var v = Assert.Single(DrenagemDoModelo.Vias(new MarkingDefinition[] { via }));
        Assert.Equal(200, v.Eixo.Length, 6);
        Assert.True(DrenagemDoModelo.Sarjeta(Boca(100, 3.5, 100.3), new[] { v })!.PontoBaixo);
        Assert.Equal(200, via.PathRef.StoredPolyline()!.Length, 6);
        Assert.Null(new PathReference { ElementIds = { "x" } }.StoredPolyline());
    }

    [Fact]
    public void Area_DoDivisorACaptacao_ComACaptacaoDeCimaEOValeDosDoisLados()
    {
        var via = ViaEmVale();
        var rampa = Boca(40, 3.5, 101.2);
        var vale = Boca(100, 3.5, 100.3);
        var c = DrenagemDoModelo.Captacoes(new MarkingDefinition[] { via, rampa, vale });
        var aRampa = c.Single(x => x.Dispositivo == rampa).Area!;
        var aVale = c.Single(x => x.Dispositivo == vale).Area!;
        Assert.Equal((0.0, 40.0), (aRampa.De, aRampa.Ate));
        Assert.Equal(40 * 6.5, aRampa.Area, 6);                             // pista do eixo ao meio-fio (3,5) + calçada (3)
        Assert.Equal((40.0, 200.0), (aVale.De, aVale.Ate));                 // capta o que passa da de cima e o outro lado do vale
        Assert.Equal(160 * 6.5, aVale.Area, 6);
        // Sarjeta em nível entre duas captações: metade para cada.
        var plana = Via((0, 0), (200, 0));
        var b1 = Boca(50, 3.5, 100);
        var b2 = Boca(150, 3.5, 100);
        var cp = DrenagemDoModelo.Captacoes(new MarkingDefinition[] { plana, b1, b2 });
        Assert.Equal((0.0, 100.0), (cp[0].Area!.De, cp[0].Area!.Ate));
        Assert.Equal((100.0, 200.0), (cp[1].Area!.De, cp[1].Area!.Ate));
        Assert.Equal(200 * 6.5, cp.Sum(x => x.Area!.Area), 6);
    }

    [Fact]
    public void LarguraContribuinte_ComSuperelevacao_APistaInteiraVaiParaOLadoBaixo()
    {
        var v = Via((0, 0), (200, 0));
        v.Output.Grade!.Superelevation.Add(new Vec2(0, 0.05));            // e > 0: a esquerda sobe
        var via = DrenagemDoModelo.Vias(new MarkingDefinition[] { v }).Single();
        Assert.Equal(3 + 7, via.LarguraContribuinte(50, -1), 6);
        Assert.Equal(3, via.LarguraContribuinte(50, 1), 6);
        Assert.Equal(3.5 + 3, DrenagemDoModelo.Vias(new MarkingDefinition[] { Via((0, 0), (200, 0)) }).Single().LarguraContribuinte(50, 1), 6);
    }

    [Fact]
    public void Aplicar_SoOsVazios_OuSubstituir_EDivergenciasComOGreide()
    {
        var via = ViaEmVale();
        var rampa = Boca(40, 3.5, 101.2);
        rampa.DeclividadeSarjeta = 0.5;                                     // informada errada
        var vale = Boca(100, 3.5, 100.3);
        var defs = new MarkingDefinition[] { via, rampa, vale };
        var div = DrenagemDoModelo.Divergencias(DrenagemDoModelo.Captacoes(defs));
        Assert.Contains(div, d => d.Criterio == "sarjeta-modelo" && d.Texto.Contains("informada 0,50 %") && d.Texto.Contains("é 2,00 %"));
        Assert.Contains(div, d => d.Criterio == "ponto-baixo-modelo" && d.Ids!.Contains(vale.Id));

        var mudou = DrenagemDoModelo.Aplicar(DrenagemDoModelo.Captacoes(defs));
        Assert.Equal(2, mudou.Count);
        Assert.Equal(260, rampa.AreaContribuicao, 6);
        Assert.Equal(0.5, rampa.DeclividadeSarjeta);                        // o informado fica
        Assert.Equal(1040, vale.AreaContribuicao, 6);
        Assert.True(vale.PontoBaixo);
        Assert.Contains(mudou, m => m.Dispositivo == vale && m.Texto.Contains("ponto baixo") && m.Texto.Contains("área 1.040,0 m²"));
        Assert.Empty(DrenagemDoModelo.Aplicar(DrenagemDoModelo.Captacoes(defs)));          // nada mais vazio
        DrenagemDoModelo.Aplicar(DrenagemDoModelo.Captacoes(defs), sobrescrever: true);
        Assert.Equal(2.0, rampa.DeclividadeSarjeta!.Value, 2);
        Assert.DoesNotContain(DrenagemDoModelo.Divergencias(DrenagemDoModelo.Captacoes(defs)), d => d.Ids!.Contains(rampa.Id));
        Assert.Contains("CAPTAÇÕES × MODELO", DrenagemDoModelo.Csv(DrenagemDoModelo.Captacoes(defs)));
    }

    [Fact]
    public void PontoBaixoSemCaptacao_NoValidarModelo_SoComDrenagemModelada()
    {
        var via = ViaEmVale();
        var vale = Boca(100, 3.5, 100.3);                                    // capta o lado esquerdo; o direito fica a 7 m
        var sem = DrenagemDoModelo.PontosBaixosSemCaptacao(new MarkingDefinition[] { via, vale });
        Assert.Equal(-1, Assert.Single(sem).Lado);
        var v13 = ModelValidator.Validate(Copias(via, vale)).Where(a => a.Codigo == "V13" && a.Mensagem.StartsWith("Ponto baixo")).ToList();
        var a = Assert.Single(v13);
        Assert.Equal(Severidade.Aviso, a.Severidade);
        Assert.Contains("lado direito, estaca 5+0,00", a.Mensagem);
        Assert.Contains(via.Id, a.Ids);
        // Projeto sem drenagem modelada: o ponto baixo não é apontado (a sinalização não é projeto de drenagem).
        Assert.DoesNotContain(ModelValidator.Validate(Copias(via)), x => x.Codigo == "V13");
        // A informação de captação no ponto baixo sem a marca: informação, não aviso.
        Assert.Contains(ModelValidator.Validate(Copias(via, vale)), x => x.Codigo == "V13" && x.Severidade == Severidade.Info && x.Mensagem.Contains("não está marcada"));
    }

    [Fact]
    public void Talvegue_SobAterroSemTravessia_ComTubo_ENaPonteNao()
    {
        var via = Via((0, 0), (200, 0));
        via.Output.Grade!.AdjustTerrain = true;
        double? Natural(Vec2 p) => 99 - 3 * Math.Max(0, 1 - Math.Abs(p.X - 100) / 40);   // vale de 3 m com fundo em x = 100
        var t = Assert.Single(DrenagemDoModelo.TalveguesSemTravessia(new MarkingDefinition[] { via }, Natural));
        Assert.Equal(100, t.Estaca, 6);
        Assert.Equal(4, t.Aterro, 6);
        Assert.Equal(3, t.Profundidade, 6);
        var v13 = Assert.Single(ModelValidator.Validate(Copias(via), terrenoNatural: Natural), x => x.Codigo == "V13");
        Assert.Contains("represa a água", v13.Mensagem);
        // Com um trecho de rede atravessando a via perto do talvegue: nada.
        var a = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(104, 10), Z = 99 };
        var b = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(104, -10), Z = 98.8 };
        var tubo = new DrainagePipeDefinition { FromId = a.Id, ToId = b.Id, Diameter = 0.6, Z = 99 };
        Assert.Empty(DrenagemDoModelo.TalveguesSemTravessia(new MarkingDefinition[] { via, a, b, tubo }, Natural));
        // Sob uma ponte da via: sem aterro, nada.
        var ponte = new BridgeDefinition { HostRoad = via.GroupId, HostStart = 70, HostEnd = 130 };
        Assert.Empty(DrenagemDoModelo.TalveguesSemTravessia(new MarkingDefinition[] { via, ponte }, Natural));
        // Sem o terreno natural, o validador diz que não conferiu.
        Assert.Contains(ModelValidator.Validate(Copias(via), incluirNaoVerificados: true),
            x => x.Codigo == "V13" && x.Severidade == Severidade.NaoVerificavel && x.Mensagem.StartsWith("Terreno natural não lido"));
    }

    [Fact]
    public void SuperficieAcabada_PistaCalcadaETerreno_EORecobrimentoUsaEla()
    {
        var via = Via((0, 0), (200, 0));
        var s = DrenagemDoModelo.SuperficieAcabada(new MarkingDefinition[] { via }, p => 98);
        Assert.Equal(100, s(new Vec2(50, 2))!.Value, 6);
        Assert.Equal(100.15, s(new Vec2(50, -5))!.Value, 6);                // calçada: sarjeta + altura do meio-fio
        Assert.Equal(98, s(new Vec2(50, 20))!.Value, 6);
        Assert.Equal(98, s(new Vec2(230, 0))!.Value, 6);                    // além da ponta do eixo
        Assert.Null(DrenagemDoModelo.SuperficieAcabada(new MarkingDefinition[] { via }, null)(new Vec2(50, 20)));

        // Rede fora da via, sob um terreno com uma baixada entre as tampas: o recobrimento cai 1 m.
        var a = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(0, 50), Z = 10 };
        var b = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(80, 50), Z = 9.6 };
        var tubo = new DrainagePipeDefinition { FromId = a.Id, ToId = b.Id, Diameter = 0.4, Z = 10 };
        var defs = new MarkingDefinition[] { a, b, tubo };
        var reta = DrainageNetwork.Analyze(defs).Single().CobrimentoMinimo;
        var baixada = DrenagemDoModelo.SuperficieAcabada(defs, p => p.X > 30 && p.X < 50 ? 9.8 - 1.0 * (1 - Math.Abs(p.X - 40) / 10) : 10 - 0.4 * p.X / 80);
        var medido = DrainageNetwork.Analyze(defs, baixada).Single().CobrimentoMinimo;
        Assert.True(reta - medido > 0.7, $"reta {reta:0.00} × superfície {medido:0.00}");
    }

    [Fact]
    public void PerfilDaRede_RamoPrincipalEOsQueChegam_ELista()
    {
        var a = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(0, 0), Z = 10 };
        var b = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(40, 0), Z = 9.8 };
        var c = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(80, 0), Z = 9.6 };
        var d = new DrainageDefinition { Type = TipoDrenagem.BocaDeLoboSimples, Position = new Vec2(40, 30), Z = 9.9 };
        var ab = new DrainagePipeDefinition { FromId = a.Id, ToId = b.Id, Diameter = 0.4, Z = 10 };
        var bc = new DrainagePipeDefinition { FromId = b.Id, ToId = c.Id, Diameter = 0.5, Z = 10 };
        var db = new DrainagePipeDefinition { FromId = d.Id, ToId = b.Id, Diameter = 0.4, Z = 10 };
        var defs = new MarkingDefinition[] { a, b, c, d, ab, bc, db };
        var l = PerfilRede.Linhas(defs);
        var r1 = l.Where(x => x.Ramo == 1).ToList();
        var r2 = l.Where(x => x.Ramo == 2).ToList();
        Assert.Equal(3, r1.Count);
        Assert.Equal(2, r2.Count);
        Assert.Equal(0, r1[0].Distancia);
        Assert.Equal(80, r1[2].Distancia, 6);
        Assert.Null(r1[0].FundoChegada);
        Assert.Null(r1[2].Trecho);
        Assert.Equal(r1[1].Dispositivo, r2[1].Dispositivo);                // o ramo da boca de lobo para no PV já listado
        Assert.InRange(r2[1].Distancia, 29, 32);                            // centro da caixa da boca de lobo ao centro do PV
        Assert.Equal(0.5, r1[1].Diametro);
        var disp = PerfilRede.Dispositivos(defs);
        Assert.Equal(4, disp.Count);
        var pvB = disp.Single(x => x.Id == b.Id);
        Assert.Equal((2, 1), (pvB.Entradas, pvB.Saidas));
        Assert.True(pvB.Profundidade > 0);
        Assert.Equal("Boca de lobo simples (guia chapéu)", disp.Single(x => x.Id == d.Id).Tipo);
        var csv = PerfilRede.Csv(l, disp);
        Assert.Contains("PERFIL LONGITUDINAL DA REDE", csv);
        Assert.Contains("DISPOSITIVOS DA REDE", csv);
    }

    [Fact]
    public void Tolerancias_DaDrenagemNoRegistro()
    {
        var nomes = Tolerancias.Todas.Select(t => t.Nome).ToList();
        foreach (var n in new[] { "Dispositivo na sarjeta", "Captação do ponto baixo", "Profundidade do ponto baixo", "Declividade informada × greide",
                     "Aterro sobre o talvegue", "Profundidade do talvegue", "Alcance da travessia" })
            Assert.Contains(n, nomes);
        Assert.Contains("talvegue sob aterro", ModelValidator.Regras["V13"].Verifica);
    }
}
