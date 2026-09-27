using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Faixa do perfil de borda de uma via, medida a partir da face do meio-fio (bordo da pista): positiva para fora
/// (meio-fio, faixa de serviço, passeio), negativa para dentro da pista (sarjeta).
/// </summary>
public sealed class EdgeBand
{
    public double D0 { get; set; }
    public double D1 { get; set; }
    public MarkingColor Color { get; set; }
    public double Thickness { get; set; }
    public double Elevation { get; set; }
    /// <summary>Código do elemento da via (MEIO-FIO, GRAMADO, CALCADA, SARJETA...).</summary>
    public string Code { get; set; } = "";
    public double Width => D1 - D0;
    public bool Inside => D1 <= 1e-6;
}

/// <summary>
/// Perfil de borda de uma via lido dos elementos reais do grupo dela (meio-fio, sarjeta, grama, calçada) – vale
/// para vias criadas com Via, com Pista + elementos junto ao bordo ou editadas depois. As conexões (interseção,
/// rotatória, cul-de-sac) aplicam o mesmo perfil em volta da própria pista, continuando a via sem emendas.
/// </summary>
public static class EdgeProfile
{
    private static readonly Polyline2 TestAxis = new(new[] { new Vec2(0, 0), new Vec2(40, 0) });

    /// <summary>Perfil do lado mais completo da via (lista vazia se a via não tiver elementos de borda).</summary>
    public static List<EdgeBand> FromRoad(RoadPavementDefinition pav, IEnumerable<MarkingDefinition> members, BuildContext ctx)
    {
        var right = new List<EdgeBand>();
        var left = new List<EdgeBand>();
        var key = RoadSectionInference.PathKey(pav.Path);
        foreach (var m in members)
        {
            if (m is not LinearMarkingDefinition l || !RoadSectionInference.IsPhysical(l.Code) || l.Id == pav.Id) continue;
            if (key.Length > 0 && RoadSectionInference.PathKey(l.Path) != key) continue;
            var copy = (LinearMarkingDefinition)MarkingDefinition.FromJson(l.ToJson())!;
            copy.Exclusions.Clear();
            copy.StartSetback = 0;
            copy.EndSetback = 0;
            MarkingGeometry geo;
            try { geo = MarkingBuilder.Build(copy, TestAxis, ctx); }
            catch { continue; }
            foreach (var p in geo.Pieces.Where(p => p.Solid == null && p.Profile == null))
            {
                var (mn, mx) = p.Shape.Bounds;
                if (mx.X - mn.X < 1) continue;   // pontas de peças recortadas
                var mid = (mn.Y + mx.Y) / 2;
                var band = mid < 0
                    ? new EdgeBand { D0 = -mx.Y - pav.RightWidth, D1 = -mn.Y - pav.RightWidth }
                    : new EdgeBand { D0 = mn.Y - pav.LeftWidth, D1 = mx.Y - pav.LeftWidth };
                band.Color = p.Color;
                band.Thickness = p.Thickness;
                band.Elevation = p.Elevation;
                band.Code = l.Code;
                (mid < 0 ? right : left).Add(band);
            }
        }
        List<EdgeBand> Clean(List<EdgeBand> side) => side
            .GroupBy(b => (Math.Round(b.D0, 3), Math.Round(b.D1, 3), b.Color, Math.Round(b.Thickness, 3), Math.Round(b.Elevation, 3)))
            .Select(g => g.First()).Where(b => b.Width > 0.02 && b.D1 > -3 && b.D0 < 30).OrderBy(b => b.D0).ToList();
        right = Clean(right);
        left = Clean(left);
        double Cover(List<EdgeBand> s) => s.Sum(b => b.Width);
        return Cover(left) > Cover(right) + 1e-6 ? left : right;
    }

    /// <summary>Perfil mais completo entre várias vias.</summary>
    public static List<EdgeBand> Best(IEnumerable<List<EdgeBand>> profiles) =>
        profiles.OrderByDescending(p => p.Where(b => !b.Inside).Sum(b => b.Width)).ThenByDescending(p => p.Count).FirstOrDefault() ?? new();

    /// <summary>Largura externa do perfil (do meio-fio até o fim da calçada).</summary>
    public static double OuterWidth(IReadOnlyList<EdgeBand> bands) => bands.Where(b => !b.Inside).Select(b => b.D1).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Aplica o perfil em volta da pista <paramref name="pavement"/>, limitado à <paramref name="region"/> (a faixa
    /// externa onde a conexão tem meio-fio e calçada). Faixas internas (sarjeta) ficam dentro da pista e só junto ao
    /// meio-fio. Devolve as peças e as áreas internas (a subtrair do pavimento).
    /// </summary>
    /// <param name="within">Área da conexão (zona): a sarjeta acompanha todo bordo da pista dentro dela – inclusive onde
    /// o meio-fio é o da própria via – mas não as bocas dos ramos (bordos sobre o limite da zona).</param>
    public static (List<MarkingPiece> Pieces, List<Polygon2> Inside) Apply(IReadOnlyList<Polygon2> pavement, IReadOnlyList<Polygon2> region, IReadOnlyList<EdgeBand> bands,
        IReadOnlyList<Polygon2>? within = null)
    {
        var pieces = new List<MarkingPiece>();
        var inside = new List<Polygon2>();
        if (pavement.Count == 0 || region.Count == 0 || bands.Count == 0) return (pieces, inside);
        var cache = new Dictionary<int, List<Polygon2>>();
        List<Polygon2> Off(double d)
        {
            var k = (int)Math.Round(d * 1000);
            if (!cache.TryGetValue(k, out var v)) cache[k] = v = Math.Abs(d) < 1e-6 ? pavement.ToList() : PolygonOps.Offset(pavement, d, true);
            return v;
        }
        var covered = new List<Polygon2>();
        foreach (var b in bands.Where(b => !b.Inside).OrderBy(b => b.D0))
        {
            var ring = PolygonOps.Difference(Off(Math.Max(0, b.D1)), Off(Math.Max(0, b.D0)));
            var area = PolygonOps.Intersect(ring, region).Where(p => p.Area > 0.01).ToList();
            foreach (var a in area) pieces.Add(new MarkingPiece(a, b.Color) { Thickness = b.Thickness, Elevation = b.Elevation, Layer = b.Code });
            covered.AddRange(area);
        }
        // Sobra da região além do perfil (ex.: calçada mais larga): continua como a última faixa externa.
        var outer = bands.Where(b => !b.Inside).OrderBy(b => b.D1).LastOrDefault();
        if (outer != null)
        {
            var rest = PolygonOps.Difference(region, PolygonOps.Union(covered)).Where(p => p.Area > 0.05).ToList();
            foreach (var r in rest) pieces.Add(new MarkingPiece(r, outer.Color) { Thickness = outer.Thickness, Elevation = outer.Elevation, Layer = outer.Code });
        }
        // Sarjeta (e outras faixas internas): dentro da pista, só onde há meio-fio/calçada ao lado.
        List<Polygon2>? border = null;
        if (within is { Count: > 0 })
        {
            // Faixa logo fora do bordo da pista, dentro da zona (afastada 0,15 m do limite): bordos com meio-fio.
            var inner = PolygonOps.Offset(within, -0.15, true);
            border = PolygonOps.Intersect(PolygonOps.Difference(Off(0.3), pavement), inner);
        }
        foreach (var b in bands.Where(b => b.Inside))
        {
            var ring = PolygonOps.Difference(Off(b.D1), Off(b.D0));
            var near = PolygonOps.Offset(border != null ? PolygonOps.Union(border.Concat(region)) : region.ToList(), -b.D0 + 0.05, true);
            var area = PolygonOps.Intersect(ring, near).Where(p => p.Area > 0.01).ToList();
            foreach (var a in area) pieces.Add(new MarkingPiece(a, b.Color) { Thickness = b.Thickness, Elevation = b.Elevation, Layer = b.Code });
            inside.AddRange(area);
        }
        return (pieces, inside);
    }
}
