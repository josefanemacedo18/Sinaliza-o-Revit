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
        _present = null;
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
        if (def.Output.Mode == OutputMode.Modelo3D && settings.PhysicalAsFloors)
        {
            // Acompanhando a superfície: o piso é criado na cota do terreno e deformado (edição de forma nativa do piso).
            SurfaceSampler? terrain = null;
            if (def.Output.Drape)
            {
                terrain = new SurfaceSampler(_doc, def.Output.SurfaceIds, _interactive, terrainOnly: true);
                if (!terrain.IsAvailable) terrain = null;
            }
            var floorPieces = geo.Pieces.Where(p => FloorEligible(def, p)).ToList();
            var oldFloors = existing.Where(r => r.Element is Floor).ToList();
            if (floorPieces.Count > 0 || oldFloors.Count > 0)
            {
                // Tipo de piso escolhido pelo usuário em pisos anteriores (por cor) é mantido.
                var userTypes = oldFloors.GroupBy(r => r.Color).ToDictionary(g => g.Key, g => g.First().Element.GetTypeId());
                // Pisos editados à mão (contorno alterado ou movidos) são preservados: a edição do usuário prevalece.
                var edited = oldFloors.Where(r => FloorEdited((Floor)r.Element)).ToList();
                var locked = edited.Select(r => r.Color).ToHashSet();
                foreach (var old in oldFloors.Where(r => !locked.Contains(r.Color))) SafeDelete(old.Element.Id);
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
                             .GroupBy(p => (p.Color, E: Math.Round(p.Elevation, 3), T: Math.Round(p.Thickness, 3))))
                {
                    // Peças vizinhas do mesmo material viram um piso só (calçadas, trechos recortados).
                    var merged = PolygonOps.Union(grp.Select(p => p.Shape)).Where(p => p.Area > 0.01).ToList();
                    foreach (var shape in merged)
                    {
                        var piece = new MarkingPiece(shape, grp.Key.Color) { Elevation = grp.Key.E, Thickness = grp.Key.T };
                        var floors = CreateFloors(piece, baseZ + def.Output.ElevationOffset, userTypes.GetValueOrDefault(grp.Key.Color), ref reason, terrain);
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
                if (failed.Count > 0)
                    result.Warnings.Add($"{info.Code}: {failed.Count} parte(s) não puderam virar Piso do Revit e foram geradas como forma direta" +
                                        (reason != null ? $" ({reason})." : "."));
                solidPieces = geo.Pieces.Where(p => !floorPieces.Contains(p)).Concat(failed).ToList();
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
                var solids = BuildSolids(g, baseZ + def.Output.ElevationOffset + lift, thickness, sampler, Styles.Material(g.Key), result.Warnings,
                    def.Output.ElevationOffset + lift);
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
        SurfaceSampler? sampler, ElementId materialId, List<string> warnings, double aboveSurfaceM = 0.001)
    {
        var res = new List<GeometryObject>();
        var options = new SolidOptions(materialId, ElementId.InvalidElementId);
        int failures = 0;
        var zBaseFt = UnitConv.Ft(zMeters);
        var above = UnitConv.Ft(Math.Max(0.001, aboveSurfaceM));
        var draped = sampler is { IsAvailable: true };

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
                    if (draped && sampler!.TrySample(UnitConv.Ft(c.X), UnitConv.Ft(c.Y), zBaseFt, out var sz, out _)) z = sz + above;
                    if (piece.Solid is { } poly) res.AddRange(PolyhedronGeometry(poly, z + lift, materialId));
                    else res.Add(ProfileSolidGeometry(piece.Profile!, z + lift, options));
                    continue;
                }
                // Sobre superfícies: a peça inteira num plano ajustado ao terreno; dividida só onde o terreno dobra
                // (sem "quadradinhos" de tamanho fixo).
                var parts = draped
                    ? DrapedParts(piece.Shape, sampler!, zBaseFt, 0).ToList()
                    : new List<(Polygon2, double, XYZ?)> { (piece.Shape, zBaseFt, null) };
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
    private static IEnumerable<(Polygon2 Shape, double Z, XYZ? Normal)> DrapedParts(Polygon2 shape, SurfaceSampler s, double zHintFt, int depth)
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
        if (dev <= UnitConv.Ft(0.015) || depth >= 7 || ext < 0.8)
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

    /// <summary>
    /// Cria o(s) piso(s) da peça (topo na cota da peça). Tenta o contorno original, depois limpo de lascas e, por fim,
    /// dividido sem furos. Lista vazia se o Revit recusar todas as tentativas (<paramref name="reason"/> recebe o motivo).
    /// </summary>
    private List<Floor> CreateFloors(MarkingPiece piece, double baseZm, ElementId? userType, ref string? reason, SurfaceSampler? terrain = null)
    {
        var res = new List<Floor>();
        var topFt = UnitConv.Ft(baseZm + piece.Elevation + piece.Thickness);
        var c0 = piece.Shape.Centroid;
        double? groundFt = null;
        if (terrain != null && terrain.TrySample(UnitConv.Ft(c0.X), UnitConv.Ft(c0.Y), topFt, out var gz, out _))
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

        // No terreno, o contorno ganha vértices a cada 4 m (pontos de apoio da deformação do piso).
        Polygon2 Prep(Polygon2 p) => groundFt == null ? p : Densified(p, 4.0);
        var attempts = new List<Func<List<Polygon2>>>
        {
            () => new List<Polygon2> { Prep(piece.Shape.Simplified(0.005) ?? piece.Shape) },
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
                var loops = ToCurveLoops(shape, level.ProjectElevation);
                if (loops.Count == 0) continue;
                try
                {
                    if (!BoundaryValidation.IsValidHorizontalBoundary(loops)) { ok = false; reason = "contorno inválido para o esboço do piso"; break; }
                    var f = Floor.Create(_doc, loops, typeId, level.Id, false, null, 0.0);
                    f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(topFt - level.ProjectElevation);
                    FloorSignature.Write(f, loops);
                    if (groundFt != null && terrain != null) DrapeFloor(f, shape, terrain, groundFt.Value);
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

    private static Polygon2 Densified(Polygon2 p, double maxLen)
    {
        List<Vec2> D(IReadOnlyList<Vec2> ring) => CurveTools.Densify(ring.Append(ring[0]).ToList(), maxLen).SkipLast(1).ToList();
        return new Polygon2(D(p.Outer), p.Holes.Select(h => (IEnumerable<Vec2>)D(h)));
    }

    /// <summary>
    /// Deforma o piso para acompanhar o terreno: edição de forma do Revit com os vértices do contorno e uma malha interna
    /// de pontos (4 m), cada um na cota da superfície. O piso continua editável com as ferramentas nativas.
    /// </summary>
    private void DrapeFloor(Floor f, Polygon2 shape, SurfaceSampler terrain, double groundFt)
    {
        try
        {
            var ed = f.GetSlabShapeEditor();
            ed.Enable();
            _doc.Regenerate();
            var top = f.get_BoundingBox(null)?.Max.Z ?? groundFt;
            // Pontos internos (malha de 4 m, afastados do contorno).
            var (mn, mx) = shape.Bounds;
            var inner = new List<XYZ>();
            const double step = 4.0;
            var edge = PolygonOps.Offset(new[] { shape }, -0.5);
            for (var x = mn.X + step / 2; x < mx.X; x += step)
                for (var y = mn.Y + step / 2; y < mx.Y; y += step)
                {
                    var v = new Vec2(x, y);
                    if (inner.Count < 400 && edge.Any(e => e.Contains(v))) inner.Add(new XYZ(UnitConv.Ft(x), UnitConv.Ft(y), top));
                }
            if (inner.Count > 0)
            {
                try { ed.AddPoints(inner); } catch (Exception ex) { Log.Error("SlabShape.AddPoints", ex); }
                _doc.Regenerate();
            }
            foreach (SlabShapeVertex v in ed.SlabShapeVertices)
            {
                var p = v.Position;
                if (!terrain.TrySample(p.X, p.Y, groundFt, out var z, out _)) continue;
                var off = z - groundFt;
                if (Math.Abs(off) > 1e-4) ed.ModifySubElement(v, off);
            }
        }
        catch (Exception ex)
        {
            Log.Error("DrapeFloor", ex);
        }
    }

    /// <summary>O contorno do piso foi alterado (ou o piso foi movido) pelo usuário depois de gerado?</summary>
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
            SharedParameters.Set(e, SharedParameters.Grupo, def is UrbanElementDefinition ue && PluginContext.Catalog.Movel(ue.Code) is { } mv
                ? UrbanCategories.Label(UrbanCategories.Of(mv.Forma)) : QuantityRow.GroupLabel(info.Group));
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
