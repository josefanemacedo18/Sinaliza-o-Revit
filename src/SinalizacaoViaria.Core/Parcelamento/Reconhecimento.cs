using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>Quadras reconhecidas na gleba: contorno, nome e testadas sugeridas (lados junto às vias).</summary>
public sealed record QuadraReconhecida(string Nome, Polygon2 Contorno, List<Vec2> Testadas);

public sealed class ResultadoReconhecimento
{
    public Polygon2 Gleba { get; init; } = null!;
    public List<QuadraReconhecida> Quadras { get; } = new();
    /// <summary>Área da gleba ocupada pelo sistema viário (vias, calçadas, canteiros) – gleba menos as quadras.</summary>
    public double AreaViaria { get; set; }
    public List<string> Avisos { get; } = new();
}

/// <summary>
/// Reconhecimento do loteamento: a gleba menos o que as vias ocupam (pista, calçadas, meios-fios, canteiros) são as quadras;
/// os lados das quadras que encostam nas vias são as testadas.
/// </summary>
public static class Reconhecimento
{
    /// <summary>Peças que ocupam o chão do sistema viário (pavimentos, calçadas, meios-fios, sarjetas, canteiros gramados).</summary>
    public static bool Viaria(MarkingPiece p) => p.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto
        or MarkingColor.PavimentoTerra or MarkingColor.Concreto or MarkingColor.Grama or MarkingColor.Brita or MarkingColor.Terra;

    /// <summary>Quadras da gleba: partes livres de vias com pelo menos <paramref name="areaMinima"/> m².</summary>
    public static ResultadoReconhecimento Quadras(Polygon2 gleba, IEnumerable<Polygon2> ocupado, double areaMinima = 50)
    {
        var res = new ResultadoReconhecimento { Gleba = gleba };
        var occ = Ocupacao(ocupado);
        var livres = PolygonOps.Difference(new[] { gleba }, occ);
        // Abertura de 5 cm: tira lascas entre peças vizinhas sem arredondar as quinas.
        livres = PolygonOps.Offset(PolygonOps.Offset(livres, -0.05), 0.05);
        var quadras = new List<Polygon2>();
        var pequenas = 0;
        foreach (var p in livres)
        {
            if (p.Area < areaMinima) { if (p.Area > 1) pequenas++; continue; }
            var s = p.Simplified(0.01, 1e-4) ?? p;
            quadras.Add(new Polygon2(LotDivider.CleanRing(s.Outer), s.Holes));
        }
        if (pequenas > 0) res.Avisos.Add($"{pequenas} área(s) livre(s) menores que {areaMinima:0} m² não viraram quadra (entram no sistema viário).");
        if (quadras.Count == 0) res.Avisos.Add("Nenhuma quadra encontrada: a gleba não tem área livre de vias.");
        var names = Nomes(quadras.Count);
        var ordered = quadras.OrderBy(q => -Math.Round(q.Centroid.Y / 25)).ThenBy(q => q.Centroid.X).ToList();
        for (int i = 0; i < ordered.Count; i++)
            res.Quadras.Add(new QuadraReconhecida(names[i], ordered[i], TestadasAutomaticas(ordered[i], occ)));
        res.AreaViaria = Math.Max(0, gleba.Area - ordered.Sum(q => q.Area));
        return res;
    }

    /// <summary>União do que as vias ocupam, com as frestas de até 10 cm entre peças fechadas.</summary>
    public static List<Polygon2> Ocupacao(IEnumerable<Polygon2> ocupado) =>
        PolygonOps.Offset(PolygonOps.Offset(PolygonOps.Union(ocupado.Where(p => p.IsValid)), 0.05), -0.05);

    /// <summary>Pontos (meio de cada lado) dos lados da quadra que encostam nas vias – as testadas sugeridas, do maior para o menor.</summary>
    public static List<Vec2> TestadasAutomaticas(Polygon2 quadra, IReadOnlyList<Polygon2> occ)
    {
        var res = new List<(Vec2 P, double L)>();
        foreach (var c in LotDivider.Cadeias(quadra.Outer))
        {
            var line = new Polyline2(c.Pontos);
            var len = line.Length;
            if (len < 1) continue;
            int n = Math.Max(3, (int)(len / 1.0)), hits = 0;
            for (int i = 0; i < n; i++)
            {
                var s = (i + 0.5) * len / n;
                var q = line.PointAt(s) + line.TangentAt(s).PerpRight * 0.5;
                if (occ.Any(o => o.Contains(q))) hits++;
            }
            if (hits >= n / 2.0) res.Add((line.PointAt(len / 2), len));
        }
        return res.OrderByDescending(x => x.L).Select(x => x.P).ToList();
    }

    /// <summary>Nomes de quadra: A, B, ... Z, AA, AB...</summary>
    public static List<string> Nomes(int n)
    {
        var res = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var k = i;
            var s = "";
            do
            {
                s = (char)('A' + k % 26) + s;
                k = k / 26 - 1;
            } while (k >= 0);
            res.Add(s);
        }
        return res;
    }

    /// <summary>Quadra nova a partir do reconhecimento (testadas na ordem do maior lado para o menor).</summary>
    public static QuadraDefinition NovaQuadra(QuadraReconhecida r, LoteamentoDefinition lot, double frente, double profundidade)
    {
        var q = new QuadraDefinition
        {
            LoteamentoId = lot.Id,
            Nome = r.Nome,
            Contorno = r.Contorno.Outer.ToList(),
            Frente = frente,
            Profundidade = profundidade,
            Z = lot.Z,
        };
        var i = 0;
        foreach (var p in r.Testadas) q.Testadas.Add(new TestadaLote { Id = "T" + ++i, Ponto = p });
        return q;
    }
}
