using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

internal static class IntersectionForms
{
    /// <summary>Via disponível para ser a preferencial (edição).</summary>
    public sealed record RoadOption(string Id, string Label);

    /// <summary>Cena real para a pré-visualização da edição (vias da interseção, já resolvidas).</summary>
    public sealed record RealScene(List<IntersectionRoad> Roads, List<List<MarkingDefinition>> Groups, Dictionary<string, Core.Geometry.Polyline2> Paths);

    public static FormWindow Intersection(IntersectionDefinition d, bool edit, IReadOnlyList<RoadOption>? roads = null, RealScene? real = null)
    {
        var example = 0;
        var exampleRoad = 1;
        var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
        FormPreview? Preview()
        {
            var c = (IntersectionDefinition)MarkingDefinition.FromJson(d.ToJson())!;
            IntersectionDemo.Scene scene;
            string info;
            if (real != null)
            {
                // Cópias das marcas das vias (os recortes da prévia não alteram o projeto).
                var groups = real.Groups.Select(g => g.Select(m =>
                {
                    var k = MarkingDefinition.FromJson(m.ToJson())!;
                    k.Exclusions.RemoveAll(e => e.SourceId != null && (e.SourceId == c.Id || c.ChildIds.Contains(e.SourceId)));
                    return k;
                }).ToList()).ToList();
                var byId = groups.SelectMany(g => g).ToDictionary(m => m.Id);
                var roadsCopy = real.Roads.Select(r => r with { Def = byId.GetValueOrDefault(r.Def.Id) as RoadPavementDefinition ?? r.Def }).ToList();
                scene = IntersectionDemo.Create(c, roadsCopy, groups, real.Paths);
                info = "Pré-visualização com as vias do projeto.";
            }
            else
            {
                var (ang, tee) = example switch { 1 => (90.0, true), 2 => (60.0, false), 3 => (45.0, true), _ => (90.0, false) };
                var main = exampleRoad switch { 0 => 0, 2 => 2, _ => 1 };
                scene = IntersectionDemo.Create(c, PluginContext.Catalog, main, 0, ang, tee);
                info = "Exemplo ilustrativo – a interseção se adapta às vias reais do projeto.";
            }
            var geo = IntersectionDemo.Build(scene, ctx, signs: false);
            var R = Math.Max(45, scene.Layout.Radius + 20);
            if (scene.Layout.Features.Count > 0) R = Math.Max(R, scene.Layout.Features.Max(f => f.TEnd) + 5);
            var node = scene.Layout.Node;
            geo.Pieces.RemoveAll(p => p.Shape.Centroid.DistanceTo(node) > R);
            return new FormPreview(geo, null, null, info, ViewScale: 250);
        }

        var w = new FormWindow(edit ? "Editar interseção" : "Interseção", "Interseção de vias",
                "Ajusta cruzamentos e entroncamentos (T, Y, oblíquos, com qualquer número de ramos) das vias do \"Sinalizar via\": " +
                "pavimento contínuo, esquinas com raio na face do meio-fio, calçadas e canteiros refeitos, sinalização interrompida e vagas " +
                "removidas a 5 m da esquina. Combine os tipos: I – sem refúgio; II – ilha gota na secundária; III – faixa de conversão " +
                "livre à direita com ilhas; IV – bolsão de conversão à esquerda na principal.",
                d, Preview, false, edit ? "Aplicar" : "Criar", 1120, 760)
            .Number("Raio das esquinas (m)", () => d.CornerRadius, v => d.CornerRadius = v, 0, 40, tooltip: "Na face do meio-fio. Vias locais 5–6 m; coletoras/arteriais 8–12 m; veículos pesados até 15 m.");

        w.Section("Controle e sinalização");
        w.Choice("Controle", new[]
            {
                ("PARE na via secundária (R-1 + legenda + retenção)", ControleIntersecao.Pare),
                ("Dê a preferência na via secundária (R-2 + LDP)", ControleIntersecao.DePreferencia),
                ("Semáforo (retenção em todas as aproximações)", ControleIntersecao.Semaforo),
                ("Sem controle (somente travessias)", ControleIntersecao.Nenhum),
            }, () => d.Control, v => d.Control = v, tooltip: "A via principal (preferencial) não recebe retenção com PARE / Dê a preferência.");
        if (roads is { Count: > 1 })
            w.Choice("Via principal", new[] { ("Automática (a que atravessa o nó, mais larga)", (string?)null) }
                    .Concat(roads.Select(r => (r.Label, (string?)r.Id))), () => d.MainRoadId, v => d.MainRoadId = v);
        w.Check("Placas R-1/R-2 e legenda PARE / símbolo de dê a preferência", () => d.Signs, v => d.Signs = v)
         .Check("Linhas de retenção / dê a preferência", () => d.StopLines, v => d.StopLines = v)
         .Check("Faixas de pedestres em cada ramo", () => d.Crosswalks, v => d.Crosswalks = v)
         .Number("Largura da faixa de pedestres (m)", () => d.CrosswalkWidth, v => d.CrosswalkWidth = v, 3, 10)
         .Number("Recuo da faixa em relação à esquina (m)", () => d.CrosswalkSetback, v => d.CrosswalkSetback = v, 0, 20)
         .Check("Rebaixamentos de calçada nas travessias (NBR 9050)", () => d.Ramps, v => d.Ramps = v);

        var ilhas = new[] { ("Nenhuma", TipoIlha.Nenhuma), ("Física (meio-fio)", TipoIlha.Fisica), ("Pintada (zebrado)", TipoIlha.Pintada) };
        w.Section("Tipo II – ilha separadora (gota) na via secundária", "A pista é alargada em volta da ilha; a travessia passa por um refúgio no nível da pista.")
         .Choice("Ilha gota", ilhas, () => d.SplitterIslands, v => d.SplitterIslands = v)
         .Number("Comprimento da ilha (m)", () => d.SplitterLength, v => d.SplitterLength = v, 6, 60)
         .Number("Largura da ilha (m)", () => d.SplitterWidth, v => d.SplitterWidth = v, 1, 8, tooltip: "Refúgio de pedestres: mínimo 1,20 m (NBR 9050); recomendado 2,00 m.");
        w.Section("Tipo III – faixa de conversão livre à direita (ilhas nas esquinas)", "Curva de raio maior com ilha triangular separando a conversão do cruzamento.")
         .Choice("Ilhas de canalização", ilhas, () => d.RightTurnIslands, v => d.RightTurnIslands = v)
         .Choice("Esquinas", new[] { ("Todas", EsquinasCanalizadas.Todas), ("Só ângulos agudos (< 75°)", EsquinasCanalizadas.Agudas), ("Só ângulos obtusos (> 105°)", EsquinasCanalizadas.Obtusas) },
             () => d.RightTurnCorners, v => d.RightTurnCorners = v)
         .Number("Raio da faixa de conversão (m)", () => d.RightTurnRadius, v => d.RightTurnRadius = v, 8, 80)
         .Number("Largura da faixa de conversão (m)", () => d.RightTurnLaneWidth, v => d.RightTurnLaneWidth = v, 3.5, 8);
        w.Section("Tipo IV – bolsão de conversão à esquerda na via principal", "Recortado do canteiro central (≥ 3 m) ou, sem canteiro, com alargamento da pista e zebrado amarelo.")
         .Check("Bolsão de conversão à esquerda", () => d.LeftTurnPockets, v => d.LeftTurnPockets = v)
         .Number("Comprimento de armazenamento (m)", () => d.PocketLength, v => d.PocketLength = v, 5, 120)
         .Number("Comprimento do teiper (m)", () => d.PocketTaper, v => d.PocketTaper = v, 5, 80)
         .Number("Largura do bolsão (m)", () => d.PocketWidth, v => d.PocketWidth = v, 2.5, 5);
        if (real == null)
            w.Section("Pré-visualização")
             .Choice("Exemplo", new[] { ("Cruzamento ortogonal", 0), ("Entroncamento em T", 1), ("Cruzamento oblíquo (60°)", 2), ("Entroncamento em Y (45°)", 3) },
                 () => example, v => example = v)
             .Choice("Via principal do exemplo", new[] { ("Via local", 0), ("Via coletora (2 + 2 faixas)", 1), ("Avenida com canteiro central", 2) },
                 () => exampleRoad, v => exampleRoad = v);
        return w;
    }

    /// <summary>Vias da interseção (para escolher a principal) e cena real da pré-visualização.</summary>
    public static (List<RoadOption>, RealScene?) ForEdit(UIDocument uidoc, IntersectionDefinition it)
    {
        var doc = uidoc.Document;
        try
        {
            var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
            var roads = svc.Roads().Where(r => it.RoadIds.Contains(r.Def.Id)).ToList();
            var all = MarkingStorage.Definitions(doc);
            var options = roads.Select((r, k) =>
            {
                var w = r.Def.RightWidth + r.Def.LeftWidth;
                var med = r.Def.Gaps.Any(g => g.Median) ? ", canteiro central" : "";
                var dir = r.Def.TwoWay ? "" : ", mão única";
                return new RoadOption(r.Def.Id, $"Via {k + 1} – pista {UiHelpers.F(w, "0.00")} m{med}{dir}");
            }).ToList();
            var groups = new List<List<MarkingDefinition>>();
            var paths = new Dictionary<string, Core.Geometry.Polyline2>();
            foreach (var r in roads)
            {
                var g = all.Where(m => m.GroupId != null && m.GroupId == r.Def.GroupId && m is not RoadPavementDefinition).ToList();
                g.Add(r.Def);
                var key = RoadSectionInference.PathKey(r.Def.Path);
                foreach (var m in g)
                    if (m.Path != null && RoadSectionInference.PathKey(m.Path) == key) paths[m.Id] = r.Axis;
                    else if (m.Path != null && PathResolver.Resolve(doc, m.Path)?.Main is { } p) paths[m.Id] = p;
                groups.Add(g);
            }
            return (options, roads.Count >= 2 ? new RealScene(roads, groups, paths) : null);
        }
        catch (Exception ex)
        {
            Log.Error("Pré-visualização da interseção", ex);
            return (new List<RoadOption>(), null);
        }
    }
}

internal static class IntersectionRunner
{
    /// <summary>Executa a atualização de interseções/rotatórias numa transação própria.</summary>
    public static List<RenderResult> Run(UIDocument uidoc, string name, Func<IntersectionService, List<RenderResult>> action)
    {
        var doc = uidoc.Document;
        using var scope = MarkingService.RenderScope();
        using var t = new Transaction(doc, name);
        t.Start();
        var service = new MarkingService(doc, uidoc.ActiveView);
        var results = action(new IntersectionService(doc, service));
        t.Commit();
        return results;
    }
}

/// <summary>Cria ou atualiza as interseções: todas as do projeto ou a do cruzamento clicado.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdIntersecao : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => RunTool(uidoc, null);

    /// <summary>Janela de interseção aplicada a todas (<paramref name="forceAll"/> = true) ou à clicada.</summary>
    internal static Result RunTool(UIDocument uidoc, bool? forceAll)
    {
        var doc = uidoc.Document;
        var d = UiHelpers.Remembered<IntersectionDefinition>("Intersecao") ?? new IntersectionDefinition();
        var all = forceAll ?? true;
        var w = IntersectionForms.Intersection(d, false);
        if (forceAll == null)
            w.Section("Aplicar em")
             .Choice("Cruzamentos", new[] { ("Todos os cruzamentos e entroncamentos do projeto", true), ("Somente o cruzamento que eu clicar", false) },
                 () => all, v => all = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        UiHelpers.Remember("Intersecao", d);
        PluginContext.SaveSettings();

        Core.Geometry.Vec2? near = null;
        if (!all)
        {
            var pick = Picking.PickPoint(uidoc, "Clique próximo ao cruzamento/entroncamento das vias");
            if (pick == null) return Result.Cancelled;
            near = DetailHelpers.ToCore(pick);
        }
        var results = IntersectionRunner.Run(uidoc, "SV - Interseções", s => s.IntersectAll(d, near));
        if (results.Count == 0)
        {
            TaskDialog.Show(AppTitle, near == null
                ? "Nenhum cruzamento encontrado. Os eixos das vias do \"Sinalizar via\" precisam se cruzar, ou uma via deve terminar junto à outra (entroncamento em T)."
                : "Nenhum cruzamento de vias perto do ponto clicado (até 60 m).");
            return Result.Cancelled;
        }
        var count = MarkingStorage.Definitions(doc).OfType<IntersectionDefinition>().Count();
        var warnings = results.Where(r => r.Warnings.Count > 0).ToList();
        if (warnings.Count > 0) Report("Interseções", warnings);
        else TaskDialog.Show(AppTitle, $"Interseções ajustadas. O projeto tem {count} interseção(ões).");
        return Result.Succeeded;
    }
}

internal static class RoundaboutForms
{
    public static FormWindow Roundabout(RoundaboutDefinition d, bool edit, bool hasRoads)
    {
        string? angles = hasRoads ? null : string.Join("; ", d.Legs.Select(l => l.AngleDeg.ToString("0.#", UiHelpers.PtBr)));
        var legWidth = d.Legs.FirstOrDefault()?.Width ?? 7.0;
        var w = new FormWindow(edit ? "Editar rotatória" : "Rotatória", "Rotatória",
            hasRoads
                ? "Os ramos foram detectados a partir das vias do \"Sinalizar via\" que passam pelo centro; as vias são recortadas e ligadas à rotatória."
                : "Informe os ângulos dos ramos (graus, 0 = leste, anti-horário) e a largura das pistas dos ramos.",
            d, () =>
            {
                var c = (RoundaboutDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.Center = Core.Geometry.Vec2.Zero;
                var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
                var L = RoundaboutGenerator.Layout(c);
                var g = RoundaboutGenerator.Build(c, ctx);
                foreach (var ch in RoundaboutGenerator.Children(c, L, new OutputSettings(), 0))
                {
                    var p = ch.Path ?? (ch as HatchMarkingDefinition)?.Boundary;
                    if (ch is SignDefinition) continue;
                    g.Merge(MarkingBuilder.Build(ch, p == null ? null : new Core.Geometry.Polyline2(p.Points, p.Closed), ctx));
                }
                return new FormPreview(g, null, null, $"Diâmetro externo: {UiHelpers.F(2 * c.OuterRadius, "0.0")} m");
            }, false, edit ? "Aplicar" : "Criar", 1080, 720);
        w.Number("Raio da ilha central (m)", () => d.IslandRadius, v => d.IslandRadius = v, 1, 100)
         .Number("Faixa galgável em volta da ilha (m)", () => d.ApronWidth, v => d.ApronWidth = v, 0, 10, tooltip: "Para o giro de ônibus e caminhões (bloquete elevado 6 cm).")
         .Integer("Faixas na pista giratória", () => d.Lanes, v => d.Lanes = v, 1, 4)
         .Number("Largura de cada faixa (m)", () => d.LaneWidth, v => d.LaneWidth = v, 3, 10)
         .Number("Raio de entrada/saída (m)", () => d.EntryRadius, v => d.EntryRadius = v, 0, 60)
         .Number("Calçada em volta (m)", () => d.SidewalkWidth, v => d.SidewalkWidth = v, 0, 20)
         .Choice("Pavimento", new[] { ("Asfalto", TipoPavimento.Asfalto), ("Bloquete", TipoPavimento.Bloquete), ("Concreto", TipoPavimento.Concreto), ("Nenhum", TipoPavimento.Nenhum) },
             () => d.Pavement, v => d.Pavement = v);
        if (!hasRoads)
            w.Text("Ângulos dos ramos (°)", () => angles, v => angles = v, tooltip: "Ex.: 0; 90; 180; 270")
             .Number("Largura das pistas dos ramos (m)", () => legWidth, v =>
             {
                 legWidth = v;
                 d.Legs = ParseAngles(angles).Select(a => new RoundaboutLeg { AngleDeg = a, Width = legWidth, Sidewalk = d.SidewalkWidth }).ToList();
             }, 3, 40);
        w.Section("Ramos e sinalização")
         .Check("Ilhas separadoras (gota) nos ramos", () => d.SplitterIslands, v => d.SplitterIslands = v)
         .Number("Comprimento das ilhas separadoras (m)", () => d.SplitterLength, v => d.SplitterLength = v, 3, 60)
         .Number("Largura das ilhas junto à pista (m)", () => d.SplitterWidth, v => d.SplitterWidth = v, 0.8, 10)
         .Check("Linhas de dê a preferência, símbolos e zebrados", () => d.Markings, v => d.Markings = v)
         .Check("Travessias de pedestres e rebaixamentos", () => d.Crosswalks, v => d.Crosswalks = v)
         .Check("Placas R-2 e R-33 em cada entrada", () => d.Signs, v => d.Signs = v)
         .Check("Paisagismo na ilha central (árvores)", () => d.Landscaping, v => d.Landscaping = v);
        return w;
    }

    public static List<double> ParseAngles(string? text)
    {
        var res = new List<double>();
        foreach (var tok in (text ?? "").Split(new[] { ';', ' ', '\t', '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(tok.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                res.Add(v);
        return res;
    }
}

/// <summary>Rotatória sobre o cruzamento de vias (ou isolada, com ramos por ângulo).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdRotatoria : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var pick = Picking.PickPoint(uidoc, "Clique o centro da rotatória (ex.: cruzamento dos eixos das vias)");
        if (pick == null) return Result.Cancelled;
        var center = DetailHelpers.ToCore(pick);
        var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        var roads = svc.Roads();
        // Encaixa o centro no cruzamento mais próximo, se houver.
        var nodes = IntersectionGenerator.FindNodes(roads);
        var near = nodes.OrderBy(n => n.Node.DistanceTo(center)).FirstOrDefault();
        if (near.Roads != null && near.Node.DistanceTo(center) < 8) center = near.Node;

        var d = UiHelpers.Remembered<RoundaboutDefinition>("Rotatoria") ?? new RoundaboutDefinition();
        d = (RoundaboutDefinition)d.CloneWithNewId();
        d.ChildIds.Clear();
        d.Center = center;
        d.Z = UnitConv.M(pick.Z);
        d.Output = PluginContext.Settings.NewOutput();
        d.Legs = RoundaboutGenerator.LegsFromRoads(center, roads, d.OuterRadius + 25);
        var hasRoads = d.Legs.Count > 0;
        if (!hasRoads && d.Legs.Count == 0)
            foreach (var a in new[] { 0.0, 90, 180, 270 }) d.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7, Sidewalk = d.SidewalkWidth });
        CommandBase.EnsureDetailViewPublic(uidoc, d.Output);
        if (UiHelpers.ShowModal(RoundaboutForms.Roundabout(d, false, hasRoads)) != true) return Result.Cancelled;
        if (hasRoads) d.Legs = RoundaboutGenerator.LegsFromRoads(center, roads, d.OuterRadius + 25);
        UiHelpers.Remember("Rotatoria", d);
        PluginContext.SaveSettings();

        var results = IntersectionRunner.Run(uidoc, "SV - Rotatória", s =>
        {
            // Interseções no mesmo nó deixam de existir (a rotatória as substitui), com os recortes que faziam nas vias.
            foreach (var it in MarkingStorage.Definitions(doc).OfType<IntersectionDefinition>().Where(i => i.Node.DistanceTo(center) < d.OuterRadius + 5).ToList())
                s.Remove(it);
            return s.Refresh(d);
        });
        Report("Rotatória", results.Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }
}
