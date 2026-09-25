using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Ponto do sistema viário que pode receber um tratamento de conexão.</summary>
internal sealed record ConnectionTarget(string Kind, Vec2 Point, IntersectionRoad? Road = null, bool AtEnd = false,
    IntersectionDefinition? Intersection = null, RoundaboutDefinition? Roundabout = null, CulDeSacDefinition? CulDeSac = null);

internal static class ConnectionPicker
{
    /// <summary>Encontro de vias, rotatória ou ponta livre de via mais próxima do ponto (até 25 m).</summary>
    public static ConnectionTarget? Nearest(Document doc, IntersectionService svc, Vec2 p)
    {
        var all = MarkingStorage.Definitions(doc);
        var roads = svc.Roads();
        var cands = new List<(double D, ConnectionTarget T)>();
        foreach (var rb in all.OfType<RoundaboutDefinition>())
            cands.Add((Math.Max(0, rb.Center.DistanceTo(p) - rb.OuterRadius), new ConnectionTarget("rotatoria", rb.Center, Roundabout: rb)));
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(roads))
        {
            if (!IntersectionGenerator.NeedsIntersection(ids.Select(i => roads[i]).ToList(), node)) continue;
            if (all.OfType<RoundaboutDefinition>().Any(r => r.Center.DistanceTo(node) < r.OuterRadius + 5)) continue;
            var it = all.OfType<IntersectionDefinition>().FirstOrDefault(i => i.Node.DistanceTo(node) < 12);
            cands.Add((node.DistanceTo(p), new ConnectionTarget("no", node, Intersection: it)));
        }
        foreach (var r in roads)
            foreach (var (atEnd, e, _) in RoadConnection.FreeEnds(r, roads.Where(o => o != r).ToList()))
            {
                var cds = all.OfType<CulDeSacDefinition>().FirstOrDefault(c => c.RoadId == r.Def.Id && c.AtRoadEnd == atEnd);
                cands.Add((e.DistanceTo(p), new ConnectionTarget("ponta", e, r, atEnd, CulDeSac: cds)));
            }
        var best = cands.OrderBy(c => c.D).FirstOrDefault();
        return best.T != null && best.D <= 25 ? best.T : null;
    }
}

/// <summary>
/// Troca o tratamento de uma conexão do sistema viário: interseção, rotatória ou nada num encontro de vias;
/// cul-de-sac ou nada numa ponta livre.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdConexao : CommandBase
{
    private enum Acao { Intersecao, Rotatoria, CulDeSac, Remover }

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var pick = Picking.PickPoint(uidoc, "Clique no encontro de vias, na rotatória ou na ponta livre de uma via");
        if (pick == null) return Result.Cancelled;
        var svc0 = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        var target = ConnectionPicker.Nearest(doc, svc0, UnitConv.ToVec2(pick));
        if (target == null)
        {
            TaskDialog.Show(AppTitle, "Nenhum encontro de vias, rotatória ou ponta livre de via a menos de 25 m do ponto clicado.");
            return Result.Cancelled;
        }

        var end = target.Kind == "ponta";
        var current = target.Roundabout != null ? "rotatória" : target.Intersection != null ? "interseção" : target.CulDeSac != null ? "cul-de-sac" : "sem tratamento";
        var acao = end ? Acao.CulDeSac : target.Roundabout != null ? Acao.Intersecao : Acao.Rotatoria;
        var options = end
            ? new[] { ("Cul-de-sac (balão de retorno)", Acao.CulDeSac), ("Sem tratamento (remover o cul-de-sac)", Acao.Remover) }
            : new[] { ("Interseção (tipos I a IV, PARE / dê a preferência / semáforo)", Acao.Intersecao), ("Rotatória", Acao.Rotatoria), ("Sem tratamento (vias sobrepostas)", Acao.Remover) };
        var w = new FormWindow("Conexão", end ? "Ponta livre de via" : "Encontro de vias",
                $"Tratamento atual: {current}. Escolha o novo tratamento – a sinalização e a geometria das vias são refeitas automaticamente.",
                null, null, false, "Continuar", 560, 300)
            .Choice("Tratamento", options, () => acao, v => acao = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;

        List<RenderResult> results;
        switch (acao)
        {
            case Acao.Intersecao:
            {
                var d = UiHelpers.Remembered<IntersectionDefinition>("Intersecao") ?? new IntersectionDefinition();
                if (target.Intersection != null) d = (IntersectionDefinition)MarkingDefinition.FromJson(target.Intersection.ToJson())!;
                if (UiHelpers.ShowModal(IntersectionForms.Intersection(d, false)) != true) return Result.Cancelled;
                UiHelpers.Remember("Intersecao", d);
                results = IntersectionRunner.Run(uidoc, "SV - Conexão: interseção", s =>
                {
                    if (target.Roundabout != null) s.Remove(target.Roundabout);
                    return s.IntersectAll(d, target.Point, 30);
                });
                break;
            }
            case Acao.Rotatoria:
            {
                var roads = svc0.Roads();
                RoundaboutDefinition d;
                if (target.Roundabout != null) d = target.Roundabout;
                else
                {
                    d = (RoundaboutDefinition)(UiHelpers.Remembered<RoundaboutDefinition>("Rotatoria") ?? new RoundaboutDefinition()).CloneWithNewId();
                    d.ChildIds.Clear();
                    d.Center = target.Point;
                    d.Legs = RoundaboutGenerator.LegsFromRoads(target.Point, roads, d.OuterRadius + 25);
                }
                if (UiHelpers.ShowModal(RoundaboutForms.Roundabout(d, target.Roundabout != null, true)) != true) return Result.Cancelled;
                UiHelpers.Remember("Rotatoria", d);
                results = IntersectionRunner.Run(uidoc, "SV - Conexão: rotatória", s =>
                    target.Roundabout != null ? s.Refresh(d) : s.ConvertToRoundabout(target.Point, d));
                break;
            }
            case Acao.CulDeSac:
            {
                var d = target.CulDeSac != null
                    ? (CulDeSacDefinition)MarkingDefinition.FromJson(target.CulDeSac.ToJson())!
                    : UiHelpers.Remembered<CulDeSacDefinition>(nameof(CulDeSacDefinition)) ?? new CulDeSacDefinition();
                if (target.Road != null) RoadConnection.FitCulDeSac(d, target.Road, 0);
                var form = SidewalkForms.CulDeSac(d, true);
                if (form == null || UiHelpers.ShowModal(form) != true) return Result.Cancelled;
                UiHelpers.Remember(nameof(CulDeSacDefinition), d);
                PluginContext.SaveSettings();
                results = IntersectionRunner.Run(uidoc, "SV - Conexão: cul-de-sac", s => s.AddCulDeSac(target.Road!, target.AtEnd, d));
                break;
            }
            default:
                results = IntersectionRunner.Run(uidoc, "SV - Conexão: remover", s =>
                {
                    if (target.Intersection != null) s.Remove(target.Intersection);
                    if (target.Roundabout != null) s.Remove(target.Roundabout);
                    if (target.CulDeSac != null) s.Remove(target.CulDeSac);
                    return new List<RenderResult>();
                });
                break;
        }
        PluginContext.SaveSettings();
        Report("Conexão", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }
}

/// <summary>Define a hierarquia viária de vias existentes (todos os elementos da via e suas conexões).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdHierarquia : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var picked = MarkingPicker.PickMany(uidoc, "Selecione elementos das vias (qualquer linha, calçada ou pavimento) e clique em Concluir");
        var groups = picked.Select(p => p.Definition.GroupId).Where(g => g != null).Distinct().ToList();
        if (groups.Count == 0)
        {
            if (picked.Count > 0) TaskDialog.Show(AppTitle, "Selecione elementos criados pelo \"Sinalizar via\".");
            return Result.Cancelled;
        }
        var all = MarkingStorage.Definitions(doc);
        var current = all.FirstOrDefault(d => d.GroupId == groups[0])?.Hierarchy ?? HierarquiaViaria.Local;
        var h = current;
        var speed = false;
        var w = new FormWindow("Hierarquia viária", "Hierarquia viária (CTB art. 60)",
                $"{groups.Count} via(s) selecionada(s). A hierarquia é gravada em todos os elementos da via (parâmetro SV_Hierarquia) e usada nos " +
                "quantitativos e na escolha da via preferencial das interseções.", null, null, false, "Aplicar", 560, 320)
            .Choice("Hierarquia", Hierarquia.Definidas.Select(x => (Hierarquia.Label(x), x)), () => h, v => h = v)
            .Check("Ajustar a velocidade das linhas à hierarquia (CTB art. 61)", () => speed, v => speed = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;

        var defs = all.Where(d => d.GroupId != null && groups.Contains(d.GroupId)).ToList();
        foreach (var d in defs)
        {
            d.Hierarchy = h;
            if (speed && d is LinearMarkingDefinition { Speed: not null } l) l.Speed = Hierarquia.DefaultSpeed(h);
        }
        var results = MarkingCreator.Commit(uidoc, defs, "SV - Hierarquia viária");
        // Interseções, rotatórias e cul-de-sacs das vias: via preferencial e hierarquia da sinalização.
        var pavIds = defs.OfType<RoadPavementDefinition>().Select(p => p.Id).ToHashSet();
        results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões", s =>
        {
            var r = new List<RenderResult>();
            foreach (var it in s.DependentOn(defs)) r.AddRange(s.Refresh(it));
            foreach (var rb in s.RoundaboutsDependentOn(defs)) r.AddRange(s.Refresh(rb));
            foreach (var c in s.CulDeSacsDependentOn(defs)) r.AddRange(s.Refresh(c));
            return r;
        }).Where(r => r.Warnings.Count > 0));
        Report("Hierarquia viária", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }
}
