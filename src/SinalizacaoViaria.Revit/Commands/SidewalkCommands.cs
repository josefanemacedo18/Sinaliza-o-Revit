using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Recorta outras marcas sob o contorno de um elemento (rampas, orelhas, canteiros...).</summary>
internal static class FootprintCutter
{
    public static readonly string[] SidewalkCodes = { "CALCADA", "GRAMADO", "MEIO-FIO", "MEIO-FIO-12", "MEIO-FIO-ALTO", "MEIO-FIO-SARJ" };

    /// <summary>Marcas pintadas/dispositivos da pista.</summary>
    public static bool RoadMarking(MarkingDefinition d) => d switch
    {
        LinearMarkingDefinition l => !SidewalkCodes.Contains(l.Code),
        HatchMarkingDefinition or ParkingMarkingDefinition or RepeatedMarkingDefinition or SymbolMarkingDefinition
            or TextMarkingDefinition or DeviceMarkingDefinition => true,
        _ => false,
    };

    public static bool Sidewalk(MarkingDefinition d) =>
        d is LinearMarkingDefinition l && SidewalkCodes.Contains(l.Code)
        || d is SidewalkAreaDefinition { Type: TipoAreaCalcada.Calcada or TipoAreaCalcada.Canteiro };

    /// <summary>Atualiza os recortes gerados por <paramref name="source"/>; devolve quantas marcas foram alteradas.</summary>
    public static int Apply(UIDocument uidoc, MarkingDefinition source, IReadOnlyList<Polygon2> footprints, Func<MarkingDefinition, bool> filter)
    {
        var doc = uidoc.Document;
        var service = new MarkingService(doc, uidoc.ActiveView);
        var touched = new List<MarkingDefinition>();
        foreach (var d in MarkingStorage.Definitions(doc))
        {
            if (d.Id == source.Id || d is IAnnotationDefinition) continue;
            var had = d.Exclusions.RemoveAll(z => z.SourceId == source.Id) > 0;
            var hits = false;
            if (footprints.Count > 0 && filter(d))
            {
                var copy = MarkingDefinition.FromJson(d.ToJson())!;
                copy.Exclusions.Clear();
                var shapes = service.BuildGeometry(copy, out _).Pieces.Select(p => p.Shape).ToList();
                foreach (var fp in footprints)
                {
                    if (PolygonOps.Intersect(shapes, new[] { fp }).Sum(p => p.Area) <= 1e-4) continue;
                    d.Exclusions.Add(new ExclusionZone { SourceId = source.Id, Points = fp.Outer.ToList() });
                    hits = true;
                }
            }
            if (had || hits) touched.Add(d);
        }
        if (touched.Count > 0) MarkingCreator.Commit(uidoc, touched, "SV - Recortar marcas");
        return touched.Count;
    }

    private static IReadOnlyList<Polygon2> One(Polygon2? p) => p == null ? Array.Empty<Polygon2>() : new[] { p };

    /// <summary>Recortes das ferramentas de calçada, conforme o tipo e as opções.</summary>
    public static void ApplyFor(UIDocument uidoc, MarkingDefinition def)
    {
        var path = def.Path == null ? null : PathResolver.Resolve(uidoc.Document, def.Path)?.Main;
        switch (def)
        {
            case CurbExtensionDefinition ce:
                Apply(uidoc, ce, One(ce.CutRoadMarkings && path != null ? SidewalkGenerator.EarFootprint(ce, path) : null), RoadMarking);
                break;
            case SidewalkAreaDefinition sa:
                Apply(uidoc, sa, One(sa.CutExisting && path != null ? SidewalkGenerator.AreaOutline(sa, path) : null), d => RoadMarking(d) || Sidewalk(d));
                break;
            case PlanterDefinition pl:
                Apply(uidoc, pl, pl.CutSidewalk && path != null ? PolygonOps.Union(SidewalkGenerator.PlanterShapes(pl, path)) : Array.Empty<Polygon2>(), Sidewalk);
                break;
        }
    }
}

/// <summary>Formulários das ferramentas de calçada (criação e edição).</summary>
internal static class SidewalkForms
{
    private static readonly Polyline2 Straight = new(new[] { Vec2.Zero, new Vec2(12, 0) });

    private static MarkingGeometry Build(MarkingDefinition d, Polyline2 path) =>
        MarkingBuilder.Build(d, path, new BuildContext { Catalog = PluginContext.Catalog, Glyphs = PluginContext.Glyphs });

    private static FormPreview Street(MarkingDefinition d, Polyline2 path, double walkSide = 1, double walk = 3)
    {
        var geo = new MarkingGeometry();
        var (mn, mx) = (new Vec2(-2, 0), new Vec2(path.Points.Max(p => p.X) + 2, 0));
        geo.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(mn.X, Math.Min(0, walkSide * walk)), new Vec2(mx.X, Math.Max(0, walkSide * walk))), MarkingColor.Concreto));
        geo.Merge(Build(d, path));
        var road = Polygon2.Rectangle(new Vec2(mn.X, walkSide > 0 ? -7 : 0), new Vec2(mx.X, walkSide > 0 ? 0 : 7));
        return new FormPreview(geo, new[] { road }, new[] { path.Points });
    }

    public static FormWindow? CurbExtension(CurbExtensionDefinition d, bool edit)
    {
        var w = new FormWindow(edit ? "Editar orelha de calçada" : "Orelha de calçada", "Orelha / avanço de calçada",
            "Desenhe ao longo da FACE DO MEIO-FIO existente, no trecho do avanço. A calçada fica à esquerda do sentido do desenho " +
            "(ou marque o contrário). A orelha avança sobre a faixa de estacionamento, encurta a travessia e recebe meio-fio novo.",
            d, () => Street(d, Straight, d.SidewalkOnLeft ? 1 : -1), okText: edit ? "Aplicar" : "Inserir");
        w.Number("Avanço sobre a pista (m)", () => d.Depth, v => d.Depth = v, 0.3, 10, tooltip: "Normalmente a largura da faixa de estacionamento (2,00–2,50 m).")
         .Choice("Transição", new[] { ("Curvas reversas", TipoTransicao.Curva), ("Chanfro reto", TipoTransicao.Chanfro) }, () => d.Transition, v => d.Transition = v)
         .Number("Raio / comprimento da transição (m)", () => d.Radius, v => d.Radius = v, 0.1, 20)
         .Number("Altura do meio-fio (m)", () => d.Height, v => d.Height = v, 0.02, 0.5)
         .Number("Largura do meio-fio (m)", () => d.CurbWidth, v => d.CurbWidth = v, 0.05, 0.5)
         .Check("Calçada existente à esquerda do sentido do desenho", () => d.SidewalkOnLeft, v => d.SidewalkOnLeft = v)
         .Section("Canteiro na orelha")
         .Check("Incluir canteiro gramado", () => d.Planter, v => d.Planter = v)
         .Number("Largura do canteiro (m)", () => d.PlanterWidth, v => d.PlanterWidth = v, 0.2, 10)
         .Number("Margem ao meio-fio e às transições (m)", () => d.PlanterMargin, v => d.PlanterMargin = v, 0, 5)
         .Integer("Árvores no canteiro", () => d.Trees, v => d.Trees = v, 0, 20)
         .Check("Recortar vagas e linhas da pista sob a orelha", () => d.CutRoadMarkings, v => d.CutRoadMarkings = v);
        if (!edit) w.Modes(("Selecionar a linha da face do meio-fio", PathMode.Linhas), ("Dois cliques na face do meio-fio (início e fim)", PathMode.DoisPontos));
        return w;
    }

    public static FormWindow? Area(SidewalkAreaDefinition d, bool edit)
    {
        var sample = new Polyline2(new[] { new Vec2(0, 0), new Vec2(9, 0), new Vec2(9, -3), new Vec2(5, -5.5), new Vec2(0, -5.5) }, true);
        var w = new FormWindow(edit ? "Editar área de calçada" : "Área de calçada / canteiro", "Área por contorno",
            "Desenhe um contorno fechado: avanços de esquina, ilhas, canteiros, parklets, ciclovia no nível da calçada ou faixa de serviço. " +
            "Os cantos podem ser arredondados automaticamente.",
            d, () =>
            {
                var geo = Build(d, sample);
                return new FormPreview(geo, new[] { Polygon2.Rectangle(new Vec2(-2, -8), new Vec2(11, 2)) }, new[] { sample.Points.Append(sample.Points[0]).ToList() });
            }, okText: edit ? "Aplicar" : "Inserir");
        w.Choice("Tipo", new[]
            {
                ("Calçada / avanço elevado (concreto)", TipoAreaCalcada.Calcada), ("Canteiro gramado elevado", TipoAreaCalcada.Canteiro),
                ("Ciclovia no nível da calçada (vermelha)", TipoAreaCalcada.Ciclovia), ("Parklet / área de estar (deck)", TipoAreaCalcada.Deck),
                ("Faixa de serviço ajardinada", TipoAreaCalcada.FaixaServico), ("Pavimento (asfalto)", TipoAreaCalcada.Pavimento),
            }, () => d.Type, v => d.Type = v)
         .Number("Altura (m)", () => d.Height, v => d.Height = v, 0, 1, tooltip: "Altura do topo em relação à pista (ciclovia: nível da calçada).")
         .Check("Meio-fio no contorno", () => d.Curb, v => d.Curb = v)
         .Number("Largura do meio-fio (m)", () => d.CurbWidth, v => d.CurbWidth = v, 0.05, 0.5)
         .Number("Raio de arredondamento dos cantos (m)", () => d.FilletRadius, v => d.FilletRadius = v, 0, 30)
         .Check("Recortar calçadas, gramados e marcas da pista sob a área", () => d.CutExisting, v => d.CutExisting = v);
        if (!edit) w.Modes(("Desenhar o contorno por pontos", PathMode.Desenhar), ("Selecionar linhas que formam o contorno fechado", PathMode.Linhas));
        return w;
    }

    public static FormWindow? Planter(PlanterDefinition d, bool edit)
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(20, 0) });
        var w = new FormWindow(edit ? "Editar canteiros" : "Canteiros na calçada", "Canteiros, jardineiras e grelhas de árvore",
            "Desenhe a linha da faixa de serviço (eixo dos canteiros) na calçada. Os canteiros são distribuídos ao longo dela, centralizados.",
            d, () => Street(d, path, 1, 3.5), okText: edit ? "Aplicar" : "Inserir");
        w.Choice("Tipo", new[]
            {
                ("Canteiro gramado (nível da calçada)", TipoCanteiroCalcada.Gramado), ("Jardineira elevada com mureta", TipoCanteiroCalcada.Jardineira),
                ("Grelha de proteção de árvore", TipoCanteiroCalcada.GrelhaArvore),
            }, () => d.Type, v => d.Type = v)
         .Number("Comprimento de cada canteiro (m)", () => d.Length, v => d.Length = v, 0.3, 100)
         .Number("Largura (m)", () => d.Width, v => d.Width = v, 0.2, 10)
         .Number("Espaçamento entre centros (m)", () => d.Spacing, v => d.Spacing = v, 0, 100, tooltip: "0 = faixa contínua.")
         .Number("Deslocamento lateral (m)", () => d.Offset, v => d.Offset = v, -20, 20, tooltip: "+ à esquerda do sentido do desenho.")
         .Number("Altura da calçada (m)", () => d.SurfaceHeight, v => d.SurfaceHeight = v, 0, 1)
         .Number("Largura da guia/mureta (m)", () => d.BorderWidth, v => d.BorderWidth = v, 0.03, 0.5)
         .Number("Altura da mureta – jardineira (m)", () => d.BorderHeight, v => d.BorderHeight = v, 0.05, 1.5)
         .Check("Árvores", () => d.Trees, v => d.Trees = v)
         .Check("Recortar a calçada sob os canteiros", () => d.CutSidewalk, v => d.CutSidewalk = v);
        if (!edit) w.Modes(("Selecionar linhas", PathMode.Linhas), ("Desenhar a linha por pontos", PathMode.Desenhar), ("Dois cliques", PathMode.DoisPontos));
        return w;
    }

    public static FormWindow? CulDeSac(CulDeSacDefinition d, bool edit)
    {
        var axis = new Polyline2(new[] { Vec2.Zero, new Vec2(0, 18) });
        var w = new FormWindow(edit ? "Editar cul-de-sac" : "Cul-de-sac", "Cul-de-sac (balão de retorno)",
            "1º clique: início do balão sobre o eixo da via; 2º clique: centro do balão (ou fim da via, nos tipos T, Y e L). " +
            "Gera pavimento, meio-fio, calçada, ilha central e linha de bordo.",
            d, () =>
            {
                var geo = Build(d, axis);
                return new FormPreview(geo, new[] { Polygon2.Rectangle(new Vec2(-30, -4), new Vec2(30, 40)) }, new[] { axis.Points },
                    $"Diâmetro de giro até o meio-fio: {UiHelpers.F(2 * d.BulbRadius, "0.0")} m");
            }, okText: edit ? "Aplicar" : "Inserir");
        w.Choice("Tipo", new[]
            {
                ("Circular", TipoCulDeSac.Circular), ("Circular excêntrico à esquerda", TipoCulDeSac.ExcentricoEsquerda),
                ("Circular excêntrico à direita", TipoCulDeSac.ExcentricoDireita), ("Gota (balão alongado)", TipoCulDeSac.Gota),
                ("Em \"T\" (martelo)", TipoCulDeSac.Martelo), ("Em \"Y\"", TipoCulDeSac.EmY),
                ("Em \"L\" à esquerda", TipoCulDeSac.EmLEsquerda), ("Em \"L\" à direita", TipoCulDeSac.EmLDireita),
            }, () => d.Type, v => d.Type = v)
         .Number("Largura da pista (m)", () => d.RoadWidth, v => d.RoadWidth = v, 3, 30)
         .Number("Raio do balão até o meio-fio (m)", () => d.BulbRadius, v => d.BulbRadius = v, 3, 60)
         .Number("Raio de concordância (m)", () => d.TransitionRadius, v => d.TransitionRadius = v, 0, 50)
         .Number("Martelo: comprimento da cabeça (m)", () => d.HeadLength, v => d.HeadLength = v, 6, 80)
         .Number("Y / L: comprimento dos ramos (m)", () => d.BranchLength, v => d.BranchLength = v, 3, 60)
         .Number("Y: ângulo dos ramos (°)", () => d.BranchAngle, v => d.BranchAngle = v, 15, 80, "0")
         .Section("Calçada e ilha")
         .Number("Largura da calçada (m)", () => d.SidewalkWidth, v => d.SidewalkWidth = v, 0, 20)
         .Number("Largura do meio-fio (m)", () => d.CurbWidth, v => d.CurbWidth = v, 0.05, 0.5)
         .Number("Altura do meio-fio (m)", () => d.Height, v => d.Height = v, 0.02, 0.5)
         .Check("Ilha central ajardinada", () => d.Island, v => d.Island = v)
         .Number("Raio da ilha (m)", () => d.IslandRadius, v => d.IslandRadius = v, 0.5, 40)
         .Check("Pavimento asfáltico", () => d.Pavement, v => d.Pavement = v)
         .Number("Espessura do pavimento (m)", () => d.PavementThickness, v => d.PavementThickness = v, 0.01, 1)
         .Check("Linha de bordo (LBO) a 0,30 m do meio-fio", () => d.EdgeLine, v => d.EdgeLine = v);
        if (!edit) w.Modes(("Dois cliques (início e centro/fim)", PathMode.DoisPontos), ("Selecionar a linha do eixo", PathMode.Linhas));
        return w;
    }

    /// <summary>Janela de edição para as definições de calçada (null se o tipo não for daqui).</summary>
    public static (FormWindow Window, MarkingDefinition Working)? ForEdit(MarkingDefinition def)
    {
        var working = MarkingDefinition.FromJson(def.ToJson())!;
        FormWindow? w = working switch
        {
            CurbExtensionDefinition ce => CurbExtension(ce, true),
            SidewalkAreaDefinition sa => Area(sa, true),
            PlanterDefinition pl => Planter(pl, true),
            CulDeSacDefinition cd => CulDeSac(cd, true),
            _ => null,
        };
        return w == null ? null : (w, working);
    }
}

internal static class SidewalkCommandRunner
{
    public static Result Run(UIDocument uidoc, MarkingDefinition template, Func<MarkingDefinition, FormWindow?> form, bool closed = false)
    {
        var w = form(template);
        if (w == null || UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        CommandBase.EnsureDetailViewPublic(uidoc, template.Output);
        if (w.PickSurfaces && MarkingCreator.PickSurfaces(uidoc) is { } s) template.Output.SurfaceIds = s;
        UiHelpers.Remember(template.GetType().Name, template);
        PluginContext.SaveSettings();
        return MarkingCreator.CreateAlongPath(uidoc, template, w.PathMode, template.DisplayCode, closed, d => FootprintCutter.ApplyFor(uidoc, d));
    }

    public static T Last<T>() where T : MarkingDefinition, new()
    {
        var d = UiHelpers.Remembered<T>(typeof(T).Name) ?? new T();
        d.Output = PluginContext.Settings.NewOutput();
        return d;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdOrelha : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        SidewalkCommandRunner.Run(uidoc, SidewalkCommandRunner.Last<CurbExtensionDefinition>(), d => SidewalkForms.CurbExtension((CurbExtensionDefinition)d, false));
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdAreaCalcada : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        SidewalkCommandRunner.Run(uidoc, SidewalkCommandRunner.Last<SidewalkAreaDefinition>(), d => SidewalkForms.Area((SidewalkAreaDefinition)d, false), closed: true);
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdCanteiro : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        SidewalkCommandRunner.Run(uidoc, SidewalkCommandRunner.Last<PlanterDefinition>(), d => SidewalkForms.Planter((PlanterDefinition)d, false));
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdCulDeSac : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        SidewalkCommandRunner.Run(uidoc, SidewalkCommandRunner.Last<CulDeSacDefinition>(), d => SidewalkForms.CulDeSac((CulDeSacDefinition)d, false));
}
