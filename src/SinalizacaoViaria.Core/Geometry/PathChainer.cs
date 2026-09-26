namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Encadeia trechos soltos (linhas, arcos discretizados) em um caminho contínuo, invertendo
/// o sentido dos trechos quando necessário. Equivale a "juntar polilinhas" do CAD.
/// </summary>
public static class PathChainer
{
    public sealed record ChainResult(List<List<Vec2>> Chains, bool AllConnected);

    /// <summary>
    /// Junta os trechos pelas extremidades (tolerância em metros). O primeiro trecho informado
    /// define o sentido do caminho resultante.
    /// </summary>
    public static ChainResult Chain(IReadOnlyList<IReadOnlyList<Vec2>> pieces, double tolerance = 0.01)
    {
        var remaining = pieces.Where(p => p.Count >= 2).Select(p => p.ToList()).ToList();
        var chains = new List<List<Vec2>>();

        while (remaining.Count > 0)
        {
            var chain = new List<Vec2>(remaining[0]);
            remaining.RemoveAt(0);

            bool grew = true;
            while (grew && remaining.Count > 0)
            {
                grew = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    var pc = remaining[i];
                    var head = chain[0];
                    var tail = chain[^1];
                    if (tail.DistanceTo(pc[0]) <= tolerance)
                    {
                        chain.AddRange(pc.Skip(1));
                    }
                    else if (tail.DistanceTo(pc[^1]) <= tolerance)
                    {
                        pc.Reverse();
                        chain.AddRange(pc.Skip(1));
                    }
                    else if (head.DistanceTo(pc[^1]) <= tolerance)
                    {
                        chain.InsertRange(0, pc.Take(pc.Count - 1));
                    }
                    else if (head.DistanceTo(pc[0]) <= tolerance)
                    {
                        pc.Reverse();
                        chain.InsertRange(0, pc.Take(pc.Count - 1));
                    }
                    else continue;

                    remaining.RemoveAt(i);
                    grew = true;
                    break;
                }
            }
            chains.Add(chain);
        }
        return new ChainResult(chains, chains.Count <= 1);
    }

    /// <summary>Verifica se o caminho fecha sobre si mesmo.</summary>
    public static bool IsClosed(IReadOnlyList<Vec2> chain, double tolerance = 0.01) =>
        chain.Count >= 4 && chain[0].DistanceTo(chain[^1]) <= tolerance;
}

/// <summary>Discretização de arcos e utilidades de curvas.</summary>
public static class CurveTools
{
    /// <summary>Número de segmentos para um arco respeitando a flecha máxima (m).</summary>
    public static int SegmentsForArc(double radius, double sweepRad, double maxChordError = 0.005, int minSeg = 2)
    {
        if (radius <= 0) return minSeg;
        var ratio = Math.Clamp(1 - maxChordError / radius, -1, 1);
        var maxStep = 2 * Math.Acos(ratio);
        if (maxStep <= 1e-6) maxStep = Math.PI / 36;
        return Math.Max(minSeg, (int)Math.Ceiling(Math.Abs(sweepRad) / maxStep));
    }

    /// <summary>Pontos de um arco (centro, raio, ângulo inicial, varredura com sinal).</summary>
    public static List<Vec2> Arc(Vec2 center, double radius, double startRad, double sweepRad, double maxChordError = 0.005)
    {
        var n = SegmentsForArc(radius, sweepRad, maxChordError);
        var pts = new List<Vec2>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            var a = startRad + sweepRad * i / n;
            pts.Add(center + Vec2.FromAngle(a) * radius);
        }
        return pts;
    }

    public static List<Vec2> Circle(Vec2 center, double radius, double maxChordError = 0.003)
    {
        var pts = Arc(center, radius, 0, 2 * Math.PI, maxChordError);
        pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    /// <summary>Subdivide segmentos maiores que <paramref name="maxLen"/>.</summary>
    /// <summary>
    /// Arredonda os cantos vivos da polilinha (vértices com deflexão maior que <paramref name="minTurnDeg"/>) com arcos de
    /// raio <paramref name="radius"/>, limitados pelo comprimento disponível dos trechos vizinhos. Arcos já discretizados
    /// (deflexões pequenas) ficam como estão.
    /// </summary>
    public static List<Vec2> FilletCorners(IReadOnlyList<Vec2> pts, double radius, bool closed = false, double minTurnDeg = 8)
    {
        var p = pts.ToList();
        if (closed && p.Count > 3 && p[0].AlmostEquals(p[^1], 1e-6)) p.RemoveAt(p.Count - 1);
        var n = p.Count;
        if (radius <= 0.01 || n < 3) return pts.ToList();
        double Len(int a, int b) => p[(a + n) % n].DistanceTo(p[(b + n) % n]);
        bool IsCorner(int i, out double turn)
        {
            turn = 0;
            if (!closed && (i == 0 || i == n - 1)) return false;
            var d1 = (p[i] - p[(i - 1 + n) % n]).Normalized();
            var d2 = (p[(i + 1) % n] - p[i]).Normalized();
            turn = Math.Acos(Math.Clamp(d1.Dot(d2), -1, 1));
            return turn > minTurnDeg * Math.PI / 180 && turn < Math.PI - 1e-3;
        }
        var corner = new bool[n];
        var turns = new double[n];
        for (int i = 0; i < n; i++) corner[i] = IsCorner(i, out turns[i]);
        var res = new List<Vec2>();
        for (int i = 0; i < n; i++)
        {
            if (!corner[i]) { res.Add(p[i]); continue; }
            var prev = (i - 1 + n) % n;
            var next = (i + 1) % n;
            // Cada trecho é dividido entre as duas pontas quando ambas são cantos.
            var availPrev = Len(prev, i) * (corner[prev] ? 0.5 : 1.0);
            var availNext = Len(i, next) * (corner[next] ? 0.5 : 1.0);
            var half = Math.Tan(turns[i] / 2);
            var t = Math.Min(radius * half, Math.Min(availPrev, availNext) * 0.999);
            var r = t / half;
            if (r < 0.05) { res.Add(p[i]); continue; }
            var d1 = (p[i] - p[prev]).Normalized();
            var d2 = (p[next] - p[i]).Normalized();
            var a = p[i] - d1 * t;
            var left = d1.Cross(d2) > 0;
            var nrm = left ? d1.PerpLeft : d1.PerpRight;
            var c = a + nrm * r;
            var a0 = Math.Atan2(a.Y - c.Y, a.X - c.X);
            var sweep = left ? turns[i] : -turns[i];
            var arc = Arc(c, r, a0, sweep, 0.005);
            res.AddRange(arc);
        }
        if (closed) res.Add(res[0]);
        return res;
    }

    public static List<Vec2> Densify(IReadOnlyList<Vec2> pts, double maxLen)
    {
        var res = new List<Vec2>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0)
            {
                var a = pts[i - 1];
                var b = pts[i];
                var n = (int)Math.Ceiling(a.DistanceTo(b) / maxLen);
                for (int k = 1; k < n; k++) res.Add(Vec2.Lerp(a, b, (double)k / n));
            }
            res.Add(pts[i]);
        }
        return res;
    }
}

/// <summary>
/// Referencial local de um símbolo: origem no ponto de inserção, +Y no sentido do tráfego
/// (para onde o motorista se desloca), +X à direita do motorista.
/// </summary>
public readonly record struct LocalFrame(Vec2 Origin, Vec2 Forward, double Scale = 1.0, bool Mirror = false)
{
    public Vec2 Right => Forward.Normalized().PerpRight;

    public Vec2 ToWorld(Vec2 local)
    {
        var f = Forward.Normalized();
        var r = f.PerpRight;
        var x = Mirror ? -local.X : local.X;
        return Origin + r * (x * Scale) + f * (local.Y * Scale);
    }

    public Polygon2 ToWorld(Polygon2 p) => p.Transform(ToWorld);

    public static LocalFrame FromRotation(Vec2 origin, double rotationDeg, double scale = 1, bool mirror = false) =>
        new(origin, Vec2.FromAngle(Angles.ToRad(90 + rotationDeg)), scale, mirror);
}
