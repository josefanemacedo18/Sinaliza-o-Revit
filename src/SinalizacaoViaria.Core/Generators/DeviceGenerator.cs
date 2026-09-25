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
/// Gera dispositivos físicos ao longo de um caminho: unidades espaçadas (segregadores, balizadores,
/// pilaretes, prismas, barreiras modulares) ou elementos contínuos (barreira New Jersey, separadores,
/// defensas). Os volumes são descritos por camadas empilhadas (peças com elevação e altura).
/// </summary>
public static class DeviceGenerator
{
    /// <summary>Camada de um perfil: largura relativa, base e topo relativos à altura total.</summary>
    private readonly record struct Layer(double WidthFactor, double Z0, double Z1);

    /// <summary>Perfil New Jersey aproximado por 4 camadas (base larga, faces inclinadas, topo estreito).</summary>
    private static readonly Layer[] NewJerseyLayers =
    {
        new(1.00, 0.00, 0.10),
        new(0.70, 0.10, 0.40),
        new(0.45, 0.40, 0.70),
        new(0.30, 0.70, 1.00),
    };

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

        // Elementos contínuos
        var guardRail = def.Forma is FormaDispositivo.Defensa or FormaDispositivo.DefensaDupla;
        if (spacing <= 0 || guardRail)
        {
            var layers = def.Forma switch
            {
                FormaDispositivo.NewJersey => NewJerseyLayers,
                FormaDispositivo.Defensa or FormaDispositivo.DefensaDupla => Array.Empty<Layer>(),
                _ => new[] { new Layer(1, 0, 1) },
            };
            foreach (var (c0, c1) in LinearPatternGenerator.Chunk(a, b, opt.MaxPieceLength))
            {
                var pts = off.SubPoints(p.ParamAt(c0), p.ParamAt(c1));
                foreach (var layer in layers)
                    AddStrip(geo, pts, W * layer.WidthFactor, color, H * (layer.Z1 - layer.Z0), H * layer.Z0);
                if (guardRail)
                {
                    // Lâmina (perfil W) no terço superior, afastada do eixo dos postes; na dupla, dos dois lados.
                    var sides = def.Forma == FormaDispositivo.DefensaDupla ? new[] { 1.0, -1.0 } : new[] { 1.0 };
                    foreach (var side in sides)
                    {
                        var rail = new Polyline2(pts).Offset(side * (W / 2 - 0.05)).Points;
                        AddStrip(geo, rail, 0.10, color, H * 0.40, H * 0.60);
                    }
                }
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
            foreach (var (shape, z0, h) in Unit(def.Forma, L, W, H))
                geo.Pieces.Add(new MarkingPiece(frame.ToWorld(shape), color) { Thickness = h, Elevation = z0, IsUnit = true });
        }
        if (!guardRail)
        {
            geo.UnitCount = n;
            geo.PaintedLength = len;
        }
        else
        {
            geo.UnitCount = n; // postes
        }
        return geo;
    }

    private static void AddStrip(MarkingGeometry geo, IReadOnlyList<Vec2> pts, double width, MarkingColor color, double height, double elevation)
    {
        foreach (var poly in PolygonOps.Strip(pts, width))
        {
            var s = poly.Simplified();
            if (s != null) geo.Pieces.Add(new MarkingPiece(s, color) { Thickness = height, Elevation = elevation });
        }
    }

    /// <summary>
    /// Peças de uma unidade em coordenadas locais (+Y ao longo do caminho, +X transversal), com base e altura.
    /// </summary>
    public static IEnumerable<(Polygon2 Shape, double Z0, double Height)> Unit(FormaDispositivo forma, double L, double W, double H)
    {
        switch (forma)
        {
            case FormaDispositivo.Cilindro:
                yield return (Circle(W / 2), 0, H);
                break;
            case FormaDispositivo.Balizador:
                var baseH = Math.Min(0.04, H * 0.1);
                yield return (Circle(Math.Max(W * 1.25, 0.10)), 0, baseH);
                yield return (Circle(W / 2), baseH, H - baseH);
                break;
            case FormaDispositivo.Tartaruga:
                yield return (Ellipse(W / 2, L / 2), 0, H * 0.5);
                yield return (Ellipse(W * 0.35, L * 0.35), H * 0.5, H * 0.5);
                break;
            case FormaDispositivo.Prisma:
                yield return (Rect(W, L), 0, H * 0.5);
                yield return (Rect(W * 0.7, L * 0.7), H * 0.5, H * 0.5);
                break;
            case FormaDispositivo.NewJersey:
                foreach (var layer in NewJerseyLayers)
                    yield return (Rect(W * layer.WidthFactor, L), H * layer.Z0, H * (layer.Z1 - layer.Z0));
                break;
            case FormaDispositivo.Defensa:
            case FormaDispositivo.DefensaDupla:
                // Poste
                yield return (Rect(Math.Min(0.15, W), Math.Min(0.15, L)), 0, H);
                break;
            default:
                yield return (Rect(W, L), 0, H);
                break;
        }
    }

    private static Polygon2 Rect(double w, double l) =>
        Polygon2.Rectangle(new Vec2(-w / 2, -l / 2), new Vec2(w / 2, l / 2));

    private static Polygon2 Circle(double r) => new(CurveTools.Circle(Vec2.Zero, r, 0.002));

    private static Polygon2 Ellipse(double rx, double ry)
    {
        var pts = new List<Vec2>();
        const int n = 32;
        for (int i = 0; i < n; i++)
        {
            var t = 2 * Math.PI * i / n;
            pts.Add(new Vec2(rx * Math.Cos(t), ry * Math.Sin(t)));
        }
        return new Polygon2(pts);
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
