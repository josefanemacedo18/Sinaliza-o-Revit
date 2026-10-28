using System.Globalization;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>O que o tema do terreno mostra.</summary>
public enum TemaTerreno
{
    /// <summary>Cota do terreno (m).</summary>
    Elevacao,
    /// <summary>Declividade (%): 100 × tangente da maior inclinação.</summary>
    Declividade,
    /// <summary>Orientação da encosta: para onde a água desce (azimute, graus a partir do Norte do projeto – eixo Y –, horário).</summary>
    Orientacao,
    /// <summary>Diferença entre duas superfícies (m): atual − natural; positivo = aterro, negativo = corte.</summary>
    Diferenca,
}

/// <summary>Classe de um tema: intervalo [De, Ate) do valor, rótulo da legenda e cor.</summary>
public sealed record ClasseTema(double De, double Ate, string Rotulo, Rgb Cor);

/// <summary>Malha de amostras do terreno: nós de (Nx+1) × (Ny+1), passo <see cref="Passo"/>; nulo fora do terreno ou da região.</summary>
public sealed class GradeTerreno
{
    public double X0 { get; init; }
    public double Y0 { get; init; }
    public double Passo { get; init; }
    public int Nx { get; init; }
    public int Ny { get; init; }
    public required double?[] Z { get; init; }
    /// <summary>Célula dentro da região pedida (o centro); nulo = todas.</summary>
    public bool[]? Dentro { get; init; }

    public double? No(int i, int j) => i < 0 || j < 0 || i > Nx || j > Ny ? null : Z[j * (Nx + 1) + i];
    public bool Usa(int i, int j) => Dentro == null || Dentro[j * Nx + i];
    public Vec2 Canto(int i, int j) => new(X0 + i * Passo, Y0 + j * Passo);
    public double AreaCelula => Passo * Passo;
}

/// <summary>Resultado de um tema: as regiões de cada classe (planta), a área e a fração de cada uma e os extremos do valor.</summary>
public sealed class MapaTema
{
    public TemaTerreno Tema { get; init; }
    public List<(ClasseTema Classe, List<Polygon2> Regioes, double Area)> Faixas { get; } = new();
    public double AreaTotal { get; set; }
    public double Min { get; set; } = double.NaN;
    public double Max { get; set; } = double.NaN;
    public double Media { get; set; } = double.NaN;
    /// <summary>Células planas (declividade abaixo do limite de plano) – sem orientação.</summary>
    public double AreaPlana { get; set; }
}

/// <summary>
/// Temas do terreno (pedido de 2026-10-27, item 13): elevação, declividade e orientação a partir da cota em qualquer ponto,
/// numa malha regular. A declividade e a orientação de cada célula saem do plano médio dos quatro cantos (diferenças
/// centrais). As classes padrão são do plugin e configuráveis – nenhuma é limite de norma.
/// </summary>
public static class TemasTerreno
{
    /// <summary>Declividade abaixo da qual a célula é "plana" para a orientação (%, padrão do plugin).</summary>
    public const double LimitePlano = 1.0;

    /// <summary>
    /// Amostra o terreno na malha que cobre [min, max] (passo aumentado para no máximo <paramref name="maxCelulas"/> células);
    /// <paramref name="regiao"/> restringe as células usadas.
    /// </summary>
    public static GradeTerreno Amostrar(Func<Vec2, double?> ground, Vec2 min, Vec2 max, double passo, Polygon2? regiao = null, int maxCelulas = 40000)
    {
        var w = Math.Max(0.1, max.X - min.X);
        var h = Math.Max(0.1, max.Y - min.Y);
        passo = Math.Max(0.1, passo);
        if (w * h / (passo * passo) > maxCelulas) passo = Math.Sqrt(w * h / maxCelulas);
        var nx = Math.Max(1, (int)Math.Ceiling(w / passo));
        var ny = Math.Max(1, (int)Math.Ceiling(h / passo));
        var z = new double?[(nx + 1) * (ny + 1)];
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                z[j * (nx + 1) + i] = ground(new Vec2(min.X + i * passo, min.Y + j * passo));
        bool[]? dentro = null;
        if (regiao != null)
        {
            dentro = new bool[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    dentro[j * nx + i] = regiao.Contains(new Vec2(min.X + (i + 0.5) * passo, min.Y + (j + 0.5) * passo));
        }
        return new GradeTerreno { X0 = min.X, Y0 = min.Y, Passo = passo, Nx = nx, Ny = ny, Z = z, Dentro = dentro };
    }

    /// <summary>Cota média, declividade (%) e orientação (graus, horário a partir do Norte do projeto) de uma célula; nulos sem os quatro cantos.</summary>
    public static (double Z, double Declividade, double Orientacao)? Celula(GradeTerreno g, int i, int j)
    {
        if (g.No(i, j) is not { } z00 || g.No(i + 1, j) is not { } z10 || g.No(i, j + 1) is not { } z01 || g.No(i + 1, j + 1) is not { } z11) return null;
        var dzdx = (z10 + z11 - z00 - z01) / (2 * g.Passo);
        var dzdy = (z01 + z11 - z00 - z10) / (2 * g.Passo);
        var decl = Math.Sqrt(dzdx * dzdx + dzdy * dzdy) * 100;
        // Para onde a água desce: −gradiente; azimute a partir de +Y (Norte), sentido horário.
        var az = Math.Atan2(-dzdx, -dzdy) * 180 / Math.PI;
        if (az < 0) az += 360;
        return ((z00 + z10 + z01 + z11) / 4, decl, az);
    }

    /// <summary>Valor do tema numa célula (nulo fora do terreno; orientação nula em célula plana).</summary>
    public static double? Valor(GradeTerreno g, int i, int j, TemaTerreno tema)
    {
        if (Celula(g, i, j) is not { } c) return null;
        return tema switch
        {
            TemaTerreno.Elevacao => c.Z,
            TemaTerreno.Declividade => c.Declividade,
            TemaTerreno.Orientacao => c.Declividade < LimitePlano ? null : c.Orientacao,
            _ => null,
        };
    }

    private static readonly Rgb[] RampaElevacao = { new(38, 115, 38), new(122, 171, 70), new(201, 214, 105), new(240, 222, 122), new(222, 170, 92), new(181, 117, 66), new(140, 92, 64), new(214, 206, 196) };
    private static readonly Rgb[] RampaDeclividade = { new(56, 158, 72), new(150, 201, 80), new(250, 224, 90), new(250, 160, 60), new(222, 75, 50), new(150, 40, 110) };
    private static readonly Rgb[] Rosa = { new(214, 64, 64), new(240, 150, 50), new(235, 220, 70), new(120, 200, 80), new(60, 180, 190), new(70, 120, 220), new(130, 80, 200), new(210, 80, 170) };
    private static readonly Rgb[] RampaDiferenca = { new(160, 30, 30), new(214, 80, 60), new(240, 160, 130), new(240, 240, 240), new(150, 210, 150), new(70, 160, 80), new(20, 100, 40) };
    public static readonly Rgb CorPlano = new(200, 200, 200);

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string N(double v, string f = "0.##") => v.ToString(f, Pt);

    /// <summary>
    /// Classes padrão (do plugin, configuráveis): elevação em <paramref name="n"/> intervalos iguais entre os extremos;
    /// declividade 0–5, 5–10, 10–20, 20–30, 30–50 e acima de 50 %; orientação nos oito rumos (N, NE, L, SE, S, SO, O, NO);
    /// diferença em intervalos simétricos de corte e aterro.
    /// </summary>
    public static List<ClasseTema> ClassesPadrao(TemaTerreno tema, double min, double max, int n = 8)
    {
        switch (tema)
        {
            case TemaTerreno.Declividade:
                return ClassesDosLimites(tema, new[] { 0.0, 5, 10, 20, 30, 50 });
            case TemaTerreno.Orientacao:
            {
                var nomes = new[] { "N", "NE", "L", "SE", "S", "SO", "O", "NO" };
                var res = new List<ClasseTema>();
                for (int k = 0; k < 8; k++)
                {
                    var c = k * 45.0;
                    res.Add(new ClasseTema(c - 22.5, c + 22.5, $"{nomes[k]} ({N((c - 22.5 + 360) % 360, "0.#")}° a {N(c + 22.5, "0.#")}°)", Rosa[k]));
                }
                return res;
            }
            case TemaTerreno.Diferenca:
            {
                var a = Math.Max(Math.Abs(double.IsNaN(min) ? 0 : min), Math.Abs(double.IsNaN(max) ? 0 : max));
                var step = Passo(Math.Max(0.1, a) / 3);
                return ClassesDosLimites(tema, new[] { -2.5 * step, -1.5 * step, -0.5 * step, 0.5 * step, 1.5 * step, 2.5 * step }, aberta: true);
            }
            default:
            {
                if (double.IsNaN(min) || double.IsNaN(max) || max - min < 1e-6) { min = double.IsNaN(min) ? 0 : min; max = min + 1; }
                n = Math.Clamp(n, 2, 12);
                var res = new List<ClasseTema>();
                for (int k = 0; k < n; k++)
                {
                    var a = min + (max - min) * k / n;
                    var b = min + (max - min) * (k + 1) / n;
                    var cor = RampaElevacao[(int)Math.Round((double)k * (RampaElevacao.Length - 1) / Math.Max(1, n - 1))];
                    res.Add(new ClasseTema(a, k == n - 1 ? b + 1e-9 : b, $"{N(a)} a {N(b)} m", cor));
                }
                return res;
            }
        }
    }

    /// <summary>Passo "redondo" (1, 2 ou 5 × 10ⁿ) mais próximo acima de <paramref name="x"/>.</summary>
    private static double Passo(double x)
    {
        var mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1e-6, x))));
        foreach (var f in new[] { 1.0, 2.0, 5.0, 10.0 }) if (x <= f * mag + 1e-12) return f * mag;
        return 10 * mag;
    }

    /// <summary>
    /// Classes pelos limites informados (crescentes): [l0, l1), [l1, l2)… e a última até o infinito; com <paramref name="aberta"/>,
    /// também uma primeira abaixo de l0.
    /// </summary>
    public static List<ClasseTema> ClassesDosLimites(TemaTerreno tema, IReadOnlyList<double> limites, bool aberta = false)
    {
        var l = limites.Where(x => !double.IsNaN(x)).Distinct().OrderBy(x => x).ToList();
        var res = new List<ClasseTema>();
        var unidade = tema switch { TemaTerreno.Declividade => " %", TemaTerreno.Orientacao => "°", _ => " m" };
        var bordas = new List<double>();
        if (aberta || l.Count == 0) bordas.Add(double.NegativeInfinity);
        bordas.AddRange(l);
        bordas.Add(double.PositiveInfinity);
        var rampa = tema switch { TemaTerreno.Declividade => RampaDeclividade, TemaTerreno.Diferenca => RampaDiferenca, TemaTerreno.Orientacao => Rosa, _ => RampaElevacao };
        var n = bordas.Count - 1;
        for (int k = 0; k < n; k++)
        {
            var (a, b) = (bordas[k], bordas[k + 1]);
            var rot = double.IsNegativeInfinity(a) ? $"abaixo de {N(b)}{unidade}"
                : double.IsPositiveInfinity(b) ? $"{N(a)}{unidade} ou mais"
                : $"{N(a)} a {N(b)}{unidade}";
            if (tema == TemaTerreno.Diferenca)
                rot += double.IsPositiveInfinity(b) || a >= 0 ? " (aterro)" : double.IsNegativeInfinity(a) || b <= 0 ? " (corte)" : "";
            var cor = rampa[(int)Math.Round((double)k * (rampa.Length - 1) / Math.Max(1, n - 1))];
            res.Add(new ClasseTema(a, b, rot, cor));
        }
        return res;
    }

    /// <summary>Lê limites "0; 5; 10" (vírgula ou ponto decimal).</summary>
    public static List<double> LerLimites(string? texto)
    {
        var res = new List<double>();
        foreach (var p in (texto ?? "").Split(new[] { ';', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(p.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) res.Add(v);
        return res;
    }

    /// <summary>Classe do valor (orientação: o setor que contém o ângulo, com a volta em 360°).</summary>
    public static int ClasseDe(IReadOnlyList<ClasseTema> classes, TemaTerreno tema, double v)
    {
        for (int k = 0; k < classes.Count; k++)
        {
            var c = classes[k];
            if (tema == TemaTerreno.Orientacao)
            {
                var x = v;
                if (x >= c.Ate) x -= 360;
                if (x >= c.De && x < c.Ate) return k;
            }
            else if (v >= c.De && v < c.Ate) return k;
        }
        return -1;
    }

    /// <summary>
    /// Mapa do tema: cada célula (dentro da região) vai para a classe do valor dela; as células de uma classe viram regiões
    /// (faixas por linha unidas). <paramref name="valor"/> substitui o valor do tema (ex.: a diferença entre superfícies).
    /// </summary>
    public static MapaTema Mapa(GradeTerreno g, TemaTerreno tema, IReadOnlyList<ClasseTema> classes, Func<int, int, double?>? valor = null)
    {
        var mapa = new MapaTema { Tema = tema };
        var rects = classes.Select(_ => new List<Polygon2>()).ToList();
        var plano = new List<Polygon2>();
        double sum = 0, mn = double.MaxValue, mx = double.MinValue;
        var count = 0;
        for (int j = 0; j < g.Ny; j++)
        {
            var run = -2;          // classe da faixa corrente (−2 = nenhuma; −1 = plana)
            var start = 0;
            void Close(int i)
            {
                if (run == -2) return;
                var r = Polygon2.Rectangle(g.Canto(start, j), g.Canto(i, j + 1));
                (run == -1 ? plano : rects[run]).Add(r);
                run = -2;
            }
            for (int i = 0; i < g.Nx; i++)
            {
                var k = -2;
                if (g.Usa(i, j))
                {
                    var v = valor?.Invoke(i, j) ?? Valor(g, i, j, tema);
                    if (v is { } x)
                    {
                        k = ClasseDe(classes, tema, x);
                        if (k < 0) k = -2;
                        sum += x; count++;
                        mn = Math.Min(mn, x); mx = Math.Max(mx, x);
                        mapa.AreaTotal += g.AreaCelula;
                    }
                    else if (tema == TemaTerreno.Orientacao && Celula(g, i, j) != null)
                    {
                        k = -1;
                        mapa.AreaPlana += g.AreaCelula;
                        mapa.AreaTotal += g.AreaCelula;
                    }
                }
                if (k != run) { Close(i); if (k != -2) { run = k; start = i; } }
            }
            Close(g.Nx);
        }
        for (int k = 0; k < classes.Count; k++)
        {
            var regs = rects[k].Count == 0 ? new List<Polygon2>() : Unir(rects[k]);
            mapa.Faixas.Add((classes[k], regs, rects[k].Sum(r => r.Area)));
        }
        if (tema == TemaTerreno.Orientacao && plano.Count > 0)
            mapa.Faixas.Add((new ClasseTema(double.NaN, double.NaN, $"plano (abaixo de {N(LimitePlano)} %)", CorPlano), Unir(plano), plano.Sum(r => r.Area)));
        if (count > 0) { mapa.Min = mn; mapa.Max = mx; mapa.Media = sum / count; }
        return mapa;
    }

    private static List<Polygon2> Unir(List<Polygon2> rects)
    {
        try { return PolygonOps.Union(rects).Where(p => p.Area > 1e-6).ToList(); }
        catch { return rects; }
    }
}

/// <summary>Medida no terreno ao longo de um caminho em planta (dois pontos = linha reta).</summary>
public sealed class MedidaTerreno
{
    /// <summary>Perfil: (distância horizontal acumulada, cota) – só onde há terreno.</summary>
    public List<Vec2> Perfil { get; } = new();
    public double Horizontal { get; set; }
    /// <summary>Comprimento sobre o terreno (soma dos trechos inclinados).</summary>
    public double SobreOTerreno { get; set; }
    public double? ZInicio { get; set; }
    public double? ZFim { get; set; }
    /// <summary>Cota do fim − cota do início (m).</summary>
    public double Desnivel => ZFim is { } b && ZInicio is { } a ? b - a : double.NaN;
    /// <summary>Desnível / distância horizontal (%), entre o início e o fim.</summary>
    public double DeclividadeMedia => Horizontal > 1e-9 && !double.IsNaN(Desnivel) ? Desnivel / Horizontal * 100 : double.NaN;
    public double Subida { get; set; }
    public double Descida { get; set; }
    /// <summary>Maior declividade (%) entre dois pontos vizinhos do perfil, em módulo.</summary>
    public double DeclividadeMaxima { get; set; }
    /// <summary>Distância horizontal sem terreno (fora do Toposolid).</summary>
    public double SemTerreno { get; set; }
}

public static class MedidasTerreno
{
    /// <summary>
    /// Distâncias, desnível e declividades ao longo do caminho, com o terreno amostrado a cada <paramref name="passo"/> m (e nos
    /// vértices). A declividade máxima é a do maior trecho inclinado entre amostras vizinhas.
    /// </summary>
    public static MedidaTerreno Medir(IReadOnlyList<Vec2> caminho, Func<Vec2, double?> ground, double passo = 0.5)
    {
        var m = new MedidaTerreno();
        if (caminho.Count < 2) return m;
        passo = Math.Max(0.05, passo);
        var pts = new List<(double S, Vec2 P)>();
        double s = 0;
        for (int k = 0; k + 1 < caminho.Count; k++)
        {
            var (a, b) = (caminho[k], caminho[k + 1]);
            var len = a.DistanceTo(b);
            var n = Math.Max(1, (int)Math.Ceiling(len / passo));
            for (int i = 0; i < n; i++) pts.Add((s + len * i / n, a + (b - a) * ((double)i / n)));
            s += len;
        }
        pts.Add((s, caminho[^1]));
        m.Horizontal = s;
        (double S, double Z)? prev = null;
        double? last = null;
        for (int i = 0; i < pts.Count; i++)
        {
            var z = ground(pts[i].P);
            if (i > 0 && (z == null || last == null)) m.SemTerreno += pts[i].S - pts[i - 1].S;
            last = z;
            if (z is not { } zz) continue;
            m.Perfil.Add(new Vec2(pts[i].S, zz));
            if (prev is { } p)
            {
                var dh = pts[i].S - p.S;
                var dz = zz - p.Z;
                m.SobreOTerreno += Math.Sqrt(dh * dh + dz * dz);
                if (dz > 0) m.Subida += dz; else m.Descida -= dz;
                if (dh > 1e-9) m.DeclividadeMaxima = Math.Max(m.DeclividadeMaxima, Math.Abs(dz) / dh * 100);
            }
            prev = (pts[i].S, zz);
        }
        m.ZInicio = ground(caminho[0]);
        m.ZFim = ground(caminho[^1]);
        return m;
    }
}

/// <summary>Plataforma: cota de projeto de uma área nivelada (dada, média do terreno ou de equilíbrio entre corte e aterro).</summary>
public static class Plataformas
{
    /// <summary>Corte e aterro (m³) só dentro do contorno, com a plataforma na cota <paramref name="z"/> (sem os taludes).</summary>
    public static (double Corte, double Aterro, double Area) Volumes(Polygon2 area, double z, Func<Vec2, double?> ground, double passo = 1.0)
    {
        var (mn, mx) = area.Bounds;
        passo = Math.Max(0.1, passo);
        var n = (mx.X - mn.X) * (mx.Y - mn.Y) / (passo * passo);
        if (n > 200_000) passo *= Math.Sqrt(n / 200_000);
        double c = 0, f = 0, a = 0;
        var a2 = passo * passo;
        for (var x = mn.X + passo / 2; x < mx.X; x += passo)
            for (var y = mn.Y + passo / 2; y < mx.Y; y += passo)
            {
                var p = new Vec2(x, y);
                if (!area.Contains(p) || ground(p) is not { } g) continue;
                a += a2;
                if (g > z) c += (g - z) * a2; else f += (z - g) * a2;
            }
        return (c, f, a);
    }

    /// <summary>Cota média do terreno dentro do contorno; nula sem terreno nele.</summary>
    public static double? CotaMedia(Polygon2 area, Func<Vec2, double?> ground, double passo = 1.0)
    {
        var (c, f, a) = Volumes(area, 0, ground, passo);
        return a < 1e-9 ? null : (c - f) / a;
    }

    /// <summary>
    /// Cota de equilíbrio: corte = aterro × <paramref name="fh"/> (fator de homogeneização) dentro do contorno – bisseção entre
    /// o menor e o maior ponto do terreno nele. Os taludes ficam fora da conta (a Terraplenagem dá o total com eles).
    /// </summary>
    public static double? CotaEquilibrio(Polygon2 area, Func<Vec2, double?> ground, double fh = 1.0, double passo = 1.0)
    {
        var media = CotaMedia(area, ground, passo);
        if (media == null) return null;
        var (mn, mx) = area.Bounds;
        double lo = double.MaxValue, hi = double.MinValue;
        for (var x = mn.X; x <= mx.X; x += Math.Max(0.5, passo))
            for (var y = mn.Y; y <= mx.Y; y += Math.Max(0.5, passo))
                if (area.Contains(new Vec2(x, y)) && ground(new Vec2(x, y)) is { } g) { lo = Math.Min(lo, g); hi = Math.Max(hi, g); }
        if (lo > hi) return media;
        fh = Math.Max(0.1, fh);
        for (int it = 0; it < 60 && hi - lo > 1e-4; it++)
        {
            var z = (lo + hi) / 2;
            var (c, f, _) = Volumes(area, z, ground, passo);
            if (c > f * fh) lo = z; else hi = z;
        }
        return (lo + hi) / 2;
    }
}

/// <summary>Edição do terreno natural (pontos do levantamento): suavização de uma região e linhas de quebra.</summary>
public static class EdicaoTerreno
{
    /// <summary>
    /// Suaviza as cotas dos pontos dentro de <paramref name="regiao"/>: em cada passada a cota vai para a média dos vizinhos a
    /// até <paramref name="raio"/> m com o peso <paramref name="peso"/> (0 a 1). Na faixa de <paramref name="raio"/> m junto ao
    /// contorno o peso cai a zero – o terreno de fora fica igual e não há degrau na borda.
    /// </summary>
    public static List<Vec3> Suavizar(IReadOnlyList<Vec3> pontos, Polygon2 regiao, int passadas = 3, double raio = 3.0, double peso = 0.5)
    {
        raio = Math.Max(0.2, raio);
        peso = Math.Clamp(peso, 0, 1);
        var z = pontos.Select(p => p.Z).ToArray();
        var cell = raio;
        var idx = new Dictionary<(int, int), List<int>>();
        (int, int) K(Vec2 p) => ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));
        for (int i = 0; i < pontos.Count; i++)
        {
            var k = K(pontos[i].XY);
            if (!idx.TryGetValue(k, out var l)) idx[k] = l = new List<int>();
            l.Add(i);
        }
        // Peso de cada ponto: 0 fora, rampa até o peso pedido a "raio" m para dentro do contorno.
        var w = new double[pontos.Count];
        var ring = regiao.Outer;
        for (int i = 0; i < pontos.Count; i++)
        {
            var p = pontos[i].XY;
            if (!regiao.Contains(p)) continue;
            var d = double.MaxValue;
            for (int e = 0; e < ring.Count; e++) d = Math.Min(d, DistSeg(p, ring[e], ring[(e + 1) % ring.Count]));
            w[i] = peso * Math.Clamp(d / raio, 0, 1);
        }
        for (int it = 0; it < Math.Max(1, passadas); it++)
        {
            var next = (double[])z.Clone();
            for (int i = 0; i < pontos.Count; i++)
            {
                if (w[i] <= 0) continue;
                var p = pontos[i].XY;
                var (cx, cy) = K(p);
                double s = 0;
                var n = 0;
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                        if (idx.TryGetValue((cx + dx, cy + dy), out var l))
                            foreach (var j in l)
                                if (j != i && pontos[j].XY.DistanceTo(p) <= raio) { s += z[j]; n++; }
                if (n > 0) next[i] = z[i] + (s / n - z[i]) * w[i];
            }
            z = next;
        }
        return pontos.Select((p, i) => p with { Z = z[i] }).ToList();
    }

    private static double DistSeg(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var l2 = ab.Dot(ab);
        var u = l2 < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
        return p.DistanceTo(a + ab * u);
    }

    /// <summary>Pontos da linha de quebra a cada <paramref name="passo"/> m, com a cota interpolada entre os vértices.</summary>
    public static List<Vec3> PontosDaLinha(IReadOnlyList<Vec3> linha, double passo = 1.0)
    {
        var res = new List<Vec3>();
        passo = Math.Max(0.1, passo);
        for (int k = 0; k + 1 < linha.Count; k++)
        {
            var (a, b) = (linha[k], linha[k + 1]);
            var n = Math.Max(1, (int)Math.Ceiling(a.XY.DistanceTo(b.XY) / passo));
            for (int i = 0; i < n; i++) res.Add(a + (b - a) * ((double)i / n));
        }
        if (linha.Count > 0) res.Add(linha[^1]);
        return res;
    }

    /// <summary>
    /// Terreno com a linha de quebra: os pontos naturais a menos de <paramref name="folga"/> m da linha (em planta) saem e entram
    /// os pontos da linha – a triangulação passa a seguir a linha (crista, pé, borda de talude, margem).
    /// </summary>
    public static List<Vec3> ComLinha(IReadOnlyList<Vec3> naturais, IReadOnlyList<Vec3> linha, double folga = 0.5, double passo = 1.0)
    {
        var lp = PontosDaLinha(linha, passo);
        bool Perto(Vec2 p)
        {
            for (int k = 0; k + 1 < linha.Count; k++) if (DistSeg(p, linha[k].XY, linha[k + 1].XY) < folga) return true;
            return false;
        }
        return naturais.Where(p => !Perto(p.XY)).Concat(lp).ToList();
    }
}

/// <summary>Comparação de duas superfícies (ex.: terreno natural × atual): volumes, extremos e a distribuição da diferença.</summary>
public sealed class ComparacaoSuperficies
{
    /// <summary>Volume onde a superfície B fica acima da A (aterro, m³) e abaixo (corte, m³).</summary>
    public double Aterro { get; set; }
    public double Corte { get; set; }
    public double Area { get; set; }
    public double Min { get; set; } = double.NaN;
    public double Max { get; set; } = double.NaN;
    /// <summary>Área (m²) com a diferença em cada classe (as do tema Diferença).</summary>
    public List<(ClasseTema Classe, double Area)> Distribuicao { get; } = new();
    public required GradeTerreno Grade { get; init; }
    /// <summary>Diferença B − A por célula (nula sem as duas superfícies).</summary>
    public required double?[] Diferenca { get; init; }

    public double? Valor(int i, int j) => Diferenca[j * Grade.Nx + i];

    /// <summary>B − A em cada célula (centro), volumes pela soma das células e a distribuição nas classes.</summary>
    public static ComparacaoSuperficies Comparar(Func<Vec2, double?> a, Func<Vec2, double?> b, Vec2 min, Vec2 max, double passo, Polygon2? regiao = null,
        IReadOnlyList<ClasseTema>? classes = null, int maxCelulas = 40000)
    {
        var g = TemasTerreno.Amostrar(a, min, max, passo, regiao, maxCelulas);
        var diff = new double?[g.Nx * g.Ny];
        var res = new ComparacaoSuperficies { Grade = g, Diferenca = diff };
        double mn = double.MaxValue, mx = double.MinValue;
        for (int j = 0; j < g.Ny; j++)
            for (int i = 0; i < g.Nx; i++)
            {
                if (!g.Usa(i, j)) continue;
                var c = g.Canto(i, j) + new Vec2(g.Passo / 2, g.Passo / 2);
                if (a(c) is not { } za || b(c) is not { } zb) continue;
                var d = zb - za;
                diff[j * g.Nx + i] = d;
                res.Area += g.AreaCelula;
                if (d > 0) res.Aterro += d * g.AreaCelula; else res.Corte -= d * g.AreaCelula;
                mn = Math.Min(mn, d); mx = Math.Max(mx, d);
            }
        if (res.Area > 0) { res.Min = mn; res.Max = mx; }
        var cls = classes ?? TemasTerreno.ClassesPadrao(TemaTerreno.Diferenca, res.Min, res.Max);
        foreach (var c in cls) res.Distribuicao.Add((c, 0));
        for (int k = 0; k < diff.Length; k++)
        {
            if (diff[k] is not { } d) continue;
            var ci = TemasTerreno.ClasseDe(cls, TemaTerreno.Diferenca, d);
            if (ci >= 0) res.Distribuicao[ci] = (res.Distribuicao[ci].Classe, res.Distribuicao[ci].Area + g.AreaCelula);
        }
        return res;
    }
}
