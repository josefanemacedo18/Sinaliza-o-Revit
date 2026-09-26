using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Segregadores, balizadores, pilaretes, prismas, barreiras e defensas ao longo de um caminho.</summary>
public partial class DeviceWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly DeviceMarkingDefinition? _existing;
    private bool _loading = true;

    public DeviceMarkingDefinition? Result { get; private set; }
    public PathMode PathMode { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    public DeviceWindow(DeviceMarkingDefinition? existing = null, string? initialCode = null)
    {
        InitializeComponent();
        _existing = existing;
        // Lista única, agrupada por família (segregadores, balizadores, barreiras, defensas/guard rail, gradis, obras).
        var view = new System.Windows.Data.ListCollectionView(_cat.Dispositivos.GroupBy(d => d.Codigo, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(d => d.Familia).ToList());
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(DispositivoDef.Familia)));
        LbTypes.ItemsSource = view;
        foreach (var c in UiHelpers.ColorItems()) CbColor.Items.Add(c);
        CbColor.SelectedIndex = 0;
        Output.Changed += (_, _) => UpdatePreview();

        if (existing != null)
        {
            Title = $"Editar – {existing.Code}";
            BtnOk.Content = "Aplicar";
            GbPath.Visibility = Visibility.Collapsed;
            LbTypes.SelectedItem = _cat.Dispositivo(existing.Code);
            var d = _cat.Dispositivo(existing.Code);
            TbLength.Text = UiHelpers.F(existing.LengthOverride ?? d?.Comprimento, "0.###");
            TbWidth.Text = UiHelpers.F(existing.WidthOverride ?? d?.Largura, "0.###");
            TbHeight.Text = UiHelpers.F(existing.HeightOverride ?? d?.Altura, "0.###");
            TbSpacing.Text = UiHelpers.F(existing.Spacing ?? d?.Espacamento, "0.##");
            TbOffset.Text = UiHelpers.F(existing.Offset, "0.###");
            TbStart.Text = UiHelpers.F(existing.StartSetback, "0.##");
            TbEnd.Text = UiHelpers.F(existing.EndSetback, "0.##");
            CkReverse.IsChecked = existing.Reverse;
            UiHelpers.FillJustify(CbJustify, existing.Justify);
            UiHelpers.SelectColor(CbColor, existing.Color);
            Output.Load(existing.Output);
        }
        else
        {
            Output.Load(PluginContext.Settings.NewOutput());
            UiHelpers.FillJustify(CbJustify, Enum.TryParse<Justificacao>(PluginContext.Settings.Get("device:pos"), out var jl) ? jl : Justificacao.Centro);
            LbTypes.SelectedItem = _cat.Dispositivo(initialCode ?? PluginContext.Settings.Get("device")) ?? _cat.Dispositivos.FirstOrDefault();
        }
        _loading = false;
        UpdatePreview();
    }

    private DispositivoDef? Device => LbTypes.SelectedItem as DispositivoDef;

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        var d = Device;
        if (d == null) return;
        TxtDescription.Text = $"{d.Codigo} – {d.Nome}\n\n{d.Descricao}";
        TxtReference.Text = d.Referencia;
        if (_loading && _existing != null) return;
        var was = _loading;
        _loading = true;
        TbLength.Text = UiHelpers.F(d.Comprimento, "0.###");
        TbWidth.Text = UiHelpers.F(d.Largura, "0.###");
        TbHeight.Text = UiHelpers.F(d.Altura, "0.###");
        TbSpacing.Text = UiHelpers.F(d.Espacamento, "0.##");
        CbColor.SelectedIndex = 0;
        _loading = was;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private DeviceMarkingDefinition BuildDefinition()
    {
        var d = Device ?? throw new FormatException("Selecione um dispositivo.");
        var r = _existing != null ? (DeviceMarkingDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new DeviceMarkingDefinition();
        r.Code = d.Codigo;
        var length = UiHelpers.Parse(TbLength, d.Comprimento, "Comprimento", 0.01, 50);
        var width = UiHelpers.Parse(TbWidth, d.Largura, "Largura", 0.01, 10);
        var height = UiHelpers.Parse(TbHeight, d.Altura, "Altura", 0.005, 10);
        var spacing = UiHelpers.Parse(TbSpacing, d.Espacamento, "Espaçamento", 0, 1000);
        r.LengthOverride = Math.Abs(length - d.Comprimento) > 1e-9 ? length : null;
        r.WidthOverride = Math.Abs(width - d.Largura) > 1e-9 ? width : null;
        r.HeightOverride = Math.Abs(height - d.Altura) > 1e-9 ? height : null;
        r.Spacing = Math.Abs(spacing - d.Espacamento) > 1e-9 ? spacing : null;
        if (spacing > 0 && spacing < length) throw new FormatException("O espaçamento entre centros deve ser maior que o comprimento do dispositivo.");
        r.Offset = UiHelpers.Parse(TbOffset, 0, "Deslocamento lateral", -200, 200);
        r.StartSetback = UiHelpers.Parse(TbStart, 0, "Recuo inicial", 0, 10000);
        r.EndSetback = UiHelpers.Parse(TbEnd, 0, "Recuo final", 0, 10000);
        r.Reverse = CkReverse.IsChecked == true;
        r.Color = UiHelpers.SelectedColor(CbColor);
        r.Justify = UiHelpers.SelectedJustify(CbJustify);
        if (_existing != null && r.Justify == Justificacao.Clique) r.Justify = _existing.Justify == Justificacao.Clique ? Justificacao.Centro : _existing.Justify;
        r.Output = Output.Save(r.Output);
        return r;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            var sample = new Polyline2(new[] { new Vec2(0, 0), new Vec2(20, 0) });
            if (d.Justify == Justificacao.Clique) d.Justify = Justificacao.Esquerda;   // prévia: um dos lados
            var geo = MarkingBuilder.Build(d, sample, new BuildContext { Catalog = _cat });
            var pav = Polygon2.Rectangle(new Vec2(-1, -3), new Vec2(21, 3));
            Preview.Show(geo, new[] { sample.Points }, new[] { pav });
            var top = geo.Pieces.Count == 0 ? 0 : geo.Pieces.Max(p => p.Elevation + p.Thickness);
            var info = geo.UnitCount > 0 ? $"{geo.UnitCount} unidade(s) em 20 m" : $"{UiHelpers.F(geo.PaintedLength)} m contínuos";
            TxtWarnings.Text = $"{info}; altura máxima {UiHelpers.F(top)} m." + (geo.Warnings.Count > 0 ? "\n" + string.Join("\n", geo.Warnings.Distinct()) : "");
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
            PathMode = RbDraw.IsChecked == true ? PathMode.Desenhar : RbTwoPoints.IsChecked == true ? PathMode.DoisPontos
                : RbEdges.IsChecked == true ? PathMode.Bordas : PathMode.Linhas;
            PluginContext.Settings.Set("device", Result.Code);
            PluginContext.Settings.Set("device:pos", Result.Justify.ToString());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
