using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Terreno nativo lido diretamente da geometria dos Toposolids (sem vistas 3D nem raios) e o "terreno original" de cada um,
/// guardado no próprio Toposolid na primeira terraplenagem: é a partir dele que o terreno é refeito a cada ajuste.
/// </summary>
public static class TerrainModel
{
    private static readonly Guid SchemaGuid = new("3F7B2C14-9D5E-4A61-8B2F-6C1D0E9A4B73");
    private const string FMesh = "Original";
    private const string FFoot = "Footprint";

    private static Schema Schema()
    {
        var s = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizaBIMTerrenoOriginal");
        b.SetDocumentation("Terreno natural (antes da terraplenagem do plugin SinalizaBIM) e área já terraplenada.");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(FMesh, typeof(string));
        b.AddSimpleField(FFoot, typeof(string));
        return b.Finish();
    }

    /// <summary>Toposolids principais (não subdivisões).</summary>
    public static List<Toposolid> Hosts(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Cast<Toposolid>().Where(t => t.HostTopoId == ElementId.InvalidElementId).ToList();

    /// <summary>Topo atual do Toposolid como malha (m).</summary>
    public static TerrainMesh Live(Toposolid topo)
    {
        var mesh = new TerrainMesh();
        GeometryElement? ge;
        try { ge = topo.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false }); }
        catch { return mesh; }
        if (ge == null) return mesh;
        foreach (var solid in Solids(ge))
            foreach (Face f in solid.Faces)
            {
                try
                {
                    var bb = f.GetBoundingBox();
                    var n = f.ComputeNormal((bb.Min + bb.Max) / 2);
                    if (n.Z < 0.05) continue;
                    var m = f.Triangulate(0.5);
                    for (int i = 0; i < m.NumTriangles; i++)
                    {
                        var t = m.get_Triangle(i);
                        Vec3 V(int k) { var p = t.get_Vertex(k); return new Vec3(UnitConv.M(p.X), UnitConv.M(p.Y), UnitConv.M(p.Z)); }
                        var (a, b, c) = (V(0), V(1), V(2));
                        // Só faces que olham para cima (o topo) – as laterais e o fundo ficam de fora.
                        var nz = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                        if (Math.Abs(nz) < 1e-9) continue;
                        mesh.Add(a, b, c);
                    }
                }
                catch { /* face sem triangulação */ }
            }
        return mesh;
    }

    private static IEnumerable<Solid> Solids(GeometryElement ge)
    {
        foreach (var o in ge)
        {
            if (o is Solid s && s.Faces.Size > 0) yield return s;
            else if (o is GeometryInstance gi)
                foreach (var ss in Solids(gi.GetInstanceGeometry())) yield return ss;
        }
    }

    private static Entity? Entity(Element e)
    {
        var sc = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaGuid);
        if (sc == null) return null;
        try { var en = e.GetEntity(sc); return en != null && en.IsValid() ? en : null; }
        catch { return null; }
    }

    /// <summary>Terreno original guardado (ou o atual, se o Toposolid ainda não foi terraplenado pelo plugin).</summary>
    public static TerrainMesh Original(Toposolid topo)
    {
        var key = topo.UniqueId;
        if (_originals.TryGetValue(key, out var cached)) return cached;
        var en = Entity(topo);
        var mesh = en != null ? TerrainMesh.Deserialize(en.Get<string>(FMesh)) : null;
        if (mesh == null) return Live(topo);            // ainda não terraplenado: o terreno atual é o natural
        _originals[key] = mesh;
        return mesh;
    }

    public static bool HasOriginal(Toposolid topo) => Entity(topo) is { } en && !string.IsNullOrEmpty(en.Get<string>(FMesh));

    /// <summary>Área terraplenada da última vez (para restaurar o que saiu do projeto).</summary>
    public static List<Polygon2> LastFootprint(Toposolid topo)
    {
        var en = Entity(topo);
        var txt = en?.Get<string>(FFoot);
        var res = new List<Polygon2>();
        if (string.IsNullOrWhiteSpace(txt)) return res;
        foreach (var ring in txt.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var v = ring.Split(',').Select(x => double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToList();
            var pts = new List<Vec2>();
            for (int i = 0; i + 1 < v.Count; i += 2) if (!double.IsNaN(v[i]) && !double.IsNaN(v[i + 1])) pts.Add(new Vec2(v[i], v[i + 1]));
            if (pts.Count >= 3) res.Add(new Polygon2(pts));
        }
        return res;
    }

    /// <summary>Grava o terreno original (se ainda não houver) e a área terraplenada agora. Exige transação.</summary>
    public static void Save(Toposolid topo, TerrainMesh original, IEnumerable<Polygon2> footprint)
    {
        var sc = Schema();
        var en = Entity(topo) ?? new Entity(sc);
        if (string.IsNullOrEmpty(en.Get<string>(FMesh))) en.Set(FMesh, original.Serialize());
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        en.Set(FFoot, string.Join(";", footprint.Select(p => string.Join(",", p.Outer.SelectMany(q => new[] { q.X.ToString("0.###", inv), q.Y.ToString("0.###", inv) })))));
        topo.SetEntity(en);
        _originals[topo.UniqueId] = original;
    }

    /// <summary>Esquece o terreno original (o terreno atual passa a ser o natural – novo levantamento). Exige transação.</summary>
    public static void Reset(Toposolid topo)
    {
        _originals.Remove(topo.UniqueId);
        try { if (Entity(topo) != null) topo.DeleteEntity(Schema()); } catch { /* sem registro */ }
    }

    private static readonly Dictionary<string, TerrainMesh> _originals = new();

    /// <summary>Esquece as malhas em cache (outro documento, desfazer).</summary>
    public static void ClearCache() => _originals.Clear();

    /// <summary>
    /// Cota do terreno NATURAL (m, absoluta): terreno original de cada Toposolid (o de cima, onde houver mais de um).
    /// Nulo sem Toposolid sob o ponto.
    /// </summary>
    public static Func<Vec2, double?>? Ground(Document doc)
    {
        var meshes = Hosts(doc).Select(Original).Where(m => m.Count > 0).ToList();
        if (meshes.Count == 0) return null;
        return p =>
        {
            double? best = null;
            foreach (var m in meshes)
                if (m.Z(p) is { } z && (best == null || z > best)) best = z;
            return best;
        };
    }
}
