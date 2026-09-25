using System.Windows;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Edição de símbolos/legendas repetidos ao longo do eixo (gerados por "Sinalizar via").</summary>
public partial class RepeatedWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly RepeatedMarkingDefinition _existing;
    private bool _loading = true;

    public RepeatedMarkingDefinition? Result { get; private set; }

    private sealed record SymbolItem(string? Code, string Label)
    {
        public override string ToString() => Label;
    }

    public RepeatedWindow(RepeatedMarkingDefinition existing)
    {
        InitializeComponent();
        _existing = existing;
        CbSymbol.Items.Add(new SymbolItem(null, "(legenda de texto)"));
        foreach (var s in _cat.Simbolos) CbSymbol.Items.Add(new SymbolItem(s.Codigo, s.ToString()));
        CbSymbol.SelectedItem = CbSymbol.Items.Cast<SymbolItem>().FirstOrDefault(i => i.Code == existing.SymbolCode) ?? CbSymbol.Items[0];
        foreach (var c in UiHelpers.ColorItems()) CbColor.Items.Add(c);
        UiHelpers.SelectColor(CbColor, existing.Color);
        TbText.Text = existing.Text ?? "";
        TbSize.Text = UiHelpers.F(string.IsNullOrEmpty(existing.SymbolCode) ? existing.TextHeight : existing.Length, "0.##");
        TbSpacing.Text = UiHelpers.F(existing.Spacing, "0.#");
        TbStart.Text = UiHelpers.F(existing.StartOffset, "0.#");
        TbEnd.Text = UiHelpers.F(existing.EndSetback, "0.#");
        TbOffset.Text = UiHelpers.F(existing.Offset, "0.###");
        CkReverse.IsChecked = existing.Reverse;
        Output.Load(existing.Output);
        Output.Changed += (_, _) => UpdatePreview();
        _loading = false;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private RepeatedMarkingDefinition BuildDefinition()
    {
        var d = (RepeatedMarkingDefinition)MarkingDefinition.FromJson(_existing.ToJson())!;
        d.SymbolCode = (CbSymbol.SelectedItem as SymbolItem)?.Code;
        d.Text = TbText.Text.Trim();
        if (d.SymbolCode == null && d.Text.Length == 0) throw new FormatException("Escolha um símbolo ou digite a legenda.");
        var size = UiHelpers.Parse(TbSize, 1.6, "Tamanho", 0.1, 20);
        if (d.SymbolCode == null) d.TextHeight = size; else d.Length = size;
        d.Spacing = UiHelpers.Parse(TbSpacing, 50, "Espaçamento", 1, 10000);
        d.StartOffset = UiHelpers.Parse(TbStart, 10, "Início", 0, 10000);
        d.EndSetback = UiHelpers.Parse(TbEnd, 5, "Recuo final", 0, 10000);
        d.Offset = UiHelpers.Parse(TbOffset, 0, "Deslocamento", -200, 200);
        d.Reverse = CkReverse.IsChecked == true;
        d.Color = UiHelpers.SelectedColor(CbColor);
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            d.Offset = 0;
            var sample = new Polyline2(new[] { new Vec2(0, 0), new Vec2(60, 0) });
            var geo = MarkingBuilder.Build(d, sample, new BuildContext { Catalog = _cat, Glyphs = PluginContext.Glyphs });
            Preview.Show(geo, new[] { sample.Points }, new[] { Polygon2.Rectangle(new Vec2(-1, -1.75), new Vec2(61, 1.75)) });
            TxtWarnings.Text = $"{geo.UnitCount} inscrição(ões) em 60 m. " + string.Join("\n", geo.Warnings.Distinct());
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
