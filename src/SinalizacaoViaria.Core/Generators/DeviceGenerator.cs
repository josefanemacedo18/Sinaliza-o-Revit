using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Parâmetros de instância de uma linha de dispositivos físicos.</summary>
public sealed class DeviceOptions
{
    public double Offset { get; set; }
    public double? Spacing { get; set; }
    public double StartSetback { get; set; }
    public double EndSetback { get; set; }
    public double? Length { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public MarkingColor? Color { get; set; }
    public bool Reverse { get; set; }

    /// <summary>Comprimento máximo das peças contínuas (acompanhamento de superfícies). 0 = sem divisão.</summary>
    public double MaxPieceLength { get; set; }
}

/// <summary>
/// Gera dispositivos físicos ao longo de um caminho: unidades espaçadas (segregadores, tachões, balizadores, cilindros,
/// pilaretes, prismas, barreiras modulares) ou elementos contínuos (barreira New Jersey, separadores, defensas), com
/// formas próximas das reais: cúpulas e troncos suavizados, faixas refletivas, perfil New Jersey normalizado e lâmina
/// de defensa em "W".
/// </summary>
public static class DeviceGenerator
{
    /// <summary>Perfil New Jersey de referência (0,60 × 0,81 m): pé vertical de 7,5 cm, face a 55° até 0,33 m e a 84° até o topo.</summary>
    private static readonly (double X, double Z)[] NewJerseyHalf = { (0.30, 0), (0.30, 0.075), (0.1214, 0.33), (0.075, 0.81) };

    public static MarkingGeometry Generate(Polyline2 path, DispositivoDef def, DeviceOptions? opt = null)
    {
        opt ??= new DeviceOptions();
        var geo = new MarkingGeometry();
        if (path.Length < 0.05) { geo.Warnings.Add("Caminho muito curto."); return geo; }

        var p = opt.Reverse ? path.Reversed() : path;
        var off = p.Offset(opt.Reverse ? -opt.Offset : opt.Offset);
        var L = Math.Max(0.02, opt.Length ?? def.Comprimento);
        var W = Math.Max(0.02, opt.Width ?? def.Largura);
        var H = Math.Max(0.01, opt.Height ?? def.Altura);
        var spacing = opt.Spacing ?? def.Espacamento;
        var color = opt.Color ?? def.Cor;
        var a = Math.Max(0, opt.StartSetback);
        var b = p.Length - Math.Max(0, opt.EndSetback);
        if (b - a <= 0.01) { geo.Warnings.Add("Os recuos são maiores que o comprimento do caminho."); return geo; }
        geo.PathLength = b - a;

        Vec2 PointAt(double s) => off.PointAtParam(p.ParamAt(s));

        // Elementos contínuos: perfil extrudado trecho a trecho do caminho.
        var guardRail = def.Forma is FormaDispositivo.Defensa or FormaDispositivo.DefensaDupla;
        if (spacing <= 0 || guardRail)
        {
            foreach (var (c0, c1) in LinearPatternGenerator.Chunk(a, b, opt.MaxPieceLength))
            {
                var pts = off.SubPoints(p.ParamAt(c0), p.ParamAt(c1));
                if (guardRail)
                {
                    var sides = def.Forma == FormaDispositivo.DefensaDupla ? new[] { 1.0, -1.0 } : new[] { 1.0 };
                    var cable = def.Codigo.Contains("CABO", StringComparison.OrdinalIgnoreCase);
                    foreach (var side in sides)
                    {
                        if (cable)
                            foreach (var zc in new[] { H * 0.62, H * 0.78, H * 0.94 })
                                Extrude(geo, pts, Box(side * 0.03, 0.02, zc - 0.01, 0.02), MarkingColor.Metal);
                        else
                            Extrude(geo, pts, WBeam(side, 0.075 + 0.02, H - 0.36, 0.31), MarkingColor.Metal);
                    }
                }
                else if (def.Forma == FormaDispositivo.NewJersey)
                    Extrude(geo, pts, NewJerseyProfile(W, H, color == MarkingColor.Branca), color);
                else
                    Extrude(geo, pts, MuretaProfile(W, H), color);
            }
            geo.PaintedLength = b - a;
            if (!guardRail) return geo;
        }

        // Unidades espaçadas (centradas no trecho disponível)
        var step = guardRail ? Math.Max(0.5, spacing) : spacing;
        var len = b - a;
        var n = Math.Max(1, (int)Math.Floor((len - L) / step + 1e-9) + 1);
        var used = (n - 1) * step;
        var s0 = a + (len - used) / 2;
        for (int i = 0; i < n; i++)
        {
            var s = s0 + i * step;
            var frame = new LocalFrame(PointAt(s), p.TangentAt(s));
            foreach (var piece in UnitPieces(def, L, W, H, color, i))
                geo.Pieces.Add(ToWorld(piece, frame) with { IsUnit = true });
        }
        geo.UnitCount = n;
        if (!guardRail) geo.PaintedLength = len;
        return geo;
    }

    // ------------------------------------------------------------------ unidades

    /// <summary>Peças de uma unidade em coordenadas locais (+Y ao longo do caminho, +X à direita).</summary>
    public static IEnumerable<MarkingPiece> UnitPieces(DispositivoDef def, double L, double W, double H, MarkingColor color, int index = 0)
    {
        switch (def.Forma)
        {
            case FormaDispositivo.Tartaruga:
            {
                // Cúpula elíptica com refletivos brancos nas duas faces inclinadas.
                yield return Loft(new[] { 1.0, 0.94, 0.78, 0.52, 0.24 }.Zip(new[] { 0, 0.35, 0.68, 0.9, 1.0 },
                    (k, z) => (Ellipse(W / 2 * k, L / 2 * k, 28), z * H)).ToList(), color);
                foreach (var sg in new[] { 1.0, -1.0 })
                    yield return Block(new Vec2(0, sg * L * 0.30), W * 0.45, L * 0.10, H * 0.28, H * 0.34, MarkingColor.Branca);
                break;
            }
            case FormaDispositivo.Caixa when H <= 0.12:
            {
                // Tachão: tronco trapezoidal com refletivos nas faces de aproximação.
                yield return Loft(new[] { (RectRing(W, L), 0.0), (RectRing(W * 0.85, L * 0.8), H * 0.6), (RectRing(W * 0.6, L * 0.55), H) }, color);
                foreach (var sg in new[] { 1.0, -1.0 })
                    yield return Block(new Vec2(0, sg * L * 0.36), W * 0.55, L * 0.06, H * 0.25, H * 0.45, MarkingColor.Branca);
                break;
            }
            case FormaDispositivo.Balizador:
            {
                var r = W / 2;
                var baseR = Math.Max(W * 1.5, 0.10);
                yield return Loft(new[] { (CircleRing(baseR), 0.0), (CircleRing(baseR * 0.9), 0.025), (CircleRing(r * 1.3), 0.045) }, MarkingColor.Preta);
                yield return Cyl(r, 0.045, H - 0.02, color);
                foreach (var (z0, z1) in new[] { (0.62, 0.70), (0.78, 0.86) })
                    yield return Cyl(r * 1.05, H * z0, H * z1, MarkingColor.Branca);
                yield return Loft(new[] { (CircleRing(r), H - 0.02), (CircleRing(r * 0.6), H) }, color);
                break;
            }
            case FormaDispositivo.Cilindro when color == MarkingColor.Concreto:
            {
                // Pilarete / frade: fuste de concreto, topo abaulado e faixa refletiva amarela.
                var r = W / 2;
                yield return Cyl(r, 0, H * 0.88, color);
                yield return Loft(new[] { (CircleRing(r), H * 0.88), (CircleRing(r * 0.92), H * 0.95), (CircleRing(r * 0.6), H * 0.99), (CircleRing(r * 0.25), H) }, color);
                yield return Cyl(r * 1.02, H * 0.74, H * 0.80, MarkingColor.Amarela);
                break;
            }
            case FormaDispositivo.Cilindro:
            {
                // Cilindro delimitador: corpo com duas faixas refletivas e base preta.
                var r = W / 2;
                yield return Loft(new[] { (CircleRing(r * 1.35), 0.0), (CircleRing(r * 1.25), 0.05) }, MarkingColor.Preta);
                yield return Cyl(r, 0.05, H - 0.03, color);
                foreach (var (z0, z1) in new[] { (0.55, 0.66), (0.76, 0.87) })
                    yield return Cyl(r * 1.03, H * z0, H * z1, MarkingColor.Branca);
                yield return Loft(new[] { (CircleRing(r), H - 0.03), (CircleRing(r * 0.75), H) }, color);
                break;
            }
            case FormaDispositivo.Prisma:
            {
                // Tronco de pirâmide com chanfro no topo.
                yield return Loft(new[] { (RectRing(W, L), 0.0), (RectRing(W * 0.93, L * 0.93), H * 0.15), (RectRing(W * 0.62, L * 0.62), H * 0.94), (RectRing(W * 0.55, L * 0.55), H) }, color);
                break;
            }
            case FormaDispositivo.NewJersey:
            {
                // Módulo: perfil New Jersey extrudado; a barreira plástica alterna vermelho e branco.
                var c = color == MarkingColor.Branca ? (index % 2 == 0 ? MarkingColor.Vermelha : MarkingColor.Branca) : color;
                var prof = NewJerseyProfile(W, H, color == MarkingColor.Branca);
                yield return ProfileSolid.Piece(new ProfileSolid(new Vec2(0, -L / 2), new Vec2(1, 0), prof, new Vec2(0, 1), L), c);
                break;
            }
            case FormaDispositivo.Defensa:
            case FormaDispositivo.DefensaDupla:
            {
                // Poste em "C" e espaçador (bloco) até a lâmina.
                yield return Block(Vec2.Zero, 0.15, 0.10, 0, H, MarkingColor.Metal);
                var sides = def.Forma == FormaDispositivo.DefensaDupla ? new[] { 1.0, -1.0 } : new[] { 1.0 };
                if (!def.Codigo.Contains("CABO", StringComparison.OrdinalIgnoreCase))
                    foreach (var sg in sides)
                        yield return Block(new Vec2(-sg * 0.085, 0), 0.10, 0.08, H - 0.33, H - 0.08, MarkingColor.Metal);
                break;
            }
            default:
                yield return Loft(new[] { (RectRing(W, L), 0.0), (RectRing(W, L), H * 0.85), (RectRing(W * 0.85, L * 0.9), H) }, color);
                break;
        }
    }

    // ------------------------------------------------------------------ perfis

    /// <summary>Perfil New Jersey escalado (x lateral, z altura); a barreira plástica tem o topo arredondado.</summary>
    public static Polygon2 NewJerseyProfile(double W, double H, bool plastic)
    {
        var sx = W / 0.60;
        var sz = H / 0.81;
        var half = NewJerseyHalf.Select(v => (X: v.X * sx, Z: v.Z * sz)).ToList();
        if (plastic)
        {
            // Plástica: laterais mais retas e topo arredondado (encaixe macho-fêmea não modelado).
            half = new List<(double X, double Z)> { (W / 2, 0), (W / 2, H * 0.12), (W * 0.36, H * 0.5), (W * 0.24, H * 0.9), (W * 0.15, H) };
        }
        var right = half.Select(v => new Vec2(v.X, v.Z)).ToList();
        var left = Enumerable.Reverse(half).Select(v => new Vec2(-v.X, v.Z)).ToList();
        return new Polygon2(right.Concat(left));
    }

    /// <summary>Mureta/separador contínuo com chanfros no topo.</summary>
    private static Polygon2 MuretaProfile(double W, double H)
    {
        var c = Math.Min(0.03, Math.Min(W, H) * 0.2);
        return new Polygon2(new[] { new Vec2(-W / 2, 0), new Vec2(W / 2, 0), new Vec2(W / 2, H - c), new Vec2(W / 2 - c, H), new Vec2(-W / 2 + c, H), new Vec2(-W / 2, H - c) });
    }

    /// <summary>Lâmina de defensa em "W" (chapa de 4 mm, 31 cm de altura), afastada <paramref name="offset"/> do eixo dos postes.</summary>
    private static Polygon2 WBeam(double side, double offset, double z0, double hb)
    {
        const double depth = 0.083, t = 0.004;
        var outer = new List<Vec2>();
        var inner = new List<Vec2>();
        const int n = 24;
        for (int i = 0; i <= n; i++)
        {
            var z = hb * i / n;
            var x = depth * (1 - Math.Cos(4 * Math.PI * z / hb)) / 2;
            outer.Add(new Vec2(side * (offset + x + t), z0 + z));
            inner.Add(new Vec2(side * (offset + x), z0 + z));
        }
        inner.Reverse();
        var ring = outer.Concat(inner).ToList();
        if (side < 0) ring.Reverse();
        return new Polygon2(ring);
    }

    private static Polygon2 Box(double x, double w, double z0, double h) =>
        new(new[] { new Vec2(x - w / 2, z0), new Vec2(x + w / 2, z0), new Vec2(x + w / 2, z0 + h), new Vec2(x - w / 2, z0 + h) });

    /// <summary>Extrusão do perfil (x lateral à esquerda do caminho) ao longo de cada trecho da polilinha.</summary>
    private static void Extrude(MarkingGeometry geo, IReadOnlyList<Vec2> pts, Polygon2 profile, MarkingColor color)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            var len = a.DistanceTo(b);
            if (len < 0.005) continue;
            var t = (b - a) / len;
            geo.Pieces.Add(ProfileSolid.Piece(new ProfileSolid(a, t.PerpLeft, profile, t, len), color));
        }
    }

    // ------------------------------------------------------------------ sólidos auxiliares

    private static MarkingPiece Cyl(double r, double z0, double z1, MarkingColor c) =>
        Loft(new[] { (CircleRing(r), z0), (CircleRing(r), z1) }, c);

    private static MarkingPiece Block(Vec2 center, double w, double l, double z0, double z1, MarkingColor c) =>
        Loft(new[] { (RectRing(w, l, center), z0), (RectRing(w, l, center), z1) }, c);

    /// <summary>Sólido por seções horizontais (mesmo número de vértices), faces laterais trianguladas.</summary>
    public static MarkingPiece Loft(IReadOnlyList<(List<Vec2> Ring, double Z)> rings, MarkingColor color)
    {
        var faces = new List<List<Vec3>>
        {
            rings[0].Ring.Select(v => Vec3.At(v, rings[0].Z)).ToList(),
            rings[^1].Ring.Select(v => Vec3.At(v, rings[^1].Z)).ToList(),
        };
        for (int k = 0; k + 1 < rings.Count; k++)
        {
            var r0 = rings[k].Ring;
            var r1 = rings[k + 1].Ring;
            var z0 = rings[k].Z;
            var z1 = rings[k + 1].Z;
            for (int i = 0; i < r0.Count; i++)
            {
                var j = (i + 1) % r0.Count;
                faces.Add(new List<Vec3> { Vec3.At(r0[i], z0), Vec3.At(r0[j], z0), Vec3.At(r1[j], z1) });
                faces.Add(new List<Vec3> { Vec3.At(r0[i], z0), Vec3.At(r1[j], z1), Vec3.At(r1[i], z1) });
            }
        }
        var poly = new Polyhedron(faces);
        return Polyhedron.Piece(poly, color) with { Elevation = 0 };
    }

    private static List<Vec2> CircleRing(double r, int n = 24) => Ellipse(r, r, n);

    private static List<Vec2> Ellipse(double rx, double ry, int n = 24) =>
        Enumerable.Range(0, n).Select(i => { var t = 2 * Math.PI * i / n; return new Vec2(rx * Math.Cos(t), ry * Math.Sin(t)); }).ToList();

    private static List<Vec2> RectRing(double w, double l, Vec2? c = null)
    {
        var o = c ?? Vec2.Zero;
        return new List<Vec2> { o + new Vec2(-w / 2, -l / 2), o + new Vec2(w / 2, -l / 2), o + new Vec2(w / 2, l / 2), o + new Vec2(-w / 2, l / 2) };
    }

    /// <summary>Leva a peça do sistema local da unidade para a planta.</summary>
    private static MarkingPiece ToWorld(MarkingPiece p, LocalFrame f)
    {
        Vec2 Dir(Vec2 v) => f.ToWorld(v) - f.ToWorld(Vec2.Zero);
        var solid = p.Solid == null ? null : new Polyhedron(p.Solid.Faces.Select(face => face.Select(v => Vec3.At(f.ToWorld(v.XY), v.Z))));
        var prof = p.Profile == null ? null : p.Profile with { Origin = f.ToWorld(p.Profile.Origin), XDir = Dir(p.Profile.XDir), ExtrudeDir = Dir(p.Profile.ExtrudeDir) };
        return p with { Shape = f.ToWorld(p.Shape), Solid = solid, Profile = prof };
    }
}

/// <summary>Símbolos/legendas repetidos ao longo de um caminho.</summary>
public static class RepeatedGenerator
{
    public static MarkingGeometry Generate(Polyline2 path, double offset, double spacing, double start, double endSetback, bool reverse,
        Func<LocalFrame, MarkingGeometry> buildAt, double itemLength)
    {
        var geo = new MarkingGeometry();
        var p = reverse ? path.Reversed() : path;
        var off = p.Offset(reverse ? -offset : offset);
        var end = p.Length - Math.Max(0, endSetback);
        if (spacing <= 0.5) spacing = 0.5;
        int count = 0;
        for (var s = Math.Max(0, start); s + itemLength <= end + 1e-6 && count < 5000; s += spacing)
        {
            // Base do item em s; o item se estende no sentido do tráfego.
            var baseParam = p.ParamAt(s);
            var mid = Math.Min(end, s + itemLength / 2);
            var dir = p.TangentAt(mid);
            var frame = new LocalFrame(off.PointAtParam(baseParam), dir);
            var g = buildAt(frame);
            g.UnitCount = 0;
            g.PathLength = 0;
            geo.Merge(g);
            count++;
        }
        geo.UnitCount = count;
        geo.PathLength = p.Length;
        if (count == 0) geo.Warnings.Add("O trecho é curto demais para o espaçamento/início informados.");
        return geo;
    }
}
