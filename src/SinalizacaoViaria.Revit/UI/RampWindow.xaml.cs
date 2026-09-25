using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Rebaixamentos de calçada (NBR 9050) e guias rebaixadas para veículos.</summary>
public partial class RampWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly RampDefinition? _existing;
    private bool _loading = true;

    public RampDefinition? Result { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    private sealed record Option(string Label, TipoRampa Value)
    {
        public override string ToString() => Label;
    }

    public RampWindow(RampDefinition? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        CbType.Items.Add(new Option("Rebaixamento com abas laterais (pedestres)", TipoRampa.RebaixamentoComAbas));
        CbType.Items.Add(new Option("Rebaixamento sem abas (laterais protegidas)", TipoRampa.RebaixamentoSemAbas));
        CbType.Items.Add(new Option("Guia rebaixada / acesso de veículos", TipoRampa.AcessoVeiculos));
        foreach (var c in UiHelpers.ColorItems().Skip(1)) CbTactileColor.Items.Add(c);
        UiHelpers.SelectColor(CbTactileColor, MarkingColor.Amarela);
        Output.Changed += (_, _) => UpdatePreview();
        var d = existing ?? new RampDefinition();
        foreach (var item in CbType.Items)
            if (item is Option o && o.Value == d.Type) CbType.SelectedItem = item;
        TbWidth.Text = UiHelpers.F(d.Width);
        TbHeight.Text = UiHelpers.F(d.Height);
        TbSlope.Text = UiHelpers.F(d.Slope * 100, "0.##");
        TbFlare.Text = UiHelpers.F(d.FlareSlope * 100, "0.##");
        CkTactile.IsChecked = d.Tactile;
        TbTactile.Text = UiHelpers.F(d.TactileWidth);
        UiHelpers.SelectColor(CbTactileColor, d.TactileColor);
        CkCut.IsChecked = d.CutSidewalk;
        Output.Load(existing?.Output ?? PluginContext.Settings.NewOutput());
        if (existing != null) { Title = "Editar rampa"; BtnOk.Content = "Aplicar"; }
        _loading = false;
        UpdatePreview();
    }

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CbType.SelectedItem is not Option o) return;
        _loading = true;
        var vehicle = o.Value == TipoRampa.AcessoVeiculos;
        TbWidth.Text = UiHelpers.F(vehicle ? 3.0 : 1.50);
        TbSlope.Text = vehicle ? "25" : "8,33";
        CkTactile.IsChecked = !vehicle;
        _loading = false;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private RampDefinition BuildDefinition()
    {
        var d = _existing != null ? (RampDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new RampDefinition();
        d.Type = (CbType.SelectedItem as Option)?.Value ?? TipoRampa.RebaixamentoComAbas;
        d.Width = UiHelpers.Parse(TbWidth, 1.5, "Largura", 0.5, 20);
        d.Height = UiHelpers.Parse(TbHeight, 0.15, "Altura", 0.02, 1);
        d.Slope = UiHelpers.Parse(TbSlope, 8.33, "Inclinação", 1, 100) / 100;
        d.FlareSlope = UiHelpers.Parse(TbFlare, 10, "Inclinação das abas", 1, 100) / 100;
        d.Tactile = CkTactile.IsChecked == true;
        d.TactileWidth = UiHelpers.Parse(TbTactile, 0.40, "Largura do piso tátil", 0.1, 2);
        d.TactileColor = UiHelpers.SelectedColor(CbTactileColor) ?? MarkingColor.Amarela;
        d.CutSidewalk = CkCut.IsChecked == true;
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            var path = new Polyline2(new[] { Vec2.Zero, new Vec2(0, 1) });
            var geo = MarkingBuilder.Build(d, path, new BuildContext { Catalog = _cat });
            var (w, len, flare) = RampGenerator.Dimensions(d);
            var road = Polygon2.Rectangle(new Vec2(-w / 2 - flare - 1, -2), new Vec2(w / 2 + flare + 1, 0));
            var walk = new MarkingGeometry();
            walk.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(-w / 2 - flare - 1, 0), new Vec2(w / 2 + flare + 1, len + 1)), MarkingColor.Concreto));
            walk = MarkingBuilder.ApplyExclusions(walk, new[] { new ExclusionZone { Points = RampGenerator.Footprint(d, path).Outer.ToList() } });
            walk.Merge(geo);
            Preview.Show(walk, null, new[] { road });
            TxtWarnings.Text = $"Comprimento da rampa: {UiHelpers.F(len)} m" + (flare > 0 ? $"; abas: {UiHelpers.F(flare)} m cada lado." : ".") +
                               (geo.Warnings.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", geo.Warnings) : "");
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
            Result = BuildDefinition();
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
