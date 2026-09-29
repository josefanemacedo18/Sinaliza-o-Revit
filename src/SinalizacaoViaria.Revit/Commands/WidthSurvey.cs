using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Leitura do levantamento: linhas existentes do meio-fio/bordo e do alinhamento (muro) de cada lado são medidas em
/// relação ao eixo a cada poucos metros e viram a tabela de larguras variáveis da via.
/// </summary>
internal static class WidthSurvey
{
    /// <summary>Medidas lidas (vazio se o usuário pulou tudo).</summary>
    public static List<PontoLargura> Read(UIDocument uidoc, Polyline2 axis, double step = 5.0)
    {
        var sides = new (string Prompt, bool Left, bool Lot)[]
        {
            ("Levantamento: selecione a(s) linha(s) do BORDO DIREITO existente (face do meio-fio ou sarjeta) e Concluir – ESC pula", false, false),
            ("Levantamento: selecione a(s) linha(s) do BORDO ESQUERDO existente e Concluir – ESC pula", true, false),
            ("Levantamento: selecione a(s) linha(s) do ALINHAMENTO DIREITO (muro, divisa dos lotes) e Concluir – ESC pula", false, true),
            ("Levantamento: selecione a(s) linha(s) do ALINHAMENTO ESQUERDO e Concluir – ESC pula", true, true),
        };
        var series = new Dictionary<(bool Left, bool Lot), List<(double S, double D)>>();
        foreach (var (prompt, left, lot) in sides)
        {
            uidoc.Selection.SetElementIds(new List<ElementId>());
            var curves = Picking.PickCurves(uidoc, prompt);
            if (curves == null) continue;
            var samples = new List<(double S, double D)>();
            foreach (var c in curves)
            {
                var pts = PathResolver.Tessellate(c.GeometryCurve).Select(UnitConv.ToVec2).ToList();
                // Densifica: medidas a cada ~1 m ao longo da linha do levantamento.
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var n = Math.Max(1, (int)Math.Ceiling(pts[i].DistanceTo(pts[i + 1])));
                    for (int k = 0; k <= n; k++)
                    {
                        var p = pts[i] + (pts[i + 1] - pts[i]) * ((double)k / n);
                        var (st, signed) = axis.Project(p);
                        if (st <= 1e-3 && signed != 0 && Math.Abs(p.DistanceTo(axis.PointAt(0))) > Math.Abs(signed) + 0.5) continue;   // antes do eixo
                        if (left ? signed <= 0.3 : signed >= -0.3) continue;                                                            // lado errado
                        samples.Add((st, Math.Abs(signed)));
                    }
                }
            }
            if (samples.Count >= 2) series[(left, lot)] = samples.OrderBy(x => x.S).ToList();
        }
        if (series.Count == 0) return new();

        double? Value(List<(double S, double D)> sm, double s)
        {
            // Média das medidas a menos de 1,5 m da estaca; fora do trecho medido, a medida da ponta mais próxima.
            var near = sm.Where(x => Math.Abs(x.S - s) <= 1.5).Select(x => x.D).ToList();
            if (near.Count > 0) return Math.Round(near.Average(), 3);
            var before = sm.LastOrDefault(x => x.S < s);
            var after = sm.FirstOrDefault(x => x.S > s);
            if (before == default) return Math.Round(after.D, 3);
            if (after == default) return Math.Round(before.D, 3);
            var u = (s - before.S) / Math.Max(1e-6, after.S - before.S);
            return Math.Round(before.D + (after.D - before.D) * u, 3);
        }
        var s0 = series.Values.Min(v => v[0].S);
        var s1 = series.Values.Max(v => v[^1].S);
        var res = new List<PontoLargura>();
        for (var s = Math.Floor(s0 / step) * step; s <= s1 + 1e-6; s += step)
        {
            var st = Math.Clamp(s, 0, axis.Length);
            res.Add(new PontoLargura
            {
                Estaca = st,
                BordoDireito = series.TryGetValue((false, false), out var a) ? Value(a, st) : null,
                BordoEsquerdo = series.TryGetValue((true, false), out var b) ? Value(b, st) : null,
                AlinhamentoDireito = series.TryGetValue((false, true), out var c) ? Value(c, st) : null,
                AlinhamentoEsquerdo = series.TryGetValue((true, true), out var d) ? Value(d, st) : null,
            });
        }
        // Remove pontos redundantes (colineares com os vizinhos, tolerância de 2 cm).
        var clean = new List<PontoLargura>();
        foreach (var p in res)
        {
            if (clean.Count >= 2)
            {
                var a0 = clean[^2];
                var a1 = clean[^1];
                bool Lin(double? x0, double? x1, double? x2) =>
                    x0 == null && x1 == null && x2 == null ||
                    x0 is { } q0 && x1 is { } q1 && x2 is { } q2 && Math.Abs(q1 - (q0 + (q2 - q0) * (a1.Estaca - a0.Estaca) / Math.Max(1e-6, p.Estaca - a0.Estaca))) < 0.02;
                if (Lin(a0.BordoDireito, a1.BordoDireito, p.BordoDireito) && Lin(a0.BordoEsquerdo, a1.BordoEsquerdo, p.BordoEsquerdo)
                    && Lin(a0.AlinhamentoDireito, a1.AlinhamentoDireito, p.AlinhamentoDireito) && Lin(a0.AlinhamentoEsquerdo, a1.AlinhamentoEsquerdo, p.AlinhamentoEsquerdo))
                    clean.RemoveAt(clean.Count - 1);
            }
            clean.Add(p);
        }
        return clean;
    }
}
