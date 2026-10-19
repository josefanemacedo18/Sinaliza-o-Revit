using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// EW – avisos do Revit vistos no Autoteste de 2026-10-19 (docs/evidencias): "A linha no croqui está ligeiramente fora da
/// linha central" nos pisos da interseção. Reprodução no núcleo (cruzamento local × local, como o Autoteste) e correção
/// pelo alinhamento das retas quase alinhadas aos eixos (<see cref="AxisSnap"/>).
/// </summary>
public class EWRevitWarningsTests
{
    private static BoundarySegment L(double x0, double y0, double x1, double y1) => new(new Vec2(x0, y0), new Vec2(x1, y1));

    private static List<BoundarySegment> Ring(params (double X, double Y)[] p) =>
        p.Select((a, i) => L(a.X, a.Y, p[(i + 1) % p.Length].X, p[(i + 1) % p.Length].Y)).ToList();

    [Fact]
    public void Resíduo_DeUmDecimoDeMilimetro_ViraRetaExata()
    {
        // Retângulo 20 × 10 com o lado de cima inclinado 0,1 mm (resíduo de recorte).
        var r = Ring((0, 0), (20, 0), (20, 10.0001), (0, 10));
        Assert.Equal(1, AxisSnap.CountNearAxis(r));
        var s = AxisSnap.Snap(r);
        Assert.Equal(0, AxisSnap.CountNearAxis(s));
        Assert.Equal(s[2].Start.Y, s[2].End.Y);
        // O vértice andou no máximo 0,1 mm e o contorno continua fechado.
        Assert.True(s.Zip(r).All(p => p.First.Start.DistanceTo(p.Second.Start) <= 1e-4 + 1e-12));
        Assert.All(s.Select((x, i) => (x, i)), t => Assert.Equal(t.x.End, s[(t.i + 1) % s.Count].Start));
    }

    [Fact]
    public void InclinacaoDeProjeto_NaoEMexida()
    {
        // 5 mm em 20 m: maior que o limite de 0,5 mm – reta inclinada de verdade.
        var r = Ring((0, 0), (20, 0.005), (20, 10), (0, 10));
        Assert.True(r.SequenceEqual(AxisSnap.Snap(r)));
        // Reta curta (5 mm) com 0,3 mm de desvio = 3,4°: fora do ângulo "quase alinhado" – continua inclinada (a vizinha longa,
        // quase horizontal, é que fica exata e leva o vértice comum junto).
        var curta = Ring((0, 0), (10, 0), (10, 5), (5.005, 5.0003), (5, 5), (0, 5));
        var s = AxisSnap.Snap(curta);
        Assert.NotEqual(s[3].Start.Y, s[3].End.Y);
        Assert.Equal(s[2].Start.Y, s[2].End.Y);
        Assert.Equal(0, AxisSnap.CountNearAxis(s));
    }

    [Fact]
    public void QuinaComAsDuasRetas_E_ArcosPreservados()
    {
        // Quina com reta quase horizontal e reta quase vertical; o arco mantém o ponto do meio.
        var r = new List<BoundarySegment>
        {
            L(0, 0, 10, 0.0002),
            L(10, 0.0002, 10.0003, 8),
            new(new Vec2(10.0003, 8), new Vec2(8, 10), new Vec2(9.4142, 9.4142)),
            L(8, 10, 0, 10),
            L(0, 10, 0, 0),
        };
        var s = AxisSnap.Snap(r);
        Assert.Equal(0, AxisSnap.CountNearAxis(s));
        // O grupo vai à cota média da reta mais longa dele (0 → 0,2 mm: 0,1 mm); a reta fica exatamente horizontal.
        Assert.Equal(0.0001, s[0].Start.Y, 12);
        Assert.Equal(s[0].Start.Y, s[0].End.Y);
        Assert.True(s[2].IsArc);
        Assert.Equal(r[2].Mid, s[2].Mid);
        Assert.Equal(s[1].Start.X, s[1].End.X);
    }

    [Fact]
    public void Trecho_Reta_ToStringNaoLanca()
    {
        // Antes: Circle lia Mid!.Value e o ToString do record lançava InvalidOperationException numa reta.
        var t = L(0, 0, 3, 4).ToString();
        Assert.Contains("Start", t);
    }

    [Fact]
    public void Cruzamento_LocalLocal_PisosDaIntersecaoSemRetasQuaseAlinhadas()
    {
        var cat = CatalogService.LoadDefault();
        var scene = IntersectionDemo.Create(new IntersectionDefinition(), cat, 0, 0, 90);
        var ctx = new BuildContext { Catalog = cat, Lookup = id => scene.Definitions.FirstOrDefault(x => x.Id == id), PathOf = x => scene.Paths.GetValueOrDefault(x.Id) };
        int before = 0, after = 0, rings = 0;
        foreach (var def in scene.Definitions.OfType<IntersectionDefinition>())
        {
            var g = MarkingBuilder.Build(def, null, ctx);
            foreach (var pe in ElementPlan.Plan(def, g).Where(p => p.IsFloor))
                foreach (var sh in pe.Shapes)
                    foreach (var ring in BoundaryFit.FitPolygon(ElementPlan.Prepare(sh), BoundaryFit.DefaultTolerance, 0.0018, g.ArcCenters))
                    {
                        rings++;
                        before += AxisSnap.CountNearAxis(ring);
                        var snapped = AxisSnap.Snap(ring);
                        after += AxisSnap.CountNearAxis(snapped);
                        // Continua um contorno válido (sem laço) e com a mesma área (diferença < 1 cm²).
                        var pts = snapped.SelectMany(x => x.Sample(0.005)).ToList();
                        var orig = ring.SelectMany(x => x.Sample(0.005)).ToList();
                        Assert.Null(PolygonChecks.Problem(pts, 1e-7));
                        Assert.InRange(Math.Abs(PolygonChecks.SignedArea(pts)) - Math.Abs(PolygonChecks.SignedArea(orig)), -1e-4, 1e-4);
                    }
        }
        Assert.True(rings > 0);
        Assert.True(before > 0, "a reprodução deixou de ter retas quase alinhadas – revise o teste");
        Assert.Equal(0, after);
    }
}
