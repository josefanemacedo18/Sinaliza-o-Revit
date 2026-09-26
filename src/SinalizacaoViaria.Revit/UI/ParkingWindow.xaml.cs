using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Estacionamento regulamentado: vagas comuns, especiais e de parada de veículos específicos.</summary>
public partial class ParkingWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly ParkingMarkingDefinition? _existing;
    private bool _loading = true;

    public ParkingMarkingDefinition? Result { get; private set; }
    public PathMode PathMode { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    public ParkingWindow(ParkingMarkingDefinition? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        foreach (var v in _cat.Vagas) LbTypes.Items.Add(v);
        foreach (var c in UiHelpers.ColorItems()) CbColor.Items.Add(c);
        foreach (var c in UiHelpers.ColorItems("Sem pintura de fundo")) CbFill.Items.Add(c);
        CbColor.SelectedIndex = 0;
        CbFill.SelectedIndex = 0;
        Output.Changed += (_, _) => UpdatePreview();

        if (existing != null)
        {
            Title = $"Editar – {existing.Code}";
            BtnOk.Content = "Aplicar";
            GbPath.Visibility = Visibility.Collapsed;
            LbTypes.SelectedItem = _cat.Vaga(existing.Code);
            var p = _cat.Vaga(existing.Code);
            TbAngle.Text = UiHelpers.F(existing.Angle ?? p?.Angulo, "0.#");
            TbWidth.Text = UiHelpers.F(existing.StallWidth ?? p?.Largura, "0.##");
            TbLength.Text = UiHelpers.F(existing.StallLength ?? p?.Comprimento, "0.##");
            TbLine.Text = UiHelpers.F(existing.LineWidth ?? p?.LarguraLinha, "0.###");
            TbCount.Text = existing.Count.ToString();
            RbLeft.IsChecked = !existing.RightSide;
            TbStart.Text = UiHelpers.F(existing.StartOffset, "0.##");
            TbCurb.Text = UiHelpers.F(existing.CurbOffset, "0.##");
            CkFlip.IsChecked = existing.FlipAngle;
            CkBack.IsChecked = existing.BackLine;
            CkOutline.IsChecked = existing.FullOutline;
            CkSymbols.IsChecked = existing.IncludeSymbols;
            UiHelpers.SelectColor(CbColor, existing.Color);
            UiHelpers.SelectColor(CbFill, existing.FillColor);
            Output.Load(existing.Output);
        }
        else
        {
            Output.Load(PluginContext.Settings.NewOutput());
            LbTypes.SelectedItem = _cat.Vaga(PluginContext.Settings.Get("parking")) ?? _cat.Vagas.FirstOrDefault();
        }
        _loading = false;
        UpdatePreview();
    }

    private VagaDef? Preset => LbTypes.SelectedItem as VagaDef;

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        var p = Preset;
        if (p == null) return;
        TxtReference.Text = $"{p.Nome}\n{p.Descricao}\n{p.Referencia}";
        if (_loading && _existing != null) return;
        var was = _loading;
        _loading = true;
        TbAngle.Text = UiHelpers.F(p.Angulo, "0.#");
        TbWidth.Text = UiHelpers.F(p.Largura, "0.##");
        TbLength.Text = UiHelpers.F(p.Comprimento, "0.##");
        TbLine.Text = UiHelpers.F(p.LarguraLinha, "0.###");
        CbColor.SelectedIndex = 0;
        _loading = was;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private ParkingMarkingDefinition BuildDefinition()
    {
        var p = Preset ?? throw new FormatException("Selecione um tipo de vaga.");
        var d = _existing != null ? (ParkingMarkingDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new ParkingMarkingDefinition();
        d.Code = p.Codigo;
        d.Angle = UiHelpers.Parse(TbAngle, p.Angulo, "Ângulo", 0, 90);
        d.StallWidth = UiHelpers.Parse(TbWidth, p.Largura, "Largura da vaga", 0.5, 20);
        d.StallLength = UiHelpers.Parse(TbLength, p.Comprimento, "Comprimento da vaga", 0.5, 40);
        d.LineWidth = UiHelpers.Parse(TbLine, p.LarguraLinha, "Largura da linha", 0.02, 1);
        d.Count = (int)UiHelpers.Parse(TbCount, 0, "Quantidade", 0, 1000);
        d.RightSide = RbRight.IsChecked == true;
        d.StartOffset = UiHelpers.Parse(TbStart, 0, "Recuo inicial", 0, 10000);
        d.CurbOffset = UiHelpers.Parse(TbCurb, 0, "Afastamento do meio-fio", -10, 10);
        d.FlipAngle = CkFlip.IsChecked == true;
        d.BackLine = CkBack.IsChecked == true;
        d.FullOutline = CkOutline.IsChecked == true;
        d.IncludeSymbols = CkSymbols.IsChecked == true;
        d.Color = UiHelpers.SelectedColor(CbColor);
        d.FillColor = UiHelpers.SelectedColor(CbFill);
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            var curb = new Polyline2(new[] { new Vec2(0, 0), new Vec2(30, 0) });
            var geo = MarkingBuilder.Build(d, curb, new BuildContext { Catalog = _cat, Glyphs = PluginContext.Glyphs });
            var side = d.RightSide ? -1 : 1;
            var pav = Polygon2.Rectangle(new Vec2(-1, Math.Min(0, side * 9)), new Vec2(31, Math.Max(0, side * 9)));
            Preview.Show(geo, new[] { curb.Points }, new[] { pav }, "Meio-fio em magenta (sentido →)");
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
            PluginContext.Settings.Set("parking", Result.Code);
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
