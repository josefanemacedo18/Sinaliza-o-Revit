using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using Xunit;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada 6, item 12: Cotar seção com camadas coloridas e hachuradas por elemento, meio-fio com perfil, legenda dos elementos
/// (amostra, nome e largura) e nomes/cores escolhidos pelo usuário – no perfil, na legenda e nos nomes da planta.
/// </summary>
public class AF3SectionStyleTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static (SectionDimensionDefinition Sd, BuildContext Ctx) Scene(int version, List<SectionItemStyle>? styles)
    {
        var (defs, geo, path) = DetailGenerator.SectionSample();
        var sd = new SectionDimensionDefinition { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45), DesenhoVersao = version, ItemStyles = styles };
        var all = defs.Append(sd).ToList();
        var ctx = new BuildContext
        {
            Catalog = Cat, ViewScale = 100, AllDefinitions = () => all, GeometryOf = geo, PathOf = path,
            Lookup = id => all.FirstOrDefault(d => d.Id == id),
        };
        return (sd, ctx);
    }

    private static List<SectionItemStyle> Custom() => new()
    {
        new SectionItemStyle { Key = "Calçada", Name = "Passeio público", Rgb = "#D2B48C" },
        new SectionItemStyle { Key = "Ciclofaixa", Rgb = "#FF8800", Hatch = Hachura.Cruzada },
        new SectionItemStyle { Key = "Faixa de rolamento", Name = "Faixa de tráfego" },
    };

    [Fact]
    public void Itens_da_secao_com_nomes_larguras_cores_e_hachuras()
    {
        var (sd, ctx) = Scene(2, Custom());
        var items = DetailGenerator.SectionItems(sd, ctx, sd.ItemStyles);
        var keys = items.Select(i => i.Key).ToList();
        foreach (var k in new[] { "Calçada", "Faixa gramada", "Meio-fio", "Sarjeta", "Ciclofaixa", "Faixa de rolamento" })
            Assert.Contains(k, keys);
        var walk = items.Single(i => i.Key == "Calçada");
        Assert.Equal("Passeio público", walk.Name);
        Assert.Equal("#D2B48C", walk.Rgb);
        Assert.Equal("2 × 2,45 m", walk.WidthText);
        var curb = items.Single(i => i.Key == "Meio-fio");
        Assert.Equal("2 × 0,15 m", curb.WidthText);
        Assert.Equal(Hachura.DiagonalDensa, curb.Hatch);
        var bike = items.Single(i => i.Key == "Ciclofaixa");
        Assert.Equal("#FF8800", bike.Rgb);
        Assert.Equal(Hachura.Cruzada, bike.Hatch);
        Assert.Equal("1,60 m", bike.WidthText);   // do eixo da LBO à borda (cotas de faixa eixo a eixo, como a cadeia)
        Assert.Equal("Faixa de tráfego", items.Single(i => i.Key == "Faixa de rolamento").Name);
        Assert.Equal(Hachura.Grama, items.Single(i => i.Key == "Faixa gramada").Hatch);
    }

    [Fact]
    public void Perfil_com_legenda_completa_e_nomes_e_cores_personalizados()
    {
        var (sd, ctx) = Scene(2, Custom());
        var pd = SectionProfileDefinition.From(sd, new Vec2(0, -11));
        Assert.Equal(2, pd.DesenhoVersao);
        Assert.Equal(3, pd.ItemStyles!.Count);
        var geo = DetailGenerator.SectionProfileDrawing(pd, ctx);
        var texts = geo.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("ELEMENTOS DA SEÇÃO", texts);
        Assert.DoesNotContain("MATERIAIS", texts);
        // Legenda completa: cada elemento com nome e largura, e a amostra com a cor/hachura escolhidas.
        var items = DetailGenerator.SectionItems(sd, ctx, sd.ItemStyles);
        foreach (var it in items)
        {
            Assert.Contains(it.Name, texts);
            Assert.Contains(it.WidthText, texts);
            Assert.Contains(geo.Pieces, p => p.Rgb == it.Rgb && p.Hatch == it.Hatch && p.Color == it.Color && p.Shape.Outer.Count == 4);
        }
        Assert.DoesNotContain("Calçada", texts);           // renomeada em todo o desenho
        Assert.Contains("Passeio público", texts);
        // Camadas do perfil: a ciclofaixa leva a cor e a hachura escolhidas, o pavimento a hachura do material.
        Assert.True(geo.Pieces.Count(p => p.Rgb == "#FF8800" && p.Hatch == Hachura.Cruzada) >= 2);   // camada + amostra
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Asfalto && p.Hatch == Hachura.Pontos);
        Assert.Contains(geo.Pieces, p => p.Color == MarkingColor.Concreto && p.Rgb == "#D2B48C" && p.Hatch == Hachura.Diagonal);
        // Meio-fio com perfil (face inclinada: 5 vértices), embutido abaixo do pavimento, nos dois lados.
        var curbs = geo.Pieces.Where(p => p.Hatch == Hachura.DiagonalDensa && p.Shape.Outer.Count == 5).ToList();
        Assert.Equal(2, curbs.Count);
        var legendX = geo.Annotations.OfType<AnnotationText>().First(t => t.Text == "ELEMENTOS DA SEÇÃO").Position.X - 5;
        var asphaltBottom = geo.Pieces.Where(p => p.Color == MarkingColor.Asfalto && p.Shape.Bounds.Max.X < legendX).Min(p => p.Shape.Bounds.Min.Y);
        Assert.All(curbs, c => Assert.True(c.Shape.Bounds.Min.Y < asphaltBottom - 1e-6));
        // Proporções do pré-moldado (notas de urbanização do plugin: 15 × 13 × 30 cm): altura total = 2 × aparente (0,30 m) e
        // topo 13/15 da base, no desenho a 1:50 numa vista 1:100 (× 2).
        Assert.All(curbs, c =>
        {
            var (mn, mx) = c.Shape.Bounds;
            Assert.Equal(0.30 * 2, mx.Y - mn.Y, 6);
            var top = c.Shape.Outer.Where(v => Math.Abs(v.Y - mx.Y) < 1e-9).ToList();
            Assert.Equal(0.15 * 2 * 13 / 15, top.Count == 1 ? Math.Abs(top[0].X - (top[0].X - mn.X < mx.X - top[0].X ? mn.X : mx.X)) : top.Max(v => v.X) - top.Min(v => v.X), 6);
        });
        // Nomes também na planta (cadeia de cotas).
        var plan = DetailGenerator.SectionDimensions(sd, ctx).Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains(plan, t => t.Replace("\n", " ") == "Passeio público");
        Assert.DoesNotContain(plan, t => t == "Calçada");
    }

    [Fact]
    public void Perfis_salvos_sem_os_campos_novos_ficam_como_antes()
    {
        // Seção gravada antes desta rodada (sem DesenhoVersao/ItemStyles): legenda de materiais, sem hachura nem cor própria.
        var (sd, ctx) = Scene(0, null);
        var json = SectionProfileDefinition.From(sd, new Vec2(0, -11)).ToJson()
            .Replace(",\"desenhoVersao\":0", "");
        var pd = (SectionProfileDefinition)MarkingDefinition.FromJson(json)!;
        Assert.Equal(0, pd.DesenhoVersao);
        Assert.Null(pd.ItemStyles);
        var geo = DetailGenerator.SectionProfileDrawing(pd, ctx);
        var texts = geo.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("MATERIAIS", texts);
        Assert.All(geo.Pieces, p => { Assert.Null(p.Rgb); Assert.Equal(Hachura.Nenhuma, p.Hatch); });
        Assert.DoesNotContain(geo.Pieces, p => p.Shape.Outer.Count == 5);
    }

    [Fact]
    public void Estilos_se_juntam_por_chave()
    {
        var project = new List<SectionItemStyle> { new() { Key = "Calçada", Name = "Passeio" }, new() { Key = "Sarjeta", Rgb = "#777777" } };
        var merged = SectionItemStyle.Merge(project, new[] { new SectionItemStyle { Key = "Calçada", Name = "Passeio público" }, new SectionItemStyle { Key = "Sarjeta" } });
        Assert.Single(merged);
        Assert.Equal("Passeio público", merged[0].Name);
        Assert.True(Rgb.TryParse("#ff8800", out var c) && c == new Rgb(255, 136, 0));
        Assert.False(Rgb.TryParse("laranja", out _));
    }
}
