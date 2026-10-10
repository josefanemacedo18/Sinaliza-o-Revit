using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Catalog;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Placas personalizadas do projeto, guardadas no próprio projeto (Informações do projeto): ao abrir o projeto em outra
/// máquina as placas continuam lá – no modelo, na legenda, no detalhe, no quadro de placas e nos quantitativos.
/// </summary>
public static class CustomSignStore
{
    private static readonly Guid SchemaGuid = new("6B1F2C44-8E3A-4D57-9A61-0C5E7D2B93F4");
    private const string FJson = "Json";

    private static Schema Schema()
    {
        var s = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizaBIMPlacasPersonalizadas");
        b.SetDocumentation("Placas de sinalização personalizadas do projeto (plugin SinalizaBIM).");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(FJson, typeof(string));
        return b.Finish();
    }

    public static List<PlacaDef> Load(Document doc)
    {
        try
        {
            var info = doc.ProjectInformation;
            if (info == null) return new();
            var e = info.GetEntity(Schema());
            return e.IsValid() ? CustomSigns.Parse(e.Get<string>(FJson)) : new();
        }
        catch (Exception ex)
        {
            Log.Error("Placas personalizadas – leitura", ex);
            return new();
        }
    }

    /// <summary>Acrescenta as placas personalizadas do projeto ao catálogo em uso.</summary>
    public static void Apply(Document doc) => CustomSigns.Apply(PluginContext.Catalog, Load(doc));

    /// <summary>Guarda as placas no projeto (substitui as de mesmo código). Usa a transação aberta ou abre uma.</summary>
    public static void Save(Document doc, IEnumerable<PlacaDef> signs)
    {
        var all = Load(doc);
        foreach (var p in signs) all = CustomSigns.Upsert(all, p);
        void Write()
        {
            var e = new Entity(Schema());
            e.Set(FJson, CustomSigns.Serialize(all));
            doc.ProjectInformation.SetEntity(e);
        }
        try
        {
            if (doc.IsModifiable) Write();
            else
            {
                using var t = new Transaction(doc, "SV - Placas personalizadas");
                t.Start();
                Write();
                t.Commit();
            }
            CustomSigns.Apply(PluginContext.Catalog, all);
        }
        catch (Exception ex)
        {
            Log.Error("Placas personalizadas – gravação", ex);
        }
    }
}
