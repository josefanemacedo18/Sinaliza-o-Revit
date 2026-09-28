using Autodesk.Revit.DB;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Superfície de apoio das marcas: terreno/pisos (raios) ou o greide da via (analítico).</summary>
public interface ISurface
{
    bool IsAvailable { get; }
    /// <summary>Sólidos e perfis sobem pela cota do centro (falso = já vêm deformados pelo greide do núcleo).</summary>
    bool LiftsSolids { get; }
    bool TrySample(double xFt, double yFt, double zHintFt, out double zFt, out XYZ normal);
}

/// <summary>Greide da via como superfície: cota = base do eixo + greide (estação, afastamento).</summary>
public sealed class GradeSampler : ISurface
{
    private readonly Core.Geometry.GradeSurface _surface;
    private readonly double _baseM;
    public GradeSampler(Core.Geometry.GradeSurface surface, double baseM) { _surface = surface; _baseM = baseM; }
    public Core.Geometry.GradeSurface Surface => _surface;
    private List<double>? _grid;
    /// <summary>Estacas comuns a todos os pisos da via (malha limpa e juntas coincidentes).</summary>
    public List<double> Grid => _grid ??= Core.Geometry.GradeFloors.Grid(_surface);
    public bool IsAvailable => true;
    public bool LiftsSolids => false;
    public bool TrySample(double xFt, double yFt, double zHintFt, out double zFt, out XYZ normal)
    {
        var p = new Core.Geometry.Vec2(UnitConv.M(xFt), UnitConv.M(yFt));
        var z = _surface.Z(p);
        zFt = UnitConv.Ft(_baseM + z);
        // Normal pela diferença finita (0,5 m).
        var zx = _surface.Z(p + new Core.Geometry.Vec2(0.5, 0)) - z;
        var zy = _surface.Z(p + new Core.Geometry.Vec2(0, 0.5)) - z;
        normal = new XYZ(-zx / 0.5, -zy / 0.5, 1).Normalize();
        return true;
    }
}

/// <summary>Superfície de um nó em nível (interseção, rotatória, cul-de-sac) costurada às vias ligadas – cota absoluta.</summary>
public sealed class NodeSampler : ISurface
{
    private readonly Core.Geometry.NodeSurface _node;
    public NodeSampler(Core.Geometry.NodeSurface node) => _node = node;
    public Core.Geometry.NodeSurface Node => _node;
    public bool IsAvailable => true;
    // Sólidos do nó já vêm deformados vértice a vértice pela superfície (MarkingService.BuildGeometry).
    public bool LiftsSolids => false;
    public bool TrySample(double xFt, double yFt, double zHintFt, out double zFt, out XYZ normal)
    {
        var p = new Core.Geometry.Vec2(UnitConv.M(xFt), UnitConv.M(yFt));
        var z = _node.Z(p);
        zFt = UnitConv.Ft(z);
        var zx = _node.Z(p + new Core.Geometry.Vec2(0.5, 0)) - z;
        var zy = _node.Z(p + new Core.Geometry.Vec2(0, 0.5)) - z;
        normal = new XYZ(-zx / 0.5, -zy / 0.5, 1).Normalize();
        return true;
    }
}

/// <summary>
/// Projeta pontos verticalmente sobre superfícies (Toposolid, pisos, topografia) usando
/// ReferenceIntersector, obtendo elevação e normal – permite que a sinalização acompanhe o greide.
/// </summary>
public sealed class SurfaceSampler : ISurface
{
    public bool LiftsSolids => true;
    private readonly Document _doc;
    private readonly ReferenceIntersector? _intersector;
    public bool IsAvailable => _intersector != null;

    private readonly Dictionary<(long, long), (bool Ok, double Z, XYZ N)> _cache = new();

    /// <param name="terrainOnly">
    /// Só o terreno (Toposolid/topografia e superfícies escolhidas que não sejam do plugin) – usado pelos pisos do próprio
    /// plugin, que não podem se apoiar em si mesmos nem em outros pisos gerados.
    /// </param>
    public SurfaceSampler(Document doc, IEnumerable<string> surfaceUniqueIds, bool allowCreateView, bool terrainOnly = false)
    {
        _doc = doc;
        var view = Find3DView(doc) ?? (allowCreateView ? Create3DView(doc) : null);
        if (view == null) return;

        var ids = surfaceUniqueIds.Select(u => doc.GetElement(u)).Where(e => e != null && (!terrainOnly || !MarkingStorage.IsMarking(e)))
            .Select(e => e!.Id).ToList();
        if (ids.Count > 0)
        {
            _intersector = new ReferenceIntersector(ids, FindReferenceTarget.Face, view);
        }
        else
        {
            var cats = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_Toposolid),
                new ElementCategoryFilter(BuiltInCategory.OST_Topography),
            };
            if (!terrainOnly) cats.Add(new ElementCategoryFilter(BuiltInCategory.OST_Floors));
            _intersector = new ReferenceIntersector(new LogicalOrFilter(cats), FindReferenceTarget.Face, view);
        }
        _intersector.FindReferencesInRevitLinks = false;
    }

    /// <summary>Elevação (pés) e normal da superfície sob o ponto (coordenadas internas).</summary>
    public bool TrySample(double xFt, double yFt, double zHintFt, out double zFt, out XYZ normal)
    {
        var key = ((long)Math.Round(xFt * 20), (long)Math.Round(yFt * 20));   // ~1,5 cm
        if (_cache.TryGetValue(key, out var c))
        {
            zFt = c.Ok ? c.Z : zHintFt;
            normal = c.N;
            return c.Ok;
        }
        var ok = Sample(xFt, yFt, zHintFt, out zFt, out normal);
        _cache[key] = (ok, zFt, normal);
        return ok;
    }

    /// <summary>Referência da face (piso, topografia) sob o ponto – para hospedar famílias baseadas em face ou em piso.</summary>
    public Reference? HitReference(double xFt, double yFt, double zHintFt)
    {
        if (_intersector == null) return null;
        try
        {
            var hit = _intersector.FindNearest(new XYZ(xFt, yFt, zHintFt + 300), -XYZ.BasisZ)
                      ?? _intersector.FindNearest(new XYZ(xFt, yFt, zHintFt - 300), XYZ.BasisZ);
            return hit?.GetReference();
        }
        catch
        {
            return null;
        }
    }

    private bool Sample(double xFt, double yFt, double zHintFt, out double zFt, out XYZ normal)
    {
        zFt = zHintFt;
        normal = XYZ.BasisZ;
        if (_intersector == null) return false;
        var origin = new XYZ(xFt, yFt, zHintFt + 300);
        var hit = _intersector.FindNearest(origin, -XYZ.BasisZ);
        if (hit == null)
        {
            // Superfície acima do caminho? tenta de baixo para cima.
            origin = new XYZ(xFt, yFt, zHintFt - 300);
            hit = _intersector.FindNearest(origin, XYZ.BasisZ);
            if (hit == null) return false;
        }
        var reference = hit.GetReference();
        var pt = reference.GlobalPoint;
        zFt = pt.Z;
        try
        {
            if (_doc.GetElement(reference)?.GetGeometryObjectFromReference(reference) is Face face)
            {
                var proj = face.Project(pt);
                if (proj != null)
                {
                    var n = face.ComputeNormal(proj.UVPoint);
                    if (n.Z < 0) n = n.Negate();
                    if (n.Z > 0.2) normal = n.Normalize();
                }
            }
        }
        catch
        {
            normal = XYZ.BasisZ;
        }
        return true;
    }

    public static View3D? Find3DView(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
            .Where(v => !v.IsTemplate && !v.IsPerspective)
            .OrderByDescending(v => v.Name == "{3D}" || v.Name.StartsWith("SV - "))
            .FirstOrDefault();

    private static View3D? Create3DView(Document doc)
    {
        try
        {
            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            if (vft == null) return null;
            var v = View3D.CreateIsometric(doc, vft.Id);
            v.Name = "SV - Projeção de sinalização";
            return v;
        }
        catch (Exception ex)
        {
            Log.Error("Create3DView", ex);
            return null;
        }
    }
}
