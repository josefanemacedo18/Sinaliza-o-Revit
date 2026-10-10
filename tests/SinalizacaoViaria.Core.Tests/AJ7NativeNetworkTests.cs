using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Traffic;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada 7, item 7: o simulador reconhece vias feitas com pisos do Revit (fora da ferramenta) e linhas de eixo, com as
/// faixas, os sentidos, os movimentos e o controle lidos da sinalização do plugin sobre elas – só polígonos e marcas, sem
/// definição de via. A mesma cena feita pelo plugin dá as mesmas faixas e os mesmos movimentos.
/// </summary>
public class AJ7NativeNetworkTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private const TipoElementoSecao R = TipoElementoSecao.FaixaRolamento;
    private static ElementoSecao E(TipoElementoSecao t, double w) => new() { Tipo = t, Largura = w };

    /// <summary>Seções: as da matriz de interseções e a mão única com as setas de sentido.</summary>
    private static RoadSetup Section(string sec)
    {
        if (sec == "mao1") return new RoadSetup
        {
            TwoWay = false, Hierarchy = HierarquiaViaria.Local, Speed = 40, Center = CenterTreatment.Nenhum, SarjetaSomada = true, SetasSentido = true,
            Right = { E(R, 3.50), E(TipoElementoSecao.Calcada, 2.50) },
            Left = { E(TipoElementoSecao.Calcada, 2.50) },
        };
        return AB2Matrix.Sections[sec]();
    }

    /// <summary>Cena do plugin: vias pelos eixos e as interseções nos nós (com faixas, retenção, PARE e R-1).</summary>
    private static Y34TipTests.World Plugin(IntersectionDefinition tpl, params (string Sec, Vec2[] Pts)[] roads)
    {
        var w = new Y34TipTests.World();
        foreach (var (sec, pts) in roads)
        {
            var pr = PathReference.FromPoints(pts, 0);
            var g = Section(sec).Build(pr, new OutputSettings(), Cat);
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            if (!IntersectionGenerator.NeedsIntersection(rs, node)) continue;
            var d = (IntersectionDefinition)tpl.CloneWithNewId();
            d.Node = node;
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, Cat));
        }
        return w;
    }

    private static IntersectionDefinition Pare(bool control = true) => new()
    {
        Control = control ? ControleIntersecao.Pare : ControleIntersecao.Nenhum, Crosswalks = true, Ramps = false, Signs = control, StopLines = control,
    };

    private static (Polyline2, double)? Ax(Dictionary<string, Polyline2> paths, MarkingDefinition d) =>
        paths.TryGetValue(d.Id, out var p) ? (p, 0.0) : (d.Path ?? (d as HatchMarkingDefinition)?.Boundary) is { Points.Count: >= 2 } pr ? (new Polyline2(pr.Points, pr.Closed), 0.0) : null;

    private static TrafficNetwork PluginNet(Y34TipTests.World w)
    {
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => w.Defs.FirstOrDefault(x => x.Id == id), AllDefinitions = () => w.Defs, PathOf = d => Ax(w.Paths, d)?.Item1 };
        return TrafficNetworkBuilder.Build(w.Defs, d => Ax(w.Paths, d), d => MarkingBuilder.Build(d, Ax(w.Paths, d)?.Item1, ctx));
    }

    /// <summary>
    /// A mesma cena sem definição de via: o pavimento (asfalto das vias e da interseção) vira pisos nativos e cada marca fica
    /// com a linha dela (o afastamento em relação ao eixo da via já aplicado – ela não sabe de via nenhuma).
    /// </summary>
    public sealed class Native
    {
        public List<MarkingDefinition> Defs { get; } = new();
        public Dictionary<string, Polyline2> Paths { get; } = new();
        public NetworkSources Sources { get; } = new();

        public TrafficNetwork Network(bool geometry = true)
        {
            var ctx = new BuildContext { Catalog = Cat, Lookup = id => Defs.FirstOrDefault(x => x.Id == id), AllDefinitions = () => Defs, PathOf = d => Ax(Paths, d)?.Item1 };
            return TrafficNetworkBuilder.Build(Defs, d => Ax(Paths, d), geometry ? d => MarkingBuilder.Build(d, Ax(Paths, d)?.Item1, ctx) : null, Sources);
        }
    }

    private static bool Physical(MarkingDefinition d) => d is LinearMarkingDefinition l &&
        (l.Code.StartsWith("CALCADA") || l.Code.StartsWith("MEIO-FIO") || l.Code.StartsWith("SARJETA") || l.Code.StartsWith("GRAMADO") || l.Code.StartsWith("PLATAFORMA"));

    public static Native ToNative(Y34TipTests.World w, string floorName = "Asfalto CBUQ 5 cm")
    {
        var n = new Native();
        var parts = Y34TipTests.Geometry(w);
        var pave = PolygonOps.Union(parts.Where(p => p.Def is RoadPavementDefinition or IntersectionDefinition && p.Piece.Color == MarkingColor.Asfalto
                                                     && p.Piece.Solid == null && p.Piece.Profile == null).Select(p => p.Piece.Shape));
        for (int i = 0; i < pave.Count; i++) n.Sources.Floors.Add(new NativeFloor($"F{i + 1}", pave[i], 0, floorName));
        foreach (var d in w.Defs)
        {
            if (d is RoadPavementDefinition or IntersectionDefinition or RampDefinition || Physical(d)) continue;
            var c = MarkingDefinition.FromJson(d.ToJson())!;
            c.GroupId = null;
            if (w.Paths.TryGetValue(d.Id, out var axis))
            {
                switch (c)
                {
                    case LinearMarkingDefinition l:
                        var line = Math.Abs(l.Offset) > 1e-6 ? axis.Offset(l.Offset) : axis;
                        l.Offset = 0;
                        l.SetPath(PathReference.FromPoints(line.Points, 0));
                        n.Paths[c.Id] = line;
                        break;
                    case RepeatedMarkingDefinition r:
                        var rl = Math.Abs(r.Offset) > 1e-6 ? axis.Offset(r.Offset) : axis;
                        r.Offset = 0;
                        r.SetPath(PathReference.FromPoints(rl.Points, 0));
                        n.Paths[c.Id] = rl;
                        break;
                    default:
                        n.Paths[c.Id] = axis;      // vagas: a linha de referência delas
                        break;
                }
            }
            n.Defs.Add(c);
        }
        return n;
    }

    // ------------------------------------------------------------------ leitura das aproximações

    /// <summary>Aproximação de um cruzamento: rumo (graus), faixas, movimentos permitidos e se para (PARE).</summary>
    public sealed record Approach(int Heading, int Lanes, string Turns, bool Stop);

    private static int Heading(TrafficLink lk)
    {
        var t = lk.Path.TangentAt(lk.Length);
        var deg = Math.Atan2(t.Y, t.X) * 180 / Math.PI;
        return ((int)Math.Round(deg / 15) * 15 + 360) % 360;
    }

    /// <summary>Aproximações do cruzamento mais próximo de <paramref name="at"/> (até 6 m).</summary>
    public static List<Approach> Approaches(TrafficNetwork net, Vec2 at)
    {
        var nd = net.Nodes.Where(n => !n.IsZone && n.Kind != TipoNo.Continuacao).OrderBy(n => n.Pos.DistanceTo(at)).FirstOrDefault();
        Assert.True(nd != null && nd.Pos.DistanceTo(at) < 6, "cruzamento não encontrado em " + at);
        return nd!.In.Select(i => net.Links[i]).Select(lk => new Approach(Heading(lk), lk.Lanes,
                string.Join(",", nd.Out.Where(o => TrafficAnalysis.Allowed(net, nd, lk.Index, o)).Select(o => TrafficAnalysis.TurnOf(net, lk.Index, o)).Distinct().OrderBy(g => g)),
                nd.StopApproaches.Contains(lk.Index)))
            .OrderBy(a => a.Heading).ToList();
    }

    private static string Show(IEnumerable<Approach> a) => string.Join(" | ", a.Select(x => $"{x.Heading}°: {x.Lanes} fx [{x.Turns}]{(x.Stop ? " PARE" : "")}"));

    private static readonly Vec2[] EW = { new(-90, 0), new(90, 0) };
    private static readonly Vec2[] NS = { new(0, -90), new(0, 90) };
    private static readonly Vec2[] N = { Vec2.Zero, new(0, 90) };

    // ------------------------------------------------------------------ testes

    [Fact]
    public void Sem_pisos_nem_linhas_o_simulador_so_le_vias_do_plugin()
    {
        // Antes (TrafficNetwork.cs, só RoadPavementDefinition): pisos e marcas sem via eram descartados – nenhuma via.
        var nat = ToNative(Plugin(Pare(), ("local", EW), ("local", NS)));
        var old = TrafficNetworkBuilder.Build(nat.Defs, d => Ax(nat.Paths, d));
        Assert.Empty(old.Roads);
        Assert.Contains(old.Notes, x => x.Contains("Nenhuma via do plugin"));
        // Com os pisos: duas vias.
        Assert.Equal(2, nat.Network().Roads.Count);
    }

    [Fact]
    public void Cruz_de_pisos_nativos_gera_faixas_sentidos_e_movimentos()
    {
        var net = ToNative(Plugin(Pare(), ("local", EW), ("local", NS))).Network();
        Assert.Equal(2, net.Roads.Count);
        Assert.All(net.Roads, r =>
        {
            Assert.True(r.TwoWay, r.Name);
            Assert.Equal(1, r.LanesForward);
            Assert.Equal(1, r.LanesBackward);
            Assert.Equal(OrigemTrecho.Piso, r.Origin);
        });
        var ap = Approaches(net, Vec2.Zero);
        Assert.Equal(4, ap.Count);
        Assert.All(ap, a => Assert.Equal("Direita,Frente,Esquerda", a.Turns));
        Assert.All(ap, a => Assert.Equal(1, a.Lanes));
        // A rede de pisos é simulada: veículos entram, cruzam e saem.
        var macro = TrafficAnalysis.Run(net, new TrafficOptions { Demand = NivelDemanda.Media, SimSeconds = 300, WarmupSeconds = 60 });
        var sim = TrafficSimulation.Run(macro);
        Assert.True(sim.Completed > 20, $"{sim.Completed} de {sim.Spawned}");
    }

    [Fact]
    public void T_de_pisos_nativos_gera_faixas_sentidos_e_movimentos()
    {
        var net = ToNative(Plugin(Pare(), ("coletora", EW), ("local", N))).Network();
        var ap = Approaches(net, Vec2.Zero);
        Assert.True(ap.Count == 3, Show(ap));
        // Leste→oeste e oeste→leste pela coletora (2 faixas por sentido) e a local chegando do norte (1 faixa).
        Assert.Contains(new Approach(0, 2, "Frente,Esquerda", false), ap);
        Assert.Contains(new Approach(180, 2, "Direita,Frente", false), ap);
        Assert.Contains(ap, a => a.Heading == 270 && a.Lanes == 1 && a.Turns == "Direita,Esquerda");
    }

    [Fact]
    public void Mao_unica_de_pisos_nativos_tem_um_sentido_so()
    {
        var net = ToNative(Plugin(Pare(), ("coletora", EW), ("mao1", NS))).Network();
        var one = net.Roads.Single(r => !r.TwoWay);
        Assert.Equal(1, one.LanesForward);
        Assert.Equal(0, one.LanesBackward);
        Assert.True(one.Axis.Points[^1].Y > one.Axis.Points[0].Y, "a mão única segue para o norte, pelas setas");
        var ap = Approaches(net, Vec2.Zero);
        Assert.True(ap.Count == 3, Show(ap));
        Assert.Contains(ap, a => a.Heading == 90 && a.Lanes == 1 && a.Turns == "Direita,Frente,Esquerda");
        Assert.Contains(ap, a => a.Heading == 0 && a.Lanes == 2 && a.Turns == "Frente,Esquerda");
        Assert.Contains(ap, a => a.Heading == 180 && a.Lanes == 2 && a.Turns == "Direita,Frente");
        Assert.DoesNotContain(ap, a => a.Heading == 270);
    }

    [Fact]
    public void Setas_restringem_as_conversoes()
    {
        var w = Plugin(Pare(), ("local", EW), ("local", NS));
        // Seta "só em frente" na faixa de quem vem do oeste, antes da faixa de pedestres.
        var arrow = new SymbolMarkingDefinition { Code = "PEM-F", Position = new Vec2(-25, -1.6), Direction = Vec2.UnitX };
        w.Defs.Add(arrow);
        var plugin = Approaches(PluginNet(w), Vec2.Zero);
        var native = Approaches(ToNative(w).Network(), Vec2.Zero);
        Assert.Contains(new Approach(0, 1, "Frente", false), native);
        Assert.Contains(new Approach(0, 1, "Frente", false), plugin);
        Assert.Equal(3, native.Count(a => a.Turns == "Direita,Frente,Esquerda"));
    }

    [Fact]
    public void PARE_vira_controle()
    {
        var net = ToNative(Plugin(Pare(), ("coletora", EW), ("local", NS))).Network();
        var nd = net.Nodes.Single(n => !n.IsZone);
        Assert.Equal(ControleNo.Pare, nd.Control);
        Assert.Equal(2, nd.StopApproaches.Count);
        Assert.All(nd.StopApproaches, i => Assert.True(Math.Abs(net.Links[i].Path.TangentAt(net.Links[i].Length).X) < 0.2, "PARE na via local (norte-sul)"));
        Assert.Contains(net.Regulations, r => r.Kind == TipoRegra.Parada && r.Applied);
        // Sem a sinalização de PARE: cruzamento sem controle (preferência da direita).
        var free = ToNative(Plugin(Pare(control: false), ("coletora", EW), ("local", NS))).Network();
        Assert.Equal(ControleNo.PreferenciaDireita, free.Nodes.Single(n => !n.IsZone).Control);
    }

    public static IEnumerable<object[]> Same() => new[]
    {
        new object[] { "cruz", "local", "local" },
        new object[] { "cruz", "coletora", "local" },
        new object[] { "T", "coletora", "local" },
        new object[] { "cruz", "coletora", "mao1" },
        new object[] { "T", "local", "mao1" },
    };

    [Theory]
    [MemberData(nameof(Same))]
    public void Resultado_igual_ao_da_mesma_cena_feita_pelo_plugin(string kind, string a, string b)
    {
        var w = Plugin(Pare(), ("" + a, EW), (b, kind == "T" ? N : NS));
        var pluginNet = PluginNet(w);
        var nativeNet = ToNative(w).Network();
        var pa = Approaches(pluginNet, Vec2.Zero);
        var na = Approaches(nativeNet, Vec2.Zero);
        Assert.True(Show(pa) == Show(na), $"plugin: {Show(pa)}\nnativo: {Show(na)}");
        // Mesmas faixas em cada trecho direcional (por rumo).
        string Lanes(TrafficNetwork net) => string.Join(" ", net.Links.Select(l => (Heading(l), l.Lanes)).Distinct().OrderBy(x => x));
        Assert.Equal(Lanes(pluginNet), Lanes(nativeNet));
        // Controle: o mesmo quando alguma aproximação para. Sem nenhuma (T em que a mão única só sai do nó), a interseção do
        // plugin guarda "PARE" sem efeito e o cruzamento de pisos fica sem controle – nenhuma placa a ler.
        var pc = pluginNet.Nodes.Single(n => !n.IsZone).Control;
        var nc = nativeNet.Nodes.Single(n => !n.IsZone).Control;
        if (pa.Any(x => x.Stop)) Assert.Equal(pc, nc);
        else Assert.Equal(ControleNo.PreferenciaDireita, nc);
    }

    [Fact]
    public void Faixas_de_pedestres_viram_travessias_do_cruzamento()
    {
        var w = Plugin(Pare(), ("coletora", EW), ("local", NS));
        var plugin = PluginNet(w).Nodes.Single(n => !n.IsZone);
        var native = ToNative(w).Network().Nodes.Single(n => !n.IsZone);
        Assert.True(plugin.Crosswalks);
        Assert.True(native.Crosswalks);
        // Sem faixas pintadas, sem travessia.
        var bare = ToNative(Plugin(new IntersectionDefinition { Control = ControleIntersecao.Nenhum, Crosswalks = false, Ramps = false, Signs = false, StopLines = false },
            ("coletora", EW), ("local", NS))).Network().Nodes.Single(n => !n.IsZone);
        Assert.False(bare.Crosswalks);
    }

    [Fact]
    public void Estacionamento_e_bloqueios_reduzem_as_faixas()
    {
        // Avenida 2+2 com estacionamento junto ao meio-fio: vagas pintadas de um lado e só a linha delimitadora (MER-L) do outro.
        RoadSetup Parked()
        {
            var s = AB2Matrix.Sections["coletora"]();
            s.Right.Clear();
            s.Left.Clear();
            s.Right.AddRange(new[] { E(R, 3.30), E(R, 3.30), E(TipoElementoSecao.Estacionamento, 2.40), E(TipoElementoSecao.Calcada, 2.50) });
            s.Left.AddRange(new[] { E(R, 3.30), E(R, 3.30), new ElementoSecao { Tipo = TipoElementoSecao.Estacionamento, Largura = 2.40, SoDelimitado = true }, E(TipoElementoSecao.Calcada, 2.50) });
            return s;
        }
        var w = new Y34TipTests.World();
        var pr = PathReference.FromPoints(EW, 0);
        var g = Parked().Build(pr, new OutputSettings(), Cat);
        var axis = new Polyline2(EW);
        foreach (var m in g) if (m.Path != null) w.Paths[m.Id] = axis;
        w.Defs.AddRange(g);
        w.Groups.Add(g);
        // Cones fechando a faixa da direita de quem vai para leste, entre x = −70 e −40.
        var cones = new DeviceMarkingDefinition { Code = "CONE" };
        cones.SetPath(PathReference.FromPoints(new[] { new Vec2(-70, -4.95), new Vec2(-40, -4.95) }, 0));
        w.Defs.Add(cones);

        var plugin = PluginNet(w);
        var native = ToNative(w).Network();
        var road = Assert.Single(native.Roads);
        // Eixo limpo: reto de ponta a ponta do piso, no eixo da pista (o eixo medial voltava para trás junto às pontas retas).
        Assert.True(road.Axis.Length is > 178 and < 181, string.Join(" ", road.Axis.Points));
        Assert.All(road.Axis.Points, p => Assert.True(Math.Abs(p.Y) < 0.3, string.Join(" ", road.Axis.Points)));
        Assert.Equal(2, road.LanesForward);       // a faixa das vagas e a da linha delimitadora não contam
        Assert.Equal(2, road.LanesBackward);
        Assert.True(road.ParkingForward && road.ParkingBackward, road.OriginNote);
        var pr0 = plugin.Roads.Single();
        Assert.Equal(pr0.LanesForward, road.LanesForward);
        Assert.Equal(pr0.LanesBackward, road.LanesBackward);
        // Os cones fecham a faixa junto ao meio-fio no trecho, como na via do plugin.
        var closed = native.Links.Where(l => l.Forward == true && l.ClosedLanes.Count > 0).ToList();
        Assert.NotEmpty(closed);
        Assert.All(closed, l => Assert.Contains(l.ClosedLanes, c => c.Lane == 0));
        string Regs(TrafficNetwork n) => string.Join(" | ", n.Regulations.Where(r => r.Code == "CONE").Select(r => r.Applied + ":" + r.Effect));
        Assert.True(plugin.Links.Count(l => l.ClosedLanes.Count > 0) == native.Links.Count(l => l.ClosedLanes.Count > 0),
            $"plugin: {Regs(plugin)}\nnativo: {Regs(native)}");
        // Antes da correção nem a via do plugin fechava a faixa: cones espaçados deixavam vãos entre as peças.
        Assert.Contains(plugin.Links, l => l.ClosedLanes.Any(c => c.Lane == 0));
    }

    [Fact]
    public void Mapa_mostra_a_origem_e_lista_o_que_nao_foi_interpretado()
    {
        var nat = ToNative(Plugin(Pare(), ("local", EW), ("local", NS)));
        // Faixa de asfalto de 2 m (sem forma de pista), linha solta longe das pistas e um piso sem sinalização nenhuma.
        nat.Sources.Floors.Add(new NativeFloor("estreito", Polygon2.Rectangle(new Vec2(200, 0), new Vec2(260, 2)), 0, "Asfalto"));
        var stray = new LinearMarkingDefinition { Code = "LMS-2" };
        stray.SetPath(PathReference.FromPoints(new[] { new Vec2(400, 50), new Vec2(450, 50) }, 0));
        nat.Defs.Add(stray);
        nat.Paths[stray.Id] = new Polyline2(new[] { new Vec2(400, 50), new Vec2(450, 50) });
        nat.Sources.Floors.Add(new NativeFloor("liso", Polygon2.Rectangle(new Vec2(-300, -4), new Vec2(-200, 4)), 0, "Pavimento asfáltico"));
        var net = nat.Network();

        Assert.Contains(net.Uninterpreted, u => u.SourceId == "estreito" && u.Why.Contains("2,50"));
        Assert.Contains(net.Uninterpreted, u => u.SourceId == stray.Id && u.Why.Contains("fora de qualquer pista"));
        var plain = net.Roads.Single(r => r.SourceIds.Contains("liso"));
        Assert.Equal(OrigemTrecho.Ambiguo, plain.Origin);
        Assert.Contains("sem linha de centro nem setas", plain.OriginNote);
        Assert.Equal(2, net.Roads.Count(r => r.Origin == OrigemTrecho.Piso));
        Assert.All(net.Links, l => Assert.NotNull(l.Road.OriginNote));
        // O relatório diz de onde veio cada via e lista o que não foi interpretado.
        var report = TrafficReport.Build(TrafficAnalysis.Run(net, new TrafficOptions { Demand = NivelDemanda.Baixa }));
        Assert.Contains("inferida de piso do Revit", report);
        Assert.Contains("Não interpretado", report);
        Assert.Contains("fora de qualquer pista", report);
        // Os pisos usados entram no mapa como pavimento.
        Assert.Contains(net.Backdrop, b => b.SourceId == "F1" && b.Layer == CamadaMapa.Pavimento);
        // Piso com nome de calçada e sinalização em cima: avisado (selecionado, entraria).
        var nat2 = ToNative(Plugin(Pare(), ("local", EW), ("local", NS)), "Calçada concreto");
        var net2 = nat2.Network();
        Assert.Empty(net2.Roads);
        Assert.Contains(net2.Uninterpreted, u => u.Why.Contains("selecione-o"));
        nat2.Sources.Floors[0] = nat2.Sources.Floors[0] with { Selected = true };
        Assert.Equal(2, nat2.Network().Roads.Count);
    }

    [Fact]
    public void Linha_de_modelo_escolhida_como_eixo_vira_via_com_as_faixas_das_linhas_pintadas()
    {
        // Sem piso nenhum: só a linha de eixo escolhida e a sinalização (linha amarela, divisórias e bordos da coletora).
        var w = Plugin(Pare(), ("coletora", EW));
        var nat = ToNative(w);
        nat.Sources.Floors.Clear();
        nat.Sources.AxisLines.Add(new AxisLine("linha-1", new Polyline2(EW)));
        var net = nat.Network();
        var r = Assert.Single(net.Roads);
        Assert.Equal(OrigemTrecho.LinhaDeEixo, r.Origin);
        Assert.True(r.TwoWay);
        Assert.Equal(2, r.LanesForward);
        Assert.Equal(2, r.LanesBackward);
        // A mesma linha sobre uma via do plugin não duplica a via.
        var both = PluginNet(w);
        var src = new NetworkSources();
        src.AxisLines.Add(new AxisLine("linha-1", new Polyline2(EW)));
        var mixed = TrafficNetworkBuilder.Build(w.Defs, d => Ax(w.Paths, d), null, src);
        Assert.Equal(both.Roads.Count, mixed.Roads.Count);
    }

    [Fact]
    public void Via_do_plugin_e_piso_nativo_formam_um_so_grafo()
    {
        // Coletora do plugin e uma rua local feita só com piso (e a sinalização dela) chegando em T.
        var w = Plugin(Pare(), ("coletora", EW), ("local", N));
        var nat = ToNative(w);
        var keep = w.Defs.Where(d => w.Groups[0].Contains(d)).ToList();     // a coletora continua do plugin
        var defs = keep.Concat(nat.Defs.Where(d => !w.Groups[0].Any(g => g.Id == d.Id))).ToList();
        var paths = new Dictionary<string, Polyline2>(nat.Paths);
        foreach (var d in keep) if (w.Paths.TryGetValue(d.Id, out var p)) paths[d.Id] = p;
        // O piso só da rua local (o asfalto da coletora já é a via do plugin).
        var src = new NetworkSources();
        foreach (var f in nat.Sources.Floors) src.Floors.Add(f);
        var net = TrafficNetworkBuilder.Build(defs, d => Ax(paths, d), null, src);
        Assert.Contains(net.Roads, r => r.Origin == OrigemTrecho.Plugin);
        Assert.Contains(net.Roads, r => r.Origin == OrigemTrecho.Piso);
        Assert.Equal(2, net.Roads.Count);
        var ap = Approaches(net, Vec2.Zero);
        Assert.True(ap.Count == 3, Show(ap));
        Assert.Contains(ap, a => a.Heading == 270 && a.Lanes == 1);
    }
}
