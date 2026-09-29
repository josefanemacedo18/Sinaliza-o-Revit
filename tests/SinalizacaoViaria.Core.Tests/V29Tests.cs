using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada V: regras de conversão nas interseções (MBST Vol. IV / CTB art. 207).</summary>
public class V29Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static (IntersectionDemo.Scene S, IntersectionDefinition It) Scene(IntersectionDefinition d, int main, bool tee)
    {
        var s = IntersectionDemo.Create(d, Cat, main, 0, 90, tee);
        return (s, s.Definitions.OfType<IntersectionDefinition>().First());
    }

    private static List<LinearMarkingDefinition> Kids(IntersectionDemo.Scene s, IntersectionDefinition it) =>
        s.Definitions.Where(x => x.GroupId == it.Id).OfType<LinearMarkingDefinition>().ToList();

    private static LinearMarkingDefinition Center(IntersectionDemo.Scene s, int road) =>
        s.Definitions.OfType<LinearMarkingDefinition>().First(m => m.GroupId == s.Layout.Roads[road].Def.GroupId && m.Code.StartsWith("LFO") && Math.Abs(m.Offset) < 0.01);

    private static bool CutAt(MarkingDefinition m, Vec2 p) => m.Exclusions.Any(e => new Polygon2(e.Points).Contains(p));

    [Fact]
    public void Cruzamento_com_preferencial_tem_eixo_tracejado_LCO_no_miolo()
    {
        var (s, it) = Scene(new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = false }, 1, false);
        Assert.True(s.Layout.ThroughMain);
        var node = s.Layout.Node;
        // O eixo contínuo da principal NÃO atravessa o cruzamento (proibiria as conversões – CTB art. 207).
        Assert.True(CutAt(Center(s, s.Layout.Main), node));
        var kids = Kids(s, it);
        var lco = kids.Where(k => k.Code == "LCO").ToList();
        Assert.Contains(lco, k => k.ColorOverride == MarkingColor.Amarela);
        Assert.Contains(lco, k => k.ColorOverride == null);              // divisórias das faixas (brancas)
        // A LCO amarela passa pelo nó e cobre a boca inteira (entre as esquinas).
        var y = lco.First(k => k.ColorOverride == MarkingColor.Amarela);
        var line = new Polyline2(y.PathRef.Points);
        Assert.True(line.Project(node).Signed is var sg && Math.Abs(sg) < 0.05);
        Assert.True(line.Length > 15);
        // Geometria: tracejada (vários traços de 1 m) e amarela.
        var geo = MarkingBuilder.Build(y, new Polyline2(y.PathRef.Points), new BuildContext { Catalog = Cat });
        var dashes = geo.Pieces.Count(p => p.Color == MarkingColor.Amarela);
        Assert.True(dashes >= 8, $"traços: {dashes}");
    }

    [Fact]
    public void Aproximacao_com_eixo_seccionado_vira_linha_dupla_continua_e_boca_dupla_tracejada()
    {
        // Via local (eixo LFO-2) como principal num T: igual à figura do MBST – LFO-3 na aproximação e LCO dupla na boca.
        var (s, it) = Scene(new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = false }, 0, true);
        Assert.Equal("LFO-2", Center(s, s.Layout.Main).Code);
        var kids = Kids(s, it);
        var yellow = kids.Where(k => k.Code == "LCO" && k.ColorOverride == MarkingColor.Amarela).ToList();
        Assert.Equal(2, yellow.Count);
        Assert.Contains(yellow, k => k.Offset > 0.05);
        Assert.Contains(yellow, k => k.Offset < -0.05);
        var lfo3 = kids.Where(k => k.Code == "LFO-3").ToList();
        Assert.Equal(3, lfo3.Count);                                     // 2 ramos da principal + a secundária
        Assert.All(lfo3, k => Assert.InRange(new Polyline2(k.PathRef.Points).Length, 14.9, 15.1));
        // O trecho seccionado original foi recortado onde a LFO-3 entra.
        var c = Center(s, s.Layout.Main);
        foreach (var k in lfo3.Where(k => new Polyline2(k.PathRef.Points).Project(s.Layout.Node).Signed is var d0 && Math.Abs(d0) < 30))
            Assert.True(CutAt(c, new Polyline2(k.PathRef.Points).PointAt(7.5)) || CutAt(Center(s, 1 - s.Layout.Main), new Polyline2(k.PathRef.Points).PointAt(7.5)));
    }

    [Fact]
    public void Conversao_a_esquerda_proibida_mantem_eixo_continuo_e_poe_R4a()
    {
        var (s, it) = Scene(new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = false, LeftTurns = false }, 1, true);
        Assert.False(CutAt(Center(s, s.Layout.Main), s.Layout.Node));
        Assert.DoesNotContain(Kids(s, it), k => k.Code == "LCO" && k.ColorOverride == MarkingColor.Amarela);
        var r4a = s.Definitions.Where(x => x.GroupId == it.Id).OfType<SignDefinition>().Where(p => p.Code == "R-4a").ToList();
        Assert.Equal(3, r4a.Count);                                      // as três aproximações do T
    }

    [Fact]
    public void Semaforo_tem_LMS1_nas_aproximacoes_so_do_lado_de_chegada()
    {
        var (s, it) = Scene(new IntersectionDefinition { Control = ControleIntersecao.Semaforo, Crosswalks = true }, 1, false);
        Assert.False(s.Layout.ThroughMain);
        var lms1 = Kids(s, it).Where(k => k.Code == "LMS-1").ToList();
        Assert.Equal(2, lms1.Count);                                     // uma divisória por aproximação da coletora
        foreach (var k in lms1)
        {
            var line = new Polyline2(k.PathRef.Points);
            Assert.InRange(line.Length, 19.9, 20.1);
            // Do lado de chegada: o tráfego que anda pela linha vai em direção ao nó pela direita dela (mão direita).
            var far = line.Points[0].DistanceTo(s.Layout.Node) > line.Points[^1].DistanceTo(s.Layout.Node) ? line.Points[0] : line.Points[^1];
            var near = far == line.Points[0] ? line.Points[^1] : line.Points[0];
            var toNode = (near - far).Normalized();
            var axis = s.Layout.Roads[s.Layout.Main].Axis;
            var pr = axis.Project(far);
            var right = -axis.TangentAt(pr.Station).PerpLeft * Math.Sign(toNode.Dot(axis.TangentAt(pr.Station)));
            // A linha fica à direita do eixo para quem segue em direção ao nó.
            Assert.True((far - axis.PointAt(pr.Station)).Dot(right) > 0);
        }
    }

    [Fact]
    public void Linhas_de_aproximacao_param_na_metade_da_quadra()
    {
        var d = new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = false };
        var s0 = IntersectionDemo.Create(d, Cat, 0, 0, 90, true);
        var it0 = s0.Definitions.OfType<IntersectionDefinition>().First();
        var mainAxis = s0.Layout.Roads[s0.Layout.Main].Axis;
        var sn = mainAxis.Project(s0.Layout.Node).Station;
        // Outro cruzamento 34 m adiante na principal: a LFO-3 daquele lado vai só até ~16 m do nó.
        var d2 = new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = false, NeighborNodes = { mainAxis.PointAt(sn + 34) } };
        var s = IntersectionDemo.Create(d2, Cat, 0, 0, 90, true);
        var it = s.Definitions.OfType<IntersectionDefinition>().First();
        var lfo3 = Kids(s, it).Where(k => k.Code == "LFO-3").Select(k => new Polyline2(k.PathRef.Points)).ToList();
        Assert.True(lfo3.Min(l => l.Length) < 10, string.Join(", ", lfo3.Select(l => l.Length.ToString("0.0"))));
        Assert.All(lfo3, l => Assert.True(l.Points.Max(p => mainAxis.Project(p).Station) <= sn + 16.01 || Math.Abs(l.Project(s.Layout.Node).Signed) > 3));
        Assert.True(Kids(s0, it0).Count(k => k.Code == "LFO-3") == 3);
    }

    // ------------------------------------------------------------------ rotatórias

    private static (RoundaboutDefinition D, RoundaboutLayout L, List<MarkingDefinition> Kids) Roundabout(int tpl, TipoRotatoria type)
    {
        var roads = new List<IntersectionRoad>();
        foreach (var (a, b) in new[] { (new Vec2(-120, 0), new Vec2(120, 0)), (new Vec2(0, -120), new Vec2(0, 120)) })
        {
            var pr = PathReference.FromPoints(new[] { a, b }, 0);
            var g = RoadTemplates.All[tpl].Create().Build(pr, new OutputSettings(), Cat);
            roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), new Polyline2(pr.Points)));
        }
        var d = new RoundaboutDefinition { Center = Vec2.Zero };
        d.ApplyPreset(type);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, roads, d.OuterRadius + 25);
        var L = RoundaboutGenerator.Layout(d);
        return (d, L, RoundaboutGenerator.Children(d, L, new OutputSettings(), 0));
    }

    private static double ToBoundary(IEnumerable<Polygon2> polys, Vec2 p) =>
        polys.SelectMany(x => new[] { x.Outer }.Concat(x.Holes)).Min(r => Enumerable.Range(0, r.Count).Min(i => SegDist(p, r[i], r[(i + 1) % r.Count])));

    private static double SegDist(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var t = ab.Length < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / ab.Dot(ab), 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    [Theory]
    [InlineData(0, TipoRotatoria.ComBypass)]
    [InlineData(1, TipoRotatoria.UmaFaixa)]
    [InlineData(2, TipoRotatoria.DuasFaixas)]
    public void Rotatoria_linha_de_bordo_acompanha_o_meio_fio_real(int tpl, TipoRotatoria type)
    {
        var (_, L, kids) = Roundabout(tpl, type);
        var lbo = kids.OfType<LinearMarkingDefinition>().Where(k => k.Code == "LBO" && !k.PathRef.Closed).ToList();
        Assert.NotEmpty(lbo);
        // Nenhum trecho "solto" no asfalto: todos os pontos a menos de 0,4 m de um meio-fio (borda da pista).
        foreach (var l in lbo)
            foreach (var p in l.PathRef.Points)
                Assert.True(ToBoundary(L.Pavement, p) < 0.4, $"LBO a {ToBoundary(L.Pavement, p):0.00} m do meio-fio em {p}");
    }

    [Fact]
    public void Rotatoria_com_bypass_tem_travessia_de_calcada_a_calcada_e_rampas_na_calcada_externa()
    {
        var (d, L, kids) = Roundabout(0, TipoRotatoria.ComBypass);
        Assert.NotEmpty(L.Refuges);
        var ftp = kids.OfType<LinearMarkingDefinition>().Where(k => k.Code == "FTP-1").ToList();
        Assert.Equal(4, ftp.Count);
        foreach (var g in L.Legs)
        {
            Assert.NotNull(g.CrosswalkSpan);
            var (lo, hi) = g.CrosswalkSpan!.Value;
            // Atravessa os by-pass: bem mais larga que a pista do ramo.
            Assert.True(hi - lo > g.Leg.Width + 6, $"travessia de {hi - lo:0.0} m num ramo de {g.Leg.Width:0.0} m");
        }
        var ramps = kids.OfType<RampDefinition>().ToList();
        Assert.Equal(8, ramps.Count);
        // Nenhuma rampa sobre ilha: todas na calçada externa (fora da pista e das ilhas).
        foreach (var r in ramps)
        {
            var tip = r.PathRef.Points[1];
            Assert.DoesNotContain(L.Refuges, isl => isl.Contains(tip));
            Assert.DoesNotContain(L.Pavement, pv => pv.Contains(tip));
        }
    }

    [Fact]
    public void Rotatoria_ilha_separadora_tem_refugio_no_nivel_da_pista_e_nariz_sem_zebrado_branco()
    {
        var (d, L, kids) = Roundabout(1, TipoRotatoria.UmaFaixa);
        foreach (var g in L.Legs.Where(g => g.Splitter != null))
        {
            var mid = g.At(d.Center, g.CrosswalkT, 0);
            Assert.DoesNotContain(L.SplitterCore, p => p.Contains(mid));
            Assert.DoesNotContain(L.SplitterCurb, p => p.Contains(mid));
            Assert.Contains(L.Pavement, p => p.Contains(mid));
        }
        Assert.DoesNotContain(kids.OfType<HatchMarkingDefinition>(), h => h.Code == "ZPA" && h.BarColor != MarkingColor.Amarela);
    }

    // ------------------------------------------------------------------ baias e recuos

    private static (List<MarkingDefinition> Defs, RecuoVia R) Bay(int tpl, TipoRecuo tipo, bool left)
    {
        var setup = RoadTemplates.All[tpl].Create();
        var r = RecuoVia.Padrao(tipo, 50);
        r.Estaca = 30;
        r.LadoEsquerdo = left;
        setup.Recuos.Add(r);
        var pr = PathReference.FromPoints(new[] { new Vec2(0, 0), new Vec2(140, 0) }, 0);
        return (setup.Build(pr, new OutputSettings(), Cat), r);
    }

    private static MarkingGeometry Geo(MarkingDefinition m) => MarkingBuilder.Build(m, m.Path == null ? null : new Polyline2(m.Path.Points, m.Path.Closed), new BuildContext { Catalog = Cat });

    [Theory]
    [InlineData(2, TipoRecuo.BaiaOnibus, true)]
    [InlineData(2, TipoRecuo.BaiaOnibus, false)]
    [InlineData(0, TipoRecuo.FaixaDesaceleracao, false)]
    [InlineData(0, TipoRecuo.RecuoEmbarque, true)]
    public void Recuo_interrompe_as_vagas_inteiras(int tpl, TipoRecuo tipo, bool left)
    {
        var (defs, r) = Bay(tpl, tipo, left);
        var side = defs.OfType<ParkingMarkingDefinition>().Where(p => p.Breaks.Count > 0).ToList();
        Assert.Single(side);
        var geo = Geo(side[0]);
        Assert.NotEmpty(geo.Pieces);
        // Nenhuma peça de vaga entre o início da transição de entrada e o fim da de saída.
        foreach (var p in geo.Pieces)
        {
            var (mn, mx) = p.Shape.Bounds;
            Assert.False(mx.X > r.Estaca + 0.3 && mn.X < r.S3 - 0.3, $"vaga em {mn.X:0.0}..{mx.X:0.0} dentro do recuo {r.Estaca}..{r.S3}");
        }
        // As vagas do outro lado continuam inteiras.
        var other = defs.OfType<ParkingMarkingDefinition>().Where(p => p.Breaks.Count == 0).ToList();
        foreach (var o in other) Assert.Contains(Geo(o).Pieces, p => p.Shape.Bounds.Max.X > r.Estaca + 1 && p.Shape.Bounds.Min.X < r.S3 - 1);
    }

    [Theory]
    [InlineData(2, TipoRecuo.BaiaOnibus, true)]
    [InlineData(1, TipoRecuo.FaixaDesaceleracao, false)]
    public void Recuo_sem_fresta_entre_pista_e_sarjeta_nas_transicoes(int tpl, TipoRecuo tipo, bool left)
    {
        var (defs, r) = Bay(tpl, tipo, left);
        var pav = Geo(defs.OfType<RoadPavementDefinition>().First());
        var sg = left ? 1 : -1;
        var gutter = defs.OfType<LinearMarkingDefinition>().FirstOrDefault(m => m.Code == "SARJETA" && Math.Sign(m.Offset) == sg);
        var curb = defs.OfType<LinearMarkingDefinition>().First(m => m.Code == "MEIO-FIO" && Math.Sign(m.Offset) == sg && Math.Abs(m.Offset) > 3);
        var edgeGeo = Geo(gutter ?? curb);
        double Cut(MarkingGeometry g, double x, bool outer)
        {
            var ys = new List<double>();
            foreach (var p in g.Pieces)
            {
                var ring = p.Shape.Outer;
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Count];
                    if ((a.X - x) * (b.X - x) <= 0 && Math.Abs(a.X - b.X) > 1e-9) ys.Add(a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X));
                }
            }
            return outer ? (sg > 0 ? ys.Max() : ys.Min()) : (sg > 0 ? ys.Min() : ys.Max());
        }
        foreach (var x in new[] { r.Estaca + (r.S1 - r.Estaca) * 0.5, r.S2 + Math.Max(0.1, (r.S3 - r.S2) * 0.5) })
        {
            var pe = Cut(pav, x, true);
            var ge = Cut(edgeGeo, x, false);
            Assert.True(Math.Abs(pe - ge) < 0.02, $"estaca {x:0.0}: pista {pe:0.000} × sarjeta/meio-fio {ge:0.000}");
        }
    }
}
