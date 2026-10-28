using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 20 (itens 13 e 21): ferramentas de terreno – temas de elevação, declividade e orientação, medidas entre pontos e pelo
/// caminho, plataformas, suavização, linhas de quebra e comparação de superfícies.
/// </summary>
public class Etapa20TerrenoTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    /// <summary>Plano descendo para o oeste: z = 100 + 0,1·x (10 %).</summary>
    private static double? Rampa(Vec2 p) => 100 + 0.1 * p.X;

    /// <summary>Plano de 12 % descendo para o oeste (longe dos limites das classes de declividade).</summary>
    private static double? Rampa12(Vec2 p) => 100 + 0.12 * p.X;

    [Fact]
    public void Temas_num_plano_inclinado()
    {
        var g = TemasTerreno.Amostrar(Rampa12, new Vec2(0, 0), new Vec2(40, 20), 1.0);
        Assert.Equal(40 * 20, g.Nx * g.Ny);
        var c = TemasTerreno.Celula(g, 5, 5)!.Value;
        Assert.Equal(12, c.Declividade, 6);
        Assert.Equal(270, c.Orientacao, 6);    // a água desce para −X (oeste)
        // Declividade: tudo na classe 10–20 % (padrão do plugin).
        var decl = TemasTerreno.Mapa(g, TemaTerreno.Declividade, TemasTerreno.ClassesPadrao(TemaTerreno.Declividade, double.NaN, double.NaN));
        Assert.Equal(800, decl.AreaTotal, 6);
        var cheia = Assert.Single(decl.Faixas, f => f.Area > 0);
        Assert.Equal(10, cheia.Classe.De);
        Assert.Equal(800, cheia.Regioes.Sum(r => Math.Abs(r.Area)), 3);
        // Orientação: tudo "O".
        var ori = TemasTerreno.Mapa(g, TemaTerreno.Orientacao, TemasTerreno.ClassesPadrao(TemaTerreno.Orientacao, double.NaN, double.NaN));
        Assert.StartsWith("O (", Assert.Single(ori.Faixas, f => f.Area > 0).Classe.Rotulo);
        // Elevação: 4 intervalos iguais de 100,06 a 104,74 m (centros das células), 10 colunas de células cada (200 m²).
        var ev = TemasTerreno.Mapa(g, TemaTerreno.Elevacao, TemasTerreno.ClassesPadrao(TemaTerreno.Elevacao, 100.06, 104.74, 4));
        Assert.All(ev.Faixas, f => Assert.InRange(f.Area, 200 - 1e-6, 200 + 1e-6));
        Assert.Equal(100.06, ev.Min, 6);
        Assert.Equal(104.74, ev.Max, 6);
    }

    [Fact]
    public void Orientacao_numa_colina_e_classes_informadas()
    {
        // Cone: a água desce para fora em todas as direções – os oito rumos aparecem, com áreas parecidas.
        double? Cone(Vec2 p) => 50 - 0.2 * p.DistanceTo(new Vec2(50, 50));
        // Região circular: num quadrado os setores diagonais teriam mais área que os dos eixos (0,586 × 0,414).
        var disco = new Polygon2(CurveTools.Circle(new Vec2(50, 50), 35, 0.05));
        var g = TemasTerreno.Amostrar(Cone, new Vec2(10, 10), new Vec2(90, 90), 1.0, disco);
        var ori = TemasTerreno.Mapa(g, TemaTerreno.Orientacao, TemasTerreno.ClassesPadrao(TemaTerreno.Orientacao, double.NaN, double.NaN));
        var setores = ori.Faixas.Where(f => !double.IsNaN(f.Classe.De)).ToList();
        Assert.Equal(8, setores.Count);
        Assert.All(setores, f => Assert.InRange(f.Area, ori.AreaTotal / 8 * 0.95, ori.AreaTotal / 8 * 1.05));
        // Setor N cobre 337,5° a 22,5° (volta em 360°).
        var cls = TemasTerreno.ClassesPadrao(TemaTerreno.Orientacao, 0, 0);
        Assert.Equal(0, TemasTerreno.ClasseDe(cls, TemaTerreno.Orientacao, 350));
        Assert.Equal(0, TemasTerreno.ClasseDe(cls, TemaTerreno.Orientacao, 10));
        Assert.Equal(2, TemasTerreno.ClasseDe(cls, TemaTerreno.Orientacao, 90));
        // Limites informados: "0; 8; 30" → 0–8, 8–30, 30 ou mais.
        var lim = TemasTerreno.ClassesDosLimites(TemaTerreno.Declividade, TemasTerreno.LerLimites("0; 8; 30"));
        Assert.Equal(3, lim.Count);
        Assert.Equal("30 % ou mais", lim[^1].Rotulo);
        Assert.Equal(1, TemasTerreno.ClasseDe(lim, TemaTerreno.Declividade, 12));
        Assert.Equal(new[] { 0.0, 8.5, 30 }, TemasTerreno.LerLimites("0; 8,5; 30"));
    }

    [Fact]
    public void Medidas_entre_pontos_e_pelo_caminho()
    {
        var m = MedidasTerreno.Medir(new[] { new Vec2(0, 0), new Vec2(30, 40) }, Rampa);
        Assert.Equal(50, m.Horizontal, 9);
        Assert.Equal(3, m.Desnivel, 9);
        Assert.Equal(6, m.DeclividadeMedia, 6);          // 3 m em 50 m
        Assert.Equal(6, m.DeclividadeMaxima, 6);
        Assert.Equal(Math.Sqrt(50 * 50 + 9), m.SobreOTerreno, 6);
        // Ida e volta: desnível zero, subida e descida de 2 m, declividade máxima 10 %.
        var v = MedidasTerreno.Medir(new[] { new Vec2(0, 0), new Vec2(20, 0), new Vec2(0, 0) }, Rampa);
        Assert.Equal(40, v.Horizontal, 9);
        Assert.Equal(0, v.Desnivel, 9);
        Assert.Equal(2, v.Subida, 6);
        Assert.Equal(2, v.Descida, 6);
        Assert.Equal(10, v.DeclividadeMaxima, 6);
        Assert.Equal(81, v.Perfil.Count);
        // Fora do terreno: a parte sem terreno é contada.
        var fora = MedidasTerreno.Medir(new[] { new Vec2(0, 0), new Vec2(10, 0) }, p => p.X < 5 ? 0 : null);
        Assert.InRange(fora.SemTerreno, 4.9, 5.6);
    }

    [Fact]
    public void Plataforma_na_cota_de_equilibrio_e_na_terraplenagem()
    {
        var area = Polygon2.Rectangle(new Vec2(0, 0), new Vec2(20, 20));
        // Plano: a média e o equilíbrio (fh = 1) ficam no meio, 101 m.
        Assert.Equal(101, Plataformas.CotaMedia(area, Rampa, 0.5)!.Value, 6);
        var eq = Plataformas.CotaEquilibrio(area, Rampa, 1.0, 0.5)!.Value;
        Assert.Equal(101, eq, 2);
        // Com homogeneização 1,25 a cota desce até o corte valer 1,25 × o aterro.
        var eq2 = Plataformas.CotaEquilibrio(area, Rampa, 1.25, 0.5)!.Value;
        Assert.True(eq2 < eq);
        var (c, f, a) = Plataformas.Volumes(area, eq2, Rampa, 0.5);
        Assert.Equal(400, a, 6);
        Assert.InRange(c / f, 1.25 - 0.01, 1.25 + 0.01);
        // A definição: borda no modelo, plataforma da terraplenagem (com os taludes) e participante da Terraplenagem.
        var d = new PlataformaDefinition { Cota = 101, CutSlope = 1, FillSlope = 1.5 };
        d.SetPath(PathReference.FromPoints(area.Outer, 101, closed: true));
        var geo = TerrainGenerators.Plataforma(d, new Polyline2(area.Outer, true));
        var pad = Assert.Single(geo.Pads);
        Assert.Equal(0, pad.Z);                      // na cota do caminho
        Assert.True(pad.Daylight);
        Assert.NotEmpty(geo.Pieces);
        Assert.All(geo.Pieces, p => Assert.Equal(MarkingColor.Terra, p.Color));
        Assert.InRange(geo.Pieces.Sum(p => p.Shape.Area), 80 * 0.15 - 0.2, 80 * 0.15 + 0.01);
        Assert.Contains(d, EarthworkRecord.Participantes(new MarkingDefinition[] { d }));
        Assert.True(MarkingDefinition.FromJson(d.ToJson()) is PlataformaDefinition { Cota: 101, Modo: CotaPlataforma.Equilibrio });
        // Na terraplenagem: a plataforma na cota com taludes até o terreno, volume dela no registro (por dona).
        var res = Grading.Design(Array.Empty<GradeCorridor>(), new[] { new GradePad(area, 101) { CutSlope = 1, FillSlope = 1.5, Owner = d.Id } }, Rampa, 0, 60, 0.5, 4, 1);
        Grading.Volumes(res, Rampa, 0.5);
        Assert.True(res.CutM3 > c * 0.95 && res.FillM3 > f * 0.95);
        Assert.True(res.ByOwner.ContainsKey(d.Id));
    }

    [Fact]
    public void Suavizacao_tira_o_ruido_dentro_e_nao_mexe_fora()
    {
        var rnd = new Random(7);
        var pts = new List<Vec3>();
        for (var x = 0; x <= 40; x++)
            for (var y = 0; y <= 40; y++)
                pts.Add(new Vec3(x, y, 10 + (rnd.NextDouble() - 0.5) * 2));
        var reg = Polygon2.Rectangle(new Vec2(10, 10), new Vec2(30, 30));
        var sv = EdicaoTerreno.Suavizar(pts, reg, 3, 2.0, 0.8);
        double Var(IEnumerable<double> z) { var l = z.ToList(); var m = l.Average(); return l.Average(v => (v - m) * (v - m)); }
        var centro = Polygon2.Rectangle(new Vec2(14, 14), new Vec2(26, 26));
        var antes = pts.Where(p => centro.Contains(p.XY)).Select(p => p.Z);
        var depois = sv.Where(p => centro.Contains(p.XY)).Select(p => p.Z);
        Assert.True(Var(depois) < Var(antes) * 0.3);
        // Fora da região, igual; perto da borda, muda menos que no meio.
        for (int i = 0; i < pts.Count; i++)
            if (!reg.Contains(pts[i].XY)) Assert.Equal(pts[i].Z, sv[i].Z);
        var borda = Enumerable.Range(0, pts.Count).Where(i => reg.Contains(pts[i].XY) && (pts[i].X == 11 || pts[i].X == 29)).Average(i => Math.Abs(sv[i].Z - pts[i].Z));
        var meio = Enumerable.Range(0, pts.Count).Where(i => centro.Contains(pts[i].XY)).Average(i => Math.Abs(sv[i].Z - pts[i].Z));
        Assert.True(borda < meio);
    }

    [Fact]
    public void Linha_de_quebra_passa_a_mandar_no_terreno()
    {
        var pts = new List<Vec3>();
        for (var x = 0; x <= 20; x += 2)
            for (var y = 0; y <= 20; y += 2)
                pts.Add(new Vec3(x, y, 0));
        // Crista de 3 m de altura em y = 9, de x = 0 a 20.
        var linha = new[] { new Vec3(0, 9, 3), new Vec3(20, 9, 3) };
        var novos = EdicaoTerreno.ComLinha(pts, linha, 0.9, 1.0);
        Assert.DoesNotContain(novos, p => Math.Abs(p.Y - 9) < 0.9 && p.Z == 0);
        Assert.Equal(21, novos.Count(p => p.Z == 3));
        var mesh = Tin.Mesh(novos);
        Assert.Equal(3, mesh.Z(new Vec2(7.3, 9))!.Value, 6);
        Assert.InRange(mesh.Z(new Vec2(7, 12))!.Value, -1e-9, 3);
        // Pontos da linha com a cota interpolada entre os vértices.
        var lp = EdicaoTerreno.PontosDaLinha(new[] { new Vec3(0, 0, 0), new Vec3(10, 0, 5) }, 2.5);
        Assert.Equal(new[] { 0, 1.25, 2.5, 3.75, 5 }, lp.Select(p => p.Z).ToArray());
    }

    [Fact]
    public void Comparacao_de_superficies_da_corte_aterro_e_a_distribuicao()
    {
        double? A(Vec2 p) => 0;
        double? B(Vec2 p) => p.X < 10 ? 1.0 : -0.5;
        var comp = ComparacaoSuperficies.Comparar(A, B, new Vec2(0, 0), new Vec2(20, 10), 0.5);
        Assert.Equal(200, comp.Area, 6);
        Assert.Equal(100, comp.Aterro, 6);
        Assert.Equal(50, comp.Corte, 6);
        Assert.Equal(-0.5, comp.Min, 9);
        Assert.Equal(1.0, comp.Max, 9);
        Assert.Equal(200, comp.Distribuicao.Sum(x => x.Area), 6);
        Assert.Equal(2, comp.Distribuicao.Count(x => x.Area > 0));
        Assert.Contains(comp.Distribuicao, x => x.Area > 0 && x.Classe.Rotulo.Contains("aterro"));
        Assert.Contains(comp.Distribuicao, x => x.Area > 0 && x.Classe.Rotulo.Contains("corte"));
    }

    [Fact]
    public void Tema_como_marca_da_vista_com_legenda()
    {
        var d = new TemaTerrenoDefinition { Tema = TemaTerreno.Declividade, Min = new Vec2(0, 0), Max = new Vec2(40, 20), Passo = 1 };
        var ctx = new BuildContext { Catalog = Cat, ViewScale = 200, TerrenoNatural = Rampa12, TerrenoAtual = Rampa12 };
        var geo = MarkingBuilder.Build(d, null, ctx);
        Assert.Empty(geo.Warnings);
        var regioes = geo.Pieces.Where(p => p.Layer == "TEMA").ToList();
        Assert.NotEmpty(regioes);
        Assert.All(geo.Pieces, p => Assert.False(string.IsNullOrEmpty(p.Rgb)));
        Assert.Equal(800, regioes.Sum(p => Math.Abs(p.Shape.Area)), 3);
        var textos = geo.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("DECLIVIDADE", textos);
        Assert.Contains(textos, t => t.StartsWith("10 a 20 % – 800 m² (100 %)"));
        // Sem terreno: aviso, nada desenhado.
        var vazio = MarkingBuilder.Build(d, null, new BuildContext { Catalog = Cat });
        Assert.NotEmpty(vazio.Warnings);
        Assert.Empty(vazio.Pieces);
        // Diferença: natural × atual.
        var dif = new TemaTerrenoDefinition { Tema = TemaTerreno.Diferenca, Min = new Vec2(0, 0), Max = new Vec2(40, 20), Passo = 1 };
        var g2 = MarkingBuilder.Build(dif, null, new BuildContext { Catalog = Cat, TerrenoNatural = Rampa, TerrenoAtual = p => Rampa(p) + 0.6 });
        Assert.Contains(g2.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith("Corte 0 m³ · Aterro 480 m³"));
        // Fica no projeto como as outras marcas (JSON, anotação, fora dos quantitativos).
        Assert.True(MarkingDefinition.FromJson(d.ToJson()) is TemaTerrenoDefinition { Tema: TemaTerreno.Declividade, Passo: 1 });
        Assert.IsAssignableFrom<IAnnotationDefinition>(d);
        Assert.Equal(GrupoMarca.Detalhamento, MarkingBuilder.Describe(d, Cat).Group);
    }

    private static string Raiz([System.Runtime.CompilerServices.CallerFilePath] string aqui = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(aqui)!);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void Ligacao_na_faixa_e_no_plugin()
    {
        string Ler(string rel) => File.ReadAllText(Path.Combine(Raiz(), rel));
        var rb = Ler("src/SinalizacaoViaria.Revit/RibbonBuilder.cs");
        foreach (var cmd in new[] { "CmdTemasTerreno", "CmdCompararSuperficies", "CmdMedirTerreno", "CmdPlataforma", "CmdLinhaQuebra", "CmdSuavizarTerreno" })
            Assert.Contains($"typeof({cmd})", rb);
        var svc = Ler("src/SinalizacaoViaria.Revit/Infrastructure/MarkingService.cs");
        Assert.Contains("ctx.TerrenoNatural = TerrainModel.Ground(_doc);", svc);
        Assert.Contains("ctx.TerrenoAtual = TerrainModel.Current(_doc);", svc);
        var ts = Ler("src/SinalizacaoViaria.Revit/Infrastructure/TerrainService.cs");
        Assert.Contains("public static void EditarNatural(", ts);
        Assert.Contains("TerrainModel.ReplaceOriginal(topo, mesh);", ts);
        var cmds = Ler("src/SinalizacaoViaria.Revit/Commands/TerrainToolsCommands.cs");
        Assert.Contains("EarthworkRecord.Participantes(MarkingStorage.Definitions(doc))", cmds);
        Assert.Contains("TerrainActions.AfterCreate(uidoc, d);", cmds);
        Assert.Contains("if (def is PlataformaDefinition) continue;", Ler("src/SinalizacaoViaria.Core/Quantities/QuantityCalculator.cs"));
        // Editar sobre o tema ou a plataforma abre os mesmos campos (e a terraplenagem é refeita depois da plataforma).
        Assert.Contains("TerrainForms.ForEdit(stored.Definition)", Ler("src/SinalizacaoViaria.Revit/Commands/EditCommands.cs"));
        Assert.Contains("case PlataformaDefinition pl:", cmds);
        Assert.Contains("case TemaTerrenoDefinition tema:", cmds);
    }
}
