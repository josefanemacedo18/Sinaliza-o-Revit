using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada Y: ajustes por esquina e por ramo na interseção (raio, faixa, rampas, controle, ilha/bolsão).</summary>
public class Y35CornerTests
{
    private const double L = 90;

    /// <summary>Cruzamento de duas coletoras (via 0 no eixo X, via 1 no eixo Y) com ajustes por ramo.</summary>
    private static Y34TipTests.World Cross(Action<IntersectionDefinition, IReadOnlyList<IntersectionRoad>>? tune, IntersectionDefinition? d = null, int a = 1, int b = 1) =>
        Y34TipTests.Make(d ?? new IntersectionDefinition { Control = ControleIntersecao.Pare, CornerRadius = 6 }, tune,
            (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { new Vec2(0, -L), new Vec2(0, L) }));

    /// <summary>Entroncamento em T: via 0 principal (eixo X), via 1 secundária saindo para o norte.</summary>
    private static Y34TipTests.World Tee(Action<IntersectionDefinition, IReadOnlyList<IntersectionRoad>>? tune, IntersectionDefinition? d = null) =>
        Y34TipTests.Make(d ?? new IntersectionDefinition { Control = ControleIntersecao.Pare }, tune,
            (1, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (0, new[] { Vec2.Zero, new Vec2(0, L) }));

    private static void Set(IntersectionDefinition d, IReadOnlyList<IntersectionRoad> rs, int road, int sign, Action<IntersectionLegSettings> f)
    {
        var s = new IntersectionLegSettings { RoadId = rs[road].Def.Id, Sign = sign };
        f(s);
        d.LegSettings.Add(s);
    }

    /// <summary>
    /// Até onde a curva da esquina avança além do canto dos bordos, pela bissetriz (R·(√2 − 1) numa esquina a 90°): o
    /// último ponto de pavimento vindo de fora para o canto.
    /// </summary>
    private static double Fillet(IntersectionLayout L, Vec2 corner)
    {
        var dir = (corner - L.Node).Normalized();
        for (var t = 15.0; t >= 0; t -= 0.01)
            if (L.Pavement.Any(p => p.Contains(corner + dir * t))) return t;
        return double.NaN;
    }

    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 0.10, $"curva a {actual:0.00} m do canto (esperado {expected:0.00} m)");

    [Fact]
    public void Raio_de_uma_esquina_muda_so_aquela_esquina()
    {
        // Esquina à direita de quem chega pelo ramo norte da via 1 = esquina noroeste.
        var w = Cross((d, rs) => Set(d, rs, 1, 1, s => s.CornerRadius = 12));
        var Lay = w.Nodes[0].Layout;
        var e = Lay.Roads[0].Def.LeftWidth;          // bordo (face do meio-fio) das duas coletoras
        var k = Math.Sqrt(2) - 1;
        Near(12 * k, Fillet(Lay, new Vec2(-e, e)));   // noroeste: R = 12
        foreach (var c in new[] { new Vec2(e, e), new Vec2(e, -e), new Vec2(-e, -e) })
            Near(6 * k, Fillet(Lay, c));               // demais: R geral = 6
        Assert.Empty(Y34TipTests.Faults(w));

        // Raio menor que o geral numa esquina.
        var w2 = Cross((d, rs) => Set(d, rs, 0, -1, s => s.CornerRadius = 3));
        var L2 = w2.Nodes[0].Layout;
        // Ramo oeste da via 0: quem chega por ele vira à direita para o sul → esquina sudoeste.
        Near(3 * k, Fillet(L2, new Vec2(-e, -e)));
        Near(6 * k, Fillet(L2, new Vec2(e, e)));
        Assert.Empty(Y34TipTests.Faults(w2));
    }

    private static List<LinearMarkingDefinition> Crosswalks(Y34TipTests.World w) =>
        w.Defs.OfType<LinearMarkingDefinition>().Where(m => m.Code.StartsWith("FTP") && w.Defs.OfType<IntersectionDefinition>().Any(i => i.Id == m.GroupId)).ToList();

    /// <summary>Ramo em que fica a travessia: via (0 = eixo X) e lado.</summary>
    private static (int Road, int Sign) LegOf(LinearMarkingDefinition m)
    {
        var c = (m.PathRef.Points[0] + m.PathRef.Points[^1]) / 2;
        return Math.Abs(c.X) > Math.Abs(c.Y) ? (0, Math.Sign(c.X)) : (1, Math.Sign(c.Y));
    }

    [Fact]
    public void Faixa_largura_recuo_e_rampas_por_ramo()
    {
        var w = Cross((d, rs) =>
        {
            Set(d, rs, 0, 1, s => s.Crosswalk = false);                         // leste: sem faixa
            Set(d, rs, 0, -1, s => s.CrosswalkWidth = 3.0);                     // oeste: faixa de 3 m
            Set(d, rs, 1, 1, s => s.CrosswalkSetback = 4.0);                    // norte: recuo de 4 m
            Set(d, rs, 1, -1, s => s.Ramps = false);                            // sul: sem rampas
        });
        var Lay = w.Nodes[0].Layout;
        var it = w.Defs.OfType<IntersectionDefinition>().First();
        var ftp = Crosswalks(w);
        Assert.Equal(3, ftp.Count);
        Assert.DoesNotContain(ftp, m => LegOf(m) == (0, 1));
        Assert.Equal(3.0, ftp.Single(m => LegOf(m) == (0, -1)).WidthOverride ?? 0, 3);
        var legN = Lay.Legs.Single(l => l.Road == 1 && l.Sign == 1);
        var legS = Lay.Legs.Single(l => l.Road == 1 && l.Sign == -1);
        Assert.True(IntersectionGenerator.SetbackOf(it, Lay, legN) >= 4.0 - 1e-9);
        Assert.True(IntersectionGenerator.CrosswalkT(it, Lay, legN) - legN.Clear > IntersectionGenerator.CrosswalkT(it, Lay, legS) - legS.Clear + 2.9);
        // Rampas: norte e oeste (2 cada); sul sem rampas; leste sem faixa.
        var ramps = w.Defs.OfType<RampDefinition>().ToList();
        Assert.Equal(4, ramps.Count);
        Assert.DoesNotContain(ramps, r => r.PathRef.Points[0].Y < -5 && Math.Abs(r.PathRef.Points[0].X) < 12);
        Assert.Empty(Y34TipTests.Faults(w));
    }

    [Fact]
    public void Controle_por_aproximacao()
    {
        // Geral: PARE só na secundária. Ajuste: PARE também no ramo leste da principal; secundária sem controle.
        var w = Tee((d, rs) =>
        {
            Set(d, rs, 0, 1, s => s.Control = ControleIntersecao.Pare);
            Set(d, rs, 1, 1, s => s.Control = ControleIntersecao.Nenhum);
        });
        var signs = w.Defs.OfType<SignDefinition>().Where(x => x.Code == "R-1").ToList();
        Assert.Single(signs);
        Assert.True(signs[0].Position.X > 5);                                   // na aproximação leste da principal
        Assert.False(w.Nodes[0].Layout.ThroughMain);                            // as linhas da principal param na retenção
        var plain = Tee(null);
        var s0 = plain.Defs.OfType<SignDefinition>().Where(x => x.Code == "R-1").ToList();
        Assert.Single(s0);
        Assert.True(s0[0].Position.Y > 5);                                      // padrão: só na secundária (norte)
        Assert.Empty(Y34TipTests.Faults(w));
    }

    [Fact]
    public void Ilha_e_bolsao_por_ramo()
    {
        // Geral sem ilha; ajuste liga a ilha gota só na secundária.
        var w = Tee((d, rs) => Set(d, rs, 1, 1, s => s.Treatment = true));
        var f = w.Nodes[0].Layout.Features;
        Assert.Single(f);
        Assert.Equal(TipoRamo.Gota, f[0].Kind);
        Assert.Empty(Y34TipTests.Faults(w));

        // Geral com ilha física em todas as secundárias; ajuste desliga num ramo.
        var d0 = new IntersectionDefinition { Control = ControleIntersecao.Pare, SplitterIslands = TipoIlha.Fisica };
        var w2 = Cross((d, rs) => Set(d, rs, 1, -1, s => s.Treatment = false), d0, 2, 1);
        var g = w2.Nodes[0].Layout.Features.Where(x => x.Kind == TipoRamo.Gota).ToList();
        Assert.Single(g);
        Assert.Equal((1, 1), (g[0].Leg.Road, g[0].Leg.Sign));

        // Bolsão de conversão à esquerda só num ramo da principal (avenida com canteiro).
        var w3 = Cross((d, rs) => Set(d, rs, 0, 1, s => s.Treatment = true), null, 2, 1);
        var p = w3.Nodes[0].Layout.Features.Where(x => x.Kind is TipoRamo.BolsaoCanteiro or TipoRamo.BolsaoAlargado).ToList();
        Assert.Single(p);
        Assert.Equal((0, 1), (p[0].Leg.Road, p[0].Leg.Sign));
    }

    [Fact]
    public void Aplicar_a_todas_as_esquinas()
    {
        var w = Cross((d, rs) =>
        {
            Set(d, rs, 1, 1, s => { s.CornerRadius = 10; s.CrosswalkWidth = 5; s.Ramps = false; });
        });
        var it = w.Defs.OfType<IntersectionDefinition>().First();
        var Lay = w.Nodes[0].Layout;
        var src = IntersectionGenerator.LegSet(it, Lay, Lay.Legs.Single(l => l.Road == 1 && l.Sign == 1))!;
        IntersectionGenerator.ApplyToAllLegs(it, Lay, src);
        Assert.Equal(4, it.LegSettings.Count);
        Assert.All(it.LegSettings, s =>
        {
            Assert.Equal(10, s.CornerRadius);
            Assert.Equal(5, s.CrosswalkWidth);
            Assert.False(s.Ramps);
        });
        // Aplicado e regerado: as quatro esquinas com R = 10, faixas de 5 m, sem rampas, sem falhas.
        var w3 = Cross((d, rs) =>
        {
            foreach (var (road, sign) in new[] { (0, 1), (0, -1), (1, 1), (1, -1) })
                Set(d, rs, road, sign, s => { s.CornerRadius = 10; s.CrosswalkWidth = 5; s.Ramps = false; });
        });
        var L3 = w3.Nodes[0].Layout;
        var e = L3.Roads[0].Def.LeftWidth;
        foreach (var c in new[] { new Vec2(e, e), new Vec2(-e, e), new Vec2(-e, -e), new Vec2(e, -e) })
            Near(10 * (Math.Sqrt(2) - 1), Fillet(L3, c));
        Assert.Empty(w3.Defs.OfType<RampDefinition>());
        Assert.All(Crosswalks(w3), m => Assert.Equal(5.0, m.WidthOverride ?? 0, 3));
        Assert.Empty(Y34TipTests.Faults(w3));
    }

    [Theory]
    [InlineData(0, 2, 60.0)]
    [InlineData(1, 0, 120.0)]
    [InlineData(2, 1, 90.0)]
    public void Ajustes_variados_sem_falhas_de_modelagem(int a, int b, double ang)
    {
        var u = Y34TipTests.Dir(ang);
        var w = Y34TipTests.Make(new IntersectionDefinition { Control = ControleIntersecao.Pare }, (d, rs) =>
            {
                Set(d, rs, 0, 1, s => { s.CornerRadius = 12; s.CrosswalkSetback = 2; });
                Set(d, rs, 0, -1, s => { s.CornerRadius = 3; s.Ramps = false; });
                Set(d, rs, 1, 1, s => { s.CrosswalkWidth = 3; s.Control = ControleIntersecao.DePreferencia; });
            },
            (a, new[] { new Vec2(-L, 0), new Vec2(L, 0) }), (b, new[] { Vec2.Zero, u * L }));
        Assert.NotEmpty(w.Nodes);
        var f = Y34TipTests.Faults(w);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(10)));
    }

    [Fact]
    public void Ajustes_por_ramo_gravados_e_lidos()
    {
        var d = new IntersectionDefinition();
        d.LegSettings.Add(new IntersectionLegSettings { RoadId = "abc", Sign = -1, CornerRadius = 9, Crosswalk = false, Control = ControleIntersecao.Semaforo, Treatment = true });
        var back = (IntersectionDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        var s = Assert.Single(back.LegSettings);
        Assert.Equal(("abc", -1, 9.0, false, ControleIntersecao.Semaforo, true), (s.RoadId, s.Sign, s.CornerRadius!.Value, s.Crosswalk!.Value, s.Control!.Value, s.Treatment!.Value));
        Assert.DoesNotContain("IsEmpty", d.ToJson());
        // Interseção gravada antes dos ajustes por ramo: lista vazia.
        var old = (IntersectionDefinition)MarkingDefinition.FromJson(new IntersectionDefinition().ToJson().Replace("\"LegSettings\":[],", ""))!;
        Assert.Empty(old.LegSettings);
    }
}
