using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada Y: rampas das travessias definidas na interseção, iguais, alinhadas e sem falhas de modelagem.</summary>
public class Y33RampTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    // Modelos: 0 = local (calçada 2,50 m), 1 = coletora (calçada 3,00 m), 2 = avenida com canteiro (calçada 3,50 m).
    private sealed record Case(IntersectionDemo.Scene S, IntersectionDefinition It, List<RampDefinition> Ramps)
    {
        public IntersectionLayout L => S.Layout;
    }

    private static Case Make(IntersectionDefinition d, int main = 1, int minor = 1, double angle = 90, bool tee = false)
    {
        var s = IntersectionDemo.Create(d, Cat, main, minor, angle, tee);
        var it = s.Definitions.OfType<IntersectionDefinition>().First();
        var ramps = s.Definitions.OfType<RampDefinition>().Where(r => r.GroupId == it.Id).ToList();
        return new Case(s, it, ramps);
    }

    private static Polyline2 PathOf(RampDefinition r) => new(r.PathRef.Points);

    private static List<Polygon2> RampSolids(RampDefinition r, Polyline2? path = null) =>
        PolygonOps.Union(RampGenerator.Generate(r, path ?? PathOf(r)).Pieces.Select(p => p.Shape));

    private static double Area(IEnumerable<Polygon2> a, IEnumerable<Polygon2> b) => PolygonOps.TotalArea(PolygonOps.Intersect(a, b));

    /// <summary>Geometria de tudo menos as rampas (calçadas, grama, meios-fios da via e da interseção).</summary>
    private static MarkingGeometry WithoutRamps(Case c) =>
        IntersectionDemo.Build(new IntersectionDemo.Scene(c.S.Definitions.Where(x => x is not RampDefinition).ToList(), c.S.Paths, c.L),
            new BuildContext { Catalog = Cat }, signs: false);

    [Fact]
    public void Rampas_saem_com_as_medidas_da_interseção_e_todas_iguais()
    {
        var c = Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, CrosswalkWidth = 4.0 });
        Assert.Equal(8, c.Ramps.Count);                                 // 4 travessias × 2 lados
        foreach (var r in c.Ramps)
        {
            Assert.Equal(TipoRampa.RebaixamentoComAbas, r.Type);
            Assert.Equal(4.0, r.Width, 3);                                // largura automática = largura da faixa
            Assert.Equal(0.0833, r.Slope, 4);
            Assert.Equal(0.10, r.FlareSlope, 4);
            Assert.True(r.Tactile);
            Assert.True(r.SquareCut);
        }
        Assert.Single(c.Ramps.Select(r => (r.Type, Math.Round(r.Width, 3), Math.Round(r.Height, 3), Math.Round(r.Slope, 4))).Distinct());

        var d = new IntersectionDefinition
        {
            Control = ControleIntersecao.Pare, RampWidth = 2.0, RampSlope = 0.07, RampFlareSlope = 0.08, RampTactile = false,
            RampDirectional = false, RampCurbHeight = 0.12,
        };
        var k = Make(d);
        Assert.Equal(8, k.Ramps.Count);
        foreach (var r in k.Ramps)
        {
            Assert.Equal(2.0, r.Width, 3);
            Assert.Equal(0.07, r.Slope, 4);
            Assert.Equal(0.08, r.FlareSlope, 4);
            Assert.Equal(0.12, r.Height, 3);
            Assert.False(r.Tactile);
        }
    }

    [Fact]
    public void Medidas_fora_da_norma_sao_limitadas()
    {
        var k = Make(new IntersectionDefinition { RampWidth = 1.0, RampSlope = 0.12, RampFlareSlope = 0.2 });
        Assert.All(k.Ramps, r =>
        {
            Assert.True(r.Width >= 1.50 - 1e-9);
            Assert.True(r.Slope <= 0.0833 + 1e-9);
            Assert.True(r.FlareSlope <= 0.10 + 1e-9);
            Assert.Empty(RampGenerator.Generate(r, PathOf(r)).Warnings);
        });
    }

    [Theory]
    [InlineData(1, 1, 90, false)]
    [InlineData(1, 1, 60, false)]
    [InlineData(2, 1, 90, true)]
    [InlineData(1, 0, 120, true)]
    public void Rampa_centrada_no_eixo_da_faixa_e_perpendicular_ao_meio_fio(int main, int minor, double angle, bool tee)
    {
        var c = Make(new IntersectionDefinition { Control = ControleIntersecao.Pare }, main, minor, angle, tee);
        Assert.NotEmpty(c.Ramps);
        var ftp = c.S.Definitions.OfType<LinearMarkingDefinition>().Where(m => m.GroupId == c.It.Id && m.Code.StartsWith("FTP")).ToList();
        foreach (var r in c.Ramps)
        {
            var f = RampGenerator.FrameOf(PathOf(r));
            // A faixa cujo eixo passa pelo início da rampa (face do meio-fio).
            // Distância à reta do eixo da faixa (a faixa termina 0,30 m antes do meio-fio).
            static double LineDist(Polyline2 l, Vec2 p) => Math.Abs((p - l.Points[0]).Cross((l.Points[^1] - l.Points[0]).Normalized()));
            var best = ftp.Select(m => new Polyline2(m.PathRef.Points)).Select(l => (L: l, D: LineDist(l, f.Curb)))
                .OrderBy(x => x.D).First();
            Assert.True(best.D < 0.01, $"rampa a {best.D:0.000} m do eixo da faixa");
            var dir = (best.L.Points[^1] - best.L.Points[0]).Normalized();
            Assert.True(Math.Abs(dir.Cross(f.Up)) < 0.01, "rampa fora do alinhamento da travessia");
        }
    }

    [Theory]
    [InlineData(1, 1, 90, false, 6.0)]
    [InlineData(1, 1, 60, false, 6.0)]
    [InlineData(1, 1, 120, true, 6.0)]
    [InlineData(2, 1, 90, true, 10.0)]
    [InlineData(1, 1, 90, false, 3.0)]
    [InlineData(0, 0, 90, false, 6.0)]
    public void Rampa_inteira_na_calcada_sem_invadir_pista_curva_ou_grama(int main, int minor, double angle, bool tee, double radius)
    {
        var c = Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, CornerRadius = radius }, main, minor, angle, tee);
        Assert.NotEmpty(c.Ramps);
        var others = WithoutRamps(c);
        var floors = others.Pieces.Where(p => p.Color is MarkingColor.Concreto or MarkingColor.Grama).Select(p => p.Shape).ToList();
        var grass = others.Pieces.Where(p => p.Color == MarkingColor.Grama).Select(p => p.Shape).ToList();
        var pav = c.L.Carriageway;
        foreach (var r in c.Ramps)
        {
            var solid = RampSolids(r);
            // Nunca sobre a pista (sarjeta, curva da esquina).
            Assert.True(Area(solid, pav) < 0.005, $"rampa {r.Type} sobre a pista: {Area(solid, pav):0.000} m²");
            // Calçada, grama e meio-fio recortados exatamente: nada por baixo da rampa.
            Assert.True(Area(solid, floors) < 0.01, $"piso sob a rampa: {Area(solid, floors):0.000} m² " + Dbg(r, others));
            // Sem sobras de grama junto à rampa (lascas pequenas).
            var near = PolygonOps.Offset(solid, 0.6, true);
            var slivers = PolygonOps.Intersect(grass, near).Where(p => p.Area > 1e-4 && p.Area < 0.10).ToList();
            Assert.True(slivers.Count == 0, $"sobras de grama junto à rampa: {string.Join(", ", slivers.Select(p => p.Area.ToString("0.000")))} m²");
        }
    }

    private static string Dbg(RampDefinition r, MarkingGeometry others)
    {
        var f = RampGenerator.FrameOf(PathOf(r));
        var solid = RampSolids(r);
        var parts = others.Pieces.Select(p => (p, I: PolygonOps.Intersect(new[] { p.Shape }, solid))).Where(x => PolygonOps.TotalArea(x.I) > 1e-4)
            .Select(x => { var c = x.I.OrderByDescending(q => q.Area).First().Centroid - f.Curb; return $"[{x.p.Color} {x.p.Layer} {PolygonOps.TotalArea(x.I):0.000} side={c.Dot(f.Side):0.00} up={c.Dot(f.Up):0.00}]"; });
        return r.Type + " w=" + r.Width + " " + string.Join(" ", parts);
    }

    [Fact]
    public void Rampas_nao_se_sobrepoem_e_ficam_fora_da_curva()
    {
        var c = Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, CornerRadius = 8 });
        var solids = c.Ramps.Select(r => RampSolids(r)).ToList();
        for (int i = 0; i < solids.Count; i++)
            for (int j = i + 1; j < solids.Count; j++)
                Assert.True(Area(solids[i], solids[j]) < 1e-4);
        foreach (var r in c.Ramps)
        {
            var f = RampGenerator.FrameOf(PathOf(r));
            var ext = IntersectionGenerator.RampHalfExtent(r);
            // As duas pontas da rampa (abas) ficam sobre o trecho reto do meio-fio: a face do meio-fio segue a linha reta.
            foreach (var s in new[] { -1.0, 1.0 })
            {
                var tip = f.P(s * ext, -0.02);
                Assert.True(c.L.Carriageway.Any(p => p.Contains(tip)), $"ponta da aba {s}: {tip} fora da pista");
                Assert.False(c.L.Carriageway.Any(p => p.Contains(f.P(s * ext, 0.02))), $"ponta da aba {s}: sobre a pista");
            }
        }
    }

    [Fact]
    public void Rampa_que_invadiria_a_curva_desloca_a_travessia_e_avisa()
    {
        // Largura = faixa (4 m) + abas de 1,50 m: 3,50 m do centro até a ponta da aba; com recuo de 1 m a faixa fica a 3 m do
        // fim da curva → a travessia é recuada.
        var c = Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, CrosswalkWidth = 4.0, CrosswalkSetback = 1.0 });
        Assert.Contains(c.L.Warnings, w => w.StartsWith("Rampas:") && w.Contains("recuada"));
        foreach (var leg in c.L.Legs)
            Assert.True(IntersectionGenerator.SetbackOf(c.It, c.L, leg) >= 1.6 - 1e-6);

        // Rampa de 1,50 m cabe no recuo pedido: sem ajuste e sem aviso.
        var k = Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, CrosswalkWidth = 4.0, CrosswalkSetback = 1.0, RampWidth = 1.5 });
        Assert.DoesNotContain(k.L.Warnings, w => w.StartsWith("Rampas:"));
        foreach (var leg in k.L.Legs)
            Assert.Equal(1.0, IntersectionGenerator.SetbackOf(k.It, k.L, leg), 6);
    }

    [Fact]
    public void Calcada_estreita_recebe_rebaixamento_total_com_aviso()
    {
        // Via local: calçada de 2,50 m – rampa de 1,80 m + faixa livre de 1,20 m não cabem.
        var c = Make(new IntersectionDefinition { Control = ControleIntersecao.Pare }, 0, 0);
        Assert.NotEmpty(c.Ramps);
        Assert.All(c.Ramps, r =>
        {
            Assert.Equal(TipoRampa.RebaixamentoTotal, r.Type);
            Assert.Equal(2.50, r.SidewalkDepth, 2);
            Assert.True(r.Slope <= 0.05 + 1e-9);                          // rampas laterais ≤ 5 %
        });
        Assert.Contains(c.L.Warnings, w => w.StartsWith("Rampas:") && w.Contains("rebaixamento total"));
        // Faixa livre pedida abaixo de 1,20 m não vale (NBR 9050): continua o rebaixamento total.
        var k = Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, RampFreeWidth = 0.5 }, 0, 0);
        Assert.All(k.Ramps, r => Assert.Equal(TipoRampa.RebaixamentoTotal, r.Type));
        // Calçada de 3,00 m: rampa de 1,80 m + 1,20 m livres cabem (com abas); pedindo 1,50 m livres, rebaixamento total.
        Assert.All(Make(new IntersectionDefinition()).Ramps, r => Assert.Equal(TipoRampa.RebaixamentoComAbas, r.Type));
        Assert.All(Make(new IntersectionDefinition { RampFreeWidth = 1.5 }).Ramps, r => Assert.Equal(TipoRampa.RebaixamentoTotal, r.Type));
    }

    [Fact]
    public void Sem_abas_e_rebaixamento_total_pedidos_valem_para_todas()
    {
        var a = Make(new IntersectionDefinition { RampType = TipoRampa.RebaixamentoSemAbas });
        Assert.All(a.Ramps, r => Assert.Equal(TipoRampa.RebaixamentoSemAbas, r.Type));
        Assert.All(a.Ramps, r => Assert.Equal(0, RampGenerator.Dimensions(r).Flare));
        var t = Make(new IntersectionDefinition { RampType = TipoRampa.RebaixamentoTotal });
        Assert.All(t.Ramps, r => Assert.Equal(TipoRampa.RebaixamentoTotal, r.Type));
    }

    [Fact]
    public void Recorte_retangular_preenche_os_cantos_das_abas()
    {
        var path = new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 1) });
        var r = new RampDefinition { Width = 2.0, Height = 0.15, SquareCut = true };
        var fp = RampGenerator.Footprint(r, path);
        var (w, len, flare) = RampGenerator.Dimensions(r);
        Assert.Equal((w + 2 * flare) * (len + 0.05), fp.Area, 3);
        var solid = RampSolids(r, path);
        // As peças cobrem o retângulo inteiro (sem o avanço de 5 cm sobre a sarjeta) – nada fica sem piso.
        Assert.Equal((w + 2 * flare) * len, PolygonOps.TotalArea(solid), 2);
        // Sem o recorte retangular, a rampa avulsa continua com o recorte em trapézio.
        var plain = new RampDefinition { Width = 2.0, Height = 0.15 };
        Assert.True(RampGenerator.Footprint(plain, path).Area < fp.Area - 1);
    }

    [Fact]
    public void Sarjeta_nao_e_recortada_pela_rampa()
    {
        var c = Make(new IntersectionDefinition { Control = ControleIntersecao.Pare });
        var gutters = c.S.Definitions.OfType<LinearMarkingDefinition>().Where(m => m.Code.StartsWith("SARJETA")).ToList();
        var ids = c.Ramps.Select(r => r.Id).ToHashSet();
        Assert.All(gutters, g => Assert.DoesNotContain(g.Exclusions, e => e.SourceId != null && ids.Contains(e.SourceId)));
        Assert.False(IntersectionGenerator.CutByRamps(new LinearMarkingDefinition { Code = "SARJETA" }));
        Assert.True(IntersectionGenerator.CutByRamps(new LinearMarkingDefinition { Code = "GRAMADO" }));
    }

    [Fact]
    public void Configuracao_das_rampas_e_gravada_e_lida_de_volta()
    {
        var d = new IntersectionDefinition { RampType = TipoRampa.RebaixamentoSemAbas, RampWidth = 2.2, RampSlope = 0.07, RampFreeWidth = 1.5, RampCurbHeight = 0.18 };
        var back = (IntersectionDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.Equal(TipoRampa.RebaixamentoSemAbas, back.RampType);
        Assert.Equal(2.2, back.RampWidth);
        Assert.Equal(0.07, back.RampSlope);
        Assert.Equal(1.5, back.RampFreeWidth);
        Assert.Equal(0.18, back.RampCurbHeight);
        // Interseção gravada antes destes campos: lê com os padrões.
        var old = (IntersectionDefinition)MarkingDefinition.FromJson(new IntersectionDefinition().ToJson()
            .Replace("\"RampWidth\":null,", "").Replace("\"RampType\"", "\"_x\""))!;
        Assert.Null(old.RampWidth);
        Assert.Equal(TipoRampa.RebaixamentoComAbas, old.RampType);
    }
}
