using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>De onde veio um trecho da rede do simulador.</summary>
public enum OrigemTrecho
{
    /// <summary>Via do plugin (Nova Via / Pista), com a seção gravada.</summary>
    Plugin,
    /// <summary>Inferida de um piso nativo do Revit (eixo medial do contorno) e da sinalização sobre ele.</summary>
    Piso,
    /// <summary>Inferida de uma linha de modelo escolhida como eixo.</summary>
    LinhaDeEixo,
    /// <summary>Inferida, mas faixas ou sentido sem sinalização que os defina (suposição indicada no trecho).</summary>
    Ambiguo,
}

/// <summary>Piso nativo do Revit (fora do plugin): contorno (m, com furos), cota do topo, nome do tipo e dos materiais, escolhido pelo usuário.</summary>
public sealed record NativeFloor(string Id, Polygon2 Shape, double Z = 0, string Name = "", bool Selected = false);

/// <summary>Linha de modelo escolhida como eixo de uma via (m).</summary>
public sealed record AxisLine(string Id, Polyline2 Axis, double Z = 0);

/// <summary>Algo do projeto que o simulador não conseguiu interpretar, e o motivo.</summary>
public sealed record Uninterpreted(string What, string Why, Vec2 Where, string? SourceId = null);

/// <summary>Fontes da rede além das definições do plugin: pisos nativos de pavimento e linhas de eixo.</summary>
public sealed class NetworkSources
{
    public List<NativeFloor> Floors { get; } = new();
    public List<AxisLine> AxisLines { get; } = new();

    private static readonly string[] PavementWords =
    {
        "asfalt", "asphalt", "cbuq", "bloquete", "paver", "intertrav", "paralelep", "pavimento", "pavement", "pista", "rua",
        "road", "street", "terra", "cascalho", "saibro", "leito",
    };
    private static readonly string[] NonRoadWords =
    {
        "calçada", "calcada", "passeio", "sidewalk", "meio-fio", "meio fio", "guia", "curb", "grama", "canteiro", "jardim",
        "ilha", "laje", "contrapiso", "piso interno", "deck", "escada",
    };

    /// <summary>O nome do tipo/material indica pavimento de via (asfalto, bloquete, concreto de pista, terra…).</summary>
    public static bool LooksLikePavement(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        return PavementWords.Any(n.Contains);
    }

    /// <summary>O nome indica outra coisa (calçada, meio-fio, canteiro, laje…).</summary>
    public static bool LooksLikeNonRoad(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        return NonRoadWords.Any(n.Contains);
    }

    /// <summary>Concreto sozinho no nome: pode ser pista ou laje – entra só com sinalização sobre ele ou escolhido.</summary>
    public static bool IsConcreteOnly(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        return (n.Contains("concreto") || n.Contains("concrete")) && !LooksLikePavement(n);
    }
}

/// <summary>
/// Vias que não são do plugin – pisos nativos de pavimento e linhas de modelo escolhidas como eixo – lidas para o
/// simulador: o eixo sai do eixo medial do piso (ou da linha), as faixas das divisórias pintadas (LMS, LCO) entre os bordos
/// (LBO ou a borda do piso), o sentido da linha amarela (LFO = mão dupla) ou das setas, e o estacionamento, a ciclofaixa e
/// as faixas estreitas ficam fora das faixas de tráfego. O resto (PARE, R-1, setas por faixa, travessias, bloqueios) é lido
/// depois pela mesma leitura da sinalização das vias do plugin.
/// </summary>
public static class InferredRoads
{
    public sealed record Result(TrafficRoad Road, RoadPavementDefinition Pav);

    private enum K { Center, Divider, Edge, Parking, Bike, Bus }

    private sealed record Paint(Polyline2 Line, K Kind, string Code, string Id);
    private sealed record Arrow(Vec2 Pos, Vec2 Dir, string Code, string Id);

    private sealed class Candidate
    {
        public required Polyline2 Axis { get; set; }
        public required OrigemTrecho Origin { get; init; }
        public List<string> Ids { get; } = new();
        public string Label { get; set; } = "";
        public double Z { get; init; }
        public bool ByMarkings { get; set; }
    }

    /// <summary>Faixas de um corte transversal: bordos, linha de centro e faixas de tráfego (afastamentos + à esquerda).</summary>
    private sealed record Section(double Right, double Left, double? Center, List<(double Lo, double Hi, int Lanes, bool Bus, bool Divided)> Lanes,
        bool ParkingRight, bool ParkingLeft, bool Bike);

    private const double MinLane = 2.3;
    private static readonly System.Globalization.CultureInfo Pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
    private static string F(double v) => v.ToString("0.00", Pt);
    /// <summary>Folga além da meia pista em volta de um encontro: as curvas das esquinas entortam o eixo medial ali.</summary>
    private const double JunctionPad = 7.0;

    public static List<Result> Read(NetworkSources src, IReadOnlyCollection<MarkingDefinition> defs, Func<MarkingDefinition, (Polyline2 Axis, double Z)?> axisOf,
        Func<MarkingDefinition, MarkingGeometry?>? geometryOf, IReadOnlyList<TrafficRoad> plugin, TrafficNetwork net)
    {
        var res = new List<Result>();
        if (src.Floors.Count == 0 && src.AxisLines.Count == 0) return res;
        var U = net.Uninterpreted;
        IEnumerable<T> Unique<T>() where T : MarkingDefinition => defs.OfType<T>().GroupBy(d => d.Id).Select(g => g.First());

        // ------------------------------------------------------------ sinalização solta (onde quer que esteja)
        var paints = new List<Paint>();
        foreach (var l in Unique<LinearMarkingDefinition>())
        {
            var code = (l.Code ?? "").ToUpperInvariant();
            K? k = code.StartsWith("LFO") ? K.Center
                : code.StartsWith("LMS") || code == "LCO" ? K.Divider
                : code == "LBO" ? K.Edge
                : code == "MER-L" ? K.Parking
                : code.StartsWith("CIC") ? K.Bike
                : code.StartsWith("ONI") ? K.Bus
                : null;
            if (k == null) continue;
            if (axisOf(l) is not { } ax || ax.Axis.Length < 1) continue;
            var line = Math.Abs(l.Offset) > 1e-6 ? ax.Axis.Offset(l.Offset) : ax.Axis;
            paints.Add(new Paint(line, k.Value, code, l.Id));
        }
        var arrows = new List<Arrow>();
        foreach (var sm in Unique<SymbolMarkingDefinition>().Where(s => (s.Code ?? "").StartsWith("PEM", StringComparison.OrdinalIgnoreCase)))
            if (sm.Direction.Length > 1e-6) arrows.Add(new Arrow(sm.Position, sm.Direction.Normalized(), sm.Code!, sm.Id));
        foreach (var rm in Unique<RepeatedMarkingDefinition>().Where(r => (r.SymbolCode ?? "").StartsWith("PEM", StringComparison.OrdinalIgnoreCase)))
        {
            if (axisOf(rm) is not { } ax || ax.Axis.Length < 1) continue;
            var line = Math.Abs(rm.Offset) > 1e-6 ? ax.Axis.Offset(rm.Offset) : ax.Axis;
            var step = Math.Max(5, rm.Spacing);
            for (var s = Math.Min(rm.StartOffset, line.Length / 2); s <= line.Length - Math.Min(rm.EndSetback, line.Length / 2) + 1e-6; s += step)
            {
                var t = line.TangentAt(s);
                arrows.Add(new Arrow(line.PointAt(s), rm.Reverse ? -t : t, rm.SymbolCode!, rm.Id));
            }
        }
        // Estacionamento (vagas pintadas): a área das vagas não é faixa de tráfego.
        var parking = new List<Polygon2>();
        foreach (var pk in Unique<ParkingMarkingDefinition>())
        {
            var g = geometryOf?.Invoke(pk);
            var path = axisOf(pk)?.Axis;
            if (g != null && g.Pieces.Count > 0 && path is { Length: > 1 })
            {
                // Faixa ocupada pelas vagas: do menor ao maior afastamento das peças em relação à linha delas, ao longo do trecho
                // que elas cobrem (as marcas de vaga paralela são só traços – a área entre eles também é vaga).
                var pr = g.Pieces.SelectMany(pc => pc.Shape.Outer).Select(path.Project).ToList();
                var (s0, s1) = (pr.Min(x => x.Station), pr.Max(x => x.Station));
                var (o0, o1) = (pr.Min(x => x.Signed), pr.Max(x => x.Signed));
                if (s1 - s0 > 1 && o1 - o0 > 0.5)
                {
                    var sub = new Polyline2(path.SubPoints(s0, s1));
                    var pts = sub.Offset(o1).Points.Concat(sub.Offset(o0).Points.Reverse()).ToList();
                    var band = new Polygon2(pts);
                    if (band.IsValid) parking.Add(band);
                }
            }
            else if (path is { Length: > 1 })
                parking.AddRange(PolygonOps.Strip(path.Offset((pk.RightSide ? -1 : 1) * 1.1).Points, 2.2));
        }

        // ------------------------------------------------------------ candidatos: linhas de eixo e pisos
        var cands = new List<Candidate>();
        var nE = 0;
        foreach (var al in src.AxisLines.Where(a => a.Axis.Length >= 5))
        {
            var c = new Candidate { Axis = al.Axis, Origin = OrigemTrecho.LinhaDeEixo, Z = al.Z, Label = $"E{++nE:00}" };
            c.Ids.Add(al.Id);
            cands.Add(c);
        }
        foreach (var al in src.AxisLines.Where(a => a.Axis.Length < 5))
            U.Add(new Uninterpreted($"Linha de eixo {al.Id}", "curta demais para ser uma via (menos de 5 m)", al.Axis.PointAt(al.Axis.Length / 2), al.Id));

        bool HasMarkings(Polygon2 poly) =>
            paints.Any(p => p.Line.Length >= 8 && Inside(poly, p.Line) >= 0.6) || arrows.Any(a => poly.Contains(a.Pos));
        var floors = new List<NativeFloor>();
        foreach (var f in src.Floors.Where(f => f.Shape.IsValid))
        {
            var marked = HasMarkings(f.Shape);
            var accept = f.Selected
                || (!NetworkSources.LooksLikeNonRoad(f.Name) && (NetworkSources.LooksLikePavement(f.Name) || marked));
            if (!accept)
            {
                // Sinalização sobre um piso que o nome diz não ser pista: avisa (selecionado, ele entra).
                if (marked && NetworkSources.LooksLikeNonRoad(f.Name))
                    U.Add(new Uninterpreted($"Piso {f.Id} ({f.Name})", "tem sinalização viária, mas o nome do tipo/material indica calçada, canteiro ou laje – selecione-o antes de abrir o simulador para usá-lo como pista",
                        f.Shape.Centroid, f.Id));
                continue;
            }
            floors.Add(f);
        }
        var floorPolys = new List<Polygon2>();
        var junctions = new List<Vec2>();
        if (floors.Count > 0)
        {
            floorPolys = PolygonOps.Union(floors.Select(f => f.Shape)).Where(p => p.IsValid).ToList();
            var fr = FloorRoads.Read(floorPolys);
            junctions = fr.Junctions;
            var axes = MergeThrough(fr.Roads.Select(r => r.Axis).ToList(), fr.Junctions).Select(a => CleanAxis(a, floorPolys, fr.Junctions)).ToList();
            var nP = 0;
            foreach (var a in axes)
            {
                var c = new Candidate
                {
                    Axis = a, Origin = OrigemTrecho.Piso, Label = $"P{++nP:00}",
                    Z = floors.Where(f => Touches(f.Shape, a)).Select(f => f.Z).DefaultIfEmpty(0).Max(),
                };
                c.Ids.AddRange(floors.Where(f => Touches(f.Shape, a)).Select(f => f.Id));
                c.ByMarkings = floors.Where(f => c.Ids.Contains(f.Id)).All(f => !f.Selected && !NetworkSources.LooksLikePavement(f.Name));
                cands.Add(c);
            }
            foreach (var f in floors.Where(f => !cands.Any(c => c.Ids.Contains(f.Id))))
                U.Add(new Uninterpreted($"Piso {f.Id}{(string.IsNullOrWhiteSpace(f.Name) ? "" : $" ({f.Name})")}",
                    "sem trecho de pista reconhecível: mais estreito que 2,50 m ou sem forma de via (o eixo medial não forma trechos)", f.Shape.Centroid, f.Id));
        }
        if (cands.Count == 0) return res;

        // ------------------------------------------------------------ o que já é via do plugin (ou linha de eixo) sai
        var kept = new List<Candidate>();
        foreach (var c in cands)
        {
            var covers = new List<(Polyline2 Axis, double Half)>();
            covers.AddRange(plugin.Select(r => (r.Axis, Math.Max(r.RightWidth, r.LeftWidth) + 0.5)));
            if (c.Origin == OrigemTrecho.Piso)
                covers.AddRange(cands.Where(o => o.Origin == OrigemTrecho.LinhaDeEixo).Select(o => (o.Axis, 3.0)));
            var runs = Outside(c.Axis, covers);
            if (runs.Count == 0)
            {
                net.Notes.Add($"{c.Label}: o trecho {(c.Origin == OrigemTrecho.Piso ? "do piso" : "da linha")} já é uma via do plugin (ou uma linha de eixo escolhida) – usada a via.");
                continue;
            }
            for (int i = 0; i < runs.Count; i++)
            {
                var k = new Candidate { Axis = runs[i], Origin = c.Origin, Z = c.Z, Label = runs.Count > 1 ? $"{c.Label}{(char)('a' + i)}" : c.Label, ByMarkings = c.ByMarkings };
                k.Ids.AddRange(c.Ids);
                kept.Add(k);
            }
        }

        // ------------------------------------------------------------ seção de cada candidato pela sinalização
        var built = new List<(Candidate C, TrafficRoad Road)>();
        foreach (var c in kept)
        {
            var near = junctions.Concat(Crossings(c.Axis, plugin.Select(r => r.Axis).Concat(kept.Where(o => o != c).Select(o => o.Axis)))).ToList();
            var sec = ReadSection(c, near, floorPolys, paints, parking, out var stations, out var why);
            if (sec == null)
            {
                U.Add(new Uninterpreted($"{Describe(c)}", why, c.Axis.PointAt(c.Axis.Length / 2), c.Ids.FirstOrDefault()));
                continue;
            }
            var road = MakeRoad(c, sec, stations, arrows, near, net.Roads.Count + built.Count + 1);
            if (road != null) built.Add((c, road));
        }
        // Pontas que chegam a outra via: o eixo segue reto até o eixo dela (entroncamento em T).
        var allAxes = plugin.Select(r => (r.Id, r.Axis, Half: Math.Max(r.RightWidth, r.LeftWidth))).ToList();
        allAxes.AddRange(built.Select(b => (b.Road.Id, b.Road.Axis, Half: Math.Max(b.Road.RightWidth, b.Road.LeftWidth))));
        foreach (var (c, road) in built)
        {
            var ax = ExtendEnds(road.Axis, allAxes.Where(a => a.Id != road.Id).Select(a => (a.Axis, a.Half)).ToList(),
                road.TrimmedStart ? Math.Max(road.RightWidth, road.LeftWidth) + JunctionPad + 6 : 6, road.TrimmedEnd ? Math.Max(road.RightWidth, road.LeftWidth) + JunctionPad + 6 : 6);
            var r2 = ax == road.Axis ? road : Clone(road, ax);
            res.Add(new Result(r2, new RoadPavementDefinition { Id = r2.Id, RightWidth = r2.RightWidth, LeftWidth = r2.LeftWidth, TwoWay = r2.TwoWay }));
            allAxes.RemoveAll(a => a.Id == r2.Id);
            allAxes.Add((r2.Id, r2.Axis, Math.Max(r2.RightWidth, r2.LeftWidth)));
        }

        // ------------------------------------------------------------ marcas longitudinais e setas fora de qualquer pista
        var roadsAll = plugin.Concat(res.Select(r => r.Road)).ToList();
        bool OnRoad(Vec2 p) => roadsAll.Any(r =>
        {
            var (st, sg) = r.Axis.Project(p);
            return st >= -2 && st <= r.Axis.Length + 2 && (sg >= 0 ? sg <= r.LeftWidth + 1.0 : -sg <= r.RightWidth + 1.0);
        });
        foreach (var p in paints.Where(p => p.Kind is K.Center or K.Divider or K.Edge).GroupBy(p => p.Id).Select(g => g.First()))
        {
            var mid = p.Line.PointAt(p.Line.Length / 2);
            if (OnRoad(mid)) continue;
            U.Add(new Uninterpreted($"Linha {p.Code}", "fora de qualquer pista reconhecida (via do plugin, piso de pavimento ou linha de eixo escolhida)", mid, p.Id));
        }
        foreach (var a in arrows.Where(a => !OnRoad(a.Pos)).GroupBy(a => a.Id).Select(g => g.First()))
            U.Add(new Uninterpreted($"Seta {a.Code}", "fora de qualquer pista reconhecida", a.Pos, a.Id));
        return res;
    }

    private static string Describe(Candidate c) =>
        (c.Origin == OrigemTrecho.LinhaDeEixo ? "Linha de eixo " : "Piso ") + string.Join(", ", c.Ids.Take(3)) + (c.Ids.Count > 3 ? "…" : "");

    // ------------------------------------------------------------------ seção transversal

    private static Section? ReadSection(Candidate c, List<Vec2> junctions, List<Polygon2> floors, List<Paint> paints, List<Polygon2> parking,
        out List<double> stations, out string why)
    {
        stations = new List<double>();
        why = c.Origin == OrigemTrecho.LinhaDeEixo
            ? "a linha não passa sobre piso de pavimento nem entre linhas de bordo/divisórias pintadas – largura da pista desconhecida"
            : "o eixo do piso não forma trecho de pista medível";
        var narrow = new List<double>();
        var A = c.Axis;
        var L = A.Length;
        // Pontos das linhas pintadas paralelas ao eixo, projetados nele (estaca, afastamento).
        var samples = new List<(double St, double Sg, K Kind, string Code)>();
        foreach (var p in paints)
        {
            var n = Math.Max(2, (int)Math.Ceiling(p.Line.Length / 0.5));
            for (int i = 0; i <= n; i++)
            {
                var s = p.Line.Length * i / n;
                var q = p.Line.PointAt(s);
                var (st, sg) = A.Project(q);
                if (st <= 0.01 || st >= L - 0.01 || Math.Abs(sg) > 25) continue;
                if (Math.Abs(p.Line.TangentAt(s).Dot(A.TangentAt(st))) < 0.92) continue;
                samples.Add((st, sg, p.Kind, p.Code));
            }
        }
        var floorHere = floors.Where(f => Touches(f, A)).ToList();
        var cuts = new List<Section>();
        var widths = new List<double>();
        var at = new List<double>();
        for (var s = 3.0; s <= L - 3.0; s += 2.5)
        {
            var p = A.PointAt(s);
            if (junctions.Any(j => j.DistanceTo(p) < 12)) continue;
            var nrm = A.TangentAt(s).PerpLeft;
            var here = samples.Where(x => Math.Abs(x.St - s) <= 0.6).ToList();
            double hl, hr;
            if (floorHere.Count > 0)
            {
                if (!floorHere.Any(f => f.Contains(p))) continue;
                hl = Ray(p, nrm, floorHere, 30);
                hr = Ray(p, -nrm, floorHere, 30);
            }
            else
            {
                // Linha de eixo sem piso: a pista vai das linhas de bordo (ou da linha mais afastada) + 0,15 m.
                var lefts = here.Where(x => x.Sg > 0).Select(x => x.Sg).ToList();
                var rights = here.Where(x => x.Sg < 0).Select(x => -x.Sg).ToList();
                if (lefts.Count + rights.Count == 0) continue;
                hl = lefts.Count > 0 ? lefts.Max() + 0.15 : 0.5;
                hr = rights.Count > 0 ? rights.Max() + 0.15 : 0.5;
            }
            if (hl + hr < 2.5) { narrow.Add(hl + hr); continue; }
            var cut = Cut(p, nrm, -hr, hl, here, parking);
            cuts.Add(cut);
            widths.Add(hl + hr);
            at.Add(s);
        }
        if (cuts.Count == 0)
        {
            if (narrow.Count > 0) why = $"pista mais estreita que 2,50 m ({F(Median(narrow))} m) – não é faixa de tráfego";
            return null;
        }
        // Bocas de cruzamento e alargamentos: fora (a pista típica é a mediana).
        var med = Median(widths);
        var good = Enumerable.Range(0, cuts.Count).Where(i => widths[i] <= med * 1.35 + 0.5).ToList();
        if (good.Count == 0) return null;
        // A seção mais frequente (faixas de cada lado e se há linha de centro).
        var best = good.GroupBy(i => Signature(cuts[i])).OrderByDescending(g => g.Count()).First().ToList();
        stations = best.Select(i => at[i]).ToList();
        var pick = best.Select(i => cuts[i]).ToList();
        var center = pick[0].Center != null ? Median(pick.Select(x => x.Center!.Value).ToList()) : (double?)null;
        var lanes = new List<(double, double, int, bool, bool)>();
        for (int k = 0; k < pick[0].Lanes.Count; k++)
            lanes.Add((Median(pick.Select(x => x.Lanes[k].Lo).ToList()), Median(pick.Select(x => x.Lanes[k].Hi).ToList()), pick[0].Lanes[k].Lanes,
                pick[0].Lanes[k].Bus, pick[0].Lanes[k].Divided));
        return new Section(Median(pick.Select(x => x.Right).ToList()), Median(pick.Select(x => x.Left).ToList()), center, lanes,
            pick.Count(x => x.ParkingRight) * 2 >= pick.Count, pick.Count(x => x.ParkingLeft) * 2 >= pick.Count, pick.Count(x => x.Bike) * 2 >= pick.Count);
    }

    private static string Signature(Section s) =>
        (s.Center != null ? "C" : "-") + ":" + string.Join(",", s.Lanes.Select(l => (l.Lo < (s.Center ?? 0) ? "R" : "L") + l.Lanes + (l.Bus ? "b" : "")));

    /// <summary>Corte transversal no ponto: bordos, linhas e faixas (afastamentos + à esquerda do eixo).</summary>
    private static Section Cut(Vec2 p, Vec2 nrm, double right, double left, List<(double St, double Sg, K Kind, string Code)> here, List<Polygon2> parking)
    {
        // Linhas no corte (agrupadas a 0,35 m – linhas duplas, LFO-3), mais os bordos da pista.
        var marks = here.OrderBy(x => x.Sg).ToList();
        var bounds = new List<(double Off, K Kind)>();
        foreach (var m in marks)
        {
            if (m.Sg < right - 0.3 || m.Sg > left + 0.3) continue;
            if (bounds.Count > 0 && m.Sg - bounds[^1].Off < 0.35)
            {
                var keep = Rank(m.Kind) < Rank(bounds[^1].Kind) ? m.Kind : bounds[^1].Kind;
                bounds[^1] = ((bounds[^1].Off + m.Sg) / 2, keep);
                continue;
            }
            bounds.Add((m.Sg, m.Kind));
        }
        var all = new List<(double Off, K Kind)> { (right, K.Edge) };
        all.AddRange(bounds.Where(b => b.Off > right + 0.2 && b.Off < left - 0.2));
        all.Add((left, K.Edge));
        double? center = bounds.Where(b => b.Kind == K.Center).Select(b => (double?)b.Off).FirstOrDefault();
        var lanes = new List<(double Lo, double Hi, int Lanes, bool Bus, bool Divided)>();
        bool parkR = false, parkL = false, bike = false;
        for (int i = 0; i + 1 < all.Count; i++)
        {
            var (lo, klo) = all[i];
            var (hi, khi) = all[i + 1];
            var w = hi - lo;
            if (w < MinLane) continue;
            var mid = (lo + hi) / 2;
            // Ciclofaixa: delimitada por linha de ciclofaixa e estreita, ou com a pintura da ciclofaixa dentro.
            if ((klo == K.Bike || khi == K.Bike) && w <= 2.6) { bike = true; continue; }
            var bus = klo == K.Bus || khi == K.Bus;
            // Vagas: a parte do corte coberta pelas vagas não é faixa.
            var a = p + nrm * lo;
            var b = p + nrm * hi;
            var covered = 0.0;
            foreach (var pk in parking)
                covered += DetailGenerator.SegmentIntervals(pk, a, b).Sum(iv => (iv.T1 - iv.T0) * w);
            covered = Math.Min(w, covered);
            if (covered > 0.5)
            {
                if (mid < (center ?? 0)) parkR = true; else parkL = true;
            }
            // Faixa entre a linha de estacionamento (MER-L) e o bordo: é a do estacionamento.
            if ((klo == K.Parking && khi == K.Edge || khi == K.Parking && klo == K.Edge) && w <= 2.8)
            {
                if (mid < (center ?? 0)) parkR = true; else parkL = true;
                continue;
            }
            var rem = w - covered;
            if (rem < MinLane) continue;
            // Sem divisória no meio: faixa larga = uma faixa; acima de 5,2 m, mais de uma (sem linha que as separe).
            var n = rem < 5.2 ? 1 : (int)Math.Round(rem / 3.4);
            // A faixa útil fica do lado oposto às vagas.
            var (l0, h0) = covered > 0.5 ? (mid < (center ?? 0) ? (hi - rem, hi) : (lo, lo + rem)) : (lo, hi);
            lanes.Add((l0, h0, n, bus, n == 1 || klo is K.Divider or K.Center || khi is K.Divider or K.Center));
        }
        return new Section(-right, left, center, lanes, parkR, parkL, bike);
    }

    private static int Rank(K k) => k switch { K.Center => 0, K.Divider => 1, K.Bus => 2, K.Bike => 3, K.Parking => 4, _ => 5 };

    // ------------------------------------------------------------------ via do simulador

    private static TrafficRoad? MakeRoad(Candidate c, Section sec, List<double> stations, List<Arrow> arrows, List<Vec2> junctions, int n)
    {
        var A = c.Axis;
        var notes = new List<string>();
        var origin = c.Origin;
        var total = sec.Lanes.Sum(l => l.Lanes);
        if (total == 0)
        {
            // Pista sem faixas legíveis (muito estreita entre as linhas): uma faixa em cada sentido, pela largura.
            sec = sec with { Lanes = new() { (-sec.Right, sec.Left, (sec.Right + sec.Left) >= 5.5 ? 2 : 1, false, false) } };
            total = sec.Lanes[0].Lanes;
        }
        // Setas sobre a pista: sentido de cada uma em relação ao eixo e o afastamento.
        var on = new List<(double Sg, int Dir)>();
        foreach (var a in arrows)
        {
            var (st, sg) = A.Project(a.Pos);
            if (st < 0 || st > A.Length || sg > sec.Left + 0.3 || -sg > sec.Right + 0.3) continue;
            if (junctions.Any(j => j.DistanceTo(a.Pos) < 8)) continue;
            var along = a.Dir.Dot(A.TangentAt(st));
            if (Math.Abs(along) < 0.7) continue;
            on.Add((sg, along > 0 ? 1 : -1));
        }
        bool twoWay;
        double axisOff;
        var reverse = false;
        int fwd, bwd;
        if (sec.Center is { } cOff)
        {
            twoWay = true;
            axisOff = cOff;
            fwd = sec.Lanes.Where(l => (l.Lo + l.Hi) / 2 < cOff).Sum(l => l.Lanes);
            bwd = sec.Lanes.Where(l => (l.Lo + l.Hi) / 2 > cOff).Sum(l => l.Lanes);
            notes.Add($"mão dupla pela linha amarela de centro (LFO), {fwd}+{bwd} faixa(s)");
            if (fwd == 0 || bwd == 0)
            {
                origin = OrigemTrecho.Ambiguo;
                notes.Add("um dos lados da linha de centro sem faixa de tráfego legível – suposta uma faixa");
                fwd = Math.Max(1, fwd);
                bwd = Math.Max(1, bwd);
            }
        }
        else if (on.Count > 0 && (on.All(x => x.Dir > 0) || on.All(x => x.Dir < 0)))
        {
            twoWay = false;
            reverse = on[0].Dir < 0;
            fwd = total;
            bwd = 0;
            axisOff = (sec.Lanes.Min(l => l.Lo) + sec.Lanes.Max(l => l.Hi)) / 2;
            notes.Add($"mão única pelas setas ({on.Count}), {fwd} faixa(s)");
            var unmarked = sec.Lanes.Count(l => !on.Any(x => x.Sg >= l.Lo - 0.3 && x.Sg <= l.Hi + 0.3));
            if (unmarked > 0 && sec.Lanes.Count > 1)
            {
                origin = OrigemTrecho.Ambiguo;
                notes.Add($"{unmarked} faixa(s) sem seta – supostas no mesmo sentido das setas");
            }
        }
        else if (on.Count > 0)
        {
            // Setas nos dois sentidos sem linha amarela: o centro fica entre as do sentido do eixo (à direita) e as contrárias.
            var plus = on.Where(x => x.Dir > 0).Select(x => x.Sg).ToList();
            var minus = on.Where(x => x.Dir < 0).Select(x => x.Sg).ToList();
            twoWay = true;
            axisOff = plus.Max() < minus.Min() ? (plus.Max() + minus.Min()) / 2 : (sec.Lanes.Min(l => l.Lo) + sec.Lanes.Max(l => l.Hi)) / 2;
            fwd = Math.Max(1, sec.Lanes.Where(l => (l.Lo + l.Hi) / 2 < axisOff).Sum(l => l.Lanes));
            bwd = Math.Max(1, sec.Lanes.Where(l => (l.Lo + l.Hi) / 2 > axisOff).Sum(l => l.Lanes));
            notes.Add($"mão dupla pelas setas nos dois sentidos (sem linha amarela), {fwd}+{bwd} faixa(s)");
            if (!(plus.Max() < minus.Min())) { origin = OrigemTrecho.Ambiguo; notes.Add("setas dos dois sentidos misturadas nas faixas – divisão suposta no meio da pista"); }
        }
        else if (sec.Lanes.Count > 1 && sec.Lanes.Any(l => l.Divided))
        {
            // Só divisórias brancas (fluxos de mesmo sentido) e nenhuma seta: mão única no sentido do traçado.
            twoWay = false;
            fwd = total;
            bwd = 0;
            axisOff = (sec.Lanes.Min(l => l.Lo) + sec.Lanes.Max(l => l.Hi)) / 2;
            origin = OrigemTrecho.Ambiguo;
            notes.Add($"divisórias brancas sem linha amarela: mão única de {fwd} faixas, sentido suposto o do traçado (sem setas)");
        }
        else
        {
            // Nada que defina o sentido: mão dupla, uma faixa por sentido (como a pista vazia do plugin).
            twoWay = true;
            var w = sec.Right + sec.Left;
            axisOff = (sec.Lanes.Min(l => l.Lo) + sec.Lanes.Max(l => l.Hi)) / 2;
            fwd = Math.Max(1, (int)Math.Round((axisOff + sec.Right) / 3.4));
            bwd = Math.Max(1, (int)Math.Round((sec.Left - axisOff) / 3.4));
            origin = OrigemTrecho.Ambiguo;
            notes.Add($"sem linha de centro nem setas: suposta mão dupla de {fwd}+{bwd} faixa(s) pela largura ({F(w)} m)");
        }
        var laneW = Math.Clamp(sec.Lanes.Sum(l => l.Hi - l.Lo) / Math.Max(1, total), 2.5, 5.0);
        if (sec.Lanes.Any(l => l.Lanes > 1 && !l.Divided)) { origin = OrigemTrecho.Ambiguo; notes.Add("faixa larga sem divisória pintada – contada pela largura"); }
        var rebuilt = Rebuild(A, junctions, Math.Max(5, Math.Max(sec.Right, sec.Left)) + JunctionPad, out var cutStart, out var cutEnd);
        var axis = new Polyline2(FloorRoads.Simplify(rebuilt.Points.ToList(), 0.25)).Offset(axisOff);
        var right = sec.Right + axisOff;
        var left = sec.Left - axisOff;
        if (reverse)
        {
            axis = axis.Reversed();
            (right, left) = (left, right);
            (cutStart, cutEnd) = (cutEnd, cutStart);
        }
        if (axis.Length < 5) return null;
        var src = c.Origin == OrigemTrecho.LinhaDeEixo ? "linha de eixo" : c.ByMarkings ? "piso (reconhecido pela sinalização sobre ele)" : "piso";
        var id = (c.Origin == OrigemTrecho.LinhaDeEixo ? "eixo:" : "piso:") + string.Join("+", c.Ids) + ":" + c.Label;
        return new TrafficRoad
        {
            Id = id, Name = $"{c.Label} – via inferida ({src})", Axis = axis, BaseZ = c.Z, TwoWay = twoWay,
            LanesForward = fwd, LanesBackward = bwd, LaneWidth = laneW, MinLaneWidth = laneW,
            SpeedKmh = 50, RightWidth = right, LeftWidth = left, TotalRight = right, TotalLeft = left, MinCarriageWidth = right + left,
            ParkingForward = reverse ? sec.ParkingLeft : sec.ParkingRight, ParkingBackward = reverse ? sec.ParkingRight : sec.ParkingLeft, BikeLane = sec.Bike,
            Origin = origin, OriginNote = $"{src}: " + string.Join("; ", notes) + $"; pista de {F(right + left)} m, faixas de {F(laneW)} m; velocidade 50 km/h sem R-19 [a confirmar]",
            SourceIds = c.Ids.ToList(), TrimmedStart = cutStart, TrimmedEnd = cutEnd,
        };
    }

    private static TrafficRoad Clone(TrafficRoad r, Polyline2 axis) => new()
    {
        Id = r.Id, Name = r.Name, Axis = axis, BaseZ = r.BaseZ, TwoWay = r.TwoWay, LanesForward = r.LanesForward, LanesBackward = r.LanesBackward,
        LaneWidth = r.LaneWidth, MinLaneWidth = r.MinLaneWidth, SpeedKmh = r.SpeedKmh, RightWidth = r.RightWidth, LeftWidth = r.LeftWidth,
        TotalRight = r.TotalRight, TotalLeft = r.TotalLeft, MinCarriageWidth = r.MinCarriageWidth, ParkingForward = r.ParkingForward,
        ParkingBackward = r.ParkingBackward, BikeLane = r.BikeLane, Origin = r.Origin, OriginNote = r.OriginNote, SourceIds = r.SourceIds,
    };

    // ------------------------------------------------------------------ geometria

    /// <summary>Junta os trechos do eixo medial que seguem retos através de um encontro (a via principal de um T).</summary>
    private static List<Polyline2> MergeThrough(List<Polyline2> axes, List<Vec2> junctions)
    {
        var list = axes.Select(a => a.Points.ToList()).ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var j in junctions)
            {
                var ends = new List<(int I, bool AtEnd, Vec2 Out)>();
                for (int i = 0; i < list.Count; i++)
                {
                    var pl = new Polyline2(list[i]);
                    if (pl.Length < 1) continue;
                    // Direção medida fora da boca do encontro (o eixo medial se curva junto às esquinas).
                    var near = Math.Min(14, pl.Length * 0.3);
                    var far = Math.Min(40, pl.Length * 0.8);
                    if (list[i][^1].DistanceTo(j) < 1.0) ends.Add((i, true, (pl.PointAt(pl.Length - near) - pl.PointAt(pl.Length - far)).Normalized()));
                    if (list[i][0].DistanceTo(j) < 1.0) ends.Add((i, false, (pl.PointAt(near) - pl.PointAt(far)).Normalized()));
                }
                (int A, int B, double Dot)? best = null;
                for (int a = 0; a < ends.Count; a++)
                    for (int b = a + 1; b < ends.Count; b++)
                    {
                        if (ends[a].I == ends[b].I) continue;
                        var d = ends[a].Out.Dot(ends[b].Out);
                        if (d < -0.9 && (best == null || d < best.Value.Dot)) best = (a, b, d);
                    }
                if (best is not { } bp) continue;
                var ea = ends[bp.A];
                var eb = ends[bp.B];
                var pa = ea.AtEnd ? list[ea.I] : Enumerable.Reverse(list[ea.I]).ToList();
                var pb = eb.AtEnd ? Enumerable.Reverse(list[eb.I]).ToList() : list[eb.I];
                var merged = pa.Concat(pb.Skip(1)).ToList();
                var (hi, lo) = ea.I > eb.I ? (ea.I, eb.I) : (eb.I, ea.I);
                list.RemoveAt(hi);
                list.RemoveAt(lo);
                list.Add(merged);
                changed = true;
                break;
            }
        }
        return list.Select(p => new Polyline2(p)).ToList();
    }

    /// <summary>
    /// Eixo medial limpo nas pontas livres (fora dos encontros): junto à ponta reta do piso a triangulação faz o eixo ir e voltar
    /// para os cantos. Tudo o que fica dentro de um círculo de 2 meias pistas + 5 m em volta da ponta sai, e o eixo segue reto,
    /// na direção que tinha ao entrar no círculo, até a borda do piso.
    /// </summary>
    private static Polyline2 CleanAxis(Polyline2 axis, List<Polygon2> floors, List<Vec2> junctions)
    {
        var A = axis;
        var L = A.Length;
        if (L < 20) return A;
        var mid = A.PointAt(L / 2);
        var n = A.TangentAt(L / 2).PerpLeft;
        var hw = (Ray(mid, n, floors, 30) + Ray(mid, -n, floors, 30)) / 2;
        var R = Math.Min(2 * hw + 5, L / 3);
        foreach (var atEnd in new[] { false, true })
        {
            L = A.Length;
            var end = atEnd ? A.PointAt(L) : A.PointAt(0);
            if (junctions.Any(j => j.DistanceTo(end) < 2)) continue;
            // Estaca em que o eixo deixa o círculo da ponta pela última vez (ponta inicial) / entra nele de vez (ponta final).
            double? cut = null;
            const double step = 0.5;
            if (!atEnd) { for (var s = 0.0; s <= L; s += step) if (A.PointAt(s).DistanceTo(end) < R) cut = s; }
            else { for (var s = L; s >= 0; s -= step) if (A.PointAt(s).DistanceTo(end) < R) cut = s; }
            if (cut is not { } c || (atEnd ? c < 10 : c > L - 10)) continue;
            var q = A.PointAt(c);
            var back = A.PointAt(atEnd ? Math.Max(0, c - 10) : Math.Min(L, c + 10));
            var dir = (q - back).Normalized();
            var reach = Ray(q, dir, floors, 2 * R + 5);
            if (reach < 0.5) continue;
            var tip = q + dir * Math.Max(0, reach - 0.05);
            var keep = atEnd ? A.SubPoints(0, c) : A.SubPoints(c, L);
            A = new Polyline2(atEnd ? keep.Append(tip) : keep.Prepend(tip));
        }
        return A;
    }

    /// <summary>
    /// Eixo refeito nos encontros: os pontos a menos de <paramref name="radius"/> do encontro saem (as esquinas entortam o eixo
    /// medial ali) – o trecho que atravessa vira reta e a ponta que chega fica cortada (prolongada depois até a outra via).
    /// </summary>
    private static Polyline2 Rebuild(Polyline2 A, List<Vec2> junctions, double radius, out bool trimStart, out bool trimEnd)
    {
        trimStart = trimEnd = false;
        var axis = A;
        foreach (var j in junctions)
        {
            var L = axis.Length;
            if (L < 1) break;
            double? sa = null, sb = null;
            for (var s = 0.0; s <= L + 1e-9; s += 0.5)
            {
                if (axis.PointAt(Math.Min(s, L)).DistanceTo(j) >= radius) continue;
                sa ??= s;
                sb = Math.Min(s, L);
            }
            if (sa is not { } s0 || sb is not { } s1) continue;
            if (s0 <= 0.5 && s1 >= L - 0.5) continue;
            List<Vec2> pts;
            if (s0 <= 0.5) { pts = axis.SubPoints(s1, L); trimStart = true; }
            else if (s1 >= L - 0.5) { pts = axis.SubPoints(0, s0); trimEnd = true; }
            else pts = axis.SubPoints(0, s0).Concat(axis.SubPoints(s1, L)).ToList();
            if (pts.Count >= 2) axis = new Polyline2(pts);
        }
        return axis;
    }

    /// <summary>Pontas do eixo prolongadas em linha reta até o eixo de uma via vizinha que esteja logo à frente.</summary>
    private static Polyline2 ExtendEnds(Polyline2 axis, List<(Polyline2 Axis, double Half)> others, double reachStart, double reachEnd)
    {
        var pts = axis.Points.ToList();
        bool changed = false;
        foreach (var atEnd in new[] { false, true })
        {
            var end = atEnd ? pts[^1] : pts[0];
            var inner = atEnd ? pts[^2] : pts[1];
            var dir = (end - inner).Normalized();
            (Vec2 P, double D)? best = null;
            foreach (var (o, half) in others)
            {
                var (st, sg) = o.Project(end);
                if (Math.Abs(sg) < 0.05) continue;
                if (RayHit(end, dir, o, half + (atEnd ? reachEnd : reachStart)) is not { } hit) continue;
                var d = hit.DistanceTo(end);
                if (best == null || d < best.Value.D) best = (hit, d);
            }
            if (best is not { } b) continue;
            if (atEnd) pts.Add(b.P); else pts.Insert(0, b.P);
            changed = true;
        }
        return changed ? new Polyline2(pts) : axis;
    }

    /// <summary>Primeiro ponto em que a semirreta (até <paramref name="max"/> m) cruza a polilinha.</summary>
    private static Vec2? RayHit(Vec2 o, Vec2 d, Polyline2 line, double max)
    {
        Vec2? best = null;
        var bt = double.MaxValue;
        for (int i = 0; i + 1 < line.Points.Count; i++)
        {
            var a = line.Points[i];
            var e = line.Points[i + 1] - a;
            var den = d.Cross(e);
            if (Math.Abs(den) < 1e-9) continue;
            var t = (a - o).Cross(e) / den;
            var u = (a - o).Cross(d) / den;
            if (t < 0 || t > max || u < -1e-9 || u > 1 + 1e-9) continue;
            if (t < bt) { bt = t; best = o + d * t; }
        }
        return best;
    }

    /// <summary>Partes do eixo fora das pistas dadas (≥ 8 m); vazio = coberto.</summary>
    private static List<Polyline2> Outside(Polyline2 axis, List<(Polyline2 Axis, double Half)> covers)
    {
        if (covers.Count == 0) return new() { axis };
        var L = axis.Length;
        var step = 1.0;
        var free = new List<bool>();
        for (var s = 0.0; s <= L + 1e-9; s += step)
        {
            var p = axis.PointAt(Math.Min(s, L));
            var t = axis.TangentAt(Math.Min(s, L));
            free.Add(!covers.Any(c =>
            {
                var (st, sg) = c.Axis.Project(p);
                return st > 0.01 && st < c.Axis.Length - 0.01 && Math.Abs(sg) <= c.Half && Math.Abs(c.Axis.TangentAt(st).Dot(t)) > 0.85;
            }));
        }
        if (free.All(f => f)) return new() { axis };
        var runs = new List<Polyline2>();
        int i0 = -1;
        for (int i = 0; i <= free.Count; i++)
        {
            var f = i < free.Count && free[i];
            if (f && i0 < 0) i0 = i;
            if (!f && i0 >= 0)
            {
                var s0 = i0 * step;
                var s1 = Math.Min(L, (i - 1) * step);
                if (s1 - s0 >= 8) runs.Add(new Polyline2(axis.SubPoints(s0, s1)));
                i0 = -1;
            }
        }
        return runs;
    }

    /// <summary>Cruzamentos do eixo com outros eixos (pontos).</summary>
    private static IEnumerable<Vec2> Crossings(Polyline2 a, IEnumerable<Polyline2> others)
    {
        foreach (var b in others)
            for (int i = 0; i + 1 < a.Points.Count; i++)
                for (int k = 0; k + 1 < b.Points.Count; k++)
                {
                    var p = a.Points[i];
                    var r = a.Points[i + 1] - p;
                    var q = b.Points[k];
                    var s = b.Points[k + 1] - q;
                    var den = r.Cross(s);
                    if (Math.Abs(den) < 1e-9) continue;
                    var t = (q - p).Cross(s) / den;
                    var u = (q - p).Cross(r) / den;
                    if (t >= 0 && t <= 1 && u >= 0 && u <= 1) yield return p + r * t;
                }
    }

    /// <summary>Distância do ponto (dentro do piso) até a borda do piso na direção dada.</summary>
    private static double Ray(Vec2 p, Vec2 dir, List<Polygon2> polys, double max)
    {
        var b = p + dir * max;
        var best = 0.0;
        foreach (var poly in polys)
            foreach (var (t0, t1) in DetailGenerator.SegmentIntervals(poly, p, b))
                if (t0 <= 1e-6) best = Math.Max(best, t1 * max);
        return best;
    }

    private static double Inside(Polygon2 poly, Polyline2 line)
    {
        var n = Math.Max(4, (int)(line.Length / 2));
        var k = 0;
        for (int i = 0; i <= n; i++) if (poly.Contains(line.PointAt(line.Length * i / n))) k++;
        return k / (double)(n + 1);
    }

    private static bool Touches(Polygon2 poly, Polyline2 axis)
    {
        var n = Math.Max(4, (int)(axis.Length / 3));
        for (int i = 0; i <= n; i++) if (poly.Contains(axis.PointAt(axis.Length * i / n))) return true;
        return false;
    }

    private static double Median(List<double> v)
    {
        if (v.Count == 0) return 0;
        var s = v.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
