using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Model;

public enum Severidade { Erro, Aviso, Info }

/// <summary>Quanto o achado é certo: Alta = verificado nos dados; Média = provável; Baixa = indício.</summary>
public enum Confianca { Alta, Media, Baixa }

/// <summary>
/// Um problema encontrado no modelo. <paramref name="RevisaoHumana"/>: o plugin não corrige sozinho – uma pessoa decide
/// (apagar, refazer, aceitar).
/// </summary>
public sealed record Achado(string Codigo, Severidade Severidade, Confianca Confianca, bool RevisaoHumana, string Mensagem,
    IReadOnlyList<string> Ids);

/// <summary>Cópia da definição gravada num elemento (o mesmo Id em vários elementos: um por cor/peça).</summary>
public sealed record CopiaGravada(string MarkingId, string Json, string Elemento);

/// <summary>
/// Validação do modelo do plugin, sem alterar nada: lê as cópias gravadas nos elementos e aponta dados ilegíveis, versões
/// mais novas, cópias divergentes, referências quebradas, ciclos de dependência, elementos do Revit ausentes e contornos
/// inválidos. Cada verificação tem código, severidade, confiança e se pede revisão humana (ver TESTS.md / AUDIT.md).
/// </summary>
public static class ModelValidator
{
    /// <param name="copias">Todas as cópias lidas do documento.</param>
    /// <param name="elementoExiste">UniqueId (dono da borda) → existe no documento. Nulo = não verificar elementos do Revit.</param>
    public static List<Achado> Validate(IReadOnlyList<CopiaGravada> copias, Func<string, bool>? elementoExiste = null)
    {
        var res = new List<Achado>();
        var defs = new Dictionary<string, MarkingDefinition>();
        foreach (var g in copias.GroupBy(c => c.MarkingId))
        {
            var lidas = g.Select(c => (Copia: c, Lida: DefinitionVersioning.Load(c.Json))).ToList();
            foreach (var bad in lidas.Where(x => x.Lida.Estado == EstadoVersao.Invalida))
                res.Add(new("V01", Severidade.Erro, Confianca.Alta, true,
                    $"Dados ilegíveis no elemento {bad.Copia.Elemento}: {bad.Lida.Erro}. A marca não pode ser editada nem atualizada.",
                    new[] { g.Key }));
            var ok = lidas.Where(x => x.Lida.Definition != null).ToList();
            if (ok.Count == 0) continue;
            var def = ok[0].Lida.Definition!;
            defs[g.Key] = def;
            if (ok.Any(x => x.Lida.Estado == EstadoVersao.MaisNova))
                res.Add(new("V02", Severidade.Aviso, Confianca.Alta, false, DefinitionVersioning.NewerMessage(def), new[] { g.Key }));
            // Normaliza pelo próprio serializador (ordem e formato) antes de comparar.
            var distintas = ok.Select(x => x.Lida.Definition!.ToJson()).Distinct().Count();
            if (distintas > 1)
                res.Add(new("V03", Severidade.Aviso, Confianca.Media, true,
                    $"{def.DisplayCode}: {ok.Count} elementos com {distintas} versões diferentes da definição – a edição usa a primeira. " +
                    "Use Atualizar na marca para regravar todas iguais.", new[] { g.Key }));
        }

        var graph = new ModelGraph(defs.Values);
        foreach (var e in graph.Broken)
        {
            var o = defs[e.Origem];
            var (sev, msg) = e.Tipo switch
            {
                TipoDependencia.Alvo => (Severidade.Aviso, $"{o.DisplayCode}: a marca detalhada não existe mais (detalhe órfão)."),
                TipoDependencia.Via => (Severidade.Aviso, $"{o.DisplayCode}: aponta para uma via que não existe mais."),
                TipoDependencia.Loteamento => (Severidade.Aviso, $"{o.DisplayCode}: o loteamento da quadra não existe mais (usa as categorias padrão)."),
                TipoDependencia.Filha => (Severidade.Info, $"{o.DisplayCode}: sinalização gerada pelo nó foi apagada (volta ao atualizar o nó)."),
                _ => (Severidade.Info, $"{o.DisplayCode}: recorte deixado por uma marca que não existe mais (sai na próxima atualização)."),
            };
            res.Add(new("V04", sev, Confianca.Alta, e.Tipo is TipoDependencia.Alvo or TipoDependencia.Via or TipoDependencia.Loteamento, msg,
                new[] { e.Origem, e.Destino }));
        }
        foreach (var c in graph.Cycles())
            res.Add(new("V05", Severidade.Erro, Confianca.Alta, true,
                $"Dependência circular entre {c.Count} marca(s): {string.Join(" → ", c.Select(id => defs[id].DisplayCode))}. " +
                "A ordem de atualização fica indefinida.", c));

        foreach (var d in defs.Values)
        {
            if (elementoExiste != null) CheckElements(d, elementoExiste, res);
            CheckShapes(d, res);
        }

        // Corte só de dados (sem desenho na planta) sem o perfil que o usa.
        var perfis = defs.Values.OfType<SectionProfileDefinition>().Select(p => p.SectionId).ToHashSet();
        foreach (var s in defs.Values.OfType<SectionDimensionDefinition>().Where(s => DetailGenerator.SectionDataOnly(s) && !perfis.Contains(s.Id)))
            res.Add(new("V08", Severidade.Info, Confianca.Alta, false,
                $"{s.DisplayCode}: linha de corte sem o perfil transversal (sai na próxima atualização).", new[] { s.Id }));

        return res.OrderBy(a => a.Severidade).ThenBy(a => a.Codigo, StringComparer.Ordinal).ToList();
    }

    /// <summary>O que cada código verifica (relatório e manual).</summary>
    public static readonly IReadOnlyDictionary<string, string> Codigos = new Dictionary<string, string>
    {
        ["V01"] = "dados da definição ilegíveis (JSON inválido ou tipo desconhecido)",
        ["V02"] = "definição gravada por versão mais nova do plugin (somente leitura)",
        ["V03"] = "elementos da mesma marca com definições diferentes",
        ["V04"] = "referência a marca inexistente (detalhe, via, loteamento, sinalização filha, recorte)",
        ["V05"] = "dependência circular entre marcas",
        ["V06"] = "linha ou elemento do Revit referenciado foi apagado",
        ["V07"] = "contorno inválido (poucos vértices, área nula, lados que se cruzam)",
        ["V08"] = "linha de corte sem o perfil transversal",
    };

    /// <summary>Relatório em texto (o mesmo que a camada Revit grava em TXT).</summary>
    public static string Report(IReadOnlyList<Achado> achados, int marcas, int elementos, string projeto, DateTime quando)
    {
        static string S(Severidade s) => s switch { Severidade.Erro => "ERRO", Severidade.Aviso => "AVISO", _ => "INFO" };
        static string C(Confianca c) => c switch { Confianca.Alta => "alta", Confianca.Media => "média", _ => "baixa" };
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"VALIDAÇÃO DO MODELO – {projeto}");
        sb.AppendLine($"{quando:dd/MM/yyyy HH:mm} · {marcas} marca(s) em {elementos} elemento(s) · nada foi alterado no projeto.");
        sb.AppendLine($"Erros: {achados.Count(a => a.Severidade == Severidade.Erro)} · Avisos: {achados.Count(a => a.Severidade == Severidade.Aviso)} · " +
                      $"Informações: {achados.Count(a => a.Severidade == Severidade.Info)} · Pedem revisão humana: {achados.Count(a => a.RevisaoHumana)}");
        sb.AppendLine();
        if (achados.Count == 0) sb.AppendLine("Nenhum problema encontrado nas verificações abaixo.");
        foreach (var a in achados)
        {
            sb.AppendLine($"[{S(a.Severidade)}] {a.Codigo} (confiança {C(a.Confianca)}{(a.RevisaoHumana ? ", revisão humana" : "")}) {a.Mensagem}");
            sb.AppendLine($"        Id: {string.Join(", ", a.Ids)}");
        }
        sb.AppendLine();
        sb.AppendLine("Verificações:");
        foreach (var (k, v) in Codigos) sb.AppendLine($"  {k} – {v}");
        sb.AppendLine("Não verificado aqui: geometria gerada no Revit (use o Autoteste), normas de projeto e aprovação legal.");
        return sb.ToString();
    }

    private static void CheckElements(MarkingDefinition d, Func<string, bool> exists, List<Achado> res)
    {
        if (d.Path is { } p && p.ElementIds.Count > 0)
        {
            var missing = p.ElementIds.Where(id => !exists(PathReference.OwnerOf(id))).ToList();
            if (missing.Count > 0)
            {
                var cached = p.Cache != null && p.Cache.Count == p.ElementIds.Count;
                res.Add(cached || p.Points.Count >= 2
                    ? new("V06", Severidade.Info, Confianca.Alta, false,
                        $"{d.DisplayCode}: {missing.Count} linha(s) de referência apagada(s) – a marca usa o traçado guardado e não acompanha mais a linha.",
                        new[] { d.Id })
                    : new("V06", Severidade.Aviso, Confianca.Alta, true,
                        $"{d.DisplayCode}: {missing.Count} linha(s) de referência apagada(s) e sem traçado guardado – a marca pode sumir ao atualizar.",
                        new[] { d.Id }));
            }
        }
        IEnumerable<string> extra = d switch
        {
            QuadraDefinition q => q.LimiteElementos.Concat(q.Divisao == ModoDivisao.Linhas ? q.LinhasLotes.Select(l => l.Elemento) : Enumerable.Empty<string>()),
            LoteamentoDefinition l => l.GlebaElementos,
            _ => Enumerable.Empty<string>(),
        };
        var faltam = extra.Where(id => !string.IsNullOrWhiteSpace(id) && !exists(PathReference.OwnerOf(id))).Distinct().Count();
        if (faltam > 0)
            res.Add(new("V06", Severidade.Aviso, Confianca.Alta, true,
                $"{d.DisplayCode}: {faltam} linha(s) do limite/divisão apagada(s) – o contorno guardado continua valendo até ser redesenhado.",
                new[] { d.Id }));
    }

    private static void CheckShapes(MarkingDefinition d, List<Achado> res)
    {
        void Ring(IReadOnlyList<Vec2> ring, string what)
        {
            if (ring.Count == 0) return;
            if (PolygonChecks.Problem(ring) is { } problem)
                res.Add(new("V07", Severidade.Erro, Confianca.Alta, true, $"{d.DisplayCode}: {what} inválido – {problem}.", new[] { d.Id }));
        }
        switch (d)
        {
            case QuadraDefinition q:
                Ring(q.Contorno, "contorno da quadra");
                break;
            case LoteamentoDefinition l:
                Ring(l.Gleba, "contorno da gleba");
                for (var i = 0; i < l.Remanescentes.Count; i++) Ring(l.Remanescentes[i].Contorno, $"área remanescente {i + 1}");
                break;
        }
    }
}
