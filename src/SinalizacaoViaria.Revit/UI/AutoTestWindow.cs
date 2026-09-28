using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Relatório do Autoteste: texto completo, copiar e abrir a pasta do arquivo.</summary>
public sealed class AutoTestWindow : Window
{
    public AutoTestWindow(string summary, string text, string path)
    {
        Title = "Autoteste do SinalizaBIM – relatório";
        Width = 1100;
        Height = 780;
        MinWidth = 700;
        MinHeight = 400;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = summary, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        head.Children.Add(new TextBlock
        {
            Text = $"Relatório salvo em: {path}\nEnvie este arquivo (ou copie o texto abaixo) para que os problemas encontrados sejam corrigidos.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 8),
            Foreground = new SolidColorBrush(Color.FromRgb(90, 96, 106)),
        });
        root.Children.Add(head);

        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(box, 1);
        root.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var copy = new Button { Content = "Copiar relatório", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(text); copy.Content = "Copiado ✓"; }
            catch { copy.Content = "Não foi possível copiar"; }
        };
        var open = new Button { Content = "Abrir a pasta", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
            catch { /* opcional */ }
        };
        var close = new Button { Content = "Fechar", Padding = new Thickness(14, 4, 14, 4), IsCancel = true, IsDefault = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(copy);
        buttons.Children.Add(open);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }
}
