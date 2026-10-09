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
/// Extensão de calçada num ponto qualquer de uma via do plugin (reta ou curva): clique junto ao meio-fio e, opcionalmente, no
/// fim do trecho. A extensão fica gravada na seção da via e é refeita a partir do eixo sempre que a via muda.
/// </summary>
internal static class RoadExtensionCommand
{
    private static ExtensaoCalcada _last = new();
    private static bool _centered = true;

    private static readonly (string, TipoTransicao)[] Kinds =
    {
        ("Curvas reversas (com raio)", TipoTransicao.Curva), ("Chanfro", TipoTransicao.Chanfro), ("Reta (perpendicular ao meio-fio)", TipoTransicao.Reta),
    };

    /// <summary>Via do plugin junto ao ponto (até o alinhamento + 2 m); nulo se não houver.</summary>
    public static IntersectionRoad? RoadAt(UIDocument uidoc, Vec2 pt)
    {
        var doc = uidoc.Document;
        var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        return svc.Roads().Select(r => (R: r, Pr: r.Axis.Project(pt)))
            .Where(x => Math.Abs(x.Pr.Signed) <= Math.Max(x.R.Def.TotalLeft, x.R.Def.TotalRight) + 2)
            .OrderBy(x => Math.Abs(x.Pr.Signed)).Select(x => (IntersectionRoad?)x.R).FirstOrDefault();
    }

    /// <summary>Clique junto ao meio-fio de uma via: nulo = não há via do plugin ali (segue o modo livre).</summary>
    public static Result? AtRoad(UIDocument uidoc, Vec2 pt)
    {
        if (RoadAt(uidoc, pt) is not { } road) return null;
        var pav = road.Def;
        var setup = RoadTemplates.FromJson(pav.SetupJson)
                    ?? throw new UserMessageException("Esta via não guardou a seção transversal (versão anterior ou Pista): use Editar → A via inteira uma vez e depois crie a extensão.");
        var (st, signed) = road.Axis.Project(pt);
        var e = _last.Clone();
        e.Id = Guid.NewGuid().ToString("N");
        e.LadoEsquerdo = signed > 0;
        var p2 = Picking.PickPoint(uidoc, "Extensão de calçada: clique no FIM do trecho, junto ao mesmo meio-fio – ESC: informar o comprimento");
        var twoClicks = p2 != null;
        if (twoClicks)
        {
            var (st2, _) = road.Axis.Project(UnitConv.ToVec2(p2!));
            e.Estaca = Math.Round(Math.Min(st, st2), 2);
            e.Comprimento = Math.Round(Math.Max(1.0, Math.Abs(st2 - st)), 2);
        }
        var click = st;
        void Place()
        {
            if (!twoClicks) e.Estaca = Math.Round(_centered ? click - e.Comprimento / 2 : click, 2);
        }
        Place();
        var w = Form(e, setup, pav, road.Axis, false, twoClicks, Place);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        Place();
        setup.ExtensoesCalcada.Add(e);
        _last = e.Clone();
        var results = new List<RenderResult>();
        using (var tg = new TransactionGroup(uidoc.Document, "SV - Extensão de calçada"))
        {
            tg.Start();
            results.AddRange(RoadAccessCommand.Regenerate(uidoc, pav, setup));
            tg.Assimilate();
        }
        CommandBase.ReportResults("Extensão de calçada", results);
        return Result.Succeeded;
    }

    /// <summary>Editar (ou remover) uma extensão de calçada da via.</summary>
    public static Result Edit(UIDocument uidoc, RoadPavementDefinition pav, string extId)
    {
        var setup = RoadTemplates.FromJson(pav.SetupJson) ?? throw new UserMessageException("Esta via não guardou a seção transversal.");
        var e = setup.ExtensoesCalcada.FirstOrDefault(x => x.Id == extId) ?? throw new UserMessageException("Extensão de calçada não encontrada na seção da via.");
        var axis = PathResolver.Resolve(uidoc.Document, pav.PathRef)?.Main ?? throw new UserMessageException("Eixo da via não encontrado.");
        var work = e.Clone();
        var remove = false;
        var w = Form(work, setup, pav, axis, true, true, () => { }, () => remove, v => remove = v);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        var i = setup.ExtensoesCalcada.IndexOf(e);
        if (remove) setup.ExtensoesCalcada.RemoveAt(i);
        else setup.ExtensoesCalcada[i] = work;
        var results = new List<RenderResult>();
        using (var tg = new TransactionGroup(uidoc.Document, "SV - Editar extensão de calçada"))
        {
            tg.Start();
            results.AddRange(RoadAccessCommand.Regenerate(uidoc, pav, setup));
            tg.Assimilate();
        }
        CommandBase.ReportResults("Extensão de calçada", results);
        return Result.Succeeded;
    }

    /// <summary>Id da extensão a que pertence um elemento derivado da via (id da via + ":" + id da extensão + ...).</summary>
    public static string? ExtensionIdOf(MarkingDefinition d, RoadPavementDefinition pav)
    {
        var prefix = RoadFeatures.PrefixOf(pav);
        if (!d.Id.StartsWith(prefix)) return null;
        var rest = d.Id[prefix.Length..];
        var k = rest.IndexOf(':');
        var id = k < 0 ? rest : rest[..k];
        if (id.EndsWith("-oposta")) id = id[..^"-oposta".Length];
        return RoadTemplates.FromJson(pav.SetupJson)?.ExtensoesCalcada.Any(x => x.Id == id) == true ? id : null;
    }

    /// <summary>Janela da extensão com prévia sobre a própria via (o trecho em volta da extensão).</summary>
    private static FormWindow Form(ExtensaoCalcada e, RoadSetup setup, RoadPavementDefinition pav, Polyline2 axis, bool edit, bool fixedLength,
        Action place, Func<bool>? remove = null, Action<bool>? setRemove = null)
    {
        var lado = setup.Lado(e.LadoEsquerdo);
        var parkTxt = lado.Parking is { } pk ? $"estacionamento de {UiHelpers.F(pk, "0.00")} m junto ao meio-fio" : "sem estacionamento junto ao meio-fio (informe a profundidade)";
        var w = new FormWindow(edit ? "Editar extensão de calçada" : "Extensão de calçada", "Extensão de calçada na via",
            $"Lado {(e.LadoEsquerdo ? "esquerdo" : "direito")} do eixo, {parkTxt}. A extensão encaixa na face do meio-fio, avança sobre o estacionamento, " +
            "recebe meio-fio e sarjeta na frente e fica gravada na via (refeita quando a via muda). Com travessia, a faixa, as rampas e o piso tátil " +
            "ficam no centro, alinhados com uma extensão espelhada do outro lado quando ele também tem estacionamento.",
            null, () => Preview(e, setup, pav, axis, place), false, edit ? "Aplicar" : "Criar", 1080, 760);
        w.Section("Trecho");
        if (!fixedLength)
            w.Choice("O ponto clicado é", new[] { ("o centro da extensão", true), ("o início (no sentido do eixo)", false) }, () => _centered, v => { _centered = v; place(); });
        w.Number("Comprimento ao longo do eixo (m)", () => e.Comprimento, v => { e.Comprimento = Math.Max(1.0, v); place(); }, 1, 200, "0.00",
                "Pontas incluídas. Com travessia, a extensão cresce o que for preciso para a faixa e as rampas ficarem sobre ela (com aviso).")
         .Number("Profundidade (m) – 0 = a largura do estacionamento", () => e.Profundidade ?? 0, v => e.Profundidade = v < 0.05 ? null : Math.Clamp(v, 0.3, 10), 0, 10)
         .Choice("Ponta inicial", Kinds, () => e.Ponta, v => e.Ponta = v)
         .Number("Raio / comprimento da ponta inicial (m)", () => e.Raio, v => e.Raio = Math.Clamp(v, 0.1, 20), 0.1, 20)
         .Choice("Ponta final", Kinds, () => e.PontaFinal ?? e.Ponta, v => e.PontaFinal = v)
         .Number("Raio / comprimento da ponta final (m)", () => e.RaioFinal ?? e.Raio, v => e.RaioFinal = Math.Clamp(v, 0.1, 20), 0.1, 20)
         .Section("Travessia no meio da quadra")
         .Check("Travessia de pedestres no centro da extensão", () => e.Travessia, v => e.Travessia = v,
             "Faixa de face a face, rampas nas duas pontas e piso tátil de alerta; a travessia sobre o canteiro central, se houver, segue a opção automática.")
         .Number("Largura da faixa (m)", () => e.LarguraFaixa, v => e.LarguraFaixa = Math.Clamp(v, 1.0, 10), 1, 10)
         .Check("Rampas nas pontas da travessia", () => e.Rampa, v => e.Rampa = v)
         .Check("Piso tátil de alerta nas rampas", () => e.Tatil, v => e.Tatil = v)
         .Section("Mobiliário (elementos urbanos), fora da travessia")
         .Check("Canteiro gramado (com as árvores dentro)", () => e.Canteiro, v => e.Canteiro = v)
         .Integer("Árvores", () => e.Arvores, v => e.Arvores = v, 0, 20)
         .Integer("Paraciclos", () => e.Paraciclos, v => e.Paraciclos = v, 0, 30)
         .Integer("Bancos", () => e.Bancos, v => e.Bancos = v, 0, 10);
        if (remove != null && setRemove != null) w.Section("Remover").Check("Remover esta extensão da via", remove, setRemove);
        return w;
    }

    /// <summary>Prévia: a via no trecho da extensão (com os recortes e a travessia), gerada como será criada.</summary>
    private static FormPreview? Preview(ExtensaoCalcada e, RoadSetup setup, RoadPavementDefinition pav, Polyline2 axis, Action place)
    {
        place();
        var c = setup.Clone();
        c.ExtensoesCalcada = c.ExtensoesCalcada.Where(x => x.Id != e.Id).Append(e.Clone()).ToList();
        var pr = PathReference.FromPoints(axis.Points, pav.PathRef.Z);
        var defs = c.Build(pr, new OutputSettings(), PluginContext.Catalog, null, pav.Id, axis);
        var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
        var center = axis.PointAt(Math.Clamp(e.EstacaTravessia, 0, axis.Length));
        var half = Math.Max(e.Comprimento / 2 + 10, 18);
        var box = Polygon2.Rectangle(center - new Vec2(half, half), center + new Vec2(half, half));
        var geo = new MarkingGeometry();
        foreach (var d in defs)
        {
            try
            {
                var p = d.Path is { Points.Count: >= 2 } dp && !(dp.Points.Count == pr.Points.Count && dp.Points[0].DistanceTo(pr.Points[0]) < 1e-9)
                    ? new Polyline2(dp.Points) : axis;
                var g = MarkingBuilder.Build(d, d.Path == null ? null : p, ctx);
                g.Warnings.Clear();
                foreach (var piece in g.Pieces)
                    if (PolygonOps.TotalArea(PolygonOps.Intersect(new[] { piece.Shape }, new[] { box })) > 1e-6) geo.Pieces.Add(piece);
            }
            catch { /* prévia */ }
        }
        var warn = c.Warnings.Distinct().ToList();
        return new FormPreview(geo, null, null, $"Trecho: estaca {PontoLargura.FormatEstaca(e.Estaca)} a {PontoLargura.FormatEstaca(e.Estaca + e.Comprimento)} ({UiHelpers.F(e.Comprimento, "0.0")} m)." +
            (warn.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", warn) : ""));
    }
}
