using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>Linha editável da seção transversal (envolve um <see cref="ElementoSecao"/>).</summary>
public sealed class SectionRow : INotifyPropertyChanged
{
    public ElementoSecao Element { get; }
    public SectionRow(ElementoSecao e) => Element = e;

    public TipoElementoSecao Tipo
    {
        get => Element.Tipo;
        set
        {
            if (Element.Tipo == value) return;
            Element.Tipo = value;
            Element.Largura = ElementoSecao.LarguraPadrao(value);
            if (value == TipoElementoSecao.Ciclofaixa) Element.Espacamento = 30;
            OnChanged();
            OnChanged(nameof(LarguraTexto));
        }
    }

    public string LarguraTexto
    {
        get => UiHelpers.F(Element.Largura);
        set
        {
            var v = UiHelpers.ParseOpt(value);
            if (v is > 0.05 and < 100) Element.Largura = v.Value;
            OnChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Monta a seção transversal completa e gera toda a via (sinalização + calçadas, canteiros e dispositivos).</summary>
public partial class RoadWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly ObservableCollection<SectionRow> _right = new();
    private readonly ObservableCollection<SectionRow> _left = new();
    private SectionRow? _selected;
    private bool _loading = true;
    private bool _loadingDetails;

    public RoadSetup? Setup { get; private set; }
    public TipoConexao Connection { get; private set; } = TipoConexao.Intersecao;
    public FimLivre FreeEnds { get; private set; } = FimLivre.Nenhum;
    public bool AutoIntersect => Connection != TipoConexao.Nenhuma;
    public bool Snap { get; private set; } = true;
    public double CurveRadius { get; private set; }
    /// <summary>Raio das esquinas (nulo = pela hierarquia das vias que se cruzam).</summary>
    public double? CornerRadius => UiHelpers.ParseOpt(TbCornerRadius.Text) is { } r && r >= 0 ? r : null;
    public bool IntersectionCrosswalks => CkIntCrosswalks.IsChecked == true;
    public bool IntersectionRamps => CkIntRamps.IsChecked == true;

    private sealed record PavementOption(string Label, TipoPavimento Value)
    {
        public override string ToString() => Label;
    }
    public OutputSettings? OutputSettings { get; private set; }
    public bool DrawPath { get; private set; }
    public bool PickSurfaces => Output.PickSurfaces;

    private sealed record Option<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    public sealed record TypeOption(TipoElementoSecao Value, string Label);

    public RoadWindow(bool draw = true)
    {
        InitializeComponent();
        RbDraw.IsChecked = draw;
        RbCurves.IsChecked = !draw;
        CbHierarchy.Items.Add(new Option<HierarquiaViaria>("— selecione a hierarquia —", HierarquiaViaria.NaoDefinida));
        foreach (var h in Hierarquia.Definidas)
            CbHierarchy.Items.Add(new Option<HierarquiaViaria>($"{Hierarquia.Label(h)} (até {Hierarquia.DefaultSpeed(h):0} km/h)", h));
        CbConnection.Items.Add(new Option<TipoConexao>("Interseção (tipos da ferramenta Interseção)", TipoConexao.Intersecao));
        CbConnection.Items.Add(new Option<TipoConexao>("Rotatória", TipoConexao.Rotatoria));
        CbConnection.Items.Add(new Option<TipoConexao>("Não ajustar (vias sobrepostas)", TipoConexao.Nenhuma));
        CbFreeEnds.Items.Add(new Option<FimLivre>("Sem tratamento", FimLivre.Nenhum));
        CbFreeEnds.Items.Add(new Option<FimLivre>("Cul-de-sac (balão de retorno)", FimLivre.CulDeSac));
        var st = PluginContext.Settings;
        Select(CbConnection, st.LastConnection);
        Select(CbFreeEnds, st.LastFreeEnds);
        TbCurveRadius.Text = UiHelpers.F(st.LastCurveRadius, "0.#");
        CkIntCrosswalks.IsChecked = st.AutoCrosswalks;
        CkIntRamps.IsChecked = st.AutoCrosswalks;

        var types = Enum.GetValues<TipoElementoSecao>().Select(t => new TypeOption(t, ElementoSecao.Rotulo(t))).ToList();
        ColTypeRight.ItemsSource = types;
        ColTypeLeft.ItemsSource = types;
        GridRight.ItemsSource = _right;
        GridLeft.ItemsSource = _left;

        FillTemplates(null);

        CbCenter.Items.Add(new Option<CenterTreatment>("LFO-2 – seccionada (ultrapassagem permitida)", CenterTreatment.LFO2));
        CbCenter.Items.Add(new Option<CenterTreatment>("LFO-1 – contínua simples", CenterTreatment.LFO1));
        CbCenter.Items.Add(new Option<CenterTreatment>("LFO-3 – dupla contínua", CenterTreatment.LFO3));
        CbCenter.Items.Add(new Option<CenterTreatment>("LFO-4 – contínua/seccionada", CenterTreatment.LFO4));
        CbCenter.Items.Add(new Option<CenterTreatment>("Canteiro central", CenterTreatment.Canteiro));
        CbCenter.Items.Add(new Option<CenterTreatment>("Sem marca no eixo", CenterTreatment.Nenhum));
        CbMedianType.Items.Add(new Option<TipoCanteiro>("Físico (meio-fio + grama)", TipoCanteiro.Fisico));
        CbMedianType.Items.Add(new Option<TipoCanteiro>("Pintado (zebrado amarelo)", TipoCanteiro.Pintado));

        CbMedianDevice.Items.Add(new Option<string?>("Nenhum", null));
        CbDispositivo.Items.Add(new Option<string?>("Nenhuma", null));
        foreach (var d in _cat.Dispositivos)
        {
            CbMedianDevice.Items.Add(new Option<string?>(d.Nome, d.Codigo));
            CbDispositivo.Items.Add(new Option<string?>(d.Nome, d.Codigo));
        }
        foreach (var v in _cat.Vagas) CbVaga.Items.Add(new Option<string>(v.Nome, v.Codigo));
        CbCorCaminhada.Items.Add(new Option<MarkingColor>("Azul", MarkingColor.Azul));
        CbCorCaminhada.Items.Add(new Option<MarkingColor>("Verde", MarkingColor.Verde));
        foreach (var (n, c) in new[] { ("Vermelha", MarkingColor.Vermelha), ("Azul", MarkingColor.Azul), ("Verde", MarkingColor.Verde),
                     ("Amarela", MarkingColor.Amarela), ("Laranja", MarkingColor.Laranja), ("Marrom", MarkingColor.Marrom) })
            CbCorOnibus.Items.Add(new Option<MarkingColor>(n, c));

        foreach (var t in _cat.LinearesDoGrupo(GrupoMarca.Longitudinal).Where(t => t.Codigo.StartsWith("LMS") || t.Codigo.StartsWith("LCO")))
            CbDivider.Items.Add(t.Codigo);
        foreach (var t in _cat.LinearesDoGrupo(GrupoMarca.Longitudinal, GrupoMarca.Ciclovia).Where(t => t.Codigo == "LBO" || t.Codigo.StartsWith("CIC-LD")))
            CbEdge.Items.Add(t.Codigo);
        foreach (var t in _cat.LinearesDoGrupo(GrupoMarca.Dispositivo))
            foreach (var v in t.Variantes) CbStuds.Items.Add($"{t.Codigo} | {v.Nome}");
        if (CbStuds.Items.Count > 0) CbStuds.SelectedIndex = 0;

        CbPavement.Items.Add(new PavementOption("Asfalto (CBUQ)", TipoPavimento.Asfalto));
        CbPavement.Items.Add(new PavementOption("Bloquete / pavimento intertravado", TipoPavimento.Bloquete));
        CbPavement.Items.Add(new PavementOption("Concreto", TipoPavimento.Concreto));
        CbPavement.Items.Add(new PavementOption("Nenhum (pista já modelada)", TipoPavimento.Nenhum));
        CbPavement.SelectedIndex = 0;

        Output.Load(PluginContext.Settings.NewOutput());
        Output.Changed += (_, _) => UpdatePreview();
        _right.CollectionChanged += (_, _) => UpdatePreview();
        _left.CollectionChanged += (_, _) => UpdatePreview();

        // Reabre com a última seção usada (ou a avenida, na primeira vez).
        var last = RoadTemplates.FromJson(PluginContext.Settings.LastRoadSetup);
        LoadSetup(last ?? RoadTemplates.All[Math.Min(2, RoadTemplates.All.Count - 1)].Create());
        if (last == null)
        {
            CbTemplate.SelectedIndex = Math.Min(2, RoadTemplates.All.Count - 1);
            TbSpeed.Text = UiHelpers.F(PluginContext.Settings.DefaultSpeed, "0");
        }
        _loading = false;
        ShowDetails(null);
        UpdatePreview();
    }

    // ------------------------------------------------------------------ carregar / montar

    private void LoadSetup(RoadSetup s)
    {
        var was = _loading;
        _loading = true;
        Select(CbHierarchy, s.Hierarchy);
        TbCornerRadius.Text = s.CornerRadius is { } cr ? UiHelpers.F(cr) : "";
        RbTwoWay.IsChecked = s.TwoWay;
        RbOneWay.IsChecked = !s.TwoWay;
        Select(CbCenter, s.Center);
        TbMedian.Text = UiHelpers.F(s.MedianWidth);
        Select(CbMedianType, s.MedianType);
        Select(CbMedianDevice, s.MedianDevice);
        CkInvertCenter.IsChecked = s.InvertCenter;
        TbSpeed.Text = UiHelpers.F(s.Speed, "0");
        CbDivider.SelectedItem = s.LaneDividerCode;
        CbEdge.SelectedItem = s.EdgeCode;
        CkEdges.IsChecked = s.EdgeLines;
        TbEdgeInset.Text = UiHelpers.F(s.EdgeInset);
        CkStuds.IsChecked = !string.IsNullOrEmpty(s.CenterStudsCode);
        if (!string.IsNullOrEmpty(s.CenterStudsCode))
            CbStuds.SelectedItem = CbStuds.Items.Cast<string>().FirstOrDefault(x => x.StartsWith(s.CenterStudsCode + " | ") && (s.CenterStudsVariant == null || x.EndsWith(s.CenterStudsVariant))) ?? CbStuds.SelectedItem;
        _right.Clear();
        foreach (var e in s.Right) Attach(_right, e.Clone());
        _left.Clear();
        foreach (var e in s.Left) Attach(_left, e.Clone());
        _loading = was;
        UpdateCenterEnabled();
    }

    private void Attach(ObservableCollection<SectionRow> list, ElementoSecao e, int index = -1)
    {
        var row = new SectionRow(e);
        row.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(SectionRow.Tipo) && row == _selected) ShowDetails(row);
            SchedulePreview();
        };
        if (index < 0 || index > list.Count) list.Add(row); else list.Insert(index, row);
    }

    private static void Select<T>(ComboBox cb, T value)
    {
        foreach (var item in cb.Items)
            if (item is Option<T> o && EqualityComparer<T>.Default.Equals(o.Value, value)) { cb.SelectedItem = item; return; }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    private static T? Selected<T>(ComboBox cb) => cb.SelectedItem is Option<T> o ? o.Value : default;

    private RoadSetup BuildSetup()
    {
        var s = new RoadSetup
        {
            Hierarchy = Selected<HierarquiaViaria>(CbHierarchy),
            TwoWay = RbTwoWay.IsChecked == true,
            Right = _right.Select(r => r.Element.Clone()).ToList(),
            Left = _left.Select(r => r.Element.Clone()).ToList(),
            Center = Selected<CenterTreatment>(CbCenter),
            MedianWidth = UiHelpers.Parse(TbMedian, 2, "Canteiro central", 0.1, 100),
            MedianType = Selected<TipoCanteiro>(CbMedianType),
            MedianDevice = Selected<string?>(CbMedianDevice),
            InvertCenter = CkInvertCenter.IsChecked == true,
            LaneDividerCode = CbDivider.SelectedItem as string ?? "LMS-2",
            EdgeLines = CkEdges.IsChecked == true,
            EdgeCode = CbEdge.SelectedItem as string ?? "LBO",
            EdgeInset = UiHelpers.Parse(TbEdgeInset, 0.1, "Afastamento do bordo", -5, 5),
            Speed = UiHelpers.Parse(TbSpeed, 60, "Velocidade", 10, 200),
            StartSetback = UiHelpers.Parse(TbStart, 0, "Recuo inicial", 0, 10000),
            EndSetback = UiHelpers.Parse(TbEnd, 0, "Recuo final", 0, 10000),
            Inscriptions = CkInscriptions.IsChecked == true,
            PhysicalElements = CkPhysical.IsChecked == true,
            Pavement = (CbPavement.SelectedItem as PavementOption)?.Value ?? TipoPavimento.Asfalto,
            PavementThickness = UiHelpers.ParseNullable(TbPavThickness, "Espessura do pavimento", 0.01, 2),
        };
        if (s.Right.Count == 0 && s.Left.Count == 0) throw new FormatException("Adicione ao menos um elemento à seção.");
        if (CkStuds.IsChecked == true && CbStuds.SelectedItem is string st)
        {
            var parts = st.Split(" | ");
            s.CenterStudsCode = parts[0];
            s.CenterStudsVariant = parts.Length > 1 ? parts[1] : null;
        }
        return s;
    }

    /// <summary>Gera as definições para o caminho escolhido (os avisos ficam em <see cref="RoadSetup.Warnings"/>).</summary>
    public List<MarkingDefinition> BuildDefinitions(PathReference path) => Setup!.Build(path, OutputSettings!, _cat);

    // ------------------------------------------------------------------ prévia

    private DispatcherOperation? _pending;

    private void SchedulePreview()
    {
        if (_loading) return;
        _pending?.Abort();
        _pending = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdatePreview));
    }

    private void UpdatePreview()
    {
        if (_loading || Preview == null) return;
        try
        {
            var s = BuildSetup();
            var o = Output.Save();
            var sample = new Polyline2(new[] { new Vec2(0, 0), new Vec2(50, 0) });
            var ctx = new BuildContext { Catalog = _cat, Glyphs = PluginContext.Glyphs };
            var geo = new MarkingGeometry();
            var defs = s.Build(new PathReference(), o, _cat);
            foreach (var d in defs) geo.Merge(MarkingBuilder.Build(d, sample, ctx));
            var half = s.TwoWay && s.Center == CenterTreatment.Canteiro ? s.MedianWidth / 2 : 0;
            var pav = Polygon2.Rectangle(new Vec2(0, -(s.SideWidth(s.Right) + half)), new Vec2(50, s.SideWidth(s.Left) + half));
            Preview.Show(geo, new[] { sample.Points }, new[] { pav });

            var counts = defs.GroupBy(d => d.DisplayCode).Select(g => $"{g.Count()}× {g.Key}");
            var warnings = s.Warnings.Concat(geo.Warnings).Distinct().ToList();
            TxtSummary.Text = $"Largura total: {UiHelpers.F(s.TotalWidth)} m  (pista: {UiHelpers.F(s.CarriagewayWidth)} m).  " +
                              $"Elementos gerados: {string.Join(", ", counts)}." +
                              (warnings.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", warnings) : "");
            TxtSummary.Foreground = warnings.Count > 0 ? System.Windows.Media.Brushes.SaddleBrown : System.Windows.Media.Brushes.Black;
        }
        catch (Exception ex)
        {
            TxtSummary.Text = ex.Message;
            TxtSummary.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
    }

    // ------------------------------------------------------------------ eventos gerais

    private void AnyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateCenterEnabled();
        UpdatePreview();
    }

    /// <summary>A velocidade acompanha a hierarquia escolhida (CTB art. 61) – pode ser alterada depois.</summary>
    private void HierarchyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var h = Selected<HierarquiaViaria>(CbHierarchy);
        if (h != HierarquiaViaria.NaoDefinida) TbSpeed.Text = UiHelpers.F(Hierarquia.DefaultSpeed(h), "0");
        UpdatePreview();
    }

    private void UpdateCenterEnabled()
    {
        if (GbCenter == null) return;
        GbCenter.IsEnabled = RbTwoWay.IsChecked == true;
        var median = Selected<CenterTreatment>(CbCenter) == CenterTreatment.Canteiro;
        TbMedian.IsEnabled = median;
        CbMedianType.IsEnabled = median;
    }

    private void FillTemplates(string? select)
    {
        CbTemplate.Items.Clear();
        foreach (var t in PluginContext.Settings.CustomRoadTemplates)
        {
            var json = t.Json;
            CbTemplate.Items.Add(new RoadTemplates.Template("★ " + t.Name, () => RoadTemplates.FromJson(json) ?? new RoadSetup()));
        }
        foreach (var t in RoadTemplates.All) CbTemplate.Items.Add(t);
        CbTemplate.SelectedItem = CbTemplate.Items.Cast<RoadTemplates.Template>().FirstOrDefault(t => t.Name == select) ?? CbTemplate.Items[0];
    }

    private void SaveTemplateClick(object sender, RoutedEventArgs e)
    {
        RoadSetup s;
        try
        {
            GridRight.CommitEdit(DataGridEditingUnit.Row, true);
            GridLeft.CommitEdit(DataGridEditingUnit.Row, true);
            s = BuildSetup();
        }
        catch (FormatException ex) { UiHelpers.Error(ex.Message); return; }
        var current = CbTemplate.SelectedItem is RoadTemplates.Template { Name: var n } && n.StartsWith("★ ") ? n[2..] : "";
        var name = current;
        var w = new FormWindow("Salvar modelo de via", "Salvar modelo de via",
                "O modelo guarda toda a seção. Use o mesmo nome para substituir um modelo existente.", null, null, false, "Salvar", 520, 240)
            .Text("Nome do modelo", () => name, v => name = v?.Trim() ?? "");
        w.Owner = this;
        if (w.ShowDialog() != true || string.IsNullOrWhiteSpace(name)) return;
        var list = PluginContext.Settings.CustomRoadTemplates;
        list.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, new Core.Settings.CustomRoadTemplate { Name = name, Json = RoadTemplates.ToJson(s) });
        PluginContext.SaveSettings();
        FillTemplates("★ " + name);
    }

    private void DeleteTemplateClick(object sender, RoutedEventArgs e)
    {
        if (CbTemplate.SelectedItem is not RoadTemplates.Template { Name: var n } || !n.StartsWith("★ "))
        {
            UiHelpers.Error("Selecione um modelo personalizado (★) para excluir.");
            return;
        }
        if (MessageBox.Show(this, $"Excluir o modelo \"{n[2..]}\"?", "SinalizaBIM", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        PluginContext.Settings.CustomRoadTemplates.RemoveAll(t => t.Name == n[2..]);
        PluginContext.SaveSettings();
        FillTemplates(null);
    }

    private void ApplyTemplateClick(object sender, RoutedEventArgs e)
    {
        if (CbTemplate.SelectedItem is not RoadTemplates.Template t) return;
        LoadSetup(t.Create());
        ShowDetails(null);
        UpdatePreview();
    }

    private void CellEdited(object? sender, DataGridCellEditEndingEventArgs e) => SchedulePreview();

    private ObservableCollection<SectionRow> ListOf(object sender) => (sender as FrameworkElement)?.Tag as string == "L" ? _left : _right;
    private DataGrid GridOf(object sender) => (sender as FrameworkElement)?.Tag as string == "L" ? GridLeft : GridRight;

    private void AddClick(object sender, RoutedEventArgs e)
    {
        var list = ListOf(sender);
        var grid = GridOf(sender);
        var idx = grid.SelectedIndex >= 0 ? grid.SelectedIndex + 1 : list.Count;
        if (grid.SelectedIndex < 0 && list.Count > 0 && list[^1].Tipo == TipoElementoSecao.Calcada) idx = list.Count - 1;
        Attach(list, new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.50 }, idx);
        grid.SelectedIndex = idx;
    }

    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        var list = ListOf(sender);
        var grid = GridOf(sender);
        if (grid.SelectedItem is SectionRow r) list.Remove(r);
    }

    private void UpClick(object sender, RoutedEventArgs e) => Move(sender, -1);
    private void DownClick(object sender, RoutedEventArgs e) => Move(sender, +1);

    private void Move(object sender, int delta)
    {
        var list = ListOf(sender);
        var grid = GridOf(sender);
        var i = grid.SelectedIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        list.Move(i, j);
        grid.SelectedIndex = j;
    }

    private void MirrorClick(object sender, RoutedEventArgs e)
    {
        var from = ListOf(sender);
        var to = from == _right ? _left : _right;
        to.Clear();
        foreach (var r in from) Attach(to, r.Element.Clone());
    }

    // ------------------------------------------------------------------ detalhes do elemento

    private void GridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid g || g.SelectedItem is not SectionRow row) return;
        // Uma seleção por vez entre as duas listas.
        if (g == GridRight) GridLeft.UnselectAll(); else GridRight.UnselectAll();
        ShowDetails(row);
    }

    private void ShowDetails(SectionRow? row)
    {
        _selected = row;
        _loadingDetails = true;
        try
        {
            PanelDetails.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
            if (row == null)
            {
                TxtSelected.Text = "Selecione um elemento em uma das listas para ver suas opções.";
                return;
            }
            var e = row.Element;
            var t = e.Tipo;
            TxtSelected.Text = $"{ElementoSecao.Rotulo(t)} – {UiHelpers.F(e.Largura)} m";
            bool parking = t == TipoElementoSecao.Estacionamento;
            bool bus = t is TipoElementoSecao.FaixaExclusiva or TipoElementoSecao.FaixaPreferencial;
            bool bike = t == TipoElementoSecao.Ciclofaixa;
            bool walk = t == TipoElementoSecao.Calcada;
            Show(parking, LblVaga, CbVaga);
            Show(bus, LblLegenda, TbLegenda);
            Show(bus || bike, LblEsp, TbEspacamento);
            Show(walk, LblServico, TbServico, LblAcesso, TbAcesso, CkGramado);
            Show(bike, CkFundo, CkBidirecional);
            Show(bus, CkFundoOnibus, LblCorOnibus, CbCorOnibus);
            Show(t is not (TipoElementoSecao.Calcada or TipoElementoSecao.FaixaSeguranca), LblDisp, CbDispositivo);
            Show(walk, LblSarjeta, TbSarjeta);
            Show(t == TipoElementoSecao.FaixaCaminhada, LblCorCaminhada, CbCorCaminhada, LblEsp, TbEspacamento);
            if (bus || bike) Show(true, LblEsp, TbEspacamento);
            Show(bike, LblLarguraLinha, TbLarguraLinha, CkSeccionada, LblTraco, PanelTraco, LblSimbolo, PanelSimbolo, LblDistSeta, TbDistSeta, CkLinhaCentral);

            Select(CbVaga, e.Vaga);
            TbLegenda.Text = e.Legenda;
            TbEspacamento.Text = UiHelpers.F(e.Espacamento, "0.#");
            TbServico.Text = UiHelpers.F(e.FaixaServico);
            TbAcesso.Text = UiHelpers.F(e.FaixaAcesso);
            CkGramado.IsChecked = e.ServicoGramado;
            CkFundo.IsChecked = e.PinturaFundo;
            CkBidirecional.IsChecked = e.Bidirecional;
            CkFundoOnibus.IsChecked = e.FundoOnibus;
            Select(CbCorOnibus, e.CorOnibus);
            Select(CbDispositivo, e.Dispositivo);
            TbSarjeta.Text = UiHelpers.F(e.Sarjeta);
            Select(CbCorCaminhada, e.CorCaminhada);
            TbLarguraLinha.Text = UiHelpers.F(e.LarguraLinha);
            CkSeccionada.IsChecked = e.LinhaSeccionada;
            TbTraco.Text = UiHelpers.F(e.TracoLinha);
            TbEspacoLinha.Text = UiHelpers.F(e.EspacoLinha);
            TbTamSimbolo.Text = UiHelpers.F(e.TamanhoSimbolo);
            TbTamSeta.Text = UiHelpers.F(e.TamanhoSeta);
            TbDistSeta.Text = UiHelpers.F(e.DistanciaSeta);
            CkLinhaCentral.IsChecked = e.LinhaCentral;
        }
        finally
        {
            _loadingDetails = false;
        }
    }

    private static void Show(bool visible, params UIElement[] elements)
    {
        foreach (var el in elements) el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DetailChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingDetails || _selected == null) return;
        var el = _selected.Element;
        if (CbVaga.SelectedItem is Option<string> v) el.Vaga = v.Value;
        el.Legenda = TbLegenda.Text.Trim();
        el.Espacamento = UiHelpers.ParseOpt(TbEspacamento.Text) is { } sp and >= 0 ? sp : el.Espacamento;
        el.FaixaServico = UiHelpers.ParseOpt(TbServico.Text) is { } fs and >= 0 ? fs : el.FaixaServico;
        el.FaixaAcesso = UiHelpers.ParseOpt(TbAcesso.Text) is { } fa and >= 0 ? fa : el.FaixaAcesso;
        el.ServicoGramado = CkGramado.IsChecked == true;
        el.PinturaFundo = CkFundo.IsChecked == true;
        el.Bidirecional = CkBidirecional.IsChecked == true;
        el.FundoOnibus = CkFundoOnibus.IsChecked == true;
        if (CbCorOnibus.SelectedItem is Option<MarkingColor> co) el.CorOnibus = co.Value;
        el.Dispositivo = Selected<string?>(CbDispositivo);
        double Num(TextBox tb, double current, double min = 0) => UiHelpers.ParseOpt(tb.Text) is { } v && v >= min ? v : current;
        el.Sarjeta = Num(TbSarjeta, el.Sarjeta);
        if (CbCorCaminhada.SelectedItem is Option<MarkingColor> cc) el.CorCaminhada = cc.Value;
        el.LarguraLinha = Num(TbLarguraLinha, el.LarguraLinha, 0.02);
        el.LinhaSeccionada = CkSeccionada.IsChecked == true;
        el.TracoLinha = Num(TbTraco, el.TracoLinha, 0.05);
        el.EspacoLinha = Num(TbEspacoLinha, el.EspacoLinha);
        el.TamanhoSimbolo = Num(TbTamSimbolo, el.TamanhoSimbolo, 0.2);
        el.TamanhoSeta = Num(TbTamSeta, el.TamanhoSeta);
        el.DistanciaSeta = Num(TbDistSeta, el.DistanciaSeta);
        el.LinhaCentral = CkLinhaCentral.IsChecked == true;
        SchedulePreview();
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            GridRight.CommitEdit(DataGridEditingUnit.Row, true);
            GridLeft.CommitEdit(DataGridEditingUnit.Row, true);
            Setup = BuildSetup();
            Setup.CornerRadius = CornerRadius;
            if (Setup.Hierarchy == HierarquiaViaria.NaoDefinida)
            {
                UiHelpers.Error("Defina a hierarquia viária da via (CTB art. 60): trânsito rápido, arterial, coletora, local, rodovia ou estrada.");
                CbHierarchy.Focus();
                return;
            }
            OutputSettings = Output.Save();
            DrawPath = RbDraw.IsChecked == true;
            Connection = Selected<TipoConexao>(CbConnection);
            FreeEnds = Selected<FimLivre>(CbFreeEnds);
            Snap = CkSnap.IsChecked == true;
            CurveRadius = UiHelpers.Parse(TbCurveRadius, 0, "Raio das curvas", 0, 5000);
            var st = PluginContext.Settings;
            st.LastRoadSetup = RoadTemplates.ToJson(Setup);
            st.AutoCrosswalks = CkIntCrosswalks.IsChecked == true;
            st.LastConnection = Connection;
            st.LastFreeEnds = FreeEnds;
            st.LastCurveRadius = CurveRadius;
            PluginContext.Settings.DefaultSpeed = Setup.Speed;
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
