using System.Globalization;
using System.Text;

namespace SinalizacaoViaria.Core.Model;

/// <summary>O achado diante da validação anterior: novo, persistente, primeira validação (sem anterior) ou não verificado.</summary>
public enum EstadoAchado { Novo, Persistente, PrimeiraValidacao, NaoVerificado }

/// <summary>
/// Uma linha da Central de Qualidade: categoria (o módulo da regra), código, gravidade, confiança, se pede revisão humana, os
/// elementos (nomes das marcas), a descrição, o estado diante da anterior, a ação recomendada, as marcas e a chave do histórico.
/// </summary>
public sealed record LinhaQualidade(int Ordem, string Categoria, string Codigo, Severidade Gravidade, Confianca Confianca, bool RevisaoHumana,
    string Elementos, string Descricao, EstadoAchado Estado, string Acao, IReadOnlyList<string> Ids, string Chave);

/// <summary>Ordem da tabela: gravidade (padrão), categoria, código ou estado.</summary>
public enum OrdemQualidade { Gravidade, Categoria, Codigo, Estado }

/// <summary>Filtro da tabela: categoria, gravidade, estado e texto (nulos = todos).</summary>
public sealed record FiltroQualidade(string? Categoria = null, Severidade? Gravidade = null, EstadoAchado? Estado = null, string? Texto = null);

/// <summary>Contagem de uma categoria por gravidade.</summary>
public sealed record ContagemCategoria(string Categoria, int Erros, int Avisos, int Infos, int NaoVerificados)
{
    public int Total => Erros + Avisos + Infos + NaoVerificados;
}

/// <summary>
/// A verificação na Central: identificação, linhas, resolvidos desde a anterior e contagens por categoria (todas as que têm
/// verificador, com zero quando nada foi achado).
/// </summary>
public sealed record ResumoQualidade(string Projeto, DateTime Quando, string? Versao, DateTime? Anterior, int Marcas, int Elementos,
    IReadOnlyList<LinhaQualidade> Linhas, IReadOnlyList<AchadoGravado> Resolvidos, IReadOnlyList<ContagemCategoria> PorCategoria)
{
    public int Conta(Severidade s) => Linhas.Count(l => l.Gravidade == s);
}

/// <summary>
/// Central de Qualidade (Etapa 12; Prompt de Interface §17): os achados do Validar Modelo – os mesmos verificadores, sem trocar
/// nenhum por uma verificação superficial – numa tabela com categoria, elemento, descrição, gravidade (erro, aviso, informação,
/// não verificado, com a confiança e a revisão humana preservadas), estado diante da validação anterior, ação recomendada e a
/// data e a versão da verificação; filtros, ordenação, os resolvidos desde a anterior e a exportação em CSV. Só aparecem as
/// categorias que têm verificador (não há categoria sem regra por trás). O diagnóstico do modelo não se mistura com o
/// Autoteste (testes do plugin): esse é <see cref="RelatorioAutoteste"/>.
/// </summary>
public static class CentralQualidade
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Ordem das categorias (Prompt de Interface §17.1, só as que têm verificador): geometria, topografia, vias, interseções,
    /// terraplenagem, drenagem, quadras e lotes, dependências e integração com o Revit; depois as demais do validador.
    /// </summary>
    public static IReadOnlyList<string> Ordem { get; } = new[]
    {
        "Geometria", "Topografia", "Vias (traçado e greide)", "Interseções", "Terraplenagem", "Drenagem", "Quadras e lotes", "Dependências",
        "Integração com o Revit", "Integração", "Dados e versões", "Detalhamento", "Outros",
    };

    private static int Posicao(string categoria)
    {
        var i = Ordem.ToList().IndexOf(categoria);
        return i < 0 ? Ordem.Count : i;
    }

    public static string Nome(EstadoAchado e) => e switch
    {
        EstadoAchado.Novo => "novo",
        EstadoAchado.Persistente => "persistente",
        EstadoAchado.PrimeiraValidacao => "primeira validação",
        _ => "não verificado",
    };

    public static string Nome(Confianca c) => c switch { Confianca.Alta => "alta", Confianca.Media => "média", _ => "baixa" };

    /// <param name="achados">Os achados do Validar Modelo (com os não verificados).</param>
    /// <param name="anterior">A validação anterior gravada deste projeto; nula = primeira.</param>
    /// <param name="nomeDaMarca">Nome de uma marca pelo Id (tipo e código); nulo = não existe mais.</param>
    public static ResumoQualidade Montar(IReadOnlyList<Achado> achados, ValidacaoGravada? anterior, Func<string, string?> nomeDaMarca, string projeto,
        DateTime quando, string? versao, int marcas, int elementos)
    {
        var problemas = achados.Where(a => a.Severidade != Severidade.NaoVerificavel).ToList();
        var chaves = ValidationHistory.Chaves(problemas);
        var chaveDe = new Dictionary<Achado, Queue<string>>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < problemas.Count; i++)
        {
            if (!chaveDe.TryGetValue(problemas[i], out var q)) chaveDe[problemas[i]] = q = new Queue<string>();
            q.Enqueue(chaves[i]);
        }
        var antes = anterior?.Achados.Select(x => x.Chave).ToHashSet();
        string Elementos(IReadOnlyList<string> ids)
        {
            if (ids.Count == 0) return "—";
            var nomes = ids.Distinct().Select(id => nomeDaMarca(id) ?? $"{(id.Length > 8 ? id[..8] : id)} (não existe mais)").ToList();
            return string.Join(", ", nomes.Take(3)) + (nomes.Count > 3 ? $" e mais {nomes.Count - 3}" : "");
        }
        var linhas = achados.Select((a, i) =>
        {
            var chave = a.Severidade == Severidade.NaoVerificavel ? "" : chaveDe[a].Dequeue();
            var estado = a.Severidade == Severidade.NaoVerificavel ? EstadoAchado.NaoVerificado
                : antes == null ? EstadoAchado.PrimeiraValidacao
                : antes.Contains(chave) ? EstadoAchado.Persistente : EstadoAchado.Novo;
            return new LinhaQualidade(i + 1, a.Modulo, a.Codigo, a.Severidade, a.Confianca, a.RevisaoHumana, Elementos(a.Ids), a.Mensagem, estado,
                a.AcaoRecomendada, a.Ids, chave);
        }).ToList();
        var ordenadas = Ordenar(linhas);
        var cmp = ValidationHistory.Comparar(achados, anterior);
        // Todas as categorias com verificador, com zero quando nada foi achado (conferido sem problema ≠ não conferido: esse tem
        // a sua linha "não verificado"); depois as dos achados sem regra conhecida.
        var porCategoria = ModelValidator.Regras.Values.Select(r => r.Modulo).Concat(linhas.Select(l => l.Categoria)).Distinct()
            .OrderBy(Posicao).ThenBy(c => c, StringComparer.Create(Pt, true))
            .Select(c =>
            {
                var g = linhas.Where(l => l.Categoria == c).ToList();
                return new ContagemCategoria(c, g.Count(l => l.Gravidade == Severidade.Erro), g.Count(l => l.Gravidade == Severidade.Aviso),
                    g.Count(l => l.Gravidade == Severidade.Info), g.Count(l => l.Gravidade == Severidade.NaoVerificavel));
            }).ToList();
        return new ResumoQualidade(projeto, quando, versao, anterior?.Quando, marcas, elementos, ordenadas, cmp.Resolvidos, porCategoria);
    }

    /// <summary>
    /// Ordem padrão: gravidade (erro, aviso, informação, não verificado), categoria (§17.1) e a ordem do validador; ou a escolhida,
    /// sempre desempatando pela gravidade e pela ordem do validador.
    /// </summary>
    public static List<LinhaQualidade> Ordenar(IEnumerable<LinhaQualidade> linhas, OrdemQualidade ordem = OrdemQualidade.Gravidade) => ordem switch
    {
        OrdemQualidade.Categoria => linhas.OrderBy(l => Posicao(l.Categoria)).ThenBy(l => l.Gravidade).ThenBy(l => l.Ordem).ToList(),
        OrdemQualidade.Codigo => linhas.OrderBy(l => l.Codigo, StringComparer.Ordinal).ThenBy(l => l.Gravidade).ThenBy(l => l.Ordem).ToList(),
        OrdemQualidade.Estado => linhas.OrderBy(l => l.Estado).ThenBy(l => l.Gravidade).ThenBy(l => l.Ordem).ToList(),
        _ => linhas.OrderBy(l => l.Gravidade).ThenBy(l => Posicao(l.Categoria)).ThenBy(l => l.Ordem).ToList(),
    };

    /// <summary>As linhas que passam no filtro (texto: na descrição, nos elementos, no código e na ação, sem diferença de maiúsculas).</summary>
    public static List<LinhaQualidade> Filtrar(IEnumerable<LinhaQualidade> linhas, FiltroQualidade f)
    {
        var texto = string.IsNullOrWhiteSpace(f.Texto) ? null : f.Texto.Trim();
        return linhas.Where(l =>
            (f.Categoria == null || l.Categoria == f.Categoria) &&
            (f.Gravidade == null || l.Gravidade == f.Gravidade) &&
            (f.Estado == null || l.Estado == f.Estado) &&
            (texto == null || new[] { l.Descricao, l.Elementos, l.Codigo, l.Acao, l.Categoria }.Any(x => x.Contains(texto, StringComparison.CurrentCultureIgnoreCase))))
            .ToList();
    }

    /// <summary>Linhas em CSV (";"), com a identificação da verificação no topo.</summary>
    public static string Csv(IEnumerable<LinhaQualidade> linhas, ResumoQualidade r)
    {
        static string C(string s) => s.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');
        var sb = new StringBuilder();
        sb.AppendLine($"CENTRAL DE QUALIDADE – {C(r.Projeto)}");
        sb.AppendLine($"Verificação de {r.Quando.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}; versão {r.Versao ?? "não informada"}; " +
                      (r.Anterior is { } a ? $"comparada com a de {a.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}" : "primeira validação deste projeto"));
        sb.AppendLine("Categoria;Código;Gravidade;Confiança;Revisão humana;Estado;Elementos;Descrição;Ação recomendada");
        foreach (var l in linhas)
            sb.AppendLine(string.Join(";", C(l.Categoria), l.Codigo, ModelValidator.Nome(l.Gravidade), Nome(l.Confianca), l.RevisaoHumana ? "sim" : "não",
                Nome(l.Estado), C(l.Elementos), C(l.Descricao), C(l.Acao)));
        return sb.ToString();
    }
}
