using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 11 do plano – telas dos módulos (Prompt de Interface §10–§16): o conteúdo das seções da janela da via (perfil,
/// pavimentação, terraplenagem, drenagem, validação e resumo, para a via nova e para a editada) e as abas da janela.
/// </summary>
public class Etapa11TelasTests
{
    private static readonly DateTime T0 = new(2026, 10, 27, 9, 0, 0);

    /// <summary>Via de 200 m com greide de 1 % (2 m → 4 m), pistas de 3,5 m e calçadas de 3 m, moldando o terreno.</summary>
    private static RoadPavementDefinition Via()
    {
        var v = new RoadPavementDefinition
        {
            PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(200, 0) }, 100), GroupId = "g-" + Guid.NewGuid().ToString("N"),
            LeftWidth = 3.5, RightWidth = 3.5, LeftSidewalk = 3, RightSidewalk = 3,
        };
        v.Output.Grade = new RoadGrade { DefaultCurve = 60, AdjustTerrain = true };
        foreach (var (s, z) in new[] { (0.0, 2.0), (200.0, 4.0) }) v.Output.Grade.Points.Add(new GradePoint(s, z));
        return v;
    }

    private static string Todas(IReadOnlyList<EstadoContexto> l) => string.Join("\n", l.Select(x => $"[{x.Nivel}] {x.Texto}"));

    [Fact]
    public void FormularioVia_ViaNova_DizOQueAconteceAoAplicar()
    {
        var c = FormularioVia.Contexto(null, Array.Empty<MarkingDefinition>(), null, new GeometricCriteria { RampaMaxima = 8 }, temToposolid: true);
        Assert.Contains("O greide da via nova sai do relevo escolhido", Todas(c.Perfil));
        Assert.Contains("o terreno é cortado e aterrado ao criar", Todas(c.Terraplenagem));
        // O traçado só existe depois de aplicar: não verificado aqui, e dito onde se confere.
        Assert.Contains(c.Validacao, l => l.Nivel == NivelSituacao.NaoVerificado && l.Texto.Contains("Validar Modelo"));
        Assert.Contains("Ao aplicar: você indica o eixo", Todas(c.Resumo));
        Assert.Contains("desfazível", Todas(c.Resumo));
        var sem = FormularioVia.Contexto(null, Array.Empty<MarkingDefinition>(), null, null, temToposolid: false);
        Assert.Contains("Sem Toposolid no projeto: a via nova fica plana", Todas(sem.Perfil));
        Assert.Contains("nada a terraplenar", Todas(sem.Terraplenagem));
    }

    [Fact]
    public void FormularioVia_ViaExistente_PerfilPavimentoTerraplenagemEImpacto()
    {
        var v = Via();
        v.Estrutura = new EstruturaPavimento { Fonte = "projeto de pavimentação", Camadas = { new CamadaPavimento { Nome = "Base", Material = "BGS", EspessuraCm = 15 } } };
        var cruz = new IntersectionDefinition { RoadIds = { v.Id, "outra" } };
        var boca = new DrainageDefinition { Type = TipoDrenagem.BocaDeLoboSimples, Position = new Vec2(50, 3.5), Z = 100 };
        var all = new MarkingDefinition[] { v, cruz, boca };
        var reg = EarthworkRecord.Gravar(EarthworkRecord.Participantes(all), 0.4, 1, 1.5, T0, null);
        var c = FormularioVia.Contexto(v, all, reg, null, temToposolid: true);
        Assert.Contains("Greide com 2 PIV(s), curva vertical padrão de 60 m; molda o terreno.", Todas(c.Perfil));
        Assert.Contains("Estaqueamento padrão", Todas(c.Perfil));
        Assert.Contains(c.Pavimentacao, l => l.Nivel == NivelSituacao.Ok && l.Texto.StartsWith("Estrutura informada: 1 camada(s), 15 cm"));
        Assert.Contains("Base: 15 cm, BGS (base)", Todas(c.Pavimentacao));
        Assert.Contains(c.Terraplenagem, l => l.Nivel == NivelSituacao.Ok && l.Texto.StartsWith("terraplenagem: em dia"));
        Assert.Contains("Última terraplenagem: 27/10/2026 09:00 – subleito 0,40 m", Todas(c.Terraplenagem));
        Assert.Contains("1 captação(ões) na sarjeta desta via", Todas(c.Drenagem));
        Assert.Contains("sem ele não há dimensionamento hidráulico", Todas(c.Drenagem));
        Assert.Contains(c.Validacao, l => l.Nivel == NivelSituacao.NaoVerificado && l.Texto.StartsWith("Limites geométricos não informados"));
        Assert.Contains("1 conexão(ões) ligada(s) à via acompanham a mudança", Todas(c.Resumo));
        Assert.Contains(c.Resumo, l => l.Nivel == NivelSituacao.Aviso && l.Texto.StartsWith("A via molda o terreno"));
        // Sem estrutura: dito como não informada (a terraplenagem usa o subleito).
        v.Estrutura = null;
        Assert.Contains(FormularioVia.Contexto(v, all, reg, null, true).Pavimentacao, l => l.Nivel == NivelSituacao.NaoVerificado && l.Texto.StartsWith("Estrutura do pavimento não informada"));
    }

    [Fact]
    public void FormularioVia_ValidacaoDoTracado_ComOsLimitesInformados()
    {
        var v = Via();
        var dentro = FormularioVia.Contexto(v, new MarkingDefinition[] { v }, null, new GeometricCriteria { RampaMaxima = 8, Fonte = "projeto" }, true);
        Assert.Contains(dentro.Validacao, l => l.Nivel == NivelSituacao.Ok && l.Texto == "Traçado e greide dentro dos limites informados (projeto).");
        var fora = FormularioVia.Contexto(v, new MarkingDefinition[] { v }, null, new GeometricCriteria { RampaMaxima = 0.5, Fonte = "projeto" }, true);
        Assert.Contains(fora.Validacao, l => l.Nivel == NivelSituacao.Aviso && l.Texto.StartsWith("1 elemento(s) do traçado ou do greide fora dos limites informados (projeto)"));
        // O mesmo que o Validar Modelo aponta (V12), pela mesma função.
        Assert.Single(ModelValidator.Tracado(v, new GeometricCriteria { RampaMaxima = 0.5 }));
        Assert.Contains(ModelValidator.Validate(new List<CopiaGravada> { new(v.Id, v.ToJson(), "e1") }, criterios: new GeometricCriteria { RampaMaxima = 0.5 }),
            a => a.Codigo == "V12");
    }

    // ------------------------------------------------------------------ gerenciador de pavimentos (§15)

    private static EstruturaPavimento Estrutura() => new()
    {
        Fonte = "projeto",
        Camadas =
        {
            new CamadaPavimento { Nome = "Base", Material = "Brita graduada", EspessuraCm = 15 },
            new CamadaPavimento { Nome = "Sub-base", Material = "Concreto", EspessuraCm = 20, Funcao = FuncaoCamada.SubBase },
        },
    };

    [Fact]
    public void Pavimento_EstadoDeCadaCamadaEOrigemDoMaterial()
    {
        var e = Estrutura();
        e.Camadas.Add(new CamadaPavimento { Nome = "", EspessuraCm = 0 });
        var est = GerenciadorPavimento.Estados(e, nome => nome.Equals("concreto", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { 1, 2, 3 }, est.Select(x => x.Ordem));
        Assert.Equal(("ok", OrigemMaterial.SoNoPlugin), (est[0].Estado, est[0].Material));
        Assert.Equal(("ok", OrigemMaterial.Revit), (est[1].Estado, est[1].Material));
        Assert.Equal(("sem nome; espessura ausente ou nula", OrigemMaterial.NaoInformado), (est[2].Estado, est[2].Material));
        Assert.Equal("material do Revit no projeto", GerenciadorPavimento.Nome(OrigemMaterial.Revit));
        Assert.StartsWith("só no SinalizaBIM", GerenciadorPavimento.Nome(OrigemMaterial.SoNoPlugin));
    }

    [Fact]
    public void Pavimento_EsquemaEQuantitativoSoComArea()
    {
        var e = Estrutura();
        var f = GerenciadorPavimento.Esquema(e, 5, "CBUQ");
        Assert.Equal(new[] { (0.0, 5.0), (5.0, 20.0), (20.0, 40.0) }, f.Select(x => (x.TopoCm, x.BaseCm)));
        Assert.Null(f[0].Hachura);
        Assert.Equal("CBUQ – 5 cm", f[0].Rotulo);
        Assert.Equal("Base – 15 cm (Brita graduada)", f[1].Rotulo);
        var q = GerenciadorPavimento.Quantidades(e, 5, 1000);
        Assert.Equal(new[] { ("Revestimento", 50.0), ("Base", 150.0), ("Sub-base", 200.0) }, q.Select(x => (x.Camada, Math.Round(x.Volume, 9))));
        // Sem área medida: nenhum valor (não estima).
        Assert.Empty(GerenciadorPavimento.Quantidades(e, 5, null));
        Assert.Empty(GerenciadorPavimento.Quantidades(e, 5, 0));
    }

    [Fact]
    public void Pavimento_ModelosDoUsuario()
    {
        var lista = new List<ModeloEstruturaPavimento>();
        Assert.Equal("informe um nome para o modelo", GerenciadorPavimento.Salvar(lista, " ", Estrutura()));
        Assert.Equal("o modelo precisa de ao menos uma camada", GerenciadorPavimento.Salvar(lista, "Vazio", new EstruturaPavimento()));
        var ruim = Estrutura();
        ruim.Camadas[0].EspessuraCm = 0;
        Assert.StartsWith("corrija antes", GerenciadorPavimento.Salvar(lista, "Ruim", ruim));
        Assert.Null(GerenciadorPavimento.Salvar(lista, "Via local", Estrutura()));
        Assert.Null(GerenciadorPavimento.Salvar(lista, "Arterial", Estrutura()));
        var outra = Estrutura();
        outra.Camadas.RemoveAt(1);
        Assert.Null(GerenciadorPavimento.Salvar(lista, "via LOCAL", outra));                  // mesmo nome: substitui
        Assert.Equal(new[] { "Arterial", "via LOCAL" }, lista.Select(m => m.Nome));
        Assert.Single(lista[1].Estrutura.Camadas);
        // O modelo guardado é uma cópia: mudar a estrutura de origem não muda o modelo.
        outra.Camadas[0].Nome = "mudou";
        Assert.Equal("Base", lista[1].Estrutura.Camadas[0].Nome);
        // A biblioteca vai e volta nas configurações (arquivo JSON do usuário).
        var arq = Path.Combine(Path.GetTempPath(), $"sv-config-{Guid.NewGuid():N}.json");
        try
        {
            var cfg = new SinalizacaoViaria.Core.Settings.PluginSettings();
            cfg.ModelosEstruturaPavimento.AddRange(lista);
            cfg.Save(arq);
            var lido = SinalizacaoViaria.Core.Settings.PluginSettings.Load(arq);
            Assert.Equal(new[] { "Arterial", "via LOCAL" }, lido.ModelosEstruturaPavimento.Select(m => m.Nome));
            Assert.Equal(15, lido.ModelosEstruturaPavimento[1].Estrutura.Camadas[0].EspessuraCm);
        }
        finally { File.Delete(arq); }
    }

    // ------------------------------------------------------------------ fontes da camada Revit

    private static string Raiz([CallerFilePath] string aqui = "")
    {
        foreach (var inicio in new[] { Path.GetDirectoryName(aqui), AppContext.BaseDirectory })
        {
            var dir = string.IsNullOrEmpty(inicio) ? null : new DirectoryInfo(inicio);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "SinalizacaoViaria.Revit"))) dir = dir.Parent;
            if (dir != null) return dir.FullName;
        }
        throw new DirectoryNotFoundException("raiz do repositório");
    }

    private static string Ler(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz(), "src", "SinalizacaoViaria.Revit" }.Concat(partes).ToArray()));

    [Fact]
    public void JanelaDaVia_NoveSecoesEAcoesReais()
    {
        var xaml = Ler("UI", "RoadWindow.xaml");
        Assert.Equal(new[] { "Identificação", "Geometria", "Perfil", "Seção", "Pavimento", "Terraplenagem", "Drenagem", "Validação", "Resumo" },
            Regex.Matches(xaml, @"<TabItem Header=""([^""]+)""").Select(m => m.Groups[1].Value));
        // Cada aba rola nas duas direções; os campos de antes continuam (os nomes que o código usa).
        Assert.Equal(9, Regex.Matches(xaml, @"<TabItem[^>]*>\s*<ScrollViewer HorizontalScrollBarVisibility=""Auto""").Count);
        foreach (var n in new[] { "CbTemplate", "CbHierarchy", "RbTwoWay", "GridRight", "GridLeft", "GbDetails", "GridWidths", "GridRecess", "CbPavement", "CbConnection",
                     "CbRelief", "RbDraw", "Output", "TbSpeed", "Preview", "TxtSummary", "BtnOk" })
            Assert.Contains($"x:Name=\"{n}\"", xaml);
        // Ações: Validar e Resumo (abas), Aplicar e Cancelar – nenhuma que não exista.
        Assert.Contains("Click=\"ValidateClick\"", xaml);
        Assert.Contains("Click=\"SummaryClick\"", xaml);
        Assert.DoesNotContain("Aplicar e atualizar dependências", xaml);
        Assert.DoesNotMatch(@"""#[0-9A-Fa-f]{3,8}""", xaml);
        var cs = Ler("UI", "RoadWindow.xaml.cs");
        Assert.Contains("public void SetContext(ContextoFormularioVia ctx)", cs);
        Assert.Contains("FormularioVia.Contexto(pav,", Ler("Commands", "EditCommands.cs"));
        Assert.Contains("FormularioVia.Contexto(null,", Ler("Commands", "CreationCommands.cs"));
    }

    // ---------------------------------------------------------------- rede de drenagem (§16)

    private static DrainageDefinition Pv(double x, double z, double y = 0) =>
        new() { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(x, y), Z = z, BoxDepth = 1.6, BoxLength = 1.0, PipeDiameter = 0.4 };

    private static DrainagePipeDefinition Tubo(DrainageDefinition a, DrainageDefinition b, double d, double? fa = null, double? fb = null) =>
        new() { FromId = a.Id, ToId = b.Id, Diameter = d, Z = a.Z, InvertFrom = fa, InvertTo = fb };

    [Fact]
    public void RedeDrenagem_TodoCriterioDoNucleoTemSuaParte()
    {
        // Os critérios que o núcleo emite (DrainageNetwork, DrainageHydraulics, DrenagemDoModelo e a própria tela): nenhum cai na parte errada.
        var nucleo = Path.Combine(Raiz(), "src", "SinalizacaoViaria.Core", "Automation");
        var criterios = new[] { "DrainageNetwork.cs", "DrainageHydraulics.cs", "DrainageModel.cs", "DrainageNetworkScreen.cs" }
            .SelectMany(f => Regex.Matches(File.ReadAllText(Path.Combine(nucleo, f)), @"(?:\.Add\(new|new ProblemaRede)\(\s*[^,()]+,\s*""([^""]+)"",")
                .Select(m => m.Groups[1].Value)).ToHashSet();
        Assert.True(criterios.Count >= 19, string.Join(", ", criterios));
        foreach (var c in criterios)
        {
            Assert.Contains(c, RedeDrenagemTela.CriteriosConhecidos);
            Assert.NotEqual("Veja a descrição.", RedeDrenagemTela.Correcao(new ProblemaRede("x", c, "x", false)));
        }
        ProblemaRede P(string c, bool limite = true) => new("T1", c, "texto", limite);
        Assert.Equal(CategoriaRede.Geometria, RedeDrenagemTela.Categoria(P("sentido", false)));
        Assert.Equal(CategoriaRede.Conectividade, RedeDrenagemTela.Categoria(P("ciclo", false)));
        Assert.Equal(CategoriaRede.Parametros, RedeDrenagemTela.Categoria(P("cobrimento")));
        Assert.Equal(CategoriaRede.Hidraulica, RedeDrenagemTela.Categoria(P("engolimento")));
        Assert.Equal(CategoriaRede.ViasETerreno, RedeDrenagemTela.Categoria(P("talvegue", false)));
        // O nível é o do Validar Modelo (V13): aviso; a divergência da captação com o greide é informação.
        Assert.Equal(NivelSituacao.Aviso, RedeDrenagemTela.Nivel(P("capacidade")));
        Assert.Equal(NivelSituacao.Informacao, RedeDrenagemTela.Nivel(P("sarjeta-modelo", false)));
    }

    [Fact]
    public void RedeDrenagem_SemChuva_SoGeometriaConectividadeEParametros()
    {
        var a = Pv(0, 100);
        var b = Pv(30, 99.5);
        var c = Pv(60, 99);
        var solta = new DrainageDefinition { Type = TipoDrenagem.BocaDeLoboSimples, Position = new Vec2(100, 3), Z = 100 };
        var contra = Tubo(a, b, 0.4, 98.0, 98.5);                                    // fundo de jusante acima do de montante
        var fino = Tubo(b, c, 0.3);                                                  // abaixo dos 400 mm do Manual SUDERHSA
        var laco = new DrainagePipeDefinition { FromId = c.Id, ToId = c.Id, Diameter = 0.4, Z = c.Z };
        var defs = new MarkingDefinition[] { a, b, c, solta, contra, fino, laco };
        var p = ProjetoDrenagem.ManualSuderhsa();
        p.Chuva = null;
        var tela = RedeDrenagemTela.Montar(defs, p, null, temTerreno: false, terrenoNatural: null);

        var geo = tela.Secao(CategoriaRede.Geometria);
        Assert.Equal("1. Consistência geométrica", geo.Titulo);
        Assert.Equal(NivelSituacao.Aviso, geo.Situacao.Nivel);
        Assert.Contains(geo.Itens, i => i.Criterio == "sentido" && i.Ids.Contains(contra.Id) && i.Correcao.Contains("cotas de fundo"));
        Assert.Contains(geo.Notas, n => n.Texto.Contains("fora delas, sem Toposolid, pela reta entre as tampas"));
        var con = tela.Secao(CategoriaRede.Conectividade);
        Assert.Contains(con.Itens, i => i.Criterio == "ligação" && i.Ids.Contains(laco.Id));
        Assert.Contains(con.Notas, n => n.Texto.StartsWith("Sem nenhum trecho: BL"));
        Assert.Contains(con.Notas, n => n.Texto.StartsWith("Lançamento(s)") && n.Texto.Contains("PV-3"));
        var par = tela.Secao(CategoriaRede.Parametros);
        Assert.Contains(EquacaoChuva.FonteManual, par.Escopo);
        Assert.Contains(par.Itens, i => i.Criterio == "diâmetro" && i.Texto.Contains("300 mm") && i.Ids.Contains(fino.Id));
        Assert.Contains(par.Notas, n => n.Nivel == NivelSituacao.Informacao && n.Texto == "Diâmetro mínimo nas galerias: 400 mm.");
        Assert.Contains(par.Notas, n => n.Nivel == NivelSituacao.NaoVerificado && n.Texto.StartsWith("Declividade mínima nas galerias não informada"));
        // Sem a equação de chuva: o cálculo hidráulico e o dimensionamento não foram feitos – e a tela diz isso.
        Assert.Equal(NivelSituacao.NaoVerificado, tela.Secao(CategoriaRede.Hidraulica).Situacao.Nivel);
        Assert.Contains("Sem equação de chuva", tela.Secao(CategoriaRede.Hidraulica).Situacao.Texto);
        Assert.Empty(tela.Secao(CategoriaRede.Hidraulica).Itens);
        Assert.Equal(NivelSituacao.NaoVerificado, tela.Secao(CategoriaRede.Dimensionamento).Situacao.Nivel);
        Assert.Empty(tela.Dimensionamento);
        Assert.Equal(0, tela.Mudam);
        Assert.Contains("nenhum cálculo hidráulico", tela.Resumo);
        Assert.DoesNotContain("dimensionada", tela.Resumo);
        Assert.Contains(tela.Secao(CategoriaRede.ViasETerreno).Notas, n => n.Nivel == NivelSituacao.NaoVerificado && n.Texto.StartsWith("Terreno natural não lido"));
        // Nenhum problema se perde: cada um está numa parte (o dimensionamento tem os seus, que não são do relatório).
        Assert.Equal(tela.Problemas.Count, tela.Secoes.Where(s => s.Categoria != CategoriaRede.Dimensionamento).Sum(s => s.Itens.Count));
        Assert.Equal(new[] { "1. Consistência geométrica", "2. Conectividade da rede", "3. Verificação de parâmetros", "4. Cálculo hidráulico", "5. Dimensionamento",
            "Drenagem × vias e terreno" }, tela.Secoes.Select(s => s.Titulo));
        var texto = RedeDrenagemTela.Texto(tela, T0);
        Assert.Contains("27/10/2026 09:00", texto);
        foreach (var s in tela.Secoes) Assert.Contains(s.Titulo.ToUpperInvariant(), texto);
        Assert.Contains("[não verificado] Sem equação de chuva", texto);
    }

    [Fact]
    public void RedeDrenagem_ComChuva_CalculoHidraulicoEDimensionamentoSeparados()
    {
        var a = Pv(0, 100);
        a.AreaContribuicao = 60000;
        a.CoeficienteEscoamento = 0.8;
        var b = Pv(80, 99.6);
        var c = Pv(140, 99.4);
        var t1 = Tubo(a, b, 0.40, 98.5, 98.1);                                       // 0,5 %: pequeno para a vazão
        var t2 = Tubo(b, c, 1.50, 97.6, 97.3);                                       // grande demais
        var defs = new MarkingDefinition[] { a, b, c, t1, t2 };
        var p = ProjetoDrenagem.ManualSuderhsa();
        var tela = RedeDrenagemTela.Montar(defs, p, null, temTerreno: true, terrenoNatural: null);

        var hid = tela.Secao(CategoriaRede.Hidraulica);
        Assert.Contains("Método racional", hid.Escopo);
        Assert.Contains("Manning", hid.Escopo);
        Assert.Contains("Fendrich", hid.Escopo);
        Assert.Contains(hid.Itens, i => i.Criterio == "capacidade" && i.Ids.Contains(t1.Id));
        Assert.Contains(hid.Notas, n => n.Texto.Contains("a lâmina nos tubos e o remanso não são calculados"));
        Assert.Contains(hid.Notas, n => n.Texto.StartsWith("Vazão máxima"));
        // O dimensionamento é o do "Aplicar": o cálculo refeito com os diâmetros calculados, diante do diâmetro do modelo.
        var dim = DrainageHydraulics.Calcular(defs, p, dimensionar: true);
        var l1 = tela.Dimensionamento.Single(l => l.Id == t1.Id);
        Assert.Equal(SituacaoDiametro.Aumentar, l1.Situacao);
        Assert.Equal(dim.Trechos.Single(t => t.Id == t1.Id).DiametroNecessario, l1.DiametroCalculado);
        Assert.Equal(0.40, l1.DiametroModelo);
        var l2 = tela.Dimensionamento.Single(l => l.Id == t2.Id);
        Assert.Equal(SituacaoDiametro.Reduzir, l2.Situacao);
        Assert.Equal(2, tela.Mudam);
        var sec = tela.Secao(CategoriaRede.Dimensionamento);
        Assert.Equal(NivelSituacao.Aviso, sec.Situacao.Nivel);
        Assert.Equal("1 trecho(s) abaixo do calculado, 1 acima.", sec.Situacao.Texto);
        Assert.Contains(sec.Itens, i => i.Nivel == NivelSituacao.Aviso && i.Ids.Contains(t1.Id) && i.Texto.Contains("Ø 400 mm no modelo"));
        Assert.Contains(sec.Itens, i => i.Nivel == NivelSituacao.Informacao && i.Ids.Contains(t2.Id) && i.Correcao.Contains("mantenha o do modelo"));
        Assert.Contains(sec.Notas, n => n.Texto.Contains("responsabilidade técnica"));
        Assert.Contains("Cálculo hidráulico feito pelo método racional; 2 trecho(s) com diâmetro diferente do calculado.", tela.Resumo);
        Assert.Contains(tela.Secao(CategoriaRede.Geometria).Notas, n => n.Texto.Contains("o terreno projetado (Toposolid)"));
        // Aplicados os diâmetros, nada muda mais.
        t1.Diameter = l1.DiametroCalculado!.Value;
        t2.Diameter = l2.DiametroCalculado!.Value;
        var depois = RedeDrenagemTela.Montar(defs, p, null, true, null);
        Assert.Equal(0, depois.Mudam);
        Assert.Equal(NivelSituacao.Ok, depois.Secao(CategoriaRede.Dimensionamento).Situacao.Nivel);
        Assert.DoesNotContain(depois.Secao(CategoriaRede.Hidraulica).Itens, i => i.Criterio == "capacidade");
    }

    [Fact]
    public void TelaDaRede_NaJanelaENoComando()
    {
        var cmd = Ler("Commands", "DrainageNetworkCommands.cs");
        var rede = cmd[cmd.IndexOf("public sealed class CmdRedeDrenagem", StringComparison.Ordinal)..cmd.IndexOf("public sealed class CmdMemorialDrenagem", StringComparison.Ordinal)];
        Assert.Contains("RedeDrenagemTela.Montar(defs, p, superficie, terreno != null, TerrainModel.Ground(doc))", rede);
        Assert.Contains("new DrainageNetworkWindow(tela, path, DateTime.Now)", rede);
        Assert.Contains("uidoc.ShowElements(elems)", rede);
        Assert.Contains("return ApplyDiameters(uidoc, defs, p);", rede);
        Assert.DoesNotContain("AddCommandLink", rede);                             // a caixa de diálogo de antes saiu
        var w = Ler("UI", "DrainageNetworkWindow.cs");
        foreach (var t in new[] { "foreach (var s in tela.Secoes)", "\"Selecionar no modelo\"", "Aplicar os diâmetros calculados", "RedeDrenagemTela.Texto(",
                     "\"Perfil\"", "\"Dispositivos\"", "Trechos medidos", "Planilha das galerias", "MessageBox.Show(this" })
            Assert.Contains(t, w);
        Assert.DoesNotMatch(@"""#[0-9A-Fa-f]{3,8}""", w);
    }

    [Fact]
    public void GerenciadorDePavimento_NaJanelaENoComando()
    {
        var cmd = Ler("Commands", "PavementStructureCommands.cs");
        Assert.Contains("new PavementManagerWindow(", cmd);
        Assert.Contains("PluginContext.Settings.ModelosEstruturaPavimento", cmd);
        Assert.DoesNotContain("EstruturaPavimento.Ler(", cmd);               // a tabela substituiu o texto "nome; espessura; …"
        var w = Ler("UI", "PavementManagerWindow.cs");
        foreach (var t in new[] { "\"+ Camada\"", "\"Remover\"", "\"▲ Subir\"", "\"▼ Descer\"", "GerenciadorPavimento.Estados(", "GerenciadorPavimento.Esquema(",
                     "GerenciadorPavimento.Quantidades(", "GerenciadorPavimento.Salvar(" })
            Assert.Contains(t, w);
    }
}
