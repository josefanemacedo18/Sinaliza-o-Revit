using System.Runtime.CompilerServices;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 18 do plano 2 – itens 4 e 21 do pedido de 2026-10-27: seção em qualquer estaca, PIVs editados com o corte e o aterro
/// refeitos na hora, volumes de um trecho de estacas, áreas por estaca e diagrama de massas. As contas são conferidas à mão;
/// a janela (Perfil da via) NÃO foi vista no Revit.
/// </summary>
public class Etapa18EstacasTests
{
    private static readonly Polyline2 Eixo = new(new[] { new Vec2(0, 0), new Vec2(100, 0) });

    /// <summary>Greide em nível na cota <paramref name="z"/>, sem abaulamento.</summary>
    private static RoadGrade Nivel(double z) => new() { Points = { new GradePoint(0, z), new GradePoint(100, z) } };

    private static readonly SecaoTipo Tipo = new(5, 5, CutSlope: 1.0, FillSlope: 1.5);

    [Theory]
    [InlineData(2.0)]
    [InlineData(-2.0)]
    [InlineData(0.5)]
    public void Secao_TerrenoPlano_AreaDoTrapezio(double h)
    {
        // Terreno plano na cota 0; plataforma de 10 m na cota h; taludes de aterro 1,5 : 1 e de corte 1 : 1.
        var sec = EstacasTerraplenagem.Secao(Eixo, 50, Nivel(h), Tipo, _ => 0.0);
        var m = h > 0 ? 1.5 : 1.0;
        var area = Math.Abs(h) * (10 + m * Math.Abs(h));
        Assert.Equal(h > 0 ? area : 0, sec.AreaAterro, 2);
        Assert.Equal(h < 0 ? area : 0, sec.AreaCorte, 2);
        // Os taludes encontram o terreno a m·|h| da borda da plataforma, dos dois lados.
        Assert.False(sec.SemEncontroEsquerdo || sec.SemEncontroDireito);
        Assert.Equal(-(5 + m * Math.Abs(h)), sec.Projeto[0].X, 2);
        Assert.Equal(5 + m * Math.Abs(h), sec.Projeto[^1].X, 2);
        Assert.Equal(area, PolygonOps.TotalArea(h > 0 ? sec.Aterro : sec.Corte), 2);
    }

    [Fact]
    public void Volumes_MediaDasAreas_IguaisAoMetodoDaTerraplenagem()
    {
        // Aterro de 2 m constante em 100 m: 26 m² × 100 m.
        var (v, secs) = EstacasTerraplenagem.Volumes(Eixo, Nivel(2), Tipo, _ => 0.0, 20);
        Assert.Equal(6, secs.Count);                 // 0, 20, …, 100
        Assert.Equal(2600, v.AterroM3, 0);
        Assert.Equal(0, v.CorteM3, 6);
        // A média das áreas agora é uma só conta (FromSections): as seções da Terraplenagem dão o mesmo que antes.
        var tri = SectionVolumes.Compute(Eixo, p => 1.0 + 0.02 * p.X, p => 0.5 + 0.01 * p.X * Math.Sin(p.Y), 20, 8, 0.25);
        var again = SectionVolumes.FromSections(tri.Secoes);
        Assert.Equal(tri.CorteM3, again.CorteM3, 9);
        Assert.Equal(tri.AterroM3, again.AterroM3, 9);
        Assert.Equal(tri.Acumulado, again.Acumulado);
        // Conferência à mão: áreas 10, 20 e 0 a cada 20 m → (10+20)/2·20 + (20+0)/2·20 = 300 + 200.
        var hand = SectionVolumes.FromSections(new[] { new SecaoVolume(0, 10, 0), new SecaoVolume(20, 20, 0), new SecaoVolume(40, 0, 0) });
        Assert.Equal(500, hand.CorteM3, 9);
    }

    [Fact]
    public void Trecho_DeEstacas_SomaOTotal_EInterpolaAsPontas()
    {
        var v = SectionVolumes.FromSections(new[] { new SecaoVolume(0, 10, 0), new SecaoVolume(20, 20, 4), new SecaoVolume(40, 0, 8) });
        var (c, a) = SectionVolumes.Trecho(v, 0, 40);
        Assert.Equal(v.CorteM3, c, 9);
        Assert.Equal(v.AterroM3, a, 9);
        var p1 = SectionVolumes.Trecho(v, 0, 13);
        var p2 = SectionVolumes.Trecho(v, 13, 40);
        Assert.Equal(v.CorteM3, p1.Corte + p2.Corte, 9);
        Assert.Equal(v.AterroM3, p1.Aterro + p2.Aterro, 9);
        // De 10 a 20: área de corte 15 → 20 (interpolada em 10) → (15 + 20)/2 · 10 = 175.
        Assert.Equal(175, SectionVolumes.Trecho(v, 10, 20).Corte, 9);
        // Fora do eixo: limitado às seções.
        Assert.Equal(v.CorteM3, SectionVolumes.Trecho(v, -50, 500).Corte, 9);
        Assert.Equal(0, SectionVolumes.Trecho(v, 40, 60).Corte, 9);
    }

    [Fact]
    public void Bruckner_ConferidoAMao()
    {
        // Seções a cada 20 m: corte 10, 10, 0 e aterro 0, 0, 10 → acumulados: corte 0, 200, 300; aterro 0, 0, 100.
        var v = SectionVolumes.FromSections(new[] { new SecaoVolume(0, 10, 0), new SecaoVolume(20, 10, 0), new SecaoVolume(40, 0, 10) });
        var m = EstacasTerraplenagem.Bruckner(v, 1.25);
        Assert.Equal(new[] { 0.0, 200.0, 300 - 1.25 * 100 }, m.Select(x => x.Ordenada).ToArray());
        // Corte e aterro que se compensam: a curva volta ao zero no ponto de passagem.
        // Corte de 100 m³ no começo e aterro de 100 m³ depois: a curva sobe, desce e termina no zero (compensado).
        var v2 = SectionVolumes.FromSections(new[] { new SecaoVolume(0, 10, 0), new SecaoVolume(20, 0, 0), new SecaoVolume(40, 0, 5), new SecaoVolume(60, 0, 0) });
        var m2 = EstacasTerraplenagem.Bruckner(v2, 1.0);
        Assert.Equal(new[] { 0.0, 100, 50, 0 }, m2.Select(x => x.Ordenada).ToArray());
        Assert.Empty(EstacasTerraplenagem.PontosDePassagem(m2));
        var v3 = SectionVolumes.FromSections(new[] { new SecaoVolume(0, 10, 0), new SecaoVolume(20, 0, 10), new SecaoVolume(40, 0, 10) });
        var pp = Assert.Single(EstacasTerraplenagem.PontosDePassagem(EstacasTerraplenagem.Bruckner(v3, 1.0)));
        Assert.InRange(pp, 20, 40);
    }

    [Fact]
    public void Pivs_AcrescentarMoverRemover_EOVolumeRefeito()
    {
        var pvis = new List<GradePoint> { new(0, 0), new(100, 0) };
        var com = EstacasTerraplenagem.AcrescentarPiv(pvis, 50, 3, 40);
        Assert.Equal(new[] { 0.0, 50, 100 }, com.Select(p => p.S).ToArray());
        Assert.Equal(40, com[1].Curve);
        // Um PIV a menos de 0,5 m de outro substitui o antigo.
        Assert.Equal(3, EstacasTerraplenagem.AcrescentarPiv(com, 50.3, 4).Count);
        var movido = EstacasTerraplenagem.MoverPiv(com, 2, 60, 2);
        Assert.Equal((60.0, 2.0, 40.0), (movido[1].S, movido[1].Z, movido[1].Curve!.Value));
        Assert.Equal(2, EstacasTerraplenagem.RemoverPiv(com, 2).Count);
        Assert.Equal(2, EstacasTerraplenagem.RemoverPiv(pvis, 1).Count);          // ficam pelo menos dois
        Assert.Equal(3, EstacasTerraplenagem.MoverPiv(com, 9, 0, 0).Count);       // número fora da lista: nada muda
        // Ida e volta pelo texto da janela.
        var txt = EstacasTerraplenagem.TextoPivs(com);
        var lidos = RoadGrade.ParsePvis(txt);
        Assert.Equal(com.Select(p => (p.S, p.Z, p.Curve)), lidos.Select(p => (p.S, p.Z, p.Curve)));
        // Subir o PIV do meio aumenta o aterro sobre o terreno plano: o volume é refeito com o greide novo.
        RoadGrade Greide(List<GradePoint> l) => new() { Points = l.Select(p => new GradePoint(p.S, p.Z, p.Curve)).ToList() };
        var antes = EstacasTerraplenagem.Volumes(Eixo, Greide(EstacasTerraplenagem.AcrescentarPiv(pvis, 50, 1)), Tipo, _ => 0.0).Volumes.AterroM3;
        var depois = EstacasTerraplenagem.Volumes(Eixo, Greide(EstacasTerraplenagem.AcrescentarPiv(pvis, 50, 3)), Tipo, _ => 0.0).Volumes.AterroM3;
        Assert.True(depois > antes + 100, $"{antes} → {depois}");
    }

    [Fact]
    public void Secao_TerrenoInclinado_CorteEAterroNaMesmaSecao()
    {
        // Terreno subindo 20 % para a esquerda; plataforma no zero: corte à esquerda, aterro à direita.
        var sec = EstacasTerraplenagem.Secao(Eixo, 30, Nivel(0), Tipo, p => 0.2 * p.Y);
        Assert.True(sec.AreaCorte > 0 && sec.AreaAterro > 0);
        // Na plataforma: ½ · 5 · 1 de cada lado; o talude de corte (1:1) a partir da borda (1 m abaixo do terreno) encontra o
        // terreno a 1,25 m: + ½ · 1,25 · 1 = 0,625.
        Assert.Equal(2.5 + 0.625, sec.AreaCorte, 1);
        Assert.True(sec.AreaAterro > 2.5);
    }

    [Fact]
    public void Graficos_SecaoAreasEMassas()
    {
        var (v, secs) = EstacasTerraplenagem.Volumes(Eixo, Nivel(0), Tipo, p => 0.2 * p.Y + 0.01 * p.X);
        var gs = EarthworkCharts.Secao(secs[2], "Estaca 2+0,00", 2);
        Assert.Contains(gs.Pieces, p => p.Color == MarkingColor.Vermelha);
        Assert.Contains(gs.Pieces, p => p.Color == MarkingColor.Verde);
        Assert.Contains(gs.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith("Estaca 2+0,00: corte"));
        Assert.Contains(gs.Annotations.OfType<AnnotationText>(), t => t.Text.Contains("exagero vertical 2"));
        var gb = EarthworkCharts.PorEstaca(v);
        Assert.Equal(v.Secoes.Count(s => s.Corte > 1e-6), gb.Pieces.Count(p => p.Color == MarkingColor.Vermelha));
        var m = EstacasTerraplenagem.Bruckner(v, 1.25);
        var gm = EarthworkCharts.Massas(m, 1.25);
        Assert.Contains(gm.Annotations.OfType<AnnotationText>(), t => t.Text.Contains("Brückner") && t.Text.Contains("1,25"));
        Assert.Contains(gm.Pieces, p => p.Color == MarkingColor.Asfalto);
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

    [Fact]
    public void Plugin_JanelaDoPerfil()
    {
        var src = File.ReadAllText(Path.Combine(Raiz(), "src", "SinalizacaoViaria.Revit", "Commands", "ProfileCommands.cs"));
        Assert.Contains("EarthworkCharts.Secao(EstacasTerraplenagem.Secao(", src);
        Assert.Contains("EarthworkCharts.Massas(EstacasTerraplenagem.Bruckner(", src);
        Assert.Contains("SectionVolumes.Trecho(vol, trechoDe, trechoAte)", src);
        Assert.Contains("\"Próxima estaca ▶\"", src);
        Assert.Contains("EstacasTerraplenagem.MoverPiv(Pivs(), pivNum, pivS, pivZ)", src);
        Assert.Contains("EstacasTerraplenagem.RemoverPiv(Pivs(), pivNum)", src);
    }
}
