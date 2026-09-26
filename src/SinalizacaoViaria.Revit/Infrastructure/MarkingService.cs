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
        _geometryCache.Clear();
    }

    /// <summary>Geometria sem detalhes (null para detalhes ou em caso de erro) – prévias de quadros.</summary>
    public MarkingGeometry? BuildGeometryOrNull(MarkingDefinition d) => OtherGeometry(d);

    /// <summary>Geometria das demais marcas (cotas de seção, quadros de quantitativos) – sem detalhes, para não haver recursão.</summary>
    private MarkingGeometry? OtherGeometry(MarkingDefinition d)
    {
        if (d is IAnnotationDefinition) return null;
        var key = d.Id + "|" + d.ToJson().GetHashCode();
        if (_geometryCache.TryGetValue(key, out var g)) return g;
        try { g = BuildGeometry(d, out _); }
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
    public MarkingGeometry BuildGeometry(MarkingDefinition def, out double baseZ, List<string>? warnings = null, View? view = null)
    {
        var ctx = PluginContext.BuildContext(def.Output.Drape && def.Output.Mode == OutputMode.Modelo3D,
            view?.Scale ?? 100, id => Definitions.GetValueOrDefault(id), () => Definitions.Values.ToList(), OtherGeometry,
            d => PathResolver.Resolve(_doc, d.Path)?.Main);
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
        if (def is RampDefinition or TrafficCalmingDefinition or CulDeSacDefinition or CurbExtensionDefinition) return MarkingBuilder.Build(def, path.Main, ctx);

        if (def is TactileRouteDefinition route)
        {
            // Todas as linhas juntas: as junções entre trechos recebem alerta.
            var g = TactileGenerator.Route(route, path.Chains, route.Elevation);
            return route.Exclusions.Count == 0 ? g : MarkingBuilder.ApplyExclusions(g, route.Exclusions);
        }

        var geo = new MarkingGeometry();
        foreach (var chain in path.Chains)
        {
            if (def is HatchMarkingDefinition { IsStrip: false } && chain.Points.Count < 3) continue;
            geo.Merge(MarkingBuilder.Build(def, chain, ctx));
        }
        return geo;
    }

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
        var existing = MarkingStorage.ById(_doc, def.Id);
        PruneExclusions(def);

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

        MarkingGeometry geo;
        double baseZ;
        try
        {
            geo = BuildGeometry(def, out baseZ, result.Warnings, view);
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

        // Pavimento, calçada, meio-fio, sarjeta e grama como Piso do Revit (editáveis com as ferramentas nativas).
        if (def.Output.Mode == OutputMode.Modelo3D && settings.PhysicalAsFloors && !def.Output.Drape)
        {
            var floorPieces = geo.Pieces.Where(p => FloorEligible(def, p)).ToList();
            if (floorPieces.Count > 0)
            {
                // Tipo de piso escolhido pelo usuário em pisos anteriores (por cor) é mantido.
                var userTypes = existing.Where(r => r.Element is Floor).GroupBy(r => r.Color)
                    .ToDictionary(g => g.Key, g => g.First().Element.GetTypeId());
                foreach (var old in existing.Where(r => r.Element is Floor)) SafeDelete(old.Element.Id);
                existing.RemoveAll(r => r.Element is Floor);
                var failed = new List<MarkingPiece>();
                foreach (var piece in floorPieces)
                {
                    var f = CreateFloor(piece, baseZ + def.Output.ElevationOffset, userTypes.GetValueOrDefault(piece.Color));
                    if (f == null) { failed.Add(piece); continue; }
                    try { f.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"SV {info.Code}"); } catch { /* opcional */ }
                    Tag(f, def, info, piece.Color, StyleService.ColorName(piece.Color), piece.Shape.Area, geo, primary);
                    keep.Add(f.Id);
                    primary = false;
                }
                solidPieces = geo.Pieces.Where(p => !floorPieces.Contains(p) || failed.Contains(p)).ToList();
            }
        }
        var groups = solidPieces.GroupBy(p => p.Color)
            .OrderByDescending(g => g.Sum(p => p.Shape.Area)).ToList();

        if (def.Output.Mode == OutputMode.Modelo3D)
        {
            SurfaceSampler? sampler = null;
            if (def.Output.Drape)
            {
                sampler = new SurfaceSampler(_doc, def.Output.SurfaceIds, _interactive);
                if (!sampler.IsAvailable) result.Warnings.Add("Nenhuma vista 3D disponível para projetar sobre a superfície – marca gerada plana.");
            }

            foreach (var g in groups)
            {
                // Linhas e símbolos sobre pinturas de fundo (ciclofaixa, faixa de caminhada) ficam logo acima delas –
                // sem faces coincidentes (que no Revit aparecem como emendas/quadrados).
                var lift = IsOverlay(def) && MarkingColors.IsPaint(g.Key) ? thickness : 0;
                var solids = BuildSolids(g, baseZ + def.Output.ElevationOffset + lift, thickness, sampler, Styles.Material(g.Key), result.Warnings);
                if (solids.Count == 0) continue;
                var ds = existing.FirstOrDefault(r => r.Color == g.Key && r.Element is DirectShape && !keep.Contains(r.Element.Id))?.Element as DirectShape;
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
                    result.Warnings.Add($"{info.Code}: o Revit recusou a geometria ({ex.Message}).");
                    continue;
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
            {
                var regions = CreateRegions(view!, g.Key, g.Select(p => p.Shape).ToList(), z, result.Warnings);
                foreach (var fr in regions)
                {
                    Tag(fr, def, info, g.Key, materialName, fr == regions[0] ? g.Sum(p => p.Shape.Area) : 0, geo, primary);
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

        foreach (var old in existing.Where(r => !keep.Contains(r.Element.Id)))
            SafeDelete(old.Element.Id);

        result.Elements.AddRange(keep);
        return result;
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
        SurfaceSampler? sampler, ElementId materialId, List<string> warnings)
    {
        var res = new List<GeometryObject>();
        var options = new SolidOptions(materialId, ElementId.InvalidElementId);
        int failures = 0;
        var zBaseFt = UnitConv.Ft(zMeters);

        foreach (var piece in pieces)
        {
            try
            {
                var t = UnitConv.Ft(piece.Thickness > 0 ? piece.Thickness : thickness);
                var z = zBaseFt;
                var lift = UnitConv.Ft(piece.Elevation);
                XYZ? normal = null;
                var c = piece.Shape.Centroid;
                if (sampler is { IsAvailable: true } && sampler.TrySample(UnitConv.Ft(c.X), UnitConv.Ft(c.Y), zBaseFt, out var sz, out var n))
                {
                    z = sz + UnitConv.Ft(0.001);
                    normal = n;
                }
                if (piece.Solid is { } poly)
                {
                    res.AddRange(PolyhedronGeometry(poly, z + lift, materialId));
                    continue;
                }
                if (piece.Profile is { } prof)
                {
                    res.Add(ProfileSolidGeometry(prof, z + lift, options));
                    continue;
                }
                // Peças empilhadas (barreiras, balizadores) começam acima da base.
                var loops = ToCurveLoops(piece.Shape, z + lift);
                if (loops.Count == 0) { failures++; continue; }
                var solid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, t, options);
                if (normal != null && normal.Z < 0.9999)
                {
                    var axis = XYZ.BasisZ.CrossProduct(normal);
                    if (axis.GetLength() > 1e-9)
                    {
                        var angle = XYZ.BasisZ.AngleTo(normal);
                        var tr = Transform.CreateRotationAtPoint(axis.Normalize(), angle, new XYZ(UnitConv.Ft(c.X), UnitConv.Ft(c.Y), z));
                        solid = SolidUtils.CreateTransformed(solid, tr);
                    }
                }
                res.Add(solid);
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

    private CurveLoop? ToCurveLoop3D(List<XYZ> raw)
    {
        var tol = _doc.Application.ShortCurveTolerance * 1.5;
        var pts = new List<XYZ>();
        foreach (var p in raw)
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > tol) pts.Add(p);
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= tol) pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 3) return null;
        var loop = new CurveLoop();
        for (int i = 0; i < pts.Count; i++) loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
        return loop;
    }

    private IList<CurveLoop> ToCurveLoops(Polygon2 poly, double zFt)
    {
        var loops = new List<CurveLoop>();
        var outer = ToCurveLoop(poly.Outer, zFt);
        if (outer == null) return loops;
        loops.Add(outer);
        foreach (var h in poly.Holes)
        {
            var hl = ToCurveLoop(h, zFt);
            if (hl != null) loops.Add(hl);
        }
        return loops;
    }

    private CurveLoop? ToCurveLoop(IReadOnlyList<Vec2> ring, double zFt)
    {
        var tol = _doc.Application.ShortCurveTolerance * 1.5;
        var pts = new List<XYZ>();
        foreach (var v in ring)
        {
            var p = new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), zFt);
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > tol) pts.Add(p);
        }
        while (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) <= tol) pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 3) return null;
        var loop = new CurveLoop();
        for (int i = 0; i < pts.Count; i++)
            loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
        return loop;
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

    private static bool FloorEligible(MarkingDefinition def, MarkingPiece p) =>
        p.Solid == null && p.Profile == null && p.Thickness >= 0.005 && p.Shape.Area > 0.01
        && p.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto or MarkingColor.Concreto or MarkingColor.Grama
        && def is RoadPavementDefinition or LinearMarkingDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition
            or SidewalkAreaDefinition or CurbExtensionDefinition or PlanterDefinition;

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
        if (ft == null)
        {
            var baseType = types.FirstOrDefault(t => !t.IsFoundationSlab) ?? types.FirstOrDefault();
            if (baseType == null) return ElementId.InvalidElementId;
            ft = (FloorType)baseType.Duplicate(name);
            var cs = CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, UnitConv.Ft(thicknessM), Styles.Material(color));
            ft.SetCompoundStructure(cs);
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

    /// <summary>Cria um piso para a peça (topo na cota da peça). Nulo se o Revit recusar o contorno.</summary>
    private Floor? CreateFloor(MarkingPiece piece, double baseZm, ElementId? userType)
    {
        try
        {
            var topFt = UnitConv.Ft(baseZm + piece.Elevation + piece.Thickness);
            var level = LevelFor(topFt);
            if (level == null) return null;
            var shape = piece.Shape.Simplified(0.005) ?? piece.Shape;
            var loops = ToCurveLoops(shape, level.ProjectElevation);
            if (loops.Count == 0) return null;
            var typeId = userType != null && userType != ElementId.InvalidElementId && _doc.GetElement(userType) is FloorType
                ? userType : FloorType(piece.Color, piece.Thickness);
            if (typeId == ElementId.InvalidElementId) return null;
            var f = Floor.Create(_doc, loops, typeId, level.Id, false, null, 0.0);
            f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(topFt - level.ProjectElevation);
            return f;
        }
        catch (Exception ex)
        {
            Log.Error("Floor.Create", ex);
            return null;
        }
    }

    // ------------------------------------------------------------------ 2D

    private List<FilledRegion> CreateRegions(View view, MarkingColor color, List<Polygon2> shapes, double zFt, List<string> warnings)
    {
        var res = new List<FilledRegion>();
        var typeId = Styles.FilledRegionType(color);
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
            SharedParameters.Set(e, SharedParameters.Grupo, QuantityRow.GroupLabel(info.Group));
            SharedParameters.Set(e, SharedParameters.Categoria, def is IAnnotationDefinition ? "Detalhamento" : QuantityRow.CategoryLabel(QuantityRow.Categorize(def, info.Group)));
            SharedParameters.Set(e, SharedParameters.Hierarquia, def is IAnnotationDefinition ? "" : Core.Definitions.Hierarquia.Label(def.Hierarchy));
            SharedParameters.Set(e, SharedParameters.Cor, StyleService.ColorName(color));
            SharedParameters.Set(e, SharedParameters.Material, material);
            SharedParameters.Set(e, SharedParameters.Area, UnitConv.Ft2(areaM2));
            SharedParameters.Set(e, SharedParameters.Extensao, primary ? UnitConv.Ft(geo.PaintedLength) : 0.0);
            SharedParameters.Set(e, SharedParameters.Quantidade, primary ? geo.UnitCount : 0);
            SharedParameters.Set(e, SharedParameters.Referencia, info.Reference);
            SharedParameters.Set(e, SharedParameters.Id, def.Id);
        }
        catch (Exception ex)
        {
            Log.Error("Tag", ex);
        }
    }
}
