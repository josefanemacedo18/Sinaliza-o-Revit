using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 10 do plano – Central do Projeto e painel contextual (Prompt de Interface §8, §9): indicadores com a situação de cada
/// valor (nunca zero para o que não se sabe), a situação do projeto sem "validado" inventado, os módulos, as propriedades da
/// marca selecionada, a seleção múltipla sem edição em lote e, nas fontes da camada Revit, o painel que só lê na seleção.
/// </summary>
public class Etapa10CentralTests
{
    private static readonly DateTime T0 = new(2026, 10, 27, 9, 0, 0);

    private static RoadPavementDefinition Via(double comprimento = 200, bool molda = true)
    {
        var v = new RoadPavementDefinition
        {
            PathRef = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(comprimento, 0) }, 100), GroupId = "g-" + Guid.NewGuid().ToString("N"),
            LeftWidth = 3.5, RightWidth = 3.5, LeftSidewalk = 3, RightSidewalk = 3,
        };
        v.Output.Grade = new RoadGrade { DefaultCurve = 60, AdjustTerrain = molda };
        foreach (var (s, z) in new[] { (0.0, 2.0), (comprimento, 4.0) }) v.Output.Grade.Points.Add(new GradePoint(s, z));
        return v;
    }

    private static IndicadorProjeto I(ResumoDoProjeto r, string chave) => r.Indicadores.Single(i => i.Chave == chave);

    private static ValidacaoGravada Validacao(params Severidade[] s) =>
        new(T0, "2.16.0", s.Select((x, k) => new AchadoGravado($"k{k}", "V07", x, "achado")).ToList());

    [Fact]
    public void Central_ProjetoVazio_SemZeroInventado()
    {
        var r = CentralDoProjeto.Resumir(Array.Empty<MarkingDefinition>(), null, null, false);
        // Contagens conhecidas: zero de verdade.
        Assert.Equal((0.0, SituacaoValor.Calculado), (I(r, "vias").Valor!.Value, I(r, "vias").Situacao));
        Assert.Equal("0", I(r, "intersecoes").ValorTexto);
        // Sem terraplenagem aplicada e sem verificação: sem valor ("—"), não zero.
        foreach (var k in new[] { "corte", "aterro", "inconsistencias" })
        {
            Assert.Null(I(r, k).Valor);
            Assert.Equal("—", I(r, k).ValorTexto);
            Assert.Equal(SituacaoValor.NaoCalculado, I(r, k).Situacao);
        }
        Assert.Contains("sem registro da terraplenagem", I(r, "corte").Detalhe);
        Assert.Equal("verificação não executada neste projeto", I(r, "inconsistencias").Detalhe);
        Assert.Equal((NivelSituacao.NaoVerificado, "Verificação não executada"), (r.NivelGeral, r.SituacaoGeral));
        Assert.DoesNotContain(r.Situacao.Concat<object>(r.Indicadores), x => x.ToString()!.Contains("validado", StringComparison.OrdinalIgnoreCase)
            && !x.ToString()!.Contains("não", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "viario", "intersecoes", "terraplenagem", "pavimentacao", "drenagem", "urbanismo", "sinalizacao", "analise", "documentacao" },
            r.Modulos.Select(m => m.Chave));
    }

    [Fact]
    public void Central_ViasExtensaoEPavimentoSoQuandoMedido()
    {
        var v = Via();
        var semEixo = Via();
        semEixo.PathRef = new PathReference();
        var defs = new MarkingDefinition[] { v, semEixo, new IntersectionDefinition { RoadIds = { v.Id, semEixo.Id } } };
        var r = CentralDoProjeto.Resumir(defs, null, null, false);
        Assert.Equal(2, I(r, "vias").Valor);
        Assert.Equal(200, I(r, "extensao").Valor!.Value, 6);
        Assert.Contains("1 via(s) sem o traçado guardado ficaram de fora", I(r, "extensao").Detalhe);
        var pav = I(r, "pavimento");
        Assert.Equal((null, SituacaoValor.NaoCalculado), (pav.Valor, pav.Situacao));
        Assert.Contains("use Calcular", pav.Detalhe);
        Assert.Contains("2 via(s) sem a estrutura do pavimento informada", pav.Detalhe);
        // Medido: calculado; medido com falha: inválido (parcial), com o número de marcas.
        Assert.Equal((1234.5, SituacaoValor.Calculado), (I(CentralDoProjeto.Resumir(defs, null, null, false, 1234.5), "pavimento").Valor!.Value,
            I(CentralDoProjeto.Resumir(defs, null, null, false, 1234.5), "pavimento").Situacao));
        var parcial = I(CentralDoProjeto.Resumir(defs, null, null, false, 1000, 2), "pavimento");
        Assert.Equal(SituacaoValor.Invalido, parcial.Situacao);
        Assert.Contains("2 marca(s) não puderam ser medidas", parcial.Detalhe);
        Assert.Equal("1.000,0 m²", parcial.ValorTexto);
        // Nenhuma via tem o traçado: indisponível, sem valor.
        var so = CentralDoProjeto.Resumir(new MarkingDefinition[] { semEixo }, null, null, false);
        Assert.Equal((null, SituacaoValor.Indisponivel), (I(so, "extensao").Valor, I(so, "extensao").Situacao));
    }

    [Fact]
    public void Central_VolumesDaTerraplenagem_EmDiaEDesatualizados()
    {
        var v = Via();
        var defs = new MarkingDefinition[] { v };
        var reg = EarthworkRecord.Gravar(EarthworkRecord.Participantes(defs), 0.4, 1, 1.5, T0, "2.16.0", 1000, 600, 2500, 1.25);
        var r = CentralDoProjeto.Resumir(defs, reg, null, false);
        Assert.Equal((1000.0, SituacaoValor.Calculado), (I(r, "corte").Valor!.Value, I(r, "corte").Situacao));
        Assert.Equal(600, I(r, "aterro").Valor);
        Assert.Contains("terraplenagem aplicada em 27/10/2026 09:00, com as marcas atuais", I(r, "corte").Detalhe);
        Assert.Contains(r.Situacao, s => s.Tema == "Terraplenagem" && s.Nivel == NivelSituacao.Ok);
        // A via muda: o volume guardado continua, marcado como desatualizado, com a ação.
        v.LeftWidth = 5;
        var depois = CentralDoProjeto.Resumir(defs, reg, Validacao(), false);
        Assert.Equal((1000.0, SituacaoValor.Desatualizado), (I(depois, "corte").Valor!.Value, I(depois, "corte").Situacao));
        Assert.Contains("1 marca(s) mudaram depois – rode a Terraplenagem", I(depois, "corte").Detalhe);
        Assert.Equal("CmdTerraplenagem", I(depois, "corte").Comando);
        Assert.Contains(depois.Situacao, s => s.Tema == "Terraplenagem" && s.Nivel == NivelSituacao.Aviso && s.Texto.StartsWith("necessita de recálculo"));
        Assert.Equal((NivelSituacao.Aviso, "Há resultados a refazer"), (depois.NivelGeral, depois.SituacaoGeral));
        // Registro sem volume gravado: não calculado, dito.
        var semVolume = EarthworkRecord.Gravar(EarthworkRecord.Participantes(defs), 0.4, 1, 1.5, T0, null);
        Assert.Contains("não gravou o volume", I(CentralDoProjeto.Resumir(defs, semVolume, null, false), "corte").Detalhe);
    }

    [Fact]
    public void Central_QuadrasLotesEAreaParcelada()
    {
        var lot = new LoteamentoDefinition { Nome = "Jardim Teste", Gleba = new() { new(-10, -10), new(112, -10), new(112, 70), new(-10, 70) } };
        var q = LTScenes.Q("S", new List<Vec2> { new(0, 0), new(102, 0), new(102, 60), new(0, 60) }, new Vec2(51, 0));
        q.Miolo = ModoMiolo.AreaPropria;
        q.LoteamentoId = lot.Id;
        var r = CentralDoProjeto.Resumir(new MarkingDefinition[] { lot, q }, null, null, false);
        Assert.Equal(6120, I(r, "quadras").Valor!.Value, 3);
        Assert.Equal(9, I(r, "lotes").Valor);                       // os mesmos 9 dos indicadores do loteamento (Etapa 7)
        var soma = LotDivider.Divide(q, lot).Parcelas.Sum(p => p.Area);
        Assert.Equal(soma, I(r, "parcelada").Valor!.Value, 6);
        Assert.Contains("lotes: 3.060,0 m²", I(r, "parcelada").Detalhe);
        // Quadra com contorno inválido: não entra e o indicador diz que é inválido.
        var ruim = new QuadraDefinition { Nome = "X", LoteamentoId = lot.Id, Contorno = new() { new(0, 0), new(1, 0) } };
        var r2 = CentralDoProjeto.Resumir(new MarkingDefinition[] { lot, q, ruim }, null, null, false);
        Assert.Equal(SituacaoValor.Invalido, I(r2, "quadras").Situacao);
        Assert.Contains("1 com contorno inválido não entram", I(r2, "quadras").Detalhe);
        Assert.Equal(6120, I(r2, "quadras").Valor!.Value, 3);
        // Quadra cujo loteamento não existe mais: dividida com as categorias padrão, e isso é dito.
        var orfa = CentralDoProjeto.Resumir(new MarkingDefinition[] { q }, null, null, false);
        Assert.Contains("sem o loteamento", I(orfa, "lotes").Detalhe);
    }

    [Fact]
    public void Central_DrenagemEInconsistencias()
    {
        var v = Via();
        var boca = new DrainageDefinition { Type = TipoDrenagem.BocaDeLoboSimples, Position = new Vec2(50, 3.5), Z = 100 };
        var pv = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(60, 0), Z = 100 };
        var tubo = new DrainagePipeDefinition { FromId = boca.Id, ToId = pv.Id, Diameter = 0.4 };
        var r = CentralDoProjeto.Resumir(new MarkingDefinition[] { v, boca, pv, tubo }, null, Validacao(Severidade.Erro, Severidade.Aviso, Severidade.Aviso, Severidade.Info), false);
        var dren = I(r, "drenagem");
        Assert.Equal(2, dren.Valor);
        Assert.Contains("2 dispositivo(s) e 1 trecho(s); sem Projeto de Drenagem: vazões e diâmetros não calculados", dren.Detalhe);
        Assert.Contains("informado", I(CentralDoProjeto.Resumir(new MarkingDefinition[] { boca }, null, null, true), "drenagem").Detalhe);
        var inc = I(r, "inconsistencias");
        Assert.Equal((3.0, SituacaoValor.Calculado), (inc.Valor!.Value, inc.Situacao));
        Assert.Contains("1 erro(s) e 2 aviso(s) na verificação de 27/10/2026 09:00 (versão 2.16.0); o projeto pode ter mudado depois", inc.Detalhe);
        Assert.Equal((NivelSituacao.Erro, "Existem erros na última verificação"), (r.NivelGeral, r.SituacaoGeral));
        // Verificação limpa e nada a refazer: o texto diz o que se sabe, sem "validado".
        var limpo = CentralDoProjeto.Resumir(new MarkingDefinition[] { Via(molda: false) }, null, Validacao(Severidade.Info), false);
        Assert.Equal(NivelSituacao.Ok, limpo.NivelGeral);
        Assert.Equal("Sem erros nem avisos na última verificação e resultados derivados atualizados", limpo.SituacaoGeral);
        var texto = CentralDoProjeto.Texto(limpo, "Projeto X", T0);
        Assert.StartsWith("CENTRAL DO PROJETO – Projeto X", texto);
        Assert.Contains("[ok] Verificação: nenhum erro nem aviso na verificação de 27/10/2026 09:00; o projeto pode ter mudado depois", texto);
    }

    [Fact]
    public void Central_TodoTipoDeMarcaTemModulo()
    {
        var tipos = typeof(MarkingDefinition).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(MarkingDefinition).IsAssignableFrom(t)).ToList();
        Assert.True(tipos.Count >= 40, $"só {tipos.Count} tipos");
        var sem = tipos.Where(t => CentralDoProjeto.ModuloDe((MarkingDefinition)Activator.CreateInstance(t)!) == null).Select(t => t.Name).ToList();
        Assert.True(sem.Count == 0, "tipo sem módulo na Central: " + string.Join(", ", sem));
    }

    // ------------------------------------------------------------------ painel contextual

    [Fact]
    public void Painel_Vazio_OrientaEOfereceAtalhos()
    {
        var c = PainelContextual.De(Array.Empty<MarkingDefinition>(), foraDoPlugin: 3);
        Assert.Equal("Nenhum elemento do SinalizaBIM selecionado", c.Titulo);
        Assert.Empty(c.Propriedades);
        Assert.Contains(c.Operacoes, o => o.Comando == "CmdCentralProjeto");
        Assert.Contains("3 elemento(s) do Revit selecionado(s), fora do SinalizaBIM", c.Notas.Single());
    }

    [Fact]
    public void Painel_Via_SecaoPerfilPavimentoEEstado()
    {
        var v = Via();
        v.Estrutura = new EstruturaPavimento { Fonte = "projeto de pavimentação", Camadas = { new CamadaPavimento { Nome = "Base", Material = "BGS", EspessuraCm = 15 } } };
        v.Notes = "Rua das Flores";
        var reg = EarthworkRecord.Gravar(new MarkingDefinition[] { v }, 0.4, 1, 1.5, T0, null);
        var c = PainelContextual.De(new MarkingDefinition[] { v, v }, 0, reg);           // dois elementos da mesma via: uma marca
        Assert.Equal(v.KindName, c.Titulo);
        string P(string nome) => c.Propriedades.Single(p => p.Nome == nome).Valor;
        Assert.Equal("Rua das Flores", P("Observação"));
        Assert.Equal("3,50 m à esquerda + 3,50 m à direita do eixo", P("Pista"));
        Assert.Equal("3,00 m (esquerda) e 3,00 m (direita)", P("Calçadas"));
        Assert.Equal("200,00 m", P("Extensão do eixo"));
        Assert.Equal("2 PIV(s); curva vertical padrão de 60 m", P("Greide"));
        Assert.Equal("Base: 15 cm, BGS (base)", P("Camada 1"));
        Assert.Equal("projeto de pavimentação", P("Fonte"));
        Assert.Contains(c.Estado, e => e.Nivel == NivelSituacao.Ok && e.Texto.StartsWith("terraplenagem: em dia"));
        Assert.Contains(c.Operacoes, o => o.Comando == "CmdPerfilVia");
        Assert.Contains(c.Operacoes, o => o.Comando == "CmdEditar");
        // Mudou depois da terraplenagem: aviso com a ação.
        v.RightWidth = 4;
        Assert.Contains(PainelContextual.Uma(v, reg).Estado, e => e.Nivel == NivelSituacao.Aviso && e.Texto.Contains("rode a Terraplenagem"));
        // Sem registro: não verificado; uma placa não molda o terreno: nada a dizer.
        Assert.Equal(NivelSituacao.NaoVerificado, PainelContextual.NaTerraplenagem(v, null)!.Nivel);
        Assert.Null(PainelContextual.NaTerraplenagem(new SignDefinition(), reg));
    }

    [Fact]
    public void Painel_SelecaoMultipla_SoOQueEIgualESemLote()
    {
        var a = Via();
        var b = Via();
        a.Notes = "Rua A";
        b.Notes = "Rua B";
        var c = PainelContextual.De(new MarkingDefinition[] { a, b });
        Assert.Equal($"2 × {a.KindName}", c.Titulo);
        Assert.DoesNotContain(c.Propriedades, p => p.Nome == "Observação");                              // difere
        Assert.Contains(c.Propriedades, p => p.Nome == "Pista");                                          // iguais
        Assert.Contains(c.Propriedades, p => p.Nome == "Código" && p.Valor == a.DisplayCode);             // o código do tipo, igual
        Assert.Equal(new[] { "CmdValidarModelo" }, c.Operacoes.Select(o => o.Comando));                   // sem edição em lote
        Assert.Contains(c.Notas, n => n.Contains("alteração em lote não é oferecida"));
        // Tipos diferentes: só a contagem por tipo.
        var m = PainelContextual.De(new MarkingDefinition[] { a, new SignDefinition(), new SignDefinition() });
        Assert.Equal("3 marcas do SinalizaBIM", m.Titulo);
        Assert.Contains(m.Propriedades, p => p.Valor == "2 marca(s)");
        Assert.Contains(m.Notas, n => n.StartsWith("Selecione elementos de um só tipo"));
    }

    [Fact]
    public void Painel_DrenagemEQuadra()
    {
        var boca = new DrainageDefinition { Type = TipoDrenagem.BocaDeLoboSimples, Position = new Vec2(50, 3.5), Z = 100, AreaContribuicao = 420,
            DeclividadeSarjeta = 0.02, Lida = new ContribuicaoLida { ViaId = "v", Quando = T0, Area = 420, Declividade = 0.02 } };
        var c = PainelContextual.Uma(boca);
        Assert.Equal("420,0 m²", c.Propriedades.Single(p => p.Nome == "Área de contribuição").Valor);
        Assert.Equal("2,00 %", c.Propriedades.Single(p => p.Nome == "Declividade da sarjeta").Valor);
        Assert.Contains(c.Estado, e => e.Texto.StartsWith("área e declividade lidas da via em 27/10/2026 09:00"));
        Assert.Contains(c.Operacoes, o => o.Comando == "CmdCaptacoesDoModelo");

        var lot = new LoteamentoDefinition { Nome = "Jardim Teste", Gleba = new() { new(-10, -10), new(112, -10), new(112, 70), new(-10, 70) } };
        var q = LTScenes.Q("S", new List<Vec2> { new(0, 0), new(102, 0), new(102, 60), new(0, 60) }, new Vec2(51, 0));
        q.Miolo = ModoMiolo.AreaPropria;
        q.LoteamentoId = lot.Id;
        var cq = PainelContextual.Uma(q, buscar: id => id == lot.Id ? lot : null);
        string P(string nome) => cq.Propriedades.Single(p => p.Nome == nome).Valor;
        Assert.Equal("Jardim Teste", P("Loteamento"));
        Assert.Equal("6.120,00 m²", P("Área"));
        Assert.StartsWith("9 (categoria Lote)", P("Lotes"));
        Assert.Contains(cq.Operacoes, o => o.Comando == "CmdQuadroQuadra");
        // Sem consultar o loteamento: dito, e os lotes contados com as categorias padrão.
        var sem = PainelContextual.Uma(q);
        Assert.Equal("não consultado", sem.Propriedades.Single(p => p.Nome == "Loteamento").Valor);
        Assert.Contains(sem.Notas, n => n.Contains("categorias e os mínimos padrão"));
    }

    // ------------------------------------------------------------------ camada Revit (fontes)

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

    private static string Ler(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz(), "src" }.Concat(partes).ToArray()));

    [Fact]
    public void Fontes_ComandosNomeadosExistemENaFaixa()
    {
        var rb = Ler("SinalizacaoViaria.Revit", "RibbonBuilder.cs");
        var naFaixa = Regex.Matches(rb, @"Data\(typeof\((\w+)\)").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (var f in new[] { "ProjectCentral.cs", "ContextPanel.cs" })
        {
            var nomes = Regex.Matches(Ler("SinalizacaoViaria.Core", "Automation", f), @"""(Cmd\w+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.True(nomes.Count >= 8, f);
            var fora = nomes.Where(n => !naFaixa.Contains(n)).ToList();
            Assert.True(fora.Count == 0, $"{f}: comando fora da faixa: {string.Join(", ", fora)}");
        }
        Assert.Contains("typeof(CmdCentralProjeto)", rb);
        Assert.Contains("typeof(CmdPainelContextual)", rb);
    }

    [Fact]
    public void Fontes_PainelSoLeNaSelecaoECentralRodaPeloComando()
    {
        var svc = Ler("SinalizacaoViaria.Revit", "Infrastructure", "ContextPanelService.cs");
        Assert.Contains("app.RegisterDockablePane(PaneId", svc);
        Assert.Contains("app.SelectionChanged += OnSelectionChanged", svc);
        Assert.Contains("ExternalEvent.Create(Handler)", svc);
        Assert.Contains("data.VisibleByDefault = false", svc);
        // O evento de seleção não pode alterar o documento nem a seleção (API): o serviço não abre transação nem seleciona.
        Assert.DoesNotContain("Transaction", svc);
        Assert.DoesNotContain("SetElementIds", svc);
        Assert.Contains("ContextPanelService.Register(application)", Ler("SinalizacaoViaria.Revit", "App.cs"));
        Assert.Contains("ContextPanelService.Unregister(application)", Ler("SinalizacaoViaria.Revit", "App.cs"));
        // Comandos abertos pela Central e pelo painel passam pelas mesmas validações do botão.
        var cb = Ler("SinalizacaoViaria.Revit", "Commands", "CommandBase.cs");
        Assert.Contains("public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) => Invoke(commandData.Application);", cb);
        Assert.Contains("public Result Invoke(UIApplication app)", cb);
        var central = Ler("SinalizacaoViaria.Revit", "Commands", "CentralCommands.cs");
        Assert.Matches(@"\[Transaction\(TransactionMode\.Manual\)\]\s*public sealed class CmdCentralProjeto", central);
        Assert.Contains("w.Comando is { } c ? CommandCatalog.Run(c, app)", central);
        Assert.DoesNotContain("new Transaction(", central);
    }
}
