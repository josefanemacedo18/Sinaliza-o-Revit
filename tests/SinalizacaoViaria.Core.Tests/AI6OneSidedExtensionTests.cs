using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AI, item 6: extensão de calçada de um lado só da esquina (só uma das vias tem estacionamento). A extensão nasce da
/// curva da esquina: o meio-fio novo é o da outra via, a curva da esquina com o mesmo raio (agora tangente à face da
/// extensão) e a face da extensão, que volta ao alinhamento com a curva reversa da ponta – contínuo e tangente do começo ao
/// fim, sem bico nem degrau. A faixa, as rampas e o piso tátil acompanham o novo fim da curva.
/// </summary>
public class AI6OneSidedExtensionTests
{
    private static IntersectionDefinition Tpl() => new()
    {
        Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true, CurbExtensions = true, CurbExtensionsOpposite = true,
    };

    public static IEnumerable<object[]> Cases() => new[]
    {
        // A coletora não tem estacionamento junto ao meio-fio: a extensão fica só do lado da avenida com canteiro.
        new object[] { "cruz", "canteiro", "coletora", 90.0 },
        new object[] { "cruz", "coletora", "canteiro", 75.0 },
        new object[] { "cruz", "canteiro", "coletora", 105.0 },
        new object[] { "cruz", "coletora", "canteiro", 60.0 },
        new object[] { "T", "canteiro", "coletora", 90.0 },
        new object[] { "T", "coletora", "canteiro", 90.0 },
    };

    private static double Turn(Vec2 a, Vec2 b, Vec2 c)
    {
        var u = (b - a).Normalized();
        var v = (c - b).Normalized();
        return Math.Abs(Math.Atan2(u.Cross(v), u.Dot(v))) * 180 / Math.PI;
    }

    /// <summary>Círculo por três pontos (centro, raio).</summary>
    private static (Vec2 C, double R) Circle3(Vec2 a, Vec2 b, Vec2 c)
    {
        var d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        var ux = ((a.X * a.X + a.Y * a.Y) * (b.Y - c.Y) + (b.X * b.X + b.Y * b.Y) * (c.Y - a.Y) + (c.X * c.X + c.Y * c.Y) * (a.Y - b.Y)) / d;
        var uy = ((a.X * a.X + a.Y * a.Y) * (c.X - b.X) + (b.X * b.X + b.Y * b.Y) * (a.X - c.X) + (c.X * c.X + c.Y * c.Y) * (b.X - a.X)) / d;
        var ctr = new Vec2(ux, uy);
        return (ctr, ctr.DistanceTo(a));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void OneSidedExtension_IsBornFromTheCornerCurve_ContinuousAndTangent(string kind, string a, string b, double ang)
    {
        var w = AB2Matrix.Make(Tpl(), AB2Matrix.Axes(kind, a, b, ang));
        var L = w.Nodes[0].Layout;
        var ones = L.Ears.Where(e => !e.Straight && e.SideA != e.SideB).ToList();
        Assert.NotEmpty(ones);
        foreach (var e in ones)
        {
            var face = e.Template.OuterFace;
            Assert.NotNull(face);
            // Começa e termina no meio-fio antigo (pontas do caminho), sem degrau.
            Assert.True(face![0].DistanceTo(e.Path.Points[0]) < 0.01, $"início da face a {face[0].DistanceTo(e.Path.Points[0]):0.000} m do meio-fio");
            Assert.True(face[^1].DistanceTo(e.Path.Points[^1]) < 0.01, $"fim da face a {face[^1].DistanceTo(e.Path.Points[^1]):0.000} m do meio-fio");
            // Tangente ao meio-fio antigo nas duas pontas (a face sai e volta na direção dele): no máximo o giro de um passo
            // da curva discretizada (a primeira corda de um arco tangente gira meio passo).
            Assert.True(Turn(e.Path.Points[1], e.Path.Points[0], face[1]) is var t0 && (t0 <= 8 || t0 >= 172), $"início: {t0:0.0}°");
            Assert.True(Turn(face[^2], face[^1], e.Path.Points[^2]) is var t1 && (t1 <= 8 || t1 >= 172), $"fim: {t1:0.0}°");
            // Contínua e tangente: nenhum vértice gira mais que 8° (arco a cada 1°, curva reversa da ponta em 10 passos por arco).
            for (int i = 1; i + 1 < face.Count; i++)
                Assert.True(Turn(face[i - 1], face[i], face[i + 1]) <= 8.0, $"bico na face em {face[i]}: {Turn(face[i - 1], face[i], face[i + 1]):0.0}°");
            // A curva da esquina continua com o mesmo raio (±1 mm): três pontos do trecho de giro mais forte.
            var cf = L.CornerFillets[(e.LegA.Road, e.LegA.Sign)];
            var arc = Enumerable.Range(1, face.Count - 2).Where(i => Turn(face[i - 1], face[i], face[i + 1]) > 0.5 && Turn(face[i - 1], face[i], face[i + 1]) < 1.5).ToList();
            Assert.True(arc.Count >= 10, "arco da esquina não encontrado na face");
            var (_, r) = Circle3(face[arc[0]], face[arc[arc.Count / 2]], face[arc[^1]]);
            Assert.InRange(r, cf.R - 0.001, cf.R + 0.001);
        }
        // Pisos sem furos, vãos, sobreposições nem pontas (verificações da matriz AB-2).
        var res = AB2Check.Run(w);
        Assert.Empty(res.Faults);
        Assert.Empty(res.Spikes);
        // Faixa, retenção, PARE, R-1 e rampas no novo fim da curva (item 1).
        Assert.Empty(AI1CornerTipTests.Check(w));
    }

    /// <summary>Print 5: a extensão de um lado só aproxima a travessia da outra via (o trecho reto dela agora vai até a nova curva).</summary>
    [Fact]
    public void OneSidedExtension_BringsTheOtherRoadsCrosswalkCloser()
    {
        var plain = AB2Matrix.Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = true, Ramps = true },
            AB2Matrix.Axes("cruz", "canteiro", "coletora", 90));
        var ext = AB2Matrix.Make(Tpl(), AB2Matrix.Axes("cruz", "canteiro", "coletora", 90));
        var L0 = plain.Nodes[0].Layout;
        var L1 = ext.Nodes[0].Layout;
        // Via 1 (coletora, sem estacionamento): a curva da esquina ficou mais curta do lado dela.
        foreach (var leg in L1.Legs.Where(l => l.Road == 1))
        {
            var before = IntersectionGenerator.CrosswalkNearT(L0.Definition!, L0, L0.Legs.First(l => l.Road == leg.Road && l.Sign == leg.Sign));
            var after = IntersectionGenerator.CrosswalkNearT(L1.Definition!, L1, leg);
            Assert.True(after < before - 1.0, $"travessia da coletora: {before:0.00} → {after:0.00} m");
        }
    }
}
