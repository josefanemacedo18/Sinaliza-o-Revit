using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Faixa de pedestres + linhas de retenção automáticas.</summary>
public partial class CrosswalkWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private bool _loading = true;

    public CrosswalkSetup? Setup { get; private set; }
    public OutputSettings? OutputSettings { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    public CrosswalkWindow()
    {
        InitializeComponent();
        foreach (var t in _cat.Lineares.Where(t => t.Codigo.StartsWith("FTP", StringComparison.OrdinalIgnoreCase) || t.Codigo == "MCC"))
            CbType.Items.Add(t);
        var lre = _cat.Linear("LRE");
        if (lre != null) foreach (var v in lre.Variantes) CbStopVariant.Items.Add(v);
        CbStopVariant.SelectedIndex = 0;
        Output.Load(PluginContext.Settings.NewOutput());
        Output.Changed += (_, _) => UpdatePreview();
        CbType.SelectedIndex = 0;
        _loading = false;
        UpdatePreview();
    }

    private TipoLinearDef? Type => CbType.SelectedItem as TipoLinearDef;

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        CbVariant.Items.Clear();
        if (Type == null) return;
        foreach (var v in Type.Variantes) CbVariant.Items.Add(v);
        CbVariant.SelectedIndex = 0;
        TbWidth.IsEnabled = Type.Codigo.StartsWith("FTP-1", StringComparison.OrdinalIgnoreCase);
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private CrosswalkSetup BuildSetup() => new()
    {
        CrosswalkCode = Type?.Codigo ?? "FTP-1",
        CrosswalkVariant = (CbVariant.SelectedItem as VarianteDef)?.Nome,
        CrosswalkWidth = TbWidth.IsEnabled ? UiHelpers.ParseNullable(TbWidth, "Largura da faixa", 1, 20) : null,
        StopLines = CkStop.IsChecked == true,
        StopLineVariant = (CbStopVariant.SelectedItem as VarianteDef)?.Nome ?? "0,40 m",
        StopLineDistance = UiHelpers.Parse(TbDistance, 1.6, "Distância", 0, 50),
        StopLineLeftSide = CkLeft.IsChecked == true,
        StopLineRightSide = CkRight.IsChecked == true,
        Span = RbFull.IsChecked == true ? StopLineSpan.PistaInteira : StopLineSpan.MeiaPista,
        EdgeSetback = UiHelpers.Parse(TbSetback, 0, "Recuo dos bordos", 0, 10),
        Overlay = CkOverlay.IsChecked == true,
    };

    /// <summary>Largura total ocupada pela faixa e largura da LRE (para posicionar a retenção).</summary>
    public (double Crosswalk, double StopLine) Widths(CrosswalkSetup s)
    {
        var t = _cat.Linear(s.CrosswalkCode);
        var v = t?.Variante(s.CrosswalkVariant);
        var cw = v == null ? 4.0 : LinearPatternGenerator.Footprint(v, s.CrosswalkWidth);
        var lre = _cat.Linear("LRE")?.Variante(s.StopLineVariant);
        var sw = lre?.Faixas.FirstOrDefault()?.Largura ?? 0.4;
        return (cw, sw);
    }

    public List<MarkingDefinition> BuildDefinitions(CrosswalkSetup s, OutputSettings o, Vec2 a, Vec2 b, double z)
    {
        var (cw, sw) = Widths(s);
        return s.Build(a, b, z, o, cw, sw);
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var s = BuildSetup();
            var o = Output.Save();
            var defs = BuildDefinitions(s, o, new Vec2(0, 0), new Vec2(0, 7), 0);
            var geo = new MarkingGeometry();
            var ctx = new BuildContext { Catalog = _cat };
            foreach (var d in defs.OfType<LinearMarkingDefinition>())
                geo.Merge(MarkingBuilder.Build(d, new Polyline2(d.PathRef.Points), ctx));
            var pav = Polygon2.Rectangle(new Vec2(-9, 0), new Vec2(9, 7));
            Preview.Show(geo, new[] { (IReadOnlyList<Vec2>)new[] { new Vec2(0, 0), new Vec2(0, 7) } }, new[] { pav });
            TxtWarnings.Text = string.Join("\n", geo.Warnings.Distinct());
        }
        catch (Exception ex)
        {
            TxtWarnings.Text = ex.Message;
        }
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Setup = BuildSetup();
            OutputSettings = Output.Save();
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
