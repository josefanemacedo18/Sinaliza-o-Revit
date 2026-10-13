using System.IO.Compression;
using System.Text;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Core.Reports;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Loteamento: lotes pela testada (R1), numeração por quadra (R2), categorias (R3), lotes no relevo (R4), quadros e memorial
/// (R5), detalhamento (R6), reconhecimento da gleba, das vias e das quadras (R7) e masterplan (R8).
/// </summary>
public class LTLoteamentoTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static DivisaoQuadra Div(QuadraDefinition q) => LotDivider.Divide(q);

    private static Parcela ByKey(DivisaoQuadra d, string key) => d.Parcelas.Single(p => p.Chave == key);

    public static IEnumerable<object[]> Scenes() => LTScenes.Names.Select(n => new object[] { n });

    // ================================================================== R1 – lotes pela testada

    [Theory]
    [MemberData(nameof(Scenes))]
    public void R1_SomaDasAreasIgualAreaDaQuadra(string scene)
    {
        var d = Div(LTScenes.Scene(scene));
        Assert.NotEmpty(d.Parcelas);
        Assert.True(Math.Abs(d.Parcelas.Sum(p => p.Area) - d.Quadra.Area) <= 0.01, $"{scene}: soma {d.Parcelas.Sum(p => p.Area):0.000} ≠ quadra {d.Quadra.Area:0.000}");
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void R1_LotesNaoSeSobrepoemEFicamNaQuadra(string scene)
    {
        var d = Div(LTScenes.Scene(scene));
        for (int i = 0; i < d.Parcelas.Count; i++)
        {
            var outside = PolygonOps.TotalArea(PolygonOps.Difference(new[] { d.Parcelas[i].Forma }, new[] { d.Quadra }));
            Assert.True(outside < 1e-3, $"{scene}: {d.Parcelas[i].Chave} sai {outside:0.0000} m² da quadra");
            for (int j = i + 1; j < d.Parcelas.Count; j++)
            {
                var ov = PolygonOps.TotalArea(PolygonOps.Intersect(new[] { d.Parcelas[i].Forma }, new[] { d.Parcelas[j].Forma }));
                Assert.True(ov < 1e-3, $"{scene}: {d.Parcelas[i].Chave} e {d.Parcelas[j].Chave} sobrepostos em {ov:0.0000} m²");
            }
        }
    }

    [Fact]
    public void R1_FrentePadraoAoLongoDaTestada_12x30()
    {
        var q = LTScenes.Scene("uma-testada");
        q.Miolo = ModoMiolo.AreaPropria;
        var d = Div(q);
        // 100 m = 8 × 12 + 4: a sobra de 4 m (menor que a frente mínima de 5 m) vai para o último lote.
        var lots = d.Parcelas.Where(p => p.TestadaId == "A").OrderBy(p => p.Forma.Centroid.X).ToList();
        Assert.Equal(8, lots.Count);
        Assert.All(lots.SkipLast(1), p => Assert.Equal(12, p.Frente, 6));
        Assert.All(lots.SkipLast(1), p => Assert.Equal(360, p.Area, 2));
        Assert.Equal(16, lots[^1].Frente, 6);
        // Divisas perpendiculares à testada: cada lote é um retângulo 12 × 30.
        Assert.All(lots.SkipLast(1), p => { var (mn, mx) = p.Forma.Bounds; Assert.Equal(12, mx.X - mn.X, 3); Assert.Equal(30, mx.Y - mn.Y, 3); });
    }

    /// <summary>Quadra de 102 m de testada (8 × 12 + 6) e 60 m de fundo.</summary>
    private static QuadraDefinition Q102(double fundo = 60) =>
        LTScenes.Q("S", new List<Vec2> { new(0, 0), new(102, 0), new(102, fundo), new(0, fundo) }, new Vec2(51, 0));

    [Fact]
    public void R1_SobraViraOUltimoLote_LoteNormal()
    {
        // 102 m = 8 × 12 + 6: a sobra de 6 m × 30 m = 180 m² atinge a frente (5 m) e a área (125 m²) mínimas – é o último lote,
        // um lote comum (categoria Lote, numerado na sequência).
        var q = Q102();
        q.Miolo = ModoMiolo.AreaPropria;
        var d = Div(q);
        var sobra = Assert.Single(d.Parcelas, p => p.Sobra);
        Assert.Equal(CategoriaLote.Lote, sobra.Categoria);
        Assert.Equal(6, sobra.Frente, 6);
        Assert.Equal(180, sobra.Area, 2);
        Assert.Null(sobra.AbaixoDoMinimo);
        var lots = d.Parcelas.Where(p => p.TestadaId == "A").OrderBy(p => p.Forma.Centroid.X).ToList();
        Assert.Same(sobra, lots[^1]);
        Assert.Equal(9, lots.Count);
        Assert.Equal(Enumerable.Range(1, 9), d.Parcelas.Where(p => p.Categoria == CategoriaLote.Lote).Select(p => int.Parse(p.Numero)).OrderBy(n => n));
        Assert.DoesNotContain(d.Parcelas, p => p.Categoria.StartsWith("Sobra"));
    }

    [Theory]
    [InlineData(100, 60, 5, 125, "4,00 m")] // 4 m < 5 m de frente
    [InlineData(102, 20, 5, 125, "6,00 m")] // 6 m × 20 m (quadra rasa) = 120 m² < 125 m²
    [InlineData(102, 60, 7, 125, "6,00 m")] // frente mínima do loteamento de 7 m
    public void R1_SobraAbaixoDosMinimos_SomadaAoUltimoLote(double testada, double fundo, double frenteMin, double areaMin, string sobra)
    {
        var q = LTScenes.Q("S", new List<Vec2> { new(0, 0), new(testada, 0), new(testada, fundo), new(0, fundo) }, new Vec2(testada / 2, 0));
        q.Miolo = ModoMiolo.AreaPropria;
        var lot = new LoteamentoDefinition { FrenteMinimaLote = frenteMin, AreaMinimaLote = areaMin };
        q.LoteamentoId = lot.Id;
        var d = LotDivider.Divide(q, lot);
        Assert.DoesNotContain(d.Parcelas, p => p.Sobra);
        var lots = d.Parcelas.Where(p => p.TestadaId == "A").OrderBy(p => p.Forma.Centroid.X).ToList();
        Assert.Equal(12 + (testada - 12 * Math.Floor(testada / 12)), lots[^1].Frente, 6);
        Assert.Contains(d.Avisos, a => a.Contains($"sobra de {sobra}") && a.Contains("somada ao último lote"));
        Assert.All(lots, p => Assert.Null(p.AbaixoDoMinimo));
    }

    [Fact]
    public void R1_MinimosDoLoteamento_5mE125m2_Editaveis()
    {
        var lot = new LoteamentoDefinition();
        Assert.Equal(5, lot.FrenteMinimaLote);
        Assert.Equal(125, lot.AreaMinimaLote);
        lot.FrenteMinimaLote = 8;
        lot.AreaMinimaLote = 200;
        var back = (LoteamentoDefinition)MarkingDefinition.FromJson(lot.ToJson())!;
        Assert.Equal(8, back.FrenteMinimaLote);
        Assert.Equal(200, back.AreaMinimaLote);
    }

    [Theory]
    [InlineData(4.0, null, "frente 4,00 m < 5,00 m")]
    [InlineData(null, 10.0, "área 120,00 m² < 125,00 m²")]
    public void R1_LoteAbaixoDoMinimo_MarcadoEAvisado(double? frente, double? profundidade, string motivo)
    {
        var q = LTScenes.Scene("uma-testada");
        q.Miolo = ModoMiolo.AreaPropria;
        var aj = q.AjusteOuNovo("A:3");
        aj.Frente = frente;
        aj.Profundidade = profundidade;
        var d = Div(q);
        var p = ByKey(d, "A:3");
        Assert.NotNull(p.AbaixoDoMinimo);
        Assert.Contains(motivo, p.AbaixoDoMinimo);
        Assert.Contains(d.Avisos, a => a.Contains($"lote {p.Numero}: abaixo do mínimo") && a.Contains(motivo));
        Assert.All(d.Parcelas.Where(x => x.Chave != "A:3"), x => Assert.Null(x.AbaixoDoMinimo));
    }

    [Fact]
    public void R1_SobraIncorporadaOuDistribuida()
    {
        var q = LTScenes.Scene("uma-testada");
        q.Sobra = ModoSobra.IncorporarUltimo;
        var d = Div(q);
        Assert.DoesNotContain(d.Parcelas, p => p.Sobra);
        Assert.Equal(16, d.Parcelas.Where(p => p.TestadaId == "A").Max(p => p.Frente), 6);
        q.Sobra = ModoSobra.Distribuir;
        d = Div(q);
        Assert.All(d.Parcelas.Where(p => p.TestadaId == "A"), p => Assert.Equal(12.5, p.Frente, 6));
    }

    [Fact]
    public void R1_LoteXPersonalizado_OsVizinhosSeReajustam()
    {
        var q = LTScenes.Scene("uma-testada");
        q.Miolo = ModoMiolo.AreaPropria;
        var before = Div(q);
        q.AjusteOuNovo("A:3").Frente = 20;
        var d = Div(q);
        var l3 = ByKey(d, "A:3");
        Assert.Equal(20, l3.Frente, 6);
        Assert.Equal(600, l3.Area, 2);
        // O lote 4 começa onde o 3 (agora com 20 m) termina; a sobra se ajusta e a área total não muda.
        Assert.Equal(l3.Forma.Bounds.Max.X, ByKey(d, "A:4").Forma.Bounds.Min.X, 3);
        Assert.Equal(before.Parcelas.Sum(p => p.Area), d.Parcelas.Sum(p => p.Area), 2);
        // 12 + 12 + 20 + 4 × 12 = 92 m: sete lotes e a sobra de 8 m – um lote comum (8 × 30 = 240 m²).
        Assert.Equal(7, d.Parcelas.Count(p => p.TestadaId == "A" && !p.Sobra));
        Assert.Equal(8, d.Parcelas.Single(p => p.Sobra).Frente, 6);
        Assert.Equal(CategoriaLote.Lote, d.Parcelas.Single(p => p.Sobra).Categoria);
    }

    [Fact]
    public void R1_ProfundidadeEDivisaoDoLote()
    {
        var q = LTScenes.Scene("uma-testada");
        q.Miolo = ModoMiolo.AreaPropria;
        q.AjusteOuNovo("A:2").Profundidade = 40;
        q.AjusteOuNovo("A:5").Partes = 2;
        var d = Div(q);
        Assert.Equal(480, ByKey(d, "A:2").Area, 2);
        Assert.Equal(6, ByKey(d, "A:5.1").Frente, 6);
        Assert.Equal(6, ByKey(d, "A:5.2").Frente, 6);
        Assert.Equal(180, ByKey(d, "A:5.1").Area, 2);
        Assert.DoesNotContain(d.Parcelas, p => p.Chave == "A:5");
    }

    [Theory]
    [InlineData(ModoEsquina.Prioridade, "A")]
    [InlineData(ModoEsquina.TestadaAnterior, "A")]
    [InlineData(ModoEsquina.TestadaPosterior, "B")]
    public void R1_LoteDeEsquina_EscolhidoNaHora(ModoEsquina modo, string dono)
    {
        // Esquina sudeste (100; 0) entre a testada A (sul, chega à esquina) e B (leste, sai dela).
        var q = LTScenes.Scene("retangular");
        q.Esquinas.Add(new EsquinaLote { Ponto = new(100, 0), Modo = modo });
        var d = Div(q);
        var corner = d.Parcelas.Single(p => p.Forma.Contains(new Vec2(99, 1)));
        Assert.Equal(dono, corner.TestadaId);
        Assert.True(corner.Esquina);
        Assert.Contains(d.Esquinas, e => e.Ponto.DistanceTo(new Vec2(100, 0)) < 1e-6 && e.Escolha.Modo == modo);
    }

    [Fact]
    public void R1_LoteDeEsquina_ComMedidaPropria()
    {
        var q = LTScenes.Scene("retangular");
        q.Esquinas.Add(new EsquinaLote { Ponto = new(100, 60), Modo = ModoEsquina.MedidaPropria, FrenteAnterior = 15, FrentePosterior = 20 });
        var d = Div(q);
        var c = d.Parcelas.Single(p => p.Chave.StartsWith("esq:"));
        Assert.Equal(300, c.Area, 2);
        var (mn, mx) = c.Forma.Bounds;
        Assert.Equal(new Vec2(80, 45), mn);
        Assert.Equal(new Vec2(100, 60), mx);
    }

    [Fact]
    public void R1_EsquinaArredondada_FrenteMedidaAteOEncontroDosAlinhamentos()
    {
        var d = Div(LTScenes.Scene("arredondada"));
        var r = 6.0;
        var corners = d.Parcelas.Where(p => p.Esquina).ToList();
        Assert.Equal(4, corners.Count);
        // Lote de esquina: frente × fundo menos o arco de raio 6 m (a frente é medida até o encontro dos alinhamentos);
        // os do meio, 12 × 30 exatos.
        Assert.All(corners, p => Assert.InRange(p.Area, p.Frente * p.Profundidade - (1 - Math.PI / 4) * r * r - 0.2, p.Frente * p.Profundidade - (1 - Math.PI / 4) * r * r + 0.2));
        Assert.All(d.Parcelas.Where(p => !p.Esquina), p => Assert.Equal(p.Frente * p.Profundidade, p.Area, 2));
    }

    [Fact]
    public void R1_FundoNoMeioDaQuadraRasa()
    {
        var d = Div(LTScenes.Scene("rasa"));
        // 50 m de fundo, testadas nos dois lados: lotes de 25 m de fundo; a sobra de 4 m vai para o último lote de cada testada.
        Assert.All(d.Parcelas.Where(p => p.Frente == 12), p => Assert.Equal(300, p.Area, 2));
        Assert.All(d.Parcelas.Where(p => p.Frente != 12), p => Assert.Equal(16 * 25, p.Area, 2));
        Assert.DoesNotContain(d.Parcelas, p => p.Chave.StartsWith("m:"));
    }

    [Fact]
    public void R1_Miolo_AosLotesOuAreaPropria()
    {
        // Uma testada só, 60 m de fundo: o padrão leva cada lote até a divisa de fundos (12 × 60).
        var q = LTScenes.Scene("uma-testada");
        var d = Div(q);
        Assert.DoesNotContain(d.Parcelas, p => p.Chave.StartsWith("m:"));
        Assert.All(d.Parcelas.Where(p => p.Frente == 12), p => Assert.Equal(720, p.Area, 2));
        Assert.Equal(6000, d.Parcelas.Sum(p => p.Area), 2);
        // Área própria: o miolo vira uma área na categoria escolhida (padrão Área verde), numerada com o prefixo dela.
        q.Miolo = ModoMiolo.AreaPropria;
        d = Div(q);
        var m = Assert.Single(d.Parcelas, p => p.Chave.StartsWith("m:"));
        Assert.Equal(CategoriaLote.AreaVerde, m.Categoria);
        Assert.Equal(3000, m.Area, 2);
        Assert.Equal("AV-1", m.Numero);
        q.MioloCategoria = CategoriaLote.Institucional;
        Assert.Equal("AI-1", Div(q).Parcelas.Single(p => p.Chave.StartsWith("m:")).Numero);
    }

    [Fact]
    public void R1_QuadraPersonalizada_PontasComLotesDeFrenteParaAsLaterais()
    {
        // Quadra 98 × 60 com quatro testadas: as laterais (oeste e leste) primeiro na prioridade, com 25 m de profundidade –
        // 5 lotes de 12 × 25 em cada ponta; as testadas longas ficam com 4 lotes de 12 × 30 entre elas (25 + 4 × 12 + 25 = 98).
        var q = LTScenes.Q("Q", new List<Vec2> { new(0, 0), new(98, 0), new(98, 60), new(0, 60) }, new Vec2(0, 30), new Vec2(98, 30), new Vec2(49, 0), new Vec2(49, 60));
        q.Testadas[0].Profundidade = 25;
        q.Testadas[1].Profundidade = 25;
        var d = Div(q);
        Assert.Equal(18, d.Parcelas.Count);
        foreach (var id in new[] { "A", "B" })
        {
            var ponta = d.Parcelas.Where(p => p.TestadaId == id).ToList();
            Assert.Equal(5, ponta.Count);
            Assert.All(ponta, p => { var (mn, mx) = p.Forma.Bounds; Assert.Equal(25, mx.X - mn.X, 3); Assert.Equal(12, mx.Y - mn.Y, 3); });
        }
        foreach (var id in new[] { "C", "D" })
        {
            var meio = d.Parcelas.Where(p => p.TestadaId == id).OrderBy(p => p.Forma.Centroid.X).ToList();
            Assert.Equal(4, meio.Count);
            Assert.Equal(new[] { 25.0, 37, 49, 61 }, meio.Select(p => Math.Round(p.Forma.Bounds.Min.X, 3)));
            Assert.All(meio, p => Assert.Equal(360, p.Area, 2));
        }
        Assert.Equal(Enumerable.Range(1, 18), d.Parcelas.Select(p => int.Parse(p.Numero)).OrderBy(n => n));
        // Qualquer lote pode mudar depois: o lote 3 da ponta oeste com 15 m empurra os seguintes da mesma testada.
        q.AjusteOuNovo("A:3").Frente = 15;
        d = Div(q);
        Assert.Equal(15 * 25, ByKey(d, "A:3").Area, 2);
        Assert.Equal(98 * 60, d.Parcelas.Sum(p => p.Area), 2);
    }

    [Fact]
    public void R1_UnirLotes_UmLoteSoComAsAreasSomadas()
    {
        var q = LTScenes.Scene("uma-testada");
        q.Miolo = ModoMiolo.AreaPropria;
        var before = Div(q);
        var n0 = before.Parcelas.Count(p => p.Categoria == CategoriaLote.Lote);
        q.Unioes.Add(new List<string> { "A:2", "A:3" });
        var d = Div(q);
        var u = ByKey(d, "A:2");
        Assert.DoesNotContain(d.Parcelas, p => p.Chave == "A:3");
        Assert.Equal(720, u.Area, 2);
        Assert.Equal(24, u.Frente, 6);
        Assert.Equal(new[] { "A:2", "A:3" }, u.Unidos);
        Assert.DoesNotContain(u.Forma.Outer, v => Math.Abs(v.X - 24) < 1e-6); // sem a divisa do meio no contorno
        Assert.Equal(Enumerable.Range(1, n0 - 1), d.Parcelas.Where(p => p.Categoria == CategoriaLote.Lote).Select(p => int.Parse(p.Numero)).OrderBy(n => n));
        Assert.Equal(before.Parcelas.Sum(p => p.Area), d.Parcelas.Sum(p => p.Area), 2);
        Assert.Equal(q.UniaoDe("A:3"), q.UniaoDe("A:2"));
        // Lote da sobra maior: a sobra de 6 m unida ao último lote (6 + 12 = 18 m).
        var s = Q102();
        s.Miolo = ModoMiolo.AreaPropria;
        var ds = Div(s);
        var last = ds.Parcelas.Where(p => p.TestadaId == "A" && !p.Sobra).OrderBy(p => p.Forma.Centroid.X).Last();
        s.Unioes.Add(new List<string> { last.Chave, ds.Parcelas.Single(p => p.Sobra).Chave });
        Assert.Equal(18, Div(s).Parcelas.Single(p => p.Chave == last.Chave).Frente, 6);
    }

    [Fact]
    public void R1_UnirLotesQueNaoSeTocam_FicamComoEstao()
    {
        var q = LTScenes.Scene("uma-testada");
        var n = Div(q).Parcelas.Count;
        q.Unioes.Add(new List<string> { "A:1", "A:5" });
        var d = Div(q);
        Assert.Equal(n, d.Parcelas.Count);
        Assert.All(d.Parcelas, p => Assert.Null(p.Unidos));
        Assert.Contains(d.Avisos, a => a.Contains($"lotes {ByKey(d, "A:1").Numero} + {ByKey(d, "A:5").Numero}") && a.Contains("não são vizinhos"));
    }

    // ================================================================== R2 – numeração por quadra

    [Theory]
    [MemberData(nameof(Scenes))]
    public void R2_LotesNumeradosDe1aN_SemFalhas(string scene)
    {
        var d = Div(LTScenes.Scene(scene));
        var nums = d.Parcelas.Where(p => p.Categoria == CategoriaLote.Lote).Select(p => int.Parse(p.Numero)).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, nums.Count), nums);
        foreach (var g in d.Parcelas.Where(p => p.Categoria != CategoriaLote.Lote).GroupBy(p => p.Categoria))
        {
            var pre = CategoriaLote.Padrao().First(c => c.Nome == g.Key).Prefixo;
            Assert.Equal(Enumerable.Range(1, g.Count()).Select(i => $"{pre}-{i}"), g.Select(p => p.Numero).OrderBy(s => int.Parse(s.Split('-')[1])));
        }
    }

    [Fact]
    public void R2_CadaQuadraComNumeracaoPropria()
    {
        var w = LTScenes.World();
        var counts = new List<int>();
        foreach (var q in w.Quadras)
        {
            var d = LotDivider.Divide(q, w.Lot);
            var nums = d.Parcelas.Where(p => p.Categoria == CategoriaLote.Lote).Select(p => int.Parse(p.Numero)).OrderBy(n => n).ToList();
            Assert.Equal(1, nums[0]);
            Assert.Equal(Enumerable.Range(1, nums.Count), nums);
            counts.Add(nums.Count);
        }
        // Quadras com quantidades diferentes de lotes: cada uma vai de 1 ao seu total.
        Assert.True(counts.Distinct().Count() > 1);
    }

    [Fact]
    public void R2_SentidoELoteInicial()
    {
        var q = LTScenes.Scene("uma-testada");
        q.LoteInicial = "A:1";
        q.SentidoHorario = false;
        var d = Div(q);
        Assert.Equal("1", ByKey(d, "A:1").Numero);
        Assert.Equal("2", ByKey(d, "A:2").Numero);
        q.SentidoHorario = true;
        d = Div(q);
        Assert.Equal("1", ByKey(d, "A:1").Numero);
        // Horário a partir do lote mais a oeste: segue pelo contorno, pelos outros lados, e volta pelo lote mais a leste.
        Assert.Equal("2", d.Parcelas.Where(p => p.Categoria == CategoriaLote.Lote).OrderByDescending(p => p.Forma.Centroid.X).First().Numero);
    }

    [Fact]
    public void R2_RenumeraAoDividirOuMudarCategoria()
    {
        var q = LTScenes.Scene("uma-testada");
        var n0 = Div(q).Parcelas.Count(p => p.Categoria == CategoriaLote.Lote);
        q.AjusteOuNovo("A:4").Partes = 2;
        var d = Div(q);
        var lots = d.Parcelas.Where(p => p.Categoria == CategoriaLote.Lote).Select(p => int.Parse(p.Numero)).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, n0 + 1), lots);
        q.AjusteOuNovo("A:6").Categoria = CategoriaLote.AreaVerde;
        d = Div(q);
        lots = d.Parcelas.Where(p => p.Categoria == CategoriaLote.Lote).Select(p => int.Parse(p.Numero)).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, n0), lots);
        Assert.Equal("AV-1", ByKey(d, "A:6").Numero);
    }

    [Fact]
    public void R2_ChaveDoLoteNaoMudaComARenumeracao()
    {
        var q = LTScenes.Scene("retangular");
        var a = Div(q).Parcelas.ToDictionary(p => p.Chave, p => p.Area);
        q.SentidoHorario = false;
        q.LoteInicial = "C:2";
        var b = Div(q).Parcelas.ToDictionary(p => p.Chave, p => p.Area);
        Assert.Equal(a.Keys.OrderBy(k => k), b.Keys.OrderBy(k => k));
        foreach (var k in a.Keys) Assert.Equal(a[k], b[k], 6);
    }

    // ================================================================== R3 – categorias

    [Fact]
    public void R3_CategoriasPadrao_LoteInstitucionalAreaVerde()
    {
        // Sem categoria de sobra: a sobra de testada é um lote comum.
        var c = CategoriaLote.Padrao();
        Assert.Equal(new[] { CategoriaLote.Lote, CategoriaLote.Institucional, CategoriaLote.AreaVerde }, c.Select(x => x.Nome));
        Assert.True(c.Single(x => x.Nome == CategoriaLote.Institucional).Publica);
        Assert.True(c.Single(x => x.Nome == CategoriaLote.AreaVerde).Publica);
        Assert.False(c.Single(x => x.Nome == CategoriaLote.Lote).Publica);
        Assert.Equal(3, c.Select(x => x.Cor).Distinct().Count());
        Assert.Equal(3, c.Select(x => x.Hachura).Distinct().Count());
    }

    [Fact]
    public void R3_CorEHachuraDaCategoriaEmCadaLote_ListaExtensivel()
    {
        var cats = CategoriaLote.Padrao();
        cats.Add(new CategoriaLote { Nome = "Comercial", Cor = "#8FA8E0", Hachura = Hachura.Cruzada, Prefixo = "C" });
        var q = LTScenes.Scene("uma-testada");
        q.AjusteOuNovo("A:2").Categoria = "Comercial";
        q.AjusteOuNovo("A:3").Categoria = CategoriaLote.Institucional;
        var geo = LotDrawing.Quadra(q, cats);
        var piece = geo.Pieces.Single(p => p.Layer == LotDrawing.LayerPrefix + "A:2");
        Assert.Equal("#8FA8E0", piece.Rgb);
        Assert.Equal(Hachura.Cruzada, piece.Hatch);
        Assert.Equal(cats[1].Cor, geo.Pieces.Single(p => p.Layer == LotDrawing.LayerPrefix + "A:3").Rgb);
        Assert.Equal(cats[0].Cor, geo.Pieces.Single(p => p.Layer == LotDrawing.LayerPrefix + "A:1").Rgb);
        var d = LotDivider.Divide(q, cats);
        Assert.Equal("C-1", ByKey(d, "A:2").Numero);
        Assert.Equal("AI-1", ByKey(d, "A:3").Numero);
    }

    [Fact]
    public void R3_QuadraInteiraComoAreaVerde()
    {
        var q = LTScenes.Scene("retangular");
        q.Inteira = true;
        q.CategoriaPadrao = CategoriaLote.AreaVerde;
        var d = Div(q);
        var p = Assert.Single(d.Parcelas);
        Assert.Equal(6000, p.Area, 2);
        Assert.Equal("AV-1", p.Numero);
    }

    [Fact]
    public void R3_DefinicoesSalvasEmJson_IdaEVolta()
    {
        var w = LTScenes.World();
        var q = w.Quadras[0];
        q.AjusteOuNovo("T1:2").Categoria = CategoriaLote.Institucional;
        q.Esquinas.Add(new EsquinaLote { Ponto = new(1, 2), Modo = ModoEsquina.MedidaPropria, FrenteAnterior = 14, FrentePosterior = 18 });
        w.Lot.Minimos["Área verde"] = 10;
        w.Lot.FrenteMinimaLote = 6;
        q.Miolo = ModoMiolo.AreaPropria;
        q.MioloCategoria = CategoriaLote.Institucional;
        q.Unioes.Add(new List<string> { "T1:3", "T1:4" });
        foreach (MarkingDefinition d in new MarkingDefinition[] { w.Lot, q, new LoteamentoDetalheDefinition { LoteamentoId = w.Lot.Id },
                     new QuadroLoteamentoDefinition { LoteamentoId = w.Lot.Id, Conteudo = TipoQuadroLoteamento.Lotes, Quadra = "A" } })
        {
            var json = d.ToJson();
            var back = MarkingDefinition.FromJson(json)!;
            Assert.Equal(d.GetType(), back.GetType());
            Assert.Equal(json, back.ToJson());
        }
        Assert.Contains("\"tipo\":\"quadra\"", q.ToJson());
    }

    // ================================================================== R4 – lotes no relevo

    [Theory]
    [InlineData("ondulado", 0.5)]
    [InlineData("vale", 1.0)]
    [InlineData("crista", 1.0)]
    [InlineData("ruido", 0.5)]
    [InlineData("degrau", 0.25)]
    public void R4_LotesAcompanhamOTerreno_Ate5cm(string terrain, double step)
    {
        var ground = TopoScene.Terrains[terrain];
        var w = LTScenes.World();
        var q = w.Quadras.OrderByDescending(x => x.Polygon!.Area).First();
        var d = LotDivider.Divide(q, w.Lot);
        foreach (var p in d.Parcelas.Take(6))
        {
            // Vértices e pontos do contorno: na cota do terreno.
            foreach (var v in LotTerrain.Contorno3D(p.Forma, ground)) Assert.Equal(ground(v.XY), v.Z, 9);
            // Superfície do lote: até 5 cm do terreno em qualquer ponto.
            var mesh = LotTerrain.Superficie(p.Forma, ground, step);
            var area = mesh.Triangles.Sum(t => Math.Abs((mesh.Vertices[t.B].XY - mesh.Vertices[t.A].XY).Cross(mesh.Vertices[t.C].XY - mesh.Vertices[t.A].XY)) / 2);
            Assert.InRange(area, p.Area * 0.995, p.Area * 1.005);
            var dev = LotTerrain.DesvioMaximo(mesh, ground);
            Assert.True(dev <= 0.05, $"{terrain}: lote {p.Chave} a {dev:0.000} m do terreno");
        }
    }

    // ================================================================== R5 – quadros e memorial

    [Fact]
    public void R5_QuadroDeAreas_PercentuaisSomam100()
    {
        var w = LTScenes.World();
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        Assert.Equal(43200, r.AreaGleba, 2);
        Assert.Equal(r.AreaGleba, r.Categorias.Sum(c => c.Area) + r.AreaViaria, 2);
        Assert.Equal(100, r.Categorias.Sum(c => c.Percentual) + r.Pct(r.AreaViaria), 2);
        Assert.Equal(w.Rec.AreaViaria, r.AreaViaria, 2);
        Assert.Equal(r.AreaViaria + r.Categorias.Where(c => c.Publica).Sum(c => c.Area), r.AreaPublica, 6);
        Assert.True(r.TotalLotes > 50);
    }

    [Fact]
    public void R5_QuadroPorQuadra_TotalIgualASomaDasQuadras()
    {
        var w = LTScenes.World();
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        var (headers, rows) = LotMemorial.LinhasQuadras(r);
        Assert.Equal("Quadra", headers[0]);
        Assert.Equal(w.Quadras.Count + 1, rows.Count);
        Assert.Equal("Total", rows[^1][0]);
        Assert.Equal(LotReport.M(w.Quadras.Sum(q => LotDivider.Divide(q, w.Lot).Quadra.Area)), rows[^1][^1]);
        Assert.Equal(r.TotalLotes.ToString(), rows[^1][1]);
    }

    [Fact]
    public void R5_MinimosEditaveis_SemValorLegalEmbutido()
    {
        var w = LTScenes.World();
        Assert.Empty(w.Lot.Minimos);
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        Assert.DoesNotContain(r.Avisos, a => a.Contains("abaixo do mínimo"));
        w.Lot.Minimos[CategoriaLote.AreaVerde] = 10;
        w.Lot.Minimos[ResumoLoteamento.SistemaViario] = 30; // a cena tem 22,6 %
        r = LotReport.Resumo(w.Lot, w.Quadras);
        Assert.Contains(r.Avisos, a => a.StartsWith("Área verde") && a.Contains("abaixo do mínimo"));
        Assert.Contains(r.Avisos, a => a.StartsWith(ResumoLoteamento.SistemaViario) && a.Contains("abaixo do mínimo"));
    }

    [Fact]
    public void R5_QuadrosIndependentesDeVista()
    {
        var w = LTScenes.World();
        var defs = new List<MarkingDefinition> { w.Lot };
        defs.AddRange(w.Quadras);
        foreach (var tipo in Enum.GetValues<TipoQuadroLoteamento>())
        {
            var qd = new QuadroLoteamentoDefinition { LoteamentoId = w.Lot.Id, Conteudo = tipo, Output = { ViewId = "vista-qualquer" } };
            Assert.True(ProjectTableViews.IsProjectTable(qd));
            var own = ProjectTableViews.ForOwnView(qd);
            Assert.Null(own.Output.ViewId);
            Assert.Equal(OutputMode.Detalhe2D, own.Output.Mode);
            Assert.StartsWith("SV - Loteamento", ProjectTableViews.ViewName(qd));
            var ctx = new BuildContext { Catalog = Cat, AllDefinitions = () => defs, ViewScale = 100 };
            var geo = MarkingBuilder.Build(own, null, ctx);
            Assert.NotEmpty(geo.Annotations.OfType<AnnotationText>());
            Assert.Contains(geo.Annotations.OfType<AnnotationText>(), t => t.Text == own.TituloPadrao);
            if (tipo == TipoQuadroLoteamento.Areas)
            {
                // Amostra de cor de cada categoria no quadro e a linha da gleba com 100 %.
                Assert.Contains(geo.Pieces, p => p.Rgb == w.Lot.Categoria(CategoriaLote.Lote).Cor);
                Assert.Contains(geo.Annotations.OfType<AnnotationText>(), t => t.Text == LotReport.P(100));
            }
        }
    }

    [Fact]
    public void R5_MemorialDescritivoDoLoteamento()
    {
        var w = LTScenes.World();
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        var doc = LotMemorial.Build(r, new DateTime(2026, 10, 13));
        Assert.Equal(LotMemorial.AllSections, doc.Sections);
        var text = string.Join("\n", doc.Blocks.OfType<MemParagraph>().Select(p => p.Text));
        Assert.Contains("Lote 1 da Quadra A", text);
        Assert.Contains("para Rua das Flores", text);
        Assert.Contains("confrontando com Lote 2 da Quadra A", text);
        Assert.Contains("Fundos:", text);
        Assert.DoesNotContain("Proprietário", text + string.Join("", doc.Blocks.OfType<MemField>().Select(f => f.Label)));
        Assert.DoesNotContain("atrícula", text + string.Join("", doc.Blocks.OfType<MemField>().Select(f => f.Label)));
        Assert.Contains("[a confirmar", text);
        // Quadro de lotes de cada quadra no memorial (lote a lote).
        var tables = doc.Blocks.OfType<MemTable>().Where(t => t.Headers.Contains("Frente para")).ToList();
        Assert.Equal(w.Quadras.Count, tables.Count);
        Assert.Equal(r.Quadras.Sum(q => q.Divisao.Parcelas.Count), tables.Sum(t => t.Rows.Count));
        var bytes = DocxWriter.Write(doc);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var sr = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = sr.ReadToEnd();
        Assert.Contains("QUADRO DE ÁREAS", xml.ToUpperInvariant());
        Assert.Contains("Rua das Flores", xml);
    }

    [Fact]
    public void R5_QuadroDeAreas_SemCategoriaDeSobras()
    {
        var w = LTScenes.World();
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        var rows = LotMemorial.LinhasAreas(r);
        Assert.DoesNotContain(rows, x => x.Item.Contains("Sobra", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(r.Categorias, c => c.Nome.Contains("Sobra", StringComparison.OrdinalIgnoreCase));
        // Todas as parcelas das quadras de lotes são lotes: as sobras de testada entram como lotes comuns.
        Assert.Equal(r.Quadras.Sum(q => q.Divisao.Parcelas.Count), r.Categorias.Sum(c => c.Quantidade));
        Assert.Equal(r.Quadras.Sum(q => q.Divisao.Parcelas.Count(p => p.Categoria == CategoriaLote.Lote)), r.TotalLotes);
    }

    [Fact]
    public void R5_QuadroDeLotes_LoteALote()
    {
        var w = LTScenes.World();
        w.Quadras[0].AjusteOuNovo("T1:2").Categoria = CategoriaLote.AreaVerde;
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        var rows = LotReport.LinhasLotes(r);
        Assert.Equal(r.Quadras.Sum(q => q.Divisao.Parcelas.Count), rows.Count);
        Assert.All(rows, x => Assert.Equal(LotReport.CabecalhosLotes.Length, x.Length));
        Assert.Equal(new[] { "QUADRA", "LOTE", "CATEGORIA", "FRENTE PARA", "FRENTE (m)", "LADO DIR. (m)", "LADO ESQ. (m)", "FUNDOS (m)", "ÁREA (m²)", "OBSERVAÇÃO" },
            LotReport.CabecalhosLotes);
        // Lote comum de 12 × 30: frente para a via, lados de 30 m, fundos de 12 m.
        bool Std(Parcela p) => p.Categoria == CategoriaLote.Lote && p.Frente == 12 && Math.Abs(p.Area - 360) < 0.01 && !p.Esquina;
        var (qa, da) = r.Quadras.First(x => x.Divisao.Parcelas.Any(Std));
        var std = da.Parcelas.First(Std);
        var row = rows.Single(x => x[0] == qa.Nome && x[1] == std.Numero);
        Assert.Equal(CategoriaLote.Lote, row[2]);
        Assert.NotEqual("–", row[3]);
        Assert.Equal(new[] { "12,00", "30,00", "30,00", "12,00", "360,00", "" }, row[4..]);
        // Lote de esquina: duas vias na frente e a observação.
        var corner = rows.First(x => x[9].Contains("esquina"));
        Assert.Contains(" / ", corner[3]);
        Assert.Contains(" + ", corner[4]);
        // Na ordem da numeração em cada quadra: 1..N e depois as outras categorias.
        foreach (var g in rows.GroupBy(x => x[0]))
        {
            var lotes = g.Where(x => x[2] == CategoriaLote.Lote).Select(x => int.Parse(x[1])).ToList();
            Assert.Equal(Enumerable.Range(1, lotes.Count), lotes);
            Assert.All(g.SkipWhile(x => x[2] == CategoriaLote.Lote), x => Assert.NotEqual(CategoriaLote.Lote, x[2]));
        }
        Assert.Contains(rows, x => x[1] == "AV-1" && x[2] == CategoriaLote.AreaVerde);
        // Filtro por quadra.
        Assert.All(LotReport.LinhasLotes(r, "B"), x => Assert.Equal("B", x[0]));
        Assert.Equal(r.Quadras.Single(x => x.Quadra.Nome == "B").Divisao.Parcelas.Count, LotReport.LinhasLotes(r, "B").Count);
    }

    [Fact]
    public void R5_QuadroDeLotesDesenhado_ComFiltroDeQuadra_EUniaoEAbaixoDoMinimoNaObservacao()
    {
        var w = LTScenes.World();
        var q = w.Quadras.Single(x => x.Nome == "A");
        q.Unioes.Add(new List<string> { "T1:2", "T1:3" });
        q.AjusteOuNovo("T1:5").Frente = 4;
        var defs = new List<MarkingDefinition> { w.Lot };
        defs.AddRange(w.Quadras);
        var ctx = new BuildContext { Catalog = Cat, AllDefinitions = () => defs, ViewScale = 100 };
        var all = MarkingBuilder.Build(new QuadroLoteamentoDefinition { LoteamentoId = w.Lot.Id, Conteudo = TipoQuadroLoteamento.Lotes, LinhasPorColuna = 400 }, null, ctx);
        var texts = all.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        foreach (var h in LotReport.CabecalhosLotes) Assert.Contains(h, texts);
        Assert.Contains("QUADRO DE LOTES", texts);
        Assert.Contains(texts, t => t.Contains("união de 2 lotes"));
        Assert.Contains(texts, t => t.Contains("abaixo do mínimo: frente 4,00 m < 5,00 m"));
        Assert.Contains("Rua das Flores", texts);
        var one = new QuadroLoteamentoDefinition { LoteamentoId = w.Lot.Id, Conteudo = TipoQuadroLoteamento.Lotes, Quadra = "A", LinhasPorColuna = 400 };
        var geo = MarkingBuilder.Build(one, null, ctx);
        var t1 = geo.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("QUADRO DE LOTES – QUADRA A", t1);
        Assert.DoesNotContain("QUADRA", t1);
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        foreach (var p in r.Quadras.Single(x => x.Quadra.Nome == "A").Divisao.Parcelas) Assert.Contains(p.Numero, t1);
        Assert.True(t1.Count < texts.Count);
    }

    [Fact]
    public void R5_Confrontacoes_FrenteLadosEFundos()
    {
        var q = LTScenes.Scene("uma-testada");
        q.Testadas[0].Via = "Rua Sul";
        var d = Div(q);
        var p = ByKey(d, "A:3");
        var lados = LotReport.Lados(p, d, q, null);
        Assert.Equal(4, lados.Count);
        var frente = lados.Single(l => l.Tipo == TipoLado.Frente);
        Assert.Equal(12, frente.Comprimento, 6);
        Assert.Equal("Rua Sul", frente.Confrontante);
        Assert.True(frente.Via);
        // Fundos até a divisa (miolo aos lotes): 12 × 60. Sem gleba e sem testada, o fundo é divisa – não via.
        Assert.Equal(60, lados.Single(l => l.Tipo == TipoLado.LadoDireito).Comprimento, 6);
        Assert.Equal(60, lados.Single(l => l.Tipo == TipoLado.LadoEsquerdo).Comprimento, 6);
        var fundos = lados.Single(l => l.Tipo == TipoLado.Fundos);
        Assert.Equal("a divisa da Quadra U", fundos.Confrontante);
        Assert.False(fundos.Via);
        // Com o miolo como área própria, os fundos confrontam com ela.
        q.Miolo = ModoMiolo.AreaPropria;
        d = Div(q);
        Assert.Equal($"Área verde AV-1 da Quadra U", LotReport.Lados(ByKey(d, "A:3"), d, q, null).Single(l => l.Tipo == TipoLado.Fundos).Confrontante);
        // De quem olha o lote da rua (sul): o lado direito é o leste.
        var right = lados.Single(l => l.Tipo == TipoLado.LadoDireito);
        Assert.True((right.A + right.B).X / 2 > p.Forma.Centroid.X);
    }

    // ================================================================== R6 – detalhamento

    [Fact]
    public void R6_Detalhe_NumeroAreaCotasCategoriaEQuadra()
    {
        var w = LTScenes.World();
        var defs = new List<MarkingDefinition> { w.Lot };
        defs.AddRange(w.Quadras);
        var det = new LoteamentoDetalheDefinition { LoteamentoId = w.Lot.Id };
        var ctx = new BuildContext { Catalog = Cat, AllDefinitions = () => defs, ViewScale = 500 };
        var geo = MarkingBuilder.Build(det, null, ctx);
        var texts = geo.Annotations.OfType<AnnotationText>().ToList();
        var q = w.Quadras[0];
        var d = LotDivider.Divide(q, w.Lot);
        var lot = d.Parcelas.First(p => p.Categoria == CategoriaLote.Lote && p.Frente == 12 && Math.Abs(p.Area - 360) < 0.01);
        Assert.Contains(texts, t => t.Text == lot.Numero && lot.Forma.Contains(t.Position));
        Assert.Contains(texts, t => t.Text == "360,00 m²" && lot.Forma.Contains(t.Position));
        Assert.Contains(texts, t => t.Text == "12,00" && lot.Forma.Contains(t.Position));
        Assert.Contains(texts, t => t.Text == "30,00" && lot.Forma.Contains(t.Position));
        Assert.Contains(texts, t => t.Text == q.Nome);
        Assert.DoesNotContain(texts, t => t.Text.StartsWith("QUADRA "));
        Assert.Contains(texts, t => t.Text == "RUA DAS FLORES");
        Assert.DoesNotContain(texts, t => t.Text.Contains("SOBRA"));
        // Cada item desliga.
        var off = new LoteamentoDetalheDefinition { LoteamentoId = w.Lot.Id, Numero = false, Area = false, Cotas = false, Categoria = false, Quadra = false, Vias = false };
        Assert.Empty(MarkingBuilder.Build(off, null, ctx).Annotations.OfType<AnnotationText>());
        var cores = MarkingBuilder.Build(new LoteamentoDetalheDefinition { LoteamentoId = w.Lot.Id, Cores = true, Divisas = true }, null, ctx);
        Assert.Equal(w.Quadras.Sum(x => LotDivider.Divide(x, w.Lot).Parcelas.Count), cores.Pieces.Count(p => p.Layer!.StartsWith(LotDrawing.LayerPrefix)));
        // Círculo da letra: miolo branco e anel preto em cada quadra.
        Assert.Equal(2 * w.Quadras.Count, cores.Pieces.Count(p => p.Layer!.StartsWith(LotDrawing.QuadraLayerPrefix)));
        // Categoria escrita nas áreas públicas (a cor/hachura também a mostra).
        q.AjusteOuNovo("T1:2").Categoria = CategoriaLote.Institucional;
        Assert.Contains(MarkingBuilder.Build(det, null, ctx).Annotations.OfType<AnnotationText>(), t => t.Text == "INSTITUCIONAL");
    }

    [Theory]
    [InlineData("grade", 500)]
    [InlineData("grade", 1000)]
    [InlineData("cruz", 500)]
    [InlineData("avenida", 1000)]
    public void R6_LetraDaQuadraNumCirculo_NoMeioSemSobreporOsRotulos(string kind, double scale)
    {
        var w = LTScenes.World(kind);
        var defs = new List<MarkingDefinition> { w.Lot };
        defs.AddRange(w.Quadras);
        var ctx = new BuildContext { Catalog = Cat, AllDefinitions = () => defs, ViewScale = scale };
        var geo = MarkingBuilder.Build(new LoteamentoDetalheDefinition { LoteamentoId = w.Lot.Id }, null, ctx);
        var texts = geo.Annotations.OfType<AnnotationText>().ToList();
        var mm = ctx.Mm(1);
        foreach (var q in w.Quadras)
        {
            var quad = q.Polygon!;
            var letter = Assert.Single(texts, t => t.Text == q.Nome);
            var ring = geo.Pieces.Single(p => p.Layer == LotDrawing.QuadraLayerPrefix + q.Nome && p.Shape.Holes.Count == 1);
            var c = new Polygon2(ring.Shape.Outer).Centroid;
            var radius = ring.Shape.Outer.Max(v => v.DistanceTo(c));
            // Letra centrada no círculo, círculo dentro da quadra.
            Assert.True(Math.Abs(letter.Position.X - c.X) < 1e-6);
            Assert.True(quad.Contains(c) && quad.DistanceTo(c) >= radius, $"{kind}: círculo da quadra {q.Nome} sai dela");
            // Fora dos números, áreas e cotas dos lotes.
            foreach (var t in texts.Where(t => t != letter && quad.Contains(t.Position)))
            {
                var box = LotDrawing.TextBox(t, mm);
                Assert.True(!box.Contains(c) && box.DistanceTo(c) >= radius, $"{kind} 1:{scale}: círculo da quadra {q.Nome} sobre \"{t.Text}\"");
            }
            // No meio da quadra: perto do ponto mais central dela.
            var (mn, mx) = quad.Bounds;
            Assert.True(c.DistanceTo(LotDrawing.InteriorPoint(quad)) < 0.25 * Math.Min(mx.X - mn.X, mx.Y - mn.Y), $"{kind}: círculo da quadra {q.Nome} longe do meio");
        }
    }

    [Fact]
    public void R6_LetraDaQuadra_DesviaDosRotulosNumaFileiraSo()
    {
        // Uma testada, lotes 12 × 60: o meio da quadra cai no rótulo de um lote – o círculo vai para a divisa ao lado.
        var q = LTScenes.Scene("uma-testada");
        var lot = new LoteamentoDefinition { Nome = "X" };
        q.LoteamentoId = lot.Id;
        var defs = new List<MarkingDefinition> { lot, q };
        var ctx = new BuildContext { Catalog = Cat, AllDefinitions = () => defs, ViewScale = 500 };
        var geo = MarkingBuilder.Build(new LoteamentoDetalheDefinition { LoteamentoId = lot.Id }, null, ctx);
        var texts = geo.Annotations.OfType<AnnotationText>().ToList();
        var letter = texts.Single(t => t.Text == "U");
        var ring = geo.Pieces.Single(p => p.Layer == LotDrawing.QuadraLayerPrefix + "U" && p.Shape.Holes.Count == 1);
        var c = new Polygon2(ring.Shape.Outer).Centroid;
        var radius = ring.Shape.Outer.Max(v => v.DistanceTo(c));
        foreach (var t in texts.Where(t => t != letter))
            Assert.True(LotDrawing.TextBox(t, ctx.Mm(1)).DistanceTo(c) >= radius, $"círculo sobre \"{t.Text}\"");
        Assert.InRange(c.Y, 20, 40);
    }

    [Fact]
    public void R6_Detalhe_SemLoteamento_SomeComOAlvo()
    {
        var ctx = new BuildContext { Catalog = Cat, AllDefinitions = () => new List<MarkingDefinition>() };
        var geo = MarkingBuilder.Build(new LoteamentoDetalheDefinition { LoteamentoId = "x" }, null, ctx);
        Assert.Contains(geo.Warnings, w => w.StartsWith(Generators.DetailGenerator.MissingTarget));
    }

    // ================================================================== R7 – reconhecimento

    [Theory]
    [InlineData("grade", 6)]
    [InlineData("cruz", 4)]
    [InlineData("T", 3)]
    [InlineData("avenida", 4)]
    public void R7_GlebaMenosVias_SaoAsQuadras(string kind, int quadras)
    {
        var w = LTScenes.World(kind);
        Assert.Equal(quadras, w.Rec.Quadras.Count);
        var occ = Reconhecimento.Ocupacao(w.Occupied);
        foreach (var q in w.Rec.Quadras)
        {
            var overlap = PolygonOps.TotalArea(PolygonOps.Intersect(new[] { q.Contorno }, occ));
            Assert.True(overlap < 0.05, $"{kind}: quadra {q.Nome} sobre a via ({overlap:0.000} m²)");
            Assert.NotEmpty(q.Testadas);
        }
        Assert.Equal(w.Rec.Gleba.Area, w.Rec.Quadras.Sum(q => q.Contorno.Area) + w.Rec.AreaViaria, 2);
        Assert.Equal(Reconhecimento.Nomes(quadras), w.Rec.Quadras.Select(q => q.Nome));
    }

    [Fact]
    public void R7_TestadasSaoOsLadosJuntoAsVias()
    {
        var w = LTScenes.World();
        var occ = Reconhecimento.Ocupacao(w.Occupied);
        foreach (var q in w.Rec.Quadras)
        {
            var chains = LotDivider.Cadeias(q.Contorno.Outer);
            foreach (var c in chains)
            {
                var line = new Polyline2(c.Pontos);
                var probe = line.PointAt(line.Length / 2) + line.TangentAt(line.Length / 2).PerpRight * 0.5;
                var nearRoad = occ.Any(o => o.Contains(probe));
                var picked = q.Testadas.Any(t => LotDivider.CadeiaEm(chains, t) == c);
                Assert.Equal(nearRoad, picked);
            }
        }
        // As quadras do meio têm três testadas; as das pontas (com a divisa da gleba), duas.
        Assert.Equal(new[] { 2, 2, 2, 2, 3, 3 }, w.Rec.Quadras.Select(q => q.Testadas.Count).OrderBy(n => n));
    }

    [Fact]
    public void R7_NomesDeQuadra()
    {
        Assert.Equal(new[] { "A", "B", "C" }, Reconhecimento.Nomes(3));
        Assert.Equal("Z", Reconhecimento.Nomes(26)[25]);
        Assert.Equal("AA", Reconhecimento.Nomes(28)[26]);
        Assert.Equal("AB", Reconhecimento.Nomes(28)[27]);
    }

    [Fact]
    public void R7_NomeDaViaPelaVia()
    {
        var w = LTScenes.World();
        Assert.Equal("Rua das Flores", w.Lot.ViaEm(new Vec2(80, 6)));
        Assert.Equal("Avenida B", w.Lot.ViaEm(new Vec2(58, 60)));
        Assert.Null(w.Lot.ViaEm(new Vec2(500, 500)));
    }

    // ================================================================== R8 – masterplan

    [Fact]
    public void R8_Masterplan_LayoutLimpo()
    {
        foreach (var c in new[] { CategoriaQuantitativo.SinalizacaoHorizontal, CategoriaQuantitativo.SinalizacaoVertical, CategoriaQuantitativo.DispositivosSegregacao,
                     CategoriaQuantitativo.Acessibilidade, CategoriaQuantitativo.Drenagem })
            Assert.True(Masterplan.Oculta(c));
        // Ficam vias, calçadas, canteiros, árvores e mobiliário.
        foreach (var c in new[] { CategoriaQuantitativo.PavimentacaoGeometria, CategoriaQuantitativo.CalcadasUrbanizacao, CategoriaQuantitativo.MobiliarioUrbano })
            Assert.False(Masterplan.Oculta(c));
        Assert.Contains(Masterplan.Detalhamento, Masterplan.ValoresOcultos);
        // O detalhamento dos lotes (números e letra da quadra no círculo) fica visível.
        Assert.DoesNotContain(Masterplan.Loteamento, Masterplan.ValoresOcultos);
        var det = Masterplan.Detalhe("x");
        Assert.True(det.Numero && det.Quadra && det.Vias);
        Assert.False(det.Area || det.Cotas || det.Categoria);
    }

    [Fact]
    public void R8_LotesForaDoQuantitativoDeSinalizacao()
    {
        var w = LTScenes.World();
        var items = w.Quadras.Select(q => ((MarkingDefinition)q, LotDrawing.Quadra(q, w.Lot.Categorias))).ToList();
        Assert.Empty(QuantityCalculator.Compute(items, Cat));
    }
}
