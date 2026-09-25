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
    public List<RoundaboutLegGeometry> Legs { get; } = new();
    public Polygon2 Zone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    public double LegLength { get; set; }
    public List<string> Warnings { get; } = new();
}

/// <summary>Rotatórias: ilha central, faixa galgável, pista giratória, ramos com ilhas separadoras e sinalização.</summary>
public static class RoundaboutGenerator
{
    private static Polygon2 Disk(Vec2 c, double r) => new(CurveTools.Circle(c, r, Math.Max(0.005, r * 0.0012)));
    private static Vec2 Dir(double deg) => new(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180));

    public static RoundaboutLayout Layout(RoundaboutDefinition d)
    {
        var L = new RoundaboutLayout();
        var c = d.Center;
        var ri = Math.Max(1, d.IslandRadius);
        var rApron = ri + Math.Max(0, d.ApronWidth);
        var ro = d.OuterRadius;
        var cw = Math.Max(0.05, d.CurbWidth);
        L.LegLength = (d.SplitterIslands ? d.SplitterLength : 0) + 12;
        var zoneR = ro + L.LegLength;
        L.Zone = Disk(c, zoneR);
        var zone = new[] { L.Zone };
        if (d.Legs.Count == 0) L.Warnings.Add("Rotatória sem ramos: informe os ângulos dos ramos ou posicione-a sobre o cruzamento de vias.");
        if (ro - rApron < 4.0) L.Warnings.Add("Pista giratória com menos de 4 m – confira a largura para os veículos de projeto.");
        if (ri < 4 && d.Legs.Count > 0) L.Warnings.Add("Ilha central pequena (< 4 m): em rotatórias compactas prefira ilha galgável.");

        // Pista: disco + ramos, com raio de entrada/saída (concordância), menos a ilha e a faixa galgável.
        var parts = new List<Polygon2> { Disk(c, ro) };
        foreach (var leg in d.Legs)
        {
            var u = Dir(leg.AngleDeg);
            parts.AddRange(PolygonOps.Strip(new[] { c + u * (ro * 0.5), c + u * (zoneR + 5) }, Math.Max(3, leg.Width)));
        }
        var full = PolygonOps.Union(parts);
        var re = Math.Max(0, d.EntryRadius);
        if (re > 0.05) full = PolygonOps.Offset(PolygonOps.Offset(full, re, true), -re, true);
        full = PolygonOps.Intersect(full, zone);

        // Ilhas separadoras (gota) em cada ramo.
        var splitters = new List<Polygon2>();
        foreach (var leg in d.Legs)
        {
            var u = Dir(leg.AngleDeg);
            var n = u.PerpLeft;
            Polygon2? sp = null;
            if (d.SplitterIslands && leg.Width >= 6)
            {
                var w0 = Math.Min(d.SplitterWidth, leg.Width - 6.0);
                if (w0 >= 0.8)
                {
                    var t0 = ro + 1.0;
                    var t1 = ro + Math.Max(3, d.SplitterLength);
                    var tri = new Polygon2(new[] { c + u * t0 - n * (w0 / 2), c + u * t1 - n * 0.3, c + u * t1 + n * 0.3, c + u * t0 + n * (w0 / 2) });
                    sp = PolygonOps.Offset(PolygonOps.Offset(new[] { tri }, -0.25, true), 0.25, true).OrderByDescending(p => p.Area).FirstOrDefault();
                    if (sp != null) splitters.Add(sp);
                }
                else L.Warnings.Add($"Ramo a {leg.AngleDeg:0}°: pista estreita para ilha separadora (mínimo ~7 m).");
            }
            L.Legs.Add(new RoundaboutLegGeometry(leg, u, n, sp));
        }

        var island = Disk(c, ri);
        var apron = PolygonOps.Difference(new[] { Disk(c, rApron) }, new[] { island });
        var pav = PolygonOps.Difference(full, splitters.Append(Disk(c, rApron)));
        L.Pavement.AddRange(pav);
        L.Apron.AddRange(apron);
        var core = PolygonOps.Offset(new[] { island }, -cw);
        L.IslandCurb.AddRange(PolygonOps.Difference(new[] { island }, core));
        L.IslandCore.AddRange(core);
        foreach (var sp in splitters)
        {
            var sc = PolygonOps.Offset(new[] { sp }, -cw);
            L.SplitterCurb.AddRange(PolygonOps.Difference(new[] { sp }, sc));
            L.SplitterCore.AddRange(sc);
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
        Raised(L.Apron, MarkingColor.Bloquete, 0.06);                       // galgável: bloquete elevado 6 cm
        Raised(L.IslandCurb, MarkingColor.Concreto, d.CurbHeight);
        Raised(L.IslandCore, MarkingColor.Grama, d.CurbHeight);
        Raised(L.SplitterCurb, MarkingColor.Concreto, d.CurbHeight);
        Raised(L.SplitterCore, MarkingColor.Concreto, d.CurbHeight);
        Raised(L.Curb, MarkingColor.Concreto, d.CurbHeight);
        Raised(L.Sidewalk, MarkingColor.Concreto, d.CurbHeight);

        if (d.Landscaping && d.IslandRadius >= 3 && ctx.Catalog.Movel("ARVORE") is { } tree)
        {
            var spots = new List<Vec2> { d.Center };
            var ring = d.IslandRadius * 0.55;
            if (d.IslandRadius >= 6)
                for (int i = 0; i < 5; i++) spots.Add(d.Center + Dir(90 + i * 72) * ring);
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

        if (d.Markings && d.Lanes > 1)
            for (int k = 1; k < d.Lanes; k++)
            {
                var r = d.IslandRadius + d.ApronWidth + k * d.LaneWidth;
                var pts = CurveTools.Circle(c, r, 0.02);
                Add(new LinearMarkingDefinition { Code = "LMS-1", PathRef = PathReference.FromPoints(pts, z, true) });
            }

        foreach (var g in L.Legs)
        {
            var hw = g.Leg.Width / 2;
            var inner = g.Splitter != null ? Math.Min(d.SplitterWidth, g.Leg.Width - 6) / 2 + 0.2 : 0.1;
            // Entrada = lado esquerdo do ramo (quem chega pela direita da pista circula no sentido anti-horário).
            if (d.Markings)
            {
                var o0 = inner;
                var o1 = hw - 0.3;
                double T(double o) => Math.Sqrt(Math.Max(0, (ro + 0.4) * (ro + 0.4) - o * o));
                Add(new LinearMarkingDefinition { Code = "LDP", PathRef = PathReference.FromPoints(new[] { g.At(c, T(o1), o1), g.At(c, T(o0), o0) }, z) });
                var tSym = ro + 4.0;
                Add(new SymbolMarkingDefinition { Code = "SDP", Length = 3.6, Position = g.At(c, tSym, (o0 + o1) / 2), Direction = -g.Dir, Z = z });
                if (g.Splitter != null)
                {
                    var t1 = ro + Math.Max(3, d.SplitterLength);
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
                var tc = ro + 7.0;
                var a = g.At(c, tc, hw - 0.3);
                var b = g.At(c, tc, -(hw - 0.3));
                var cwk = Add(new LinearMarkingDefinition { Code = "FTP-1", WidthOverride = 3.0, PathRef = PathReference.FromPoints(new[] { a, b }, z) });
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
                Add(new SignDefinition { Code = "R-2", Position = g.At(c, ro + 3.5, hw + 0.9), Direction = -g.Dir, Z = z, Width = 0.75 });
                if (g.Splitter != null)
                    Add(new SignDefinition { Code = "R-33", Position = g.At(c, ro + Math.Max(3, d.SplitterLength) * 0.45, 0), Direction = -g.Dir, Z = z });
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
