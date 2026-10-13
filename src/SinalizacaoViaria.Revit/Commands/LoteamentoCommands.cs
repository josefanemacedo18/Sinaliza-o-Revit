using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Parcelamento;
using SinalizacaoViaria.Core.Reports;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Seleção de lotes (subdivisões do Toposolid ou pisos gerados pelo Loteamento).</summary>
internal sealed class LotSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => LotService.KeyOf(elem) != null && MarkingStorage.Read(elem)?.Definition is QuadraDefinition;
    public bool AllowReference(Reference reference, XYZ position) => false;
}

/// <summary>Seleção do limite da gleba: linhas, linha de propriedade ou Toposolid.</summary>
internal sealed class GlebaSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => elem is CurveElement || elem is Toposolid { HostTopoId.Value: -1 }
                                              || elem.Category?.BuiltInCategory is BuiltInCategory.OST_SiteProperty or BuiltInCategory.OST_SitePropertyLineSegment;
    public bool AllowReference(Reference reference, XYZ position) => false;
}

/// <summary>Seleção de vias para nomear: eixos (linhas), pisos e elementos das vias do plugin.</summary>
internal sealed class RoadNameSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => elem is CurveElement || elem is Floor || MarkingStorage.Read(elem) != null && LotService.KeyOf(elem) == null;
    public bool AllowReference(Reference reference, XYZ position) => false;
}

internal static class LotCommands
{
    public static LoteamentoDefinition RequireLot(Document doc) =>
        LotService.Loteamentos(doc).FirstOrDefault()
        ?? throw new UserMessageException("Nenhum loteamento neste projeto: use primeiro Loteamento (gleba → quadras → lotes).");

    /// <summary>Lote clicado (ou pré-selecionado): elemento, quadra e chave do lote.</summary>
    public static (Element Element, QuadraDefinition Quadra, string Key)? PickLot(UIDocument uidoc, string prompt)
    {
        var doc = uidoc.Document;
        var pre = uidoc.Selection.GetElementIds().Select(doc.GetElement).FirstOrDefault(e => e != null && new LotSelectionFilter().AllowElement(e));
        Element? e = pre;
        if (e == null)
        {
            try
            {
                var r = uidoc.Selection.PickObject(ObjectType.Element, new LotSelectionFilter(), prompt);
                e = doc.GetElement(r);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }
        if (e == null || MarkingStorage.Read(e)?.Definition is not QuadraDefinition q || LotService.KeyOf(e) is not { } key) return null;
        return (e, q, key);
    }

    /// <summary>Gera o loteamento e as quadras (e refaz detalhes e quadros ligados a ele) – uma operação desfazível.</summary>
    public static List<RenderResult> Commit(UIDocument uidoc, LoteamentoDefinition? lot, IEnumerable<QuadraDefinition> quadras, string name)
    {
        var defs = new List<MarkingDefinition>();
        if (lot != null) defs.Add(lot);
        defs.AddRange(quadras);
        return MarkingCreator.Commit(uidoc, defs, name);
    }

    public static void Report(string title, List<RenderResult> results, IEnumerable<string>? extra = null)
    {
        var warn = results.SelectMany(r => r.Warnings).Concat(extra ?? Array.Empty<string>()).Distinct().ToList();
        var td = new TaskDialog(CommandBase.AppTitle)
        {
            MainInstruction = title,
            MainContent = warn.Count == 0 ? "Pronto." : string.Join("\n", warn.Take(18).Select(w => "• " + w)) + (warn.Count > 18 ? $"\n… e mais {warn.Count - 18}." : ""),
        };
        td.Show();
    }

    public static double? LevelZ(View v) => v is ViewPlan { GenLevel: { } l } ? UnitConv.M(l.ProjectElevation) : null;
}

/// <summary>
/// Loteamento: limite da gleba (linha de propriedade, linhas, Toposolid ou cliques) → quadras reconhecidas (a gleba menos as
/// vias) → lotes pela testada, com a medida padrão, numeração por quadra e categorias. Num projeto que já tem loteamento:
/// reconhece de novo as quadras (vias mudaram) ou abre as categorias.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdLoteamento : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var existing = LotService.Loteamentos(doc).FirstOrDefault();
        if (existing != null)
        {
            var nq = LotService.Quadras(doc, existing.Id).Count;
            var td = new TaskDialog(AppTitle)
            {
                MainInstruction = $"Loteamento \"{existing.Nome}\" ({nq} quadra(s))",
                MainContent = "O que fazer?",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Reconhecer as quadras de novo", "As vias mudaram: refaz o contorno das quadras (mantém testadas, lotes personalizados e nomes).");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Categorias, cores, mínimos e filtros");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Criar outro loteamento (outra gleba)");
            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1: return Rerecognize(uidoc, existing);
                case TaskDialogResult.CommandLink2: return CategoriasCommand.Run(uidoc);
                case TaskDialogResult.CommandLink3: break;
                default: return Result.Cancelled;
            }
        }

        var notes = new List<string>();
        var (gleba, ids) = PickGleba(uidoc, notes);
        if (gleba == null)
        {
            if (notes.Count > 0) throw new UserMessageException(string.Join("\n", notes));
            return Result.Cancelled;
        }
        var opt = new NovoLoteamentoOpcoes();
        var service = new MarkingService(doc, uidoc.ActiveView, interactive: false);
        var occ = LotService.Ocupacao(doc, service, gleba, nativeFloors: true);
        var rec = Reconhecimento.Quadras(gleba, occ, opt.AreaMinimaQuadra);
        if (rec.Quadras.Count == 0)
            throw new UserMessageException("Nenhuma quadra na gleba: não há área livre entre as vias.\n\nCrie as vias (Via / Pista) antes, ou desenhe a gleba maior que o sistema viário.");
        var lot = new LoteamentoDefinition { Gleba = gleba.Outer.ToList(), GlebaElementos = ids, Z = LotCommands.LevelZ(uidoc.ActiveView) ?? 0 };
        if (UiHelpers.ShowModal(LotForms.Novo(lot, opt, rec, Reconhecimento.Ocupacao(occ))) != true) return Result.Cancelled;
        var quadras = rec.Quadras.Select(r =>
        {
            var q = Reconhecimento.NovaQuadra(r, lot, opt.Frente, opt.Profundidade);
            q.Sobra = opt.Sobra;
            q.SentidoHorario = opt.SentidoHorario;
            return q;
        }).ToList();
        var results = LotCommands.Commit(uidoc, lot, quadras, "SV - Loteamento");
        var lots = quadras.Sum(q => LotDivider.Divide(q, lot.Categorias).Parcelas.Count(p => p.Categoria == CategoriaLote.Lote));
        LotCommands.Report($"Loteamento \"{lot.Nome}\": {quadras.Count} quadra(s) e {lots} lote(s).", results, rec.Avisos.Concat(notes).Append(
            "Próximos passos: Quadra (testadas, esquinas, numeração), Lote (medidas e categoria), Nomear Vias, Detalhar Lotes, Quadros, Memorial e Masterplan."));
        return Result.Succeeded;
    }

    private static (Polygon2? Gleba, List<string> Ids) PickGleba(UIDocument uidoc, List<string> notes)
    {
        var doc = uidoc.Document;
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = "Limite da gleba",
            MainContent = "A gleba é a área total do loteamento: as quadras são a gleba menos as vias.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Selecionar a gleba", "Linha de propriedade, linhas fechadas (de modelo ou de detalhe) ou o Toposolid – depois clique Concluir.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Desenhar o contorno da gleba", "Cliques nos vértices; ESC fecha o contorno (as linhas ficam no modelo).");
        switch (td.Show())
        {
            case TaskDialogResult.CommandLink1:
            {
                var pre = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => e != null && new GlebaSelectionFilter().AllowElement(e)).ToList();
                List<Element> els;
                if (pre.Count > 0) els = pre!;
                else
                {
                    try
                    {
                        els = uidoc.Selection.PickObjects(ObjectType.Element, new GlebaSelectionFilter(), "Selecione o limite da gleba e clique Concluir").Select(doc.GetElement).ToList();
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return (null, new());
                    }
                }
                var poly = LotService.GlebaFrom(doc, els, notes);
                return (poly, els.Select(e => e.UniqueId).ToList());
            }
            case TaskDialogResult.CommandLink2:
            {
                var pick = Picking.PickPolyline(uidoc, "Clique os vértices da gleba – ESC fecha o contorno", closed: true, keepAsModelLines: true);
                if (pick == null || pick.Value.Points.Count < 3) return (null, new());
                var (pts, _) = Picking.ToCore(pick.Value.Points);
                var poly = new Polygon2(pts);
                return (poly.IsValid ? poly : null, pick.Value.Lines.Select(id => doc.GetElement(id)?.UniqueId).Where(u => u != null).Cast<string>().ToList());
            }
            default:
                return (null, new());
        }
    }

    /// <summary>Quadras de novo a partir das vias atuais: contornos atualizados, quadras novas acrescentadas.</summary>
    private static Result Rerecognize(UIDocument uidoc, LoteamentoDefinition lot)
    {
        var doc = uidoc.Document;
        var gleba = lot.GlebaPolygon ?? throw new UserMessageException("O loteamento não tem gleba gravada.");
        var service = new MarkingService(doc, uidoc.ActiveView, interactive: false);
        var occ = LotService.Ocupacao(doc, service, gleba, nativeFloors: true);
        var rec = Reconhecimento.Quadras(gleba, occ);
        var quadras = LotService.Quadras(doc, lot.Id);
        var changed = new List<QuadraDefinition>();
        var used = quadras.Select(q => q.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = Reconhecimento.Nomes(used.Count + rec.Quadras.Count + 5).Where(n => !used.Contains(n)).GetEnumerator();
        var matched = new HashSet<string>();
        var template = quadras.FirstOrDefault();
        foreach (var r in rec.Quadras)
        {
            var q = quadras.FirstOrDefault(x => !matched.Contains(x.Id) && x.Polygon is { } p && (r.Contorno.Contains(p.Centroid) || p.Contains(r.Contorno.Centroid)));
            if (q != null)
            {
                matched.Add(q.Id);
                q.Contorno = r.Contorno.Outer.ToList();
                changed.Add(q);
                continue;
            }
            names.MoveNext();
            var nq = Reconhecimento.NovaQuadra(r with { Nome = names.Current ?? r.Nome }, lot, template?.Frente ?? 12, template?.Profundidade ?? 30);
            if (template != null) { nq.Sobra = template.Sobra; nq.SentidoHorario = template.SentidoHorario; }
            changed.Add(nq);
        }
        var lost = quadras.Where(q => !matched.Contains(q.Id)).Select(q => q.Nome).ToList();
        var results = LotCommands.Commit(uidoc, lot, changed, "SV - Loteamento – reconhecer quadras");
        var extra = rec.Avisos.ToList();
        if (lost.Count > 0) extra.Add($"Quadra(s) {string.Join(", ", lost)} não encontrada(s) entre as vias atuais – ficaram como estavam (apague os lotes delas se não existem mais).");
        LotCommands.Report($"{changed.Count} quadra(s) atualizada(s).", results, extra);
        return Result.Succeeded;
    }
}

/// <summary>Quadra: medidas padrão, sobra, numeração (sentido e lote 1), testadas apontadas e escolha do lote de esquina.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdQuadraLote : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var picked = LotCommands.PickLot(uidoc, "Clique um lote da quadra");
        if (picked == null) return Result.Cancelled;
        var q = picked.Value.Quadra;
        var lot = LotService.Loteamento(doc, q.LoteamentoId);
        while (true)
        {
            var acao = LotForms.AcaoQuadra.Nenhuma;
            if (UiHelpers.ShowModal(LotForms.Quadra(q, lot, a => acao = a)) != true) return Result.Cancelled;
            if (acao == LotForms.AcaoQuadra.Nenhuma) break;
            switch (acao)
            {
                case LotForms.AcaoQuadra.ApontarTestadas:
                {
                    var pts = new List<Vec2>();
                    while (Picking.PickPoint(uidoc, $"Clique a testada {pts.Count + 1} (sobre o lado da quadra, na ordem de prioridade) – ESC termina") is { } p)
                        pts.Add(DetailHelpers.ToCore(p));
                    if (pts.Count > 0) q.Testadas = Retestadas(q, pts);
                    break;
                }
                case LotForms.AcaoQuadra.TestadasAutomaticas:
                {
                    var gleba = lot?.GlebaPolygon ?? q.Polygon!;
                    var occ = LotService.Ocupacao(doc, new MarkingService(doc, uidoc.ActiveView, interactive: false), gleba, nativeFloors: true);
                    var pts = Reconhecimento.TestadasAutomaticas(q.Polygon!, Reconhecimento.Ocupacao(occ));
                    if (pts.Count > 0) q.Testadas = Retestadas(q, pts);
                    else TaskDialog.Show(AppTitle, "Nenhum lado desta quadra encosta numa via.");
                    break;
                }
                case LotForms.AcaoQuadra.LoteInicial:
                {
                    var l = LotCommands.PickLot(uidoc, "Clique o lote que recebe o número 1");
                    if (l != null && l.Value.Quadra.Id == q.Id) q.LoteInicial = l.Value.Key;
                    else if (l != null) TaskDialog.Show(AppTitle, "Esse lote é de outra quadra.");
                    break;
                }
            }
        }
        var results = LotCommands.Commit(uidoc, lot, new[] { q }, $"SV - Quadra {q.Nome}");
        LotCommands.Report($"Quadra {q.Nome} atualizada.", results);
        return Result.Succeeded;
    }

    /// <summary>Testadas novas pelos pontos (ordem = prioridade), mantendo medidas e nome de via das que caem no mesmo lado.</summary>
    private static List<TestadaLote> Retestadas(QuadraDefinition q, List<Vec2> pts)
    {
        var chains = LotDivider.Cadeias(q.Polygon!.Outer);
        var res = new List<TestadaLote>();
        var used = new HashSet<string>();
        foreach (var p in pts)
        {
            var c = LotDivider.CadeiaEm(chains, p);
            var old = q.Testadas.FirstOrDefault(t => LotDivider.CadeiaEm(chains, t.Ponto) == c && !used.Contains(t.Id));
            var id = old?.Id ?? NextId(q.Testadas.Select(t => t.Id).Concat(res.Select(t => t.Id)));
            used.Add(id);
            res.Add(new TestadaLote { Id = id, Ponto = p, Frente = old?.Frente, Profundidade = old?.Profundidade, Via = old?.Via });
        }
        return res;
    }

    private static string NextId(IEnumerable<string> used)
    {
        var set = used.ToHashSet();
        var i = 1;
        while (set.Contains("T" + i)) i++;
        return "T" + i;
    }
}

/// <summary>Lote: frente, profundidade, divisão e categoria – os vizinhos e a numeração se ajustam.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdLote : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var picked = LotCommands.PickLot(uidoc, "Clique o lote a personalizar");
        if (picked == null) return Result.Cancelled;
        var (_, q, key) = picked.Value;
        var lot = LotService.Loteamento(doc, q.LoteamentoId);
        var d = LotDivider.Divide(q, lot?.Categorias);
        var p = d.Parcelas.FirstOrDefault(x => x.Chave == key) ?? throw new UserMessageException("Lote não encontrado na quadra (use Atualizar).");
        if (UiHelpers.ShowModal(LotForms.Lote(q, lot, p)) != true) return Result.Cancelled;
        var results = LotCommands.Commit(uidoc, lot, new[] { q }, $"SV - Lote {q.Nome}-{p.Numero}");
        var warn = results.SelectMany(r => r.Warnings).ToList();
        if (warn.Count > 0) LotCommands.Report($"Lote {p.Numero} da quadra {q.Nome}", results);
        return Result.Succeeded;
    }
}

/// <summary>Nome das ruas: selecione a via (eixo, piso ou elementos dela) e escreva o nome – vale para o memorial e os textos.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdNomearVias : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var lot = LotCommands.RequireLot(doc);
        var count = 0;
        while (true)
        {
            List<Element> els;
            try
            {
                els = uidoc.Selection.PickObjects(ObjectType.Element, new RoadNameSelectionFilter(),
                    "Selecione a via (eixo, piso ou um elemento dela) e clique Concluir – ESC termina").Select(doc.GetElement).Where(e => e != null).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }
            var axes = els.Select(e => (Element: e, Axis: LotService.AxisOf(doc, e))).Where(x => x.Axis is { Count: >= 2 }).ToList();
            if (axes.Count == 0)
            {
                TaskDialog.Show(AppTitle, "Não foi possível ler o eixo dos elementos escolhidos.");
                continue;
            }
            var uids = axes.Select(a => a.Element.UniqueId).ToHashSet();
            var name = lot.Vias.FirstOrDefault(v => v.Elementos.Any(uids.Contains))?.Nome ?? "";
            if (UiHelpers.ShowModal(LotForms.NomeVia(() => name, v => name = v, lot.Vias.Select(v => v.Nome))) != true) continue;
            lot.Vias.RemoveAll(v => v.Elementos.Any(uids.Contains));
            if (!string.IsNullOrWhiteSpace(name))
                foreach (var (e, axis) in axes) lot.Vias.Add(new ViaNomeada { Nome = name, Eixo = axis!, Elementos = new() { e.UniqueId } });
            LotCommands.Commit(uidoc, lot, Array.Empty<QuadraDefinition>(), "SV - Nomear via");
            count++;
        }
        return count > 0 ? Result.Succeeded : Result.Cancelled;
    }
}

/// <summary>Detalhamento dos lotes na vista ativa: número, área, cotas dos lados, categoria, quadra e vias.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdDetalharLotes : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var view = DetailHelpers.RequireDetailView(uidoc);
        var lot = LotCommands.RequireLot(doc);
        var existing = MarkingStorage.Definitions(doc).OfType<LoteamentoDetalheDefinition>()
            .FirstOrDefault(d => d.LoteamentoId == lot.Id && d.Output.ViewId == view.UniqueId);
        var det = existing ?? new LoteamentoDetalheDefinition { LoteamentoId = lot.Id };
        var service = new MarkingService(doc, view, interactive: false);
        var ctx = service.ProjectContext(view);
        if (UiHelpers.ShowModal(LotForms.Detalhe(det, () => MarkingBuilder.Build(det, null, ctx), view.Scale)) != true) return Result.Cancelled;
        DetailHelpers.PrepareOutput(det, view);
        var results = MarkingCreator.Commit(uidoc, new[] { det }, "SV - Detalhar lotes");
        var warn = results.SelectMany(r => r.Warnings).ToList();
        if (warn.Count > 0) LotCommands.Report("Detalhamento dos lotes", results);
        return Result.Succeeded;
    }
}

/// <summary>Quadros do loteamento (áreas, quadras, lotes) numa Legenda própria ou na vista, e a tabela nativa dos lotes.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdQuadrosLoteamento : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var lot = LotCommands.RequireLot(doc);
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = "Quadros do loteamento",
            MainContent = "Os quadros vão para uma vista própria (Legenda) que pode ser colocada em qualquer folha – não dependem de planta nenhuma.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Quadro de áreas", "Gleba, lotes, institucional, áreas verdes, sobras, sistema viário e áreas públicas – m² e %.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Quadro de quadras", "Número de lotes e área por categoria de cada quadra.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Quadro de lotes", "Quadra, lote, categoria, frente e área de cada lote.");
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Tabela do Revit (lista de lotes)", "Tabela nativa: quadra, lote, categoria e área, atualizada pelo Revit.");
        var r = td.Show();
        if (r == TaskDialogResult.CommandLink4) return Schedule(uidoc);
        var tipo = r switch
        {
            TaskDialogResult.CommandLink1 => TipoQuadroLoteamento.Areas,
            TaskDialogResult.CommandLink2 => TipoQuadroLoteamento.Quadras,
            TaskDialogResult.CommandLink3 => TipoQuadroLoteamento.Lotes,
            _ => (TipoQuadroLoteamento?)null,
        };
        if (tipo == null) return Result.Cancelled;
        var template = new QuadroLoteamentoDefinition { LoteamentoId = lot.Id, Conteudo = tipo.Value };
        var label = template.KindName;
        var place = TablePlacementWindow.Ask(doc, uidoc.ActiveView, label.ToLowerInvariant(), ProjectTableViews.ViewName(template));
        if (place == null) return Result.Cancelled;
        if (place.OwnView) return ProjectTableCommand.Run(uidoc, template, place, "SV - " + label, label);
        var view = DetailHelpers.RequireDetailView(uidoc);
        var at = Picking.PickPoint(uidoc, "Clique o canto superior esquerdo do quadro");
        if (at == null) return Result.Cancelled;
        var def = (QuadroLoteamentoDefinition)template.CloneWithNewId();
        def.Position = DetailHelpers.ToCore(at);
        DetailHelpers.PrepareOutput(def, view);
        ProjectTableCommand.Warnings(MarkingCreator.Commit(uidoc, new[] { def }, "SV - " + label));
        return Result.Succeeded;
    }

    private static Result Schedule(UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var notes = new List<string>();
        ViewSchedule? s;
        using (var t = new Transaction(doc, "SV - Tabela de lotes"))
        {
            t.Start();
            s = LotService.LotSchedule(doc, notes);
            t.Commit();
        }
        if (s == null) throw new UserMessageException(string.Join("\n", notes.DefaultIfEmpty("Tabela não criada.")));
        uidoc.ActiveView = s;
        return Result.Succeeded;
    }
}

/// <summary>Memorial descritivo do loteamento (.docx): gleba, quadro de áreas, sistema viário, quadras e descrição de cada lote.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdMemorialLoteamento : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var lot = LotCommands.RequireLot(doc);
        var resumo = LotReport.Resumo(lot, LotService.Quadras(doc, lot.Id));
        var dlg = new SaveFileDialog
        {
            Filter = "Documento do Word|*.docx",
            FileName = $"Memorial descritivo - {lot.Nome}.docx".Replace(":", ""),
            InitialDirectory = !string.IsNullOrEmpty(doc.PathName) ? Path.GetDirectoryName(doc.PathName) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() != true) return Result.Cancelled;
        try
        {
            File.WriteAllBytes(dlg.FileName, DocxWriter.Write(LotMemorial.Build(resumo)));
        }
        catch (Exception ex)
        {
            throw new UserMessageException("Não foi possível gravar o memorial (o arquivo está aberto no Word?): " + ex.Message);
        }
        var td = new TaskDialog(AppTitle)
        {
            MainInstruction = "Memorial do loteamento gerado.",
            MainContent = dlg.FileName + $"\n\n{resumo.Quadras.Count} quadra(s), {resumo.TotalLotes} lote(s). Preencha no Word os campos em branco (município, limites da gleba, responsável técnico, ART/RRT)." +
                          (resumo.Avisos.Count > 0 ? "\n\nAvisos:\n" + string.Join("\n", resumo.Avisos.Take(10).Select(a => "• " + a)) : ""),
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir no Word");
        if (td.Show() == TaskDialogResult.CommandLink1)
            try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Abrir memorial do loteamento", ex); }
        return Result.Succeeded;
    }
}

/// <summary>Categorias (cores, hachuras, numeração, mínimos) e filtros da vista ativa.</summary>
internal static class CategoriasCommand
{
    private static readonly string[] Palette = { "#8FA8E0", "#E0C38F", "#B58FE0", "#8FD9E0", "#E08FB8", "#A0A0A0" };

    public static Result Run(UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var lot = LotCommands.RequireLot(doc);
        var work = (LoteamentoDefinition)MarkingDefinition.FromJson(lot.ToJson())!;
        var opt = new LotForms.CategoriasOpcoes();
        if (UiHelpers.ShowModal(LotForms.Categorias(work, opt)) != true) return Result.Cancelled;
        var quadras = LotService.Quadras(doc, lot.Id);
        // Renomeadas: mínimos e ajustes acompanham.
        foreach (var (from, to) in opt.Renomear)
        {
            if (work.Minimos.Remove(from, out var m)) work.Minimos[to] = m;
            foreach (var q in quadras)
            {
                if (q.CategoriaPadrao == from) q.CategoriaPadrao = to;
                foreach (var a in q.Ajustes.Where(a => a.Categoria == from)) a.Categoria = to;
            }
        }
        // Excluídas: os lotes delas voltam a Lote.
        foreach (var name in opt.Excluir)
        {
            var now = opt.Renomear.TryGetValue(name, out var rn) ? rn : name;
            work.Categorias.RemoveAll(c => c.Nome == now);
            work.Minimos.Remove(now);
            foreach (var q in quadras)
            {
                if (q.CategoriaPadrao == now) q.CategoriaPadrao = CategoriaLote.Lote;
                foreach (var a in q.Ajustes.Where(a => a.Categoria == now)) a.Categoria = null;
            }
        }
        if (!string.IsNullOrWhiteSpace(opt.NovaCategoria) && work.Categorias.All(c => !string.Equals(c.Nome, opt.NovaCategoria, StringComparison.OrdinalIgnoreCase)))
        {
            var initials = new string(opt.NovaCategoria.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).Take(3).ToArray());
            work.Categorias.Add(new CategoriaLote
            {
                Nome = opt.NovaCategoria, Cor = Palette[work.Categorias.Count % Palette.Length], Hachura = Core.Model.Hachura.Cruzada,
                Prefixo = initials.Length > 0 ? initials : "C",
            });
        }
        var results = LotCommands.Commit(uidoc, work, quadras, "SV - Categorias do loteamento");
        var notes = new List<string>();
        if (opt.AplicarNaVista)
        {
            var visible = opt.Visivel.ToDictionary(kv => opt.Renomear.TryGetValue(kv.Key, out var n) ? n : kv.Key, kv => kv.Value);
            using var t = new Transaction(doc, "SV - Filtros dos lotes");
            t.Start();
            try { LotService.ApplyCategoryFilters(doc, uidoc.ActiveView, work, visible, opt.Cores, notes); }
            catch (Exception ex) { notes.Add("Filtros não aplicados: " + ex.Message); }
            t.Commit();
        }
        LotCommands.Report("Categorias do loteamento atualizadas.", results, notes);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdCategoriasLotes : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => CategoriasCommand.Run(uidoc);
}

/// <summary>
/// Masterplan: cópia da planta ativa com o layout limpo – lotes nas cores das categorias, vias, calçadas, árvores e mobiliário,
/// nomes de quadra e de via e números dos lotes; sem sinalização, cotas nem textos técnicos. Cria o modelo de vista "SV - Masterplan".
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdMasterplan : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var lot = LotCommands.RequireLot(doc);
        var source = uidoc.ActiveView;
        var notes = new List<string>();
        View mp;
        List<RenderResult> results;
        using (var tg = new TransactionGroup(doc, "SV - Masterplan"))
        {
            tg.Start();
            using (var t = new Transaction(doc, "SV - Masterplan – vista"))
            {
                t.Start();
                mp = LotService.CreateMasterplanView(doc, source, lot, notes);
                t.Commit();
            }
            var det = Masterplan.Detalhe(lot.Id);
            DetailHelpers.PrepareOutput(det, mp);
            results = MarkingCreator.Commit(uidoc, new[] { det }, "SV - Masterplan – textos");
            tg.Assimilate();
        }
        uidoc.ActiveView = mp;
        LotCommands.Report($"Masterplan \"{mp.Name}\" criado.", results, notes.Append(
            "Ficam os lotes por categoria, vias, calçadas, canteiros, árvores e mobiliário, nomes de quadra e de via e números dos lotes; " +
            "saem a sinalização, o piso tátil, a drenagem, as cotas e os textos técnicos."));
        return Result.Succeeded;
    }
}
