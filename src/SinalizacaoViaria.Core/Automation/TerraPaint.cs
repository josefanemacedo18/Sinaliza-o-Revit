using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Pistas de terra (leito natural / revestimento primário): nada de pintura automática sobre elas e aviso quando o usuário pinta
/// por cima (a tinta não tem onde aderir – use a sinalização vertical).
/// </summary>
public static class TerraPaint
{
    public const string AvisoVia = "Pista de terra: a sinalização horizontal (pintura) não é gerada – a tinta não adere ao leito natural; " +
                                   "use a sinalização vertical.";

    /// <summary>Pintura (linhas, zebrados, símbolos, legendas, vagas) – não os elementos físicos (meio-fio, calçada, sarjeta...).</summary>
    public static bool IsPaint(MarkingDefinition d) => d switch
    {
        LinearMarkingDefinition l => !IntersectionGenerator.IsPhysical(l),
        HatchMarkingDefinition or RepeatedMarkingDefinition or ParkingMarkingDefinition or SymbolMarkingDefinition or TextMarkingDefinition => true,
        _ => false,
    };

    /// <summary>O ponto está sobre a pista de uma via de terra (e fora das pistas pavimentadas).</summary>
    public static bool OnTerra(Vec2 p, IReadOnlyList<IntersectionRoad> roads)
    {
        static bool On(IntersectionRoad r, Vec2 q)
        {
            var (st, sd) = r.Axis.Project(q);
            return st > -0.5 && st < r.Axis.Length + 0.5 && sd >= -r.Def.RightWidth - 0.05 && sd <= r.Def.LeftWidth + 0.05;
        }
        return roads.Any(r => r.Def.Material == TipoPavimento.Terra && On(r, p)) && !roads.Any(r => r.Def.Material != TipoPavimento.Terra && On(r, p));
    }

    private static List<Vec2> Samples(MarkingDefinition d, Polyline2? path)
    {
        switch (d)
        {
            case SymbolMarkingDefinition s: return new List<Vec2> { s.Position };
            case TextMarkingDefinition t: return new List<Vec2> { t.Position };
            case HatchMarkingDefinition h when h.Boundary.Points.Count >= 3:
                path ??= new Polyline2(h.Boundary.Points, true);
                break;
        }
        if (path == null || path.Length < 1e-6) return new List<Vec2>();
        var off = d switch { LinearMarkingDefinition l => l.Offset, RepeatedMarkingDefinition r => r.Offset, _ => 0 };
        var src = Math.Abs(off) > 1e-6 ? path.Offset(off) : path;
        var n = Math.Clamp((int)(src.Length / 2), 2, 200);
        return Enumerable.Range(0, n + 1).Select(i => src.PointAt(src.Length * i / n)).ToList();
    }

    /// <summary>Marcas de pintura que caem (na maior parte) sobre pista de terra.</summary>
    public static List<MarkingDefinition> OnTerra(IEnumerable<MarkingDefinition> defs, IReadOnlyList<IntersectionRoad> roads, Func<MarkingDefinition, Polyline2?> pathOf)
    {
        if (!roads.Any(r => r.Def.Material == TipoPavimento.Terra)) return new List<MarkingDefinition>();
        return defs.Where(IsPaint).Where(d =>
        {
            var pts = Samples(d, pathOf(d));
            return pts.Count > 0 && pts.Count(p => OnTerra(p, roads)) * 2 > pts.Count;
        }).ToList();
    }

    /// <summary>Aviso para pintura feita sobre pista de terra (lista vazia = nenhuma).</summary>
    public static List<string> Warnings(IEnumerable<MarkingDefinition> defs, IReadOnlyList<IntersectionRoad> roads, Func<MarkingDefinition, Polyline2?> pathOf)
    {
        var hits = OnTerra(defs, roads, pathOf);
        return hits.Count == 0 ? new List<string>()
            : new List<string> { $"Pintura sobre pista de terra ({string.Join(", ", hits.Select(h => h.DisplayCode).Distinct())}): a tinta não adere ao leito natural – " +
                                 "prefira a sinalização vertical ou pavimente o trecho." };
    }

    /// <summary>Caminho da marca a partir dos pontos gravados (marcas geradas, com o caminho já em pontos).</summary>
    public static Polyline2? StoredPath(MarkingDefinition d) =>
        (d.Path ?? (d as HatchMarkingDefinition)?.Boundary) is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points, pr.Closed) : null;
}
