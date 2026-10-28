using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Reports;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 22 (itens 6 e 21): drenagem completa – catálogo de tubos, peças, juntas, conexões e vala por trecho no quantitativo e
/// no memorial; linha piezométrica e de energia com o nível do lançamento; bueiros; rede gerada ao longo da via.
/// </summary>
public class Etapa22DrenagemTests
{
    private static List<CopiaGravada> Copias(params MarkingDefinition[] defs) => defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e-" + d.Id)).ToList();

    private static DrainageDefinition Pv(double x, double z, double y = 0) =>
        new() { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(x, y), Z = z, BoxDepth = 1.6, BoxLength = 1.0, PipeDiameter = 0.4 };

    private static DrainagePipeDefinition Tubo(DrainageDefinition a, DrainageDefinition b, double d, double? fa = null, double? fb = null) =>
        new() { FromId = a.Id, ToId = b.Id, Diameter = d, Z = a.Z, InvertFrom = fa, InvertTo = fb };

    [Fact]
    public void Catalogo_padrao_escolha_do_tubo_e_texto()
    {
        var p = ProjetoDrenagem.ManualSuderhsa();
        var padrao = CatalogoTubos.Padrao(p);
        Assert.Equal(p.Diametros.Count, padrao.Count);
        Assert.All(padrao, t => Assert.Equal((CatalogoTubos.FontePadrao, 1.0, "ponta e bolsa"), (t.Fonte, t.ComprimentoPeca, t.Junta)));
        var a = Pv(0, 100);
        var b = Pv(40, 99.6);
        var t600 = Tubo(a, b, 0.60);
        var (tubo, cat) = CatalogoTubos.Do(t600, p);
        Assert.True(cat);
        Assert.Equal("CONC-600", tubo.Codigo);
        Assert.Equal("Tubo de concreto DN 600 (ponta e bolsa)", tubo.Nome);
        // Diâmetro fora do catálogo: concreto do padrão do plugin, sem código.
        var (fora, cat2) = CatalogoTubos.Do(Tubo(a, b, 0.45), p);
        Assert.False(cat2);
        Assert.Equal(0.45, fora.Diametro);

        // Catálogo do usuário: o código gravado vale; com outro diâmetro, o do mesmo material e classe.
        p.Tubos = CatalogoTubos.Ler("código; material; classe; DN (mm); parede; peça; junta; n; fonte\n" +
                                    "PEAD-600; PEAD corrugado; SN 8; 600; 35; 6; elástica; 0,010; fabricante X\n" +
                                    "PEAD-800; PEAD corrugado; SN 8; 800; 45; 6; elástica; 0,010; fabricante X\n" +
                                    "CA-800; concreto; PA-2; 800; ; 1,5; ponta e bolsa; ; catálogo Y", out var erros);
        Assert.Empty(erros);
        Assert.Equal(3, p.Tubos.Count);
        Assert.Equal((0.035, 6.0, 0.010), (p.Tubos[0].Parede!.Value, p.Tubos[0].ComprimentoPeca, p.Tubos[0].Rugosidade!.Value));
        Assert.Null(p.Tubos[2].Parede);
        Assert.Null(p.Tubos[2].Rugosidade);
        t600.Tubo = "PEAD-600";
        Assert.Equal("PEAD-600", CatalogoTubos.Do(t600, p).Tubo.Codigo);
        t600.Diameter = 0.80;                                                  // "Aplicar os diâmetros calculados" mudou o DN
        Assert.Equal("PEAD-800", CatalogoTubos.Do(t600, p).Tubo.Codigo);
        t600.Tubo = null;
        Assert.Equal("PEAD-800", CatalogoTubos.Do(t600, p).Tubo.Codigo);       // o primeiro com o DN
        Assert.Equal("Tubo de PEAD corrugado DN 800 SN 8 (elástica)", CatalogoTubos.Do(t600, p).Tubo.Nome);
        // Ida e volta pelo texto e pelo JSON do projeto.
        var volta = CatalogoTubos.Ler(CatalogoTubos.Texto(p.Tubos), out var e2);
        Assert.Empty(e2);
        Assert.Equal(CatalogoTubos.Texto(p.Tubos), CatalogoTubos.Texto(volta));
        var json = ProjetoDrenagem.FromJson(p.ToJson());
        Assert.Equal(CatalogoTubos.Texto(p.Tubos), CatalogoTubos.Texto(json.Tubos));
        // Linhas inválidas ficam de fora, com o motivo.
        CatalogoTubos.Ler("X; concreto; ; 30\nY; ; ; 400; ; 0\nZ; ; ; 400\nZ; ; ; 500", out var erros3);
        Assert.Equal(3, erros3.Count);
        Assert.Contains(erros3, x => x.Contains("DN"));
        Assert.Contains(erros3, x => x.Contains("peça"));
        Assert.Contains(erros3, x => x.Contains("repetido"));
        // A predefinição não apaga o catálogo, o berço, o talude nem a perda nos PVs.
        (p.EspessuraBerco, p.TaludeVala, p.PerdaPv) = (0.1, 0.5, 0.8);
        p.AplicarCriterios(ProjetoDrenagem.MemorialModelo());
        Assert.Equal(3, p.Tubos.Count);
        Assert.Equal((0.1, 0.5, 0.8), (p.EspessuraBerco, p.TaludeVala, p.PerdaPv));
        Assert.Equal(0.015, p.Rugosidade);                                     // os critérios da fonte vieram
        // O clone é independente.
        var c = p.Clone();
        c.Tubos[0].Material = "outro";
        Assert.Equal("PEAD corrugado", p.Tubos[0].Material);
    }

    [Fact]
    public void Pecas_juntas_conexoes_e_vala_conferidas_a_mao()
    {
        var a = Pv(0, 100);
        var b = Pv(40, 99.6);
        var t = Tubo(a, b, 0.60, 98.0, 97.8);
        var defs = new MarkingDefinition[] { a, b, t };
        var p = ProjetoDrenagem.ManualSuderhsa();
        var e = DrainageNetwork.Ends(t, id => defs.First(d => d.Id == id))!;
        var tt = Assert.Single(CatalogoTubos.Trechos(defs, p));
        Assert.Equal(40, tt.Extensao, 6);
        Assert.Equal(40 - e.InsetA - e.InsetB, tt.ExtensaoTubo, 6);           // 39 m entre as paredes (caixas de 1 m)
        Assert.Equal((int)Math.Ceiling(tt.ExtensaoTubo - 1e-6), tt.Pecas);
        Assert.Equal(tt.Pecas - 1, tt.Juntas);
        Assert.Equal(2, tt.Conexoes);
        // Vala: largura 2 × 0,60 = 1,20 m (maior que o externo 0,696); profundidade das tampas ao fundo externo: 2,048 e 1,848.
        var parede = DrainageNetwork.Wall(0.6);
        Assert.Equal(0.048, parede, 9);
        Assert.Equal(1.2, tt.Vala.Largura, 9);
        Assert.Equal(1.948, tt.Vala.ProfundidadeMedia, 6);
        Assert.Equal(1.2 * 1.948 * 40, tt.Vala.Escavacao, 6);                  // o mesmo do quantitativo antigo
        Assert.Equal(2 * 40 * 1.948, tt.Vala.Escoramento, 6);
        var vt = Math.PI * 0.348 * 0.348 * 40;
        Assert.Equal(vt, tt.Vala.VolumeTubo, 6);
        Assert.Equal(1.2 * 1.948 * 40 - vt, tt.Vala.Reaterro, 6);
        Assert.Equal(0, tt.Vala.Berco);
        // Com berço de 10 cm: a vala desce 0,10 m e o reaterro desconta o berço.
        p.EspessuraBerco = 0.10;
        var b10 = CatalogoTubos.Trechos(defs, p)[0].Vala;
        Assert.Equal(2.048, b10.ProfundidadeMedia, 6);
        Assert.Equal(1.2 * 0.10 * 40, b10.Berco, 6);
        Assert.Equal(1.2 * 2.048 * 40 - vt - 4.8, b10.Reaterro, 6);
        // Com talude 1 : 1 (sem berço): (b + h)·h com h linear de 2,048 a 1,848; sem escoramento.
        p.EspessuraBerco = 0;
        p.TaludeVala = 1;
        var tal = CatalogoTubos.Trechos(defs, p)[0].Vala;
        var (h0, h1) = (2.048, 1.848);
        var esperado = (1.2 * (h0 + h1) / 2 + (h0 * h0 + h0 * h1 + h1 * h1) / 3) * 40;
        Assert.Equal(esperado, tal.Escavacao, 1);
        Assert.Equal(0, tal.Escoramento);

        // Quantitativo: tubo, peças, juntas e conexões; com o padrão, as linhas antigas iguais.
        p.TaludeVala = 0;
        var q = DrainageMemorial.Quantitativo(defs, p);
        Assert.Equal(40, q.Single(x => x.Descricao == "Tubo de concreto DN 600 (ponta e bolsa)").Quantidade, 6);
        Assert.Equal(tt.Pecas, q.Single(x => x.Descricao.StartsWith("Peças – tubo de concreto DN 600")).Quantidade);
        Assert.Equal(tt.Juntas, q.Single(x => x.Descricao.StartsWith("Juntas ponta e bolsa – tubo de concreto DN 600")).Quantidade);
        Assert.Equal(2, q.Single(x => x.Descricao.StartsWith("Conexões")).Quantidade);
        Assert.Equal(1.2 * 1.948 * 40, q.Single(x => x.Descricao.StartsWith("Escavação")).Quantidade, 6);
        Assert.DoesNotContain(q, x => x.Descricao.StartsWith("Berço"));
        // Peça de 6 m do catálogo: 7 peças para 39 m.
        p.Tubos = CatalogoTubos.Ler("PEAD-600; PEAD corrugado; ; 600; ; 6; elástica", out _);
        var pead = CatalogoTubos.Trechos(defs, p)[0];
        Assert.Equal((7, 6), (pead.Pecas, pead.Juntas));
        Assert.Contains(DrainageMemorial.Quantitativo(defs, p), x => x.Descricao == "Tubo de PEAD corrugado DN 600 (elástica)");
    }

    [Fact]
    public void Lamina_normal_na_secao_circular()
    {
        var (d, s, n) = (0.6, 0.005, 0.013);
        var plena = Hidraulica.SecaoPlena(d, s, n).Vazao;
        // Meia seção: mesma R = D/4, metade da área → metade da vazão plena.
        Assert.Equal(d / 2, LinhaPiezometrica.LaminaNormal(plena / 2, d, s, n)!.Value, 6);
        Assert.Equal(plena, LinhaPiezometrica.Vazao(d, d, s, n), 9);
        // A máxima é a 0,938 D (≈ 1,076 × a plena); acima dela, sem lâmina normal.
        Assert.InRange(LinhaPiezometrica.Vazao(d, 0.938 * d, s, n) / plena, 1.07, 1.08);
        Assert.Null(LinhaPiezometrica.LaminaNormal(plena * 1.1, d, s, n));
        Assert.Null(LinhaPiezometrica.LaminaNormal(0.01, d, 0, n));
        Assert.Equal(0, LinhaPiezometrica.LaminaNormal(0, d, s, n));
        var (area, per) = LinhaPiezometrica.SecaoParcial(d, d);
        Assert.Equal(Math.PI * d * d / 4, area, 9);
        Assert.Equal(Math.PI * d, per, 9);
    }

    /// <summary>Rede a → b → c, 50 m cada, DN 600 a 0,5 %, com a área em a (Manual SUDERHSA, Fendrich).</summary>
    private static (DrainageDefinition A, DrainageDefinition B, DrainageDefinition C, DrainagePipeDefinition T1, DrainagePipeDefinition T2) Rede()
    {
        var a = Pv(0, 100);
        a.AreaContribuicao = 15000;
        a.CoeficienteEscoamento = 0.8;
        var b = Pv(50, 99.5);
        var c = Pv(100, 99.0);
        return (a, b, c, Tubo(a, b, 0.6, 98.5, 98.25), Tubo(b, c, 0.6, 98.2, 97.95));
    }

    private static CorpoDaguaDefinition LagoAoLado(double nivel)
    {
        var lago = new CorpoDaguaDefinition { Corpo = TipoCorpoDagua.Lago, NivelAgua = nivel, CotaFundo = nivel - 2, TaludeMargem = 2, Nome = "Teste" };
        lago.SetPath(PathReference.FromPoints(new[] { new Vec2(110, -20), new Vec2(160, -20), new Vec2(160, 20), new Vec2(110, 20) }, 0));
        return lago;
    }

    [Fact]
    public void Piezometrica_saida_livre_remanso_do_lago_e_extravasamento()
    {
        var (a, b, c, t1, t2) = Rede();
        var p = ProjetoDrenagem.ManualSuderhsa();
        var defs = new List<MarkingDefinition> { a, b, c, t1, t2 };
        var hid = DrainageHydraulics.Calcular(defs, p);
        var h2 = hid.Trechos.Single(t => t.Id == t2.Id);
        Assert.True(h2.Vazao < h2.VazaoMaxima);                               // dentro da capacidade

        // Saída livre: no último trecho, o nível é o fundo + a lâmina normal; nada afogado.
        var r = LinhaPiezometrica.Calcular(defs, p, hid);
        Assert.True(r.Calculada);
        var p2 = r.Trechos.Single(t => t.Id == t2.Id);
        var yn = LinhaPiezometrica.LaminaNormal(h2.Vazao / 1000, 0.6, h2.Declividade, p.Rugosidade)!.Value;
        Assert.Equal(97.95 + yn, p2.NivelJusante, 9);
        Assert.Equal(98.2 + yn, p2.NivelMontante, 9);
        Assert.Equal(yn, p2.LaminaNormal!.Value, 9);
        Assert.False(p2.Afogado);
        Assert.True(p2.EnergiaMontante > p2.NivelMontante);
        Assert.Empty(r.Problemas);
        Assert.Contains("saída livre", r.Lancamentos.Single().Value);
        Assert.Equal(new[] { t1.Id, t2.Id }, r.Trechos.Select(t => t.Id));    // de montante para jusante

        // Lago a 10 m do lançamento com o nível acima da geratriz superior (98,55): o T2 fica sob pressão.
        var lago = LagoAoLado(98.9);
        defs.Add(lago);
        r = LinhaPiezometrica.Calcular(defs, p);
        p2 = r.Trechos.Single(t => t.Id == t2.Id);
        Assert.Contains("nível d'água informado de Lago Teste", r.Lancamentos.Single().Value);
        Assert.Equal(98.9, p2.NivelJusante, 9);
        var area = Math.PI * 0.36 / 4;
        var sf = Math.Pow(h2.Vazao / 1000 * p.Rugosidade / (area * Math.Pow(0.15, 2.0 / 3)), 2);
        Assert.Equal(98.9 + sf * h2.Extensao, p2.NivelMontante, 9);
        Assert.True(p2.SobPressao && p2.Afogado);
        Assert.Contains(r.Problemas, x => x.Criterio == "afogamento" && x.Ids!.Contains(t2.Id));
        Assert.DoesNotContain(r.Problemas, x => x.Criterio == "extravasamento");
        // O nível no PV b é o de montante do T2; ele é a condição de jusante do T1.
        Assert.Equal(p2.NivelMontante, r.Dispositivos.Single(d => d.Id == b.Id).Nivel, 9);
        Assert.Equal(Math.Max(98.25 + LinhaPiezometrica.LaminaNormal(hid.Trechos.Single(t => t.Id == t1.Id).Vazao / 1000, 0.6, 0.005, p.Rugosidade)!.Value,
            p2.NivelMontante), r.Trechos.Single(t => t.Id == t1.Id).NivelJusante, 9);

        // Perda localizada K = 1 na entrada do T2: o nível em b sobe V²/2g.
        p.PerdaPv = 1;
        var rk = LinhaPiezometrica.Calcular(defs, p);
        var v = rk.Trechos.Single(t => t.Id == t2.Id).Velocidade;
        Assert.Equal(p2.NivelMontante + v * v / (2 * LinhaPiezometrica.G), rk.Dispositivos.Single(d => d.Id == b.Id).Nivel, 9);
        p.PerdaPv = 0;

        // Lago acima da tampa do lançamento (99,0): o PV c extravasa – na tela e no Validar Modelo (V13).
        defs.Remove(lago);
        defs.Add(LagoAoLado(99.3));
        r = LinhaPiezometrica.Calcular(defs, p);
        var ex = Assert.Single(r.Problemas, x => x.Criterio == "extravasamento" && x.Ids!.Contains(c.Id));
        Assert.Contains("acima da tampa", ex.Texto);
        Assert.True(r.Dispositivos.Single(d => d.Id == c.Id).Extravasa);
        var tela = RedeDrenagemTela.Montar(defs, p, null, false, null);
        Assert.Contains(tela.Secao(CategoriaRede.Hidraulica).Itens, i => i.Criterio == "extravasamento");
        Assert.Contains(tela.Secao(CategoriaRede.Hidraulica).Notas, n => n.Texto.StartsWith("Linha piezométrica:") && n.Nivel == NivelSituacao.Aviso);
        Assert.True(tela.Piezometrica.Calculada);
        Assert.Equal(2, tela.Tubos.Count);
        var v13 = ModelValidator.Validate(Copias(defs.ToArray()), drenagem: p).Where(x => x.Codigo == "V13").ToList();
        Assert.Contains(v13, x => x.Mensagem.Contains("extravasa"));
        // CSV e memorial.
        Assert.Contains("LINHA PIEZOMÉTRICA", LinhaPiezometrica.Csv(r));
        Assert.Contains("sim", LinhaPiezometrica.Csv(r).Split('\n').Single(l => l.StartsWith("PV-3;")));
        var doc = DrainageMemorial.Build(defs, p, new DateTime(2026, 10, 28));
        Assert.Contains(doc.Blocks.OfType<MemHeading>(), h => h.Text == "Linha piezométrica");
        Assert.Contains(doc.Blocks.OfType<MemHeading>(), h => h.Text == "Tubos, peças e valas por trecho");
        Assert.Contains(doc.Blocks.OfType<MemBullets>().SelectMany(x => x.Items), x => x.Contains("extravasa"));
        // Sem equação de chuva: não calculada.
        p.Chuva = null;
        Assert.False(LinhaPiezometrica.Calcular(defs, p).Calculada);
        Assert.Contains("não calculada", LinhaPiezometrica.Csv(LinhaPiezometrica.Calcular(defs, p)));
    }

    [Fact]
    public void Rugosidade_do_tubo_do_catalogo_no_calculo()
    {
        var (a, b, c, t1, t2) = Rede();
        var p = ProjetoDrenagem.ManualSuderhsa();
        var defs = new MarkingDefinition[] { a, b, c, t1, t2 };
        var antes = DrainageHydraulics.Calcular(defs, p).Trechos.Single(t => t.Id == t1.Id).VazaoMaxima;
        p.Tubos = CatalogoTubos.Ler("PEAD-600; PEAD corrugado; ; 600; ; 6; elástica; 0,010", out _);
        var depois = DrainageHydraulics.Calcular(defs, p).Trechos.Single(t => t.Id == t1.Id).VazaoMaxima;
        Assert.Equal(antes * 0.013 / 0.010, depois, 6);
    }
}
