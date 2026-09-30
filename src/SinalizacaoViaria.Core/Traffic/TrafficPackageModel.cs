using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>
/// Cópia em memória do modelo (definições e eixos já resolvidos) para testar as soluções como elas ficam no projeto: o
/// pacote é aplicado à cópia pela mesma regra do plugin e a rede é lida de novo – a solução avaliada é a implantada.
/// Os eixos são resolvidos antes (no Revit, na thread da API); o resto roda em segundo plano.
/// </summary>
public sealed class ModelSnapshot
{
    public required IReadOnlyList<MarkingDefinition> Defs { get; init; }
    /// <summary>Eixo e cota de cada definição com caminho (id → eixo).</summary>
    public required IReadOnlyDictionary<string, (Polyline2 Axis, double Z)> Axes { get; init; }
    public required Catalogo Catalog { get; init; }

    /// <summary>Rede do modelo com o pacote aplicado (nulo = o modelo como está), lida como o plugin lê o projeto.</summary>
    public TrafficNetwork Network(ProjectPackage? package)
    {
        var defs = Defs.Select(d => MarkingDefinition.FromJson(d.ToJson())!).ToList();
        var paths = new Dictionary<string, Polyline2>();
        foreach (var (id, a) in Axes) paths[id] = a.Axis;
        if (package != null) TrafficPackageModel.ApplyToModel(defs, paths, package, Catalog);
        (Polyline2, double)? Ax(MarkingDefinition d) =>
            Axes.TryGetValue(d.Id, out var a) ? (a.Axis, a.Z)
            : paths.TryGetValue(d.Id, out var p) ? (p, 0.0)
            : d.Path is { Points.Count: >= 2 } pr ? (new Polyline2(pr.Points, pr.Closed), pr.Z) : null;
        var ctx = new BuildContext { Catalog = Catalog, Lookup = id => defs.FirstOrDefault(x => x.Id == id), AllDefinitions = () => defs, PathOf = d => Ax(d)?.Item1 };
        return TrafficNetworkBuilder.Build(defs, Ax, d =>
        {
            try { return MarkingBuilder.Build(d, Ax(d)?.Item1, ctx); }
            catch { return null; }
        });
    }
}

/// <summary>
/// O que um pacote de intervenção muda no modelo – a mesma regra no projeto (ApplyPackage do plugin) e no modelo em
/// memória usado para conferir "avaliado × aplicado": campos da interseção, planos dos outros semáforos, rotatória que
/// substitui o cruzamento, elementos avulsos (grupos focais, placas, retenções) e retiradas.
/// </summary>
public static class TrafficPackageModel
{
    /// <summary>Campos da interseção alterados pelo pacote (controle, conversões, bolsões, travessias, linhas, plano).</summary>
    public static void ApplyTo(IntersectionDefinition def, ProjectPackage p)
    {
        if (p.Control is { } c) def.Control = c;
        if (p.LeftTurns is { } lt) def.LeftTurns = lt;
        if (p.LeftTurnPockets is { } lp) def.LeftTurnPockets = lp;
        if (p.Crosswalks is { } cw) { def.Crosswalks = cw; if (cw) def.Ramps = true; }
        if (p.ApproachLines is { } al) def.ApproachLines = al;
        if (p.Plan != null) def.SignalPlan = p.Plan.Clone();
        else if (def.Control != ControleIntersecao.Semaforo) def.SignalPlan = null;
    }

    /// <summary>Plano de outro semáforo do pacote (onda verde).</summary>
    public static void ApplyPlan(IntersectionDefinition def, SignalPlanDef plan)
    {
        def.SignalPlan = plan.Clone();
        def.Control = ControleIntersecao.Semaforo;
    }

    /// <summary>Rotatória que substitui o cruzamento (tipo do pacote, saída da interseção).</summary>
    public static RoundaboutDefinition RoundaboutTemplate(IntersectionDefinition def, TipoRotatoria type)
    {
        var tpl = new RoundaboutDefinition();
        tpl.ApplyPreset(type);
        tpl.Output = def.Output.Clone();
        return tpl;
    }

    /// <summary>Elementos avulsos do pacote: grupos focais (elemento urbano), placas e linhas de retenção.</summary>
    public static List<MarkingDefinition> Elements(ProjectPackage p, Func<Vec2, double> zAt, Func<OutputSettings> output)
    {
        var res = new List<MarkingDefinition>();
        foreach (var pt in p.Points)
            res.Add(pt.Kind == "Semaforo"
                ? new UrbanElementDefinition { Code = pt.Code, Position = pt.Position, Direction = pt.Direction, Z = zAt(pt.Position), Output = output() }
                : new SignDefinition { Code = pt.Code, Position = pt.Position, Direction = pt.Direction, Z = zAt(pt.Position), Output = output() });
        foreach (var ln in p.Lines)
            res.Add(new LinearMarkingDefinition { Code = ln.Code, Variant = "0,40 m", PathRef = PathReference.FromPoints(new[] { ln.A, ln.B }, zAt(ln.A)), Output = output() });
        return res;
    }

    /// <summary>A marca é retirada pelo pacote (grupos focais da travessia que deixa de ser semaforizada).</summary>
    public static bool Removes(ProjectPackage p, MarkingDefinition d) =>
        p.RemoveNear is { } rm && d is UrbanElementDefinition u && (u.Code ?? "").StartsWith(rm.Code, StringComparison.OrdinalIgnoreCase) && u.Position.DistanceTo(rm.At) < rm.Radius;

    /// <summary>
    /// Aplica o pacote a um modelo em memória, como o plugin faz no projeto: a interseção é refeita pelo gerador (placas,
    /// marcas, travessias e recortes das vias), a rotatória substitui o cruzamento, os planos vão para os outros semáforos e
    /// os elementos avulsos entram no modelo. <paramref name="paths"/>: eixos resolvidos por id de marca.
    /// </summary>
    public static List<string> ApplyToModel(List<MarkingDefinition> defs, Dictionary<string, Polyline2> paths, ProjectPackage p, Catalogo cat)
    {
        var msgs = new List<string>();
        IntersectionRoad? RoadOf(string id)
        {
            if (defs.FirstOrDefault(d => d.Id == id) is not RoadPavementDefinition pav) return null;
            var axis = paths.GetValueOrDefault(pav.Id) ?? (pav.Path is { Points.Count: >= 2 } pr ? new Polyline2(pr.Points) : null);
            return axis == null ? null : new IntersectionRoad(pav, axis);
        }
        List<MarkingDefinition> GroupOf(RoadPavementDefinition pav) => defs.Where(d => d.GroupId != null && d.GroupId == pav.GroupId).ToList();
        void Clear(string sourceId)
        {
            defs.RemoveAll(d => d.GroupId == sourceId && d.Id != sourceId);
            foreach (var d in defs) d.Exclusions.RemoveAll(z => z.SourceId == sourceId);
        }
        void Regenerate(IntersectionDefinition it)
        {
            var roads = it.RoadIds.Select(RoadOf).Where(r => r != null).Cast<IntersectionRoad>().ToList();
            Clear(it.Id);
            defs.Remove(it);
            if (roads.Count < 2) { defs.Add(it); return; }
            IntersectionDemo.Create(it, roads, roads.Select(r => GroupOf(r.Def)).ToList(), paths, defs, cat);
        }

        if (p.NodeKey != null && !p.ScenarioOnly)
        {
            var def = defs.OfType<IntersectionDefinition>().FirstOrDefault(d => d.Id == p.NodeKey);
            if (def == null) msgs.Add("A interseção não foi encontrada no modelo – nada alterado nela.");
            else if (p.Roundabout is { } type)
            {
                var roads = def.RoadIds.Select(RoadOf).Where(r => r != null).Cast<IntersectionRoad>().ToList();
                Clear(def.Id);
                defs.Remove(def);
                var rb = RoundaboutTemplate(def, type);
                rb.Center = def.Node;
                rb.Z = def.Z;
                rb.Legs = RoundaboutGenerator.LegsFromRoads(rb.Center, roads, rb.OuterRadius + 25);
                var L = RoundaboutGenerator.Layout(rb);
                foreach (var r in roads)
                    foreach (var m in GroupOf(r.Def))
                        foreach (var cut in RoundaboutGenerator.RoadCuts(rb, L, m))
                            m.Exclusions.Add(new ExclusionZone { SourceId = rb.Id, Points = cut.Outer.ToList() });
                defs.Add(rb);
                foreach (var c in RoundaboutGenerator.Children(rb, L, def.Output.Clone(), def.Z)) { c.GroupId = rb.Id; defs.Add(c); }
                msgs.Add($"Cruzamento convertido em rotatória ({type}).");
            }
            else
            {
                ApplyTo(def, p);
                Regenerate(def);
                msgs.Add($"Interseção refeita ({def.Control}).");
            }
        }
        foreach (var (key, plan) in p.OtherPlans)
        {
            if (defs.OfType<IntersectionDefinition>().FirstOrDefault(d => d.Id == key) is not { } d2) continue;
            ApplyPlan(d2, plan);
            Regenerate(d2);
        }
        var gone = defs.RemoveAll(d => Removes(p, d));
        if (gone > 0) msgs.Add($"{gone} grupo(s) focal(is) retirado(s).");
        double ZAt(Vec2 at) => p.NodeKey != null && defs.OfType<IntersectionDefinition>().FirstOrDefault(d => d.Id == p.NodeKey) is { } it ? it.Z : 0;
        defs.AddRange(Elements(p, ZAt, () => new OutputSettings()));
        return msgs;
    }
}
