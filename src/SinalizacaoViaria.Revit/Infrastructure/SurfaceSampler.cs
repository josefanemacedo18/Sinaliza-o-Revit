using Autodesk.Revit.DB;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Projeta pontos verticalmente sobre superfícies (Toposolid, pisos, topografia) usando
/// ReferenceIntersector, obtendo elevação e normal – permite que a sinalização acompanhe o greide.
/// </summary>
public sealed class SurfaceSampler
{
    private readonly Document _doc;
    private readonly ReferenceIntersector? _intersector;
    public bool IsAvailable => _intersector != null;

    public SurfaceSampler(Document doc, IEnumerable<string> surfaceUniqueIds, bool allowCreateView)
    {
        _doc = doc;
        var view = Find3DView(doc) ?? (allowCreateView ? Create3DView(doc) : null);
        if (view == null) return;

        var ids = surfaceUniqueIds.Select(u => doc.GetElement(u)?.Id).Where(i => i != null).Cast<ElementId>().ToList();
        if (ids.Count > 0)
        {
            _intersector = new ReferenceIntersector(ids, FindReferenceTarget.Face, view);
        }
        else
        {
            var cats = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_Toposolid),
                new ElementCategoryFilter(BuiltInCategory.OST_Floors),
                new ElementCategoryFilter(BuiltInCategory.OST_Topography),
            };
            _intersector = new ReferenceIntersector(new LogicalOrFilter(cats), FindReferenceTarget.Face, view);
        }
        _intersector.FindReferencesInRevitLinks = false;
    }

    /// <summary>Elevação (pés) e normal da superfície sob o ponto (coordenadas internas).</summary>
    public bool TrySample(double xFt, double yFt, double zHintFt, out double zFt, out XYZ normal)
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
