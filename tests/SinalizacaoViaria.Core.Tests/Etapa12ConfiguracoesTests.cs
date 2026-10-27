using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Settings;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 12 do plano – gerenciador de configurações (Prompt de Interface §19 e §26): onde fica cada configuração e o que ela
/// afeta, a validação antes de aplicar, o que muda ao salvar, as do projeto e dos elementos, os perfis reutilizáveis (exemplo,
/// usuário, referência citada), a persistência e o tamanho das janelas.
/// </summary>
public class Etapa12ConfiguracoesTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static IEnumerable<string> Materiais => Cat.Materiais.Select(m => m.Nome);
    private static string Todas(IEnumerable<EstadoContexto> l) => string.Join("\n", l.Select(x => $"[{x.Nivel}] {x.Texto}"));

    // ------------------------------------------------------------------ registro

    [Fact]
    public void Registro_DoUsuario_GruposOndeFicaEOQueAfeta()
    {
        var s = new PluginSettings { DefaultSpeed = 50, DefaultMaterial = "Tinta acrílica base água" };
        var itens = RegistroConfiguracoes.DoUsuario(s);
        Assert.All(itens, i => Assert.Equal(OndeFica.Usuario, i.Onde));
        Assert.All(itens, i => Assert.Contains(i.Grupo, RegistroConfiguracoes.Grupos));
        Assert.All(itens, i => Assert.False(string.IsNullOrWhiteSpace(i.Afeta), i.Nome));
        Assert.Equal("50", itens.Single(i => i.Nome == "Velocidade padrão das vias (km/h)").Valor);
        Assert.Equal("Tinta acrílica base água", itens.Single(i => i.Nome == "Material de pintura padrão").Valor);
        Assert.Equal("nenhum limite informado (nada é conferido)", itens.Single(i => i.Nome == "Limites do traçado e do greide").Valor);
        // Unidades: dito como fixo, sem fingir que é configurável.
        Assert.Contains("fixo, não configurável", itens.Single(i => i.Grupo == "Unidades").Afeta);
        // O histórico do Validar Modelo fica neste computador, não no modelo.
        Assert.Contains("não no modelo", itens.Single(i => i.Nome == "Histórico do Validar Modelo").Afeta);
    }

    /// <summary>Memória do último uso de uma ferramenta – não é configuração e não sai no gerenciador.</summary>
    private static readonly string[] SoMemoriaDeUso = { nameof(PluginSettings.LastConnection), nameof(PluginSettings.LastFreeEnds), nameof(PluginSettings.LastCurveRadius), nameof(PluginSettings.LastDrawRoad) };

    [Fact]
    public void Registro_CadaConfiguracaoDoUsuarioApareceNoGerenciador()
    {
        // Mudar qualquer configuração aparece no "o que muda ao salvar" – uma configuração nova sem lugar no registro falha aqui.
        foreach (var p in typeof(PluginSettings).GetProperties().Where(p => p.CanWrite && !SoMemoriaDeUso.Contains(p.Name)))
        {
            var antes = new PluginSettings();
            var depois = antes.Clone();
            switch (p.Name)
            {
                case nameof(PluginSettings.CriteriosGeometricos): depois.CriteriosGeometricos.RaioMinimo = 123; break;
                case nameof(PluginSettings.PerfisCriterios): depois.PerfisCriterios.Add(new PerfilCriterios { Nome = "x" }); break;
                case nameof(PluginSettings.CustomRoadTemplates): depois.CustomRoadTemplates.Add(new CustomRoadTemplate { Name = "x" }); break;
                case nameof(PluginSettings.ModelosEstruturaPavimento): depois.ModelosEstruturaPavimento.Add(new ModeloEstruturaPavimento { Nome = "x" }); break;
                case nameof(PluginSettings.LastUsed): depois.LastUsed["x"] = "y"; break;
                case nameof(PluginSettings.LastRoadSetup): depois.LastRoadSetup = "{}"; break;
                case nameof(PluginSettings.UserCatalogPath): depois.UserCatalogPath = "c.json"; break;
                default:
                    var v = p.GetValue(depois);
                    p.SetValue(depois, v switch
                    {
                        bool b => !b,
                        double d => d + 0.5,
                        string t => t + "x",
                        Enum e => Enum.GetValues(e.GetType()).Cast<object>().First(x => !x.Equals(e)),
                        _ => throw new InvalidOperationException($"{p.Name}: tipo sem teste"),
                    });
                    break;
            }
            Assert.True(RegistroConfiguracoes.Impacto(antes, depois).Count > 0, $"{p.Name} não aparece no gerenciador de configurações");
        }
        Assert.Empty(RegistroConfiguracoes.Impacto(new PluginSettings(), new PluginSettings()));
    }

    [Fact]
    public void Impacto_DizDeQuantoParaQuantoEOQueAfeta()
    {
        var antes = new PluginSettings();
        var depois = antes.Clone();
        depois.DefaultMaterial = "Termoplástico extrudado";
        depois.CriteriosGeometricos.RampaMaxima = 8;
        depois.CriteriosGeometricos.Fonte = "Norma X, tabela 3";
        depois.AutoConnect = false;
        var t = RegistroConfiguracoes.Impacto(antes, depois);
        Assert.Equal(3, t.Count);
        Assert.Contains("Material de pintura padrão: Tinta acrílica base solvente → Termoplástico extrudado – marcas novas e, nos Quantitativos, o consumo das marcas sem material próprio.", t);
        Assert.Contains("Limites do traçado e do greide: nenhum limite informado (nada é conferido) → rampa máx. 8 % – Norma X, tabela 3 – Perfil da Via (verificação) e Validar Modelo (V12) – a próxima verificação.", t);
        Assert.Contains("Ímã de conexão: sim → não – comportamento das próximas edições – nada muda agora.", t);
    }

    // ------------------------------------------------------------------ validação

    [Fact]
    public void Validar_PadraoOk()
    {
        var v = RegistroConfiguracoes.Validar(new PluginSettings(), Materiais, _ => true);
        Assert.Equal("[Ok] Todas as configurações do usuário são válidas.", Todas(v));
    }

    [Fact]
    public void Validar_FaixasCoerenciaMaterialCatalogoEFonte()
    {
        var s = new PluginSettings
        {
            DefaultSpeed = 5, MinModelThickness = 0, ElevationOffset = double.NaN, BoundaryTolerance = 0.2, DefaultMaterial = "Tinta inexistente",
            UserCatalogPath = "nao-existe.json", RoadReliefMaxGrade = 0.5,
        };
        s.CriteriosGeometricos.RampaMinima = 5;
        s.CriteriosGeometricos.RampaMaxima = 3;
        s.CriteriosGeometricos.RaioMinimo = -10;
        var t = Todas(RegistroConfiguracoes.Validar(s, Materiais, f => f != "nao-existe.json"));
        Assert.Contains("[Erro] Velocidade padrão: 5 km/h – use de 10 a 200 km/h.", t);
        Assert.Contains("[Erro] Espessura mínima modelada: 0 m – use de 0,001 a 0,1 m.", t);
        Assert.Contains("[Erro] Elevação adicional padrão: inválido m – use de -10 a 10 m.", t);
        Assert.Contains("[Erro] Desvio máximo dos contornos: 200 mm – use de 0 a 50 mm.", t);
        Assert.Contains("[Erro] Rampa máxima da via nova: 50 % – use de 0,5 a 30 %.", t);
        Assert.Contains("[Erro] Material de pintura padrão \"Tinta inexistente\" não está no catálogo", t);
        Assert.Contains("[Aviso] Catálogo do usuário não encontrado (nao-existe.json)", t);
        Assert.Contains("[Erro] Raio mínimo: -10 m – use de 0 a 100000 m.", t);
        Assert.Contains("[Erro] Rampa mínima (5 %) maior que a máxima (3 %): nenhuma rampa passaria.", t);
        Assert.Contains("[Aviso] Limites geométricos sem fonte", t);
        Assert.DoesNotContain("[Ok]", t);
        // Fonte sem limite: só informação.
        var f = new PluginSettings();
        f.CriteriosGeometricos.Fonte = "Norma X";
        Assert.Equal("[Informacao] Fonte dos limites informada sem nenhum limite: nada é conferido.", Todas(RegistroConfiguracoes.Validar(f, Materiais)));
        // A declividade do terreno sozinha também pede a fonte.
        var d = new PluginSettings();
        d.CriteriosGeometricos.DeclividadeMaximaTerreno = 45;
        Assert.Contains("[Aviso] Limites geométricos sem fonte", Todas(RegistroConfiguracoes.Validar(d, Materiais)));
    }

    // ------------------------------------------------------------------ projeto e elementos

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

    private static string Revit(params string[] partes) => Path.Combine(new[] { Raiz(), "src", "SinalizacaoViaria.Revit" }.Concat(partes).ToArray());
    private static string Ler(params string[] partes) => File.ReadAllText(Revit(partes));

    private static HashSet<string> Comandos() =>
        Directory.GetFiles(Revit(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\bclass (Cmd\w+)").Select(m => m.Groups[1].Value)).ToHashSet();

    [Fact]
    public void DoProjeto_DrenagemTerraplenagemEElementos()
    {
        var sem = RegistroConfiguracoes.DoProjeto(new ProjetoDrenagem(), null, Array.Empty<MarkingDefinition>());
        Assert.Equal("não informada – a vazão não é calculada (só a geometria da rede é conferida)", sem.Single(i => i.Nome == "Equação de chuvas intensas").Valor);
        Assert.Equal("nenhuma – os Quantitativos usam o fator de homogeneização padrão (1,25)", sem.Single(i => i.Nome == "Última terraplenagem aplicada").Valor);
        Assert.Equal("não informada", sem.Single(i => i.Nome == "Fonte dos critérios").Valor);
        Assert.All(sem, i => Assert.Equal(OndeFica.Projeto, i.Onde));
        Assert.DoesNotContain(sem, i => i.Grupo is "Sistema viário" or "Pavimentação" or "Urbanismo");   // sem vias nem loteamentos, nada por elemento

        var v1 = new RoadPavementDefinition { Estaqueamento = new Estaqueamento { Inicio = 100 } };
        v1.Estrutura = new EstruturaPavimento { Camadas = { new CamadaPavimento { Nome = "Base", EspessuraCm = 15 } } };
        var v2 = new RoadPavementDefinition();
        var lot = new LoteamentoDefinition();
        lot.Minimos["Sistema viário"] = 20;
        var reg = new RegistroTerraplenagem { Quando = new DateTime(2026, 10, 26, 8, 30, 0), Subleito = 0.6, TaludeCorte = 1, TaludeAterro = 1.5, Homogeneizacao = 1.3 };
        var com = RegistroConfiguracoes.DoProjeto(new ProjetoDrenagem { Fonte = "Manual X" }, reg, new MarkingDefinition[] { v1, v2, lot });
        Assert.Equal("26/10/2026 08:30; subleito 0,6 m; taludes corte 1 e aterro 1,5; homogeneização 1,30", com.Single(i => i.Nome == "Última terraplenagem aplicada").Valor);
        Assert.Equal("Manual X", com.Single(i => i.Nome == "Fonte dos critérios").Valor);
        Assert.Equal("1 de 2 via(s) com estaqueamento próprio (as demais: distância desde o início)", com.Single(i => i.Nome == "Estaqueamento das vias").Valor);
        Assert.Equal("1 de 2 via(s) com estrutura informada", com.Single(i => i.Nome == "Estrutura do pavimento das vias").Valor);
        Assert.Equal("1 de 1 loteamento(s) com mínimos informados", com.Single(i => i.Nome == "Mínimos e lote mínimo dos loteamentos").Valor);
        Assert.All(com.Where(i => i.Grupo is "Sistema viário" or "Pavimentação" or "Urbanismo" or "Seções-tipo"), i => Assert.Equal(OndeFica.Elemento, i.Onde));
        // Todo comando apontado existe na faixa do plugin.
        var cmds = Comandos();
        Assert.All(com.Concat(RegistroConfiguracoes.DoUsuario(new PluginSettings())), i => Assert.Contains(i.Comando!, cmds));
    }

    // ------------------------------------------------------------------ perfis

    [Fact]
    public void Perfis_ExemploUsuarioEReferenciaCitada()
    {
        // O plugin não traz perfis de limites nem de estrutura: nada que aparente ser norma.
        var padrao = new PluginSettings();
        Assert.Empty(padrao.PerfisCriterios);
        Assert.Empty(padrao.ModelosEstruturaPavimento);
        var secoes = RoadTemplates.All.Select(t => t.Name).ToList();
        var s = new PluginSettings();
        s.CustomRoadTemplates.Add(new CustomRoadTemplate { Name = "Minha rua" });
        s.ModelosEstruturaPavimento.Add(new ModeloEstruturaPavimento { Nome = "Sem fonte", Estrutura = new EstruturaPavimento { Camadas = { new CamadaPavimento { EspessuraCm = 20 } } } });
        s.ModelosEstruturaPavimento.Add(new ModeloEstruturaPavimento { Nome = "Com fonte", Estrutura = new EstruturaPavimento { Fonte = "Projeto de pavimentação, prancha 3" } });
        var c = new GeometricCriteria { RaioMinimo = 50, Fonte = "Norma X, tabela 2, classe III" };
        s.PerfisCriterios.Add(new PerfilCriterios { Nome = "Classe III", Criterios = c });
        s.PerfisCriterios.Add(new PerfilCriterios { Nome = "Rascunho", Criterios = new GeometricCriteria { RampaMaxima = 10 } });
        var p = RegistroConfiguracoes.Perfis(s, secoes);
        Assert.Equal(secoes, p.Where(x => x.Tipo == TipoPerfil.Exemplo).Select(x => x.Nome));
        Assert.All(p.Where(x => x.Tipo == TipoPerfil.Exemplo), x => Assert.Contains("não representa norma", x.Resumo));
        Assert.Equal(TipoPerfil.Usuario, p.Single(x => x.Nome == "Minha rua").Tipo);
        Assert.Equal((TipoPerfil.Usuario, "1 camada(s), 20 cm"), (p.Single(x => x.Nome == "Sem fonte").Tipo, p.Single(x => x.Nome == "Sem fonte").Resumo));
        Assert.Equal((TipoPerfil.ReferenciaCitada, "Projeto de pavimentação, prancha 3"), (p.Single(x => x.Nome == "Com fonte").Tipo, p.Single(x => x.Nome == "Com fonte").Fonte));
        Assert.Equal((TipoPerfil.ReferenciaCitada, "raio mín. 50 m – Norma X, tabela 2, classe III"), (p.Single(x => x.Nome == "Classe III").Tipo, p.Single(x => x.Nome == "Classe III").Resumo));
        Assert.Equal((TipoPerfil.Usuario, "rampa máx. 10 % – fonte não informada pelo usuário"), (p.Single(x => x.Nome == "Rascunho").Tipo, p.Single(x => x.Nome == "Rascunho").Resumo));
        Assert.Equal("referência citada pelo usuário (o plugin não confere)", RegistroConfiguracoes.Nome(TipoPerfil.ReferenciaCitada));
        Assert.Equal("exemplo do plugin (não é norma)", RegistroConfiguracoes.Nome(TipoPerfil.Exemplo));
    }

    // ------------------------------------------------------------------ persistência

    [Fact]
    public void Persistencia_SalvaLeEVoltaAoPadraoSemArquivoOuComArquivoRuim()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sv-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            var arq = Path.Combine(dir, "config.json");
            // Sem arquivo: o padrão (estado previsível).
            var semArquivo = PluginSettings.Load(arq);
            Assert.Equal((60.0, "Tinta acrílica base solvente", 0), (semArquivo.DefaultSpeed, semArquivo.DefaultMaterial, semArquivo.PerfisCriterios.Count));
            var s = new PluginSettings { DefaultSpeed = 45 };
            s.CriteriosGeometricos.RampaMaxima = 9;
            s.CriteriosGeometricos.Fonte = "Norma Y";
            s.PerfisCriterios.Add(new PerfilCriterios { Nome = "Y", Criterios = s.CriteriosGeometricos.Clone() });
            s.Set(TamanhosDeJanela.Chave("QualityCenterWindow"), "1300x820");
            s.Save(arq);
            var lido = PluginSettings.Load(arq);
            Assert.Equal((45.0, 9.0, "Norma Y"), (lido.DefaultSpeed, lido.CriteriosGeometricos.RampaMaxima!.Value, lido.CriteriosGeometricos.Fonte));
            Assert.Equal(("Y", 9.0), (lido.PerfisCriterios.Single().Nome, lido.PerfisCriterios.Single().Criterios.RampaMaxima!.Value));
            Assert.Equal("1300x820", lido.Get("janela:QualityCenterWindow"));
            // Arquivo de uma versão anterior, sem os perfis e com um campo desconhecido: lê o que conhece, o resto no padrão.
            File.WriteAllText(arq, "{\"DefaultSpeed\": 70, \"CampoQueNaoExiste\": 1}");
            var antigo = PluginSettings.Load(arq);
            Assert.Equal((70.0, 0), (antigo.DefaultSpeed, antigo.PerfisCriterios.Count));
            Assert.NotNull(antigo.CriteriosGeometricos);
            // Arquivo corrompido: o padrão.
            File.WriteAllText(arq, "{ isto não é json");
            Assert.Equal(60.0, PluginSettings.Load(arq).DefaultSpeed);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Clone_Independente_CopiarDeMantemOObjeto()
    {
        var s = new PluginSettings();
        s.PerfisCriterios.Add(new PerfilCriterios { Nome = "A" });
        var c = s.Clone();
        c.PerfisCriterios.Add(new PerfilCriterios { Nome = "B" });
        c.CriteriosGeometricos.RaioMinimo = 80;
        c.LastUsed["k"] = "v";
        c.DefaultSpeed = 30;
        Assert.Single(s.PerfisCriterios);   // a janela edita a cópia: nada muda antes de salvar
        Assert.Null(s.CriteriosGeometricos.RaioMinimo);
        Assert.False(s.LastUsed.ContainsKey("k"));
        var mesmo = s;
        s.CopiarDe(c);
        Assert.Same(mesmo, s);
        Assert.Equal((30.0, 80.0, 2), (s.DefaultSpeed, s.CriteriosGeometricos.RaioMinimo!.Value, s.PerfisCriterios.Count));
    }

    [Fact]
    public void TamanhosDeJanela_LerEscreverELimpar()
    {
        Assert.Equal("1200x800", TamanhosDeJanela.Escrever(1200.4, 799.6));
        Assert.Equal((1200.0, 800.0), TamanhosDeJanela.Ler("1200x800", 820, 520, 1920, 1040));
        Assert.Null(TamanhosDeJanela.Ler("600x800", 820, 520, 1920, 1040));    // menor que o mínimo: o padrão da janela
        Assert.Equal((1920.0, 1040.0), TamanhosDeJanela.Ler("2560x1400", 820, 520, 1920, 1040));   // maior que a tela: limitado
        foreach (var ruim in new[] { null, "", "abc", "1200", "1200x", "NaNx800", "Infinityx800", "1200x800x3" })
            Assert.Null(TamanhosDeJanela.Ler(ruim, 0, 0, 1920, 1040));
        var d = new Dictionary<string, string> { ["janela:A"] = "1x1", ["janela:B"] = "2x2", ["ultimo"] = "x" };
        Assert.Equal(2, TamanhosDeJanela.Limpar(d));
        Assert.Equal(new[] { "ultimo" }, d.Keys);
    }

    // ------------------------------------------------------------------ janela, comando e tamanho das janelas

    [Fact]
    public void JanelaDeConfiguracoes_AbasValidacaoImpactoEPerfis()
    {
        var xaml = Ler("UI", "SettingsWindow.xaml");
        foreach (var aba in new[] { "Do usuário (este computador)", "Do projeto", "Perfis reutilizáveis", "Onde fica cada configuração" })
            Assert.Contains($"Header=\"{aba}\"", xaml);
        Assert.Contains("x:Name=\"PanelValidacao\"", xaml);
        Assert.Contains("Content=\"Salvar como perfil…\" Click=\"SalvarPerfilClick\"", xaml);
        Assert.Contains("Content=\"Restaurar o tamanho das janelas\" Click=\"RestaurarJanelasClick\"", xaml);
        var cs = Ler("UI", "SettingsWindow.xaml.cs");
        // Salvar: tudo numa cópia, validado, com o que muda – e só então aplicado ao objeto em uso (antes, um campo inválido no
        // meio deixava os anteriores aplicados na sessão).
        var salvar = cs[cs.IndexOf("private void SaveClick", StringComparison.Ordinal)..];
        salvar = salvar[..salvar.IndexOf("// -----", StringComparison.Ordinal)];
        Assert.Contains("c = Candidata();", salvar);
        Assert.Contains("RegistroConfiguracoes.Validar(c, Materiais, File.Exists)", salvar);
        Assert.Contains("RegistroConfiguracoes.Impacto(atual, c)", salvar);
        Assert.Contains("atual.CopiarDe(c);", salvar);
        Assert.True(salvar.IndexOf("atual.CopiarDe(c);", StringComparison.Ordinal) > salvar.IndexOf("RegistroConfiguracoes.Validar(", StringComparison.Ordinal));
        Assert.DoesNotMatch(@"\bs\.DefaultMode = (?!=)", cs);   // nenhuma atribuição direta ao objeto em uso
        Assert.Contains("var c = PluginContext.Settings.Clone();", cs);
        var cmd = Ler("Commands", "ReportCommands.cs");
        Assert.Contains("RegistroConfiguracoes.DoProjeto(DrainageDesignStore.Load(doc), EarthworkRecordStore.Load(doc), MarkingStorage.Definitions(doc))", cmd);
        Assert.Contains("if (w.Comando is { } cmd) return CommandCatalog.Run(cmd, app);", cmd);
        // Tamanho das janelas: no ShowModal, só nas telas da lista – e todas elas existem.
        var ui = Ler("UI", "UiHelpers.cs");
        Assert.Matches(@"ShowModal\(Window w\)\s*\{[^}]*FitScrollViewers\(w\);\s*LembrarTamanho\(w\);", ui);
        var lista = Regex.Match(ui, @"TamanhoLembrado = new\(StringComparer\.Ordinal\)\s*\{(?<l>[^}]*)\}").Groups["l"].Value;
        var nomes = Regex.Matches(lista, @"nameof\((\w+)\)").Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("QualityCenterWindow", nomes);
        Assert.Contains("QuantitiesWindow", nomes);
        var classes = Directory.GetFiles(Revit("UI"), "*.cs").SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\bclass (\w+Window)\b").Select(m => m.Groups[1].Value)).ToHashSet();
        Assert.All(nomes, n => Assert.Contains(n, classes));
    }
}
