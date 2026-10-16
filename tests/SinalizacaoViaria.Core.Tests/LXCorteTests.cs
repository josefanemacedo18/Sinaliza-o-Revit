using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada LX: o Corte Transversal não deixa desenho na planta antes do perfil (1) e o perfil segue o layout das referências –
/// "seção típica de via local" (A) e "meio-fio com sarjeta tipo B" (B) (2).
/// </summary>
public class LXCorteTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static SectionDimensionDefinition Cut(bool chain, bool profile) =>
        new() { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45), PlanChain = chain, Profile = profile };

    private static BuildContext Ctx(IReadOnlyList<MarkingDefinition> defs, Func<MarkingDefinition, MarkingGeometry?> geoOf,
        Func<MarkingDefinition, Polyline2?> pathOf, params MarkingDefinition[] extra) => Ctx(100, defs, geoOf, pathOf, extra);

    private static BuildContext Ctx(double view, IReadOnlyList<MarkingDefinition> defs, Func<MarkingDefinition, MarkingGeometry?> geoOf,
        Func<MarkingDefinition, Polyline2?> pathOf, params MarkingDefinition[] extra) => new()
    {
        Catalog = Cat, ViewScale = view, AllDefinitions = () => defs.Concat(extra).ToList(), GeometryOf = geoOf, PathOf = pathOf,
        Lookup = id => extra.FirstOrDefault(x => x.Id == id) ?? defs.FirstOrDefault(x => x.Id == id),
    };

    // ================================================================== 1 – corte sem desenho duplicado

    [Fact]
    public void F1_CorteTransversal_NadaNaPlantaSoOPerfil()
    {
        // Antes: o modo "só o corte" gravava na planta, sobre a via, a linha de corte vermelha, o EIXO, as marcas A–A e o
        // título "SEÇÃO A–A (cotas em metros...)" – um desenho de corte antes do perfil. Agora a cota só guarda a linha de corte
        // (dados do perfil) e o único desenho é o perfil final.
        var (defs, geoOf, pathOf) = DetailGenerator.SectionSample();
        var cut = Cut(chain: false, profile: true);
        var ctx = Ctx(defs, geoOf, pathOf, cut);
        var plan = DetailGenerator.SectionDimensions(cut, ctx);
        Assert.Empty(plan.Annotations);
        Assert.Empty(plan.Pieces);
        Assert.True(DetailGenerator.SectionDataOnly(cut));
        // O perfil continua inteiro, lido da cota guardada.
        var profile = DetailGenerator.SectionProfileDrawing(SectionProfileDefinition.From(cut, Vec2.Zero), ctx);
        Assert.NotEmpty(profile.Pieces);
        Assert.Contains(profile.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith("SEÇÃO TRANSVERSAL A–A"));
        Assert.Contains(profile.Annotations.OfType<AnnotationText>(), t => Regex.IsMatch(t.Text, @"^\d+,\d+$"));
    }

    [Fact]
    public void F1_CotarSecao_PlantaContinuaComCortaEMarcas()
    {
        // Os outros modos não mudam: com cotas na planta (com ou sem perfil) e só cotas, a linha de corte e as marcas A–A ficam.
        var (defs, geoOf, pathOf) = DetailGenerator.SectionSample();
        foreach (var (chain, profile) in new[] { (true, true), (true, false), (false, false) })
        {
            var cut = Cut(chain, profile);
            var plan = DetailGenerator.SectionDimensions(cut, Ctx(defs, geoOf, pathOf, cut));
            Assert.False(DetailGenerator.SectionDataOnly(cut));
            Assert.Contains(plan.Annotations.OfType<AnnotationLine>(), l => l.Color == MarkingColor.Vermelha);
            Assert.Contains(plan.Annotations.OfType<AnnotationText>(), t => t.Text == "A");
        }
        // Seção antiga com o perfil embutido (posição gravada na cota) continua desenhando o perfil – não vira só dados.
        var legacy = Cut(chain: false, profile: true);
        legacy.ProfilePosition = new Vec2(0, -11);
        Assert.False(DetailGenerator.SectionDataOnly(legacy));
        Assert.NotEmpty(DetailGenerator.SectionDimensions(legacy, Ctx(defs, geoOf, pathOf, legacy)).Pieces);
    }

    // ================================================================== 2 – layout do perfil (referências A e B)

    private static readonly Vec2 Pos = new(0, -11);

    /// <summary>Perfil da via de exemplo (calçadas, meios-fios, sarjetas, ciclofaixa, duas faixas) – desenho atual.</summary>
    private static (MarkingGeometry Geo, BuildContext Ctx, List<AnnotationText> Texts, double LegendLeft) Perfil(double view = 100, double scale = 50,
        Action<SectionProfileDefinition>? edit = null)
    {
        var (defs, geoOf, pathOf) = DetailGenerator.SectionSample();
        var cut = Cut(chain: false, profile: true);
        cut.ProfileScale = scale;
        cut.DesenhoVersao = SectionDimensionDefinition.DesenhoAtual;
        var ctx = Ctx(view, defs, geoOf, pathOf, cut);
        var pd = SectionProfileDefinition.From(cut, Pos);
        edit?.Invoke(pd);
        var geo = DetailGenerator.SectionProfileDrawing(pd, ctx);
        var texts = geo.Annotations.OfType<AnnotationText>().ToList();
        // A tabela de elementos (≥ 60 mm de largura, título centrado) e o detalhe abaixo dela ficam à direita do perfil.
        var legend = texts.FirstOrDefault(t => t.Text == "ELEMENTOS DA SEÇÃO");
        return (geo, ctx, texts, legend != null ? legend.Position.X - ctx.Mm(30) : double.MaxValue);
    }

    private static double SidewalkTop(MarkingGeometry geo, double legendLeft) =>
        geo.Pieces.Where(p => p.Color == MarkingColor.Concreto && p.Shape.Bounds.Max.X < legendLeft).Max(p => p.Shape.Bounds.Max.Y);

    [Fact]
    public void F2_CotaTotalNoTopo_CadeiaPorElementoAcimaDoPerfil()
    {
        // Ref. A: a cota total (faixa de domínio) no topo e, logo abaixo, a cadeia de cotas por elemento – acima do desenho.
        // Antes: cadeia e total embaixo do perfil.
        var (geo, ctx, texts, legendLeft) = Perfil();
        var top = SidewalkTop(geo, legendLeft);
        var total = Assert.Single(texts, t => t.Text == "16,90");
        foreach (var v in new[] { "2,45", "0,55", "4,85", "3,20", "1,60" })
        {
            var chain = texts.Where(t => t.Text == v && t.Position.X < legendLeft).ToList();
            Assert.NotEmpty(chain);
            Assert.All(chain, t => Assert.True(t.Position.Y - ctx.Mm(2.0) > top + ctx.Mm(3), $"{v} não está acima do perfil"));
            Assert.All(chain, t => Assert.True(total.Position.Y > t.Position.Y + ctx.Mm(2.0), "a cota total não está acima da cadeia"));
        }
    }

    [Fact]
    public void F2_EixoComSimboloERotulo_AtravessandoOPerfil()
    {
        // Ref. A: eixo (CL) em traço-ponto atravessando o desenho, com o símbolo e o rótulo no topo. Antes: só o texto "EIXO".
        var (geo, ctx, texts, legendLeft) = Perfil();
        var eixo = Assert.Single(texts, t => t.Text == "EIXO");
        var total = Assert.Single(texts, t => t.Text == "16,90");
        Assert.True(eixo.Position.Y > total.Position.Y, "o rótulo do eixo fica acima da cota total");
        // Símbolo ℄: arco (C) com muitos pontos logo à esquerda do rótulo.
        Assert.Contains(geo.Annotations.OfType<AnnotationLine>(), l => l.Points.Count >= 12 &&
            l.Points.All(p => p.X < eixo.Position.X && p.X > eixo.Position.X - ctx.Mm(4) && Math.Abs(p.Y - eixo.Position.Y) < ctx.Mm(4)));
        // Traço-ponto vertical no eixo, do terreno até acima da cadeia de cotas.
        var axisX = total.Position.X;                // corte simétrico: o eixo fica no meio da cota total
        var dashes = geo.Annotations.OfType<AnnotationLine>().Where(l => l.Points.Count == 2 && l.Points.All(p => Math.Abs(p.X - axisX) < 1e-6)).ToList();
        Assert.True(dashes.Count >= 10);
        var asphalt = geo.Pieces.Where(p => p.Color == MarkingColor.Asfalto && p.Shape.Bounds.Max.X < legendLeft).Min(p => p.Shape.Bounds.Min.Y);
        Assert.True(dashes.Min(l => l.Points.Min(p => p.Y)) < asphalt);
        Assert.True(dashes.Max(l => l.Points.Max(p => p.Y)) > texts.Single(t => t.Text == "4,85").Position.Y);
    }

    [Fact]
    public void F2_CaimentoComSetaSobreASuperficie_NaPistaENasCalcadas()
    {
        // Ref. A: seta de caimento junto à superfície, paralela a ela, com o %, na pista e nas calçadas. Antes: setas horizontais
        // soltas acima dos níveis.
        var (geo, ctx, texts, legendLeft) = Perfil();
        var slopes = texts.Where(t => t.Text == "i = 2,0 %" && t.Position.X < legendLeft).ToList();
        var curbXs = geo.Pieces.Where(p => p.Hatch == Hachura.DiagonalDensa && p.Shape.Outer.Count == 5 && p.Shape.Bounds.Max.X < legendLeft)
            .Select(p => p.Shape.Bounds.Min.X).OrderBy(x => x).ToList();
        Assert.Equal(2, curbXs.Count);
        Assert.True(slopes.Count(t => t.Position.X > curbXs[0] && t.Position.X < curbXs[1]) >= 2, "caimento na pista");
        Assert.True(slopes.Count(t => t.Position.X < curbXs[0] || t.Position.X > curbXs[1]) >= 2, "caimento nas calçadas");
        foreach (var t in slopes)
        {
            var arrow = geo.Annotations.OfType<AnnotationLine>().Where(l => l.Points.Count == 2)
                .Select(l => (A: l.Points[0], B: l.Points[1]))
                .Where(l => Math.Abs((l.A.X + l.B.X) / 2 - t.Position.X) < ctx.Mm(1) && t.Position.Y - (l.A.Y + l.B.Y) / 2 > 0 && t.Position.Y - (l.A.Y + l.B.Y) / 2 < ctx.Mm(4.0))
                .OrderBy(l => t.Position.Y - (l.A.Y + l.B.Y) / 2).First();
            // Paralela à superfície: inclinação de 2 % no desenho (sem exagero vertical).
            Assert.Equal(0.02, Math.Abs((arrow.B.Y - arrow.A.Y) / (arrow.B.X - arrow.A.X)), 3);
        }
    }

    [Fact]
    public void F2_CamadasDoPavimento_HachurasDistintasEChamadas()
    {
        // Ref. A: camadas do pavimento com hachuras distintas e chamadas (linha + texto) nomeando cada uma. Antes: só o revestimento.
        var (geo, ctx, texts, legendLeft) = Perfil();
        var main = geo.Pieces.Where(p => p.Shape.Bounds.Max.X < legendLeft).ToList();
        var hatches = new[] { Hachura.Pontos, Hachura.Brita, Hachura.Solo, Hachura.Terra };
        foreach (var h in hatches) Assert.Contains(main, p => p.Hatch == h);
        var calls = new (string Text, Hachura Hatch)[]
        {
            ("Revestimento asfáltico (CBUQ) – e = 5 cm", Hachura.Pontos), ("Base de brita graduada simples (BGS) – e = 15 cm", Hachura.Brita),
            ("Sub-base de solo-brita – e = 15 cm", Hachura.Solo), ("Subleito compactado", Hachura.Terra),
        };
        foreach (var (text, hatch) in calls)
        {
            var t = Assert.Single(texts, x => x.Text == text);
            // Linha de chamada: termina junto ao texto e começa dentro da camada (com a hachura dela).
            var leader = geo.Annotations.OfType<AnnotationLine>().Single(l => l.Points.Count == 3 &&
                Math.Abs(l.Points[^1].X - (t.Position.X - ctx.Mm(1))) < 1e-6 && Math.Abs(l.Points[^1].Y - (t.Position.Y - ctx.Mm(2.0 * 0.8 * 0.55))) < 1e-6);
            Assert.Contains(main, p => p.Hatch == hatch && p.Shape.Contains(leader.Points[0]));
        }
        // Camadas escolhidas na janela (nome, espessura e hachura).
        var (g2, _, t2, l2) = Perfil(edit: pd => pd.Layers = new() { new SectionLayer { Name = "Macadame seco", ThicknessCm = 20, Hatch = Hachura.Cruzada } });
        Assert.Contains(t2, x => x.Text == "Macadame seco – e = 20 cm");
        Assert.DoesNotContain(t2, x => x.Text.StartsWith("Base de brita"));
        var layer = g2.Pieces.Single(p => p.Hatch == Hachura.Cruzada && p.Shape.Bounds.Max.X < l2 && p.Rgb != null && p.Color == MarkingColor.Brita);
        // Espessura na vertical (topo e fundo têm os mesmos x): 20 cm a 1:50 numa vista 1:100.
        var thick = layer.Shape.Outer.GroupBy(v => Math.Round(v.X, 9)).Where(g => g.Count() == 2).Select(g => g.Max(v => v.Y) - g.Min(v => v.Y)).ToList();
        Assert.NotEmpty(thick);
        Assert.All(thick, d => Assert.Equal(0.20 * 2, d, 6));
    }

    [Fact]
    public void F2_DetalheMeioFioESarjeta_FormaRealCotasEmCmENotas()
    {
        // Ref. B: meio-fio e sarjeta com a forma real (espelho inclinado, parte enterrada, sarjeta com caimento), cotas em cm,
        // inclinações e notas, com título e escala. Antes: não havia detalhe.
        var (geo, ctx, texts, legendLeft) = Perfil();
        var right = texts.Select(t => t.Text).ToList();
        foreach (var x in new[] { "DETALHE – MEIO-FIO E SARJETA", "ESCALA 1:20 – COTAS EM CENTÍMETROS", "13", "15", "30", "3 %", "15 %",
                     "CONCRETO fck ≥ 20 MPa", "GREIDE DO PAVIMENTO", "PASSEIO OU TERRENO APILOADO" })
            Assert.Contains(x, right);
        // Meio-fio a 1:20 numa vista 1:100 (× 5): 30 cm de altura total, base 15, topo 13 (espelho a 15 %).
        var curb = Assert.Single(geo.Pieces, p => p.Hatch == Hachura.DiagonalDensa && p.Shape.Outer.Count == 5 && p.Shape.Bounds.Min.X > legendLeft);
        var (mn, mx) = curb.Shape.Bounds;
        Assert.Equal(0.30 * 5, mx.Y - mn.Y, 6);
        Assert.Equal(0.15 * 5, mx.X - mn.X, 6);
        Assert.Equal((0.15 - 0.15 * 0.15) * 5, curb.Shape.Outer.Where(v => Math.Abs(v.Y - mx.Y) < 1e-9).Max(v => v.X) -
            curb.Shape.Outer.Where(v => Math.Abs(v.Y - mx.Y) < 1e-9).Min(v => v.X), 6);
        // Sarjeta com caimento para o meio-fio (topo inclinado, 4 vértices).
        Assert.Contains(geo.Pieces, p => p.Hatch == Hachura.DiagonalDensa && p.Shape.Outer.Count == 4 && p.Shape.Bounds.Min.X > legendLeft &&
            p.Shape.Outer.Select(v => v.Y).Distinct().Count() == 3);
        // Medidas escolhidas: enterrada 20 cm (total 35), espelho a 10 %, outra nota; sem detalhe.
        var (_, _, t2, _) = Perfil(edit: pd => pd.CurbGutter = new CurbGutterSpec { BuriedCm = 20, FaceSlopePct = 10, Concrete = "CONCRETO fck ≥ 25 MPa" });
        var r2 = t2.Select(t => t.Text).ToList();
        foreach (var x in new[] { "35", "20", "13,5", "10 %", "CONCRETO fck ≥ 25 MPa" }) Assert.Contains(x, r2);
        var (_, _, t3, _) = Perfil(edit: pd => pd.CurbGutter = new CurbGutterSpec { Detail = false });
        Assert.DoesNotContain(t3, t => t.Text.StartsWith("DETALHE"));
    }

    [Fact]
    public void F2_TerrenoLateralHachurado()
    {
        // Ref. A: terreno além do alinhamento, dos dois lados, hachurado. Antes: só uma linha de terreno sob o corte.
        var (geo, ctx, _, legendLeft) = Perfil();
        var walks = geo.Pieces.Where(p => p.Color == MarkingColor.Concreto && p.Shape.Bounds.Max.X < legendLeft).ToList();
        var x0 = walks.Min(p => p.Shape.Bounds.Min.X);
        var x1 = walks.Max(p => p.Shape.Bounds.Max.X);
        Assert.Contains(geo.Pieces, p => p.Hatch == Hachura.Terra && p.Shape.Bounds.Min.X < x0 - ctx.Mm(5) && p.Shape.Bounds.Max.X > x1 + ctx.Mm(5));
    }

    [Fact]
    public void F2_TituloSublinhadoComEscala_SemCortarOTexto()
    {
        // Ref. A: título sublinhado com a escala embaixo. Antes: o sublinhado passava pelos descendentes (Ç) do título.
        var (geo, ctx, texts, _) = Perfil();
        var title = Assert.Single(texts, t => t.Text == "SEÇÃO TRANSVERSAL A–A");
        var scale = Assert.Single(texts, t => t.Text == "ESCALA 1:50 – COTAS E NÍVEIS EM METROS");
        var bottom = title.Position.Y - ctx.Mm(title.PaperHeightMm * 1.3);       // pé dos descendentes
        var under = geo.Annotations.OfType<AnnotationLine>().Where(l => l.Points.Count == 2 && Math.Abs(l.Points[0].Y - l.Points[1].Y) < 1e-9 &&
            l.Points[0].Y < title.Position.Y && l.Points[0].Y > scale.Position.Y && Math.Abs((l.Points[0].X + l.Points[1].X) / 2 - title.Position.X) < 1e-6).ToList();
        Assert.NotEmpty(under);
        Assert.All(under, l => Assert.True(l.Points[0].Y < bottom, "o sublinhado corta o título"));
        Assert.All(under, l => Assert.Equal(DetailGenerator.TextWidth(title.Text, title.PaperHeightMm, ctx), Math.Abs(l.Points[1].X - l.Points[0].X), 6));
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(100, 20)]
    [InlineData(100, 50)]
    [InlineData(100, 100)]
    [InlineData(200, 100)]
    [InlineData(200, 200)]
    [InlineData(20, 200)]
    public void F2_TextosSemSobreposicao_LegiveisDe1a20a1a200(double view, double scale)
    {
        // Tudo em mm de papel × escala: nenhum texto sobre outro, do perfil a 1:20 até 1:200, com altura legível (≥ 1,5 mm).
        var (geo, ctx, texts, _) = Perfil(view, scale);
        Assert.All(texts, t => Assert.True(t.PaperHeightMm >= 1.5 - 1e-9, $"texto pequeno: {t.Text}"));
        var boxes = texts.Select(t =>
        {
            var w = DetailGenerator.TextWidth(t.Text, t.PaperHeightMm, ctx);
            var n = t.Text.Split('\n').Length;
            var x0 = t.Align switch { TextAlign.Left => t.Position.X, TextAlign.Right => t.Position.X - w, _ => t.Position.X - w / 2 };
            return (t.Text, X0: x0, X1: x0 + w, Y0: t.Position.Y - ctx.Mm(t.PaperHeightMm * (1.15 + 1.45 * (n - 1))), Y1: t.Position.Y);
        }).ToList();
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var (a, b) = (boxes[i], boxes[j]);
                var overlap = a.X1 > b.X0 + 1e-6 && b.X1 > a.X0 + 1e-6 && a.Y1 > b.Y0 + 1e-6 && b.Y1 > a.Y0 + 1e-6;
                Assert.False(overlap, $"\"{a.Text}\" sobre \"{b.Text}\" (vista 1:{view}, perfil 1:{scale})");
            }
    }

    [Fact]
    public void F2_PersonalizacoesEEscalaMantidas()
    {
        // Nomes e cores dos elementos continuam valendo no perfil e na legenda; o perfil fica no lugar (canto superior esquerdo)
        // em qualquer escala da vista.
        var (geo, _, texts, _) = Perfil(edit: pd => pd.ItemStyles = new() { new SectionItemStyle { Key = "Calçada", Name = "Passeio público", Rgb = "#D2B48C" } });
        Assert.DoesNotContain(texts, t => t.Text == "Calçada");
        Assert.True(texts.Count(t => t.Text == "Passeio público") >= 3);          // dois trechos + legenda
        Assert.Contains(geo.Pieces, p => p.Rgb == "#D2B48C" && p.Hatch == Hachura.Diagonal);
        foreach (var view in new[] { 50.0, 100, 200 })
        {
            var (g, ctx, t, _) = Perfil(view);
            var maxY = g.Annotations.OfType<AnnotationLine>().SelectMany(l => l.Points).Concat(t.Select(x => x.Position)).Max(p => p.Y);
            var minX = g.Annotations.OfType<AnnotationLine>().SelectMany(l => l.Points).Min(p => p.X);
            Assert.InRange(maxY, Pos.Y - ctx.Mm(6), Pos.Y);
            Assert.True(minX >= Pos.X - 1e-9);
        }
    }
}

/// <summary>Rodada LX (correção): o chanfro do lote se mantém em qualquer modo de divisão e é cotado no detalhamento.</summary>
public class LXChanfroTests
{
    private static BuildContext Ctx(LoteamentoDefinition lot, IEnumerable<QuadraDefinition> quadras)
    {
        var all = quadras.Cast<MarkingDefinition>().Append(lot).ToList();
        return new BuildContext { Catalog = CatalogService.LoadDefault(), ViewScale = 500, AllDefinitions = () => all, Lookup = id => all.FirstOrDefault(x => x.Id == id) };
    }

    [Theory]
    [InlineData(ModoDivisao.Testada)]
    [InlineData(ModoDivisao.Linhas)]
    public void Chanfro_MantemSeEmQualquerModoDeDivisao_ECotadoUmaVezNoDetalhe(ModoDivisao modo)
    {
        var (_, lot, _, quadras) = LVCorrecoesTests.Cruz();
        foreach (var q in quadras) { q.Chanfro = true; q.ChanfroFace = 3; q.Divisao = modo; }
        var ch = quadras.SelectMany(LotDivider.Chanfros).ToList();
        Assert.Equal(4, ch.Count);
        // O triângulo sai dos lotes da quina (área da quadra descontada em 4 chanfros) e a face é uma divisa do lote.
        foreach (var q in quadras)
        {
            var div = LotDivider.Divide(q, lot);
            var mine = LotDivider.Chanfros(q);
            Assert.Equal(mine.Count, div.Chanfros.Count);
            foreach (var c in mine)
            {
                Assert.DoesNotContain(div.Parcelas, p => PolygonOps.TotalArea(PolygonOps.Intersect(new[] { p.Forma }, new[] { c.Triangulo })) > 0.01);
                Assert.Contains(div.Parcelas, p => p.Forma.Outer.Any(v => v.DistanceTo(c.A) < 0.05) && p.Forma.Outer.Any(v => v.DistanceTo(c.B) < 0.05));
            }
        }
        // Detalhamento: cada chanfro cotado uma vez (3,00 m), com ou sem divisas.
        foreach (var divisas in new[] { false, true })
        {
            var det = new LoteamentoDetalheDefinition { LoteamentoId = lot.Id, Cotas = true, Divisas = divisas };
            var g = LotDrawing.Detalhe(det, Ctx(lot, quadras));
            var faces = g.Annotations.OfType<AnnotationText>().Count(t => t.Text == "3,00" && Math.Abs(t.Rotation) > 1e-9 || t.Text == "3,00");
            Assert.Equal(4, faces);
        }
        // Chanfro desligado: nada de cota nem de corte.
        foreach (var q in quadras) q.Chanfro = false;
        Assert.Empty(quadras.SelectMany(LotDivider.Chanfros));
    }
}
