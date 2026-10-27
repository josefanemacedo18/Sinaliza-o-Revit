using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Gerenciador da estrutura do pavimento de uma via (Etapa 11; Prompt de Interface §15): as camadas sob o revestimento numa
/// tabela (ordem, nome, material, espessura, função, estado e se o material existe no Revit), com adicionar, remover e
/// reordenar; o corte esquemático; o quantitativo previsto só com a área medida da via; e os modelos do usuário. O plugin não
/// dimensiona: as espessuras e a fonte são do projeto de pavimentação.
/// </summary>
public sealed class PavementManagerWindow : Window
{
    /// <summary>Uma camada na tabela (a espessura em texto, com vírgula).</summary>
    public sealed class LinhaCamada : INotifyPropertyChanged
    {
        private string _nome = "", _material = "", _espessura = "", _estado = "", _origem = "";
        private int _ordem;
        private FuncaoCamada _funcao = FuncaoCamada.Base;
        public int Ordem { get => _ordem; set => Set(ref _ordem, value); }
        public string Nome { get => _nome; set => Set(ref _nome, value); }
        public string Material { get => _material; set => Set(ref _material, value); }
        public string Espessura { get => _espessura; set => Set(ref _espessura, value); }
        public FuncaoCamada Funcao { get => _funcao; set => Set(ref _funcao, value); }
        public string Estado { get => _estado; set => Set(ref _estado, value); }
        public string Origem { get => _origem; set => Set(ref _origem, value); }
        public Hachura Hachura { get; set; } = Hachura.Brita;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        {
            if (EqualityComparer<T>.Default.Equals(f, v)) return;
            f = v;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public CamadaPavimento ToCamada() => new()
        {
            Nome = Nome.Trim(), Material = string.IsNullOrWhiteSpace(Material) ? null : Material.Trim(), Funcao = Funcao,
            EspessuraCm = UiHelpers.ParseOpt(Espessura) ?? double.NaN,
            Hachura = Funcao is FuncaoCamada.Reforco or FuncaoCamada.Regularizacao ? Hachura.Solo : Hachura == Hachura.Nenhuma ? Hachura.Brita : Hachura,
        };

        public static LinhaCamada De(CamadaPavimento c) => new()
        {
            Nome = c.Nome, Material = c.Material ?? "", Espessura = UiHelpers.F(c.EspessuraCm, "0.##"), Funcao = c.Funcao, Hachura = c.Hachura,
        };
    }

    private readonly ObservableCollection<LinhaCamada> _linhas = new();
    private readonly DataGrid _grid = new();
    private readonly TextBox _revestimento = new();
    private readonly TextBox _fonte = new();
    private readonly Canvas _esquema = new() { Height = 260, ClipToBounds = true, Background = UiTokens.Surface };
    private readonly StackPanel _quant = new();
    private readonly TextBlock _problemas = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _modelos = new() { MinWidth = 180 };
    private readonly CheckBox _refazer = new() { Content = "Refazer a terraplenagem com a nova profundidade" };
    private readonly Func<string, bool> _materialNoRevit;
    private readonly double? _area;
    private readonly string _revestimentoNome;
    private readonly List<ModeloEstruturaPavimento> _biblioteca;

    /// <summary>A estrutura aplicada (nula = sem camadas), a espessura do revestimento (cm) e se refaz a terraplenagem.</summary>
    public EstruturaPavimento? Resultado { get; private set; }
    public double RevestimentoCm { get; private set; }
    public bool RefazerTerraplenagem => _refazer.IsChecked == true;

    /// <param name="via">Código da via (título).</param>
    /// <param name="revestimento">Material do revestimento (o piso da pista).</param>
    /// <param name="area">Área em planta da pista desta via medida nas peças (m²); nula = não medida.</param>
    /// <param name="moldaTerreno">A via molda um terreno existente: oferece refazer a terraplenagem.</param>
    public PavementManagerWindow(string via, string revestimento, double revestimentoCm, EstruturaPavimento? estrutura, double? area,
        Func<string, bool> materialNoRevit, IEnumerable<string> materiaisDoRevit, bool moldaTerreno, List<ModeloEstruturaPavimento> biblioteca)
    {
        _materialNoRevit = materialNoRevit;
        _area = area;
        _revestimentoNome = revestimento;
        _biblioteca = biblioteca;
        Title = $"Estrutura do pavimento – {via}";
        Width = 1120;
        Height = 760;
        MinWidth = 820;
        MinHeight = 520;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SinalizaBIM;component/UI/Styles.xaml") });
        if (Resources["SvWindow"] is Style st) Style = st;

        var root = new Grid { Margin = new Thickness(UiTokens.SpaceL) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---------------------------------------------------------------- esquerda: revestimento, camadas, modelos
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock { Text = "Camadas do pavimento", Style = S("Title") });
        left.Children.Add(new TextBlock
        {
            Style = S("Hint"),
            Text = "De cima para baixo. O revestimento é o próprio piso da pista; abaixo dele, base, sub-base, reforço ou regularização. A profundidade " +
                   "total é o fundo da caixa da terraplenagem sob a pista (e dos nós ligados, pela via mais funda); as camadas vão ao corte transversal " +
                   "e ao quantitativo por camada. O plugin não dimensiona: informe as espessuras do projeto e a fonte.",
        });
        var rev = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
        rev.Children.Add(new TextBlock { Text = $"Revestimento ({revestimento}) – espessura (cm)", Style = S("FieldLabel") });
        _revestimento.Text = UiHelpers.F(revestimentoCm, "0.#");
        _revestimento.TextChanged += (_, _) => Agendar();
        rev.Children.Add(_revestimento);
        left.Children.Add(rev);

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.CanUserSortColumns = false;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.Height = 230;
        _grid.ItemsSource = _linhas;
        _grid.Columns.Add(new DataGridTextColumn { Header = "Nº", Binding = new Binding(nameof(LinhaCamada.Ordem)), IsReadOnly = true, Width = 34 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Nome", Binding = new Binding(nameof(LinhaCamada.Nome)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Material", Binding = new Binding(nameof(LinhaCamada.Material)), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Espessura (cm)", Binding = new Binding(nameof(LinhaCamada.Espessura)), Width = 90 });
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Função", Width = 120, SelectedValuePath = "Value", DisplayMemberPath = "Label",
            ItemsSource = Enum.GetValues<FuncaoCamada>().Select(f => new { Value = f, Label = EstruturaPavimento.Nome(f) }).ToList(),
            SelectedValueBinding = new Binding(nameof(LinhaCamada.Funcao)),
        });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new Binding(nameof(LinhaCamada.Estado)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Material no Revit", Binding = new Binding(nameof(LinhaCamada.Origem)), IsReadOnly = true, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        _grid.CellEditEnding += (_, _) => Agendar();
        foreach (var c in estrutura?.Camadas ?? new List<CamadaPavimento>()) _linhas.Add(LinhaCamada.De(c));
        left.Children.Add(_grid);

        var ops = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        ops.Children.Add(Botao("+ Camada", () => { _linhas.Add(new LinhaCamada { Nome = "Camada", Espessura = "10" }); _grid.SelectedIndex = _linhas.Count - 1; }));
        ops.Children.Add(Botao("Remover", () => { if (_grid.SelectedItem is LinhaCamada l) _linhas.Remove(l); }));
        ops.Children.Add(Botao("▲ Subir", () => Mover(-1)));
        ops.Children.Add(Botao("▼ Descer", () => Mover(1)));
        left.Children.Add(ops);

        var mats = materiaisDoRevit.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        left.Children.Add(new TextBlock
        {
            Style = S("Caption"), TextWrapping = TextWrapping.Wrap,
            Text = mats.Count == 0 ? "O projeto não tem materiais do Revit: os materiais das camadas ficam só no SinalizaBIM (texto)."
                : $"Materiais do Revit no projeto ({mats.Count}): escreva o nome igual para associar a camada a um deles; outro nome fica só no SinalizaBIM.",
        });

        var fonte = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
        fonte.Children.Add(new TextBlock { Text = "Fonte (projeto de pavimentação, dimensionamento)", Style = S("FieldLabel") });
        _fonte.Text = estrutura?.Fonte ?? "";
        fonte.Children.Add(_fonte);
        left.Children.Add(fonte);

        left.Children.Add(new TextBlock { Text = "Modelos guardados", Style = S("SectionTitle") });
        var mod = new WrapPanel();
        mod.Children.Add(_modelos);
        mod.Children.Add(Botao("Usar modelo", UsarModelo));
        mod.Children.Add(Botao("Guardar como modelo…", GuardarModelo));
        mod.Children.Add(Botao("Excluir modelo", ExcluirModelo));
        left.Children.Add(mod);
        left.Children.Add(new TextBlock { Style = S("Caption"), Text = "Os modelos ficam nas configurações do plugin (deste usuário), não no projeto." });
        RecarregarModelos();
        if (moldaTerreno)
        {
            _refazer.IsChecked = true;
            _refazer.ToolTip = "Sem refazer, o Toposolid fica com a caixa antiga e o Validar Modelo acusa a terraplenagem desatualizada (V15).";
            _refazer.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(_refazer);
        }
        left.Children.Add(_problemas);
        root.Children.Add(UiHelpers.Scroller(left));

        // ---------------------------------------------------------------- direita: corte e quantitativo
        var right = new StackPanel();
        right.Children.Add(new TextBlock { Text = "Corte esquemático", Style = S("Title") });
        right.Children.Add(new Border { BorderBrush = UiTokens.Border, BorderThickness = new Thickness(1), Child = _esquema });
        right.Children.Add(new TextBlock { Text = "Quantitativo previsto", Style = S("SectionTitle") });
        right.Children.Add(_quant);
        var rs = UiHelpers.Scroller(right);
        Grid.SetColumn(rs, 1);
        root.Children.Add(rs);

        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Aplicar", Style = S("Primary"), IsDefault = true };
        ok.Click += (_, _) => Aplicar();
        foot.Children.Add(ok);
        foot.Children.Add(new Button { Content = "Cancelar", IsCancel = true });
        Grid.SetRow(foot, 1);
        Grid.SetColumnSpan(foot, 2);
        root.Children.Add(foot);
        Content = root;
        _esquema.SizeChanged += (_, _) => Desenhar();
        Atualizar();
    }

    private Style? S(string key) => TryFindResource(key) as Style;

    private Button Botao(string texto, Action acao)
    {
        var b = new Button { Content = texto, Style = S("Secondary"), Margin = new Thickness(0, 0, 6, 4), MinWidth = 0, Padding = new Thickness(10, 3, 10, 3) };
        b.Click += (_, _) => { acao(); Atualizar(); };
        return b;
    }

    private void Mover(int d)
    {
        var i = _grid.SelectedIndex;
        var j = i + d;
        if (i < 0 || j < 0 || j >= _linhas.Count) return;
        _linhas.Move(i, j);
        _grid.SelectedIndex = j;
    }

    private void Agendar() => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Atualizar));

    private EstruturaPavimento Estrutura() => new()
    {
        Camadas = _linhas.Select(l => l.ToCamada()).ToList(),
        Fonte = string.IsNullOrWhiteSpace(_fonte.Text) ? null : _fonte.Text.Trim(),
    };

    private double Revestimento() => UiHelpers.ParseOpt(_revestimento.Text) ?? double.NaN;

    /// <summary>Estados, esquema e quantitativo pelo que está na tabela agora.</summary>
    private void Atualizar()
    {
        var e = Estrutura();
        var estados = GerenciadorPavimento.Estados(e, _materialNoRevit);
        for (var i = 0; i < _linhas.Count; i++)
        {
            _linhas[i].Ordem = i + 1;
            _linhas[i].Estado = estados[i].Estado;
            _linhas[i].Origem = GerenciadorPavimento.Nome(estados[i].Material);
        }
        var problemas = e.Problemas();
        var rev = Revestimento();
        if (!(rev >= 1 && rev <= 50)) problemas.Insert(0, "espessura do revestimento fora de 1 a 50 cm");
        _problemas.Inlines.Clear();
        _problemas.Margin = new Thickness(0, 6, 0, 0);
        _problemas.Foreground = problemas.Count > 0 ? UiTokens.Error : UiTokens.TextSecondary;
        _problemas.Text = problemas.Count > 0 ? "A corrigir antes de aplicar: " + string.Join("; ", problemas) + "."
            : e.Camadas.Count == 0 ? "Sem camadas: a terraplenagem usa o subleito das opções dela (0,40 m por padrão)." : "";

        _quant.Children.Clear();
        var q = GerenciadorPavimento.Quantidades(e, double.IsFinite(rev) ? rev : 0, _area);
        if (q.Count == 0)
            _quant.Children.Add(new TextBlock { Style = S("Caption"), TextWrapping = TextWrapping.Wrap,
                Text = _area is > 0 ? "Sem camadas com espessura." : "Não calculado: a área em planta da pista desta via não pôde ser medida." });
        else
        {
            _quant.Children.Add(new TextBlock { Style = S("Caption"), TextWrapping = TextWrapping.Wrap,
                Text = $"Para a pista desta via: {UiHelpers.F(_area!.Value, "N1")} m² em planta, medidos nas peças (os nós usam a estrutura da via ligada mais funda – Quantitativos › Camadas do pavimento). Volume = área × espessura, sem a sobrelargura das camadas sob a sarjeta e o meio-fio." });
            foreach (var x in q)
                _quant.Children.Add(new TextBlock { Text = $"{x.Camada}: {UiHelpers.F(x.EspessuraCm, "0.#")} cm → {UiHelpers.F(x.Volume, "N2")} m³", Style = S("Value") });
        }
        Desenhar();
    }

    /// <summary>Faixas proporcionais à espessura (mínimo legível), cada uma com o nome, a espessura e o material.</summary>
    private void Desenhar()
    {
        _esquema.Children.Clear();
        var rev = Revestimento();
        var faixas = GerenciadorPavimento.Esquema(Estrutura(), double.IsFinite(rev) ? rev : 0, _revestimentoNome);
        var w = _esquema.ActualWidth > 10 ? _esquema.ActualWidth : 320;
        if (faixas.Count == 0) return;
        var total = faixas[^1].BaseCm;
        var disponivel = _esquema.Height - 24;
        double Escala(double cm) => Math.Max(16, cm / total * disponivel);
        var y = 8.0;
        foreach (var f in faixas)
        {
            var h = Escala(f.BaseCm - f.TopoCm);
            var cor = f.Hachura switch
            {
                null => Color.FromRgb(62, 66, 72),
                Hachura.Solo or Hachura.Terra => Color.FromRgb(190, 160, 120),
                Hachura.Brita or Hachura.Pontos => Color.FromRgb(188, 186, 180),
                _ => Color.FromRgb(214, 196, 160),
            };
            var r = new Rectangle { Width = w - 16, Height = h, Fill = new SolidColorBrush(cor), Stroke = UiTokens.BorderStrong, StrokeThickness = 0.8 };
            Canvas.SetLeft(r, 8);
            Canvas.SetTop(r, y);
            _esquema.Children.Add(r);
            var t = new TextBlock { Text = f.Rotulo, FontSize = UiTokens.FontCaption, Foreground = f.Hachura == null ? UiTokens.TextOnAccent : UiTokens.Text, Width = w - 28, TextTrimming = TextTrimming.CharacterEllipsis };
            Canvas.SetLeft(t, 14);
            Canvas.SetTop(t, y + Math.Max(0, (h - 15) / 2));
            _esquema.Children.Add(t);
            y += h;
        }
    }

    private void RecarregarModelos()
    {
        _modelos.ItemsSource = null;
        _modelos.ItemsSource = _biblioteca.Select(m => m.Nome).ToList();
        if (_biblioteca.Count > 0) _modelos.SelectedIndex = 0;
    }

    private void UsarModelo()
    {
        if (_modelos.SelectedItem is not string nome || _biblioteca.FirstOrDefault(m => m.Nome == nome) is not { } m) return;
        _linhas.Clear();
        foreach (var c in m.Estrutura.Camadas) _linhas.Add(LinhaCamada.De(c));
        _fonte.Text = m.Estrutura.Fonte ?? "";
    }

    private void GuardarModelo()
    {
        string? nome = _modelos.SelectedItem as string;
        var f = new FormWindow("Guardar modelo", "Guardar a estrutura como modelo", "O modelo fica nas configurações do plugin e aparece para qualquer via.", null, null, false, "Guardar", 520, 260)
            .Text("Nome do modelo", () => nome, v => nome = v);
        if (UiHelpers.ShowModal(f) != true) return;
        var erro = GerenciadorPavimento.Salvar(_biblioteca, nome, Estrutura());
        if (erro != null) { UiHelpers.Error("Modelo não guardado: " + erro + "."); return; }
        PluginContext.SaveSettings();
        RecarregarModelos();
        _modelos.SelectedItem = nome!.Trim();
    }

    private void ExcluirModelo()
    {
        if (_modelos.SelectedItem is not string nome) return;
        if (MessageBox.Show($"Excluir o modelo \"{nome}\"? As vias que já usam a estrutura não mudam.", "SinalizaBIM", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _biblioteca.RemoveAll(m => m.Nome == nome);
        PluginContext.SaveSettings();
        RecarregarModelos();
    }

    private void Aplicar()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        Atualizar();
        var e = Estrutura();
        var rev = Revestimento();
        var problemas = e.Problemas();
        if (!(rev >= 1 && rev <= 50)) problemas.Insert(0, "espessura do revestimento fora de 1 a 50 cm");
        if (problemas.Count > 0)
        {
            UiHelpers.Error("A estrutura não foi aplicada – corrija antes:\n• " + string.Join("\n• ", problemas));
            return;
        }
        Resultado = e.Camadas.Count > 0 ? e : null;
        RevestimentoCm = rev;
        DialogResult = true;
    }
}
