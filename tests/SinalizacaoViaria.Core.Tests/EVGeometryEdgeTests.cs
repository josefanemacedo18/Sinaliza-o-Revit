using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// EV / fase IV: primitivas geométricas em entradas degeneradas – polilinha vazia, de um ponto, com pontos repetidos,
/// voltas de 180°, estacas fora do trecho; traçado horizontal com PIs coincidentes, colineares, deflexão ~180° e raio que
/// não cabe. Critério: nenhum NaN/∞ na saída, pontas preservadas e aviso quando a entrada é ajustada.
/// </summary>
public class EVGeometryEdgeTests
{
    private static bool Finite(Vec2 p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
    private static bool AllFinite(IEnumerable<Vec2> pts) => pts.All(Finite);

    // ------------------------------------------------------------------ Polyline2

    [Fact]
    public void Polilinha_VaziaEDeUmPonto()
    {
        var empty = new Polyline2(Array.Empty<Vec2>());
        Assert.Equal(0, empty.Length);
        Assert.Equal(0, empty.SegmentCount);
        Assert.Equal(Vec2.Zero, empty.PointAt(3));
        Assert.Same(empty, empty.Offset(1));

        var p = new Vec2(5, 7);
        var one = new Polyline2(new[] { p, p, p });          // repetidos colapsam num ponto
        Assert.Single(one.Points);
        Assert.Equal(0, one.Length);
        Assert.Equal(p, one.PointAt(10));
        Assert.Equal(Vec2.UnitX, one.TangentAt(0));
        Assert.Equal(new[] { p }, one.SubPoints(0, 5));
        // Sem segmentos não há projeção: distância máxima (contrato documentado – um eixo degenerado nunca é "o mais próximo").
        Assert.Equal((0.0, double.MaxValue), one.Project(new Vec2(8, 11)));
        Assert.Equal((0.0, double.MaxValue), empty.Project(new Vec2(8, 11)));
    }

    [Fact]
    public void Polilinha_PontosRepetidosEEstacasForaDoTrecho()
    {
        var pl = new Polyline2(new[] { new Vec2(0, 0), new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 0), new Vec2(10, 5) });
        Assert.Equal(3, pl.Points.Count);
        Assert.Equal(15, pl.Length, 9);
        Assert.Equal(new Vec2(0, 0), pl.PointAt(-3));
        Assert.Equal(new Vec2(10, 5), pl.PointAt(99));
        Assert.Equal(new Vec2(10, 2), pl.PointAt(12));
        Assert.Equal(new Vec2(0, 1), pl.TangentAt(14));
        // Trecho invertido ou vazio não lança exceção.
        Assert.NotEmpty(pl.SubPoints(8, 8));
    }

    [Fact]
    public void Polilinha_FechadaComPontoFinalRepetido()
    {
        var sq = new[] { new Vec2(0, 0), new Vec2(4, 0), new Vec2(4, 4), new Vec2(0, 4) };
        var a = new Polyline2(sq, closed: true);
        var b = new Polyline2(sq.Append(sq[0]), closed: true);
        Assert.True(a.Closed);
        Assert.Equal(16, a.Length, 9);
        Assert.Equal(a.Points, b.Points);
        // Dois pontos não fecham um contorno.
        Assert.False(new Polyline2(sq.Take(2), closed: true).Closed);
    }

    [Fact]
    public void Polilinha_DeslocamentoNaVoltaDe180Graus_SemNaN()
    {
        var pl = new Polyline2(new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(0, 0) });
        var off = pl.Offset(1);
        Assert.Equal(pl.Points.Count, off.Points.Count);
        Assert.True(AllFinite(off.Points));
        Assert.Equal(new Vec2(10, 1), off.Points[1]);
    }

    [Fact]
    public void Polilinha_DeslocamentoEmAnguloAgudo_LimitadoPeloEsquadro()
    {
        // Ângulo de 10° entre os lados: sem limite o vértice deslocado iria a ~11,5 m; com limite 4, no máximo 4 m.
        var a = new Vec2(0, 0);
        var b = new Vec2(10, 0);
        var c = b + Vec2.FromAngle(Math.PI - 10 * Math.PI / 180) * 10;
        var off = new Polyline2(new[] { a, b, c }).Offset(-1, miterLimit: 4);
        Assert.True(AllFinite(off.Points));
        Assert.InRange(off.Points[1].DistanceTo(b), 1 - 1e-9, 4 + 1e-9);
    }

    [Fact]
    public void Polilinha_ProjecaoComSinal()
    {
        var pl = Polyline2.FromSegment(new Vec2(0, 0), new Vec2(10, 0));
        Assert.Equal((5.0, 2.0), pl.Project(new Vec2(5, 2)));
        Assert.Equal((5.0, -2.0), pl.Project(new Vec2(5, -2)));
        // Além da ponta: estaca na ponta e distância até ela.
        var (s, d) = pl.Project(new Vec2(13, 4));
        Assert.Equal(10, s, 9);
        Assert.Equal(5, Math.Abs(d), 9);
    }

    // ------------------------------------------------------------------ Tracado

    [Fact]
    public void Tracado_MenosDeDoisPontos_ErroClaro()
    {
        var ex = Assert.Throws<ArgumentException>(() => Tracado.Build(new List<Pi> { new(new Vec2(0, 0)) }));
        Assert.Contains("dois pontos", ex.Message);
    }

    [Fact]
    public void Tracado_PIsCoincidentes_SemNaNEComAsPontas()
    {
        var a = new Vec2(0, 0);
        var b = new Vec2(100, 0);
        var path = Tracado.Build(new List<Pi> { new(a), new(a, 30, 10), new(b) });
        Assert.True(AllFinite(path.Points));
        Assert.Equal(a, path.Points[0]);
        Assert.Equal(b, path.Points[^1]);
        Assert.Equal(100, path.Length, 6);
    }

    [Fact]
    public void Tracado_PIsColineares_ViramReta()
    {
        var path = Tracado.Build(new List<Pi> { new(new Vec2(0, 0)), new(new Vec2(50, 0), 200, 40), new(new Vec2(120, 0)) });
        Assert.True(AllFinite(path.Points));
        Assert.Equal(120, path.Length, 6);
        Assert.All(path.Points, p => Assert.Equal(0, p.Y, 9));
    }

    [Theory]
    [InlineData(179.0)]
    [InlineData(179.99)]
    [InlineData(180.0)]
    public void Tracado_DeflexaoPertoDe180_SemNaNEComAviso(double deflexaoGraus)
    {
        var a = new Vec2(0, 0);
        var b = new Vec2(100, 0);
        var c = b + Vec2.FromAngle(Math.PI - deflexaoGraus * Math.PI / 180 + Math.PI) * 60;
        var warnings = new List<string>();
        var path = Tracado.Build(new List<Pi> { new(a), new(b, 25, 10), new(c) }, warnings: warnings);
        Assert.True(AllFinite(path.Points));
        Assert.Equal(a, path.Points[0]);
        Assert.True(path.Points[^1].DistanceTo(c) < 1e-6);
        Assert.Contains(warnings, w => w.Contains("reduzidas"));
    }

    [Fact]
    public void Tracado_RaioQueNaoCabe_ReduzidoComAvisoECurvaEntreOsPIs()
    {
        var warnings = new List<string>();
        var pis = new List<Pi> { new(new Vec2(0, 0)), new(new Vec2(40, 0), 500, 0), new(new Vec2(40, 40)) };
        var path = Tracado.Build(pis, warnings: warnings);
        Assert.Contains(warnings, w => w.Contains("reduzidas"));
        Assert.True(AllFinite(path.Points));
        // A curva reduzida cabe nos dois lados: os pontos ficam no quadrado definido pelos PIs.
        // (folga de 5 cm: a curva é integrada numericamente.)
        Assert.All(path.Points, p => Assert.InRange(p.X, -0.05, 40.05));
        Assert.All(path.Points, p => Assert.InRange(p.Y, -0.05, 40.05));
        // Sem quinas: deflexão entre pontos vizinhos pequena.
        for (int i = 1; i + 1 < path.Points.Count; i++)
        {
            var u = (path.Points[i] - path.Points[i - 1]).Normalized();
            var v = (path.Points[i + 1] - path.Points[i]).Normalized();
            Assert.True(Math.Acos(Math.Clamp(u.Dot(v), -1, 1)) < 3 * Math.PI / 180);
        }
    }

    [Fact]
    public void Tracado_EspiralMaiorQueOArco_Limitada()
    {
        // Espiral pedida (400 m) maior que o desenvolvimento do arco: limitada a 95 % de R·Δ, sem NaN e com curvatura contínua.
        var path = Tracado.Build(new List<Pi> { new(new Vec2(0, 0)), new(new Vec2(500, 0), 100, 400), new(new Vec2(500, 500)) });
        Assert.True(AllFinite(path.Points));
        Assert.Equal(new Vec2(500, 500), path.Points[^1]);
        for (int i = 1; i + 1 < path.Points.Count; i++)
        {
            var u = (path.Points[i] - path.Points[i - 1]).Normalized();
            var v = (path.Points[i + 1] - path.Points[i]).Normalized();
            Assert.True(Math.Acos(Math.Clamp(u.Dot(v), -1, 1)) < 3 * Math.PI / 180);
        }
    }
}
