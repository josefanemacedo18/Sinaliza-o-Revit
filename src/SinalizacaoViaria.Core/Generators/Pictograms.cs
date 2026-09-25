using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Desenha os pictogramas das placas a partir da descrição do catálogo (itens simples + silhuetas
/// esquemáticas de veículos, pessoas e animais). Coordenadas em unidades da área útil da placa.
/// </summary>
public static class PictogramRenderer
{
    /// <summary>Área útil (centro e tamanho da unidade, em m) conforme a forma da placa.</summary>
    public static (Vec2 Center, double Unit) SafeArea(FormaPlaca forma, Polygon2 inner)
    {
        var (mn, mx) = inner.Bounds;
        var w = mx.X - mn.X;
        var h = mx.Y - mn.Y;
        var c = (mn + mx) / 2;
        return forma switch
        {
            FormaPlaca.Circulo or FormaPlaca.Octogono => (c, w * 0.80),
            FormaPlaca.Losango => (c, w * 0.56),
            FormaPlaca.TrianguloInvertido => (new Vec2(c.X, mn.Y + h * 0.64), w * 0.40),
            _ => (c, Math.Min(w, h) * 0.90),
        };
    }

    /// <summary>Peças do pictograma, na ordem de desenho (cada item em uma camada acima da anterior).</summary>
    public static List<(Polygon2 Shape, MarkingColor Color, int Layer)> Render(PlacaDef p, Polygon2 inner, string? legend,
        IGlyphOutlineProvider glyphs, double borderWidth)
    {
        var res = new List<(Polygon2, MarkingColor, int)>();
        var (center, unit) = SafeArea(p.Forma, inner);
        Vec2 T(double x, double y) => center + new Vec2(x, y) * unit;
        Vec2 TP(double[] a) => T(a[0], a.Length > 1 ? a[1] : 0);
        int layer = 2;

        foreach (var item in p.Pictograma ?? new List<PictoItem>())
        {
            var color = item.Vazado ? p.CorFundo : item.Cor ?? p.CorLegenda;
            List<Polygon2> shapes;
            try
            {
                shapes = Item(item, TP, unit, legend, glyphs);
            }
            catch
            {
                continue; // item mal definido no catálogo do usuário: ignora
            }
            shapes = PolygonOps.Intersect(shapes, new[] { inner });
            foreach (var s in shapes) res.Add((s, color, layer));
            layer++;
        }

        if (p.Proibicao)
        {
            var (mn, mx) = inner.Bounds;
            var c = (mn + mx) / 2;
            var r = (mx.X - mn.X) / 2;
            var d = new Vec2(1, -1).Normalized();
            var bar = PolygonOps.Strip(new[] { c - d * r * 1.1, c + d * r * 1.1 }, Math.Max(borderWidth, r * 0.16));
            foreach (var s in PolygonOps.Intersect(bar, new[] { inner })) res.Add((s, MarkingColor.Vermelha, layer));
        }
        return res;
    }

    private static List<Polygon2> Item(PictoItem it, Func<double[], Vec2> tp, double unit, string? legend, IGlyphOutlineProvider glyphs)
    {
        var pts = it.Pts?.Select(tp).ToList() ?? new List<Vec2>();
        var c = it.C != null ? tp(it.C) : tp(new[] { 0.0, 0.0 });
        switch (it.Tipo.ToLowerInvariant())
        {
            case "linha":
                return PolygonOps.Strip(pts, it.W * unit, roundJoins: true);
            case "seta":
            {
                var w = it.W * unit;
                var res = SymbolBuilder.StrokeArrow(pts, w, w * it.Cabeca, w * it.Cabeca * 0.9);
                if (it.Dupla)
                {
                    var rev = Enumerable.Reverse(pts).ToList();
                    res = PolygonOps.Union(res.Concat(SymbolBuilder.StrokeArrow(rev, w, w * it.Cabeca, w * it.Cabeca * 0.9)));
                }
                return res;
            }
            case "poligono":
                return PolygonOps.Union(new[] { new Polygon2(pts) });
            case "retangulo":
                return new List<Polygon2> { Polygon2.Rectangle(new Vec2(Math.Min(pts[0].X, pts[1].X), Math.Min(pts[0].Y, pts[1].Y)),
                    new Vec2(Math.Max(pts[0].X, pts[1].X), Math.Max(pts[0].Y, pts[1].Y))) };
            case "circulo":
                return new List<Polygon2> { new Polygon2(CurveTools.Circle(c, it.R * unit, unit * 0.002)) };
            case "anel":
            {
                var ro = (it.R + it.W / 2) * unit;
                var ri = (it.R - it.W / 2) * unit;
                var outer = new Polygon2(CurveTools.Circle(c, ro, unit * 0.002));
                return ri > 0 ? PolygonOps.Difference(new[] { outer }, new[] { new Polygon2(CurveTools.Circle(c, ri, unit * 0.002)) }) : new List<Polygon2> { outer };
            }
            case "texto":
            {
                var text = it.Editavel && !string.IsNullOrWhiteSpace(legend) && legend != "-" ? legend! : it.Texto ?? "";
                return Text(text, c, it.H * unit, it.W > 0.08 ? it.W * unit : unit * 0.95, glyphs);
            }
            case "silhueta":
            {
                var local = Silhouettes.Get(it.Nome ?? "");
                var rot = it.Rot * Math.PI / 180;
                var (cs, sn) = (Math.Cos(rot), Math.Sin(rot));
                var k = it.K * unit;
                Vec2 Map(Vec2 v)
                {
                    var x = it.Espelhar ? -v.X : v.X;
                    var y = v.Y;
                    return c + new Vec2(x * cs - y * sn, x * sn + y * cs) * k;
                }
                return local.Select(s => s.Transform(Map)).ToList();
            }
            default:
                return new List<Polygon2>();
        }
    }

    /// <summary>Texto centrado em <paramref name="center"/>, reduzido para caber em <paramref name="maxWidth"/>.</summary>
    public static List<Polygon2> Text(string text, Vec2 center, double height, double maxWidth, IGlyphOutlineProvider glyphs)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        var th = height;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var g = TextGenerator.Generate(text, new TextOptions { Height = th, WidthFactor = 0.62, LetterSpacing = th * 0.08, LineSpacing = th * 0.4, BottomToTop = false },
                new LocalFrame(Vec2.Zero, Vec2.UnitY), MarkingColor.Preta, glyphs);
            var b = g.Bounds;
            if (b == null) return new();
            var bw = b.Value.Max.X - b.Value.Min.X;
            if (bw <= maxWidth || attempt == 5)
            {
                var shift = center - (b.Value.Min + b.Value.Max) / 2;
                return g.Pieces.Select(p => p.Shape.Transform(v => v + shift)).ToList();
            }
            th *= maxWidth / bw * 0.98;
        }
        return new();
    }
}

/// <summary>Silhuetas esquemáticas (vista lateral voltada para +X, cabem em −0,5…0,5).</summary>
public static class Silhouettes
{
    private static readonly Dictionary<string, Func<List<Polygon2>>> Library = new(StringComparer.OrdinalIgnoreCase)
    {
        ["carro"] = Car,
        ["carroTopo"] = CarTop,
        ["caminhao"] = Truck,
        ["onibus"] = Bus,
        ["moto"] = Motorcycle,
        ["bicicleta"] = Bicycle,
        ["pedestre"] = () => Pedestrian(1.0),
        ["crianca"] = () => Pedestrian(0.72),
        ["criancas"] = Children,
        ["trator"] = Tractor,
        ["animal"] = Cow,
        ["cervo"] = Deer,
        ["carroca"] = Cart,
        ["trem"] = Train,
        ["bonde"] = Tram,
        ["aviao"] = Plane,
        ["cadeirante"] = Wheelchair,
        ["trabalhador"] = Worker,
        ["carrodemao"] = HandCart,
        ["cruz"] = () => new() { Rect(-0.1, -0.4, 0.1, 0.4), Rect(-0.4, -0.1, 0.4, 0.1) },
        ["bomba"] = FuelPump,
        ["talheres"] = Cutlery,
        ["cama"] = Bed,
        ["telefone"] = Phone,
        ["chave"] = Wrench,
        ["pneu"] = Tyre,
        ["taxi"] = Taxi,
        ["informacao"] = Info,
        ["buzina"] = Horn,
        ["corrente"] = Chain,
        ["vento"] = Wind,
        ["pedras"] = Rocks,
        ["cascalho"] = Gravel,
        ["arvore"] = Tree,
        ["barco"] = Boat,
        ["policia"] = Shield,
        ["banheiro"] = Toilet,
    };

    public static IReadOnlyCollection<string> Names => Library.Keys;

    public static List<Polygon2> Get(string name) =>
        Library.TryGetValue(name, out var f) ? f() : new List<Polygon2> { Rect(-0.3, -0.3, 0.3, 0.3) };

    // ------------------------------------------------------------------ primitivas

    private static Polygon2 Rect(double x0, double y0, double x1, double y1) => Polygon2.Rectangle(new Vec2(x0, y0), new Vec2(x1, y1));
    private static Polygon2 Circ(double x, double y, double r) => new(CurveTools.Circle(new Vec2(x, y), r, 0.002));
    private static Polygon2 Poly(params double[] xy) => new(Enumerable.Range(0, xy.Length / 2).Select(i => new Vec2(xy[2 * i], xy[2 * i + 1])));
    private static List<Polygon2> Stroke(double w, params double[] xy) =>
        PolygonOps.Strip(Enumerable.Range(0, xy.Length / 2).Select(i => new Vec2(xy[2 * i], xy[2 * i + 1])).ToList(), w, roundJoins: true);
    private static List<Polygon2> Ring(double x, double y, double r, double w) =>
        PolygonOps.Difference(new[] { Circ(x, y, r + w / 2) }, new[] { Circ(x, y, r - w / 2) });
    private static List<Polygon2> U(params IEnumerable<Polygon2>[] parts) => PolygonOps.Union(parts.SelectMany(p => p));
    private static List<Polygon2> L(params Polygon2[] p) => p.ToList();
    private static List<Polygon2> Cut(List<Polygon2> a, params Polygon2[] holes) => PolygonOps.Difference(a, holes);

    /// <summary>Roda com "folga" em relação à carroceria.</summary>
    private static List<Polygon2> WithWheels(List<Polygon2> body, double r, params double[] xs)
    {
        var y = -0.14;
        var cut = Cut(body, xs.Select(x => Circ(x, y, r * 1.3)).ToArray());
        return U(cut, xs.Select(x => Circ(x, y, r)));
    }

    // ------------------------------------------------------------------ veículos

    private static List<Polygon2> Car()
    {
        var body = L(Poly(-0.48, -0.14, 0.48, -0.14, 0.48, 0.0, 0.40, 0.05, 0.22, 0.07, 0.10, 0.22, -0.22, 0.22, -0.34, 0.07, -0.48, 0.05));
        body = Cut(body, Poly(0.07, 0.10, -0.03, 0.10, -0.03, 0.18, 0.04, 0.18), Poly(-0.07, 0.10, -0.29, 0.10, -0.20, 0.18, -0.07, 0.18));
        return WithWheels(body, 0.09, -0.28, 0.28);
    }

    /// <summary>Carro visto de cima (frente para +Y).</summary>
    private static List<Polygon2> CarTop()
    {
        var body = L(Poly(-0.2, -0.44, 0.2, -0.44, 0.24, -0.36, 0.24, 0.34, 0.18, 0.46, -0.18, 0.46, -0.24, 0.34, -0.24, -0.36));
        return Cut(body, Poly(-0.17, 0.12, 0.17, 0.12, 0.14, 0.26, -0.14, 0.26), Poly(-0.16, -0.3, 0.16, -0.3, 0.14, -0.2, -0.14, -0.2));
    }

    private static List<Polygon2> Taxi()
    {
        var car = Car();
        return U(car, L(Rect(-0.1, 0.24, 0.02, 0.29)));
    }

    private static List<Polygon2> Truck()
    {
        var box = Rect(-0.5, -0.14, 0.18, 0.26);
        var cab = Poly(0.22, -0.14, 0.5, -0.14, 0.5, 0.06, 0.42, 0.18, 0.22, 0.18);
        var body = Cut(L(box, cab), Poly(0.28, 0.06, 0.44, 0.06, 0.40, 0.14, 0.28, 0.14));
        return WithWheels(body, 0.08, -0.38, -0.2, 0.36);
    }

    private static List<Polygon2> Bus()
    {
        var body = L(Poly(-0.5, -0.14, 0.5, -0.14, 0.5, 0.2, 0.46, 0.24, -0.5, 0.24));
        var windows = Enumerable.Range(0, 5).Select(i => Rect(-0.44 + i * 0.17, 0.06, -0.31 + i * 0.17, 0.18)).ToArray();
        body = Cut(body, windows);
        return WithWheels(body, 0.08, -0.32, 0.32);
    }

    private static List<Polygon2> Motorcycle()
    {
        var wheels = U(Ring(-0.3, -0.14, 0.13, 0.05), Ring(0.3, -0.14, 0.13, 0.05));
        var frame = U(Stroke(0.06, -0.3, -0.14, -0.05, 0.02, 0.18, 0.02, 0.3, -0.14), Stroke(0.05, 0.18, 0.02, 0.24, 0.18, 0.14, 0.2),
            L(Poly(-0.15, 0.0, 0.1, 0.0, 0.06, 0.08, -0.12, 0.07)));
        var rider = U(L(Circ(0.02, 0.4, 0.07)), Stroke(0.08, 0.0, 0.3, -0.04, 0.08, 0.08, 0.02), Stroke(0.06, 0.0, 0.26, 0.16, 0.19));
        return U(wheels, frame, rider);
    }

    private static List<Polygon2> Bicycle()
    {
        var wheels = U(Ring(-0.28, -0.12, 0.17, 0.045), Ring(0.28, -0.12, 0.17, 0.045));
        var frame = Stroke(0.045, -0.28, -0.12, -0.05, -0.12, 0.16, 0.1, -0.1, 0.1, -0.05, -0.12);
        var fork = Stroke(0.045, 0.28, -0.12, 0.16, 0.1, 0.12, 0.2, 0.2, 0.2);
        var seat = U(Stroke(0.045, -0.1, 0.1, -0.12, 0.18), L(Rect(-0.2, 0.17, -0.04, 0.21)));
        return U(wheels, frame, fork, seat);
    }

    private static List<Polygon2> Pedestrian(double s)
    {
        List<Polygon2> P(params double[] xy) => Stroke(0.085, xy);
        var man = U(L(Circ(0.02, 0.40, 0.075)), P(0.0, 0.27, -0.04, 0.0), P(-0.04, 0.0, -0.16, -0.22, -0.2, -0.44), P(-0.04, 0.0, 0.08, -0.2, 0.18, -0.44),
            P(0.0, 0.25, -0.12, 0.1, -0.2, 0.0), P(0.0, 0.25, 0.1, 0.12, 0.2, 0.06));
        // Escala a partir dos pés (crianças menores com os pés na mesma linha).
        return Math.Abs(s - 1) < 1e-9 ? man : man.Select(p => p.Transform(v => new Vec2(v.X * s, -0.47 + (v.Y + 0.47) * s))).ToList();
    }

    private static List<Polygon2> Children()
    {
        var a = Pedestrian(1.0).Select(p => p.Transform(v => v * 0.9 + new Vec2(-0.18, 0.02))).ToList();
        var b = Pedestrian(0.72).Select(p => p.Transform(v => v + new Vec2(0.24, -0.04))).ToList();
        return U(a, b, Stroke(0.05, -0.02, 0.1, 0.1, 0.02));
    }

    private static List<Polygon2> Wheelchair()
    {
        var head = Circ(0.0, 0.40, 0.08);
        var body = U(Stroke(0.09, 0.0, 0.28, -0.04, 0.0, 0.2, 0.0, 0.26, -0.28), Stroke(0.07, 0.0, 0.2, 0.18, 0.2));
        var wheel = Ring(-0.06, -0.18, 0.2, 0.06);
        return U(L(head), body, wheel);
    }

    private static List<Polygon2> Tractor()
    {
        var body = L(Poly(-0.3, -0.05, 0.42, -0.05, 0.42, 0.1, 0.0, 0.12, -0.3, 0.12));
        var cab = U(Stroke(0.04, -0.28, 0.12, -0.28, 0.38, 0.02, 0.38, 0.02, 0.12));
        var big = Circ(-0.22, -0.16, 0.2);
        var small = Circ(0.34, -0.24, 0.12);
        return U(Cut(U(body, cab), Circ(-0.22, -0.16, 0.25), Circ(0.34, -0.24, 0.16)), L(big, small));
    }

    private static List<Polygon2> Cow()
    {
        var body = L(Poly(-0.38, -0.02, 0.2, -0.02, 0.26, 0.2, -0.38, 0.22, -0.44, 0.1));
        var head = L(Poly(0.2, 0.08, 0.3, 0.26, 0.46, 0.18, 0.44, 0.08));
        var legs = U(Stroke(0.07, -0.32, 0.0, -0.32, -0.3), Stroke(0.07, -0.2, 0.0, -0.2, -0.3), Stroke(0.07, 0.08, 0.0, 0.08, -0.3), Stroke(0.07, 0.18, 0.0, 0.18, -0.3));
        var horns = Stroke(0.035, 0.28, 0.24, 0.26, 0.34, 0.34, 0.26, 0.38, 0.34);
        var tail = Stroke(0.03, -0.42, 0.18, -0.48, 0.0);
        return U(body, head, legs, horns, tail);
    }

    private static List<Polygon2> Deer()
    {
        var body = L(Poly(-0.36, 0.0, 0.16, 0.0, 0.22, 0.14, -0.36, 0.16, -0.4, 0.08));
        var neck = L(Poly(0.12, 0.08, 0.24, 0.1, 0.34, 0.34, 0.26, 0.36));
        var head = L(Poly(0.26, 0.32, 0.36, 0.38, 0.48, 0.3, 0.34, 0.28));
        var legs = U(Stroke(0.05, -0.3, 0.02, -0.34, -0.36), Stroke(0.05, -0.2, 0.02, -0.14, -0.36), Stroke(0.05, 0.08, 0.02, 0.06, -0.36), Stroke(0.05, 0.14, 0.02, 0.22, -0.36));
        var antlers = U(Stroke(0.03, 0.3, 0.38, 0.24, 0.5, 0.16, 0.52), Stroke(0.03, 0.26, 0.46, 0.3, 0.52), Stroke(0.03, 0.34, 0.38, 0.4, 0.5, 0.46, 0.52));
        return U(body, neck, head, legs, antlers);
    }

    private static List<Polygon2> Cart()
    {
        var horse = Cow().Select(p => p.Transform(v => v * 0.62 + new Vec2(0.2, 0.06))).ToList();
        var cart = U(L(Rect(-0.5, -0.02, -0.08, 0.1)), Ring(-0.3, -0.12, 0.13, 0.04), Stroke(0.03, -0.08, 0.05, 0.1, 0.1));
        return U(horse, cart);
    }

    private static List<Polygon2> Train()
    {
        var body = L(Poly(-0.5, -0.12, 0.44, -0.12, 0.5, -0.02, 0.5, 0.1, 0.06, 0.1, 0.06, 0.3, -0.34, 0.3, -0.34, 0.1, -0.5, 0.1));
        body = U(body, L(Rect(0.24, 0.1, 0.34, 0.26)));
        body = Cut(body, Rect(-0.26, 0.16, -0.04, 0.26));
        return WithWheels(body, 0.07, -0.36, -0.14, 0.12, 0.34);
    }

    private static List<Polygon2> Tram()
    {
        var body = L(Poly(-0.46, -0.12, 0.46, -0.12, 0.46, 0.22, -0.46, 0.22));
        body = Cut(body, Rect(-0.38, 0.06, -0.14, 0.16), Rect(-0.08, 0.06, 0.14, 0.16), Rect(0.2, 0.06, 0.38, 0.16));
        var pole = Stroke(0.03, 0.0, 0.22, 0.12, 0.44, -0.3, 0.44);
        return U(WithWheels(body, 0.07, -0.3, 0.3), pole);
    }

    private static List<Polygon2> Plane() => U(L(Poly(-0.04, -0.46, 0.04, -0.46, 0.05, 0.38, 0.0, 0.48, -0.05, 0.38)),
        L(Poly(-0.46, -0.02, 0.46, -0.02, 0.46, 0.06, 0.05, 0.16, -0.05, 0.16, -0.46, 0.06)),
        L(Poly(-0.18, -0.44, 0.18, -0.44, 0.18, -0.38, 0.04, -0.3, -0.04, -0.3, -0.18, -0.38)));

    private static List<Polygon2> Worker()
    {
        var man = Pedestrian(0.9).Select(p => p.Transform(v => v + new Vec2(-0.12, 0.0))).ToList();
        var shovel = U(Stroke(0.04, 0.1, 0.12, 0.3, -0.26), L(Poly(0.26, -0.28, 0.36, -0.22, 0.42, -0.36, 0.3, -0.4)));
        var pile = L(Poly(0.18, -0.46, 0.5, -0.46, 0.44, -0.36, 0.3, -0.42));
        return U(man, shovel, pile);
    }

    private static List<Polygon2> HandCart()
    {
        var man = Pedestrian(0.9).Select(p => p.Transform(v => v + new Vec2(-0.24, 0.0))).ToList();
        var cart = U(L(Poly(0.0, -0.1, 0.44, -0.1, 0.48, 0.12, 0.02, 0.12)), Ring(0.24, -0.26, 0.12, 0.05), Stroke(0.035, 0.02, 0.06, -0.12, 0.1));
        return U(man, cart);
    }

    // ------------------------------------------------------------------ serviços e diversos

    private static List<Polygon2> FuelPump()
    {
        var body = Cut(L(Rect(-0.3, -0.42, 0.12, 0.34)), Rect(-0.2, 0.08, 0.02, 0.24));
        var hose = Stroke(0.05, 0.12, 0.2, 0.26, 0.12, 0.26, -0.24, 0.34, -0.3);
        return U(body, hose, L(Rect(-0.36, -0.46, 0.18, -0.4)));
    }

    private static List<Polygon2> Cutlery() => U(Stroke(0.07, -0.16, -0.44, -0.16, 0.1), Stroke(0.05, -0.26, 0.42, -0.26, 0.14, -0.06, 0.14, -0.06, 0.42),
        Stroke(0.05, -0.16, 0.14, -0.16, 0.42), L(Poly(0.12, -0.44, 0.2, -0.44, 0.2, 0.44, 0.1, 0.3, 0.1, 0.0)));

    private static List<Polygon2> Bed() => U(L(Rect(-0.44, -0.3, -0.36, 0.2)), L(Rect(-0.44, -0.12, 0.44, 0.0)), L(Rect(0.36, -0.3, 0.44, 0.0)),
        L(Circ(-0.24, 0.08, 0.08)), L(Poly(-0.12, 0.0, 0.4, 0.0, 0.4, 0.12, -0.12, 0.14)));

    private static List<Polygon2> Phone() => U(L(Poly(-0.4, 0.1, -0.26, 0.26, 0.26, 0.26, 0.4, 0.1, 0.3, 0.0, 0.16, 0.08, -0.16, 0.08, -0.3, 0.0)),
        L(Poly(-0.2, 0.04, 0.2, 0.04, 0.34, -0.36, -0.34, -0.36)));

    private static List<Polygon2> Wrench()
    {
        var shaft = Stroke(0.1, -0.3, -0.3, 0.16, 0.16);
        var head = Cut(L(Circ(0.24, 0.24, 0.17)), Rect(0.2, 0.28, 0.5, 0.5), Poly(0.26, 0.12, 0.44, 0.3, 0.3, 0.44, 0.12, 0.26));
        return U(shaft, head, L(Circ(-0.32, -0.32, 0.08)));
    }

    private static List<Polygon2> Tyre() => U(Ring(0, 0, 0.32, 0.14), L(Circ(0, 0, 0.1)));

    private static List<Polygon2> Info()
    {
        var t = PolygonOps.Union(new[] { Circ(0, 0.3, 0.08), Rect(-0.07, -0.36, 0.07, 0.16), Rect(-0.14, 0.08, 0.07, 0.16), Rect(-0.16, -0.42, 0.16, -0.34) });
        return t;
    }

    private static List<Polygon2> Horn() => U(L(Poly(-0.44, -0.08, -0.2, -0.08, 0.3, -0.34, 0.3, 0.34, -0.2, 0.08, -0.44, 0.08)), L(Rect(0.3, -0.36, 0.38, 0.36)));

    private static List<Polygon2> Chain() => U(Enumerable.Range(0, 4).SelectMany(i =>
        PolygonOps.Difference(new[] { Rect(-0.46 + i * 0.24, -0.1, -0.16 + i * 0.24, 0.1) }, new[] { Rect(-0.4 + i * 0.24, -0.04, -0.22 + i * 0.24, 0.04) })).ToList());

    private static List<Polygon2> Wind() => U(Stroke(0.06, -0.46, 0.2, 0.14, 0.2, 0.24, 0.28, 0.2, 0.36), Stroke(0.06, -0.46, 0.0, 0.3, 0.0, 0.4, -0.08, 0.34, -0.16),
        Stroke(0.06, -0.46, -0.2, 0.06, -0.2), L(Poly(-0.1, -0.46, 0.46, -0.46, 0.46, -0.4, -0.1, -0.4)));

    private static List<Polygon2> Rocks() => U(L(Poly(-0.46, -0.46, 0.46, -0.46, -0.46, 0.46)), L(Poly(0.1, 0.2, 0.22, 0.24, 0.26, 0.12, 0.14, 0.08)),
        L(Poly(0.2, -0.04, 0.34, 0.0, 0.36, -0.14, 0.22, -0.16)), L(Poly(0.3, 0.3, 0.38, 0.34, 0.42, 0.26, 0.34, 0.22)));

    private static List<Polygon2> Gravel()
    {
        var car = Car().Select(p => p.Transform(v => v * 0.8 + new Vec2(-0.1, -0.12))).ToList();
        var stones = new[] { (0.36, 0.2), (0.44, 0.02), (0.3, 0.34), (0.46, 0.3) }.Select(s => Circ(s.Item1, s.Item2, 0.04));
        return U(car, stones);
    }

    private static List<Polygon2> Tree() => U(L(Rect(-0.05, -0.46, 0.05, -0.1)), L(Circ(0, 0.12, 0.28)), L(Circ(-0.18, -0.02, 0.16)), L(Circ(0.18, -0.02, 0.16)));

    private static List<Polygon2> Boat() => U(L(Poly(-0.46, -0.1, 0.46, -0.1, 0.32, -0.3, -0.34, -0.3)), L(Poly(-0.02, -0.06, -0.02, 0.44, 0.32, -0.06)), Stroke(0.03, -0.04, -0.1, -0.04, 0.44));

    private static List<Polygon2> Shield() => Cut(L(Poly(-0.34, 0.4, 0.34, 0.4, 0.34, 0.0, 0.0, -0.44, -0.34, 0.0)), Poly(-0.06, 0.2, 0.06, 0.2, 0.06, -0.06, -0.06, -0.06));

    private static List<Polygon2> Toilet()
    {
        var man = Pedestrian(0.8).Select(p => p.Transform(v => v + new Vec2(-0.22, 0.0))).ToList();
        var woman = U(L(Circ(0.24, 0.32, 0.06)), L(Poly(0.24, 0.24, 0.38, -0.12, 0.1, -0.12)), Stroke(0.05, 0.18, -0.12, 0.18, -0.38), Stroke(0.05, 0.3, -0.12, 0.3, -0.38));
        return U(man, woman, L(Rect(-0.02, -0.4, 0.02, 0.4)));
    }
}
