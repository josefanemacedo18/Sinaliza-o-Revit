using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
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
    private static Catalogo Catalog => PluginContext.Catalog;

    // ------------------------------------------------------------------ geometria

    /// <summary>Gera a geometria (sem tocar no modelo) – usado também em quantitativos e prévias.</summary>
    public MarkingGeometry BuildGeometry(MarkingDefinition def, out double baseZ, List<string>? warnings = null)
    {
        var ctx = PluginContext.BuildContext(def.Output.Drape && def.Output.Mode == OutputMode.Modelo3D);
        switch (def)
        {
            case SymbolMarkingDefinition s:
                baseZ = s.Z;
                return MarkingBuilder.Build(def, null, ctx);
            case TextMarkingDefinition t:
                baseZ = t.Z;
                return MarkingBuilder.Build(def, null, ctx);
        }

        var path = PathResolver.Resolve(_doc, def.Path);
        baseZ = path?.Z ?? 0;
        if (path != null) warnings?.AddRange(path.Warnings);
        if (path == null || path.Chains.Count == 0) return MarkingBuilder.Build(def, null, ctx);

        var geo = new MarkingGeometry();
        foreach (var chain in path.Chains)
        {
            if (def is HatchMarkingDefinition && chain.Points.Count < 3) continue;
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
        var existing = MarkingStorage.ById(_doc, def.Id);

        MarkingGeometry geo;
        double baseZ;
        try
        {
            geo = BuildGeometry(def, out baseZ, result.Warnings);
        }
        catch (Exception ex)
        {
            Log.Error($"BuildGeometry {def.DisplayCode}", ex);
            result.Warnings.Add($"{def.DisplayCode}: erro ao gerar a geometria – {ex.Message}");
            return result;
        }
        result.Geometry = geo;
        result.Warnings.AddRange(geo.Warnings);

        if (geo.Pieces.Count == 0)
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

        var groups = geo.Pieces.GroupBy(p => p.Color)
            .OrderByDescending(g => g.Sum(p => p.Shape.Area)).ToList();
        var keep = new HashSet<ElementId>();
        var primary = true;

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
                var solids = BuildSolids(g, baseZ + def.Output.ElevationOffset, thickness, sampler, Styles.Material(g.Key), result.Warnings);
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
            var view = ResolveView(def, result);
            if (view == null)
            {
                result.Elements.AddRange(existing.Select(e => e.Element.Id));
                return result;
            }
            var z = PlaneZ(view);
            // Regiões preenchidas não podem ser remodeladas: remove e recria.
            foreach (var old in existing.Where(r => r.Element is FilledRegion)) SafeDelete(old.Element.Id);
            existing.RemoveAll(r => r.Element is FilledRegion);

            foreach (var g in groups)
            {
                var regions = CreateRegions(view, g.Key, g.Select(p => p.Shape).ToList(), z, result.Warnings);
                foreach (var fr in regions)
                {
                    Tag(fr, def, info, g.Key, materialName, fr == regions[0] ? g.Sum(p => p.Shape.Area) : 0, geo, primary);
                    keep.Add(fr.Id);
                    primary = false;
                }
            }
        }

        foreach (var old in existing.Where(r => !keep.Contains(r.Element.Id)))
            SafeDelete(old.Element.Id);

        result.Elements.AddRange(keep);
        return result;
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
                XYZ? normal = null;
                var c = piece.Shape.Centroid;
                if (sampler is { IsAvailable: true } && sampler.TrySample(UnitConv.Ft(c.X), UnitConv.Ft(c.Y), zBaseFt, out var sz, out var n))
                {
                    z = sz + UnitConv.Ft(0.001);
                    normal = n;
                }
                var loops = ToCurveLoops(piece.Shape, z);
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

    // ------------------------------------------------------------------ 2D

    private List<FilledRegion> CreateRegions(View view, MarkingColor color, List<Polygon2> shapes, double zFt, List<string> warnings)
    {
        var res = new List<FilledRegion>();
        var typeId = Styles.FilledRegionType(color);
        ElementId? lineStyle = PluginContext.Settings.VisibleBoundary2D ? null : Styles.InvisibleLineStyle();

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
