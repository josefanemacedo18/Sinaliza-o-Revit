using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Etapas do Autoteste. Layout da área de teste (coordenadas locais, m):
/// A (plano, sem terreno) x 0–1150: vias por modelo (x 0–260), conexões (x 350–1150, y 0–1380), catálogo de sinalização
/// (x 0–300, y 1000–2300), calçadas e travessias (x 350–1150, y 1450–1950); B (Toposolid com relevo) x 1300–2300, y 0–1000;
/// C (plano) x ≥ 2600: nós viários.
/// </summary>
internal sealed partial class AutoTestRunner
{
    private readonly Dictionary<string, string> _roadNames = new();
    private readonly Dictionary<string, RoadPavementDefinition> _roads = new();
    private readonly Dictionary<string, string> _made = new();

    private static RoadSetup T(int i) => RoadTemplates.All[Math.Clamp(i, 0, RoadTemplates.All.Count - 1)].Create();

    /// <summary>Via criada como pela ferramenta Via (eixo em linhas de modelo, conexões, bordo e relevo).</summary>
    private PickedRoad? MakeRoad(StepReport s, string key, RoadSetup setup, double radius, bool connect, RelevoVia relief, FimLivre ends,
        params (double X, double Y)[] pts)
    {
        var path = Lines(Math.Max(radius, radius > 0 ? RoadSetup.MinAxisRadius(setup.MaxHalfWidth) : 0), pts);
        var output = Out();
        var defs = setup.Build(path, output, PluginContext.Catalog);
        if (radius > 0) RoadSetup.ApplyAxisRadius(defs, radius);
        var opt = new CmdSinalizarVia.RoadCreation(connect, TipoConexao.Intersecao, ends, true, null, true, relief, output);
        Take(s, CmdSinalizarVia.Create(_uidoc, defs, opt, setup.Warnings), defs);
        var pav = defs.OfType<RoadPavementDefinition>().FirstOrDefault();
        if (pav?.GroupId == null) { s.Error("A seção não gerou o pavimento da via."); return null; }
        _roadNames[pav.GroupId] = key;
        var road = RoadWorks.RoadByGroup(_doc, pav.GroupId);
        if (road == null) { s.Error("A via não foi encontrada no modelo depois de criada."); return null; }
        _roads[key] = road.Pavement;
        s.Note($"{defs.Count} marca(s) na via; eixo {road.Axis.Length:0} m.");
        return road;
    }

    private IntersectionDefinition? ItNear(Vec2 p, double r = 8) =>
        MarkingStorage.Definitions(_doc).OfType<IntersectionDefinition>().Where(i => i.Node.DistanceTo(p) < r).OrderBy(i => i.Node.DistanceTo(p)).FirstOrDefault();

    private IntersectionService Svc() => new(_doc, new MarkingService(_doc, _uidoc.ActiveView));

    private IntersectionRoad? RoadOf(RoadPavementDefinition pav) => Svc().Roads().FirstOrDefault(r => r.Def.GroupId == pav.GroupId);

    // ================================================================== preparação

    private void Setup()
    {
        Step("Preparação", "Área de teste, nível e vistas", s =>
        {
            _level = new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.ProjectElevation)).FirstOrDefault();
            if (_level == null) { s.Error("O projeto não tem nenhum nível."); return; }
            _z0 = UnitConv.M(_level.ProjectElevation);
            double maxX = double.MinValue, minY = double.MaxValue;
            var n = 0;
            foreach (var e in new FilteredElementCollector(_doc).WhereElementIsNotElementType().WhereElementIsViewIndependent())
            {
                if (e.Category is not { CategoryType: CategoryType.Model } || e is RevitLinkInstance) continue;
                var bic = e.Category.BuiltInCategory;
                if (bic is BuiltInCategory.OST_ProjectBasePoint or BuiltInCategory.OST_SharedBasePoint) continue;
                BoundingBoxXYZ? bb;
                try { bb = e.get_BoundingBox(null); } catch { continue; }
                if (bb == null) continue;
                maxX = Math.Max(maxX, UnitConv.M(bb.Max.X));
                minY = Math.Min(minY, UnitConv.M(bb.Min.Y));
                if (++n > 80000) break;
            }
            _origin = n == 0 ? new Vec2(0, 0) : new Vec2(Math.Ceiling((maxX + 400) / 100) * 100, Math.Floor(minY / 100) * 100);
            if (Math.Abs(_origin.X) > 25000) s.Warn($"A área de teste ficou a {_origin.X / 1000:0.0} km da origem do Revit (modelo muito extenso): a precisão pode cair.");
            s.Note($"{n} elemento(s) de modelo no projeto; área de teste a partir de X = {_origin.X:0} m, Y = {_origin.Y:0} m.");

            using var t = new Transaction(_doc, "SV Autoteste - vistas");
            t.Start();
            var vft = new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan) ?? new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.AreaPlan);
            if (vft != null)
            {
                _plan = ViewPlan.Create(_doc, vft.Id, _level.Id);
                try { _plan.Name = $"SV Autoteste – planta {DateTime.Now:HHmmss}"; } catch { /* nome repetido */ }
                try { _plan.Scale = 500; } catch { /* opcional */ }
            }
            else s.Error("Nenhum tipo de vista de planta no projeto: as ferramentas de detalhamento não serão testadas.");
            var v3 = new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
            if (v3 != null)
            {
                var view3 = View3D.CreateIsometric(_doc, v3.Id);
                try { view3.Name = $"SV Autoteste – 3D {DateTime.Now:HHmmss}"; } catch { /* nome repetido */ }
                var a = P(-50, -150);
                var b = P(4500, 2400);
                view3.SetSectionBox(new BoundingBoxXYZ
                {
                    Min = new XYZ(UnitConv.Ft(a.X), UnitConv.Ft(a.Y), UnitConv.Ft(_z0 - 60)),
                    Max = new XYZ(UnitConv.Ft(b.X), UnitConv.Ft(b.Y), UnitConv.Ft(_z0 + 80)),
                });
            }
            t.Commit();
        });
    }

    /// <summary>Relevo do Toposolid de teste (m acima do nível): encosta, dois morros e um vale.</summary>
    private static double Relief(double x, double y)
    {
        double G(double cx, double cy, double sx, double sy) => Math.Exp(-((x - cx) * (x - cx) / (2 * sx * sx) + (y - cy) * (y - cy) / (2 * sy * sy)));
        var z = 0.012 * (x - 1300);
        z += 16 * G(1700, 300, 55, 70);
        z += 32 * G(1640, 720, 60, 60);
        z -= 16 * Math.Exp(-(x - 2000) * (x - 2000) / (2 * 30.0 * 30.0));
        z += 0.6 * Math.Sin(x / 23.0) * Math.Cos(y / 31.0);
        return z;
    }

    private double GroundLocal(double x, double y) => _z0 + Relief(x, y);

    // ================================================================== vias

    private void Roads()
    {
        for (int i = 0; i < RoadTemplates.All.Count; i++)
        {
            var idx = i;
            Step("Vias", "Via – " + RoadTemplates.All[i].Name, s =>
            {
                var y = 60.0 * idx;
                MakeRoad(s, $"modelo {idx}", T(idx), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (0, y), (240, y));
            });
        }
        Step("Vias", "Via em curva (raio 60 m) e em S", s =>
            MakeRoad(s, "curva", T(1), 60, true, RelevoVia.Plana, FimLivre.Nenhum, (0, 640), (120, 640), (200, 720), (260, 720)));
        Step("Vias", "Pista (só a parte dos veículos) ligada a outra via", s =>
        {
            var d = new RoadPavementDefinition { Hierarchy = HierarquiaViaria.Local, Output = Out() };
            // Pista que termina no eixo da via local (modelo 0): entroncamento automático.
            var path = Lines(0, (120, 0), (120, -120));
            var defs = RoadConnection.BuildCarriageway(d, path, d.Output, "LFO-2", true, Hierarquia.DefaultSpeed(HierarquiaViaria.Local));
            Take(s, MarkingCreator.Commit(_uidoc, defs, "SV Autoteste - Pista"), defs);
            if (defs.OfType<RoadPavementDefinition>().FirstOrDefault() is { } pav)
            {
                Take(s, IntersectionRunner.Run(_uidoc, "SV Autoteste - conexões da pista", sv =>
                    sv.Connect(pav, TipoConexao.Intersecao, FimLivre.Nenhum, IntersectionService.AutoTemplate(true), new RoundaboutDefinition(), new CulDeSacDefinition(), true)));
                if (pav.GroupId != null) _roadNames[pav.GroupId] = "pista";
                if (ItNear(P(120, 0), 10) == null) s.Error("A Pista não se ligou à via local: nenhuma interseção no encontro.");
            }
        });
    }

    // ================================================================== conexões

    private void Connections()
    {
        Step("Conexões", "Interseção em cruz (automática)", s =>
        {
            MakeRoad(s, "X1 leste-oeste", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (380, 100), (620, 100));
            MakeRoad(s, "X1 norte-sul", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (500, -20), (500, 220));
            if (ItNear(P(500, 100)) is not { } it) s.Error("A interseção não foi criada no cruzamento.");
            else s.Note($"Interseção com {it.RoadIds.Count} via(s), raio {it.CornerRadius:0.0} m.");
        });
        Step("Conexões", "Entroncamento em T", s =>
        {
            MakeRoad(s, "T principal", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (680, 100), (920, 100));
            MakeRoad(s, "T ramo", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (800, 100), (800, -20));
            if (ItNear(P(800, 100)) == null) s.Error("O entroncamento em T não criou a interseção.");
        });
        Step("Conexões", "Cruzamento esconso (≈52°)", s =>
        {
            MakeRoad(s, "esconsa 1", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (960, 100), (1150, 100));
            MakeRoad(s, "esconsa 2", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (1000, 30), (1110, 170));
            if (ItNear(P(1055, 100), 12) == null) s.Error("O cruzamento esconso não criou a interseção.");
        });
        Step("Conexões", "Ferramenta Conexão: interseção esconsa → rotatória", s =>
        {
            var rb = new RoundaboutDefinition { Output = Out() };
            rb.ApplyPreset(TipoRotatoria.UmaFaixa);
            var node = ItNear(P(1055, 100), 12)?.Node ?? P(1055, 100);
            Take(s, IntersectionRunner.Run(_uidoc, "SV Autoteste - conexão rotatória", sv => sv.ConvertToRoundabout(node, rb)));
            if (!MarkingStorage.Definitions(_doc).OfType<RoundaboutDefinition>().Any(r => r.Center.DistanceTo(node) < 10)) s.Error("A rotatória não foi criada no nó.");
            if (ItNear(node, 10) != null) s.Error("A interseção antiga continua no nó depois de virar rotatória.");
        });
        Step("Conexões", "Avenida × via local (hierarquias diferentes)", s =>
        {
            MakeRoad(s, "avenida", T(2), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (380, 350), (700, 350));
            MakeRoad(s, "local × avenida", T(0), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (540, 250), (540, 450));
            if (ItNear(P(540, 350), 10) is not { } it) s.Error("Sem interseção entre a avenida e a via local.");
            else s.Note($"Via principal da interseção: {(it.MainRoadId == null ? "não definida" : "definida")}; controle {it.Control}.");
        });
        Step("Conexões", "Coletora × coletora (base das variações)", s =>
        {
            MakeRoad(s, "coletora 1", T(1), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (760, 350), (1000, 350));
            MakeRoad(s, "coletora 2", T(1), 0, true, RelevoVia.Plana, FimLivre.Nenhum, (880, 250), (880, 450));
            if (ItNear(P(880, 350)) == null) s.Error("Sem interseção entre as coletoras.");
        });
        void Variant(string name, Action<IntersectionDefinition> change)
        {
            Step("Conexões", "Interseção – " + name, s =>
            {
                if (ItNear(P(880, 350)) is not { } it) { s.Error("Interseção base não existe."); return; }
                var c = (IntersectionDefinition)MarkingDefinition.FromJson(it.ToJson())!;
                change(c);
                Take(s, IntersectionRunner.Run(_uidoc, "SV Autoteste - interseção " + name, sv => sv.Refresh(c)));
                if (!Present().Contains(c.Id)) s.Error("A interseção sumiu depois de regenerar.");
            });
        }
        foreach (var v in Enum.GetValues<ControleIntersecao>()) Variant($"controle {v}", c => c.Control = v);
        foreach (var v in Enum.GetValues<TipoIlha>().Where(v => v != TipoIlha.Nenhuma))
        {
            Variant($"ilhas divisórias {v}", c => { c.SplitterIslands = v; c.RightTurnIslands = TipoIlha.Nenhuma; });
            Variant($"ilhas de conversão à direita {v}", c => { c.SplitterIslands = TipoIlha.Nenhuma; c.RightTurnIslands = v; });
        }
        Variant("bolsões de conversão à esquerda", c => { c.SplitterIslands = TipoIlha.Nenhuma; c.RightTurnIslands = TipoIlha.Nenhuma; c.LeftTurnPockets = true; });
        Variant("raio pequeno (3 m), sem faixas", c => { c.LeftTurnPockets = false; c.CornerRadius = 3; c.Crosswalks = false; c.Ramps = false; });
        Variant("raio grande (15 m), faixas e rampas", c => { c.CornerRadius = 15; c.Crosswalks = true; c.Ramps = true; });

        var types = Enum.GetValues<TipoRotatoria>();
        for (int k = 0; k < types.Length; k++)
        {
            var v = types[k];
            var cx = 440 + 230 * (k % 3);
            var cy = 650 + 230 * (k / 3);
            Step("Conexões", $"Rotatória – {v}", s =>
            {
                MakeRoad(s, $"rotatória {v} L-O", T(0), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (cx - 100, cy), (cx + 100, cy));
                MakeRoad(s, $"rotatória {v} N-S", T(0), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (cx, cy - 100), (cx, cy + 100));
                var d = new RoundaboutDefinition { Output = Out(), Z = _z0 };
                d.ApplyPreset(v);
                d.Center = P(cx, cy);
                d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, Svc().Roads(), d.OuterRadius + 25);
                if (d.Legs.Count != 4) s.Warn($"Ramos encontrados: {d.Legs.Count} (esperado 4).");
                Take(s, CmdRotatoria.Place(_uidoc, d));
                if (!Present().Contains(d.Id)) s.Error("A rotatória não gerou elementos.");
                s.Note($"Raio externo {d.OuterRadius:0.0} m.");
            });
        }

        var cds = Enum.GetValues<TipoCulDeSac>();
        for (int k = 0; k < cds.Length; k++)
        {
            var v = cds[k];
            var x = 380 + 95 * k;
            Step("Conexões", $"Cul-de-sac – {v}", s =>
            {
                var road = MakeRoad(s, $"cul-de-sac {v}", T(0), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (x, 1250), (x, 1340));
                if (road == null || RoadOf(road.Pavement) is not { } ir) return;
                var d = new CulDeSacDefinition { Type = v, Output = Out() };
                Take(s, IntersectionRunner.Run(_uidoc, "SV Autoteste - cul-de-sac", sv => sv.AddCulDeSac(ir, true, d)));
                if (!MarkingStorage.Definitions(_doc).OfType<CulDeSacDefinition>().Any(c => c.RoadId == ir.Def.Id)) s.Error("O cul-de-sac não foi criado na ponta da via.");
            });
        }
        Step("Conexões", "Via nova com cul-de-sac automático na ponta livre", s =>
        {
            var road = MakeRoad(s, "cul-de-sac automático", T(1), 0, true, RelevoVia.Plana, FimLivre.CulDeSac, (1150, 1250), (1150, 1360));
            if (road != null && !MarkingStorage.Definitions(_doc).OfType<CulDeSacDefinition>().Any(c => c.RoadId == road.Pavement.Id))
                s.Error("A opção \"cul-de-sac nas pontas livres\" não criou o balão.");
        });
    }

    // ================================================================== sinalização horizontal

    private void Horizontal()
    {
        var cat = PluginContext.Catalog;
        var idx = 0;
        foreach (var t in cat.Lineares)
        {
            var type = t;
            Step("Sinalização horizontal", $"Linha {type.Codigo} – {type.Nome}", s =>
            {
                var variants = type.Variantes.Count == 0 ? new List<string?> { null } : type.Variantes.Select(v => (string?)v.Nome).ToList();
                var defs = new List<MarkingDefinition>();
                foreach (var v in variants)
                {
                    var col = idx % 4;
                    var row = idx / 4;
                    idx++;
                    var x = col * 45.0;
                    var y = 1000 + row * 5.0;
                    var len = type.Transversal ? 6.0 : 30.0;
                    defs.Add(new LinearMarkingDefinition { Code = type.Codigo, Variant = v, Output = Out(), PathRef = Pts((x, y), (x + len, y)) });
                }
                Commit(s, defs, "linha " + type.Codigo);
                _made.TryAdd(type.Codigo, defs[0].Id);
                s.Note($"{defs.Count} variante(s).");
            });
        }
        Step("Sinalização horizontal", "Zebrados e marcações de área – catálogo", s =>
        {
            var defs = cat.Hachuras.Select((h, k) =>
            {
                var d = new HatchMarkingDefinition { Code = h.Codigo, Output = Out() };
                d.SetPath(Rect(12 * (k % 8), 1250 + 12 * (k / 8), 9, 9));
                return (MarkingDefinition)d;
            }).ToList();
            Commit(s, defs, "zebrados");
            if (defs.Count > 0) _made["zebrado"] = defs[0].Id;
        });
        Step("Sinalização horizontal", "Zebrado em faixa ao longo de linha e zebrado com furo", s =>
        {
            var strip = new HatchMarkingDefinition { Code = cat.Hachuras.FirstOrDefault()?.Codigo ?? "ZPA", StripWidth = 1.5, Output = Out() };
            strip.SetPath(Pts((110, 1250), (140, 1250), (160, 1265)));
            var ring = new HatchMarkingDefinition { Code = cat.Hachuras.FirstOrDefault()?.Codigo ?? "ZPA", Output = Out() };
            var outer = Rect(180, 1250, 20, 20);
            ring.SetPath(outer);
            Commit(s, new MarkingDefinition[] { strip, ring }, "zebrados especiais");
        });
        Step("Sinalização horizontal", "Setas e símbolos – catálogo", s =>
            Commit(s, cat.Simbolos.Select((y, k) => (MarkingDefinition)new SymbolMarkingDefinition
            {
                Code = y.Codigo, Position = P(8 * (k % 10), 1300 + 10 * (k / 10)), Direction = Vec2.UnitY, Z = _z0, Output = Out(),
            }).ToList(), "símbolos"));
        Step("Sinalização horizontal", "Legendas – catálogo", s =>
            Commit(s, cat.Legendas.Select((l, k) => (MarkingDefinition)new TextMarkingDefinition
            {
                Text = l.Texto, Position = P(10 * (k % 10), 1330 + 14 * (k / 10)), Direction = Vec2.UnitY, Z = _z0, Output = Out(),
            }).ToList(), "legendas"));
        Step("Sinalização horizontal", "Vagas de estacionamento – catálogo", s =>
            Commit(s, cat.Vagas.Select((v, k) => (MarkingDefinition)new ParkingMarkingDefinition
            {
                Code = v.Codigo, Output = Out(), PathRef = Pts((70 * (k % 4), 1390 + 16 * (k / 4)), (70 * (k % 4) + 30, 1390 + 16 * (k / 4))),
            }).ToList(), "vagas"));
        Step("Sinalização horizontal", "Inscrições repetidas (símbolo e texto)", s =>
            Commit(s, new MarkingDefinition[]
            {
                new RepeatedMarkingDefinition { SymbolCode = cat.Simbolos.FirstOrDefault()?.Codigo, Output = Out(), PathRef = Pts((0, 1470), (160, 1470)) },
                new RepeatedMarkingDefinition { Text = "ÔNIBUS", Output = Out(), PathRef = Pts((0, 1480), (160, 1480)) },
            }, "inscrições"));
        Step("Sinalização horizontal", "Área de conflito (MAC) na interseção", s =>
        {
            var d = new HatchMarkingDefinition { Code = "MAC", Output = Out(), Overlay = true };
            d.SetPath(Rect(494, 94, 12, 12));
            Commit(s, new[] { d }, "MAC");
            FootprintCutter.ApplyOverlay(_uidoc, d);
        });
        foreach (var v in Enum.GetValues<TipoCanalizacao>())
            Step("Sinalização horizontal", $"Canalização – {v}", s =>
            {
                var k = (int)v;
                var d = new ChannelizationDefinition { Type = v, Output = Out(), PathRef = Pts((0, 1500 + 20 * k), (220, 1500 + 20 * k)) };
                Commit(s, new[] { d }, "canalização");
                FootprintCutter.ApplyFor(_uidoc, d);
            });
        foreach (var v in Enum.GetValues<TipoCiclo>())
            Step("Sinalização horizontal", $"Ciclovia / faixa – {v}", s =>
            {
                var k = (int)v;
                var b = new BikeLaneSetup { Type = v, Width = BikeLaneSetup.DefaultWidth(v) };
                Commit(s, b.Build(Pts((0, 1580 + 10 * k), (80, 1580 + 10 * k), (120, 1600 + 10 * k)), Out()), "ciclovia");
            });

        // Travessias sobre uma via de verdade (recortam as linhas sob elas).
        PickedRoad? ped = null;
        Step("Sinalização horizontal", "Via das travessias (modelo local)", s => ped = MakeRoad(s, "travessias", T(0), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (700, 1520), (1100, 1520)));
        var codes = cat.Lineares.Where(t => t.Codigo.StartsWith("FTP", StringComparison.OrdinalIgnoreCase) || t.Codigo == "MCC").Select(t => t.Codigo).ToList();
        for (int k = 0; k < codes.Count; k++)
        {
            var code = codes[k];
            var x = 720 + 30 * k;
            Step("Sinalização horizontal", $"Faixa de pedestres {code} (sobre a via)", s =>
            {
                var cs = new CrosswalkSetup { CrosswalkCode = code };
                var defs = UI.CrosswalkWindow.Build(cat, cs, Out(), P(x, 1520 - 5.2), P(x, 1520 + 5.2), _z0);
                Commit(s, defs, "faixa de pedestres");
                foreach (var d in defs.Where(d => d.Overlay)) FootprintCutter.ApplyOverlay(_uidoc, d);
            });
        }
        Step("Sinalização horizontal", "Setas sobre a via (pintura sobre o piso)", s =>
            Commit(s, new[] { 850.0, 900, 950 }.Select(x => (MarkingDefinition)new SymbolMarkingDefinition
            {
                Code = "PEM-F", Position = P(x, 1520 - 1.5), Direction = Vec2.UnitX, Z = _z0, Output = Out(), GroupId = ped?.GroupId,
            }).ToList(), "setas na via"));
        Step("Sinalização horizontal", "Cruzamento rodoferroviário", s =>
        {
            var road = MakeRoad(s, "ferroviária", T(0), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (350, 1600), (650, 1600));
            if (road == null) return;
            var rc = new RailCrossingSetup
            {
                Station = road.Axis.Length / 2, RightWidth = road.Pavement.RightWidth, LeftWidth = road.Pavement.LeftWidth, TwoWay = road.Pavement.TwoWay,
            };
            var defs = rc.Build(road.Pavement.PathRef, road.Axis, road.Pavement.PathRef.Z, Out());
            Commit(s, defs, "cruzamento rodoferroviário");
            foreach (var d in defs.Where(d => d.Overlay)) FootprintCutter.ApplyOverlay(_uidoc, d);
        });
    }

    // ================================================================== vertical

    private void Vertical()
    {
        var cat = PluginContext.Catalog;
        Step("Sinalização vertical", "Dispositivos físicos – catálogo", s =>
            Commit(s, cat.Dispositivos.Select((d, k) => (MarkingDefinition)new DeviceMarkingDefinition
            {
                Code = d.Codigo, Output = Out(), PathRef = Pts((30 * (k % 8), 1700 + 8 * (k / 8)), (30 * (k % 8) + 20, 1700 + 8 * (k / 8))),
            }).ToList(), "dispositivos"));
        var signs = _opt.Full ? cat.Placas.ToList() : cat.Placas.Where((_, k) => k % 8 == 0).ToList();
        Step("Sinalização vertical", $"Placas – {(_opt.Full ? "catálogo completo" : "amostra")} ({signs.Count})", s =>
        {
            var defs = signs.Select((p, k) => (MarkingDefinition)new SignDefinition
            {
                Code = p.Codigo, Position = P(5 * (k % 20), 1740 + 5 * (k / 20)), Direction = Vec2.UnitY, Z = _z0, Output = Out(),
            }).ToList();
            foreach (var chunk in defs.Chunk(40)) Commit(s, chunk, "placas");
            s.Note($"{defs.Count} placa(s).");
        });
        Step("Sinalização vertical", "Placas – tipos de suporte", s =>
            Commit(s, Enum.GetValues<TipoSuporte>().Select((v, k) => (MarkingDefinition)new SignDefinition
            {
                Code = "R-1", Support = v, Position = P(120 + 20 * k, 1740), Direction = Vec2.UnitY, Z = _z0, Output = Out(),
            }).ToList(), "suportes"));
        Step("Sinalização vertical", "Mobiliário urbano – catálogo (por ponto)", s =>
            Commit(s, cat.Mobiliario.Select((m, k) => (MarkingDefinition)new UrbanElementDefinition
            {
                Code = m.Codigo, UsePath = false, Position = P(8 * (k % 10), 1880 + 8 * (k / 10)), Direction = Vec2.UnitY, Z = _z0, Output = Out(),
            }).ToList(), "mobiliário"));
        Step("Sinalização vertical", "Mobiliário urbano ao longo de linha", s =>
        {
            var d = new UrbanElementDefinition { Code = cat.Mobiliario.FirstOrDefault()?.Codigo ?? "BANCO", Output = Out() };
            d.SetPath(Pts((0, 1920), (60, 1920)));
            Commit(s, new[] { d }, "mobiliário em linha");
        });
        Step("Sinalização vertical", "Famílias do Revit classificadas como elementos urbanos", s =>
            s.Note($"{FamilyClassifier.Collect(_doc).Count} família(s) classificada(s) no projeto."));
    }

    // ================================================================== calçadas

    private void Sidewalks()
    {
        const double Y0 = 1420;
        PickedRoad? cal = null, mod = null;
        Step("Calçadas", "Via das calçadas (modelo local)", s => cal = MakeRoad(s, "calçadas", T(0), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (350, Y0), (650, Y0)));
        Step("Calçadas", "Via da moderação (modelo local)", s => mod = MakeRoad(s, "moderação", T(0), 0, false, RelevoVia.Plana, FimLivre.Nenhum, (700, Y0), (1100, Y0)));
        var face = 5.2;   // modelo local: faixa 3,00 + estacionamento 2,20 até a face do meio-fio
        foreach (var v in Enum.GetValues<TipoRampa>())
            Step("Calçadas", $"Rampa – {v}", s =>
            {
                var x = 370 + 25 * (int)v;
                var scanner = new CurbScanner(_doc, _uidoc.ActiveView);
                var pt = P(x, Y0 - face + 0.3);
                var hit = CurbFinder.Nearest(scanner.Faces(pt), pt);
                if (hit == null) { s.Error($"Nenhuma face de meio-fio encontrada junto a {L(pt)}."); return; }
                var d = new RampDefinition { Type = v, Output = Out() };
                d.SetPath(PathReference.FromPoints(new[] { hit.Position, hit.Position + new Vec2(0, -1.2) }, _z0));
                Commit(s, new[] { d }, "rampa");
                s.Note($"Calçada/meio-fio recortados em {RampCutter.Apply(_uidoc, d)} elemento(s).");
            });
        Step("Calçadas", "Orelha (avanço de calçada sobre o estacionamento)", s =>
        {
            var d = new CurbExtensionDefinition { Output = Out(), PathRef = Pts((500, Y0 - face), (485, Y0 - face)) };
            Commit(s, new[] { d }, "orelha");
            FootprintCutter.ApplyFor(_uidoc, d);
        });
        foreach (var v in Enum.GetValues<TipoAreaCalcada>())
            Step("Calçadas", $"Área de calçada – {v}", s =>
            {
                var d = new SidewalkAreaDefinition { Type = v, Output = Out() };
                d.SetPath(Rect(350 + 14 * (int)v, 1660, 10, 8));
                Commit(s, new[] { d }, "área de calçada");
                FootprintCutter.ApplyFor(_uidoc, d);
            });
        foreach (var v in Enum.GetValues<TipoCanteiroCalcada>())
            Step("Calçadas", $"Canteiro na calçada – {v}", s =>
            {
                var x = 540 + 12 * (int)v;
                var d = new PlanterDefinition { Type = v, Output = Out(), PathRef = Pts((x, Y0 - face - 1.0), (x + 2, Y0 - face - 1.0)) };
                Commit(s, new[] { d }, "canteiro");
                FootprintCutter.ApplyFor(_uidoc, d);
            });
        foreach (var v in Enum.GetValues<TipoModeracao>())
            Step("Calçadas", $"Moderação de tráfego – {v}", s =>
            {
                var x = 740 + 60 * (int)v;
                var d = new TrafficCalmingDefinition { Type = v, Output = Out(), PathRef = Pts((x, Y0 - face), (x, Y0 + face)) };
                Commit(s, new[] { d }, "moderação");
                FootprintCutter.ApplyFor(_uidoc, d);
            });
        Step("Calçadas", "Piso tátil (rota com mudança de direção)", s =>
            Commit(s, new[] { new TactileRouteDefinition { Output = Out(), PathRef = Pts((600, Y0 - face - 1.5), (630, Y0 - face - 1.5), (630, Y0 - face - 0.4)) } }, "piso tátil"));
        foreach (var v in Enum.GetValues<TipoViaFerrea>())
            Step("Calçadas", $"Via férrea – {v}", s =>
                Commit(s, new[] { new RailwayDefinition { Type = v, Output = Out(), PathRef = Pts((700, 1680 + 20 * (int)v), (780, 1680 + 20 * (int)v)) } }, "via férrea"));
        foreach (var v in Enum.GetValues<TipoDrenagem>())
            Step("Calçadas", $"Drenagem – {v}", s =>
            {
                var t = new DrainageDefinition { Type = v };
                t.ApplyDefaults();
                t.Output = InfraRunner.Output3D();
                var x = 380 + 30 * (int)v;
                if (t.IsLinear)
                {
                    var d = (DrainageDefinition)t.CloneWithNewId();
                    d.SetPath(Pts((x, Y0 + face - 0.3), (x + 12, Y0 + face - 0.3)));
                    Commit(s, new[] { d }, "canaleta");
                    FootprintCutter.ApplyFor(_uidoc, d);
                    return;
                }
                var scanner = new CurbScanner(_doc, _uidoc.ActiveView);
                var batch = DrainageCommand.Fitted(t, P(x, Y0 + face - 0.3), _z0, scanner, null);
                if (batch == null) { s.Error("Nenhum meio-fio encontrado junto ao ponto (o encaixe na guia falhou)."); return; }
                Take(s, DrainageCommand.Place(_uidoc, batch), batch);
            });
        Step("Calçadas", "Drenagem – série de grelhas ao longo do meio-fio", s =>
        {
            var t = new DrainageDefinition { Type = TipoDrenagem.GrelhaSarjeta };
            t.ApplyDefaults();
            t.Output = InfraRunner.Output3D();
            var batch = DrainageCommand.Fitted(t, P(420, Y0 - face + 0.3), _z0, new CurbScanner(_doc, _uidoc.ActiveView), P(470, Y0 - face + 0.3));
            if (batch == null) { s.Error("Nenhum meio-fio encontrado para a série."); return; }
            Take(s, DrainageCommand.Place(_uidoc, batch), batch);
            s.Note($"{batch.Count} grelha(s) na série.");
        });
        _ = cal; _ = mod;
    }
}
