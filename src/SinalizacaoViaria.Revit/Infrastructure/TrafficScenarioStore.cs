using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Cenários do Simulador de Tráfego guardados no próprio projeto (Informações do projeto).</summary>
public static class TrafficScenarioStore
{
    private static readonly Guid SchemaGuid = new("6B0E4D2A-7C31-4F85-9A6E-2D8B1C5F0E47");
    private const string FJson = "Json";

    private static Schema Schema()
    {
        var s = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizaBIMCenariosTrafego");
        b.SetDocumentation("Cenários do Simulador de Tráfego do plugin SinalizaBIM (demanda, contagens, controles e planos).");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(FJson, typeof(string));
        return b.Finish();
    }

    public static List<TrafficScenario> Load(Document doc)
    {
        try
        {
            var info = doc.ProjectInformation;
            if (info == null) return new();
            var e = info.GetEntity(Schema());
            return e.IsValid() ? TrafficScenario.FromJson(e.Get<string>(FJson)) : new();
        }
        catch (Exception ex)
        {
            Log.Error("Cenários de tráfego – leitura", ex);
            return new();
        }
    }

    /// <summary>Grava a lista de cenários (transação própria).</summary>
    public static void Save(Document doc, IEnumerable<TrafficScenario> list)
    {
        using var t = new Transaction(doc, "Simulador de Tráfego – salvar cenários");
        t.Start();
        var e = new Entity(Schema());
        e.Set(FJson, TrafficScenario.ToJson(list));
        doc.ProjectInformation.SetEntity(e);
        t.Commit();
    }
}
