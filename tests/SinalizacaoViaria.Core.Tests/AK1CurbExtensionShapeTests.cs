using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AK: extensões de calçada nas esquinas com a forma das referências (NACTO, PBOT) – raio da esquina do veículo de
/// projeto, trecho reto com a largura toda indo além do prolongamento do alinhamento predial da esquina e transição suave
/// (curva reversa tangente) de volta ao meio-fio, com a largura da faixa junto ao meio-fio, sem invadir a faixa de rolamento,
/// em qualquer hierarquia e número de faixas, e também quando só um lado da esquina tem a extensão.
/// </summary>
public static class AKShape
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static ElementoSecao E(TipoElementoSecao t, double w) => new() { Tipo = t, Largura = w };
    private const TipoElementoSecao R = TipoElementoSecao.FaixaRolamento;
    private const TipoElementoSecao P = TipoElementoSecao.Estacionamento;
    private const TipoElementoSecao C = TipoElementoSecao.Calcada;

    /// <summary>Seções da matriz: hierarquia × faixas × estacionamento.</summary>
    public static readonly Dictionary<string, Func<RoadSetup>> Sections = new()
    {
        // Local, mão única de 1 faixa com estacionamento à direita.
        ["local1"] = () => new RoadSetup
        {
            TwoWay = false, Hierarchy = HierarquiaViaria.Local, Speed = 30, Center = CenterTreatment.Nenhum, SarjetaSomada = true,
            Right = { E(R, 3.50), E(P, 2.20), E(C, 2.50) }, Left = { E(C, 2.50) },
        },
        // Local, 1 + 1 faixas e estacionamento dos dois lados (modelo do plugin).
        ["local2"] = () => RoadTemplates.All[0].Create(),
        // Coletora, mão única de 3 faixas, estacionamento só à esquerda.
        ["coletora3"] = () => new RoadSetup
        {
            TwoWay = false, Hierarchy = HierarquiaViaria.Coletora, Speed = 50, Center = CenterTreatment.Nenhum, SarjetaSomada = true,
            Right = { E(R, 3.30), E(R, 3.30), E(R, 3.50), E(C, 3.00) }, Left = { E(P, 2.40), E(C, 3.00) },
        },
        // Coletora, 2 + 2 faixas sem estacionamento (modelo do plugin).
        ["coletora4"] = () => RoadTemplates.All[1].Create(),
        // Coletora, 2 + 2 faixas com estacionamento dos dois lados.
        ["coletora4p"] = () => new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Coletora, Speed = 50, Center = CenterTreatment.LFO1, SarjetaSomada = true,
            Right = { E(R, 3.30), E(R, 3.50), E(P, 2.50), E(C, 3.00) }, Left = { E(R, 3.30), E(R, 3.50), E(P, 2.50), E(C, 3.00) },
        },
        // Arterial com canteiro central, 2 + 2 faixas e estacionamento (modelo do plugin).
        ["arterial"] = () => RoadTemplates.All[2].Create(),
    };

    public static IntersectionDefinition Template() => new()
    {
        Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true, CurbExtensions = true,
    };

    public static List<(string Sec, Vec2[] Pts)> Axes(string kind, string a, string b, double ang) => AB2Matrix.Axes(kind, a, b, ang);

    /// <summary>Vias pelos eixos e as interseções nos nós, como o plugin faz (seções desta matriz).</summary>
    public static Y34TipTests.World Make(IntersectionDefinition? template, List<(string Sec, Vec2[] Pts)> roads,
        Action<IntersectionDefinition, IReadOnlyList<IntersectionRoad>>? tune = null)
    {
        var w = new Y34TipTests.World();
        foreach (var (sec, pts) in roads)
        {
            var pr = PathReference.FromPoints(pts, 0);
            var setup = Sections.TryGetValue(sec, out var f) ? f() : AB2Matrix.Sections[sec]();
            var g = setup.Build(pr, new OutputSettings(), Cat);
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

    public static Y34TipTests.World Make(string kind, string a, string b, double ang, IntersectionDefinition? template = null,
        Action<IntersectionDefinition, IReadOnlyList<IntersectionRoad>>? tune = null) => Make(template, Axes(kind, a, b, ang), tune);

    public static double Turn(Vec2 a, Vec2 b, Vec2 c)
    {
        var u = (b - a).Normalized();
        var v = (c - b).Normalized();
        return Math.Abs(Math.Atan2(u.Cross(v), u.Dot(v))) * 180 / Math.PI;
    }

    /// <summary>Círculo por três pontos (centro, raio).</summary>
    public static (Vec2 C, double R) Circle3(Vec2 a, Vec2 b, Vec2 c)
    {
        var d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-12) return (a, double.PositiveInfinity);
        var ux = ((a.X * a.X + a.Y * a.Y) * (b.Y - c.Y) + (b.X * b.X + b.Y * b.Y) * (c.Y - a.Y) + (c.X * c.X + c.Y * c.Y) * (a.Y - b.Y)) / d;
        var uy = ((a.X * a.X + a.Y * a.Y) * (c.X - b.X) + (b.X * b.X + b.Y * b.Y) * (a.X - c.X) + (c.X * c.X + c.Y * c.Y) * (b.X - a.X)) / d;
        var ctr = new Vec2(ux, uy);
        return (ctr, ctr.DistanceTo(a));
    }

    /// <summary>Face (bordo voltado para a pista) da extensão, no sentido do caminho (do ramo A para o B).</summary>
    public static List<Vec2> Face(EarPlan e) => SidewalkGenerator.EarOuterEdge(e.Template, e.Path);

    /// <summary>Raio da face junto à esquina: círculo pelos pontos em volta do ponto da face mais perto do vértice da esquina.</summary>
    public static double CornerRadiusOf(IntersectionLayout L, EarPlan e)
    {
        var cf = L.CornerFillets[(e.LegA.Road, e.LegA.Sign)];
        var face = Face(e);
        var i = Enumerable.Range(0, face.Count).OrderBy(k => face[k].DistanceTo(cf.V)).First();
        // Pontos a 0,6 m (ao longo da face) de cada lado do ponto mais perto do vértice: dentro do arco da esquina.
        Vec2 Walk(int from, int step, double dist)
        {
            var acc = 0.0;
            for (int k = from; k + step >= 0 && k + step < face.Count; k += step)
            {
                // Vértices da face (sobre o arco): pontos interpolados nas cordas tiram o raio do lugar.
                acc += face[k].DistanceTo(face[k + step]);
                if (acc >= dist) return face[k + step];
            }
            return face[step > 0 ? face.Count - 1 : 0];
        }
        return Circle3(Walk(i, -1, 0.6), face[i], Walk(i, 1, 0.6)).R;
    }

    /// <summary>Estação (t ao longo do ramo, a partir do nó) de um ponto.</summary>
    public static double T(IntersectionLayout L, IntersectionLeg leg, Vec2 p)
    {
        var axis = L.Roads[leg.Road].Axis;
        var (s, _) = axis.Project(p);
        return (s - leg.NodeStation) * leg.Sign;
    }

    /// <summary>
    /// Ponta da extensão no ramo <paramref name="leg"/>: comprimento (ao longo do meio-fio) da transição de volta ao meio-fio
    /// e a maior inclinação da face em relação a ele (graus).
    /// </summary>
    public static (double Length, double MaxSlope, double FullT, double EndT) EndTransition(IntersectionLayout L, EarPlan e, IntersectionLeg leg, int side, double face)
    {
        var pts = Face(e);
        // A face vai do ramo A para o B: a ponta longe da esquina no ramo A é o primeiro ponto.
        if (!e.Straight && leg.Road == e.LegA.Road && leg.Sign == e.LegA.Sign && (e.LegA != e.LegB)) pts = Enumerable.Reverse(pts).ToList();
        // Do fim (ponta longe da esquina) para dentro: avanço (o) e estação (t) de cada ponto.
        var dir = L.Roads[leg.Road].Axis;
        double Off(Vec2 p)
        {
            var t = T(L, leg, p);
            var edge = side > 0 ? L.HiEdge(leg, t) : L.LoEdge(leg, t);
            var (_, signed) = dir.Project(p);
            return edge - signed * leg.Sign * side;
        }
        var far = pts.Count - 1;
        var endT = T(L, leg, pts[far]);
        double fullT = endT;
        var maxSlope = 0.0;
        for (int k = far; k > 0; k--)
        {
            var o0 = Off(pts[k]);
            var o1 = Off(pts[k - 1]);
            var dt = Math.Abs(T(L, leg, pts[k - 1]) - T(L, leg, pts[k]));
            if (Math.Abs(o1 - o0) > 1e-4 || dt > 1e-4) maxSlope = Math.Max(maxSlope, Math.Atan2(Math.Abs(o1 - o0), dt) * 180 / Math.PI);
            if (o1 >= face - 0.01)
            {
                fullT = T(L, leg, pts[k - 1]);
                break;
            }
        }
        return (Math.Abs(endT - fullT), maxSlope, fullT, endT);
    }

    /// <summary>
    /// Faixas de rolamento de cada via (da pista menos as faixas junto ao meio-fio que não são de rolamento: estacionamento e
    /// a sarjeta somada), ao longo do eixo inteiro – inclusive dentro do cruzamento.
    /// </summary>
    public static List<Polygon2> TravelBands(Y34TipTests.World w)
    {
        var res = new List<Polygon2>();
        foreach (var r in w.Roads)
        {
            var left = r.Def.LeftWidth - CurbSide(r.Def, true);
            var right = r.Def.RightWidth - CurbSide(r.Def, false);
            res.AddRange(RoadGenerator.Band(r.Axis, -right, left));
        }
        return PolygonOps.Union(res);
    }

    /// <summary>
    /// Largura junto ao meio-fio que não é faixa de rolamento, lida da seção: estacionamento (com a sarjeta somada) ou, sem
    /// ele, só a sarjeta somada.
    /// </summary>
    public static double CurbSide(RoadPavementDefinition r, bool left)
    {
        if (IntersectionGenerator.ParkingDepth(r, left) is { } park) return park;
        var setup = RoadTemplates.FromJson(r.SetupJson);
        if (setup == null || setup.SarjetaSomada != true || !setup.PhysicalElements) return 0;
        var side = left ? setup.Left : setup.Right;
        var k = side.FindIndex(e => e.Tipo == TipoElementoSecao.Calcada);
        return k >= 0 && (k == 0 || !side[k - 1].Elevado) ? Math.Max(0, side[k].Sarjeta) : 0;
    }

    /// <summary>Área (m²) da extensão, com a sarjeta nova em frente a ela, sobre as faixas de rolamento.</summary>
    public static double Invasion(Y34TipTests.World w, EarPlan e)
    {
        var shapes = new List<Polygon2> { e.Footprint };
        if (e.GutterPath != null) shapes.AddRange(PolygonOps.Strip(e.GutterPath.Points, e.Gutter, roundJoins: true));
        return PolygonOps.TotalArea(PolygonOps.Intersect(PolygonOps.Union(shapes), TravelBands(w)));
    }

    /// <summary>Estação no ramo A onde o prolongamento do alinhamento predial da via B (fundo da calçada dela) cruza o meio-fio de A.</summary>
    public static double PropertyLineT(IntersectionLayout L, IntersectionLeg la, IntersectionLeg lb, bool onA)
    {
        var cf = L.CornerFillets[(la.Road, la.Sign)];
        var half = cf.Span * Math.PI / 360;
        var (leg, u, other) = onA ? (la, cf.Ua, lb) : (lb, cf.Ub, la);
        var rd = L.Roads[other.Road].Def;
        // Lado da via "outra" voltado para a esquina: lado baixo do ramo B (−o) ou lado alto do ramo A (+o).
        var walk = onA ? (lb.Sign > 0 ? rd.RightSidewalk : rd.LeftSidewalk) : (la.Sign > 0 ? rd.LeftSidewalk : rd.RightSidewalk);
        var p = cf.V + u * ((walk + L.CurbWidth) / Math.Sin(2 * half));
        return (p - L.Node).Dot(leg.Dir);
    }
}

public class AK1CurbExtensionShapeTests
{
    public static IEnumerable<object[]> OneSided() => new[]
    {
        // Só a local tem estacionamento junto ao meio-fio: a extensão fica só do lado dela.
        new object[] { "cruz", "local2", "coletora4", 90.0 },
        new object[] { "T", "coletora4", "local2", 90.0 },
        new object[] { "T", "local2", "coletora4", 90.0 },
        new object[] { "cruz", "arterial", "coletora4", 90.0 },
    };

    public static IEnumerable<object[]> TwoSided() => new[]
    {
        new object[] { "cruz", "local2", "local2", 90.0 },
        new object[] { "cruz", "arterial", "local2", 90.0 },
        new object[] { "cruz", "coletora4p", "local2", 90.0 },
        new object[] { "T", "arterial", "local2", 90.0 },
    };

    public static IEnumerable<object[]> Oblique() => new[]
    {
        new object[] { "cruz", "local2", "local2", 60.0 },
        new object[] { "cruz", "local2", "local2", 120.0 },
        new object[] { "cruz", "local2", "coletora4", 60.0 },
        new object[] { "cruz", "local2", "coletora4", 120.0 },
    };

    /// <summary>
    /// Defeito 1 (print do usuário, imagem 1 das referências): a ponta da extensão voltava ao meio-fio numa curva reversa de
    /// raio 1,5 m sobre 2,2–2,5 m de avanço – transição de ~1,3 × a largura, inclinada a mais de 65° (um "calombo"). A
    /// transição natural é suave: pelo menos 1,8 × a largura ao longo do meio-fio e no máximo 55° de inclinação.
    /// </summary>
    [Theory]
    [MemberData(nameof(OneSided))]
    [MemberData(nameof(TwoSided))]
    [MemberData(nameof(Oblique))]
    public void Transition_BackToTheCurb_IsGentle(string kind, string a, string b, double ang)
    {
        var w = AKShape.Make(kind, a, b, ang);
        var L = w.Nodes[0].Layout;
        var ears = L.Ears.Where(e => !e.Straight).ToList();
        Assert.NotEmpty(ears);
        foreach (var e in ears)
            foreach (var (on, leg, side) in new[] { (e.SideA, e.LegA, 1), (e.SideB, e.LegB, -1) })
            {
                if (!on) continue;
                var es = L.EarSides[(leg.Road, leg.Sign, side)];
                var (len, slope, _, _) = AKShape.EndTransition(L, e, leg, side, es.Depth);
                Assert.True(len >= 1.8 * es.Depth - 0.05, $"{IntersectionGenerator.LegLabel(L, leg)}: transição de {len:0.00} m para {es.Depth:0.00} m de largura");
                Assert.True(slope <= 55.5, $"{IntersectionGenerator.LegLabel(L, leg)}: transição inclinada {slope:0.0}°");
            }
    }

    /// <summary>
    /// Defeito 2: com extensão dos dois lados, a face contornava a esquina paralela ao meio-fio antigo – raio da esquina +
    /// avanço (6 + 2,2 = 8,2 m), alargando a esquina em vez de manter o raio do veículo de projeto (imagem 1: "curb radius as
    /// determined by design vehicle"). A face nova usa o raio da esquina, tangente às duas faces deslocadas.
    /// </summary>
    [Theory]
    [MemberData(nameof(TwoSided))]
    [MemberData(nameof(OneSided))]
    [MemberData(nameof(Oblique))]
    public void CornerCurve_KeepsTheDesignRadius(string kind, string a, string b, double ang)
    {
        var w = AKShape.Make(kind, a, b, ang);
        var L = w.Nodes[0].Layout;
        foreach (var e in L.Ears.Where(e => !e.Straight))
        {
            var cf = L.CornerFillets[(e.LegA.Road, e.LegA.Sign)];
            var r = AKShape.CornerRadiusOf(L, e);
            Assert.InRange(r, cf.R - 0.02, cf.R + 0.02);
        }
    }

    /// <summary>
    /// A largura toda da extensão vai pelo menos 1,5 m (5 pés – NACTO [a confirmar]) além do prolongamento do alinhamento
    /// predial da outra via, e a face é contínua e tangente do começo ao fim.
    /// </summary>
    [Theory]
    [MemberData(nameof(OneSided))]
    [MemberData(nameof(TwoSided))]
    [MemberData(nameof(Oblique))]
    public void FullWidth_ReachesBeyondThePropertyLine_AndFaceIsSmooth(string kind, string a, string b, double ang)
    {
        var w = AKShape.Make(kind, a, b, ang);
        var L = w.Nodes[0].Layout;
        foreach (var e in L.Ears.Where(e => !e.Straight))
        {
            foreach (var (on, leg, side) in new[] { (e.SideA, e.LegA, 1), (e.SideB, e.LegB, -1) })
            {
                if (!on) continue;
                var es = L.EarSides[(leg.Road, leg.Sign, side)];
                var prop = AKShape.PropertyLineT(L, e.LegA, e.LegB, side > 0);
                Assert.True(es.TFullEnd >= prop + 1.5 - 0.02, $"{IntersectionGenerator.LegLabel(L, leg)}: largura toda até {es.TFullEnd:0.00}, alinhamento em {prop:0.00}");
            }
            var face = AKShape.Face(e);
            Assert.True(face[0].DistanceTo(e.Path.Points[0]) < 0.01, "a face não começa no meio-fio");
            Assert.True(face[^1].DistanceTo(e.Path.Points[^1]) < 0.01, "a face não termina no meio-fio");
            for (int i = 1; i + 1 < face.Count; i++)
                Assert.True(AKShape.Turn(face[i - 1], face[i], face[i + 1]) <= 8.0, $"bico na face em {face[i]}: {AKShape.Turn(face[i - 1], face[i], face[i + 1]):0.0}°");
        }
        var res = AB2Check.Run(w);
        Assert.Empty(res.Faults);
        Assert.Empty(res.Spikes);
    }

    public static IEnumerable<object[]> Matrix()
    {
        var secs = new[] { "local1", "local2", "coletora3", "coletora4", "coletora4p", "arterial" };
        foreach (var a in secs)
            foreach (var b in secs)
                yield return new object[] { a, b };
    }

    /// <summary>Hierarquia × número de faixas × estacionamento: a extensão nunca avança sobre uma faixa de rolamento.</summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Matrix_ExtensionNeverInvadesATravelLane(string a, string b)
    {
        var w = AKShape.Make("cruz", a, b, 90);
        var L = w.Nodes[0].Layout;
        foreach (var e in L.Ears)
            Assert.True(AKShape.Invasion(w, e) < 0.02, $"extensão {IntersectionGenerator.LegLabel(L, e.LegA)}: {AKShape.Invasion(w, e):0.000} m² sobre faixa de rolamento");
        var res = AB2Check.Run(w);
        Assert.Empty(res.Faults);
    }

    /// <summary>
    /// Defeito 3: a largura informada valia em qualquer lado da esquina – numa via sem estacionamento (ou além da largura
    /// dele) a extensão avançava sobre a faixa de rolamento. Agora a largura fica limitada à faixa junto ao meio-fio (com aviso).
    /// </summary>
    [Theory]
    [InlineData("local2", "coletora4", 3.5)]
    [InlineData("coletora4", "coletora4", 2.5)]
    [InlineData("arterial", "local2", 4.0)]
    public void ForcedWidth_IsLimitedToTheCurbSideLane(string a, string b, double depth)
    {
        var t = AKShape.Template();
        t.CurbExtensionDepth = depth;
        var w = AKShape.Make("cruz", a, b, 90, t);
        var L = w.Nodes[0].Layout;
        foreach (var e in L.Ears)
            Assert.True(AKShape.Invasion(w, e) < 0.02, $"extensão {IntersectionGenerator.LegLabel(L, e.LegA)}: {AKShape.Invasion(w, e):0.000} m² sobre faixa de rolamento");
        Assert.Empty(AB2Check.Run(w).Faults);
    }
}
