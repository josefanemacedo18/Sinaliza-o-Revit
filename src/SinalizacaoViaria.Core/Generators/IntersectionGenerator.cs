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

/// <summary>Tipo de tratamento aplicado num ramo.</summary>
public enum TipoRamo { Simples, Gota, BolsaoCanteiro, BolsaoAlargado }

/// <summary>
/// Tratamento de um ramo (sistema local: t = distância ao nó ao longo do ramo, o = lateral para a esquerda de quem se
/// afasta do nó; o tráfego que chega ao nó circula no lado +o).
/// </summary>
public sealed class LegFeature
{
    public required IntersectionLeg Leg { get; init; }
    public TipoRamo Kind { get; init; }
    /// <summary>Alargamento de cada lado da pista (m) em função de t.</summary>
    public Func<double, double> Delta { get; init; } = _ => 0;
    /// <summary>Fim do trecho modificado (a partir daí a via segue como desenhada).</summary>
    public double TEnd { get; init; }
    /// <summary>Faixa lateral recortada/reconstruída (o, em relação ao ramo).</summary>
    public double ExtLo { get; init; }
    public double ExtHi { get; init; }
    // Gota
    public double IslandStart { get; init; }
    public double IslandLength { get; init; }
    public double IslandWidth { get; init; }
    public bool Painted { get; init; }
    // Bolsão
    public double PocketStart { get; init; }
    public double StorageEnd { get; init; }
    public double TaperEnd { get; init; }
    public double PocketHi { get; init; }
    public double PocketWidth { get; init; }
}

/// <summary>Resultado geométrico de uma interseção.</summary>
public sealed class IntersectionLayout
{
    public Vec2 Node { get; init; }
    public double Radius { get; set; }
    public Polygon2 Zone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Área refeita pela interseção (zona do nó sem os ramos já fora das esquinas + trechos tratados).</summary>
    public List<Polygon2> Rebuild { get; } = new();
    public List<IntersectionRoad> Roads { get; } = new();
    /// <summary>Índice da via preferencial.</summary>
    public int Main { get; set; }
    public List<Polygon2> Pavement { get; } = new();
    public List<Polygon2> Curb { get; } = new();
    public List<Polygon2> Sidewalk { get; } = new();
    public List<Polygon2> MedianCurb { get; } = new();
    public List<Polygon2> MedianCore { get; } = new();
    /// <summary>Ilhas físicas (gota e ilhas triangulares das esquinas), já sem as passagens de pedestres.</summary>
    public List<Polygon2> Islands { get; } = new();
    /// <summary>Ilhas pintadas entre fluxos de mesmo sentido (ZPA branco).</summary>
    public List<Polygon2> PaintedIslands { get; } = new();
    /// <summary>Áreas pintadas entre fluxos opostos (ZPA-A amarelo).</summary>
    public List<Polygon2> PaintedMedians { get; } = new();
    /// <summary>Canteiros e ilhas elevados (a faixa de pedestres não é pintada sobre eles).</summary>
    public List<Polygon2> Obstacles { get; } = new();
    /// <summary>Canteiros, ilhas e áreas zebradas: limitam as linhas de retenção.</summary>
    public List<Polygon2> SpanStops { get; } = new();
    /// <summary>Pista final (sem canteiros e ilhas físicas), inclusive fora da zona – usada para posicionar retenções.</summary>
    public List<Polygon2> Carriageway { get; } = new();
    public List<IntersectionLeg> Legs { get; } = new();
    public List<LegFeature> Features { get; } = new();
    /// <summary>Por via: áreas onde a sinalização pintada é removida (miolo + aproximações até a retenção).</summary>
    public Dictionary<int, List<Polygon2>> PaintCuts { get; } = new();
    /// <summary>Por via: áreas onde as vagas são removidas (pintura + 5 m antes da esquina, CTB art. 181).</summary>
    public Dictionary<int, List<Polygon2>> ParkingCuts { get; } = new();
    /// <summary>Por via: áreas onde os elementos físicos (pavimento, meio-fio, calçada, canteiro) são refeitos pela interseção.</summary>
    public Dictionary<int, List<Polygon2>> PhysicalCuts { get; } = new();
    public List<string> Warnings { get; } = new();
    public MarkingColor PavementColor { get; set; } = MarkingColor.Asfalto;
    public double PavementThickness { get; set; } = 0.05;
    public double CurbHeight { get; set; } = 0.15;
    public double CurbWidth { get; set; } = 0.15;

    public LegFeature? FeatureOf(IntersectionLeg leg) => Features.FirstOrDefault(f => ReferenceEquals(f.Leg, leg));

    /// <summary>Ponto do ramo (t ao longo, o lateral).</summary>
    public Vec2 At(IntersectionLeg leg, double t, double o)
    {
        var axis = Roads[leg.Road].Axis;
        var s = Math.Clamp(leg.StationAt(t), 0, axis.Length);
        return axis.PointAt(s) + axis.TangentAt(s).PerpLeft * (leg.Sign * o);
    }

    /// <summary>Sentido do tráfego que chega ao nó pelo ramo, na distância t.</summary>
    public Vec2 Inbound(IntersectionLeg leg, double t)
    {
        var axis = Roads[leg.Road].Axis;
        var s = Math.Clamp(leg.StationAt(t), 0, axis.Length);
        return axis.TangentAt(s) * -leg.Sign;
    }

    /// <summary>Bordo da pista do lado de chegada (+o) e do lado de saída (−o), com o alargamento.</summary>
    public double HiEdge(IntersectionLeg leg, double t)
    {
        var r = Roads[leg.Road].Def;
        return (leg.Sign > 0 ? r.LeftWidth : r.RightWidth) + (FeatureOf(leg)?.Delta(t) ?? 0);
    }

    public double LoEdge(IntersectionLeg leg, double t)
    {
        var r = Roads[leg.Road].Def;
        return (leg.Sign > 0 ? r.RightWidth : r.LeftWidth) + (FeatureOf(leg)?.Delta(t) ?? 0);
    }

    /// <summary>Polígono do ramo entre t0 e t1 com limites laterais variáveis.</summary>
    public List<Polygon2> LegPoly(IntersectionLeg leg, double t0, double t1, Func<double, double> lo, Func<double, double> hi, double step = 0.5)
    {
        var axis = Roads[leg.Road].Axis;
        var max = leg.Sign > 0 ? axis.Length - leg.NodeStation : leg.NodeStation;
        t1 = Math.Min(t1, max);
        t0 = Math.Max(0, t0);
        if (t1 - t0 < 0.05) return new();
        var n = Math.Max(1, (int)Math.Ceiling((t1 - t0) / step));
        var top = new List<Vec2>();
        var bot = new List<Vec2>();
        for (int i = 0; i <= n; i++)
        {
            var t = t0 + (t1 - t0) * i / n;
            var a = lo(t);
            var b = hi(t);
            if (b < a) b = a;
            top.Add(At(leg, t, b));
            bot.Add(At(leg, t, a));
        }
        bot.Reverse();
        var ring = top.Concat(bot).ToList();
        return PolygonOps.Union(new[] { new Polygon2(ring) }).Where(p => p.Area > 1e-4).ToList();
    }

    /// <summary>Linha do ramo em o(t), de t0 a t1 (no sentido de quem se afasta do nó).</summary>
    public List<Vec2> LegLine(IntersectionLeg leg, double t0, double t1, Func<double, double> o, double step = 1.0)
    {
        var n = Math.Max(1, (int)Math.Ceiling((t1 - t0) / step));
        var pts = new List<Vec2>();
        for (int i = 0; i <= n; i++)
        {
            var t = t0 + (t1 - t0) * i / n;
            pts.Add(At(leg, t, o(t)));
        }
        return pts;
    }
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

    /// <summary>
    /// Falso quando o nó é só a emenda de duas vias alinhadas (continuação em linha reta): cada via termina no nó e
    /// não há interseção a tratar.
    /// </summary>
    public static bool NeedsIntersection(IReadOnlyList<IntersectionRoad> roads, Vec2 node)
    {
        var dirs = new List<Vec2>();
        foreach (var r in roads)
        {
            var axis = ExtendTo(r.Axis, node, Math.Max(r.Def.TotalLeft, r.Def.TotalRight) + 3);
            var (s, dist, _) = Project(axis, node);
            if (dist > Math.Max(r.Def.TotalLeft, r.Def.TotalRight) + 3) continue;
            if (axis.Length - s > 3) dirs.Add(axis.TangentAt(Math.Min(axis.Length, s + 1)));
            if (s > 3) dirs.Add(-axis.TangentAt(Math.Max(0, s - 1)));
        }
        if (dirs.Count >= 3) return true;
        return dirs.Count == 2 && dirs[0].Dot(dirs[1]) > -0.96;
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

    private static List<Polygon2> Close(IEnumerable<Polygon2> p, double r) =>
        r > 0.05 ? PolygonOps.Offset(PolygonOps.Offset(p, r, true), -r, true) : p.ToList();

    private static List<Polygon2> Open(IEnumerable<Polygon2> p, double r) =>
        r > 0.01 ? PolygonOps.Offset(PolygonOps.Offset(p, -r, true), r, true) : p.ToList();

    private static double AngleOf(Vec2 v) => Math.Atan2(v.Y, v.X) * 180 / Math.PI;

    private static double Ccw(double from, double to)
    {
        var a = (to - from) % 360;
        return a < 0 ? a + 360 : a;
    }

    /// <summary>Via preferencial: a indicada, ou a de maior hierarquia; depois a que atravessa o nó (dois ramos), mais larga e mais longa.</summary>
    public static int MainRoad(IntersectionDefinition d, IReadOnlyList<IntersectionRoad> roads, IReadOnlyList<IntersectionLeg> legs)
    {
        var k = d.MainRoadId == null ? -1 : roads.ToList().FindIndex(r => r.Def.Id == d.MainRoadId);
        if (k >= 0) return k;
        return Enumerable.Range(0, roads.Count)
            .OrderByDescending(i => Hierarquia.Rank(roads[i].Def.Hierarchy))
            .ThenByDescending(i => legs.Count(l => l.Road == i) >= 2)
            .ThenByDescending(i => Math.Round(roads[i].Def.RightWidth + roads[i].Def.LeftWidth, 1))
            .ThenByDescending(i => Math.Round(roads[i].Def.TotalLeft + roads[i].Def.TotalRight, 1))
            .ThenByDescending(i => roads[i].Axis.Length)
            .First();
    }

    /// <summary>Ramos a partir do nó e distância livre (fim da curva da esquina) em cada um.</summary>
    private static List<IntersectionLeg> ComputeLegs(IntersectionLayout L, List<Polygon2> pav, IReadOnlyList<List<Polygon2>> own,
        double reach, bool warn)
    {
        var legs = new List<IntersectionLeg>();
        var cw = L.CurbWidth;
        foreach (var (r, i) in L.Roads.Select((r, i) => (r, i)))
        {
            var others = PolygonOps.Difference(pav, PolygonOps.Offset(own[i], 0.02));
            var (sn, _, _) = Project(r.Axis, L.Node);
            foreach (var sign in new[] { -1, 1 })
            {
                var avail = sign > 0 ? r.Axis.Length - sn : sn;
                if (avail < 3) continue;
                double clear = -1;
                for (var t = 0.0; t <= Math.Min(avail, reach); t += 0.25)
                {
                    var s = sn + sign * t;
                    var p = r.Axis.PointAt(s);
                    var nrm = r.Axis.TangentAt(s).PerpLeft;
                    var a = p - nrm * (r.Def.RightWidth + cw + 3);
                    var b = p + nrm * (r.Def.LeftWidth + cw + 3);
                    var hit = others.Any(o =>
                    {
                        var (mn, mx) = o.Bounds;
                        if (Math.Max(a.X, b.X) < mn.X || Math.Min(a.X, b.X) > mx.X || Math.Max(a.Y, b.Y) < mn.Y || Math.Min(a.Y, b.Y) > mx.Y) return false;
                        return DetailGenerator.SegmentIntervals(o, a, b).Sum(iv => iv.T1 - iv.T0) * a.DistanceTo(b) > 0.02;
                    });
                    if (!hit) { clear = t; break; }
                }
                if (clear < 0)
                {
                    if (warn) L.Warnings.Add("Um dos ramos é curto demais para a esquina – confira o raio.");
                    clear = Math.Min(avail, reach);
                }
                if (avail < clear + 1) continue;
                legs.Add(new IntersectionLeg(i, sign, sn, clear, r.Axis.TangentAt(sn + sign * Math.Min(clear, avail)) * sign));
            }
        }
        return legs;
    }

    private static double MaxT(IntersectionLayout L, IntersectionLeg leg)
    {
        var axis = L.Roads[leg.Road].Axis;
        return leg.Sign > 0 ? axis.Length - leg.NodeStation : leg.NodeStation;
    }

    /// <summary>Distância livre das travessias/retenção a partir do fim da esquina.</summary>
    private static double StopFar(IntersectionDefinition d) =>
        d.Crosswalks ? d.CrosswalkSetback + d.CrosswalkWidth + (d.StopLines ? 1.6 + 0.4 : 0) + 0.3 : (d.StopLines ? 1.5 : 0.3);

    /// <summary>Tratamentos dos ramos (gota nas secundárias, bolsão de conversão à esquerda na principal).</summary>
    private static List<LegFeature> Features(IntersectionDefinition d, IntersectionLayout L, IReadOnlyList<IntersectionLeg> legs,
        IReadOnlyList<List<Polygon2>> carriages)
    {
        var res = new List<LegFeature>();
        foreach (var leg in legs)
        {
            var r = L.Roads[leg.Road].Def;
            var max = MaxT(L, leg);
            var hiW = leg.Sign > 0 ? r.LeftWidth : r.RightWidth;
            var loW = leg.Sign > 0 ? r.RightWidth : r.LeftWidth;
            var c = leg.Clear;
            if (leg.Road != L.Main && d.SplitterIslands != TipoIlha.Nenhuma)
            {
                if (!r.TwoWay) continue;
                // Ramo quase paralelo a outro (< 25°): não cabe ilha separadora.
                if (legs.Any(o => !ReferenceEquals(o, leg) && o.Dir.Dot(leg.Dir) > Math.Cos(25 * Math.PI / 180))) continue;
                // Início da ilha: onde o eixo sai da pista das outras vias.
                var tEdge = 0.0;
                var others = carriages.Where((_, j) => j != leg.Road).SelectMany(x => x).ToList();
                while (tEdge < c && others.Any(o => o.Contains(L.At(leg, tEdge, 0)))) tEdge += 0.25;
                var w = Math.Clamp(d.SplitterWidth, 1.0, 8.0);
                var t0 = tEdge + 1.0;
                var len = Math.Max(Math.Max(6, d.SplitterLength), d.Crosswalks ? c + d.CrosswalkSetback + d.CrosswalkWidth + 3.5 - t0 : 0);
                var delta = w / 2;
                var taper = Math.Max(10, 12 * delta);
                var tEnd = t0 + len + taper;
                if (tEnd > max - 2)
                {
                    L.Warnings.Add("Ramo secundário curto para a ilha separadora (gota) – encurte a ilha ou prolongue a via.");
                    continue;
                }
                var tl = t0 + len;
                res.Add(new LegFeature
                {
                    Leg = leg, Kind = TipoRamo.Gota, TEnd = tEnd,
                    Delta = t => t <= tl ? delta : t >= tEnd ? 0 : delta * (1 - (t - tl) / taper),
                    ExtLo = -(leg.Sign > 0 ? r.TotalRight : r.TotalLeft) - delta - 0.3,
                    ExtHi = (leg.Sign > 0 ? r.TotalLeft : r.TotalRight) + delta + 0.3,
                    IslandStart = t0, IslandLength = len, IslandWidth = w, Painted = d.SplitterIslands == TipoIlha.Pintada,
                });
            }
            else if (leg.Road == L.Main && d.LeftTurnPockets)
            {
                if (!r.TwoWay) continue;
                var P = Math.Clamp(d.PocketWidth, 2.5, 5.0);
                var S = Math.Max(5, d.PocketLength);
                var T = Math.Max(5, d.PocketTaper);
                var med = r.Gaps.Where(g => g.Median && Math.Abs(g.Offset) <= g.Width / 2 + 0.5).OrderByDescending(g => g.Width).FirstOrDefault();
                if (med != null)
                {
                    var lo = leg.Sign * med.Offset - med.Width / 2;
                    var hi = leg.Sign * med.Offset + med.Width / 2;
                    // O bolsão ocupa o canteiro deixando um separador de pelo menos 0,30 m.
                    P = Math.Min(P, med.Width - 0.3);
                    if (P < 2.7)
                    {
                        L.Warnings.Add($"Canteiro central com {med.Width:0.00} m: estreito para o bolsão de conversão à esquerda (mínimo 3,00 m).");
                        continue;
                    }
                    var tEnd = c + S + T;
                    if (tEnd > max - 2) { L.Warnings.Add("Via principal curta para o bolsão de conversão à esquerda."); continue; }
                    res.Add(new LegFeature
                    {
                        Leg = leg, Kind = TipoRamo.BolsaoCanteiro, TEnd = tEnd, ExtLo = lo - 0.05, ExtHi = hi + 0.05,
                        PocketStart = c, StorageEnd = c + S, TaperEnd = tEnd, PocketHi = hi, PocketWidth = P,
                    });
                }
                else
                {
                    var delta = P / 2;
                    var tw = Math.Max(15, 10 * P);
                    var tT = c + S + T;
                    var tEnd = tT + tw;
                    if (tEnd > max - 2) { L.Warnings.Add("Via principal curta para o bolsão de conversão à esquerda (alargamento)."); continue; }
                    res.Add(new LegFeature
                    {
                        Leg = leg, Kind = TipoRamo.BolsaoAlargado, TEnd = tEnd,
                        Delta = t => t <= tT ? delta : t >= tEnd ? 0 : delta * (1 - (t - tT) / tw),
                        ExtLo = -(leg.Sign > 0 ? r.TotalRight : r.TotalLeft) - delta - 0.3,
                        ExtHi = (leg.Sign > 0 ? r.TotalLeft : r.TotalRight) + delta + 0.3,
                        PocketStart = c, StorageEnd = c + S, TaperEnd = tT, PocketHi = delta, PocketWidth = P,
                    });
                }
            }
        }
        return res;
    }

    /// <summary>Contorno da ilha gota (cabeça arredondada junto à via principal, cauda afunilada).</summary>
    private static List<Polygon2> GotaShape(IntersectionLayout L, LegFeature f)
    {
        var t0 = f.IslandStart;
        var hw = f.IslandWidth / 2;
        var tl = t0 + f.IslandLength;
        double Half(double t)
        {
            if (t < t0 + hw) { var k = t0 + hw - t; return Math.Sqrt(Math.Max(0, hw * hw - k * k)); }
            var tb = t0 + Math.Max(hw, 0.55 * f.IslandLength);
            if (t <= tb) return hw;
            return hw + (0.25 - hw) * (t - tb) / Math.Max(0.1, tl - tb);
        }
        var poly = L.LegPoly(f.Leg, t0, tl, t => -Half(t), Half, 0.25);
        return Open(poly, 0.15);
    }

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
        var channels = d.RightTurnIslands != TipoIlha.Nenhuma;
        var rs = channels ? Math.Max(d.RightTurnRadius, rc + 3) : rc;
        var maxHalf = input.Max(r => Math.Max(r.Def.TotalLeft, r.Def.TotalRight));
        foreach (var r in input) L.Roads.Add(r with { Axis = ExtendTo(r.Axis, node, maxHalf + 3) });
        L.PavementColor = L.Roads[0].Def.Color;
        L.PavementThickness = L.Roads[0].Def.ActualThickness;
        L.CurbHeight = L.Roads[0].Def.CurbHeight;
        var cw = L.Roads.Max(r => r.Def.CurbWidth);
        L.CurbWidth = cw;

        // Ângulo mínimo entre as vias (define o alcance das esquinas).
        var dirs = L.Roads.Select(r => r.Axis.TangentAt(Project(r.Axis, node).Station)).ToList();
        var minSin = 1.0;
        for (int i = 0; i < dirs.Count; i++)
            for (int j = i + 1; j < dirs.Count; j++)
                minSin = Math.Min(minSin, Math.Abs(dirs[i].Cross(dirs[j])));
        if (minSin < 0.5 && !channels)
            L.Warnings.Add("Vias muito oblíquas (< 30°): considere canalizar as esquinas (Tipo III) ou realinhar o ramo secundário (T entre 75° e 105°).");
        var reachFeat = (d.SplitterIslands != TipoIlha.Nenhuma ? Math.Max(d.SplitterLength, 12) + 25 : 0);
        reachFeat = Math.Max(reachFeat, d.LeftTurnPockets ? d.PocketLength + d.PocketTaper + Math.Max(15, 10 * d.PocketWidth) : 0);
        var baseReach = maxHalf / Math.Max(0.25, minSin) * 2 + 2 * rs + 12;
        var r1 = Math.Min(260, baseReach + reachFeat + (reachFeat > 0 ? 10 : 0));
        var big = Circle(node, r1);
        var clip = new[] { Circle(node, r1 - rs - 1) };
        var reach = Math.Min(r1 - rs - 2, baseReach);

        // Pista de cada via (inteira) e seus canteiros centrais.
        var cn = L.Roads.Select(r => PolygonOps.Intersect(RoadGenerator.Band(r.Axis, -r.Def.RightWidth, r.Def.LeftWidth), new[] { big })).ToList();
        var medBands = L.Roads.Select(r => PolygonOps.Intersect(PolygonOps.Union(r.Def.Gaps.Where(g => g.Median)
            .SelectMany(g => RoadGenerator.Band(r.Axis, g.Offset - g.Width / 2, g.Offset + g.Width / 2))), new[] { big })).ToList();

        // 1ª passada: esquinas simples → ramos, via principal e tratamentos dos ramos.
        var pav0 = PolygonOps.Intersect(Close(PolygonOps.Union(cn.SelectMany(x => x)), rc), clip);
        var legs0 = ComputeLegs(L, pav0, cn, reach, false);
        L.Main = MainRoad(d, L.Roads, legs0);
        L.Features.AddRange(Features(d, L, legs0, cn));

        // Alargamentos (gota, bolsão sem canteiro) e bolsões no canteiro.
        var widen = L.Roads.Select(_ => new List<Polygon2>()).ToList();
        var pockets = L.Roads.Select(_ => new List<Polygon2>()).ToList();
        foreach (var f in L.Features)
        {
            var i = f.Leg.Road;
            var r = L.Roads[i].Def;
            var hiW = f.Leg.Sign > 0 ? r.LeftWidth : r.RightWidth;
            var loW = f.Leg.Sign > 0 ? r.RightWidth : r.LeftWidth;
            if (f.Kind is TipoRamo.Gota or TipoRamo.BolsaoAlargado)
            {
                widen[i].AddRange(L.LegPoly(f.Leg, 0, f.TEnd, _ => hiW - 0.01, t => hiW + f.Delta(t)));
                widen[i].AddRange(L.LegPoly(f.Leg, 0, f.TEnd, t => -loW - f.Delta(t), _ => -loW + 0.01));
            }
            if (f.Kind == TipoRamo.BolsaoCanteiro)
            {
                var hi = f.PocketHi;
                var lo = hi - f.PocketWidth;
                pockets[i].AddRange(L.LegPoly(f.Leg, 0, f.TaperEnd,
                    t => t <= f.StorageEnd ? lo : lo + (hi - lo) * (t - f.StorageEnd) / Math.Max(0.1, f.TaperEnd - f.StorageEnd), _ => hi + 0.05));
            }
        }
        var own = L.Roads.Select((_, i) => PolygonOps.Union(cn[i].Concat(widen[i]))).ToList();

        // 2ª passada: pista final com esquinas (raio simples ou faixa de conversão livre).
        var U = PolygonOps.Union(own.SelectMany(x => x));
        var pavS = PolygonOps.Intersect(Close(U, rc), clip);
        var pav = pavS;
        var islands = new List<Polygon2>();
        var paintedIslands = new List<Polygon2>();
        if (channels)
        {
            // Raio por esquina: a tangente da curva (R·cot(θ/2)) não passa do raio pedido – esquinas agudas ficam com
            // raio menor, sem estender a faixa de conversão por dezenas de metros ao longo da via.
            var sorted = legs0.Select(l => (Leg: l, A: AngleOf(l.Dir))).OrderBy(x => x.A).ToList();
            var corners = new List<(double From, double Span, double R)>();
            for (int k = 0; k < sorted.Count && sorted.Count > 1; k++)
            {
                var a = sorted[k].A;
                var span = Ccw(a, sorted[(k + 1) % sorted.Count].A);
                var ok = d.RightTurnCorners switch
                {
                    EsquinasCanalizadas.Agudas => span < 75,
                    EsquinasCanalizadas.Obtusas => span > 105,
                    _ => true,
                };
                if (!ok || span >= 170) continue;
                var R = Math.Min(rs, rs * Math.Tan(span * Math.PI / 360));
                if (R > rc + 1) corners.Add((a, span, Math.Round(R, 1)));
            }
            var keep = new List<Polygon2>();
            foreach (var grp in corners.GroupBy(c => c.R))
            {
                var pavB = PolygonOps.Intersect(Close(U, grp.Key), clip);
                foreach (var lobe in PolygonOps.Difference(pavB, pavS).Where(p => p.Area > 1.0))
                {
                    var v = lobe.Centroid - node;
                    if (v.Length > reach) continue;
                    var phi = AngleOf(v);
                    if (grp.Any(c => Ccw(c.From, phi) <= c.Span)) keep.Add(lobe);
                }
            }
            if (keep.Count > 0)
            {
                pav = PolygonOps.Union(pavS.Concat(keep));
                var lane = Math.Max(3.5, d.RightTurnLaneWidth);
                var core = PolygonOps.Difference(PolygonOps.Intersect(PolygonOps.Offset(pav, -lane), PolygonOps.Offset(keep, 0.05)),
                    PolygonOps.Offset(U, 0.6));
                var isl = Open(core, 0.6).Where(p => p.Area >= (d.RightTurnIslands == TipoIlha.Fisica ? 5.0 : 8.0)).ToList();
                if (isl.Count == 0) L.Warnings.Add("Esquinas sem espaço para a ilha de canalização: aumente o raio da faixa de conversão ou reduza a largura da faixa.");
                if (d.RightTurnIslands == TipoIlha.Fisica) islands.AddRange(isl);
                else paintedIslands.AddRange(isl);
            }
        }

        // Canteiros centrais: terminam antes da pista das outras vias (nariz arredondado); bolsões recortados.
        var medT = L.Roads.Select((_, i) =>
        {
            if (medBands[i].Count == 0) return new List<Polygon2>();
            var others = PolygonOps.Offset(own.Where((_, j) => j != i).SelectMany(x => x), 1.0);
            return Open(PolygonOps.Difference(PolygonOps.Difference(medBands[i], others), pockets[i]), 0.3).Where(p => p.Area > 0.5).ToList();
        }).ToList();

        // Ilhas gota.
        var gotas = new List<Polygon2>();
        var paintedMedians = new List<Polygon2>();
        foreach (var f in L.Features.Where(f => f.Kind == TipoRamo.Gota))
        {
            var g = GotaShape(L, f);
            if (f.Painted) paintedMedians.AddRange(L.LegPoly(f.Leg, f.IslandStart, f.TEnd, t => -f.Delta(t) + 0.05, t => f.Delta(t) - 0.05));
            else
            {
                gotas.AddRange(g);
                // Zebrado amarelo à frente da cauda da ilha.
                var tl = f.IslandStart + f.IslandLength;
                paintedMedians.AddRange(PolygonOps.Difference(
                    L.LegPoly(f.Leg, tl - 3, f.TEnd, t => -f.Delta(t) + 0.05, t => f.Delta(t) - 0.05), PolygonOps.Offset(g, 0.3)));
            }
        }
        foreach (var f in L.Features.Where(f => f.Kind == TipoRamo.BolsaoAlargado))
        {
            var sE = f.StorageEnd;
            var tT = f.TaperEnd;
            paintedMedians.AddRange(L.LegPoly(f.Leg, sE, f.TEnd, t => -f.Delta(t) + 0.05,
                t => t < tT ? -f.Delta(t) + 0.05 + (2 * f.Delta(t) - 0.1) * (t - sE) / Math.Max(0.1, tT - sE) : f.Delta(t) - 0.05));
        }

        // 3ª: ramos definitivos (a travessia fica depois do fim da curva, inclusive das faixas de conversão).
        var legs = ComputeLegs(L, pav, own, reach, true);
        foreach (var leg in legs)
        {
            var f = L.Features.FirstOrDefault(x => x.Leg.Road == leg.Road && x.Leg.Sign == leg.Sign);
            if (f == null) continue;
            L.Features.Remove(f);
            L.Features.Add(new LegFeature
            {
                Leg = leg, Kind = f.Kind, Delta = f.Delta, TEnd = f.TEnd, ExtLo = f.ExtLo, ExtHi = f.ExtHi,
                IslandStart = f.IslandStart, IslandLength = f.IslandLength, IslandWidth = f.IslandWidth, Painted = f.Painted,
                PocketStart = f.PocketStart, StorageEnd = f.StorageEnd, TaperEnd = f.TaperEnd, PocketHi = f.PocketHi, PocketWidth = f.PocketWidth,
            });
        }
        L.Features.RemoveAll(f => !legs.Contains(f.Leg));
        L.Legs.AddRange(legs);
        var rn = Math.Max(L.Legs.Count > 0 ? L.Legs.Max(l => l.Clear) + 0.3 : maxHalf + rc, maxHalf + 1);
        L.Radius = rn;
        L.Zone = Circle(node, rn);
        var zone = new[] { L.Zone };

        // Travessias cortam canteiros e ilhas (refúgio no nível da pista, NBR 9050).
        var obstacles = PolygonOps.Union(medT.SelectMany(x => x).Concat(gotas).Concat(islands));
        L.Obstacles.AddRange(obstacles);
        if (d.Crosswalks)
        {
            var bands = new List<Polygon2>();
            foreach (var leg in L.Legs)
            {
                var tc = leg.Clear + d.CrosswalkSetback + d.CrosswalkWidth / 2;
                var hw = d.CrosswalkWidth / 2 - 0.1;
                bands.AddRange(L.LegPoly(leg, tc - hw, tc + hw, t => -L.LoEdge(leg, t), t => L.HiEdge(leg, t)));
            }
            if (bands.Count > 0)
            {
                for (int i = 0; i < medT.Count; i++) medT[i] = PolygonOps.Difference(medT[i], bands).Where(p => p.Area > 0.3).ToList();
                gotas = PolygonOps.Difference(gotas, bands).Where(p => p.Area > 0.3).ToList();
                paintedMedians = PolygonOps.Difference(paintedMedians, bands);
                paintedIslands = PolygonOps.Difference(paintedIslands, bands);
            }
        }
        islands.AddRange(gotas);

        // Áreas refeitas pela interseção: zona do nó (sem os trechos dos ramos já fora das esquinas) + trechos tratados.
        var ext = L.Roads.Select(_ => new List<Polygon2>()).ToList();
        foreach (var f in L.Features)
            ext[f.Leg.Road].AddRange(L.LegPoly(f.Leg, 0, f.TEnd + 0.5, _ => f.ExtLo, _ => f.ExtHi, 1.0));
        var tails = new List<Polygon2>();
        foreach (var leg in L.Legs)
        {
            var r = L.Roads[leg.Road].Def;
            var f = L.FeatureOf(leg);
            var t0 = Math.Max(leg.Clear + 0.5, f != null ? f.TEnd + 0.5 : 0);
            var hiT = (leg.Sign > 0 ? r.TotalLeft : r.TotalRight) + 0.2;
            var loT = (leg.Sign > 0 ? r.TotalRight : r.TotalLeft) + 0.2;
            if (rn + 5 > t0) tails.AddRange(L.LegPoly(leg, t0, rn + 5, _ => -loT, _ => hiT, 1.0));
        }
        tails = PolygonOps.Union(tails);
        var core0 = PolygonOps.Difference(zone, tails);
        for (int i = 0; i < L.Roads.Count; i++) L.PhysicalCuts[i] = PolygonOps.Union(core0.Concat(ext[i]));
        var rebuild = PolygonOps.Union(core0.Concat(ext.SelectMany(x => x)));
        L.Rebuild.AddRange(rebuild);

        var solid = PolygonOps.Union(medT.SelectMany(x => x).Concat(islands));
        var carriage = PolygonOps.Difference(pav, solid);
        L.Carriageway.AddRange(carriage);
        L.Pavement.AddRange(PolygonOps.Intersect(carriage, rebuild));

        // Calçadas e meio-fio (zona do nó: todas as vias; trechos tratados: só a própria via).
        List<Polygon2> SideBands(IntersectionRoad r) => new[]
        {
            r.Def.RightSidewalk > 0.01 ? RoadGenerator.Band(r.Axis, -r.Def.TotalRight, -r.Def.RightWidth) : new List<Polygon2>(),
            r.Def.LeftSidewalk > 0.01 ? RoadGenerator.Band(r.Axis, r.Def.LeftWidth, r.Def.TotalLeft) : new List<Polygon2>(),
        }.SelectMany(b => b).ToList();
        var sideParts = PolygonOps.Intersect(L.Roads.SelectMany(SideBands), core0).ToList();
        for (int i = 0; i < L.Roads.Count; i++)
            if (ext[i].Count > 0) sideParts.AddRange(PolygonOps.Intersect(SideBands(L.Roads[i]), ext[i]));
        var walks = L.Roads.Select(r => new[] { r.Def.RightSidewalk, r.Def.LeftSidewalk }).SelectMany(x => x).Where(w => w > 0.5).ToList();
        if (walks.Count > 0 && channels)
        {
            // Calçada acompanhando a curva das faixas de conversão.
            var sw = walks.Min();
            var lobes = PolygonOps.Difference(pav, pavS);
            if (lobes.Count > 0)
                sideParts.AddRange(PolygonOps.Intersect(PolygonOps.Intersect(PolygonOps.Offset(pav, cw + sw, true), PolygonOps.Offset(lobes, cw + sw + 1, true)), core0));
        }
        var sides = PolygonOps.Difference(PolygonOps.Union(sideParts), pav);
        var curbRing = PolygonOps.Difference(PolygonOps.Offset(pav, cw, true), pav);
        L.Curb.AddRange(PolygonOps.Intersect(curbRing, sides));
        L.Sidewalk.AddRange(PolygonOps.Difference(sides, L.Curb).Where(p => p.Area > 0.05));

        var medParts = PolygonOps.Intersect(medT.SelectMany(x => x), core0).ToList();
        for (int i = 0; i < L.Roads.Count; i++)
            if (ext[i].Count > 0) medParts.AddRange(PolygonOps.Intersect(medT[i], ext[i]));
        var medZ = PolygonOps.Union(medParts);
        if (medZ.Count > 0)
        {
            var core = PolygonOps.Offset(medZ, -cw);
            L.MedianCurb.AddRange(PolygonOps.Difference(medZ, core));
            L.MedianCore.AddRange(core);
        }
        L.Islands.AddRange(islands.Where(p => p.Area > 0.3));
        L.PaintedIslands.AddRange(paintedIslands);
        L.PaintedMedians.AddRange(paintedMedians.Where(p => p.Area > 0.3));
        L.SpanStops.AddRange(L.Obstacles.Concat(L.PaintedMedians).Concat(L.PaintedIslands));

        // Recortes da sinalização pintada: miolo + aproximação até depois da retenção + trechos tratados.
        var stopFar = L.Legs.Count <= 2 ? 0.3 : StopFar(d);
        for (int i = 0; i < L.Roads.Count; i++)
        {
            var r = L.Roads[i];
            var paint = new List<Polygon2>(PolygonOps.Intersect(pav, core0));
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
            foreach (var f in L.Features.Where(f => f.Leg.Road == i))
            {
                var band = f.Kind == TipoRamo.BolsaoCanteiro
                    ? L.LegPoly(f.Leg, 0, f.TEnd, _ => f.PocketHi - f.PocketWidth - 0.35, _ => f.PocketHi + 0.35, 1.0)
                    : L.LegPoly(f.Leg, 0, f.TEnd, t => -L.LoEdge(f.Leg, t) - 0.3, t => L.HiEdge(f.Leg, t) + 0.3, 1.0);
                paint.AddRange(band);
                parking.AddRange(band);
            }
            L.PaintCuts[i] = PolygonOps.Union(paint);
            L.ParkingCuts[i] = PolygonOps.Union(parking);
        }
        return L;
    }

    /// <summary>Recortes a aplicar numa marca do grupo da via <paramref name="road"/>.</summary>
    public static List<Polygon2> CutsFor(MarkingDefinition member, int road, IntersectionLayout L)
    {
        var phys = L.PhysicalCuts.GetValueOrDefault(road) ?? new List<Polygon2> { L.Zone };
        if (member is IAnnotationDefinition) return new();
        if (member is RoadPavementDefinition || IsPhysical(member)) return phys;
        if (member is ParkingMarkingDefinition) return L.ParkingCuts.GetValueOrDefault(road) ?? new();
        if (member is DeviceMarkingDefinition) return PolygonOps.Union(phys.Concat(L.PaintCuts.GetValueOrDefault(road) ?? new()));
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
        // Rebaixamentos das travessias recortam as calçadas refeitas pela interseção.
        if (d.Crosswalks && d.Ramps)
        {
            var ramps = Children(d, L, new OutputSettings(), 0).OfType<RampDefinition>()
                .Select(r => RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points))).ToList();
            if (ramps.Count > 0)
            {
                var sw = PolygonOps.Difference(L.Sidewalk, ramps);
                var cb = PolygonOps.Difference(L.Curb, ramps);
                L.Sidewalk.Clear(); L.Sidewalk.AddRange(sw);
                L.Curb.Clear(); L.Curb.AddRange(cb);
            }
        }
        if (roads.Any(r => r.Def.Material != TipoPavimento.Nenhum))
            Raised(L.Pavement, L.PavementColor, L.PavementThickness, -L.PavementThickness);
        Raised(L.Curb, MarkingColor.Concreto, L.CurbHeight);
        Raised(L.Sidewalk, MarkingColor.Concreto, L.CurbHeight);
        Raised(L.MedianCurb, MarkingColor.Concreto, L.CurbHeight);
        Raised(L.MedianCore, MarkingColor.Grama, L.CurbHeight);
        foreach (var isl in L.Islands)
        {
            var core = PolygonOps.Offset(new[] { isl }, -L.CurbWidth);
            Raised(PolygonOps.Difference(new[] { isl }, core), MarkingColor.Concreto, L.CurbHeight);
            // Ilhas pequenas: concreto; maiores: ajardinadas.
            Raised(core, isl.Area > 25 ? MarkingColor.Grama : MarkingColor.Concreto, L.CurbHeight);
        }
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

    // ------------------------------------------------------------------ sinalização da interseção

    private static readonly string[] NotShifted = { "FTP-1", "FTP-2", "LRE", "LDP", "MCC", "LRV" };

    /// <summary>Trecho da pista do lado de chegada na distância t: do bordo até o eixo, canteiro ou ilha.</summary>
    private static (double Lo, double Hi) ApproachSpan(IntersectionLayout L, IntersectionLeg leg, double t)
    {
        var r = L.Roads[leg.Road].Def;
        var f = L.FeatureOf(leg);
        var hi = L.HiEdge(leg, t) - 0.3;
        var floor = -L.LoEdge(leg, t) + 0.3;
        if (r.TwoWay) floor = f?.Kind == TipoRamo.BolsaoAlargado ? -f.Delta(t) : 0;
        var a = L.At(leg, t, hi);
        var b = L.At(leg, t, floor);
        var len = a.DistanceTo(b);
        if (len < 0.1) return (floor, hi);
        // A partir do bordo, até o primeiro obstáculo (canteiro, ilha).
        var reach = 1.0;
        foreach (var o in L.SpanStops)
            foreach (var iv in DetailGenerator.SegmentIntervals(o, a, b))
                if (iv.T0 < reach) reach = Math.Max(0, iv.T0);
        var lo = hi - (hi - floor) * reach;
        if (reach < 1.0) lo += 0.1;
        return (Math.Min(lo, hi), hi);
    }

    /// <summary>
    /// Sinalização criada pela interseção (marcas independentes): travessias, retenções ou "dê a preferência" conforme o
    /// controle, legendas e placas, ilhas pintadas, bolsões e as linhas das vias deslocadas nos alargamentos.
    /// </summary>
    public static List<MarkingDefinition> Children(IntersectionDefinition d, IntersectionLayout L, OutputSettings output, double z,
        IReadOnlyList<IReadOnlyCollection<MarkingDefinition>>? roadMembers = null)
    {
        var res = new List<MarkingDefinition>();
        HierarquiaViaria? hierarchy = L.Roads.Count > 0 ? L.Roads[L.Main].Def.Hierarchy : null;
        T Add<T>(T def) where T : MarkingDefinition
        {
            def.Output = output.Clone();
            def.GroupId = d.Id;
            def.Hierarchy = hierarchy;
            res.Add(def);
            return def;
        }
        var stopFar = StopFar(d);
        // Continuação de uma via na outra (dois ramos): só a geometria, sem travessias nem controle.
        if (L.Legs.Count <= 2) return res;
        foreach (var leg in L.Legs)
        {
            hierarchy = L.Roads[leg.Road].Def.Hierarchy;
            var r = L.Roads[leg.Road];
            var f = L.FeatureOf(leg);
            var isMain = leg.Road == L.Main;
            var inbound = r.Def.TwoWay || leg.Sign < 0;   // mão única: só o ramo por onde o tráfego chega ao nó
            var max = MaxT(L, leg);
            var tc = leg.Clear + d.CrosswalkSetback + d.CrosswalkWidth / 2;
            var tStop = d.Crosswalks ? leg.Clear + d.CrosswalkSetback + d.CrosswalkWidth + 1.6 + 0.2 : leg.Clear + 1.0;
            if (tStop > max - 1) continue;

            // Travessia de pedestres e rebaixamentos.
            if (d.Crosswalks)
            {
                var a = L.At(leg, tc, L.HiEdge(leg, tc));
                var b = L.At(leg, tc, -L.LoEdge(leg, tc));
                var cwDefs = new CrosswalkSetup { CrosswalkWidth = d.CrosswalkWidth, StopLines = false, EdgeSetback = 0.3 }
                    .Build(a, b, z, output, d.CrosswalkWidth, 0.40);
                var near = new[] { Circle(L.At(leg, tc, 0), d.CrosswalkWidth * 3 + L.HiEdge(leg, tc) + L.LoEdge(leg, tc)) };
                foreach (var def in cwDefs)
                {
                    foreach (var gp in PolygonOps.Intersect(L.Obstacles, near))
                        def.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = gp.Outer.ToList() });
                    Add(def);
                }
                if (d.Ramps)
                    foreach (var (sgn, w, walk) in new[] { (1.0, L.HiEdge(leg, tc), leg.Sign > 0 ? r.Def.LeftSidewalk : r.Def.RightSidewalk),
                                                           (-1.0, L.LoEdge(leg, tc), leg.Sign > 0 ? r.Def.RightSidewalk : r.Def.LeftSidewalk) })
                    {
                        if (walk <= 0.5) continue;
                        var curb = L.At(leg, tc, sgn * w);
                        var side = (L.At(leg, tc, sgn * (w + 1)) - curb).Normalized();
                        Add(new RampDefinition
                        {
                            PathRef = PathReference.FromPoints(new[] { curb, curb + side }, z),
                            Width = Math.Min(1.50, d.CrosswalkWidth),
                            Height = r.Def.CurbHeight,
                        });
                    }
            }

            // Controle do direito de passagem.
            if (inbound)
            {
                var (lo, hi) = ApproachSpan(L, leg, tStop);
                var mid = (lo + hi) / 2;
                var dir = L.Inbound(leg, tStop);
                var control = d.Control;
                var secondary = !isMain && control is ControleIntersecao.Pare or ControleIntersecao.DePreferencia;
                var signal = control == ControleIntersecao.Semaforo;
                var walkHi = (leg.Sign > 0 ? r.Def.LeftSidewalk : r.Def.RightSidewalk) > 0.5;
                var signAt = L.At(leg, tStop + 0.3, L.HiEdge(leg, tStop) + (walkHi ? L.CurbWidth + 0.45 : 1.0));
                if (secondary && control == ControleIntersecao.Pare || signal)
                {
                    if (d.StopLines && hi - lo > 0.5)
                        Add(new LinearMarkingDefinition { Code = "LRE", Variant = "0,40 m", PathRef = PathReference.FromPoints(new[] { L.At(leg, tStop, hi), L.At(leg, tStop, lo) }, z) });
                    if (secondary && d.Signs)
                    {
                        if (hi - lo >= 2.2 && tStop + 3.6 < max)
                            Add(new TextMarkingDefinition { Text = "PARE", Height = 1.6, Position = L.At(leg, tStop + 3.4, mid), Direction = dir, Z = z });
                        Add(new SignDefinition { Code = "R-1", Position = signAt, Direction = dir, Z = z });
                    }
                }
                else if (secondary && control == ControleIntersecao.DePreferencia)
                {
                    if (d.StopLines && hi - lo > 0.5)
                        Add(new LinearMarkingDefinition { Code = "LDP", PathRef = PathReference.FromPoints(new[] { L.At(leg, tStop, hi), L.At(leg, tStop, lo) }, z) });
                    if (d.Signs)
                    {
                        if (hi - lo >= 2.2 && tStop + 5 < max)
                            Add(new SymbolMarkingDefinition { Code = "SDP", Length = 3.6, Position = L.At(leg, tStop + 4.5, mid), Direction = dir, Z = z });
                        Add(new SignDefinition { Code = "R-2", Position = signAt, Direction = dir, Z = z, Width = 0.75 });
                    }
                }
            }

            if (f == null) continue;
            var tS = Math.Min(leg.Clear + stopFar, f.TEnd);

            // Linhas da via deslocadas no alargamento (a pintura original é recortada no trecho tratado).
            if (f.Kind is TipoRamo.Gota or TipoRamo.BolsaoAlargado && roadMembers != null && leg.Road < roadMembers.Count && f.TEnd - tS > 1)
            {
                var key = RoadSectionInference.PathKey(r.Def.Path);
                foreach (var m in roadMembers[leg.Road].OfType<LinearMarkingDefinition>())
                {
                    if (RoadSectionInference.IsPhysical(m.Code) || NotShifted.Any(c => m.Code.StartsWith(c))) continue;
                    if (RoadSectionInference.PathKey(m.PathRef) != key) continue;
                    var on = m.Offset * leg.Sign;
                    if (r.Def.TwoWay && Math.Abs(on) < 0.35) continue;   // eixo: substituído pela canalização
                    var sg = Math.Sign(on);
                    var pts = L.LegLine(leg, tS, f.TEnd, t => on + sg * f.Delta(t));
                    var c = (LinearMarkingDefinition)m.CloneWithNewId();
                    c.Exclusions.Clear();
                    c.Offset = 0;
                    c.Alignment = null;
                    c.StartSetback = c.EndSetback = 0;
                    c.Phase = 0;
                    if (leg.Sign < 0) c.InvertSides = !c.InvertSides;
                    c.PathRef = PathReference.FromPoints(pts, z);
                    Add(c);
                }
            }

            var inDir = L.Inbound(leg, tS + 6);
            switch (f.Kind)
            {
                case TipoRamo.Gota:
                {
                    var tl = f.IslandStart + f.IslandLength;
                    if (!f.Painted && tl - 3 > tS)
                        foreach (var sg in new[] { 1.0, -1.0 })
                            Add(new LinearMarkingDefinition { Code = "LFO-1", PathRef = PathReference.FromPoints(L.LegLine(leg, tS, tl - 3, t => sg * f.Delta(t)), z) });
                    break;
                }
                case TipoRamo.BolsaoAlargado:
                {
                    if (f.StorageEnd > tS)
                    {
                        Add(new LinearMarkingDefinition { Code = "LFO-1", PathRef = PathReference.FromPoints(L.LegLine(leg, tS, f.StorageEnd, t => -f.Delta(t)), z) });
                        Add(new LinearMarkingDefinition { Code = "LMS-1", PathRef = PathReference.FromPoints(L.LegLine(leg, tS, f.StorageEnd, t => f.Delta(t)), z) });
                    }
                    Add(new LinearMarkingDefinition { Code = "LMS-2", PathRef = PathReference.FromPoints(L.LegLine(leg, Math.Max(tS, f.StorageEnd), f.TaperEnd, t => f.Delta(t)), z) });
                    if (f.StorageEnd - tS > 8)
                        Add(new SymbolMarkingDefinition { Code = "PEM-E", Length = 5.0, Position = L.At(leg, tS + 4.5, 0), Direction = inDir, Z = z });
                    break;
                }
                case TipoRamo.BolsaoCanteiro:
                {
                    var hi = f.PocketHi;
                    if (f.StorageEnd > tS)
                    {
                        Add(new LinearMarkingDefinition { Code = "LMS-1", PathRef = PathReference.FromPoints(L.LegLine(leg, tS, f.StorageEnd, _ => hi), z) });
                        // Separação do fluxo oposto junto ao que resta do canteiro.
                        Add(new LinearMarkingDefinition { Code = "LFO-1", PathRef = PathReference.FromPoints(L.LegLine(leg, tS, f.StorageEnd, _ => hi - f.PocketWidth + 0.1), z) });
                    }
                    Add(new LinearMarkingDefinition { Code = "LMS-2", PathRef = PathReference.FromPoints(L.LegLine(leg, Math.Max(tS, f.StorageEnd), f.TaperEnd, _ => hi), z) });
                    if (f.StorageEnd - tS > 8)
                        Add(new SymbolMarkingDefinition { Code = "PEM-E", Length = 5.0, Position = L.At(leg, tS + 4.5, hi - f.PocketWidth / 2), Direction = inDir, Z = z });
                    break;
                }
            }
        }

        // Canalização pintada: ilhas entre fluxos de mesmo sentido (branco) e entre fluxos opostos (amarelo).
        hierarchy = L.Roads[L.Main].Def.Hierarchy;
        foreach (var p in L.PaintedIslands)
            Add(new HatchMarkingDefinition { Code = "ZPA", Boundary = PathReference.FromPoints(p.Outer, z, true) });
        foreach (var p in L.PaintedMedians)
            Add(new HatchMarkingDefinition { Code = "ZPA-A", Boundary = PathReference.FromPoints(p.Outer, z, true) });
        return res;
    }
}
