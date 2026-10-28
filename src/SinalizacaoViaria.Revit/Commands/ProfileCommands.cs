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

    /// <summary>PIVs atuais da via no formato "estaca; cota; curva" (uma linha por PIV).</summary>
    private static string CurrentPvis(RoadGrade g)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return string.Join("\n", g.Points.OrderBy(p => p.S).Select(p =>
            p.Curve is { } c ? string.Format(ci, "{0:0.##}; {1:0.###}; {2:0.#}", p.S, p.Z, c) : string.Format(ci, "{0:0.##}; {1:0.###}", p.S, p.Z)));
    }
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
        // Estaqueamento da via (D-023): rótulos dos gráficos e das seções; gravado ao aplicar.
        var est = road.Pavement.Estaqueamento?.Clone() ?? new Estaqueamento();
        // Terraplenagem por estaca estimada pela seção-tipo (Etapa 18): plataforma da via (pista e calçadas) e os taludes.
        Func<Vec2, double?> ground = p => hasTerrain ? groundFn(p) : null;
        SecaoTipo Tipo() => new(road.Pavement.TotalLeft, road.Pavement.TotalRight, o.CutSlope, o.FillSlope, Trechos: road.Grade.Taludes);
        var grafico = 0;
        double estacaVer = 0, exagero = 2, trechoDe = 0, trechoAte = L;
        var pivNum = 1;
        double pivS = Math.Round(L / 2), pivZ = 0, pivC = 0;
        List<GradePoint> Pivs() => RoadGrade.ParsePvis(o.ManualPvis);
        void GravarPivs(List<GradePoint> l) => o.ManualPvis = EstacasTerraplenagem.TextoPivs(l);

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
                    var tipo = Tipo();
                    var (vol, _) = EstacasTerraplenagem.Volumes(road.Axis, r.Grade, tipo, ground, est.Intervalo, 0.5);
                    var g = grafico switch
                    {
                        1 => EarthworkCharts.Secao(EstacasTerraplenagem.Secao(road.Axis, estacaVer, r.Grade, tipo, ground), $"Estaca {est.Rotulo(estacaVer)}", exagero),
                        2 => EarthworkCharts.PorEstaca(vol, est),
                        3 => EarthworkCharts.Massas(EstacasTerraplenagem.Bruckner(vol, o.Homogenization), o.Homogenization, est),
                        _ => RoadProfileDesigner.Chart(r),
                    };
                    g.Warnings.AddRange(r.Warnings);
                    var (tc, ta) = SectionVolumes.Trecho(vol, trechoDe, trechoAte);
                    var info = $"{r.Summary()}\nÁrea de corte no eixo {r.CutM2.ToString("N0", pt)} m² · aterro {r.FillM2.ToString("N0", pt)} m²";
                    info += hasTerrain
                        ? $"\nTerraplenagem estimada pela seção-tipo (plataforma de {(tipo.LeftHalf + tipo.RightHalf).ToString("0.##", pt)} m e taludes; seções a cada " +
                          $"{est.Intervalo.ToString("0.##", pt)} m, média das áreas): corte {vol.CorteM3.ToString("N0", pt)} m³ · aterro {vol.AterroM3.ToString("N0", pt)} m³; " +
                          $"trecho {est.Rotulo(trechoDe)} a {est.Rotulo(trechoAte)}: corte {tc.ToString("N0", pt)} m³ · aterro {ta.ToString("N0", pt)} m³ – o exato vem da Terraplenagem."
                        : "\nSem Toposolid: sem terreno nas seções (corte e aterro por estaca indisponíveis).";
                    return new FormPreview(g, null, null, info, Paper: true);
                }, false, "Aplicar", 1240, 800)
            .Choice("Gráfico", new[]
            {
                ("Perfil longitudinal – terreno × greide", 0), ("Seção transversal na estaca", 1), ("Áreas de corte e aterro por estaca", 2),
                ("Diagrama de massas (Brückner)", 3),
            }, () => grafico, v => grafico = v,
                "A seção, as áreas e o diagrama usam a seção-tipo da via (plataforma e taludes) sobre o terreno – uma estimativa na hora; o resultado exato vem da Terraplenagem.")
            .If(() => grafico == 1, x => x
                .Text("Estaca da seção (n+resto ou m)", () => est.Rotulo(estacaVer), v =>
                {
                    if (est.Ler(v) is { } val && est.Distancia(val, L) is { } d) estacaVer = Math.Clamp(d, 0, L);
                }, false, "Ex.: 5+10,00 – ou a distância em metros desde o início do eixo.")
                .Button("◀ Estaca anterior", () => estacaVer = Math.Max(0, Math.Ceiling(estacaVer / est.Intervalo - 1 - 1e-9) * est.Intervalo))
                .Button("Próxima estaca ▶", () => estacaVer = Math.Min(L, Math.Floor(estacaVer / est.Intervalo + 1 + 1e-9) * est.Intervalo))
                .Number("Exagero vertical (1 = sem exagero)", () => exagero, v => exagero = v, 1, 20, "0.#"))
            .If(() => grafico == 3 && o.Mode != ModoGreide.Compensado, x => x
                .Number("Fator de homogeneização (corte ÷ aterro)", () => o.Homogenization, v => o.Homogenization = v, 1, 2, "0.00",
                    "Volume de corte que cada m³ de aterro compactado consome (informado por você – ensaio ou projeto)."))
            .If(() => grafico >= 2, x => x
                .Number("Volumes do trecho – da distância (m)", () => trechoDe, v => trechoDe = Math.Clamp(v, 0, L), 0, L, "0.##")
                .Number("até a distância (m)", () => trechoAte, v => trechoAte = Math.Clamp(v, 0, L), 0, L, "0.##"))
            .Choice("Greide", new[]
            {
                ("Acompanhar o terreno suavizado (menor terraplenagem)", ModoGreide.AcompanharTerreno), ("Rampa constante entre as pontas", ModoGreide.RampaConstante),
                ("Compensar corte e aterro (menos bota-fora e empréstimo)", ModoGreide.Compensado),
                ("Nivelado numa cota", ModoGreide.Nivelado), ("PIVs digitados (edite os da via)", ModoGreide.Manual),
            }, () => o.Mode, v =>
            {
                o.Mode = v;
                // PIVs digitados partem do greide atual da via (para ajustar em vez de começar do zero).
                if (v == ModoGreide.Manual && string.IsNullOrWhiteSpace(o.ManualPvis)) o.ManualPvis = CurrentPvis(road.Grade);
            })
            .Number("Rampa máxima (%)", () => o.MaxGrade * 100, v => o.MaxGrade = v / 100, 0.5, 15, "0.0#", "DNIT: 3–6 % em rodovias; vias urbanas até 8–10 %.")
            .Number("Curva vertical nos PIVs (m)", () => o.VerticalCurve, v => o.VerticalCurve = v, 0, 1000, "0")
            .Number("Parâmetro K das curvas (m/%; 0 = só o comprimento acima)", () => o.CurveK, v => o.CurveK = v, 0, 200, "0.#",
                "L = K × variação de rampa (%). Urbano ~40 km/h: K 4–7; rodovia 80 km/h: K 24–30 (DNIT). O maior entre L e o comprimento acima vale.")
            .If(() => o.Mode == ModoGreide.Compensado, x => x
                .Number("Suavização do terreno (m)", () => o.Smoothing, v => o.Smoothing = v, 5, 2000, "0")
                .Number("Fator de homogeneização (corte ÷ aterro)", () => o.Homogenization, v => o.Homogenization = v, 1, 2, "0.00",
                    "O greide sobe ou desce até o corte cobrir o aterro compactado × este fator."))
            .If(() => o.Mode == ModoGreide.AcompanharTerreno, x => x
                .Number("Suavização do terreno (m)", () => o.Smoothing, v => o.Smoothing = v, 5, 2000, "0", "Janela da média móvel: maior = greide mais reto.")
                .Number("Alteamento do greide (m, + sobe / − desce)", () => o.Offset, v => o.Offset = v, -30, 30))
            .If(() => o.Mode is ModoGreide.AcompanharTerreno or ModoGreide.RampaConstante or ModoGreide.Compensado, x => x
                .Check("Início na cota do terreno", () => o.StartZ == null, v => o.StartZ = v ? null : o.StartZ ?? 0)
                .Number("Cota no início (m, relativa à base)", () => o.StartZ ?? 0, v => { if (o.StartZ != null) o.StartZ = v; }, -500, 500)
                .Check("Fim na cota do terreno", () => o.EndZ == null, v => o.EndZ = v ? null : o.EndZ ?? 0)
                .Number("Cota no fim (m, relativa à base)", () => o.EndZ ?? 0, v => { if (o.EndZ != null) o.EndZ = v; }, -500, 500))
            .If(() => o.Mode == ModoGreide.Nivelado, x => x.Number("Cota do greide (m, relativa à base)", () => o.LevelZ, v => o.LevelZ = v, -500, 500))
            .If(() => o.Mode == ModoGreide.Manual, x => x.Text("PIVs – uma linha por PIV: estaca; cota; curva", () => o.ManualPvis, v => o.ManualPvis = v ?? "", true,
                "Ex.: 0; 0\\n120; 8; 80\\n300; 2")
                // Editar um PIV com o perfil à vista: o gráfico, as áreas e os volumes se refazem a cada botão.
                .Integer("PIV nº (1 = o primeiro em estaca)", () => pivNum, v => pivNum = v, 1, 500)
                .Button("Ler o PIV nº nos campos abaixo", () =>
                {
                    var l = Pivs();
                    if (pivNum >= 1 && pivNum <= l.Count) (pivS, pivZ, pivC) = (l[pivNum - 1].S, l[pivNum - 1].Z, l[pivNum - 1].Curve ?? 0);
                })
                .Number("Estaca do PIV (m ao longo do eixo)", () => pivS, v => pivS = Math.Clamp(v, 0, L), 0, L, "0.##")
                .Number("Cota do PIV (m, relativa à base)", () => pivZ, v => pivZ = v, -500, 500, "0.###")
                .Number("Curva vertical do PIV (m; 0 = a padrão)", () => pivC, v => pivC = v, 0, 1000, "0")
                .Button("Cota do terreno nesta estaca", () => { if (hasTerrain && GroundAt(pivS) is { } gz) pivZ = Math.Round(gz, 3); })
                .Button("Acrescentar o PIV", () => GravarPivs(EstacasTerraplenagem.AcrescentarPiv(Pivs(), pivS, pivZ, pivC)))
                .Button("Mover o PIV nº para a estaca e a cota acima", () => GravarPivs(EstacasTerraplenagem.MoverPiv(Pivs(), pivNum, pivS, pivZ)))
                .Button("Remover o PIV nº", () => GravarPivs(EstacasTerraplenagem.RemoverPiv(Pivs(), pivNum))))
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
        // Estaqueamento da via (D-023): só muda como as estacas aparecem nos relatórios desta via.
        var inicioTxt = est.Formatar(est.Inicio);
        var intervalo = est.Intervalo;
        var equacoesTxt = est.EquacoesTexto();
        w.Section("Estaqueamento (relatórios desta via)")
            .Text("Estaca do início do eixo (n+resto ou m)", () => inicioTxt, v => inicioTxt = v ?? "", false,
                "Ex.: 100+0,00 (com estacas de 20 m, 2 000 m). Os formulários de largura, recuo e extensão continuam pedindo a distância desde o início do eixo.")
            .Number("Intervalo entre estacas (m)", () => intervalo, v => intervalo = v, 1, 1000, "0.##")
            .Text("Equações de estaca (estaca atrás = estaca à frente; …)", () => equacoesTxt, v => equacoesTxt = v ?? "", false,
                "Ex.: 15+12,40 = 16+0,00; 40+0,00 = 39+15,00. Vazio = sem equações.");
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        est.Intervalo = intervalo;
        var problemas = new List<string>();
        if (est.Ler(inicioTxt) is { } ini) est.Inicio = ini;
        else problemas.Add($"estaca inicial \"{inicioTxt}\" não é uma estaca");
        problemas.AddRange(est.LerEquacoes(equacoesTxt, L));
        var graves = est.Problemas(L).Where(p => !p.Contains("repete estacas")).ToList();
        problemas.AddRange(graves);
        if (problemas.Count > 0)
        {
            TaskDialog.Show(AppTitle, "Estaqueamento não aplicado – nada foi alterado:\n• " + string.Join("\n• ", problemas));
            return Result.Cancelled;
        }
        var applied = Apply(uidoc, road, o, _replace, est).Where(r => r.Warnings.Count > 0).ToList();
        if (EditImpact.Of(uidoc.Document, new[] { road.Pavement }) is { } impact) applied.Add(impact);
        Report("Perfil da via", applied, alwaysShow: true);
        return Result.Succeeded;
    }

    /// <summary>
    /// Aplica o perfil: greide em toda a via, obras nos trechos que o terreno pede (substituindo as da via, se
    /// <paramref name="replace"/>) e terraplenagem. O primeiro resultado traz o resumo.
    /// </summary>
    /// <param name="estacas">Estaqueamento a gravar na via; nulo = manter o que ela tem.</param>
    internal static List<RenderResult> Apply(UIDocument uidoc, PickedRoad road, PerfilOpcoes o, bool replace, Estaqueamento? estacas = null)
    {
        var est = estacas ?? road.Pavement.Estaqueamento;
        var doc = uidoc.Document;
        var L = road.Axis.Length;
        var groundFn = RoadWorks.Ground(doc, road.BaseZ);
        var hasTerrain = TerrainModel.Hosts(doc).Count > 0;
        var hosted = RoadWorks.Hosted(doc, road.GroupId);
        var res = RoadProfileDesigner.Design(L, s => hasTerrain ? groundFn(road.Axis.PointAt(s)) : road.Grade.Z(s), o);
        var baseGrade = res.Grade;
        baseGrade.AdjustTerrain = true;
        // Os taludes por lado e por trecho da via não são desta janela: ficam como estão.
        baseGrade.Taludes = road.Grade.Taludes.Select(t => t.Clone()).ToList();
        var results = new List<RenderResult>();
        var keep = hosted;
        if (o.AutoStructures && replace && hosted.Count > 0)
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
        results.AddRange(RoadWorks.SetGrade(uidoc, road.GroupId, grade, created, est, estacas != null));
        // O pavimento gravado com o greide novo (o lido antes ainda tem o greide anterior, e a terraplenagem usa o que recebe).
        var gravado = RoadWorks.RoadByGroup(doc, road.GroupId)?.Pavement ?? road.Pavement;
        TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { gravado }.Concat(created).ToList());
        var summary = res.Summary() + (created.Count > 0 ? $"\n{created.Count} obra(s) criada(s) na via." : "\nNenhuma obra necessária: a via fica em aterro/corte no terreno.");
        // Elementos do greide aplicado (PIV, rampas, L, K, PCV/PTV, ponto alto/baixo) – um CSV por via, refeito a cada aplicação.
        try
        {
            Directory.CreateDirectory(PluginPaths.Root);
            var key = string.Concat((road.GroupId ?? road.Pavement.Id).Where(char.IsLetterOrDigit));
            var path = Path.Combine(PluginPaths.Root, $"greide-{key}.csv");
            File.WriteAllText(path, AlignmentElements.VerticalCsv(AlignmentElements.Vertical(grade), est), new System.Text.UTF8Encoding(true));
            var axisPath = Path.Combine(PluginPaths.Root, $"eixo-{key}.csv");
            File.WriteAllText(axisPath, AlignmentElements.AxisCsv(AlignmentElements.FromAxis(road.Axis), est), new System.Text.UTF8Encoding(true));
            summary += $"\nElementos do greide (PIV, rampas, L, K, PCV/PTV): {path}\nElementos do eixo (tangentes, curvas, raios, estacas): {axisPath}";
            // Limites geométricos informados pelo usuário (Configurações), com a fonte dele (K8): sem limites, nada é conferido.
            var crit = PluginContext.Settings.CriteriosGeometricos;
            if (crit is { IsEmpty: false })
            {
                var v = GeometricCheck.Axis(AlignmentElements.FromAxis(road.Axis), crit)
                    .Concat(GeometricCheck.Vertical(AlignmentElements.Vertical(grade), crit)).Select(x => x with { Estacas = est }).ToList();
                var checkPath = Path.Combine(PluginPaths.Root, $"verificacao-{key}.csv");
                File.WriteAllText(checkPath, GeometricCheck.Csv(v, crit), new System.Text.UTF8Encoding(true));
                summary += "\n" + GeometricCheck.Summary(v, crit) + $"\nVerificação do traçado e do greide: {checkPath}";
            }
            else summary += "\nLimites geométricos (raio, rampa, K) não informados em Configurações: traçado e greide não conferidos.";
        }
        catch (Exception ex)
        {
            Log.Error("Perfil da via – elementos do greide", ex);
        }
        results.Insert(0, new RenderResult());
        results[0].Warnings.Add(summary);
        results[0].Warnings.AddRange(res.Warnings);
        return results;
    }
}
