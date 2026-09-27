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

/// <summary>Formulários das ferramentas derivadas dos manuais (canalização em transição, cruzamento rodoferroviário).</summary>
internal static class ManualForms
{
    public static FormWindow Channelization(ChannelizationDefinition d, bool editing)
    {
        var w = new FormWindow(editing ? "Editar canalização" : "Canalização em transição (MTL / MAO / MAP)", "Marcas de canalização em transição",
                "Selecione ou desenhe a LINHA DE REFERÊNCIA no sentido do tráfego: o bordo da faixa antes da transição (o zebrado abre para a esquerda " +
                "ou para a direita conforme o sinal da variação de largura). Comprimentos pela velocidade – l = 0,5·V·d (MBST Vol. IV 6.2; DER/SP B.3) – todos editáveis.",
                d, () =>
                {
                    var total = ChannelizationGenerator.LocalOutline(d).Max(c => c.Max(p => p.S)) + 10;
                    var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(Math.Max(40, total), 0) });
                    var geo = MarkingBuilder.Build(d, axis, new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs });
                    var dd = Math.Abs(d.WidthChange) + (d.Type == TipoCanalizacao.Obstaculo ? d.Clearance : 0);
                    var road = d.Type switch
                    {
                        TipoCanalizacao.Obstaculo => Polygon2.Rectangle(new Vec2(0, -3.5 - dd), new Vec2(axis.Length, 3.5 + dd)),
                        _ => Polygon2.Rectangle(new Vec2(0, Math.Min(-3.5, -3.5 + Math.Min(0, d.WidthChange))), new Vec2(axis.Length, Math.Max(3.5, 3.5 + Math.Max(0, d.WidthChange)))),
                    };
                    var l = ChannelizationGenerator.EntryLength(d);
                    return new FormPreview(geo, new[] { road }, new[] { axis.Points },
                        $"Transição de {UiHelpers.F(l, "0.0")} m (l = 0,5·V·d = {UiHelpers.F(0.5 * d.Speed * Math.Abs(d.WidthChange), "0")} m). Barras a cada {UiHelpers.F(d.Gap > 0.01 ? d.Gap : DesignRules.ChannelHatchGap(d.Speed))} m.");
                }, true, editing ? "Aplicar" : "Criar", 1080, 720)
            .Choice("Tipo", new[]
            {
                ("MTL – transição de largura da pista (estreitamento / alargamento)", TipoCanalizacao.TransicaoLargura),
                ("MAO – aproximação de obstáculo ou ilha na pista", TipoCanalizacao.Obstaculo),
                ("MAP – acostamento pavimentado (início / fim / estreitamento)", TipoCanalizacao.Acostamento),
            }, () => d.Type, v => d.Type = v)
            .Number("Velocidade (km/h)", () => d.Speed, v => d.Speed = v, 10, 200, "0")
            .Number("Variação de largura d (m) – positiva à esquerda, negativa à direita do sentido", () => d.WidthChange, v => d.WidthChange = v, -30, 30,
                tooltip: "Quanto o bordo da faixa se desloca. MTL/MAP: largura suprimida ou acrescida; MAO: largura do obstáculo/ilha.")
            .Number("Comprimento da transição l (m) – 0 = calculado", () => d.Length, v => d.Length = v, 0, 1000, "0.0",
                tooltip: "l = 0,5·V·d. Mínimos junto a obstáculos: 30 m (urbana) e 60 m (rodovia).")
            .Number("Estação inicial ao longo da linha (m)", () => d.StartStation, v => d.StartStation = v, 0, 10000, "0.0")
            .Check("Rodovia (mínimos rurais)", () => d.Rural, v => d.Rural = v)
            .Section("Obstáculo (MAO)")
            .Number("Extensão do obstáculo ao longo da via (m)", () => d.ObstacleLength, v => d.ObstacleLength = v, 0.5, 500, "0.0")
            .Number("Afastamento lateral a (m) – 0,30 a 0,60", () => d.Clearance, v => d.Clearance = v, 0, 2)
            .Number("Transição de saída (m) – 0 = igual à de entrada", () => d.ExitLength, v => d.ExitLength = v, 0, 1000, "0.0")
            .Check("Obstáculo no eixo de via de mão dupla: área neutra dos dois lados", () => d.BothSides, v => d.BothSides = v)
            .Section("Acostamento (MAP)")
            .Number("Trecho tangente L (m) – 0 = ta pela velocidade (30/40/50 m)", () => d.TangentLength, v => d.TangentLength = v, 0, 1000, "0.0")
            .Section("Pintura")
            .Number("Largura das barras (m)", () => d.BarWidth, v => d.BarWidth = v, 0.05, 2)
            .Number("Espaçamento das barras (m) – 0 = 1,50 (V < 80) / 2,50", () => d.Gap, v => d.Gap = v, 0, 20)
            .Number("Largura da linha de canalização (m)", () => d.LineWidth, v => d.LineWidth = v, 0, 0.6)
            .Choice("Cor", new[] { ("Branca (fluxos no mesmo sentido)", MarkingColor.Branca), ("Amarela (fluxos opostos)", MarkingColor.Amarela) }, () => d.Color, v => d.Color = v);
        if (!editing)
            w.Modes(("Selecionar linha existente (associativo)", PathMode.Linhas), ("Desenhar a linha por pontos", PathMode.Desenhar),
                ("Dois cliques (início e fim, no sentido do tráfego)", PathMode.DoisPontos), ("Borda de piso / via (associativo)", PathMode.Bordas));
        return w;
    }

    public static FormWindow RailCrossing(RailCrossingSetup s, MarkingDefinition holder, double roadLength)
    {
        return new FormWindow("Cruzamento rodoferroviário", "Passagem em nível – sinalização completa",
                "Conjunto do MBST Vol. IX / Vol. IV (MCF) e DER/SP (projeto-tipo 13): linha de retenção dupla paralela ao trilho, retângulo de advertência " +
                "com a cruz de Santo André por faixa, linha dupla contínua na aproximação, PARE, LRV opcional e placas A-41 + R-1 (a 3,60 m do eixo da ferrovia) e A-39/A-40 antecipadas.",
                holder, () =>
                {
                    var half = Math.Max(120, s.WarningDistance + 60);
                    var axis = new Polyline2(new[] { new Vec2(-half, 0), new Vec2(half, 0) });
                    var tmp = new RailCrossingSetup
                    {
                        Station = half, RailDirection = s.RailDirection, RailHalfWidth = s.RailHalfWidth, Tracks = s.Tracks, TwoWay = s.TwoWay, RightWidth = s.RightWidth,
                        LeftWidth = s.LeftWidth, LanesPerDirection = s.LanesPerDirection, Speed = s.Speed, Barrier = s.Barrier, StopLineDistance = s.StopLineDistance,
                        RectangleDistance = s.RectangleDistance, RectangleLength = s.RectangleLength, LineWidth = s.LineWidth, NoPassingLength = s.NoPassingLength,
                        PareLegend = s.PareLegend, Lrv = s.Lrv, Signs = s.Signs, ConflictArea = s.ConflictArea,
                    };
                    var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
                    var geo = new MarkingGeometry();
                    foreach (var d in tmp.Build(PathReference.FromPoints(axis.Points, 0), axis, 0, new OutputSettings()))
                    {
                        var path = d.Path == null ? null : new Polyline2(d.Path.Points, d.Path.Closed);
                        geo.Merge(MarkingBuilder.Build(d, path, ctx));
                    }
                    var road = Polygon2.Rectangle(new Vec2(-half, -s.RightWidth - 2.5), new Vec2(half, s.LeftWidth + 2.5));
                    var rd = tmp.RailDirection is { } r && r.Length > 0.5 ? r.Normalized() : new Vec2(0, 1);
                    var guides = new List<IReadOnlyList<Vec2>> { axis.Points };
                    for (int t = 0; t < Math.Max(1, s.Tracks); t++)
                        foreach (var sg in new[] { -1, 1 })
                        {
                            var o = new Vec2((t - (s.Tracks - 1) / 2.0) * 2.0 + sg * s.RailHalfWidth, 0);
                            guides.Add(new[] { o - rd * 12, o + rd * 12 });
                        }
                    return new FormPreview(geo, new[] { road }, guides, $"Placa de advertência a {UiHelpers.F(s.WarningDistance, "0")} m da linha de retenção (desaceleração 2 m/s² + 10 m). Linhas finas = trilhos.");
                }, true, "Criar", 1120, 740)
            .Section("Via e ferrovia")
            .Number("Velocidade da via (km/h)", () => s.Speed, v => s.Speed = v, 10, 200, "0")
            .Check("Mão dupla", () => s.TwoWay, v => s.TwoWay = v)
            .Number("Largura da pista à direita do eixo (m)", () => s.RightWidth, v => s.RightWidth = v, 1, 30)
            .Number("Largura da pista à esquerda do eixo (m)", () => s.LeftWidth, v => s.LeftWidth = v, 1, 30)
            .Integer("Faixas por sentido", () => s.LanesPerDirection, v => s.LanesPerDirection = v, 1, 6)
            .Integer("Linhas férreas", () => s.Tracks, v => s.Tracks = v, 1, 6)
            .Number("Meia largura da linha férrea (m)", () => s.RailHalfWidth, v => s.RailHalfWidth = v, 0.4, 2, tooltip: "Do eixo ao trilho externo: 0,80 m (bitola larga 1,60 m); 0,50 m (métrica).")
            .Check("Com barreira / cancela (A-40 em vez de A-39)", () => s.Barrier, v => s.Barrier = v)
            .Section("Marcas no pavimento")
            .Number("Linha de retenção: distância ao trilho externo (m)", () => s.StopLineDistance, v => s.StopLineDistance = v, 1, 20, tooltip: "MBST: mínimo 3,00 m. DER/SP: 5,00 m sem cancela; 2,00 m antes da cancela.")
            .Number("Largura das linhas de retenção (m)", () => s.LineWidth, v => s.LineWidth = v, 0.3, 0.6)
            .Number("Retângulo de advertência: distância à linha de retenção (m)", () => s.RectangleDistance, v => s.RectangleDistance = v, 5, 300, "0", tooltip: "15 a 150 m conforme a velocidade e a visibilidade.")
            .Number("Retângulo de advertência: extensão (m)", () => s.RectangleLength, v => s.RectangleLength = v, 3, 30, "0")
            .Number("Linha dupla contínua: extensão antes da retenção (m) – 0 = até a placa de advertência", () => s.NoPassingLength, v => s.NoPassingLength = v, 0, 1000, "0")
            .Check("Legenda PARE por faixa", () => s.PareLegend, v => s.PareLegend = v)
            .Check("Linhas de redução de velocidade (LRV) na aproximação", () => s.Lrv, v => s.Lrv = v)
            .Check("Marcação de área de conflito (MAC) sobre a passagem", () => s.ConflictArea, v => s.ConflictArea = v)
            .Section("Placas")
            .Check("Placas A-41 + R-1 (junto à ferrovia), A-39/A-40 e R-19 antecipadas", () => s.Signs, v => s.Signs = v)
            .Hint(roadLength > 0 ? $"Eixo da via com {UiHelpers.F(roadLength, "0")} m; o conjunto se estende {UiHelpers.F(s.WarningDistance + s.StopLineDistance + 30, "0")} m para cada lado do cruzamento." : "");
    }
}

/// <summary>Marcas de canalização em transição (MTL, MAO, MAP) ao longo de uma linha de referência.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCanalizacao : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var d = UiHelpers.Remembered<ChannelizationDefinition>("Canalizacao") ?? new ChannelizationDefinition();
        d = (ChannelizationDefinition)d.CloneWithNewId();
        d.PathRef = new PathReference();
        d.Output = PluginContext.Settings.NewOutput();
        d.Speed = d.Speed <= 0 ? PluginContext.Settings.DefaultSpeed : d.Speed;
        var w = ManualForms.Channelization(d, false);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        UiHelpers.Remember("Canalizacao", d);
        PluginContext.SaveSettings();
        EnsureDetailView(uidoc, d.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) d.Output.SurfaceIds = s;
        return MarkingCreator.CreateAlongPath(uidoc, d, w.PathMode, d.DisplayCode, afterEach: x => FootprintCutter.ApplyFor(uidoc, x));
    }
}

/// <summary>Cruzamento rodoferroviário completo sobre uma via existente (clique o ponto de cruzamento no eixo).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCruzamentoFerroviario : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        var roads = svc.Roads();
        var pick = Picking.PickPoint(uidoc, "Clique o ponto onde a ferrovia cruza a via (sobre uma via criada pelo SinalizaBIM)");
        if (pick == null) return Result.Cancelled;
        var p = DetailHelpers.ToCore(pick);
        var best = roads.Select(r => (Road: r, Proj: IntersectionGenerator.Project(r.Axis, p))).OrderBy(x => x.Proj.Distance).FirstOrDefault();
        PathReference axisRef;
        Polyline2 axis;
        double station, z;
        var s = new RailCrossingSetup();
        try { if (PluginContext.Settings.Get("ferrovia:ultima") is { } j && System.Text.Json.JsonSerializer.Deserialize<RailCrossingSetup>(j) is { } last) s = last; }
        catch { /* configuração antiga */ }
        s.RailDirection = null;
        if (best.Road != null && best.Proj.Distance <= Math.Max(best.Road.Def.TotalLeft, best.Road.Def.TotalRight) + 2)
        {
            axisRef = best.Road.Def.PathRef;
            axis = best.Road.Axis;
            station = best.Proj.Station;
            z = axisRef.Z;
            s.RightWidth = best.Road.Def.RightWidth;
            s.LeftWidth = best.Road.Def.LeftWidth;
            s.TwoWay = best.Road.Def.TwoWay;
            s.Speed = best.Road.Def.Hierarchy is { } hh ? Hierarquia.DefaultSpeed(hh) : s.Speed;
            s.LanesPerDirection = Math.Max(1, (int)Math.Round((s.TwoWay ? best.Road.Def.RightWidth : best.Road.Def.RightWidth + best.Road.Def.LeftWidth) / 3.5));
        }
        else
        {
            // Sem via do SinalizaBIM: dois cliques definem o eixo da via no sentido do tráfego principal.
            TaskDialog.Show("SinalizaBIM", "Nenhuma via do SinalizaBIM neste ponto. Clique dois pontos do EIXO da via (o cruzamento fica no primeiro).");
            var a = Picking.PickPoint(uidoc, "Eixo da via: ponto do cruzamento com a ferrovia");
            var b = a == null ? null : Picking.PickPoint(uidoc, "Eixo da via: outro ponto (sentido do tráfego principal)");
            if (a == null || b == null) return Result.Cancelled;
            var (pts, zz) = Picking.ToCore(new[] { a, b });
            var dir = (pts[1] - pts[0]).Normalized();
            var reach = 400.0;
            var full = new List<Vec2> { pts[0] - dir * reach, pts[0] + dir * reach };
            axisRef = PathReference.FromPoints(full, zz);
            axis = new Polyline2(full);
            station = reach;
            z = zz;
        }
        // Direção da ferrovia: dois cliques opcionais ao longo do trilho.
        var r1 = Picking.PickPoint(uidoc, "Direção da ferrovia: clique um ponto sobre o trilho (ESC = perpendicular à via)");
        var r2 = r1 == null ? null : Picking.PickPoint(uidoc, "Direção da ferrovia: segundo ponto sobre o trilho");
        if (r1 != null && r2 != null)
        {
            var v = DetailHelpers.ToCore(r2) - DetailHelpers.ToCore(r1);
            if (v.Length > 0.5) s.RailDirection = v.Normalized();
        }
        s.Station = station;
        var holder = new LinearMarkingDefinition { Output = PluginContext.Settings.NewOutput() };
        var w = ManualForms.RailCrossing(s, holder, axis.Length);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        PluginContext.Settings.Set("ferrovia:ultima", System.Text.Json.JsonSerializer.Serialize(s));
        PluginContext.SaveSettings();
        EnsureDetailView(uidoc, holder.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } surf) holder.Output.SurfaceIds = surf;
        var defs = s.Build(axisRef, axis, z, holder.Output);
        var results = MarkingCreator.Commit(uidoc, defs, "SV - Cruzamento rodoferroviário");
        foreach (var d in defs.Where(d => d.Overlay)) FootprintCutter.ApplyOverlay(uidoc, d);
        Report("Cruzamento rodoferroviário", results);
        return Result.Succeeded;
    }
}
