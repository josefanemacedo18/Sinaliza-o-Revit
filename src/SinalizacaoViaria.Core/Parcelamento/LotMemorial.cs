using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Reports;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>
/// Memorial descritivo do loteamento (.docx): identificação, gleba, quadro de áreas com os percentuais, sistema viário, quadras
/// e a descrição de cada lote (frente, lados, fundos, confrontações e área). Projetual: sem proprietário nem matrícula.
/// </summary>
public static class LotMemorial
{
    public const string SecIdentificacao = "1. Identificação";
    public const string SecGleba = "2. Gleba";
    public const string SecAreas = "3. Quadro de áreas";
    public const string SecViario = "4. Sistema viário";
    public const string SecQuadras = "5. Quadras";
    public const string SecLotes = "6. Descrição dos lotes";
    public const string SecResponsabilidade = "7. Responsabilidade técnica";

    public static readonly string[] AllSections = { SecIdentificacao, SecGleba, SecAreas, SecViario, SecQuadras, SecLotes, SecResponsabilidade };

    /// <summary>Nota sobre a legislação: a lei federal não foi conferida no texto; os mínimos são os informados pelo usuário.</summary>
    public const string NotaLegislacao =
        "Percentuais de áreas públicas e dimensões mínimas de lotes conforme a legislação municipal aplicável e a Lei Federal nº 6.766/1979 " +
        "[a confirmar no texto da lei]. Os mínimos indicados no quadro são os informados no projeto.";

    public static MemorialDocument Build(ResumoLoteamento r, DateTime? date = null)
    {
        var lot = r.Loteamento;
        var doc = new MemorialDocument { Title = "MEMORIAL DESCRITIVO DO LOTEAMENTO" };
        var b = doc.Blocks;

        b.Add(new MemHeading(1, SecIdentificacao));
        b.Add(new MemParagraph($"Loteamento: {lot.Nome}."));
        if (!string.IsNullOrWhiteSpace(lot.Municipio)) b.Add(new MemParagraph($"Município: {lot.Municipio}."));
        else b.Add(new MemField("Município/UF", "municipio"));
        b.Add(new MemParagraph($"Data: {(date ?? DateTime.Now):dd/MM/yyyy}."));

        b.Add(new MemHeading(1, SecGleba));
        var gleba = lot.GlebaPolygon;
        if (gleba != null)
            b.Add(new MemParagraph($"A gleba tem área de {LotReport.M(r.AreaGleba)} m² e perímetro de {LotReport.M(LotDivider.Perimeter(gleba))} m, " +
                                   $"dividida em {r.Quadras.Count} quadra(s) e {r.TotalLotes} lote(s)."));
        else b.Add(new MemParagraph("Limite da gleba não definido no modelo.", Italic: true));
        b.Add(new MemField("Limites e confrontações da gleba", "confrontacoes_gleba"));

        b.Add(new MemHeading(1, SecAreas));
        b.Add(QuadroAreas(r));
        b.Add(new MemParagraph(NotaLegislacao, Italic: true));
        foreach (var a in r.Avisos.Where(a => a.Contains("abaixo do mínimo"))) b.Add(new MemParagraph("Atenção: " + a, Italic: true));

        b.Add(new MemHeading(1, SecViario));
        b.Add(new MemParagraph($"O sistema viário (vias, calçadas e canteiros) ocupa {LotReport.M(r.AreaViaria)} m² ({LotReport.P(r.Pct(r.AreaViaria))} da gleba)."));
        var vias = lot.Vias.Where(v => !string.IsNullOrWhiteSpace(v.Nome)).ToList();
        if (vias.Count > 0)
            b.Add(new MemBullets(vias.Select(v => $"{v.Nome}: extensão de {LotReport.M(ExtensaoNaGleba(v, gleba))} m no loteamento.").ToList()));
        else b.Add(new MemParagraph("Vias sem nome: use Loteamento → Nomear Vias para que as confrontações tragam o nome das ruas.", Italic: true));

        b.Add(new MemHeading(1, SecQuadras));
        b.Add(QuadroQuadras(r));

        b.Add(new MemHeading(1, SecLotes));
        foreach (var (q, d) in r.Quadras)
        {
            b.Add(new MemHeading(2, $"Quadra {q.Nome}"));
            foreach (var p in d.Parcelas)
            {
                var lados = LotReport.Lados(p, d, q, lot);
                b.Add(new MemParagraph($"{LotReport.Rotulo(p)} da Quadra {q.Nome} – {LotReport.Descricao(p, lados)}"));
            }
        }

        b.Add(new MemHeading(1, SecResponsabilidade));
        b.Add(new MemField("Responsável técnico", "responsavel_tecnico"));
        b.Add(new MemField("Registro profissional (CAU/CREA)", "registro"));
        b.Add(new MemField("ART/RRT nº", "art"));
        b.Add(new MemField("Local e data", "local_data"));
        return doc;
    }

    /// <summary>Quadro de áreas (as mesmas linhas do quadro desenhado).</summary>
    public static MemTable QuadroAreas(ResumoLoteamento r)
    {
        var rows = LinhasAreas(r).Select(l => (IReadOnlyList<string>)new[] { l.Item, l.Qtd, l.Area, l.Pct, l.Min }).ToList();
        return new MemTable(new[] { "Especificação", "Quant.", "Área (m²)", "% da gleba", "Mínimo" }, rows, new[] { 3.2, 0.8, 1.4, 1.1, 1.0 },
            new[] { 1, 2, 3 });
    }

    public static MemTable QuadroQuadras(ResumoLoteamento r)
    {
        var (headers, rows) = LinhasQuadras(r);
        var widths = headers.Select((h, i) => i == 0 ? 1.0 : 1.2).ToList();
        return new MemTable(headers, rows, widths, Enumerable.Range(1, headers.Count - 1).ToList(), TotalRow: true);
    }

    /// <summary>Linhas do quadro de áreas: gleba, categorias, sistema viário e áreas públicas.</summary>
    public static List<(string Item, string Qtd, string Area, string Pct, string Min)> LinhasAreas(ResumoLoteamento r)
    {
        static string Min(double? m) => m is { } v ? LotReport.P(v) : "";
        var rows = new List<(string, string, string, string, string)>();
        foreach (var c in r.Categorias)
        {
            var nome = c.Nome == CategoriaLote.Lote ? "Lotes" : c.Nome == CategoriaLote.Sobra ? "Sobras" : c.Nome;
            rows.Add((nome, c.Quantidade.ToString(), LotReport.M(c.Area), LotReport.P(c.Percentual), Min(c.Minimo)));
        }
        var lot = r.Loteamento;
        double? MinKey(string k) => lot.Minimos.FirstOrDefault(kv => string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase)) is { Key: not null } kv ? kv.Value : null;
        rows.Add((ResumoLoteamento.SistemaViario, "", LotReport.M(r.AreaViaria), LotReport.P(r.Pct(r.AreaViaria)), Min(MinKey(ResumoLoteamento.SistemaViario))));
        rows.Add(("Área total da gleba", "", LotReport.M(r.AreaGleba), LotReport.P(100), ""));
        var pub = string.Join(" + ", new[] { "sistema viário" }.Concat(r.Categorias.Where(c => c.Publica).Select(c => c.Nome.ToLowerInvariant())));
        rows.Add(($"{ResumoLoteamento.AreasPublicas} ({pub})", "", LotReport.M(r.AreaPublica), LotReport.P(r.Pct(r.AreaPublica)), Min(MinKey(ResumoLoteamento.AreasPublicas))));
        return rows;
    }

    /// <summary>Cabeçalhos e linhas do quadro por quadra (com a linha de total).</summary>
    public static (List<string> Headers, List<IReadOnlyList<string>> Rows) LinhasQuadras(ResumoLoteamento r)
    {
        var cats = r.Categorias.Select(c => c.Nome).ToList();
        var headers = new List<string> { "Quadra", "Lotes" };
        headers.AddRange(cats.Select(c => (c == CategoriaLote.Lote ? "Lotes" : c == CategoriaLote.Sobra ? "Sobras" : c) + " (m²)"));
        headers.Add("Área da quadra (m²)");
        var rows = new List<IReadOnlyList<string>>();
        foreach (var l in r.LinhasQuadras)
        {
            var row = new List<string> { l.Nome, l.Lotes.ToString() };
            row.AddRange(cats.Select(c => l.AreaPorCategoria.TryGetValue(c, out var a) ? LotReport.M(a) : "–"));
            row.Add(LotReport.M(l.Area));
            rows.Add(row);
        }
        var total = new List<string> { "Total", r.LinhasQuadras.Sum(l => l.Lotes).ToString() };
        total.AddRange(cats.Select(c => LotReport.M(r.LinhasQuadras.Sum(l => l.AreaPorCategoria.TryGetValue(c, out var a) ? a : 0))));
        total.Add(LotReport.M(r.LinhasQuadras.Sum(l => l.Area)));
        rows.Add(total);
        return (headers, rows);
    }

    private static double ExtensaoNaGleba(ViaNomeada v, Polygon2? gleba)
    {
        if (v.Eixo.Count < 2) return 0;
        var pts = CurveTools.Densify(v.Eixo, 1.0);
        var s = 0.0;
        for (int i = 0; i + 1 < pts.Count; i++)
            if (gleba == null || gleba.Contains((pts[i] + pts[i + 1]) / 2)) s += pts[i].DistanceTo(pts[i + 1]);
        return s;
    }
}
