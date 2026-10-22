using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Model;

/// <summary>Natureza de uma referência entre definições.</summary>
public enum TipoDependencia
{
    /// <summary>Detalhe → marca detalhada (<see cref="IAnnotationDefinition.TargetId"/>).</summary>
    Alvo,
    /// <summary>Interseção, rotatória ou balão → pavimento da via ligada.</summary>
    Via,
    /// <summary>Quadra → loteamento.</summary>
    Loteamento,
    /// <summary>Nó (interseção/rotatória) → sinalização gerada por ele (<c>ChildIds</c>): a filha depende do nó.</summary>
    Filha,
    /// <summary>Trecho de drenagem → dispositivo de montante ou de jusante (PV, boca de lobo, grelha).</summary>
    Rede,
    /// <summary>Marca recortada → marca que fez o recorte (<see cref="ExclusionZone.SourceId"/>). Fraca: não ordena a regeneração.</summary>
    Recorte,
}

/// <summary>
/// Referência gravada em <paramref name="Origem"/> apontando para <paramref name="Destino"/>. Para a ordem de regeneração,
/// o dependente é a origem, exceto em <see cref="TipoDependencia.Filha"/> (o nó guarda a lista, mas é a filha que depende dele).
/// </summary>
public readonly record struct Dependencia(string Origem, string Destino, TipoDependencia Tipo)
{
    public bool Forte => Tipo != TipoDependencia.Recorte;
    public string Dependente => Tipo == TipoDependencia.Filha ? Destino : Origem;
    public string Referenciado => Tipo == TipoDependencia.Filha ? Origem : Destino;
}

/// <summary>
/// Grafo de dependências entre as definições do projeto: referências quebradas, ciclos e ordem de regeneração (o que é
/// referenciado antes de quem depende). Puro (sem Revit): a camada Revit só entrega as definições lidas do documento.
/// </summary>
public sealed class ModelGraph
{
    private readonly Dictionary<string, MarkingDefinition> _nodes = new();
    private readonly List<Dependencia> _edges = new();

    public ModelGraph(IEnumerable<MarkingDefinition> definitions)
    {
        foreach (var d in definitions)
            if (!string.IsNullOrEmpty(d.Id)) _nodes.TryAdd(d.Id, d);
        foreach (var d in _nodes.Values) _edges.AddRange(References(d));
    }

    public IReadOnlyDictionary<string, MarkingDefinition> Nodes => _nodes;
    public IReadOnlyList<Dependencia> Edges => _edges;

    /// <summary>Referências cujo destino não existe no projeto.</summary>
    public IEnumerable<Dependencia> Broken => _edges.Where(e => !_nodes.ContainsKey(e.Destino));

    /// <summary>Referências gravadas numa definição (sem repetir).</summary>
    public static IEnumerable<Dependencia> References(MarkingDefinition d)
    {
        var seen = new HashSet<(string, TipoDependencia)>();
        IEnumerable<Dependencia> One(string? to, TipoDependencia t)
        {
            if (!string.IsNullOrWhiteSpace(to) && to != d.Id && seen.Add((to, t))) yield return new Dependencia(d.Id, to, t);
        }
        var list = new List<Dependencia>();
        if (d is IAnnotationDefinition a) list.AddRange(One(a.TargetId, TipoDependencia.Alvo));
        switch (d)
        {
            case IntersectionDefinition i:
                foreach (var r in i.RoadIds) list.AddRange(One(r, TipoDependencia.Via));
                list.AddRange(One(i.MainRoadId, TipoDependencia.Via));
                foreach (var l in i.LegSettings) list.AddRange(One(l.RoadId, TipoDependencia.Via));
                foreach (var c in i.ChildIds) list.AddRange(One(c, TipoDependencia.Filha));
                break;
            case RoundaboutDefinition rb:
                foreach (var l in rb.Legs) list.AddRange(One(l.RoadId, TipoDependencia.Via));
                foreach (var c in rb.ChildIds) list.AddRange(One(c, TipoDependencia.Filha));
                break;
            case CulDeSacDefinition cs:
                list.AddRange(One(cs.RoadId, TipoDependencia.Via));
                break;
            case DrainagePipeDefinition tp:
                // O trecho é desenhado entre as caixas dos dois dispositivos: refeito depois deles.
                list.AddRange(One(tp.FromId, TipoDependencia.Rede));
                list.AddRange(One(tp.ToId, TipoDependencia.Rede));
                break;
            case QuadraDefinition q:
                list.AddRange(One(q.LoteamentoId, TipoDependencia.Loteamento));
                break;
        }
        foreach (var z in d.Exclusions) list.AddRange(One(z.SourceId, TipoDependencia.Recorte));
        return list;
    }

    /// <summary>
    /// Interseções, rotatórias e balões a refazer quando as marcas <paramref name="changed"/> mudam (eixo, greide ou seção de
    /// uma via): os ligados ao pavimento de uma via do mesmo grupo de alguma marca alterada – a interseção pelas vias
    /// (<c>RoadIds</c>), a rotatória pela via ou pelo grupo de um ramo, o balão pela via. Nessa ordem (interseções, rotatórias,
    /// balões), como a atualização automática do Revit os refaz.
    /// </summary>
    public static List<MarkingDefinition> ConnectionsToRefresh(IEnumerable<MarkingDefinition> changed, IEnumerable<MarkingDefinition> all)
    {
        var groups = changed.Select(d => d.GroupId).Where(g => g != null).Cast<string>().ToHashSet();
        var list = all.ToList();
        var roadIds = list.OfType<RoadPavementDefinition>().Where(p => p.GroupId != null && groups.Contains(p.GroupId)).Select(p => p.Id).ToHashSet();
        var res = new List<MarkingDefinition>();
        res.AddRange(list.OfType<IntersectionDefinition>().Where(i => i.RoadIds.Any(roadIds.Contains)));
        res.AddRange(list.OfType<RoundaboutDefinition>().Where(r => r.Legs.Any(l => l.RoadId != null && roadIds.Contains(l.RoadId)
                                                                                  || l.GroupId != null && groups.Contains(l.GroupId))));
        res.AddRange(list.OfType<CulDeSacDefinition>().Where(c => c.RoadId != null && roadIds.Contains(c.RoadId)));
        return res;
    }

    /// <summary>Quem depende de <paramref name="id"/> (direta ou indiretamente), só pelas referências fortes.</summary>
    public IReadOnlyList<string> Dependents(string id, bool transitive = true)
    {
        var rev = Reverse();
        var res = new List<string>();
        var seen = new HashSet<string> { id };
        var queue = new Queue<string>();
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!rev.TryGetValue(cur, out var deps)) continue;
            foreach (var dep in deps)
                if (seen.Add(dep))
                {
                    res.Add(dep);
                    if (transitive) queue.Enqueue(dep);
                }
        }
        return res;
    }

    /// <summary>
    /// Ciclos entre referências fortes (componentes fortemente conexos com mais de um nó, ou laço em si mesmo – Tarjan,
    /// iterativo). Ids em cada ciclo em ordem estável.
    /// </summary>
    public List<List<string>> Cycles()
    {
        var adj = Forward();
        var index = new Dictionary<string, int>();
        var low = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var stack = new Stack<string>();
        var res = new List<List<string>>();
        var next = 0;
        foreach (var start in _nodes.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (index.ContainsKey(start)) continue;
            var work = new Stack<(string V, IEnumerator<string> It)>();
            Visit(start);
            while (work.Count > 0)
            {
                var (v, it) = work.Peek();
                if (it.MoveNext())
                {
                    var w = it.Current;
                    if (!index.ContainsKey(w)) Visit(w);
                    else if (onStack.Contains(w)) low[v] = Math.Min(low[v], index[w]);
                    continue;
                }
                work.Pop();
                if (work.Count > 0) { var p = work.Peek().V; low[p] = Math.Min(low[p], low[v]); }
                if (low[v] != index[v]) continue;
                var comp = new List<string>();
                string x;
                do { x = stack.Pop(); onStack.Remove(x); comp.Add(x); } while (x != v);
                if (comp.Count > 1 || (adj.TryGetValue(v, out var self) && self.Contains(v)))
                    res.Add(comp.OrderBy(k => k, StringComparer.Ordinal).ToList());
            }

            void Visit(string v)
            {
                index[v] = low[v] = next++;
                stack.Push(v);
                onStack.Add(v);
                var succ = adj.TryGetValue(v, out var s) ? s.Where(_nodes.ContainsKey).OrderBy(k => k, StringComparer.Ordinal).ToList() : new List<string>();
                work.Push((v, succ.GetEnumerator()));
            }
        }
        return res;
    }

    /// <summary>
    /// Ordem de regeneração (Kahn): cada definição depois das que ela referencia. As que estão em ciclo ficam de fora e vão
    /// para <paramref name="inCycle"/>.
    /// </summary>
    public List<string> RegenerationOrder(out List<string> inCycle)
    {
        var adj = Forward();
        var indeg = _nodes.Keys.ToDictionary(k => k, _ => 0);
        var rev = Reverse();
        foreach (var (v, succ) in adj)
            if (indeg.ContainsKey(v)) indeg[v] = succ.Count(_nodes.ContainsKey);
        var ready = new SortedSet<string>(indeg.Where(p => p.Value == 0).Select(p => p.Key), StringComparer.Ordinal);
        var order = new List<string>();
        while (ready.Count > 0)
        {
            var v = ready.Min!;
            ready.Remove(v);
            order.Add(v);
            if (!rev.TryGetValue(v, out var deps)) continue;
            foreach (var d in deps)
                if (indeg.ContainsKey(d) && --indeg[d] == 0) ready.Add(d);
        }
        var placed = order.ToHashSet();
        inCycle = _nodes.Keys.Where(k => !placed.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        return order;
    }

    /// <summary>
    /// Ordem para gerar um conjunto de definições (criação, atualização, dependentes): primeiro as marcas, depois os detalhes
    /// e por fim as anotações do projeto todo, como sempre foi; dentro de cada grupo, cada definição depois das do próprio
    /// conjunto que ela referencia (referências fortes – nó antes das filhas, marca antes do detalhe do detalhe); empate pela
    /// ordem de entrada. Sem dependências dentro de um grupo o resultado é o de antes (D-004). Num ciclo, a ordem de entrada
    /// decide.
    /// </summary>
    public static List<MarkingDefinition> RegenerationSort(IEnumerable<MarkingDefinition> definitions)
    {
        var list = definitions.ToList();
        var byId = new Dictionary<string, List<int>>();
        for (int i = 0; i < list.Count; i++)
            if (!string.IsNullOrEmpty(list[i].Id))
            {
                if (!byId.TryGetValue(list[i].Id, out var l)) byId[list[i].Id] = l = new List<int>();
                l.Add(i);
            }
        // Referências de cada item a outros itens do conjunto (dependente → referenciados).
        var refs = new List<int>[list.Count];
        for (int i = 0; i < list.Count; i++) refs[i] = new List<int>();
        foreach (var d in list)
            foreach (var e in References(d).Where(e => e.Forte))
                if (byId.TryGetValue(e.Dependente, out var deps) && byId.TryGetValue(e.Referenciado, out var targets))
                    foreach (var a in deps)
                        foreach (var b in targets)
                            if (a != b && !refs[a].Contains(b)) refs[a].Add(b);
        var depth = new int[list.Count];
        var state = new byte[list.Count];   // 0 = não visto, 1 = na pilha, 2 = pronto
        int Depth(int i)
        {
            if (state[i] == 2) return depth[i];
            if (state[i] == 1) return 0;   // ciclo: não aprofunda
            state[i] = 1;
            var dd = 0;
            foreach (var j in refs[i]) dd = Math.Max(dd, Depth(j) + 1);
            state[i] = 2;
            return depth[i] = dd;
        }
        for (int i = 0; i < list.Count; i++) Depth(i);
        static int Group(MarkingDefinition d) => d is IProjectWideAnnotation ? 2 : d is IAnnotationDefinition ? 1 : 0;
        return Enumerable.Range(0, list.Count).OrderBy(i => Group(list[i])).ThenBy(i => depth[i]).ThenBy(i => i).Select(i => list[i]).ToList();
    }

    /// <summary>Dependente → conjunto do que ele referencia (só fortes, destinos existentes ou não).</summary>
    private Dictionary<string, HashSet<string>> Forward()
    {
        var adj = new Dictionary<string, HashSet<string>>();
        foreach (var e in _edges.Where(e => e.Forte))
        {
            if (!adj.TryGetValue(e.Dependente, out var s)) adj[e.Dependente] = s = new HashSet<string>();
            s.Add(e.Referenciado);
        }
        return adj;
    }

    /// <summary>Referenciado → dependentes (só fortes).</summary>
    private Dictionary<string, HashSet<string>> Reverse()
    {
        var rev = new Dictionary<string, HashSet<string>>();
        foreach (var e in _edges.Where(e => e.Forte))
        {
            if (!rev.TryGetValue(e.Referenciado, out var s)) rev[e.Referenciado] = s = new HashSet<string>();
            s.Add(e.Dependente);
        }
        return rev;
    }
}
