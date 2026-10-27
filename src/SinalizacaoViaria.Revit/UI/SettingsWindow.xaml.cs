using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Settings;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.UI;

/// <summary>
/// Configurações (Etapa 12; Prompt de Interface §19 e §26): as do usuário (este computador), editáveis, validadas todas antes de
/// salvar e com o que muda ao salvar; as do projeto e de cada elemento, com onde ficam e o comando que as edita; os perfis
/// reutilizáveis (exemplo, do usuário, com referência citada); e onde fica cada configuração. O conteúdo vem de
/// <see cref="RegistroConfiguracoes"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    public sealed record LinhaRegistro(string Grupo, string Configuracao, string Onde, string Valor, string Afeta, string? Comando);
    public sealed record LinhaPerfil(string Categoria, string Nome, string Tipo, string Fonte, string Resumo);

    /// <summary>O usuário pediu o Autoteste (roda depois que a janela fecha).</summary>
    public bool RunAutoTest { get; private set; }

    /// <summary>Comando escolhido para editar uma configuração do projeto (roda depois que a janela fecha).</summary>
    public string? Comando { get; private set; }

    private readonly IReadOnlyList<ItemConfiguracao>? _projeto;
    private readonly string? _nomeDoProjeto;
    /// <summary>Perfis de limites em edição (gravados só ao salvar).</summary>
    private readonly List<PerfilCriterios> _perfis;
    private bool _limparJanelas;

    private void AutoTestClick(object sender, RoutedEventArgs e)
    {
        if (!Descartar("rodar o Autoteste")) return;
        RunAutoTest = true;
        DialogResult = false;
    }

    /// <param name="projeto">Configurações do projeto e dos elementos (<see cref="RegistroConfiguracoes.DoProjeto"/>); nulo = sem projeto aberto.</param>
    public SettingsWindow(IReadOnlyList<ItemConfiguracao>? projeto = null, string? nomeDoProjeto = null)
    {
        InitializeComponent();
        _projeto = projeto;
        _nomeDoProjeto = nomeDoProjeto;
        var s = PluginContext.Settings;
        _perfis = s.Clone().PerfisCriterios;
        CbMode.Items.Add("Modelo 3D");
        CbMode.Items.Add("Detalhe 2D");
        CbMode.SelectedIndex = s.DefaultMode == OutputMode.Modelo3D ? 0 : 1;
        foreach (var m in PluginContext.Catalog.Materiais) CbMaterial.Items.Add(m.Nome);
        CbMaterial.SelectedItem = s.DefaultMaterial;
        TbSpeed.Text = UiHelpers.F(s.DefaultSpeed, "0");
        TbFont.Text = s.FontFamily;
        CkBold.IsChecked = s.FontBold;
        TbMinThickness.Text = UiHelpers.F(s.MinModelThickness, "0.####");
        TbElevation.Text = UiHelpers.F(s.ElevationOffset, "0.####");
        CkDrape.IsChecked = s.DrapeByDefault;
        TbPiece.Text = UiHelpers.F(s.DrapePieceLength, "0.##");
        CkAuto.IsChecked = s.AutoUpdate;
        CkAutoIntersect.IsChecked = s.AutoIntersect;
        CkAutoConnect.IsChecked = s.AutoConnect;
        CkAutoCrosswalks.IsChecked = s.AutoCrosswalks;
        CkFloors.IsChecked = s.PhysicalAsFloors;
        CkRampFloors.IsChecked = s.RampsAsFloors;
        CkPaintFloors.IsChecked = s.PaintAsFloors;
        TbBoundaryTol.Text = UiHelpers.F(s.BoundaryTolerance * 1000, "0.##");
        CkBoundary.IsChecked = s.VisibleBoundary2D;
        CkRealistic.IsChecked = s.RealisticAppearance;
        MostrarCriterios(s.CriteriosGeometricos ?? new GeometricCriteria());
        TbCatalog.Text = PluginContext.UserCatalogPath;
        UpdateStatus();
        FillPerfis(null);
        Janelas();
        Validacao(RegistroConfiguracoes.Validar(s, Materiais, File.Exists));
        Projeto();
        Perfis();
        Registro();
    }

    private static IEnumerable<string> Materiais => PluginContext.Catalog.Materiais.Select(m => m.Nome);

    private Style? S(string key) => TryFindResource(key) as Style;

    // ---------------------------------------------------------------- limites geométricos e perfis

    private void MostrarCriterios(GeometricCriteria c)
    {
        TbCritFonte.Text = c.Fonte ?? "";
        TbCritRaio.Text = UiHelpers.F(c.RaioMinimo, "0.##");
        TbCritTangente.Text = UiHelpers.F(c.TangenteMinima, "0.##");
        TbCritDeflexao.Text = UiHelpers.F(c.DeflexaoMaximaSemCurva, "0.###");
        TbCritRampaMax.Text = UiHelpers.F(c.RampaMaxima, "0.###");
        TbCritRampaMin.Text = UiHelpers.F(c.RampaMinima, "0.###");
        TbCritKConvexa.Text = UiHelpers.F(c.KMinimoConvexa, "0.##");
        TbCritKConcava.Text = UiHelpers.F(c.KMinimoConcava, "0.##");
        TbCritCurvaVert.Text = UiHelpers.F(c.CurvaVerticalMinima, "0.##");
        TbCritTerreno.Text = UiHelpers.F(c.DeclividadeMaximaTerreno, "0.##");
    }

    /// <summary>Os limites dos campos; valor fora da faixa ou não numérico lança <see cref="FormatException"/> com o nome do campo.</summary>
    private GeometricCriteria LerCriterios() => new()
    {
        Fonte = string.IsNullOrWhiteSpace(TbCritFonte.Text) ? null : TbCritFonte.Text.Trim(),
        RaioMinimo = UiHelpers.ParseNullable(TbCritRaio, "Raio mínimo", 0, 100000),
        TangenteMinima = UiHelpers.ParseNullable(TbCritTangente, "Tangente mínima", 0, 100000),
        DeflexaoMaximaSemCurva = UiHelpers.ParseNullable(TbCritDeflexao, "Deflexão máxima sem curva", 0, 180),
        RampaMaxima = UiHelpers.ParseNullable(TbCritRampaMax, "Rampa máxima", 0, 100),
        RampaMinima = UiHelpers.ParseNullable(TbCritRampaMin, "Rampa mínima", 0, 100),
        KMinimoConvexa = UiHelpers.ParseNullable(TbCritKConvexa, "K mínimo convexa", 0, 100000),
        KMinimoConcava = UiHelpers.ParseNullable(TbCritKConcava, "K mínimo côncava", 0, 100000),
        CurvaVerticalMinima = UiHelpers.ParseNullable(TbCritCurvaVert, "Comprimento mínimo de curva vertical", 0, 100000),
        DeclividadeMaximaTerreno = UiHelpers.ParseNullable(TbCritTerreno, "Declividade máxima do terreno", 0, 1000),
    };

    private PerfilCriterios? PerfilEscolhido => CbPerfil.SelectedIndex > 0 && CbPerfil.SelectedIndex - 1 < _perfis.Count ? _perfis[CbPerfil.SelectedIndex - 1] : null;

    private void FillPerfis(string? escolher)
    {
        CbPerfil.Items.Clear();
        CbPerfil.Items.Add("(escolha um perfil)");
        foreach (var p in _perfis) CbPerfil.Items.Add(p.Nome);
        var i = escolher == null ? -1 : _perfis.FindIndex(p => string.Equals(p.Nome, escolher, StringComparison.OrdinalIgnoreCase));
        CbPerfil.SelectedIndex = i >= 0 ? i + 1 : 0;
        PerfilChanged(this, new RoutedEventArgs());
    }

    private void PerfilChanged(object sender, RoutedEventArgs e)
    {
        if (TxtPerfil == null) return;
        TxtPerfil.Text = PerfilEscolhido is { } p
            ? $"{RegistroConfiguracoes.Nome(string.IsNullOrWhiteSpace(p.Criterios.Fonte) ? TipoPerfil.Usuario : TipoPerfil.ReferenciaCitada)}: {RegistroConfiguracoes.Criterios(p.Criterios)}"
            : _perfis.Count == 0 ? "Nenhum perfil guardado: o plugin não traz perfis de limites (não há valores normativos embutidos)." : "";
    }

    private void AplicarPerfilClick(object sender, RoutedEventArgs e)
    {
        if (PerfilEscolhido is { } p) MostrarCriterios(p.Criterios);
    }

    private void SalvarPerfilClick(object sender, RoutedEventArgs e)
    {
        GeometricCriteria c;
        try { c = LerCriterios(); }
        catch (FormatException ex) { UiHelpers.Error(ex.Message); return; }
        var nome = PerfilEscolhido?.Nome ?? "";
        var w = new FormWindow("Salvar perfil de limites", "Salvar perfil de limites",
                "Guarda os limites e a fonte atuais. Use o mesmo nome para substituir um perfil. Sem fonte, o perfil fica como \"do usuário\".", null, null, false, "Salvar", 520, 240)
            .Text("Nome do perfil", () => nome, v => nome = v?.Trim() ?? "");
        w.Owner = this;
        if (w.ShowDialog() != true || string.IsNullOrWhiteSpace(nome)) return;
        _perfis.RemoveAll(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase));
        _perfis.Insert(0, new PerfilCriterios { Nome = nome, Criterios = c });
        FillPerfis(nome);
        Perfis();
    }

    private void ExcluirPerfilClick(object sender, RoutedEventArgs e)
    {
        if (PerfilEscolhido is not { } p) return;
        _perfis.Remove(p);
        FillPerfis(null);
        Perfis();
    }

    // ---------------------------------------------------------------- janelas

    private void Janelas()
    {
        var n = PluginContext.Settings.LastUsed.Keys.Count(k => k.StartsWith(TamanhosDeJanela.Prefixo, StringComparison.Ordinal));
        TxtJanelas.Text = _limparJanelas ? $"{n} janela(s) voltam ao tamanho padrão ao salvar." : $"{n} janela(s) com o tamanho guardado.";
    }

    private void RestaurarJanelasClick(object sender, RoutedEventArgs e)
    {
        _limparJanelas = true;
        Janelas();
    }

    // ---------------------------------------------------------------- catálogo

    private void UpdateStatus()
    {
        var path = TbCatalog.Text;
        TxtCatalogStatus.Text = File.Exists(path)
            ? "Catálogo do usuário encontrado." + (PluginContext.CatalogError != null ? " " + PluginContext.CatalogError : "")
            : "Nenhum arquivo do usuário – usando apenas o catálogo padrão.";
    }

    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Catálogo JSON|*.json", FileName = TbCatalog.Text };
        if (dlg.ShowDialog(this) == true) { TbCatalog.Text = dlg.FileName; UpdateStatus(); }
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CatalogService.EnsureUserCopy(TbCatalog.Text);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }

    private void FolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(TbCatalog.Text) ?? PluginPaths.Root;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UiHelpers.Error(ex.Message);
        }
    }

    // ---------------------------------------------------------------- validar e salvar

    private void Validacao(IEnumerable<EstadoContexto> linhas)
    {
        PanelValidacao.Children.Clear();
        foreach (var l in linhas) PanelValidacao.Children.Add(ContextPanelPage.Linha(l.Nivel, l.Texto));
    }

    /// <summary>
    /// As configurações dos campos numa cópia das atuais – nada muda no plugin até salvar. Campo não numérico ou fora da faixa
    /// lança <see cref="FormatException"/> com o nome do campo.
    /// </summary>
    private PluginSettings Candidata()
    {
        var c = PluginContext.Settings.Clone();
        c.DefaultMode = CbMode.SelectedIndex == 1 ? OutputMode.Detalhe2D : OutputMode.Modelo3D;
        c.DefaultMaterial = CbMaterial.SelectedItem as string ?? c.DefaultMaterial;
        c.DefaultSpeed = UiHelpers.Parse(TbSpeed, 60, "Velocidade", 10, 200);
        c.FontFamily = string.IsNullOrWhiteSpace(TbFont.Text) ? "Arial" : TbFont.Text.Trim();
        c.FontBold = CkBold.IsChecked == true;
        c.MinModelThickness = UiHelpers.Parse(TbMinThickness, 0.003, "Espessura mínima", 0.001, 0.1);
        c.ElevationOffset = UiHelpers.Parse(TbElevation, 0.001, "Elevação adicional", -10, 10);
        c.DrapeByDefault = CkDrape.IsChecked == true;
        c.DrapePieceLength = UiHelpers.Parse(TbPiece, 2, "Comprimento das peças", 0.5, 50);
        c.AutoUpdate = CkAuto.IsChecked == true;
        c.AutoIntersect = CkAutoIntersect.IsChecked == true;
        c.AutoConnect = CkAutoConnect.IsChecked == true;
        c.AutoCrosswalks = CkAutoCrosswalks.IsChecked == true;
        c.PhysicalAsFloors = CkFloors.IsChecked == true;
        c.RampsAsFloors = CkRampFloors.IsChecked == true;
        c.PaintAsFloors = CkPaintFloors.IsChecked == true;
        c.BoundaryTolerance = UiHelpers.Parse(TbBoundaryTol, 5, "Desvio máximo dos contornos", 0, 50) / 1000;
        c.VisibleBoundary2D = CkBoundary.IsChecked == true;
        c.RealisticAppearance = CkRealistic.IsChecked == true;
        c.CriteriosGeometricos = LerCriterios();
        c.PerfisCriterios = _perfis.Select(p => new PerfilCriterios { Nome = p.Nome, Criterios = p.Criterios.Clone() }).ToList();
        c.UserCatalogPath = string.Equals(TbCatalog.Text, PluginPaths.DefaultUserCatalog, StringComparison.OrdinalIgnoreCase) ? null : TbCatalog.Text;
        if (_limparJanelas) TamanhosDeJanela.Limpar(c.LastUsed);
        return c;
    }

    /// <summary>Sem alterações pendentes, ou o usuário aceita descartá-las para <paramref name="paraQue"/>.</summary>
    private bool Descartar(string paraQue)
    {
        bool pendente;
        try { pendente = RegistroConfiguracoes.Impacto(PluginContext.Settings, Candidata()).Count > 0; }
        catch (FormatException) { pendente = true; }
        return !pendente || MessageBox.Show(this, $"Há alterações não salvas nas configurações do usuário. Descartá-las para {paraQue}?", "Configurações – SinalizaBIM",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var atual = PluginContext.Settings;
        PluginSettings c;
        try { c = Candidata(); }
        catch (FormatException ex)
        {
            // Antes, os campos já lidos ficavam aplicados na sessão; agora nada muda até tudo passar.
            UiHelpers.Error(ex.Message + "\n\nNada foi alterado.");
            return;
        }
        var problemas = RegistroConfiguracoes.Validar(c, Materiais, File.Exists);
        Validacao(problemas);
        if (problemas.Any(p => p.Nivel == NivelSituacao.Erro))
        {
            UiHelpers.Error("Corrija os valores marcados como erro no alto da janela antes de salvar. Nada foi alterado.");
            return;
        }
        var impacto = RegistroConfiguracoes.Impacto(atual, c);
        if (impacto.Count > 0 && MessageBox.Show(this, "Ao salvar:\n\n• " + string.Join("\n• ", impacto) +
                "\n\nNada é alterado agora nos elementos existentes. Salvar?", "Configurações – SinalizaBIM", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        atual.CopiarDe(c);
        PluginContext.SaveSettings();
        PluginContext.ReloadCatalog();
        DialogResult = true;
    }

    // ---------------------------------------------------------------- projeto, perfis e registro

    private void Projeto()
    {
        PanelProjeto.Children.Clear();
        if (_projeto == null)
        {
            PanelProjeto.Children.Add(ContextPanelPage.Linha(NivelSituacao.NaoVerificado,
                "Nenhum projeto aberto: as configurações do projeto ficam gravadas no modelo – abra o projeto e volte aqui."));
            return;
        }
        PanelProjeto.Children.Add(new TextBlock { Text = $"Projeto: {_nomeDoProjeto ?? "—"}", Style = S("SectionTitle") });
        PanelProjeto.Children.Add(new TextBlock
        {
            Style = S("Hint"), TextWrapping = TextWrapping.Wrap,
            Text = "Gravadas no modelo: vão com o arquivo do projeto para quem o abrir. Cada uma é editada no comando dela (escolha a linha e abra o comando); " +
                   "o que vale para todos os seus projetos fica na aba do usuário.",
        });
        var g = UiTables.Grid(Linhas(_projeto), 360, false, ("Grupo", nameof(LinhaRegistro.Grupo), 0.7), ("Configuração", nameof(LinhaRegistro.Configuracao), 1.2),
            ("Onde fica", nameof(LinhaRegistro.Onde), 0.9), ("Valor", nameof(LinhaRegistro.Valor), 2.2), ("O que afeta", nameof(LinhaRegistro.Afeta), 1.8));
        PanelProjeto.Children.Add(g);
        var abrir = new Button { Content = "Abrir o comando da linha escolhida", Style = S("Secondary"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        abrir.Click += (_, _) =>
        {
            if (g.SelectedItem is not LinhaRegistro { Comando: { } cmd } l) return;
            if (!Descartar($"abrir \"{l.Configuracao}\"")) return;
            Comando = cmd;
            DialogResult = false;
        };
        PanelProjeto.Children.Add(abrir);
    }

    private static List<LinhaRegistro> Linhas(IEnumerable<ItemConfiguracao> itens) =>
        itens.OrderBy(i => RegistroConfiguracoes.Grupos.ToList().IndexOf(i.Grupo)).ThenBy(i => i.Onde)
            .Select(i => new LinhaRegistro(i.Grupo, i.Nome, RegistroConfiguracoes.Nome(i.Onde), i.Valor, i.Afeta, i.Comando)).ToList();

    private void Perfis()
    {
        PanelPerfis.Children.Clear();
        PanelPerfis.Children.Add(new TextBlock
        {
            Style = S("Hint"), TextWrapping = TextWrapping.Wrap,
            Text = "Os perfis do plugin são exemplos para começar – nenhum representa norma. \"Referência citada\" é a fonte que o usuário informou; o plugin não a confere. " +
                   "Seções-tipo: na janela da via; estruturas de pavimento: na Estrutura do Pavimento; limites geométricos: na aba do usuário. Parcelamento: as categorias " +
                   "e os mínimos ficam em cada loteamento (não há perfis de parcelamento).",
        });
        var t = PluginContext.Settings.Clone();
        t.PerfisCriterios = _perfis;
        var linhas = RegistroConfiguracoes.Perfis(t, RoadTemplates.All.Select(x => x.Name))
            .Select(p => new LinhaPerfil(p.Categoria, p.Nome, RegistroConfiguracoes.Nome(p.Tipo), p.Fonte ?? "—", p.Resumo)).ToList();
        PanelPerfis.Children.Add(UiTables.Grid(linhas, 420, false, ("Categoria", nameof(LinhaPerfil.Categoria), 0.9), ("Perfil", nameof(LinhaPerfil.Nome), 1.6),
            ("Tipo", nameof(LinhaPerfil.Tipo), 1.2), ("Fonte", nameof(LinhaPerfil.Fonte), 1.0), ("Resumo", nameof(LinhaPerfil.Resumo), 1.8)));
    }

    private void Registro()
    {
        PanelRegistro.Children.Clear();
        PanelRegistro.Children.Add(new TextBlock
        {
            Style = S("Hint"), TextWrapping = TextWrapping.Wrap,
            Text = $"Do usuário: {PluginPaths.Settings} (não vai com o projeto). Do projeto: gravadas no modelo. De cada elemento: na própria marca. " +
                   "Preferências pessoais (janelas, últimas escolhas) não entram no modelo.",
        });
        var itens = RegistroConfiguracoes.DoUsuario(PluginContext.Settings).Concat(_projeto ?? Array.Empty<ItemConfiguracao>());
        PanelRegistro.Children.Add(UiTables.Grid(Linhas(itens), 460, false, ("Grupo", nameof(LinhaRegistro.Grupo), 0.7), ("Configuração", nameof(LinhaRegistro.Configuracao), 1.3),
            ("Onde fica", nameof(LinhaRegistro.Onde), 0.9), ("Valor", nameof(LinhaRegistro.Valor), 1.8), ("O que afeta", nameof(LinhaRegistro.Afeta), 2.0)));
    }
}
