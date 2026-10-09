using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AB-2: matriz de interseções – seções (mão única de 1 faixa, local, coletora, avenida com canteiro, avenida com
/// canteiro largo) × ângulos × tipos (cruz, T, Y, 5 ramos), com vias com e sem canteiro e larguras diferentes.
/// </summary>
public static class AB2Matrix
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static ElementoSecao E(TipoElementoSecao t, double w) => new() { Tipo = t, Largura = w };
    private const TipoElementoSecao R = TipoElementoSecao.FaixaRolamento;

    /// <summary>Seções da matriz (nome curto → seção).</summary>
    public static readonly Dictionary<string, Func<RoadSetup>> Sections = new()
    {
        ["mao1"] = () => new RoadSetup
        {
            TwoWay = false, Hierarchy = HierarquiaViaria.Local, Speed = 40, Center = CenterTreatment.Nenhum, SarjetaSomada = true,
            Right = { E(R, 3.50), E(TipoElementoSecao.Calcada, 2.50) },
            Left = { E(TipoElementoSecao.Calcada, 2.50) },
        },
        ["local"] = () => RoadTemplates.All[0].Create(),
        ["coletora"] = () => RoadTemplates.All[1].Create(),
        ["canteiro"] = () => RoadTemplates.All[2].Create(),
        ["canteiroLargo"] = () =>
        {
            var s = RoadTemplates.All[2].Create();
            s.MedianWidth = 8.0;
            return s;
        },
    };

    /// <summary>Pares de seções (principal, outra): com e sem canteiro, larguras diferentes.</summary>
    public static readonly (string A, string B)[] Pairs =
    {
        ("local", "local"), ("coletora", "canteiro"), ("canteiro", "local"), ("canteiroLargo", "coletora"),
        ("mao1", "canteiro"), ("canteiro", "canteiroLargo"), ("canteiro", "mao1"),
    };

    public static readonly double[] Angles = { 30, 45, 60, 75, 90, 105, 120, 135, 150 };
    public static readonly string[] Kinds = { "cruz", "T", "Y", "5ramos" };

    public static Vec2 Dir(double deg) => new(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180));

    /// <summary>Eixos de cada tipo (a = principal, b = outra; <paramref name="ang"/> = ângulo entre os ramos).</summary>
    public static List<(string Sec, Vec2[] Pts)> Axes(string kind, string a, string b, double ang)
    {
        const double L = 90;
        switch (kind)
        {
            case "cruz":
                return new() { (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { -Dir(ang) * L, Dir(ang) * L }) };
            case "T":
                return new() { (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { Vec2.Zero, Dir(ang) * L }) };
            case "Y":
                // Tronco vindo de baixo e dois ramos que se abrem com o ângulo pedido.
                return new()
                {
                    (a, new[] { new Vec2(0, -L), Vec2.Zero }),
                    (a, new[] { Vec2.Zero, Dir(90 - ang / 2) * L }),
                    (b, new[] { Vec2.Zero, Dir(90 + ang / 2) * L }),
                };
            default:
            {
                // Cruz + um quinto ramo na maior abertura entre os quatro.
                var dirs = new[] { 0.0, 180.0, ang, ang + 180 }.Select(d => (d % 360 + 360) % 360).OrderBy(d => d).ToList();
                var best = 0.0;
                var mid = 0.0;
                for (int i = 0; i < dirs.Count; i++)
                {
                    var gap = ((dirs[(i + 1) % dirs.Count] - dirs[i]) % 360 + 360) % 360;
                    if (gap > best) { best = gap; mid = dirs[i] + gap / 2; }
                }
                return new()
                {
                    (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }),
                    (b, new[] { -Dir(ang) * L, Dir(ang) * L }),
                    ("local", new[] { Vec2.Zero, Dir(mid) * L }),
                };
            }
        }
    }

    public static IntersectionDefinition Template(bool crosswalks = true) => new()
    {
        Control = ControleIntersecao.Pare, Crosswalks = crosswalks, Ramps = crosswalks,
    };

    /// <summary>Vias pelos eixos e as interseções nos nós, como o plugin faz.</summary>
    public static Y34TipTests.World Make(IntersectionDefinition? template, List<(string Sec, Vec2[] Pts)> roads,
        Action<IntersectionDefinition, IReadOnlyList<IntersectionRoad>>? tune = null)
    {
        var w = new Y34TipTests.World();
        foreach (var (sec, pts) in roads)
        {
            var pr = PathReference.FromPoints(pts, 0);
            var g = Sections[sec]().Build(pr, new OutputSettings(), Cat);
            var pav = g.OfType<RoadPavementDefinition>().First();
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(pav, axis));
        }
        template ??= Template();
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            if (!IntersectionGenerator.NeedsIntersection(rs, node)) continue;
            var d = (IntersectionDefinition)template.CloneWithNewId();
            d.Node = node;
            tune?.Invoke(d, rs);
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, Cat));
        }
        return w;
    }

    public static Y34TipTests.World Make(string kind, string a, string b, double ang, IntersectionDefinition? template = null) =>
        Make(template, Axes(kind, a, b, ang));

    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in Kinds)
            foreach (var (a, b) in Pairs)
                foreach (var ang in Angles)
                    yield return new object[] { kind, a, b, ang };
    }

    /// <summary>Menor ângulo interno (graus) de um anel, ignorando vértices quase colineares.</summary>
    public static double MinInteriorAngle(IReadOnlyList<Vec2> ring)
    {
        var min = 180.0;
        var n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            var a = ring[(i - 1 + n) % n];
            var b = ring[i];
            var c = ring[(i + 1) % n];
            var u = a - b;
            var v = c - b;
            if (u.Length < 0.02 || v.Length < 0.02) continue;
            var ang = Math.Acos(Math.Clamp(u.Dot(v) / (u.Length * v.Length), -1, 1)) * 180 / Math.PI;
            min = Math.Min(min, ang);
        }
        return min;
    }
}

/// <summary>Medidas das esquinas de uma interseção (raio da curva do bordo da pista e ponto de tangência).</summary>
public static class AB2Measure
{
    public sealed record CornerMeasure(IntersectionCorner Corner, IntersectionLeg A, IntersectionLeg B, double Span, double Requested,
        double Expected, double? Measured, Vec2? TangentA, Vec2? TangentB, double MaxDeviation, int ArcPoints);

    private static double Ang(Vec2 v) => Math.Atan2(v.Y, v.X);

    private static double Ccw(double a, double b)
    {
        var d = (b - a) * 180 / Math.PI;
        return (d % 360 + 360) % 360;
    }

    /// <summary>
    /// Raio da curva de cada esquina: ajuste de círculo (mínimos quadrados) aos pontos do bordo da pista dentro do triângulo da
    /// esquina que se afastam mais de 2 cm dos dois bordos retos. Pontos de tangência = extremos desse arco.
    /// </summary>
    public static List<CornerMeasure> Corners(IntersectionLayout L)
    {
        var d = L.Definition!;
        var res = new List<CornerMeasure>();
        foreach (var c in L.Corners)
        {
            var la = L.Legs.FirstOrDefault(l => l.Road == c.RoadA && l.Sign == (c.LeftA ? 1 : -1));
            var lb = L.Legs.FirstOrDefault(l => l.Road == c.RoadB && l.Sign == (c.LeftB ? -1 : 1));
            if (la == null || lb == null) continue;
            var span = Ccw(Ang(la.Dir), Ang(lb.Dir));
            var R = Math.Max(0, IntersectionGenerator.LegSet(d, L, la)?.CornerRadius ?? d.CornerRadius);
            var eff = span < 90 ? Math.Round(R * Math.Tan(span * Math.PI / 360), 2) : R;
            // Bordos retos dos dois ramos (face do meio-fio = bordo da pista) longe do nó.
            var tA = la.Clear + 15;
            var tB = lb.Clear + 15;
            var a0 = L.At(la, tA, L.HiEdge(la, tA));
            var a1 = L.At(la, tA - 1, L.HiEdge(la, tA - 1));
            var b0 = L.At(lb, tB, -L.LoEdge(lb, tB));
            var b1 = L.At(lb, tB - 1, -L.LoEdge(lb, tB - 1));
            static double DistLine(Vec2 p, Vec2 q0, Vec2 q1)
            {
                var u = (q1 - q0).Normalized();
                var w = p - q0;
                return Math.Abs(u.X * w.Y - u.Y * w.X);
            }
            // Vértice V (encontro dos bordos retos) e tangente esperada T = r·cot(θ/2): o arco fica a até T de V.
            // Direções dos bordos retos para fora do nó (de a1 para a0, a 1 m mais longe).
            var ua = (a0 - a1).Normalized();
            var ub = (b0 - b1).Normalized();
            var den = ua.X * ub.Y - ua.Y * ub.X;
            Vec2? V = null;
            if (Math.Abs(den) > 1e-6)
            {
                var w0 = b0 - a0;
                var s0 = (w0.X * ub.Y - w0.Y * ub.X) / den;
                V = a0 + ua * s0;
            }
            var half = span * Math.PI / 360;
            var T = eff / Math.Tan(Math.Max(1e-3, half));
            var tri = PolygonOps.Offset(new[] { c.Tri }, 0.5);
            var pts = new List<Vec2>();
            foreach (var pg in L.Pavement)
                foreach (var ring in new[] { pg.Outer }.Concat(pg.Holes))
                    for (int i = 0; i < ring.Count; i++)
                    {
                        var p0 = ring[i];
                        var p1 = ring[(i + 1) % ring.Count];
                        var k = Math.Max(1, (int)Math.Ceiling(p0.DistanceTo(p1) / 0.05));
                        for (int j = 0; j < k; j++)
                        {
                            var p = p0 + (p1 - p0) * (j / (double)k);
                            if (!tri.Any(t => t.Contains(p))) continue;
                            if (V is { } v && p.DistanceTo(v) > T * 1.05 + 0.05) continue;
                            if (DistLine(p, a0, a1) > 0.02 && DistLine(p, b0, b1) > 0.02 && p.DistanceTo(L.Node) < 60) pts.Add(p);
                        }
                    }
            double? measured = null;
            Vec2? ta = null, tb = null;
            // Desvio máximo do bordo em relação à curva ideal: círculo de raio esperado tangente aos dois bordos retos.
            var maxDev = double.NaN;
            if (V is { } vv && span < 179)
            {
                var bis = (ua + ub).Normalized();
                var C = vv + bis * (eff / Math.Sin(Math.Max(1e-3, half)));
                // Esquina quase reta (≥ 120°): a curva se afasta menos de 2 cm dos bordos retos – sem pontos, sem desvio.
                maxDev = pts.Count == 0 ? (span >= 120 ? 0 : double.NaN) : pts.Max(p => Math.Abs(p.DistanceTo(C) - eff));
                ta = vv + ua * T;
                tb = vv + ub * T;
            }
            if (pts.Count >= 6)
            {
                // Círculo de Kåsa.
                double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0, sz = 0;
                var n = pts.Count;
                var mx = pts.Average(p => p.X);
                var my = pts.Average(p => p.Y);
                foreach (var p in pts)
                {
                    var x = p.X - mx; var y = p.Y - my; var z = x * x + y * y;
                    sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y; sxz += x * z; syz += y * z; sz += z;
                }
                var det = sxx * syy - sxy * sxy;
                if (Math.Abs(det) > 1e-9)
                {
                    var cx = (sxz * syy - syz * sxy) / (2 * det);
                    var cy = (syz * sxx - sxz * sxy) / (2 * det);
                    var r = Math.Sqrt(cx * cx + cy * cy + sz / n);
                    measured = r;
                }
            }
            res.Add(new CornerMeasure(c, la, lb, span, R, eff, measured, ta, tb, maxDev, pts.Count));
        }
        return res;
    }
}
