using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Geometria de um ramo da rotatória (sistema local: t ao longo do ramo a partir do centro, o à esquerda).</summary>
public sealed record RoundaboutLegGeometry(RoundaboutLeg Leg, Vec2 Dir, Vec2 Left, Polygon2? Splitter)
{
    /// <summary>Ilha separadora pintada (zebrado com linha de canalização) – quando o ramo não tem ilha física.</summary>
    public Polygon2? PaintedSplitter { get; init; }
    public Vec2 At(Vec2 center, double t, double o) => center + Dir * t + Left * o;
}

public sealed class RoundaboutLayout
{
    public List<Polygon2> Pavement { get; } = new();
    public List<Polygon2> Apron { get; } = new();
    public List<Polygon2> IslandCurb { get; } = new();
    public List<Polygon2> IslandCore { get; } = new();
    public List<Polygon2> Curb { get; } = new();
    public List<Polygon2> Sidewalk { get; } = new();
    public List<Polygon2> SplitterCurb { get; } = new();
    public List<Polygon2> SplitterCore { get; } = new();
    /// <summary>Divisores físicos entre as faixas (turbo-rotatória).</summary>
    public List<Polygon2> Dividers { get; } = new();
    public List<RoundaboutLegGeometry> Legs { get; } = new();
    /// <summary>Área onde os elementos físicos das vias (pavimento, calçadas, meios-fios) são recortados.</summary>
    public Polygon2 Zone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Área onde a pintura das vias é interrompida (o anel, nos modos que não remodelam as entradas).</summary>
    public Polygon2 PaintZone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Borda externa da faixa galgável (= ilha quando não há faixa galgável).</summary>
    public Polygon2 ApronOuter { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Ilha central pintada (minirrotatória MIR) – sem obra civil.</summary>
    public bool PaintedIsland { get; set; }
    /// <summary>Contorno da ilha central (círculo ou elipse).</summary>
    public Polygon2 Island { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Borda externa do anel (pista giratória).</summary>
    public Polygon2 Outer { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    public double LegLength { get; set; }
    public List<string> Warnings { get; } = new();

    /// <summary>Distância, a partir de <paramref name="origin"/> na direção <paramref name="dir"/>, até a borda externa do anel.</summary>
    public double ToOuter(Vec2 origin, Vec2 dir) => RoundaboutGenerator.Ray(Outer, origin, dir);
}

/// <summary>
/// Rotatórias (minirrotatória, compacta, uma ou duas faixas, turbo, oval e com by-pass): ilha central circular ou
/// elíptica, faixa galgável, pista giratória, ramos com raios de entrada e saída distintos, ilhas separadoras e
/// sinalização.
/// </summary>
public static class RoundaboutGenerator
{
    private static Polygon2 Disk(Vec2 c, double r) => new(CurveTools.Circle(c, r, Math.Max(0.005, r * 0.0012)));
    private static Vec2 Dir(double deg) => new(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180));

    private static List<Polygon2> Close(IEnumerable<Polygon2> p, double r) =>
        r > 0.05 ? PolygonOps.Offset(PolygonOps.Offset(p, r, true), -r, true) : p.ToList();

    /// <summary>Elipse (ou círculo) com semieixos a (na direção <paramref name="angleDeg"/>) e b.</summary>
    public static Polygon2 Ellipse(Vec2 c, double a, double b, double angleDeg)
    {
        var u = Dir(angleDeg);
        var v = u.PerpLeft;
        var n = Math.Clamp((int)(Math.Max(a, b) * 8), 48, 256);
        return new Polygon2(Enumerable.Range(0, n).Select(i =>
        {
            var t = 2 * Math.PI * i / n;
            return c + u * (a * Math.Cos(t)) + v * (b * Math.Sin(t));
        }));
    }

    /// <summary>Primeira interseção do raio com o contorno (distância; 0 se não houver).</summary>
    public static double Ray(Polygon2 poly, Vec2 origin, Vec2 dir)
    {
        double best = double.MaxValue;
        var r = poly.Outer;
        for (int i = 0; i < r.Count; i++)
        {
            var p = r[i];
            var e = r[(i + 1) % r.Count] - p;
            var den = dir.Cross(e);
            if (Math.Abs(den) < 1e-12) continue;
            var t = (p - origin).Cross(e) / den;
            var s = (p - origin).Cross(dir) / den;
            if (t > 1e-6 && s >= -1e-9 && s <= 1 + 1e-9 && t < best) best = t;
        }
        return best == double.MaxValue ? 0 : best;
    }

    /// <summary>Setor angular (a → b, anti-horário) como polígono de raio R a partir de c.</summary>
    private static Polygon2 Sector(Vec2 c, double a, double b, double R)
    {
        var span = b - a;
        while (span <= 0) span += 360;
        var n = Math.Max(2, (int)Math.Ceiling(span / 5));
        var pts = new List<Vec2> { c };
        for (int i = 0; i <= n; i++) pts.Add(c + Dir(a + span * i / n) * R);
        return new Polygon2(pts);
    }

    private static Polygon2 Grow(Polygon2 poly, double r) =>
        Math.Abs(r) < 1e-6 ? poly : PolygonOps.Offset(new[] { poly }, r, true).OrderByDescending(p => p.Area).FirstOrDefault() ?? poly;

    private static double Norm(double deg) => ((deg % 360) + 360) % 360;

    public static RoundaboutLayout Layout(RoundaboutDefinition d)
    {
        var L = new RoundaboutLayout();
        var c = d.Center;
        var ri = Math.Max(1, d.IslandRadius);
        var k = Math.Clamp(d.Elongation, 1, 4);
        var cw = Math.Max(0.05, d.CurbWidth);
        var painted = d.IslandType == TipoIlhaCentral.Pintada;
        L.PaintedIsland = painted;
        var apronW = painted ? 0 : Math.Max(0, d.ApronWidth);
        var ring = apronW + Math.Max(1, d.Lanes) * d.LaneWidth;
        var island = k > 1.001 ? Ellipse(c, ri * k, ri, d.OvalAngleDeg) : Disk(c, ri);
        L.Island = island;
        var apronOuter = apronW > 0.01 ? Grow(island, apronW) : island;
        L.ApronOuter = apronOuter;
        var outer = Grow(island, ring);
        L.Outer = outer;
        var complete = d.Integration == IntegracaoRotatoria.Completa;
        L.LegLength = complete
            ? (d.SplitterIslands ? d.SplitterLength : 0) + 12 + (d.Bypass ? Math.Max(d.BypassRadius, Math.Max(d.EntryRadius, d.ExitRadius ?? d.EntryRadius) + 16) * 1.6 : 0)
            : 0;
        // Recortes nas vias: completa = toda a zona remodelada; anel = só a pista giratória; ilha = só a ilha (pintura no anel).
        L.PaintZone = Grow(outer, 0.05);
        L.Zone = d.Integration switch
        {
            IntegracaoRotatoria.Completa => Grow(outer, L.LegLength),
            IntegracaoRotatoria.Anel => L.PaintZone,
            _ => Grow(apronOuter, 0.05),
        };
        var zone = new[] { L.Zone };
        var zoneR = Grow(outer, Math.Max(L.LegLength, 5)).Outer.Max(v => v.DistanceTo(c));
        if (d.Legs.Count == 0) L.Warnings.Add("Rotatória sem ramos: informe os ângulos dos ramos ou posicione-a sobre o cruzamento de vias.");
        if (d.Lanes * d.LaneWidth < 4.0) L.Warnings.Add("Pista giratória com menos de 4 m – confira a largura para os veículos de projeto.");
        var inscribed = 2 * (ri + ring);
        if (d.Type == TipoRotatoria.Mini && inscribed > 28) L.Warnings.Add($"Minirrotatória com diâmetro inscrito de {inscribed:0.0} m (referência: 13 a 25 m).");
        if (d.Type != TipoRotatoria.Mini && d.IslandType is not (TipoIlhaCentral.Galgavel or TipoIlhaCentral.Pintada) && ri < 4 && d.Legs.Count > 0)
            L.Warnings.Add("Ilha central pequena (< 4 m): em rotatórias compactas prefira ilha galgável ou pintada (minirrotatória).");
        if (d.Lanes >= 2 && d.Type is TipoRotatoria.Compacta or TipoRotatoria.Mini) L.Warnings.Add("Rotatórias compactas/mini devem ter uma faixa no anel.");
        if (!complete && d.Legs.Any(l => l.RoadId == null && l.GroupId == null) && d.Legs.Count > 0)
            L.Warnings.Add("Integração 'anel' ou 'somente ilha' sem vias ligadas: a pista dos ramos não é gerada – use 'Completa' ou crie as vias antes.");

        // Pista: anel + ramos, com raio de entrada (lado de chegada) e de saída (lado de partida) por ramo.
        List<Polygon2> full;
        var bypassIslands = new List<Polygon2>();
        if (complete)
        {
            var parts = new List<Polygon2> { outer };
            foreach (var leg in d.Legs)
            {
                var u = Dir(leg.AngleDeg);
                var t0 = Math.Max(0.5, L.ToOuter(c, u) * 0.6);
                parts.AddRange(PolygonOps.Strip(new[] { c + u * t0, c + u * (zoneR + 5) }, Math.Max(3, leg.Width)));
            }
            var raw = PolygonOps.Union(parts);
            full = PolygonOps.Intersect(CloseCorners(d, raw, c, zoneR), zone);

            // Faixas de conversão livre à direita (by-pass): entre cada ramo e o seguinte no sentido de giro.
            if (d.Bypass && d.Legs.Count >= 2)
            {
                var re = Math.Max(0, d.EntryRadius);
                var rx = Math.Max(0, d.ExitRadius ?? d.EntryRadius);
                var bw = Math.Clamp(d.BypassWidth, 3.0, 8.0);
                // A folga entre as concordâncias cresce ~0,41·ΔR na bissetriz: raio suficiente para a faixa + ilha de 2 m.
                var rb = Math.Max(d.BypassRadius, Math.Max(re, rx) + (bw + 2.0) / 0.414);
                List<Polygon2> lobes = new(), lanes = new(), isl = new();
                for (int it = 0; it < 5; it++)
                {
                    var big = PolygonOps.Intersect(Close(raw, rb), zone);
                    var band = PolygonOps.Difference(big, PolygonOps.Offset(big, -bw));
                    lobes = PolygonOps.Difference(big, PolygonOps.Offset(full, 0.02)).Where(p => p.Area > 5).ToList();
                    lanes = PolygonOps.Intersect(lobes, band);
                    isl = PolygonOps.Difference(lobes, PolygonOps.Offset(lanes, 0.02));
                    isl = PolygonOps.Offset(PolygonOps.Offset(isl, -0.4, true), 0.4, true).Where(p => p.Area > 4).ToList();
                    if (isl.Count >= Math.Min(d.Legs.Count, 2)) break;
                    rb *= 1.3;   // sem espaço para a ilha: concordância maior
                }
                if (rb > d.BypassRadius + 0.5) L.Warnings.Add($"Raio do by-pass ajustado para {rb:0.0} m (espaço para a faixa e a ilha).");
                if (lanes.Count == 0) L.Warnings.Add("Sem espaço para o by-pass: aumente o raio do by-pass ou reduza a largura da faixa.");
                else
                {
                    full = PolygonOps.Union(full.Concat(lobes));
                    bypassIslands.AddRange(isl);
                }
            }
        }
        else full = d.Integration == IntegracaoRotatoria.Anel ? new List<Polygon2> { outer } : new List<Polygon2>();

        // Ilhas separadoras (gota) em cada ramo: físicas, pintadas ou nenhuma (padrão geral ou por ramo).
        var splitters = new List<Polygon2>();
        foreach (var leg in d.Legs)
        {
            var u = Dir(leg.AngleDeg);
            var n = u.PerpLeft;
            var rou = L.ToOuter(c, u);
            var mode = leg.Splitter == IlhaSeparadora.Padrao ? d.SplitterStyle : leg.Splitter;
            if (!d.SplitterIslands || mode == IlhaSeparadora.Padrao) mode = d.SplitterIslands ? IlhaSeparadora.Fisica : IlhaSeparadora.Nenhuma;
            Polygon2? sp = null;
            if (mode != IlhaSeparadora.Nenhuma && leg.Width >= 6)
            {
                var w0 = Math.Min(d.SplitterWidth, leg.Width - 6.0);
                if (w0 >= 0.8)
                {
                    var t0 = rou + 1.0;
                    var t1 = rou + Math.Max(3, leg.SplitterLength ?? d.SplitterLength);
                    var tri = new Polygon2(new[] { c + u * t0 - n * (w0 / 2), c + u * t1 - n * 0.3, c + u * t1 + n * 0.3, c + u * t0 + n * (w0 / 2) });
                    sp = PolygonOps.Offset(PolygonOps.Offset(new[] { tri }, -0.25, true), 0.25, true).OrderByDescending(p => p.Area).FirstOrDefault();
                }
                else L.Warnings.Add($"Ramo a {leg.AngleDeg:0}°: pista estreita para ilha separadora (mínimo ~7 m).");
            }
            else if (mode != IlhaSeparadora.Nenhuma && leg.Width < 6)
                L.Warnings.Add($"Ramo a {leg.AngleDeg:0}°: pista estreita para ilha separadora (mínimo ~7 m).");
            var physical = sp != null && mode == IlhaSeparadora.Fisica;
            if (physical) splitters.Add(sp!);
            L.Legs.Add(new RoundaboutLegGeometry(leg, u, n, physical ? sp : null) { PaintedSplitter = sp != null && mode == IlhaSeparadora.Pintada ? sp : null });
        }

        var solids = splitters.Concat(bypassIslands).ToList();
        if (complete) L.Pavement.AddRange(PolygonOps.Difference(full, solids.Append(apronOuter)));
        else if (d.Integration == IntegracaoRotatoria.Anel) L.Pavement.AddRange(PolygonOps.Difference(full, new[] { apronOuter }));
        if (apronW > 0.01) L.Apron.AddRange(PolygonOps.Difference(new[] { apronOuter }, new[] { island }));
        if (painted)
        {
            // Sem obra civil: a ilha é a linha de canalização + tachões (+ zebrado), gerados como marcas filhas.
        }
        else if (d.IslandType == TipoIlhaCentral.Galgavel) L.IslandCore.Add(island);
        else
        {
            var core = PolygonOps.Offset(new[] { island }, -cw);
            L.IslandCurb.AddRange(PolygonOps.Difference(new[] { island }, core));
            L.IslandCore.AddRange(core);
        }
        foreach (var sp in solids)
        {
            var sc = PolygonOps.Offset(new[] { sp }, -cw);
            L.SplitterCurb.AddRange(PolygonOps.Difference(new[] { sp }, sc));
            L.SplitterCore.AddRange(sc);
        }

        // Turbo: divisores físicos entre as faixas, interrompidos nas entradas/saídas dos ramos.
        if (d.TurboDividers && d.Lanes >= 2)
        {
            var dw = Math.Clamp(d.DividerWidth, 0.15, 1.0);
            var gaps = d.Legs.SelectMany(l => PolygonOps.Strip(new[] { c, c + Dir(l.AngleDeg) * (zoneR + 5) }, Math.Max(3, l.Width) + 2)).ToList();
            for (int q = 1; q < d.Lanes; q++)
            {
                var r = apronW + q * d.LaneWidth;
                var ringBand = PolygonOps.Difference(PolygonOps.Offset(new[] { island }, r + dw / 2, true), PolygonOps.Offset(new[] { island }, r - dw / 2, true));
                L.Dividers.AddRange(PolygonOps.Difference(ringBand, gaps).Where(p => p.Area > 0.2));
            }
        }

        // Meio-fio externo e calçada em volta – só quando as entradas são remodeladas.
        if (complete)
        {
            var sw = Math.Max(0, d.SidewalkWidth);
            var curb = PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(full, cw, true), full), zone);
            L.Curb.AddRange(curb);
            if (sw > 0.05)
                L.Sidewalk.AddRange(PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(full, cw + sw, true), PolygonOps.Offset(full, cw, true)), zone));
        }
        return L;
    }

    /// <summary>
    /// Concordância dos cantos entre ramos vizinhos com raios distintos por ramo: metade do canto junto ao ramo que
    /// chega (raio de entrada dele) e metade junto ao ramo que sai (raio de saída dele). Circulação anti-horária.
    /// </summary>
    private static List<Polygon2> CloseCorners(RoundaboutDefinition d, List<Polygon2> raw, Vec2 c, double zoneR)
    {
        var re0 = Math.Max(0, d.EntryRadius);
        var rx0 = Math.Max(0, d.ExitRadius ?? d.EntryRadius);
        var legs = d.Legs.OrderBy(l => Norm(l.AngleDeg)).ToList();
        if (legs.Count < 2) return Close(raw, re0);
        var cache = new Dictionary<int, List<Polygon2>>();
        List<Polygon2> Closed(double r)
        {
            var key = (int)Math.Round(r * 10);
            if (!cache.TryGetValue(key, out var v)) cache[key] = v = Close(raw, r);
            return v;
        }
        var pieces = new List<Polygon2>(raw);
        for (int i = 0; i < legs.Count; i++)
        {
            var A = legs[i];
            var B = legs[(i + 1) % legs.Count];
            var a = Norm(A.AngleDeg);
            var b = Norm(B.AngleDeg);
            var span = b - a;
            while (span <= 0) span += 360;
            var mid = a + span / 2;
            var re = Math.Max(0, A.EntryRadius ?? re0);
            var rx = Math.Max(0, B.ExitRadius ?? rx0);
            pieces.AddRange(PolygonOps.Intersect(Closed(re), new[] { Sector(c, a, mid, zoneR + 10) }));
            pieces.AddRange(PolygonOps.Intersect(Closed(rx), new[] { Sector(c, mid, b, zoneR + 10) }));
        }
        return PolygonOps.Union(pieces);
    }

    public static MarkingGeometry Build(RoundaboutDefinition d, BuildContext ctx)
    {
        var L = Layout(d);
        var geo = new MarkingGeometry();
        geo.Warnings.AddRange(L.Warnings);
        void Raised(IEnumerable<Polygon2> shapes, MarkingColor col, double h, double elev = 0)
        {
            foreach (var s in shapes)
            {
                var ss = s.Simplified();
                if (ss != null && ss.Area > 1e-4) geo.Pieces.Add(new MarkingPiece(ss, col) { Thickness = h, Elevation = elev });
            }
        }
        // Rebaixamentos das travessias recortam a calçada da rotatória.
        if (L.Sidewalk.Count > 0)
        {
            var ramps = Children(d, L, new OutputSettings(), 0).OfType<RampDefinition>()
                .Select(r => RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points))).ToList();
            if (ramps.Count > 0)
            {
                var sw = PolygonOps.Difference(L.Sidewalk, ramps);
                var cb = PolygonOps.Difference(L.Curb, ramps);
                L.Sidewalk.Clear(); L.Sidewalk.AddRange(sw);
                L.Curb.Clear(); L.Curb.AddRange(cb);
            }
        }
        var pavColor = d.Pavement switch { TipoPavimento.Bloquete => MarkingColor.Bloquete, TipoPavimento.Concreto => MarkingColor.PavimentoConcreto, _ => MarkingColor.Asfalto };
        var pt = d.Pavement switch { TipoPavimento.Bloquete => 0.08, TipoPavimento.Concreto => 0.15, _ => 0.05 };
        if (d.Pavement != TipoPavimento.Nenhum) Raised(L.Pavement, pavColor, pt, -pt);
        Raised(L.Apron, MarkingColor.Bloquete, Math.Clamp(d.ApronHeight, 0.02, 0.15));   // galgável: bloquete elevado
        Raised(L.IslandCurb, MarkingColor.Concreto, d.CurbHeight);
        switch (d.IslandType)
        {
            case TipoIlhaCentral.Galgavel: Raised(L.IslandCore, MarkingColor.Branca, 0.07); break;   // cúpula galgável pintada
            case TipoIlhaCentral.Pavimentada: Raised(L.IslandCore, MarkingColor.Concreto, d.CurbHeight); break;
            case TipoIlhaCentral.Pintada: break;                                                     // marcas filhas (LCA, tachões, zebrado)
            default: Raised(L.IslandCore, MarkingColor.Grama, d.CurbHeight); break;
        }
        Raised(L.Dividers, MarkingColor.Concreto, 0.10);
        Raised(L.SplitterCurb, MarkingColor.Concreto, d.CurbHeight);
        Raised(L.SplitterCore, MarkingColor.Concreto, d.CurbHeight);
        Raised(L.Curb, MarkingColor.Concreto, d.CurbHeight);
        Raised(L.Sidewalk, MarkingColor.Concreto, d.CurbHeight);

        if (d.Landscaping && d.IslandType == TipoIlhaCentral.Ajardinada && d.IslandRadius >= 3 && ctx.Catalog.Movel("ARVORE") is { } tree)
        {
            var spots = new List<Vec2> { d.Center };
            var ring = d.IslandRadius * 0.55;
            var count = d.Trees > 0 ? d.Trees - 1 : d.IslandRadius >= 6 ? 5 : 0;
            var k = Math.Max(1, d.Elongation);
            var ax = Dir(d.OvalAngleDeg);
            for (int i = 0; i < count; i++)
            {
                var a = Dir(90 + i * 360.0 / count);
                var local = ax * (a.Dot(ax) * k) + ax.PerpLeft * a.Dot(ax.PerpLeft);
                spots.Add(d.Center + local * ring);
            }
            foreach (var (s, i) in spots.Select((s, i) => (s, i)))
                foreach (var p in UrbanGenerator.BuildAt(tree, new LocalFrame(s, Dir(i * 40)), null, null, i == 0 ? null : 5.0, null).Pieces)
                    geo.Pieces.Add(p with { Elevation = p.Elevation + d.CurbHeight });
        }
        geo.UnitCount = 1;
        geo.PathLength = d.Legs.Count;
        return geo;
    }

    /// <summary>
    /// Trechos da borda externa do anel entre os ramos (para a linha de bordo). Um único trecho fechado quando não há
    /// ramos abertos; o polígono é percorrido a partir de uma abertura para não partir um trecho no início.
    /// </summary>
    public static List<(List<Vec2> Points, bool Closed)> RingEdgeChains(RoundaboutLayout L, Vec2 c, double reach)
    {
        var openings = L.Legs.SelectMany(g => PolygonOps.Strip(new[] { c, c + g.Dir * reach }, Math.Max(3, g.Leg.Width) + 0.6)).ToList();
        var pts = L.Outer.Outer;
        var n = pts.Count;
        bool Open(Vec2 p) => openings.Any(o => o.Contains(p));
        var start = -1;
        for (int i = 0; i < n; i++) if (Open(pts[i])) { start = i; break; }
        if (start < 0) return new List<(List<Vec2>, bool)> { (pts.ToList(), true) };
        var chains = new List<(List<Vec2>, bool)>();
        var cur = new List<Vec2>();
        for (int s = 1; s <= n; s++)
        {
            var p = pts[(start + s) % n];
            if (!Open(p)) cur.Add(p);
            else if (cur.Count >= 2) { chains.Add((cur, false)); cur = new List<Vec2>(); }
            else cur.Clear();
        }
        if (cur.Count >= 2) chains.Add((cur, false));
        return chains;
    }

    private static double SignedArea(IReadOnlyList<Vec2> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++) a += pts[i].X * pts[(i + 1) % pts.Count].Y - pts[(i + 1) % pts.Count].X * pts[i].Y;
        return a / 2;
    }

    /// <summary>Sinalização da rotatória (marcas independentes): dê a preferência, divisão de faixas, zebrados, travessias e placas.</summary>
    public static List<MarkingDefinition> Children(RoundaboutDefinition d, RoundaboutLayout L, OutputSettings output, double z)
    {
        var res = new List<MarkingDefinition>();
        var c = d.Center;
        var painted = L.PaintedIsland;
        var apronW = painted ? 0 : Math.Max(0, d.ApronWidth);
        var k = Math.Max(1, d.Elongation);
        var v = Math.Max(20, d.ApproachSpeed);
        T Add<T>(T def) where T : MarkingDefinition
        {
            def.Output = output.Clone();
            def.GroupId = d.Id;
            res.Add(def);
            return def;
        }
        LinearMarkingDefinition ClosedLine(string code, Polygon2 ring, double? width = null, MarkingColor? color = null, string? variant = null) =>
            Add(new LinearMarkingDefinition { Code = code, Variant = variant, WidthOverride = width, ColorOverride = color, PathRef = PathReference.FromPoints(ring.Outer, z, true) });

        // ---- ilha central pintada (MBST Vol. IV, MIR 6.a): LCA branca de 0,20 m + tachões a cada 0,25–0,50 m (+ zebrado)
        if (painted)
        {
            var lw = Math.Clamp(d.PaintedLineWidth, 0.10, 0.40);
            ClosedLine("LCA", Grow(L.Island, -lw / 2), lw, MarkingColor.Branca, "0,20 m");
            if (d.PaintedIslandFill)
                Add(new HatchMarkingDefinition
                {
                    Code = "ZPA", BorderWidth = 0, BarColor = MarkingColor.Branca, ReferenceDirection = Vec2.UnitX,
                    Boundary = PathReference.FromPoints(Grow(L.Island, -(lw + 0.05)).Outer, z, true),
                });
            if (d.StudSpacing >= 0.05)
                Add(new DeviceMarkingDefinition { Code = "TACHAO-SEG", Spacing = d.StudSpacing, PathRef = PathReference.FromPoints(Grow(L.Island, 0.15).Outer, z, true) });
        }

        // ---- anel: linha entre faixas, linha de bordo interna (junto à ilha/galgável) e externa (entre os ramos)
        if (d.Markings && d.Lanes > 1 && !d.TurboDividers)
            for (int q = 1; q < d.Lanes; q++)
                ClosedLine(string.IsNullOrWhiteSpace(d.RingLaneLine) ? "LMS-2" : d.RingLaneLine, Grow(L.Island, apronW + q * d.LaneWidth));
        if (d.Markings && d.InnerEdgeLine && !painted)
            ClosedLine("LBO", Grow(L.ApronOuter, 0.15));
        var reach = L.Zone.Outer.Max(p => p.DistanceTo(c)) + 10;
        if (d.Markings && d.OuterEdgeLine && d.Integration != IntegracaoRotatoria.SomenteIlha)
        {
            var inward = SignedArea(L.Outer.Outer) >= 0 ? 0.15 : -0.15;
            foreach (var (pts, closed) in RingEdgeChains(L, c, reach))
                Add(new LinearMarkingDefinition { Code = "LBO", Offset = inward, PathRef = PathReference.FromPoints(pts, z, closed) });
        }

        // ---- setas de movimento em curva (IMC) no anel, após cada entrada (uma por faixa)
        if (d.Markings && d.RingArrows)
            foreach (var g in L.Legs)
            {
                var dirA = Dir(g.Leg.AngleDeg + 28);
                var rOut = Ray(L.Outer, c, dirA);
                if (rOut <= 0) continue;
                for (int q = 0; q < Math.Max(1, d.Lanes); q++)
                {
                    var r = rOut - (d.Lanes - q - 0.5) * d.LaneWidth;
                    Add(new SymbolMarkingDefinition { Code = "IMC", Length = v > 60 ? 6.0 : 4.5, Position = c + dirA * r, Direction = dirA.PerpLeft, Z = z });
                }
            }

        var islandR = d.IslandRadius * k;
        var dirSigns = d.DirectionSigns == PlacaSentidoRotatoria.Automatico
            ? (islandR < 12 || painted ? PlacaSentidoRotatoria.R33NasEntradas : PlacaSentidoRotatoria.R24aNaIlha)
            : d.DirectionSigns;

        foreach (var g in L.Legs)
        {
            var leg = g.Leg;
            var hw = leg.Width / 2;
            var splitterW = g.Splitter != null || g.PaintedSplitter != null ? Math.Min(d.SplitterWidth, leg.Width - 6) : 0;
            var inner = splitterW > 0 ? splitterW / 2 + 0.2 : 0.1;
            // Entrada = lado esquerdo do ramo (quem chega pela direita da pista circula no sentido anti-horário).
            var rou = L.ToOuter(c, g.Dir);
            var splitLen = Math.Max(3, leg.SplitterLength ?? d.SplitterLength);
            var control = leg.Control == ControleRamo.Padrao ? ControleRamo.DeAPreferencia : leg.Control;
            var crosswalk = leg.Crosswalk ?? d.Crosswalks;
            var signs = leg.Signs ?? d.Signs;

            if (d.Markings)
            {
                var o0 = inner;
                var o1 = hw - 0.3;
                double T(double o) => L.ToOuter(c + g.Left * o, g.Dir) + 0.4;
                if (control == ControleRamo.Pare)
                {
                    // Parada obrigatória: linha de retenção + legenda PARE (≥ 1,60 m antes) + R-1.
                    Add(new LinearMarkingDefinition { Code = "LRE", Variant = "0,40 m", PathRef = PathReference.FromPoints(new[] { g.At(c, T(o1), o1), g.At(c, T(o0), o0) }, z) });
                    Add(new TextMarkingDefinition { Text = "PARE", Height = DesignRules.LegendHeight(v), Position = g.At(c, rou + 2.4 + DesignRules.LegendHeight(v) / 2, (o0 + o1) / 2), Direction = -g.Dir, Z = z });
                }
                else
                {
                    Add(new LinearMarkingDefinition { Code = "LDP", Variant = "0,40 m (0,60 × 0,60 m)", PathRef = PathReference.FromPoints(new[] { g.At(c, T(o1), o1), g.At(c, T(o0), o0) }, z) });
                    var sym = DesignRules.YieldSymbolLength(v);
                    Add(new SymbolMarkingDefinition { Code = "SDP", Length = sym, Position = g.At(c, rou + 1.6 + sym / 2, (o0 + o1) / 2), Direction = -g.Dir, Z = z });
                }
                if (g.Splitter != null)
                {
                    // Nariz zebrado (branco) na ponta da ilha física.
                    var t1 = rou + splitLen;
                    var tip = g.At(c, t1 + 10, 0);
                    Add(new HatchMarkingDefinition
                    {
                        Code = "ZPA",
                        Boundary = PathReference.FromPoints(new[] { g.At(c, t1 - 0.2, -0.6), tip, g.At(c, t1 - 0.2, 0.6) }, z, true),
                    });
                }
                if (g.PaintedSplitter is { } ps)
                {
                    // Gota pintada: zebrado com linha de canalização – amarelo entre fluxos opostos, branco em mão única.
                    var col = leg.TwoWay ? MarkingColor.Amarela : MarkingColor.Branca;
                    Add(new HatchMarkingDefinition
                    {
                        Code = "ZPA", BarWidth = 0.50, Gap = DesignRules.ChannelHatchGap(v), BorderWidth = 0.20, BarColor = col, BorderColor = col,
                        ReferenceDirection = -g.Dir, AxisPoint = g.At(c, rou, 0),
                        Boundary = PathReference.FromPoints(ps.Outer, z, true),
                    });
                }
                if (d.ApproachDoubleLine && leg.TwoWay && d.Integration == IntegracaoRotatoria.Completa)
                {
                    // LFO-3 entre a ponta da ilha separadora e o fim da zona remodelada (aproximação sem ultrapassagem).
                    var t0 = (g.Splitter != null || g.PaintedSplitter != null ? rou + splitLen : rou + 2.0) + 0.5;
                    var t1 = L.ToOuter(c, g.Dir) + Math.Max(0, L.LegLength) - 0.3;
                    var zoneT = Ray(L.Zone, c, g.Dir);
                    if (zoneT > t0 + 2) t1 = zoneT - 0.3;
                    if (t1 > t0 + 1) Add(new LinearMarkingDefinition { Code = "LFO-3", PathRef = PathReference.FromPoints(new[] { g.At(c, t0, 0), g.At(c, t1, 0) }, z) });
                }
            }
            if (crosswalk)
            {
                var tc = rou + Math.Max(2, d.CrosswalkDistance);
                var a = g.At(c, tc, hw - 0.3);
                var b = g.At(c, tc, -(hw - 0.3));
                var cwk = Add(new LinearMarkingDefinition { Code = "FTP-1", WidthOverride = Math.Clamp(d.CrosswalkWidth, 2, 10), Overlay = true,
                    PathRef = PathReference.FromPoints(new[] { a, b }, z) });
                if (g.Splitter != null) cwk.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = g.Splitter.Outer.ToList() });
                if (leg.Sidewalk > 0.5 && d.Integration == IntegracaoRotatoria.Completa)
                    foreach (var sgn in new[] { 1.0, -1.0 })
                    {
                        var curb = g.At(c, tc, sgn * hw);
                        Add(new RampDefinition { PathRef = PathReference.FromPoints(new[] { curb, curb + g.Left * sgn }, z), Height = d.CurbHeight });
                    }
            }
            if (signs)
            {
                var sx = rou + 3.0;
                // R-1 ou R-2 no lado direito de quem chega (1,5 a 15 m da borda do anel); R-33 no mesmo suporte, acima.
                Add(new SignDefinition { Code = control == ControleRamo.Pare ? "R-1" : "R-2", Position = g.At(c, sx, hw + 0.9), Direction = -g.Dir, Z = z, Width = 0.75 });
                if (dirSigns is PlacaSentidoRotatoria.R33NasEntradas or PlacaSentidoRotatoria.Ambas)
                    Add(new SignDefinition { Code = "R-33", Position = g.At(c, sx, hw + 0.9), Direction = -g.Dir, Z = z, Width = 0.60, MountHeight = 2.10 + 0.75 + 0.10, Support = TipoSuporte.Nenhum });
                if (dirSigns is PlacaSentidoRotatoria.R24aNaIlha or PlacaSentidoRotatoria.Ambas && !painted)
                {
                    var edge = Ray(L.Island, c, g.Dir);
                    if (edge > 1.5) Add(new SignDefinition { Code = "R-24a", Position = c + g.Dir * (edge - 0.8), Direction = -g.Dir, Z = z, Width = 0.60, BaseElevation = d.CurbHeight });
                }
                if (d.AdvanceWarning)
                {
                    // A-12 com "A ... m": à distância de desaceleração (MBST Vol. II) antes da entrada.
                    var dist = DesignRules.WarningDistance(v);
                    var pa = g.At(c, rou + dist, hw + 0.9);
                    Add(new SignDefinition { Code = "A-12", Position = pa, Direction = -g.Dir, Z = z, Width = 0.60 });
                    Add(new SignDefinition { Code = "INF-DIST", Position = pa, Direction = -g.Dir, Z = z, Width = 0.60, Height = 0.25, Legend = $"A {dist} m",
                        MountHeight = 2.10 - 0.25 - 0.05, Support = TipoSuporte.Nenhum });
                }
                if (d.AlignmentMarkers && !painted)
                {
                    var edge = Ray(L.Island, c, g.Dir);
                    foreach (var o in new[] { -1.2, 1.2 })
                        Add(new SignDefinition { Code = "MA-ALIN", Position = c + g.Dir * (edge - 0.45) + g.Left * o, Direction = -g.Dir, Z = z, MountHeight = 0.80, BaseElevation = d.CurbHeight });
                }
            }
        }
        return res;
    }

    /// <summary>Ramos a partir das vias que passam pelo centro (ou terminam junto a ele).</summary>
    public static List<RoundaboutLeg> LegsFromRoads(Vec2 center, IReadOnlyList<IntersectionRoad> roads, double reach)
    {
        var legs = new List<RoundaboutLeg>();
        foreach (var r in roads)
        {
            var (s, dist, _) = IntersectionGenerator.Project(r.Axis, center);
            if (dist > Math.Max(r.Def.TotalLeft, r.Def.TotalRight) + 2) continue;
            foreach (var sign in new[] { -1, 1 })
            {
                var avail = sign > 0 ? r.Axis.Length - s : s;
                if (avail < reach * 0.6) continue;
                var p = r.Axis.PointAt(s + sign * Math.Min(avail, reach));
                var v = p - center;
                if (v.Length < 1) continue;
                legs.Add(new RoundaboutLeg
                {
                    AngleDeg = Math.Atan2(v.Y, v.X) * 180 / Math.PI,
                    Width = r.Def.RightWidth + r.Def.LeftWidth,
                    Sidewalk = Math.Max(r.Def.RightSidewalk, r.Def.LeftSidewalk),
                    RoadId = r.Def.Id,
                    GroupId = r.Def.GroupId,
                    TwoWay = r.Def.TwoWay,
                });
            }
        }
        return legs;
    }

    /// <summary>
    /// Ramos redetectados a partir das vias mantêm a personalização feita pelo usuário (ilha, travessia, raios,
    /// controle, placas): cada ramo novo herda do ramo antigo mais próximo em ângulo (até 25°) ou da mesma via.
    /// </summary>
    public static List<RoundaboutLeg> MergeLegSettings(IReadOnlyList<RoundaboutLeg> old, List<RoundaboutLeg> fresh)
    {
        foreach (var f in fresh)
        {
            var best = old.Where(o => o.RoadId != null && o.RoadId == f.RoadId && AngleDiff(o.AngleDeg, f.AngleDeg) < 90)
                .Concat(old.Where(o => AngleDiff(o.AngleDeg, f.AngleDeg) < 25))
                .OrderBy(o => AngleDiff(o.AngleDeg, f.AngleDeg)).FirstOrDefault();
            if (best != null) f.CopySettingsFrom(best);
        }
        return fresh;
    }

    private static double AngleDiff(double a, double b)
    {
        var d = Math.Abs(Norm(a) - Norm(b));
        return Math.Min(d, 360 - d);
    }

    /// <summary>Pistas de exemplo dos ramos (pré-visualização dos modos que não geram a pista dos ramos).</summary>
    public static List<Polygon2> PreviewRoads(RoundaboutDefinition d, RoundaboutLayout L)
    {
        var c = d.Center;
        var reach = L.Outer.Outer.Max(p => p.DistanceTo(c)) + 30;
        var res = new List<Polygon2>();
        foreach (var g in L.Legs)
            res.AddRange(PolygonOps.Strip(new[] { c, c + g.Dir * reach }, Math.Max(3, g.Leg.Width)));
        return PolygonOps.Union(res);
    }
}
