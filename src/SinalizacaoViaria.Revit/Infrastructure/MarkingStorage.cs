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
        // Dados de uma versão mais nova do plugin: regravar perderia os campos que esta versão não conhece.
        if (DefinitionVersioning.IsNewer(def)) throw new InvalidOperationException(DefinitionVersioning.NewerMessage(def));
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
        string id;
        try { id = entity.Get<string>(FId); }
        catch (Exception ex)
        {
            Log.Error($"Leitura de definição do elemento {e.Id}", ex);
            return null;
        }
        return Read(e, entity, id);
    }

    private static StoredMarking? Read(Element e, Entity entity, string id, string? json = null)
    {
        Stats.Parses++;
        try
        {
            var lida = DefinitionVersioning.Load(json ?? entity.Get<string>(FDef));
            if (lida.Definition == null)
            {
                Log.Error($"Leitura de definição do elemento {e.Id}", new InvalidDataException(lida.Erro));
                return null;
            }
            if (lida.Estado == EstadoVersao.MaisNova && Newer.Add(id))
                Log.Info($"Elemento {e.Id}: definição v{lida.StoredVersion} (versão mais nova do plugin) – somente leitura.");
            MarkingColors.TryParse(entity.Get<string>(FColor), out var color);
            return new StoredMarking(e, id, color, lida.Definition);
        }
        catch (Exception ex)
        {
            Log.Error($"Leitura de definição do elemento {e.Id}", ex);
            return null;
        }
    }

    /// <summary>Marcas de versão mais nova já avisadas no log (uma linha por marca na sessão).</summary>
    private static readonly HashSet<string> Newer = new();

    /// <summary>JSON gravado em cada elemento, sem interpretar – para a validação do modelo (cópias divergentes, dados ilegíveis).</summary>
    public static List<CopiaGravada> Copies(Document doc) => Copies(doc, out _);

    /// <inheritdoc cref="Copies(Document)"/>
    /// <param name="elements">Elementos de cada marca (para selecionar os envolvidos num achado).</param>
    public static List<CopiaGravada> Copies(Document doc, out Dictionary<string, List<ElementId>> elements)
    {
        var res = new List<CopiaGravada>();
        elements = new Dictionary<string, List<ElementId>>();
        var schema = Schema.Lookup(SchemaGuid);
        if (schema == null) return res;
        foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(new ExtensibleStorageFilter(SchemaGuid)))
        {
            try
            {
                var entity = e.GetEntity(schema);
                if (entity == null || !entity.IsValid()) continue;
                var id = entity.Get<string>(FId) ?? "";
                res.Add(new CopiaGravada(id, entity.Get<string>(FDef) ?? "", $"{e.Id} ({e.Name})"));
                if (!elements.TryGetValue(id, out var list)) elements[id] = list = new List<ElementId>();
                list.Add(e.Id);
            }
            catch (Exception ex)
            {
                Log.Error($"Validação – leitura do elemento {e.Id}", ex);
            }
        }
        return res;
    }

    public static bool IsMarking(Element e) => Read(e) != null;

    /// <summary>
    /// Elementos com o esquema e o id da marca gravado, sem interpretar o JSON. Ler o id é barato; interpretar a definição de
    /// cada elemento a cada consulta deixava o projeto quadrático (o Autoteste completo passava de 1 h e meia): cada geração
    /// chama ById, e o atualizador e as interseções chamam Definitions várias vezes por transação.
    /// </summary>
    private static IEnumerable<(Element Element, Entity Entity, string Id)> Entries(Document doc)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema == null) yield break;
        Stats.Scans++;
        // Inclui o JSON interpretado por quem percorre (Scan, ById, All): é o custo total de ler as marcas.
        using var perf = Perf.Measure("Ler marcas do projeto (varredura + JSON)");
        foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(new ExtensibleStorageFilter(SchemaGuid)))
        {
            Entity entity;
            string? id;
            try
            {
                entity = e.GetEntity(schema);
                if (entity == null || !entity.IsValid()) continue;
                id = entity.Get<string>(FId);
            }
            catch (Exception ex)
            {
                Log.Error($"Leitura do id da marca no elemento {e.Id}", ex);
                continue;
            }
            Stats.Elements++;
            yield return (e, entity, id ?? "");
        }
    }

    /// <summary>Todos os elementos de sinalização do documento.</summary>
    public static List<StoredMarking> All(Document doc)
    {
        var res = new List<StoredMarking>();
        foreach (var (e, entity, id) in Entries(doc))
        {
            var r = Read(e, entity, id);
            if (r != null) res.Add(r);
        }
        return res;
    }

    /// <summary>Ids das marcas presentes no documento (sem interpretar as definições).</summary>
    public static HashSet<string> Ids(Document doc) => Entries(doc).Select(x => x.Id).ToHashSet();

    /// <summary>Elementos de cada marca com a cor gravada, sem interpretar as definições (o que existe no modelo).</summary>
    public static Dictionary<string, List<(Element Element, MarkingColor Color)>> Elements(Document doc)
    {
        var res = new Dictionary<string, List<(Element, MarkingColor)>>();
        foreach (var (e, entity, id) in Entries(doc))
        {
            MarkingColor color;
            try { MarkingColors.TryParse(entity.Get<string>(FColor), out color); }
            catch { continue; }
            if (!res.TryGetValue(id, out var list)) res[id] = list = new List<(Element, MarkingColor)>();
            list.Add((e, color));
        }
        return res;
    }

    /// <summary>
    /// Uma definição por marca (conjuntos com várias cores aparecem uma única vez): só o primeiro elemento legível de cada marca é
    /// interpretado – os demais gravam o mesmo JSON.
    /// </summary>
    public static List<MarkingDefinition> Definitions(Document doc) => Scan(doc, null);

    /// <summary>
    /// Definições de um tipo (ou interface): o tipo é lido do começo do JSON gravado (<see cref="DefinitionKinds"/>) e só as
    /// marcas desse tipo são interpretadas. Mesmo resultado de <c>Definitions(doc).OfType&lt;T&gt;()</c>.
    /// </summary>
    public static List<T> Definitions<T>(Document doc) where T : class =>
        Scan(doc, DefinitionKinds.MayBe<T>).OfType<T>().ToList();

    /// <summary>Definições de qualquer um dos tipos indicados (classes ou interfaces), lidas como em <see cref="Definitions{T}"/>.</summary>
    public static List<MarkingDefinition> Definitions(Document doc, params Type[] kinds) =>
        Scan(doc, json => DefinitionKinds.TypeOf(json) is not { } t || kinds.Any(k => k.IsAssignableFrom(t)))
            .Where(d => kinds.Any(k => k.IsInstanceOfType(d))).ToList();

    /// <summary>
    /// Marcas do conjunto <paramref name="groupId"/>: só o JSON que contém o id do conjunto é interpretado. Mesmo resultado de
    /// <c>Definitions(doc).Where(d =&gt; d.GroupId == groupId)</c>.
    /// </summary>
    public static List<MarkingDefinition> InGroup(Document doc, string? groupId) => groupId == null
        ? Definitions(doc).Where(d => d.GroupId == null).ToList()
        : Mentioning(doc, groupId).Where(d => d.GroupId == groupId).ToList();

    /// <summary>
    /// Marcas cujo JSON gravado cita algum dos textos (ids de conjunto, de marca ou de elemento) – as demais nem são
    /// interpretadas. Quem chama confere o critério exato: o texto pode aparecer em outro campo. Com muitos textos (o
    /// atualizador com centenas de elementos alterados) procurar cada um custaria mais que interpretar: lê todas.
    /// </summary>
    public static List<MarkingDefinition> Mentioning(Document doc, params string?[] texts)
    {
        var keys = texts.Where(t => !string.IsNullOrEmpty(t)).Cast<string>().Distinct().ToArray();
        if (keys.Length == 0) return new List<MarkingDefinition>();
        if (keys.Length > 32) return Scan(doc, null);
        return Scan(doc, json => keys.Any(k => json.Contains(k, StringComparison.Ordinal)));
    }

    /// <summary>Definição de uma marca (a do primeiro elemento legível dela), sem interpretar as demais.</summary>
    public static MarkingDefinition? Definition(Document doc, string markingId)
    {
        foreach (var (e, entity, id) in Entries(doc))
            if (id == markingId && Read(e, entity, id) is { } r)
                return r.Definition;
        return null;
    }

    /// <param name="keep">
    /// Filtro barato sobre o JSON gravado, antes de interpretá-lo; falso descarta a marca (os elementos de uma marca gravam o
    /// mesmo JSON). Só pode descartar o que a interpretação também descartaria.
    /// </param>
    private static List<MarkingDefinition> Scan(Document doc, Func<string, bool>? keep)
    {
        var res = new List<MarkingDefinition>();
        var done = new HashSet<string>();
        foreach (var (e, entity, id) in Entries(doc))
        {
            if (done.Contains(id)) continue;
            string? json = null;
            if (keep != null)
            {
                try { json = entity.Get<string>(FDef); }
                catch { continue; }
                if (json != null && !keep(json)) { done.Add(id); continue; }
            }
            var r = Read(e, entity, id, json);
            if (r == null) continue; // ilegível: tenta o próximo elemento da mesma marca
            done.Add(id);
            res.Add(r.Definition);
        }
        return res;
    }

    public static List<StoredMarking> ById(Document doc, string markingId)
    {
        var res = new List<StoredMarking>();
        foreach (var (e, entity, id) in Entries(doc))
        {
            if (id != markingId) continue;
            var r = Read(e, entity, id);
            if (r != null) res.Add(r);
        }
        return res;
    }

    /// <summary>Contadores de leitura (Autoteste: quanto do tempo de cada etapa vai para varrer e interpretar as marcas).</summary>
    public static class Stats
    {
        public static long Scans, Elements, Parses;
        public static (long Scans, long Elements, long Parses) Snapshot() => (Scans, Elements, Parses);
    }
}
