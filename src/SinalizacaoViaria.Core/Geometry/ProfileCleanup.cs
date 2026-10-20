namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Perfis dos sólidos de perfil (<see cref="Model.ProfileSolid"/>: faces e pictogramas das placas, sarjetões, dispositivos)
/// prontos para o Revit. O Autoteste completo de 2026-10-20 (docs/evidencias) teve o Revit recusando o perfil de duas peças
/// do catálogo de placas ("Curve loops do not satisfy the input requirements"): pictogramas cujo contorno passa duas vezes
/// pelo mesmo vértice ou volta sobre si mesmo com uma lasca de 0,1 mm (a grade de precisão das operações booleanas).
/// <see cref="Problem"/> faz no núcleo o mesmo preparo do contorno que o plugin faz no Revit e aponta esses casos;
/// <see cref="Parts"/> devolve o perfil corrigido.
/// </summary>
public static class ProfileCleanup
{
    /// <summary>Pontos mais próximos que isto saem do contorno no Revit: 1,5 × ShortCurveTolerance (0,00256 pé) ≈ 1,17 mm.</summary>
    public const double RevitDedup = 0.00256 * 0.3048 * 1.5;

    /// <summary>
    /// Lados a menos disto (0,01 mm) contam como encostados: vértice repetido, lasca de largura nula. Nas 168 placas do
    /// catálogo, 1 µm, 0,01 mm e 0,05 mm apontam os mesmos 19 perfis; com 0,1 mm ou mais entram traços finos legítimos das
    /// letras (dois vértices a 0,14 mm), que o Revit aceitou no Autoteste.
    /// </summary>
    public const double Touch = 1e-5;

    /// <summary>
    /// Mesmo preparo de <c>MarkingService.ToCurveLoop3D</c> no plano do perfil: pontos seguidos mais próximos que
    /// <paramref name="tol"/> saem (inclusive o fechamento) e "espinhos" (lado que volta sobre o anterior) são removidos.
    /// </summary>
    public static List<Vec2> Prepare(IReadOnlyList<Vec2> ring, double tol = RevitDedup)
    {
        var pts = new List<Vec2>();
        foreach (var p in ring)
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > tol) pts.Add(p);
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= tol) pts.RemoveAt(pts.Count - 1);
        var changed = true;
        for (var pass = 0; pass < 6 && changed && pts.Count >= 3; pass++)
        {
            changed = false;
            var res = new List<Vec2>(pts.Count);
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[(i - 1 + pts.Count) % pts.Count];
                var b = pts[i];
                var c = pts[(i + 1) % pts.Count];
                var u = b - a;
                var v = c - b;
                var lu = u.Length;
                var lv = v.Length;
                if (lu <= tol) { changed = true; continue; }
                if (lu > 1e-9 && lv > 1e-9 && u.Dot(v) / (lu * lv) < -0.9995) { changed = true; continue; }
                res.Add(b);
            }
            pts = res;
        }
        return pts;
    }

    /// <summary>Primeiro motivo para o Revit recusar o perfil depois do preparo (nulo = aceito).</summary>
    public static string? Problem(Polygon2 profile, double tol = RevitDedup, double touch = Touch)
    {
        var outer = Prepare(profile.Outer, tol);
        if (outer.Count < 3) return "contorno menor que a tolerância do Revit";
        if (PolygonChecks.Problem(outer, touch) is { } p) return "contorno: " + p;
        var rings = new List<List<Vec2>> { outer };
        foreach (var h in profile.Holes)
        {
            var hole = Prepare(h, tol);
            if (hole.Count < 3) continue;   // o plugin descarta o furo menor que a tolerância
            if (PolygonChecks.Problem(hole, touch) is { } hp) return "furo: " + hp;
            rings.Add(hole);
        }
        for (var a = 0; a < rings.Count; a++)
            for (var b = a + 1; b < rings.Count; b++)
                if (RingsTouch(rings[a], rings[b], touch)) return a == 0 ? "furo encosta no contorno" : "furos encostados";
        return null;
    }

    /// <summary>
    /// Perfil aceito pelo Revit: o próprio, se já passa; senão os "pescoços" e as lascas mais finos que 2 × <paramref name="r"/>
    /// saem (abre e fecha com <paramref name="r"/>, 0,5 mm), cada parte que sobra vira um perfil e as que ainda não passam,
    /// menores que a tolerância, somem. Lista vazia = nada modelável.
    /// </summary>
    public static List<Polygon2> Parts(Polygon2 profile, double r = 0.0005)
    {
        if (Problem(profile) == null) return new List<Polygon2> { profile };
        var res = new List<Polygon2>();
        foreach (var part in PolygonOps.Heal(new[] { profile }, r))
        {
            if (Problem(part) == null) { res.Add(part); continue; }
            // Furo encostado que a limpeza não separou: partes sem furo (cortes pelos furos).
            foreach (var piece in PolygonOps.SplitHoles(part))
                if (Problem(piece) == null) res.Add(piece);
        }
        return res;
    }

    private static bool RingsTouch(IReadOnlyList<Vec2> a, IReadOnlyList<Vec2> b, double tol)
    {
        for (var i = 0; i < a.Count; i++)
            for (var j = 0; j < b.Count; j++)
                if (PolygonChecks.SegmentsTouch(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count], tol)) return true;
        return false;
    }
}
