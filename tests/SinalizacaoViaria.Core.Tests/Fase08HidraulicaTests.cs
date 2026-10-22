using System.IO.Compression;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Reports;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Fase VIII – dimensionamento da rede pelo método racional, conferido contra as fontes enviadas pelo usuário: as curvas
/// IDF e os exemplos 6.1 a 6.3 do Manual de Drenagem Urbana (SUDERHSA, 2002) e a planilha de galerias do memorial modelo
/// (Miguelópolis/SP, 2019).
/// </summary>
public class Fase08HidraulicaTests(ITestOutputHelper output)
{
    private static DrainageDefinition Pv(double x, double z, double y = 0) =>
        new() { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(x, y), Z = z, BoxDepth = 1.6, BoxLength = 1.0, PipeDiameter = 0.4 };

    private static DrainagePipeDefinition Tubo(DrainageDefinition a, DrainageDefinition b, double d, double? fa = null, double? fb = null) =>
        new() { FromId = a.Id, ToId = b.Id, Diameter = d, Z = a.Z, InvertFrom = fa, InvertTo = fb };

    [Theory]
    // Manual SUDERHSA, tabela B.3 (Fendrich e Parigot de Souza), intensidades em mm/h.
    [InlineData(2, 5, 120.06, 133.28)]
    [InlineData(10, 60, 64.89, 58.46)]
    [InlineData(50, 120, 57.59, 45.10)]
    [InlineData(5, 30, 83.69, 82.37)]
    public void CurvasIdf_ConferemComATabelaB3DoManual(double tr, double t, double fendrich, double parigot)
    {
        Assert.Equal(fendrich, EquacaoChuva.Fendrich().Intensidade(t, tr), 1);
        Assert.Equal(parigot, EquacaoChuva.ParigotDeSouza().Intensidade(t, tr), 1);
    }

    [Fact]
    public void Sarjeta_Exemplo61DoManual()
    {
        // Rua com 3 % de declividade transversal, n = 0,017, declividade longitudinal 0,005 m/m.
        var (toda, a1) = Hidraulica.Sarjeta(0.15, 0.03, 0.017, 0.005);
        Assert.Equal(0.375, a1, 3);
        Assert.Equal(0.272, toda, 3);
        var (sarjeta, a2) = Hidraulica.Sarjeta(0.10, 0.03, 0.017, 0.005);
        Assert.Equal(0.167, a2, 3);
        Assert.Equal(0.0925, sarjeta, 3);
        // A lâmina que escoa a vazão volta ao ponto de partida.
        Assert.Equal(0.10, Hidraulica.LaminaSarjeta(sarjeta, 0.03, 0.017, 0.005), 6);
        // Fatores da tabela 6.3, interpolados entre as declividades da tabela.
        Assert.Equal(0.50, Hidraulica.FatorSarjeta(0.4));
        Assert.Equal(0.80, Hidraulica.FatorSarjeta(2));
        Assert.Equal(0.65, Hidraulica.FatorSarjeta(4), 9);
        Assert.Equal(0.20, Hidraulica.FatorSarjeta(12));
        Assert.Equal(0.50, Hidraulica.FatorSarjeta(0.1));
    }

    [Fact]
    public void BocasDeLobo_Exemplo62DoManual()
    {
        // Guia: L = Q / (1,7·y^1,5) = 1,75 m para 94 L/s com 10 cm de lâmina.
        Assert.Equal(1.75, 0.094 / Hidraulica.BocaDeGuia(1.0, 0.10, 0.15), 2);
        // Combinada: guia padrão (L = 1,0 m) 54 L/s + grelha 0,87 × 0,29 (lado da guia fora do perímetro) 78 L/s.
        Assert.Equal(0.054, Hidraulica.BocaDeGuia(1.0, 0.10, 0.15), 3);
        Assert.Equal(0.078, Hidraulica.Grelha(0.87 + 2 * 0.29, 0.2, 0.10), 3);
        // Lâmina acima do dobro da abertura: orifício (eq. 6.6); grelha com lâmina de 0,42 m ou mais: orifício (eq. 6.7).
        Assert.Equal(3.01 * 1.0 * Math.Pow(0.10, 1.5) * Math.Sqrt((0.25 - 0.05) / 0.10), Hidraulica.BocaDeGuia(1.0, 0.25, 0.10), 9);
        Assert.Equal(2.91 * 0.2 * Math.Sqrt(0.5), Hidraulica.Grelha(1.45, 0.2, 0.5), 9);
        // Tabela 6.4: combinada no ponto baixo 65 %; grelha de barras transversais em ponto intermediário 50 %.
        Assert.Equal(0.65, Hidraulica.FatorBocaDeLobo(new DrainageDefinition { Type = TipoDrenagem.BocaDeLoboCombinada, PontoBaixo = true }));
        Assert.Equal(0.50, Hidraulica.FatorBocaDeLobo(new DrainageDefinition { Type = TipoDrenagem.GrelhaSarjeta, Bars = OrientacaoBarras.Transversal }));
        Assert.Equal(0.80, Hidraulica.FatorBocaDeLobo(new DrainageDefinition { Type = TipoDrenagem.BocaDeLoboSimples }));
    }

    [Fact]
    public void Galeria_Exemplo63DoManual()
    {
        // 94 L/s com declividade 0,001 m/m e n = 0,013: D = 0,457 m – adota-se 0,50 m.
        var lista = ProjetoDrenagem.ManualSuderhsa().Diametros;
        Assert.Equal(0.50, Hidraulica.DiametroNecessario(0.094, 0.001, 0.013, lista));
        Assert.True(Hidraulica.SecaoPlena(0.457, 0.001, 0.013).Vazao >= 0.0935);
        Assert.True(Hidraulica.SecaoPlena(0.40, 0.001, 0.013).Vazao < 0.094);
        Assert.Null(Hidraulica.DiametroNecessario(50, 0.001, 0.013, lista));
        Assert.Equal(180, Hidraulica.EspacamentoPv(1.2));
        Assert.Equal(150, Hidraulica.EspacamentoPv(0.6));
        Assert.Equal(120, Hidraulica.EspacamentoPv(0.4));
        // Kirpich (eq. 4.3) e duração mínima.
        Assert.Equal(57 * Math.Pow(0.1, 0.385), Hidraulica.Kirpich(1000, 10), 9);
    }

    // Planilha do memorial modelo: extensão, i, área acumulada, vazão, vazão máxima, cotas de terreno e de galeria, velocidade.
    private static readonly (double L, double I, double A, double Q, double Qmax, double Tm, double Tj, double Gm, double Gj, double V)[] Planilha =
    {
        (95.60, 72.68, 21.4235, 3056.85, 3102.71, 509.00, 509.00, 505.47, 504.66, 2.74),
        (78.13, 71.912, 21.4235, 3039.42, 3102.71, 509.00, 508.33, 504.66, 503.99, 2.74),
        (68.78, 71.296, 22.5927, 3190.36, 3280.15, 508.33, 507.72, 503.99, 503.34, 2.90),
        (63.40, 70.792, 23.9398, 3367.47, 3365.36, 507.72, 507.19, 503.34, 502.70, 2.98),
        (62.04, 70.345, 25.1484, 3525.15, 3608.95, 507.19, 506.80, 502.70, 501.99, 3.19),
        (43.00, 69.943, 26.5042, 3703.40, 3608.95, 506.80, 506.56, 501.99, 501.50, 3.19),
        (85.92, 69.67, 26.7674, 3731.93, 3762.59, 506.56, 505.52, 501.50, 500.42, 3.33),
        (85.59, 69.15, 27.0215, 3751.49, 3762.59, 505.52, 504.33, 500.42, 499.35, 3.33),
        (42.00, 68.63, 27.2756, 3770.91, 3837.10, 504.33, 502.60, 499.35, 498.81, 3.39),
        (34.14, 68.39, 27.9723, 3859.44, 3837.10, 502.60, 501.00, 498.81, 498.36, 3.39),
        (11.55, 68.19, 27.9723, 3853.12, 5985.80, 501.00, 500.37, 498.36, 498.00, 5.29),
        (21.66, 68.15, 27.9723, 3851.75, 5985.80, 500.37, 498.76, 498.00, 497.31, 5.29),
    };

    /// <summary>A rede do memorial modelo: 13 PVs em linha, galeria DN 1200, áreas locais = acréscimos da área acumulada.</summary>
    private static (List<MarkingDefinition> Defs, List<DrainagePipeDefinition> Tubos) RedeDoModelo()
    {
        var pvs = new List<DrainageDefinition>();
        var x = 0.0;
        for (int k = 0; k <= Planilha.Length; k++)
        {
            var terreno = k < Planilha.Length ? Planilha[k].Tm : Planilha[^1].Tj;
            var pv = Pv(x, terreno);
            var area = k == 0 ? Planilha[0].A : k < Planilha.Length ? Planilha[k].A - Planilha[k - 1].A : 0;
            pv.AreaContribuicao = Math.Round(area * 10000, 2);
            if (k == 0) pv.TempoConcentracao = 26.022;   // a planilha começa com t ≈ 26 min (George Ribeiro, não reproduzido)
            pvs.Add(pv);
            if (k < Planilha.Length) x += Planilha[k].L;
        }
        var tubos = Planilha.Select((r, k) => Tubo(pvs[k], pvs[k + 1], 1.2, r.Gm, r.Gj)).ToList();
        // Ordem embaralhada no projeto: a planilha sai de montante para jusante assim mesmo.
        return (tubos.AsEnumerable().Reverse().Cast<MarkingDefinition>().Concat(pvs).ToList(), tubos);
    }

    [Fact]
    public void PlanilhaDoMemorialModelo_ReproduzidaComOsCriteriosDele()
    {
        var (defs, tubos) = RedeDoModelo();
        var p = ProjetoDrenagem.MemorialModelo();
        p.Rugosidade = 0.01506;   // o n que reproduz a vazão máxima da planilha (o memorial não o escreve)
        var r = DrainageHydraulics.Calcular(defs, p);
        Assert.Equal(12, r.Trechos.Count);
        Assert.Equal(tubos.Select(t => t.Id), r.Trechos.Select(t => t.Id));
        for (int k = 0; k < 12; k++)
        {
            var (t, e) = (r.Trechos[k], Planilha[k]);
            output.WriteLine($"{t.Nome}: i {t.Intensidade:0.000} ({e.I}), A {t.AreaHa:0.0000}, Q {t.Vazao:0.00} ({e.Q}), Qmax {t.VazaoMaxima:0.00} ({e.Qmax}), " +
                             $"v {t.Velocidade:0.00} ({e.V}), C {t.Coeficiente:0.0000}, t {t.Duracao:0.000}");
            Assert.Equal($"T{k + 1}", t.Nome);
            Assert.Equal(e.L, t.Extensao, 2);
            Assert.Equal(e.A, t.AreaHa, 3);
            Assert.True(Math.Abs(t.Intensidade - e.I) < 0.05, $"i do trecho {k + 1}");
            Assert.True(Math.Abs(t.Vazao / e.Q - 1) < 0.003, $"Q do trecho {k + 1}");
            // A planilha usa a declividade arredondada (0,0085…); com as cotas exatas a capacidade muda até ~1,3 %.
            Assert.True(Math.Abs(t.VazaoMaxima / e.Qmax - 1) < 0.015, $"Qmax do trecho {k + 1}");
            Assert.True(Math.Abs(t.Velocidade - e.V) < 0.05, $"v do trecho {k + 1}");
            Assert.Equal(e.Tm - e.Gm, t.ProfundidadeMontante, 6);
            Assert.Equal(e.Tj - e.Gj, t.ProfundidadeJusante, 6);
        }
        // C de Horner com 80 % de área impermeável: 0,707 no primeiro trecho, 0,727 no último (como na planilha).
        Assert.Equal(0.707, r.Trechos[0].Coeficiente, 2);
        Assert.Equal(0.727, r.Trechos[^1].Coeficiente, 2);
        // Na própria planilha, o trecho 6 passa da capacidade a seção plena (3 703,40 > 3 608,95 L/s, +2,6 %); com as cotas
        // exatas (declividade 0,01140, não 0,0115) dá +3,0 %, e o plugin aponta – e só ele.
        var cap = r.Problemas.Where(x => x.Criterio == "capacidade").Select(x => x.Elemento[..3].Trim()).ToList();
        foreach (var x in r.Problemas) output.WriteLine(x.Texto);
        Assert.Equal(new[] { "T6" }, cap);
        // Com n = 0,015 da predefinição, as vazões ficam a menos de 1 % das da planilha.
        var r2 = DrainageHydraulics.Calcular(defs, ProjetoDrenagem.MemorialModelo());
        Assert.All(r2.Trechos.Zip(Planilha), z => Assert.True(Math.Abs(z.First.Vazao / z.Second.Q - 1) < 0.01));
        var csv = DrainageHydraulics.Csv(r, p);
        Assert.Contains("PLANILHA DE DIMENSIONAMENTO DE GALERIAS", csv);
        Assert.Contains("T1;PV-1;PV-2;galeria;95,60;", csv);
    }

    [Fact]
    public void Rede_TempoDeConcentracaoEAreasNasJuncoes()
    {
        // Dois ramos chegam ao PV c: o tempo de concentração é o maior que chega; as áreas somam.
        var a = Pv(0, 100); a.AreaContribuicao = 10000; a.TempoConcentracao = 12; a.CoeficienteEscoamento = 0.9;
        var b = Pv(0, 100, 80); b.AreaContribuicao = 20000; b.CoeficienteEscoamento = 0.5;
        var c = Pv(60, 99.5);
        var d = Pv(120, 99);
        var p = ProjetoDrenagem.ManualSuderhsa();
        var defs = new MarkingDefinition[] { d, c, b, a, Tubo(c, d, 0.6), Tubo(a, c, 0.4), Tubo(b, c, 0.5) };
        var r = DrainageHydraulics.Calcular(defs, p);
        Assert.Equal(new[] { "T1", "T2", "T3" }, r.Trechos.Select(t => t.Nome));
        var ac = r.Trechos.Single(t => t.Montante == "PV-4");
        var bc = r.Trechos.Single(t => t.Montante == "PV-3");
        var cd = r.Trechos.Single(t => t.Montante == "PV-2");
        Assert.Equal(12, ac.Duracao, 6);
        Assert.Equal(10, bc.Duracao, 6);   // sem tempo próprio: o tempo inicial do projeto
        var chegada = Math.Max(ac.Duracao + ac.Extensao / ac.Velocidade / 60, bc.Duracao + bc.Extensao / bc.Velocidade / 60);
        Assert.Equal(chegada, cd.Duracao, 6);
        Assert.Equal(3, cd.AreaHa, 9);
        Assert.Equal((0.9 * 1 + 0.5 * 2) / 3, cd.Coeficiente, 9);
        var i = p.Chuva!.Intensidade(cd.Duracao, p.TempoRetorno);
        Assert.Equal(Hidraulica.Racional(cd.Coeficiente, i, 3), cd.Vazao, 6);
        // Vazão externa entra sem chuva e segue para jusante.
        c.VazaoExterna = 100;
        Assert.Equal(cd.Vazao + 100, DrainageHydraulics.Calcular(defs, p).Trechos.Single(t => t.Montante == "PV-2").Vazao, 6);
        // Sem equação de chuva: nada é calculado.
        Assert.Empty(DrainageHydraulics.Calcular(defs, new ProjetoDrenagem()).Trechos);
    }

    [Fact]
    public void Dimensionamento_IndicaEAplicaOMenorDiametro()
    {
        var a = Pv(0, 100); a.AreaContribuicao = 60000; a.CoeficienteEscoamento = 0.8;
        var b = Pv(80, 99.6);
        var t = Tubo(a, b, 0.40, 98.5, 98.1);   // 0,5 %
        var defs = new MarkingDefinition[] { a, b, t };
        var p = ProjetoDrenagem.ManualSuderhsa();
        var r = DrainageHydraulics.Calcular(defs, p);
        var tr = Assert.Single(r.Trechos);
        output.WriteLine($"Q {tr.Vazao:0.0} L/s, Qmax {tr.VazaoMaxima:0.0}, D necessário {tr.DiametroNecessario}");
        Assert.True(tr.Vazao > tr.VazaoMaxima);
        Assert.Contains(r.Problemas, x => x.Criterio == "capacidade" && x.Texto.Contains($"{tr.DiametroNecessario!.Value * 1000:0} mm"));
        var dim = DrainageHydraulics.Calcular(defs, p, dimensionar: true);
        Assert.True(dim.Dimensionado);
        Assert.Equal(tr.DiametroNecessario, dim.Trechos[0].Diametro);
        Assert.DoesNotContain(dim.Problemas, x => x.Criterio == "capacidade");
        // Aplicado ao tubo, a verificação passa.
        t.Diameter = tr.DiametroNecessario!.Value;
        Assert.DoesNotContain(DrainageHydraulics.Calcular(defs, p).Problemas, x => x.Criterio == "capacidade");
    }

    [Fact]
    public void Captacao_SarjetaEEngolimentoComAAreaLocal()
    {
        var bl = new DrainageDefinition
        {
            Type = TipoDrenagem.BocaDeLoboCombinada, Position = new Vec2(0, 0), Z = 100, AreaContribuicao = 1000, CoeficienteEscoamento = 0.9,
            DeclividadeSarjeta = 2, OpeningLength = 1.0, OpeningHeight = 0.15, GrateLength = 0.87, GrateWidth = 0.29,
        };
        var pv = Pv(10, 99.9);
        var p = ProjetoDrenagem.ManualSuderhsa();
        var defs = new MarkingDefinition[] { bl, pv, Tubo(bl, pv, 0.4) };
        var r = DrainageHydraulics.Calcular(defs, p);
        var c = Assert.Single(r.Captacoes);
        var i = p.Chuva!.Intensidade(10, 2);
        Assert.Equal(Hidraulica.Racional(0.9, i, 0.1), c.Vazao, 6);
        Assert.Equal(Hidraulica.Sarjeta(0.10, 0.03, 0.017, 0.02).Vazao * 1000 * 0.80, c.CapacidadeSarjeta!.Value, 6);
        Assert.True(c.Lamina < 0.10);
        Assert.Equal(0.55, c.Fator);   // combinada, ponto intermediário, barras transversais
        Assert.True(r.Trechos.Single().Ligacao);
        Assert.Empty(r.Problemas.Where(x => x.Criterio is "sarjeta" or "engolimento"));
        // Área grande demais para a sarjeta e para a boca de lobo.
        bl.AreaContribuicao = 60000;
        var r2 = DrainageHydraulics.Calcular(defs, p);
        Assert.Contains(r2.Problemas, x => x.Criterio == "sarjeta");
        Assert.Contains(r2.Problemas, x => x.Criterio == "engolimento");
    }

    [Fact]
    public void Geometria_PvDeQuedaEspacamentoEGeratrizSuperior()
    {
        var a = Pv(0, 100);
        var b = Pv(200, 99);
        var c = Pv(260, 98);
        var defs = new MarkingDefinition[] { a, b, c, Tubo(a, b, 0.4, 97, 96.0), Tubo(b, c, 0.6, 95.0, 94.7) };
        var probs = DrainageNetwork.Check(defs, ProjetoDrenagem.ManualSuderhsa());
        foreach (var x in probs) output.WriteLine(x.Texto);
        Assert.Contains(probs, x => x.Criterio == "espaçamento" && x.Texto.Contains("máximo de 120 m"));
        Assert.Contains(probs, x => x.Criterio == "queda" && x.Texto.Contains("1,00 m"));
        Assert.DoesNotContain(probs, x => x.Criterio == "alinhamento");   // o tubo maior sai mais baixo
        var defs2 = new MarkingDefinition[] { a, b, c, Tubo(a, b, 0.4, 97, 96.0), Tubo(b, c, 0.6, 96.0, 95.7) };
        Assert.Contains(DrainageNetwork.Check(defs2, ProjetoDrenagem.ManualSuderhsa()), x => x.Criterio == "alinhamento");
    }

    [Fact]
    public void Validacao_V13ComAVazao()
    {
        var a = Pv(0, 100); a.AreaContribuicao = 60000; a.CoeficienteEscoamento = 0.8;
        var b = Pv(80, 99.6);
        var t = Tubo(a, b, 0.40, 98.5, 98.1);
        var copias = new MarkingDefinition[] { a, b, t }.Select((d, i) => new CopiaGravada(d.Id, d.ToJson(), $"e{i}")).ToList();
        var v = ModelValidator.Validate(copias, drenagem: ProjetoDrenagem.ManualSuderhsa());
        Assert.Contains(v, x => x.Codigo == "V13" && x.Mensagem.Contains("capacidade a seção plena") && x.Mensagem.Contains("SUDERHSA"));
        // Sem chuva, a vazão não é verificada.
        Assert.DoesNotContain(ModelValidator.Validate(copias, drenagem: new ProjetoDrenagem()), x => x.Mensagem.Contains("capacidade"));
        // Os campos novos do dispositivo são gravados.
        var back = Assert.IsType<DrainageDefinition>(MarkingDefinition.FromJson(a.ToJson()));
        Assert.Equal(60000, back.AreaContribuicao);
        Assert.Equal(0.8, back.CoeficienteEscoamento);
    }

    [Fact]
    public void Memorial_DocxNaEstruturaDoModelo()
    {
        var (defs, _) = RedeDoModelo();
        var p = ProjetoDrenagem.MemorialModelo();
        (p.Obra, p.Municipio, p.Autor, p.Registro, p.Proprietario) = ("Loteamento Teste", "Cidade/UF", "Fulano de Tal", "CREA 123", "Prefeitura");
        var doc = DrainageMemorial.Build(defs, p, new DateTime(2026, 10, 22));
        Assert.Equal(new[] { DrainageMemorial.SecProjeto, DrainageMemorial.SecParametros, DrainageMemorial.SecPlanilha, DrainageMemorial.SecServicos,
            DrainageMemorial.SecQuantitativo, DrainageMemorial.SecFinais }, doc.Sections);
        var textos = doc.Blocks.OfType<MemParagraph>().Select(x => x.Text).ToList();
        Assert.Contains(textos, x => x.Contains("ln(T − 0,5)"));
        Assert.Contains(textos, x => x.Contains("fórmula de Horner") && x.Contains("80 %"));
        Assert.Contains(textos, x => x.Contains("0,80 m sobre a geratriz superior externa"));
        var planilha = doc.Blocks.OfType<MemTable>().First(x => x.Headers.Contains("Vazão máx. (L/s)"));
        Assert.Equal(12, planilha.Rows.Count);
        Assert.Equal(2, doc.Blocks.OfType<MemSection>().Count());
        // Quantitativo: tubo DN 1200 = soma das extensões (691,81 m; o modelo arredonda para 692) e 13 PVs.
        var q = DrainageMemorial.Quantitativo(defs, p);
        Assert.Equal(691.81, q.Single(x => x.Descricao.Contains("DN 1200")).Quantidade, 2);
        Assert.Equal(13, q.Single(x => x.Descricao == "Poço de visita").Quantidade);
        Assert.Contains(q, x => x.Descricao.StartsWith("Escavação"));

        var bytes = DocxWriter.Write(doc);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        foreach (var part in DocxWriter.Parts.Append("word/header1.xml").Append("word/footer1.xml")) Assert.NotNull(zip.GetEntry(part));
        var xml = new StreamReader(zip.GetEntry("word/document.xml")!.Open()).ReadToEnd();
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(xml, "<w:sectPr>").Count);
        Assert.Contains("w:orient=\"landscape\"", xml);
        Assert.Contains("w:type=\"page\"", xml);
        _ = System.Xml.Linq.XDocument.Parse(xml);
        var footer = new StreamReader(zip.GetEntry("word/footer1.xml")!.Open()).ReadToEnd();
        Assert.Contains("NUMPAGES", footer);
        Assert.Contains("FULANO DE TAL", new StreamReader(zip.GetEntry("word/header1.xml")!.Open()).ReadToEnd());
        Assert.Contains("footer1.xml", new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open()).ReadToEnd());
        // O memorial geral continua sem cabeçalho nem rodapé.
        using var zip2 = new ZipArchive(new MemoryStream(DocxWriter.Write(new MemorialDocument())));
        Assert.Null(zip2.GetEntry("word/footer1.xml"));
    }
}
