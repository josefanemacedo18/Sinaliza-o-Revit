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
    private readonly bool _interactive;
    private StyleService? _styles;

    /// <param name="interactive">Falso quando chamado pelo atualizador automático (não cria vistas nem pergunta nada).</param>
    public MarkingService(Document doc, View? activeView = null, bool interactive = true)
    {
        _doc = doc;
        _activeView = activeView;
        _interactive = interactive;
    }

    private StyleService Styles => _styles ??= new StyleService(_doc);
    private Dictionary<string, MarkingDefinition>? _definitions;

    /// <summary>Definições do documento (cache por geração – detalhes e legendas consultam as demais marcas).</summary>
    private Dictionary<string, MarkingDefinition> Definitions =>
        _definitions ??= MarkingStorage.Definitions(_doc).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());

    private readonly Dictionary<string, MarkingGeometry?> _geometryCache = new();

    /// <summary>Invalida o cache de definições (após gravar outras marcas na mesma transação).</summary>
    public void Invalidate()
    {
        _definitions = null;
        _present = null;
        _geometryCache.Clear();
    }

    private Dictionary<string, List<StoredMarking>>? _present;

    /// <summary>Elementos existentes de cada marca (por id).</summary>
    private Dictionary<string, List<StoredMarking>> Present =>
        _present ??= MarkingStorage.All(_doc).GroupBy(r => r.MarkingId).ToDictionary(g => g.Key, g => g.ToList());

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
        id => Definitions.GetValueOrDefault(id), () => Definitions.Values.ToList(), OtherGeometry, d => PathResolver.Resolve(_doc, d.Path)?.Main);

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

    /// <summary>Ordena para gerar primeiro as marcas e depois os detalhes/anotações que dependem delas.</summary>
    public static List<MarkingDefinition> DependencyOrder(IEnumerable<MarkingDefinition> defs) =>
        defs.OrderBy(d => d is IProjectWideAnnotation ? 2 : d is IAnnotationDefinition ? 1 : 0).ToList();

    /// <summary>Regenera os detalhes/anotações que apontam para as marcas indicadas (e os quadros de legenda).</summary>
    public List<RenderResult> RenderDependents(IEnumerable<string> markingIds, bool includeLegends)
    {
        var ids = markingIds.ToHashSet();
        _definitions = null;
        var deps = Definitions.Values.Where(d => d is IAnnotationDefinition a && !ids.Contains(d.Id)
            && (a.TargetId != null ? ids.Contains(a.TargetId) : includeLegends && d is IProjectWideAnnotation)).ToList();
        var res = new List<RenderResult>();
        foreach (var d in DependencyOrder(deps))
        {
            try { res.Add(Render(d)); }
            catch (Exception ex) { Log.Error($"RenderDependents {d.DisplayCode}", ex); }
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
        var geo = BuildGeometryCore(def, out baseZ, warnings, view);
        // Nó no relevo: rampas, ilhas, meios-fios e dispositivos acompanham a superfície do nó vértice a vértice (antes só
        // subiam pela cota do centro e ficavam tortos/soltos em interseções inclinadas).
        if (def.Output.Mode == OutputMode.Modelo3D && NodeFor(def) is { } node)
        {
            var b = baseZ;
            GradeLift.Apply(geo, v => node.Z(v) - b);
        }
        return geo;
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
            view?.Scale ?? 100, id => Definitions.GetValueOrDefault(id), () => Definitions.Values.ToList(), OtherGeometry,
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
            // Na via com greide (fora de nós), rampas e moderadores acompanham o greide vértice a vértice.
            if (Graded(def) && path.Main != null && NodeFor(def) == null) GradeLift.Apply(g1, SurfaceFor(def, path.Main));
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
            return MarkingBuilder.BuildHatch(hatch, path.Chains.Where(c => c.Points.Count >= 3).ToList(), ctx);

        var graded = Graded(def) && path.Main != null;
        var geo = new MarkingGeometry();
        foreach (var chain in path.Chains)
        {
            // Com greide, o eixo é refinado (2 m): barreiras, dispositivos e sólidos varridos acompanham as curvas verticais.
            var c = graded ? new Polyline2(CurveTools.Densify(chain.Points, 2.0), chain.Closed) : chain;
            geo.Merge(MarkingBuilder.Build(def, c, ctx));
        }
        if (graded) GradeLift.Apply(geo, SurfaceFor(def, path.Main!));
        return geo;
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
        if (def.Output.Mode == OutputMode.Modelo3D && NodeFor(def) is { } node) return new NodeSampler(node);
        if (!Graded(def)) return null;
        var axis = PathResolver.Resolve(_doc, def.Path)?.Main;
        return axis == null ? null : new GradeSampler(SurfaceFor(def, axis), baseZ);
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
            foreach (var blend in BlendsFor(pav, surf, PathResolver.Resolve(_doc, pav.Path)?.Z ?? 0)) surf.Blends.Add(blend);
        return _surfaces[key] = surf;
    }

    /// <summary>Pavimento da via a que a marca pertence (o próprio, ou o do mesmo grupo).</summary>
    private RoadPavementDefinition? RoadOf(MarkingDefinition def)
    {
        if (def is RoadPavementDefinition p) return p;
        if (def.GroupId == null) return null;
        return Definitions.Values.OfType<RoadPavementDefinition>().FirstOrDefault(x => x.GroupId == def.GroupId);
    }

    /// <summary>Concordâncias da via com as interseções e rotatórias em que ela não é a principal.</summary>
    private IEnumerable<NodeBlend> BlendsFor(RoadPavementDefinition pav, GradeSurface surf, double baseZ)
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
            // Concordância de 20 m (mais longa quando a diferença de rampa é grande, até 40 m).
            var own = surf.Grade.GradeAt(Math.Clamp(c + reach + 10, 0, surf.Axis.Length));
            var len = Math.Clamp(20 + 200 * Math.Abs(own), 20, 40);
            yield return new NodeBlend(node, c, reach, len, baseZ);
        }
    }

    /// <summary>
    /// Superfície do nó (interseção, rotatória, cul-de-sac – e as marcas filhas deles) costurada ao greide das vias ligadas;
    /// nulo quando nenhuma via ligada tem greide (o nó fica plano, como antes).
    /// </summary>
    /// <summary>Nó (interseção, rotatória, cul-de-sac) dono da marca – ele mesmo ou o pai das marcas filhas.</summary>
    private MarkingDefinition? NodeOwner(MarkingDefinition def) =>
        def is IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition ? def
            : Definitions.Values.FirstOrDefault(o => o is IntersectionDefinition i && i.ChildIds.Contains(def.Id) || o is RoundaboutDefinition r && r.ChildIds.Contains(def.Id));

    public NodeSurface? NodeFor(MarkingDefinition def)
    {
        var defs = Definitions;
        var owner = NodeOwner(def);
        if (owner == null) return null;
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

    /// <summary>Cria ou regenera todos os elementos da marca.</summary>
    public RenderResult Render(MarkingDefinition def)
    {
        var result = new RenderResult();
        _definitions = null;
        _present = null;
        var existing = MarkingStorage.ById(_doc, def.Id);
        PruneExclusions(def);
        // Traçado das linhas de referência guardado na marca: apagar a linha depois não faz a marca perder o caminho.
        try { PathResolver.RefreshCache(_doc, def.Path); } catch (Exception ex) { Log.Error("Cache do caminho", ex); }

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
                // Tipo de piso escolhido pelo usuário em pisos anteriores (por cor) é mantido.
                var userTypes = oldFloors.GroupBy(r => r.Color).ToDictionary(g => g.Key, g => g.First().Element.GetTypeId());
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
                foreach (var grp in floorPieces.Where(p => !locked.Contains(p.Color))
                             .GroupBy(ElementPlan.FloorKey))
                {
                    var parts = FloorParts(def, grp.Select(p => p.Shape), terrain, baseZ);
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
                                baseZ + def.Output.ElevationOffset, userTypes.GetValueOrDefault(grp.Key.Color), ref reason);
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
                        var floors = CreateFloors(piece, baseZ + def.Output.ElevationOffset, userTypes.GetValueOrDefault(grp.Key.Color), ref reason, terrain, plane);
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
                foreach (var f in pool) if (f.IsValidObject && !keep.Contains(f.Id)) SafeDelete(f.Id);
                _reuse = null;
                // Inclinação e edição de forma de todos os pisos da marca de uma vez (uma regeneração, não uma por piso).
                FlushShapeEdits();
                if (failed.Count > 0)
                    result.Warnings.Add($"{info.Code}: {failed.Count} parte(s) não puderam virar Piso do Revit e foram geradas como forma direta" +
                                        (reason != null ? $" ({reason})." : "."));
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
        var ids = MarkingStorage.All(_doc).Select(r => r.MarkingId).ToHashSet();
        def.Exclusions.RemoveAll(z => !string.IsNullOrEmpty(z.SourceId) && !ids.Contains(z.SourceId!));
    }

    public void Delete(string markingId)
    {
        foreach (var r in MarkingStorage.ById(_doc, markingId)) SafeDelete(r.Element.Id);
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
        int failures = 0;
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
                    else res.Add(ProfileSolidGeometry(piece.Profile!, z + lift, options));
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
                if (lu > 1e-9 && lv > 1e-9 && u.DotProduct(v) / (lu * lv) < -0.9995) { changed = true; continue; }   // espinho
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
    /// apoio da edição de forma sobre o terreno).
    /// </summary>
    private CurveLoop? ToCurveLoop(IReadOnlyList<Vec2> ring, double zFt, bool raw = false)
    {
        var tol = _doc.Application.ShortCurveTolerance * 1.5;
        var pts = new List<XYZ>();
        foreach (var v in ring)
        {
            var p = new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), zFt);
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > tol) pts.Add(p);
        }
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= tol) pts.RemoveAt(pts.Count - 1);
        pts = Sanitize(pts, tol);
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
        var loop = new CurveLoop();
        for (int i = 0; i < pts.Count; i++)
            loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
        return loop;
    }

    private CurveLoop? ToCurveLoopLines(IReadOnlyList<Vec2> ring, double zFt) => ToCurveLoop(ring, zFt, raw: true);

    /// <summary>Desvio máximo do contorno ajustado (m) – configurável em Configurações (0 = retas uma a uma, como antes).</summary>
    private static double BoundaryTolerance => Math.Max(0, PluginContext.Settings.BoundaryTolerance);

    private static CurveLoop? FittedLoop(List<Vec2> ring, double zFt, double shortFt)
    {
        var segs = BoundaryFit.Fit(ring, BoundaryTolerance, UnitConv.M(shortFt));
        if (segs.Count < 2 || segs.Count == 2 && !segs.Any(x => x.IsArc)) return null;
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

    private static readonly string[] OverlayCodes = { "CIC-LD", "CIC-LC", "FCA-BD", "MCC", "SIC", "CIC-SETA", "SPE", "LCA" };

    /// <summary>Marca pintada que costuma ficar sobre uma pintura de fundo.</summary>
    private static bool IsOverlay(MarkingDefinition d) => d switch
    {
        LinearMarkingDefinition l => OverlayCodes.Contains(l.Code),
        RepeatedMarkingDefinition r => r.SymbolCode != null && OverlayCodes.Contains(r.SymbolCode),
        SymbolMarkingDefinition s => OverlayCodes.Contains(s.Code),
        TextMarkingDefinition or RepeatedMarkingDefinition => true,
        _ => false,
    };

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
            try { loops.AddRange(ToCurveLoops(sh, level.ProjectElevation)); } catch { /* contorno degenerado */ }
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
            // Tipos criados por versões anteriores podem ter herdado a camada variável do tipo base.
            try
            {
                var cs0 = ft.GetCompoundStructure();
                if (cs0 != null && cs0.VariableLayerIndex >= 0) { cs0.VariableLayerIndex = -1; ft.SetCompoundStructure(cs0); }
            }
            catch (Exception ex) { Log.Error("FloorType (camada variável)", ex); }
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
    private List<(Polygon2 Shape, Plane3? Plane)> FloorParts(MarkingDefinition def, IEnumerable<Polygon2> shapes, ISurface? terrain, double baseZ)
    {
        var merged = PolygonOps.Union(shapes).Where(p => p.Area > 0.01).ToList();
        var parts = new List<(Polygon2 Shape, Plane3? Plane)>();
        foreach (var m in merged)
        {
            if (terrain == null) { parts.Add((m, null)); continue; }
            try
            {
                var zf = SurfaceZ(terrain, UnitConv.Ft(baseZ + def.Output.ElevationOffset));
                var tol = terrain is SurfaceSampler ? 0.03 : GradeFloorTolerance;
                var split = FloorPlanes.Split(m, zf, tol, (terrain as GradeSampler)?.Surface ?? (terrain as NodeSampler)?.Node.Major?.Surface,
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
            var g = BuildGeometry(src, out var b);
            var terrain = GradeFor(src, b);
            if (terrain != null)
            {
                var tiles = new List<(Polygon2, Plane3, double)>();
                foreach (var grp in g.Pieces.Where(p => FloorEligible(src, p) && p.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto or MarkingColor.PavimentoTerra)
                             .GroupBy(p => (p.Color, E: Math.Round(p.Elevation, 3), T: Math.Round(p.Thickness, 3), p.Layer)))
                    foreach (var (shape, plane) in FloorParts(src, grp.Select(p => p.Shape), terrain, b))
                        if (plane is { } pl) tiles.Add((shape, pl, grp.Key.E + grp.Key.T));
                if (tiles.Count > 0) ts = new TiledSampler(baseSampler, tiles);
            }
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
            SharedParameters.Set(e, SharedParameters.Categoria, def is IAnnotationDefinition ? "Detalhamento" : QuantityRow.CategoryLabel(QuantityRow.Categorize(def, info.Group)));
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
