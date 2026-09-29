using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada T: largura variável, recuos (baias, faixas auxiliares).</summary>
public class V25Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static readonly Polyline2 Axis = new(new[] { Vec2.Zero, new Vec2(100, 0) });

    private static List<MarkingDefinition> Build(RoadSetup s) => s.Build(PathReference.FromPoints(Axis.Points, 0), new OutputSettings(), Cat, axis: Axis);

    /// <summary>Menor y (lado direito) das peças da marca na abscissa x.</summary>
    private static double RightEdgeAt(MarkingGeometry g, double x)
    {
        for (var y = 0.0; y > -20; y -= 0.005)
            if (!g.Pieces.Any(p => p.Shape.Contains(new Vec2(x, y - 0.005)))) return y;
        return double.NaN;
    }

    [Fact]
    public void VariableWidth_CurbFollowsTheSurveyAndSidewalkKeepsWidth()
    {
        var s = RoadTemplates.All[1].Create();                 // coletora: bordo a 7,10 m
        s.LargurasVariaveis.Add(new PontoLargura { Estaca = 0 });
        s.LargurasVariaveis.Add(new PontoLargura { Estaca = 50, BordoDireito = 7.60 });
        s.LargurasVariaveis.Add(new PontoLargura { Estaca = 100, BordoDireito = 7.60 });
        var defs = Build(s);
        var pav = defs.OfType<RoadPavementDefinition>().First();
        Assert.True(pav.HasEdgeVariation);
        var g = MarkingBuilder.Build(pav, Axis, Ctx);
        // Borda do asfalto (sem a sarjeta de 0,30 m, que é faixa própria).
        Assert.Equal(-6.80, RightEdgeAt(g, 0), 2);
        Assert.Equal(-7.30, RightEdgeAt(g, 75), 2);
        Assert.Equal(-7.05, RightEdgeAt(g, 25), 2);
        // Linha de bordo e calçada deslocadas junto com o meio-fio; calçada com a mesma largura.
        var lbo = defs.OfType<LinearMarkingDefinition>().First(l => l.Code == "LBO" && l.Offset < 0);
        Assert.Equal(-0.5, lbo.PathRef.Lateral!.ShiftAt(80, Axis), 2);
        var walk = defs.OfType<LinearMarkingDefinition>().Where(l => l.Code == "CALCADA" && l.Offset < 0).ToList();
        Assert.All(walk, w => Assert.True(Math.Abs(w.PathRef.Lateral!.WidenAt(80, Axis)) < 1e-6));
        // O eixo e a faixa interna não se movem.
        var center = defs.OfType<LinearMarkingDefinition>().First(l => Math.Abs(l.Offset) < 0.01);
        Assert.Null(center.PathRef.Lateral);
        // Interseção usa a largura local.
        var local = pav.Local(80, Axis);
        Assert.Equal(7.60, local.RightWidth, 2);
        Assert.Equal(pav.RightSidewalk, local.RightSidewalk, 2);
    }

    [Fact]
    public void VariableWidth_LotLineNarrowsTheSidewalk()
    {
        var s = RoadTemplates.All[1].Create();
        var (_, lot, _) = s.Nominal(false);
        s.LargurasVariaveis.Add(new PontoLargura { Estaca = 0, AlinhamentoDireito = lot - 0.8 });
        var defs = Build(s);
        var pav = defs.OfType<RoadPavementDefinition>().First();
        Assert.Equal(pav.RightSidewalk - 0.8, pav.Local(30, Axis).RightSidewalk, 2);
        Assert.Equal(pav.RightWidth, pav.Local(30, Axis).RightWidth, 2);
        var free = defs.OfType<LinearMarkingDefinition>().Where(l => l.Code == "CALCADA" && l.Offset < 0).OrderByDescending(l => l.WidthOverride).First();
        Assert.Equal(-0.8, free.PathRef.Lateral!.WidenAt(30, Axis), 2);
        var geo = MarkingBuilder.Build(free, Axis, Ctx);
        Assert.NotEmpty(geo.Pieces);
    }

    [Fact]
    public void BusBay_SetsBackTheCurbKeepsTheLaneAndMarksTheBay()
    {
        var s = RoadTemplates.All[1].Create();
        var r = RecuoVia.Padrao(TipoRecuo.BaiaOnibus, s.Speed);
        r.Estaca = 30;
        r.Profundidade = 2.0;
        s.Recuos.Add(r);
        var defs = Build(s);
        var pav = defs.OfType<RoadPavementDefinition>().First();
        var mid = (r.S1 + r.S2) / 2;
        Assert.Equal(7.10 + r.Profundidade, pav.Local(mid, Axis).RightWidth, 2);
        Assert.Equal(7.10, pav.Local(10, Axis).RightWidth, 2);
        // Calçada recua: o alinhamento não muda.
        Assert.Equal(pav.RightSidewalk - r.Profundidade, pav.Local(mid, Axis).RightSidewalk, 2);
        var lbo = defs.OfType<LinearMarkingDefinition>().First(l => l.Code == "LBO" && l.Offset < 0);
        Assert.Null(lbo.PathRef.Lateral);
        Assert.Single(lbo.Breaks);
        var lg = MarkingBuilder.Build(lbo, Axis, Ctx);
        Assert.DoesNotContain(lg.Pieces, p => p.Shape.Centroid.X > r.Estaca + 1 && p.Shape.Centroid.X < r.S3 - 1);
        var rec = defs.OfType<RecessMarkingDefinition>().Single();
        var rg = MarkingBuilder.Build(rec, Axis, Ctx);
        Assert.Contains(rg.Pieces, p => p.Color == MarkingColor.Amarela);
        Assert.Contains(rg.Pieces, p => p.Color == MarkingColor.Branca);
        Assert.Contains(defs, d => d is SignDefinition { Code: "SAU-ONIBUS" });
        Assert.Contains(defs, d => d is UrbanElementDefinition { Code: "ABRIGO" });
    }

    [Fact]
    public void DecelerationLane_MovesTheEdgeLineAndSeparatesWithLco()
    {
        var s = RoadTemplates.All[7].Create();                 // rodovia com acostamento
        var r = RecuoVia.Padrao(TipoRecuo.FaixaDesaceleracao, 80, 3.5);
        r.Estaca = 10;
        r.Comprimento = 40;
        s.Recuos.Add(r);
        var defs = Build(s);
        var lbo = defs.OfType<LinearMarkingDefinition>().First(l => l.Code == "LBO" && l.Offset < 0);
        Assert.Equal(-3.5, lbo.PathRef.Lateral!.ShiftAt(r.S1 + 5, Axis), 2);
        var rg = MarkingBuilder.Build(defs.OfType<RecessMarkingDefinition>().Single(), Axis, Ctx);
        Assert.NotEmpty(rg.Pieces);
    }

    [Fact]
    public void Sidewalk_LevelsRiseTowardTheLotAndAccessStripRamps()
    {
        var s = RoadTemplates.All[1].Create();
        var walk = s.Right.Last(e => e.Tipo == TipoElementoSecao.Calcada);
        walk.Largura = 3.5;
        walk.FaixaAcesso = 0.8;
        walk.InclinacaoTransversal = 2;
        walk.NiveisAlinhamento = NivelAlinhamento.Parse("0:0,20; 40:0,40; 60:0,40; 100:0,20");
        var defs = Build(s);
        var strips = defs.OfType<LinearMarkingDefinition>().Where(l => l.Offset < 0 && l.LevelProfile.Count > 0).ToList();
        Assert.True(strips.Count >= 2);
        var access = strips.OrderBy(l => l.Offset).First();              // a mais externa (junto ao lote)
        var at50 = SidewalkLevels.Function(access, Axis, Cat)!;
        var lot = at50(new Vec2(50, access.Offset - access.WidthOverride!.Value / 2 + 0.01));
        Assert.Equal(0.40 - 0.15, lot, 2);                               // 0,40 acima da pista = 0,25 acima do topo padrão
        var free = strips.OrderBy(l => l.Offset).Skip(1).First();
        var f = SidewalkLevels.Function(free, Axis, Cat)!;
        var inner = f(new Vec2(50, free.Offset + free.WidthOverride!.Value / 2 - 0.01));
        var outer = f(new Vec2(50, free.Offset - free.WidthOverride!.Value / 2 + 0.01));
        Assert.True(outer > inner);                                      // sobe em direção ao lote (2 %)
        Assert.True((outer - inner) / free.WidthOverride!.Value < 0.031);
        // Faixa de acesso vencendo mais de 8,33 % gera aviso.
        Assert.Contains(s.Warnings, w => w.Contains("8,33"));
        var geo = MarkingBuilder.Build(access, Axis, Ctx);
        Assert.All(geo.Pieces, p => Assert.True(p.Thickness > 0.2));   // laje mais espessa sob o topo elevado
    }

    [Fact]
    public void Estaca_ParsesTwentyMeterStations()
    {
        Assert.Equal(110, PontoLargura.ParseEstaca("5+10"));
        Assert.Equal(110.5, PontoLargura.ParseEstaca("110,5"));
        Assert.Equal("5+10,00", PontoLargura.FormatEstaca(110));
    }
}

public class V25IntersectionTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static IntersectionDemo.Scene Scene(int main, int minor, LinhaContinuidade mode = LinhaContinuidade.Automatica) =>
        IntersectionDemo.Create(new IntersectionDefinition { Control = ControleIntersecao.Pare, Crosswalks = true, StopLines = true, CornerRadius = 8, ContinuityLine = mode },
            Cat, main, minor, 90, false, 70);

    [Fact]
    public void ArterialWithCollector_GetsDashedContinuityLineAcrossTheMouth()
    {
        var s = Scene(2, 1);                                   // avenida (arterial) × coletora
        var lco = EdgeLco(s);
        Assert.NotEmpty(lco);
        var main = s.Layout.Roads[s.Layout.Main];
        foreach (var l in lco)
        {
            var pts = l.PathRef.Points;
            var len = new Polyline2(pts).Length;
            // Atravessa a boca: pelo menos a largura da coletora entre meios-fios.
            Assert.True(len > 10, $"LCO com {len:0.0} m");
            // Fica junto ao bordo da principal.
            var d = Math.Abs(main.Axis.Project(pts[pts.Count / 2]).Signed);
            Assert.InRange(d, Math.Min(main.Def.LeftWidth, main.Def.RightWidth) - 0.8, Math.Max(main.Def.LeftWidth, main.Def.RightWidth));
        }
        var geo = IntersectionDemo.Build(s, new BuildContext { Catalog = Cat }, false);
        Assert.NotEmpty(geo.Pieces);
    }

    [Fact]
    public void LocalStreets_DoNotGetContinuityLineUnlessAsked()
    {
        Assert.Empty(EdgeLco(Scene(0, 0)));
        Assert.NotEmpty(EdgeLco(Scene(0, 0, LinhaContinuidade.Sempre)));
        Assert.Empty(EdgeLco(Scene(2, 1, LinhaContinuidade.Nunca)));
    }

    /// <summary>LCO do bordo da principal (a do miolo – eixo e faixas – fica longe do bordo).</summary>
    private static List<LinearMarkingDefinition> EdgeLco(IntersectionDemo.Scene s)
    {
        var main = s.Layout.Roads[s.Layout.Main];
        var edge = Math.Min(main.Def.LeftWidth, main.Def.RightWidth) - 1.0;
        return s.Definitions.OfType<LinearMarkingDefinition>().Where(l => l.Code == "LCO")
            .Where(l => Math.Abs(main.Axis.Project(l.PathRef.Points[l.PathRef.Points.Count / 2]).Signed + l.Offset) > edge).ToList();
    }
}
