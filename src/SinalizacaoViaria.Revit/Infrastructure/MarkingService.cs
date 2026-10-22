using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Resultado da geração de uma marca no Revit.</summary>
public sealed class RenderResult
{
    public List<ElementId> Elements { get; } = new();
    public List<string> Warnings { get; } = new();
    public MarkingGeometry? Geometry { get; set; }
    public bool Success => Elements.Count > 0;
}

/// <summary>
/// Converte definições paramétricas em elementos do Revit e os mantém sincronizados:
/// um elemento por cor (DirectShape 3D ou Região preenchida 2D), todos com a definição gravada.
/// Todos os métodos exigem uma transação aberta.
/// </summary>
public sealed class MarkingService
{
    private readonly Document _doc;
    private readonly View? _activeView;
    /// <summary>Escala usada na geração no lugar da escala da vista (ajuste de escala: desenho na escala antiga e na nova).</summary>
    private double? _scaleOverride;
    private readonly bool _interactive;
    private StyleService? _styles;

    /// <param name="interactive">Falso quando chamado pelo atualizador automático (não cria vistas nem pergunta nada).</param>
    public MarkingService(Document doc, View? activeView = null, bool interactive = true)
    {
        _doc = doc;
        _activeView = activeView;
        _interactive = interactive;
        // Placas personalizadas guardadas no projeto entram no catálogo antes de gerar.
        CustomSignStore.Apply(doc);
    }

    private StyleService Styles => _styles ??= new StyleService(_doc);
    private Dictionary<string, MarkingDefinition>? _definitions;

    /// <summary>Definições do documento (cache por geração – detalhes e legendas consultam as demais marcas).</summary>
    private Dictionary<string, MarkingDefinition> Definitions =>
        _definitions ??= MarkingStorage.Definitions(_doc).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());

    private Dictionary<string, MarkingDefinition?>? _byId;

    /// <summary>
    /// Definição pelo id para os geradores (via, interseção e detalhe consultam as marcas ligadas): do cache geral se ele já
    /// existe; senão só a marca pedida é interpretada. Depois de 8 consultas o cache geral, que lê tudo de uma vez, compensa.
    /// </summary>
    private MarkingDefinition? Lookup(string id)
    {
        if (_definitions != null) return _definitions.GetValueOrDefault(id);
        _byId ??= new Dictionary<string, MarkingDefinition?>();
        if (_byId.TryGetValue(id, out var d)) return d;
        if (_byId.Count >= 8) return Definitions.GetValueOrDefault(id);
        return _byId[id] = MarkingStorage.Definition(_doc, id);
    }

    private readonly Dictionary<string, MarkingGeometry?> _geometryCache = new();

    /// <summary>Invalida o cache de definições (após gravar outras marcas na mesma transação).</summary>
    public void Invalidate()
    {
        _definitions = null;
        _nodeParents = null;
        _byId = null;
        _present = null;
        _geometryCache.Clear();
    }

    private Dictionary<string, List<(Element Element, MarkingColor Color)>>? _present;

    /// <summary>Elementos existentes de cada marca (por id).</summary>
    private Dictionary<string, List<(Element Element, MarkingColor Color)>> Present => _present ??= MarkingStorage.Elements(_doc);

    /// <summary>
    /// Geometria para quantitativos: só o que existe no modelo. Cores cujos elementos foram apagados saem, e pisos
    /// entram com a área real (inclusive editados à mão).
    /// </summary>
    public MarkingGeometry QuantityGeometry(MarkingDefinition d)
    {
        var geo = BuildGeometry(d, out _);
        return FilterPresent(d, geo);
    }

    private MarkingGeometry FilterPresent(MarkingDefinition d, MarkingGeometry geo)
    {
        if (!Present.TryGetValue(d.Id, out var els) || els.Count == 0) return geo;
        // Placa num elemento só: todas as cores estão nele.
        if (ElementPlan.SingleElement(d)) return geo;
        var colors = els.Select(e => e.Color).ToHashSet();
        var res = new MarkingGeometry { PaintedLength = geo.PaintedLength, PathLength = geo.PathLength, UnitCount = geo.UnitCount };
        res.Annotations.AddRange(geo.Annotations);
        res.Warnings.AddRange(geo.Warnings);
        res.Pieces.AddRange(geo.Pieces.Where(p => colors.Contains(p.Color)));
        foreach (var g in els.GroupBy(e => e.Color))
            if (g.All(e => e.Element is Floor))
                res.AreaOverrides[g.Key] = g.Sum(e => FloorArea((Floor)e.Element));
        // Unidades/extensão ficam com o elemento principal: se todos os elementos de uma marca com várias cores
        // sumiram, a marca inteira sumiu (não chega aqui).
        return res;
    }

    /// <summary>Geometria sem detalhes (null para detalhes ou em caso de erro) – prévias de quadros.</summary>
    public MarkingGeometry? BuildGeometryOrNull(MarkingDefinition d) => OtherGeometry(d);

    /// <summary>Contexto de geração com o projeto inteiro (marcas, geometrias e eixos) – listas e prévias dos detalhes.</summary>
    public BuildContext ProjectContext(View? view) => PluginContext.BuildContext(false, view?.Scale ?? 100,
        Lookup, () => Definitions.Values.ToList(), OtherGeometry, d => PathResolver.Resolve(_doc, d.Path)?.Main);

    /// <summary>Geometria das demais marcas (cotas de seção, quadros de quantitativos) – sem detalhes, para não haver recursão.</summary>
    private MarkingGeometry? OtherGeometry(MarkingDefinition d)
    {
        if (d is IAnnotationDefinition) return null;
        var key = d.Id + "|" + d.ToJson().GetHashCode();
        if (_geometryCache.TryGetValue(key, out var g)) return g;
        try { g = FilterPresent(d, BuildGeometry(d, out _)); }
        catch (Exception ex) { Log.Error($"OtherGeometry {d.DisplayCode}", ex); g = null; }
        _geometryCache[key] = g;
        return g;
    }

    /// <summary>
    /// Ordena para gerar primeiro as marcas e depois os detalhes/anotações que dependem delas; dentro de cada grupo, pelo grafo
    /// de dependências (<see cref="Core.Model.ModelGraph.RegenerationSort"/>).
    /// </summary>
    public static List<MarkingDefinition> DependencyOrder(IEnumerable<MarkingDefinition> defs) => Core.Model.ModelGraph.RegenerationSort(defs);

    /// <summary>Regenera os detalhes/anotações que apontam para as marcas indicadas (e os quadros de legenda).</summary>
    public List<RenderResult> RenderDependents(IEnumerable<string> markingIds, bool includeLegends)
    {
        var ids = markingIds.ToHashSet();
        _definitions = null;
        _nodeParents = null;
        _byId = null;
        // Só os detalhes e anotações do projeto são interpretados (o tipo vem do começo do JSON gravado).
        var deps = MarkingStorage.Definitions<IAnnotationDefinition>(_doc).Cast<MarkingDefinition>().Where(d => d is IAnnotationDefinition a && !ids.Contains(d.Id)
            && (a.TargetId != null ? ids.Contains(a.TargetId) : includeLegends && d is IProjectWideAnnotation)).ToList();
        var res = new List<RenderResult>();
        foreach (var d in DependencyOrder(deps))
        {
            try { res.Add(Render(d)); }
            catch (Exception ex) { Log.Error($"RenderDependents {d.DisplayCode}", ex); }
        }
        return res;
    }

    /// <summary>
    /// Trechos de rede (F8) ligados aos dispositivos regenerados: o tubo acompanha a caixa que mudou de lugar, de cota ou de
    /// medidas (a ponta do tubo é a parede da caixa).
    /// </summary>
    public List<RenderResult> RenderNetwork(IEnumerable<string> deviceIds)
    {
        var ids = deviceIds.ToHashSet();
        _definitions = null;
        _nodeParents = null;
        _byId = null;
        var res = new List<RenderResult>();
        foreach (var p in MarkingStorage.Definitions<DrainagePipeDefinition>(_doc).Where(p => !ids.Contains(p.Id) && (ids.Contains(p.FromId) || ids.Contains(p.ToId))))
        {
            try { res.Add(Render(p)); }
            catch (Exception ex) { Log.Error($"RenderNetwork {p.DisplayCode}", ex); }
        }
        return res;
    }

    private static Catalogo Catalog => PluginContext.Catalog;

    // ------------------------------------------------------------------ geometria

    /// <summary>Gera a geometria (sem tocar no modelo) – usado também em quantitativos e prévias.</summary>
    private bool? _hasTopo;

    /// <summary>Há Toposolid (terreno nativo) no projeto – as obras deixam aterros e cortes para a terraplenagem dele.</summary>
    public bool HasToposolid => _hasTopo ??= new FilteredElementCollector(_doc).OfClass(typeof(Toposolid)).Any();

    /// <summary>Esquece o cache de "há Toposolid" (depois de criar um terreno).</summary>
    public void ResetTerrainCache() => _hasTopo = null;

    public MarkingGeometry BuildGeometry(MarkingDefinition def, out double baseZ, List<string>? warnings = null, View? view = null)
    {
        using var perf = Perf.Measure("Geometria do núcleo (BuildGeometry)");
        var geo = BuildGeometryCore(def, out baseZ, warnings, view);
        // Nó no relevo: rampas, ilhas, meios-fios e dispositivos acompanham vértice a vértice as FACES DOS PISOS do nó
        // (retalhamento comum) – encostam na calçada e na sarjeta em volta sem fresta nem degrau; fora da malha, a superfície do
        // nó. Antes seguiam a superfície teórica, a até 2 cm das faces planas dos pisos.
        if (def.Output.Mode == OutputMode.Modelo3D && NodeFor(def) is { } node)
        {
            var b = baseZ;
            var owner = NodeOwner(def);
            var tiling = owner != null ? TryTiling() : null;
            double Abs(Vec2 v) => (owner != null ? tiling?.NodeZ(owner.Id, v) : null) ?? node.Z(v);
            GradeLift.Apply(geo, v => Abs(v) - b);
            if (def is RampDefinition ramp && PathResolver.Resolve(_doc, ramp.Path)?.Main is { Points.Count: >= 2 } rp)
                RampSlopeWarning(geo, ramp, rp, Abs);
        }
        return geo;
    }

    /// <summary>Retalhamento comum dos pisos, ou nulo se não der para montar (as peças seguem a superfície teórica).</summary>
    private FloorTiling? TryTiling()
    {
        try { return Tiling(); }
        catch (Exception ex)
        {
            Log.Error("Retalhamento comum dos pisos", ex);
            return null;
        }
    }

    /// <summary>Rampa numa rua inclinada: aviso quando a inclinação total passa de 8,33 % (RampGenerator.SlopeOnRelief).</summary>
    private static void RampSlopeWarning(MarkingGeometry geo, RampDefinition ramp, Polyline2 path, Func<Vec2, double> z)
    {
        if (RampGenerator.SlopeOnRelief(ramp, path, z).Warning is { } w) geo.Warnings.Add(w);
    }

    private MarkingGeometry BuildGeometryCore(MarkingDefinition def, out double baseZ, List<string>? warnings, View? view)
    {
        // Obras que acompanham a topografia leem o terreno natural (Toposolid) relativo à base da marca.
        var groundBase = def.Path == null ? def.PointZ ?? 0 : PathResolver.Resolve(_doc, def.Path)?.Z ?? 0;
        Func<Vec2, double?>? ground = null;
        if (def is ITerrainAware { FollowTerrain: true } ta && (ta.GroundLine == null || ta.GroundLine.Count < 2 || def is InterchangeDefinition or IHostedStructure { HostRoad: not null }))
            ground = TerrainFunction(groundBase);
        // Terra é sempre do terreno nativo (Toposolid): aterros, cortes, saias e reaterros nunca viram sólidos – a terraplenagem os
        // constrói no Toposolid (criado automaticamente quando o projeto não tem um).
        var native = true;
        var ctx = PluginContext.BuildContext(def.Output.Drape && def.Output.Mode == OutputMode.Modelo3D,
            _scaleOverride ?? view?.Scale ?? 100, Lookup, () => Definitions.Values.ToList(), OtherGeometry,
            d => PathResolver.Resolve(_doc, d.Path)?.Main, ground, native, d => PathResolver.Resolve(_doc, d.Path)?.Z ?? 0);
        if (def.Path == null)
        {
            baseZ = def.PointZ ?? 0;
            return MarkingBuilder.Build(def, null, ctx);
        }

        var path = PathResolver.Resolve(_doc, def.Path);
        baseZ = path?.Z ?? 0;
        if (path != null) warnings?.AddRange(path.Warnings);
        if (path == null || path.Chains.Count == 0) return MarkingBuilder.Build(def, null, ctx);

        // Rampas e moderadores usam apenas os extremos do primeiro trecho.
        if (def is RampDefinition or TrafficCalmingDefinition or CulDeSacDefinition or CurbExtensionDefinition)
        {
            var g1 = MarkingBuilder.Build(def, path.Main, ctx);
            // Na via com greide (fora de nós), rampas e moderadores acompanham vértice a vértice as faces dos pisos da via.
            if (Graded(def) && path.Main != null && NodeFor(def) == null)
            {
                GradeLift.Apply(g1, RoadLift(def, path.Main));
                if (def is RampDefinition ramp) RampSlopeWarning(g1, ramp, path.Main, RoadLift(def, path.Main));
            }
            return g1;
        }

        if (def is TactileRouteDefinition route)
        {
            // Todas as linhas juntas: as junções entre trechos recebem alerta.
            var g = TactileGenerator.Route(route, path.Chains, route.Elevation);
            return route.Exclusions.Count == 0 ? g : MarkingBuilder.ApplyExclusions(g, route.Exclusions);
        }

        // Zebrado por contorno: todos os contornos juntos – o interno vira furo (anel em volta de uma rotatória).
        if (def is HatchMarkingDefinition { IsStrip: false } hatch)
            return MarkingBuilder.BuildHatchArea(hatch, path.Chains.Where(c => c.Points.Count >= 3).ToList(), ctx);

        var graded = Graded(def) && path.Main != null;
        var geo = new MarkingGeometry();
        foreach (var chain in path.Chains)
        {
            // Com greide, o eixo é refinado (2 m): barreiras, dispositivos e sólidos varridos acompanham as curvas verticais.
            var c = graded ? new Polyline2(CurveTools.Densify(chain.Points, 2.0), chain.Closed) : chain;
            geo.Merge(MarkingBuilder.Build(def, c, ctx));
        }
        if (graded) GradeLift.Apply(geo, RoadLift(def, path.Main!));
        return geo;
    }

    /// <summary>
    /// Cota (relativa à base da via) das peças sólidas de uma marca da via: as faces dos pisos dela (retalhamento comum) –
    /// barreiras, dispositivos, rampas e moderadores assentam nos pisos em volta; sem a malha, o greide teórico.
    /// </summary>
    private Func<Vec2, double> RoadLift(MarkingDefinition def, Polyline2 axis)
    {
        var surf = SurfaceFor(def, axis);
        var road = RoadOf(def);
        var tiling = road?.Output.Grade != null ? TryTiling() : null;
        var roadBase = road != null ? PathResolver.Resolve(_doc, road.Path)?.Z ?? 0 : 0;
        if (tiling == null || road == null) return surf.Z;
        return v => tiling.RoadZ(road.Id, v) is { } z ? z - roadBase : surf.Z(v);
    }

    /// <summary>Marca da via que acompanha o greide (as obras hospedadas já aplicam o greide na própria geração).</summary>
    public static bool Graded(MarkingDefinition def) =>
        def.Output.Grade is { IsFlat: false } && def.Path != null && def is not ITerrainAware && def is not IAnnotationDefinition
        && def.Output.Mode == OutputMode.Modelo3D;

    /// <summary>Greide da via como superfície de apoio de pisos e pinturas (nulo = sem greide).</summary>
    private ISurface? GradeFor(MarkingDefinition def, double baseZ)
    {
        var g = GradeForCore(def, baseZ);
        // Calçada com níveis variáveis: a superfície de apoio ganha o perfil de níveis da faixa.
        if (def.Output.Mode == OutputMode.Modelo3D && def is LinearMarkingDefinition { LevelProfile.Count: > 0 } lm
            && PathResolver.Resolve(_doc, def.Path)?.Main is { Points.Count: >= 2 } axis
            && Core.Automation.SidewalkLevels.Function(lm, axis, Catalog) is { } f)
            return new LevelSampler(g, baseZ, f);
        return g;
    }

    private ISurface? GradeForCore(MarkingDefinition def, double baseZ)
    {
        if (def.Output.Mode == OutputMode.Modelo3D && NodeFor(def) is { } node)
        {
            if (NodeOwner(def) is not { } owner || owner.Id == def.Id) return new NodeSampler(node);
            return new NodeSampler(node, NodeRoads(node, owner.Id));
        }
        if (!Graded(def)) return null;
        var axis = PathResolver.Resolve(_doc, def.Path)?.Main;
        return axis == null ? null : new GradeSampler(SurfaceFor(def, axis), baseZ);
    }

    /// <summary>
    /// Superfície das marcas de um nó: a do nó dentro dele e, fora, a da via em que a peça está (com a concordância) –
    /// rampas, extensões e faixas que passam da borda do nó ficam sobre a calçada e a pista da via, sem degrau.
    /// </summary>
    /// <summary>Superfície (cota absoluta, m) de uma marca do nó (NodeSurface.ForMembers) – nula se a marca não é de um nó com greide.</summary>
    public Func<Vec2, double>? MemberSurface(MarkingDefinition def)
    {
        if (NodeOwner(def) is not { } owner || owner.Id == def.Id || NodeFor(def) is not { } node) return null;
        return node.ForMembers(NodeRoads(node, owner.Id));
    }

    private List<NodeSurface.MemberRoad> NodeRoads(NodeSurface node, string ownerId)
    {
        var roads = new List<NodeSurface.MemberRoad>();
        foreach (var leg in node.Legs)
        {
            if (leg.Id == null || !Definitions.TryGetValue(leg.Id, out var d) || d is not RoadPavementDefinition pav) continue;
            if (PathResolver.Resolve(_doc, pav.Path) is not { Main: { Points.Count: >= 2 } axis } path || pav.Output.Grade == null) continue;
            var surf = SurfaceFor(pav, axis);
            // Trecho da via ocupado pelo nó: o recorte dele na via (o mesmo que a concordância usa).
            var cut = pav.Exclusions.Where(e => e.SourceId == ownerId).SelectMany(e => e.Points).Select(q => surf.Locate(q).S).ToList();
            if (cut.Count == 0) continue;
            roads.Add(new NodeSurface.MemberRoad(surf, path.Z, pav.Id, cut.Min(), cut.Max(), Math.Max(pav.TotalLeft, pav.TotalRight) + 3));
        }
        return roads;
    }

    private readonly Dictionary<string, GradeSurface> _surfaces = new();

    /// <summary>
    /// Superfície do greide de uma marca da via: abaulamento só na largura da pista (calçadas em nível na transversal) e
    /// concordância com os nós em que a via é SECUNDÁRIA – nos primeiros metros depois do nó ela parte exatamente da
    /// superfície dele (a da via principal) e passa suavemente para o próprio greide.
    /// </summary>
    public GradeSurface SurfaceFor(MarkingDefinition def, Polyline2 axis)
    {
        var pav = RoadOf(def);
        // Sempre o eixo da via (rampas, moderadores e outros membros têm caminho próprio, curto).
        if (pav != null && !ReferenceEquals(pav, def) && PathResolver.Resolve(_doc, pav.Path)?.Main is { Points.Count: >= 2 } roadAxis) axis = roadAxis;
        var key = (pav?.Id ?? def.Id) + "|" + axis.Length.ToString("0.###") + "|" + def.Output.Grade!.GetHashCode();
        if (_surfaces.TryGetValue(key, out var cached)) return cached;
        var grade = def.Output.Grade!.Clone();
        if (pav != null) grade.CrossfallWidth = Math.Max(1, Math.Max(pav.LeftWidth, pav.RightWidth));
        var surf = new GradeSurface(axis, grade);
        if (pav != null)
        {
            var zones = new List<FloorTiling.Zone>();
            foreach (var (ownerId, blend) in BlendsFor(pav, surf, PathResolver.Resolve(_doc, pav.Path)?.Z ?? 0))
            {
                surf.Blends.Add(blend);
                zones.Add(new FloorTiling.Zone(ownerId, blend.Station, blend.Start));
            }
            _zones[surf] = zones;
        }
        return _surfaces[key] = surf;
    }

    /// <summary>Nós em que a via de cada superfície é secundária (para o retalhamento comum dos pisos).</summary>
    private readonly Dictionary<GradeSurface, List<FloorTiling.Zone>> _zones = new();
    private FloorTiling? _tiling;
    private object? _tilingFor;

    /// <summary>
    /// Retalhamento comum dos pisos do documento: uma malha de juntas por via com greide (montada só quando uma marca dela é
    /// gerada), usada por todos os pisos da via – pista, sarjeta, meio-fio, calçada, grama e pintura – e, nos nós, a da via
    /// principal. Refeito quando as definições mudam.
    /// </summary>
    private FloorTiling Tiling()
    {
        var defs = Definitions;
        if (_tiling != null && ReferenceEquals(_tilingFor, defs)) return _tiling;
        _tilingFor = defs;
        return _tiling = new FloorTiling(TilingRoad, TilingNode, GradeFloorTolerance);
    }

    private FloorTiling.Road? TilingRoad(string id)
    {
        if (!Definitions.TryGetValue(id, out var d) || d is not RoadPavementDefinition pav) return null;
        var path = PathResolver.Resolve(_doc, pav.Path);
        if (path?.Main is not { Points.Count: >= 2 } axis) return null;
        GradeSurface surf;
        if (pav.Output.Grade != null) surf = SurfaceFor(pav, axis);
        else
        {
            // Via sem greide como principal de um nó com greide: plano na cota da base.
            var flat = RoadGrade.Flat(axis.Length);
            flat.CrossfallWidth = Math.Max(1, Math.Max(pav.LeftWidth, pav.RightWidth));
            surf = new GradeSurface(axis, flat);
        }
        return new FloorTiling.Road(pav.Id, surf, path.Z, pav.TotalLeft, pav.TotalRight, _zones.GetValueOrDefault(surf) ?? new List<FloorTiling.Zone>());
    }

    private FloorTiling.Node? TilingNode(string id)
    {
        if (!Definitions.TryGetValue(id, out var owner) || NodeFor(owner) is not { Major.Id: { } major } node) return null;
        return new FloorTiling.Node(major, node.Legs.Select(l => l.Id).OfType<string>().ToList());
    }

    /// <summary>Pavimento da via a que a marca pertence (o próprio, ou o do mesmo grupo).</summary>
    private RoadPavementDefinition? RoadOf(MarkingDefinition def)
    {
        if (def is RoadPavementDefinition p) return p;
        if (def.GroupId == null) return null;
        return Definitions.Values.OfType<RoadPavementDefinition>().FirstOrDefault(x => x.GroupId == def.GroupId);
    }

    /// <summary>Concordâncias da via com as interseções e rotatórias em que ela não é a principal.</summary>
    private IEnumerable<(string OwnerId, NodeBlend Blend)> BlendsFor(RoadPavementDefinition pav, GradeSurface surf, double baseZ)
    {
        foreach (var owner in Definitions.Values)
        {
            var ids = owner switch
            {
                IntersectionDefinition it => it.RoadIds,
                RoundaboutDefinition rb => rb.Legs.Select(l => l.RoadId).OfType<string>().ToList(),
                _ => null,
            };
            if (ids == null || !ids.Contains(pav.Id)) continue;
            if (NodeFor(owner) is not { } node || node.Major?.Id == pav.Id) continue;
            // Recorte do nó sobre esta via: estação do centro e alcance ao longo do eixo.
            var cut = pav.Exclusions.Where(e => e.SourceId == owner.Id).SelectMany(e => e.Points).ToList();
            if (cut.Count == 0) continue;
            var locs = cut.Select(surf.Locate).ToList();
            var c = surf.Locate(new Polygon2(cut).Centroid).S;
            var reach = locs.Max(l => Math.Abs(l.S - c));
            // Concordância de 20 m (mais longa quando a rampa própria é forte ou o empeno da pista passaria de 0,75 %).
            var len = NodeBlend.LengthFor(node, surf, baseZ, c, reach);
            yield return (owner.Id, new NodeBlend(node, c, reach, len, baseZ));
        }
    }

    /// <summary>
    /// Superfície do nó (interseção, rotatória, cul-de-sac – e as marcas filhas deles) costurada ao greide das vias ligadas;
    /// nulo quando nenhuma via ligada tem greide (o nó fica plano, como antes).
    /// </summary>
    /// <summary>Nó (interseção, rotatória, cul-de-sac) dono da marca – ele mesmo ou o pai das marcas filhas.</summary>
    internal MarkingDefinition? NodeOwner(MarkingDefinition def) =>
        def is IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition ? def
            : NodeParents.FirstOrDefault(o => o is IntersectionDefinition i && i.ChildIds.Contains(def.Id) || o is RoundaboutDefinition r && r.ChildIds.Contains(def.Id));

    private List<MarkingDefinition>? _nodeParents;

    /// <summary>
    /// Interseções e rotatórias (donas das marcas filhas). Toda marca 3D gerada pergunta se é filha de um nó: sem o cache geral,
    /// só os nós são interpretados – antes cada geração interpretava o projeto inteiro só para responder "não".
    /// </summary>
    private List<MarkingDefinition> NodeParents => _nodeParents ??= _definitions != null
        ? _definitions.Values.Where(o => o is IntersectionDefinition or RoundaboutDefinition).ToList()
        : MarkingStorage.Definitions(_doc, typeof(IntersectionDefinition), typeof(RoundaboutDefinition));

    public NodeSurface? NodeFor(MarkingDefinition def)
    {
        var owner = NodeOwner(def);
        if (owner == null) return null;
        var defs = Definitions;
        IEnumerable<string?> ids = owner switch
        {
            IntersectionDefinition it => it.RoadIds,
            RoundaboutDefinition rb => rb.Legs.Select(l => l.RoadId),
            CulDeSacDefinition c => new[] { c.RoadId },
            _ => Array.Empty<string?>(),
        };
        var legs = new List<NodeSurface.Leg>();
        foreach (var id in ids.Where(x => x != null).Distinct())
        {
            if (!defs.TryGetValue(id!, out var d) || d is not RoadPavementDefinition pav) continue;
            var path = PathResolver.Resolve(_doc, pav.Path);
            if (path?.Main == null || path.Main.Points.Count < 2) continue;
            var half = Math.Max(1, Math.Max(pav.LeftWidth, pav.RightWidth));
            var grade = (pav.Output.Grade ?? RoadGrade.Flat(path.Main.Length)).Clone();
            grade.CrossfallWidth = half;
            // Prioridade pela hierarquia (CTB art. 60): a via principal atravessa o nó com o próprio greide.
            var rank = pav.Hierarchy is { } h && h != HierarquiaViaria.NaoDefinida ? (int)h : 10;
            legs.Add(new NodeSurface.Leg(new GradeSurface(path.Main, grade), path.Z, half, rank, pav.Id));
        }
        if (legs.Count == 0) return null;
        var node = new NodeSurface(legs);
        return node.IsFlat ? null : node;
    }

    private Func<Vec2, double?>? _ground;
    private bool _groundRead;

    /// <summary>
    /// Cota do terreno NATURAL (m) relativa a <paramref name="baseZ"/>: o terreno original de cada Toposolid (guardado na primeira
    /// terraplenagem), lido direto da geometria – sem vistas 3D. Topografias antigas caem no amostrador por raios.
    /// </summary>
    private Func<Vec2, double?>? TerrainFunction(double baseZ)
    {
        try
        {
            if (!_groundRead)
            {
                _groundRead = true;
                _ground = TerrainModel.Ground(_doc);
                if (_ground == null)
                {
                    var sampler = new SurfaceSampler(_doc, Array.Empty<string>(), _interactive, terrainOnly: true);
                    if (sampler.IsAvailable)
                        _ground = p => sampler.TrySample(UnitConv.Ft(p.X), UnitConv.Ft(p.Y), 0, out var z, out _) ? UnitConv.M(z) : null;
                }
            }
            var g = _ground;
            return g == null ? null : p => g(p) - baseZ;
        }
        catch (Exception ex)
        {
            Log.Error("Terreno natural", ex);
            return null;
        }
    }

    /// <summary>Esquece o terreno lido (depois de criar ou moldar o Toposolid).</summary>
    public void InvalidateTerrain() { _groundRead = false; _ground = null; _hasTopo = null; }

    // ------------------------------------------------------------------ criação / regeneração

    /// <summary>
    /// Verdadeiro enquanto o plugin gera elementos (evita que o atualizador confunda elementos
    /// recém-criados com cópias). O Revit executa os atualizadores no commit da transação, por isso
    /// a marcação é feita por transação em <see cref="RenderScope"/>.
    /// </summary>
    public static bool IsRendering => _renderDepth > 0;
    private static int _renderDepth;

    /// <summary>Delimita uma transação do plugin (use com "using").</summary>
    public static IDisposable RenderScope() => new Scope();

    private sealed class Scope : IDisposable
    {
        public Scope() => _renderDepth++;
        public void Dispose() => _renderDepth--;
    }

    /// <summary>Contornos degenerados deixados de fora ao montar pisos de pintura (avisados no fim da marca).</summary>
    private int _skippedContours;

    /// <summary>
    /// Definição gravada por uma versão mais nova do plugin: os elementos ficam como estão (regerar com esta versão perderia
    /// os dados que ela não conhece) e o motivo vai para os avisos.
    /// </summary>
    private static bool KeepNewer(MarkingDefinition def, List<StoredMarking> existing, RenderResult result)
    {
        if (!DefinitionVersioning.IsNewer(def)) return false;
        result.Elements.AddRange(existing.Select(e => e.Element.Id));
        result.Warnings.Add(DefinitionVersioning.NewerMessage(def));
        return true;
    }

    /// <summary>Cria ou regenera todos os elementos da marca.</summary>
    public RenderResult Render(MarkingDefinition def)
    {
        using var perf = Perf.Measure("Gerar marcas (Render)");
        var result = new RenderResult();
        _definitions = null;
        _nodeParents = null;
        _byId = null;
        _present = null;
        var existing = MarkingStorage.ById(_doc, def.Id);
        if (KeepNewer(def, existing, result)) return result;
        // Loteamento: dados num elemento próprio; quadra: um lote por subdivisão do Toposolid (ou piso plano).
        if (def is LoteamentoDefinition or QuadraDefinition) return LotService.Render(_doc, def, existing);
        // Só o corte transversal: nada na planta – a cota fica num elemento de dados, só para o perfil ler a linha de corte.
        if (def is SectionDimensionDefinition sec && DetailGenerator.SectionDataOnly(sec)) return SaveAsData(def, existing);
        PruneExclusions(def);
        // Traçado das linhas de referência guardado na marca: apagar a linha depois não faz a marca perder o caminho.
        try { PathResolver.RefreshCache(_doc, def.Path); } catch (Exception ex) { Log.Error("Cache do caminho", ex); }
        // Pista já modelada (pavimento "Nenhum"): sem piso – a definição fica num elemento de dados, que liga a via às outras.
        if (def is RoadPavementDefinition { Material: TipoPavimento.Nenhum }) return SaveAsData(def, existing);

        // Detalhes, anotações e legendas existem somente na vista.
        if (def is IAnnotationDefinition) def.Output.Mode = OutputMode.Detalhe2D;
        View? view = null;
        if (def.Output.Mode == OutputMode.Detalhe2D)
        {
            view = ResolveView(def, result);
            if (view == null)
            {
                result.Elements.AddRange(existing.Select(e => e.Element.Id));
                return result;
            }
        }

        // Escala em que o detalhe foi desenhado (base do ajuste quando a escala da vista mudar).
        if (view != null && def is IAnnotationDefinition) ViewScaleMemory.Remember(_doc, view);
        // Detalhe movido à mão (ou o grupo dele): a nova posição passa a valer antes de regenerar.
        if (def is IPlacedAnnotation placed) FollowManualMove(def, placed, existing);
        // Trecho de prancha: escala, região de corte girada e posição na folha acompanham o eixo atual.
        if (def is SheetSegmentDefinition sheetSeg && view != null) SheetBuilder.Sync(_doc, sheetSeg, view, result);
        // Placa girada/movida com as ferramentas do Revit: o giro vai para a definição antes de regenerar.
        if (def is SignDefinition sign) FollowSignMove(sign, existing);
        // Grupo de detalhes: desfeito para trocar os elementos e refeito no fim.
        if (def is IGroupedAnnotation) Ungroup(existing);

        MarkingGeometry geo;
        double baseZ;
        try
        {
            geo = BuildGeometry(def, out baseZ, result.Warnings, view);
            if (def is IPlacedAnnotation pa && LinesAnchor(geo) is { } anchor) pa.DrawnAnchor = anchor;
        }
        catch (Exception ex)
        {
            Log.Error($"BuildGeometry {def.DisplayCode}", ex);
            result.Warnings.Add($"{def.DisplayCode}: erro ao gerar a geometria – {ex.Message}");
            return result;
        }
        result.Geometry = geo;
        result.Warnings.AddRange(geo.Warnings);

        if (def is IAnnotationDefinition { TargetId: not null } && geo.Warnings.Any(w => w.StartsWith(Core.Generators.DetailGenerator.MissingTarget)))
        {
            // A marca de referência foi excluída: o detalhe também é removido.
            foreach (var old in existing) SafeDelete(old.Element.Id);
            return result;
        }

        if (geo.Pieces.Count == 0 && geo.Annotations.Count == 0)
        {
            // Mantém os elementos antigos (ex.: caminho excluído) para não perder o trabalho.
            result.Warnings.Add($"{def.DisplayCode}: nenhuma peça gerada.");
            result.Elements.AddRange(existing.Select(e => e.Element.Id));
            return result;
        }

        SharedParameters.Ensure(_doc);
        var info = MarkingBuilder.Describe(def, Catalog);
        var settings = PluginContext.Settings;
        var materialName = def.Output.Material ?? settings.DefaultMaterial;
        var material = Catalog.Material(materialName);
        var thickness = def.Output.Thickness > 0
            ? def.Output.Thickness
            : Math.Max(settings.MinModelThickness, (material?.EspessuraMm ?? 0.6) / 1000.0);

        var keep = new HashSet<ElementId>();
        var primary = true;
        _arcCenters = geo.ArcCenters;
        var solidPieces = geo.Pieces;

        // Sinalização horizontal como Piso do Revit: peças de pintura com a espessura do material, sobre o pavimento.
        var paintFloors = def.Output.Mode == OutputMode.Modelo3D && settings.PaintAsFloors && PaintDefinition(def);
        if (paintFloors)
        {
            var lift = IsOverlay(def) ? thickness : 0;
            var pt = Math.Max(0.002, thickness);
            for (int i = 0; i < geo.Pieces.Count; i++)
            {
                var pc = geo.Pieces[i];
                if (pc.Solid == null && pc.Profile == null && MarkingColors.IsPaint(pc.Color))
                    geo.Pieces[i] = pc with { Elevation = pc.Elevation + lift, Thickness = pt };
            }
            solidPieces = geo.Pieces;
        }

        // Pavimento, calçada, meio-fio, sarjeta e grama como Piso do Revit (editáveis com as ferramentas nativas).
        if (def.Output.Mode == OutputMode.Modelo3D && (settings.PhysicalAsFloors || paintFloors))
        {
            // Acompanhando a superfície: o piso é criado na cota do terreno e deformado (edição de forma nativa do piso).
            ISurface? terrain = GradeFor(def, baseZ);
            if (terrain == null && def.Output.Drape)
            {
                terrain = new SurfaceSampler(_doc, def.Output.SurfaceIds, _interactive, terrainOnly: true);
                if (!terrain.IsAvailable) terrain = null;
            }
            var floorPieces = geo.Pieces.Where(p => settings.PhysicalAsFloors && FloorEligible(def, p) || paintFloors && PaintFloorEligible(p)).ToList();
            var oldFloors = existing.Where(r => r.Element is Floor).ToList();
            if (floorPieces.Count > 0 || oldFloors.Count > 0)
            {
                // Tipo de piso trocado pelo usuário em pisos anteriores é mantido (ver UserFloorTypes).
                var userTypes = UserFloorTypes(oldFloors);
                // Pisos editados à mão (contorno alterado ou movidos) são preservados: a edição do usuário prevalece.
                var edited = oldFloors.Where(r => FloorEdited((Floor)r.Element)).ToList();
                var locked = edited.Select(r => r.Color).ToHashSet();
                // Pisos anteriores: os que saírem iguais (mesma chave) são mantidos; os demais são apagados no fim.
                var pool = oldFloors.Where(r => !locked.Contains(r.Color)).Select(r => (Floor)r.Element).ToList();
                _reuse = new Dictionary<string, Floor>();
                foreach (var f in pool)
                    if (FloorSignature.ReadKey(f) is { } k) _reuse.TryAdd(k, f);
                existing.RemoveAll(r => r.Element is Floor && !locked.Contains(r.Color));
                foreach (var r in oldFloors.Where(r => locked.Contains(r.Color)))
                {
                    Tag(r.Element, def, info, r.Color, StyleService.ColorName(r.Color), FloorArea((Floor)r.Element), geo, primary);
                    keep.Add(r.Element.Id);
                    primary = false;
                }
                if (edited.Count > 0 && _interactive)
                    result.Warnings.Add($"{info.Code}: {edited.Count} piso(s) editado(s) à mão foram mantidos como estão (para gerar de novo, apague o piso e use Atualizar).");

                var failed = new List<MarkingPiece>();
                string? reason = null;
                var floorGroups = floorPieces.Where(p => !locked.Contains(p.Color)).GroupBy(ElementPlan.FloorKey)
                    .Select(grp => (Group: grp, Parts: FloorParts(def, grp.Select(p => p.Shape), terrain, baseZ, MarkingColors.IsPaint(grp.Key.Color))))
                    .ToList();
                // Bordas comuns entre os pisos físicos da marca ajustadas uma vez só (ajuste topológico, K13).
                PrepareSharedFit(floorGroups.Where(g => !MarkingColors.IsPaint(g.Group.Key.Color)).SelectMany(g => g.Parts.Select(p => p.Shape)));
                foreach (var (grp, groupParts) in floorGroups)
                {
                    var parts = groupParts;
                    if (MarkingColors.IsPaint(grp.Key.Color) && parts.Count > 1)
                    {
                        // Pintura: um piso por cor e por plano, com todos os traços/áreas como contornos do mesmo esboço.
                        var rest = new List<(Polygon2 Shape, Plane3? Plane)>();
                        foreach (var byPlane in parts.GroupBy(x => x.Plane is { } pl ? $"{pl.A:0.000}|{pl.B:0.00000}|{pl.C:0.00000}" : "plano"))
                        {
                            var list = byPlane.ToList();
                            var plane0 = list[0].Plane;
                            if (plane0 == null && terrain != null) { rest.AddRange(list); continue; }
                            var multi = CreateMultiFloor(list.Select(x => x.Shape).ToList(), grp.Key.Color, grp.Key.Elevation, grp.Key.Thickness, plane0,
                                baseZ + def.Output.ElevationOffset, UserType(userTypes, grp.Key.Color, grp.Key.Thickness), ref reason);
                            if (multi == null) { rest.AddRange(list); continue; }
                            try { multi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"SV {info.Code}"); } catch { /* opcional */ }
                            Tag(multi, def, info, grp.Key.Color, StyleService.ColorName(grp.Key.Color), FloorArea(multi, list.Sum(x => x.Shape.Area)), geo, primary);
                            keep.Add(multi.Id);
                            primary = false;
                        }
                        parts = rest;
                    }
                    foreach (var (shape, plane) in parts)
                    {
                        var piece = new MarkingPiece(shape, grp.Key.Color) { Elevation = grp.Key.Elevation, Thickness = grp.Key.Thickness, Layer = grp.Key.Layer };
                        var floors = CreateFloors(piece, baseZ + def.Output.ElevationOffset, UserType(userTypes, grp.Key.Color, grp.Key.Thickness), ref reason, terrain, plane);
                        if (floors.Count == 0) { failed.Add(piece); continue; }
                        foreach (var f in floors)
                        {
                            try { f.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"SV {info.Code}"); } catch { /* opcional */ }
                            Tag(f, def, info, grp.Key.Color, StyleService.ColorName(grp.Key.Color), FloorArea(f, piece.Shape.Area / floors.Count), geo, primary);
                            keep.Add(f.Id);
                            primary = false;
                        }
                    }
                }
                _sharedFits.Clear();
                foreach (var f in pool) if (f.IsValidObject && !keep.Contains(f.Id)) SafeDelete(f.Id);
                _reuse = null;
                // Inclinação e edição de forma de todos os pisos da marca de uma vez (uma regeneração, não uma por piso).
                FlushShapeEdits();
                if (failed.Count > 0)
                    result.Warnings.Add($"{info.Code}: {failed.Count} parte(s) não puderam virar Piso do Revit e foram geradas como forma direta" +
                                        (reason != null ? $" ({reason})." : "."));
                if (_skippedContours > 0)
                    result.Warnings.Add($"{info.Code}: {_skippedContours} contorno(s) degenerado(s) da pintura ficaram de fora do piso (detalhes no log).");
                _skippedContours = 0;
                solidPieces = geo.Pieces.Where(p => !floorPieces.Contains(p)).Concat(failed).ToList();
            }
        }
        // Volume de escavação (túneis): Massa que escava o Toposolid – separada das peças da obra.
        var bore = solidPieces.Where(p => p.Layer == Core.Generators.EarthworksGenerator.BoreLayer).ToList();
        if (bore.Count > 0) solidPieces = solidPieces.Where(p => p.Layer != Core.Generators.EarthworksGenerator.BoreLayer).ToList();
        var groups = solidPieces.GroupBy(p => p.Color)
            .OrderByDescending(g => g.Sum(p => p.Shape.Area)).ToList();

        if (def.Output.Mode == OutputMode.Modelo3D)
        {
            ISurface? sampler = GradeFor(def, baseZ);
            if (sampler is GradeSampler or NodeSampler) sampler = TiledFor(def, sampler);
            if (sampler == null && def.Output.Drape)
            {
                sampler = new SurfaceSampler(_doc, def.Output.SurfaceIds, _interactive);
                if (!sampler.IsAvailable) result.Warnings.Add("Nenhuma vista 3D disponível para projetar sobre a superfície – marca gerada plana.");
            }

            if (ElementPlan.SingleElement(def) && groups.Count > 0)
            {
                // Placa: poste, chapa, orla, fundo e legenda num elemento só (cada sólido com o seu material) – seleciona-se e
                // gira-se a placa inteira.
                var all = new List<GeometryObject>();
                foreach (var g in groups)
                    all.AddRange(BuildSolids(g, baseZ + def.Output.ElevationOffset, thickness, sampler, Styles.Material(g.Key), result.Warnings, def.Output.ElevationOffset));
                if (all.Count > 0)
                {
                    var ds = existing.FirstOrDefault(r => r.Element is DirectShape && !IsBore(r.Element))?.Element as DirectShape;
                    if (ds == null)
                    {
                        ds = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        ds.ApplicationId = "SinalizacaoViaria";
                        ds.ApplicationDataId = def.Id;
                    }
                    DropInvalidShapes(ds, all, info.Code, result.Warnings);
                    try
                    {
                        ds.SetShape(all);
                        try { ds.SetName($"SV {info.Code}"); } catch { /* nome é opcional */ }
                        Tag(ds, def, info, groups[0].Key, materialName, groups.Sum(g => g.Sum(p => p.Shape.Area)), geo, primary);
                        if (SignFrameStore.Of(all, Styles.Material(MarkingColor.Metal)) is { } frame) SignFrameStore.Write(ds, frame);
                        keep.Add(ds.Id);
                        primary = false;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"SetShape {info.Code}", ex);
                        result.Warnings.Add($"{info.Code}: o Revit recusou a geometria ({ex.Message}).");
                        if (ds.IsValidObject && !existing.Any(r => r.Element.Id == ds.Id)) SafeDelete(ds.Id);
                    }
                }
                groups.Clear();
            }

            foreach (var g in groups)
            {
                // Linhas e símbolos sobre pinturas de fundo (ciclofaixa, faixa de caminhada) ficam logo acima delas –
                // sem faces coincidentes (que no Revit aparecem como emendas/quadrados).
                var lift = !paintFloors && IsOverlay(def) && MarkingColors.IsPaint(g.Key) ? thickness : 0;
                var solids = BuildSolids(g, baseZ + def.Output.ElevationOffset + lift, thickness, sampler, Styles.Material(g.Key), result.Warnings,
                    def.Output.ElevationOffset + lift);
                if (solids.Count == 0) continue;
                var ds = existing.FirstOrDefault(r => r.Color == g.Key && r.Element is DirectShape && !IsBore(r.Element) && !keep.Contains(r.Element.Id))?.Element as DirectShape;
                if (ds == null)
                {
                    ds = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    ds.ApplicationId = "SinalizacaoViaria";
                    ds.ApplicationDataId = def.Id;
                }
                DropInvalidShapes(ds, solids, $"{info.Code} {StyleService.ColorName(g.Key)}", result.Warnings);
                if (solids.Count == 0)
                {
                    if (ds.IsValidObject && !existing.Any(r => r.Element.Id == ds.Id)) SafeDelete(ds.Id);
                    continue;
                }
                try
                {
                    ds.SetShape(solids);
                }
                catch (Exception ex)
                {
                    Log.Error($"SetShape {info.Code}", ex);
                    // Segunda tentativa com os contornos limpos (lascas, espinhos e vértices quase coincidentes removidos).
                    var cleaned = g.Select(p => p.Solid != null || p.Profile != null ? p
                        : PolygonOps.Clean(new[] { p.Shape }, 0.01).Select(c => c.Simplified(0.02) ?? c).Select(c => p with { Shape = c }).FirstOrDefault() ?? p).ToList();
                    var retry = BuildSolids(cleaned, baseZ + def.Output.ElevationOffset + lift, thickness, sampler, Styles.Material(g.Key), new List<string>(), def.Output.ElevationOffset + lift);
                    DropInvalidShapes(ds, retry, $"{info.Code} {StyleService.ColorName(g.Key)} (limpo)", new List<string>());
                    var ok = false;
                    if (retry.Count > 0)
                        try { ds.SetShape(retry); ok = true; } catch (Exception ex2) { Log.Error($"SetShape (limpo) {info.Code}", ex2); }
                    if (!ok)
                    {
                        result.Warnings.Add($"{info.Code}: o Revit recusou a geometria ({ex.Message}).");
                        if (ds.IsValidObject && !existing.Any(r => r.Element.Id == ds.Id)) SafeDelete(ds.Id);
                        continue;
                    }
                }
                try { ds.SetName($"SV {info.Code} {StyleService.ColorName(g.Key)}"); } catch { /* nome é opcional */ }
                Tag(ds, def, info, g.Key, materialName, g.Sum(p => p.Shape.Area), geo, primary);
                keep.Add(ds.Id);
                primary = false;
            }
        }
        else
        {
            var z = PlaneZ(view!);
            // Regiões, textos e linhas de detalhe não são remodelados: remove e recria.
            foreach (var old in existing.Where(r => r.Element is not DirectShape)) SafeDelete(old.Element.Id);
            existing.RemoveAll(r => r.Element is not DirectShape);

            foreach (var g in groups)
                // Detalhamento: cor própria e hachura por elemento (perfis, legendas) viram tipos de região próprios.
                foreach (var sub in g.GroupBy(p => (p.Rgb, p.Hatch)))
                {
                    var regions = CreateRegions(view!, g.Key, sub.Select(p => p.Shape).ToList(), z, result.Warnings, sub.Key.Rgb, sub.Key.Hatch);
                    foreach (var fr in regions)
                    {
                        Tag(fr, def, info, g.Key, materialName, fr == regions[0] ? sub.Sum(p => p.Shape.Area) : 0, geo, primary);
                        keep.Add(fr.Id);
                        primary = false;
                    }
                }
            foreach (var e in CreateAnnotations(view!, geo.Annotations, z, result.Warnings))
            {
                if (primary)
                {
                    Tag(e, def, info, MarkingColor.Preta, materialName, 0, geo, true);
                    primary = false;
                }
                else MarkingStorage.Write(e, def, MarkingColor.Preta);
                keep.Add(e.Id);
            }
        }

        if (bore.Count > 0 && def.Output.Mode == OutputMode.Modelo3D)
        {
            try
            {
                var solids = BuildSolids(bore, baseZ + def.Output.ElevationOffset, thickness, null, Styles.Material(MarkingColor.Terra), new List<string>(), def.Output.ElevationOffset);
                if (solids.Count > 0)
                {
                    var ds = existing.FirstOrDefault(r => IsBore(r.Element) && !keep.Contains(r.Element.Id))?.Element as DirectShape;
                    if (ds == null)
                    {
                        // Massa: oculta nas vistas por padrão (Mostrar massa) – só escava o terreno.
                        ds = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_Mass));
                        ds.ApplicationId = "SinalizacaoViaria";
                        ds.ApplicationDataId = def.Id;
                    }
                    ds.SetShape(solids);
                    try { ds.SetName($"SV {info.Code} escavação do terreno"); } catch { /* opcional */ }
                    MarkingStorage.Write(ds, def, MarkingColor.Terra);
                    try { ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"SV escavação {info.Code}"); } catch { /* opcional */ }
                    keep.Add(ds.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Escavação {info.Code}", ex);
                result.Warnings.Add($"{info.Code}: volume de escavação do terreno não criado ({ex.Message}).");
            }
        }

        if (keep.Count == 0 && def.Output.Mode == OutputMode.Modelo3D && geo.Pieces.Count > 0)
        {
            // Último recurso: nenhuma peça pôde ser modelada. Um marcador mínimo guarda a definição para a via/marca não
            // desaparecer do projeto (conexões continuam a enxergá-la) – corrija a geometria e use Atualizar.
            try
            {
                var c = geo.Pieces.OrderByDescending(p => p.Shape.Area).First().Shape.Centroid;
                var cz = UnitConv.Ft(baseZ + def.Output.ElevationOffset);
                var box = Polygon2.Rectangle(new Vec2(c.X - 0.15, c.Y - 0.15), new Vec2(c.X + 0.15, c.Y + 0.15));
                var loops = ToCurveLoops(box, cz);
                var solid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, UnitConv.Ft(0.02), new SolidOptions(Styles.Material(MarkingColor.Preta), ElementId.InvalidElementId));
                var anchor = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                anchor.ApplicationId = "SinalizacaoViaria";
                anchor.ApplicationDataId = def.Id;
                anchor.SetShape(new List<GeometryObject> { solid });
                try { anchor.SetName($"SV {info.Code} (marcador – geometria recusada)"); } catch { /* opcional */ }
                Tag(anchor, def, info, MarkingColor.Preta, materialName, 0, geo, true);
                keep.Add(anchor.Id);
                result.Warnings.Add($"{info.Code}: NENHUMA peça pôde ser modelada pelo Revit – ficou só um marcador de 30 cm em ({c.X:0.0}; {c.Y:0.0}). " +
                                    "Veja o arquivo log.txt (Sobre) e envie ao suporte; depois de corrigir, use Atualizar.");
            }
            catch (Exception ex)
            {
                Log.Error("Marcador de último recurso", ex);
            }
        }

        foreach (var old in existing.Where(r => !keep.Contains(r.Element.Id)))
            SafeDelete(old.Element.Id);

        if (def is IGroupedAnnotation grouped && keep.Count > 1) MakeGroup(keep, grouped.GroupName, result);

        result.Elements.AddRange(keep);
        return result;
    }

    /// <summary>Volume de escavação do terreno (Massa gerada pelo plugin para túneis).</summary>
    public static bool IsBore(Element e) => e is DirectShape && e.Category?.BuiltInCategory == BuiltInCategory.OST_Mass;

    /// <summary>Canto inferior esquerdo das linhas de detalhe da geometria (m).</summary>
    private static Vec2? LinesAnchor(MarkingGeometry geo)
    {
        var pts = geo.Annotations.OfType<AnnotationLine>().SelectMany(l => l.Points).ToList();
        return pts.Count == 0 ? null : new Vec2(pts.Min(p => p.X), pts.Min(p => p.Y));
    }

    /// <summary>Compara as linhas de detalhe existentes com a última geração: se foram movidas, desloca o detalhe junto.</summary>
    private static void FollowManualMove(MarkingDefinition def, IPlacedAnnotation placed, List<StoredMarking> existing)
    {
        if (placed.DrawnAnchor is not { } drawn) return;
        double minX = double.MaxValue, minY = double.MaxValue;
        var any = false;
        foreach (var r in existing)
        {
            if (r.Element is not CurveElement { ViewSpecific: true } ce) continue;
            Curve? c;
            try { c = ce.GeometryCurve; } catch { continue; }
            if (c == null || !c.IsBound) continue;
            foreach (var p in new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
            {
                minX = Math.Min(minX, UnitConv.M(p.X));
                minY = Math.Min(minY, UnitConv.M(p.Y));
                any = true;
            }
        }
        if (!any) return;
        var delta = new Vec2(minX - drawn.X, minY - drawn.Y);
        if (delta.Length < 0.001) return;
        def.Translate(delta, 0);
        placed.DrawnAnchor = drawn + delta;
    }

    /// <summary>
    /// Compara a referência da placa gravada na geração com a medida na geometria atual: se a placa foi girada ou movida no
    /// Revit (inteira, como corpo rígido), o giro e o deslocamento passam para a definição.
    /// </summary>
    private void FollowSignMove(SignDefinition sign, List<StoredMarking> existing)
    {
        try
        {
            var ds = existing.Select(r => r.Element).OfType<DirectShape>().FirstOrDefault(e => !IsBore(e));
            if (ds == null || SignFrameStore.Read(ds) is not { } stored) return;
            if (SignFrameStore.Of(ds, Styles.Material(MarkingColor.Metal)) is not { } now) return;
            SignFrame.Follow(sign, stored, now, minMove: 0.002, minAngle: 0.0005);
        }
        catch (Exception ex)
        {
            Log.Error("Giro manual da placa", ex);
        }
    }

    private void Ungroup(IEnumerable<StoredMarking> existing)
    {
        foreach (var gid in existing.Select(r => r.Element.GroupId).Where(id => id != ElementId.InvalidElementId).Distinct().ToList())
        {
            try
            {
                if (_doc.GetElement(gid) is not Group g) continue;
                var typeId = g.GetTypeId();
                g.UngroupMembers();
                // Tipo de grupo sem outras instâncias: removido para não acumular tipos no projeto.
                var others = new FilteredElementCollector(_doc).OfClass(typeof(Group)).Any(x => x.GetTypeId() == typeId);
                if (!others) SafeDelete(typeId);
            }
            catch (Exception ex)
            {
                Log.Error("Desagrupar detalhe", ex);
            }
        }
    }

    private void MakeGroup(ICollection<ElementId> ids, string name, RenderResult result)
    {
        try
        {
            var group = _doc.Create.NewGroup(ids);
            var names = new FilteredElementCollector(_doc).OfClass(typeof(GroupType)).Select(t => t.Name).ToHashSet();
            var unique = name;
            for (int i = 2; names.Contains(unique); i++) unique = $"{name} ({i})";
            try { group.GroupType.Name = unique; } catch { /* nome é opcional */ }
        }
        catch (Exception ex)
        {
            Log.Error("Agrupar detalhe", ex);
            result.Warnings.Add($"Os elementos do detalhe não puderam ser agrupados ({ex.Message}) – use Selecionar Conjunto para movê-los juntos.");
        }
    }

    /// <summary>Remove recortes cuja marca de origem (ex.: rampa) não existe mais.</summary>
    private void PruneExclusions(MarkingDefinition def)
    {
        if (def.Exclusions.Count == 0) return;
        var ids = MarkingStorage.Ids(_doc);
        def.Exclusions.RemoveAll(z => !string.IsNullOrEmpty(z.SourceId) && !ids.Contains(z.SourceId!));
    }

    /// <summary>
    /// Escala da vista mudou: o detalhe é ajustado no lugar – cada texto, linha e região existente vai para o lugar do desenho na
    /// escala nova, com o deslocamento que a pessoa deu a ele; o que ela apagou continua apagado e o texto editado mantém o texto
    /// dela (AnnotationScale.Ajustar). <paramref name="oldScale"/> nula = desconhecida: é deduzida pelos textos que ficaram no
    /// lugar. Sem nada a aproveitar, o detalhe é refeito.
    /// </summary>
    public RenderResult RescaleInPlace(MarkingDefinition def, View view, double? oldScale)
    {
        var result = new RenderResult();
        var existing = MarkingStorage.ById(_doc, def.Id).Where(r => r.Element.OwnerViewId == view.Id).ToList();
        if (KeepNewer(def, existing, result)) return result;
        if (existing.Count == 0) return Render(def);
        if (def is IPlacedAnnotation placed) FollowManualMove(def, placed, existing);
        var newScale = (double)view.Scale;
        // Tipo de região de cada cor/hachura do detalhe → chave da região (casa as regiões existentes com as geradas).
        var regionKeys = new Dictionary<ElementId, string>();
        List<ItemDetalhe> Draw(double scale, out MarkingGeometry g)
        {
            _scaleOverride = scale;
            try { g = BuildGeometry(def, out _, null, view); }
            finally { _scaleOverride = null; }
            foreach (var p in g.Pieces.GroupBy(p => (p.Color, p.Rgb, p.Hatch)).Select(x => x.Key))
                regionKeys.TryAdd(Styles.FilledRegionType(p.Color, p.Rgb, p.Hatch), AnnotationScale.ChaveRegiao(p.Color, p.Rgb, p.Hatch));
            return AnnotationScale.Itens(g);
        }
        var depois = Draw(newScale, out var newGeo);
        var items = existing.Select(r => ItemOf(r.Element, regionKeys)).ToList();
        if (oldScale == null)
        {
            // Escala antiga desconhecida (projeto aberto depois da última geração): a que mais textos deixa exatamente no lugar.
            var texts = items.Where(i => i?.Tipo == TipoItemDetalhe.Texto).Cast<ItemDetalhe>().ToList();
            var best = 0;
            foreach (var s in ViewScaleMemory.Candidates.Where(c => Math.Abs(c - newScale) > 0.5))
            {
                var g = Draw(s, out _).Where(i => i.Tipo == TipoItemDetalhe.Texto).ToList();
                var hits = texts.Count(t => g.Any(x => x.Chave == t.Chave && x.Ref.DistanceTo(t.Ref) < 0.001));
                if (hits > best) { best = hits; oldScale = s; }
            }
            if (oldScale == null) return Render(def);
        }
        var antes = Draw(oldScale.Value, out _);
        var known = existing.Select((r, i) => (r, i)).Where(x => items[x.i] != null).ToList();
        var plan = AnnotationScale.Ajustar(antes, depois, known.Select(x => items[x.i]!).ToList(), oldScale.Value / 1000.0);
        if (plan.Mover.Count == 0 && known.Count > 0) return Render(def);

        if (def is IGroupedAnnotation) Ungroup(existing);
        var info = MarkingBuilder.Describe(def, Catalog);
        var materialName = def.Output.Material ?? PluginContext.Settings.DefaultMaterial;
        var z = PlaneZ(view);
        var keep = new HashSet<ElementId>(existing.Where((r, i) => items[i] == null).Select(r => r.Element.Id));
        XYZ P(Vec2 v) => new(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), z);
        var regionsToMake = new List<ItemDetalhe>();
        foreach (var (k, target) in plan.Mover)
        {
            var e = known[k].r.Element;
            try
            {
                switch (e)
                {
                    case TextNote tn:
                        tn.Coord = new XYZ(UnitConv.Ft(target.Ref.X), UnitConv.Ft(target.Ref.Y), tn.Coord.Z);
                        keep.Add(e.Id);
                        break;
                    case CurveElement ce:
                        ce.SetGeometryCurve(Line.CreateBound(P(target.Aneis[0][0]), P(target.Aneis[0][1])), true);
                        keep.Add(e.Id);
                        break;
                    case FilledRegion:
                        // Região não muda de contorno no lugar: é refeita com o mesmo estilo (a pessoa não a apagou).
                        regionsToMake.Add(target);
                        SafeDelete(e.Id);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Ajuste de escala {def.DisplayCode}", ex);
                keep.Add(e.Id);
            }
        }
        foreach (var k in plan.Apagar) SafeDelete(known[k].r.Element.Id);
        regionsToMake.AddRange(plan.Criar.Where(c => c.Tipo == TipoItemDetalhe.Regiao));
        foreach (var it in regionsToMake)
        {
            if (it.Fonte is not FonteRegiao src) continue;
            var delta = it.Ref - ItemDetalheRef(src);
            var shapes = src.Pecas.Select(p => p.Transform(v => v + delta)).ToList();
            foreach (var fr in CreateRegions(view, src.Color, shapes, z, result.Warnings, src.Rgb, src.Hatch))
            {
                Tag(fr, def, info, src.Color, materialName, 0, newGeo, false);
                keep.Add(fr.Id);
            }
        }
        var fresh = plan.Criar.Where(c => c.Tipo != TipoItemDetalhe.Regiao).Select(c => c.Fonte).OfType<Annotation2D>().ToList();
        foreach (var e in CreateAnnotations(view, fresh, z, result.Warnings))
        {
            MarkingStorage.Write(e, def, MarkingColor.Preta);
            keep.Add(e.Id);
        }
        if (def is IPlacedAnnotation pa && LinesAnchor(newGeo) is { } anchor) pa.DrawnAnchor = anchor;
        // A definição (posição seguida, âncora) fica gravada nos elementos do detalhe.
        foreach (var id in keep)
            if (_doc.GetElement(id) is { } el && MarkingStorage.Read(el) is { } r)
                MarkingStorage.Write(el, def, r.Color);
        if (def is IGroupedAnnotation grouped && keep.Count > 1) MakeGroup(keep, grouped.GroupName, result);
        ViewScaleMemory.Remember(_doc, view);
        result.Elements.AddRange(keep);
        return result;
    }

    private static Vec2 ItemDetalheRef(FonteRegiao src)
    {
        var pts = src.Pecas.SelectMany(p => p.Holes.Prepend(p.Outer)).SelectMany(r => r).ToList();
        return new Vec2((pts.Min(p => p.X) + pts.Max(p => p.X)) / 2, (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2);
    }

    /// <summary>Elemento de detalhe existente como item do ajuste de escala (nulo = não é texto, linha nem região).</summary>
    private static ItemDetalhe? ItemOf(Element e, IReadOnlyDictionary<ElementId, string> regionKeys)
    {
        try
        {
            switch (e)
            {
                case TextNote tn:
                    return ItemDetalhe.Texto(tn.Text ?? "", new Vec2(UnitConv.M(tn.Coord.X), UnitConv.M(tn.Coord.Y)));
                case CurveElement { ViewSpecific: true } ce when ce.GeometryCurve is Line { IsBound: true } l:
                    return new ItemDetalhe(TipoItemDetalhe.Linha, "", new[] { (IReadOnlyList<Vec2>)new[]
                    {
                        new Vec2(UnitConv.M(l.GetEndPoint(0).X), UnitConv.M(l.GetEndPoint(0).Y)), new Vec2(UnitConv.M(l.GetEndPoint(1).X), UnitConv.M(l.GetEndPoint(1).Y)),
                    } });
                case FilledRegion fr:
                {
                    var rings = new List<IReadOnlyList<Vec2>>();
                    foreach (var loop in fr.GetBoundaries())
                    {
                        var pts = new List<Vec2>();
                        foreach (var c in loop)
                        {
                            var t = c.Tessellate();
                            for (int i = 0; i + 1 < t.Count; i++) pts.Add(new Vec2(UnitConv.M(t[i].X), UnitConv.M(t[i].Y)));
                        }
                        if (pts.Count >= 3) rings.Add(pts);
                    }
                    if (rings.Count == 0) return null;
                    return new ItemDetalhe(TipoItemDetalhe.Regiao, regionKeys.GetValueOrDefault(fr.GetTypeId()) ?? "", rings);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Elemento de detalhe no ajuste de escala", ex);
        }
        return null;
    }

    public void Delete(string markingId)
    {
        foreach (var r in MarkingStorage.ById(_doc, markingId)) SafeDelete(r.Element.Id);
    }

    /// <summary>
    /// Grava a definição num elemento de dados (DataStorage, mesmo esquema das marcas), sem desenho: os elementos que ela tinha
    /// (a linha de corte e as marcas A–A das versões anteriores) saem.
    /// </summary>
    private RenderResult SaveAsData(MarkingDefinition def, List<StoredMarking> existing)
    {
        var result = new RenderResult();
        Ungroup(existing);
        var ds = existing.Select(r => r.Element).OfType<Autodesk.Revit.DB.ExtensibleStorage.DataStorage>().FirstOrDefault()
            ?? Autodesk.Revit.DB.ExtensibleStorage.DataStorage.Create(_doc);
        MarkingStorage.Write(ds, def, MarkingColor.Preta);
        // Sem desenho, nada a selecionar: o elemento de dados não entra na lista dos criados.
        foreach (var r in existing.Where(r => r.Element.Id != ds.Id)) SafeDelete(r.Element.Id);
        return result;
    }

    private void SafeDelete(ElementId id)
    {
        try { if (_doc.GetElement(id) != null) _doc.Delete(id); }
        catch (Exception ex) { Log.Error("Delete", ex); }
    }

    private View? ResolveView(MarkingDefinition def, RenderResult result)
    {
        View? view = null;
        if (!string.IsNullOrEmpty(def.Output.ViewId)) view = _doc.GetElement(def.Output.ViewId) as View;
        if (view == null && _activeView != null && SupportsDetail(_activeView))
        {
            view = _activeView;
            def.Output.ViewId = view.UniqueId;
        }
        if (view == null)
            result.Warnings.Add($"{def.DisplayCode}: a vista da representação 2D não existe mais – abra uma vista em planta e use 'Alternar 2D/3D'.");
        return view;
    }

    public static bool SupportsDetail(View v) =>
        !v.IsTemplate && (v is ViewPlan || v is ViewDrafting);

    private static double PlaneZ(View view)
    {
        if (view is ViewPlan vp && vp.GenLevel != null) return vp.GenLevel.ProjectElevation;
        return view.SketchPlane?.GetPlane().Origin.Z ?? 0;
    }

    // ------------------------------------------------------------------ 3D

    private List<GeometryObject> BuildSolids(IEnumerable<MarkingPiece> pieces, double zMeters, double thickness,
        ISurface? sampler, ElementId materialId, List<string> warnings, double aboveSurfaceM = 0.001)
    {
        var res = new List<GeometryObject>();
        var options = new SolidOptions(materialId, ElementId.InvalidElementId);
        int failures = 0, tiny = 0;
        var zBaseFt = UnitConv.Ft(zMeters);
        var above = UnitConv.Ft(Math.Max(0.001, aboveSurfaceM));
        var draped = sampler is { IsAvailable: true };
        // Sobre greide/nó a pista é feita de pisos planos (desvio ≤ 12 mm): a pintura fica 1,5 cm acima da superfície teórica
        // para nunca "afundar" no piso.
        // Sobre greide/nó a pista é feita de pisos planos: com os planos dela (TiledSampler) a pintura fica 4 mm acima do piso; sem
        // eles, 2,5 cm acima da superfície teórica (os pisos se afastam dela até 2 cm).
        if (sampler is TiledSampler) above = Math.Max(above, UnitConv.Ft(0.004));
        else if (sampler is GradeSampler or NodeSampler) above = Math.Max(above, UnitConv.Ft(0.025));

        foreach (var piece in pieces)
        {
            try
            {
                var t = UnitConv.Ft(piece.Thickness > 0 ? piece.Thickness : thickness);
                var lift = UnitConv.Ft(piece.Elevation);
                if (piece.Solid != null || piece.Profile != null)
                {
                    var z = zBaseFt;
                    var c = piece.Shape.Centroid;
                    if (draped && sampler!.LiftsSolids && sampler.TrySample(UnitConv.Ft(c.X), UnitConv.Ft(c.Y), zBaseFt, out var sz, out _)) z = sz + above;
                    if (piece.Round is { } round && RoundGeometry(round, z + lift, options) is { } exact) { res.Add(exact); continue; }
                    if (piece.Solid is { } poly) res.AddRange(PolyhedronGeometry(poly, z + lift, materialId));
                    else if (ProfileSolids(piece.Profile!, z + lift, options) is { Count: > 0 } ps) res.AddRange(ps);
                    else tiny++;
                    continue;
                }
                // Sobre superfícies: a peça inteira num plano ajustado ao terreno; dividida só onde o terreno dobra
                // (sem "quadradinhos" de tamanho fixo).
                var parts = !draped ? new List<(Polygon2, double, XYZ?)> { (piece.Shape, zBaseFt, null) }
                    : sampler is TiledSampler ts && ts.Clip(piece.Shape) is { Count: > 0 } clipped ? clipped
                    : DrapedParts(piece.Shape, sampler!, zBaseFt, 0).ToList();
                foreach (var (shape, zPart, normal) in parts)
                {
                    var z = draped ? zPart + above : zPart;
                    var loops = ToCurveLoops(shape, z + lift);
                    if (loops.Count == 0) { failures++; continue; }
                    var solid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, t, options);
                    if (normal != null && normal.Z < 0.99999)
                    {
                        var axis = XYZ.BasisZ.CrossProduct(normal);
                        if (axis.GetLength() > 1e-9)
                        {
                            var c = shape.Centroid;
                            var angle = XYZ.BasisZ.AngleTo(normal);
                            var tr = Transform.CreateRotationAtPoint(axis.Normalize(), angle, new XYZ(UnitConv.Ft(c.X), UnitConv.Ft(c.Y), z));
                            solid = SolidUtils.CreateTransformed(solid, tr);
                        }
                    }
                    res.Add(solid);
                }
            }
            catch (Exception ex)
            {
                failures++;
                if (failures == 1) Log.Error("CreateExtrusionGeometry", ex);
            }
        }
        if (failures > 0) warnings.Add($"{failures} peça(s) não puderam ser modeladas (geometria muito pequena ou inválida).");
        if (tiny > 0) warnings.Add($"{tiny} peça(s) menores que a tolerância do Revit (cerca de 1 mm) ficaram de fora.");
        return res;
    }

    /// <summary>
    /// Divide a peça até cada parte ficar num plano do terreno (desvio ≤ 1,5 cm): devolve a parte, a cota do plano no
    /// centroide (pés) e a normal do plano. Rampas constantes = peça única; só as mudanças de greide geram emendas.
    /// </summary>
    private static IEnumerable<(Polygon2 Shape, double Z, XYZ? Normal)> DrapedParts(Polygon2 shape, ISurface s, double zHintFt, int depth)
    {
        var ring = CurveTools.Densify(shape.Outer.Append(shape.Outer[0]).ToList(), 5.0);
        var step = Math.Max(1, ring.Count / 32);
        var pts = new List<Vec2>();
        for (int i = 0; i < ring.Count; i += step) pts.Add(ring[i]);
        var cen = shape.Centroid;
        pts.Add(cen);
        var samples = new List<(double X, double Y, double Z)>();
        foreach (var v in pts)
        {
            double x = UnitConv.Ft(v.X), y = UnitConv.Ft(v.Y);
            if (s.TrySample(x, y, zHintFt, out var z, out _)) samples.Add((x, y, z));
        }
        if (samples.Count < 3)
        {
            yield return (shape, samples.Count > 0 ? samples.Average(q => q.Z) : zHintFt, null);
            yield break;
        }
        // Plano por mínimos quadrados: z = a·x + b·y + c (coordenadas centradas).
        double mx = samples.Average(q => q.X), my = samples.Average(q => q.Y), mz = samples.Average(q => q.Z);
        double sxx = 0, sxy = 0, syy = 0, sxz = 0, syz = 0;
        foreach (var q in samples)
        {
            double dx = q.X - mx, dy = q.Y - my, dz = q.Z - mz;
            sxx += dx * dx; sxy += dx * dy; syy += dy * dy; sxz += dx * dz; syz += dy * dz;
        }
        var det = sxx * syy - sxy * sxy;
        double a = 0, b = 0;
        if (Math.Abs(det) > 1e-12) { a = (sxz * syy - syz * sxy) / det; b = (syz * sxx - sxz * sxy) / det; }
        else if (sxx > 1e-12) a = sxz / sxx;
        else if (syy > 1e-12) b = syz / syy;
        var dev = samples.Max(q => Math.Abs(mz + a * (q.X - mx) + b * (q.Y - my) - q.Z));
        var (mn, mxp) = shape.Bounds;
        var ext = Math.Max(mxp.X - mn.X, mxp.Y - mn.Y);
        if (dev <= UnitConv.Ft(s is GradeSampler or NodeSampler ? 0.012 : 0.015) || depth >= 9 || ext < 0.8)
        {
            var zc = mz + a * (UnitConv.Ft(cen.X) - mx) + b * (UnitConv.Ft(cen.Y) - my);
            yield return (shape, zc, new XYZ(-a, -b, 1).Normalize());
            yield break;
        }
        // Divide ao meio pelo lado mais longo.
        List<Polygon2> halves;
        if (mxp.X - mn.X >= mxp.Y - mn.Y)
        {
            var xm = (mn.X + mxp.X) / 2;
            halves = PolygonOps.Intersect(new[] { shape }, new[] { Polygon2.Rectangle(new Vec2(mn.X - 1, mn.Y - 1), new Vec2(xm, mxp.Y + 1)) })
                .Concat(PolygonOps.Intersect(new[] { shape }, new[] { Polygon2.Rectangle(new Vec2(xm, mn.Y - 1), new Vec2(mxp.X + 1, mxp.Y + 1)) })).ToList();
        }
        else
        {
            var ym = (mn.Y + mxp.Y) / 2;
            halves = PolygonOps.Intersect(new[] { shape }, new[] { Polygon2.Rectangle(new Vec2(mn.X - 1, mn.Y - 1), new Vec2(mxp.X + 1, ym)) })
                .Concat(PolygonOps.Intersect(new[] { shape }, new[] { Polygon2.Rectangle(new Vec2(mn.X - 1, ym), new Vec2(mxp.X + 1, mxp.Y + 1)) })).ToList();
        }
        foreach (var h in halves.Where(h => h.Area > 1e-4))
            foreach (var part in DrapedParts(h, s, zHintFt, depth + 1))
                yield return part;
    }

    /// <summary>
    /// Sólido de revolução com superfície curva exata: cilindro (extrusão de círculo), tronco de cone (transição entre dois
    /// círculos) ou anel/tubo com raio interno (revolução). Nulo se o Revit recusar – a peça cai na aproximação poliédrica.
    /// </summary>
    private static Solid? RoundGeometry(RoundSolid r, double zBaseFt, SolidOptions options)
    {
        try
        {
            XYZ P(Vec3 v) => new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), zBaseFt + UnitConv.Ft(v.Z));
            var a = P(r.A);
            var b = P(r.B);
            var axis = b - a;
            var len = axis.GetLength();
            if (len < 0.005) return null;
            var dz = axis.Normalize();
            var refv = Math.Abs(dz.Z) > 0.95 ? XYZ.BasisX : XYZ.BasisZ;
            var ux = dz.CrossProduct(refv).Normalize();
            var uy = dz.CrossProduct(ux).Normalize();
            double ra = UnitConv.Ft(r.RA), rb = UnitConv.Ft(r.RB), ia = UnitConv.Ft(r.InnerA), ib = UnitConv.Ft(r.InnerB);
            CurveLoop Circle(XYZ c, double rad)
            {
                var plane = Plane.CreateByOriginAndBasis(c, ux, uy);
                var loop = new CurveLoop();
                loop.Append(Arc.Create(plane, rad, 0, Math.PI));
                loop.Append(Arc.Create(plane, rad, Math.PI, 2 * Math.PI));
                return loop;
            }
            var hollow = ia > 0.003 || ib > 0.003;
            if (Math.Abs(ra - rb) < 1e-6 && (!hollow || Math.Abs(ia - ib) < 1e-6))
            {
                var loops = new List<CurveLoop> { Circle(a, ra) };
                if (hollow && ia < ra - 0.003) loops.Add(Circle(a, ia));
                return GeometryCreationUtilities.CreateExtrusionGeometry(loops, dz, len, options);
            }
            if (!hollow)
                return GeometryCreationUtilities.CreateLoftGeometry(new List<CurveLoop> { Circle(a, Math.Max(0.003, ra)), Circle(b, Math.Max(0.003, rb)) }, options);
            // Anel cônico (paredes de poço, cones de redução): revolução de um trapézio fora do eixo.
            XYZ Q(double x, double z) => a + ux * x + dz * z;
            var pts = new[] { Q(Math.Max(0.003, ia), 0), Q(ra, 0), Q(rb, len), Q(Math.Max(0.003, ib), len) };
            var profile = new CurveLoop();
            for (int i = 0; i < pts.Length; i++) profile.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Length]));
            return GeometryCreationUtilities.CreateRevolvedGeometry(new Frame(a, ux, uy, dz), new List<CurveLoop> { profile }, 0, 2 * Math.PI, options);
        }
        catch (Exception ex)
        {
            Log.Error("RoundGeometry", ex);
            return null;
        }
    }

    /// <summary>Poliedro (rampas, abas) via TessellatedShapeBuilder – sólido quando possível, senão malha.</summary>
    /// <summary>
    /// Tira da lista os objetos que a forma direta não aceita (<c>DirectShape.IsValidShape</c>, um a um) e avisa quantos e onde
    /// (m). Um objeto recusado fazia o Revit recusar a forma inteira e a cor toda da marca sumia
    /// (ilha da rotatória oval, Autoteste de 2026-10-20 15:12).
    /// </summary>
    private static void DropInvalidShapes(DirectShape ds, List<GeometryObject> objs, string what, List<string> warnings)
    {
        var where = new List<string>();
        for (int i = objs.Count - 1; i >= 0; i--)
        {
            bool ok;
            try { ok = ds.IsValidShape(new List<GeometryObject> { objs[i] }); }
            catch { ok = false; }
            if (ok) continue;
            where.Add(Where(objs[i]));
            objs.RemoveAt(i);
        }
        if (where.Count == 0) return;
        Log.Info($"Forma direta {what}: {where.Count} objeto(s) inválidos para o Revit ficaram de fora – em {string.Join("; ", where)}.");
        warnings.Add($"{what}: {where.Count} sólido(s) inválidos para forma direta ficaram de fora (em {string.Join("; ", where.Take(3))}{(where.Count > 3 ? "; …" : "")}).");
    }

    /// <summary>Centro do objeto em planta (m, coordenadas internas), para localizar a peça descartada.</summary>
    private static string Where(GeometryObject g)
    {
        try
        {
            XYZ? c = g switch
            {
                Solid so when so.GetBoundingBox() is { } bb => bb.Transform.OfPoint((bb.Min + bb.Max) / 2),
                Mesh me when me.Vertices.Count > 0 => me.Vertices.Aggregate(XYZ.Zero, (a, v) => a + v) / me.Vertices.Count,
                _ => null,
            };
            return c == null ? g.GetType().Name : $"{UnitConv.M(c.X):0.0}; {UnitConv.M(c.Y):0.0}";
        }
        catch { return g.GetType().Name; }
    }

    private static IList<GeometryObject> PolyhedronGeometry(Polyhedron p, double zBaseFt, ElementId materialId)
    {
        var b = new TessellatedShapeBuilder { Target = TessellatedShapeBuilderTarget.AnyGeometry, Fallback = TessellatedShapeBuilderFallback.Mesh };
        b.OpenConnectedFaceSet(true);
        foreach (var f in p.Faces)
        {
            var pts = new List<XYZ>();
            foreach (var v in f)
            {
                var x = new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), zBaseFt + UnitConv.Ft(v.Z));
                if (pts.Count == 0 || !pts[^1].IsAlmostEqualTo(x)) pts.Add(x);
            }
            while (pts.Count > 3 && pts[0].IsAlmostEqualTo(pts[^1])) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) continue;
            b.AddFace(new TessellatedFace(pts, materialId));
        }
        b.CloseConnectedFaceSet();
        b.Build();
        var r = b.GetBuildResult();
        return r.GetGeometricalObjects();
    }

    /// <summary>
    /// Sólidos do perfil: o próprio; se o Revit recusa o contorno (que se toca, ou menor que a tolerância), as partes limpas
    /// do <see cref="ProfileCleanup"/>. Vazio = peça menor que a tolerância (nada a modelar); recusa das partes propaga.
    /// </summary>
    private List<Solid> ProfileSolids(ProfileSolid p, double zBaseFt, SolidOptions options)
    {
        try { return new List<Solid> { ProfileSolidGeometry(p, zBaseFt, options) }; }
        catch (Exception)
        {
            return ProfileCleanup.Parts(p.Profile).Select(part => ProfileSolidGeometry(p with { Profile = part }, zBaseFt, options)).ToList();
        }
    }

    /// <summary>Sólido de perfil vertical: contorno no plano (XDir, Z) extrudado ao longo de ExtrudeDir.</summary>
    private Solid ProfileSolidGeometry(ProfileSolid p, double zBaseFt, SolidOptions options)
    {
        var x = p.XDir.Normalized();
        var e = p.ExtrudeDir.Normalized();
        var depth = p.Depth;
        if (depth < 0) { depth = -depth; e = -e; }
        XYZ Map(Vec2 v)
        {
            var plan = p.Origin + x * v.X;
            return new XYZ(UnitConv.Ft(plan.X), UnitConv.Ft(plan.Y), zBaseFt + UnitConv.Ft(v.Y));
        }
        var loops = new List<CurveLoop>();
        var outer = ToCurveLoop3D(p.Profile.Outer.Select(Map).ToList());
        if (outer == null) throw new InvalidOperationException("Perfil inválido.");
        loops.Add(outer);
        foreach (var h in p.Profile.Holes)
        {
            var hl = ToCurveLoop3D(h.Select(Map).ToList());
            if (hl != null) loops.Add(hl);
        }
        var dir = new XYZ(e.X, e.Y, 0);
        return GeometryCreationUtilities.CreateExtrusionGeometry(loops, dir, UnitConv.Ft(Math.Max(0.001, depth)), options);
    }

    /// <summary>
    /// Remove vértices quase coincidentes e "espinhos" (ida e volta na mesma direção) que fazem o Revit recusar o
    /// contorno – aparecem em recortes oblíquos e em curvas com raio pequeno.
    /// </summary>
    private static List<XYZ> Sanitize(List<XYZ> pts, double tol)
    {
        bool changed = true;
        for (int pass = 0; pass < 6 && changed && pts.Count >= 3; pass++)
        {
            changed = false;
            var res = new List<XYZ>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[(i - 1 + pts.Count) % pts.Count];
                var b = pts[i];
                var c = pts[(i + 1) % pts.Count];
                var u = b - a;
                var v = c - b;
                var lu = u.GetLength();
                var lv = v.GetLength();
                if (lu <= tol) { changed = true; continue; }
                if (lu > 1e-9 && lv > 1e-9 && u.DotProduct(v) / (lu * lv) < -0.9995)
                {
                    // Espinho: a ponta é cortada no pé da perpendicular sobre o lado mais longo (ver ProfileCleanup.SpikeFoot).
                    changed = true;
                    if (ProfileCleanup.SpikeFoot(new Vec2(a.X, a.Y), new Vec2(b.X, b.Y), new Vec2(c.X, c.Y), tol) is { } f) res.Add(new XYZ(f.X, f.Y, b.Z));
                    continue;
                }
                res.Add(b);
            }
            pts = res;
        }
        return pts;
    }

    private CurveLoop? ToCurveLoop3D(List<XYZ> raw)
    {
        var tol = _doc.Application.ShortCurveTolerance * 1.5;
        var pts = new List<XYZ>();
        foreach (var p in raw)
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > tol) pts.Add(p);
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= tol) pts.RemoveAt(pts.Count - 1);
        pts = Sanitize(pts, tol);
        if (pts.Count < 3) return null;
        var loop = new CurveLoop();
        for (int i = 0; i < pts.Count; i++) loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
        return loop;
    }

    /// <summary>Contorno pronto para o Revit (ver <see cref="ElementPlan.Prepare"/>).</summary>
    private static Polygon2 Prepare(Polygon2 poly) => ElementPlan.Prepare(poly);

    private IList<CurveLoop> ToCurveLoops(Polygon2 poly, double zFt, bool raw = false)
    {
        if (!raw) poly = Prepare(poly);
        var loops = new List<CurveLoop>();
        var outer = ToCurveLoop(poly.Outer, zFt, raw);
        if (outer == null) return loops;
        loops.Add(outer);
        foreach (var h in poly.Holes)
        {
            var hl = ToCurveLoop(h, zFt, raw);
            if (hl != null) loops.Add(hl);
        }
        // Arcos de anéis vizinhos não podem se tocar: se o ajuste fez um furo encostar no contorno, volta às retas.
        if (!raw && BoundaryTolerance > 0 && loops.Count > 1 && !BoundaryValidation.IsValidHorizontalBoundary(loops))
        {
            loops.Clear();
            var o2 = ToCurveLoopLines(poly.Outer, zFt);
            if (o2 == null) return loops;
            loops.Add(o2);
            foreach (var h in poly.Holes) if (ToCurveLoopLines(h, zFt) is { } hl) loops.Add(hl);
        }
        return loops;
    }

    /// <summary>
    /// Contorno do Revit com linhas e arcos inteiros (ver <see cref="BoundaryFit"/>): cada reta vira uma linha só e cada
    /// curva vira arcos – o esboço do piso fica limpo e leve. <paramref name="raw"/>: vértices mantidos um a um (pontos de
    /// apoio da edição de forma sobre o terreno). Nos dois modos as retas quase alinhadas aos eixos ficam exatas; no modo
    /// <paramref name="raw"/> o vértice anda no máximo 0,5 mm – a edição de forma lê as cotas na posição final de cada
    /// vértice, então os pontos de apoio continuam valendo (Autoteste de 2026-10-20 09:44: 34 avisos de retas a 0,0002° –
    /// deslocamento de 0,0 mm – em pisos que acompanham o terreno, que pulavam o alinhamento).
    /// </summary>
    private CurveLoop? ToCurveLoop(IReadOnlyList<Vec2> ring, double zFt, bool raw = false)
    {
        var tol = _doc.Application.ShortCurveTolerance * 1.5;
        var pts = SketchPoints(ring, zFt, tol);
        if (pts.Count < 3) return null;
        if (!raw && BoundaryTolerance > 0)
        {
            try
            {
                var fitted = FittedLoop(pts.Select(p => new Vec2(UnitConv.M(p.X), UnitConv.M(p.Y))).ToList(), zFt, tol);
                if (fitted != null) return fitted;
            }
            catch (Exception ex) { Log.Error("Contorno com arcos", ex); }
        }
        {
            // Retas "quase" alinhadas aos eixos (resíduo de recorte) ficam exatas: o Revit avisa "ligeiramente fora da linha
            // central" em cada uma. Depois, pontos que ficaram perto demais saem (curva curta).
            var ring2 = pts.Select(p => new Vec2(UnitConv.M(p.X), UnitConv.M(p.Y))).ToList();
            var snapped = AxisSnap.Snap(ring2.Select((p, i) => new Core.Geometry.BoundarySegment(p, ring2[(i + 1) % ring2.Count])).ToList(),
                raw ? AxisSnap.MaxOffset : SnapOffset);
            pts = Sanitize(snapped.Select(x => new XYZ(UnitConv.Ft(x.Start.X), UnitConv.Ft(x.Start.Y), zFt)).ToList(), tol);
            if (pts.Count < 3) return null;
        }
        var loop = new CurveLoop();
        for (int i = 0; i < pts.Count; i++)
            loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
        return loop;
    }

    /// <summary>Vértices do esboço (pés): sem pontos colados (abaixo de <paramref name="tol"/>) e sem espinhos.</summary>
    private List<XYZ> SketchPoints(IReadOnlyList<Vec2> ring, double zFt, double tol)
    {
        var pts = new List<XYZ>();
        foreach (var v in ring)
        {
            var p = new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), zFt);
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > tol) pts.Add(p);
        }
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= tol) pts.RemoveAt(pts.Count - 1);
        return Sanitize(pts, tol);
    }

    /// <summary>
    /// Contornos já ajustados em conjunto (<see cref="BoundaryFit.FitRings"/>) para os pisos físicos da marca em criação, pelos
    /// pontos que <see cref="FittedLoop"/> recebe: a borda comum a dois pisos sai igual nos dois (antes cada um ajustava a sua e
    /// eles se sobrepunham numa lasca ao longo das curvas – emenda de vias em curva, rotatórias).
    /// </summary>
    private Dictionary<string, List<Core.Geometry.BoundarySegment>> _sharedFits = new();

    private static string RingKey(IReadOnlyList<Vec2> ring) => string.Join(";", ring.Select(v => $"{v.X:R},{v.Y:R}"));

    private void PrepareSharedFit(IEnumerable<Polygon2> shapes)
    {
        _sharedFits = new();
        if (BoundaryTolerance <= 0) return;
        using var perf = Perf.Measure("Ajuste conjunto dos contornos (FitRings)");
        var tol = _doc.Application.ShortCurveTolerance * 1.5;
        var rings = new List<IReadOnlyList<Vec2>>();
        foreach (var shape in shapes)
        {
            Polygon2 poly;
            // Como em CreateFloors: a parte preparada passa de novo pelo preparo em ToCurveLoops.
            try { poly = Prepare(Prepare(shape)); }
            catch { continue; }
            foreach (var r in new[] { poly.Outer }.Concat(poly.Holes))
            {
                var pts = SketchPoints(r, 0, tol);
                if (pts.Count >= 3) rings.Add(pts.Select(p => new Vec2(UnitConv.M(p.X), UnitConv.M(p.Y))).ToList());
            }
        }
        if (rings.Count < 2) return;
        try
        {
            var fits = BoundaryFit.FitRings(rings, BoundaryTolerance, UnitConv.M(tol), _arcCenters, SnapOffset);
            for (int i = 0; i < rings.Count; i++)
                if (fits[i] is { } f) _sharedFits.TryAdd(RingKey(rings[i]), f);
        }
        catch (Exception ex)
        {
            Log.Error("Ajuste conjunto dos contornos", ex);
            _sharedFits.Clear();
        }
    }

    /// <summary>Só retas (sem o ajuste de arcos), mas com o alinhamento aos eixos: contorno com furos cujos arcos se tocariam.</summary>
    private CurveLoop? ToCurveLoopLines(IReadOnlyList<Vec2> ring, double zFt) => ToCurveLoop(ring, zFt, raw: true);

    /// <summary>Desvio máximo do contorno ajustado (m) – configurável em Configurações (0 = retas uma a uma, como antes).</summary>
    private static double BoundaryTolerance => Math.Max(0, PluginContext.Settings.BoundaryTolerance);

    /// <summary>
    /// Quanto um vértice pode andar para a reta ficar exatamente alinhada: o mesmo desvio já aceito no ajuste do contorno
    /// (o ajuste é que desloca as pontas das retas em 1 a 4 mm), nunca menos que os 0,5 mm dos resíduos de recorte.
    /// </summary>
    private static double SnapOffset => Math.Max(AxisSnap.MaxOffset, BoundaryTolerance);

    /// <summary>Centros exatos das curvas da marca em criação (ver <see cref="MarkingGeometry.ArcCenters"/>).</summary>
    private IReadOnlyList<Vec2> _arcCenters = Array.Empty<Vec2>();

    private CurveLoop? FittedLoop(List<Vec2> ring, double zFt, double shortFt)
    {
        // Retas quase alinhadas aos eixos ficam exatas (ver AxisSnap): sem o aviso "ligeiramente fora da linha central"; ajuste
        // que se cruza tenta tolerância menor e, por fim, volta às retas (BoundaryFit.FitRing).
        var segs = _sharedFits.TryGetValue(RingKey(ring), out var shared) ? shared
            : BoundaryFit.FitRing(ring, BoundaryTolerance, UnitConv.M(shortFt), _arcCenters, SnapOffset);
        if (segs == null) return null;
        XYZ P(Vec2 v) => new(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), zFt);
        var loop = new CurveLoop();
        foreach (var s in segs)
        {
            var a = P(s.Start);
            var b = P(s.End);
            if (a.DistanceTo(b) <= shortFt) return null;
            Curve c;
            if (s.Mid is { } m)
            {
                try { c = Arc.Create(a, b, P(m)); }
                catch { c = Line.CreateBound(a, b); }
            }
            else c = Line.CreateBound(a, b);
            loop.Append(c);
        }
        return loop.IsOpen() ? null : loop;
    }

    /// <summary>Marca pintada que costuma ficar sobre uma pintura de fundo (ver <see cref="ElementPlan.IsOverlayPaint"/>).</summary>
    private static bool IsOverlay(MarkingDefinition d) => ElementPlan.IsOverlayPaint(d);

    // ------------------------------------------------------------------ pisos

    private static bool PaintDefinition(MarkingDefinition def) => ElementPlan.PaintDefinition(def);

    private static bool PaintFloorEligible(MarkingPiece p) => ElementPlan.PaintFloorEligible(p);

    /// <summary>
    /// Um piso com vários contornos (pintura de uma cor): topo na cota da pintura; no greide, inclinado no plano dado.
    /// Nulo se o Revit recusar (as peças seguem uma a uma).
    /// </summary>
    private Floor? CreateMultiFloor(List<Polygon2> shapes, MarkingColor color, double elevation, double thickness, Plane3? plane, double baseZm,
        ElementId? userType, ref string? reason)
    {
        if (shapes.Count == 0) return null;
        var all = shapes.SelectMany(x => x.Outer).ToList();
        var c0 = new Vec2(all.Average(v => v.X), all.Average(v => v.Y));
        var groundFt = plane is { } pl ? UnitConv.Ft(pl.Z(c0)) : (double?)null;
        var topFt = groundFt is { } g ? g + UnitConv.Ft(elevation + thickness) : UnitConv.Ft(baseZm + elevation + thickness);
        var level = LevelFor(topFt);
        if (level == null) return null;
        ElementId typeId;
        try
        {
            typeId = userType != null && userType != ElementId.InvalidElementId && _doc.GetElement(userType) is FloorType ? userType : FloorType(color, thickness);
        }
        catch (Exception ex) { reason = ex.Message; return null; }
        if (typeId == ElementId.InvalidElementId) return null;
        var loops = new List<CurveLoop>();
        foreach (var sh in shapes)
            try { loops.AddRange(ToCurveLoops(sh, level.ProjectElevation)); }
            catch (Exception ex)
            {
                // Contorno degenerado (lado nulo, laço): a peça fica de fora do piso – contado e avisado, não some em silêncio.
                _skippedContours++;
                Log.Info($"Piso {color}: contorno degenerado ignorado ({sh.Area:0.###} m²) – {ex.Message}");
            }
        if (loops.Count == 0) return null;
        Floor? f = null;
        try
        {
            if (!BoundaryValidation.IsValidHorizontalBoundary(loops)) { reason = "contornos da pintura inválidos para um único piso"; return null; }
            var key = FloorSignature.Key(loops, typeId, level.Id, topFt, PlaneKey(plane));
            if (Reuse(key) is { } same) return same;
            f = Floor.Create(_doc, loops, typeId, level.Id, false, null, 0.0);
            f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(topFt - level.ProjectElevation);
            FloorSignature.Write(f, loops);
            FloorSignature.WriteKey(f, key);
            if (plane is { IsLevel: false } pp) _shapeEdits.Add(new ShapeEdit(f, pp, null, null, groundFt!.Value));
            return f;
        }
        catch (Exception ex)
        {
            Log.Error("Piso da pintura", ex);
            reason = ex.Message;
            if (f != null) SafeDelete(f.Id);
            return null;
        }
    }

    private static bool FloorEligible(MarkingDefinition def, MarkingPiece p) => ElementPlan.FloorEligible(def, p);

    private readonly Dictionary<(MarkingColor, int), ElementId> _floorTypes = new();
    private List<Level>? _levels;

    /// <summary>Tipo de piso "SV - {material} {espessura}" (uma camada com o material da sinalização).</summary>
    private ElementId FloorType(MarkingColor color, double thicknessM)
    {
        var mm = (int)Math.Round(thicknessM * 1000);
        if (_floorTypes.TryGetValue((color, mm), out var id)) return id;
        var name = $"SV - {StyleService.ColorName(color)} {mm / 10.0:0.#} cm";
        var types = new FilteredElementCollector(_doc).OfClass(typeof(FloorType)).Cast<FloorType>().ToList();
        var ft = types.FirstOrDefault(t => t.Name == name);
        if (ft != null)
        {
            // Tipos criados por versões anteriores podem ter herdado a camada variável do tipo base – ou a espessura dele,
            // quando o Revit recusou a estrutura: o piso sairia mais grosso que o nome e invadiria os de baixo (D-011).
            try
            {
                var cs0 = ft.GetCompoundStructure();
                if (cs0 != null)
                {
                    var fix = false;
                    if (cs0.VariableLayerIndex >= 0) { cs0.VariableLayerIndex = -1; fix = true; }
                    var width = UnitConv.M(cs0.GetWidth());
                    if (FloorTypeNames.Mismatch(name, width))
                    {
                        var keep = Math.Max(0, cs0.StructuralMaterialIndex);
                        for (int i = cs0.LayerCount - 1; i >= 0; i--) if (i != keep && cs0.LayerCount > 1) cs0.DeleteLayer(i);
                        cs0.SetLayerWidth(0, UnitConv.Ft(thicknessM));
                        fix = true;
                        Log.Info($"Tipo de piso \"{name}\": estrutura de {width * 100:0.#} cm corrigida para {thicknessM * 100:0.#} cm.");
                    }
                    if (fix) ft.SetCompoundStructure(cs0);
                }
            }
            catch (Exception ex) { Log.Error("FloorType (estrutura do tipo existente)", ex); }
        }
        if (ft == null)
        {
            var baseType = types.FirstOrDefault(t => !t.IsFoundationSlab) ?? types.FirstOrDefault();
            if (baseType == null) return ElementId.InvalidElementId;
            ft = (FloorType)baseType.Duplicate(name);
            // A estrutura do tipo copiado é reduzida a uma camada (uma estrutura nova pode ter condição de EndCap
            // inválida para pisos e ser recusada pelo Revit).
            try
            {
                var cs = ft.GetCompoundStructure();
                if (cs == null) throw new InvalidOperationException("tipo sem estrutura composta");
                var keep = Math.Max(0, cs.StructuralMaterialIndex);
                for (int i = cs.LayerCount - 1; i >= 0; i--) if (i != keep && cs.LayerCount > 1) cs.DeleteLayer(i);
                cs.SetLayerWidth(0, UnitConv.Ft(thicknessM));
                cs.SetMaterialId(0, Styles.Material(color));
                cs.SetLayerFunction(0, MaterialFunctionAssignment.Structure);
                cs.EndCap = EndCapCondition.NoEndCap;
                // Sem camada variável: na edição de forma o piso inteiro acompanha o greide com espessura constante (com camada
                // variável o Revit afina a laje e recusa: "muito fino para seu tipo").
                cs.VariableLayerIndex = -1;
                ft.SetCompoundStructure(cs);
            }
            catch (Exception ex)
            {
                Log.Error("FloorType (estrutura copiada)", ex);
                try
                {
                    var cs = CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, UnitConv.Ft(thicknessM), Styles.Material(color));
                    cs.EndCap = EndCapCondition.NoEndCap;
                    ft.SetCompoundStructure(cs);
                }
                catch (Exception ex2)
                {
                    // Fica o tipo copiado (espessura/material do tipo base) – melhor um piso do que nenhum.
                    Log.Error("FloorType (estrutura nova)", ex2);
                }
            }
        }
        _floorTypes[(color, mm)] = ft.Id;
        return ft.Id;
    }

    private Level? LevelFor(double zFt)
    {
        _levels ??= new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
        if (_levels.Count == 0) return null;
        return _levels.LastOrDefault(l => l.ProjectElevation <= zFt + 0.01) ?? _levels[0];
    }

    /// <summary>
    /// Cria o(s) piso(s) da peça (topo na cota da peça). Tenta o contorno original, depois limpo de lascas e, por fim,
    /// dividido sem furos. Lista vazia se o Revit recusar todas as tentativas (<paramref name="reason"/> recebe o motivo).
    /// </summary>
    /// <summary>
    /// Tipos de piso que o usuário trocou nos pisos anteriores da marca, por cor e pela espessura do tipo que o plugin tinha
    /// dado (nula = qualquer espessura da cor). Trocado = o tipo atual difere do gravado na chave do piso ao criá-lo; piso sem
    /// chave (versões antigas) conta como trocado só se o tipo não for um tipo do plugin. Antes valia o tipo do primeiro piso
    /// de cada cor para todas as peças dela: numa nova geração, a gola da árvore da ilha (12 cm) recebia o tipo de 15 cm do
    /// meio-fio e afundava 3 cm na ilha – 150 pares sobrepostos nas rotatórias (Autoteste de 2026-10-20 15:12).
    /// </summary>
    private Dictionary<(MarkingColor Color, double? Thickness), ElementId> UserFloorTypes(IEnumerable<StoredMarking> oldFloors)
    {
        var res = new Dictionary<(MarkingColor, double?), ElementId>();
        foreach (var r in oldFloors)
        {
            try
            {
                var current = r.Element.GetTypeId();
                double? thickness = null;
                if (FloorSignature.ReadKey(r.Element) is { } key)
                {
                    var parts = key.Split('|');
                    if (parts.Length < 5 || !long.TryParse(parts[4], out var created) || created == current.Value) continue;
                    thickness = FloorTypeNames.Thickness(_doc.GetElement(new ElementId(created))?.Name);
                }
                else if (FloorTypeNames.Thickness(_doc.GetElement(current)?.Name) != null) continue;
                res.TryAdd((r.Color, thickness), current);
            }
            catch (Exception ex) { Log.Error("Tipo de piso do usuário", ex); }
        }
        return res;
    }

    /// <summary>Tipo trocado pelo usuário para a peça (mesma cor e espessura, ou da cor inteira); nulo = o do plugin.</summary>
    private static ElementId? UserType(Dictionary<(MarkingColor Color, double? Thickness), ElementId> types, MarkingColor color, double thickness)
    {
        foreach (var ((c, t), id) in types)
            if (c == color && t is { } tt && Math.Abs(tt - FloorTypeNames.Rounded(thickness)) <= 0.0005) return id;
        return types.GetValueOrDefault((color, null));
    }

    private List<Floor> CreateFloors(MarkingPiece piece, double baseZm, ElementId? userType, ref string? reason, ISurface? terrain = null,
        Plane3? plane = null)
    {
        var res = new List<Floor>();
        var topFt = UnitConv.Ft(baseZm + piece.Elevation + piece.Thickness);
        var c0 = piece.Shape.Centroid;
        double? groundFt = null;
        if (plane is { } pl)
        {
            groundFt = UnitConv.Ft(pl.Z(c0));
            topFt = groundFt.Value + UnitConv.Ft(piece.Elevation + piece.Thickness);
        }
        else if (terrain != null && terrain.TrySample(UnitConv.Ft(c0.X), UnitConv.Ft(c0.Y), topFt, out var gz, out _))
        {
            groundFt = gz;
            topFt = gz + UnitConv.Ft(piece.Elevation + piece.Thickness);
        }
        var level = LevelFor(topFt);
        if (level == null) { reason = "o projeto não tem níveis"; return res; }
        ElementId typeId;
        try
        {
            typeId = userType != null && userType != ElementId.InvalidElementId && _doc.GetElement(userType) is FloorType
                ? userType : FloorType(piece.Color, piece.Thickness);
        }
        catch (Exception ex)
        {
            Log.Error("FloorType", ex);
            reason = "não foi possível criar o tipo de piso: " + ex.Message;
            return res;
        }
        if (typeId == ElementId.InvalidElementId) { reason = "o projeto não tem nenhum tipo de piso"; return res; }

        // No terreno/greide, o contorno ganha vértices a cada 2–3 m (pontos de apoio da deformação do piso). O contorno já
        // preparado NÃO passa de novo pela limpeza (ela removeria os vértices colineares e as bordas longas ficariam retas).
        var shaped = groundFt != null && plane == null;
        // No greide: vértices nas estacas da grade da via (dos dois lados, emparelhados) – malha regular e limpa.
        Polygon2 Prep(Polygon2 p) => !shaped ? p : terrain is GradeSampler gsp ? GradeFloors.Resample(p, gsp.Surface, gsp.Grid) : Densified(p, terrain is NodeSampler ? 1.5 : 3.0);
        var attempts = new List<Func<List<Polygon2>>>
        {
            () => new List<Polygon2> { Prep(Prepare(piece.Shape)) },
            () => PolygonOps.Clean(new[] { piece.Shape }),
            () => PolygonOps.Clean(new[] { piece.Shape }).SelectMany(p => PolygonOps.SplitHoles(p)).ToList(),
        };
        foreach (var attempt in attempts)
        {
            List<Polygon2> shapes;
            try { shapes = attempt(); }
            catch { continue; }
            if (shapes.Count == 0) continue;
            var created = new List<Floor>();
            var ok = true;
            foreach (var shape in shapes)
            {
                var loops = ToCurveLoops(shape, level.ProjectElevation, raw: shaped);
                if (loops.Count == 0) continue;
                try
                {
                    if (!BoundaryValidation.IsValidHorizontalBoundary(loops)) { ok = false; reason = "contorno inválido para o esboço do piso"; break; }
                    // Piso que acompanha o terreno (edição de forma com a malha do terreno) é sempre refeito; os demais com a
                    // mesma chave (contorno, tipo, nível, cota e plano) ficam como estão.
                    var key = plane == null && groundFt != null && terrain != null ? null : FloorSignature.Key(loops, typeId, level.Id, topFt, PlaneKey(plane));
                    if (key != null && Reuse(key) is { } same) { created.Add(same); continue; }
                    var f = Floor.Create(_doc, loops, typeId, level.Id, false, null, 0.0);
                    f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(topFt - level.ProjectElevation);
                    FloorSignature.Write(f, loops);
                    if (key != null) FloorSignature.WriteKey(f, key);
                    if (plane is { } pp) { if (!pp.IsLevel) _shapeEdits.Add(new ShapeEdit(f, pp, null, null, groundFt!.Value)); }
                    else if (groundFt != null && terrain != null) _shapeEdits.Add(new ShapeEdit(f, null, shape, terrain, groundFt.Value));
                    created.Add(f);
                }
                catch (Exception ex)
                {
                    Log.Error("Floor.Create", ex);
                    reason = ex.Message;
                    ok = false;
                    break;
                }
            }
            if (ok && created.Count > 0) { res.AddRange(created); return res; }
            foreach (var f in created) SafeDelete(f.Id);
        }
        return res;
    }

    /// <summary>Tolerância de planicidade dos pisos planos sobre greide/nó (m) – no Toposolid, 3 cm.</summary>
    public const double GradeFloorTolerance = 0.02;

    /// <summary>
    /// Partes dos pisos: peças vizinhas do mesmo material unidas num piso só; sobre greide/nó/terreno, divididas em partes
    /// PLANAS (cada uma um piso plano inclinado, sem os vincos da edição de forma) – juntas retas perpendiculares ao eixo e na
    /// crista do abaulamento, partes coplanares fundidas de volta. Plano nulo = piso na cota (sem superfície) ou empenado.
    /// </summary>
    private List<(Polygon2 Shape, Plane3? Plane)> FloorParts(MarkingDefinition def, IEnumerable<Polygon2> shapes, ISurface? terrain, double baseZ,
        bool paint = false)
    {
        var merged = ElementPlan.FloorUnion(shapes).Where(p => p.Area > 0.01).ToList();
        var parts = new List<(Polygon2 Shape, Plane3? Plane)>();
        foreach (var m in merged)
        {
            if (terrain == null) { parts.Add((m, null)); continue; }
            // Via e nó com greide: o retalhamento comum (as mesmas juntas e os mesmos planos para todos os pisos).
            if (terrain is GradeSampler or NodeSampler && TiledParts(def, m, baseZ, paint) is { } tiled)
            {
                parts.AddRange(tiled);
                continue;
            }
            try
            {
                var zf = SurfaceZ(terrain, UnitConv.Ft(baseZ + def.Output.ElevationOffset));
                var tol = terrain is SurfaceSampler ? 0.03 : GradeFloorTolerance;
                var split = terrain is NodeSampler { Zones.Count: > 0 } ns
                    ? FloorPlanes.SplitMember(m, zf, tol, ns.Zones, ns.Node.Major?.Surface, 25, 1.0)
                    : FloorPlanes.Split(m, zf, tol, (terrain as GradeSampler)?.Surface ?? (terrain as NodeSampler)?.Node.Major?.Surface,
                        terrain is SurfaceSampler ? 60 : 25, ringStep: terrain is SurfaceSampler ? 3.0 : 1.0);
                // Partes empenadas (superfície torcida, só sem eixo de referência) seguem com edição de forma.
                parts.AddRange(split.Select(x => (x.Part, x.Warped ? null : (Plane3?)x.Plane)));
            }
            catch (Exception ex)
            {
                Log.Error("FloorPlanes", ex);
                parts.Add((m, null));
            }
        }
        return parts;
    }

    /// <summary>
    /// Partes de um piso pelo retalhamento comum: o do nó para as marcas do nó, o da via para as marcas dela (nulo = sem via
    /// com greide). A marca com base própria diferente da base da via leva os planos junto (a superfície dela é o greide a partir
    /// da própria base).
    /// </summary>
    private List<(Polygon2 Shape, Plane3? Plane)>? TiledParts(MarkingDefinition def, Polygon2 shape, double baseZ, bool paint)
    {
        try
        {
            var tiling = Tiling();
            if (NodeOwner(def) is { } owner)
                return tiling.ForNode(owner.Id, shape, paint)?.Select(x => (x.Part, (Plane3?)x.Plane)).ToList();
            if (RoadOf(def) is not { Output.Grade: not null } road) return null;
            var parts = tiling.ForRoad(road.Id, shape, paint);
            if (parts == null) return null;
            var dz = baseZ - (PathResolver.Resolve(_doc, road.Path)?.Z ?? baseZ);
            return parts.Select(x => (x.Part, (Plane3?)(x.Plane with { A = x.Plane.A + dz }))).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Retalhamento comum dos pisos", ex);
            return null;
        }
    }

    private readonly Dictionary<string, TiledSampler?> _tiles = new();

    /// <summary>
    /// Pintura sobre a pista com greide/nó: a superfície de apoio passa a ser a dos PISOS PLANOS da pista (mesmos planos,
    /// recortados nas mesmas juntas) – a pintura fica 4 mm acima do piso em todo ponto, sem afundar nem flutuar.
    /// </summary>
    private ISurface TiledFor(MarkingDefinition def, ISurface baseSampler)
    {
        MarkingDefinition? src = NodeOwner(def) ?? RoadOf(def);
        if (src == null || ReferenceEquals(src, def) || src.Id == def.Id) return baseSampler;
        if (_tiles.TryGetValue(src.Id, out var cached)) return (ISurface?)cached ?? baseSampler;
        TiledSampler? ts = null;
        try
        {
            var tiles = new List<(Polygon2, Plane3, double)>();
            void AddTiles(MarkingDefinition owner)
            {
                var g = BuildGeometry(owner, out var b);
                var terrain = GradeFor(owner, b);
                if (terrain == null) return;
                foreach (var grp in g.Pieces.Where(p => FloorEligible(owner, p) && p.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto or MarkingColor.PavimentoTerra)
                             .GroupBy(p => (p.Color, E: Math.Round(p.Elevation, 3), T: Math.Round(p.Thickness, 3), p.Layer)))
                    foreach (var (shape, plane) in FloorParts(owner, grp.Select(p => p.Shape), terrain, b))
                        if (plane is { } pl) tiles.Add((shape, pl, grp.Key.E + grp.Key.T));
            }
            AddTiles(src);
            // Pintura do nó que passa da borda dele (faixas, LCO): fica sobre os pisos planos da via em que está.
            if (src is IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition && NodeFor(src) is { } node)
                foreach (var leg in node.Legs)
                    if (leg.Id != null && Definitions.TryGetValue(leg.Id, out var rd) && rd is RoadPavementDefinition) AddTiles(rd);
            if (tiles.Count > 0) ts = new TiledSampler(baseSampler, tiles);
        }
        catch (Exception ex) { Log.Error("Pisos da pista para a pintura", ex); }
        _tiles[src.Id] = ts;
        return (ISurface?)ts ?? baseSampler;
    }

    /// <summary>Cota (m) da superfície num ponto (m), com memória – a divisão em planos amostra os mesmos pontos várias vezes.</summary>
    private static Func<Vec2, double> SurfaceZ(ISurface terrain, double hintFt)
    {
        var memo = new Dictionary<(long, long), double>();
        var last = UnitConv.M(hintFt);
        return v =>
        {
            var k = ((long)Math.Round(v.X * 50), (long)Math.Round(v.Y * 50));
            if (memo.TryGetValue(k, out var z)) return z;
            if (terrain.TrySample(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), UnitConv.Ft(last), out var zf, out _)) last = UnitConv.M(zf);
            return memo[k] = last;
        };
    }

    private sealed record ShapeEdit(Floor Floor, Plane3? Plane, Polygon2? Shape, ISurface? Terrain, double GroundFt);

    private readonly List<ShapeEdit> _shapeEdits = new();
    private Dictionary<string, Floor>? _reuse;

    private static string PlaneKey(Plane3? p) =>
        p is not { } pl || pl.IsLevel ? "plano" : FormattableString.Invariant($"{pl.A:0.00000}|{pl.B:0.000000}|{pl.C:0.000000}");

    /// <summary>Piso anterior da marca com a mesma chave (mantido em vez de apagado e recriado).</summary>
    private Floor? Reuse(string key)
    {
        if (_reuse == null || !_reuse.Remove(key, out var f) || !f.IsValidObject) return null;
        return f;
    }

    /// <summary>
    /// Edição de forma dos pisos da marca em lote: liga os editores de todos, regenera UMA vez, acrescenta os pontos da
    /// malha do terreno (mais uma regeneração, só se houver) e ajusta as cotas. Antes era uma regeneração por piso.
    /// Inclinação (plano): só os vértices do contorno, cada um no plano – o topo continua uma face plana única.
    /// </summary>
    private void FlushShapeEdits()
    {
        if (_shapeEdits.Count == 0) return;
        var edits = _shapeEdits.Where(e => e.Floor.IsValidObject).ToList();
        _shapeEdits.Clear();
        var ready = new List<(ShapeEdit E, SlabShapeEditor Ed)>();
        foreach (var e in edits)
        {
            try
            {
                var ed = e.Floor.GetSlabShapeEditor();
                ed.Enable();
                ready.Add((e, ed));
            }
            catch (Exception ex) { Log.Error("Edição de forma do piso", ex); }
        }
        if (ready.Count == 0) return;
        _doc.Regenerate();
        var added = false;
        foreach (var (e, ed) in ready)
        {
            if (e.Terrain == null || e.Shape == null) continue;
            try { added |= AddDrapePoints(e.Floor, ed, e.Shape, e.Terrain, e.GroundFt); }
            catch (Exception ex) { Log.Error("DrapeFloor (pontos)", ex); }
        }
        if (added) _doc.Regenerate();
        foreach (var (e, ed) in ready)
        {
            try
            {
                foreach (SlabShapeVertex v in ed.SlabShapeVertices)
                {
                    var p = v.Position;
                    double off;
                    if (e.Plane is { } plane) off = UnitConv.Ft(plane.Z(new Vec2(UnitConv.M(p.X), UnitConv.M(p.Y)))) - e.GroundFt;
                    else if (e.Terrain != null && e.Terrain.TrySample(p.X, p.Y, e.GroundFt, out var z, out _)) off = z - e.GroundFt;
                    else continue;
                    if (Math.Abs(off) > 1e-5) ed.ModifySubElement(v, off);
                }
            }
            catch (Exception ex) { Log.Error("Edição de forma do piso (cotas)", ex); }
        }
    }

    private static Polygon2 Densified(Polygon2 p, double maxLen)
    {
        List<Vec2> D(IReadOnlyList<Vec2> ring) => CurveTools.Densify(ring.Append(ring[0]).ToList(), maxLen).SkipLast(1).ToList();
        return new Polygon2(D(p.Outer), p.Holes.Select(h => (IEnumerable<Vec2>)D(h)));
    }

    /// <summary>
    /// Pontos internos da edição de forma para o piso acompanhar o terreno: no greide, linhas paralelas ao eixo (inclui a
    /// crista do abaulamento); no terreno, malha regular (4 m). O piso continua editável com as ferramentas nativas.
    /// </summary>
    private bool AddDrapePoints(Floor f, SlabShapeEditor ed, Polygon2 shape, ISurface terrain, double groundFt)
    {
        var top = f.get_BoundingBox(null)?.Max.Z ?? groundFt;
        var inner = new List<XYZ>();
        if (terrain is GradeSampler gs)
            inner.AddRange(GradeFloors.Supports(shape, gs.Surface, gs.Grid).Select(v => new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), top)));
        else
        {
            var (mn, mx) = shape.Bounds;
            // Nó (concordância torcida): malha fina e regular de 1,5 m; terreno: 3 m.
            var step = Math.Max(terrain is NodeSampler ? 1.5 : 3.0, Math.Sqrt(Math.Max(1, shape.Area) / 1200));
            var edge = PolygonOps.Offset(new[] { shape }, -0.4);
            for (var x = mn.X + step / 2; x < mx.X; x += step)
                for (var y = mn.Y + step / 2; y < mx.Y; y += step)
                {
                    var v = new Vec2(x, y);
                    if (inner.Count < 1500 && edge.Any(e => e.Contains(v))) inner.Add(new XYZ(UnitConv.Ft(x), UnitConv.Ft(y), top));
                }
        }
        if (inner.Count == 0) return false;
        try { ed.AddPoints(inner); }
        catch (Exception ex)
        {
            Log.Error("SlabShape.AddPoints", ex);
            foreach (var q in inner) try { ed.AddPoint(q); } catch { /* ponto repetido ou na borda */ }
        }
        return true;
    }

    private bool FloorEdited(Floor f)
    {
        try
        {
            if (_doc.GetElement(f.SketchId) is not Sketch sk) return false;
            var loops = new List<CurveLoop>();
            foreach (CurveArray arr in sk.Profile)
            {
                var loop = new CurveLoop();
                foreach (Curve c in arr) loop.Append(c);
                loops.Add(loop);
            }
            return FloorSignature.Differs(f, loops);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Área real do piso (m²) – inclui edições do usuário.</summary>
    public static double FloorArea(Floor f, double fallback = 0)
    {
        var a = f.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
        return a > 1e-9 ? UnitConv.M2(a) : fallback;
    }

    // ------------------------------------------------------------------ 2D

    private List<FilledRegion> CreateRegions(View view, MarkingColor color, List<Polygon2> shapes, double zFt, List<string> warnings,
        string? rgb = null, Hachura hatch = Hachura.Nenhuma)
    {
        var res = new List<FilledRegion>();
        var typeId = Styles.FilledRegionType(color, rgb, hatch);
        ElementId? lineStyle = PluginContext.Settings.VisibleBoundary2D ? null : Styles.InvisibleLineStyle();

        // Em planta as peças empilhadas/sobrepostas da mesma cor são unidas (regiões não podem se sobrepor).
        shapes = PolygonOps.Union(shapes);
        // Tenta uma única região com todas as peças; se falhar, uma região por peça.
        var all = shapes.SelectMany(s => ToCurveLoops(s, zFt)).ToList();
        if (all.Count == 0) return res;
        try
        {
            res.Add(FilledRegion.Create(_doc, typeId, view.Id, all));
        }
        catch
        {
            int failures = 0;
            foreach (var s in shapes)
            {
                try { res.Add(FilledRegion.Create(_doc, typeId, view.Id, ToCurveLoops(s, zFt))); }
                catch { failures++; }
            }
            if (failures > 0) warnings.Add($"{failures} peça(s) não puderam ser desenhadas como região preenchida.");
        }
        if (lineStyle != null)
            foreach (var fr in res)
                try { fr.SetLineStyleId(lineStyle); } catch { /* estilo opcional */ }
        return res;
    }

    /// <summary>Textos (TextNote) e linhas de chamada (linhas de detalhe) do detalhamento.</summary>
    private List<Element> CreateAnnotations(View view, List<Annotation2D> annotations, double zFt, List<string> warnings)
    {
        var res = new List<Element>();
        var tol = _doc.Application.ShortCurveTolerance * 1.05;
        int failures = 0;
        foreach (var a in annotations)
        {
            try
            {
                switch (a)
                {
                    case AnnotationLine l:
                    {
                        var style = Styles.LeaderLineStyle(l.Color);
                        for (int i = 0; i + 1 < l.Points.Count; i++)
                        {
                            var p0 = new XYZ(UnitConv.Ft(l.Points[i].X), UnitConv.Ft(l.Points[i].Y), zFt);
                            var p1 = new XYZ(UnitConv.Ft(l.Points[i + 1].X), UnitConv.Ft(l.Points[i + 1].Y), zFt);
                            if (p0.DistanceTo(p1) < tol) continue;
                            var dc = _doc.Create.NewDetailCurve(view, Line.CreateBound(p0, p1));
                            dc.LineStyle = style;
                            res.Add(dc);
                        }
                        break;
                    }
                    case AnnotationText t when !string.IsNullOrWhiteSpace(t.Text):
                    {
                        var opt = new TextNoteOptions(Styles.TextType(t.PaperHeightMm))
                        {
                            HorizontalAlignment = t.Align switch
                            {
                                TextAlign.Left => HorizontalTextAlignment.Left,
                                TextAlign.Right => HorizontalTextAlignment.Right,
                                _ => HorizontalTextAlignment.Center,
                            },
                            VerticalAlignment = VerticalTextAlignment.Top,
                            Rotation = t.Rotation,
                        };
                        var pos = new XYZ(UnitConv.Ft(t.Position.X), UnitConv.Ft(t.Position.Y), zFt);
                        res.Add(TextNote.Create(_doc, view.Id, pos, t.Text.Replace("\n", "\r"), opt));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (failures++ == 0) Log.Error("CreateAnnotations", ex);
            }
        }
        if (failures > 0) warnings.Add($"{failures} texto(s)/linha(s) de detalhamento não puderam ser criados.");
        return res;
    }

    // ------------------------------------------------------------------ dados

    private void Tag(Element e, MarkingDefinition def, MarkingInfo info, MarkingColor color, string material, double areaM2,
        MarkingGeometry geo, bool primary)
    {
        MarkingStorage.Write(e, def, color);
        try
        {
            SharedParameters.Set(e, SharedParameters.Codigo, info.Code);
            SharedParameters.Set(e, SharedParameters.Descricao, info.Name);
            SharedParameters.Set(e, SharedParameters.Grupo, def is UrbanElementDefinition ue && PluginContext.Catalog.Movel(ue.Code) is { } mv
                ? UrbanCategories.Label(UrbanCategories.Of(mv.Forma)) : QuantityRow.GroupLabel(info.Group));
            SharedParameters.Set(e, SharedParameters.Categoria, def is LoteamentoDetalheDefinition ? Core.Parcelamento.Masterplan.Loteamento
                : def is IAnnotationDefinition ? "Detalhamento" : QuantityRow.CategoryLabel(QuantityRow.Categorize(def, info.Group)));
            SharedParameters.Set(e, SharedParameters.Hierarquia, def is IAnnotationDefinition ? "" : Core.Definitions.Hierarquia.Label(def.Hierarchy));
            // Memorial (placas, dispositivos, mobiliário...): contado por modelo, sem desdobrar por cor nem material
            // – igual à janela de quantitativos; as peças coloridas de uma placa ficam numa só linha da tabela.
            var memorial = def is not IAnnotationDefinition && QuantityRow.IsMemorialCategory(QuantityRow.Categorize(def, info.Group));
            SharedParameters.Set(e, SharedParameters.Cor, memorial ? "" : StyleService.ColorName(color));
            SharedParameters.Set(e, SharedParameters.Material, memorial ? "" : material);
            SharedParameters.Set(e, SharedParameters.Area, UnitConv.Ft2(areaM2));
            SharedParameters.Set(e, SharedParameters.Extensao, primary ? UnitConv.Ft(geo.PaintedLength) : 0.0);
            SharedParameters.Set(e, SharedParameters.Quantidade, primary ? geo.UnitCount : 0);
            SharedParameters.Set(e, SharedParameters.Referencia, info.Reference);
            // Coluna "Quantidade / Un." da tabela do Revit (igual à janela de quantitativos).
            SharedParameters.Set(e, SharedParameters.Unidade, info.Unit);
            SharedParameters.Set(e, SharedParameters.QtdMedicao, info.Unit switch
            {
                "m²" => areaM2,
                "m" => primary ? geo.PaintedLength : 0.0,
                _ => primary ? Math.Max(geo.UnitCount, 1) : 0.0,
            });
            SharedParameters.Set(e, SharedParameters.Id, def.Id);
        }
        catch (Exception ex)
        {
            Log.Error("Tag", ex);
        }
    }
}
