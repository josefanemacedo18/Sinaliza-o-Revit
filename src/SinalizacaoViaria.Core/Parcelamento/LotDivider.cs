using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>Área resultante da divisão da quadra: lote, parte de lote, lote de esquina, sobra de testada ou miolo.</summary>
public sealed class Parcela
{
    /// <summary>Chave estável (não muda com a renumeração): "testada:ordem", "testada:ordem.parte", "testada:s", "esq:a-b", "m:k" ou "q".</summary>
    public string Chave { get; init; } = "";
    public Polygon2 Forma { get; set; } = null!;
    public string Categoria { get; set; } = CategoriaLote.Lote;
    public string? TestadaId { get; init; }
    public int Ordem { get; init; }
    public int Parte { get; init; }
    /// <summary>Frente medida ao longo da testada (m).</summary>
    public double Frente { get; set; }
    /// <summary>Profundidade de projeto (m) – a real pode ser menor junto a outra testada ou à divisa.</summary>
    public double Profundidade { get; init; }
    /// <summary>Trecho da testada em frente ao lote.</summary>
    public List<Vec2> LinhaFrente { get; set; } = new();
    /// <summary>Último lote da testada feito com a sobra (lote normal).</summary>
    public bool Sobra { get; init; }
    public bool Esquina { get; init; }
    /// <summary>Chaves dos lotes unidos neste (nulo = não é união).</summary>
    public List<string>? Unidos { get; set; }
    /// <summary>Motivo quando o lote fica abaixo da frente ou da área mínima (nulo = atende).</summary>
    public string? AbaixoDoMinimo { get; set; }
    /// <summary>Número na quadra ("12") ou com prefixo da categoria ("AV-1").</summary>
    public string Numero { get; set; } = "";
    /// <summary>Estaca no perímetro da quadra do meio da frente (ordena a numeração). NaN = sem frente (miolo).</summary>
    public double Posicao { get; set; } = double.NaN;
    public double Area => Forma.Area;
}

/// <summary>Trecho do contorno da quadra entre duas quinas (candidato a testada).</summary>
public sealed class Cadeia
{
    public int Indice { get; init; }
    /// <summary>Pontos do contorno (alinhamento real, com os arcos de esquina).</summary>
    public List<Vec2> Pontos { get; init; } = new();
    /// <summary>
    /// Linha de divisão: o alinhamento com as metades dos arcos de esquina trocadas pelo prolongamento dos lados retos até o
    /// encontro dos alinhamentos – as frentes e as divisas perpendiculares são medidas nela.
    /// </summary>
    public List<Vec2> Divisao { get; init; } = new();
    /// <summary>Estaca (m) do primeiro ponto no perímetro da quadra.</summary>
    public double Inicio { get; init; }
    /// <summary>Comprimento real no contorno (m).</summary>
    public double ComprimentoReal { get; init; }
    /// <summary>Giro (graus, + à esquerda) na quina do fim desta cadeia para a próxima.</summary>
    public double GiroFim { get; init; }
    /// <summary>Testada apontada sobre esta cadeia (nulo = divisa ou lado sem lotes).</summary>
    public string? TestadaId { get; set; }

    private Polyline2? _linha;
    /// <summary>Linha de divisão como polilinha.</summary>
    public Polyline2 Linha => _linha ??= new Polyline2(Divisao.Count >= 2 ? Divisao : Pontos);
    /// <summary>Comprimento da linha de divisão (m).</summary>
    public double Comprimento => Linha.Length;
}

/// <summary>Esquina entre duas testadas consecutivas: onde a escolha do lote de esquina vale.</summary>
public sealed record EsquinaInfo(Vec2 Ponto, string Anterior, string Posterior, double Giro, EsquinaLote Escolha);

/// <summary>
/// Chanfro da quina de esquina: <paramref name="Vertice"/> no encontro dos alinhamentos, a face de <paramref name="A"/> (na testada
/// que chega à esquina) a <paramref name="B"/> (na que sai) e o triângulo que sai do lote.
/// </summary>
public sealed record ChanfroQuina(Vec2 Vertice, Vec2 A, Vec2 B, double Face, Polygon2 Triangulo)
{
    /// <summary>Recuo sobre cada limite do lote, a partir do encontro dos alinhamentos (m) – o cateto.</summary>
    public double Cateto => Vertice.DistanceTo(A);

    /// <summary>O lado <paramref name="a"/>–<paramref name="b"/> é a face deste chanfro (em qualquer sentido).</summary>
    public bool EFace(Vec2 a, Vec2 b, double tol = 0.02) =>
        a.DistanceTo(A) < tol && b.DistanceTo(B) < tol || a.DistanceTo(B) < tol && b.DistanceTo(A) < tol;
}

public sealed class DivisaoQuadra
{
    public Polygon2 Quadra { get; init; } = null!;
    /// <summary>Contorno sem os chanfros (com as quinas vivas); nulo quando a quadra não tem chanfro.</summary>
    public Polygon2? SemChanfro { get; init; }
    public List<Parcela> Parcelas { get; } = new();
    public List<Cadeia> Cadeias { get; } = new();
    public List<EsquinaInfo> Esquinas { get; } = new();
    public List<ChanfroQuina> Chanfros { get; } = new();
    /// <summary>Linhas (modo por linhas) que não fecham nenhum lote – identificadores (UniqueId) para apontar no Revit.</summary>
    public List<string> LinhasSoltas { get; } = new();
    public List<string> Avisos { get; } = new();
}

/// <summary>
/// Divide a quadra em lotes a partir das testadas: lotes de frente padrão (ou personalizada) ao longo de cada testada, com as
/// divisas perpendiculares a ela e o fundo na profundidade dada; esquinas pela escolha do usuário; sobra de testada; miolo;
/// numeração de 1 a N na quadra. As áreas das parcelas somam a área da quadra.
/// </summary>
public static class LotDivider
{
    private const double Far = 4000;
    /// <summary>Lado mais longo que ainda faz parte de um arco de esquina discretizado (m).</summary>
    private const double MaxArcEdge = 5;

    // ------------------------------------------------------------------ cadeias (lados da quadra)

    /// <summary>
    /// Lados da quadra separados nas quinas: vértices vivos (giro ≥ 35°) ou arcos fechados (raio &lt; 25 m) – a curva de uma
    /// via de raio grande não é quina.
    /// </summary>
    public static List<Cadeia> Cadeias(IReadOnlyList<Vec2> ring, double cornerDeg = 35, double maxCornerRadius = 25)
    {
        var r = CleanRing(ring);
        var n = r.Count;
        var res = new List<Cadeia>();
        if (n < 3) return res;
        var turn = new double[n];
        var rad = new double[n];
        for (int i = 0; i < n; i++)
        {
            var a = r[(i - 1 + n) % n];
            var b = r[i];
            var c = r[(i + 1) % n];
            var u = (b - a).Normalized();
            var v = (c - b).Normalized();
            turn[i] = Math.Atan2(u.Cross(v), u.Dot(v)) * 180 / Math.PI;
            var len = ((b - a).Length + (c - b).Length) / 2;
            rad[i] = Math.Abs(turn[i]) < 1e-6 ? double.MaxValue : len / (Math.Abs(turn[i]) * Math.PI / 180);
        }
        // Zonas de quina: vértice vivo (giro ≥ 35°) ou trecho de arco fechado (raio < 25 m), girando para o mesmo lado. O corte
        // fica na metade do giro da zona.
        var inZone = new bool[n];
        for (int i = 0; i < n; i++)
            inZone[i] = Math.Abs(turn[i]) >= cornerDeg || Math.Abs(turn[i]) > 1 && rad[i] < maxCornerRadius;
        var cuts = new List<(int Vertex, double Turn, int First, int Last)>();
        var start = Enumerable.Range(0, n).FirstOrDefault(i => !inZone[i]);
        var step = 0;
        while (step < n)
        {
            var k = (start + step) % n;
            if (!inZone[k]) { step++; continue; }
            var zone = new List<int>();
            var sign = Math.Sign(turn[k]);
            // A zona continua só por lados curtos (arco discretizado); quinas separadas por um lado longo são quinas distintas.
            while (step < n && inZone[(start + step) % n] && Math.Sign(turn[(start + step) % n]) == sign
                   && (zone.Count == 0 || r[zone[^1]].DistanceTo(r[(start + step) % n]) <= MaxArcEdge))
            {
                zone.Add((start + step) % n);
                step++;
            }
            var total = zone.Sum(z => turn[z]);
            if (Math.Abs(total) < cornerDeg) continue;
            // As pontas do arco (giro pequeno, mas por lados curtos) também são do arco: o encontro dos alinhamentos é
            // calculado com os lados retos de fora dele.
            while (zone.Count < n && Math.Sign(turn[(zone[0] - 1 + n) % n]) == sign && Math.Abs(turn[(zone[0] - 1 + n) % n]) > 0.05
                   && r[(zone[0] - 1 + n) % n].DistanceTo(r[zone[0]]) <= MaxArcEdge && !zone.Contains((zone[0] - 1 + n) % n))
                zone.Insert(0, (zone[0] - 1 + n) % n);
            while (zone.Count < n && Math.Sign(turn[(zone[^1] + 1) % n]) == sign && Math.Abs(turn[(zone[^1] + 1) % n]) > 0.05
                   && r[zone[^1]].DistanceTo(r[(zone[^1] + 1) % n]) <= MaxArcEdge && !zone.Contains((zone[^1] + 1) % n))
                zone.Add((zone[^1] + 1) % n);
            total = zone.Sum(z => turn[z]);
            var acc = 0.0;
            var cut = zone[0];
            foreach (var z in zone)
            {
                acc += turn[z];
                cut = z;
                if (Math.Abs(acc) >= Math.Abs(total) / 2 - 1e-9) break;
            }
            cuts.Add((cut, total, zone[0], zone[^1]));
        }
        cuts = cuts.GroupBy(c => c.Vertex).Select(g => g.First()).OrderBy(c => c.Vertex).ToList();
        if (cuts.Count == 0)
        {
            // Quadra sem quinas (circular): dois lados.
            cuts.Add((0, 0, 0, 0));
            cuts.Add((n / 2, 0, n / 2, n / 2));
        }
        else if (cuts.Count == 1)
        {
            var opp = (cuts[0].Vertex + n / 2) % n;
            cuts.Add((opp, 0, opp, opp));
            cuts = cuts.OrderBy(c => c.Vertex).ToList();
        }

        var cum = new double[n + 1];
        for (int i = 0; i < n; i++) cum[i + 1] = cum[i] + r[i].DistanceTo(r[(i + 1) % n]);
        for (int c = 0; c < cuts.Count; c++)
        {
            var za = cuts[c];
            var zb = cuts[(c + 1) % cuts.Count];
            var pts = Walk(r, za.Vertex, zb.Vertex);
            var len = 0.0;
            for (int j = 0; j + 1 < pts.Count; j++) len += pts[j].DistanceTo(pts[j + 1]);
            // Linha de divisão: arcos de esquina trocados pelo encontro dos alinhamentos retos.
            var div = Walk(r, za.Last, zb.First);
            if (div.Count < 2 || za.Last == zb.Vertex || zb.First == za.Vertex) div = new List<Vec2>(pts);
            else
            {
                if (za.First != za.Last && Pi(r, za.First, za.Last) is { } pa) div.Insert(0, pa);
                else div = Walk(r, za.Vertex, zb.First);
                if (zb.First != zb.Last && Pi(r, zb.First, zb.Last) is { } pb) div.Add(pb);
                else div.AddRange(Walk(r, zb.First, zb.Vertex).Skip(1));
            }
            res.Add(new Cadeia { Indice = c, Pontos = pts, Divisao = div, Inicio = cum[za.Vertex], ComprimentoReal = len, GiroFim = zb.Turn });
        }
        return res;
    }

    /// <summary>Vértices do anel de <paramref name="a"/> a <paramref name="b"/> (sentido do anel, inclusive).</summary>
    private static List<Vec2> Walk(IReadOnlyList<Vec2> r, int a, int b)
    {
        var n = r.Count;
        var pts = new List<Vec2> { r[a] };
        var i = a;
        while (i != b)
        {
            i = (i + 1) % n;
            pts.Add(r[i]);
        }
        return pts;
    }

    /// <summary>
    /// Encontro dos alinhamentos retos antes e depois de um arco de esquina (vértices <paramref name="first"/> a
    /// <paramref name="last"/>); nulo se forem paralelos ou o encontro ficar longe demais.
    /// </summary>
    private static Vec2? Pi(IReadOnlyList<Vec2> r, int first, int last)
    {
        var n = r.Count;
        var a0 = r[(first - 1 + n) % n];
        var a1 = r[first];
        var b0 = r[last];
        var b1 = r[(last + 1) % n];
        var u = a1 - a0;
        var v = b1 - b0;
        var den = u.Cross(v);
        if (Math.Abs(den) < 1e-9) return null;
        var t = (b0 - a0).Cross(v) / den;
        var p = a0 + u * t;
        var arc = 0.0;
        for (int i = first; i != last; i = (i + 1) % n) arc += r[i].DistanceTo(r[(i + 1) % n]);
        return p.DistanceTo(a1) <= 3 * arc + 1 && p.DistanceTo(b0) <= 3 * arc + 1 ? p : null;
    }

    /// <summary>Anel sem vértices repetidos nem colineares (tolerância de 1 mm).</summary>
    public static List<Vec2> CleanRing(IReadOnlyList<Vec2> ring)
    {
        var r = new List<Vec2>();
        foreach (var p in ring)
            if (r.Count == 0 || r[^1].DistanceTo(p) > 1e-3) r.Add(p);
        while (r.Count > 1 && r[0].DistanceTo(r[^1]) <= 1e-3) r.RemoveAt(r.Count - 1);
        var changed = true;
        while (changed && r.Count > 3)
        {
            changed = false;
            for (int i = 0; i < r.Count && r.Count > 3; i++)
            {
                var a = r[(i - 1 + r.Count) % r.Count];
                var b = r[i];
                var c = r[(i + 1) % r.Count];
                var ab = b - a;
                var ac = c - a;
                var l = ac.Length;
                if (l < 1e-9 || Math.Abs(ac.Cross(ab)) / l < 1e-3 && ab.Dot(ac) > 0 && ab.Length < l)
                {
                    r.RemoveAt(i);
                    changed = true;
                    i--;
                }
            }
        }
        if (Polygon2.SignedArea(r) < 0) r.Reverse();
        return r;
    }

    // ------------------------------------------------------------------ chanfro das quinas

    /// <summary>A quadra tem chanfro ligado, com alguma face maior que zero (a dela ou a de alguma esquina).</summary>
    public static bool TemChanfro(QuadraDefinition q) => q.Chanfro && !q.Inteira && (q.ChanfroMedidaQuadra > 0.05 || q.Esquinas.Any(e => e.Chanfro > 0.05));

    /// <summary>
    /// Chanfros das quinas da quadra (com <see cref="QuadraDefinition.Chanfro"/> ligado): em cada esquina convexa entre duas
    /// testadas, com a medida da esquina (<see cref="EsquinaLote.Chanfro"/>; 0 = sem chanfro nela) ou a da quadra, os catetos
    /// iguais ao longo das duas testadas a partir do encontro dos alinhamentos – a medida é o próprio cateto, sobre cada limite
    /// (<see cref="MedidaChanfro.Catetos"/>), ou a face (hipotenusa, <see cref="MedidaChanfro.Face"/>). Vazio sem chanfro.
    /// </summary>
    public static List<ChanfroQuina> Chanfros(QuadraDefinition q)
    {
        if (!TemChanfro(q) || q.Polygon is not { IsValid: true } poly) return new();
        var chains = Cadeias(CleanRing(poly.Outer));
        var ids = new Dictionary<Cadeia, string>();
        foreach (var t in q.Testadas)
            if (CadeiaEm(chains, t.Ponto) is { } c && !ids.ContainsKey(c)) ids[c] = t.Id;
        return Chanfros(q, chains, c => ids.GetValueOrDefault(c));
    }

    /// <summary>
    /// Largura da calçada na quina <paramref name="p"/> do lote – o padrão da face do chanfro: a menor das calçadas das vias cujo
    /// alinhamento passa pela quina (até <paramref name="tol"/> m); nula sem via.
    /// </summary>
    public static double? CalcadaNaQuina(IEnumerable<Generators.IntersectionRoad> roads, Vec2 p, double tol = 1.0)
    {
        double? best = null;
        foreach (var r in roads)
        {
            var signed = r.Axis.Project(p).Signed;
            var (edge, walk) = signed >= 0 ? (r.Def.TotalLeft, r.Def.LeftSidewalk) : (r.Def.TotalRight, r.Def.RightSidewalk);
            if (walk < 0.05 || Math.Abs(Math.Abs(signed) - edge) > tol) continue;
            best = best == null ? walk : Math.Min(best.Value, walk);
        }
        return best;
    }

    /// <summary>Triângulos dos chanfros das quadras do projeto (perto de <paramref name="near"/>, quando dado).</summary>
    public static List<Polygon2> Triangulos(IEnumerable<MarkingDefinition> all, Vec2? near = null, double radius = 60) =>
        all.OfType<QuadraDefinition>().Where(TemChanfro).SelectMany(Chanfros)
            .Where(c => near == null || c.Vertice.DistanceTo(near.Value) <= radius).Select(c => c.Triangulo).ToList();

    private static List<ChanfroQuina> Chanfros(QuadraDefinition q, IReadOnlyList<Cadeia> chains, Func<Cadeia, string?> testada)
    {
        var res = new List<ChanfroQuina>();
        if (!TemChanfro(q) || chains.Count < 2) return res;
        for (int i = 0; i < chains.Count; i++)
        {
            var c = chains[i];
            var nx = chains[(i + 1) % chains.Count];
            if (testada(c) == null || testada(nx) == null || c.GiroFim <= 1) continue;
            var corner = c.Pontos[^1];
            var esq = q.Esquinas.Where(e => e.Ponto.DistanceTo(corner) < 5).OrderBy(e => e.Ponto.DistanceTo(corner)).FirstOrDefault();
            var medida = esq?.Chanfro ?? q.ChanfroMedidaQuadra;
            if (medida < 0.05) continue;
            var la = c.Linha;
            var lb = nx.Linha;
            var v = la.Points[^1];
            var ua = (la.Points[^2] - v).Normalized();
            var ub = (lb.Points[1] - v).Normalized();
            var half = Math.Acos(Math.Clamp(ua.Dot(ub), -1, 1)) / 2;
            if (half < 1e-3) continue;
            // Catetos iguais: pelos catetos, a própria medida sobre cada limite; pela face, face = 2 × cateto × sen(ângulo/2). No
            // máximo 45 % de cada testada.
            var cateto = q.ChanfroMedida == MedidaChanfro.Catetos ? medida : medida / (2 * Math.Sin(half));
            var leg = Math.Min(cateto, 0.45 * Math.Min(la.Length, lb.Length));
            var a = la.PointAt(la.Length - leg);
            var b = lb.PointAt(leg);
            var tri = new Polygon2(new[] { a, v, b });
            if (tri.Area < 1e-4) continue;
            res.Add(new ChanfroQuina(v, a, b, a.DistanceTo(b), tri));
        }
        return res;
    }

    /// <summary>Cadeia mais próxima do ponto.</summary>
    public static Cadeia? CadeiaEm(IReadOnlyList<Cadeia> cadeias, Vec2 p) =>
        cadeias.OrderBy(c => Math.Abs(new Polyline2(c.Pontos).Project(p).Signed)).FirstOrDefault();

    // ------------------------------------------------------------------ divisão

    private sealed class Slot
    {
        public Cadeia Chain = null!;
        public TestadaLote Testada = null!;
        public double A, B;
        public bool OpenA, OpenB;
        public double Depth;
        public string Key = "";
        public int Ordem, Parte;
        public bool Sobra, Corner;
        /// <summary>Prioridade de cálculo: lotes de esquina primeiro.</summary>
        public int Pass;
        public int ChainRank;
        public Polygon2[]? Exclude;
    }

    /// <summary>Divide a quadra com as categorias e os mínimos do loteamento.</summary>
    public static DivisaoQuadra Divide(QuadraDefinition q, LoteamentoDefinition? lot) =>
        Divide(q, lot?.Categorias, lot?.FrenteMinimaLote ?? 5, lot?.AreaMinimaLote ?? 125);

    /// <summary>
    /// Divide a quadra. <paramref name="categorias"/> define a numeração (categorias com prefixo); a frente e a área mínimas
    /// decidem se a sobra de testada vira lote e marcam os lotes abaixo do mínimo.
    /// </summary>
    public static DivisaoQuadra Divide(QuadraDefinition q, IReadOnlyList<CategoriaLote>? categorias = null, double frenteMinimaLote = 5, double areaMinimaLote = 125)
    {
        categorias ??= CategoriaLote.Padrao();
        var poly = q.Polygon;
        var res = new DivisaoQuadra { Quadra = poly ?? new Polygon2(new[] { Vec2.Zero, Vec2.UnitX, Vec2.UnitY }) };
        if (poly == null || !poly.IsValid)
        {
            res.Avisos.Add($"Quadra {q.Nome}: contorno inválido.");
            return res;
        }
        var quad = new Polygon2(CleanRing(poly.Outer), poly.Holes);
        res = new DivisaoQuadra { Quadra = quad };
        var chains = Cadeias(quad.Outer);
        res.Cadeias.AddRange(chains);

        if (q.Inteira)
        {
            res.Parcelas.Add(new Parcela { Chave = "q", Forma = quad, Categoria = q.CategoriaPadrao });
            Numerar(res, q, categorias);
            return res;
        }

        // Testadas → cadeias (a primeira testada que cair numa cadeia fica com ela).
        var chainOf = new Dictionary<string, Cadeia>();
        var rank = new Dictionary<Cadeia, int>();
        foreach (var t in q.Testadas)
        {
            var c = CadeiaEm(chains, t.Ponto);
            if (c == null || c.TestadaId != null) continue;
            c.TestadaId = t.Id;
            chainOf[t.Id] = c;
            rank[c] = rank.Count;
        }
        // Lotes pelas linhas desenhadas: cada região fechada é um lote (numeração, categorias, mínimos e uniões como sempre).
        if (q.Divisao == ModoDivisao.Linhas)
        {
            PorLinhas(res, q, quad, chains);
            // Chanfro das quinas também nos lotes por linhas: a face sai de cada lote que ocupa a quina (as linhas são lidas
            // com as quinas vivas; o chanfro vale enquanto estiver ligado na quadra, seja qual for o modo de divisão).
            var cl = Chanfros(q, chains, c => c.TestadaId);
            if (cl.Count > 0)
            {
                res.Chanfros.AddRange(cl);
                foreach (var p in res.Parcelas)
                    if (Largest(PolygonOps.Difference(new[] { p.Forma }, cl.Select(c => c.Triangulo))) is { } cutP && cutP.Area < p.Forma.Area - 1e-6)
                        p.Forma = new Polygon2(CleanRing(cutP.Outer), cutP.Holes);
            }
            return Fecha(res, q, categorias, frenteMinimaLote, areaMinimaLote);
        }
        if (chainOf.Count == 0)
        {
            res.Avisos.Add($"Quadra {q.Nome}: nenhuma testada apontada – a quadra ficou numa área só ({q.MioloCategoria}).");
            res.Parcelas.Add(new Parcela { Chave = "q", Forma = quad, Categoria = q.MioloCategoria });
            Numerar(res, q, categorias);
            return res;
        }
        // Chanfro das quinas de esquina: o triângulo sai da quadra (a calçada da interseção fica com ele); as frentes e as divisas
        // continuam medidas a partir do encontro dos alinhamentos.
        var chanfros = Chanfros(q, chains, c => c.TestadaId);
        var sharp = quad;
        if (chanfros.Count > 0 && Largest(PolygonOps.Difference(new[] { quad }, chanfros.Select(c => c.Triangulo))) is { } cut)
        {
            quad = new Polygon2(CleanRing(cut.Outer), cut.Holes);
            res = new DivisaoQuadra { Quadra = quad, SemChanfro = sharp };
            res.Cadeias.AddRange(chains);
            res.Chanfros.AddRange(chanfros);
        }
        var testada = q.Testadas.Where(t => chainOf.ContainsKey(t.Id)).ToDictionary(t => t.Id);
        var nC = chains.Count;
        Cadeia Next(Cadeia c) => chains[(c.Indice + 1) % nC];

        // Profundidade de cada testada (fundo no meio da quadra entre testadas opostas próximas).
        var depth = new Dictionary<Cadeia, double>();
        foreach (var (id, c) in chainOf)
        {
            var d = Math.Max(0.5, testada[id].Profundidade ?? q.Profundidade);
            // Com o miolo para os lotes, as fileiras opostas se encontram no meio mesmo além da profundidade dada.
            if (q.FundoNoMeio && OppositeTestada(quad, chains, c) is { } w && (w < 2 * d - 1e-6 || q.Miolo == ModoMiolo.AosLotes && testada[id].Profundidade == null)) d = w / 2;
            depth[c] = d;
        }

        // Esquinas entre testadas: quem fica com o lote de esquina.
        var range = chainOf.Values.ToDictionary(c => c, c => (A: 0.0, B: c.Comprimento));
        var openStart = chainOf.Values.ToDictionary(c => c, _ => false);
        var openEnd = chainOf.Values.ToDictionary(c => c, _ => false);
        // Ponta encurtada pela esquina de outra testada: o lote vai até o fundo dos lotes dela (a faixa dela é descontada).
        var excludeStart = new Dictionary<Cadeia, Polygon2[]>();
        // Testada dona da esquina do fim: a sobra fica antes do lote de esquina (que continua inteiro).
        var ownsEndCorner = new HashSet<Cadeia>();
        var excludeEnd = new Dictionary<Cadeia, Polygon2[]>();
        var cornerLots = new List<(Polygon2 Shape, string Key, Cadeia Prev, Cadeia NextC, double FPrev, double FNext, Vec2 V)>();
        foreach (var c in chains)
        {
            var nx = Next(c);
            var convex = c.GiroFim > 1;
            if (c.TestadaId == null && nx.TestadaId == null) continue;
            if (c.TestadaId == null || nx.TestadaId == null)
            {
                // Testada encontrando uma divisa: o lote vai até a divisa (quina convexa).
                if (convex)
                {
                    if (c.TestadaId != null) openEnd[c] = true;
                    if (nx.TestadaId != null) openStart[nx] = true;
                }
                continue;
            }
            if (!convex) continue;
            var v = c.Pontos[^1];
            var choice = q.Esquinas.Where(e => e.Ponto.DistanceTo(v) < 5).OrderBy(e => e.Ponto.DistanceTo(v)).FirstOrDefault()?.Clone()
                         ?? new EsquinaLote { Ponto = v, Modo = ModoEsquina.Prioridade };
            res.Esquinas.Add(new EsquinaInfo(v, c.TestadaId, nx.TestadaId, c.GiroFim, choice));
            var owner = choice.Modo switch
            {
                ModoEsquina.TestadaAnterior => c,
                ModoEsquina.TestadaPosterior => nx,
                ModoEsquina.MedidaPropria => null,
                _ => rank[c] <= rank[nx] ? c : nx,
            };
            if (owner == c)
            {
                openEnd[c] = true;
                ownsEndCorner.Add(c);
                var band = Band(c.Linha, depth[c]);
                var s0 = ExitStation(nx.Linha, band, fromStart: true);
                range[nx] = (Math.Max(range[nx].A, s0), range[nx].B);
                openStart[nx] = true;
                excludeStart[nx] = band;
            }
            else if (owner == nx)
            {
                openStart[nx] = true;
                var band = Band(nx.Linha, depth[nx]);
                var s1 = ExitStation(c.Linha, band, fromStart: false);
                range[c] = (range[c].A, Math.Min(range[c].B, s1));
                openEnd[c] = true;
                excludeEnd[c] = band;
            }
            else
            {
                // Lote de esquina com medida própria: frente em cada testada; as duas testadas começam depois dele.
                var fa = Math.Clamp(choice.FrenteAnterior, 0.5, c.Comprimento);
                var fb = Math.Clamp(choice.FrentePosterior, 0.5, nx.Comprimento);
                var la = c.Linha;
                var lb = nx.Linha;
                var pa = la.PointAt(c.Comprimento - fa);
                var na = Normal(la, c.Comprimento - fa);
                var pb = lb.PointAt(fb);
                var nb = Normal(lb, fb);
                var pts = new List<Vec2>(la.SubPoints(c.Comprimento - fa, c.Comprimento));
                pts.AddRange(lb.SubPoints(0, fb).Skip(1));
                if (RayHit(pa, na, pb, nb) is { } apex) pts.Add(apex);
                else
                {
                    pts.Add(pb + nb * Math.Max(fa, depth[nx]));
                    pts.Add(pa + na * Math.Max(fb, depth[c]));
                }
                var shape = Largest(PolygonOps.Intersect(new[] { new Polygon2(pts) }, new[] { quad }));
                if (shape != null)
                    cornerLots.Add((shape, $"esq:{c.TestadaId}-{nx.TestadaId}", c, nx, fa, fb, v));
                range[c] = (range[c].A, Math.Min(range[c].B, c.Comprimento - fa));
                range[nx] = (Math.Max(range[nx].A, fb), range[nx].B);
            }
        }

        // Lotes ao longo de cada testada.
        var slots = new List<Slot>();
        foreach (var (id, c) in chainOf.OrderBy(kv => rank[kv.Value]))
        {
            var t = testada[id];
            var (a0, b0) = range[c];
            var f = Math.Max(0.5, t.Frente ?? q.Frente);
            if (b0 - a0 < 0.05)
            {
                res.Avisos.Add($"Quadra {q.Nome}: a testada {id} ficou toda com os lotes de esquina.");
                continue;
            }
            var stations = new List<(double A, double B, int Ordem, double Depth, bool Sobra, int Partes, bool Custom)>();
            var s = a0;
            var k = 1;
            while (true)
            {
                var aj = q.Ajuste($"{id}:{k}");
                var fk = aj?.Frente is > 0.1 ? aj.Frente.Value : f;
                if (s + fk > b0 + 1e-6) break;
                stations.Add((s, s + fk, k, aj?.Profundidade is > 0.1 ? aj.Profundidade.Value : depth[c], false, Math.Max(1, aj?.Partes ?? 1), aj?.Frente != null));
                s += fk;
                k++;
            }
            var rest = b0 - s;
            // A sobra vira lote quando atinge a frente e a área mínimas (as do loteamento ou a frente da quadra, a maior).
            var minF = Math.Max(frenteMinimaLote, q.FrenteMinima);
            var restOk = rest >= minF - 1e-6 && RestArea(quad, c, s, b0, rest, depth[c], ownsEndCorner.Contains(c) && b0 >= c.Comprimento - 1e-6 && stations.Count > 0
                ? stations[^1].B - stations[^1].A : 0) >= areaMinimaLote - 1e-6;
            if (rest > 1e-3)
            {
                // Nenhum lote inteiro coube, ou a quadra pede lote a mais a partir de uma frente dada: lote comum com o resto.
                if (stations.Count == 0 || q.FrenteMinima > 0 && restOk)
                {
                    var aj = q.Ajuste($"{id}:{k}");
                    stations.Add((s, b0, k, aj?.Profundidade is > 0.1 ? aj.Profundidade.Value : depth[c], false, Math.Max(1, aj?.Partes ?? 1), true));
                }
                else
                {
                    var mode = q.Sobra == ModoSobra.LoteSobra && !restOk ? ModoSobra.IncorporarUltimo : q.Sobra;
                    if (q.Sobra == ModoSobra.LoteSobra && !restOk)
                        res.Avisos.Add($"Quadra {q.Nome}, testada {id}: sobra de {LotReport.M(rest)} m (menor que a frente mínima de {LotReport.M(minF)} m ou que a área mínima de {LotReport.M(areaMinimaLote)} m²) somada ao último lote.");
                    switch (mode)
                    {
                        case ModoSobra.IncorporarUltimo:
                        {
                            var l = stations[^1];
                            stations[^1] = l with { B = b0 };
                            break;
                        }
                        case ModoSobra.Distribuir:
                        {
                            var free = stations.Count(x => !x.Custom);
                            var add = rest / Math.Max(1, free > 0 ? free : stations.Count);
                            var pos = a0;
                            for (int i = 0; i < stations.Count; i++)
                            {
                                var w = stations[i].B - stations[i].A + (free == 0 || !stations[i].Custom ? add : 0);
                                stations[i] = stations[i] with { A = pos, B = pos + w };
                                pos += w;
                            }
                            stations[^1] = stations[^1] with { B = b0 };
                            break;
                        }
                        default:
                        {
                            // Lote da sobra (lote normal): o último da testada – antes do lote de esquina quando a testada fica com a
                            // esquina do fim.
                            if (ownsEndCorner.Contains(c) && b0 >= c.Comprimento - 1e-6 && stations.Count > 0)
                            {
                                var last = stations[^1];
                                var w = last.B - last.A;
                                stations[^1] = last with { A = s - w + rest, B = b0 };
                                stations.Insert(stations.Count - 1, (s - w, s - w + rest, 0, depth[c], true, 1, false));
                            }
                            else stations.Add((s, b0, 0, depth[c], true, 1, false));
                            break;
                        }
                    }
                }
            }

            for (int i = 0; i < stations.Count; i++)
            {
                var st = stations[i];
                var isFirst = i == 0;
                var isLast = i == stations.Count - 1;
                var baseKey = st.Sobra ? $"{id}:s" : $"{id}:{st.Ordem}";
                var parts = st.Sobra ? 1 : st.Partes;
                for (int p = 0; p < parts; p++)
                {
                    var pa = st.A + (st.B - st.A) * p / parts;
                    var pb = st.A + (st.B - st.A) * (p + 1) / parts;
                    var oa = isFirst && p == 0 && openStart[c] && pa <= range[c].A + 1e-6;
                    var ob = isLast && p == parts - 1 && openEnd[c] && pb >= range[c].B - 1e-6;
                    var corner = (oa && range[c].A <= 1e-6) || (ob && range[c].B >= c.Comprimento - 1e-6);
                    slots.Add(new Slot
                    {
                        Chain = c, Testada = testada[id], A = pa, B = pb, OpenA = oa, OpenB = ob, Depth = st.Depth,
                        Key = parts > 1 ? $"{baseKey}.{p + 1}" : baseKey, Ordem = st.Ordem, Parte = parts > 1 ? p + 1 : 0,
                        Sobra = st.Sobra, Corner = corner, Pass = corner ? 0 : 1, ChainRank = rank[c],
                        Exclude = oa && excludeStart.TryGetValue(c, out var ea) ? ea : ob && excludeEnd.TryGetValue(c, out var eb) ? eb : null,
                    });
                }
            }
        }

        // Regiões: esquinas com medida própria, depois lotes de esquina, depois os demais (por prioridade e ao longo da testada).
        var assigned = new List<Polygon2>();
        // Alcance de cada lote (para os fundos até a divisa): a faixa lateral dele, sem limite de profundidade.
        var reach = new Dictionary<Parcela, (Polyline2 Line, double A, double B, bool OpenA, bool OpenB)>();
        var quadArr = new[] { quad };
        foreach (var cl in cornerLots)
        {
            var shape = Largest(PolygonOps.Difference(new[] { cl.Shape }, assigned));
            if (shape == null) continue;
            assigned.Add(shape);
            var front = new List<Vec2>(cl.Prev.Linha.SubPoints(cl.Prev.Comprimento - cl.FPrev, cl.Prev.Comprimento));
            front.AddRange(cl.NextC.Linha.SubPoints(0, cl.FNext).Skip(1));
            var aj = q.Ajuste(cl.Key);
            res.Parcelas.Add(new Parcela
            {
                Chave = cl.Key, Forma = shape, Categoria = aj?.Categoria ?? q.CategoriaPadrao, TestadaId = cl.Prev.TestadaId,
                Frente = cl.FPrev + cl.FNext, Profundidade = Math.Max(cl.FPrev, cl.FNext), LinhaFrente = front, Esquina = true,
                Posicao = Perimeter(sharp, cl.Prev.Inicio + cl.Prev.Comprimento),
            });
        }
        foreach (var sl in slots.OrderBy(x => x.Pass).ThenBy(x => x.ChainRank).ThenBy(x => x.A))
        {
            var line = sl.Chain.Linha;
            var sector = Sector(line, sl.A, sl.B, sl.OpenA, sl.OpenB, sl.Depth);
            var region = PolygonOps.Intersect(PolygonOps.Intersect(new[] { sector }, Band(line, sl.Depth)), quadArr);
            if (sl.Exclude != null) region = PolygonOps.Difference(region, sl.Exclude);
            var shape = Largest(PolygonOps.Difference(region, assigned));
            if (shape == null || shape.Area < 0.01)
            {
                res.Avisos.Add($"Quadra {q.Nome}: lote {sl.Key} sem área (ocupado por outro lote).");
                continue;
            }
            assigned.Add(shape);
            var cat = q.Ajuste(sl.Key)?.Categoria
                      ?? (sl.Parte > 0 ? q.Ajuste($"{sl.Testada.Id}:{sl.Ordem}")?.Categoria : null)
                      ?? q.CategoriaPadrao;
            var parcel = new Parcela
            {
                Chave = sl.Key, Forma = shape, Categoria = cat, TestadaId = sl.Testada.Id, Ordem = sl.Ordem, Parte = sl.Parte,
                Frente = sl.B - sl.A, Profundidade = sl.Depth, LinhaFrente = line.SubPoints(sl.A, sl.B), Sobra = sl.Sobra,
                Esquina = sl.Corner, Posicao = Perimeter(sharp, sl.Chain.Inicio + (sl.A + sl.B) / 2),
            };
            res.Parcelas.Add(parcel);
            var baseKey = sl.Parte > 0 ? $"{sl.Testada.Id}:{sl.Ordem}" : sl.Key;
            if (q.Ajuste(baseKey)?.Profundidade == null)
                reach[parcel] = (line, sl.A, sl.B, sl.OpenA && sl.A <= 1e-6, sl.OpenB && sl.B >= sl.Chain.Comprimento - 1e-6);
        }

        // Miolo: o que sobrou da quadra. Para os lotes (padrão): cada lote fica com o miolo atrás dele (fundos até a divisa ou até
        // outros lotes); o que nenhum alcança vai para o vizinho com a maior divisa. Área própria: vira uma área na categoria escolhida.
        var miolo = PolygonOps.Difference(quadArr, assigned).Where(p => p.Area > 1e-6).OrderByDescending(p => p.Area).ToList();
        if (q.Miolo == ModoMiolo.AosLotes && miolo.Count > 0)
        {
            foreach (var p in res.Parcelas.ToList())
            {
                if (!reach.TryGetValue(p, out var rc) || miolo.Count == 0) continue;
                var far = Sector(rc.Line, rc.A, rc.B, rc.OpenA, rc.OpenB, Far / 8);
                if (!PolygonOps.Intersect(miolo, new[] { far }).Any(x => x.Area > 1e-4)) continue;
                // A faixa do lote segue para os fundos só enquanto está livre na largura toda: para no primeiro lote que a
                // atravessa (sem lotes em L).
                var (fmn, fmx) = far.Bounds;
                var others = res.Parcelas.Where(o => !ReferenceEquals(o, p)).Select(o => o.Forma).Where(o =>
                {
                    var (mn, mx) = o.Bounds;
                    return mn.X <= fmx.X && mx.X >= fmn.X && mn.Y <= fmx.Y && mx.Y >= fmn.Y;
                }).ToList();
                var stop = Far / 8;
                if (others.Count > 0)
                {
                    var front = FrontLine(rc.Line, rc.A, rc.B);
                    foreach (var b in PolygonOps.Intersect(new[] { far }, others).Where(x => x.Area > 1e-3))
                        foreach (var v in b.Outer) stop = Math.Min(stop, Math.Abs(front.Project(v).Signed));
                }
                if (stop <= p.Profundidade + 1e-3) continue;
                var parts = PolygonOps.Intersect(miolo, new[] { Sector(rc.Line, rc.A, rc.B, rc.OpenA, rc.OpenB, stop) }).Where(x => x.Area > 1e-4).ToList();
                if (parts.Count == 0) continue;
                var mine = PolygonOps.Union(parts.Append(p.Forma)).OrderByDescending(x => x.Area).First();
                // Só as partes que encostam no lote (as que ficam atrás de outro lote não pulam por cima dele).
                var taken = PolygonOps.Intersect(new[] { mine }, parts).Where(x => x.Area > 1e-4).ToList();
                if (taken.Count == 0) continue;
                p.Forma = mine.Simplified(0.001, 1e-7) ?? mine;
                miolo = PolygonOps.Difference(miolo, taken).Where(x => x.Area > 1e-6).ToList();
            }
        }
        var mk = 1;
        foreach (var m in miolo.OrderByDescending(x => x.Area))
        {
            // Lascas e pedaços menores que um lote mínimo vão para o vizinho com a maior divisa; o resto vira área própria.
            if ((m.Area < 1.0 || q.Miolo == ModoMiolo.AosLotes && m.Area < areaMinimaLote) && MergeIntoNeighbour(res.Parcelas, m)) continue;
            var key = $"m:{mk++}";
            var cat = q.Ajuste(key)?.Categoria ?? q.MioloCategoria;
            res.Parcelas.Add(new Parcela { Chave = key, Forma = m, Categoria = cat });
            if (q.Miolo == ModoMiolo.AosLotes)
                res.Avisos.Add($"Quadra {q.Nome}: miolo de {LotReport.M(m.Area)} m² que nenhum lote alcança pelos fundos ficou como {cat} " +
                               "(mude a categoria em Lote ou una a um lote vizinho com Unir Lotes).");
        }
        return Fecha(res, q, categorias, frenteMinimaLote, areaMinimaLote);
    }

    /// <summary>Uniões, lotes abaixo do mínimo e numeração (comum aos dois modos de divisão).</summary>
    private static DivisaoQuadra Fecha(DivisaoQuadra res, QuadraDefinition q, IReadOnlyList<CategoriaLote> categorias, double frenteMinimaLote, double areaMinimaLote)
    {
        var naoUnidos = Unir(res, q);
        foreach (var p in res.Parcelas.Where(p => string.Equals(p.Categoria, CategoriaLote.Lote, StringComparison.OrdinalIgnoreCase)))
        {
            var why = new List<string>();
            if (p.LinhaFrente.Count < 2) why.Add("sem frente para via");
            else if (p.Frente < frenteMinimaLote - 1e-6) why.Add($"frente {LotReport.M(p.Frente)} m < {LotReport.M(frenteMinimaLote)} m");
            if (p.Area < areaMinimaLote - 1e-6) why.Add($"área {LotReport.M(p.Area)} m² < {LotReport.M(areaMinimaLote)} m²");
            p.AbaixoDoMinimo = why.Count > 0 ? string.Join("; ", why) : null;
        }
        Numerar(res, q, categorias);
        foreach (var g in naoUnidos)
            res.Avisos.Add($"Quadra {q.Nome}: os lotes {string.Join(" + ", g.Select(p => p.Numero))} não são vizinhos – não foram unidos.");
        foreach (var p in res.Parcelas.Where(p => p.AbaixoDoMinimo != null))
            res.Avisos.Add($"Quadra {q.Nome}, lote {p.Numero}: abaixo do mínimo ({p.AbaixoDoMinimo}).");
        return res;
    }

    /// <summary>
    /// Lotes pelas linhas desenhadas: as regiões fechadas da quadra (<see cref="DivisaoPorLinhas"/>). A frente de cada lote é o
    /// trecho mais longo do contorno dele sobre o contorno da quadra – de preferência numa testada; lote que encosta em duas
    /// testadas é de esquina; a posição da frente no contorno ordena a numeração.
    /// </summary>
    private static void PorLinhas(DivisaoQuadra res, QuadraDefinition q, Polygon2 quad, List<Cadeia> chains)
    {
        var linhas = q.LinhasLotes.Where(l => l.Pontos.Count >= 2)
            .Select((l, i) => new LinhaDivisa(string.IsNullOrEmpty(l.Elemento) ? $"#{i + 1}" : l.Elemento, l.Pontos)).ToList();
        var div = DivisaoPorLinhas.Dividir(quad, linhas);
        res.Avisos.AddRange(div.Avisos.Select(a => $"Quadra {q.Nome}: {a}"));
        res.LinhasSoltas.AddRange(div.LinhasSoltas);
        if (div.LinhasSoltas.Count > 0)
            res.Avisos.Add($"Quadra {q.Nome}: {div.LinhasSoltas.Count} linha(s) não fecham nenhum lote (soltas, fora da quadra ou paradas a mais de " +
                           "0,50 m de outra linha) – estão selecionadas no Revit.");
        var ring = quad.Outer;
        var outer = new Polygon2(ring);
        foreach (var r in div.Regioes)
        {
            // Trechos do contorno do lote sobre o contorno da quadra, separados por cadeia (lado da quadra).
            var runs = new List<(List<Vec2> Pts, Cadeia? Chain)>();
            var o = r.Forma.Outer;
            for (int i = 0; i < o.Count; i++)
            {
                var a = o[i];
                var b = o[(i + 1) % o.Count];
                var mid = (a + b) / 2;
                if (outer.DistanceTo(mid) > 1e-3) continue;
                var c = CadeiaEm(chains, mid);
                if (runs.Count > 0 && runs[^1].Chain == c && runs[^1].Pts[^1].DistanceTo(a) < 1e-6) runs[^1].Pts.Add(b);
                else runs.Add((new List<Vec2> { a, b }, c));
            }
            if (runs.Count > 1 && runs[0].Chain == runs[^1].Chain && runs[^1].Pts[^1].DistanceTo(runs[0].Pts[0]) < 1e-6)
            {
                runs[^1].Pts.AddRange(runs[0].Pts.Skip(1));
                runs.RemoveAt(0);
            }
            static double Len(List<Vec2> p) { var s = 0.0; for (int i = 0; i + 1 < p.Count; i++) s += p[i].DistanceTo(p[i + 1]); return s; }
            var rankOf = q.Testadas.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
            var front = runs.Where(x => Len(x.Pts) > 0.05)
                .OrderBy(x => x.Chain?.TestadaId is { } id && rankOf.TryGetValue(id, out var k) ? k : int.MaxValue)
                .ThenByDescending(x => Len(x.Pts)).Select(x => ((List<Vec2>, Cadeia?)?)x).FirstOrDefault();
            var testadas = runs.Where(x => x.Chain?.TestadaId != null && Len(x.Pts) >= 1).Select(x => x.Chain).Distinct().Count();
            var lados = runs.Where(x => Len(x.Pts) >= 1).Select(x => x.Chain).Distinct().Count();
            var lf = front?.Item1 ?? new List<Vec2>();
            res.Parcelas.Add(new Parcela
            {
                Chave = r.Chave, Forma = r.Forma, Categoria = q.Ajuste(r.Chave)?.Categoria ?? q.CategoriaPadrao, TestadaId = front?.Item2?.TestadaId,
                Frente = lf.Count >= 2 ? Len(lf) : 0, LinhaFrente = lf, Esquina = q.Testadas.Count > 0 ? testadas >= 2 : lados >= 2,
                Posicao = lf.Count >= 2 ? Estaca(ring, new Polyline2(lf).PointAt(Len(lf) / 2)) : double.NaN,
            });
        }
    }

    /// <summary>
    /// Categoria do lote <paramref name="p"/> (pelo pincel ou pela janela Lote): gravada no ajuste da chave dele; a categoria
    /// padrão da quadra (a do miolo nas áreas de miolo) não é gravada.
    /// </summary>
    public static void DefineCategoria(QuadraDefinition q, Parcela p, string categoria)
    {
        var baseKey = p.Chave.Contains('.') ? p.Chave[..p.Chave.IndexOf('.')] : p.Chave;
        var padrao = p.Chave.StartsWith("m:") || p.Chave == "q" && !q.Inteira ? q.MioloCategoria : q.CategoriaPadrao;
        q.AjusteOuNovo(p.Chave).Categoria = string.Equals(categoria, padrao, StringComparison.OrdinalIgnoreCase) && p.Chave == baseKey ? null : categoria;
        q.Ajustes.RemoveAll(x => x.Frente == null && x.Profundidade == null && x.Partes <= 1 && x.Categoria == null);
    }

    /// <summary>Estaca (m) do ponto no perímetro do anel (a partir do primeiro vértice).</summary>
    private static double Estaca(IReadOnlyList<Vec2> ring, Vec2 p)
    {
        double best = double.MaxValue, at = 0, cum = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var ab = ring[(i + 1) % ring.Count] - a;
            var l = ab.Length;
            var t = l < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / (l * l), 0, 1);
            var d = p.DistanceTo(a + ab * t);
            if (d < best) { best = d; at = cum + t * l; }
            cum += l;
        }
        return at;
    }

    /// <summary>
    /// Lotes unidos: cada grupo de lotes vizinhos vira um lote só (chave e categoria do primeiro, frentes somadas). Grupos com
    /// lotes que não se tocam ficam como estão (devolvidos para o aviso, depois da numeração).
    /// </summary>
    private static List<List<Parcela>> Unir(DivisaoQuadra res, QuadraDefinition q)
    {
        var failed = new List<List<Parcela>>();
        foreach (var group in q.Unioes.Where(g => g.Count >= 2))
        {
            var members = group.Select(k => res.Parcelas.FirstOrDefault(p => p.Chave == k)).Where(p => p != null).Cast<Parcela>().Distinct().ToList();
            if (members.Count < 2) continue;
            var union = PolygonOps.Union(members.Select(m => m.Forma)).Where(x => x.Area > 1e-6).ToList();
            if (union.Count != 1)
            {
                failed.Add(members);
                continue;
            }
            var first = members[0];
            var ordered = members.Where(m => !double.IsNaN(m.Posicao)).OrderBy(m => m.Posicao).ToList();
            var front = new List<Vec2>();
            foreach (var m in ordered.Where(m => m.TestadaId == first.TestadaId))
                front.AddRange(front.Count > 0 && m.LinhaFrente.Count > 0 && front[^1].DistanceTo(m.LinhaFrente[0]) < 1e-3 ? m.LinhaFrente.Skip(1) : m.LinhaFrente);
            var merged = new Parcela
            {
                Chave = first.Chave, Forma = union[0].Simplified(0.001, 1e-7) ?? union[0], Categoria = first.Categoria, TestadaId = first.TestadaId,
                Ordem = first.Ordem, Parte = first.Parte, Frente = members.Sum(m => m.Frente), Profundidade = members.Max(m => m.Profundidade),
                LinhaFrente = front.Count >= 2 ? front : first.LinhaFrente, Esquina = members.Any(m => m.Esquina),
                Posicao = ordered.Count > 0 ? ordered[0].Posicao : double.NaN, Unidos = members.Select(m => m.Chave).ToList(),
            };
            var at = res.Parcelas.IndexOf(first);
            foreach (var m in members) res.Parcelas.Remove(m);
            res.Parcelas.Insert(Math.Min(at, res.Parcelas.Count), merged);
        }
        return failed;
    }

    /// <summary>Distância do meio da testada, para dentro, até outra testada do outro lado da quadra (nulo = não há).</summary>
    private static double? OppositeTestada(Polygon2 quad, IReadOnlyList<Cadeia> chains, Cadeia c)
    {
        var line = c.Linha;
        var mid = line.PointAt(c.Comprimento / 2);
        var n = Normal(line, c.Comprimento / 2);
        double? best = null;
        Cadeia? hit = null;
        foreach (var o in chains)
        {
            if (o == c) continue;
            for (int i = 0; i + 1 < o.Pontos.Count; i++)
            {
                if (SegmentHit(mid + n * 0.01, n, o.Pontos[i], o.Pontos[i + 1]) is { } t && (best == null || t + 0.01 < best))
                {
                    best = t + 0.01;
                    hit = o;
                }
            }
        }
        return hit?.TestadaId != null ? best : null;
    }

    /// <summary>
    /// Área do lote que a sobra formaria (a faixa dela até a profundidade, dentro da quadra): no fim da testada ou, quando a testada
    /// fica com a esquina do fim (<paramref name="cornerWidth"/> &gt; 0), logo antes do lote de esquina.
    /// </summary>
    private static double RestArea(Polygon2 quad, Cadeia c, double s, double b0, double rest, double depth, double cornerWidth)
    {
        var (a, b) = cornerWidth > 0 ? (s - cornerWidth, s - cornerWidth + rest) : (s, b0);
        return PolygonOps.TotalArea(PolygonOps.Intersect(new[] { quad }, new[] { Sector(c.Linha, a, b, false, false, depth) }));
    }

    private static double? SegmentHit(Vec2 o, Vec2 d, Vec2 a, Vec2 b)
    {
        var e = b - a;
        var den = d.Cross(e);
        if (Math.Abs(den) < 1e-12) return null;
        var t = (a - o).Cross(e) / den;
        var u = (a - o).Cross(d) / den;
        return t > 0 && u >= -1e-9 && u <= 1 + 1e-9 ? t : null;
    }

    /// <summary>Normal para dentro da quadra (à esquerda da testada) na estaca; nos vértices, a bissetriz.</summary>
    public static Vec2 Normal(Polyline2 line, double s)
    {
        var pts = line.Points;
        for (int i = 1; i + 1 < pts.Count; i++)
        {
            if (Math.Abs(line.StationOfVertex(i) - s) < 1e-6)
            {
                var n1 = (pts[i] - pts[i - 1]).Normalized().PerpLeft;
                var n2 = (pts[i + 1] - pts[i]).Normalized().PerpLeft;
                var bis = n1 + n2;
                return bis.Length < 1e-9 ? n1 : bis.Normalized();
            }
        }
        return line.TangentAt(Math.Clamp(s, 0, line.Length)).PerpLeft;
    }

    /// <summary>Faixa de profundidade <paramref name="depth"/> atrás da testada (prolongada nas pontas pelas tangentes).</summary>
    private static Polygon2[] Band(Polyline2 line, double depth)
    {
        var pts = line.Points;
        var t0 = (pts[1] - pts[0]).Normalized();
        var t1 = (pts[^1] - pts[^2]).Normalized();
        var ext = new List<Vec2> { pts[0] - t0 * Far };
        ext.AddRange(pts);
        ext.Add(pts[^1] + t1 * Far);
        // Faixa simétrica de largura 2 × profundidade: o lado de fora da quadra é descartado no recorte pela quadra.
        return PolygonOps.Strip(ext, 2 * depth).ToArray();
    }

    /// <summary>Estaca em que a testada sai da faixa de outra (vindo do início ou do fim).</summary>
    private static double ExitStation(Polyline2 line, Polygon2[] band, bool fromStart)
    {
        var len = line.Length;
        bool Inside(double s)
        {
            var p = line.PointAt(s) + Normal(line, s) * 0.05;
            return band.Any(b => b.Contains(p));
        }
        var step = 0.05;
        if (fromStart)
        {
            var s = step / 2;
            while (s < len && Inside(s)) s += step;
            if (s >= len) return len;
            var lo = Math.Max(0, s - step);
            var hi = s;
            for (int i = 0; i < 30; i++) { var m = (lo + hi) / 2; if (Inside(m)) lo = m; else hi = m; }
            return lo < step / 2 + 1e-9 && !Inside(lo) ? 0 : hi;
        }
        else
        {
            var s = len - step / 2;
            while (s > 0 && Inside(s)) s -= step;
            if (s <= 0) return 0;
            var lo = s;
            var hi = Math.Min(len, s + step);
            for (int i = 0; i < 30; i++) { var m = (lo + hi) / 2; if (Inside(m)) hi = m; else lo = m; }
            return hi > len - step / 2 - 1e-9 && !Inside(hi) ? len : lo;
        }
    }

    /// <summary>
    /// Setor do lote entre as estacas <paramref name="a"/> e <paramref name="b"/>: divisas pelas normais à testada; lado aberto
    /// (prolongado pela tangente) onde o lote vai até a quina/divisa.
    /// </summary>
    private static Polygon2 Sector(Polyline2 line, double a, double b, bool openA, bool openB, double depth)
    {
        var L = 4 * depth + 40;
        var pa = line.PointAt(a);
        var pb = line.PointAt(b);
        var na = Normal(line, a);
        var nb = Normal(line, b);
        var ta = line.TangentAt(Math.Clamp(a, 0, line.Length));
        var tb = line.TangentAt(Math.Clamp(b, 0, line.Length));
        var pts = new List<Vec2>();
        Vec2? apex = !openA && !openB ? RayHit(pa, na, pb, nb, L) : null;
        if (openA)
        {
            pts.Add(pa - ta * Far + na * L);
            pts.Add(pa - ta * Far);
        }
        else if (apex == null) pts.Add(pa + na * L);
        pts.AddRange(line.SubPoints(a, b));
        if (openB)
        {
            pts.Add(pb + tb * Far);
            pts.Add(pb + tb * Far + nb * L);
        }
        else if (apex == null) pts.Add(pb + nb * L);
        if (apex != null) pts.Add(apex.Value);
        return new Polygon2(pts);
    }

    /// <summary>Encontro dos raios (p1 + n1·u, p2 + n2·v) à frente dos dois pontos (até <paramref name="max"/>); nulo se não se encontram.</summary>
    private static Vec2? RayHit(Vec2 p1, Vec2 n1, Vec2 p2, Vec2 n2, double max = 1e9)
    {
        var den = n1.Cross(n2);
        if (Math.Abs(den) < 1e-9) return null;
        var d = p2 - p1;
        var u = d.Cross(n2) / den;
        var v = d.Cross(n1) / den;
        return u > 1e-6 && v > 1e-6 && u < max && v < max ? p1 + n1 * u : null;
    }

    private static Polygon2? Largest(IEnumerable<Polygon2> parts) =>
        parts.Where(p => p.Area > 1e-6).OrderByDescending(p => p.Area).FirstOrDefault();

    /// <summary>Estaca no perímetro reduzida ao comprimento do contorno.</summary>
    private static double Perimeter(Polygon2 quad, double s)
    {
        var per = 0.0;
        var r = quad.Outer;
        for (int i = 0; i < r.Count; i++) per += r[i].DistanceTo(r[(i + 1) % r.Count]);
        return ((s % per) + per) % per;
    }

    /// <summary>Junta a lasca ao lote vizinho com a maior divisa em comum; falso se nenhum encosta nela.</summary>
    /// <summary>Frente do lote prolongada pelas tangentes das pontas (mede a profundidade de qualquer ponto da faixa).</summary>
    private static Polyline2 FrontLine(Polyline2 line, double a, double b)
    {
        var pts = line.SubPoints(a, b);
        if (pts.Count < 2) pts = new List<Vec2> { line.PointAt(a), line.PointAt(b) };
        var ta = (pts[1] - pts[0]).Normalized();
        var tb = (pts[^1] - pts[^2]).Normalized();
        var res = new List<Vec2> { pts[0] - ta * Far };
        res.AddRange(pts);
        res.Add(pts[^1] + tb * Far);
        return new Polyline2(res);
    }

    private static bool MergeIntoNeighbour(List<Parcela> parcels, Polygon2 sliver)
    {
        Parcela? best = null;
        Polygon2? merged = null;
        var bestShared = 1e-4;
        foreach (var p in parcels)
        {
            var u = PolygonOps.Union(new[] { p.Forma, sliver });
            if (u.Count != 1) continue;
            var shared = (Perimeter(p.Forma) + Perimeter(sliver) - Perimeter(u[0])) / 2;
            if (shared > bestShared)
            {
                bestShared = shared;
                best = p;
                merged = u[0];
            }
        }
        if (best == null || merged == null) return false;
        best.Forma = merged;
        return true;
    }

    public static double Perimeter(Polygon2 p)
    {
        var s = 0.0;
        for (int i = 0; i < p.Outer.Count; i++) s += p.Outer[i].DistanceTo(p.Outer[(i + 1) % p.Outer.Count]);
        foreach (var h in p.Holes)
            for (int i = 0; i < h.Count; i++) s += h[i].DistanceTo(h[(i + 1) % h.Count]);
        return s;
    }

    // ------------------------------------------------------------------ numeração

    /// <summary>
    /// Numeração da quadra: lotes (categorias sem prefixo) de 1 a N e as demais com o prefixo da categoria (AV-1, AI-1...),
    /// seguindo o contorno no sentido escolhido a partir do lote inicial (padrão: o lote de esquina mais a noroeste).
    /// </summary>
    public static void Numerar(DivisaoQuadra d, QuadraDefinition q, IReadOnlyList<CategoriaLote> categorias)
    {
        if (d.Parcelas.Count == 0) return;
        // Estacas no contorno com as quinas vivas (o chanfro não muda a numeração).
        var contorno = d.SemChanfro ?? d.Quadra;
        var per = Perimeter(new Polygon2(contorno.Outer));
        var (mn, mx) = contorno.Bounds;
        var nw = new Vec2(mn.X, mx.Y);
        Vec2 FrontMid(Parcela p) => p.LinhaFrente.Count >= 2 ? new Polyline2(p.LinhaFrente).PointAt(new Polyline2(p.LinhaFrente).Length / 2) : p.Forma.Centroid;
        var withPos = d.Parcelas.Where(p => !double.IsNaN(p.Posicao)).ToList();
        var start = q.LoteInicial != null ? d.Parcelas.FirstOrDefault(p => p.Chave == q.LoteInicial && !double.IsNaN(p.Posicao)) : null;
        start ??= withPos.Where(p => p.Esquina).OrderBy(p => FrontMid(p).DistanceTo(nw)).FirstOrDefault()
                  ?? withPos.OrderBy(p => FrontMid(p).DistanceTo(nw)).FirstOrDefault();
        var s0 = start?.Posicao ?? 0;
        // Contorno anti-horário: o sentido horário percorre as estacas ao contrário.
        double Key(Parcela p) => double.IsNaN(p.Posicao) ? double.MaxValue
            : q.SentidoHorario ? ((s0 - p.Posicao) % per + per) % per : ((p.Posicao - s0) % per + per) % per;
        var ordered = d.Parcelas.OrderBy(Key).ThenBy(p => -p.Forma.Centroid.Y).ThenBy(p => p.Forma.Centroid.X).ToList();
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ordered)
        {
            var cat = categorias.FirstOrDefault(c => string.Equals(c.Nome, p.Categoria, StringComparison.OrdinalIgnoreCase));
            var prefix = cat?.Prefixo?.Trim() ?? "";
            counters.TryGetValue(prefix, out var k);
            counters[prefix] = ++k;
            p.Numero = prefix.Length == 0 ? k.ToString() : $"{prefix}-{k}";
        }
        // Ordem final da lista = ordem da numeração.
        var byKey = ordered.ToList();
        d.Parcelas.Clear();
        d.Parcelas.AddRange(byKey);
    }
}
