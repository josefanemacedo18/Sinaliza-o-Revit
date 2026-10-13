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

    /// <summary>Unidade de medição do item (m², m ou un) – coluna "Un." da tabela de quantitativos.</summary>
    public static readonly Def Unidade = new("SV_Unidade", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B0D"), SpecTypeId.String.Text, "Unidade de medição do item: m², m ou un.");

    /// <summary>Quantidade na unidade de medição do item (área, extensão ou unidades) – coluna "Quantidade", somada na tabela.</summary>
    public static readonly Def QtdMedicao = new("SV_QtdMedicao", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B0E"), SpecTypeId.Number, "Quantidade na unidade de medição (m², m ou un).");

    /// <summary>Nível de serviço (HCM) gravado pelo Simulador de Tráfego no cruzamento/via.</summary>
    public static readonly Def NivelServico = new("SV_NivelServico", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B0F"), SpecTypeId.String.Text, "Nível de serviço (A a F) do último cenário gravado pelo Simulador de Tráfego.");

    /// <summary>Resumo do tráfego (cenário, volume, atraso, controle, ciclo) gravado pelo Simulador de Tráfego.</summary>
    public static readonly Def Trafego = new("SV_Trafego", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B10"), SpecTypeId.String.Text, "Resumo do Simulador de Tráfego: cenário, volume, atraso, controle e ciclo.");

    public static IReadOnlyList<Def> All { get; } = new[] { Codigo, Descricao, Grupo, Cor, Material, Area, Extensao, Quantidade, Referencia, Id, Categoria, Hierarquia, Unidade, QtdMedicao, NivelServico, Trafego };

    // Loteamento: identificação do lote (filtros por categoria, tabelas e etiquetas). Ligados a Toposolid, Pisos e Modelos genéricos.
    public static readonly Def Quadra = new("SV_Quadra", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B11"), SpecTypeId.String.Text, "Quadra do loteamento (A, B, C...).");
    public static readonly Def Lote = new("SV_Lote", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B12"), SpecTypeId.String.Text, "Número do lote na quadra (1 a N) ou da área (AV-1, AI-1...).");
    public static readonly Def CategoriaLote = new("SV_CategoriaLote", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B13"), SpecTypeId.String.Text, "Categoria da área do loteamento: Lote, Institucional, Área verde, Sobra...");
    public static readonly Def AreaLote = new("SV_AreaLote", new("A1D4C1B0-5E3F-4D3A-9B21-0C8E7F6A5B14"), SpecTypeId.Area, "Área do lote (projeção horizontal).");

    public static IReadOnlyList<Def> Lotes { get; } = new[] { Quadra, Lote, CategoriaLote, AreaLote };

    /// <summary>Categorias que recebem os parâmetros de lote (subdivisão do Toposolid, piso sem Toposolid e forma direta).</summary>
    public static readonly BuiltInCategory[] LotCategories = { BuiltInCategory.OST_Toposolid, BuiltInCategory.OST_Floors, BuiltInCategory.OST_GenericModel };

    /// <summary>Garante os parâmetros de lote no documento (transação aberta).</summary>
    public static void EnsureLotes(Document doc)
    {
        var app = doc.Application;
        var cats = app.Create.NewCategorySet();
        foreach (var bic in LotCategories)
            if (Category.GetCategory(doc, bic) is { AllowsBoundParameters: true } c) cats.Insert(c);
        var missing = Lotes.Where(d => SharedParameterElement.Lookup(doc, d.Guid) == null).ToList();
        if (missing.Count > 0)
        {
            var original = app.SharedParametersFilename;
            try
            {
                Directory.CreateDirectory(PluginPaths.Root);
                if (!File.Exists(PluginPaths.SharedParameters)) File.WriteAllText(PluginPaths.SharedParameters, string.Empty);
                app.SharedParametersFilename = PluginPaths.SharedParameters;
                var file = app.OpenSharedParameterFile();
                if (file == null) { Log.Info("Não foi possível abrir o arquivo de parâmetros compartilhados."); return; }
                var group = file.Groups.get_Item(GroupName) ?? file.Groups.Create(GroupName);
                foreach (var d in missing)
                {
                    var def = group.Definitions.get_Item(d.Name) as ExternalDefinition;
                    if (def == null || def.GUID != d.Guid)
                        def = (ExternalDefinition)group.Definitions.Create(new ExternalDefinitionCreationOptions(def == null ? d.Name : d.Name + "_", d.Spec)
                        {
                            GUID = d.Guid,
                            Description = d.Description,
                        });
                    doc.ParameterBindings.Insert(def, app.Create.NewInstanceBinding(cats), GroupTypeId.Data);
                }
            }
            catch (Exception ex)
            {
                Log.Error("SharedParameters.EnsureLotes", ex);
            }
            finally
            {
                try { app.SharedParametersFilename = original; } catch { /* sem arquivo original */ }
            }
        }
        // Projetos em que os parâmetros já existiam: garante as três categorias.
        foreach (var d in Lotes)
        {
            try
            {
                if (SharedParameterElement.Lookup(doc, d.Guid) is not { } spe) continue;
                var def = spe.GetDefinition();
                if (doc.ParameterBindings.get_Item(def) is not InstanceBinding b) continue;
                var changed = false;
                foreach (Category c in cats)
                    if (!b.Categories.Contains(c)) { b.Categories.Insert(c); changed = true; }
                if (changed) doc.ParameterBindings.ReInsert(def, b, GroupTypeId.Data);
            }
            catch (Exception ex)
            {
                Log.Error($"SharedParameters.EnsureLotes {d.Name}", ex);
            }
        }
    }

    private const string GroupName = "Sinalizacao Viaria";

    /// <summary>Garante os parâmetros no documento. Deve ser chamado dentro de uma transação.</summary>
    public static void Ensure(Document doc)
    {
        if (All.All(d => SharedParameterElement.Lookup(doc, d.Guid) != null))
        {
            EnsureFloorBinding(doc);
            return;
        }

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
            cats.Insert(Category.GetCategory(doc, BuiltInCategory.OST_Floors));
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

    /// <summary>
    /// Estende os parâmetros SV_* a outra categoria (famílias do usuário classificadas como elementos urbanos:
    /// mobiliário, iluminação, plantio, equipamentos...). Deve ser chamado dentro de uma transação.
    /// </summary>
    public static bool BindTo(Document doc, Category? category)
    {
        if (category == null || !category.AllowsBoundParameters) return false;
        Ensure(doc);
        var ok = true;
        foreach (var d in All)
        {
            try
            {
                if (SharedParameterElement.Lookup(doc, d.Guid) is not { } spe) { ok = false; continue; }
                var def = spe.GetDefinition();
                if (doc.ParameterBindings.get_Item(def) is not InstanceBinding b) { ok = false; continue; }
                if (b.Categories.Contains(category)) continue;
                b.Categories.Insert(category);
                doc.ParameterBindings.ReInsert(def, b, GroupTypeId.Data);
            }
            catch (Exception ex)
            {
                ok = false;
                Log.Error($"SharedParameters.BindTo {category.Name}", ex);
            }
        }
        return ok;
    }

    private static readonly HashSet<int> FloorBound = new();

    /// <summary>Projetos antigos: os parâmetros SV_* passam a valer também para Pisos (elementos físicos como piso).</summary>
    private static void EnsureFloorBinding(Document doc)
    {
        var key = doc.GetHashCode();
        if (!FloorBound.Add(key)) return;
        try
        {
            var floors = Category.GetCategory(doc, BuiltInCategory.OST_Floors);
            if (floors == null) return;
            foreach (var d in All)
            {
                if (SharedParameterElement.Lookup(doc, d.Guid) is not { } spe) continue;
                var def = spe.GetDefinition();
                if (doc.ParameterBindings.get_Item(def) is not InstanceBinding b || b.Categories.Contains(floors)) continue;
                b.Categories.Insert(floors);
                doc.ParameterBindings.ReInsert(def, b, GroupTypeId.Data);
            }
        }
        catch (Exception ex)
        {
            Log.Error("SharedParameters.EnsureFloorBinding", ex);
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
