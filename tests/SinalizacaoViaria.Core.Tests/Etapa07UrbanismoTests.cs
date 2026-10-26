using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;
using SinalizacaoViaria.Core.Reports;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 7 do plano – urbanismo e elementos complementares (Prompt Master §12; Motores §11, §12; Interface §13): indicadores
/// do parcelamento, mínimos informados com a fonte e conferidos no Validar Modelo (V16), e substituição controlada de
/// elementos urbanos.
/// </summary>
public class Etapa07UrbanismoTests
{
    private static List<CopiaGravada> Copias(params MarkingDefinition[] defs) => defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e-" + d.Id)).ToList();

    /// <summary>Gleba de 122 × 80 m com uma quadra de 102 × 60 m (testada sul): 8 lotes de 12 × 30 m e a sobra de 6 × 30 m.</summary>
    private static (LoteamentoDefinition Lot, QuadraDefinition Q) Cena()
    {
        var lot = new LoteamentoDefinition { Nome = "Jardim Teste", Gleba = new() { new(-10, -10), new(112, -10), new(112, 70), new(-10, 70) } };
        var q = LTScenes.Q("S", new List<Vec2> { new(0, 0), new(102, 0), new(102, 60), new(0, 60) }, new Vec2(51, 0));
        q.Miolo = ModoMiolo.AreaPropria;
        q.LoteamentoId = lot.Id;
        return (lot, q);
    }

    [Fact]
    public void Indicadores_DensidadeAreasFrentesEPercentuais()
    {
        var (lot, q) = Cena();
        var r = LotReport.Resumo(lot, new[] { q });
        var i = IndicadoresUrbanisticos.Calcular(r);
        Assert.Equal(9, i.Lotes);
        Assert.Equal(9760, i.AreaGleba, 3);
        Assert.Equal(9 / 0.976, i.LotesPorHectare!.Value, 6);
        Assert.Equal(180, i.AreaMenor!.Value, 2);
        Assert.Equal(360, i.AreaMaior!.Value, 2);
        Assert.Equal(3060 / 9.0, i.AreaMedia!.Value, 2);
        Assert.Equal((6.0, 12.0), (Math.Round(i.FrenteMenor!.Value, 6), Math.Round(i.FrenteMaior!.Value, 6)));
        Assert.Equal(0, i.AbaixoDoMinimo);
        Assert.Equal(3640 / 9760.0 * 100, i.PctViario, 3);                     // gleba − quadra
        Assert.Equal(102 * 30 / 9760.0 * 100, i.PctVerde, 1);                   // o miolo da quadra (área própria) é área verde
        var linhas = IndicadoresUrbanisticos.Linhas(i);
        Assert.Contains(linhas, l => l.Indicador.StartsWith("Densidade bruta") && l.Valor == "9,2");
        Assert.Contains(linhas, l => l.Indicador == "Menor e maior lote" && l.Valor == "180,00 m² e 360,00 m²");
        // Sem gleba nem lotes: o que não se mede é dito.
        var vazio = IndicadoresUrbanisticos.Linhas(IndicadoresUrbanisticos.Calcular(LotReport.Resumo(new LoteamentoDefinition(), Array.Empty<QuadraDefinition>())));
        Assert.Contains(vazio, l => l.Valor == "gleba não definida");
        Assert.Contains(vazio, l => l.Valor == "sem lotes");
    }

    [Fact]
    public void V16_MinimoNaoAtendidoComAFonte_ELoteAbaixoDoMinimo()
    {
        var (lot, q) = Cena();
        lot.Minimos[ResumoLoteamento.SistemaViario] = 40;
        lot.FonteMinimos = "Lei municipal 123/2020, art. 8º";
        lot.AreaMinimaLote = 400;                                                // 7 lotes de 360 m² ficam abaixo (a sobra vai para o último)
        var v16 = ModelValidator.Validate(Copias(lot, q)).Where(a => a.Codigo == "V16").ToList();
        var min = Assert.Single(v16, a => a.Mensagem.Contains("Sistema viário"));
        Assert.Equal(Severidade.Aviso, min.Severidade);
        Assert.Contains("Jardim Teste: Sistema viário com 37,30 % da gleba, abaixo do mínimo informado de 40,00 % (fonte: Lei municipal 123/2020, art. 8º)", min.Mensagem);
        var lotes = Assert.Single(v16, a => a.Mensagem.Contains("abaixo do lote mínimo"));
        Assert.Contains("quadra S: 7 lote(s)", lotes.Mensagem);
        Assert.Contains(q.Id, lotes.Ids);
        Assert.Equal("Quadras e lotes", lotes.Modulo);
        // Atendidos: nada.
        lot.Minimos[ResumoLoteamento.SistemaViario] = 30;
        lot.AreaMinimaLote = 125;
        Assert.DoesNotContain(ModelValidator.Validate(Copias(lot, q)), a => a.Codigo == "V16");
    }

    [Fact]
    public void V16_SemMinimosInformados_NaoVerificado_ESemFonteDito()
    {
        var (lot, q) = Cena();
        var n = Assert.Single(ModelValidator.Validate(Copias(lot, q), incluirNaoVerificados: true), a => a.Codigo == "V16");
        Assert.Equal(Severidade.NaoVerificavel, n.Severidade);
        Assert.Contains("não informados", n.Mensagem);
        lot.Minimos[CategoriaLote.AreaVerde] = 50;                              // o miolo da quadra é área verde: ~31 % da gleba
        var a = Assert.Single(ModelValidator.Validate(Copias(lot, q)), x => x.Codigo == "V16");
        Assert.Contains("(sem fonte informada)", a.Mensagem);
        // Loteamento sem quadras: nada a conferir.
        Assert.DoesNotContain(ModelValidator.Validate(Copias(new LoteamentoDefinition()), incluirNaoVerificados: true), x => x.Codigo == "V16");
    }

    [Fact]
    public void Memorial_TrazOsIndicadoresEAFonteDosMinimos()
    {
        var (lot, q) = Cena();
        lot.FonteMinimos = "Plano Diretor";
        var doc = LotMemorial.Build(LotReport.Resumo(lot, new[] { q }), new DateTime(2026, 10, 26));
        Assert.Contains(doc.Blocks.OfType<MemParagraph>(), p => p.Text == "Fonte dos mínimos: Plano Diretor.");
        var t = Assert.Single(doc.Blocks.OfType<MemTable>(), x => x.Headers[0] == "Indicador");
        Assert.Contains(t.Rows, r => r[0] == "Lotes" && r[1] == "9");
        Assert.Equal("Plano Diretor", ((LoteamentoDefinition)MarkingDefinition.FromJson(lot.ToJson())!).FonteMinimos);
        lot.FonteMinimos = null;
        Assert.Contains(LotMemorial.Build(LotReport.Resumo(lot, new[] { q })).Blocks.OfType<MemParagraph>(), p => p.Text == "Fonte dos mínimos: não informada no projeto.");
    }

    [Fact]
    public void Substituicao_TrocaOTipo_MantemPosicaoECaminho_EMedidasConformeAEscolha()
    {
        var cat = CatalogService.LoadDefault();
        var lixeira = cat.Movel("LIXEIRA")!;
        var banco = new UrbanElementDefinition { Code = "BANCO", Position = new Vec2(10, 5), Direction = Vec2.UnitX, Length = 2.4, Color = MarkingColor.Madeira };
        var linha = new UrbanElementDefinition { Code = "PARACICLO", Spacing = 3, Offset = 0.8 };
        linha.SetPath(PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(30, 0) }, 0));
        var ja = new UrbanElementDefinition { Code = "LIXEIRA" };
        var m = SubstituicaoUrbana.Trocar(new[] { banco, linha, ja }, lixeira, manterMedidas: true);
        Assert.Equal(2, m.Count);
        Assert.Equal("LIXEIRA", banco.Code);
        Assert.Equal(2.4, banco.Length);                                        // medidas próprias ficam
        Assert.Equal(new Vec2(10, 5), banco.Position);
        Assert.Equal(Vec2.UnitX, banco.Direction);
        Assert.Equal(3, linha.Spacing);
        Assert.Equal(0.8, linha.Offset);
        Assert.True(linha.UsePath);
        Assert.Equal("BANCO → LIXEIRA", m[0].Texto);

        var outro = new UrbanElementDefinition { Code = "BANCO", Length = 2.4, Color = MarkingColor.Madeira };
        var l2 = new UrbanElementDefinition { Code = "BANCO", Spacing = 3 };
        l2.SetPath(PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(30, 0) }, 0));
        var m2 = SubstituicaoUrbana.Trocar(new[] { outro, l2 }, lixeira, manterMedidas: false, espacamentoDoTipo: true);
        Assert.Null(outro.Length);
        Assert.Null(outro.Color);
        Assert.Null(l2.Spacing);
        Assert.Contains("medidas e cor do novo tipo", m2[0].Texto);
        Assert.Contains("espaçamento do novo tipo", m2[1].Texto);
    }
}
