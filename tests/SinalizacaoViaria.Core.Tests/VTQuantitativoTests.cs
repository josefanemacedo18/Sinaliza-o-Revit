using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Core.Reports;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada VT, fase 5: o corte e o aterro feitos no terreno entram no quantitativo – por hierarquia, com a parte de cada via e
/// interseção, e o balanço de massas (bota-fora ou empréstimo) – na janela, no CSV, na planilha e no memorial. Antes o volume só
/// aparecia no relatório da Terraplenagem.
/// </summary>
public class VTQuantitativoTests
{
    private static List<EarthworkItem> Items(TopoScene.Scene sc)
    {
        var res = new List<EarthworkItem>();
        var i = 0;
        foreach (var (owner, v) in sc.Design.ByOwner)
        {
            var road = sc.World.Roads.FirstOrDefault(r => r.Def.Id == owner)?.Def;
            res.Add(new EarthworkItem(road != null ? $"Via {++i}" : "Interseção", road?.Hierarchy, v.Cut, v.Fill, v.Area));
        }
        return res;
    }

    [Theory]
    [InlineData("incl5", "cruz", "local2", "local2")]
    [InlineData("incl8", "cruz", "coletora4", "local2")]
    [InlineData("suave", "T", "local2", "local2")]
    public void V13_TerraplenagemNoQuantitativo(string terrain, string kind, string a, string b)
    {
        var sc = VTRelevoTests.Get(terrain, kind, a, b);
        var items = Items(sc);
        var rows = QuantityCalculator.AddEarthworks(new List<QuantityRow>(), items, 1.25);
        var cut = rows.Where(r => r.Code == "TER-CORTE").ToList();
        var fill = rows.Where(r => r.Code == "TER-ATERRO").ToList();
        Assert.NotEmpty(cut);
        // Volumes: os mesmos da terraplenagem (a soma das partes é o total), em m³ e na categoria 10.
        Assert.Equal(sc.Design.CutM3, cut.Sum(r => r.Volume), 6);
        Assert.Equal(sc.Design.FillM3, fill.Sum(r => r.Volume), 6);
        Assert.All(rows, r =>
        {
            Assert.Equal("m³", r.Unit);
            Assert.Equal(r.Volume, r.MainQuantity);
            Assert.Equal(CategoriaQuantitativo.ObrasDeArteTerraplenagem, r.Category);
            Assert.Equal(QuantityCalculator.EarthworkSubcategory, r.Subcategory);
        });
        // Uma linha por hierarquia, com a parte de cada elemento na referência.
        Assert.Equal(cut.Count, cut.Select(r => r.Hierarchy).Distinct().Count());
        Assert.All(cut, r => Assert.Contains("m³", r.Reference));
        // Balanço de massas com o fator de homogeneização.
        var balance = sc.Design.CutM3 - sc.Design.FillM3 * 1.25;
        var bal = rows.Single(r => r.Code is "TER-BOTAFORA" or "TER-EMPRESTIMO");
        Assert.Equal(balance > 0 ? "TER-BOTAFORA" : "TER-EMPRESTIMO", bal.Code);
        Assert.Equal(Math.Abs(balance), bal.Volume, 6);
        // CSV e memorial.
        var csv = QuantityCalculator.ToCsv(rows);
        Assert.Contains("TER-CORTE", csv);
        Assert.Contains("m³", csv);
        var lines = Memorial.UnitLines(rows, CategoriaQuantitativo.ObrasDeArteTerraplenagem);
        Assert.Equal(sc.Design.CutM3, lines.Single(l => l.Code == "TER-CORTE").Quantity, 6);
    }

    /// <summary>Sem terraplenagem (nenhum volume), o quantitativo não muda.</summary>
    [Fact]
    public void V14_SemTerraplenagem_QuantitativoIgual()
    {
        var rows = new List<QuantityRow> { new() { Code = "LFO-1", Unit = "m", PaintedLength = 10 } };
        var res = QuantityCalculator.AddEarthworks(rows, Array.Empty<EarthworkItem>());
        Assert.Single(res);
        Assert.Equal(10, res[0].MainQuantity);
    }
}
