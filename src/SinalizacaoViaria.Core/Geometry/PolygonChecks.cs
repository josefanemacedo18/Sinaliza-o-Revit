namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Verificações de validade de contornos fechados (quadras, glebas, áreas): poucos vértices, área nula, vértices repetidos,
/// lados que se cruzam ou se tocam (laço, "gravata") e espinhos (lado que volta sobre o anterior). Polígonos assim viram
/// pisos inválidos no Revit e áreas erradas nos quadros.
/// </summary>
public static class PolygonChecks
{
    /// <summary>Tolerância (m) para considerar pontos coincidentes e lados que se tocam.</summary>
    public const double DefaultTolerance = 1e-6;

    /// <summary>Área (m²) abaixo da qual o contorno é considerado degenerado.</summary>
    public const double MinArea = 1e-6;

    /// <summary>Contorno sem o último ponto quando ele repete o primeiro (anel fechado explicitamente).</summary>
    public static List<Vec2> Open(IReadOnlyList<Vec2> ring, double tol = DefaultTolerance)
    {
        var r = ring.ToList();
        if (r.Count > 1 && r[0].DistanceTo(r[^1]) <= tol) r.RemoveAt(r.Count - 1);
        return r;
    }

    /// <summary>Vértices consecutivos coincidentes (inclusive o fechamento) – lados de comprimento nulo.</summary>
    public static int RepeatedVertices(IReadOnlyList<Vec2> ring, double tol = DefaultTolerance)
    {
        var r = Open(ring, tol);
        var n = 0;
        for (var i = 0; i < r.Count; i++)
            if (r[i].DistanceTo(r[(i + 1) % r.Count]) <= tol) n++;
        return n;
    }

    /// <summary>
    /// Pares de lados (índice do lado i = do vértice i ao i + 1) que se cruzam ou se tocam fora do vértice comum. Lados
    /// vizinhos só contam quando um volta sobre o outro (espinho). Vértices repetidos são ignorados antes da verificação.
    /// </summary>
    public static List<(int A, int B)> SelfIntersections(IReadOnlyList<Vec2> ring, double tol = DefaultTolerance)
    {
        var r = Dedup(Open(ring, tol), tol);
        var res = new List<(int, int)>();
        var n = r.Count;
        if (n < 3) return res;
        for (var i = 0; i < n; i++)
        {
            var a = r[i];
            var b = r[(i + 1) % n];
            for (var j = i + 1; j < n; j++)
            {
                var c = r[j];
                var d = r[(j + 1) % n];
                // Lados vizinhos (vértice comum b = c, ou a = d no fechamento): espinho = a ponta livre de um sobre o outro.
                if (j == i + 1)
                {
                    if (DistancePointSegment(a, c, d) <= tol || DistancePointSegment(d, a, b) <= tol) res.Add((i, j));
                    continue;
                }
                if (i == 0 && j == n - 1)
                {
                    if (DistancePointSegment(b, c, d) <= tol || DistancePointSegment(c, a, b) <= tol) res.Add((i, j));
                    continue;
                }
                if (SegmentsTouch(a, b, c, d, tol)) res.Add((i, j));
            }
        }
        return res;
    }

    /// <summary>Descrição do primeiro problema do contorno (nulo = válido).</summary>
    public static string? Problem(IReadOnlyList<Vec2> ring, double tol = DefaultTolerance)
    {
        if (ring.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) return "coordenada inválida (NaN/infinito)";
        var r = Dedup(Open(ring, tol), tol);
        if (r.Count < 3) return $"apenas {r.Count} vértice(s) distinto(s)";
        if (Collinear(r, tol)) return "área nula (vértices alinhados)";
        // Antes da área: a "gravata" simétrica tem área com sinal nula, mas o problema dela é o cruzamento.
        var x = SelfIntersections(r, tol);
        if (x.Count > 0) return $"lados que se cruzam ou se tocam ({x.Count} par(es), o primeiro: lados {x[0].A + 1} e {x[0].B + 1})";
        if (Math.Abs(SignedArea(r)) < MinArea) return $"área nula (menor que {MinArea} m²)";
        return null;
    }

    public static double SignedArea(IReadOnlyList<Vec2> r)
    {
        double s = 0;
        for (var i = 0; i < r.Count; i++)
        {
            var p = r[i];
            var q = r[(i + 1) % r.Count];
            s += p.X * q.Y - q.X * p.Y;
        }
        return s / 2;
    }

    public static double DistancePointSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var l2 = ab.LengthSquared;
        if (l2 < 1e-24) return p.DistanceTo(a);
        var t = Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>Segmentos ab e cd se cruzam ou se tocam (a menos de <paramref name="tol"/>).</summary>
    public static bool SegmentsTouch(Vec2 a, Vec2 b, Vec2 c, Vec2 d, double tol = DefaultTolerance)
    {
        // Distância com sinal (m) do ponto à reta do segmento: tolerância em metros, independente do comprimento.
        static double Side(Vec2 s0, Vec2 s1, Vec2 p)
        {
            var v = s1 - s0;
            var len = v.Length;
            return len < 1e-12 ? 0 : v.Cross(p - s0) / len;
        }
        var d1 = Side(c, d, a);
        var d2 = Side(c, d, b);
        var d3 = Side(a, b, c);
        var d4 = Side(a, b, d);
        if (((d1 > tol && d2 < -tol) || (d1 < -tol && d2 > tol)) && ((d3 > tol && d4 < -tol) || (d3 < -tol && d4 > tol)))
            return true;
        return DistancePointSegment(a, c, d) <= tol || DistancePointSegment(b, c, d) <= tol ||
               DistancePointSegment(c, a, b) <= tol || DistancePointSegment(d, a, b) <= tol;
    }

    /// <summary>Todos os vértices sobre a reta pelo primeiro e pelo mais distante dele.</summary>
    private static bool Collinear(List<Vec2> r, double tol)
    {
        var a = r[0];
        var b = r.MaxBy(p => p.DistanceTo(a));
        var len = a.DistanceTo(b);
        if (len <= tol) return true;
        var u = (b - a) / len;
        return r.All(p => Math.Abs(u.Cross(p - a)) <= tol);
    }

    private static List<Vec2> Dedup(List<Vec2> r, double tol)
    {
        var res = new List<Vec2>();
        foreach (var p in r)
            if (res.Count == 0 || res[^1].DistanceTo(p) > tol) res.Add(p);
        while (res.Count > 1 && res[0].DistanceTo(res[^1]) <= tol) res.RemoveAt(res.Count - 1);
        return res;
    }
}
