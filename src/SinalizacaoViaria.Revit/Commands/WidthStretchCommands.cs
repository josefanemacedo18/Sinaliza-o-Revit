using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Janela do trecho com outra largura (a mesma na ferramenta e na janela da via): o que muda, nova largura, estacas de A e B
/// e transição em cada ponta, com um desenho do trecho marcando "Início (A)" e "Fim (B)".
/// </summary>
internal static class WidthStretchForm
{
    public static bool Show(RoadSetup setup, TrechoLargura t, string title, Action<FormWindow>? extra = null)
    {
        var elements = setup.ElementosTrecho();
        if (elements.Count == 0) throw new UserMessageException("Esta via não tem elementos cuja largura possa mudar por trecho.");
        if (!elements.Contains(t.Elemento)) t.Elemento = elements[0];
        if (t.NovaLargura <= 0) t.NovaLargura = setup.LarguraAtual(t.Elemento) ?? 1;
        var w = new FormWindow(title, "Alterar largura por trecho",
            "Entre o início (A) e o fim (B) o elemento escolhido fica com a nova largura; antes de A e depois de B, as transições voltam à " +
            "largura da seção. Os outros elementos mantêm a largura. A via é refeita a partir do eixo (interseções, linhas e pisos acompanham).",
            null, () => Preview(setup, t), false, "Continuar", 1080, 700);
        w.Choice("O que muda", elements.Select(e => ($"{TrechoLargura.Rotulo(e)} – hoje {UiHelpers.F(setup.LarguraAtual(e) ?? 0)} m", e)), () => t.Elemento,
                v => t.Elemento = v, preset: v => { t.Elemento = v; t.NovaLargura = setup.LarguraAtual(v) ?? t.NovaLargura; })
         .Number("Nova largura (m)", () => t.NovaLargura, v => t.NovaLargura = Math.Max(0, v), 0, 60, "0.00",
             tooltip: "Largura do elemento entre A e B. Calçada: o meio-fio fica e o alinhamento se desloca. Pista: metade para cada lado. Canteiro: os dois lados se afastam.")
         .Text("Início (A) – estaca (m ou 5+10)", () => PontoLargura.FormatEstaca(t.A), v => t.EstacaA = PontoLargura.ParseEstaca(v) ?? t.EstacaA)
         .Text("Fim (B) – estaca (m ou 5+10)", () => PontoLargura.FormatEstaca(t.B), v => t.EstacaB = PontoLargura.ParseEstaca(v) ?? t.EstacaB)
         .Number("Transição antes de A (m)", () => t.TransicaoA, v => t.TransicaoA = Math.Max(0, v), 0, 500)
         .Number("Transição depois de B (m)", () => t.TransicaoB, v => t.TransicaoB = Math.Max(0, v), 0, 500)
         .Check("Transições em curva S (suave)", () => t.CurvaS, v => t.CurvaS = v);
        extra?.Invoke(w);
        return UiHelpers.ShowModal(w) == true;
    }

    /// <summary>Desenho do trecho numa via reta com a mesma seção: A e B marcados, cotas da largura nova.</summary>
    private static FormPreview? Preview(RoadSetup setup, TrechoLargura t)
    {
        var c = setup.Clone();
        c.TrechosLargura.Clear();
        c.LargurasVariaveis.Clear();
        c.Recuos.Clear();
        c.Retornos.Clear();
        c.ExtensoesCalcada.Clear();
        c.TravessiasCanteiro.Clear();
        var tt = t.Clone();
        var shift = 15 - t.Inicio;
        tt.EstacaA = t.A + shift;
        tt.EstacaB = t.B + shift;
        c.TrechosLargura.Add(tt);
        var len = tt.Fim + 15;
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(len, 0) });
        var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
        var geo = new MarkingGeometry();
        foreach (var d in c.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), PluginContext.Catalog, axis: axis))
            geo.Merge(MarkingBuilder.Build(d, axis, ctx));
        var guides = new List<IReadOnlyList<Vec2>>();
        var now = setup.LarguraAtual(t.Elemento) ?? 0;
        if (c.TrechoSpan(tt) is { } span)
        {
            var top = c.Nominal(true).Alinhamento + Math.Max(0, t.NovaLargura - now) + 1.5;
            var bottom = -c.Nominal(false).Alinhamento - Math.Max(0, t.NovaLargura - now) - 1.5;
            foreach (var (x, label) in new[] { (tt.A, "Início (A)"), (tt.B, "Fim (B)") })
            {
                guides.Add(new[] { new Vec2(x, bottom), new Vec2(x, top) });
                geo.Annotations.Add(new AnnotationText(new Vec2(x, top + 1.2), label, 3.0));
                // Cota da largura nova: traço com setas nas duas pontas e o valor ao lado.
                var (lo, hi) = span;
                guides.Add(new[] { new Vec2(x + 0.8, lo), new Vec2(x + 0.8, hi) });
                foreach (var (y, dir) in new[] { (lo, 1.0), (hi, -1.0) })
                    guides.Add(new[] { new Vec2(x + 0.5, y + dir * 0.6), new Vec2(x + 0.8, y), new Vec2(x + 1.1, y + dir * 0.6) });
                geo.Annotations.Add(new AnnotationText(new Vec2(x + 2.2, (lo + hi) / 2), $"{UiHelpers.F(t.NovaLargura)} m", 2.5) { Rotation = Math.PI / 2 });
            }
        }
        var info = $"{TrechoLargura.Rotulo(t.Elemento)}: {UiHelpers.F(now)} → {UiHelpers.F(t.NovaLargura)} m entre A (estaca {PontoLargura.FormatEstaca(t.A)}) " +
                   $"e B (estaca {PontoLargura.FormatEstaca(t.B)}); transições de {UiHelpers.F(t.TransicaoA)} m antes de A e {UiHelpers.F(t.TransicaoB)} m depois de B.";
        if (t.B - t.A < 0.5) info += "\n⚠ A e B quase coincidem.";
        return new FormPreview(geo, null, guides, info, ViewScale: 500);
    }
}

/// <summary>
/// Alterar Largura por Trecho: clique a via, o início (A) e o fim (B) no eixo (ou digite as estacas), escolha o que muda, a nova
/// largura e as transições. A planta mostra setas e cotas em A e B antes de confirmar; o trecho fica na seção da via.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdLarguraTrecho : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var pick = Picking.PickPoint(uidoc, "Alterar largura por trecho: clique o INÍCIO (A) do trecho sobre a via – ESC cancela");
        if (pick == null) return Result.Cancelled;
        if (RoadExtensionCommand.RoadAt(uidoc, UnitConv.ToVec2(pick)) is not { } road)
            throw new UserMessageException("Nenhuma via do plugin junto ao ponto clicado.");
        var pav = road.Def;
        var setup = RoadTemplates.FromJson(pav.SetupJson)
                    ?? throw new UserMessageException("Esta via não guardou a seção transversal (versão anterior): use Editar → A via inteira uma vez e repita.");
        var len = road.Axis.Length;
        var sa = Math.Round(road.Axis.Project(UnitConv.ToVec2(pick)).Station, 2);
        var t = new TrechoLargura { EstacaA = sa, EstacaB = Math.Min(len, sa + 30) };
        var pb = Picking.PickPoint(uidoc, "Alterar largura por trecho: clique o FIM (B) do trecho – ESC: digitar as estacas na janela");
        if (pb != null) t.EstacaB = Math.Round(road.Axis.Project(UnitConv.ToVec2(pb)).Station, 2);
        (t.EstacaA, t.EstacaB) = (Math.Min(t.EstacaA, t.EstacaB), Math.Max(t.EstacaA, t.EstacaB));
        var speedRatio = setup.Speed <= 40 ? 5 : setup.Speed <= 60 ? 8 : 15;   // 1 : n – indicativo, como nos recuos
        t.TransicaoA = t.TransicaoB = 10;

        // Trechos já gravados do mesmo elemento que se sobrepõem a este são substituídos (ou só removidos).
        bool Overlaps(TrechoLargura x) => x.Elemento == t.Elemento && x.Fim >= t.Inicio && x.Inicio <= t.Fim;
        var remove = false;
        if (!WidthStretchForm.Show(setup, t, "Alterar largura por trecho", w =>
            {
                w.Section("Outros trechos desta via", $"{setup.TrechosLargura.Count} trecho(s) gravado(s). Os do mesmo elemento que se sobrepõem a este são substituídos.");
                if (setup.TrechosLargura.Count > 0) w.Check("Só remover os trechos sobrepostos deste elemento (sem criar este)", () => remove, v => remove = v);
                w.Section("Transições", $"Referência: 1 : {speedRatio} para {setup.Speed:0} km/h (indicativo – conferir o manual do órgão [a confirmar]).");
            })) return Result.Cancelled;
        if (t.B > len + 0.01 || t.A < -0.01) throw new UserMessageException($"As estacas devem ficar entre 0 e {len:0.00} m (comprimento do eixo).");
        if (!remove && !ConfirmInPlan(uidoc, road, setup, t)) return Result.Cancelled;

        var work = setup.Clone();
        var removed = work.TrechosLargura.RemoveAll(Overlaps);
        if (remove && removed == 0) throw new UserMessageException("Nenhum trecho deste elemento se sobrepõe ao indicado – nada a remover.");
        if (!remove) work.TrechosLargura.Add(t);
        var results = RoadAccessCommand.Regenerate(uidoc, pav, work);
        if (work.Warnings.Count > 0 && results.Count > 0) results[0].Warnings.InsertRange(0, work.Warnings);
        Report("Largura por trecho", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }

    /// <summary>Setas e cotas provisórias na planta em A e B; desfeitas depois da resposta.</summary>
    private static bool ConfirmInPlan(UIDocument uidoc, IntersectionRoad road, RoadSetup setup, TrechoLargura t)
    {
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;
        if (setup.TrechoSpan(t) is not { } span) return true;
        var now = setup.LarguraAtual(t.Elemento) ?? 0;
        using var group = new TransactionGroup(doc, "SV - Prévia do trecho");
        group.Start();
        try
        {
            var drawn = false;
            using (var tr = new Transaction(doc, "SV - setas e cotas"))
            {
                tr.Start();
                try
                {
                    var textType = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                    const double z = 0;
                    void Seg(Vec2 a, Vec2 b)
                    {
                        if (a.DistanceTo(b) > 0.05) doc.Create.NewDetailCurve(view, Line.CreateBound(UnitConv.ToXyz(a, z), UnitConv.ToXyz(b, z)));
                    }
                    void Arrow(Vec2 tip, Vec2 dir)
                    {
                        var d = dir.Normalized();
                        var side = d.PerpLeft;
                        Seg(tip, tip - d * 0.8 + side * 0.35);
                        Seg(tip, tip - d * 0.8 - side * 0.35);
                    }
                    foreach (var (st, label) in new[] { (t.A, "A – início"), (t.B, "B – fim") })
                    {
                        var s = Math.Clamp(st, 0, road.Axis.Length);
                        var p = road.Axis.PointAt(s);
                        var n = road.Axis.TangentAt(s).PerpLeft;
                        var (lo, hi) = span;
                        // Cota: traço entre as bordas novas do elemento, com setas nas duas pontas.
                        var a = p + n * lo;
                        var b = p + n * hi;
                        Seg(a, b);
                        Arrow(a, a - b);
                        Arrow(b, b - a);
                        // Seta de chamada até o ponto do eixo e o texto.
                        var outer = Math.Max(Math.Abs(lo), Math.Abs(hi)) + 1.5;
                        var sg = Math.Abs(hi) >= Math.Abs(lo) ? 1 : -1;
                        var tip = p + n * (sg * outer);
                        Seg(tip, tip + n * (sg * 5));
                        Arrow(tip, -n * sg);
                        var text = $"{label}: estaca {PontoLargura.FormatEstaca(st)}\n{TrechoLargura.Rotulo(t.Elemento)}: {UiHelpers.F(t.NovaLargura)} m (hoje {UiHelpers.F(now)} m)";
                        TextNote.Create(doc, view.Id, UnitConv.ToXyz(tip + n * (sg * 5.5), z), text, textType);
                    }
                    drawn = true;
                    tr.Commit();
                }
                catch (Exception ex)
                {
                    Log.Error("Prévia do trecho na planta", ex);
                    if (tr.HasStarted() && !tr.HasEnded()) tr.RollBack();
                }
            }
            if (drawn) uidoc.RefreshActiveView();
            var td = new TaskDialog(AppTitle)
            {
                MainInstruction = "Aplicar o trecho com a nova largura?",
                MainContent = (drawn ? "A planta mostra as setas e as cotas em A e B. " : "(Esta vista não aceita linhas de detalhe – sem setas na planta.) ") +
                              $"{TrechoLargura.Rotulo(t.Elemento)}: {UiHelpers.F(now)} → {UiHelpers.F(t.NovaLargura)} m de {PontoLargura.FormatEstaca(t.A)} a " +
                              $"{PontoLargura.FormatEstaca(t.B)}, transições de {UiHelpers.F(t.TransicaoA)} m e {UiHelpers.F(t.TransicaoB)} m.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes,
            };
            var ok = td.Show() == TaskDialogResult.Yes;
            group.RollBack();
            return ok;
        }
        catch
        {
            if (group.HasStarted() && !group.HasEnded()) group.RollBack();
            throw;
        }
    }
}
