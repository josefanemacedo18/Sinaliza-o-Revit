using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var s = PluginContext.Settings;
        CbMode.Items.Add("Modelo 3D");
        CbMode.Items.Add("Detalhe 2D");
        CbMode.SelectedIndex = s.DefaultMode == OutputMode.Modelo3D ? 0 : 1;
        foreach (var m in PluginContext.Catalog.Materiais) CbMaterial.Items.Add(m.Nome);
        CbMaterial.SelectedItem = s.DefaultMaterial;
        TbSpeed.Text = UiHelpers.F(s.DefaultSpeed, "0");
        TbFont.Text = s.FontFamily;
        CkBold.IsChecked = s.FontBold;
        TbMinThickness.Text = UiHelpers.F(s.MinModelThickness, "0.####");
        TbElevation.Text = UiHelpers.F(s.ElevationOffset, "0.####");
        CkDrape.IsChecked = s.DrapeByDefault;
        TbPiece.Text = UiHelpers.F(s.DrapePieceLength, "0.##");
        CkAuto.IsChecked = s.AutoUpdate;
        CkBoundary.IsChecked = s.VisibleBoundary2D;
        TbCatalog.Text = PluginContext.UserCatalogPath;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var path = TbCatalog.Text;
        TxtCatalogStatus.Text = File.Exists(path)
            ? "Catálogo do usuário encontrado." + (PluginContext.CatalogError != null ? " " + PluginContext.CatalogError : "")
            : "Nenhum arquivo do usuário – usando apenas o catálogo padrão.";
    }

    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Catálogo JSON|*.json", FileName = TbCatalog.Text };
        if (dlg.ShowDialog(this) == true) { TbCatalog.Text = dlg.FileName; UpdateStatus(); }
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CatalogService.EnsureUserCopy(TbCatalog.Text);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }

    private void FolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(TbCatalog.Text) ?? PluginPaths.Root;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = PluginContext.Settings;
            s.DefaultMode = CbMode.SelectedIndex == 1 ? OutputMode.Detalhe2D : OutputMode.Modelo3D;
            s.DefaultMaterial = CbMaterial.SelectedItem as string ?? s.DefaultMaterial;
            s.DefaultSpeed = UiHelpers.Parse(TbSpeed, 60, "Velocidade", 10, 200);
            s.FontFamily = string.IsNullOrWhiteSpace(TbFont.Text) ? "Arial" : TbFont.Text.Trim();
            s.FontBold = CkBold.IsChecked == true;
            s.MinModelThickness = UiHelpers.Parse(TbMinThickness, 0.003, "Espessura mínima", 0.001, 0.1);
            s.ElevationOffset = UiHelpers.Parse(TbElevation, 0.001, "Elevação adicional", -10, 10);
            s.DrapeByDefault = CkDrape.IsChecked == true;
            s.DrapePieceLength = UiHelpers.Parse(TbPiece, 2, "Comprimento das peças", 0.5, 50);
            s.AutoUpdate = CkAuto.IsChecked == true;
            s.VisibleBoundary2D = CkBoundary.IsChecked == true;
            s.UserCatalogPath = string.Equals(TbCatalog.Text, PluginPaths.DefaultUserCatalog, StringComparison.OrdinalIgnoreCase) ? null : TbCatalog.Text;
            PluginContext.SaveSettings();
            PluginContext.ReloadCatalog();
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
