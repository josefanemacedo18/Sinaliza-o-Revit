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
            OnChanged(nameof(AlturaTexto));
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

    /// <summary>Nível do topo (m) em relação à pista; vazio = padrão do tipo (calçada/canteiro 0,15; faixas 0).</summary>
    public string AlturaTexto
    {
        get => UiHelpers.F(Element.AlturaEfetiva);
        set
        {
            if (string.IsNullOrWhiteSpace(value)) Element.Altura = null;
            else if (UiHelpers.ParseOpt(value) is { } v and >= -2 and <= 5)
                Element.Altura = Math.Abs(v - ElementoSecao.AlturaPadrao(Element.Tipo)) < 1e-6 ? null : v;
            OnChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Linha da tabela de larguras medidas (texto em pt-BR; vazio = medida da seção).</summary>
public sealed class WidthRow
{
    public PontoLargura P { get; }
    public WidthRow(PontoLargura p) => P = p;
    private static string T(double? v) => v is { } x ? UiHelpers.F(x) : "";
    private static double? V(string? t) => UiHelpers.ParseOpt(t) is { } x and > 0 and < 200 ? x : null;
    public string Estaca { get => UiHelpers.F(P.Estaca, "0.##"); set { if (PontoLargura.ParseEstaca(value) is { } e && e >= 0) P.Estaca = e; } }
    public string BordoEsq { get => T(P.BordoEsquerdo); set => P.BordoEsquerdo = V(value); }
    public string BordoDir { get => T(P.BordoDireito); set => P.BordoDireito = V(value); }
    public string AlinhEsq { get => T(P.AlinhamentoEsquerdo); set => P.AlinhamentoEsquerdo = V(value); }
    public string AlinhDir { get => T(P.AlinhamentoDireito); set => P.AlinhamentoDireito = V(value); }
}

/// <summary>Linha da tabela de recuos (baias, faixas auxiliares).</summary>
public sealed class RecessRow
{
    public RecuoVia R { get; }
    public RecessRow(RecuoVia r) => R = r;
    private static double N(string? t, double cur, double min = 0) => UiHelpers.ParseOpt(t) is { } x && x >= min ? x : cur;
    public string TipoTexto => RecuoVia.Rotulo(R.Tipo);
    public bool Esquerdo { get => R.LadoEsquerdo; set => R.LadoEsquerdo = value; }
    public string Estaca { get => UiHelpers.F(R.Estaca, "0.##"); set { if (PontoLargura.ParseEstaca(value) is { } e && e >= 0) R.Estaca = e; } }
    public string Comprimento { get => UiHelpers.F(R.Comprimento, "0.##"); set => R.Comprimento = N(value, R.Comprimento); }
    public string Profundidade { get => UiHelpers.F(R.Profundidade); set => R.Profundidade = N(value, R.Profundidade, 0.1); }
    public string TaperIn { get => UiHelpers.F(R.TaperEntrada, "0.##"); set => R.TaperEntrada = N(value, R.TaperEntrada); }
    public string TaperOut { get => UiHelpers.F(R.TaperSaida, "0.##"); set => R.TaperSaida = N(value, R.TaperSaida); }
    public bool CalcadaRecua { get => R.CalcadaRecua; set => R.CalcadaRecua = value; }
    public bool Curva { get => R.CurvaReversa; set => R.CurvaReversa = value; }
}

/// <summary>Monta a seção transversal completa e gera toda a via (sinalização + calçadas, canteiros e dispositivos).</summary>
public partial class RoadWindow : Window
{
    private readonly Catalogo _cat = PluginContext.Catalog;
    private readonly ObservableCollection<SectionRow> _right = new();
    private readonly ObservableCollection<SectionRow> _left = new();
    private SectionRow? _selected;
    private readonly ObservableCollection<WidthRow> _widths = new();
    private readonly ObservableCollection<RecessRow> _recesses = new();

    /// <summary>Ler meios-fios e muros existentes do desenho depois de indicar o eixo.</summary>
    public bool ReadSurvey => CkReadSurvey.IsChecked == true;
    private bool _loading = true;
    private bool _loadingDetails;
    private bool _editing;
    /// <summary>Seção carregada na edição: retornos, travessias e extensões de calçada passam para a seção nova.</summary>
    private RoadSetup? _loaded;

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
    /// <summary>Relevo escolhido para a via sobre o Toposolid.</summary>
    public RelevoVia Relief { get; private set; } = RelevoVia.Plana;
    public bool DrawPath { get; private set; }
    /// <summary>Eixos reconhecidos em pisos existentes (a seção é ajustada à largura medida em cada via).</summary>
    public bool FromFloors { get; private set; }
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
        RbFloors.Checked += (_, _) =>
        {
            foreach (var it in CbPavement.Items)
                if (it is PavementOption { Value: TipoPavimento.Nenhum }) CbPavement.SelectedItem = it;
        };
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
        CbRelief.Items.Add(new Option<RelevoVia>("Acompanhar o terreno (corte e aterro só na plataforma)", RelevoVia.AcompanharTerreno));
        CbRelief.Items.Add(new Option<RelevoVia>("Greide suavizado com corte e aterro (rampa máxima)", RelevoVia.GreideSuavizado));
        CbRelief.Items.Add(new Option<RelevoVia>("Plana, sem alterar o terreno", RelevoVia.Plana));
        Select(CbRelief, st.RoadRelief);
        TbReliefGrade.Text = UiHelpers.F(st.RoadReliefMaxGrade * 100, "0.#");
        TbCutSlope.Text = UiHelpers.F(st.RoadCutSlope, "0.0#");
        TbFillSlope.Text = UiHelpers.F(st.RoadFillSlope, "0.0#");
        CkIntCrosswalks.IsChecked = st.AutoCrosswalks;
        CkIntRamps.IsChecked = st.AutoCrosswalks;

        var types = Enum.GetValues<TipoElementoSecao>().Select(t => new TypeOption(t, ElementoSecao.Rotulo(t))).ToList();
        ColTypeRight.ItemsSource = types;
        ColTypeLeft.ItemsSource = types;
        GridRight.ItemsSource = _right;
        GridLeft.ItemsSource = _left;

        FillTemplates(null);
        GridWidths.ItemsSource = _widths;
        GridRecess.ItemsSource = _recesses;
        foreach (var (l, v) in new[] { ("Elemento junto ao meio-fio", AbsorcaoLargura.ElementoJuntoAoBordo), ("Última faixa de tráfego", AbsorcaoLargura.UltimaFaixaDeTrafego),
                     ("Todas as faixas (proporcional)", AbsorcaoLargura.TodasAsFaixas), ("Faixa de estacionamento", AbsorcaoLargura.Estacionamento) })
            CbAbsorb.Items.Add(new Option<AbsorcaoLargura>(l, v));
        CbAbsorb.SelectedIndex = 0;
        foreach (var t in Enum.GetValues<TipoRecuo>()) CbRecessType.Items.Add(new Option<TipoRecuo>(RecuoVia.Rotulo(t), t));
        CbRecessType.SelectedIndex = 0;

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
        {
            CbCorOnibus.Items.Add(new Option<MarkingColor>(n, c));
            CbCorCruz.Items.Add(new Option<MarkingColor>(n, c));
        }

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
        CbPavement.Items.Add(new PavementOption("Terra (leito natural – sem pintura)", TipoPavimento.Terra));
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

    /// <summary>Abre a janela com a seção de uma via existente (Editar → via inteira).</summary>
    public void LoadForEdit(RoadSetup s)
    {
        _editing = true;
        _loaded = s.Clone();
        Title = "Editar via – seção transversal";
        LoadSetup(s);
        if (CbTemplate.Items.Count > 0) CbTemplate.SelectedIndex = -1;
        RbCurves.IsChecked = true;
        RbDraw.IsEnabled = false;
        RbCurves.IsEnabled = false;
        RbFloors.IsEnabled = false;
        CkSnap.IsEnabled = false;
        BtnOk.Content = "Aplicar à via";
        SchedulePreview();
    }

    private void LoadSetup(RoadSetup s)
    {
        var was = _loading;
        _loading = true;
        Select(CbHierarchy, s.Hierarchy);
        CkTactile.IsChecked = s.PisoTatil;
        // Via existente sem a opção: mantém (sem setas); via nova: com setas na mão única.
        CkArrows.IsChecked = s.SetasSentido ?? !_editing;
        // Sinalização automática: via nova com os três grupos; via existente sem a opção, desligada.
        var sv = s.Sinalizacao ?? (_editing ? null : new SinalizacaoAutomatica());
        CkAutoSpeed.IsChecked = sv?.Velocidade == true;
        CkAutoOneWay.IsChecked = sv?.Sentido == true;
        CkAutoCrossing.IsChecked = sv?.Travessias == true;
        TbCornerRadius.Text = s.CornerRadius is { } cr ? UiHelpers.F(cr) : "";
        RbTwoWay.IsChecked = s.TwoWay;
        RbOneWay.IsChecked = !s.TwoWay;
        Select(CbCenter, s.Center);
        TbMedian.Text = UiHelpers.F(s.MedianWidth);
        Select(CbMedianType, s.MedianType);
        TbMedianHeight.Text = UiHelpers.F(s.MedianHeight);
        TbMedianVeg.Text = s.MedianVegetationHeight is { } mv ? UiHelpers.F(mv) : "";
        Select(CbMedianDevice, s.MedianDevice);
        CkInvertCenter.IsChecked = s.InvertCenter;
        // Via existente: mantém o que ela tinha (vias antigas: sarjeta dentro da faixa); via nova: somada.
        CkGutterAdds.IsChecked = s.SarjetaSomada ?? !_editing;
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
        _widths.Clear();
        foreach (var p in s.LargurasVariaveis.OrderBy(p => p.Estaca)) _widths.Add(new WidthRow(p.Clone()));
        _stretches.Clear();
        _stretches.AddRange(s.TrechosLargura.Select(t => t.Clone()));
        UpdateStretchText();
        _recesses.Clear();
        foreach (var r in s.Recuos) _recesses.Add(new RecessRow(r.Clone()));
        Select(CbAbsorb, s.Absorcao);
        CkSmoothWidth.IsChecked = s.TransicaoSuave;
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
            MedianHeight = UiHelpers.Parse(TbMedianHeight, RoadSetup.CurbHeight, "Nível do canteiro central", -2, 5),
            MedianVegetationHeight = UiHelpers.ParseOpt(TbMedianVeg.Text) is { } mvh and >= -2 and <= 5 ? mvh : null,
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
            SarjetaSomada = CkGutterAdds.IsChecked == true,
            Pavement = (CbPavement.SelectedItem as PavementOption)?.Value ?? TipoPavimento.Asfalto,
            PavementThickness = UiHelpers.ParseNullable(TbPavThickness, "Espessura do pavimento", 0.01, 2),
        };
        if (s.Right.Count == 0 && s.Left.Count == 0) throw new FormatException("Adicione ao menos um elemento à seção.");
        s.LargurasVariaveis = _widths.Select(w => w.P.Clone()).OrderBy(p => p.Estaca).ToList();
        s.Recuos = _recesses.Select(r => r.R.Clone()).ToList();
        s.PisoTatil = CkTactile.IsChecked == true;
        s.SetasSentido = CkArrows.IsChecked == true;
        var auto = new SinalizacaoAutomatica
        {
            Velocidade = CkAutoSpeed.IsChecked == true, Sentido = CkAutoOneWay.IsChecked == true, Travessias = CkAutoCrossing.IsChecked == true,
        };
        s.Sinalizacao = auto.Empty ? null : auto;
        s.TrechosLargura = _stretches.Select(t => t.Clone()).ToList();
        if (_loaded != null)
        {
            // O que fica gravado na via e não aparece nesta janela segue com ela.
            s.Retornos = _loaded.Retornos.Select(r => r.Clone()).ToList();
            s.TravessiasCanteiro = _loaded.TravessiasCanteiro.Select(t => t.Clone()).ToList();
            s.ExtensoesCalcada = _loaded.ExtensoesCalcada.Select(e => e.Clone()).ToList();
            s.FaixaUnicaCentrada = _loaded.FaixaUnicaCentrada && !s.TwoWay;
            s.EspacamentoSetas = _loaded.EspacamentoSetas;
        }
        s.Absorcao = Selected<AbsorcaoLargura>(CbAbsorb);
        s.TransicaoSuave = CkSmoothWidth.IsChecked == true;
        if (CkStuds.IsChecked == true && CbStuds.SelectedItem is string st)
        {
            var parts = st.Split(" | ");
            s.CenterStudsCode = parts[0];
            s.CenterStudsVariant = parts.Length > 1 ? parts[1] : null;
        }
        return s;
    }

    /// <summary>Gera as definições para o caminho escolhido (os avisos ficam em <see cref="RoadSetup.Warnings"/>).</summary>
    public List<MarkingDefinition> BuildDefinitions(PathReference path, Polyline2? axis = null) => Setup!.Build(path, OutputSettings!, _cat, axis: axis);
    public List<MarkingDefinition> BuildDefinitions(RoadSetup setup, PathReference path, Polyline2? axis) => setup.Build(path, OutputSettings!, _cat, axis: axis);

    // ------------------------------------------------------------------ largura variável e recuos

    private void AddWidthClick(object sender, RoutedEventArgs e)
    {
        var next = _widths.Count == 0 ? 0 : _widths.Max(w => w.P.Estaca) + 20;
        _widths.Add(new WidthRow(new PontoLargura { Estaca = next }));
        SchedulePreview();
    }

    private void RemoveWidthClick(object sender, RoutedEventArgs e)
    {
        if (GridWidths.SelectedItem is WidthRow w) _widths.Remove(w);
        SchedulePreview();
    }

    private void ClearWidthsClick(object sender, RoutedEventArgs e)
    {
        _widths.Clear();
        SchedulePreview();
    }

    /// <summary>Medida "de meio-fio a meio-fio" numa estaca, repartida entre os lados.</summary>
    /// <summary>Trechos com outra largura (mesma janela da ferramenta Alterar Largura por Trecho, com as estacas digitadas).</summary>
    private readonly List<TrechoLargura> _stretches = new();

    private void AddStretchClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = BuildSetup();
            var start = _stretches.Count == 0 ? 20 : _stretches.Max(x => x.Fim) + 20;
            var t = new TrechoLargura { EstacaA = start + 10, EstacaB = start + 40 };
            if (!Commands.WidthStretchForm.Show(s, t, "Alterar largura por trecho")) return;
            _stretches.RemoveAll(x => x.Elemento == t.Elemento && x.Fim >= t.Inicio && x.Inicio <= t.Fim);
            _stretches.Add(t);
            UpdateStretchText();
            SchedulePreview();
        }
        catch (FormatException ex) { UiHelpers.Error(ex.Message); }
        catch (UserMessageException ex) { UiHelpers.Error(ex.Message); }
    }

    private void ClearStretchesClick(object sender, RoutedEventArgs e)
    {
        _stretches.Clear();
        UpdateStretchText();
        SchedulePreview();
    }

    private void UpdateStretchText()
    {
        if (TxtStretches == null) return;
        TxtStretches.Text = _stretches.Count == 0 ? "Nenhum trecho com largura alterada." : string.Join("\n", _stretches.OrderBy(x => x.A).Select(x =>
            $"• {TrechoLargura.Rotulo(x.Elemento)}: {UiHelpers.F(x.NovaLargura)} m de {PontoLargura.FormatEstaca(x.A)} (A) a {PontoLargura.FormatEstaca(x.B)} (B), " +
            $"transições {UiHelpers.F(x.TransicaoA)} / {UiHelpers.F(x.TransicaoB)} m"));
    }

    private void AddRecessClick(object sender, RoutedEventArgs e)
    {
        var t = Selected<TipoRecuo>(CbRecessType);
        var speed = UiHelpers.ParseOpt(TbSpeed.Text) ?? 50;
        var lane = _right.Where(r => ElementoSecao.EhFaixaDeTrafego(r.Tipo)).Select(r => r.Element.Largura).DefaultIfEmpty(3.5).Last();
        var r = RecuoVia.Padrao(t, speed, lane);
        r.Estaca = _recesses.Count == 0 ? 20 : _recesses.Max(x => x.R.S3) + 20;
        _recesses.Add(new RecessRow(r));
        SchedulePreview();
    }

    private void RemoveRecessClick(object sender, RoutedEventArgs e)
    {
        if (GridRecess.SelectedItem is RecessRow r) _recesses.Remove(r);
        SchedulePreview();
    }

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
            var len = Math.Max(50, Math.Max(s.LargurasVariaveis.Select(p => p.Estaca).DefaultIfEmpty(0).Max(), s.Recuos.Select(r => r.S3).DefaultIfEmpty(0).Max()) + 10);
            var sample = new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) });
            var ctx = new BuildContext { Catalog = _cat, Glyphs = PluginContext.Glyphs };
            var geo = new MarkingGeometry();
            var defs = s.Build(new PathReference(), o, _cat, axis: sample);
            foreach (var d in defs) geo.Merge(MarkingBuilder.Build(d, sample, ctx));
            var half = s.TwoWay && s.Center == CenterTreatment.Canteiro ? s.MedianWidth / 2 : 0;
            var pav = Polygon2.Rectangle(new Vec2(0, -(s.SideWidth(s.Right) + half)), new Vec2(len, s.SideWidth(s.Left) + half));
            Preview.Show(geo, new[] { sample.Points }, new[] { pav });

            var counts = defs.GroupBy(d => d.DisplayCode).Select(g => $"{g.Count()}× {g.Key}");
            var warnings = s.Warnings.Concat(geo.Warnings).Distinct().ToList();
            var wd = s.Widths();
            TxtWidths.Text =
                $"Largura total (alinhamento a alinhamento) ......... {UiHelpers.F(wd.Total),7} m\n" +
                $"Pista entre as faces dos meios-fios ............... {UiHelpers.F(wd.EntreMeiosFios),7} m\n" +
                $"Pista incluindo os meios-fios ..................... {UiHelpers.F(wd.ComMeiosFios),7} m\n" +
                $"  faixas de tráfego {UiHelpers.F(wd.Faixas)} · sarjetas {UiHelpers.F(wd.Sarjetas)}{(s.SarjetaSomada == true ? " (somadas)" : " (dentro das faixas)")}" +
                $" · meios-fios {UiHelpers.F(wd.MeiosFios)} · calçadas {UiHelpers.F(wd.Calcadas)}" +
                (wd.Canteiros > 0 ? $" · canteiros laterais {UiHelpers.F(wd.Canteiros)}" : "") + (wd.Central > 0 ? $" · canteiro central {UiHelpers.F(wd.Central)}" : "");
            if (s.HasVariableWidth)
            {
                var rg = s.WidthRange();
                TxtWidths.Text += $"\nLargura variável: entre meios-fios de {UiHelpers.F(rg.MinEntre)} a {UiHelpers.F(rg.MaxEntre)} m; total de {UiHelpers.F(rg.MinTotal)} a {UiHelpers.F(rg.MaxTotal)} m" +
                                  (s.Recuos.Count > 0 ? $" · {s.Recuos.Count} recuo(s)" : "");
            }
            var (nbl, nal, _) = s.Nominal(true);
            var (nbr, nar, _) = s.Nominal(false);
            TxtNominal.Text = $"Seção atual (do eixo): bordo esq. {UiHelpers.F(nbl)} m · dir. {UiHelpers.F(nbr)} m; alinhamento esq. {UiHelpers.F(nal)} m · dir. {UiHelpers.F(nar)} m.";
            TxtSummary.Text = $"Elementos gerados: {string.Join(", ", counts)}." +
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
        TbMedianHeight.IsEnabled = median;
        TbMedianVeg.IsEnabled = median;
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
            Show(parking && !e.SoDelimitado, LblVaga, CbVaga);
            Show(parking, CkSoDelimitado);
            Show(bus, LblLegenda, TbLegenda);
            Show(bus || bike, LblEsp, TbEspacamento);
            Show(walk, LblServico, TbServico, LblAcesso, TbAcesso, CkGramado);
            Show(bike, CkFundo, CkBidirecional);
            Show(bus, CkFundoOnibus, LblCorOnibus, CbCorOnibus);
            Show(t is not (TipoElementoSecao.Calcada or TipoElementoSecao.FaixaSeguranca), LblDisp, CbDispositivo);
            Show(walk, LblSarjeta, TbSarjeta, LblMeioFio, TbMeioFio, LblInclinacao, TbInclinacao, LblNiveis, TbNiveis, LblNiveisHint);
            LblAltura.Text = walk ? "Altura do meio-fio / nível da calçada (m)" : "Nível do topo em relação à pista (m)";
            Show(walk || t == TipoElementoSecao.CanteiroFisico, LblVegetacao, TbVegetacao);
            Show(true, LblAltura, TbAltura);
            Show(t == TipoElementoSecao.FaixaCaminhada, LblCorCaminhada, CbCorCaminhada, LblEsp, TbEspacamento);
            if (bus || bike) Show(true, LblEsp, TbEspacamento);
            Show(bike, LblLarguraLinha, TbLarguraLinha, CkSeccionada, LblTraco, PanelTraco, LblSimbolo, PanelSimbolo, LblDistSeta, TbDistSeta, CkLinhaCentral,
                CkCruzColorido, LblCorCruz, CbCorCruz, LblZona, TbZona);
            if (parking && e.SoDelimitado) Show(true, LblLarguraLinha, TbLarguraLinha, LblTraco, PanelTraco);
            CkSoDelimitado.IsChecked = e.SoDelimitado;

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
            TbMeioFio.Text = UiHelpers.F(e.LarguraMeioFio);
            TbInclinacao.Text = UiHelpers.F(e.InclinacaoTransversal, "0.##");
            TbNiveis.Text = NivelAlinhamento.Format(e.NiveisAlinhamento);
            Select(CbCorCaminhada, e.CorCaminhada);
            TbLarguraLinha.Text = UiHelpers.F(parking ? e.LarguraDelimitacao ?? 0.10 : e.LarguraLinha);
            CkSeccionada.IsChecked = e.LinhaSeccionada;
            TbTraco.Text = UiHelpers.F(e.TracoLinha);
            TbEspacoLinha.Text = UiHelpers.F(e.EspacoLinha);
            TbTamSimbolo.Text = UiHelpers.F(e.TamanhoSimbolo);
            TbTamSeta.Text = UiHelpers.F(e.TamanhoSeta);
            TbDistSeta.Text = UiHelpers.F(e.DistanciaSeta);
            CkLinhaCentral.IsChecked = e.LinhaCentral;
            CkCruzColorido.IsChecked = e.CruzamentoColorido;
            Select(CbCorCruz, e.CorCruzamento ?? MarkingColor.Vermelha);
            TbZona.Text = UiHelpers.F(e.ZonaConflitoEfetiva, "0.#");
            TbAltura.Text = UiHelpers.F(e.AlturaEfetiva);
            TbVegetacao.Text = e.AlturaVegetacao is { } av ? UiHelpers.F(av) : "";
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
        el.LarguraMeioFio = Num(TbMeioFio, el.LarguraMeioFio, 0.05);
        el.InclinacaoTransversal = UiHelpers.ParseOpt(TbInclinacao.Text) is { } inc and >= -8 and <= 8 ? inc : el.InclinacaoTransversal;
        if (sender == TbNiveis) el.NiveisAlinhamento = NivelAlinhamento.Parse(TbNiveis.Text);
        if (CbCorCaminhada.SelectedItem is Option<MarkingColor> cc) el.CorCaminhada = cc.Value;
        if (el.Tipo == TipoElementoSecao.Estacionamento)
        {
            el.SoDelimitado = CkSoDelimitado.IsChecked == true;
            el.LarguraDelimitacao = Num(TbLarguraLinha, el.LarguraDelimitacao ?? 0.10, 0.02);
        }
        else el.LarguraLinha = Num(TbLarguraLinha, el.LarguraLinha, 0.02);
        el.LinhaSeccionada = CkSeccionada.IsChecked == true;
        el.TracoLinha = Num(TbTraco, el.TracoLinha, 0.05);
        el.EspacoLinha = Num(TbEspacoLinha, el.EspacoLinha);
        el.TamanhoSimbolo = Num(TbTamSimbolo, el.TamanhoSimbolo, 0.2);
        el.TamanhoSeta = Num(TbTamSeta, el.TamanhoSeta);
        el.DistanciaSeta = Num(TbDistSeta, el.DistanciaSeta);
        el.LinhaCentral = CkLinhaCentral.IsChecked == true;
        if (el.Tipo == TipoElementoSecao.Ciclofaixa)
        {
            el.CruzamentoColorido = CkCruzColorido.IsChecked == true;
            if (CbCorCruz.SelectedItem is Option<MarkingColor> cz) el.CorCruzamento = cz.Value == MarkingColor.Vermelha && el.CorCruzamento == null ? null : cz.Value;
            if (sender == TbZona && UiHelpers.ParseOpt(TbZona.Text) is { } zc and >= 0 and <= 200) el.ZonaConflito = Math.Abs(zc - 20) < 1e-9 && el.ZonaConflito == null ? null : zc;
        }
        if (sender == TbAltura) _selected.AlturaTexto = TbAltura.Text;
        el.AlturaVegetacao = UiHelpers.ParseOpt(TbVegetacao.Text) is { } veg and >= -2 and <= 5 ? veg : null;
        if (sender == CkSoDelimitado) ShowDetails(_selected);
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
            FromFloors = RbFloors.IsChecked == true;
            Connection = Selected<TipoConexao>(CbConnection);
            FreeEnds = Selected<FimLivre>(CbFreeEnds);
            Snap = CkSnap.IsChecked == true;
            CurveRadius = UiHelpers.Parse(TbCurveRadius, 0, "Raio das curvas", 0, 5000);
            var st = PluginContext.Settings;
            // A próxima via reabre com a mesma seção, mas sem as medidas e recuos (que são desta via).
            var last = Setup.Clone();
            last.LargurasVariaveis = new();
            last.Recuos = new();
            st.LastRoadSetup = RoadTemplates.ToJson(last);
            st.AutoCrosswalks = CkIntCrosswalks.IsChecked == true;
            st.LastConnection = Connection;
            st.LastFreeEnds = FreeEnds;
            st.LastCurveRadius = CurveRadius;
            Relief = Selected<RelevoVia>(CbRelief);
            st.RoadRelief = Relief;
            st.RoadReliefMaxGrade = UiHelpers.Parse(TbReliefGrade, 8, "Rampa máxima", 0.5, 30) / 100;
            st.RoadCutSlope = UiHelpers.Parse(TbCutSlope, 1, "Talude de corte", 0.2, 5);
            st.RoadFillSlope = UiHelpers.Parse(TbFillSlope, 1.5, "Talude de aterro", 0.5, 5);
            PluginContext.Settings.DefaultSpeed = Setup.Speed;
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }
}
