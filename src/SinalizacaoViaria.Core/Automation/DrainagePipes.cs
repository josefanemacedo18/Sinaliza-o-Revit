using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Tubo do catálogo do Projeto de Drenagem (Etapa 22): material, classe, diâmetro interno, parede, comprimento da peça, junta e
/// rugosidade – os valores e a fonte são os do usuário; sem eles, os do padrão do plugin (<see cref="CatalogoTubos.Padrao"/>).
/// </summary>
public sealed class TuboCatalogo
{
    /// <summary>Código único no catálogo (o trecho guarda este código).</summary>
    public string Codigo { get; set; } = "";
    /// <summary>Material como sai no quantitativo ("concreto", "PEAD corrugado"...).</summary>
    public string Material { get; set; } = "concreto";
    /// <summary>Classe ou série, como o usuário escrever (ex.: a classe de resistência do fabricante); vazio = sem classe.</summary>
    public string Classe { get; set; } = "";
    /// <summary>Diâmetro interno (m).</summary>
    public double Diametro { get; set; } = 0.40;
    /// <summary>Espessura da parede (m); nula = a do desenho (<see cref="DrainageNetwork.Wall"/>, 8 % do diâmetro, mínimo 4 cm).</summary>
    public double? Parede { get; set; }
    /// <summary>Comprimento útil de uma peça (m).</summary>
    public double ComprimentoPeca { get; set; } = CatalogoTubos.ComprimentoPecaPadrao;
    /// <summary>Tipo de junta ("ponta e bolsa", "macho e fêmea", "elástica"...).</summary>
    public string Junta { get; set; } = "ponta e bolsa";
    /// <summary>Rugosidade de Manning do tubo; nula = a do Projeto de Drenagem.</summary>
    public double? Rugosidade { get; set; }
    /// <summary>Fonte dos dados (fabricante, norma citada pelo usuário, "padrão do plugin").</summary>
    public string? Fonte { get; set; }

    public double ParedeEfetiva => Parede ?? DrainageNetwork.Wall(Diametro);
    public double DiametroExterno => Diametro + 2 * ParedeEfetiva;

    /// <summary>Nome no quantitativo: "Tubo de concreto DN 400 (ponta e bolsa)", com a classe quando houver.</summary>
    public string Nome => $"Tubo de {Material.Trim()} DN {(Diametro * 1000).ToString("0", CultureInfo.InvariantCulture)}" +
                          (string.IsNullOrWhiteSpace(Classe) ? "" : " " + Classe.Trim()) + (string.IsNullOrWhiteSpace(Junta) ? "" : $" ({Junta.Trim()})");

    public TuboCatalogo Clone() => (TuboCatalogo)MemberwiseClone();
}

/// <summary>Vala de um trecho: largura do fundo, profundidade média, volumes de escavação, berço, tubo e reaterro, área escorada.</summary>
public sealed record ValaTrecho(double Largura, double ProfundidadeMedia, double Escavacao, double Berco, double VolumeTubo, double Reaterro, double Escoramento);

/// <summary>Tubo, peças, juntas, conexões e vala de um trecho medido.</summary>
public sealed record TuboTrecho(string Id, string Nome, string Montante, string Jusante, TuboCatalogo Tubo, bool DoCatalogo, double Extensao,
    double ExtensaoTubo, int Pecas, int Juntas, int Conexoes, ValaTrecho Vala);

/// <summary>
/// Catálogo de tubos, peças e valas (Etapa 22, item 6). O tubo de cada trecho é o do código gravado nele, ou o do catálogo com
/// o mesmo diâmetro, ou um tubo de concreto do padrão do plugin. As peças contam o tubo entre as paredes das caixas; a extensão
/// do quantitativo continua entre os centros dos dispositivos (como na planilha do memorial modelo). A vala tem a largura de
/// <see cref="ProjetoDrenagem.LarguraValaDiametros"/> diâmetros (no mínimo o diâmetro externo), vai até o fundo externo do tubo
/// mais o berço e tem o talude informado.
/// </summary>
public static class CatalogoTubos
{
    /// <summary>Comprimento da peça do catálogo padrão (m) – padrão do plugin, editável no catálogo.</summary>
    public const double ComprimentoPecaPadrao = 1.0;
    public const string FontePadrao = "padrão do plugin";

    private static string Mm(double d) => (d * 1000).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Catálogo padrão do plugin: um tubo de concreto ponta e bolsa por diâmetro comercial do projeto, peças de 1 m.</summary>
    public static List<TuboCatalogo> Padrao(ProjetoDrenagem p) => p.Diametros.Distinct().OrderBy(d => d)
        .Select(d => new TuboCatalogo { Codigo = "CONC-" + Mm(d), Material = "concreto", Diametro = d, Junta = "ponta e bolsa", Fonte = FontePadrao }).ToList();

    /// <summary>O catálogo em uso: o do projeto ou, vazio, o padrão.</summary>
    public static IReadOnlyList<TuboCatalogo> Lista(ProjetoDrenagem? p) => p == null ? Padrao(new ProjetoDrenagem()) : p.Tubos.Count > 0 ? p.Tubos : Padrao(p);

    /// <summary>
    /// Tubo do trecho: o do código gravado (se tem o diâmetro do trecho), o do mesmo material e diâmetro, o primeiro do catálogo
    /// com o diâmetro, ou o concreto do padrão do plugin (<c>DoCatalogo = false</c>).
    /// </summary>
    public static (TuboCatalogo Tubo, bool DoCatalogo) Do(DrainagePipeDefinition pipe, ProjetoDrenagem? p)
    {
        var lista = Lista(p);
        bool Mesmo(TuboCatalogo t) => Math.Abs(t.Diametro - pipe.Diameter) < 5e-4;
        var gravado = pipe.Tubo == null ? null : lista.FirstOrDefault(t => t.Codigo == pipe.Tubo);
        if (gravado != null && Mesmo(gravado)) return (gravado, true);
        if (gravado != null && lista.FirstOrDefault(t => Mesmo(t) && t.Material == gravado.Material && t.Classe == gravado.Classe) is { } irmao) return (irmao, true);
        if (lista.FirstOrDefault(Mesmo) is { } igual) return (igual, true);
        return (new TuboCatalogo { Codigo = "", Material = "concreto", Diametro = pipe.Diameter, Junta = "ponta e bolsa", Fonte = FontePadrao }, false);
    }

    /// <summary>Peças de comprimento <paramref name="peca"/> para cobrir <paramref name="extensao"/> (a última pode ser cortada).</summary>
    public static int Pecas(double extensao, double peca) => extensao <= 1e-6 ? 0 : (int)Math.Ceiling(extensao / Math.Max(0.1, peca) - 1e-6);

    /// <summary>
    /// Vala de um trecho: profundidade a cada metro do topo (<paramref name="superficie"/> ou a reta entre as tampas) até o fundo
    /// externo do tubo menos o berço; seção (largura + talude × h) × h; escoramento das duas paredes onde a profundidade média
    /// passa da de escoramento (só com paredes verticais).
    /// </summary>
    public static ValaTrecho Vala(PontasTrecho e, double comprimento, TuboCatalogo tubo, ProjetoDrenagem p, Func<Vec2, double?>? superficie = null)
    {
        var parede = tubo.ParedeEfetiva;
        var largura = Math.Max(tubo.DiametroExterno, p.LarguraValaDiametros * tubo.Diametro);
        var berco = Math.Max(0, p.EspessuraBerco);
        var m = Math.Max(0, p.TaludeVala);
        var n = Math.Max(1, (int)Math.Ceiling(comprimento));
        double area = 0, prof = 0;
        double Prof(double t)
        {
            var q = e.A + (e.B - e.A) * t;
            var topo = superficie?.Invoke(q) ?? e.RimA + (e.RimB - e.RimA) * t;
            var fundo = e.InvertA + (e.InvertB - e.InvertA) * t - parede - berco;
            return Math.Max(0, topo - fundo);
        }
        for (int i = 0; i <= n; i++)
        {
            var h = Prof((double)i / n);
            var w = i == 0 || i == n ? 0.5 : 1.0;
            area += w * (largura + m * h) * h;
            prof += w * h;
        }
        area /= n;
        prof /= n;
        var esc = area * comprimento;
        var r = tubo.DiametroExterno / 2;
        var vt = Math.PI * r * r * comprimento;
        var vb = largura * berco * comprimento;
        var escor = m <= 1e-9 && prof > p.ProfundidadeEscoramento ? 2 * comprimento * prof : 0;
        return new ValaTrecho(largura, prof, esc, vb, vt, Math.Max(0, esc - vt - vb), escor);
    }

    /// <summary>Cabeçalho do catálogo em texto (uma linha por tubo, campos separados por ";").</summary>
    public const string CabecalhoTexto = "código; material; classe; DN (mm); parede (mm, vazio = do desenho); peça (m); junta; n (vazio = do projeto); fonte";

    private static string F(double v, string f) => v.ToString(f, CultureInfo.GetCultureInfo("pt-BR"));

    /// <summary>O catálogo como texto editável (o formato que <see cref="Ler"/> lê).</summary>
    public static string Texto(IEnumerable<TuboCatalogo> lista) => string.Join("\n", lista.Select(t => string.Join("; ", new[]
    {
        t.Codigo, t.Material, t.Classe, F(t.Diametro * 1000, "0"), t.Parede is { } e ? F(e * 1000, "0.#") : "", F(t.ComprimentoPeca, "0.##"), t.Junta,
        t.Rugosidade is { } n ? F(n, "0.0000") : "", t.Fonte ?? "",
    })));

    /// <summary>
    /// Lê o catálogo em texto: código; material; classe; DN (mm); parede (mm); peça (m); junta; n; fonte – vírgula ou ponto
    /// decimal. Linhas vazias e a do cabeçalho são ignoradas; cada linha inválida vai para <paramref name="erros"/> e fica de fora.
    /// </summary>
    public static List<TuboCatalogo> Ler(string? texto, out List<string> erros)
    {
        erros = new List<string>();
        var res = new List<TuboCatalogo>();
        static double? Num(string s) => double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        var k = 0;
        foreach (var bruta in (texto ?? "").Replace("\r", "").Split('\n'))
        {
            k++;
            var linha = bruta.Trim();
            if (linha.Length == 0 || linha.StartsWith("código", StringComparison.OrdinalIgnoreCase)) continue;
            var c = linha.Split(';').Select(x => x.Trim()).ToList();
            while (c.Count < 9) c.Add("");
            if (c[0].Length == 0) { erros.Add($"linha {k}: sem código"); continue; }
            if (Num(c[3]) is not { } dn || dn < 50 || dn > 5000) { erros.Add($"linha {k} ({c[0]}): DN em mm entre 50 e 5000"); continue; }
            double? parede = c[4].Length == 0 ? null : Num(c[4]) is { } e && e > 0 && e < 1000 ? e / 1000 : double.NaN;
            if (parede is double.NaN) { erros.Add($"linha {k} ({c[0]}): parede em mm (vazio = a do desenho)"); continue; }
            var peca = c[5].Length == 0 ? ComprimentoPecaPadrao : Num(c[5]) ?? double.NaN;
            if (!(peca >= 0.1 && peca <= 50)) { erros.Add($"linha {k} ({c[0]}): peça em m entre 0,1 e 50"); continue; }
            double? n = c[7].Length == 0 ? null : Num(c[7]) is { } nn && nn > 0.005 && nn < 0.1 ? nn : double.NaN;
            if (n is double.NaN) { erros.Add($"linha {k} ({c[0]}): n de Manning entre 0,005 e 0,1 (vazio = o do projeto)"); continue; }
            if (res.Any(t => t.Codigo == c[0])) { erros.Add($"linha {k}: código {c[0]} repetido"); continue; }
            res.Add(new TuboCatalogo
            {
                Codigo = c[0], Material = c[1].Length == 0 ? "concreto" : c[1], Classe = c[2], Diametro = dn / 1000, Parede = parede, ComprimentoPeca = peca,
                Junta = c[6], Rugosidade = n, Fonte = c[8].Length == 0 ? null : c[8],
            });
        }
        return res;
    }

    /// <summary>Bloco do CSV: tubo, peças, juntas, conexões e vala por trecho.</summary>
    public static string Csv(IReadOnlyList<TuboTrecho> trechos)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("TUBOS, PEÇAS E VALAS POR TRECHO (estimativas; extensão entre os centros, peças entre as paredes das caixas)");
        sb.AppendLine("Trecho;Montante;Jusante;Tubo;Do catálogo;Extensão (m);Tubo entre caixas (m);Peças;Juntas;Conexões;Largura da vala (m);Profundidade média (m);Escavação (m³);Berço (m³);Volume do tubo (m³);Reaterro (m³);Escoramento (m²)");
        foreach (var t in trechos)
            sb.AppendLine(string.Join(";", t.Nome, t.Montante, t.Jusante, t.Tubo.Nome, t.DoCatalogo ? "sim" : "não (padrão do plugin)", F(t.Extensao, "0.00"),
                F(t.ExtensaoTubo, "0.00"), t.Pecas, t.Juntas, t.Conexoes, F(t.Vala.Largura, "0.00"), F(t.Vala.ProfundidadeMedia, "0.00"), F(t.Vala.Escavacao, "0.00"),
                F(t.Vala.Berco, "0.00"), F(t.Vala.VolumeTubo, "0.00"), F(t.Vala.Reaterro, "0.00"), F(t.Vala.Escoramento, "0.00")));
        return sb.ToString();
    }

    /// <summary>Tubo, peças, juntas, conexões e vala de cada trecho medido, de montante para jusante.</summary>
    public static List<TuboTrecho> Trechos(IReadOnlyCollection<MarkingDefinition> defs, ProjetoDrenagem? p, Func<Vec2, double?>? superficie = null)
    {
        p ??= new ProjetoDrenagem();
        var byId = defs.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var res = new List<TuboTrecho>();
        foreach (var t in DrainageNetwork.Analyze(defs))
        {
            if (byId.GetValueOrDefault(t.Id) is not DrainagePipeDefinition pipe || DrainageNetwork.Ends(pipe, id => byId.GetValueOrDefault(id)) is not { } e) continue;
            var (tubo, cat) = Do(pipe, p);
            var lt = Math.Max(0, t.Comprimento - e.InsetA - e.InsetB);
            var pecas = Pecas(lt, tubo.ComprimentoPeca);
            res.Add(new TuboTrecho(t.Id, t.Nome, t.Montante, t.Jusante, tubo, cat, t.Comprimento, lt, pecas, Math.Max(0, pecas - 1), pecas > 0 ? 2 : 0,
                Vala(e, t.Comprimento, tubo, p, superficie)));
        }
        return res;
    }
}

/// <summary>Linha piezométrica num trecho: níveis d'água e de energia nas pontas, lâmina normal, velocidade e regime.</summary>
public sealed record PiezometricaTrecho(string Id, string Nome, string Montante, string Jusante, double Vazao, double Diametro, double? LaminaNormal,
    double FundoMontante, double FundoJusante, double NivelMontante, double NivelJusante, double EnergiaMontante, double EnergiaJusante,
    double Velocidade, bool SobPressao)
{
    /// <summary>Geratriz superior interna submersa em alguma ponta.</summary>
    public bool Afogado => NivelMontante > FundoMontante + Diametro + 1e-6 || NivelJusante > FundoJusante + Diametro + 1e-6;
}

/// <summary>Nível d'água num dispositivo pela linha piezométrica, diante da tampa.</summary>
public sealed record PiezometricaDispositivo(string Id, string Rotulo, double Tampa, double Nivel, double Energia)
{
    public double Folga => Tampa - Nivel;
    public bool Extravasa => Nivel > Tampa + 1e-6;
}

/// <summary>Resultado da linha piezométrica (nulo sem a equação de chuva: sem vazões).</summary>
public sealed class ResultadoPiezometrica
{
    public bool Calculada { get; init; }
    public List<PiezometricaTrecho> Trechos { get; } = new();
    public List<PiezometricaDispositivo> Dispositivos { get; } = new();
    /// <summary>Condição de jusante de cada lançamento (rótulo → texto).</summary>
    public Dictionary<string, string> Lancamentos { get; } = new();
    public List<ProblemaRede> Problemas { get; } = new();
}

/// <summary>
/// Linha piezométrica e de energia da rede (Etapa 22), de jusante para montante, com as vazões do método racional
/// (<see cref="DrainageHydraulics.Calcular"/>). No lançamento, o nível d'água informado do corpo d'água a menos de
/// <see cref="Model.ModelValidator.DistanciaLancamento"/> m ou, sem ele, a saída livre (a lâmina normal do último trecho). Em cada
/// trecho: lâmina normal por Manning na seção circular parcialmente cheia; se o nível de jusante passa da geratriz superior, ou
/// a vazão passa da máxima da seção parcial, o trecho fica sob pressão e o nível sobe pela perda por atrito a seção plena
/// (Manning: Sf = (Q·n / (A·R^⅔))²); com remanso abaixo da geratriz, o nível de jusante se estende em nível até o montante
/// (simplificação sem a curva de remanso). Na entrada do trecho, a perda localizada K·V²/2g do Projeto de Drenagem.
/// </summary>
public static class LinhaPiezometrica
{
    public const double G = 9.81;
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string N(double v, string f = "0.00") => v.ToString(f, Pt);

    /// <summary>Área e perímetro molhados de um tubo de diâmetro <paramref name="d"/> com lâmina <paramref name="y"/>.</summary>
    public static (double Area, double Perimetro) SecaoParcial(double d, double y)
    {
        var yy = Math.Clamp(y, 0, d);
        var th = 2 * Math.Acos(Math.Clamp(1 - 2 * yy / d, -1, 1));
        return (d * d / 8 * (th - Math.Sin(th)), d * th / 2);
    }

    /// <summary>Vazão (m³/s) por Manning com a lâmina <paramref name="y"/>.</summary>
    public static double Vazao(double d, double y, double s, double n)
    {
        if (s <= 0 || n <= 0 || y <= 0) return 0;
        var (a, per) = SecaoParcial(d, y);
        return per < 1e-12 ? 0 : a * Math.Pow(a / per, 2.0 / 3) * Math.Sqrt(s) / n;
    }

    /// <summary>
    /// Lâmina normal (m) para a vazão <paramref name="q"/> (m³/s); nula com declividade nula ou negativa ou vazão acima da
    /// máxima da seção parcial (a 0,938 D).
    /// </summary>
    public static double? LaminaNormal(double q, double d, double s, double n)
    {
        if (q <= 1e-12) return 0;
        if (s <= 0 || d <= 0 || n <= 0) return null;
        var ymax = 0.938 * d;
        if (Vazao(d, ymax, s, n) < q) return null;
        double lo = 0, hi = ymax;
        for (int i = 0; i < 80; i++)
        {
            var mid = (lo + hi) / 2;
            if (Vazao(d, mid, s, n) < q) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    public static ResultadoPiezometrica Calcular(IReadOnlyCollection<MarkingDefinition> defs, ProjetoDrenagem p, ResultadoDrenagem? hidraulica = null)
    {
        if (!p.TemChuva) return new ResultadoPiezometrica { Calculada = false };
        var hid = hidraulica ?? DrainageHydraulics.Calcular(defs, p);
        var res = new ResultadoPiezometrica { Calculada = true };
        var byId = defs.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var labels = DrainageNetwork.Labels(defs);
        var porId = hid.Trechos.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        var tubos = DrainageNetwork.PipeOrder(defs).Where(t => t.FromId != t.ToId && porId.ContainsKey(t.Id)).ToList();
        var saem = tubos.Select(t => t.FromId).ToHashSet();
        var aguas = defs.OfType<CorpoDaguaDefinition>().Where(a => a.PathRef.Points.Count >= 2).ToList();
        var nivel = new Dictionary<string, double>();
        var energia = new Dictionary<string, double>();
        var chegada = new Dictionary<string, double>();
        var energiaChegada = new Dictionary<string, double>();
        string L(string id) => labels.GetValueOrDefault(id, byId.GetValueOrDefault(id)?.DisplayCode ?? id);

        // Lançamentos: o nível do corpo d'água mais alto a menos da distância de lançamento.
        foreach (var fim in tubos.Select(t => t.ToId).Distinct().Where(id => !saem.Contains(id)))
        {
            if (byId.GetValueOrDefault(fim) is not DrainageDefinition dv) continue;
            double? tw = null;
            string? nome = null;
            foreach (var a in aguas)
            {
                var path = new Polyline2(a.PathRef.Points, !a.EhRio);
                var agua = CorposDagua.Agua(a, path);
                var dist = agua.Count == 0 ? double.MaxValue : agua.Min(g => g.Contains(dv.Position) ? 0 : Distancia(dv.Position, g));
                if (dist > Model.ModelValidator.DistanciaLancamento) continue;
                var z = CorposDagua.NivelPerto(a, path, dv.Position, null);
                if (tw == null || z > tw) { tw = z; nome = a.KindName + (string.IsNullOrWhiteSpace(a.Nome) ? "" : " " + a.Nome.Trim()); }
            }
            if (tw is { } z0)
            {
                nivel[fim] = z0;
                res.Lancamentos[L(fim)] = $"nível d'água informado de {nome}: {N(z0, "0.000")} m";
            }
            else res.Lancamentos[L(fim)] = "saída livre (lâmina normal do último trecho)";
        }

        // De jusante para montante.
        for (int k = tubos.Count - 1; k >= 0; k--)
        {
            var pipe = tubos[k];
            var h = porId[pipe.Id];
            var q = Math.Max(0, h.Vazao) / 1000;
            var d = Math.Max(0.05, h.Diametro);
            var n = CatalogoTubos.Do(pipe, p).Tubo.Rugosidade ?? p.Rugosidade;
            var s = h.Declividade;
            var area = Math.PI * d * d / 4;
            var vPlena = q / area;
            var sf = Math.Pow(q * n / (area * Math.Pow(d / 4, 2.0 / 3)), 2);
            var yn = LaminaNormal(q, d, s, n);
            var (zm, zj) = (h.FundoMontante, h.FundoJusante);
            var hj = zj + (yn ?? d);
            if (nivel.TryGetValue(pipe.ToId, out var hdev)) hj = Math.Max(hj, hdev);
            var pressao = yn == null || hj > zj + d + 1e-9;
            var hm = Math.Max(zm + (yn ?? d), pressao ? hj + sf * h.Extensao : hj);
            double V(double nivelAgua, double fundo)
            {
                var y = nivelAgua - fundo;
                if (y >= d - 1e-9) return vPlena;
                var a = SecaoParcial(d, y).Area;
                return a > 1e-9 ? q / a : 0;
            }
            var vm = V(hm, zm);
            var vj = V(hj, zj);
            var em = hm + vm * vm / (2 * G);
            var ej = hj + vj * vj / (2 * G);
            res.Trechos.Add(new PiezometricaTrecho(pipe.Id, h.Nome, h.Montante, h.Jusante, h.Vazao, d, yn, zm, zj, hm, hj, em, ej, vm, pressao));
            var hu = hm + Math.Max(0, p.PerdaPv) * vm * vm / (2 * G);
            nivel[pipe.FromId] = Math.Max(nivel.GetValueOrDefault(pipe.FromId, double.NegativeInfinity), hu);
            energia[pipe.FromId] = Math.Max(energia.GetValueOrDefault(pipe.FromId, double.NegativeInfinity), em + (hu - hm));
            // Nos lançamentos sem nível d'água, o nível da caixa é o maior dos trechos que chegam (depois de todos).
            chegada[pipe.ToId] = Math.Max(chegada.GetValueOrDefault(pipe.ToId, double.NegativeInfinity), hj);
            energiaChegada[pipe.ToId] = Math.Max(energiaChegada.GetValueOrDefault(pipe.ToId, double.NegativeInfinity), ej);
        }
        res.Trechos.Reverse();
        foreach (var (id, z) in chegada) nivel.TryAdd(id, z);
        foreach (var (id, z) in energiaChegada) energia.TryAdd(id, z);

        foreach (var (id, z) in nivel)
        {
            if (byId.GetValueOrDefault(id) is not DrainageDefinition dv) continue;
            res.Dispositivos.Add(new PiezometricaDispositivo(id, L(id), dv.Z, z, energia.GetValueOrDefault(id, z)));
        }
        res.Dispositivos.Sort((a, b) => string.CompareOrdinal(a.Rotulo, b.Rotulo));

        // Problemas: trecho afogado pelo remanso (a falta de capacidade já é apontada no cálculo) e dispositivo extravasando.
        foreach (var t in res.Trechos.Where(t => t.Afogado))
        {
            var h = porId[t.Id];
            if (h.Vazao > h.VazaoMaxima + 1e-6) continue;
            var id = $"{t.Nome} ({t.Montante} → {t.Jusante})";
            res.Problemas.Add(new(id, "afogamento", $"{id}: linha piezométrica acima da geratriz superior do tubo (nível {N(Math.Max(t.NivelMontante, t.NivelJusante), "0.000")} m) " +
                "pelo remanso de jusante – o trecho trabalha sob pressão.", false, new[] { t.Id }));
        }
        foreach (var dv in res.Dispositivos.Where(x => x.Extravasa))
            res.Problemas.Add(new(dv.Rotulo, "extravasamento", $"{dv.Rotulo}: linha piezométrica em {N(dv.Nivel, "0.000")} m, {N(-dv.Folga)} m acima da tampa ({N(dv.Tampa, "0.000")} m) – " +
                "o dispositivo extravasa.", false, new[] { dv.Id }));
        return res;
    }

    /// <summary>Bloco do CSV: a linha piezométrica por trecho, os níveis nos dispositivos e a condição de cada lançamento.</summary>
    public static string Csv(ResultadoPiezometrica r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("LINHA PIEZOMÉTRICA (de jusante para montante; nível d'água e de energia, m)");
        if (!r.Calculada) { sb.AppendLine("não calculada: sem equação de chuva no Projeto de Drenagem"); return sb.ToString(); }
        sb.AppendLine("Trecho;Montante;Jusante;Vazão (L/s);Diâmetro (m);Lâmina normal (m);Fundo M;Fundo J;Nível M;Nível J;Energia M;Energia J;Velocidade M (m/s);Regime");
        foreach (var t in r.Trechos)
            sb.AppendLine(string.Join(";", t.Nome, t.Montante, t.Jusante, N(t.Vazao), N(t.Diametro), t.LaminaNormal is { } y ? N(y, "0.000") : "acima da seção",
                N(t.FundoMontante, "0.000"), N(t.FundoJusante, "0.000"), N(t.NivelMontante, "0.000"), N(t.NivelJusante, "0.000"), N(t.EnergiaMontante, "0.000"),
                N(t.EnergiaJusante, "0.000"), N(t.Velocidade), t.Afogado ? "sob pressão" : "livre"));
        sb.AppendLine("Dispositivo;Tampa (m);Nível d'água (m);Energia (m);Folga até a tampa (m);Extravasa");
        foreach (var d in r.Dispositivos)
            sb.AppendLine(string.Join(";", d.Rotulo, N(d.Tampa, "0.000"), N(d.Nivel, "0.000"), N(d.Energia, "0.000"), N(d.Folga), d.Extravasa ? "sim" : "não"));
        foreach (var (l, c) in r.Lancamentos) sb.AppendLine($"Lançamento {l};{c}");
        return sb.ToString();
    }

    private static double Distancia(Vec2 p, Polygon2 g)
    {
        var best = double.MaxValue;
        var o = g.Outer;
        for (int i = 0; i < o.Count; i++)
        {
            var (a, b) = (o[i], o[(i + 1) % o.Count]);
            var ab = b - a;
            var l2 = ab.Dot(ab);
            var u = l2 < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
            best = Math.Min(best, p.DistanceTo(a + ab * u));
        }
        return best;
    }
}
