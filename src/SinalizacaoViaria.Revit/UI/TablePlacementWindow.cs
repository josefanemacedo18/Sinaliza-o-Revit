using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using SinalizacaoViaria.Revit.Infrastructure;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Onde gerar a legenda/quadro e em que folha colocar.</summary>
/// <param name="OwnView">Vista própria (Legenda) em vez da vista ativa.</param>
/// <param name="ViewName">Nome da vista própria.</param>
/// <param name="SheetId">Folha existente escolhida (nula com <paramref name="NewSheet"/> ou sem folha).</param>
/// <param name="NewSheet">Cria uma folha nova para o quadro.</param>
/// <param name="PickOnSheet">Clicar a posição na folha (só quando a folha escolhida é a vista ativa).</param>
public sealed record TablePlacement(bool OwnView, string ViewName, ElementId? SheetId, bool NewSheet, bool PickOnSheet)
{
    public bool OnSheet => NewSheet || SheetId != null;
}

/// <summary>
/// Destino da legenda de placas, do quadro de placas, do quadro de quantitativos e das Tabelas do Revit: vista própria
/// (independente da vista ativa) ou a vista ativa, e colocação direta numa folha escolhida.
/// </summary>
public sealed class TablePlacementWindow : Window
{
    private readonly RadioButton _own = new() { Content = "Vista própria – Legenda do Revit (vai para várias folhas e não depende de planta nenhuma)", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton _active = new() { Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBox _name = new();
    private readonly ComboBox _sheet = new();
    private readonly CheckBox _pick = new() { Content = "Clicar a posição na folha (senão: lugar livre no alto à direita)", Margin = new Thickness(0, 6, 0, 0) };
    private readonly ElementId? _activeSheet;

    public TablePlacement? Result { get; private set; }

    private sealed record SheetOption(string Label, ElementId? Id, bool New)
    {
        public override string ToString() => Label;
    }

    /// <param name="schedule">Tabela nativa do Revit: só a escolha da folha.</param>
    public TablePlacementWindow(Document doc, View active, string what, string defaultName, bool schedule)
    {
        Title = schedule ? "Tabela do Revit – folha" : $"{char.ToUpper(what[0])}{what[1..]} – onde gerar";
        Width = 560;
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
        _activeSheet = active is ViewSheet vs ? vs.Id : null;
        var canActive = MarkingService.SupportsDetail(active);
        _active.Content = canActive ? $"Na vista ativa \"{active.Name}\" (clicar o canto do quadro)" : "Na vista ativa – indisponível (abra uma planta ou vista de desenho)";
        _active.IsEnabled = canActive;
        _name.Text = defaultName;

        _sheet.Items.Add(new SheetOption("Não colocar em folha agora", null, false));
        _sheet.Items.Add(new SheetOption("Nova folha", null, true));
        foreach (var s in ProjectTableHost.Sheets(doc))
            _sheet.Items.Add(new SheetOption($"{s.SheetNumber} – {s.Name}{(s.Id == _activeSheet ? "  (ativa)" : "")}", s.Id, false));
        _sheet.SelectedIndex = _activeSheet != null
            ? _sheet.Items.Cast<SheetOption>().ToList().FindIndex(o => o.Id == _activeSheet)
            : schedule ? 1 : 0;
        _pick.IsChecked = _activeSheet != null;
        _sheet.SelectionChanged += (_, _) => Sync();
        _own.Checked += (_, _) => Sync();
        _active.Checked += (_, _) => Sync();

        var panel = new StackPanel { Margin = new Thickness(12) };
        if (!schedule)
        {
            panel.Children.Add(new TextBlock { Text = "Gerar em", FontWeight = FontWeights.SemiBold });
            panel.Children.Add(_own);
            panel.Children.Add(_active);
            var nameRow = new Grid { Margin = new Thickness(20, 4, 0, 8) };
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition());
            var nameLabel = new TextBlock { Text = "Nome da vista", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_name, 1);
            nameRow.Children.Add(nameLabel);
            nameRow.Children.Add(_name);
            panel.Children.Add(nameRow);
        }
        panel.Children.Add(new TextBlock { Text = "Colocar na folha", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 2) });
        panel.Children.Add(_sheet);
        panel.Children.Add(_pick);
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.75,
            Text = schedule
                ? "A Tabela é do próprio Revit: atualiza sozinha e pode ir para várias folhas (arraste-a do Navegador de projeto)."
                : "O conteúdo lê o projeto inteiro e continua se atualizando quando placas e marcas mudam. A Legenda pode ser arrastada " +
                  "do Navegador de projeto para outras folhas. Sem nenhuma Legenda no projeto, o quadro vai para uma vista de desenho (uma folha só).",
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        if (TryFindResource("Primary") is Style primary) ok.Style = primary;
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true, MinWidth = 80 });
        panel.Children.Add(buttons);
        Content = panel;
        Sync();
    }

    private void Sync()
    {
        var opt = _sheet.SelectedItem as SheetOption;
        var inActive = _active.IsChecked == true;
        _name.IsEnabled = !inActive;
        _sheet.IsEnabled = !inActive;
        _pick.IsEnabled = !inActive && opt?.Id != null && opt.Id == _activeSheet;
        if (!_pick.IsEnabled) _pick.IsChecked = false;
    }

    private void Accept()
    {
        var opt = _sheet.SelectedItem as SheetOption;
        var inActive = _active.IsChecked == true;
        if (!inActive && string.IsNullOrWhiteSpace(_name.Text))
        {
            UiHelpers.Error("Informe o nome da vista.");
            return;
        }
        Result = new TablePlacement(!inActive, _name.Text.Trim(), inActive ? null : opt?.Id, !inActive && opt?.New == true, _pick.IsEnabled && _pick.IsChecked == true);
        DialogResult = true;
    }

    /// <summary>Mostra a janela; nulo se cancelada.</summary>
    public static TablePlacement? Ask(Document doc, View active, string what, string defaultName, bool schedule = false)
    {
        var w = new TablePlacementWindow(doc, active, what, defaultName, schedule);
        return UiHelpers.ShowModal(w) == true ? w.Result : null;
    }
}
