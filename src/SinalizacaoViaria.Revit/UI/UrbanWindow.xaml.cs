using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Mobiliário e elementos urbanísticos viários: bancos, postes, árvores, abrigos, paraciclos...</summary>
public partial class UrbanWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly UrbanElementDefinition? _existing;
    private bool _loading = true;

    public UrbanElementDefinition? Result { get; private set; }
    /// <summary>Nulo = inserção por ponto.</summary>
    public PathMode? PathMode { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    public UrbanWindow(UrbanElementDefinition? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        foreach (var m in _cat.Mobiliario) LbTypes.Items.Add(m);
        foreach (var c in UiHelpers.ColorItems()) CbColor.Items.Add(c);
        CbColor.SelectedIndex = 0;
        Output.Changed += (_, _) => UpdatePreview();
        if (existing != null)
        {
            Title = $"Editar – {existing.Code}";
            BtnOk.Content = "Aplicar";
            RbPoint.IsEnabled = RbPath.IsEnabled = RbDraw.IsEnabled = false;
            RbPath.IsChecked = existing.UsePath;
            LbTypes.SelectedItem = _cat.Movel(existing.Code);
            var m = _cat.Movel(existing.Code);
            TbLength.Text = UiHelpers.F(existing.Length ?? m?.Comprimento, "0.##");
            TbWidth.Text = UiHelpers.F(existing.Width ?? m?.Largura, "0.##");
            TbHeight.Text = UiHelpers.F(existing.Height ?? m?.Altura, "0.##");
            TbSpacing.Text = UiHelpers.F(existing.Spacing ?? m?.Espacamento, "0.##");
            TbOffset.Text = UiHelpers.F(existing.Offset, "0.##");
            TbStart.Text = UiHelpers.F(existing.StartOffset, "0.##");
            TbEnd.Text = UiHelpers.F(existing.EndSetback, "0.##");
            TbBase.Text = UiHelpers.F(existing.BaseElevation, "0.##");
            TbRotation.Text = UiHelpers.F(existing.RotationDeg, "0.#");
            UiHelpers.SelectColor(CbColor, existing.Color);
            Output.Load(existing.Output);
        }
        else
        {
            Output.Load(PluginContext.Settings.NewOutput());
            LbTypes.SelectedItem = _cat.Movel(PluginContext.Settings.Get("urban")) ?? _cat.Mobiliario.FirstOrDefault();
        }
        _loading = false;
        UpdatePreview();
    }

    private MobiliarioDef? Item => LbTypes.SelectedItem as MobiliarioDef;

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        var m = Item;
        if (m == null) return;
        TxtDescription.Text = $"{m.Codigo} – {m.Nome}\n\n{m.Descricao}";
        TxtReference.Text = m.Referencia;
        if (_loading && _existing != null) return;
        var was = _loading;
        _loading = true;
        TbLength.Text = UiHelpers.F(m.Comprimento, "0.##");
        TbWidth.Text = UiHelpers.F(m.Largura, "0.##");
        TbHeight.Text = UiHelpers.F(m.Altura, "0.##");
        TbSpacing.Text = UiHelpers.F(m.Espacamento > 0 ? m.Espacamento : 10, "0.##");
        CbColor.SelectedIndex = 0;
        _loading = was;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e)
    {
        if (PanelPath != null) PanelPath.IsEnabled = RbPoint.IsChecked != true;
        UpdatePreview();
    }

    private UrbanElementDefinition BuildDefinition()
    {
        var m = Item ?? throw new FormatException("Selecione um elemento.");
        var d = _existing != null ? (UrbanElementDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new UrbanElementDefinition();
        d.Code = m.Codigo;
        double? Opt(TextBox tb, double def, string name)
        {
            var v = UiHelpers.Parse(tb, def, name, 0.01, 100);
            return Math.Abs(v - def) > 1e-9 ? v : null;
        }
        d.Length = Opt(TbLength, m.Comprimento, "Comprimento");
        d.Width = Opt(TbWidth, m.Largura, "Largura");
        d.Height = Opt(TbHeight, m.Altura, "Altura");
        d.Spacing = UiHelpers.Parse(TbSpacing, m.Espacamento > 0 ? m.Espacamento : 10, "Espaçamento", 0.3, 1000);
        d.Offset = UiHelpers.Parse(TbOffset, 0, "Deslocamento", -100, 100);
        d.BaseElevation = UiHelpers.Parse(TbBase, 0.15, "Nível de assentamento", -5, 50);
        d.StartOffset = UiHelpers.Parse(TbStart, 2, "Início", 0, 10000);
        d.EndSetback = UiHelpers.Parse(TbEnd, 1, "Recuo final", 0, 10000);
        d.RotationDeg = UiHelpers.Parse(TbRotation, 90, "Rotação", -360, 360);
        d.Color = UiHelpers.SelectedColor(CbColor);
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = (UrbanElementDefinition)MarkingDefinition.FromJson(BuildDefinition().ToJson())!;
            var ctx = new BuildContext { Catalog = _cat };
            MarkingGeometry geo;
            IEnumerable<IReadOnlyList<Vec2>>? guides = null;
            if (RbPoint.IsChecked == true && (_existing == null || !_existing.UsePath))
            {
                d.UsePath = false;
                d.Position = Vec2.Zero;
                d.Direction = Vec2.UnitY;
                geo = MarkingBuilder.Build(d, null, ctx);
            }
            else
            {
                var sample = new Polyline2(new[] { new Vec2(0, 0), new Vec2(40, 0) });
                d.UsePath = true;
                geo = MarkingBuilder.Build(d, sample, ctx);
                guides = new[] { sample.Points };
            }
            Preview.Show(geo, guides, null);
            var top = geo.Pieces.Count == 0 ? 0 : geo.Pieces.Max(p => p.Elevation + p.Thickness);
            TxtWarnings.Text = $"{geo.UnitCount} unidade(s); altura máxima {UiHelpers.F(top)} m. " + string.Join("\n", geo.Warnings.Distinct());
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
            PathMode = RbPath.IsChecked == true ? UI.PathMode.Linhas : RbDraw.IsChecked == true ? UI.PathMode.Desenhar : null;
            PluginContext.Settings.Set("urban", Result.Code);
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
