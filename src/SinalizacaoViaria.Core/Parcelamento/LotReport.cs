using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Parcelamento;

public enum TipoLado { Frente, LadoDireito, LadoEsquerdo, Fundos, Divisa, Chanfro }

/// <summary>Lado do lote: tipo, extremos, comprimento (desenvolvimento, nos trechos em curva) e com quem confronta.</summary>
public sealed record LadoLote(TipoLado Tipo, Vec2 A, Vec2 B, double Comprimento, string Confrontante, bool Curvo, bool Via);

public sealed record LinhaCategoria(string Nome, int Quantidade, double Area, double Percentual, bool Publica, double? Minimo);

public sealed record LinhaQuadra(string Nome, int Lotes, IReadOnlyDictionary<string, double> AreaPorCategoria, double Area);

/// <summary>Quadra do loteamento já dividida.</summary>
public sealed record QuadraDividida(QuadraDefinition Quadra, DivisaoQuadra Divisao);

/// <summary>Resumo de áreas do loteamento (quadros e memorial).</summary>
public sealed class ResumoLoteamento
{
    public LoteamentoDefinition Loteamento { get; init; } = null!;
    public List<QuadraDividida> Quadras { get; } = new();
    public double AreaGleba { get; set; }
    public double AreaQuadras { get; set; }
    /// <summary>Gleba menos as quadras e a área remanescente (vias, calçadas, canteiros).</summary>
    public double AreaViaria { get; set; }
    /// <summary>Sobra da gleba em volta das vias (não é quadra) – entra na área verde.</summary>
    public double AreaRemanescente { get; set; }
    public List<LinhaCategoria> Categorias { get; } = new();
    public List<LinhaQuadra> LinhasQuadras { get; } = new();
    public double AreaPublica { get; set; }
    public List<string> Avisos { get; } = new();

    public double Pct(double area) => AreaGleba > 1e-9 ? area / AreaGleba * 100 : 0;
    public int TotalLotes => Categorias.Where(c => c.Nome == CategoriaLote.Lote).Sum(c => c.Quantidade);
    public const string SistemaViario = "Sistema viário";
    public const string AreasPublicas = "Áreas públicas";
}

/// <summary>Quadros, confrontações e memorial do loteamento.</summary>
public static class LotReport
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    public static string M(double v) => v.ToString("#,##0.00", Br);
    public static string P(double v) => v.ToString("0.00", Br) + " %";

    /// <summary>Divide todas as quadras do loteamento e soma as áreas por categoria e por quadra.</summary>
    public static ResumoLoteamento Resumo(LoteamentoDefinition lot, IEnumerable<QuadraDefinition> quadras)
    {
        var res = new ResumoLoteamento { Loteamento = lot };
        foreach (var q in quadras.Where(q => q.LoteamentoId == lot.Id).OrderBy(q => q.Nome, StringComparer.OrdinalIgnoreCase))
            res.Quadras.Add(new QuadraDividida(q, LotDivider.Divide(q, lot)));
        var gleba = lot.GlebaPolygon;
        res.AreaQuadras = res.Quadras.Sum(q => q.Divisao.Quadra.Area);
        var rem = lot.RemanescentePolygons.ToList();
        if (gleba != null)
        {
            res.AreaGleba = gleba.Area;
            var inside = PolygonOps.TotalArea(PolygonOps.Intersect(res.Quadras.Select(q => q.Divisao.Quadra), new[] { gleba }));
            var union = PolygonOps.TotalArea(PolygonOps.Union(res.Quadras.Select(q => q.Divisao.Quadra)));
            if (res.AreaQuadras - inside > 1) res.Avisos.Add($"{M(res.AreaQuadras - inside)} m² de quadras ficam fora da gleba.");
            if (res.AreaQuadras - union > 1) res.Avisos.Add($"{M(res.AreaQuadras - union)} m² de quadras se sobrepõem.");
            // A sobra da gleba em volta das vias é área verde: o sistema viário é o que resta (a soma das linhas dá a gleba).
            res.AreaRemanescente = rem.Count == 0 ? 0 : PolygonOps.TotalArea(PolygonOps.Intersect(rem, new[] { gleba }));
            res.AreaViaria = Math.Max(0, res.AreaGleba - inside - res.AreaRemanescente);
        }
        else
        {
            res.AreaRemanescente = rem.Sum(p => p.Area);
            res.AreaGleba = res.AreaQuadras + res.AreaRemanescente;
            res.Avisos.Add("Gleba não definida: os percentuais são sobre a soma das quadras e o sistema viário não é calculado.");
        }

        var parcels = res.Quadras.SelectMany(q => q.Divisao.Parcelas).ToList();
        var names = lot.Categorias.Select(c => c.Nome).ToList();
        foreach (var extra in parcels.Select(p => p.Categoria).Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(c => !names.Contains(c, StringComparer.OrdinalIgnoreCase)))
            names.Add(extra);
        var remCat = CategoriaRemanescente(lot);
        if (res.AreaRemanescente > 0.01 && !names.Contains(remCat.Nome, StringComparer.OrdinalIgnoreCase)) names.Add(remCat.Nome);
        foreach (var name in names)
        {
            var ps = parcels.Where(p => string.Equals(p.Categoria, name, StringComparison.OrdinalIgnoreCase)).ToList();
            var isRem = res.AreaRemanescente > 0.01 && string.Equals(name, remCat.Nome, StringComparison.OrdinalIgnoreCase);
            var count = ps.Count + (isRem ? rem.Count : 0);
            if (count == 0 && name != CategoriaLote.Lote) continue;
            var cat = isRem ? remCat : lot.Categoria(name);
            var area = ps.Sum(p => p.Area) + (isRem ? res.AreaRemanescente : 0);
            res.Categorias.Add(new LinhaCategoria(name, count, area, res.Pct(area), string.Equals(cat.Nome, name, StringComparison.OrdinalIgnoreCase) && cat.Publica, Minimo(lot, name)));
        }
        res.AreaPublica = res.AreaViaria + res.Categorias.Where(c => c.Publica).Sum(c => c.Area);
        foreach (var (q, d) in res.Quadras)
        {
            var byCat = d.Parcelas.GroupBy(p => p.Categoria, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(p => p.Area), StringComparer.OrdinalIgnoreCase);
            var lotes = d.Parcelas.Count(p => string.Equals(p.Categoria, CategoriaLote.Lote, StringComparison.OrdinalIgnoreCase));
            res.LinhasQuadras.Add(new LinhaQuadra(q.Nome, lotes, byCat, d.Quadra.Area));
            res.Avisos.AddRange(d.Avisos);
        }
        // Mínimos editáveis (legislação aplicável – conferir): avisa o que ficou abaixo.
        foreach (var (key, min) in lot.Minimos)
        {
            double area = key switch
            {
                ResumoLoteamento.SistemaViario => res.AreaViaria,
                ResumoLoteamento.AreasPublicas => res.AreaPublica,
                _ => res.Categorias.Where(c => string.Equals(c.Nome, key, StringComparison.OrdinalIgnoreCase)).Sum(c => c.Area),
            };
            if (res.Pct(area) + 1e-9 < min) res.Avisos.Add($"{key}: {P(res.Pct(area))} da gleba, abaixo do mínimo informado de {P(min)}.");
        }
        return res;
    }

    private static double? Minimo(LoteamentoDefinition lot, string key) =>
        lot.Minimos.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) is { Key: not null } kv ? kv.Value : null;

    /// <summary>Categoria da sobra da gleba: a área verde do loteamento (ou uma nova, se ela foi excluída).</summary>
    public static CategoriaLote CategoriaRemanescente(LoteamentoDefinition lot) =>
        lot.Categorias.FirstOrDefault(c => string.Equals(c.Nome, CategoriaLote.AreaVerde, StringComparison.OrdinalIgnoreCase))
        ?? CategoriaLote.Padrao().First(c => c.Nome == CategoriaLote.AreaVerde);

    /// <summary>Número da sobra da gleba: "AV-R" (ou "AV-R1", "AV-R2"... com mais de uma) – não se confunde com as áreas das quadras.</summary>
    public static string NumeroRemanescente(LoteamentoDefinition lot, int i, int count)
    {
        var pre = CategoriaRemanescente(lot).Prefixo;
        var tag = (string.IsNullOrWhiteSpace(pre) ? "" : pre + "-") + "R";
        return count > 1 ? tag + (i + 1) : tag;
    }

    /// <summary>Rótulo do lote: "Lote 12" ou "Área verde AV-1".</summary>
    public static string Rotulo(Parcela p) =>
        string.Equals(p.Categoria, CategoriaLote.Lote, StringComparison.OrdinalIgnoreCase) ? $"Lote {p.Numero}" : $"{p.Categoria} {p.Numero}";

    /// <summary>
    /// Lados do lote com as confrontações: frente(s) para as vias, lados direito e esquerdo (de quem olha o lote da via) e
    /// fundos; trechos em curva saem com o desenvolvimento.
    /// </summary>
    public static List<LadoLote> Lados(Parcela p, DivisaoQuadra d, QuadraDefinition q, LoteamentoDefinition? lot)
    {
        var ring = p.Forma.Outer;
        var n = ring.Count;
        var gleba = lot?.GlebaPolygon;
        var edges = new List<(Vec2 A, Vec2 B, string Conf, bool Via)>();
        var chanfros = new HashSet<string>();
        for (int i = 0; i < n; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            if (a.DistanceTo(b) < 1e-4) continue;
            var mid = (a + b) / 2;
            // Face do chanfro da esquina: lado próprio, para o encontro das duas vias.
            if (d.Chanfros.FirstOrDefault(c => c.EFace(a, b, 0.05)) is { } ch)
            {
                var vias = new[] { ViaDe(ch.A, d, q, lot), ViaDe(ch.B, d, q, lot) }.Where(v => v != null).Distinct().ToList();
                var conf = vias.Count == 2 ? $"a esquina da {vias[0]} com a {vias[1]}" : vias.Count == 1 ? $"a esquina da {vias[0]}" : "a esquina";
                chanfros.Add(conf);
                edges.Add((a, b, conf, true));
                continue;
            }
            var probe = mid + (b - a).Normalized().PerpRight * 0.2;
            var other = d.Parcelas.FirstOrDefault(o => !ReferenceEquals(o, p) && o.Forma.Contains(probe));
            if (other != null)
            {
                edges.Add((a, b, $"{Rotulo(other)} da Quadra {q.Nome}", false));
                continue;
            }
            if (d.Quadra.Contains(probe))
            {
                edges.Add((a, b, $"área da Quadra {q.Nome}", false));
                continue;
            }
            // Fora da quadra: via quando o lado está numa testada ou, com a gleba conhecida, no espaço entre as quadras dentro dela
            // (sistema viário); fora da gleba, o limite dela; sem gleba e sem testada, a divisa da quadra.
            var naTestada = d.Cadeias.Any(c => c.TestadaId != null && Math.Abs(new Polyline2(c.Pontos).Project(mid).Signed) < 0.05);
            if (!naTestada && gleba != null && !gleba.Contains(probe))
            {
                edges.Add((a, b, "o limite da gleba", false));
                continue;
            }
            if (!naTestada && gleba == null)
            {
                edges.Add((a, b, $"a divisa da Quadra {q.Nome}", false));
                continue;
            }
            var via = ViaDe(mid, d, q, lot) ?? "via pública";
            edges.Add((a, b, via, true));
        }
        // Junta trechos seguidos com o mesmo confrontante e pouca deflexão (arcos e vértices alinhados).
        var sides = new List<(List<Vec2> Pts, string Conf, bool Via, double Turn)>();
        foreach (var e in edges)
        {
            if (sides.Count > 0)
            {
                var last = sides[^1];
                var prevDir = (last.Pts[^1] - last.Pts[^2]).Normalized();
                var dir = (e.B - e.A).Normalized();
                var turn = Math.Abs(Math.Atan2(prevDir.Cross(dir), prevDir.Dot(dir))) * 180 / Math.PI;
                if (last.Conf == e.Conf && last.Pts[^1].DistanceTo(e.A) < 1e-4 && turn < 20 && last.Turn + turn < 100)
                {
                    last.Pts.Add(e.B);
                    sides[^1] = (last.Pts, last.Conf, last.Via, last.Turn + turn);
                    continue;
                }
            }
            sides.Add((new List<Vec2> { e.A, e.B }, e.Conf, e.Via, 0));
        }
        // O primeiro lado pode continuar o último (o anel começa no meio de um lado).
        if (sides.Count > 1)
        {
            var f = sides[0];
            var l = sides[^1];
            var d0 = (l.Pts[^1] - l.Pts[^2]).Normalized();
            var d1 = (f.Pts[1] - f.Pts[0]).Normalized();
            var turn = Math.Abs(Math.Atan2(d0.Cross(d1), d0.Dot(d1))) * 180 / Math.PI;
            if (f.Conf == l.Conf && l.Pts[^1].DistanceTo(f.Pts[0]) < 1e-4 && turn < 20)
            {
                l.Pts.AddRange(f.Pts.Skip(1));
                sides[^1] = (l.Pts, l.Conf, l.Via, l.Turn + f.Turn + turn);
                sides.RemoveAt(0);
            }
        }

        static double Len(List<Vec2> pts) { var s = 0.0; for (int i = 0; i + 1 < pts.Count; i++) s += pts[i].DistanceTo(pts[i + 1]); return s; }
        static Vec2 Out(List<Vec2> pts) => (pts[^1] - pts[0]).Normalized().PerpRight;
        var main = sides.Where(s => s.Via && !chanfros.Contains(s.Conf)).OrderByDescending(s => Len(s.Pts)).FirstOrDefault();
        var res = new List<LadoLote>();
        Vec2? nf = main.Pts != null ? Out(main.Pts) : null;
        foreach (var s in sides)
        {
            var len = Len(s.Pts);
            var curvo = s.Pts.Count > 3 && s.Turn > 5;
            TipoLado tipo;
            if (chanfros.Contains(s.Conf)) tipo = TipoLado.Chanfro;
            else if (s.Via) tipo = TipoLado.Frente;
            else if (nf is { } f)
            {
                var o = Out(s.Pts);
                var right = (-f).PerpRight;
                if (o.Dot(f) < -0.5) tipo = TipoLado.Fundos;
                else if (o.Dot(right) > 0.3) tipo = TipoLado.LadoDireito;
                else if (o.Dot(right) < -0.3) tipo = TipoLado.LadoEsquerdo;
                else tipo = TipoLado.Fundos;
            }
            else tipo = TipoLado.Divisa;
            res.Add(new LadoLote(tipo, s.Pts[0], s.Pts[^1], len, s.Conf, curvo, s.Via));
        }
        // Ordem do memorial: frente(s), lado direito, fundos, lado esquerdo (sentido anti-horário a partir da frente principal).
        return res.OrderBy(l => l.Tipo switch { TipoLado.Frente => 0, TipoLado.Chanfro => 0.5, TipoLado.LadoDireito => 1, TipoLado.Fundos => 2, TipoLado.LadoEsquerdo => 3, _ => 4 })
            .ThenByDescending(l => l.Comprimento).ToList();
    }

    /// <summary>Nome da via em frente: o da testada (se dado), senão o da via nomeada mais próxima.</summary>
    private static string? ViaDe(Vec2 p, DivisaoQuadra d, QuadraDefinition q, LoteamentoDefinition? lot)
    {
        var chain = d.Cadeias.Where(c => c.TestadaId != null).OrderBy(c => Math.Abs(new Polyline2(c.Pontos).Project(p).Signed)).FirstOrDefault();
        if (chain != null && Math.Abs(new Polyline2(chain.Pontos).Project(p).Signed) < 0.5)
        {
            var t = q.Testadas.FirstOrDefault(x => x.Id == chain.TestadaId);
            if (!string.IsNullOrWhiteSpace(t?.Via)) return t!.Via;
        }
        return lot?.ViaEm(p);
    }

    /// <summary>Cabeçalhos do quadro de lotes (lote a lote).</summary>
    public static readonly string[] CabecalhosLotes =
        { "QUADRA", "LOTE", "CATEGORIA", "FRENTE PARA", "FRENTE (m)", "LADO DIR. (m)", "LADO ESQ. (m)", "FUNDOS (m)", "ÁREA (m²)", "OBSERVAÇÃO" };

    /// <summary>
    /// Quadro de lotes, lote a lote (na ordem da numeração): quadra, número, categoria, via(s) da frente, medida de cada lado, área e
    /// observações (esquina, união, frente em curva, abaixo do mínimo). <paramref name="quadra"/> filtra uma quadra.
    /// </summary>
    public static List<string[]> LinhasLotes(ResumoLoteamento r, string? quadra = null)
    {
        var rows = new List<string[]>();
        foreach (var (q, d) in r.Quadras)
        {
            if (quadra != null && !string.Equals(q.Nome, quadra, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var p in NaOrdem(d.Parcelas)) rows.Add(LinhaLote(p, d, q, r.Loteamento));
        }
        return rows;
    }

    /// <summary>Parcelas na ordem da numeração: lotes 1..N, depois as outras categorias (AI-1, AV-1...).</summary>
    public static IEnumerable<Parcela> NaOrdem(IEnumerable<Parcela> ps) =>
        ps.OrderBy(p => string.Equals(p.Categoria, CategoriaLote.Lote, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Numero.Contains('-') ? p.Numero[..p.Numero.LastIndexOf('-')] : "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => int.TryParse(p.Numero.Contains('-') ? p.Numero[(p.Numero.LastIndexOf('-') + 1)..] : p.Numero, out var n) ? n : int.MaxValue)
            .ThenBy(p => p.Numero, StringComparer.Ordinal);

    /// <summary>Linha do quadro de lotes (as colunas de <see cref="CabecalhosLotes"/>).</summary>
    public static string[] LinhaLote(Parcela p, DivisaoQuadra d, QuadraDefinition q, LoteamentoDefinition? lot)
    {
        var lados = Lados(p, d, q, lot);
        string Med(TipoLado t)
        {
            var ls = lados.Where(l => l.Tipo == t).ToList();
            return ls.Count == 0 ? "–" : string.Join(" + ", ls.Select(l => M(l.Comprimento)));
        }
        var ruas = lados.Where(l => l.Via && l.Tipo != TipoLado.Chanfro).Select(l => l.Confrontante).Distinct().ToList();
        var obs = new List<string>();
        // Esquina: frente para duas vias (ou dois lados de via).
        if (lados.Count(l => l.Tipo == TipoLado.Frente) >= 2) obs.Add("esquina");
        if (p.Unidos is { Count: > 1 } u) obs.Add($"união de {u.Count} lotes");
        if (lados.Any(l => l.Tipo == TipoLado.Frente && l.Curvo)) obs.Add("frente em curva");
        foreach (var l in lados.Where(l => l.Tipo == TipoLado.Chanfro)) obs.Add($"chanfro {M(l.Comprimento)} m");
        if (p.AbaixoDoMinimo != null) obs.Add("abaixo do mínimo: " + p.AbaixoDoMinimo);
        else if (ruas.Count == 0) obs.Add("sem frente para via");
        return new[]
        {
            q.Nome, p.Numero, p.Categoria, ruas.Count > 0 ? string.Join(" / ", ruas) : "–", Med(TipoLado.Frente), Med(TipoLado.LadoDireito),
            Med(TipoLado.LadoEsquerdo), Med(TipoLado.Fundos), M(p.Area), string.Join("; ", obs),
        };
    }

    /// <summary>Descrição do lote para o memorial (frente, lados, fundos e área).</summary>
    public static string Descricao(Parcela p, IReadOnlyList<LadoLote> lados)
    {
        var parts = new List<string>();
        foreach (var l in lados)
        {
            var nome = l.Tipo switch
            {
                TipoLado.Frente => "Frente",
                TipoLado.LadoDireito => "Lado direito",
                TipoLado.LadoEsquerdo => "Lado esquerdo",
                TipoLado.Fundos => "Fundos",
                TipoLado.Chanfro => "Chanfro",
                _ => "Divisa",
            };
            var medida = l.Curvo ? $"{M(l.Comprimento)} m em curva" : $"{M(l.Comprimento)} m";
            parts.Add(l.Via ? $"{nome}: {medida} para {l.Confrontante}" : $"{nome}: {medida} confrontando com {l.Confrontante}");
        }
        return string.Join("; ", parts) + $"; área de {M(p.Area)} m².";
    }
}
