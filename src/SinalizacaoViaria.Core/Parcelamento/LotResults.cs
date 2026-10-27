using System.Globalization;
using System.Text;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>Uma parte da tela de resultados do parcelamento: título e linhas com o nível.</summary>
public sealed record SecaoParcelamento(string Titulo, IReadOnlyList<EstadoContexto> Linhas);

/// <summary>Um lote (ou outra parcela) na tela: quadra, número, categoria, área, frente, o problema (vazio = nenhum) e as chaves para selecionar.</summary>
public sealed record LoteNaTela(string Quadra, string Numero, string Categoria, double Area, double Frente, string Problema, string QuadraId, string Chave);

/// <summary>Uma quadra na tela: nome, lotes, área, área por categoria (texto) e o Id.</summary>
public sealed record QuadraNaTela(string Nome, int Lotes, double Area, string Categorias, string Id);

/// <summary>Conteúdo da tela de resultados do parcelamento.</summary>
public sealed record TelaParcelamento(string Resumo, IReadOnlyList<SecaoParcelamento> Secoes, IReadOnlyList<QuadraNaTela> Quadras, IReadOnlyList<LoteNaTela> Lotes,
    IReadOnlyList<LinhaCategoria> Categorias, ResumoLoteamento Base, IndicadoresLoteamento Indicadores)
{
    public SecaoParcelamento Secao(string titulo) => Secoes.First(s => s.Titulo == titulo);
    public IEnumerable<LoteNaTela> ComProblema => Lotes.Where(l => l.Problema.Length > 0);
}

/// <summary>
/// Resultados do parcelamento (Etapa 11; Prompt de Interface §13): área de intervenção e limites, quadras, lotes, sistema viário
/// associado, áreas públicas, parâmetros dimensionais, áreas residuais e verificação – com os números do mesmo resumo dos quadros
/// e do memorial (<see cref="LotReport.Resumo"/>). Conformidade legal ou urbanística não é afirmada: só os mínimos e o lote mínimo
/// informados no loteamento são conferidos, com a fonte que o usuário escreveu.
/// </summary>
public static class ResultadosParcelamento
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string M(double v) => LotReport.M(v);
    private static string P(double v) => LotReport.P(v);
    private static EstadoContexto L(NivelSituacao n, string t) => new(n, t);

    public const string Area = "Área de intervenção e limites";
    public const string Quadras = "Quadras";
    public const string Lotes = "Lotes";
    public const string Viario = "Sistema viário associado";
    public const string Publicas = "Áreas públicas";
    public const string Parametros = "Parâmetros dimensionais";
    public const string Residuais = "Áreas residuais";
    public const string Verificacao = "Verificação";

    public static string Nome(ModoSobra m) => m switch
    {
        ModoSobra.IncorporarUltimo => "somada ao último lote",
        ModoSobra.Distribuir => "distribuída entre os lotes da testada",
        _ => "vira o último lote quando atinge a frente e a área mínimas (senão, somada ao último)",
    };

    public static TelaParcelamento Montar(LoteamentoDefinition lot, IEnumerable<QuadraDefinition> quadras)
    {
        var r = LotReport.Resumo(lot, quadras);
        var ind = IndicadoresUrbanisticos.Calcular(r);
        var parcelas = r.Quadras.SelectMany(q => q.Divisao.Parcelas.Select(p => (q.Quadra, Divisao: q.Divisao, P: p))).ToList();
        var lotes = parcelas.Select(x => new LoteNaTela(x.Quadra.Nome, x.P.Numero, x.P.Categoria, x.P.Area, x.P.Frente, x.P.AbaixoDoMinimo ?? "", x.Quadra.Id, x.P.Chave))
            .OrderBy(l => l.Quadra, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Numero, StringComparer.Create(Pt, true)).ToList();
        var areaParcelada = parcelas.Sum(x => x.P.Area);
        var secoes = new List<SecaoParcelamento>();

        // ---------------------------------------------------------------- área e limites
        var area = new List<EstadoContexto>();
        area.Add(lot.GlebaPolygon != null
            ? L(NivelSituacao.Informacao, $"Gleba de {M(r.AreaGleba)} m² ({(r.AreaGleba / 10000).ToString("0.0000", Pt)} ha).")
            : L(NivelSituacao.Aviso, "Gleba não definida: os percentuais são sobre a soma das quadras e o sistema viário não é calculado."));
        area.Add(L(NivelSituacao.Informacao, $"{r.Quadras.Count} quadra(s), {M(r.AreaQuadras)} m² ({P(r.Pct(r.AreaQuadras))} da gleba)."));
        area.Add(L(NivelSituacao.Informacao, $"Área parcelada: {M(areaParcelada)} m² – a soma das parcelas de todas as categorias."));
        secoes.Add(new(Area, area));

        // ---------------------------------------------------------------- quadras
        var quad = new List<EstadoContexto>();
        if (r.Quadras.Count == 0) quad.Add(L(NivelSituacao.NaoVerificado, "Nenhuma quadra neste loteamento."));
        else
        {
            var maior = r.LinhasQuadras.MaxBy(q => q.Area)!;
            var menor = r.LinhasQuadras.MinBy(q => q.Area)!;
            quad.Add(L(NivelSituacao.Informacao, $"Maior: quadra {maior.Nome}, {M(maior.Area)} m²; menor: quadra {menor.Nome}, {M(menor.Area)} m²."));
        }
        secoes.Add(new(Quadras, quad));

        // ---------------------------------------------------------------- lotes
        var lt = IndicadoresUrbanisticos.Linhas(ind).Take(7).Select(x => L(NivelSituacao.Informacao, $"{x.Indicador}: {x.Valor}.")).ToList();
        secoes.Add(new(Lotes, lt));

        // ---------------------------------------------------------------- sistema viário
        var via = new List<EstadoContexto>();
        via.Add(lot.GlebaPolygon != null
            ? L(NivelSituacao.Informacao, $"{M(r.AreaViaria)} m² ({P(r.Pct(r.AreaViaria))} da gleba): a gleba menos as quadras e a área remanescente – vias, calçadas e canteiros.")
            : L(NivelSituacao.NaoVerificado, "Sem a gleba, a área do sistema viário não é calculada."));
        if (lot.Vias.Count > 0) via.Add(L(NivelSituacao.Informacao, $"Vias nomeadas: {string.Join(", ", lot.Vias.Select(v => v.Nome).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())}."));
        else via.Add(L(NivelSituacao.Informacao, "Nenhuma via nomeada (Nomear Vias): o memorial e as confrontações usam o nome reconhecido pelo plugin."));
        secoes.Add(new(Viario, via));

        // ---------------------------------------------------------------- áreas públicas
        var pub = new List<EstadoContexto>();
        foreach (var c in r.Categorias.Where(c => c.Publica))
            pub.Add(L(NivelSituacao.Informacao, $"{c.Nome}: {c.Quantidade} área(s), {M(c.Area)} m² ({P(c.Percentual)})."));
        pub.Add(L(NivelSituacao.Informacao, $"Total público (sistema viário e categorias públicas): {M(r.AreaPublica)} m² ({P(r.Pct(r.AreaPublica))} da gleba)."));
        secoes.Add(new(Publicas, pub));

        // ---------------------------------------------------------------- parâmetros
        var par = new List<EstadoContexto>();
        var fonte = string.IsNullOrWhiteSpace(lot.FonteMinimos) ? "fonte não informada" : lot.FonteMinimos!.Trim();
        par.Add(L(NivelSituacao.Informacao, $"Lote mínimo: frente de {lot.FrenteMinimaLote.ToString("0.##", Pt)} m e área de {lot.AreaMinimaLote.ToString("0.##", Pt)} m² – {fonte}."));
        foreach (var g in r.Quadras.Select(q => q.Quadra).GroupBy(q => (q.Frente, q.Profundidade, q.Sobra, q.Miolo, q.MioloCategoria)))
        {
            var (f, prof, sobra, miolo, cat) = g.Key;
            par.Add(L(NivelSituacao.Informacao, $"Quadra(s) {string.Join(", ", g.Select(q => q.Nome))}: lotes de {f.ToString("0.##", Pt)} × {prof.ToString("0.##", Pt)} m; " +
                $"sobra de testada {Nome(sobra)}; miolo {(miolo == ModoMiolo.AreaPropria ? $"como {cat}" : "dividido entre os lotes")}."));
        }
        secoes.Add(new(Parametros, par));

        // ---------------------------------------------------------------- áreas residuais
        var res = new List<EstadoContexto>();
        res.Add(r.AreaRemanescente > 0.01
            ? L(NivelSituacao.Informacao, $"Área remanescente (sobra da gleba em volta das vias): {lot.Remanescentes.Count} área(s), {M(r.AreaRemanescente)} m² " +
                $"({P(r.Pct(r.AreaRemanescente))}), na categoria {LotReport.CategoriaRemanescente(lot).Nome}.")
            : L(NivelSituacao.Informacao, "Nenhuma área remanescente da gleba fora das quadras e das vias."));
        var miolos = parcelas.Where(x => x.P.Chave.StartsWith("m:", StringComparison.Ordinal)).ToList();
        if (miolos.Count > 0)
            res.Add(L(NivelSituacao.Informacao, $"Miolos de quadra em área própria: {miolos.Count}, {M(miolos.Sum(x => x.P.Area))} m²."));
        var sobras = parcelas.Count(x => x.P.Sobra);
        if (sobras > 0) res.Add(L(NivelSituacao.Informacao, $"{sobras} lote(s) feitos com a sobra da testada (lote normal, com a frente e a área mínimas)."));
        secoes.Add(new(Residuais, res));

        // ---------------------------------------------------------------- verificação
        var ver = new List<EstadoContexto>();
        var problema = lotes.Count(l => l.Problema.Length > 0);
        ver.Add(problema == 0
            ? L(NivelSituacao.Ok, $"Nenhum lote abaixo do lote mínimo informado ({fonte}).")
            : L(NivelSituacao.Aviso, $"{problema} lote(s) abaixo do lote mínimo informado ({fonte}) – ver a tabela."));
        if (lot.Minimos.Count == 0)
            ver.Add(L(NivelSituacao.NaoVerificado, "Percentuais mínimos (sistema viário, áreas públicas, categorias) não informados: não conferidos (Loteamento › Categorias, mínimos)."));
        foreach (var (chave, min) in lot.Minimos.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var obtido = LotReport.Percentual(r, chave);
            ver.Add(obtido + 1e-9 < min
                ? L(NivelSituacao.Aviso, $"{chave}: {P(obtido)} da gleba, abaixo do mínimo informado de {P(min)} ({fonte}).")
                : L(NivelSituacao.Ok, $"{chave}: {P(obtido)} da gleba – mínimo informado de {P(min)} ({fonte})."));
        }
        // Os avisos da divisão e das áreas (fora da gleba, sobreposição), sem repetir os mínimos já ditos acima.
        foreach (var a in r.Avisos.Distinct().Where(a => !a.Contains("abaixo do mínimo informado", StringComparison.Ordinal)).Take(20))
            ver.Add(L(NivelSituacao.Aviso, a));
        ver.Add(L(NivelSituacao.Informacao, "Conformidade legal ou urbanística não é afirmada: só os mínimos e o lote mínimo informados no loteamento são conferidos, com a fonte escrita pelo usuário."));
        secoes.Add(new(Verificacao, ver));

        var quadrasTela = r.LinhasQuadras.Zip(r.Quadras, (l, q) => new QuadraNaTela(l.Nome, l.Lotes, l.Area,
            string.Join("; ", l.AreaPorCategoria.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {M(kv.Value)} m²")), q.Quadra.Id)).ToList();
        var resumo = $"Loteamento \"{lot.Nome}\": {r.Quadras.Count} quadra(s), {ind.Lotes} lote(s), {M(areaParcelada)} m² parcelados" +
                     (problema > 0 ? $"; {problema} lote(s) abaixo do lote mínimo informado." : ".");
        return new TelaParcelamento(resumo, secoes, quadrasTela, lotes, r.Categorias, r, ind);
    }

    /// <summary>Texto para copiar: o resumo e cada parte.</summary>
    public static string Texto(TelaParcelamento t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(t.Resumo);
        foreach (var s in t.Secoes)
        {
            sb.AppendLine();
            sb.AppendLine(s.Titulo.ToUpperInvariant());
            foreach (var l in s.Linhas) sb.AppendLine($"  [{CentralDoProjeto.Nome(l.Nivel)}] {l.Texto}");
        }
        return sb.ToString();
    }
}
