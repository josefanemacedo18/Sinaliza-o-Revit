using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Traffic;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AA: calibração da microssimulação contra as referências (HCM 7 e prática brasileira) – cada medida dentro da
/// faixa de referência. As cenas e as medidas estão em <see cref="AA2CalibrationBench"/>.
/// </summary>
public class AA2CalibrationTests
{
    private readonly ITestOutputHelper _out;
    public AA2CalibrationTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Semaforo_FluxoDeSaturacaoETempoPerdido()
    {
        var d = AA2CalibrationBench.SignalDischarge();
        _out.WriteLine($"intervalo {d.SatHeadway:0.00} s → {d.SatFlow:0} veh/h verde/faixa; tempo perdido {d.LostTime:0.0} s; por posição {string.Join(" ", d.ByPosition.Select(x => x.ToString("0.00")))}");
        // Vazão de saturação por faixa ~1 700–1 900 veh/h de verde (intervalo ~2,0 s depois dos primeiros).
        Assert.InRange(d.SatFlow, 1700, 1900);
        // Tempo perdido na partida ~2 s (HCM: 2 s).
        Assert.InRange(d.LostTime, 1.0, 3.0);
        // Os primeiros saem mais devagar (reação e aceleração) e o intervalo converge para o de saturação.
        Assert.True(d.ByPosition[0] > d.ByPosition[4] + 0.5);
    }

    [Fact]
    public void Semaforo_VeiculosPesadosComEquivalenciaDoHcm()
    {
        var d = AA2CalibrationBench.SignalDischarge(1, 0.10);
        _out.WriteLine($"10 % pesados: {d.SatFlow:0} veh/h, equivalência {d.HeavyPce:0.00}");
        // HCM: E_T = 2,0 carros por caminhão/ônibus (s_misto = s / (1 + P_T (E_T − 1))).
        Assert.InRange(d.HeavyPce, 1.5, 2.6);
    }

    [Fact]
    public void Pare_BrechasCriticaEDeSeguimento()
    {
        foreach (var vc in new[] { 300.0, 600.0 })
        {
            var (cap, f) = AA2CalibrationBench.Stop(vc);
            _out.WriteLine($"PARE vc={vc}: capacidade {cap:0} (HCM {AA2CalibrationBench.Hcm(vc, 6.5, 4.0):0}); tc {f.Tc:0.00} s, tf {f.Tf:0.00} s (n = {f.Gaps})");
            // HCM 7, cap. 20: secundária em frente numa principal de duas faixas – tc = 6,5 s, tf = 4,0 s.
            Assert.InRange(f.Tc, 5.5, 7.5);
            Assert.InRange(f.Tf, 3.5, 4.6);
        }
    }

    [Fact]
    public void Rotatoria_BrechasCriticaEDeSeguimento()
    {
        foreach (var vc in new[] { 300.0, 600.0 })
        {
            var (cap, f) = AA2CalibrationBench.Roundabout(vc);
            _out.WriteLine($"rotatória vc={vc}: capacidade {cap:0} (HCM {1380 * Math.Exp(-1.02e-3 * vc):0}); tc {f.Tc:0.00} s, tf {f.Tf:0.00} s (n = {f.Gaps})");
            // HCM 7, cap. 22 (uma faixa): 1 380·e^(−1,02·10⁻³·vc) ⇔ tc ≈ 4,98 s e tf ≈ 2,61 s.
            Assert.InRange(f.Tc, 4.0, 6.0);
            Assert.InRange(f.Tf, 2.3, 3.3);
        }
        // Sem fluxo circulante a entrada escoa no intervalo de seguimento (capacidade ~1 380 veh/h).
        Assert.InRange(AA2CalibrationBench.RoundaboutCapacity(0), 1200, 1500);
    }

    [Fact]
    public void AceitacaoDeBrechas_Heterogenea()
    {
        var (_, f) = AA2CalibrationBench.Stop(600);
        _out.WriteLine("PARE: fração das brechas aproveitadas por comprimento (s): " +
                       string.Join(" ", f.Accepted.Select((p, i) => double.IsNaN(p) ? "" : $"{i}:{p:0.00}").Where(x => x != "")));
        // Não é um degrau: há comprimentos de brecha que uns aceitam e outros não.
        Assert.Contains(f.Accepted, p => p > 0.1 && p < 0.9);
        // E a desigualdade entre motoristas é a de uma distribuição (desvio ~0,5 s em torno do tc do HCM).
        var (_, it, _, net) = AA2CalibrationBench.Cross(1, ControleIntersecao.Pare);
        var sim = TrafficSimulation.Run(TrafficAnalysis.Run(net, AA2CalibrationBench.Options(seconds: 600)));
        var g = sim.DriverGapShifts;
        var mean = g.Average();
        var sd = Math.Sqrt(g.Sum(x => (x - mean) * (x - mean)) / g.Count);
        _out.WriteLine($"desvio pessoal da brecha: média {mean:0.00} s, desvio-padrão {sd:0.00} s, {g.Min():0.0} a {g.Max():0.0} s (n = {g.Count})");
        Assert.InRange(sd, 0.3, 0.7);
        Assert.True(g.Max() - g.Min() > 1.5);
    }

    [Fact]
    public void Pedestres_NaTravessiaSemaforizada_SeguramAsConversoes()
    {
        var f0 = AA2CalibrationBench.RightTurnFlow(0);
        var f3 = AA2CalibrationBench.RightTurnFlow(300);
        var f6 = AA2CalibrationBench.RightTurnFlow(600);
        _out.WriteLine($"conversão à direita (veh/h de verde): {f0:0} sem pedestres, {f3:0} com 300 ped/h, {f6:0} com 600 ped/h");
        // HCM 7 (fator de pedestres/bicicletas): ~0,66 com 300 ped/h e ~0,47 com 600 ped/h da vazão sem pedestres.
        Assert.True(f0 > f3 && f3 > f6);
        Assert.InRange(f3 / f0, 0.50, 0.85);
        Assert.InRange(f6 / f0, 0.35, 0.65);
    }

    [Fact]
    public void TrocasDeFaixa_AntecipadasAntesDasConversoes()
    {
        var (median, late, n) = AA2CalibrationBench.LaneChanges();
        _out.WriteLine($"trocas obrigatórias: mediana a {median:0} m da retenção, {late:P0} a menos de 30 m (n = {n})");
        Assert.True(n > 20);
        Assert.True(median > 80, $"mediana {median:0} m");
        Assert.True(late < 0.15, $"{late:P0} tardias");
    }

    [Fact]
    public void Spillback_FilaAlcancaOCruzamentoAnteriorSemBloquearOMiolo()
    {
        var (up, inBox, queue, overlaps) = AA2CalibrationBench.Spillback();
        _out.WriteLine($"montante {up:0} veh/h, parados no miolo {inBox:0.00} (média), fila {queue:0} m, sobreposições {overlaps}");
        // A fila de jusante chega ao cruzamento anterior (90 m) e segura quem vem de montante...
        Assert.True(queue > 40, $"fila {queue:0} m");
        Assert.True(up < 1500 * 0.8, $"montante {up:0} veh/h");
        // ...sem ninguém parar dentro do cruzamento (CTB art. 45) e sem choque.
        Assert.True(inBox < 0.1, $"parados no miolo {inBox:0.00}");
        Assert.Equal(0, overlaps);
    }
}
