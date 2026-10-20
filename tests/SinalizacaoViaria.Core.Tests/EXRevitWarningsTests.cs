using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// EX – Autoteste 2.1.0 de 2026-10-19 23:19 (docs/evidencias/2026-10-19-autoteste-rapido-2.1.0.txt), etapa do loteamento:
/// 62 avisos "ligeiramente fora da linha central" (pisos que não passam pelo alinhamento do EW2: lotes, áreas remanescentes,
/// acabamentos do terreno), 65 + 12 + 11 avisos de "Marca" duplicada (dois loteamentos no projeto) e 239 de pisos sobrepostos
/// sem dizer em que elementos (o relatório só detalha 25).
/// </summary>
public class EXRevitWarningsTests
{
    private static List<BoundarySegment> Segs(IReadOnlyList<Vec2> r) => r.Select((v, i) => new BoundarySegment(v, r[(i + 1) % r.Count])).ToList();

    [Fact]
    public void SnapRing_LoteEmCoordenadasGrandesComResiduo_FicaExato()
    {
        // Lote 12 × 30 na área do Autoteste (X ≈ 4970, Y ≈ 2210) com resíduo de 0,17 mm num vértice.
        var x0 = 4970.123456789;
        var y0 = 2210.987654321;
        var ring = new List<Vec2> { new(x0, y0), new(x0 + 12, y0 + 0.00017), new(x0 + 12, y0 + 30), new(x0 - 0.00012, y0 + 30) };
        Assert.Equal(2, AxisSnap.CountNearAxis(Segs(ring)));
        var snapped = AxisSnap.SnapRing(ring);
        Assert.Equal(4, snapped.Count);
        Assert.Equal(0, AxisSnap.CountNearAxis(Segs(snapped)));
        Assert.InRange(Math.Abs(Math.Abs(PolygonChecks.SignedArea(snapped)) - 360), 0, 0.01);
        Assert.All(snapped.Zip(ring), t => Assert.True(t.First.DistanceTo(t.Second) <= AxisSnap.MaxOffset + 1e-12));
    }

    [Fact]
    public void SnapRing_LoteInclinadoPeloProjeto_NaoMexe()
    {
        var a = 5 * Math.PI / 180;
        var u = new Vec2(Math.Cos(a), Math.Sin(a));
        var v = new Vec2(-Math.Sin(a), Math.Cos(a));
        var o = new Vec2(5000, 2200);
        var ring = new List<Vec2> { o, o + u * 12, o + u * 12 + v * 30, o + v * 30 };
        Assert.Equal(ring, AxisSnap.SnapRing(ring));
    }

    [Fact]
    public void SnapRing_PoucosPontos_DevolveComoVeio()
    {
        var two = new List<Vec2> { new(0, 0), new(10, 0.0001) };
        Assert.Equal(two, AxisSnap.SnapRing(two));
    }

    /// <summary>
    /// Lotes de verdade (quadras reconhecidas em volta do cruzamento local × local, divididas em 12 × 30) passados pelo mesmo
    /// preparo do LotService: o alinhamento não deforma nenhum lote (área e validade iguais) e nenhum fica com reta quase
    /// alinhada. Nesta cena, na origem, os lotes já saem exatos – o alinhamento não pode mexer neles.
    /// </summary>
    [Fact]
    public void Lotes_DoCruzamento_AlinhamentoNaoDeformaENaoDeixaRetaQuaseAlinhada()
    {
        var cat = CatalogService.LoadDefault();
        var scene = IntersectionDemo.Create(new IntersectionDefinition(), cat, 0, 0, 90, length: 110);
        var ctx = new BuildContext { Catalog = cat, Lookup = id => scene.Definitions.FirstOrDefault(x => x.Id == id), PathOf = x => scene.Paths.GetValueOrDefault(x.Id) };
        var ocupado = new List<Polygon2>();
        foreach (var def in scene.Definitions)
        {
            var path = scene.Paths.GetValueOrDefault(def.Id) ?? (def.Path is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points, pr.Closed) : null);
            try { ocupado.AddRange(MarkingBuilder.Build(def, path, ctx).Pieces.Where(p => !MarkingColors.IsPaint(p.Color)).Select(p => p.Shape)); }
            catch { /* peça sem geometria em planta */ }
        }
        var gleba = new Polygon2(new[] { new Vec2(-100, -100), new Vec2(100, -100), new Vec2(100, 100), new Vec2(-100, 100) });
        var rec = Reconhecimento.Quadras(gleba, ocupado);
        Assert.True(rec.Quadras.Count >= 4);
        var lot = new LoteamentoDefinition { Gleba = gleba.Outer.ToList() };
        var lotes = 0;
        foreach (var r in rec.Quadras)
        {
            var q = Reconhecimento.NovaQuadra(r, lot, 12, 30);
            foreach (var p in LotDivider.Divide(q, lot).Parcelas)
            {
                lotes++;
                var ring = (p.Forma.Simplified(0.005, 1e-6) ?? p.Forma).Outer.ToList();
                var snapped = AxisSnap.SnapRing(ring);
                Assert.Equal(0, AxisSnap.CountNearAxis(Segs(snapped)));
                Assert.InRange(Math.Abs(PolygonChecks.SignedArea(snapped)) - Math.Abs(PolygonChecks.SignedArea(ring)), -1e-3, 1e-3);
                Assert.Null(PolygonChecks.Problem(snapped, 1e-7));
                if (AxisSnap.CountNearAxis(Segs(ring)) == 0) Assert.Equal(ring, snapped);
            }
        }
        Assert.True(lotes > 20);
    }

    // ------------------------------------------------------------------ Marca dos lotes

    private static LoteamentoDefinition Lot(string nome, string id) => new() { Nome = nome, Id = id };

    [Fact]
    public void Marca_LoteamentoUnico_ContinuaQuadraLote()
    {
        var a = Lot("Jardim", "23efbb00000000000000000000000000");
        var pre = LotMarks.Prefixo(a, new[] { a });
        Assert.Equal("", pre);
        Assert.Equal("B-3", LotMarks.Lote(pre, "B", "3"));
        Assert.Equal("AV-R", LotMarks.Remanescente(pre, "AV-R"));
    }

    [Fact]
    public void Marca_DoisLoteamentos_GanhaONome()
    {
        var a = Lot("Jardim", "23efbb00000000000000000000000000");
        var b = Lot(" Bosque ", "cf386500000000000000000000000000");
        Assert.Equal("Jardim B-3", LotMarks.Lote(LotMarks.Prefixo(a, new[] { a, b }), "B", "3"));
        Assert.Equal("Bosque AV-R2", LotMarks.Remanescente(LotMarks.Prefixo(b, new[] { a, b }), "AV-R2"));
        Assert.Equal("Loteamento", LotMarks.Prefixo(Lot("  ", "11"), new[] { a, Lot("  ", "11") }));
    }

    [Fact]
    public void Marca_Homonimos_GanhamOInicioDoId()
    {
        var a = Lot("Autoteste", "23efbb00000000000000000000000000");
        var b = Lot("autoteste", "cf386500000000000000000000000000");
        var c = Lot("Autoteste", "23ef0100000000000000000000000000");
        var todos = new[] { a, b, c };
        Assert.Equal("autoteste-cf38", LotMarks.Prefixo(b, todos)); // nome igual sem diferenciar maiúsculas; cada um mantém a grafia dele
        // a e c começam igual (23ef): o prefixo cresce até diferir.
        Assert.Equal("Autoteste-23efbb00", LotMarks.Prefixo(a, todos));
        Assert.Equal("Autoteste-23ef0100", LotMarks.Prefixo(c, todos));
    }

    /// <summary>
    /// Cenário do relatório: o loteamento do Autoteste anterior ficou no projeto (marcas "B-3", geradas quando era o único) e
    /// o Autoteste cria outro com o mesmo nome e as mesmas quadras. Nenhuma marca se repete.
    /// </summary>
    [Fact]
    public void Marca_AutotesteRepetidoNoMesmoProjeto_NenhumaMarcaRepetida()
    {
        var antigo = Lot("Autoteste", Guid.NewGuid().ToString("N"));
        var novo = Lot("Autoteste", Guid.NewGuid().ToString("N"));
        var quadras = new[] { "A", "B", "C", "D", "E", "F" };
        IEnumerable<string> Marcas(string pre) => quadras.SelectMany(q => Enumerable.Range(1, 12).Select(n => LotMarks.Lote(pre, q, n.ToString())))
            .Append(LotMarks.Remanescente(pre, "AV-R"));
        var sozinho = Marcas(LotMarks.Prefixo(antigo, new[] { antigo })).ToList();
        var depois = Marcas(LotMarks.Prefixo(novo, new[] { antigo, novo })).ToList();
        var refeito = Marcas(LotMarks.Prefixo(antigo, new[] { antigo, novo })).ToList();
        Assert.Empty(sozinho.Intersect(depois));
        Assert.Empty(refeito.Intersect(depois));
        Assert.Equal(sozinho.Count + depois.Count, sozinho.Concat(depois).Distinct().Count());
    }

    // ------------------------------------------------------------------ resumo dos avisos do Autoteste

    private static readonly string[] Report =
    {
        "Aviso do Revit: Pisos realçadas se sobrepõem. – PAV-ASF (Pisos) #2932239 [marca 74c332], PAV-ASF (Pisos) #2932845 [marca a0c16a]",
        "Aviso do Revit: Pisos realçadas se sobrepõem. – LFO-2 (Pisos) #2932246 [marca 7773e2], MER-0 (Pisos) #2932975 [marca df34aa]",
        "Aviso do Revit: Pisos realçadas se sobrepõem. – MER-0 (Pisos) #2932409 [marca ffbcfd], MER-0 (Pisos) #2932975 [marca df34aa]",
        "Aviso do Revit: Pisos realçadas se sobrepõem. – MER-0 (Pisos) #2932409 [marca ffbcfd], MER-0 (Pisos) #2932410 [marca ffbcfd] [mesma marca]",
        "Aviso do Revit: A linha no croqui está ligeiramente fora da linha central e pode causar imprecisões. – QUADRA-B (Pisos) #2925062 [marca 23efbb], <Croqui> #2917980",
        "Aviso do Revit: A linha no croqui está ligeiramente fora da linha central e pode causar imprecisões. – Pisos #2925069",
        "Lento: 153 s.",
    };

    [Fact]
    public void Resumo_ContaPorTextoEPorElemento()
    {
        var lines = WarningSummary.Lines(Report).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal("Σ 4 × \"Pisos realçadas se sobrepõem.\" (1 entre elementos da mesma marca) – em MER-0 3, PAV-ASF 1, LFO-2 1".Length, lines[0].Length);
        Assert.StartsWith("Σ 4 × \"Pisos realçadas se sobrepõem.\" (1 entre elementos da mesma marca) – em MER-0 3, ", lines[0]);
        Assert.Contains("PAV-ASF 1", lines[0]);
        Assert.Contains("LFO-2 1", lines[0]);
        Assert.Equal("Σ 2 × \"A linha no croqui está ligeiramente fora da linha central e pode causar imprecisões.\" – em Pisos 1, QUADRA-B 1", lines[1]);
    }

    [Fact]
    public void Resumo_CodigosDeUmAviso()
    {
        Assert.Equal(new[] { "PAV-ASF", "PAV-ASF" }, WarningSummary.Codes(Report[0]));
        Assert.Equal(new[] { "QUADRA-B" }, WarningSummary.Codes(Report[4]));
        Assert.Empty(WarningSummary.Codes("Aviso do Revit: sem elementos"));
        Assert.Equal("Pisos realçadas se sobrepõem.", WarningSummary.Text(Report[0]));
    }
}
