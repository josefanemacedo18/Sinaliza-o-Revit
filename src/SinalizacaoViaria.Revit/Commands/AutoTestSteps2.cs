using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

internal sealed partial class AutoTestRunner
{
    private Toposolid? _topo;

    // ================================================================== topografia

    private void Terrain()
    {
        Step("Topografia", "Toposolid de teste com relevo (encosta, 2 morros e um vale)", s =>
        {
            var typeId = new FilteredElementCollector(_doc).OfClass(typeof(ToposolidType)).FirstElementId();
            if (typeId == ElementId.InvalidElementId || _level == null) { s.Error("O projeto não tem tipo de Toposolid: a parte de topografia não pode ser testada."); return; }
            const double x0 = 1300, x1 = 2300, y0 = -50, y1 = 1000, step = 20;
            var pts = new List<XYZ>();
            for (var x = x0; x <= x1 + 1e-6; x += step)
                for (var y = y0; y <= y1 + 1e-6; y += step)
                {
                    var w = P(x, y);
                    pts.Add(new XYZ(UnitConv.Ft(w.X), UnitConv.Ft(w.Y), UnitConv.Ft(GroundLocal(x, y))));
                }
            var loop = new CurveLoop();
            var c = new[] { P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1) }
                .Select(v => new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), _level.ProjectElevation)).ToList();
            for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(c[i], c[(i + 1) % 4]));
            using var t = new Transaction(_doc, "SV Autoteste - Toposolid");
            t.Start();
            _topo = Toposolid.Create(_doc, new List<CurveLoop> { loop }, pts, typeId, _level.Id);
            t.Commit();
            if (_topo == null || !_topo.IsValidObject) { s.Error("O Toposolid de teste não foi criado."); _topo = null; return; }
            var g = TerrainModel.Ground(_doc);
            var probe = g?.Invoke(P(1700, 300));
            if (probe == null) s.Error("O plugin não consegue ler a cota do Toposolid de teste (TerrainModel.Ground).");
            else if (Math.Abs(probe.Value - GroundLocal(1700, 300)) > 0.5)
                s.Error($"Cota lida do Toposolid ({probe:0.00}) diferente da criada ({GroundLocal(1700, 300):0.00}).");
            s.Note($"{pts.Count} pontos; cotas de {pts.Min(p => UnitConv.M(p.Z)) - _z0:0.0} a {pts.Max(p => UnitConv.M(p.Z)) - _z0:0.0} m acima do nível.");
        });
        if (_topo == null) return;

        Step("Topografia", "Via acompanhando o terreno (flanco do morro)", s =>
            MakeRoad(s, "relevo – acompanha", T(1), 0, true, RelevoVia.AcompanharTerreno, FimLivre.Nenhum, (1320, 250), (1880, 250)));
        Step("Topografia", "Via com greide suavizado", s =>
            MakeRoad(s, "relevo – suavizada", T(0), 0, true, RelevoVia.GreideSuavizado, FimLivre.Nenhum, (1320, 430), (1880, 430)));
        Step("Topografia", "Via transversal no relevo com dois cruzamentos", s =>
        {
            MakeRoad(s, "relevo – transversal", T(0), 0, true, RelevoVia.AcompanharTerreno, FimLivre.Nenhum, (1450, 150), (1450, 520));
            foreach (var y in new[] { 250.0, 430 })
                if (ItNear(P(1450, y), 10) is not { } it) s.Error($"Sem interseção no cruzamento em relevo {L(P(1450, y))}.");
                else s.Note($"Interseção em {L(P(1450, y))}: cota {it.Z - _z0:0.00} m (terreno {Relief(1450, y):0.00} m).");
        });

        if (!_opt.Terrain) return;
        PickedRoad? prof = null;
        Step("Topografia", "Via plana para o Perfil da Via (morro alto e vale)", s =>
            prof = MakeRoad(s, "perfil", T(0), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (1320, 700), (2280, 700)));
        Step("Topografia", "Perfil da Via – cálculo em todos os modos", s =>
        {
            if (prof == null) { s.Error("Sem via para o perfil."); return; }
            var ground = RoadWorks.Ground(_doc, prof.BaseZ);
            var len = prof.Axis.Length;
            foreach (var m in Enum.GetValues<ModoGreide>())
            {
                var o = new PerfilOpcoes { Mode = m, CurveK = m == ModoGreide.RampaConstante ? 20 : 0 };
                if (m == ModoGreide.Manual) o.ManualPvis = $"0; 2\n{len / 2:0}; 12; 120\n{len:0}; 8";
                if (m == ModoGreide.Nivelado) o.LevelZ = 10;
                try
                {
                    var r = RoadProfileDesigner.Design(len, x => ground(prof.Axis.PointAt(x)), o);
                    s.Note($"{m}: {r.Summary().Replace("\n", " ")} | corte {r.CutM2:0} m², aterro {r.FillM2:0} m²");
                    foreach (var w in r.Warnings) s.Warn($"{m}: {w}");
                    if (r.Grade.Points.Count < 2) s.Error($"{m}: greide sem PIVs.");
                    _ = RoadProfileDesigner.Chart(r);
                }
                catch (Exception ex) { s.Error($"{m}: {ex.GetType().Name}: {ex.Message}\n      {Log.Where(ex)}"); }
            }
        });
        Step("Topografia", "Perfil da Via – aplicar (acompanhar terreno + obras automáticas)", s =>
        {
            if (prof == null) return;
            var road = RoadWorks.RoadByGroup(_doc, prof.GroupId)!;
            var rs = CmdPerfilVia.Apply(_uidoc, road, new PerfilOpcoes { Mode = ModoGreide.AcompanharTerreno, AutoStructures = true }, true);
            Take(s, rs);
            var hosted = RoadWorks.Hosted(_doc, prof.GroupId);
            s.Note("Obras criadas: " + (hosted.Count == 0 ? "nenhuma" : string.Join(", ", hosted.Select(h => h.KindName))));
            if (!hosted.OfType<TunnelDefinition>().Any()) s.Warn("O morro de 32 m não virou túnel (esperado com corte > 18 m).");
            if (!hosted.OfType<BridgeDefinition>().Any()) s.Warn("O vale de 16 m não virou viaduto/ponte (esperado com aterro > 8 m).");
        });
    }

    // ================================================================== obras

    private void Works()
    {
        if (_topo != null)
        {
            Step("Obras", "Viaduto num trecho de via existente, sobre outra via", s =>
            {
                MakeRoad(s, "sob o viaduto", T(0), 0, false, RelevoVia.AcompanharTerreno, FimLivre.Nenhum, (2180, 800), (2180, 990));
                var up = MakeRoad(s, "com viaduto", T(1), 0, false, RelevoVia.AcompanharTerreno, FimLivre.Nenhum, (2060, 905), (2290, 905));
                if (up == null) return;
                var b = new BridgeDefinition { Kind = TipoObraDeArte.Viaduto };
                b.ApplyKindDefaults();
                b.Output = InfraRunner.Output3D();
                Take(s, HostedRunner.OnExistingRoad(_uidoc, b, up, 60, 180), new[] { b });
            });
            Step("Obras", "Ponte sobre o vale (via existente)", s =>
            {
                var r = MakeRoad(s, "com ponte", T(0), 0, false, RelevoVia.AcompanharTerreno, FimLivre.Nenhum, (1880, 560), (2120, 560));
                if (r == null) return;
                var b = new BridgeDefinition { Kind = TipoObraDeArte.Ponte };
                b.ApplyKindDefaults();
                b.Output = InfraRunner.Output3D();
                Take(s, HostedRunner.OnExistingRoad(_uidoc, b, r, 60, 180), new[] { b });
            });
            Step("Obras", "Passarela sobre a via do flanco (via nova)", s =>
            {
                var b = new BridgeDefinition { Kind = TipoObraDeArte.Passarela };
                b.ApplyKindDefaults();
                b.Output = InfraRunner.Output3D();
                var rs = HostedRunner.OnNewRoad(_uidoc, b, Lines(0, (1800, 195), (1800, 305)), -1);
                if (rs == null) { s.Error("A via da passarela não foi criada."); return; }
                Take(s, rs, new[] { b });
            });
            Step("Obras", "Túnel no morro (via nova)", s =>
            {
                var tn = new TunnelDefinition { Output = InfraRunner.Output3D() };
                tn.StartZ = Relief(1560, 300) - 0.5;
                tn.EndZ = Relief(1840, 300) - 0.5;
                var rs = HostedRunner.OnNewRoad(_uidoc, tn, Lines(0, (1560, 300), (1840, 300)), -1);
                if (rs == null) { s.Error("A via do túnel não foi criada."); return; }
                Take(s, rs, new[] { tn });
            });
            Step("Obras", "Trincheira num trecho da via suavizada", s =>
            {
                if (!_roads.TryGetValue("relevo – suavizada", out var pav) || RoadWorks.RoadByGroup(_doc, pav.GroupId!) is not { } r) { s.Error("Via suavizada não existe."); return; }
                var tr = new TrenchDefinition { Output = InfraRunner.Output3D() };
                Take(s, HostedRunner.OnExistingRoad(_uidoc, tr, r, 180, 380), new[] { tr });
            });
            Step("Obras", "Muros de arrimo – todos os tipos (com terraplenagem)", s =>
            {
                var defs = Enum.GetValues<TipoMuro>().Select((v, k) => (MarkingDefinition)new RetainingWallDefinition
                {
                    Type = v, Output = InfraRunner.Output3D(), PathRef = Pts((2200, 40 + 45 * k), (2240, 40 + 45 * k)),
                }).ToList();
                Commit(s, defs, "muros");
                TerrainActions.AfterCreate(_uidoc, defs, quiet: true);
            });
            Step("Obras", "Taludes de aterro e de corte", s =>
            {
                var defs = Enum.GetValues<TipoTalude>().Select((v, k) => (MarkingDefinition)new SlopeDefinition
                {
                    Type = v, Output = InfraRunner.Output3D(), PathRef = Pts((2250, 330 + 60 * k), (2290, 330 + 60 * k)),
                }).ToList();
                Commit(s, defs, "taludes");
                TerrainActions.AfterCreate(_uidoc, defs, quiet: true);
            });
            Step("Obras", "Terraplenagem – simulação (só volumes)", s => Grade(s, new GradingOptions { DryRun = true }));
            Step("Obras", "Terraplenagem – aplicar no Toposolid", s => Grade(s, new GradingOptions()));
            Step("Obras", "Terraplenagem – mapa de corte e aterro", s => Grade(s, new GradingOptions { CutFillMap = true }));
        }
        var nodes = _opt.Full ? Enum.GetValues<TipoNoViario>() : new[] { TipoNoViario.Diamante, TipoNoViario.TrevoParcial };
        for (int k = 0; k < nodes.Length; k++)
        {
            var v = nodes[k];
            var y = 300 + 1300.0 * k;
            Step("Obras", $"Nó viário – {v}", s =>
            {
                MakeRoad(s, $"nó {v} principal", T(8), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (2600, y), (3500, y));
                MakeRoad(s, $"nó {v} transversal", T(1), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (3050, y - 450), (3050, y + 450));
                var d = new InterchangeDefinition { Type = v, Output = InfraRunner.Output3D() };
                var rs = InterchangeBuilder.Build(_uidoc, d, P(3050, y), _z0);
                if (rs == null) { s.Error("O nó viário não foi montado."); return; }
                Take(s, rs);
                s.Note($"Obras do nó: {d.WorkIds.Count}; ramos: {d.Ramps.Count}.");
            });
        }
    }

    private void Grade(StepReport s, GradingOptions opt)
    {
        var defs = TestDefs().Where(TerrainService.Grades).ToList();
        if (defs.Count == 0) { s.Error("Nenhum elemento do teste molda o terreno."); return; }
        var r = CmdTerraplenagem.Apply(_uidoc, defs, opt, false);
        if (r == null) { s.Error("O Revit recusou a terraplenagem (ver mensagens)."); return; }
        foreach (var line in r.Text().Split('\n').Where(l => l.Trim().Length > 0)) s.Note(line.Trim());
    }

    // ================================================================== edição

    private StoredMarking? Stored(string id) => MarkingStorage.ById(_doc, id).FirstOrDefault();

    private double Area(MarkingDefinition d)
    {
        try { return new MarkingService(_doc, _uidoc.ActiveView).BuildGeometry(d, out _).TotalArea; }
        catch { return -1; }
    }

    private void Editing()
    {
        Step("Edição", "Editar linha (LFO-2 → LFO-1) na via local", s =>
        {
            var gid = _roads.GetValueOrDefault("modelo 0")?.GroupId;
            var line = MarkingStorage.Definitions(_doc).OfType<LinearMarkingDefinition>().FirstOrDefault(l => l.GroupId == gid && l.Code.StartsWith("LFO"));
            if (line == null) { s.Error("A via local não tem linha de eixo LFO."); return; }
            var a0 = Area(line);
            line.Code = "LFO-1";
            Commit(s, new[] { line }, "editar linha");
            var a1 = Area(line);
            s.Note($"Área pintada: {a0:0.00} → {a1:0.00} m².");
            if (a1 <= a0) s.Error("A linha contínua (LFO-1) não ficou com mais área que a seccionada (LFO-2).");
        });
        Step("Edição", "Editar interseção (raio 12 m)", s =>
        {
            if (ItNear(P(500, 100)) is not { } it) { s.Error("Interseção X1 não existe."); return; }
            it.CornerRadius = 12;
            Take(s, IntersectionRunner.Run(_uidoc, "SV Autoteste - editar interseção", sv => sv.Refresh(it)));
        });
        Step("Edição", "Editar rotatória (mais uma faixa)", s =>
        {
            var rb = MarkingStorage.Definitions(_doc).OfType<RoundaboutDefinition>().FirstOrDefault(r => !_before.Contains(r.Id));
            if (rb == null) { s.Error("Nenhuma rotatória do teste."); return; }
            rb.Lanes = Math.Min(3, rb.Lanes + 1);
            Take(s, IntersectionRunner.Run(_uidoc, "SV Autoteste - editar rotatória", sv => sv.Refresh(rb)));
        });
        Step("Edição", "Mover o eixo de uma via (atualização automática)", s =>
        {
            if (!_roads.TryGetValue("modelo 3", out var pav)) { s.Error("Via do modelo 3 não existe."); return; }
            var els = MarkingStorage.All(_doc).Where(r => r.MarkingId == pav.Id).Select(r => r.Element).ToList();
            double Y() => els.Where(e => e.IsValidObject).Select(e => e.get_BoundingBox(null)).Where(b => b != null).Select(b => UnitConv.M((b!.Min.Y + b.Max.Y) / 2)).DefaultIfEmpty(double.NaN).Average();
            var y0 = Y();
            var lines = pav.PathRef.ElementIds.Select(u => _doc.GetElement(u)).Where(e => e != null).Select(e => e!.Id).ToList();
            if (lines.Count == 0) { s.Error("O eixo da via não é de linhas de modelo."); return; }
            using (var t = new Transaction(_doc, "SV Autoteste - mover eixo"))
            {
                t.Start();
                ElementTransformUtils.MoveElements(_doc, lines, new XYZ(0, UnitConv.Ft(3), 0));
                t.Commit();
            }
            els = MarkingStorage.All(_doc).Where(r => r.MarkingId == pav.Id).Select(r => r.Element).ToList();
            var y1 = Y();
            s.Note($"Centro do pavimento em Y: {y0 - _origin.Y:0.00} → {y1 - _origin.Y:0.00} m.");
            if (double.IsNaN(y1) || Math.Abs(y1 - y0 - 3) > 0.3) s.Error("O pavimento não acompanhou o eixo movido 3 m (atualização automática).");
        });
        Step("Edição", "Apagar Trecho – linha de eixo de uma via (clique numa peça)", s =>
        {
            var gid = _roads.GetValueOrDefault("modelo 1")?.GroupId;
            var line = MarkingStorage.Definitions(_doc).OfType<LinearMarkingDefinition>().FirstOrDefault(l => l.GroupId == gid && l.Code.StartsWith("L") && !l.Code.StartsWith("LBO"));
            if (line == null || Stored(line.Id) is not { } st) { s.Error("A via coletora não tem linha de eixo."); return; }
            TrimAndCheck(s, st, geo => geo.Pieces.Count == 0 ? new List<Polygon2>() :
                new[] { geo.Pieces[geo.Pieces.Count / 2] }.Select(p => TrimTools.ZoneFor(p, p.Shape.Centroid, 3)).Where(z => z != null).Cast<Polygon2>().ToList());
        });
        Step("Edição", "Apagar Trecho – marca fora da via (janela)", s =>
        {
            if (!_made.TryGetValue("LFO-1", out var id) || Stored(id) is not { } st) { s.Error("Linha LFO-1 do catálogo não existe."); return; }
            TrimAndCheck(s, st, geo =>
            {
                var b = geo.Bounds;
                if (b == null) return new List<Polygon2>();
                var mid = (b.Value.Min + b.Value.Max) / 2;
                return TrimTools.ZonesInWindow(geo, mid - new Vec2(3, 3), mid + new Vec2(3, 3));
            });
        });
        Step("Edição", "Apagar Trecho – zebrado (janela)", s =>
        {
            if (!_made.TryGetValue("zebrado", out var id) || Stored(id) is not { } st) { s.Error("Zebrado do catálogo não existe."); return; }
            TrimAndCheck(s, st, geo =>
            {
                var b = geo.Bounds;
                return b == null ? new List<Polygon2>() : TrimTools.ZonesInWindow(geo, b.Value.Min, (b.Value.Min + b.Value.Max) / 2);
            });
        });
        Step("Edição", "Apagar Trecho – marca cuja linha de referência foi apagada", s =>
        {
            var path = Lines(0, (320, 1020), (350, 1020));
            var d = new LinearMarkingDefinition { Code = "LBO", Output = Out(), PathRef = path };
            Commit(s, new[] { d }, "linha sem referência");
            using (var t = new Transaction(_doc, "SV Autoteste - apagar linha de referência"))
            {
                t.Start();
                foreach (var u in path.ElementIds) if (_doc.GetElement(u) is { } e) _doc.Delete(e.Id);
                t.Commit();
            }
            if (Stored(d.Id) is not { } st) { s.Error("A marca sumiu junto com a linha de referência."); return; }
            TrimAndCheck(s, st, geo =>
            {
                var b = geo.Bounds;
                if (b == null) return new List<Polygon2>();
                var mid = (b.Value.Min + b.Value.Max) / 2;
                return new List<Polygon2> { Polygon2.Rectangle(mid - new Vec2(2, 1), mid + new Vec2(2, 1)) };
            });
        });
        Step("Edição", "Alternar 2D/3D (5 linhas)", s =>
        {
            if (_plan == null) { s.Error("Sem vista de planta de teste."); return; }
            var defs = _made.Values.Take(5).Select(id => MarkingStorage.Definitions(_doc).FirstOrDefault(d => d.Id == id)).OfType<MarkingDefinition>().ToList();
            foreach (var d in defs) { d.Output.Mode = OutputMode.Detalhe2D; d.Output.ViewId = _plan.UniqueId; }
            Commit(s, defs, "para 2D");
            var twoD = MarkingStorage.All(_doc).Count(r => defs.Any(d => d.Id == r.MarkingId) && r.Element.OwnerViewId == _plan.Id);
            if (twoD == 0) s.Error("Nenhum elemento 2D criado na planta.");
            foreach (var d in defs) { d.Output.Mode = OutputMode.Modelo3D; d.Output.ViewId = null; }
            Commit(s, defs, "para 3D");
            s.Note($"{twoD} elemento(s) 2D criados e convertidos de volta.");
        });
        Step("Edição", "Excluir marca (e conferir que sai do armazenamento)", s =>
        {
            var d = new SymbolMarkingDefinition { Code = "PEM-F", Position = P(320, 1050), Z = _z0, Output = Out() };
            Commit(s, new[] { d }, "símbolo a excluir");
            using (var t = new Transaction(_doc, "SV Autoteste - excluir"))
            {
                t.Start();
                new MarkingService(_doc, _uidoc.ActiveView).Delete(d.Id);
                t.Commit();
            }
            if (Stored(d.Id) != null) s.Error("A marca excluída continua no modelo.");
        });
        Step("Edição", "Atualizar Todas (marcas do teste)", s =>
        {
            var ids = TestDefs().Select(d => d.Id).ToHashSet();
            Take(s, CmdAtualizarTodas.RefreshAll(_uidoc, ids));
            s.Note($"{ids.Count} marca(s) regeneradas.");
        });
    }

    /// <summary>Apagar Trecho como a ferramenta: peças, áreas escolhidas, gravar; depois confere e devolve as peças.</summary>
    private void TrimAndCheck(StepReport s, StoredMarking st, Func<MarkingGeometry, List<Polygon2>> choose)
    {
        var (geo, fromModel, warnings) = CmdApagarTrecho.Pieces(_uidoc, st);
        foreach (var w in warnings) s.Warn(w);
        if (geo.Pieces.Count == 0) { s.Error("A ferramenta não conseguiu ler as peças da marca."); return; }
        s.Note($"{geo.Pieces.Count} peça(s){(fromModel ? " lidas dos elementos do modelo (sem caminho)" : "")}.");
        var zones = choose(geo);
        if (zones.Count == 0) { s.Error("Nenhuma área a apagar foi obtida da planta das peças."); return; }
        var before = MarkingStorage.ById(_doc, st.MarkingId).Count;
        var a0 = fromModel ? 0 : Area(st.Definition);
        var (rs, direct) = CmdApagarTrecho.Apply(_uidoc, st, fromModel, zones, true);
        Take(s, rs);
        if (fromModel)
        {
            s.Note($"{direct} peça(s) recortadas direto no modelo.");
            if (direct == 0) s.Error("Nenhuma peça foi recortada no modelo.");
            return;
        }
        var def = MarkingStorage.Definitions(_doc).First(d => d.Id == st.MarkingId);
        var a1 = Area(def);
        s.Note($"Área: {a0:0.00} → {a1:0.00} m²; elementos {before} → {MarkingStorage.ById(_doc, st.MarkingId).Count}.");
        if (a1 >= a0 - 1e-3) s.Error("A área da marca não diminuiu depois de apagar o trecho.");
        // Devolver as peças.
        if (Stored(st.MarkingId) is { } st2)
        {
            CmdApagarTrecho.Apply(_uidoc, st2, false, Array.Empty<Polygon2>(), true);
            var a2 = Area(MarkingStorage.Definitions(_doc).First(d => d.Id == st.MarkingId));
            if (Math.Abs(a2 - a0) > 0.01) s.Error($"Devolver as peças não restaurou a marca ({a2:0.00} m² em vez de {a0:0.00}).");
        }
    }

    // ================================================================== detalhamento

    private void Detailing()
    {
        if (_plan == null) return;
        MarkingDefinition Prep(MarkingDefinition d) { DetailHelpers.PrepareOutput(d, _plan); return d; }
        var lineId = _made.GetValueOrDefault("LFO-1") ?? _made.Values.FirstOrDefault();
        Step("Detalhamento", "Detalhar placas (5)", s =>
        {
            var signs = MarkingStorage.Definitions(_doc).OfType<SignDefinition>().Where(x => !_before.Contains(x.Id)).Take(5).ToList();
            if (signs.Count == 0) { s.Warn("Nenhuma placa do teste para detalhar."); return; }
            Commit(s, signs.Select((x, k) => Prep(new SignPlanDetailDefinition { SignId = x.Id, Number = $"P{k + 1:00}" })), "detalhar placas");
        });
        Step("Detalhamento", "Anotar", s =>
        {
            if (lineId == null) { s.Error("Sem marca para anotar."); return; }
            Commit(s, new[] { Prep(new LabelDefinition { MarkingTargetId = lineId, Anchor = P(15, 1000), LabelPosition = P(25, 1010) }) }, "anotar");
        });
        Step("Detalhamento", "Quadro de legenda", s => Commit(s, new[] { Prep(new LegendDefinition { Position = P(-200, 1300) }) }, "legenda"));
        Step("Detalhamento", "Cotar seção + perfil transversal", s =>
        {
            var d = (SectionDimensionDefinition)Prep(new SectionDimensionDefinition { Start = P(60, -14), End = P(60, 14) });
            Commit(s, new[] { d }, "cotar seção");
            Commit(s, new[] { Prep(SectionProfileDefinition.From(d, P(-200, 0))) }, "perfil transversal");
        });
        Step("Detalhamento", "Detalhe típico", s =>
        {
            if (lineId == null) { s.Error("Sem marca para detalhar."); return; }
            Commit(s, new[] { Prep(new TypicalDetailDefinition { MarkingTargetId = lineId, Position = P(-200, 1100) }) }, "detalhe típico");
        });
        Step("Detalhamento", "Quadro de quantitativos", s => Commit(s, new[] { Prep(new QuantityTableDefinition { Position = P(-400, 1300) }) }, "quadro"));
        Step("Detalhamento", "Quadro de placas", s => Commit(s, new[] { Prep(new QuantityTableDefinition { Position = P(-600, 1300), SignsOnly = true }) }, "quadro de placas"));
        Step("Detalhamento", "Legenda e quadro de placas em vista própria, numa folha", s =>
        {
            // Sem depender da planta: vista própria (Legenda, ou desenho se o projeto não tiver Legenda) e folha nova.
            var notes = new List<string>();
            var own = new MarkingDefinition[] { ProjectTableViews.ForOwnView(new LegendDefinition()), ProjectTableViews.ForOwnView(new QuantityTableDefinition { SignsOnly = true }) };
            var views = new List<View>();
            using (var t = new Transaction(_doc, "SV Autoteste - vistas dos quadros"))
            {
                t.Start();
                foreach (var d in own) views.Add(ProjectTableHost.EnsureView(_doc, d, "SV Autoteste - " + d.KindName, notes));
                t.Commit();
            }
            Commit(s, own, "quadros em vista própria");
            using (var t = new Transaction(_doc, "SV Autoteste - quadros na folha"))
            {
                t.Start();
                var sheet = ProjectTableHost.NewSheet(_doc, "SV-AT-Q", "Autoteste – legenda e quadro");
                foreach (var v in views)
                    if (!ProjectTableHost.Place(_doc, v, sheet, null, notes)) s.Error($"\"{v.Name}\" não foi para a folha {sheet.SheetNumber}.");
                t.Commit();
                s.Note($"Folha {sheet.SheetNumber}: " + string.Join(", ", views.Select(v => $"{v.Name} ({v.ViewType})")));
            }
            foreach (var n in notes.Distinct()) s.Warn(n);
        });
        Step("Detalhamento", "Notas gerais", s => Commit(s, new[] { Prep(new NotesDefinition { Position = P(-200, 900) }) }, "notas"));
        Step("Detalhamento", "Norte", s => Commit(s, new[] { Prep(new NorthArrowDefinition { Position = P(-100, 900) }) }, "norte"));
        Step("Detalhamento", "Mostrar/ocultar eixos na planta", s =>
        {
            var sub = new StyleService(_doc).LineSubcategory(StyleService.AxisLineStyleName);
            if (sub == null) { s.Error("Subcategoria de linhas de eixo não existe."); return; }
            using var t = new Transaction(_doc, "SV Autoteste - eixos");
            t.Start();
            _plan.SetCategoryHidden(sub.Id, true);
            _plan.SetCategoryHidden(sub.Id, false);
            t.Commit();
        });
        Step("Detalhamento", "Quantitativos: cálculo e tabelas do Revit", s =>
        {
            var service = new MarkingService(_doc, _uidoc.ActiveView);
            var items = new List<(MarkingDefinition, MarkingGeometry)>();
            foreach (var d in TestDefs())
            {
                try { items.Add((d, service.QuantityGeometry(d))); }
                catch (Exception ex) { s.Error($"{d.DisplayCode}: {ex.Message}"); }
            }
            var rows = Core.Quantities.QuantityCalculator.Compute(items, PluginContext.Catalog, PluginContext.Settings.DefaultMaterial);
            s.Note($"{rows.Count} linha(s) de quantitativo para {items.Count} marca(s).");
            var thumbs = QuantityThumbnails.Build(items);
            var notes = new List<string>();
            using (var t = new Transaction(_doc, "SV Autoteste - preparar quantitativos"))
            {
                t.Start();
                var prep = QuantitySchedule.Prepare(_doc, rows, thumbs);
                t.Commit();
                s.Note($"{prep.Elements} elemento(s) preparados, {prep.Images} imagem(ns).");
            }
            using (var t = new Transaction(_doc, "SV Autoteste - tabela"))
            {
                t.Start();
                var v = QuantitySchedule.Create(_doc, null, notes);
                QuantitySchedule.PlaceOnSheet(_doc, v, notes);
                t.Commit();
                s.Note($"Tabela \"{v.Name}\" com {QuantitySchedule.Rows(v)} linha(s).");
            }
            foreach (var n in notes.Distinct()) s.Warn(n);
        });
    }
}
