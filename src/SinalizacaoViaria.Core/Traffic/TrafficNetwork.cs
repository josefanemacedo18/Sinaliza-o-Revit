using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Traffic;

/// <summary>Tipo de nó da rede viária.</summary>
public enum TipoNo { Intersecao, Rotatoria, Extremidade, CulDeSac, Continuacao, CruzamentoSemControle }

/// <summary>Controle de tráfego do nó.</summary>
public enum ControleNo { Livre, Pare, DePreferencia, Semaforo, Rotatoria, PreferenciaDireita }

/// <summary>Via do projeto, lida do pavimento (seção, sentidos, faixas, velocidade, hierarquia, greide).</summary>
public sealed class TrafficRoad
{
    public string Id { get; init; } = "";
    public string? GroupId { get; init; }
    public string Name { get; set; } = "";
    public HierarquiaViaria? Hierarchy { get; init; }
    public required Polyline2 Axis { get; init; }
    public double BaseZ { get; init; }
    public RoadGrade? Grade { get; init; }
    public bool TwoWay { get; init; } = true;
    /// <summary>Faixas de tráfego geral no sentido do eixo / no sentido contrário.</summary>
    public int LanesForward { get; init; } = 1;
    public int LanesBackward { get; init; } = 1;
    public int BusLanesForward { get; init; }
    public int BusLanesBackward { get; init; }
    public double LaneWidth { get; init; } = 3.5;
    /// <summary>Velocidade regulamentada (km/h).</summary>
    public double SpeedKmh { get; init; } = 60;
    public bool SpeedFromSection { get; init; }
    public bool ParkingForward { get; init; }
    public bool ParkingBackward { get; init; }
    public bool BikeLane { get; init; }
    public bool SidewalkRight { get; init; }
    public bool SidewalkLeft { get; init; }
    public bool Median { get; init; }
    public double RightWidth { get; init; }
    public double LeftWidth { get; init; }
    /// <summary>Largura da pista entre meios-fios (m).</summary>
    public double CarriageWidth => RightWidth + LeftWidth;

    public double Z(double s) => BaseZ + (Grade?.Z(Math.Clamp(s, 0, Axis.Length)) ?? 0);
}

/// <summary>Nó da rede: interseção, rotatória, extremidade (entrada/saída do tráfego), balão, emenda.</summary>
public sealed class TrafficNode
{
    public int Index { get; init; }
    public TipoNo Kind { get; set; }
    public ControleNo Control { get; set; }
    public Vec2 Pos { get; set; }
    public double Z { get; set; }
    public string? SourceId { get; init; }
    public string? MainRoadId { get; set; }
    public int RoundaboutLanes { get; init; } = 1;
    public double RoundaboutRadius { get; init; }
    public bool Crosswalks { get; init; }
    public bool Signs { get; init; }
    public List<int> In { get; } = new();
    public List<int> Out { get; } = new();
    public HashSet<string> RoadIds { get; } = new();
    public string Label { get; set; } = "";
    public bool IsZone => Kind is TipoNo.Extremidade or TipoNo.CulDeSac;
}

/// <summary>Trecho direcional (um sentido) de uma via entre dois nós.</summary>
public sealed class TrafficLink
{
    public int Index { get; init; }
    public int From { get; init; }
    public int To { get; init; }
    public required TrafficRoad Road { get; init; }
    /// <summary>No sentido do eixo da via.</summary>
    public bool Forward { get; init; }
    /// <summary>Geometria no sentido do tráfego (eixo da via).</summary>
    public required Polyline2 Path { get; init; }
    public double S0 { get; init; }
    public double S1 { get; init; }
    public double Length => Path.Length;
    public int Lanes { get; init; } = 1;
    public double FreeSpeed { get; set; }          // m/s
    public double Capacity { get; set; }           // veh/h (todas as faixas)
    public double SaturationPerLane { get; set; }  // veh/h/faixa
    public double MinRadius { get; set; } = double.PositiveInfinity;
    public double GradePct { get; set; }
    /// <summary>Pontos de velocidade reduzida (moderação de tráfego): posição ao longo do trecho (m), velocidade (m/s), o quê.</summary>
    public List<(double At, double Speed, string What)> SlowPoints { get; } = new();
    /// <summary>Travessias de pedestres no meio da quadra: posição ao longo do trecho (m) e largura atravessada (m).</summary>
    public List<(double At, double Width)> Crosswalks { get; } = new();
    /// <summary>
    /// Afastamento (m, positivo à direita do sentido do trecho) do centro da faixa <paramref name="lane"/> (0 = a da
    /// direita, junto ao meio-fio).
    /// </summary>
    public double LaneOffset(int lane)
    {
        var w = Road.LaneWidth;
        lane = Math.Clamp(lane, 0, Lanes - 1);
        if (!Road.TwoWay) return (Lanes / 2.0 - lane - 0.5) * w;
        var inner = Road.Median ? 1.0 : 0.0;
        return inner + (Lanes - lane - 0.5) * w;
    }

    public string Name => Road.Name + (Road.TwoWay ? (Forward ? " (sentido do eixo)" : " (sentido contrário)") : "");
}

/// <summary>Placa do projeto (para conferir a sinalização da rede).</summary>
public sealed record TrafficSign(string Id, string Code, Vec2 Position, Vec2 Direction, string? Legend);

/// <summary>Rede viária do projeto pronta para análise e simulação.</summary>
public sealed class TrafficNetwork
{
    public List<TrafficRoad> Roads { get; } = new();
    public List<TrafficNode> Nodes { get; } = new();
    public List<TrafficLink> Links { get; } = new();
    public List<TrafficSign> Signs { get; } = new();
    /// <summary>Faixas de pedestres (código, eixo da travessia).</summary>
    public List<(string Id, string Code, Polyline2 Path)> Crosswalks { get; } = new();
    public List<(string Id, Polyline2 Path, string Code)> Parking { get; } = new();
    /// <summary>Recortes das vagas (esquinas já tratadas pelas interseções).</summary>
    public Dictionary<string, List<Polygon2>> ParkingCuts { get; } = new();
    public List<(string Id, Vec2 Pos)> Ramps { get; } = new();
    public List<(string Id, Polyline2 Path, TipoModeracao Type)> Calming { get; } = new();
    /// <summary>Cruzamentos em desnível (viadutos, túneis): as vias passam sem conflito.</summary>
    public List<(Vec2 Pos, string A, string B, double Dz)> GradeSeparations { get; } = new();
    public List<string> Notes { get; } = new();

    public TrafficRoad? Road(string id) => Roads.FirstOrDefault(r => r.Id == id);
    public IEnumerable<TrafficLink> LinksOf(TrafficRoad r) => Links.Where(l => ReferenceEquals(l.Road, r));
    public (Vec2 Min, Vec2 Max) Bounds()
    {
        var pts = Roads.SelectMany(r => r.Axis.Points).ToList();
        if (pts.Count == 0) return (Vec2.Zero, new Vec2(100, 100));
        return (new Vec2(pts.Min(p => p.X), pts.Min(p => p.Y)), new Vec2(pts.Max(p => p.X), pts.Max(p => p.Y)));
    }
}

/// <summary>Monta a rede viária a partir das definições do projeto (vias, interseções, rotatórias, balões, placas...).</summary>
public static class TrafficNetworkBuilder
{
    /// <param name="axisOf">Eixo resolvido (m) e cota base de uma marca com caminho; nulo se não resolvido.</param>
    public static TrafficNetwork Build(IReadOnlyCollection<MarkingDefinition> defs, Func<MarkingDefinition, (Polyline2 Axis, double Z)?> axisOf)
    {
        var net = new TrafficNetwork();
        // ------------------------------------------------------------ vias
        var n = 0;
        var pavs = defs.OfType<RoadPavementDefinition>().GroupBy(p => p.Id).Select(g => g.First()).ToList();
        foreach (var pav in pavs)
        {
            var ax = axisOf(pav);
            if (ax is not { } a || a.Axis.Length < 5) continue;
            net.Roads.Add(RoadFrom(pav, a.Axis, a.Z, ++n));
        }
        if (net.Roads.Count == 0)
        {
            net.Notes.Add("Nenhuma via do plugin (Via / Pista) no projeto: crie as vias para simular o tráfego.");
            return net;
        }

        // ------------------------------------------------------------ nós declarados
        var attach = net.Roads.ToDictionary(r => r.Id, _ => new List<(double S, int Node)>());
        void Attach(TrafficRoad r, double s, TrafficNode nd)
        {
            s = Math.Clamp(s, 0, r.Axis.Length);
            if (attach[r.Id].Any(x => x.Node == nd.Index && Math.Abs(x.S - s) < 1)) return;
            attach[r.Id].Add((s, nd.Index));
            nd.RoadIds.Add(r.Id);
        }
        TrafficNode NewNode(TipoNo kind, ControleNo control, Vec2 pos, double z, string? src, string? main = null, int rbLanes = 1, double rbR = 0,
            bool crosswalks = false, bool signs = false)
        {
            var nd = new TrafficNode
            {
                Index = net.Nodes.Count, Kind = kind, Control = control, Pos = pos, Z = z, SourceId = src, MainRoadId = main,
                RoundaboutLanes = rbLanes, RoundaboutRadius = rbR, Crosswalks = crosswalks, Signs = signs,
            };
            net.Nodes.Add(nd);
            return nd;
        }

        foreach (var it in defs.OfType<IntersectionDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
        {
            var roads = it.RoadIds.Select(net.Road).Where(r => r != null).Cast<TrafficRoad>().ToList();
            if (roads.Count < 2) continue;
            var control = it.Control switch
            {
                ControleIntersecao.Pare => ControleNo.Pare,
                ControleIntersecao.DePreferencia => ControleNo.DePreferencia,
                ControleIntersecao.Semaforo => ControleNo.Semaforo,
                _ => ControleNo.PreferenciaDireita,
            };
            var nd = NewNode(TipoNo.Intersecao, control, it.Node, it.Z, it.Id, it.MainRoadId, crosswalks: it.Crosswalks, signs: it.Signs);
            foreach (var r in roads) Attach(r, r.Axis.Project(it.Node).Station, nd);
        }
        foreach (var rb in defs.OfType<RoundaboutDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
        {
            var R = rb.OuterRadius;
            var nd = NewNode(TipoNo.Rotatoria, ControleNo.Rotatoria, rb.Center, rb.Z, rb.Id, rbLanes: Math.Max(1, rb.Lanes), rbR: R,
                crosswalks: rb.CrosswalkWidth > 0);
            foreach (var r in net.Roads)
            {
                var (st, dist) = (r.Axis.Project(rb.Center).Station, Math.Abs(r.Axis.Project(rb.Center).Signed));
                var legOfRoad = rb.Legs.Any(l => l.RoadId == r.Id);
                if (!legOfRoad && dist > R + 3) continue;
                // A via chega à rotatória no anel externo: um ou dois pontos (via que atravessa o centro).
                var half = Math.Sqrt(Math.Max(0, R * R - dist * dist));
                var sIn = st - half;
                var sOut = st + half;
                if (sIn > 0.5) Attach(r, sIn, nd);
                if (sOut < r.Axis.Length - 0.5) Attach(r, sOut, nd);
            }
        }

        // ------------------------------------------------------------ cruzamentos sem interseção (e desníveis)
        var iroads = net.Roads.Select(r => new IntersectionRoad(pavs.First(p => p.Id == r.Id), r.Axis)).ToList();
        foreach (var (p, ids) in IntersectionGenerator.FindNodes(iroads))
        {
            if (net.Nodes.Any(x => x.Pos.DistanceTo(p) < Math.Max(12, x.RoundaboutRadius + 6))) continue;
            var rs = ids.Select(i => net.Roads[i]).ToList();
            var zs = rs.Select(r => r.Z(r.Axis.Project(p).Station)).ToList();
            if (zs.Max() - zs.Min() > 2.5)
            {
                net.GradeSeparations.Add((p, rs[0].Id, rs[^1].Id, zs.Max() - zs.Min()));
                continue;
            }
            var nd = NewNode(TipoNo.CruzamentoSemControle, ControleNo.PreferenciaDireita, p, zs.Average(), null);
            foreach (var r in rs) Attach(r, r.Axis.Project(p).Station, nd);
        }

        // ------------------------------------------------------------ pontas das vias
        var cds = defs.OfType<CulDeSacDefinition>().Where(c => c.RoadId != null).ToList();
        foreach (var r in net.Roads)
            foreach (var atEnd in new[] { false, true })
            {
                var s = atEnd ? r.Axis.Length : 0;
                var p = r.Axis.PointAt(s);
                var reach = Math.Max(r.RightWidth, r.LeftWidth) + 4;
                if (attach[r.Id].Any(x => Math.Abs(x.S - s) < reach)) continue;
                // Ponta dentro de uma rotatória (eixo desenhado até o centro): já ligada ao anel.
                if (net.Nodes.Any(x => x.Kind == TipoNo.Rotatoria && x.Pos.DistanceTo(p) < x.RoundaboutRadius + 1)) continue;
                // Emenda com a ponta de outra via (continuação).
                var cont = net.Nodes.FirstOrDefault(x => x.Kind == TipoNo.Continuacao && x.Pos.DistanceTo(p) < 5);
                if (cont == null)
                {
                    var other = net.Roads.Where(o => o != r).SelectMany(o => new[] { (o, 0.0), (o, o.Axis.Length) })
                        .FirstOrDefault(x => x.Item1.Axis.PointAt(x.Item2).DistanceTo(p) < 5);
                    if (other.Item1 != null)
                    {
                        cont = NewNode(TipoNo.Continuacao, ControleNo.Livre, p, r.Z(s), null);
                        Attach(other.Item1, other.Item2, cont);
                    }
                }
                if (cont != null) { Attach(r, s, cont); continue; }
                var cd = cds.FirstOrDefault(c => c.RoadId == r.Id && c.AtRoadEnd == atEnd);
                var nd = cd != null
                    ? NewNode(TipoNo.CulDeSac, ControleNo.Livre, p, r.Z(s), cd.Id)
                    : NewNode(TipoNo.Extremidade, ControleNo.Livre, p, r.Z(s), null);
                Attach(r, s, nd);
            }

        // ------------------------------------------------------------ trechos direcionais
        foreach (var r in net.Roads)
        {
            var list = attach[r.Id].OrderBy(x => x.S).ToList();
            for (int i = 0; i + 1 < list.Count; i++)
            {
                var (s0, a) = list[i];
                var (s1, b) = list[i + 1];
                if (a == b) continue;                                   // dentro do anel de uma rotatória
                if (s1 - s0 < 1.0) continue;
                var pts = r.Axis.SubPoints(s0, s1);
                if (pts.Count < 2) continue;
                var fwd = new Polyline2(pts);
                if (r.LanesForward > 0) AddLink(net, r, a, b, fwd, s0, s1, true);
                if (r.LanesBackward > 0) AddLink(net, r, b, a, fwd.Reversed(), s0, s1, false);
            }
        }

        // ------------------------------------------------------------ demais elementos
        foreach (var sg in defs.OfType<SignDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
            net.Signs.Add(new TrafficSign(sg.Id, sg.Code, sg.Position, sg.Direction, sg.Legend));
        foreach (var l in defs.OfType<LinearMarkingDefinition>().Where(l => l.Code.StartsWith("FTP", StringComparison.OrdinalIgnoreCase)).GroupBy(d => d.Id).Select(g => g.First()))
            if (axisOf(l) is { } a) net.Crosswalks.Add((l.Id, l.Code, a.Axis));
        foreach (var pk in defs.OfType<ParkingMarkingDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
            if (axisOf(pk) is { } a)
            {
                net.Parking.Add((pk.Id, a.Axis, pk.Code));
                net.ParkingCuts[pk.Id] = pk.Exclusions.Where(x => x.Enabled && x.Points.Count >= 3).Select(x => new Polygon2(x.Points)).ToList();
            }
        foreach (var rp in defs.OfType<RampDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
            if (axisOf(rp) is { } a && a.Axis.Points.Count > 0) net.Ramps.Add((rp.Id, a.Axis.Points[0]));
        foreach (var tc in defs.OfType<TrafficCalmingDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
            if (axisOf(tc) is { } a) net.Calming.Add((tc.Id, a.Axis, tc.Type));

        foreach (var lk in net.Links) Characterize(net, lk);
        Label(net);
        return net;
    }

    private static void AddLink(TrafficNetwork net, TrafficRoad r, int from, int to, Polyline2 path, double s0, double s1, bool forward)
    {
        var lk = new TrafficLink
        {
            Index = net.Links.Count, From = from, To = to, Road = r, Forward = forward, Path = path, S0 = s0, S1 = s1,
            Lanes = Math.Max(1, forward ? r.LanesForward : r.LanesBackward),
        };
        net.Links.Add(lk);
        net.Nodes[from].Out.Add(lk.Index);
        net.Nodes[to].In.Add(lk.Index);
    }

    /// <summary>Via do projeto a partir do pavimento: a seção gravada (faixas por lado) ou, sem ela, as larguras.</summary>
    private static TrafficRoad RoadFrom(RoadPavementDefinition pav, Polyline2 axis, double z, int n)
    {
        var setup = RoadTemplates.FromJson(pav.SetupJson);
        int Lanes(IEnumerable<ElementoSecao> side) => side.Count(e => e.Tipo is TipoElementoSecao.FaixaRolamento or TipoElementoSecao.FaixaPreferencial);
        int Bus(IEnumerable<ElementoSecao> side) => side.Count(e => e.Tipo == TipoElementoSecao.FaixaExclusiva);
        var h = pav.Hierarchy ?? (setup?.Hierarchy is { } sh && sh != HierarquiaViaria.NaoDefinida ? sh : (HierarquiaViaria?)null);
        int fwd, bwd, busF = 0, busB = 0;
        double laneW = 3.5;
        bool parkF = false, parkB = false, bike = false, twoWay = pav.TwoWay, median = false;
        double speed;
        var fromSection = false;
        if (setup != null)
        {
            twoWay = setup.TwoWay;
            var rl = Lanes(setup.Right);
            var ll = Lanes(setup.Left);
            fwd = twoWay ? rl : rl + ll;
            bwd = twoWay ? ll : 0;
            busF = twoWay ? Bus(setup.Right) : Bus(setup.Right) + Bus(setup.Left);
            busB = twoWay ? Bus(setup.Left) : 0;
            var lanes = setup.Right.Concat(setup.Left).Where(e => ElementoSecao.EhFaixaDeTrafego(e.Tipo)).Select(e => e.Largura).ToList();
            if (lanes.Count > 0) laneW = lanes.Average();
            parkF = setup.Right.Any(e => e.Tipo == TipoElementoSecao.Estacionamento);
            parkB = setup.Left.Any(e => e.Tipo == TipoElementoSecao.Estacionamento);
            bike = setup.Right.Concat(setup.Left).Any(e => e.Tipo == TipoElementoSecao.Ciclofaixa);
            median = setup.TwoWay && setup.Center == CenterTreatment.Canteiro;
            speed = setup.Speed;
            fromSection = true;
            if (fwd + bwd == 0) { fwd = Math.Max(1, busF); bwd = twoWay ? Math.Max(1, busB) : 0; }
        }
        else
        {
            if (twoWay)
            {
                fwd = Math.Max(1, (int)Math.Round(pav.RightWidth / 3.4));
                bwd = Math.Max(1, (int)Math.Round(pav.LeftWidth / 3.4));
            }
            else
            {
                fwd = Math.Max(1, (int)Math.Round((pav.RightWidth + pav.LeftWidth) / 3.4));
                bwd = 0;
            }
            speed = h is { } hh ? Hierarquia.DefaultSpeed(hh) : 50;
        }
        return new TrafficRoad
        {
            Id = pav.Id, GroupId = pav.GroupId, Hierarchy = h, Axis = axis, BaseZ = z, Grade = pav.Output.Grade, TwoWay = twoWay,
            LanesForward = fwd, LanesBackward = bwd, BusLanesForward = busF, BusLanesBackward = busB, LaneWidth = laneW,
            SpeedKmh = speed > 0 ? speed : 50, SpeedFromSection = fromSection, ParkingForward = parkF, ParkingBackward = parkB, BikeLane = bike,
            SidewalkRight = pav.RightSidewalk > 0.5, SidewalkLeft = pav.LeftSidewalk > 0.5, Median = median,
            RightWidth = pav.RightWidth, LeftWidth = pav.LeftWidth,
            Name = !string.IsNullOrWhiteSpace(pav.Notes) ? pav.Notes!.Trim() : $"V{n:00} – {Hierarquia.Label(h).Replace("Via ", "")}",
        };
    }

    /// <summary>Capacidade, velocidade livre, raio mínimo, rampa, moderação e travessias do trecho.</summary>
    private static void Characterize(TrafficNetwork net, TrafficLink lk)
    {
        var r = lk.Road;
        var rank = Hierarquia.Rank(r.Hierarchy);
        // Fluxo de saturação base por faixa (ucp/h): vias expressas/rodovias multifaixa maiores, locais menores.
        var basePerLane = rank >= 5 ? 2000 : rank == 4 ? 1800 : rank == 3 ? 1700 : rank == 2 ? 1600 : 1500;
        var fw = Math.Clamp(1 + (r.LaneWidth - 3.6) / 9.0, 0.85, 1.05);            // largura de faixa (HCM)
        var park = lk.Forward ? r.ParkingForward : r.ParkingBackward;
        var fp = park ? 0.90 : 1.0;                                               // manobras de estacionamento
        // Rampa média do trecho.
        var dz = r.Z(lk.S1) - r.Z(lk.S0);
        var grade = (lk.Forward ? dz : -dz) / Math.Max(1, lk.S1 - lk.S0) * 100;
        lk.GradePct = grade;
        var fg = Math.Clamp(1 - grade / 200.0, 0.85, 1.05);
        lk.SaturationPerLane = basePerLane * fw * fp * fg;
        lk.Capacity = lk.SaturationPerLane * lk.Lanes;
        // Raio mínimo de curva no trecho (três pontos a cada 5 m).
        var minR = double.PositiveInfinity;
        for (var s = 5.0; s < lk.Length - 5; s += 5)
        {
            var a = lk.Path.PointAt(s - 5);
            var b = lk.Path.PointAt(s);
            var c = lk.Path.PointAt(s + 5);
            var cross = Math.Abs((b - a).Cross(c - b));
            if (cross < 1e-6) continue;
            var R = a.DistanceTo(b) * b.DistanceTo(c) * a.DistanceTo(c) / (2 * cross);
            minR = Math.Min(minR, R);
        }
        lk.MinRadius = minR;
        var v = r.SpeedKmh / 3.6;
        // Velocidade segura na curva: v² = 127 R (e + f), com e + f ≈ 0,20 no urbano.
        if (!double.IsInfinity(minR)) v = Math.Min(v, Math.Sqrt(127 * minR * 0.20) / 3.6);
        if (grade > 6) v *= 0.9;
        lk.FreeSpeed = Math.Max(3, v);
        // Moderação de tráfego e travessias de pedestres cruzando o trecho.
        foreach (var (_, path, type) in net.Calming)
            foreach (var at in Crossings(lk.Path, path))
                lk.SlowPoints.Add((at, type switch
                {
                    TipoModeracao.OndulacaoB => 20 / 3.6,
                    _ => 30 / 3.6,
                }, type.ToString()));
        foreach (var (_, _, path) in net.Crosswalks)
            foreach (var at in Crossings(lk.Path, path))
                if (at > 12 && at < lk.Length - 12) lk.Crosswalks.Add((at, r.CarriageWidth));
    }

    /// <summary>Posições (ao longo de <paramref name="along"/>) onde <paramref name="across"/> o cruza.</summary>
    internal static List<double> Crossings(Polyline2 along, Polyline2 across)
    {
        var res = new List<double>();
        var acc = 0.0;
        for (int i = 0; i + 1 < along.Points.Count; i++)
        {
            var a = along.Points[i];
            var b = along.Points[i + 1];
            for (int j = 0; j + 1 < across.Points.Count; j++)
            {
                var c = across.Points[j];
                var d = across.Points[j + 1];
                var r = b - a;
                var s = d - c;
                var den = r.Cross(s);
                if (Math.Abs(den) < 1e-9) continue;
                var t = (c - a).Cross(s) / den;
                var u = (c - a).Cross(r) / den;
                if (t >= 0 && t <= 1 && u >= -0.05 && u <= 1.05) res.Add(acc + t * a.DistanceTo(b));
            }
            acc += a.DistanceTo(b);
        }
        return res;
    }

    private static void Label(TrafficNetwork net)
    {
        int i = 0, e = 0, r = 0;
        foreach (var nd in net.Nodes)
        {
            nd.Label = nd.Kind switch
            {
                TipoNo.Intersecao or TipoNo.CruzamentoSemControle => $"I{++i:00}",
                TipoNo.Rotatoria => $"R{++r:00}",
                TipoNo.Extremidade => $"E{++e:00}",
                TipoNo.CulDeSac => $"B{++e:00}",
                _ => $"C{nd.Index:00}",
            };
        }
    }
}
