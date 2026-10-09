using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Base para os comandos que usam a janela de marcas lineares.</summary>
public abstract class LinearCommandBase : CommandBase
{
    protected abstract string WindowTitle { get; }
    protected abstract GrupoMarca[] Groups { get; }
    protected virtual PathMode DefaultMode => PathMode.Linhas;

    protected override Result Run(UIApplication app, UIDocument uidoc) => RunWindow(uidoc, WindowTitle, Groups, DefaultMode);

    public static Result RunWindow(UIDocument uidoc, string title, GrupoMarca[] groups, PathMode mode)
    {
        var w = new LinearWindow(title, groups, null, mode);
        if (UiHelpers.ShowModal(w) != true || w.Result == null) return Result.Cancelled;
        PluginContext.SaveSettings();
        var template = w.Result;
        EnsureDetailView(uidoc, template.Output);
        if (w.PickSurfaces)
        {
            var s = MarkingCreator.PickSurfaces(uidoc);
            if (s != null) template.Output.SurfaceIds = s;
        }
        return MarkingCreator.CreateAlongPath(uidoc, template, w.PathMode, template.Code,
            afterEach: d => FootprintCutter.ApplyFor(uidoc, d));
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdLinhaLongitudinal : LinearCommandBase
{
    protected override string WindowTitle => "Marcas longitudinais";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Longitudinal, GrupoMarca.Canalizacao, GrupoMarca.Estacionamento };
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdLinhaTransversal : LinearCommandBase
{
    protected override string WindowTitle => "Marcas transversais";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Transversal };
    protected override PathMode DefaultMode => PathMode.DoisPontos;
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdTachas : LinearCommandBase
{
    public const string Title = "Tachas e tachões (bloqueios físicos)";
    public static readonly GrupoMarca[] StudGroups = { GrupoMarca.Dispositivo };
    /// <summary>Chave da última tacha usada (a janela linear abre nela).</summary>
    public const string SettingsKey = "linear:Dispositivo";
    protected override string WindowTitle => Title;
    protected override GrupoMarca[] Groups => StudGroups;
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdPisoTatil : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        SidewalkCommandRunner.Run(uidoc, SidewalkCommandRunner.Last<TactileRouteDefinition>(), d => SidewalkForms.Tactile((TactileRouteDefinition)d, false));
}

/// <summary>Linhas avulsas de ciclovia (CIC-LD, CIC-FD, MCC...).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCicloviaLinhas : LinearCommandBase
{
    protected override string WindowTitle => "Ciclovias e ciclofaixas – marcas avulsas";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Ciclovia };
}

/// <summary>Ciclofaixa, ciclovia ou faixa de caminhada completa ao longo do eixo da faixa.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdCiclovia : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var st = PluginContext.Settings;
        BikeLaneSetup? last = null;
        try { if (st.Get("ciclo:ultima") is { } j) last = System.Text.Json.JsonSerializer.Deserialize<BikeLaneSetup>(j); }
        catch { /* configuração antiga */ }
        var s = last ?? new BikeLaneSetup();
        var holder = new LinearMarkingDefinition { Output = st.NewOutput(), Justify = s.Justify };
        var lastType = s.Type;
        var w = new FormWindow("Ciclovia / faixa de caminhada", "Ciclofaixa, ciclovia ou faixa de caminhada",
                "Desenhe ou selecione o EIXO da faixa. Fundo colorido contínuo, linhas de delimitação, linha central (bidirecional), " +
                "símbolos e setas espaçados e segregação física opcional – tudo num grupo, editável e associado ao eixo.",
                holder, () =>
                {
                    if (s.Type != lastType) { s.Width = BikeLaneSetup.DefaultWidth(s.Type); lastType = s.Type; }
                    var axis = new Core.Geometry.Polyline2(new[] { new Core.Geometry.Vec2(0, 0), new Core.Geometry.Vec2(24, 0) });
                    var ctx = new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs };
                    var geo = new Core.Model.MarkingGeometry();
                    var tmp = new BikeLaneSetup
                    {
                        Type = s.Type, Width = s.Width, Background = s.Background, RedContrastLines = s.RedContrastLines, ContrastWidth = s.ContrastWidth,
                        WalkColor = s.WalkColor, Lines = s.Lines, DashedLines = s.DashedLines,
                        LineWidth = s.LineWidth, CenterDash = s.CenterDash, CenterGap = s.CenterGap, SymbolSpacing = Math.Min(s.SymbolSpacing, 10),
                        SymbolLength = s.SymbolLength, Arrows = s.Arrows, ArrowLength = s.ArrowLength, Segregation = s.Segregation,
                        Justify = holder.Justify == Justificacao.Clique ? Justificacao.Esquerda : holder.Justify,
                    };
                    foreach (var d in tmp.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings())) geo.Merge(MarkingBuilder.Build(d, axis, ctx));
                    var road = Core.Geometry.Polygon2.Rectangle(new Core.Geometry.Vec2(0, -s.Width / 2 - 1.5), new Core.Geometry.Vec2(24, s.Width / 2 + 1.5));
                    return new FormPreview(geo, new[] { road }, new[] { axis.Points }, "Pré-visualização com símbolos a cada 10 m.");
                }, true, "Criar", 1080, 720)
            .Choice("Tipo", new[]
                {
                    ("Ciclofaixa unidirecional", TipoCiclo.CiclofaixaUnidirecional), ("Ciclofaixa bidirecional", TipoCiclo.CiclofaixaBidirecional),
                    ("Ciclovia segregada", TipoCiclo.Ciclovia), ("Faixa de caminhada (pedestres)", TipoCiclo.FaixaCaminhada),
                }, () => s.Type, v => s.Type = v)
            .Number("Largura total (m)", () => s.Width, v => s.Width = v, 0.8, 6, tooltip: "Unidirecional ≥ 1,20 m (recomendado 1,50 m); bidirecional ≥ 2,50 m.")
            .Check("Pintura de fundo (vermelha / azul-verde)", () => s.Background, v => s.Background = v)
            .Check("Sem fundo: linha vermelha de contraste por dentro da linha branca (CET-SP Vol. 13, padrão II)", () => s.RedContrastLines, v => s.RedContrastLines = v,
                tooltip: "Padrão II da CET: faixa branca de 0,25 m acompanhada de faixa vermelha de 0,15 m pelo lado interno, em vez da pintura total do fundo.")
            .Number("Largura da linha vermelha de contraste (m)", () => s.ContrastWidth, v => s.ContrastWidth = v, 0.05, 0.5)
            .Choice("Cor da faixa de caminhada", new[] { ("Azul", Core.Model.MarkingColor.Azul), ("Verde", Core.Model.MarkingColor.Verde) }, () => s.WalkColor, v => s.WalkColor = v)
            .Section("Linhas")
            .Choice("Linha de delimitação", new[] { ("À esquerda do eixo", LadoLinha.Esquerda), ("À direita do eixo", LadoLinha.Direita), ("Dos dois lados", LadoLinha.Ambos), ("Sem linha", LadoLinha.Nenhum) },
                () => s.Lines, v => s.Lines = v, tooltip: "Lado da pista dos veículos, olhando no sentido em que o eixo foi desenhado.")
            .Check("Linha de delimitação seccionada (1 × 1 m)", () => s.DashedLines, v => s.DashedLines = v)
            .Number("Largura da linha (m)", () => s.LineWidth, v => s.LineWidth = v, 0.05, 0.3)
            .Number("Bidirecional: traço da linha central (m)", () => s.CenterDash, v => s.CenterDash = v, 0.3, 6)
            .Number("Bidirecional: espaço da linha central (m)", () => s.CenterGap, v => s.CenterGap = v, 0.3, 6)
            .Section("Símbolos e segregação")
            .Number("Símbolos a cada (m, 0 = sem)", () => s.SymbolSpacing, v => s.SymbolSpacing = v, 0, 500)
            .Number("Tamanho do símbolo (m)", () => s.SymbolLength, v => s.SymbolLength = v, 0.3, 4)
            .Check("Setas de sentido", () => s.Arrows, v => s.Arrows = v)
            .Number("Tamanho da seta (m)", () => s.ArrowLength, v => s.ArrowLength = v, 0.3, 4)
            .Choice("Segregação física", new[] { ("Nenhuma", (string?)null), ("Tartarugas (segregador)", "SEG-CIC"), ("Balizadores flexíveis", "BAL-FLEX"), ("Tachões", "TACHAO-SEG") },
                () => s.Segregation, v => s.Segregation = v)
            .Section("Presa à via e cruzamentos")
            .Check("Prender a uma via existente: entra na seção da via clicada (lado + afastamento do meio-fio)", () => s.OnRoad, v => s.OnRoad = v,
                tooltip: "A faixa passa a fazer parte da seção transversal: acompanha curvas e mudanças de largura e é refeita com a via, as interseções e as pranchas. Clique na via do lado em que ela deve ficar.")
            .Number("Afastamento do meio-fio (m)", () => s.CurbOffset, v => s.CurbOffset = v, 0, 10,
                tooltip: "Da face do meio-fio à borda externa da ciclofaixa. Estacionamento que caiba nele fica por fora; a diferença vira faixa de segurança zebrada.")
            .Check("A faixa de rolamento vizinha cede a largura (meio-fio no lugar)", () => s.TakeLane, v => s.TakeLane = v,
                tooltip: "Desmarcado (ou se a faixa ficar com menos de 2,70 m [a confirmar]): a pista alarga e o meio-fio se afasta.")
            .Check("Cruzamentos: pintura colorida entre os quadrados da MCC", () => s.CrossingColor, v => s.CrossingColor = v)
            .Choice("Cor da pintura do cruzamento", new[] { ("Vermelha", Core.Model.MarkingColor.Vermelha), ("Verde", Core.Model.MarkingColor.Verde), ("Azul", Core.Model.MarkingColor.Azul) },
                () => s.CrossingColorValue, v => s.CrossingColorValue = v)
            .Number("Zona de conflito antes do cruzamento – linha seccionada (m)", () => s.ConflictZone, v => s.ConflictZone = v, 0, 200,
                tooltip: "Onde os veículos que convertem cruzam a ciclofaixa a linha de delimitação fica seccionada (padrão 20 m [a confirmar]; 0 = contínua).")
            .Modes(("Selecionar linhas existentes (eixo da faixa)", PathMode.Linhas), ("Desenhar o eixo por pontos", PathMode.Desenhar));
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        s.Justify = holder.Justify;
        st.Set("ciclo:ultima", System.Text.Json.JsonSerializer.Serialize(s));
        PluginContext.SaveSettings();
        if (s.OnRoad) return OnRoad(uidoc, s);
        EnsureDetailView(uidoc, holder.Output);

        s.Justify = holder.Justify;
        var path = MarkingCreator.PickPath(uidoc, w.PathMode, s.Justify == Justificacao.Centro ? "Eixo da faixa" : "Borda da faixa");
        if (path == null) return Result.Cancelled;
        var justify = s.Justify;
        if (s.Justify == Justificacao.Clique)
        {
            var side = MarkingCreator.PickSide(uidoc, path);
            if (side == null) return Result.Cancelled;
            s.Justify = side.Value;
        }
        var defs = s.Build(path, holder.Output);
        s.Justify = justify;
        Report("Ciclovia", MarkingCreator.Commit(uidoc, defs, "SV - Ciclovia"));
        return Result.Succeeded;
    }

    /// <summary>Ciclofaixa presa à via: entra na seção da via clicada (lado do clique + afastamento) e a via é refeita.</summary>
    private static Result OnRoad(UIDocument uidoc, BikeLaneSetup s)
    {
        var pt = Picking.PickPoint(uidoc, "Ciclofaixa na via: clique sobre a via, do lado em que a faixa deve ficar");
        if (pt == null) return Result.Cancelled;
        var p = UnitConv.ToVec2(pt);
        if (RoadExtensionCommand.RoadAt(uidoc, p) is not { } road)
            throw new UserMessageException("Nenhuma via do plugin junto ao ponto clicado.");
        var pav = road.Def;
        var setup = RoadTemplates.FromJson(pav.SetupJson)
                    ?? throw new UserMessageException("Esta via não guardou a seção transversal (versão anterior ou Pista): use Editar → A via inteira uma vez e depois insira a ciclofaixa.");
        var left = road.Axis.Project(p).Signed > 0;
        var msgs = setup.InserirCiclofaixa(left, s.ToElemento(), s.CurbOffset, s.TakeLane);
        var results = new List<RenderResult>();
        using (var tg = new Autodesk.Revit.DB.TransactionGroup(uidoc.Document, "SV - Ciclofaixa na via"))
        {
            tg.Start();
            results.AddRange(RoadAccessCommand.Regenerate(uidoc, pav, setup));
            tg.Assimilate();
        }
        ReportResults("Ciclofaixa na via", results);
        if (msgs.Count > 0) TaskDialog.Show(AppTitle, string.Join("\n", msgs));
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdCalcadas : LinearCommandBase
{
    protected override string WindowTitle => "Urbanização – meios-fios, sarjetas, sarjetões, calçadas, canteiros e faixas de caminhada";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Urbanizacao };
}
