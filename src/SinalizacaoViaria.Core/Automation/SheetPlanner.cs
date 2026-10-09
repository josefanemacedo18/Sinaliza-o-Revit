using System.Globalization;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Área da planta numa folha (mm de papel).</summary>
public sealed record SheetFrame(double WidthMm, double HeightMm)
{
    /// <summary>Largura coberta no modelo (m) na escala 1:<paramref name="scale"/>.</summary>
    public double Width(double scale) => WidthMm * scale / 1000.0;
    public double Height(double scale) => HeightMm * scale / 1000.0;

    /// <summary>
    /// Área da planta numa folha de <paramref name="sheetWmm"/> × <paramref name="sheetHmm"/>: menos as margens e a coluna da
    /// direita (legenda de placas, quadro de quantidades e carimbo).
    /// </summary>
    public static SheetFrame PlanArea(double sheetWmm, double sheetHmm, double marginMm, double rightColumnMm) =>
        new(Math.Max(50, sheetWmm - 2 * marginMm - rightColumnMm), Math.Max(50, sheetHmm - 2 * marginMm));
}

/// <summary>
/// Trecho do eixo numa folha: estacas de início e fim, centro e ângulo da vista (direção do trecho), tamanho da região de
/// corte no modelo e as linhas de corte compartilhadas com a folha anterior/seguinte.
/// </summary>
public sealed record SheetSegment(int Index, double Start, double End, Vec2 Center, double AngleRad, double Width, double Height, bool Fits,
    double? PrevMatch = null, double? NextMatch = null)
{
    public double AngleDeg => AngleRad * 180 / Math.PI;
    public Vec2 U => Vec2.FromAngle(AngleRad);
    public Vec2 V => U.PerpLeft;

    /// <summary>Região de corte (retângulo girado) no modelo.</summary>
    public IReadOnlyList<Vec2> Crop => new[]
    {
        Center - U * (Width / 2) - V * (Height / 2), Center + U * (Width / 2) - V * (Height / 2),
        Center + U * (Width / 2) + V * (Height / 2), Center - U * (Width / 2) + V * (Height / 2),
    };
}

/// <summary>
/// Divide o eixo de uma via em trechos que cabem na folha escolhida, na escala escolhida, com sobreposição entre folhas
/// consecutivas e linhas de corte ("continua na prancha X") no meio da sobreposição. A vista de cada trecho é girada ao
/// longo dele (texto legível: ângulo entre −90° e 90°).
/// </summary>
public static class SheetPlanner
{
    /// <summary>Estaca (m) em "10+5,00" (estacas de 20 m) ou em metros ("205", "205,5").</summary>
    public static double ParseStation(string text)
    {
        var t = text.Trim().Replace(" ", "");
        var ci = CultureInfo.InvariantCulture;
        static string Num(string s) => s.Replace(',', '.');
        if (t.Contains('+'))
        {
            var p = t.Split('+');
            return double.Parse(Num(p[0]), ci) * 20 + (p[1].Length > 0 ? double.Parse(Num(p[1]), ci) : 0);
        }
        return double.Parse(Num(t), ci);
    }

    /// <summary>"0-200; 9+0,00 a 20+0,00" → [(0, 200), (180, 400)]. Separadores de trecho: ";" ou quebra de linha.</summary>
    public static List<(double S0, double S1)> ParseRanges(string text)
    {
        var res = new List<(double, double)>();
        foreach (var raw in text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var r = raw.Trim();
            if (r.Length == 0) continue;
            string[] parts;
            if (r.Contains(" a ")) parts = r.Split(" a ");
            else if (r.Contains('–')) parts = r.Split('–');
            else parts = r.Split('-');
            if (parts.Length != 2) throw new FormatException($"Trecho \"{r}\": use início-fim (ex.: 0-200 ou 9+0,00 a 20+0,00).");
            var a = ParseStation(parts[0]);
            var b = ParseStation(parts[1]);
            res.Add((Math.Min(a, b), Math.Max(a, b)));
        }
        return res;
    }

    /// <summary>Trechos a partir de cortes clicados no eixo: cada corte vira a linha de corte, com a sobreposição dividida dos dois lados.</summary>
    public static List<(double S0, double S1)> RangesFromCuts(IEnumerable<double> cuts, double length, double overlap)
    {
        var c = cuts.Where(x => x > 1e-6 && x < length - 1e-6).OrderBy(x => x).Distinct().ToList();
        var res = new List<(double, double)>();
        var s0 = 0.0;
        foreach (var x in c)
        {
            res.Add((s0, Math.Min(length, x + overlap / 2)));
            s0 = Math.Max(0, x - overlap / 2);
        }
        res.Add((s0, length));
        return res;
    }

    /// <summary>Ângulo da vista: direção do trecho (corda início → fim), normalizado em (−90°, 90°].</summary>
    public static double ViewAngle(Polyline2 axis, double s0, double s1)
    {
        var d = axis.PointAt(Math.Clamp(s1, 0, axis.Length)) - axis.PointAt(Math.Clamp(s0, 0, axis.Length));
        if (d.Length < 1e-9) d = axis.TangentAt(Math.Clamp(s0, 0, axis.Length));
        return Normalize(Math.Atan2(d.Y, d.X));
    }

    public static double Normalize(double a)
    {
        while (a > Math.PI / 2 + 1e-12) a -= Math.PI;
        while (a <= -Math.PI / 2 + 1e-12) a += Math.PI;
        return a;
    }

    /// <summary>
    /// Trecho <paramref name="s0"/>–<paramref name="s1"/> numa folha: a região de corte (largura × altura da área da planta na
    /// escala) fica centrada no trecho e girada pelo ângulo da vista (o pedido ou o do trecho); cabe se o eixo e a faixa
    /// lateral <paramref name="halfWidth"/> ficam dentro dela.
    /// </summary>
    public static SheetSegment Segment(Polyline2 axis, int index, double s0, double s1, SheetFrame frame, double scale, double halfWidth,
        double? angleDeg = null)
    {
        s0 = Math.Clamp(s0, 0, axis.Length);
        s1 = Math.Clamp(s1, s0, axis.Length);
        var ang = angleDeg is { } a ? Normalize(a * Math.PI / 180) : ViewAngle(axis, s0, s1);
        var u = Vec2.FromAngle(ang);
        var v = u.PerpLeft;
        var pts = s1 - s0 < 1e-9 ? new List<Vec2> { axis.PointAt(s0) } : axis.SubPoints(s0, s1);
        double umin = double.MaxValue, umax = double.MinValue, vmin = double.MaxValue, vmax = double.MinValue;
        foreach (var p in pts)
        {
            var pu = p.Dot(u);
            var pv = p.Dot(v);
            umin = Math.Min(umin, pu); umax = Math.Max(umax, pu);
            vmin = Math.Min(vmin, pv); vmax = Math.Max(vmax, pv);
        }
        var w = frame.Width(scale);
        var h = frame.Height(scale);
        var fits = umax - umin <= w + 1e-9 && vmax - vmin + 2 * halfWidth <= h + 1e-9;
        var center = u * ((umin + umax) / 2) + v * ((vmin + vmax) / 2);
        return new SheetSegment(index, s0, s1, center, ang, w, h, fits);
    }

    /// <summary>Número de folhas de um eixo reto de comprimento <paramref name="length"/> (largura coberta <paramref name="width"/>).</summary>
    public static int ExpectedCount(double length, double width, double overlap) =>
        length <= width + 1e-9 ? 1 : (int)Math.Ceiling((length - width) / (width - overlap) - 1e-9) + 1;

    /// <summary>
    /// Divisão automática: cada folha leva o maior trecho que cabe nela; a seguinte começa <paramref name="overlap"/> m antes
    /// do fim da anterior. Linhas de corte no meio de cada sobreposição.
    /// </summary>
    public static List<SheetSegment> Auto(Polyline2 axis, SheetFrame frame, double scale, double overlap, double halfWidth)
    {
        var L = axis.Length;
        if (L < 1e-6) return new List<SheetSegment>();
        overlap = Math.Max(0, overlap);
        bool Fits(double a, double b) => Segment(axis, 0, a, b, frame, scale, halfWidth).Fits;
        if (!Fits(0, Math.Min(L, 0.01)))
            throw new InvalidOperationException($"A faixa da via ({2 * halfWidth:0.#} m) não cabe na altura da folha a 1:{scale:0} – use uma escala menor ou uma folha maior.");
        var res = new List<SheetSegment>();
        var s0 = 0.0;
        for (int guard = 0; guard < 10000; guard++)
        {
            if (Fits(s0, L))
            {
                res.Add(Segment(axis, res.Count + 1, s0, L, frame, scale, halfWidth));
                break;
            }
            double lo = s0, hi = L;
            for (int k = 0; k < 60; k++)
            {
                var mid = (lo + hi) / 2;
                if (Fits(s0, mid)) lo = mid; else hi = mid;
            }
            res.Add(Segment(axis, res.Count + 1, s0, lo, frame, scale, halfWidth));
            var next = lo - overlap;
            if (next <= s0 + 1e-6)
                throw new InvalidOperationException($"A sobreposição ({overlap:0.#} m) é maior que o trecho que cabe na folha a 1:{scale:0}.");
            s0 = next;
        }
        return LinkMatches(res);
    }

    /// <summary>Trechos definidos pelo usuário (estacas ou cortes clicados).</summary>
    public static List<SheetSegment> FromRanges(Polyline2 axis, IReadOnlyList<(double S0, double S1)> ranges, SheetFrame frame, double scale, double halfWidth) =>
        LinkMatches(ranges.OrderBy(r => r.S0).Select((r, i) => Segment(axis, i + 1, r.S0, r.S1, frame, scale, halfWidth)).ToList());

    /// <summary>Linha de corte entre folhas consecutivas: no meio da sobreposição (ou do intervalo entre elas).</summary>
    public static List<SheetSegment> LinkMatches(List<SheetSegment> segs)
    {
        var res = segs.OrderBy(s => s.Start).ToList();
        for (int i = 1; i < res.Count; i++)
        {
            var m = (res[i - 1].End + res[i].Start) / 2;
            res[i - 1] = res[i - 1] with { NextMatch = m };
            res[i] = res[i] with { PrevMatch = m };
        }
        return res;
    }

    /// <summary>Estaca no formato "10+5,00" (estacas de 20 m).</summary>
    public static string Station(double s)
    {
        var e = (int)Math.Floor(s / 20 + 1e-9);
        return $"{e}+{(s - e * 20).ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"))}";
    }
}
