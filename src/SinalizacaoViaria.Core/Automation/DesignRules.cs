namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Regras de projeto tiradas dos manuais (MBST Vol. II, IV e VI; DER/SP Vol. I): distâncias de colocação de placas,
/// espaçamento das linhas de redução de velocidade, comprimentos de transição das marcas de canalização, espaçamento
/// de tachas e tachões. Tudo paramétrico – os valores são pontos de partida editáveis nas ferramentas.
/// </summary>
public static class DesignRules
{
    /// <summary>
    /// Distância de desaceleração e/ou manobra entre a placa e o ponto crítico (MBST Vol. II, item 3.13.2):
    /// desaceleração suave e constante de 2,00 m/s² da velocidade de aproximação até a velocidade final.
    /// Ex.: 60 → 0 km/h = 69 m; 80 → 30 km/h = 106 m.
    /// </summary>
    public static double DecelerationDistance(double approachKmh, double finalKmh, double decelMs2 = 2.0)
    {
        var v0 = Math.Max(0, approachKmh) / 3.6;
        var v1 = Math.Clamp(finalKmh, 0, approachKmh) / 3.6;
        return Math.Max(0, (v0 * v0 - v1 * v1) / (2 * Math.Max(0.1, decelMs2)));
    }

    /// <summary>Distância arredondada para a mensagem complementar "A ... m" (múltiplo de 10 m, mínimo 20 m).</summary>
    public static int WarningDistance(double approachKmh, double finalKmh = 20) =>
        (int)Math.Max(20, Math.Round((DecelerationDistance(approachKmh, finalKmh) + 5) / 10) * 10);

    /// <summary>
    /// Linhas de estímulo à redução de velocidade (MBST Vol. IV, 5.2): posições das linhas (m, a partir da primeira)
    /// para reduzir de <paramref name="v0Kmh"/> a <paramref name="vfKmh"/> com desaceleração <paramref name="decel"/>
    /// (1,47 m/s²) e intervalo de tempo <paramref name="dt"/> (1 s) entre linhas: Ei = i·(V0·t − 0,5·a·t²·i).
    /// A última linha fica onde a velocidade já deve estar reduzida (2 m antes do ponto crítico).
    /// </summary>
    public static List<double> LrvStations(double v0Kmh, double vfKmh, double decel = 1.47, double dt = 1.0)
    {
        var v0 = Math.Max(1, v0Kmh) / 3.6;
        var vf = Math.Clamp(vfKmh, 0, v0Kmh - 1) / 3.6;
        var ta = (v0 - vf) / Math.Max(0.1, decel);
        var n = Math.Max(1, (int)Math.Round(ta / Math.Max(0.2, dt)));
        var res = new List<double> { 0 };
        for (int i = 1; i <= n; i++)
        {
            var e = i * (v0 * dt - 0.5 * decel * dt * dt * i);
            if (e <= res[^1] + 0.5) break;
            res.Add(Math.Round(e * 2) / 2);
        }
        return res;
    }

    /// <summary>Largura da linha LRV pela velocidade regulamentada (MBST Vol. IV): 0,20 / 0,30 / 0,40 m.</summary>
    public static double LrvLineWidth(double vKmh) => vKmh <= 60 ? 0.20 : vKmh <= 80 ? 0.30 : 0.40;

    /// <summary>
    /// Padrão traço/espaço (m) de um conjunto de LRV no sentido do tráfego: linha, intervalo, linha… A sequência
    /// começa na primeira linha (mais espaçada) e termina na última (junto ao ponto crítico).
    /// </summary>
    public static double[] LrvPattern(double v0Kmh, double vfKmh, double lineWidth)
    {
        var st = LrvStations(v0Kmh, vfKmh);
        var pat = new List<double>();
        for (int i = 0; i < st.Count; i++)
        {
            pat.Add(lineWidth);
            if (i + 1 < st.Count) pat.Add(Math.Max(0.5, st[i + 1] - st[i] - lineWidth));
        }
        return pat.ToArray();
    }

    /// <summary>
    /// Comprimento do trecho de transição (taper) das marcas de canalização (MBST Vol. IV, MTL/MAO; DER/SP B.3):
    /// l = 0,5 · V · d, com V em km/h e d a variação de largura (m). Mínimo de 30 m (urbana) ou 60 m (rodovia) para
    /// obstáculos; em alargamentos pode reduzir-se à transição de acostamento.
    /// </summary>
    public static double TaperLength(double vKmh, double widthChange, bool rural = false, bool obstacle = false)
    {
        var l = 0.5 * Math.Max(10, vKmh) * Math.Max(0, widthChange);
        if (obstacle) l = Math.Max(l, rural ? 60 : 30);
        return Math.Max(l, ShoulderTransition(vKmh) * 0.5);
    }

    /// <summary>Transição no acostamento ta (DER/SP Quadro B-9): 30 / 40 / 50 m conforme a velocidade.</summary>
    public static double ShoulderTransition(double vKmh) => vKmh < 60 ? 30 : vKmh < 80 ? 40 : 50;

    /// <summary>Espaçamento entre as barras do zebrado de canalização (DER/SP Quadro B-10): 1,50 m (V &lt; 80) ou 2,50 m.</summary>
    public static double ChannelHatchGap(double vKmh) => vKmh < 80 ? 1.50 : 2.50;

    /// <summary>
    /// Espaçamento de tachas junto às linhas longitudinais (MBST Vol. VI, Tabela 4.6): situação normal 8/12/16 m,
    /// especial (neblina, curvas, declives) 6/9/12 m, conforme a velocidade.
    /// </summary>
    public static double StudSpacing(double vKmh, bool special = false) =>
        vKmh < 80 ? (special ? 6 : 8) : vKmh <= 90 ? (special ? 9 : 12) : (special ? 12 : 16);

    /// <summary>Espaçamento de balizadores em curva pelo raio (MBST Vol. VI, Tabela 4.1); tangente = 60 m.</summary>
    public static double DelineatorSpacing(double radius) =>
        radius <= 0 ? 60 : radius <= 50 ? 10 : radius <= 150 ? 15 : radius <= 230 ? 20 : radius <= 400 ? 30 : radius <= 600 ? 40 : radius <= 800 ? 50 : 60;

    /// <summary>Espaçamento de marcadores de alinhamento em curva pelo raio externo (MBST Vol. VI, Tabela 5.1).</summary>
    public static double AlignmentMarkerSpacing(double radius) =>
        radius <= 50 ? 5 : radius <= 150 ? 8 : radius <= 230 ? 10 : radius <= 400 ? 15 : radius <= 600 ? 20 : radius <= 800 ? 25 : 30;

    /// <summary>Tamanho do símbolo "Dê a preferência" (SIP) pela velocidade (MBST Vol. IV, 8.2.1): 3,60 m (≤ 60) ou 6,00 m.</summary>
    public static double YieldSymbolLength(double vKmh) => vKmh <= 60 ? 3.60 : 6.00;

    /// <summary>Altura das legendas no pavimento (MBST Vol. IV, 8.3): urbana 1,60 / 2,40 m; rural 2,40 / 4,00 m.</summary>
    public static double LegendHeight(double vKmh, bool rural = false) => rural ? (vKmh <= 60 ? 2.40 : 4.00) : (vKmh <= 80 ? 1.60 : 2.40);

    /// <summary>Largura das linhas longitudinais (LFO/LMS/LBO) pela velocidade (MBST Vol. IV): 0,10 m (&lt; 80) ou 0,15 m.</summary>
    public static double LongitudinalLineWidth(double vKmh) => vKmh < 80 ? 0.10 : 0.15;
}
