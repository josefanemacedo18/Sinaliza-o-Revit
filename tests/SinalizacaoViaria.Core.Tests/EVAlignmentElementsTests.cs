using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// EV / fase V: relatório de elementos do traçado (TS/SC/CS/ST) e do greide (PIV/PCV/PTV, K, ponto alto/baixo), conferido
/// com valores calculados à mão e com a geometria gerada pelas mesmas rotinas.
/// </summary>
public class EVAlignmentElementsTests
{
    // ------------------------------------------------------------------ horizontal

    [Fact]
    public void Horizontal_CurvaCircularSimples_ValoresAMao()
    {
        // Δ = 90° à esquerda, R = 50, sem espiral: T = R·tg(45°) = 50; Lc = R·π/2 = 78,540 m.
        var pis = new List<Pi> { new(new Vec2(0, 0)), new(new Vec2(100, 0), 50), new(new Vec2(100, 100)) };
        var e = Assert.Single(AlignmentElements.Horizontal(pis, null, out var length));
        Assert.Equal(90, e.DeflexaoGraus, 9);
        Assert.Equal(50, e.Raio, 9);
        Assert.Equal(50, e.Tangente, 9);
        Assert.Equal(50, e.EstacaTs!.Value, 9);
        Assert.Equal(50, e.EstacaSc!.Value, 9);
        Assert.Equal(50 + 25 * Math.PI, e.EstacaCs!.Value, 9);
        Assert.Equal(50 + 25 * Math.PI, e.EstacaSt!.Value, 9);
        Assert.Equal(25 * Math.PI, e.Circular, 9);
        Assert.False(e.Reduzida);
        Assert.Equal(50 + 25 * Math.PI + 50, length, 9);
        // A geometria gerada tem a mesma extensão (integração numérica: poucos cm).
        Assert.InRange(Tracado.Build(pis).Length, length - 0.1, length + 0.1);
    }

    [Fact]
    public void Horizontal_ComEspirais_ConsistenteComAGeometria()
    {
        var pis = new List<Pi>
        {
            new(new Vec2(0, 0)), new(new Vec2(300, 0), 150, 40), new(new Vec2(300, 300), 120, 30), new(new Vec2(600, 320)),
        };
        var els = AlignmentElements.Horizontal(pis, null, out var length);
        Assert.Equal(2, els.Count);
        Assert.True(els[0].DeflexaoGraus > 0);          // esquerda
        Assert.True(els[1].DeflexaoGraus < 0);          // direita
        foreach (var e in els)
        {
            Assert.Equal(e.EstacaTs!.Value + e.Espiral, e.EstacaSc!.Value, 9);
            Assert.Equal(e.EstacaCs!.Value + e.Espiral, e.EstacaSt!.Value, 9);
            Assert.True(e.Circular > 0);
        }
        Assert.True(els[1].EstacaTs > els[0].EstacaSt);
        Assert.InRange(Tracado.Build(pis).Length, length - 0.1, length + 0.1);
        // O TS fica sobre a tangente de entrada, a T do PI.
        var ts = pis[1].P - (pis[1].P - pis[0].P).Normalized() * els[0].Tangente;
        Assert.True(Tracado.Build(pis).Project(ts).Signed is var d && Math.Abs(d) < 0.01);
    }

    [Fact]
    public void Horizontal_ReduzidaESemCurva_NoCsv()
    {
        var pis = new List<Pi>
        {
            new(new Vec2(0, 0)), new(new Vec2(40, 0), 500), new(new Vec2(40, 40)), new(new Vec2(80, 80)),
        };
        var warnings = new List<string>();
        var els = AlignmentElements.Horizontal(pis, warnings);
        Assert.True(els[0].Reduzida);
        Assert.True(els[0].Raio < 40);
        Assert.False(els[1].TemCurva);
        Assert.Equal(-45, els[1].DeflexaoGraus, 6);           // à direita
        Assert.Contains(warnings, w => w.Contains("reduzidas"));
        var csv = AlignmentElements.HorizontalCsv(els);
        Assert.StartsWith("PI;X (m);Y (m);Deflexão (°);R (m)", csv);
        Assert.Contains("reduzida para caber", csv);
        Assert.Contains("sem curva", csv);
        Assert.Contains("500,000", csv);                // R pedido, vírgula decimal
    }

    [Fact]
    public void Horizontal_SoDoisPontos_ExtensaoDaReta()
    {
        var els = AlignmentElements.Horizontal(new List<Pi> { new(new Vec2(0, 0)), new(new Vec2(30, 40)) }, null, out var length);
        Assert.Empty(els);
        Assert.Equal(50, length, 9);
    }

    // ------------------------------------------------------------------ vertical

    [Fact]
    public void Vertical_CurvaConvexa_ValoresAMao()
    {
        // i1 = +2 %, i2 = −2 %, A = −4 %, L = 60 (padrão), K = 15; PCV 70, PTV 130; ponto alto no PIV, flecha A·L/8 = 0,30 m.
        var g = new RoadGrade { Points = { new(0, 0), new(100, 2), new(200, 0) } };
        var els = AlignmentElements.Vertical(g);
        Assert.Equal(3, els.Count);
        Assert.Null(els[0].RampaEntradaPct);
        Assert.Equal(2, els[0].RampaSaidaPct!.Value, 9);
        var v = els[1];
        Assert.True(v.Convexa);
        Assert.Equal(-4, v.VariacaoPct!.Value, 9);
        Assert.Equal(60, v.Comprimento, 9);
        Assert.Equal(15, v.K!.Value, 9);
        Assert.Equal(70, v.EstacaPcv!.Value, 9);
        Assert.Equal(130, v.EstacaPtv!.Value, 9);
        Assert.Equal(1.4, v.CotaPcv!.Value, 9);
        Assert.Equal(1.4, v.CotaPtv!.Value, 9);
        Assert.Equal(100, v.EstacaExtremo!.Value, 9);
        Assert.Equal(1.7, v.CotaExtremo!.Value, 9);
        Assert.Null(els[2].RampaSaidaPct);
    }

    [Fact]
    public void Vertical_PisoDeSuavizacaoELimiteDoVao()
    {
        // Côncava com A = 6 %: pedida 20 m, efetiva K = 10 → 60 m.
        var sag = AlignmentElements.Vertical(new RoadGrade { Points = { new(0, 3), new(100, 0, 20), new(200, 3) } })[1];
        Assert.False(sag.Convexa);
        Assert.Equal(20, sag.ComprimentoPedido, 9);
        Assert.Equal(60, sag.Comprimento, 9);
        Assert.Equal(10, sag.K!.Value, 9);
        Assert.Equal(100, sag.EstacaExtremo!.Value, 9);       // ponto baixo
        // Vão curto: limitada a 98 % do menor vão vizinho.
        var tight = AlignmentElements.Vertical(new RoadGrade { Points = { new(0, 0), new(30, 3), new(60, 0) } })[1];
        Assert.Equal(0.98 * 30, tight.Comprimento, 9);
        // Rampa contínua no PIV (A = 0): sem curva.
        var flat = AlignmentElements.Vertical(new RoadGrade { Points = { new(0, 0), new(50, 1), new(100, 2) } })[1];
        Assert.False(flat.TemCurva);
    }

    [Fact]
    public void Vertical_PontoExtremoForaDaCurva_NaoAparece()
    {
        // Rampas do mesmo sinal (+4 % → +1 %): sem ponto de rampa nula.
        var v = AlignmentElements.Vertical(new RoadGrade { Points = { new(0, 0), new(100, 4), new(200, 5) } })[1];
        Assert.True(v.TemCurva);
        Assert.Null(v.EstacaExtremo);
    }

    [Fact]
    public void Vertical_ConsistenteComAsCotasDoGreide()
    {
        var g = new RoadGrade { Points = { new(0, 10), new(120, 13, 80), new(260, 9), new(400, 9.5) } };
        foreach (var v in AlignmentElements.Vertical(g).Where(v => v.TemCurva))
        {
            // PCV e PTV sobre as tangentes; rampa contínua na entrada e na saída da parábola.
            Assert.Equal(v.Cota - v.RampaEntradaPct!.Value / 100 * v.Comprimento / 2, v.CotaPcv!.Value, 6);
            Assert.Equal(v.Cota + v.RampaSaidaPct!.Value / 100 * v.Comprimento / 2, v.CotaPtv!.Value, 6);
            Assert.Equal(v.RampaEntradaPct.Value / 100, g.GradeAt(v.EstacaPcv!.Value - 1), 3);
            Assert.Equal(v.RampaSaidaPct.Value / 100, g.GradeAt(v.EstacaPtv!.Value + 1), 3);
        }
    }

    [Fact]
    public void Vertical_PIVsForaDeOrdem_SaemOrdenados()
    {
        var els = AlignmentElements.Vertical(new RoadGrade { Points = { new(200, 0), new(0, 0), new(100, 2) } });
        Assert.Equal(new[] { 0.0, 100.0, 200.0 }, els.Select(e => e.Estaca));
    }

    [Fact]
    public void Csv_EEstacas()
    {
        Assert.Equal("0+0,00", AlignmentElements.Estaca(0));
        Assert.Equal("2+5,50", AlignmentElements.Estaca(45.5));
        Assert.Equal("2+0,00", AlignmentElements.Estaca(40));
        var csv = AlignmentElements.VerticalCsv(AlignmentElements.Vertical(new RoadGrade { Points = { new(0, 0), new(100, 2), new(200, 0) } }));
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("PIV;Estaca;Estaca (m);Cota (m);i1 (%);i2 (%);A (%);L (m)", lines[0]);
        Assert.Contains("convexa", lines[2]);
        Assert.Contains("15,00", lines[2]);              // K
        Assert.Contains("3+10,00 (70,00)", lines[2]);    // PCV
    }
}

/// <summary>EW / F5b: elementos do eixo das vias comuns, reconhecidos na polilinha gerada (cantos arredondados, cantos vivos).</summary>
public class EWAxisElementsTests
{
    private static Polyline2 Eixo(double radius, params (double X, double Y)[] p) =>
        new(CurveTools.FilletCorners(p.Select(q => new Vec2(q.X, q.Y)).ToList(), radius));

    [Fact]
    public void CantoArredondado_TangenteCurvaTangente()
    {
        // PIs (0,0)–(100,0)–(100,100) com raio 30: T = 30, curva de 70 a 70 + 15π (corda: 0,005 m de flecha).
        var axis = Eixo(30, (0, 0), (100, 0), (100, 100));
        var els = AlignmentElements.FromAxis(axis);
        Assert.Equal(new[] { TipoElementoEixo.Tangente, TipoElementoEixo.Curva, TipoElementoEixo.Tangente }, els.Select(e => e.Tipo));
        var c = els[1];
        Assert.Equal(70, c.Inicio, 6);
        Assert.Equal(30, c.Raio, 6);
        Assert.Equal(90, c.DeflexaoGraus, 6);
        Assert.Equal("esquerda", c.Sentido);
        Assert.Equal(30, c.TangenteExterna, 6);
        Assert.InRange(c.Comprimento, 15 * Math.PI - 0.01, 15 * Math.PI);
        Assert.Equal(axis.Length, els[^1].Fim, 9);
        Assert.Equal(70, els[0].Comprimento, 6);
        Assert.Equal(70, els[2].Comprimento, 2);
    }

    [Fact]
    public void CantoVivo_ViraVerticeSemCurva()
    {
        var els = AlignmentElements.FromAxis(new Polyline2(new[] { new Vec2(0, 0), new Vec2(50, 0), new Vec2(50, -50) }));
        Assert.Equal(new[] { TipoElementoEixo.Tangente, TipoElementoEixo.Vertice, TipoElementoEixo.Tangente }, els.Select(e => e.Tipo));
        Assert.Equal(-90, els[1].DeflexaoGraus, 9);
        Assert.Equal("direita", els[1].Sentido);
        Assert.Equal(50, els[1].Inicio, 9);
    }

    [Fact]
    public void CurvaReversa_DuasCurvasComTangenteEntreElas()
    {
        var els = AlignmentElements.FromAxis(Eixo(30, (0, 0), (100, 0), (100, 100), (200, 100)));
        var curvas = els.Where(e => e.Tipo == TipoElementoEixo.Curva).ToList();
        Assert.Equal(2, curvas.Count);
        Assert.Equal(90, curvas[0].DeflexaoGraus, 6);
        Assert.Equal(-90, curvas[1].DeflexaoGraus, 6);
        Assert.All(curvas, c => Assert.Equal(30, c.Raio, 6));
        // Tangente entre as curvas: 100 − 30 − 30 = 40 m.
        var meio = els[els.IndexOf(curvas[0]) + 1];
        Assert.Equal(TipoElementoEixo.Tangente, meio.Tipo);
        Assert.Equal(40, meio.Comprimento, 2);
    }

    [Fact]
    public void Reta_SoUmaTangente_E_Csv()
    {
        var els = AlignmentElements.FromAxis(Polyline2.FromSegment(new Vec2(0, 0), new Vec2(120, 0)));
        var t = Assert.Single(els);
        Assert.Equal(120, t.Comprimento, 9);
        var csv = AlignmentElements.AxisCsv(AlignmentElements.FromAxis(Eixo(30, (0, 0), (100, 0), (100, 100))));
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Elemento;Início;Fim;Início (m);Fim (m);Comprimento (m);Raio (m);Deflexão (°);Sentido;T (m)", lines[0]);
        Assert.StartsWith("curva circular;3+10,00;", lines[2]);
        Assert.Contains(";30,000;90,0000;esquerda;30,000", lines[2]);
    }
}
