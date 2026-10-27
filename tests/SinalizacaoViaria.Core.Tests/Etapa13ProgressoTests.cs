using System.Runtime.CompilerServices;
using SinalizacaoViaria.Core.Automation;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 13 do plano – progresso e cancelamento nas operações longas (Prompt de Interface §21): sem percentual inventado, o
/// limitador das atualizações e o cancelamento só nos pontos seguros (antes de gravar).
/// </summary>
public class Etapa13ProgressoTests
{
    private sealed class Registro : IProgressoOperacao
    {
        public List<EstadoProgresso> Estados { get; } = new();
        public bool Cancelado { get; set; }
        public void Informar(EstadoProgresso estado) => Estados.Add(estado);
    }

    [Fact]
    public void Estado_FracaoSoComTotalConhecido()
    {
        Assert.Null(new EstadoProgresso("Calculando").Fracao);           // sem contagem: indicador de atividade
        Assert.Null(new EstadoProgresso("Medindo", Feitos: 12).Fracao);   // sem total: nada de percentual
        Assert.Null(new EstadoProgresso("Medindo", Feitos: 0, Total: 0).Fracao);
        Assert.Equal(0.25, new EstadoProgresso("Medindo", Feitos: 75, Total: 300).Fracao);
        Assert.Equal(1.0, new EstadoProgresso("Medindo", Feitos: 310, Total: 300).Fracao);   // o total pode crescer no meio: limitado
        Assert.Equal("75 de 300 marcas", new EstadoProgresso("Medindo", Feitos: 75, Total: 300).Contagem("marcas"));
        Assert.Equal("1.250 marcas", new EstadoProgresso("Medindo", Feitos: 1250).Contagem("marcas"));
        Assert.Equal("", new EstadoProgresso("Calculando").Contagem("marcas"));
    }

    [Fact]
    public void Limitador_UmaAtualizacaoPorIntervaloMaisAsTrocasDeEtapaEAUltima()
    {
        var agora = 0.0;
        var l = new LimitadorDeProgresso(100, () => agora);
        Assert.True(l.Deve("A"));           // a primeira
        agora = 50;
        Assert.False(l.Deve("A"));          // dentro do intervalo
        Assert.True(l.Deve("B"));           // etapa nova: sempre
        agora = 60;
        Assert.True(l.Deve("B", ultima: true));
        agora = 120;
        Assert.False(l.Deve("B"));
        agora = 161;
        Assert.True(l.Deve("B"));
    }

    [Fact]
    public void Passo_CancelaSoNoPontoSeguro()
    {
        var p = new Registro();
        var l = new LimitadorDeProgresso(0);
        Progresso.Passo(p, l, "Atualizar todas as marcas", "Gerando as marcas", 3, 10, "Linear LFO-1");
        Assert.Equal(new EstadoProgresso("Gerando as marcas", "Linear LFO-1", 3, 10), Assert.Single(p.Estados));
        p.Cancelado = true;
        var ex = Assert.Throws<OperacaoCanceladaException>(() => Progresso.Passo(p, l, "Atualizar todas as marcas", "Gerando as marcas", 4, 10));
        Assert.Equal("Atualizar todas as marcas: cancelado pelo usuário – nada foi alterado.", ex.Message);
        // Depois de gravar não se cancela mais: a etapa sai como não cancelável e nada é lançado.
        Progresso.SemCancelar(p, l, "Refazendo os recortes de sobreposição", 1, 4);
        Assert.False(p.Estados[^1].Cancelavel);
        // Sem janela (Autoteste, chamadas internas): nada muda.
        Progresso.Passo(null, l, "x", "y", 1, 2);
    }

    // ------------------------------------------------------------------ ligação no plugin

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
    public void AtualizarTodas_CancelarAntesDeGravarDesfazTudo()
    {
        var src = Ler("Commands", "EditCommands.cs");
        var corpo = src[src.IndexOf("internal static List<RenderResult> RefreshAll(", StringComparison.Ordinal)..];
        corpo = corpo[..corpo.IndexOf("/// <summary>Converte marcas", StringComparison.Ordinal)];
        var commit = corpo.IndexOf("t.Commit();", StringComparison.Ordinal);
        Assert.True(commit > 0);
        // Todos os pontos que podem cancelar ficam antes da gravação, dentro do using da transação (sair desfaz).
        var passos = System.Text.RegularExpressions.Regex.Matches(corpo, @"Progresso\.Passo\(").Select(m => m.Index).ToList();
        Assert.True(passos.Count >= 4);
        Assert.All(passos, i => Assert.True(i < commit, "Progresso.Passo depois de gravar"));
        Assert.True(corpo.IndexOf("using (var t = new Transaction(doc, \"SV - Atualizar todas\"))", StringComparison.Ordinal) < passos.Min());
        // Depois de gravar, só etapas sem cancelar.
        Assert.Contains("Progresso.SemCancelar(progresso, limite, \"Refazendo os recortes de sobreposição\"", corpo[commit..]);
        Assert.DoesNotContain("Progresso.Passo(", corpo[commit..]);
        // O comando mostra a janela, avisa do cancelamento e não gera relatório.
        Assert.Contains("using var progresso = ProgressWindow.Abrir(\"Atualizar todas as marcas\", \"marcas\");", src);
        Assert.Contains("catch (OperacaoCanceladaException ex)", src);
        // O Autoteste chama sem janela (sem cancelar).
        Assert.Contains("CmdAtualizarTodas.RefreshAll(_uidoc, ids)", Ler("Commands", "AutoTestSteps2.cs"));
    }

    [Fact]
    public void Quantitativos_ComProgressoECancelar_JanelaSemPercentualInventado()
    {
        var cmd = Ler("Commands", "ReportCommands.cs");
        Assert.Contains("using var progresso = ProgressWindow.Abrir(\"Quantitativos\", \"marcas\", \"Lendo as marcas do projeto\");", cmd);
        Assert.Contains("Core.Automation.Progresso.Passo(progresso, limite, operacao, \"Medindo as marcas no modelo\", i, defs.Count", cmd);
        Assert.Contains("\"Calculando a terraplenagem (corte e aterro)\"", cmd);
        var w = Ler("UI", "ProgressWindow.cs");
        Assert.Contains("_barra.IsIndeterminate = true;", w);                    // sem total: atividade, sem percentual
        Assert.Contains("if (e.Fracao is { } f)", w);
        Assert.Contains("EnableWindow(UiHelpers.RevitHandle, false);", w);       // Revit desabilitado durante a operação…
        Assert.Contains("EnableWindow(UiHelpers.RevitHandle, true);", w);        // …e devolvido ao fechar
        Assert.Contains("Dispatcher.Invoke(DispatcherPriority.Background", w);  // a janela redesenha no fio do Revit
        Assert.DoesNotContain("Task.Run", w);                                     // nada da API fora do fio do Revit
    }
}
