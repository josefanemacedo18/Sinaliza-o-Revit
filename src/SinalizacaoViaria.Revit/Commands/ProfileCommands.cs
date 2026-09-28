using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Perfil da via: a via passa a ter greide (perfil longitudinal) sobre a topografia e vira obra onde o terreno pede –
/// viaduto/ponte nos aterros altos, túnel nos cortes profundos, trincheira nos cortes médios; o resto é aterro e corte no
/// Toposolid. Tudo continua sendo a mesma via (pisos, faixas, conexões).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdPerfilVia : CommandBase
{
    private static PerfilOpcoes _opt = new();
    private static bool _replace = true;

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var road = RoadWorks.PickRoad(uidoc, "Perfil da via: clique a VIA (pista, calçada ou linha)");
        if (road == null) return Result.Cancelled;
        var L = road.Axis.Length;
        var groundFn = RoadWorks.Ground(doc, road.BaseZ);
        var hasTerrain = TerrainModel.Hosts(doc).Count > 0;
        double? GroundAt(double s) => groundFn(road.Axis.PointAt(s));
        var hosted = RoadWorks.Hosted(doc, road.GroupId);
        var o = _opt;
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        PerfilResultado Compute() => RoadProfileDesigner.Design(L, s => hasTerrain ? GroundAt(s) : road.Grade.Z(s), o);

        var w = new FormWindow("Perfil da via", "Perfil longitudinal e obras pela topografia",
                $"PARA QUE SERVE: definir a altura da via (o GREIDE) ao longo dos seus {L.ToString("0", pt)} m sobre a topografia.\n" +
                "O GRÁFICO ao lado é um corte ao longo do eixo: em marrom o terreno natural, a linha escura é a via; em VERMELHO " +
                "o terreno que será cortado, em VERDE o aterro. Embaixo as estacas (distância ao longo da via), à esquerda as cotas; " +
                "os quadrados brancos são os PIVs (pontos onde a rampa muda) e \"i = %\" é a rampa de cada trecho.\n" +
                "COMO USAR: 1) escolha como o greide é traçado (acompanhar o terreno, rampa constante, nível ou PIVs digitados); " +
                "2) limite a rampa máxima; 3) diga a partir de que altura de aterro/corte a via vira obra (viaduto/ponte, trincheira, " +
                "túnel – faixas coloridas no alto do gráfico). O gráfico se atualiza a cada mudança.\n" +
                "AO APLICAR: o greide passa a valer para a via inteira (pista, calçadas, linhas, dispositivos), as obras são criadas " +
                "nos trechos indicados e o Toposolid é cortado/aterrado com taludes até o terreno natural." +
                (hasTerrain ? "" : "\n⚠ Sem Toposolid no projeto: o perfil parte do greide atual (crie o terreno em Massa e terreno para usar a topografia)."),
                null, () =>
                {
                    var r = Compute();
                    var g = RoadProfileDesigner.Chart(r);
                    g.Warnings.AddRange(r.Warnings);
                    return new FormPreview(g, null, null,
                        $"{r.Summary()}\nÁrea de corte no eixo {r.CutM2.ToString("N0", pt)} m² · aterro {r.FillM2.ToString("N0", pt)} m²", Paper: true);
                }, false, "Aplicar", 1240, 800)
            .Choice("Greide", new[]
            {
                ("Acompanhar o terreno suavizado (menor terraplenagem)", ModoGreide.AcompanharTerreno), ("Rampa constante entre as pontas", ModoGreide.RampaConstante),
                ("Nivelado numa cota", ModoGreide.Nivelado), ("PIVs digitados", ModoGreide.Manual),
            }, () => o.Mode, v => o.Mode = v)
            .Number("Rampa máxima (%)", () => o.MaxGrade * 100, v => o.MaxGrade = v / 100, 0.5, 15, "0.0#", "DNIT: 3–6 % em rodovias; vias urbanas até 8–10 %.")
            .Number("Curva vertical nos PIVs (m)", () => o.VerticalCurve, v => o.VerticalCurve = v, 0, 1000, "0")
            .If(() => o.Mode == ModoGreide.AcompanharTerreno, x => x
                .Number("Suavização do terreno (m)", () => o.Smoothing, v => o.Smoothing = v, 5, 2000, "0", "Janela da média móvel: maior = greide mais reto.")
                .Number("Alteamento do greide (m, + sobe / − desce)", () => o.Offset, v => o.Offset = v, -30, 30))
            .If(() => o.Mode is ModoGreide.AcompanharTerreno or ModoGreide.RampaConstante, x => x
                .Check("Início na cota do terreno", () => o.StartZ == null, v => o.StartZ = v ? null : o.StartZ ?? 0)
                .Number("Cota no início (m, relativa à base)", () => o.StartZ ?? 0, v => { if (o.StartZ != null) o.StartZ = v; }, -500, 500)
                .Check("Fim na cota do terreno", () => o.EndZ == null, v => o.EndZ = v ? null : o.EndZ ?? 0)
                .Number("Cota no fim (m, relativa à base)", () => o.EndZ ?? 0, v => { if (o.EndZ != null) o.EndZ = v; }, -500, 500))
            .If(() => o.Mode == ModoGreide.Nivelado, x => x.Number("Cota do greide (m, relativa à base)", () => o.LevelZ, v => o.LevelZ = v, -500, 500))
            .If(() => o.Mode == ModoGreide.Manual, x => x.Text("PIVs – uma linha por PIV: estaca; cota; curva", () => o.ManualPvis, v => o.ManualPvis = v ?? "", true,
                "Ex.: 0; 0\\n120; 8; 80\\n300; 2"))
            .Section("Seção")
            .Number("Caimento transversal da pista (%)", () => o.Crossfall * 100, v => o.Crossfall = v / 100, 0, 8, "0.0#", "Abaulamento de duas águas (2 % usual).")
            .Number("Talude de corte (H : 1 V)", () => o.CutSlope, v => o.CutSlope = v, 0.3, 5, "0.0#")
            .Number("Talude de aterro (H : 1 V)", () => o.FillSlope, v => o.FillSlope = v, 1, 5, "0.0#")
            .Section("A via vira obra onde o terreno pede")
            .Check("Criar as obras automaticamente", () => o.AutoStructures, v => o.AutoStructures = v)
            .If(() => o.AutoStructures, x => x
                .Number("Viaduto/ponte quando o aterro passar de (m)", () => o.BridgeFill, v => o.BridgeFill = v, 2, 100)
                .Check("Pontes (sobre rios) em vez de viadutos", () => o.Rivers, v => o.Rivers = v)
                .Number("Túnel quando o corte passar de (m)", () => o.TunnelCut, v => o.TunnelCut = v, 5, 300)
                .Number("Trincheira (muros) quando o corte passar de (m, 0 = taludes)", () => o.TrenchCut, v => o.TrenchCut = v, 0, 100)
                .Number("Comprimento mínimo de uma obra (m)", () => o.MinStructure, v => o.MinStructure = v, 5, 500, "0")
                .Number("Unir obras separadas por menos de (m)", () => o.MergeGap, v => o.MergeGap = v, 0, 500, "0")
                .Check("Substituir as obras que esta via já tem", () => _replace, v => _replace = v,
                    "As obras usam os últimos parâmetros de Viaduto/Ponte, Túnel e Trincheira (sistema, pilares, emboques, muros)."));
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;

        var res = Compute();
        var baseGrade = res.Grade;
        baseGrade.AdjustTerrain = true;
        var results = new List<RenderResult>();
        var keep = hosted;
        if (o.AutoStructures && _replace && hosted.Count > 0)
        {
            using (MarkingService.RenderScope())
            using (var t = new Transaction(doc, "SV - Remover obras da via"))
            {
                t.Start();
                var svc = new MarkingService(doc, uidoc.ActiveView);
                foreach (var h in hosted) svc.Delete(h.Id);
                t.Commit();
            }
            keep = new();
        }
        var created = new List<MarkingDefinition>();
        if (o.AutoStructures)
            foreach (var seg in res.Segments.Where(x => x.Kind is TipoTrecho.Viaduto or TipoTrecho.Tunel or TipoTrecho.Trincheira))
            {
                if (keep.OfType<IHostedStructure>().Any(h => Math.Max(h.HostStart, h.HostEnd) > seg.S0 && Math.Min(h.HostStart, h.HostEnd) < seg.S1)) continue;
                MarkingDefinition def;
                switch (seg.Kind)
                {
                    case TipoTrecho.Viaduto:
                    {
                        var key = o.Rivers ? "infra:ponte" : "infra:viaduto";
                        var b = UiHelpers.Remembered<BridgeDefinition>(key) ?? new BridgeDefinition { Kind = o.Rivers ? TipoObraDeArte.Ponte : TipoObraDeArte.Viaduto };
                        if (UiHelpers.Remembered<BridgeDefinition>(key) == null) b.ApplyKindDefaults();
                        b = (BridgeDefinition)b.CloneWithNewId();
                        b.ProfileKind = PerfilObra.GreideDaVia;
                        b.PierStations = null;
                        b.Water = o.Rivers && b.Water;
                        def = RoadWorks.Host(b, road.Pavement, seg.S0, seg.S1);
                        break;
                    }
                    case TipoTrecho.Tunel:
                    {
                        var tn = (TunnelDefinition)(UiHelpers.Remembered<TunnelDefinition>("infra:tunel") ?? new TunnelDefinition()).CloneWithNewId();
                        tn.StraightAxis = false;
                        def = RoadWorks.Host(tn, road.Pavement, seg.S0, seg.S1);
                        break;
                    }
                    default:
                    {
                        var tr = (TrenchDefinition)(UiHelpers.Remembered<TrenchDefinition>("infra:trincheira") ?? new TrenchDefinition()).CloneWithNewId();
                        tr.KeepRoadGrade = true;
                        tr.CoverLength = 0;
                        def = RoadWorks.Host(tr, road.Pavement, seg.S0, seg.S1);
                        break;
                    }
                }
                created.Add(def);
            }
        var grade = RoadWorks.Compose(doc, road, keep.Concat(created), baseGrade);
        foreach (var c in created) c.Output.Grade = grade.Clone();
        results.AddRange(RoadWorks.SetGrade(uidoc, road.GroupId, grade, created));
        TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { road.Pavement }.Concat(created).ToList());
        var summary = res.Summary() + (created.Count > 0 ? $"\n{created.Count} obra(s) criada(s) na via." : "\nNenhuma obra necessária: a via fica em aterro/corte no terreno.");
        results.Insert(0, new RenderResult());
        results[0].Warnings.Add(summary);
        results[0].Warnings.AddRange(res.Warnings);
        Report("Perfil da via", results.Where(r => r.Warnings.Count > 0).ToList(), alwaysShow: true);
        return Result.Succeeded;
    }
}
