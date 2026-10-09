using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Via participante de uma interseção (pavimento/seção + eixo resolvido).</summary>
public sealed record IntersectionRoad(RoadPavementDefinition Def, Polyline2 Axis)
{
    /// <summary>A via com a seção que tem no ponto <paramref name="p"/> (vias de largura variável).</summary>
    public IntersectionRoad LocalAt(Vec2 p) => Def.HasEdgeVariation ? this with { Def = Def.Local(Axis.Project(p).Station, Axis) } : this;
}

/// <summary>Ramo de uma via a partir do nó da interseção.</summary>
public sealed record IntersectionLeg(int Road, int Sign, double NodeStation, double Clear, Vec2 Dir)
{
    /// <summary>Estação no eixo a uma distância <paramref name="t"/> do nó, ao longo do ramo.</summary>
    public double StationAt(double t) => NodeStation + Sign * t;
}

/// <summary>
/// Rampas de uma travessia: a do lado alto (+o) e a do lado baixo (−o) do ramo (nula = sem calçada ou omitida), o
/// deslocamento extra da travessia para as rampas caberem no trecho reto do meio-fio e os avisos dos ajustes.
/// </summary>
public sealed record RampPlan(RampDefinition? Hi, RampDefinition? Lo, double Shift, IReadOnlyList<string> Notes)
{
    public static RampPlan None { get; } = new(null, null, 0, Array.Empty<string>());
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
/// <summary>Esquina da interseção: via A (lado alto do ramo) → via B (lado baixo do ramo seguinte).</summary>
public sealed record IntersectionCorner(Polygon2 Tri, int RoadA, bool LeftA, int RoadB, bool LeftB, Vec2 PA, Vec2 PB);

public sealed class IntersectionLayout
{
    public Vec2 Node { get; init; }
    /// <summary>Definição que gerou o arranjo (opções de sinalização usadas pelos recortes).</summary>
    public IntersectionDefinition? Definition { get; init; }
    public double Radius { get; set; }
    /// <summary>Emenda de duas vias pela ponta (continuação em ângulo ou com mudança de seção): curva concordada.</summary>
    public bool IsBend { get; set; }
    /// <summary>Eixo da emenda (da via 0 para a via 1, com a curva) e trecho de transição.</summary>
    public Polyline2? BendAxis { get; set; }
    /// <summary>O eixo da via i corre no sentido contrário ao da emenda.</summary>
    public bool[] BendReversed { get; set; } = new bool[2];
    public Polygon2 Zone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Área refeita pela interseção (zona do nó sem os ramos já fora das esquinas + trechos tratados).</summary>
    public List<Polygon2> Rebuild { get; } = new();
    public List<IntersectionRoad> Roads { get; } = new();
    /// <summary>Índice da via preferencial.</summary>
    public int Main { get; set; }
    public List<Polygon2> Pavement { get; } = new();
    public List<Polygon2> Curb { get; } = new();
    public List<Polygon2> Sidewalk { get; } = new();
    /// <summary>
    /// Esquinas entre ramos vizinhos: triângulo (nó e fins das curvas), via e lado de cada ramo – o perfil de calçada de
    /// cada via é aplicado do lado dela e interpolado ao longo da curva.
    /// </summary>
    public List<IntersectionCorner> Corners { get; } = new();
    /// <summary>Calçada das esquinas até a maior das duas larguras (a transição é feita pelo perfil interpolado).</summary>
    public List<Polygon2> SidewalkMax { get; } = new();
    /// <summary>Faixa de serviço gramada das calçadas (composição repetida das vias) – subconjunto de Sidewalk.</summary>
    public List<Polygon2> SidewalkService { get; } = new();
    /// <summary>Sarjeta junto ao meio-fio (composição repetida das vias).</summary>
    public List<Polygon2> Gutter { get; } = new();
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
    /// <summary>
    /// A via principal atravessa o nó sem semáforo: as linhas dela seguem pela boca das secundárias (LCO no miolo, eixo
    /// contínuo só quando as conversões à esquerda são proibidas).
    /// </summary>
    public bool ThroughMain { get; set; }
    /// <summary>Por via: áreas onde a sinalização pintada é removida (miolo + aproximações até a retenção).</summary>
    public Dictionary<int, List<Polygon2>> PaintCuts { get; } = new();
    /// <summary>Faixas além do bordo dos lados retos (sem esquina): nada de outra via entra nelas.</summary>
    public List<Polygon2> StraightSides { get; } = new();
    /// <summary>Ramos cuja travessia sobre o canteiro ficou rebaixada porque as rampas não cabem junto à ponta do canteiro.</summary>
    public HashSet<(int Road, int Sign)> MedianRampsBlocked { get; } = new();
    /// <summary>Por via: áreas onde as vagas são removidas (pintura + 5 m antes da esquina, CTB art. 181).</summary>
    public Dictionary<int, List<Polygon2>> ParkingCuts { get; } = new();
    /// <summary>Por via: áreas onde os elementos físicos (pavimento, meio-fio, calçada, canteiro) são refeitos pela interseção.</summary>
    public Dictionary<int, List<Polygon2>> PhysicalCuts { get; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>Rampas planejadas de cada travessia (via, sentido do ramo) – medidas da interseção já ajustadas.</summary>
    public Dictionary<(int Road, int Sign), RampPlan> RampPlans { get; } = new();
    /// <summary>
    /// Por ramo (via, sentido): distância do nó ao fim da curva da esquina (ponto de tangência do meio-fio com o trecho
    /// reto), o maior dos dois lados. A faixa de pedestres começa nele.
    /// </summary>
    public Dictionary<(int Road, int Sign), double> TangentT { get; } = new();
    /// <summary>Orelhas (avanços de calçada) das esquinas geradas com a interseção.</summary>
    public List<EarPlan> Ears { get; } = new();
    /// <summary>Trecho de cada lado de ramo (via, sentido, +1 alto / −1 baixo) coberto por orelha.</summary>
    public Dictionary<(int Road, int Sign, int Side), EarSide> EarSides { get; } = new();
    public MarkingColor PavementColor { get; set; } = MarkingColor.Asfalto;
    public double PavementThickness { get; set; } = 0.05;
    public double CurbHeight { get; set; } = 0.15;
    public double CurbWidth { get; set; } = 0.15;
    /// <summary>Emenda: meia largura da pista à direita / à esquerda ao longo do eixo da emenda (transição).</summary>
    public Func<double, double>? BendRight { get; set; }
    public Func<double, double>? BendLeft { get; set; }
    /// <summary>Emenda: largura do canteiro central ao longo do eixo da emenda (0 = sem canteiro).</summary>
    public Func<double, double>? BendMedian { get; set; }

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
public static partial class IntersectionGenerator
{
    public const double NodeMergeDistance = 3.0;
    private static readonly string[] PhysicalCodes = { "CALCADA", "GRAMADO", "SARJETA", "SARJETAO", "PLATAFORMA" };

    public static bool IsPhysical(MarkingDefinition d) =>
        d is LinearMarkingDefinition l && (PhysicalCodes.Contains(l.Code) || l.Code.StartsWith("MEIO-FIO"));

    /// <summary>
    /// Elementos da via recortados pelas rampas: calçada, grama, meio-fio e plataforma. A sarjeta fica no nível da pista, à
    /// frente da face do meio-fio onde a rampa começa – não é recortada (senão sobrava um entalhe de 5 cm na sarjeta).
    /// </summary>
    public static bool CutByRamps(MarkingDefinition d) =>
        IsPhysical(d) && d is LinearMarkingDefinition l && !l.Code.StartsWith("SARJETA");

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

    /// <summary>
    /// Eixo prolongado até o nó quando a via termina junto a outra (entroncamento em T). Só prolonga quando o nó fica
    /// ADIANTE da ponta: se a ponta já passou do nó (eixo avançando sobre a outra via), o eixo já passa por ele – inserir o
    /// nó antes da ponta fazia o eixo ir e voltar (grampo), com furos no pavimento e bordos sem meio-fio.
    /// </summary>
    public static Polyline2 ExtendTo(Polyline2 axis, Vec2 node, double tolerance)
    {
        var pts = axis.Points.ToList();
        if (pts.Count < 2) return axis;
        // Prolonga NA DIREÇÃO do próprio eixo até o pé do nó nessa reta: um vértice novo fora dela (dobra a menos de 1 m do
        // nó) invertia o trecho curto nas faixas deslocadas de 5–10 m (calçada, sarjeta) – "cauda de andorinha" que abria
        // furos na pista. Nó muito fora da direção do eixo (> 1,5 m): mantém o prolongamento até o próprio nó.
        Vec2? Extension(Vec2 end, Vec2 inner)
        {
            var t = (end - inner).Normalized();
            var along = (node - end).Dot(t);
            if (along <= 0.01) return null;
            var lateral = Math.Abs(t.Cross(node - end));
            return lateral <= 1.5 ? end + t * along : node;
        }
        var d0 = pts[0].DistanceTo(node);
        var d1 = pts[^1].DistanceTo(node);
        if (d0 <= tolerance && d0 > 0.01 && d0 <= d1 && Extension(pts[0], pts[1]) is { } e0) pts.Insert(0, e0);
        else if (d1 <= tolerance && d1 > 0.01 && Extension(pts[^1], pts[^2]) is { } e1) pts.Add(e1);
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
                    var def = roads[x].Def;
                    foreach (var (end, inner) in new[]
                             {
                                 def.MergeStart ? ((Vec2, Vec2)?)null : (ax.Points[0], ax.Points[1]),
                                 def.MergeEnd ? null : (ax.Points[^1], ax.Points[^2]),
                             }.Where(e => e != null).Select(e => e!.Value))
                    {
                        var (_, dist, q) = Project(roads[y].Axis, end);
                        // Ponta que não chega (ou passa) do eixo da outra: o nó é onde a DIREÇÃO do eixo encontra o da outra via
                        // (o eixo é prolongado em linha reta até ele), não a projeção perpendicular da ponta.
                        if (dist > 0.01 && dist <= tol && RayHit(end, (end - inner).Normalized(), roads[y].Axis, 2 * tol) is { } hit
                            && hit.DistanceTo(end) <= 2.5 * dist + 0.5)
                            q = hit;
                        // Um nó por PAR de vias: um nó próximo de outro par (ex.: duas vias emendadas pela ponta) não impede
                        // que a terceira via, chegando pela ponta, entre no mesmo cruzamento (o agrupamento abaixo os junta).
                        if (dist <= tol && !raw.Any(r => r.Item1.DistanceTo(q) < NodeMergeDistance && (r.Item2 == x && r.Item3 == y || r.Item2 == y && r.Item3 == x)))
                            raw.Add((q, x, y));
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
        // Duas vias pela ponta: qualquer deflexão (> 3°) ou mudança de seção vira emenda concordada (sem cunhas nem degraus).
        if (dirs.Count != 2) return false;
        if (dirs[0].Dot(dirs[1]) > -0.9986) return true;
        var near = roads.Where(r => Project(r.Axis, node).Distance < Math.Max(r.Def.TotalLeft, r.Def.TotalRight) + 3).ToList();
        return near.Count == 2 && (Math.Abs(near[0].Def.RightWidth + near[0].Def.LeftWidth - near[1].Def.RightWidth - near[1].Def.LeftWidth) > 0.05
                                   || Math.Abs(near[0].Def.TotalLeft + near[0].Def.TotalRight - near[1].Def.TotalLeft - near[1].Def.TotalRight) > 0.05);
    }

    /// <summary>Primeiro ponto em que a semirreta (origem <paramref name="o"/>, direção <paramref name="dir"/>) cruza o eixo.</summary>
    private static Vec2? RayHit(Vec2 o, Vec2 dir, Polyline2 axis, double maxDist)
    {
        Vec2? best = null;
        var bestT = maxDist;
        for (int j = 0; j + 1 < axis.Points.Count; j++)
        {
            var q = axis.Points[j];
            var s = axis.Points[j + 1] - q;
            var den = dir.Cross(s);
            if (Math.Abs(den) < 1e-9) continue;
            var t = (q - o).Cross(s) / den;
            var u = (q - o).Cross(dir) / den;
            if (t >= -1e-6 && t <= bestT && u >= -1e-9 && u <= 1 + 1e-9) { bestT = t; best = o + dir * t; }
        }
        return best;
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


    // ------------------------------------------------------------------ emenda pela ponta (continuação)

    /// <summary>
    /// Emenda de duas vias que se encontram pela ponta (continuação): em vez de um cruzamento, a pista, o meio-fio, a
    /// sarjeta, a faixa gramada, a calçada e o canteiro central seguem por uma CURVA concordada entre os dois eixos, com
    /// transição suave de largura quando as seções diferem. As pontas retas das duas vias (que deixavam uma cunha vazia
    /// do lado de fora e se sobrepunham do lado de dentro) são recortadas pela zona da emenda; as linhas pintadas
    /// continuam pela curva (<see cref="BendLines"/>).
    /// </summary>
    private static IntersectionLayout? Bend(IntersectionDefinition d, IReadOnlyList<IntersectionRoad> input)
    {
        if (input.Count != 2) return null;
        var node = d.Node;
        var ends = new List<bool>();
        foreach (var r in input)
        {
            if (r.Axis.Points.Count < 2 || r.Axis.Length < 4) return null;
            var (st, dist, _) = Project(r.Axis, node);
            if (dist > 1.5) return null;
            var tol = Math.Min(1.5, r.Axis.Length * 0.2);
            if (st > tol && st < r.Axis.Length - tol) return null;          // a via atravessa o nó: é um cruzamento
            ends.Add(st >= r.Axis.Length - tol);
        }
        var A = input[0];
        var B = input[1];
        var axA = ends[0] ? A.Axis : A.Axis.Reversed();                     // termina no nó
        var axB = ends[1] ? B.Axis.Reversed() : B.Axis;                     // começa no nó
        var flipA = !ends[0];
        var flipB = ends[1];
        (double R, double L, double TR, double TL, double SR, double SL, double Med) Side(RoadPavementDefinition p, bool flip)
        {
            var med = p.Gaps.Where(g => g.Median && Math.Abs(g.Offset) < 0.3).Select(g => g.Width).DefaultIfEmpty(0).Max();
            return flip ? (p.LeftWidth, p.RightWidth, p.TotalLeft, p.TotalRight, p.LeftSidewalk, p.RightSidewalk, med)
                        : (p.RightWidth, p.LeftWidth, p.TotalRight, p.TotalLeft, p.RightSidewalk, p.LeftSidewalk, med);
        }
        var sa = Side(A.Def, flipA);
        var sb = Side(B.Def, flipB);
        var tA = axA.TangentAt(axA.Length);
        var tB = axB.TangentAt(0);
        var defl = Math.Acos(Math.Clamp(tA.Dot(tB), -1, 1));
        var maxHalf = new[] { sa.TR, sa.TL, sb.TR, sb.TL }.Max();
        var dw = new[] { Math.Abs(sa.R - sb.R), Math.Abs(sa.L - sb.L), Math.Abs(sa.TR - sb.TR), Math.Abs(sa.TL - sb.TL), Math.Abs(sa.Med - sb.Med) }.Max();
        // Raio do eixo na emenda: a borda interna da calçada ainda faz curva (nunca menor que a meia largura + 1,5 m).
        var R = Math.Max(RoadSetup.MinAxisRadius(maxHalf), Math.Max(d.CornerRadius, 2.0 * maxHalf));
        var half = Math.Tan(defl / 2);
        var T = R * half;
        // Transição de largura (1 : 8 por lado, entre 6 e 40 m no total) somada às tangentes da curva.
        var m = Math.Max(2.0, dw > 0.05 ? Math.Clamp(8 * dw, 6, 40) / 2 : 0);
        var limit = 0.45 * Math.Min(axA.Length, axB.Length);
        if (T + m > limit)
        {
            T = Math.Max(0.5, limit - m);
            if (T + m > limit) m = Math.Max(0.5, limit - T);
            if (half > 1e-6) R = T / half;
        }
        var reach = T + m;
        var pA = axA.PointAt(Math.Max(0, axA.Length - reach));
        var pB = axB.PointAt(Math.Min(axB.Length, reach));
        var raw = defl < 0.5 * Math.PI / 180 ? new List<Vec2> { pA, pB } : new List<Vec2> { pA, node, pB };
        var bend = new Polyline2(raw.Count == 3 ? CurveTools.FilletCorners(raw, R, false, 0.3) : raw);
        var len = bend.Length;
        if (len < 1) return null;
        double U(double s) { var u = Math.Clamp(s / len, 0, 1); return u * u * (3 - 2 * u); }
        double Lp(double a, double b, double s) => a + (b - a) * U(s);
        Func<double, double> Right = s => Lp(sa.R, sb.R, s), Left = s => Lp(sa.L, sb.L, s);
        Func<double, double> TotR = s => Lp(sa.TR, sb.TR, s), TotL = s => Lp(sa.TL, sb.TL, s);
        var samples = Enumerable.Range(1, 39).Select(i => len * i / 40.0).ToList();
        List<Polygon2> Band(Func<double, double> lo, Func<double, double> hi) => RoadGenerator.VariableBand(bend, 0, len, lo, hi, samples);

        var L = new IntersectionLayout { Node = node, IsBend = true, BendAxis = bend, BendReversed = new[] { flipA, flipB }, Definition = d, BendRight = Right, BendLeft = Left,
            BendMedian = s => Lp(sa.Med, sb.Med, s) };
        L.Roads.AddRange(input);
        L.PavementColor = A.Def.Color;
        L.PavementThickness = A.Def.ActualThickness;
        L.CurbHeight = A.Def.CurbHeight;
        var cw = Math.Max(A.Def.CurbWidth, B.Def.CurbWidth);
        L.CurbWidth = cw;
        L.Main = 0;
        L.Legs.Add(new IntersectionLeg(0, ends[0] ? -1 : 1, ends[0] ? A.Axis.Length : 0, reach, -tA));
        L.Legs.Add(new IntersectionLeg(1, ends[1] ? -1 : 1, ends[1] ? B.Axis.Length : 0, reach, tB));

        var pav = PolygonOps.Union(Band(s => -Right(s), Left));
        // Canteiro central (quando as duas vias têm): acompanha a curva.
        if (sa.Med > 0.1 || sb.Med > 0.1)
        {
            var med = PolygonOps.Union(Band(s => -Lp(sa.Med, sb.Med, s) / 2, s => Lp(sa.Med, sb.Med, s) / 2)).Where(p => p.Area > 0.2).ToList();
            if (med.Count > 0)
            {
                var core = PolygonOps.Offset(med, -cw);
                L.MedianCurb.AddRange(PolygonOps.Difference(med, core));
                L.MedianCore.AddRange(core);
                L.Obstacles.AddRange(med);
                pav = PolygonOps.Difference(pav, med);
            }
        }
        L.Carriageway.AddRange(pav);
        L.Pavement.AddRange(pav);
        var sideParts = new List<Polygon2>();
        if (sa.SR > 0.01 || sb.SR > 0.01) sideParts.AddRange(Band(s => -TotR(s), s => -Right(s) + 0.01));
        if (sa.SL > 0.01 || sb.SL > 0.01) sideParts.AddRange(Band(s => Left(s) - 0.01, TotL));
        var sides = PolygonOps.Difference(PolygonOps.Union(sideParts), pav);
        var curbRing = PolygonOps.Difference(PolygonOps.Offset(PolygonOps.Union(Band(s => -Right(s), Left)), cw, true), PolygonOps.Union(Band(s => -Right(s), Left)));
        L.Curb.AddRange(PolygonOps.Intersect(curbRing, sides));
        L.Sidewalk.AddRange(PolygonOps.Difference(sides, L.Curb).Where(p => p.Area > 0.05));
        L.SidewalkMax.AddRange(L.Sidewalk);
        if (d.MatchRoadSection && SectionMatch.From(L.Roads.Select(r => r.Def), cw) is { } sec)
        {
            var full = PolygonOps.Union(Band(s => -Right(s), Left));
            if (sec.HasService && sec.Grass) L.SidewalkService.AddRange(SectionMatch.ServiceBand(full, L.Sidewalk, cw, sec.Service));
            if (sec.HasGutter) L.Gutter.AddRange(SectionMatch.GutterBand(full, L.Curb, sec.Gutter));
        }

        // Zona refeita: a faixa da emenda + as pontas retas das duas vias (o canto externo delas sobrava fora da curva).
        var zoneParts = new List<Polygon2>();
        zoneParts.AddRange(Band(s => -TotR(s) - 0.05, s => TotL(s) + 0.05));
        foreach (var (ax, fromEnd, sd) in new[] { (axA, true, sa), (axB, false, sb) })
        {
            var s0 = fromEnd ? Math.Max(0, ax.Length - reach) : 0;
            var s1 = fromEnd ? ax.Length : Math.Min(ax.Length, reach);
            var sub = new Polyline2(ax.SubPoints(s0, s1));
            if (sub.Points.Count >= 2 && sub.Length > 0.05) zoneParts.AddRange(RoadGenerator.Band(sub, -sd.TR - 0.3, sd.TL + 0.3));
        }
        var zone = PolygonOps.Union(zoneParts);
        if (zone.Count == 0) return null;
        L.Zone = zone.OrderByDescending(p => p.Area).First();
        L.Radius = zone.SelectMany(p => p.Outer).Max(v => v.DistanceTo(node));
        L.Rebuild.AddRange(zone);
        for (int i = 0; i < 2; i++)
        {
            L.PhysicalCuts[i] = zone.ToList();
            L.PaintCuts[i] = zone.ToList();
            L.ParkingCuts[i] = PolygonOps.Offset(zone, 2.0, true);
        }
        if (defl > 60 * Math.PI / 180)
            L.Warnings.Add($"Emenda com deflexão de {defl * 180 / Math.PI:0}°: considere uma interseção em T/rotatória ou uma curva de raio maior no eixo.");
        return L;
    }

    /// <summary>
    /// Linhas pintadas da emenda: cada linha longitudinal de uma via continua pela curva até a linha correspondente da
    /// outra (mesmo código e afastamento parecido, com o afastamento interpolado); as sem par vão até o meio da emenda.
    /// </summary>
    private static List<MarkingDefinition> BendLines(IntersectionDefinition d, IntersectionLayout L, double z,
        IReadOnlyList<IReadOnlyCollection<MarkingDefinition>> members)
    {
        var res = new List<MarkingDefinition>();
        var bend = L.BendAxis!;
        var len = bend.Length;
        string[] notLongitudinal = { "FTP-1", "FTP-2", "LRE", "LDP", "MCC", "LRV", "LCO" };
        List<(LinearMarkingDefinition Line, double Off)> Lines(int i)
        {
            if (i >= members.Count) return new();
            var r = L.Roads[i];
            var sNode = Project(r.Axis, L.Node).Station;
            return members[i].OfType<LinearMarkingDefinition>()
                .Where(l => !IsPhysical(l) && !notLongitudinal.Contains(l.Code) && Math.Abs(l.Offset) <= Math.Max(r.Def.TotalLeft, r.Def.TotalRight))
                .Select(l =>
                {
                    var off = l.Offset + (l.PathRef.Lateral is { IsEmpty: false } lat ? lat.ShiftAt(sNode, r.Axis) : 0);
                    return (l, L.BendReversed[i] ? -off : off);
                }).ToList();
        }
        var a = Lines(0);
        var b = Lines(1);
        var usedB = new HashSet<LinearMarkingDefinition>();
        double U(double s) { var u = Math.Clamp(s / len, 0, 1); return u * u * (3 - 2 * u); }
        void Emit(LinearMarkingDefinition src, Func<double, double> off, double s0, double s1)
        {
            if (s1 - s0 < 0.5) return;
            var pts = new List<Vec2>();
            var n = Math.Max(2, (int)Math.Ceiling((s1 - s0) / 0.75));
            for (int k = 0; k <= n; k++)
            {
                var s = s0 + (s1 - s0) * k / n;
                pts.Add(bend.PointAt(s) + bend.TangentAt(s).PerpLeft * off(s));
            }
            var c = (LinearMarkingDefinition)src.CloneWithNewId();
            c.PathRef = PathReference.FromPoints(pts, z);
            c.Offset = 0;
            c.Exclusions.Clear();
            c.Breaks.Clear();
            c.LevelProfile.Clear();
            c.GroupId = d.Id;
            res.Add(c);
        }
        foreach (var (la, oa) in a)
        {
            var pair = b.Where(x => !usedB.Contains(x.Line) && x.Line.Code == la.Code && Math.Sign(x.Off) == Math.Sign(oa) && Math.Abs(x.Off - oa) < 2.5)
                .OrderBy(x => Math.Abs(x.Off - oa)).FirstOrDefault();
            if (pair.Line != null)
            {
                usedB.Add(pair.Line);
                var ob = pair.Off;
                Emit(la, s => oa + (ob - oa) * U(s), 0, len);
            }
            else Unpaired(la, oa, 0, len / 2, true);
        }
        foreach (var (lb, ob) in b.Where(x => !usedB.Contains(x.Line))) Unpaired(lb, ob, len / 2, len, false);
        return res;

        // Linha sem par: a de bordo (LBO) acompanha o bordo da pista na transição, à mesma distância do meio-fio que tem na
        // via; as demais só vão até onde cabem na pista em transição (antes saíam da pista quando a outra via é mais estreita).
        void Unpaired(LinearMarkingDefinition src, double off, double s0, double s1, bool fromA)
        {
            Func<double, double>? half = off >= 0 ? L.BendLeft : L.BendRight;
            if (half == null) { Emit(src, _ => off, s0, s1); return; }
            var sg = Math.Sign(off);
            var gap = half(fromA ? 0 : len) - Math.Abs(off);
            if (src.Code.StartsWith("LBO") && gap > 0 && gap <= 1.0)   // bordo externo (não a linha junto ao canteiro)
            {
                Emit(src, s => sg * (half(s) - gap), s0, s1);
                return;
            }
            // Cabe na pista: dentro do bordo e fora do canteiro central (que também está em transição).
            bool Fits(double s) => Math.Abs(off) + 0.3 <= half(s) && (L.BendMedian == null || L.BendMedian(s) < 0.05 || Math.Abs(off) - 0.2 >= L.BendMedian(s) / 2);
            const double step = 0.25;
            if (fromA) { var e = s0; while (e + step <= s1 && Fits(e + step)) e += step; Emit(src, _ => off, s0, e); }
            else { var b0 = s1; while (b0 - step >= s0 && Fits(b0 - step)) b0 -= step; Emit(src, _ => off, b0, s1); }
        }
    }

    // ------------------------------------------------------------------ layout

    private static List<Polygon2> Close(IEnumerable<Polygon2> p, double r) =>
        r > 0.05 ? PolygonOps.Offset(PolygonOps.Offset(p, r, true), -r, true) : p.ToList();

    /// <summary>Raio máximo do nariz do canteiro central (m) – [a confirmar].</summary>
    public const double MaxNoseRadius = 1.50;

    /// <summary>
    /// Nariz do canteiro central arredondado: os cantos da ponta junto à outra via (<paramref name="ends"/>) com raio de até
    /// metade da largura (semicírculo no canteiro estreito), limitado a <paramref name="rMax"/>. O raio cai (até 0,35 m) se a
    /// ponta é estreita (bolsão) e o arredondamento a apagaria; os outros cantos (travessias, bolsões) ficam como estão.
    /// </summary>
    internal static List<Polygon2> RoundNoses(List<Polygon2> med, List<Polygon2> ends, double rMax)
    {
        if (ends.Count == 0 || rMax <= 0.35) return med;
        var touch = PolygonOps.Offset(ends, 0.05);
        static double Area(List<Polygon2> a) => a.Sum(x => x.Area);
        var res = new List<Polygon2>();
        foreach (var p in med)
        {
            var one = new List<Polygon2> { p };
            if (Area(PolygonOps.Intersect(one, touch)) < 1e-4) { res.Add(p); continue; }
            List<Polygon2>? done = null;
            for (var r = rMax; r > 0.35 && done == null; r *= 0.8)
            {
                // Só os cantos de verdade (as lascas numéricas do abre/fecha ao longo dos lados retos ficam).
                var loss = PolygonOps.Difference(one, Open(one, r)).Where(x => x.Area > 1e-3 && Area(PolygonOps.Intersect(new[] { x }, touch)) > 1e-5).ToList();
                if (loss.Count == 0) { done = one; break; }
                // Abre 2 cm: sem agulhas de área nula onde dois cantos tirados se tocam.
                var q = Open(PolygonOps.Difference(one, loss), 0.02).Where(x => x.Area > 1e-3).ToList();
                // A ponta continua junto à outra via (o nariz não recua) e o canteiro não se divide.
                if (q.Count == 1 && Area(PolygonOps.Intersect(PolygonOps.Offset(q, 0.06), ends)) > 1e-4) done = q;
            }
            res.AddRange(done ?? one);
        }
        return res;
    }

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
                // O ramo fica livre depois do ÚLTIMO corte transversal que ainda toca as outras vias (e as esquinas): num
                // cruzamento oblíquo o corte junto ao nó cai inteiro dentro da própria pista (a outra via passa por dentro
                // dela) e parecia livre logo em t = 0 – a travessia ia parar dentro da outra via.
                double clear = -1;
                var lastHit = -1.0;
                var tMax = Math.Min(avail, reach);
                for (var t = 0.0; t <= tMax; t += 0.25)
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
                    if (hit) lastHit = t;
                }
                if (lastHit < 0) clear = 0;
                else if (lastHit + 0.25 <= tMax) clear = lastHit + 0.25;
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
    private static double StopFar(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg) =>
        CrosswalkOn(d, L, leg) ? TangentGap(L, leg) + SetbackOf(d, L, leg) + CrosswalkWidthOf(d, L, leg) + (d.StopLines ? 1.6 + 0.4 : 0) + 0.3 : (d.StopLines ? 1.5 : 0.3);

    // ------------------------------------------------------------------ ajustes por ramo e rampas das travessias

    /// <summary>Ajustes gravados para o ramo (via + sentido a partir do nó), se houver.</summary>
    public static IntersectionLegSettings? LegSet(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg) =>
        d.LegSettings.Count == 0 || leg.Road >= L.Roads.Count ? null
            : d.LegSettings.FirstOrDefault(s => s.RoadId == L.Roads[leg.Road].Def.Id && s.Sign == leg.Sign);

    /// <summary>Ajustes do ramo, criados vazios quando ainda não existem (janela de ajustes por esquina).</summary>
    public static IntersectionLegSettings LegSetOrNew(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg)
    {
        if (LegSet(d, L, leg) is { } s) return s;
        s = new IntersectionLegSettings { RoadId = L.Roads[leg.Road].Def.Id, Sign = leg.Sign };
        d.LegSettings.Add(s);
        return s;
    }

    /// <summary>"Aplicar a todas as esquinas": copia os ajustes de um ramo para todos os ramos da interseção.</summary>
    public static void ApplyToAllLegs(IntersectionDefinition d, IntersectionLayout L, IntersectionLegSettings src)
    {
        var copy = new IntersectionLegSettings
        {
            CornerRadius = src.CornerRadius, Crosswalk = src.Crosswalk, CrosswalkWidth = src.CrosswalkWidth, CrosswalkSetback = src.CrosswalkSetback,
            Ramps = src.Ramps, Control = src.Control, Treatment = src.Treatment,
            CurbExtension = src.CurbExtension, CurbExtensionLength = src.CurbExtensionLength,
            CurbExtensionLengthOther = src.CurbExtensionLengthOther, CurbExtensionToParking = src.CurbExtensionToParking,
        };
        foreach (var leg in L.Legs)
        {
            var s = LegSetOrNew(d, L, leg);
            s.CornerRadius = copy.CornerRadius;
            s.Crosswalk = copy.Crosswalk;
            s.CrosswalkWidth = copy.CrosswalkWidth;
            s.CrosswalkSetback = copy.CrosswalkSetback;
            s.Ramps = copy.Ramps;
            s.Control = copy.Control;
            s.Treatment = copy.Treatment;
            s.CurbExtension = copy.CurbExtension;
            s.CurbExtensionLength = copy.CurbExtensionLength;
            s.CurbExtensionLengthOther = copy.CurbExtensionLengthOther;
            s.CurbExtensionToParking = copy.CurbExtensionToParking;
            s.CurbExtensionDepth = src.CurbExtensionDepth;
            s.CurbExtensionEnds = src.CurbExtensionEnds;
            s.CurbExtensionEndRadius = src.CurbExtensionEndRadius;
            s.CurbExtensionRamp = src.CurbExtensionRamp;
            s.CurbExtensionTactile = src.CurbExtensionTactile;
            s.OppositeExtension = src.OppositeExtension;
            s.OppositeExtensionDepth = src.OppositeExtensionDepth;
            s.OppositeExtensionBefore = src.OppositeExtensionBefore;
            s.OppositeExtensionAfter = src.OppositeExtensionAfter;
            s.OppositeExtensionEnds = src.OppositeExtensionEnds;
            s.OppositeExtensionEndRadius = src.OppositeExtensionEndRadius;
            s.OppositeExtensionRamp = src.OppositeExtensionRamp;
            s.OppositeExtensionTactile = src.OppositeExtensionTactile;
        }
        d.LegSettings.RemoveAll(x => x.IsEmpty);
    }

    /// <summary>Nome de um ramo para a janela: via, largura da pista e para onde ele vai a partir do nó.</summary>
    public static string LegLabel(IntersectionLayout L, IntersectionLeg leg)
    {
        string[] rosa = { "leste", "nordeste", "norte", "noroeste", "oeste", "sudoeste", "sul", "sudeste" };
        var a = (AngleOf(leg.Dir) + 360) % 360;
        var dir = rosa[(int)Math.Round(a / 45) % 8];
        var r = L.Roads[leg.Road].Def;
        var roadNo = L.Roads.Select(x => x.Def.Id).Distinct().ToList().IndexOf(r.Id) + 1;
        return $"Via {roadNo} ({(r.RightWidth + r.LeftWidth).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} m) – ramo para o {dir}{(leg.Road == L.Main ? ", principal" : "")}";
    }

    /// <summary>O ramo tem faixa de pedestres.</summary>
    public static bool CrosswalkOn(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg) => LegSet(d, L, leg)?.Crosswalk ?? d.Crosswalks;

    /// <summary>Largura da faixa de pedestres do ramo (m).</summary>
    public static double CrosswalkWidthOf(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg) =>
        Math.Max(1.0, LegSet(d, L, leg)?.CrosswalkWidth ?? d.CrosswalkWidth);

    /// <summary>A travessia do ramo tem rampas.</summary>
    public static bool RampsOn(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg) =>
        CrosswalkOn(d, L, leg) && (LegSet(d, L, leg)?.Ramps ?? d.Ramps);

    /// <summary>Recuo da faixa a partir do fim da curva: o pedido + o deslocamento para as rampas caberem no trecho reto.</summary>
    public static double SetbackOf(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg) =>
        Math.Max(0, LegSet(d, L, leg)?.CrosswalkSetback ?? d.CrosswalkSetback) + RampPlanOf(d, L, leg).Shift;

    /// <summary>
    /// Do ponto livre do ramo (<see cref="IntersectionLeg.Clear"/>) ao fim da curva da esquina: o recuo da faixa é medido a
    /// partir do ponto de tangência – a faixa fica toda no trecho reto do meio-fio, o mais perto possível da esquina.
    /// </summary>
    public static double TangentGap(IntersectionLayout L, IntersectionLeg leg) =>
        L.TangentT.TryGetValue((leg.Road, leg.Sign), out var t) ? Math.Max(0, t - leg.Clear) : 0;

    /// <summary>Distância do nó ao eixo da faixa de pedestres do ramo.</summary>
    public static double CrosswalkT(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg) =>
        leg.Clear + TangentGap(L, leg) + SetbackOf(d, L, leg) + CrosswalkWidthOf(d, L, leg) / 2;

    /// <summary>Distância do nó à borda da faixa de pedestres junto à esquina (fim da curva + recuo).</summary>
    public static double CrosswalkNearT(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg) =>
        leg.Clear + TangentGap(L, leg) + SetbackOf(d, L, leg);

    /// <summary>Canteiro central atravessado pela faixa do ramo: faces (referencial do ramo), tipo da travessia e rampa-modelo.</summary>
    public sealed record MedianPass(double Lo, double Hi, Automation.TipoTravessiaCanteiro Mode, RampDefinition Template)
    {
        public double Width => Hi - Lo;
    }

    /// <summary>
    /// Canteiros centrais que a faixa de pedestres do ramo atravessa (os laterais terminam antes da travessia). Rampas e
    /// patamar quando o canteiro comporta (e a interseção tem rampas); senão, passagem rebaixada no nível da pista.
    /// </summary>
    public static List<MedianPass> MedianPassesOf(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg)
    {
        var res = new List<MedianPass>();
        if (leg.Road >= L.Roads.Count || !CrosswalkOn(d, L, leg)) return res;
        var r = L.Roads[leg.Road].Def;
        var tpl = RampTemplate(d, r, CrosswalkWidthOf(d, L, leg));
        // Rampas do canteiro: com abas (o rebaixamento total é da calçada), na altura do meio-fio do canteiro.
        tpl.Type = tpl.Type == TipoRampa.RebaixamentoSemAbas ? TipoRampa.RebaixamentoSemAbas : TipoRampa.RebaixamentoComAbas;
        tpl.Height = Math.Clamp(L.CurbHeight, 0.02, 0.40);
        tpl.CutSidewalk = false;
        var ramps = RampsOn(d, L, leg);
        foreach (var g in r.Gaps.Where(g => g.Median && Math.Abs(g.Offset) <= g.Width / 2 + 0.1 && g.Width > 0.05))
        {
            var a = leg.Sign * (g.Offset - g.Width / 2);
            var b = leg.Sign * (g.Offset + g.Width / 2);
            var mode = ramps && !L.MedianRampsBlocked.Contains((leg.Road, leg.Sign))
                ? Automation.MedianCrossing.Resolve(g.Width, tpl, d.MedianCrossing) : Automation.TipoTravessiaCanteiro.NivelDaPista;
            res.Add(new MedianPass(Math.Min(a, b), Math.Max(a, b), mode, tpl));
        }
        return res;
    }

    /// <summary>Rampas planejadas para a travessia do ramo (calculadas uma vez por arranjo).</summary>
    public static RampPlan RampPlanOf(IntersectionDefinition d, IntersectionLayout L, IntersectionLeg leg)
    {
        if (leg.Road >= L.Roads.Count || !RampsOn(d, L, leg)) return RampPlan.None;
        if (L.RampPlans.TryGetValue((leg.Road, leg.Sign), out var p)) return p;
        p = PlanRamps(d, L.Roads[leg.Road].Def, leg.Sign, CrosswalkWidthOf(d, L, leg),
            Math.Max(0, LegSet(d, L, leg)?.CrosswalkSetback ?? d.CrosswalkSetback), CrosswalkInset(L, leg, 1), CrosswalkInset(L, leg, -1));
        // Ajustes de cada extensão: sem rampa naquele lado, ou piso tátil próprio.
        RampDefinition? Ear(RampDefinition? rp, int side)
        {
            if (rp == null || !L.EarSides.TryGetValue((leg.Road, leg.Sign, side), out var e)) return rp;
            if (!e.Ramp) return null;
            if (e.Tactile is { } t && t != rp.Tactile)
            {
                var c = (RampDefinition)rp.ShallowCopy();
                c.Tactile = t;
                return c;
            }
            return rp;
        }
        p = p with { Hi = Ear(p.Hi, 1), Lo = Ear(p.Lo, -1) };
        L.RampPlans[(leg.Road, leg.Sign)] = p;
        return p;
    }

    /// <summary>
    /// Confere as rampas nos ramos definitivos contra a pista real (curva da esquina, faixa de conversão, alargamentos):
    /// enquanto o corpo da rampa (da face do meio-fio para dentro) tocar a pista, a travessia é recuada mais 5 cm. Ramo
    /// curto: rampa omitida. Junta os avisos dos ajustes.
    /// </summary>
    private static void FinishRampPlans(IntersectionDefinition d, IntersectionLayout L, List<Polygon2> pav)
    {
        if (L.Legs.Count <= 2) return;
        var notes = new List<(string Text, int Count)>();
        var shifts = new List<double>();
        foreach (var leg in L.Legs)
        {
            var p = RampPlanOf(d, L, leg);
            if (p.Hi == null && p.Lo == null) continue;
            var extra = 0.0;
            bool Touches()
            {
                var tc = CrosswalkT(d, L, leg) + extra;
                foreach (var (sgn, rp) in new[] { (1.0, p.Hi), (-1.0, p.Lo) })
                {
                    if (rp == null) continue;
                    var w = (sgn > 0 ? L.HiEdge(leg, tc) : L.LoEdge(leg, tc)) - EarInset(L, leg, (int)sgn, tc);
                    var curb = L.At(leg, tc, sgn * w);
                    var up = (L.At(leg, tc, sgn * (w + 1)) - curb).Normalized();
                    var f = new RampGenerator.Frame(curb, up, up.PerpLeft);
                    var (_, len, _) = RampGenerator.Dimensions(rp);
                    var ext = RampHalfExtent(rp);
                    var body = new Polygon2(new[] { f.P(-ext, 0.01), f.P(ext, 0.01), f.P(ext, len), f.P(-ext, len) });
                    if (PolygonOps.TotalArea(PolygonOps.Intersect(new[] { body }, pav)) > 1e-4) return true;
                }
                return false;
            }
            while (extra < 3.0 && Touches()) extra += 0.05;
            if (extra > 1e-6)
            {
                var shift = p.Shift + extra;
                var ns = p.Notes.Where(n => !n.StartsWith("travessia recuada")).Append(
                    $"travessia recuada mais {shift:0.00} m para a rampa ficar inteira no trecho reto do meio-fio, fora da curva da esquina").ToList();
                p = p with { Shift = shift, Notes = ns };
                L.RampPlans[(leg.Road, leg.Sign)] = p;
            }
            // A rampa (até a ponta da aba do lado de fora do nó) precisa caber antes do fim do ramo.
            var room = MaxT(L, leg) - 0.5 - CrosswalkT(d, L, leg);
            var hi = p.Hi != null && RampHalfExtent(p.Hi) > room ? null : p.Hi;
            var lo = p.Lo != null && RampHalfExtent(p.Lo) > room ? null : p.Lo;
            if (hi != p.Hi || lo != p.Lo)
            {
                p = p with { Hi = hi, Lo = lo, Notes = p.Notes.Append("ramo curto demais para a rampa inteira – rampa omitida; prolongue a via ou reduza a rampa").ToList() };
                L.RampPlans[(leg.Road, leg.Sign)] = p;
            }
            if (p.Shift > 1e-6) shifts.Add(p.Shift);
            foreach (var n in p.Notes.Where(n => !n.StartsWith("travessia recuada")).Distinct())
            {
                var k = notes.FindIndex(x => x.Text == n);
                if (k < 0) notes.Add((n, 1));
                else notes[k] = (n, notes[k].Count + 1);
            }
        }
        // Esquinas muito agudas: as rampas das duas travessias da esquina se encontram – as duas recuam juntas (passo de
        // 10 cm, até 6 m) até ficarem separadas por 0,30 m.
        List<Polygon2> Bodies(IntersectionLeg leg)
        {
            var res = new List<Polygon2>();
            var p = RampPlanOf(d, L, leg);
            var tc = CrosswalkT(d, L, leg);
            foreach (var (sgn, rp) in new[] { (1.0, p.Hi), (-1.0, p.Lo) })
            {
                if (rp == null) continue;
                // Contorno real da rampa (o mesmo que os filhos geram), com folga de 0,15 m.
                var w = (sgn > 0 ? L.HiEdge(leg, tc) : L.LoEdge(leg, tc)) - EarInset(L, leg, (int)sgn, tc);
                var curb = L.At(leg, tc, sgn * w);
                var up = (L.At(leg, tc, sgn * (w + 1)) - curb).Normalized();
                var path = new Polyline2(new[] { curb, curb + up });
                var probe = (RampDefinition)rp.CloneWithNewId();
                probe.PathRef = PathReference.FromPoints(path.Points, 0);
                res.AddRange(PolygonOps.Offset(new[] { RampGenerator.Footprint(probe, path) }, 0.15));
            }
            return res;
        }
        var clashMoved = new HashSet<IntersectionLeg>();
        for (int guard = 0; guard < 100; guard++)
        {
            var bodies = L.Legs.ToDictionary(l => l, Bodies);
            var clash = false;
            for (int i = 0; i < L.Legs.Count && !clash; i++)
                for (int j = i + 1; j < L.Legs.Count && !clash; j++)
                {
                    var (a, b) = (L.Legs[i], L.Legs[j]);
                    if (bodies[a].Count == 0 || bodies[b].Count == 0) continue;
                    if (PolygonOps.TotalArea(PolygonOps.Intersect(bodies[a], bodies[b])) < 1e-3) continue;
                    clash = true;
                    foreach (var leg in new[] { a, b })
                    {
                        var p = RampPlanOf(d, L, leg);
                        L.RampPlans[(leg.Road, leg.Sign)] = p with { Shift = p.Shift + 0.10 };
                        clashMoved.Add(leg);
                    }
                }
            if (!clash) break;
        }
        if (clashMoved.Count > 0)
        {
            var moved = clashMoved.Select(l => RampPlanOf(d, L, l).Shift).ToList();
            L.Warnings.Add($"Rampas: esquina aguda – {clashMoved.Count} travessias recuadas até {moved.Max():0.00} m para as rampas das duas vias não se encontrarem.");
            foreach (var l in clashMoved) if (!shifts.Contains(RampPlanOf(d, L, l).Shift)) shifts.Add(RampPlanOf(d, L, l).Shift);
        }
        foreach (var (n, c) in notes) L.Warnings.Add($"Rampas: {n}{(c > 1 ? $" ({c} travessias)" : "")}.");
        if (shifts.Count > 0)
        {
            var (a, b) = (shifts.Min(), shifts.Max());
            var span = b - a < 0.005 ? $"{a:0.00} m" : $"{a:0.00} a {b:0.00} m";
            L.Warnings.Add($"Rampas: travessia recuada mais {span} (além do recuo pedido) para a rampa ficar inteira no trecho reto do meio-fio, " +
                           $"fora da curva da esquina{(shifts.Count > 1 ? $" ({shifts.Count} travessias)" : "")}.");
        }
    }

    /// <summary>
    /// Ponta da via que passou do nó (menos de 3 m além dele, sem ramo): o trecho da ponta – pista, meio-fio e calçada,
    /// com a tampa – sai da via. Sem isso a tampa oblíqua da calçada invadia a calçada da outra via do lado oposto.
    /// </summary>
    private static List<Polygon2> Stubs(IntersectionLayout L, int i)
    {
        var res = new List<Polygon2>();
        var r = L.Roads[i];
        var axis = r.Axis;
        var sn = Project(axis, L.Node).Station;
        var wide = Math.Max(r.Def.TotalLeft, r.Def.TotalRight) + 0.3;
        foreach (var sign in new[] { -1, 1 })
        {
            var rest = sign > 0 ? axis.Length - sn : sn;
            if (rest < 0.01 || rest >= 3) continue;
            var a = axis.PointAt(sn);
            var tip = axis.PointAt(sign > 0 ? axis.Length : 0);
            var dir = (tip - a).Length > 1e-6 ? (tip - a).Normalized() : axis.TangentAt(sn) * sign;
            res.AddRange(RoadGenerator.Band(new Polyline2(new[] { a, tip + dir * 1.0 }), -wide, wide));
        }
        return res;
    }

    /// <summary>Rampa com as medidas pedidas na interseção (antes dos ajustes).</summary>
    public static RampDefinition RampTemplate(IntersectionDefinition d, RoadPavementDefinition r, double crosswalkWidth) => new()
    {
        Type = d.RampType == TipoRampa.AcessoVeiculos ? TipoRampa.RebaixamentoComAbas : d.RampType,
        Width = Math.Max(MinRampWidth, d.RampWidth ?? crosswalkWidth),
        Height = Math.Clamp(d.RampCurbHeight is > 0.02 ? d.RampCurbHeight.Value : r.CurbHeight, 0.02, 0.40),
        Slope = Math.Clamp(d.RampSlope, 0.02, MaxRampSlope),
        FlareSlope = Math.Clamp(d.RampFlareSlope, 0.02, MaxFlareSlope),
        Tactile = d.RampTactile,
        DirectionalTactile = d.RampDirectional,
        SquareCut = true,
    };

    /// <summary>Largura da faixa de serviço da calçada de um lado da via (entre o meio-fio e a faixa livre; 0 sem ela).</summary>
    public static double ServiceStrip(RoadPavementDefinition r, bool left)
    {
        var setup = RoadTemplates.FromJson(r.SetupJson);
        if (setup == null) return 0;
        var side = left ? setup.Left : setup.Right;
        var walk = side.FirstOrDefault(e => e.Tipo == TipoElementoSecao.Calcada);
        if (walk == null) return 0;
        var cw = Math.Min(walk.MeioFioEfetivo, walk.Largura - 0.05);
        return Math.Max(0, Math.Clamp(walk.FaixaServico, cw, walk.Largura) - cw);
    }

    public const double MinRampWidth = 1.50;
    public const double MaxRampSlope = 0.0833;
    public const double MaxFlareSlope = 0.10;
    /// <summary>Inclinação máxima das rampas laterais do rebaixamento total (NBR 9050, 6.12.7.3.3: 5 %).</summary>
    public const double TotalSideSlope = 0.05;
    /// <summary>Deslocamento máximo da travessia (além do recuo pedido) antes de reduzir a largura ou tirar as abas.</summary>
    public const double MaxRampShift = 2.0;
    /// <summary>Folga entre a rampa (ponta da aba) e o fim da curva do meio-fio.</summary>
    public const double RampCurveGap = 0.10;

    /// <summary>Extensão da rampa ao longo do meio-fio, do centro até a ponta da aba (ou da rampa lateral).</summary>
    public static double RampHalfExtent(RampDefinition r)
    {
        var (w, _, fl) = RampGenerator.Dimensions(r);
        return w / 2 + (r.Type == TipoRampa.RebaixamentoSemAbas ? 0 : fl);
    }

    /// <summary>
    /// Rampas de uma travessia com as medidas da interseção, ajustadas para caber sem falhas (NBR 9050):
    /// <list type="number">
    /// <item>calçada que não comporta a rampa e a faixa livre → rebaixamento total da calçada (rampas laterais ≤ 5 %);</item>
    /// <item>rampa que invadiria a curva da esquina → a travessia (com a rampa centrada nela) é deslocada para o trecho
    /// reto do meio-fio, até 2 m além do recuo pedido;</item>
    /// <item>ainda não cabe → largura automática reduzida (mínimo 1,50 m), depois rampa sem abas; por fim a travessia é
    /// deslocada o que for preciso.</item>
    /// </list>
    /// Cada ajuste gera um aviso. As duas rampas (lado alto e baixo do ramo) ficam no eixo da mesma faixa.
    /// </summary>
    public static RampPlan PlanRamps(IntersectionDefinition d, RoadPavementDefinition r, int sign, double crosswalkWidth, double setback,
        double earHi = 0, double earLo = 0)
    {
        var notes = new List<string>();
        var free = Math.Max(1.20, d.RampFreeWidth);
        // Com orelha, a rampa começa na borda dela: a profundidade disponível é a calçada + o avanço.
        var walks = new[] { sign > 0 ? r.LeftSidewalk : r.RightSidewalk, sign > 0 ? r.RightSidewalk : r.LeftSidewalk };
        if (walks[0] > 0.5) walks[0] += earHi;
        if (walks[1] > 0.5) walks[1] += earLo;
        var ramps = new RampDefinition?[2];
        for (int k = 0; k < 2; k++)
        {
            var walk = walks[k];
            if (walk <= 0.5) continue;
            var rp = RampTemplate(d, r, crosswalkWidth);
            // Com orelha a rampa fica sobre ela: um patamar nivelado atravessa o resto da orelha, a faixa do meio-fio antigo e a
            // faixa de serviço (grama) até a faixa livre da calçada – senão a rampa desembocava na grama.
            var ear = k == 0 ? earHi : earLo;
            if (ear > 0.05 && rp.Type != TipoRampa.RebaixamentoTotal)
            {
                var (_, run0, _) = RampGenerator.Dimensions(rp);
                var service = ServiceStrip(r, (k == 0) == (sign > 0));
                rp.LandingDepth = Math.Max(rp.LandingDepth, Math.Round(Math.Max(0, ear + r.CurbWidth + service - run0), 2));
            }
            if (rp.Type != TipoRampa.RebaixamentoTotal)
            {
                var (_, run, _) = RampGenerator.Dimensions(rp);
                // Tolerância de 1 cm: 8,33 % é 1:12 (a rampa de 0,15 m tem 1,80 m).
                if (walk - run - rp.LandingDepth < free - 0.01)
                {
                    notes.Add($"calçada de {walk:0.00} m não comporta a rampa de {run:0.00} m e a faixa livre de {free:0.00} m – feito o rebaixamento total da calçada (NBR 9050, 6.12.7.3.3)");
                    rp.Type = TipoRampa.RebaixamentoTotal;
                }
            }
            if (rp.Type == TipoRampa.RebaixamentoTotal)
            {
                // Platô no nível da pista em toda a profundidade da calçada; rampas laterais ao longo do meio-fio.
                rp.SidewalkDepth = walk;
                rp.Slope = Math.Min(rp.Slope, TotalSideSlope);
                rp.DirectionalTactile = false;
            }
            ramps[k] = rp;
        }
        if (ramps.All(x => x == null)) return RampPlan.None;
        var avail = setback + crosswalkWidth / 2 - RampCurveGap;
        double Need() => ramps.Where(x => x != null).Select(x => RampHalfExtent(x!) - avail).Max();
        if (Need() > MaxRampShift && d.RampWidth == null)
        {
            // Largura automática (= faixa): reduz só o necessário para o deslocamento não passar do máximo.
            var before = ramps.Where(x => x != null).Max(x => x!.Width);
            foreach (var x in ramps.Where(x => x != null))
            {
                var over = RampHalfExtent(x!) - avail - MaxRampShift;
                if (over > 0) x!.Width = Math.Max(MinRampWidth, Math.Round(x.Width - 2 * over - 0.005, 2));
            }
            var after = ramps.Where(x => x != null).Min(x => x!.Width);
            if (after < before - 1e-6)
                notes.Add($"largura da rampa reduzida de {before:0.00} m para {after:0.00} m (mínimo 1,50 m, NBR 9050, 6.12.7.3.1) para caber fora da curva da esquina");
        }
        if (Need() > MaxRampShift)
            foreach (var x in ramps.Where(x => x is { Type: TipoRampa.RebaixamentoComAbas }))
                if (RampHalfExtent(x!) - avail > MaxRampShift)
                {
                    x!.Type = TipoRampa.RebaixamentoSemAbas;
                    notes.Add("rampa sem abas para caber fora da curva da esquina – proteja as laterais (faixa de serviço, mobiliário)");
                }
        var shift = Math.Max(0, Need());
        if (shift > 1e-3)
        {
            shift = Math.Ceiling(shift * 20 - 1e-6) / 20;
            notes.Add($"travessia recuada mais {shift:0.00} m para a rampa ficar inteira no trecho reto do meio-fio, fora da curva da esquina");
        }
        return new RampPlan(ramps[0], ramps[1], shift, notes);
    }

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
            // Ajuste do ramo: ilha (secundária) / bolsão (principal) sim ou não; sem ajuste, a regra geral.
            var treat = LegSet(d, L, leg)?.Treatment;
            var islandType = d.SplitterIslands != TipoIlha.Nenhuma ? d.SplitterIslands : TipoIlha.Fisica;
            if (leg.Road != L.Main && (treat ?? d.SplitterIslands != TipoIlha.Nenhuma))
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
                var len = Math.Max(Math.Max(6, d.SplitterLength), CrosswalkOn(d, L, leg) ? c + SetbackOf(d, L, leg) + CrosswalkWidthOf(d, L, leg) + 3.5 - t0 : 0);
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
                    IslandStart = t0, IslandLength = len, IslandWidth = w, Painted = islandType == TipoIlha.Pintada,
                });
            }
            else if (leg.Road == L.Main && (treat ?? d.LeftTurnPockets))
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
        var L = new IntersectionLayout { Node = d.Node, Definition = d };
        if (input.Count < 2)
        {
            L.Warnings.Add("A interseção precisa de pelo menos duas vias com pavimento (Sinalizar via).");
            return L;
        }
        var node = d.Node;
        // Vias de largura variável: a interseção usa a seção de cada via no ponto do cruzamento.
        input = input.Select(r => r.LocalAt(node)).ToList();
        // Duas vias que se encontram pela ponta: emenda concordada, não cruzamento.
        if (Bend(d, input) is { } bend) return bend;
        var rc = Math.Max(0, d.CornerRadius);
        // Raio por esquina (ajuste por ramo: esquina à direita de quem chega): o menor fecha a pista toda; os maiores
        // acrescentam a curva só na esquina deles.
        var radii = d.LegSettings.Where(x => x.CornerRadius != null).Select(x => Math.Max(0, x.CornerRadius!.Value)).Append(rc).ToList();
        var rMin = radii.Min();
        var rMax = radii.Max();
        var channels = d.RightTurnIslands != TipoIlha.Nenhuma;
        var rs = channels ? Math.Max(d.RightTurnRadius, rMax + 3) : rMax;
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
        var anyTreatment = d.LegSettings.Any(x => x.Treatment == true);
        var reachFeat = (d.SplitterIslands != TipoIlha.Nenhuma || anyTreatment ? Math.Max(d.SplitterLength, 12) + 25 : 0);
        reachFeat = Math.Max(reachFeat, d.LeftTurnPockets || anyTreatment ? d.PocketLength + d.PocketTaper + Math.Max(15, 10 * d.PocketWidth) : 0);
        var baseReach = maxHalf / Math.Max(0.25, minSin) * 2 + 2 * rs + 12;
        var r1 = Math.Min(260, baseReach + reachFeat + (reachFeat > 0 ? 10 : 0));
        var big = Circle(node, r1);
        var clip = new[] { Circle(node, r1 - rs - 1) };
        var reach = Math.Min(r1 - rs - 2, baseReach);

        // Pista de cada via (inteira) e seus canteiros centrais.
        var cn = L.Roads.Select(r => PolygonOps.Intersect(RoadGenerator.Band(r.Axis, -r.Def.RightWidth, r.Def.LeftWidth), new[] { big })).ToList();
        var medBands = L.Roads.Select(r => PolygonOps.Intersect(PolygonOps.Union(r.Def.Gaps.Where(g => g.Median)
            .SelectMany(g => RoadGenerator.Band(r.Axis, g.Offset - g.Width / 2, g.Offset + g.Width / 2))), new[] { big })).ToList();

        // Pista com o raio de cada esquina: o menor em toda a pista + a curva maior só no setor da esquina que a pede.
        // Esquina aguda: o raio é reduzido para a curva não avançar mais que o raio pedido ao longo das vias (a tangente de
        // um arco de raio R num canto de θ é R·cot(θ/2): 2,4 R a 45°, 3,7 R a 30° – o asfalto engolia a esquina).
        List<Polygon2> Corners(List<Polygon2> u, IReadOnlyList<IntersectionLeg> lg, bool warn = false)
        {
            if (lg.Count < 2) return PolygonOps.Intersect(Close(u, rMin), clip);
            var sorted = lg.Select(l => (Leg: l, A: AngleOf(l.Dir))).OrderBy(x => x.A).ToList();
            var cs = new List<(double From, double Span, double R)>();
            for (int k = 0; k < sorted.Count; k++)
            {
                var (la, aa) = sorted[k];
                var (lb, ab) = sorted[(k + 1) % sorted.Count];
                var span = Ccw(aa, ab);
                if (Straight(la, lb, span)) continue;
                var R = Math.Max(0, LegSet(d, L, la)?.CornerRadius ?? rc);
                var eff = span < 90 ? R * Math.Tan(span * Math.PI / 360) : R;
                eff = Math.Round(eff, 2);
                if (warn && R - eff >= 0.05)
                    L.Warnings.Add($"Esquina aguda ({span:0}°): raio reduzido de {R:0.0#} m para {eff:0.0#} m – a curva não avança mais que o raio pedido ao longo das vias.");
                if (warn) NoteTangent(la, lb, span, eff);
                cs.Add((aa, span, eff));
            }
            if (cs.Count == 0) return PolygonOps.Intersect(Close(u, rMin), clip);
            var rb = cs.Min(c => c.R);
            var basis = PolygonOps.Intersect(Close(u, rb), clip);
            var extra = new List<Polygon2>();
            var cache = new Dictionary<double, List<Polygon2>>();
            foreach (var (from, span, R) in cs)
            {
                if (R - rb < 0.05) continue;
                if (!cache.TryGetValue(R, out var closed)) cache[R] = closed = PolygonOps.Difference(PolygonOps.Intersect(Close(u, R), clip), basis);
                foreach (var lobe in closed.Where(p => p.Area > 0.05))
                {
                    var v = lobe.Centroid - node;
                    if (v.Length < reach && Ccw(from, AngleOf(v)) <= span) extra.Add(lobe);
                }
            }
            return extra.Count == 0 ? basis : PolygonOps.Union(basis.Concat(extra));
        }

        // Fim da curva de cada lado da esquina: os bordos retos dos dois ramos se encontram em V; a curva de raio r é tangente a
        // eles a r·cot(θ/2) de V.
        void NoteTangent(IntersectionLeg la, IntersectionLeg lb, double span, double r)
        {
            if (span >= 179 || r <= 0.01) return;
            const double far = 30;
            var a0 = L.At(la, far, L.HiEdge(la, far));
            var a1 = L.At(la, far - 1, L.HiEdge(la, far - 1));
            var b0 = L.At(lb, far, -L.LoEdge(lb, far));
            var b1 = L.At(lb, far - 1, -L.LoEdge(lb, far - 1));
            var ua = (a0 - a1).Normalized();
            var ub = (b0 - b1).Normalized();
            var den = ua.X * ub.Y - ua.Y * ub.X;
            if (Math.Abs(den) < 1e-6) return;
            var w0 = b0 - a0;
            var V = a0 + ua * ((w0.X * ub.Y - w0.Y * ub.X) / den);
            var T = r / Math.Tan(span * Math.PI / 360);
            void Keep(IntersectionLeg leg, Vec2 p)
            {
                var t = (p - node).Dot(leg.Dir);
                var key = (leg.Road, leg.Sign);
                L.TangentT[key] = L.TangentT.TryGetValue(key, out var old) ? Math.Max(old, t) : t;
            }
            Keep(la, V + ua * T);
            Keep(lb, V + ub * T);
        }

        // 1ª passada: esquinas simples → ramos, via principal e tratamentos dos ramos.
        var u0 = PolygonOps.Union(cn.SelectMany(x => x));
        var pav0 = PolygonOps.Intersect(Close(u0, rMin), clip);
        var legs0 = ComputeLegs(L, pav0, cn, reach, false);
        var pav1 = Corners(u0, legs0);
        if (PolygonOps.TotalArea(PolygonOps.Difference(pav0, pav1)) + PolygonOps.TotalArea(PolygonOps.Difference(pav1, pav0)) > 0.05)
        {
            pav0 = pav1;
            legs0 = ComputeLegs(L, pav0, cn, reach, false);
        }
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
        var pavS = Corners(U, legs0, warn: true);
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
        // Canteiros LATERAIS (entre a pista e a faixa de estacionamento/marginal) terminam atrás da travessia: senão entram
        // no cruzamento e fecham a conversão de quem vem pela faixa externa.
        var latCuts = L.Roads.Select(_ => new List<Polygon2>()).ToList();
        var medT = L.Roads.Select((_, i) =>
        {
            if (medBands[i].Count == 0) return new List<Polygon2>();
            // Canteiro contínuo (opção): não abre para as vias sem canteiro – só conversões à direita.
            var others = PolygonOps.Offset(own.Where((_, j) => j != i && (!d.MedianContinuous || medBands[j].Count > 0)).SelectMany(x => x), 1.0);
            var band = PolygonOps.Difference(PolygonOps.Difference(medBands[i], others), pockets[i]);
            var r = L.Roads[i];
            var lateral = r.Def.Gaps.Where(g => g.Median && Math.Abs(g.Offset) > g.Width / 2 + 0.1).ToList();
            if (lateral.Count > 0)
            {
                var latBand = PolygonOps.Union(lateral.SelectMany(g => RoadGenerator.Band(r.Axis, g.Offset - g.Width / 2, g.Offset + g.Width / 2)));
                var stopAt = new List<Polygon2>();
                foreach (var leg in legs0.Where(l => l.Road == i))
                {
                    var tEnd = leg.Clear + (CrosswalkOn(d, L, leg) ? TangentGap(L, leg) + SetbackOf(d, L, leg) + CrosswalkWidthOf(d, L, leg) + 0.8 : 1.0);
                    var wide = Math.Max(r.Def.TotalLeft, r.Def.TotalRight) + 2;
                    stopAt.AddRange(L.LegPoly(leg, -maxHalf, tEnd, _ => -wide, _ => wide));
                }
                if (stopAt.Count > 0)
                {
                    // O trecho retirado do canteiro lateral vira pista (refeita pela interseção) e sai da via.
                    var cutLat = PolygonOps.Intersect(latBand, stopAt);
                    latCuts[i].AddRange(cutLat);
                    band = PolygonOps.Difference(band, cutLat);
                }
            }
            var noseR = Math.Min(MaxNoseRadius, r.Def.Gaps.Where(g => g.Median).Select(g => g.Width).DefaultIfEmpty(0).Max() / 2 - 0.01);
            return RoundNoses(Open(band, 0.3).Where(p => p.Area > 0.5).ToList(), others, noseR);
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
        var warn0 = L.Warnings.Count;
        FinishRampPlans(d, L, pav);
        if (EarCorners(d, L).Any() || StraightEarSides(d, L).Any())
        {
            // Orelhas nas esquinas: planejadas com folga, as rampas refeitas na borda delas (a travessia fica sobre a orelha)
            // e o plano final com a posição definitiva das travessias.
            PlanEars(d, L, pav, true);
            if (L.Ears.Count > 0)
            {
                L.Warnings.RemoveRange(warn0, L.Warnings.Count - warn0);
                L.RampPlans.Clear();
                FinishRampPlans(d, L, PolygonOps.Difference(pav, L.Ears.Select(e => e.Footprint)));
                PlanEars(d, L, pav, false);
            }
        }

        // Zona refeita pela interseção: cada ramo até o fim da curva da esquina (e até sair das calçadas das outras
        // vias) + os cantos entre ramos vizinhos. Assim a curva da esquina, o meio-fio e a calçada que a contorna
        // ficam inteiros dentro da zona (a zona circular cortava o asfalto das curvas).
        var corridors = L.Roads.Select(r => RoadGenerator.Band(r.Axis, -r.Def.TotalRight, r.Def.TotalLeft)).ToList();
        var reachT = new Dictionary<IntersectionLeg, double>();
        foreach (var leg in L.Legs)
        {
            var r = L.Roads[leg.Road].Def;
            var others = corridors.Where((_, j) => j != leg.Road).SelectMany(x => x).ToList();
            var max = MaxT(L, leg);
            var t = leg.Clear;
            while (t < max - 0.5)
            {
                var a = L.At(leg, t, -(leg.Sign > 0 ? r.TotalRight : r.TotalLeft));
                var b = L.At(leg, t, leg.Sign > 0 ? r.TotalLeft : r.TotalRight);
                var hit = others.Any(o => DetailGenerator.SegmentIntervals(o, a, b).Sum(iv => iv.T1 - iv.T0) * a.DistanceTo(b) > 0.02);
                if (!hit) break;
                t += 0.25;
            }
            reachT[leg] = Math.Min(max, t + 0.3);
        }
        double HiTot(IntersectionLeg leg) => (leg.Sign > 0 ? L.Roads[leg.Road].Def.TotalLeft : L.Roads[leg.Road].Def.TotalRight) + 0.2;
        double LoTot(IntersectionLeg leg) => (leg.Sign > 0 ? L.Roads[leg.Road].Def.TotalRight : L.Roads[leg.Road].Def.TotalLeft) + 0.2;
        var byAngle = L.Legs.Select(l => (Leg: l, A: AngleOf(l.Dir))).OrderBy(x => x.A).ToList();
        // Lados de ramo sem esquina (via que segue reta do outro lado do nó): a calçada e o meio-fio da própria via
        // continuam; a zona vai só até o bordo da pista.
        var hiCorner = new HashSet<IntersectionLeg>();
        var loCorner = new HashSet<IntersectionLeg>();
        for (int k = 0; k < byAngle.Count && byAngle.Count > 1; k++)
        {
            var next = byAngle[(k + 1) % byAngle.Count];
            if (Straight(byAngle[k].Leg, next.Leg, Ccw(byAngle[k].A, next.A))) continue;
            hiCorner.Add(byAngle[k].Leg);
            loCorner.Add(next.Leg);
        }
        var zoneParts = new List<Polygon2>();
        foreach (var leg in L.Legs)
            zoneParts.AddRange(L.LegPoly(leg, 0, reachT[leg],
                t => loCorner.Contains(leg) ? -LoTot(leg) : -L.LoEdge(leg, t),
                t => hiCorner.Contains(leg) ? HiTot(leg) : L.HiEdge(leg, t), 0.5));
        var cornerZones = new List<(Polygon2 Tri, IntersectionLeg A, IntersectionLeg B)>();
        for (int k = 0; k < byAngle.Count && byAngle.Count > 1; k++)
        {
            var (la, aa) = byAngle[k];
            var (lb, ab) = byAngle[(k + 1) % byAngle.Count];
            if (Straight(la, lb, Ccw(aa, ab))) continue;
            var pa = L.At(la, reachT[la], HiTot(la));
            var pb = L.At(lb, reachT[lb], -LoTot(lb));
            var tri = new Polygon2(new[] { node, pa, pb });
            if (tri.Area < 0.01) continue;
            // Canto da quadra (encontro dos alinhamentos das duas vias) além da corda PA–PB (esquina obtusa, ramo que começa
            // no nó): a zona vai até ele – senão sobrava um vão sem calçada nem meio-fio atrás da ponta do ramo.
            var da = (pa - L.At(la, Math.Max(0, reachT[la] - 1), HiTot(la))).Normalized();
            var db = (pb - L.At(lb, Math.Max(0, reachT[lb] - 1), -LoTot(lb))).Normalized();
            var den = da.X * db.Y - da.Y * db.X;
            if (Math.Abs(den) > 0.05)
            {
                var w0 = pb - pa;
                var sA = (w0.X * db.Y - w0.Y * db.X) / den;
                var X = pa + da * sA;
                var chord = pb - pa;
                double Side(Vec2 q) => chord.X * (q.Y - pa.Y) - chord.Y * (q.X - pa.X);
                // Só quando o canto não fica mais longe do nó que PA ou PB (ramos quase paralelos dariam um canto dezenas de
                // metros adiante).
                if (Side(X) * Side(node) < 0 && X.DistanceTo(node) <= Math.Max(pa.DistanceTo(node), pb.DistanceTo(node)))
                    tri = new Polygon2(new[] { node, pa, X, pb });
            }
            var fixedTri = PolygonOps.Union(new[] { tri });
            zoneParts.AddRange(fixedTri);
            foreach (var ft in fixedTri)
            {
                cornerZones.Add((ft, la, lb));
                L.Corners.Add(new IntersectionCorner(ft, la.Road, la.Sign > 0, lb.Road, lb.Sign < 0,
                    L.At(la, reachT[la], HiTot(la)), L.At(lb, reachT[lb], -LoTot(lb))));
            }
        }
        if (zoneParts.Count == 0) zoneParts.Add(Circle(node, maxHalf + rc));
        var zoneU = PolygonOps.Union(zoneParts);
        // Lados sem esquina (via que segue reta): a zona não passa do bordo da pista. A ponta do ramo oblíquo (corte
        // transversal em t = 0) atravessava a outra via e entalhava o meio-fio e a calçada do lado reto em "V".
        var zoneTrimmed = false;
        var keepOut = new List<Polygon2>();
        var keepOutOf = L.Roads.Select(_ => new List<Polygon2>()).ToList();
        foreach (var leg in L.Legs)
        {
            var ko = new List<Polygon2>();
            if (!loCorner.Contains(leg)) ko.AddRange(L.LegPoly(leg, 0, reachT[leg], _ => -LoTot(leg) - maxHalf, t => -L.LoEdge(leg, t), 0.5));
            if (!hiCorner.Contains(leg)) ko.AddRange(L.LegPoly(leg, 0, reachT[leg], t => L.HiEdge(leg, t), _ => HiTot(leg) + maxHalf, 0.5));
            keepOut.AddRange(ko);
            keepOutOf[leg.Road].AddRange(ko);
        }
        if (keepOut.Count > 0 && byAngle.Count > 2)
        {
            var trimmed = PolygonOps.Difference(zoneU, keepOut).Where(p => p.Area > 0.05).ToList();
            if (trimmed.Count > 0)
            {
                zoneU = trimmed;
                zoneTrimmed = true;
                L.StraightSides.AddRange(PolygonOps.Union(keepOut));
                // A ponta da pista do ramo oblíquo também não passa do bordo reto da outra via.
                pav = PolygonOps.Difference(pav, keepOut).Where(p => p.Area > 0.05).ToList();
            }
        }
        L.Zone = zoneU.OrderByDescending(p => p.Area).First();
        L.Radius = zoneU.SelectMany(p => p.Outer).Max(v => v.DistanceTo(node));
        var core0 = zoneU;
        var zone = zoneU;

        // Travessias cortam canteiros e ilhas (refúgio no nível da pista, NBR 9050).
        var obstacles = PolygonOps.Union(medT.SelectMany(x => x).Concat(gotas).Concat(islands));
        L.Obstacles.AddRange(obstacles);
        var refuges = L.Roads.Select(_ => new List<Polygon2>()).ToList();
        {
            var bands = new List<Polygon2>();
            foreach (var leg in L.Legs.Where(l => CrosswalkOn(d, L, l)))
            {
                var tc = CrosswalkT(d, L, leg);
                var hw = CrosswalkWidthOf(d, L, leg) / 2 - 0.1;
                var band = L.LegPoly(leg, tc - hw, tc + hw, t => -L.LoEdge(leg, t), t => L.HiEdge(leg, t));
                // Canteiro largo com rampas e patamar: a faixa não corta o canteiro (as rampas sobem nas duas faces).
                var passes = MedianPassesOf(d, L, leg);
                foreach (var m in passes)
                    foreach (var msg in MedianCrossing.Warnings(m.Width, m.Template, d.MedianCrossing))
                        if (!L.Warnings.Contains($"Travessia sobre o canteiro: {msg}.")) L.Warnings.Add($"Travessia sobre o canteiro: {msg}.");
                var kept = passes.Where(m => m.Mode == TipoTravessiaCanteiro.Rampas).ToList();
                if (kept.Count > 0)
                {
                    // As rampas e as abas precisam ficar inteiras sobre o canteiro: junto à ponta (esquina aguda) a travessia
                    // fica rebaixada no nível da pista.
                    var he = RampHalfExtent(kept[0].Template) + 0.1;
                    if (kept.Any(m => PolygonOps.Difference(L.LegPoly(leg, tc - he, tc + he, _ => m.Lo + 0.02, _ => m.Hi - 0.02), medT[leg.Road]).Sum(p => p.Area) > 0.02))
                    {
                        L.MedianRampsBlocked.Add((leg.Road, leg.Sign));
                        const string near = "Travessia sobre o canteiro rebaixada no nível da pista: perto da ponta do canteiro as rampas não cabem inteiras.";
                        if (!L.Warnings.Contains(near)) L.Warnings.Add(near);
                        kept.Clear();
                    }
                }
                var rampReach = kept.Count > 0 ? RampHalfExtent(kept[0].Template) + 0.3 : 0;
                foreach (var m in kept)
                    band = PolygonOps.Difference(band, L.LegPoly(leg, tc - rampReach - 1, tc + rampReach + 1, _ => m.Lo, _ => m.Hi));
                bands.AddRange(band);
                // Refúgio: o canteiro central da própria via é cortado no nível da pista na travessia (também fora do nó). O corte
                // da via começa 0,30 m antes, do lado do nó: com a faixa logo além do limite da zona sobrava uma tira de 5 cm do
                // canteiro da via (lascas de meio-fio < 0,01 m²); o canteiro da interseção cresce até o refúgio.
                if (medBands[leg.Road].Count > 0)
                {
                    // Com a faixa no fim da curva o refúgio pode ficar até ~1 m além do fim da zona: o corte vai até a zona.
                    var t0 = tc - Math.Max(hw, rampReach) - 0.3;
                    if (reachT.TryGetValue(leg, out var rz) && t0 > rz - 0.1 && t0 - rz < 1.0) t0 = rz - 0.1;
                    // Com rampas o canteiro sob as rampas e as abas é refeito pela interseção (recortado pelas rampas).
                    refuges[leg.Road].AddRange(PolygonOps.Intersect(L.LegPoly(leg, t0, tc + Math.Max(hw, rampReach), t => -L.LoEdge(leg, t), t => L.HiEdge(leg, t)),
                        medBands[leg.Road]).Where(p => p.Area > 0.2));
                }
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

        // Áreas refeitas pela interseção: zona do nó + trechos tratados (gota, bolsão).
        var ext = L.Roads.Select(_ => new List<Polygon2>()).ToList();
        foreach (var f in L.Features)
            ext[f.Leg.Road].AddRange(L.LegPoly(f.Leg, 0, f.TEnd + 0.5, _ => f.ExtLo, _ => f.ExtHi, 1.0));
        for (int i = 0; i < L.Roads.Count; i++) ext[i].AddRange(refuges[i]);
        // Rampa que começa logo depois do fim da zona (travessia no fim da curva): a parte refeita entra 0,2 m sob a rampa –
        // senão sobrava uma lasca da grama/calçada da via entre a zona e a aba.
        foreach (var leg in L.Legs)
        {
            if (!CrosswalkOn(d, L, leg) || RampPlanOf(d, L, leg) is not { } rpl || rpl.Hi == null && rpl.Lo == null) continue;
            var near = CrosswalkT(d, L, leg) - new[] { rpl.Hi, rpl.Lo }.Where(x => x != null).Max(x => RampHalfExtent(x!));
            // Só lasca (< 0,20 m entre a zona e a aba); uma faixa maior fica com a via.
            if (near > reachT[leg] - 0.2 && near - reachT[leg] < 0.2)
                ext[leg.Road].AddRange(L.LegPoly(leg, reachT[leg] - 0.1, near + 0.2, _ => -LoTot(leg), _ => HiTot(leg), 0.5));
        }
        for (int i = 0; i < L.Roads.Count; i++) ext[i].AddRange(latCuts[i].Select(p => PolygonOps.Offset(new[] { p }, 0.05)).SelectMany(x => x));
        // A ponta (calçada, meio-fio, tampa) de um ramo oblíquo também não entra no lado reto das outras vias.
        for (int i = 0; i < L.Roads.Count; i++)
            L.PhysicalCuts[i] = PolygonOps.Union(core0.Concat(ext[i]).Concat(Stubs(L, i))
                .Concat(zoneTrimmed ? keepOutOf.Where((_, j) => j != i).SelectMany(x => x) : Enumerable.Empty<Polygon2>()));
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
        // Calçada contornando a curva da esquina: faixa paralela ao meio-fio no canto entre ramos vizinhos (com raio maior
        // que a largura da calçada, as calçadas retas das duas vias não chegam à curva).
        foreach (var (tri, la, lb) in cornerZones)
        {
            var ra = L.Roads[la.Road].Def;
            var rb = L.Roads[lb.Road].Def;
            var swA = la.Sign > 0 ? ra.LeftSidewalk : ra.RightSidewalk;
            var swB = lb.Sign > 0 ? rb.RightSidewalk : rb.LeftSidewalk;
            // Com calçadas de larguras diferentes (ou o meio-fio da interseção mais para dentro que o da via), o arco da curva
            // passava do alinhamento da via mais estreita e entrava no lote – o corte da zona deixava um dente: sai a parte
            // além dos DOIS alinhamentos (o lote da quadra).
            // Alinhamento real (HiTot/LoTot têm 0,2 m de folga para a zona cobrir os elementos da via). Esquina aguda (< 60°):
            // a ponta da quadra fica longe, no fim das faixas – o corte ali deixava cunhas soltas; fica como era.
            var acute = Ccw(AngleOf(la.Dir), AngleOf(lb.Dir)) < 60;
            // Semiplanos além de cada alinhamento (retas pelo alinhamento no fim do ramo; o lote pode ficar antes do nó
            // na direção de um dos ramos).
            Polygon2 Beyond(IntersectionLeg leg, double off)
            {
                var p0 = L.At(leg, reachT[leg], off);
                var p1 = L.At(leg, Math.Max(0, reachT[leg] - 1), off);
                var dir = (p0 - p1).Normalized();
                var outward = (L.At(leg, reachT[leg], off + Math.Sign(off)) - p0).Normalized();
                return new Polygon2(new[] { p0 - dir * 200, p0 + dir * 200, p0 + dir * 200 + outward * 200, p0 - dir * 200 + outward * 200 });
            }
            var lot = acute ? new List<Polygon2>() : PolygonOps.Intersect(new[] { Beyond(la, HiTot(la) - 0.2) }, new[] { Beyond(lb, -(LoTot(lb) - 0.2)) });
            List<Polygon2> NoLot(List<Polygon2> a) => lot.Count > 0 ? PolygonOps.Difference(a, lot).Where(p => p.Area > 0.01).ToList() : a;
            if (swA > 0.01 || swB > 0.01)
                L.SidewalkMax.AddRange(NoLot(PolygonOps.Difference(PolygonOps.Intersect(PolygonOps.Offset(pav, Math.Max(swA, swB), true), new[] { tri }), pav)));
            if (swA <= 0.01 || swB <= 0.01) continue;
            sideParts.AddRange(NoLot(PolygonOps.Intersect(PolygonOps.Offset(pav, Math.Min(swA, swB), true), new[] { tri })));
        }
        var sides = PolygonOps.Difference(PolygonOps.Union(sideParts), pav);
        var curbRing = PolygonOps.Difference(PolygonOps.Offset(pav, cw, true), pav);
        L.Curb.AddRange(PolygonOps.Intersect(curbRing, sides));
        L.Sidewalk.AddRange(PolygonOps.Difference(sides, L.Curb).Where(p => p.Area > 0.05));
        if (d.MatchRoadSection && Automation.SectionMatch.From(L.Roads.Select(r => r.Def), cw) is { } sec)
        {
            // Composição das vias: faixa de serviço (gramada) entre meio-fio e passeio e sarjeta junto ao meio-fio.
            if (sec.HasService && sec.Grass) L.SidewalkService.AddRange(Automation.SectionMatch.ServiceBand(pav, L.Sidewalk, cw, sec.Service));
            if (sec.HasGutter) L.Gutter.AddRange(Automation.SectionMatch.GutterBand(pav, L.Curb, sec.Gutter));
        }

        // Canteiro de cada via numa interseção só (zona + trechos tratados dela): sem emenda entre as duas partes – a junção
        // deixava uma agulha de área nula no meio-fio do nariz arredondado.
        var medParts = new List<Polygon2>();
        for (int i = 0; i < L.Roads.Count; i++)
            if (medT[i].Count > 0) medParts.AddRange(PolygonOps.Intersect(medT[i], ext[i].Count > 0 ? PolygonOps.Union(core0.Concat(ext[i])) : core0));
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
        for (int i = 0; i < L.Roads.Count; i++)
        {
            var r = L.Roads[i];
            var paint = new List<Polygon2>();
            var parking = new List<Polygon2>();
            // Via preferencial que atravessa o nó (sem semáforo): as linhas dela seguem contínuas; só o bordo é
            // interrompido na boca das outras vias (e as vagas a 5 m dela).
            var through = i == L.Main && L.Legs.Count >= 3 && L.Legs.Count(l => l.Road == i) >= 2
                          && d.Control != ControleIntersecao.Semaforo && !L.Features.Any(f => f.Leg.Road == i)
                          // Ramo da principal com controle próprio (PARE em todas, semáforo): as linhas param na retenção.
                          && L.Legs.Where(l => l.Road == i).All(l => LegSet(d, L, l)?.Control is null or ControleIntersecao.Nenhum);
            if (through)
            {
                L.ThroughMain = true;
                var mouth = PolygonOps.Difference(PolygonOps.Intersect(pav, core0), own[i]);
                // Entra na pista o bastante para cortar o bordo (inclusive com acostamento), sem chegar ao eixo.
                var reachIn = Math.Max(0.8, 0.45 * Math.Min(r.Def.RightWidth, r.Def.LeftWidth));
                paint.AddRange(PolygonOps.Offset(mouth, reachIn, true));
                parking.AddRange(PolygonOps.Offset(mouth, 5.0, true));
            }
            else
            {
                paint.AddRange(PolygonOps.Intersect(pav, core0));
                parking.AddRange(paint);
            }
            foreach (var leg in L.Legs.Where(l => l.Road == i))
            {
                if (through && !CrosswalkOn(d, L, leg)) continue;
                var stopFar = L.Legs.Count <= 2 ? 0.3 : StopFar(d, L, leg);
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
            if (zoneTrimmed)
                foreach (var ko in keepOutOf.Where((_, j) => j != i)) { paint.AddRange(ko); parking.AddRange(ko); }
            L.PaintCuts[i] = PolygonOps.Union(paint);
            L.ParkingCuts[i] = PolygonOps.Union(parking);
        }
        return L;
    }

    /// <summary>Recortes a aplicar numa marca do grupo da via <paramref name="road"/>.</summary>
    public static List<Polygon2> CutsFor(MarkingDefinition member, int road, IntersectionLayout L)
    {
        var cuts = BaseCutsFor(member, road, L);
        // Orelhas das esquinas: saem o pavimento, a sarjeta, a pintura e as vagas da via por baixo delas.
        if (L.Ears.Count == 0 || !CutByEars(member)) return cuts;
        return PolygonOps.Union(cuts.Concat(L.Ears.SelectMany(e => EarCut(e, member))));
    }

    private static List<Polygon2> BaseCutsFor(MarkingDefinition member, int road, IntersectionLayout L)
    {
        var phys = L.PhysicalCuts.GetValueOrDefault(road) ?? new List<Polygon2> { L.Zone };
        if (member is IAnnotationDefinition) return new();
        if (member is LinearMarkingDefinition { Code: "SARJETA" } && road < L.Roads.Count)
        {
            // A sarjeta só é refeita pela interseção onde o meio-fio ao lado também é: junto ao meio-fio que a via mantém
            // (ex.: lado oposto de um T) ela continua a da própria via.
            var r = L.Roads[road];
            var carriage = RoadGenerator.Band(r.Axis, -r.Def.RightWidth, r.Def.LeftWidth);
            var outside = PolygonOps.Difference(phys, carriage);
            return outside.Count == 0 ? new() : PolygonOps.Intersect(phys, PolygonOps.Offset(outside, 0.6, true));
        }
        if (member is RoadPavementDefinition || IsPhysical(member)) return phys;
        // Elementos derivados da via (extensões de calçada, piso tátil, rampas e mobiliário das travessias – ids "via:..."):
        // saem só onde a interseção refaz a calçada, não na área de pintura das aproximações.
        if (member is CurbExtensionDefinition or TactileRouteDefinition || member is RampDefinition or UrbanElementDefinition && member.Id.Contains(':'))
            return phys;
        if (member is ParkingMarkingDefinition) return L.ParkingCuts.GetValueOrDefault(road) ?? new();
        if (member is DeviceMarkingDefinition) return PolygonOps.Union(phys.Concat(L.PaintCuts.GetValueOrDefault(road) ?? new()));
        var paint = L.PaintCuts.GetValueOrDefault(road) ?? new();
        // Eixo e divisórias de faixa: miolo da principal (LCO) e aproximações (linha contínua) – MBST Vol. IV.
        if (member is LinearMarkingDefinition lin && road < L.Roads.Count && LineRuleFor(L, road, lin) is { Cut.Count: > 0 } rule)
        {
            var r = L.Roads[road];
            var bands = rule.Cut.SelectMany(c => RoadGenerator.Band(new Polyline2(r.Axis.SubPoints(c.S0, c.S1)), lin.Offset - 0.6, lin.Offset + 0.6));
            return PolygonOps.Union(paint.Concat(bands));
        }
        return paint;
    }

    // ------------------------------------------------------------------ linhas da via no cruzamento (MBST Vol. IV)

    /// <summary>Papel de uma linha longitudinal da via no cruzamento.</summary>
    private enum PapelLinha { Nenhum, Eixo, Faixa }

    /// <summary>Linha que substitui um trecho de uma linha da via (estacas no eixo da via).</summary>
    public sealed record LineReplacement(double S0, double S1, string Code, MarkingColor? Color, bool Double, string Why);

    /// <summary>Regra de uma linha da via no cruzamento: trechos recortados e as linhas que os substituem.</summary>
    public sealed record LineRule(List<(double S0, double S1)> Cut, List<LineReplacement> Add);

    private static PapelLinha RoleOf(LinearMarkingDefinition m, IntersectionRoad r)
    {
        var code = m.Code ?? "";
        if (m.PathRef == null || RoadSectionInference.IsPhysical(code) || NotShifted.Any(c => code.StartsWith(c)) || code.StartsWith("LCO")) return PapelLinha.Nenhum;
        if (RoadSectionInference.PathKey(m.PathRef) != RoadSectionInference.PathKey(r.Def.Path)) return PapelLinha.Nenhum;
        // Só as linhas internas da pista (o bordo tem a LCO própria na boca das secundárias).
        var edge = m.Offset >= 0 ? r.Def.LeftWidth : r.Def.RightWidth;
        if (Math.Abs(m.Offset) > edge - 0.6) return PapelLinha.Nenhum;
        if (code.StartsWith("LFO")) return r.Def.TwoWay ? PapelLinha.Eixo : PapelLinha.Faixa;
        if (code.StartsWith("LMS") || code.StartsWith("MFE") || code.StartsWith("MFP")) return PapelLinha.Faixa;
        return PapelLinha.Nenhum;
    }

    /// <summary>Velocidade da via (km/h) para as cadências (LCO 1 × 1 m até 60 km/h, 2 × 2 m acima).</summary>
    private static double SpeedOf(IntersectionRoad r) => Hierarquia.DefaultSpeed(r.Def.Hierarchy ?? HierarquiaViaria.Local);

    /// <summary>Estaca do fim do trecho livre de um ramo, limitada à metade da distância ao nó vizinho mais próximo.</summary>
    private static double LegLimit(IntersectionLayout L, IntersectionLeg leg, IReadOnlyList<Vec2> neighbors)
    {
        var axis = L.Roads[leg.Road].Axis;
        var lim = MaxT(L, leg) - 2;
        foreach (var n in neighbors)
        {
            var (st, dist, _) = Project(axis, n);
            if (dist > 6) continue;
            var t = (st - leg.NodeStation) * leg.Sign;
            if (t > 1) lim = Math.Min(lim, t / 2 - 1);
        }
        return lim;
    }

    /// <summary>
    /// Regra MBST Vol. IV / CTB art. 207 de uma linha da via <paramref name="road"/> no cruzamento:
    /// <list type="bullet">
    /// <item>via principal que atravessa o nó: o eixo amarelo vira LCO (dupla se a linha é dupla) na boca das secundárias,
    /// permitindo as conversões à esquerda; com conversões proibidas o eixo segue contínuo; divisórias de faixa viram LCO
    /// branca;</item>
    /// <item>aproximações: eixo seccionado passa a contínuo (LFO-3; LFO-1 em pista estreita) por 15 m (30 m acima de
    /// 60 km/h) antes do cruzamento; no semáforo as divisórias são LMS-1 nos 20 m antes da retenção.</item>
    /// </list>
    /// </summary>
    public static LineRule? LineRuleFor(IntersectionLayout L, int road, LinearMarkingDefinition m, IntersectionDefinition? d = null)
    {
        d ??= L.Definition;
        if (d == null || L.IsBend || L.Legs.Count <= 2 || road >= L.Roads.Count) return null;
        var r = L.Roads[road];
        var role = RoleOf(m, r);
        if (role == PapelLinha.Nenhum) return null;
        var code = m.Code ?? "";
        var cut = new List<(double, double)>();
        var add = new List<LineReplacement>();
        var legs = L.Legs.Where(l => l.Road == road).ToList();
        var dashed = code is "LFO-2" or "LFO-4";
        var dbl = code is "LFO-3" or "LFO-4";
        var wide = r.Def.LeftWidth + r.Def.RightWidth >= 7.0;
        var through = L.ThroughMain && road == L.Main && legs.Count >= 2;

        // 1. Miolo da principal que atravessa o nó.
        if (through)
        {
            var box = legs.Select(l => l.StationAt(l.Clear + (CrosswalkOn(d, L, l) ? Math.Max(0, TangentGap(L, l) + SetbackOf(d, L, l) - 0.4) : 0))).ToList();
            var (s0, s1) = (box.Min(), box.Max());
            if (s1 - s0 > 1)
            {
                if (role == PapelLinha.Eixo)
                {
                    if (d.LeftTurns)
                    {
                        cut.Add((s0, s1));
                        if (d.BoxContinuity) add.Add(new LineReplacement(s0, s1, "LCO", MarkingColor.Amarela, dbl || d.ApproachLines && dashed,
                            "conversões à esquerda permitidas: eixo tracejado (LCO) na boca das transversais"));
                    }
                    else if (dashed || code == "LFO-2")
                    {
                        cut.Add((s0, s1));
                        add.Add(new LineReplacement(s0, s1, wide ? "LFO-3" : "LFO-1", null, false, "conversões à esquerda proibidas: eixo contínuo"));
                    }
                }
                else
                {
                    cut.Add((s0, s1));
                    if (d.BoxContinuity) add.Add(new LineReplacement(s0, s1, "LCO", null, false, "continuidade das faixas no cruzamento"));
                }
            }
        }

        // 2. Aproximações.
        if (d.ApproachLines)
            foreach (var leg in legs)
            {
                if (L.FeatureOf(leg) != null) continue;                       // gota/bolsão têm linhas próprias
                var t0 = through && !CrosswalkOn(d, L, leg) ? leg.Clear : leg.Clear + StopFar(d, L, leg);
                var lim = LegLimit(L, leg, d.NeighborNodes);
                if (role == PapelLinha.Eixo && dashed)
                {
                    var t1 = Math.Min(t0 + (SpeedOf(r) > 60 ? 30 : 15), lim);
                    if (t1 - t0 < 5) continue;
                    var (a, b) = (leg.StationAt(t0), leg.StationAt(t1));
                    cut.Add((Math.Min(a, b), Math.Max(a, b)));
                    add.Add(new LineReplacement(Math.Min(a, b), Math.Max(a, b), wide ? "LFO-3" : "LFO-1", null, false, "aproximação do cruzamento: ultrapassagem proibida"));
                }
                else if (role == PapelLinha.Faixa && d.Control == ControleIntersecao.Semaforo && code.StartsWith("LMS-2"))
                {
                    // Só as divisórias do lado de chegada (+o do ramo; mão única: o ramo por onde o tráfego chega).
                    var inbound = r.Def.TwoWay ? m.Offset * leg.Sign > 0.3 : leg.Sign < 0;
                    if (!inbound) continue;
                    var t1 = Math.Min(t0 + 20, lim);
                    if (t1 - t0 < 5) continue;
                    var (a, b) = (leg.StationAt(t0), leg.StationAt(t1));
                    cut.Add((Math.Min(a, b), Math.Max(a, b)));
                    add.Add(new LineReplacement(Math.Min(a, b), Math.Max(a, b), "LMS-1", null, false, "aproximação do semáforo: troca de faixa proibida"));
                }
            }
        return cut.Count == 0 && add.Count == 0 ? null : new LineRule(cut, add);
    }

    /// <summary>Linhas que substituem os trechos recortados das linhas das vias (LCO no miolo, contínuas nas aproximações).</summary>
    private static IEnumerable<LinearMarkingDefinition> RuleLines(IntersectionDefinition d, IntersectionLayout L, double z,
        IReadOnlyList<IReadOnlyCollection<MarkingDefinition>> roadMembers)
    {
        for (int k = 0; k < L.Roads.Count && k < roadMembers.Count; k++)
        {
            var r = L.Roads[k];
            var speed = SpeedOf(r);
            var seen = new HashSet<string>();
            foreach (var m in roadMembers[k].OfType<LinearMarkingDefinition>())
            {
                if (LineRuleFor(L, k, m, d) is not { } rule) continue;
                foreach (var a in rule.Add)
                {
                    // A mesma linha (mesmo código e deslocamento) só uma vez.
                    if (!seen.Add($"{a.Code}|{a.Color}|{Math.Round(m.Offset, 2)}|{Math.Round(a.S0, 1)}")) continue;
                    var line = new Polyline2(r.Axis.SubPoints(a.S0, a.S1));
                    if (line.Length < 1) continue;
                    var pts = (Math.Abs(m.Offset) > 1e-6 ? line.Offset(m.Offset) : line).Points.ToList();
                    if (a.Double)
                    {
                        var (w, g) = speed > 80 ? (0.15, 0.15) : (0.10, 0.10);
                        foreach (var sg in new[] { 1.0, -1.0 })
                            yield return new LinearMarkingDefinition
                            {
                                Code = a.Code, Speed = speed, ColorOverride = a.Color, WidthOverride = w, Offset = sg * g,
                                PathRef = PathReference.FromPoints(pts, z),
                            };
                    }
                    else
                        yield return new LinearMarkingDefinition
                        {
                            Code = a.Code, Speed = speed, ColorOverride = a.Color,
                            WidthOverride = a.Code == "LCO" ? (speed > 80 ? 0.15 : 0.10) : null,
                            PathRef = PathReference.FromPoints(pts, z),
                        };
                }
            }
        }
    }

    // ------------------------------------------------------------------ geometria da interseção

    public static MarkingGeometry Build(IntersectionDefinition d, BuildContext ctx)
    {
        var roads = ResolveRoads(d, ctx);
        var L = Layout(d, roads);
        var geo = new MarkingGeometry();
        geo.Warnings.AddRange(L.Warnings);
        if (L.Pavement.Count == 0) return geo;
        // Orelhas das esquinas (marcas filhas): a pista e a sarjeta da interseção saem de baixo delas.
        var earFp = L.Ears.SelectMany(e => e.CutZone.Count > 0 ? e.CutZone : new List<Polygon2> { e.Footprint }).ToList();
        List<Polygon2> NoEars(List<Polygon2> a) => earFp.Count == 0 ? a : PolygonOps.Difference(a, earFp).Where(p => p.Area >= 0.01).ToList();
        void Raised(IEnumerable<Polygon2> shapes, MarkingColor c, double h, double elev = 0)
        {
            foreach (var s in shapes)
            {
                var ss = s.Simplified();
                if (ss != null && ss.Area > 1e-4) geo.Pieces.Add(new MarkingPiece(ss, c) { Thickness = h, Elevation = elev });
            }
        }
        // Rebaixamentos das travessias recortam as calçadas refeitas pela interseção (retângulo da rampa com as abas: a
        // grama e o meio-fio saem inteiros, sem lascas).
        var rampBodies = new List<Polygon2>();
        if (L.Legs.Any(l => RampsOn(d, L, l)))
        {
            var ramps = Children(d, L, new OutputSettings(), 0).OfType<RampDefinition>()
                .Select(r => RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points))).ToList();
            // Corpo das rampas (da face do meio-fio para dentro): nada do perfil de calçada da esquina fica por baixo.
            rampBodies = PolygonOps.Difference(ramps, L.Pavement);
            if (ramps.Count > 0)
            {
                static List<Polygon2> Cut(List<Polygon2> a, List<Polygon2> b, double min) => PolygonOps.Difference(a, b).Where(p => p.Area >= min).ToList();
                var sw = Cut(L.Sidewalk, ramps, 0.01);
                var cb = Cut(L.Curb, ramps, 0.01);
                var sv = Cut(L.SidewalkService, ramps, 0.01);
                var sm = Cut(L.SidewalkMax, ramps, 0.01);
                // Rampas do canteiro central (travessia com patamar): o meio-fio e a grama do canteiro saem sob elas.
                var mc = Cut(L.MedianCurb, ramps, 0.01);
                var mo = Cut(L.MedianCore, ramps, 0.01);
                L.MedianCurb.Clear(); L.MedianCurb.AddRange(mc);
                L.MedianCore.Clear(); L.MedianCore.AddRange(mo);
                L.Sidewalk.Clear(); L.Sidewalk.AddRange(sw);
                L.Curb.Clear(); L.Curb.AddRange(cb);
                L.SidewalkService.Clear(); L.SidewalkService.AddRange(sv);
                L.SidewalkMax.Clear(); L.SidewalkMax.AddRange(sm);
            }
        }
        var profile = d.MatchRoadSection ? d.EdgeProfile : new List<Automation.EdgeBand>();
        var perRoad = d.MatchRoadSection ? d.RoadProfiles.Where(p => p.Left.Count + p.Right.Count > 0).ToList() : new List<Automation.RoadEdgeProfile>();
        if ((profile.Count > 0 || perRoad.Count > 0) && (L.Curb.Count > 0 || L.Sidewalk.Count > 0 || L.SidewalkMax.Count > 0))
        {
            // Mesmos elementos das vias (meio-fio, sarjeta, faixa gramada, passeio) em volta da pista da interseção: o perfil
            // de cada via do lado dela e, nas esquinas entre vias diferentes, interpolado ao longo da curva.
            var region = PolygonOps.Union(L.Curb.Concat(L.Sidewalk));
            var (pieces, inside) = perRoad.Count > 0
                ? ApplyPerRoad(L, d.RoadProfiles, profile, region)
                : Automation.EdgeProfile.Apply(L.Pavement, region, profile, new[] { L.Zone });
            if (rampBodies.Count > 0)
                pieces = pieces.SelectMany(p => PolygonOps.Difference(new[] { p.Shape }, rampBodies).Where(x => x.Area >= 0.01).Select(x => p with { Shape = x })).ToList();
            if (earFp.Count > 0)
                pieces = pieces.SelectMany(p => NoEars(new List<Polygon2> { p.Shape }).Select(x => p with { Shape = x })).ToList();
            // Sarjetas que as vias mantêm dentro da zona (junto ao meio-fio delas) também saem do pavimento da interseção.
            inside.AddRange(KeptGutters(L));
            // Sem lascas nem "espinhos" (diferença pista − sarjetas de vias em ângulo): abre/fecha 5 mm e descarta pisos < 0,01 m².
            if (roads.Any(r => r.Def.Material != TipoPavimento.Nenhum))
            {
                var pv = NoEars(inside.Count > 0 ? PolygonOps.Difference(L.Pavement, inside) : L.Pavement);
                var good = SoundPavement(pv);
                // Cunhas tiradas da pista entre a sarjeta e a linha do meio-fio passam para a sarjeta vizinha (sem vão); as
                // que não encostam em sarjeta voltam para a pista (sem furo).
                var slivers = PolygonOps.Difference(pv, good).Where(x => x.Area > 1e-4).ToList();
                pieces = AbsorbSlivers(pieces, slivers, out var left);
                if (left.Count > 0) good = Sound(PolygonOps.Union(good.Concat(left)));
                Raised(good, L.PavementColor, L.PavementThickness, -L.PavementThickness);
            }
            AddPieces(geo, pieces.SelectMany(p => Sound(new[] { p.Shape }).Select(x => p with { Shape = x })));
        }
        else
        {
            if (roads.Any(r => r.Def.Material != TipoPavimento.Nenhum))
                Raised(SoundPavement(NoEars(L.Gutter.Count > 0 ? PolygonOps.Difference(L.Pavement, L.Gutter) : L.Pavement)), L.PavementColor, L.PavementThickness, -L.PavementThickness);
            AddPieces(geo, NoEars(L.Curb).Select(c => new MarkingPiece(c, MarkingColor.Concreto) { Thickness = L.CurbHeight, Layer = "MEIO-FIO" }));
            Raised(NoEars(L.Gutter), MarkingColor.Concreto, 0.005);
            if (L.SidewalkService.Count > 0)
            {
                // Sobras da faixa gramada menores que 0,15 m² (junto à aba da rampa) ficam no passeio.
                var service = L.SidewalkService.Where(p => p.Area >= 0.15).ToList();
                Raised(PolygonOps.Difference(L.Sidewalk, service), MarkingColor.Concreto, L.CurbHeight);
                Raised(service, MarkingColor.Grama, L.CurbHeight);
            }
            else Raised(L.Sidewalk, MarkingColor.Concreto, L.CurbHeight);
        }
        // Miolo de canteiro menor que um piso (ponta estreita do nariz): vira meio-fio junto com a borda, sem lasca de grama.
        var tinyCore = L.MedianCore.Where(p => p.Area < MinFloorArea).ToList();
        Raised(tinyCore.Count > 0 ? PolygonOps.Union(L.MedianCurb.Concat(tinyCore)) : L.MedianCurb, MarkingColor.Concreto, L.CurbHeight);
        Raised(L.MedianCore.Where(p => p.Area >= MinFloorArea), MarkingColor.Grama, L.CurbHeight);
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

    /// <summary>
    /// Perfil de calçada por via: nas esquinas, setores ao longo da curva com o perfil interpolado entre o lado da via A e o
    /// da via B (larguras de meio-fio, grama e passeio mudam aos poucos, sem degraus nem "calçada torta"); nos trechos
    /// retos dentro da zona, o perfil do lado da própria via.
    /// </summary>
    private static (List<MarkingPiece> Pieces, List<Polygon2> Inside) ApplyPerRoad(IntersectionLayout L, List<Automation.RoadEdgeProfile> profiles,
        List<Automation.EdgeBand> fallback, List<Polygon2> region)
    {
        List<Automation.EdgeBand> Side(int road, bool left)
        {
            var p = profiles.FirstOrDefault(x => x.RoadId == L.Roads[road].Def.Id);
            if (p == null) return fallback;
            return p.Side(left);
        }
        var pieces = new List<MarkingPiece>();
        var inside = new List<Polygon2>();
        var cache = new Dictionary<int, List<Polygon2>>();
        var full = PolygonOps.Union(region.Concat(L.SidewalkMax)).Where(p => p.Area > 0.02).ToList();
        // Bordos do pavimento sobre o limite da zona (corte transversal da pista) não são face de meio-fio.
        var zoneInner = PolygonOps.Offset(L.Rebuild.Count > 0 ? L.Rebuild : new List<Polygon2> { L.Zone }, -0.12, true);
        var done = new List<Polygon2>();
        foreach (var c in L.Corners)
        {
            var a = Side(c.RoadA, c.LeftA);
            var b = Side(c.RoadB, c.LeftB);
            // A calçada da esquina inteira: do trecho reto da via A, pela curva, até o trecho reto da via B.
            var comp = full.Where(p => !done.Contains(p))
                .Select(p => (P: p, O: PolygonOps.TotalArea(PolygonOps.Intersect(new[] { p }, new[] { c.Tri }))))
                .Where(x => x.O > 0.01).OrderByDescending(x => x.O).Select(x => x.P).FirstOrDefault();
            if (comp == null) continue;
            done.Add(comp);
            if (a.Count == 0 && b.Count == 0) continue;
            var chain = CurbChain(L.Pavement, comp, c.Tri, c.PA, zoneInner);
            if (chain == null) continue;
            var (pts, nrm, wts) = chain.Value;
            var aligned = Automation.EdgeProfile.Aligned(a, b);
            // Exatamente a calçada da esquina: 2 cm a mais sobrepunham a calçada da via no limite da zona.
            var clip = new[] { comp };
            var covered = new List<Polygon2>();
            foreach (var (shape, attr, ins) in Automation.EdgeProfile.AlongChain(pts, nrm, wts, aligned))
            {
                if (ins)
                {
                    var g = PolygonOps.Intersect(PolygonOps.Intersect(new[] { shape }, PolygonOps.Offset(new[] { comp }, 2.0)), L.Pavement).Where(x => x.Area > 1e-3).ToList();
                    inside.AddRange(g);
                    foreach (var sh in g)
                        pieces.Add(new MarkingPiece(sh, attr.Color) { Thickness = attr.Thickness, Elevation = attr.Elevation, Layer = attr.Code });
                    continue;
                }
                foreach (var sh in PolygonOps.Difference(PolygonOps.Intersect(new[] { shape }, clip), L.Pavement).Where(x => x.Area > 1e-3))
                {
                    pieces.Add(new MarkingPiece(sh, attr.Color) { Thickness = attr.Thickness, Elevation = attr.Elevation, Layer = attr.Code });
                    covered.Add(sh);
                }
            }
            // Sobras nos trechos retos (fora da curva): continuam com a faixa externa da via daquele lado.
            var outerA = a.Where(x => !x.Inside).OrderBy(x => x.D1).LastOrDefault();
            var outerB = b.Where(x => !x.Inside).OrderBy(x => x.D1).LastOrDefault();
            var free = PolygonOps.Difference(new[] { comp }, PolygonOps.Offset(covered, 0.01));
            var triNear = PolygonOps.Offset(new[] { c.Tri }, 0.5);
            var left = PolygonOps.Difference(free, triNear);
            // Atrás das rampas (a rampa corta o meio-fio e a cadeia da curva para nela): a faixa livre que sobra na esquina,
            // longe do bordo, também é passeio – antes ficava um vão entre a rampa e o alinhamento.
            var behind = PolygonOps.Difference(PolygonOps.Difference(PolygonOps.Intersect(new[] { comp }, triNear), covered), PolygonOps.Offset(L.Pavement, 0.6, true));
            left.AddRange(behind);
            foreach (var sh in left.Where(x => x.Area > 0.05))
            {
                var nearA = sh.Centroid.DistanceTo(c.PA) < sh.Centroid.DistanceTo(c.PB);
                var ob = nearA ? outerA ?? outerB : outerB ?? outerA;
                if (ob != null) pieces.Add(new MarkingPiece(sh, ob.Color) { Thickness = ob.Thickness, Elevation = ob.Elevation, Layer = ob.Code });
            }
        }
        var rest = (done.Count > 0 ? PolygonOps.Difference(region, PolygonOps.Union(done)) : region).Where(p => p.Area > 0.02).ToList();
        // Sarjeta das esquinas (vai 2 m além da calçada da esquina): os trechos retos não a repetem por cima.
        var cornerInside = PolygonOps.Union(inside);
        var skip = done.Concat(cornerInside).ToList();
        foreach (var part in rest)
        {
            var (road, left) = NearestSide(L, part.Centroid);
            var bands = road < 0 ? fallback : Side(road, left);
            if (bands.Count == 0) bands = fallback;
            if (bands.Count == 0) continue;
            var (pc, ins) = Automation.EdgeProfile.Apply(L.Pavement, new[] { part }, bands, null, true, cache);
            foreach (var pi in pc)
            {
                var shapes = skip.Count > 0 ? PolygonOps.Difference(new[] { pi.Shape }, skip) : new List<Polygon2> { pi.Shape };
                foreach (var sh in shapes.Where(x => x.Area > 1e-3))
                    pieces.Add(new MarkingPiece(sh, pi.Color) { Thickness = pi.Thickness, Elevation = pi.Elevation, Layer = pi.Layer });
            }
            inside.AddRange((skip.Count > 0 ? PolygonOps.Difference(ins, skip) : ins).Where(x => x.Area > 1e-3));
        }
        // Sobras pequenas da calçada da interseção que nenhuma faixa cobriu (junto ao limite da zona, entre a curva e o trecho
        // reto): passam para a peça vizinha com que mais encostam – antes ficavam vãos de 0,1–0,4 m entre os pisos.
        if (pieces.Count > 0)
        {
            var coveredAll = PolygonOps.Union(pieces.Select(p => p.Shape).Concat(inside));
            var holes = PolygonOps.Difference(PolygonOps.Difference(full, coveredAll), L.Pavement).Where(x => x.Area > 1e-3 && x.Area < 0.5).ToList();
            foreach (var hole in holes)
            {
                var grown = PolygonOps.Offset(new[] { hole }, 0.02);
                var best = pieces.Where(p => p.Elevation >= -1e-6 && p.Layer != "SARJETA")
                    .Select(p => (P: p, A: PolygonOps.TotalArea(PolygonOps.Intersect(new[] { p.Shape }, grown))))
                    .Where(x => x.A > 1e-5).OrderByDescending(x => x.A).Select(x => x.P).FirstOrDefault();
                if (best != null) pieces.Add(best with { Shape = hole });
            }
        }
        // Sobras de faixa gramada menores que 0,15 m² (entre a curva e a aba da rampa, no limite da zona) viram passeio.
        var walkAttr = pieces.FirstOrDefault(p => p.Layer == "CALCADA");
        if (walkAttr != null)
            for (int i = 0; i < pieces.Count; i++)
                if (pieces[i].Layer == "GRAMADO" && pieces[i].Shape.Area < 0.15)
                    pieces[i] = walkAttr with { Shape = pieces[i].Shape };
        // Peças iguais (mesmo material, altura e camada) e encostadas viram uma só: as costuras internas, simplificadas
        // separadamente, abriam vãos de 1–4 cm entre os pisos.
        var merged = pieces.GroupBy(p => (p.Color, Math.Round(p.Thickness, 4), Math.Round(p.Elevation, 4), p.Layer))
            .SelectMany(g => g.Count() == 1 ? g.ToList() : PolygonOps.Union(g.Select(p => p.Shape)).Where(x => x.Area > 1e-3).Select(x => g.First() with { Shape = x }).ToList())
            .ToList();
        return (merged, PolygonOps.Union(inside));
    }

    /// <summary>
    /// Linha do meio-fio da esquina: trecho do contorno do pavimento dentro do triângulo, de A para B, com pontos a cada
    /// ~0,25 m, as normais para fora da pista e o peso (0 em A, 1 em B) pelo comprimento.
    /// </summary>
    private static (List<Vec2> Pts, List<Vec2> Normals, List<double> W)? CurbChain(IReadOnlyList<Polygon2> pavement, Polygon2 comp, Polygon2 tri, Vec2 pa,
        IReadOnlyList<Polygon2> zoneInner)
    {
        // Bordo do pavimento junto a esta calçada (a face do meio-fio fica na borda interna dela), fora do limite da zona.
        var near = PolygonOps.Offset(new[] { comp }, 0.25);
        bool In(Vec2 p) => near.Any(z => z.Contains(p)) && zoneInner.Any(z => z.Contains(p));
        List<Vec2>? best = null;
        var bestLen = 0.0;
        foreach (var pg in pavement)
            foreach (var raw in new[] { pg.Outer }.Concat(pg.Holes))
            {
                var ring = new List<Vec2>();
                for (int i = 0; i < raw.Count; i++)
                {
                    var p0 = raw[i];
                    var p1 = raw[(i + 1) % raw.Count];
                    var k = Math.Max(1, (int)Math.Ceiling(p0.DistanceTo(p1) / 0.25));
                    for (int j = 0; j < k; j++) ring.Add(p0 + (p1 - p0) * (j / (double)k));
                }
                var n = ring.Count;
                var inside = ring.Select(In).ToArray();
                if (inside.All(x => x)) continue;
                var s0 = Array.FindIndex(inside, x => !x);
                var run = new List<Vec2>();
                for (int q = 1; q <= n; q++)
                {
                    var i = (s0 + q) % n;
                    if (inside[i]) { run.Add(ring[i]); continue; }
                    if (run.Count > 1)
                    {
                        var len = Enumerable.Range(1, run.Count - 1).Sum(t => run[t].DistanceTo(run[t - 1]));
                        if (len > bestLen) { bestLen = len; best = run; }
                    }
                    run = new List<Vec2>();
                }
            }
        if (best == null || best.Count < 3) return null;
        // Sem "espinhos" de largura zero no bordo (a curva maior da esquina encontra a reta da via e o contorno vai e volta
        // sobre ela): a cadeia que ia e voltava dobrava as faixas e a calçada da esquina sumia.
        for (var again = true; again && best.Count >= 3;)
        {
            again = false;
            for (int i = 1; i + 1 < best.Count; i++)
            {
                var u0 = best[i] - best[i - 1];
                var u1 = best[i + 1] - best[i];
                if (u0.Length < 1e-6 || u1.Length < 1e-6 || u0.Dot(u1) < -0.5 * u0.Length * u1.Length)
                {
                    best.RemoveAt(i);
                    again = true;
                    break;
                }
            }
        }
        if (best.Count < 3) return null;
        if (best[0].DistanceTo(pa) > best[^1].DistanceTo(pa)) best.Reverse();
        // Prolonga as pontas 1 m na tangente (as faixas chegam inteiras até o limite da zona).
        var e0 = (best[0] - best[1]).Normalized();
        var e1 = (best[^1] - best[^2]).Normalized();
        best.Insert(0, best[0] + e0 * 1.0);
        best.Add(best[^1] + e1 * 1.0);
        var normals = new List<Vec2>();
        for (int i = 0; i < best.Count; i++)
        {
            var t = (best[Math.Min(best.Count - 1, i + 1)] - best[Math.Max(0, i - 1)]).Normalized();
            normals.Add(new Vec2(t.Y, -t.X));
        }
        // Normais para fora do pavimento.
        var mid = best.Count / 2;
        var probe = best[mid] + normals[mid] * 0.1;
        if (pavement.Any(p => p.Contains(probe))) normals = normals.Select(v => v * -1).ToList();
        // Peso: 0 no trecho reto da via A, 1 no da via B; a passagem acontece só na curva (dentro do triângulo da esquina).
        var acc = new List<double> { 0 };
        for (int i = 1; i < best.Count; i++) acc.Add(acc[^1] + best[i].DistanceTo(best[i - 1]));
        var inTri = best.Select(p => tri.Contains(p)).ToArray();
        var f = Array.IndexOf(inTri, true);
        var l = Array.LastIndexOf(inTri, true);
        var w = new List<double>();
        for (int i = 0; i < best.Count; i++)
        {
            if (f < 0 || l <= f) { w.Add(acc[i] / Math.Max(1e-6, acc[^1])); continue; }
            var u = Math.Clamp((acc[i] - acc[f]) / Math.Max(1e-6, acc[l] - acc[f]), 0, 1);
            w.Add(u * u * (3 - 2 * u));
        }
        return (best, normals, w);
    }

    /// <summary>Via e lado cuja calçada (entre a pista e o alinhamento) fica mais perto do ponto.</summary>
    private static (int Road, bool Left) NearestSide(IntersectionLayout L, Vec2 p)
    {
        var best = (-1, false);
        var score = double.MaxValue;
        for (int i = 0; i < L.Roads.Count; i++)
        {
            var r = L.Roads[i];
            var sd = r.Axis.SignedDistance(p);
            var left = sd > 0;
            var w = left ? r.Def.LeftWidth : r.Def.RightWidth;
            var t = left ? r.Def.TotalLeft : r.Def.TotalRight;
            var ad = Math.Abs(sd);
            var sc = ad < w ? w - ad : ad > t + 0.3 ? ad - t : 0;
            if (sc < score) { score = sc; best = (i, left); }
        }
        return best;
    }

    /// <summary>Faixas de sarjeta das vias (vãos do pavimento junto ao meio-fio) que não são recortadas pela interseção.</summary>
    private static List<Polygon2> KeptGutters(IntersectionLayout L)
    {
        var res = new List<Polygon2>();
        for (int i = 0; i < L.Roads.Count; i++)
        {
            var r = L.Roads[i];
            var gutter = new LinearMarkingDefinition { Code = "SARJETA" };
            var cut = CutsFor(gutter, i, L);
            foreach (var g in r.Def.Gaps.Where(g => !g.Median))
            {
                var band = RoadGenerator.Band(r.Axis, g.Offset - g.Width / 2, g.Offset + g.Width / 2);
                res.AddRange(PolygonOps.Intersect(cut.Count > 0 ? PolygonOps.Difference(band, cut) : band, new[] { L.Zone }));
            }
        }
        return res;
    }

    /// <summary>
    /// Lado sem esquina: a MESMA via segue reta do outro lado do nó (a calçada e o meio-fio dela continuam). Duas vias
    /// diferentes emendadas em linha reta (uma começa na ponta da outra) têm esquina reta: os bordos, que podem ter larguras
    /// diferentes, são concordados e o meio-fio e a calçada refeitos com a transição – antes o degrau ficava sem meio-fio.
    /// </summary>
    private static bool Straight(IntersectionLeg a, IntersectionLeg b, double span) => span >= 179 && a.Road == b.Road;

    /// <summary>Menor piso gerado (m²): abaixo disso é lasca de operação booleana.</summary>
    public const double MinFloorArea = 0.01;

    /// <summary>
    /// Pisos sem lascas: abre e fecha 5 mm (some com "espinhos" e fendas finas que o Revit não consegue desenhar) e
    /// descarta pedaços menores que <see cref="MinFloorArea"/>.
    /// </summary>
    internal static List<Polygon2> Sound(IEnumerable<Polygon2> polys) =>
        PolygonOps.Clean(polys, 0.005).Where(p => p.Area >= MinFloorArea).ToList();

    /// <summary>Cada lasca vai para a peça baixa (sarjeta) que encosta nela; sem vizinha, volta a ser pista.</summary>
    private static List<MarkingPiece> AbsorbSlivers(List<MarkingPiece> pieces, List<Polygon2> slivers, out List<Polygon2> left)
    {
        left = new List<Polygon2>();
        if (slivers.Count == 0) return pieces;
        var res = pieces.ToList();
        foreach (var sl in slivers)
        {
            var grown = PolygonOps.Offset(new[] { sl }, 0.01);
            var k = res.FindIndex(p => p.Layer == "SARJETA" && PolygonOps.TotalArea(PolygonOps.Intersect(new[] { p.Shape }, grown)) > 1e-5);
            if (k < 0) { left.Add(sl); continue; }
            var merged = PolygonOps.Union(new[] { res[k].Shape, sl }).OrderByDescending(x => x.Area).ToList();
            res[k] = res[k] with { Shape = merged[0] };
            foreach (var extra in merged.Skip(1).Where(x => x.Area > 1e-3)) res.Add(res[k] with { Shape = extra });
        }
        return res;
    }

    /// <summary>
    /// Pavimento sem cunhas: as sobras entre a sarjeta recortada e a linha do meio-fio na transição da esquina (cunhas de
    /// 3–5° com 0,5–1 m e poucos centímetros de largura) saem – nenhum trecho de pista com menos de 6 cm de largura.
    /// </summary>
    internal static List<Polygon2> SoundPavement(IEnumerable<Polygon2> polys)
    {
        // Só tira (abertura): o fechamento do Clean preenchia reentrâncias estreitas da pista – o asfalto avançava sob o
        // meio-fio e o nariz do canteiro.
        var src = polys.ToList();
        return PolygonOps.Intersect(PolygonOps.Clean(src, 0.03), src).Where(p => p.Area >= MinFloorArea).ToList();
    }

    /// <summary>Acrescenta peças já prontas (simplificadas, sem lascas).</summary>
    internal static void AddPieces(MarkingGeometry geo, IEnumerable<MarkingPiece> pieces)
    {
        foreach (var p in pieces)
        {
            var ss = p.Shape.Simplified();
            if (ss != null && ss.Area > 1e-4) geo.Pieces.Add(p with { Shape = ss });
        }
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
        var hi = L.HiEdge(leg, t) - EarInset(L, leg, 1, t) - 0.3;
        var floor = -(L.LoEdge(leg, t) - EarInset(L, leg, -1, t)) + 0.3;
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
        // Emenda pela ponta: as linhas das vias continuam pela curva, sem travessias nem controle.
        if (L.IsBend)
        {
            if (roadMembers != null) foreach (var c in BendLines(d, L, z, roadMembers)) Add(c);
            return res;
        }
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
            var crosswalk = CrosswalkOn(d, L, leg);
            var cwW = CrosswalkWidthOf(d, L, leg);
            var tc = CrosswalkT(d, L, leg);
            var stopFar = StopFar(d, L, leg);
            var tStop = crosswalk ? leg.Clear + TangentGap(L, leg) + SetbackOf(d, L, leg) + cwW + 1.6 + 0.2 : leg.Clear + 1.0;
            if (tStop > max - 1) continue;

            // Travessia de pedestres e rebaixamentos.
            if (crosswalk)
            {
                var a = L.At(leg, tc, L.HiEdge(leg, tc) - EarInset(L, leg, 1, tc));
                var b = L.At(leg, tc, -(L.LoEdge(leg, tc) - EarInset(L, leg, -1, tc)));
                var cwDefs = new CrosswalkSetup { CrosswalkWidth = cwW, StopLines = false, EdgeSetback = 0.3 }
                    .Build(a, b, z, output, cwW, 0.40);
                var near = new[] { Circle(L.At(leg, tc, 0), cwW * 3 + L.HiEdge(leg, tc) + L.LoEdge(leg, tc)) };
                foreach (var def in cwDefs)
                {
                    foreach (var gp in PolygonOps.Intersect(L.Obstacles, near))
                        def.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = gp.Outer.ToList() });
                    Add(def);
                }
                // Rampas com as medidas da interseção (já ajustadas), centradas no eixo da faixa, subindo perpendicular ao
                // meio-fio a partir da face dele.
                var plan = RampPlanOf(d, L, leg);
                foreach (var (sgn, w, tpl) in new[] { (1.0, L.HiEdge(leg, tc) - EarInset(L, leg, 1, tc), plan.Hi), (-1.0, L.LoEdge(leg, tc) - EarInset(L, leg, -1, tc), plan.Lo) })
                {
                    if (tpl == null) continue;
                    var curb = L.At(leg, tc, sgn * w);
                    var side = (L.At(leg, tc, sgn * (w + 1)) - curb).Normalized();
                    var ramp = (RampDefinition)tpl.CloneWithNewId();
                    ramp.PathRef = PathReference.FromPoints(new[] { curb, curb + side }, z);
                    Add(ramp);
                }
                // Canteiro central atravessado: rampas nas duas faces com patamar no meio, ou passagem rebaixada no nível da
                // pista com faixa de alerta junto a cada borda (NBR 9050 / NBR 16537 – itens a confirmar).
                var hwc = cwW / 2 - 0.1;
                foreach (var mp in MedianPassesOf(d, L, leg))
                {
                    Vec2 At(double t, double o) => L.At(leg, t, o);
                    if (mp.Mode == TipoTravessiaCanteiro.Rampas)
                        foreach (var rp in MedianCrossing.Ramps(At, tc, mp.Lo, mp.Hi, z, mp.Template, null)) Add(rp);
                    else
                        foreach (var tr in MedianCrossing.AlertStrips(At, tc - hwc, tc + hwc, mp.Lo, mp.Hi, z, null)) Add(tr);
                }
            }

            // Controle do direito de passagem.
            if (inbound)
            {
                var (lo, hi) = ApproachSpan(L, leg, tStop);
                var mid = (lo + hi) / 2;
                var dir = L.Inbound(leg, tStop);
                // Controle da aproximação: o do ramo (ajuste – ex.: PARE também na principal) ou o geral.
                var own = LegSet(d, L, leg)?.Control;
                var control = own ?? d.Control;
                var secondary = (own != null || !isMain) && control is ControleIntersecao.Pare or ControleIntersecao.DePreferencia;
                var signal = control == ControleIntersecao.Semaforo;
                var walkHi = (leg.Sign > 0 ? r.Def.LeftSidewalk : r.Def.RightSidewalk) > 0.5;
                var signAt = L.At(leg, tStop + 0.3, L.HiEdge(leg, tStop) - EarInset(L, leg, 1, tStop + 0.3) + (walkHi ? L.CurbWidth + 0.45 : 1.0));
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

        // Eixo e faixas no miolo (LCO) e nas aproximações (contínuas) – MBST Vol. IV / CTB art. 207.
        if (roadMembers != null)
            foreach (var line in RuleLines(d, L, z, roadMembers)) Add(line);
        // Conversões à esquerda proibidas: R-4a em cada aproximação (o eixo segue contínuo pela boca das transversais).
        if (!d.LeftTurns && d.Signs)
            foreach (var leg in L.Legs)
            {
                var r = L.Roads[leg.Road];
                if (!(r.Def.TwoWay || leg.Sign < 0)) continue;
                var t = leg.Clear + StopFar(d, L, leg) + 6;
                if (t > MaxT(L, leg) - 1) continue;
                var walkHi = (leg.Sign > 0 ? r.Def.LeftSidewalk : r.Def.RightSidewalk) > 0.5;
                hierarchy = r.Def.Hierarchy;
                Add(new SignDefinition { Code = "R-4a", Position = L.At(leg, t, L.HiEdge(leg, t) - EarInset(L, leg, 1, t) + (walkHi ? L.CurbWidth + 0.45 : 1.0)), Direction = L.Inbound(leg, t), Z = z });
            }
        hierarchy = L.Roads[L.Main].Def.Hierarchy;

        // Linha de continuidade (LCO): o bordo da via principal continua tracejado na boca da secundária – quem está na
        // principal e entra na secundária (ou vice-versa) cruza a linha (MBST Vol. IV).
        if (UseContinuityLine(d, L))
            foreach (var lco in ContinuityLines(d, L, z, roadMembers != null && L.Main < roadMembers.Count ? roadMembers[L.Main] : null)) Add(lco);

        // Orelhas das esquinas: recortam a pintura da interseção e são recortadas pelas rampas (que ficam na borda delas).
        if (L.Ears.Count > 0)
        {
            hierarchy = L.Roads[L.Main].Def.Hierarchy;
            var rampFps = res.OfType<RampDefinition>().Select(r => RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points))).ToList();
            foreach (var c in res.Where(c => c is not RampDefinition && CutByEars(c)))
                foreach (var e in L.Ears)
                    if (Near(c, e.Footprint))
                        foreach (var zc in EarCut(e, c))
                            c.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = zc.Outer.ToList() });
            foreach (var e in L.Ears)
            {
                var ear = (CurbExtensionDefinition)e.Template.CloneWithNewId();
                ear.PathRef = PathReference.FromPoints(e.Path.Points, z);
                foreach (var fp in rampFps)
                    if (PolygonOps.TotalArea(PolygonOps.Intersect(new[] { fp }, new[] { e.Footprint })) > 1e-4)
                        ear.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = fp.Outer.ToList() });
                Add(ear);
                // A sarjeta da via contorna a frente da orelha, no nível da pista.
                if (e.GutterPath != null)
                {
                    var g = new LinearMarkingDefinition { Code = "SARJETA", WidthOverride = e.Gutter, PathRef = PathReference.FromPoints(e.GutterPath.Points, z) };
                    // Nas pontas a sarjeta nova encosta na da via (que segue além da orelha) sem entrar na orelha.
                    g.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = e.Footprint.Outer.ToList() });
                    Add(g);
                }
            }
        }
        // Piso tátil nas calçadas (opção das vias): a rede segue pelas esquinas e liga cada rampa das travessias (as do canteiro
        // central ficam de fora).
        if (TactileNetworkOn(L))
        {
            hierarchy = L.Roads[L.Main].Def.Hierarchy;
            var sideRamps = res.OfType<RampDefinition>().Where(r =>
            {
                var f = RampGenerator.FrameOf(new Polyline2(r.PathRef.Points));
                return !L.Obstacles.Any(o => o.Contains(f.P(0, 0.3)));
            }).ToList();
            foreach (var t in TactileNetwork(L, z, sideRamps)) Add(t);
        }
        return res;
    }

    /// <summary>A marca pode tocar o polígono (caixas envolventes com 3 m de folga).</summary>
    private static bool Near(MarkingDefinition c, Polygon2 p)
    {
        var pts = (c.Path ?? (c as HatchMarkingDefinition)?.Boundary)?.Points.ToList() ?? new List<Vec2>();
        if (c is SymbolMarkingDefinition sm) pts.Add(sm.Position);
        if (c is TextMarkingDefinition tm) pts.Add(tm.Position);
        if (pts.Count == 0) return true;
        var (mn, mx) = p.Bounds;
        const double m = 3.0;
        return pts.Max(q => q.X) >= mn.X - m && pts.Min(q => q.X) <= mx.X + m && pts.Max(q => q.Y) >= mn.Y - m && pts.Min(q => q.Y) <= mx.Y + m;
    }

    /// <summary>A interseção recebe LCO no bordo da principal? Automático: principal arterial/rodovia/trânsito rápido com secundária de hierarquia menor.</summary>
    public static bool UseContinuityLine(IntersectionDefinition d, IntersectionLayout L)
    {
        if (d.ContinuityLine == LinhaContinuidade.Nunca || L.Roads.Count < 2) return false;
        if (d.ContinuityLine == LinhaContinuidade.Sempre) return true;
        var main = Hierarquia.Rank(L.Roads[L.Main].Def.Hierarchy);
        var minor = L.Roads.Where((_, i) => i != L.Main).Select(r => Hierarquia.Rank(r.Def.Hierarchy)).DefaultIfEmpty(0).Max();
        return main >= Hierarquia.Rank(HierarquiaViaria.Arterial) && minor < main && minor > 0;
    }

    /// <summary>LCO ao longo do bordo da via principal em cada boca das vias secundárias (do fim de uma curva ao início da outra).</summary>
    public static List<LinearMarkingDefinition> ContinuityLines(IntersectionDefinition d, IntersectionLayout L, double z,
        IReadOnlyCollection<MarkingDefinition>? mainMembers = null)
    {
        var res = new List<LinearMarkingDefinition>();
        var main = L.Roads[L.Main];
        var axis = main.Axis;
        var sN = axis.Project(L.Node).Station;
        var speed = Hierarquia.DefaultSpeed(main.Def.Hierarchy ?? HierarquiaViaria.Arterial);
        var reach = L.Roads.Where((_, i) => i != L.Main).Select(r => Math.Max(r.Def.TotalLeft, r.Def.TotalRight)).DefaultIfEmpty(10).Max() + d.CornerRadius + 8;
        var mouths = new List<Polygon2>();
        foreach (var (r, i) in L.Roads.Select((r, i) => (r, i)).Where(x => x.i != L.Main))
            mouths.AddRange(RoadGenerator.Band(r.Axis, -(r.Def.RightWidth + d.CornerRadius * 0.95), r.Def.LeftWidth + d.CornerRadius * 0.95));
        if (mouths.Count == 0) return res;
        foreach (var side in new[] { 1, -1 })
        {
            var edge = side > 0 ? main.Def.LeftWidth : main.Def.RightWidth;
            var gutter = main.Def.Gaps.Where(g => !g.Median && Math.Sign(g.Offset) == side).Select(g => g.Width).DefaultIfEmpty(0).Sum();
            var off = side * (edge - gutter - 0.15);
            // Continua a linha de bordo que a via já tem desse lado (a mais externa), quando existe.
            var lbo = mainMembers?.OfType<LinearMarkingDefinition>().Where(l => l.Code == "LBO" && Math.Sign(l.Offset) == side && Math.Abs(l.Offset) > edge * 0.5)
                .OrderByDescending(l => Math.Abs(l.Offset)).FirstOrDefault();
            if (lbo != null) off = lbo.Offset;
            var s0 = Math.Max(0, sN - reach);
            var s1 = Math.Min(axis.Length, sN + reach);
            // Trechos do bordo dentro das bocas (amostrado a cada 0,25 m).
            double? start = null;
            var runs = new List<(double A, double B)>();
            for (var s = s0; s <= s1 + 1e-6; s += 0.25)
            {
                var p = axis.PointAt(s) + axis.TangentAt(s).PerpLeft * off;
                var inside = mouths.Any(m => m.Contains(p)) && !L.StraightSides.Any(k => k.Contains(p));
                if (inside && start == null) start = s;
                if ((!inside || s + 0.25 > s1) && start is { } a) { runs.Add((a, s)); start = null; }
            }
            foreach (var (a, b) in runs.Where(x => x.B - x.A > 2))
            {
                var pts = new List<Vec2>();
                for (var s = a; s <= b + 1e-6; s += 1.0) pts.Add(axis.PointAt(s) + axis.TangentAt(s).PerpLeft * off);
                if (pts.Count < 2 || pts[^1].DistanceTo(axis.PointAt(b) + axis.TangentAt(b).PerpLeft * off) > 0.05)
                    pts.Add(axis.PointAt(b) + axis.TangentAt(b).PerpLeft * off);
                res.Add(new LinearMarkingDefinition { Code = "LCO", Speed = speed, PathRef = PathReference.FromPoints(pts, z) });
            }
        }
        return res;
    }
}
