using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AI, item 1: faixa de pedestres, retenção, "PARE" e R-1 o mais perto possível da ponta da esquina. O fim da curva
/// do meio-fio (ponto de tangência) é medido na geometria do asfalto + sarjeta, não no gerador. Com rampas, elas ficam
/// inteiras no trecho reto e a ponta da rampa (aba ou rampa lateral) fica junto ao fim da curva (até 0,30 m); sem rampas,
/// a borda da faixa fica junto ao fim da curva. A retenção fica logo atrás da faixa (1,60 m livres, padrão do repositório –
/// MANUAL.md, Faixa de Pedestres), o "PARE" logo atrás da retenção (1,60 m livres – ESTUDO-MANUAIS.md, "PARE ≥ 1,60 m antes
/// da retenção") e a R-1 na calçada junto à retenção.
/// </summary>
public class AI1CornerTipTests
{
    /// <summary>Medidas de um ramo com travessia.</summary>
    public sealed record LegTip(string Leg, double Tangent, double Near, double Far, double? StopNear, double? StopFar, double? PareNear,
        Vec2? Sign, double? SignT, double SignLateral, double CurbLateral, double WalkLateral, double Bulge, double TanHi = double.NaN, double TanLo = double.NaN, double Clear = 0, double? RampHi = null, double? RampLo = null, bool AcuteClash = false);

    /// <summary>Pista até a face do meio-fio: asfalto + sarjeta (junto às rampas a sarjeta vira asfalto).</summary>
    public static List<Polygon2> Asphalt(List<Y34TipTests.Part> parts, Vec2 node, double r)
    {
        static bool Gutter(Y34TipTests.Part x) => x.Def is LinearMarkingDefinition l && l.Code.StartsWith("SARJETA") || x.Piece.Layer == "SARJETA"
            || x.Def is IntersectionDefinition && x.Piece.Color == MarkingColor.Concreto && x.Piece.Thickness <= 0.02 && x.Piece.Layer == null;
        var disk = new[] { new Polygon2(CurveTools.Circle(node, r, 0.05)) };
        return PolygonOps.Intersect(PolygonOps.Union(parts.Where(p => p.Piece.Solid == null && p.Piece.Profile == null
            && (p.Piece.Color == MarkingColor.Asfalto || Gutter(p))).Select(p => p.Piece.Shape)), disk);
    }

    /// <summary>Distância do eixo do ramo ao bordo do asfalto do lado <paramref name="side"/> na estaca t (raio de fora para dentro).</summary>
    public static double Edge(IntersectionLayout L, IntersectionLeg leg, List<Polygon2> asphalt, double t, int side, double reach, bool retry = true)
    {
        // O raio começa 3 m além do bordo reto da via (na calçada): de mais longe ele cruzaria a pista da outra via.
        reach = Math.Min(reach, (side > 0 ? L.HiEdge(leg, t) : L.LoEdge(leg, t)) + 3);
        var a = L.At(leg, t, side * reach);
        // Até o meio da pista do outro lado (mão única: o eixo pode ficar no bordo).
        var back = (side > 0 ? L.LoEdge(leg, t) : L.HiEdge(leg, t)) / 2;
        var b = L.At(leg, t, -side * back);
        reach += back;
        var best = double.NaN;
        foreach (var p in asphalt)
            foreach (var iv in DetailGenerator.SegmentIntervals(p, a, b))
                if (double.IsNaN(best) || iv.T0 < best) best = iv.T0;
        // Raio passando exatamente por um vértice: repete 1 cm ao lado.
        if (double.IsNaN(best) && retry) return Edge(L, leg, asphalt, t + 0.0137, side, reach, false);
        return double.IsNaN(best) ? double.NaN : reach * (1 - best) - back;
    }

    /// <summary>
    /// Fim da curva da esquina de um lado do ramo: a partir do bordo reto junto à faixa (valor em <paramref name="tRef"/>),
    /// indo para o nó, os pontos do bordo que se afastam da reta (3 cm a 1,2 m) recebem um círculo por mínimos quadrados; o fim
    /// da curva é a projeção do centro na reta do bordo. Sem curva (lado contínuo do T), NaN.
    /// </summary>
    public static double Tangent(IntersectionLayout L, IntersectionLeg leg, List<Polygon2> asphalt, double tRef, int side)
    {
        const double reach = 40;
        var o0 = Edge(L, leg, asphalt, tRef, side, reach);
        if (double.IsNaN(o0)) return double.NaN;
        var pts = new List<(double X, double Y)>();
        var straight = tRef;
        var still = true;
        for (var t = tRef; t > Math.Max(-6, leg.Clear - 12); t -= 0.02)
        {
            var o = Edge(L, leg, asphalt, t, side, reach);
            if (double.IsNaN(o)) break;
            var dlt = Math.Abs(o - o0);
            if (still && dlt < 0.003) straight = t; else still = false;
            if (dlt > 1.2) break;
            if (dlt > 0.03) pts.Add((t, dlt));
        }
        if (pts.Count < 6) return still ? double.NaN : straight;
        // Círculo de Kasa: x² + y² + D x + E y + F = 0 (mínimos quadrados lineares).
        double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sxz = 0, syz = 0, sz = 0;
        foreach (var (x, y) in pts)
        {
            var z = x * x + y * y;
            sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y; sxz += x * z; syz += y * z; sz += z;
        }
        var n = pts.Count;
        var m = new[,] { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
        var r = new[] { -sxz, -syz, -sz };
        static double Det(double[,] a) => a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0]) + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
        var det = Det(m);
        if (Math.Abs(det) < 1e-12) return straight;
        double Solve(int col)
        {
            var c = (double[,])m.Clone();
            for (int k = 0; k < 3; k++) c[k, col] = r[k];
            return Det(c) / det;
        }
        var xc = -Solve(0) / 2;
        // Arco que não chega a ser círculo (bordo que mal se afasta da reta): fica o último ponto reto.
        var res = pts.Max(p => Math.Abs(Math.Sqrt((p.X - xc) * (p.X - xc) + (p.Y + Solve(1) / 2) * (p.Y + Solve(1) / 2)) - Math.Sqrt(xc * xc + Solve(1) * Solve(1) / 4 - Solve(2))));
        return res > 0.03 || double.IsNaN(xc) ? straight : Math.Min(xc, straight + 0.30);
    }

    /// <summary>Maior afastamento do bordo (dos dois lados) em relação ao bordo reto em <paramref name="tRef"/>, entre t0 e t1.</summary>
    public static double Bulge(IntersectionLayout L, IntersectionLeg leg, List<Polygon2> asphalt, double tRef, double t0, double t1)
    {
        var max = 0.0;
        foreach (var s in new[] { 1, -1 })
        {
            var o0 = Edge(L, leg, asphalt, tRef, s, 40);
            if (double.IsNaN(o0)) continue;
            for (var t = t0; t <= t1 + 1e-9; t += 0.05)
                if (Edge(L, leg, asphalt, t, s, 40) is var o && !double.IsNaN(o)) max = Math.Max(max, Math.Abs(o - o0));
        }
        return max;
    }

    private static double T(IntersectionLayout L, IntersectionLeg leg, Vec2 p) => (p - L.At(leg, 0, 0)).Dot(leg.Dir);
    private static double Lat(IntersectionLayout L, IntersectionLeg leg, Vec2 p) => (p - L.At(leg, 0, 0)).Dot(leg.Dir.PerpLeft);

    /// <summary>Faixa, retenção, PARE e R-1 de cada ramo com travessia.</summary>
    public static List<LegTip> Measure(Y34TipTests.World w)
    {
        var parts = Y34TipTests.Geometry(w);
        var res = new List<LegTip>();
        foreach (var n in w.Nodes)
        {
            var L = n.Layout;
            var d = L.Definition!;
            var asphalt = Asphalt(parts, L.Node, Math.Max(45, L.Radius + 20));
            var kids = w.Defs.Where(x => x.GroupId == d.Id).ToList();
            foreach (var leg in L.Legs.Where(l => IntersectionGenerator.CrosswalkOn(d, L, l)))
            {
                var r = L.Roads[leg.Road].Def;
                var half = Math.Max(r.TotalLeft, r.TotalRight) + 1;
                bool OnLeg(Vec2 p, double t0, double t1) => T(L, leg, p) is var t && t >= t0 && t <= t1 && Math.Abs(Lat(L, leg, p)) <= half;
                // Faixa pintada (barras) deste ramo.
                var cw = parts.Where(p => p.Def.GroupId == d.Id && p.Def is LinearMarkingDefinition l && l.Code.StartsWith("FTP")
                    && OnLeg(p.Piece.Shape.Centroid, leg.Clear - 3, leg.Clear + 30)).SelectMany(p => p.Piece.Shape.Outer).ToList();
                if (cw.Count == 0) continue;
                var near = cw.Min(v => T(L, leg, v));
                var far = cw.Max(v => T(L, leg, v));
                var tRef = Math.Min(near + 1.0, far);
                var tHi = Tangent(L, leg, asphalt, tRef, 1);
                var tLo = Tangent(L, leg, asphalt, tRef, -1);
                var tan = double.IsNaN(tHi) ? tLo : double.IsNaN(tLo) ? tHi : Math.Max(tHi, tLo);
                // Retenção (LRE) e PARE deste ramo.
                var lre = parts.Where(p => p.Def.GroupId == d.Id && p.Def is LinearMarkingDefinition { Code: "LRE" } && OnLeg(p.Piece.Shape.Centroid, far, far + 6))
                    .SelectMany(p => p.Piece.Shape.Outer).ToList();
                var pare = parts.Where(p => p.Def.GroupId == d.Id && p.Def is TextMarkingDefinition { Text: "PARE" } && OnLeg(p.Piece.Shape.Centroid, far, far + 12))
                    .SelectMany(p => p.Piece.Shape.Outer).ToList();
                var sign = kids.OfType<SignDefinition>().Where(s => s.Code == "R-1" && OnLeg(s.Position, far, far + 6)).Select(s => (Vec2?)s.Position).FirstOrDefault();
                var curbLat = L.HiEdge(leg, far + 2) - IntersectionGenerator.EarInset(L, leg, 1, far + 2);
                // Ponta da rampa do lado alto e do baixo voltada para o nó (contorno com as abas).
                double? RampNear(int side)
                {
                    var rs = kids.OfType<RampDefinition>().Where(r => r.PathRef.Points.Count >= 2 && OnLeg(r.PathRef.Points[0], near - 6, far + 6)
                        && Math.Sign(Lat(L, leg, r.PathRef.Points[0])) == side).ToList();
                    if (rs.Count == 0) return null;
                    return rs.Min(r => RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points)).Outer.Min(v => T(L, leg, v)));
                }
                var walk = leg.Sign > 0 ? r.LeftSidewalk : r.RightSidewalk;
                res.Add(new LegTip($"via {leg.Road}{(leg.Sign > 0 ? "+" : "−")}", tan, near, far,
                    lre.Count > 0 ? lre.Min(v => T(L, leg, v)) : null, lre.Count > 0 ? lre.Max(v => T(L, leg, v)) : null,
                    pare.Count > 0 ? pare.Min(v => T(L, leg, v)) : null,
                    sign, sign is { } sp ? T(L, leg, sp) : null, sign is { } sq ? Math.Abs(Lat(L, leg, sq)) : 0, curbLat, curbLat + walk,
                    Bulge(L, leg, asphalt, tRef, near, far), tHi, tLo, L.ClearCap.TryGetValue((leg.Road, leg.Sign), out var cap) ? Math.Min(leg.Clear, cap) : leg.Clear, RampNear(1), RampNear(-1),
                    L.Warnings.Any(x => x.Contains("esquina aguda") && x.Contains("recuadas"))));
            }
        }
        return res;
    }

    /// <summary>Falhas de um caso: faixa fora da ponta, retenção/PARE/R-1 fora do lugar.</summary>
    public static List<string> Check(Y34TipTests.World w)
    {
        var res = new List<string>();
        foreach (var m in Measure(w))
        {
            if (double.IsNaN(m.Tangent) && m.Clear <= 0) { res.Add($"{m.Leg}: fim da curva não medido"); continue; }
            // Referência: o fim da curva, mas nunca antes do ponto em que o ramo sai da pista das outras vias.
            double Ref(double t) => double.IsNaN(t) ? m.Clear : Math.Max(t, m.Clear);
            // Dentro da curva: o bordo da pista ao longo da faixa se afasta do bordo reto.
            if (m.Bulge > 0.02) res.Add($"{m.Leg}: faixa dentro da curva (bordo {m.Bulge:0.00} m fora do alinhamento)");
            var gaps = new List<double>();
            foreach (var (tip, tan, side) in new[] { (m.RampHi, m.TanHi, "alto"), (m.RampLo, m.TanLo, "baixo") })
            {
                if (tip is not { } tp || double.IsNaN(tan)) continue;
                var g = tp - Ref(tan);
                // Rampa inteira no trecho reto (a face do meio-fio na ponta da aba ainda é reta).
                if (g < -0.05) res.Add($"{m.Leg}: rampa do lado {side} entra {-g:0.00} m na curva");
                gaps.Add(g);
            }
            if (gaps.Count > 0)
            {
                // Com rampas: a do lado que comanda a posição da faixa termina junto ao fim da curva.
                if (gaps.Min() > 0.30 && !m.AcuteClash) res.Add($"{m.Leg}: rampa a {gaps.Min():0.00} m do fim da curva");
            }
            else
            {
                var dist = m.Near - Ref(m.Tangent);
                if (dist > 0.30) res.Add($"{m.Leg}: faixa a {dist:0.00} m do fim da curva");
            }
            if (m.StopNear is { } s0)
            {
                var gap = s0 - m.Far;
                if (Math.Abs(gap - 1.60) > 0.05) res.Add($"{m.Leg}: retenção a {gap:0.00} m da faixa (esperado 1,60 m)");
                if (m.PareNear is { } p0 && Math.Abs(p0 - m.StopFar!.Value - 1.60) > 0.10)
                    res.Add($"{m.Leg}: PARE a {p0 - m.StopFar!.Value:0.00} m da retenção (esperado 1,60 m)");
                if (m.Sign == null) res.Add($"{m.Leg}: sem R-1 junto à retenção");
                else
                {
                    if (Math.Abs(m.SignT!.Value - (s0 + m.StopFar!.Value) / 2) > 0.6) res.Add($"{m.Leg}: R-1 a {m.SignT - s0:0.00} m da retenção");
                    if (m.SignLateral < m.CurbLateral + 0.30 || m.SignLateral > m.WalkLateral + 0.01) res.Add($"{m.Leg}: R-1 fora da calçada (lateral {m.SignLateral:0.00} m, meio-fio {m.CurbLateral:0.00} m)");
                }
            }
        }
        return res;
    }

    private static IntersectionDefinition Tpl(bool ext = false, bool opposite = true) => new()
    {
        Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true, CurbExtensions = ext, CurbExtensionsOpposite = ext && opposite,
    };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in new[] { "cruz", "T" })
            foreach (var (a, b) in AB2Matrix.Pairs)
                foreach (var ang in new[] { 60.0, 75, 90, 105, 120 })
                    yield return new object[] { kind, a, b, ang };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Crosswalk_StopLine_Pare_AndR1_AtTheCornerTip(string kind, string a, string b, double ang)
    {
        var w = AB2Matrix.Make(Tpl(), AB2Matrix.Axes(kind, a, b, ang));
        Assert.NotEmpty(Measure(w));
        Assert.Empty(Check(w));
    }

    [Theory]
    [InlineData("cruz", "canteiro", "canteiro", 90.0)]
    [InlineData("cruz", "canteiro", "canteiroLargo", 90.0)]
    [InlineData("cruz", "coletora", "canteiro", 75.0)]
    [InlineData("T", "canteiro", "local", 90.0)]
    [InlineData("cruz", "local", "local", 90.0)]
    public void WithCurbExtensions_TheCrosswalkStaysAtTheTip(string kind, string a, string b, double ang)
    {
        var w = AB2Matrix.Make(Tpl(ext: true), AB2Matrix.Axes(kind, a, b, ang));
        Assert.NotEmpty(Measure(w));
        Assert.Empty(Check(w));
    }

    /// <summary>Print 4: cruzamento de avenidas com canteiro central e extensões dos dois lados.</summary>
    [Fact]
    public void AvenueCrossing_WithMedianAndExtensions_AllFourCrosswalksAtTheTips()
    {
        var w = AB2Matrix.Make(Tpl(ext: true), AB2Matrix.Axes("cruz", "canteiro", "canteiro", 90));
        var m = Measure(w);
        Assert.Equal(4, m.Count);
        Assert.All(m, x => Assert.NotNull(x.RampHi ?? x.RampLo));
        Assert.Empty(Check(w));
    }
}
