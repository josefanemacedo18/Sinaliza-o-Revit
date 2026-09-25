using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Primitivas 3D para mobiliário urbano em coordenadas locais (x = direita, y = frente, z = altura):
/// esferas/elipsoides, troncos de cone, vigas orientadas e caixas – todas convexas (poliedros).
/// </summary>
internal sealed class FurnitureBuilder
{
    private readonly LocalFrame _f;
    public MarkingGeometry Geo { get; } = new();

    public FurnitureBuilder(LocalFrame frame) => _f = frame;

    private Vec2 W(double x, double y) => _f.ToWorld(new Vec2(x, y));
    private Vec3 W3(double x, double y, double z) => Vec3.At(W(x, y), z);

    private void Add(Polyhedron p, MarkingColor c) => Geo.Pieces.Add(Polyhedron.Piece(p, c));

    /// <summary>Caixa (extrusão vertical) centrada em (x, y).</summary>
    public void Box(double x, double y, double sx, double sy, double z0, double h, MarkingColor c)
    {
        var poly = new Polygon2(new[] { W(x - sx / 2, y - sy / 2), W(x + sx / 2, y - sy / 2), W(x + sx / 2, y + sy / 2), W(x - sx / 2, y + sy / 2) });
        Geo.Pieces.Add(new MarkingPiece(poly, c) { Thickness = h, Elevation = z0 });
    }

    /// <summary>Caixa com cantos arredondados em planta.</summary>
    public void RoundedBox(double x, double y, double sx, double sy, double r, double z0, double h, MarkingColor c)
    {
        var rect = Polygon2.Rectangle(new Vec2(x - sx / 2 + r, y - sy / 2 + r), new Vec2(x + sx / 2 - r, y + sy / 2 - r));
        foreach (var p in PolygonOps.Offset(new[] { rect }, r, true))
            Geo.Pieces.Add(new MarkingPiece(p.Transform(v => W(v.X, v.Y)), c) { Thickness = h, Elevation = z0 });
    }

    /// <summary>Tronco de cone vertical (poste cônico, tronco de árvore, lixeira).</summary>
    public void Frustum(double x, double y, double d0, double d1, double z0, double h, MarkingColor c, int n = 16)
    {
        var bottom = Enumerable.Range(0, n).Select(i => { var a = 2 * Math.PI * i / n; return W3(x + Math.Cos(a) * d0 / 2, y + Math.Sin(a) * d0 / 2, z0); }).ToList();
        var top = Enumerable.Range(0, n).Select(i => { var a = 2 * Math.PI * i / n; return W3(x + Math.Cos(a) * d1 / 2, y + Math.Sin(a) * d1 / 2, z0 + h); }).ToList();
        var faces = new List<IEnumerable<Vec3>> { bottom, top };
        for (int i = 0; i < n; i++) faces.Add(new[] { bottom[i], bottom[(i + 1) % n], top[(i + 1) % n], top[i] });
        Add(new Polyhedron(faces), c);
    }

    /// <summary>Elipsoide (copas, arbustos, luminárias).</summary>
    public void Ellipsoid(double x, double y, double z, double rx, double ry, double rz, MarkingColor c, int lon = 12, int lat = 6)
    {
        var rings = new List<List<Vec3>>();
        for (int j = 1; j < lat; j++)
        {
            var phi = Math.PI * j / lat - Math.PI / 2;
            rings.Add(Enumerable.Range(0, lon).Select(i =>
            {
                var th = 2 * Math.PI * i / lon;
                return W3(x + rx * Math.Cos(phi) * Math.Cos(th), y + ry * Math.Cos(phi) * Math.Sin(th), z + rz * Math.Sin(phi));
            }).ToList());
        }
        var south = W3(x, y, z - rz);
        var north = W3(x, y, z + rz);
        var faces = new List<IEnumerable<Vec3>>();
        for (int i = 0; i < lon; i++)
        {
            faces.Add(new[] { south, rings[0][(i + 1) % lon], rings[0][i] });
            faces.Add(new[] { north, rings[^1][i], rings[^1][(i + 1) % lon] });
        }
        for (int j = 0; j + 1 < rings.Count; j++)
            for (int i = 0; i < lon; i++)
                faces.Add(new[] { rings[j][i], rings[j][(i + 1) % lon], rings[j + 1][(i + 1) % lon], rings[j + 1][i] });
        Add(new Polyhedron(faces), c);
    }

    /// <summary>Viga de seção retangular entre dois pontos 3D (braços, ripas inclinadas, tubos).</summary>
    public void Beam((double X, double Y, double Z) a, (double X, double Y, double Z) b, double w, double h, MarkingColor c)
    {
        var p0 = W3(a.X, a.Y, a.Z);
        var p1 = W3(b.X, b.Y, b.Z);
        var d = p1 - p0;
        var len = Math.Sqrt(d.Dot(d));
        if (len < 1e-4) return;
        var u = d * (1 / len);
        var up = Math.Abs(u.Z) > 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 0, 1);
        var s = u.Cross(up);
        s *= 1 / Math.Sqrt(s.Dot(s));
        var t = s.Cross(u);
        var hw = w / 2;
        var hh = h / 2;
        Vec3[] Q(Vec3 o) => new[] { o + s * hw + t * hh, o - s * hw + t * hh, o - s * hw - t * hh, o + s * hw - t * hh };
        var q0 = Q(p0);
        var q1 = Q(p1);
        var faces = new List<IEnumerable<Vec3>> { q0, q1 };
        for (int i = 0; i < 4; i++) faces.Add(new[] { q0[i], q0[(i + 1) % 4], q1[(i + 1) % 4], q1[i] });
        Add(new Polyhedron(faces), c);
    }

    /// <summary>Tubo ao longo de uma polilinha 3D (paraciclo, braço curvo).</summary>
    public void Tube(IReadOnlyList<(double X, double Y, double Z)> pts, double d, MarkingColor c)
    {
        for (int i = 0; i + 1 < pts.Count; i++) Beam(pts[i], pts[i + 1], d, d, c);
    }

    /// <summary>Placa vertical (chapa) no plano x–z, centrada em y, com espessura t.</summary>
    public void Panel(double x0, double x1, double y, double t, double z0, double z1, MarkingColor c) =>
        Box((x0 + x1) / 2, y, Math.Abs(x1 - x0), t, z0, z1 - z0, c);
}

/// <summary>Desenho do mobiliário urbano (volumes detalhados, com leitura clara em planta e em 3D).</summary>
public static class UrbanDesigns
{
    /// <summary>Semente determinística a partir da posição (variação natural das árvores).</summary>
    private static double Hash(Vec2 p, int k) =>
        Math.Abs(Math.Sin(p.X * 12.9898 + p.Y * 78.233 + k * 37.719) * 43758.5453) % 1.0;

    public static MarkingGeometry Build(MobiliarioDef m, LocalFrame frame, double L, double W, double H, MarkingColor c)
    {
        var v = new FurnitureBuilder(frame);
        switch (m.Forma)
        {
            case FormaMobiliario.Banco: Bench(v, L, W, H, c); break;
            case FormaMobiliario.Lixeira: Bin(v, W, H, c); break;
            case FormaMobiliario.PosteIluminacao: StreetLight(v, L, W, H, c, pedestrian: false); break;
            case FormaMobiliario.PostePedestre: StreetLight(v, L, W, H, c, pedestrian: true); break;
            case FormaMobiliario.Arvore: Tree(v, frame.Origin, L, W, H, c == MarkingColor.Grama ? MarkingColor.Folhagem : c); break;
            case FormaMobiliario.Palmeira: Palm(v, frame.Origin, L, W, H, c == MarkingColor.Grama ? MarkingColor.Folhagem : c); break;
            case FormaMobiliario.Arbusto: Shrub(v, frame.Origin, L, W, H, c == MarkingColor.Grama ? MarkingColor.Folhagem : c); break;
            case FormaMobiliario.AbrigoOnibus: Shelter(v, L, W, H, c); break;
            case FormaMobiliario.Paraciclo: BikeRack(v, L, H, c); break;
            case FormaMobiliario.Hidrante: Hydrant(v, W, H, c); break;
            case FormaMobiliario.Floreira: Planter(v, frame.Origin, L, W, H, c); break;
            case FormaMobiliario.PlacaLogradouro: StreetName(v, L, W, H, c); break;
            case FormaMobiliario.Semaforo: TrafficLight(v, H, c); break;
        }
        v.Geo.UnitCount = 1;
        return v.Geo;
    }

    // ------------------------------------------------------------------ vegetação

    private static void Tree(FurnitureBuilder v, Vec2 at, double crownD, double pit, double H, MarkingColor leaf)
    {
        // Canteiro: guia de concreto + terra, com a grelha/gola do tronco.
        if (pit > 0.3)
        {
            var o = pit / 2;
            v.Box(0, o - 0.04, pit, 0.08, 0, 0.12, MarkingColor.Concreto);
            v.Box(0, -o + 0.04, pit, 0.08, 0, 0.12, MarkingColor.Concreto);
            v.Box(o - 0.04, 0, 0.08, pit - 0.16, 0, 0.12, MarkingColor.Concreto);
            v.Box(-o + 0.04, 0, 0.08, pit - 0.16, 0, 0.12, MarkingColor.Concreto);
            v.Box(0, 0, pit - 0.16, pit - 0.16, 0, 0.08, MarkingColor.Grama);
        }
        var trunkH = Math.Max(1.6, H * 0.42);
        var r = crownD / 2;
        v.Frustum(0, 0, 0.30, 0.18, 0, trunkH, MarkingColor.Marrom, 10);
        // Galhos principais em direção à copa.
        var rot = Hash(at, 1) * Math.PI * 2;
        for (int i = 0; i < 3; i++)
        {
            var a = rot + i * 2 * Math.PI / 3;
            v.Beam((0, 0, trunkH - 0.1), (Math.Cos(a) * r * 0.45, Math.Sin(a) * r * 0.45, trunkH + (H - trunkH) * 0.35), 0.10, 0.10, MarkingColor.Marrom);
        }
        // Copa: massa central + lóbulos (contorno orgânico em planta).
        var crownH = H - trunkH;
        var zc = trunkH + crownH * 0.52;
        v.Ellipsoid(0, 0, zc, r * 0.62, r * 0.62, crownH * 0.50, leaf);
        var lobes = 5 + (int)(Hash(at, 2) * 3);
        for (int i = 0; i < lobes; i++)
        {
            var a = rot + i * 2 * Math.PI / lobes + Hash(at, 10 + i) * 0.5;
            var rr = r * (0.36 + 0.12 * Hash(at, 20 + i));
            var dist = r - rr;
            v.Ellipsoid(Math.Cos(a) * dist, Math.Sin(a) * dist, zc - crownH * 0.08 + Hash(at, 30 + i) * crownH * 0.2, rr, rr, crownH * 0.34, leaf, 10, 5);
        }
        v.Ellipsoid(0, 0, trunkH + crownH * 0.82, r * 0.40, r * 0.40, crownH * 0.22, leaf, 10, 5);
    }

    private static void Palm(FurnitureBuilder v, Vec2 at, double crownD, double pit, double H, MarkingColor leaf)
    {
        if (pit > 0.3) v.RoundedBox(0, 0, pit, pit, 0.05, 0, 0.10, MarkingColor.Concreto);
        v.Frustum(0, 0, 0.38, 0.26, 0, H - 0.4, MarkingColor.Marrom, 10);
        var r = crownD / 2;
        var rot = Hash(at, 3) * Math.PI;
        for (int i = 0; i < 8; i++)
        {
            var a = rot + i * Math.PI / 4;
            v.Beam((0, 0, H - 0.3), (Math.Cos(a) * r * 0.55, Math.Sin(a) * r * 0.55, H + 0.2), 0.35, 0.05, leaf);
            v.Beam((Math.Cos(a) * r * 0.55, Math.Sin(a) * r * 0.55, H + 0.2), (Math.Cos(a) * r, Math.Sin(a) * r, H - 0.6), 0.28, 0.05, leaf);
        }
        v.Ellipsoid(0, 0, H - 0.2, 0.3, 0.3, 0.35, leaf, 8, 4);
    }

    private static void Shrub(FurnitureBuilder v, Vec2 at, double d, double w, double H, MarkingColor leaf)
    {
        var r = d / 2;
        v.Ellipsoid(0, 0, H * 0.5, r * 0.7, r * 0.7, H * 0.5, leaf, 10, 5);
        for (int i = 0; i < 4; i++)
        {
            var a = Hash(at, 5) * 6 + i * Math.PI / 2;
            v.Ellipsoid(Math.Cos(a) * r * 0.45, Math.Sin(a) * r * 0.45, H * 0.4, r * 0.5, r * 0.5, H * 0.4, leaf, 8, 4);
        }
    }

    private static void Planter(FurnitureBuilder v, Vec2 at, double L, double W, double H, MarkingColor c)
    {
        v.RoundedBox(0, 0, L, W, 0.04, 0, H, c);
        v.RoundedBox(0, 0, L - 0.12, W - 0.12, 0.02, H - 0.08, 0.06, MarkingColor.Marrom);
        var n = Math.Max(1, (int)Math.Round(L / 0.45));
        for (int i = 0; i < n; i++)
        {
            var x = -L / 2 + (i + 0.5) * L / n;
            var s = Math.Min(W, L / n) * 0.45;
            v.Ellipsoid(x, (Hash(at, i) - 0.5) * 0.1, H + s * 0.5, s, s, s * 0.8, MarkingColor.Folhagem, 8, 4);
        }
    }

    // ------------------------------------------------------------------ mobiliário

    private static void Bench(FurnitureBuilder v, double L, double W, double H, MarkingColor wood)
    {
        wood = wood == MarkingColor.Marrom ? MarkingColor.Madeira : wood;
        var seatZ = 0.45;
        var depth = Math.Min(W, 0.55);
        // Apoios laterais em aço (pé + braço).
        foreach (var x in new[] { -(L / 2 - 0.12), L / 2 - 0.12 })
        {
            v.Beam((x, depth / 2 - 0.05, 0), (x, depth / 2 - 0.08, seatZ), 0.06, 0.06, MarkingColor.Metal);
            v.Beam((x, -depth / 2 + 0.05, 0), (x, -depth / 2 + 0.02, H), 0.06, 0.06, MarkingColor.Metal);
            v.Beam((x, depth / 2 - 0.08, seatZ), (x, -depth / 2 + 0.03, seatZ - 0.02), 0.06, 0.05, MarkingColor.Metal);
            v.Beam((x, depth / 2 - 0.02, 0.68), (x, -depth / 2 + 0.03, 0.68), 0.06, 0.04, MarkingColor.Metal);   // braço
            v.Box(x, 0, 0.12, depth, 0, 0.02, MarkingColor.Metal);
        }
        // Assento: 4 ripas; encosto inclinado: 3 ripas.
        for (int i = 0; i < 4; i++)
        {
            var y = depth / 2 - 0.07 - i * (depth - 0.12) / 3.6;
            v.Box(0, y, L, 0.09, seatZ, 0.035, wood);
        }
        for (int i = 0; i < 3; i++)
        {
            var z = seatZ + 0.12 + i * (H - seatZ - 0.16) / 2.5;
            var y = -depth / 2 + 0.04 - (z - seatZ) * 0.12;
            v.Beam((-L / 2 + 0.03, y, z), (L / 2 - 0.03, y, z), 0.035, 0.09, wood);
        }
    }

    private static void Bin(FurnitureBuilder v, double W, double H, MarkingColor c)
    {
        v.Frustum(0, -W / 2 - 0.05, 0.07, 0.06, 0, H + 0.05, MarkingColor.Metal, 10);
        v.Frustum(0, 0, W * 0.82, W, H * 0.30, H * 0.60, c, 16);
        v.Frustum(0, 0, W * 1.04, W * 0.9, H * 0.90, 0.06, MarkingColor.Preta, 16);
        v.Beam((0, -W / 2 - 0.05, H * 0.62), (0, -W / 2 + 0.02, H * 0.62), 0.05, 0.04, MarkingColor.Metal);
    }

    private static void StreetLight(FurnitureBuilder v, double arm, double baseD, double H, MarkingColor c, bool pedestrian)
    {
        v.Frustum(0, 0, baseD * 2.2, baseD * 1.8, 0, 0.45, MarkingColor.Concreto, 12);
        v.Frustum(0, 0, baseD * 1.1, baseD * 0.55, 0.45, H - 0.45, c, 12);
        if (pedestrian)
        {
            // Luminária de topo (pétala) – iluminação da calçada.
            v.Frustum(0, 0, 0.12, 0.45, H, 0.18, c, 12);
            v.Ellipsoid(0, 0, H + 0.05, 0.2, 0.2, 0.06, MarkingColor.Vidro, 10, 4);
            return;
        }
        // Braço curvo sobre a pista (+Y) e luminária plana com difusor.
        var pts = new List<(double, double, double)>();
        var rise = Math.Min(1.0, arm * 0.4);
        for (int i = 0; i <= 8; i++)
        {
            var t = i / 8.0;
            pts.Add((0, arm * t, H - 0.25 + rise * Math.Sin(t * Math.PI / 2)));
        }
        v.Tube(pts, 0.07, c);
        var tip = pts[^1];
        v.RoundedBox(0, tip.Item2 + 0.25, 0.32, 0.65, 0.12, tip.Item3 - 0.12, 0.12, c);
        v.RoundedBox(0, tip.Item2 + 0.25, 0.26, 0.55, 0.10, tip.Item3 - 0.14, 0.02, MarkingColor.Vidro);
    }

    private static void Shelter(FurnitureBuilder v, double L, double W, double H, MarkingColor c)
    {
        // Frente (+Y) voltada para a pista; fundo em vidro, cobertura com caimento para trás.
        var back = -W / 2 + 0.10;
        foreach (var x in new[] { -(L / 2 - 0.10), L / 2 - 0.10 })
        {
            v.Box(x, back, 0.10, 0.10, 0, H - 0.05, c);
            v.Box(x, W / 2 - 0.25, 0.08, 0.08, 0, H - 0.12, c);
        }
        v.Beam((-L / 2, W / 2, H - 0.12), (L / 2, W / 2, H - 0.12), 0.08, 0.14, c);
        // Cobertura.
        v.Beam((-L / 2 - 0.15, 0.05, H - 0.02), (L / 2 + 0.15, 0.05, H - 0.02), W + 0.35, 0.06, c);
        // Painéis de vidro: fundo e uma lateral; painel publicitário na outra lateral.
        v.Panel(-L / 2 + 0.15, L / 2 - 0.15, back, 0.02, 0.15, H - 0.20, MarkingColor.Vidro);
        v.Box(-(L / 2 - 0.10), 0, 0.02, W - 0.45, 0.15, H - 0.35, MarkingColor.Vidro);
        v.Box(L / 2 - 0.10, 0, 0.12, W - 0.45, 0.10, 1.9, MarkingColor.Branca);
        // Banco com ripas de madeira sobre mãos-francesas.
        for (int i = 0; i < 3; i++) v.Box(0, back + 0.12 + i * 0.12, L * 0.62, 0.09, 0.45, 0.035, MarkingColor.Madeira);
        foreach (var x in new[] { -L * 0.27, L * 0.27 }) v.Beam((x, back + 0.02, 0.30), (x, back + 0.36, 0.44), 0.05, 0.05, MarkingColor.Metal);
    }

    private static void BikeRack(FurnitureBuilder v, double L, double H, MarkingColor c)
    {
        var r = L / 2;
        var pts = new List<(double, double, double)> { (-r, 0, 0), (-r, 0, H - r) };
        for (int i = 1; i < 10; i++)
        {
            var a = Math.PI - Math.PI * i / 10;
            pts.Add((Math.Cos(a) * r, 0, H - r + Math.Sin(a) * r));
        }
        pts.Add((r, 0, H - r));
        pts.Add((r, 0, 0));
        v.Tube(pts, 0.05, c);
        v.Beam((-r, 0, 0.30), (r, 0, 0.30), 0.04, 0.04, c);
        foreach (var x in new[] { -r, r }) v.Box(x, 0, 0.14, 0.14, 0, 0.01, c);
    }

    private static void Hydrant(FurnitureBuilder v, double W, double H, MarkingColor c)
    {
        v.Frustum(0, 0, W * 1.5, W * 1.3, 0, 0.06, c, 12);
        v.Frustum(0, 0, W, W * 0.92, 0.06, H * 0.72, c, 14);
        v.Frustum(0, 0, W * 1.18, W * 1.18, H * 0.74, 0.05, c, 14);
        v.Ellipsoid(0, 0, H * 0.80, W * 0.46, W * 0.46, H * 0.18, c, 12, 5);
        v.Frustum(0, 0, 0.05, 0.04, H * 0.95, H * 0.07, MarkingColor.Metal, 6);
        foreach (var (dx, dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0) })
            v.Beam((dx * W * 0.35, dy * W * 0.35, H * 0.52), (dx * (W * 0.5 + 0.09), dy * (W * 0.5 + 0.09), H * 0.52), 0.09, 0.09, c);
    }

    private static void StreetName(FurnitureBuilder v, double L, double W, double H, MarkingColor c)
    {
        v.Frustum(0, 0, 0.09, 0.06, 0, H, MarkingColor.Metal, 10);
        v.Box(0, 0.045, L, 0.012, H - W - 0.02, W, c);
        v.Box(0.045, 0, 0.012, L, H - 2 * W - 0.07, W, c);
        v.Frustum(0, 0, 0.08, 0.02, H, 0.05, MarkingColor.Metal, 8);
    }

    private static void TrafficLight(FurnitureBuilder v, double H, MarkingColor c)
    {
        v.Frustum(0, 0, 0.22, 0.20, 0, 0.30, MarkingColor.Concreto, 12);
        v.Frustum(0, 0, 0.12, 0.10, 0.30, H - 0.30, MarkingColor.Metal, 12);
        // Grupo focal voltado para o tráfego (−Y), com anteparo e pestanas.
        var y0 = -0.14;
        v.RoundedBox(0, y0 - 0.10, 0.30, 0.22, 0.04, H - 1.00, 1.00, c);
        v.Box(0, y0 - 0.215, 0.46, 0.01, H - 1.08, 1.16, MarkingColor.Preta);
        double[] z = { H - 0.23, H - 0.50, H - 0.77 };
        MarkingColor[] colors = { MarkingColor.Vermelha, MarkingColor.Amarela, MarkingColor.Verde };
        for (int i = 0; i < 3; i++)
        {
            v.Ellipsoid(0, y0 - 0.225, z[i], 0.10, 0.02, 0.10, colors[i], 12, 4);
            v.Beam((-0.12, y0 - 0.23, z[i] + 0.12), (0.12, y0 - 0.23, z[i] + 0.12), 0.18, 0.02, c);
        }
    }
}
