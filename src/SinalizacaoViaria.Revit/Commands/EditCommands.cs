using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

internal static class MarkingPicker
{
    /// <summary>Marcas da seleção atual ou escolhidas pelo usuário (uma definição por marca).</summary>
    public static List<StoredMarking> PickMany(UIDocument uidoc, string prompt)
    {
        var doc = uidoc.Document;
        var pre = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => e != null)
            .Select(MarkingStorage.Read).Where(r => r != null).Cast<StoredMarking>().ToList();
        if (pre.Count > 0) return pre.GroupBy(r => r.MarkingId).Select(g => g.First()).ToList();
        try
        {
            var refs = uidoc.Selection.PickObjects(ObjectType.Element, new MarkingSelectionFilter(), prompt);
            return refs.Select(r => MarkingStorage.Read(doc.GetElement(r))).Where(r => r != null).Cast<StoredMarking>()
                .GroupBy(r => r.MarkingId).Select(g => g.First()).ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return new();
        }
    }

    public static StoredMarking? PickOne(UIDocument uidoc, string prompt)
    {
        var doc = uidoc.Document;
        var pre = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => e != null)
            .Select(MarkingStorage.Read).Where(r => r != null).Cast<StoredMarking>().GroupBy(r => r.MarkingId).ToList();
        if (pre.Count == 1) return pre[0].First();
        try
        {
            var r = uidoc.Selection.PickObject(ObjectType.Element, new MarkingSelectionFilter(), prompt);
            return MarkingStorage.Read(doc.GetElement(r));
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>Edita os parâmetros de uma marca existente.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdEditar : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var stored = MarkingPicker.PickOne(uidoc, "Selecione a marca a editar");
        if (stored == null) return Result.Cancelled;
        if (stored.Definition is RoundaboutDefinition rb)
        {
            var hasRoads = rb.Legs.Any(l => l.RoadId != null || l.GroupId != null);
            if (UiHelpers.ShowModal(RoundaboutForms.Roundabout(rb, true, hasRoads)) != true) return Result.Cancelled;
            Report("Rotatória", IntersectionRunner.Run(uidoc, "SV - Editar rotatória", s => s.Refresh(rb)).Where(r => r.Warnings.Count > 0).ToList());
            return Result.Succeeded;
        }
        if (stored.Definition is IntersectionDefinition inter)
        {
            var (options, real) = IntersectionForms.ForEdit(uidoc, inter);
            if (UiHelpers.ShowModal(IntersectionForms.Intersection(inter, true, options, real)) != true) return Result.Cancelled;
            Report("Interseção", IntersectionRunner.Run(uidoc, "SV - Editar interseção", s => s.Refresh(inter)).Where(r => r.Warnings.Count > 0).ToList());
            return Result.Succeeded;
        }
        // Elemento de uma via: escolher o que editar (só ele, a via inteira ou o pavimento/raios).
        if (stored.Definition.GroupId is { } gid && stored.Definition is not (IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition))
        {
            var members = MarkingStorage.Definitions(uidoc.Document).Where(d => d.GroupId == gid).ToList();
            var pavement = members.OfType<RoadPavementDefinition>().FirstOrDefault();
            if (pavement != null && members.Count > 1)
            {
                var info = MarkingBuilder.Describe(stored.Definition, PluginContext.Catalog);
                var td = new TaskDialog(AppTitle)
                {
                    MainInstruction = "O que você quer editar?",
                    MainContent = $"Elemento selecionado: {info.Code} – {info.Name}.\nEle faz parte de uma via com {members.Count} elementos (pista, linhas, calçadas, meios-fios...).",
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Só este elemento", "Parâmetros deste elemento: largura, variante, deslocamento, cor...");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "A via inteira (seção transversal)",
                    "Faixas, calçadas, meios-fios, sarjetas, canteiros, ciclofaixas e linhas – adicionar, remover ou alterar. A via é regenerada mantendo o eixo, o pavimento e as conexões.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Pavimento, hierarquia e raios", "Material, espessura, hierarquia viária, raio das esquinas e das curvas do eixo.");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = td.Show();
                if (choice == TaskDialogResult.CommandLink2) return EditWholeRoad(uidoc, pavement, members);
                if (choice == TaskDialogResult.CommandLink3) return EditPavement(uidoc, pavement);
                if (choice != TaskDialogResult.CommandLink1) return Result.Cancelled;
                if (stored.Definition is RoadPavementDefinition) return EditPavement(uidoc, pavement);
            }
        }
        if (stored.Definition is RoadPavementDefinition pv) return EditPavement(uidoc, pv);
        if (stored.Definition is CulDeSacDefinition { RoadId: not null } linked)
        {
            var form0 = SidewalkForms.CulDeSac(linked, true);
            if (form0 == null || UiHelpers.ShowModal(form0) != true) return Result.Cancelled;
            Report("Cul-de-sac", IntersectionRunner.Run(uidoc, "SV - Editar cul-de-sac", s => s.Refresh(linked)).Where(r => r.Warnings.Count > 0).ToList());
            return Result.Succeeded;
        }
        if ((SidewalkForms.ForEdit(stored.Definition) ?? DetailForms.ForEdit(uidoc, stored.Definition)) is { } form)
        {
            if (UiHelpers.ShowModal(form.Window) != true) return Result.Cancelled;
            var def = form.Working;
            def.Id = stored.MarkingId;
            EnsureDetailView(uidoc, def.Output, keepExistingView: true);
            var r = MarkingCreator.Commit(uidoc, new[] { def }, $"SV - Editar {def.DisplayCode}");
            FootprintCutter.ApplyFor(uidoc, def);
            Report("Edição", r);
            return Result.Succeeded;
        }
        MarkingDefinition? edited = stored.Definition switch
        {
            LinearMarkingDefinition l => Show(new LinearWindow("Editar marca linear", Enum.GetValues<GrupoMarca>(), l), w => w.Result),
            HatchMarkingDefinition h => Show(new HatchWindow(h), w => w.Result),
            SymbolMarkingDefinition s => Show(new SymbolWindow(s), w => w.Result),
            TextMarkingDefinition t => Show(new TextWindow(t), w => w.Result),
            ParkingMarkingDefinition p => Show(new ParkingWindow(p), w => w.Result),
            RepeatedMarkingDefinition r => Show(new RepeatedWindow(r), w => w.Result),
            DeviceMarkingDefinition dv => Show(new DeviceWindow(dv), w => w.Result),
            SignDefinition sg => Show(new SignWindow(sg), w => w.Result),
            UrbanElementDefinition ue => Show(new UrbanWindow(ue), w => w.Result),
            RampDefinition rp => Show(new RampWindow(rp), w => w.Result),
            TrafficCalmingDefinition tc => Show(new CalmingWindow(tc), w => w.Result),
            ChannelizationDefinition cz => EditChannelization(cz),
            SignPlanDetailDefinition sp => Show(new SignDetailWindow(DetailScale(uidoc, sp),
                MarkingStorage.ById(uidoc.Document, sp.SignId).FirstOrDefault()?.Definition as SignDefinition, sp), w => w.Result),
            LabelDefinition lb => Show(new LabelWindow(lb), w => w.Result),
            LegendDefinition lg => Show(new LegendWindow(DetailScale(uidoc, lg), MarkingStorage.Definitions(uidoc.Document), lg), w => w.Result),
            _ => null,
        };
        if (edited == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        edited.Id = stored.MarkingId;
        EnsureDetailView(uidoc, edited.Output, keepExistingView: true);
        var results = MarkingCreator.Commit(uidoc, new[] { edited }, $"SV - Editar {edited.DisplayCode}");
        if (edited is RampDefinition ramp) RampCutter.Apply(uidoc, ramp);
        if (edited is LinearMarkingDefinition or TrafficCalmingDefinition or HatchMarkingDefinition or ChannelizationDefinition || edited.Overlay) FootprintCutter.ApplyFor(uidoc, edited);
        Report("Edição", results);
        return Result.Succeeded;
    }


    /// <summary>Pavimento da via: material, espessura, hierarquia e raios (larguras e faixas: editar a via inteira).</summary>
    private Result EditPavement(UIDocument uidoc, RoadPavementDefinition pv)
    {
        // Pavimento da via: material, espessura e raio das esquinas (larguras e faixas: Sinalizar via / Pista).
        var work = (RoadPavementDefinition)MarkingDefinition.FromJson(pv.ToJson())!;
        var thick = work.ActualThickness;
        var radius = work.CornerRadius ?? 0;
        var h = work.Hierarchy ?? HierarquiaViaria.Local;
        var axisR = work.PathRef.SmoothRadius ?? 0;
        var minR = RoadSetup.MinAxisRadius(Math.Max(work.TotalLeft, work.TotalRight));
        var fw = new FormWindow("Editar pavimento da via", "Pavimento da via",
                "Material, espessura, hierarquia e raio das esquinas. As interseções desta via são refeitas com o novo raio.",
                null, null, false, "Aplicar", 600, 380)
            .Choice("Pavimento", new[] { ("Asfalto (CBUQ)", TipoPavimento.Asfalto), ("Bloquete / intertravado", TipoPavimento.Bloquete), ("Concreto", TipoPavimento.Concreto) },
                () => work.Material, v => work.Material = v)
            .Number("Espessura (m)", () => thick, v => thick = v, 0.01, 1)
            .Choice("Hierarquia viária (CTB art. 60)", Hierarquia.Definidas.Select(x => (Hierarquia.Label(x), x)), () => h, v => h = v)
            .Number("Raio das esquinas (m, 0 = pela hierarquia)", () => radius, v => radius = v, 0, 60,
                tooltip: "Raio de concordância na face do meio-fio. Qualquer valor a partir de 0,5 m gera curva.")
            .Number($"Raio das curvas do eixo (m, 0 = cantos como desenhados; mín. {UiHelpers.F(minR, "0.0")})", () => axisR, v => axisR = v, 0, 2000,
                tooltip: "Arredonda os cantos vivos do eixo: pavimento, linhas de bordo, meios-fios e calçadas passam a fazer a curva " +
                         "(a borda interna também). Valores menores que a meia largura + 1,5 m são aumentados.");
        if (UiHelpers.ShowModal(fw) != true) return Result.Cancelled;
        work.Thickness = Math.Abs(thick - work.DefaultThickness) > 1e-6 ? thick : null;
        work.CornerRadius = radius > 0.01 ? radius : null;
        work.Hierarchy = h;
        double? newR = axisR > 0.01 ? Math.Max(axisR, minR) : null;
        var group = new List<MarkingDefinition> { work };
        if (newR != work.PathRef.SmoothRadius && work.GroupId != null)
            group.AddRange(MarkingStorage.Definitions(uidoc.Document).Where(d => d.GroupId == work.GroupId && d.Id != work.Id && d.Path != null
                && d is not IntersectionDefinition));
        foreach (var g in group) g.Path!.SmoothRadius = newR;
        var res = MarkingCreator.Commit(uidoc, group, "SV - Editar pavimento");
        res.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões", s =>
        {
            var r = new List<RenderResult>();
            var pavs = MarkingStorage.Definitions(uidoc.Document).OfType<RoadPavementDefinition>().ToDictionary(p => p.Id);
            foreach (var it in s.DependentOn(new[] { work }))
            {
                it.CornerRadius = Hierarquia.NodeRadius(it.RoadIds.Select(id => pavs.GetValueOrDefault(id)).Where(p => p != null)!);
                r.AddRange(s.Refresh(it));
            }
            foreach (var rb in s.RoundaboutsDependentOn(new[] { work })) r.AddRange(s.Refresh(rb));
            foreach (var c in s.CulDeSacsDependentOn(new[] { work })) r.AddRange(s.Refresh(c));
            return r;
        }));
        Report("Pavimento", res.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }

    /// <summary>
    /// Edita a seção transversal completa de uma via existente: reabre a janela da via com a seção guardada, regenera
    /// todos os elementos do grupo sobre o mesmo eixo (o pavimento mantém o Id, então interseções e rotatórias continuam
    /// ligadas) e refaz as conexões.
    /// </summary>
    private Result EditWholeRoad(UIDocument uidoc, RoadPavementDefinition pav, List<MarkingDefinition> members)
    {
        var doc = uidoc.Document;
        var setup = RoadTemplates.FromJson(pav.SetupJson);
        if (setup == null)
        {
            // Via de versão anterior (sem a seção guardada): reconstrói o básico a partir do pavimento.
            setup = new RoadSetup { TwoWay = pav.TwoWay, Hierarchy = pav.Hierarchy ?? HierarquiaViaria.Local, Pavement = pav.Material, PavementThickness = pav.Thickness, CornerRadius = pav.CornerRadius };
            void Side(List<ElementoSecao> side, double carriage, double sidewalk)
            {
                var n = Math.Max(1, (int)Math.Round(carriage / 3.5));
                for (int i = 0; i < n; i++) side.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = carriage / n });
                if (sidewalk > 0.05) side.Add(new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = sidewalk });
            }
            Side(setup.Right, pav.RightWidth, pav.RightSidewalk);
            Side(setup.Left, pav.LeftWidth, pav.LeftSidewalk);
            TaskDialog.Show(AppTitle, "Esta via foi criada por uma versão anterior e não guardou a seção completa: a janela abre com pista e calçadas " +
                                      "reconstruídas a partir do pavimento. Confira faixas, linhas e elementos antes de aplicar.");
        }
        var w = new RoadWindow(false);
        w.LoadForEdit(setup);
        if (UiHelpers.ShowModal(w) != true || w.Setup == null || w.OutputSettings == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        EnsureDetailView(uidoc, w.OutputSettings, keepExistingView: true);

        var defs = w.Setup.Build(pav.PathRef, w.OutputSettings, PluginContext.Catalog, pav.GroupId, pav.Id);   // Build clona o caminho por marca
        var newPav = defs.OfType<RoadPavementDefinition>().FirstOrDefault();
        if (newPav != null)
        {
            newPav.CornerRadius ??= pav.CornerRadius;
            newPav.Hierarchy ??= pav.Hierarchy;
            newPav.Exclusions.AddRange(pav.Exclusions);
        }
        // Recortes das conexões (interseções, rotatórias, rampas) são reaplicados pelo RefreshDependents.
        var results = new List<RenderResult>();
        using (MarkingService.RenderScope())
        using (var t = new Transaction(doc, "SV - Editar via (remover elementos antigos)"))
        {
            t.Start();
            var service = new MarkingService(doc, uidoc.ActiveView);
            foreach (var m in members.Where(m => m.Id != pav.Id)) service.Delete(m.Id);
            if (newPav == null) service.Delete(pav.Id);
            t.Commit();
        }
        results.AddRange(MarkingCreator.Commit(uidoc, defs, "SV - Editar via"));
        if (w.Setup.Warnings.Count > 0 && results.Count > 0) results[0].Warnings.InsertRange(0, w.Setup.Warnings);
        try
        {
            results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões da via", sv => sv.RefreshDependents(defs)).Where(r => r.Warnings.Count > 0));
        }
        catch (Exception ex)
        {
            Log.Error("Editar via – conexões", ex);
            results.Add(new RenderResult { Geometry = null });
            results[^1].Warnings.Add("As conexões da via não puderam ser refeitas: " + ex.Message);
        }
        Report("Via", results);
        return Result.Succeeded;
    }

    private static double DetailScale(UIDocument uidoc, MarkingDefinition d) =>
        (!string.IsNullOrEmpty(d.Output.ViewId) ? uidoc.Document.GetElement(d.Output.ViewId) as View : null)?.Scale ?? uidoc.ActiveView.Scale;

    private static MarkingDefinition? Show<TW>(TW w, Func<TW, MarkingDefinition?> result) where TW : System.Windows.Window =>
        UiHelpers.ShowModal(w) == true ? result(w) : null;

    private static MarkingDefinition? EditChannelization(ChannelizationDefinition cz)
    {
        var copy = (ChannelizationDefinition)MarkingDefinition.FromJson(cz.ToJson())!;
        return UiHelpers.ShowModal(ManualForms.Channelization(copy, true)) == true ? copy : null;
    }

    private static void EnsureDetailView(UIDocument uidoc, OutputSettings o, bool keepExistingView)
    {
        if (o.Mode != OutputMode.Detalhe2D) return;
        if (keepExistingView && !string.IsNullOrEmpty(o.ViewId) && uidoc.Document.GetElement(o.ViewId) is View) return;
        CommandBase.EnsureDetailViewPublic(uidoc, o);
    }
}

/// <summary>Regenera todas as marcas do projeto.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdAtualizarTodas : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        PluginContext.ReloadCatalog();
        var defs = MarkingStorage.Definitions(doc);
        if (defs.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhuma marca de sinalização encontrada neste projeto.");
            return Result.Cancelled;
        }
        var results = new List<RenderResult>();
        using (MarkingService.RenderScope())
        using (var t = new Transaction(doc, "SV - Atualizar todas"))
        {
            t.Start();
            var service = new MarkingService(doc, uidoc.ActiveView);
            foreach (var d in MarkingService.DependencyOrder(defs.Where(d => d is not (IntersectionDefinition or RoundaboutDefinition)))) results.Add(service.Render(d));
            var inter = new IntersectionService(doc, service);
            foreach (var it in MarkingStorage.Definitions(doc).OfType<IntersectionDefinition>())
            {
                try { results.AddRange(inter.Refresh(it).Where(r => r.Warnings.Count > 0)); }
                catch (Exception ex) { Log.Error("Refresh interseção", ex); }
            }
            foreach (var rb in MarkingStorage.Definitions(doc).OfType<RoundaboutDefinition>())
            {
                try { results.AddRange(inter.Refresh(rb).Where(r => r.Warnings.Count > 0)); }
                catch (Exception ex) { Log.Error("Refresh rotatória", ex); }
            }
            foreach (var cds in MarkingStorage.Definitions(doc).OfType<CulDeSacDefinition>().Where(c => c.RoadId != null).ToList())
            {
                try { results.AddRange(inter.Refresh(cds).Where(r => r.Warnings.Count > 0)); }
                catch (Exception ex) { Log.Error("Refresh cul-de-sac", ex); }
            }
            t.Commit();
        }
        // Marcas sobrepostas (faixas de pedestres, zebrados) e lombadas invertidas refazem os recortes.
        foreach (var d in MarkingStorage.Definitions(doc).Where(d => d.Overlay || d is TrafficCalmingDefinition { Type: TipoModeracao.LombadaInvertida }))
        {
            try { FootprintCutter.ApplyFor(uidoc, d); }
            catch (Exception ex) { Log.Error("Recortes de sobreposição", ex); }
        }
        Report("Atualização", results, alwaysShow: true);
        return Result.Succeeded;
    }
}

/// <summary>Converte marcas entre representação 3D e 2D.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdAlternar2D3D : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var picked = MarkingPicker.PickMany(uidoc, "Selecione as marcas a converter entre 3D e 2D e clique em Concluir");
        if (picked.Count == 0) return Result.Cancelled;
        var defs = picked.Select(p => p.Definition).ToList();
        var to2D = defs.Count(d => d.Output.Mode == OutputMode.Modelo3D) >= defs.Count / 2.0;
        if (to2D && !MarkingService.SupportsDetail(uidoc.ActiveView))
            throw new UserMessageException("Para converter em 2D, abra a vista em planta onde as regiões preenchidas devem ser criadas.");
        foreach (var d in defs)
        {
            d.Output.Mode = to2D ? OutputMode.Detalhe2D : OutputMode.Modelo3D;
            if (to2D) d.Output.ViewId = uidoc.ActiveView.UniqueId;
        }
        var results = MarkingCreator.Commit(uidoc, defs, to2D ? "SV - Converter para 2D" : "SV - Converter para 3D");
        Report(to2D ? "Conversão para 2D" : "Conversão para 3D", results);
        return Result.Succeeded;
    }
}

/// <summary>Seleciona todos os elementos da mesma marca ou do mesmo grupo.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdSelecionarConjunto : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var stored = MarkingPicker.PickOne(uidoc, "Selecione uma marca");
        if (stored == null) return Result.Cancelled;
        var all = MarkingStorage.All(uidoc.Document);
        var selection = all.Where(r => r.MarkingId == stored.MarkingId).ToList();
        if (!string.IsNullOrEmpty(stored.Definition.GroupId))
        {
            var td = new TaskDialog(AppTitle) { MainInstruction = "Selecionar o quê?" };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Somente esta marca");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Todo o grupo (ex.: todas as linhas da via ou da travessia)");
            if (td.Show() == TaskDialogResult.CommandLink2)
                selection = all.Where(r => r.Definition.GroupId == stored.Definition.GroupId).ToList();
        }
        uidoc.Selection.SetElementIds(selection.Select(r => r.Element.Id).ToList());
        return Result.Succeeded;
    }
}
