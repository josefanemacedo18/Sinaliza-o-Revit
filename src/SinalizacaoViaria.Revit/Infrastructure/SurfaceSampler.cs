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

/// <summary>
/// Calçada com níveis variáveis (inclinação transversal, nível no alinhamento): a superfície de apoio (greide da via ou
/// plano da base) mais o perfil de níveis da faixa.
/// </summary>
public sealed class LevelSampler : ISurface
{
    private readonly ISurface? _inner;
    private readonly double _baseM;
    private readonly Func<Core.Geometry.Vec2, double> _offset;
    public LevelSampler(ISurface? inner, double baseM, Func<Core.Geometry.Vec2, double> offset) { _inner = inner; _baseM = baseM; _offset = offset; }
    public bool IsAvailable => true;
    public bool LiftsSolids => _inner?.LiftsSolids ?? false;
    public bool TrySample(double xFt, double yFt, double zHintFt, out double zFt, out XYZ normal)
    {
        var p = new Core.Geometry.Vec2(UnitConv.M(xFt), UnitConv.M(yFt));
        double Base(Core.Geometry.Vec2 q) =>
            _inner != null && _inner.TrySample(UnitConv.Ft(q.X), UnitConv.Ft(q.Y), zHintFt, out var z0, out _) ? UnitConv.M(z0) : _baseM;
        double Z(Core.Geometry.Vec2 q) => Base(q) + _offset(q);
        var z = Z(p);
        zFt = UnitConv.Ft(z);
        var zx = Z(p + new Core.Geometry.Vec2(0.5, 0)) - z;
        var zy = Z(p + new Core.Geometry.Vec2(0, 0.5)) - z;
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
/// Superfície dos pisos planos da pista (planos já divididos): cada ponto pega o plano do piso que o contém; fora deles, a
/// superfície de base. A pintura fica exatamente sobre os pisos.
/// </summary>
public sealed class TiledSampler : ISurface
{
    private readonly ISurface _base;
    private readonly List<(Core.Geometry.Polygon2 Part, Core.Geometry.Plane3 Plane, double Offset)> _tiles;
    private readonly Dictionary<(int, int), List<int>> _index = new();
    private const double Cell = 10;

    public TiledSampler(ISurface baseSurface, List<(Core.Geometry.Polygon2, Core.Geometry.Plane3, double)> tiles)
    {
        _base = baseSurface;
        _tiles = tiles;
        for (int i = 0; i < tiles.Count; i++)
        {
            var (mn, mx) = tiles[i].Item1.Bounds;
            for (var x = (int)Math.Floor(mn.X / Cell); x <= (int)Math.Floor(mx.X / Cell); x++)
                for (var y = (int)Math.Floor(mn.Y / Cell); y <= (int)Math.Floor(mx.Y / Cell); y++)
                {
                    if (!_index.TryGetValue((x, y), out var l)) _index[(x, y)] = l = new List<int>();
                    l.Add(i);
                }
        }
    }

    public bool IsAvailable => true;
    public bool LiftsSolids => _base.LiftsSolids;

    private int Find(Core.Geometry.Vec2 p)
    {
        if (!_index.TryGetValue(((int)Math.Floor(p.X / Cell), (int)Math.Floor(p.Y / Cell)), out var l)) return -1;
        foreach (var i in l) if (_tiles[i].Part.Contains(p)) return i;
        return -1;
    }

    public bool TrySample(double xFt, double yFt, double zHintFt, out double zFt, out XYZ normal)
    {
        var p = new Core.Geometry.Vec2(UnitConv.M(xFt), UnitConv.M(yFt));
        var i = Find(p);
        if (i < 0) return _base.TrySample(xFt, yFt, zHintFt, out zFt, out normal);
        var (_, pl, off) = _tiles[i];
        zFt = UnitConv.Ft(pl.Z(p) + off);
        normal = new XYZ(-pl.B, -pl.C, 1).Normalize();
        return true;
    }

    /// <summary>
    /// Peça recortada pelos pisos: cada pedaço com a cota (pés, no centro) e a normal do plano do piso em que está. Vazio se a
    /// peça não estiver sobre nenhum piso (usa-se então a superfície de base).
    /// </summary>
    public List<(Core.Geometry.Polygon2 Shape, double Z, XYZ? Normal)> Clip(Core.Geometry.Polygon2 shape)
    {
        var res = new List<(Core.Geometry.Polygon2, double, XYZ?)>();
        var (mn, mx) = shape.Bounds;
        var cand = new HashSet<int>();
        for (var x = (int)Math.Floor(mn.X / Cell); x <= (int)Math.Floor(mx.X / Cell); x++)
            for (var y = (int)Math.Floor(mn.Y / Cell); y <= (int)Math.Floor(mx.Y / Cell); y++)
                if (_index.TryGetValue((x, y), out var l)) foreach (var i in l) cand.Add(i);
        var covered = 0.0;
        foreach (var i in cand)
        {
            var (part, pl, off) = _tiles[i];
            List<Core.Geometry.Polygon2> pieces;
            try { pieces = Core.Geometry.PolygonOps.Intersect(new[] { shape }, new[] { part }); } catch { continue; }
            foreach (var q in pieces.Where(q => q.Area > 1e-5))
            {
                var c = q.Centroid;
                res.Add((q, UnitConv.Ft(pl.Z(c) + off), new XYZ(-pl.B, -pl.C, 1).Normalize()));
                covered += q.Area;
            }
        }
        // Só vale se a peça estiver (quase) toda sobre os pisos; senão, a superfície de base decide.
        return covered >= shape.Area * 0.98 ? res : new List<(Core.Geometry.Polygon2, double, XYZ?)>();
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
