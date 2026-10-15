using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Perfil transversal – desenho por elemento (rodada LX, referências "seção típica de via local" e "meio-fio com sarjeta tipo B"):
/// cotas encadeadas por elemento e cota total no topo, eixo com rótulo, caimentos sobre a superfície, camadas do pavimento com
/// hachuras distintas e chamadas, terreno lateral hachurado, detalhe ampliado do meio-fio e da sarjeta com cotas em cm e título
/// sublinhado com a escala. Tudo em mm de papel × escala da vista; cada texto vai para onde não sobrepõe outro.
/// </summary>
public static partial class DetailGenerator
{
    /// <summary>Retângulos já ocupados (coordenadas da vista): textos e marcas finas (linhas de chamada, prolongamentos de cota).</summary>
    private sealed class Placer
    {
        public readonly List<(double X0, double Y0, double X1, double Y1)> Texts = new();
        public readonly List<(double X0, double Y0, double X1, double Y1)> Marks = new();

        private static bool Hit((double X0, double Y0, double X1, double Y1) r, (double X0, double Y0, double X1, double Y1) o) =>
            r.X1 > o.X0 && r.X0 < o.X1 && r.Y1 > o.Y0 && r.Y0 < o.Y1;

        public bool Free((double X0, double Y0, double X1, double Y1) r) => !Texts.Any(o => Hit(r, o)) && !Marks.Any(o => Hit(r, o));
    }

    /// <summary>Retângulo de um texto horizontal (topo em <paramref name="top"/>), com acentos, descendentes e folga.</summary>
    private static (double X0, double Y0, double X1, double Y1) TextRect(Vec2 top, string text, double mm, TextAlign align, BuildContext ctx,
        double padMm = 0.35)
    {
        var w = TextWidth(text, mm, ctx);
        var n = text.Split('\n').Length;
        var x0 = align switch { TextAlign.Left => top.X, TextAlign.Right => top.X - w, _ => top.X - w / 2 };
        var p = ctx.Mm(padMm);
        return (x0 - p, top.Y - ctx.Mm(mm * (1.3 + 1.45 * (n - 1))) - p, x0 + w + p, top.Y + ctx.Mm(mm * 0.25) + p);
    }

    /// <summary>Elemento da pista (recebe as camadas do pavimento por baixo): faixas, ciclofaixa, sarjeta...</summary>
    private static bool IsRoadKey(string? key) => key is "Faixa de rolamento" or "Pista" or "Faixa de ônibus" or "Ciclofaixa" or "Estacionamento"
        or "Zebrado" or "Canalização" or "Sarjeta" or "Sarjetão" or "Segregador";

    private static readonly string[] LayerTones = { "#CFCBC2", "#DCCDAE", "#D3C4A4", "#C9BD9F" };
    private const string GroundTone = "#ECE5D8";

    /// <summary>Perfil transversal no desenho por elemento (ver o resumo da classe).</summary>
    private static void SectionProfileV2(MarkingGeometry geo, SectionDimensionDefinition sd, SectionProfileDefinition pd, BuildContext ctx,
        IReadOnlyList<(MarkingDefinition Def, MarkingGeometry Geo)> withGeo, List<double> planStations, List<double> axes, Vec2 pos, string letter)
    {
        var a = sd.Start;
        var b = sd.End;
        var L = a.DistanceTo(b);
        var firstAnnotation = geo.Annotations.Count;
        var firstPiece = geo.Pieces.Count;
        var segs = ProfileSegments(a, b, withGeo);
        if (segs.Count == 0) { geo.Warnings.Add("Perfil transversal: nada cortado pela linha de seção."); return; }
        var spec = pd.CurbGutter ?? new CurbGutterSpec();
        var layers = SectionLayer.Effective(pd.Layers);
        var styles = pd.ItemStyles;
        var place = new Placer();

        // Caimento real: trechos quebrados no eixo e nos bordos; cada ponta recebe o desnível do caimento.
        var saxes = pd.DrawSlopes ? SectionAxes(sd, ctx) : new List<SectionAxis>();
        double Dz(double s) => pd.DrawSlopes ? SlopeDz(saxes, s, pd.CrossSlopePct, pd.SidewalkSlopePct) : 0;
        var cuts = new List<double>();
        foreach (var x in saxes)
        {
            cuts.Add(x.S);
            foreach (var sg in new[] { 1.0, -1.0 })
            {
                var w = sg * x.LeftSign >= 0 ? x.Pav.LeftWidth : x.Pav.RightWidth;
                cuts.Add(x.S + sg * w / x.Sin);
                cuts.Add(x.S + sg * (w + x.Pav.CurbWidth) / x.Sin);
            }
        }
        List<ProfileSegment> Split(IEnumerable<ProfileSegment> src, IReadOnlyList<double> at)
        {
            var res = new List<ProfileSegment>();
            foreach (var sg in src)
            {
                var s0 = sg.S0;
                foreach (var c in at.Where(c => c > sg.S0 + 0.01 && c < sg.S1 - 0.01).OrderBy(c => c)) { res.Add(sg with { S0 = s0, S1 = c }); s0 = c; }
                res.Add(sg with { S0 = s0 });
            }
            return res;
        }
        segs = Split(Split(segs, cuts), planStations);
        var stretches = Stretches(withGeo, segs, planStations, a, b);
        const double Eps = 0.004;

        (string? Rgb, Hachura Hatch) Look(ProfileSegment sg)
        {
            var mid = (sg.S0 + sg.S1) / 2;
            var def = sg.Paint ? Hachura.Nenhuma : DefaultHatch(sg.Color, sg.Def);
            var tr = stretches.FirstOrDefault(x => x.S0 <= mid + 1e-6 && x.S1 >= mid - 1e-6);
            if (tr == null || tr.Top.Color != sg.Color || tr.Top.Paint != sg.Paint || !ReferenceEquals(tr.Top.Def, sg.Def)) return (null, def);
            var style = styles?.FirstOrDefault(x => x.Key == tr.Key);
            var rgb = style?.Rgb is { } hex && Rgb.TryParse(hex, out var parsed) ? parsed.Hex : null;
            return (rgb, style?.Hatch ?? def);
        }
        static bool IsCurb(ProfileSegment x) => !x.Paint && x.Def is LinearMarkingDefinition l && l.Code.StartsWith("MEIO-FIO", StringComparison.OrdinalIgnoreCase);
        var buried = Math.Max(0, spec.BuriedCm) / 100.0;
        double CurbBottom(ProfileSegment x) => x.Z0 - buried;

        // Camadas sob a pista: do fundo do revestimento (ou da sarjeta) para baixo, nas espessuras escolhidas.
        string? KeyAt(double s) => stretches.FirstOrDefault(x => x.S0 <= s + 1e-6 && x.S1 >= s - 1e-6)?.Key;
        double? SurfaceBottom(double s) =>
            segs.Where(x => !x.Paint && x.S0 <= s + 1e-6 && x.S1 >= s - 1e-6 && x.Z0 < 0.5).Select(x => (double?)x.Z0).DefaultIfEmpty(null).Min();
        // Intervalos entre todas as bordas do corte; pista = pavimento ou sarjeta por cima (meio-fio fora).
        var bounds = new List<double>();
        foreach (var v in segs.SelectMany(x => new[] { x.S0, x.S1 }).Concat(planStations).Append(0.0).Append(L)
                     .Select(v => Math.Clamp(v, 0, L)).OrderBy(v => v))
            if (bounds.Count == 0 || v - bounds[^1] > 0.005) bounds.Add(v);
        bool RoadAt(double s) =>
            !segs.Any(x => IsCurb(x) && x.S0 <= s + 1e-6 && x.S1 >= s - 1e-6) &&
            segs.Any(x => !x.Paint && x.S0 <= s + 1e-6 && x.S1 >= s - 1e-6 && x.Z0 < 0.5 &&
                          (MarkingColors.IsPavement(x.Color) || x.Def is LinearMarkingDefinition l && l.Code.StartsWith("SARJETA", StringComparison.OrdinalIgnoreCase)));
        var roadRuns = new List<(double S0, double S1)>();
        for (int i = 0; i + 1 < bounds.Count; i++)
        {
            if (!RoadAt((bounds[i] + bounds[i + 1]) / 2) || SurfaceBottom((bounds[i] + bounds[i + 1]) / 2) == null) continue;
            if (roadRuns.Count > 0 && Math.Abs(roadRuns[^1].S1 - bounds[i]) < 1e-6) roadRuns[^1] = (roadRuns[^1].S0, bounds[i + 1]);
            else roadRuns.Add((bounds[i], bounds[i + 1]));
        }
        var layerDepth = layers.Sum(x => x.ThicknessCm) / 100.0;

        var mag = ctx.ViewScale / Math.Max(1, pd.ProfileScale);
        var vex = Math.Clamp(pd.VerticalExaggeration, 1, 10);
        var vmag = mag * vex;
        var paintMin = ctx.Mm(0.35) / vmag;
        var zMax = segs.Max(x => (x.Paint ? x.Z1 + paintMin : x.Z1) + Math.Max(Dz(x.S0 + Eps), Dz(x.S1 - Eps)));
        var zBottom = Math.Min(0, segs.Min(x => (IsCurb(x) ? CurbBottom(x) : x.Z0) + Math.Min(Dz(x.S0 + Eps), Dz(x.S1 - Eps))));
        foreach (var (r0, r1) in roadRuns)
            foreach (var s in new[] { r0 + Eps, (r0 + r1) / 2, r1 - Eps })
                if (SurfaceBottom(s) is { } sb) zBottom = Math.Min(zBottom, sb + Dz(s) - layerDepth);
        var tMm = pd.TextMm;
        var nm = tMm * 0.8;          // nomes e chamadas
        var sm = tMm * 0.75;         // níveis e caimentos
        var fmt = pd.Decimals <= 0 ? "0" : "0." + new string('0', Math.Clamp(pd.Decimals, 0, 3));
        // Posições horizontais relativas ao início do corte; o desenho é empurrado para a direita do ponto de inserção o quanto
        // sobrar para a esquerda (terreno lateral, nomes e cotas espalhados).
        double R(double s) => s * mag;

        // ---------------------------------------------------------------- nomes dos elementos (uma linha, com chamadas)
        var labels = new List<(double Anchor, string Text, double W, bool Inline)>();
        foreach (var t in stretches)
        {
            var name = ItemName(styles, t.Key);
            var w = (t.S1 - t.S0) * mag;
            var text = TextWidth(name, nm, ctx) < w * 0.94 ? name
                : name.Contains(' ') && name.Split(' ').Max(x => TextWidth(x, nm, ctx)) < Math.Max(w * 0.94, ctx.Mm(26)) ? WrapToWidth(name, Math.Max(w * 0.94, ctx.Mm(26)), nm, ctx) : name;
            var tw = TextWidth(text, nm, ctx);
            labels.Add((R((t.S0 + t.S1) / 2), text, tw, tw < w * 0.94));
        }
        var labelX = PackRow(labels.Select(l => (l.Anchor, l.W)).ToList(), ctx.Mm(1.6));
        var nameLines = labels.Count == 0 ? 1 : labels.Max(l => l.Text.Split('\n').Length);

        // ---------------------------------------------------------------- cotas encadeadas por elemento: linhas dos textos
        var spans = new List<(double C, double W, string Text, int Row, double At)>();
        for (int i = 0; i + 1 < planStations.Count; i++)
        {
            var len = planStations[i + 1] - planStations[i];
            var text = len.ToString(fmt, Pt);
            var c = (R(planStations[i]) + R(planStations[i + 1])) / 2;
            spans.Add((c, len * mag, text, 0, c));
        }
        var rowsUsed = ChainRows(spans, ctx, tMm);
        var ext = ctx.Mm(9);
        var over = new[] { ext + ctx.Mm(1.5), labels.Count > 0 ? -labelX.Zip(labels, (x, l) => x - l.W / 2).Min() : 0,
            spans.Count > 0 ? -spans.Min(sp => sp.At - TextWidth(sp.Text, tMm, ctx) / 2) : 0 }.Max();
        var x0 = pos.X + over + ctx.Mm(1.5);
        double X(double s) => x0 + s * mag;
        for (int i = 0; i < labels.Count; i++) { labels[i] = labels[i] with { Anchor = labels[i].Anchor + x0 }; labelX[i] += x0; }
        for (int i = 0; i < spans.Count; i++) spans[i] = spans[i] with { C = spans[i].C + x0, At = spans[i].At + x0 };

        // ---------------------------------------------------------------- alturas: de baixo (superfície) para cima
        var bandH = ctx.Mm(sm * 1.3 + 4.0);                      // níveis e setas de caimento sobre a superfície
        var gapNames = ctx.Mm(3.0);                              // chamadas inclinadas entre os nomes e a faixa dos níveis
        var namesH = ctx.Mm(nm * (1.3 + 1.45 * (nameLines - 1)));
        var chainGap = ctx.Mm(3.0);
        double RowTop(int k) => ctx.Mm(0.6 + tMm + (k > 0 ? tMm * 1.5 + 1.2 : 0));
        var totalGap = ctx.Mm(2.5);
        var axisLabelH = ctx.Mm(tMm * 0.85 * 1.3 + 2.0);
        var hasTotal = planStations.Count > 2;
        var aboveTop = bandH + gapNames + namesH + chainGap + RowTop(Math.Max(0, rowsUsed - 1)) + (hasTotal ? totalGap + RowTop(0) : 0)
            + (axes.Count > 0 ? axisLabelH : 0);
        var yTop = pos.Y - ctx.Mm(2) - aboveTop;
        double Y(double z) => yTop - (zMax - z) * vmag;
        Vec2 P(double s, double z) => new(X(s), Y(z));
        var bandTop = yTop + bandH;
        var namesBottom = bandTop + gapNames;
        var namesTop = namesBottom + namesH;
        var chainY = namesTop + chainGap;
        var totalY = chainY + RowTop(Math.Max(0, rowsUsed - 1)) + totalGap;
        var axisTop = (hasTotal ? totalY + RowTop(0) : chainY + RowTop(Math.Max(0, rowsUsed - 1))) + axisLabelH;
        var groundY = Y(zBottom) - ctx.Mm(4.5);

        // ---------------------------------------------------------------- terreno: sob o corte e nas laterais (hachura de terra)
        // Um polígono só: o fundo de cada trecho (camadas, meio-fio enterrado, calçada) em cima, o fundo do desenho embaixo e o
        // terreno além do alinhamento dos dois lados, no nível da ponta do corte, com a linha de interrupção por fora.
        double Underside(double s)
        {
            var z = segs.Where(x => !x.Paint && x.S0 <= s + 1e-6 && x.S1 >= s - 1e-6 && x.Z0 < 0.5).Select(x => IsCurb(x) ? CurbBottom(x) : x.Z0)
                .DefaultIfEmpty(0).Min();
            if (roadRuns.Any(r => s >= r.S0 - 1e-6 && s <= r.S1 + 1e-6) && SurfaceBottom(s) is { } sb) z = Math.Min(z, sb - layerDepth);
            return z;
        }
        var ground = new List<Vec2>();
        for (int i = bounds.Count - 2; i >= 0; i--)
        {
            var u = Underside((bounds[i] + bounds[i + 1]) / 2);
            ground.Add(P(bounds[i + 1], u + Dz(Math.Max(bounds[i], bounds[i + 1] - Eps))));
            ground.Add(P(bounds[i], u + Dz(Math.Min(bounds[i + 1], bounds[i] + Eps))));
        }
        var ends = new List<(double X, double Y)>();
        foreach (var (s, dir) in new[] { (0.0, -1.0), (L, 1.0) })
        {
            var si = Math.Clamp(s - dir * Eps * 2, 0, L);
            var top = SurfaceTop(segs, si) ?? 0;
            ends.Add((X(s) + dir * ext, Y(top + Dz(si))));
        }
        var soil = new List<Vec2> { new(ends[0].X, groundY), new(ends[1].X, groundY), new(ends[1].X, ends[1].Y), new(X(L), ends[1].Y) };
        soil.AddRange(ground);
        soil.Add(new Vec2(X(0), ends[0].Y));
        soil.Add(new Vec2(ends[0].X, ends[0].Y));
        geo.Pieces.Add(new MarkingPiece(new Polygon2(soil), MarkingColor.Terra) { Rgb = GroundTone, Hatch = Hachura.Terra });
        foreach (var (xo, yz) in ends)
        {
            var xi = xo < X(0) ? X(0) : X(L);
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(xi, yz), new Vec2(xo, yz) }, MarkingColor.Preta));
            geo.Annotations.Add(new AnnotationLine(BreakLine(new Vec2(xo, yz + ctx.Mm(1)), new Vec2(xo, groundY - ctx.Mm(1)), ctx), MarkingColor.Preta));
        }

        // ---------------------------------------------------------------- camadas do pavimento (sob a pista)
        var layerPolys = new List<(int K, List<Vec2> Top, List<Vec2> Bottom)>();
        foreach (var (r0, r1) in roadRuns)
        {
            var cutsIn = bounds.Where(s => s >= r0 - 1e-6 && s <= r1 + 1e-6).ToList();
            var depth = 0.0;
            for (int k = 0; k < layers.Count; k++)
            {
                var t = layers[k].ThicknessCm / 100.0;
                var topPts = new List<Vec2>();
                var botPts = new List<Vec2>();
                for (int i = 0; i + 1 < cutsIn.Count; i++)
                {
                    var m = (cutsIn[i] + cutsIn[i + 1]) / 2;
                    var sb = SurfaceBottom(m) ?? 0;
                    foreach (var s in new[] { cutsIn[i], cutsIn[i + 1] })
                    {
                        var d = Dz(Math.Clamp(s, cutsIn[i] + Eps, cutsIn[i + 1] - Eps));
                        topPts.Add(P(s, sb - depth + d));
                        botPts.Add(P(s, sb - depth - t + d));
                    }
                }
                var ring = topPts.Concat(Enumerable.Reverse(botPts)).ToList();
                geo.Pieces.Add(new MarkingPiece(new Polygon2(ring), k == 0 ? MarkingColor.Brita : MarkingColor.Terra)
                    { Rgb = LayerTones[k % LayerTones.Length], Hatch = layers[k].Hatch });
                geo.Annotations.Add(new AnnotationLine(ring.Append(ring[0]).ToList(), MarkingColor.Preta));
                layerPolys.Add((k, topPts, botPts));
                depth += t;
            }
        }

        // ---------------------------------------------------------------- elementos cortados (cor e hachura do elemento)
        foreach (var sg in segs.OrderBy(x => x.Paint).ThenBy(x => x.Z1 - x.Z0 > 0.3 ? 1 : 0))
        {
            var z1 = sg.Paint ? sg.Z1 + paintMin : sg.Z1;
            var d0 = Dz(sg.S0 + Eps);
            var d1 = Dz(sg.S1 - Eps);
            Vec2[] quad;
            if (IsCurb(sg))
            {
                // Lado da pista = o mais baixo; o espelho inclina conforme a inclinação escolhida na altura aparente.
                var others = segs.Where(x => !IsCurb(x)).ToList();
                var streetAtStart = (SurfaceTop(others, sg.S0 - 0.05) ?? -1) <= (SurfaceTop(others, sg.S1 + 0.05) ?? -1);
                var bat = Math.Min((sg.S1 - sg.S0) * 0.5, Math.Max(0, z1 - sg.Z0) * spec.FaceSlopePct / 100.0);
                var zb = CurbBottom(sg);
                quad = streetAtStart
                    ? new[] { P(sg.S0, zb + d0), P(sg.S1, zb + d1), P(sg.S1, z1 + d1), P(sg.S0 + bat, z1 + d0), P(sg.S0, sg.Z0 + d0) }
                    : new[] { P(sg.S0, zb + d0), P(sg.S1, zb + d1), P(sg.S1, sg.Z0 + d1), P(sg.S1 - bat, z1 + d1), P(sg.S0, z1 + d0) };
            }
            else quad = new[] { P(sg.S0, sg.Z0 + d0), P(sg.S1, sg.Z0 + d1), P(sg.S1, z1 + d1), P(sg.S0, z1 + d0) };
            var (rgb, hatch) = Look(sg);
            geo.Pieces.Add(new MarkingPiece(new Polygon2(quad), sg.Color) { Rgb = rgb, Hatch = hatch });
            if (!sg.Paint) geo.Annotations.Add(new AnnotationLine(quad.Append(quad[0]).ToList(), MarkingColor.Preta));
        }

        // ---------------------------------------------------------------- textos: nomes (com chamadas), caimentos, níveis
        for (int i = 0; i < labels.Count; i++)
        {
            var (anchor, text, w, inline) = labels[i];
            var lx = labelX[i];
            var top = new Vec2(lx, namesTop - ctx.Mm(nm * 1.45 * (nameLines - text.Split('\n').Length)));
            geo.Annotations.Add(new AnnotationText(top, text, nm));
            place.Texts.Add(TextRect(top, text, nm, TextAlign.Center, ctx));
            var t = stretches[i];
            var surf = SurfaceTop(segs, (t.S0 + t.S1) / 2);
            if (inline && Math.Abs(lx - anchor) < ctx.Mm(0.3) || surf == null) continue;
            // Chamada: do nome desce, inclina até o elemento acima da faixa dos níveis e desce até a superfície (ponto).
            var lblBottom = top.Y - ctx.Mm(nm * (1.3 + 1.45 * (text.Split('\n').Length - 1))) - ctx.Mm(0.3);
            var end = P((t.S0 + t.S1) / 2, surf.Value + Dz((t.S0 + t.S1) / 2)) + new Vec2(0, ctx.Mm(0.5));
            var knee = new Vec2(anchor, bandTop);
            var pts = new List<Vec2> { new(lx, lblBottom), new(lx, Math.Max(namesBottom - ctx.Mm(0.5), bandTop + ctx.Mm(0.4))), knee, end };
            geo.Annotations.Add(new AnnotationLine(pts, MarkingColor.Preta));
            Dot(geo, end, ctx);
            place.Marks.Add((anchor - ctx.Mm(0.3), end.Y, anchor + ctx.Mm(0.3), bandTop));
        }

        var stations = planStations.Concat(segs.Where(x => !x.Paint).SelectMany(x => new[] { x.S0, x.S1 }))
            .Where(s => s >= -1e-6 && s <= L + 1e-6).OrderBy(s => s).ToList();
        var st = new List<double>();
        foreach (var s in stations) if (st.Count == 0 || s - st[^1] > 0.02) st.Add(s);

        // Caimento transversal (pista e calçada): seta sobre a superfície apontando para o lado mais baixo, com o valor.
        for (int i = 0; i + 1 < st.Count; i++)
        {
            var s0 = st[i];
            var s1 = st[i + 1];
            var len = s1 - s0;
            var mid = (s0 + s1) / 2;
            var key = KeyAt(mid);
            var isRoad = key is "Faixa de rolamento" or "Pista" or "Faixa de ônibus" or "Ciclofaixa" or "Estacionamento";
            var isWalk = key is "Calçada";
            var w = len * mag;
            if (pd.CrossSlopePct <= 0 || !(isRoad || isWalk) || w < ctx.Mm(isWalk ? 9 : 12) || SurfaceTop(segs, mid) is not { } top0) continue;
            var slopeHere = len > 0.05 ? (Dz(s1 - Eps) - Dz(s0 + Eps)) / len : 0;
            var pct = Math.Abs(slopeHere) * 100;
            if (pd.DrawSlopes && pct <= 0.05) continue;
            double dir;
            if (pd.DrawSlopes) dir = slopeHere < 0 ? 1.0 : -1.0;
            else
            {
                var ax = axes.Count > 0 ? axes.OrderBy(x => Math.Abs(x - mid)).First() : L / 2;
                dir = mid >= ax ? 1.0 : -1.0;
                pct = isWalk ? pd.SidewalkSlopePct : pd.CrossSlopePct;
            }
            var label = $"i = {pct.ToString("0.0", Pt)} %";
            var half = Math.Min(w * 0.18, ctx.Mm(7));
            foreach (var f in new[] { 0.5, 0.7, 0.3, 0.82, 0.18 })
            {
                var c = s0 + len * f;
                var sA = c - dir * half / mag;
                var sB = c + dir * half / mag;
                var lift = ctx.Mm(1.1);
                var pa = P(sA, top0 + Dz(sA)) + new Vec2(0, lift);
                var pb = P(sB, top0 + Dz(sB)) + new Vec2(0, lift);
                var tTop = new Vec2(X(c), Math.Max(pa.Y, pb.Y) + ctx.Mm(0.6 + sm * 1.25));
                var tr = TextRect(tTop, label, sm, TextAlign.Center, ctx);
                var ar = (Math.Min(pa.X, pb.X), Math.Min(pa.Y, pb.Y) - ctx.Mm(0.5), Math.Max(pa.X, pb.X), Math.Max(pa.Y, pb.Y) + ctx.Mm(0.5));
                if (!place.Free(tr) || !place.Free(ar)) continue;
                geo.Annotations.Add(new AnnotationLine(new[] { pa, pb }, MarkingColor.Preta));
                var u = (pb - pa).Normalized();
                var ah = ctx.Mm(1.1);
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { pb, pb - u * ah * 1.6 + u.PerpLeft * ah * 0.5, pb - u * ah * 1.6 - u.PerpLeft * ah * 0.5 }), MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationText(tTop, label, sm));
                place.Texts.Add(tr);
                place.Marks.Add(ar);
                break;
            }
        }

        // Níveis (▽ +0,15) de cada trecho, onde houver lugar.
        if (pd.ProfileLevels)
        {
            double? lastLevel = null;
            for (int i = 0; i + 1 < st.Count; i++)
            {
                var s0 = st[i];
                var s1 = st[i + 1];
                var mid = (s0 + s1) / 2;
                if (SurfaceTop(segs, mid) is not { } top0) { lastLevel = null; continue; }
                var lv = top0 + Dz(mid);
                if (lastLevel != null && Math.Abs(lastLevel.Value - lv) <= 0.005) continue;
                var lt = Level(lv);
                foreach (var f in new[] { 0.5, 0.2, 0.8 })
                {
                    var s = s0 + (s1 - s0) * f;
                    var tip = P(s, top0 + Dz(s)) + new Vec2(0, ctx.Mm(0.4));
                    var th = ctx.Mm(1.2);
                    var tTop = tip + new Vec2(0, th + ctx.Mm(0.6 + sm * 1.25));
                    var tr = TextRect(tTop, lt, sm, TextAlign.Center, ctx);
                    var mark = (tip.X - th * 0.6, tip.Y, tip.X + th * 0.6, tip.Y + th);
                    if (!place.Free(tr) || !place.Free(mark)) continue;
                    geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { tip, tip + new Vec2(-th * 0.6, th), tip + new Vec2(th * 0.6, th) }), MarkingColor.Preta));
                    geo.Annotations.Add(new AnnotationText(tTop, lt, sm));
                    place.Texts.Add(tr);
                    place.Marks.Add(mark);
                    lastLevel = lv;
                    break;
                }
            }
        }

        // Cotas verticais dos desníveis (texto ao lado, só onde cabe – o detalhe do meio-fio traz as medidas em cm).
        if (pd.ProfileHeights)
            for (int i = 1; i + 1 < st.Count; i++)
            {
                var s = st[i];
                var left = SurfaceTop(segs, (st[i - 1] + s) / 2) + Dz(s - Eps);
                var right = SurfaceTop(segs, (s + st[i + 1]) / 2) + Dz(s + Eps);
                if (left == null || right == null || Math.Abs(left.Value - right.Value) < 0.03) continue;
                var low = Math.Min(left.Value, right.Value);
                var high = Math.Max(left.Value, right.Value);
                var side = left.Value < right.Value ? -1.0 : 1.0;
                var xd = X(s) + side * ctx.Mm(3.0);
                var text = (high - low).ToString("0.00", Pt);
                var tTop = new Vec2(xd + side * ctx.Mm(0.8), (Y(low) + Y(high)) / 2 + ctx.Mm(sm * 0.65));
                var al = side < 0 ? TextAlign.Right : TextAlign.Left;
                var tr = TextRect(tTop, text, sm, al, ctx);
                var mark = (Math.Min(X(s), xd) - ctx.Mm(0.3), Y(low), Math.Max(X(s), xd) + ctx.Mm(0.3), Y(high));
                if (!place.Free(tr) || !place.Free(mark)) continue;
                foreach (var z in new[] { low, high })
                    geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(X(s) + side * ctx.Mm(0.8), Y(z)), new Vec2(xd + side * ctx.Mm(1.0), Y(z)) }, MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(xd, Y(low)), new Vec2(xd, Y(high)) }, MarkingColor.Preta));
                foreach (var z in new[] { low, high }) Tick(geo, new Vec2(xd, Y(z)), new Vec2(0, 1), ctx, pd.Terminal);
                geo.Annotations.Add(new AnnotationText(tTop, text, sm, al));
                place.Texts.Add(tr);
                place.Marks.Add(mark);
            }

        // ---------------------------------------------------------------- cotas no topo: cadeia por elemento e total
        var xs = planStations.Select(X).ToList();
        if (xs.Count >= 2)
        {
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(xs[0] - ctx.Mm(1.5), chainY), new Vec2(xs[^1] + ctx.Mm(1.5), chainY) }, MarkingColor.Preta));
            foreach (var x in xs)
            {
                Tick(geo, new Vec2(x, chainY), new Vec2(1, 0), ctx, pd.Terminal);
                place.Marks.Add((x - ctx.Mm(0.25), chainY - ctx.Mm(1.0), x + ctx.Mm(0.25), chainY + ctx.Mm(1.0)));
            }
            foreach (var (c, _, text, row, x) in spans)
            {
                var top = new Vec2(x, chainY + RowTop(row));
                geo.Annotations.Add(new AnnotationText(top, text, tMm));
                place.Texts.Add(TextRect(top, text, tMm, TextAlign.Center, ctx));
                if (row == 0) continue;
                // Vão estreito: texto na linha de cima com chamada até o meio do vão.
                var knee = new Vec2(c, chainY + ctx.Mm(0.6 + tMm * 1.25 + 0.2));
                geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(x, top.Y - ctx.Mm(tMm * 1.3 + 0.1)), knee, new Vec2(c, chainY) }, MarkingColor.Preta));
                place.Marks.Add((c - ctx.Mm(0.25), chainY, c + ctx.Mm(0.25), knee.Y));
            }
            if (hasTotal)
            {
                geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(xs[0] - ctx.Mm(1.5), totalY), new Vec2(xs[^1] + ctx.Mm(1.5), totalY) }, MarkingColor.Preta));
                foreach (var x in new[] { xs[0], xs[^1] }) Tick(geo, new Vec2(x, totalY), new Vec2(1, 0), ctx, pd.Terminal);
                var text = (planStations[^1] - planStations[0]).ToString(fmt, Pt);
                var top = new Vec2((xs[0] + xs[^1]) / 2, totalY + RowTop(0));
                geo.Annotations.Add(new AnnotationText(top, text, tMm));
                place.Texts.Add(TextRect(top, text, tMm, TextAlign.Center, ctx));
            }
            // Linhas de chamada das cotas: da superfície até a cota total (pontas) ou a cadeia, interrompidas nos textos.
            for (int i = 0; i < xs.Count; i++)
            {
                var s = planStations[i];
                var surf = SurfaceTop(segs, Math.Clamp(s + (i == 0 ? Eps : -Eps), 0, L));
                var from = surf != null ? Y(surf.Value + Dz(Math.Clamp(s, 0, L))) + ctx.Mm(1.0) : yTop + ctx.Mm(1.0);
                var to = (i == 0 || i == xs.Count - 1) && hasTotal ? totalY + ctx.Mm(1.0) : chainY + ctx.Mm(1.0);
                Broken(geo, new Vec2(xs[i], from), new Vec2(xs[i], to), place, ctx);
            }
        }

        // ---------------------------------------------------------------- eixo (traço-ponto) com o símbolo e o rótulo
        foreach (var s0 in axes)
        {
            var x = X(s0);
            var labelTop = axisTop - ctx.Mm(0.6);
            var r = ctx.Mm(tMm * 0.42);
            var cy = labelTop - ctx.Mm(tMm * 0.85) / 2;
            CenterLineSymbol(geo, new Vec2(x, cy), r);
            var top = new Vec2(x + r * 1.4 + ctx.Mm(0.6), labelTop);
            geo.Annotations.Add(new AnnotationText(top, "EIXO", tMm * 0.85, TextAlign.Left));
            place.Texts.Add(TextRect(top, "EIXO", tMm * 0.85, TextAlign.Left, ctx));
            DashDotBroken(geo, new Vec2(x, groundY - ctx.Mm(1.5)), new Vec2(x, cy - r - ctx.Mm(0.8)), place, ctx);
        }

        // ---------------------------------------------------------------- chamadas das camadas (abaixo do perfil)
        var main = stretches.Where(t => t.Key is "Faixa de rolamento" or "Pista").OrderByDescending(t => t.S1 - t.S0).FirstOrDefault()
                   ?? stretches.Where(t => IsRoadKey(t.Key)).OrderByDescending(t => t.S1 - t.S0).FirstOrDefault();
        var rows = new List<(string Text, Vec2 Anchor)>();
        if (main != null)
        {
            var w = main.S1 - main.S0;
            var pave = segs.Where(x => !x.Paint && MarkingColors.IsPavement(x.Color) && x.S0 <= main.S0 + w / 2 && x.S1 >= main.S0 + w / 2).FirstOrDefault();
            var sAt = new List<double>();
            var n = 1 + layers.Count + 1;
            for (int k = 0; k < n; k++) sAt.Add(main.S0 + w * (0.62 - 0.08 * k));
            var sb = SurfaceBottom(main.S0 + w / 2) ?? 0;
            if (pave != null)
            {
                var s = sAt[0];
                rows.Add(($"{LayerName(pave)} – e = {((pave.Z1 - pave.Z0) * 100).ToString("0.#", Pt)} cm", P(s, (pave.Z0 + pave.Z1) / 2 + Dz(s))));
            }
            var depth = 0.0;
            for (int k = 0; k < layers.Count; k++)
            {
                var t = layers[k].ThicknessCm / 100.0;
                var s = sAt[k + 1];
                rows.Add(($"{layers[k].Name} – e = {layers[k].ThicknessCm.ToString("0.#", Pt)} cm", P(s, sb - depth - t / 2 + Dz(s))));
                depth += t;
            }
            {
                var s = sAt[^1];
                var zg = Y(sb - depth + Dz(s));
                rows.Add(("Subleito compactado", new Vec2(X(s), (zg + groundY) / 2)));
            }
        }
        var rowH = ctx.Mm(nm * 1.45 + 1.6);
        var calloutTop = groundY - ctx.Mm(3.0);
        var xText = rows.Count > 0 ? rows.Max(r => r.Anchor.X) + ctx.Mm(7) : 0;
        var calloutsRight = double.MinValue;
        for (int k = 0; k < rows.Count; k++)
        {
            var (text, anchor) = rows[k];
            var top = new Vec2(xText, calloutTop - k * rowH);
            var mid = top.Y - ctx.Mm(nm * 0.55);
            geo.Annotations.Add(new AnnotationLine(new[] { anchor, new Vec2(anchor.X, mid), new Vec2(xText - ctx.Mm(1.0), mid) }, MarkingColor.Preta));
            Dot(geo, anchor, ctx);
            geo.Annotations.Add(new AnnotationText(top, text, nm, TextAlign.Left));
            var tr = TextRect(top, text, nm, TextAlign.Left, ctx);
            place.Texts.Add(tr);
            calloutsRight = Math.Max(calloutsRight, tr.X1);
        }
        var bottom = rows.Count > 0 ? calloutTop - rows.Count * rowH : groundY - ctx.Mm(2);

        // ---------------------------------------------------------------- título sublinhado com a escala
        var title = letter.Length > 0 ? $"SEÇÃO TRANSVERSAL {letter}–{letter}" : "SEÇÃO TRANSVERSAL";
        var where = saxes.Count > 0 ? saxes : SectionAxes(sd, ctx);
        var sub = string.Join(" · ", where.Select(x =>
        {
            var nmv = !string.IsNullOrWhiteSpace(x.Pav.Notes) ? x.Pav.Notes!.Trim() : Hierarquia.Label(x.Pav.Hierarchy);
            var est = (int)(x.Station / 20);
            return $"{nmv} – estaca {est}+{(x.Station - est * 20).ToString("0.00", Pt)} ({x.Station.ToString("0.00", Pt)} m)";
        }).Distinct());
        var scale = vex > 1.001 ? $"ESCALA H 1:{pd.ProfileScale:0} – V 1:{pd.ProfileScale / vex:0.#} (EXAGERO VERTICAL {vex:0.#}×) – COTAS EM METROS"
                                : $"ESCALA 1:{pd.ProfileScale:0} – COTAS E NÍVEIS EM METROS";
        var cx = X(L / 2);
        var ty = bottom - ctx.Mm(4);
        var bottomY = TitleBlock(geo, place, ctx, new Vec2(cx, ty), title, new[] { scale, sub }.Where(x => x.Length > 0).ToList(), tMm);

        // ---------------------------------------------------------------- legenda e detalhe do meio-fio e sarjeta (à direita)
        var rightX = new[] { X(L) + ext, calloutsRight, labelX.Count > 0 ? labelX.Zip(labels, (lx, l) => lx + l.W / 2).Max() : double.MinValue }.Max()
                     + ctx.Mm(10);
        var nextY = pos.Y - ctx.Mm(2);
        if (pd.ProfileLegend)
            nextY = ItemLegendAt(geo, ctx, stretches, styles, new Vec2(rightX, nextY), tMm) - ctx.Mm(8);
        if (spec.Detail && segs.Any(IsCurb))
            CurbDetail(geo, place, ctx, pd, spec, layers, segs, stretches, new Vec2(rightX, nextY), tMm);

        if (pd.FrameBox)
        {
            var pts = geo.Annotations.Skip(firstAnnotation).OfType<AnnotationLine>().SelectMany(l => l.Points)
                .Concat(geo.Pieces.Skip(firstPiece).SelectMany(p => p.Shape.Outer)).ToList();
            if (pts.Count > 0)
            {
                var pad = ctx.Mm(4);
                var mn = new Vec2(pts.Min(p => p.X) - pad, Math.Min(pts.Min(p => p.Y), bottomY) - pad);
                var mx = new Vec2(pts.Max(p => p.X) + pad, pos.Y);
                Frame(geo, mn, mx);
            }
        }
        if (vex > 1.001) geo.Warnings.Add($"Perfil com exagero vertical de {vex:0.#}× (as alturas estão ampliadas em relação às larguras).");
    }

    /// <summary>
    /// Textos de uma fila (âncora e largura) espalhados sem sobreposição, o mais perto possível das âncoras e na mesma ordem:
    /// grupos que se tocam são centrados na média das âncoras.
    /// </summary>
    private static List<double> PackRow(IReadOnlyList<(double Anchor, double W)> items, double gap)
    {
        var order = Enumerable.Range(0, items.Count).OrderBy(i => items[i].Anchor).ToList();
        var groups = new List<(List<int> Idx, double Left, double Width)>();
        foreach (var i in order)
        {
            var g = (Idx: new List<int> { i }, Left: items[i].Anchor - items[i].W / 2, Width: items[i].W);
            groups.Add(g);
            while (groups.Count > 1 && groups[^2].Left + groups[^2].Width + gap > groups[^1].Left)
            {
                var p = groups[^2];
                var q = groups[^1];
                var idx = p.Idx.Concat(q.Idx).ToList();
                var width = p.Width + gap + q.Width;
                // Centro do grupo = média das âncoras (deslocamento mínimo).
                var offs = new List<double>();
                var acc = 0.0;
                foreach (var j in idx) { offs.Add(acc + items[j].W / 2); acc += items[j].W + gap; }
                var left = idx.Select((j, n) => items[j].Anchor - offs[n]).Average();
                groups.RemoveRange(groups.Count - 2, 2);
                groups.Add((idx, left, width));
            }
        }
        var res = new double[items.Count];
        foreach (var (idx, left, _) in groups)
        {
            var x = left;
            foreach (var j in idx) { res[j] = x + items[j].W / 2; x += items[j].W + gap; }
        }
        return res.ToList();
    }

    /// <summary>
    /// Linha de cada texto da cadeia de cotas: na linha 0, centrado, o texto que cabe no seu vão; os outros vão para a linha de
    /// cima, espalhados sem sobreposição (com chamada até o vão). Devolve quantas linhas foram usadas.
    /// </summary>
    private static int ChainRows(List<(double C, double W, string Text, int Row, double At)> spans, BuildContext ctx, double tMm)
    {
        var up = new List<int>();
        for (int i = 0; i < spans.Count; i++)
        {
            var (c, w, text, _, _) = spans[i];
            if (TextWidth(text, tMm, ctx) + ctx.Mm(1.2) < w) spans[i] = (c, w, text, 0, c);
            else up.Add(i);
        }
        if (up.Count == 0) return 1;
        var xs = PackRow(up.Select(i => (spans[i].C, TextWidth(spans[i].Text, tMm, ctx))).ToList(), ctx.Mm(1.8));
        for (int k = 0; k < up.Count; k++)
        {
            var (c, w, text, _, _) = spans[up[k]];
            spans[up[k]] = (c, w, text, 1, xs[k]);
        }
        return 2;
    }

    /// <summary>Terminal de cota (traço a 45°, seta ou ponto) no ponto <paramref name="p"/> de uma linha de cota na direção <paramref name="u"/>.</summary>
    private static void Tick(MarkingGeometry geo, Vec2 p, Vec2 u, BuildContext ctx, TerminalCota terminal)
    {
        var n = u.PerpLeft;
        switch (terminal)
        {
            case TerminalCota.Ponto:
            {
                var r = ctx.Mm(0.45);
                geo.Pieces.Add(new MarkingPiece(new Polygon2(CurveTools.Circle(p, r, r * 0.05)), MarkingColor.Preta));
                break;
            }
            case TerminalCota.Seta:
            {
                var al = ctx.Mm(1.6);
                var aw = ctx.Mm(0.45);
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { p, p - u * al + n * aw, p - u * al - n * aw }), MarkingColor.Preta));
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { p, p + u * al + n * aw, p + u * al - n * aw }), MarkingColor.Preta));
                break;
            }
            default:
            {
                var t = (u + n).Normalized() * ctx.Mm(1.0);
                geo.Annotations.Add(new AnnotationLine(new[] { p - t, p + t }, MarkingColor.Preta));
                break;
            }
        }
    }

    /// <summary>Ponto cheio na ponta de uma linha de chamada.</summary>
    private static void Dot(MarkingGeometry geo, Vec2 p, BuildContext ctx)
    {
        var r = ctx.Mm(0.35);
        geo.Pieces.Add(new MarkingPiece(new Polygon2(CurveTools.Circle(p, r, r * 0.05)), MarkingColor.Preta));
    }

    /// <summary>Trechos de a→b fora dos textos já colocados (a linha é interrompida atrás de cada texto).</summary>
    private static List<(Vec2 A, Vec2 B)> OutsideTexts(Vec2 a, Vec2 b, Placer place)
    {
        var len = a.DistanceTo(b);
        if (len < 1e-9) return new();
        var cut = new List<(double T0, double T1)>();
        foreach (var r in place.Texts)
        {
            // Recorte do segmento pelo retângulo (Liang–Barsky).
            double t0 = 0, t1 = 1;
            var d = b - a;
            bool Clip(double p, double q)
            {
                if (Math.Abs(p) < 1e-12) return q >= 0;
                var t = q / p;
                if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
                return true;
            }
            if (Clip(-d.X, a.X - r.X0) && Clip(d.X, r.X1 - a.X) && Clip(-d.Y, a.Y - r.Y0) && Clip(d.Y, r.Y1 - a.Y) && t1 > t0) cut.Add((t0, t1));
        }
        var res = new List<(Vec2, Vec2)>();
        var from = 0.0;
        foreach (var (c0, c1) in cut.OrderBy(c => c.T0))
        {
            if (c0 > from + 1e-9) res.Add((a + (b - a) * from, a + (b - a) * c0));
            from = Math.Max(from, c1);
        }
        if (from < 1 - 1e-9) res.Add((a + (b - a) * from, b));
        return res;
    }

    /// <summary>A linha a→b não passa por nenhum texto já colocado.</summary>
    private static bool Clear(Vec2 a, Vec2 b, Placer place)
    {
        var parts = OutsideTexts(a, b, place);
        return parts.Count == 1 && parts[0].A.DistanceTo(a) < 1e-9 && parts[0].B.DistanceTo(b) < 1e-9;
    }

    private static void Broken(MarkingGeometry geo, Vec2 a, Vec2 b, Placer place, BuildContext ctx)
    {
        foreach (var (p, q) in OutsideTexts(a, b, place))
            if (p.DistanceTo(q) > ctx.Mm(0.3)) geo.Annotations.Add(new AnnotationLine(new[] { p, q }, MarkingColor.Preta));
    }

    private static void DashDotBroken(MarkingGeometry geo, Vec2 a, Vec2 b, Placer place, BuildContext ctx)
    {
        foreach (var (p, q) in OutsideTexts(a, b, place)) DashDot(geo, p, q, ctx);
    }

    /// <summary>Símbolo de eixo (C e L sobrepostos) centrado em <paramref name="c"/>.</summary>
    private static void CenterLineSymbol(MarkingGeometry geo, Vec2 c, double r)
    {
        var arc = new List<Vec2>();
        for (int i = 0; i <= 16; i++)
        {
            var ang = Math.PI / 4 + i * (1.5 * Math.PI) / 16;
            arc.Add(c + Vec2.FromAngle(ang) * r);
        }
        geo.Annotations.Add(new AnnotationLine(arc, MarkingColor.Preta));
        geo.Annotations.Add(new AnnotationLine(new[] { c + new Vec2(0, r * 1.25), c + new Vec2(0, -r * 1.25), c + new Vec2(r * 0.9, -r * 1.25) }, MarkingColor.Preta));
    }

    /// <summary>Linha de interrupção (zigue-zague no meio) de a até b.</summary>
    private static List<Vec2> BreakLine(Vec2 a, Vec2 b, BuildContext ctx)
    {
        var m = (a + b) / 2;
        var u = (b - a).Normalized();
        var n = u.PerpLeft;
        var z = ctx.Mm(1.2);
        return new List<Vec2> { a, m - u * z, m - u * z * 0.4 + n * z, m + u * z * 0.4 - n * z, m + u * z, b };
    }

    /// <summary>Título sublinhado e as linhas de baixo (escala, local), centrados em <paramref name="top"/>; devolve o pé do bloco.</summary>
    private static double TitleBlock(MarkingGeometry geo, Placer place, BuildContext ctx, Vec2 top, string title, IReadOnlyList<string> lines, double tMm)
    {
        var titleMm = tMm * 1.5;
        geo.Annotations.Add(new AnnotationText(top, title, titleMm));
        place.Texts.Add(TextRect(top, title, titleMm, TextAlign.Center, ctx));
        var tw = TextWidth(title, titleMm, ctx);
        // Sublinhado abaixo dos descendentes (Ç, vírgulas) – não corta o texto.
        var uy = top.Y - ctx.Mm(titleMm * 1.3 + 0.8);
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(top.X - tw / 2, uy), new Vec2(top.X + tw / 2, uy) }, MarkingColor.Preta));
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(top.X - tw / 2, uy - ctx.Mm(0.5)), new Vec2(top.X + tw / 2, uy - ctx.Mm(0.5)) }, MarkingColor.Preta));
        var y = uy - ctx.Mm(1.6);
        var sm = tMm * 0.75;
        foreach (var l in lines)
        {
            var p = new Vec2(top.X, y);
            geo.Annotations.Add(new AnnotationText(p, l, sm));
            place.Texts.Add(TextRect(p, l, sm, TextAlign.Center, ctx));
            y -= ctx.Mm(sm * 1.6);
        }
        return y;
    }

    /// <summary>Tabela "ELEMENTOS DA SEÇÃO" com o canto superior esquerdo em <paramref name="at"/>; devolve o pé da tabela.</summary>
    private static double ItemLegendAt(MarkingGeometry geo, BuildContext ctx, IReadOnlyList<Stretch> stretches, IReadOnlyList<SectionItemStyle>? styles,
        Vec2 at, double tMm)
    {
        var before = geo.Annotations.Count;
        ItemLegend(geo, ctx, stretches, styles, Array.Empty<ProfileSegment>(), at, tMm);
        var ys = geo.Annotations.Skip(before).OfType<AnnotationLine>().SelectMany(l => l.Points).Select(p => p.Y).ToList();
        return ys.Count > 0 ? ys.Min() : at.Y;
    }

    /// <summary>
    /// Detalhe ampliado do meio-fio e da sarjeta (referência "meio-fio com sarjeta tipo B"): forma real com o espelho inclinado e a
    /// parte enterrada, sarjeta com o caimento para o meio-fio, pavimento com o greide e a base, passeio ou terreno apiloado atrás;
    /// cotas em cm, inclinações, notas e título sublinhado com a escala. Larguras e altura aparente vêm do corte.
    /// </summary>
    private static void CurbDetail(MarkingGeometry geo, Placer place, BuildContext ctx, SectionProfileDefinition pd, CurbGutterSpec spec,
        IReadOnlyList<SectionLayer> layers, IReadOnlyList<ProfileSegment> segs, IReadOnlyList<Stretch> stretches, Vec2 at, double tMm)
    {
        var curb = segs.First(x => !x.Paint && x.Def is LinearMarkingDefinition l && l.Code.StartsWith("MEIO-FIO", StringComparison.OrdinalIgnoreCase));
        var gutter = stretches.FirstOrDefault(t => t.Key == "Sarjeta");
        var gutterSeg = gutter?.Top;
        var pave = segs.FirstOrDefault(x => !x.Paint && MarkingColors.IsPavement(x.Color));
        var cw = Math.Max(0.05, curb.S1 - curb.S0);                                  // largura da base do meio-fio
        var gw = gutter != null ? gutter.S1 - gutter.S0 : 0;                          // largura da sarjeta
        var gt = gutterSeg != null ? Math.Max(0.03, gutterSeg.Z1 - gutterSeg.Z0) : 0; // espessura da sarjeta
        var gTop = gutterSeg?.Z1 ?? pave?.Z1 ?? curb.Z0;
        var ch = Math.Max(0.02, curb.Z1 - gTop);                                     // altura aparente
        var cb = Math.Max(0, spec.BuriedCm) / 100.0;                                 // altura enterrada
        var bat = Math.Min(cw * 0.5, ch * spec.FaceSlopePct / 100.0);
        var gs = spec.GutterSlopePct / 100.0;
        var tr = pave != null ? Math.Max(0.02, pave.Z1 - pave.Z0) : 0.05;            // revestimento
        var cross = pd.CrossSlopePct / 100.0;
        const double pw = 0.40;                                                      // pavimento mostrado
        const double bw = 0.22;                                                      // passeio/terreno mostrado
        var zbot = -cb - 0.05;
        var topL = gw * gs + pw * cross;

        var md = ctx.ViewScale / Math.Max(1, spec.DetailScale);
        var sm = tMm * 0.75;
        var nm = tMm * 0.8;
        var notesH = ctx.Mm(3 * (nm * 1.45 + 2.2) + 4);
        var xMin = -gw - pw;
        Vec2 D(double x, double z) => new(at.X + ctx.Mm(4) + (x - xMin) * md, at.Y - notesH - (Math.Max(ch, topL) - z) * md);

        void Region(IEnumerable<(double X, double Z)> pts, MarkingColor color, string? rgb, Hachura hatch, bool outline = true)
        {
            var ring = pts.Select(p => D(p.X, p.Z)).ToList();
            geo.Pieces.Add(new MarkingPiece(new Polygon2(ring), color) { Rgb = rgb, Hatch = hatch });
            if (outline) geo.Annotations.Add(new AnnotationLine(ring.Append(ring[0]).ToList(), MarkingColor.Preta));
        }

        // Base (primeira camada) sob o pavimento, a sarjeta e o meio-fio; terreno apiloado atrás do meio-fio.
        var baseLayer = layers.FirstOrDefault();
        Region(new[] { (xMin, zbot), (cw, zbot), (cw, -cb), (0.0, -cb), (0.0, gw > 0 ? -gt : -tr), (-gw, gw > 0 ? -gt : -tr), (-gw, gw * gs - tr), (xMin, topL - tr) },
            MarkingColor.Brita, LayerTones[0], baseLayer?.Hatch ?? Hachura.Brita);
        Region(new[] { (cw, zbot), (cw + bw, zbot), (cw + bw, ch), (cw, ch) }, MarkingColor.Terra, GroundTone, Hachura.Terra);
        // Revestimento até a sarjeta (greide do pavimento).
        Region(new[] { (xMin, topL - tr), (-gw, gw * gs - tr), (-gw, gw * gs), (xMin, topL) }, pave?.Color ?? MarkingColor.Asfalto, null,
            pave != null ? DefaultHatch(pave.Color, pave.Def) : Hachura.Pontos);
        // Sarjeta com caimento para o meio-fio e meio-fio com o espelho inclinado.
        if (gw > 0)
            Region(new[] { (-gw, -gt), (0.0, -gt), (0.0, 0.0), (-gw, gw * gs) }, MarkingColor.Concreto, null, Hachura.DiagonalDensa);
        Region(new[] { (0.0, -cb), (cw, -cb), (cw, ch), (bat, ch), (0.0, 0.0) }, MarkingColor.Concreto, null, Hachura.DiagonalDensa);
        // Interrupções nas pontas.
        geo.Annotations.Add(new AnnotationLine(BreakLine(D(xMin, topL + 0.03), D(xMin, zbot - 0.03), ctx), MarkingColor.Preta));
        geo.Annotations.Add(new AnnotationLine(BreakLine(D(cw + bw, ch + 0.03), D(cw + bw, zbot - 0.03), ctx), MarkingColor.Preta));
        foreach (var (x0, z0, x1, z1) in new[] { (xMin, topL, cw + bw, ch), (xMin, zbot, cw + bw, zbot) })
            place.Marks.Add((D(x0, z0).X, Math.Min(D(x0, z0).Y, D(x1, z1).Y), D(x1, z1).X, Math.Max(D(x0, z0).Y, D(x1, z1).Y)));
        place.Marks.Add((D(xMin, 0).X, D(0, zbot).Y, D(cw + bw, 0).X, D(0, Math.Max(ch, topL)).Y));

        // Cotas em cm arredondadas a 0,5 cm (12,75 → 13).
        string Cm(double m) => (Math.Round(m * 200, MidpointRounding.AwayFromZero) / 2).ToString("0.#", Pt);

        // Cotas em cm: topo do meio-fio, larguras (sarjeta e base) embaixo, alturas (enterrada, aparente e total) à direita.
        void HDim(double xa, double xb, double z, double offMm, string text)
        {
            var pa = D(xa, z);
            var pb = D(xb, z);
            var y = pa.Y + ctx.Mm(offMm);
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(pa.X, pa.Y + Math.Sign(offMm) * ctx.Mm(0.8)), new Vec2(pa.X, y + Math.Sign(offMm) * ctx.Mm(1.0)) }, MarkingColor.Preta));
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(pb.X, pb.Y + Math.Sign(offMm) * ctx.Mm(0.8)), new Vec2(pb.X, y + Math.Sign(offMm) * ctx.Mm(1.0)) }, MarkingColor.Preta));
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(pa.X - ctx.Mm(1.2), y), new Vec2(pb.X + ctx.Mm(1.2), y) }, MarkingColor.Preta));
            Tick(geo, new Vec2(pa.X, y), new Vec2(1, 0), ctx, pd.Terminal);
            Tick(geo, new Vec2(pb.X, y), new Vec2(1, 0), ctx, pd.Terminal);
            var top = new Vec2((pa.X + pb.X) / 2, y + ctx.Mm(0.6 + tMm * 1.25));
            if (TextWidth(text, tMm, ctx) + ctx.Mm(1) > pb.X - pa.X)
            {
                // Vão curto: texto ao lado, por fora.
                top = new Vec2(pb.X + ctx.Mm(1.6) + TextWidth(text, tMm, ctx) / 2, top.Y);
            }
            geo.Annotations.Add(new AnnotationText(top, text, tMm));
            place.Texts.Add(TextRect(top, text, tMm, TextAlign.Center, ctx));
        }
        // Cota vertical à direita de x (as linhas de chamada saem da face posterior do meio-fio, em xFrom).
        void VDim(double xFrom, double x, double za, double zb, double offMm, string text, bool lines = true)
        {
            var pa = D(x, za);
            var pb = D(x, zb);
            var xd = pa.X + ctx.Mm(offMm);
            if (lines)
                foreach (var z in new[] { za, zb })
                    geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(D(xFrom, z).X + ctx.Mm(0.8), D(xFrom, z).Y), new Vec2(xd + ctx.Mm(1.0), D(xFrom, z).Y) }, MarkingColor.Preta));
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(xd, pa.Y - ctx.Mm(1.2)), new Vec2(xd, pb.Y + ctx.Mm(1.2)) }, MarkingColor.Preta));
            Tick(geo, new Vec2(xd, pa.Y), new Vec2(0, 1), ctx, pd.Terminal);
            Tick(geo, new Vec2(xd, pb.Y), new Vec2(0, 1), ctx, pd.Terminal);
            var top = new Vec2(xd + ctx.Mm(1.0), (pa.Y + pb.Y) / 2 + ctx.Mm(tMm * 0.65));
            geo.Annotations.Add(new AnnotationText(top, text, tMm, TextAlign.Left));
            place.Texts.Add(TextRect(top, text, tMm, TextAlign.Left, ctx));
        }
        HDim(bat, cw, ch, 4.0, Cm(cw - bat));
        if (gw > 0) HDim(-gw, 0, zbot, -4.0 - tMm * 1.6, Cm(gw));
        HDim(0, cw, zbot, -4.0 - tMm * 1.6, Cm(cw));
        var xr = cw + bw;
        VDim(cw, xr, -cb, 0, 3.0, Cm(cb));
        VDim(cw, xr, 0, ch, 3.0, Cm(ch), lines: false);
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(D(cw + bw, ch).X + ctx.Mm(0.8), D(cw, ch).Y), new Vec2(D(xr, ch).X + ctx.Mm(3.0 + tMm * 3.2 + 1.0), D(xr, ch).Y) }, MarkingColor.Preta));
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(D(xr, -cb).X + ctx.Mm(4.0), D(xr, -cb).Y), new Vec2(D(xr, -cb).X + ctx.Mm(3.0 + tMm * 3.2 + 1.0), D(xr, -cb).Y) }, MarkingColor.Preta));
        VDim(cw, xr, -cb, ch, 3.0 + tMm * 3.2, Cm(cb + ch), lines: false);

        // Inclinações: caimento da sarjeta (seta para o meio-fio) e espelho do meio-fio.
        if (gw > 0)
        {
            var pa = D(-gw * 0.85, gw * 0.85 * gs) + new Vec2(0, ctx.Mm(1.2));
            var pb = D(-gw * 0.15, gw * 0.15 * gs) + new Vec2(0, ctx.Mm(1.2));
            geo.Annotations.Add(new AnnotationLine(new[] { pa, pb }, MarkingColor.Preta));
            var u = (pb - pa).Normalized();
            var ah = ctx.Mm(1.0);
            geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { pb, pb - u * ah * 1.6 + u.PerpLeft * ah * 0.5, pb - u * ah * 1.6 - u.PerpLeft * ah * 0.5 }), MarkingColor.Preta));
            var top = new Vec2((pa.X + pb.X) / 2, Math.Max(pa.Y, pb.Y) + ctx.Mm(0.6 + sm * 1.25));
            var text = $"{spec.GutterSlopePct.ToString("0.#", Pt)} %";
            geo.Annotations.Add(new AnnotationText(top, text, sm));
            place.Texts.Add(TextRect(top, text, sm, TextAlign.Center, ctx));
        }
        {
            // Espelho: chamada do meio da face para cima e para a esquerda.
            var face = D(bat / 2, ch / 2);
            var text = $"{spec.FaceSlopePct.ToString("0.#", Pt)} %";
            var top = new Vec2(face.X - ctx.Mm(2.5), D(0, ch).Y + ctx.Mm(sm * 1.25 + 1.4));
            geo.Annotations.Add(new AnnotationLine(new[] { face, new Vec2(face.X - ctx.Mm(2.5), top.Y - ctx.Mm(sm * 1.3 + 0.3)) }, MarkingColor.Preta));
            Dot(geo, face, ctx);
            geo.Annotations.Add(new AnnotationText(top, text, sm, TextAlign.Right));
            place.Texts.Add(TextRect(top, text, sm, TextAlign.Right, ctx));
        }

        // Notas com chamadas, em linhas acima do detalhe (cada uma onde não sobrepõe).
        var notes = new List<(string Text, Vec2 Target, bool Right)>
        {
            ("GREIDE DO PAVIMENTO", D(xMin + pw * 0.35, topL - pw * 0.35 * cross), false),
            ((spec.Concrete ?? "").Trim(), D(cw * 0.55, ch * 0.4), true),
            ("PASSEIO OU TERRENO APILOADO", D(cw + bw * 0.6, ch * 0.5), true),
        };
        if (baseLayer != null) notes.Add((baseLayer.Name.ToUpperInvariant(), D(xMin + pw * 0.6, zbot + 0.03), false));
        var noteTop = at.Y - ctx.Mm(1);
        foreach (var (text, target, right) in notes.Where(n => n.Text.Length > 0))
        {
            var placed = false;
            if (!right)
            {
                // À esquerda do detalhe, na altura do alvo, com chamada horizontal.
                var top = new Vec2(D(xMin, 0).X - ctx.Mm(4), target.Y + ctx.Mm(nm * 0.6));
                var r = TextRect(top, text, nm, TextAlign.Right, ctx);
                var start = new Vec2(r.X1, target.Y);
                if (place.Free(r) && Clear(start, target, place))
                {
                    geo.Annotations.Add(new AnnotationText(top, text, nm, TextAlign.Right));
                    place.Texts.Add(r);
                    geo.Annotations.Add(new AnnotationLine(new[] { start, target }, MarkingColor.Preta));
                    Dot(geo, target, ctx);
                    placed = true;
                }
            }
            for (int k = 0; k < 8 && !placed; k++)
            {
                var top = new Vec2(right ? target.X + ctx.Mm(5) : target.X - ctx.Mm(5), noteTop - k * ctx.Mm(nm * 1.45 + 2.2));
                if (target.Y > top.Y) continue;
                var al = right ? TextAlign.Left : TextAlign.Right;
                var r = TextRect(top, text, nm, al, ctx);
                var elbow = new Vec2(top.X + (right ? -ctx.Mm(1) : ctx.Mm(1)), top.Y - ctx.Mm(nm * 0.6));
                var start = new Vec2(right ? r.X0 : r.X1, elbow.Y);
                if (!place.Free(r) || !Clear(elbow, target, place) || !Clear(start, elbow, place)) continue;
                geo.Annotations.Add(new AnnotationText(top, text, nm, al));
                place.Texts.Add(r);
                geo.Annotations.Add(new AnnotationLine(new[] { start, elbow, target }, MarkingColor.Preta));
                Dot(geo, target, ctx);
                placed = true;
            }
        }

        // Título sublinhado com a escala.
        var bottom = D(0, zbot).Y - ctx.Mm(4.0 + tMm * 1.6 + tMm * 1.6 + 4);
        var cx = (D(xMin, 0).X + D(xr, 0).X) / 2;
        TitleBlock(geo, place, ctx, new Vec2(cx, bottom), "DETALHE – MEIO-FIO E SARJETA",
            new[] { $"ESCALA 1:{spec.DetailScale:0} – COTAS EM CENTÍMETROS" }, tMm);
    }
}
