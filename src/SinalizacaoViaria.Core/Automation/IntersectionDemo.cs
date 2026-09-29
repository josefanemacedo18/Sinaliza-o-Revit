using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Monta uma cena de exemplo (vias do "Sinalizar via" + interseção + sinalização) sem o Revit, reproduzindo o que o
/// plugin faz no projeto: usada na pré-visualização da janela de interseções e nos testes.
/// </summary>
public static class IntersectionDemo
{
    public sealed record Scene(List<MarkingDefinition> Definitions, Dictionary<string, Polyline2> Paths, IntersectionLayout Layout);

    /// <summary>
    /// Via principal (modelo <paramref name="mainTemplate"/>) no eixo X e via secundária a <paramref name="angleDeg"/>°;
    /// com <paramref name="tee"/> a secundária termina na principal (entroncamento em T/Y).
    /// </summary>
    public static Scene Create(IntersectionDefinition template, Catalogo cat, int mainTemplate = 1, int minorTemplate = 0,
        double angleDeg = 90, bool tee = false, double length = 110)
    {
        var defs = new List<MarkingDefinition>();
        var paths = new Dictionary<string, Polyline2>();
        var roads = new List<IntersectionRoad>();
        var groups = new List<List<MarkingDefinition>>();
        void Road(int tpl, Vec2 a, Vec2 b)
        {
            var pr = PathReference.FromPoints(new[] { a, b }, 0);
            var g = RoadTemplates.All[Math.Clamp(tpl, 0, RoadTemplates.All.Count - 1)].Create().Build(pr, new OutputSettings(), cat);
            var pav = g.OfType<RoadPavementDefinition>().FirstOrDefault() ?? RoadSectionInference.Infer(g, cat);
            if (pav == null) return;
            if (!g.Contains(pav)) g.Add(pav);
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) paths[m.Id] = axis;
            defs.AddRange(g);
            groups.Add(g);
            roads.Add(new IntersectionRoad(pav, axis));
        }
        var u = new Vec2(Math.Cos(angleDeg * Math.PI / 180), Math.Sin(angleDeg * Math.PI / 180));
        Road(mainTemplate, new Vec2(-length, 0), new Vec2(length, 0));
        Road(minorTemplate, tee ? Vec2.Zero : u * -length, u * length);
        var d = (IntersectionDefinition)template.CloneWithNewId();
        d.Node = Vec2.Zero;
        d.MainRoadId = null;
        d.NeighborNodes = template.NeighborNodes.ToList();
        return Create(d, roads, groups, paths, defs, cat);
    }

    /// <summary>
    /// Cena com vias reais: <paramref name="groups"/> são as marcas de cada via (cópias – recebem os recortes) e
    /// <paramref name="paths"/> os eixos resolvidos por id de marca.
    /// </summary>
    public static Scene Create(IntersectionDefinition d, List<IntersectionRoad> roads, List<List<MarkingDefinition>> groups,
        Dictionary<string, Polyline2> paths, List<MarkingDefinition>? defs = null, Catalogo? cat = null)
    {
        defs ??= groups.SelectMany(g => g).ToList();
        d.RoadIds = roads.Select(r => r.Def.Id).ToList();
        // Mesmo perfil de calçada que o plugin lê das vias no projeto (cada lado de cada via).
        if (d.MatchRoadSection && cat != null)
        {
            var pctx = new BuildContext { Catalog = cat };
            var profiles = roads.Select((r, k) => EdgeProfile.FromRoadSides(r.Def, groups[k].Where(m => m.Id != r.Def.Id), pctx)).ToList();
            d.RoadProfiles = profiles;
            d.EdgeProfile = EdgeProfile.Best(profiles.Select(p => p.Left.Sum(b => b.Width) >= p.Right.Sum(b => b.Width) ? p.Left : p.Right));
        }
        var L = IntersectionGenerator.Layout(d, roads);
        for (int k = 0; k < roads.Count; k++)
            foreach (var m in groups[k])
                foreach (var cut in IntersectionGenerator.CutsFor(m, k, L))
                    m.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = cut.Outer.ToList() });
        var children = L.Pavement.Count > 0
            ? IntersectionGenerator.Children(d, L, new OutputSettings(), 0, groups.Select(g => (IReadOnlyCollection<MarkingDefinition>)g).ToList())
            : new List<MarkingDefinition>();
        var ramps = children.OfType<RampDefinition>().Select(r => RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points))).ToList();
        foreach (var m in defs.Where(IntersectionGenerator.IsPhysical))
            foreach (var fp in ramps) m.Exclusions.Add(new ExclusionZone { Points = fp.Outer.ToList() });
        defs.Add(d);
        defs.AddRange(children);
        return new Scene(defs, paths, L);
    }

    /// <summary>Geometria de toda a cena (placas ficam de fora quando <paramref name="signs"/> é falso).</summary>
    public static MarkingGeometry Build(Scene s, BuildContext baseCtx, bool signs = true)
    {
        var ctx = new BuildContext
        {
            Catalog = baseCtx.Catalog,
            Glyphs = baseCtx.Glyphs,
            Lookup = id => s.Definitions.FirstOrDefault(x => x.Id == id),
            PathOf = x => s.Paths.GetValueOrDefault(x.Id),
        };
        var geo = new MarkingGeometry();
        foreach (var def in s.Definitions)
        {
            if (!signs && def is SignDefinition) continue;
            Polyline2? path = s.Paths.GetValueOrDefault(def.Id);
            if (path == null && (def.Path ?? (def as HatchMarkingDefinition)?.Boundary) is { Points.Count: >= 2 } pr)
                path = new Polyline2(pr.Points, pr.Closed);
            try
            {
                var g = MarkingBuilder.Build(def, path, ctx);
                if (def is IntersectionDefinition) geo.Warnings.AddRange(g.Warnings);
                g.Warnings.Clear();
                geo.Merge(g);
            }
            catch { /* pré-visualização: ignora a peça */ }
        }
        return geo;
    }
}
