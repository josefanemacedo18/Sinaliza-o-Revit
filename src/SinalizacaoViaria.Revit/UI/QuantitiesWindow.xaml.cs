using SinalizacaoViaria.Revit.Infrastructure;
using System.Windows.Media.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Revit.UI;

public partial class QuantitiesWindow : Window
{
    private readonly List<QuantityRow> _rows;
    private readonly List<ComponentRow> _components;
    private readonly List<Core.Automation.SecoesDaVia> _sections;
    private readonly List<CamadaQuantidade> _layers;
    private readonly List<string> _noStructure;
    private readonly string _projectName;
    private readonly ListCollectionView _view;
    private readonly List<Core.Automation.EstadoContexto> _origem;
    private readonly List<LinhaPorVia> _vias;
    private readonly List<LinhaParcelamento> _parcelamento;

    public bool CreateSchedule { get; private set; }
    public bool SchedulesByCategory { get; private set; }
    /// <summary>Marcas a selecionar no modelo (das linhas escolhidas).</summary>
    public IReadOnlyList<string> Selecao { get; private set; } = Array.Empty<string>();

    private sealed record Option(string Label, CategoriaQuantitativo? Value)
    {
        public override string ToString() => Label;
    }

    private sealed record HOption(string Label, bool All, Core.Definitions.HierarquiaViaria? Value)
    {
        public override string ToString() => Label;
    }

    private readonly Dictionary<string, BitmapSource> _thumbs = new();

    /// <param name="origem">De onde vêm os números (<see cref="OrigemQuantitativos.Linhas"/>).</param>
    /// <param name="vias">Resumo por via (<see cref="QuantitativosPorVia"/>).</param>
    /// <param name="parcelamento">Quadras e lotes por loteamento (<see cref="QuantitativosParcelamento"/>).</param>
    public QuantitiesWindow(List<QuantityRow> rows, string projectName, Dictionary<string, BitmapSource>? thumbnails = null,
        List<ComponentRow>? components = null, List<Core.Automation.SecoesDaVia>? sections = null,
        List<CamadaQuantidade>? layers = null, List<string>? noStructure = null, List<Core.Automation.EstadoContexto>? origem = null,
        List<LinhaPorVia>? vias = null, List<LinhaParcelamento>? parcelamento = null)
    {
        InitializeComponent();
        _rows = rows;
        _origem = origem ?? new List<Core.Automation.EstadoContexto>();
        _vias = vias ?? new List<LinhaPorVia>();
        _parcelamento = parcelamento ?? new List<LinhaParcelamento>();
        // Por via e quadras e lotes (Etapa 12; Prompt de Interface §18): só com vias ou loteamentos.
        TabVias.Visibility = _vias.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TabParcelamento.Visibility = _parcelamento.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        GridParcelamento.ItemsSource = _parcelamento;
        // Origem dos valores (§18.1): a aba com tudo; o que pede atenção também no alto.
        TabOrigem.Visibility = _origem.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var l in _origem) PanelOrigem.Children.Add(ContextPanelPage.Linha(l.Nivel, l.Texto));
        foreach (var l in _origem.Where(l => l.Nivel is Core.Automation.NivelSituacao.Aviso or Core.Automation.NivelSituacao.Erro))
            PanelAlertas.Children.Add(ContextPanelPage.Linha(l.Nivel, l.Texto));
        TxtOrigem.Text = _origem.Count > 0 ? _origem[0].Texto + " Origem e método de cada número: escolha a linha ou veja a aba Origem dos valores." : "";
        _layers = layers ?? new List<CamadaQuantidade>();
        _noStructure = noStructure ?? new List<string>();
        // Camadas do pavimento (Etapa 5): só com vias ou nós com pavimento.
        TabLayers.Visibility = _layers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtLayers.Text = _noStructure.Count > 0 ? $"Sem estrutura do pavimento informada (só o revestimento): {string.Join(", ", _noStructure)}" : "";
        _components = components ?? new List<ComponentRow>();
        _sections = sections ?? new List<Core.Automation.SecoesDaVia>();
        // Terraplenagem por estaca (F7b): só com vias que moldam o terreno.
        TabStations.Visibility = _sections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        GridStations.ItemsSource = Core.Automation.SectionVolumes.Rows(_sections);
        TxtStations.Text = string.Join("\n", _sections.Select(v => v.Comparacao));
        _projectName = projectName;
        if (thumbnails != null) foreach (var (k, v) in thumbnails) _thumbs[k] = v;
        ThumbnailConverter.Images = _thumbs;
        CbCategory.Items.Add(new Option("Todas as categorias", null));
        foreach (var c in rows.Select(r => r.Category).Distinct().OrderBy(c => c))
            CbCategory.Items.Add(new Option($"{QuantityRow.CategoryLabel(c)} ({rows.Count(r => r.Category == c)})", c));
        CbHierarchy.Items.Add(new HOption("Todas", true, null));
        foreach (var h in rows.Select(r => r.Hierarchy).Distinct().OrderByDescending(Core.Definitions.Hierarquia.Rank))
            CbHierarchy.Items.Add(new HOption($"{Core.Definitions.Hierarquia.Label(h)} ({rows.Count(r => r.Hierarchy == h)})", false, h));
        CbHierarchy.SelectedIndex = 0;
        _view = new ListCollectionView(_rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QuantityRow.CategoryName)));
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QuantityRow.SubcategoryName)));
        TxtProject.Text = projectName;
        _view.Filter = o => Matches((QuantityRow)o);
        GridRows.ItemsSource = _view;
        CbCategory.SelectedIndex = 0;
        Refresh();
    }

    private CategoriaQuantitativo? Category => (CbCategory.SelectedItem as Option)?.Value;

    private bool Matches(QuantityRow r)
    {
        if (Category is { } c && r.Category != c) return false;
        if (CbHierarchy?.SelectedItem is HOption { All: false } h && r.Hierarchy != h.Value) return false;
        var q = TbSearch?.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        // Filtrar por elemento: o nome das marcas de origem ("Via 3", "Placa R-1").
        return r.Origens.Any(o => o.Nome.Contains(q, StringComparison.OrdinalIgnoreCase))
               || r.Code.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.Material.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Color.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.GroupName.Contains(q, StringComparison.OrdinalIgnoreCase) || r.SubcategoryName.Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.FamilyTypes.Contains(q, StringComparison.OrdinalIgnoreCase) || r.HierarchyName.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private List<QuantityRow> Visible => _rows.Where(Matches).ToList();

    /// <summary>Resumo por componente com o filtro de hierarquia (a categoria não se aplica: são componentes da seção).</summary>
    private List<ComponentRow> VisibleComponents => _components
        .Where(r => CbHierarchy?.SelectedItem is not HOption { All: false } h || r.Hierarchy == h.Value).ToList();

    /// <summary>Camadas do pavimento com o filtro de hierarquia.</summary>
    private List<CamadaQuantidade> VisibleLayers => _layers
        .Where(r => CbHierarchy?.SelectedItem is not HOption { All: false } h || r.Hierarquia == h.Value).ToList();

    /// <summary>Vias com o filtro de hierarquia e a busca pelo nome.</summary>
    private List<LinhaPorVia> VisibleVias => _vias
        .Where(r => CbHierarchy?.SelectedItem is not HOption { All: false } h || r.Hierarquia == h.Value)
        .Where(r => string.IsNullOrWhiteSpace(TbSearch?.Text) || r.Via.Contains(TbSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        _view.Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var v = Visible;
        // Categorias de memorial: sem colunas de cor, material, consumo, área e extensão.
        var memorialOnly = Category is { } cat && QuantityRow.IsMemorialCategory(cat);
        var vis = memorialOnly ? Visibility.Collapsed : Visibility.Visible;
        ColMaterial.Visibility = vis;
        ColConsumption.Visibility = vis;
        ColLength.Visibility = vis;
        ColArea.Visibility = vis;
        GridCategories.ItemsSource = CategoryTree(v);
        GridSummary.ItemsSource = QuantityCalculator.Summary(v);
        GridHierarchy.ItemsSource = QuantityCalculator.HierarchySummary(v);
        GridComponents.ItemsSource = VisibleComponents;
        GridLayers.ItemsSource = VisibleLayers;
        GridVias.ItemsSource = VisibleVias;
        var cats = v.Select(r => r.Category).Distinct().Count();
        TxtHeader.Text = $"{v.Count} item(ns) em {cats} categoria(s) e {v.Select(r => (r.Category, r.SubcategoryName)).Distinct().Count()} subcategoria(s)" +
                         (Category is { } c ? $" – filtro: {QuantityRow.CategoryLabel(c)}" : "") + $" – {DateTime.Now:dd/MM/yyyy HH:mm}";
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        Kpis.ItemsSource = new[]
        {
            new Kpi("Elementos no modelo", v.Sum(r => r.Elements).ToString("N0", pt), "un", UiTokens.Indicadores.Elementos),
            new Kpi("Área pintada", v.Where(r => MarkingColors.IsPaint(r.Color) && !r.IsFamily).Sum(r => r.Area).ToString("N1", pt), "m²", UiTokens.Indicadores.Pintura),
            new Kpi("Extensão pintada", v.Where(r => r.Category == CategoriaQuantitativo.SinalizacaoHorizontal).Sum(r => r.PaintedLength).ToString("N1", pt), "m", UiTokens.Indicadores.ExtensaoPintada),
            new Kpi("Pavimento", v.Where(r => r.Category == CategoriaQuantitativo.PavimentacaoGeometria && MarkingColors.IsPavement(r.Color)).Sum(r => r.Area).ToString("N1", pt), "m²", UiTokens.Indicadores.Pavimento),
            new Kpi("Placas", v.Where(r => r.Category == CategoriaQuantitativo.SinalizacaoVertical).Sum(r => r.Units).ToString("N0", pt), "un", UiTokens.Indicadores.Placas),
            new Kpi("Elementos urbanos", v.Where(r => r.Category == CategoriaQuantitativo.MobiliarioUrbano).Sum(r => r.Units).ToString("N0", pt), "un", UiTokens.Indicadores.ElementosUrbanos),
            Tinta(v, pt),
        }.Concat(v.Any(r => r.Unit == "m³")
            ? new[]
            {
                // Terraplenagem (corte e aterro no terreno, calculados contra o terreno original).
                new Kpi("Corte", v.Where(r => r.Code == "TER-CORTE").Sum(r => r.Volume).ToString("N1", pt), "m³", UiTokens.Indicadores.Corte),
                new Kpi("Aterro", v.Where(r => r.Code == "TER-ATERRO").Sum(r => r.Volume).ToString("N1", pt), "m³", UiTokens.Indicadores.Aterro),
            }
            : Array.Empty<Kpi>()).ToArray();
    }

    /// <summary>
    /// Tinta estimada: com uma unidade só, o total e a unidade (L, kg); com materiais de unidades diferentes, o total de cada uma
    /// ("12,50 L + 30,00 kg") – litros e quilos não se somam.
    /// </summary>
    private static Kpi Tinta(List<QuantityRow> v, CultureInfo pt)
    {
        var com = v.Where(r => r.MaterialConsumption > 1e-6).ToList();
        var unidades = com.Select(r => r.ConsumptionUnit).Distinct().ToList();
        return unidades.Count <= 1
            ? new Kpi("Tinta estimada", com.Sum(r => r.MaterialConsumption).ToString("N1", pt), unidades.FirstOrDefault() ?? "", UiTokens.Indicadores.Tinta)
            : new Kpi("Tinta estimada", QuantityCalculator.Consumo(com), "", UiTokens.Indicadores.Tinta);
    }

    /// <summary>Resumo em árvore: subtotal da categoria seguido das subcategorias.</summary>
    private static List<QuantityRow> CategoryTree(List<QuantityRow> rows)
    {
        var res = new List<QuantityRow>();
        var subs = QuantityCalculator.SubcategorySummary(rows);
        foreach (var c in QuantityCalculator.CategorySummary(rows))
        {
            res.Add(c);
            foreach (var s in subs.Where(x => x.Category == c.Category))
            {
                s.Code = "";
                s.Name = "      " + s.Name;
                res.Add(s);
            }
        }
        return res;
    }

    /// <summary>Cartão de indicador: o nome, o valor, a unidade e a cor da categoria (<see cref="UiTokens.Indicadores"/>).</summary>
    public sealed record Kpi(string Label, string Value, string Unit, Brush Accent);

    private void ExpandClick(object sender, RoutedEventArgs e) => SetExpanded(GridRows, true);

    private void CollapseClick(object sender, RoutedEventArgs e) => SetExpanded(GridRows, false);

    private static void SetExpanded(DependencyObject root, bool expanded)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Expander { Tag: "group" } ex) ex.IsExpanded = expanded;
            SetExpanded(child, expanded);
        }
    }

    private void ExportXlsxClick(object sender, RoutedEventArgs e)
    {
        var v = Visible;
        var suffix = Category is { } c ? " - " + QuantityRow.CategoryLabel(c) : "";
        var dlg = new SaveFileDialog
        {
            Filter = "Planilha do Excel|*.xlsx",
            FileName = $"Quantitativos - {_projectName}{suffix}.xlsx".Replace(":", ""),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var pngCache = new Dictionary<string, byte[]?>();
            byte[]? Png(QuantityRow r)
            {
                var key = QuantityThumbnails.Key(r.Code, r.Color);
                if (pngCache.TryGetValue(key, out var b)) return b;
                b = ThumbnailConverter.Find(r) is { } bmp ? GeometryPreview.Png(bmp) : null;
                pngCache[key] = b;
                return b;
            }
            File.WriteAllBytes(dlg.FileName, QuantityWorkbook.Build(v, QuantityCalculator.Summary(v), _projectName, Png, VisibleComponents, _sections, VisibleLayers, _noStructure,
                OrigemTextos, VisibleVias, _parcelamento));
            MessageBox.Show(this, "Planilha exportada:\n" + dlg.FileName, "Quantitativos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Não foi possível gravar a planilha: " + ex.Message);
        }
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        var v = Visible;
        var suffix = Category is { } c ? " - " + QuantityRow.CategoryLabel(c) : "";
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (separado por ponto e vírgula)|*.csv",
            FileName = $"Quantitativos - {_projectName}{suffix}.csv".Replace(":", ""),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, QuantityCalculator.ToCsv(v, QuantityCalculator.Summary(v), _projectName, VisibleComponents, _sections, VisibleLayers, _noStructure,
                OrigemTextos, VisibleVias, _parcelamento), new UTF8Encoding(true));
            MessageBox.Show(this, "Arquivo exportado:\n" + dlg.FileName, "Quantitativos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiHelpers.Error("Não foi possível gravar o arquivo: " + ex.Message);
        }
    }

    /// <summary>A origem dos números no cabeçalho das exportações.</summary>
    private IEnumerable<string> OrigemTextos => _origem.Select(l => l.Texto);

    /// <summary>Seleciona no modelo as marcas das linhas escolhidas na aba aberta (itens, vias ou loteamentos).</summary>
    private void SelectClick(object sender, RoutedEventArgs e)
    {
        var ids = (Tabs.SelectedItem == TabVias ? GridVias.SelectedItems.OfType<LinhaPorVia>().Select(v => v.Id)
                : Tabs.SelectedItem == TabParcelamento ? GridParcelamento.SelectedItems.OfType<LinhaParcelamento>().Select(l => l.LoteamentoId)
                : GridRows.SelectedItems.OfType<QuantityRow>().SelectMany(r => r.Origens.Select(o => o.Id)))
            .Distinct().ToList();
        if (ids.Count == 0)
        {
            MessageBox.Show(this, "Escolha uma linha com marcas do plugin (itens detalhados, uma via ou um loteamento). Pisos desenhados à mão e famílias " +
                                  "do usuário não são marcas do plugin.", "Quantitativos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Selecao = ids;
        DialogResult = true;
    }

    private void ScheduleClick(object sender, RoutedEventArgs e)
    {
        CreateSchedule = true;
        DialogResult = true;
    }

    private void SchedulesByCategoryClick(object sender, RoutedEventArgs e)
    {
        SchedulesByCategory = true;
        DialogResult = true;
    }

    public IReadOnlyList<CategoriaQuantitativo> Categories => _rows.Select(r => r.Category).Distinct().OrderBy(c => c).ToList();
}

/// <summary>Resumo de um grupo do quadro (quantidade de itens e totais por unidade).</summary>
public sealed class GroupSummaryConverter : IValueConverter
{
    public static GroupSummaryConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CollectionViewGroup g) return "";
        var rows = Flatten(g).ToList();
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var parts = new List<string> { $"{rows.Count} item(ns)" };
        var area = rows.Sum(r => r.Area);
        var len = rows.Sum(r => r.PaintedLength);
        var un = rows.Sum(r => r.Units);
        if (area > 1e-6) parts.Add($"{area.ToString("N2", pt)} m²");
        if (len > 1e-6) parts.Add($"{len.ToString("N2", pt)} m");
        if (un > 0) parts.Add($"{un.ToString("N0", pt)} un");
        return "— " + string.Join("  ·  ", parts);
    }

    private static IEnumerable<QuantityRow> Flatten(CollectionViewGroup g)
    {
        foreach (var it in g.Items)
        {
            if (it is QuantityRow r) yield return r;
            else if (it is CollectionViewGroup sub) foreach (var x in Flatten(sub)) yield return x;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Cor da marca → pincel (amostra de cor no quadro).</summary>
public sealed class MarkingColorBrushConverter : IValueConverter
{
    public static MarkingColorBrushConverter Instance { get; } = new();
    private readonly Dictionary<MarkingColor, Brush> _cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not MarkingColor c) return Brushes.Transparent;
        if (_cache.TryGetValue(c, out var b)) return b;
        var rgb = MarkingColors.Display(c);
        b = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
        b.Freeze();
        _cache[c] = b;
        return b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
