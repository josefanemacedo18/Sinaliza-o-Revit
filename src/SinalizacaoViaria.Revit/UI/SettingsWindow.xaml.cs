using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

public partial class SettingsWindow : Window
{
    /// <summary>O usuário pediu o Autoteste (roda depois que a janela fecha).</summary>
    public bool RunAutoTest { get; private set; }

    private void AutoTestClick(object sender, RoutedEventArgs e)
    {
        RunAutoTest = true;
        DialogResult = false;
    }

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
        CkAutoIntersect.IsChecked = s.AutoIntersect;
        CkAutoConnect.IsChecked = s.AutoConnect;
        CkAutoCrosswalks.IsChecked = s.AutoCrosswalks;
        CkFloors.IsChecked = s.PhysicalAsFloors;
        CkPaintFloors.IsChecked = s.PaintAsFloors;
        TbBoundaryTol.Text = UiHelpers.F(s.BoundaryTolerance * 1000, "0.##");
        CkBoundary.IsChecked = s.VisibleBoundary2D;
        var c = s.CriteriosGeometricos ?? new GeometricCriteria();
        TbCritFonte.Text = c.Fonte ?? "";
        TbCritRaio.Text = UiHelpers.F(c.RaioMinimo, "0.##");
        TbCritTangente.Text = UiHelpers.F(c.TangenteMinima, "0.##");
        TbCritDeflexao.Text = UiHelpers.F(c.DeflexaoMaximaSemCurva, "0.###");
        TbCritRampaMax.Text = UiHelpers.F(c.RampaMaxima, "0.###");
        TbCritRampaMin.Text = UiHelpers.F(c.RampaMinima, "0.###");
        TbCritKConvexa.Text = UiHelpers.F(c.KMinimoConvexa, "0.##");
        TbCritKConcava.Text = UiHelpers.F(c.KMinimoConcava, "0.##");
        TbCritCurvaVert.Text = UiHelpers.F(c.CurvaVerticalMinima, "0.##");
        TbCritTerreno.Text = UiHelpers.F(c.DeclividadeMaximaTerreno, "0.##");
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
            s.AutoIntersect = CkAutoIntersect.IsChecked == true;
            s.AutoConnect = CkAutoConnect.IsChecked == true;
            s.AutoCrosswalks = CkAutoCrosswalks.IsChecked == true;
            s.PhysicalAsFloors = CkFloors.IsChecked == true;
            s.PaintAsFloors = CkPaintFloors.IsChecked == true;
            s.BoundaryTolerance = UiHelpers.Parse(TbBoundaryTol, 5, "Desvio máximo dos contornos", 0, 50) / 1000;
            s.VisibleBoundary2D = CkBoundary.IsChecked == true;
            s.CriteriosGeometricos = new GeometricCriteria
            {
                Fonte = string.IsNullOrWhiteSpace(TbCritFonte.Text) ? null : TbCritFonte.Text.Trim(),
                RaioMinimo = UiHelpers.ParseNullable(TbCritRaio, "Raio mínimo", 0, 100000),
                TangenteMinima = UiHelpers.ParseNullable(TbCritTangente, "Tangente mínima", 0, 100000),
                DeflexaoMaximaSemCurva = UiHelpers.ParseNullable(TbCritDeflexao, "Deflexão máxima sem curva", 0, 180),
                RampaMaxima = UiHelpers.ParseNullable(TbCritRampaMax, "Rampa máxima", 0, 100),
                RampaMinima = UiHelpers.ParseNullable(TbCritRampaMin, "Rampa mínima", 0, 100),
                KMinimoConvexa = UiHelpers.ParseNullable(TbCritKConvexa, "K mínimo convexa", 0, 100000),
                KMinimoConcava = UiHelpers.ParseNullable(TbCritKConcava, "K mínimo côncava", 0, 100000),
                CurvaVerticalMinima = UiHelpers.ParseNullable(TbCritCurvaVert, "Comprimento mínimo de curva vertical", 0, 100000),
                DeclividadeMaximaTerreno = UiHelpers.ParseNullable(TbCritTerreno, "Declividade máxima do terreno", 0, 1000),
            };
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
