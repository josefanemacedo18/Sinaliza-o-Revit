using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Fim do canteiro central nas interseções pela curva de giro à esquerda do veículo de projeto (DNIT IPR-718, 2005, cap. 8,
/// item 8 – "Canteiro Central", Tabelas 62 e 63 e Figura 133, critério da AASHTO): o bordo interno da trajetória de quem
/// converte à esquerda é um arco de raio de controle R tangente ao bordo do canteiro e ao eixo (ou ao bordo do canteiro) da
/// via transversal; o nariz fica do lado de dentro desse arco. Com nariz semicircular a ponta fica a R − L/2 do eixo da via
/// transversal (L = largura do canteiro) – a abertura 2R − L da Tabela 63 (21,0 m para L = 3,0 m e R = 12 m); com nariz em
/// ogiva os dois arcos formam a ponta, arredondada em L/5 ("R₂ ... próximo de 1/5 da largura do canteiro central"). Cada lado
/// do nariz só é limitado pela conversão que passa por ele: do lado de quem chega pelo ramo, o giro para a via transversal;
/// do outro, o de quem vem da via transversal e entra no ramo. A abertura nunca fica menor que 12 m (item 8.8.2).
/// </summary>
public static partial class IntersectionGenerator
{
    /// <summary>Largura do canteiro até a qual o nariz automático é semicircular (m) – IPR-718, item 8.8.2.</summary>
    public const double LarguraNarizSemicircular = 3.0;

    /// <summary>Abertura mínima do canteiro central (m) – IPR-718, item 8.8.2 ("em nenhum caso deverá ser inferior a 12 m").</summary>
    public const double AberturaMinimaCanteiro = 12.0;

    /// <summary>
    /// Canteiro central de uma via moldado no nó: o canteiro, a área que a interseção refaz (o canteiro da via sai ali), a área
    /// em que a pintura da via junto às faces do canteiro é removida (a abertura), a ponta de cada ramo (distância do nó) e
    /// os avisos.
    /// </summary>
    internal sealed record NarizesCanteiro(List<Polygon2> Canteiro, List<Polygon2> Refeito, List<Polygon2> Pintura,
        Dictionary<(int Road, int Sign), double> Pontas, List<string> Avisos)
    {
        /// <summary>Centro do nariz semicircular de cada ramo (distância do nó): a travessia começa nele.</summary>
        public Dictionary<(int Road, int Sign), double> Centros { get; } = new();
    }

    /// <summary>Recuo máximo da travessia para o nariz ficar inteiro à frente dela (m) – padrão do plugin; acima disso fica como está.</summary>
    public const double MaxNoseSetback = 5.0;

    /// <summary>Gaps de canteiro central (no eixo) de uma via.</summary>
    internal static IEnumerable<PavementGap> CentralGaps(RoadPavementDefinition r) =>
        r.Gaps.Where(g => g.Median && Math.Abs(g.Offset) <= g.Width / 2 + 0.1 && g.Width > 0.05);

    /// <summary>
    /// Molda o canteiro central da via <paramref name="i"/> nos ramos do nó (ver o resumo da classe). <paramref name="central"/>
    /// é a faixa do canteiro central da via; <paramref name="hardCut"/>, o que nunca fica com canteiro (pista das outras vias
    /// + 1 m, bolsões); <paramref name="crossRoads"/>, as vias que abrem o canteiro.
    /// </summary>
    internal static NarizesCanteiro CentralMedianNoses(IntersectionDefinition d, IntersectionLayout L, int i, List<Polygon2> central,
        List<Polygon2> hardCut, IReadOnlyCollection<int> crossRoads)
    {
        var res = new NarizesCanteiro(new(), new(), new(), new(), new());
        var road = L.Roads[i];
        var gaps = CentralGaps(road.Def).ToList();
        var allowed = PolygonOps.Difference(central, hardCut);
        if (gaps.Count == 0 || allowed.Count == 0 || crossRoads.Count == 0)
        {
            res.Canteiro.AddRange(allowed);
            return res;
        }
        var R = Math.Clamp(d.MedianControlRadius, 3.0, 60.0);
        var tMinPoly = new List<Polygon2>();
        var wide = Math.Max(road.Def.TotalLeft, road.Def.TotalRight) + 5;
        var near = new List<(IntersectionLeg Leg, double TShape, FormaNarizCanteiro Shape, double RNose, double Lo, double Hi, List<Polygon2> Cut, bool Pocket, double TArc)>();
        foreach (var leg in L.Legs.Where(l => l.Road == i))
        {
            // Faces do canteiro no referencial do ramo (o: esquerda de quem se afasta do nó).
            var lo = gaps.Min(g => Math.Min(leg.Sign * (g.Offset - g.Width / 2), leg.Sign * (g.Offset + g.Width / 2)));
            var hi = gaps.Max(g => Math.Max(leg.Sign * (g.Offset - g.Width / 2), leg.Sign * (g.Offset + g.Width / 2)));
            var M = hi - lo;
            var oPlus = leg.Dir.Normalized().PerpLeft;
            var forbidden = new List<Polygon2>();
            var tShape = 0.0;
            var tMin = 0.0;
            // Maior estaca do centro dos arcos de giro: o centro do nariz semicircular fica nela (a ponta encosta no arco).
            var tArc = 0.0;
            foreach (var j in crossRoads)
            {
                var axisJ = L.Roads[j].Axis;
                var (stJ, _, pJ) = Project(axisJ, L.Node);
                var uJ = axisJ.TangentAt(stJ).Normalized();
                // Normal da via transversal apontando para longe do ramo.
                var nJ = uJ.PerpLeft;
                if (nJ.Dot(leg.Dir) > 0) nJ = nJ * -1;
                var sinA = Math.Abs(leg.Dir.Normalized().Dot(nJ));
                if (sinA < 0.25) continue;
                // Abertura mínima: a largura da via transversal, nunca menos de 12 m (metade de cada lado do eixo dela).
                var wJ = L.Roads[j].Def.LeftWidth + L.Roads[j].Def.RightWidth;
                tMin = Math.Max(tMin, Math.Max(AberturaMinimaCanteiro, wJ) / 2 / sinA);
                // Face do canteiro da via transversal do lado de longe: quem converte passa entre ela e o ramo.
                var sgn = Math.Sign(nJ.Dot(uJ.PerpLeft));
                var mFar = CentralGaps(L.Roads[j].Def).Select(g => Math.Max(sgn * (g.Offset - g.Width / 2), sgn * (g.Offset + g.Width / 2)))
                    .DefaultIfEmpty(0).Max();
                var pC = pJ + nJ * Math.Max(0, mFar);
                foreach (var side in new[] { 1, -1 })
                {
                    // O lado só é limitado se a via transversal tem o ramo para onde (ou de onde) passa a conversão.
                    var toward = oPlus * -side;
                    if (!L.Legs.Any(l => l.Road == j && l.Dir.Normalized().Dot(toward) > 0.3)) continue;
                    var oE = side > 0 ? hi : lo;
                    var oO = oE - side * R;
                    double Dist(double t) => (pC - L.At(leg, t, oO)).Dot(nJ);
                    var d0 = Dist(0);
                    var d1 = Dist(10);
                    if (Math.Abs(d1 - d0) < 1e-6) continue;
                    var tStar = (R - d0) * 10 / (d1 - d0);
                    if (tStar <= 0 || tStar > 80) continue;
                    var o = L.At(leg, tStar, oO);
                    var tangE = L.At(leg, tStar, oE);
                    var tangC = o + nJ * R;
                    // Só do lado do próprio ramo: o setor do giro não alcança o canteiro do ramo oposto (via que atravessa o nó).
                    forbidden.AddRange(PolygonOps.Intersect(Sweep(o, tangE, tangC, R, R + 40), L.LegPoly(leg, 0, R + 40, _ => -wide, _ => wide)));
                    tShape = Math.Max(tShape, tStar + 1.0);
                    tArc = Math.Max(tArc, tStar);
                }
            }
            if (tShape <= 0 && tMin <= 0) continue;
            tShape = Math.Max(tShape, tMin + M + 1.0);
            var cut = forbidden.Count > 0 ? PolygonOps.Union(forbidden) : new List<Polygon2>();
            if (tMin > 0) cut = PolygonOps.Union(cut.Concat(L.LegPoly(leg, 0, tMin, _ => -wide, _ => wide)));
            allowed = PolygonOps.Difference(allowed, cut);
            // Raio do nariz: semicírculo (metade da largura, menos o bolsão) ou ogiva (1/5 da largura).
            var f = L.FeatureOf(leg);
            var mLoc = f?.Kind == TipoRamo.BolsaoCanteiro ? Math.Max(0.6, M - f.PocketWidth) : M;
            var shape = d.MedianNose == FormaNarizCanteiro.Automatica
                ? (M <= LarguraNarizSemicircular + 1e-6 ? FormaNarizCanteiro.Semicircular : FormaNarizCanteiro.Ogiva) : d.MedianNose;
            var rNose = shape == FormaNarizCanteiro.Semicircular ? Math.Min(M, mLoc) / 2 - 0.01 : Math.Min(Math.Max(0.3, M / 5), mLoc / 2 - 0.01);
            if (tMin > 0) tMinPoly.AddRange(L.LegPoly(leg, 0, tMin, _ => -wide, _ => wide));
            near.Add((leg, tShape, shape, Math.Max(0.3, rNose), lo, hi, cut, f?.Kind == TipoRamo.BolsaoCanteiro, tArc));
        }

        // Ponta de cada ramo: semicírculo com o diâmetro do canteiro, o mais perto do nó que fica fora das conversões (a ponta
        // encosta tangente no arco de giro: raio de abertura 1 cm menor deslocaria a ponta quase meio metro); ogiva e canteiro
        // estreitado por bolsão: o trecho perto do nó aberto com o raio do nariz (os demais cantos ficam como estão).
        var shaped = new List<Polygon2>();
        var nearZone = new List<Polygon2>();
        var carriage = RoadGenerator.Band(road.Axis, -road.Def.RightWidth, road.Def.LeftWidth);
        foreach (var (leg, tShape0, shape, rNose, lo, hi, cutLeg, pocket, tArc) in near)
        {
            var tShape = tShape0;
            List<Polygon2> nose;
            if (shape == FormaNarizCanteiro.Semicircular && !pocket)
            {
                var rr = (hi - lo) / 2;
                // Centro do semicírculo: na estaca do centro do arco de giro (tangência exata – um polígono com 1 mm de folga
                // deslocaria a ponta ~0,15 m) ou além, se a pista da outra via (+ 1 m), o bolsão ou a abertura mínima pedem.
                var blocked = PolygonOps.Union(hardCut.Concat(tMinPoly));
                bool Fits(double c) => PolygonOps.TotalArea(PolygonOps.Intersect(new[] { SemiCap(L, leg, c, lo, hi) }, blocked)) < 1e-4;
                double cLo = Math.Max(rr, tArc), cHi = Math.Max(cLo, tShape) + rr;
                if (!Fits(cLo))
                {
                    for (int it = 0; it < 30 && cHi - cLo > 0.002; it++)
                    {
                        var m = (cLo + cHi) / 2;
                        if (Fits(m)) cHi = m; else cLo = m;
                    }
                    cLo = cHi;
                }
                res.Centros[(leg.Road, leg.Sign)] = cLo;
                // O trecho moldado vai pelo menos até o centro (o nariz chega à largura toda antes do canteiro da via).
                tShape = Math.Max(tShape, cLo + 0.5);
                var zone = L.LegPoly(leg, 0, tShape, _ => -wide, _ => wide);
                nearZone.AddRange(zone);
                nose = PolygonOps.Intersect(PolygonOps.Union(new[] { SemiCap(L, leg, cLo, lo, hi) }
                    .Concat(PolygonOps.Intersect(allowed, L.LegPoly(leg, cLo, tShape, _ => lo - 0.01, _ => hi + 0.01)))), zone).Where(p => p.Area > 0.05).ToList();
            }
            else
            {
                var zone = L.LegPoly(leg, 0, tShape, _ => -wide, _ => wide);
                nearZone.AddRange(zone);
                var part = PolygonOps.Intersect(allowed, L.LegPoly(leg, 0, tShape + 3 * rNose + 1, _ => -wide, _ => wide));
                nose = PolygonOps.Intersect(Open(part, rNose), zone).Where(p => p.Area > 0.05).ToList();
            }
            shaped.AddRange(nose);
            var tip = nose.SelectMany(p => p.Outer).Select(v => (Project(road.Axis, v).Station - leg.NodeStation) * leg.Sign).DefaultIfEmpty(tShape).Min();
            res.Pontas[(leg.Road, leg.Sign)] = tip;
            // O canteiro da via e a pista junto a ele (1 m além de cada face) saem até o fim do nariz: a interseção refaz os dois.
            // Só o canteiro (como antes) deixava, com o nariz além da zona do nó (ramo oblíquo), a lasca entre o nariz e a face
            // sem pista (fresta) ou como uma ponta aguda da pista da interseção encostada na pista da via.
            res.Refeito.AddRange(PolygonOps.Intersect(carriage, L.LegPoly(leg, 0, tShape, _ => lo - 1.0, _ => hi + 1.0)));
            if (tip > 0.5) res.Pintura.AddRange(L.LegPoly(leg, 0, tip, _ => lo - 0.45, _ => hi + 0.45));
        }
        var rest = nearZone.Count > 0 ? PolygonOps.Difference(allowed, PolygonOps.Union(nearZone)) : allowed;
        // O nariz e o resto do canteiro se encontram no fim do trecho moldado: diferenças de décimo de milímetro deixavam uma
        // fresta de espessura zero na emenda, que o anel do meio-fio contornava (agulha de meio-fio entrando no miolo). Fechada
        // com 5 mm (as quinas ficam como estão).
        var merged = PolygonOps.Offset(PolygonOps.Offset(PolygonOps.Union(shaped.Concat(rest)), 0.005), -0.005);
        res.Canteiro.AddRange(merged.Where(p => p.Area > 0.3));
        return res;
    }

    /// <summary>Semicírculo do nariz: centro no eixo do canteiro a <paramref name="c"/> do nó, voltado para o nó, com o diâmetro do canteiro.</summary>
    private static Polygon2 SemiCap(IntersectionLayout L, IntersectionLeg leg, double c, double lo, double hi)
    {
        var mid = (lo + hi) / 2;
        var rr = (hi - lo) / 2;
        var p = L.At(leg, c, mid);
        var dir = (L.At(leg, c + 0.5, mid) - L.At(leg, Math.Max(0, c - 0.5), mid)).Normalized();
        var n = dir.PerpLeft;
        const int k = 48;
        var pts = new List<Vec2>();
        for (int q = 0; q <= k; q++)
        {
            var phi = Math.PI / 2 + Math.PI * q / k;
            pts.Add(p + dir * (rr * Math.Cos(phi)) + n * (rr * Math.Sin(phi)));
        }
        return new Polygon2(pts);
    }

    /// <summary>
    /// Área varrida por uma conversão em torno do centro <paramref name="o"/>: o setor entre os pontos de tangência
    /// <paramref name="a"/> e <paramref name="b"/> (o arco menor), além do raio de controle <paramref name="r"/>.
    /// </summary>
    private static List<Polygon2> Sweep(Vec2 o, Vec2 a, Vec2 b, double r, double big)
    {
        var a0 = Math.Atan2(a.Y - o.Y, a.X - o.X);
        var a1 = Math.Atan2(b.Y - o.Y, b.X - o.X);
        var da = a1 - a0;
        while (da > Math.PI) da -= 2 * Math.PI;
        while (da < -Math.PI) da += 2 * Math.PI;
        var n = Math.Max(8, (int)Math.Ceiling(Math.Abs(da) / (Math.PI / 90)));
        var ring = new List<Vec2> { o };
        for (int k = 0; k <= n; k++)
        {
            var ang = a0 + da * k / n;
            ring.Add(o + new Vec2(Math.Cos(ang), Math.Sin(ang)) * big);
        }
        // Círculo de controle com 2 mm de erro de corda e nunca menor que o raio (a ponta não recua por causa do polígono).
        var disk = new Polygon2(CurveTools.Circle(o, r + 0.002, 0.002));
        return PolygonOps.Difference(new[] { new Polygon2(ring) }, new[] { disk });
    }
}
