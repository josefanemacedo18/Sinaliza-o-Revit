using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Como o caminho de uma nova marca é obtido.</summary>
public enum PathMode
{
    Linhas,
    Desenhar,
    DoisPontos,
    /// <summary>Junto ao bordo de uma via existente (montagem da via passo a passo).</summary>
    BordoDaVia,
    /// <summary>Arestas de pisos, calçadas, lajes, topografia ou outros elementos (associativo).</summary>
    Bordas,
}

/// <summary>Janela genérica para marcas lineares (longitudinais, transversais, tachas, piso tátil, ciclovia).</summary>
public partial class LinearWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly List<TipoLinearDef> _types;
    private readonly LinearMarkingDefinition? _existing;
    private readonly string _settingsKey;
    private bool _loading = true;

    public LinearMarkingDefinition? Result { get; private set; }
    public PathMode PathMode { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    public LinearWindow(string title, GrupoMarca[] groups, LinearMarkingDefinition? existing = null, PathMode defaultMode = PathMode.Linhas)
    {
        InitializeComponent();
        Title = existing == null ? title : $"Editar – {existing.Code}";
        TxtTitle.Text = title;
        _existing = existing;
        _settingsKey = "linear:" + string.Join(",", groups);
        _types = _cat.LinearesDoGrupo(groups).ToList();
        if (existing != null && _types.All(t => t.Codigo != existing.Code) && _cat.Linear(existing.Code) is { } extra) _types.Insert(0, extra);

        foreach (var c in UiHelpers.ColorItems()) CbColor.Items.Add(c);
        CbColor.SelectedIndex = 0;
        CbAlign.Items.Add("Conforme catálogo");
        foreach (var a in Enum.GetValues<AlinhamentoPadrao>()) CbAlign.Items.Add(a);
        CbAlign.SelectedIndex = 0;
        TbSpeed.Text = UiHelpers.F(PluginContext.Settings.DefaultSpeed, "0");
        UiHelpers.FillJustify(CbJustify, Enum.TryParse<Justificacao>(PluginContext.Settings.Get(_settingsKey + ":pos"), out var jl) ? jl : Justificacao.Centro);
        Output.Changed += (_, _) => UpdatePreview();

        FillList("");
        if (existing != null)
        {
            BtnOk.Content = "Aplicar";
            GbPath.Visibility = Visibility.Collapsed;
            LoadDefinition(existing);
        }
        else
        {
            Output.Load(PluginContext.Settings.NewOutput());
            RbCurves.IsChecked = defaultMode == PathMode.Linhas;
            RbDraw.IsChecked = defaultMode == PathMode.Desenhar;
            RbTwoPoints.IsChecked = defaultMode == PathMode.DoisPontos;
            RbRoadEdge.IsChecked = defaultMode == PathMode.BordoDaVia;
            RbEdges.IsChecked = defaultMode == PathMode.Bordas;
            var last = PluginContext.Settings.Get(_settingsKey);
            LbTypes.SelectedItem = _types.FirstOrDefault(t => t.Codigo == last) ?? _types.FirstOrDefault();
        }
        _loading = false;
        UpdatePreview();
    }

    private TipoLinearDef? SelectedType => LbTypes.SelectedItem as TipoLinearDef;
    private VarianteDef? SelectedVariant => CbVariant.SelectedItem as VarianteDef;

    private void FillList(string filter)
    {
        var sel = SelectedType;
        LbTypes.Items.Clear();
        foreach (var t in _types.Where(t => filter.Length == 0 ||
                     t.Codigo.Contains(filter, StringComparison.OrdinalIgnoreCase) || t.Nome.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            LbTypes.Items.Add(t);
        if (sel != null && LbTypes.Items.Contains(sel)) LbTypes.SelectedItem = sel;
    }

    private void LoadDefinition(LinearMarkingDefinition d)
    {
        LbTypes.SelectedItem = _types.FirstOrDefault(t => t.Codigo == d.Code);
        var type = SelectedType;
        if (type != null) CbVariant.SelectedItem = type.Variantes.FirstOrDefault(v => v.Nome == d.Variant) ?? type.Variantes.FirstOrDefault();
        CkBySpeed.IsChecked = d.Speed.HasValue && d.Variant == null;
        if (d.Speed.HasValue) TbSpeed.Text = UiHelpers.F(d.Speed.Value, "0");
        TbOffset.Text = UiHelpers.F(d.Offset, "0.###");
        TbPhase.Text = UiHelpers.F(d.Phase, "0.###");
        TbStart.Text = UiHelpers.F(d.StartSetback, "0.###");
        TbEnd.Text = UiHelpers.F(d.EndSetback, "0.###");
        TbWidth.Text = UiHelpers.F(d.WidthOverride, "0.###");
        if (d.PatternOverride is { Length: >= 2 } p)
        {
            TbDash.Text = UiHelpers.F(p[0], "0.###");
            TbGap.Text = UiHelpers.F(p[1], "0.###");
        }
        CkReverse.IsChecked = d.Reverse;
        CkInvert.IsChecked = d.InvertSides;
        if (d.Depth is { } dp) TbDepth.Text = UiHelpers.F(dp, "0.###");
        UiHelpers.FillJustify(CbJustify, d.Justify);
        UiHelpers.SelectColor(CbColor, d.ColorOverride);
        CbAlign.SelectedItem = d.Alignment.HasValue ? d.Alignment.Value : CbAlign.Items[0];
        Output.Load(d.Output);
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => FillList(TbSearch.Text.Trim());

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        var t = SelectedType;
        CbVariant.Items.Clear();
        if (t == null) return;
        foreach (var v in t.Variantes) CbVariant.Items.Add(v);
        CbVariant.DisplayMemberPath = nameof(VarianteDef.Nome);
        CbVariant.SelectedIndex = 0;
        TxtDescription.Text = $"{t.Codigo} – {t.Nome}\n\n{t.Descricao}";
        TxtReference.Text = t.Referencia + (t.LarguraMax > 0 ? $"\nLargura de referência: {UiHelpers.F(t.LarguraMin)} a {UiHelpers.F(t.LarguraMax)} m" : "");
        var hasVelocity = t.Variantes.Any(v => v.VelocidadeMax > 0);
        CkBySpeed.IsEnabled = hasVelocity;
        if (!_loading && _existing == null) CkBySpeed.IsChecked = hasVelocity;
        CkInvert.IsEnabled = t.Variantes.Any(v => v.Faixas.Any(f => Math.Abs(f.Deslocamento) > 1e-9));
        if (_existing == null && t.Transversal) RbTwoPoints.IsChecked = true;
        var sj = t.Codigo == "SARJETAO";
        LblDepth.Visibility = TbDepth.Visibility = sj ? Visibility.Visible : Visibility.Collapsed;
        if (sj && _existing == null) RbTwoPoints.IsChecked = true;   // atravessa a via: dois cliques de sarjeta a sarjeta
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private LinearMarkingDefinition BuildDefinition()
    {
        var t = SelectedType ?? throw new FormatException("Selecione um tipo de marca.");
        var d = _existing != null ? (LinearMarkingDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new LinearMarkingDefinition();
        d.Code = t.Codigo;
        var bySpeed = CkBySpeed.IsChecked == true && CkBySpeed.IsEnabled;
        d.Speed = bySpeed ? UiHelpers.Parse(TbSpeed, 60, "Velocidade", 10, 200) : null;
        d.Variant = bySpeed ? null : SelectedVariant?.Nome;
        d.Offset = UiHelpers.Parse(TbOffset, 0, "Deslocamento lateral", -200, 200);
        d.Phase = UiHelpers.Parse(TbPhase, 0, "Fase", -1000, 1000);
        d.StartSetback = UiHelpers.Parse(TbStart, 0, "Recuo inicial", 0, 10000);
        d.EndSetback = UiHelpers.Parse(TbEnd, 0, "Recuo final", 0, 10000);
        d.WidthOverride = UiHelpers.ParseNullable(TbWidth, "Largura", 0.01, 50);
        var dash = UiHelpers.ParseNullable(TbDash, "Traço", 0.01, 1000);
        var gap = UiHelpers.ParseNullable(TbGap, "Espaço", 0, 1000);
        d.PatternOverride = dash.HasValue && gap.HasValue ? new[] { dash.Value, gap.Value } : null;
        d.Reverse = CkReverse.IsChecked == true;
        d.InvertSides = CkInvert.IsChecked == true;
        d.ColorOverride = UiHelpers.SelectedColor(CbColor);
        d.Alignment = CbAlign.SelectedItem is AlinhamentoPadrao a ? a : null;
        d.Depth = t.Codigo == "SARJETAO" ? UiHelpers.Parse(TbDepth, 0.05, "Flecha do sarjetão", 0, 0.25) : null;
        d.Justify = UiHelpers.SelectedJustify(CbJustify);
        if (_existing != null && d.Justify == Justificacao.Clique) d.Justify = _existing.Justify == Justificacao.Clique ? Justificacao.Centro : _existing.Justify;
        d.Output = Output.Save(d.Output);
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            var t = SelectedType!;
            var variant = MarkingBuilder.ResolveVariant(t, d.Variant, d.Speed);
            TxtPattern.Text = variant == null ? "" : "Variante aplicada: " + variant.Nome + (variant.Observacao != null ? $" – {variant.Observacao}" : "");

            Polyline2 sample;
            var pavement = new List<Polygon2>();
            if (t.Transversal)
            {
                sample = new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 7) });
                pavement.Add(Polygon2.Rectangle(new Vec2(-7, 0), new Vec2(7, 7)));
            }
            else
            {
                var pts = new List<Vec2> { new(-15, 0), new(0, 0) };
                pts.AddRange(CurveTools.Arc(new Vec2(0, 30), 30, -Math.PI / 2, 0.8).Skip(1));
                sample = new Polyline2(pts);
                pavement.AddRange(PolygonOps.Strip(sample.Points, 9 + Math.Abs(d.Offset) * 2));
            }
            if (d.Justify == Justificacao.Clique) d.Justify = Justificacao.Esquerda;   // prévia: um dos lados
            var geo = MarkingBuilder.Build(d, sample, new BuildContext { Catalog = _cat });
            Preview.Show(geo, new[] { sample.Points }, pavement);
            TxtWarnings.Text = string.Join("\n", geo.Warnings.Distinct());
        }
        catch (FormatException ex)
        {
            TxtWarnings.Text = ex.Message;
        }
        catch (Exception ex)
        {
            TxtWarnings.Text = "Erro na pré-visualização: " + ex.Message;
        }
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Result = BuildDefinition();
            PathMode = RbDraw.IsChecked == true ? PathMode.Desenhar : RbTwoPoints.IsChecked == true ? PathMode.DoisPontos
                : RbRoadEdge.IsChecked == true ? PathMode.BordoDaVia : RbEdges.IsChecked == true ? PathMode.Bordas : PathMode.Linhas;
            PluginContext.Settings.Set(_settingsKey, Result.Code);
            PluginContext.Settings.Set(_settingsKey + ":pos", Result.Justify.ToString());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
