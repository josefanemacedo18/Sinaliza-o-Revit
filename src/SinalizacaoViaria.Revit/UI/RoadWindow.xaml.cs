using System.Windows;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Configuração da seção transversal para gerar toda a sinalização longitudinal de uma via.</summary>
public partial class RoadWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private bool _loading = true;

    public RoadSetup? Setup { get; private set; }
    public OutputSettings? OutputSettings { get; private set; }
    public bool DrawPath { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    private sealed record Option(string Label, CenterTreatment Value)
    {
        public override string ToString() => Label;
    }

    public RoadWindow()
    {
        InitializeComponent();
        CbCenter.Items.Add(new Option("LFO-2 – seccionada (ultrapassagem permitida)", CenterTreatment.LFO2));
        CbCenter.Items.Add(new Option("LFO-1 – contínua simples", CenterTreatment.LFO1));
        CbCenter.Items.Add(new Option("LFO-3 – dupla contínua", CenterTreatment.LFO3));
        CbCenter.Items.Add(new Option("LFO-4 – contínua/seccionada", CenterTreatment.LFO4));
        CbCenter.Items.Add(new Option("Canteiro central (bordos)", CenterTreatment.Canteiro));
        CbCenter.Items.Add(new Option("Sem marca no eixo", CenterTreatment.Nenhum));
        CbCenter.SelectedIndex = 0;

        foreach (var t in _cat.LinearesDoGrupo(GrupoMarca.Longitudinal).Where(t => t.Codigo.StartsWith("LMS") || t.Codigo.StartsWith("LCO") || t.Codigo == "MFE"))
            CbDivider.Items.Add(t.Codigo);
        CbDivider.SelectedItem = "LMS-2";
        foreach (var t in _cat.LinearesDoGrupo(GrupoMarca.Longitudinal, GrupoMarca.Ciclovia).Where(t => t.Codigo == "LBO" || t.Codigo.StartsWith("CIC-LD")))
            CbEdge.Items.Add(t.Codigo);
        CbEdge.SelectedItem = "LBO";
        foreach (var t in _cat.LinearesDoGrupo(GrupoMarca.Dispositivo))
            foreach (var v in t.Variantes) CbStuds.Items.Add($"{t.Codigo} | {v.Nome}");
        if (CbStuds.Items.Count > 0) CbStuds.SelectedIndex = 0;

        TbSpeed.Text = UiHelpers.F(PluginContext.Settings.DefaultSpeed, "0");
        Output.Load(PluginContext.Settings.NewOutput());
        Output.Changed += (_, _) => UpdatePreview();
        _loading = false;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || GbTwoWay == null || GbOneWay == null) return;
        var two = RbTwoWay.IsChecked == true;
        GbTwoWay.Visibility = two ? Visibility.Visible : Visibility.Collapsed;
        GbOneWay.Visibility = two ? Visibility.Collapsed : Visibility.Visible;
        UpdatePreview();
    }

    private RoadSetup BuildSetup()
    {
        var s = new RoadSetup
        {
            TwoWay = RbTwoWay.IsChecked == true,
            RightLanes = RoadSetup.ParseWidths(TbRight.Text),
            LeftLanes = RoadSetup.ParseWidths(TbLeft.Text),
            OneWayLanes = RoadSetup.ParseWidths(TbOneWay.Text),
            Center = (CbCenter.SelectedItem as Option)?.Value ?? CenterTreatment.LFO2,
            MedianWidth = UiHelpers.Parse(TbMedian, 2, "Canteiro", 0, 100),
            LaneDividerCode = CbDivider.SelectedItem as string ?? "LMS-2",
            EdgeLines = CkEdges.IsChecked == true,
            EdgeCode = CbEdge.SelectedItem as string ?? "LBO",
            EdgeInset = UiHelpers.Parse(TbEdgeInset, 0.1, "Afastamento do bordo", -5, 5),
            Speed = UiHelpers.Parse(TbSpeed, 60, "Velocidade", 10, 200),
            StartSetback = UiHelpers.Parse(TbStart, 0, "Recuo inicial", 0, 10000),
            EndSetback = UiHelpers.Parse(TbEnd, 0, "Recuo final", 0, 10000),
        };
        if (s.TwoWay && (s.RightLanes.Count == 0 || s.LeftLanes.Count == 0)) throw new FormatException("Informe ao menos uma faixa em cada sentido.");
        if (!s.TwoWay && s.OneWayLanes.Count == 0) throw new FormatException("Informe ao menos uma faixa.");
        if (CkStuds.IsChecked == true && CbStuds.SelectedItem is string st)
        {
            var parts = st.Split(" | ");
            s.CenterStudsCode = parts[0];
            s.CenterStudsVariant = parts.Length > 1 ? parts[1] : null;
        }
        return s;
    }

    private List<LinearMarkingDefinition> Build(RoadSetup s, OutputSettings o)
    {
        var defs = s.Build(new PathReference(), o);
        if (CkInvertCenter.IsChecked == true)
            foreach (var d in defs.Where(d => d.Code == "LFO-4")) d.InvertSides = true;
        return defs;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var s = BuildSetup();
            var o = Output.Save();
            var sample = new Polyline2(new[] { new Vec2(0, 0), new Vec2(48, 0) });
            var ctx = new BuildContext { Catalog = _cat };
            var geo = new MarkingGeometry();
            foreach (var d in Build(s, o)) geo.Merge(MarkingBuilder.Build(d, sample, ctx));
            var half = s.TwoWay ? s.LeftLanes.Sum() + (s.Center == CenterTreatment.Canteiro ? s.MedianWidth / 2 : 0) : s.OneWayLanes.Sum() / 2;
            var halfR = s.TwoWay ? s.RightLanes.Sum() + (s.Center == CenterTreatment.Canteiro ? s.MedianWidth / 2 : 0) : s.OneWayLanes.Sum() / 2;
            var pav = Polygon2.Rectangle(new Vec2(0, -halfR), new Vec2(48, half));
            Preview.Show(geo, new[] { sample.Points }, new[] { pav }, "Trecho reto de 48 m (eixo tracejado em magenta)");
            var codes = Build(s, o).GroupBy(d => d.Code).Select(g => $"{g.Count()}× {g.Key}");
            TxtSummary.Text = $"Largura total da pista: {UiHelpers.F(s.TotalWidth)} m.  Linhas geradas: {string.Join(", ", codes)}.";
            TxtSummary.Foreground = System.Windows.Media.Brushes.Black;
        }
        catch (Exception ex)
        {
            TxtSummary.Text = ex.Message;
            TxtSummary.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
    }

    public List<LinearMarkingDefinition> BuildDefinitions(PathReference path)
    {
        var defs = Build(Setup!, OutputSettings!);
        foreach (var d in defs)
        {
            d.PathRef = new PathReference { ElementIds = new List<string>(path.ElementIds), Points = new List<Vec2>(path.Points), Z = path.Z };
        }
        return defs;
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Setup = BuildSetup();
            OutputSettings = Output.Save();
            DrawPath = RbDraw.IsChecked == true;
            PluginContext.Settings.DefaultSpeed = Setup.Speed;
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
