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

/// <summary>Perfis de borda dos dois lados de uma via (usados pelas conexões esquina a esquina).</summary>
public sealed class RoadEdgeProfile
{
    public string RoadId { get; set; } = "";
    public List<EdgeBand> Left { get; set; } = new();
    public List<EdgeBand> Right { get; set; } = new();
    public List<EdgeBand> Side(bool left) => left ? Left : Right;
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
        var (left, right) = Sides(pav, members, ctx);
        double Cover(List<EdgeBand> s) => s.Sum(b => b.Width);
        return Cover(left) > Cover(right) + 1e-6 ? left : right;
    }

    /// <summary>Perfis de borda de cada lado da via.</summary>
    public static RoadEdgeProfile FromRoadSides(RoadPavementDefinition pav, IEnumerable<MarkingDefinition> members, BuildContext ctx)
    {
        var (left, right) = Sides(pav, members, ctx);
        return new RoadEdgeProfile { RoadId = pav.Id, Left = left, Right = right };
    }

    private static (List<EdgeBand> Left, List<EdgeBand> Right) Sides(RoadPavementDefinition pav, IEnumerable<MarkingDefinition> members, BuildContext ctx)
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
        return (Contiguous(Clean(left)), Contiguous(Clean(right)));
    }

    /// <summary>
    /// Só o que encosta no meio-fio: faixas externas encadeadas a partir da face (meio-fio, serviço, passeio) e, dentro da
    /// pista, só a sarjeta junto à guia. Canteiros laterais, plataformas e faixas elevadas longe da borda ficam de fora
    /// (antes viravam um "anel" de grama em volta das esquinas).
    /// </summary>
    private static List<EdgeBand> Contiguous(List<EdgeBand> bands)
    {
        var res = new List<EdgeBand>();
        foreach (var b in bands.Where(b => b.Inside && b.D1 > -0.06 && b.Width <= 1.5 && b.Code is "SARJETA" or "SARJETAO").OrderByDescending(b => b.D1))
            if (res.Count == 0 || Math.Abs(res[^1].D0 - b.D1) < 0.06) res.Add(b);
        var reach = 0.0;
        foreach (var b in bands.Where(b => !b.Inside).OrderBy(b => b.D0))
        {
            if (b.D0 > reach + 0.06) break;           // lacuna: o resto não é da calçada junto à guia
            res.Add(b);
            reach = Math.Max(reach, b.D1);
        }
        return res.OrderBy(b => b.D0).ToList();
    }

    /// <summary>
    /// Perfil intermediário entre <paramref name="a"/> (peso 0) e <paramref name="b"/> (peso 1): as faixas são alinhadas
    /// pelo código (meio-fio com meio-fio, grama com grama...) e as larguras interpoladas; faixa que só existe de um lado
    /// afina até zero – menos o meio-fio, que mantém a largura (não "some" na curva).
    /// </summary>
    public static List<EdgeBand> Blend(IReadOnlyList<EdgeBand> a, IReadOnlyList<EdgeBand> b, double w)
    {
        w = Math.Clamp(w, 0, 1);
        if (a.Count == 0 && b.Count == 0) return new();
        var res = new List<EdgeBand>();
        foreach (var inside in new[] { false, true })
        {
            var sa = a.Where(x => x.Inside == inside).OrderBy(x => inside ? -x.D1 : x.D0).ToList();
            var sb = b.Where(x => x.Inside == inside).OrderBy(x => inside ? -x.D1 : x.D0).ToList();
            var pos = 0.0;
            foreach (var (x, y) in Align(sa, sb))
            {
                double Wd(EdgeBand? e, EdgeBand? other) => e?.Width ?? (other != null && other.Code.StartsWith("MEIO-FIO") ? other.Width : 0);
                var width = Math.Round(Wd(x, y) * (1 - w) + Wd(y, x) * w, 3);
                if (width < 0.01) continue;
                var src = (w < 0.5 ? x ?? y : y ?? x)!;
                var band = new EdgeBand { Color = src.Color, Thickness = src.Thickness, Elevation = src.Elevation, Code = src.Code };
                if (inside) { band.D1 = -pos; band.D0 = -pos - width; }
                else { band.D0 = pos; band.D1 = pos + width; }
                pos += width;
                res.Add(band);
            }
        }
        return res.OrderBy(x => x.D0).ToList();
    }

    /// <summary>Faixa alinhada entre dois perfis: atributos e larguras em cada ponta.</summary>
    public sealed record AlignedBand(EdgeBand Attr, double WidthA, double WidthB, bool Inside);

    /// <summary>
    /// Faixas de <paramref name="a"/> e <paramref name="b"/> alinhadas pelo código, com a largura de cada lado (faixa que
    /// só existe de um lado tem largura 0 do outro – o meio-fio mantém a largura).
    /// </summary>
    public static List<AlignedBand> Aligned(IReadOnlyList<EdgeBand> a, IReadOnlyList<EdgeBand> b)
    {
        var res = new List<AlignedBand>();
        foreach (var inside in new[] { false, true })
        {
            var sa = a.Where(x => x.Inside == inside).OrderBy(x => inside ? -x.D1 : x.D0).ToList();
            var sb = b.Where(x => x.Inside == inside).OrderBy(x => inside ? -x.D1 : x.D0).ToList();
            foreach (var (x, y) in Align(sa, sb))
            {
                double Wd(EdgeBand? e, EdgeBand? other) => e?.Width ?? (other != null && other.Code.StartsWith("MEIO-FIO") ? other.Width : 0);
                res.Add(new AlignedBand((x ?? y)!, Wd(x, y), Wd(y, x), inside));
            }
        }
        return res;
    }

    /// <summary>
    /// Faixas ao longo de uma linha de meio-fio (<paramref name="chain"/>, da ponta A à ponta B, com as normais para fora da
    /// pista): as larguras variam continuamente de A para B. Faixas externas saem para fora; as internas (sarjeta) entram
    /// na pista.
    /// </summary>
    public static List<(Polygon2 Shape, EdgeBand Attr, bool Inside)> AlongChain(IReadOnlyList<Vec2> chain, IReadOnlyList<Vec2> normals, IReadOnlyList<double> weights,
        IReadOnlyList<AlignedBand> bands)
    {
        var res = new List<(Polygon2, EdgeBand, bool)>();
        if (chain.Count < 2) return res;
        foreach (var inside in new[] { false, true })
        {
            var list = bands.Where(x => x.Inside == inside).ToList();
            var n = chain.Count;
            var acc = new double[n];
            foreach (var bd in list)
            {
                var d0 = acc.ToArray();
                for (int i = 0; i < n; i++) acc[i] += bd.WidthA * (1 - weights[i]) + bd.WidthB * weights[i];
                if (acc.Zip(d0, (x, y) => x - y).Max() < 0.01) continue;
                var sg = inside ? -1 : 1;
                var ring = new List<Vec2>();
                for (int i = 0; i < n; i++) ring.Add(chain[i] + normals[i] * (sg * d0[i]));
                for (int i = n - 1; i >= 0; i--) ring.Add(chain[i] + normals[i] * (sg * acc[i]));
                foreach (var pg in PolygonOps.Union(new[] { new Polygon2(ring) }).Where(pg => pg.Area > 1e-3))
                    res.Add((pg, bd.Attr, inside));
            }
        }
        return res;
    }

    /// <summary>Alinhamento (maior subsequência comum pelo código) de duas listas de faixas.</summary>
    private static List<(EdgeBand? A, EdgeBand? B)> Align(List<EdgeBand> a, List<EdgeBand> b)
    {
        static string K(EdgeBand e) => e.Code.StartsWith("MEIO-FIO") ? "MEIO-FIO" : e.Code;
        var n = a.Count;
        var m = b.Count;
        var t = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                t[i, j] = K(a[i]) == K(b[j]) ? t[i + 1, j + 1] + 1 : Math.Max(t[i + 1, j], t[i, j + 1]);
        var res = new List<(EdgeBand?, EdgeBand?)>();
        int p = 0, q = 0;
        while (p < n || q < m)
        {
            if (p < n && q < m && K(a[p]) == K(b[q])) { res.Add((a[p], b[q])); p++; q++; }
            else if (q >= m || (p < n && t[p + 1, q] >= t[p, q + 1])) { res.Add((a[p], null)); p++; }
            else { res.Add((null, b[q])); q++; }
        }
        return res;
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
        IReadOnlyList<Polygon2>? within = null, bool fillRest = true, Dictionary<int, List<Polygon2>>? offsets = null)
    {
        var pieces = new List<MarkingPiece>();
        var inside = new List<Polygon2>();
        if (pavement.Count == 0 || region.Count == 0 || bands.Count == 0) return (pieces, inside);
        var cache = offsets ?? new Dictionary<int, List<Polygon2>>();
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
        if (outer != null && fillRest)
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
