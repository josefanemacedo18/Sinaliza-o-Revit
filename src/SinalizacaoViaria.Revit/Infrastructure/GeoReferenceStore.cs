using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Geo;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Georreferenciamento do projeto (origem geográfica das imagens de satélite) guardado no próprio projeto.</summary>
public static class GeoReferenceStore
{
    private static readonly Guid SchemaGuid = new("3E9C2B71-5A04-4D6F-8B1E-7C2A9F40D615");
    private const string FJson = "Json";

    private static Schema Schema()
    {
        var s = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizaBIMGeorreferencia");
        b.SetDocumentation("Origem geográfica (latitude, longitude, altitude, Norte verdadeiro) das imagens de satélite do plugin SinalizaBIM.");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(FJson, typeof(string));
        return b.Finish();
    }

    public static GeoReference? Load(Document doc)
    {
        try
        {
            var e = doc.ProjectInformation?.GetEntity(Schema());
            return e is { } en && en.IsValid() ? GeoReference.FromJson(en.Get<string>(FJson)) : null;
        }
        catch (Exception ex)
        {
            Log.Error("Georreferência – leitura", ex);
            return null;
        }
    }

    /// <summary>Grava (dentro de uma transação já aberta).</summary>
    public static void Save(Document doc, GeoReference geo)
    {
        var e = new Entity(Schema());
        e.Set(FJson, geo.ToJson());
        doc.ProjectInformation.SetEntity(e);
    }
}
