using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Nova placa personalizada (código, nome, forma, tamanho, cores e legenda), guardada no projeto.</summary>
public sealed class CustomSignWindow : Window
{
    private readonly Catalogo _cat;
    private readonly TextBox _code = new() { Text = "P-1" };
    private readonly TextBox _name = new();
    private readonly ComboBox _shape = new();
    private readonly TextBox _width = new() { Text = "1,00" };
    private readonly TextBox _height = new() { Text = "0,50" };
    private readonly ComboBox _back = new();
    private readonly ComboBox _border = new();
    private readonly ComboBox _text = new();
    private readonly ComboBox _category = new();
    private readonly TextBox _legend = new() { AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox _picto = new() { IsEditable = false };
    private readonly GeometryPreview _preview = new() { Height = 200, Paper = true };

    public PlacaDef? Result { get; private set; }

    private sealed record Option<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    public CustomSignWindow(Catalogo cat)
    {
        _cat = cat;
        Title = "Nova placa personalizada";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        try
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
            if (TryFindResource("SvWindow") is Style st) Style = st;
        }
        catch
        {
            // Sem o estilo do plugin: janela padrão.
        }
        foreach (var (l, f) in new[] { ("Retângulo", FormaPlaca.Retangulo), ("Quadrado", FormaPlaca.Quadrado), ("Círculo", FormaPlaca.Circulo),
                     ("Losango", FormaPlaca.Losango), ("Octógono", FormaPlaca.Octogono), ("Triângulo invertido", FormaPlaca.TrianguloInvertido) })
            _shape.Items.Add(new Option<FormaPlaca>(l, f));
        _shape.SelectedIndex = 0;
        foreach (var c in new[] { MarkingColor.Branca, MarkingColor.Amarela, MarkingColor.Vermelha, MarkingColor.Azul, MarkingColor.Verde,
                     MarkingColor.Preta, MarkingColor.Laranja, MarkingColor.Marrom })
        {
            _back.Items.Add(new Option<MarkingColor>(c.ToString(), c));
            _border.Items.Add(new Option<MarkingColor>(c.ToString(), c));
            _text.Items.Add(new Option<MarkingColor>(c.ToString(), c));
        }
        _back.SelectedIndex = 3;      // azul
        _border.SelectedIndex = 0;    // branca
        _text.SelectedIndex = 0;      // branca
        foreach (var c in Enum.GetValues<CategoriaPlaca>()) _category.Items.Add(new Option<CategoriaPlaca>(SignWindow.CategoryLabel(c), c));
        _category.SelectedIndex = (int)CategoriaPlaca.Indicacao;
        // Pictograma de qualquer placa do catálogo (ou nenhum); a legenda continua ao lado dele.
        _picto.Items.Add(new Option<PlacaDef?>("(nenhum)", null));
        foreach (var pl in cat.Placas.Where(x => !x.Personalizada && (x.Pictograma is { Count: > 0 } || x.Proibicao)).OrderBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase))
            _picto.Items.Add(new Option<PlacaDef?>($"{pl.Codigo} – {pl.Nome}", pl));
        _picto.SelectedIndex = 0;

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(string label, FrameworkElement field)
        {
            var r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = new TextBlock { Text = label, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(t, r);
            Grid.SetRow(field, r);
            Grid.SetColumn(field, 1);
            field.Margin = new Thickness(0, 3, 0, 3);
            grid.Children.Add(t);
            grid.Children.Add(field);
        }
        Row("Código", _code);
        Row("Nome", _name);
        Row("Categoria", _category);
        Row("Forma", _shape);
        Row("Largura / diâmetro (m)", _width);
        Row("Altura – retângulo (m)", _height);
        Row("Cor de fundo", _back);
        Row("Cor da orla", _border);
        Row("Cor da legenda", _text);
        Row("Legenda (uma linha por linha)", _legend);
        Row("Pictograma do catálogo", _picto);
        Row("Prévia", _preview);
        foreach (var tb in new[] { _code, _width, _height, _legend }) tb.TextChanged += (_, _) => UpdatePreview();
        foreach (var cb in new[] { _shape, _back, _border, _text, _picto }) cb.SelectionChanged += (_, _) => UpdatePreview();
        var hint = new TextBlock
        {
            Text = "A placa fica guardada neste projeto e aparece na lista de placas (categoria escolhida), na legenda, no detalhe, " +
                   "no quadro de placas e nos quantitativos. O código não pode ser de uma placa do catálogo.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontStyle = FontStyles.Italic,
        };
        Grid.SetRow(hint, grid.RowDefinitions.Count);
        Grid.SetColumnSpan(hint, 2);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Salvar no projeto", IsDefault = true, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3) };
        if (TryFindResource("Primary") is Style ps) ok.Style = ps;
        ok.Click += (_, _) => Save();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true, Padding = new Thickness(10, 3, 10, 3) });
        Grid.SetRow(buttons, grid.RowDefinitions.Count);
        Grid.SetColumnSpan(buttons, 2);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(buttons);
        Content = grid;
        UpdatePreview();
    }

    private static T Sel<T>(ComboBox c) => ((Option<T>)c.SelectedItem).Value;

    private PlacaDef Build(FormaPlaca shape, double w, double h, string? legend) =>
        CustomSigns.WithPictogram(CustomSigns.Create(_code.Text, _name.Text, shape, w, h, Sel<MarkingColor>(_back), Sel<MarkingColor>(_border), legend,
            Sel<MarkingColor>(_text), Sel<CategoriaPlaca>(_category)), Sel<PlacaDef?>(_picto));

    /// <summary>Face da placa como o condutor a vê.</summary>
    private void UpdatePreview()
    {
        if (Content == null) return;
        try
        {
            var shape = Sel<FormaPlaca>(_shape);
            var w = UiHelpers.ParseOpt(_width.Text) ?? 1.0;
            var h = shape == FormaPlaca.Retangulo ? UiHelpers.ParseOpt(_height.Text) ?? 0.5 : w;
            if (w <= 0.05 || h <= 0.05) return;
            var legend = string.IsNullOrWhiteSpace(_legend.Text) ? null : _legend.Text.Replace("\r\n", "\n").Trim();
            var p = Build(shape, w, h, legend);
            var geo = new MarkingGeometry();
            foreach (var (shp, color, _) in SignGenerator.Face(p, w, h, 0, null, PluginContext.Glyphs)) geo.Pieces.Add(new MarkingPiece(shp, color));
            _preview.Show(geo);
        }
        catch (Exception ex)
        {
            _preview.Show(null, null, null, ex.Message);
        }
    }

    private void Save()
    {
        try
        {
            var shape = Sel<FormaPlaca>(_shape);
            var w = UiHelpers.Parse(_width, 1.0, "Largura", 0.05, 10);
            var h = shape == FormaPlaca.Retangulo ? UiHelpers.Parse(_height, 0.5, "Altura", 0.05, 10) : w;
            var legend = string.IsNullOrWhiteSpace(_legend.Text) ? null : _legend.Text.Replace("\r\n", "\n").Trim();
            var p = Build(shape, w, h, legend);
            var problems = CustomSigns.Validate(p, _cat);
            if (problems.Count > 0) { UiHelpers.Error(string.Join("\n", problems)); return; }
            Result = p;
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
