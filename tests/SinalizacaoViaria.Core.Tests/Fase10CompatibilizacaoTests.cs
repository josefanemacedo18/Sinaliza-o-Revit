using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Fase X – compatibilização (Prompt Master §10, §16; RT-1.3): pares de pisos sobrepostos classificados em camada
/// intencional, interseção legítima, duplicação acidental, conflito real e inconclusivo, pelo que foi medido. Nada é apagado:
/// a classe dá o motivo e a ação recomendada.
/// </summary>
public class Fase10CompatibilizacaoTests(ITestOutputHelper output)
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static FatosSobreposicao Fatos(string a, string b, double? v, double? va = 1, double? vb = 1, string? ma = "m1", string? mb = "m2",
        string? ga = "g1", string? gb = "g2", bool pa = false, bool pb = false, bool? empilhados = null, bool? viasComNo = null) =>
        new(a, b, ma, mb, ga, gb, v, va, vb, pa, pb, empilhados, viasComNo);

    private static ClasseSobreposicao Classe(FatosSobreposicao f) => OverlapClassifier.Classificar(f).Classe;

    [Fact]
    public void SemVolume_EmpilhadosSaoCamada_OsDemaisInconclusivos()
    {
        Assert.Equal(ClasseSobreposicao.CamadaIntencional, Classe(Fatos("LFO-1", "PAV-ASF", 0, pa: true, empilhados: true)));
        // Abaixo do limite do Autoteste (1e-4 pé³ ≈ 2,8 cm³) também é "sem volume".
        Assert.Equal(ClasseSobreposicao.CamadaIntencional, Classe(Fatos("LFO-1", "PAV-ASF", 2e-6, pa: true, empilhados: true)));
        var c = OverlapClassifier.Classificar(Fatos("CALCADA", "MEIO-FIO", 0));
        Assert.Equal(ClasseSobreposicao.Inconclusivo, c.Classe);
        Assert.Contains("aviso de passagem", c.Motivo);
        Assert.Equal(ClasseSobreposicao.Inconclusivo, Classe(Fatos("CALCADA", "MEIO-FIO", null)));
    }

    [Fact]
    public void MesmoCodigoNoMesmoLugar_Duplicacao_ComAcaoPorOrigem()
    {
        // A mesma marca gerou duas peças iguais: defeito do gerador.
        var mesma = OverlapClassifier.Classificar(Fatos("CALCADA", "CALCADA", 0.30, 0.30, 0.30, ma: "m1", mb: "m1", gb: "g1"));
        Assert.Equal(ClasseSobreposicao.Duplicacao, mesma.Classe);
        Assert.Contains("gerador", mesma.Acao);
        // Duas marcas iguais (ferramenta aplicada duas vezes): uma pessoa apaga uma delas.
        var duas = OverlapClassifier.Classificar(Fatos("CALCADA", "CALCADA", 0.29, 0.30, 0.30));
        Assert.Equal(ClasseSobreposicao.Duplicacao, duas.Classe);
        Assert.Contains("não apaga sozinho", duas.Acao);
        // Pisos do usuário (código = nome do tipo) também: duplicação não precisa saber a intenção.
        Assert.Equal(ClasseSobreposicao.Duplicacao, Classe(Fatos("Piso 10 cm", "Piso 10 cm", 0.5, 0.5, 0.5, ma: null, mb: null)));
        // Mesmo código, mas um piso bem maior que o outro: não é duplicação.
        Assert.Equal(ClasseSobreposicao.Conflito, Classe(Fatos("CALCADA", "CALCADA", 0.30, 0.30, 3.0)));
    }

    [Fact]
    public void PisoQueNaoEDoPlugin_Inconclusivo()
    {
        var c = OverlapClassifier.Classificar(Fatos("Piso 10 cm", "PAV-ASF", 0.2, ma: null));
        Assert.Equal(ClasseSobreposicao.Inconclusivo, c.Classe);
        Assert.Equal("revisão humana", c.Acao);
    }

    [Fact]
    public void DuasViasQueSeCruzam_LegitimaSemNo_ConflitoComNo()
    {
        Assert.Equal(ClasseSobreposicao.InterseccaoLegitima, Classe(Fatos("PAV-ASF", "PAV-CON", 0.4, 10, 10, viasComNo: false)));
        // Sem nó, qualquer peça das duas vias (calçada sobre a pista da outra) é consequência do cruzamento.
        var calcada = OverlapClassifier.Classificar(Fatos("CALCADA", "PAV-ASF", 0.1, 5, 10, viasComNo: false));
        Assert.Equal(ClasseSobreposicao.InterseccaoLegitima, calcada.Classe);
        Assert.Contains("interseção", calcada.Acao);
        // Com nó, os pavimentos não deviam mais se cruzar: o recorte do nó falhou.
        Assert.Equal(ClasseSobreposicao.Conflito, Classe(Fatos("PAV-ASF", "PAV-CON", 0.4, 10, 10, viasComNo: true)));
        // Sem saber se há nó, não se afirma nada.
        Assert.Equal(ClasseSobreposicao.Inconclusivo, Classe(Fatos("PAV-ASF", "PAV-CON", 0.4, 10, 10)));
        // Peças da mesma via que se invadem não são cruzamento.
        Assert.Equal(ClasseSobreposicao.Conflito, Classe(Fatos("PAV-ASF", "PAV-ASF", 0.01, 10, 10, gb: "g1")));
    }

    [Fact]
    public void PisoInteiroDentroDeOutro_PinturaEscondidaEConflito_OutroTipoInconclusivo()
    {
        var escondida = OverlapClassifier.Classificar(Fatos("FTP-1", "ROTATORIA", 0.0030, 0.0030, 2.0, pa: true));
        Assert.Equal(ClasseSobreposicao.Conflito, escondida.Classe);
        Assert.Contains("escondida", escondida.Motivo);
        Assert.Equal(ClasseSobreposicao.Inconclusivo, Classe(Fatos("GRAMADO", "CALCADA", 0.20, 0.20, 2.0)));
        // Pintura que só entra em parte no outro piso: conflito comum.
        var parte = OverlapClassifier.Classificar(Fatos("FTP-1", "ROTATORIA", 0.0005, 0.0030, 2.0, pa: true));
        Assert.Equal(ClasseSobreposicao.Conflito, parte.Classe);
        Assert.Contains("marcas diferentes", parte.Motivo);
    }

    [Fact]
    public void Resumo_DaMaisGraveParaAMenosGrave_ComContagemEVolume()
    {
        var c = (ClasseSobreposicao k) => new Classificacao(k, "", "");
        var texto = OverlapClassifier.Resumo(new[]
        {
            (c(ClasseSobreposicao.CamadaIntencional), 0.0), (c(ClasseSobreposicao.Conflito), 0.012), (c(ClasseSobreposicao.Conflito), 0.003),
            (c(ClasseSobreposicao.InterseccaoLegitima), 0.4),
        });
        Assert.Equal("Por classe: conflito real 2 (0,015 m³); interseção legítima 1 (0,4 m³); camada intencional 1 (0 m³).", texto);
        Assert.Null(OverlapClassifier.Resumo(Array.Empty<(Classificacao, double)>()));
    }

    [Fact]
    public void Pecas_PinturaApoiadaNaoConta_PinturaEnterradaEDuplicacaoSim()
    {
        Polygon2 Ret(double x0, double y0, double x1, double y1) => new(new List<Vec2> { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) });
        var pecas = new List<PecaPiso>
        {
            new(Ret(0, 0, 20, 7), 0.10, 0, 0, "PAV-ASF", "pav", "via1", false),
            new(Ret(2, 3.4, 18, 3.6), 0.003, 0.10, 0, "LFO-1", "lfo", "via1", true),      // apoiada no topo do pavimento
            new(Ret(5, 1, 7, 2), 0.003, 0.05, 0, "LRE", "lre", "via1", true),            // enterrada no pavimento
            new(Ret(0, 7, 20, 9), 0.10, 0.05, 0, "CALCADA", "cal", "via1", false),
            new(Ret(0, 7, 20, 9), 0.10, 0.05, 0, "CALCADA", "cal2", "via1", false),       // a mesma calçada desenhada de novo
        };
        var res = OverlapClassifier.DasPecas(pecas);
        foreach (var (p, c) in res) output.WriteLine($"{pecas[p.A].Codigo} × {pecas[p.B].Codigo}: {OverlapClassifier.Nome(c.Classe)} – {c.Motivo}");
        Assert.Equal(2, res.Count);
        Assert.Contains(res, r => pecas[r.Par.A].Codigo == "PAV-ASF" && pecas[r.Par.B].Codigo == "LRE" && r.C.Classe == ClasseSobreposicao.Conflito
            && r.C.Motivo.Contains("escondida"));
        Assert.Contains(res, r => pecas[r.Par.A].Codigo == "CALCADA" && r.C.Classe == ClasseSobreposicao.Duplicacao);
    }

    // ------------------------------------------------------------------ com os geradores reais

    /// <summary>
    /// Peças planas que viram pisos no Revit (mesmo corte do <c>MarkingService</c>), com código, marca e grupo. Com
    /// <paramref name="ajustar"/>, o contorno de cada marca passa pelo ajuste conjunto do Revit (D-016, emulado como no
    /// <see cref="FCRevitFullRun4Tests"/>): é ele que chega ao modelo.
    /// </summary>
    private static List<PecaPiso> Pecas(IReadOnlyList<MarkingDefinition> defs, IReadOnlyDictionary<string, Polyline2> paths, bool ajustar = false)
    {
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => defs.FirstOrDefault(x => x.Id == id), PathOf = d => paths.GetValueOrDefault(d.Id), AllDefinitions = () => defs };
        var res = new List<PecaPiso>();
        foreach (var d in defs)
        {
            var path = paths.GetValueOrDefault(d.Id) ?? (d.Path is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points, pr.Closed) : null);
            var geo = MarkingBuilder.Build(d, path, ctx);
            var pieces = ElementPlan.RevitPieces(d, geo).Where(PieceOverlaps.IsFloorPiece).ToList();
            if (ajustar) pieces = FCRevitFullRun4Tests.Fitted(pieces, geo.ArcCenters);
            res.AddRange(pieces.Select(p => new PecaPiso(p.Shape, p.Thickness, p.Elevation, 0, d.DisplayCode, d.Id, d.GroupId, MarkingColors.IsPaint(p.Color))));
        }
        return res;
    }

    private static string Lista(List<PecaPiso> pecas, List<(PieceOverlap Par, Classificacao C)> res) =>
        string.Join("; ", res.GroupBy(r => $"{pecas[r.Par.A].Codigo} × {pecas[r.Par.B].Codigo} → {OverlapClassifier.Nome(r.C.Classe)}")
            .Select(g => $"{g.Key} {g.Count()} ({g.Sum(r => r.Par.Volume) * 1e6:0} cm³)"));

    /// <summary>
    /// Duas vias que se cruzam sem interseção: as sobreposições entre as peças das duas vias são interseção legítima (falta o
    /// nó), não conflito – e nenhuma é atribuída a uma via só. Com o nó gerado (mesmas vias) e o contorno ajustado como no
    /// Revit, nenhum par: nas peças cruas o classificador achava meio-fio × calçada da interseção (2 × 62 cm³ – um vértice
    /// a 5 mm da borda comum, simplificado num piso e não no outro), que o ajuste conjunto dos contornos (D-016) já desfaz.
    /// </summary>
    [Fact]
    public void ViasCruzadas_SemNoSaoInterseccaoLegitima_ComNoNaoSobraCruzamento()
    {
        var a = new[] { new Vec2(-60, 0), new Vec2(60, 0) };
        var b = new[] { new Vec2(0, -60), new Vec2(0, 60) };
        var defs = new List<MarkingDefinition>();
        var paths = new Dictionary<string, Polyline2>();
        foreach (var pts in new[] { a, b })
        {
            var pr = PathReference.FromPoints(pts, 0);
            var g = RoadTemplates.All[1].Create().Build(pr, new OutputSettings(), Cat);
            foreach (var m in g) if (m.Path != null) paths[m.Id] = new Polyline2(pts);
            defs.AddRange(g);
        }
        var pecas = Pecas(defs, paths);
        var nos = OverlapClassifier.NosEntreVias(defs);
        var res = OverlapClassifier.DasPecas(pecas, nos);
        output.WriteLine("Sem nó: " + Lista(pecas, res));
        Assert.NotEmpty(res);
        Assert.All(res.Where(r => pecas[r.Par.A].Grupo != pecas[r.Par.B].Grupo), r => Assert.Equal(ClasseSobreposicao.InterseccaoLegitima, r.C.Classe));
        Assert.Contains(res, r => OverlapClassifier.Pavimento(pecas[r.Par.A].Codigo) && OverlapClassifier.Pavimento(pecas[r.Par.B].Codigo));
        Assert.DoesNotContain(res, r => pecas[r.Par.A].Grupo == pecas[r.Par.B].Grupo);   // cada via sozinha não tem pisos sobrepostos

        var w = Y34TipTests.Make(null, (1, a), (1, b));
        Assert.Single(w.Nodes);
        var cruas = Pecas(w.Defs, w.Paths);
        var resCruas = OverlapClassifier.DasPecas(cruas, OverlapClassifier.NosEntreVias(w.Defs));
        output.WriteLine("Com nó, peças cruas: " + (resCruas.Count == 0 ? "nenhum par" : Lista(cruas, resCruas)));
        Assert.DoesNotContain(resCruas, r => r.C.Classe is ClasseSobreposicao.InterseccaoLegitima or ClasseSobreposicao.Duplicacao);
        var pecasNo = Pecas(w.Defs, w.Paths, ajustar: true);
        var resNo = OverlapClassifier.DasPecas(pecasNo, OverlapClassifier.NosEntreVias(w.Defs));
        Assert.True(resNo.Count == 0, "Com nó, contorno ajustado: " + Lista(pecasNo, resNo));
    }

    [Fact]
    public void NosEntreVias_LeInterseccaoERotatoria_NuloForaDasVias()
    {
        var p1 = new RoadPavementDefinition { GroupId = "g1" };
        var p2 = new RoadPavementDefinition { GroupId = "g2" };
        var p3 = new RoadPavementDefinition { GroupId = "g3" };
        var no = new IntersectionDefinition { GroupId = "n1", RoadIds = { p1.Id, p2.Id } };
        var rot = new RoundaboutDefinition { GroupId = "r1", Legs = { new RoundaboutLeg { RoadId = p2.Id }, new RoundaboutLeg { RoadId = p3.Id } } };
        var f = OverlapClassifier.NosEntreVias(new MarkingDefinition[] { p1, p2, p3, no, rot });
        Assert.True(f("g1", "g2"));
        Assert.True(f("g3", "g2"));
        Assert.False(f("g1", "g3"));
        Assert.Null(f("g1", "g1"));
        Assert.Null(f("g1", "n1"));      // peça do nó: não é via
    }
}
