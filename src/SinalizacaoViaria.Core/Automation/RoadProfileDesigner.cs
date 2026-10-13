using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Como o greide da via é definido na ferramenta Perfil da via.</summary>
public enum ModoGreide
{
    /// <summary>Acompanha o terreno suavizado, limitado pela rampa máxima (menor movimento de terra).</summary>
    AcompanharTerreno,
    /// <summary>Rampa constante entre as cotas das pontas.</summary>
    RampaConstante,
    /// <summary>Em nível numa cota única.</summary>
    Nivelado,
    /// <summary>PIVs digitados (estaca; cota; curva vertical).</summary>
    Manual,
    /// <summary>Acompanha o terreno suavizado com o greide subido/descido até o corte compensar o aterro (menos bota-fora e empréstimo).</summary>
    Compensado,
}

/// <summary>Relevo da via ao ser criada sobre o terreno nativo (Toposolid).</summary>
public enum RelevoVia
{
    /// <summary>Via plana na cota do eixo – o terreno não é alterado.</summary>
    Plana,
    /// <summary>Greide colado no terreno (suavização curta): a via sobe e desce com a topografia; a plataforma fica em nível
    /// transversal com pequenos cortes/aterros laterais.</summary>
    AcompanharTerreno,
    /// <summary>Greide suavizado com rampa máxima e curvas verticais: cortes e aterros com taludes no Toposolid.</summary>
    GreideSuavizado,
}

/// <summary>O que o trecho da via vira conforme a topografia.</summary>
public enum TipoTrecho { Plataforma, Aterro, Corte, Viaduto, Tunel, Trincheira }

public sealed record TrechoVia(TipoTrecho Kind, double S0, double S1, double MaxHeight)
{
    public double Length => S1 - S0;
}

/// <summary>Opções da ferramenta Perfil da via.</summary>
public sealed class PerfilOpcoes
{
    public PerfilOpcoes Clone()
    {
        var c = (PerfilOpcoes)MemberwiseClone();
        c.Fixed = new List<Vec2>(Fixed);
        c.FixedHalf = new List<double>(FixedHalf);
        c.FixedSlope = new List<double>(FixedSlope);
        return c;
    }

    public ModoGreide Mode { get; set; } = ModoGreide.AcompanharTerreno;
    public double MaxGrade { get; set; } = 0.06;
    public double VerticalCurve { get; set; } = 80;
    /// <summary>Janela de suavização do terreno (m).</summary>
    public double Smoothing { get; set; } = 120;
    /// <summary>Alteamento/rebaixo geral do greide em relação ao terreno suavizado (m).</summary>
    public double Offset { get; set; }
    /// <summary>Cotas nas pontas (relativas à base). Nulo = no terreno.</summary>
    public double? StartZ { get; set; }
    public double? EndZ { get; set; }
    /// <summary>Cota do greide nivelado (m).</summary>
    public double LevelZ { get; set; }
    /// <summary>PIVs manuais, um por linha: "estaca; cota; curva".</summary>
    public string ManualPvis { get; set; } = "";
    public double Crossfall { get; set; } = 0.02;
    public double CutSlope { get; set; } = 1.0;
    public double FillSlope { get; set; } = 1.5;
    /// <summary>
    /// Parâmetro K das curvas verticais (m por % de variação de rampa; 0 = usar <see cref="VerticalCurve"/>): L = K·|Δi|.
    /// DNIT (urbano ~40 km/h): K ≥ 4–7 (convexa); rodovias 80 km/h: K ≥ 24–30.
    /// </summary>
    public double CurveK { get; set; }
    /// <summary>Fator de homogeneização usado no modo compensado (corte ÷ aterro).</summary>
    public double Homogenization { get; set; } = 1.25;

    /// <summary>
    /// Pontos obrigados (estaca, cota relativa): cruzamentos e entroncamentos com vias existentes – o greide passa exatamente
    /// na cota da outra via ali, concordando suavemente numa janela de <see cref="FixedBlend"/> m.
    /// </summary>
    public List<Vec2> Fixed { get; set; } = new();
    /// <summary>
    /// Meia extensão do patamar de cada ponto obrigado (mesma ordem de <see cref="Fixed"/>; faltando = 6 m): o alcance da área do
    /// nó ao longo da via (<see cref="RoadProfileDesigner.NodeReach"/>) – num Y a área do nó vai bem além de 6 m. Negativo
    /// (<see cref="RoadProfileDesigner.PassThrough"/>) = sem patamar: a via é a principal do nó e passa com a própria rampa.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<double> FixedHalf { get; set; } = new();
    /// <summary>
    /// Rampa do patamar de cada ponto obrigado (mesma ordem de <see cref="Fixed"/>; faltando = 0, em nível): a da superfície do
    /// nó ao longo do eixo da via secundária – a rampa da principal projetada no eixo dela (zero num cruzamento em ângulo reto).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<double> FixedSlope { get; set; } = new();
    public double FixedBlend { get; set; } = 40;

    // ---- obras automáticas
    public bool AutoStructures { get; set; } = true;
    /// <summary>Aterro acima deste valor vira viaduto/ponte (m).</summary>
    public double BridgeFill { get; set; } = 8.0;
    /// <summary>Corte acima deste valor vira túnel (m de terreno sobre o greide).</summary>
    public double TunnelCut { get; set; } = 18.0;
    /// <summary>Trincheira (muros de contenção) em cortes acima deste valor; 0 = taludes de corte.</summary>
    public double TrenchCut { get; set; }
    /// <summary>Comprimento mínimo de uma obra (m); trechos menores viram aterro/corte.</summary>
    public double MinStructure { get; set; } = 30;
    /// <summary>Obras próximas (vão menor que este) são unidas numa só (m).</summary>
    public double MergeGap { get; set; } = 40;
    /// <summary>Ponte (em vez de viaduto) nos trechos elevados.</summary>
    public bool Rivers { get; set; }
}

/// <summary>Greide e trechos de obra propostos para uma via.</summary>
public sealed class PerfilResultado
{
    public RoadGrade Grade { get; init; } = new();
    public List<TrechoVia> Segments { get; } = new();
    public List<Vec2> Ground { get; } = new();
    public double CutM2 { get; set; }
    public double FillM2 { get; set; }
    public List<string> Warnings { get; } = new();

    public string Summary()
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        string Km(double s) => $"{(int)(s / 1000)}+{s % 1000:000}";
        var lines = Segments.Where(x => x.Kind is TipoTrecho.Viaduto or TipoTrecho.Tunel or TipoTrecho.Trincheira)
            .Select(x => $"{Nome(x.Kind)}: {Km(x.S0)} a {Km(x.S1)} ({x.Length.ToString("0", pt)} m, até {x.MaxHeight.ToString("0.0", pt)} m)").ToList();
        var gmax = 0.0;
        for (var s = 0.0; s < (Ground.Count > 0 ? Ground[^1].X : 0); s += 5) gmax = Math.Max(gmax, Math.Abs(Grade.GradeAt(s)));
        lines.Insert(0, $"Rampa máxima do greide: {(gmax * 100).ToString("0.0", pt)} %");
        return string.Join("\n", lines);
    }

    public static string Nome(TipoTrecho t) => t switch
    {
        TipoTrecho.Viaduto => "Viaduto/ponte",
        TipoTrecho.Tunel => "Túnel",
        TipoTrecho.Trincheira => "Trincheira",
        TipoTrecho.Aterro => "Aterro",
        TipoTrecho.Corte => "Corte",
        _ => "Plataforma",
    };
}

/// <summary>
/// Perfil longitudinal da via: lê o terreno ao longo do eixo, propõe o greide (acompanhando o terreno com rampa máxima e
/// curvas verticais, em rampa constante, nivelado ou por PIVs) e decide onde a via vira obra – viaduto/ponte nos aterros
/// altos, túnel nos cortes profundos, trincheira (muros) nos cortes médios – o resto é aterro/corte no Toposolid.
/// </summary>
public static class RoadProfileDesigner
{
    public const double Step = 5;

    /// <summary>Opções do perfil para o relevo escolhido na criação da via (sem obras automáticas).</summary>
    public static PerfilOpcoes ForRelief(RelevoVia r, double maxGrade, double cut, double fill) => new()
    {
        Mode = ModoGreide.AcompanharTerreno,
        Smoothing = r == RelevoVia.AcompanharTerreno ? 40 : 150,
        VerticalCurve = r == RelevoVia.AcompanharTerreno ? 40 : 80,
        MaxGrade = r == RelevoVia.AcompanharTerreno ? 0.30 : maxGrade,
        CutSlope = cut,
        FillSlope = fill,
        AutoStructures = false,
    };

    public static PerfilResultado Design(double length, Func<double, double?> groundAt, PerfilOpcoes o)
    {
        if (o.Mode == ModoGreide.Compensado)
        {
            // Busca do alteamento que equilibra corte e aterro (áreas no eixo, aterro × fator de homogeneização).
            var baseOpt = o.Clone();
            baseOpt.Mode = ModoGreide.AcompanharTerreno;
            double Balance(double off) { baseOpt.Offset = off; var r0 = DesignCore(length, groundAt, baseOpt); return r0.CutM2 - r0.FillM2 * o.Homogenization; }
            double lo = -15, hi = 15;
            var blo = Balance(lo);
            for (int i = 0; i < 40 && hi - lo > 0.01; i++)
            {
                var mid = (lo + hi) / 2;
                var bm = Balance(mid);
                // Subir o greide diminui o corte e aumenta o aterro (balanço decrescente).
                if (Math.Sign(bm) == Math.Sign(blo)) { lo = mid; blo = bm; } else hi = mid;
            }
            baseOpt.Offset = (lo + hi) / 2;
            var res = DesignCore(length, groundAt, baseOpt);
            res.Warnings.Insert(0, $"Greide compensado: alteamento de {baseOpt.Offset.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"))} m sobre o terreno suavizado.");
            return res;
        }
        return DesignCore(length, groundAt, o);
    }

    private static PerfilResultado DesignCore(double length, Func<double, double?> groundAt, PerfilOpcoes o)
    {
        var L = Math.Max(1, length);
        var n = Math.Max(2, (int)Math.Ceiling(L / Step));
        var ss = Enumerable.Range(0, n + 1).Select(i => L * i / n).ToList();
        var raw = ss.Select(s => groundAt(s)).ToList();
        // Sem terreno em algum ponto: interpola entre os vizinhos conhecidos (ou 0).
        var known = raw.Select((z, i) => (z, i)).Where(t => t.z != null).ToList();
        var ground = new double[ss.Count];
        for (int i = 0; i < ss.Count; i++)
        {
            if (raw[i] is { } z) { ground[i] = z; continue; }
            var prev = known.LastOrDefault(k => k.i < i);
            var next = known.FirstOrDefault(k => k.i > i);
            ground[i] = prev.z != null && next.z != null
                ? prev.z.Value + (next.z.Value - prev.z.Value) * (i - prev.i) / (double)(next.i - prev.i)
                : prev.z ?? next.z ?? 0;
        }
        var res = new PerfilResultado();
        for (int i = 0; i < ss.Count; i++) res.Ground.Add(new Vec2(ss[i], ground[i]));
        var z0 = o.StartZ ?? ground[0];
        var z1 = o.EndZ ?? ground[^1];
        var g = Math.Max(0.005, o.MaxGrade);
        var grade = new RoadGrade { DefaultCurve = o.VerticalCurve, Crossfall = o.Crossfall, CutSlope = o.CutSlope, FillSlope = o.FillSlope };
        switch (o.Mode)
        {
            case ModoGreide.Nivelado:
                grade.Points.AddRange(new[] { new GradePoint(0, o.LevelZ), new GradePoint(L, o.LevelZ) });
                break;
            case ModoGreide.RampaConstante:
                grade.Points.AddRange(new[] { new GradePoint(0, z0), new GradePoint(L, z1) });
                if (Math.Abs(z1 - z0) / L > g + 1e-9) res.Warnings.Add("A rampa constante entre as pontas passa da rampa máxima.");
                break;
            case ModoGreide.Manual:
                foreach (var line in (o.ManualPvis ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(new[] { ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().Replace(',', '.')).ToList();
                    if (parts.Count < 2) continue;
                    if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) continue;
                    if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) continue;
                    double? c = parts.Count > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cv) ? cv : null;
                    grade.Points.Add(new GradePoint(Math.Clamp(s, 0, L), z, c));
                }
                if (grade.Points.Count < 2)
                {
                    res.Warnings.Add("PIVs manuais inválidos (use uma linha por PIV: estaca; cota; curva) – usado o terreno suavizado.");
                    goto default;
                }
                grade.Normalize();
                break;
            default:
            {
                // Terreno suavizado (média móvel), com as pontas fixas, deslocado e limitado pela rampa máxima. Perto das pontas a
                // janela usa o terreno refletido na ponta (2·z0 − z): a média de uma rampa constante continua sendo a rampa. Antes a
                // janela cortada puxava a média para dentro e o greide dobrava nos primeiros 10 m (juntas à toa nos pisos).
                var w = Math.Max(1, (int)Math.Round(Math.Max(Step, o.Smoothing) / Step / 2));
                var last = ss.Count - 1;
                double G(int k) => k < 0 ? 2 * ground[0] - ground[Math.Min(last, -k)]
                    : k > last ? 2 * ground[last] - ground[Math.Max(0, 2 * last - k)] : ground[k];
                var sm = new double[ss.Count];
                for (int i = 0; i < ss.Count; i++)
                {
                    double sum = 0, wsum = 0;
                    for (int k = i - w; k <= i + w; k++)
                    {
                        var wk = 1.0 - Math.Abs(k - i) / (w + 1.0);
                        sum += G(k) * wk; wsum += wk;
                    }
                    sm[i] = sum / wsum + o.Offset;
                }
                sm[0] = z0;
                sm[^1] = z1;
                // Cotas obrigadas: pontas informadas e cruzamentos com as vias existentes (o greide passa exatamente na cota
                // delas, em nível 6 m para cada lado – a interseção fica sem rampa no miolo). Entre elas, o terreno suavizado
                // limitado pela rampa máxima. Antes as cotas eram "puxadas" e depois a rampa máxima as deslocava; o patamar
                // colado de volta deixava saltos (rampas de até 120 %) ao lado dele e nas pontas.
                var (zs, pinned, gEff) = PinnedGrade(ss, sm, g, L, o.StartZ != null ? z0 : null, o.EndZ != null ? z1 : null, o.Fixed, 0, o.FixedHalf, o.FixedSlope);
                if (gEff > g + 1e-6)
                    res.Warnings.Add($"As cotas obrigadas (cruzamentos/pontas) exigem rampa de {gEff.ToString("P0", CultureInfo.GetCultureInfo("pt-BR"))} " +
                                     $"(máx. {g.ToString("P0", CultureInfo.GetCultureInfo("pt-BR"))}) entre elas.");
                SetPivs(grade, zs, pinned);
                Smooth(grade, ss, L, gEff, MaxCurvature(g, o.VerticalCurve), o.StartZ != null ? z0 : null, o.EndZ != null ? z1 : null, o.Fixed, o.FixedHalf, o.FixedSlope);
                break;
            }
        }
        // Curvas verticais pelo parâmetro K: L = K·|Δi| (em %), no mínimo a curva padrão.
        if (o.CurveK > 0 && o.Mode != ModoGreide.Manual)
        {
            var pts = grade.Points.OrderBy(q => q.S).ToList();
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                var g0 = (pts[i].Z - pts[i - 1].Z) / Math.Max(1e-6, pts[i].S - pts[i - 1].S);
                var g1 = (pts[i + 1].Z - pts[i].Z) / Math.Max(1e-6, pts[i + 1].S - pts[i].S);
                var lv = o.CurveK * Math.Abs(g1 - g0) * 100;
                pts[i] = new GradePoint(pts[i].S, pts[i].Z, Math.Max(lv, pts[i].Curve ?? o.VerticalCurve));
            }
            grade.Points.Clear();
            grade.Points.AddRange(pts);
        }
        // Áreas de corte e aterro no eixo (indicador rápido).
        for (int i = 0; i + 1 < ss.Count; i++)
        {
            var d = grade.Z(ss[i]) - ground[i];
            if (d > 0) res.FillM2 += d * (ss[i + 1] - ss[i]); else res.CutM2 -= d * (ss[i + 1] - ss[i]);
        }
        res.Grade.Points.AddRange(grade.Points);
        res.Grade.DefaultCurve = grade.DefaultCurve;
        res.Grade.Crossfall = grade.Crossfall;
        res.Grade.CutSlope = grade.CutSlope;
        res.Grade.FillSlope = grade.FillSlope;
        res.Grade.Touch();
        Segment(res, ss, ground, o);
        return res;
    }

    /// <summary>
    /// PIVs do greide a partir das cotas por estação: simplificação (0,25 m) com as cotas obrigadas mantidas como PIVs – inclusive
    /// o centro de cada patamar (colinear, a normalização o tirava), que limita as curvas das bordas do patamar a metade dele.
    /// </summary>
    private static void SetPivs(RoadGrade grade, List<Vec2> zs, List<Vec2> pinned, double tol = 0.25)
    {
        var pts = DouglasPeucker(zs, tol);
        pts.RemoveAll(q => pinned.Any(pp => Math.Abs(pp.X - q.X) < 0.5 && Math.Abs(pp.X - q.X) > 1e-9));
        foreach (var pp in pinned.Where(pp => !pts.Any(q => Math.Abs(q.X - pp.X) < 1e-9))) pts.Add(pp);
        grade.Points.Clear();
        foreach (var p in pts.OrderBy(q => q.X)) grade.Points.Add(new GradePoint(p.X, p.Y));
        grade.Normalize();
        foreach (var pp in pinned.Where(pp => !grade.Points.Any(q => Math.Abs(q.S - pp.X) < 1e-6))) grade.Points.Add(new GradePoint(pp.X, pp.Y));
        grade.Points.Sort((a, b) => a.S.CompareTo(b.S));
        grade.Touch();
    }

    /// <summary>
    /// Patamares numa via que já tem greide: nos nós dela (estação, cota) o greide passa na cota e fica em nível 6 m para cada lado,
    /// e em volta a rampa fica limitada a <paramref name="maxGrade"/> (ou à rampa que a via já tinha, se maior). Usado quando
    /// outra via é ligada a ela – antes só a via nova ganhava o patamar e a existente chegava inclinada ao nó (a superfície do nó
    /// prolongava a rampa dela por cima da boca das outras vias).
    /// </summary>
    public static RoadGrade WithPlatforms(RoadGrade g, double length, IEnumerable<Vec2> pins, double maxGrade, IReadOnlyList<double>? halves = null,
        IReadOnlyList<double>? slopes = null)
    {
        var L = Math.Max(1, length);
        var n = Math.Max(2, (int)Math.Ceiling(L / Step));
        var ss = Enumerable.Range(0, n + 1).Select(i => L * i / n).ToList();
        var target = ss.Select(x => g.Z(x)).ToArray();
        var current = 0.0;
        for (int i = 1; i < ss.Count; i++) current = Math.Max(current, Math.Abs(target[i] - target[i - 1]) / (ss[i] - ss[i - 1]));
        var gm = Math.Max(maxGrade, current);
        var pinList = pins.ToList();
        var (zs, pinned, gEff) = PinnedGrade(ss, target, gm, L, null, null, pinList, 0, halves, slopes);
        var res = g.Clone();
        res.WithoutWorks = null;
        SetPivs(res, zs, pinned);
        Smooth(res, ss, L, gEff, MaxCurvature(gm, g.DefaultCurve), null, null, pinList, halves, slopes);
        return res;
    }

    /// <summary>
    /// Greide pelas cotas obrigadas: estações (as da amostragem + as das cotas obrigadas), cota de cada uma – o alvo limitado
    /// à faixa alcançável a partir das cotas obrigadas com a rampa máxima e depois a rampa limitada entre vizinhas –, os pontos
    /// obrigados (PIVs) e a rampa usada (maior que a pedida só quando as cotas obrigadas a exigem).
    /// </summary>
    /// <param name="curvature">
    /// Variação máxima da rampa por metro (1/m; 0 = sem limite): as mudanças de rampa ficam espalhadas em curvas verticais –
    /// sem ela o vale/crista do terreno virava uma quina (−30 % a +30 % em 5 m).
    /// </param>
    public static (List<Vec2> Z, List<Vec2> Pins, double Grade) PinnedGrade(List<double> ss, double[] target, double g, double L,
        double? startZ, double? endZ, IEnumerable<Vec2> fixedPts, double curvature = 0, IReadOnlyList<double>? halves = null,
        IReadOnlyList<double>? slopes = null)
    {
        // Patamares: em cada cruzamento cobrindo a área do nó (no mínimo 6 m para cada lado; só para dentro da via nas pontas),
        // na cota obrigada e com a rampa do nó ao longo da via (em nível num cruzamento em ângulo reto); pontas informadas.
        var flats = new List<(double A, double B, double Z, double C, double G)>();
        if (startZ is { } sz) flats.Add((0, 0, sz, 0, 0));
        if (endZ is { } ez) flats.Add((L, L, ez, L, 0));
        var k0 = 0;
        foreach (var f in fixedPts)
        {
            var c = Math.Clamp(f.X, 0, L);
            var hk = halves != null && k0 < halves.Count ? halves[k0] : 0;
            var gk = slopes != null && k0 < slopes.Count ? slopes[k0] : 0;
            // Via principal do nó (PassThrough): passa na cota com a própria rampa, sem patamar.
            var h = hk < 0 ? 0 : Math.Max(PlatformHalf, hk);
            k0++;
            flats.Add((Math.Max(0, c - h), Math.Min(L, c + h), f.Y, c, hk < 0 ? 0 : gk));
        }
        // Estações: as da amostragem + as bordas e o centro de cada patamar (sem amostras coladas nelas: intervalos curtos
        // encurtariam as curvas verticais dos PIVs vizinhos).
        var st = new SortedSet<double>(ss);
        var gap = ss.Count > 1 ? (ss[^1] - ss[0]) / (ss.Count - 1) : 1;
        foreach (var (a, b, _, _, _) in flats)
        {
            if (curvature > 0)
                st.RemoveWhere(x => x > 1e-9 && x < L - 1e-9 && (Math.Abs(x - a) < gap / 2 || Math.Abs(x - b) < gap / 2) && (x < a - 1e-9 || x > b + 1e-9));
            st.Add(a); st.Add(b); st.Add((a + b) / 2);
        }
        var s = st.ToList();
        var n = s.Count;
        double Target(double x)
        {
            if (x <= ss[0]) return target[0];
            for (int i = 1; i < ss.Count; i++)
                if (x <= ss[i]) return target[i - 1] + (target[i] - target[i - 1]) * (x - ss[i - 1]) / Math.Max(1e-9, ss[i] - ss[i - 1]);
            return target[^1];
        }
        var pin = new double?[n];
        foreach (var (a, b, fz, fc, fg) in flats)
            for (int i = 0; i < n; i++)
                if (s[i] >= a - 1e-9 && s[i] <= b + 1e-9) pin[i] = fz + fg * (s[i] - fc);
        // Rampa possível: a pedida, ou a que as cotas obrigadas vizinhas exigem.
        var gEff = g;
        int? last = null;
        for (int i = 0; i < n; i++)
        {
            if (pin[i] == null) continue;
            if (last is { } j && s[i] - s[j] > 1e-6) gEff = Math.Max(gEff, Math.Abs(pin[i]!.Value - pin[j]!.Value) / (s[i] - s[j]) * 1.0001);
            last = i;
        }
        // Faixa alcançável das cotas obrigadas e alvo dentro dela.
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (pin[i] is { } pz) { z[i] = pz; continue; }
            double lo = double.MinValue, hi = double.MaxValue;
            for (int k = 0; k < n; k++)
            {
                if (pin[k] is not { } kz) continue;
                var d = Math.Abs(s[i] - s[k]);
                lo = Math.Max(lo, kz - gEff * d);
                hi = Math.Min(hi, kz + gEff * d);
            }
            z[i] = Math.Clamp(Target(s[i]), Math.Min(lo, hi), Math.Max(lo, hi));
        }
        // Rampa limitada entre vizinhas (as obrigadas ficam).
        for (int iter = 0; iter < 20; iter++)
        {
            var changed = false;
            for (int i = 1; i < n; i++)
            {
                if (pin[i] != null) continue;
                var ds = s[i] - s[i - 1];
                var c = Math.Clamp(z[i], z[i - 1] - gEff * ds, z[i - 1] + gEff * ds);
                if (Math.Abs(c - z[i]) > 1e-9) { z[i] = c; changed = true; }
            }
            for (int i = n - 2; i >= 0; i--)
            {
                if (pin[i] != null) continue;
                var ds = s[i + 1] - s[i];
                var c = Math.Clamp(z[i], z[i + 1] - gEff * ds, z[i + 1] + gEff * ds);
                if (Math.Abs(c - z[i]) > 1e-9) { z[i] = c; changed = true; }
            }
            if (!changed) break;
        }
        if (curvature > 0) LimitCurvature(s, z, pin, gEff, curvature);
        var pins = new List<Vec2>();
        foreach (var (a, b, zz, fc, fg) in flats)
        {
            pins.Add(new Vec2(a, zz + fg * (a - fc)));
            if (b - a > 1e-6) { pins.Add(new Vec2((a + b) / 2, zz + fg * ((a + b) / 2 - fc))); pins.Add(new Vec2(b, zz + fg * (b - fc))); }
        }
        return (s.Select((x, i) => new Vec2(x, z[i])).ToList(), pins.DistinctBy(p => Math.Round(p.X, 6)).ToList(), gEff);
    }

    /// <summary>
    /// Curvas verticais nas cotas por estação: a variação de rampa em cada estação fica abaixo de
    /// <paramref name="curvature"/> × o menor intervalo vizinho (com folga de 2 %, a curva que o greide põe no PIV cabe entre os
    /// vizinhos), sem passar da rampa máxima. Projeções sucessivas sobre cada restrição (só as estações livres se movem, o
    /// mínimo que a corrige) até todas valerem.
    /// </summary>
    private static void LimitCurvature(List<double> s, double[] z, double?[] pin, double g, double curvature)
    {
        var n = s.Count;
        for (int iter = 0; iter < 40000; iter++)
        {
            var worst = 0.0;
            // Varreduras alternadas (ida e volta): a ordem das projeções não empurra o greide para um lado.
            var back = iter % 2 == 1;
            // Rampa entre vizinhas: as duas estações (livres) se aproximam igualmente.
            for (int ii = 1; ii < n; ii++)
            {
                var i = back ? n - ii : ii;
                var h = s[i] - s[i - 1];
                if (h < 1e-9) continue;
                var d = (z[i] - z[i - 1]) / h;
                var e = Math.Abs(d) - g;
                if (e <= 1e-9) continue;
                double ga = pin[i - 1] == null ? -1 / h : 0, gb = pin[i] == null ? 1 / h : 0;
                var nn = ga * ga + gb * gb;
                if (nn < 1e-12) continue;
                worst = Math.Max(worst, e);
                var k = -Math.Sign(d) * e / nn;
                z[i - 1] += k * ga;
                z[i] += k * gb;
            }
            // Variação da rampa em cada estação.
            for (int ii = 1; ii + 1 < n; ii++)
            {
                var i = back ? n - 1 - ii : ii;
                var h1 = s[i] - s[i - 1];
                var h2 = s[i + 1] - s[i];
                if (h1 < 1e-9 || h2 < 1e-9) continue;
                var d = (z[i + 1] - z[i]) / h2 - (z[i] - z[i - 1]) / h1;
                var e = Math.Abs(d) - curvature * 0.98 * Math.Min(h1, h2);
                if (e <= 1e-9) continue;
                double ga = pin[i - 1] == null ? 1 / h1 : 0, gb = pin[i] == null ? -(1 / h1 + 1 / h2) : 0, gc = pin[i + 1] == null ? 1 / h2 : 0;
                var nn = ga * ga + gb * gb + gc * gc;
                if (nn < 1e-12) continue;
                worst = Math.Max(worst, e);
                var k = -Math.Sign(d) * e / nn;
                z[i - 1] += k * ga;
                z[i] += k * gb;
                z[i + 1] += k * gc;
            }
            if (worst < 1e-7) break;
        }
    }

    /// <summary>
    /// Curvas verticais cortadas: quando os PIVs ficam tão perto que as curvas não cabem entre eles, a rampa muda numa quina
    /// (−30 % a +30 % em 5 m no fundo de um vale). Nesses casos o greide é refeito pelas estações com a variação de rampa
    /// limitada (<see cref="LimitCurvature"/>) e PIVs a cada estação na parte curva; greides que já são suaves ficam como estão.
    /// </summary>
    private static void Smooth(RoadGrade grade, List<double> ss, double L, double g, double curvature, double? startZ, double? endZ, IEnumerable<Vec2> fixedPts,
        IReadOnlyList<double>? halves = null, IReadOnlyList<double>? slopes = null)
    {
        if (curvature <= 0 || CurvatureOf(grade, L) <= curvature) return;
        var target = ss.Select(x => grade.Z(x)).ToArray();
        var (zs, pinned, _) = PinnedGrade(ss, target, g, L, startZ, endZ, fixedPts, curvature, halves, slopes);
        // Um PIV por estação na parte curva (só os colineares saem): juntar estações alongava os trechos e as curvas dos
        // PIVs vizinhos deixavam de caber (até 10 % acima do limite em curvas longas).
        SetPivs(grade, zs, pinned, 1e-4);
    }

    /// <summary>Meia extensão mínima do patamar em nível de um cruzamento (m).</summary>
    public const double PlatformHalf = 6;

    /// <summary>
    /// Meia extensão de uma cota obrigada sem patamar (<see cref="PerfilOpcoes.FixedHalf"/>): a via PRINCIPAL do nó passa na cota
    /// com a própria rampa; só as secundárias ficam em nível dentro da interseção. Antes as duas vias ganhavam patamar de ~60 m
    /// mesmo em rampa constante (cortes e aterros à toa e mais curvas verticais).
    /// </summary>
    public const double PassThrough = -1;

    /// <summary>
    /// Via principal de um nó (índice em <paramref name="roads"/>, na ordem das vias do nó): a que atravessa o cruzamento com o
    /// próprio greide – a mesma regra da superfície do nó (<see cref="NodeSurface.MajorIndex"/>: maior hierarquia, empate = pista
    /// mais larga, depois a primeira). −1 = nenhuma via.
    /// </summary>
    /// <summary>
    /// Cota (relativa à base da principal) em que a via secundária fica em nível no nó: a da boca da interseção – a superfície
    /// do nó além da pista da principal fica na cota da borda dela (eixo menos o abaulamento). Com superelevação, a do eixo
    /// (as duas bocas ficam em cotas diferentes; a concordância vence a diferença). Antes era a cota do eixo da principal: a
    /// secundária ficava 2 % × meia pista acima da boca e a concordância empenava a pista para descer até ela.
    /// </summary>
    public static double MouthZ(RoadGrade major, double station, RoadPavementDefinition majorDef)
        => MouthZCore(major, station, majorDef);

    /// <summary>
    /// Rampa do patamar da via secundária no nó: a da superfície do nó ao longo do eixo dela – a rampa da principal projetada
    /// na direção da secundária (zero num cruzamento em ângulo reto). Num Y com a principal em rampa, o ramo oblíquo em nível
    /// ficava metros acima ou abaixo da boca do nó e a concordância vencia a diferença com rampas acima da máxima.
    /// </summary>
    public static double NodeSlope(RoadGrade major, double majorStation, double majorLength, Vec2 majorTangent, Vec2 secondaryTangent)
    {
        // Na ponta da principal (ramo de um Y, T pela ponta) a rampa é a do fim dela – o greide não segue além da ponta.
        double a = Math.Max(0, majorStation - 0.5), b = Math.Min(majorLength, majorStation + 0.5);
        var g = b - a > 1e-6 ? (major.Z(b) - major.Z(a)) / (b - a) : 0;
        return g * majorTangent.Dot(secondaryTangent);
    }

    private static double MouthZCore(RoadGrade major, double station, RoadPavementDefinition majorDef)
    {
        var z = major.Z(station);
        if (Math.Abs(major.E(station)) > 1e-9) return z;
        return z - major.Crossfall * Math.Max(1, Math.Max(majorDef.LeftWidth, majorDef.RightWidth));
    }

    public static int MajorOf(IReadOnlyList<RoadPavementDefinition> roads) =>
        NodeSurface.MajorIndex(roads.Select(p => (p.Hierarchy is { } h && h != HierarquiaViaria.NaoDefinida ? (int)h : 10,
            Math.Max(1, Math.Max(p.LeftWidth, p.RightWidth)))).ToList());

    /// <summary>
    /// Alcance da área de um nó ao longo do eixo: maior distância, em estação, entre o nó e os pontos dos recortes que ele fez
    /// na via (zonas que contêm o nó ou passam a até 2 m dele). 0 = sem recorte. É a extensão em que a via segue a superfície do
    /// nó – o patamar do greide tem de cobri-la, senão a concordância vence o desnível em poucos metros (rampas de até 55 % num Y
    /// em terreno de 25 %).
    /// </summary>
    public static double NodeReach(Polyline2 axis, Vec2 node, IEnumerable<IReadOnlyList<Vec2>> zones)
    {
        var c = axis.Project(node).Station;
        var best = 0.0;
        foreach (var z in zones)
        {
            if (z.Count < 3) continue;
            var poly = new Polygon2(z);
            if (!poly.Contains(node) && poly.DistanceTo(node) > 2) continue;
            foreach (var p in z) best = Math.Max(best, Math.Abs(axis.Project(p).Station - c));
        }
        return best;
    }

    /// <summary>
    /// Meia extensão do patamar de um cruzamento: o alcance do nó (<see cref="NodeReach"/>) mais a parte da curva vertical que
    /// entra no patamar (até 1/4 da curva padrão, com a rampa de chegada no máximo) – a curva vertical fica fora da área do nó,
    /// que segue em nível. 0 = sem recorte (patamar mínimo).
    /// </summary>
    public static double PlatformFor(double reach, double curve) => reach > 1e-6 ? reach + Math.Max(0, curve) / 4 : 0;

    /// <summary>Maior variação de rampa por metro do greide (amostras a cada 0,5 m, diferença segunda em 1 m).</summary>
    public static double CurvatureOf(RoadGrade g, double length)
    {
        var worst = 0.0;
        for (var s = 1.0; s <= length - 1; s += 0.5) worst = Math.Max(worst, Math.Abs(g.Z(s + 1) - 2 * g.Z(s) + g.Z(s - 1)));
        return worst;
    }

    /// <summary>Curvatura máxima do greide (1/m) para a curva vertical e a rampa máxima: uma curva inteira vai de −rampa a +rampa.</summary>
    public static double MaxCurvature(double maxGrade, double curve) => curve > 1e-6 ? 2 * Math.Max(0.005, maxGrade) / curve : 0;

    public static List<Vec2> DouglasPeucker(List<Vec2> pts, double tol)
    {
        if (pts.Count < 3) return pts;
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double best = 0; var idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                var t = (pts[i].X - pts[a].X) / Math.Max(1e-9, pts[b].X - pts[a].X);
                var zl = pts[a].Y + (pts[b].Y - pts[a].Y) * t;
                var d = Math.Abs(pts[i].Y - zl);
                if (d > best) { best = d; idx = i; }
            }
            if (idx > 0 && best > tol) { keep[idx] = true; stack.Push((a, idx)); stack.Push((idx, b)); }
        }
        return pts.Where((_, i) => keep[i]).ToList();
    }

    /// <summary>Trechos da via: obra onde o aterro/corte passa dos limites (unidos e com comprimento mínimo).</summary>
    private static void Segment(PerfilResultado res, List<double> ss, double[] ground, PerfilOpcoes o)
    {
        TipoTrecho Kind(int i)
        {
            var d = res.Grade.Z(ss[i]) - ground[i];
            if (!o.AutoStructures) return d > 0.3 ? TipoTrecho.Aterro : d < -0.3 ? TipoTrecho.Corte : TipoTrecho.Plataforma;
            if (d > o.BridgeFill) return TipoTrecho.Viaduto;
            if (-d > o.TunnelCut) return TipoTrecho.Tunel;
            if (o.TrenchCut > 0.5 && -d > o.TrenchCut) return TipoTrecho.Trincheira;
            return d > 0.3 ? TipoTrecho.Aterro : d < -0.3 ? TipoTrecho.Corte : TipoTrecho.Plataforma;
        }
        var runs = new List<(TipoTrecho K, int A, int B)>();
        for (int i = 0; i < ss.Count; i++)
        {
            var k = Kind(i);
            if (runs.Count > 0 && runs[^1].K == k) runs[^1] = (k, runs[^1].A, i);
            else runs.Add((k, i, i));
        }
        bool IsWork(TipoTrecho k) => k is TipoTrecho.Viaduto or TipoTrecho.Tunel or TipoTrecho.Trincheira;
        // Une obras do mesmo tipo separadas por pouco.
        for (int r = 1; r + 1 < runs.Count; r++)
        {
            var (k, a, b) = runs[r];
            if (IsWork(k)) continue;
            if (IsWork(runs[r - 1].K) && runs[r - 1].K == runs[r + 1].K && ss[b] - ss[a] < o.MergeGap)
            {
                runs[r - 1] = (runs[r - 1].K, runs[r - 1].A, runs[r + 1].B);
                runs.RemoveRange(r, 2);
                r--;
            }
        }
        foreach (var (k, a, b) in runs)
        {
            var s0 = ss[a];
            var s1 = ss[Math.Min(ss.Count - 1, b + 1)];
            var h = Enumerable.Range(a, b - a + 1).Select(i => Math.Abs(res.Grade.Z(ss[i]) - ground[i])).DefaultIfEmpty(0).Max();
            var kind = IsWork(k) && s1 - s0 < o.MinStructure ? (k == TipoTrecho.Viaduto ? TipoTrecho.Aterro : TipoTrecho.Corte) : k;
            if (res.Segments.Count > 0 && res.Segments[^1].Kind == kind)
                res.Segments[^1] = res.Segments[^1] with { S1 = s1, MaxHeight = Math.Max(res.Segments[^1].MaxHeight, h) };
            else res.Segments.Add(new TrechoVia(kind, s0, s1, h));
        }
    }

    /// <summary>
    /// Gráfico do perfil longitudinal (prévia): terreno, greide, trechos de obra e PIVs, em planta com exagero vertical.
    /// </summary>
    public static MarkingGeometry Chart(PerfilResultado r, double exaggeration = 0)
    {
        var geo = new MarkingGeometry();
        if (r.Ground.Count < 2) return geo;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var L = r.Ground[^1].X;
        var zs = r.Ground.Select(p => p.Y).Concat(r.Ground.Select(p => r.Grade.Z(p.X))).ToList();
        var zmin = Math.Floor(zs.Min() - 3);
        var zmax = Math.Ceiling(zs.Max() + 3);
        var k = exaggeration > 0 ? exaggeration : Math.Clamp(L * 0.35 / Math.Max(1, zmax - zmin), 1, 20);
        Vec2 P(double s, double z) => new(s, (z - zmin) * k);
        var H = (zmax - zmin) * k;
        var th = Math.Max(0.2, H * 0.006);
        // Textos proporcionais ao gráfico (a prévia usa escala 1:100 → mm de papel = 10 × altura em metros).
        double Mm(double modelHeight) => modelHeight * 10;
        var tx = Math.Max(0.8, L * 0.013);
        MarkingColor? ColorOf(TipoTrecho t) => t switch
        {
            TipoTrecho.Viaduto => MarkingColor.Azul,
            TipoTrecho.Tunel => MarkingColor.Preta,
            TipoTrecho.Trincheira => MarkingColor.Laranja,
            _ => null,
        };
        bool InStructure(double s) => r.Segments.Any(x => ColorOf(x.Kind) != null && s >= x.S0 && s <= x.S1);

        // Grade de referência: cotas (horizontais) e estacas (verticais), com rótulos.
        double Nice(double span, int n)
        {
            var raw = span / n;
            var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            foreach (var f in new[] { 1.0, 2.0, 5.0, 10.0 }) if (raw <= f * mag) return f * mag;
            return 10 * mag;
        }
        var dz = Nice(zmax - zmin, 6);
        for (var z = Math.Ceiling(zmin / dz) * dz; z <= zmax + 1e-9; z += dz)
        {
            geo.Annotations.Add(new AnnotationLine(new[] { P(0, z), P(L, z) }, MarkingColor.Concreto));
            geo.Annotations.Add(new AnnotationText(P(0, z) + new Vec2(-tx * 0.6, -tx * 0.35), z.ToString("0.#", pt) + " m", Mm(tx * 0.8), TextAlign.Right));
        }
        var ds = Nice(L, 10);
        for (var st = 0.0; st <= L + 1e-9; st += ds)
        {
            geo.Annotations.Add(new AnnotationLine(new[] { P(st, zmin), P(st, zmax) }, MarkingColor.Concreto));
            geo.Annotations.Add(new AnnotationText(P(st, zmin) - new Vec2(0, tx * 1.4), $"{(int)(st / 1000)}+{st % 1000:000}", Mm(tx * 0.8)));
        }
        geo.Annotations.Add(new AnnotationText(new Vec2(0, -tx * 3.2), "Estaca (km+m) – distância ao longo do eixo da via", Mm(tx * 0.85), TextAlign.Left));

        // Terreno natural (massa marrom até a base do gráfico).
        var ground = new List<Vec2> { P(0, zmin) };
        ground.AddRange(r.Ground.Select(p => P(p.X, p.Y)));
        ground.Add(P(L, zmin));
        geo.Add(new Polygon2(ground), MarkingColor.Terra);
        // Corte (terreno acima do greide, vermelho) e aterro (greide acima do terreno, verde) – fora das obras.
        var cutQuads = new List<Polygon2>();
        var fillQuads = new List<Polygon2>();
        for (int i = 0; i + 1 < r.Ground.Count; i++)
        {
            var (s0, g0) = (r.Ground[i].X, r.Ground[i].Y);
            var (s1, g1) = (r.Ground[i + 1].X, r.Ground[i + 1].Y);
            if (InStructure((s0 + s1) / 2)) continue;
            double z0 = r.Grade.Z(s0), z1 = r.Grade.Z(s1);
            if (Math.Abs(g0 - z0) < 0.05 && Math.Abs(g1 - z1) < 0.05) continue;
            var quad = new Polygon2(new[] { P(s0, g0), P(s1, g1), P(s1, z1), P(s0, z0) });
            if (Math.Abs(quad.Area) < 1e-6) continue;
            ((g0 + g1) / 2 > (z0 + z1) / 2 ? cutQuads : fillQuads).Add(quad);
        }
        foreach (var (quads, col) in new[] { (cutQuads, MarkingColor.Vermelha), (fillQuads, MarkingColor.Verde) })
        {
            List<Polygon2> merged;
            try { merged = PolygonOps.Union(quads); } catch { merged = quads; }
            foreach (var q in merged.Where(x => x.Area > 1e-6)) geo.Pieces.Add(new MarkingPiece(q, col) { Elevation = 0.02 });
        }
        // Greide: faixa escura; nos trechos de obra, na cor da obra e mais grossa.
        Polygon2 Band(double a, double b, double half)
        {
            var top = new List<Vec2>();
            var bot = new List<Vec2>();
            var step = Math.Max(0.5, (b - a) / 200);
            for (var s = a; s <= b + 1e-6; s += step)
            {
                top.Add(P(s, r.Grade.Z(s)) + new Vec2(0, half));
                bot.Add(P(s, r.Grade.Z(s)) - new Vec2(0, half));
            }
            bot.Reverse();
            return new Polygon2(top.Concat(bot));
        }
        geo.Add(Band(0, L, th), MarkingColor.Asfalto);
        var nSeg = 0;
        foreach (var seg in r.Segments)
        {
            if (ColorOf(seg.Kind) is not { } c) continue;
            geo.Add(Band(seg.S0, seg.S1, th * 2.5), c);
            // Barra no alto do gráfico com o nome e a extensão da obra (rótulos alternados em duas alturas).
            geo.Add(Polygon2.Rectangle(new Vec2(seg.S0, H + H * 0.04), new Vec2(seg.S1, H + H * 0.09)), c);
            var yl = H + H * 0.09 + tx * (nSeg++ % 2 == 0 ? 1.4 : 2.8);
            geo.Annotations.Add(new AnnotationText(new Vec2((seg.S0 + seg.S1) / 2, yl), $"{PerfilResultado.Nome(seg.Kind)} {seg.Length.ToString("0", pt)} m", Mm(tx * 0.8)));
        }
        // PIVs com a cota, e a rampa de cada tangente.
        var m = Math.Max(0.5, H * 0.012);
        var pts = r.Grade.Points.OrderBy(q => q.S).ToList();
        var lastLabel = double.MinValue;
        foreach (var q in pts)
        {
            geo.Add(Polygon2.Rectangle(P(q.S, q.Z) - new Vec2(m, m), P(q.S, q.Z) + new Vec2(m, m)), MarkingColor.Branca);
            if (q.S - lastLabel < L * 0.05) continue;
            lastLabel = q.S;
            geo.Annotations.Add(new AnnotationText(P(q.S, q.Z) + new Vec2(0, tx * 1.2), q.Z.ToString("0.00", pt), Mm(tx * 0.7)));
        }
        for (int i = 0; i + 1 < pts.Count && pts.Count <= 20; i++)
        {
            var len = pts[i + 1].S - pts[i].S;
            if (len < L * 0.06) continue;
            var g = (pts[i + 1].Z - pts[i].Z) / len;
            var mid = (pts[i].S + pts[i + 1].S) / 2;
            geo.Annotations.Add(new AnnotationText(P(mid, r.Grade.Z(mid)) + new Vec2(0, tx * 2.2), $"i = {(g * 100).ToString("0.0", pt)} %", Mm(tx * 0.75)));
        }
        // Legenda.
        var lx = L * 0.01;
        var ly = H + H * 0.09 + tx * 5;
        foreach (var (c, t) in new[] { (MarkingColor.Terra, "Terreno natural"), (MarkingColor.Asfalto, "Greide (eixo da via)"), (MarkingColor.Vermelha, "Corte"),
                     (MarkingColor.Verde, "Aterro"), (MarkingColor.Azul, "Viaduto/ponte"), (MarkingColor.Preta, "Túnel"), (MarkingColor.Laranja, "Trincheira") })
        {
            geo.Add(Polygon2.Rectangle(new Vec2(lx, ly), new Vec2(lx + tx * 1.6, ly + tx)), c);
            geo.Annotations.Add(new AnnotationText(new Vec2(lx + tx * 2.1, ly + tx * 0.1), t, Mm(tx * 0.85), TextAlign.Left));
            lx += tx * (3.0 + t.Length * 0.8);
        }
        geo.Annotations.Add(new AnnotationText(new Vec2(L, -tx * 3.2), $"Escala vertical exagerada {k.ToString("0.#", pt)}×", Mm(tx * 0.8), TextAlign.Right));
        // Greide, obras e PIVs sempre por cima do terreno (inclusive dentro dos túneis).
        for (int i = 0; i < geo.Pieces.Count; i++)
            if (geo.Pieces[i].Color is not (MarkingColor.Terra or MarkingColor.Vermelha or MarkingColor.Verde)) geo.Pieces[i] = geo.Pieces[i] with { Elevation = 0.05 };
        return geo;
    }
}
