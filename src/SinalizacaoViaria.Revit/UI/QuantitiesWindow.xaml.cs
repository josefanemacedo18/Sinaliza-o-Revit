using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Revit.UI;

public partial class QuantitiesWindow : Window
{
    private readonly List<QuantityRow> _rows;
    private readonly List<QuantityRow> _summary;
    private readonly string _projectName;

    public bool CreateSchedule { get; private set; }

    public QuantitiesWindow(List<QuantityRow> rows, string projectName)
    {
        InitializeComponent();
        _rows = rows;
        _summary = QuantityCalculator.Summary(rows);
        _projectName = projectName;
        GridRows.ItemsSource = _rows;
        GridSummary.ItemsSource = _summary;
        TxtHeader.Text = $"{projectName}: {rows.Sum(r => r.Elements)} marca(s) – {UiHelpers.F(rows.Sum(r => r.Area))} m² pintados – " +
                         $"{UiHelpers.F(rows.Sum(r => r.PaintedLength))} m de linhas – {rows.Sum(r => r.Units)} unidade(s)";
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (separado por ponto e vírgula)|*.csv",
            FileName = $"Quantitativos sinalização - {_projectName}.csv",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, QuantityCalculator.ToCsv(_rows, _summary), new UTF8Encoding(true));
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
}
