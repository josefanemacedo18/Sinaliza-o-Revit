using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Sinalização tátil no piso (ABNT NBR 16537:2024): placas moduladas (0,25 m, 0,30 m ou 0,40 m) sobrepostas ao piso, com relevo
/// de alerta (Tabela 1, em malha ortogonal) ou direcional (Tabela 3, barras paralelas ao sentido do deslocamento).
/// </summary>
public static class TactileGenerator
{
    /// <summary>Uma placa (ou fração de placa) com o seu relevo.</summary>
    public sealed record Tile(Polygon2 Shape, bool Alert, List<Polygon2> Relief);

    /// <summary>Placa sobreposta ao piso acabado: desnível de no máximo 2 mm (NBR 16537, 8.2).</summary>
    public const double TileThickness = 0.002;
    /// <summary>Relevo sobre a placa: a altura do relevo do piso sobreposto é a altura total (8.2) – 2 + 2 = 4 mm, o
    /// recomendado das Tabelas 1 e 3 (3 a 5 mm).</summary>
    public const double ReliefHeight = 0.002;
    private const double Joint = 0.004;          // junta entre placas (visível no detalhamento; a placa nominal é o módulo)
    /// <summary>Base do relevo de alerta: 24–28 mm, recomendado 25 (Tabela 1) – octógono com 25 mm entre faces e 27,1 mm
    /// entre vértices.</summary>
    public const double DomeDiameter = 0.025;
    /// <summary>Entre centros dos relevos de alerta: 42–53 mm, recomendado 50; diagonal 60–75 (Tabela 1).</summary>
    public const double DomeSpacing = 0.05;
    private const double DomeSpacingMax = 0.053;
    /// <summary>Base do relevo direcional: 30–40 mm, recomendado 30 (Tabela 3).</summary>
    public const double BarWidth = 0.030;
    /// <summary>Entre centros dos relevos direcionais: 70–85 mm, recomendado 83; entre bases 45–55 (Tabela 3).</summary>
    public const double BarSpacing = 0.083;
    private const double BarSpacingMax = 0.085;

    /// <summary>Relevo de alerta: octógono circunscrito ao círculo da base (25 mm entre faces).</summary>
    private static Polygon2 Dome(Vec2 c, double d)
    {
        var r = d / 2 / Math.Cos(Math.PI / 8);
        return new Polygon2(Enumerable.Range(0, 8).Select(i => c + Vec2.FromAngle(Math.PI / 8 + i * Math.PI / 4) * r));
    }

    /// <summary>Relevos por placa: o maior espaçamento entre centros que a tabela admite ou menos (primeira linha a meio
    /// espaçamento da borda – NOTA das Tabelas 1 e 3).</summary>
    private static int Count(double module, double maxSpacing) => Math.Max(2, (int)Math.Ceiling(module / maxSpacing - 1e-9));

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
                        var n = Count(module, DomeSpacingMax);
                        var sp = module / n;
                        for (int i = 0; i < n; i++)
                            for (int k = 0; k < n; k++)
                            {
                                var ca = a0 + (i + 0.5) * sp;
                                var cb = b0 + (k + 0.5) * sp;
                                if (ca + DomeDiameter / 2 > a1 - j || cb + DomeDiameter / 2 > b1 - j) continue;
                                rel.Add(Dome(P(ca, cb), DomeDiameter));
                            }
                    }
                    else
                    {
                        var n = Count(module, BarSpacingMax);
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
    public static List<Tile> Square(Vec2 c, Vec2 dir, double size, double module, bool relief = true) =>
        Box(c, dir, size, size, module, relief);

    /// <summary>Retângulo de alerta centrado em <paramref name="c"/>: <paramref name="along"/> na direção <paramref name="dir"/>.</summary>
    public static List<Tile> Box(Vec2 c, Vec2 dir, double along, double across, double module, bool relief = true)
    {
        var u = dir.Normalized();
        return Rect(c - u * (along / 2) - u.PerpLeft * (across / 2), u, along, across, module, true, relief);
    }

    /// <summary>Faixa de alerta do rebaixamento de calçada: a 0,50 m da guia (NBR 16537, 6.6, Figuras 22 a 26).</summary>
    public const double RampAlertSetback = 0.50;
    /// <summary>Largura da faixa de alerta nas travessias: 0,40 m (0,40 a 0,60 – Figuras 22 a 30 e 75).</summary>
    public const double CrossingAlert = 0.40;

    /// <summary>Lado da área de alerta nas mudanças de direção: o dobro da largura da faixa direcional (7.4.3).</summary>
    public static double TurnAlert(double width) => 2 * width;

    /// <summary>Lado maior da área de alerta no encontro de três ou quatro faixas: o triplo da largura, até 0,90 m (7.4.4 e 7.4.5).</summary>
    public static double JunctionAlert(double width) => Math.Max(width, Math.Min(3 * width, 0.90));

    /// <summary>Mudança de direção que pede alerta: ângulo entre os trechos de 150° ou menos – deflexão de 30° ou mais (7.4.2, 7.4.3).</summary>
    public const double SharpTurnDeg = 30;

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

    public static MarkingGeometry Route(TactileRouteDefinition d, IReadOnlyList<Polyline2> chains, double elevation = 0.15)
    {
        var geo = new MarkingGeometry();
        var m = Math.Max(0.1, d.Module);
        var width = Math.Max(1, d.Rows) * m;
        var turnSize = Math.Max(TurnAlert(width), Math.Max(1, d.AlertModules) * m);
        var meet = Math.Max(JunctionAlert(width), Math.Max(1, d.AlertModules) * m);
        var endDepth = Math.Max(1, d.EndAlertModules) * m;
        var directional = new List<Tile>();
        var alerts = new List<Tile>();
        var squares = new List<Polygon2>();

        void AddBox(Vec2 c, Vec2 dir, double along, double across)
        {
            alerts.AddRange(Box(c, dir, along, across, m, d.Relief));
            var u = dir.Normalized();
            var v = u.PerpLeft;
            var (ha, hc) = (along / 2, across / 2);
            squares.Add(new Polygon2(new[] { c - u * ha - v * hc, c + u * ha - v * hc, c + u * ha + v * hc, c - u * ha + v * hc }));
        }

        // Junções: extremidade de um caminho sobre outro caminho. Três faixas (Figura 48): área de alerta com o lado maior
        // (3 × largura, até 0,90 m) ao longo da faixa contínua, centrada no ramal, da borda de lá da faixa contínua até uma
        // largura além da borda de cá (2 × largura). Quatro faixas (ramais dos dois lados no mesmo ponto, Figura 51): quadrado
        // de 3 × largura (até 0,90 m) centrado no encontro dos eixos.
        var junctionEnds = new HashSet<(int, bool)>();
        var meets = new List<(int Chain, double S, Vec2 Q, Vec2 T, int Side)>();
        for (int i = 0; i < chains.Count; i++)
            foreach (var atStart in new[] { true, false })
            {
                var e = atStart ? chains[i].Points[0] : chains[i].Points[^1];
                var inner = atStart ? chains[i].PointAt(Math.Min(chains[i].Length, 1.0)) : chains[i].PointAt(Math.Max(0, chains[i].Length - 1.0));
                for (int j = 0; j < chains.Count; j++)
                {
                    if (i == j) continue;
                    var (s, dist, q) = IntersectionGenerator.Project(chains[j], e);
                    if (dist > width / 2 + 0.1 || s < 0.05 || s > chains[j].Length - 0.05) continue;
                    junctionEnds.Add((i, atStart));
                    var t = chains[j].TangentAt(s);
                    meets.Add((j, s, q, t, Math.Sign(t.Cross(inner - q)) is var sg && sg != 0 ? sg : 1));
                    break;
                }
            }
        if (d.AlertAtJunctions && !d.AlertOnly)
        {
            var done = new bool[meets.Count];
            for (int a = 0; a < meets.Count; a++)
            {
                if (done[a]) continue;
                done[a] = true;
                var (ch, s, q, t, side) = meets[a];
                var both = false;
                for (int b = a + 1; b < meets.Count; b++)
                    if (!done[b] && meets[b].Chain == ch && Math.Abs(meets[b].S - s) < width)
                    {
                        done[b] = true;
                        if (meets[b].Side != side) both = true;
                    }
                if (both) AddBox(q, t, meet, meet);
                else AddBox(q + t.PerpLeft * (side * width / 2), t, meet, 2 * width);
            }
        }

        for (int ci = 0; ci < chains.Count; ci++)
        {
            var ch = chains[ci];
            var pts = ch.Points.ToList();
            if (ch.Closed && pts.Count > 2 && pts[0].DistanceTo(pts[^1]) > 1e-6) pts.Add(pts[0]);
            if (pts.Count < 2) continue;
            // Mudanças de direção de 30° ou mais (7.4.3, Figura 47): quadrado de 2 × largura com o canto externo no encontro
            // dos alinhamentos laterais de fora – centro deslocado meia largura para dentro da curva em cada trecho.
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                var a = (pts[i] - pts[i - 1]).Normalized();
                var b = (pts[i + 1] - pts[i]).Normalized();
                var turn = Math.Acos(Math.Clamp(a.Dot(b), -1, 1)) * 180 / Math.PI;
                if (turn >= SharpTurnDeg - 1e-6 && d.AlertAtTurns && !d.AlertOnly) AddBox(pts[i] + (b - a) * (width / 2), a, turnSize, turnSize);
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
