using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>Ponto de uma seção transversal: afastamento lateral (m, + à esquerda do eixo) e cota (m).</summary>
public readonly record struct SectionPt(double Y, double Z);

/// <summary>
/// Greide (perfil longitudinal): pontos de interseção vertical (PIV) ligados por rampas retas, concordados por curvas
/// verticais parabólicas (DNIT – Manual de Projeto Geométrico).
/// </summary>
public sealed class VerticalProfile
{
    public List<(double S, double Z)> Pvis { get; } = new();

    /// <summary>Comprimento das curvas verticais nos PIVs internos (m). 0 = sem concordância.</summary>
    public double CurveLength { get; set; }

    public static VerticalProfile Flat(double z) => new() { Pvis = { (0, z), (1e9, z) } };

    public static VerticalProfile Linear(double length, double z0, double z1) => new() { Pvis = { (0, z0), (Math.Max(0.01, length), z1) } };

    /// <summary>Sobe de <paramref name="z0"/> até <paramref name="top"/> em <paramref name="rampIn"/>, segue plano e desce até <paramref name="z1"/>.</summary>
    public static VerticalProfile Hump(double length, double z0, double top, double z1, double rampIn, double rampOut, double curve)
    {
        var p = new VerticalProfile { CurveLength = Math.Max(0, curve) };
        rampIn = Math.Clamp(rampIn, 0, length);
        rampOut = Math.Clamp(rampOut, 0, length - rampIn);
        p.Pvis.Add((0, z0));
        if (rampIn > 0.01) p.Pvis.Add((rampIn, top));
        if (rampOut > 0.01 && length - rampOut > rampIn + 0.01) p.Pvis.Add((length - rampOut, top));
        p.Pvis.Add((length, rampOut > 0.01 ? z1 : top));
        if (rampIn <= 0.01) p.Pvis[0] = (0, top);
        return p;
    }

    private double Line(double s)
    {
        if (Pvis.Count == 0) return 0;
        if (s <= Pvis[0].S) return Pvis[0].Z + Grade0(0) * (s - Pvis[0].S);
        for (int i = 0; i + 1 < Pvis.Count; i++)
        {
            var (a, za) = Pvis[i];
            var (b, zb) = Pvis[i + 1];
            if (s <= b || i + 2 == Pvis.Count)
                return b - a < 1e-9 ? zb : za + (zb - za) * (s - a) / (b - a);
        }
        return Pvis[^1].Z;
    }

    private double Grade0(int i) => i + 1 < Pvis.Count && Pvis[i + 1].S - Pvis[i].S > 1e-9
        ? (Pvis[i + 1].Z - Pvis[i].Z) / (Pvis[i + 1].S - Pvis[i].S) : 0;

    /// <summary>Cota do greide na estação <paramref name="s"/>.</summary>
    public double Z(double s)
    {
        var z = Line(s);
        if (CurveLength <= 0.01) return z;
        for (int i = 1; i + 1 < Pvis.Count; i++)
        {
            // A curva cabe nos trechos vizinhos (metade de cada lado do PIV).
            var lv = Math.Min(CurveLength, 1.8 * Math.Min(Pvis[i].S - Pvis[i - 1].S, Pvis[i + 1].S - Pvis[i].S));
            if (lv < 0.01) continue;
            var x0 = Pvis[i].S - lv / 2;
            if (s < x0 || s > Pvis[i].S + lv / 2) continue;
            var g1 = Grade0(i - 1);
            var g2 = Grade0(i);
            var x = s - x0;
            var zStart = Pvis[i].Z - g1 * lv / 2;
            return zStart + g1 * x + (g2 - g1) / (2 * lv) * x * x;
        }
        return z;
    }

    /// <summary>Rampa (m/m) na estação.</summary>
    public double Grade(double s, double ds = 0.05) => (Z(s + ds) - Z(s - ds)) / (2 * ds);

    /// <summary>Maior rampa (em módulo) entre os PIVs.</summary>
    public double MaxGrade => Enumerable.Range(0, Math.Max(0, Pvis.Count - 1)).Select(i => Math.Abs(Grade0(i))).DefaultIfEmpty(0).Max();
}

/// <summary>
/// Sólidos 3D das obras de infraestrutura: seções varridas ao longo do eixo com greide, caixas, cilindros e barras entre
/// dois pontos. Faces laterais trianguladas (sempre planas – o Revit aceita qualquer trecho, inclusive em curva e rampa).
/// </summary>
public static class SolidSweep
{
    /// <summary>Estações do eixo: vértices, divisões de até <paramref name="maxStep"/> e quebras pedidas.</summary>
    public static List<double> Stations(Polyline2 path, double s0, double s1, double maxStep, IEnumerable<double>? breaks = null)
    {
        var set = new SortedSet<double> { s0, s1 };
        double acc = 0;
        for (int i = 1; i < path.Points.Count; i++)
        {
            acc += path.Points[i - 1].DistanceTo(path.Points[i]);
            if (acc > s0 + 1e-6 && acc < s1 - 1e-6) set.Add(acc);
        }
        if (breaks != null) foreach (var b in breaks) if (b > s0 + 1e-6 && b < s1 - 1e-6) set.Add(b);
        var list = set.ToList();
        var res = new List<double>();
        for (int i = 0; i < list.Count; i++)
        {
            res.Add(list[i]);
            if (i + 1 == list.Count) break;
            var gap = list[i + 1] - list[i];
            var n = (int)Math.Ceiling(gap / Math.Max(0.2, maxStep));
            for (int k = 1; k < n; k++) res.Add(list[i] + gap * k / n);
        }
        return res;
    }

    /// <summary>Normal (à esquerda) média na estação – trechos vizinhos compartilham a face, sem frestas nas curvas.</summary>
    public static Vec2 Normal(Polyline2 path, double s)
    {
        var L = path.Length;
        var a = path.TangentAt(Math.Clamp(s - 0.01, 0, L)).PerpLeft;
        var b = path.TangentAt(Math.Clamp(s + 0.01, 0, L)).PerpLeft;
        var n = a + b;
        return n.Length < 1e-9 ? a : n.Normalized();
    }

    /// <summary>
    /// Varre uma seção (pontos (lateral, cota absoluta) em sequência – convexa ou não, sem autointerseção) ao longo do eixo,
    /// de <paramref name="s0"/> a <paramref name="s1"/>. A seção pode variar com a estação (mesmo número de pontos). Cada
    /// trecho contínuo vira UM sólido fechado (sem emendas internas); estações em que a seção varia linearmente são
    /// dispensadas. Com <paramref name="merge"/> = falso cada intervalo vira um bloco separado (placas, gabiões).
    /// </summary>
    public static int Along(MarkingGeometry geo, Polyline2 path, Func<double, IReadOnlyList<SectionPt>?> sectionAt, MarkingColor color,
        double s0 = 0, double s1 = double.NaN, double maxStep = 4, string? layer = null, IEnumerable<double>? breaks = null, bool merge = true)
    {
        var L = path.Length;
        if (double.IsNaN(s1)) s1 = L;
        s0 = Math.Clamp(s0, 0, L);
        s1 = Math.Clamp(s1, 0, L);
        if (s1 - s0 < 0.02) return 0;
        var st = Stations(path, s0, s1, maxStep, breaks);
        var faces = new List<(double S, List<Vec3>? F, Vec2 P, Vec2 N)>();
        foreach (var x in st)
        {
            var sec = sectionAt(x);
            var n = Normal(path, x);
            var p = path.PointAt(x);
            if (sec == null || sec.Count < 3 || SectionArea(sec) < 1e-7) { faces.Add((x, null, p, n)); continue; }
            faces.Add((x, sec.Select(q => Vec3.At(p + n * q.Y, q.Z)).ToList(), p, n));
        }
        var count = 0;
        if (!merge)
        {
            for (int i = 0; i + 1 < faces.Count; i++)
            {
                if (faces[i].F is not { } fa || faces[i + 1].F is not { } fb || fa.Count != fb.Count || faces[i + 1].S - faces[i].S < 0.01) continue;
                var poly = Shell(new List<List<Vec3>> { fa, fb }, new[] { faces[i], faces[i + 1] }.Select(f => (f.F!, f.P, f.N)).ToList());
                if (poly == null) continue;
                geo.Pieces.Add(new MarkingPiece(poly.Footprint(), color) { Solid = poly, Layer = layer });
                count++;
            }
            return count;
        }
        var sf = faces.Select(f => (f.S, f.F)).ToList();
        var i0 = 0;
        while (i0 < faces.Count)
        {
            if (faces[i0].F == null) { i0++; continue; }
            // Trecho contínuo com o mesmo número de pontos.
            var j0 = i0;
            while (j0 + 1 < faces.Count && faces[j0 + 1].F is { } nx && nx.Count == faces[i0].F!.Count) j0++;
            if (j0 > i0 && faces[j0].S - faces[i0].S >= 0.01)
            {
                var kept = new List<int> { i0 };
                var a = i0;
                while (a < j0)
                {
                    var b = a + 1;
                    while (b + 1 <= j0 && Linear(sf, a, b + 1)) b++;
                    kept.Add(b);
                    a = b;
                }
                var rings = kept.Select(k => faces[k]).ToList();
                // Remove estações coincidentes (intervalos < 1 cm).
                var clean = new List<(double S, List<Vec3>? F, Vec2 P, Vec2 N)> { rings[0] };
                foreach (var r in rings.Skip(1)) if (r.S - clean[^1].S >= 0.005) clean.Add(r);
                if (clean.Count >= 2)
                {
                    var poly = Shell(clean.Select(c => c.F!).ToList(), clean.Select(c => (c.F!, c.P, c.N)).ToList());
                    if (poly != null)
                    {
                        geo.Pieces.Add(new MarkingPiece(poly.Footprint(), color) { Solid = poly, Layer = layer });
                        count++;
                    }
                }
            }
            i0 = j0 + 1;
        }
        return count;
    }

    /// <summary>
    /// Casca fechada ligando anéis sucessivos (mesmo número de pontos, cada anel plano): tampas nas pontas e faces laterais
    /// (quadriláteros quando planos, senão dois triângulos).
    /// </summary>
    public static Polyhedron? Shell(IReadOnlyList<List<Vec3>> rings, IReadOnlyList<(List<Vec3> F, Vec2 P, Vec2 N)>? planInfo = null)
    {
        if (rings.Count < 2) return null;
        var m = rings[0].Count;
        if (m < 3 || rings.Any(r => r.Count != m)) return null;
        var faces = new List<List<Vec3>>();
        var start = rings[0].ToList();
        start.Reverse();
        AddPolygon(faces, start);
        AddPolygon(faces, rings[^1].ToList());
        for (int i = 0; i + 1 < rings.Count; i++)
        {
            var a = rings[i];
            var b = rings[i + 1];
            for (int k = 0; k < m; k++)
            {
                var k1 = (k + 1) % m;
                AddQuad(faces, a[k], a[k1], b[k1], b[k]);
            }
        }
        Polygon2? plan = null;
        if (planInfo != null && planInfo.Count >= 2)
        {
            var right = new List<Vec2>();
            var left = new List<Vec2>();
            foreach (var (f, p, n) in planInfo)
            {
                var lat = f.Select(v => (v.XY - p).Dot(n)).ToList();
                right.Add(p + n * lat.Min());
                left.Add(p + n * lat.Max());
            }
            left.Reverse();
            var ring = right.Concat(left).ToList();
            if (Polygon2.SignedArea(ring) is var ar && Math.Abs(ar) > 1e-6) plan = new Polygon2(ring);
        }
        return Polyhedron.Shell(faces, plan);
    }

    /// <summary>Quadrilátero a-b-c-d: uma face se for plano, senão dois triângulos (a-b-c, a-c-d). Faces degeneradas são descartadas.</summary>
    private static void AddQuad(List<List<Vec3>> faces, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        var n = (b - a).Cross(c - a);
        var nl = Math.Sqrt(n.Dot(n));
        var n2 = (c - a).Cross(d - a);
        var nl2 = Math.Sqrt(n2.Dot(n2));
        if (nl < 1e-10 && nl2 < 1e-10) return;
        if (nl > 1e-10 && nl2 > 1e-10)
        {
            var dist = Math.Abs((d - a).Dot(n * (1 / nl)));
            var scale = Math.Max(Len(c - a), Len(d - b));
            if (dist <= 1e-5 * Math.Max(1, scale) && n.Dot(n2) > 0) { faces.Add(new List<Vec3> { a, b, c, d }); return; }
        }
        if (nl > 1e-10) faces.Add(new List<Vec3> { a, b, c });
        if (nl2 > 1e-10) faces.Add(new List<Vec3> { a, c, d });
    }

    private static double Len(Vec3 v) => Math.Sqrt(v.Dot(v));

    /// <summary>Polígono: uma face se for plano; senão em leque de triângulos (tampas empenadas).</summary>
    private static void AddPolygon(List<List<Vec3>> faces, List<Vec3> pts)
    {
        if (pts.Count == 3) { faces.Add(pts); return; }
        var n = Polyhedron.Normal(pts);
        var nl = Len(n);
        if (nl < 1e-12) return;
        var nn = n * (1 / nl);
        var c = new Vec3(pts.Average(v => v.X), pts.Average(v => v.Y), pts.Average(v => v.Z));
        var planar = pts.All(v => Math.Abs((v - c).Dot(nn)) < 1e-5);
        if (planar) { faces.Add(pts); return; }
        for (int i = 1; i + 1 < pts.Count; i++) faces.Add(new List<Vec3> { pts[0], pts[i], pts[i + 1] });
    }

    /// <summary>As faces entre i e j são interpolação linear das extremas (tolerância de 1 cm)?</summary>
    private static bool Linear(List<(double S, List<Vec3>? F)> faces, int i, int j)
    {
        var (sa, fa) = faces[i];
        var (sb, fb) = faces[j];
        if (sb - sa < 1e-9) return true;
        for (int m = i + 1; m < j; m++)
        {
            if (faces[m].F is not { } fm || fm.Count != fa!.Count) return false;
            var t = (faces[m].S - sa) / (sb - sa);
            for (int k = 0; k < fm.Count; k++)
            {
                var e = fa[k] + (fb![k] - fa[k]) * t;
                var d = fm[k] - e;
                if (d.Dot(d) > 1e-4) return false;
            }
        }
        return true;
    }

    private static double FaceArea(IReadOnlyList<Vec3> f)
    {
        var n = Polyhedron.Normal(f);
        return Math.Sqrt(n.Dot(n)) / 2;
    }

    /// <summary>Sólido entre duas faces correspondentes (tampas planas e laterais planas ou em triângulos).</summary>
    public static Polyhedron? Prism(IReadOnlyList<Vec3> fa, IReadOnlyList<Vec3> fb)
    {
        if (fa.Count < 3 || fa.Count != fb.Count) return null;
        return Shell(new List<List<Vec3>> { fa.ToList(), fb.ToList() });
    }

    private static double SectionArea(IReadOnlyList<SectionPt> s)
    {
        double a = 0;
        for (int i = 0; i < s.Count; i++)
        {
            var p = s[i];
            var q = s[(i + 1) % s.Count];
            a += p.Y * q.Z - q.Y * p.Z;
        }
        return Math.Abs(a) / 2;
    }

    /// <summary>Retângulo na seção: de <paramref name="y0"/> a <paramref name="y1"/> e de <paramref name="z0"/> a <paramref name="z1"/>.</summary>
    public static SectionPt[] Rect(double y0, double y1, double z0, double z1) =>
        new[] { new SectionPt(y0, z0), new SectionPt(y1, z0), new SectionPt(y1, z1), new SectionPt(y0, z1) };

    /// <summary>Trapézio: base de <paramref name="b0"/> a <paramref name="b1"/> em z0 e topo de <paramref name="t0"/> a <paramref name="t1"/> em z1.</summary>
    public static SectionPt[] Trapezoid(double b0, double b1, double z0, double t0, double t1, double z1) =>
        new[] { new SectionPt(b0, z0), new SectionPt(b1, z0), new SectionPt(t1, z1), new SectionPt(t0, z1) };

    /// <summary>Caixa orientada: centro, direção do comprimento, comprimento, largura e cotas.</summary>
    public static Polyhedron Box(Vec2 c, Vec2 dir, double along, double across, double z0, double z1)
    {
        var u = dir.Length < 1e-9 ? Vec2.UnitX : dir.Normalized();
        var v = u.PerpLeft;
        var a = u * (along / 2);
        var b = v * (across / 2);
        var basePts = new[] { c - a - b, c + a - b, c + a + b, c - a + b };
        return Polyhedron.Prism(basePts, _ => z0, _ => z1);
    }

    /// <summary>Cilindro (prisma de <paramref name="n"/> lados).</summary>
    public static Polyhedron Cylinder(Vec2 c, double r, double z0, double z1, int n = 32)
    {
        var pts = Enumerable.Range(0, n).Select(i => c + Vec2.FromAngle(2 * Math.PI * i / n) * r).ToList();
        return Polyhedron.Prism(pts, _ => z0, _ => z1);
    }

    /// <summary>Tronco de cone (base <paramref name="r0"/>, topo <paramref name="r1"/>).</summary>
    public static Polyhedron Frustum(Vec2 c, double r0, double r1, double z0, double z1, int n = 32)
    {
        var bottom = Enumerable.Range(0, n).Select(i => Vec3.At(c + Vec2.FromAngle(2 * Math.PI * i / n) * r0, z0)).ToList();
        var top = Enumerable.Range(0, n).Select(i => Vec3.At(c + Vec2.FromAngle(2 * Math.PI * i / n) * r1, z1)).ToList();
        return Prism(bottom, top)!;
    }

    /// <summary>Barra de seção quadrada entre dois pontos 3D (estais, pendurais, diagonais de treliça, guarda-corpos).</summary>
    public static Polyhedron? Rod(Vec3 a, Vec3 b, double size)
    {
        var d = b - a;
        var len = Math.Sqrt(d.Dot(d));
        if (len < 1e-6) return null;
        var dn = d * (1 / len);
        var refv = Math.Abs(dn.Z) > 0.95 ? new Vec3(1, 0, 0) : new Vec3(0, 0, 1);
        var u = Norm(dn.Cross(refv)) * (size / 2);
        var v = Norm(u.Cross(dn)) * (size / 2);
        var fa = new List<Vec3> { a - u - v, a + u - v, a + u + v, a - u + v };
        var fb = new List<Vec3> { b - u - v, b + u - v, b + u + v, b - u + v };
        return Prism(fa, fb);
    }

    /// <summary>Cilindro em qualquer direção (tubos, ventiladores, bueiros): prisma de <paramref name="n"/> lados entre dois pontos.</summary>
    public static Polyhedron? Tube(Vec3 a, Vec3 b, double r, int n = 20)
    {
        var d = b - a;
        var len = Math.Sqrt(d.Dot(d));
        if (len < 1e-6) return null;
        var dn = d * (1 / len);
        var refv = Math.Abs(dn.Z) > 0.95 ? new Vec3(1, 0, 0) : new Vec3(0, 0, 1);
        var u = Norm(dn.Cross(refv));
        var v = Norm(u.Cross(dn));
        var fa = new List<Vec3>();
        var fb = new List<Vec3>();
        for (int i = 0; i < n; i++)
        {
            var ang = 2 * Math.PI * i / n;
            var o = u * (Math.Cos(ang) * r) + v * (Math.Sin(ang) * r);
            fa.Add(a + o);
            fb.Add(b + o);
        }
        return Prism(fa, fb);
    }

    /// <summary>Extruda uma seção (lateral, cota) de <paramref name="p"/> ao longo de <paramref name="t"/> por <paramref name="length"/> (emboques, testas).</summary>
    public static Polyhedron? Extrude(IReadOnlyList<SectionPt> section, Vec2 p, Vec2 t, double length)
    {
        var n = t.PerpLeft;
        var fa = section.Select(q => Vec3.At(p + n * q.Y, q.Z)).ToList();
        var fb = section.Select(q => Vec3.At(p + t * length + n * q.Y, q.Z)).ToList();
        return Prism(fa, fb);
    }

    private static Vec3 Norm(Vec3 v)
    {
        var l = Math.Sqrt(v.Dot(v));
        return l < 1e-12 ? new Vec3(1, 0, 0) : v * (1 / l);
    }

    /// <summary>
    /// Sólido de revolução exato (cilindro, tronco de cone ou tubo/anel com raio interno) entre dois pontos: no Revit vira
    /// superfície curva de verdade; aqui leva também a aproximação com <paramref name="n"/> lados.
    /// </summary>
    public static void AddRound(MarkingGeometry geo, Vec3 a, Vec3 b, double ra, double rb, MarkingColor color, string? layer = null, bool isUnit = false,
        double innerA = 0, double innerB = 0, int n = 32)
    {
        var poly = RoundPoly(a, b, ra, rb, innerA, innerB, n);
        if (poly == null) return;
        geo.Pieces.Add(new MarkingPiece(poly.Footprint(), color)
        {
            Solid = poly, Layer = layer, IsUnit = isUnit, Round = new RoundSolid(a, b, ra, rb, Math.Max(0, innerA), Math.Max(0, innerB)),
        });
    }

    /// <summary>Coluna vertical circular (ou cônica) com superfície exata no Revit.</summary>
    public static void AddColumn(MarkingGeometry geo, Vec2 c, double r0, double r1, double z0, double z1, MarkingColor color, string? layer = null, bool isUnit = false) =>
        AddRound(geo, Vec3.At(c, z0), Vec3.At(c, z1), r0, r1, color, layer, isUnit);

    /// <summary>Aproximação poliédrica de um sólido de revolução (maciço ou oco).</summary>
    public static Polyhedron? RoundPoly(Vec3 a, Vec3 b, double ra, double rb, double innerA = 0, double innerB = 0, int n = 32)
    {
        var d = b - a;
        var len = Math.Sqrt(d.Dot(d));
        if (len < 1e-6 || ra <= 1e-6 && rb <= 1e-6) return null;
        var dn = d * (1 / len);
        var refv = Math.Abs(dn.Z) > 0.95 ? new Vec3(1, 0, 0) : new Vec3(0, 0, 1);
        var u = Norm(dn.Cross(refv));
        var v = Norm(u.Cross(dn));
        List<Vec3> Ring(Vec3 c, double r) => Enumerable.Range(0, n).Select(i =>
        {
            var ang = 2 * Math.PI * i / n;
            return c + u * (Math.Cos(ang) * r) + v * (Math.Sin(ang) * r);
        }).ToList();
        var ao = Ring(a, Math.Max(1e-4, ra));
        var bo = Ring(b, Math.Max(1e-4, rb));
        if (innerA <= 1e-6 && innerB <= 1e-6) return Shell(new List<List<Vec3>> { ao, bo });
        var ai = Ring(a, Math.Max(1e-4, Math.Min(innerA, ra * 0.98)));
        var bi = Ring(b, Math.Max(1e-4, Math.Min(innerB, rb * 0.98)));
        return HollowLoft(ao, bo, ai, bi);
    }

    /// <summary>
    /// Casca oca entre dois anéis (contornos externos <paramref name="ao"/>→<paramref name="bo"/> e internos
    /// <paramref name="ai"/>→<paramref name="bi"/>, mesmo número de pontos) – cones excêntricos, tubos, emboques.
    /// </summary>
    public static Polyhedron? HollowLoft(IReadOnlyList<Vec3> ao, IReadOnlyList<Vec3> bo, IReadOnlyList<Vec3> ai, IReadOnlyList<Vec3> bi)
    {
        var n = ao.Count;
        if (n < 3 || bo.Count != n || ai.Count != n || bi.Count != n) return null;
        var faces = new List<List<Vec3>>();
        for (int k = 0; k < n; k++)
        {
            var k1 = (k + 1) % n;
            AddQuad(faces, ao[k], ao[k1], bo[k1], bo[k]);
            AddQuad(faces, ai[k1], ai[k], bi[k], bi[k1]);
            AddQuad(faces, ao[k1], ao[k], ai[k], ai[k1]);
            AddQuad(faces, bo[k], bo[k1], bi[k1], bi[k]);
        }
        return Polyhedron.Shell(faces);
    }

    /// <summary>Seção circular (tubos varridos ao longo do eixo – corrimãos, canaletas meia-cana).</summary>
    public static SectionPt[] Circle(double cy, double cz, double r, int n = 20) =>
        Enumerable.Range(0, n).Select(i => new SectionPt(cy + Math.Cos(2 * Math.PI * i / n) * r, cz + Math.Sin(2 * Math.PI * i / n) * r)).ToArray();

    /// <summary>Retângulo com cantos arredondados (<paramref name="r"/>) – pilares-parede, vigas, coroamentos.</summary>
    public static SectionPt[] RoundedRect(double y0, double y1, double z0, double z1, double r, int seg = 4)
    {
        r = Math.Max(0, Math.Min(r, Math.Min(Math.Abs(y1 - y0), Math.Abs(z1 - z0)) / 2 - 1e-4));
        if (r < 1e-4) return Rect(y0, y1, z0, z1);
        var (ya, yb) = (Math.Min(y0, y1), Math.Max(y0, y1));
        var (za, zb) = (Math.Min(z0, z1), Math.Max(z0, z1));
        var pts = new List<SectionPt>();
        void Corner(double cy, double cz, double a0)
        {
            for (int i = 0; i <= seg; i++)
            {
                var a = a0 + Math.PI / 2 * i / seg;
                pts.Add(new SectionPt(cy + Math.Cos(a) * r, cz + Math.Sin(a) * r));
            }
        }
        Corner(ya + r, za + r, Math.PI);
        Corner(yb - r, za + r, 1.5 * Math.PI);
        Corner(yb - r, zb - r, 0);
        Corner(ya + r, zb - r, 0.5 * Math.PI);
        return pts.ToArray();
    }

    /// <summary>Retângulo com chanfros de <paramref name="c"/> nos quatro cantos.</summary>
    public static SectionPt[] Chamfered(double y0, double y1, double z0, double z1, double c)
    {
        var (ya, yb) = (Math.Min(y0, y1), Math.Max(y0, y1));
        var (za, zb) = (Math.Min(z0, z1), Math.Max(z0, z1));
        c = Math.Max(0, Math.Min(c, Math.Min(yb - ya, zb - za) / 2 - 1e-4));
        if (c < 1e-4) return Rect(ya, yb, za, zb);
        return new[]
        {
            new SectionPt(ya + c, za), new SectionPt(yb - c, za), new SectionPt(yb, za + c), new SectionPt(yb, zb - c),
            new SectionPt(yb - c, zb), new SectionPt(ya + c, zb), new SectionPt(ya, zb - c), new SectionPt(ya, za + c),
        };
    }

    /// <summary>Espelha uma seção (lateral → −lateral) mantendo o sentido do contorno.</summary>
    public static SectionPt[] Mirror(IEnumerable<SectionPt> sec) => sec.Select(q => new SectionPt(-q.Y, q.Z)).Reverse().ToArray();

    /// <summary>Desloca uma seção (lateral e cota).</summary>
    public static SectionPt[] Shift(IEnumerable<SectionPt> sec, double dy, double dz) => sec.Select(q => new SectionPt(q.Y + dy, q.Z + dz)).ToArray();

    public static void Add(MarkingGeometry geo, Polyhedron? p, MarkingColor color, string? layer = null, bool isUnit = false)
    {
        if (p == null) return;
        geo.Pieces.Add(new MarkingPiece(p.Footprint(), color) { Solid = p, Layer = layer, IsUnit = isUnit });
    }

    /// <summary>Faixa pintada (espessura de tinta) sobre uma superfície com greide – linhas de bordo e eixo em obras de arte.</summary>
    public static void PaintStrip(MarkingGeometry geo, Polyline2 path, VerticalProfile profile, double lateral, double width, MarkingColor color,
        double s0, double s1, double dash = 0, double gap = 0, double lift = 0.003)
    {
        SectionPt[] Sec(double s) => Rect(lateral - width / 2, lateral + width / 2, profile.Z(s) + 0.0005, profile.Z(s) + lift);
        if (dash <= 0 || gap <= 0)
        {
            Along(geo, path, Sec, color, s0, s1, 6);
            return;
        }
        for (var s = s0; s < s1; s += dash + gap)
            Along(geo, path, Sec, color, s, Math.Min(s1, s + dash), 6);
    }
}
