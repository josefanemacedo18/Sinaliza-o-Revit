using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Cria/obtém materiais, tipos de região preenchida e estilos de linha usados pelo plugin.</summary>
public sealed class StyleService
{
    public const string Prefix = "SV - ";
    public const string AxisLineStyleName = "SV - Eixo de sinalização";

    private readonly Document _doc;
    private readonly Dictionary<MarkingColor, ElementId> _materials = new();
    private readonly Dictionary<MarkingColor, ElementId> _regionTypes = new();
    private ElementId? _solidFill;

    public StyleService(Document doc) => _doc = doc;

    public static string ColorName(MarkingColor c) => c switch
    {
        MarkingColor.Branca => "Branca",
        MarkingColor.Amarela => "Amarela",
        MarkingColor.Vermelha => "Vermelha",
        MarkingColor.Azul => "Azul",
        MarkingColor.Preta => "Preta",
        MarkingColor.Concreto => "Concreto",
        MarkingColor.Grama => "Grama",
        MarkingColor.Metal => "Metal",
        _ => c.ToString(),
    };

    public static Color RevitColor(MarkingColor c)
    {
        var rgb = MarkingColors.Display(c);
        return new Color(rgb.R, rgb.G, rgb.B);
    }

    public ElementId SolidFillPattern()
    {
        if (_solidFill != null) return _solidFill;
        var fp = new FilteredElementCollector(_doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill && f.GetFillPattern().Target == FillPatternTarget.Drafting);
        _solidFill = fp?.Id ?? ElementId.InvalidElementId;
        return _solidFill;
    }

    /// <summary>Material "SV - Sinalização Branca" etc. com padrão de superfície sólido (cor visível em planta mesmo em linhas ocultas).</summary>
    public ElementId Material(MarkingColor color)
    {
        if (_materials.TryGetValue(color, out var id)) return id;
        var name = MarkingColors.IsPaint(color) ? $"{Prefix}Sinalização {ColorName(color)}" : $"{Prefix}{ColorName(color)}";
        var mat = new FilteredElementCollector(_doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name == name);
        if (mat == null)
        {
            var mid = Autodesk.Revit.DB.Material.Create(_doc, name);
            mat = (Material)_doc.GetElement(mid);
            var c = RevitColor(color);
            mat.Color = c;
            mat.MaterialClass = MarkingColors.IsPaint(color) ? "Pintura" : color == MarkingColor.Grama ? "Vegetação" : color == MarkingColor.Metal ? "Metal" : "Concreto";
            mat.MaterialCategory = MarkingColors.IsPaint(color) ? "Sinalização viária" : "Elementos viários";
            var solid = SolidFillPattern();
            if (solid != ElementId.InvalidElementId)
            {
                mat.SurfaceForegroundPatternId = solid;
                mat.SurfaceForegroundPatternColor = c;
                mat.CutForegroundPatternId = solid;
                mat.CutForegroundPatternColor = c;
            }
        }
        _materials[color] = mat.Id;
        return mat.Id;
    }

    /// <summary>Tipo de região preenchida sólida para cada cor.</summary>
    public ElementId FilledRegionType(MarkingColor color)
    {
        if (_regionTypes.TryGetValue(color, out var id)) return id;
        var name = $"{Prefix}{ColorName(color)}";
        var types = new FilteredElementCollector(_doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        var frt = types.FirstOrDefault(t => t.Name == name);
        if (frt == null)
        {
            var baseType = types.FirstOrDefault() ?? throw new InvalidOperationException("O projeto não possui nenhum tipo de região preenchida para duplicar.");
            frt = (FilledRegionType)baseType.Duplicate(name);
            var solid = SolidFillPattern();
            if (solid != ElementId.InvalidElementId)
            {
                frt.ForegroundPatternId = solid;
                frt.ForegroundPatternColor = RevitColor(color);
                try { frt.BackgroundPatternId = ElementId.InvalidElementId; } catch { /* algumas versões exigem padrão válido */ }
            }
            frt.IsMasking = false;
            try { frt.LineWeight = 1; } catch { /* opcional */ }
        }
        _regionTypes[color] = frt.Id;
        return frt.Id;
    }

    /// <summary>Estilo de linha para eixos/caminhos de referência (tracejado magenta, fácil de ocultar).</summary>
    public GraphicsStyle AxisLineStyle()
    {
        var lines = Category.GetCategory(_doc, BuiltInCategory.OST_Lines);
        Category? sub = null;
        foreach (Category c in lines.SubCategories)
            if (c.Name == AxisLineStyleName) { sub = c; break; }
        if (sub == null)
        {
            sub = _doc.Settings.Categories.NewSubcategory(lines, AxisLineStyleName);
            sub.LineColor = new Color(200, 0, 160);
            sub.SetLineWeight(1, GraphicsStyleType.Projection);
            var dash = new FilteredElementCollector(_doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                .FirstOrDefault(l => l.Name.Contains("Dash", StringComparison.OrdinalIgnoreCase) || l.Name.Contains("Trac", StringComparison.OrdinalIgnoreCase));
            if (dash != null) sub.SetLinePatternId(dash.Id, GraphicsStyleType.Projection);
        }
        return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
    }

    public ElementId? InvisibleLineStyle()
    {
        try
        {
            var cat = Category.GetCategory(_doc, BuiltInCategory.OST_InvisibleLines);
            return cat?.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id;
        }
        catch
        {
            return null;
        }
    }
}
