using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Geometria de um ramo da rotatória (sistema local: t ao longo do ramo a partir do centro, o à esquerda).</summary>
public sealed record RoundaboutLegGeometry(RoundaboutLeg Leg, Vec2 Dir, Vec2 Left, Polygon2? Splitter)
{
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
    public Polygon2 Zone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
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

    public static RoundaboutLayout Layout(RoundaboutDefinition d)
    {
        var L = new RoundaboutLayout();
        var c = d.Center;
        var ri = Math.Max(1, d.IslandRadius);
        var k = Math.Clamp(d.Elongation, 1, 4);
        var cw = Math.Max(0.05, d.CurbWidth);
        var ring = Math.Max(0, d.ApronWidth) + Math.Max(1, d.Lanes) * d.LaneWidth;
        var island = k > 1.001 ? Ellipse(c, ri * k, ri, d.OvalAngleDeg) : Disk(c, ri);
        L.Island = island;
        var apronOuter = d.ApronWidth > 0.01 ? PolygonOps.Offset(new[] { island }, d.ApronWidth, true).OrderByDescending(p => p.Area).First() : island;
        var outer = PolygonOps.Offset(new[] { island }, ring, true).OrderByDescending(p => p.Area).First();
        L.Outer = outer;
        L.LegLength = (d.SplitterIslands ? d.SplitterLength : 0) + 12 + (d.Bypass ? Math.Max(d.BypassRadius, Math.Max(d.EntryRadius, d.ExitRadius ?? d.EntryRadius) + 16) * 1.6 : 0);
        L.Zone = PolygonOps.Offset(new[] { outer }, L.LegLength, true).OrderByDescending(p => p.Area).First();
        var zone = new[] { L.Zone };
        var zoneR = L.Zone.Outer.Max(v => v.DistanceTo(c));
        if (d.Legs.Count == 0) L.Warnings.Add("Rotatória sem ramos: informe os ângulos dos ramos ou posicione-a sobre o cruzamento de vias.");
        if (d.Lanes * d.LaneWidth < 4.0) L.Warnings.Add("Pista giratória com menos de 4 m – confira a largura para os veículos de projeto.");
        var inscribed = 2 * (ri + ring);
        if (d.Type == TipoRotatoria.Mini && inscribed > 28) L.Warnings.Add($"Minirrotatória com diâmetro inscrito de {inscribed:0.0} m (referência: 13 a 25 m).");
        if (d.Type != TipoRotatoria.Mini && d.IslandType != TipoIlhaCentral.Galgavel && ri < 4 && d.Legs.Count > 0)
            L.Warnings.Add("Ilha central pequena (< 4 m): em rotatórias compactas prefira ilha galgável (minirrotatória).");
        if (d.Lanes >= 2 && d.Type is TipoRotatoria.Compacta or TipoRotatoria.Mini) L.Warnings.Add("Rotatórias compactas/mini devem ter uma faixa no anel.");

        // Pista: anel + ramos, com raio de entrada (lado de chegada) e de saída (lado de partida) distintos.
        var parts = new List<Polygon2> { outer };
        foreach (var leg in d.Legs)
        {
            var u = Dir(leg.AngleDeg);
            var t0 = Math.Max(0.5, L.ToOuter(c, u) * 0.6);
            parts.AddRange(PolygonOps.Strip(new[] { c + u * t0, c + u * (zoneR + 5) }, Math.Max(3, leg.Width)));
        }
        var raw = PolygonOps.Union(parts);
        var re = Math.Max(0, d.EntryRadius);
        var rx = Math.Max(0, d.ExitRadius ?? d.EntryRadius);
        List<Polygon2> full;
        if (d.Legs.Count >= 2 && Math.Abs(re - rx) > 0.05)
        {
            // Cada canto entre ramos vizinhos: metade junto ao ramo que chega (entrada) e metade junto ao que sai.
            var entry = Close(raw, re);
            var exit = Close(raw, rx);
            var angles = d.Legs.Select(l => ((l.AngleDeg % 360) + 360) % 360).OrderBy(a => a).ToList();
            var pieces = new List<Polygon2>(raw);
            for (int i = 0; i < angles.Count; i++)
            {
                var a = angles[i];
                var b = angles[(i + 1) % angles.Count];
                var span = b - a;
                while (span <= 0) span += 360;
                var mid = a + span / 2;
                pieces.AddRange(PolygonOps.Intersect(entry, new[] { Sector(c, a, mid, zoneR + 10) }));
                pieces.AddRange(PolygonOps.Intersect(exit, new[] { Sector(c, mid, b, zoneR + 10) }));
            }
            full = PolygonOps.Union(pieces);
        }
        else full = Close(raw, re);
        full = PolygonOps.Intersect(full, zone);

        // Faixas de conversão livre à direita (by-pass): entre cada ramo e o seguinte no sentido de giro.
        var bypassIslands = new List<Polygon2>();
        if (d.Bypass && d.Legs.Count >= 2)
        {
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

        // Ilhas separadoras (gota) em cada ramo.
        var splitters = new List<Polygon2>();
        foreach (var leg in d.Legs)
        {
            var u = Dir(leg.AngleDeg);
            var n = u.PerpLeft;
            var rou = L.ToOuter(c, u);
            Polygon2? sp = null;
            if (d.SplitterIslands && leg.Width >= 6)
            {
                var w0 = Math.Min(d.SplitterWidth, leg.Width - 6.0);
                if (w0 >= 0.8)
                {
                    var t0 = rou + 1.0;
                    var t1 = rou + Math.Max(3, d.SplitterLength);
                    var tri = new Polygon2(new[] { c + u * t0 - n * (w0 / 2), c + u * t1 - n * 0.3, c + u * t1 + n * 0.3, c + u * t0 + n * (w0 / 2) });
                    sp = PolygonOps.Offset(PolygonOps.Offset(new[] { tri }, -0.25, true), 0.25, true).OrderByDescending(p => p.Area).FirstOrDefault();
                    if (sp != null) splitters.Add(sp);
                }
                else L.Warnings.Add($"Ramo a {leg.AngleDeg:0}°: pista estreita para ilha separadora (mínimo ~7 m).");
            }
            L.Legs.Add(new RoundaboutLegGeometry(leg, u, n, sp));
        }

        var solids = splitters.Concat(bypassIslands).ToList();
        var pav = PolygonOps.Difference(full, solids.Append(apronOuter));
        L.Pavement.AddRange(pav);
        L.Apron.AddRange(PolygonOps.Difference(new[] { apronOuter }, new[] { island }));
        if (d.IslandType == TipoIlhaCentral.Galgavel) L.IslandCore.Add(island);
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
                var r = d.ApronWidth + q * d.LaneWidth;
                var ringBand = PolygonOps.Difference(PolygonOps.Offset(new[] { island }, r + dw / 2, true), PolygonOps.Offset(new[] { island }, r - dw / 2, true));
                L.Dividers.AddRange(PolygonOps.Difference(ringBand, gaps).Where(p => p.Area > 0.2));
            }
        }

        // Meio-fio externo e calçada em volta (largura dos ramos / da rotatória).
        var sw = Math.Max(0, d.SidewalkWidth);
        var curb = PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(full, cw, true), full), zone);
        L.Curb.AddRange(curb);
        if (sw > 0.05)
            L.Sidewalk.AddRange(PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(full, cw + sw, true), PolygonOps.Offset(full, cw, true)), zone));
        return L;
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
        if (d.Crosswalks)
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

    /// <summary>Sinalização da rotatória (marcas independentes): dê a preferência, divisão de faixas, zebrados, travessias e placas.</summary>
    public static List<MarkingDefinition> Children(RoundaboutDefinition d, RoundaboutLayout L, OutputSettings output, double z)
    {
        var res = new List<MarkingDefinition>();
        var c = d.Center;
        var ro = d.OuterRadius;
        T Add<T>(T def) where T : MarkingDefinition
        {
            def.Output = output.Clone();
            def.GroupId = d.Id;
            res.Add(def);
            return def;
        }

        if (d.Markings && d.Lanes > 1 && !d.TurboDividers)
            for (int k = 1; k < d.Lanes; k++)
            {
                var ringLine = PolygonOps.Offset(new[] { L.Island }, d.ApronWidth + k * d.LaneWidth, true).OrderByDescending(p => p.Area).FirstOrDefault();
                if (ringLine != null) Add(new LinearMarkingDefinition { Code = "LMS-1", PathRef = PathReference.FromPoints(ringLine.Outer, z, true) });
            }

        foreach (var g in L.Legs)
        {
            var hw = g.Leg.Width / 2;
            var inner = g.Splitter != null ? Math.Min(d.SplitterWidth, g.Leg.Width - 6) / 2 + 0.2 : 0.1;
            // Entrada = lado esquerdo do ramo (quem chega pela direita da pista circula no sentido anti-horário).
            var rou = L.ToOuter(c, g.Dir);
            if (d.Markings)
            {
                var o0 = inner;
                var o1 = hw - 0.3;
                double T(double o) => L.ToOuter(c + g.Left * o, g.Dir) + 0.4;
                Add(new LinearMarkingDefinition { Code = "LDP", PathRef = PathReference.FromPoints(new[] { g.At(c, T(o1), o1), g.At(c, T(o0), o0) }, z) });
                var tSym = rou + 4.0;
                Add(new SymbolMarkingDefinition { Code = "SDP", Length = 3.6, Position = g.At(c, tSym, (o0 + o1) / 2), Direction = -g.Dir, Z = z });
                if (g.Splitter != null)
                {
                    var t1 = rou + Math.Max(3, d.SplitterLength);
                    var tip = g.At(c, t1 + 10, 0);
                    Add(new HatchMarkingDefinition
                    {
                        Code = "ZPA",
                        Boundary = PathReference.FromPoints(new[] { g.At(c, t1 - 0.2, -0.6), tip, g.At(c, t1 - 0.2, 0.6) }, z, true),
                    });
                }
            }
            if (d.Crosswalks)
            {
                var tc = rou + Math.Max(2, d.CrosswalkDistance);
                var a = g.At(c, tc, hw - 0.3);
                var b = g.At(c, tc, -(hw - 0.3));
                var cwk = Add(new LinearMarkingDefinition { Code = "FTP-1", WidthOverride = Math.Clamp(d.CrosswalkWidth, 2, 10), Overlay = true,
                    PathRef = PathReference.FromPoints(new[] { a, b }, z) });
                if (g.Splitter != null) cwk.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = g.Splitter.Outer.ToList() });
                if (g.Leg.Sidewalk > 0.5)
                    foreach (var sgn in new[] { 1.0, -1.0 })
                    {
                        var curb = g.At(c, tc, sgn * hw);
                        Add(new RampDefinition { PathRef = PathReference.FromPoints(new[] { curb, curb + g.Left * sgn }, z), Height = d.CurbHeight });
                    }
            }
            if (d.Signs)
            {
                Add(new SignDefinition { Code = "R-2", Position = g.At(c, rou + 3.5, hw + 0.9), Direction = -g.Dir, Z = z, Width = 0.75 });
                if (g.Splitter != null)
                    Add(new SignDefinition { Code = "R-33", Position = g.At(c, rou + Math.Max(3, d.SplitterLength) * 0.45, 0), Direction = -g.Dir, Z = z });
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
                });
            }
        }
        return legs;
    }
}
