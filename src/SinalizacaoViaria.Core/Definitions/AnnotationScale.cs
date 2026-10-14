using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Definitions;

/// <summary>
/// Detalhamento dimensionado em milímetros de papel × escala da vista: textos (notas nativas, que já seguem a escala),
/// símbolos, quadros, cotas e perfis. Quando a escala de uma vista muda, os detalhes dela são ajustados no lugar para a nova
/// escala – o que a pessoa moveu, apagou ou editou neles continua como estava.
/// </summary>
public static class AnnotationScale
{
    /// <summary>
    /// Detalhes desenhados na vista <paramref name="viewId"/> (UniqueId) – os que dependem da escala –, na ordem de geração
    /// (os que apontam para uma marca antes dos que leem o projeto inteiro).
    /// </summary>
    public static List<MarkingDefinition> ToRescale(IEnumerable<MarkingDefinition> defs, string viewId) =>
        defs.Where(d => d is IAnnotationDefinition && !string.IsNullOrEmpty(viewId) && d.Output.ViewId == viewId)
            .GroupBy(d => d.Id).Select(g => g.First())
            .OrderBy(d => d is IProjectWideAnnotation ? 1 : 0)
            .ThenBy(d => d is SectionProfileDefinition ? 1 : 0)
            .ToList();

    /// <summary>
    /// O detalhe é ajustado no lugar na troca de escala (sem apagar e refazer). Só o trecho de prancha é refeito: ele define a
    /// vista (escala, região de corte e posição na folha), não é um desenho editável.
    /// </summary>
    public static bool AjustaNoLugar(MarkingDefinition d) => d is IAnnotationDefinition && d is not SheetSegmentDefinition;

    /// <summary>Chave da região: cor, cor própria e hachura (a mesma união de peças que vira uma região preenchida).</summary>
    public static string ChaveRegiao(MarkingColor color, string? rgb, Hachura hatch) => $"{color}|{rgb}|{hatch}";

    /// <summary>
    /// Itens do detalhe como o Revit os cria: cada texto (posição e conteúdo), cada segmento das linhas e as peças de mesma
    /// cor/hachura unidas numa região (anéis externos e furos).
    /// </summary>
    public static List<ItemDetalhe> Itens(MarkingGeometry g, double minSegment = 0.0005)
    {
        var res = new List<ItemDetalhe>();
        foreach (var grp in g.Pieces.GroupBy(p => ChaveRegiao(p.Color, p.Rgb, p.Hatch)))
        {
            var rings = PolygonOps.Union(grp.Select(p => p.Shape)).SelectMany(p => p.Holes.Prepend(p.Outer)).Where(r => r.Count >= 3)
                .Select(r => (IReadOnlyList<Vec2>)r.ToList()).ToList();
            var first = grp.First();
            if (rings.Count > 0)
                res.Add(new ItemDetalhe(TipoItemDetalhe.Regiao, grp.Key, rings) { Fonte = new FonteRegiao(first.Color, first.Rgb, first.Hatch, grp.Select(p => p.Shape).ToList()) });
        }
        foreach (var a in g.Annotations)
        {
            switch (a)
            {
                case AnnotationLine l:
                    for (int i = 0; i + 1 < l.Points.Count; i++)
                        if (l.Points[i].DistanceTo(l.Points[i + 1]) >= minSegment)
                            res.Add(new ItemDetalhe(TipoItemDetalhe.Linha, "", new[] { (IReadOnlyList<Vec2>)new[] { l.Points[i], l.Points[i + 1] } })
                            {
                                Fonte = new AnnotationLine(new[] { l.Points[i], l.Points[i + 1] }, l.Color),
                            });
                    break;
                case AnnotationText t when !string.IsNullOrWhiteSpace(t.Text):
                    res.Add(ItemDetalhe.Texto(t.Text, t.Position) with { Fonte = t });
                    break;
            }
        }
        return res;
    }

    /// <summary>
    /// Ajuste dos elementos <paramref name="existentes"/> de um detalhe desenhado como <paramref name="antes"/> (escala antiga)
    /// para <paramref name="depois"/> (escala nova): cada elemento vai para o lugar do seu correspondente na escala nova, com o
    /// mesmo deslocamento que a pessoa deu a ele; o que ela apagou continua apagado, o texto editado continua com o texto dela, e
    /// só o que aparece na escala nova (e não existia na antiga) é criado. <paramref name="mm"/>: metros por mm de papel na escala
    /// antiga (raio para reencontrar um texto cujo conteúdo foi editado).
    /// </summary>
    public static AjusteDeEscala Ajustar(IReadOnlyList<ItemDetalhe> antes, IReadOnlyList<ItemDetalhe> depois, IReadOnlyList<ItemDetalhe> existentes, double mm)
    {
        var plan = new AjusteDeEscala();
        // 1. Antes ↔ depois: mesmo tipo e chave, na ordem em que o gerador os produz.
        var next = new int[antes.Count];
        Array.Fill(next, -1);
        var usedNew = new bool[depois.Count];
        foreach (var grp in Enumerable.Range(0, antes.Count).GroupBy(i => (antes[i].Tipo, antes[i].Chave)))
        {
            var news = Enumerable.Range(0, depois.Count).Where(j => depois[j].Tipo == grp.Key.Tipo && depois[j].Chave == grp.Key.Chave).ToList();
            var k = 0;
            foreach (var i in grp)
            {
                if (k >= news.Count) break;
                next[i] = news[k];
                usedNew[news[k]] = true;
                k++;
            }
        }
        // 2. Existentes ↔ antes: mesmo tipo e chave, pelo mais próximo (os intactos ficam a zero de distância).
        var owner = new int[existentes.Count];
        Array.Fill(owner, -1);
        var takenOld = new bool[antes.Count];
        void Greedy(Func<ItemDetalhe, ItemDetalhe, bool> same, double maxDist)
        {
            var pairs = new List<(double D, int E, int O)>();
            for (int e = 0; e < existentes.Count; e++)
            {
                if (owner[e] >= 0) continue;
                for (int o = 0; o < antes.Count; o++)
                {
                    if (takenOld[o] || !same(existentes[e], antes[o])) continue;
                    var d = existentes[e].Ref.DistanceTo(antes[o].Ref) + Math.Abs(existentes[e].Tamanho - antes[o].Tamanho);
                    if (d <= maxDist) pairs.Add((d, e, o));
                }
            }
            foreach (var (_, e, o) in pairs.OrderBy(p => p.D))
            {
                if (owner[e] >= 0 || takenOld[o]) continue;
                owner[e] = o;
                takenOld[o] = true;
            }
        }
        Greedy((a, b) => a.Tipo == b.Tipo && a.Chave == b.Chave, double.MaxValue);
        // Texto com o conteúdo editado: o texto gerado mais próximo (até 15 mm de papel).
        Greedy((a, b) => a.Tipo == TipoItemDetalhe.Texto && b.Tipo == TipoItemDetalhe.Texto, 15 * mm);
        // 3. Cada existente reencontrado vai para o correspondente da escala nova, com o deslocamento que tinha.
        for (int e = 0; e < existentes.Count; e++)
        {
            var o = owner[e];
            if (o < 0) continue;
            if (next[o] < 0)
            {
                plan.Apagar.Add(e);
                continue;
            }
            var delta = existentes[e].Ref - antes[o].Ref;
            plan.Mover.Add((e, depois[next[o]].Movido(delta)));
        }
        // 4. Itens novos da escala nova (sem correspondente na antiga); os que a pessoa apagou não voltam.
        for (int j = 0; j < depois.Count; j++)
            if (!usedNew[j]) plan.Criar.Add(depois[j]);
        return plan;
    }
}

public enum TipoItemDetalhe { Texto, Linha, Regiao }

/// <summary>
/// Elemento de detalhe para o ajuste de escala: texto (um anel com a posição; chave = conteúdo), segmento de linha (um anel com
/// os dois pontos) ou região (anéis; chave = <see cref="AnnotationScale.ChaveRegiao"/>).
/// </summary>
public sealed record ItemDetalhe(TipoItemDetalhe Tipo, string Chave, IReadOnlyList<IReadOnlyList<Vec2>> Aneis)
{
    /// <summary>O que o gerador produziu (texto, segmento ou <see cref="FonteRegiao"/>) – para criar o elemento quando ele é novo.</summary>
    public object? Fonte { get; init; }

    public static ItemDetalhe Texto(string text, Vec2 position) =>
        new(TipoItemDetalhe.Texto, NormalizarTexto(text), new[] { (IReadOnlyList<Vec2>)new[] { position } });

    /// <summary>Conteúdo do texto sem as diferenças de fim de linha do Revit ("\r") e espaços nas pontas.</summary>
    public static string NormalizarTexto(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary>Ponto de referência: posição do texto, meio do segmento ou centro do retângulo envolvente da região.</summary>
    public Vec2 Ref
    {
        get
        {
            if (Tipo == TipoItemDetalhe.Texto) return Aneis[0][0];
            var pts = Aneis.SelectMany(r => r).ToList();
            return new Vec2((pts.Min(p => p.X) + pts.Max(p => p.X)) / 2, (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2);
        }
    }

    /// <summary>Medida para distinguir itens vizinhos: comprimento do segmento ou diagonal da região (0 no texto).</summary>
    public double Tamanho
    {
        get
        {
            if (Tipo == TipoItemDetalhe.Texto) return 0;
            if (Tipo == TipoItemDetalhe.Linha) return Aneis[0][0].DistanceTo(Aneis[0][^1]);
            var pts = Aneis.SelectMany(r => r).ToList();
            return new Vec2(pts.Max(p => p.X) - pts.Min(p => p.X), pts.Max(p => p.Y) - pts.Min(p => p.Y)).Length;
        }
    }

    public ItemDetalhe Movido(Vec2 d) =>
        d.Length < 1e-12 ? this : this with { Aneis = Aneis.Select(r => (IReadOnlyList<Vec2>)r.Select(p => p + d).ToList()).ToList() };
}

/// <summary>Peças de uma região preenchida (mesma cor, cor própria e hachura).</summary>
public sealed record FonteRegiao(MarkingColor Color, string? Rgb, Hachura Hatch, List<Polygon2> Pecas);

/// <summary>Plano do ajuste de escala de um detalhe (índices dos elementos existentes).</summary>
public sealed class AjusteDeEscala
{
    /// <summary>Elemento existente → nova geometria (texto: só a posição; o conteúdo dele é mantido).</summary>
    public List<(int Existente, ItemDetalhe Novo)> Mover { get; } = new();
    /// <summary>Existentes que não existem mais na escala nova.</summary>
    public List<int> Apagar { get; } = new();
    /// <summary>Itens que aparecem na escala nova e não existiam na antiga.</summary>
    public List<ItemDetalhe> Criar { get; } = new();
}
