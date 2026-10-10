using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AI, item 2: placas personalizadas guardadas no projeto e placas empilhadas no mesmo suporte – legenda, detalhe da
/// placa, quadro de placas e quantitativos contam cada placa.
/// </summary>
public class AI2SignStackTests
{
    private static Catalogo Cat() => CatalogService.LoadDefault();

    private static PlacaDef Custom() => CustomSigns.Create("PP-1", "Rua sem saída – Vila Nova", FormaPlaca.Retangulo, 1.20, 0.40,
        MarkingColor.Azul, MarkingColor.Branca, "RUA SEM SAÍDA\nVILA NOVA", MarkingColor.Branca);

    [Fact]
    public void Placa_personalizada_salva_e_lida_de_volta()
    {
        var json = CustomSigns.Serialize(new[] { Custom() });
        var back = Assert.Single(CustomSigns.Parse(json));
        Assert.Equal("PP-1", back.Codigo);
        Assert.Equal("Rua sem saída – Vila Nova", back.Nome);
        Assert.Equal(FormaPlaca.Retangulo, back.Forma);
        Assert.Equal(1.20, back.Largura, 6);
        Assert.Equal(0.40, back.Altura, 6);
        Assert.Equal(MarkingColor.Azul, back.CorFundo);
        Assert.Equal(MarkingColor.Branca, back.CorOrla);
        Assert.Equal("RUA SEM SAÍDA\nVILA NOVA", back.Legenda);
        Assert.True(back.Personalizada);
        // Código de uma placa normativa não pode ser reaproveitado.
        var cat = Cat();
        Assert.NotEmpty(CustomSigns.Validate(CustomSigns.Create("R-1", "x", FormaPlaca.Circulo, 0.5, 0.5, MarkingColor.Branca, MarkingColor.Vermelha, null, MarkingColor.Preta), cat));
        Assert.Empty(CustomSigns.Validate(back, cat));
    }

    [Fact]
    public void Placa_personalizada_entra_no_modelo_legenda_e_quantitativo()
    {
        var cat = Cat();
        CustomSigns.Apply(cat, CustomSigns.Parse(CustomSigns.Serialize(new[] { Custom() })));
        var sign = new SignDefinition { Code = "PP-1", Position = new Vec2(0, 0) };
        var ctx = new BuildContext { Catalog = cat, AllDefinitions = () => new MarkingDefinition[] { sign } };
        var geo = MarkingBuilder.Build(sign, null, ctx);
        Assert.DoesNotContain(geo.Warnings, w => w.Contains("não existe"));
        Assert.NotEmpty(geo.Pieces);
        var legend = DetailGenerator.Legend(new LegendDefinition(), ctx);
        Assert.Equal(1, legend.UnitCount);
        Assert.Contains(legend.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith("PP-1 – Rua sem saída"));
        var rows = QuantityCalculator.Compute(new[] { ((MarkingDefinition)sign, geo) }, cat);
        Assert.Equal(1, Assert.Single(rows, r => r.Code == "PP-1").Units);
    }

    [Fact]
    public void Placa_personalizada_com_pictograma_do_catalogo_e_linhas_de_texto()
    {
        var cat = Cat();
        var from = cat.Placa("A-32b")!;
        Assert.NotEmpty(from.Pictograma!);
        var p = CustomSigns.WithPictogram(CustomSigns.Create("PP-2", "Escola – 200 m", FormaPlaca.Retangulo, 1.50, 0.50,
            MarkingColor.Amarela, MarkingColor.Preta, "ESCOLA\n200 m", MarkingColor.Preta), from);
        Assert.Equal(from.Pictograma!.Count, p.Pictograma!.Count);
        Assert.NotSame(from.Pictograma, p.Pictograma);
        Assert.NotSame(from.Pictograma[0], p.Pictograma[0]);
        // Salva e lida de volta com o pictograma.
        var back = Assert.Single(CustomSigns.Parse(CustomSigns.Serialize(new[] { p })));
        Assert.Equal(p.Pictograma.Count, back.Pictograma!.Count);
        Assert.Equal("ESCOLA\n200 m", back.Legenda);

        // Face: pictograma num quadrado à esquerda e as duas linhas de texto à direita – os dois desenhados.
        var face = SignGenerator.Face(back, 1.50, 0.50, 0, null, new BlockFontProvider());
        var border = back.Orla * 1.50;
        var split = -0.75 + border + (0.50 - 2 * border);
        var symbols = face.Where(f => f.Layer >= 2).Select(f => f.Shape.Bounds).ToList();
        Assert.Contains(symbols, b => b.Max.X <= split + 1e-6);
        Assert.Contains(symbols, b => b.Min.X >= split - 1e-6);
        Assert.All(symbols, b => Assert.True(b.Max.X <= split + 1e-6 || b.Min.X >= split - 1e-6, "pictograma e texto não se sobrepõem"));
        var text = symbols.Where(b => b.Min.X >= split - 1e-6).ToList();
        Assert.True(text.Max(b => b.Max.Y) - text.Min(b => b.Min.Y) > 0.20, "duas linhas de texto");

        // Sem pictograma ("nenhum"): só o texto, como antes.
        var none = CustomSigns.WithPictogram(CustomSigns.Create("PP-3", "x", FormaPlaca.Retangulo, 1.0, 0.4, MarkingColor.Azul, MarkingColor.Branca, "X", MarkingColor.Branca), null);
        Assert.Null(none.Pictograma);
        Assert.False(none.Proibicao);
    }

    private static SignDefinition Stacked() => new()
    {
        Code = "R-1", Position = new Vec2(5, 2), MountHeight = 2.10, StackGap = 0.05,
        Stack = new List<StackedSign> { new() { Code = "R-19" }, new() { Code = "PP-1" } },
    };

    [Fact]
    public void Tres_placas_empilhadas_com_alturas_e_espacamento_corretos()
    {
        var cat = Cat();
        CustomSigns.Apply(cat, new[] { Custom() });
        var d = Stacked();
        var plates = SignStack.Layout(d, cat);
        Assert.Equal(new[] { "R-1", "R-19", "PP-1" }, plates.Select(p => p.Def.Codigo));
        // A mais baixa (PP-1, 0,40 m) com a altura livre; as outras acima, a 0,05 m uma da outra.
        Assert.Equal(2.10, plates[2].Bottom, 6);
        Assert.Equal(0.40, plates[2].PlateHeight, 6);
        for (int i = 0; i + 1 < plates.Count; i++)
            Assert.Equal(0.05, plates[i].Bottom - (plates[i + 1].Bottom + plates[i + 1].PlateHeight), 6);
        var top = plates[0].Bottom + plates[0].PlateHeight;

        // No modelo: as chapas de cada placa na altura dela e o poste até o topo da placa de cima.
        var ctx = new BuildContext { Catalog = cat };
        var geo = MarkingBuilder.Build(d, null, ctx);
        var faces = geo.Pieces.Where(p => p.Profile != null).Select(p => p.Profile!.Profile.Bounds).ToList();
        foreach (var pl in plates)
            Assert.True(faces.Any(b => Math.Abs(b.Min.Y - pl.Bottom) < 1e-3 && Math.Abs(b.Max.Y - (pl.Bottom + pl.PlateHeight)) < 1e-3),
                $"{pl.Def.Codigo}: {pl.Bottom:0.000}–{pl.Bottom + pl.PlateHeight:0.000}; faces: " + string.Join(" ", faces.Select(b => $"[{b.Min.Y:0.000};{b.Max.Y:0.000}]").Distinct()));
        var post = geo.Pieces.Single(p => p.Color == MarkingColor.Metal && p.Profile == null);
        Assert.Equal(top + d.BaseElevation, post.Elevation + post.Thickness, 6);
        Assert.DoesNotContain(geo.Warnings, w => w.Contains("Altura livre"));
    }

    [Fact]
    public void Empilhadas_contam_na_legenda_no_quadro_no_detalhe_e_no_quantitativo()
    {
        var cat = Cat();
        CustomSigns.Apply(cat, new[] { Custom() });
        var d = Stacked();
        var defs = new MarkingDefinition[] { d };
        var ctx = new BuildContext { Catalog = cat, AllDefinitions = () => defs, Lookup = id => defs.FirstOrDefault(x => x.Id == id) };
        var geo = MarkingBuilder.Build(d, null, ctx);

        // Legenda: uma linha por placa.
        var legend = DetailGenerator.Legend(new LegendDefinition(), ctx);
        Assert.Equal(3, legend.UnitCount);
        foreach (var code in new[] { "R-1", "R-19", "PP-1" })
            Assert.Contains(legend.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith(code + " – "));

        // Quadro de placas: cada placa com quantidade 1.
        var table = DetailGenerator.QuantityTable(new QuantityTableDefinition { SignsOnly = true }, ctx);
        var texts = table.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        foreach (var code in new[] { "R-1", "R-19", "PP-1" }) Assert.Contains(code, texts);

        // Detalhe da placa: as três placas desenhadas e a altura livre sob a mais baixa.
        var detail = DetailGenerator.TypicalDetail(new TypicalDetailDefinition { MarkingTargetId = d.Id }, ctx);
        var notes = string.Join("\n", detail.Annotations.OfType<AnnotationText>().Select(t => t.Text));
        Assert.Contains("No mesmo suporte: R-19", notes);
        Assert.Contains("No mesmo suporte: PP-1", notes);
        Assert.Contains("mais baixa: 2,10", notes.Replace('.', ','));

        // Quantitativo: uma unidade de cada código.
        var rows = QuantityCalculator.Compute(new[] { ((MarkingDefinition)d, geo) }, cat);
        foreach (var code in new[] { "R-1", "R-19", "PP-1" }) Assert.Equal(1, Assert.Single(rows, r => r.Code == code).Units);
    }

    [Fact]
    public void Placa_sem_pilha_continua_igual()
    {
        // Projetos salvos antes (sem Stack) geram a mesma placa.
        var cat = Cat();
        var json = new SignDefinition { Code = "R-1", Position = new Vec2(1, 1) }.ToJson();
        var d = (SignDefinition)MarkingDefinition.FromJson(json)!;
        Assert.Null(d.Stack);
        Assert.Equal(0.05, d.StackGap, 9);
        var g = MarkingBuilder.Build(d, null, new BuildContext { Catalog = cat });
        var post = g.Pieces.Single(p => p.Color == MarkingColor.Metal && p.Profile == null);
        var p = cat.Placa("R-1")!;
        Assert.Equal(d.BaseElevation + d.MountHeight + SignGenerator.PlateHeight(p.Forma, p.Largura, p.Altura), post.Elevation + post.Thickness, 6);
    }
}
