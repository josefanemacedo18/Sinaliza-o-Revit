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
}
