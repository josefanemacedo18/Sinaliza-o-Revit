using SinalizacaoViaria.Core.Catalog;

namespace SinalizacaoViaria.Core.Tests;

public class CatalogTests
{
    [Fact]
    public void DefaultCatalog_LoadsAndValidates()
    {
        var cat = CatalogService.LoadDefault();
        Assert.NotEmpty(cat.Lineares);
        Assert.NotEmpty(cat.Hachuras);
        Assert.NotEmpty(cat.Simbolos);
        Assert.NotEmpty(cat.Vagas);
        Assert.NotEmpty(cat.Materiais);
        Assert.Empty(CatalogService.Validate(cat));
    }

    [Theory]
    [InlineData("LFO-1")] [InlineData("LFO-2")] [InlineData("LFO-3")] [InlineData("LFO-4")]
    [InlineData("LMS-1")] [InlineData("LMS-2")] [InlineData("LBO")] [InlineData("LCO")]
    [InlineData("LRE")] [InlineData("LDP")] [InlineData("FTP-1")] [InlineData("FTP-2")]
    [InlineData("MCC")] [InlineData("LRV")] [InlineData("LPP")] [InlineData("PTA")] [InlineData("TAC-A")]
    public void DefaultCatalog_ContainsMbstCodes(string code)
    {
        Assert.NotNull(CatalogService.LoadDefault().Linear(code));
    }

    [Theory]
    [InlineData(40, "V ≤ 60 km/h (2 × 4 m)")]
    [InlineData(60, "V ≤ 60 km/h (2 × 4 m)")]
    [InlineData(70, "60 < V ≤ 80 km/h (3 × 6 m)")]
    [InlineData(110, "V > 80 km/h (4 × 12 m)")]
    public void VariantBySpeed(double speed, string expected)
    {
        var t = CatalogService.LoadDefault().Linear("LFO-2")!;
        Assert.Equal(expected, t.VariantePorVelocidade(speed)!.Nome);
    }

    [Fact]
    public void UserCatalog_OverridesByCode()
    {
        var cat = CatalogService.LoadDefault();
        var user = new Catalogo
        {
            Lineares =
            {
                new TipoLinearDef { Codigo = "LBO", Nome = "Bordo personalizado", Variantes = { new VarianteDef { Nome = "x", Faixas = { new FaixaDef { Largura = 0.2 } } } } },
                new TipoLinearDef { Codigo = "NOVO", Nome = "Nova", Variantes = { new VarianteDef { Nome = "x", Faixas = { new FaixaDef { Largura = 0.1 } } } } },
            },
        };
        var count = cat.Lineares.Count;
        CatalogService.Merge(cat, user);
        Assert.Equal(count + 1, cat.Lineares.Count);
        Assert.Equal("Bordo personalizado", cat.Linear("LBO")!.Nome);
    }

    [Fact]
    public void Catalog_RoundTripsJson()
    {
        var cat = CatalogService.LoadDefault();
        var again = CatalogService.Parse(CatalogService.Serialize(cat));
        Assert.Equal(cat.Lineares.Count, again.Lineares.Count);
        Assert.Equal(cat.Linear("LFO-4")!.Variantes[0].Faixas[1].Padrao, again.Linear("LFO-4")!.Variantes[0].Faixas[1].Padrao);
    }
}
