using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

internal sealed partial class AutoTestRunner
{
    // ================================================================== rodada T: largura variável, recuos, calçadas, segurança

    private void RoundT()
    {
        const double Y = 2700;
        Step("Largura e recuos", "Via com largura variável (10,00 → 10,50 m entre meios-fios) e estreitamento do lote", s =>
        {
            var setup = T(1);
            setup.LargurasVariaveis.Add(new PontoLargura { Estaca = 0 });
            setup.LargurasVariaveis.Add(new PontoLargura { Estaca = 60, BordoDireito = setup.Nominal(false).Bordo });
            setup.LargurasVariaveis.Add(new PontoLargura { Estaca = 120, BordoDireito = setup.Nominal(false).Bordo + 0.50, AlinhamentoDireito = setup.Nominal(false).Alinhamento + 0.50 });
            setup.LargurasVariaveis.Add(new PontoLargura { Estaca = 200, BordoEsquerdo = setup.Nominal(true).Bordo, AlinhamentoEsquerdo = setup.Nominal(true).Alinhamento - 0.80 });
            var road = MakeRoad(s, "largura variável", setup, 0, false, RelevoVia.Plana, FimLivre.Nenhum, (0, Y), (260, Y));
            if (road?.Pavement is { } pav && !pav.HasEdgeVariation) s.Error("O pavimento não recebeu a variação de largura.");
        });
        Step("Largura e recuos", "Baia de ônibus, faixa de desaceleração e de aceleração", s =>
        {
            var setup = T(2);
            setup.Recuos.Add(RecuoVia.Padrao(TipoRecuo.BaiaOnibus, setup.Speed, 3.5));
            setup.Recuos[^1].Estaca = 40;
            setup.Recuos.Add(RecuoVia.Padrao(TipoRecuo.FaixaDesaceleracao, setup.Speed, 3.5));
            setup.Recuos[^1].Estaca = 140;
            setup.Recuos.Add(RecuoVia.Padrao(TipoRecuo.FaixaAceleracao, setup.Speed, 3.5));
            setup.Recuos[^1].Estaca = 260;
            setup.Recuos[^1].LadoEsquerdo = true;
            MakeRoad(s, "recuos", setup, 0, false, RelevoVia.Plana, FimLivre.Nenhum, (320, Y), (700, Y));
            var recs = TestDefs().OfType<RecessMarkingDefinition>().Count();
            if (recs < 3) s.Error($"Esperadas 3 marcas de recuo; criadas {recs}.");
        });
        Step("Largura e recuos", "Calçada com inclinação transversal e níveis do alinhamento (edificações antigas mais altas)", s =>
        {
            var setup = T(0);
            foreach (var e in setup.Right.Concat(setup.Left).Where(e => e.Tipo == TipoElementoSecao.Calcada))
            {
                e.InclinacaoTransversal = 2.0;
                e.NiveisAlinhamento = new List<NivelAlinhamento> { new() { Estaca = 0, Nivel = 0 }, new() { Estaca = 60, Nivel = 0.35 }, new() { Estaca = 120, Nivel = 0.10 } };
            }
            MakeRoad(s, "níveis da calçada", setup, 0, false, RelevoVia.Plana, FimLivre.Nenhum, (0, Y + 120), (200, Y + 120));
            if (!TestDefs().OfType<LinearMarkingDefinition>().Any(l => l.LevelProfile.Count > 0))
                s.Error("Nenhuma faixa da calçada recebeu o perfil de níveis.");
        });
        Step("Largura e recuos", "Arterial × coletora com linha de continuidade (LCO) na interseção", s =>
        {
            MakeRoad(s, "LCO arterial", T(2), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (320, Y + 160), (620, Y + 160));
            MakeRoad(s, "LCO coletora", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (470, Y + 160), (470, Y + 300));
            var it = ItNear(P(470, Y + 160), 12);
            if (it == null) s.Error("A interseção não foi criada.");
            else s.Note($"Interseção criada; linha de continuidade: {it.ContinuityLine}, via principal {it.MainRoadId?[..6]}…");
        });
        Step("Largura e recuos", "Via nova emendada na ponta de outra em ângulo (emenda concordada)", s =>
        {
            MakeRoad(s, "emenda A", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (700, Y + 120), (700, Y + 40));
            MakeRoad(s, "emenda B", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (700, Y + 40), (760, Y - 20));
            var it = ItNear(P(700, Y + 40), 6);
            if (it == null) s.Error("A emenda das duas vias pela ponta não foi criada.");
            else if (TestDefs().Any(d => d.GroupId == it.Id && d is SignDefinition or RampDefinition))
                s.Error("A emenda gerou placas/rampas de cruzamento (deveria ser só a curva com as linhas contínuas).");
            else s.Note($"Emenda criada com {TestDefs().Count(d => d.GroupId == it.Id)} linha(s) contínua(s) na curva.");
        });
        Step("Largura e recuos", "Várias linhas selecionadas viram várias vias (malha com T e cruz)", s =>
        {
            var segs = new[] { ((900.0, Y), (1000.0, Y)), ((1000.0, Y), (1100.0, Y)), ((1000.0, Y - 80), (1000.0, Y)), ((1000.0, Y), (1000.0, Y + 80)), ((1050.0, Y), (1050.0, Y + 60)) };
            var ids = new List<ElementId>();
            using (var t = new Transaction(_doc, "SV Autoteste - malha de linhas"))
            {
                t.Start();
                foreach (var (a0, b0) in segs)
                    ids.AddRange(Picking.CreateAxis(_doc, _plan ?? _uidoc.ActiveView, new[] { new RoadConnection.Piece(P(a0.Item1, a0.Item2), P(b0.Item1, b0.Item2), null) }, UnitConv.Ft(_z0)));
                t.Commit();
            }
            var pts = ids.Select(id => (IReadOnlyList<Vec2>)((CurveElement)_doc.GetElement(id)).GeometryCurve.Tessellate().Select(UnitConv.ToVec2).ToList()).ToList();
            var groups = RoadChains.Split(pts, 0.05);
            if (groups.Count != 3) s.Error($"Esperadas 3 vias (avenida, transversal, T); o traçado virou {groups.Count}.");
            var opt = new CmdSinalizarVia.RoadCreation(true, TipoConexao.Intersecao, FimLivre.Nenhum, true, null, true, RelevoVia.Plana, Out());
            foreach (var g in groups)
            {
                var path = PathReference.FromElements(g.Select(i => _doc.GetElement(ids[i]).UniqueId));
                var setup = T(0);
                var defs = setup.Build(path, Out(), PluginContext.Catalog);
                Take(s, CmdSinalizarVia.Create(_uidoc, defs, opt, setup.Warnings), defs);
            }
            var cross = ItNear(P(1000, Y), 8);
            var tee = ItNear(P(1050, Y), 8);
            if (cross == null) s.Error("Sem interseção no cruzamento da malha.");
            if (tee == null) s.Error("Sem interseção no T da malha.");
        });
        Step("Largura e recuos", "Recuo (baia) acrescentado numa via já criada", s =>
        {
            var pav = TestDefs().OfType<RoadPavementDefinition>().FirstOrDefault(p => p.GroupId != null && _roadNames.GetValueOrDefault(p.GroupId) == "níveis da calçada");
            if (pav == null) { s.Error("Via de teste não encontrada."); return; }
            var setup = RoadTemplates.FromJson(pav.SetupJson);
            if (setup == null) { s.Error("A via não guardou a seção."); return; }
            var r = RecuoVia.Padrao(TipoRecuo.BaiaOnibus, setup.Speed, 3.3);
            r.Estaca = 60;
            r.Profundidade = 2.0;
            setup.Recuos.Add(r);
            var members = MarkingStorage.Definitions(_doc).Where(d => d.GroupId == pav.GroupId).ToList();
            Take(s, RoadRegen.Regenerate(_uidoc, pav, members, setup, pav.Output.Clone(), PathResolver.Resolve(_doc, pav.PathRef)?.Main));
            if (!MarkingStorage.Definitions(_doc).OfType<RecessMarkingDefinition>().Any(x => x.GroupId == pav.GroupId))
                s.Error("A baia não foi criada na via existente.");
        });
        Step("Largura e recuos", "Via reconhecida em pisos comuns do Revit (T feito com dois pisos, pista já modelada)", s =>
        {
            var level = _level;
            if (level == null) { s.Error("Sem nível."); return; }
            var floors = new List<Element>();
            using (var t = new Transaction(_doc, "SV Autoteste - pisos comuns"))
            {
                t.Start();
                var type = Floor.GetDefaultFloorType(_doc, false);
                CurveLoop Rect(double x0, double y0, double x1, double y1)
                {
                    XYZ Q(double x, double y) => UnitConv.ToXyz(P(x, y), _z0);
                    var l = new CurveLoop();
                    l.Append(Line.CreateBound(Q(x0, y0), Q(x1, y0)));
                    l.Append(Line.CreateBound(Q(x1, y0), Q(x1, y1)));
                    l.Append(Line.CreateBound(Q(x1, y1), Q(x0, y1)));
                    l.Append(Line.CreateBound(Q(x0, y1), Q(x0, y0)));
                    return l;
                }
                floors.Add(Floor.Create(_doc, new List<CurveLoop> { Rect(1200, Y - 3.5, 1400, Y + 3.5) }, type, level.Id));
                floors.Add(Floor.Create(_doc, new List<CurveLoop> { Rect(1297, Y + 3, 1303, Y + 90) }, type, level.Id));
                t.Commit();
            }
            var warnings = new List<string>();
            var found = FloorRoadInput.FromElements(_uidoc, floors, false, warnings);
            foreach (var w in warnings) s.Note(w);
            if (found == null || found.Count != 2) { s.Error($"Esperadas 2 vias reconhecidas; foram {found?.Count ?? 0}."); return; }
            var opt = new CmdSinalizarVia.RoadCreation(true, TipoConexao.Intersecao, FimLivre.Nenhum, true, null, true, RelevoVia.Plana, Out());
            foreach (var (path, road) in found)
            {
                var setup = FloorRoads.Fit(T(0), road);
                setup.Pavement = TipoPavimento.Nenhum;
                var defs = setup.Build(path, Out(), PluginContext.Catalog);
                Take(s, CmdSinalizarVia.Create(_uidoc, defs, opt, setup.Warnings), defs);
                s.Note($"Via de {road.Axis.Length:0} m com pista de {road.Width:0.00} m (seção ajustada: {setup.CarriagewayWidth:0.00} m).");
            }
            if (ItNear(P(1300, Y), 8) == null) s.Warn("Os dois eixos reconhecidos não formaram a interseção em T.");
        });
        foreach (var v in Enum.GetValues<TipoSonorizador>())
            Step("Segurança viária", $"Sonorizador longitudinal – {v}", s =>
            {
                var d = new RumbleStripDefinition { Output = Out(), Position = PosicaoSonorizador.Livre, SegmentLength = 12, GapLength = 3.6 };
                d.ApplyPreset(v);
                var y = Y + 360 + 6 * (int)v;
                d.PathRef = Pts((0, y), (80, y));
                Commit(s, new[] { d }, "sonorizador");
            });
        Step("Segurança viária", "Área de escape (caixa de retenção, faixa de serviço, berma, sinalização)", s =>
        {
            var o = InfraRunner.Output3D();
            var d = new EscapeRampDefinition { Output = o, Length = 150, EntrySpeed = 110, Grade = 5 };
            d.PathRef = Pts((120, Y + 380), (290, Y + 380));
            Commit(s, new[] { d }, "área de escape");
            s.Note($"Comprimento necessário {d.RequiredLength:0} m; caixa {d.ActualLength:0} m.");
        });
    }
}
