using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Painel comum a todas as janelas: modo 3D/2D, material, espessura e projeção sobre superfícies.</summary>
public partial class OutputPanel : UserControl
{
    public OutputPanel()
    {
        InitializeComponent();
        foreach (var m in PluginContext.Catalog.Materiais) CbMaterial.Items.Add(m.Nome);
    }

    /// <summary>Usuário pediu para escolher superfícies após fechar a janela.</summary>
    public bool PickSurfaces => CkPickSurfaces.IsChecked == true && CkDrape.IsChecked == true;

    /// <summary>Disparado quando o modo muda (para atualizar prévias).</summary>
    public event EventHandler? Changed;

    public void Load(OutputSettings o)
    {
        Rb3D.IsChecked = o.Mode == OutputMode.Modelo3D;
        Rb2D.IsChecked = o.Mode == OutputMode.Detalhe2D;
        var mat = o.Material ?? PluginContext.Settings.DefaultMaterial;
        CbMaterial.SelectedItem = CbMaterial.Items.Cast<string>().FirstOrDefault(m => m == mat) ?? (CbMaterial.Items.Count > 0 ? CbMaterial.Items[0] : null);
        TbThickness.Text = o.Thickness > 0 ? UiHelpers.F(o.Thickness, "0.###") : "";
        TbElevation.Text = UiHelpers.F(o.ElevationOffset, "0.###");
        CkDrape.IsChecked = o.Drape;
        CkPickSurfaces.IsChecked = o.SurfaceIds.Count > 0;
        UpdateEnabled();
    }

    public OutputSettings Save(OutputSettings? baseSettings = null)
    {
        var o = baseSettings?.Clone() ?? new OutputSettings();
        o.Mode = Rb2D.IsChecked == true ? OutputMode.Detalhe2D : OutputMode.Modelo3D;
        o.Material = CbMaterial.SelectedItem as string;
        o.Thickness = UiHelpers.ParseNullable(TbThickness, "Espessura", 0, 1) ?? 0;
        o.ElevationOffset = UiHelpers.Parse(TbElevation, 0.001, "Elevação adicional", -100, 100);
        o.Drape = CkDrape.IsChecked == true && o.Mode == OutputMode.Modelo3D;
        if (!o.Drape) o.SurfaceIds.Clear();
        return o;
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        UpdateEnabled();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEnabled()
    {
        if (TbThickness == null) return;
        var is3D = Rb3D.IsChecked == true;
        TbThickness.IsEnabled = is3D;
        TbElevation.IsEnabled = is3D;
        CkDrape.IsEnabled = is3D;
    }
}
