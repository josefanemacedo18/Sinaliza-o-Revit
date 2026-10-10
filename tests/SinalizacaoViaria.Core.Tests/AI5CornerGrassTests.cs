using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AI, item 5: faixa de serviço gramada nas esquinas (a quina do lote continua reta). Na curva da esquina a grama
/// acompanha o meio-fio – arco concêntrico a ele (±1 mm) – ou a esquina fica sem grama (passeio); nunca cortada pela quina
/// do lote nem com pontas onde uma rampa ou uma extensão a corta.
/// </summary>
public class AI5CornerGrassTests
{
    private static List<Polygon2> Grass(Y34TipTests.World w)
    {
        var parts = Y34TipTests.Geometry(w);
        return parts.Where(p => p.Piece.Color == MarkingColor.Grama && p.Piece.Layer == "GRAMADO" && p.Piece.Solid == null && p.Piece.Profile == null
                && p.Def is not RoadPavementDefinition || p.Piece.Color == MarkingColor.Grama && p.Def is LinearMarkingDefinition { Code: "GRAMADO" })
            .GroupBy(p => (p.Def, ElementPlan.FloorKey(p.Piece)))
            .SelectMany(g => PolygonOps.Union(g.Select(x => x.Piece.Shape)).Select(ElementPlan.Prepare)).ToList();
    }

    /// <summary>Setor da curva de uma esquina: do centro do arco, entre os raios que passam pelos fins da curva (estreitado 2°).</summary>
    private static (Polygon2 Poly, Vec2 U0, Vec2 U1) Sector(Vec2 c, Vec2 ta, Vec2 tb)
    {
        var a0 = Math.Atan2(ta.Y - c.Y, ta.X - c.X);
        var a1 = Math.Atan2(tb.Y - c.Y, tb.X - c.X);
        var da = a1 - a0;
        while (da > Math.PI) da -= 2 * Math.PI;
        while (da < -Math.PI) da += 2 * Math.PI;
        var shrink = Math.Sign(da) * 2 * Math.PI / 180;
        var pts = new List<Vec2> { c };
        for (int i = 0; i <= 40; i++) pts.Add(c + Vec2.FromAngle(a0 + shrink + (da - 2 * shrink) * i / 40) * 40);
        return (new Polygon2(pts), Vec2.FromAngle(a0 + shrink), Vec2.FromAngle(a1 - shrink));
    }

    /// <summary>Falhas da grama nas esquinas de um caso.</summary>
    public static List<string> Check(Y34TipTests.World w)
    {
        var res = new List<string>();
        var grass = Grass(w);
        var L = w.Nodes[0].Layout;
        // Sem pontas (ângulo interno &lt; 15°) em nenhum piso de grama perto do nó.
        var disk = new Polygon2(CurveTools.Circle(L.Node, Math.Max(25, L.Radius + 6), 0.05));
        foreach (var g in grass.Where(g => g.Outer.Any(v => disk.Contains(v))))
            if (AB2Matrix.MinInteriorAngle(g.Outer) is var ang && ang < 15) res.Add($"grama com ponta de {ang:0}° ({g.Area:0.00} m²)");
        foreach (var ((road, sign), cf) in L.CornerFillets)
        {
            var la = L.Legs.FirstOrDefault(l => l.Road == road && l.Sign == sign);
            if (la == null || !L.SideTangentT.TryGetValue((road, sign, 1), out var tA)) continue;
            var half = cf.Span * Math.PI / 360;
            var T = cf.R / Math.Tan(half);
            var c = cf.V + (cf.Ua + cf.Ub).Normalized() * (cf.R / Math.Sin(half));
            var (sec, u0, u1) = Sector(c, cf.V + cf.Ua * T, cf.V + cf.Ub * T);
            // Só a curva: até 1,5 × o raio do centro (as faixas retas das vias ficam de fora).
            var near = new Polygon2(CurveTools.Circle(c, cf.R * 1.5, 0.005));
            var inCorner = PolygonOps.Intersect(PolygonOps.Intersect(grass, new[] { sec }), new[] { near }).Where(p => p.Area > 0.01).ToList();
            if (inCorner.Count == 0) continue;                                    // esquina sem grama: passeio
            // Grama na curva: só dois raios (borda junto ao meio-fio e borda de dentro), concêntricos ao meio-fio (±1 mm).
            var ds = inCorner.SelectMany(p => p.Outer).Where(v => v.DistanceTo(c) > 0.2 && !OnRay(c, u0, v) && !OnRay(c, u1, v)).Select(v => v.DistanceTo(c)).ToList();
            if (ds.Count == 0) continue;
            var (r0, r1) = (ds.Min(), ds.Max());
            var off = ds.Where(d => Math.Abs(d - r0) > 0.001 && Math.Abs(d - r1) > 0.001).ToList();
            if (off.Count > 0) res.Add($"esquina da via {road}{(sign > 0 ? "+" : "−")}: grama fora do arco concêntrico ({off.Count} pontos, até {off.Max(d => Math.Min(Math.Abs(d - r0), Math.Abs(d - r1))):0.000} m)");
        }
        return res;
    }

    /// <summary>Ponto sobre o raio que limita o setor (corte da grama no limite do setor).</summary>
    private static bool OnRay(Vec2 c, Vec2 u, Vec2 v) => (v - c).Dot(u) > 0 && Math.Abs((v - c).Cross(u)) < 0.002;

    public static IEnumerable<object[]> Cases() => new[]
    {
        new object[] { "cruz", "canteiro", "canteiro", 90.0, 6.0, false },
        new object[] { "cruz", "canteiro", "canteiro", 90.0, 10.0, false },
        new object[] { "cruz", "canteiro", "canteiro", 90.0, 12.0, false },
        new object[] { "cruz", "canteiro", "coletora", 75.0, 8.0, false },
        new object[] { "T", "canteiro", "canteiro", 105.0, 12.0, false },
        new object[] { "cruz", "coletora", "canteiro", 75.0, 6.0, true },
        new object[] { "cruz", "canteiro", "coletora", 105.0, 6.0, true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void CornerGrass_FollowsTheCurbOrIsLeftOut_NeverCutByTheLotCorner(string kind, string a, string b, double ang, double radius, bool ext)
    {
        var w = AB2Matrix.Make(new IntersectionDefinition
        {
            Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true, CornerRadius = radius, CurbExtensions = ext, CurbExtensionsOpposite = ext,
        }, AB2Matrix.Axes(kind, a, b, ang));
        Assert.NotEmpty(Grass(w));
        Assert.Empty(Check(w));
        // Sem furos nem vãos entre os pisos (a grama que sai vira passeio).
        Assert.Empty(AB2Check.Run(w).Faults);
    }
}
