using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 3 do plano – diagnóstico da superfície do terreno (Prompt Master §6; Motores §6): dados insuficientes, coordenadas
/// inválidas, cotas diferentes no mesmo ponto, triângulos sem área, declividade acima do limite informado (sem limite, não
/// conferida) e consultas fora da superfície.
/// </summary>
public class Etapa03TerrenoTests
{
    private static TerrainMesh Plano(double z = 10, double lado = 20, double passo = 5) =>
        TerrainMesh.FromFunction(Vec2.Zero, new Vec2(lado, lado), passo, p => z + 0.02 * p.X);

    private static List<ProblemaTerreno> Tipos(List<AchadoTerreno> a) => a.Select(x => x.Tipo).ToList();

    [Fact]
    public void SuperficieBoa_SemAchados()
    {
        var t = Plano();
        Assert.Empty(TerrainDiagnostics.Diagnosticar(t));
        Assert.Empty(TerrainDiagnostics.Diagnosticar(t, 0.10));                                  // 2 % < 10 %
        Assert.Empty(TerrainDiagnostics.Diagnosticar(t, null, new[] { new Vec2(5, 5), new Vec2(19, 1) }));
    }

    [Fact]
    public void SemSuperficie_OuPontosNumaReta_DadosInsuficientes()
    {
        Assert.Equal(ProblemaTerreno.DadosInsuficientes, Assert.Single(TerrainDiagnostics.Diagnosticar(null)).Tipo);
        Assert.Equal(ProblemaTerreno.DadosInsuficientes, Assert.Single(TerrainDiagnostics.Diagnosticar(new TerrainMesh())).Tipo);
        var reta = new TerrainMesh();
        reta.Add(new Vec3(0, 0, 1), new Vec3(5, 0, 2), new Vec3(10, 0, 3));
        var r = TerrainDiagnostics.Diagnosticar(reta);
        Assert.Contains(ProblemaTerreno.DadosInsuficientes, Tipos(r));
        Assert.Contains(ProblemaTerreno.TrianguloDegenerado, Tipos(r));
        Assert.Contains("numa reta", r.First(x => x.Tipo == ProblemaTerreno.DadosInsuficientes).Texto);
    }

    [Fact]
    public void MesmoPontoComCotasDiferentes_EDegrauVertical()
    {
        var t = Plano();
        // Dois triângulos que dividem o vértice (20; 20), um com a cota 0,5 m acima: dado inconsistente ou paredão.
        t.Add(new Vec3(20, 20, 10.9), new Vec3(25, 20, 11), new Vec3(25, 25, 11));
        var a = Assert.Single(TerrainDiagnostics.Diagnosticar(t), x => x.Tipo == ProblemaTerreno.CotasDiferentesNoMesmoPonto);
        Assert.Equal(1, a.Quantidade);
        Assert.Contains("0,50 m", a.Texto);
        Assert.Equal(new Vec2(20, 20), a.Onde);
        // 5 mm de diferença é ruído de arredondamento: não conta.
        var t2 = Plano();
        t2.Add(new Vec3(20, 20, 10.405), new Vec3(25, 20, 10.5), new Vec3(25, 25, 10.5));
        Assert.DoesNotContain(TerrainDiagnostics.Diagnosticar(t2), x => x.Tipo == ProblemaTerreno.CotasDiferentesNoMesmoPonto);
    }

    [Fact]
    public void CoordenadaInvalida_ETrianguloSemArea()
    {
        var t = Plano();
        t.Add(new Vec3(30, 30, double.NaN), new Vec3(35, 30, 1), new Vec3(35, 35, 1));
        t.Add(new Vec3(40, 40, 1), new Vec3(40.005, 40, 1), new Vec3(40.01, 40, 1.1));
        var r = TerrainDiagnostics.Diagnosticar(t);
        Assert.Equal(1, r.Single(x => x.Tipo == ProblemaTerreno.CoordenadaInvalida).Quantidade);
        Assert.Equal(1, r.Single(x => x.Tipo == ProblemaTerreno.TrianguloDegenerado).Quantidade);
    }

    [Fact]
    public void Declividade_SoComLimiteInformado()
    {
        var t = TerrainMesh.FromFunction(Vec2.Zero, new Vec2(20, 20), 5, p => p.X < 10 ? 0.05 * p.X : 0.5 + 0.6 * (p.X - 10));
        Assert.DoesNotContain(TerrainDiagnostics.Diagnosticar(t), x => x.Tipo == ProblemaTerreno.DeclividadeAcimaDoLimite);
        var a = Assert.Single(TerrainDiagnostics.Diagnosticar(t, 0.30), x => x.Tipo == ProblemaTerreno.DeclividadeAcimaDoLimite);
        Assert.Contains("30 %", a.Texto);
        Assert.Contains("60 %", a.Texto);
        Assert.True(a.Onde!.Value.X > 10);
    }

    [Fact]
    public void ConsultasForaDaSuperficie_SaoExtrapolacao()
    {
        var a = Assert.Single(TerrainDiagnostics.Diagnosticar(Plano(), null, new[] { new Vec2(5, 5), new Vec2(30, 5), new Vec2(-1, 3) }));
        Assert.Equal(ProblemaTerreno.ForaDaSuperficie, a.Tipo);
        Assert.Equal(2, a.Quantidade);
        Assert.Equal(new Vec2(30, 5), a.Onde);
    }
    // ------------------------------------------------------------------ V14 no Validar Modelo

    private static RoadPavementDefinition Via(double x0, double x1, bool molda = true)
    {
        var r = new RoadPavementDefinition { PathRef = PathReference.FromPoints(new[] { new Vec2(x0, 10), new Vec2(x1, 10) }, 0), GroupId = "g" };
        r.Output.Grade = new RoadGrade { AdjustTerrain = molda };
        return r;
    }

    private static List<CopiaGravada> Copias(params MarkingDefinition[] defs) => defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e")).ToList();

    [Fact]
    public void V14_ViaQueMoldaOTerrenoForaDaSuperficie_EDeclividadeSoComLimite()
    {
        var via = Via(5, 40);                                   // o terreno vai até x = 20
        var r = ModelValidator.Validate(Copias(via), terreno: Plano());
        var fora = Assert.Single(r, a => a.Codigo == "V14");
        Assert.Equal(Severidade.Aviso, fora.Severidade);
        Assert.Equal("Topografia", fora.Modulo);
        Assert.Contains("fora da superfície", fora.Mensagem);
        Assert.Equal(new[] { via.Id }, fora.Ids);
        Assert.Contains("ampliar o levantamento", fora.AcaoRecomendada);
        // Via que não molda o terreno: não é consulta (pode passar fora dele de propósito).
        Assert.DoesNotContain(ModelValidator.Validate(Copias(Via(5, 40, molda: false)), terreno: Plano()), a => a.Codigo == "V14");

        var ingreme = TerrainMesh.FromFunction(Vec2.Zero, new Vec2(20, 20), 5, p => 0.6 * p.X);
        Assert.DoesNotContain(ModelValidator.Validate(Copias(Via(2, 18)), terreno: ingreme), a => a.Codigo == "V14");
        var limite = new GeometricCriteria { DeclividadeMaximaTerreno = 30 };
        var dec = Assert.Single(ModelValidator.Validate(Copias(Via(2, 18)), criterios: limite, terreno: ingreme), a => a.Codigo == "V14");
        Assert.Contains("mais íngremes que 30 %", dec.Mensagem);
        Assert.True(limite.IsEmpty);                            // o limite do terreno não liga o V12
    }

    [Fact]
    public void V14_NaoVerificado_SemTerrenoOuSemLimite()
    {
        var via = Via(2, 18);
        var sem = ModelValidator.Validate(Copias(via), incluirNaoVerificados: true);
        Assert.Contains(sem, a => a.Codigo == "V14" && a.Severidade == Severidade.NaoVerificavel && a.Mensagem.StartsWith("Superfície do terreno não lida"));
        var semLimite = ModelValidator.Validate(Copias(via), terreno: Plano(), incluirNaoVerificados: true);
        Assert.Contains(semLimite, a => a.Codigo == "V14" && a.Severidade == Severidade.NaoVerificavel && a.Mensagem.StartsWith("Declividade máxima do terreno"));
        // Com o terreno e o limite, o V14 roda inteiro: nada não verificado nem problema nesta superfície.
        var completo = ModelValidator.Validate(Copias(via), criterios: new GeometricCriteria { DeclividadeMaximaTerreno = 30 }, incluirNaoVerificados: true,
            terreno: Plano());
        Assert.DoesNotContain(completo, a => a.Codigo == "V14");
    }
}
