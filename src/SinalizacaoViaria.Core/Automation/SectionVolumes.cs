using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Seção transversal numa estaca: áreas de corte e de aterro (m²) entre o terreno e o projeto.</summary>
public readonly record struct SecaoVolume(double Estaca, double Corte, double Aterro);

/// <summary>Volumes por estaca e totais pelo método das seções.</summary>
public sealed class VolumesPorSecao
{
    public List<SecaoVolume> Secoes { get; } = new();
    /// <summary>Volumes (m³) pela média das áreas das seções vizinhas × distância entre elas.</summary>
    public double CorteM3 { get; set; }
    public double AterroM3 { get; set; }
    /// <summary>Volumes acumulados (m³) até cada seção – a curva de massas usa a diferença (corte − aterro).</summary>
    public List<(double Estaca, double Corte, double Aterro)> Acumulado { get; } = new();
}

/// <summary>
/// Seções de uma via do projeto conjunto (quantitativo por estaca, F7b): volumes medidos só na superfície de projeto que é
/// da via, e a parte dela na malha (<see cref="GradingResult.ByOwner"/>) para conferir os dois métodos.
/// </summary>
public sealed record SecoesDaVia(string Via, HierarquiaViaria? Hierarquia, VolumesPorSecao Volumes, double MalhaCorte, double MalhaAterro,
    Estaqueamento? Estacas = null)
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Diferença relativa das seções em relação à malha (%); nula quando a malha não tem volume.</summary>
    public static double? Diferenca(double secoes, double malha) => malha > 1e-6 ? (secoes - malha) / malha * 100 : null;

    /// <summary>Resumo dos dois métodos (ex.: "Via local 1 – corte: seções 1.234,5 m³, malha 1.240,0 m³ (−0,4 %); ...").</summary>
    public string Comparacao
    {
        get
        {
            string Par(string nome, double sec, double malha) =>
                $"{nome}: seções {sec.ToString("N1", Pt)} m³, malha {malha.ToString("N1", Pt)} m³" +
                (Diferenca(sec, malha) is { } d ? $" ({(d >= 0 ? "+" : "−")}{Math.Abs(d).ToString("0.0", Pt)} %)" : "");
            return $"{Via} – {Par("corte", Volumes.CorteM3, MalhaCorte)}; {Par("aterro", Volumes.AterroM3, MalhaAterro)}";
        }
    }
}

/// <summary>Uma linha da tabela por estaca (janela, CSV e planilha do quantitativo).</summary>
public sealed record LinhaEstaca(string Via, string Estaca, double EstacaM, double AreaCorte, double AreaAterro, double CorteAcumulado, double AterroAcumulado)
{
    /// <summary>Massa acumulada (corte − aterro, m³) – a curva de massas.</summary>
    public double Massa => CorteAcumulado - AterroAcumulado;
}

/// <summary>
/// Volumes de terraplenagem pelo <b>método das seções transversais (média das áreas)</b>: em cada estaca, a seção
/// perpendicular ao eixo é amostrada entre o terreno e a superfície de projeto, e o volume entre duas seções é a média das
/// áreas × a distância entre elas. É um cálculo independente da malha de <see cref="Grading.Volumes"/> e serve para
/// conferi-la e para o quantitativo por estaca. Limitações conhecidas (documentadas, não corrigidas): sem correção de
/// curvatura (Pappus) – em curva fechada as seções perpendiculares se sobrepõem no lado interno – e a média das áreas
/// superestima o volume quando a forma das seções vizinhas muda muito (o prismoide não é aplicado).
/// </summary>
public static class SectionVolumes
{
    /// <param name="axis">Eixo das estacas.</param>
    /// <param name="design">Cota de projeto (nulo = fora da obra: o ponto não entra na seção).</param>
    /// <param name="ground">Cota do terreno natural (nulo = sem terreno no ponto).</param>
    /// <param name="spacing">Distância entre seções (m); a última seção fica sempre no fim do eixo.</param>
    /// <param name="halfWidth">Meia largura da seção (m) – cubra a plataforma e os taludes.</param>
    /// <param name="step">Passo da amostragem ao longo da seção (m).</param>
    public static VolumesPorSecao Compute(Polyline2 axis, Func<Vec2, double?> design, Func<Vec2, double?> ground,
        double spacing = 20, double halfWidth = 30, double step = 0.25)
    {
        var res = new VolumesPorSecao();
        if (axis.Length < 1e-6) return res;
        spacing = Math.Max(0.5, spacing);
        step = Math.Clamp(step, 0.01, Math.Max(0.01, halfWidth));
        var stations = new List<double>();
        for (var s = 0.0; s < axis.Length - 1e-6; s += spacing) stations.Add(s);
        stations.Add(axis.Length);
        var n = Math.Max(1, (int)Math.Ceiling(2 * halfWidth / step));
        var dy = 2 * halfWidth / n;
        // As seções das pontas são amostradas 1 mm para dentro: exatamente na ponta, a plataforma termina sobre a própria
        // seção e o teste "dentro/fora" do contorno é ambíguo (a seção final saía vazia e o último trecho perdia metade).
        var eps = Math.Min(1e-3, axis.Length / 4);
        foreach (var s in stations)
        {
            var sm = Math.Clamp(s, eps, axis.Length - eps);
            var c = axis.PointAt(sm);
            var nrm = axis.TangentAt(sm).PerpLeft;
            double cut = 0, fill = 0;
            for (var k = 0; k < n; k++)
            {
                // Ponto médio de cada faixa da seção (regra do ponto médio).
                var p = c + nrm * (-halfWidth + (k + 0.5) * dy);
                if (design(p) is not { } z || ground(p) is not { } g) continue;
                var d = g - z;
                if (d > 0) cut += d * dy; else fill -= d * dy;
            }
            res.Secoes.Add(new SecaoVolume(s, cut, fill));
        }
        return FromSections(res.Secoes);
    }

    /// <summary>
    /// Volumes pela média das áreas das seções vizinhas × a distância entre elas, e os acumulados em cada seção (a base do
    /// diagrama de massas). As seções em ordem de estaca.
    /// </summary>
    public static VolumesPorSecao FromSections(IEnumerable<SecaoVolume> sections)
    {
        var res = new VolumesPorSecao();
        res.Secoes.AddRange(sections.OrderBy(x => x.Estaca));
        if (res.Secoes.Count == 0) return res;
        double vc = 0, vf = 0;
        res.Acumulado.Add((res.Secoes[0].Estaca, 0, 0));
        for (var i = 1; i < res.Secoes.Count; i++)
        {
            var a = res.Secoes[i - 1];
            var b = res.Secoes[i];
            var ds = b.Estaca - a.Estaca;
            vc += (a.Corte + b.Corte) / 2 * ds;
            vf += (a.Aterro + b.Aterro) / 2 * ds;
            res.Acumulado.Add((b.Estaca, vc, vf));
        }
        res.CorteM3 = vc;
        res.AterroM3 = vf;
        return res;
    }

    /// <summary>
    /// Volumes de um trecho de estacas (de <paramref name="s0"/> a <paramref name="s1"/>, m ao longo do eixo): a mesma média
    /// das áreas, com as áreas nas pontas do trecho interpoladas entre as seções vizinhas. Trechos vizinhos somam o total.
    /// </summary>
    public static (double Corte, double Aterro) Trecho(VolumesPorSecao v, double s0, double s1)
    {
        if (s1 < s0) (s0, s1) = (s1, s0);
        var secs = v.Secoes;
        if (secs.Count < 2) return (0, 0);
        SecaoVolume At(double s)
        {
            if (s <= secs[0].Estaca) return secs[0] with { Estaca = s };
            for (int i = 0; i + 1 < secs.Count; i++)
                if (s <= secs[i + 1].Estaca)
                {
                    var (a, b) = (secs[i], secs[i + 1]);
                    var t = (s - a.Estaca) / Math.Max(1e-12, b.Estaca - a.Estaca);
                    return new SecaoVolume(s, a.Corte + (b.Corte - a.Corte) * t, a.Aterro + (b.Aterro - a.Aterro) * t);
                }
            return secs[^1] with { Estaca = s };
        }
        s0 = Math.Max(s0, secs[0].Estaca);
        s1 = Math.Min(s1, secs[^1].Estaca);
        if (s1 - s0 < 1e-9) return (0, 0);
        var pts = new List<SecaoVolume> { At(s0) };
        pts.AddRange(secs.Where(x => x.Estaca > s0 + 1e-9 && x.Estaca < s1 - 1e-9));
        pts.Add(At(s1));
        var r = FromSections(pts);
        return (r.CorteM3, r.AterroM3);
    }

    /// <summary>
    /// Seções sobre a superfície de projeto de um dono (a via) no projeto conjunto: pontos da superfície de outro dono (o nó de
    /// uma interseção, outra via) ficam fora – a soma das vias não conta duas vezes o que é do nó, e o resultado confere com a
    /// parte da malha daquela via (<see cref="GradingResult.ByOwner"/>).
    /// </summary>
    public static VolumesPorSecao ForOwner(GradingResult design, string owner, Polyline2 axis, Func<Vec2, double?> ground,
        double halfWidth, double spacing = 20, double step = 0.25) =>
        Compute(axis, p => design.DesignAt(p) is ({ } z, var o) && o == owner ? z : null, ground, spacing, halfWidth, step);

    /// <summary>Linhas da tabela por estaca de todas as vias, na ordem dada.</summary>
    public static List<LinhaEstaca> Rows(IEnumerable<SecoesDaVia> vias) => vias
        .SelectMany(v => v.Volumes.Secoes.Select((s, i) => new LinhaEstaca(v.Via, (v.Estacas ?? new Estaqueamento()).Rotulo(s.Estaca), s.Estaca, s.Corte, s.Aterro,
            v.Volumes.Acumulado[i].Corte, v.Volumes.Acumulado[i].Aterro)))
        .ToList();

    /// <summary>Bloco do CSV do quantitativo: a comparação com a malha e as estacas de cada via.</summary>
    public static string Csv(IEnumerable<SecoesDaVia> vias)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        string N(double x) => x.ToString("0.00", pt);
        var list = vias.ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("TERRAPLENAGEM POR ESTACA (método das seções – média das áreas; a malha é a do quantitativo)");
        foreach (var v in list) sb.AppendLine(v.Comparacao.Replace(';', ','));
        sb.AppendLine("Via;Estaca;Estaca (m);Área de corte (m²);Área de aterro (m²);Corte acumulado (m³);Aterro acumulado (m³);Massa acumulada (m³)");
        foreach (var r in Rows(list))
            sb.AppendLine(string.Join(";", r.Via, r.Estaca, N(r.EstacaM), N(r.AreaCorte), N(r.AreaAterro), N(r.CorteAcumulado), N(r.AterroAcumulado), N(r.Massa)));
        sb.AppendLine("Seções perpendiculares ao eixo a cada 20 m, só na superfície de projeto da via (nós e outras vias ficam fora); sem correção de curvatura.");
        return sb.ToString();
    }

    /// <summary>CSV pt-BR (";" e vírgula decimal) com as áreas e os volumes acumulados por estaca.</summary>
    public static string Csv(VolumesPorSecao v, Estaqueamento? estacas = null)
    {
        var est = estacas ?? new Estaqueamento();
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        string N(double x) => x.ToString("F2", pt);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Estaca;Estaca (m);Área de corte (m²);Área de aterro (m²);Corte acumulado (m³);Aterro acumulado (m³);Massa acumulada corte − aterro (m³)");
        for (var i = 0; i < v.Secoes.Count; i++)
        {
            var s = v.Secoes[i];
            var acc = v.Acumulado[i];
            sb.AppendLine(string.Join(";", est.Rotulo(s.Estaca), N(s.Estaca), N(s.Corte), N(s.Aterro), N(acc.Corte), N(acc.Aterro),
                N(acc.Corte - acc.Aterro)));
        }
        sb.AppendLine($"Total;;;;{N(v.CorteM3)};{N(v.AterroM3)};{N(v.CorteM3 - v.AterroM3)}");
        sb.AppendLine("Método: seções transversais perpendiculares ao eixo, média das áreas × distância; sem correção de curvatura.");
        return sb.ToString();
    }
}
