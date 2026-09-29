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
}
