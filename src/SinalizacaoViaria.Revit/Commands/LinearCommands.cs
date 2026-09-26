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

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var w = new LinearWindow(WindowTitle, Groups, null, DefaultMode);
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
            afterEach: d => { if (d is LinearMarkingDefinition { Code: "SARJETAO" }) FootprintCutter.ApplyFor(uidoc, d); });
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
    protected override string WindowTitle => "Tachas e tachões";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Dispositivo };
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
                        Type = s.Type, Width = s.Width, Background = s.Background, WalkColor = s.WalkColor, Lines = s.Lines, DashedLines = s.DashedLines,
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
            .Modes(("Selecionar linhas existentes (eixo da faixa)", PathMode.Linhas), ("Desenhar o eixo por pontos", PathMode.Desenhar));
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        s.Justify = holder.Justify;
        st.Set("ciclo:ultima", System.Text.Json.JsonSerializer.Serialize(s));
        PluginContext.SaveSettings();
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
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdCalcadas : LinearCommandBase
{
    protected override string WindowTitle => "Urbanização – meios-fios, sarjetas, sarjetões, calçadas, canteiros e faixas de caminhada";
    protected override GrupoMarca[] Groups => new[] { GrupoMarca.Urbanizacao };
}
