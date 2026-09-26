using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Revit.UI;

public partial class QuantitiesWindow : Window
{
    private readonly List<QuantityRow> _rows;
    private readonly string _projectName;
    private readonly ListCollectionView _view;

    public bool CreateSchedule { get; private set; }
    public bool SchedulesByCategory { get; private set; }

    private sealed record Option(string Label, CategoriaQuantitativo? Value)
    {
        public override string ToString() => Label;
    }

    private sealed record HOption(string Label, bool All, Core.Definitions.HierarquiaViaria? Value)
    {
        public override string ToString() => Label;
    }

    public QuantitiesWindow(List<QuantityRow> rows, string projectName)
    {
        InitializeComponent();
        _rows = rows;
        _projectName = projectName;
        CbCategory.Items.Add(new Option("Todas as categorias", null));
        foreach (var c in rows.Select(r => r.Category).Distinct().OrderBy(c => c))
            CbCategory.Items.Add(new Option($"{QuantityRow.CategoryLabel(c)} ({rows.Count(r => r.Category == c)})", c));
        CbHierarchy.Items.Add(new HOption("Todas", true, null));
        foreach (var h in rows.Select(r => r.Hierarchy).Distinct().OrderByDescending(Core.Definitions.Hierarquia.Rank))
            CbHierarchy.Items.Add(new HOption($"{Core.Definitions.Hierarquia.Label(h)} ({rows.Count(r => r.Hierarchy == h)})", false, h));
        CbHierarchy.SelectedIndex = 0;
        _view = new ListCollectionView(_rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QuantityRow.CategoryName)));
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QuantityRow.SubcategoryName)));
        TxtProject.Text = projectName;
        _view.Filter = o => Matches((QuantityRow)o);
        GridRows.ItemsSource = _view;
        CbCategory.SelectedIndex = 0;
        Refresh();
    }

    private CategoriaQuantitativo? Category => (CbCategory.SelectedItem as Option)?.Value;

    private bool Matches(QuantityRow r)
    {
        if (Category is { } c && r.Category != c) return false;
        if (CbHierarchy?.SelectedItem is HOption { All: false } h && r.Hierarchy != h.Value) return false;
        var q = TbSearch?.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return r.Code.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.Material.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Color.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.GroupName.Contains(q, StringComparison.OrdinalIgnoreCase) || r.SubcategoryName.Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.FamilyTypes.Contains(q, StringComparison.OrdinalIgnoreCase) || r.HierarchyName.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private List<QuantityRow> Visible => _rows.Where(Matches).ToList();

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        _view.Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var v = Visible;
        GridCategories.ItemsSource = CategoryTree(v);
        GridSummary.ItemsSource = QuantityCalculator.Summary(v);
        GridHierarchy.ItemsSource = QuantityCalculator.HierarchySummary(v);
        var cats = v.Select(r => r.Category).Distinct().Count();
        TxtHeader.Text = $"{v.Count} item(ns) em {cats} categoria(s) e {v.Select(r => (r.Category, r.SubcategoryName)).Distinct().Count()} subcategoria(s)" +
                         (Category is { } c ? $" – filtro: {QuantityRow.CategoryLabel(c)}" : "") + $" – {DateTime.Now:dd/MM/yyyy HH:mm}";
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        Kpis.ItemsSource = new[]
        {
            new Kpi("Elementos no modelo", v.Sum(r => r.Elements).ToString("N0", pt), "un", "#1F5FA8"),
            new Kpi("Área pintada", v.Where(r => MarkingColors.IsPaint(r.Color) && !r.IsFamily).Sum(r => r.Area).ToString("N1", pt), "m²", "#E0A800"),
            new Kpi("Extensão pintada", v.Where(r => r.Category == CategoriaQuantitativo.SinalizacaoHorizontal).Sum(r => r.PaintedLength).ToString("N1", pt), "m", "#F2F2F2"),
            new Kpi("Pavimento", v.Where(r => r.Category == CategoriaQuantitativo.PavimentacaoGeometria && MarkingColors.IsPavement(r.Color)).Sum(r => r.Area).ToString("N1", pt), "m²", "#3E4248"),
            new Kpi("Placas", v.Where(r => r.Category == CategoriaQuantitativo.SinalizacaoVertical).Sum(r => r.Units).ToString("N0", pt), "un", "#C62828"),
            new Kpi("Elementos urbanos", v.Where(r => r.Category == CategoriaQuantitativo.MobiliarioUrbano).Sum(r => r.Units).ToString("N0", pt), "un", "#4E8F3A"),
            new Kpi("Tinta estimada", v.Sum(r => r.MaterialConsumption).ToString("N1", pt), v.FirstOrDefault(r => r.MaterialConsumption > 0)?.ConsumptionUnit ?? "", "#7A5AA6"),
        };
    }

    /// <summary>Resumo em árvore: subtotal da categoria seguido das subcategorias.</summary>
    private static List<QuantityRow> CategoryTree(List<QuantityRow> rows)
    {
        var res = new List<QuantityRow>();
        var subs = QuantityCalculator.SubcategorySummary(rows);
        foreach (var c in QuantityCalculator.CategorySummary(rows))
        {
            res.Add(c);
            foreach (var s in subs.Where(x => x.Category == c.Category))
            {
                s.Code = "";
                s.Name = "      " + s.Name;
                res.Add(s);
            }
        }
        return res;
    }

    public sealed record Kpi(string Label, string Value, string Unit, string Color)
    {
        public Brush Accent { get; } = (Brush)new BrushConverter().ConvertFromString(Color)!;
    }

    private void ExpandClick(object sender, RoutedEventArgs e) => SetExpanded(GridRows, true);

    private void CollapseClick(object sender, RoutedEventArgs e) => SetExpanded(GridRows, false);

    private static void SetExpanded(DependencyObject root, bool expanded)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Expander { Tag: "group" } ex) ex.IsExpanded = expanded;
            SetExpanded(child, expanded);
        }
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        var v = Visible;
        var suffix = Category is { } c ? " - " + QuantityRow.CategoryLabel(c) : "";
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (separado por ponto e vírgula)|*.csv",
            FileName = $"Quantitativos - {_projectName}{suffix}.csv".Replace(":", ""),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, QuantityCalculator.ToCsv(v, QuantityCalculator.Summary(v)), new UTF8Encoding(true));
            MessageBox.Show(this, "Arquivo exportado:\n" + dlg.FileName, "Quantitativos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Não foi possível gravar o arquivo: " + ex.Message);
        }
    }

    private void ScheduleClick(object sender, RoutedEventArgs e)
    {
        CreateSchedule = true;
        DialogResult = true;
    }

    private void SchedulesByCategoryClick(object sender, RoutedEventArgs e)
    {
        SchedulesByCategory = true;
        DialogResult = true;
    }

    public IReadOnlyList<CategoriaQuantitativo> Categories => _rows.Select(r => r.Category).Distinct().OrderBy(c => c).ToList();
}

/// <summary>Resumo de um grupo do quadro (quantidade de itens e totais por unidade).</summary>
public sealed class GroupSummaryConverter : IValueConverter
{
    public static GroupSummaryConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CollectionViewGroup g) return "";
        var rows = Flatten(g).ToList();
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var parts = new List<string> { $"{rows.Count} item(ns)" };
        var area = rows.Sum(r => r.Area);
        var len = rows.Sum(r => r.PaintedLength);
        var un = rows.Sum(r => r.Units);
        if (area > 1e-6) parts.Add($"{area.ToString("N2", pt)} m²");
        if (len > 1e-6) parts.Add($"{len.ToString("N2", pt)} m");
        if (un > 0) parts.Add($"{un.ToString("N0", pt)} un");
        return "— " + string.Join("  ·  ", parts);
    }

    private static IEnumerable<QuantityRow> Flatten(CollectionViewGroup g)
    {
        foreach (var it in g.Items)
        {
            if (it is QuantityRow r) yield return r;
            else if (it is CollectionViewGroup sub) foreach (var x in Flatten(sub)) yield return x;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Cor da marca → pincel (amostra de cor no quadro).</summary>
public sealed class MarkingColorBrushConverter : IValueConverter
{
    public static MarkingColorBrushConverter Instance { get; } = new();
    private readonly Dictionary<MarkingColor, Brush> _cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not MarkingColor c) return Brushes.Transparent;
        if (_cache.TryGetValue(c, out var b)) return b;
        var rgb = MarkingColors.Display(c);
        b = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
        b.Freeze();
        _cache[c] = b;
        return b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
