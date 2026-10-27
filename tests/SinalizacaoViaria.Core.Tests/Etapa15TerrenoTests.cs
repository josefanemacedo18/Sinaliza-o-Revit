using System.Runtime.CompilerServices;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 15 do plano 2 – item 13 do pedido de 2026-10-27: "o terreno fica comendo pedaços da via, principalmente as rampas e
/// nas interseções". Sob a calçada o terreno fica no fundo da laje, que é o fundo da rampa; o pé da rampa tem milímetros
/// (rebaixamento total: placa de 5 mm) e a triangulação do Toposolid pode passar até 2 cm acima do projeto
/// (<see cref="TerrainRebuild.RefineTolerance"/>). As rampas não entravam na terraplenagem – nem as da via nem as dos nós,
/// cuja geometria tira o corpo delas da calçada. Agora cada rampa tem plataforma própria, 5 cm abaixo do fundo.
/// </summary>
public class Etapa15TerrenoTests
{
    /// <summary>Menor espessura de uma rampa sobre o fundo dela (o pé, junto ao meio-fio).</summary>
    private static double PeDaRampa(RampDefinition r) =>
        r.Type == TipoRampa.RebaixamentoTotal ? Math.Max(0.005, r.LipHeight) : Math.Max(0, r.LipHeight);

    [Fact]
    public void PeDaRampa_SemFolga_CabiaNoErroDaTriangulacao_ComFolgaNao()
    {
        foreach (var r in new[] { new RampDefinition(), new RampDefinition { Type = TipoRampa.RebaixamentoTotal, SidewalkDepth = 3 } })
        {
            // Antes: terreno no fundo da laje = fundo da rampa – o pé fica a menos que o erro aceito na triangulação.
            Assert.True(PeDaRampa(r) < TerrainRebuild.RefineTolerance, $"{r.Type}: o defeito não se reproduz");
            // Agora: o terreno fica a folga abaixo do fundo – acima do erro da triangulação.
            Assert.True(PeDaRampa(r) + InfraRoads.FolgaSobRampas > TerrainRebuild.RefineTolerance + 0.02);
        }
    }

    private static GradeCorridor Calcada()
    {
        // Faixa de 20 m × 10 m na cota 0 (o fundo da laje da calçada), sem talude.
        var c = new GradeCorridor { DaylightLeft = false, DaylightRight = false, Owner = "via" };
        for (var x = 0.0; x <= 20; x += 1) { c.Left.Add(new Vec3(x, 5, 0)); c.Right.Add(new Vec3(x, -5, 0)); }
        return c;
    }

    [Fact]
    public void PlataformaDaRampa_VenceAFaixaDaVia_ESaiDaPista()
    {
        var rampa = new RampDefinition();
        var path = new Polyline2(new[] { new Vec2(10, -2), new Vec2(10, 0) });   // do meio-fio (y = −2) para dentro da calçada
        var fp = RampGenerator.Footprint(rampa, path);
        var pista = new Polygon2(new[] { new Vec2(0, -5), new Vec2(20, -5), new Vec2(20, -2), new Vec2(0, -2) });
        var pads = InfraRoads.RampPads(new[] { (fp, (Func<Vec2, double>)(_ => 0.0)) }, new[] { pista }, "via");
        var pad = Assert.Single(pads);
        Assert.False(pad.Daylight);
        Assert.Equal("via", pad.Owner);
        // A parte sobre a pista (5 cm além da face do meio-fio) sai: ali o terreno já está no fundo da estrutura.
        Assert.True(pad.Area.Area < fp.Area - 0.01);
        Assert.False(pad.Area.Contains(new Vec2(10, -2.02)));

        var res = Grading.Design(new[] { Calcada() }, pads, _ => 0.0, 0, 30, 0.5, 1.0, 1.0);
        Assert.Equal(-InfraRoads.FolgaSobRampas, res.DesignZ(new Vec2(10, -1.5))!.Value, 6);   // sob a rampa
        Assert.Equal(0, res.DesignZ(new Vec2(4, 2))!.Value, 6);                                  // sob a calçada
        // Os pontos da faixa que caem na rampa vão para o Toposolid com a cota da rampa (TerrainRebuild usa DesignZ).
        Assert.Contains(res.Points, p => fp.Contains(p.XY) && Math.Abs(res.DesignZ(p.XY)!.Value + InfraRoads.FolgaSobRampas) < 1e-9);
    }

    [Fact]
    public void SemRampas_NadaMuda()
    {
        Assert.Empty(InfraRoads.RampPads(Array.Empty<(Polygon2, Func<Vec2, double>)>(), Array.Empty<Polygon2>(), "via"));
        var res = Grading.Design(new[] { Calcada() }, Array.Empty<GradePad>(), _ => 0.0, 0, 30, 0.5, 1.0, 1.0);
        Assert.Equal(0, res.DesignZ(new Vec2(10, -1.5))!.Value, 6);
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
    public void Plugin_RampasNaTerraplenagem_EAutotesteConfereRampasENos()
    {
        var ts = File.ReadAllText(Path.Combine(Raiz(), "src", "SinalizacaoViaria.Revit", "Infrastructure", "TerrainService.cs"));
        // Via: rampas do grupo dela ou sobre a calçada dela, fora dos nós.
        Assert.Contains("pads.AddRange(InfraRoads.RampPads(ramps, roadCorr.SelectMany(c => c.Paved).ToList(), road.Id));", ts);
        Assert.Contains("!deNos.Contains(r.Id)", ts);
        // Nó: as rampas filhas entram ANTES da plataforma do nó (a primeira que contém o ponto vale).
        var nos = ts.IndexOf("pads.AddRange(InfraRoads.RampPads(ramps, paved, def.Id));", StringComparison.Ordinal);
        Assert.True(nos > 0 && nos < ts.IndexOf("var pad = new GradePad(u, z0 - depth)", nos, StringComparison.Ordinal));
        var at = File.ReadAllText(Path.Combine(Raiz(), "src", "SinalizacaoViaria.Revit", "Commands", "AutoTestChecks.cs"));
        Assert.Contains("FloorTerrainCheck.Pisos(noRampa, terrain, 0.0)", at);
        Assert.Contains("FloorTerrainCheck.Pisos(noNo, terrain)", at);
    }
}
