using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 22 – interseções (prioridade do usuário, D-040): o T da imagem 1 do usuário e as rotatórias com vias de calçadas
/// diferentes, reproduzidos no núcleo.
/// <list type="bullet">
/// <item>Canteiro central aberto pela curva de giro à esquerda (DNIT IPR-718, 2005, cap. 8, Tabelas 62 e 63), com o nariz à
/// frente da travessia e o refúgio no nível da pista; a regra antiga continua como opção.</item>
/// <item>Ciclofaixa até a faixa de pedestres (antes parava na retenção, com ~2,4 m sem ciclofaixa).</item>
/// <item>Calçada da rotatória com a largura e a composição de cada lado de cada via (antes uma largura só em volta toda).</item>
/// </list>
/// </summary>
public class Etapa22IntersecoesTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static ElementoSecao E(TipoElementoSecao t, double w) => new() { Tipo = t, Largura = w };
    private const TipoElementoSecao R = TipoElementoSecao.FaixaRolamento;

    /// <summary>Via sem canteiro: uma faixa de 3,50 m e calçada de cada lado.</summary>
    private static RoadSetup Principal() => new()
    {
        Center = CenterTreatment.LFO2, SarjetaSomada = true,
        Right = { E(R, 3.50), E(TipoElementoSecao.Calcada, 3.0) },
        Left = { E(R, 3.50), E(TipoElementoSecao.Calcada, 3.0) },
    };

    /// <summary>A via da imagem 1: canteiro central físico, faixa, estacionamento, ciclofaixa junto à calçada e calçada.</summary>
    private static RoadSetup Avenida(double canteiro = 3.0) => new()
    {
        Center = CenterTreatment.Canteiro, MedianWidth = canteiro, MedianType = TipoCanteiro.Fisico, SarjetaSomada = true,
        Right = { E(R, 3.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Ciclofaixa, 1.50), E(TipoElementoSecao.Calcada, 3.00) },
        Left = { E(R, 3.50), E(TipoElementoSecao.Estacionamento, 2.20), E(TipoElementoSecao.Ciclofaixa, 1.50), E(TipoElementoSecao.Calcada, 3.00) },
    };

    /// <summary>Via com a calçada dada de cada lado (com o meio-fio).</summary>
    private static RoadSetup Local(double dir, double esq) => new()
    {
        Center = CenterTreatment.LFO2, SarjetaSomada = true,
        Right = { E(R, 3.50), E(TipoElementoSecao.Calcada, dir) },
        Left = { E(R, 3.50), E(TipoElementoSecao.Calcada, esq) },
    };

    private static Y34TipTests.World T(IntersectionDefinition? tpl = null, double canteiro = 3.0) =>
        AE1BikeLaneTests.World(tpl, (Principal(), new[] { new Vec2(0, -70), new Vec2(0, 70) }), (Avenida(canteiro), new[] { new Vec2(0, 0), new Vec2(110, 0) }));

    private static IntersectionDefinition Tpl(Action<IntersectionDefinition> f)
    {
        var d = new IntersectionDefinition { Control = ControleIntersecao.Pare };
        f(d);
        return d;
    }

    // ------------------------------------------------------------------ canteiro central

    [Fact]
    public void Canteiro_de_3_m_abre_pela_curva_de_giro_como_a_Tabela_63_do_DNIT()
    {
        var w = T();
        var L = w.Nodes.Single().Layout;
        // DNIT IPR-718, Tabela 63: nariz semicircular, L = 3 m, R = 12 m → abertura 2R − L = 21,0 m (10,5 m do eixo da transversal).
        var tip = L.MedianTips[(1, 1)];
        Assert.InRange(tip, 10.45, 10.55);
        // Nada do canteiro dentro da abertura (antes ele ia até a boca da transversal, a ~4,8 m do eixo).
        var med = PolygonOps.Union(L.Obstacles);
        Assert.InRange(med.SelectMany(p => p.Outer).Min(v => v.X), tip - 0.05, tip + 0.05);
        Assert.Empty(Y34TipTests.Faults(w));
    }

    [Fact]
    public void Canteiro_largo_ganha_ogiva_e_duas_avenidas_respeitam_o_canteiro_da_outra()
    {
        // Canteiro de 6 m (acima de 3 m): ogiva; Tabela 63 (R = 12 m, L = 6 m): abertura de 12,0 m.
        var wide = T(canteiro: 6.0);
        Assert.InRange(wide.Nodes.Single().Layout.MedianTips[(1, 1)], 5.95, 6.05);
        Assert.Empty(Y34TipTests.Faults(wide));
        // Duas avenidas: o arco é tangente ao bordo do canteiro da outra (não ao eixo dela) – 10,0 m em cada ramo.
        var two = AE1BikeLaneTests.World(null, (Avenida(), new[] { new Vec2(0, -110), new Vec2(0, 110) }), (Avenida(), new[] { new Vec2(-110, 0), new Vec2(110, 0) }));
        var L = two.Nodes.Single().Layout;
        Assert.Equal(4, L.MedianTips.Count);
        Assert.All(L.MedianTips.Values, t => Assert.InRange(t, 9.95, 10.05));
        // Abertura nunca menor que 12 m (DNIT).
        Assert.All(L.MedianTips.Values, t => Assert.True(2 * t >= IntersectionGenerator.AberturaMinimaCanteiro - 1e-6));
        Assert.Empty(Y34TipTests.Faults(two));
    }

    [Fact]
    public void Travessia_fica_atras_do_nariz_e_atravessa_o_canteiro_no_nivel_da_pista()
    {
        var w = T();
        var L = w.Nodes.Single().Layout;
        var d = L.Definition!;
        var leg = L.Legs.Single(l => l.Road == 1);
        var tip = L.MedianTips[(1, 1)];
        var near = IntersectionGenerator.CrosswalkT(d, L, leg) - IntersectionGenerator.CrosswalkWidthOf(d, L, leg) / 2;
        // O nariz semicircular inteiro fica à frente da faixa: a borda de perto da faixa no centro do semicírculo (ponta + L/2).
        Assert.True(L.CrosswalkExtra.ContainsKey((1, 1)));
        Assert.InRange(near, tip + 1.5 - 0.01, tip + 1.5 + 0.2);
        // A faixa corta o canteiro: refúgio no nível da pista, com piso tátil.
        var passes = IntersectionGenerator.MedianPassesOf(d, L, leg);
        Assert.Single(passes);
        Assert.Equal(TipoTravessiaCanteiro.NivelDaPista, passes[0].Mode);
        var tc = IntersectionGenerator.CrosswalkT(d, L, leg);
        Assert.False(L.MedianCurb.Concat(L.MedianCore).Any(p => p.Contains(L.At(leg, tc, 0))), "canteiro sobre a travessia");

        // Opção desligada: a travessia fica onde a esquina e as rampas a põem.
        var off = T(Tpl(t => t.CrosswalkBehindNose = false)).Nodes.Single().Layout;
        Assert.Empty(off.CrosswalkExtra);
    }

    [Fact]
    public void Regra_antiga_do_canteiro_continua_como_opcao()
    {
        var w = T(Tpl(t => t.MedianOpening = AberturaCanteiro.BocaDaVia));
        var L = w.Nodes.Single().Layout;
        Assert.Empty(L.MedianTips);
        Assert.Empty(L.CrosswalkExtra);
        // Canteiro até a boca da transversal + 1 m (a pista dela tem 3,5 m de cada lado do eixo).
        var med = PolygonOps.Union(L.Obstacles);
        Assert.InRange(med.SelectMany(p => p.Outer).Min(v => v.X), 3.5, 6.0);
        Assert.Empty(Y34TipTests.Faults(w));
    }

    [Fact]
    public void Opcoes_novas_da_intersecao_vao_e_voltam_no_JSON()
    {
        var d = new IntersectionDefinition
        {
            MedianOpening = AberturaCanteiro.BocaDaVia, MedianControlRadius = 15, MedianNose = FormaNarizCanteiro.Ogiva, CrosswalkBehindNose = false,
        };
        var back = (IntersectionDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.Equal(AberturaCanteiro.BocaDaVia, back.MedianOpening);
        Assert.Equal(15, back.MedianControlRadius);
        Assert.Equal(FormaNarizCanteiro.Ogiva, back.MedianNose);
        Assert.False(back.CrosswalkBehindNose);
        // Padrões: a regra do DNIT para o VP (Tabela 62: 12 m), nariz automático, travessia atrás do nariz.
        var n = new IntersectionDefinition();
        Assert.Equal(AberturaCanteiro.CurvaDeGiro, n.MedianOpening);
        Assert.Equal(12.0, n.MedianControlRadius);
        Assert.Equal(FormaNarizCanteiro.Automatica, n.MedianNose);
        Assert.True(n.CrosswalkBehindNose);
    }

    // ------------------------------------------------------------------ ciclofaixa

    [Fact]
    public void Ciclofaixa_chega_ate_a_faixa_de_pedestres()
    {
        var w = T();
        var L = w.Nodes.Single().Layout;
        var d = L.Definition!;
        var leg = L.Legs.Single(l => l.Road == 1);
        var far = IntersectionGenerator.CrosswalkT(d, L, leg) + IntersectionGenerator.CrosswalkWidthOf(d, L, leg) / 2;
        var parts = Y34TipTests.Geometry(w);
        var fill = parts.Where(p => p.Def is LinearMarkingDefinition { Code: "CIC-FD" } && p.Def.GroupId != d.Id).Select(p => p.Piece.Shape).ToList();
        Assert.NotEmpty(fill);
        // Nos dois lados da avenida (ciclofaixa de 7,2 a 8,7 m do eixo: canteiro 1,5 + faixa 3,5 + estacionamento 2,2): o fundo
        // da ciclofaixa começa na borda de longe da faixa (antes ficava ~2,4 m sem ciclofaixa).
        foreach (var y in new[] { -7.95, 7.95 })
        {
            Assert.True(fill.Any(p => p.Contains(new Vec2(far + 0.15, y))), $"sem ciclofaixa logo depois da faixa (y = {y})");
            Assert.False(fill.Any(p => p.Contains(new Vec2(far - 0.30, y))), $"ciclofaixa sobre a faixa de pedestres (y = {y})");
        }
        Assert.True(IntersectionGenerator.IsCycleMember(new LinearMarkingDefinition { Code = "CIC-LD" }));
        Assert.False(IntersectionGenerator.IsCycleMember(new LinearMarkingDefinition { Code = "LBO" }));
    }

    // ------------------------------------------------------------------ rotatória: calçada de cada ramo

    /// <summary>Rotatória de uma faixa no cruzamento das vias dadas (cada uma com a sua calçada), como o plugin faz.</summary>
    private static (Y34TipTests.World W, RoundaboutDefinition D, RoundaboutLayout L) Rotatoria(Action<RoundaboutDefinition>? tune,
        params (RoadSetup S, Vec2[] Pts)[] roads)
    {
        var w = new Y34TipTests.World();
        foreach (var (s, pts) in roads)
        {
            var pr = PathReference.FromPoints(pts, 0);
            var axis = new Polyline2(pr.Points);
            var g = s.Build(pr, new OutputSettings(), Cat, axis: axis);
            foreach (var m in g) if (m.Path is { } p && p.Points.Count == pr.Points.Count && p.Points[0].DistanceTo(pr.Points[0]) < 1e-9) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        var d = new RoundaboutDefinition { Center = Vec2.Zero };
        d.ApplyPreset(TipoRotatoria.UmaFaixa);
        tune?.Invoke(d);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, w.Roads, d.OuterRadius + 25);
        // Como o IntersectionService: perfil geral e o de cada lado de cada ramo, lidos dos elementos das vias.
        if (d.MatchRoadSection)
        {
            var ctx = new BuildContext { Catalog = Cat };
            var sides = w.Roads.Select((r, k) => EdgeProfile.FromRoadSides(r.Def, w.Groups[k].Where(m => m.Id != r.Def.Id), ctx)).ToList();
            d.EdgeProfile = EdgeProfile.Best(w.Roads.Select((r, k) => EdgeProfile.FromRoad(r.Def, w.Groups[k].Where(m => m.Id != r.Def.Id), ctx)));
            if (d.EdgeProfile.Count > 0) d.SidewalkWidth = Math.Max(0, EdgeProfile.OuterWidth(d.EdgeProfile) - d.CurbWidth);
            foreach (var l in d.Legs)
                if (w.Roads.FindIndex(r => r.Def.Id == l.RoadId) is var k and >= 0) l.SetEdgeSides(sides[k]);
        }
        var L = RoundaboutGenerator.Layout(d);
        foreach (var g in w.Groups)
            foreach (var m in g)
                foreach (var cut in RoundaboutGenerator.RoadCuts(d, L, m))
                    m.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = cut.Outer.ToList() });
        w.Defs.Add(d);
        foreach (var c in RoundaboutGenerator.Children(d, L, new OutputSettings(), 0)) { c.GroupId = d.Id; w.Defs.Add(c); }
        return (w, d, L);
    }

    /// <summary>As quatro vias da conversa: calçadas de 2, 5, 3 e 4/1,5 m.</summary>
    private static readonly (Func<RoadSetup> S, Vec2[] Pts)[] Quatro =
    {
        (() => Local(2.0, 2.0), new[] { new Vec2(0, 0), new Vec2(120, 0) }),
        (() => Local(5.0, 5.0), new[] { new Vec2(0, 0), new Vec2(0, 120) }),
        (() => Local(3.0, 3.0), new[] { new Vec2(0, 0), new Vec2(-120, 0) }),
        (() => Local(4.0, 1.5), new[] { new Vec2(0, 0), new Vec2(0, -120) }),
    };

    private static (Y34TipTests.World W, RoundaboutDefinition D, RoundaboutLayout L) RotatoriaQuatro(Action<RoundaboutDefinition>? tune) =>
        Rotatoria(tune, Quatro.Select(x => (x.S(), x.Pts)).ToArray());

    /// <summary>Afastamento do bordo externo de <paramref name="area"/> no lado <paramref name="side"/> do ramo, na estaca t.</summary>
    private static double OuterReach(IReadOnlyList<Polygon2> area, RoundaboutLegGeometry g, double t, int side)
    {
        bool In(double o) => area.Any(p => p.Contains(g.At(Vec2.Zero, t, side * o)));
        var o = g.Leg.Width / 2 + 0.05;
        var last = double.NaN;
        for (; o < g.Leg.Width / 2 + 12; o += 0.01) if (In(o)) last = o;
        return last;
    }

    [Fact]
    public void Calcada_da_rotatoria_segue_a_de_cada_lado_de_cada_via()
    {
        foreach (var match in new[] { false, true })
        {
            var (w, d, L) = RotatoriaQuatro(x => x.MatchRoadSection = match);
            Assert.NotEmpty(L.WalkChains);
            var walk = PolygonOps.Union(L.Curb.Concat(L.Sidewalk));
            foreach (var g in L.Legs)
            {
                // Junto ao limite da zona (onde emenda com a calçada da via): a largura daquele lado da via, sem degrau. A zona é um
                // círculo: a 2,5 m do limite no eixo, a borda da calçada (até ~13 m do eixo) ainda está dentro dela.
                var t = RoundaboutGenerator.Ray(L.Zone, Vec2.Zero, g.Dir) - 2.5;
                foreach (var side in new[] { 1, -1 })
                {
                    var road = w.Roads.Single(r => r.Def.Id == g.Leg.RoadId).Def;
                    var expected = g.Leg.RoadSign * side > 0 ? road.LeftSidewalk : road.RightSidewalk;
                    Assert.Equal(expected, RoundaboutGenerator.WalkTotal(d, g.Leg, side), 2);
                    Assert.InRange(OuterReach(walk, g, t, side), g.Leg.Width / 2 + expected - 0.04, g.Leg.Width / 2 + expected + 0.02);
                }
            }
            // Entre os ramos a largura passa aos poucos de uma via à outra (sem degraus): pontos a no máximo 0,25 m, variação de
            // largura bem menor que isso (o alargamento junto às rampas tem transição de 4 m).
            foreach (var ch in L.WalkChains)
                for (int i = 1; i < ch.Points.Count; i++)
                {
                    Assert.True(ch.Points[i].DistanceTo(ch.Points[i - 1]) <= 1.0 + 1e-6);
                    Assert.True(Math.Abs(ch.WidthAt(i) - ch.WidthAt(i - 1)) < 0.12, $"degrau de {Math.Abs(ch.WidthAt(i) - ch.WidthAt(i - 1)):0.000} m na calçada");
                }
            // Sem sobreposição com o meio-fio e a pista, e nada fora da zona.
            Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(L.Sidewalk, L.Curb)) < 0.01);
            Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(L.Sidewalk, L.Pavement)) < 0.01);
            Assert.True(PolygonOps.TotalArea(PolygonOps.Difference(L.Sidewalk, new[] { L.Zone })) < 0.01);
        }
    }

    [Fact]
    public void Composicao_de_cada_via_segue_pela_calcada_da_rotatoria_sem_pisos_sobrepostos()
    {
        var (w, d, L) = RotatoriaQuatro(null);
        Assert.True(d.MatchRoadSection);
        var parts = Y34TipTests.Geometry(w);
        // As peças da calçada em volta (meio-fio, faixa gramada, passeio, sarjeta) – as árvores da ilha ficam de fora.
        var mine = parts.Where(p => p.Def == d && p.Piece.Solid == null && p.Piece.Profile == null && p.Piece.Elevation >= -1e-6
                                    && p.Piece.Layer is "CALCADA" or "GRAMADO" or "MEIO-FIO" or "SARJETA").ToList();
        var walk = mine.Where(p => p.Piece.Layer is "CALCADA" or "GRAMADO" or "MEIO-FIO").Select(p => p.Piece.Shape).ToList();
        Assert.NotEmpty(walk);
        // Em cada boca, a borda externa das peças da calçada é a calçada daquele lado da via.
        foreach (var g in L.Legs)
        {
            var t = RoundaboutGenerator.Ray(L.Zone, Vec2.Zero, g.Dir) - 2.5;
            foreach (var side in new[] { 1, -1 })
            {
                var expected = RoundaboutGenerator.WalkTotal(d, g.Leg, side);
                Assert.InRange(OuterReach(walk, g, t, side), g.Leg.Width / 2 + expected - 0.06, g.Leg.Width / 2 + expected + 0.03);
            }
        }
        // Pisos da rotatória sem sobreposição entre si.
        for (int i = 0; i < mine.Count; i++)
            for (int j = i + 1; j < mine.Count; j++)
            {
                if (Math.Abs(mine[i].Piece.Elevation - mine[j].Piece.Elevation) > 0.5) continue;
                var a = PolygonOps.TotalArea(PolygonOps.Intersect(new[] { mine[i].Piece.Shape }, new[] { mine[j].Piece.Shape }));
                Assert.True(a < 0.01, $"{mine[i].Piece.Layer} × {mine[j].Piece.Layer}: {a:0.000} m²");
            }
    }

    [Fact]
    public void Opcao_desligada_volta_a_largura_unica_e_ramos_sem_via_usam_a_geral()
    {
        var (_, d, L) = RotatoriaQuatro(x => { x.SidewalkPerLeg = false; x.MatchRoadSection = false; });
        Assert.Empty(L.WalkChains);
        Assert.All(d.Legs, l => Assert.Equal(d.SidewalkWidth + d.CurbWidth, RoundaboutGenerator.WalkTotal(d, l, 1), 6));
        // A mesma faixa constante de antes: afastamento do meio-fio + calçada geral em volta da pista.
        var cw = d.CurbWidth;
        var full = PolygonOps.Union(L.Pavement.Concat(L.SplitterCurb).Concat(L.SplitterCore).Concat(L.Refuges).Concat(L.Apron).Append(L.Island));
        var expected = PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(full, cw + d.SidewalkWidth, true), PolygonOps.Offset(full, cw, true)), new[] { L.Zone });
        Assert.InRange(PolygonOps.TotalArea(L.Sidewalk), PolygonOps.TotalArea(expected) * 0.98, PolygonOps.TotalArea(expected) * 1.02);

        // Ramos sem via (por ângulo): a calçada geral em volta toda, ou a de cada ramo quando informada.
        var m = new RoundaboutDefinition { Center = Vec2.Zero, MatchRoadSection = false };
        m.ApplyPreset(TipoRotatoria.UmaFaixa);
        m.Legs = new[] { 0.0, 90, 180, 270 }.Select(a => new RoundaboutLeg { AngleDeg = a, Width = 7 }).ToList();
        Assert.Empty(RoundaboutGenerator.Layout(m).WalkChains);
        m.Legs[1].SidewalkLeft = 4.0;
        Assert.NotEmpty(RoundaboutGenerator.Layout(m).WalkChains);
    }

    [Fact]
    public void Ramos_da_via_que_atravessa_trocam_os_lados()
    {
        // Via passante leste–oeste: calçada de 4,5 m à direita do eixo (sul) e 1,5 m à esquerda (norte).
        var (w, d, L) = Rotatoria(x => x.MatchRoadSection = false, (Local(4.5, 1.5), new[] { new Vec2(-120, 0), new Vec2(120, 0) }),
            (Local(3.0, 2.0), new[] { new Vec2(0, 0), new Vec2(85, 85) }));
        var east = d.Legs.Single(l => Math.Abs(l.AngleDeg) < 1);
        var west = d.Legs.Single(l => Math.Abs(Math.Abs(l.AngleDeg) - 180) < 1);
        Assert.Equal(1, east.RoadSign);
        Assert.Equal(-1, west.RoadSign);
        // Para quem sai a leste, o sul fica à direita; para quem sai a oeste, à esquerda.
        Assert.Equal(4.5, east.SidewalkRight);
        Assert.Equal(1.5, east.SidewalkLeft);
        Assert.Equal(4.5, west.SidewalkLeft);
        Assert.Equal(1.5, west.SidewalkRight);
        // A personalização de um ramo ligado não leva a calçada (ela vem da via); a de um ramo sem via, sim.
        var linked = new RoundaboutLeg { RoadId = "x", SidewalkLeft = 9 };
        linked.CopySettingsFrom(new RoundaboutLeg { SidewalkLeft = 1 });
        Assert.Equal(9, linked.SidewalkLeft);
        var free = new RoundaboutLeg();
        free.CopySettingsFrom(new RoundaboutLeg { SidewalkLeft = 1, SidewalkRight = 2 });
        Assert.Equal(1, free.SidewalkLeft);
        Assert.Equal(2, free.SidewalkRight);
    }

    [Fact]
    public void Calcada_estreita_alarga_junto_a_rampa_e_a_travessia_fica_a_um_veiculo_da_entrada()
    {
        var (w, d, L) = RotatoriaQuatro(x => x.MatchRoadSection = false);
        var ramps = w.Defs.OfType<RampDefinition>().Where(r => r.GroupId == d.Id).ToList();
        Assert.NotEmpty(ramps);
        // Calçadas de 2,0 e 1,5 m não comportam a rampa (1,80 m) e a faixa livre de 1,20 m (NBR 9050): a calçada da rotatória
        // alarga junto à rampa (no meio-fio curvo da entrada o rebaixamento total, de 7 a 8 m, não cabe).
        Assert.DoesNotContain(ramps, r => r.Type == TipoRampa.RebaixamentoTotal);
        Assert.Equal(3, L.Warnings.Count(x => x.Contains("alarga")));
        var walk = PolygonOps.Union(L.Curb.Concat(L.Sidewalk).Concat(ramps.Select(r => RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points)))));
        foreach (var g in L.Legs)
        {
            // A travessia fica onde a rotatória a põe (a cerca de um veículo da entrada), não é empurrada para longe.
            Assert.InRange(g.CrosswalkT - L.ToOuter(Vec2.Zero, g.Dir), d.CrosswalkDistance - 0.01, d.CrosswalkDistance + RoundaboutGenerator.MaxCrosswalkShift + 0.01);
            foreach (var (sg, frame) in new[] { (1, g.RampHi), (-1, g.RampLo) })
            {
                if (frame is not { } f) continue;
                var (_, run, _) = RampGenerator.Dimensions(ramps[0]);
                var need = Math.Max(RoundaboutGenerator.WalkTotal(d, g.Leg, sg), run + RoundaboutGenerator.FaixaLivreRampa);
                var o = 0.05;
                while (o < 12 && walk.Any(p => p.Contains(f.Curb + f.Up * (o + 0.02)))) o += 0.02;
                Assert.True(o >= need - 0.06, $"ramo a {g.Leg.AngleDeg:0}° lado {sg}: calçada de {o:0.00} m junto à rampa (precisa de {need:0.00} m)");
            }
        }
        // Nenhuma rampa sobre a pista (a conferência de falhas das interseções: pisos sobre a pista, bordos sem meio-fio).
        Assert.Empty(Y36Faults(w));
    }

    /// <summary>Falhas de modelagem em volta da rotatória (as mesmas conferências das interseções).</summary>
    private static List<string> Y36Faults(Y34TipTests.World w) =>
        Y34TipTests.FaultsAt(Y34TipTests.Geometry(w), Vec2.Zero, 45, true);

    [Fact]
    public void Opcoes_novas_da_rotatoria_vao_e_voltam_no_JSON()
    {
        var d = new RoundaboutDefinition { SidewalkPerLeg = false };
        d.Legs.Add(new RoundaboutLeg
        {
            AngleDeg = 30, SidewalkLeft = 4.25, SidewalkRight = 1.5, RoadSign = -1,
            EdgeLeft = { new EdgeBand { D0 = 0, D1 = 0.15, Code = "MEIO-FIO", Color = MarkingColor.Concreto } },
        });
        var back = (RoundaboutDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.False(back.SidewalkPerLeg);
        var l = back.Legs.Single();
        Assert.Equal(4.25, l.SidewalkLeft);
        Assert.Equal(1.5, l.SidewalkRight);
        Assert.Equal(-1, l.RoadSign);
        Assert.Equal("MEIO-FIO", l.EdgeLeft.Single().Code);
        Assert.Empty(l.EdgeRight);
        // Rotatória antiga (sem os campos): a calçada de cada ramo vale a geral até a próxima atualização pelas vias.
        Assert.True(new RoundaboutDefinition().SidewalkPerLeg);
        Assert.Null(new RoundaboutLeg().SidewalkLeft);
    }
}
