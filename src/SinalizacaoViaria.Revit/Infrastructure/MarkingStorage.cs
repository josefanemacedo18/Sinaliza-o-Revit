using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Registro gravado em cada elemento de sinalização (Extensible Storage).</summary>
public sealed record StoredMarking(Element Element, string MarkingId, MarkingColor Color, MarkingDefinition Definition);

/// <summary>
/// Grava a definição paramétrica completa (JSON) em cada elemento gerado. É isso que permite editar,
/// regenerar e atualizar automaticamente a sinalização – o equivalente aos "objetos inteligentes" do CAD.
/// </summary>
public static class MarkingStorage
{
    public static readonly Guid SchemaGuid = new("7C1E9A52-4B2D-4F6A-9C83-2E5D1B0A6F41");
    private const string FId = "MarkingId";
    private const string FDef = "Definition";
    private const string FColor = "Color";
    private const string FVersion = "SchemaVersion";

    public static Schema GetSchema()
    {
        var s = Schema.Lookup(SchemaGuid);
        if (s != null) return s;
        var b = new SchemaBuilder(SchemaGuid);
        b.SetSchemaName("SinalizacaoViariaMarking");
        b.SetDocumentation("Definição paramétrica de sinalização viária horizontal (plugin SinalizacaoViaria).");
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField(FId, typeof(string));
        b.AddSimpleField(FDef, typeof(string));
        b.AddSimpleField(FColor, typeof(string));
        b.AddSimpleField(FVersion, typeof(int));
        return b.Finish();
    }

    public static void Write(Element e, MarkingDefinition def, MarkingColor color)
    {
        var schema = GetSchema();
        var entity = new Entity(schema);
        entity.Set(FId, def.Id);
        entity.Set(FDef, def.ToJson());
        entity.Set(FColor, color.ToString());
        entity.Set(FVersion, MarkingDefinition.CurrentVersion);
        e.SetEntity(entity);
    }

    public static StoredMarking? Read(Element e)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema == null) return null;
        Entity entity;
        try { entity = e.GetEntity(schema); }
        catch { return null; }
        if (entity == null || !entity.IsValid()) return null;
        try
        {
            var id = entity.Get<string>(FId);
            var json = entity.Get<string>(FDef);
            var def = MarkingDefinition.FromJson(json);
            if (def == null) return null;
            MarkingColors.TryParse(entity.Get<string>(FColor), out var color);
            return new StoredMarking(e, id, color, def);
        }
        catch (Exception ex)
        {
            Log.Error($"Leitura de definição do elemento {e.Id}", ex);
            return null;
        }
    }

    public static bool IsMarking(Element e) => Read(e) != null;

    /// <summary>Todos os elementos de sinalização do documento.</summary>
    public static List<StoredMarking> All(Document doc)
    {
        var res = new List<StoredMarking>();
        if (Schema.Lookup(SchemaGuid) == null) return res;
        var col = new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(new ExtensibleStorageFilter(SchemaGuid));
        foreach (var e in col)
        {
            var r = Read(e);
            if (r != null) res.Add(r);
        }
        return res;
    }

    /// <summary>Uma definição por marca (conjuntos com várias cores aparecem uma única vez).</summary>
    public static List<MarkingDefinition> Definitions(Document doc) =>
        All(doc).GroupBy(r => r.MarkingId).Select(g => g.First().Definition).ToList();

    public static List<StoredMarking> ById(Document doc, string markingId) =>
        All(doc).Where(r => r.MarkingId == markingId).ToList();
}
