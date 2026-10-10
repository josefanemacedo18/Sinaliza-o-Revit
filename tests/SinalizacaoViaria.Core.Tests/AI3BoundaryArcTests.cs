using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AI, item 3: contornos de piso com arcos verdadeiros – cada curva vira UM arco (centro e raio ±1 mm do traçado),
/// tangente às retas vizinhas onde o traçado é tangente, sem trechos menores que a tolerância de curva curta do Revit e sem
/// sequências de "tracinhos" onde cabe um arco.
/// </summary>
public class AI3BoundaryArcTests
{
    /// <summary>Aresta mínima usada no Revit (ShortCurveTolerance ≈ 0,00256 pé × 1,5, ver MarkingService.ToCurveLoop).</summary>
    public const double MinEdge = 0.00256 * 0.3048 * 1.5;

    private readonly ITestOutputHelper _out;
    public AI3BoundaryArcTests(ITestOutputHelper o) => _out = o;

    private static Vec2 Grid(Vec2 v) => new(Math.Round(v.X, 4), Math.Round(v.Y, 4));

    /// <summary>Retângulo com cantos arredondados (raio r), arcos a cada <paramref name="stepDeg"/>, vértices na grade de 0,1 mm (Clipper).</summary>
    private static List<Vec2> RoundedRect(double w, double h, double r, double stepDeg)
    {
        var pts = new List<Vec2>();
        var corners = new[] { new Vec2(w - r, r), new Vec2(w - r, h - r), new Vec2(r, h - r), new Vec2(r, r) };
        var a0 = new[] { -90.0, 0, 90, 180 };
        var n = (int)Math.Round(90 / stepDeg);
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= n; i++)
                pts.Add(Grid(corners[c] + Vec2.FromAngle((a0[c] + 90.0 * i / n) * Math.PI / 180) * r));
        return pts;
    }

    /// <summary>Direção do trecho na ponta inicial ou final (tangente, nos arcos).</summary>
    public static Vec2 Dir(BoundarySegment s, bool atStart)
    {
        if (!s.IsArc) return (s.End - s.Start).Normalized();
        var (c, _) = s.Circle;
        var sweep = BoundaryFit.Sweep(c, s.Start, s.Mid!.Value, s.End);
        var rad = (atStart ? s.Start : s.End) - c;
        var t = sweep > 0 ? new Vec2(-rad.Y, rad.X) : new Vec2(rad.Y, -rad.X);
        return t.Normalized();
    }

    public static double TurnDeg(Vec2 u, Vec2 v) => Math.Abs(Math.Atan2(u.Cross(v), u.Dot(v))) * 180 / Math.PI;

    /// <summary>Círculo de mínimos quadrados (Kasa) pelos pontos.</summary>
    public static (Vec2 C, double R) Kasa(IReadOnlyList<Vec2> p)
    {
        var mx = p.Average(v => v.X);
        var my = p.Average(v => v.Y);
        double suu = 0, svv = 0, suv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
        foreach (var q in p)
        {
            var u = q.X - mx;
            var v = q.Y - my;
            suu += u * u; svv += v * v; suv += u * v;
            suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
        }
        var det = suu * svv - suv * suv;
        var bu = 0.5 * (suuu + suvv);
        var bv = 0.5 * (svvv + svuu);
        var uc = (bu * svv - bv * suv) / det;
        var vc = (svv == 0 ? 0 : (suu * bv - suv * bu) / det);
        var c = new Vec2(mx + uc, my + vc);
        return (c, p.Average(q => q.DistanceTo(c)));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(3.0)]
    [InlineData(6.0)]
    [InlineData(12.0)]
    [InlineData(25.0)]
    public void Canto_arredondado_vira_um_arco_exato_e_tangente(double r)
    {
        var ring = RoundedRect(80, 60, r, 1);
        var fit = BoundaryFit.Fit(ring, BoundaryFit.DefaultTolerance, MinEdge);
        _out.WriteLine($"R = {r}: {ring.Count} vértices → {fit.Count} trechos ({fit.Count(s => s.IsArc)} arcos)");
        Assert.Equal(4, fit.Count(s => s.IsArc));
        Assert.Equal(8, fit.Count);
        var centers = new[] { new Vec2(80 - r, r), new Vec2(80 - r, 60 - r), new Vec2(r, 60 - r), new Vec2(r, r) };
        foreach (var a in fit.Where(s => s.IsArc))
        {
            var (c, rr) = a.Circle;
            var best = centers.Min(x => x.DistanceTo(c));
            Assert.True(best <= 0.001, $"centro a {best * 1000:0.00} mm");
            Assert.InRange(rr, r - 0.001, r + 0.001);
        }
        for (int i = 0; i < fit.Count; i++)
        {
            var t = TurnDeg(Dir(fit[i], false), Dir(fit[(i + 1) % fit.Count], true));
            Assert.True(t <= 0.05, $"junção {i}: {t:0.000}° (não tangente)");
        }
        Assert.All(fit, s => Assert.True(s.Start.DistanceTo(s.End) >= MinEdge, $"trecho de {s.Start.DistanceTo(s.End) * 1000:0.00} mm"));
    }

    [Fact]
    public void Curva_partida_por_vertice_de_recorte_vira_um_arco_so()
    {
        // Canto R = 8 m amostrado em dois pedaços com passos diferentes (setores da calçada) e um vértice de recorte 0,3 mm fora
        // da curva no meio: continua UM arco.
        var r = 8.0;
        var c = new Vec2(20 - r, r);
        var pts = new List<Vec2> { new(0, 0) };
        for (int i = 0; i <= 25; i++) pts.Add(Grid(c + Vec2.FromAngle((-90 + 40.0 * i / 25) * Math.PI / 180) * r));
        pts.Add(Grid(c + Vec2.FromAngle(-49.5 * Math.PI / 180) * (r + 0.0003)));
        for (int i = 1; i <= 20; i++) pts.Add(Grid(c + Vec2.FromAngle((-50 + 50.0 * i / 20) * Math.PI / 180) * r));
        pts.Add(new Vec2(20, 30));
        pts.Add(new Vec2(0, 30));
        var fit = BoundaryFit.Fit(pts, BoundaryFit.DefaultTolerance, MinEdge);
        _out.WriteLine(string.Join(" | ", fit.Select(s => s.IsArc ? $"arco R={s.Circle.Radius:0.0000}" : $"reta {s.Length:0.000}")));
        var arcs = fit.Where(s => s.IsArc).ToList();
        Assert.Single(arcs);
        var (cc, rr) = arcs[0].Circle;
        Assert.True(cc.DistanceTo(c) <= 0.001, $"centro a {cc.DistanceTo(c) * 1000:0.00} mm");
        Assert.InRange(rr, r - 0.001, r + 0.001);
    }

    public static IEnumerable<object[]> Scenes() => new[]
    {
        new object[] { "cruz", "canteiro", "canteiro", 90.0, 6.0, false },
        new object[] { "cruz", "canteiro", "canteiro", 90.0, 12.0, false },
        new object[] { "T", "canteiro", "local", 75.0, 8.0, false },
        new object[] { "cruz", "local", "local", 120.0, 5.0, false },
        new object[] { "cruz", "coletora", "canteiro", 75.0, 6.0, true },
        new object[] { "cruz", "canteiro", "canteiro", 90.0, 6.0, true },
    };

    /// <summary>Falhas dos contornos ajustados de todos os pisos (unidos e preparados como no Revit) de uma cena.</summary>
    public static List<string> Check(Y34TipTests.World w, Action<string>? log = null)
    {
        var res = new List<string>();
        var parts = Y34TipTests.Geometry(w);
        var floors = parts.Where(p => p.Piece.Solid == null && p.Piece.Profile == null && p.Piece.Round == null && p.Piece.Thickness > 0.002)
            .GroupBy(p => (p.Def, ElementPlan.FloorKey(p.Piece)))
            .SelectMany(g => PolygonOps.Union(g.Select(x => x.Piece.Shape)).Select(sh => (Key: $"{g.Key.Def.DisplayCode}/{g.First().Piece.Layer}", Shape: ElementPlan.Prepare(sh))))
            .ToList();
        var L = w.Nodes[0].Layout;
        // Esquinas com extensão na curva: a face do meio-fio da esquina passou para a frente da extensão (a de um lado só nasce
        // da curva com outro centro, ver AI6) – a curva antiga não existe mais como meio-fio.
        var moved = L.Ears.Where(e => !e.Straight).Select(e => (e.LegA.Road, e.LegA.Sign)).ToHashSet();
        var fillets = L.CornerFillets.Where(kv => !moved.Contains(kv.Key)).Select(kv => kv.Value).Where(f => f.Span < 179 && f.R > 0.3).Select(f =>
        {
            var half = f.Span * Math.PI / 360;
            return (C: f.V + (f.Ua + f.Ub).Normalized() * (f.R / Math.Sin(half)), f.R);
        }).ToList();
        var found = fillets.Select(_ => 0).ToArray();
        // Centros das curvas que o gerador entrega com a geometria (como no Revit, MarkingService.FittedLoop).
        var centers = L.CornerFillets.Values.Where(f => f.Span < 179 && f.R > 0.01)
            .Select(f => f.V + (f.Ua + f.Ub).Normalized() * (f.R / Math.Sin(f.Span * Math.PI / 360)))
            .Concat(L.Ears.SelectMany(e => e.Template.ArcCenters ?? new List<Vec2>())).ToList();
        int arcs = 0, segs = 0;
        foreach (var (key, shape) in floors)
            foreach (var ring in new[] { shape.Outer }.Concat(shape.Holes))
            {
                var fit = BoundaryFit.Fit(ring, BoundaryFit.DefaultTolerance, MinEdge, centers);
                segs += fit.Count;
                arcs += fit.Count(s => s.IsArc);
                var orig = BoundaryFit.Significant(ring);
                for (int i = 0; i < fit.Count; i++)
                {
                    var s = fit[i];
                    var nx = fit[(i + 1) % fit.Count];
                    if (s.Start.DistanceTo(s.End) < MinEdge) res.Add($"{key}: trecho de {s.Start.DistanceTo(s.End) * 1000:0.00} mm");
                    // Junção tangente onde o traçado é liso (giro do contorno original ≤ 1° no ponto).
                    var k = Enumerable.Range(0, orig.Count).OrderBy(j => orig[j].DistanceTo(s.End)).First();
                    var ot = TurnDeg((orig[k] - orig[(k - 1 + orig.Count) % orig.Count]).Normalized(), (orig[(k + 1) % orig.Count] - orig[k]).Normalized());
                    var ft = TurnDeg(Dir(s, false), Dir(nx, true));
                    if ((s.IsArc || nx.IsArc) && ot <= 1.0 && ft > 0.5 && orig[k].DistanceTo(s.End) < 0.002)
                        res.Add($"{key}: junção não tangente em ({s.End.X:0.00};{s.End.Y:0.00}): {ft:0.00}°");
                    // Duas curvas seguidas na mesma circunferência: deviam ser um arco só.
                    if (s.IsArc && nx.IsArc && s.Circle.Center.DistanceTo(nx.Circle.Center) < 0.005 && Math.Abs(s.Circle.Radius - nx.Circle.Radius) < 0.005
                        && Math.Abs(BoundaryFit.Sweep(s.Circle.Center, s.Start, s.Mid!.Value, s.End) + BoundaryFit.Sweep(nx.Circle.Center, nx.Start, nx.Mid!.Value, nx.End)) <= BoundaryFit.MaxSweep)
                        res.Add($"{key}: curva partida em dois arcos em ({s.End.X:0.00};{s.End.Y:0.00})");
                    // Arco concêntrico a uma curva de esquina (centro a até 5 cm do centro dado pelo eixo): faixa de largura
                    // constante (vértices de dentro do arco à mesma distância do centro, ±1 mm) → arco com o centro da curva e o
                    // raio da faixa (±1 mm). Faixas de largura variável (transição entre os perfis das duas vias) ficam de fora.
                    if (s.IsArc)
                    {
                        var (c, r) = s.Circle;
                        var sw = BoundaryFit.Sweep(c, s.Start, s.Mid!.Value, s.End);
                        var a0 = Math.Atan2(s.Start.Y - c.Y, s.Start.X - c.X) + sw * 0.05;
                        foreach (var fc in fillets.Where(fc => fc.C.DistanceTo(c) <= 0.05))
                        {
                            var ds = orig.Where(v => Math.Abs(v.DistanceTo(fc.C) - r) < BoundaryFit.DefaultTolerance && Within(Math.Atan2(v.Y - c.Y, v.X - c.X) - a0, sw * 0.9))
                                .Select(v => v.DistanceTo(fc.C)).OrderBy(x => x).ToList();
                            if (ds.Count < 4 || ds[^1] - ds[0] > 0.001 || Math.Abs(sw) < 10 * Math.PI / 180) continue;
                            var med = ds[ds.Count / 2];
                            if (c.DistanceTo(fc.C) > 0.001 || Math.Abs(r - med) > 0.001)
                                res.Add($"{key}: arco R={r:0.0000} da curva R={fc.R:0.00}: centro a {c.DistanceTo(fc.C) * 1000:0.00} mm, raio a {Math.Abs(r - med) * 1000:0.00} mm da faixa");
                        }
                        for (int f = 0; f < fillets.Count; f++)
                            if (c.DistanceTo(fillets[f].C) <= 0.001 && Math.Abs(r - fillets[f].R) <= 0.001) found[f]++;
                    }
                }
                // Sem "tracinhos": 3 ou mais retas curtas seguidas (< 0,5 m) girando pouco (< 12°) eram uma curva.
                int run = 0;
                for (int i = 0; i < fit.Count * 2; i++)
                {
                    var s = fit[i % fit.Count];
                    var nx = fit[(i + 1) % fit.Count];
                    var smooth = !s.IsArc && !nx.IsArc && s.Length < 0.5 && nx.Length < 0.5 && TurnDeg(Dir(s, false), Dir(nx, true)) is var t && t > 0.05 && t < 12;
                    run = smooth ? run + 1 : 0;
                    if (run == 2) { res.Add($"{key}: tracinhos em ({s.End.X:0.00};{s.End.Y:0.00})"); break; }
                }
            }
        // Curva de cada esquina (face do meio-fio, do eixo): um arco com o centro e o raio dela (±1 mm) em algum piso.
        for (int f = 0; f < fillets.Count; f++)
            if (found[f] == 0) res.Add($"esquina R={fillets[f].R:0.00}: nenhum arco com o centro e o raio da curva (±1 mm)");
        log?.Invoke($"{floors.Count} pisos → {segs} trechos ({arcs} arcos)");
        return res.Distinct().ToList();
    }

    private static bool Within(double d, double sweep)
    {
        while (d <= -Math.PI) d += 2 * Math.PI;
        while (d > Math.PI) d -= 2 * Math.PI;
        if (sweep > 0) { if (d < 0) d += 2 * Math.PI; return d >= -1e-9 && d <= sweep + 1e-9; }
        if (d > 0) d -= 2 * Math.PI;
        return d <= 1e-9 && d >= sweep - 1e-9;
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void Pisos_da_intersecao_com_arcos_exatos_tangentes_e_sem_tracinhos(string kind, string a, string b, double ang, double radius, bool ext)
    {
        var w = AB2Matrix.Make(new IntersectionDefinition
        {
            Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true, CornerRadius = radius, CurbExtensions = ext, CurbExtensionsOpposite = ext,
        }, AB2Matrix.Axes(kind, a, b, ang));
        var f = Check(w, _out.WriteLine);
        foreach (var x in f.Take(40)) _out.WriteLine(x);
        Assert.True(f.Count == 0, $"{f.Count} falhas: " + string.Join(" | ", f.Take(8)));
    }
}
