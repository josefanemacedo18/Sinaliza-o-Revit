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
    /// <summary>Do eixo ao alinhamento (pista + calçada) de cada lado (m) e largura do canteiro central.</summary>
    public double TotalRight { get; init; }
    public double TotalLeft { get; init; }
    public double MedianWidth { get; init; }
    /// <summary>Menor largura de faixa ao longo da via (largura variável: estreitamentos), m.</summary>
    public double MinLaneWidth { get; init; } = 3.5;
    /// <summary>Menor largura entre meios-fios ao longo da via (m) – igual à nominal sem largura variável.</summary>
    public double MinCarriageWidth { get; init; }
    /// <summary>Estaca do ponto mais estreito (m) – só com largura variável.</summary>
    public double? NarrowestAt { get; init; }

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
    public int RoundaboutLanes { get; set; } = 1;
    public double RoundaboutRadius { get; set; }
    public bool Crosswalks { get; set; }
    /// <summary>
    /// Rotatória só do cenário (teste de solução num cruzamento): o nó passa a ser uma rotatória com as faixas e o raio do
    /// tipo que o pacote implanta, mas os trechos da rede continuam chegando ao centro – a linha de "Dê a preferência" fica
    /// no anel externo (<see cref="RoundaboutRadius"/>), onde a rotatória aplicada corta as vias.
    /// </summary>
    public bool ScenarioRoundabout { get; set; }
    private (TipoNo Kind, int Lanes, double Radius, bool Crosswalks)? _design;
    /// <summary>Guarda (na 1ª análise) e restaura o nó do projeto antes de aplicar o cenário.</summary>
    public void RestoreDesign()
    {
        _design ??= (Kind, RoundaboutLanes, RoundaboutRadius, Crosswalks);
        (Kind, RoundaboutLanes, RoundaboutRadius, Crosswalks) = _design.Value;
        ScenarioRoundabout = false;
    }
    /// <summary>Tipo do nó no projeto (sem o cenário).</summary>
    public TipoNo DesignKind => _design?.Kind ?? Kind;
    public bool Signs { get; init; }
    public List<int> In { get; } = new();
    public List<int> Out { get; } = new();
    public HashSet<string> RoadIds { get; } = new();
    public string Label { get; set; } = "";
    public bool IsZone => Kind is TipoNo.Extremidade or TipoNo.CulDeSac;
    /// <summary>Controle do projeto (o cenário pode trocar <see cref="Control"/> temporariamente).</summary>
    public ControleNo DesignControl { get; set; }
    /// <summary>Bolsões de conversão à esquerda na interseção.</summary>
    /// <summary>Bolsões de conversão à esquerda no cenário (os do projeto ou os acrescentados como teste de solução).</summary>
    public bool LeftPockets { get; set; }
    /// <summary>Bolsões de conversão à esquerda do projeto.</summary>
    public bool DesignLeftPockets { get; init; }
    /// <summary>Comprimento útil do bolsão (armazenamento + meio taper) da interseção – o mesmo que o gerador desenha (m).</summary>
    public double PocketDesignLength { get; init; } = 40;
    public double PocketDesignWidth { get; init; } = 3.0;
    /// <summary>Ilhas de conversão à direita (faixa de giro livre canalizada).</summary>
    public bool RightTurnIslands { get; init; }
    /// <summary>Plano semafórico gravado na interseção.</summary>
    public SignalPlanDef? StoredPlan { get; init; }
    /// <summary>Chave estável do nó (id da interseção/rotatória ou posição) – cenários e contagens.</summary>
    public string Key => SourceId ?? $"{Pos.X:0}:{Pos.Y:0}";

    // ---------------------------------------------------------------- regulamentação lida do projeto
    /// <summary>Conversões proibidas (R-4, R-5, R-25/R-26) por aproximação.</summary>
    public HashSet<(int In, Giro Turn)> ProhibitedTurns { get; } = new();
    /// <summary>Conversões à esquerda proibidas só no cenário (teste de solução).</summary>
    public bool ScenarioNoLeft { get; set; }
    /// <summary>Interseção com conversões à esquerda proibidas (eixo contínuo pela boca, R-4a – CTB art. 207).</summary>
    public bool NoLeftTurns { get; set; }
    /// <summary>Aproximações com PARE (R-1, legenda PARE, LRE) e com "Dê a preferência" (R-2, LDP, SDP).</summary>
    public HashSet<int> StopApproaches { get; } = new();
    public HashSet<int> YieldApproaches { get; } = new();
    /// <summary>Aproximações secundárias pela sinalização (vazio = pela via principal/hierarquia).</summary>
    public HashSet<int> MinorApproaches => StopApproaches.Union(YieldApproaches).ToHashSet();
    /// <summary>Vias com canteiro/barreira contínua atravessando o nó: não se cruza nem se converte à esquerda através delas.</summary>
    public HashSet<string> MedianRoads { get; } = new();
    /// <summary>Semáforo físico (elemento urbano) no cruzamento.</summary>
    public bool SignalHeads { get; set; }
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
    /// <summary>
    /// Bolsão de conversão à esquerda no fim do trecho (m; 0 = sem): uma faixa a mais, índice <see cref="Lanes"/>, que só
    /// existe nos últimos metros antes da retenção – quem converte à esquerda espera nela sem segurar a faixa direta.
    /// </summary>
    public double PocketLength { get; set; }
    /// <summary>Largura do bolsão (m).</summary>
    public double PocketWidth { get; set; } = 3.0;
    /// <summary>Afastamento das faixas diretas no fim do trecho (m, para fora) – o alargamento que abre o bolsão no eixo.</summary>
    public double EndShift { get; set; }
    /// <summary>Afastamento das faixas no início do trecho (m) – o mesmo alargamento, do lado de quem sai do nó pelo ramo do bolsão.</summary>
    public double StartShift { get; set; }
    public double StartShiftLength { get; set; }

    /// <summary>Posição lateral da faixa na estaca <paramref name="s"/>, com o alargamento dos bolsões (m à direita do eixo do trecho).</summary>
    public double LaneOffsetAt(int lane, double s)
    {
        var off = LaneOffset(lane);
        if (lane >= Lanes) return off;
        if (EndShift > 0) off += EndShift * Math.Clamp((s - (Length - PocketLength - 40)) / 25, 0, 1);
        if (StartShift > 0) off += StartShift * Math.Clamp(1 - (s - StartShiftLength) / 25, 0, 1);
        return off;
    }
    /// <summary>
    /// Ponto onde o trecho encontra o nó na ponta de chegada (<paramref name="atEnd"/>) ou de saída: a ponta do trecho, ou o
    /// anel externo quando o nó é uma rotatória só do cenário (os trechos seguem até o centro).
    /// </summary>
    public Vec2 NodeEdge(TrafficNetwork net, bool atEnd)
    {
        var nd = net.Nodes[atEnd ? To : From];
        if (!nd.ScenarioRoundabout) return atEnd ? Path.Points[^1] : Path.Points[0];
        var r = Math.Min(nd.RoundaboutRadius, Length * 0.45);
        return Path.PointAt(atEnd ? Length - r : r);
    }
    private List<(double At, double Width)>? _designCrosswalks;
    /// <summary>Guarda (na 1ª análise) e restaura as travessias do projeto antes de aplicar o cenário.</summary>
    public void RestoreCrosswalks()
    {
        _designCrosswalks ??= Crosswalks.ToList();
        Crosswalks.Clear();
        Crosswalks.AddRange(_designCrosswalks);
    }
    /// <summary>Faixas da microssimulação (as de tráfego mais o bolsão).</summary>
    public int SimLanes => Lanes + (PocketLength > 0 ? 1 : 0);
    public double FreeSpeed { get; set; }          // m/s
    public double Capacity { get; set; }           // veh/h (todas as faixas)
    public double SaturationPerLane { get; set; }  // veh/h/faixa
    /// <summary>Atrito lateral da faixa (largura, estacionamento, rampa – fw·fp·fg do HCM): alonga o intervalo na microssimulação.</summary>
    public double Friction { get; set; } = 1.0;
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
        // Bolsão de conversão à esquerda: faixa a mais no eixo (alargamento – as faixas diretas se afastam, EndShift) ou
        // recortada no canteiro, só no fim do trecho.
        if (lane >= Lanes && PocketLength > 0) return Road.TwoWay ? (Road.Median ? 1.0 - PocketWidth / 2 : 0) : (Lanes / 2.0 - Lanes - 0.5) * w;
        lane = Math.Clamp(lane, 0, Lanes - 1);
        if (!Road.TwoWay) return (Lanes / 2.0 - lane - 0.5) * w;
        var inner = Road.Median ? 1.0 : 0.0;
        return inner + (Lanes - lane - 0.5) * w;
    }

    public string Name => Road.Name + (Road.TwoWay ? (Forward ? " (sentido do eixo)" : " (sentido contrário)") : "");
    /// <summary>Chave estável da aproximação: id da via + sentido.</summary>
    public string Key => Road.Id + (Forward ? "|+" : "|-");
    /// <summary>Pontos de ônibus no trecho: posição (m), em baia (fora da faixa) ou na faixa, id da marca.</summary>
    public List<(double At, bool Bay, string? Id)> BusStops { get; } = new();
    /// <summary>Faixa de desaceleração (conversão à direita) antes do nó de jusante: extensão (m), 0 = sem.</summary>
    public double DecelLane { get; set; }
    /// <summary>Faixa de aceleração depois do nó de montante: extensão (m), 0 = sem.</summary>
    public double AccelLane { get; set; }
    /// <summary>Equivalente de caminhão/ônibus em automóveis no trecho (cresce nos aclives – HCM).</summary>
    public double HeavyEquivalent { get; set; } = 2.0;
    /// <summary>Menor largura de faixa no trecho (m).</summary>
    public double LaneWidth { get; set; } = 3.5;

    // ---------------------------------------------------------------- regulamentação lida do projeto
    /// <summary>Velocidade máxima regulamentada no trecho (R-19), km/h. Nulo = a da via.</summary>
    public double? SpeedLimitKmh { get; set; }
    /// <summary>Parcela do trecho com ultrapassagem proibida (LFO-1/LFO-3, R-7), 0–1.</summary>
    public double NoPassing { get; set; }
    /// <summary>Troca de faixa proibida (LMS-1, R-8, tachões/balizadores entre faixas).</summary>
    public bool LaneChangeForbidden { get; set; }
    /// <summary>Faixas fechadas num trecho: zebrado/canalização, obra (cones), faixa exclusiva pintada…</summary>
    public List<(double S0, double S1, int Lane, string Why)> ClosedLanes { get; } = new();
    /// <summary>Movimentos permitidos por faixa (setas PEM junto ao fim do trecho). Faixa sem seta = livre.</summary>
    public Dictionary<int, HashSet<Giro>> LaneTurns { get; } = new();
    /// <summary>Trecho fechado ao tráfego geral (R-3 contra o sentido, R-10, R-32, bloqueio físico transversal).</summary>
    public bool Closed { get; set; }
    public string? ClosedWhy { get; set; }
    /// <summary>Estacionamento: R-6a/R-6c proíbem (false), R-6b/vagas pintadas regulamentam (true). Nulo = o da seção.</summary>
    public bool? ParkingOverride { get; set; }
    /// <summary>R-9: caminhões proibidos.</summary>
    public bool TrucksForbidden { get; set; }
    /// <summary>Travessias de pedestres semaforizadas no meio da quadra (posição ao longo do trecho).</summary>
    public List<double> SignalizedCrossings { get; } = new();
    /// <summary>
    /// Tempos das travessias semaforizadas do trecho no cenário (ciclo, vermelho dos veículos, defasagem): as do projeto
    /// com o plano padrão ou o do cenário, e as criadas no simulador.
    /// </summary>
    public List<CrossingPlan> CrossingPlans { get; } = new();
    /// <summary>Capacidade do trecho sem as travessias semaforizadas (veh/h).</summary>
    public double BaseCapacity { get; set; }
    /// <summary>Quantas faixas ficam abertas no ponto mais restrito do trecho.</summary>
    public int MinOpenLanes => Math.Max(0, Lanes - (ClosedLanes.Count == 0 ? 0 : ClosedLanes.GroupBy(c => c.Lane).Count()));
    /// <summary>A faixa está fechada na posição <paramref name="s"/>.</summary>
    public bool LaneClosedAt(int lane, double s) => ClosedLanes.Any(c => c.Lane == lane && s >= c.S0 && s <= c.S1);
    /// <summary>Movimentos permitidos na faixa (setas); nulo = livre.</summary>
    public HashSet<Giro>? TurnsOf(int lane) => LaneTurns.TryGetValue(lane, out var t) ? t : null;
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
    /// <summary>Áreas de escape de caminhões (id, início da caixa).</summary>
    public List<(string Id, Vec2 Pos)> EscapeRamps { get; } = new();
    /// <summary>Sonorizadores longitudinais (id, caminho).</summary>
    public List<(string Id, Polyline2 Path)> RumbleStrips { get; } = new();
    /// <summary>Tudo o que a sinalização do projeto significa para o tráfego (lido e aplicado, ou não associado).</summary>
    public List<TrafficRegulation> Regulations { get; } = new();
    public List<string> Notes { get; } = new();
    /// <summary>Planta real do projeto (pavimento, calçadas, canteiros e pintura) para o mapa; vazia sem geometria.</summary>
    public List<MapShape> Backdrop { get; } = new();

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
/// <summary>Travessia semaforizada no meio da quadra: estaca no trecho, ciclo, vermelho dos veículos e defasagem (s).</summary>
public sealed record CrossingPlan(double At, double Cycle, double Red, double Offset, Vec2 Pos);

public static class TrafficNetworkBuilder
{
    /// <param name="axisOf">Eixo resolvido (m) e cota base de uma marca com caminho; nulo se não resolvido.</param>
    /// <param name="geometryOf">Geometria de uma marca (zebrados e canalizações que fecham faixas); opcional.</param>
    public static TrafficNetwork Build(IReadOnlyCollection<MarkingDefinition> defs, Func<MarkingDefinition, (Polyline2 Axis, double Z)?> axisOf,
        Func<MarkingDefinition, Model.MarkingGeometry?>? geometryOf = null)
    {
        var net = new TrafficNetwork();
        // Geometria calculada uma vez por marca (a leitura da sinalização e a planta do mapa usam as mesmas peças).
        if (geometryOf != null)
        {
            var inner = geometryOf;
            var memo = new Dictionary<string, Model.MarkingGeometry?>();
            geometryOf = d => memo.TryGetValue(d.Id, out var g) ? g : memo[d.Id] = inner(d);
        }
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
            bool crosswalks = false, bool signs = false, bool pockets = false, bool islands = false, SignalPlanDef? plan = null, double pocketLength = 40, double pocketWidth = 3.0)
        {
            var nd = new TrafficNode
            {
                Index = net.Nodes.Count, Kind = kind, Control = control, DesignControl = control, Pos = pos, Z = z, SourceId = src, MainRoadId = main,
                RoundaboutLanes = rbLanes, RoundaboutRadius = rbR, Crosswalks = crosswalks, Signs = signs, LeftPockets = pockets, DesignLeftPockets = pockets,
                RightTurnIslands = islands, StoredPlan = plan, PocketDesignLength = pocketLength, PocketDesignWidth = pocketWidth,
            };
            net.Nodes.Add(nd);
            return nd;
        }

        foreach (var it in defs.OfType<IntersectionDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
        {
            var roads = it.RoadIds.Select(net.Road).Where(r => r != null).Cast<TrafficRoad>().ToList();
            if (roads.Count < 2) continue;
            // Emenda de duas vias pela ponta (continuação em curva): não é cruzamento, o tráfego segue livre.
            if (roads.Count == 2 && roads.All(r => { var st = r.Axis.Project(it.Node).Station; return st < 2 || st > r.Axis.Length - 2; }))
            {
                var cn = NewNode(TipoNo.Continuacao, ControleNo.Livre, it.Node, it.Z, it.Id);
                foreach (var r in roads) Attach(r, r.Axis.Project(it.Node).Station, cn);
                continue;
            }
            var control = it.Control switch
            {
                ControleIntersecao.Pare => ControleNo.Pare,
                ControleIntersecao.DePreferencia => ControleNo.DePreferencia,
                ControleIntersecao.Semaforo => ControleNo.Semaforo,
                _ => ControleNo.PreferenciaDireita,
            };
            var nd = NewNode(TipoNo.Intersecao, control, it.Node, it.Z, it.Id, it.MainRoadId, crosswalks: it.Crosswalks, signs: it.Signs,
                pockets: it.LeftTurnPockets, islands: it.RightTurnIslands != TipoIlha.Nenhuma, plan: it.SignalPlan,
                pocketLength: Math.Max(5, it.PocketLength) + Math.Max(5, it.PocketTaper) / 2, pocketWidth: Math.Clamp(it.PocketWidth, 2.5, 5.0));
            nd.NoLeftTurns = !it.LeftTurns;
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

        foreach (var er in defs.OfType<EscapeRampDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
            if (axisOf(er) is { } a && a.Axis.Points.Count > 0) net.EscapeRamps.Add((er.Id, a.Axis.Points[0]));
        foreach (var rs in defs.OfType<RumbleStripDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
            if (axisOf(rs) is { } a) net.RumbleStrips.Add((rs.Id, a.Axis));
        ReadRecesses(net, defs, axisOf);
        ReadBusStops(net, defs);
        foreach (var nd in net.Nodes.Where(x => x.NoLeftTurns))
            foreach (var li in nd.In)
            {
                nd.ProhibitedTurns.Add((li, Giro.Esquerda));
                nd.ProhibitedTurns.Add((li, Giro.Retorno));
            }
        foreach (var nd in net.Nodes) SetPockets(net, nd);
        // Antes: as travessias só entravam nos trechos depois da leitura da sinalização, e o grupo focal de uma travessia no
        // meio da quadra ficava "longe de faixas de pedestres" – o semáforo de pedestres do projeto era ignorado.
        AssignCrosswalks(net);
        TrafficRegulations.Read(net, defs, axisOf, geometryOf);
        if (geometryOf != null) TrafficBackdrop.Read(net, defs, geometryOf);

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
        // Largura variável: o ponto mais estreito limita a largura das faixas (gargalo).
        double minCarriage = pav.RightWidth + pav.LeftWidth, minLane = laneW;
        double? narrowAt = null;
        if (pav.HasEdgeVariation)
        {
            var (fn, _) = pav.EdgeFunctions(axis);
            var totalLanes = Math.Max(1, fwd + bwd + busF + busB);
            var nominal = pav.RightWidth + pav.LeftWidth;
            for (var st = 0.0; st <= axis.Length + 1e-6; st += Math.Max(2, axis.Length / 200))
            {
                var v = fn(Math.Min(st, axis.Length));
                var wdt = nominal + v.CurbL + v.CurbR;
                if (wdt < minCarriage - 1e-6) { minCarriage = wdt; narrowAt = st; }
            }
            minLane = Math.Max(2.0, laneW - Math.Max(0, nominal - minCarriage) / totalLanes);
        }
        return new TrafficRoad
        {
            MinCarriageWidth = minCarriage, MinLaneWidth = minLane, NarrowestAt = narrowAt,
            TotalRight = pav.TotalRight, TotalLeft = pav.TotalLeft,
            MedianWidth = pav.Gaps.Where(g => g.Median && Math.Abs(g.Offset) < 0.3).Select(g => g.Width).DefaultIfEmpty(0).Max(),
            Id = pav.Id, GroupId = pav.GroupId, Hierarchy = h, Axis = axis, BaseZ = z, Grade = pav.Output.Grade, TwoWay = twoWay,
            LanesForward = fwd, LanesBackward = bwd, BusLanesForward = busF, BusLanesBackward = busB, LaneWidth = laneW,
            SpeedKmh = speed > 0 ? speed : 50, SpeedFromSection = fromSection, ParkingForward = parkF, ParkingBackward = parkB, BikeLane = bike,
            SidewalkRight = pav.RightSidewalk > 0.5, SidewalkLeft = pav.LeftSidewalk > 0.5, Median = median,
            RightWidth = pav.RightWidth, LeftWidth = pav.LeftWidth,
            Name = !string.IsNullOrWhiteSpace(pav.Notes) ? pav.Notes!.Trim() : $"V{n:00} – {Hierarquia.Label(h).Replace("Via ", "")}",
        };
    }

    /// <summary>Trecho direcional da via que passa na estaca <paramref name="s"/> no sentido pedido.</summary>
    private static TrafficLink? LinkAt(TrafficNetwork net, TrafficRoad r, double s, bool forward) =>
        net.Links.FirstOrDefault(l => ReferenceEquals(l.Road, r) && l.Forward == forward && s >= l.S0 - 0.5 && s <= l.S1 + 0.5);

    /// <summary>Via cujo eixo passa junto ao ponto (até <paramref name="tol"/> m do eixo, ou dentro da pista).</summary>
    private static (TrafficRoad Road, double S, double Signed)? RoadNear(TrafficNetwork net, Vec2 p, double tol)
    {
        (TrafficRoad, double, double)? best = null;
        var bd = double.MaxValue;
        foreach (var r in net.Roads)
        {
            var pr = r.Axis.Project(p);
            var d = Math.Abs(pr.Signed);
            var reach = (pr.Signed > 0 ? r.LeftWidth : r.RightWidth) + tol;
            if (d > reach || d >= bd) continue;
            bd = d;
            best = (r, pr.Station, pr.Signed);
        }
        return best;
    }

    /// <summary>Baias de ônibus e faixas de aceleração/desaceleração (recuos da via) ligadas aos trechos.</summary>
    private static void ReadRecesses(TrafficNetwork net, IReadOnlyCollection<MarkingDefinition> defs, Func<MarkingDefinition, (Polyline2 Axis, double Z)?> axisOf)
    {
        foreach (var rc in defs.OfType<RecessMarkingDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
        {
            if (axisOf(rc) is not { } a) continue;
            var ax = a.Axis;
            var mid = ax.PointAt(Math.Clamp((rc.S1 + rc.S2) / 2, 0, ax.Length));
            var r = net.Roads.Where(x => Math.Abs(x.Axis.Project(mid).Signed) < 1.0).OrderBy(x => Math.Abs(x.Axis.Project(mid).Signed)).FirstOrDefault();
            if (r == null) continue;
            double St(double s) => r.Axis.Project(ax.PointAt(Math.Clamp(s, 0, ax.Length))).Station;
            // Lado esquerdo de via de mão dupla = tráfego contra o eixo.
            var forward = !rc.Reverse && !(rc.Left && r.TwoWay);
            var s1 = St(rc.S1);
            var s2 = St(rc.S2);
            var lk = LinkAt(net, r, (s1 + s2) / 2, forward);
            if (lk == null) continue;
            double Along(double s) => lk.Forward ? s - lk.S0 : lk.S1 - s;
            switch (rc.Type)
            {
                case TipoRecuo.BaiaOnibus:
                    lk.BusStops.Add((Math.Clamp(Along((s1 + s2) / 2), 0, lk.Length), true, rc.Id));
                    break;
                case TipoRecuo.FaixaDesaceleracao:
                    // A faixa termina junto ao nó de jusante: extensão útil = do início da faixa até o fim do trecho.
                    lk.DecelLane = Math.Max(lk.DecelLane, lk.Length - Math.Clamp(Math.Min(Along(s1), Along(s2)), 0, lk.Length));
                    break;
                case TipoRecuo.FaixaAceleracao:
                    lk.AccelLane = Math.Max(lk.AccelLane, Math.Clamp(Math.Max(Along(s1), Along(s2)), 0, lk.Length));
                    break;
            }
        }
    }

    /// <summary>Pontos de ônibus sem baia (placa de ponto de ônibus ou abrigo junto ao meio-fio): param na faixa.</summary>
    private static void ReadBusStops(TrafficNetwork net, IReadOnlyCollection<MarkingDefinition> defs)
    {
        var points = new List<(Vec2 P, string Id)>();
        foreach (var sg in defs.OfType<SignDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
        {
            var c = (sg.Code ?? "").ToUpperInvariant();
            if (c.Contains("ONIBUS") || c.Contains("ÔNIBUS") || c == "SAU-19" || (sg.Legend ?? "").Contains("ÔNIBUS", StringComparison.OrdinalIgnoreCase))
                points.Add((sg.Position, sg.Id));
        }
        foreach (var ue in defs.OfType<UrbanElementDefinition>().GroupBy(d => d.Id).Select(g => g.First()))
            if ((ue.Code ?? "").StartsWith("ABRIGO", StringComparison.OrdinalIgnoreCase) && !ue.UsePath)
                points.Add((ue.Position, ue.Id));
        foreach (var (p, id) in points)
        {
            if (RoadNear(net, p, 8) is not { } hit) continue;
            var (r, s, signed) = hit;
            var forward = signed <= 0 || !r.TwoWay;
            var lk = LinkAt(net, r, s, forward);
            if (lk == null) continue;
            var at = Math.Clamp(lk.Forward ? s - lk.S0 : lk.S1 - s, 0, lk.Length);
            // Já é uma baia (placa e abrigo que acompanham o recuo).
            if (lk.BusStops.Any(b => Math.Abs(b.At - at) < 30)) continue;
            lk.BusStops.Add((at, false, id));
        }
    }

    /// <summary>Capacidade, velocidade livre, raio mínimo, rampa, moderação e travessias do trecho.</summary>
    /// <summary>
    /// Aproximações que recebem o bolsão de conversão à esquerda – a mesma regra do gerador da interseção: ramos da via
    /// principal, de mão dupla, com comprimento para o bolsão e canteiro (se houver) de pelo menos 3,00 m.
    /// </summary>
    public static IEnumerable<TrafficLink> PocketApproaches(TrafficNetwork net, TrafficNode nd)
    {
        if (nd.Kind != TipoNo.Intersecao) yield break;
        var main = nd.MainRoadId ?? nd.RoadIds.FirstOrDefault();
        foreach (var li in nd.In)
        {
            var l = net.Links[li];
            if (l.Road.Id != main || !l.Road.TwoWay || l.Length < nd.PocketDesignLength + 15) continue;
            if (l.Road.Median && l.Road.MedianWidth < 3.0) continue;
            yield return l;
        }
    }

    /// <summary>Bolsões do nó no cenário (<see cref="TrafficNode.LeftPockets"/>): faixa a mais no fim das aproximações da principal.</summary>
    public static void SetPockets(TrafficNetwork net, TrafficNode nd)
    {
        foreach (var li in nd.In) { net.Links[li].PocketLength = 0; net.Links[li].EndShift = 0; }
        foreach (var lo in nd.Out) { net.Links[lo].StartShift = 0; net.Links[lo].StartShiftLength = 0; }
        if (!nd.LeftPockets) return;
        foreach (var l in PocketApproaches(net, nd))
        {
            l.PocketLength = nd.PocketDesignLength;
            l.PocketWidth = nd.PocketDesignWidth;
            if (l.Road.Median) continue;
            // Alargamento (sem canteiro): as faixas diretas deste sentido e as de quem sai pelo mesmo ramo se afastam P/2.
            l.EndShift = nd.PocketDesignWidth / 2;
            if (nd.Out.Select(o => net.Links[o]).FirstOrDefault(o => o.Road.Id == l.Road.Id && o.Forward != l.Forward) is { } rev)
            {
                rev.StartShift = nd.PocketDesignWidth / 2;
                rev.StartShiftLength = l.PocketLength + 15;
            }
        }
    }

    private static void Characterize(TrafficNetwork net, TrafficLink lk)
    {
        var r = lk.Road;
        var rank = Hierarquia.Rank(r.Hierarchy);
        // Fluxo de saturação base por faixa (ucp/h): vias expressas/rodovias multifaixa maiores, locais menores.
        var basePerLane = rank >= 5 ? 2000 : rank == 4 ? 1800 : rank == 3 ? 1700 : rank == 2 ? 1600 : 1500;
        // Largura de faixa no trecho: a menor quando o estreitamento cai dentro dele.
        lk.LaneWidth = r.NarrowestAt is { } na && na >= lk.S0 - 1 && na <= lk.S1 + 1 ? r.MinLaneWidth : r.LaneWidth;
        var fw = Math.Clamp(1 + (lk.LaneWidth - 3.6) / 9.0, 0.80, 1.05);           // largura de faixa (HCM)
        var park = lk.ParkingOverride ?? (lk.Forward ? r.ParkingForward : r.ParkingBackward);
        var fp = park ? 0.90 : 1.0;                                               // manobras de estacionamento
        // Rampa média do trecho.
        var dz = r.Z(lk.S1) - r.Z(lk.S0);
        var grade = (lk.Forward ? dz : -dz) / Math.Max(1, lk.S1 - lk.S0) * 100;
        lk.GradePct = grade;
        var fg = Math.Clamp(1 - grade / 200.0, 0.85, 1.05);
        // Caminhões e ônibus nos aclives: equivalente maior (HCM – veículos pesados em rampa).
        lk.HeavyEquivalent = 2.0 + Math.Max(0, grade - 2) * 0.35;
        // Pontos de ônibus na faixa (sem baia) bloqueiam a faixa da direita durante o embarque.
        var inLaneStops = lk.BusStops.Count(b => !b.Bay);
        lk.SaturationPerLane = basePerLane * fw * fp * fg;
        lk.Friction = fw * fp * fg;
        // Bloqueio por ônibus parados na faixa (HCM: fbb = (N − 14,4·Nb/3600)/N, 12 ônibus/h de referência).
        var fbb = inLaneStops > 0 ? Math.Max(0.5, (lk.Lanes - inLaneStops * 14.4 * 12 / 3600.0) / lk.Lanes) : 1.0;
        lk.Capacity = lk.SaturationPerLane * lk.Lanes * fbb;
        // Faixas fechadas (zebrado, cones, faixa exclusiva pintada): o trecho vale pelo gargalo.
        if (lk.ClosedLanes.Count > 0) lk.Capacity *= (double)lk.MinOpenLanes / Math.Max(1, lk.Lanes);
        // Travessia semaforizada no meio da quadra: o trecho só escoa no verde dos veículos (ciclo 75 s, 18 s de pedestres + 4 s).
        lk.BaseCapacity = lk.Capacity;
        foreach (var _ in lk.SignalizedCrossings) lk.Capacity *= 53.0 / 75.0;
        if (lk.Closed || lk.MinOpenLanes == 0) { lk.Capacity = 1; lk.Closed = true; lk.ClosedWhy ??= "todas as faixas fechadas"; }
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
        var v = (lk.SpeedLimitKmh ?? r.SpeedKmh) / 3.6;
        // Ultrapassagem proibida em pista simples de mão dupla (HCM – pistas simples): velocidade média menor.
        if (lk.Lanes == 1 && r.TwoWay && r.SpeedKmh >= 60) v *= 1 - 0.06 * Math.Clamp(lk.NoPassing, 0, 1);
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
    }

    /// <summary>
    /// Travessias de pedestres (FTP) que cruzam cada trecho fora da boca dos cruzamentos – antes da leitura da
    /// sinalização, que precisa delas para achar o semáforo de travessia no meio da quadra.
    /// </summary>
    private static void AssignCrosswalks(TrafficNetwork net)
    {
        foreach (var lk in net.Links)
            foreach (var (_, _, path) in net.Crosswalks)
                foreach (var at in Crossings(lk.Path, path))
                    if (at > 12 && at < lk.Length - 12 && !lk.Crosswalks.Any(c => Math.Abs(c.At - at) < 1)) lk.Crosswalks.Add((at, lk.Road.CarriageWidth));
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
