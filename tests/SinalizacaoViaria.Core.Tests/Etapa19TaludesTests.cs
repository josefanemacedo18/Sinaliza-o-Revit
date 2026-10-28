using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 19 (itens 5 e 17): taludamento por lado e por trecho – talude fixo (com limite) ou largura fixa entre duas estacas,
/// com transições, na Terraplenagem e na seção do Perfil da via.
/// </summary>
public class Etapa19TaludesTests
{
    private static readonly Polyline2 Axis = new(new[] { new Vec2(0, 0), new Vec2(120, 0) });
    private const double Depth = 0.4;

    /// <summary>Via reta de 10 m (5 + 5) com greide plano a 2,0 m sobre terreno plano na cota 0: aterro dos dois lados.</summary>
    private static List<GradeCorridor> Corredores(IReadOnlyList<TrechoTalude>? taludes) =>
        InfraRoads.RoadCorridors(Axis, VerticalProfile.Flat(2.0), -5, 5, 0, 120, Depth, 1.0, 1.5, Array.Empty<MarkingDefinition>(), taludes);

    private static GradingResult Design(IReadOnlyList<TrechoTalude>? taludes, Func<Vec2, double?>? ground = null) =>
        Grading.Design(Corredores(taludes), Array.Empty<GradePad>(), ground ?? (_ => 0.0), 0, 30, 0.25, 1.0, 1.0);

    /// <summary>Distância da borda ao pé do talude lateral (lado + esquerdo) na estaca x.</summary>
    private static double Pe(GradingResult r, double x, bool left)
    {
        var e = r.SlopeEdges.First(se => se.Line.Count > 10 && (se.Line[0].Y > 0) == left && Math.Abs(Math.Abs(se.Line[0].Y) - 5) < 1e-6);
        var k = Enumerable.Range(0, e.Line.Count).OrderBy(i => Math.Abs(e.Line[i].X - x)).First();
        return e.Day[k];
    }

    [Fact]
    public void Talude_por_lado_e_por_trecho_com_transicoes()
    {
        var t = new TrechoTalude { Lado = LadoTalude.Direito, EstacaA = 40, EstacaB = 80, TransicaoA = 10, TransicaoB = 20, Corte = 2.0, Aterro = 3.0 };
        var right = TaludesDaVia.Resolver(1.0, 1.5, new[] { t }, false, 120);
        var left = TaludesDaVia.Resolver(1.0, 1.5, new[] { t }, true, 120);
        Assert.Equal(3.0, right(60).Aterro, 9);
        Assert.Equal(2.0, right(60).Corte, 9);
        Assert.Equal(2.25, right(35).Aterro, 9);   // meio da transição de entrada
        Assert.Equal(2.25, right(90).Aterro, 9);   // meio da transição de saída
        Assert.Equal(1.5, right(20).Aterro, 9);
        Assert.Equal(1.5, left(60).Aterro, 9);
        // Na terraplenagem: aterro de 1,60 m (greide 2,0 − estrutura 0,4) – pé a 1,6 × 3 = 4,8 m à direita no trecho, 2,4 m fora dele e do outro lado.
        var r = Design(new[] { t });
        Assert.InRange(Pe(r, 60, false), 4.8 - 0.05, 4.8 + 0.05);
        Assert.InRange(Pe(r, 10, false), 2.4 - 0.05, 2.4 + 0.05);
        Assert.InRange(Pe(r, 60, true), 2.4 - 0.05, 2.4 + 0.05);
        Assert.InRange(Pe(r, 35, false), 1.6 * 2.25 - 0.05, 1.6 * 2.25 + 0.05);
        // A superfície do talude no trecho segue 3 : 1.
        Assert.InRange(r.SlopeZ(new Vec2(60, -5 - 3))!.Value, 1.6 - 1.0 - 0.02, 1.6 - 1.0 + 0.02);
        // Volume de aterro maior que sem o trecho.
        var r0 = Design(null);
        Grading.Volumes(r, _ => 0.0, 0.5);
        Grading.Volumes(r0, _ => 0.0, 0.5);
        Assert.True(r.FillM3 > r0.FillM3 + 40);
    }

    [Fact]
    public void Talude_fixo_com_limite_vira_face_onde_nao_alcanca_o_terreno()
    {
        var t = new TrechoTalude { Lado = LadoTalude.Esquerdo, EstacaA = 0, EstacaB = null, Aterro = 1.5, Corte = 1.0, Limite = 1.0 };
        var r = Design(new[] { t });
        // O aterro precisaria de 2,4 m: a 1,0 m da borda o talude para (cota 1,6 − 1/1,5) e o terreno fica natural além.
        Assert.InRange(r.SlopeZ(new Vec2(60, 5.5))!.Value, 1.6 - 0.5 / 1.5 - 0.02, 1.6 - 0.5 / 1.5 + 0.02);
        Assert.Null(r.SlopeZ(new Vec2(60, 5 + 1.5)));
        Assert.Contains(r.Points, p => Math.Abs(p.X - 60) < 1.1 && Math.Abs(p.Y - 6.0) < 1e-3 && Math.Abs(p.Z - (1.6 - 1.0 / 1.5)) < 1e-3);
        Assert.Contains(r.Points, p => Math.Abs(p.X - 60) < 1.1 && Math.Abs(p.Y - (6.0 + Grading.WallGap)) < 1e-3 && Math.Abs(p.Z) < 1e-6);
        // O lado direito não tem limite: pé a 2,4 m.
        Assert.InRange(Pe(r, 60, false), 2.4 - 0.05, 2.4 + 0.05);
        // Onde o talude alcança o terreno dentro do limite nada muda (terreno a 1,4 m: aterro de 0,2 m, pé a 0,3 m).
        var high = Design(new[] { t }, _ => 1.4);
        Assert.InRange(Pe(high, 60, true), 0.3 - 0.05, 0.3 + 0.05);
    }

    [Fact]
    public void Largura_fixa_leva_o_talude_ao_terreno_na_largura_pedida()
    {
        var t = new TrechoTalude { Lado = LadoTalude.Ambos, EstacaA = 30, EstacaB = 90, TransicaoA = 10, TransicaoB = 10, Metodo = MetodoTalude.LarguraFixa, Largura = 4.0 };
        var spec = TaludesDaVia.Resolver(1.0, 1.5, new[] { t }, true, 120)(60);
        Assert.Equal(1.0, spec.PesoLargura, 9);
        var (_, fill, lim) = spec.NoVertice(new Vec2(60, 5), 1.6, Vec2.UnitY, _ => 0.0);
        Assert.Equal(4.0 / 1.6, fill, 9);
        Assert.Equal(4.0, lim, 9);
        var r = Design(new[] { t });
        Assert.InRange(Pe(r, 60, true), 4.0 - 0.05, 4.0 + 0.05);
        Assert.InRange(Pe(r, 60, false), 4.0 - 0.05, 4.0 + 0.05);
        Assert.InRange(r.SlopeZ(new Vec2(60, 5 + 2))!.Value, 0.8 - 0.02, 0.8 + 0.02);
        // Na transição (peso 0,5): inclinação entre 1,5 e 2,5.
        var half = TaludesDaVia.Resolver(1.0, 1.5, new[] { t }, true, 120)(25);
        Assert.Equal(0.5, half.PesoLargura, 9);
        Assert.InRange(Pe(r, 25, true), 1.6 * 2.0 - 0.06, 1.6 * 2.0 + 0.06);
        // Em corte (terreno 3 m acima): a largura fixa também manda – 4 m para subir 1,4 m.
        var cut = Design(new[] { t }, _ => 3.0);
        Assert.InRange(Pe(cut, 60, true), 4.0 - 0.05, 4.0 + 0.05);
    }

    [Fact]
    public void Secao_do_perfil_usa_os_mesmos_taludes()
    {
        var t = new TrechoTalude { Lado = LadoTalude.Direito, EstacaA = 40, EstacaB = 80, Aterro = 3.0 };
        var grade = new RoadGrade { Points = { new(0, 1.6), new(120, 1.6) } };
        var tipo = new SecaoTipo(5, 5, 1.0, 1.5, Trechos: new[] { t });
        var sec = EstacasTerraplenagem.Secao(Axis, 60, grade, tipo, _ => 0.0);
        // Pé à direita a 1,6 × 3 = 4,8 m da borda; à esquerda 2,4 m.
        Assert.InRange(sec.Projeto[0].X, -(5 + 4.8) - 0.05, -(5 + 4.8) + 0.05);
        Assert.InRange(sec.Projeto[^1].X, 5 + 2.4 - 0.05, 5 + 2.4 + 0.05);
        // Área de aterro: plataforma 10 × 1,6 + triângulos 1,6 × 4,8 / 2 + 1,6 × 2,4 / 2.
        Assert.InRange(sec.AreaAterro, 16 + 3.84 + 1.92 - 0.1, 16 + 3.84 + 1.92 + 0.1);
        // Com limite: face vertical no limite.
        var lim = new SecaoTipo(5, 5, 1.0, 1.5, Trechos: new[] { new TrechoTalude { Lado = LadoTalude.Esquerdo, Limite = 1.0 } });
        var s2 = EstacasTerraplenagem.Secao(Axis, 60, grade, lim, _ => 0.0);
        Assert.False(s2.SemEncontroEsquerdo);
        Assert.Equal(6.0, s2.Projeto[^1].X, 6);
        Assert.Equal(0.0, s2.Projeto[^1].Y, 6);
        Assert.Equal(6.0, s2.Projeto[^2].X, 6);
        Assert.InRange(s2.Projeto[^2].Y, 1.6 - 1.0 / 1.5 - 1e-6, 1.6 - 1.0 / 1.5 + 1e-6);
    }

    [Fact]
    public void Taludes_ficam_no_greide_da_via()
    {
        var g = new RoadGrade { Taludes = { new TrechoTalude { Lado = LadoTalude.Esquerdo, EstacaA = 10, EstacaB = 50, Metodo = MetodoTalude.LarguraFixa, Largura = 2.5 } } };
        var c = g.Clone();
        Assert.Single(c.Taludes);
        Assert.NotSame(g.Taludes[0], c.Taludes[0]);
        var json = System.Text.Json.JsonSerializer.Serialize(g, Serialization.JsonConfig.Compact);
        var back = System.Text.Json.JsonSerializer.Deserialize<RoadGrade>(json, Serialization.JsonConfig.Compact)!;
        Assert.Equal(MetodoTalude.LarguraFixa, back.Taludes[0].Metodo);
        Assert.Equal(2.5, back.Taludes[0].Largura);
        Assert.True(TaludesDaVia.Algum(back));
        Assert.False(TaludesDaVia.Algum(new RoadGrade()));
        // Valores inválidos não valem (ficam os da via).
        var bad = TaludesDaVia.Resolver(1.0, 1.5, new[] { new TrechoTalude { Aterro = 0 } }, true, 100)(50);
        Assert.Equal(1.5, bad.Aterro);
        // Sem trecho de talude a terraplenagem não muda (mesmos pés).
        var r0 = Design(null);
        var r1 = Design(Array.Empty<TrechoTalude>());
        Assert.Equal(Pe(r0, 60, true), Pe(r1, 60, true), 9);
        Assert.Null(Corredores(null)[0].TaludesEsquerda);
    }

    private static string Raiz([System.Runtime.CompilerServices.CallerFilePath] string aqui = "")
    {
        var d = new DirectoryInfo(Path.GetDirectoryName(aqui)!);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void Ligacao_no_plugin()
    {
        var root = Raiz();
        var terrain = File.ReadAllText(Path.Combine(root, "src/SinalizacaoViaria.Revit/Infrastructure/TerrainService.cs"));
        Assert.Contains("works, g?.Taludes, opt.MaxDaylight", terrain);
        var profile = File.ReadAllText(Path.Combine(root, "src/SinalizacaoViaria.Revit/Commands/ProfileCommands.cs"));
        Assert.Contains("baseGrade.Taludes = road.Grade.Taludes", profile);
        Assert.Contains("Trechos: road.Grade.Taludes", profile);
        // A terraplenagem depois de gravar o greide usa o pavimento gravado (o lido antes tinha o greide anterior).
        Assert.Contains("var gravado = RoadWorks.RoadByGroup(doc, road.GroupId)?.Pavement ?? road.Pavement;", profile);
        Assert.DoesNotContain("TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { road.Pavement }", profile);
        var works = File.ReadAllText(Path.Combine(root, "src/SinalizacaoViaria.Revit/Commands/RoadWorks.cs"));
        Assert.Contains("TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { RoadByGroup(doc, road.GroupId)?.Pavement ?? road.Pavement }, quiet: true)", works);
        var cmd = File.ReadAllText(Path.Combine(root, "src/SinalizacaoViaria.Revit/Commands/SlopeStretchCommands.cs"));
        Assert.Contains("RoadWorks.SetGrade(uidoc, road.GroupId, grade)", cmd);
        Assert.Contains("TerrainActions.AfterCreate(uidoc, new MarkingDefinition[] { gravado })", cmd);
        Assert.Contains("grade.WithoutWorks.Taludes", cmd);
    }
}
