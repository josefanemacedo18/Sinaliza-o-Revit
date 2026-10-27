using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 13 do plano – fluxo integrado (Prompt Master §20; Motores §17): via → interseção → terraplenagem → drenagem →
/// quantitativos → validação. No núcleo, a mesma sequência de ações e conferências da etapa "Fluxo integrado" do Autoteste
/// (que roda no Revit); no plugin, a ligação da etapa.
/// </summary>
public class Etapa13IntegradoTests
{
    private static readonly DateTime T0 = new(2026, 10, 27, 9, 0, 0);

    private static RoadPavementDefinition Via(Vec2 a, Vec2 b) => new()
    {
        PathRef = PathReference.FromPoints(new[] { a, b }, 100), GroupId = "g-" + Guid.NewGuid().ToString("N"),
        LeftWidth = 5.2, RightWidth = 5.2, LeftSidewalk = 3, RightSidewalk = 3,
        Output = { Grade = new RoadGrade { DefaultCurve = 60, AdjustTerrain = true, Points = { new GradePoint(0, 2), new GradePoint(230, 4) } } },
    };

    private static List<CopiaGravada> Copias(IEnumerable<MarkingDefinition> defs) => defs.Select(d => new CopiaGravada(d.Id, d.ToJson(), "e-" + d.Id)).ToList();

    private static List<Achado> V15(IEnumerable<MarkingDefinition> defs, RegistroTerraplenagem reg) =>
        ModelValidator.Validate(Copias(defs), terraplenagem: reg).Where(a => a.Codigo == "V15").ToList();

    [Fact]
    public void Fluxo_CadaModuloVeOQueOAnteriorFez()
    {
        // 1) Duas vias que se cruzam e a interseção ligada às duas.
        var leste = Via(new Vec2(0, 0), new Vec2(230, 0));
        var norte = Via(new Vec2(110, -80), new Vec2(110, 80));
        var no = new IntersectionDefinition { RoadIds = { leste.Id, norte.Id }, Node = new Vec2(110, 0) };
        var defs = new List<MarkingDefinition> { leste, norte, no };
        Assert.Equal(new[] { leste.Id, norte.Id, no.Id }, EarthworkRecord.Participantes(defs).Select(d => d.Id));

        // 2) Terraplenagem aplicada: o registro tem as vias e a interseção; nada pendente, nenhuma V15.
        var reg = EarthworkRecord.Gravar(EarthworkRecord.Participantes(defs), 0.4, 1.0, 1.5, T0, "2.19.0");
        Assert.Empty(EarthworkRecord.Comparar(reg, defs));
        Assert.Empty(V15(defs, reg));

        // 3) Grelha na sarjeta da via leste-oeste (face do meio-fio à direita, 60 m antes do cruzamento): lida na via certa; a
        //    caixa escava o terreno e a terraplenagem aplicada não a tem – "nova", só ela.
        var grelha = new DrainageDefinition { Type = TipoDrenagem.GrelhaSarjeta, Position = new Vec2(50, -5.2), Z = 100 };
        defs.Add(grelha);
        var cap = DrenagemDoModelo.Captacoes(defs).Single(c => c.Dispositivo == grelha);
        Assert.Equal(leste.Id, cap.Sarjeta?.ViaId);
        var nova = Assert.Single(EarthworkRecord.Comparar(reg, defs));
        Assert.Equal((grelha.Id, SituacaoTerraplenagem.Nova), (nova.Id, nova.Situacao));
        // Ler do Modelo grava a leitura na grelha sem mudar a assinatura da escavação.
        var assinatura = EarthworkRecord.Assinatura(grelha);
        Assert.NotEmpty(DrenagemDoModelo.Aplicar(new[] { cap }, quando: T0));
        Assert.Equal(assinatura, EarthworkRecord.Assinatura(grelha));
        Assert.Equal(EstadoLeitura.EmDia, DrenagemDoModelo.Estado(DrenagemDoModelo.Captacoes(defs).Single(c => c.Dispositivo == grelha)));
        reg = EarthworkRecord.Gravar(EarthworkRecord.Participantes(defs), 0.4, 1.0, 1.5, T0.AddMinutes(5), "2.19.0");
        Assert.Empty(V15(defs, reg));

        // 5) Editar o talude de corte da via (como no formulário): a terraplenagem fica desatualizada para ela (V15) e o impacto
        //    diz o que acompanha (a interseção) e o que fica por refazer (o terreno).
        leste.Output.Grade!.CutSlope = 1.5;
        var alterada = Assert.Single(EarthworkRecord.Comparar(reg, defs));
        Assert.Equal((leste.Id, SituacaoTerraplenagem.Alterada), (alterada.Id, alterada.Situacao));
        Assert.Contains(leste.Id, Assert.Single(V15(defs, reg)).Ids);
        var impacto = EstadoDosResultados.Impacto(new[] { leste }, defs, reg);
        Assert.Contains(impacto, i => i.Resultado == "Terraplenagem" && i.Ids.SequenceEqual(new[] { leste.Id }));
        Assert.Contains(impacto, i => i.Resultado == "Interseções" && i.Ids.Contains(no.Id));
        // O talude não muda a sarjeta: a captação continua em dia (nada de "Drenagem" no impacto).
        Assert.DoesNotContain(impacto, i => i.Resultado == "Drenagem");
        Assert.Equal(EstadoLeitura.EmDia, DrenagemDoModelo.Estado(DrenagemDoModelo.Captacoes(defs).Single(c => c.Dispositivo == grelha)));
        // Terraplenagem refeita: limpo de novo.
        reg = EarthworkRecord.Gravar(EarthworkRecord.Participantes(defs), 0.4, 1.0, 1.5, T0.AddMinutes(10), "2.19.0");
        Assert.Empty(EarthworkRecord.Comparar(reg, defs));
        Assert.Empty(V15(defs, reg));
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

    private static string Ler(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz(), "src", "SinalizacaoViaria.Revit", "Commands" }.Concat(partes).ToArray()));

    [Fact]
    public void Autoteste_FluxoIntegrado_OpcaoOrdemEEtapasJuntas()
    {
        var at = Ler("AutoTest.cs");
        Assert.Contains("public bool RunIntegrado => Integrado || Full;", at);
        Assert.Contains("() => o.Integrado, v => o.Integrado = v)", at);
        // Depois das obras (o terreno da área B já existe quando a Topografia roda) e antes da edição, das verificações e do fim.
        var ordem = new[] { "if (_opt.Works) Works();", "if (_opt.RunIntegrado) Integrado();", "if (_opt.Editing) Editing();", "if (_opt.RunChecks) Checks();" }
            .Select(x => at.IndexOf(x, StringComparison.Ordinal)).ToList();
        Assert.All(ordem, i => Assert.True(i > 0));
        Assert.Equal(ordem.OrderBy(i => i), ordem);

        var f = Ler("AutoTestSteps5.cs");
        // As cinco etapas, em ordem; da 2ª em diante sempre juntas com a 1ª (o modo rápido não corta o fluxo no meio).
        var etapas = Regex.Matches(f, @"Step\(G, ""(\d)/5 ").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, etapas);
        Assert.Equal(4, Regex.Matches(f, @"\}, always: true\);").Count);
        Assert.Contains("if (_steps.Count == inicio || leste is not { } viaL", f);
        // Sem terreno da Topografia, o fluxo cria o mesmo Toposolid de teste.
        Assert.Contains("Step(G, \"Terreno de teste (o Toposolid com relevo da Topografia)\", TerrenoDeTeste);", f);
        Assert.Contains("Step(\"Topografia\", \"Toposolid de teste com relevo (encosta, 2 morros e um vale)\", TerrenoDeTeste);", Ler("AutoTestSteps2.cs"));
        // Os motores de verdade, os mesmos dos comandos – nenhum atalho do teste.
        foreach (var motor in new[]
                 {
                     "CmdTerraplenagem.Apply(_uidoc,", "EarthworkRecordStore.Load(_doc)", "EarthworkRecord.Comparar(reg, all)", "TerrainService.Quantities(",
                     "DrainageCommand.Fitted(", "DrainageCommand.Place(_uidoc, batch)", "DrenagemDoModelo.Captacoes(", "DrenagemDoModelo.Aplicar(",
                     "CmdQuantitativos.Collect(_doc,", "QuantitativosPorVia.Linhas(items, nomes, earth)", "ModelValidator.Validate(",
                     "EstadoDosResultados.Impacto(",
                 })
            Assert.Contains(motor, f);
    }
}
