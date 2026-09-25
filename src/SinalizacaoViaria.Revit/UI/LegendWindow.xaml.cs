using System.Windows;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Opções do quadro de legenda automático.</summary>
public partial class LegendWindow : Window
{
    private readonly LegendDefinition? _existing;
    private readonly IReadOnlyList<MarkingDefinition> _all;
    private readonly double _viewScale;
    private readonly bool _loading;

    public LegendDefinition? Result { get; private set; }

    public LegendWindow(double viewScale, IReadOnlyList<MarkingDefinition> all, LegendDefinition? existing = null)
    {
        _loading = true;
        InitializeComponent();
        _existing = existing;
        _all = all;
        _viewScale = viewScale > 0 ? viewScale : 100;
        Preview.Paper = true;
        Preview.ViewScale = _viewScale;
        TxtPreview.Text = $"Pré-visualização – escala da vista 1:{_viewScale:0}";
        var d = existing ?? UiHelpers.Remembered<LegendDefinition>("Legenda") ?? new LegendDefinition();
        TbTitle.Text = d.Title;
        TbRow.Text = UiHelpers.F(d.RowMm, "0.#");
        TbSample.Text = UiHelpers.F(d.SampleMm, "0.#");
        TbColumn.Text = UiHelpers.F(d.TextColumnMm, "0.#");
        TbText.Text = UiHelpers.F(d.TextMm, "0.#");
        CkHorizontal.IsChecked = d.Horizontal;
        CkVertical.IsChecked = d.Vertical;
        CkPhysical.IsChecked = d.Physical;
        if (existing != null) { Title = "Editar quadro de legenda"; BtnOk.Content = "Aplicar"; }
        _loading = false;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private LegendDefinition BuildDefinition()
    {
        var d = _existing != null ? (LegendDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new LegendDefinition();
        d.Title = TbTitle.Text.Trim();
        d.RowMm = UiHelpers.Parse(TbRow, 10, "Altura da linha", 4, 100);
        d.SampleMm = UiHelpers.Parse(TbSample, 24, "Coluna da amostra", 5, 200);
        d.TextColumnMm = UiHelpers.Parse(TbColumn, 90, "Coluna da descrição", 20, 500);
        d.TextMm = UiHelpers.Parse(TbText, 2, "Altura do texto", 0.8, 20);
        d.Horizontal = CkHorizontal.IsChecked == true;
        d.Vertical = CkVertical.IsChecked == true;
        d.Physical = CkPhysical.IsChecked == true;
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            d.Position = Vec2.Zero;
            var ctx = new BuildContext
            {
                Catalog = PluginContext.Catalog,
                Glyphs = PluginContext.Glyphs,
                ViewScale = _viewScale,
                AllDefinitions = () => _all,
            };
            var geo = DetailGenerator.Legend(d, ctx);
            Preview.Show(geo, null, null, null);
        }
        catch (Exception ex)
        {
            Preview.Show(null, null, null, ex.Message);
        }
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Result = BuildDefinition();
            if (_existing == null) UiHelpers.Remember("Legenda", Result);
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
