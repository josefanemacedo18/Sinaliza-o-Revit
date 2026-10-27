using System.Diagnostics;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Reports;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Formulários da rede de drenagem: o trecho (criação e edição) e o Projeto de Drenagem.</summary>
internal static class DrainageNetworkForms
{
    private const string Hint = "Tubo de concreto entre as paredes das caixas, no fundo de cada uma (ou nas cotas informadas). " +
                                "O diâmetro pode ser o calculado: Relatório da Rede › Aplicar os diâmetros calculados (com a equação de chuva do Projeto de Drenagem).";

    /// <summary>
    /// Janela do trecho. Com <paramref name="ends"/> (edição), mostra as cotas de fundo: automáticas (o fundo das caixas) ou
    /// informadas.
    /// </summary>
    public static FormWindow Pipe(DrainagePipeDefinition p, bool edit, PontasTrecho? ends)
    {
        var w = new FormWindow(edit ? "Editar trecho de rede" : "Trecho de rede", "Trecho de rede de drenagem", Hint, null, null, false,
            edit ? "Aplicar" : "Escolher os dispositivos", 640, edit ? 460 : 340);
        w.Section("Tubo")
         .Number("Diâmetro interno (mm)", () => p.Diameter * 1000, v => p.Diameter = v / 1000, 100, 3000, "0",
             "Diâmetro comercial do trecho. A parede é desenhada com 8 % do diâmetro, mínimo 4 cm.");
        if (ends == null) return w;
        var manual = p.InvertFrom != null || p.InvertTo != null;
        w.Section("Cotas de fundo", $"Montante {ends.CodeA}, jusante {ends.CodeB}. Automático = o fundo de cada caixa.")
         .Check("Informar as cotas de fundo", () => manual, v => manual = v)
         .If(() => manual, x => x
             .Number("Fundo no montante (m)", () => p.InvertFrom ?? ends.InvertA, v => p.InvertFrom = manual ? v : null, -10000, 10000, "0.000")
             .Number("Fundo no jusante (m)", () => p.InvertTo ?? ends.InvertB, v => p.InvertTo = manual ? v : null, -10000, 10000, "0.000"));
        return w;
    }

    /// <summary>Janela de edição do trecho (null se a marca não for um trecho).</summary>
    public static (FormWindow Window, MarkingDefinition Working)? ForEdit(UIDocument uidoc, MarkingDefinition def)
    {
        if (def is not DrainagePipeDefinition) return null;
        var working = (DrainagePipeDefinition)MarkingDefinition.FromJson(def.ToJson())!;
        var all = MarkingStorage.Definitions(uidoc.Document);
        var ends = DrainageNetwork.Ends(working, id => all.FirstOrDefault(d => d.Id == id));
        return (Pipe(working, true, ends), working);
    }

    private static double? Opt(double v) => v > 0 ? v : null;

    /// <summary>
    /// Janela do Projeto de Drenagem. A equação de chuva é editada numa cópia (<paramref name="chuva"/>) e só vale com
    /// "Calcular as vazões" marcado – quem chama grava <c>p.Chuva = usar ? chuva : null</c>.
    /// </summary>
    public static FormWindow Projeto(ProjetoDrenagem p, Func<EquacaoChuva> chuva, Action<EquacaoChuva> setChuva, Func<bool> usar, Action<bool> setUsar)
    {
        var w = new FormWindow("Projeto de Drenagem", "Projeto de drenagem pluvial",
            "Parâmetros do cálculo da rede (método racional) e do memorial, gravados neste projeto. Sem a equação de chuva, só a geometria da rede é " +
            "conferida. As predefinições trazem todos os critérios de cada fonte; confira e ajuste ao órgão e ao local do projeto. Zero = não conferir.",
            null, null, false, "Gravar no projeto", 760, 820);
        var presets = new[] { ("Manual de Drenagem Urbana – SUDERHSA (2002), curva de Fendrich – Curitiba", 0),
                              ("Memorial modelo – Miguelópolis/SP (2019), curva de Barretos (DAEE-SP)", 1) };
        var preset = 0;
        var eqs = EquacaoChuva.Predefinidas();
        var eqEscolhida = 0;
        w.Section("Predefinição")
         .Choice("Critérios da fonte", presets, () => preset, v => preset = v)
         .Button("Aplicar a predefinição (substitui os critérios e a equação; mantém a identificação)", () =>
         {
             p.AplicarCriterios(preset == 0 ? ProjetoDrenagem.ManualSuderhsa() : ProjetoDrenagem.MemorialModelo());
             setChuva(p.Chuva!.Clone());
             setUsar(true);
         })
         .Text("Fonte dos critérios", () => p.Fonte, v => p.Fonte = string.IsNullOrWhiteSpace(v) ? null : v!.Trim());

        w.Section("Chuva de projeto")
         .Check("Calcular as vazões com a equação abaixo", usar, setUsar)
         .Choice("Equação predefinida", eqs.Select((e, k) => (e.Nome, k)), () => eqEscolhida, v => eqEscolhida = v)
         .Button("Usar a equação predefinida", () => { setChuva(eqs[eqEscolhida].Clone()); setUsar(true); })
         .Text("Nome", () => chuva().Nome, v => chuva().Nome = v ?? "")
         .Text("Fonte da equação", () => chuva().Fonte, v => chuva().Fonte = string.IsNullOrWhiteSpace(v) ? null : v!.Trim())
         .Choice("Forma", new[] { ("i = K·T^a / (t + b)^c  (mm/h)", FormaIdf.Potencia), ("i = (t + b)^−c · (A + B·ln(T − d))  (mm/min)", FormaIdf.Logaritmica) },
             () => chuva().Forma, v => chuva().Forma = v)
         .Number("K ou A", () => chuva().Coeficiente, v => chuva().Coeficiente = v, -1e6, 1e6, "0.####")
         .Number("a ou B", () => chuva().CoeficienteRetorno, v => chuva().CoeficienteRetorno = v, -1e3, 1e3, "0.####")
         .Number("b (min)", () => chuva().DuracaoSomada, v => chuva().DuracaoSomada = v, 0, 1000, "0.####")
         .Number("c", () => chuva().ExpoenteDuracao, v => chuva().ExpoenteDuracao = v, 0, 10, "0.####")
         .If(() => chuva().Forma == FormaIdf.Logaritmica, x => x.Number("d (anos)", () => chuva().RetornoSubtraido, v => chuva().RetornoSubtraido = v, 0, 1000, "0.####"))
         .Number("Tempo de retorno T (anos)", () => p.TempoRetorno, v => p.TempoRetorno = v, 1, 1000, "0.#",
             "Manual SUDERHSA, tabela 3.1: microdrenagem residencial e comercial 2 anos (2 a 5); avenidas 2 a 10. O memorial modelo adota 10 anos.")
         .Number("Tempo de concentração inicial (min)", () => p.TempoInicial, v => p.TempoInicial = v, 1, 600, "0.#",
             "Nas cabeceiras, quando a área não tem tempo próprio nem talvegue. Manual SUDERHSA: usualmente 10 min.")
         .Number("Duração mínima da chuva (min)", () => p.DuracaoMinima, v => p.DuracaoMinima = v, 0, 600, "0.#");

        w.Section("Coeficiente de escoamento")
         .Choice("Método", new[] { ("C de cada área, ponderado pelas áreas (Manual SUDERHSA, tabela 4.1)", MetodoCoeficiente.Ponderado),
                                   ("Fórmula de Horner: C = 0,364·log t + 0,0042·p − 0,145 (memorial modelo)", MetodoCoeficiente.Horner) },
             () => p.Coeficiente, v => p.Coeficiente = v)
         .Number("C das áreas sem valor próprio", () => p.CoeficientePadrao, v => p.CoeficientePadrao = v, 0.01, 1, "0.00")
         .Number("Área impermeável p das áreas sem valor próprio (%)", () => p.ImpermeabilizacaoPadrao, v => p.ImpermeabilizacaoPadrao = v, 0, 100, "0.#");

        w.Section("Galerias e ligações")
         .Number("Rugosidade de Manning n", () => p.Rugosidade, v => p.Rugosidade = v, 0.008, 0.05, "0.0000")
         .Text("Diâmetros comerciais (mm, separados por ;)", () => string.Join("; ", p.Diametros.OrderBy(d => d).Select(d => (d * 1000).ToString("0"))), v =>
         {
             var lista = (v ?? "").Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                 .Select(s => double.TryParse(s, System.Globalization.NumberStyles.Float, UiHelpers.PtBr, out var mm) ? mm / 1000 : 0)
                 .Where(d => d >= 0.1 && d <= 5).Distinct().OrderBy(d => d).ToList();
             if (lista.Count > 0) p.Diametros = lista;
         })
         .Number("Diâmetro mínimo das galerias (mm)", () => (p.DiametroMinimo ?? 0) * 1000, v => p.DiametroMinimo = Opt(v / 1000), 0, 5000, "0")
         .Number("Diâmetro das ligações das bocas de lobo (mm)", () => (p.DiametroLigacao ?? 0) * 1000, v => p.DiametroLigacao = Opt(v / 1000), 0, 5000, "0")
         .Number("Velocidade mínima a seção plena (m/s)", () => p.VelocidadeMinima ?? 0, v => p.VelocidadeMinima = Opt(v), 0, 20, "0.00")
         .Number("Velocidade máxima a seção plena (m/s)", () => p.VelocidadeMaxima ?? 0, v => p.VelocidadeMaxima = Opt(v), 0, 20, "0.00")
         .Number("Recobrimento mínimo (m)", () => p.RecobrimentoMinimo ?? 0, v => p.RecobrimentoMinimo = Opt(v), 0, 10, "0.00",
             "Sobre a geratriz superior externa do tubo, medido sob a superfície acabada: as vias do plugin e, fora delas, o terreno (Toposolid).")
         .Number("Declividade mínima das galerias (%)", () => p.DeclividadeMinima ?? 0, v => p.DeclividadeMinima = Opt(v), 0, 100, "0.###")
         .Number("Declividade máxima das galerias (%)", () => p.DeclividadeMaxima ?? 0, v => p.DeclividadeMaxima = Opt(v), 0, 100, "0.###")
         .Number("Declividade mínima das ligações (%)", () => p.DeclividadeMinimaLigacao ?? 0, v => p.DeclividadeMinimaLigacao = Opt(v), 0, 100, "0.###")
         .Check("Espaçamento máximo entre PVs pela tabela 6.1 (120/150/180 m)", () => p.EspacamentoPv, v => p.EspacamentoPv = v)
         .Number("Queda que caracteriza PV de queda (m)", () => p.QuedaPv ?? 0, v => p.QuedaPv = Opt(v), 0, 20, "0.00");

        w.Section("Sarjetas e bocas de lobo")
         .Number("Lâmina máxima junto à guia (cm)", () => p.LaminaMaxima * 100, v => p.LaminaMaxima = v / 100, 1, 50, "0.#")
         .Number("Declividade transversal da via (%)", () => p.DeclividadeTransversal * 100, v => p.DeclividadeTransversal = v / 100, 0.5, 20, "0.##")
         .Number("Rugosidade de Manning da via", () => p.RugosidadeSarjeta, v => p.RugosidadeSarjeta = v, 0.008, 0.05, "0.000")
         .Check("Fatores de redução das sarjetas e das bocas de lobo (DAEE/CETESB, 1980)", () => p.FatoresReducao, v => p.FatoresReducao = v)
         .Number("Excesso admitido sobre o engolimento (%)", () => p.ExcessoBocaDeLobo * 100, v => p.ExcessoBocaDeLobo = v / 100, 0, 100, "0")
         .Number("Velocidade mínima na sarjeta (m/s)", () => p.VelocidadeSarjetaMinima ?? 0, v => p.VelocidadeSarjetaMinima = Opt(v), 0, 20, "0.00")
         .Number("Velocidade máxima na sarjeta (m/s)", () => p.VelocidadeSarjetaMaxima ?? 0, v => p.VelocidadeSarjetaMaxima = Opt(v), 0, 20, "0.00");

        w.Section("Valas (quantitativo do memorial)")
         .Number("Escorar valas a partir de (m)", () => p.ProfundidadeEscoramento, v => p.ProfundidadeEscoramento = v, 0, 20, "0.00")
         .Number("Largura mínima da vala (× diâmetro)", () => p.LarguraValaDiametros, v => p.LarguraValaDiametros = v, 1, 5, "0.#");

        static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v!.Trim();
        w.Section("Identificação (memorial)")
         .Text("Obra / empreendimento", () => p.Obra, v => p.Obra = T(v))
         .Text("Município/UF", () => p.Municipio, v => p.Municipio = T(v))
         .Text("Localização do imóvel", () => p.Endereco, v => p.Endereco = T(v))
         .Text("Proprietário", () => p.Proprietario, v => p.Proprietario = T(v))
         .Text("CNPJ / CPF do proprietário", () => p.ProprietarioDocumento, v => p.ProprietarioDocumento = T(v))
         .Text("Autor do projeto", () => p.Autor, v => p.Autor = T(v))
         .Text("Registro profissional (CREA/CAU)", () => p.Registro, v => p.Registro = T(v))
         .Text("ART / RRT", () => p.Art, v => p.Art = T(v));
        return w;
    }
}

/// <summary>
/// Trecho de rede (F8): tubo entre dois dispositivos com caixa. Clique no dispositivo de montante e no de jusante; o de
/// jusante vira o montante do trecho seguinte (Esc termina). Cada trecho é gravado ao ser escolhido (Ctrl+Z desfaz um a um).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdTrechoDrenagem : CommandBase
{
    private const string Key = "trecho-drenagem";

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var template = UiHelpers.Remembered<DrainagePipeDefinition>(Key) ?? new DrainagePipeDefinition();
        if (UiHelpers.ShowModal(DrainageNetworkForms.Pipe(template, false, null)) != true) return Result.Cancelled;
        UiHelpers.Remember(Key, template);
        PluginContext.SaveSettings();

        var from = Pick(uidoc, "Trecho de rede: clique no dispositivo de MONTANTE (PV, boca de lobo ou grelha com caixa) – Esc cancela");
        if (from == null) return Result.Cancelled;
        var results = new List<RenderResult>();
        var n = 0;
        while (Pick(uidoc, $"Trecho de rede: clique no dispositivo de JUSANTE de {from.DisplayCode} – Esc termina") is { } to)
        {
            if (to.Id == from.Id)
            {
                Notify.Show("O trecho precisa de dois dispositivos diferentes: clique no de jusante.");
                continue;
            }
            var p = (DrainagePipeDefinition)template.CloneWithNewId();
            p.FromId = from.Id;
            p.ToId = to.Id;
            p.InvertFrom = null;
            p.InvertTo = null;
            p.Z = from.Z;
            p.Output = InfraRunner.Output3D();
            results.AddRange(MarkingCreator.Commit(uidoc, new[] { p }, "SV - Trecho de drenagem"));
            n++;
            from = to;
        }
        if (n == 0) return Result.Cancelled;
        CommandBaseReport.Show("Trecho de drenagem", results);
        return Result.Succeeded;
    }

    private static DrainageDefinition? Pick(UIDocument uidoc, string prompt)
    {
        try
        {
            var r = uidoc.Selection.PickObject(ObjectType.Element, new DrainageDeviceFilter(), prompt);
            return MarkingStorage.Read(uidoc.Document.GetElement(r))?.Definition as DrainageDefinition;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>Projeto de Drenagem: equação de chuva, critérios e identificação do memorial, gravados no projeto do Revit.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdProjetoDrenagem : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => Edit(uidoc) ? Result.Succeeded : Result.Cancelled;

    /// <summary>Abre a janela e grava; falso se cancelada.</summary>
    public static bool Edit(UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var p = DrainageDesignStore.Load(doc);
        var chuva = p.Chuva?.Clone() ?? EquacaoChuva.Fendrich();
        var usar = p.Chuva != null;
        var w = DrainageNetworkForms.Projeto(p, () => chuva, v => chuva = v, () => usar, v => usar = v);
        if (UiHelpers.ShowModal(w) != true) return false;
        p.Chuva = usar ? chuva : null;
        DrainageDesignStore.Save(doc, p);
        return true;
    }
}

/// <summary>
/// Relatório da Rede (F8): trechos medidos, problemas e – com a equação de chuva do Projeto de Drenagem – a planilha de
/// dimensionamento das galerias e a verificação das captações, em CSV. Pode aplicar os diâmetros calculados aos trechos.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdRedeDrenagem : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var defs = MarkingStorage.Definitions(doc);
        if (!defs.Any(d => d is DrainagePipeDefinition))
        {
            TaskDialog.Show(AppTitle, "Nenhum trecho de rede neste projeto. Ligue os dispositivos com Trecho de Rede (guia SinalizaBIM › Drenagem).");
            return Result.Cancelled;
        }
        var p = DrainageDesignStore.Load(doc);
        // Recobrimento sob a superfície acabada (vias do plugin e terreno projetado) – Etapa 6; antes, a reta entre as tampas.
        var superficie = DrenagemDoModelo.SuperficieAcabada(defs, TerrainModel.Current(doc));
        var trechos = DrainageNetwork.Analyze(defs, superficie);
        var problemas = DrainageNetwork.Check(defs, p, superficie);
        var hid = DrainageHydraulics.Calcular(defs, p);
        problemas.AddRange(hid.Problemas);
        // Drenagem × vias e terreno: captações lidas do modelo, pontos baixos sem captação e talvegues sob aterro.
        var captacoes = DrenagemDoModelo.Captacoes(defs);
        problemas.AddRange(DrenagemDoModelo.Divergencias(captacoes));
        foreach (var pb in DrenagemDoModelo.PontosBaixosSemCaptacao(defs))
            problemas.Add(new ProblemaRede(pb.Via, "ponto-baixo", $"Ponto baixo da sarjeta sem captação: {pb.Via}, lado {DrenagemDoModelo.Lado(pb.Lado)}, estaca {pb.EstacaTexto}.",
                false, new[] { pb.ViaId }));
        if (TerrainModel.Ground(doc) is { } natural)
            foreach (var tv in DrenagemDoModelo.TalveguesSemTravessia(defs, natural))
                problemas.Add(new ProblemaRede(tv.Via, "talvegue", $"Talvegue sob o aterro da {tv.Via}, estaca {tv.EstacaTexto} (aterro {tv.Aterro.ToString("0.0", UiHelpers.PtBr)} m): sem travessia da rede – a via represa a água.",
                    false, new[] { tv.ViaId }));
        string? path = null;
        try
        {
            Directory.CreateDirectory(PluginPaths.Root);
            path = Path.Combine(PluginPaths.Root, $"rede-drenagem-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var csv = DrainageNetwork.Csv(trechos, problemas, p) + (p.TemChuva ? "\n" + DrainageHydraulics.Csv(hid, p) : "") +
                      "\n" + PerfilRede.Csv(PerfilRede.Linhas(defs, superficie), PerfilRede.Dispositivos(defs)) +
                      (captacoes.Count > 0 ? "\n" + DrenagemDoModelo.Csv(captacoes) : "");
            File.WriteAllText(path, csv, new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            Log.Error("Relatório da Rede", ex);
            path = null;
        }

        var extensao = trechos.Sum(t => t.Comprimento);
        var mudar = hid.Trechos.Where(t => t.DiametroNecessario is { } dn && Math.Abs(dn - t.Diametro) > 1e-6).ToList();
        var resumo = p.TemChuva && hid.Trechos.Count > 0
            ? $"Vazão máxima {hid.Trechos.Max(t => t.Vazao).ToString("N1", UiHelpers.PtBr)} L/s; {mudar.Count} trecho(s) com diâmetro diferente do calculado.\n"
            : "";
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = $"Rede de drenagem: {trechos.Count} trecho(s), {extensao.ToString("N1", UiHelpers.PtBr)} m" +
                              (problemas.Count == 0 ? " – nenhum problema." : $" – {problemas.Count} problema(s)."),
            MainContent = resumo + string.Join("\n", problemas.Take(12).Select(x => "• " + x.Texto)) +
                          (problemas.Count > 12 ? $"\n… e mais {problemas.Count - 12} no relatório." : "") +
                          (p.TemChuva ? $"\n\nCritérios: {p.FonteTexto}." :
                              "\n\nSem equação de chuva no Projeto de Drenagem: só a geometria foi conferida (vazões não calculadas).") +
                          "\nRecobrimento medido sob a superfície acabada (vias do plugin e terreno). O relatório traz o perfil longitudinal da rede e as captações × modelo.",
            FooterText = path != null ? $"Relatório: {path}" : "O relatório não pôde ser gravado (ver log).",
        };
        var ids = problemas.SelectMany(x => x.Ids ?? Array.Empty<string>()).ToHashSet();
        var elems = ids.Count == 0 ? new List<ElementId>()
            : MarkingStorage.All(doc).Where(s => ids.Contains(s.MarkingId)).Select(s => s.Element.Id).Distinct().ToList();
        if (elems.Count > 0) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Selecionar os {elems.Count} elemento(s) com problema");
        if (mudar.Count > 0)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Aplicar os diâmetros calculados ({mudar.Count} trecho(s))",
                "Menor diâmetro comercial da lista do Projeto de Drenagem que escoa a vazão a seção plena. Os tubos são redesenhados.");
        if (path != null) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Abrir o relatório");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Projeto de Drenagem (chuva e critérios)");
        td.CommonButtons = TaskDialogCommonButtons.Close;
        switch (td.Show())
        {
            case TaskDialogResult.CommandLink1:
                uidoc.Selection.SetElementIds(elems);
                break;
            case TaskDialogResult.CommandLink2:
                return ApplyDiameters(uidoc, defs, p);
            case TaskDialogResult.CommandLink3 when path != null:
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Error("Relatório da Rede – abrir", ex); }
                break;
            case TaskDialogResult.CommandLink4:
                CmdProjetoDrenagem.Edit(uidoc);
                break;
        }
        return Result.Succeeded;
    }

    /// <summary>Dimensiona a rede (diâmetros calculados de montante para jusante) e redesenha os trechos que mudam.</summary>
    private static Result ApplyDiameters(UIDocument uidoc, List<MarkingDefinition> defs, ProjetoDrenagem p)
    {
        var dim = DrainageHydraulics.Calcular(defs, p, dimensionar: true);
        var pipes = defs.OfType<DrainagePipeDefinition>().ToDictionary(x => x.Id);
        var changed = new List<MarkingDefinition>();
        foreach (var t in dim.Trechos)
            if (t.DiametroNecessario is { } dn && pipes.TryGetValue(t.Id, out var pipe) && Math.Abs(pipe.Diameter - dn) > 1e-6)
            {
                pipe.Diameter = dn;
                changed.Add(pipe);
            }
        if (changed.Count == 0) return Result.Cancelled;
        var r = MarkingCreator.Commit(uidoc, changed, "SV - Diâmetros calculados da rede");
        CommandBaseReport.Show("Diâmetros calculados", r);
        TaskDialog.Show(AppTitle, $"{changed.Count} trecho(s) redesenhado(s) com o diâmetro calculado. Rode o Relatório da Rede de novo para conferir " +
                                  "recobrimento, velocidades e o alinhamento pela geratriz superior nos PVs.");
        return Result.Succeeded;
    }
}

/// <summary>
/// Memorial de Drenagem (.docx): identificação, parâmetros de projeto, planilha de dimensionamento das galerias, descritivo
/// dos serviços, quantitativo e disposições finais – na estrutura do memorial modelo, com os números do modelo e do Projeto
/// de Drenagem.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdMemorialDrenagem : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var defs = MarkingStorage.Definitions(doc);
        if (!defs.Any(d => d is DrainagePipeDefinition))
        {
            TaskDialog.Show(AppTitle, "Nenhum trecho de rede neste projeto: ligue os dispositivos com Trecho de Rede antes de gerar o memorial.");
            return Result.Cancelled;
        }
        var p = DrainageDesignStore.Load(doc);
        if (!p.TemChuva)
        {
            var td0 = new TaskDialog(AppTitle)
            {
                MainInstruction = "O Projeto de Drenagem não tem equação de chuva.",
                MainContent = "Sem a equação de chuva as vazões não são calculadas e o memorial não teria a planilha das galerias.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td0.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir o Projeto de Drenagem");
            if (td0.Show() != TaskDialogResult.CommandLink1 || !CmdProjetoDrenagem.Edit(uidoc)) return Result.Cancelled;
            p = DrainageDesignStore.Load(doc);
            if (!p.TemChuva) return Result.Cancelled;
        }
        var title = string.IsNullOrWhiteSpace(p.Obra) ? (string.IsNullOrWhiteSpace(doc.Title) ? "Projeto" : doc.Title) : p.Obra!;
        var dlg = new SaveFileDialog
        {
            Filter = "Documento do Word|*.docx",
            FileName = $"Memorial de drenagem - {title}.docx".Replace(":", ""),
            InitialDirectory = !string.IsNullOrEmpty(doc.PathName) ? Path.GetDirectoryName(doc.PathName) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() != true) return Result.Cancelled;
        try
        {
            File.WriteAllBytes(dlg.FileName, DocxWriter.Write(DrainageMemorial.Build(defs, p, DateTime.Now,
                DrenagemDoModelo.SuperficieAcabada(defs, TerrainModel.Current(doc)))));
        }
        catch (Exception ex)
        {
            throw new UserMessageException("Não foi possível gravar o memorial (o arquivo está aberto no Word?): " + ex.Message);
        }
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = "Memorial de drenagem gerado.",
            MainContent = dlg.FileName + "\n\nConfira no Word a identificação, os critérios e o descritivo dos serviços antes de assinar.",
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir no Word");
        if (td.Show() == TaskDialogResult.CommandLink1)
            try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Abrir memorial de drenagem", ex); }
        return Result.Succeeded;
    }
}

/// <summary>
/// Ler do Modelo (Etapa 6; RT-1.5): para cada boca de lobo e grelha, a sarjeta lida da via (declividade pelo greide, sentido,
/// ponto baixo) e a área que escoa até ela pela geometria da via. Mostra o que leu e grava nos dispositivos – só os campos
/// vazios ou todos, conforme a escolha. Nada é gravado sem a escolha.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCaptacoesDoModelo : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var defs = MarkingStorage.Definitions(doc);
        var captacoes = DrenagemDoModelo.Captacoes(defs);
        if (captacoes.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Nenhuma boca de lobo ou grelha com caixa neste projeto.");
            return Result.Cancelled;
        }
        var lidas = captacoes.Where(c => c.Sarjeta != null).ToList();
        var fora = captacoes.Count - lidas.Count;
        var semGreide = lidas.Count(c => c.Sarjeta!.Declividade == null);
        var pt = UiHelpers.PtBr;
        string Linha(CaptacaoLida c)
        {
            var s = c.Sarjeta!;
            return $"• {c.Rotulo} – {s.Via}, lado {DrenagemDoModelo.Lado(s.Lado)}, estaca {s.EstacaTexto}: " +
                   (s.Declividade is { } d ? $"sarjeta {d.ToString("0.00", pt)} %" : "via sem greide") +
                   (s.PontoBaixo ? ", ponto baixo" : "") +
                   (c.Area is { Area: > 0 } a ? $", área {a.Area.ToString("N1", pt)} m²" : "");
        }
        var vazios = DrenagemDoModelo.Aplicar(captacoes.Select(Copia).ToList());
        var todos = DrenagemDoModelo.Aplicar(captacoes.Select(Copia).ToList(), sobrescrever: true);
        var desatualizadas = DrenagemDoModelo.AtualizarDesatualizadas(captacoes.Select(Copia).ToList());
        // Estado do que já foi lido antes (Etapa 8): em dia, desatualizado ou editado à mão (preservado).
        var estados = captacoes.GroupBy(DrenagemDoModelo.Estado).ToDictionary(g => g.Key, g => g.Count());
        int E(EstadoLeitura e) => estados.GetValueOrDefault(e);
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = $"Captações lidas do modelo: {lidas.Count} de {captacoes.Count}",
            MainContent = string.Join("\n", lidas.Take(12).Select(Linha)) + (lidas.Count > 12 ? $"\n… e mais {lidas.Count - 12}." : "") +
                          (fora > 0 ? $"\n\n{fora} captação(ões) fora da sarjeta de uma via do plugin (a mais de {DrenagemDoModelo.AlcanceMeioFio.ToString("0.0", pt)} m da face do meio-fio): nada lido." : "") +
                          (semGreide > 0 ? $"\n{semGreide} em via sem greide: sem declividade nem sentido do escoamento." : "") +
                          "\n\nA área é a da via (pista do eixo ao meio-fio e calçada, entre o divisor e a captação): lotes e áreas externas não entram – some-as à mão." +
                          $"\nLeituras anteriores: {E(EstadoLeitura.EmDia)} em dia, {E(EstadoLeitura.Desatualizada)} desatualizada(s), {E(EstadoLeitura.Editada)} editada(s) à mão (ficam), " +
                          $"{E(EstadoLeitura.NuncaLida)} nunca lida(s).",
        };
        if (vazios.Count > 0)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Preencher só os campos vazios ({vazios.Count} captação(ões))",
                "Área de contribuição igual a 0, declividade da sarjeta vazia e ponto baixo desmarcado onde o greide tem um. O que você informou fica.");
        if (todos.Count > 0)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Substituir pelo modelo ({todos.Count} captação(ões))",
                "Troca a área e a declividade informadas pelas lidas da via. Desfazer (Ctrl+Z) volta ao que estava.");
        if (desatualizadas.Count > 0)
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, $"Atualizar só as desatualizadas ({desatualizadas.Count} captação(ões))",
                "Relê só as captações cujo valor lido antes não confere mais com o modelo e troca só os campos que ainda eram os lidos; as editadas à mão ficam.");
        td.CommonButtons = TaskDialogCommonButtons.Close;
        var choice = td.Show();
        if (choice is not (TaskDialogResult.CommandLink1 or TaskDialogResult.CommandLink2 or TaskDialogResult.CommandLink3)) return Result.Cancelled;
        // As definições lidas agora são as gravadas: alteradas no lugar e regravadas.
        var mudou = choice == TaskDialogResult.CommandLink3
            ? DrenagemDoModelo.AtualizarDesatualizadas(captacoes)
            : DrenagemDoModelo.Aplicar(captacoes, sobrescrever: choice == TaskDialogResult.CommandLink2);
        var res = MarkingCreator.Commit(uidoc, mudou.Select(m => (MarkingDefinition)m.Dispositivo).ToList(), "SV - Captações lidas do modelo");
        var resumo = new RenderResult();
        resumo.Warnings.Add($"{mudou.Count} captação(ões) atualizada(s):\n" + string.Join("\n", mudou.Take(15).Select(m => m.Texto)) +
                            (mudou.Count > 15 ? $"\n… e mais {mudou.Count - 15}." : ""));
        res.Insert(0, resumo);
        Report("Ler do Modelo", res.Where(r => r.Warnings.Count > 0).ToList(), alwaysShow: true);
        return Result.Succeeded;

        // Cópia para contar o que mudaria, sem tocar nas definições.
        static CaptacaoLida Copia(CaptacaoLida c) => c with { Dispositivo = (DrainageDefinition)MarkingDefinition.FromJson(c.Dispositivo.ToJson())! };
    }
}
