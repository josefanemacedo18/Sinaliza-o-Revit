using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Placas de sinalização vertical (regulamentação, advertência, indicação, obras...).</summary>
public partial class SignWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly SignDefinition? _existing;
    private bool _loading = true;

    public SignDefinition? Result { get; private set; }
    public bool FixedAngle { get; private set; }
    public double AngleDeg { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    private sealed record Option<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    public static string CategoryLabel(CategoriaPlaca c) => c switch
    {
        CategoriaPlaca.Regulamentacao => "Regulamentação",
        CategoriaPlaca.Advertencia => "Advertência",
        CategoriaPlaca.Indicacao => "Indicação",
        CategoriaPlaca.Educativa => "Educativa",
        CategoriaPlaca.Servicos => "Serviços auxiliares",
        CategoriaPlaca.Turistica => "Atrativos turísticos",
        CategoriaPlaca.Obras => "Obras",
        _ => c.ToString(),
    };

    public SignWindow(SignDefinition? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        CbCategory.Items.Add(new Option<CategoriaPlaca?>("Todas as categorias", null));
        foreach (var c in Enum.GetValues<CategoriaPlaca>()) CbCategory.Items.Add(new Option<CategoriaPlaca?>(CategoryLabel(c), c));
        CbSupport.Items.Add(new Option<TipoSuporte>("Coluna simples", TipoSuporte.Simples));
        CbSupport.Items.Add(new Option<TipoSuporte>("Duas colunas", TipoSuporte.Duplo));
        CbSupport.Items.Add(new Option<TipoSuporte>("Sem suporte (fixada em poste/parede)", TipoSuporte.Nenhum));
        CbSupport.Items.Add(new Option<TipoSuporte>("Braço projetado (coluna + braço sobre a pista)", TipoSuporte.BracoProjetado));
        CbSupport.Items.Add(new Option<TipoSuporte>("Semipórtico (coluna + viga em balanço)", TipoSuporte.SemiPortico));
        CbSupport.Items.Add(new Option<TipoSuporte>("Pórtico (duas colunas + viga sobre toda a pista)", TipoSuporte.Portico));
        CbSupport.SelectedIndex = 0;
        CbSupport.SelectionChanged += SupportChanged;
        CbCategory.SelectedIndex = 0;
        Output.Changed += (_, _) => UpdatePreview();

        if (existing != null)
        {
            Title = $"Editar – {existing.Code}";
            BtnOk.Content = "Aplicar";
            GbPlacement.Visibility = Visibility.Collapsed;
            LbTypes.SelectedItem = _cat.Placa(existing.Code);
            var p = _cat.Placa(existing.Code);
            CbSize.Text = UiHelpers.F(existing.Width ?? p?.Largura, "0.###");
            TbHeight.Text = UiHelpers.F(existing.Height ?? p?.Altura, "0.###");
            TbLegend.Text = existing.Legend ?? "";
            TbMount.Text = UiHelpers.F(existing.MountHeight);
            TbPost.Text = UiHelpers.F(existing.PostDiameter, "0.###");
            TbLateral.Text = UiHelpers.F(existing.LateralOffset, "0.##");
            TbBase.Text = UiHelpers.F(existing.BaseElevation, "0.##");
            foreach (var item in CbSupport.Items)
                if (item is Option<TipoSuporte> o && o.Value == existing.Support) CbSupport.SelectedItem = item;
            TbSpan.Text = UiHelpers.F(existing.StructureSpan, "0.##");
            TbColumn.Text = UiHelpers.F(existing.StructureColumn, "0.###");
            TbBeam.Text = UiHelpers.F(existing.StructureBeam, "0.##");
            ShowStructureFields();
            Output.Load(existing.Output);
        }
        else
        {
            Output.Load(PluginContext.Settings.NewOutput());
            LbTypes.SelectedItem = _cat.Placa(PluginContext.Settings.Get("sign")) ?? _cat.Placas.FirstOrDefault();
        }
        _loading = false;
        UpdatePreview();
    }

    private PlacaDef? Plate => LbTypes.SelectedItem as PlacaDef;

    private void CategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        var sel = Plate;
        var cat = (CbCategory.SelectedItem as Option<CategoriaPlaca?>)?.Value;
        var q = TbSearch?.Text?.Trim() ?? "";
        LbTypes.Items.Clear();
        foreach (var p in _cat.Placas.Where(p => (cat == null || p.Categoria == cat) && (q.Length == 0
                     || p.Codigo.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Nome.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || p.Descricao.Contains(q, StringComparison.OrdinalIgnoreCase))))
            LbTypes.Items.Add(p);
        if (sel != null && LbTypes.Items.Contains(sel)) LbTypes.SelectedItem = sel;
        else if (LbTypes.Items.Count > 0) LbTypes.SelectedIndex = 0;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => CategoryChanged(sender, null!);

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        var p = Plate;
        if (p == null) return;
        TxtDescription.Text = $"{p.Codigo} – {p.Nome}\n{CategoryLabel(p.Categoria)}\n\n{p.Descricao}";
        TxtReference.Text = p.Referencia;
        if (_loading && _existing != null) return;
        var was = _loading;
        _loading = true;
        CbSize.Items.Clear();
        foreach (var t in p.Tamanhos.DefaultIfEmpty(p.Largura)) CbSize.Items.Add(UiHelpers.F(t, "0.###"));
        CbSize.Text = UiHelpers.F(p.Largura, "0.###");
        TbHeight.Text = UiHelpers.F(p.Altura, "0.###");
        TbHeight.IsEnabled = p.Forma is FormaPlaca.Retangulo or FormaPlaca.CruzSantoAndre;
        TbLegend.Text = "";
        _loading = was;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private TipoSuporte SelectedSupport => (CbSupport.SelectedItem as Option<TipoSuporte>)?.Value ?? TipoSuporte.Simples;
    private bool OverheadSelected => SelectedSupport is TipoSuporte.BracoProjetado or TipoSuporte.SemiPortico or TipoSuporte.Portico;

    private void ShowStructureFields()
    {
        var v = OverheadSelected ? Visibility.Visible : Visibility.Collapsed;
        LblSpan.Visibility = TbSpan.Visibility = LblColumn.Visibility = TbColumn.Visibility = LblBeam.Visibility = TbBeam.Visibility = v;
    }

    private void SupportChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        ShowStructureFields();
        // Placas aéreas: altura livre mínima de 5,50 m (MBST/DER) e placa deslocada para o meio da pista.
        if (OverheadSelected)
        {
            if (UiHelpers.Parse(TbMount, 2.1, "Altura livre", 0, 20) < 5.5) TbMount.Text = "5,50";
            if (Math.Abs(UiHelpers.Parse(TbLateral, 0, "Deslocamento", -100, 100)) < 0.5)
            {
                var span = UiHelpers.Parse(TbSpan, 12, "Vão", 1, 100);
                TbLateral.Text = UiHelpers.F(SelectedSupport == TipoSuporte.Portico ? span / 2 : span - 2.0, "0.##");
            }
        }
        else if (UiHelpers.Parse(TbMount, 2.1, "Altura livre", 0, 20) > 4) TbMount.Text = "2,10";
    }

    private SignDefinition BuildDefinition()
    {
        var p = Plate ?? throw new FormatException("Selecione uma placa.");
        var d = _existing != null ? (SignDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new SignDefinition();
        d.Code = p.Codigo;
        var w = UiHelpers.ParseOpt(CbSize.SelectedItem as string ?? CbSize.Text) ?? p.Largura;
        if (w < 0.1 || w > 10) throw new FormatException("Largura da placa deve estar entre 0,10 e 10 m.");
        d.Width = Math.Abs(w - p.Largura) > 1e-9 ? w : null;
        var h = UiHelpers.Parse(TbHeight, p.Altura, "Altura", 0.1, 10);
        d.Height = Math.Abs(h - p.Altura) > 1e-9 ? h : null;
        d.Legend = string.IsNullOrWhiteSpace(TbLegend.Text) ? null : TbLegend.Text.Trim();
        d.Support = (CbSupport.SelectedItem as Option<TipoSuporte>)?.Value ?? TipoSuporte.Simples;
        d.MountHeight = UiHelpers.Parse(TbMount, 2.10, "Altura livre", 0, 12);
        d.StructureSpan = UiHelpers.Parse(TbSpan, 12, "Vão da viga", 1, 100);
        d.StructureColumn = UiHelpers.Parse(TbColumn, 0.30, "Coluna da estrutura", 0.1, 1.0);
        d.StructureBeam = UiHelpers.Parse(TbBeam, 0.60, "Altura da viga", 0.15, 2.0);
        d.PostDiameter = UiHelpers.Parse(TbPost, 0.063, "Diâmetro da coluna", 0.02, 0.5);
        d.LateralOffset = UiHelpers.Parse(TbLateral, 0, "Deslocamento", -10, 10);
        d.BaseElevation = UiHelpers.Parse(TbBase, 0.15, "Nível da base", -5, 50);
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            var p = Plate!;
            var w = d.Width ?? p.Largura;
            var h = d.Height ?? p.Altura;
            var geo = new MarkingGeometry();
            foreach (var (shape, color, _) in SignGenerator.Face(p, w, h, d.MountHeight, d.Legend, PluginContext.Glyphs))
                geo.Pieces.Add(new MarkingPiece(shape, color));
            var top = d.MountHeight + SignGenerator.PlateHeight(p.Forma, w, h);
            var post = d.PostDiameter;
            void Post(double x) => geo.Pieces.Insert(0, new MarkingPiece(Polygon2.Rectangle(new Vec2(x - post / 2, 0), new Vec2(x + post / 2, top)), MarkingColor.Metal));
            if (d.Support == TipoSuporte.Simples) Post(-d.LateralOffset);
            else if (d.Support == TipoSuporte.Duplo) { Post(-w / 3); Post(w / 3); }
            var groundHalf = Math.Max(1.2, w);
            if (d.Overhead)
            {
                // Elevação: coluna no bordo (x = −deslocamento), viga avançando sobre a pista até o vão.
                var dir = d.LateralOffset < 0 ? -1.0 : 1.0;
                var x0 = -d.LateralOffset;
                var beamTop = top + 0.10 + d.StructureBeam;
                var c = d.StructureColumn;
                geo.Pieces.Insert(0, new MarkingPiece(Polygon2.Rectangle(new Vec2(x0 - c / 2, 0), new Vec2(x0 + c / 2, beamTop)), MarkingColor.Metal));
                var x1 = x0 + dir * d.StructureSpan;
                geo.Pieces.Insert(0, new MarkingPiece(Polygon2.Rectangle(new Vec2(Math.Min(x0, x1) - c / 2, beamTop - d.StructureBeam), new Vec2(Math.Max(x0, x1) + c / 2, beamTop)), MarkingColor.Metal));
                if (d.Support == TipoSuporte.Portico) geo.Pieces.Insert(0, new MarkingPiece(Polygon2.Rectangle(new Vec2(x1 - c / 2, 0), new Vec2(x1 + c / 2, beamTop)), MarkingColor.Metal));
                foreach (var sg in new[] { -0.35, 0.35 })
                    geo.Pieces.Insert(0, new MarkingPiece(Polygon2.Rectangle(new Vec2(sg * w - 0.03, top), new Vec2(sg * w + 0.03, top + 0.10)), MarkingColor.Metal));
                groundHalf = Math.Max(groundHalf, Math.Max(Math.Abs(x0), Math.Abs(x1)) + 1);
                top = beamTop;
            }
            var ground = Polygon2.Rectangle(new Vec2(-groundHalf, -0.15), new Vec2(groundHalf, 0));
            Preview.Show(geo, null, new[] { ground }, $"Altura total: {UiHelpers.F(top)} m");
            var full = MarkingBuilder.Build(d, null, new BuildContext { Catalog = _cat, Glyphs = PluginContext.Glyphs });
            TxtWarnings.Text = string.Join("\n", full.Warnings.Distinct());
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
            PluginContext.Settings.Set("sign", Result.Code);
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
