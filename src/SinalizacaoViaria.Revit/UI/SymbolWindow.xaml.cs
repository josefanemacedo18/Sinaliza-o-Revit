using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Setas (PEM, mudança de faixa) e símbolos (SIA, bicicleta, Dê a preferência...).</summary>
public partial class SymbolWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly SymbolMarkingDefinition? _existing;
    private bool _loading = true;

    public SymbolMarkingDefinition? Result { get; private set; }
    public bool FixedAngle { get; private set; }
    public double AngleDeg { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    public SymbolWindow(SymbolMarkingDefinition? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        foreach (var s in _cat.Simbolos) LbSymbols.Items.Add(s);
        foreach (var c in UiHelpers.ColorItems()) CbColor.Items.Add(c);
        CbColor.SelectedIndex = 0;
        Output.Changed += (_, _) => UpdatePreview();
        if (existing != null)
        {
            Title = $"Editar – {existing.Code}";
            BtnOk.Content = "Aplicar";
            GbPlacement.Visibility = Visibility.Collapsed;
            LbSymbols.SelectedItem = _cat.Simbolo(existing.Code);
            CbLength.Text = UiHelpers.F(existing.Length, "0.##");
            TbWidthFactor.Text = UiHelpers.F(existing.WidthFactor, "0.##");
            CkMirror.IsChecked = existing.Mirror;
            UiHelpers.SelectColor(CbColor, existing.ColorOverride);
            Output.Load(existing.Output);
        }
        else
        {
            Output.Load(PluginContext.Settings.NewOutput());
            LbSymbols.SelectedItem = _cat.Simbolo(PluginContext.Settings.Get("symbol")) ?? _cat.Simbolos.FirstOrDefault();
        }
        _loading = false;
        UpdatePreview();
    }

    private SimboloDef? Symbol => LbSymbols.SelectedItem as SimboloDef;

    private void SymbolChanged(object sender, SelectionChangedEventArgs e)
    {
        var s = Symbol;
        if (s == null) return;
        TxtDescription.Text = $"{s.Codigo} – {s.Nome}\n\n{s.Descricao}";
        TxtReference.Text = s.Referencia;
        var keep = _loading && _existing != null ? CbLength.Text : null;
        CbLength.Items.Clear();
        foreach (var l in s.Comprimentos) CbLength.Items.Add(UiHelpers.F(l, "0.##"));
        if (keep != null) CbLength.Text = keep; else CbLength.SelectedIndex = 0;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();
    private void AnyKey(object sender, KeyEventArgs e) => UpdatePreview();

    private SymbolMarkingDefinition BuildDefinition()
    {
        var s = Symbol ?? throw new FormatException("Selecione um símbolo.");
        var d = _existing != null ? (SymbolMarkingDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new SymbolMarkingDefinition();
        d.Code = s.Codigo;
        var text = CbLength.SelectedItem as string ?? CbLength.Text;
        d.Length = UiHelpers.ParseOpt(text) ?? s.Comprimentos.FirstOrDefault(5.0);
        if (d.Length <= 0.1 || d.Length > 30) throw new FormatException("Comprimento deve estar entre 0,10 e 30 m.");
        d.WidthFactor = UiHelpers.Parse(TbWidthFactor, 1, "Fator de largura", 0.3, 3);
        d.Mirror = CkMirror.IsChecked == true;
        d.ColorOverride = UiHelpers.SelectedColor(CbColor);
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = (SymbolMarkingDefinition)MarkingDefinition.FromJson(BuildDefinition().ToJson())!;
            d.Position = Vec2.Zero;
            d.Direction = Vec2.UnitY;
            var geo = MarkingBuilder.Build(d, null, new BuildContext { Catalog = _cat });
            var lane = Polygon2.Rectangle(new Vec2(-1.75, -0.8), new Vec2(1.75, Math.Max(3, d.Length) + 0.8));
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
            PluginContext.Settings.Set("symbol", Result.Code);
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
