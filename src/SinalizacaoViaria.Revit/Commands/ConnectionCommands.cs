using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Quantities;
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
        // Uma só ferramenta: tratar a conexão clicada ou todas as interseções do projeto.
        var saved = PluginContext.Settings.Get("conexao:escopo");
        var scope = saved == "todas" ? 1 : saved == "acesso" ? 2 : 0;
        var w0 = new FormWindow("Conexões", "Conexões do sistema viário",
                "Interseção, rotatória ou cul-de-sac. As vias já se conectam sozinhas ao puxar a ponta do eixo até outra via – " +
                "use esta ferramenta para trocar o tipo de uma conexão, ajustar todas as interseções de uma vez ou abrir um acesso " +
                "numa via (retorno em U ou nova via em T).",
                null, null, false, "Continuar", 600, 320)
            .Choice("Aplicar em", new[]
                {
                    ("Uma conexão (clicar no encontro de vias, rotatória ou ponta de via)", 0), ("Todas as interseções do projeto", 1),
                    ("Abrir acesso numa via (retorno em U ou nova via em T)", 2),
                }, () => scope, v => scope = v);
        if (UiHelpers.ShowModal(w0) != true) return Result.Cancelled;
        PluginContext.Settings.Set("conexao:escopo", scope == 1 ? "todas" : scope == 2 ? "acesso" : "uma");
        if (scope == 1) return CmdIntersecao.RunTool(uidoc, true);
        if (scope == 2) return RoadAccessCommand.Run(uidoc);
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
                // Interseção existente: a janela mostra as vias reais e permite ajustar cada esquina e cada ramo.
                IReadOnlyList<IntersectionForms.RoadOption>? roadOptions = null;
                IntersectionForms.RealScene? real = null;
                if (target.Intersection != null)
                {
                    d = (IntersectionDefinition)MarkingDefinition.FromJson(target.Intersection.ToJson())!;
                    (roadOptions, real) = IntersectionForms.ForEdit(uidoc, target.Intersection);
                }
                if (UiHelpers.ShowModal(IntersectionForms.Intersection(d, false, roadOptions, real)) != true) return Result.Cancelled;
                IntersectionForms.RememberDefaults(d);
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
        // Qualquer elemento serve para delimitar a via: marcas do plugin, linhas (eixo/bordas) ou pisos desenhados à mão.
        List<Element> elems;
        var pre = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => e != null && RoadElementFilter.Accept(e!)).Cast<Element>().ToList();
        if (pre.Count > 0) elems = pre;
        else
        {
            try
            {
                elems = uidoc.Selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Element, new RoadElementFilter(),
                        "Selecione os elementos da via – marcas do plugin, linhas (eixo, bordas) ou PISOS – e clique em Concluir")
                    .Select(r => doc.GetElement(r)).Where(e => e != null).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        }
        var all = MarkingStorage.Definitions(doc);
        var picked = elems.Select(MarkingStorage.Read).Where(r => r != null).Cast<StoredMarking>().ToList();
        var lineIds = elems.Where(e => e is CurveElement && !MarkingStorage.IsMarking(e)).Select(e => e.UniqueId).ToHashSet();
        var userFloors = elems.Where(e => e is Floor && !MarkingStorage.IsMarking(e)).ToList();
        // Marcas que usam as linhas/bordas escolhidas como caminho também pertencem à via.
        var loose = all.Where(d => d.Path?.ElementIds.Any(id => lineIds.Contains(PathReference.OwnerOf(id))
                                                             || userFloors.Any(f => f.UniqueId == PathReference.OwnerOf(id))) == true).ToList();
        loose.AddRange(all.Where(d => d.GroupId == null && picked.Any(p => p.MarkingId == d.Id)));
        var groups = picked.Select(p => p.Definition.GroupId).Concat(loose.Select(d => d.GroupId)).Where(g => g != null).Distinct().ToList();
        if (groups.Count == 0 && loose.Count == 0 && userFloors.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Selecione marcas do plugin, linhas de eixo/borda ou pisos da via.");
            return Result.Cancelled;
        }
        var current = (groups.Count > 0 ? all.FirstOrDefault(d => d.GroupId == groups[0])?.Hierarchy : loose.FirstOrDefault()?.Hierarchy)
                      ?? HierarquiaViaria.Local;
        var h = current;
        var speed = false;
        var curPav = all.OfType<RoadPavementDefinition>().FirstOrDefault(p => groups.Count > 0 && p.GroupId == groups[0]);
        string? radiusText = curPav?.CornerRadius is { } cr0 ? UiHelpers.F(cr0) : "";
        var w = new FormWindow("Hierarquia e esquinas", "Hierarquia viária (CTB art. 60) e raio das esquinas",
                $"{groups.Count} via(s) selecionada(s). A hierarquia é gravada em todos os elementos da via (parâmetro SV_Hierarquia) e usada nos " +
                "quantitativos e na escolha da via preferencial das interseções.", null, null, false, "Aplicar", 560, 320)
            .Choice("Hierarquia", Hierarquia.Definidas.Select(x => (Hierarquia.Label(x), x)), () => h, v => h = v)
            .Check("Ajustar a velocidade das linhas à hierarquia (CTB art. 61)", () => speed, v => speed = v)
            .Text("Raio das esquinas (m, vazio = pela hierarquia)", () => radiusText, v => radiusText = v,
                tooltip: "Raio de concordância na face do meio-fio nas interseções destas vias (aplicado também às interseções existentes).");
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        double? radius = UiHelpers.ParseOpt(radiusText ?? "") is { } rv && rv >= 0 ? rv : null;

        var defs = all.Where(d => d.GroupId != null && groups.Contains(d.GroupId)).ToList();
        defs.AddRange(loose.Where(l => defs.All(d => d.Id != l.Id)));
        if (userFloors.Count > 0)
        {
            // Pisos desenhados à mão: recebem a hierarquia (e entram nos quantitativos por hierarquia).
            using var t = new Transaction(doc, "SV - Hierarquia dos pisos");
            t.Start();
            SharedParameters.Ensure(doc);
            foreach (var f in userFloors)
            {
                SharedParameters.Set(f, SharedParameters.Hierarquia, Hierarquia.Label(h));
                SharedParameters.Set(f, SharedParameters.Categoria, QuantityRow.CategoryLabel(CategoriaQuantitativo.PavimentacaoGeometria));
                SharedParameters.Set(f, SharedParameters.Codigo, "PAV-PISO");
                SharedParameters.Set(f, SharedParameters.Descricao, "Pavimento (piso do projeto)");
                SharedParameters.Set(f, SharedParameters.Area, f.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0);
            }
            t.Commit();
        }
        foreach (var d in defs)
        {
            d.Hierarchy = h;
            if (d is RoadPavementDefinition pv) pv.CornerRadius = radius;
            if (speed && d is LinearMarkingDefinition { Speed: not null } l) l.Speed = Hierarquia.DefaultSpeed(h);
        }
        var results = MarkingCreator.Commit(uidoc, defs, "SV - Hierarquia viária");
        // Interseções, rotatórias e cul-de-sacs das vias: via preferencial e hierarquia da sinalização.
        var pavIds = defs.OfType<RoadPavementDefinition>().Select(p => p.Id).ToHashSet();
        results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões", s =>
        {
            var r = new List<RenderResult>();
            var pavs = MarkingStorage.Definitions(doc).OfType<RoadPavementDefinition>().ToDictionary(p => p.Id);
            foreach (var it in s.DependentOn(defs))
            {
                it.CornerRadius = Hierarquia.NodeRadius(it.RoadIds.Select(id => pavs.GetValueOrDefault(id)).Where(p => p != null)!);
                r.AddRange(s.Refresh(it));
            }
            foreach (var rb in s.RoundaboutsDependentOn(defs)) r.AddRange(s.Refresh(rb));
            foreach (var c in s.CulDeSacsDependentOn(defs)) r.AddRange(s.Refresh(c));
            return r;
        }).Where(r => r.Warnings.Count > 0));
        Report("Hierarquia viária", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }
}

/// <summary>Seleção para delimitar uma via: marcas do plugin, linhas e pisos.</summary>
internal sealed class RoadElementFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
{
    public static bool Accept(Element e) => e is Floor || e is CurveElement || MarkingStorage.IsMarking(e);
    public bool AllowElement(Element elem) => Accept(elem);
    public bool AllowReference(Reference reference, XYZ position) => false;
}

/// <summary>
/// Abrir acesso numa via existente: retorno em U (abertura no canteiro, bolsão, alargamento ou rotatória) com o raio do
/// veículo de projeto, ou nova via menor saindo em T do ponto clicado, já ligada por interseção. Tudo numa só operação
/// (um Desfazer) e editável depois pelo Editar sobre a via.
/// </summary>
internal static class RoadAccessCommand
{
    private enum Acesso { Retorno, NovaVia }
    private enum FormaRetorno { Abertura, Bolsao, Alargamento, Rotatoria }

    private static readonly (string, VeiculoProjeto)[] Vehicles =
        Enum.GetValues<VeiculoProjeto>().Select(v => (RetornoVia.Rotulo(v), v)).ToArray();

    public static Result Run(UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var pick = Picking.PickPoint(uidoc, "Abrir acesso: clique na via, no ponto do acesso (do lado de quem vai retornar ou do lado da nova via) – ESC cancela");
        if (pick == null) return Result.Cancelled;
        var pt = UnitConv.ToVec2(pick);
        var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        var road = svc.Roads().Select(r => (R: r, Pr: r.Axis.Project(pt)))
            .Where(x => Math.Abs(x.Pr.Signed) <= Math.Max(x.R.Def.TotalLeft, x.R.Def.TotalRight) + 2)
            .OrderBy(x => Math.Abs(x.Pr.Signed)).Select(x => (IntersectionRoad?)x.R).FirstOrDefault()
            ?? throw new UserMessageException("Nenhuma via do plugin no ponto clicado. Clique sobre a pista ou a calçada da via.");
        var pav = road.Def;
        var (st, signed) = road.Axis.Project(pt);

        var kind = PluginContext.Settings.Get("acesso:tipo") == "via" ? Acesso.NovaVia : Acesso.Retorno;
        var r = new RetornoVia { Estaca = Math.Round(st, 1), SentidoDoEixo = signed < 0 };
        var forma = FormaRetorno.Abertura;
        var raioExt = 0.0;
        var raioInt = 0.0;
        var espera = 0.0;
        var tplIdx = 0;
        var angle = 90.0;
        var length = 40.0;
        var radius = pav.CornerRadius ?? 6.0;
        var w = new FormWindow("Abrir acesso na via", "Retorno em U ou nova via em T",
                $"Via clicada: pista de {UiHelpers.F(pav.LeftWidth + pav.RightWidth, "0.00")} m{(pav.Gaps.Any(g => g.Median) ? ", canteiro central" : "")}, estaca {UiHelpers.F(st, "0.0")} m. " +
                "Retorno: o veículo de projeto (DNIT) define o raio de giro; o canteiro abre onde a faixa varrida passa e a pista oposta é alargada " +
                "se o giro não couber. Nova via: sai do ponto clicado, para o lado do clique, e é ligada por interseção.",
                null, null, false, "Criar", 700, 640)
            .Choice("Acesso", new[] { ("Retorno em U", Acesso.Retorno), ("Nova via menor (T)", Acesso.NovaVia) }, () => kind, v => kind = v)
            .Section("Retorno em U")
            .Choice("Forma", new[]
                {
                    ("Abertura no canteiro central (alarga a pista oposta se precisar)", FormaRetorno.Abertura),
                    ("Bolsão de espera (no canteiro largo ou lateral, à direita)", FormaRetorno.Bolsao),
                    ("Alargamento da pista oposta (sem canteiro)", FormaRetorno.Alargamento),
                    ("Rotatória no ponto (retorno pela rotatória)", FormaRetorno.Rotatoria),
                }, () => forma, v => forma = v)
            .Choice("Veículo de projeto", Vehicles, () => r.Veiculo, v => r.Veiculo = v)
            .Number("Raio externo (m) – 0 = o do veículo", () => raioExt, v => raioExt = v, 0, 40)
            .Number("Raio interno (m) – 0 = o do veículo", () => raioInt, v => raioInt = v, 0, 40)
            .Check("Quem retorna segue no sentido do eixo (pista da direita do eixo)", () => r.SentidoDoEixo, v => r.SentidoDoEixo = v,
                "Automático pelo lado clicado. Desmarque para o retorno de quem vem no sentido contrário.")
            .Number("Comprimento de espera do bolsão (m) – 0 = 2 veículos", () => espera, v => espera = v, 0, 200)
            .Check("Sinalização (PEM-RE, R-3, R-5a, R-6a, LMS no bolsão)", () => r.Sinalizacao, v => r.Sinalizacao = v)
            .Section("Nova via em T")
            .Choice("Seção da nova via", RoadTemplates.All.Select((t, i) => (t.Name, i)).ToList(), () => tplIdx, v => tplIdx = v)
            .Number("Ângulo com a via existente (°)", () => angle, v => angle = Math.Clamp(v, 30, 150), 30, 150, "0",
                "90° = perpendicular. Medido a partir do sentido do eixo existente, para o lado do clique.")
            .Number("Comprimento da nova via (m)", () => length, v => length = Math.Max(10, v), 10, 2000, "0.0")
            .Number("Raio das esquinas (m)", () => radius, v => radius = Math.Max(0, v), 0, 60);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        PluginContext.Settings.Set("acesso:tipo", kind == Acesso.NovaVia ? "via" : "retorno");
        PluginContext.SaveSettings();

        var results = new List<RenderResult>();
        using var tg = new TransactionGroup(doc, kind == Acesso.Retorno ? "SV - Abrir acesso: retorno" : "SV - Abrir acesso: nova via");
        tg.Start();
        if (kind == Acesso.Retorno && forma == FormaRetorno.Rotatoria)
        {
            var rb = (RoundaboutDefinition)(UiHelpers.Remembered<RoundaboutDefinition>("Rotatoria") ?? new RoundaboutDefinition()).CloneWithNewId();
            rb.ChildIds.Clear();
            var center = road.Axis.PointAt(st);
            results.AddRange(IntersectionRunner.Run(uidoc, "SV - Retorno por rotatória", s => s.AddRoundabout(center, s.RoadZ(road, center), rb, out _)));
        }
        else if (kind == Acesso.Retorno)
        {
            var setup = RoadTemplates.FromJson(pav.SetupJson)
                        ?? throw new UserMessageException("Esta via não guardou a seção transversal (versão anterior ou Pista): use Editar → A via inteira uma vez e depois abra o acesso.");
            r.Tipo = forma switch { FormaRetorno.Bolsao => TipoRetorno.Bolsao, FormaRetorno.Alargamento => TipoRetorno.Alargamento, _ => TipoRetorno.AberturaCanteiro };
            r.RaioExterno = raioExt > 0.5 ? raioExt : null;
            r.RaioInterno = raioInt > 0.5 ? raioInt : null;
            r.Espera = espera > 0.5 ? espera : null;
            setup.Retornos.Add(r);
            results.AddRange(Regenerate(uidoc, pav, setup));
        }
        else
        {
            var setup = RoadTemplates.All[Math.Clamp(tplIdx, 0, RoadTemplates.All.Count - 1)].Create();
            setup.CornerRadius = radius;
            var pts = AcessoVia.BranchAxis(road.Axis, pt, angle, length);
            var zFt = UnitConv.Ft(pav.Path?.Z ?? 0);
            List<ElementId> ids;
            using (var t = new Transaction(doc, "SV - Eixo da nova via"))
            {
                t.Start();
                ids = Picking.CreateAxis(doc, uidoc.ActiveView, RoadConnection.Fillet(pts, 0), zFt);
                t.Commit();
            }
            var path = PathReference.FromElements(ids.Select(id => doc.GetElement(id).UniqueId));
            path.Z = pav.Path?.Z ?? 0;
            var (_, created) = RoadWorks.CreateRoad(uidoc, setup, path, null, true, "Nova via");
            results.AddRange(created);
        }
        tg.Assimilate();
        CommandBase.ReportResults(kind == Acesso.Retorno ? "Retorno" : "Nova via", results);
        return Result.Succeeded;
    }

    /// <summary>Regenera a via com a seção nova (retornos) e as conexões dela.</summary>
    public static List<RenderResult> Regenerate(UIDocument uidoc, RoadPavementDefinition pav, RoadSetup setup)
    {
        var doc = uidoc.Document;
        var members = MarkingStorage.Definitions(doc).Where(d => d.GroupId == pav.GroupId).ToList();
        var axis = PathResolver.Resolve(doc, pav.PathRef)?.Main;
        return RoadRegen.Regenerate(uidoc, pav, members, setup, pav.Output.Clone(), axis);
    }

    /// <summary>Editar os retornos de uma via (Editar sobre a via, a seta ou as placas do retorno): alterar ou remover.</summary>
    public static Result EditRetornos(UIDocument uidoc, RoadPavementDefinition pav, Vec2? near = null)
    {
        var doc = uidoc.Document;
        var setup = RoadTemplates.FromJson(pav.SetupJson) ?? throw new UserMessageException("A via não guardou a seção transversal.");
        if (setup.Retornos.Count == 0) throw new UserMessageException("Esta via não tem retornos.");
        var axis = PathResolver.Resolve(doc, pav.PathRef)?.Main;
        var idx = 0;
        if (near is { } p && axis != null)
        {
            var s0 = axis.Project(p).Station;
            idx = setup.Retornos.Select((x, i) => (D: Math.Abs(x.Estaca - s0), i)).OrderBy(x => x.D).First().i;
        }
        var work = setup.Retornos.Select(x => x.Clone()).ToList();
        var remove = new bool[work.Count];
        RetornoVia Cur() => work[Math.Clamp(idx, 0, work.Count - 1)];
        var fw = new FormWindow("Retornos da via", "Retornos em U desta via",
                "Escolha o retorno e altere o que precisar – a via é refeita com a abertura, os alargamentos/bolsões e a sinalização.",
                null, null, false, "Aplicar", 680, 600)
            .Choice("Retorno", work.Select((x, i) => ($"Estaca {UiHelpers.F(x.Estaca, "0.0")} m – {x.Tipo} – {x.Veiculo}", i)).ToList(), () => idx, v => idx = v, preset: v => idx = v)
            .Number("Estaca do início do giro (m)", () => Cur().Estaca, v => Cur().Estaca = v, 0, 100000, "0.0")
            .Choice("Forma", new[] { ("Abertura no canteiro", TipoRetorno.AberturaCanteiro), ("Bolsão de espera", TipoRetorno.Bolsao), ("Alargamento da pista oposta", TipoRetorno.Alargamento) },
                () => Cur().Tipo, v => Cur().Tipo = v)
            .Choice("Veículo de projeto", Vehicles, () => Cur().Veiculo, v => Cur().Veiculo = v)
            .Number("Raio externo (m) – 0 = o do veículo", () => Cur().RaioExterno ?? 0, v => Cur().RaioExterno = v > 0.5 ? v : null, 0, 40)
            .Number("Raio interno (m) – 0 = o do veículo", () => Cur().RaioInterno ?? 0, v => Cur().RaioInterno = v > 0.5 ? v : null, 0, 40)
            .Check("Quem retorna segue no sentido do eixo", () => Cur().SentidoDoEixo, v => Cur().SentidoDoEixo = v)
            .Number("Comprimento de espera (m) – 0 = 2 veículos", () => Cur().Espera ?? 0, v => Cur().Espera = v > 0.5 ? v : null, 0, 200)
            .Check("Sinalização", () => Cur().Sinalizacao, v => Cur().Sinalizacao = v)
            .Check("REMOVER este retorno (a via volta a ser contínua)", () => remove[Math.Clamp(idx, 0, work.Count - 1)], v => remove[Math.Clamp(idx, 0, work.Count - 1)] = v);
        if (UiHelpers.ShowModal(fw) != true) return Result.Cancelled;
        setup.Retornos = work.Where((_, i) => !remove[i]).ToList();
        using var tg = new TransactionGroup(doc, "SV - Editar retornos da via");
        tg.Start();
        var results = Regenerate(uidoc, pav, setup);
        tg.Assimilate();
        CommandBase.ReportResults("Retornos", results);
        return Result.Succeeded;
    }
}
