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
