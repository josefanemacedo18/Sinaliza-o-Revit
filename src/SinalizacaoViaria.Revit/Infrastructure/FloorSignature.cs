using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// "Assinatura" do contorno com que o plugin criou um piso (nº de curvas, perímetro e soma dos pontos médios). Na
/// regeneração, um piso cujo esboço não confere mais foi editado (ou movido) pelo usuário e é preservado.
/// </summary>
public static class FloorSignature
{
    private static readonly Guid SchemaGuid = new("4D2A7B90-3C1E-4F58-A6D2-91B0E7C45F13");
    private const string Field = "Signature";

    private static Schema GetSchema()
    {
        var s = Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizaBIMFloorSignature");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(Field, typeof(string));
        return b.Finish();
    }

    private static double[] Compute(IEnumerable<CurveLoop> loops)
    {
        double n = 0, len = 0, sx = 0, sy = 0;
        foreach (var loop in loops)
            foreach (var c in loop)
            {
                n++;
                len += c.Length;
                var m = c.Evaluate(0.5, true);
                sx += m.X;
                sy += m.Y;
            }
        return new[] { n, len, sx, sy };
    }

    public static void Write(Element e, IEnumerable<CurveLoop> loops)
    {
        try
        {
            var v = Compute(loops);
            var entity = new Entity(GetSchema());
            entity.Set(Field, string.Join(";", v.Select(x => x.ToString("R", CultureInfo.InvariantCulture))));
            e.SetEntity(entity);
        }
        catch (Exception ex)
        {
            Log.Error("FloorSignature.Write", ex);
        }
    }

    /// <summary>Verdadeiro se o contorno atual difere do gravado na criação (sem assinatura: falso).</summary>
    public static bool Differs(Element e, IEnumerable<CurveLoop> current)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema == null) return false;
        var entity = e.GetEntity(schema);
        if (entity == null || !entity.IsValid()) return false;
        var stored = entity.Get<string>(Field)?.Split(';')
            .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
        if (stored == null || stored.Length < 4 || stored.Any(double.IsNaN)) return false;
        var now = Compute(current);
        var tol = 0.01 * Math.Max(1, stored[0]);   // ~3 mm por curva (em pés)
        return Math.Abs(now[0] - stored[0]) > 0.5 || Math.Abs(now[1] - stored[1]) > 0.01
            || Math.Abs(now[2] - stored[2]) > tol || Math.Abs(now[3] - stored[3]) > tol;
    }
}
