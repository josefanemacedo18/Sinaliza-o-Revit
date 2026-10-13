using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Parcelamento;

public enum TipoLado { Frente, LadoDireito, LadoEsquerdo, Fundos, Divisa }

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
    /// <summary>Gleba menos as quadras (vias, calçadas, canteiros e o que não virou quadra).</summary>
    public double AreaViaria { get; set; }
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
            res.Quadras.Add(new QuadraDividida(q, LotDivider.Divide(q, lot.Categorias)));
        var gleba = lot.GlebaPolygon;
        res.AreaQuadras = res.Quadras.Sum(q => q.Divisao.Quadra.Area);
        if (gleba != null)
        {
            res.AreaGleba = gleba.Area;
            var inside = PolygonOps.TotalArea(PolygonOps.Intersect(res.Quadras.Select(q => q.Divisao.Quadra), new[] { gleba }));
            var union = PolygonOps.TotalArea(PolygonOps.Union(res.Quadras.Select(q => q.Divisao.Quadra)));
            if (res.AreaQuadras - inside > 1) res.Avisos.Add($"{M(res.AreaQuadras - inside)} m² de quadras ficam fora da gleba.");
            if (res.AreaQuadras - union > 1) res.Avisos.Add($"{M(res.AreaQuadras - union)} m² de quadras se sobrepõem.");
            res.AreaViaria = Math.Max(0, res.AreaGleba - inside);
        }
        else
        {
            res.AreaGleba = res.AreaQuadras;
            res.Avisos.Add("Gleba não definida: os percentuais são sobre a soma das quadras e o sistema viário não é calculado.");
        }

        var parcels = res.Quadras.SelectMany(q => q.Divisao.Parcelas).ToList();
        var names = lot.Categorias.Select(c => c.Nome).ToList();
        foreach (var extra in parcels.Select(p => p.Categoria).Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(c => !names.Contains(c, StringComparer.OrdinalIgnoreCase)))
            names.Add(extra);
        foreach (var name in names)
        {
            var ps = parcels.Where(p => string.Equals(p.Categoria, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ps.Count == 0 && name != CategoriaLote.Lote) continue;
            var cat = lot.Categoria(name);
            var area = ps.Sum(p => p.Area);
            res.Categorias.Add(new LinhaCategoria(name, ps.Count, area, res.Pct(area), cat.Nome == name && cat.Publica, Minimo(lot, name)));
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
        for (int i = 0; i < n; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            if (a.DistanceTo(b) < 1e-4) continue;
            var mid = (a + b) / 2;
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
            if (gleba != null && !gleba.Contains(probe))
            {
                edges.Add((a, b, "o limite da gleba", false));
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
        var main = sides.Where(s => s.Via).OrderByDescending(s => Len(s.Pts)).FirstOrDefault();
        var res = new List<LadoLote>();
        Vec2? nf = main.Pts != null ? Out(main.Pts) : null;
        foreach (var s in sides)
        {
            var len = Len(s.Pts);
            var curvo = s.Pts.Count > 3 && s.Turn > 5;
            TipoLado tipo;
            if (s.Via) tipo = TipoLado.Frente;
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
        return res.OrderBy(l => l.Tipo switch { TipoLado.Frente => 0, TipoLado.LadoDireito => 1, TipoLado.Fundos => 2, TipoLado.LadoEsquerdo => 3, _ => 4 })
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
                _ => "Divisa",
            };
            var medida = l.Curvo ? $"{M(l.Comprimento)} m em curva" : $"{M(l.Comprimento)} m";
            parts.Add(l.Via ? $"{nome}: {medida} para {l.Confrontante}" : $"{nome}: {medida} confrontando com {l.Confrontante}");
        }
        return string.Join("; ", parts) + $"; área de {M(p.Area)} m².";
    }
}
