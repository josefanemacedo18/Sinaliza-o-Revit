using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 13 do plano – relatório do Simulador de Tráfego reprodutível (F11): o que é análise, o que é microssimulação e o que é
/// animação, com os parâmetros, as sementes e a assinatura do cenário; a mesma semente dá os mesmos números.
/// </summary>
public class Etapa13TrafegoTests
{
    private static (TrafficResult Macro, SimResult Sim) Rodar(TrafficNetwork net, int seed)
    {
        var opt = new TrafficOptions { Demand = NivelDemanda.Pico, SimSeconds = 300, WarmupSeconds = 120, Seed = seed };
        var macro = TrafficAnalysis.Run(net, opt);
        return (macro, TrafficSimulation.Run(macro));
    }

    /// <summary>O relatório sem a linha da data de geração.</summary>
    private static string SemData(string s) => string.Join("\n", s.Replace("\r", "").Split('\n').Where(l => !l.StartsWith("Gerado em ", StringComparison.Ordinal)));

    [Fact]
    public void MesmaSemente_MesmosNumerosEMesmoRelatorio()
    {
        var (m, _) = AA1SolutionReproTests.CrossPare();
        var net = m.Network();
        var (a, sa) = Rodar(net, 7);
        var (b, sb) = Rodar(m.Network(), 7);
        Assert.Equal((sa.Spawned, sa.Completed, sa.MeanDelay, sa.MeanTravelTime, sa.Frames.Count), (sb.Spawned, sb.Completed, sb.MeanDelay, sb.MeanTravelTime, sb.Frames.Count));
        Assert.Equal(TrafficReport.Assinatura(a), TrafficReport.Assinatura(b));
        Assert.Equal(SemData(TrafficReport.Build(a, sa, "P")), SemData(TrafficReport.Build(b, sb, "P")));
        // Outra semente: outra amostra (as chegadas mudam) e a mesma assinatura da rede com outro parâmetro – a semente.
        var (c, sc) = Rodar(m.Network(), 8);
        Assert.NotEqual((sa.Spawned, sa.MeanDelay), (sc.Spawned, sc.MeanDelay));
        Assert.NotEqual(TrafficReport.Assinatura(a), TrafficReport.Assinatura(c));
    }

    [Fact]
    public void Relatorio_SeparaAnaliseSimulacaoEAnimacaoComParametrosESementes()
    {
        var (m, _) = AA1SolutionReproTests.CrossPare();
        var (r, sim) = Rodar(m.Network(), 7);
        var t = TrafficReport.Build(r, sim, "P");
        Assert.Contains("9. ANÁLISE, SIMULAÇÃO E ANIMAÇÃO – PARÂMETROS PARA REPRODUZIR", t);
        Assert.Contains("• Análise (seções 1 a 7, sem a microssimulação): determinística", t);
        Assert.Contains("chegadas de Poisson e rotas sorteadas com a semente 7; perfil de cada motorista com a", t);
        Assert.Contains("semente 224 (derivada da primeira). Passo de 0,25 s; aquecimento de 120 s e medição de 300 s.", t);
        Assert.Contains($"um a cada 1 s simulado, desde 60 s antes do fim do aquecimento ({sim.Frames.Count} quadro(s)). É ilustração: nenhum número deste", t);
        Assert.Contains("microssimulada com 3 sementes (7 a 9)", t);
        Assert.Contains($"Assinatura do cenário (rede lida do projeto + parâmetros): {TrafficReport.Assinatura(r)}.", t);
        Assert.Contains("min de aquecimento, semente 7):", t);   // resumo
        // A animação começa 60 s antes da medição e grava um quadro por segundo: (aquecimento − 60 + medição) quadros, ±1.
        Assert.InRange(sim.Frames.Count, 300 + 60 - 1, 300 + 60 + 1);
        // Sem microssimulação, o relatório diz que ela e a animação não rodaram.
        Assert.Contains("• Microssimulação e animação: não rodadas neste relatório (só a análise).", TrafficReport.Build(r, null, "P"));
    }

    [Fact]
    public void Assinatura_MudaComOParametroEComARede()
    {
        var (m, _) = AA1SolutionReproTests.CrossPare();
        var net = m.Network();
        var o = new TrafficOptions { Demand = NivelDemanda.Pico };
        var a = TrafficReport.Assinatura(TrafficAnalysis.Run(net, o));
        Assert.Equal(16, a.Length);
        Assert.Equal(a, TrafficReport.Assinatura(TrafficAnalysis.Run(m.Network(), new TrafficOptions { Demand = NivelDemanda.Pico })));
        Assert.NotEqual(a, TrafficReport.Assinatura(TrafficAnalysis.Run(net, new TrafficOptions { Demand = NivelDemanda.Media })));
        var (outra, _) = AA1SolutionReproTests.Cross(ControleIntersecao.Semaforo);
        Assert.NotEqual(a, TrafficReport.Assinatura(TrafficAnalysis.Run(outra.Network(), o)));
    }
}
