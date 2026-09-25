using System.IO;
using Autodesk.Revit.DB;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Parâmetros compartilhados de instância (GUIDs fixos) vinculados a Modelos genéricos e Itens de detalhe,
/// permitindo tabelas, filtros de vista e etiquetas nativas do Revit.
/// </summary>
public static class SharedParameters
{
    public sealed record Def(string Name, Guid Guid, ForgeTypeId Spec, string Description);

    public static readonly Def Codigo = new("SV_Codigo", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B01"), SpecTypeId.String.Text, "Código da marca (MBST): LFO-2, LBO, FTP-1...");
    public static readonly Def Descricao = new("SV_Descricao", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B02"), SpecTypeId.String.Text, "Descrição da marca viária.");
    public static readonly Def Grupo = new("SV_Grupo", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B03"), SpecTypeId.String.Text, "Grupo: longitudinal, transversal, canalização...");
    public static readonly Def Cor = new("SV_Cor", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B04"), SpecTypeId.String.Text, "Cor da demarcação.");
    public static readonly Def Material = new("SV_Material", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B05"), SpecTypeId.String.Text, "Material de demarcação.");
    public static readonly Def Area = new("SV_Area", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B06"), SpecTypeId.Area, "Área pintada.");
    public static readonly Def Extensao = new("SV_Extensao", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B07"), SpecTypeId.Length, "Extensão pintada (soma dos traços).");
    public static readonly Def Quantidade = new("SV_Quantidade", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B08"), SpecTypeId.Int.Integer, "Unidades (tachas, vagas, símbolos).");
    public static readonly Def Referencia = new("SV_Referencia", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B09"), SpecTypeId.String.Text, "Referência normativa.");
    public static readonly Def Id = new("SV_Id", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B0A"), SpecTypeId.String.Text, "Identificador do conjunto de sinalização.");

    public static readonly Def Categoria = new("SV_Categoria", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B0B"), SpecTypeId.String.Text, "Categoria do quantitativo: sinalização horizontal, vertical, dispositivos, acessibilidade, calçadas...");

    public static readonly Def Hierarquia = new("SV_Hierarquia", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B0C"), SpecTypeId.String.Text, "Hierarquia viária (CTB art. 60): trânsito rápido, arterial, coletora, local, rodovia, estrada.");

    public static IReadOnlyList<Def> All { get; } = new[] { Codigo, Descricao, Grupo, Cor, Material, Area, Extensao, Quantidade, Referencia, Id, Categoria, Hierarquia };

    private const string GroupName = "Sinalizacao Viaria";

    /// <summary>Garante os parâmetros no documento. Deve ser chamado dentro de uma transação.</summary>
    public static void Ensure(Document doc)
    {
        if (All.All(d => SharedParameterElement.Lookup(doc, d.Guid) != null)) return;

        var app = doc.Application;
        var original = app.SharedParametersFilename;
        try
        {
            Directory.CreateDirectory(PluginPaths.Root);
            if (!File.Exists(PluginPaths.SharedParameters)) File.WriteAllText(PluginPaths.SharedParameters, string.Empty);
            app.SharedParametersFilename = PluginPaths.SharedParameters;
            var file = app.OpenSharedParameterFile();
            if (file == null) { Log.Info("Não foi possível abrir o arquivo de parâmetros compartilhados."); return; }
            var group = file.Groups.get_Item(GroupName) ?? file.Groups.Create(GroupName);

            var cats = app.Create.NewCategorySet();
            cats.Insert(Category.GetCategory(doc, BuiltInCategory.OST_GenericModel));
            cats.Insert(Category.GetCategory(doc, BuiltInCategory.OST_DetailComponents));
            var binding = app.Create.NewInstanceBinding(cats);

            foreach (var d in All)
            {
                if (SharedParameterElement.Lookup(doc, d.Guid) != null) continue;
                var def = group.Definitions.get_Item(d.Name) as ExternalDefinition;
                if (def == null || def.GUID != d.Guid)
                {
                    var opt = new ExternalDefinitionCreationOptions(def == null ? d.Name : d.Name + "_", d.Spec)
                    {
                        GUID = d.Guid,
                        Description = d.Description,
                    };
                    def = (ExternalDefinition)group.Definitions.Create(opt);
                }
                doc.ParameterBindings.Insert(def, binding, GroupTypeId.Data);
            }
        }
        catch (Exception ex)
        {
            Log.Error("SharedParameters.Ensure", ex);
        }
        finally
        {
            try { app.SharedParametersFilename = original; } catch { /* sem arquivo original */ }
        }
    }

    public static void Set(Element e, Def d, string? value)
    {
        var p = e.get_Parameter(d.Guid);
        if (p != null && !p.IsReadOnly) p.Set(value ?? string.Empty);
    }

    public static void Set(Element e, Def d, double internalValue)
    {
        var p = e.get_Parameter(d.Guid);
        if (p != null && !p.IsReadOnly) p.Set(internalValue);
    }

    public static void Set(Element e, Def d, int value)
    {
        var p = e.get_Parameter(d.Guid);
        if (p != null && !p.IsReadOnly) p.Set(value);
    }
}
