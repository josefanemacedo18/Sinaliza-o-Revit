using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Automation;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Projeto de Drenagem (chuva, critérios e identificação do memorial) guardado no próprio projeto (Informações do projeto).</summary>
public static class DrainageDesignStore
{
    private static readonly Guid SchemaGuid = new("3C9A7E15-52D4-4B6F-8E21-A7F04D6B9C38");
    private const string FJson = "Json";

    private static Schema Schema()
    {
        var s = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizaBIMProjetoDrenagem");
        b.SetDocumentation("Projeto de drenagem do plugin SinalizaBIM (equação de chuva, critérios e identificação do memorial).");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(FJson, typeof(string));
        return b.Finish();
    }

    /// <summary>O projeto gravado; sem registro, um projeto vazio (sem equação de chuva: só a geometria é conferida).</summary>
    public static ProjetoDrenagem Load(Document doc)
    {
        try
        {
            var info = doc.ProjectInformation;
            if (info == null) return new ProjetoDrenagem();
            var e = info.GetEntity(Schema());
            return e.IsValid() ? ProjetoDrenagem.FromJson(e.Get<string>(FJson)) : new ProjetoDrenagem();
        }
        catch (Exception ex)
        {
            Log.Error("Projeto de drenagem – leitura", ex);
            return new ProjetoDrenagem();
        }
    }

    /// <summary>Grava o projeto (transação própria).</summary>
    public static void Save(Document doc, ProjetoDrenagem p)
    {
        using var t = new Transaction(doc, "SV - Projeto de drenagem");
        t.Start();
        var e = new Entity(Schema());
        e.Set(FJson, p.ToJson());
        doc.ProjectInformation.SetEntity(e);
        t.Commit();
    }
}
