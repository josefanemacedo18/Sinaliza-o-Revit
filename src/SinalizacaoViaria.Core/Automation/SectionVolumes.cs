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

    /// <summary>CSV pt-BR (";" e vírgula decimal) com as áreas e os volumes acumulados por estaca.</summary>
    public static string Csv(VolumesPorSecao v)
    {
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        string N(double x) => x.ToString("F2", pt);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Estaca;Estaca (m);Área de corte (m²);Área de aterro (m²);Corte acumulado (m³);Aterro acumulado (m³);Massa acumulada corte − aterro (m³)");
        for (var i = 0; i < v.Secoes.Count; i++)
        {
            var s = v.Secoes[i];
            var acc = v.Acumulado[i];
            sb.AppendLine(string.Join(";", AlignmentElements.Estaca(s.Estaca), N(s.Estaca), N(s.Corte), N(s.Aterro), N(acc.Corte), N(acc.Aterro),
                N(acc.Corte - acc.Aterro)));
        }
        sb.AppendLine($"Total;;;;{N(v.CorteM3)};{N(v.AterroM3)};{N(v.CorteM3 - v.AterroM3)}");
        sb.AppendLine("Método: seções transversais perpendiculares ao eixo, média das áreas × distância; sem correção de curvatura.");
        return sb.ToString();
    }
}
