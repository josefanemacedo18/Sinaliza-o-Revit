using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Model;

/// <summary>
/// Gravidade do achado. <see cref="NaoVerificavel"/>: a verificação não pôde rodar (falta dado ou configuração) – não é
/// "sem problema" (Prompt Master §17).
/// </summary>
public enum Severidade { Erro, Aviso, Info, NaoVerificavel }

/// <summary>Quanto o achado é certo: Alta = verificado nos dados; Média = provável; Baixa = indício.</summary>
public enum Confianca { Alta, Media, Baixa }

/// <summary>
/// Um problema encontrado no modelo. <paramref name="RevisaoHumana"/>: o plugin não corrige sozinho – uma pessoa decide
/// (apagar, refazer, aceitar). <paramref name="Acao"/>: ação recomendada própria deste achado; nula = a da regra
/// (<see cref="ModelValidator.Regras"/>).
/// </summary>
public sealed record Achado(string Codigo, Severidade Severidade, Confianca Confianca, bool RevisaoHumana, string Mensagem,
    IReadOnlyList<string> Ids, string? Acao = null)
{
    /// <summary>Módulo da regra (Dados e versões, Dependências, Geometria, Vias, Interseções, Quadras e lotes, Drenagem…).</summary>
    public string Modulo => ModelValidator.Regras.TryGetValue(Codigo, out var r) ? r.Modulo : "Outros";

    /// <summary>O que fazer: a ação própria do achado ou, sem ela, a da regra.</summary>
    public string AcaoRecomendada => Acao ?? (ModelValidator.Regras.TryGetValue(Codigo, out var r) ? r.Acao : "revisão humana");
}

/// <summary>Regra do validador: código, módulo, o que verifica e a ação recomendada padrão.</summary>
public sealed record Regra(string Codigo, string Modulo, string Verifica, string Acao);

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
    /// <param name="incluirNaoVerificados">Acrescenta, com <see cref="Severidade.NaoVerificavel"/>, as verificações que não
    /// puderam rodar (sem documento, sem limites, sem Projeto de Drenagem ou sem equação de chuva, sem terreno).</param>
    /// <param name="terreno">Superfície do terreno PROJETADO (o Toposolid como está) para o V14. Nulo = não conferida.</param>
    public static List<Achado> Validate(IReadOnlyList<CopiaGravada> copias, Func<string, bool>? elementoExiste = null,
        GeometricCriteria? criterios = null, Automation.ProjetoDrenagem? drenagem = null, bool incluirNaoVerificados = false,
        Automation.TerrainMesh? terreno = null)
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
            var (sev, msg, acao) = e.Tipo switch
            {
                TipoDependencia.Alvo => (Severidade.Aviso, $"{o.DisplayCode}: a marca detalhada não existe mais (detalhe órfão).", "apagar o detalhe ou refazê-lo sobre outra marca"),
                TipoDependencia.Via => (Severidade.Aviso, $"{o.DisplayCode}: aponta para uma via que não existe mais.", "refazer a conexão com uma via existente ou apagar a marca"),
                TipoDependencia.Loteamento => (Severidade.Aviso, $"{o.DisplayCode}: o loteamento da quadra não existe mais (usa as categorias padrão).", "ligar a quadra a um loteamento existente ou recriar o loteamento"),
                TipoDependencia.Filha => (Severidade.Info, $"{o.DisplayCode}: sinalização gerada pelo nó foi apagada (volta ao atualizar o nó).", "atualizar o nó se a sinalização deve voltar"),
                TipoDependencia.Rede => (Severidade.Aviso, $"{o.DisplayCode}: o dispositivo de drenagem de uma ponta do trecho não existe mais (o tubo não é desenhado).", "ligar o trecho a um dispositivo existente ou apagá-lo"),
                _ => (Severidade.Info, $"{o.DisplayCode}: recorte deixado por uma marca que não existe mais (sai na próxima atualização).", "nenhuma – sai na próxima atualização da marca"),
            };
            res.Add(new("V04", sev, Confianca.Alta, e.Tipo is TipoDependencia.Alvo or TipoDependencia.Via or TipoDependencia.Loteamento or TipoDependencia.Rede, msg,
                new[] { e.Origem, e.Destino }, acao));
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
        if (terreno != null) CheckTerrain(defs, terreno, criterios?.DeclividadeMaximaTerreno, res);

        // Corte só de dados (sem desenho na planta) sem o perfil que o usa.
        var perfis = defs.Values.OfType<SectionProfileDefinition>().Select(p => p.SectionId).ToHashSet();
        foreach (var s in defs.Values.OfType<SectionDimensionDefinition>().Where(s => DetailGenerator.SectionDataOnly(s) && !perfis.Contains(s.Id)))
            res.Add(new("V08", Severidade.Info, Confianca.Alta, false,
                $"{s.DisplayCode}: linha de corte sem o perfil transversal (sai na próxima atualização).", new[] { s.Id }));

        if (incluirNaoVerificados) res.AddRange(NaoVerificados(defs.Values, elementoExiste, criterios, drenagem, terreno));
        return res.OrderBy(a => a.Severidade).ThenBy(a => a.Codigo, StringComparer.Ordinal).ToList();
    }

    /// <summary>Verificações que não puderam rodar com os dados dados – ditas, nunca contadas como "sem problema".</summary>
    private static IEnumerable<Achado> NaoVerificados(IEnumerable<MarkingDefinition> defs, Func<string, bool>? elementoExiste,
        GeometricCriteria? criterios, Automation.ProjetoDrenagem? drenagem, Automation.TerrainMesh? terreno)
    {
        var lista = defs.ToList();
        static Achado N(string codigo, string msg, string acao) => new(codigo, Severidade.NaoVerificavel, Confianca.Alta, false, msg, Array.Empty<string>(), acao);
        if (elementoExiste == null && lista.Any(d => d.Path is { ElementIds.Count: > 0 } || d is QuadraDefinition or LoteamentoDefinition))
            yield return N("V06", "Linhas e elementos do Revit citados pelas marcas não conferidos (validação sem o documento).", "validar com o projeto aberto no Revit");
        var vias = lista.OfType<RoadPavementDefinition>().Count();
        if (vias > 0 && criterios is not { IsEmpty: false })
            yield return N("V12", $"Limites geométricos não informados: traçado e greide de {vias} via(s) não conferidos.",
                "informar os limites e a fonte em Configurações › Limites geométricos do traçado e do greide");
        if (lista.Any(d => d is DrainagePipeDefinition))
        {
            if (drenagem == null)
                yield return N("V13", "Sem Projeto de Drenagem: só o sentido, as ligações e os ciclos da rede foram conferidos; critérios e vazão não.",
                    "preencher o Projeto de Drenagem (critérios e equação de chuva)");
            else if (!drenagem.TemChuva)
                yield return N("V13", "Sem equação de chuva no Projeto de Drenagem: vazão, capacidade e velocidade das galerias, sarjetas e engolimento não calculados.",
                    "escolher ou digitar a equação de chuva do local, com a fonte, no Projeto de Drenagem");
        }
        if (terreno == null)
        {
            if (lista.OfType<RoadPavementDefinition>().Any(r => r.Output.Grade is { AdjustTerrain: true }))
                yield return N("V14", "Superfície do terreno não lida: os dados do terreno e as vias sobre ele não foram conferidos.", "validar com o Toposolid no projeto");
        }
        else if (criterios?.DeclividadeMaximaTerreno == null)
            yield return N("V14", "Declividade máxima do terreno não informada: a declividade das faces não foi conferida.",
                "informar a declividade máxima em Configurações › Limites geométricos");
    }

    /// <summary>
    /// Superfície do terreno projetado (V14): dados insuficientes ou inválidos, cotas diferentes no mesmo ponto, triângulos sem
    /// área, declividade acima do limite informado e eixos das vias que moldam o terreno fora da superfície (extrapolação).
    /// </summary>
    private static void CheckTerrain(Dictionary<string, MarkingDefinition> defs, Automation.TerrainMesh terreno, double? declividadePct,
        List<Achado> res)
    {
        var vias = defs.Values.OfType<RoadPavementDefinition>().Where(r => r.Output.Grade is { AdjustTerrain: true })
            .Select(r => (r, StoredAxis(r.PathRef))).Where(x => x.Item2 != null).ToList();
        var pontos = new List<(Vec2 P, string Via)>();
        foreach (var (r, axis) in vias)
            for (var s = 0.0; s <= axis!.Length + 1e-9; s += 5) pontos.Add((axis.PointAt(Math.Min(s, axis.Length)), r.Id));
        foreach (var a in Automation.TerrainDiagnostics.Diagnosticar(terreno, declividadePct / 100, pontos.Select(p => p.P)))
        {
            var sev = a.Tipo switch
            {
                Automation.ProblemaTerreno.DadosInsuficientes or Automation.ProblemaTerreno.CoordenadaInvalida => Severidade.Erro,
                Automation.ProblemaTerreno.TrianguloDegenerado => Severidade.Info,
                _ => Severidade.Aviso,
            };
            var ids = a.Tipo == Automation.ProblemaTerreno.ForaDaSuperficie
                ? pontos.Where(p => terreno.Z(p.P) == null).Select(p => p.Via).Distinct().ToArray()
                : Array.Empty<string>();
            var onde = a.Onde is { } o ? $" – por exemplo em ({o.X:0.00}; {o.Y:0.00})" : "";
            var acao = a.Tipo switch
            {
                Automation.ProblemaTerreno.ForaDaSuperficie => "ampliar o levantamento ou o Toposolid até as vias, ou não moldar o terreno nessas vias",
                Automation.ProblemaTerreno.DeclividadeAcimaDoLimite => "conferir o levantamento ou o projeto nas faces indicadas",
                _ => (string?)null,
            };
            res.Add(new("V14", sev, Confianca.Alta, sev != Severidade.Info, $"Terreno: {a.Texto}{onde}.", ids, acao));
        }
    }

    /// <summary>Regras do validador: módulo, o que cada código verifica e a ação recomendada padrão (relatório e manual).</summary>
    public static readonly IReadOnlyDictionary<string, Regra> Regras = new[]
    {
        new Regra("V01", "Dados e versões", "dados da definição ilegíveis (JSON inválido ou tipo desconhecido)", "refazer a marca (o log tem o erro da leitura)"),
        new Regra("V02", "Dados e versões", "definição gravada por versão mais nova do plugin (somente leitura)", "abrir o projeto com a versão que gravou a marca, ou recriá-la"),
        new Regra("V03", "Dados e versões", "elementos da mesma marca com definições diferentes", "atualizar a marca para regravar todas as cópias iguais"),
        new Regra("V04", "Dependências", "referência a marca inexistente (detalhe, via, loteamento, sinalização filha, recorte)", "refazer a ligação ou apagar a marca órfã"),
        new Regra("V05", "Dependências", "dependência circular entre marcas", "apagar e refazer uma das marcas do ciclo"),
        new Regra("V06", "Integração com o Revit", "linha ou elemento do Revit referenciado foi apagado", "redesenhar a linha de referência, ou manter o traçado guardado"),
        new Regra("V07", "Geometria", "contorno inválido (poucos vértices, área nula, lados que se cruzam)", "redesenhar o contorno sem lados que se cruzem e com área"),
        new Regra("V08", "Detalhamento", "linha de corte sem o perfil transversal", "nenhuma – sai na próxima atualização, ou refazer o perfil"),
        new Regra("V09", "Quadras e lotes", "quadras do mesmo loteamento sobrepostas", "ajustar os contornos das quadras"),
        new Regra("V10", "Quadras e lotes", "quadra fora da gleba do loteamento", "ajustar a quadra ou a gleba"),
        new Regra("V11", "Interseções", "interseção com menos de duas vias existentes", "apagar ou refazer a conexão entre as vias"),
        new Regra("V12", "Vias (traçado e greide)", "eixo ou greide de via fora dos limites geométricos informados pelo usuário (raio, tangente, rampa, K)",
            "rever o traçado ou o greide, ou os limites informados"),
        new Regra("V13", "Drenagem", "rede de drenagem: contrafluxo, trecho ligado ao próprio dispositivo, ciclo ou fora dos critérios do Projeto de Drenagem (declividade, recobrimento, diâmetro, PVs, capacidade, velocidade, sarjeta, engolimento)",
            "rever o trecho ou o dispositivo indicado, ou o critério no Projeto de Drenagem"),
        new Regra("V14", "Topografia", "superfície do terreno projetado: dados insuficientes ou inválidos, cotas diferentes no mesmo ponto, triângulos sem área, declividade acima do limite informado, eixos fora da superfície",
            "conferir o levantamento ou o Toposolid no ponto indicado"),
    }.ToDictionary(r => r.Codigo);

    /// <summary>O que cada código verifica (relatório e manual).</summary>
    public static readonly IReadOnlyDictionary<string, string> Codigos = Regras.ToDictionary(kv => kv.Key, kv => kv.Value.Verifica);

    public static string Nome(Severidade s) => s switch
    {
        Severidade.Erro => "ERRO", Severidade.Aviso => "AVISO", Severidade.Info => "INFO", _ => "NÃO VERIFICADO",
    };

    /// <summary>
    /// Relatório em texto (o mesmo que a camada Revit grava em TXT): resumo executivo com a versão, contagens por gravidade e
    /// por módulo, a comparação com a validação anterior (quando há), os achados com módulo e ação recomendada e o que não
    /// pôde ser verificado.
    /// </summary>
    public static string Report(IReadOnlyList<Achado> achados, int marcas, int elementos, string projeto, DateTime quando,
        string? versao = null, ComparacaoValidacao? comparacao = null)
    {
        static string C(Confianca c) => c switch { Confianca.Alta => "alta", Confianca.Media => "média", _ => "baixa" };
        var problemas = achados.Where(a => a.Severidade != Severidade.NaoVerificavel).ToList();
        var nao = achados.Where(a => a.Severidade == Severidade.NaoVerificavel).ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"VALIDAÇÃO DO MODELO – {projeto}");
        sb.AppendLine($"{quando:dd/MM/yyyy HH:mm}{(versao != null ? $" · SinalizaBIM {versao}" : "")} · {marcas} marca(s) em {elementos} elemento(s) · nada foi alterado no projeto.");
        sb.AppendLine($"Erros: {problemas.Count(a => a.Severidade == Severidade.Erro)} · Avisos: {problemas.Count(a => a.Severidade == Severidade.Aviso)} · " +
                      $"Informações: {problemas.Count(a => a.Severidade == Severidade.Info)} · Não verificados: {nao.Count} · " +
                      $"Pedem revisão humana: {problemas.Count(a => a.RevisaoHumana)}");
        if (problemas.Count > 0)
            sb.AppendLine("Por módulo: " + string.Join(" · ", problemas.GroupBy(a => a.Modulo).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key} {g.Count()}")));
        if (comparacao != null)
            sb.AppendLine(comparacao.Anterior is { } ant
                ? $"Desde a validação de {ant:dd/MM/yyyy HH:mm}: {comparacao.Novos.Count} novo(s), {comparacao.Persistentes.Count} persistente(s), {comparacao.Resolvidos.Count} resolvido(s)."
                : "Primeira validação registrada deste projeto: nada a comparar.");
        sb.AppendLine();
        if (problemas.Count == 0) sb.AppendLine("Nenhum problema encontrado nas verificações que rodaram.");
        var novos = comparacao?.Novos.ToHashSet() ?? new HashSet<Achado>();
        foreach (var a in problemas)
        {
            var marca = comparacao?.Anterior != null ? (novos.Contains(a) ? " [NOVO]" : " [persistente]") : "";
            sb.AppendLine($"[{Nome(a.Severidade)}]{marca} {a.Codigo} · {a.Modulo} (confiança {C(a.Confianca)}{(a.RevisaoHumana ? ", revisão humana" : "")}) {a.Mensagem}");
            sb.AppendLine($"        Ação: {a.AcaoRecomendada}");
            sb.AppendLine($"        Id: {string.Join(", ", a.Ids)}");
        }
        if (comparacao is { Resolvidos.Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Resolvidos desde a validação anterior:");
            foreach (var r in comparacao.Resolvidos) sb.AppendLine($"  [{Nome(r.Severidade)}] {r.Codigo} {r.Mensagem}");
        }
        sb.AppendLine();
        sb.AppendLine("Não verificado:");
        foreach (var a in nao) sb.AppendLine($"  {a.Codigo} · {a.Modulo}: {a.Mensagem} → {a.AcaoRecomendada}");
        sb.AppendLine("  Geometria gerada no Revit (pisos sobrepostos, pisos × terreno, retas fora do eixo): use o Autoteste. Normas de projeto e aprovação legal: fora do plugin.");
        sb.AppendLine();
        sb.AppendLine("Verificações:");
        foreach (var r in Regras.Values) sb.AppendLine($"  {r.Codigo} – {r.Verifica} ({r.Modulo})");
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
            v = v.Select(x => x with { Estacas = road.Estaqueamento }).ToList();
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
