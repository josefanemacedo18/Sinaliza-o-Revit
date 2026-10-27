using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 9 do plano – design system e faixa de opções por tema (Prompt de Interface §5, §7, §22–24; D-027): os botões da
/// faixa (cada comando uma vez, nos painéis do fluxo, com ícone próprio, dica curta e disponibilidade coerente), os tokens de
/// cor iguais no XAML e no código, as janelas sem cores soltas e com todas as chaves de estilo definidas, o Diagnóstico do
/// Terreno (o mesmo V14 do Validar Modelo) e o manual com todos os rótulos. Lê as fontes da camada Revit como texto.
/// </summary>
public class Etapa09InterfaceTests
{
    /// <summary>Comandos fora da faixa de propósito: o Autoteste abre por Configurações; os outros dois são código morto (K4).</summary>
    private static readonly string[] ForaDaFaixa = { "CmdAreaConflito", "CmdNovaVia", "CmdAutoteste" };

    private static readonly string[] PaineisPrincipais =
        { "Projeto", "Terreno", "Sistema Viário", "Interseções", "Terraplenagem", "Pavimentação", "Drenagem", "Urbanismo", "Análise", "Documentação", "Sistema" };

    private static readonly string[] PaineisSinalizacao = { "Sinalização Horizontal", "Sinalização Vertical", "Segurança Viária" };

    private sealed record Botao(string Comando, string Rotulo, string Icone, string Dica);

    /// <summary>Raiz do repositório pelo caminho deste arquivo (vale também com o teste compilado fora da árvore).</summary>
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

    private static List<Botao> Botoes() =>
        Regex.Matches(Ler("RibbonBuilder.cs"), @"Data\(typeof\((\w+)\),\s*""([^""]+)"",\s*""([^""]+)"",\s*""((?:[^""\\]|\\.)*)""")
            .Select(m => new Botao(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, Regex.Unescape(m.Groups[4].Value))).ToList();

    private static List<(string Guia, string Painel)> Paineis() =>
        Regex.Matches(Ler("RibbonBuilder.cs"), @"Panel\(app, (\w+), ""([^""]+)""\)").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();

    /// <summary>Todas as classes Cmd* da camada Revit (comandos: CommandBase, LinearCommandBase ou derivados).</summary>
    private static HashSet<string> Comandos() =>
        Directory.GetFiles(Revit(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\bclass (Cmd\w+)").Select(m => m.Groups[1].Value)).ToHashSet();

    [Fact]
    public void Faixa_CadaComandoUmaVez()
    {
        var botoes = Botoes();
        var comandos = Comandos();
        Assert.True(botoes.Count >= 99, $"só {botoes.Count} botões lidos da faixa");
        var repetidos = botoes.GroupBy(b => b.Comando).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(repetidos.Count == 0, "comando repetido na faixa: " + string.Join(", ", repetidos));
        var faltam = comandos.Except(botoes.Select(b => b.Comando)).Except(ForaDaFaixa).OrderBy(x => x).ToList();
        Assert.True(faltam.Count == 0, "comando fora da faixa: " + string.Join(", ", faltam));
        Assert.All(botoes, b => Assert.Contains(b.Comando, comandos));
        Assert.DoesNotContain(botoes, b => ForaDaFaixa.Contains(b.Comando));
        // Rótulos também únicos: o mesmo nome em dois botões confunde quem procura.
        Assert.Equal(botoes.Count, botoes.Select(b => b.Rotulo).Distinct().Count());
        Assert.Contains(botoes, b => b.Comando == "CmdDiagnosticoTerreno");
    }

    [Fact]
    public void Faixa_PaineisPorTema()
    {
        var p = Paineis();
        Assert.Equal(PaineisPrincipais, p.Where(x => x.Guia == "TabName").Select(x => x.Painel));
        Assert.Equal(PaineisSinalizacao, p.Where(x => x.Guia == "SignTab").Select(x => x.Painel));
        Assert.Equal(PaineisPrincipais.Length + PaineisSinalizacao.Length, p.Count);
        var rb = Ler("RibbonBuilder.cs");
        Assert.Contains("TabName = \"SinalizaBIM\"", rb);
        Assert.Contains("SignTab = \"SinalizaBIM Sinalização\"", rb);
        Assert.DoesNotContain("InfraTab", rb);
        // Botões nos painéis do tema: amostra de cada um.
        string PainelDe(string cmd)
        {
            var i = rb.IndexOf($"typeof({cmd})", StringComparison.Ordinal);
            Assert.True(i > 0, cmd);
            return Regex.Matches(rb[..i], @"Panel\(app, \w+, ""([^""]+)""\)").Last().Groups[1].Value;
        }
        Assert.Equal("Projeto", PainelDe("CmdAtualizarTodas"));
        Assert.Equal("Terreno", PainelDe("CmdImportarImagem"));
        Assert.Equal("Sistema Viário", PainelDe("CmdPerfilVia"));
        Assert.Equal("Sistema Viário", PainelDe("CmdViaduto"));
        Assert.Equal("Interseções", PainelDe("CmdNoViario"));
        Assert.Equal("Terraplenagem", PainelDe("CmdMuroArrimo"));
        Assert.Equal("Pavimentação", PainelDe("CmdEstruturaPavimento"));
        Assert.Equal("Drenagem", PainelDe("CmdCaptacoesDoModelo"));
        Assert.Equal("Urbanismo", PainelDe("CmdMasterplan"));
        Assert.Equal("Análise", PainelDe("CmdValidarModelo"));
        Assert.Equal("Documentação", PainelDe("CmdMemorialDrenagem"));
        Assert.Equal("Sistema", PainelDe("CmdSobre"));
        Assert.Equal("Sinalização Horizontal", PainelDe("CmdApagarTrecho"));
        Assert.Equal("Sinalização Vertical", PainelDe("CmdQuadroLegenda"));
        Assert.Equal("Segurança Viária", PainelDe("CmdCruzamentoFerroviario"));
    }

    [Fact]
    public void Faixa_IconeProprioDesenhado()
    {
        var botoes = Botoes();
        var desenhos = Regex.Matches(Ler("Infrastructure", "IconFactory.cs"), @"case ""(\w+)"":").Select(m => m.Groups[1].Value).ToHashSet();
        var semDesenho = botoes.Where(b => !desenhos.Contains(b.Icone)).Select(b => $"{b.Rotulo} ({b.Icone})").ToList();
        Assert.True(semDesenho.Count == 0, "ícone sem desenho (cai no 'i' padrão): " + string.Join(", ", semDesenho));
        var repetidos = botoes.GroupBy(b => b.Icone).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(" / ", g.Select(b => b.Rotulo))}").ToList();
        Assert.True(repetidos.Count == 0, "o mesmo ícone em botões diferentes: " + string.Join("; ", repetidos));
    }

    [Fact]
    public void Faixa_DicasCurtasEDisponibilidadeCoerente()
    {
        foreach (var b in Botoes())
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Dica), b.Rotulo);
            Assert.True(b.Dica.Length <= 120, $"{b.Rotulo}: dica com {b.Dica.Length} caracteres – o detalhe vai na descrição longa");
            Assert.EndsWith(".", b.Dica);
        }
        var rb = Ler("RibbonBuilder.cs");
        Assert.Contains("AvailabilityClassName = (NoDocumentRequiredAttribute.On(cmd) ? typeof(AlwaysAvailable) : typeof(ProjectDocumentAvailability)).FullName", rb);
        Assert.Contains("d.LongDescription = details", rb);
        var disp = Ler("Commands", "CommandAvailability.cs");
        Assert.Matches(@"class ProjectDocumentAvailability : IExternalCommandAvailability", disp);
        Assert.Contains("ActiveUIDocument?.Document is { IsFamilyDocument: false }", disp);
        Assert.Matches(@"class AlwaysAvailable : IExternalCommandAvailability", disp);
        // Os comandos que rodam sem projeto são os mesmos para o CommandBase e para a faixa (o atributo é a única fonte).
        var semDocumento = Directory.GetFiles(Revit("Commands"), "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\[NoDocumentRequired\]\s*public sealed class (\w+)").Select(m => m.Groups[1].Value))
            .OrderBy(x => x).ToList();
        Assert.Equal(new[] { "CmdCatalogo", "CmdConfiguracoes", "CmdSobre" }, semDocumento);
        Assert.Contains("private bool RequiresDocument => !NoDocumentRequiredAttribute.On(GetType());", Ler("Commands", "CommandBase.cs"));
        Assert.DoesNotContain(Directory.GetFiles(Revit("Commands"), "*.cs"), f => File.ReadAllText(f).Contains("override bool RequiresDocument"));
    }

    private static Dictionary<string, string> CoresDoXaml() =>
        Regex.Matches(Ler("UI", "Styles.xaml"), @"<SolidColorBrush x:Key=""(\w+)"" Color=""(#[0-9A-Fa-f]{6})""/>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant());

    [Fact]
    public void DesignSystem_TokensIguaisNoXamlENoCodigo()
    {
        var cs = Ler("UI", "UiTokens.cs");
        var topo = cs[..cs.IndexOf("public static class Indicadores", StringComparison.Ordinal)];
        var codigo = Regex.Matches(topo, @"public static readonly SolidColorBrush (\w+) = Make\(""(#[0-9A-Fa-f]{6})""\)")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant());
        var xaml = CoresDoXaml();
        Assert.True(codigo.Count >= 20, $"só {codigo.Count} tokens no código");
        Assert.Equal(codigo.OrderBy(k => k.Key), xaml.OrderBy(k => k.Key));
        // As chaves antigas continuam (janelas e código que já as usam) e os nomes antigos têm o mesmo valor dos novos.
        foreach (var k in new[] { "Accent", "AccentDark", "Muted", "Panel", "Border", "Warn" }) Assert.True(xaml.ContainsKey(k), k);
        Assert.Equal(xaml["TextSecondary"], xaml["Muted"]);
        Assert.Equal(xaml["Warning"], xaml["Warn"]);
    }

    /// <summary>Contraste (WCAG 2.x) entre duas cores #RRGGBB.</summary>
    private static double Contraste(string a, string b)
    {
        static double L(string h)
        {
            double C(int i)
            {
                var c = Convert.ToInt32(h.Substring(1 + 2 * i, 2), 16) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * C(0) + 0.7152 * C(1) + 0.0722 * C(2);
        }
        var (la, lb) = (L(a), L(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void DesignSystem_ContrasteDoTexto()
    {
        // Interface §23: texto legível (≥ 4,5 : 1) sobre a superfície e o painel; texto sobre a cor primária e os estados.
        var c = CoresDoXaml();
        foreach (var texto in new[] { "Text", "TextSecondary", "Accent", "AccentDark", "Success", "Warning", "Error" })
            foreach (var fundo in new[] { "Surface", "Panel" })
                Assert.True(Contraste(c[texto], c[fundo]) >= 4.5, $"{texto} sobre {fundo}: {Contraste(c[texto], c[fundo]):0.00}");
        Assert.True(Contraste(c["TextOnAccent"], c["Accent"]) >= 4.5);
        Assert.True(Contraste(c["TextOnAccent"], c["Error"]) >= 4.5);
        Assert.True(Contraste(c["Warning"], c["WarningBackground"]) >= 4.5);
        Assert.True(Contraste(c["Error"], c["ErrorBackground"]) >= 4.5);
        Assert.True(Contraste(c["Success"], c["SuccessBackground"]) >= 4.5);
        // Desabilitado: distinguível, mas não precisa do contraste do texto (WCAG 1.4.3 o isenta).
        Assert.True(Contraste(c["Disabled"], c["Surface"]) >= 3);
    }

    [Fact]
    public void DesignSystem_ComponentesEFoco()
    {
        var xaml = Ler("UI", "Styles.xaml");
        foreach (var k in new[] { "SvWindow", "Title", "SectionTitle", "Subtitle", "Hint", "Caption", "FieldLabel", "Value", "ValueLarge", "ValidationText",
                     "StatusOk", "StatusWarning", "StatusError", "StatusInfo", "StatusUnknown", "Card", "BannerInfo", "BannerWarning", "BannerError",
                     "BannerSuccess", "Primary", "Secondary", "Danger", "FocusVisual", "CardPadding", "SectionMargin" })
            Assert.True(xaml.Contains($"x:Key=\"{k}\""), k);
        foreach (var t in new[] { "DataGrid", "DataGridColumnHeader", "ProgressBar", "Expander", "ToolTip", "TextBox", "Button", "CheckBox", "ComboBox" })
            Assert.Matches($@"<Style TargetType=""{t}"">", xaml);
        // Foco de teclado visível nos controles (Interface §23) e valores com algarismos alinhados.
        Assert.Contains("<Setter Property=\"FocusVisualStyle\" Value=\"{StaticResource FocusVisual}\"/>", xaml);
        Assert.Contains("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\">", xaml);
        Assert.Contains("Typography.NumeralAlignment", xaml);
        // Contêineres sem cor de texto própria: o conteúdo herda a do texto (um expansor com cor pintaria as linhas da tabela
        // agrupada dos Quantitativos).
        foreach (var t in new[] { "Expander", "GroupBox", "TabItem", "DataGrid", "ListBox" })
        {
            var m = Regex.Match(xaml, $@"<Style TargetType=""{t}"">(.*?)</Style>", RegexOptions.Singleline);
            Assert.True(m.Success, t);
            Assert.DoesNotContain("\"Foreground\"", m.Groups[1].Value);
        }
    }

    [Fact]
    public void Janelas_ChavesDeEstiloDefinidas()
    {
        // StaticResource sem a chave só falha ao abrir a janela no Revit: aqui cada chave usada tem de existir em Styles.xaml
        // ou na própria janela, e em Styles.xaml cada recurso é definido antes de ser usado.
        var estilos = Ler("UI", "Styles.xaml");
        var globais = Regex.Matches(estilos, @"x:Key=""(\w+)""").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (Match m in Regex.Matches(estilos, @"\{StaticResource (\w+)\}"))
        {
            var def = estilos.IndexOf($"x:Key=\"{m.Groups[1].Value}\"", StringComparison.Ordinal);
            Assert.True(def >= 0 && def < m.Index, $"Styles.xaml: {m.Groups[1].Value} usado antes de definido");
        }
        var faltas = new List<string>();
        foreach (var f in Directory.GetFiles(Revit("UI"), "*.xaml"))
        {
            var t = File.ReadAllText(f);
            var locais = Regex.Matches(t, @"x:Key=""(\w+)""").Select(m => m.Groups[1].Value).ToHashSet();
            var usadas = Regex.Matches(t, @"\{StaticResource (\w+)\}|<StaticResource ResourceKey=""(\w+)""").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            faltas.AddRange(usadas.Where(k => !globais.Contains(k) && !locais.Contains(k)).Distinct().Select(k => $"{Path.GetFileName(f)}: {k}"));
        }
        foreach (var f in Directory.GetFiles(Revit(), "*.cs", SearchOption.AllDirectories))
            faltas.AddRange(Regex.Matches(File.ReadAllText(f), @"(?:FindResource|TryFindResource)\(""(\w+)""\)|SetResourceReference\([^,]+,\s*""(\w+)""\)")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Where(k => !globais.Contains(k)).Select(k => $"{Path.GetFileName(f)}: {k}"));
        Assert.True(faltas.Count == 0, "chave de estilo inexistente: " + string.Join(", ", faltas));
    }

    [Fact]
    public void Janelas_SemCoresSoltas()
    {
        var faltas = new List<string>();
        foreach (var f in Directory.GetFiles(Revit("UI"), "*.xaml").Where(f => Path.GetFileName(f) != "Styles.xaml"))
        {
            var t = File.ReadAllText(f);
            faltas.AddRange(Regex.Matches(t, @"""#[0-9A-Fa-f]{6,8}""").Select(m => $"{Path.GetFileName(f)}: {m.Value}"));
            faltas.AddRange(Regex.Matches(t, @"(Foreground|Background|BorderBrush)=""(White|Black|Gray|Red|Green|Blue|DarkRed|SaddleBrown)""").Select(m => $"{Path.GetFileName(f)}: {m.Value}"));
        }
        // Código das janelas: as cores de interface vêm de UiTokens. Os desenhos (mapa, pré-visualização, miniaturas, ícones)
        // têm a sua paleta de dados e ficam fora.
        var desenhos = new[] { "TrafficMap.cs", "GeometryPreview.cs", "QuantityThumbnails.cs", "SignThumbnails.cs" };
        var cromaticas = new[] { "0x5F, 0x6B, 0x7A", "0x17, 0x4A, 0x83", "0xD5, 0xDB, 0xE3", "0x1F, 0x5F, 0xA8", "0x3A, 0x44, 0x52", "0xF7, 0xF9, 0xFB", "0xE8, 0xF1, 0xFB", "90, 96, 106" };
        foreach (var f in Directory.GetFiles(Revit("UI"), "*.cs").Where(f => !desenhos.Contains(Path.GetFileName(f)) && Path.GetFileName(f) != "UiTokens.cs"))
        {
            var t = File.ReadAllText(f);
            faltas.AddRange(cromaticas.Where(t.Contains).Select(c => $"{Path.GetFileName(f)}: FromRgb({c})"));
            faltas.AddRange(Regex.Matches(t, @"""#[0-9A-Fa-f]{6}""").Select(m => $"{Path.GetFileName(f)}: {m.Value}"));
            faltas.AddRange(Regex.Matches(t, @"Brushes\.(Gray|DarkRed|SaddleBrown|DimGray)\b").Select(m => $"{Path.GetFileName(f)}: {m.Value}"));
        }
        Assert.True(faltas.Count == 0, "cor fora dos tokens: " + string.Join(", ", faltas));
        Assert.Contains("UiTokens.Indicadores.Pintura", Ler("UI", "QuantitiesWindow.xaml.cs"));
        Assert.Contains("UiTokens.Error", Ler("UI", "RoadWindow.xaml.cs"));
    }

    // ------------------------------------------------------------------ Diagnóstico do Terreno

    /// <summary>Plano com 10 % de declividade em x (cotas de 100 a 110 m) sobre 100 × 40 m, malha de 10 m.</summary>
    private static TerrainMesh Plano() => TerrainMesh.FromFunction(new Vec2(0, -20), new Vec2(100, 20), 10, p => 100 + 0.1 * p.X);

    /// <summary>Via de 200 m que molda o terreno: a metade além de x = 100 fica fora da superfície.</summary>
    private static RoadPavementDefinition Via()
    {
        var v = new RoadPavementDefinition
        {
            PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(200, 0) }, 100), GroupId = "g-" + Guid.NewGuid().ToString("N"),
            LeftWidth = 3.5, RightWidth = 3.5,
        };
        v.Output.Grade = new RoadGrade { DefaultCurve = 60, AdjustTerrain = true };
        foreach (var (s, z) in new[] { (0.0, 100.0), (200.0, 104.0) }) v.Output.Grade.Points.Add(new GradePoint(s, z));
        return v;
    }

    [Fact]
    public void DiagnosticoTerreno_ResumoDaSuperficie()
    {
        var r = TerrainDiagnostics.Resumir(Plano());
        Assert.Equal(80, r.Triangulos);
        Assert.Equal(55, r.Pontos);
        Assert.Equal(4000, r.AreaPlanta, 6);
        Assert.Equal(100, r.CotaMinima!.Value, 9);
        Assert.Equal(110, r.CotaMaxima!.Value, 9);
        Assert.Equal(10, r.Desnivel!.Value, 9);
        Assert.Equal(new Vec2(0, -20), r.Min);
        Assert.Equal(new Vec2(100, 20), r.Max);
        var vazio = TerrainDiagnostics.Resumir(new TerrainMesh());
        Assert.Equal(0, vazio.Triangulos);
        Assert.Null(vazio.CotaMinima);
        Assert.Null(vazio.Desnivel);
    }

    [Fact]
    public void DiagnosticoTerreno_MesmoV14DoValidarModelo()
    {
        var via = Via();
        var mesh = Plano();
        var crit = new GeometricCriteria { DeclividadeMaximaTerreno = 5, Fonte = "projeto de teste" };
        var topo = ModelValidator.Topografia(new MarkingDefinition[] { via }, mesh, crit);
        var copias = new List<CopiaGravada> { new(via.Id, via.ToJson(), "e1") };
        static string K(Achado a) => $"{a.Codigo}|{a.Severidade}|{a.Mensagem}|{string.Join(",", a.Ids)}";
        Assert.Equal(ModelValidator.Validate(copias, criterios: crit, incluirNaoVerificados: true, terreno: mesh).Where(a => a.Codigo == "V14").Select(K).OrderBy(x => x),
            topo.Select(K).OrderBy(x => x));
        // Faces de 10 % acima do limite de 5 % e a metade da via fora da superfície (com a via indicada).
        Assert.Contains(topo, a => a.Severidade == Severidade.Aviso && a.Mensagem.Contains("80 face(s) mais íngremes que 5 % (até 10 %)"));
        var fora = Assert.Single(topo, a => a.Mensagem.Contains("fora da superfície"));
        Assert.Equal(new[] { via.Id }, fora.Ids);
        Assert.DoesNotContain(topo, a => a.Severidade == Severidade.NaoVerificavel);
        // Sem a declividade informada: a declividade fica não verificada, como no Validar Modelo.
        var sem = ModelValidator.Topografia(new MarkingDefinition[] { via }, mesh, new GeometricCriteria());
        Assert.Contains(sem, a => a.Severidade == Severidade.NaoVerificavel && a.Mensagem.StartsWith("Declividade máxima do terreno não informada"));
        Assert.DoesNotContain(sem, a => a.Mensagem.Contains("íngremes"));
        Assert.Equal(ModelValidator.Validate(copias, criterios: new GeometricCriteria(), incluirNaoVerificados: true, terreno: mesh).Where(a => a.Codigo == "V14").Select(K).OrderBy(x => x),
            sem.Select(K).OrderBy(x => x));
        // Sem superfície (Toposolid sem faces lidas): o mesmo erro de dados insuficientes.
        Assert.Contains(ModelValidator.Topografia(Array.Empty<MarkingDefinition>(), new TerrainMesh(), crit),
            a => a.Severidade == Severidade.Erro && a.Mensagem.Contains("não há superfície do terreno"));
    }

    [Fact]
    public void DiagnosticoTerreno_Relatorio()
    {
        var mesh = Plano();
        var crit = new GeometricCriteria { DeclividadeMaximaTerreno = 5, Fonte = "projeto de teste" };
        var achados = ModelValidator.Topografia(new MarkingDefinition[] { Via() }, mesh, crit);
        var t = TerrainDiagnostics.Relatorio(1, TerrainDiagnostics.Resumir(mesh), 0, null, crit.DeclividadeMaximaTerreno, crit.FonteTexto, achados);
        Assert.Contains("SUPERFÍCIE PROJETADA (o Toposolid como está agora)", t);
        Assert.Contains("• 1 Toposolid(s) principal(is) no projeto.", t);
        Assert.Contains("80 triângulo(s), 55 ponto(s) distinto(s) em planta, 4.000,0 m² em planta", t);
        Assert.Contains("Cotas de 100,00 a 110,00 m (desnível de 10,00 m); retângulo em planta de 100,0 × 40,0 m.", t);
        Assert.Contains("Nenhum Toposolid tem o terreno original guardado", t);
        Assert.Contains("• 5 % – projeto de teste.", t);
        Assert.Contains("[AVISO] Terreno: 80 face(s) mais íngremes que 5 % (até 10 %)", t);
        Assert.EndsWith("Nada foi alterado no projeto.", t);
        // Com o original guardado em 1 de 2 Toposolids e sem limite informado.
        var original = TerrainMesh.FromFunction(new Vec2(0, -20), new Vec2(100, 20), 10, p => 101);
        var t2 = TerrainDiagnostics.Relatorio(2, TerrainDiagnostics.Resumir(mesh), 1, TerrainDiagnostics.Resumir(original), null, new GeometricCriteria().FonteTexto,
            ModelValidator.Topografia(Array.Empty<MarkingDefinition>(), mesh, new GeometricCriteria()));
        Assert.Contains("• Guardado em 1 de 2 Toposolid(s).", t2);
        Assert.Contains("Cotas de 101,00 a 101,00 m (desnível de 0,00 m)", t2);
        Assert.Contains("Não informada: a declividade das faces não foi conferida", t2);
        Assert.Contains("[NÃO VERIFICADO] Declividade máxima do terreno não informada", t2);
        Assert.Contains("• Nenhum problema nas verificações que rodaram.", t2);
    }

    [Fact]
    public void DiagnosticoTerreno_ComandoSomenteLeitura()
    {
        var cmd = Ler("Commands", "TerrainDiagnosticCommands.cs");
        Assert.Contains("[Transaction(TransactionMode.ReadOnly)]", cmd);
        Assert.Contains("ModelValidator.Topografia(", cmd);
        Assert.Contains("TerrainDiagnostics.Relatorio(", cmd);
        Assert.DoesNotContain("new Transaction(", cmd);
    }

    [Fact]
    public void Manual_TemOsPaineisEOsRotulos()
    {
        var manual = File.ReadAllText(Path.Combine(Raiz(), "docs", "MANUAL.md"));
        var faltam = PaineisPrincipais.Concat(PaineisSinalizacao).Concat(Botoes().Select(b => b.Rotulo)).Where(r => !manual.Contains(r)).ToList();
        Assert.True(faltam.Count == 0, "o manual não cita: " + string.Join(", ", faltam));
        Assert.Contains("SinalizaBIM Sinalização", manual);
        Assert.Contains("Diagnóstico do Terreno", manual);
    }
}
