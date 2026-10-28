using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 21 (item 11): corpos d'água – seção e fundo do leito do rio, fundo do lago pelo talude, água e faixa de proteção
/// informada, escavação pela Terraplenagem e a conferência V18 (lotes na faixa, lançamento afogado, faixa não informada).
/// </summary>
public class Etapa21AguaTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static List<CopiaGravada> Copias(params MarkingDefinition[] defs) => defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e-" + d.Id)).ToList();
    private static double? Plano100(Vec2 p) => 100;

    private static CorpoDaguaDefinition Rio(params Vec2[] eixo)
    {
        var d = new CorpoDaguaDefinition { Corpo = TipoCorpoDagua.Rio, LarguraFundo = 3, Lamina = 1, TaludeMargem = 2 };
        d.SetPath(PathReference.FromPoints(eixo, 0));
        return d;
    }

    private static CorpoDaguaDefinition Lago(double lado, double nivel, double fundo, double talude)
    {
        var d = new CorpoDaguaDefinition { Corpo = TipoCorpoDagua.Lago, NivelAgua = nivel, CotaFundo = fundo, TaludeMargem = talude };
        d.SetPath(PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(lado, 0), new Vec2(lado, lado), new Vec2(0, lado) }, 0));
        return d;
    }

    [Fact]
    public void Secao_do_leito_e_fundo_informado_por_estaca()
    {
        var d = Rio(new Vec2(0, 0), new Vec2(120, 0));
        Assert.Equal(7, CorposDagua.LarguraAgua(d), 9);                       // 3 + 2 × 1 × 2
        d.Fundo = CorposDagua.LerFundo("0; 100\r\n100;99\n\nlixo");
        Assert.Equal(new[] { new Vec2(0, 100), new Vec2(100, 99) }, d.Fundo);
        Assert.Equal(new[] { new Vec2(10, 100.5) }, CorposDagua.LerFundo("10; 100,5"));
        var fundo = CorposDagua.Fundo(d, 120, null);
        var s = CorposDagua.Secao(d, fundo, 50);
        Assert.Equal(99.5, s.ZFundo, 9);
        Assert.Equal(100.5, s.ZAgua, 9);
        Assert.Equal(7, s.LarguraAgua, 9);
        Assert.Equal(5, s.AreaMolhada, 9);                                    // (3 + 2 × 1) × 1
        Assert.Equal(99, fundo.Z(120), 9);                                    // depois do último ponto: a última cota
        Assert.False(new CorpoDaguaDefinition { Corpo = TipoCorpoDagua.Lagoa }.EhRio);
        Assert.Equal("Córrego", new CorpoDaguaDefinition().KindName);
    }

    [Fact]
    public void Fundo_pelo_terreno_nunca_sobe_para_jusante()
    {
        var d = Rio(new Vec2(0, 0), new Vec2(100, 0));
        d.Profundidade = 1.5;
        // Terreno no eixo com uma lombada entre as estacas 50 e 80.
        var terreno = CorposDagua.Perfil(new[] { new Vec2(0, 105), new Vec2(50, 100), new Vec2(60, 102), new Vec2(100, 98) });
        var fundo = CorposDagua.Fundo(d, 100, terreno);
        Assert.Equal(103.5, fundo.Z(0), 6);
        Assert.Equal(98.5, fundo.Z(50), 6);
        Assert.Equal(98.5, fundo.Z(60), 6);                                   // a lombada não sobe o fundo
        Assert.Equal(97.5, fundo.Z(90), 6);
        Assert.Equal(96.5, fundo.Z(100), 6);
        for (var x = 1.0; x <= 100; x += 1) Assert.True(fundo.Z(x) <= fundo.Z(x - 1) + 1e-9, $"subiu na estaca {x}");
        // Sem terreno nem fundo informado: o padrão relativo (e o gerador avisa).
        Assert.Equal(-1.5, CorposDagua.Fundo(d, 100, null).Z(30), 9);
        var geo = WaterGenerator.Build(d, new Polyline2(d.PathRef.Points, false), new BuildContext { Catalog = Cat });
        Assert.Contains(geo.Warnings, w => w.Contains("sem terreno para o fundo padrão"));
    }

    [Fact]
    public void Fundo_do_lago_recuado_pelo_talude()
    {
        var margem = new Polygon2(new[] { new Vec2(0, 0), new Vec2(40, 0), new Vec2(40, 40), new Vec2(0, 40) });
        var f = CorposDagua.FundoDoLago(margem, 98, 100, 2)!;                 // recuo de (100 − 98) × 2 = 4 m
        Assert.Equal(32 * 32, Math.Abs(f.Area), 1);
        Assert.Null(CorposDagua.FundoDoLago(margem, 98, 100, 12));            // 24 m para dentro em cada lado: não cabe
        Assert.Equal(1600, Math.Abs(CorposDagua.FundoDoLago(margem, 100, 100, 2)!.Area), 6);
        // O gerador avisa quando não cabe ou quando o nível não está acima do fundo.
        var lago = Lago(40, 100, 98, 12);
        Assert.Contains(WaterGenerator.Build(lago, new Polyline2(lago.PathRef.Points, true), new BuildContext { Catalog = Cat }).Warnings, w => w.Contains("não cabe"));
        lago = Lago(40, 97, 98, 2);
        Assert.Contains(WaterGenerator.Build(lago, new Polyline2(lago.PathRef.Points, true), new BuildContext { Catalog = Cat }).Warnings, w => w.Contains("não está acima do fundo"));
    }

    [Fact]
    public void Agua_e_faixa_de_protecao_informada()
    {
        var rio = Rio(new Vec2(0, 0), new Vec2(100, 0));
        var eixo = new Polyline2(rio.PathRef.Points, false);
        Assert.Equal(700, CorposDagua.Agua(rio, eixo).Sum(p => Math.Abs(p.Area)), 1);
        Assert.Empty(CorposDagua.Faixa(rio, eixo));                         // 0 = não informada
        rio.FaixaProtecao = 30;
        Assert.Equal(100 * 60, CorposDagua.Faixa(rio, eixo).Sum(p => Math.Abs(p.Area)), 1);   // 30 m de cada lado, fora da água
        var lago = Lago(40, 100, 98, 2);
        var contorno = new Polyline2(lago.PathRef.Points, true);
        Assert.Equal(1600, CorposDagua.Agua(lago, contorno).Sum(p => Math.Abs(p.Area)), 6);
        lago.FaixaProtecao = 10;
        // Anel de 10 m em volta: entre 4 × 40 × 10 + π × 10² (cantos redondos) e 4 × 40 × 10 + 4 × 10² (cantos vivos).
        Assert.InRange(CorposDagua.Faixa(lago, contorno).Sum(p => Math.Abs(p.Area)), 1600 + Math.PI * 100 - 5, 2000 + 1);
        Assert.Equal(100, CorposDagua.NivelPerto(lago, contorno, new Vec2(500, 500), null));
    }

    [Fact]
    public void Rio_escavado_pela_terraplenagem_com_a_agua()
    {
        var d = Rio(new Vec2(0, 0), new Vec2(100, 0));
        d.Fundo = new() { new Vec2(0, 99), new Vec2(100, 98) };
        var path = new Polyline2(d.PathRef.Points, false);
        var geo = WaterGenerator.Build(d, path, new BuildContext { Catalog = Cat, Ground = Plano100 });
        Assert.Empty(geo.Warnings);
        var c = Assert.Single(geo.Corridors);
        Assert.True(c.SpillStart && c.SpillEnd);
        Assert.Equal(99, c.Left[0].Z, 6);
        Assert.Equal(98, c.Right[^1].Z, 6);
        Assert.Equal(3, c.Left[0].XY.DistanceTo(c.Right[0].XY), 6);
        // A água: varredura na largura da superfície, com o topo no nível (fundo + lâmina).
        var agua = geo.Pieces.Where(p => p.Color == MarkingColor.Agua).ToList();
        Assert.NotEmpty(agua);
        var topo = agua.SelectMany(p => p.Solid!.Faces.SelectMany(f => f)).ToList();
        Assert.Equal(100, topo.Where(v => v.X < 0.01).Max(v => v.Z), 6);
        Assert.Equal(99, topo.Where(v => v.X > 99.99).Max(v => v.Z), 6);
        Assert.Equal(700, agua.Sum(p => Math.Abs(p.Shape.Area)), 1);
        // Terraplenagem no plano 100: corte do trapézio, de 1 a 2 m de altura (b = 3, talude 2) ≈ ∫(3h + 2h²) = 916,7 m³ + as pontas.
        var res = Grading.Design(geo.Corridors, geo.Pads, Plano100, 0, 60, 0.5, 4, 0.5);
        Grading.Volumes(res, Plano100, 0.5);
        Assert.InRange(res.CutM3, 880, 1060);
        Assert.True(res.FillM3 < 5, $"aterro {res.FillM3}");
        // É participante da terraplenagem (e sai dela se não escavar).
        Assert.Contains(d, EarthworkRecord.Participantes(new MarkingDefinition[] { d }));
        d.AdjustTerrain = false;
        Assert.Empty(EarthworkRecord.Participantes(new MarkingDefinition[] { d }));
    }

    [Fact]
    public void Lago_com_o_fundo_na_cota_e_a_agua_no_nivel()
    {
        var d = Lago(40, 100, 98, 2);
        var geo = WaterGenerator.Build(d, new Polyline2(d.PathRef.Points, true), new BuildContext { Catalog = Cat });
        Assert.Empty(geo.Warnings);
        var pad = Assert.Single(geo.Pads);
        Assert.Equal(98, pad.Z);
        Assert.Equal(2, pad.CutSlope);
        Assert.True(pad.Daylight);
        Assert.Equal(1024, Math.Abs(pad.Area.Area), 1);
        var agua = Assert.Single(geo.Pieces, p => p.Color == MarkingColor.Agua);
        Assert.Equal(100, agua.Elevation + agua.Thickness, 6);
        Assert.Equal(1600, Math.Abs(agua.Shape.Area), 6);
        // Escavação no plano 100: tronco de pirâmide 40 × 40 → 32 × 32 com 2 m (prismoide: 2/6 × (1600 + 4 × 36² + 1024)).
        var res = Grading.Design(geo.Corridors, geo.Pads, Plano100, 0, 60, 0.5, 2, 0.5);
        Grading.Volumes(res, Plano100, 0.25);
        Assert.InRange(res.CutM3, 2602.7 * 0.96, 2602.7 * 1.04);
        Assert.True(res.FillM3 < 5, $"aterro {res.FillM3}");
    }

    /// <summary>Gleba com a quadra de 102 × 60 m (testada sul: lotes de 12 × 30 m de y = 0 a 30) e um rio ao sul, no y = −20.</summary>
    private static (LoteamentoDefinition Lot, QuadraDefinition Q, CorpoDaguaDefinition Rio) CenaMargem(double faixa)
    {
        var lot = new LoteamentoDefinition { Nome = "Margem", Gleba = new() { new(-10, -40), new(112, -40), new(112, 70), new(-10, 70) } };
        var q = LTScenes.Q("S", new List<Vec2> { new(0, 0), new(102, 0), new(102, 60), new(0, 60) }, new Vec2(51, 0));
        q.Miolo = ModoMiolo.AreaPropria;
        q.LoteamentoId = lot.Id;
        var rio = Rio(new Vec2(-10, -20), new Vec2(112, -20));
        rio.Nome = "do Teste";
        rio.FaixaProtecao = faixa;
        rio.FonteFaixa = "Lei citada pelo usuário, art. 1º";
        return (lot, q, rio);
    }

    [Fact]
    public void V18_lotes_dentro_da_faixa_informada_com_a_lei_citada()
    {
        // Água até y = −16,5; faixa de 30 m até y = 13,5: os lotes da testada sul entram nela.
        var (lot, q, rio) = CenaMargem(30);
        var v18 = Assert.Single(ModelValidator.Validate(Copias(lot, q, rio)), a => a.Codigo == "V18");
        Assert.Equal(Severidade.Aviso, v18.Severidade);
        Assert.Equal("Corpos d'água", v18.Modulo);
        Assert.Contains("Quadra S:", v18.Mensagem);
        Assert.Contains("dentro da faixa de proteção de 30 m de Rio do Teste", v18.Mensagem);
        Assert.Contains("fonte: Lei citada pelo usuário, art. 1º", v18.Mensagem);
        Assert.Contains(q.Id, v18.Ids);
        Assert.Contains(rio.Id, v18.Ids);
        // Faixa de 10 m (até y = −6,5): nenhum lote.
        (lot, q, rio) = CenaMargem(10);
        Assert.DoesNotContain(ModelValidator.Validate(Copias(lot, q, rio), incluirNaoVerificados: true), a => a.Codigo == "V18");
        // Sem faixa informada: não verificado (nunca uma largura legal inventada).
        (lot, q, rio) = CenaMargem(0);
        var nv = Assert.Single(ModelValidator.Validate(Copias(lot, q, rio), incluirNaoVerificados: true), a => a.Codigo == "V18");
        Assert.Equal(Severidade.NaoVerificavel, nv.Severidade);
        Assert.Contains("faixa de proteção não informada", nv.Mensagem);
        Assert.Contains(ModelValidator.Regras, r => r.Key == "V18");
    }

    [Fact]
    public void V18_lancamento_afogado_pelo_fundo_da_caixa_e_pelo_informado()
    {
        var rio = Rio(new Vec2(0, 0), new Vec2(100, 0));
        rio.Fundo = new() { new Vec2(0, 95), new Vec2(100, 94) };              // nível d'água na estaca 50: 94,5 + 1 = 95,5
        rio.FaixaProtecao = 15;
        var a = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(50, 40), Z = 101 };
        var b = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(50, 10), Z = 100 };
        var tubo = new DrainagePipeDefinition { FromId = a.Id, ToId = b.Id, Diameter = 0.4, Z = 101 };
        // Fundo da caixa de jusante acima do nível: só a informação do lançamento.
        var info = Assert.Single(ModelValidator.Validate(Copias(rio, a, b, tubo)), x => x.Codigo == "V18");
        Assert.Equal(Severidade.Info, info.Severidade);
        Assert.Contains("a rede lança em Rio", info.Mensagem);
        Assert.Contains("nível d'água 95,50 m", info.Mensagem.Replace('.', ','));
        // Caixa de jusante mais funda (fundo 0,5 m abaixo do nível), sem fundo informado no trecho: saída afogada.
        var prof = DrainageGenerator.NetworkBox(b)!.Value.InvertDepth;
        b.Z = 95.5 + prof - 0.5;
        var afogada = Assert.Single(ModelValidator.Validate(Copias(rio, a, b, tubo)), x => x.Codigo == "V18");
        Assert.Equal(Severidade.Aviso, afogada.Severidade);
        Assert.Contains("saída afogada", afogada.Mensagem);
        Assert.Contains(b.Id, afogada.Ids);
        // O fundo informado no trecho vale sobre o da caixa.
        tubo.InvertTo = 96;
        Assert.Equal(Severidade.Info, Assert.Single(ModelValidator.Validate(Copias(rio, a, b, tubo)), x => x.Codigo == "V18").Severidade);
        tubo.InvertTo = 95;
        Assert.Equal(Severidade.Aviso, Assert.Single(ModelValidator.Validate(Copias(rio, a, b, tubo)), x => x.Codigo == "V18").Severidade);
        // Longe da água (mais de 30 m): não é lançamento nele.
        b.Position = new Vec2(50, 60);
        a.Position = new Vec2(50, 90);
        Assert.DoesNotContain(ModelValidator.Validate(Copias(rio, a, b, tubo)), x => x.Codigo == "V18");
    }

    [Fact]
    public void Json_central_e_quantitativo()
    {
        var d = Lago(40, 100, 98, 2.5);
        d.Corpo = TipoCorpoDagua.Lagoa;
        d.Nome = "Azul";
        d.FaixaProtecao = 12;
        d.FonteFaixa = "informado";
        var back = Assert.IsType<CorpoDaguaDefinition>(MarkingDefinition.FromJson(d.ToJson()));
        Assert.Equal((TipoCorpoDagua.Lagoa, "Azul", 100.0, 98.0, 2.5, 12.0, "informado"),
            (back.Corpo, back.Nome, back.NivelAgua, back.CotaFundo, back.TaludeMargem, back.FaixaProtecao, back.FonteFaixa));
        Assert.True(back.PathRef.Closed);
        Assert.Equal("AGUA-LAGO", back.DisplayCode);
        var rio = Rio(new Vec2(0, 0), new Vec2(10, 0));
        rio.Fundo = new() { new Vec2(0, 5), new Vec2(10, 4.5) };
        Assert.Equal(rio.Fundo, Assert.IsType<CorpoDaguaDefinition>(MarkingDefinition.FromJson(rio.ToJson())).Fundo);
        Assert.Equal("AGUA-RIO", rio.DisplayCode);
        Assert.Equal("drenagem", CentralDoProjeto.ModuloDe(d));
        Assert.Equal(GrupoMarca.Urbanizacao, MarkingBuilder.Describe(d, Cat).Group);
    }

    [Fact]
    public void Toda_marca_registrada_vai_e_volta_pelo_json()
    {
        // Regressão: uma propriedade com o nome do discriminador ("tipo") impede gravar a marca no modelo.
        var tipos = typeof(MarkingDefinition).GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonDerivedTypeAttribute), false)
            .Cast<System.Text.Json.Serialization.JsonDerivedTypeAttribute>().ToList();
        Assert.Contains(tipos, t => t.DerivedType == typeof(CorpoDaguaDefinition));
        foreach (var t in tipos)
        {
            if (t.DerivedType.GetConstructor(Type.EmptyTypes) == null) continue;
            var d = (MarkingDefinition)Activator.CreateInstance(t.DerivedType)!;
            var back = MarkingDefinition.FromJson(d.ToJson());
            Assert.True(back?.GetType() == t.DerivedType, $"{t.DerivedType.Name} não voltou do JSON");
        }
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
        Assert.Contains("typeof(CmdRioCorrego)", rb);
        Assert.Contains("typeof(CmdLagoLagoa)", rb);
        Assert.Contains("\"Corpos d'Água\"", rb);
        Assert.Contains("WaterForms.ForEdit(uidoc, stored.Definition)", Ler("src/SinalizacaoViaria.Revit/Commands/EditCommands.cs"));
        var cmds = Ler("src/SinalizacaoViaria.Revit/Commands/WaterCommands.cs");
        Assert.Contains("if (d.AdjustTerrain) TerrainActions.AfterCreate(uidoc, d);", cmds);
        Assert.Contains("TerrainModel.Ground(", cmds);
        // As pontas em talude do leito chegam à terraplenagem do Toposolid.
        Assert.Contains("SpillStart = c.SpillStart, SpillEnd = c.SpillEnd", Ler("src/SinalizacaoViaria.Revit/Infrastructure/TerrainService.cs"));
        Assert.Contains("### 24.10 Corpos d'água", Ler("docs/MANUAL.md"));
    }
}
