using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 16 do plano 2 – esquinas (pedido de 2026-10-27): item 19, chanfro de 1,50 m por padrão medido sobre os limites do lote
/// (imagem 4); item 20, faixa tátil da esquina paralela ao alinhamento predial, seguindo o chanfro, com alerta nas mudanças de
/// direção (imagem 1); item 12, esquina da extensão de calçada em chanfro e rampas alinhadas às travessias (imagem 2).
/// </summary>
public class Etapa16EsquinasTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    // ------------------------------------------------------------------ item 19 – chanfro pelos catetos

    /// <summary>Quadra em paralelogramo com a quina V = (0, 0) de ângulo <paramref name="ang"/> entre duas testadas.</summary>
    private static QuadraDefinition Paralelogramo(double ang)
    {
        var d = Y34TipTests.Dir(ang) * 40;
        var q = new QuadraDefinition { Nome = "A", Frente = 12, Profundidade = 20, Contorno = new() { Vec2.Zero, new Vec2(60, 0), new Vec2(60, 0) + d, d } };
        q.Testadas.Add(new TestadaLote { Id = "T1", Ponto = new Vec2(30, 0) });
        q.Testadas.Add(new TestadaLote { Id = "T2", Ponto = d / 2 });
        return q;
    }

    [Theory]
    [InlineData(90.0)]
    [InlineData(60.0)]
    [InlineData(120.0)]
    public void Item19_QuadraNova_Chanfro150EmCadaLado_SobreOsLimites(double ang)
    {
        var q = Paralelogramo(ang);
        var antes = LotDivider.Divide(q).Parcelas.Sum(p => p.Area);
        var lot = new LoteamentoDefinition();
        Assert.Equal(1.5, lot.ChanfroQuadrasNovas);
        Reconhecimento.ComChanfroPadrao(q, lot);
        Assert.True(q.Chanfro);
        Assert.Equal(MedidaChanfro.Catetos, q.ChanfroMedida);
        var ch = Assert.Single(LotDivider.Chanfros(q));
        // 1,50 m em cada lado, medidos sobre os limites a partir do encontro dos alinhamentos (também em esquina não reta).
        Assert.Equal(1.5, ch.Vertice.DistanceTo(ch.A), 6);
        Assert.Equal(1.5, ch.Vertice.DistanceTo(ch.B), 6);
        Assert.Equal(2 * 1.5 * Math.Sin(ang / 2 * Math.PI / 180), ch.Face, 6);
        // O lote de esquina perde o triângulo: ½ × 1,5 × 1,5 × sen(ângulo).
        var tri = 0.5 * 1.5 * 1.5 * Math.Sin(ang * Math.PI / 180);
        Assert.Equal(antes - tri, LotDivider.Divide(q).Parcelas.Sum(p => p.Area), 2);
    }

    [Fact]
    public void Item19_PorEsquina_NaMedidaDaQuadra_EAFaceContinuaComoOpcao()
    {
        var q = Paralelogramo(90);
        q.Chanfro = true;
        q.ChanfroMedida = MedidaChanfro.Catetos;
        q.ChanfroCateto = 1.5;
        // Medida própria na esquina (catetos de 2,5 m).
        q.Esquinas.Add(new EsquinaLote { Ponto = Vec2.Zero, Chanfro = 2.5 });
        Assert.Equal(2.5, Assert.Single(LotDivider.Chanfros(q)).Cateto, 6);
        // Sem chanfro nesta esquina.
        q.Esquinas[0].Chanfro = 0;
        Assert.Empty(LotDivider.Chanfros(q));
        // Pela face (a medida até a 2.20): face de 3 m = catetos de 3/√2.
        q.Esquinas.Clear();
        q.ChanfroMedida = MedidaChanfro.Face;
        q.ChanfroFace = 3;
        var f = Assert.Single(LotDivider.Chanfros(q));
        Assert.Equal(3, f.Face, 6);
        Assert.Equal(3 / Math.Sqrt(2), f.Cateto, 6);
    }

    [Fact]
    public void Item19_QuadrasAntigasAbremIguais_EOPadraoSoNasNovas()
    {
        // Quadra gravada antes da 2.21 (sem os campos novos): medida pela face, como estava.
        var json = JsonNode.Parse(new QuadraDefinition { Nome = "A", Chanfro = true, ChanfroFace = 3 }.ToJson())!.AsObject();
        Assert.True(json.Remove("chanfroMedida") && json.Remove("chanfroCateto"));
        var lida = (QuadraDefinition)MarkingDefinition.FromJson(json.ToJsonString())!;
        Assert.Equal(MedidaChanfro.Face, lida.ChanfroMedida);
        Assert.Equal(3, lida.ChanfroMedidaQuadra);
        // Loteamento gravado antes: as quadras novas dele nascem com 1,50 m.
        var lj = JsonNode.Parse(new LoteamentoDefinition().ToJson())!.AsObject();
        Assert.True(lj.Remove("chanfroQuadrasNovas"));
        var lot = (LoteamentoDefinition)MarkingDefinition.FromJson(lj.ToJsonString())!;
        Assert.Equal(1.5, lot.ChanfroQuadrasNovas);
        // O reconhecimento sozinho não liga o chanfro (quadras de teste e do Autoteste como antes); o plugin aplica o padrão ao
        // criar a quadra. Padrão 0 no loteamento = quadras novas sem chanfro.
        var r = new QuadraReconhecida("Q", Polygon2.Rectangle(Vec2.Zero, new Vec2(60, 40)), new List<Vec2> { new(30, 0), new(0, 20) });
        Assert.False(Reconhecimento.NovaQuadra(r, lot, 12, 30).Chanfro);
        lot.ChanfroQuadrasNovas = 0;
        Assert.False(Reconhecimento.ComChanfroPadrao(Reconhecimento.NovaQuadra(r, lot, 12, 30), lot).Chanfro);
        lot.ChanfroQuadrasNovas = 2;
        var q2 = Reconhecimento.ComChanfroPadrao(Reconhecimento.NovaQuadra(r, lot, 12, 30), lot);
        Assert.True(q2.Chanfro);
        Assert.Equal(2, q2.ChanfroCateto);
    }

    [Fact]
    public void Item19_Cruzamento_CalcadaDaIntersecaoOcupaOsChanfrosDe150()
    {
        var c = LVCorrecoesTests.Cruz();
        foreach (var q in c.Quadras) Reconhecimento.ComChanfroPadrao(q, c.Lot);
        var ch = c.Quadras.SelectMany(LotDivider.Chanfros).ToList();
        Assert.Equal(4, ch.Count);
        Assert.All(ch, x => Assert.Equal(1.5, x.Cateto, 3));
        var parts = LVCorrecoesTests.GeometriaComLoteamento(c.W, c.Lot, c.Quadras);
        var walk = parts.Where(x => x.Def is IntersectionDefinition && x.Piece.Color is MarkingColor.Concreto or MarkingColor.Grama && x.Piece.Thickness > 0.05)
            .Select(x => x.Piece.Shape).ToList();
        foreach (var x in ch)
            Assert.True(PolygonOps.TotalArea(PolygonOps.Intersect(new[] { x.Triangulo }, walk)) > 0.97 * x.Triangulo.Area, $"chanfro em {x.Vertice} sem calçada");
        var node = Assert.Single(c.W.Nodes).Layout;
        var faults = Y34TipTests.FaultsAt(parts, node.Node, Math.Max(25, node.Radius + 6), true);
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
    }

    // ------------------------------------------------------------------ item 20 – piso tátil da esquina

    /// <summary>
    /// Via local (modelo 0) com piso tátil e a calçada da imagem de referência: meio-fio 15 + mobiliário 70 + faixa livre 175
    /// (percurso 145 + faixa tátil 30) + acesso 40 = 300 cm. <paramref name="servico"/>: faixa de serviço com o meio-fio.
    /// </summary>
    private static RoadSetup Secao(double largura = 3.0, bool acesso = false, double servico = 0.85)
    {
        var s = RoadTemplates.All[0].Create();
        foreach (var e in s.Left.Concat(s.Right).Where(e => e.Tipo == TipoElementoSecao.Calcada))
        {
            e.Largura = largura;
            e.LarguraMeioFio = 0.15;
            e.FaixaServico = servico;
            e.FaixaAcesso = 0.40;
        }
        s.PisoTatil = true;
        s.PisoTatilJuntoAoAcesso = acesso;
        return s;
    }

    /// <summary>
    /// Calçada de 3,60 m (meio-fio 15, serviço 55, faixa livre 250, acesso 40): a faixa direcional (eixo a 1,95 m da face do
    /// meio-fio) passa além do topo das rampas (1,80 m) e os ramais ligam cada rampa (com a faixa antes do topo da rampa o ramal
    /// não liga – K29, de antes desta etapa).
    /// </summary>
    private static RoadSetup SecaoCruz() => Secao(3.6, servico: 0.70);

    /// <summary>Cruz de duas vias locais com piso tátil (<see cref="SecaoCruz"/>); <paramref name="no"/> ajusta a interseção.</summary>
    private static Y34TipTests.World Cruz(Action<IntersectionDefinition>? no = null)
    {
        var w = new Y34TipTests.World();
        foreach (var pts in new[] { new[] { new Vec2(-90, 0), new Vec2(90, 0) }, new[] { new Vec2(0, -90), new Vec2(0, 90) } })
        {
            var s = SecaoCruz();
            var pr = PathReference.FromPoints(pts, 0);
            var axis = new Polyline2(pr.Points);
            var g = s.Build(pr, new OutputSettings(), Cat, axis: axis);
            foreach (var m in g)
                if (m.Path is { } p && p.Points.Count == pr.Points.Count && p.Points[0].DistanceTo(pr.Points[0]) < 1e-9) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        foreach (var (node, ids) in IntersectionGenerator.FindNodes(w.Roads))
        {
            var rs = ids.Select(i => w.Roads[i]).ToList();
            var d = new IntersectionDefinition { Control = ControleIntersecao.Pare, Node = node };
            no?.Invoke(d);
            w.Nodes.Add(IntersectionDemo.Create(d, rs, ids.Select(i => w.Groups[i]).ToList(), w.Paths, w.Defs, Cat));
        }
        return w;
    }

    /// <summary>Quina do lote no quadrante (sx, sy) da cruz: o encontro dos alinhamentos prediais.</summary>
    private static Vec2 QuinaDoLote(Y34TipTests.World w, int sx, int sy)
    {
        var (h, v) = (w.Roads[0].Def, w.Roads[1].Def);   // h ao longo de +x (esquerda = +y); v ao longo de +y (esquerda = −x)
        return new Vec2(sx * (sx < 0 ? v.TotalLeft : v.TotalRight), sy * (sy > 0 ? h.TotalLeft : h.TotalRight));
    }

    /// <summary>Distância do alinhamento à faixa direcional (largura da calçada menos o afastamento da faixa), lados alto e baixo.</summary>
    private static (double Dx, double Dy) DistanciaDoLote(int sx, int sy)
    {
        var s = SecaoCruz();
        var ly = s.Lado(sy > 0);
        var lx = s.Lado(sx < 0);
        return (lx.SidewalkWidth - lx.TactileCenter(), ly.SidewalkWidth - ly.TactileCenter());
    }

    /// <summary>Faixa tátil da interseção na esquina do quadrante (sx, sy) e os vértices com mudança de direção de 30° ou mais.</summary>
    private static (List<Vec2> Pts, List<(Vec2 P, double Turn)> Turns) Esquina(Y34TipTests.World w, int sx, int sy)
    {
        var it = w.Nodes.Single().Layout.Definition!;
        var route = w.Defs.OfType<TactileRouteDefinition>().Where(t => t.GroupId == it.Id && !t.AlertOnly)
            .Single(t => t.PathRef.Points.Average(p => p.X) * sx > 0 && t.PathRef.Points.Average(p => p.Y) * sy > 0);
        var pts = route.PathRef.Points.ToList();
        var turns = new List<(Vec2, double)>();
        for (int i = 1; i + 1 < pts.Count; i++)
        {
            var turn = AKShape.Turn(pts[i - 1], pts[i], pts[i + 1]);
            if (turn >= TactileGenerator.SharpTurnDeg) turns.Add((pts[i], turn));
        }
        return (pts, turns);
    }

    private static readonly (int X, int Y)[] Quadrantes = { (1, 1), (-1, 1), (-1, -1), (1, -1) };

    [Fact]
    public void Item20_EsquinaParalelaAoAlinhamento_QuinaComAlerta_RedeLigadaAsRampas()
    {
        // Raio de 3 m: a quina da faixa (a 1,65 m dos dois alinhamentos) fica a mais de 0,60 m lisos da pista.
        var w = Cruz(d => d.CornerRadius = 3);
        foreach (var (sx, sy) in Quadrantes)
        {
            var (pts, turns) = Esquina(w, sx, sy);
            // Uma mudança de direção só, na quina: as retas a (largura da calçada − afastamento) dos dois alinhamentos.
            var (t, turn) = Assert.Single(turns);
            Assert.InRange(turn, 89, 91);
            var (dx, dy) = DistanciaDoLote(sx, sy);
            var v = QuinaDoLote(w, sx, sy);
            Assert.True(t.DistanceTo(new Vec2(v.X - sx * dx, v.Y - sy * dy)) < 0.02, $"quina da faixa em {t}");
            // Os trechos junto à quina são paralelos aos alinhamentos (na mesma distância do lote).
            Assert.Contains(pts, p => Math.Abs(p.Y - (v.Y - sy * dy)) < 0.01 && Math.Abs(p.X) > Math.Abs(t.X) + 0.4);
            Assert.Contains(pts, p => Math.Abs(p.X - (v.X - sx * dx)) < 0.01 && Math.Abs(p.Y) > Math.Abs(t.Y) + 0.4);
        }
        // Alerta em cada mudança de direção e em cada junção, todas as rampas ligadas, nada sobre rampa, meio-fio, grama ou mobiliário.
        var L = w.Nodes.Single().Layout;
        var faults = AC3TactileNetworkTests.Check(w, L.Node, 30, 4);
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
        Assert.Empty(Y34TipTests.Faults(w));
    }

    [Fact]
    public void Item20_ComOLoteChanfrado_AFaixaSegueOChanfro_DuasMudancasDeDirecao()
    {
        const double c = 1.5;
        var w0 = Cruz();
        // Os chanfros nas quatro quinas (catetos de 1,50 m), como as quadras novas: a interseção é refeita com eles.
        var tris = Quadrantes.Select(q =>
        {
            var v = QuinaDoLote(w0, q.X, q.Y);
            return new Polygon2(new[] { v + new Vec2(q.X * c, 0), v, v + new Vec2(0, q.Y * c) });
        }).ToList();
        var w = Cruz(d => d.ChanfrosLote = tris);
        foreach (var (sx, sy) in Quadrantes)
        {
            var (pts, turns) = Esquina(w, sx, sy);
            Assert.Equal(2, turns.Count);
            Assert.All(turns, x => Assert.InRange(x.Turn, 44, 46));
            // O trecho entre as duas é paralelo à face do chanfro, na distância média dos dois lados.
            var v = QuinaDoLote(w, sx, sy);
            var (a, b) = (v + new Vec2(sx * c, 0), v + new Vec2(0, sy * c));
            var face = (b - a).Normalized();
            var seg = (turns[1].P - turns[0].P).Normalized();
            Assert.True(Math.Abs(seg.Cross(face)) < 1e-3, "trecho do chanfro fora de paralelo");
            var (dx, dy) = DistanciaDoLote(sx, sy);
            foreach (var (p, _) in turns) Assert.Equal((dx + dy) / 2, Math.Abs((p - a).Cross(face)), 3);
        }
        var L = w.Nodes.Single().Layout;
        var faults = AC3TactileNetworkTests.Check(w, L.Node, 30, 4);
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
        Assert.Empty(Y34TipTests.Faults(w));
    }

    [Theory]
    [InlineData(6.0, false)]
    [InlineData(10.0, true)]
    public void Item20_QuinaPertoDaPista_CortaAQuinaComTrechoReto(double raio, bool estreita)
    {
        // Raio de 6 m: entre a quina do lote e o meio-fio sobram 2,60 m na bissetriz e a quina da faixa ficaria a 0,27 m da pista;
        // a faixa corta a quina com os 0,60 m lisos do lado da pista (além do meio-fio). Raio de 10 m: sobram 0,95 m, menos que as
        // duas folgas – o corte fica com as folgas equilibradas, sem tocar o meio-fio nem o lote.
        var w = Cruz(d => d.CornerRadius = raio);
        var L = w.Nodes.Single().Layout;
        foreach (var (sx, sy) in Quadrantes)
        {
            var (pts, turns) = Esquina(w, sx, sy);
            Assert.Equal(2, turns.Count);
            Assert.All(turns, x => Assert.InRange(x.Turn, 44, 46));
            var v = QuinaDoLote(w, sx, sy);
            var mid = (turns[0].P + turns[1].P) / 2;
            var aoLote = mid.DistanceTo(v);
            var aPista = L.Pavement.Min(g => g.DistanceTo(mid));
            var nasPontas = turns.Min(x => L.Pavement.Min(g => g.DistanceTo(x.P)));
            if (estreita) Assert.True(nasPontas > L.CurbWidth + 0.125 && aoLote > 0.125 + 0.05, $"corte a {nasPontas:0.00} m da pista e {aoLote:0.00} m do lote");
            else Assert.True(Math.Abs(nasPontas - (L.CurbWidth + 0.60 + 0.125)) < 0.03, $"pontas do corte a {nasPontas:0.00} m da pista");
            Assert.True(aPista >= nasPontas - 1e-6);
            // Toda a faixa na calçada: nada sobre a pista nem dentro do lote.
            Assert.DoesNotContain(pts, p => L.Pavement.Any(g => g.Contains(p)));
            Assert.DoesNotContain(pts, p => p.X * sx > v.X * sx + 0.01 && p.Y * sy > v.Y * sy + 0.01);
        }
        var faults = AC3TactileNetworkTests.Check(w, L.Node, 30, 4);
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
        Assert.Empty(Y34TipTests.Faults(w));
    }

    [Fact]
    public void Item20_OpcaoPeloMeioFio_ComoAte220()
    {
        var w = Cruz(d => { d.TatilEsquinaPeloMeioFio = true; d.CornerRadius = 3; });
        foreach (var (sx, sy) in Quadrantes)
            Assert.Empty(Esquina(w, sx, sy).Turns);   // acompanha a curva: nenhuma mudança de direção de 30° ou mais
        var L = w.Nodes.Single().Layout;
        var faults = AC3TactileNetworkTests.Check(w, L.Node, 30, 4);
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
    }

    [Fact]
    public void Item20_FaixaJuntoAoAcesso_ComoAImagem_EPelaNormaQuandoNaoCabe()
    {
        static RoadSetup Via(double largura, bool acesso) => Secao(largura, acesso);
        var lado = Via(3.0, true).Lado(false);
        Assert.Equal(1.75, lado.Free, 6);
        // Encostada na faixa de acesso: o eixo a 0,40 + 0,125 m do lote.
        Assert.Equal(3.0 - 0.40 - 0.125, lado.TactileCenter(), 6);
        Assert.Equal(lado.FreeCenter, Via(3.0, false).Lado(false).TactileCenter(), 6);
        // A faixa da via sai nessa posição.
        var s0 = Via(3.0, true);
        var pr = PathReference.FromPoints(new[] { new Vec2(-50, 0), new Vec2(50, 0) }, 0);
        var defs = s0.Build(pr, new OutputSettings(), Cat, axis: new Polyline2(pr.Points));
        var main = defs.OfType<TactileRouteDefinition>().Single(t => t.PathRef.Points.All(p => p.Y < 0));
        Assert.All(main.PathRef.Points, p => Assert.Equal(lado.Face + lado.TactileCenter(), -p.Y, 3));
        // Faixa livre de 1,40 m: sobrariam 1,15 m – fica a posição da norma (7.8.2 b).
        var estreita = Via(2.65, true).Lado(false);
        Assert.Equal(1.40, estreita.Free, 6);
        Assert.Equal((estreita with { TactileAtAccess = false }).TactileCenter(), estreita.TactileCenter(), 9);
    }

    // ------------------------------------------------------------------ item 12 – esquina e extensão de calçada (imagem 2)

    /// <summary>Cada rampa das travessias com o eixo no eixo da faixa de pedestres dela (paralelo e a menos de 5 cm).</summary>
    private static void RampasAlinhadas(Y34TipTests.World w)
    {
        var it = w.Nodes.Single().Layout.Definition!;
        var ramps = w.Defs.OfType<RampDefinition>().Where(r => r.GroupId == it.Id).ToList();
        var faixas = w.Defs.OfType<LinearMarkingDefinition>().Where(l => l.GroupId == it.Id && l.Code.StartsWith("FTP")).Select(l => new Polyline2(l.PathRef.Points)).ToList();
        Assert.NotEmpty(ramps);
        Assert.NotEmpty(faixas);
        foreach (var r in ramps)
        {
            var f = RampGenerator.FrameOf(new Polyline2(r.PathRef.Points));
            Assert.True(faixas.Any(fx =>
            {
                var u = (fx.Points[^1] - fx.Points[0]).Normalized();
                return Math.Abs(u.Cross(f.Up)) < 0.02 && Math.Abs((f.Curb - fx.Points[0]).Cross(u)) < 0.05;
            }), $"rampa em {f.Curb} fora do eixo da faixa");
        }
    }

    [Fact]
    public void Item12_RampasAlinhadasAsTravessias_ComESemExtensao()
    {
        RampasAlinhadas(AKShape.Make("cruz", "local2", "local2", 90, AB2Matrix.Template()));
        RampasAlinhadas(AKShape.Make("cruz", "local2", "local2", 90));
        var t = AKShape.Template();
        t.CurbExtensionCornerChamfer = 2.0;
        RampasAlinhadas(AKShape.Make("cruz", "local2", "local2", 90, t));
    }

    [Theory]
    [InlineData(90.0, 2.0)]
    [InlineData(90.0, 1.0)]
    [InlineData(70.0, 1.5)]
    public void Item12_EsquinaDaExtensaoEmChanfro_FaceRetaComORecuoPedido(double ang, double c)
    {
        var t = AKShape.Template();
        t.CurbExtensionCornerChamfer = c;
        var w = AKShape.Make("cruz", "local2", "local2", ang, t);
        var L = w.Nodes[0].Layout;
        var ears = L.Ears.Where(e => !e.Straight).ToList();
        Assert.Equal(4, ears.Count);
        foreach (var e in ears)
        {
            var cf = L.CornerFillets[(e.LegA.Road, e.LegA.Sign)];
            var face = AKShape.Face(e);
            // A face reta do chanfro: 2 × recuo × sen(ângulo/2), com mudança de direção de (180° − ângulo)/2 nas duas pontas.
            var len = 2 * c * Math.Sin(cf.Span / 2 * Math.PI / 180);
            var giro = (180 - cf.Span) / 2;
            var ok = Enumerable.Range(1, face.Count - 3).Any(i => Math.Abs(face[i].DistanceTo(face[i + 1]) - len) < 0.02
                && Math.Abs(AKShape.Turn(face[i - 1], face[i], face[i + 1]) - giro) < 1.5 && Math.Abs(AKShape.Turn(face[i], face[i + 1], face[i + 2]) - giro) < 1.5);
            Assert.True(ok, $"esquina {e.LegA.Road}/{e.LegA.Sign} ({cf.Span:0}°) sem a face reta de {len:0.00} m");
            // Sem centro de curva novo (só o da esquina antiga): o Revit não faz arco no chanfro.
            Assert.Single(e.Template.ArcCenters!);
        }
        Assert.Empty(AB2Check.Run(w).Faults);
    }

    [Fact]
    public void Item12_ChanfroPorEsquina_ZeroVoltaACurva_ERecuoGrandeEReduzido()
    {
        var t = AKShape.Template();
        t.CurbExtensionCornerChamfer = 2.0;
        // Ajuste 0 em todas as esquinas: curva com o raio, mesmo com o geral em chanfro.
        var w = AKShape.Make("cruz", "local2", "local2", 90, t, (d, rs) =>
        {
            foreach (var r in rs)
                foreach (var sign in new[] { 1, -1 })
                    d.LegSettings.Add(new IntersectionLegSettings { RoadId = r.Def.Id, Sign = sign, CurbExtensionCornerChamfer = 0, CurbExtensionCornerRadius = 4 });
        });
        var L = w.Nodes[0].Layout;
        foreach (var e in L.Ears.Where(e => !e.Straight))
        {
            Assert.InRange(AKShape.CornerRadiusOf(L, e), 3.97, 4.03);
            Assert.Equal(2, e.Template.ArcCenters!.Count);
        }
        // Recuo grande demais: o chanfro entraria na calçada – é reduzido, com aviso.
        t.CurbExtensionCornerChamfer = 30;
        var w2 = AKShape.Make("cruz", "local2", "local2", 90, t);
        Assert.Contains(w2.Nodes[0].Layout.Warnings, x => x.Contains("recuo do chanfro da esquina da extensão de calçada reduzido"));
        Assert.Empty(AB2Check.Run(w2).Faults);
    }

    // ------------------------------------------------------------------ ligação no plugin

    private static string Raiz([CallerFilePath] string aqui = "")
    {
        foreach (var inicio in new[] { Path.GetDirectoryName(aqui), AppContext.BaseDirectory })
        {
            var dir = string.IsNullOrEmpty(inicio) ? null : new DirectoryInfo(inicio);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "SinalizacaoViaria.Revit"))) dir = dir.Parent;
            if (dir != null) return dir.FullName;
        }
        throw new DirectoryNotFoundException("raiz do repositório");
    }

    private static string Fonte(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz(), "src", "SinalizacaoViaria.Revit" }.Concat(partes).ToArray()));

    [Fact]
    public void Plugin_JanelasEComandos()
    {
        // Item 19: quadras novas com o padrão e as calçadas das interseções refeitas; janela da quadra e do loteamento.
        var cmd = Fonte("Commands", "LoteamentoCommands.cs");
        Assert.Equal(2, cmd.Split("Reconhecimento.ComChanfroPadrao(").Length - 1);
        Assert.Equal(4, cmd.Split("RefazerInterseccoes(").Length - 1);   // a definição + quadra + loteamento novo + reconhecer
        var forms = Fonte("UI", "LotForms.cs");
        Assert.Contains("Em cada lado, sobre os limites do lote (catetos)", forms);
        Assert.Contains("lot.ChanfroQuadrasNovas", forms);
        Assert.Contains("Reconhecimento.ComChanfroPadrao(", forms);
        Assert.Contains("it.ChanfrosLote = Core.Parcelamento.LotDivider.Triangulos(all, node);", Fonte("Infrastructure", "IntersectionService.cs"));
        // Item 20: opção da esquina na interseção e posição da faixa na via.
        Assert.Contains("d.TatilEsquinaPeloMeioFio = v", Fonte("Commands", "IntersectionCommands.cs"));
        Assert.Contains("x:Name=\"CkTactileAccess\"", Fonte("UI", "RoadWindow.xaml"));
        Assert.Contains("s.PisoTatilJuntoAoAcesso = CkTactileAccess.IsChecked == true;", Fonte("UI", "RoadWindow.xaml.cs"));
        // Item 12: chanfro da esquina da extensão na janela da interseção, nas extensões e por esquina.
        Assert.Equal(2, Fonte("Commands", "IntersectionCommands.cs").Split("CurbExtensionCornerChamfer = v").Length - 1);
        Assert.Equal(2, Fonte("Commands", "SidewalkCommands.cs").Split("CurbExtensionCornerChamfer = v").Length - 1);
    }
}
