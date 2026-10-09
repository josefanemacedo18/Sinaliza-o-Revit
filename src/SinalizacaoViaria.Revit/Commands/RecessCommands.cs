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

/// <summary>Regenera uma via inteira com uma seção nova, mantendo eixo, pavimento, greide, trechos apagados e conexões.</summary>
internal static class RoadRegen
{
    public static List<RenderResult> Regenerate(UIDocument uidoc, RoadPavementDefinition pav, List<MarkingDefinition> members, RoadSetup setup,
        OutputSettings output, Polyline2? axis)
    {
        var doc = uidoc.Document;
            var defs = setup.Build(pav.PathRef, output, PluginContext.Catalog, pav.GroupId, pav.Id, axis);   // Build clona o caminho por marca
            // Sinalização automática editada à mão: fica como está e não é gerada outra igual no lugar.
            var manual = members.Where(m => m.Id != pav.Id && m.EditadoManualmente).ToList();
            defs = Core.Automation.AutoSignage.Preserve(defs, manual);
            var newPav = defs.OfType<RoadPavementDefinition>().FirstOrDefault();
            if (newPav != null)
            {
                newPav.CornerRadius ??= pav.CornerRadius;
                newPav.MergeStart = pav.MergeStart;
                newPav.MergeEnd = pav.MergeEnd;
                newPav.Hierarchy ??= pav.Hierarchy;
                newPav.Exclusions.AddRange(pav.Exclusions);
            }
            // Trechos apagados à mão (Apagar Trecho) passam para a marca equivalente da seção nova (mesmo tipo e código).
            var used = new HashSet<string>();
            foreach (var d in defs.Where(d => d is not RoadPavementDefinition))
            {
                var old = members.FirstOrDefault(m => !used.Contains(m.Id) && m.GetType() == d.GetType() && m.DisplayCode == d.DisplayCode && m.Exclusions.Any(z => z.Manual));
                if (old == null) continue;
                used.Add(old.Id);
                d.Exclusions.AddRange(old.Exclusions.Where(z => z.Manual));
            }
            // Recortes das conexões (interseções, rotatórias, rampas) são reaplicados pelo RefreshDependents.
            var results = new List<RenderResult>();
            using (MarkingService.RenderScope())
            using (var t = new Transaction(doc, "SV - Editar via (remover elementos antigos)"))
            {
                t.Start();
                var service = new MarkingService(doc, uidoc.ActiveView);
                foreach (var m in members.Where(m => m.Id != pav.Id && !manual.Contains(m))) service.Delete(m.Id);
                if (newPav == null) service.Delete(pav.Id);
                t.Commit();
            }
            results.AddRange(MarkingCreator.Commit(uidoc, defs, "SV - Editar via"));
            if (setup.Warnings.Count > 0 && results.Count > 0) results[0].Warnings.InsertRange(0, setup.Warnings);
            try
            {
                results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões da via", sv => sv.RefreshDependents(defs)).Where(r => r.Warnings.Count > 0));
            }
            catch (Exception ex)
            {
                Log.Error("Editar via – conexões", ex);
                results.Add(new RenderResult { Geometry = null });
                results[^1].Warnings.Add("As conexões da via não puderam ser refeitas: " + ex.Message);
            }
            // Obras hospedadas (pontes, viadutos, túneis, trincheiras) acompanham a nova seção; o terreno é refeito.
            results.AddRange(RoadWorks.RefreshHosted(uidoc, pav.GroupId));
            TerrainActions.AfterCreate(uidoc, defs, quiet: true);
        return results;
    }
}

/// <summary>
/// Recuos numa via já criada: baia de ônibus, faixa de desaceleração/aceleração ou recuo de embarque clicando no
/// bordo – a via é regenerada com o recuo (meio-fio, calçada, linhas, MVE, LCO, setas, placa e abrigo). O recuo pode
/// ser editado ou removido depois pelo Editar.
/// </summary>
internal static class RecessCommand
{
    private static readonly (string, TipoRecuo)[] Types =
    {
        ("Baia de ônibus", TipoRecuo.BaiaOnibus), ("Faixa de desaceleração (saída / conversão)", TipoRecuo.FaixaDesaceleracao),
        ("Faixa de aceleração (entrada)", TipoRecuo.FaixaAceleracao), ("Recuo de embarque / táxi / carga", TipoRecuo.RecuoEmbarque),
    };

    /// <summary>Formulário do recuo com prévia sobre a seção da própria via.</summary>
    public static FormWindow Form(RecuoVia r, RoadSetup setup, bool edit, Func<bool>? remove = null, Action<bool>? setRemove = null)
    {
        var laneW = setup.Right.Concat(setup.Left).Where(e => ElementoSecao.EhFaixaDeTrafego(e.Tipo)).Select(e => e.Largura).DefaultIfEmpty(3.5).Average();
        var w = new FormWindow(edit ? "Editar recuo da via" : "Recuo na via", "Baia de ônibus ou faixa auxiliar numa via existente",
            "O meio-fio recua no trecho (taper de entrada, extensão com a profundidade total e taper de saída) e a via é regenerada com a " +
            "sinalização do recuo: MVE amarela e ÔNIBUS na baia, LCO na boca, setas nas faixas auxiliares, placa e abrigo. Valores iniciais " +
            "pela velocidade da via (DNIT/AASHTO, indicativos).",
            null, () =>
            {
                var c = setup.Clone();
                c.LargurasVariaveis.Clear();
                c.Recuos = new List<RecuoVia> { r.Clone() };
                var clone = c.Recuos[0];
                clone.Estaca = 20;
                var len = clone.S3 + 30;
                var axis = new Polyline2(new[] { Vec2.Zero, new Vec2(len, 0) });
                var defs = c.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), PluginContext.Catalog, axis: axis);
                var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
                var geo = new MarkingGeometry();
                foreach (var d in defs)
                {
                    try
                    {
                        var p = d.Path is { Points.Count: >= 2 } pr && pr.ElementIds.Count == 0 && d is not SignDefinition && d is not UrbanElementDefinition ? new Polyline2(pr.Points) : axis;
                        var g = MarkingBuilder.Build(d, d.Path == null ? null : p, ctx);
                        g.Warnings.Clear();
                        geo.Merge(g);
                    }
                    catch { /* prévia */ }
                }
                var warn = c.Warnings.Distinct().ToList();
                return new FormPreview(geo, null, null, $"Trecho do recuo: {clone.S3 - clone.Estaca:0.0} m ({clone.TaperEntrada:0.#} + {clone.Comprimento:0.#} + {clone.TaperSaida:0.#})." +
                    (warn.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", warn) : ""));
            }, false, edit ? "Aplicar" : "Criar", 1080, 720);
        w.Choice("Tipo", Types, () => r.Tipo, v => r.Tipo = v, preset: v =>
            {
                var p = RecuoVia.Padrao(v, setup.Speed, laneW);
                var (st, left) = (r.Estaca, r.LadoEsquerdo);
                r.Tipo = v; r.Comprimento = p.Comprimento; r.Profundidade = p.Profundidade; r.TaperEntrada = p.TaperEntrada; r.TaperSaida = p.TaperSaida;
                r.Legenda = p.Legenda; r.PlacaEAbrigo = p.PlacaEAbrigo;
                r.Estaca = st; r.LadoEsquerdo = left;
            })
         .Check("Lado esquerdo do eixo (senão, direito)", () => r.LadoEsquerdo, v => r.LadoEsquerdo = v)
         .Number("Estaca do início da transição (m)", () => r.Estaca, v => r.Estaca = v, 0, 100000, "0.0")
         .Number("Taper de entrada (m)", () => r.TaperEntrada, v => r.TaperEntrada = v, 0, 300, "0.0")
         .Number("Extensão com a profundidade total (m)", () => r.Comprimento, v => r.Comprimento = v, 1, 1000, "0.0")
         .Number("Taper de saída (m)", () => r.TaperSaida, v => r.TaperSaida = v, 0, 300, "0.0")
         .Number("Profundidade do recuo (m)", () => r.Profundidade, v => r.Profundidade = v, 0.5, 8, "0.00")
         .Check("A calçada estreita (o alinhamento fica); senão, a calçada inteira se desloca", () => r.CalcadaRecua, v => r.CalcadaRecua = v)
         .Check("Transições em curva reversa (S)", () => r.CurvaReversa, v => r.CurvaReversa = v)
         .Check("Sinalização do recuo (MVE, legenda, LCO, setas)", () => r.Sinalizacao, v => r.Sinalizacao = v)
         .Check("Placa de ponto e abrigo na calçada (baia)", () => r.PlacaEAbrigo, v => r.PlacaEAbrigo = v)
         .Text("Legenda (embarque: TÁXI, CARGA E DESCARGA…)", () => r.Legenda, v => r.Legenda = v ?? "");
        if (remove != null && setRemove != null) w.Check("REMOVER este recuo da via", remove, setRemove);
        return w;
    }

    /// <summary>Clica no bordo de uma via existente e cria o recuo nesse ponto.</summary>
    public static Result Create(UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var p = Picking.PickPoint(uidoc, "Recuo na via: clique junto ao BORDO da via, no início do recuo (lado da baia/faixa) – ESC cancela");
        if (p == null) return Result.Cancelled;
        var pt = UnitConv.ToVec2(p);
        var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        var road = svc.Roads().Select(r => (R: r, Pr: r.Axis.Project(pt)))
            .Where(x => Math.Abs(x.Pr.Signed) <= Math.Max(x.R.Def.TotalLeft, x.R.Def.TotalRight) + 2)
            .OrderBy(x => Math.Abs(x.Pr.Signed)).Select(x => (IntersectionRoad?)x.R).FirstOrDefault();
        if (road == null) throw new UserMessageException("Nenhuma via do plugin no ponto clicado. Clique sobre a pista ou a calçada, junto ao bordo onde será o recuo.");
        var pav = road.Def;
        var setup = RoadTemplates.FromJson(pav.SetupJson)
                    ?? throw new UserMessageException("Esta via não guardou a seção transversal (versão anterior ou Pista): use Editar → A via inteira uma vez e depois crie o recuo.");
        var (st, signed) = road.Axis.Project(pt);
        var r = RecuoVia.Padrao(TipoRecuo.BaiaOnibus, setup.Speed,
            setup.Right.Concat(setup.Left).Where(e => ElementoSecao.EhFaixaDeTrafego(e.Tipo)).Select(e => e.Largura).DefaultIfEmpty(3.5).Average());
        r.Estaca = Math.Round(st, 1);
        r.LadoEsquerdo = signed > 0;
        var w = Form(r, setup, false);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        setup.Recuos.Add(r);
        return Apply(uidoc, pav, setup, "Recuo na via");
    }

    /// <summary>Edita (ou remove) o recuo correspondente à marca clicada.</summary>
    public static Result EditExisting(UIDocument uidoc, RecessMarkingDefinition rec)
    {
        var doc = uidoc.Document;
        var members = MarkingStorage.Definitions(doc).Where(d => d.GroupId == rec.GroupId).ToList();
        var pav = members.OfType<RoadPavementDefinition>().FirstOrDefault()
                  ?? throw new UserMessageException("A via deste recuo não foi encontrada.");
        var setup = RoadTemplates.FromJson(pav.SetupJson) ?? throw new UserMessageException("A via não guardou a seção transversal: edite a via inteira.");
        var r = setup.Recuos.Where(x => x.Tipo == rec.Type && x.LadoEsquerdo == rec.Left).OrderBy(x => Math.Abs(x.Estaca - rec.S0)).FirstOrDefault()
                ?? throw new UserMessageException("O recuo não foi encontrado na seção da via: edite a via inteira (quadro Recuos).");
        var remove = false;
        var work = r.Clone();
        var w = Form(work, setup, true, () => remove, v => remove = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        var i = setup.Recuos.IndexOf(r);
        if (remove) setup.Recuos.RemoveAt(i); else setup.Recuos[i] = work;
        return Apply(uidoc, pav, setup, remove ? "Recuo removido" : "Recuo editado");
    }

    private static Result Apply(UIDocument uidoc, RoadPavementDefinition pav, RoadSetup setup, string title)
    {
        var doc = uidoc.Document;
        var members = MarkingStorage.Definitions(doc).Where(d => d.GroupId == pav.GroupId).ToList();
        var output = pav.Output.Clone();
        var axis = PathResolver.Resolve(doc, pav.PathRef)?.Main;
        var results = RoadRegen.Regenerate(uidoc, pav, members, setup, output, axis);
        CommandBase.ReportResults(title, results);
        return Result.Succeeded;
    }
}

/// <summary>Baia de ônibus / faixa auxiliar numa via existente (clicando no bordo).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdRecuoVia : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) => RecessCommand.Create(uidoc);
}
