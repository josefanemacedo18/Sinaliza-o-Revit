using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

// Cenas de interseção desenhadas em planta a partir do núcleo: as vias pelo RoadSetup (como "Via" no plugin), os nós pelo
// mesmo caminho do plugin (IntersectionGenerator.Layout, recortes nas marcas das vias e elementos filhos) e as rotatórias pelo
// RoundaboutGenerator. Cada cena vira um SVG em escala com as peças na ordem de altura (pista, peças elevadas, pintura).
var outDir = args.Length > 0 ? args[0] : "cenas";
var only = args.Skip(1).ToHashSet();
Directory.CreateDirectory(outDir);
var cat = CatalogService.LoadDefault();
// Casos da matriz dos testes (AB2Matrix): "m-<tipo>-<a>-<b>-<ângulo>", ex.: m-T-canteiroLargo-coletora-60.
foreach (var sc in Cenas.All().Concat(only.Where(n => n.StartsWith("m-")).Select(Cenas.Matriz)))
{
    if (only.Count > 0 && !only.Contains(sc.Name)) continue;
    Svg.S = sc.Name.EndsWith("-zoom") || sc.Window.X1 - sc.Window.X0 < 30 ? 50 : 20;
    var w = sc.Build(cat);
    var parts = Cenas.Geometry(w, cat);
    File.WriteAllText(Path.Combine(outDir, sc.Name + ".svg"), Svg.Render(parts, sc.Window, sc.Name));
    var rel = new StringBuilder();
    rel.AppendLine($"# {sc.Name}");
    foreach (var n in w.Nodes) foreach (var a in n.Layout.Warnings) rel.AppendLine("aviso do nó: " + a);
    foreach (var rb in w.Defs.OfType<RoundaboutDefinition>())
    {
        var rl = RoundaboutGenerator.Layout(rb);
        foreach (var a in rl.Warnings) rel.AppendLine("aviso da rotatória: " + a);
        foreach (var g in rl.Legs)
            rel.AppendLine($"ramo {g.Leg.AngleDeg:0}°: anel {rl.ToOuter(rb.Center, g.Dir):0.00}, travessia {g.CrosswalkT:0.00}, rampas {g.RampType}, " +
                           $"calçada esq. {RoundaboutGenerator.WalkTotal(rb, g.Leg, 1):0.00} dir. {RoundaboutGenerator.WalkTotal(rb, g.Leg, -1):0.00}");
        foreach (var ch in rl.WalkChains)
            rel.AppendLine($"trecho {ch.From?.Leg.AngleDeg:0}°/{ch.FromSide} → {ch.To?.Leg.AngleDeg:0}°/{ch.ToSide}: {ch.WidthFrom:0.00} → {ch.WidthTo:0.00} m ({ch.Points.Count} pontos)");
    }
    foreach (var n in w.Nodes)
    {
        foreach (var (k, t) in n.Layout.MedianTips) rel.AppendLine($"ponta do canteiro: via {k.Road} sentido {k.Sign}: {t:0.00} m");
        string Bx(Polygon2 p) { var (a0, a1) = p.Bounds; return $"[{a0.X:0.0};{a0.Y:0.0} → {a1.X:0.0};{a1.Y:0.0}] {p.Area:0.0} m²"; }
        foreach (var p in n.Layout.Obstacles) rel.AppendLine("obstáculo " + Bx(p));
        foreach (var p in n.Layout.MedianCore) rel.AppendLine("miolo do canteiro " + Bx(p));
        foreach (var leg in n.Layout.Legs)
            rel.AppendLine($"ramo via {leg.Road} sentido {leg.Sign}: livre {leg.Clear:0.00}, travessia {IntersectionGenerator.CrosswalkT(n.Layout.Definition!, n.Layout, leg):0.00}");
    }
    foreach (var g in parts.GroupBy(p => (p.Def.GetType().Name, p.Def.DisplayCode)).OrderBy(g => g.Key.Name))
        rel.AppendLine($"{g.Key.Name} {g.Key.DisplayCode}: {g.Count()} peça(s)");
    // Peças das conexões (rotatória, interseção) por camada e cor: área total e menor peça.
    foreach (var g in parts.Where(p => p.Def is RoundaboutDefinition or IntersectionDefinition && p.Piece.Solid == null && p.Piece.Profile == null)
                 .GroupBy(p => (p.Def.DisplayCode, Layer: p.Piece.Layer ?? p.Piece.Component ?? "", p.Piece.Color)).OrderBy(g => g.Key.Layer))
        rel.AppendLine($"  {g.Key.DisplayCode} {g.Key.Layer} {g.Key.Color}: {g.Count()} peça(s), {g.Sum(p => p.Piece.Shape.Area):0.00} m², menor {g.Min(p => p.Piece.Shape.Area):0.000} m²");
    // Pontas < 15° nos pisos das conexões, como o teste da matriz mede (pisos da mesma chave unidos e preparados).
    foreach (var g in parts.Where(x => x.Def is IntersectionDefinition or RoundaboutDefinition && x.Piece.Solid == null && x.Piece.Profile == null && x.Piece.Layer != "SARJETA")
                 .GroupBy(x => (x.Def, ElementPlan.FloorKey(x.Piece))))
        foreach (var sh in PolygonOps.Union(g.Select(x => x.Piece.Shape)).Select(ElementPlan.Prepare))
        {
            var ring = sh.Outer;
            for (int k = 0; k < ring.Count; k++)
            {
                var a0 = ring[(k - 1 + ring.Count) % ring.Count];
                var b0 = ring[k];
                var c1 = ring[(k + 1) % ring.Count];
                var u0 = a0 - b0;
                var w0 = c1 - b0;
                if (u0.Length < 0.02 || w0.Length < 0.02) continue;
                var ang = Math.Acos(Math.Clamp(u0.Dot(w0) / (u0.Length * w0.Length), -1, 1)) * 180 / Math.PI;
                if (ang < 15) rel.AppendLine($"ponta de {ang:0}° em {b0} ({g.Key.Def.DisplayCode}/{g.First().Piece.Layer}, {sh.Area:0.00} m²; lados {u0.Length:0.000} e {w0.Length:0.000} m)");
            }
        }
    // Peças perto de um ponto (CENAS_PONTO="x;y;r"): para investigar uma falha apontada pelos testes.
    if (Environment.GetEnvironmentVariable("CENAS_PONTO") is { Length: > 0 } pt)
    {
        var v = pt.Split(';').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var c0 = new Vec2(v[0], v[1]);
        var disk = new[] { new Polygon2(CurveTools.Circle(c0, v[2], 0.005)) };
        foreach (var part in parts.Where(x => x.Piece.Solid == null && x.Piece.Profile == null))
        {
            var ov = PolygonOps.TotalArea(PolygonOps.Intersect(new[] { part.Piece.Shape }, disk));
            if (ov < 1e-7) continue;
            rel.AppendLine($"perto: {part.Def.DisplayCode}/{part.Piece.Layer}/{part.Piece.Component} {part.Piece.Color} elev {part.Piece.Elevation:0.000} esp {part.Piece.Thickness:0.000} " +
                           $"área no disco {ov:0.0000} m² (peça {part.Piece.Shape.Area:0.00} m²)");
            var all = Environment.GetEnvironmentVariable("CENAS_TODOS") == "1" && part.Piece.Shape.Area < 5;
            foreach (var ring in new[] { part.Piece.Shape.Outer }.Concat(part.Piece.Shape.Holes))
                for (int k = 0; k < ring.Count; k++)
                    if (all || ring[k].DistanceTo(c0) < v[2] * 4)
                        rel.AppendLine($"    v{k}: {ring[k].X:0.0000}; {ring[k].Y:0.0000}");
        }
        for (var ang = 0; ang < 360; ang += 30)
        {
            var q = c0 + new Vec2(Math.Cos(ang * Math.PI / 180), Math.Sin(ang * Math.PI / 180)) * (v[2] * 0.5);
            var hits = parts.Where(x => x.Piece.Solid == null && x.Piece.Profile == null && x.Piece.Shape.Contains(q)).Select(x => $"{x.Def.DisplayCode}/{x.Piece.Layer}").ToList();
            rel.AppendLine($"  ponto {q}: {(hits.Count == 0 ? "VAZIO" : string.Join(", ", hits))}");
        }
    }
    File.WriteAllText(Path.Combine(outDir, sc.Name + ".txt"), rel.ToString());
    Console.WriteLine($"{sc.Name}: {parts.Count} peças");
}

public sealed record Part(MarkingDefinition Def, MarkingPiece Piece);

public sealed class World
{
    public List<IntersectionRoad> Roads { get; } = new();
    public List<List<MarkingDefinition>> Groups { get; } = new();
    public Dictionary<string, Polyline2> Paths { get; } = new();
    public List<MarkingDefinition> Defs { get; } = new();
    public List<IntersectionDemo.Scene> Nodes { get; } = new();
}

public sealed record Cena(string Name, Func<Catalogo, World> Build, (double X0, double Y0, double X1, double Y1) Window);

public static class Cenas
{
    private static ElementoSecao E(TipoElementoSecao t, double w, Action<ElementoSecao>? cfg = null)
    {
        var e = new ElementoSecao { Tipo = t, Largura = w };
        cfg?.Invoke(e);
        return e;
    }

    private const TipoElementoSecao R = TipoElementoSecao.FaixaRolamento;

    /// <summary>Via principal simples: uma faixa por sentido e calçadas com faixa de serviço gramada.</summary>
    public static RoadSetup Principal(double calcada = 3.0) => new()
    {
        Center = CenterTreatment.LFO2, SarjetaSomada = true,
        Right = { E(R, 3.50), E(TipoElementoSecao.Calcada, calcada) },
        Left = { E(R, 3.50), E(TipoElementoSecao.Calcada, calcada) },
    };

    /// <summary>Via da imagem 1: canteiro central físico, faixa, estacionamento, ciclofaixa junto à calçada e calçada.</summary>
    public static RoadSetup ComCanteiroECiclofaixa(double canteiro = 3.0) => new()
    {
        Center = CenterTreatment.Canteiro, MedianWidth = canteiro, MedianType = TipoCanteiro.Fisico, SarjetaSomada = true,
        Right = { E(R, 3.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Ciclofaixa, 1.50), E(TipoElementoSecao.Calcada, 3.00) },
        Left = { E(R, 3.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Ciclofaixa, 1.50), E(TipoElementoSecao.Calcada, 3.00) },
    };

    /// <summary>Via com calçadas de largura dada em cada lado (ramos da rotatória).</summary>
    public static RoadSetup Local(double calcadaDir, double calcadaEsq, double faixa = 3.5) => new()
    {
        Center = CenterTreatment.LFO2, SarjetaSomada = true,
        Right = { E(R, faixa), E(TipoElementoSecao.Calcada, calcadaDir) },
        Left = { E(R, faixa), E(TipoElementoSecao.Calcada, calcadaEsq) },
    };

    /// <summary>Seções da matriz dos testes de interseção (AB2Matrix.Sections).</summary>
    private static RoadSetup Secao(string nome) => nome switch
    {
        "mao1" => new RoadSetup
        {
            TwoWay = false, Hierarchy = HierarquiaViaria.Local, Speed = 40, Center = CenterTreatment.Nenhum, SarjetaSomada = true,
            Right = { E(R, 3.50), E(TipoElementoSecao.Calcada, 2.50) },
            Left = { E(TipoElementoSecao.Calcada, 2.50) },
        },
        "local" => RoadTemplates.All[0].Create(),
        "coletora" => RoadTemplates.All[1].Create(),
        "canteiro" => RoadTemplates.All[2].Create(),
        "canteiroLargo" => RoadTemplates.All[2].Create() is var s ? Largo(s) : null!,
        _ => throw new ArgumentException(nome),
    };

    private static RoadSetup Largo(RoadSetup s) { s.MedianWidth = 8.0; return s; }

    private static Vec2 D(double deg) => new(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180));

    /// <summary>Caso da matriz (AB2Matrix.Axes): cruz, T, Y ou 5ramos.</summary>
    public static Cena Matriz(string name)
    {
        // Janela opcional: "~x0~y0~x1~y1" no fim do nome (planta ampliada).
        var win = (-40.0, -40.0, 40.0, 40.0);
        var parts = name.Split('~');
        if (parts.Length == 5)
            win = (double.Parse(parts[1], CultureInfo.InvariantCulture), double.Parse(parts[2], CultureInfo.InvariantCulture),
                   double.Parse(parts[3], CultureInfo.InvariantCulture), double.Parse(parts[4], CultureInfo.InvariantCulture));
        var p = parts[0].Split('-');
        var (kind, a, b, ang) = (p[1], p[2], p[3], double.Parse(p[4], CultureInfo.InvariantCulture));
        const double L = 90;
        List<(string, Vec2[])> axes = kind switch
        {
            "cruz" => new() { (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { -D(ang) * L, D(ang) * L }) },
            "T" => new() { (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { Vec2.Zero, D(ang) * L }) },
            "Y" => new() { (a, new[] { new Vec2(0, -L), Vec2.Zero }), (a, new[] { Vec2.Zero, D(90 - ang / 2) * L }), (b, new[] { Vec2.Zero, D(90 + ang / 2) * L }) },
            _ => Cinco(a, b, ang),
        };
        return new Cena(name, c => Nos(c, new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true },
            axes.Select(x => (Secao(x.Item1), x.Item2)).ToArray()), win);
    }

    private static List<(string, Vec2[])> Cinco(string a, string b, double ang)
    {
        const double L = 90;
        var dirs = new[] { 0.0, 180.0, ang, ang + 180 }.Select(d => (d % 360 + 360) % 360).OrderBy(d => d).ToList();
        double best = 0, mid = 0;
        for (int i = 0; i < dirs.Count; i++)
        {
            var gap = ((dirs[(i + 1) % dirs.Count] - dirs[i]) % 360 + 360) % 360;
            if (gap > best) { best = gap; mid = dirs[i] + gap / 2; }
        }
        return new() { (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { -D(ang) * L, D(ang) * L }), ("local", new[] { Vec2.Zero, D(mid) * L }) };
    }

    public static IEnumerable<Cena> All()
    {
        yield return new("t-canteiro-ciclofaixa", c => Nos(c, null, (Principal(), new[] { new Vec2(0, -70), new Vec2(0, 70) }),
            (ComCanteiroECiclofaixa(), new[] { new Vec2(0, 0), new Vec2(110, 0) })), (-22, -26, 45, 26));
        yield return new("t-canteiro-ciclofaixa-zoom", c => Nos(c, null, (Principal(), new[] { new Vec2(0, -70), new Vec2(0, 70) }),
            (ComCanteiroECiclofaixa(), new[] { new Vec2(0, 0), new Vec2(110, 0) })), (2, -12, 24, 12));
        yield return new("t-canteiro-largo", c => Nos(c, null, (Principal(), new[] { new Vec2(0, -70), new Vec2(0, 70) }),
            (ComCanteiroECiclofaixa(6.0), new[] { new Vec2(0, 0), new Vec2(110, 0) })), (-22, -28, 45, 28));
        yield return new("cruz-canteiro-ciclofaixa", c => Nos(c, null, (Principal(), new[] { new Vec2(0, -70), new Vec2(0, 70) }),
            (ComCanteiroECiclofaixa(), new[] { new Vec2(-110, 0), new Vec2(110, 0) })), (-45, -28, 45, 28));
        yield return new("cruz-duas-avenidas", c => Nos(c, null, (ComCanteiroECiclofaixa(), new[] { new Vec2(0, -110), new Vec2(0, 110) }),
            (ComCanteiroECiclofaixa(), new[] { new Vec2(-110, 0), new Vec2(110, 0) })), (-45, -45, 45, 45));
        yield return new("rotatoria-calcadas", c => Rotatoria(c, new[]
        {
            (Local(2.0, 2.0), new[] { new Vec2(0, 0), new Vec2(120, 0) }),
            (Local(5.0, 5.0), new[] { new Vec2(0, 0), new Vec2(0, 120) }),
            (Local(3.0, 3.0), new[] { new Vec2(0, 0), new Vec2(-120, 0) }),
            (Local(4.0, 1.5), new[] { new Vec2(0, 0), new Vec2(0, -120) }),
        }), (-50, -50, 50, 50));
        // Mesma rotatória sem a composição das vias (só a largura de cada lado) e com a opção desligada (largura única).
        yield return new("rotatoria-calcadas-sem-secao", c => Rotatoria(c, d => d.MatchRoadSection = false, new[]
        {
            (Local(2.0, 2.0), new[] { new Vec2(0, 0), new Vec2(120, 0) }),
            (Local(5.0, 5.0), new[] { new Vec2(0, 0), new Vec2(0, 120) }),
            (Local(3.0, 3.0), new[] { new Vec2(0, 0), new Vec2(-120, 0) }),
            (Local(4.0, 1.5), new[] { new Vec2(0, 0), new Vec2(0, -120) }),
        }), (-50, -50, 50, 50));
        yield return new("rotatoria-largura-unica", c => Rotatoria(c, d => d.SidewalkPerLeg = false, new[]
        {
            (Local(2.0, 2.0), new[] { new Vec2(0, 0), new Vec2(120, 0) }),
            (Local(5.0, 5.0), new[] { new Vec2(0, 0), new Vec2(0, 120) }),
            (Local(3.0, 3.0), new[] { new Vec2(0, 0), new Vec2(-120, 0) }),
            (Local(4.0, 1.5), new[] { new Vec2(0, 0), new Vec2(0, -120) }),
        }), (-50, -50, 50, 50));
        // Uma via que atravessa a rotatória (dois ramos da mesma via, um a favor e outro contra o eixo) e uma terceira oblíqua.
        yield return new("rotatoria-via-passante", c => Rotatoria(c, new[]
        {
            (Local(4.5, 1.5), new[] { new Vec2(-120, 0), new Vec2(120, 0) }),
            (Local(3.0, 2.0), new[] { new Vec2(0, 0), new Vec2(85, 85) }),
        }), (-50, -50, 50, 50));
    }

    /// <summary>Vias e os nós onde se encontram, como o plugin faz (Y34TipTests.World, com RoadSetup).</summary>
    public static World Nos(Catalogo cat, IntersectionDefinition? template, params (RoadSetup S, Vec2[] Pts)[] roads)
    {
        var w = Vias(cat, roads);
        template ??= new IntersectionDefinition { Control = ControleIntersecao.Pare };
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            if (!IntersectionGenerator.NeedsIntersection(rs, node)) continue;
            var d = (IntersectionDefinition)template.CloneWithNewId();
            d.Node = node;
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, cat));
        }
        return w;
    }

    private static World Vias(Catalogo cat, (RoadSetup S, Vec2[] Pts)[] roads)
    {
        var w = new World();
        foreach (var (s, pts) in roads)
        {
            var pr = PathReference.FromPoints(pts, 0);
            var axis = new Polyline2(pr.Points);
            var g = s.Build(pr, new OutputSettings(), cat, axis: axis);
            var pav = g.OfType<RoadPavementDefinition>().First();
            foreach (var m in g) if (m.Path is { } p && p.Points.Count == pr.Points.Count && p.Points[0].DistanceTo(pr.Points[0]) < 1e-9) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(pav, axis));
        }
        return w;
    }

    /// <summary>Rotatória no centro das vias (ramos pelas vias) e os recortes nas marcas delas.</summary>
    public static World Rotatoria(Catalogo cat, params (RoadSetup S, Vec2[] Pts)[] roads) => Rotatoria(cat, null, roads);

    public static World Rotatoria(Catalogo cat, Action<RoundaboutDefinition>? setup, params (RoadSetup S, Vec2[] Pts)[] roads)
    {
        var w = Vias(cat, roads);
        var d = new RoundaboutDefinition { Center = Vec2.Zero };
        d.ApplyPreset(TipoRotatoria.UmaFaixa);
        setup?.Invoke(d);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, w.Roads, d.OuterRadius + 25);
        // Como o plugin (IntersectionService.Refresh): perfil geral e o de cada lado de cada ramo, lidos das vias.
        if (d.MatchRoadSection)
        {
            var pctx = new BuildContext { Catalog = cat };
            var sides = w.Roads.Select((r, k) => EdgeProfile.FromRoadSides(r.Def, w.Groups[k].Where(m => m.Id != r.Def.Id), pctx)).ToList();
            d.EdgeProfile = EdgeProfile.Best(w.Roads.Select((r, k) => EdgeProfile.FromRoad(r.Def, w.Groups[k].Where(m => m.Id != r.Def.Id), pctx)));
            if (d.EdgeProfile.Count > 0) d.SidewalkWidth = Math.Max(0, EdgeProfile.OuterWidth(d.EdgeProfile) - d.CurbWidth);
            foreach (var l in d.Legs)
                if (w.Roads.FindIndex(r => r.Def.Id == l.RoadId) is var k and >= 0) l.SetEdgeSides(sides[k]);
        }
        var L = RoundaboutGenerator.Layout(d);
        foreach (var m in w.Defs.ToList())
            foreach (var cut in RoundaboutGenerator.RoadCuts(d, L, m))
                m.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = cut.Outer.ToList() });
        w.Defs.Add(d);
        w.Defs.AddRange(RoundaboutGenerator.Children(d, L, new OutputSettings(), 0));
        return w;
    }

    public static List<Part> Geometry(World w, Catalogo cat)
    {
        var ctx = new BuildContext { Catalog = cat, Lookup = id => w.Defs.FirstOrDefault(x => x.Id == id), PathOf = x => w.Paths.GetValueOrDefault(x.Id) };
        var res = new List<Part>();
        foreach (var def in w.Defs)
        {
            if (def is SignDefinition) continue;
            var path = w.Paths.GetValueOrDefault(def.Id);
            if (path == null && (def.Path ?? (def as HatchMarkingDefinition)?.Boundary) is { Points.Count: >= 2 } pr) path = new Polyline2(pr.Points, pr.Closed);
            try
            {
                var g = MarkingBuilder.Build(def, path, ctx);
                res.AddRange(g.Pieces.Select(p => new Part(def, p)));
            }
            catch (Exception ex) { Console.Error.WriteLine($"{def.DisplayCode}: {ex.Message}"); }
        }
        return res;
    }
}

public static class Svg
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static double S = 20;   // px por metro

    private static int Ordem(Part p)
    {
        if (MarkingColors.IsPavement(p.Piece.Color)) return 0;
        if (MarkingColors.IsPaint(p.Piece.Color) && p.Piece.Elevation < 0.01) return 2;
        return 1;
    }

    public static string Render(List<Part> parts, (double X0, double Y0, double X1, double Y1) win, string title)
    {
        var (x0, y0, x1, y1) = win;
        var W = (x1 - x0) * S;
        var H = (y1 - y0) * S;
        string P(Vec2 v) => $"{((v.X - x0) * S).ToString("0.##", Inv)},{((y1 - v.Y) * S).ToString("0.##", Inv)}";
        string Ring(IReadOnlyList<Vec2> r) => "M" + string.Join("L", r.Select(P)) + "Z";
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns='http://www.w3.org/2000/svg' width='{W.ToString("0", Inv)}' height='{(H + 24).ToString("0", Inv)}' viewBox='0 0 {W.ToString("0", Inv)} {(H + 24).ToString("0", Inv)}'>");
        sb.AppendLine($"<rect width='100%' height='100%' fill='#ffffff'/>");
        var box = new Polygon2(new[] { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) });
        foreach (var p in parts.OrderBy(Ordem).ThenBy(p => p.Piece.Elevation + Math.Max(0, p.Piece.Thickness)))
        {
            var (b0, b1) = p.Piece.Shape.Bounds;
            if (b1.X < x0 || b0.X > x1 || b1.Y < y0 || b0.Y > y1) continue;
            var rgb = p.Piece.Rgb ?? MarkingColors.Display(p.Piece.Color).Hex;
            var d = Ring(p.Piece.Shape.Outer) + string.Concat(p.Piece.Shape.Holes.Select(Ring));
            var stroke = Ordem(p) == 1 ? " stroke='#555' stroke-width='0.6'" : Ordem(p) == 0 ? " stroke='#222' stroke-width='0.4'" : "";
            sb.AppendLine($"<path d='{d}' fill='{rgb}' fill-rule='evenodd'{stroke}><title>{System.Security.SecurityElement.Escape(p.Def.DisplayCode + " " + p.Def.KindName + " " + p.Piece.Color + " " + p.Piece.Layer)}</title></path>");
        }
        // Grade de 5 m.
        for (var gx = Math.Ceiling(x0 / 5) * 5; gx <= x1; gx += 5)
            sb.AppendLine($"<line x1='{((gx - x0) * S).ToString("0.#", Inv)}' y1='0' x2='{((gx - x0) * S).ToString("0.#", Inv)}' y2='{H.ToString("0", Inv)}' stroke='#00000018'/>");
        for (var gy = Math.Ceiling(y0 / 5) * 5; gy <= y1; gy += 5)
            sb.AppendLine($"<line x1='0' y1='{((y1 - gy) * S).ToString("0.#", Inv)}' x2='{W.ToString("0", Inv)}' y2='{((y1 - gy) * S).ToString("0.#", Inv)}' stroke='#00000018'/>");
        sb.AppendLine($"<text x='6' y='{(H + 17).ToString("0", Inv)}' font-family='sans-serif' font-size='13'>{title} – grade de 5 m</text>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
