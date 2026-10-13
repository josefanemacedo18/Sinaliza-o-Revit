using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AL, item 3: cena de ponta a ponta de vias sobre topografia difícil, montada como o plugin faz no Revit – greide de
/// cada via pelo terreno (cotas obrigadas nos cruzamentos), superfície da via e do nó (concordância das secundárias), pisos
/// divididos em partes planas, faixas e plataformas da terraplenagem e o Toposolid refeito (simulado pela mesma triangulação).
/// </summary>
public static class TopoScene
{
    /// <summary>Terrenos difíceis (cota absoluta em m).</summary>
    public static readonly Dictionary<string, Func<Vec2, double>> Terrains = new()
    {
        // Ondulação forte nas duas direções: amplitude 8 m, comprimentos de onda 40 e 55 m.
        ["ondulado"] = p => 8 * Math.Sin(2 * Math.PI * p.X / 40) * Math.Cos(2 * Math.PI * p.Y / 55) + 4 * Math.Sin(2 * Math.PI * (p.X + p.Y) / 31),
        // Degrau de 16 m em ~6 m, atravessado pelas vias (e outro, de 15 m, na outra direção).
        ["degrau"] = p => 16 / (1 + Math.Exp(-(p.X - 6) / 1.2)) + 15 / (1 + Math.Exp(-(p.Y + 10) / 1.0)),
        // Vale em V (40 %) ao longo de y, com o fundo descendo 5 %.
        ["vale"] = p => 0.40 * Math.Abs(p.X - 3) + 0.05 * p.Y,
        // Crista (35 %) ao longo de x, subindo 4 %.
        ["crista"] = p => 25 - 0.35 * Math.Abs(p.Y + 2) + 0.04 * p.X,
        // Terreno ruidoso (semente fixa): soma de ondas aleatórias, ±10 m.
        ["ruido"] = Noise(20260412),
        // Referência: terreno plano (a planta das peças tem de ser a mesma em qualquer terreno).
        ["plano"] = _ => 0,
    };

    private static Func<Vec2, double> Noise(int seed)
    {
        var rnd = new Random(seed);
        var waves = Enumerable.Range(0, 14).Select(_ =>
        {
            var ang = rnd.NextDouble() * Math.PI * 2;
            var len = 12 + rnd.NextDouble() * 60;
            return (Dir: new Vec2(Math.Cos(ang), Math.Sin(ang)), K: 2 * Math.PI / len, Amp: 0.6 + rnd.NextDouble() * 2.2, Ph: rnd.NextDouble() * Math.PI * 2);
        }).ToList();
        return p => waves.Sum(w => w.Amp * Math.Sin(w.K * p.Dot(w.Dir) + w.Ph));
    }

    public sealed class Scene
    {
        public required Y34TipTests.World World { get; init; }
        public required Func<Vec2, double> Ground { get; init; }
        public List<RoadGrade> Grades { get; } = new();
        public List<GradeSurface> Surfaces { get; } = new();
        public Dictionary<string, NodeSurface> Nodes { get; } = new();
        public GradingResult Design { get; set; } = null!;
        public TerrainMesh Original { get; set; } = null!;
        public TerrainMesh Terrain { get; set; } = null!;
        public List<(MarkingDefinition Def, MarkingPiece Piece)> Parts { get; } = new();
        public List<Vec2> Fixed { get; } = new();
        /// <summary>Cotas obrigadas de cada via (estaca, cota) e a rampa máxima pedida.</summary>
        public List<List<Vec2>> Pins { get; } = new();
        /// <summary>Meia extensão do patamar de cada cota obrigada (o alcance do nó na via; mínimo 6 m).</summary>
        public List<List<double>> Halves { get; } = new();
        public List<double> MaxGrades { get; } = new();
        public List<List<string>> GradeWarnings { get; } = new();
        public const double Subgrade = 0.40;
        public const double Cut = 1.0;
        public const double Fill = 1.5;
    }

    /// <summary>Ramo de uma via no nó: índice da via no mundo.</summary>
    private static int RoadIndex(Y34TipTests.World w, string pavId) => w.Roads.FindIndex(r => r.Def.Id == pavId);

    /// <summary>
    /// Cena: vias pelos eixos (seções da matriz AK), greide de cada uma pelo terreno – na ordem de criação, passando na cota das
    /// vias já criadas nos cruzamentos –, interseções, superfícies e terraplenagem.
    /// </summary>
    public static Scene Make(string terrain, string kind, string a, string b, double ang, RelevoVia relief = RelevoVia.AcompanharTerreno,
        IntersectionDefinition? template = null)
    {
        var ground = Terrains[terrain];
        var roads = kind == "isolada" ? new List<(string Sec, Vec2[] Pts)> { (a, new[] { new Vec2(-90, 0), new Vec2(90, 0) }) } : AKShape.Axes(kind, a, b, ang);
        template ??= new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true, CurbExtensions = true };
        // 0) Planta (não depende do greide): vias e interseções – o alcance de cada nó ao longo das vias dá o patamar delas.
        var w = AKShape.Make(template, roads, null);
        var nodeIds = w.Nodes.Select(n => n.Layout.Definition!.Id).ToHashSet();
        double Reach(int i, Vec2 node) => RoadProfileDesigner.NodeReach(w.Roads[i].Axis, node,
            w.Roads[i].Def.Exclusions.Where(e => e.SourceId != null && nodeIds.Contains(e.SourceId)).Select(e => (IReadOnlyList<Vec2>)e.Points));
        // 1) Greides: a via i passa na cota das vias j < i onde os eixos se encontram.
        var axes = roads.Select(r => new Polyline2(r.Pts)).ToList();
        var grades = new List<RoadGrade>();
        var fixedPts = new List<Vec2>();
        var pins = new List<List<Vec2>>();
        var halves = new List<List<double>>();
        var maxG = new List<double>();
        var warns = new List<List<string>>();
        for (int i = 0; i < axes.Count; i++)
        {
            var axis = axes[i];
            var opt = RoadProfileDesigner.ForRelief(relief, 0.08, Scene.Cut, Scene.Fill);
            for (int j = 0; j < i; j++)
                foreach (var node in Crossings(axes[i], axes[j]))
                {
                    var sj = Math.Clamp(axes[j].Project(node).Station, 0, axes[j].Length);
                    var zj = grades[j].Z(sj);
                    opt.Fixed.Add(new Vec2(Math.Clamp(axis.Project(node).Station, 0, axis.Length), zj));
                    opt.FixedHalf.Add(RoadProfileDesigner.PlatformFor(Reach(i, node), opt.VerticalCurve));
                    fixedPts.Add(node);
                    // A via existente também ganha o patamar no nó (como o plugin faz ao ligar a via nova a ela).
                    pins[j].Add(new Vec2(sj, zj));
                    halves[j].Add(RoadProfileDesigner.PlatformFor(Reach(j, node), grades[j].DefaultCurve));
                    grades[j] = RoadProfileDesigner.WithPlatforms(grades[j], axes[j].Length, pins[j], maxG[j], halves[j]);
                }
            if (opt.Fixed.FirstOrDefault(f => f.X < 1) is { } f0 && f0 != default) opt.StartZ = f0.Y;
            if (opt.Fixed.FirstOrDefault(f => f.X > axis.Length - 1) is { } f1 && f1 != default) opt.EndZ = f1.Y;
            var res = RoadProfileDesigner.Design(axis.Length, s => ground(axis.PointAt(s)), opt);
            var g = res.Grade;
            pins.Add(opt.Fixed.ToList());
            halves.Add(opt.FixedHalf.ToList());
            maxG.Add(opt.MaxGrade);
            warns.Add(res.Warnings.ToList());
            g.AdjustTerrain = true;
            g.CutSlope = Scene.Cut;
            g.FillSlope = Scene.Fill;
            grades.Add(g);
        }
        // 2) Vias com o greide em todas as marcas e as interseções nos nós (como o plugin: o greide vai junto com a via).
        var idx = 0;
        foreach (var grp in w.Groups)
        {
            var g = grades[idx++];
            foreach (var d in grp) d.Output.Grade = g.Clone();
        }
        var sc = new Scene { World = w, Ground = ground };
        sc.Grades.AddRange(grades);
        sc.Fixed.AddRange(fixedPts);
        sc.Pins.AddRange(pins);
        sc.Halves.AddRange(halves);
        sc.MaxGrades.AddRange(maxG);
        sc.GradeWarnings.AddRange(warns);
        // 3) Superfícies: nós (via principal atravessa) e vias (concordância onde são secundárias).
        foreach (var n in w.Nodes)
        {
            var it = n.Layout.Definition!;
            sc.Nodes[it.Id] = NodeFor(w, it);
        }
        for (int i = 0; i < w.Roads.Count; i++) sc.Surfaces.Add(SurfaceFor(sc, i));
        // 4) Peças de todas as marcas.
        foreach (var p in Y34TipTests.Geometry(w)) sc.Parts.Add((p.Def, p.Piece));
        // 5) Terraplenagem e Toposolid.
        var (mn, mx) = Bounds(w);
        sc.Original = TerrainMesh.FromFunction(mn - new Vec2(40, 40), mx + new Vec2(40, 40), 2.5, ground);
        var corridors = new List<GradeCorridor>();
        var pads = new List<GradePad>();
        for (int i = 0; i < w.Roads.Count; i++) corridors.AddRange(RoadCorridors(sc, i));
        foreach (var n in w.Nodes) pads.AddRange(NodePads(sc, n));
        sc.Design = Grading.Design(corridors, pads, p => sc.Original.Z(p), 0, 60, 0.5, 1.0, 1.0);
        sc.Terrain = TerrainRebuild.Simulate(sc.Original, sc.Design);
        return sc;
    }

    private static (Vec2 Min, Vec2 Max) Bounds(Y34TipTests.World w)
    {
        var pts = w.Roads.SelectMany(r => r.Axis.Points).ToList();
        return (new Vec2(pts.Min(p => p.X), pts.Min(p => p.Y)), new Vec2(pts.Max(p => p.X), pts.Max(p => p.Y)));
    }

    /// <summary>Pontos onde dois eixos se cruzam (ou onde a ponta de um encosta no outro).</summary>
    private static IEnumerable<Vec2> Crossings(Polyline2 a, Polyline2 b)
    {
        var res = new List<Vec2>();
        for (int i = 0; i + 1 < a.Points.Count; i++)
            for (int j = 0; j + 1 < b.Points.Count; j++)
            {
                var p = a.Points[i];
                var r = a.Points[i + 1] - p;
                var q = b.Points[j];
                var s = b.Points[j + 1] - q;
                var den = r.Cross(s);
                if (Math.Abs(den) < 1e-9) continue;
                var t = (q - p).Cross(s) / den;
                var u = (q - p).Cross(r) / den;
                if (t >= -1e-6 && t <= 1 + 1e-6 && u >= -1e-6 && u <= 1 + 1e-6) res.Add(p + r * t);
            }
        return res.DistinctBy(v => (Math.Round(v.X, 2), Math.Round(v.Y, 2)));
    }

    /// <summary>Nó: superfície costurada das vias que chegam nele (como MarkingService.NodeFor).</summary>
    public static NodeSurface NodeFor(Y34TipTests.World w, IntersectionDefinition it)
    {
        var legs = new List<NodeSurface.Leg>();
        foreach (var id in it.RoadIds.Distinct())
        {
            var i = RoadIndex(w, id);
            if (i < 0) continue;
            var pav = w.Roads[i].Def;
            var half = Math.Max(1, Math.Max(pav.LeftWidth, pav.RightWidth));
            var grade = pav.Output.Grade!.Clone();
            grade.CrossfallWidth = half;
            var rank = pav.Hierarchy is { } h && h != HierarquiaViaria.NaoDefinida ? (int)h : 10;
            legs.Add(new NodeSurface.Leg(new GradeSurface(w.Roads[i].Axis, grade), 0, half, rank, pav.Id));
        }
        return new NodeSurface(legs);
    }

    /// <summary>Superfície do greide da via, com a concordância nos nós em que ela é secundária (como MarkingService.SurfaceFor).</summary>
    public static GradeSurface SurfaceFor(Scene sc, int i)
    {
        var w = sc.World;
        var pav = w.Roads[i].Def;
        var grade = pav.Output.Grade!.Clone();
        grade.CrossfallWidth = Math.Max(1, Math.Max(pav.LeftWidth, pav.RightWidth));
        var surf = new GradeSurface(w.Roads[i].Axis, grade);
        foreach (var n in w.Nodes)
        {
            var it = n.Layout.Definition!;
            if (!it.RoadIds.Contains(pav.Id)) continue;
            var node = sc.Nodes[it.Id];
            if (node.Major?.Id == pav.Id) continue;
            var cut = pav.Exclusions.Where(e => e.SourceId == it.Id).SelectMany(e => e.Points).ToList();
            if (cut.Count == 0) continue;
            var locs = cut.Select(surf.Locate).ToList();
            var c = surf.Locate(new Polygon2(cut).Centroid).S;
            var reach = locs.Max(l => Math.Abs(l.S - c));
            var own = surf.Grade.GradeAt(Math.Clamp(c + reach + 10, 0, surf.Axis.Length));
            var len = Math.Clamp(20 + 200 * Math.Abs(own), 20, 40);
            surf.Blends.Add(new NodeBlend(node, c, reach, len, 0));
        }
        return surf;
    }

    /// <summary>Superfície de uma marca: a do nó (interseção e filhas) ou a da via a que pertence (nulo = fora da cena).</summary>
    public static Func<Vec2, double>? SurfaceOf(Scene sc, MarkingDefinition def)
    {
        var w = sc.World;
        foreach (var n in w.Nodes)
        {
            var it = n.Layout.Definition!;
            if (def.Id == it.Id) return sc.Nodes[it.Id].Z;
            // Marcas do nó: a superfície do nó dentro dele e a da via fora (como MarkingService.GradeForCore).
            if (it.ChildIds.Contains(def.Id) || def.GroupId == it.Id) return MembersOf(sc, it);
        }
        for (int i = 0; i < w.Groups.Count; i++)
            if (w.Groups[i].Contains(def)) return sc.Surfaces[i].Z;
        return null;
    }

    /// <summary>Superfície das marcas de um nó (NodeSurface.ForMembers com as superfícies das vias dele).</summary>
    public static Func<Vec2, double> MembersOf(Scene sc, IntersectionDefinition it) => sc.Nodes[it.Id].ForMembers(Roads(sc, it));

    /// <summary>Vias do nó para as marcas dele (como MarkingService.NodeRoads): trecho do eixo no recorte do nó e meia faixa da via.</summary>
    public static List<NodeSurface.MemberRoad> Roads(Scene sc, IntersectionDefinition it)
    {
        var roads = new List<NodeSurface.MemberRoad>();
        foreach (var id in it.RoadIds.Distinct())
        {
            var i = RoadIndex(sc.World, id);
            if (i < 0) continue;
            var pav = sc.World.Roads[i].Def;
            var surf = sc.Surfaces[i];
            var cut = pav.Exclusions.Where(e => e.SourceId == it.Id).SelectMany(e => e.Points).Select(q => surf.Locate(q).S).ToList();
            if (cut.Count == 0) continue;
            roads.Add(new NodeSurface.MemberRoad(surf, 0, id, cut.Min(), cut.Max(), Math.Max(pav.TotalLeft, pav.TotalRight) + 3));
        }
        return roads;
    }

    /// <summary>
    /// Superfície de referência da divisão dos pisos em planos (como MarkingService.FloorParts): a da via principal no nó e
    /// nas marcas do nó; a da própria via nas marcas dela.
    /// </summary>
    public static GradeSurface? RefOf(Scene sc, MarkingDefinition def, Vec2? at = null)
    {
        var w = sc.World;
        foreach (var n in w.Nodes)
        {
            var it = n.Layout.Definition!;
            if (def.Id == it.Id) return sc.Nodes[it.Id].Major?.Surface;
            if (it.ChildIds.Contains(def.Id) || def.GroupId == it.Id)
                return at is { } p ? sc.Nodes[it.Id].MemberReference(Roads(sc, it), p) : sc.Nodes[it.Id].Major?.Surface;
        }
        for (int i = 0; i < w.Groups.Count; i++)
            if (w.Groups[i].Contains(def)) return sc.Surfaces[i];
        return null;
    }

    /// <summary>Nó de que a marca é membro (nulo = o próprio nó ou marca de via).</summary>
    public static IntersectionDefinition? NodeOf(Scene sc, MarkingDefinition def)
    {
        foreach (var n in sc.World.Nodes)
        {
            var it = n.Layout.Definition!;
            if (def.Id != it.Id && (it.ChildIds.Contains(def.Id) || def.GroupId == it.Id)) return it;
        }
        return null;
    }

    /// <summary>Tolerância de plano dos pisos no greide (MarkingService.GradeFloorTolerance).</summary>
    public const double FloorTolerance = 0.02;

    /// <summary>Pisos divididos em partes planas, como o plugin cria no Revit (mesmo agrupamento, tolerância e referência).</summary>
    public static List<(MarkingDefinition Def, MarkingPiece Piece, Polygon2 Merged, Polygon2 Part, Plane3 Plane, bool Warped, Func<Vec2, double> Z)> Floors(Scene sc)
    {
        var res = new List<(MarkingDefinition, MarkingPiece, Polygon2, Polygon2, Plane3, bool, Func<Vec2, double>)>();
        foreach (var grp in sc.Parts.GroupBy(p => (p.Def, p.Piece.Color, E: Math.Round(p.Piece.Elevation, 3), T: Math.Round(p.Piece.Thickness, 3), p.Piece.Layer)))
        {
            var zf = SurfaceOf(sc, grp.Key.Def);
            if (zf == null) continue;
            var node = NodeOf(sc, grp.Key.Def);
            foreach (var m in PolygonOps.Union(grp.Select(x => x.Piece.Shape)).Where(p => p.Area > 0.01))
            {
                var split = node != null
                    ? FloorPlanes.SplitMember(m, zf, FloorTolerance, sc.Nodes[node.Id].MemberZones(Roads(sc, node)), sc.Nodes[node.Id].Major?.Surface, 25, 1.0)
                    : FloorPlanes.Split(m, zf, FloorTolerance, RefOf(sc, grp.Key.Def), 25, ringStep: 1.0);
                foreach (var (part, plane, warped) in split)
                    res.Add((grp.Key.Def, grp.First().Piece, m, part, plane, warped, zf));
            }
        }
        return res;
    }

    /// <summary>Faixas da terraplenagem da via (como TerrainService.Features): plataforma de calçada a calçada, no greide, com a concordância.</summary>
    public static List<GradeCorridor> RoadCorridors(Scene sc, int i)
    {
        var road = sc.World.Roads[i].Def;
        var axis = sc.World.Roads[i].Axis;
        var g = road.Output.Grade!;
        var prof = g.ToProfile(2);
        var right = -(road.TotalRight + 0.3);
        var left = road.TotalLeft + 0.3;
        var s0 = Math.Max(0, road.StartSetback);
        var s1 = Math.Max(s0 + 0.5, axis.Length - Math.Max(0, road.EndSetback));
        var corr = InfraRoads.RoadCorridors(axis, prof, right, left, s0, s1, Scene.Subgrade, g.CutSlope, g.FillSlope, Array.Empty<MarkingDefinition>());
        InfraRoads.FitToSurface(corr, left, right, sc.Surfaces[i].Z, Scene.Subgrade, InfraRoads.SectionBreaks(g));
        // Nós em que a via é secundária: a crista da via principal, prolongada, cruza a concordância.
        foreach (var b in sc.Surfaces[i].Blends)
            foreach (var c in corr) c.Breaklines.AddRange(InfraRoads.NodeBreaklines(b.Node));
        return corr;
    }

    /// <summary>Plataformas do nó (como TerrainService.Features): contorno das peças, na superfície do nó, sob o pavimento.</summary>
    public static List<GradePad> NodePads(Scene sc, IntersectionDemo.Scene n)
    {
        var it = n.Layout.Definition!;
        var node = sc.Nodes[it.Id];
        var pieces = sc.Parts.Where(p => p.Def.Id == it.Id && p.Piece.Elevation < 0.5).Select(p => p.Piece.Shape);
        var pads = PolygonOps.Union(pieces).Where(u => u.Area > 1)
            .Select(u => new GradePad(u, -Scene.Subgrade) { CutSlope = Scene.Cut, FillSlope = Scene.Fill, Label = "Interseção", ZAt = q => node.Z(q) - Scene.Subgrade })
            .ToList();
        // Marcas do nó no chão (calçadas de esquina, extensões, rampas): plataforma na superfície delas, sem talude próprio –
        // ficam dentro das plataformas do nó e das vias (como TerrainService.Features).
        var members = MembersOf(sc, it);
        var own = PolygonOps.Union(pieces).Where(u => u.Area > 1).ToList();
        var mine = sc.Parts.Where(p => p.Def.Id != it.Id && NodeOf(sc, p.Def)?.Id == it.Id && p.Piece.Elevation < 0.5).Select(p => p.Piece.Shape).ToList();
        foreach (var u in PolygonOps.Difference(PolygonOps.Union(mine), own).Where(u => u.Area > 0.05))
            pads.Add(new GradePad(u, -Scene.Subgrade) { Daylight = false, Label = "Interseção", ZAt = q => members(q) - Scene.Subgrade });
        foreach (var pd in pads) pd.Breaklines.AddRange(InfraRoads.NodeBreaklines(node));
        return pads;
    }
}
