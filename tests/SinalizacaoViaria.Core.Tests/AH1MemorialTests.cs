using System.IO.Compression;
using System.Xml.Linq;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Core.Reports;
using Xunit;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada 8, item 22: memorial descritivo .docx gerado do modelo (ZipArchive + WordprocessingML). O arquivo abre como zip
/// válido com as partes do Word, tem todas as seções e os campos em branco, e as quantidades batem com o quantitativo.
/// </summary>
public class AH1MemorialTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly System.Globalization.CultureInfo Pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Avenida × via local com interseção, placas, rampa, boca de lobo – e o quadro de quantidades delas.</summary>
    private static (Y34TipTests.World W, List<QuantityRow> Rows) Project()
    {
        var w = Y34TipTests.Make(null,
            (2, new[] { new Vec2(-120, 0), new Vec2(120, 0) }),
            (0, new[] { new Vec2(0, -90), new Vec2(0, 90) }));
        w.Defs.AddRange(new MarkingDefinition[]
        {
            new SignDefinition { Code = "R-1", Position = new Vec2(8, -12) },
            new SignDefinition { Code = "R-1", Position = new Vec2(-8, 12) },
            new SignDefinition { Code = "A-18", Position = new Vec2(60, 14) },
            new RampDefinition { PathRef = PathReference.FromPoints(new[] { new Vec2(40, 7.0), new Vec2(42, 7.0) }, 0) },
            new DrainageDefinition { Position = new Vec2(70, 7.0) },
        });
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => w.Defs.FirstOrDefault(x => x.Id == id), PathOf = x => w.Paths.GetValueOrDefault(x.Id) };
        var items = new List<(MarkingDefinition, MarkingGeometry)>();
        foreach (var d in w.Defs)
        {
            var path = w.Paths.GetValueOrDefault(d.Id);
            if (path == null && (d.Path ?? (d as HatchMarkingDefinition)?.Boundary) is { Points.Count: >= 2 } pr) path = new Polyline2(pr.Points, pr.Closed);
            items.Add((d, MarkingBuilder.Build(d, path, ctx)));
        }
        return (w, QuantityCalculator.Compute(items, Cat));
    }

    private static (ZipArchive Zip, XDocument Doc) Open(byte[] bytes)
    {
        var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var s = zip.GetEntry("word/document.xml")!.Open();
        return (zip, XDocument.Load(s));
    }

    private static string Text(XElement e) => string.Concat(e.Descendants(Wn + "t").Select(t => t.Value));

    private static List<List<string>> TableRows(XDocument doc) =>
        doc.Descendants(Wn + "tr").Select(tr => tr.Elements(Wn + "tc").Select(Text).ToList()).ToList();

    [Fact]
    public void Docx_abre_como_zip_valido_com_as_partes_do_word()
    {
        var (w, rows) = Project();
        var bytes = DocxWriter.Write(Memorial.Build(new MemorialInput { Definitions = w.Defs, Rows = rows, PathOf = d => w.Paths.GetValueOrDefault(d.Id) }));
        Assert.True(bytes.Length > 2000);
        Assert.Equal((byte)'P', bytes[0]);   // assinatura PK do zip
        Assert.Equal((byte)'K', bytes[1]);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        foreach (var part in DocxWriter.Parts)
        {
            var e = zip.GetEntry(part);
            Assert.True(e != null, $"parte {part} ausente");
            using var s = e!.Open();
            XDocument.Load(s);   // XML bem formado
        }
        using var ct = zip.GetEntry("[Content_Types].xml")!.Open();
        var types = XDocument.Load(ct).ToString();
        Assert.Contains("wordprocessingml.document.main+xml", types);
        using var rel = zip.GetEntry("_rels/.rels")!.Open();
        Assert.Contains("word/document.xml", XDocument.Load(rel).ToString());
    }

    [Fact]
    public void Memorial_tem_todas_as_secoes_e_os_campos_em_branco()
    {
        var (w, rows) = Project();
        var memo = Memorial.Build(new MemorialInput { Definitions = w.Defs, Rows = rows, PathOf = d => w.Paths.GetValueOrDefault(d.Id) });
        var (zip, doc) = Open(DocxWriter.Write(memo));
        using var _ = zip;
        var headings = doc.Descendants(Wn + "p")
            .Where(p => p.Element(Wn + "pPr")?.Element(Wn + "pStyle")?.Attribute(Wn + "val")?.Value == "Heading1").Select(Text).ToList();
        Assert.Equal(Memorial.AllSections, headings);
        // Campos em branco (controles de conteúdo) para nome do projeto, responsável técnico e ART.
        var tags = doc.Descendants(Wn + "sdt").Select(s => s.Descendants(Wn + "tag").First().Attribute(Wn + "val")!.Value).ToList();
        foreach (var (_, tag) in Memorial.Fields) Assert.Contains(tag, tags);
        Assert.All(doc.Descendants(Wn + "sdt"), s =>
        {
            Assert.NotNull(s.Descendants(Wn + "showingPlcHdr").FirstOrDefault());
            Assert.Equal("clique para preencher", Text(s.Element(Wn + "sdtContent")!));
        });
        var all = Text(doc.Root!);
        // Vias, interseção, acessibilidade, drenagem e normas descritas a partir do modelo.
        Assert.Contains("Via arterial", all);
        Assert.Contains("Via local", all);
        Assert.Contains("PARE (R-1) nas vias secundárias", all);
        Assert.Contains("I (sem refúgio)", all);
        var walkRamps = w.Defs.OfType<RampDefinition>().Count(r => r.Type != TipoRampa.AcessoVeiculos);   // a avulsa + as da interseção
        Assert.True(walkRamps > 1);
        Assert.Contains($"{walkRamps} rebaixamento(s) de calçada", all);
        Assert.Contains("ABNT NBR 9050", all);
        Assert.Contains("ABNT NBR 16537", all);
        Assert.Contains("[a confirmar]", all);
        var table = TableRows(doc);
        foreach (var r in w.Roads)
        {
            var len = w.Paths.GetValueOrDefault(r.Def.Id)!.Length.ToString("N2", Pt);
            Assert.Contains(table, row => row.Count == 7 && row[1] == Hierarquia.Label(r.Def.Hierarchy) && row[4] == len);
        }
    }

    [Fact]
    public void Quantidades_do_memorial_batem_com_o_quantitativo()
    {
        var (w, rows) = Project();
        var (zip, doc) = Open(DocxWriter.Write(Memorial.Build(new MemorialInput { Definitions = w.Defs, Rows = rows, PathOf = d => w.Paths.GetValueOrDefault(d.Id) })));
        using var _ = zip;
        var table = TableRows(doc);

        // Sinalização vertical: unidades por código (somadas as hierarquias), conferidas uma a uma e no total.
        var signs = rows.Where(r => r.Category == CategoriaQuantitativo.SinalizacaoVertical).GroupBy(r => r.Code)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Units));
        Assert.True(signs["R-1"] >= 2);   // as 2 avulsas + as da interseção com PARE
        Assert.Equal(1, signs["A-18"]);
        foreach (var (code, units) in signs)
            Assert.Contains(table, row => row.Count == 3 && row[0] == code && row[2] == units.ToString(Pt));
        Assert.Contains(table, row => row.Count == 3 && row[0] == "Total" && row[2] == signs.Values.Sum().ToString(Pt));

        // Sinalização horizontal: área e extensão por código, cor e material.
        var hor = rows.Where(r => r.Category == CategoriaQuantitativo.SinalizacaoHorizontal).GroupBy(r => (r.Code, r.Color, r.Material)).ToList();
        Assert.NotEmpty(hor);
        foreach (var g in hor)
        {
            var area = g.Sum(r => r.Area).ToString("N2", Pt);
            var len = g.Sum(r => r.PaintedLength).ToString("N2", Pt);
            Assert.True(table.Any(row => row.Count == 8 && row[0] == g.Key.Code && row[2] == g.First().ColorLabel && row[3] == g.Key.Material
                                         && row[4] == area && row[5] == len), $"{g.Key.Code} {g.Key.Color}: {area} m², {len} m");
        }
        Assert.Contains(table, row => row.Count == 8 && row[0] == "Total" && row[4] == hor.Sum(g => g.Sum(r => r.Area)).ToString("N2", Pt));

        // Acessibilidade, drenagem e resumo por categoria vêm do mesmo quadro.
        foreach (var c in new[] { CategoriaQuantitativo.Acessibilidade, CategoriaQuantitativo.Drenagem })
            foreach (var g in rows.Where(r => r.Category == c).GroupBy(r => r.Code))
                Assert.Contains(table, row => row[0] == g.Key && row[2] == g.Sum(r => r.MainQuantity).ToString("N2", Pt));
        foreach (var s in QuantityCalculator.CategorySummary(rows))
            Assert.Contains(table, row => row.Count == 5 && row[0] == s.Name && row[1] == s.Area.ToString("N2", Pt) && row[3] == s.Units.ToString(Pt));
    }
}
