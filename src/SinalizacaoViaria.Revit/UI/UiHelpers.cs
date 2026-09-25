using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Utilidades de interface: números no padrão brasileiro, janelas filhas do Revit, listas de cores.</summary>
public static class UiHelpers
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static IntPtr RevitHandle { get; set; }

    public static bool? ShowModal(Window w)
    {
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

    public static void Error(string msg) => MessageBox.Show(msg, "Sinalização Viária", MessageBoxButton.OK, MessageBoxImage.Warning);
}
