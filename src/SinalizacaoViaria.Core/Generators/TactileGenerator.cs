using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Sinalização tátil no piso (ABNT NBR 16537): placas moduladas (0,25 m ou 0,40 m) com relevo de alerta (domos em
/// malha ortogonal) ou direcional (barras paralelas ao sentido do deslocamento).
/// </summary>
public static class TactileGenerator
{
    /// <summary>Uma placa (ou fração de placa) com o seu relevo.</summary>
    public sealed record Tile(Polygon2 Shape, bool Alert, List<Polygon2> Relief);

    public const double TileThickness = 0.005;
    public const double ReliefHeight = 0.004;
    private const double Joint = 0.004;          // junta entre placas (visível no detalhamento)
    private const double DomeDiameter = 0.025;   // base dos domos: 22–30 mm (NBR 16537)
    private const double DomeSpacing = 0.05;     // entre centros: 42–70 mm
    private const double BarWidth = 0.025;       // barras: 20–40 mm
    private const double BarSpacing = 0.083;     // entre centros: 70–85 mm

    private static Polygon2 Hex(Vec2 c, double r) =>
        new(Enumerable.Range(0, 6).Select(i => c + Vec2.FromAngle(Math.PI / 6 + i * Math.PI / 3) * r));

    /// <summary>Malha de placas no retângulo origem + u·a + v·b (0 ≤ a ≤ lenU, 0 ≤ b ≤ lenV), v = esquerda de u.</summary>
    public static List<Tile> Rect(Vec2 origin, Vec2 u, double lenU, double lenV, double module, bool alert, bool relief = true)
    {
        var res = new List<Tile>();
        u = u.Normalized();
        var v = u.PerpLeft;
        module = Math.Max(0.1, module);
        if (lenU < 0.01 || lenV < 0.01) return res;
        Vec2 P(double a, double b) => origin + u * a + v * b;
        for (double a0 = 0; a0 < lenU - 0.005; a0 += module)
        {
            var a1 = Math.Min(lenU, a0 + module);
            for (double b0 = 0; b0 < lenV - 0.005; b0 += module)
            {
                var b1 = Math.Min(lenV, b0 + module);
                var j = Joint / 2;
                var shape = new Polygon2(new[] { P(a0 + j, b0 + j), P(a1 - j, b0 + j), P(a1 - j, b1 - j), P(a0 + j, b1 - j) });
                var rel = new List<Polygon2>();
                if (relief)
                {
                    if (alert)
                    {
                        var n = Math.Max(2, (int)Math.Round(module / DomeSpacing));
                        var sp = module / n;
                        for (int i = 0; i < n; i++)
                            for (int k = 0; k < n; k++)
                            {
                                var ca = a0 + (i + 0.5) * sp;
                                var cb = b0 + (k + 0.5) * sp;
                                if (ca + DomeDiameter / 2 > a1 - j || cb + DomeDiameter / 2 > b1 - j) continue;
                                rel.Add(Hex(P(ca, cb), DomeDiameter / 2));
                            }
                    }
                    else
                    {
                        var n = Math.Max(2, (int)Math.Round(module / BarSpacing));
                        var sp = module / n;
                        var e0 = a0 + 0.015;
                        var e1 = a1 - 0.015;
                        if (e1 - e0 > 0.03)
                            for (int k = 0; k < n; k++)
                            {
                                var cb = b0 + (k + 0.5) * sp;
                                if (cb + BarWidth / 2 > b1 - j) continue;
                                rel.Add(new Polygon2(new[] { P(e0, cb - BarWidth / 2), P(e1, cb - BarWidth / 2), P(e1, cb + BarWidth / 2), P(e0, cb + BarWidth / 2) }));
                            }
                    }
                }
                res.Add(new Tile(shape, alert, rel));
            }
        }
        return res;
    }

    /// <summary>Faixa de placas centrada num trecho reto a→b.</summary>
    public static List<Tile> Segment(Vec2 a, Vec2 b, double width, double module, bool alert, bool relief = true)
    {
        var len = a.DistanceTo(b);
        if (len < 0.01) return new();
        var u = (b - a) / len;
        return Rect(a - u.PerpLeft * (width / 2), u, len, width, module, alert, relief);
    }

    /// <summary>Quadrado de alerta centrado em <paramref name="c"/>, alinhado com <paramref name="dir"/>.</summary>
    public static List<Tile> Square(Vec2 c, Vec2 dir, double size, double module, bool relief = true)
    {
        var u = dir.Normalized();
        return Rect(c - u * (size / 2) - u.PerpLeft * (size / 2), u, size, size, module, true, relief);
    }

    /// <summary>Acrescenta as placas (e o relevo) à geometria, na elevação indicada.</summary>
    public static void AddTiles(MarkingGeometry geo, IEnumerable<Tile> tiles, MarkingColor color, double elevation, bool relief = true)
    {
        foreach (var t in tiles)
        {
            geo.Pieces.Add(new MarkingPiece(t.Shape, color) { Thickness = TileThickness, Elevation = elevation });
            if (!relief) continue;
            foreach (var r in t.Relief)
                geo.Pieces.Add(new MarkingPiece(r, MarkingColor.RelevoTatil) { Thickness = ReliefHeight, Elevation = elevation + TileThickness });
        }
    }

    /// <summary>Remove (ou recorta) as placas que caem dentro das áreas indicadas.</summary>
    public static List<Tile> Subtract(IEnumerable<Tile> tiles, IReadOnlyList<Polygon2> areas)
    {
        if (areas.Count == 0) return tiles.ToList();
        var res = new List<Tile>();
        foreach (var t in tiles)
        {
            if (areas.Any(a => a.Contains(t.Shape.Centroid) && t.Shape.Outer.All(a.Contains))) continue;
            var parts = PolygonOps.Difference(new[] { t.Shape }, areas);
            if (parts.Count == 0) continue;
            // Relevo recortado junto com a placa (barras parciais descartadas quando muito curtas).
            var rel = PolygonOps.Difference(t.Relief, areas).Where(r => r.Area > 0.00025).ToList();
            foreach (var p in parts.Where(p => p.Area > 1e-4)) res.Add(new Tile(p, t.Alert, rel.Where(r => p.Contains(r.Centroid)).ToList()));
        }
        return res;
    }

    /// <summary>Número de fileiras e módulo para uma largura (0,25 / 0,30 / 0,40 m).</summary>
    public static (double Module, int Rows) ForWidth(double width)
    {
        foreach (var m in new[] { 0.25, 0.40, 0.30 })
        {
            var n = width / m;
            if (Math.Abs(n - Math.Round(n)) < 0.02 && Math.Round(n) >= 1) return (m, (int)Math.Round(n));
        }
        return (Math.Min(0.40, width), Math.Max(1, (int)Math.Round(width / 0.25)));
    }

    // ------------------------------------------------------------------ rota tátil

    private const double SharpTurnDeg = 20;

    public static MarkingGeometry Route(TactileRouteDefinition d, IReadOnlyList<Polyline2> chains, double elevation = 0.15)
    {
        var geo = new MarkingGeometry();
        var m = Math.Max(0.1, d.Module);
        var width = Math.Max(1, d.Rows) * m;
        var alertSize = Math.Max(Math.Max(1, d.AlertModules), Math.Max(1, d.Rows)) * m;
        var endDepth = Math.Max(1, d.EndAlertModules) * m;
        var directional = new List<Tile>();
        var alerts = new List<Tile>();
        var squares = new List<Polygon2>();

        void AddSquare(Vec2 c, Vec2 dir)
        {
            var sq = Square(c, dir, alertSize, m, d.Relief);
            alerts.AddRange(sq);
            var u = dir.Normalized();
            var v = u.PerpLeft;
            var h = alertSize / 2;
            squares.Add(new Polygon2(new[] { c - u * h - v * h, c + u * h - v * h, c + u * h + v * h, c - u * h + v * h }));
        }

        // Junções: extremidade de um caminho sobre outro caminho.
        var junctionEnds = new HashSet<(int, bool)>();
        for (int i = 0; i < chains.Count; i++)
            foreach (var atStart in new[] { true, false })
            {
                var e = atStart ? chains[i].Points[0] : chains[i].Points[^1];
                for (int j = 0; j < chains.Count; j++)
                {
                    if (i == j) continue;
                    var (s, dist, q) = IntersectionGenerator.Project(chains[j], e);
                    if (dist > width / 2 + 0.1 || s < 0.05 || s > chains[j].Length - 0.05) continue;
                    junctionEnds.Add((i, atStart));
                    if (d.AlertAtJunctions && !d.AlertOnly) AddSquare(q, chains[j].TangentAt(s));
                    break;
                }
            }

        for (int ci = 0; ci < chains.Count; ci++)
        {
            var ch = chains[ci];
            var pts = ch.Points.ToList();
            if (ch.Closed && pts.Count > 2 && pts[0].DistanceTo(pts[^1]) > 1e-6) pts.Add(pts[0]);
            if (pts.Count < 2) continue;
            // Mudanças de direção (vértices com deflexão acentuada).
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                var a = (pts[i] - pts[i - 1]).Normalized();
                var b = (pts[i + 1] - pts[i]).Normalized();
                var turn = Math.Acos(Math.Clamp(a.Dot(b), -1, 1)) * 180 / Math.PI;
                if (turn >= SharpTurnDeg && d.AlertAtTurns && !d.AlertOnly) AddSquare(pts[i], a);
            }
            // Extremidades: faixa de alerta com a profundidade indicada.
            var line = new Polyline2(pts);
            var s0 = 0.0;
            var s1 = line.Length;
            if (!d.AlertOnly && d.AlertAtEnds && !ch.Closed)
            {
                if (!junctionEnds.Contains((ci, true)) && line.Length > endDepth + 0.05)
                {
                    alerts.AddRange(Segment(line.PointAt(0), line.PointAt(endDepth), width, m, true, d.Relief));
                    s0 = endDepth;
                }
                if (!junctionEnds.Contains((ci, false)) && line.Length > s0 + endDepth + 0.05)
                {
                    alerts.AddRange(Segment(line.PointAt(line.Length - endDepth), line.PointAt(line.Length), width, m, true, d.Relief));
                    s1 = line.Length - endDepth;
                }
            }
            // Faixa contínua (direcional ou de alerta), trecho a trecho.
            var sub = line.SubPoints(s0, s1);
            for (int i = 0; i + 1 < sub.Count; i++)
                directional.AddRange(Segment(sub[i], sub[i + 1], width, m, d.AlertOnly, d.Relief));
        }

        var clipped = Subtract(directional, PolygonOps.Union(squares));
        AddTiles(geo, clipped, d.Color, elevation, d.Relief);
        AddTiles(geo, alerts, d.Color, elevation, d.Relief);
        geo.PathLength = chains.Sum(c => c.Length);
        geo.PaintedLength = geo.PathLength;
        geo.UnitCount = clipped.Count + alerts.Count;
        return geo;
    }

    /// <summary>Faixas PTA/PTD do catálogo: placas com relevo ao longo do caminho.</summary>
    public static MarkingGeometry Linear(Polyline2 path, double width, bool alert, MarkingColor color, double offset, double elevation = 0.15)
    {
        var geo = new MarkingGeometry();
        var (m, _) = ForWidth(width);
        var line = Math.Abs(offset) > 1e-6 ? path.Offset(offset) : path;
        var tiles = new List<Tile>();
        for (int i = 0; i + 1 < line.Points.Count; i++) tiles.AddRange(Segment(line.Points[i], line.Points[i + 1], width, m, alert));
        AddTiles(geo, tiles, color, elevation);
        geo.PathLength = line.Length;
        geo.PaintedLength = line.Length;
        return geo;
    }
}
