using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Trecho do meio-fio externo da rotatória entre dois ramos vizinhos: da boca de um ramo, pela concordância (e pelo
/// by-pass), até a boca do ramo seguinte. A calçada ao longo dele passa da largura do lado do primeiro ramo (peso 0) para
/// a do lado do segundo (peso 1). Nos trechos retos junto aos ramos o peso fica em 0 ou 1: a calçada tem ali a largura da
/// via e emenda nela sem degrau.
/// </summary>
/// <param name="Normals">Normais unitárias para fora da pista, uma por ponto.</param>
/// <param name="Widths">Largura da calçada além do meio-fio em cada ponto: a passagem de um ramo ao outro e, junto às rampas das
/// travessias, o alargamento que a rampa e a faixa livre pedem.</param>
/// <param name="FromSide">Lado do ramo inicial (+1 = esquerda de quem sai da rotatória por ele; −1 = direita; 0 = sem ramo).</param>
/// <param name="WidthFrom">Largura da calçada além do meio-fio na ponta do ramo inicial.</param>
public sealed record RoundaboutWalkChain(List<Vec2> Points, List<Vec2> Normals, List<double> Weights, List<double> Widths,
    RoundaboutLegGeometry? From, int FromSide, RoundaboutLegGeometry? To, int ToSide, double WidthFrom, double WidthTo)
{
    /// <summary>Largura da calçada além do meio-fio no ponto <paramref name="i"/>.</summary>
    public double WidthAt(int i) => Widths[i];
    public double MaxWidth => Widths.Max();
}

/// <summary>Rampa de travessia que pede calçada mais larga que a da via: ponto no meio-fio, meia extensão ao longo dele e largura além do meio-fio.</summary>
public sealed record RoundaboutRampNeed(Vec2 Curb, double HalfExtent, double Width);

public static partial class RoundaboutGenerator
{
    /// <summary>
    /// Faixa livre que precisa sobrar na calçada atrás da rampa da travessia (m): o mínimo de 1,20 m que as interseções já
    /// usam (<see cref="IntersectionDefinition.RampFreeWidth"/>, NBR 9050).
    /// </summary>
    public const double FaixaLivreRampa = 1.20;

    /// <summary>
    /// Comprimento mínimo (m) em que a calçada alargada junto à rampa volta à largura da via, de cada lado; cresce com o
    /// alargamento (4 m por metro alargado) – padrão do plugin.
    /// </summary>
    public const double TransicaoAlargamento = 4.0;

    /// <summary>
    /// Rampas das travessias cuja calçada (a da via daquele lado) não comporta a rampa e a faixa livre de 1,20 m: a calçada da
    /// rotatória alarga junto a elas (a travessia fica a um veículo da entrada; o rebaixamento total, com 7 a 8 m de meio-fio,
    /// não cabe no meio-fio curvo da entrada). Acrescenta um aviso por rampa.
    /// </summary>
    internal static List<RoundaboutRampNeed> RampNeeds(RoundaboutDefinition d, RoundaboutLayout L, Vec2 c, double cw)
    {
        var res = new List<RoundaboutRampNeed>();
        if (!d.SidewalkPerLeg || d.Integration != IntegracaoRotatoria.Completa) return res;
        foreach (var g in L.Legs)
        {
            if (!(g.Leg.Crosswalk ?? d.Crosswalks)) continue;
            foreach (var (sg, frame) in new[] { (1, g.RampHi), (-1, g.RampLo) })
            {
                if (frame is not { } f || !HasWalk(d, g.Leg, sg)) continue;
                var rp = RampOf(d, g.RampType);
                var (_, run, _) = RampGenerator.Dimensions(rp);
                var need = run + rp.LandingDepth + FaixaLivreRampa;
                var walk = WalkTotal(d, g.Leg, sg);
                if (walk >= need - 0.01) continue;
                res.Add(new RoundaboutRampNeed(f.Curb, IntersectionGenerator.RampHalfExtent(rp), need - cw));
                L.Warnings.Add($"Ramo a {g.Leg.AngleDeg:0}°: calçada de {walk:0.00} m ({(sg > 0 ? "à esquerda" : "à direita")} de quem sai) não comporta a rampa " +
                               $"da travessia e a faixa livre de {FaixaLivreRampa:0.00} m (NBR 9050) – a calçada da rotatória alarga para {need:0.00} m junto " +
                               $"à rampa e volta à largura da via antes da boca.");
            }
        }
        return res;
    }

    /// <summary>
    /// Largura da calçada (com o meio-fio) do lado <paramref name="side"/> do ramo (+1 = esquerda de quem sai da rotatória
    /// pelo ramo, −1 = direita): a da via ligada naquele lado (composição lida da via, quando a rotatória segue a seção das
    /// vias, ou a largura da calçada da via); sem via, ou com a opção desligada, a calçada geral da rotatória.
    /// </summary>
    public static double WalkTotal(RoundaboutDefinition d, RoundaboutLeg leg, int side)
    {
        var cw = Math.Max(0.05, d.CurbWidth);
        var general = Math.Max(0, d.SidewalkWidth) + cw;
        if (!d.SidewalkPerLeg) return general;
        var prof = side > 0 ? leg.EdgeLeft : leg.EdgeRight;
        if (d.MatchRoadSection && prof.Any(b => !b.Inside)) return EdgeProfile.OuterWidth(prof);
        return (side > 0 ? leg.SidewalkLeft : leg.SidewalkRight) is { } v ? Math.Max(0, v) : general;
    }

    /// <summary>Largura da calçada além do meio-fio do lado <paramref name="side"/> do ramo.</summary>
    public static double WalkBeyondCurb(RoundaboutDefinition d, RoundaboutLeg leg, int side) =>
        Math.Max(0, WalkTotal(d, leg, side) - Math.Max(0.05, d.CurbWidth));

    /// <summary>Maior largura de calçada além do meio-fio em volta da rotatória.</summary>
    public static double MaxWalk(RoundaboutDefinition d) =>
        d.Legs.Select(l => Math.Max(WalkBeyondCurb(d, l, 1), WalkBeyondCurb(d, l, -1))).Append(Math.Max(0, d.SidewalkWidth)).Max();

    /// <summary>O ramo tem calçada (de algum lado) para receber as rampas da travessia.</summary>
    internal static bool HasWalk(RoundaboutDefinition d, RoundaboutLeg leg) =>
        d.SidewalkPerLeg ? WalkBeyondCurb(d, leg, 1) > 0.5 || WalkBeyondCurb(d, leg, -1) > 0.5 : leg.Sidewalk > 0.5 || d.SidewalkWidth > 0.5;

    /// <summary>O lado <paramref name="side"/> do ramo tem calçada para a rampa da travessia.</summary>
    internal static bool HasWalk(RoundaboutDefinition d, RoundaboutLeg leg, int side) =>
        d.SidewalkPerLeg ? WalkBeyondCurb(d, leg, side) > 0.5 : leg.Sidewalk > 0.5 || d.SidewalkWidth > 0.5;

    /// <summary>
    /// Trechos do meio-fio externo entre as bocas dos ramos: o bordo da pista <paramref name="full"/> fora do limite da
    /// zona, de um ramo ao seguinte, com as normais para fora e o peso da passagem de um ramo ao outro. As pontas avançam
    /// 1 m além da boca (as faixas chegam inteiras ao limite da zona, que é curvo).
    /// </summary>
    internal static List<RoundaboutWalkChain> WalkChains(RoundaboutDefinition d, RoundaboutLayout L, IReadOnlyList<Polygon2> full, Vec2 c,
        IReadOnlyList<RoundaboutRampNeed>? needs = null)
    {
        var res = new List<RoundaboutWalkChain>();
        if (full.Count == 0 || L.Legs.Count == 0) return res;
        var zoneInner = PolygonOps.Offset(new[] { L.Zone }, -0.05, true);
        bool Mouth(Vec2 p) => !zoneInner.Any(z => z.Contains(p));
        foreach (var pg in full)
        {
            var raw = pg.Outer;
            var ring = new List<Vec2>();
            for (int i = 0; i < raw.Count; i++)
            {
                var p0 = raw[i];
                var p1 = raw[(i + 1) % raw.Count];
                var k = Math.Max(1, (int)Math.Ceiling(p0.DistanceTo(p1) / 0.25));
                for (int j = 0; j < k; j++) ring.Add(p0 + (p1 - p0) * (j / (double)k));
            }
            var n = ring.Count;
            var mouth = ring.Select(Mouth).ToArray();
            if (mouth.All(x => !x) || mouth.All(x => x)) continue;
            var s0 = Array.FindIndex(mouth, x => x);
            var run = new List<Vec2>();
            for (int q = 1; q <= n; q++)
            {
                var i = (s0 + q) % n;
                if (!mouth[i]) { run.Add(ring[i]); continue; }
                if (run.Count >= 2)
                {
                    // Das bocas: o último ponto da boca anterior e o primeiro da seguinte (no meio-fio, sobre o limite da zona).
                    var pts = new List<Vec2> { ring[(i - run.Count - 1 + n) % n] };
                    pts.AddRange(run);
                    pts.Add(ring[i]);
                    if (Chain(d, L, c, pts, full, needs ?? Array.Empty<RoundaboutRampNeed>()) is { } ch) res.Add(ch);
                }
                run = new List<Vec2>();
            }
        }
        return res;
    }

    private static RoundaboutWalkChain? Chain(RoundaboutDefinition d, RoundaboutLayout L, Vec2 c, List<Vec2> pts, IReadOnlyList<Polygon2> full,
        IReadOnlyList<RoundaboutRampNeed> needs)
    {
        // Sem "espinhos" (o contorno vai e volta sobre a mesma reta): dobrariam a calçada.
        for (var again = true; again && pts.Count >= 3;)
        {
            again = false;
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                var u0 = pts[i] - pts[i - 1];
                var u1 = pts[i + 1] - pts[i];
                if (u0.Length < 1e-6 || u1.Length < 1e-6 || u0.Dot(u1) < -0.5 * u0.Length * u1.Length)
                {
                    pts.RemoveAt(i);
                    again = true;
                    break;
                }
            }
        }
        if (pts.Count < 3) return null;
        // Linha do meio-fio alisada (1 cm) e redistribuída a cada 0,25 m: os degraus de milímetros que as operações booleanas
        // deixam no contorno da pista viravam, a 3 m de afastamento, dentes de vários centímetros na borda da calçada.
        var rdp = Clipper2Lib.Clipper.RamerDouglasPeucker(new Clipper2Lib.PathD(pts.Select(p => new Clipper2Lib.PointD(p.X, p.Y))), 0.01);
        if (rdp.Count >= 2)
        {
            var dense = new List<Vec2>();
            for (int i = 0; i + 1 < rdp.Count; i++)
            {
                var a = new Vec2(rdp[i].x, rdp[i].y);
                var b = new Vec2(rdp[i + 1].x, rdp[i + 1].y);
                var k = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / 0.25));
                for (int j = 0; j < k; j++) dense.Add(a + (b - a) * (j / (double)k));
            }
            dense.Add(new Vec2(rdp[^1].x, rdp[^1].y));
            if (dense.Count >= 3) pts = dense;
        }
        // Ramo e lado de cada ponta: o bordo reto do ramo (afastamento lateral = meia largura da pista) que passa por ela.
        (RoundaboutLegGeometry? G, int Side) EndOf(Vec2 p)
        {
            RoundaboutLegGeometry? best = null;
            var side = 0;
            var err = 0.6;
            foreach (var g in L.Legs)
            {
                if ((p - c).Dot(g.Dir) <= 0) continue;
                var o = (p - c).Dot(g.Left);
                var e = Math.Abs(Math.Abs(o) - Math.Max(3, g.Leg.Width) / 2);
                if (e < err) { err = e; best = g; side = Math.Sign(o); }
            }
            return (best, side);
        }
        var (ga, sa) = EndOf(pts[0]);
        var (gb, sb) = EndOf(pts[^1]);
        double Width(RoundaboutLegGeometry? g, int side) => g == null || side == 0 ? Math.Max(0, d.SidewalkWidth) : WalkBeyondCurb(d, g.Leg, side);
        // Trecho reto junto a cada ramo (sobre o bordo do ramo): fica com a largura da via daquele lado.
        bool OnEdge(RoundaboutLegGeometry? g, int side, Vec2 p) =>
            g != null && side != 0 && (p - c).Dot(g.Dir) > 0 && Math.Abs((p - c).Dot(g.Left) - side * Math.Max(3, g.Leg.Width) / 2) < 0.05;
        var ka = 0;
        while (ka + 1 < pts.Count && OnEdge(ga, sa, pts[ka + 1])) ka++;
        var kb = pts.Count - 1;
        while (kb - 1 >= 0 && OnEdge(gb, sb, pts[kb - 1])) kb--;
        // Pontas prolongadas 1 m na tangente, para além do limite da zona.
        var e0 = (pts[0] - pts[1]).Normalized();
        var e1 = (pts[^1] - pts[^2]).Normalized();
        pts.Insert(0, pts[0] + e0 * 1.0);
        pts.Add(pts[^1] + e1 * 1.0);
        ka++;
        kb++;
        var acc = new List<double> { 0 };
        for (int i = 1; i < pts.Count; i++) acc.Add(acc[^1] + pts[i].DistanceTo(pts[i - 1]));
        var w = new List<double>();
        for (int i = 0; i < pts.Count; i++)
        {
            double u;
            if (kb > ka + 1) u = Math.Clamp((acc[i] - acc[ka]) / Math.Max(1e-6, acc[kb] - acc[ka]), 0, 1);
            else u = acc[i] / Math.Max(1e-6, acc[^1]);
            w.Add(u * u * (3 - 2 * u));
        }
        var normals = new List<Vec2>();
        for (int i = 0; i < pts.Count; i++)
        {
            var t = (pts[Math.Min(pts.Count - 1, i + 1)] - pts[Math.Max(0, i - 1)]).Normalized();
            normals.Add(new Vec2(t.Y, -t.X));
        }
        // Normais para fora da pista (o sentido do contorno depende da orientação do polígono).
        var mid = pts.Count / 2;
        var probe = pts[mid] + normals[mid] * 0.1;
        if (full.Any(p => p.Contains(probe))) normals = normals.Select(v => v * -1).ToList();
        var (wa, wb) = (Width(ga, sa), Width(gb, sb));
        var widths = w.Select(x => wa + (wb - wa) * x).ToList();
        // Junto às rampas que pedem mais calçada: a largura delas ao longo da rampa e das abas, com transição suave.
        foreach (var nd in needs)
        {
            var k = Enumerable.Range(0, pts.Count).OrderBy(i => pts[i].DistanceTo(nd.Curb)).First();
            if (pts[k].DistanceTo(nd.Curb) > 0.6) continue;
            // Transição de pelo menos 4 m, mais longa quanto maior o alargamento (no máximo ~1:2,7 na borda da calçada).
            var taper = Math.Max(TransicaoAlargamento, 4.0 * Math.Max(0, nd.Width - widths[k]));
            for (int i = 0; i < pts.Count; i++)
            {
                var dist = Math.Abs(acc[i] - acc[k]);
                var f = dist <= nd.HalfExtent ? 1.0 : dist >= nd.HalfExtent + taper ? 0.0 : 1 - (dist - nd.HalfExtent) / taper;
                f = f * f * (3 - 2 * f);
                if (nd.Width > widths[i]) widths[i] += (nd.Width - widths[i]) * f;
            }
        }
        return new RoundaboutWalkChain(pts, normals, w, widths, ga, sa, gb, sb, wa, wb);
    }

    /// <summary>
    /// Calçada além do meio-fio ao longo dos trechos entre ramos, com a largura de cada ponto (da via de um ramo à do
    /// seguinte e o alargamento junto às rampas), dentro da zona.
    /// </summary>
    internal static List<Polygon2> VariableWalk(IReadOnlyList<RoundaboutWalkChain> chains, IReadOnlyList<Polygon2> full, double cw, IReadOnlyList<Polygon2> zone)
    {
        if (chains.Count == 0) return new();
        var max = chains.Max(ch => ch.MaxWidth);
        var inner = PolygonOps.Offset(full, cw, true);
        // Todas da mesma largura: a faixa constante (arcos limpos, como antes).
        if (chains.All(ch => ch.Widths.All(x => Math.Abs(x - max) < 1e-3)))
            return PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(full, cw + max, true), inner), zone).Where(p => p.Area > 0.01).ToList();
        var outer = new List<Polygon2>();
        foreach (var ch in chains)
            outer.AddRange(VarOffset(ch, ch.Widths.Select(x => cw + x).ToList()));
        return PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Union(outer), inner), zone).Where(p => p.Area > 0.01).ToList();
    }

    /// <summary>
    /// Pontos até a distância <paramref name="r"/> (por ponto) da linha do meio-fio, do lado de fora: a união de "cápsulas"
    /// (envoltória de dois círculos) entre pontos vizinhos – o deslocamento verdadeiro com raio variável, sem as pontas que o
    /// afastamento ponto a ponto deixa nas curvas e nos vértices da linha – limitada ao lado de fora do trecho (não atravessa
    /// a pista de um ramo estreito para o outro lado).
    /// </summary>
    private static List<Polygon2> VarOffset(RoundaboutWalkChain ch, IReadOnlyList<double> r)
    {
        var keep = Rdp3(ch.Points, r, 0.005);
        var caps = new List<Polygon2>();
        for (int k = 0; k + 1 < keep.Count; k++)
        {
            var (ia, ib) = (keep[k], keep[k + 1]);
            if (r[ia] < 1e-4 && r[ib] < 1e-4) continue;
            caps.Add(Capsule(ch.Points[ia], Math.Max(1e-4, r[ia]), ch.Points[ib], Math.Max(1e-4, r[ib])));
        }
        if (caps.Count == 0) return new();
        var side = EdgeProfile.SweptWinding(ch.Points, ch.Normals, ch.Points.Select(_ => r.Max() + 0.5).ToList(), 1);
        return PolygonOps.Intersect(PolygonOps.Union(caps), side);
    }

    /// <summary>Envoltória convexa de dois círculos (centro e raio): a cápsula afunilada entre eles.</summary>
    private static Polygon2 Capsule(Vec2 a, double ra, Vec2 b, double rb)
    {
        var pts = new List<Vec2>();
        foreach (var (c, rr) in new[] { (a, ra), (b, rb) })
        {
            var n = Math.Clamp((int)Math.Ceiling(2 * Math.PI * rr / 0.12), 16, 200);
            for (int i = 0; i < n; i++)
            {
                var t = 2 * Math.PI * i / n;
                pts.Add(c + new Vec2(Math.Cos(t), Math.Sin(t)) * rr);
            }
        }
        return new Polygon2(Hull(pts));
    }

    /// <summary>Envoltória convexa (cadeia monótona de Andrew), no sentido anti-horário.</summary>
    private static List<Vec2> Hull(List<Vec2> pts)
    {
        var p = pts.OrderBy(v => v.X).ThenBy(v => v.Y).ToList();
        if (p.Count < 3) return p;
        static double Cross(Vec2 o, Vec2 a, Vec2 b) => (a - o).Cross(b - o);
        var h = new List<Vec2>();
        foreach (var v in p)
        {
            while (h.Count >= 2 && Cross(h[^2], h[^1], v) <= 0) h.RemoveAt(h.Count - 1);
            h.Add(v);
        }
        var lower = h.Count + 1;
        for (int i = p.Count - 2; i >= 0; i--)
        {
            var v = p[i];
            while (h.Count >= lower && Cross(h[^2], h[^1], v) <= 0) h.RemoveAt(h.Count - 1);
            h.Add(v);
        }
        h.RemoveAt(h.Count - 1);
        return h;
    }

    /// <summary>Simplificação (Ramer–Douglas–Peucker) da linha com a largura como terceira coordenada: devolve os índices mantidos.</summary>
    private static List<int> Rdp3(IReadOnlyList<Vec2> p, IReadOnlyList<double> w, double tol)
    {
        var keep = new bool[p.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, p.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b - a < 2) continue;
            var (ax, ay, az) = (p[a].X, p[a].Y, w[a]);
            var (dx, dy, dz) = (p[b].X - ax, p[b].Y - ay, w[b] - az);
            var len2 = dx * dx + dy * dy + dz * dz;
            var best = -1.0;
            var at = -1;
            for (int i = a + 1; i < b; i++)
            {
                var (ex, ey, ez) = (p[i].X - ax, p[i].Y - ay, w[i] - az);
                var u = len2 < 1e-12 ? 0 : Math.Clamp((ex * dx + ey * dy + ez * dz) / len2, 0, 1);
                var (fx, fy, fz) = (ex - u * dx, ey - u * dy, ez - u * dz);
                var dist = Math.Sqrt(fx * fx + fy * fy + fz * fz);
                if (dist > best) { best = dist; at = i; }
            }
            if (best > tol && at > 0) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
        }
        return Enumerable.Range(0, p.Count).Where(i => keep[i]).ToList();
    }

    /// <summary>
    /// Composição das calçadas por ramo: o meio-fio é o anel do meio-fio da rotatória; além dele, ao longo de cada trecho entre
    /// ramos, as faixas (grama, passeio...) passam do perfil do lado do primeiro ramo para o do segundo, como nas esquinas das
    /// interseções; a sobra da calçada (alargamento junto às rampas, calçada mais larga que o perfil) fica com a faixa externa
    /// do ramo mais próximo. A sarjeta segue o bordo da pista dentro da zona com o perfil geral. Devolve as peças e as áreas
    /// dentro da pista (sarjeta), a tirar do pavimento.
    /// </summary>
    internal static (List<MarkingPiece> Pieces, List<Polygon2> Inside) ApplyPerLeg(RoundaboutDefinition d, RoundaboutLayout L, IReadOnlyList<Polygon2> region,
        List<EdgeBand> fallback)
    {
        var pieces = new List<MarkingPiece>();
        var inside = new List<Polygon2>();
        var cw = Math.Max(0.05, d.CurbWidth);
        List<EdgeBand> Side(RoundaboutLegGeometry? g, int side)
        {
            if (g == null || side == 0 || !d.SidewalkPerLeg) return fallback;
            var p = side > 0 ? g.Leg.EdgeLeft : g.Leg.EdgeRight;
            return p.Count > 0 ? p : fallback;
        }
        static bool IsCurb(EdgeBand b) => b.Code.StartsWith("MEIO-FIO");
        var zone = new[] { L.Zone };
        // Meio-fio: o anel da rotatória (largura constante), com os atributos do meio-fio das vias.
        var curbAttr = d.Legs.SelectMany(l => l.EdgeLeft.Concat(l.EdgeRight)).Concat(fallback).FirstOrDefault(b => !b.Inside && IsCurb(b));
        var curb = PolygonOps.Intersect(region, L.Curb).Where(p => p.Area > 1e-3).ToList();
        foreach (var sh in curb)
            pieces.Add(new MarkingPiece(sh, curbAttr?.Color ?? MarkingColor.Concreto) { Thickness = curbAttr?.Thickness ?? d.CurbHeight, Elevation = curbAttr?.Elevation ?? 0, Layer = "MEIO-FIO" });
        var walk = PolygonOps.Difference(region, L.Curb).Where(p => p.Area > 1e-3).ToList();
        var done = new List<Polygon2>();
        foreach (var ch in L.WalkChains)
        {
            var a = Side(ch.From, ch.FromSide).Where(b => !b.Inside).ToList();
            var b = Side(ch.To, ch.ToSide).Where(b => !b.Inside).ToList();
            var reach = Math.Max(Math.Max(EdgeProfile.OuterWidth(a), EdgeProfile.OuterWidth(b)), cw + ch.MaxWidth) + 0.5;
            var area = PolygonOps.Intersect(EdgeProfile.SweptWinding(ch.Points, ch.Normals, ch.Points.Select(_ => reach).ToList(), 1), zone);
            var clip = PolygonOps.Intersect(walk, area).Where(p => p.Area > 1e-3).ToList();
            if (clip.Count == 0) continue;
            done.AddRange(clip);
            // Faixas além do meio-fio (o meio-fio já é o anel): limites pelo deslocamento verdadeiro de cada ponto.
            var covered = new List<Polygon2>();
            var acc = ch.Points.Select(_ => cw).ToList();
            var prev = new List<Polygon2>();
            var bands = EdgeProfile.Aligned(a, b).Where(x => !x.Inside && !IsCurb(x.Attr)).ToList();
            for (int k = 0; k < bands.Count; k++)
            {
                var bd = bands[k];
                var attr = bd.Attr;
                List<Polygon2> band;
                if (k == bands.Count - 1)
                    // A faixa externa fica com todo o resto da calçada (a borda dela é a da calçada, sem lascas entre as duas;
                    // inclui o alargamento junto às rampas e a calçada mais larga que o perfil).
                    band = prev.Count > 0 ? PolygonOps.Difference(clip, prev) : clip;
                else
                {
                    var next = Enumerable.Range(0, ch.Points.Count).Select(i => acc[i] + bd.WidthA * (1 - ch.Weights[i]) + bd.WidthB * ch.Weights[i]).ToList();
                    if (next.Zip(acc, (x, y) => x - y).Max() < 0.01) { acc = next; continue; }
                    var upTo = VarOffset(ch, next);
                    band = prev.Count > 0 ? PolygonOps.Difference(upTo, prev) : upTo;
                    prev = PolygonOps.Union(prev.Concat(upTo));
                    acc = next;
                }
                foreach (var sh in PolygonOps.Difference(PolygonOps.Intersect(band, clip), covered).Where(x => x.Area > 1e-3))
                {
                    pieces.Add(new MarkingPiece(sh, attr.Color) { Thickness = attr.Thickness, Elevation = attr.Elevation, Layer = attr.Code });
                    covered.Add(sh);
                }
            }
            // Perfis sem faixa além do meio-fio: a calçada fica com o passeio do perfil geral.
            if (bands.Count == 0 && fallback.Where(x => !x.Inside && !IsCurb(x)).OrderBy(x => x.D1).LastOrDefault() is { } ob)
                foreach (var sh in clip)
                    pieces.Add(new MarkingPiece(sh, ob.Color) { Thickness = ob.Thickness, Elevation = ob.Elevation, Layer = ob.Code });
        }
        // O que nenhum trecho cobriu (contorno sem ramos): o perfil geral, como antes.
        var left = done.Count > 0 ? PolygonOps.Difference(walk, PolygonOps.Union(done)).Where(p => p.Area > 0.01).ToList() : walk;
        if (left.Count > 0 && fallback.Count > 0)
        {
            var (pc, _) = EdgeProfile.Apply(L.Pavement, left, fallback.Where(x => !x.Inside).ToList());
            pieces.AddRange(pc);
        }
        // Sarjeta junto ao meio-fio (externo e das ilhas), como com o perfil único.
        var gutter = fallback.Where(x => x.Inside).ToList();
        if (gutter.Count > 0)
        {
            var (pc, ins) = EdgeProfile.Apply(L.Pavement, region, gutter, zone);
            pieces.AddRange(pc);
            inside.AddRange(ins);
        }
        // Faixa gramada sem pontas finas (a ponta que afina até zero entre uma via com grama e outra sem passa para o passeio);
        // sem lascas de poucos cm² junto às rampas (viravam formas que o Revit recusa).
        return (IntersectionGenerator.TrimGrassSpikes(pieces).Where(p => p.Shape.Area >= 0.005).ToList(), inside);
    }
}
