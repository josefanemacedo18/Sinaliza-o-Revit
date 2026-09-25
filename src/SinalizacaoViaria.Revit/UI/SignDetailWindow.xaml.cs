using System.Windows;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Opções do detalhe de placas em planta (símbolo ampliado + chamada + código).</summary>
public partial class SignDetailWindow : Window
{
    public enum Scope { Selecionadas, NaVista, Todas }

    private sealed record Option<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    private static readonly (string Label, Vec2 Dir)[] Directions =
    {
        ("Acima (norte da vista)", new Vec2(0, 1)), ("Acima à direita", new Vec2(1, 1)), ("À direita", new Vec2(1, 0)),
        ("Abaixo à direita", new Vec2(1, -1)), ("Abaixo", new Vec2(0, -1)), ("Abaixo à esquerda", new Vec2(-1, -1)),
        ("À esquerda", new Vec2(-1, 0)), ("Acima à esquerda", new Vec2(-1, 1)),
    };

    private readonly SignPlanDetailDefinition? _existing;
    private readonly SignDefinition _sample;
    private readonly double _viewScale;
    private readonly bool _loading;

    public SignPlanDetailDefinition? Result { get; private set; }
    public Scope SelectedScope => (CbScope.SelectedItem as Option<Scope>)?.Value ?? Scope.NaVista;

    /// <param name="sample">Placa usada na pré-visualização.</param>
    public SignDetailWindow(double viewScale, SignDefinition? sample, SignPlanDetailDefinition? existing = null, int selectedCount = 0)
    {
        _loading = true;
        InitializeComponent();
        _existing = existing;
        _viewScale = viewScale > 0 ? viewScale : 100;
        _sample = sample ?? new SignDefinition { Code = "R-1" };
        Preview.Paper = true;
        Preview.ViewScale = _viewScale;
        TxtPreview.Text = $"Pré-visualização – escala da vista 1:{_viewScale:0}";

        if (existing == null)
        {
            if (selectedCount > 0) CbScope.Items.Add(new Option<Scope>($"Placas selecionadas ({selectedCount})", Scope.Selecionadas));
            CbScope.Items.Add(new Option<Scope>("Todas as placas visíveis na vista", Scope.NaVista));
            CbScope.Items.Add(new Option<Scope>("Todas as placas do projeto", Scope.Todas));
            CbScope.SelectedIndex = 0;
        }
        else
        {
            CbScope.Visibility = Visibility.Collapsed;
            LbScope.Visibility = Visibility.Collapsed;
            Title = "Editar detalhe de placa";
            BtnOk.Content = "Aplicar";
        }
        foreach (var d in Directions) CbDirection.Items.Add(new Option<Vec2>(d.Label, d.Dir));

        var def = existing ?? UiHelpers.Remembered<SignPlanDetailDefinition>("DetalhePlaca") ?? new SignPlanDetailDefinition();
        TbSymbol.Text = UiHelpers.F(def.SymbolMm, "0.#");
        TbText.Text = UiHelpers.F(def.TextMm, "0.#");
        var dist = def.OffsetMm.Length;
        TbDistance.Text = UiHelpers.F(dist, "0.#");
        var best = 0;
        if (dist > 1e-6)
        {
            var u = def.OffsetMm / dist;
            best = Enumerable.Range(0, Directions.Length).OrderByDescending(i => Directions[i].Dir.Normalized().Dot(u)).First();
        }
        CbDirection.SelectedIndex = best;
        CkLeader.IsChecked = def.Leader;
        CkLabel.IsChecked = def.Label;
        CkName.IsChecked = def.ShowName;
        _loading = false;
        UpdatePreview();
    }

    private void AnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private SignPlanDetailDefinition BuildDefinition()
    {
        var d = _existing != null ? (SignPlanDetailDefinition)MarkingDefinition.FromJson(_existing.ToJson())! : new SignPlanDetailDefinition();
        d.SymbolMm = UiHelpers.Parse(TbSymbol, 12, "Largura do símbolo", 2, 200);
        d.TextMm = UiHelpers.Parse(TbText, 2, "Altura do texto", 0.8, 20);
        var dist = UiHelpers.Parse(TbDistance, 20, "Distância", 0, 500);
        var dir = (CbDirection.SelectedItem as Option<Vec2>)?.Value ?? new Vec2(0, 1);
        d.OffsetMm = dir.Normalized() * dist;
        d.Leader = CkLeader.IsChecked == true;
        d.Label = CkLabel.IsChecked == true;
        d.ShowName = CkName.IsChecked == true;
        return d;
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var d = BuildDefinition();
            var sign = (SignDefinition)MarkingDefinition.FromJson(_sample.ToJson())!;
            sign.Position = Vec2.Zero;
            d.SignId = sign.Id;
            var ctx = new BuildContext
            {
                Catalog = PluginContext.Catalog,
                Glyphs = PluginContext.Glyphs,
                ViewScale = _viewScale,
                Lookup = id => id == sign.Id ? sign : null,
            };
            var geo = DetailGenerator.SignDetail(d, ctx);
            Preview.Show(geo, null, null, geo.Warnings.Count > 0 ? string.Join("; ", geo.Warnings) : null);
        }
        catch (Exception ex)
        {
            Preview.Show(null, null, null, ex.Message);
        }
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Result = BuildDefinition();
            if (_existing == null) UiHelpers.Remember("DetalhePlaca", Result);
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
