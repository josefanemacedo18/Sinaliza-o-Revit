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

/// <summary>
/// Move uma via inteira (eixo, marcas do grupo, extensões, piso tátil, elementos avulsos sobre ela) por um deslocamento, numa
/// única operação desfazível: as linhas do eixo são movidas, as posições guardadas são deslocadas e a via, as vias ligadas e
/// as interseções (levadas para o novo cruzamento) são refeitas como quando o eixo é editado à mão.
/// </summary>
internal static class RoadMover
{
    public static List<RenderResult> Move(UIDocument uidoc, IntersectionRoad road, Vec2 delta, string name)
    {
        var doc = uidoc.Document;
        var pav = road.Def;
        if (delta.Length < 1e-4) return new List<RenderResult>();
        var path = pav.PathRef;
        if (path.ElementIds.Any(PathReference.IsEdge))
            throw new UserMessageException("O eixo desta via é a borda de outro elemento (piso, laje...): mova esse elemento – a via o acompanha.");
        var curves = path.ElementIds.Select(id => doc.GetElement(id)).OfType<CurveElement>().ToList();
        if (path.IsAssociative && curves.Count == 0) throw new UserMessageException("As linhas do eixo desta via não foram encontradas.");
        if (curves.Any(c => c.Pinned)) throw new UserMessageException("O eixo desta via está fixado (pin): desafixe as linhas e repita.");

        var results = new List<RenderResult>();
        using var tg = new TransactionGroup(doc, name);
        tg.Start();
        using (MarkingService.RenderScope())
        using (Updater.MarkingUpdater.Pause())
        using (var t = new Transaction(doc, name))
        {
            t.Start();
            var service = new MarkingService(doc, uidoc.ActiveView);
            var inter = new IntersectionService(doc, service);
            var all = MarkingStorage.Definitions(doc);
            var roads = inter.Roads();
            var members = RoadSpacing.Members(all, pav, road.Axis, roads);
            var movedRoadIds = members.OfType<RoadPavementDefinition>().Select(p => p.Id).ToHashSet();
            movedRoadIds.Add(pav.Id);

            // 1. O eixo (fonte única) e as posições guardadas nas marcas que vão junto.
            if (curves.Count > 0)
                ElementTransformUtils.MoveElements(doc, curves.Select(c => c.Id).ToList(), UnitConv.ToXyz(delta, 0));
            RoadSpacing.Translate(members, delta);
            // Gravadas já deslocadas: o ímã e as vias ligadas leem o projeto antes de a via ser refeita.
            var stored = MarkingStorage.All(doc).ToLookup(r => r.MarkingId);
            foreach (var m in members)
                foreach (var r in stored[m.Id]) MarkingStorage.Write(r.Element, m, r.Color);
            service.Invalidate();

            // 2. Via, vias ligadas, extensões e piso tátil refeitos; nós das interseções levados para o novo cruzamento antes
            //    de refazer as interseções (as personalizações de cada uma ficam).
            Updater.MarkingUpdater.Regenerate(doc, members, PluginContext.Settings.AutoConnect, s =>
            {
                var after = s.Roads();
                var now = MarkingStorage.All(doc);
                var its = now.GroupBy(r => r.MarkingId).Select(g => g.First().Definition).OfType<IntersectionDefinition>().ToList();
                var byId = now.ToLookup(r => r.MarkingId);
                foreach (var it in RoadSpacing.FollowNodes(its, after, movedRoadIds, delta))
                    foreach (var r in byId[it.Id]) MarkingStorage.Write(r.Element, it, r.Color);
            });
            t.Commit();
        }
        tg.Assimilate();
        return results;
    }

    /// <summary>Via do plugin pelo clique (nula = cancelado).</summary>
    public static IntersectionRoad? Pick(UIDocument uidoc, string prompt, out Vec2 at)
    {
        at = default;
        var p = Picking.PickPoint(uidoc, prompt);
        if (p == null) return null;
        at = UnitConv.ToVec2(p);
        return RoadExtensionCommand.RoadAt(uidoc, at) ?? throw new UserMessageException("Nenhuma via do plugin junto ao ponto clicado.");
    }

    /// <summary>Pré-visualização: as duas vias (antes tracejado nos eixos, depois em contorno) e a linha da medida.</summary>
    public static FormPreview Preview(IntersectionRoad a, IntersectionRoad b, Vec2 delta, DistanciaVias? d, string info)
    {
        static IEnumerable<IReadOnlyList<Vec2>> Edges(IntersectionRoad r, Vec2 shift)
        {
            var axis = new Polyline2(r.Axis.Points.Select(p => p + shift));
            yield return axis.Points;
            foreach (var w in new[] { r.Def.LeftWidth, -r.Def.RightWidth, r.Def.TotalLeft, -r.Def.TotalRight })
                if (Math.Abs(w) > 0.01) yield return axis.Offset(w).Points;
        }
        var pav = new List<Polygon2>();
        foreach (var (r, s) in new[] { (a, Vec2.Zero), (b, delta) })
        {
            var axis = new Polyline2(r.Axis.Points.Select(p => p + s));
            var left = axis.Offset(r.Def.LeftWidth).Points;
            var right = axis.Offset(-r.Def.RightWidth).Points.Reverse();
            pav.Add(new Polygon2(left.Concat(right)));
        }
        var guides = Edges(a, Vec2.Zero).Concat(Edges(b, delta)).ToList();
        guides.Add(b.Axis.Points);   // eixo de B antes de mover
        if (d != null) guides.Add(new[] { d.PontoA, d.PontoB + delta });
        return new FormPreview(null, pav, guides, info);
    }
}

/// <summary>
/// Distância entre vias: escolha a via A (fica) e a via B (move); a janela mostra a distância atual entre eixos, meios-fios e
/// bordos externos e aceita a nova – B se move inteira ao longo da normal de A, com as interseções refeitas.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdDistanciaVias : CommandBase
{
    private static ReferenciaDistancia _modo = ReferenciaDistancia.Eixos;

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var a = RoadMover.Pick(uidoc, "Distância entre vias: clique sobre a via A (a que fica parada) – ESC cancela", out _);
        if (a == null) return Result.Cancelled;
        var b = RoadMover.Pick(uidoc, "Distância entre vias: clique sobre a via B (a que vai se mover), no trecho onde quer medir", out var atB);
        if (b == null) return Result.Cancelled;
        if (b.Def.Id == a.Def.Id || (a.Def.GroupId != null && a.Def.GroupId == b.Def.GroupId))
            throw new UserMessageException("Escolha duas vias diferentes.");
        var d = RoadSpacing.Measure(a, b, atB)
                ?? throw new UserMessageException($"As vias se cruzam ou formam mais de {RoadSpacing.AnguloMaximo:0}° neste trecho – não há distância entre elas para ajustar. Use Mover via.");
        var modo = _modo;
        var nova = Math.Round(d.Valor(modo), 2);
        string Info() => $"Eixos {UiHelpers.F(d.Eixos)} m · meios-fios {UiHelpers.F(d.MeiosFios)} m · bordos {UiHelpers.F(d.Bordos)} m" +
                         (d.AnguloGraus > 0.5 ? $" · eixos a {d.AnguloGraus:0.0}°" : "");
        var w = new FormWindow("Distância entre vias", "Distância entre vias",
                "Medida na seção perpendicular ao eixo de A, no ponto clicado em B. A via B se move inteira (eixo, sinalização, calçadas, " +
                "extensões, piso tátil e elementos sobre ela) e as interseções são refeitas no novo cruzamento.",
                null, () => RoadMover.Preview(a, b, d.Deslocamento(modo, nova), d, Info()), false, "Mover a via B", 900, 560)
            .Section("Distância atual",
                $"Entre eixos: {UiHelpers.F(d.Eixos)} m\nEntre meios-fios (faces): {UiHelpers.F(d.MeiosFios)} m\n" +
                $"Entre bordos externos (limite das calçadas): {UiHelpers.F(d.Bordos)} m" +
                (d.AnguloGraus > 0.5 ? $"\nAs vias não são paralelas ({d.AnguloGraus:0.0}°): a distância vale no ponto clicado." : ""))
            .Section("Nova distância")
            .Choice("Medir entre", new[] { ("Eixos", ReferenciaDistancia.Eixos), ("Meios-fios (faces)", ReferenciaDistancia.MeiosFios),
                    ("Bordos externos (limite das calçadas)", ReferenciaDistancia.Bordos) },
                () => modo, v => { modo = v; })
            .Number("Nova distância (m)", () => nova, v => nova = v, -500, 5000);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        _modo = modo;
        var delta = d.Deslocamento(modo, nova);
        if (modo != ReferenciaDistancia.Eixos && nova < 0)
            throw new UserMessageException("Distância negativa: as vias ficariam sobrepostas.");
        var res = RoadMover.Move(uidoc, b, delta, "SV - Distância entre vias");
        Report("Distância entre vias", res);
        return Result.Succeeded;
    }
}

/// <summary>Mover via: desloca uma via inteira por um vetor digitado (X/Y em metros) ou por dois cliques.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdMoverVia : CommandBase
{
    private static double _dx = 5, _dy;

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var road = RoadMover.Pick(uidoc, "Mover via: clique sobre a via a mover – ESC cancela", out _);
        if (road == null) return Result.Cancelled;
        double dx = _dx, dy = _dy;
        var byClicks = false;
        var w = new FormWindow("Mover via", "Mover via",
                "A via se move inteira (eixo, sinalização, calçadas, extensões, piso tátil e elementos sobre ela) numa única operação " +
                "(Ctrl+Z desfaz tudo). As vias ligadas a ela acompanham e as interseções são refeitas.",
                null, () => RoadMover.Preview(road, road, new Vec2(dx, dy), null, $"Deslocamento {UiHelpers.F(Math.Sqrt(dx * dx + dy * dy))} m"),
                false, "Mover", 860, 520)
            .Section("Deslocamento")
            .Number("ΔX (m, leste +)", () => dx, v => dx = v, -10000, 10000)
            .Number("ΔY (m, norte +)", () => dy, v => dy = v, -10000, 10000)
            .Check("Indicar por dois cliques na planta (de / para)", () => byClicks, v => byClicks = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        if (byClicks)
        {
            var p0 = Picking.PickPoint(uidoc, "Mover via: clique o ponto DE origem");
            if (p0 == null) return Result.Cancelled;
            var p1 = Picking.PickPoint(uidoc, "Mover via: clique o ponto PARA onde ele vai");
            if (p1 == null) return Result.Cancelled;
            var v = UnitConv.ToVec2(p1) - UnitConv.ToVec2(p0);
            (dx, dy) = (v.X, v.Y);
        }
        (_dx, _dy) = (dx, dy);
        var res = RoadMover.Move(uidoc, road, new Vec2(dx, dy), "SV - Mover via");
        Report("Mover via", res);
        return Result.Succeeded;
    }
}
