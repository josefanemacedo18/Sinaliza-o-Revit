namespace SinalizacaoViaria.Core.Geometry;

/// <summary>Vértice de um traçado horizontal: ponto de interseção (PI), raio da curva circular e comprimento das espirais.</summary>
public readonly record struct Pi(Vec2 P, double Radius = 0, double Spiral = 0);

/// <summary>
/// Traçado horizontal por PIs com curvas circulares concordadas por espirais de transição (clotoides) – DNIT, Manual de
/// Projeto Geométrico. Amostrado finamente (curvatura contínua, sem quebras visíveis).
/// </summary>
public static class Tracado
{
    /// <summary>
    /// Gera o eixo pelos PIs. Raios que não cabem entre os PIs vizinhos são reduzidos (e avisados em
    /// <paramref name="warnings"/>).
    /// </summary>
    public static Polyline2 Build(IReadOnlyList<Pi> pis, double step = 0.75, List<string>? warnings = null)
    {
        if (pis.Count < 2) throw new ArgumentException("São precisos ao menos dois pontos.");
        var n = pis.Count;
        // Tangentes externas de cada curva (T) e fator de redução para caber.
        var curves = new (double R, double Ls, double Delta, double T)[n];
        for (int i = 1; i + 1 < n; i++)
        {
            var din = (pis[i].P - pis[i - 1].P).Normalized();
            var dout = (pis[i + 1].P - pis[i].P).Normalized();
            var delta = Math.Atan2(din.Cross(dout), din.Dot(dout));
            var r = Math.Max(0, pis[i].Radius);
            if (r < 0.5 || Math.Abs(delta) < 1e-4) { curves[i] = (0, 0, delta, 0); continue; }
            var ls = Math.Clamp(pis[i].Spiral, 0, r * Math.Abs(delta) * 0.95);
            curves[i] = (r, ls, delta, Tangent(r, ls, Math.Abs(delta)));
        }
        // Reduz raios/espirais que não cabem nos trechos entre PIs.
        for (int pass = 0; pass < 8; pass++)
        {
            var ok = true;
            for (int i = 0; i + 1 < n; i++)
            {
                var len = pis[i].P.DistanceTo(pis[i + 1].P);
                var need = curves[i].T + curves[i + 1].T;
                if (need <= len * 0.999 || need < 1e-9) continue;
                ok = false;
                var k = len * 0.98 / need;
                foreach (var j in new[] { i, i + 1 })
                {
                    if (curves[j].R <= 0) continue;
                    var (r, ls, dl, _) = curves[j];
                    r *= k; ls *= k;
                    curves[j] = (r, ls, dl, Tangent(r, ls, Math.Abs(dl)));
                }
            }
            if (ok) break;
            if (pass == 0) warnings?.Add("Curvas reduzidas para caber entre os pontos do traçado.");
        }
        var pts = new List<Vec2> { pis[0].P };
        for (int i = 1; i + 1 < n; i++)
        {
            var (r, ls, delta, t) = curves[i];
            if (r <= 0) { pts.Add(pis[i].P); continue; }
            var din = (pis[i].P - pis[i - 1].P).Normalized();
            var ts = pis[i].P - din * t;
            pts.Add(ts);
            pts.AddRange(Transition(ts, din, r, ls, delta, step).Skip(1));
        }
        pts.Add(pis[^1].P);
        // Remove pontos coincidentes.
        var clean = new List<Vec2> { pts[0] };
        foreach (var p in pts.Skip(1)) if (p.DistanceTo(clean[^1]) > 1e-4) clean.Add(p);
        return new Polyline2(clean);
    }

    /// <summary>Tangente externa T = (R + p)·tg(Δ/2) + k da curva com espirais simétricas.</summary>
    private static double Tangent(double r, double ls, double delta)
    {
        if (ls < 1e-6) return r * Math.Tan(delta / 2);
        var (xs, ys) = SpiralEnd(r, ls);
        var ts = ls / (2 * r);
        var p = ys - r * (1 - Math.Cos(ts));
        var k = xs - r * Math.Sin(ts);
        return (r + p) * Math.Tan(delta / 2) + k;
    }

    /// <summary>Coordenadas do fim da espiral (x ao longo da tangente, y lateral).</summary>
    private static (double X, double Y) SpiralEnd(double r, double ls)
    {
        double x = 0, y = 0;
        const int m = 200;
        var ds = ls / m;
        for (int i = 0; i < m; i++)
        {
            var s = (i + 0.5) * ds;
            var h = s * s / (2 * r * ls);
            x += Math.Cos(h) * ds;
            y += Math.Sin(h) * ds;
        }
        return (x, y);
    }

    /// <summary>Espiral + arco + espiral a partir do TS, integrando a curvatura.</summary>
    private static List<Vec2> Transition(Vec2 ts, Vec2 dir, double r, double ls, double delta, double step)
    {
        var sign = Math.Sign(delta);
        var lc = Math.Max(0, r * Math.Abs(delta) - ls);
        var total = 2 * ls + lc;
        double Kappa(double s) => sign * (s < ls ? s / (r * Math.Max(1e-9, ls)) : s < ls + lc ? 1 / r : (total - s) / (r * Math.Max(1e-9, ls)));
        // Passo menor em curvas fechadas (≤ 1,5° por passo).
        var ds = Math.Min(step, r * 1.5 * Math.PI / 180);
        var m = Math.Max(2, (int)Math.Ceiling(total / ds));
        ds = total / m;
        var heading = Math.Atan2(dir.Y, dir.X);
        var p = ts;
        var res = new List<Vec2> { p };
        const int sub = 4;
        for (int i = 0; i < m; i++)
        {
            for (int k = 0; k < sub; k++)
            {
                var s = i * ds + (k + 0.5) * ds / sub;
                var h = heading + Kappa(s) * ds / sub / 2;
                p += Vec2.FromAngle(h) * (ds / sub);
                heading += Kappa(s) * ds / sub;
            }
            res.Add(p);
        }
        return res;
    }
}
