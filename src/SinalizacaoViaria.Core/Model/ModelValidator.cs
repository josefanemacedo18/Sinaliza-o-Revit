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
/// mais novas, cópias divergentes, referências quebradas, ciclos de dependência, elementos do Revit ausentes, contornos
/// inválidos e a compatibilização do parcelamento e das interseções (quadras sobrepostas ou fora da gleba, nó sem duas vias). Cada verificação tem código, severidade, confiança e se pede revisão humana (ver TESTS.md / AUDIT.md).
/// </summary>
public static class ModelValidator
{
    /// <param name="copias">Todas as cópias lidas do documento.</param>
    /// <param name="elementoExiste">UniqueId (dono da borda) → existe no documento. Nulo = não verificar elementos do Revit.</param>
    /// <param name="criterios">Limites geométricos informados pelo usuário (V12). Nulo ou vazio = não conferir.</param>
    /// <param name="drenagem">Projeto de drenagem (V13): critérios e, com a equação de chuva, a vazão. Nulo = só a geometria (sentido, ligação, ciclo).</param>
    public static List<Achado> Validate(IReadOnlyList<CopiaGravada> copias, Func<string, bool>? elementoExiste = null,
        GeometricCriteria? criterios = null, Automation.ProjetoDrenagem? drenagem = null)
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
                TipoDependencia.Rede => (Severidade.Aviso, $"{o.DisplayCode}: o dispositivo de drenagem de uma ponta do trecho não existe mais (o tubo não é desenhado)."),
                _ => (Severidade.Info, $"{o.DisplayCode}: recorte deixado por uma marca que não existe mais (sai na próxima atualização)."),
            };
            res.Add(new("V04", sev, Confianca.Alta, e.Tipo is TipoDependencia.Alvo or TipoDependencia.Via or TipoDependencia.Loteamento or TipoDependencia.Rede, msg,
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

        CheckLayout(defs, res);
        if (criterios is { IsEmpty: false }) CheckAlignment(defs, criterios, res);
        CheckDrainage(defs, drenagem, res);

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
        ["V09"] = "quadras do mesmo loteamento sobrepostas",
        ["V10"] = "quadra fora da gleba do loteamento",
        ["V11"] = "interseção com menos de duas vias existentes",
        ["V12"] = "eixo ou greide de via fora dos limites geométricos informados pelo usuário (raio, tangente, rampa, K)",
        ["V13"] = "rede de drenagem: contrafluxo, trecho ligado ao próprio dispositivo, ciclo ou fora dos critérios do Projeto de Drenagem (declividade, recobrimento, diâmetro, PVs, capacidade, velocidade, sarjeta, engolimento)",
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
        // Limite e linhas dos lotes das quadras por linhas são relidos a cada atualização: apagá-los muda o resultado.
        if (d is QuadraDefinition q)
        {
            var faltam = q.LimiteElementos.Concat(q.Divisao == ModoDivisao.Linhas ? q.LinhasLotes.Select(x => x.Elemento) : Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id) && !exists(PathReference.OwnerOf(id))).Distinct().Count();
            if (faltam > 0)
                res.Add(new("V06", Severidade.Aviso, Confianca.Alta, true,
                    $"{d.DisplayCode}: {faltam} linha(s) do limite/divisão apagada(s) – o contorno guardado continua valendo até ser redesenhado.",
                    new[] { d.Id }));
        }
        // As linhas da gleba só servem para criar o loteamento: depois vale o contorno guardado (nada é relido delas).
        if (d is LoteamentoDefinition lot)
        {
            var faltam = lot.GlebaElementos.Where(id => !string.IsNullOrWhiteSpace(id) && !exists(PathReference.OwnerOf(id))).Distinct().Count();
            if (faltam > 0)
                res.Add(new("V06", Severidade.Info, Confianca.Alta, false,
                    $"{d.DisplayCode}: {faltam} linha(s) usadas para criar a gleba foram apagadas – sem efeito (o loteamento usa o contorno guardado).",
                    new[] { d.Id }));
        }
    }

    /// <summary>Área (m²) abaixo da qual sobreposições e excessos são ruído numérico de contornos que só se encostam.</summary>
    public const double AreaTolerance = 0.5;

    /// <summary>Compatibilização do parcelamento e das interseções (V09–V11).</summary>
    private static void CheckLayout(Dictionary<string, MarkingDefinition> defs, List<Achado> res)
    {
        static Polygon2? Valid(IReadOnlyList<Vec2> ring) => PolygonChecks.Problem(ring) == null ? new Polygon2(ring) : null;
        foreach (var lot in defs.Values.OfType<LoteamentoDefinition>())
        {
            var quadras = defs.Values.OfType<QuadraDefinition>().Where(q => q.LoteamentoId == lot.Id)
                .Select(q => (Q: q, P: Valid(q.Contorno))).Where(x => x.P != null).OrderBy(x => x.Q.Nome, StringComparer.Ordinal).ToList();
            for (var i = 0; i < quadras.Count; i++)
                for (var j = i + 1; j < quadras.Count; j++)
                {
                    var a = quadras[i];
                    var b = quadras[j];
                    if (!BoundsOverlap(a.P!, b.P!)) continue;
                    var area = PolygonOps.TotalArea(PolygonOps.Intersect(new[] { a.P! }, new[] { b.P! }));
                    if (area > AreaTolerance)
                        res.Add(new("V09", Severidade.Erro, Confianca.Alta, true,
                            $"Quadras {a.Q.Nome} e {b.Q.Nome} se sobrepõem em {area:0.##} m² – os lotes e o quadro de áreas contam essa área duas vezes.",
                            new[] { a.Q.Id, b.Q.Id }));
                }
            if (Valid(lot.Gleba) is not { } gleba) continue;
            foreach (var (q, p) in quadras)
            {
                var fora = PolygonOps.TotalArea(PolygonOps.Difference(new[] { p! }, new[] { gleba }));
                if (fora > AreaTolerance)
                    res.Add(new("V10", Severidade.Aviso, Confianca.Media, true,
                        $"Quadra {q.Nome}: {fora:0.##} m² fora da gleba de {lot.Nome} (contorno da quadra ou da gleba desatualizado?).",
                        new[] { q.Id, lot.Id }));
            }
        }
        foreach (var n in defs.Values.OfType<IntersectionDefinition>())
        {
            // Sem Distinct: uma via que passa duas vezes pelo nó (laço) conta duas vezes, como na geração.
            var vias = n.RoadIds.Count(id => !string.IsNullOrWhiteSpace(id) && defs.ContainsKey(id));
            if (vias < 2)
                res.Add(new("V11", Severidade.Aviso, Confianca.Alta, true,
                    $"{n.DisplayCode}: só {vias} via(s) existente(s) no cruzamento – a interseção não liga mais duas vias (apagar ou refazer a conexão).",
                    new[] { n.Id }));
        }
    }

    /// <summary>
    /// Eixo (pelo traçado guardado na via) e greide de cada pavimento contra os limites que o usuário informou (V12, K8). O
    /// eixo vem do cache do caminho: se a linha de referência mudou depois da última geração, a via precisa ser atualizada
    /// (confiança média).
    /// </summary>
    private static void CheckAlignment(Dictionary<string, MarkingDefinition> defs, GeometricCriteria c, List<Achado> res)
    {
        foreach (var road in defs.Values.OfType<RoadPavementDefinition>())
        {
            var v = new List<Violacao>();
            if (StoredAxis(road.PathRef) is { } axis) v.AddRange(GeometricCheck.Axis(AlignmentElements.FromAxis(axis), c));
            if (road.Output.Grade is { } grade && grade.SortedPoints().Count >= 2) v.AddRange(GeometricCheck.Vertical(AlignmentElements.Vertical(grade), c));
            if (v.Count == 0) continue;
            var lista = string.Join("; ", v.Take(4).Select(x => x.Texto)) + (v.Count > 4 ? $"; … e mais {v.Count - 4}" : "");
            res.Add(new("V12", Severidade.Aviso, Confianca.Media, true,
                $"{road.DisplayCode}: {v.Count} elemento(s) fora dos limites informados ({c.FonteTexto}): {lista}.", new[] { road.Id }));
        }
    }

    /// <summary>
    /// Rede de drenagem (V13, F8): sentido do escoamento, ligações e ciclos sempre; os critérios do Projeto de Drenagem e, com
    /// a equação de chuva, a vazão (capacidade e velocidade das galerias, sarjetas e engolimento). O dispositivo inexistente já
    /// sai no V04. O recobrimento é medido sob a reta entre as tampas dos dois dispositivos (o terreno entre eles não é lido –
    /// confiança média), como a sarjeta, que depende da área de contribuição informada.
    /// </summary>
    private static void CheckDrainage(Dictionary<string, MarkingDefinition> defs, Automation.ProjetoDrenagem? c, List<Achado> res)
    {
        if (!defs.Values.Any(d => d is DrainagePipeDefinition)) return;
        var problemas = Automation.DrainageNetwork.Check(defs.Values, c).Where(p => p.Criterio != "dispositivo").ToList();
        if (c is { TemChuva: true }) problemas.AddRange(Automation.DrainageHydraulics.Calcular(defs.Values, c).Problemas);
        foreach (var p in problemas)
        {
            var texto = p.PorLimite ? $"{p.Texto} Critérios: {c!.FonteTexto}." : p.Texto;
            var media = p.Criterio is "cobrimento" or "sarjeta" or "engolimento";
            res.Add(new("V13", Severidade.Aviso, media ? Confianca.Media : Confianca.Alta, true, texto, p.Ids ?? Array.Empty<string>()));
        }
    }

    /// <summary>Eixo guardado na definição: pontos fixos ou o traçado das linhas na última geração, com os cantos arredondados.</summary>
    private static Polyline2? StoredAxis(PathReference p)
    {
        var pts = p.Points.Count >= 2 ? p.Points
            : p.Cache is { Count: > 0 } c ? c.SelectMany((q, k) => k == 0 ? q : q.Skip(1)).ToList() : null;
        if (pts == null || pts.Count < 2) return null;
        return new Polyline2(PathReference.Smooth(pts, p.SmoothRadius, p.Closed).ToList(), p.Closed);
    }

    private static bool BoundsOverlap(Polygon2 a, Polygon2 b)
    {
        var (a0, a1) = a.Bounds;
        var (b0, b1) = b.Bounds;
        return a0.X <= b1.X && b0.X <= a1.X && a0.Y <= b1.Y && b0.Y <= a1.Y;
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
