using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AE (item 9): ciclofaixas presas à via (lado + afastamento) – acompanham curvas e mudanças de largura e seguem pelos
/// cruzamentos com a marcação de cruzamento rodocicloviário (MCC), pintura colorida opcional, linha seccionada na zona de
/// conflito com as conversões e a interseção entre ciclofaixas que se cruzam.
/// </summary>
public class AE1BikeLaneTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static ElementoSecao E(TipoElementoSecao t, double w) => new() { Tipo = t, Largura = w };

    /// <summary>Via com ciclofaixa (1,50 m) entre a faixa e o meio-fio dos dois lados: faixa 0–L, ciclofaixa L–L+1,5, sarjeta 0,30.</summary>
    public static RoadSetup Bike(double lane = 3.3, bool left = true, bool right = true, HierarquiaViaria h = HierarquiaViaria.Coletora,
        Action<ElementoSecao>? cfg = null)
    {
        var s = new RoadSetup { Hierarchy = h, Speed = 50, Center = CenterTreatment.LFO2, SarjetaSomada = true };
        foreach (var (side, on) in new[] { (s.Right, right), (s.Left, left) })
        {
            side.Add(E(TipoElementoSecao.FaixaRolamento, lane));
            if (on)
            {
                var b = E(TipoElementoSecao.Ciclofaixa, 1.5);
                cfg?.Invoke(b);
                side.Add(b);
            }
            side.Add(E(TipoElementoSecao.Calcada, 2.5));
        }
        return s;
    }

    /// <summary>Vias pelos eixos (cada uma com a sua seção) e as interseções nos nós, como o plugin faz.</summary>
    public static Y34TipTests.World World(IntersectionDefinition? template, params (RoadSetup S, Vec2[] Pts)[] roads)
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
        template ??= new IntersectionDefinition { Control = ControleIntersecao.Pare };
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            if (!IntersectionGenerator.NeedsIntersection(rs, node)) continue;
            var d = (IntersectionDefinition)template.CloneWithNewId();
            d.Node = node;
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, Cat));
        }
        return w;
    }

    private static readonly Vec2[] MainAxis = { new(-120, 0), new(120, 0) };
    private static readonly Vec2[] CrossAxis = { new(0, -120), new(0, 120) };
    private static readonly Vec2[] StemAxis = { new(0, -120), new(0, 0) };

    private static List<Y34TipTests.Part> Paint(List<Y34TipTests.Part> parts, params string[] codes) =>
        parts.Where(p => p.Def is LinearMarkingDefinition l && codes.Contains(l.Code)).ToList();

    /// <summary>Peças com o centro numa janela (x0..x1, y0..y1).</summary>
    private static List<Y34TipTests.Part> In(IEnumerable<Y34TipTests.Part> parts, double x0, double x1, double y0, double y1) =>
        parts.Where(p => p.Piece.Shape.Centroid is var c && c.X >= x0 && c.X <= x1 && c.Y >= y0 && c.Y <= y1).ToList();

    /// <summary>A pintura cobre o ponto.</summary>
    private static bool Covers(IEnumerable<Y34TipTests.Part> parts, Vec2 p) => parts.Any(x => x.Piece.Shape.Contains(p));

    private static List<string> Faults(Y34TipTests.World w)
    {
        var parts = Y34TipTests.Geometry(w);
        return w.Nodes.SelectMany(n => Y34TipTests.FaultsAt(parts, n.Layout.Node, Math.Max(25, n.Layout.Radius + 6), true)).ToList();
    }

    // ------------------------------------------------------------------ largura variável

    [Fact]
    public void Ciclofaixa_acompanha_o_alargamento_da_pista_sem_mudar_de_largura()
    {
        var s = Bike();
        var p0 = s.LarguraAtual(ElementoTrecho.Pista)!.Value;
        s.TrechosLargura.Add(new TrechoLargura { Elemento = ElementoTrecho.Pista, EstacaA = 40, EstacaB = 80, NovaLargura = p0 + 2, TransicaoA = 10, TransicaoB = 10 });
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(150, 0) });
        var defs = s.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, axis: axis);
        var fd = defs.OfType<LinearMarkingDefinition>().Single(l => l.Code == "CIC-FD" && l.Offset < 0);
        var pieces = MarkingBuilder.Build(fd, axis, Ctx).Pieces;
        (double Lo, double Hi) Span(double x)
        {
            var cut = new[] { Polygon2.Rectangle(new Vec2(x - 0.05, -100), new Vec2(x + 0.05, 100)) };
            var ys = PolygonOps.Intersect(pieces.Select(p => p.Shape), cut).SelectMany(p => p.Outer).Select(v => v.Y).ToList();
            return (ys.Min(), ys.Max());
        }
        // Fora do trecho: 3,30–4,80 m do eixo. No trecho: o meio-fio vai 1 m para fora e a ciclofaixa vai junto, com 1,50 m.
        var (lo0, hi0) = Span(10);
        Assert.InRange(hi0 - lo0, 1.5 - 0.03, 1.5 + 0.03);
        Assert.InRange(lo0, -4.8 - 0.03, -4.8 + 0.03);
        foreach (var x in new[] { 40.0, 60.0, 80.0 })
        {
            var (lo, hi) = Span(x);
            Assert.InRange(hi - lo, 1.5 - 0.03, 1.5 + 0.03);
            Assert.InRange(lo, -5.8 - 0.03, -5.8 + 0.03);
        }
        // A linha de delimitação também vai junto (borda interna da ciclofaixa).
        var ld = defs.OfType<LinearMarkingDefinition>().Single(l => l.Code == "CIC-LD" && l.Offset < 0);
        var lp = MarkingBuilder.Build(ld, axis, Ctx).Pieces;
        var at60 = PolygonOps.Intersect(lp.Select(p => p.Shape), new[] { Polygon2.Rectangle(new Vec2(59.95, -100), new Vec2(60.05, 100)) })
            .SelectMany(p => p.Outer).Average(v => v.Y);
        Assert.InRange(at60, -4.3 - 0.03, -4.3 + 0.03);
    }

    // ------------------------------------------------------------------ cruz

    [Fact]
    public void Ciclofaixa_atravessa_a_cruz_com_MCC_e_tracejado_na_zona_de_conflito()
    {
        var w = World(null, (Bike(), MainAxis), (RoadTemplates.All[0].Create(), CrossAxis));
        var L = w.Nodes.Single().Layout;
        Assert.Equal(0, L.Main);
        var parts = Y34TipTests.Geometry(w);
        var mcc = Paint(parts, "MCC");
        var half2 = w.Roads[1].Def.RightWidth;               // meia pista da transversal (boca)
        // MCC nas duas bordas das duas ciclofaixas, de um lado a outro da boca da transversal.
        foreach (var y in new[] { -3.3, -4.8, 3.3, 4.8 })
        {
            Assert.True(Covers(mcc, new Vec2(0.2, y)) || Covers(mcc, new Vec2(-0.2, y)) || Covers(mcc, new Vec2(0.6, y)), $"sem MCC em y = {y}");
            Assert.NotEmpty(In(mcc, half2 - 1.5, half2, y - 0.3, y + 0.3));
            Assert.NotEmpty(In(mcc, -half2, -half2 + 1.5, y - 0.3, y + 0.3));
        }
        // Na boca não sobra a linha contínua nem o fundo da via.
        var bike = Paint(parts, "CIC-LD", "CIC-FD");
        Assert.Empty(In(bike, -half2 + 0.5, half2 - 0.5, -5.0, -3.0));
        Assert.Empty(In(bike, -half2 + 0.5, half2 - 0.5, 3.0, 5.0));
        // Nada de pintura da ciclofaixa sobre as faixas de pedestres.
        var ftp = parts.Where(p => p.Def is LinearMarkingDefinition { Code: "FTP-1" or "FTP-2" }).Select(p => p.Piece.Shape).ToList();
        Assert.NotEmpty(ftp);
        var cyc = Paint(parts, "MCC", "CIC-LD", "CIC-FD").Select(p => p.Piece.Shape).ToList();
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(cyc, ftp)) < 1e-3, "pintura da ciclofaixa sobre a faixa de pedestres");

        // Zona de conflito: quem chega pelo oeste (lado y < 0) converte à direita para a transversal cruzando a ciclofaixa –
        // a linha de delimitação fica seccionada nos 20 m antes do cruzamento; do lado de saída segue contínua.
        var ld = Paint(parts, "CIC-LD");
        var mccWest = In(mcc, -40, 0, -3.6, -3.0).Min(p => p.Piece.Shape.Outer.Min(v => v.X));
        var dashed = In(ld, mccWest - 30, mccWest - 0.5, -3.6, -3.0);
        Assert.True(dashed.Count >= 6, $"tracejado da zona de conflito com {dashed.Count} traços");
        Assert.All(dashed, p => Assert.InRange(p.Piece.Shape.Outer.Max(v => v.X) - p.Piece.Shape.Outer.Min(v => v.X), 0.2, 1.05));
        Assert.Contains(w.Defs.OfType<LinearMarkingDefinition>(), l => l.Code == "CIC-LD" && (l.Variant ?? "").StartsWith("Seccionada"));
        // Lado de saída do ramo oeste (y > 0): contínua (sem falhas) depois da faixa de pedestres.
        for (var x = -40.0; x <= -25; x += 0.25)
            Assert.True(Covers(ld, new Vec2(x, 3.3)), $"linha de saída interrompida em x = {x}");
        Assert.Empty(Faults(w));
    }

    // ------------------------------------------------------------------ T

    [Fact]
    public void Ciclofaixa_no_T_segue_continua_no_lado_sem_boca_e_com_MCC_na_boca()
    {
        var w = World(null, (Bike(), MainAxis), (RoadTemplates.All[0].Create(), StemAxis));
        var parts = Y34TipTests.Geometry(w);
        var mcc = Paint(parts, "MCC");
        var ld = Paint(parts, "CIC-LD");
        var fd = Paint(parts, "CIC-FD");
        // Lado contínuo (norte, sem boca): linha e fundo seguem pelo cruzamento, sem MCC.
        Assert.Empty(In(mcc, -30, 30, 3.0, 5.2));
        foreach (var x in new[] { -2.0, 0.0, 2.0 })
        {
            Assert.True(Covers(ld, new Vec2(x, 3.3)), $"linha da ciclofaixa interrompida no lado contínuo em x = {x}");
            Assert.True(Covers(fd, new Vec2(x, 4.05)), $"fundo da ciclofaixa interrompido no lado contínuo em x = {x}");
        }
        // Lado da boca (sul): MCC nas duas bordas.
        foreach (var y in new[] { -3.3, -4.8 })
            Assert.True(Covers(mcc, new Vec2(0.2, y)) || Covers(mcc, new Vec2(-0.2, y)) || Covers(mcc, new Vec2(0.6, y)), $"sem MCC em y = {y}");
        // Tracejado só onde há conversão: ramo oeste, lado sul. No ramo leste o lado norte (chegada) não tem boca – contínua.
        var mccWest = In(mcc, -40, 0, -3.6, -3.0).Min(p => p.Piece.Shape.Outer.Min(v => v.X));
        Assert.True(In(ld, mccWest - 30, mccWest - 0.5, -3.6, -3.0).Count >= 6);
        for (var x = 20.0; x <= 45; x += 0.25)
            Assert.True(Covers(ld, new Vec2(x, 3.3)), $"chegada pelo leste (sem conversão) seccionada em x = {x}");
        Assert.Empty(Faults(w));
    }

    // ------------------------------------------------------------------ ciclofaixas que se cruzam

    [Fact]
    public void Ciclofaixas_que_se_cruzam_geram_a_intersecao_entre_elas()
    {
        void Color(ElementoSecao e) { e.CruzamentoColorido = true; e.CorCruzamento = MarkingColor.Verde; }
        var w = World(null, (Bike(cfg: Color), MainAxis), (Bike(3.0, h: HierarquiaViaria.Local, cfg: Color), CrossAxis));
        var parts = Y34TipTests.Geometry(w);
        var mcc = Paint(parts, "MCC");
        // Quadrado comum à ciclofaixa sul da principal (y −4,8..−3,3) e à leste da transversal (x 3,0..4,5).
        var overlap = Polygon2.Rectangle(new Vec2(3.0, -4.8), new Vec2(4.5, -3.3));
        var inner = Polygon2.Rectangle(new Vec2(3.25, -4.55), new Vec2(4.25, -3.55));
        // Sem quadrados da MCC dentro da área comum (nenhuma das duas atravessa a outra com os quadrados).
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(mcc.Select(p => p.Piece.Shape), new[] { inner })) < 1e-3, "MCC dentro da interseção das ciclofaixas");
        // A pintura colorida cobre a área comum uma vez só.
        var green = parts.Where(p => p.Piece.Color == MarkingColor.Verde).Select(p => p.Piece.Shape).ToList();
        Assert.NotEmpty(green);
        var each = green.Sum(g => PolygonOps.TotalArea(PolygonOps.Intersect(new[] { g }, new[] { overlap })));
        var once = PolygonOps.TotalArea(PolygonOps.Intersect(PolygonOps.Union(green), new[] { overlap }));
        Assert.True(once > 0.8, $"área comum sem pintura colorida ({once:0.00} m²)");
        Assert.InRange(each, once - 0.02, once + 0.02);
        // As duas ciclofaixas ainda têm MCC fora da área comum, dos dois lados de cada uma.
        Assert.True(Covers(mcc, new Vec2(0, -3.3)) || Covers(mcc, new Vec2(0.4, -3.3)));
        Assert.True(Covers(mcc, new Vec2(3.0, 0)) || Covers(mcc, new Vec2(3.0, 0.4)));
        Assert.Empty(Faults(w));
    }

    // ------------------------------------------------------------------ cor e largura

    [Fact]
    public void Pintura_do_cruzamento_opcional_e_com_cor_configuravel()
    {
        var off = World(null, (Bike(), MainAxis), (RoadTemplates.All[0].Create(), CrossAxis));
        var offParts = Y34TipTests.Geometry(off);
        var box = Polygon2.Rectangle(new Vec2(-2, -4.6), new Vec2(2, -3.5));
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(offParts.Where(p => p.Def is LinearMarkingDefinition { Code: "CIC-FD" }).Select(p => p.Piece.Shape), new[] { box })) < 1e-3);
        var on = World(null, (Bike(cfg: e => { e.CruzamentoColorido = true; e.CorCruzamento = MarkingColor.Verde; }), MainAxis), (RoadTemplates.All[0].Create(), CrossAxis));
        var onParts = Y34TipTests.Geometry(on);
        var col = onParts.Where(p => p.Piece.Color == MarkingColor.Verde).Select(p => p.Piece.Shape).ToList();
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(col, new[] { box })) > 0.9 * box.Area, "cruzamento sem pintura colorida");
        // Padrão da cor: vermelha.
        var red = World(null, (Bike(cfg: e => e.CruzamentoColorido = true), MainAxis), (RoadTemplates.All[0].Create(), CrossAxis));
        Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(Y34TipTests.Geometry(red).Where(p => p.Def is LinearMarkingDefinition { Code: "CIC-FD" } && p.Piece.Color == MarkingColor.Vermelha)
            .Select(p => p.Piece.Shape), new[] { box })) > 0.9 * box.Area);
        Assert.Empty(Faults(on));
    }

    [Fact]
    public void Cruzamento_acompanha_a_largura_da_via()
    {
        foreach (var lane in new[] { 3.0, 3.6 })
        {
            var w = World(null, (Bike(lane), MainAxis), (RoadTemplates.All[0].Create(), CrossAxis));
            var mcc = Paint(Y34TipTests.Geometry(w), "MCC");
            foreach (var y in new[] { -lane, -(lane + 1.5), lane, lane + 1.5 })
                Assert.True(Covers(mcc, new Vec2(0.2, y)) || Covers(mcc, new Vec2(-0.2, y)) || Covers(mcc, new Vec2(0.6, y)), $"faixa {lane}: sem MCC em y = {y}");
            Assert.Empty(In(mcc, -3, 3, -lane + 0.5, lane - 0.5));
            Assert.Empty(Faults(w));
        }
    }

    // ------------------------------------------------------------------ presa à via (lado + afastamento)

    [Fact]
    public void Ciclofaixa_inserida_na_secao_pelo_lado_e_afastamento_do_meio_fio()
    {
        // Via local (faixa 3,00 + estacionamento 2,20): afastamento 2,70 m do meio-fio = estacionamento por fora + 0,50 m zebrado.
        var s = RoadTemplates.All[0].Create();
        var curb0 = s.PavementDefinition().RightWidth;
        var msgs = s.InserirCiclofaixa(false, new BikeLaneSetup { Width = 1.5 }.ToElemento(), 2.7, ocuparPista: false);
        Assert.Equal(new[] { TipoElementoSecao.FaixaRolamento, TipoElementoSecao.Ciclofaixa, TipoElementoSecao.FaixaSeguranca, TipoElementoSecao.Estacionamento, TipoElementoSecao.Calcada },
            s.Right.Select(e => e.Tipo).ToArray());
        Assert.Equal(0.5, s.Right[2].Largura, 6);
        Assert.InRange(s.PavementDefinition().RightWidth - curb0, 2.0 - 1e-6, 2.0 + 1e-6);   // pista alargada (1,50 + 0,50)
        Assert.NotEmpty(msgs);
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(80, 0) });
        var defs = s.Build(PathReference.FromPoints(axis.Points, 0), new OutputSettings(), Cat, axis: axis);
        var fd = defs.OfType<LinearMarkingDefinition>().Single(l => l.Code == "CIC-FD");
        Assert.InRange(fd.Offset, -(3.0 + 0.75) - 1e-6, -(3.0 + 0.75) + 1e-6);
        Assert.Contains(defs.OfType<LinearMarkingDefinition>(), l => l.Code == "CIC-LD" && Math.Abs(l.Offset + 3.0) < 1e-6);
        // A seção guardada na via traz a ciclofaixa: a via refeita a partir do eixo a mantém.
        Assert.Contains(RoadTemplates.FromJson(defs.OfType<RoadPavementDefinition>().Single().SetupJson)!.Right, e => e.Tipo == TipoElementoSecao.Ciclofaixa);

        // Ocupando a pista: a faixa vizinha cede a largura e o meio-fio fica no lugar.
        var c = new RoadSetup { SarjetaSomada = true, Center = CenterTreatment.LFO2 };
        c.Right.Add(E(TipoElementoSecao.FaixaRolamento, 4.6));
        c.Right.Add(E(TipoElementoSecao.Calcada, 2.5));
        c.Left.Add(E(TipoElementoSecao.FaixaRolamento, 3.5));
        c.Left.Add(E(TipoElementoSecao.Calcada, 2.5));
        var w0 = c.PavementDefinition().RightWidth;
        c.InserirCiclofaixa(false, new BikeLaneSetup { Width = 1.5 }.ToElemento(), 0, ocuparPista: true);
        Assert.Equal(3.1, c.Right[0].Largura, 6);
        Assert.InRange(c.PavementDefinition().RightWidth, w0 - 1e-6, w0 + 1e-6);
        // Faixa que ficaria estreita demais: a pista alarga.
        var n = RoadTemplates.All[0].Create();
        var m = n.InserirCiclofaixa(true, new BikeLaneSetup { Width = 1.5 }.ToElemento(), 0, ocuparPista: true);
        Assert.Equal(3.0, n.Left[0].Largura, 6);
        Assert.Contains(m, x => x.Contains("alargada"));
    }

    [Fact]
    public void Campos_novos_sao_opcionais_e_projetos_antigos_mantem_o_padrao()
    {
        var s = Bike(cfg: e => { e.CruzamentoColorido = true; e.CorCruzamento = MarkingColor.Azul; e.ZonaConflito = 12; });
        var back = RoadTemplates.FromJson(RoadTemplates.ToJson(s))!;
        var e = back.Right.Single(x => x.Tipo == TipoElementoSecao.Ciclofaixa);
        Assert.True(e.CruzamentoColorido);
        Assert.Equal(MarkingColor.Azul, e.CorCruzamento);
        Assert.Equal(12, e.ZonaConflito);
        // JSON de um projeto salvo antes (sem os campos): sem pintura no cruzamento, cor e zona padrão.
        var old = RoadTemplates.FromJson("{\"Right\":[{\"Tipo\":\"Ciclofaixa\",\"Largura\":1.5}]}")!;
        var o = old.Right.Single();
        Assert.False(o.CruzamentoColorido);
        Assert.Null(o.CorCruzamento);
        Assert.Null(o.ZonaConflito);
        Assert.Equal(20, o.ZonaConflitoEfetiva);
    }
}
