using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada Y: cruzamentos criados a partir da ponta de um eixo ou ligados direto na ponta de uma via existente – vias de
/// larguras, ângulos e seções diferentes (local × arterial, com e sem canteiro, 60/90/120°, T e cruz). Critérios: pavimento
/// contínuo sem furos nem sobreposição, meio-fio e calçada fechando nas esquinas, nenhum piso &lt; 0,01 m² nem
/// autointerseção, pintura dentro da pista.
/// </summary>
public class Y34TipTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    // Modelos: 0 = local, 1 = coletora, 2 = avenida com canteiro central (arterial), 9 = mão única.
    public sealed class World
    {
        public List<IntersectionRoad> Roads { get; } = new();
        public List<List<MarkingDefinition>> Groups { get; } = new();
        public Dictionary<string, Polyline2> Paths { get; } = new();
        public List<MarkingDefinition> Defs { get; } = new();
        public List<IntersectionDemo.Scene> Nodes { get; } = new();
    }

    public static Vec2 Dir(double deg) => new(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180));

    /// <summary>Vias pelos pontos do eixo (como o "Sinalizar via" gera) e as interseções nos nós, como o plugin faz.</summary>
    public static World Make(IntersectionDefinition? template, params (int Tpl, Vec2[] Pts)[] roads) => Make(template, null, roads);

    /// <summary>Idem, com <paramref name="tune"/> ajustando cada interseção (ex.: ajustes por ramo) antes de gerar.</summary>
    public static World Make(IntersectionDefinition? template, Action<IntersectionDefinition, IReadOnlyList<IntersectionRoad>>? tune, params (int Tpl, Vec2[] Pts)[] roads)
    {
        var w = new World();
        foreach (var (tpl, pts) in roads)
        {
            var pr = PathReference.FromPoints(pts, 0);
            var g = RoadTemplates.All[tpl].Create().Build(pr, new OutputSettings(), Cat);
            var pav = g.OfType<RoadPavementDefinition>().First();
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(pav, axis));
        }
        template ??= new IntersectionDefinition { Control = ControleIntersecao.Pare };
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            if (!IntersectionGenerator.NeedsIntersection(rs, node)) continue;
            var d = (IntersectionDefinition)template.CloneWithNewId();
            d.Node = node;
            tune?.Invoke(d, rs);
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, Cat));
        }
        return w;
    }

    public sealed record Part(MarkingDefinition Def, MarkingPiece Piece);

    public static List<Part> Geometry(World w)
    {
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => w.Defs.FirstOrDefault(x => x.Id == id), PathOf = x => w.Paths.GetValueOrDefault(x.Id) };
        var res = new List<Part>();
        foreach (var def in w.Defs)
        {
            if (def is SignDefinition) continue;
            var path = w.Paths.GetValueOrDefault(def.Id);
            if (path == null && (def.Path ?? (def as HatchMarkingDefinition)?.Boundary) is { Points.Count: >= 2 } pr) path = new Polyline2(pr.Points, pr.Closed);
            var g = MarkingBuilder.Build(def, path, ctx);
            res.AddRange(g.Pieces.Select(p => new Part(def, p)));
        }
        return res;
    }

    private static bool IsPavement(MarkingPiece p) => p.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto;
    private static bool IsRaised(MarkingPiece p) => p.Color is MarkingColor.Concreto or MarkingColor.Grama;
    private static bool IsGutter(Part x) => x.Def is LinearMarkingDefinition l && l.Code.StartsWith("SARJETA") || x.Piece.Layer == "SARJETA"
                                            || x.Def is IntersectionDefinition && x.Piece.Color == MarkingColor.Concreto && x.Piece.Thickness <= 0.02 && x.Piece.Layer == null;
    private static bool IsCurb(Part x) => x.Def is LinearMarkingDefinition l && l.Code.StartsWith("MEIO-FIO") || x.Piece.Layer?.StartsWith("MEIO-FIO") == true
                                          || x.Def is RampDefinition;
    private static bool IsPaint(Part x) => x.Def is LinearMarkingDefinition or HatchMarkingDefinition or SymbolMarkingDefinition or TextMarkingDefinition
                                           && !IntersectionGenerator.IsPhysical(x.Def) && !IsPavement(x.Piece) && !IsRaised(x.Piece);

    /// <summary>Falhas de modelagem em volta de cada nó (lista vazia = sem falhas).</summary>
    public static List<string> Faults(World w)
    {
        var parts = Geometry(w);
        return w.Nodes.SelectMany(s => FaultsAt(parts, s.Layout.Node, Math.Max(25, s.Layout.Radius + 6),
            s.Layout.Roads.All(r => r.Def.LeftSidewalk > 0.5 && r.Def.RightSidewalk > 0.5))).ToList();
    }

    /// <summary>Falhas de modelagem num disco de raio <paramref name="R"/> em volta de <paramref name="node"/>.</summary>
    public static List<string> FaultsAt(List<Part> parts, Vec2 node, double R, bool walks)
    {
        var faults = new List<string>();
        {
            var disk = new[] { new Polygon2(CurveTools.Circle(node, R, 0.01)) };
            List<Part> Near(Func<Part, bool> f) => parts.Where(f).Where(x => PolygonOps.Intersect(new[] { x.Piece.Shape }, disk).Sum(p => p.Area) > 1e-6).ToList();
            var pav = Near(x => IsPavement(x.Piece));
            var raised = Near(x => IsRaised(x.Piece));
            var tag = $"nó ({node.X:0.0}; {node.Y:0.0})";

            // 1. Pavimento contínuo: sem furos perto do nó e sem pisos sobrepostos.
            var pavU = PolygonOps.Union(pav.Select(x => x.Piece.Shape));
            var inner = new[] { new Polygon2(CurveTools.Circle(node, R - 4, 0.01)) };
            foreach (var p in PolygonOps.Intersect(pavU, inner))
                foreach (var h in p.Holes)
                {
                    var hp = new Polygon2(h);
                    // Furo com canteiro/ilha/sarjeta por cima não é furo.
                    var covered = PolygonOps.TotalArea(PolygonOps.Intersect(new[] { hp }, raised.Select(x => x.Piece.Shape)));
                    if (hp.Area - covered > 0.01) faults.Add($"{tag}: furo no pavimento de {hp.Area - covered:0.000} m² em {hp.Centroid}");
                }
            for (int i = 0; i < pav.Count; i++)
                for (int j = i + 1; j < pav.Count; j++)
                {
                    if (pav[i].Def == pav[j].Def) continue;
                    var ov = PolygonOps.Intersect(new[] { pav[i].Piece.Shape }, new[] { pav[j].Piece.Shape });
                    var a = PolygonOps.TotalArea(ov);
                    if (a > 0.01 && Relevant(ov)) faults.Add($"{tag}: pavimentos sobrepostos ({pav[i].Def.DisplayCode} × {pav[j].Def.DisplayCode}) {a:0.000} m² em {ov.OrderByDescending(q => q.Area).First().Centroid}");
                }
            // Pisos elevados sobre a pista ou uns sobre os outros.
            foreach (var r in raised)
            {
                var ov = PolygonOps.Intersect(new[] { r.Piece.Shape }, pavU);
                var a = PolygonOps.TotalArea(ov);
                if (a > 0.02 && !IsGutter(r) && Relevant(ov)) faults.Add($"{tag}: {r.Def.DisplayCode}/{r.Piece.Layer} sobre a pista {a:0.000} m² em {ov.OrderByDescending(q => q.Area).First().Centroid}");
            }
            for (int i = 0; i < raised.Count; i++)
                for (int j = i + 1; j < raised.Count; j++)
                {
                    if (raised[i].Def == raised[j].Def && raised[i].Def is not IntersectionDefinition) continue;
                    var ov = PolygonOps.Intersect(new[] { raised[i].Piece.Shape }, new[] { raised[j].Piece.Shape });
                    var a = PolygonOps.TotalArea(ov);
                    if (a > 0.02 && Relevant(ov)) faults.Add($"{tag}: pisos sobrepostos ({raised[i].Def.DisplayCode}/{raised[i].Piece.Layer} × {raised[j].Def.DisplayCode}/{raised[j].Piece.Layer}) {a:0.000} m² em {ov.OrderByDescending(q => q.Area).First().Centroid}");
                }

            // 2. Meio-fio e calçada fechando nas esquinas: todo bordo da pista junto ao nó tem piso elevado encostado.
            if (walks)
            {
                var raisedU = PolygonOps.Union(raised.Select(x => x.Piece.Shape));
                var probe = new[] { new Polygon2(CurveTools.Circle(node, R - 5, 0.01)) };
                foreach (var p in PolygonOps.Intersect(pavU, probe))
                    foreach (var ring in new[] { p.Outer }.Concat(p.Holes))
                        for (int i = 0; i < ring.Count; i++)
                        {
                            var a = ring[i];
                            var b = ring[(i + 1) % ring.Count];
                            if (a.DistanceTo(node) > R - 5.5 || b.DistanceTo(node) > R - 5.5 || a.DistanceTo(b) < 0.05) continue;
                            var m = (a + b) / 2;
                            var n = (b - a).Normalized().PerpLeft;
                            var outP = pavU.Any(q => q.Contains(m + n * 0.03)) ? m - n * 0.06 : m + n * 0.06;
                            if (pavU.Any(q => q.Contains(outP))) continue;
                            if (!raisedU.Any(q => q.Contains(outP))) { faults.Add($"{tag}: bordo da pista sem meio-fio em {m}"); break; }
                        }
            }

            // 3. Nenhum piso < 0,01 m² nem com autointerseção.
            foreach (var x in pav.Concat(raised))
            {
                if (x.Piece.Solid != null || x.Piece.Profile != null) continue;
                if (x.Piece.Shape.Area < 0.01) faults.Add($"{tag}: piso de {x.Piece.Shape.Area:0.0000} m² ({x.Def.DisplayCode}/{x.Piece.Layer}/{x.Piece.Color}) em {x.Piece.Shape.Centroid}");
                if (SelfIntersects(x.Piece.Shape)) faults.Add($"{tag}: piso com autointerseção ({x.Def.DisplayCode}/{x.Piece.Layer}/{x.Piece.Color}, {x.Piece.Shape.Area:0.000} m²) em {x.Piece.Shape.Centroid}");
            }

            // Sarjeta só junto ao meio-fio (nunca atravessando a pista no limite da zona).
            var curbs = PolygonOps.Offset(PolygonOps.Union(raised.Where(IsCurb).Select(x => x.Piece.Shape)), 0.5);
            foreach (var g in raised.Where(IsGutter))
            {
                var loose = PolygonOps.TotalArea(PolygonOps.Difference(PolygonOps.Intersect(new[] { g.Piece.Shape }, disk), curbs));
                if (loose > 0.05) faults.Add($"{tag}: sarjeta {g.Def.DisplayCode}/{g.Piece.Layer} longe do meio-fio {loose:0.000} m² em {g.Piece.Shape.Centroid}");
            }

            // 4. Pintura dentro da pista (sarjeta conta como pista).
            var gutters = raised.Where(IsGutter).Select(x => x.Piece.Shape);
            var road = PolygonOps.Offset(PolygonOps.Union(pavU.Concat(gutters)), 0.02);
            foreach (var g in Near(IsPaint).GroupBy(x => x.Def))
            {
                var outA = PolygonOps.TotalArea(PolygonOps.Difference(PolygonOps.Intersect(g.Select(x => x.Piece.Shape), disk), road));
                if (outA > 0.02) faults.Add($"{tag}: pintura {g.Key.DisplayCode} fora da pista {outA:0.000} m² em {PolygonOps.Difference(PolygonOps.Intersect(g.Select(x => x.Piece.Shape), disk), road).OrderByDescending(q => q.Area).First().Centroid}");
            }
        }
        return faults;
    }

    /// <summary>Sobreposição relevante: mais de 0,02 m² e com largura média acima de 1 cm (não conta costura de 1–2 mm).</summary>
    private static bool Relevant(List<Polygon2> ov) =>
        ov.Any(p => p.Area > 0.02 && 2 * p.Area / Math.Max(1e-9, Perimeter(p.Outer)) > 0.01);

    private static double Perimeter(IReadOnlyList<Vec2> r) => Enumerable.Range(0, r.Count).Sum(i => r[i].DistanceTo(r[(i + 1) % r.Count]));

    private static bool SelfIntersects(Polygon2 p)
    {
        foreach (var ring in new[] { p.Outer }.Concat(p.Holes))
        {
            var n = ring.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    if (Cross(ring[i], ring[(i + 1) % n], ring[j], ring[(j + 1) % n])) return true;
                }
        }
        return false;
        static bool Cross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            double O(Vec2 p, Vec2 q, Vec2 r) => (q - p).Cross(r - p);
            var o1 = O(a, b, c); var o2 = O(a, b, d); var o3 = O(c, d, a); var o4 = O(c, d, b);
            return o1 * o2 < -1e-12 && o3 * o4 < -1e-12;
        }
    }

    // ------------------------------------------------------------------ cenários

    public static IEnumerable<object[]> Cases()
    {
        foreach (var (a, b) in new[] { (0, 0), (0, 2), (2, 0), (1, 2) })
            foreach (var ang in new[] { 60.0, 90.0, 120.0 })
            {
                yield return new object[] { "T_ponta_no_meio", a, b, ang };
                yield return new object[] { "ponta_com_ponta_T", a, b, ang };
                yield return new object[] { "cruz_de_pontas", a, b, ang };
                yield return new object[] { "ponta_na_ponta_e_via_passando", a, b, ang };
            }
    }

    /// <summary>Vias de cada cenário: <paramref name="a"/> = modelo da via principal, <paramref name="b"/> = da nova via.</summary>
    public static (int, Vec2[])[] Scenario(string kind, int a, int b, double ang)
    {
        var L = 90.0;
        var u = Dir(ang);
        return kind switch
        {
            // Via nova criada a partir de um ponto do eixo da existente (ponta da nova no meio da existente).
            "T_ponta_no_meio" => new[] { (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { Vec2.Zero, u * L }) },
            // Via existente termina no nó; a nova sai da ponta dela, seguindo; uma terceira sai da mesma ponta (T feito de pontas).
            "ponta_com_ponta_T" => new[] { (a, new[] { new Vec2(-L, 0), Vec2.Zero }), (a, new[] { Vec2.Zero, new Vec2(L, 0) }), (b, new[] { Vec2.Zero, u * L }) },
            // Cruz de quatro vias que terminam todas no mesmo ponto (larguras diferentes duas a duas).
            "cruz_de_pontas" => new[] { (a, new[] { new Vec2(-L, 0), Vec2.Zero }), (a, new[] { Vec2.Zero, new Vec2(L, 0) }), (b, new[] { Vec2.Zero, u * L }), (b, new[] { -u * L, Vec2.Zero }) },
            // Via ligada direto na ponta de outra (seção diferente), e uma transversal passando pelo nó.
            "ponta_na_ponta_e_via_passando" => new[] { (a, new[] { new Vec2(-L, 0), Vec2.Zero }), (b, new[] { Vec2.Zero, new Vec2(L, 0) }), (a, new[] { -u * L, u * L }) },
            _ => throw new ArgumentException(kind),
        };
    }

    public static IEnumerable<object[]> NearCases()
    {
        foreach (var (a, b) in new[] { (0, 1), (1, 2), (2, 0), (9, 0), (0, 9) })
            foreach (var ang in new[] { 60.0, 90.0, 120.0 })
                foreach (var kind in new[] { "ponta_antes_do_eixo", "ponta_passando_do_eixo", "emenda_ponta_ponta", "pontas_quase_juntas", "eixo_com_vertice_perto", "T_de_pontas_secoes_diferentes" })
                    yield return new object[] { kind, a, b, ang };
    }

    /// <summary>Casos de uso real: a ponta não cai exatamente no eixo/na ponta da outra via.</summary>
    public static (int, Vec2[])[] NearScenario(string kind, int a, int b, double ang)
    {
        var L = 90.0;
        var u = Dir(ang);
        return kind switch
        {
            // Ponta da nova via 1 m antes do eixo da existente (dentro da pista) e 0,7 m além dele.
            "ponta_antes_do_eixo" => new[] { (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { u * 1.0, u * L }) },
            "ponta_passando_do_eixo" => new[] { (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { -u * 0.7, u * L }) },
            // Duas vias pela ponta (emenda em ângulo com seções diferentes).
            "emenda_ponta_ponta" => new[] { (a, new[] { new Vec2(-L, 0), Vec2.Zero }), (b, new[] { Vec2.Zero, u * L }) },
            // Três pontas quase no mesmo ponto (0,4 m) – T feito de pontas.
            "pontas_quase_juntas" => new[] { (a, new[] { new Vec2(-L, 0), Vec2.Zero }), (a, new[] { new Vec2(0.3, 0.1), new Vec2(L, 0) }), (b, new[] { u * 0.4, u * L }) },
            // Via de seção diferente ligada na ponta de outra, em linha reta, e uma terceira chegando pela ponta (T de pontas).
            "T_de_pontas_secoes_diferentes" => new[] { (a, new[] { new Vec2(-L, 0), Vec2.Zero }), (b, new[] { Vec2.Zero, new Vec2(L, 0) }), (a, new[] { Vec2.Zero, u * L }) },
            // Eixo da nova via com um vértice (deflexão de 20°) a 8 m do nó.
            "eixo_com_vertice_perto" => new[] { (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { Vec2.Zero, u * 8, u * 8 + Dir(ang + 20) * (L - 8) }) },
            _ => throw new ArgumentException(kind),
        };
    }

    [Theory]
    [MemberData(nameof(NearCases))]
    public void Ponta_perto_do_eixo_ou_da_outra_ponta_sem_falhas(string kind, int a, int b, double ang)
    {
        var w = Make(null, NearScenario(kind, a, b, ang));
        Assert.NotEmpty(w.Nodes);
        var f = Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Cruzamento_pela_ponta_do_eixo_sem_falhas(string kind, int a, int b, double ang)
    {
        var w = Make(null, Scenario(kind, a, b, ang));
        Assert.NotEmpty(w.Nodes);
        var f = Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }
}
