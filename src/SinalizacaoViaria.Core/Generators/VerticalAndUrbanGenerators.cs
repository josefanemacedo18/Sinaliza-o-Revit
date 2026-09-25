using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Construção de volumes simples em coordenadas locais de um <see cref="LocalFrame"/>.</summary>
internal sealed class VolumeBuilder
{
    private readonly LocalFrame _f;
    public MarkingGeometry Geo { get; } = new();

    public VolumeBuilder(LocalFrame frame) => _f = frame;

    private Vec2 W(double x, double y) => _f.ToWorld(new Vec2(x, y));
    private Vec2 Right => _f.Forward.Normalized().PerpRight;
    private Vec2 Fwd => _f.Forward.Normalized();

    /// <summary>Caixa centrada em (x, y) com dimensões sx (lateral) × sy (frente), da elevação z0 com altura h.</summary>
    public void Box(double x, double y, double sx, double sy, double z0, double h, MarkingColor c, bool unit = false)
    {
        var poly = new Polygon2(new[] { W(x - sx / 2, y - sy / 2), W(x + sx / 2, y - sy / 2), W(x + sx / 2, y + sy / 2), W(x - sx / 2, y + sy / 2) });
        Geo.Pieces.Add(new MarkingPiece(poly, c) { Thickness = h, Elevation = z0, IsUnit = unit });
    }

    public void Cylinder(double x, double y, double d, double z0, double h, MarkingColor c)
    {
        var center = W(x, y);
        Geo.Pieces.Add(new MarkingPiece(new Polygon2(CurveTools.Circle(center, d / 2, 0.003)), c) { Thickness = h, Elevation = z0 });
    }

    /// <summary>Anel quadrado (canteiro de árvore).</summary>
    public void Ring(double size, double wall, double h, MarkingColor c)
    {
        var o = size / 2;
        var i = o - wall;
        var outer = new[] { W(-o, -o), W(o, -o), W(o, o), W(-o, o) };
        var inner = new[] { W(-i, -i), W(i, -i), W(i, i), W(-i, i) };
        Geo.Pieces.Add(new MarkingPiece(new Polygon2(outer, new[] { inner }), c) { Thickness = h });
    }

    /// <summary>Perfil vertical no plano "lateral × altura" passando por y, extrudado para a frente.</summary>
    public void ProfileFacingForward(Polygon2 profileXZ, double y, double depth, MarkingColor c)
    {
        var origin = W(0, y);
        var p = new ProfileSolid(origin, Right, profileXZ, Fwd, depth);
        Geo.Pieces.Add(ProfileSolid.Piece(p, c));
    }

    /// <summary>Perfil vertical no plano "frente × altura" passando por x, extrudado lateralmente.</summary>
    public void ProfileSideways(Polygon2 profileYZ, double x, double depth, MarkingColor c)
    {
        var origin = W(x, 0);
        var p = new ProfileSolid(origin, Fwd, profileYZ, Right, depth);
        Geo.Pieces.Add(ProfileSolid.Piece(p, c));
    }
}

/// <summary>Placas de sinalização vertical (chapa com orla, fundo e legenda) e seus suportes.</summary>
public static class SignGenerator
{
    public const double PlateThickness = 0.02;

    /// <summary>Contorno da chapa no plano da placa: X lateral (centrado), Y = altura acima do solo.</summary>
    public static Polygon2 Outline(FormaPlaca forma, double w, double h, double bottom)
    {
        switch (forma)
        {
            case FormaPlaca.Circulo:
                return new Polygon2(CurveTools.Circle(new Vec2(0, bottom + w / 2), w / 2, 0.002));
            case FormaPlaca.Octogono:
            {
                var r = w / 2 / Math.Cos(Math.PI / 8);
                var c = new Vec2(0, bottom + w / 2);
                return new Polygon2(Enumerable.Range(0, 8).Select(i => c + Vec2.FromAngle(Math.PI / 8 + i * Math.PI / 4) * r));
            }
            case FormaPlaca.TrianguloInvertido:
            {
                var th = w * Math.Sqrt(3) / 2;
                return new Polygon2(new[] { new Vec2(0, bottom), new Vec2(w / 2, bottom + th), new Vec2(-w / 2, bottom + th) });
            }
            case FormaPlaca.Losango:
            {
                var d = w * Math.Sqrt(2);
                return new Polygon2(new[] { new Vec2(0, bottom), new Vec2(d / 2, bottom + d / 2), new Vec2(0, bottom + d), new Vec2(-d / 2, bottom + d / 2) });
            }
            case FormaPlaca.Quadrado:
                return Polygon2.Rectangle(new Vec2(-w / 2, bottom), new Vec2(w / 2, bottom + w));
            case FormaPlaca.CruzSantoAndre:
            {
                // w = vão total; travessas com 1/6 do comprimento, a 50° da horizontal.
                var c = new Vec2(0, bottom + h / 2);
                var a = Math.Atan2(h, w);
                var len = Math.Sqrt(w * w + h * h);
                var bw = len / 6.5;
                var d1 = Vec2.FromAngle(a);
                var d2 = Vec2.FromAngle(Math.PI - a);
                var arms = PolygonOps.Union(PolygonOps.Strip(new[] { c - d1 * (len / 2), c + d1 * (len / 2) }, bw)
                    .Concat(PolygonOps.Strip(new[] { c - d2 * (len / 2), c + d2 * (len / 2) }, bw)));
                return arms[0];
            }
            default:
                return Polygon2.Rectangle(new Vec2(-w / 2, bottom), new Vec2(w / 2, bottom + h));
        }
    }

    /// <summary>Altura total ocupada pela chapa.</summary>
    public static double PlateHeight(FormaPlaca forma, double w, double h) => forma switch
    {
        FormaPlaca.Circulo or FormaPlaca.Octogono or FormaPlaca.Quadrado => w,
        FormaPlaca.TrianguloInvertido => w * Math.Sqrt(3) / 2,
        FormaPlaca.Losango => w * Math.Sqrt(2),
        _ => h,  // retângulo e cruz de Santo André
    };

    /// <summary>
    /// Face da placa em "elevação" (X lateral, Y altura): peças de orla, fundo e legenda –
    /// usada tanto para o 3D quanto para a pré-visualização.
    /// </summary>
    public static List<(Polygon2 Shape, MarkingColor Color, int Layer)> Face(PlacaDef p, double w, double h, double bottom, string? legend, IGlyphOutlineProvider glyphs)
    {
        var res = new List<(Polygon2, MarkingColor, int)>();
        var outline = Outline(p.Forma, w, h, bottom);
        var border = p.Orla * w;
        List<Polygon2> inner;
        if (border > 0.002)
        {
            res.Add((outline, p.CorOrla, 0));
            inner = PolygonOps.Offset(new[] { outline }, -border);
            foreach (var i in inner) res.Add((i, p.CorFundo, 1));
        }
        else
        {
            res.Add((outline, p.CorFundo, 0));
            inner = new List<Polygon2> { outline };
        }

        var text = legend ?? p.Legenda;
        if ((p.Pictograma is { Count: > 0 } || p.Proibicao) && inner.Count > 0)
        {
            res.AddRange(PictogramRenderer.Render(p, inner[0], text, glyphs, border));
            return res;
        }
        if (!string.IsNullOrWhiteSpace(text) && text != "-" && inner.Count > 0)
        {
            var (mn, mx) = inner[0].Bounds;
            var availW = (mx.X - mn.X) * (p.Forma is FormaPlaca.Losango or FormaPlaca.TrianguloInvertido ? 0.5 : 0.8);
            var availH = (mx.Y - mn.Y) * 0.8;
            var lines = text.Split('\n').Length;
            var th = Math.Min(availH / lines * 0.75, p.Forma == FormaPlaca.Octogono ? w * 0.28 : w * 0.40);
            var cy = p.Forma == FormaPlaca.TrianguloInvertido ? mn.Y + (mx.Y - mn.Y) * 0.62 : (mn.Y + mx.Y) / 2;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var total = lines * th + (lines - 1) * th * 0.4;
                var frame = new LocalFrame(new Vec2(0, cy - total / 2), Vec2.UnitY);
                var g = TextGenerator.Generate(text, new TextOptions { Height = th, WidthFactor = 0.62, LetterSpacing = th * 0.08, LineSpacing = th * 0.4, BottomToTop = false }, frame, p.CorLegenda, glyphs);
                var b = g.Bounds;
                if (b == null) break;
                if (b.Value.Max.X - b.Value.Min.X <= availW || attempt == 5)
                {
                    foreach (var piece in g.Pieces) res.Add((piece.Shape, piece.Color, 2));
                    break;
                }
                th *= availW / (b.Value.Max.X - b.Value.Min.X) * 0.98;
            }
        }
        return res;
    }

    public static MarkingGeometry Generate(SignDefinition d, PlacaDef p, IGlyphOutlineProvider glyphs)
    {
        var geo = new MarkingGeometry();
        var w = d.Width ?? p.Largura;
        var h = d.Height ?? p.Altura;
        var plateH = PlateHeight(p.Forma, w, h);
        var f = d.Direction.Length < 1e-9 ? Vec2.UnitY : d.Direction.Normalized();
        var right = f.PerpRight;            // direita do condutor = eixo X da face
        var toDriver = -f;                  // a face aponta para quem chega
        var postR = d.Support == TipoSuporte.Nenhum ? 0.01 : d.PostDiameter / 2;
        var center = d.Position + right * d.LateralOffset;
        var backPlane = center + toDriver * (postR + 0.002);
        var bottom = d.MountHeight;

        foreach (var (shape, color, layer) in Face(p, w, h, bottom, d.Legend, glyphs))
        {
            // Camadas do pictograma empilhadas (0,5 mm cada) para não haver faces coincidentes.
            var offset = layer switch { 0 => 0.0, 1 => PlateThickness, _ => PlateThickness + 0.002 + (layer - 2) * 0.0005 };
            var depth = layer == 0 ? PlateThickness : layer == 1 ? 0.002 : 0.0005;
            var origin = backPlane + toDriver * offset;
            geo.Pieces.Add(ProfileSolid.Piece(new ProfileSolid(origin, right, shape, toDriver, depth), color));
        }

        var postTop = bottom + plateH;
        void Post(Vec2 at) => geo.Pieces.Add(new MarkingPiece(new Polygon2(CurveTools.Circle(at, d.PostDiameter / 2, 0.002)), MarkingColor.Metal)
        {
            Thickness = postTop,
        });
        switch (d.Support)
        {
            case TipoSuporte.Simples: Post(d.Position); break;
            case TipoSuporte.Duplo:
                Post(center - right * (w / 3));
                Post(center + right * (w / 3));
                break;
        }
        geo.UnitCount = 1;
        geo.PathLength = 0;
        if (bottom < 2.0 && d.Support != TipoSuporte.Nenhum)
            geo.Warnings.Add($"Altura livre de {bottom:0.00} m – em calçadas o MBST recomenda no mínimo 2,10 m sob a placa.");
        return geo;
    }
}

/// <summary>Mobiliário e elementos urbanísticos viários (volumes esquemáticos, com dimensões reais).</summary>
public static class UrbanGenerator
{
    public static MarkingGeometry BuildAt(MobiliarioDef m, LocalFrame frame, double? lOverride, double? wOverride, double? hOverride, MarkingColor? colorOverride) =>
        UrbanDesigns.Build(m, frame, lOverride ?? m.Comprimento, wOverride ?? m.Largura, hOverride ?? m.Altura, colorOverride ?? m.Cor);

    public static MarkingGeometry Generate(UrbanElementDefinition d, MobiliarioDef m, Polyline2? path)
    {
        if (!d.UsePath || path == null)
        {
            var frame = new LocalFrame(d.Position, d.Direction.Length < 1e-9 ? Vec2.UnitY : d.Direction);
            return BuildAt(m, frame, d.Length, d.Width, d.Height, d.Color);
        }
        var spacing = d.Spacing ?? (m.Espacamento > 0 ? m.Espacamento : 10);
        var geo = new MarkingGeometry();
        var off = path.Offset(d.Offset);
        var end = path.Length - Math.Max(0, d.EndSetback);
        int n = 0;
        for (var s = Math.Max(0, d.StartOffset); s <= end + 1e-6 && n < 5000; s += Math.Max(0.3, spacing))
        {
            var t = path.TangentAt(s);
            var frame = new LocalFrame(off.PointAtParam(path.ParamAt(s)), t.Rotate(Angles.ToRad(d.RotationDeg)));
            var g = BuildAt(m, frame, d.Length, d.Width, d.Height, d.Color);
            g.UnitCount = 0;
            geo.Merge(g);
            n++;
        }
        geo.UnitCount = n;
        geo.PathLength = path.Length;
        if (n == 0) geo.Warnings.Add("O trecho é curto demais para o espaçamento informado.");
        return geo;
    }
}

/// <summary>
/// Rampas de calçada (NBR 9050): superfícies planas inclinadas contínuas (rampa central, abas laterais
/// triangulares), piso tátil de alerta e direcional acompanhando a inclinação, guias rebaixadas de
/// veículos e rebaixamento total para calçadas estreitas.
/// </summary>
public static class RampGenerator
{
    /// <summary>Referencial: origem no meio-fio (centro da rampa), Up = subida (para dentro da calçada), Side = lateral.</summary>
    public readonly record struct Frame(Vec2 Curb, Vec2 Up, Vec2 Side)
    {
        public Vec2 P(double side, double up) => Curb + Side * side + Up * up;
    }

    public static Frame FrameOf(Polyline2 path)
    {
        var a = path.Points[0];
        var b = path.Points[^1];
        var up = (b - a).Normalized();
        if (up.Length < 0.5) up = Vec2.UnitY;
        return new Frame(a, up, up.PerpLeft);
    }

    /// <summary>Largura, comprimento da subida e comprimento de cada aba (ou das rampas laterais no rebaixamento total).</summary>
    public static (double Width, double Length, double Flare) Dimensions(RampDefinition d)
    {
        var slope = Math.Max(0.01, d.Type == TipoRampa.AcessoVeiculos ? Math.Max(d.Slope, 0.10) : d.Slope);
        var run = d.Height / slope;
        return d.Type switch
        {
            TipoRampa.RebaixamentoComAbas => (d.Width, run, d.Height / Math.Max(0.01, d.FlareSlope)),
            TipoRampa.AcessoVeiculos => (d.Width, run, Math.Min(0.60, run)),
            TipoRampa.RebaixamentoTotal => (d.Width, d.SidewalkDepth, run),
            _ => (d.Width, run, 0),
        };
    }

    /// <summary>Área ocupada em planta (usada para recortar a calçada).</summary>
    public static Polygon2 Footprint(RampDefinition d, Polyline2 path)
    {
        var f = FrameOf(path);
        var (w, len, flare) = Dimensions(d);
        var hw = w / 2;
        const double e = 0.05; // avança sobre a face do meio-fio para garantir o recorte da guia
        if (d.Type == TipoRampa.RebaixamentoTotal)
            return new Polygon2(new[] { f.P(-hw - flare, -e), f.P(hw + flare, -e), f.P(hw + flare, len), f.P(-hw - flare, len) });
        return new Polygon2(new[] { f.P(-hw - flare, -e), f.P(hw + flare, -e), f.P(hw, len), f.P(-hw, len) });
    }

    public static MarkingGeometry Generate(RampDefinition d, Polyline2 path)
    {
        var geo = new MarkingGeometry();
        var f = FrameOf(path);
        var (w, len, flare) = Dimensions(d);
        var h = d.Height;
        var hw = w / 2;
        var slope = h / Math.Max(1e-6, len);
        const MarkingColor concrete = MarkingColor.Concreto;

        if (d.Type == TipoRampa.RebaixamentoTotal)
        {
            // Platô no nível da pista (placa fina) + rampas laterais subindo ao longo do meio-fio.
            var depth = len;
            geo.Pieces.Add(Polyhedron.Piece(Polyhedron.Prism(new[] { f.P(-hw, 0), f.P(hw, 0), f.P(hw, depth), f.P(-hw, depth) }, _ => 0, _ => 0.005), concrete));
            foreach (var s in new[] { 1.0, -1.0 })
            {
                var a0 = f.P(s * hw, 0);
                var a1 = f.P(s * (hw + flare), 0);
                var a2 = f.P(s * (hw + flare), depth);
                var a3 = f.P(s * hw, depth);
                double Top(Vec2 p) => Math.Clamp(Math.Abs((p - f.Curb).Dot(f.Side)) - hw, 0, flare) / flare * h;
                geo.Pieces.Add(Polyhedron.Piece(Polyhedron.Prism(new[] { a0, a1, a2, a3 }, _ => 0, Top), concrete));
            }
            if (d.Tactile)
            {
                var t0 = d.TactileSetback;
                var t1 = t0 + d.TactileWidth;
                TactileGenerator.AddTiles(geo, TactileGenerator.Rect(f.P(-hw + 0.01, t0), f.Up, t1 - t0, 2 * hw - 0.02, 0.25, true), d.TactileColor, 0.005);
            }
            Annotate(geo, f, w, depth, $"i ≤ {d.Slope * 100:0.##} %  (rebaixamento total)", arrowAlongSide: true, flare);
            return Finish(geo, d, w);
        }

        // Rampa central: plano inclinado do meio-fio (z = 0) até o fim da subida (z = h).
        double RampZ(Vec2 p) => Math.Clamp((p - f.Curb).Dot(f.Up), 0, len) * slope;
        var main = Polyhedron.Prism(new[] { f.P(-hw, 0), f.P(hw, 0), f.P(hw, len), f.P(-hw, len) }, _ => 0, RampZ);
        geo.Pieces.Add(Polyhedron.Piece(main, concrete));

        // Abas laterais: triângulos com superfície plana passando por (meio-fio, 0), (aba, h) e (fim da rampa, h).
        if (flare > 0.01)
        {
            foreach (var s in new[] { 1.0, -1.0 })
            {
                var p1 = f.P(s * hw, 0);
                var p2 = f.P(s * (hw + flare), 0);
                var p3 = f.P(s * hw, len);
                var top = new[] { Vec3.At(p1, 0), Vec3.At(p2, h), Vec3.At(p3, h) };
                var bottom = new[] { Vec3.At(p1, 0), Vec3.At(p2, 0), Vec3.At(p3, 0) };
                var faces = new List<IEnumerable<Vec3>>
                {
                    bottom,
                    top,
                    new[] { Vec3.At(p1, 0), Vec3.At(p2, 0), Vec3.At(p2, h) },                    // face no meio-fio
                    new[] { Vec3.At(p2, 0), Vec3.At(p3, 0), Vec3.At(p3, h), Vec3.At(p2, h) },    // face encostada na calçada
                    new[] { Vec3.At(p3, 0), Vec3.At(p1, 0), Vec3.At(p3, h) },                    // face encostada na rampa
                };
                geo.Pieces.Add(Polyhedron.Piece(new Polyhedron(faces), concrete));
            }
        }

        // Piso tátil de alerta sobre a rampa, acompanhando a inclinação (5 mm acima da superfície).
        if (d.Tactile && d.Type != TipoRampa.AcessoVeiculos)
        {
            var t0 = Math.Clamp(d.TactileSetback, 0, len);
            var t1 = Math.Clamp(t0 + d.TactileWidth, 0, len);
            // Placas moduladas de 0,25 m com relevo, assentadas sobre o plano inclinado.
            void Sloped(IEnumerable<TactileGenerator.Tile> tiles)
            {
                foreach (var t in tiles)
                {
                    geo.Pieces.Add(Polyhedron.Piece(Polyhedron.Prism(t.Shape.Outer, RampZ, p => RampZ(p) + TactileGenerator.TileThickness), d.TactileColor));
                    foreach (var r in t.Relief)
                        geo.Pieces.Add(Polyhedron.Piece(Polyhedron.Prism(r.Outer, p => RampZ(p) + TactileGenerator.TileThickness,
                            p => RampZ(p) + TactileGenerator.TileThickness + TactileGenerator.ReliefHeight), MarkingColor.RelevoTatil));
                }
            }
            if (t1 - t0 > 0.02) Sloped(TactileGenerator.Rect(f.P(-hw + 0.01, t0), f.Up, t1 - t0, 2 * hw - 0.02, 0.25, true));
            if (d.DirectionalTactile && len - t1 > 0.1) Sloped(TactileGenerator.Rect(f.P(-0.125, t1), f.Up, len - t1, 0.25, 0.25, false));
        }

        Annotate(geo, f, w, len, d.Type == TipoRampa.AcessoVeiculos ? $"Guia rebaixada  i = {slope * 100:0.#} %" : $"i = {slope * 100:0.##} %", false, flare);
        return Finish(geo, d, w);
    }

    /// <summary>Seta de subida e indicação da inclinação (visíveis na representação 2D).</summary>
    private static void Annotate(MarkingGeometry geo, Frame f, double w, double len, string text, bool arrowAlongSide, double flare)
    {
        if (!arrowAlongSide)
        {
            var a = f.P(0, len * 0.15);
            var b = f.P(0, len * 0.85);
            geo.Annotations.Add(new AnnotationLine(new[] { a, b }, MarkingColor.Preta));
            geo.Annotations.Add(new AnnotationLine(new[] { b + f.Side * 0.12 - f.Up * 0.2, b, b - f.Side * 0.12 - f.Up * 0.2 }, MarkingColor.Preta));
            geo.Annotations.Add(new AnnotationText(f.P(0, len + 0.65), text, 2.0));
        }
        else
        {
            foreach (var s in new[] { 1.0, -1.0 })
            {
                var a = f.P(s * (w / 2 + 0.1), len / 2);
                var b = f.P(s * (w / 2 + flare - 0.1), len / 2);
                geo.Annotations.Add(new AnnotationLine(new[] { a, b }, MarkingColor.Preta));
                var dir = (b - a).Normalized();
                geo.Annotations.Add(new AnnotationLine(new[] { b - dir * 0.2 + dir.PerpLeft * 0.12, b, b - dir * 0.2 - dir.PerpLeft * 0.12 }, MarkingColor.Preta));
            }
            geo.Annotations.Add(new AnnotationText(f.P(0, len + 0.65), text, 2.0));
        }
    }

    private static MarkingGeometry Finish(MarkingGeometry geo, RampDefinition d, double w)
    {
        geo.UnitCount = 1;
        geo.PathLength = w;
        if (d.Type != TipoRampa.AcessoVeiculos)
        {
            if (d.Slope > 0.0833 + 1e-6) geo.Warnings.Add($"Inclinação de {d.Slope * 100:0.##} % acima do máximo de 8,33 % (NBR 9050).");
            if (w < 1.50 - 1e-6) geo.Warnings.Add($"Largura de {w:0.00} m abaixo da mínima recomendada de 1,50 m para rebaixamentos (NBR 9050).");
            if (d.Type == TipoRampa.RebaixamentoComAbas && d.FlareSlope > 0.10 + 1e-6) geo.Warnings.Add("Abas laterais com inclinação acima de 10 % (NBR 9050).");
        }
        return geo;
    }
}

/// <summary>Quebra-molas (ondulações A/B), faixas elevadas e lombadas invertidas.</summary>
public static class TrafficCalmingGenerator
{
    public static (double Length, double Height, double Ramp) Defaults(TipoModeracao t) => t switch
    {
        TipoModeracao.OndulacaoA => (3.70, 0.08, 0),
        TipoModeracao.OndulacaoB => (1.50, 0.06, 0),
        TipoModeracao.FaixaElevada => (8.00, 0.15, 1.50),
        _ => (2.50, 0.10, 0),
    };

    public static MarkingGeometry Generate(TrafficCalmingDefinition d, Polyline2 path, Catalogo cat)
    {
        var geo = new MarkingGeometry();
        var a = path.Points[0];
        var b = path.Points[^1];
        var width = a.DistanceTo(b);
        if (width < 0.5) { geo.Warnings.Add("Largura da pista muito pequena."); return geo; }
        var u = (b - a) / width;             // através da pista
        var t = u.PerpRight;                 // sentido do tráfego
        var (dl, dh, dr) = Defaults(d.Type);
        var L = d.Length ?? dl;
        var H = d.Height ?? dh;
        var R = Math.Min(d.RampLength ?? dr, L / 2 - 0.1);
        var dip = d.Type == TipoModeracao.LombadaInvertida;

        double Z(double x) // x em [-L/2, L/2]
        {
            var ax = Math.Abs(x);
            if (ax >= L / 2) return 0;
            if (d.Type == TipoModeracao.FaixaElevada)
                return ax <= L / 2 - R ? H : H * (L / 2 - ax) / R;
            var k = 2 * ax / L;
            var z = H * (1 - k * k);
            return dip ? -z : z;
        }

        // Volume
        const int n = 32;
        var top = new List<Vec2>();
        for (int i = 0; i <= n; i++)
        {
            var x = -L / 2 + L * i / n;
            top.Add(new Vec2(x, Z(x)));
        }
        List<Vec2> ring;
        if (dip)
        {
            ring = new List<Vec2>(top);
            for (int i = n; i >= 0; i--) ring.Add(new Vec2(top[i].X, top[i].Y - 0.10));
        }
        else
        {
            // O perfil começa e termina em z = 0: a base fecha o contorno.
            ring = new List<Vec2>(top);
        }
        var profile = PolygonOps.FromContours(new[] { (IReadOnlyList<Vec2>)ring });
        foreach (var p in profile)
            geo.Pieces.Add(ProfileSolid.Piece(new ProfileSolid(a, t, p, u, width), dip ? MarkingColor.Concreto : MarkingColor.Asfalto));

        // Pintura sobre a superfície
        if (d.Marking)
        {
            var footprint = new Polygon2(new[] { a - t * (L / 2), b - t * (L / 2), b + t * (L / 2), a + t * (L / 2) });
            var paint = new List<(Polygon2 Shape, MarkingColor Color)>();
            if (d.Type == TipoModeracao.FaixaElevada)
            {
                var white = d.MarkingColor ?? MarkingColor.Branca;
                // Rampas: triângulos apontando para o platô.
                foreach (var side in new[] { -1.0, 1.0 })
                {
                    for (double s = 0.5; s + 0.5 <= width; s += 1.0)
                    {
                        var baseC = a + u * (s + 0.25) + t * (side * L / 2);
                        var apex = baseC - t * (side * R * 0.9);
                        paint.Add((new Polygon2(new[] { baseC - u * 0.25, baseC + u * 0.25, apex }), white));
                    }
                }
                if (d.Crosswalk && cat.Linear("FTP-1") is { } ftp)
                {
                    var plateau = L - 2 * R;
                    var zebra = LinearPatternGenerator.Generate(new Polyline2(new[] { a, b }), ftp, ftp.Variantes[0],
                        new LinearOptions { WidthOverride = Math.Max(1, plateau - 0.6) });
                    foreach (var pc in zebra.Pieces)
                        geo.Pieces.Add(pc with { Elevation = H + 0.001, Thickness = 0.003 });
                }
            }
            else if (!dip && cat.Hachura("MOT") is { } mot)
            {
                var h = HatchGenerator.Generate(footprint, mot, new HatchOptions { ReferenceDirection = u, BorderWidth = 0, BarColor = d.MarkingColor });
                paint.AddRange(h.Pieces.Select(p => (p.Shape, p.Color)));
            }
            geo.Pieces.AddRange(DrapeOnProfile(paint, a, t, Z, -L / 2, L / 2));
        }

        geo.UnitCount = 1;
        geo.PathLength = width;
        return geo;
    }

    /// <summary>Fatia polígonos de pintura em faixas estreitas, elevando cada fatia até a superfície do perfil.</summary>
    public static IEnumerable<MarkingPiece> DrapeOnProfile(IEnumerable<(Polygon2 Shape, MarkingColor Color)> paint, Vec2 origin, Vec2 t,
        Func<double, double> z, double x0, double x1, double band = 0.20)
    {
        var list = paint.ToList();
        if (list.Count == 0) yield break;
        var n = t.PerpLeft;
        for (var x = x0; x < x1 - 1e-9; x += band)
        {
            var xe = Math.Min(x1, x + band);
            var strip = new Polygon2(new[] { origin + t * x - n * 1e4, origin + t * xe - n * 1e4, origin + t * xe + n * 1e4, origin + t * x + n * 1e4 });
            var elev = Math.Max(z(x), Math.Max(z(xe), z((x + xe) / 2))) + 0.001;
            foreach (var group in list.GroupBy(p => p.Color))
                foreach (var piece in PolygonOps.Intersect(group.Select(g => g.Shape), new[] { strip }))
                {
                    var s = piece.Simplified();
                    if (s != null) yield return new MarkingPiece(s, group.Key) { Elevation = elev, Thickness = 0.003 };
                }
        }
    }
}
