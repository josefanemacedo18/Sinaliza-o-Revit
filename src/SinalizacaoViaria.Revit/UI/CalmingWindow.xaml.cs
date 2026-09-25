using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Quebra-molas (ondulações A/B), faixas elevadas e lombadas invertidas.</summary>
public partial class CalmingWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly TrafficCalmingDefinition? _existing;
    private bool _loading = true;

    public TrafficCalmingDefinition? Result { get; private set; }
    public PathMode PathMode { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    private sealed record Option(string Label, TipoModeracao Value)
    {
        public override string ToString() => Label;
    }

    public CalmingWindow(TrafficCalmingDefinition? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        CbType.Items.Add(new Option("Quebra-mola – ondulação transversal tipo A", TipoModeracao.OndulacaoA));
        CbType.Items.Add(new Option("Quebra-mola – ondulação transversal tipo B", TipoModeracao.OndulacaoB));
        CbType.Items.Add(new Option("Faixa elevada para travessia de pedestres", TipoModeracao.FaixaElevada));
        CbType.Items.Add(new Option("Lombada invertida (valeta transversal)", TipoModeracao.LombadaInvertida));
        foreach (var c in UiHelpers.ColorItems()) CbColor.Items.Add(c);
        CbColor.SelectedIndex = 0;
        Output.Changed += (_, _) => UpdatePreview();
        var d = existing ?? new TrafficCalmingDefinition();
        foreach (var item in CbType.Items)
            if (item is Option o && o.Value == d.Type) CbType.SelectedItem = item;
        Fill(d.Type, d);
        Output.Load(existing?.Output ?? PluginContext.Settings.NewOutput());
        if (existing != null) { Title = "Editar dispositivo de moderação"; BtnOk.Content = "Aplicar"; GbPath.Visibility = Visibility.Collapsed; }
        _loading = false;
        UpdatePreview();
    }

    private TipoModeracao Type => (CbType.SelectedItem as Option)?.Value ?? TipoModeracao.OndulacaoA;

    private void Fill(TipoModeracao t, TrafficCalmingDefinition? d = null)
    {
        var (l, h, r) = TrafficCalmingGenerator.Defaults(t);
        TbLength.Text = UiHelpers.F(d?.Length ?? l);
        TbHeight.Text = UiHelpers.F(d?.Height ?? h);
        TbRamp.Text = UiHelpers.F(d?.RampLength ?? r);
        TbRamp.IsEnabled = t == TipoModeracao.FaixaElevada;
        CkCrosswalk.IsEnabled = t == TipoModeracao.FaixaElevada;
        if (d != null)
        {
            CkMarking.IsChecked = d.Marking;
            CkCrosswalk.IsChecked = d.Crosswalk;
            UiHelpers.SelectColor(CbColor, d.MarkingColor);
        }
    }

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _loading = true;
        Fill(Type);
        _loading = false;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private TrafficCalmingDefinition BuildDefinition()
    {
        var d = _existing != null ? (TrafficCalmingDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new TrafficCalmingDefinition();
        d.Type = Type;
        var (l, h, r) = TrafficCalmingGenerator.Defaults(d.Type);
        var len = UiHelpers.Parse(TbLength, l, "Extensão", 0.3, 30);
        var hh = UiHelpers.Parse(TbHeight, h, "Altura", 0.01, 0.5);
        var rr = UiHelpers.Parse(TbRamp, r, "Rampa", 0, 10);
        d.Length = Math.Abs(len - l) > 1e-9 ? len : null;
        d.Height = Math.Abs(hh - h) > 1e-9 ? hh : null;
        d.RampLength = Math.Abs(rr - r) > 1e-9 ? rr : null;
        d.Marking = CkMarking.IsChecked == true;
        d.MarkingColor = UiHelpers.SelectedColor(CbColor);
        d.Crosswalk = CkCrosswalk.IsChecked == true;
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            var path = new Polyline2(new[] { Vec2.Zero, new Vec2(0, 7) });
            var geo = MarkingBuilder.Build(d, path, new BuildContext { Catalog = _cat });
            var road = Polygon2.Rectangle(new Vec2(-8, 0), new Vec2(8, 7));
            Preview.Show(geo, null, new[] { road });
            var top = geo.Pieces.Where(p => p.Profile != null).Select(p => p.Profile!.Profile.Bounds).ToList();
            var info = top.Count == 0 ? "" : $"Altura máxima {UiHelpers.F(top.Max(b => b.Max.Y))} m; mínima {UiHelpers.F(top.Min(b => b.Min.Y))} m.";
            TxtWarnings.Text = info + "\nDimensões padrão conforme as resoluções do CONTRAN sobre ondulações transversais e faixas elevadas – confira a versão vigente e a sinalização vertical obrigatória (A-18 e placas de velocidade)." +
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
            PathMode = RbCurves.IsChecked == true ? PathMode.Linhas : PathMode.DoisPontos;
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
