using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada LV: extensão de calçada manual junto à interseção (1), janelas com rolagem horizontal (2), quadro de áreas com o
/// sistema viário (3), quadra que não se repete e detalhamento que não volta ao início na troca de escala (4), chanfro na
/// quina do lote de esquina (5) e rampas no masterplan (6).
/// </summary>
public class LVCorrecoesTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    // ================================================================== cenas

    /// <summary>
    /// Gleba de 200 × 180 m com uma só quadra de 60 × 60 m cercada por vias de 10 m (anel de 80 × 80 m) no meio – o resto da
    /// gleba fica fora das vias (como no projeto da imagem 4).
    /// </summary>
    public static (LoteamentoDefinition Lot, Polygon2 Gleba, List<Polygon2> Occ, ResultadoReconhecimento Rec, List<QuadraDefinition> Quadras) Anel()
    {
        var gleba = Polygon2.Rectangle(new Vec2(-100, -90), new Vec2(100, 90));
        var ring = new Polygon2(Polygon2.Rectangle(new Vec2(-40, -40), new Vec2(40, 40)).Outer,
            new[] { Polygon2.Rectangle(new Vec2(-30, -30), new Vec2(30, 30)).Outer.Reverse().ToList() });
        var occ = new List<Polygon2> { ring };
        var rec = Reconhecimento.Quadras(gleba, occ);
        var lot = new LoteamentoDefinition { Nome = "Gleba grande", Gleba = gleba.Outer.ToList(), Remanescentes = rec.Remanescentes.Select(AreaRemanescente.From).ToList() };
        var quadras = rec.Quadras.Select(r => Reconhecimento.NovaQuadra(r, lot, 10, 30)).ToList();
        return (lot, gleba, occ, rec, quadras);
    }

    // ================================================================== 4 – quadra repetida

    [Fact]
    public void D4_GlebaMaiorQueAsVias_UmaQuadraSo()
    {
        var a = Anel();
        // Só a quadra cercada pelas vias; o resto da gleba (fora do anel) não é quadra.
        var q = Assert.Single(a.Quadras);
        Assert.InRange(q.Polygon!.Area, 3600 - 1, 3600 + 1);
        Assert.Equal(a.Quadras.Count, a.Quadras.Select(x => x.Nome).Distinct().Count());
        // A sobra da gleba (200 × 180 − 80 × 80) fica gravada como área remanescente, com o furo das vias.
        var rem = Assert.Single(a.Lot.RemanescentePolygons);
        Assert.InRange(rem.Area, 36000 - 6400 - 1, 36000 - 6400 + 1);
        Assert.Single(rem.Holes);
        Assert.InRange(a.Rec.AreaViaria, 2800 - 1, 2800 + 1);
    }

    [Fact]
    public void D4_Detalhamento_SemGlebaRepetidaNemRotulosDuplicados()
    {
        var a = Anel();
        var defs = new List<MarkingDefinition> { a.Lot };
        defs.AddRange(a.Quadras);
        var det = new LoteamentoDetalheDefinition { LoteamentoId = a.Lot.Id, Cores = true };
        var geo = MarkingBuilder.Build(det, null, new BuildContext { Catalog = Cat, AllDefinitions = () => defs, ViewScale = 500 });
        // Nenhuma cor de lote maior que a quadra (a gleba não volta como "área verde" com outro piso); a sobra da gleba é área
        // verde com o furo das vias – não cobre as vias nem a quadra.
        var colored = geo.Pieces.Where(p => p.Layer?.StartsWith(LotDrawing.LayerPrefix) == true).ToList();
        var lots = colored.Where(p => !p.Layer!.StartsWith(LotDrawing.LayerPrefix + "r:")).ToList();
        Assert.NotEmpty(lots);
        Assert.All(lots, p => Assert.True(p.Shape.Area <= 3600 + 1, $"piso de {p.Shape.Area:0} m² (a quadra tem 3600 m²)"));
        var rem = Assert.Single(colored, p => p.Layer!.StartsWith(LotDrawing.LayerPrefix + "r:"));
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(new[] { rem.Shape }, a.Occ.Append(a.Quadras[0].Polygon!))) < 0.5,
            "a área remanescente cobre as vias ou a quadra");
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(lots.Select(p => p.Shape), a.Occ)) < 0.5, "lote sobre a via");
        // Um círculo de quadra por quadra e cada rótulo de área (AV-1, ...) uma vez só.
        Assert.Single(geo.Pieces, p => p.Layer?.StartsWith(LotDrawing.QuadraLayerPrefix) == true && p.Color == MarkingColor.Branca);
        var labels = geo.Annotations.OfType<AnnotationText>().Select(t => t.Text).Where(t => Regex.IsMatch(t, @"^A[A-Z]-\d+$")).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Fact]
    public void D4_TrocaDeEscala_AjustaNoLugarSemPerderAsEdicoes()
    {
        // O detalhamento dos lotes (e os demais detalhes) desenhado numa vista não é apagado e refeito quando a escala dela muda.
        var a = Anel();
        var det = new LoteamentoDetalheDefinition { LoteamentoId = a.Lot.Id, Output = { ViewId = "planta-1", Mode = OutputMode.Detalhe2D } };
        Assert.True(AnnotationScale.AjustaNoLugar(det));
        Assert.True(AnnotationScale.AjustaNoLugar(new SectionDimensionDefinition()));
        Assert.True(AnnotationScale.AjustaNoLugar(new QuadroLoteamentoDefinition()));
        Assert.False(AnnotationScale.AjustaNoLugar(new SheetSegmentDefinition()));

        var defs = new List<MarkingDefinition> { a.Lot };
        defs.AddRange(a.Quadras);
        MarkingGeometry At(double scale) => MarkingBuilder.Build(det, null, new BuildContext { Catalog = Cat, AllDefinitions = () => defs, ViewScale = scale });
        var antes = AnnotationScale.Itens(At(500));
        var depois = AnnotationScale.Itens(At(250));
        // O que a pessoa fez no detalhe (escala 1:500): moveu o número do lote 1, apagou o do lote 2 e editou o do lote 3.
        var existentes = antes.ToList();
        int Idx(string text) => existentes.FindIndex(x => x.Tipo == TipoItemDetalhe.Texto && x.Chave == text);
        var moved = Idx("1");
        var shift = new Vec2(1.5, -0.8);
        existentes[moved] = existentes[moved].Movido(shift);
        var edited = Idx("3");
        existentes[edited] = ItemDetalhe.Texto("Lote três", existentes[edited].Ref);
        var deleted = Idx("2");
        var deletedRef = existentes[deleted].Ref;
        existentes.RemoveAt(deleted);
        if (edited > deleted) edited--;
        if (moved > deleted) moved--;

        var plan = AnnotationScale.Ajustar(antes, depois, existentes, 500 / 1000.0);
        var target = plan.Mover.ToDictionary(m => m.Existente, m => m.Novo);
        Assert.Empty(plan.Apagar);
        // Nada volta: o número apagado não é recriado.
        Assert.DoesNotContain(plan.Criar, x => x.Tipo == TipoItemDetalhe.Texto && x.Chave == "2");
        // Todos os outros elementos são reaproveitados (nenhum é recriado).
        Assert.Equal(existentes.Count, plan.Mover.Count);
        var n1 = depois.First(x => x.Tipo == TipoItemDetalhe.Texto && x.Chave == "1");
        Assert.True(target[moved].Ref.DistanceTo(n1.Ref + shift) < 1e-9, "o texto movido perdeu o deslocamento");
        var n3 = depois.First(x => x.Tipo == TipoItemDetalhe.Texto && x.Chave == "3");
        Assert.True(target[edited].Ref.DistanceTo(n3.Ref) < 1e-9, "o texto editado não acompanhou a escala");
        // Os intactos vão exatamente para o desenho da escala nova (textos, cotas, linhas e o círculo da quadra).
        foreach (var (e, novo) in plan.Mover.Where(m => m.Existente != moved && m.Existente != edited))
            Assert.Contains(depois, d => d.Tipo == novo.Tipo && d.Ref.DistanceTo(novo.Ref) < 1e-9);
        Assert.DoesNotContain(plan.Mover, m => m.Novo.Tipo == TipoItemDetalhe.Texto && m.Novo.Ref.DistanceTo(deletedRef) < 1e-9 && m.Novo.Chave == "2");
    }

    [Fact]
    public void D4_SoCorteTransversal_SemCotasNaPlanta()
    {
        // "Só o corte transversal": na planta ficam a linha de corte e as marcas A–A (sem a cadeia de cotas); o perfil sai inteiro.
        var (defs, geoOf, pathOf) = DetailGenerator.SectionSample();
        var ctx = new BuildContext { Catalog = Cat, ViewScale = 100, AllDefinitions = () => defs, GeometryOf = geoOf, PathOf = pathOf };
        SectionDimensionDefinition Sd(bool chain) => new() { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45), PlanChain = chain, Profile = true };
        var full = DetailGenerator.SectionDimensions(Sd(true), ctx);
        var only = DetailGenerator.SectionDimensions(Sd(false), ctx);
        static bool Number(AnnotationText t) => Regex.IsMatch(t.Text, @"^\d+,\d+$");
        Assert.Contains(full.Annotations.OfType<AnnotationText>(), Number);
        Assert.DoesNotContain(only.Annotations.OfType<AnnotationText>(), Number);
        Assert.Contains(only.Annotations.OfType<AnnotationText>(), t => t.Text == "A");
        Assert.Contains(only.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith("SEÇÃO A–A"));
        var cut = Sd(false);
        var withCut = new BuildContext { Catalog = Cat, ViewScale = 100, AllDefinitions = () => defs.Append(cut).ToList(), GeometryOf = geoOf, PathOf = pathOf,
            Lookup = id => id == cut.Id ? cut : defs.FirstOrDefault(x => x.Id == id) };
        var profile = DetailGenerator.SectionProfileDrawing(SectionProfileDefinition.From(cut, Vec2.Zero), withCut);
        Assert.Contains(profile.Annotations.OfType<AnnotationText>(), Number);
    }

    // ================================================================== 3 – quadro de áreas

    [Fact]
    public void D3_QuadroDeAreas_SistemaViarioContadoESomaIgualAGleba()
    {
        var a = Anel();
        var r = LotReport.Resumo(a.Lot, a.Quadras);
        // Vias dentro da gleba: 80² − 60² = 2800 m².
        Assert.InRange(r.AreaViaria, 2800 - 1, 2800 + 1);
        Assert.All(r.Categorias, c => Assert.InRange(c.Percentual, 0, 100));
        // A gleba é a soma de tudo: lotes + áreas (a sobra da gleba como área verde) + sistema viário = 100 %.
        Assert.InRange(r.Categorias.Sum(c => c.Area) + r.AreaViaria, r.AreaGleba - 0.01, r.AreaGleba + 0.01);
        Assert.InRange(r.Categorias.Sum(c => c.Percentual) + r.Pct(r.AreaViaria), 100 - 1e-6, 100 + 1e-6);
        Assert.InRange(r.AreaRemanescente, 29600 - 1, 29600 + 1);
        var verde = Assert.Single(r.Categorias, c => c.Nome == CategoriaLote.AreaVerde);
        Assert.InRange(verde.Area, 29600 + r.Quadras.Sum(q => q.Divisao.Parcelas.Where(p => p.Categoria == CategoriaLote.AreaVerde).Sum(p => p.Area)) - 1, double.MaxValue);
        Assert.True(verde.Publica);
        Assert.InRange(r.Pct(r.AreaPublica), 0, 100);
        // No grade normal (sem sobra), a mesma soma.
        var w = LTScenes.World();
        var rw = LotReport.Resumo(w.Lot, w.Quadras);
        Assert.InRange(rw.Categorias.Sum(c => c.Area) + rw.AreaViaria, rw.AreaGleba - 0.01, rw.AreaGleba + 0.01);
        Assert.True(rw.AreaViaria > 1000);
    }

    /// <summary>Largura de avanço da Arial (milésimos do em); a altura do texto do Revit é a altura das maiúsculas (0,716 em).</summary>
    private static readonly Dictionary<char, int> Arial = BuildArial();

    private static Dictionary<char, int> BuildArial()
    {
        var w = new Dictionary<char, int> { [' '] = 278, ['.'] = 278, [','] = 278, ['('] = 333, [')'] = 333, ['+'] = 584, ['%'] = 889, ['²'] = 333,
            ['-'] = 333, ['–'] = 556, ['/'] = 278, [':'] = 278, ['º'] = 365 };
        var up = new[] { 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611 };
        var lo = new[] { 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500 };
        for (int i = 0; i < 26; i++) { w[(char)('A' + i)] = up[i]; w[(char)('a' + i)] = lo[i]; }
        for (char d = '0'; d <= '9'; d++) w[d] = 556;
        return w;
    }

    /// <summary>Largura (mm de papel) do texto em Arial com a altura de maiúsculas <paramref name="mm"/>.</summary>
    public static double ArialWidthMm(string s, double mm) => s.Sum(ch =>
    {
        if (Arial.TryGetValue(ch, out var v)) return v;
        var b = ch.ToString().Normalize(System.Text.NormalizationForm.FormD)[0];
        return Arial.TryGetValue(b, out var bv) ? bv : 556;
    }) / 1000.0 / 0.716 * mm;

    [Fact]
    public void D3_QuadroDeAreas_TextosCabemNasCelulas()
    {
        var w = LTScenes.World();
        var defs = new List<MarkingDefinition> { w.Lot };
        defs.AddRange(w.Quadras);
        var ctx = new BuildContext { Catalog = Cat, AllDefinitions = () => defs, ViewScale = 100 };
        var qd = new QuadroLoteamentoDefinition { LoteamentoId = w.Lot.Id, Conteudo = TipoQuadroLoteamento.Areas };
        var geo = MarkingBuilder.Build(qd, null, ctx);
        var mm = ctx.Mm(1);
        var lines = geo.Annotations.OfType<AnnotationLine>().ToList();
        var pts = lines.SelectMany(l => l.Points).ToList();
        double left = pts.Min(p => p.X), right = pts.Max(p => p.X), bottom = pts.Min(p => p.Y);
        var xs = lines.Where(l => l.Points.Count == 2 && Math.Abs(l.Points[0].X - l.Points[1].X) < 1e-6).Select(l => l.Points[0].X)
            .Append(left).Append(right).Distinct().OrderBy(x => x).ToList();
        var faults = new List<string>();
        foreach (var t in geo.Annotations.OfType<AnnotationText>().Where(t => t.Position.Y > bottom))
        {
            var width = ArialWidthMm(t.Text, t.PaperHeightMm) * mm;
            var (x0, x1) = t.Align switch
            {
                TextAlign.Left => (t.Position.X, t.Position.X + width),
                TextAlign.Right => (t.Position.X - width, t.Position.X),
                _ => (t.Position.X - width / 2, t.Position.X + width / 2),
            };
            // Célula onde o texto foi ancorado (o título ocupa a largura toda).
            var title = t.Text == qd.TituloPadrao;
            var cl = title ? left : xs.Last(x => x <= t.Position.X + 1e-6 - (t.Align == TextAlign.Right ? 1e-3 : 0));
            var cr = title ? right : xs.First(x => x > cl + 1e-6);
            if (x0 < cl + 0.8 * mm || x1 > cr - 0.8 * mm) faults.Add($"\"{t.Text}\" ({(x1 - x0) / mm:0.0} mm) não cabe na célula de {(cr - cl) / mm:0.0} mm");
        }
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
    }
}
