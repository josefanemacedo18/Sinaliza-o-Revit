using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Biblioteca paramétrica de setas e símbolos. Todas as formas são construídas em coordenadas
/// locais (origem na base, +Y = sentido do tráfego) e dimensionadas pelo comprimento nominal L.
/// As proporções seguem o desenho típico do MBST e podem ser ajustadas pelo fator de largura.
/// </summary>
public static class SymbolBuilder
{
    /// <summary>Gera o símbolo posicionado no mundo.</summary>
    public static MarkingGeometry Build(SimboloDef def, double length, LocalFrame frame, MarkingColor? colorOverride = null, double widthFactor = 1.0)
    {
        var geo = new MarkingGeometry();
        var fg = colorOverride ?? def.Cor;
        var local = BuildLocal(def.Forma, length, widthFactor);

        if (def.CorFundo is { } bgColor && local.Background != null)
        {
            var bg = PolygonOps.Difference(new[] { local.Background }, local.Figure);
            geo.AddRange(bg.Select(frame.ToWorld), bgColor);
        }
        geo.AddRange(local.Figure.Select(frame.ToWorld), fg);
        geo.PathLength = length;
        geo.UnitCount = 1;
        return geo;
    }

    public sealed record LocalShape(List<Polygon2> Figure, Polygon2? Background);

    public static LocalShape BuildLocal(FormaSimbolo forma, double L, double widthFactor = 1.0)
    {
        widthFactor = widthFactor <= 0 ? 1 : widthFactor;
        List<Polygon2> fig = forma switch
        {
            FormaSimbolo.SetaFrente => StraightArrow(L, widthFactor),
            FormaSimbolo.SetaDireita => TurnArrow(L, +1, widthFactor),
            FormaSimbolo.SetaEsquerda => TurnArrow(L, -1, widthFactor),
            FormaSimbolo.SetaFrenteDireita => StraightAndTurn(L, +1, widthFactor),
            FormaSimbolo.SetaFrenteEsquerda => StraightAndTurn(L, -1, widthFactor),
            FormaSimbolo.SetaDireitaEsquerda => DoubleTurn(L, widthFactor),
            FormaSimbolo.SetaRetornoEsquerda => UTurn(L, -1, widthFactor),
            FormaSimbolo.SetaRetornoDireita => UTurn(L, +1, widthFactor),
            FormaSimbolo.SetaMudancaFaixaEsquerda => LaneChange(L, -1, widthFactor),
            FormaSimbolo.SetaMudancaFaixaDireita => LaneChange(L, +1, widthFactor),
            FormaSimbolo.AcessoDeficiente => Accessibility(L),
            FormaSimbolo.Bicicleta => Bicycle(L),
            FormaSimbolo.DePreferencia => YieldTriangle(L, widthFactor),
            FormaSimbolo.CruzSantoAndre => SaintAndrew(L, widthFactor),
            FormaSimbolo.ServicoSaude => HealthCross(L),
            _ => StraightArrow(L, widthFactor),
        };

        Polygon2? background = forma switch
        {
            FormaSimbolo.AcessoDeficiente or FormaSimbolo.ServicoSaude =>
                Polygon2.Rectangle(new Vec2(-L / 2, 0), new Vec2(L / 2, L)),
            _ => null,
        };
        return new LocalShape(fig, background);
    }

    // ------------------------------------------------------------------ setas

    private readonly record struct ArrowDims(double Shaft, double HeadW, double HeadL);

    private static ArrowDims Dims(double L, double wf) => new(0.036 * L * wf, 0.12 * L * wf, 0.32 * L);

    /// <summary>Haste ao longo de <paramref name="centerline"/> terminando em ponta triangular.</summary>
    public static List<Polygon2> StrokeArrow(IReadOnlyList<Vec2> centerline, double shaft, double headW, double headL)
    {
        var pts = centerline.ToList();
        var tip = pts[^1];
        var dir = (pts[^1] - pts[^2]).Normalized();
        var baseC = tip - dir * headL;
        // Encurta a haste até a base da ponta (com pequena sobreposição para a união).
        pts[^1] = baseC + dir * Math.Min(0.02, headL * 0.1);
        var parts = new List<Polygon2>();
        parts.AddRange(PolygonOps.Strip(pts, shaft));
        var n = dir.PerpLeft * (headW / 2);
        parts.Add(new Polygon2(new[] { baseC - n, tip, baseC + n }));
        return PolygonOps.Union(parts);
    }

    private static List<Polygon2> StraightArrow(double L, double wf)
    {
        var d = Dims(L, wf);
        return StrokeArrow(new[] { new Vec2(0, 0), new Vec2(0, L) }, d.Shaft, d.HeadW, d.HeadL);
    }

    /// <summary>Centro-linha de um ramo de conversão (sigma = +1 direita, -1 esquerda).</summary>
    private static List<Vec2> TurnBranch(double L, double y1, int sigma)
    {
        var r = 0.16 * L;
        var pts = new List<Vec2> { new(0, y1 - 0.01) };
        var center = new Vec2(sigma * r, y1);
        var start = sigma > 0 ? Math.PI : 0;
        var sweep = sigma > 0 ? -Math.PI / 2 : Math.PI / 2;
        pts.AddRange(CurveTools.Arc(center, r, start, sweep, 0.003));
        var end = pts[^1];
        pts.Add(end + new Vec2(sigma * (0.04 * L + 0.26 * L), 0));
        return pts;
    }

    private static List<Polygon2> TurnArrow(double L, int sigma, double wf)
    {
        var d = Dims(L, wf);
        var y1 = 0.72 * L;
        var line = new List<Vec2> { new(0, 0) };
        line.AddRange(TurnBranch(L, y1, sigma));
        return StrokeArrow(line, d.Shaft, d.HeadW, 0.26 * L);
    }

    private static List<Polygon2> StraightAndTurn(double L, int sigma, double wf)
    {
        var d = Dims(L, wf);
        var parts = StraightArrow(L, wf);
        var y1 = 0.30 * L;
        parts.AddRange(StrokeArrow(TurnBranch(L, y1, sigma), d.Shaft, d.HeadW, 0.26 * L));
        return PolygonOps.Union(parts);
    }

    private static List<Polygon2> DoubleTurn(double L, double wf)
    {
        var d = Dims(L, wf);
        var y1 = 0.72 * L;
        var parts = new List<Polygon2>();
        parts.AddRange(PolygonOps.Strip(new[] { new Vec2(0, 0), new Vec2(0, y1) }, d.Shaft));
        parts.AddRange(StrokeArrow(TurnBranch(L, y1, +1), d.Shaft, d.HeadW, 0.26 * L));
        parts.AddRange(StrokeArrow(TurnBranch(L, y1, -1), d.Shaft, d.HeadW, 0.26 * L));
        return PolygonOps.Union(parts);
    }

    private static List<Polygon2> UTurn(double L, int sigma, double wf)
    {
        var d = Dims(L, wf);
        var y1 = 0.62 * L;
        var R = 0.18 * L;
        // Constrói para a esquerda e espelha quando necessário.
        var pts = new List<Vec2> { new(0, 0) };
        pts.AddRange(CurveTools.Arc(new Vec2(-R, y1), R, 0, Math.PI, 0.003));
        var end = pts[^1];
        pts.Add(end + new Vec2(0, -(0.10 * L + 0.26 * L)));
        var res = StrokeArrow(pts, d.Shaft, d.HeadW, 0.26 * L);
        return sigma > 0 ? res.Select(p => p.Transform(v => new Vec2(-v.X, v.Y))).ToList() : res;
    }

    private static List<Polygon2> LaneChange(double L, int sigma, double wf)
    {
        var d = Dims(L, wf);
        var lat = 0.22 * L * sigma;
        var pts = new List<Vec2> { new(0, 0), new(0, 0.30 * L) };
        // Curva suave (quadrática) até a posição lateral.
        var p0 = new Vec2(0, 0.30 * L);
        var c = new Vec2(0, 0.55 * L);
        var p2 = new Vec2(lat, 0.80 * L);
        for (int i = 1; i <= 12; i++)
        {
            var t = i / 12.0;
            var q = (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * c + t * t * p2;
            pts.Add(q);
        }
        var dir = (p2 - c).Normalized();
        pts.Add(p2 + dir * (L - 0.80 * L));
        return StrokeArrow(pts, d.Shaft, d.HeadW * 1.1, d.HeadL);
    }

    // ------------------------------------------------------------------ símbolos

    private static List<Polygon2> Accessibility(double L)
    {
        Vec2 P(double u, double v) => new((u - 0.5) * L, v * L);
        var parts = new List<Polygon2>();
        parts.Add(new Polygon2(CurveTools.Circle(P(0.53, 0.82), 0.075 * L)));
        parts.AddRange(PolygonOps.Strip(new[] { P(0.50, 0.72), P(0.50, 0.46), P(0.70, 0.46), P(0.78, 0.22), P(0.88, 0.22) }, 0.075 * L));
        parts.AddRange(PolygonOps.Strip(new[] { P(0.50, 0.62), P(0.67, 0.62) }, 0.06 * L));
        var arc = CurveTools.Arc(P(0.45, 0.36), 0.21 * L, Angles.ToRad(-35), Angles.ToRad(265), 0.002 * L)
            ;
        parts.AddRange(PolygonOps.Strip(arc, 0.065 * L, roundJoins: true));
        return PolygonOps.Union(parts);
    }

    private static List<Polygon2> Bicycle(double L)
    {
        Vec2 P(double u, double v) => new(u * L, v * L);
        var w = 0.035 * L;
        var parts = new List<Polygon2>();
        foreach (var cx in new[] { -0.30, 0.30 })
        {
            var outer = CurveTools.Circle(P(cx, 0.19), 0.18 * L + w / 2);
            var inner = CurveTools.Circle(P(cx, 0.19), 0.18 * L - w / 2);
            parts.Add(new Polygon2(outer, new[] { inner }));
        }
        void S(params Vec2[] pts) => parts.AddRange(PolygonOps.Strip(pts, w, roundJoins: true));
        S(P(-0.30, 0.19), P(-0.03, 0.19), P(0.22, 0.47), P(-0.12, 0.47), P(-0.30, 0.19));
        S(P(-0.03, 0.19), P(-0.12, 0.47));
        S(P(0.30, 0.19), P(0.22, 0.47), P(0.19, 0.55), P(0.27, 0.55));
        S(P(-0.19, 0.51), P(-0.05, 0.51));
        return PolygonOps.Union(parts);
    }

    private static List<Polygon2> YieldTriangle(double L, double wf)
    {
        var half = L / 6 * wf;
        var outer = new Polygon2(new[] { new Vec2(0, 0), new Vec2(half, L), new Vec2(-half, L) });
        var stroke = 0.04 * L * wf;
        var inner = PolygonOps.Offset(new[] { outer }, -stroke);
        return PolygonOps.Difference(new[] { outer }, inner);
    }

    private static List<Polygon2> SaintAndrew(double L, double wf)
    {
        var hx = 0.15 * L * wf;
        var w = 0.06 * L * wf;
        var parts = new List<Polygon2>();
        parts.AddRange(PolygonOps.Strip(new[] { new Vec2(-hx, 0), new Vec2(hx, L) }, w));
        parts.AddRange(PolygonOps.Strip(new[] { new Vec2(hx, 0), new Vec2(-hx, L) }, w));
        return PolygonOps.Union(parts);
    }

    private static List<Polygon2> HealthCross(double L)
    {
        var v = Polygon2.Rectangle(new Vec2(-0.15 * L, 0.10 * L), new Vec2(0.15 * L, 0.90 * L));
        var h = Polygon2.Rectangle(new Vec2(-0.40 * L, 0.35 * L), new Vec2(0.40 * L, 0.65 * L));
        return PolygonOps.Union(new[] { v, h });
    }
}
