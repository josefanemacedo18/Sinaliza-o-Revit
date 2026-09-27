using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Um ramo do nó viário: eixo, greide, meia largura e seção do tabuleiro.</summary>
public sealed class Alignment
{
    public required string Name { get; init; }
    public required Polyline2 Path { get; init; }
    public required VerticalProfile Profile { get; init; }
    public required DeckSpec Spec { get; init; }
    /// <summary>Pista dupla com canteiro central (barreira dupla e faixas por sentido).</summary>
    public double Median { get; init; }
    public int LanesPerDirection { get; init; } = 1;
    public bool Closed { get; init; }
    public double Half => Spec.DeckHalf;
}

/// <summary>
/// Interseções em desnível (nós viários): diamante, diamante com rotatórias, trevo completo, trevo parcial, trombeta e
/// rotatória em dois níveis. Monta os ramos (via principal, transversal, rampas e laços) com greide, detecta onde um ramo
/// passa sobre outro (tabuleiro com vigas, pilares e encontros) e o resto vai em aterro com taludes – com barreiras,
/// faixas, iluminação e a terraplenagem para o Toposolid.
/// </summary>
/// <remarks>Referências: DNIT – Manual de Projeto de Interseções (2005): tipos de interconexão, raios mínimos de laço
/// (≈ 50 m para 40 km/h), rampas ≤ 5 % (ramos ascendentes), gabarito vertical 5,50 m.</remarks>
public static class InterchangeGenerator
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static List<Alignment> Layout(InterchangeDefinition d, Func<Vec2, double> ground, List<string> warnings)
    {
        var o = d.Position;
        var ex = Vec2.FromAngle(d.AngleDeg * Math.PI / 180);
        var ec = ex.Rotate(Math.Clamp(d.CrossAngleDeg, 45, 135) * Math.PI / 180);
        var ey = ex.PerpLeft;
        Vec2 W(double x, double y) => o + ex * x + ey * y;
        var nc = ec.PerpRight;                                   // normal da transversal apontando para +x
        var H = d.Height;
        var g = Math.Max(0.01, d.RampGrade);
        var mainHalf = d.MainWidth / 2;
        var crossHalf = d.CrossWidth / 2;
        var rampHalf = Math.Max(2.5, d.RampWidth / 2);
        var list = new List<Alignment>();
        // Greides referidos à base do nó (plano): o terreno é que se ajusta a ele na terraplenagem – estável ao regenerar.
        var groundAt = (Func<Vec2, double>)(_ => 0.0);

        DeckSpec Deck(double roadHalf, int lanes, bool twoWay) => new()
        {
            RoadHalf = roadHalf, Lanes = lanes, TwoWay = twoWay, System = SistemaEstrutural.VigasPreMoldadas, PierType = TipoPilar.Portico,
            PierSize = 1.2, SpanLength = 30, Lighting = false, Markings = d.Markings, Barrier = TipoGuarda.NewJersey,
            GirderDepth = Math.Max(1.0, d.DeckDepth - 0.4),
        };

        // ---- vias principal e transversal
        var upperMain = !d.MainBelow;
        var R = Math.Max(30, d.LoopRadius);
        // A principal precisa alcançar o início das rampas (rampa de subida + transição + entrelaçamento).
        var reach = crossHalf + H / g + 110 + 80 + (d.Type == TipoNoViario.TrevoCompleto ? 2 * R + 30 : 0);
        var mainLen = Math.Max(Math.Max(200, d.MainLength), 2 * reach);
        var crossLen = Math.Max(Math.Max(160, d.CrossLength), 2 * (H / g + 60 + (d.Type == TipoNoViario.TrevoCompleto ? 2 * R : 0) + mainHalf));
        if (mainLen > d.MainLength + 1 || crossLen > d.CrossLength + 1)
            warnings.Add($"Extensões ajustadas para caber as rampas: principal {mainLen.ToString("0", Pt)} m, transversal {crossLen.ToString("0", Pt)} m.");
        var trumpet = d.Type == TipoNoViario.Trombeta;
        var mainPath = new Polyline2(new[] { W(-mainLen / 2, 0), W(mainLen / 2, 0) });
        var crossStart = trumpet ? mainHalf + 20 : -crossLen / 2;
        var crossPath = new Polyline2(new[] { o + ec * crossStart, o + ec * (crossLen / 2) });
        VerticalProfile Over(Polyline2 p, double center)
        {
            var L = p.Length;
            var z0 = groundAt(p.PointAt(0));
            var z1 = groundAt(p.PointAt(L));
            var rin = Math.Abs(H - z0) / g;
            var rout = Math.Abs(H - z1) / g;
            var flat = 2 * (Math.Max(mainHalf, crossHalf) + 25);
            var a = Math.Max(0, center - flat / 2 - rin);
            var b = Math.Min(L, center + flat / 2 + rout);
            var prof = new VerticalProfile { CurveLength = 60 };
            prof.Pvis.Add((0, z0));
            if (a > 1) prof.Pvis.Add((a, z0));
            prof.Pvis.Add((Math.Max(a + 1, center - flat / 2), H));
            prof.Pvis.Add((Math.Min(b - 1, center + flat / 2), H));
            if (b < L - 1) prof.Pvis.Add((b, z1));
            prof.Pvis.Add((L, z1));
            if (a <= 0 || b >= L) warnings.Add("Via curta para as rampas de acesso ao viaduto – aumente a extensão da via.");
            return prof;
        }
        var mainProf = upperMain ? Over(mainPath, mainLen / 2) : VerticalProfile.Flat(0);
        var crossCenter = -crossStart;
        var crossProf = upperMain ? VerticalProfile.Flat(0) : trumpet ? TrumpetStem(crossPath, H, groundAt, g) : Over(crossPath, crossCenter);
        list.Add(new Alignment
        {
            Name = "Via principal", Path = mainPath, Profile = mainProf, Median = d.MainMedian, LanesPerDirection = d.MainLanes,
            Spec = Deck(mainHalf - 0.46, 2 * d.MainLanes, false),
        });
        if (d.Type == TipoNoViario.DiamanteRotatorias)
        {
            // A transversal é interrompida nas rotatórias dos terminais.
            var rr0 = Math.Max(15, d.RoundaboutRadius) + 4.5 + 2;
            var dd = Math.Max(crossHalf + mainHalf + 40, d.TerminalDistance);
            var cut = new[] { (0.0, crossCenter - dd - rr0), (crossCenter - dd + rr0, crossCenter + dd - rr0), (crossCenter + dd + rr0, crossPath.Length) };
            foreach (var (a, b) in cut)
                if (b - a > 5) list.Add(new Alignment
                {
                    Name = "Via transversal", Path = new Polyline2(new[] { crossPath.PointAt(a), crossPath.PointAt(b) }), Profile = Sub(crossProf, a, b),
                    LanesPerDirection = d.CrossLanes, Spec = Deck(crossHalf - 0.46, 2 * d.CrossLanes, true),
                });
        }
        else if (d.Type != TipoNoViario.RotatoriaElevada)
            list.Add(new Alignment
            {
                Name = "Via transversal", Path = crossPath, Profile = crossProf, LanesPerDirection = d.CrossLanes,
                Spec = Deck(crossHalf - 0.46, 2 * d.CrossLanes, true),
            });
        double MainZ(Vec2 p) => mainProf.Z(IntersectionGenerator.Project(mainPath, p).Station);
        double CrossZ(Vec2 p) => crossProf.Z(IntersectionGenerator.Project(crossPath, p).Station);

        // ---- ramos
        Alignment Ramp(string name, IReadOnlyList<Vec2> pts, double fillet, double zStart, double zEnd, double flatStart = 60, double flatEnd = 15, bool closed = false)
        {
            var smooth = CurveTools.FilletCorners(pts, fillet);
            var path = new Polyline2(smooth);
            var L = path.Length;
            var prof = new VerticalProfile { CurveLength = 40 };
            var a = Math.Min(flatStart, L * 0.3);
            var b = Math.Max(a + 1, L - Math.Min(flatEnd, L * 0.2));
            prof.Pvis.Add((0, zStart));
            prof.Pvis.Add((a, zStart));
            prof.Pvis.Add((b, zEnd));
            prof.Pvis.Add((L, zEnd));
            var grade = Math.Abs(zEnd - zStart) / Math.Max(1, b - a);
            if (grade > g * 1.25) warnings.Add($"{name}: rampa de {(grade * 100).ToString("0.0", Pt)} % (máx. {(g * 100).ToString("0.0", Pt)} %).");
            return new Alignment { Name = name, Path = path, Profile = prof, Spec = Deck(rampHalf - 0.46, 1, false), Closed = closed };
        }
        var gap = 6.0;
        // Rampa diagonal (diamante) no quadrante (qx, qy): diverge da principal (lado qy) longe do centro e chega à transversal
        // na estação qy·D, pelo lado qx.
        Alignment Diamond(int qx, int qy, double D, double extraOut = 0, Vec2? terminal = null, double? terminalZ = null)
        {
            var tz = terminalZ ?? (upperMain ? 0.0 : crossProf.Z(crossCenter + qy * D));
            var mz = upperMain ? H : 0.0;
            var rise = Math.Abs(tz - mz);
            var xd = crossHalf + rise / g + 110 + extraOut;
            var start = W(qx * xd, qy * (mainHalf + rampHalf + 0.5));
            var along = W(qx * (xd - 70), qy * (mainHalf + rampHalf + gap + extraOut * 0.3));
            var t = terminal ?? o + ec * (qy * D) + nc * (qx * (crossHalf + rampHalf + 0.5));
            var side = terminal == null ? nc * qx : (t - o).Normalized();
            var near = t + side * 28;
            return Ramp($"Rampa {(qx > 0 ? "L" : "O")}{(qy > 0 ? "N" : "S")}", new[] { start, along, near, t }, 45, upperMain ? MainZ(start) : 0, tz);
        }
        // Laço (trevo) no quadrante (qx, qy): 270° tangente às bordas das duas vias.
        Alignment Loop(int qx, int qy)
        {
            var e1 = mainHalf + gap + rampHalf;
            var e2 = crossHalf + gap + rampHalf;
            // Centro: afastado R+e1 da principal e R+e2 da transversal (em coordenadas locais).
            var yLoc = qy * (R + e1);
            var ncLoc = new Vec2(nc.Dot(ex), nc.Dot(ey));
            var xLoc = (qx * (R + e2) - yLoc * ncLoc.Y) / ncLoc.X;
            var c = W(xLoc, yLoc);
            var tm = c - ey * (qy * R);                                 // tangente junto à principal
            var tc = c - nc * (qx * R);                                 // tangente junto à transversal
            var am = Math.Atan2((tm - c).Y, (tm - c).X);
            var ac = Math.Atan2((tc - c).Y, (tc - c).X);
            var sweep = ac - am;
            while (sweep <= -Math.PI) sweep += 2 * Math.PI;
            while (sweep > Math.PI) sweep -= 2 * Math.PI;
            // O laço dá a volta pelo lado de fora (≈ 270°).
            sweep = sweep > 0 ? sweep - 2 * Math.PI : sweep + 2 * Math.PI;
            var arc = CurveTools.Arc(c, R, am, sweep, 0.05);
            var tanStart = (arc[1] - arc[0]).Normalized();
            var tanEnd = (arc[^1] - arc[^2]).Normalized();
            var pts = new List<Vec2> { arc[0] - tanStart * 90 };
            pts.AddRange(arc);
            pts.Add(arc[^1] + tanEnd * 45);
            var path = new Polyline2(pts);
            var L = path.Length;
            var zs = upperMain ? H : 0.0;
            var ze = upperMain ? 0.0 : CrossZ(pts[^1]);
            var prof = new VerticalProfile { CurveLength = 40 };
            prof.Pvis.Add((0, zs));
            prof.Pvis.Add((70, zs));
            prof.Pvis.Add((L - 30, ze));
            prof.Pvis.Add((L, ze));
            return new Alignment { Name = $"Laço {(qx > 0 ? "L" : "O")}{(qy > 0 ? "N" : "S")}", Path = path, Profile = prof, Spec = Deck(rampHalf - 0.46, 1, false) };
        }

        var D = Math.Max(crossHalf + mainHalf + 40, d.TerminalDistance);
        switch (d.Type)
        {
            case TipoNoViario.Diamante:
                foreach (var (qx, qy) in Quadrants) list.Add(Diamond(qx, qy, D));
                break;
            case TipoNoViario.DiamanteRotatorias:
            {
                var rr = Math.Max(15, d.RoundaboutRadius);
                foreach (var qy in new[] { -1, 1 })
                {
                    var c = o + ec * (qy * D);
                    var zr = crossProf.Z(crossCenter + qy * D);
                    list.Add(Ring($"Rotatória {(qy > 0 ? "N" : "S")}", c, rr, zr));
                    foreach (var qx in new[] { -1, 1 })
                    {
                        var dir = (nc * qx + ec * (qy * 0.6)).Normalized();
                        list.Add(Diamond(qx, qy, D, 0, c + dir * (rr + rampHalf + 1), zr));
                    }
                }
                break;
            }
            case TipoNoViario.TrevoCompleto:
                foreach (var (qx, qy) in Quadrants)
                {
                    list.Add(Loop(qx, qy));
                    list.Add(Diamond(qx, qy, D + 2 * R + 20, 2 * R + 30));
                }
                break;
            case TipoNoViario.TrevoParcial:
                list.Add(Loop(-1, 1));
                list.Add(Loop(1, -1));
                list.Add(Diamond(1, 1, D));
                list.Add(Diamond(-1, -1, D));
                break;
            case TipoNoViario.Trombeta:
            {
                // Laço (+x,+y) da haste à principal, rampa semidireta que cruza a principal para o lado sul e rampa externa.
                list.Add(Loop(1, 1));
                var stemEnd = o + ec * crossStart;
                var x1 = -(crossHalf + 40);
                var endX = x1 - 60 - H / g - 60;
                var pts = new[] { stemEnd, stemEnd + ec * -10 + nc * -30, W(x1, mainHalf + 25), W(x1 - 30, -mainHalf - rampHalf - 8), W(endX, -mainHalf - rampHalf - 0.5) };
                var semi = Ramp("Rampa semidireta", pts, 60, crossProf.Z(0), 0, 40, 60);
                // A semidireta sobe até cruzar a principal e só depois desce.
                var sp = new VerticalProfile { CurveLength = 50 };
                var Ls = semi.Path.Length;
                var sCross = IntersectionGenerator.Project(semi.Path, W(x1 - 15, 0)).Station;
                sp.Pvis.Add((0, crossProf.Z(0)));
                sp.Pvis.Add((Math.Min(Ls * 0.8, sCross + mainHalf + 15), H));
                sp.Pvis.Add((Ls, 0));
                list.Add(new Alignment { Name = semi.Name, Path = semi.Path, Profile = sp, Spec = semi.Spec });
                list.Add(Diamond(1, 1, crossStart + 2 * R + 60, 2 * R + 30));
                break;
            }
            case TipoNoViario.RotatoriaElevada:
            {
                var rr = Math.Max(d.RoundaboutRadius, mainHalf + 18);
                list.Add(Ring("Rotatória elevada", o, rr, H));
                // Pernas da transversal descendo do anel.
                foreach (var qy in new[] { -1, 1 })
                {
                    var a = o + ec * (qy * (rr + 4));
                    var b = o + ec * (qy * crossLen / 2);
                    var leg = new Polyline2(new[] { a, b });
                    var L = leg.Length;
                    var zEnd = groundAt(b);
                    var prof = new VerticalProfile { CurveLength = 50 };
                    prof.Pvis.Add((0, H));
                    prof.Pvis.Add((15, H));
                    prof.Pvis.Add((Math.Min(L - 5, 15 + Math.Abs(H - zEnd) / g), zEnd));
                    prof.Pvis.Add((L, zEnd));
                    list.Add(new Alignment { Name = $"Via transversal {(qy > 0 ? "N" : "S")}", Path = leg, Profile = prof, Spec = Deck(crossHalf - 0.46, 2 * d.CrossLanes, true) });
                }
                foreach (var (qx, qy) in Quadrants)
                {
                    var dir = (ex * qx + ec * qy).Normalized();
                    list.Add(Diamond(qx, qy, rr, 0, o + dir * (rr + rampHalf + 2), H));
                }
                break;
            }
        }
        return list;

        Alignment Ring(string name, Vec2 c, double r, double z)
        {
            var pts = CurveTools.Circle(c, r, 0.05);
            pts.Add(pts[0]);
            return new Alignment { Name = name, Path = new Polyline2(pts), Profile = VerticalProfile.Flat(z), Spec = Deck(4.5, 1, false), Closed = true };
        }
    }

    private static readonly (int, int)[] Quadrants = { (1, 1), (-1, 1), (-1, -1), (1, -1) };

    /// <summary>Trecho [a, b] de um greide, reamostrado a partir de 0.</summary>
    private static VerticalProfile Sub(VerticalProfile p, double a, double b)
    {
        var r = new VerticalProfile();
        for (var s = a; s < b; s += 5) r.Pvis.Add((s - a, p.Z(s)));
        r.Pvis.Add((b - a, p.Z(b)));
        return r;
    }

    private static VerticalProfile TrumpetStem(Polyline2 stem, double h, Func<Vec2, double> ground, double g)
    {
        var L = stem.Length;
        var z1 = ground(stem.PointAt(L));
        var prof = new VerticalProfile { CurveLength = 60 };
        prof.Pvis.Add((0, h));
        prof.Pvis.Add((20, h));
        prof.Pvis.Add((Math.Min(L - 5, 20 + Math.Abs(h - z1) / g), z1));
        prof.Pvis.Add((L, z1));
        return prof;
    }

    /// <summary>Trechos (estações) em que o ramo passa sobre outro com gabarito, ou em que o aterro ficaria alto demais.</summary>
    public static List<(double S0, double S1)> StructureRanges(Alignment a, IReadOnlyList<Alignment> all, Func<Vec2, double> ground, double clearance)
    {
        var L = a.Path.Length;
        var marks = new List<double>();
        for (var s = 0.0; s <= L; s += 2)
        {
            var p = a.Path.PointAt(s);
            var z = a.Profile.Z(s);
            var over = false;
            foreach (var b in all)
            {
                if (ReferenceEquals(a, b)) continue;
                var (st, dist, _) = IntersectionGenerator.Project(b.Path, p);
                if (dist > b.Half + a.Half + 2) continue;
                if (z - b.Profile.Z(st) >= clearance * 0.7) { over = true; break; }
            }
            if (over || z - ground(p) > 9.0) marks.Add(s);
        }
        var ranges = new List<(double, double)>();
        foreach (var s in marks)
        {
            if (ranges.Count > 0 && s - ranges[^1].Item2 <= 8) ranges[^1] = (ranges[^1].Item1, s);
            else ranges.Add((s, s));
        }
        return ranges.Select(r => (Math.Max(0, r.Item1 - 8), Math.Min(L, r.Item2 + 8))).Where(r => r.Item2 - r.Item1 > 4).ToList();
    }

    public static MarkingGeometry Build(InterchangeDefinition d, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        Func<Vec2, double> ground = d.FollowTerrain ? ctx.GroundAt : _ => 0;
        var warnings = new List<string>();
        var list = Layout(d, ground, warnings);
        geo.Warnings.AddRange(warnings.Distinct());
        double area = 0;
        foreach (var a in list)
        {
            var L = a.Path.Length;
            var ranges = StructureRanges(a, list, ground, d.Clearance);
            // Trechos em aterro/corte entre as obras de arte.
            var cursor = 0.0;
            foreach (var (s0, s1) in ranges)
            {
                if (s0 - cursor > 0.5) Segment(geo, a, cursor, s0, ground, d);
                BridgeGenerator.Structure(geo, a.Path, a.Profile, a.Spec with { Markings = false, Lighting = false }, s0, s1, ground);
                cursor = s1;
            }
            if (L - cursor > 0.5) Segment(geo, a, cursor, L, ground, d);
            if (d.Markings) Markings(geo, a);
            if (a.Median > 0.2)
            {
                Infra.NewJersey(geo, a.Path, a.Profile, 0, 1, 0, L);
                Infra.NewJersey(geo, a.Path, a.Profile, 0, -1, 0, L);
                if (d.Lighting) Infra.Lights(geo, a.Path, a.Profile, 0, 0, L, 40, true, 12, 0.81);
            }
            else if (d.Lighting && !a.Closed) Infra.Lights(geo, a.Path, a.Profile, a.Half - 0.23, 0, L, 40, false, 10, 0.81);
            area += L * 2 * a.Half;
            if (a.Closed) Island(geo, a, d, list, ground);
        }
        geo.UnitCount = 1;
        geo.PathLength = list.Sum(a => a.Path.Length);
        geo.PaintedLength = geo.PathLength;
        BridgeGenerator.Measure(geo, MarkingColor.Asfalto, area);
        return geo;
    }

    private static void Segment(MarkingGeometry geo, Alignment a, double s0, double s1, Func<Vec2, double> ground, InterchangeDefinition d)
    {
        var spec = a.Spec with { Markings = false };
        BridgeGenerator.Approach(geo, a.Path, a.Profile, spec, s0, s1, ground, d.FillSlope, false);
    }

    private static void Markings(MarkingGeometry geo, Alignment a)
    {
        var L = a.Path.Length;
        var half = a.Spec.RoadHalf;
        if (a.Median > 0.2)
        {
            var m = a.Median / 2 + 0.46;
            Infra.LaneMarkings(geo, a.Path, a.Profile, -half, -m, a.LanesPerDirection, 0, L, false);
            Infra.LaneMarkings(geo, a.Path, a.Profile, m, half, a.LanesPerDirection, 0, L, false);
        }
        else Infra.LaneMarkings(geo, a.Path, a.Profile, -half, half, Math.Max(1, a.Spec.Lanes), 0, L, a.Spec.TwoWay);
    }

    /// <summary>Ilha central da rotatória (aterro gramado); na rotatória elevada, duas ilhas deixando a principal passar.</summary>
    private static void Island(MarkingGeometry geo, Alignment ring, InterchangeDefinition d, IReadOnlyList<Alignment> all, Func<Vec2, double> ground)
    {
        var pts = ring.Path.Points;
        var c = new Vec2(pts.Average(p => p.X), pts.Average(p => p.Y));
        var r = pts.Average(p => p.DistanceTo(c)) - ring.Half - 0.3;
        if (r < 3) return;
        var z = ring.Profile.Z(0);
        var disk = new Polygon2(CurveTools.Circle(c, r, 0.1));
        var main = all.FirstOrDefault(x => x.Median > 0.2);
        var pieces = new List<Polygon2> { disk };
        if (main != null && IntersectionGenerator.Project(main.Path, c).Distance < r)
            pieces = PolygonOps.Difference(pieces, PolygonOps.Strip(main.Path.Points, 2 * (main.Half + 6)));
        foreach (var part in pieces)
        {
            var g = ground(part.Centroid);
            var hull = part.Outer.ToList();
            if (z - g > 0.4) SolidSweep.Add(geo, Polyhedron.Prism(hull, _ => g, _ => z - 0.10), MarkingColor.Terra, "ILHA");
            SolidSweep.Add(geo, Polyhedron.Prism(hull, _ => z - 0.10, _ => z + 0.15), MarkingColor.Grama, "ILHA");
            geo.Pads.Add(new GradePad(part, z - 0.10) { FillSlope = d.FillSlope, Daylight = false, Label = "Ilha" });
        }
    }
}
