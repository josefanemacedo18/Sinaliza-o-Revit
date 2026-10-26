using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Automation;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Registro da última terraplenagem aplicada (Etapa 5) guardado no próprio projeto (Informações do projeto): o que moldou o
/// terreno, com que assinatura, subleito e taludes. Alimenta a V15 (terraplenagem desatualizada) e o fundo da estrutura no
/// Autoteste (K19).
/// </summary>
public static class EarthworkRecordStore
{
    private static readonly Guid SchemaGuid = new("CCFFAF19-0BB3-45C2-9C33-E558BEF93B1C");
    private const string FJson = "Json";

    private static Schema Schema()
    {
        var s = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizaBIMRegistroTerraplenagem");
        b.SetDocumentation("Registro da última terraplenagem aplicada pelo plugin SinalizaBIM (participantes, assinaturas, subleito e taludes).");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(FJson, typeof(string));
        return b.Finish();
    }

    /// <summary>O registro gravado; nulo quando a terraplenagem nunca foi aplicada por esta versão (ou o registro não se lê).</summary>
    public static RegistroTerraplenagem? Load(Document doc)
    {
        try
        {
            var info = doc.ProjectInformation;
            if (info == null) return null;
            var e = info.GetEntity(Schema());
            return e.IsValid() ? RegistroTerraplenagem.FromJson(e.Get<string>(FJson)) : null;
        }
        catch (Exception ex)
        {
            Log.Error("Registro da terraplenagem – leitura", ex);
            return null;
        }
    }

    /// <summary>Grava o registro. Deve ser chamado dentro da transação da terraplenagem (desfazer a terraplenagem desfaz o registro).</summary>
    public static void Save(Document doc, RegistroTerraplenagem r)
    {
        var e = new Entity(Schema());
        e.Set(FJson, r.ToJson());
        doc.ProjectInformation.SetEntity(e);
    }
}
