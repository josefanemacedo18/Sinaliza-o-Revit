using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Perfil transversal (corte) desenhado a partir do modelo cortado pela linha de seção.</summary>
public static partial class DetailGenerator
{
    /// <summary>Trecho de uma camada cortada pela seção: estações (m ao longo do corte) e níveis (m, relativos ao topo da pista).</summary>
    public sealed record ProfileSegment(double S0, double S1, double Z0, double Z1, MarkingColor Color, MarkingDefinition Def, bool Paint);

    /// <summary>Camadas cortadas pela linha de seção a→b (pavimento, meios-fios, calçadas, canteiros, plataformas, pintura, dispositivos).</summary>
    public static List<ProfileSegment> ProfileSegments(Vec2 a, Vec2 b, IEnumerable<(MarkingDefinition Def, MarkingGeometry Geo)> items)
    {
        var L = a.DistanceTo(b);
        var raw = new List<ProfileSegment>();
        foreach (var (def, g) in items)
        {
            var lift = Math.Max(0, def.Output.ElevationOffset - 0.001);
            foreach (var p in g.Pieces)
            {
                if (p.Profile != null) continue;                        // placas e perfis verticais não entram no corte
                var (mn, mx) = p.Shape.Bounds;
                if (Math.Max(a.X, b.X) < mn.X || Math.Min(a.X, b.X) > mx.X || Math.Max(a.Y, b.Y) < mn.Y || Math.Min(a.Y, b.Y) > mx.Y) continue;
                double z0, z1;
                var paint = false;
                if (p.Solid != null) { z0 = p.Elevation + p.Solid.MinZ; z1 = p.Elevation + p.Solid.MaxZ; }
                else if (MarkingColors.IsPaint(p.Color) && p.Thickness < 0.005) { z0 = z1 = p.Elevation + lift; paint = true; }
                else { z0 = p.Elevation; z1 = p.Elevation + (p.Thickness > 0 ? p.Thickness : 0.02); }
                if (z0 > 0.5) continue;                                 // copas de árvores, placas e luminárias
                foreach (var (t0, t1) in SegmentIntervals(p.Shape, a, b))
                    if ((t1 - t0) * L > 0.005) raw.Add(new ProfileSegment(t0 * L, t1 * L, z0, z1, p.Color, def, paint));
            }
        }
        // Une trechos encostados da mesma camada (peças consecutivas de uma faixa).
        var res = new List<ProfileSegment>();
        foreach (var grp in raw.GroupBy(r => (r.Color, Z0: Math.Round(r.Z0, 3), Z1: Math.Round(r.Z1, 3), r.Paint)))
            foreach (var r in grp.OrderBy(r => r.S0))
            {
                var i = res.FindLastIndex(x => x.Color == r.Color && Math.Abs(x.Z0 - r.Z0) < 1e-3 && Math.Abs(x.Z1 - r.Z1) < 1e-3 && x.Paint == r.Paint);
                if (i >= 0 && r.S0 <= res[i].S1 + 0.005) res[i] = res[i] with { S1 = Math.Max(res[i].S1, r.S1) };
                else res.Add(r);
            }
        return res.OrderBy(r => r.S0).ToList();
    }

    /// <summary>Nível do topo da superfície (maior topo de camada sólida) na estação <paramref name="s"/>.</summary>
    public static double? SurfaceTop(IReadOnlyList<ProfileSegment> segs, double s) =>
        segs.Where(x => !x.Paint && x.S0 <= s + 1e-6 && x.S1 >= s - 1e-6 && x.Z1 < 0.5).Select(x => (double?)x.Z1).DefaultIfEmpty(null).Max();

    private static string Level(double z) =>
        Math.Abs(z) < 0.0005 ? "±0,00" : (z > 0 ? "+" : "−") + Math.Abs(z).ToString("0.00", Pt);

    private static string LayerName(ProfileSegment s) => s.Def switch
    {
        _ when MarkingColors.IsPavement(s.Color) => s.Color switch
        {
            MarkingColor.Bloquete => "Pavimento em blocos de concreto",
            MarkingColor.PavimentoConcreto => "Pavimento de concreto",
            MarkingColor.PavimentoTerra => "Pista de terra (leito natural / revestimento primário)",
            _ => "Revestimento asfáltico (CBUQ)",
        },
        LinearMarkingDefinition l => l.Code.ToUpperInvariant() switch
        {
            var c when c.StartsWith("MEIO-FIO") => "Meio-fio de concreto",
            "CALCADA" => "Passeio em concreto",
            "GRAMADO" => "Grama / vegetação sobre solo",
            "SARJETA" => "Sarjeta de concreto",
            "SARJETAO" => "Sarjetão de concreto",
            "PLATAFORMA" => "Plataforma elevada (laje de concreto)",
            _ => s.Paint ? "Pintura de demarcação viária" : "Elemento físico",
        },
        SidewalkAreaDefinition or CurbExtensionDefinition or CulDeSacDefinition => s.Color == MarkingColor.Grama ? "Grama / vegetação sobre solo" : "Passeio em concreto",
        PlanterDefinition => "Canteiro",
        RailwayDefinition => s.Color switch
        {
            MarkingColor.Brita => "Lastro de brita",
            MarkingColor.Terra => "Sublastro compactado",
            MarkingColor.Metal => "Trilho / fixação (aço)",
            MarkingColor.Madeira => "Dormente de madeira",
            MarkingColor.Concreto => "Concreto (dormente, laje ou valeta)",
            _ => "Via embutida – revestimento",
        },
        DeviceMarkingDefinition => "Dispositivo de segregação",
        RampDefinition => "Rampa de acessibilidade",
        TrafficCalmingDefinition => "Dispositivo de moderação",
        IntersectionDefinition or RoundaboutDefinition => s.Color == MarkingColor.Grama ? "Grama / vegetação sobre solo" : s.Color == MarkingColor.Concreto ? "Concreto" : "Pavimento",
        _ => s.Paint ? "Pintura de demarcação viária" : "Elemento físico",
    };

    /// <summary>Eixo de via cruzado pela seção: estação no corte, pavimento, seno do ângulo, lado esquerdo (+1/−1) e estaca no eixo.</summary>
    public sealed record SectionAxis(double S, RoadPavementDefinition Pav, double Sin, double LeftSign, double Station, Polyline2 Path);

    public static List<SectionAxis> SectionAxes(SectionDimensionDefinition sd, BuildContext ctx)
    {
        var a = sd.Start;
        var b = sd.End;
        var L = a.DistanceTo(b);
        var u = (b - a) / Math.Max(1e-9, L);
        var res = new List<SectionAxis>();
        foreach (var d in (ctx.AllDefinitions?.Invoke() ?? Array.Empty<MarkingDefinition>()).OfType<RoadPavementDefinition>())
        {
            if (ctx.PathOf?.Invoke(d) is not { } path) continue;
            foreach (var t in Crossings(path, a, b))
            {
                var p = a + (b - a) * t;
                var (st, _) = path.Project(p);
                var tan = path.TangentAt(st);
                var sin = Math.Max(0.2, Math.Abs(u.Cross(tan)));
                var left = u.Dot(tan.PerpLeft) >= 0 ? 1.0 : -1.0;
                var local = d.HasEdgeVariation ? d.Local(st, path) : d;
                if (!res.Any(x => Math.Abs(x.S - t * L) < 0.05)) res.Add(new SectionAxis(t * L, local, sin, left, st, path));
            }
        }
        return res.OrderBy(x => x.S).ToList();
    }

    /// <summary>
    /// Deslocamento vertical do desenho na estação <paramref name="s"/> do corte: pista em duas águas a partir do eixo (ou
    /// superelevação), e calçada subindo para o lote com a sua inclinação – a partir da cota do bordo.
    /// </summary>
    private static double SlopeDz(IReadOnlyList<SectionAxis> axes, double s, double crossPct, double walkPct)
    {
        if (axes.Count == 0) return 0;
        var ax = axes.OrderBy(x => Math.Abs(x.S - s)).First();
        var o = (s - ax.S) * ax.Sin * ax.LeftSign;               // afastamento perpendicular, + à esquerda do eixo
        var p = ax.Pav;
        var grade = p.Output.Grade;
        var cross = grade is { Crossfall: > 1e-6 } ? grade.Crossfall : crossPct / 100.0;
        var e = grade?.E(ax.Station) ?? 0;
        var w = o >= 0 ? p.LeftWidth : p.RightWidth;
        var total = o >= 0 ? p.TotalLeft : p.TotalRight;
        var ao = Math.Abs(o);
        double Road(double y) => Math.Abs(e) > 1e-9 ? e * y : -cross * Math.Abs(y);
        if (ao <= w) return Road(o);
        var edge = Road(Math.Sign(o) * w);
        if (ao > total + 0.5) return edge;
        // Calçada: sobe para o lote a partir da face do meio-fio (inclinação da seção guardada ou a informada).
        var slope = walkPct / 100.0;
        var setup = Automation.RoadTemplates.FromJson(p.SetupJson);
        var walkEl = setup == null ? null : (o >= 0 ? setup.Left : setup.Right).FirstOrDefault(x => x.Tipo == Automation.TipoElementoSecao.Calcada);
        if (walkEl != null && Math.Abs(walkEl.InclinacaoTransversal) > 1e-6) slope = walkEl.InclinacaoTransversal / 100.0;
        return edge + slope * Math.Max(0, ao - w - p.CurbWidth);
    }

    /// <summary>
    /// Perfil transversal desenhado em escala ampliada: camadas cortadas preenchidas e contornadas, nome e nível de cada trecho,
    /// caimento da pista, cotas horizontais (larguras) e verticais (desníveis), eixo, legenda dos materiais e título.
    /// </summary>
    private static void SectionProfile(MarkingGeometry geo, SectionDimensionDefinition sd, SectionProfileDefinition pd, BuildContext ctx,
        IReadOnlyList<(MarkingDefinition Def, MarkingGeometry Geo)> withGeo, List<double> planStations, List<double> axes, Vec2 pos, string letter)
    {
        var a = sd.Start;
        var b = sd.End;
        var L = a.DistanceTo(b);
        var firstAnnotation = geo.Annotations.Count;
        var firstPiece = geo.Pieces.Count;
        var segs = ProfileSegments(a, b, withGeo);
        if (segs.Count == 0) { geo.Warnings.Add("Perfil transversal: nada cortado pela linha de seção."); return; }
        // Caimento real: os trechos são quebrados no eixo e nos bordos e cada ponta recebe o desnível do caimento.
        var saxes = pd.DrawSlopes ? SectionAxes(sd, ctx) : new List<SectionAxis>();
        double Dz(double s) => pd.DrawSlopes ? SlopeDz(saxes, s, pd.CrossSlopePct, pd.SidewalkSlopePct) : 0;
        if (saxes.Count > 0)
        {
            var cuts = new List<double>();
            foreach (var x in saxes)
            {
                cuts.Add(x.S);
                foreach (var sg in new[] { 1.0, -1.0 })
                {
                    var side = sg * x.LeftSign >= 0;
                    var w = side ? x.Pav.LeftWidth : x.Pav.RightWidth;
                    cuts.Add(x.S + sg * w / x.Sin);
                    cuts.Add(x.S + sg * (w + x.Pav.CurbWidth) / x.Sin);
                }
            }
            var split = new List<ProfileSegment>();
            foreach (var sg in segs)
            {
                var pts = cuts.Where(c => c > sg.S0 + 0.01 && c < sg.S1 - 0.01).OrderBy(c => c).ToList();
                var s0 = sg.S0;
                foreach (var c in pts) { split.Add(sg with { S0 = s0, S1 = c }); s0 = c; }
                split.Add(sg with { S0 = s0 });
            }
            segs = split;
        }
        const double Eps = 0.004;

        var mag = ctx.ViewScale / Math.Max(1, pd.ProfileScale);
        var vex = Math.Clamp(pd.VerticalExaggeration, 1, 10);
        var vmag = mag * vex;
        var paintMin = ctx.Mm(0.35) / vmag;           // pintura: faixa fina visível no papel
        var zMax = segs.Max(x => (x.Paint ? x.Z1 + paintMin : x.Z1) + Math.Max(Dz(x.S0 + Eps), Dz(x.S1 - Eps)));
        var zMin = Math.Min(segs.Min(x => x.Z0 + Math.Min(Dz(x.S0 + Eps), Dz(x.S1 - Eps))), 0);
        var tMm = pd.TextMm;
        var fmt = pd.Decimals <= 0 ? "0" : "0." + new string('0', Math.Clamp(pd.Decimals, 0, 3));

        // Faixa superior de textos (nomes e níveis) e origem do desenho.
        var nameBand = ctx.Mm(tMm * 0.8 * 2.6 + 3);
        var levelBand = pd.ProfileLevels ? ctx.Mm(tMm * 0.75 * 1.6 + 2) : 0;
        var x0 = pos.X + ctx.Mm(6);
        var yTop = pos.Y - ctx.Mm(4) - nameBand - levelBand;
        double X(double s) => x0 + s * mag;
        double Y(double z) => yTop - (zMax - z) * vmag;
        Vec2 P(double s, double z) => new(X(s), Y(z));

        // Camadas: pavimento e sólidos primeiro, pintura por cima.
        foreach (var sg in segs.OrderBy(x => x.Paint).ThenBy(x => x.Z1 - x.Z0 > 0.3 ? 1 : 0))
        {
            var z1 = sg.Paint ? sg.Z1 + paintMin : sg.Z1;
            var d0 = Dz(sg.S0 + Eps);
            var d1 = Dz(sg.S1 - Eps);
            var quad = new[] { P(sg.S0, sg.Z0 + d0), P(sg.S1, sg.Z0 + d1), P(sg.S1, z1 + d1), P(sg.S0, z1 + d0) };
            geo.Pieces.Add(new MarkingPiece(new Polygon2(quad), sg.Color));
            if (!sg.Paint)
                geo.Annotations.Add(new AnnotationLine(quad.Append(quad[0]).ToList(), MarkingColor.Preta));
        }
        // Linha do terreno/subleito sob o corte, com pequenas marcas de solo.
        var ground = Y(zMin) - ctx.Mm(0.6);
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(X(0), ground), new Vec2(X(L), ground) }, MarkingColor.Preta));
        for (var gx = X(0); gx < X(L) - ctx.Mm(2); gx += ctx.Mm(3))
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(gx, ground), new Vec2(gx - ctx.Mm(1.2), ground - ctx.Mm(1.2)) }, MarkingColor.Preta));

        // Estações do perfil: as da cadeia em planta (eixos das linhas, bordas) e as bordas das camadas sólidas.
        var st = planStations.Concat(segs.Where(x => !x.Paint).SelectMany(x => new[] { x.S0, x.S1 })).Where(s => s >= -1e-6 && s <= L + 1e-6)
            .OrderBy(s => s).ToList();
        var stations = new List<double>();
        foreach (var s in st) if (stations.Count == 0 || s - stations[^1] > 0.02) stations.Add(s);

        // Trechos entre estações: nome, nível e caimento.
        var nameY = pos.Y - ctx.Mm(4);
        double? lastLevel = null;
        for (int i = 0; i + 1 < stations.Count; i++)
        {
            var s0 = stations[i];
            var s1 = stations[i + 1];
            var len = s1 - s0;
            var mid = (s0 + s1) / 2;
            var top0 = SurfaceTop(segs, mid);
            if (top0 == null) { lastLevel = null; continue; }
            double? top = top0.Value + Dz(mid);
            var w = len * mag;
            var name = SectionLabel(withGeo, a + (b - a) * (mid / L), len);
            if (name != null)
            {
                var nm = tMm * 0.8;
                var lines = TextWidth(name, nm, ctx) < w * 0.94 ? name
                    : name.Contains(' ') && name.Split(' ').Max(x => TextWidth(x, nm, ctx)) < w * 0.94 ? WrapToWidth(name, w * 0.94, nm, ctx) : null;
                if (lines != null && lines.Split('\n').Length <= 2)
                {
                    geo.Annotations.Add(new AnnotationText(new Vec2(X(mid), nameY), lines, nm));
                    // Chamada fina do nome até a superfície.
                    var lineTop = nameY - ctx.Mm(nm * 1.45 * lines.Split('\n').Length + 0.8);
                    var surf = Y(top.Value) + ctx.Mm(1.0) + levelBand;
                    if (lineTop - surf > ctx.Mm(1)) geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(X(mid), lineTop), new Vec2(X(mid), surf) }, MarkingColor.Preta));
                }
            }
            if (pd.ProfileLevels && (lastLevel == null || Math.Abs(lastLevel.Value - top.Value) > 0.005))
            {
                var lt = Level(top.Value);
                var lm = tMm * 0.75;
                if (TextWidth(lt, lm, ctx) < w * 0.95 || i == 0 || i + 2 == stations.Count)
                {
                    lastLevel = top;
                    // Triângulo de nível (▽) com o valor.
                    var tip = P(mid, top.Value) + new Vec2(0, ctx.Mm(0.4));
                    var th = ctx.Mm(1.2);
                    geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { tip, tip + new Vec2(-th * 0.6, th), tip + new Vec2(th * 0.6, th) }), MarkingColor.Preta));
                    geo.Annotations.Add(new AnnotationText(tip + new Vec2(0, th + ctx.Mm(lm + 0.9)), lt, lm));
                }
            }
            // Caimento transversal (pista e calçada): seta para o lado mais baixo com a inclinação real do trecho.
            var slopeHere = len > 0.05 ? (Dz(s1 - Eps) - Dz(s0 + Eps)) / len : 0;
            var isRoad = name is "Faixa de rolamento" or "Pista" or "Faixa de ônibus" or "Ciclofaixa" or "Estacionamento";
            var isWalk = name is "Calçada";
            var pct = Math.Abs(slopeHere) * 100;
            if (pd.CrossSlopePct > 0 && (isRoad || isWalk) && w > ctx.Mm(isWalk ? 10 : 14) && (pct > 0.05 || !pd.DrawSlopes))
            {
                double dir;
                if (pd.DrawSlopes && pct > 0.05) dir = slopeHere < 0 ? 1.0 : -1.0;
                else
                {
                    var ax = axes.Count > 0 ? axes.OrderBy(x => Math.Abs(x - mid)).First() : L / 2;
                    dir = mid >= ax ? 1.0 : -1.0;
                    pct = isWalk ? pd.SidewalkSlopePct : pd.CrossSlopePct;
                }
                var y = Y(top.Value) + ctx.Mm(tMm * 0.75 + 2.2);
                var half = Math.Min(w * 0.3, ctx.Mm(9));
                var from = new Vec2(X(mid) - dir * half, y);
                var to = new Vec2(X(mid) + dir * half, y);
                geo.Annotations.Add(new AnnotationLine(new[] { from, to }, MarkingColor.Preta));
                var ah = ctx.Mm(1.1);
                geo.Pieces.Add(new MarkingPiece(new Polygon2(new[] { to, to + new Vec2(-dir * ah * 1.6, ah * 0.5), to + new Vec2(-dir * ah * 1.6, -ah * 0.5) }), MarkingColor.Preta));
                geo.Annotations.Add(new AnnotationText(new Vec2(X(mid), y + ctx.Mm(tMm * 0.75 + 0.6)), $"i = {pct.ToString("0.0", Pt)} %", tMm * 0.75));
            }
        }

        // Cotas verticais dos desníveis (degraus de meio-fio, plataformas, canteiros).
        if (pd.ProfileHeights)
            for (int i = 1; i + 1 < stations.Count; i++)
            {
                var s = stations[i];
                var left = SurfaceTop(segs, (stations[i - 1] + s) / 2) + Dz(s - Eps);
                var right = SurfaceTop(segs, (s + stations[i + 1]) / 2) + Dz(s + Eps);
                if (left == null || right == null || Math.Abs(left.Value - right.Value) < 0.03) continue;
                var low = Math.Min(left.Value, right.Value);
                var high = Math.Max(left.Value, right.Value);
                var towardLow = left.Value < right.Value ? -1.0 : 1.0;   // a cota fica sobre o lado mais baixo
                Dimension(geo, ctx, P(s, low), P(s, high), new Vec2(towardLow, 0), ctx.Mm(3.5), (high - low).ToString("0.00", Pt), tMm * 0.8,
                    terminal: pd.Terminal);
            }

        // Cotas horizontais (larguras) sob o perfil e total.
        var dimY = ground - ctx.Mm(1);
        var off = ctx.Mm(5);
        var stagger = false;
        for (int i = 0; i + 1 < planStations.Count; i++)
        {
            var len = planStations[i + 1] - planStations[i];
            var text = len.ToString(fmt, Pt);
            var fits = TextWidth(text, tMm, ctx) < len * mag * 0.9;
            stagger = !fits && !stagger;
            Dimension(geo, ctx, new Vec2(X(planStations[i]), dimY), new Vec2(X(planStations[i + 1]), dimY), new Vec2(0, -1), off, text, tMm,
                fits || !stagger ? 0 : tMm * 1.4, terminal: pd.Terminal);
        }
        var totalOff = off + ctx.Mm(tMm * 3.2 + 2);
        if (planStations.Count > 2)
            Dimension(geo, ctx, new Vec2(X(planStations[0]), dimY), new Vec2(X(planStations[^1]), dimY), new Vec2(0, -1), totalOff,
                (planStations[^1] - planStations[0]).ToString(fmt, Pt), tMm, terminal: pd.Terminal);
        var bottom = dimY - totalOff - ctx.Mm(tMm + 3);

        // Eixo da via (traço-ponto) atravessando o perfil.
        foreach (var s0 in axes)
        {
            DashDot(geo, new Vec2(X(s0), dimY - off - ctx.Mm(1)), new Vec2(X(s0), nameY + ctx.Mm(1)), ctx);
            geo.Annotations.Add(new AnnotationText(new Vec2(X(s0), nameY + ctx.Mm(tMm * 0.85 + 2)), "EIXO", tMm * 0.85));
        }

        // Título e escala.
        var title = letter.Length > 0 ? $"SEÇÃO TRANSVERSAL {letter}–{letter}" : "SEÇÃO TRANSVERSAL";
        var where = saxes.Count > 0 ? saxes : SectionAxes(sd, ctx);
        var sub = string.Join(" · ", where.Select(x =>
        {
            var nm = !string.IsNullOrWhiteSpace(x.Pav.Notes) ? x.Pav.Notes!.Trim() : Hierarquia.Label(x.Pav.Hierarchy);
            var est = (int)(x.Station / 20);
            return $"{nm} – estaca {est}+{(x.Station - est * 20).ToString("0.00", Pt)} ({x.Station.ToString("0.00", Pt)} m)";
        }).Distinct());
        var scale = vex > 1.001 ? $"ESCALA H 1:{pd.ProfileScale:0} – V 1:{pd.ProfileScale / vex:0.#} (EXAGERO VERTICAL {vex:0.#}×) – COTAS EM METROS"
                                : $"ESCALA 1:{pd.ProfileScale:0} – COTAS E NÍVEIS EM METROS";
        var titleMm = tMm * 1.5;
        var ty = bottom - ctx.Mm(3);
        geo.Annotations.Add(new AnnotationText(new Vec2(X(L / 2), ty), title, titleMm));
        var tw = TextWidth(title, titleMm, ctx);
        var uy = ty - ctx.Mm(titleMm * 1.15);
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(X(L / 2) - tw / 2, uy), new Vec2(X(L / 2) + tw / 2, uy) }, MarkingColor.Preta));
        geo.Annotations.Add(new AnnotationText(new Vec2(X(L / 2), uy - ctx.Mm(1.2)), scale, tMm * 0.75));
        if (sub.Length > 0) geo.Annotations.Add(new AnnotationText(new Vec2(X(L / 2), uy - ctx.Mm(1.2 + tMm * 0.75 * 1.6)), sub, tMm * 0.75));

        // Legenda dos materiais cortados (amostra + nome + espessura).
        if (pd.ProfileLegend)
        {
            var layers = segs.Where(x => !x.Paint || segs.All(y => y.Paint)).GroupBy(LayerName)
                .Select(g => (Name: g.Key, g.First().Color, Thick: g.Max(x => x.Z1 - x.Z0))).ToList();
            if (segs.Any(x => x.Paint)) layers.Add(("Pintura de demarcação viária", segs.First(x => x.Paint).Color, 0));
            var lx = X(L) + ctx.Mm(12);
            var ly = yTop + ctx.Mm(2);
            var lm = tMm * 0.8;
            geo.Annotations.Add(new AnnotationText(new Vec2(lx, ly), "MATERIAIS", tMm, TextAlign.Left));
            ly -= ctx.Mm(tMm * 1.9);
            foreach (var (nm, color, thick) in layers)
            {
                var sw = Polygon2.Rectangle(new Vec2(lx, ly - ctx.Mm(3)), new Vec2(lx + ctx.Mm(6), ly));
                geo.Pieces.Add(new MarkingPiece(sw, color));
                geo.Annotations.Add(new AnnotationLine(sw.Outer.Append(sw.Outer[0]).ToList(), MarkingColor.Preta));
                var label = thick > 0.004 && !MarkingColors.IsPaint(color) ? $"{nm} (e = {(thick * 100).ToString("0.#", Pt)} cm)" : nm;
                geo.Annotations.Add(new AnnotationText(new Vec2(lx + ctx.Mm(8), ly - ctx.Mm(0.2)), label, lm, TextAlign.Left));
                ly -= ctx.Mm(4.4);
            }
        }
        if (pd.FrameBox)
        {
            var pts = geo.Annotations.Skip(firstAnnotation).OfType<AnnotationLine>().SelectMany(l => l.Points)
                .Concat(geo.Pieces.Skip(firstPiece).SelectMany(p => p.Shape.Outer)).ToList();
            if (pts.Count > 0)
            {
                var pad = ctx.Mm(4);
                var mn = new Vec2(pts.Min(p => p.X) - pad, pts.Min(p => p.Y) - pad - ctx.Mm(tMm * 2));
                var mx = new Vec2(pts.Max(p => p.X) + pad, pos.Y);
                Frame(geo, mn, mx);
            }
        }
        if (vex > 1.001) geo.Warnings.Add($"Perfil com exagero vertical de {vex:0.#}× (as alturas estão ampliadas em relação às larguras).");
    }
}
