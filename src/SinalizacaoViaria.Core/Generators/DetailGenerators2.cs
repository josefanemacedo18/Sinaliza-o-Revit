using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Cotas, detalhes típicos, quadros, notas e norte.</summary>
public static partial class DetailGenerator
{
    // ------------------------------------------------------------------ primitivas de cota

    /// <summary>Largura aproximada de um texto (m de modelo) – para dimensionar colunas e evitar sobreposições.</summary>
    public static double TextWidth(string text, double textMm, BuildContext ctx) =>
        text.Split('\n').Max(LineWidthFactor) * ctx.Mm(textMm);

    /// <summary>Largura de uma linha em múltiplos da altura do texto (Arial: maiúsculas mais largas que minúsculas).</summary>
    public static double LineWidthFactor(string line) => line.Sum(ch => ch switch
    {
        ' ' or '.' or ',' or ':' or ';' or '\'' or '|' or '!' => 0.35,
        'M' or 'W' or 'm' or 'w' or 'Ø' or '%' => 1.05,
        'I' or 'i' or 'l' or 'j' or 't' or 'f' or 'r' or '1' => 0.45,
        '–' or '—' => 0.9,
        _ when char.IsUpper(ch) => 0.86,
        _ when char.IsDigit(ch) => 0.72,
        _ => 0.68,
    });

    /// <summary>Ângulo de texto legível (entre −90° e +90°).</summary>
    private static double Readable(Vec2 dir)
    {
        var a = Math.Atan2(dir.Y, dir.X);
        if (a > Math.PI / 2 + 1e-9) a -= Math.PI;
        if (a < -Math.PI / 2 - 1e-9) a += Math.PI;
        return a;
    }

    /// <summary>
    /// Cota alinhada entre <paramref name="a"/> e <paramref name="b"/>, com linha de cota deslocada <paramref name="offset"/> (m)
    /// para o lado <paramref name="side"/>, linhas de chamada, tiques a 45° e texto centralizado.
    /// </summary>
    public static void Dimension(MarkingGeometry geo, BuildContext ctx, Vec2 a, Vec2 b, Vec2 side, double offset, string text, double textMm,
        double textShift = 0, bool extension = true, TerminalCota terminal = TerminalCota.Traco)
    {
        var len = a.DistanceTo(b);
        if (len < 1e-4) return;
        var u = (b - a) / len;
        var n = side.Normalized();
        var gap = ctx.Mm(1.0);
        var over = ctx.Mm(1.5);
        var pa = a + n * offset;
        var pb = b + n * offset;
        if (extension && Math.Abs(offset) > gap)
        {
            geo.Annotations.Add(new AnnotationLine(new[] { a + n * gap * Math.Sign(offset), pa + n * over * Math.Sign(offset) }, MarkingColor.Preta));
            geo.Annotations.Add(new AnnotationLine(new[] { b + n * gap * Math.Sign(offset), pb + n * over * Math.Sign(offset) }, MarkingColor.Preta));
        }
        switch (terminal)
        {
            case TerminalCota.Seta:
            {
                // Setas cheias para dentro (ou para fora, com a linha prolongada, quando o trecho é curto).
                var al = ctx.Mm(2.0);
                var aw = ctx.Mm(0.55);
                var inside = len > 2.6 * al;
                var d = inside ? u : -u;
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { pa, pa + d * al + n * aw, pa + d * al - n * aw }), MarkingColor.Preta));
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { pb, pb - d * al + n * aw, pb - d * al - n * aw }), MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationLine(inside ? new[] { pa, pb } : new[] { pa - u * al * 1.6, pb + u * al * 1.6 }, MarkingColor.Preta));
                break;
            }
            case TerminalCota.Ponto:
            {
                var r = ctx.Mm(0.45);
                geo.Pieces.Add(new MarkingPiece(new Polygon2(CurveTools.Circle(pa, r, r * 0.05)), MarkingColor.Preta));
                geo.Pieces.Add(new MarkingPiece(new Polygon2(CurveTools.Circle(pb, r, r * 0.05)), MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationLine(new[] { pa - u * over, pb + u * over }, MarkingColor.Preta));
                break;
            }
            default:
            {
                geo.Annotations.Add(new AnnotationLine(new[] { pa - u * over, pb + u * over }, MarkingColor.Preta));
                var tick = (u + n).Normalized() * ctx.Mm(1.2);
                geo.Annotations.Add(new AnnotationLine(new[] { pa - tick, pa + tick }, MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationLine(new[] { pb - tick, pb + tick }, MarkingColor.Preta));
                break;
            }
        }

        var rot = Readable(u);
        var up = Vec2.FromAngle(rot + Math.PI / 2);
        if (up.Dot(n) < 0) up = -up;
        var mid = (pa + pb) / 2 + up * (ctx.Mm(0.6 + textShift) + ctx.Mm(textMm));
        // Posição do TextNote = topo do texto no sistema girado.
        geo.Annotations.Add(new AnnotationText(mid, text, textMm) { Rotation = rot });
    }

    // ------------------------------------------------------------------ cota de seção

    /// <summary>Intervalos (t ∈ [0,1]) do segmento a→b dentro do polígono.</summary>
    public static List<(double T0, double T1)> SegmentIntervals(Polygon2 poly, Vec2 a, Vec2 b)
    {
        var ts = new List<double>();
        void Ring(IReadOnlyList<Vec2> r)
        {
            for (int i = 0; i < r.Count; i++)
            {
                var p = r[i];
                var q = r[(i + 1) % r.Count];
                var d1 = b - a;
                var d2 = q - p;
                var den = d1.Cross(d2);
                if (Math.Abs(den) < 1e-12) continue;
                var t = (p - a).Cross(d2) / den;
                var s = (p - a).Cross(d1) / den;
                if (t >= -1e-9 && t <= 1 + 1e-9 && s >= -1e-9 && s <= 1 + 1e-9) ts.Add(Math.Clamp(t, 0, 1));
            }
        }
        Ring(poly.Outer);
        foreach (var h in poly.Holes) Ring(h);
        ts.Add(0);
        ts.Add(1);
        ts = ts.OrderBy(t => t).ToList();
        var res = new List<(double, double)>();
        for (int i = 0; i + 1 < ts.Count; i++)
        {
            if (ts[i + 1] - ts[i] < 1e-9) continue;
            var m = a + (b - a) * ((ts[i] + ts[i + 1]) / 2);
            if (poly.Contains(m)) res.Add((ts[i], ts[i + 1]));
        }
        return res;
    }

    private static bool SectionIncludes(MarkingDefinition d, SectionDimensionDefinition sd)
    {
        if (d is IAnnotationDefinition or SignDefinition or UrbanElementDefinition or SymbolMarkingDefinition or TextMarkingDefinition or RepeatedMarkingDefinition
            or RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or TactileRouteDefinition)
            return false;
        var physical = d is DeviceMarkingDefinition or RampDefinition or TrafficCalmingDefinition or CurbExtensionDefinition or SidewalkAreaDefinition
            or PlanterDefinition or CulDeSacDefinition
            || d is LinearMarkingDefinition l && (l.Code is "CALCADA" or "GRAMADO" or "SARJETA" or "SARJETAO" || l.Code.StartsWith("MEIO-FIO"));
        return physical ? sd.Physical : sd.Horizontal;
    }

    /// <summary>
    /// Estações (m ao longo de a→b) das bordas/eixos dos elementos cortados pela seção. Cada elemento é tratado à parte
    /// (calçada e meio-fio encostados têm cada um a sua cota); linhas estreitas são cotadas pelo eixo e as bordas de
    /// pinturas de fundo que ficam sob uma linha são absorvidas pelo eixo dela.
    /// </summary>
    public static List<double> SectionStations(SectionDimensionDefinition sd, IEnumerable<MarkingGeometry> geometries)
    {
        var a = sd.Start;
        var b = sd.End;
        var L = a.DistanceTo(b);
        var edges = new List<double>();
        var axes = new List<(double S, double Half)>();
        foreach (var g in geometries)
        {
            var intervals = new List<(double, double)>();
            foreach (var p in g.Pieces)
            {
                if (MarkingColors.IsPavement(p.Color) || p.Elevation > 0.5) continue;   // pavimento e volumes altos (árvores, placas) não definem cotas
                var (mn, mx) = p.Shape.Bounds;
                if (Math.Max(a.X, b.X) < mn.X || Math.Min(a.X, b.X) > mx.X || Math.Max(a.Y, b.Y) < mn.Y || Math.Min(a.Y, b.Y) > mx.Y) continue;
                intervals.AddRange(SegmentIntervals(p.Shape, a, b).Select(i => (i.T0 * L, i.T1 * L)));
            }
            // Une intervalos sobrepostos/encostados do MESMO elemento (peças da mesma faixa).
            var merged = new List<(double S0, double S1)>();
            foreach (var iv in intervals.OrderBy(i => i.Item1))
            {
                if (merged.Count > 0 && iv.Item1 <= merged[^1].S1 + 0.01) merged[^1] = (merged[^1].S0, Math.Max(merged[^1].S1, iv.Item2));
                else merged.Add(iv);
            }
            // Só linhas PINTADAS são cotadas pelo eixo (meio-fio estreito é elemento físico: bordas).
            var paint = g.Pieces.Count > 0 && g.Pieces.All(p => MarkingColors.IsPaint(p.Color));
            foreach (var (s0, s1) in merged)
            {
                if (paint && sd.LineAxes && s1 - s0 <= sd.AxisMaxWidth) axes.Add(((s0 + s1) / 2, (s1 - s0) / 2));
                else { edges.Add(s0); edges.Add(s1); }
            }
        }
        var st = new List<double>();
        if (sd.IncludeEnds) { st.Add(0); st.Add(L); }
        st.AddRange(axes.Select(x => x.S));
        st.AddRange(edges.Where(e => !axes.Any(x => Math.Abs(e - x.S) <= x.Half + 0.02)));
        st = st.Where(s => s >= -1e-6 && s <= L + 1e-6).OrderBy(s => s).ToList();
        var res = new List<double>();
        foreach (var s in st)
            if (res.Count == 0 || s - res[^1] > 0.02) res.Add(s);
        return res;
    }

    public static MarkingGeometry SectionDimensions(SectionDimensionDefinition sd, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var a = sd.Start;
        var b = sd.End;
        var L = a.DistanceTo(b);
        if (L < 0.2) { geo.Warnings.Add("Linha de seção muito curta."); return geo; }
        var defs = ctx.AllDefinitions?.Invoke() ?? Array.Empty<MarkingDefinition>();
        var withGeo = defs.Select(d => (Def: d, Geo: SectionIncludes(d, sd) || IsPavement(d) ? ctx.GeometryOf?.Invoke(d) : null))
            .Where(x => x.Geo != null).Select(x => (x.Def, Geo: x.Geo!)).ToList();
        var st = SectionStations(sd, withGeo.Where(x => SectionIncludes(x.Def, sd)).Select(x => x.Geo));
        if (st.Count < 2) { geo.Warnings.Add("Nenhum elemento cortado pela linha de seção."); return geo; }

        var u = (b - a) / L;
        var n = u.PerpLeft * Math.Sign(sd.OffsetMm == 0 ? 1 : sd.OffsetMm);
        var off = ctx.Mm(Math.Abs(sd.OffsetMm));
        Vec2 P(double s) => a + u * s;
        var fmt = "0." + new string('0', Math.Clamp(sd.Decimals, 0, 3));
        if (sd.Decimals <= 0) fmt = "0";

        // Eixo(s) de via cruzados pela seção.
        var axes = new List<double>();
        if (sd.AxisMarker || sd.SplitAtAxis)
            foreach (var d in defs.OfType<RoadPavementDefinition>())
                if (ctx.PathOf?.Invoke(d) is { } path)
                    foreach (var t in Crossings(path, a, b)) if (!axes.Any(x => Math.Abs(x - t * L) < 0.05)) axes.Add(t * L);
        if (sd.SplitAtAxis)
        {
            foreach (var s0 in axes) if (!st.Any(x => Math.Abs(x - s0) < 0.02)) st.Add(s0);
            st.Sort();
        }

        var stagger = false;
        var labelMm = sd.TextMm * 0.8;
        for (int i = 0; i + 1 < st.Count; i++)
        {
            var len = st[i + 1] - st[i];
            var text = len.ToString(fmt, Pt);
            var fits = TextWidth(text, sd.TextMm, ctx) < len * 0.9;
            stagger = !fits && !stagger;
            Dimension(geo, ctx, P(st[i]), P(st[i + 1]), n, off, text, sd.TextMm, fits || !stagger ? 0 : sd.TextMm * 1.4, terminal: sd.Terminal);
            if (!sd.Labels) continue;
            // Nome do trecho entre a linha de seção e a linha de cota (só quando cabe).
            var name = SectionLabel(withGeo, P((st[i] + st[i + 1]) / 2), len);
            if (name == null) continue;
            var lines = TextWidth(name, labelMm, ctx) < len * 0.92 ? name
                : name.Contains(' ') && name.Split(' ').Max(w => TextWidth(w, labelMm, ctx)) < len * 0.92 ? WrapToWidth(name, len * 0.92, labelMm, ctx) : null;
            if (lines == null) continue;
            var rot = Readable(u);
            var up = Vec2.FromAngle(rot + Math.PI / 2);
            if (up.Dot(n) < 0) up = -up;
            var nl = lines.Split('\n').Length;
            // Texto logo abaixo da linha de cota (entre ela e a seção).
            var mid = P((st[i] + st[i + 1]) / 2) + n * off - up * ctx.Mm(1.0);
            if (off > ctx.Mm(labelMm * 1.6 * nl + 2)) geo.Annotations.Add(new AnnotationText(mid, lines, labelMm) { Rotation = rot });
        }
        var totalOff = off + ctx.Mm(sd.TextMm * 3.2 + 2);
        if (sd.Total && st.Count > 2)
            Dimension(geo, ctx, P(st[0]), P(st[^1]), n, totalOff, (st[^1] - st[0]).ToString(fmt, Pt), sd.TextMm, terminal: sd.Terminal);
        var outer = sd.Total && st.Count > 2 ? totalOff : off;

        // Linha de seção (traço fino) para referência.
        geo.Annotations.Add(new AnnotationLine(new[] { a, b }, MarkingColor.Vermelha));

        // Eixo: linha traço-ponto atravessando a cadeia de cotas, com a inscrição "EIXO".
        if (sd.AxisMarker)
            foreach (var s0 in axes)
            {
                var from = P(s0) - n * ctx.Mm(4);
                var to = P(s0) + n * (outer + ctx.Mm(3));
                DashDot(geo, from, to, ctx);
                var ang = Readable(n);
                var upA = Vec2.FromAngle(ang + Math.PI / 2);
                geo.Annotations.Add(new AnnotationText(from - n * ctx.Mm(1) + upA * ctx.Mm(sd.TextMm * 0.5), "EIXO", sd.TextMm * 0.85,
                    n.Dot(Vec2.FromAngle(ang)) > 0 ? TextAlign.Right : TextAlign.Left) { Rotation = ang });
            }

        // Marcas de corte nas pontas (letra no círculo + seta do sentido de observação) e título.
        var letter = (sd.SectionLetter ?? "").Trim().ToUpperInvariant();
        if (letter.Length > 0)
        {
            var r = ctx.Mm(Math.Max(3.0, sd.TextMm * 1.6));
            foreach (var (p0, dir) in new[] { (a, -u), (b, u) })
            {
                var c = p0 + dir * (r + ctx.Mm(3));
                var circle = CurveTools.Circle(c, r, r * 0.02);
                circle.Add(circle[0]);
                geo.Annotations.Add(new AnnotationLine(circle, MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationLine(new[] { p0, c - dir * r }, MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationText(c + new Vec2(0, ctx.Mm(sd.TextMm * 1.3) / 2), letter, sd.TextMm * 1.3, TextAlign.Center));
                // Seta cheia indicando o sentido de observação (para o lado oposto às cotas).
                var tip = c - n * (r + ctx.Mm(3.2));
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { tip, c - n * r + dir * ctx.Mm(1.4), c - n * r - dir * ctx.Mm(1.4) }), MarkingColor.Preta));
            }
            var titleRot = Readable(u);
            var tUp = Vec2.FromAngle(titleRot + Math.PI / 2);
            if (tUp.Dot(n) < 0) tUp = -tUp;
            var title = $"SEÇÃO {letter}–{letter}";
            var tMm = sd.TextMm * 1.5;
            // Bloco de 2 linhas além da cota total: o topo do texto fica do lado de fora, o texto cresce em direção à seção.
            var tPos = (a + b) / 2 + tUp * (outer + ctx.Mm(sd.TextMm * 1.6 + 6 + 2 * tMm * 1.45));
            geo.Annotations.Add(new AnnotationText(tPos, title + "\n(cotas em metros)", tMm) { Rotation = titleRot });
        }
        geo.UnitCount = st.Count - 1;
        return geo;
    }

    private static bool IsPavement(MarkingDefinition d) => d is RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition;

    /// <summary>Parâmetros t ∈ [0,1] do segmento a→b onde ele cruza a polilinha.</summary>
    public static List<double> Crossings(Polyline2 path, Vec2 a, Vec2 b)
    {
        var res = new List<double>();
        var d1 = b - a;
        for (int i = 0; i + 1 < path.Points.Count; i++)
        {
            var p = path.Points[i];
            var d2 = path.Points[i + 1] - p;
            var den = d1.Cross(d2);
            if (Math.Abs(den) < 1e-12) continue;
            var t = (p - a).Cross(d2) / den;
            var s = (p - a).Cross(d1) / den;
            if (t >= 0 && t <= 1 && s >= 0 && s <= 1) res.Add(t);
        }
        return res;
    }

    private static void DashDot(MarkingGeometry geo, Vec2 from, Vec2 to, BuildContext ctx)
    {
        var len = from.DistanceTo(to);
        if (len < 1e-6) return;
        var u = (to - from) / len;
        var dash = ctx.Mm(4);
        var gap = ctx.Mm(1);
        var dot = ctx.Mm(0.4);
        for (double s = 0; s < len; s += dash + gap + dot + gap)
        {
            geo.Annotations.Add(new AnnotationLine(new[] { from + u * s, from + u * Math.Min(len, s + dash) }, MarkingColor.Preta));
            var ds = s + dash + gap;
            if (ds < len) geo.Annotations.Add(new AnnotationLine(new[] { from + u * ds, from + u * Math.Min(len, ds + dot) }, MarkingColor.Preta));
        }
    }

    private static string WrapToWidth(string text, double width, double textMm, BuildContext ctx)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var w in text.Split(' '))
        {
            var cand = line.Length == 0 ? w : line + " " + w;
            if (line.Length > 0 && TextWidth(cand, textMm, ctx) > width) { lines.Add(line); line = w; }
            else line = cand;
        }
        if (line.Length > 0) lines.Add(line);
        return string.Join("\n", lines);
    }

    /// <summary>Nome do elemento cortado no ponto <paramref name="m"/> da seção (null = sem nome).</summary>
    public static string? SectionLabel(IReadOnlyList<(MarkingDefinition Def, MarkingGeometry Geo)> items, Vec2 m, double width)
    {
        string? best = null;
        var rank = int.MaxValue;
        foreach (var (def, g) in items)
        {
            if (!g.Pieces.Any(p => p.Elevation <= 0.5 && p.Shape.Contains(m))) continue;
            var (name, r) = def switch
            {
                LinearMarkingDefinition l => l.Code.ToUpperInvariant() switch
                {
                    "CALCADA" => ("Calçada", 1),
                    "GRAMADO" => ("Faixa gramada", 1),
                    "SARJETA" => ("Sarjeta", 1),
                    "SARJETAO" => ("Sarjetão", 1),
                    var c when c.StartsWith("MEIO-FIO") => ("Meio-fio", 1),
                    "CIC-FD" => ("Ciclofaixa", 2),
                    "FCA" => ("Faixa de caminhada", 2),
                    "ONI-FD" => ("Faixa de ônibus", 2),
                    _ => ((string)null!, int.MaxValue),
                },
                SidewalkAreaDefinition or CurbExtensionDefinition => ("Calçada", 1),
                PlanterDefinition => ("Canteiro", 1),
                DeviceMarkingDefinition => ("Segregador", 3),
                HatchMarkingDefinition h => (h.Code.StartsWith("ZPA", StringComparison.OrdinalIgnoreCase) ? "Zebrado" : "Canalização", 3),
                ParkingMarkingDefinition => ("Estacionamento", 3),
                RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition => (width >= 2.2 ? "Faixa de rolamento" : width >= 0.8 ? "Pista" : null!, 9),
                _ => ((string)null!, int.MaxValue),
            };
            if (name != null && r < rank) { best = name; rank = r; }
        }
        return best;
    }

    /// <summary>Via de exemplo (pontos, definições e geometrias) para a pré-visualização das cotas de seção.</summary>
    public static (List<MarkingDefinition> Defs, Func<MarkingDefinition, MarkingGeometry?> Geometry, Func<MarkingDefinition, Polyline2?> Path) SectionSample()
    {
        var defs = new List<MarkingDefinition>();
        var geos = new Dictionary<MarkingDefinition, MarkingGeometry>();
        void Add(MarkingDefinition d, double y0, double y1, MarkingColor c, double elev = 0)
        {
            defs.Add(d);
            if (!geos.TryGetValue(d, out var g)) geos[d] = g = new MarkingGeometry();
            g.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, y0), new Vec2(30, y1)), c) { Elevation = elev });
        }
        var road = new RoadPavementDefinition();
        defs.Add(road);
        geos[road] = new MarkingGeometry();
        geos[road].Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, -5.3), new Vec2(30, 5.3)), MarkingColor.Asfalto));
        Add(new LinearMarkingDefinition { Code = "CALCADA" }, 5.45, 8.45, MarkingColor.Concreto);
        Add(new LinearMarkingDefinition { Code = "CALCADA" }, -8.45, -5.45, MarkingColor.Concreto);
        Add(new LinearMarkingDefinition { Code = "MEIO-FIO" }, 5.3, 5.45, MarkingColor.Concreto);
        Add(new LinearMarkingDefinition { Code = "MEIO-FIO" }, -5.45, -5.3, MarkingColor.Concreto);
        Add(new LinearMarkingDefinition { Code = "CIC-FD" }, 3.5, 5.0, MarkingColor.Vermelha);
        Add(new LinearMarkingDefinition { Code = "LBO" }, 3.3, 3.5, MarkingColor.Branca);
        Add(new LinearMarkingDefinition { Code = "LFO-2" }, -0.05, 0.05, MarkingColor.Amarela);
        Add(new LinearMarkingDefinition { Code = "LBO" }, -5.2, -5.1, MarkingColor.Branca);
        var paths = new Dictionary<MarkingDefinition, Polyline2> { [road] = new(new[] { new Vec2(0, 0), new Vec2(30, 0) }) };
        return (defs.Distinct().ToList(), d => geos.GetValueOrDefault(d), d => paths.GetValueOrDefault(d));
    }

    // ------------------------------------------------------------------ detalhe típico

    public static MarkingGeometry TypicalDetail(TypicalDetailDefinition td, BuildContext ctx)
    {
        if (ctx.Lookup?.Invoke(td.MarkingTargetId) is not { } target) return Missing(MissingTarget + " (marca excluída).");
        var geo = new MarkingGeometry();
        var mag = ctx.ViewScale / Math.Max(1, td.DetailScale);
        var info = MarkingBuilder.Describe(target, ctx.Catalog);
        var local = new MarkingGeometry();         // desenho em metros reais (origem no canto inferior esquerdo)
        var dims = new List<(Vec2 A, Vec2 B, Vec2 Side, double OffMm, string Text)>();
        var notes = new List<string>();

        switch (target)
        {
            case SignDefinition sg when ctx.Catalog.Placa(sg.Code) is { } p:
                SignElevation(sg, p, ctx, local, dims, notes);
                break;
            case LinearMarkingDefinition l when ctx.Catalog.Linear(l.Code) is { } t && MarkingBuilder.ResolveVariant(t, l.Variant, l.Speed) is { } v:
                LinearDetail(l, t, v, ctx, local, dims, notes);
                break;
            case HatchMarkingDefinition h when ctx.Catalog.Hachura(h.Code) is { } ht:
            {
                var c = (HatchMarkingDefinition)MarkingDefinition.FromJson(h.ToJson())!;
                c.Exclusions.Clear();
                c.StripWidth = null;
                c.AxisPoint = null;
                c.ReferenceDirection = Vec2.UnitX;
                local.Merge(MarkingBuilder.Build(c, new Polyline2(new[] { Vec2.Zero, new Vec2(6, 0), new Vec2(6, 3), new Vec2(0, 3) }, true), Plain(ctx)));
                dims.Add((new Vec2(0, 3), new Vec2(6, 3), Vec2.UnitY, 6, "6,00 (trecho)"));
                var bw = h.BarWidth ?? ht.LarguraBarra;
                var gap = h.Gap ?? ht.Espacamento;
                notes.Add($"Barras de {F(bw)} m, espaço livre de {F(gap)} m (eixo a eixo {F(bw + gap)} m), inclinação de {F(h.AngleDeg ?? ht.Angulo, "0")}°.");
                if (ht.LarguraBorda > 0) notes.Add($"Linha de canalização (contorno) com {F(ht.LarguraBorda)} m.");
                break;
            }
            case ParkingMarkingDefinition pk when ctx.Catalog.Vaga(pk.Code) is { } vt:
            {
                var c = (ParkingMarkingDefinition)MarkingDefinition.FromJson(pk.ToJson())!;
                c.Exclusions.Clear();
                c.Count = 2; c.StartOffset = 0; c.CurbOffset = 0; c.RightSide = true;
                local.Merge(MarkingBuilder.Build(c, new Polyline2(new[] { Vec2.Zero, new Vec2(16, 0) }), Plain(ctx)));
                var w = pk.StallWidth ?? vt.Largura;
                var len = pk.StallLength ?? vt.Comprimento;
                notes.Add($"Vaga de {F(w)} × {F(len)} m a {F(pk.Angle ?? vt.Angulo, "0")}°, linhas de {F(vt.LarguraLinha)} m.");
                if (vt.FaixaAdicional > 0) notes.Add($"Faixa adicional de circulação de {F(vt.FaixaAdicional)} m (zebrada).");
                break;
            }
            default:
            {
                var s = Sample(target, ctx, 10, 0);
                local.Merge(s);
                break;
            }
        }
        if (local.Pieces.Count == 0 && local.Annotations.Count == 0) return Missing("Não há desenho típico para este tipo de marca.");

        // Normaliza: canto inferior esquerdo do desenho real em (0,0).
        var (mn, mx) = Bounds(local.Pieces.Select(p => p.Shape).DefaultIfEmpty(Polygon2.Rectangle(Vec2.Zero, new Vec2(1, 1))));
        foreach (var l in local.Annotations.OfType<AnnotationLine>())
            foreach (var p in l.Points) { mn = new Vec2(Math.Min(mn.X, p.X), Math.Min(mn.Y, p.Y)); mx = new Vec2(Math.Max(mx.X, p.X), Math.Max(mx.Y, p.Y)); }
        if (target is not SignDefinition && dims.Count == 0)
        {
            dims.Add((new Vec2(mn.X, mx.Y), mx, Vec2.UnitY, 6, F(mx.X - mn.X)));
            dims.Add((new Vec2(mx.X, mn.Y), mx, Vec2.UnitX, 6, F(mx.Y - mn.Y)));
        }

        var title = td.Title ?? $"DETALHE TÍPICO – {info.Code} {info.Name.ToUpperInvariant()}";
        var titleH = ctx.Mm(td.TextMm * 1.3);
        var pad = ctx.Mm(8);
        var origin = td.Position + new Vec2(pad, -(titleH + ctx.Mm(6) + pad + (mx.Y - mn.Y) * mag));
        Vec2 W(Vec2 v) => origin + (v - mn) * mag;

        // Fundo de pavimento sob marcas pintadas (marcas brancas sobre papel branco).
        var painted = target is LinearMarkingDefinition or HatchMarkingDefinition or ParkingMarkingDefinition or SymbolMarkingDefinition or TextMarkingDefinition or RepeatedMarkingDefinition;
        var shapes = local.Pieces.Select(p => new MarkingPiece(p.Shape.Transform(W), p.Color)).ToList();
        if (painted)
        {
            var bg = Polygon2.Rectangle(W(mn) - new Vec2(ctx.Mm(3), ctx.Mm(3)), W(mx) + new Vec2(ctx.Mm(3), ctx.Mm(3)));
            foreach (var part in PolygonOps.Difference(new[] { bg }, shapes.Select(s => s.Shape))) geo.Pieces.Add(new MarkingPiece(part, MarkingColor.Asfalto));
        }
        foreach (var g in shapes.GroupBy(s => s.Color))
            foreach (var u in PolygonOps.Union(g.Select(s => s.Shape))) geo.Pieces.Add(new MarkingPiece(u, g.Key));
        foreach (var l in local.Annotations.OfType<AnnotationLine>()) geo.Annotations.Add(l with { Points = l.Points.Select(W).ToList() });
        foreach (var t in local.Annotations.OfType<AnnotationText>()) geo.Annotations.Add(t with { Position = W(t.Position) });

        foreach (var (a, b, side, offMm, text) in dims)
            Dimension(geo, ctx, W(a), W(b), side, ctx.Mm(offMm), text, td.TextMm, terminal: td.Terminal);

        var right = W(mx).X;
        geo.Annotations.Add(new AnnotationText(new Vec2(td.Position.X + pad, td.Position.Y - ctx.Mm(2)), title, td.TextMm * 1.3, TextAlign.Left));
        geo.Annotations.Add(new AnnotationText(new Vec2(td.Position.X + pad, td.Position.Y - ctx.Mm(2) - titleH * 1.5), $"ESCALA 1:{td.DetailScale:0} – COTAS EM METROS", td.TextMm * 0.8, TextAlign.Left));
        var y = origin.Y - ctx.Mm(8);
        foreach (var note in notes)
        {
            geo.Annotations.Add(new AnnotationText(new Vec2(origin.X, y), "• " + note, td.TextMm, TextAlign.Left));
            y -= ctx.Mm(td.TextMm * 1.7);
        }
        // Moldura do detalhe.
        var frameMin = new Vec2(td.Position.X, y - ctx.Mm(2));
        var drawnRight = geo.Annotations.OfType<AnnotationLine>().SelectMany(l => l.Points).Select(p => p.X)
            .Concat(geo.Pieces.Select(p => p.Shape.Bounds.Max.X)).DefaultIfEmpty(right).Max();
        var frameMax = new Vec2(Math.Max(Math.Max(right, drawnRight) + pad, td.Position.X + TextWidth(title, td.TextMm * 1.3, ctx) + 2 * pad), td.Position.Y);
        foreach (var note in notes) frameMax = new Vec2(Math.Max(frameMax.X, origin.X + TextWidth("• " + note, td.TextMm, ctx) + pad), frameMax.Y);
        if (td.FrameBox) Frame(geo, frameMin, frameMax);
        // Título sublinhado (padrão de prancha).
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(td.Position.X + pad, td.Position.Y - ctx.Mm(2) - titleH * 1.12),
            new Vec2(td.Position.X + pad + TextWidth(title, td.TextMm * 1.3, ctx), td.Position.Y - ctx.Mm(2) - titleH * 1.12) }, MarkingColor.Preta));
        geo.UnitCount = 1;
        return geo;
    }

    private static BuildContext Plain(BuildContext ctx) => new() { Catalog = ctx.Catalog, Glyphs = ctx.Glyphs };

    private static void Frame(MarkingGeometry geo, Vec2 mn, Vec2 mx) =>
        geo.Annotations.Add(new AnnotationLine(new[] { mn, new Vec2(mx.X, mn.Y), mx, new Vec2(mn.X, mx.Y), mn }, MarkingColor.Preta));

    private static void LinearDetail(LinearMarkingDefinition l, TipoLinearDef t, VarianteDef v, BuildContext ctx, MarkingGeometry local,
        List<(Vec2, Vec2, Vec2, double, string)> dims, List<string> notes)
    {
        var period = v.Faixas.Select(f => f.Padrao.Sum()).DefaultIfEmpty(0).Max();
        var len = t.Transversal ? 4 : period > 0 ? Math.Min(30, Math.Max(2 * period, 3)) : 4;
        var c = (LinearMarkingDefinition)MarkingDefinition.FromJson(l.ToJson())!;
        c.Exclusions.Clear();
        c.Offset = 0; c.StartSetback = 0; c.EndSetback = 0; c.Phase = 0; c.Alignment = AlinhamentoPadrao.Inicio;
        local.Merge(MarkingBuilder.Build(c, new Polyline2(new[] { Vec2.Zero, new Vec2(len, 0) }), Plain(ctx)));

        var widthScale = l.WidthOverride is > 0 ? l.WidthOverride.Value / Math.Max(1e-6, v.Faixas.Max(f => f.Largura)) : 1;
        var stripes = v.Faixas.Select(f => (Y: f.Deslocamento, W: f.Largura * widthScale, f.Padrao)).OrderByDescending(f => f.Y).ToList();
        var top = stripes.Max(s => s.Y + s.W / 2);
        var bottom = stripes.Min(s => s.Y - s.W / 2);
        // Traço e espaço (primeira faixa tracejada).
        var dashed = stripes.FirstOrDefault(s => s.Padrao.Length >= 2);
        if (dashed.Padrao != null)
        {
            double x = 0;
            for (int i = 0; i < Math.Min(2, dashed.Padrao.Length); i++)
            {
                dims.Add((new Vec2(x, top), new Vec2(x + dashed.Padrao[i], top), Vec2.UnitY, 6, F(dashed.Padrao[i])));
                x += dashed.Padrao[i];
            }
            notes.Add($"Cadência: traço de {F(dashed.Padrao[0])} m e espaço de {F(dashed.Padrao[1])} m.");
        }
        else notes.Add(t.Transversal ? "Marca transversal – largura medida no sentido do tráfego." : "Linha contínua.");
        // Larguras e afastamentos (lado direito).
        var xr = len;
        var edges = new List<double>();
        foreach (var s in stripes) { edges.Add(s.Y + s.W / 2); edges.Add(s.Y - s.W / 2); }
        edges = edges.OrderByDescending(e => e).ToList();
        for (int i = 0; i + 1 < edges.Count; i++)
            if (edges[i] - edges[i + 1] > 1e-4)
                dims.Add((new Vec2(xr, edges[i]), new Vec2(xr, edges[i + 1]), Vec2.UnitX, 6 + i * 7, F(edges[i] - edges[i + 1])));
        if (stripes.Count > 1) dims.Add((new Vec2(xr, top), new Vec2(xr, bottom), Vec2.UnitX, 6 + edges.Count * 7, F(top - bottom)));
        notes.Add($"{t.Codigo} – {v.Nome}. Cor: {(l.ColorOverride ?? t.Cor).ToString().ToLowerInvariant()}.");
    }

    private static void SignElevation(SignDefinition sg, PlacaDef p, BuildContext ctx, MarkingGeometry local,
        List<(Vec2, Vec2, Vec2, double, string)> dims, List<string> notes)
    {
        var w = sg.Width ?? p.Largura;
        var h = sg.Height ?? p.Altura;
        var bottom = sg.MountHeight;
        var ph = SignGenerator.PlateHeight(p.Forma, w, h);
        foreach (var (shape, color) in FlatFace(p, w, h, sg.Legend, ctx.Glyphs))
        {
            var (fmn, _) = shape.Bounds;
            local.Pieces.Add(new MarkingPiece(shape, color));
        }
        // FlatFace desenha a partir de y = 0: sobe a placa até a altura livre.
        for (int i = 0; i < local.Pieces.Count; i++)
            local.Pieces[i] = local.Pieces[i] with { Shape = local.Pieces[i].Shape.Transform(v => v + new Vec2(0, bottom)) };
        var postW = Math.Max(0.04, sg.PostDiameter);
        void Post(double x) => local.Pieces.Insert(0, new MarkingPiece(Polygon2.Rectangle(new Vec2(x - postW / 2, 0), new Vec2(x + postW / 2, bottom + ph * 0.95)), MarkingColor.Metal));
        switch (sg.Support)
        {
            case TipoSuporte.Simples: Post(0); break;
            case TipoSuporte.Duplo: Post(-w / 3); Post(w / 3); break;
        }
        var gw = Math.Max(w, 0.8) * 0.9;
        local.Annotations.Add(new AnnotationLine(new[] { new Vec2(-gw, 0), new Vec2(gw, 0) }, MarkingColor.Preta));
        for (var x = -gw; x < gw - 0.05; x += 0.12)
            local.Annotations.Add(new AnnotationLine(new[] { new Vec2(x, 0), new Vec2(x - 0.08, -0.08) }, MarkingColor.Preta));

        dims.Add((new Vec2(-w / 2, bottom + ph), new Vec2(w / 2, bottom + ph), Vec2.UnitY, 6, F(w)));
        if (p.Forma is FormaPlaca.Retangulo or FormaPlaca.TrianguloInvertido or FormaPlaca.Losango or FormaPlaca.CruzSantoAndre)
            dims.Add((new Vec2(w / 2, bottom), new Vec2(w / 2, bottom + ph), Vec2.UnitX, 6, F(ph)));
        dims.Add((new Vec2(-w / 2, 0), new Vec2(-w / 2, bottom), -Vec2.UnitX, 6, F(bottom)));
        dims.Add((new Vec2(-w / 2, 0), new Vec2(-w / 2, bottom + ph), -Vec2.UnitX, 16, F(bottom + ph)));
        notes.Add($"{p.Codigo} – {p.Nome}. Altura livre sob a placa: {F(bottom)} m.");
        notes.Add($"Suporte: {(sg.Support == TipoSuporte.Duplo ? "duas colunas" : sg.Support == TipoSuporte.Nenhum ? "fixação em estrutura existente" : "coluna simples")} Ø {F(sg.PostDiameter * 1000, "0")} mm; afastamento lateral mínimo de 0,30 m do meio-fio.");
        if (!string.IsNullOrWhiteSpace(p.Descricao)) notes.Add(p.Descricao);
    }

    // ------------------------------------------------------------------ quadros

    private sealed record Column(string Header, double WidthM, TextAlign Align);

    public static MarkingGeometry QuantityTable(QuantityTableDefinition qt, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var all = (ctx.AllDefinitions?.Invoke() ?? Array.Empty<MarkingDefinition>()).ToList();
        var rowH = ctx.Mm(qt.RowMm);
        var th = qt.TextMm;
        var pad = ctx.Mm(1.5);
        var x0 = qt.Position.X;
        var y0 = qt.Position.Y;

        var rows = new List<(string[] Cells, bool Header, PlacaDef? Sign)>();
        string[] headers;
        TextAlign[] aligns;
        if (qt.SignsOnly)
        {
            headers = new[] { "SÍMBOLO", "Nº", "CÓDIGO", "DESCRIÇÃO", "DIMENSÕES (m)", "QTD" };
            aligns = new[] { TextAlign.Center, TextAlign.Center, TextAlign.Center, TextAlign.Left, TextAlign.Center, TextAlign.Center };
            var details = all.OfType<SignPlanDetailDefinition>().Where(d => !string.IsNullOrWhiteSpace(d.Number)).ToList();
            foreach (var g in all.OfType<SignDefinition>().GroupBy(s => (s.Code, W: s.Width, H: s.Height)).OrderBy(g => g.Key.Code, StringComparer.OrdinalIgnoreCase))
            {
                var p = ctx.Catalog.Placa(g.Key.Code);
                var w = g.Key.W ?? p?.Largura ?? 0;
                var h = g.Key.H ?? p?.Altura ?? 0;
                var ids = g.Select(s => s.Id).ToHashSet();
                var nums = details.Where(d => ids.Contains(d.SignId)).Select(d => d.Number!).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                var dim = p == null ? "" : p.Forma switch
                {
                    FormaPlaca.Circulo => $"Ø {F(w)}",
                    FormaPlaca.Octogono or FormaPlaca.Quadrado or FormaPlaca.Losango or FormaPlaca.TrianguloInvertido => $"L = {F(w)}",
                    _ => $"{F(w)} × {F(h)}",
                };
                rows.Add((new[] { "", string.Join(", ", nums), g.Key.Code, p?.Nome ?? g.Key.Code, dim, g.Count().ToString() }, false, p));
            }
        }
        else
        {
            headers = new[] { "ITEM", "CÓDIGO", "DESCRIÇÃO", "UN.", "QUANTIDADE" };
            aligns = new[] { TextAlign.Center, TextAlign.Center, TextAlign.Left, TextAlign.Center, TextAlign.Right };
            var items = all.Where(d => d is not IAnnotationDefinition).Select(d => (d, ctx.GeometryOf?.Invoke(d))).Where(i => i.Item2 != null)
                .Select(i => (i.d, i.Item2!)).ToList();
            var qrows = QuantityCalculator.Compute(items, ctx.Catalog);
            if (!string.IsNullOrEmpty(qt.Category) && Enum.TryParse<CategoriaQuantitativo>(qt.Category, out var cat))
                qrows = qrows.Where(r => r.Category == cat).ToList();
            // Uma linha por código (cores somadas), na unidade de medição do item.
            foreach (var cg in qrows.GroupBy(r => r.Category).OrderBy(g => g.Key))
            {
                rows.Add((new[] { QuantityRow.CategoryLabel(cg.Key).ToUpperInvariant() }, true, null));
                var catNo = (int)cg.Key + 1;
                var item = 0;
                foreach (var g in cg.GroupBy(r => r.Code).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var first = g.First();
                    var q = first.Unit switch { "m²" => g.Sum(r => r.Area), "m" => g.Sum(r => r.PaintedLength), _ => g.Sum(r => r.Units) };
                    rows.Add((new[] { $"{catNo}.{++item}", first.Code, first.Name, first.Unit, first.Unit is "m²" or "m" ? F(q) : q.ToString("0") }, false, null));
                }
            }
        }

        // Larguras das colunas a partir do conteúdo.
        var widths = new double[headers.Length];
        for (int c = 0; c < headers.Length; c++)
        {
            widths[c] = rows.Where(r => !r.Header && r.Cells.Length > c).Select(r => r.Cells[c]).Append(headers[c])
                .Max(s => LineWidthFactor(s.Length > 70 ? s[..70] : s)) * ctx.Mm(th) + 2 * pad;
        }
        if (qt.SignsOnly) widths[0] = Math.Max(widths[0], rowH * 1.6);
        var sampleRowH = qt.SignsOnly ? Math.Max(rowH, widths[0] * 0.8) : rowH;
        var totalW = widths.Sum();
        var titleH = rowH * 1.3;
        var bodyH = rowH + rows.Sum(r => r.Header ? rowH : sampleRowH);
        if (rows.Count == 0) bodyH += rowH;
        var totalH = titleH + bodyH;
        void Line(double ax, double ay, double bx, double by) => geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(ax, ay), new Vec2(bx, by) }, MarkingColor.Preta));

        Frame(geo, new Vec2(x0, y0 - totalH), new Vec2(x0 + totalW, y0));
        Line(x0, y0 - titleH, x0 + totalW, y0 - titleH);
        geo.Annotations.Add(new AnnotationText(new Vec2(x0 + totalW / 2, y0 - (titleH - ctx.Mm(th * 1.2)) / 2), qt.Title, th * 1.2));
        var y = y0 - titleH;
        void Cells(string[] cells, double h, bool header)
        {
            if (header && cells.Length == 1)
            {
                geo.Annotations.Add(new AnnotationText(new Vec2(x0 + pad, y - (h - ctx.Mm(th)) / 2), cells[0], th, TextAlign.Left));
            }
            else
            {
                var x = x0;
                for (int c = 0; c < widths.Length && c < cells.Length; c++)
                {
                    var text = cells[c].Length > 70 ? cells[c][..69] + "…" : cells[c];
                    var tx = aligns[c] switch { TextAlign.Left => x + pad, TextAlign.Right => x + widths[c] - pad, _ => x + widths[c] / 2 };
                    geo.Annotations.Add(new AnnotationText(new Vec2(tx, y - (h - ctx.Mm(th)) / 2), text, th, aligns[c]));
                    x += widths[c];
                }
            }
            y -= h;
            Line(x0, y, x0 + totalW, y);
        }
        var bodyTop = y;
        Cells(headers, rowH, false);
        var colTop = y;
        if (rows.Count == 0) Cells(new[] { "Nenhum item no projeto." }, rowH, true);
        var segments = new List<(double Top, double Bottom)>();
        var segTop = y;
        foreach (var r in rows)
        {
            if (r.Header)
            {
                if (segTop - y > 1e-9) segments.Add((segTop, y));
                Cells(r.Cells, rowH, true);
                segTop = y;
                continue;
            }
            var top = y;
            Cells(r.Cells, sampleRowH, false);
            if (r.Sign != null)
            {
                var face = FlatFace(r.Sign, r.Sign.Largura, r.Sign.Altura, r.Sign.Legenda, ctx.Glyphs);
                if (face.Count > 0)
                {
                    var (fmn, fmx) = Bounds(face.Select(f => f.Shape));
                    var box = Math.Min(widths[0], sampleRowH) - 2 * pad;
                    var k = box / Math.Max(fmx.X - fmn.X, fmx.Y - fmn.Y);
                    var cc = new Vec2(x0 + widths[0] / 2, top - sampleRowH / 2);
                    var fc = (fmn + fmx) / 2;
                    foreach (var (s, col) in face) geo.Pieces.Add(new MarkingPiece(s.Transform(v => cc + (v - fc) * k), col));
                }
            }
        }
        if (segTop - y > 1e-9) segments.Add((segTop, y));
        // Separadores verticais: no cabeçalho e em cada bloco (os títulos de categoria ocupam a linha inteira).
        segments.Insert(0, (bodyTop, colTop));
        foreach (var (t, b) in segments)
        {
            var x = x0;
            for (int c = 0; c + 1 < widths.Length; c++)
            {
                x += widths[c];
                Line(x, t, x, b);
            }
        }
        geo.UnitCount = rows.Count(r => !r.Header);
        return geo;
    }

    // ------------------------------------------------------------------ notas e norte

    /// <summary>Quebra o texto em linhas de até <paramref name="maxChars"/> caracteres.</summary>
    public static List<string> Wrap(string text, int maxChars)
    {
        var res = new List<string>();
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
                {
                    res.Add(line);
                    line = word;
                }
                else line = line.Length == 0 ? word : line + " " + word;
            }
            res.Add(line);
        }
        return res;
    }

    public static MarkingGeometry Notes(NotesDefinition nd, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var pad = ctx.Mm(2);
        var th = nd.TextMm;
        var lineH = ctx.Mm(th * 1.6);
        var width = ctx.Mm(nd.WidthMm);
        var maxChars = Math.Max(10, (int)((nd.WidthMm - 8 - (nd.Numbered ? th * 2 : 0)) / (th * 0.62)));
        var x0 = nd.Position.X;
        var y = nd.Position.Y - pad;
        geo.Annotations.Add(new AnnotationText(new Vec2(x0 + width / 2, y), nd.Title, th * 1.2));
        y -= ctx.Mm(th * 1.2) + pad;
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(x0, y), new Vec2(x0 + width, y) }, MarkingColor.Preta));
        y -= pad;
        var paras = nd.Text.Replace("\r", "").Split('\n').Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        for (int i = 0; i < paras.Count; i++)
        {
            var prefix = nd.Numbered ? $"{i + 1}." : "";
            var lines = Wrap(paras[i].Trim(), maxChars);
            var indent = nd.Numbered ? ctx.Mm(th * 2.2) : 0;
            if (nd.Numbered) geo.Annotations.Add(new AnnotationText(new Vec2(x0 + pad, y), prefix, th, TextAlign.Left));
            geo.Annotations.Add(new AnnotationText(new Vec2(x0 + pad + indent, y), string.Join("\n", lines), th, TextAlign.Left));
            y -= lines.Count * lineH + ctx.Mm(th * 0.5);
        }
        Frame(geo, new Vec2(x0, y - pad), new Vec2(x0 + width, nd.Position.Y));
        return geo;
    }

    public static MarkingGeometry NorthArrow(NorthArrowDefinition na, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var r = ctx.Mm(na.SizeMm) / 2;
        var c = na.Position;
        var a = na.AngleDeg * Math.PI / 180;
        Vec2 R(double x, double y) => c + new Vec2(x * Math.Cos(a) - y * Math.Sin(a), x * Math.Sin(a) + y * Math.Cos(a)) * r;
        var circle = CurveTools.Circle(c, r, r * 0.01);
        circle.Add(circle[0]);
        switch (na.Style)
        {
            case EstiloNorte.RosaDosVentos:
            {
                // Rosa dos ventos de 4 pontas: metades alternadas cheias/vazadas, letras N, L, S, O.
                for (int k = 0; k < 4; k++)
                {
                    var q = k * Math.PI / 2;
                    Vec2 Q(double x, double y) => R(x * Math.Cos(q) - y * Math.Sin(q), x * Math.Sin(q) + y * Math.Cos(q));
                    geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { Q(0, 0.95), Q(0, 0), Q(-0.2, 0.2) }), MarkingColor.Preta));
                    geo.Annotations.Add(new AnnotationLine(new[] { Q(0, 0.95), Q(0.2, 0.2), Q(0, 0) }, MarkingColor.Preta));
                }
                var inner = CurveTools.Circle(c, r * 0.45, r * 0.01);
                inner.Add(inner[0]);
                geo.Annotations.Add(new AnnotationLine(inner, MarkingColor.Preta));
                var t = ctx.Mm(3) / 2;
                geo.Annotations.Add(new AnnotationText(R(0, 1.2) + new Vec2(0, t * 2), "N", 3.5));
                geo.Annotations.Add(new AnnotationText(R(0, -1.2) + new Vec2(0, 0), "S", 2.5));
                geo.Annotations.Add(new AnnotationText(R(1.25, 0) + new Vec2(0, t), "L", 2.5, TextAlign.Left));
                geo.Annotations.Add(new AnnotationText(R(-1.25, 0) + new Vec2(0, t), "O", 2.5, TextAlign.Right));
                break;
            }
            case EstiloNorte.Seta:
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { R(0, 1.0), R(0.35, 0.35), R(0.1, 0.35), R(0.1, -0.9), R(-0.1, -0.9), R(-0.1, 0.35), R(-0.35, 0.35) }), MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationText(R(0, 1.15) + new Vec2(0, ctx.Mm(3)), "N", 3.5));
                break;
            default:
                geo.Annotations.Add(new AnnotationLine(circle, MarkingColor.Preta));
                // Seta: metade preenchida, metade em contorno.
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { R(0, 0.95), R(0, -0.55), R(-0.32, -0.75) }), MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationLine(new[] { R(0, 0.95), R(0.32, -0.75), R(0, -0.55) }, MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationText(R(0, 1.15) + new Vec2(0, ctx.Mm(3)), "N", 3.5));
                break;
        }
        return geo;
    }
}
