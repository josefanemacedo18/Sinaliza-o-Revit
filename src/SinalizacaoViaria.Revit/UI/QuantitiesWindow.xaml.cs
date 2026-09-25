using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
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

    public QuantitiesWindow(List<QuantityRow> rows, string projectName)
    {
        InitializeComponent();
        _rows = rows;
        _projectName = projectName;
        CbCategory.Items.Add(new Option("Todas as categorias", null));
        foreach (var c in rows.Select(r => r.Category).Distinct().OrderBy(c => c))
            CbCategory.Items.Add(new Option($"{QuantityRow.CategoryLabel(c)} ({rows.Count(r => r.Category == c)})", c));
        _view = new ListCollectionView(_rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QuantityRow.CategoryName)));
        _view.Filter = o => Matches((QuantityRow)o);
        GridRows.ItemsSource = _view;
        CbCategory.SelectedIndex = 0;
        Refresh();
    }

    private CategoriaQuantitativo? Category => (CbCategory.SelectedItem as Option)?.Value;

    private bool Matches(QuantityRow r)
    {
        if (Category is { } c && r.Category != c) return false;
        var q = TbSearch?.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return r.Code.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.Material.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Color.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.GroupName.Contains(q, StringComparison.OrdinalIgnoreCase);
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
        GridCategories.ItemsSource = QuantityCalculator.CategorySummary(v);
        GridSummary.ItemsSource = QuantityCalculator.Summary(v);
        TxtHeader.Text = $"{_projectName}: {v.Sum(r => r.Elements)} elemento(s) – {v.Select(r => r.Category).Distinct().Count()} categoria(s) – " +
                         $"{UiHelpers.F(v.Where(r => Core.Model.MarkingColors.IsPaint(r.Color)).Sum(r => r.Area))} m² pintados – " +
                         $"{UiHelpers.F(v.Sum(r => r.PaintedLength))} m – {v.Sum(r => r.Units)} unidade(s)";
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
