using System.Globalization;
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
    /// Pontos obrigados (estaca, cota relativa): cruzamentos e entroncamentos com vias existentes – o greide passa exatamente
    /// na cota da outra via ali, concordando suavemente numa janela de <see cref="FixedBlend"/> m.
    /// </summary>
    public List<Vec2> Fixed { get; set; } = new();
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
        Smoothing = r == RelevoVia.AcompanharTerreno ? 20 : 150,
        VerticalCurve = r == RelevoVia.AcompanharTerreno ? 20 : 80,
        MaxGrade = r == RelevoVia.AcompanharTerreno ? 0.30 : maxGrade,
        CutSlope = cut,
        FillSlope = fill,
        AutoStructures = false,
    };

    public static PerfilResultado Design(double length, Func<double, double?> groundAt, PerfilOpcoes o)
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
                // Terreno suavizado (média móvel), com as pontas fixas, deslocado e limitado pela rampa máxima.
                var w = Math.Max(1, (int)Math.Round(Math.Max(Step, o.Smoothing) / Step / 2));
                var sm = new double[ss.Count];
                for (int i = 0; i < ss.Count; i++)
                {
                    double sum = 0, wsum = 0;
                    for (int k = Math.Max(0, i - w); k <= Math.Min(ss.Count - 1, i + w); k++)
                    {
                        var wk = 1.0 - Math.Abs(k - i) / (w + 1.0);
                        sum += ground[k] * wk; wsum += wk;
                    }
                    sm[i] = sum / wsum + o.Offset;
                }
                sm[0] = z0;
                sm[^1] = z1;
                // Cruzamentos com vias existentes: o greide é puxado para a cota delas (concordância em cosseno).
                foreach (var f in o.Fixed)
                    for (int i = 0; i < ss.Count; i++)
                    {
                        var d = Math.Abs(ss[i] - f.X);
                        if (d >= o.FixedBlend) continue;
                        var wf = 0.5 + 0.5 * Math.Cos(Math.PI * d / o.FixedBlend);
                        sm[i] = sm[i] * (1 - wf) + f.Y * wf;
                    }
                var z = LimitGrade(ss, sm, g);
                var pts = DouglasPeucker(ss.Select((s, i) => new Vec2(s, z[i])).ToList(), 0.25);
                foreach (var p in pts) grade.Points.Add(new GradePoint(p.X, p.Y));
                // Plataforma de 12 m em nível no cruzamento (a cota é exata ali e a interseção fica sem rampa no miolo).
                foreach (var f in o.Fixed.Where(f => f.X > 0.5 && f.X < L - 0.5))
                {
                    grade.Points.RemoveAll(q => Math.Abs(q.S - f.X) < 8);
                    if (f.X - 6 > 0.5) grade.Points.Add(new GradePoint(f.X - 6, f.Y));
                    grade.Points.Add(new GradePoint(f.X, f.Y));
                    if (f.X + 6 < L - 0.5) grade.Points.Add(new GradePoint(f.X + 6, f.Y));
                }
                grade.Normalize();
                break;
            }
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

    /// <summary>Greide mais próximo da referência com rampa ≤ g, pontas fixas (duas varreduras de envoltória).</summary>
    private static double[] LimitGrade(List<double> ss, double[] target, double g)
    {
        var n = ss.Count;
        var up = new double[n];
        var dn = new double[n];
        // Envoltória superior e inferior alcançáveis a partir das pontas.
        var z = (double[])target.Clone();
        for (int iter = 0; iter < 3; iter++)
        {
            for (int i = 1; i < n; i++)
            {
                var ds = ss[i] - ss[i - 1];
                z[i] = Math.Clamp(z[i], z[i - 1] - g * ds, z[i - 1] + g * ds);
            }
            z[n - 1] = target[n - 1];
            for (int i = n - 2; i >= 0; i--)
            {
                var ds = ss[i + 1] - ss[i];
                z[i] = Math.Clamp(z[i], z[i + 1] - g * ds, z[i + 1] + g * ds);
            }
            z[0] = target[0];
        }
        _ = up; _ = dn;
        return z;
    }

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
