using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using Xunit;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada 6, item 25: anotações dimensionadas em milímetros de papel × escala da vista. Gerado em 1:100, 1:500 e 1:1000, cada
/// detalhe tem no papel os mesmos tamanhos (textos, símbolos, quadros, perfis), reescalado em torno do ponto de inserção.
/// </summary>
public class AF4PaperScaleTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly double[] Scales = { 100, 500, 1000 };
    private const double TolMm = 0.01;

    private sealed record PaperGeo(List<List<Vec2>> Pieces, List<List<Vec2>> Lines, List<(Vec2 P, string T, double H)> Texts);

    /// <summary>Geometria levada ao papel (mm) em relação ao ponto de inserção.</summary>
    private static PaperGeo Paper(MarkingGeometry g, Vec2 origin, double scale)
    {
        var k = 1000 / scale;
        Vec2 M(Vec2 p) => (p - origin) * k;
        return new PaperGeo(
            g.Pieces.Select(p => p.Shape.Outer.Select(M).ToList()).ToList(),
            g.Annotations.OfType<AnnotationLine>().Select(l => l.Points.Select(M).ToList()).ToList(),
            g.Annotations.OfType<AnnotationText>().Select(t => (M(t.Position), t.Text, t.PaperHeightMm)).ToList());
    }

    private static void Same(List<List<Vec2>> a, List<List<Vec2>> b, string what)
    {
        Assert.True(a.Count == b.Count, $"{what}: {a.Count} × {b.Count}");
        for (int i = 0; i < a.Count; i++)
        {
            Assert.True(a[i].Count == b[i].Count, $"{what} {i}: {a[i].Count} × {b[i].Count} vértices");
            for (int k = 0; k < a[i].Count; k++)
                Assert.True(a[i][k].DistanceTo(b[i][k]) < TolMm, $"{what} {i}.{k}: {a[i][k]} × {b[i][k]} mm");
        }
    }

    /// <summary>O detalhe inteiro é o mesmo no papel nas três escalas.</summary>
    private static void SamePaper(string name, Func<double, (MarkingGeometry Geo, Vec2 Origin)> build)
    {
        var (g0, o0) = build(Scales[0]);
        Assert.True(g0.Pieces.Count + g0.Annotations.Count > 0, $"{name}: nada gerado");
        Assert.DoesNotContain(g0.Warnings, w => w.StartsWith(DetailGenerator.MissingTarget));
        var p0 = Paper(g0, o0, Scales[0]);
        foreach (var s in Scales.Skip(1))
        {
            var (g, o) = build(s);
            var p = Paper(g, o, s);
            Same(p0.Pieces, p.Pieces, $"{name} 1:{s} peças");
            Same(p0.Lines, p.Lines, $"{name} 1:{s} linhas");
            Assert.True(p0.Texts.Count == p.Texts.Count, $"{name} 1:{s}: textos {p0.Texts.Count} × {p.Texts.Count}");
            for (int i = 0; i < p0.Texts.Count; i++)
            {
                Assert.Equal(p0.Texts[i].T, p.Texts[i].T);
                Assert.Equal(p0.Texts[i].H, p.Texts[i].H, 9);
                Assert.True(p0.Texts[i].P.DistanceTo(p.Texts[i].P) < TolMm, $"{name} 1:{s} texto \"{p.Texts[i].T}\": {p0.Texts[i].P} × {p.Texts[i].P} mm");
            }
        }
    }

    private static BuildContext Ctx(IReadOnlyList<MarkingDefinition> defs, double scale,
        Func<MarkingDefinition, MarkingGeometry?>? geo = null, Func<MarkingDefinition, Polyline2?>? path = null) => new()
    {
        Catalog = Cat, ViewScale = scale, Lookup = id => defs.FirstOrDefault(d => d.Id == id), AllDefinitions = () => defs,
        GeometryOf = geo, PathOf = path,
    };

    [Fact]
    public void Detalhe_de_placa_igual_no_papel()
    {
        var sign = new SignDefinition { Code = "R-19", Position = new Vec2(50, 20) };
        var det = new SignPlanDetailDefinition { SignId = sign.Id, SymbolMm = 12, OffsetMm = new Vec2(15, 20), Number = "P01", NumberBubble = true };
        var defs = new MarkingDefinition[] { sign, det };
        SamePaper("Detalhe de placa", s => (MarkingBuilder.Build(det, null, Ctx(defs, s)), sign.Position));
    }

    [Fact]
    public void Legenda_quadros_notas_e_norte_iguais_no_papel()
    {
        var defs = new List<MarkingDefinition>
        {
            new SignDefinition { Code = "R-1" }, new SignDefinition { Code = "A-18" }, new LinearMarkingDefinition { Code = "LFO-1" },
            new UrbanElementDefinition { Code = "BANCO", Position = Vec2.Zero },
        };
        var lg = new LegendDefinition { Position = new Vec2(120, 80) };
        var nt = new NotesDefinition { Position = new Vec2(-40, 10) };
        var na = new NorthArrowDefinition { Position = new Vec2(7, -3), AngleDeg = 20, Style = EstiloNorte.RosaDosVentos };
        var qt = new QuantityTableDefinition { Position = new Vec2(300, 0) };
        defs.AddRange(new MarkingDefinition[] { lg, nt, na, qt });
        SamePaper("Legenda", s => (MarkingBuilder.Build(lg, null, Ctx(defs, s)), lg.Position));
        SamePaper("Notas", s => (MarkingBuilder.Build(nt, null, Ctx(defs, s)), nt.Position));
        SamePaper("Norte", s => (MarkingBuilder.Build(na, null, Ctx(defs, s)), na.Position));
        SamePaper("Quadro de quantitativos", s =>
        {
            BuildContext? c = null;
            c = Ctx(defs, s, d => d is IAnnotationDefinition ? null : MarkingBuilder.Build(d, null, c!));
            return (MarkingBuilder.Build(qt, null, c), qt.Position);
        });
    }

    [Fact]
    public void Detalhe_tipico_igual_no_papel()
    {
        var line = new LinearMarkingDefinition { Code = "LFO-2", PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(60, 0) }, 0) };
        var td = new TypicalDetailDefinition { MarkingTargetId = line.Id, Position = new Vec2(10, -30), DetailScale = 25 };
        var defs = new MarkingDefinition[] { line, td };
        SamePaper("Detalhe típico", s => (MarkingBuilder.Build(td, null, Ctx(defs, s, path: d => new Polyline2(d.Path!.Points))), td.Position));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Perfil_transversal_igual_no_papel(int version)
    {
        var (defs, geo, path) = DetailGenerator.SectionSample();
        var sd = new SectionDimensionDefinition { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45), DesenhoVersao = version };
        var pd = SectionProfileDefinition.From(sd, new Vec2(-20, -14));
        var all = defs.Append(sd).Append(pd).ToList();
        SamePaper($"Perfil v{version}", s => (MarkingBuilder.Build(pd, null, Ctx(all, s, geo, path)), pd.Position));
    }

    [Fact]
    public void Cotas_da_secao_e_chamadas_com_textos_e_simbolos_iguais_no_papel()
    {
        // Ficam presas ao modelo (linha de corte, ponto da marca): o que tem de ser igual no papel são os textos e os símbolos.
        var (defs, geo, path) = DetailGenerator.SectionSample();
        var sd = new SectionDimensionDefinition { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45) };
        var line = new LinearMarkingDefinition { Code = "LFO-1" };
        var lb = new LabelDefinition { MarkingTargetId = line.Id, Anchor = new Vec2(0, 0), LabelPosition = new Vec2(5, 3), TextMm = 2.5 };
        var all = defs.Append(sd).Append(line).Append(lb).ToList();
        foreach (var (name, build) in new (string, Func<double, MarkingGeometry>)[]
                 {
                     ("Cota de seção", s => MarkingBuilder.Build(sd, null, Ctx(all, s, geo, path))),
                     ("Anotação", s => MarkingBuilder.Build(lb, null, Ctx(all, s))),
                 })
        {
            var g0 = build(100);
            foreach (var s in Scales.Skip(1))
            {
                var g = build(s);
                // Cada texto com a mesma altura no papel (nomes que não cabem no trecho são omitidos nas escalas menores).
                var h0 = g0.Annotations.OfType<AnnotationText>().GroupBy(t => t.Text).ToDictionary(x => x.Key, x => x.Select(t => t.PaperHeightMm).Distinct().ToList());
                var t1 = g.Annotations.OfType<AnnotationText>().ToList();
                Assert.NotEmpty(t1);
                Assert.All(t1, t => Assert.True(h0.TryGetValue(t.Text, out var hs) && hs.Contains(t.PaperHeightMm), $"{name} 1:{s}: \"{t.Text}\" {t.PaperHeightMm} mm"));
                // Símbolos cheios (setas do corte, terminais): mesma largura e altura no papel.
                var s0 = g0.Pieces.Select(p => Size(p.Shape, 100)).OrderBy(x => x.X).ThenBy(x => x.Y).ToList();
                var s1 = g.Pieces.Select(p => Size(p.Shape, s)).OrderBy(x => x.X).ThenBy(x => x.Y).ToList();
                Assert.True(s0.Count == s1.Count, $"{name} 1:{s}: {s0.Count} × {s1.Count} símbolos");
                for (int i = 0; i < s0.Count; i++) Assert.True(s0[i].DistanceTo(s1[i]) < TolMm, $"{name} 1:{s} símbolo {i}: {s0[i]} × {s1[i]} mm");
            }
        }
    }

    private static Vec2 Size(Polygon2 p, double scale)
    {
        var (mn, mx) = p.Bounds;
        return (mx - mn) * (1000 / scale);
    }

    [Fact]
    public void Texto_de_2_5_mm_no_papel_vira_metros_pela_escala()
    {
        foreach (var s in Scales)
        {
            var ctx = new BuildContext { Catalog = Cat, ViewScale = s };
            Assert.Equal(2.5 * s / 1000, ctx.Mm(2.5), 12);
        }
    }

    [Fact]
    public void Mudar_a_escala_da_vista_refaz_so_os_detalhes_dela()
    {
        static T In<T>(T d, string view) where T : MarkingDefinition
        {
            d.Output.Mode = OutputMode.Detalhe2D;
            d.Output.ViewId = view;
            return d;
        }
        var sign = In(new SignDefinition { Code = "R-1" }, "planta-1");          // marca 2D: tamanho real, não muda
        var det = In(new SignPlanDetailDefinition { SignId = sign.Id }, "planta-1");
        var lg = In(new LegendDefinition(), "planta-1");
        var sd = In(new SectionDimensionDefinition(), "planta-1");
        var pd = In(SectionProfileDefinition.From(sd, Vec2.Zero), "planta-1");
        var other = In(new NotesDefinition(), "planta-2");
        var defs = new MarkingDefinition[] { pd, lg, sign, det, sd, other };
        var list = AnnotationScale.ToRescale(defs, "planta-1");
        Assert.Equal(new[] { det.Id, lg.Id, sd.Id, pd.Id }, list.Select(d => d.Id).ToArray());
        Assert.Empty(AnnotationScale.ToRescale(defs, ""));
        Assert.Single(AnnotationScale.ToRescale(defs, "planta-2"));
    }
}
