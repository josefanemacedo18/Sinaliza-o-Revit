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

    /// <summary>Pisos e faixas que um dispositivo de drenagem substitui (pavimento, sarjeta, meio-fio, calçada, grama, pinturas).</summary>
    public static bool DrainageTarget(MarkingDefinition d) =>
        RoadMarking(d) || Sidewalk(d)
        || d is RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition or CurbExtensionDefinition or SidewalkAreaDefinition or PlanterDefinition
        || d is LinearMarkingDefinition l && (l.Code is "SARJETA" or "PLATAFORMA" or "SARJETAO" || l.Code.StartsWith("MEIO-FIO"));

    /// <summary>Recorta pavimento, sarjeta, meio-fio e calçada sob uma boca de lobo, grelha, PV ou canaleta.</summary>
    public static int ApplyDrainage(UIDocument uidoc, DrainageDefinition d)
    {
        var path = d.IsLinear && d.Path != null ? PathResolver.Resolve(uidoc.Document, d.Path)?.Main : null;
        var fp = d.CutFloors ? DrainageGenerator.Footprint(d, path) : new List<Polygon2>();
        var center = d.IsLinear ? path?.PointAt(path.Length / 2) ?? d.Position : d.Position;
        var radius = 45 + (path?.Length ?? 0) / 2;
        return Apply(uidoc, d, fp, DrainageTarget, center, radius);
    }

    /// <summary>Atualiza os recortes gerados por <paramref name="source"/>; devolve quantas marcas foram alteradas.</summary>
    /// <param name="near">Só examina marcas cujo eixo passa a menos de <paramref name="radius"/> m deste ponto (recorte local, rápido).</param>
    public static int Apply(UIDocument uidoc, MarkingDefinition source, IReadOnlyList<Polygon2> footprints, Func<MarkingDefinition, bool> filter,
        Vec2? near = null, double radius = 50)
    {
        var doc = uidoc.Document;
        var service = new MarkingService(doc, uidoc.ActiveView);
        var touched = new List<MarkingDefinition>();
        bool Close(MarkingDefinition d)
        {
            if (near is not { } c || d.Path == null) return true;
            var axis = PathResolver.Resolve(doc, d.Path)?.Main;
            return axis == null || axis.Points.Count < 2 || CurbFinder.Project(axis, c).Distance <= radius;
        }
        foreach (var d in MarkingStorage.Definitions(doc))
        {
            if (d.Id == source.Id || d is IAnnotationDefinition) continue;
            var had = d.Exclusions.RemoveAll(z => z.SourceId == source.Id) > 0;
            var hits = false;
            if (footprints.Count > 0 && filter(d) && Close(d))
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
                // A orelha substitui a pista, a pintura e o meio-fio antigo atrás da face (o dela contorna a frente).
                Apply(uidoc, ce, ce.CutRoadMarkings && path != null ? SidewalkGenerator.EarCutZone(ce, path) : Array.Empty<Polygon2>(),
                    d => RoadMarking(d) || d is LinearMarkingDefinition l && l.Code.StartsWith("MEIO-FIO"));
                break;
            case SidewalkAreaDefinition sa:
                Apply(uidoc, sa, One(sa.CutExisting && path != null ? SidewalkGenerator.AreaOutline(sa, path) : null), d => RoadMarking(d) || Sidewalk(d));
                break;
            case LinearMarkingDefinition { Code: "SARJETAO" } sj when path != null:
            {
                // O sarjetão substitui o pavimento e interrompe linhas, vagas e sarjetas sob ele.
                var cat = PluginContext.Catalog.Linear("SARJETAO");
                var variant = cat == null ? null : MarkingBuilder.ResolveVariant(cat, sj.Variant, sj.Speed);
                var w = sj.WidthOverride ?? variant?.Faixas.Max(f => f.Largura) ?? 1.0;
                var axis = RoadGenerator.Trimmed(path, sj.StartSetback, sj.EndSetback);
                if (Math.Abs(sj.Offset) > 1e-6) axis = axis.Offset(sj.Offset);
                Apply(uidoc, sj, SarjetaoGenerator.Footprint(axis, w),
                    d => RoadMarking(d) || d is RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition);
                break;
            }
            case PlanterDefinition pl:
                Apply(uidoc, pl, pl.CutSidewalk && path != null ? PolygonOps.Union(SidewalkGenerator.PlanterShapes(pl, path)) : Array.Empty<Polygon2>(), Sidewalk);
                break;
            case DrainageDefinition dr:
                ApplyDrainage(uidoc, dr);
                return;
            case TrafficCalmingDefinition { Type: TipoModeracao.LombadaInvertida } tc:
            {
                // A lombada invertida fica abaixo do pavimento: a pista (piso) e as linhas são recortadas sobre ela.
                var geo = new MarkingService(uidoc.Document, uidoc.ActiveView).BuildGeometry(tc, out _);
                var fp = PolygonOps.Offset(PolygonOps.Union(geo.Pieces.Select(p => p.Shape)), 0.01);
                Apply(uidoc, tc, fp, d => RoadMarking(d) || d is RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition);
                return;
            }
        }
        if (def.Overlay) ApplyOverlay(uidoc, def);
        else if (PaintedLine(def)) CutByOverlays(uidoc, def);
    }

    /// <summary>Linha longitudinal pintada (eixo, bordo, divisão) – interrompida pelas marcas que se sobrepõem.</summary>
    public static bool PaintedLine(MarkingDefinition d) =>
        d is LinearMarkingDefinition l && !l.Overlay && !SidewalkCodes.Contains(l.Code) && !IntersectionGenerator.IsPhysical(l)
        && PluginContext.Catalog.Linear(l.Code) is { Transversal: false } t && t.Grupo != Core.Catalog.GrupoMarca.Urbanizacao;

    /// <summary>Área ocupada por uma marca sobreposta (vãos entre as barras incluídos) + 10 cm.</summary>
    public static List<Polygon2> OverlayFootprint(MarkingGeometry geo)
    {
        var u = PolygonOps.Union(geo.Pieces.Select(p => p.Shape));
        return PolygonOps.Offset(PolygonOps.Offset(u, 0.6, true), -0.5, true).Where(p => p.Area > 0.05).ToList();
    }

    /// <summary>Marca "por cima": interrompe as linhas pintadas sob ela.</summary>
    public static int ApplyOverlay(UIDocument uidoc, MarkingDefinition def)
    {
        var geo = new MarkingService(uidoc.Document, uidoc.ActiveView).BuildGeometry(def, out _);
        return Apply(uidoc, def, OverlayFootprint(geo), PaintedLine);
    }

    /// <summary>Linha nova (ou editada) passando sob marcas sobrepostas existentes: recebe os recortes delas.</summary>
    public static void CutByOverlays(UIDocument uidoc, MarkingDefinition line)
    {
        var doc = uidoc.Document;
        var service = new MarkingService(doc, uidoc.ActiveView);
        var copy = MarkingDefinition.FromJson(line.ToJson())!;
        copy.Exclusions.Clear();
        var shapes = service.BuildGeometry(copy, out _).Pieces.Select(p => p.Shape).ToList();
        if (shapes.Count == 0) return;
        var changed = false;
        foreach (var o in MarkingStorage.Definitions(doc).Where(d => d.Overlay && d.Id != line.Id))
        {
            if (line.Exclusions.Any(z => z.SourceId == o.Id)) continue;
            foreach (var fp in OverlayFootprint(service.BuildGeometry(o, out _)))
            {
                if (PolygonOps.Intersect(shapes, new[] { fp }).Sum(p => p.Area) <= 1e-4) continue;
                line.Exclusions.Add(new ExclusionZone { SourceId = o.Id, Points = fp.Outer.ToList() });
                changed = true;
            }
        }
        if (changed) MarkingCreator.Commit(uidoc, new[] { line }, "SV - Recortar sob faixas");
    }
}

/// <summary>Formulários das ferramentas de calçada (criação e edição).</summary>
internal static class SidewalkForms
{
    private static readonly (string, TipoTransicao)[] Kinds =
    {
        ("Curvas reversas", TipoTransicao.Curva), ("Chanfro reto", TipoTransicao.Chanfro), ("Reta – acompanha a calçada / travessia", TipoTransicao.Reta),
    };

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

    private static Polyline2 CornerCurb()
    {
        var pts = new List<Vec2> { new(-16, 0), new(-6, 0) };
        pts.AddRange(CurveTools.Arc(new Vec2(-6, 6), 6, -Math.PI / 2, Math.PI / 2, 0.02).Skip(1));
        pts.Add(new Vec2(0, 16));
        return new Polyline2(pts);
    }

    public static FormWindow? CurbExtension(CurbExtensionDefinition d, bool edit)
    {
        var corner = false;
        var w = new FormWindow(edit ? "Editar orelha de calçada" : "Orelha de calçada", "Orelha / avanço de calçada",
            "Desenhe ou selecione a FACE DO MEIO-FIO existente no trecho do avanço – em linha reta (meio de quadra) ou contornando a " +
            "ESQUINA (selecione as linhas/arco da esquina ou desenhe os pontos em volta dela). A calçada fica à esquerda do sentido do " +
            "desenho (ou desmarque a opção). A orelha avança sobre o estacionamento, encurta a travessia e recebe meio-fio novo.",
            d, () =>
            {
                var path = corner ? CornerCurb() : Straight;
                if (!corner) return Street(d, path, d.SidewalkOnLeft ? 1 : -1);
                var geo = new MarkingGeometry();
                var block = new Polygon2(path.Points.Concat(new[] { new Vec2(0, 20), new Vec2(-20, 20), new Vec2(-20, 0) }));
                geo.Pieces.Add(new MarkingPiece(block, MarkingColor.Concreto));
                var c = (CurbExtensionDefinition)MarkingDefinition.FromJson(d.ToJson())!;
                c.SidewalkOnLeft = true;
                geo.Merge(Build(c, path));
                return new FormPreview(geo, new[] { Polygon2.Rectangle(new Vec2(-20, -9), new Vec2(9, 20)) }, new[] { path.Points });
            }, okText: edit ? "Aplicar" : "Inserir");
        w.Check("Prévia: orelha de esquina", () => corner, v => corner = v)
         .Number("Avanço sobre a pista (m)", () => d.Depth, v => d.Depth = v, 0.3, 10, tooltip: "Normalmente a largura da faixa de estacionamento (2,00–2,50 m).")
         .Choice("Ponta inicial", Kinds, () => d.Transition, v => d.Transition = v,
             tooltip: "Curva / chanfro: volta ao meio-fio junto ao estacionamento. Reta: ponta perpendicular, acompanhando a calçada ou a travessia.")
         .Number("Raio / comprimento da ponta inicial (m)", () => d.Radius, v => d.Radius = v, 0.1, 20)
         .Choice("Ponta final", Kinds, () => d.EndTransition ?? d.Transition, v => d.EndTransition = v)
         .Number("Raio / comprimento da ponta final (m)", () => d.EndRadius ?? d.Radius, v => d.EndRadius = v, 0.1, 20)
         .Number("Raio mínimo na esquina (m)", () => d.CornerRadius, v => d.CornerRadius = v, 0, 40,
             tooltip: "0 = acompanha a esquina (raio da esquina + avanço). Valores maiores suavizam o meio-fio da orelha.")
         .Number("Altura do meio-fio (m)", () => d.Height, v => d.Height = v, 0.02, 0.5)
         .Number("Largura do meio-fio (m)", () => d.CurbWidth, v => d.CurbWidth = v, 0.05, 0.5)
         .Check("Calçada existente à esquerda do sentido do desenho", () => d.SidewalkOnLeft, v => d.SidewalkOnLeft = v)
         .Section("Canteiro na orelha")
         .Check("Incluir canteiro gramado", () => d.Planter, v => d.Planter = v)
         .Number("Largura do canteiro (m)", () => d.PlanterWidth, v => d.PlanterWidth = v, 0.2, 10)
         .Number("Margem ao meio-fio e às transições (m)", () => d.PlanterMargin, v => d.PlanterMargin = v, 0, 5)
         .Integer("Árvores no canteiro", () => d.Trees, v => d.Trees = v, 0, 20)
         .Check("Recortar vagas e linhas da pista sob a orelha", () => d.CutRoadMarkings, v => d.CutRoadMarkings = v);
        if (!edit) w.Modes(("Selecionar as linhas da face do meio-fio (reta, esquina ou arco)", PathMode.Linhas),
            ("Desenhar a face do meio-fio por pontos (contornando a esquina)", PathMode.Desenhar),
            ("Dois cliques na face do meio-fio (trecho reto)", PathMode.DoisPontos));
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
         .Check("Seguir a composição da calçada da via (faixa de serviço gramada e sarjeta)", () => d.MatchRoadSection, v => d.MatchRoadSection = v,
             "Quando o cul-de-sac está ligado a uma via, lê a seção dela e continua a faixa de serviço e a sarjeta. Desligado: use os campos abaixo.")
         .Number("Faixa de serviço junto ao meio-fio (m, 0 = nenhuma)", () => d.ServiceStripWidth, v => d.ServiceStripWidth = v, 0, 5)
         .Check("Faixa de serviço gramada", () => d.ServiceStripGrass, v => d.ServiceStripGrass = v)
         .Number("Sarjeta junto ao meio-fio (m, 0 = nenhuma)", () => d.GutterWidth, v => d.GutterWidth = v, 0, 1.5)
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

    public static FormWindow? Tactile(TactileRouteDefinition d, bool edit)
    {
        var main = new Polyline2(new[] { new Vec2(0, 0), new Vec2(6, 0), new Vec2(6, 4), new Vec2(10, 7) });
        var branch = new Polyline2(new[] { new Vec2(3, -3), new Vec2(3, 0) });
        var w = new FormWindow(edit ? "Editar piso tátil" : "Piso tátil (NBR 16537)", "Sinalização tátil no piso",
            "Desenhe o percurso (eixo da faixa). Placas moduladas com relevo: direcional (barras no sentido do deslocamento) e alerta " +
            "(domos) nas mudanças de direção, nos extremos e nas junções entre trechos (selecione ou desenhe vários trechos de uma vez). " +
            "Confira as dimensões e a distribuição com a ABNT NBR 16537 vigente.",
            d, () =>
            {
                var chains = d.AlertOnly ? new[] { main } : new[] { main, branch };
                var geo = TactileGenerator.Route(d, chains, 0);
                return new FormPreview(geo, new[] { Polygon2.Rectangle(new Vec2(-2, -5), new Vec2(12, 9)) }, chains.Select(c => c.Points),
                    $"{geo.UnitCount} placa(s) de {UiHelpers.F(d.Module)} m");
            }, okText: edit ? "Aplicar" : "Inserir");
        w.Check("Somente faixa de alerta (sem direcional)", () => d.AlertOnly, v => d.AlertOnly = v, "Ex.: alerta junto a rebaixamentos, obstáculos suspensos, desníveis.")
         .Choice("Placa (módulo)", new[] { ("0,25 × 0,25 m", 0.25), ("0,40 × 0,40 m", 0.40), ("0,30 × 0,30 m", 0.30) }, () => d.Module, v => d.Module = v)
         .Integer("Fileiras (largura da faixa)", () => d.Rows, v => d.Rows = v, 1, 6, "Largura = fileiras × módulo (direcional usual: 0,25 a 0,60 m).")
         .Choice("Cor (contrastante com o piso)", UiHelpers.ColorItems().Skip(1).Where(c => c.Color is { } cc && MarkingColors.IsPaint(cc))
                .Select(c => (c.Label, c.Color!.Value)), () => d.Color, v => d.Color = v)
         .Check("Relevo (domos e barras) em 3D e planta", () => d.Relief, v => d.Relief = v, "Desative em projetos muito grandes para aliviar o modelo.")
         .Number("Nível de assentamento (m acima da pista)", () => d.Elevation, v => d.Elevation = v, -1, 3, tooltip: "Topo da calçada (0,15 m usual).")
         .Section("Alertas")
         .Check("Nas mudanças de direção", () => d.AlertAtTurns, v => d.AlertAtTurns = v)
         .Check("No início e no fim do percurso", () => d.AlertAtEnds, v => d.AlertAtEnds = v)
         .Check("Nas junções entre trechos (T)", () => d.AlertAtJunctions, v => d.AlertAtJunctions = v)
         .Integer("Lado do quadrado de alerta (placas)", () => d.AlertModules, v => d.AlertModules = v, 1, 6, "No mínimo a largura da direcional.")
         .Integer("Profundidade do alerta nos extremos (placas)", () => d.EndAlertModules, v => d.EndAlertModules = v, 1, 6);
        if (!edit) w.Modes(("Desenhar o percurso por pontos", PathMode.Desenhar), ("Selecionar linhas (vários trechos)", PathMode.Linhas), ("Dois cliques", PathMode.DoisPontos));
        return w;
    }

    public static FormWindow? Railway(RailwayDefinition d, bool edit)
    {
        var sample = new Polyline2(new[] { Vec2.Zero, new Vec2(12, 0) });
        var w = new FormWindow(edit ? "Editar via férrea" : "Via férrea", "Via férrea completa e personalizável",
            "Desenhe ou selecione o EIXO da via (entre as linhas, quando houver mais de uma). Gera a superestrutura completa em 3D: " +
            "sublastro, lastro com taludes, dormentes, placas de apoio e trilhos com perfil real – ou via em laje (fixação direta) / via " +
            "embutida no pavimento (VLT) –, com várias linhas, entrevia e valetas de drenagem. Nível zero: topo da plataforma " +
            "(via embutida: topo do pavimento). Use 'Cruzamento Rodoferroviário' para a sinalização da passagem em nível.",
            d, () =>
            {
                var geo = Build(d, sample);
                return new FormPreview(geo, null, new[] { sample.Points },
                    "Por km: " + RailwayGenerator.Summary(d, 1000) + (geo.Warnings.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", geo.Warnings.Distinct()) : ""));
            }, okText: edit ? "Aplicar" : "Inserir");
        w.Section("Tipo de via")
         .Choice("Superestrutura", new[]
            {
                ("Em lastro de brita (convencional)", TipoViaFerrea.Lastro), ("Em laje de concreto (fixação direta)", TipoViaFerrea.Laje),
                ("Embutida no pavimento (VLT / bonde)", TipoViaFerrea.Embutida),
            }, () => d.Type, v => d.Type = v)
         .Choice("Bitola", new[]
            {
                ("Larga – 1,600 m", BitolaFerroviaria.Larga), ("Métrica – 1,000 m", BitolaFerroviaria.Metrica),
                ("Padrão – 1,435 m (metrô / VLT)", BitolaFerroviaria.Padrao), ("Mista – 1,000 + 1,600 m (3 trilhos)", BitolaFerroviaria.Mista),
                ("Personalizada", BitolaFerroviaria.Personalizada),
            }, () => d.Gauge, v => d.Gauge = v)
         .Number("Bitola personalizada (m)", () => d.CustomGauge, v => d.CustomGauge = v, 0.5, 2.5, "0.000")
         .Choice("Perfil do trilho", new[]
            {
                ("TR-45", PerfilTrilho.TR45), ("TR-57", PerfilTrilho.TR57), ("TR-68 (carga pesada)", PerfilTrilho.TR68),
                ("UIC-60", PerfilTrilho.UIC60), ("Ri-60 – canaleta (via embutida)", PerfilTrilho.Ri60),
            }, () => d.Rail, v => d.Rail = v)
         .Integer("Número de linhas", () => d.Tracks, v => d.Tracks = v, 1, 8)
         .Number("Entrevia – eixo a eixo (m)", () => d.TrackSpacing, v => d.TrackSpacing = v, 2.5, 20, "0.00", "Usual: 4,00 a 5,25 m (carga), 3,50 a 4,00 m (metrô/VLT).")
         .Number("Deslocamento lateral do conjunto (m)", () => d.Offset, v => d.Offset = v, -50, 50, "0.00", "+ à esquerda do sentido do eixo.")
         .Section("Dormentes e fixações", "Via em lastro (o espaçamento vale também para as fixações da via em laje).")
         .Choice("Dormente", new[]
            {
                ("Concreto monobloco protendido", TipoDormente.ConcretoMonobloco), ("Concreto bibloco", TipoDormente.ConcretoBibloco),
                ("Madeira", TipoDormente.Madeira), ("Aço", TipoDormente.Aco),
            }, () => d.Sleeper, v => d.Sleeper = v)
         .Number("Espaçamento entre dormentes (m)", () => d.SleeperSpacing, v => d.SleeperSpacing = v, 0.3, 1.2, "0.00", "Usual 0,60 m (1.667 dormentes/km).")
         .Number("Comprimento do dormente (m, 0 = pela bitola)", () => d.SleeperLength, v => d.SleeperLength = v, 0, 4)
         .Number("Largura da base do dormente (m, 0 = pelo tipo)", () => d.SleeperWidth, v => d.SleeperWidth = v, 0, 0.5)
         .Number("Altura do dormente (m, 0 = pelo tipo)", () => d.SleeperHeight, v => d.SleeperHeight = v, 0, 0.4)
         .Check("Placas de apoio e fixações", () => d.Fastenings, v => d.Fastenings = v)
         .Section("Lastro, sublastro e drenagem")
         .Number("Altura do lastro sob o dormente (m)", () => d.BallastDepth, v => d.BallastDepth = v, 0.1, 1, "0.00", "Usual 0,30 m (carga).")
         .Number("Ombro do lastro (m)", () => d.BallastShoulder, v => d.BallastShoulder = v, 0, 1.5)
         .Number("Talude do lastro (H : 1 V)", () => d.BallastSlope, v => d.BallastSlope = v, 0.5, 4, "0.0")
         .Number("Espessura do sublastro (m, 0 = sem)", () => d.SubBallastDepth, v => d.SubBallastDepth = v, 0, 1)
         .Number("Sublastro além do pé do lastro (m)", () => d.SubBallastExtra, v => d.SubBallastExtra = v, 0, 5)
         .Check("Valetas de drenagem de concreto", () => d.Ditches, v => d.Ditches = v)
         .Number("Valeta – largura (m)", () => d.DitchWidth, v => d.DitchWidth = v, 0.3, 3)
         .Number("Valeta – profundidade (m)", () => d.DitchDepth, v => d.DitchDepth = v, 0.1, 2)
         .Section("Laje / via embutida")
         .Number("Espessura da laje de concreto (m)", () => d.SlabThickness, v => d.SlabThickness = v, 0.1, 1)
         .Number("Largura da laje / faixa por linha (m, 0 = automática)", () => d.SlabWidth, v => d.SlabWidth = v, 0, 10)
         .Choice("Revestimento da via embutida", new[]
            {
                ("Concreto", SuperficieViaEmbutida.Concreto), ("Asfalto", SuperficieViaEmbutida.Asfalto),
                ("Grama (via verde)", SuperficieViaEmbutida.Grama), ("Bloquete", SuperficieViaEmbutida.Bloquete),
            }, () => d.Surface, v => d.Surface = v)
         .Number("Topo do trilho em relação ao pavimento (m)", () => d.RailTopLevel, v => d.RailTopLevel = v, -0.1, 0.1, "0.000", "0 = rente ao piso.");
        if (!edit) w.Modes(("Desenhar o eixo por pontos", PathMode.Desenhar), ("Selecionar linhas existentes", PathMode.Linhas), ("Dois cliques", PathMode.DoisPontos));
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
            TactileRouteDefinition tr => Tactile(tr, true),
            RailwayDefinition rw => Railway(rw, true),
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
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        // Rápido: clique na esquina de uma interseção e informe quanto a orelha avança (sem desenhar linhas). ESC: modo livre.
        if (MarkingStorage.Definitions(uidoc.Document).OfType<IntersectionDefinition>().Any())
        {
            var pick = Picking.PickPoint(uidoc, "Orelha: clique na ESQUINA de uma interseção (perto do meio-fio da via em que ela avança) – ESC: desenhar/selecionar a face do meio-fio");
            if (pick != null)
            {
                var r = IntersectionEarEdit.AtCorner(uidoc, UnitConv.ToVec2(pick));
                if (r != null) return r.Value;
            }
        }
        return SidewalkCommandRunner.Run(uidoc, SidewalkCommandRunner.Last<CurbExtensionDefinition>(), d => SidewalkForms.CurbExtension((CurbExtensionDefinition)d, false));
    }
}

/// <summary>
/// Orelhas geradas pela interseção: por clique na esquina (avanço ao longo da via clicada e da outra, ou até o início do
/// estacionamento) e edição de uma orelha existente – as medidas ficam na interseção, que as refaz a cada atualização.
/// </summary>
internal static class IntersectionEarEdit
{
    private static double _along = 5.0, _other = 5.0;
    private static bool _toParking;

    private static (IntersectionDefinition It, IntersectionLayout L)? Find(UIDocument uidoc, Vec2 p, IntersectionDefinition? only = null)
    {
        var doc = uidoc.Document;
        var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
        var roads = svc.Roads();
        foreach (var it in (only != null ? new[] { only } : MarkingStorage.Definitions(doc).OfType<IntersectionDefinition>().ToArray())
                     .OrderBy(i => i.Node.DistanceTo(p)))
        {
            if (only == null && it.Node.DistanceTo(p) > 60) break;
            var rs = roads.Where(r => it.RoadIds.Contains(r.Def.Id)).ToList();
            if (rs.Count < 2) continue;
            try
            {
                var L = IntersectionGenerator.Layout((IntersectionDefinition)MarkingDefinition.FromJson(it.ToJson())!, rs);
                if (L.IsBend || L.Legs.Count < 3) continue;
                if (only == null && !L.Zone.Contains(p) && L.Zone.Outer.Min(v => v.DistanceTo(p)) > 15) continue;
                return (it, L);
            }
            catch (Exception ex) { Log.Error("Orelha na esquina", ex); }
        }
        return null;
    }

    /// <summary>Clique na esquina: nulo = não há interseção ali (segue o modo livre).</summary>
    public static Result? AtCorner(UIDocument uidoc, Vec2 p)
    {
        if (Find(uidoc, p) is not { } f || IntersectionGenerator.CornerAt(f.L, p) is not { } c) return null;
        var work = (IntersectionDefinition)MarkingDefinition.FromJson(f.It.ToJson())!;
        var fw = new FormWindow("Orelha na esquina", "Orelha na esquina da interseção",
            $"Esquina entre {IntersectionGenerator.LegLabel(f.L, c.A)} e {IntersectionGenerator.LegLabel(f.L, c.B)}. A orelha avança sobre a faixa de " +
            "estacionamento, contorna a curva da esquina e fica ligada à interseção (refeita quando ela é editada). A largura e as pontas valem para " +
            "todas as orelhas desta interseção.", null, null, false, "Aplicar", 640, 560);
        fw.Section("Avanço")
          .Number("Avança pela calçada a partir da esquina, na via clicada (m)", () => _along, v => _along = Math.Max(0.5, v), 0.5, 60,
              tooltip: "Medido na face do meio-fio a partir do fim da curva da esquina.")
          .Number("Avanço ao longo da outra via da esquina (m) – 0 = só na via clicada", () => _other, v => _other = v < 0.05 ? 0 : Math.Max(0.5, v), 0, 60)
          .Check("Terminar no início do estacionamento", () => _toParking, v => _toParking = v,
              "Lê a faixa de estacionamento de cada via: a orelha vai até a primeira vaga (5 m depois da travessia/retenção – CTB art. 181).");
        EarShape(fw, work);
        if (UiHelpers.ShowModal(fw) != true) return Result.Cancelled;
        CopyShape(f.It, work);
        IntersectionGenerator.SetCornerEar(f.It, f.L, c.A, c.NearA, _along, _other, _toParking);
        CommandBase.ReportResults("Orelha", IntersectionRunner.Run(uidoc, "SV - Orelha na esquina", s => s.Refresh(f.It)).Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }

    /// <summary>Editar uma orelha gerada pela interseção: ajustes da esquina dela e as medidas gerais das orelhas.</summary>
    public static Result Run(UIDocument uidoc, IntersectionDefinition parent, CurbExtensionDefinition? ear)
    {
        var mid = ear?.PathRef.Points is { Count: >= 2 } pts ? new Polyline2(pts).PointAt(new Polyline2(pts).Length / 2) : parent.Node;
        var f = Find(uidoc, mid, parent);
        var work = (IntersectionDefinition)MarkingDefinition.FromJson(parent.ToJson())!;
        var corner = f is { } ff ? IntersectionGenerator.CornerAt(ff.L, mid) : null;
        IntersectionLegSettings? set = corner is { } c0 && f is { } f0 ? IntersectionGenerator.LegSet(parent, f0.L, c0.A) : null;
        var remove = false;
        var lenA = set?.CurbExtensionLength ?? -1;
        var lenB = set?.CurbExtensionLengthOther ?? -1;
        bool? toParking = set?.CurbExtensionToParking;
        var fw = new FormWindow("Orelha da interseção", "Orelha gerada pela interseção",
            "Esta orelha é da interseção: as medidas abaixo ficam nela e a orelha é refeita a cada atualização (travessia e rampas na borda dela).",
            null, null, false, "Aplicar", 640, 600);
        if (corner != null)
            fw.Section("Esta esquina")
              .Check("Remover a orelha desta esquina", () => remove, v => remove = v)
              .Number("Avanço ao longo do ramo dono da esquina (m) – −1 = geral, 0 = sem orelha deste lado", () => lenA, v => lenA = v, -1, 60)
              .Number("Avanço ao longo da outra via (m) – −1 = o mesmo, 0 = sem orelha nela", () => lenB, v => lenB = v, -1, 60)
              .Choice("Terminar no início do estacionamento", new (string, bool?)[] { ("Geral da interseção", null), ("Sim", true), ("Não", false) },
                  () => toParking, v => toParking = v);
        IntersectionForms.EarSection(fw, work);
        if (UiHelpers.ShowModal(fw) != true) return Result.Cancelled;
        CopyShape(parent, work);
        parent.CurbExtensions = work.CurbExtensions;
        parent.CurbExtensionLength = work.CurbExtensionLength;
        parent.CurbExtensionToParking = work.CurbExtensionToParking;
        if (corner is { } c && f is { } fl)
        {
            var s = IntersectionGenerator.LegSetOrNew(parent, fl.L, c.A);
            s.CurbExtension = remove ? false : s.CurbExtension;
            s.CurbExtensionLength = lenA < -0.5 ? null : lenA < 0.05 ? 0 : Math.Max(0.5, lenA);
            s.CurbExtensionLengthOther = lenB < -0.5 ? null : lenB < 0.05 ? 0 : Math.Max(0.5, lenB);
            s.CurbExtensionToParking = toParking;
            parent.LegSettings.RemoveAll(x => x.IsEmpty);
        }
        CommandBase.ReportResults("Orelha", IntersectionRunner.Run(uidoc, "SV - Orelha da interseção", sv => sv.Refresh(parent)).Where(r => r.Warnings.Count > 0).ToList());
        return Result.Succeeded;
    }

    private static void EarShape(FormWindow fw, IntersectionDefinition d)
    {
        fw.Section("Forma (todas as orelhas desta interseção)")
          .Number("Largura do avanço (m) – 0 = a da faixa de estacionamento", () => d.CurbExtensionDepth ?? 0,
              v => d.CurbExtensionDepth = v < 0.05 ? null : Math.Clamp(v, 0.3, 10), 0, 10)
          .Choice("Forma das pontas", new[]
              {
                  ("Curvas reversas", TipoTransicao.Curva), ("Chanfro", TipoTransicao.Chanfro), ("Reta – acompanha a calçada", TipoTransicao.Reta),
              }, () => d.CurbExtensionEnds, v => d.CurbExtensionEnds = v)
          .Number("Raio da curva / comprimento do chanfro das pontas (m)", () => d.CurbExtensionEndRadius, v => d.CurbExtensionEndRadius = Math.Clamp(v, 0.1, 10), 0.1, 10);
    }

    private static void CopyShape(IntersectionDefinition to, IntersectionDefinition from)
    {
        to.CurbExtensionDepth = from.CurbExtensionDepth;
        to.CurbExtensionEnds = from.CurbExtensionEnds;
        to.CurbExtensionEndRadius = from.CurbExtensionEndRadius;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdAreaCalcada : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        SidewalkCommandRunner.Run(uidoc, SidewalkCommandRunner.Last<SidewalkAreaDefinition>(), d => SidewalkForms.Area((SidewalkAreaDefinition)d, false), closed: true);
}

/// <summary>Via férrea completa (lastro, laje ou embutida – VLT).</summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdFerrovia : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc) =>
        SidewalkCommandRunner.Run(uidoc, SidewalkCommandRunner.Last<RailwayDefinition>(), d => SidewalkForms.Railway((RailwayDefinition)d, false));
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
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        // Na ponta de uma via: o balão se liga a ela (acompanha o eixo e recorta a via). Senão, posicionamento livre.
        var doc = uidoc.Document;
        var pick = Picking.PickPoint(uidoc, "Cul-de-sac: clique na PONTA de uma via (balão nela) ou SOBRE uma via (rua sem saída saindo dela, com a mesma seção) – ESC: posicionar livremente");
        if (pick != null)
        {
            var svc = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
            var target = ConnectionPicker.Nearest(doc, svc, UnitConv.ToVec2(pick));
            if (target is { Kind: "ponta", Road: not null })
            {
                var d = target.CulDeSac != null
                    ? (CulDeSacDefinition)MarkingDefinition.FromJson(target.CulDeSac.ToJson())!
                    : SidewalkCommandRunner.Last<CulDeSacDefinition>();
                RoadConnection.FitCulDeSac(d, target.Road, 0);
                var form = SidewalkForms.CulDeSac(d, true);
                if (form == null || UiHelpers.ShowModal(form) != true) return Result.Cancelled;
                UiHelpers.Remember(nameof(CulDeSacDefinition), d);
                PluginContext.SaveSettings();
                Report("Cul-de-sac", IntersectionRunner.Run(uidoc, "SV - Cul-de-sac", s => s.AddCulDeSac(target.Road, target.AtEnd, d)).Where(r => r.Warnings.Count > 0).ToList());
                return Result.Succeeded;
            }
            // Clique sobre uma via (fora da ponta): rua sem saída saindo dela, com a mesma seção + balão.
            var p0 = UnitConv.ToVec2(pick);
            var svc2 = new IntersectionService(doc, new MarkingService(doc, uidoc.ActiveView));
            var host = svc2.Roads().Select(r => (Road: r, Proj: IntersectionGenerator.Project(r.Axis, p0)))
                .Where(x => x.Proj.Distance <= Math.Max(x.Road.Def.TotalLeft, x.Road.Def.TotalRight) + 1.0)
                .OrderBy(x => x.Proj.Distance).FirstOrDefault();
            if (host.Road != null) return BranchWithCulDeSac(uidoc, svc2, host.Road, host.Proj.Point, pick.Z);
            TaskDialog.Show(AppTitle, "Nenhuma via perto do ponto clicado – posicione o balão por dois cliques.");
        }
        return SidewalkCommandRunner.Run(uidoc, SidewalkCommandRunner.Last<CulDeSacDefinition>(), d => SidewalkForms.CulDeSac((CulDeSacDefinition)d, false));
    }

    /// <summary>
    /// Rua sem saída a partir de uma via existente: o ramal recebe exatamente os mesmos elementos da via (pista, linhas,
    /// sarjeta, meio-fio, faixa gramada, calçada), a interseção em T é feita com a via e o balão, com o mesmo perfil de
    /// calçada, fecha a outra ponta.
    /// </summary>
    private Result BranchWithCulDeSac(UIDocument uidoc, IntersectionService svc, IntersectionRoad host, Vec2 start, double zFt)
    {
        var doc = uidoc.Document;
        var endPick = Picking.PickPoint(uidoc, "Rua sem saída: clique o CENTRO do balão (fim da rua) – ESC cancela");
        if (endPick == null) return Result.Cancelled;
        var end = UnitConv.ToVec2(endPick);
        var d = SidewalkCommandRunner.Last<CulDeSacDefinition>();
        var minLen = Math.Max(host.Def.TotalLeft, host.Def.TotalRight) + Math.Max(d.BulbRadius, 6) + 8;
        if (start.DistanceTo(end) < minLen)
            throw new UserMessageException($"Rua sem saída muito curta: o centro do balão deve ficar a pelo menos {minLen:0} m do eixo da via.");
        d.MatchRoadSection = true;
        var form = SidewalkForms.CulDeSac(d, true);
        if (form == null || UiHelpers.ShowModal(form) != true) return Result.Cancelled;
        UiHelpers.Remember(nameof(CulDeSacDefinition), d);
        PluginContext.SaveSettings();

        // Eixo do ramal: do eixo da via existente até o centro do balão (linhas de modelo associativas).
        List<ElementId> ids;
        using (var t = new Transaction(doc, "SV - Eixo da rua sem saída"))
        {
            t.Start();
            ids = Picking.CreateAxis(doc, uidoc.ActiveView, RoadConnection.Fillet(new[] { start, end }, 0), zFt);
            t.Commit();
        }
        var uids = ids.Select(id => doc.GetElement(id).UniqueId).ToList();

        // Cópia exata da seção da via existente (todos os elementos no mesmo eixo dela).
        var key = RoadSectionInference.PathKey(host.Def.Path);
        var members = MarkingStorage.Definitions(doc)
            .Where(m => m.GroupId != null && m.GroupId == host.Def.GroupId && m.Path != null && RoadSectionInference.PathKey(m.Path) == key
                        && m is not (IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition))
            .ToList();
        if (members.Count == 0) members.Add(host.Def);
        var gid = Guid.NewGuid().ToString("N");
        var defs = new List<MarkingDefinition>();
        foreach (var m in members)
        {
            var c = m.CloneWithNewId();
            c.GroupId = gid;
            c.Exclusions.Clear();
            var pr = PathReference.FromElements(uids);
            pr.Z = host.Def.Path?.Z ?? pr.Z;
            c.SetPath(pr);
            if (c is LinearMarkingDefinition l) { l.StartSetback = 0; l.EndSetback = 0; }
            if (c is RoadPavementDefinition pv) { pv.StartSetback = 0; pv.EndSetback = 0; }
            defs.Add(c);
        }
        var results = MarkingCreator.Commit(uidoc, defs, "SV - Rua sem saída");
        var newPav = defs.OfType<RoadPavementDefinition>().FirstOrDefault();
        if (newPav != null)
        {
            var template = IntersectionService.AutoTemplate();
            template.Output = newPav.Output.Clone();
            var rb = UiHelpers.Remembered<RoundaboutDefinition>("Rotatoria") ?? new RoundaboutDefinition();
            try
            {
                results.AddRange(IntersectionRunner.Run(uidoc, "SV - Conexões da rua sem saída",
                    sv => sv.Connect(newPav, TipoConexao.Intersecao, FimLivre.CulDeSac, template, rb, d, radiusByHierarchy: true)).Where(r => r.Warnings.Count > 0));
            }
            catch (Exception ex)
            {
                Log.Error("Rua sem saída – conexões", ex);
                results.Add(new RenderResult { Geometry = null });
                results[^1].Warnings.Add("A interseção ou o balão não puderam ser criados: " + ex.Message);
            }
        }
        Report("Rua sem saída", results);
        return Result.Succeeded;
    }
}
