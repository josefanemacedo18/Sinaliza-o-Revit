using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// EV / fase VII: volumes de terraplenagem pelo método das seções (média das áreas), com volumes conhecidos, o erro esperado
/// do método e a conferência com a malha de <see cref="Grading.Volumes"/>.
/// </summary>
public class EVSectionVolumesTests
{
    private static readonly Polyline2 Reta = Polyline2.FromSegment(new Vec2(0, 0), new Vec2(100, 0));

    /// <summary>Plataforma de 10 m de largura (|y| ≤ 5) na cota 0 ao longo do eixo reto.</summary>
    private static double? Faixa(Vec2 p) => Math.Abs(p.Y) <= 5 ? 0 : null;

    [Fact]
    public void Corte_Constante_VolumeExato()
    {
        var v = SectionVolumes.Compute(Reta, Faixa, _ => 1.0);
        Assert.Equal(1000, v.CorteM3, 6);
        Assert.Equal(0, v.AterroM3, 9);
        Assert.Equal(6, v.Secoes.Count);                    // 0, 20, …, 100
        Assert.All(v.Secoes, s => Assert.Equal(10, s.Corte, 9));
        Assert.Equal(1000, v.Acumulado[^1].Corte, 6);
    }

    [Fact]
    public void Aterro_Constante_VolumeExato()
    {
        var v = SectionVolumes.Compute(Reta, Faixa, _ => -2.0);
        Assert.Equal(2000, v.AterroM3, 6);
        Assert.Equal(0, v.CorteM3, 9);
    }

    [Fact]
    public void TerrenoLinear_MetodoExato_QualquerEspacamento()
    {
        // Área A(s) = 10 · 0,01·s → V = ∫ 0,1·s ds = 500 m³; a média das áreas é exata para variação linear.
        foreach (var e in new[] { 5.0, 20.0, 33.0 })
            // (2 casas: as seções das pontas são amostradas 1 mm para dentro.)
            Assert.Equal(500, SectionVolumes.Compute(Reta, Faixa, p => 0.01 * p.X, e).CorteM3, 2);
    }

    [Fact]
    public void TerrenoQuadratico_SuperestimaComoEsperado()
    {
        // A(s) = 10·1e-4·s² → exato 333,33 m³; trapézios de 20 m superestimam em h²/12 · ∫A'' = 400/12 · 2e-3 · 100 = 6,67 m³.
        var v20 = SectionVolumes.Compute(Reta, Faixa, p => 1e-4 * p.X * p.X, 20).CorteM3;
        Assert.Equal(1000.0 / 3 + 20.0 / 3, v20, 2);
        var v5 = SectionVolumes.Compute(Reta, Faixa, p => 1e-4 * p.X * p.X, 5).CorteM3;
        Assert.True(Math.Abs(v5 - 1000.0 / 3) < Math.Abs(v20 - 1000.0 / 3));
    }

    [Fact]
    public void UltimaSecaoNoFimDoEixo()
    {
        var v = SectionVolumes.Compute(Polyline2.FromSegment(new Vec2(0, 0), new Vec2(50, 0)), Faixa, _ => 1.0, 20);
        Assert.Equal(new[] { 0.0, 20.0, 40.0, 50.0 }, v.Secoes.Select(s => s.Estaca));
        Assert.Equal(500, v.CorteM3, 6);
    }

    [Fact]
    public void EixoEmCurva_FaixaSimetrica_IgualAExtensaoVezesLargura()
    {
        // Arco de 90°, R = 50; faixa de 10 m centrada no eixo: área = L·W (Pappus com centroide no eixo).
        var pts = Enumerable.Range(0, 181).Select(i => Vec2.FromAngle(i * Math.PI / 360) * 50).ToList();
        var arco = new Polyline2(pts);
        double? Anel(Vec2 p) => Math.Abs(p.Length - 50) <= 5 ? 0 : null;
        var v = SectionVolumes.Compute(arco, Anel, _ => 1.0, 10, 8, 0.05);
        Assert.InRange(v.CorteM3, arco.Length * 10 * 0.995, arco.Length * 10 * 1.005);
    }

    [Fact]
    public void ConfereComAMalhaDaTerraplenagem()
    {
        // Mesma plataforma pelos dois métodos: malha estratificada (Grading.Volumes) × seções.
        var rect = new Polygon2(new[] { new Vec2(0, -5), new Vec2(100, -5), new Vec2(100, 5), new Vec2(0, 5) });
        var res = new GradingResult();
        res.Pads.Add((rect, _ => 0.0));
        res.PadOwners.Add("via");
        res.Footprint.Add(rect);
        Func<Vec2, double?> ground = p => 1.0 + 0.02 * p.X;          // corte de 1 a 3 m: 2 000 m³
        Grading.Volumes(res, ground, 1.0);
        var sec = SectionVolumes.Compute(Reta, p => res.DesignZ(p), ground);
        Assert.Equal(2000, sec.CorteM3, 2);
        Assert.InRange(res.CutM3, sec.CorteM3 * 0.98, sec.CorteM3 * 1.02);
    }

    [Fact]
    public void Csv_PtBr()
    {
        var csv = SectionVolumes.Csv(SectionVolumes.Compute(Reta, Faixa, _ => 1.0));
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Estaca;Estaca (m);Área de corte (m²)", lines[0]);
        Assert.StartsWith("1+0,00;20,00;10,00;0,00;200,00;0,00;200,00", lines[2]);
        Assert.Contains("Total;;;;1000,00;0,00;1000,00", csv);
        Assert.Contains("sem correção de curvatura", csv);
    }

    [Fact]
    public void EixoDegenerado_SemSecoes()
    {
        var v = SectionVolumes.Compute(new Polyline2(new[] { new Vec2(1, 1) }), Faixa, _ => 1.0);
        Assert.Empty(v.Secoes);
        Assert.Equal(0, v.CorteM3);
    }
}
