using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Parcelamento;
using SinalizacaoViaria.Core.Reports;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

internal sealed partial class AutoTestRunner
{
    // ================================================================== Loteamento

    private void Loteamento()
    {
        const double Y = 3400;
        LoteamentoDefinition? lot = null;
        var quadras = new List<QuadraDefinition>();
        Step("Loteamento", "Gleba com três vias → quadras reconhecidas e lotes 12 × 30 (subdivisões do Toposolid ou pisos planos)", s =>
        {
            MakeRoad(s, "loteamento leste-oeste", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (0, Y), (240, Y));
            MakeRoad(s, "loteamento norte-sul 1", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (70, Y - 90), (70, Y + 90));
            MakeRoad(s, "loteamento norte-sul 2", T(1), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (170, Y - 90), (170, Y + 90));
            var gleba = new Polygon2(new[] { P(0, Y - 90), P(240, Y - 90), P(240, Y + 90), P(0, Y + 90) });
            var service = new MarkingService(_doc, _plan ?? _uidoc.ActiveView, false);
            var occ = LotService.Ocupacao(_doc, service, gleba, nativeFloors: false);
            var rec = Reconhecimento.Quadras(gleba, occ);
            if (rec.Quadras.Count != 6) s.Error($"Esperadas 6 quadras entre as três vias; reconhecidas {rec.Quadras.Count}.");
            if (rec.Quadras.Count == 0) return;
            lot = new LoteamentoDefinition { Nome = "Autoteste", Gleba = gleba.Outer.ToList(), Z = _z0 };
            quadras = rec.Quadras.Select(r => Reconhecimento.NovaQuadra(r, lot, 12, 30)).ToList();
            Commit(s, new MarkingDefinition[] { lot }.Concat(quadras), "loteamento");
            var expected = quadras.Sum(q => LotDivider.Divide(q, lot).Parcelas.Count);
            var lotId = lot.Id;
            var els = MarkingStorage.All(_doc).Where(r => r.Definition is QuadraDefinition qd && qd.LoteamentoId == lotId).ToList();
            if (els.Count != expected) s.Error($"Lotes no modelo: {els.Count} de {expected}.");
            var subs = els.Count(r => r.Element is Toposolid);
            s.Note($"{rec.Quadras.Count} quadra(s), {expected} área(s): {subs} subdivisão(ões) do Toposolid e {els.Count - subs} piso(s) plano(s).");
            var area = els.Sum(r => UnitConv.M2(r.Element.get_Parameter(SharedParameters.AreaLote.Guid)?.AsDouble() ?? 0));
            var total = quadras.Sum(q => q.Polygon!.Area);
            if (Math.Abs(area - total) > 0.5) s.Error($"Soma das áreas dos lotes ({area:0.00} m²) diferente da área das quadras ({total:0.00} m²).");
            if (els.Any(r => string.IsNullOrEmpty(r.Element.get_Parameter(SharedParameters.Lote.Guid)?.AsString())))
                s.Error("Lote sem número (parâmetro SV_Lote vazio).");
        });
        Step("Loteamento", "Lote personalizado (frente 20 m) e área verde – vizinhos e numeração refeitos", s =>
        {
            if (lot == null || quadras.Count == 0) { s.Error("Sem loteamento da etapa anterior."); return; }
            var q = quadras.OrderByDescending(x => x.Polygon!.Area).First();
            var t0 = q.Testadas[0].Id;
            q.AjusteOuNovo($"{t0}:2").Frente = 20;
            q.AjusteOuNovo($"{t0}:3").Categoria = CategoriaLote.AreaVerde;
            Commit(s, new MarkingDefinition[] { lot, q }, "lote personalizado");
            var mine = MarkingStorage.All(_doc).Where(r => r.Definition.Id == q.Id).ToList();
            var custom = mine.FirstOrDefault(r => LotService.KeyOf(r.Element) == $"{t0}:2");
            if (custom == null) s.Error("Lote personalizado não encontrado no modelo.");
            else s.Note($"Lote personalizado: {UnitConv.M2(custom.Element.get_Parameter(SharedParameters.AreaLote.Guid)?.AsDouble() ?? 0):0.00} m².");
            if (!mine.Any(r => r.Element.get_Parameter(SharedParameters.CategoriaLote.Guid)?.AsString() == CategoriaLote.AreaVerde))
                s.Error("A área verde não aparece no modelo (SV_CategoriaLote).");
            var nums = mine.Where(r => r.Element.get_Parameter(SharedParameters.CategoriaLote.Guid)?.AsString() == CategoriaLote.Lote)
                .Select(r => int.TryParse(r.Element.get_Parameter(SharedParameters.Lote.Guid)?.AsString(), out var n) ? n : -1).OrderBy(n => n).ToList();
            if (!nums.SequenceEqual(Enumerable.Range(1, nums.Count))) s.Error($"Numeração da quadra {q.Nome} com falhas: {string.Join(", ", nums)}.");
        });
        Step("Loteamento", "Unir lotes (dois vizinhos viram um) e quadro de lotes da quadra", s =>
        {
            if (lot == null || quadras.Count == 0) { s.Error("Sem loteamento da etapa anterior."); return; }
            var q = quadras.OrderByDescending(x => x.Polygon!.Area).First();
            var t0 = q.Testadas[0].Id;
            var (k1, k2) = ($"{t0}:4", $"{t0}:5");
            var before = LotDivider.Divide(q, lot);
            var a1 = before.Parcelas.FirstOrDefault(p => p.Chave == k1);
            var a2 = before.Parcelas.FirstOrDefault(p => p.Chave == k2);
            if (a1 == null || a2 == null) { s.Error($"Lotes {k1} e {k2} não existem na quadra {q.Nome}."); return; }
            q.Unioes.Add(new List<string> { k1, k2 });
            Commit(s, new MarkingDefinition[] { lot, q }, "unir lotes");
            var mine = MarkingStorage.All(_doc).Where(r => r.Definition.Id == q.Id).ToList();
            var merged = mine.FirstOrDefault(r => LotService.KeyOf(r.Element) == k1);
            if (merged == null) s.Error("O lote unido não está no modelo.");
            else
            {
                var area = UnitConv.M2(merged.Element.get_Parameter(SharedParameters.AreaLote.Guid)?.AsDouble() ?? 0);
                if (Math.Abs(area - (a1.Area + a2.Area)) > 0.05) s.Error($"Lote unido com {area:0.00} m²; esperado {a1.Area + a2.Area:0.00} m².");
                else s.Note($"Lote unido: {area:0.00} m².");
            }
            if (mine.Any(r => LotService.KeyOf(r.Element) == k2)) s.Error("O segundo lote da união continua no modelo.");
            var nums = mine.Where(r => r.Element.get_Parameter(SharedParameters.CategoriaLote.Guid)?.AsString() == CategoriaLote.Lote)
                .Select(r => int.TryParse(r.Element.get_Parameter(SharedParameters.Lote.Guid)?.AsString(), out var n) ? n : -1).OrderBy(n => n).ToList();
            if (!nums.SequenceEqual(Enumerable.Range(1, nums.Count))) s.Error($"Numeração da quadra {q.Nome} com falhas depois da união: {string.Join(", ", nums)}.");
            var notes = new List<string>();
            var qd = ProjectTableViews.ForOwnView(new QuadroLoteamentoDefinition { LoteamentoId = lot.Id, Conteudo = TipoQuadroLoteamento.Lotes, Quadra = q.Nome });
            View? v = null;
            using (var t = new Transaction(_doc, "SV Autoteste - vista do quadro de lotes"))
            {
                t.Start();
                v = ProjectTableHost.EnsureView(_doc, qd, ProjectTableViews.ViewName(qd), notes);
                t.Commit();
            }
            Commit(s, new MarkingDefinition[] { qd }, "quadro de lotes da quadra");
            var texts = new FilteredElementCollector(_doc, v.Id).OfClass(typeof(TextNote)).Count(e => MarkingStorage.Read(e)?.Definition.Id == qd.Id);
            if (texts == 0) s.Error("O quadro de lotes da quadra não criou textos.");
            foreach (var n in notes) s.Note(n);
        });
        Step("Loteamento", "Nome de via, detalhamento, quadro na Legenda, filtros, masterplan e memorial", s =>
        {
            if (lot == null) { s.Error("Sem loteamento da etapa anterior."); return; }
            var view = _plan ?? _uidoc.ActiveView;
            lot.Vias.Add(new ViaNomeada { Nome = "Rua Autoteste", Eixo = new() { P(0, Y), P(240, Y) } });
            var det = new LoteamentoDetalheDefinition { LoteamentoId = lot.Id };
            DetailHelpers.PrepareOutput(det, view);
            Commit(s, new MarkingDefinition[] { lot, det }, "detalhar lotes");
            var texts = new FilteredElementCollector(_doc, view.Id).OfClass(typeof(TextNote)).Count(e => MarkingStorage.Read(e)?.Definition.Id == det.Id);
            if (texts == 0) s.Error("O detalhamento dos lotes não criou textos.");
            var notes = new List<string>();
            var qd = ProjectTableViews.ForOwnView(new QuadroLoteamentoDefinition { LoteamentoId = lot.Id, Conteudo = TipoQuadroLoteamento.Areas });
            using (var t = new Transaction(_doc, "SV Autoteste - vista do quadro"))
            {
                t.Start();
                ProjectTableHost.EnsureView(_doc, qd, ProjectTableViews.ViewName(qd), notes);
                t.Commit();
            }
            Commit(s, new MarkingDefinition[] { qd }, "quadro de áreas");
            using (var t = new Transaction(_doc, "SV Autoteste - filtros e masterplan"))
            {
                t.Start();
                LotService.ApplyCategoryFilters(_doc, view, lot, null, true, notes);
                if (view is ViewPlan)
                {
                    var mp = LotService.CreateMasterplanView(_doc, view, lot, notes);
                    s.Note($"Masterplan: \"{mp.Name}\".");
                }
                t.Commit();
            }
            foreach (var n in notes) s.Note(n);
            var r = LotReport.Resumo(lot, LotService.Quadras(_doc, lot.Id));
            if (Math.Abs(r.Categorias.Sum(c => c.Percentual) + r.Pct(r.AreaViaria) - 100) > 0.01) s.Error("Os percentuais do quadro de áreas não somam 100 %.");
            if (DocxWriter.Write(LotMemorial.Build(r)).Length < 1000) s.Error("Memorial do loteamento vazio.");
        });
    }
}
