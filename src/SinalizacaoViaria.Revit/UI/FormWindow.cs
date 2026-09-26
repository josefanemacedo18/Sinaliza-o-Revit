using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Resultado da pré-visualização de um formulário.</summary>
public sealed record FormPreview(MarkingGeometry? Geometry, IEnumerable<Polygon2>? Pavement = null,
    IEnumerable<IReadOnlyList<Vec2>>? Guides = null, string? Info = null, bool Paper = false, double ViewScale = 100);

/// <summary>
/// Janela de parâmetros montada em código (campos + pré-visualização ao vivo + saída 3D/2D), usada pelas
/// ferramentas de calçadas e de detalhamento. Os campos leem e gravam diretamente na definição de trabalho.
/// </summary>
public sealed class FormWindow : Window
{
    private readonly StackPanel _fields = new();
    private readonly GeometryPreview _preview = new();
    private readonly TextBlock _warnings = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly List<Action> _apply = new();
    private readonly Func<FormPreview?>? _previewFunc;
    private readonly OutputPanel? _output;
    private readonly MarkingDefinition? _def;
    private readonly List<(RadioButton Button, PathMode Mode)> _modes = new();
    private Grid? _grid;
    private bool _loading = true;

    public bool PickSurfaces => _output?.PickSurfaces == true;
    public PathMode PathMode => _modes.FirstOrDefault(m => m.Button.IsChecked == true).Mode;

    public FormWindow(string title, string heading, string? hint, MarkingDefinition? def, Func<FormPreview?>? preview,
        bool showOutput = true, string okText = "Inserir", double width = 960, double height = 640)
    {
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 760;
        MinHeight = 480;
        _def = def;
        _previewFunc = preview;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(preview != null ? 380 : 1, preview != null ? GridUnitType.Pixel : GridUnitType.Star) });
        if (preview != null) root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _fields.Children.Add(new TextBlock { Text = heading, Style = (Style)Resources["Title"] });
        if (!string.IsNullOrWhiteSpace(hint)) _fields.Children.Add(new TextBlock { Text = hint, Style = (Style)Resources["Hint"] });
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 12, 0), Content = _fields };
        root.Children.Add(scroll);

        if (showOutput && def != null)
        {
            _output = new OutputPanel();
            _output.Changed += (_, _) => Refresh();
        }

        if (preview != null)
        {
            var dock = new DockPanel();
            Grid.SetColumn(dock, 1);
            var t = new TextBlock { Text = "Pré-visualização", Style = (Style)Resources["Title"] };
            DockPanel.SetDock(t, Dock.Top);
            DockPanel.SetDock(_warnings, Dock.Bottom);
            dock.Children.Add(t);
            dock.Children.Add(_warnings);
            dock.Children.Add(new Border { BorderBrush = (System.Windows.Media.Brush)Resources["Border"], BorderThickness = new Thickness(1), Child = _preview });
            root.Children.Add(dock);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        var ok = new Button { Content = okText, IsDefault = true, Style = (Style)Resources["Primary"] };
        ok.Click += (_, _) => Ok();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true });
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            if (_output != null && def != null)
            {
                _fields.Children.Add(_output);
                _output.Load(def.Output);
            }
            _loading = false;
            Refresh();
        };
    }

    // ------------------------------------------------------------------ campos

    public FormWindow Section(string header, string? hint = null)
    {
        _grid = null;
        _fields.Children.Add(new TextBlock { Text = header, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
        if (hint != null) _fields.Children.Add(new TextBlock { Text = hint, Style = (Style)Resources["Hint"] });
        return this;
    }

    public FormWindow Hint(string text)
    {
        _grid = null;
        _fields.Children.Add(new TextBlock { Text = text, Style = (Style)Resources["Hint"] });
        return this;
    }

    private void Row(string label, FrameworkElement control, string? tooltip)
    {
        if (_grid == null)
        {
            _grid = new Grid();
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _grid.ColumnDefinitions.Add(new ColumnDefinition());
            _fields.Children.Add(_grid);
        }
        var r = _grid.RowDefinitions.Count;
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tb = new TextBlock { Text = label, Style = (Style)Resources["FieldLabel"], ToolTip = tooltip };
        Grid.SetRow(tb, r);
        Grid.SetRow(control, r);
        Grid.SetColumn(control, 1);
        if (tooltip != null) control.ToolTip = tooltip;
        _grid.Children.Add(tb);
        _grid.Children.Add(control);
    }

    public FormWindow Number(string label, Func<double> get, Action<double> set, double min, double max, string fmt = "0.00", string? tooltip = null)
    {
        var tb = new TextBox { Text = UiHelpers.F(get(), fmt) };
        tb.TextChanged += (_, _) => Refresh();
        Row(label, tb, tooltip);
        _apply.Add(() => set(UiHelpers.Parse(tb, get(), label, min, max)));
        return this;
    }

    public FormWindow Integer(string label, Func<int> get, Action<int> set, int min, int max, string? tooltip = null) =>
        Number(label, () => get(), v => set((int)Math.Round(v)), min, max, "0", tooltip);

    public FormWindow Check(string label, Func<bool> get, Action<bool> set, string? tooltip = null)
    {
        _grid = null;
        var cb = new CheckBox { Content = label, IsChecked = get(), ToolTip = tooltip, Margin = new Thickness(0, 3, 0, 3) };
        cb.Checked += (_, _) => Refresh();
        cb.Unchecked += (_, _) => Refresh();
        _fields.Children.Add(cb);
        _apply.Add(() => set(cb.IsChecked == true));
        return this;
    }

    public FormWindow Text(string label, Func<string?> get, Action<string?> set, bool multiline = false, string? tooltip = null)
    {
        var tb = new TextBox { Text = get() ?? "" };
        if (multiline)
        {
            tb.AcceptsReturn = true;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.Height = 70;
            tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        tb.TextChanged += (_, _) => Refresh();
        Row(label, tb, tooltip);
        _apply.Add(() => set(tb.Text.Replace("\r\n", "\n")));
        return this;
    }

    private sealed record Item<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    public FormWindow Choice<T>(string label, IEnumerable<(string Label, T Value)> options, Func<T> get, Action<T> set, string? tooltip = null)
    {
        var cb = new ComboBox();
        var current = get();
        foreach (var (l, v) in options)
        {
            var it = new Item<T>(l, v);
            cb.Items.Add(it);
            if (EqualityComparer<T>.Default.Equals(v, current)) cb.SelectedItem = it;
        }
        if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;
        cb.SelectionChanged += (_, _) => Refresh();
        Row(label, cb, tooltip);
        _apply.Add(() => { if (cb.SelectedItem is Item<T> it) set(it.Value); });
        return this;
    }

    public FormWindow Modes(params (string Label, PathMode Mode)[] modes)
    {
        _grid = null;
        var box = new GroupBox { Header = "Inserção" };
        var sp = new StackPanel();
        var list = modes.ToList();
        // Onde se pode escolher linhas, também se pode escolher bordas de pisos/calçadas.
        var k = list.FindIndex(m => m.Mode == PathMode.Linhas);
        if (k >= 0 && list.All(m => m.Mode != PathMode.Bordas)) list.Insert(k + 1, (UiHelpers.EdgesLabel, PathMode.Bordas));
        foreach (var (l, m) in list)
        {
            var rb = new RadioButton { Content = l, GroupName = "modes" + GetHashCode(), IsChecked = _modes.Count == 0, Margin = new Thickness(0, 2, 0, 2) };
            sp.Children.Add(rb);
            _modes.Add((rb, m));
        }
        if (_def != null && MarkingBuilder.SupportsJustify(_def))
        {
            var cb = new ComboBox { Margin = new Thickness(0, 6, 0, 2) };
            UiHelpers.FillJustify(cb, _def.Justify);
            cb.SelectionChanged += (_, _) => Refresh();
            sp.Children.Add(new TextBlock { Text = "Posição em relação à linha", Margin = new Thickness(0, 6, 0, 0) });
            sp.Children.Add(cb);
            _apply.Add(() => _def.Justify = UiHelpers.SelectedJustify(cb));
        }
        box.Content = sp;
        _fields.Children.Add(box);
        return this;
    }

    // ------------------------------------------------------------------ comportamento

    private bool ApplyAll(out string? error)
    {
        error = null;
        try
        {
            foreach (var a in _apply) a();
            if (_output != null && _def != null) _def.Output = _output.Save(_def.Output);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Refresh()
    {
        if (_loading || _previewFunc == null) return;
        if (!ApplyAll(out var err))
        {
            _warnings.Text = "⚠ " + err;
            return;
        }
        try
        {
            var p = _previewFunc();
            if (p == null) { _preview.Show(null, null, null, "Sem pré-visualização"); return; }
            _preview.Paper = p.Paper;
            _preview.ViewScale = p.ViewScale;
            _preview.Show(p.Geometry, p.Guides, p.Pavement);
            var w = p.Geometry?.Warnings.Distinct().ToList() ?? new List<string>();
            _warnings.Text = (p.Info ?? "") + (w.Count > 0 ? (string.IsNullOrEmpty(p.Info) ? "" : "\n") + "⚠ " + string.Join("\n⚠ ", w) : "");
        }
        catch (Exception ex)
        {
            _warnings.Text = ex.Message;
        }
    }

    private void Ok()
    {
        if (!ApplyAll(out var err))
        {
            UiHelpers.Error(err!);
            return;
        }
        DialogResult = true;
    }
}
