using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Zebrados (ZPA), chevrons, quadriculados e faixas de lombada.</summary>
public partial class HatchWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly HatchMarkingDefinition? _existing;
    private bool _loading = true;

    public HatchMarkingDefinition? Result { get; private set; }
    public PathMode PathMode { get; private set; }
    public bool PickReferenceDirection { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    public HatchWindow(HatchMarkingDefinition? existing = null, string? initialCode = null)
    {
        InitializeComponent();
        _existing = existing;
        foreach (var h in _cat.Hachuras) LbTypes.Items.Add(h);
        foreach (var c in UiHelpers.ColorItems()) { CbBarColor.Items.Add(c); CbBorderColor.Items.Add(c); }
        CbBarColor.SelectedIndex = 0;
        CbBorderColor.SelectedIndex = 0;
        foreach (var (l, v) in new[] { ("Para dentro do contorno (anel interno)", LadoFaixa.Interno), ("Para fora do contorno (anel externo)", LadoFaixa.Externo),
                     ("Centrada na linha", LadoFaixa.Centro), ("À esquerda do sentido da linha", LadoFaixa.Esquerda), ("À direita do sentido da linha", LadoFaixa.Direita) })
            CbStripSide.Items.Add(new SideItem(l, v));
        CbStripSide.SelectedIndex = 0;
        Output.Changed += (_, _) => UpdatePreview();

        if (existing != null)
        {
            Title = $"Editar – {existing.Code}";
            BtnOk.Content = "Aplicar";
            GbPath.Visibility = Visibility.Collapsed;
            LbTypes.SelectedItem = _cat.Hachura(existing.Code);
            Fill(existing);
            CkOverlay.IsChecked = existing.Overlay;
            Output.Load(existing.Output);
        }
        else
        {
            Output.Load(PluginContext.Settings.NewOutput());
            LbTypes.SelectedItem = _cat.Hachura(initialCode ?? PluginContext.Settings.Get("hatch")) ?? _cat.Hachuras.FirstOrDefault();
        }
        _loading = false;
        UpdatePreview();
    }

    private sealed record SideItem(string Label, LadoFaixa Value)
    {
        public override string ToString() => Label;
    }

    private HachuraDef? Preset => LbTypes.SelectedItem as HachuraDef;

    /// <summary>Faixa (anel) ao longo do contorno em vez de área preenchida.</summary>
    public bool IsStrip => UiHelpers.ParseOpt(TbStrip?.Text) is > 0.01;

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        var p = Preset;
        if (p == null) return;
        TxtDescription.Text = $"{p.Codigo} – {p.Nome}\n\n{p.Descricao}";
        TxtReference.Text = p.Referencia;
        if (_loading && _existing != null) return;
        var was = _loading;
        _loading = true;
        TbBar.Text = UiHelpers.F(p.LarguraBarra, "0.###");
        TbGap.Text = UiHelpers.F(p.Espacamento, "0.###");
        TbAngle.Text = UiHelpers.F(p.Angulo, "0.#");
        TbBorder.Text = UiHelpers.F(p.LarguraBorda, "0.###");
        CkChevron.IsChecked = p.Chevron;
        CkCrossed.IsChecked = p.Cruzado;
        CbBarColor.SelectedIndex = 0;
        CbBorderColor.SelectedIndex = 0;
        _loading = was;
        UpdatePreview();
    }

    private void Fill(HatchMarkingDefinition d)
    {
        var p = _cat.Hachura(d.Code);
        TbBar.Text = UiHelpers.F(d.BarWidth ?? p?.LarguraBarra, "0.###");
        TbGap.Text = UiHelpers.F(d.Gap ?? p?.Espacamento, "0.###");
        TbAngle.Text = UiHelpers.F(d.AngleDeg ?? p?.Angulo, "0.#");
        TbBorder.Text = UiHelpers.F(d.BorderWidth ?? p?.LarguraBorda, "0.###");
        TbPhase.Text = UiHelpers.F(d.Phase, "0.###");
        CkChevron.IsChecked = d.Chevron ?? p?.Chevron;
        CkCrossed.IsChecked = d.Crossed ?? p?.Cruzado;
        UiHelpers.SelectColor(CbBarColor, d.BarColor);
        UiHelpers.SelectColor(CbBorderColor, d.BorderColor);
        TbStrip.Text = UiHelpers.F(d.StripWidth ?? 0, "0.##");
        foreach (var it in CbStripSide.Items) if (it is SideItem si && si.Value == d.StripSide) CbStripSide.SelectedItem = it;
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private HatchMarkingDefinition BuildDefinition()
    {
        var p = Preset ?? throw new FormatException("Selecione um tipo.");
        var d = _existing != null ? (HatchMarkingDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new HatchMarkingDefinition();
        d.Code = p.Codigo;
        d.BarWidth = UiHelpers.Parse(TbBar, p.LarguraBarra, "Largura da barra", 0.01, 5);
        d.Gap = UiHelpers.Parse(TbGap, p.Espacamento, "Espaço", 0, 50);
        d.AngleDeg = UiHelpers.Parse(TbAngle, p.Angulo, "Ângulo", -180, 180);
        d.BorderWidth = UiHelpers.Parse(TbBorder, p.LarguraBorda, "Contorno", 0, 2);
        d.Phase = UiHelpers.Parse(TbPhase, 0, "Deslocamento", -100, 100);
        d.Chevron = CkChevron.IsChecked == true;
        d.Crossed = CkCrossed.IsChecked == true;
        d.BarColor = UiHelpers.SelectedColor(CbBarColor);
        d.BorderColor = UiHelpers.SelectedColor(CbBorderColor);
        var strip = UiHelpers.Parse(TbStrip, 0, "Largura da faixa", 0, 50);
        d.StripWidth = strip > 0.01 ? strip : null;
        d.StripSide = (CbStripSide.SelectedItem as SideItem)?.Value ?? LadoFaixa.Interno;
        d.Overlay = CkOverlay.IsChecked == true;
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            // Área típica de afunilamento (nariz) com 30 m.
            var sample = new Polyline2(new[] { new Vec2(0, 0), new Vec2(30, 0), new Vec2(30, 3.5), new Vec2(8, 1.2) }, closed: true);
            var probe = (HatchMarkingDefinition)MarkingDefinition.FromJson(d.ToJson())!;
            probe.ReferenceDirection ??= Vec2.UnitX;
            Core.Model.MarkingGeometry geo;
            Polygon2 pav;
            if (probe.IsStrip)
            {
                // Anel em volta de uma ilha circular de exemplo (raio 8 m).
                var ring = new Polyline2(CurveTools.Circle(new Vec2(15, 2), 8, 0.02), closed: true);
                probe.ReferenceDirection = null;
                geo = MarkingBuilder.Build(probe, ring, new BuildContext { Catalog = _cat });
                pav = Polygon2.Rectangle(new Vec2(-3, -12), new Vec2(33, 16));
                Preview.Show(geo, new[] { ring.Points }, new[] { pav });
            }
            else
            {
                geo = MarkingBuilder.Build(probe, sample, new BuildContext { Catalog = _cat });
                pav = Polygon2.Rectangle(new Vec2(-3, -4), new Vec2(33, 7.5));
                Preview.Show(geo, new[] { sample.Points }, new[] { pav });
            }
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
            PathMode = RbDraw.IsChecked == true ? PathMode.Desenhar : RbEdges.IsChecked == true ? PathMode.Bordas : PathMode.Linhas;
            PickReferenceDirection = CkRefDir.IsChecked == true;
            PluginContext.Settings.Set("hatch", Result.Code);
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
