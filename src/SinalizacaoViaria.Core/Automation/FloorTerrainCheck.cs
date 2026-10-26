using System.Globalization;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Piso sobre o terreno projetado: código e marca, contorno, cota do topo em cada ponto (nula onde não se confere) e a
/// profundidade, abaixo do topo, em que o terreno deve ficar sob ele. É o subleito sob a pista e a espessura da laje sob
/// calçada e meio-fio. Nula quando não se sabe: aí só se confere o terreno acima do topo.
/// </summary>
public sealed record PisoSobreTerreno(string Codigo, string? Marca, Polygon2 Contorno, Func<Vec2, double?> Topo, double? Profundidade);

/// <summary>Um ponto conferido: onde, a cota do topo do piso e a profundidade esperada do terreno (nula = não se sabe).</summary>
public sealed record AmostraPiso(Vec2 Ponto, double Topo, double? Profundidade, string Codigo, string? Marca);

public enum ProblemaPisoTerreno
{
    /// <summary>O terreno passa acima do topo do piso: o Toposolid cobre o piso (piso enterrado).</summary>
    TerrenoCobrePiso,
    /// <summary>O terreno entra no corpo do piso ou da estrutura sob ele, sem chegar ao topo.</summary>
    TerrenoDentroDoPiso,
    /// <summary>Vão entre o fundo da estrutura e o terreno (piso suspenso).</summary>
    VaoSobOPiso,
    /// <summary>Degrau entre o terreno e a borda externa da seção, onde o talude devia começar.</summary>
    DegrauNaBorda,
}

/// <summary>Achado de um piso (ou de uma borda): quantos pontos, de quantos conferidos, o pior desvio (m) e onde.</summary>
public sealed record AchadoPisoTerreno(ProblemaPisoTerreno Problema, string Codigo, string? Marca, int Pontos, int Conferidos, double PiorDesvio, Vec2 Onde)
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public string Texto => string.Format(Pt, "{0}: {1} em {2} de {3} ponto(s), até {4:0.000} m em ({5:0.00}; {6:0.00})", Codigo, Problema switch
    {
        ProblemaPisoTerreno.TerrenoCobrePiso => "terreno acima do topo do piso (piso enterrado)",
        ProblemaPisoTerreno.TerrenoDentroDoPiso => "terreno dentro do piso ou da estrutura",
        ProblemaPisoTerreno.VaoSobOPiso => "vão entre a estrutura e o terreno (piso suspenso)",
        _ => "degrau entre o terreno e a borda da seção",
    }, Pontos, Conferidos, Math.Abs(PiorDesvio), Onde.X, Onde.Y);
}

/// <summary>
/// Pisos da via × terreno projetado (RT-1.2), sem o Revit. Sob cada piso o terreno deve ficar na profundidade esperada (o
/// fundo da estrutura), e na borda externa da seção deve encostar no topo do acabamento, onde começa o talude (plataforma por
/// camadas da terraplenagem, <c>Grading</c>). As tolerâncias são da conferência (precisão da triangulação do Toposolid), não
/// valores normativos.
/// </summary>
public static class FloorTerrainCheck
{
    /// <summary>Tolerância sob os pisos (m).</summary>
    public const double Tolerancia = 0.02;

    /// <summary>Tolerância na borda da seção (m): o Toposolid, sem linhas de quebra, arredonda a aresta do talude.</summary>
    public const double ToleranciaBorda = 0.05;

    /// <summary>Pontos dentro de cada piso, numa grade de <paramref name="passo"/> m, a pelo menos <paramref name="margem"/> m do contorno.</summary>
    public static IEnumerable<AmostraPiso> Amostras(IEnumerable<PisoSobreTerreno> pisos, double passo = 1.0, double margem = 0.3)
    {
        foreach (var p in pisos)
        {
            var (mn, mx) = p.Contorno.Bounds;
            for (var x = Math.Floor(mn.X / passo) * passo + passo / 2; x < mx.X; x += passo)
                for (var y = Math.Floor(mn.Y / passo) * passo + passo / 2; y < mx.Y; y += passo)
                {
                    var q = new Vec2(x, y);
                    if (!p.Contorno.Contains(q) || p.Contorno.DistanceTo(q) < margem || p.Topo(q) is not double top) continue;
                    yield return new AmostraPiso(q, top, p.Profundidade, p.Codigo, p.Marca);
                }
        }
    }

    /// <summary>Terreno sob os pisos, ponto a ponto, agrupado por piso e problema. Ponto sem terreno não é conferido.</summary>
    public static List<AchadoPisoTerreno> Pisos(IEnumerable<AmostraPiso> amostras, Func<Vec2, double?> terreno, double tol = Tolerancia)
    {
        var conferidos = new Dictionary<(string, string?), int>();
        var achados = new Dictionary<(ProblemaPisoTerreno, string, string?), (int N, double Pior, Vec2 Onde)>();
        void Anota(ProblemaPisoTerreno pr, AmostraPiso a, double desvio)
        {
            var k = (pr, a.Codigo, a.Marca);
            var cur = achados.TryGetValue(k, out var c) ? c : (N: 0, Pior: 0.0, Onde: a.Ponto);
            achados[k] = (cur.N + 1, Math.Abs(desvio) > Math.Abs(cur.Pior) ? desvio : cur.Pior, Math.Abs(desvio) > Math.Abs(cur.Pior) ? a.Ponto : cur.Onde);
        }
        foreach (var a in amostras)
        {
            if (terreno(a.Ponto) is not double t) continue;
            conferidos[(a.Codigo, a.Marca)] = conferidos.GetValueOrDefault((a.Codigo, a.Marca)) + 1;
            if (t > a.Topo + tol) { Anota(ProblemaPisoTerreno.TerrenoCobrePiso, a, t - a.Topo); continue; }
            if (a.Profundidade is not double prof) continue;
            var fundo = a.Topo - prof;
            if (t > fundo + tol) Anota(ProblemaPisoTerreno.TerrenoDentroDoPiso, a, t - fundo);
            else if (t < fundo - tol) Anota(ProblemaPisoTerreno.VaoSobOPiso, a, t - fundo);
        }
        return achados.Select(kv => new AchadoPisoTerreno(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value.N,
                conferidos[(kv.Key.Item2, kv.Key.Item3)], kv.Value.Pior, kv.Value.Onde))
            .OrderBy(a => a.Problema).ThenByDescending(a => Math.Abs(a.PiorDesvio)).ToList();
    }

    /// <summary>
    /// Bordas externas da seção: em cada ponto (com a direção para fora), o topo do piso logo dentro (5 cm) e o terreno logo
    /// fora (2 cm) – o talude começa no topo do acabamento. Ponto sem piso ou sem terreno não é conferido.
    /// </summary>
    public static AchadoPisoTerreno? Bordas(IEnumerable<(Vec2 Ponto, Vec2 Fora)> bordas, Func<Vec2, double?> topo, Func<Vec2, double?> terreno,
        string codigo = "borda da seção", double tol = ToleranciaBorda) => Bordas(bordas, topo, terreno, out _, codigo, tol);

    /// <summary>Idem, com quantos pontos foram conferidos (com piso e terreno) – zero quer dizer "não verificado", não "sem degrau".</summary>
    public static AchadoPisoTerreno? Bordas(IEnumerable<(Vec2 Ponto, Vec2 Fora)> bordas, Func<Vec2, double?> topo, Func<Vec2, double?> terreno,
        out int conferidos, string codigo = "borda da seção", double tol = ToleranciaBorda)
    {
        int n = 0;
        conferidos = 0;
        double pior = 0;
        var onde = default(Vec2);
        foreach (var (p, fora) in bordas)
        {
            var u = fora.Normalized();
            if (topo(p - u * 0.05) is not double z || terreno(p + u * 0.02) is not double t) continue;
            conferidos++;
            var d = t - z;
            if (Math.Abs(d) <= tol) continue;
            n++;
            if (Math.Abs(d) > Math.Abs(pior)) { pior = d; onde = p; }
        }
        return n == 0 ? null : new AchadoPisoTerreno(ProblemaPisoTerreno.DegrauNaBorda, codigo, null, n, conferidos, pior, onde);
    }

    /// <summary>Pontos das bordas externas de uma via: dos dois lados do eixo, a cada <paramref name="passo"/> m, longe das pontas.</summary>
    public static IEnumerable<(Vec2 Ponto, Vec2 Fora)> BordasDaVia(Polyline2 eixo, double esquerda, double direita, double passo = 2.0,
        double longeDasPontas = 5.0, Func<Vec2, bool>? pular = null)
    {
        for (var s = longeDasPontas; s <= eixo.Length - longeDasPontas; s += passo)
        {
            var c = eixo.PointAt(s);
            var n = eixo.TangentAt(s).PerpLeft;
            foreach (var (p, f) in new[] { (c + n * esquerda, n), (c - n * direita, -n) })
                if (pular == null || !pular(p)) yield return (p, f);
        }
    }
}
