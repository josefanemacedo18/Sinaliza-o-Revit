using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>Regras do Apagar Trecho: peça sob o clique e área a apagar (contorno da peça com folga de 3 cm).</summary>
public static class TrimTools
{
    public const double Gap = 0.03;

    /// <summary>Peça sob o ponto (ou a até 30 cm dele); havendo várias, a menor (um traço antes da faixa inteira).</summary>
    public static MarkingPiece? PieceAt(MarkingGeometry geo, Vec2 p, double tolerance = 0.3)
    {
        MarkingPiece? best = null;
        double bestKey = double.MaxValue;
        foreach (var pc in geo.Pieces)
        {
            var d = pc.Shape.Contains(p) ? 0 : pc.Shape.DistanceTo(p);
            if (d > tolerance) continue;
            var key = d * 1000 + pc.Shape.Area;
            if (key < bestKey) { bestKey = key; best = pc; }
        }
        return best;
    }

    /// <summary>
    /// Área a apagar para a peça clicada: a peça inteira; se for longa (> 12 m, linha contínua), só o trecho dentro de um
    /// círculo de diâmetro <paramref name="around"/> em volta do clique (corta a linha ao longo dela, em qualquer direção).
    /// </summary>
    public static Polygon2? ZoneFor(MarkingPiece piece, Vec2 p, double around = 3)
    {
        var shape = piece.Shape;
        var (mn, mx) = shape.Bounds;
        if (Math.Max(mx.X - mn.X, mx.Y - mn.Y) > 12)
        {
            var circle = new Polygon2(Enumerable.Range(0, 32).Select(i => p + new Vec2(Math.Cos(i * Math.PI / 16), Math.Sin(i * Math.PI / 16)) * (around / 2)));
            var part = PolygonOps.Intersect(new[] { shape }, new[] { circle }).OrderByDescending(x => x.Area).FirstOrDefault();
            if (part == null) return null;
            shape = part;
        }
        return Grow(shape);
    }

    /// <summary>Partes de todas as peças dentro da janela (retângulo entre dois cantos).</summary>
    public static List<Polygon2> ZonesInWindow(MarkingGeometry geo, Vec2 a, Vec2 b)
    {
        var rect = Polygon2.Rectangle(new Vec2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Vec2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
        var res = new List<Polygon2>();
        if (rect.Area < 1e-4) return res;
        foreach (var pc in geo.Pieces)
            foreach (var part in PolygonOps.Intersect(new[] { pc.Shape }, new[] { rect }).Where(x => x.Area > 1e-4))
                if (Grow(part) is { } g) res.Add(g);
        return res;
    }

    private static Polygon2? Grow(Polygon2 shape) =>
        PolygonOps.Offset(new[] { shape }, Gap).OrderByDescending(x => x.Area).FirstOrDefault() ?? shape;
}
