using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Referência de posição/giro da placa (centro do suporte metálico e centro da chapa, em metros) gravada no elemento quando
/// o plugin o gera. Se o usuário mover ou girar a placa com as ferramentas do Revit, a referência medida na geometria atual
/// difere da gravada e o deslocamento vai para a definição (<see cref="SignFrame.Follow"/>): a regeneração não desfaz o giro.
/// </summary>
public static class SignFrameStore
{
    private static readonly Guid SchemaGuid = new("C3E5A1B7-2F84-4C69-9D0E-7B52A6F1E834");
    private const string Field = "Frame";

    private static Schema GetSchema()
    {
        var s = Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizaBIMSignFrame");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(Field, typeof(string));
        return b.Finish();
    }

    public static void Write(Element e, SignFrame f)
    {
        try
        {
            var entity = new Entity(GetSchema());
            entity.Set(Field, string.Join(";", new[] { f.Support.X, f.Support.Y, f.Face.X, f.Face.Y }.Select(x => x.ToString("R", CultureInfo.InvariantCulture))));
            e.SetEntity(entity);
        }
        catch (Exception ex)
        {
            Log.Error("SignFrameStore.Write", ex);
        }
    }

    public static SignFrame? Read(Element e)
    {
        try
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;
            var entity = e.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return null;
            var v = entity.Get<string>(Field)?.Split(';')
                .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
            if (v == null || v.Length < 4 || v.Any(double.IsNaN)) return null;
            return new SignFrame(new Vec2(v[0], v[1]), new Vec2(v[2], v[3]));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Referência medida nos sólidos (pés): centro por volume dos sólidos metálicos e dos demais.</summary>
    public static SignFrame? Of(IEnumerable<GeometryObject> geometry, ElementId metal)
    {
        double wm = 0, xm = 0, ym = 0, wf = 0, xf = 0, yf = 0;
        foreach (var s in Solids(geometry))
        {
            double v;
            XYZ c;
            try
            {
                v = s.Volume;
                if (v <= 1e-9) continue;
                c = s.ComputeCentroid();
            }
            catch { continue; }
            var isMetal = s.Faces.Size > 0 && s.Faces.get_Item(0).MaterialElementId == metal;
            if (isMetal) { wm += v; xm += c.X * v; ym += c.Y * v; }
            else { wf += v; xf += c.X * v; yf += c.Y * v; }
        }
        if (wm <= 0 || wf <= 0) return null;
        var support = new Vec2(UnitConv.M(xm / wm), UnitConv.M(ym / wm));
        var face = new Vec2(UnitConv.M(xf / wf), UnitConv.M(yf / wf));
        return support.DistanceTo(face) < SignFrame.MinLever ? null : new SignFrame(support, face);
    }

    /// <summary>Referência medida na geometria atual do elemento.</summary>
    public static SignFrame? Of(Element e, ElementId metal)
    {
        try
        {
            var g = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
            return g == null ? null : Of(g.Cast<GeometryObject>(), metal);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<Solid> Solids(IEnumerable<GeometryObject> geometry)
    {
        foreach (var o in geometry)
        {
            if (o is Solid s) yield return s;
            else if (o is GeometryInstance gi)
                foreach (var x in Solids(gi.GetInstanceGeometry().Cast<GeometryObject>())) yield return x;
            else if (o is GeometryElement ge)
                foreach (var x in Solids(ge.Cast<GeometryObject>())) yield return x;
        }
    }
}
