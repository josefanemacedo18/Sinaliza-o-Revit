using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Via participante de uma interseção (pavimento/seção + eixo resolvido).</summary>
public sealed record IntersectionRoad(RoadPavementDefinition Def, Polyline2 Axis);

/// <summary>Ramo de uma via a partir do nó da interseção.</summary>
public sealed record IntersectionLeg(int Road, int Sign, double NodeStation, double Clear, Vec2 Dir)
{
    /// <summary>Estação no eixo a uma distância <paramref name="t"/> do nó, ao longo do ramo.</summary>
    public double StationAt(double t) => NodeStation + Sign * t;
}

/// <summary>Resultado geométrico de uma interseção.</summary>
public sealed class IntersectionLayout
{
    public Vec2 Node { get; init; }
    public double Radius { get; set; }
    public Polygon2 Zone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    public List<IntersectionRoad> Roads { get; } = new();
    public List<Polygon2> Pavement { get; } = new();
    public List<Polygon2> Curb { get; } = new();
    public List<Polygon2> Sidewalk { get; } = new();
    public List<Polygon2> MedianCurb { get; } = new();
    public List<Polygon2> MedianCore { get; } = new();
    public List<IntersectionLeg> Legs { get; } = new();
    /// <summary>Por via: áreas onde a sinalização pintada é removida (miolo + aproximações até a retenção).</summary>
    public Dictionary<int, List<Polygon2>> PaintCuts { get; } = new();
    /// <summary>Por via: áreas onde as vagas são removidas (pintura + 5 m antes da esquina, CTB art. 181).</summary>
    public Dictionary<int, List<Polygon2>> ParkingCuts { get; } = new();
    public List<string> Warnings { get; } = new();
    public MarkingColor PavementColor { get; set; } = MarkingColor.Asfalto;
    public double PavementThickness { get; set; } = 0.05;
    public double CurbHeight { get; set; } = 0.15;
}

/// <summary>
/// Geometria de interseções entre vias do "Sinalizar via": pavimento contínuo no miolo, esquinas com raio na face do
/// meio-fio, calçadas e canteiros reconstruídos junto ao nó e recortes da sinalização das vias.
/// </summary>
public static class IntersectionGenerator
{
    public const double NodeMergeDistance = 3.0;
    private static readonly string[] PhysicalCodes = { "CALCADA", "GRAMADO", "SARJETA", "SARJETAO" };

    public static bool IsPhysical(MarkingDefinition d) =>
        d is LinearMarkingDefinition l && (PhysicalCodes.Contains(l.Code) || l.Code.StartsWith("MEIO-FIO"));

    private static Polygon2 Circle(Vec2 c, double r) => new(CurveTools.Circle(c, r, Math.Max(0.005, r * 0.0015)));

    /// <summary>Projeção de um ponto no eixo: estação e distância.</summary>
    public static (double Station, double Distance, Vec2 Point) Project(Polyline2 axis, Vec2 p)
    {
        double best = double.MaxValue, bestS = 0;
        Vec2 bestP = axis.Points[0];
        double s = 0;
        for (int i = 0; i + 1 < axis.Points.Count; i++)
        {
            var a = axis.Points[i];
            var b = axis.Points[i + 1];
            var ab = b - a;
            var len = ab.Length;
            var t = len < 1e-9 ? 0 : Math.Clamp((p - a).Dot(ab) / (len * len), 0, 1);
            var q = a + ab * t;
            var d = q.DistanceTo(p);
            if (d < best) { best = d; bestS = s + t * len; bestP = q; }
            s += len;
        }
        return (bestS, best, bestP);
    }

    /// <summary>Eixo prolongado até o nó quando a via termina junto a outra (entroncamento em T).</summary>
    public static Polyline2 ExtendTo(Polyline2 axis, Vec2 node, double tolerance)
    {
        var pts = axis.Points.ToList();
        if (pts[0].DistanceTo(node) <= tolerance && pts[0].DistanceTo(node) > 0.01) pts.Insert(0, node);
        else if (pts[^1].DistanceTo(node) <= tolerance && pts[^1].DistanceTo(node) > 0.01) pts.Add(node);
        return new Polyline2(pts);
    }

    // ------------------------------------------------------------------ nós

    /// <summary>Encontra os cruzamentos (e entroncamentos em T) entre os eixos das vias.</summary>
    public static List<(Vec2 Node, List<int> Roads)> FindNodes(IReadOnlyList<IntersectionRoad> roads)
    {
        var raw = new List<(Vec2, int, int)>();
        for (int i = 0; i < roads.Count; i++)
            for (int j = i + 1; j < roads.Count; j++)
            {
                var a = roads[i].Axis;
                var b = roads[j].Axis;
                foreach (var p in AxisCrossings(a, b)) raw.Add((p, i, j));
                // Entroncamentos: extremidade de uma via junto ao eixo (ou à pista) da outra.
                foreach (var (x, y) in new[] { (i, j), (j, i) })
                {
                    var ax = roads[x].Axis;
                    var tol = Math.Max(roads[y].Def.TotalLeft, roads[y].Def.TotalRight) + 2.0;
                    foreach (var end in new[] { ax.Points[0], ax.Points[^1] })
                    {
                        var (_, dist, q) = Project(roads[y].Axis, end);
                        if (dist <= tol && !raw.Any(r => r.Item1.DistanceTo(q) < NodeMergeDistance)) raw.Add((q, x, y));
                    }
                }
            }
        var nodes = new List<(Vec2 Node, List<int> Roads)>();
        foreach (var (p, i, j) in raw)
        {
            var k = nodes.FindIndex(n => n.Node.DistanceTo(p) < NodeMergeDistance);
            if (k < 0) nodes.Add((p, new List<int> { i, j }));
            else
            {
                if (!nodes[k].Roads.Contains(i)) nodes[k].Roads.Add(i);
                if (!nodes[k].Roads.Contains(j)) nodes[k].Roads.Add(j);
            }
        }
        return nodes;
    }

    private static IEnumerable<Vec2> AxisCrossings(Polyline2 a, Polyline2 b)
    {
        for (int i = 0; i + 1 < a.Points.Count; i++)
            for (int j = 0; j + 1 < b.Points.Count; j++)
            {
                var p = a.Points[i];
                var r = a.Points[i + 1] - p;
                var q = b.Points[j];
                var s = b.Points[j + 1] - q;
                var den = r.Cross(s);
                if (Math.Abs(den) < 1e-9) continue;
                var t = (q - p).Cross(s) / den;
                var u = (q - p).Cross(r) / den;
                if (t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9) yield return p + r * t;
            }
    }

    // ------------------------------------------------------------------ layout

    public static IntersectionLayout Layout(IntersectionDefinition d, IReadOnlyList<IntersectionRoad> input)
    {
        var L = new IntersectionLayout { Node = d.Node };
        if (input.Count < 2)
        {
            L.Warnings.Add("A interseção precisa de pelo menos duas vias com pavimento (Sinalizar via).");
            return L;
        }
        var node = d.Node;
        var rc = Math.Max(0, d.CornerRadius);
        var maxHalf = input.Max(r => Math.Max(r.Def.TotalLeft, r.Def.TotalRight));
        foreach (var r in input) L.Roads.Add(r with { Axis = ExtendTo(r.Axis, node, maxHalf + 3) });
        L.PavementColor = L.Roads[0].Def.Color;
        L.PavementThickness = L.Roads[0].Def.ActualThickness;
        L.CurbHeight = L.Roads[0].Def.CurbHeight;
        var cw = L.Roads.Max(r => r.Def.CurbWidth);

        // Ângulo mínimo entre as vias (define o alcance das esquinas).
        var dirs = L.Roads.Select(r => r.Axis.TangentAt(Project(r.Axis, node).Station)).ToList();
        var minSin = 1.0;
        for (int i = 0; i < dirs.Count; i++)
            for (int j = i + 1; j < dirs.Count; j++)
                minSin = Math.Min(minSin, Math.Abs(dirs[i].Cross(dirs[j])));
        if (minSin < 0.5) L.Warnings.Add("Vias muito oblíquas (< 30°): confira as esquinas e considere canalizar a interseção.");
        var r1 = Math.Min(150, maxHalf / Math.Max(0.25, minSin) * 2 + 2 * rc + 12);
        var big = Circle(node, r1);

        // Pista de cada via: com canteiros (Cw) e contínua (Cn); o cruzamento das pistas é pavimento contínuo.
        List<Polygon2> Carriage(IntersectionRoad r, bool medians)
        {
            var band = RoadGenerator.Band(r.Axis, -r.Def.RightWidth, r.Def.LeftWidth);
            if (!medians) return band;
            var gaps = r.Def.Gaps.Where(g => g.Median).SelectMany(g => RoadGenerator.Band(r.Axis, g.Offset - g.Width / 2, g.Offset + g.Width / 2));
            return PolygonOps.Difference(band, gaps);
        }
        var cn = L.Roads.Select(r => PolygonOps.Intersect(Carriage(r, false), new[] { big })).ToList();
        var cwp = L.Roads.Select(r => PolygonOps.Intersect(Carriage(r, true), new[] { big })).ToList();
        var parts = cwp.SelectMany(p => p).ToList();
        for (int i = 0; i < cn.Count; i++)
            for (int j = i + 1; j < cn.Count; j++)
                parts.AddRange(PolygonOps.Intersect(cn[i], cn[j]));
        var u = PolygonOps.Union(parts);
        var pav = rc > 0.05 ? PolygonOps.Offset(PolygonOps.Offset(u, rc, true), -rc, true) : u;
        pav = PolygonOps.Intersect(pav, new[] { Circle(node, r1 - rc - 1) });

        // Ramos e distância livre (fim da curva da esquina) em cada um.
        foreach (var (r, i) in L.Roads.Select((r, i) => (r, i)))
        {
            var ownN = PolygonOps.Offset(cn[i], 0.02);
            var others = PolygonOps.Difference(pav, ownN);
            var (sn, _, _) = Project(r.Axis, node);
            foreach (var sign in new[] { -1, 1 })
            {
                var avail = sign > 0 ? r.Axis.Length - sn : sn;
                if (avail < 3) continue;
                double clear = -1;
                for (var t = 0.0; t <= Math.Min(avail, r1 - rc - 2); t += 0.25)
                {
                    var s = sn + sign * t;
                    var p = r.Axis.PointAt(s);
                    var nrm = r.Axis.TangentAt(s).PerpLeft;
                    var a = p - nrm * (r.Def.RightWidth + cw);
                    var b = p + nrm * (r.Def.LeftWidth + cw);
                    var hit = others.Any(o =>
                    {
                        var (mn, mx) = o.Bounds;
                        if (Math.Max(a.X, b.X) < mn.X || Math.Min(a.X, b.X) > mx.X || Math.Max(a.Y, b.Y) < mn.Y || Math.Min(a.Y, b.Y) > mx.Y) return false;
                        return DetailGenerator.SegmentIntervals(o, a, b).Sum(iv => iv.T1 - iv.T0) * a.DistanceTo(b) > 0.02;
                    });
                    if (!hit) { clear = t; break; }
                }
                if (clear < 0) { L.Warnings.Add("Um dos ramos é curto demais para a esquina – confira o raio."); clear = Math.Min(avail, r1 - rc - 2); }
                if (avail < clear + 1) continue;
                L.Legs.Add(new IntersectionLeg(i, sign, sn, clear, r.Axis.TangentAt(sn + sign * Math.Min(clear, avail)) * sign));
            }
        }
        var rn = Math.Max(L.Legs.Count > 0 ? L.Legs.Max(l => l.Clear) + 0.3 : maxHalf + rc, maxHalf + 1);
        L.Radius = rn;
        L.Zone = Circle(node, rn);
        var zone = new[] { L.Zone };

        // Miolo: pavimento, meio-fio das esquinas, calçadas e pontas de canteiro reconstruídos dentro da zona.
        var pavZ = PolygonOps.Intersect(pav, zone);
        L.Pavement.AddRange(pavZ);
        var sideBands = L.Roads.SelectMany(r => new[]
        {
            r.Def.RightSidewalk > 0.01 ? RoadGenerator.Band(r.Axis, -r.Def.TotalRight, -r.Def.RightWidth) : new List<Polygon2>(),
            r.Def.LeftSidewalk > 0.01 ? RoadGenerator.Band(r.Axis, r.Def.LeftWidth, r.Def.TotalLeft) : new List<Polygon2>(),
        }.SelectMany(b => b)).ToList();
        var sides = PolygonOps.Difference(PolygonOps.Intersect(PolygonOps.Union(sideBands), zone), pav);
        var curbRing = PolygonOps.Difference(PolygonOps.Offset(pav, cw, true), pav);
        L.Curb.AddRange(PolygonOps.Intersect(curbRing, sides));
        L.Sidewalk.AddRange(PolygonOps.Difference(sides, L.Curb));
        var medians = L.Roads.SelectMany(r => r.Def.Gaps.Where(g => g.Median).SelectMany(g => RoadGenerator.Band(r.Axis, g.Offset - g.Width / 2, g.Offset + g.Width / 2))).ToList();
        var medZ = PolygonOps.Difference(PolygonOps.Intersect(PolygonOps.Union(medians), zone), pav);
        if (medZ.Count > 0)
        {
            var core = PolygonOps.Offset(medZ, -cw);
            L.MedianCurb.AddRange(PolygonOps.Difference(medZ, core));
            L.MedianCore.AddRange(core);
        }

        // Recortes da sinalização pintada: miolo + aproximação até depois da linha de retenção.
        var stopFar = d.Crosswalks ? d.CrosswalkSetback + d.CrosswalkWidth + (d.StopLines ? 1.6 + 0.4 : 0) + 0.3 : 0.3;
        for (int i = 0; i < L.Roads.Count; i++)
        {
            var r = L.Roads[i];
            var paint = new List<Polygon2>(PolygonOps.Intersect(pav, zone));
            var parking = new List<Polygon2>(paint);
            foreach (var leg in L.Legs.Where(l => l.Road == i))
            {
                var s0 = leg.NodeStation;
                var s1 = Math.Clamp(leg.StationAt(leg.Clear + stopFar), 0, r.Axis.Length);
                var s1p = Math.Clamp(leg.StationAt(leg.Clear + stopFar + 5.0), 0, r.Axis.Length);
                var axisA = new Polyline2(r.Axis.SubPoints(Math.Min(s0, s1), Math.Max(s0, s1)));
                var axisP = new Polyline2(r.Axis.SubPoints(Math.Min(s0, s1p), Math.Max(s0, s1p)));
                if (axisA.Points.Count >= 2) paint.AddRange(RoadGenerator.Band(axisA, -r.Def.RightWidth - 0.3, r.Def.LeftWidth + 0.3));
                if (axisP.Points.Count >= 2) parking.AddRange(RoadGenerator.Band(axisP, -r.Def.RightWidth - 0.3, r.Def.LeftWidth + 0.3));
            }
            L.PaintCuts[i] = PolygonOps.Union(paint);
            L.ParkingCuts[i] = PolygonOps.Union(parking);
        }
        return L;
    }

    /// <summary>Recortes a aplicar numa marca do grupo da via <paramref name="road"/>.</summary>
    public static List<Polygon2> CutsFor(MarkingDefinition member, int road, IntersectionLayout L)
    {
        if (member is IAnnotationDefinition) return new();
        if (member is RoadPavementDefinition || IsPhysical(member)) return new() { L.Zone };
        if (member is ParkingMarkingDefinition) return L.ParkingCuts.GetValueOrDefault(road) ?? new();
        if (member is DeviceMarkingDefinition) return PolygonOps.Union(new[] { L.Zone }.Concat(L.PaintCuts.GetValueOrDefault(road) ?? new()));
        return L.PaintCuts.GetValueOrDefault(road) ?? new();
    }

    // ------------------------------------------------------------------ geometria da interseção

    public static MarkingGeometry Build(IntersectionDefinition d, BuildContext ctx)
    {
        var roads = ResolveRoads(d, ctx);
        var L = Layout(d, roads);
        var geo = new MarkingGeometry();
        geo.Warnings.AddRange(L.Warnings);
        if (L.Pavement.Count == 0) return geo;
        void Raised(IEnumerable<Polygon2> shapes, MarkingColor c, double h, double elev = 0)
        {
            foreach (var s in shapes)
            {
                var ss = s.Simplified();
                if (ss != null && ss.Area > 1e-4) geo.Pieces.Add(new MarkingPiece(ss, c) { Thickness = h, Elevation = elev });
            }
        }
        if (roads.Any(r => r.Def.Material != TipoPavimento.Nenhum))
            Raised(L.Pavement, L.PavementColor, L.PavementThickness, -L.PavementThickness);
        Raised(L.Curb, MarkingColor.Concreto, L.CurbHeight);
        Raised(L.Sidewalk, MarkingColor.Concreto, L.CurbHeight);
        Raised(L.MedianCurb, MarkingColor.Concreto, L.CurbHeight);
        Raised(L.MedianCore, MarkingColor.Grama, L.CurbHeight);
        geo.UnitCount = 1;
        geo.PathLength = L.Legs.Count;
        return geo;
    }

    public static List<IntersectionRoad> ResolveRoads(IntersectionDefinition d, BuildContext ctx)
    {
        var res = new List<IntersectionRoad>();
        foreach (var id in d.RoadIds)
        {
            if (ctx.Lookup?.Invoke(id) is not RoadPavementDefinition pv) continue;
            var axis = ctx.PathOf?.Invoke(pv);
            if (axis == null || axis.Points.Count < 2) continue;
            res.Add(new IntersectionRoad(pv, axis));
        }
        return res;
    }

    // ------------------------------------------------------------------ travessias e rampas

    /// <summary>Faixas de pedestres, linhas de retenção e rebaixamentos em cada ramo (marcas independentes).</summary>
    public static List<MarkingDefinition> Children(IntersectionDefinition d, IntersectionLayout L, OutputSettings output, double z)
    {
        var res = new List<MarkingDefinition>();
        if (!d.Crosswalks) return res;
        foreach (var leg in L.Legs)
        {
            var r = L.Roads[leg.Road];
            var c = leg.StationAt(leg.Clear + d.CrosswalkSetback + d.CrosswalkWidth / 2);
            if (c < 0.5 || c > r.Axis.Length - 0.5) continue;
            var p = r.Axis.PointAt(c);
            var n = r.Axis.TangentAt(c).PerpLeft * leg.Sign;      // n = esquerda de quem se afasta do nó
            var leftW = leg.Sign > 0 ? r.Def.LeftWidth : r.Def.RightWidth;
            var rightW = leg.Sign > 0 ? r.Def.RightWidth : r.Def.LeftWidth;
            var a = p + n * leftW;       // lado por onde chegam os veículos (mão dupla)
            var b = p - n * rightW;
            var setup = new CrosswalkSetup
            {
                CrosswalkWidth = d.CrosswalkWidth,
                StopLines = d.StopLines,
                StopLineLeftSide = true,
                StopLineRightSide = false,
                Span = r.Def.TwoWay ? StopLineSpan.MeiaPista : StopLineSpan.PistaInteira,
                EdgeSetback = 0.3,
            };
            // Mão única: só há retenção no ramo por onde o tráfego chega ao nó.
            var inbound = r.Def.TwoWay || leg.Sign < 0;
            setup.StopLines = d.StopLines && inbound;
            var defs = setup.Build(a, b, z, output, d.CrosswalkWidth, 0.40);
            var gaps = r.Def.Gaps.Where(g => g.Median).SelectMany(g => RoadGenerator.Band(r.Axis, g.Offset - g.Width / 2, g.Offset + g.Width / 2)).ToList();
            foreach (var def in defs)
            {
                def.GroupId = d.Id;
                foreach (var gp in PolygonOps.Intersect(gaps, new[] { Circle(p, d.CrosswalkWidth * 3) }))
                    def.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = gp.Outer.ToList() });
            }
            res.AddRange(defs);

            if (!d.Ramps) continue;
            foreach (var (side, w, hasWalk) in new[] { (n, leftW, (leg.Sign > 0 ? r.Def.LeftSidewalk : r.Def.RightSidewalk) > 0.5),
                                                        (-n, rightW, (leg.Sign > 0 ? r.Def.RightSidewalk : r.Def.LeftSidewalk) > 0.5) })
            {
                if (!hasWalk) continue;
                var curb = p + side * w;
                res.Add(new RampDefinition
                {
                    PathRef = PathReference.FromPoints(new[] { curb, curb + side }, z),
                    Width = Math.Min(1.50, d.CrosswalkWidth),
                    Height = r.Def.CurbHeight,
                    Output = output.Clone(),
                    GroupId = d.Id,
                });
            }
        }
        return res;
    }
}
