using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

internal static class IntersectionForms
{
    public static FormWindow Intersection(IntersectionDefinition d, bool edit) =>
        new FormWindow(edit ? "Editar interseção" : "Interseção", "Interseção de vias",
                "Ajusta o cruzamento/entroncamento das vias do \"Sinalizar via\": pavimento contínuo no miolo, esquinas com raio na face do " +
                "meio-fio, calçadas e pontas de canteiro reconstruídas, sinalização das vias interrompida antes da travessia e vagas " +
                "removidas a 5 m da esquina. Travessias, linhas de retenção e rebaixamentos são criados em cada ramo.",
                d, null, false, edit ? "Aplicar" : "Criar", 620, 600)
            .Number("Raio das esquinas (m)", () => d.CornerRadius, v => d.CornerRadius = v, 0, 40, tooltip: "Na face do meio-fio. Vias locais 5–6 m; coletoras/arteriais 8–12 m; veículos pesados até 15 m.")
            .Check("Faixas de pedestres em cada ramo", () => d.Crosswalks, v => d.Crosswalks = v)
            .Number("Largura da faixa de pedestres (m)", () => d.CrosswalkWidth, v => d.CrosswalkWidth = v, 3, 10)
            .Number("Recuo da faixa em relação à esquina (m)", () => d.CrosswalkSetback, v => d.CrosswalkSetback = v, 0, 20)
            .Check("Linhas de retenção (LRE)", () => d.StopLines, v => d.StopLines = v)
            .Check("Rebaixamentos de calçada nas travessias (NBR 9050)", () => d.Ramps, v => d.Ramps = v);
}

internal static class IntersectionRunner
{
    /// <summary>Executa a atualização de interseções/rotatórias numa transação própria.</summary>
    public static List<RenderResult> Run(UIDocument uidoc, string name, Func<IntersectionService, List<RenderResult>> action)
    {
        var doc = uidoc.Document;
        using var scope = MarkingService.RenderScope();
        using var t = new Transaction(doc, name);
        t.Start();
        var service = new MarkingService(doc, uidoc.ActiveView);
        var results = action(new IntersectionService(doc, service));
        t.Commit();
        return results;
    }
}

/// <summary>Cria ou atualiza a interseção mais próxima do ponto clicado.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdIntersecao : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var pick = Picking.PickPoint(uidoc, "Clique próximo ao cruzamento/entroncamento das vias (criadas com \"Sinalizar via\")");
        if (pick == null) return Result.Cancelled;
        var p = DetailHelpers.ToCore(pick);
        var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        var roads = svc.Roads();
        if (roads.Count < 2)
            throw new UserMessageException("São necessárias duas vias com pavimento criadas pelo \"Sinalizar via\" (a seção fica registrada no pavimento).");
        var nodes = IntersectionGenerator.FindNodes(roads);
        if (nodes.Count == 0) throw new UserMessageException("Os eixos das vias não se cruzam nem se encontram (entroncamento).");
        var (node, ids) = nodes.OrderBy(n => n.Node.DistanceTo(p)).First();
        if (node.DistanceTo(p) > 60) throw new UserMessageException("Nenhum cruzamento de vias perto do ponto clicado.");

        var existing = MarkingStorage.Definitions(doc).OfType<IntersectionDefinition>()
            .FirstOrDefault(e => e.Node.DistanceTo(node) < IntersectionGenerator.NodeMergeDistance * 2);
        var d = existing ?? UiHelpers.Remembered<IntersectionDefinition>("Intersecao") ?? new IntersectionDefinition();
        if (existing == null)
        {
            d = (IntersectionDefinition)d.CloneWithNewId();
            d.ChildIds.Clear();
            d.RoadIds.Clear();
            d.Node = node;
            d.Z = roads[ids[0]].Def.Path?.Z ?? 0;
            d.Output = roads[ids[0]].Def.Output.Clone();
        }
        foreach (var i in ids) if (!d.RoadIds.Contains(roads[i].Def.Id)) d.RoadIds.Add(roads[i].Def.Id);
        if (UiHelpers.ShowModal(IntersectionForms.Intersection(d, existing != null)) != true) return Result.Cancelled;
        UiHelpers.Remember("Intersecao", d);
        PluginContext.SaveSettings();
        var results = IntersectionRunner.Run(uidoc, "SV - Interseção", s => s.Refresh(d));
        Report("Interseção", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }
}

internal static class RoundaboutForms
{
    public static FormWindow Roundabout(RoundaboutDefinition d, bool edit, bool hasRoads)
    {
        string? angles = hasRoads ? null : string.Join("; ", d.Legs.Select(l => l.AngleDeg.ToString("0.#", UiHelpers.PtBr)));
        var legWidth = d.Legs.FirstOrDefault()?.Width ?? 7.0;
        var w = new FormWindow(edit ? "Editar rotatória" : "Rotatória", "Rotatória",
            hasRoads
                ? "Os ramos foram detectados a partir das vias do \"Sinalizar via\" que passam pelo centro; as vias são recortadas e ligadas à rotatória."
                : "Informe os ângulos dos ramos (graus, 0 = leste, anti-horário) e a largura das pistas dos ramos.",
            d, () =>
            {
                var c = (RoundaboutDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.Center = Core.Geometry.Vec2.Zero;
                var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
                var L = RoundaboutGenerator.Layout(c);
                var g = RoundaboutGenerator.Build(c, ctx);
                foreach (var ch in RoundaboutGenerator.Children(c, L, new OutputSettings(), 0))
                {
                    var p = ch.Path ?? (ch as HatchMarkingDefinition)?.Boundary;
                    if (ch is SignDefinition) continue;
                    g.Merge(MarkingBuilder.Build(ch, p == null ? null : new Core.Geometry.Polyline2(p.Points, p.Closed), ctx));
                }
                return new FormPreview(g, null, null, $"Diâmetro externo: {UiHelpers.F(2 * c.OuterRadius, "0.0")} m");
            }, false, edit ? "Aplicar" : "Criar", 1080, 720);
        w.Number("Raio da ilha central (m)", () => d.IslandRadius, v => d.IslandRadius = v, 1, 100)
         .Number("Faixa galgável em volta da ilha (m)", () => d.ApronWidth, v => d.ApronWidth = v, 0, 10, tooltip: "Para o giro de ônibus e caminhões (bloquete elevado 6 cm).")
         .Integer("Faixas na pista giratória", () => d.Lanes, v => d.Lanes = v, 1, 4)
         .Number("Largura de cada faixa (m)", () => d.LaneWidth, v => d.LaneWidth = v, 3, 10)
         .Number("Raio de entrada/saída (m)", () => d.EntryRadius, v => d.EntryRadius = v, 0, 60)
         .Number("Calçada em volta (m)", () => d.SidewalkWidth, v => d.SidewalkWidth = v, 0, 20)
         .Choice("Pavimento", new[] { ("Asfalto", TipoPavimento.Asfalto), ("Bloquete", TipoPavimento.Bloquete), ("Concreto", TipoPavimento.Concreto), ("Nenhum", TipoPavimento.Nenhum) },
             () => d.Pavement, v => d.Pavement = v);
        if (!hasRoads)
            w.Text("Ângulos dos ramos (°)", () => angles, v => angles = v, tooltip: "Ex.: 0; 90; 180; 270")
             .Number("Largura das pistas dos ramos (m)", () => legWidth, v =>
             {
                 legWidth = v;
                 d.Legs = ParseAngles(angles).Select(a => new RoundaboutLeg { AngleDeg = a, Width = legWidth, Sidewalk = d.SidewalkWidth }).ToList();
             }, 3, 40);
        w.Section("Ramos e sinalização")
         .Check("Ilhas separadoras (gota) nos ramos", () => d.SplitterIslands, v => d.SplitterIslands = v)
         .Number("Comprimento das ilhas separadoras (m)", () => d.SplitterLength, v => d.SplitterLength = v, 3, 60)
         .Number("Largura das ilhas junto à pista (m)", () => d.SplitterWidth, v => d.SplitterWidth = v, 0.8, 10)
         .Check("Linhas de dê a preferência, símbolos e zebrados", () => d.Markings, v => d.Markings = v)
         .Check("Travessias de pedestres e rebaixamentos", () => d.Crosswalks, v => d.Crosswalks = v)
         .Check("Placas R-2 e R-33 em cada entrada", () => d.Signs, v => d.Signs = v)
         .Check("Paisagismo na ilha central (árvores)", () => d.Landscaping, v => d.Landscaping = v);
        return w;
    }

    public static List<double> ParseAngles(string? text)
    {
        var res = new List<double>();
        foreach (var tok in (text ?? "").Split(new[] { ';', ' ', '\t', '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(tok.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                res.Add(v);
        return res;
    }
}

/// <summary>Rotatória sobre o cruzamento de vias (ou isolada, com ramos por ângulo).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdRotatoria : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var pick = Picking.PickPoint(uidoc, "Clique o centro da rotatória (ex.: cruzamento dos eixos das vias)");
        if (pick == null) return Result.Cancelled;
        var center = DetailHelpers.ToCore(pick);
        var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        var roads = svc.Roads();
        // Encaixa o centro no cruzamento mais próximo, se houver.
        var nodes = IntersectionGenerator.FindNodes(roads);
        var near = nodes.OrderBy(n => n.Node.DistanceTo(center)).FirstOrDefault();
        if (near.Roads != null && near.Node.DistanceTo(center) < 8) center = near.Node;

        var d = UiHelpers.Remembered<RoundaboutDefinition>("Rotatoria") ?? new RoundaboutDefinition();
        d = (RoundaboutDefinition)d.CloneWithNewId();
        d.ChildIds.Clear();
        d.Center = center;
        d.Z = UnitConv.M(pick.Z);
        d.Output = PluginContext.Settings.NewOutput();
        d.Legs = RoundaboutGenerator.LegsFromRoads(center, roads, d.OuterRadius + 25);
        var hasRoads = d.Legs.Count > 0;
        if (!hasRoads && d.Legs.Count == 0)
            foreach (var a in new[] { 0.0, 90, 180, 270 }) d.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = d.SidewalkWidth });
        CommandBase.EnsureDetailViewPublic(uidoc, d.Output);
        if (UiHelpers.ShowModal(RoundaboutForms.Roundabout(d, false, hasRoads)) != true) return Result.Cancelled;
        if (hasRoads) d.Legs = RoundaboutGenerator.LegsFromRoads(center, roads, d.OuterRadius + 25);
        UiHelpers.Remember("Rotatoria", d);
        PluginContext.SaveSettings();

        var results = IntersectionRunner.Run(uidoc, "SV - Rotatória", s =>
        {
            // Interseções no mesmo nó deixam de existir (a rotatória as substitui).
            var service = new MarkingService(doc, uidoc.ActiveView);
            foreach (var it in MarkingStorage.Definitions(doc).OfType<IntersectionDefinition>().Where(i => i.Node.DistanceTo(center) < d.OuterRadius + 5))
            {
                foreach (var c in it.ChildIds) service.Delete(c);
                service.Delete(it.Id);
            }
            return s.Refresh(d);
        });
        Report("Rotatória", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }
}
