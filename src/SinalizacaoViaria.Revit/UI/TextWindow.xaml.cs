using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Legendas alongadas (PARE, ÔNIBUS, ESCOLA...).</summary>
public partial class TextWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly TextMarkingDefinition? _existing;
    private bool _loading = true;

    public TextMarkingDefinition? Result { get; private set; }
    public bool FixedAngle { get; private set; }
    public double AngleDeg { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    public TextWindow(TextMarkingDefinition? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        foreach (var l in _cat.Legendas) LbPresets.Items.Add(l);
        foreach (var s in _cat.TamanhosLetra) CbSize.Items.Add(s);
        foreach (var f in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s)) CbFont.Items.Add(f);
        foreach (var c in UiHelpers.ColorItems("Branca (padrão)").Skip(1)) CbColor.Items.Add(c);
        Output.Changed += (_, _) => UpdatePreview();

        if (existing != null)
        {
            Title = "Editar legenda";
            BtnOk.Content = "Aplicar";
            GbPlacement.Visibility = Visibility.Collapsed;
            TbText.Text = existing.Text.Replace("\n", Environment.NewLine);
            TbHeight.Text = UiHelpers.F(existing.Height, "0.##");
            TbScale.Text = UiHelpers.F(existing.Scale * 100, "0.#");
            TbWidthFactor.Text = UiHelpers.F(existing.WidthFactor, "0.###");
            TbLetter.Text = UiHelpers.F(existing.LetterSpacing, "0.###");
            TbLine.Text = UiHelpers.F(existing.LineSpacing, "0.##");
            CbFont.Text = existing.FontFamily;
            CkBold.IsChecked = existing.Bold;
            CkBottomUp.IsChecked = existing.BottomToTop;
            UiHelpers.SelectColor(CbColor, existing.Color);
            Output.Load(existing.Output);
        }
        else
        {
            CbSize.SelectedIndex = 0;
            ApplySize(_cat.TamanhosLetra.FirstOrDefault());
            CbFont.Text = PluginContext.Settings.FontFamily;
            CkBold.IsChecked = PluginContext.Settings.FontBold;
            TbText.Text = "PARE";
            CbColor.SelectedIndex = 0;
            Output.Load(PluginContext.Settings.NewOutput());
        }
        _loading = false;
        UpdatePreview();
    }

    private void PresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LbPresets.SelectedItem is LegendaDef l) TbText.Text = l.Texto.Replace("\n", Environment.NewLine);
    }

    private void LetterSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        ApplySize(CbSize.SelectedItem as TamanhoLetraDef);
        UpdatePreview();
    }

    private void ApplySize(TamanhoLetraDef? s)
    {
        if (s == null) return;
        TbHeight.Text = UiHelpers.F(s.Altura, "0.##");
        TbWidthFactor.Text = UiHelpers.F(s.FatorLargura, "0.###");
        TbLetter.Text = UiHelpers.F(s.Espacamento, "0.###");
        TbLine.Text = UiHelpers.F(s.Entrelinha, "0.##");
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private TextMarkingDefinition BuildDefinition()
    {
        var text = TbText.Text.Replace("\r", "").Trim();
        if (text.Length == 0) throw new FormatException("Digite o texto da legenda.");
        var d = _existing != null ? (TextMarkingDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new TextMarkingDefinition();
        d.Text = text;
        d.Height = UiHelpers.Parse(TbHeight, 1.6, "Altura", 0.1, 10);
        d.Scale = UiHelpers.Parse(TbScale, 100, "Escala", 10, 1000) / 100.0;
        d.WidthFactor = UiHelpers.Parse(TbWidthFactor, 0.4, "Fator de largura", 0.05, 3);
        d.LetterSpacing = UiHelpers.Parse(TbLetter, 0.08, "Espaço entre letras", 0, 5);
        d.LineSpacing = UiHelpers.Parse(TbLine, 1, "Espaço entre linhas", 0, 20);
        d.FontFamily = string.IsNullOrWhiteSpace(CbFont.Text) ? "Arial" : CbFont.Text.Trim();
        d.Bold = CkBold.IsChecked == true;
        d.BottomToTop = CkBottomUp.IsChecked == true;
        d.Color = UiHelpers.SelectedColor(CbColor) ?? Core.Model.MarkingColor.Branca;
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = (TextMarkingDefinition)MarkingDefinition.FromJson(BuildDefinition().ToJson())!;
            d.Position = Vec2.Zero;
            d.Direction = Vec2.UnitY;
            var geo = MarkingBuilder.Build(d, null, new BuildContext { Catalog = _cat, Glyphs = PluginContext.Glyphs });
            var h = geo.Bounds?.Max.Y ?? d.Height;
            var lane = Polygon2.Rectangle(new Vec2(-1.75, -0.8), new Vec2(1.75, h + 0.8));
            Preview.Show(geo, null, new[] { lane }, "Faixa de 3,50 m – tráfego para cima");
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
            Result = BuildDefinition();
            FixedAngle = RbAngle.IsChecked == true;
            AngleDeg = UiHelpers.Parse(TbAngle, 0, "Ângulo", -360, 360);
            PluginContext.Settings.FontFamily = Result.FontFamily;
            PluginContext.Settings.FontBold = Result.Bold;
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
