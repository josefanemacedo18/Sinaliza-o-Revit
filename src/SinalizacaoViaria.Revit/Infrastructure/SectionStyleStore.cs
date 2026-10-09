using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Serialization;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Nomes, cores e hachuras escolhidos para os elementos das seções transversais, guardados no próprio projeto (Informações
/// do projeto): as próximas seções deste projeto já começam com essas escolhas.
/// </summary>
public static class SectionStyleStore
{
    private static readonly Guid SchemaGuid = new("3D8A6F21-5B47-4C9E-8F13-A2E7C0B94D58");
    private const string FJson = "Json";

    private static Schema Schema()
    {
        var s = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizaBIMEstilosSecao");
        b.SetDocumentation("Nomes, cores e hachuras dos elementos das seções transversais do plugin SinalizaBIM.");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(FJson, typeof(string));
        return b.Finish();
    }

    public static List<SectionItemStyle> Load(Document doc)
    {
        try
        {
            var info = doc.ProjectInformation;
            if (info == null) return new();
            var e = info.GetEntity(Schema());
            if (!e.IsValid()) return new();
            return JsonSerializer.Deserialize<List<SectionItemStyle>>(e.Get<string>(FJson), JsonConfig.Compact) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("Estilos de seção – leitura", ex);
            return new();
        }
    }

    /// <summary>Junta <paramref name="styles"/> às escolhas já guardadas (por elemento). Usa a transação aberta ou abre uma.</summary>
    public static void Remember(Document doc, IEnumerable<SectionItemStyle> styles)
    {
        var merged = SectionItemStyle.Merge(Load(doc), styles);
        void Write()
        {
            var e = new Entity(Schema());
            e.Set(FJson, JsonSerializer.Serialize(merged, JsonConfig.Compact));
            doc.ProjectInformation.SetEntity(e);
        }
        try
        {
            if (doc.IsModifiable) { Write(); return; }
            using var t = new Transaction(doc, "SV - Estilos da seção");
            t.Start();
            Write();
            t.Commit();
        }
        catch (Exception ex)
        {
            Log.Error("Estilos de seção – gravação", ex);
        }
    }
}
