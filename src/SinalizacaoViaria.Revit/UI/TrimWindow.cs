using System.Windows;
using System.Windows.Controls;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Apagar Trecho: planta da marca de sinalização horizontal com as peças reais (cada traço, seta, faixa). Clique numa peça
/// para apagá-la (fica em vermelho); clique de novo para devolver. Shift + arrastar apaga tudo dentro da janela. Em linhas
/// contínuas longas, apaga só o comprimento escolhido em volta do clique.
/// </summary>
public sealed class TrimWindow : Window
{
    private readonly MarkingGeometry _geo;
    private readonly GeometryPreview _preview = new() { Selecting = true };
    private readonly TextBox _tbLen = new() { Text = "3", Width = 60 };
    private readonly CheckBox _ckActive = new() { Content = "Recortes ativos (desmarque para mostrar a marca inteira sem perder os recortes)", IsChecked = true };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly List<Polygon2> _zones;

    /// <summary>Áreas a apagar (contorno das peças com 3 cm de folga).</summary>
    public IReadOnlyList<Polygon2> Zones => _zones;
    public bool Active => _ckActive.IsChecked == true;

    public TrimWindow(string markingName, MarkingGeometry geo, IEnumerable<Polygon2> existing, bool active)
    {
        _geo = geo;
        _zones = existing.ToList();
        Title = "Apagar trecho de sinalização horizontal";
        Width = 1100;
        Height = 720;
        MinWidth = 800;
        MinHeight = 500;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;
        _ckActive.IsChecked = active;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock { Text = markingName, Style = (Style)Resources["Title"], TextWrapping = TextWrapping.Wrap });
        left.Children.Add(new TextBlock
        {
            Style = (Style)Resources["Hint"],
            TextWrapping = TextWrapping.Wrap,
            Text = "Na planta ao lado estão as peças desta marca, exatamente como no modelo.\n\n" +
                   "• CLIQUE numa peça (um traço, uma seta, uma faixa) para apagá-la – ela fica vermelha.\n" +
                   "• Clique numa área vermelha para devolver a peça.\n" +
                   "• SHIFT + arrastar: apaga tudo desta marca dentro da janela.\n" +
                   "• Arrastar: move a planta · roda do mouse: zoom · 2 cliques: enquadrar.\n\n" +
                   "A marca continua a mesma (edição, quantitativos, conexões): os trechos apagados ficam guardados nela.",
        });
        var len = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        len.Children.Add(new TextBlock { Text = "Linha contínua longa: apagar ", VerticalAlignment = VerticalAlignment.Center });
        len.Children.Add(_tbLen);
        len.Children.Add(new TextBlock { Text = " m em volta do clique", VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(len);
        _ckActive.Margin = new Thickness(0, 10, 0, 0);
        left.Children.Add(_ckActive);
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var undo = new Button { Content = "Desfazer último" };
        undo.Click += (_, _) => { if (_zones.Count > 0) _zones.RemoveAt(_zones.Count - 1); Refresh(); };
        var clear = new Button { Content = "Devolver tudo" };
        clear.Click += (_, _) => { _zones.Clear(); Refresh(); };
        buttons.Children.Add(undo);
        buttons.Children.Add(clear);
        left.Children.Add(buttons);
        left.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var border = new Border { BorderBrush = (System.Windows.Media.Brush)Resources["Border"], BorderThickness = new Thickness(1), Child = _preview };
        Grid.SetColumn(border, 1);
        root.Children.Add(border);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Aplicar", IsDefault = true, Style = (Style)Resources["Primary"] };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        root.Children.Add(bottom);
        Content = root;

        _preview.ModelClicked += Click;
        _preview.ModelWindow += Window;
        _preview.Show(_geo);
        Refresh();
    }

    private double AroundLength => UiHelpers.ParseOpt(_tbLen.Text) is { } v && v > 0.2 ? v : 3;

    private void Click(Vec2 p)
    {
        // Clique numa área já marcada: devolve a peça.
        var hit = _zones.FindLastIndex(z => z.Contains(p));
        if (hit >= 0) { _zones.RemoveAt(hit); Refresh(); return; }
        var piece = TrimTools.PieceAt(_geo, p);
        if (piece == null) { _status.Text = "Nenhuma peça desta marca sob o clique."; return; }
        var zone = TrimTools.ZoneFor(piece, p, AroundLength);
        if (zone != null) _zones.Add(zone);
        Refresh();
    }

    private void Window(Vec2 a, Vec2 b)
    {
        _zones.AddRange(TrimTools.ZonesInWindow(_geo, a, b));
        Refresh();
    }

    private void Refresh()
    {
        _preview.Marked.Clear();
        _preview.Marked.AddRange(_zones);
        _preview.InvalidateVisual();
        _status.Text = _zones.Count == 0 ? "Nada marcado para apagar." : $"{_zones.Count} trecho(s) marcado(s) para apagar.";
    }
}
