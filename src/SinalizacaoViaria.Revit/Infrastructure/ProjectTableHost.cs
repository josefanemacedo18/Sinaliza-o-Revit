using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Vista própria da legenda de placas e dos quadros (placas e quantidades) e colocação em folha.
/// </summary>
/// <remarks>
/// O tipo nativo escolhido é a vista de <b>Legenda</b>: não pertence a nenhum nível nem região de corte, pode ser colocada em
/// várias folhas ao mesmo tempo (vistas de desenho e plantas só vão para uma) e aceita linhas, regiões preenchidas e textos –
/// necessários para desenhar a face de cada placa. A API do Revit não cria uma Legenda do zero: a vista é feita duplicando uma
/// Legenda que já exista no projeto. Sem nenhuma, cai para uma vista de desenho (uma folha por vez) e avisa como habilitar.
/// As quantidades também saem como <b>Tabela</b> nativa (comando Quantitativos), que o próprio Revit atualiza.
/// </remarks>
public static class ProjectTableHost
{
    /// <summary>A vista é própria de quadro (Legenda ou desenho), e não uma planta.</summary>
    public static bool IsOwnView(View? v) => v is { IsTemplate: false } && v.ViewType is ViewType.Legend or ViewType.DraftingView;

    /// <summary>
    /// Vista própria do quadro: reaproveita a dele (se já tiver) ou cria uma Legenda nova (duplicando uma existente) – ou uma
    /// vista de desenho, se o projeto não tiver nenhuma Legenda. Grava a vista na definição. Exige transação aberta.
    /// </summary>
    public static View EnsureView(Document doc, MarkingDefinition d, string? name, List<string> notes)
    {
        if (!string.IsNullOrEmpty(d.Output.ViewId) && doc.GetElement(d.Output.ViewId) is View current && IsOwnView(current))
            return current;
        View? view = null;
        var legend = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(v => v.ViewType == ViewType.Legend && !v.IsTemplate && v.CanViewBeDuplicated(ViewDuplicateOption.Duplicate));
        if (legend != null)
        {
            try { view = doc.GetElement(legend.Duplicate(ViewDuplicateOption.Duplicate)) as View; }
            catch (Exception ex) { Log.Error("Duplicar legenda", ex); }
        }
        if (view == null)
        {
            var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting)
                       ?? throw new UserMessageException("O projeto não tem tipo de vista de desenho nem vista de Legenda para receber o quadro.");
            view = ViewDrafting.Create(doc, type.Id);
            notes.Add("O projeto não tem nenhuma vista de Legenda, e a API do Revit só cria uma Legenda duplicando outra – o quadro foi para uma vista de desenho " +
                      "(vai para uma folha por vez). Para usar Legenda (várias folhas): Vista → Legendas → Legenda, crie uma vazia e gere o quadro de novo.");
        }
        view.Name = SheetBuilder.UniqueViewName(doc, string.IsNullOrWhiteSpace(name) ? ProjectTableViews.ViewName(d) : name.Trim());
        try { view.Scale = ProjectTableViews.Scale; } catch (Exception ex) { Log.Error("Escala da vista do quadro", ex); }
        d.Output.Mode = OutputMode.Detalhe2D;
        d.Output.ViewId = view.UniqueId;
        return view;
    }

    /// <summary>Folhas do projeto, por número.</summary>
    public static List<ViewSheet> Sheets(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder && !s.IsTemplate)
            .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Folha nova (primeiro carimbo carregado), com o número e o nome indicados. Exige transação aberta.</summary>
    public static ViewSheet NewSheet(Document doc, string number, string name)
    {
        var titleBlock = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().FirstElementId();
        var sheet = ViewSheet.Create(doc, titleBlock ?? ElementId.InvalidElementId);
        sheet.SheetNumber = SheetBuilder.UniqueSheetNumber(doc, number);
        sheet.Name = name;
        return sheet;
    }

    /// <summary>
    /// Coloca a vista (Legenda, desenho ou Tabela) na folha: no ponto clicado (canto superior esquerdo) ou num lugar livre no
    /// alto à direita. Devolve falso quando não foi possível (a mensagem vai para <paramref name="notes"/>). Exige transação
    /// aberta e a vista com conteúdo (o Revit não coloca vista vazia).
    /// </summary>
    public static bool Place(Document doc, View view, ViewSheet sheet, XYZ? topLeft, List<string> notes)
    {
        try
        {
            if (view is ViewSchedule sched)
            {
                var existing = new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                    .FirstOrDefault(i => i.ScheduleId == sched.Id);
                var inst = existing ?? ScheduleSheetInstance.Create(doc, sheet.Id, sched.Id, topLeft ?? XYZ.Zero);
                doc.Regenerate();
                var bb = inst.get_BoundingBox(sheet);
                if (bb != null)
                {
                    var target = topLeft ?? Spot(doc, sheet, bb.Max - bb.Min, inst.Id, notes);
                    if (target != null) inst.Point += target - new XYZ(bb.Min.X, bb.Max.Y, 0);
                }
                return true;
            }
            var vp = sheet.GetAllViewports().Select(doc.GetElement).OfType<Viewport>().FirstOrDefault(v => v.ViewId == view.Id);
            if (vp == null)
            {
                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                {
                    notes.Add(view.ViewType == ViewType.Legend
                        ? $"\"{view.Name}\" não pôde ir para a folha {sheet.SheetNumber}."
                        : $"\"{view.Name}\" já está em outra folha (vista de desenho vai para uma folha só – use uma vista de Legenda para várias).");
                    return false;
                }
                vp = Viewport.Create(doc, sheet.Id, view.Id, topLeft ?? XYZ.Zero);
            }
            doc.Regenerate();
            var o = vp.GetBoxOutline();
            var at = topLeft ?? Spot(doc, sheet, o.MaximumPoint - o.MinimumPoint, vp.Id, notes);
            if (at != null) vp.SetBoxCenter(vp.GetBoxCenter() + new XYZ(at.X - o.MinimumPoint.X, at.Y - o.MaximumPoint.Y, 0));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Colocar quadro na folha", ex);
            notes.Add($"não foi possível colocar \"{view.Name}\" na folha {sheet.SheetNumber}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Canto superior esquerdo livre na folha (pés) para um quadro do tamanho dado (pés).</summary>
    private static XYZ Spot(Document doc, ViewSheet sheet, XYZ size, ElementId self, List<string> notes)
    {
        static Vec2 M(XYZ p) => new(UnitConv.M(p.X), UnitConv.M(p.Y));
        var tb = SheetBuilder.TitleBlockBox(doc, sheet);
        var margin = new Vec2(0.010, 0.010);
        // Sem carimbo: a área de uma folha A1 a partir da origem.
        var areaMin = tb != null ? M(tb.Min) + margin : Vec2.Zero;
        var areaMax = tb != null ? M(tb.Max) - margin : new Vec2(0.831, 0.584);
        var occupied = new List<(Vec2 Min, Vec2 Max)>();
        foreach (var vp in sheet.GetAllViewports().Where(id => id != self).Select(doc.GetElement).OfType<Viewport>())
        {
            var o = vp.GetBoxOutline();
            occupied.Add((M(o.MinimumPoint), M(o.MaximumPoint)));
        }
        foreach (var si in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                     .Where(i => i.Id != self && !i.IsTitleblockRevisionSchedule))
            if (si.get_BoundingBox(sheet) is { } b) occupied.Add((M(b.Min), M(b.Max)));
        // Reserva aproximada para o carimbo no canto inferior direito (185 × 60 mm) – o quadro pode ser arrastado depois.
        occupied.Add((new Vec2(areaMax.X - 0.185, areaMin.Y), new Vec2(areaMax.X, areaMin.Y + 0.060)));
        var sz = M(size);
        var spot = ProjectTableViews.SheetSpot(areaMin, areaMax, occupied, sz);
        if (spot is not { } at)
        {
            notes.Add($"não há espaço livre na folha {sheet.SheetNumber} para o quadro: ficou no canto superior esquerdo – arraste para o lugar.");
            return new XYZ(UnitConv.Ft(areaMin.X), UnitConv.Ft(areaMax.Y), 0);
        }
        return new XYZ(UnitConv.Ft(at.X), UnitConv.Ft(at.Y + sz.Y), 0);
    }
}
