using System.Globalization;
using System.Text;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Curva horizontal de um PI do traçado como o eixo é gerado (<see cref="Tracado.Solve"/>): deflexão, raio e espiral efetivos
/// (e os pedidos), tangente externa e as estacas teóricas TS, SC, CS e ST (m desde o início). PI sem curva: só a deflexão.
/// </summary>
public sealed record ElementoHorizontal(int Pi, Vec2 Ponto, double DeflexaoGraus, double Raio, double RaioPedido, double Espiral,
    double EspiralPedida, double Tangente, double? EstacaTs, double? EstacaSc, double? EstacaCs, double? EstacaSt)
{
    public bool TemCurva => Raio > 0;
    /// <summary>Raio ou espiral menores que os pedidos (não cabiam entre os PIs vizinhos).</summary>
    public bool Reduzida => TemCurva && (Raio < RaioPedido - 1e-6 || Espiral < EspiralPedida - 1e-6);
    /// <summary>Desenvolvimento do trecho circular (m) entre SC e CS.</summary>
    public double Circular => EstacaCs is { } cs && EstacaSc is { } sc ? cs - sc : 0;
}

/// <summary>
/// PIV do greide como as cotas são calculadas (<see cref="RoadGrade.Z(double)"/>): rampas de entrada e saída (%), comprimento
/// efetivo da curva vertical e o pedido (no PIV ou o padrão do greide), K = L / |A| (m por %), PCV/PTV e o ponto alto/baixo
/// dentro da curva. O efetivo difere do pedido quando o piso de suavização (K ≥ 10) o aumenta ou o vão entre PIVs o limita.
/// Cotas relativas à base do eixo, como no greide.
/// </summary>
public sealed record ElementoVertical(int Piv, double Estaca, double Cota, double? RampaEntradaPct, double? RampaSaidaPct,
    double Comprimento, double ComprimentoPedido, double? K, bool Convexa, double? EstacaPcv, double? CotaPcv, double? EstacaPtv,
    double? CotaPtv, double? EstacaExtremo, double? CotaExtremo)
{
    public bool TemCurva => Comprimento >= 0.01;
    /// <summary>Variação de rampa A = i2 − i1 (%).</summary>
    public double? VariacaoPct => RampaEntradaPct is { } a && RampaSaidaPct is { } b ? b - a : null;
}

/// <summary>
/// Relatório de elementos do eixo: curvas horizontais do traçado por PIs e PIVs do greide. Os valores saem das MESMAS rotinas
/// que geram a geometria (sem cálculo paralelo que possa divergir). Não verifica critérios normativos (raio mínimo,
/// distância de visibilidade): só descreve o que foi projetado.
/// </summary>
public static class AlignmentElements
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Elementos das curvas horizontais nos PIs internos (estacas teóricas pelo desenvolvimento das curvas).</summary>
    public static List<ElementoHorizontal> Horizontal(IReadOnlyList<Pi> pis, List<string>? warnings = null) =>
        Horizontal(pis, warnings, out _);

    /// <inheritdoc cref="Horizontal(IReadOnlyList{Pi}, List{string}?)"/>
    /// <param name="length">Extensão teórica do traçado (m): tangentes + desenvolvimento das curvas.</param>
    public static List<ElementoHorizontal> Horizontal(IReadOnlyList<Pi> pis, List<string>? warnings, out double length)
    {
        var curves = Tracado.Solve(pis, warnings);
        var res = new List<ElementoHorizontal>();
        double st = 0;          // estaca do fim do elemento anterior (ST, PI sem curva ou início)
        double tPrev = 0;       // tangente externa do elemento anterior (consome o começo do trecho entre PIs)
        for (int i = 1; i + 1 < pis.Count; i++)
        {
            var (r, ls, delta, t) = curves[i];
            var ts = st + pis[i - 1].P.DistanceTo(pis[i].P) - tPrev - t;
            if (r <= 0)
            {
                res.Add(new(i, pis[i].P, delta * Angles.RadToDeg, 0, pis[i].Radius, 0, pis[i].Spiral, 0, null, null, null, null));
                st = ts;
                tPrev = 0;
                continue;
            }
            // Mesmo desenvolvimento da geração: espiral + arco + espiral, Lc = R·|Δ| − Ls.
            var lc = Math.Max(0, r * Math.Abs(delta) - ls);
            res.Add(new(i, pis[i].P, delta * Angles.RadToDeg, r, pis[i].Radius, ls, pis[i].Spiral, t, ts, ts + ls, ts + ls + lc,
                ts + 2 * ls + lc));
            st = ts + 2 * ls + lc;
            tPrev = t;
        }
        length = st + pis[^2].P.DistanceTo(pis[^1].P) - tPrev;
        return res;
    }

    /// <summary>PIVs do greide (o primeiro e o último sem curva).</summary>
    public static List<ElementoVertical> Vertical(RoadGrade grade)
    {
        var p = grade.SortedPoints();
        var res = new List<ElementoVertical>();
        for (int i = 0; i < p.Count; i++)
        {
            double? gIn = i > 0 ? RoadGrade.SlopeOf(p[i - 1], p[i]) * 100 : null;
            double? gOut = i + 1 < p.Count ? RoadGrade.SlopeOf(p[i], p[i + 1]) * 100 : null;
            if (gIn == null || gOut == null)
            {
                res.Add(new(i, p[i].S, p[i].Z, gIn, gOut, 0, 0, null, false, null, null, null, null, null, null));
                continue;
            }
            var l = grade.CurveLength(p, i, out _);
            var wanted = p[i].Curve ?? grade.DefaultCurve;
            var a = gOut.Value - gIn.Value;
            if (l < 0.01 || Math.Abs(a) < 1e-9)
            {
                res.Add(new(i, p[i].S, p[i].Z, gIn, gOut, 0, wanted, null, a < 0, null, null, null, null, null, null));
                continue;
            }
            var pcv = p[i].S - l / 2;
            var ptv = p[i].S + l / 2;
            // Ponto de rampa nula dentro da curva (alto na convexa, baixo na côncava): x = −i1·L / A desde o PCV.
            double? sx = null, zx = null;
            var x = -gIn.Value * l / a;
            if (x > 1e-9 && x < l - 1e-9) { sx = pcv + x; zx = grade.Z(pcv + x); }
            res.Add(new(i, p[i].S, p[i].Z, gIn, gOut, l, wanted, l / Math.Abs(a), a < 0, pcv, grade.Z(pcv), ptv, grade.Z(ptv), sx, zx));
        }
        return res;
    }

    /// <summary>Estaca no formato "n + resto" (estacas de 20 m), como nos títulos dos cortes.</summary>
    public static string Estaca(double s)
    {
        var e = (int)Math.Floor(s / 20 + 1e-9);
        return $"{e}+{(s - e * 20).ToString("0.00", Pt)}";
    }

    /// <summary>CSV no padrão brasileiro (";" e vírgula decimal) das curvas horizontais.</summary>
    public static string HorizontalCsv(IEnumerable<ElementoHorizontal> els)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PI;X (m);Y (m);Deflexão (°);R (m);R pedido (m);Ls (m);Ls pedida (m);T (m);TS;SC;CS;ST;Circular (m);Observação");
        foreach (var e in els)
            sb.AppendLine(string.Join(";", e.Pi, N(e.Ponto.X, 3), N(e.Ponto.Y, 3), N(e.DeflexaoGraus, 4), N(e.Raio, 3), N(e.RaioPedido, 3),
                N(e.Espiral, 3), N(e.EspiralPedida, 3), N(e.Tangente, 3), Est(e.EstacaTs), Est(e.EstacaSc), Est(e.EstacaCs), Est(e.EstacaSt),
                N(e.Circular, 3), !e.TemCurva ? "sem curva" : e.Reduzida ? "reduzida para caber" : ""));
        return sb.ToString();
    }

    /// <summary>CSV no padrão brasileiro (";" e vírgula decimal) dos PIVs do greide.</summary>
    public static string VerticalCsv(IEnumerable<ElementoVertical> els)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PIV;Estaca;Estaca (m);Cota (m);i1 (%);i2 (%);A (%);L (m);L pedido (m);K (m/%);Tipo;PCV;Cota PCV;PTV;Cota PTV;Ponto alto/baixo;Cota");
        foreach (var e in els)
            sb.AppendLine(string.Join(";", e.Piv, Estaca(e.Estaca), N(e.Estaca, 3), N(e.Cota, 3), N(e.RampaEntradaPct, 3), N(e.RampaSaidaPct, 3),
                N(e.VariacaoPct, 3), N(e.Comprimento, 2), N(e.ComprimentoPedido, 2), N(e.K, 2),
                !e.TemCurva ? "" : e.Convexa ? "convexa" : "côncava", Est(e.EstacaPcv), N(e.CotaPcv, 3), Est(e.EstacaPtv), N(e.CotaPtv, 3),
                Est(e.EstacaExtremo), N(e.CotaExtremo, 3)));
        return sb.ToString();
    }

    private static string N(double? v, int dec) => v is { } x ? x.ToString("F" + dec, Pt) : "";
    private static string Est(double? s) => s is { } x ? $"{Estaca(x)} ({x.ToString("0.00", Pt)})" : "";
}
