using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Fase X – pisos da via × terreno projetado (RT-1.2; Prompt Master §6, §16): piso enterrado, terreno dentro do piso, piso
/// suspenso e degrau na borda da seção, conferidos no núcleo pelo <see cref="FloorTerrainCheck"/> – o mesmo que o Autoteste
/// usa no Revit, com o Toposolid projetado.
/// </summary>
public class Fase10PisoTerrenoTests(ITestOutputHelper output)
{
    private static Polygon2 Ret(double x0, double y0, double x1, double y1) => new(new List<Vec2> { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) });

    private static List<AchadoPisoTerreno> Laje(double terreno, double? profundidade = 0.40) =>
        FloorTerrainCheck.Pisos(FloorTerrainCheck.Amostras(new[] { new PisoSobreTerreno("PAV-ASF", "m", Ret(0, 0, 10, 7), _ => 1.0, profundidade) }),
            _ => terreno);

    [Fact]
    public void SobOPiso_CadaProblemaPelaCotaDoTerreno()
    {
        Assert.Empty(Laje(0.60));                                   // no fundo da estrutura (1,00 − 0,40)
        Assert.Empty(Laje(0.615));                                  // dentro da tolerância de 2 cm
        var cobre = Assert.Single(Laje(1.05));
        Assert.Equal(ProblemaPisoTerreno.TerrenoCobrePiso, cobre.Problema);
        Assert.Equal(cobre.Conferidos, cobre.Pontos);
        Assert.Equal(0.05, cobre.PiorDesvio, 9);
        Assert.Contains("enterrado", cobre.Texto);
        Assert.Equal(ProblemaPisoTerreno.TerrenoDentroDoPiso, Assert.Single(Laje(0.80)).Problema);
        var vao = Assert.Single(Laje(0.30));
        Assert.Equal(ProblemaPisoTerreno.VaoSobOPiso, vao.Problema);
        Assert.Equal(-0.30, vao.PiorDesvio, 9);
        // Profundidade desconhecida: só o terreno acima do topo é conferido.
        Assert.Empty(Laje(0.30, null));
        Assert.Equal(ProblemaPisoTerreno.TerrenoCobrePiso, Assert.Single(Laje(1.10, null)).Problema);
    }

    [Fact]
    public void SemTerreno_NadaConferido_EMargemDoContorno()
    {
        Assert.Empty(FloorTerrainCheck.Pisos(FloorTerrainCheck.Amostras(new[] { new PisoSobreTerreno("CALCADA", "m", Ret(0, 0, 4, 2), _ => 0.15, 0.15) }), _ => null));
        // Pontos a menos de 0,3 m do contorno ficam de fora: a 0,2 m do bordo o terreno pode subir para o talude.
        var amostras = FloorTerrainCheck.Amostras(new[] { new PisoSobreTerreno("CALCADA", "m", Ret(0, 0, 4, 2), _ => 0.15, 0.15) }).ToList();
        Assert.NotEmpty(amostras);
        Assert.All(amostras, a => Assert.True(a.Ponto.X >= 0.3 && a.Ponto.X <= 3.7 && a.Ponto.Y >= 0.3 && a.Ponto.Y <= 1.7));
    }

    [Fact]
    public void Bordas_TaludeComecaNoTopo_DegrauEApontado()
    {
        var eixo = new Polyline2(new[] { new Vec2(0, 0), new Vec2(60, 0) });
        double? Topo(Vec2 p) => Math.Abs(p.Y) <= 5 ? 1.0 : null;
        // Corte 1:1 dos dois lados, começando na borda: sem degrau.
        double? Corte(Vec2 p) => Math.Abs(p.Y) <= 5 ? 0.6 : 1.0 + (Math.Abs(p.Y) - 5);
        Assert.Null(FloorTerrainCheck.Bordas(FloorTerrainCheck.BordasDaVia(eixo, 5, 5), Topo, Corte));
        // Terreno 0,5 m abaixo da borda direita: degrau.
        double? Degrau(Vec2 p) => p.Y < -5 ? 0.5 : Corte(p);
        var d = FloorTerrainCheck.Bordas(FloorTerrainCheck.BordasDaVia(eixo, 5, 5), Topo, Degrau);
        Assert.NotNull(d);
        Assert.Equal(ProblemaPisoTerreno.DegrauNaBorda, d!.Problema);
        Assert.Equal(2 * d.Pontos, d.Conferidos);                  // só um dos lados
        Assert.Equal(-0.5, d.PiorDesvio, 6);
        Assert.True(d.Onde.Y < 0);
    }

    public static IEnumerable<object[]> Cenas() => new[]
    {
        new object[] { "ondulado", "isolada", "coletora4", "coletora4" },
        new object[] { "degrau", "cruz", "local2", "local2" },
        new object[] { "vale", "T", "coletora4", "local2" },
        new object[] { "crista", "Y", "arterial", "local2" },
        new object[] { "ruido", "cruz", "arterial", "local2" },
    };

    /// <summary>
    /// Nas cenas de topografia difícil do AL3 (terraplenagem do núcleo e Toposolid simulado), nenhum piso enterrado, suspenso ou
    /// com o terreno dentro, e as bordas externas das vias encostam no terreno: o verificador não acusa o que está certo.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cenas))]
    public void CenasDoAL3_PisosEBordasNoTerreno(string terrain, string kind, string a, string b)
    {
        var sc = TopoScene.Make(terrain, kind, a, b, 90);
        var pisos = new List<PisoSobreTerreno>();
        foreach (var (def, piece) in sc.Parts.Where(p => p.Piece.Elevation < 0.5 && p.Piece.Solid == null && p.Piece.Profile == null && p.Piece.Round == null))
        {
            if (TopoScene.SurfaceOf(sc, def) is not { } zf) continue;
            var top = piece.Elevation + Math.Max(0.001, piece.Thickness);
            var raised = piece.Elevation + piece.Thickness >= 0.05;
            // Fundo esperado como na terraplenagem por camadas: o subleito sob a pista, o fundo da laje sob o resto.
            var depth = raised ? top - Math.Min(0, piece.Elevation) : top + TopoScene.Scene.Subgrade;
            pisos.Add(new PisoSobreTerreno(def.DisplayCode, def.Id, piece.Shape,
                p => TopoScene.PlatformWant(sc, piece, zf, p) is null ? null : zf(p) + top, depth));
        }
        var amostras = FloorTerrainCheck.Amostras(pisos, 2.0).ToList();
        Assert.True(amostras.Count > 200, $"{amostras.Count} ponto(s) sob os pisos");
        var sob = FloorTerrainCheck.Pisos(amostras, p => sc.Terrain.Z(p));
        Assert.True(sob.Count == 0, string.Join("\n", sob.Select(x => x.Texto)));
        // Contra o terreno ORIGINAL (o que a conferência do Autoteste usava por engano): corte e aterro viram "piso enterrado" e
        // "piso suspenso" – o verificador separa o terreno projetado do natural.
        var original = FloorTerrainCheck.Pisos(amostras, p => sc.Original.Z(p));
        Assert.Contains(original, x => x.Problema is ProblemaPisoTerreno.TerrenoCobrePiso or ProblemaPisoTerreno.VaoSobOPiso);

        var zonas = sc.World.Nodes.Select(n => n.Layout.Zone).ToList();
        var pertoDoNo = zonas.Count == 0 ? null : TerrainRebuild.Mask(zonas, 3.0);
        foreach (var (grp, i) in sc.World.Groups.Select((g, i) => (g, i)))
        {
            var pav = grp.OfType<RoadPavementDefinition>().First();
            var mine = pisos.Where(p => grp.Any(d => d.Id == p.Marca)).ToList();
            double? Topo(Vec2 p) => mine.Where(x => x.Contorno.Contains(p)).Select(x => x.Topo(p)).Where(z => z != null).Max();
            var borda = FloorTerrainCheck.Bordas(FloorTerrainCheck.BordasDaVia(sc.World.Roads[i].Axis, pav.TotalLeft, pav.TotalRight, 2.0, 5.0, pertoDoNo),
                Topo, p => sc.Terrain.Z(p), out var conferidos, pav.DisplayCode);
            output.WriteLine($"{terrain} {kind} via {i}: {conferidos} ponto(s) de borda, {(borda == null ? "no terreno" : borda.Texto)}");
            Assert.True(conferidos >= 10, $"via {i}: só {conferidos} ponto(s) de borda conferidos");
            Assert.Null(borda);
        }
    }
}
