using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Face de meio-fio voltada para a pista: linha (com a calçada à esquerda do sentido) e medidas da guia.</summary>
public sealed record CurbFace(Polyline2 Line, double CurbHeight, double CurbWidth, double GutterWidth, double BaseZ, string? SourceId);

/// <summary>Ponto encontrado junto a um meio-fio.</summary>
public sealed record CurbHit(CurbFace Face, double Station, Vec2 Position, Vec2 Along, double Distance);

/// <summary>Uma fonte de geometria para procurar meios-fios (peças já com recortes aplicados).</summary>
public sealed record CurbSource(MarkingGeometry Geometry, double BaseZ, string? Id, bool AllCurb = false);

/// <summary>
/// Encontra as faces de meio-fio voltadas para a pista em qualquer elemento do projeto (vias, interseções, rotatórias,
/// orelhas, meios-fios avulsos): o lado da guia que encosta no pavimento/sarjeta é a face; a calçada fica do outro lado.
/// Usado para encaixar bocas de lobo e grelhas no meio-fio (um clique ou em série ao longo dele).
/// </summary>
public static class CurbFinder
{
    private static readonly MarkingColor[] RoadColors = { MarkingColor.Asfalto, MarkingColor.Bloquete, MarkingColor.PavimentoConcreto, MarkingColor.PavimentoTerra };

    public static bool IsCurbPiece(MarkingPiece p) => p.Layer is "MEIO-FIO" && p.Solid == null && p.Profile == null;

    public static bool IsRoadPiece(MarkingPiece p) =>
        p.Solid == null && p.Profile == null && p.Elevation + p.Thickness <= 0.03
        && (RoadColors.Contains(p.Color) || p.Layer is "SARJETA" or "SARJETAO" || p.Color == MarkingColor.Concreto && p.Thickness <= 0.02);

    public static List<CurbFace> Faces(IEnumerable<CurbSource> sources)
    {
        var curbs = new List<(Polygon2 Shape, double Top, double Z, string? Id)>();
        var road = new List<Polygon2>();
        var gutters = new List<Polygon2>();
        foreach (var src in sources)
            foreach (var p in src.Geometry.Pieces)
            {
                if (IsCurbPiece(p) || src.AllCurb && p.Solid == null && p.Profile == null && p.Thickness >= 0.05) curbs.Add((p.Shape, p.Elevation + p.Thickness, src.BaseZ, src.Id));
                else if (IsRoadPiece(p))
                {
                    road.Add(p.Shape);
                    if (p.Layer is "SARJETA" || p.Color == MarkingColor.Concreto) gutters.Add(p.Shape);
                }
            }
        var faces = new List<CurbFace>();
        if (curbs.Count == 0 || road.Count == 0) return faces;
        bool InRoad(Vec2 q) => road.Any(r => r.Contains(q));
        foreach (var grp in curbs.GroupBy(c => (Math.Round(c.Top, 2), Math.Round(c.Z, 2))))
        {
            var id = grp.First().Id;
            foreach (var poly in PolygonOps.Union(grp.Select(c => c.Shape)))
            {
                // Arestas cujo lado de fora encosta na pista, orientadas com a guia à esquerda.
                var segs = new List<(Vec2 A, Vec2 B)>();
                foreach (var ring in new[] { poly.Outer }.Concat(poly.Holes))
                    for (int i = 0; i < ring.Count; i++)
                    {
                        var a = ring[i];
                        var b = ring[(i + 1) % ring.Count];
                        var e = b - a;
                        if (e.Length < 0.02) continue;
                        var t = e.Normalized();
                        var m = (a + b) * 0.5;
                        var outDir = poly.Contains(m + t.PerpLeft * 0.01) ? t.PerpRight : t.PerpLeft;
                        if (!InRoad(m + outDir * 0.20) || poly.Contains(m + outDir * 0.05)) continue;
                        // Guia (calçada) à esquerda: a pista fica à direita do sentido.
                        segs.Add(outDir.Dot(t.PerpRight) > 0 ? (a, b) : (b, a));
                    }
                foreach (var chain in Chain(segs))
                {
                    var line = new Polyline2(chain);
                    if (line.Length < 0.3) continue;
                    var mid = line.PointAt(line.Length / 2);
                    var n = line.TangentAt(line.Length / 2).PerpLeft;
                    faces.Add(new CurbFace(line, grp.Key.Item1, Width(poly, mid, n), Gutter(gutters, mid, -n), grp.Key.Item2, id));
                }
            }
        }
        return faces;
    }

    /// <summary>Largura da guia medida da face para dentro.</summary>
    private static double Width(Polygon2 curb, Vec2 face, Vec2 inward)
    {
        for (var d = 0.02; d < 1.0; d += 0.01)
            if (!curb.Contains(face + inward * d)) return Math.Round(d, 2);
        return 0.15;
    }

    /// <summary>Largura da sarjeta de concreto junto à face (0 se não houver).</summary>
    private static double Gutter(List<Polygon2> gutters, Vec2 face, Vec2 outward)
    {
        if (!gutters.Any(g => g.Contains(face + outward * 0.03))) return 0;
        for (var d = 0.05; d < 1.5; d += 0.01)
            if (!gutters.Any(g => g.Contains(face + outward * d))) return Math.Round(d, 2);
        return 0.30;
    }

    /// <summary>Encadeia segmentos orientados em polilinhas (ponta com ponta).</summary>
    private static List<List<Vec2>> Chain(List<(Vec2 A, Vec2 B)> segs)
    {
        var res = new List<List<Vec2>>();
        var left = segs.ToList();
        while (left.Count > 0)
        {
            var cur = new List<Vec2> { left[0].A, left[0].B };
            left.RemoveAt(0);
            var grown = true;
            while (grown)
            {
                grown = false;
                for (int i = 0; i < left.Count; i++)
                {
                    if (left[i].A.DistanceTo(cur[^1]) < 1e-4) { cur.Add(left[i].B); left.RemoveAt(i); grown = true; break; }
                    if (left[i].B.DistanceTo(cur[0]) < 1e-4) { cur.Insert(0, left[i].A); left.RemoveAt(i); grown = true; break; }
                }
            }
            res.Add(Simplify(cur));
        }
        return res;
    }

    private static List<Vec2> Simplify(List<Vec2> pts)
    {
        var res = new List<Vec2> { pts[0] };
        for (int i = 1; i + 1 < pts.Count; i++)
        {
            var a = res[^1];
            var b = pts[i];
            var c = pts[i + 1];
            if (Math.Abs((b - a).Cross(c - b)) < 1e-6 && (b - a).Dot(c - b) > 0) continue;
            res.Add(b);
        }
        res.Add(pts[^1]);
        return res;
    }

    /// <summary>Face mais próxima do ponto (até <paramref name="maxDistance"/> m).</summary>
    public static CurbHit? Nearest(IReadOnlyList<CurbFace> faces, Vec2 p, double maxDistance = 4.0)
    {
        CurbHit? best = null;
        foreach (var f in faces)
        {
            var (s, dist, on) = Project(f.Line, p);
            if (dist > maxDistance || best != null && dist >= best.Distance) continue;
            best = new CurbHit(f, s, on, f.Line.TangentAt(s), dist);
        }
        return best;
    }

    /// <summary>
    /// Posições em série entre as projeções de <paramref name="a"/> e <paramref name="b"/> na mesma face, a cada
    /// <paramref name="spacing"/> m (as duas pontas incluídas).
    /// </summary>
    public static List<CurbHit> Series(CurbFace face, Vec2 a, Vec2 b, double spacing)
    {
        var (sa, _, _) = Project(face.Line, a);
        var (sb, _, _) = Project(face.Line, b);
        var s0 = Math.Min(sa, sb);
        var s1 = Math.Max(sa, sb);
        var res = new List<CurbHit>();
        spacing = Math.Max(2, spacing);
        var n = Math.Max(0, (int)Math.Floor((s1 - s0) / spacing + 1e-6));
        // Distribui igualmente quando sobra um trecho curto no fim.
        var step = n == 0 ? 0 : (s1 - s0) / n;
        if (n > 0 && step > spacing * 1.001) step = spacing;
        for (int i = 0; i <= n; i++)
        {
            var s = Math.Min(s1, s0 + i * step);
            res.Add(new CurbHit(face, s, face.Line.PointAt(s), face.Line.TangentAt(s), 0));
        }
        if (n > 0 && s1 - res[^1].Station > spacing * 0.35)
            res.Add(new CurbHit(face, s1, face.Line.PointAt(s1), face.Line.TangentAt(s1), 0));
        return res;
    }

    /// <summary>Projeção de um ponto na polilinha: estação, distância e ponto projetado.</summary>
    public static (double Station, double Distance, Vec2 Point) Project(Polyline2 line, Vec2 p)
    {
        double best = double.MaxValue, bestS = 0, acc = 0;
        var bestP = line.Points[0];
        for (int i = 0; i + 1 < line.Points.Count; i++)
        {
            var a = line.Points[i];
            var b = line.Points[i + 1];
            var ab = b - a;
            var l2 = ab.Dot(ab);
            var t = l2 < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
            var q = a + ab * t;
            var d = p.DistanceTo(q);
            if (d < best) { best = d; bestS = acc + Math.Sqrt(l2) * t; bestP = q; }
            acc += Math.Sqrt(l2);
        }
        return (bestS, best, bestP);
    }
}
