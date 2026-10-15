using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada LW: quadro de lotes com uma frente só e o perímetro (1), lotes por linhas do Revit (2), categoria por clique (3) e
/// quadro de uma quadra (4).
/// </summary>
public class LWLotesTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    // ================================================================== 1 – quadro de lotes

    [Fact]
    public void Q_QuadroDeLotes_LoteCategoriaViaAreaPerimetroObservacao()
    {
        var w = LTScenes.World();
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        Assert.Equal(new[] { "QUADRA", "LOTE", "CATEGORIA", "VIA", "ÁREA (m²)", "PERÍMETRO (m)", "OBSERVAÇÃO" }, LotReport.CabecalhosLotes);
        var rows = LotReport.LinhasLotes(r);
        Assert.All(rows, x => Assert.Equal(LotReport.CabecalhosLotes.Length, x.Length));
        // Nenhuma medida como soma.
        Assert.All(rows, x => Assert.DoesNotContain(x, c => c.Contains(" + ")));
        // Lote comum de 12 × 30: área e perímetro.
        bool Std(Parcela p) => p.Categoria == CategoriaLote.Lote && p.Frente == 12 && Math.Abs(p.Area - 360) < 0.01 && !p.Esquina;
        var (qa, da) = r.Quadras.First(x => x.Divisao.Parcelas.Any(Std));
        var std = da.Parcelas.First(Std);
        var row = rows.Single(x => x[0] == qa.Nome && x[1] == std.Numero);
        Assert.Equal(new[] { "360,00", "84,00" }, row[4..6]);
        // Perímetro de cada lote = o do contorno dele.
        foreach (var (q, d) in r.Quadras)
            foreach (var p in d.Parcelas)
                Assert.Equal(LotReport.M(LotDivider.Perimeter(p.Forma)), rows.Single(x => x[0] == q.Nome && x[1] == p.Numero)[5]);
        // Lote de esquina: as duas vias na coluna VIA e "esquina" na observação.
        var corner = rows.First(x => x[6].Contains("esquina"));
        Assert.Contains(" / ", corner[3]);
    }

    [Fact]
    public void Q_LoteDeEsquina_UmaFrenteSo_OutraViaNoLado()
    {
        var w = LTScenes.World();
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        var n = 0;
        foreach (var (q, d) in r.Quadras)
            foreach (var p in d.Parcelas)
            {
                var lados = LotReport.Lados(p, d, q, w.Lot);
                // Todo lote tem no máximo uma frente.
                Assert.True(lados.Count(l => l.Tipo == TipoLado.Frente) <= 1, $"lote {q.Nome}-{p.Numero} com mais de uma frente");
                if (lados.Count(l => l.Via && l.Tipo != TipoLado.Chanfro) < 2) continue;
                // Lote de esquina (duas vias): a frente é a da testada dele; o lado para a outra via é lateral e confronta com ela.
                n++;
                var frente = Assert.Single(lados, l => l.Tipo == TipoLado.Frente);
                Assert.Contains(lados, l => l.Via && l.Tipo is TipoLado.LadoDireito or TipoLado.LadoEsquerdo);
                var front = new Polyline2(p.LinhaFrente);
                Assert.True(Math.Abs(front.Project((frente.A + frente.B) / 2).Signed) < 0.1, $"frente fora da testada do lote {q.Nome}-{p.Numero}");
                Assert.Contains("esquina", LotReport.LinhaLote(p, d, q, w.Lot)[6]);
            }
        Assert.True(n >= 4, $"{n} lote(s) de esquina com duas vias");
    }

    [Fact]
    public void Q_QuadroDeUmaQuadra_DesenhadoEMemorial()
    {
        var w = LTScenes.World();
        var defs = new List<MarkingDefinition> { w.Lot };
        defs.AddRange(w.Quadras);
        var ctx = new BuildContext { Catalog = Cat, AllDefinitions = () => defs, ViewScale = 100 };
        var one = new QuadroLoteamentoDefinition { LoteamentoId = w.Lot.Id, Conteudo = TipoQuadroLoteamento.Lotes, Quadra = "A", LinhasPorColuna = 400 };
        var texts = MarkingBuilder.Build(one, null, ctx).Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        foreach (var h in new[] { "LOTE", "CATEGORIA", "VIA", "ÁREA (m²)", "PERÍMETRO (m)", "OBSERVAÇÃO" }) Assert.Contains(h, texts);
        foreach (var h in new[] { "QUADRA", "FRENTE (m)", "LADO DIR. (m)", "LADO ESQ. (m)", "FUNDOS (m)", "FRENTE PARA" }) Assert.DoesNotContain(h, texts);
        var r = LotReport.Resumo(w.Lot, w.Quadras);
        var t = LotMemorial.QuadroLotes(r, "A");
        Assert.Equal(new[] { "Lote", "Categoria", "Via", "Área (m²)", "Perímetro (m)", "Observação" }, t.Headers);
        Assert.Equal(t.Headers.Count, t.Widths.Count);
        Assert.All(t.Rows, x => Assert.Equal(t.Headers.Count, x.Count));
    }

    // ================================================================== 2 – lotes por linhas

    /// <summary>Quadra 60 × 30 no modo por linhas, com as linhas dadas (pares de pontos), testada ao sul.</summary>
    private static QuadraDefinition PorLinhas(params (string Id, Vec2[] Pts)[] linhas)
    {
        var q = new QuadraDefinition { Nome = "L", Contorno = Polygon2.Rectangle(Vec2.Zero, new Vec2(60, 30)).Outer.ToList(), Divisao = ModoDivisao.Linhas };
        q.Testadas.Add(new TestadaLote { Id = "T1", Ponto = new Vec2(30, 0) });
        foreach (var (id, pts) in linhas) q.LinhasLotes.Add(new LinhaDeLote { Elemento = id, Pontos = pts.ToList() });
        return q;
    }

    private static (string, Vec2[]) V(string id, double x, double y0 = 0, double y1 = 30) => (id, new[] { new Vec2(x, y0), new Vec2(x, y1) });

    [Fact]
    public void L_LinhasRetas_CadaRegiaoUmLote_AreasSomamAQuadra()
    {
        var q = PorLinhas(V("a", 15), V("b", 30), V("c", 45));
        var d = LotDivider.Divide(q);
        Assert.Equal(4, d.Parcelas.Count);
        Assert.All(d.Parcelas, p => Assert.Equal(450, p.Area, 3));
        Assert.Equal(1800, d.Parcelas.Sum(p => p.Area), 3);
        // Numeração 1..4 pela regra da quadra; frente de 15 m sobre a testada.
        Assert.Equal(new[] { "1", "2", "3", "4" }, d.Parcelas.Select(p => p.Numero).OrderBy(x => x));
        Assert.All(d.Parcelas, p => Assert.Equal(15, p.Frente, 3));
        Assert.Empty(d.LinhasSoltas);
        Assert.All(d.Parcelas, p => Assert.StartsWith("l:", p.Chave));
    }

    [Fact]
    public void L_PontasCurtasProlongadas_PassadasIgnoradas_SoltasAvisadas()
    {
        // "a" para 0,3 m antes do contorno (prolongada); "b" passa 2 m para fora (sobra ignorada); "c" para 0,8 m antes (não fecha).
        var q = PorLinhas(V("a", 15, 0.3, 29.7), V("b", 30, -2, 32), V("c", 45, 0.8, 30));
        var d = LotDivider.Divide(q);
        Assert.Equal(3, d.Parcelas.Count);
        Assert.Equal(1800, d.Parcelas.Sum(p => p.Area), 3);
        Assert.Equal(new[] { 450.0, 450.0, 900.0 }, d.Parcelas.Select(p => Math.Round(p.Area, 3)).OrderBy(x => x));
        Assert.Equal(new[] { "c" }, d.LinhasSoltas);
        Assert.Contains(d.Avisos, a => a.Contains("não fecham"));
    }

    [Fact]
    public void L_LinhasCruzadasECurva()
    {
        // Cruz: 4 lotes de 30 × 15.
        var cruz = LotDivider.Divide(PorLinhas(V("v", 30), ("h", new[] { new Vec2(0, 15), new Vec2(60, 15) })));
        Assert.Equal(4, cruz.Parcelas.Count);
        Assert.All(cruz.Parcelas, p => Assert.Equal(450, p.Area, 3));
        // Linha curva (arco discretizado) de baixo para cima: duas regiões que somam a quadra.
        var arc = Enumerable.Range(0, 31).Select(i => new Vec2(30 + 8 * Math.Sin(i * Math.PI / 30), i)).ToArray();
        var curva = LotDivider.Divide(PorLinhas(("arc", arc)));
        Assert.Equal(2, curva.Parcelas.Count);
        Assert.Equal(1800, curva.Parcelas.Sum(p => p.Area), 3);
        // Linha que só encosta no contorno (não corta): não fecha lote.
        var toca = LotDivider.Divide(PorLinhas(("t", new[] { new Vec2(10, 0), new Vec2(10, -5) })));
        Assert.Single(toca.Parcelas);
        Assert.Equal(new[] { "t" }, toca.LinhasSoltas);
    }

    [Fact]
    public void L_MoverALinha_MantemAChaveEACategoria()
    {
        var q = PorLinhas(V("a", 15), V("b", 30));
        var d0 = LotDivider.Divide(q);
        var meio = d0.Parcelas.Single(p => p.Forma.Contains(new Vec2(22, 10)));
        q.AjusteOuNovo(meio.Chave).Categoria = CategoriaLote.AreaVerde;
        // A linha "a" vai para x = 12: o lote do meio fica maior e continua área verde.
        q.LinhasLotes[0].Pontos = new List<Vec2> { new(12, 0), new(12, 30) };
        var d1 = LotDivider.Divide(q);
        var av = Assert.Single(d1.Parcelas, p => p.Categoria == CategoriaLote.AreaVerde);
        Assert.Equal(meio.Chave, av.Chave);
        Assert.Equal(18 * 30, av.Area, 3);
        Assert.StartsWith("AV-", av.Numero);
    }

    [Fact]
    public void L_Lasca_VaiParaOVizinho_SemPerderArea()
    {
        // Duas linhas a 2 cm uma da outra: lasca de 0,6 m² somada ao vizinho, com aviso.
        var d = LotDivider.Divide(PorLinhas(V("a", 30), V("b", 30.02)));
        Assert.Equal(2, d.Parcelas.Count);
        Assert.Equal(1800, d.Parcelas.Sum(p => p.Area), 3);
        Assert.Contains(d.Avisos, a => a.Contains("Lasca"));
    }

    [Fact]
    public void L_QuadroEMemorialDosLotesPorLinhas()
    {
        var lot = new LoteamentoDefinition { Nome = "L", Gleba = Polygon2.Rectangle(new Vec2(-20, -20), new Vec2(80, 30)).Outer.ToList() };
        var q = PorLinhas(V("a", 15), V("b", 30), V("c", 45));
        q.LoteamentoId = lot.Id;
        q.Testadas[0].Via = "Rua Sul";
        var r = LotReport.Resumo(lot, new[] { q });
        var rows = LotReport.LinhasLotes(r, "L");
        Assert.Equal(4, rows.Count);
        // Lote do meio: 15 × 30 – área 450 e perímetro 90, frente para a Rua Sul.
        var (qq, d) = r.Quadras[0];
        var p = d.Parcelas.Single(x => x.Forma.Contains(new Vec2(22, 10)));
        var row = rows.Single(x => x[1] == p.Numero);
        Assert.Equal(new[] { "Rua Sul", "450,00", "90,00" }, row[3..6]);
        var lados = LotReport.Lados(p, d, qq, lot);
        Assert.Equal("Rua Sul", Assert.Single(lados, l => l.Tipo == TipoLado.Frente).Confrontante);
    }

    [Fact]
    public void L_CasosDeBorda_EsquinaArredondada_Vertice_LinhaSobreOContorno()
    {
        // Quadra de esquinas arredondadas (arcos discretizados): linhas de um lado a outro – as áreas somam a quadra.
        var q = LTScenes.Scene("arredondada");
        var (mn, mx) = q.Polygon!.Bounds;
        q.Divisao = ModoDivisao.Linhas;
        for (int i = 1; i < 6; i++)
        {
            var x = mn.X + (mx.X - mn.X) * i / 6;
            q.LinhasLotes.Add(new LinhaDeLote { Elemento = "v" + i, Pontos = new() { new(x, mn.Y - 1), new(x, mx.Y + 1) } });
        }
        var d = LotDivider.Divide(q);
        Assert.Equal(6, d.Parcelas.Count);
        Assert.Equal(q.Polygon!.Area, d.Parcelas.Sum(p => p.Area), 3);
        // Diagonal de quina a quina (passa pelos vértices) e uma linha por cima do contorno (não divide nada).
        var r = PorLinhas(("diag", new[] { Vec2.Zero, new Vec2(60, 30) }), ("borda", new[] { new Vec2(0, 30), new Vec2(60, 30) }));
        var dd = LotDivider.Divide(r);
        Assert.Equal(2, dd.Parcelas.Count);
        Assert.All(dd.Parcelas, p => Assert.Equal(900, p.Area, 3));
        Assert.Equal(new[] { "borda" }, dd.LinhasSoltas);
    }

    // ================================================================== 3 – categoria por clique (pincel)

    [Fact]
    public void C_PincelDeCategoria_TestadaELinhas()
    {
        // Pela testada: um lote vira institucional; a numeração se refaz (AI-1) e os vizinhos não mudam.
        var w = LTScenes.World();
        var q = w.Quadras[0];
        var d = LotDivider.Divide(q, w.Lot);
        var p = d.Parcelas.First(x => x.Categoria == CategoriaLote.Lote && !x.Esquina);
        LotDivider.DefineCategoria(q, p, CategoriaLote.Institucional);
        var d1 = LotDivider.Divide(q, w.Lot);
        var inst = Assert.Single(d1.Parcelas, x => x.Chave == p.Chave);
        Assert.Equal(CategoriaLote.Institucional, inst.Categoria);
        Assert.StartsWith("AI-", inst.Numero);
        Assert.Equal(d.Parcelas.Count, d1.Parcelas.Count);
        // De volta a lote: o ajuste sai (nada gravado à toa).
        LotDivider.DefineCategoria(q, inst, CategoriaLote.Lote);
        Assert.Null(q.Ajuste(p.Chave));
        // Por linhas: área verde num lote; os outros continuam lotes 1..3.
        var l = PorLinhas(V("a", 15), V("b", 30), V("c", 45));
        var dl = LotDivider.Divide(l);
        LotDivider.DefineCategoria(l, dl.Parcelas[1], CategoriaLote.AreaVerde);
        var dl1 = LotDivider.Divide(l);
        Assert.Equal(dl.Parcelas[1].Chave, Assert.Single(dl1.Parcelas, x => x.Categoria == CategoriaLote.AreaVerde).Chave);
        Assert.Equal(new[] { "1", "2", "3" }, dl1.Parcelas.Where(x => x.Categoria == CategoriaLote.Lote).Select(x => x.Numero).OrderBy(x => x));
    }

    [Fact]
    public void Q_LoteComFrenteEFundosParaVia_NaoEEsquina()
    {
        // Gleba em volta de toda a quadra: os lados norte e sul são vias; o lote do meio (15 × 30) tem frente e fundos para via.
        var lot = new LoteamentoDefinition { Nome = "L", Gleba = Polygon2.Rectangle(new Vec2(-20, -20), new Vec2(80, 50)).Outer.ToList() };
        var q = PorLinhas(V("a", 15), V("b", 30), V("c", 45));
        q.LoteamentoId = lot.Id;
        var r = LotReport.Resumo(lot, new[] { q });
        var (qq, d) = r.Quadras[0];
        var meio = d.Parcelas.Single(x => x.Forma.Contains(new Vec2(22, 10)));
        var obs = LotReport.LinhaLote(meio, d, qq, lot)[6];
        Assert.DoesNotContain("esquina", obs);
        Assert.Contains("frente e fundos para via", obs);
        // O da ponta (via também no lado) é esquina.
        var ponta = d.Parcelas.Single(x => x.Forma.Contains(new Vec2(5, 10)));
        Assert.Contains("esquina", LotReport.LinhaLote(ponta, d, qq, lot)[6]);
    }
}
