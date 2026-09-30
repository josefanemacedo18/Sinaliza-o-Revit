using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Traffic;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AA: "avaliado × aplicado" – a solução é avaliada no cenário, o pacote é aplicado ao modelo como o plugin faz
/// (TrafficPackageModel: controle, plano, faixas, bolsões, proibições, remoções), a rede é relida e a análise e a
/// microssimulação rodam de novo com as mesmas sementes.
/// </summary>
public class AA1SolutionReproTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private readonly ITestOutputHelper _out;
    public AA1SolutionReproTests(ITestOutputHelper o) => _out = o;

    /// <summary>Modelo em memória: marcas + eixos (como o projeto).</summary>
    public sealed class Model
    {
        public List<MarkingDefinition> Defs { get; } = new();
        public Dictionary<string, Polyline2> Paths { get; } = new();

        public RoadPavementDefinition Road(int template, params Vec2[] pts)
        {
            var axis = new Polyline2(pts);
            var pr = PathReference.FromPoints(pts, 0);
            var g = RoadTemplates.All[template].Create().Build(pr, new OutputSettings(), Cat, axis: axis);
            foreach (var m in g) if (m.Path is { } p && p.Points.SequenceEqual(pr.Points)) Paths[m.Id] = axis;
            Defs.AddRange(g);
            return g.OfType<RoadPavementDefinition>().First();
        }

        /// <summary>Interseção gerada como o plugin (placas, marcas, travessias e recortes das vias).</summary>
        public IntersectionDefinition Intersection(Vec2 at, ControleIntersecao c, RoadPavementDefinition main, params RoadPavementDefinition[] others)
        {
            var roads = new[] { main }.Concat(others).Select(r => new IntersectionRoad(r, Paths[r.Id])).ToList();
            var d = new IntersectionDefinition { Node = at, Control = c, MainRoadId = main.Id };
            IntersectionDemo.Create(d, roads, roads.Select(r => Defs.Where(m => m.GroupId == r.Def.GroupId).ToList()).ToList(), Paths, Defs, Cat);
            return d;
        }

        public Model Clone()
        {
            var c = new Model();
            c.Defs.AddRange(Defs.Select(d => MarkingDefinition.FromJson(d.ToJson())!));
            foreach (var (k, v) in Paths) c.Paths[k] = v;
            return c;
        }

        /// <summary>Cópia do modelo como o plugin a entrega ao simulador (definições e eixos resolvidos).</summary>
        public ModelSnapshot Snapshot() => new() { Defs = Defs, Axes = Paths.ToDictionary(kv => kv.Key, kv => (kv.Value, 0.0)), Catalog = Cat };

        public TrafficNetwork Network() => Snapshot().Network(null);
    }

    /// <summary>Resultado de macro + micro (média e faixa das sementes) no local e na rede.</summary>
    public sealed record Measure(double MacroLocal, double MacroNet, MicroStat MicroLocal, MicroStat MicroNet)
    {
        public override string ToString() =>
            $"HCM local {MacroLocal:0.0} s, rede {MacroNet:0.00} veh·h/h; micro local {MicroLocal.Mean:0.0} [{MicroLocal.Min:0.0}–{MicroLocal.Max:0.0}] s/veh, " +
            $"rede {MicroNet.Mean:0.0} [{MicroNet.Min:0.0}–{MicroNet.Max:0.0}] s/veh";
    }

    public static Measure Run(TrafficNetwork net, TrafficOptions opt, int? node, int? link, int seeds = 3)
    {
        var macro = TrafficAnalysis.Run(net, opt);
        var local = node is { } n ? macro.Nodes[n].Delay : link is { } l ? macro.Links[l].TravelTime : 0;
        var (loc, all) = TrafficSolutions.Micro(net, opt, node, link, seeds);
        return new Measure(local, macro.TotalDelayH, loc, all);
    }

    // ------------------------------------------------------------------ cenas (geradas como o plugin)

    /// <summary>Cena de teste: o modelo, o problema (nó ou trecho) e o cenário de demanda.</summary>
    public sealed record Scene(string Name, Model M, string? NodeKey, Vec2? LinkAt, TrafficOptions Opt);

    private static TrafficOptions Opt(NivelDemanda d) => new() { Demand = d, SimSeconds = 600, WarmupSeconds = 120 };

    /// <summary>Cruzamento de duas coletoras (2 + 2 faixas) com PARE na secundária e a via principal carregada.</summary>
    public static (Model M, IntersectionDefinition I) CrossPare() => Cross(ControleIntersecao.Pare);

    public static (Model M, IntersectionDefinition I) Cross(ControleIntersecao c, int tpl = 1)
    {
        var m = new Model();
        var a = m.Road(tpl, new Vec2(-300, 0), new Vec2(300, 0));
        var b = m.Road(tpl, new Vec2(0, -250), new Vec2(0, 250));
        var it = m.Intersection(Vec2.Zero, c, a, b);
        return (m, it);
    }

    /// <summary>Via com dois semáforos a 300 m (corredor para a onda verde).</summary>
    public static (Model M, IntersectionDefinition I) Corridor()
    {
        var m = new Model();
        var a = m.Road(1, new Vec2(-500, 0), new Vec2(500, 0));
        var b = m.Road(0, new Vec2(-150, -200), new Vec2(-150, 200));
        var c = m.Road(0, new Vec2(150, -200), new Vec2(150, 200));
        var i1 = m.Intersection(new Vec2(-150, 0), ControleIntersecao.Semaforo, a, b);
        m.Intersection(new Vec2(150, 0), ControleIntersecao.Semaforo, a, c);
        return (m, i1);
    }

    /// <summary>Travessia semaforizada no meio da quadra (faixa FTP-1 e grupos focais dos dois lados).</summary>
    public static Model MidBlock()
    {
        var m = new Model();
        m.Road(1, new Vec2(-300, 0), new Vec2(300, 0));
        m.Defs.Add(new LinearMarkingDefinition { Code = "FTP-1", Variant = "4,00 m", PathRef = PathReference.FromPoints(new[] { new Vec2(0, -9), new Vec2(0, 9) }, 0), Output = new OutputSettings() });
        m.Defs.Add(new UrbanElementDefinition { Code = "SEMAFORO", Position = new Vec2(-3, -9), Direction = Vec2.UnitX, Output = new OutputSettings() });
        m.Defs.Add(new UrbanElementDefinition { Code = "SEMAFORO", Position = new Vec2(3, 9), Direction = -Vec2.UnitX, Output = new OutputSettings() });
        return m;
    }

    public static IEnumerable<Scene> Scenes()
    {
        var (m1, i1) = Cross(ControleIntersecao.Pare);
        yield return new Scene("PARE, coletoras, pico", m1, i1.Id, null, Opt(NivelDemanda.Pico));
        var (m2, i2) = Cross(ControleIntersecao.Nenhum);
        yield return new Scene("sem controle, coletoras, média", m2, i2.Id, null, Opt(NivelDemanda.Media));
        var (m3, i3) = Cross(ControleIntersecao.Semaforo);
        yield return new Scene("semáforo, coletoras, pico", m3, i3.Id, null, Opt(NivelDemanda.Pico));
        var (m4, i4) = Corridor();
        yield return new Scene("corredor com 2 semáforos, pico", m4, i4.Id, null, Opt(NivelDemanda.Pico));
        yield return new Scene("travessia no meio da quadra, pico", MidBlock(), null, new Vec2(0, -3), Opt(NivelDemanda.Pico));
    }

    private static (int? Node, int? Link) Target(Scene s, TrafficNetwork net)
    {
        int? node = s.NodeKey != null ? net.Nodes.First(n => n.Key == s.NodeKey).Index : null;
        int? link = s.LinkAt is { } p
            ? net.Links.Where(l => l.CrossingPlans.Count > 0).OrderBy(l => Math.Abs(l.Path.Project(p).Signed)).First().Index
            : null;
        return (node, link);
    }

    /// <summary>Solução do tipo pedido: avaliada no cenário e aplicada no modelo (rede relida), mesmas sementes.</summary>
    private (Measure Eval, Measure Appl, SolutionTrial T)? EvalApply(string kind)
    {
        foreach (var s in Scenes())
        {
            var net = s.M.Network();
            var baseRes = TrafficAnalysis.Run(net, s.Opt);
            var (node, link) = Target(s, net);
            var t = TrafficSolutions.For(net, s.Opt, baseRes, node, link, seeds: 0).FirstOrDefault(x => x.Kind == kind && !x.Failed && x.Package != null);
            if (t == null) continue;
            // Avaliado: o cenário da solução (o que o botão "Aplicar no cenário" faz).
            var o = TrafficSolutions.Clone(s.Opt);
            t.Apply(o);
            var eval = Run(net, o, node, link);
            // Aplicado: o pacote no modelo (a regra do plugin), a rede relida e o cenário que fica depois de aplicar.
            var applied = s.M.Snapshot().Network(t.Package);
            var o2 = TrafficSolutions.Clone(s.Opt);
            TrafficSolutions.AfterApply(o2, t);
            TrafficAnalysis.Run(applied, o2);
            var (n2, l2) = TrafficSolutions.Map(applied, node is { } ni ? net.Nodes[ni] : null, link is { } li ? net.Links[li] : null);
            var appl = Run(applied, o2, n2, l2);
            _out.WriteLine($"{s.Name} – {t.Title} ({kind})\n   AVALIADO {eval}\n   APLICADO {appl}");
            return (eval, appl, t);
        }
        return null;
    }

    public static IEnumerable<object[]> Kinds() =>
        new[] { "semaforo", "pare", "preferencia", "rotatoria", "retemporizar", "ondaverde", "proibiresquerda", "bolsao", "travessia-ciclo", "travessia-sem" }
            .Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AvaliadoXAplicado_DiferencaDeAtrasoMenorQue10PorCento(string kind)
    {
        var r = EvalApply(kind);
        Assert.True(r != null, $"nenhuma cena oferece a solução {kind}");
        var (eval, appl, t) = r!.Value;
        static double Diff(double a, double b) => Math.Abs(a - b) / Math.Max(1.0, Math.Max(a, b));
        var dn = Diff(eval.MicroLocal.Mean, appl.MicroLocal.Mean);
        var dr = Diff(eval.MicroNet.Mean, appl.MicroNet.Mean);
        _out.WriteLine($"   diferença: local {dn:P1}, rede {dr:P1}; HCM local {Diff(eval.MacroLocal, appl.MacroLocal):P1}");
        Assert.True(dn < 0.10, $"{t.Title}: atraso local avaliado {eval.MicroLocal.Mean:0.0} × aplicado {appl.MicroLocal.Mean:0.0} s/veh ({dn:P1})");
        Assert.True(dr < 0.10, $"{t.Title}: atraso na rede avaliado {eval.MicroNet.Mean:0.0} × aplicado {appl.MicroNet.Mean:0.0} s/veh ({dr:P1})");
    }

    [Fact]
    public void Recomendadas_SaoAsImplantadasENaoPioramARede()
    {
        var recommended = 0;
        foreach (var s in Scenes())
        {
            var net = s.M.Network();
            var baseRes = TrafficAnalysis.Run(net, s.Opt);
            var (node, link) = Target(s, net);
            var snap = s.M.Snapshot();
            var trials = TrafficSolutions.For(net, s.Opt, baseRes, node, link, TrafficSolutions.DefaultSeeds, snap);
            foreach (var t in trials) _out.WriteLine($"{s.Name}: {t.Summary()}");
            var nd = node is { } ni ? net.Nodes[ni] : null;
            var lk = link is { } li ? net.Links[li] : null;
            foreach (var t in trials.Where(t => t.Improves))
            {
                recommended++;
                Assert.True(t.MicroTested, t.Summary());
                Assert.True(t.MicroLocalAfter!.Runs >= 3);
                // O critério: local ≥ 10 % melhor na média e não pior no pior caso; rede melhor na média e não pior no pior caso.
                Assert.True(t.MicroLocalAfter.Mean <= t.MicroLocalBefore!.Mean * 0.9 && t.MicroLocalAfter.Max <= t.MicroLocalBefore.Max);
                Assert.True(t.MicroNetAfter!.Mean < t.MicroNetBefore!.Mean && t.MicroNetAfter.Max <= t.MicroNetBefore.Max);
                if (t.Package is not { ScenarioOnly: false }) continue;
                // A recomendação é o que o plugin implanta: medida no modelo com o pacote aplicado, e os números batem com a
                // rede relida depois de aplicar (mesmas sementes).
                Assert.True(t.EvaluatedOnModel, t.Summary());
                var applied = snap.Network(t.Package);
                var o2 = TrafficSolutions.Clone(s.Opt);
                TrafficSolutions.AfterApply(o2, t);
                var (n2, l2) = TrafficSolutions.Map(applied, nd, lk);
                var (loc, all) = TrafficSolutions.Micro(applied, o2, n2, l2);
                Assert.Equal(t.MicroLocalAfter.Mean, loc.Mean, 6);
                Assert.Equal(t.MicroNetAfter.Mean, all.Mean, 6);
                // E com outras sementes (fora da amostra da decisão) a rede não piora na média.
                var oOut = TrafficSolutions.Clone(o2);
                oOut.Seed += 100;
                var bOut = TrafficSolutions.Clone(s.Opt);
                bOut.Seed += 100;
                var baseNet = snap.Network(null);
                var (n0, l0) = TrafficSolutions.Map(baseNet, nd, lk);
                var before = TrafficSolutions.Micro(baseNet, bOut, n0, l0).Net;
                var after = TrafficSolutions.Micro(applied, oOut, n2, l2).Net;
                _out.WriteLine($"   {t.Title} fora da amostra (sementes +100): rede {before.Mean:0.0} → {after.Mean:0.0} s/veh");
                Assert.True(after.Mean <= before.Mean, $"{s.Name} – {t.Title}: rede {before.Mean:0.0} → {after.Mean:0.0} s/veh com outras sementes");
            }
        }
        Assert.True(recommended > 0, "nenhuma solução recomendada nas cenas de teste");
    }
}
