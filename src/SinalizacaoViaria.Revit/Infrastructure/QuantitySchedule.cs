using System.IO;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Tabela nativa do Revit com o mesmo layout da janela de quantitativos: blocos por categoria e subcategoria
/// (cabeçalho e subtotal), colunas Imagem · Código · Descrição · Quantidade · Un. · Área · Extensão · Unid. ·
/// Hierarquia · Material, total geral e título em faixa azul.
/// </summary>
/// <remarks>
/// O Revit aceita no máximo 4 campos de classificação/agrupamento – a tabela usa Categoria → Subcategoria → Código → Cor
/// (a cor é oculta: só separa as linhas da pintura por cor, como na janela). A imagem aparece quando a tabela é
/// colocada numa folha (limitação do Revit: a vista de tabela mostra só o nome da imagem).
/// </remarks>
public static class QuantitySchedule
{
    public const string ScheduleName = "SV - Quantitativos de sinalização";
    private const string DetailCategory = "Detalhamento";

    private static readonly Color Blue = new(31, 95, 168);
    private static readonly Color LightBlue = new(232, 239, 248);
    private static readonly Color DarkBlue = new(23, 74, 131);
    private static readonly Color White = new(255, 255, 255);

    private static double Mm(double mm) => mm / 304.8;

    /// <summary>
    /// Completa os parâmetros das colunas "Quantidade" e "Un." nos elementos já existentes (projetos anteriores),
    /// tira cor/material dos itens contados por modelo e associa a miniatura de cada item ao parâmetro "Imagem".
    /// Deve ser chamado dentro de uma transação.
    /// </summary>
    public static (int Elements, int Images) Prepare(Document doc, IReadOnlyList<QuantityRow> rows, IReadOnlyDictionary<string, BitmapSource>? thumbs)
    {
        SharedParameters.Ensure(doc);
        var units = rows.GroupBy(r => r.Code.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Unit);
        var memorialLabels = Enum.GetValues<CategoriaQuantitativo>().Where(QuantityRow.IsMemorialCategory).Select(QuantityRow.CategoryLabel).ToHashSet();
        var colorByName = MarkingColors.All.ToDictionary(StyleService.ColorName, c => c, StringComparer.OrdinalIgnoreCase);
        var images = new Dictionary<string, ElementId>();
        int count = 0, withImage = 0;

        IEnumerable<Element> elements;
        try
        {
            elements = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .WherePasses(new ElementParameterFilter(new SharedParameterApplicableRule(SharedParameters.Codigo.Name))).ToElements();
        }
        catch (Exception ex)
        {
            Log.Error("Tabela: coleta dos elementos", ex);
            return (0, 0);
        }

        foreach (var e in elements)
        {
            try
            {
                var code = e.get_Parameter(SharedParameters.Codigo.Guid)?.AsString();
                if (string.IsNullOrWhiteSpace(code)) continue;
                var category = e.get_Parameter(SharedParameters.Categoria.Guid)?.AsString() ?? "";
                if (category == DetailCategory) continue;
                count++;
                var memorial = memorialLabels.Contains(category);
                if (memorial)
                {
                    SharedParameters.Set(e, SharedParameters.Cor, "");
                    SharedParameters.Set(e, SharedParameters.Material, "");
                }
                // Unidade e quantidade de medição (a mesma regra da janela: m² → área, m → extensão, un → unidades).
                var area = UnitConv.M2(e.get_Parameter(SharedParameters.Area.Guid)?.AsDouble() ?? 0);
                var length = UnitConv.M(e.get_Parameter(SharedParameters.Extensao.Guid)?.AsDouble() ?? 0);
                var qty = e.get_Parameter(SharedParameters.Quantidade.Guid)?.AsInteger() ?? 0;
                var unit = units.TryGetValue(code.Trim().ToUpperInvariant(), out var u) && !string.IsNullOrEmpty(u) ? u
                    : e.get_Parameter(SharedParameters.Unidade.Guid)?.AsString() is { Length: > 0 } cur ? cur
                    : length > 1e-6 ? "m" : area > 1e-6 && qty == 0 ? "m²" : "un";
                SharedParameters.Set(e, SharedParameters.Unidade, unit);
                SharedParameters.Set(e, SharedParameters.QtdMedicao, unit switch { "m²" => area, "m" => length, _ => (double)qty });

                // Miniatura da própria sinalização no parâmetro "Imagem" do elemento.
                if (thumbs == null || thumbs.Count == 0) continue;
                var colorName = e.get_Parameter(SharedParameters.Cor.Guid)?.AsString() ?? "";
                var key = colorByName.TryGetValue(colorName, out var color) ? QuantityThumbnails.Key(code, color) : code;
                if (!thumbs.ContainsKey(key)) key = code;
                if (!thumbs.TryGetValue(key, out var bmp)) continue;
                if (!images.TryGetValue(key, out var imageId))
                {
                    imageId = ImageFor(doc, key, bmp);
                    images[key] = imageId;
                }
                if (imageId == ElementId.InvalidElementId) continue;
                var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_IMAGE);
                if (p != null && !p.IsReadOnly && p.Set(imageId)) withImage++;
            }
            catch (Exception ex)
            {
                Log.Error($"Tabela: elemento {e.Id}", ex);
            }
        }
        return (count, withImage);
    }

    /// <summary>Tipo de imagem do Revit com a miniatura (reaproveita/recarrega o existente com o mesmo nome).</summary>
    private static ElementId ImageFor(Document doc, string key, BitmapSource bmp)
    {
        try
        {
            var dir = Path.Combine(PluginPaths.Root, "miniaturas");
            Directory.CreateDirectory(dir);
            var safe = new string(key.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
            var file = Path.Combine(dir, $"SV_{safe}.png");
            File.WriteAllBytes(file, GeometryPreview.Png(bmp));
            var name = Path.GetFileName(file);
            var existing = new FilteredElementCollector(doc).OfClass(typeof(ImageType)).Cast<ImageType>()
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                try { existing.ReloadFrom(new ImageTypeOptions(file, false, ImageTypeSource.Import)); }
                catch (Exception ex) { Log.Error("Recarregar imagem " + name, ex); }
                return existing.Id;
            }
            return ImageType.Create(doc, new ImageTypeOptions(file, false, ImageTypeSource.Import)).Id;
        }
        catch (Exception ex)
        {
            Log.Error("Imagem da tabela " + key, ex);
            return ElementId.InvalidElementId;
        }
    }

    /// <summary>Cria a tabela (geral ou de uma categoria). Deve ser chamado dentro de uma transação.</summary>
    public static ViewSchedule Create(Document doc, CategoriaQuantitativo? category, List<string> notes)
    {
        if (SharedParameterElement.Lookup(doc, SharedParameters.Codigo.Guid) == null)
            throw new InvalidOperationException("os parâmetros SV_* não existem no projeto (crie ou atualize alguma sinalização e tente de novo).");

        // Multicategoria: formas diretas (modelos genéricos), pisos e famílias do usuário juntos.
        var sched = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
        var name = category is { } cc ? $"SV - {QuantityRow.CategoryLabel(cc)}" : ScheduleName;
        var baseName = name;
        var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Select(v => v.Name).ToHashSet();
        for (int i = 2; existing.Contains(name); i++) name = $"{baseName} ({i})";
        sched.Name = name;

        var def = sched.Definition;
        var fields = def.GetSchedulableFields();

        ScheduleField? AddField(SchedulableField? sf, string heading, double widthMm, bool total = false,
            ScheduleHorizontalAlignment align = ScheduleHorizontalAlignment.Left, ForgeTypeId? unit = null, double accuracy = 0.01)
        {
            if (sf == null) return null;
            try
            {
                var f = def.AddField(sf);
                Try(() => f.ColumnHeading = heading);
                Try(() => f.GridColumnWidth = Mm(widthMm));
                Try(() => f.HorizontalAlignment = align);
                if (total) Try(() => f.DisplayType = ScheduleFieldDisplayType.Totals);
                if (unit != null)
                    Try(() => f.SetFormatOptions(new FormatOptions(unit) { UseDefault = false, Accuracy = accuracy }));
                return f;
            }
            catch (Exception ex)
            {
                notes.Add($"coluna \"{heading}\" não adicionada ({ex.Message})");
                Log.Error($"Campo {heading} da tabela", ex);
                return null;
            }
        }
        SchedulableField? Shared(SharedParameters.Def d) =>
            SharedParameterElement.Lookup(doc, d.Guid) is { } spe ? fields.FirstOrDefault(f => f.ParameterId == spe.Id) : null;

        var right = ScheduleHorizontalAlignment.Right;
        var center = ScheduleHorizontalAlignment.Center;
        var imageField = fields.FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_IMAGE));
        var categoria = AddField(Shared(SharedParameters.Categoria), "Categoria", 40);
        var grupo = AddField(Shared(SharedParameters.Grupo), "Subcategoria", 40);
        var cor = AddField(Shared(SharedParameters.Cor), "Cor", 18);
        var imagem = AddField(imageField, "Imagem", 24, align: center);
        if (imagem == null && imageField == null) notes.Add("a coluna Imagem não está disponível nesta tabela do Revit");
        var codigo = AddField(Shared(SharedParameters.Codigo), "Código", 20)
                     ?? throw new InvalidOperationException("o campo SV_Codigo não está disponível para tabelas multicategoria.");
        AddField(Shared(SharedParameters.Descricao), "Descrição", 70);
        AddField(Shared(SharedParameters.QtdMedicao), "Quantidade", 22, true, right, UnitTypeId.General);
        AddField(Shared(SharedParameters.Unidade), "Un.", 10, align: center);
        AddField(Shared(SharedParameters.Area), "Área (m²)", 22, true, right, UnitTypeId.SquareMeters);
        AddField(Shared(SharedParameters.Extensao), "Extensão (m)", 22, true, right, UnitTypeId.Meters);
        AddField(Shared(SharedParameters.Quantidade), "Unid.", 14, true, right);
        AddField(Shared(SharedParameters.Hierarquia), "Hierarquia", 28);
        AddField(Shared(SharedParameters.Material), "Material", 36);

        // Categoria e subcategoria viram cabeçalhos de bloco (ocultas como colunas); a cor só separa linhas.
        if (grupo != null) Try(() => grupo.IsHidden = true);
        if (cor != null) Try(() => cor.IsHidden = true);
        if (categoria != null) Try(() => categoria.IsHidden = true);

        Try(() => def.AddFilter(new ScheduleFilter(codigo.FieldId, ScheduleFilterType.HasValue)), "filtro de código");
        if (categoria != null)
        {
            Try(() => def.AddFilter(new ScheduleFilter(categoria.FieldId, ScheduleFilterType.NotEqual, DetailCategory)), "filtro de detalhamento");
            if (category is { } c)
                def.AddFilter(new ScheduleFilter(categoria.FieldId, ScheduleFilterType.Equal, QuantityRow.CategoryLabel(c)));
        }

        // No máximo 4 níveis de classificação/agrupamento (limite do Revit).
        var sorts = new List<ScheduleSortGroupField>();
        if (categoria != null && category == null)
            sorts.Add(new ScheduleSortGroupField(categoria.FieldId) { ShowHeader = true, ShowFooter = true, ShowFooterTitle = true, ShowBlankLine = true });
        if (grupo != null)
            sorts.Add(new ScheduleSortGroupField(grupo.FieldId) { ShowHeader = true, ShowFooter = category != null, ShowFooterTitle = true });
        sorts.Add(new ScheduleSortGroupField(codigo.FieldId));
        if (cor != null) sorts.Add(new ScheduleSortGroupField(cor.FieldId));
        foreach (var s in sorts.Take(4))
            Try(() => def.AddSortGroupField(s), "classificação");

        def.IsItemized = false;
        Try(() => def.ShowGridLines = true);
        Try(() => def.ShowTitle = true);
        Try(() => def.ShowHeaders = true);
        def.ShowGrandTotal = true;
        def.ShowGrandTotalCount = false;
        def.ShowGrandTotalTitle = true;
        Try(() => def.GrandTotalTitle = "TOTAL GERAL");

        doc.Regenerate();
        StyleHeader(sched, category, notes);
        return sched;

        void Try(Action a, string? what = null)
        {
            try { a(); }
            catch (Exception ex)
            {
                if (what != null) notes.Add($"{what}: {ex.Message}");
                Log.Error("Tabela " + (what ?? "formatação"), ex);
            }
        }
    }

    /// <summary>Título em faixa azul (como a janela) e cabeçalhos das colunas em azul claro, em negrito.</summary>
    private static void StyleHeader(ViewSchedule sched, CategoriaQuantitativo? category, List<string> notes)
    {
        try
        {
            var header = sched.GetTableData().GetSectionData(SectionType.Header);
            if (header == null || header.NumberOfRows == 0) return;
            var title = category is { } c ? $"QUANTITATIVO – {QuantityRow.CategoryLabel(c).ToUpperInvariant()}" : "QUANTITATIVO DE SINALIZAÇÃO VIÁRIA E URBANIZAÇÃO";
            try { header.SetCellText(header.FirstRowNumber, header.FirstColumnNumber, title); } catch { /* título padrão */ }
            for (int r = header.FirstRowNumber; r <= header.LastRowNumber; r++)
            {
                var isTitle = r == header.FirstRowNumber;
                for (int col = header.FirstColumnNumber; col <= header.LastColumnNumber; col++)
                {
                    try
                    {
                        var st = header.GetTableCellStyle(r, col);
                        st.BackgroundColor = isTitle ? Blue : LightBlue;
                        st.TextColor = isTitle ? White : DarkBlue;
                        st.IsFontBold = true;
                        if (isTitle) st.TextSize = 11;
                        var o = st.GetCellStyleOverrideOptions();
                        o.BackgroundColor = true;
                        o.FontColor = true;
                        o.Bold = true;
                        if (isTitle) o.FontSize = true;
                        st.SetCellStyleOverrideOptions(o);
                        header.SetCellStyle(r, col, st);
                    }
                    catch
                    {
                        // Células mescladas (título): só a primeira aceita estilo.
                    }
                }
                if (isTitle) Try(() => header.SetRowHeight(r, Mm(9)));
            }
        }
        catch (Exception ex)
        {
            notes.Add("formatação do cabeçalho: " + ex.Message);
            Log.Error("Tabela: cabeçalho", ex);
        }

        static void Try(Action a) { try { a(); } catch { /* opcional */ } }
    }

    /// <summary>Folha com a tabela (as imagens só aparecem em folhas). Deve ser chamado dentro de uma transação.</summary>
    public static ViewSheet? PlaceOnSheet(Document doc, ViewSchedule sched, List<string> notes)
    {
        try
        {
            var titleBlock = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().FirstElementId();
            var sheet = ViewSheet.Create(doc, titleBlock ?? ElementId.InvalidElementId);
            var numbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber).ToHashSet();
            var n = 1;
            while (numbers.Contains($"SV-Q{n:00}")) n++;
            sheet.SheetNumber = $"SV-Q{n:00}";
            sheet.Name = "Quantitativos de sinalização";
            ScheduleSheetInstance.Create(doc, sheet.Id, sched.Id, new XYZ(Mm(20), Mm(280), 0));
            return sheet;
        }
        catch (Exception ex)
        {
            notes.Add("folha com a tabela não criada: " + ex.Message);
            Log.Error("Tabela: folha", ex);
            return null;
        }
    }

    /// <summary>Número de linhas do corpo da tabela.</summary>
    public static int Rows(ViewSchedule v)
    {
        try { return Math.Max(0, v.GetTableData().GetSectionData(SectionType.Body).NumberOfRows); }
        catch { return 0; }
    }
}
