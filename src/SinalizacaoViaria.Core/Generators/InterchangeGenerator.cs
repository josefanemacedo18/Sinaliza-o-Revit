using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Papel de um ramo no nó viário.</summary>
public enum PapelRamo { Principal, Transversal, Rampa, Laco, Externo, Anel }

/// <summary>Um ramo do nó viário: eixo, greide, seção do tabuleiro e sentido de tráfego.</summary>
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
    public PapelRamo Role { get; init; } = PapelRamo.Rampa;
    /// <summary>Ramo ligado à via principal no início (saída) ou no fim (entrada) – nariz com zebrado.</summary>
    public bool MainAtStart { get; init; }
    public bool MainAtEnd { get; init; }
    public double Half => Spec.DeckHalf;
}

/// <summary>
/// Interseções em desnível (nós viários): diamante, diamante com rotatórias, trevo completo, trevo parcial, trombeta e
/// rotatória em dois níveis. Ramos com traçado de clotoides (curvatura contínua), laços de 270° tangentes às faixas
/// auxiliares em quadrantes separados, ramos externos contornando os laços, superelevação, faixas de mudança de
/// velocidade, zebrados nos narizes, setas, defensas, pórticos de sinalização, iluminação (postes e torres) e a
/// terraplenagem para o Toposolid.
/// </summary>
/// <remarks>Referências: DNIT – Manual de Projeto de Interseções (2005) e Manual de Projeto Geométrico: laços com R ≥ 50 m
/// (40 km/h), rampas ≤ 5 % nos ramos ascendentes, superelevação ≤ 8 %, espirais de transição, gabarito vertical 5,50 m;
/// CONTRAN/MBST Vol. IV (zebrado de canalização) e Vol. I (sinalização indicativa).</remarks>
public static class InterchangeGenerator
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly (int, int)[] Quadrants = { (1, 1), (-1, 1), (-1, -1), (1, -1) };

    public static List<Alignment> Layout(InterchangeDefinition d, Func<Vec2, double> ground, List<string> warnings)
    {
        var o = d.Position;
        var ex = Vec2.FromAngle(d.AngleDeg * Math.PI / 180);
        var ey = ex.PerpLeft;
        var ec = ex.Rotate(Math.Clamp(d.CrossAngleDeg, 45, 135) * Math.PI / 180);
        var nc = ec.PerpRight;                                   // normal da transversal apontando para +x
        Vec2 W(double x, double y) => o + ex * x + ey * y;
        Vec2 C(double t, double off) => o + ec * t + nc * off;
        var H = d.Height;
        var g = Math.Max(0.01, d.RampGrade);
        var mainHalf = d.MainWidth / 2;
        var crossHalf = d.CrossWidth / 2;
        var rampHalf = Math.Max(2.5, d.RampWidth / 2);
        var eo = mainHalf + rampHalf;                            // eixo da faixa auxiliar junto à principal
        var e2 = crossHalf + rampHalf;                           // idem junto à transversal
        var R = Math.Max(30, d.LoopRadius);
        var Rr = Math.Max(60, d.RampRadius);
        var ls = Math.Max(0, d.SpiralLength);
        var upperMain = !d.MainBelow;
        var list = new List<Alignment>();
        const double rampShoulders = 0.5;

        DeckSpec Deck(double roadHalf, int lanes, bool twoWay, double super = 0) => new()
        {
            RoadHalf = roadHalf, Lanes = lanes, TwoWay = twoWay, System = SistemaEstrutural.VigasPreMoldadas, PierType = TipoPilar.Circular,
            PierSize = 1.3, SpanLength = 30, Lighting = false, Markings = d.Markings, Barrier = TipoGuarda.NewJersey,
            GirderDepth = Math.Max(1.0, d.DeckDepth - 0.4), Superelevation = super, Drains = true, WingWalls = TipoAla.Paralelas,
        };

        // ---- principal e transversal
        var loops = d.Type is TipoNoViario.TrevoCompleto or TipoNoViario.TrevoParcial;
        var reach = crossHalf + H / g + d.SpeedChangeLength + 160 + (loops ? 2.6 * R + 60 : 0);
        var mainLen = Math.Max(Math.Max(200, d.MainLength), 2 * reach);
        var crossLen = Math.Max(Math.Max(160, d.CrossLength), 2 * (H / g + 90 + (loops ? 2.6 * R + 40 : 0) + mainHalf));
        if (mainLen > d.MainLength + 1 || crossLen > d.CrossLength + 1)
            warnings.Add($"Extensões ajustadas para caber os ramos: principal {mainLen.ToString("0", Pt)} m, transversal {crossLen.ToString("0", Pt)} m.");
        var trumpet = d.Type == TipoNoViario.Trombeta;
        var mainPath = new Polyline2(new[] { W(-mainLen / 2, 0), W(mainLen / 2, 0) });
        var crossStart = trumpet ? mainHalf + 14 : -crossLen / 2;
        var crossPath = new Polyline2(new[] { o + ec * crossStart, o + ec * (crossLen / 2) });
        VerticalProfile Over(Polyline2 p, double center, double halfFlat)
        {
            var L = p.Length;
            var run = H / g;
            var a = Math.Max(0, center - halfFlat - run);
            var b = Math.Min(L, center + halfFlat + run);
            var prof = new VerticalProfile { CurveLength = 70 };
            prof.Pvis.Add((0, 0));
            if (a > 1) prof.Pvis.Add((a, 0));
            prof.Pvis.Add((Math.Max(a + 1, center - halfFlat), H));
            prof.Pvis.Add((Math.Min(b - 1, center + halfFlat), H));
            if (b < L - 1) prof.Pvis.Add((b, 0));
            prof.Pvis.Add((L, 0));
            if (a <= 0 || b >= L) warnings.Add("Via curta para as rampas de acesso ao viaduto – aumente a extensão da via.");
            return prof;
        }
        var crossCenter = -crossStart;
        var mainProf = upperMain ? Over(mainPath, mainLen / 2, crossHalf + 30) : VerticalProfile.Flat(0);
        var crossProf = upperMain ? VerticalProfile.Flat(0) : trumpet ? TrumpetStem(crossPath, H, g) : Over(crossPath, crossCenter, mainHalf + 25);
        double MainZ(Vec2 p) => mainProf.Z(IntersectionGenerator.Project(mainPath, p).Station);
        double CrossZ(Vec2 p) => crossProf.Z(IntersectionGenerator.Project(crossPath, p).Station);

        list.Add(new Alignment
        {
            Name = "Via principal", Path = mainPath, Profile = mainProf, Median = d.MainMedian, LanesPerDirection = d.MainLanes, Role = PapelRamo.Principal,
            Spec = Deck(mainHalf - 0.46, 2 * d.MainLanes, false),
        });
        void AddCross(Polyline2 path, VerticalProfile prof, string name = "Via transversal") => list.Add(new Alignment
        {
            Name = name, Path = path, Profile = prof, LanesPerDirection = d.CrossLanes, Role = PapelRamo.Transversal,
            Spec = Deck(crossHalf - 0.46, 2 * d.CrossLanes, true) with { SpanLength = Math.Max(20, mainHalf + 8) },
        });
        if (d.Type == TipoNoViario.DiamanteRotatorias)
        {
            var rr0 = Math.Max(15, d.RoundaboutRadius) + 4.5 + 2;
            var dd = Math.Max(crossHalf + mainHalf + 40, d.TerminalDistance);
            foreach (var (a, b) in new[] { (0.0, crossCenter - dd - rr0), (crossCenter - dd + rr0, crossCenter + dd - rr0), (crossCenter + dd + rr0, crossPath.Length) })
                if (b - a > 5) AddCross(new Polyline2(new[] { crossPath.PointAt(a), crossPath.PointAt(b) }), Sub(crossProf, a, b));
        }
        else if (d.Type != TipoNoViario.RotatoriaElevada) AddCross(crossPath, crossProf);

        // ---- construtores de ramos
        Alignment Ramp(string name, PapelRamo role, List<Pi> pis, bool fromMain, double zStart, double zEnd, double flatStart, double flatEnd, double super,
            bool mainAtStart, bool mainAtEnd)
        {
            var path = Tracado.Build(pis, 0.75, warnings);
            var L = path.Length;
            var prof = new VerticalProfile { CurveLength = 45 };
            var a = Math.Min(flatStart, L * 0.35);
            var b = Math.Max(a + 1, L - Math.Min(flatEnd, L * 0.25));
            prof.Pvis.Add((0, zStart));
            if (a > 0.5) prof.Pvis.Add((a, zStart));
            if (b < L - 0.5) prof.Pvis.Add((b, zEnd));
            prof.Pvis.Add((L, zEnd));
            var grade = Math.Abs(zEnd - zStart) / Math.Max(1, b - a);
            if (grade > g * 1.25) warnings.Add($"{name}: rampa de {(grade * 100).ToString("0.0", Pt)} % (máx. {(g * 100).ToString("0.0", Pt)} %).");
            _ = fromMain;
            return new Alignment
            {
                Name = name, Path = path, Profile = prof, Role = role, MainAtStart = mainAtStart, MainAtEnd = mainAtEnd,
                Spec = Deck(rampHalf - rampShoulders, 1, false, super) with { SpanLength = 25, PierType = TipoPilar.Circular, PierSize = 1.1 },
            };
        }
        string Q(int qx, int qy) => (qx > 0 ? "L" : "O") + (qy > 0 ? "N" : "S");
        // Tráfego pela direita: na pista do lado qy o sentido é +x se qy < 0.
        int Travel(int qy) => qy < 0 ? 1 : -1;
        var mainZ0 = upperMain ? H : 0.0;

        // Rampa diagonal (diamante): saída antes do cruzamento, entrada depois; terminal perpendicular à transversal.
        Alignment Diamond(int qx, int qy, double D, Vec2? terminal = null, double? terminalZ = null, Vec2? terminalDir = null)
        {
            var exit = qx * Travel(qy) < 0;
            var tz = terminalZ ?? (upperMain ? 0.0 : crossProf.Z(crossCenter + qy * D));
            var rise = Math.Abs(tz - mainZ0);
            var xa = crossHalf + rise / g + d.SpeedChangeLength + 110;
            var A = W(qx * (xa + 60), qy * eo);
            var B = W(qx * xa, qy * eo);
            var T = terminal ?? C(qy * D, qx * (crossHalf + 0.3));
            var tdir = terminalDir ?? nc * qx;
            var Cp = T + tdir * 45;
            var pis = new List<Pi> { new(A), new(B, Rr * 2.5, ls), new(Cp, Math.Max(40, Rr * 0.55), ls * 0.6), new(T) };
            if (!exit) pis.Reverse();
            var name = $"Rampa {(exit ? "de saída" : "de entrada")} {Q(qx, qy)}";
            return exit
                ? Ramp(name, PapelRamo.Rampa, pis, true, MainZ(A), tz, d.SpeedChangeLength + 60, 20, 0.02 * -qy * Travel(qy), true, false)
                : Ramp(name, PapelRamo.Rampa, pis, false, tz, MainZ(A), 20, d.SpeedChangeLength + 60, 0.02 * -qy * Travel(qy), false, true);
        }
        // Centro do laço no quadrante (qx, qy): tangente à faixa auxiliar da principal e à da transversal.
        Vec2 LoopCenter(int qx, int qy)
        {
            var yL = qy * (R + eo);
            var nx = nc.Dot(ex);
            var ny = nc.Dot(ey);
            var xL = (qx * (R + e2) - yL * ny) / nx;
            return W(xL, yL);
        }
        // Laço de 270° (sempre no sentido horário – tráfego pela direita).
        Alignment Loop(int qx, int qy)
        {
            var c = LoopCenter(qx, qy);
            var tm = c - ey * (qy * R);
            var tc = c - nc * (qx * R);
            var exit = qx * Travel(qy) > 0;                     // sai da principal depois do cruzamento
            var a0 = Math.Atan2((exit ? tm : tc).Y - c.Y, (exit ? tm : tc).X - c.X);
            var a1 = Math.Atan2((exit ? tc : tm).Y - c.Y, (exit ? tc : tm).X - c.X);
            var sweep = a1 - a0;
            while (sweep >= 0) sweep -= 2 * Math.PI;
            while (sweep < -2 * Math.PI) sweep += 2 * Math.PI;
            var arc = CurveTools.Arc(c, R, a0, sweep, 0.004);
            var mainTravel = ex * Travel(qy);
            var crossTravel = exit ? (tc - c).PerpRight.Normalized() : -(tc - c).PerpRight.Normalized();
            var pts = new List<Vec2>();
            if (exit)
            {
                // Faixa de desaceleração ao longo da principal, depois do viaduto.
                var start = tm - mainTravel * Math.Max(20, (tm - o).Dot(mainTravel) - (crossHalf + 20));
                pts.Add(start);
                pts.AddRange(arc);
                pts.Add(arc[^1] + (arc[^1] - arc[^2]).Normalized() * 40);
            }
            else
            {
                pts.Add(arc[0] - (arc[1] - arc[0]).Normalized() * 40);
                pts.AddRange(arc);
                var end = tm + mainTravel * Math.Max(20, -(tm - o).Dot(mainTravel) - (crossHalf + 20));
                pts.Add(end);
            }
            _ = crossTravel;
            var path = new Polyline2(pts);
            var L = path.Length;
            var zm = MainZ(tm);
            var zc = CrossZ(tc);
            var prof = new VerticalProfile { CurveLength = 40 };
            var (zs, ze) = exit ? (zm, zc) : (zc, zm);
            var fs = exit ? (tm - pts[0]).Length : 40;
            var fe = exit ? 40 : (pts[^1] - tm).Length;
            prof.Pvis.Add((0, zs));
            prof.Pvis.Add((Math.Min(L * 0.3, fs), zs));
            prof.Pvis.Add((Math.Max(L * 0.7, L - fe), ze));
            prof.Pvis.Add((L, ze));
            return new Alignment
            {
                Name = $"Laço {Q(qx, qy)}", Path = path, Profile = prof, Role = PapelRamo.Laco, MainAtStart = exit, MainAtEnd = !exit,
                Spec = Deck(rampHalf - rampShoulders, 1, false, Math.Abs(d.Superelevation)) with { SpanLength = 25, PierType = TipoPilar.Circular, PierSize = 1.1 },
            };
        }
        // Ramo externo (conversão à direita) contornando o laço do quadrante por fora.
        Alignment Outer(int qx, int qy, double gap = 22)
        {
            var c = LoopCenter(qx, qy);
            var u = (ey * qy + nc * qx).Normalized();
            var off = R + rampHalf + gap;
            var xA = (off - (o + ey * (qy * eo) - c).Dot(u)) / ex.Dot(u);
            var A = o + ex * xA + ey * (qy * eo);
            var tB = (off - (o + nc * (qx * e2) - c).Dot(u)) / ec.Dot(u);
            var B = o + nc * (qx * e2) + ec * tB;
            var A0 = A + ex * (Math.Sign(xA) * (d.SpeedChangeLength + 80));
            var B0 = B + ec * (Math.Sign(tB) * 90);
            var exit = qx * Travel(qy) < 0;                     // antes do cruzamento: sai da principal
            var pis = new List<Pi> { new(A0), new(A, Rr, ls), new(B, Rr, ls), new(B0) };
            if (!exit) pis.Reverse();
            var zA = MainZ(A0);
            var zB = CrossZ(B0);
            var name = $"Ramo externo {Q(qx, qy)}";
            return exit
                ? Ramp(name, PapelRamo.Externo, pis, true, zA, zB, d.SpeedChangeLength, 40, 0.04, true, false)
                : Ramp(name, PapelRamo.Externo, pis, false, zB, zA, 40, d.SpeedChangeLength, 0.04, false, true);
        }

        var D = Math.Max(crossHalf + mainHalf + 60, d.TerminalDistance);
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
                        var dir = (nc * qx + ec * (qy * 0.55)).Normalized();
                        list.Add(Diamond(qx, qy, D, c + dir * (rr + rampHalf + 1.5), zr, dir));
                    }
                }
                break;
            }
            case TipoNoViario.TrevoCompleto:
                foreach (var (qx, qy) in Quadrants)
                {
                    list.Add(Loop(qx, qy));
                    list.Add(Outer(qx, qy));
                }
                break;
            case TipoNoViario.TrevoParcial:
                // Laços (conversões à esquerda) e ramos externos em dois quadrantes; rampas diagonais nos outros dois.
                foreach (var (qx, qy) in new[] { (1, -1), (-1, 1) })
                {
                    list.Add(Loop(qx, qy));
                    list.Add(Outer(qx, qy));
                }
                list.Add(Diamond(-1, -1, D));
                list.Add(Diamond(1, 1, D));
                break;
            case TipoNoViario.Trombeta:
            {
                var stemEnd = o + ec * crossStart;
                var zStem = crossProf.Z(0);
                // Laço: haste → principal (sentido −x), no quadrante (+x, +y).
                list.Add(Loop(1, 1));
                // Ramo externo: principal (−x) → haste, contornando o laço.
                list.Add(Outer(1, 1));
                // Semidireta: principal (+x, pista sul) → haste, numa curva à esquerda que passa sobre a principal.
                var pivot = W(-(crossHalf + rampHalf + 4), -eo);
                var turnR = Math.Max(Rr, eo + mainHalf + 60);
                var pis = new List<Pi>
                {
                    new(W(-(mainLen / 2 - 40), -eo)), new(pivot + ex * -(turnR * 1.2 + d.SpeedChangeLength)), new(pivot, turnR, ls * 1.5),
                    new(stemEnd - nc * (crossHalf + rampHalf + 4) + ec * 20), new(stemEnd + ec * 60 - nc * (crossHalf + rampHalf + 4)),
                };
                var x1 = (pivot - o).Dot(ex);
                var semi = Ramp("Rampa semidireta", PapelRamo.Rampa, pis, true, 0, zStem, d.SpeedChangeLength, 20, 0.04, true, false);
                var sp = new VerticalProfile { CurveLength = 60 };
                var Ls = semi.Path.Length;
                var sCross = IntersectionGenerator.Project(semi.Path, W(x1, 0)).Station;
                sp.Pvis.Add((0, 0));
                sp.Pvis.Add((Math.Max(10, sCross - mainHalf - 10 - H / g), 0));
                sp.Pvis.Add((sCross - mainHalf - 10, H));
                sp.Pvis.Add((Math.Min(Ls - 20, sCross + mainHalf + 10), H));
                sp.Pvis.Add((Ls, zStem));
                list.Add(new Alignment { Name = semi.Name, Path = semi.Path, Profile = sp, Spec = semi.Spec, Role = semi.Role, MainAtStart = true });
                break;
            }
            case TipoNoViario.RotatoriaElevada:
            {
                var rr = Math.Max(d.RoundaboutRadius, mainHalf + 18);
                list.Add(Ring("Rotatória elevada", o, rr, H));
                foreach (var qy in new[] { -1, 1 })
                {
                    var a = o + ec * (qy * (rr + 4.5));
                    var b = o + ec * (qy * crossLen / 2);
                    var leg = new Polyline2(new[] { a, b });
                    var L = leg.Length;
                    var prof = new VerticalProfile { CurveLength = 50 };
                    prof.Pvis.Add((0, H));
                    prof.Pvis.Add((15, H));
                    prof.Pvis.Add((Math.Min(L - 5, 15 + H / g), 0));
                    prof.Pvis.Add((L, 0));
                    AddCross(leg, prof, $"Via transversal {(qy > 0 ? "N" : "S")}");
                }
                foreach (var (qx, qy) in Quadrants)
                {
                    var dir = (ex * qx + ec * qy).Normalized();
                    list.Add(Diamond(qx, qy, rr, o + dir * (rr + rampHalf + 2.5), H, dir));
                }
                break;
            }
        }
        return list;

        Alignment Ring(string name, Vec2 c, double r, double z)
        {
            var pts = CurveTools.Circle(c, r, 0.004);
            pts.Add(pts[0]);
            return new Alignment
            {
                Name = name, Path = new Polyline2(pts), Profile = VerticalProfile.Flat(z), Closed = true, Role = PapelRamo.Anel,
                Spec = Deck(4.5, 1, false, -0.02) with { SpanLength = 22, PierType = TipoPilar.Circular },
            };
        }
    }

    /// <summary>Trecho [a, b] de um greide, reamostrado a partir de 0.</summary>
    private static VerticalProfile Sub(VerticalProfile p, double a, double b)
    {
        var r = new VerticalProfile();
        for (var s = a; s < b; s += 5) r.Pvis.Add((s - a, p.Z(s)));
        r.Pvis.Add((b - a, p.Z(b)));
        return r;
    }

    private static VerticalProfile TrumpetStem(Polyline2 stem, double h, double g)
    {
        var L = stem.Length;
        var prof = new VerticalProfile { CurveLength = 60 };
        prof.Pvis.Add((0, h));
        prof.Pvis.Add((20, h));
        prof.Pvis.Add((Math.Min(L - 5, 20 + h / g), 0));
        prof.Pvis.Add((L, 0));
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
                if (dist > b.Half + a.Half + 3) continue;
                if (z - b.Profile.Z(st) >= clearance * 0.7) { over = true; break; }
            }
            if (over || z - ground(p) > 9.0 || a.Role == PapelRamo.Anel && z - ground(p) > 4) marks.Add(s);
        }
        if (a.Closed && marks.Count > 0 && marks.Count * 2 >= L - 2) return new List<(double, double)> { (0, L) };
        var ranges = new List<(double, double)>();
        foreach (var s in marks)
        {
            if (ranges.Count > 0 && s - ranges[^1].Item2 <= 8) ranges[^1] = (ranges[^1].Item1, s);
            else ranges.Add((s, s));
        }
        return ranges.Select(r => (Math.Max(0, r.Item1 - 10), Math.Min(L, r.Item2 + 10))).Where(r => r.Item2 - r.Item1 > 4).ToList();
    }

    // ================================================================== geometria

    public static MarkingGeometry Build(InterchangeDefinition d, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        Func<Vec2, double> ground = d.FollowTerrain ? ctx.GroundAt : _ => 0;
        var warnings = new List<string>();
        var list = Layout(d, ground, warnings);
        geo.Warnings.AddRange(warnings.Distinct());
        var main = list.First(a => a.Role == PapelRamo.Principal);
        double area = 0;
        foreach (var a in list)
        {
            var spec = a.Spec with { Native = ctx.NativeTerrain };
            var L = a.Path.Length;
            var surf = spec.Surface(a.Profile);
            var ranges = StructureRanges(a, list, ground, d.Clearance);
            var cursor = 0.0;
            foreach (var (s0, s1) in ranges)
            {
                if (s0 - cursor > 0.5) AtGrade(geo, a, spec, cursor, s0, ground, d);
                BridgeGenerator.Structure(geo, a.Path, a.Profile, spec with { Markings = false, Lighting = false, SpanLength = Math.Max(18, Math.Min(spec.SpanLength, (s1 - s0) / 2)) },
                    s0, s1, ground, d.FillSlope, s0 > 0.5, s1 < L - 0.5);
                cursor = s1;
            }
            if (L - cursor > 0.5) AtGrade(geo, a, spec, cursor, L, ground, d);
            if (d.Markings) Markings(geo, a, surf, d);
            if (a.Median > 0.2)
            {
                Infra.NewJerseyDouble(geo, a.Path, surf, 0, 0, L);
                if (d.Lighting)
                    for (var s = 20.0; s < L; s += 40)
                    {
                        var p = a.Path.PointAt(s);
                        var n = SolidSweep.Normal(a.Path, s);
                        var z = surf.Z(s, 0) + 0.81;
                        Infra.Pole(geo, p, z, 12, n, a.Path.TangentAt(s), 2.8);
                        Infra.Pole(geo, p, z, 12, -n, a.Path.TangentAt(s), 2.8, column: false);
                    }
            }
            else if (d.Lighting && !a.Closed)
                Infra.Lights(geo, a.Path, surf, a.Spec.RoadHalf + 0.8, 0, L, 38, false, 10, 0, inward: true);
            if (d.GoreMarkings && a.Role is PapelRamo.Rampa or PapelRamo.Laco or PapelRamo.Externo)
            {
                if (a.MainAtStart) Gore(geo, main, a, true);
                if (a.MainAtEnd) Gore(geo, main, a, false);
                Arrows(geo, a, surf);
            }
            if (d.Gantries && a.MainAtStart) Gantry(geo, main, a, d.Clearance);
            area += L * 2 * a.Half;
            if (a.Closed) Island(geo, a, d, list, ground, ctx.NativeTerrain);
            if (a.Role == PapelRamo.Laco) LoopInfield(geo, a, d, ground, ctx.NativeTerrain);
        }
        geo.UnitCount = 1;
        geo.PathLength = list.Sum(a => a.Path.Length);
        geo.PaintedLength = geo.PathLength;
        BridgeGenerator.Measure(geo, MarkingColor.Asfalto, area);
        return geo;
    }

    /// <summary>
    /// Trecho em aterro/corte: pavimento com superelevação, acostamentos, base, aterro (sem Toposolid), defensas metálicas
    /// nos aterros altos e a faixa de terraplenagem.
    /// </summary>
    private static void AtGrade(MarkingGeometry geo, Alignment a, DeckSpec spec, double s0, double s1, Func<Vec2, double> ground, InterchangeDefinition d)
    {
        var path = a.Path;
        var prof = a.Profile;
        var surf = spec.Surface(prof);
        var half = spec.DeckHalf;
        Infra.Pavement(geo, path, surf, -spec.RoadHalf - 0.4, spec.RoadHalf + 0.4, s0, s1);
        Infra.Base(geo, path, surf, -half, half, s0, s1);
        Infra.Embankment(geo, path, prof, -half, half, s0, s1, d.FillSlope, ground, native: spec.Native);
        if (d.Guardrails && a.Median < 0.2)
            foreach (var side in new[] { -1, 1 })
            {
                // Defensa onde o aterro passa de 1,5 m.
                var y = side * (spec.RoadHalf + 0.9);
                var runs = new List<(double, double)>();
                double? start = null;
                for (var s = s0; s <= s1 + 1e-6; s += 4)
                {
                    var high = prof.Z(s) - ground(path.PointAt(s) + SolidSweep.Normal(path, s) * y) > 1.5;
                    if (high && start == null) start = s;
                    if ((!high || s + 4 > s1) && start != null) { runs.Add((start.Value, Math.Min(s1, s))); start = null; }
                }
                foreach (var (r0, r1) in runs.Where(r => r.Item2 - r.Item1 > 8)) Guardrail(geo, path, surf, y, side, r0, r1);
            }
        geo.Corridors.Add(Infra.Corridor(path, prof, -half - 0.8, half + 0.8, s0, s1, Infra.Wearing + 0.35, 1.0, d.FillSlope, label: a.Name));
    }

    /// <summary>Defensa metálica (lâmina tripla onda) com postes a cada 2 m e terminais abatidos.</summary>
    public static void Guardrail(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double y, int side, double s0, double s1)
    {
        var face = -side;                                   // face para a pista
        SolidSweep.Along(geo, path, s =>
        {
            var z = surf.Z(s, y);
            var ramp = Math.Min(1, Math.Min(s - s0, s1 - s) / 8);   // terminais abatidos
            var zc = z + 0.25 + 0.30 * ramp;
            var w = new (double Q, double Z)[] { (0, -0.16), (0.06, -0.12), (0.03, -0.06), (0.06, 0), (0.03, 0.06), (0.06, 0.12), (0, 0.16), (-0.005, 0.16), (-0.005, -0.16) };
            return w.Select(q => new SectionPt(y + face * q.Q, zc + q.Z)).ToArray();
        }, MarkingColor.Metal, s0, s1, 2, "DEFENSA");
        for (var s = s0 + 1; s < s1 - 1; s += 2)
        {
            var p = path.PointAt(s) + SolidSweep.Normal(path, s) * (y - face * 0.10);
            var z = surf.Z(s, y);
            var ramp = Math.Min(1, Math.Min(s - s0, s1 - s) / 8);
            SolidSweep.Add(geo, SolidSweep.Box(p, path.TangentAt(s), 0.10, 0.15, z - 0.4, z + 0.25 + 0.30 * ramp + 0.12), MarkingColor.Metal, "DEFENSA");
        }
    }

    /// <summary>Faixas: principal com canteiro (duas pistas), demais com bordos e divisão; bordos tracejados nos ramos junto às saídas.</summary>
    private static void Markings(MarkingGeometry geo, Alignment a, DeckSurface surf, InterchangeDefinition d)
    {
        var L = a.Path.Length;
        var half = a.Spec.RoadHalf;
        if (a.Median > 0.2)
        {
            var m = a.Median / 2 + 0.46;
            Infra.LaneMarkings(geo, a.Path, surf, -half, -m, a.LanesPerDirection, 0, L, false);
            Infra.LaneMarkings(geo, a.Path, surf, m, half, a.LanesPerDirection, 0, L, false);
        }
        else if (a.Role == PapelRamo.Anel)
            Infra.LaneMarkings(geo, a.Path, surf, -half, half, 2, 0, L, false);
        else Infra.LaneMarkings(geo, a.Path, surf, -half, half, Math.Max(1, a.Spec.Lanes), 0, L, a.Spec.TwoWay);
    }

    /// <summary>Zebrado do nariz (MBST Vol. IV) entre a borda da principal e a do ramo, onde eles se separam.</summary>
    private static void Gore(MarkingGeometry geo, Alignment main, Alignment ramp, bool atStart)
    {
        var mh = main.Spec.DeckHalf - 0.40;
        var rh = ramp.Spec.RoadHalf;
        var L = ramp.Path.Length;
        var left = new List<Vec2>();
        var right = new List<Vec2>();
        var zs = new List<double>();
        for (var k = 0.0; k < Math.Min(L, 260); k += 1.0)
        {
            var s = atStart ? k : L - k;
            var p = ramp.Path.PointAt(s);
            var (ms, dist, mp) = IntersectionGenerator.Project(main.Path, p);
            var side = Math.Sign((p - mp).Dot(main.Path.TangentAt(ms).PerpLeft));
            var n = (p - mp).Normalized();
            var gap = dist - rh - mh;
            if (gap < 0.3) { if (left.Count > 0) break; continue; }
            if (gap > 3.6) break;
            left.Add(mp + n * mh);
            right.Add(p - n * rh);
            zs.Add(ramp.Profile.Z(s));
            _ = side;
        }
        if (left.Count < 4) return;
        var ring = left.Concat(Enumerable.Reverse(right)).ToList();
        var gore = new Polygon2(ring);
        if (gore.Area < 2) return;
        var z = zs.Average();
        // Linhas de contorno e zebrado a 45°.
        var stripes = new List<Polygon2>();
        var (mn, mx) = gore.Bounds;
        var diag = (mx - mn).Length;
        var dir = (right[^1] - right[0]).Normalized();
        var perp = dir.PerpLeft;
        var c = (mn + mx) * 0.5;
        var sd = (dir + perp).Normalized();
        for (var t = -diag; t <= diag; t += 1.6)
        {
            var a0 = c + sd.PerpLeft * t - sd * diag;
            var a1 = c + sd.PerpLeft * t + sd * diag;
            stripes.AddRange(PolygonOps.Strip(new[] { a0, a1 }, 0.40));
        }
        foreach (var part in PolygonOps.Intersect(new[] { gore }, stripes).Concat(PolygonOps.Strip(ring.Append(ring[0]).ToList(), 0.15)))
            if (part.Area > 0.01)
                geo.Pieces.Add(new MarkingPiece(part, MarkingColor.Branca) { Elevation = z + 0.0005, Thickness = 0.003, Layer = "ZEBRADO" });
    }

    /// <summary>Setas de sentido pintadas nos ramos (a cada 60 m).</summary>
    private static void Arrows(MarkingGeometry geo, Alignment a, DeckSurface surf)
    {
        var L = a.Path.Length;
        for (var s = 40.0; s < L - 30; s += 60)
        {
            var p = a.Path.PointAt(s);
            var t = a.Path.TangentAt(s);
            var n = t.PerpLeft;
            var z = surf.Z(s, 0);
            // Seta reta (MBST): haste 3,2 m × 0,15 m e ponta 1,8 m × 0,6 m.
            var shaft = new Polygon2(new[] { p - t * 2.5 - n * 0.075, p + t * 0.7 - n * 0.075, p + t * 0.7 + n * 0.075, p - t * 2.5 + n * 0.075 });
            var head = new Polygon2(new[] { p + t * 0.7 - n * 0.30, p + t * 2.5, p + t * 0.7 + n * 0.30 });
            foreach (var poly in new[] { shaft, head })
                geo.Pieces.Add(new MarkingPiece(poly, MarkingColor.Branca) { Elevation = z + 0.0005, Thickness = 0.003, Layer = "SETA" });
        }
    }

    /// <summary>Pórtico de sinalização indicativa sobre a pista da principal, 150 m antes da saída.</summary>
    private static void Gantry(MarkingGeometry geo, Alignment main, Alignment ramp, double clearance)
    {
        var start = ramp.Path.PointAt(0);
        var (ms, _, mp) = IntersectionGenerator.Project(main.Path, start);
        var side = Math.Sign((start - mp).Dot(main.Path.TangentAt(ms).PerpLeft));
        var travel = side < 0 ? 1 : -1;
        var s = Math.Clamp(ms - travel * 150, 5, main.Path.Length - 5);
        var p = main.Path.PointAt(s);
        var t = main.Path.TangentAt(s);
        var n = t.PerpLeft;
        var z = main.Profile.Z(s);
        var y0 = side * (main.Median / 2 + 0.3);
        var y1 = side * (main.Spec.DeckHalf + 1.2);
        var top = z + clearance + 0.6;
        foreach (var y in new[] { y0, y1 })
        {
            SolidSweep.Add(geo, SolidSweep.Box(p + n * y, t, 1.2, 1.2, z - 0.3, z + 0.3), MarkingColor.Concreto, "PORTICO");
            SolidSweep.AddColumn(geo, p + n * y, 0.22, 0.20, z + 0.3, top + 1.6, MarkingColor.Metal, "PORTICO", true);
        }
        // Viga treliçada (dois banzos e diagonais).
        var a = p + n * y0;
        var b = p + n * y1;
        foreach (var dz in new[] { top, top + 1.4 })
            SolidSweep.AddRound(geo, Vec3.At(a, dz), Vec3.At(b, dz), 0.09, 0.09, MarkingColor.Metal, "PORTICO", n: 12);
        var k = Math.Max(2, (int)((b - a).Length / 1.4));
        for (int i = 0; i < k; i++)
        {
            var q0 = a + (b - a) * (i / (double)k);
            var q1 = a + (b - a) * ((i + 1) / (double)k);
            SolidSweep.AddRound(geo, Vec3.At(q0, top), Vec3.At(q1, top + 1.4), 0.035, 0.035, MarkingColor.Metal, "PORTICO", n: 8);
        }
        // Placa verde (fundo verde, tarja branca) voltada para o tráfego que chega.
        var c = a + (b - a) * 0.6;
        var face = -t * travel;
        SolidSweep.Add(geo, SolidSweep.Box(c + face * 0.25, n, Math.Min((b - a).Length * 0.8, 7.5), 0.06, top - 0.3, top + 2.1), MarkingColor.Verde, "PLACA");
        SolidSweep.Add(geo, SolidSweep.Box(c + face * 0.285, n, Math.Min((b - a).Length * 0.8, 7.5) - 0.2, 0.01, top - 0.2, top + 2.0), MarkingColor.Branca, "PLACA");
        SolidSweep.Add(geo, SolidSweep.Box(c + face * 0.30, n, Math.Min((b - a).Length * 0.8, 7.5) - 0.34, 0.01, top - 0.13, top + 1.93), MarkingColor.Verde, "PLACA");
        // Seta de saída na placa.
        SolidSweep.Add(geo, SolidSweep.Box(c + face * 0.315 + n * (side * 1.5), n, 0.18, 0.01, top + 0.2, top + 1.3), MarkingColor.Branca, "PLACA");
    }

    /// <summary>Ilha central da rotatória (gramada, torre de iluminação); na elevada, duas ilhas deixando a principal passar.</summary>
    private static void Island(MarkingGeometry geo, Alignment ring, InterchangeDefinition d, IReadOnlyList<Alignment> all, Func<Vec2, double> ground, bool native)
    {
        var pts = ring.Path.Points;
        var c = new Vec2(pts.Average(p => p.X), pts.Average(p => p.Y));
        var r = pts.Average(p => p.DistanceTo(c)) - ring.Half - 0.3;
        if (r < 3) return;
        var z = ring.Profile.Z(0);
        var disk = new Polygon2(CurveTools.Circle(c, r, 0.01));
        var main = all.FirstOrDefault(x => x.Median > 0.2);
        var pieces = new List<Polygon2> { disk };
        var elevated = main != null && IntersectionGenerator.Project(main.Path, c).Distance < r;
        if (elevated) pieces = PolygonOps.Difference(pieces, PolygonOps.Strip(main!.Path.Points, 2 * (main.Half + 6)));
        foreach (var part in pieces)
        {
            var g = ground(part.Centroid);
            var hull = part.Outer.ToList();
            // Meio-fio da ilha e grama.
            geo.Pieces.Add(new MarkingPiece(part, MarkingColor.Grama) { Elevation = z - 0.05, Thickness = 0.20, Layer = "ILHA" });
            if (!elevated && !native && z - g > 0.4) SolidSweep.Add(geo, Polyhedron.Prism(hull, _ => g, _ => z - 0.05), MarkingColor.Terra, "ILHA");
            if (!elevated) geo.Pads.Add(new GradePad(part, z - 0.10) { FillSlope = d.FillSlope, Daylight = false, Label = "Ilha" });
        }
        if (d.HighMasts && !elevated) HighMast(geo, c, z + 0.15);
    }

    /// <summary>Área interna do laço: grama (acabamento do Toposolid) e torre de iluminação.</summary>
    private static void LoopInfield(MarkingGeometry geo, Alignment loop, InterchangeDefinition d, Func<Vec2, double> ground, bool native)
    {
        var pts = loop.Path.Points;
        // Centro do arco = média dos pontos do trecho curvo (ignora as tangentes).
        var n = pts.Count;
        var mid = pts.Skip(n / 5).Take(n * 3 / 5).ToList();
        var c = new Vec2(mid.Average(p => p.X), mid.Average(p => p.Y));
        var r = mid.Average(p => p.DistanceTo(c)) - loop.Half - 6;
        if (r < 8) return;
        if (native) geo.TerrainFinishes.Add((new Polygon2(CurveTools.Circle(c, r, 0.05)), MarkingColor.Grama, "Ilha do laço"));
        if (d.HighMasts && d.Lighting) HighMast(geo, c, ground(c));
    }

    /// <summary>Torre de iluminação de 30 m com coroa de projetores.</summary>
    private static void HighMast(MarkingGeometry geo, Vec2 c, double z)
    {
        SolidSweep.AddColumn(geo, c, 1.0, 1.0, z - 0.8, z + 0.2, MarkingColor.Concreto, "ILUMINACAO");
        SolidSweep.AddColumn(geo, c, 0.32, 0.16, z + 0.2, z + 30, MarkingColor.Metal, "ILUMINACAO", true);
        SolidSweep.AddRound(geo, Vec3.At(c, z + 29.6), Vec3.At(c, z + 29.9), 1.2, 1.2, MarkingColor.Metal, "ILUMINACAO", false, 1.05, 1.05, 32);
        for (int i = 0; i < 8; i++)
        {
            var dir = Vec2.FromAngle(2 * Math.PI * i / 8);
            SolidSweep.Add(geo, SolidSweep.Box(c + dir * 1.25, dir, 0.45, 0.6, z + 29.3, z + 29.75), MarkingColor.Metal, "ILUMINACAO");
            SolidSweep.Add(geo, SolidSweep.Box(c + dir * 1.49, dir, 0.03, 0.5, z + 29.35, z + 29.7), MarkingColor.Vidro, "ILUMINACAO");
        }
    }
}
