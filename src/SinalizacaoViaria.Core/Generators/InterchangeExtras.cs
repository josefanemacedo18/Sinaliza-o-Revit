using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Um ramo já criado (via do plugin) visto pelo nó: registro + eixo/greide atuais.</summary>
public sealed record RampOnSite(RampRecord Record, PlanRoad Road);

/// <summary>
/// Acabamentos do nó viário montado sobre vias do plugin (as pistas são das vias): zebrados nos narizes, linhas de
/// continuidade nas faixas de mudança de velocidade, pórticos de sinalização antes das saídas, torres de iluminação e grama
/// nos laços, postes ao longo dos ramos e os tabuleiros do anel da rotatória em dois níveis.
/// </summary>
public static class InterchangeExtras
{
    public static MarkingGeometry Build(InterchangeDefinition d, PlanRoad main, PlanRoad cross, IReadOnlyList<RampOnSite> ramps,
        IEnumerable<(Vec2 Center, double Radius, double Z)> roundabouts, IEnumerable<(double A0, double A1)> ringDecks, Func<Vec2, double> ground, double baseZ,
        bool native)
    {
        var geo = new MarkingGeometry();
        PlanRoad? RoadById(string? id) => id == main.Id ? main : id == cross.Id ? cross : null;
        double Rel(double zAbs) => zAbs - baseZ;
        foreach (var rs in ramps)
        {
            var r = rs.Road;
            var rec = rs.Record;
            var L = r.Axis.Length;
            double RampZ(double s) => r.BaseZ + r.Grade.Z(Math.Clamp(s, 0, L));
            foreach (var atStart in new[] { true, false })
            {
                var link = atStart ? rec.StartLink : rec.EndLink;
                if (link != LigacaoRamo.Paralela) continue;
                if (RoadById(atStart ? rec.StartRoad : rec.EndRoad) is not { } road) continue;
                if (d.GoreMarkings) Gore(geo, road, r, atStart, rec.LaneWidth, Rel);
                ContinuityLine(geo, road, r, atStart, rec, Rel);
                if (d.Gantries && atStart && road.Id == main.Id) Gantry(geo, main, r, d.Clearance, Rel);
            }
            if (d.Lighting)
                for (var s = 25.0; s < L - 15; s += 45)
                {
                    var p = r.Axis.PointAt(s);
                    var t = r.Axis.TangentAt(s);
                    var n = t.PerpLeft;
                    var y = -(rec.LaneWidth + rec.Shoulder + 0.6);
                    var z = Rel(RampZ(s) + r.Grade.Z(s, y) - r.Grade.Z(s));
                    Infra.Pole(geo, p + n * y, z, 10, n, t, 2.6);
                }
            if (rec.Role == PapelRamo.Laco) LoopInfield(geo, r, d, ground, native, Rel);
        }
        foreach (var (c, rr, z) in roundabouts)
            if (d.HighMasts && d.Lighting) HighMast(geo, c, Rel(z) + 0.15);
        // Rotatória em dois níveis: tabuleiros do anel sobre a principal (o pavimento é o da rotatória).
        var ring = roundabouts.FirstOrDefault();
        if (ring.Radius > 0)
            foreach (var (a0, a1) in ringDecks)
            {
                var ringR = ring.Radius - 4.5;
                var arc = new Polyline2(CurveTools.Arc(ring.Center, ringR, a0, a1 - a0, 0.01));
                var zr = Rel(ring.Z);
                var spec = new DeckSpec
                {
                    RoadHalf = 5.2, RoadProvided = true, Wear = 0.05, Barrier = TipoGuarda.NewJersey, System = SistemaEstrutural.VigasPreMoldadas,
                    SpanLength = Math.Max(20, arc.Length / 2), PierType = TipoPilar.Circular, PierSize = 1.2, Lighting = false, Markings = false, Native = native,
                    GirderDepth = Math.Max(1.0, d.DeckDepth - 0.4), HostSurface = (_, _) => zr,
                };
                BridgeGenerator.Structure(geo, arc, VerticalProfile.Flat(zr), spec, 0, arc.Length, p => ground(p), d.FillSlope, false, false);
            }
        geo.UnitCount = 1;
        return geo;
    }

    /// <summary>Zebrado do nariz entre o bordo da via e o da faixa do ramo, onde eles se separam (MBST Vol. IV).</summary>
    private static void Gore(MarkingGeometry geo, PlanRoad road, PlanRoad ramp, bool atStart, double lane, Func<double, double> rel)
    {
        var L = ramp.Axis.Length;
        var left = new List<Vec2>();
        var right = new List<Vec2>();
        var zs = new List<double>();
        for (var k = 0.0; k < Math.Min(L, 320); k += 1.0)
        {
            var s = atStart ? k : L - k;
            var p = ramp.Axis.PointAt(s);
            var (st, signed) = road.Axis.Project(p);
            var mp = road.Axis.PointAt(st);
            var edge = signed >= 0 ? road.HalfLeft : road.HalfRight;
            var n = (p - mp).Length < 1e-9 ? road.Axis.TangentAt(st).PerpLeft : (p - mp).Normalized();
            var gap = Math.Abs(signed) - edge;
            if (gap < 0.25) { if (left.Count > 0) break; continue; }
            if (gap > 3.2) break;
            left.Add(mp + n * edge);
            right.Add(p);
            zs.Add(ramp.BaseZ + ramp.Grade.Z(s));
        }
        if (left.Count < 4) return;
        var ring = left.Concat(Enumerable.Reverse(right)).ToList();
        var gore = new Polygon2(ring);
        if (gore.Area < 2) return;
        var z = rel(zs.Average());
        // Nariz pavimentado sob o zebrado (entre a pista da via e a do ramo).
        geo.Pieces.Add(new MarkingPiece(gore, MarkingColor.Asfalto) { Elevation = z - 0.05, Thickness = 0.05, Layer = "NARIZ" });
        var (mn, mx) = gore.Bounds;
        var diag = (mx - mn).Length;
        var dir = (right[^1] - right[0]).Normalized();
        var sd = (dir + dir.PerpLeft).Normalized();
        var c = (mn + mx) * 0.5;
        var stripes = new List<Polygon2>();
        for (var t = -diag; t <= diag; t += 1.6)
            stripes.AddRange(PolygonOps.Strip(new[] { c + sd.PerpLeft * t - sd * diag, c + sd.PerpLeft * t + sd * diag }, 0.40));
        foreach (var part in PolygonOps.Intersect(new[] { gore }, stripes).Concat(PolygonOps.Strip(ring.Append(ring[0]).ToList(), 0.15)))
            if (part.Area > 0.01)
                geo.Pieces.Add(new MarkingPiece(part, MarkingColor.Branca) { Elevation = z + 0.006, Thickness = 0.003, Layer = "ZEBRADO" });
    }

    /// <summary>
    /// Linha de continuidade (tracejada 1 × 1 m) entre a faixa de mudança de velocidade e a faixa da via, do fim do taper até o
    /// nariz – onde a linha de bordo da via foi recortada.
    /// </summary>
    private static void ContinuityLine(MarkingGeometry geo, PlanRoad road, PlanRoad ramp, bool atStart, RampRecord rec, Func<double, double> rel)
    {
        var L = ramp.Axis.Length;
        var s0 = Math.Min(rec.Taper, L / 3);
        double? end = null;
        for (var k = s0; k < Math.Min(L, 500); k += 1)
        {
            var s = atStart ? k : L - k;
            var (_, signed) = road.Axis.Project(ramp.Axis.PointAt(s));
            var edge = signed >= 0 ? road.HalfLeft : road.HalfRight;
            if (Math.Abs(signed) - edge > 0.4) { end = k; break; }
        }
        if (end is not { } e || e - s0 < 5) return;
        for (var k = s0; k + 1 <= e; k += 2)
        {
            var sa = atStart ? k : L - k - 1;
            var sb = sa + 1;
            var pts = new List<Vec2> { ramp.Axis.PointAt(sa) - ramp.Axis.TangentAt(sa).PerpLeft * 0.10, ramp.Axis.PointAt(sb) - ramp.Axis.TangentAt(sb).PerpLeft * 0.10 };
            foreach (var part in PolygonOps.Strip(pts, 0.20))
                geo.Pieces.Add(new MarkingPiece(part, MarkingColor.Branca) { Elevation = rel(ramp.BaseZ + ramp.Grade.Z(sa)) + 0.006, Thickness = 0.003, Layer = "LINHA" });
        }
    }

    /// <summary>Pórtico de sinalização indicativa sobre a principal, 150 m antes do início da saída.</summary>
    private static void Gantry(MarkingGeometry geo, PlanRoad main, PlanRoad ramp, double clearance, Func<double, double> rel)
    {
        var start = ramp.Axis.PointAt(0);
        var (ms, signed) = main.Axis.Project(start);
        var side = Math.Sign(signed);
        var travel = side < 0 ? 1 : -1;
        var s = Math.Clamp(ms - travel * 150, 5, main.Axis.Length - 5);
        var p = main.Axis.PointAt(s);
        var t = main.Axis.TangentAt(s);
        var n = t.PerpLeft;
        var z = rel(main.BaseZ + main.Grade.Z(s));
        var y0 = side * 0.8;
        var y1 = side * ((side > 0 ? main.TotalLeft : main.TotalRight) + 1.5);
        var top = z + clearance + 0.6;
        foreach (var y in new[] { y0, y1 })
        {
            SolidSweep.Add(geo, SolidSweep.Box(p + n * y, t, 1.2, 1.2, z - 0.3, z + 0.3), MarkingColor.Concreto, "PORTICO");
            SolidSweep.AddColumn(geo, p + n * y, 0.22, 0.20, z + 0.3, top + 1.6, MarkingColor.Metal, "PORTICO", true);
        }
        var a = p + n * y0;
        var b = p + n * y1;
        foreach (var dz in new[] { top, top + 1.4 })
            SolidSweep.AddRound(geo, Vec3.At(a, dz), Vec3.At(b, dz), 0.09, 0.09, MarkingColor.Metal, "PORTICO", n: 12);
        var k = Math.Max(2, (int)((b - a).Length / 1.4));
        for (int i = 0; i < k; i++)
        {
            var q0 = a + (b - a) * (i / (double)k);
            var q1 = a + (b - a) * ((i + 1) / (double)k);
            SolidSweep.AddRound(geo, Vec3.At(q0, top), Vec3.At(q1, top + 1.4), 0.035, 0.035, MarkingColor.Metal, "PORTICO", n: 8);
        }
        var c = a + (b - a) * 0.6;
        var face = -t * travel;
        var w = Math.Min((b - a).Length * 0.8, 7.5);
        SolidSweep.Add(geo, SolidSweep.Box(c + face * 0.25, n, w, 0.06, top - 0.3, top + 2.1), MarkingColor.Verde, "PLACA");
        SolidSweep.Add(geo, SolidSweep.Box(c + face * 0.285, n, w - 0.2, 0.01, top - 0.2, top + 2.0), MarkingColor.Branca, "PLACA");
        SolidSweep.Add(geo, SolidSweep.Box(c + face * 0.30, n, w - 0.34, 0.01, top - 0.13, top + 1.93), MarkingColor.Verde, "PLACA");
        SolidSweep.Add(geo, SolidSweep.Box(c + face * 0.315 + n * (side * 1.5), n, 0.18, 0.01, top + 0.2, top + 1.3), MarkingColor.Branca, "PLACA");
        // Ponta da seta.
        var tip = c + face * 0.315 + n * (side * 1.5);
        SolidSweep.Add(geo, SolidSweep.Box(tip + n * (side * 0.18), n, 0.5, 0.01, top + 1.1, top + 1.3), MarkingColor.Branca, "PLACA");
    }

    private static void LoopInfield(MarkingGeometry geo, PlanRoad loop, InterchangeDefinition d, Func<Vec2, double> ground, bool native, Func<double, double> rel)
    {
        var pts = loop.Axis.Points;
        var n = pts.Count;
        var mid = pts.Skip(n / 5).Take(n * 3 / 5).ToList();
        if (mid.Count < 3) return;
        var c = new Vec2(mid.Average(p => p.X), mid.Average(p => p.Y));
        var r = mid.Average(p => p.DistanceTo(c)) - 8;
        if (r < 8) return;
        geo.TerrainFinishes.Add((new Polygon2(CurveTools.Circle(c, r, 0.05)), MarkingColor.Grama, "Ilha do laço"));
        if (d.HighMasts && d.Lighting) HighMast(geo, c, ground(c));
    }

    /// <summary>Torre de iluminação de 30 m com coroa de projetores.</summary>
    public static void HighMast(MarkingGeometry geo, Vec2 c, double z)
    {
        SolidSweep.AddColumn(geo, c, 1.0, 1.0, z - 0.8, z + 0.2, MarkingColor.Concreto, "ILUMINACAO");
        SolidSweep.AddColumn(geo, c, 0.32, 0.16, z + 0.2, z + 30, MarkingColor.Metal, "ILUMINACAO", true);
        SolidSweep.AddRound(geo, Vec3.At(c, z + 29.6), Vec3.At(c, z + 29.9), 1.2, 1.2, MarkingColor.Metal, "ILUMINACAO", false, 1.05, 1.05, 32);
        for (int i = 0; i < 8; i++)
        {
            var dir = Vec2.FromAngle(2 * Math.PI * i / 8);
            SolidSweep.Add(geo, SolidSweep.Box(c + dir * 1.25, dir, 0.45, 0.6, z + 29.3, z + 29.75), MarkingColor.Metal, "ILUMINACAO");
            SolidSweep.Add(geo, SolidSweep.Box(c + dir * 1.49, dir, 0.03, 0.5, z + 29.35, z + 29.7), MarkingColor.Vidro, "ILUMINACAO");
        }
    }
}
