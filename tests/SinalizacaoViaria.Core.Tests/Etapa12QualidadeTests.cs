using System.Runtime.CompilerServices;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 12 do plano – qualidade, quantitativos e configurações (Prompt de Interface §17–§19, §26). Central de Qualidade: as
/// linhas dos achados do Validar Modelo com o estado diante da validação anterior, a ordem, os filtros, as contagens por
/// categoria e o CSV; e a leitura do relatório do Autoteste (testes do plugin), conferida com os relatórios reais.
/// </summary>
public class Etapa12QualidadeTests
{
    private static readonly DateTime T0 = new(2026, 10, 27, 9, 0, 0);

    private static Achado A(string codigo, Severidade s, params string[] ids) =>
        new(codigo, s, Confianca.Alta, s != Severidade.Info, $"{codigo} em {string.Join(",", ids)}", ids);

    private static Achado NaoVerificado(string codigo, string texto) => new(codigo, Severidade.NaoVerificavel, Confianca.Alta, false, texto, Array.Empty<string>());

    private static string? Nome(string id) => id switch { "a" => "Via V-1", "b" => "Via V-2", "c" => "Quadra Q1", "d" => "Lote 3", _ => null };

    private static ResumoQualidade Montar(IReadOnlyList<Achado> achados, ValidacaoGravada? anterior = null) =>
        CentralQualidade.Montar(achados, anterior, Nome, "Projeto teste", T0, "2.18.0", 4, 9);

    // ------------------------------------------------------------------ Central de Qualidade

    [Fact]
    public void Central_PrimeiraValidacao_SemComparacao()
    {
        var r = Montar(new[] { A("V07", Severidade.Erro, "a"), A("V12", Severidade.Aviso, "b"), NaoVerificado("V14", "Terreno: nenhum Toposolid no projeto.") });
        Assert.Null(r.Anterior);
        Assert.Empty(r.Resolvidos);
        Assert.Equal(new[] { EstadoAchado.PrimeiraValidacao, EstadoAchado.PrimeiraValidacao, EstadoAchado.NaoVerificado }, r.Linhas.Select(l => l.Estado));
        // O "não verificado" não é problema: não tem chave no histórico.
        Assert.Equal("", r.Linhas.Single(l => l.Gravidade == Severidade.NaoVerificavel).Chave);
        Assert.Equal((1, 1, 0, 1), (r.Conta(Severidade.Erro), r.Conta(Severidade.Aviso), r.Conta(Severidade.Info), r.Conta(Severidade.NaoVerificavel)));
        Assert.Equal(("Projeto teste", T0, "2.18.0", 4, 9), (r.Projeto, r.Quando, r.Versao, r.Marcas, r.Elementos));
    }

    [Fact]
    public void Central_ComAnterior_NovoPersistenteEResolvido()
    {
        var antes = new[]
        {
            new Achado("V07", Severidade.Erro, Confianca.Alta, true, "Quadra Q1: contorno com 2 vértices.", new[] { "a" }),
            A("V09", Severidade.Aviso, "c", "d"),
        };
        // Como o comando: a anterior vem do JSON gravado.
        var anterior = ValidationHistory.FromJson(ValidationHistory.ToJson(ValidationHistory.Gravar(antes, T0.AddDays(-1), "2.17.0")));
        var r = Montar(new[]
        {
            new Achado("V07", Severidade.Erro, Confianca.Alta, true, "Quadra Q1: contorno com 1 vértice.", new[] { "a" }),   // mensagem mudou: o mesmo achado
            A("V12", Severidade.Aviso, "b"),
        }, anterior);
        Assert.Equal(T0.AddDays(-1), r.Anterior);
        Assert.Equal(EstadoAchado.Persistente, r.Linhas.Single(l => l.Codigo == "V07").Estado);
        Assert.Equal(EstadoAchado.Novo, r.Linhas.Single(l => l.Codigo == "V12").Estado);
        Assert.Equal("V09", Assert.Single(r.Resolvidos).Codigo);
    }

    [Fact]
    public void Central_AchadosIguais_CadaUmComASuaChave()
    {
        // Dois achados com os mesmos valores (a mesma lista de marcas) são iguais como registro: a chave de cada um vai pela
        // posição na lista, não pelo valor.
        var ids = new[] { "p" };
        var x1 = new Achado("V13", Severidade.Aviso, Confianca.Alta, true, "Trecho T1: declividade abaixo da mínima.", ids);
        var x2 = new Achado("V13", Severidade.Aviso, Confianca.Alta, true, "Trecho T1: declividade abaixo da mínima.", ids);
        Assert.Equal(x1, x2);
        var anterior = ValidationHistory.Gravar(new[] { x1 }, T0.AddDays(-1), null);
        var r = Montar(new[] { x1, x2 }, anterior);
        Assert.Equal(new[] { "V13:p", "V13:p#2" }, r.Linhas.Select(l => l.Chave));
        Assert.Equal(new[] { EstadoAchado.Persistente, EstadoAchado.Novo }, r.Linhas.Select(l => l.Estado));
        // A mesma instância duas vezes também.
        Assert.Equal(new[] { "V13:p", "V13:p#2" }, Montar(new[] { x1, x1 }, anterior).Linhas.Select(l => l.Chave));
    }

    private static readonly Achado[] Varios =
    {
        A("V14", Severidade.Aviso, "a"),              // Topografia
        A("V07", Severidade.Aviso, "c"),              // Geometria
        A("V01", Severidade.Erro, "d"),               // Dados e versões
        A("V13", Severidade.Info, "b"),               // Drenagem
        NaoVerificado("V15", "Terraplenagem: nenhuma aplicada."),
    };

    [Fact]
    public void Central_Ordem_GravidadeCategoriaCodigoEstado()
    {
        var r = Montar(Varios);
        // Padrão: gravidade e, na mesma gravidade, a ordem das categorias (§17.1).
        Assert.Equal(new[] { "V01", "V07", "V14", "V13", "V15" }, r.Linhas.Select(l => l.Codigo));
        Assert.Equal(new[] { "V07", "V14", "V15", "V13", "V01" }, CentralQualidade.Ordenar(r.Linhas, OrdemQualidade.Categoria).Select(l => l.Codigo));
        Assert.Equal(new[] { "V01", "V07", "V13", "V14", "V15" }, CentralQualidade.Ordenar(r.Linhas, OrdemQualidade.Codigo).Select(l => l.Codigo));
        // Estado: primeira validação antes de não verificado; empate pela gravidade e pela ordem do validador.
        Assert.Equal(new[] { "V01", "V14", "V07", "V13", "V15" }, CentralQualidade.Ordenar(r.Linhas, OrdemQualidade.Estado).Select(l => l.Codigo));
    }

    [Fact]
    public void Central_PorCategoria_TodasAsQueTemVerificador()
    {
        // Toda regra do validador tem a sua categoria na ordem da Central (uma regra nova sem lugar cairia no fim).
        Assert.All(ModelValidator.Regras.Values, x => Assert.Contains(x.Modulo, CentralQualidade.Ordem));
        var r = Montar(Varios.Append(A("V99", Severidade.Aviso, "a")).ToArray());
        var modulos = ModelValidator.Regras.Values.Select(x => x.Modulo).Distinct().OrderBy(m => CentralQualidade.Ordem.ToList().IndexOf(m)).ToList();
        // Todas as categorias com regra (zero = conferida sem achado), na ordem; o achado sem regra conhecida vai para "Outros", no fim.
        Assert.Equal(modulos.Append("Outros"), r.PorCategoria.Select(c => c.Categoria));
        Assert.Equal((0, 1, 0, 0), Contagem(r, "Geometria"));
        Assert.Equal((1, 0, 0, 0), Contagem(r, "Dados e versões"));
        Assert.Equal((0, 0, 1, 0), Contagem(r, "Drenagem"));
        Assert.Equal((0, 0, 0, 1), Contagem(r, "Terraplenagem"));
        Assert.Equal((0, 0, 0, 0), Contagem(r, "Interseções"));
        Assert.Equal((0, 1, 0, 0), Contagem(r, "Outros"));
        Assert.Equal(r.Linhas.Count, r.PorCategoria.Sum(c => c.Total));
        // Sem achado de regra desconhecida, não há "Outros".
        Assert.DoesNotContain(Montar(Varios).PorCategoria, c => c.Categoria == "Outros");
    }

    private static (int, int, int, int) Contagem(ResumoQualidade r, string categoria)
    {
        var c = r.PorCategoria.Single(x => x.Categoria == categoria);
        return (c.Erros, c.Avisos, c.Infos, c.NaoVerificados);
    }

    [Fact]
    public void Central_Elementos_NomesDasMarcasEApagadas()
    {
        var r = Montar(new[]
        {
            A("V04", Severidade.Aviso),
            A("V04", Severidade.Aviso, "a", "b"),
            A("V04", Severidade.Aviso, "a", "0123456789abcdef"),
            A("V04", Severidade.Aviso, "a", "b", "c", "d", "a"),
        });
        Assert.Equal(new[] { "—", "Via V-1, Via V-2", "Via V-1, 01234567 (não existe mais)", "Via V-1, Via V-2, Quadra Q1 e mais 1" },
            r.Linhas.OrderBy(l => l.Ordem).Select(l => l.Elementos));
        // As marcas continuam inteiras para a seleção no modelo.
        Assert.Equal(new[] { "a", "b", "c", "d", "a" }, r.Linhas.Single(l => l.Ordem == 4).Ids);
    }

    [Fact]
    public void Central_Filtrar_CategoriaGravidadeEstadoTexto()
    {
        var r = Montar(Varios);
        string[] Codigos(FiltroQualidade f) => CentralQualidade.Filtrar(r.Linhas, f).Select(l => l.Codigo).ToArray();
        Assert.Equal(new[] { "V14" }, Codigos(new FiltroQualidade(Categoria: "Topografia")));
        Assert.Equal(new[] { "V07", "V14" }, Codigos(new FiltroQualidade(Gravidade: Severidade.Aviso)));
        Assert.Equal(new[] { "V15" }, Codigos(new FiltroQualidade(Estado: EstadoAchado.NaoVerificado)));
        // Texto: nos elementos (nome da marca), na ação recomendada e no código, sem diferença de maiúsculas.
        Assert.Equal(new[] { "V14" }, Codigos(new FiltroQualidade(Texto: "via v-1")));
        Assert.Equal(new[] { "V15" }, Codigos(new FiltroQualidade(Texto: "RODAR A TERRAPLENAGEM")));
        Assert.Equal(new[] { "V13" }, Codigos(new FiltroQualidade(Texto: " v13 ")));
        Assert.Equal(r.Linhas.Count, Codigos(new FiltroQualidade(Texto: "   ")).Length);
        Assert.Empty(Codigos(new FiltroQualidade(Categoria: "Topografia", Gravidade: Severidade.Erro)));
    }

    [Fact]
    public void Central_Csv_IdentificacaoECamposInteiros()
    {
        var achados = Varios.Append(new Achado("V12", Severidade.Aviso, Confianca.Media, true, "Via V-2; rampa 9 %\nacima do limite", new[] { "b" })).ToArray();
        var anterior = ValidationHistory.Gravar(new[] { A("V07", Severidade.Aviso, "c") }, new DateTime(2026, 10, 26, 9, 0, 0), "2.17.0");
        var r = Montar(achados, anterior);
        var linhas = CentralQualidade.Csv(r.Linhas, r).TrimEnd().Split(Environment.NewLine);
        Assert.Equal("CENTRAL DE QUALIDADE – Projeto teste", linhas[0]);
        Assert.Equal("Verificação de 27/10/2026 09:00; versão 2.18.0; comparada com a de 26/10/2026 09:00", linhas[1]);
        Assert.Equal("Categoria;Código;Gravidade;Confiança;Revisão humana;Estado;Elementos;Descrição;Ação recomendada", linhas[2]);
        Assert.Equal(r.Linhas.Count, linhas.Length - 3);
        Assert.All(linhas.Skip(2), l => Assert.Equal(9, l.Split(';').Length));   // o ";" e a quebra de linha da mensagem não partem o campo
        Assert.Contains(linhas, l => l.StartsWith("Vias (traçado e greide);V12;AVISO;média;sim;novo;Via V-2;Via V-2, rampa 9 % acima do limite;", StringComparison.Ordinal));
        Assert.Contains(linhas, l => l.StartsWith("Geometria;V07;AVISO;alta;sim;persistente;Quadra Q1;", StringComparison.Ordinal));
        Assert.Contains("primeira validação deste projeto", CentralQualidade.Csv(Montar(Varios).Linhas, Montar(Varios)));
    }

    // ------------------------------------------------------------------ Autoteste (testes do plugin)

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

    private static string Evidencia(string nome) => Path.Combine(Raiz(), "docs", "evidencias", nome);

    private static string Ler(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz(), "src", "SinalizacaoViaria.Revit" }.Concat(partes).ToArray()));

    private const string Revit270 = "Autodesk Revit 2027 2027.0 (build 27.0.4.412)";

    /// <summary>Os relatórios do Autoteste que o usuário enviou (docs/evidencias): o que a Central lê deles.</summary>
    [Theory]
    [InlineData("2026-10-19-autoteste-rapido.txt", "19/10/2026 16:05", "00:03:12", "rápido", 162, "2.0.0.0", Revit270, "Projeto1", 8, 4, 4, 0, null)]
    [InlineData("2026-10-19-autoteste-rapido-2.1.0.txt", "19/10/2026 23:19", "00:03:06", "rápido", 171, "2.1.0.0", Revit270, "Projeto1", 7, 4, 3, 0, null)]
    [InlineData("2026-10-20-autoteste-completo.txt", "20/10/2026 01:45", "01:42:30", "completo", 0, "2.1.0.0", Revit270, "Projeto1", 178, 97, 80, 1,
        "Sinalização vertical › Placas – catálogo completo (168)")]
    [InlineData("2026-10-20-autoteste-completo-2.txt", "20/10/2026 09:44", "01:41:25", "completo", 0, "2.1.0.0", Revit270, "Projeto1", 178, 106, 72, 0, null)]
    [InlineData("2026-10-20-autoteste-completo-3.txt", "20/10/2026 15:12", "01:41:15", "completo", 0, "2.1.0.0", Revit270, "Projeto1", 178, 110, 67, 1,
        "Conexões › Rotatória – Oval")]
    [InlineData("2026-10-21-autoteste-completo-4.txt", "21/10/2026 09:35", "00:36:13", "completo", 0, "2.1.0.0", "Autodesk Revit 2027 2027.3 (build 27.3.0.28)",
        "TESTE PLUGIN", 180, 111, 68, 1, "Conexões › Rotatória – Oval")]
    public void Autoteste_LeOsRelatoriosReais(string arquivo, string data, string duracao, string modo, int naoExecutadas, string plugin, string revit,
        string projeto, int etapas, int ok, int aviso, int erro, string? comErro)
    {
        var caminho = Evidencia(arquivo);
        var r = RelatorioAutoteste.Ler(caminho, File.ReadAllText(caminho));
        Assert.NotNull(r);
        Assert.Equal(caminho, r.Arquivo);
        Assert.Equal(data, r.Data?.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal((duracao, modo, naoExecutadas, plugin, revit, projeto), (r.Duracao, r.Modo, r.NaoExecutadas ?? -1, r.Plugin, r.Revit, r.Projeto));
        Assert.Equal((etapas, ok, aviso, erro), (r.Etapas.Count, r.Ok, r.ComAviso, r.ComErro));
        // As etapas lidas conferem com o resumo escrito pelo próprio Autoteste.
        Assert.StartsWith($"{etapas} etapas: {ok} OK, {aviso} com aviso, {erro} com erro", r.ResumoDoRelatorio);
        // Cada etapa no seu bloco; OK sem mensagens, aviso com pelo menos um ⚠, erro com pelo menos um ✖.
        Assert.All(r.Etapas, e => Assert.False(string.IsNullOrWhiteSpace(e.Bloco)));
        Assert.All(r.Etapas.Where(e => e.Situacao == "OK"), e => Assert.Empty(e.Erros.Concat(e.Avisos)));
        Assert.All(r.Etapas.Where(e => e.Situacao == "AVISO"), e => Assert.NotEmpty(e.Avisos));
        Assert.All(r.Etapas.Where(e => e.Situacao == "ERRO"), e => Assert.NotEmpty(e.Erros));
        Assert.Equal(comErro, r.Etapas.SingleOrDefault(e => e.Situacao == "ERRO") is { } x ? $"{x.Bloco} › {x.Nome}" : null);
    }

    [Fact]
    public void Autoteste_MensagemDaEtapaComErro_PrimeiraLinha()
    {
        var caminho = Evidencia("2026-10-21-autoteste-completo-4.txt");
        var e = RelatorioAutoteste.Ler(caminho, File.ReadAllText(caminho))!.Etapas.Single(x => x.Situacao == "ERRO");
        Assert.Equal((57927L, 285), (e.Ms, e.Elementos));
        // A mensagem de várias linhas fica na primeira (o resto é a pilha da exceção, que está no relatório).
        Assert.Equal("Erro tratado pelo plugin (só ia para o log): [CreateExtrusionGeometry] ArgumentException: Curve loops do not satisfy the input requirements.",
            Assert.Single(e.Erros));
        Assert.NotEmpty(e.Avisos);
        Assert.DoesNotContain(e.Avisos, m => m.StartsWith("Parameter name", StringComparison.Ordinal) || m.Contains("Σ", StringComparison.Ordinal));
    }

    [Fact]
    public void Autoteste_OutroTexto_NaoEhRelatorio()
    {
        foreach (var outro in new[] { "2026-10-20-log-autoteste-completo.txt", "2026-10-20-log-autoteste-completo-3.txt", "2026-10-19-validacao-modelo.txt" })
            Assert.Null(RelatorioAutoteste.Ler(outro, File.ReadAllText(Evidencia(outro))));
        Assert.Null(RelatorioAutoteste.Ler("x.txt", ""));
        // Só o cabeçalho, sem etapas: lido, com zero etapas e modo não informado.
        var vazio = RelatorioAutoteste.Ler("x.txt", "﻿SINALIZABIM – RELATÓRIO DO AUTOTESTE\r\nDETALHES\r\n");
        Assert.NotNull(vazio);
        Assert.Equal(("não informado", 0), (vazio.Modo, vazio.Etapas.Count));
    }

    [Fact]
    public void Autoteste_MaisRecente_PeloNomeDoArquivo()
    {
        const string pasta = "SinalizaBIM";
        var arquivos = new[] { "autoteste-20261020-091500.txt", "autoteste-20261021-085918-dados.jsonl", "validacao-20261022-100000.txt",
            "autoteste-20261021-085918.txt", "qualidade-20261023-080000.csv", "AUTOTESTE-20261019-160500.TXT" }.Select(n => Path.Combine(pasta, n)).ToList();
        Assert.Equal(Path.Combine(pasta, "autoteste-20261021-085918.txt"), RelatorioAutoteste.MaisRecente(arquivos));
        // O nome decide sem diferença de maiúsculas (Windows).
        Assert.Equal(Path.Combine(pasta, "AUTOTESTE-20261025-160500.TXT"), RelatorioAutoteste.MaisRecente(arquivos.Append(Path.Combine(pasta, "AUTOTESTE-20261025-160500.TXT"))));
        Assert.Null(RelatorioAutoteste.MaisRecente(new[] { Path.Combine(pasta, "validacao-20261022-100000.txt"), Path.Combine(pasta, "autoteste-1-dados.jsonl") }));
        Assert.Null(RelatorioAutoteste.MaisRecente(Array.Empty<string>()));
    }

    // ------------------------------------------------------------------ comando e janela

    [Fact]
    public void ValidarModelo_AbreACentralSemAlterarOModelo()
    {
        var cmd = Ler("Commands", "ValidationCommands.cs");
        Assert.Contains("[Transaction(TransactionMode.ReadOnly)]", cmd);
        Assert.DoesNotContain("new Transaction(", cmd);
        // Os mesmos verificadores, o mesmo relatório e o histórico de antes; a Central só mostra.
        Assert.Contains("ModelValidator.Validate(copias,", cmd);
        Assert.Contains("ModelValidator.Report(achados, marcas, copias.Count, doc.Title, now, version, cmp, estados)", cmd);
        Assert.Contains("File.WriteAllText(historyPath, ValidationHistory.ToJson(ValidationHistory.Gravar(achados, now, version)))", cmd);
        Assert.Contains("CentralQualidade.Montar(achados, previous, Nome, doc.Title, now, version, marcas, copias.Count)", cmd);
        Assert.Contains("new QualityCenterWindow(resumo, path, estados, UltimoAutoteste())", cmd);
        Assert.Contains("RelatorioAutoteste.MaisRecente(Directory.GetFiles(PluginPaths.Root, \"autoteste-*.txt\"))", cmd);
        // A seleção escolhida na Central volta ao modelo, com a vista aproximada; a caixa de diálogo antiga saiu.
        Assert.Contains("uidoc.Selection.SetElementIds(ids)", cmd);
        Assert.Contains("uidoc.ShowElements(ids)", cmd);
        Assert.DoesNotContain("AddCommandLink", cmd);
        // Marca que tem elementos mas não se lê não é dita apagada.
        Assert.Contains("(dados ilegíveis)", cmd);
        Assert.Contains("abre a Central de Qualidade", Ler("RibbonBuilder.cs"));
    }

    [Fact]
    public void CentralDeQualidade_AbasFiltrosEAcoesReais()
    {
        var w = Ler("UI", "QualityCenterWindow.cs");
        foreach (var aba in new[] { "\"Diagnóstico do modelo\"", "\"Por categoria\"", "$\"Resolvidos ({r.Resolvidos.Count})\"", "\"Estado dos resultados\"", "\"Autoteste (testes do plugin)\"" })
            Assert.Contains($"Header = {aba}", w);
        foreach (var campo in new[] { "Categoria", "Gravidade", "Estado", "Ordenar por", "Buscar" }) Assert.Contains($"Campo(\"{campo}\"", w);
        // Os filtros e a ordem guardam o valor em cada opção (não a posição): todas as gravidades, estados e ordens têm a sua.
        Assert.DoesNotContain("SelectedIndex switch", w);
        foreach (var n in Enum.GetNames<Severidade>()) Assert.Contains($"Severidade.{n}, \"", w);
        foreach (var n in Enum.GetNames<EstadoAchado>()) Assert.Contains($"EstadoAchado.{n}, \"", w);
        foreach (var n in Enum.GetNames<OrdemQualidade>()) Assert.Contains($"OrdemQualidade.{n}, \"", w);
        Assert.Contains("CentralQualidade.Ordenar(CentralQualidade.Filtrar(_r.Linhas, Filtro()), ordem)", w);
        Assert.Contains("CentralQualidade.Csv(_filtradas, _r)", w);
        Assert.Contains("\"Selecionar no modelo\"", w);
        Assert.Contains("\"Exportar CSV\"", w);
        // Tabelas sem reordenar pelo cabeçalho (números em texto); nenhum botão finge validar de novo.
        Assert.Contains("UiTables.Grid(", w);
        Assert.DoesNotContain("new DataGrid", w);
        Assert.DoesNotContain("Validar de novo", w);
        // O Autoteste fica separado do diagnóstico do modelo.
        Assert.Contains("são testes do plugin, não o diagnóstico deste modelo", w);
    }
}
