using System.Runtime.CompilerServices;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 13 do plano – desempenho (Prompt de Motores §18; Master §21): o que a medição do núcleo (tools/Desempenho) mostrou e
/// foi corrigido, com o mesmo resultado de antes.
/// </summary>
public class Etapa13DesempenhoTests
{
    /// <summary>Polígonos variados: retângulos, quadriláteros girados e um com furo, espalhados como plataformas de vias.</summary>
    private static List<Polygon2> Plataformas(Random r)
    {
        var res = new List<Polygon2>();
        for (var i = 0; i < 40; i++)
        {
            var c = new Vec2(r.NextDouble() * 300, r.NextDouble() * 300);
            var (w, h, ang) = (2 + r.NextDouble() * 40, 2 + r.NextDouble() * 12, r.NextDouble() * Math.PI);
            Vec2 P(double x, double y) => c + new Vec2(x * Math.Cos(ang) - y * Math.Sin(ang), x * Math.Sin(ang) + y * Math.Cos(ang));
            var outer = new[] { P(-w, -h), P(w, -h), P(w, h), P(-w, h) };
            res.Add(i % 7 == 0 && h > 4
                ? new Polygon2(outer, new[] { new[] { P(-w / 2, -h / 3), P(-w / 2, h / 3), P(w / 2, h / 3), P(w / 2, -h / 3) } })
                : new Polygon2(outer));
        }
        return res;
    }

    [Fact]
    public void PontoMaisProximoDasPlataformas_OIndiceDaOMesmoPontoQueABuscaEmTodos()
    {
        // Antes, os contornos dos taludes procuravam o ponto mais próximo percorrendo todos os lados de todas as plataformas –
        // quadrático no tamanho do projeto (10,8 s de 13 s na terraplenagem de 64 vias). O índice tem de dar exatamente o
        // mesmo ponto: perto das plataformas pelos lados das células vizinhas, longe delas (sem candidato a até uma célula)
        // pela busca em todos.
        var r = new Random(13);
        var plats = Plataformas(r);
        var indice = new RingIndex(plats);
        var longe = 0;
        for (var k = 0; k < 4000; k++)
        {
            Vec2 q;
            if (k % 4 == 3) q = new Vec2(-200 + r.NextDouble() * 700, -200 + r.NextDouble() * 700);   // em qualquer lugar, inclusive longe
            else
            {
                // Junto ao contorno (como os pontos dos contornos a 0,3 e 0,6 m das plataformas).
                var g = plats[r.Next(plats.Count)];
                var ring = r.Next(4) == 0 && g.Holes.Count > 0 ? g.Holes[0] : g.Outer;
                var i = r.Next(ring.Count);
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                q = a + (b - a) * r.NextDouble() + new Vec2(r.NextDouble() - 0.5, r.NextDouble() - 0.5) * 2;
            }
            var esperado = Grading.Nearest(plats, q);
            Assert.Equal(esperado, indice.Nearest(q));
            if (esperado is { } e && q.DistanceTo(e) > 2) longe++;
        }
        Assert.True(longe > 100, "o caso sem candidato perto (busca em todos) também precisa ser conferido");
        Assert.Null(new RingIndex(Array.Empty<Polygon2>()).Nearest(new Vec2(1, 1)));
    }

    [Fact]
    public void Terraplenagem_ContornosPeloIndice()
    {
        var src = File.ReadAllText(Path.Combine(Raiz(), "src", "SinalizacaoViaria.Core", "Automation", "Grading.cs"));
        Assert.Contains("var nearest = new RingIndex(plats);", src);
        Assert.Contains("var bp = nearest.Nearest(q);", src);
        Assert.DoesNotContain("Nearest(plats, q)", src);
    }

    [Fact]
    public void Terraplenagem_VariasVias_VolumesPorViaIguaisESomaDoTotal()
    {
        // Vias iguais lado a lado, longe uma da outra: cada uma com o mesmo corte, a menos da amostragem (a malha dos volumes cai
        // em pontos diferentes de cada via – menos de 1 %), e a soma das donas é o total.
        var corredores = Enumerable.Range(0, 6).Select(i =>
        {
            var c = new GradeCorridor { CutSlope = 1.0, FillSlope = 1.5, Owner = "v" + i };
            var y = i * 40.0;
            for (var x = 0.0; x <= 100; x += 5) { c.Left.Add(new Vec3(x, y + 6.5, 2)); c.Right.Add(new Vec3(x, y - 6.5, 2)); }
            return c;
        }).ToList();
        var res = Grading.Design(corredores, Array.Empty<GradePad>(), _ => 4.0, 0, 30, 0.5, 1.0, 1.0);
        var cortes = corredores.Select(c => res.ByOwner[c.Owner!].Cut).ToList();
        Assert.All(cortes, v => Assert.InRange(v, cortes[0] * 0.99, cortes[0] * 1.01));
        Assert.Equal(res.CutM3, cortes.Sum(), 3);
        Assert.Contains(res.GroundPoints, p => Math.Abs(p.Z - 4) < 1e-9);   // pés dos taludes junto às plataformas
    }

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
}
