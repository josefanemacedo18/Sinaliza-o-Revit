using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Utilidades de interface: números no padrão brasileiro, janelas filhas do Revit, listas de cores.</summary>
public static class UiHelpers
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static IntPtr RevitHandle { get; set; }

    public static bool? ShowModal(Window w)
    {
        // Painéis roláveis da janela também na horizontal (nada cortado em janela estreita ou com rótulo longo).
        w.Loaded += (_, _) => FitScrollViewers(w);
        LembrarTamanho(w);
        // WindowStartupLocation não é propriedade de dependência: não pode ficar no estilo XAML.
        if (RevitHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(w).Owner = RevitHandle;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        return w.ShowDialog();
    }

    // ------------------------------------------------------------------ tamanho das janelas (§26)

    /// <summary>Telas grandes e redimensionáveis que reabrem no tamanho em que foram fechadas (Prompt de Interface §26).</summary>
    internal static readonly HashSet<string> TamanhoLembrado = new(StringComparer.Ordinal)
    {
        nameof(ProjectCentralWindow), nameof(QualityCenterWindow), nameof(QuantitiesWindow), nameof(DrainageNetworkWindow), nameof(TerrainWindow),
        nameof(LotResultsWindow), nameof(PavementManagerWindow), nameof(RoadWindow), nameof(SettingsWindow), nameof(AutoTestWindow), nameof(TrafficWindow),
    };

    /// <summary>
    /// Reabre a janela no último tamanho (sem tamanho guardado, ilegível ou menor que o mínimo: o padrão dela; maior que a tela:
    /// limitado à tela) e guarda o tamanho ao fechar. Preferência pessoal, nas configurações do usuário – não no modelo.
    /// </summary>
    private static void LembrarTamanho(Window w)
    {
        var tipo = w.GetType().Name;
        if (!TamanhoLembrado.Contains(tipo) || w.SizeToContent != SizeToContent.Manual) return;
        var chave = Core.Settings.TamanhosDeJanela.Chave(tipo);
        var area = SystemParameters.WorkArea;
        if (Core.Settings.TamanhosDeJanela.Ler(PluginContext.Settings.Get(chave), w.MinWidth, w.MinHeight, area.Width, area.Height) is { } t)
        {
            w.Width = t.Largura;
            w.Height = t.Altura;
        }
        w.Closing += (_, _) =>
        {
            if (w.WindowState != WindowState.Normal || w.ActualWidth < 1 || w.ActualHeight < 1) return;
            PluginContext.Settings.Set(chave, Core.Settings.TamanhosDeJanela.Escrever(w.ActualWidth, w.ActualHeight));
            PluginContext.SaveSettings();
        };
    }

    // ------------------------------------------------------------------ rolagem horizontal

    /// <summary>Largura mínima da coluna dos campos (caixas de texto, listas) nas grades das janelas (px).</summary>
    public const double MinFieldWidth = 120;

    private static readonly ConditionalWeakTable<ScrollViewer, object> Fitted = new();

    /// <summary>Painel rolável nas duas direções (veja <see cref="FitWidth"/>).</summary>
    public static ScrollViewer Scroller(FrameworkElement content, double maxHeight = double.PositiveInfinity)
    {
        var sv = new ScrollViewer
        {
            Content = content, MaxHeight = maxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        sv.Loaded += (_, _) => FitWidth(sv);
        return sv;
    }

    /// <summary>Todos os painéis roláveis da janela (inclusive os de abas ainda não abertas) com <see cref="FitWidth"/>.</summary>
    public static void FitScrollViewers(DependencyObject root)
    {
        foreach (var sv in LogicalDescendants(root).OfType<ScrollViewer>().ToList()) FitWidth(sv);
    }

    /// <summary>
    /// Rolagem horizontal sem perder a quebra de linha: o conteúdo acompanha a largura visível do painel (os textos continuam
    /// quebrando), mas nunca fica mais estreito que o necessário – rótulos (colunas de largura automática) + coluna dos campos com
    /// <see cref="MinFieldWidth"/>. Abaixo disso aparece a barra horizontal e dá para arrastar até o fim; nada é cortado.
    /// Desenhos (prévia, mapa) não são tocados.
    /// </summary>
    public static void FitWidth(ScrollViewer sv)
    {
        if (sv.Content is not (Panel or TextBlock) || sv.Content is Canvas || Fitted.TryGetValue(sv, out _)) return;
        var content = (FrameworkElement)sv.Content;
        Fitted.Add(sv, new object());
        sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        var grids = LogicalDescendants(content).OfType<Grid>().Prepend(content as Grid).OfType<Grid>().ToList();
        foreach (var g in grids)
            for (int i = 0; i < g.ColumnDefinitions.Count; i++)
            {
                var col = g.ColumnDefinitions[i];
                if (col.Width.IsStar && col.MinWidth < MinFieldWidth
                    && g.Children.OfType<Control>().Any(c => Grid.GetColumn(c) == i && c is TextBox or ComboBox or ListBox or Slider))
                    col.MinWidth = MinFieldWidth;
            }
        double Need()
        {
            var need = 0.0;
            foreach (var g in grids)
            {
                if (!g.IsVisible || g.ColumnDefinitions.Count < 2 || g != content && !content.IsAncestorOf(g)) continue;
                var x = g == content ? 0 : g.TranslatePoint(new Point(0, 0), content).X;
                var cols = g.ColumnDefinitions.Sum(c => c.Width.IsStar ? c.MinWidth : c.ActualWidth);
                need = Math.Max(need, x + cols + g.Margin.Right);
            }
            // Itens de largura fixa (gráficos, botões) também não são cortados.
            if (content is Panel panel)
                foreach (var ch in panel.Children.OfType<FrameworkElement>().Where(c => c.IsVisible))
                {
                    var fixedW = double.IsNaN(ch.Width) ? ch.MinWidth : ch.Width;
                    if (fixedW > 0) need = Math.Max(need, ch.Margin.Left + fixedW + ch.Margin.Right);
                }
            return need;
        }
        void Fit()
        {
            // Largura útil com a barra vertical sempre descontada: a largura do conteúdo não depende de a barra vertical aparecer
            // (senão largura → quebra de linha → altura → barra → largura entraria em laço).
            var view = sv.ActualWidth - SystemParameters.VerticalScrollBarWidth - sv.Padding.Left - sv.Padding.Right
                       - sv.BorderThickness.Left - sv.BorderThickness.Right - content.Margin.Left - content.Margin.Right;
            if (view <= 0) return;
            var w = Math.Max(view, Need());
            if (double.IsNaN(content.Width) || Math.Abs(content.Width - w) > 1) content.Width = w;
        }
        sv.SizeChanged += (_, _) => Fit();
        // Shift + roda do mouse: rola na horizontal.
        sv.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Shift || sv.ScrollableWidth <= 0) return;
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        // Campos que aparecem/somem (opções condicionais) mudam a largura necessária.
        content.LayoutUpdated += (_, _) => Fit();
        Fit();
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in LogicalDescendants(child)) yield return d;
        }
    }

    public static string F(double v, string fmt = "0.00") => v.ToString(fmt, PtBr);
    public static string F(double? v, string fmt = "0.00") => v.HasValue ? v.Value.ToString(fmt, PtBr) : "";

    /// <summary>Aceita "0,15" ou "0.15". Vazio = nulo.</summary>
    public static double? ParseOpt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().Replace(" ", "");
        if (t.Contains(',') && t.Contains('.')) t = t.Replace(".", "");
        t = t.Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static double Parse(TextBox tb, double fallback, string field, double min = double.MinValue, double max = double.MaxValue)
    {
        var v = ParseOpt(tb.Text);
        if (v == null)
        {
            if (string.IsNullOrWhiteSpace(tb.Text)) return fallback;
            throw new FormatException($"Valor inválido em \"{field}\": {tb.Text}");
        }
        if (v < min || v > max) throw new FormatException($"\"{field}\" deve estar entre {F(min)} e {F(max)}.");
        return v.Value;
    }

    public static double? ParseNullable(TextBox tb, string field, double min = double.MinValue, double max = double.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(tb.Text)) return null;
        return Parse(tb, 0, field, min, max);
    }

    public static double? TryParse(TextBox tb) => ParseOpt(tb.Text);

    public sealed record ColorItem(MarkingColor? Color, string Label)
    {
        public override string ToString() => Label;
    }

    public static List<ColorItem> ColorItems(string defaultLabel = "Padrão do catálogo")
    {
        var list = new List<ColorItem> { new(null, defaultLabel) };
        list.AddRange(MarkingColors.All.Select(c => new ColorItem(c, c.ToString())));
        return list;
    }

    public static void SelectColor(ComboBox cb, MarkingColor? c)
    {
        foreach (var item in cb.Items)
            if (item is ColorItem ci && ci.Color == c) { cb.SelectedItem = item; return; }
        cb.SelectedIndex = 0;
    }

    public static MarkingColor? SelectedColor(ComboBox cb) => (cb.SelectedItem as ColorItem)?.Color;

    /// <summary>Lembra as últimas opções de uma janela (definição completa em JSON).</summary>
    public static void Remember(string key, Core.Definitions.MarkingDefinition def) =>
        PluginContext.Settings.Set("def:" + key, def.ToJson());

    public static T? Remembered<T>(string key) where T : Core.Definitions.MarkingDefinition
    {
        try
        {
            var json = PluginContext.Settings.Get("def:" + key);
            return json == null ? null : Core.Definitions.MarkingDefinition.FromJson(json) as T;
        }
        catch
        {
            return null;
        }
    }

    public static void Error(string msg) => MessageBox.Show(msg, "SinalizaBIM", MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>Opções de posição em relação à linha de referência.</summary>
    public static readonly (string Label, Core.Definitions.Justificacao Value)[] JustifyOptions =
    {
        ("Centralizado na linha", Core.Definitions.Justificacao.Centro),
        ("Borda na linha – indicar o lado com um clique", Core.Definitions.Justificacao.Clique),
        ("Borda na linha – elemento à ESQUERDA (sentido da linha)", Core.Definitions.Justificacao.Esquerda),
        ("Borda na linha – elemento à DIREITA (sentido da linha)", Core.Definitions.Justificacao.Direita),
    };

    public const string JustifyTip = "Centro: o eixo do elemento fica sobre a linha. Borda: a face lateral do elemento fica sobre a linha " +
        "(ex.: meio-fio ou calçada encostados na borda de um piso) e o elemento cresce para o lado escolhido. " +
        "Com 'indicar o lado', após escolher a linha clique do lado em que o elemento deve ficar.";

    public static void FillJustify(ComboBox cb, Core.Definitions.Justificacao value)
    {
        cb.Items.Clear();
        foreach (var (l, _) in JustifyOptions) cb.Items.Add(l);
        cb.SelectedIndex = Math.Max(0, Array.FindIndex(JustifyOptions, o => o.Value == value));
        cb.ToolTip = JustifyTip;
    }

    public static Core.Definitions.Justificacao SelectedJustify(ComboBox cb) =>
        cb.SelectedIndex >= 0 && cb.SelectedIndex < JustifyOptions.Length ? JustifyOptions[cb.SelectedIndex].Value : Core.Definitions.Justificacao.Centro;

    public const string EdgesLabel = "Selecionar bordas (arestas) de pisos, calçadas, lajes ou topografia (associativo)";
}
