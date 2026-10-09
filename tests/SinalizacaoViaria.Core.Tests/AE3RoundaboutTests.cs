using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AE (item 17): rotatórias realistas com 3 a 6 ramos de larguras e ângulos diferentes – ilha central com faixa
/// galgável, ilhas separadoras (físicas ou pintadas) alinhadas ao ramo, linha "dê a preferência" em todas as entradas, faixa
/// de pedestres recuada cerca de um veículo da entrada, setas e linhas de bordo, R-33 e placa de advertência (A-12), sem
/// falhas de modelagem.
/// </summary>
public class AE3RoundaboutTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    public static (Y34TipTests.World W, RoundaboutDefinition D, RoundaboutLayout L, List<MarkingDefinition> Children) Make(TipoRotatoria type,
        IlhaSeparadora style, double[] angles, int[] templates, Action<RoundaboutDefinition>? tune = null)
    {
        var w = new Y34TipTests.World();
        for (int i = 0; i < angles.Length; i++)
        {
            var pr = PathReference.FromPoints(new[] { Vec2.Zero, Y34TipTests.Dir(angles[i]) * 130 }, 0);
            var g = RoadTemplates.All[templates[i]].Create().Build(pr, new OutputSettings(), Cat);
            var axis = new Polyline2(pr.Points);
            foreach (var m in g) if (m.Path != null) w.Paths[m.Id] = axis;
            w.Defs.AddRange(g);
            w.Groups.Add(g);
            w.Roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        var d = new RoundaboutDefinition { Center = Vec2.Zero };
        d.ApplyPreset(type);
        d.SplitterStyle = style;
        tune?.Invoke(d);
        d.Legs = RoundaboutGenerator.LegsFromRoads(d.Center, w.Roads, d.OuterRadius + 25);
        var L = RoundaboutGenerator.Layout(d);
        foreach (var g in w.Groups)
            foreach (var m in g)
                foreach (var cut in RoundaboutGenerator.RoadCuts(d, L, m))
                    m.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = cut.Outer.ToList() });
        w.Defs.Add(d);
        var children = RoundaboutGenerator.Children(d, L, new OutputSettings(), 0);
        foreach (var c in children) { c.GroupId = d.Id; w.Defs.Add(c); }
        return (w, d, L, children);
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var style in new[] { IlhaSeparadora.Fisica, IlhaSeparadora.Pintada })
        {
            yield return new object[] { TipoRotatoria.UmaFaixa, style, new[] { 0.0, 100, 230 }, new[] { 0, 1, 0 } };
            yield return new object[] { TipoRotatoria.UmaFaixa, style, new[] { 0.0, 80, 185, 270 }, new[] { 1, 0, 2, 0 } };
            yield return new object[] { TipoRotatoria.DuasFaixas, style, new[] { 0.0, 65, 140, 215, 290 }, new[] { 0, 1, 0, 1, 0 } };
            yield return new object[] { TipoRotatoria.DuasFaixas, style, new[] { 0.0, 55, 115, 180, 240, 300 }, new[] { 0, 0, 1, 0, 0, 1 } };
        }
    }

    private static Vec2 Mid(LinearMarkingDefinition l) => (l.PathRef.Points[0] + l.PathRef.Points[^1]) / 2;

    /// <summary>Direção principal (momentos de segunda ordem da área) e centroide do polígono.</summary>
    private static (Vec2 C, Vec2 Axis) Principal(Polygon2 p)
    {
        var c = p.Centroid;
        double sxx = 0, syy = 0, sxy = 0;
        var r = p.Outer;
        for (int i = 0; i < r.Count; i++)
        {
            var a = r[i] - c;
            var b = r[(i + 1) % r.Count] - c;
            var cr = a.X * b.Y - b.X * a.Y;
            sxx += cr * (a.X * a.X + a.X * b.X + b.X * b.X);
            syy += cr * (a.Y * a.Y + a.Y * b.Y + b.Y * b.Y);
            sxy += cr * (a.X * b.Y + 2 * a.X * a.Y + 2 * b.X * b.Y + b.X * a.Y);
        }
        var ang = 0.5 * Math.Atan2(2 * sxy / 2, sxx - syy);
        return (c, new Vec2(Math.Cos(ang), Math.Sin(ang)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Rotatoria_realista_em_todas_as_entradas(TipoRotatoria type, IlhaSeparadora style, double[] angles, int[] templates)
    {
        var (w, d, L, ch) = Make(type, style, angles, templates, x => { x.RingArrows = true; x.AdvanceWarning = true; x.DirectionSigns = PlacaSentidoRotatoria.R33NasEntradas; });
        var c = d.Center;
        Assert.Equal(angles.Length, L.Legs.Count);
        Assert.NotEmpty(L.Apron);                                           // faixa galgável em volta da ilha
        var ldp = ch.OfType<LinearMarkingDefinition>().Where(l => l.Code == "LDP").ToList();
        var ftp = ch.OfType<LinearMarkingDefinition>().Where(l => l.Code == "FTP-1").ToList();
        var signs = ch.OfType<SignDefinition>().ToList();
        Assert.Equal(L.Legs.Count, ldp.Count);
        foreach (var g in L.Legs)
        {
            var hw = g.Leg.Width / 2;
            var rou = L.ToOuter(c, g.Dir);
            // Ilha separadora alinhada ao ramo: eixo maior paralelo ao ramo e centrada nele.
            var sp = g.Splitter ?? g.PaintedSplitter;
            if (g.Leg.MedianWidth < 0.5)
            {
                Assert.NotNull(sp);
                Assert.Equal(style == IlhaSeparadora.Pintada, g.PaintedSplitter != null);
                var (pc, ax) = Principal(sp!);
                Assert.True(Math.Abs(ax.Dot(g.Dir)) > Math.Cos(6 * Math.PI / 180), $"ilha do ramo {g.Leg.AngleDeg:0}° desalinhada ({Math.Acos(Math.Abs(ax.Dot(g.Dir))) * 180 / Math.PI:0.0}°)");
                Assert.True(Math.Abs((pc - c).Dot(g.Left)) < 0.6, $"ilha do ramo {g.Leg.AngleDeg:0}° fora do eixo ({(pc - c).Dot(g.Left):0.00} m)");
                Assert.True((pc - c).Dot(g.Dir) > rou, "ilha separadora dentro do anel");
            }
            // "Dê a preferência" na entrada (lado esquerdo do ramo, junto ao anel).
            var mine = ldp.Where(l => Math.Abs((Mid(l) - c).Dot(g.Left)) < hw && (Mid(l) - c).Dot(g.Dir) > 0
                                      && Math.Abs((Mid(l) - c).Normalized().Dot(g.Dir)) > 0.6).ToList();
            var e = Assert.Single(mine);
            Assert.True((Mid(e) - c).Dot(g.Left) > 0, "LDP fora da faixa de entrada");
            // Toda a linha fora do anel e junto dele (em cada ponto, a borda do anel na mesma posição lateral).
            foreach (var p in e.PathRef.Points)
            {
                var edgeAt = L.ToOuter(c + g.Left * (p - c).Dot(g.Left), g.Dir);
                Assert.InRange((p - c).Dot(g.Dir), edgeAt + 0.2, edgeAt + 1.5);
            }
            foreach (var (a, b) in e.PathRef.Points.Zip(e.PathRef.Points.Skip(1)))
            {
                var m = (a + b) / 2;
                Assert.True((m - c).Dot(g.Dir) >= L.ToOuter(c + g.Left * (m - c).Dot(g.Left), g.Dir) + 0.1, $"LDP do ramo {g.Leg.AngleDeg:0}° invade o anel");
            }
            var tl = e.PathRef.Points.Max(p => (p - c).Dot(g.Dir));
            // Faixa de pedestres recuada cerca de um veículo (≈ 5 m [a confirmar]) da linha de entrada.
            var cw = Assert.Single(ftp, f => Math.Abs((Mid(f) - c).Dot(g.Left)) < hw && (Mid(f) - c).Dot(g.Dir) > rou
                                             && Math.Abs((Mid(f) - c).Normalized().Dot(g.Dir)) > 0.8);
            var near = (Mid(cw) - c).Dot(g.Dir) - cw.WidthOverride!.Value / 2;
            Assert.InRange(near - tl, 4.5, 8.0);
            // R-33 e A-12 voltadas para quem chega.
            Assert.Contains(signs, s => s.Code == "R-33" && s.Direction.Dot(-g.Dir) > 0.95);
            Assert.Contains(signs, s => s.Code == "A-12" && s.Direction.Dot(-g.Dir) > 0.95 && (s.Position - c).Dot(g.Dir) > rou + 20);
        }
        // Setas no anel (uma por faixa, após cada entrada) e linhas de bordo interna e externa.
        Assert.Equal(L.Legs.Count * d.Lanes, ch.OfType<SymbolMarkingDefinition>().Count(s => s.Code == "IMC"));
        Assert.True(ch.OfType<LinearMarkingDefinition>().Count(l => l.Code == "LBO") >= 2);
        var parts = Y34TipTests.Geometry(w);
        var R = L.Zone.Outer.Max(p => p.DistanceTo(c)) + 6;
        var f = Y34TipTests.FaultsAt(parts, c, R, walks: true);
        Assert.True(f.Count == 0, string.Join("\n", f.Take(12)));
    }

    [Theory]
    [InlineData(TipoRotatoria.Compacta)]
    [InlineData(TipoRotatoria.UmaFaixa)]
    [InlineData(TipoRotatoria.DuasFaixas)]
    [InlineData(TipoRotatoria.Mini)]
    public void Rotatoria_nova_ja_vem_com_setas_R33_e_advertencia(TipoRotatoria type)
    {
        // Sem ajustar nada além do tipo (como a janela faz): setas no anel, R-33 em cada entrada e A-12 antes dela.
        var (_, d, L, ch) = Make(type, IlhaSeparadora.Padrao, new[] { 0.0, 90, 180, 270 }, new[] { 1, 0, 1, 0 });
        Assert.True(d.RingArrows && d.AdvanceWarning);
        Assert.Equal(L.Legs.Count * d.Lanes, ch.OfType<SymbolMarkingDefinition>().Count(s => s.Code == "IMC"));
        Assert.Equal(L.Legs.Count, ch.OfType<SignDefinition>().Count(s => s.Code == "R-33"));
        Assert.Equal(L.Legs.Count, ch.OfType<SignDefinition>().Count(s => s.Code == "A-12"));
        Assert.Equal(L.Legs.Count, ch.OfType<LinearMarkingDefinition>().Count(l => l.Code == "LDP"));
        var n = RoundaboutDefinition.Nova();
        Assert.True(n.RingArrows && n.AdvanceWarning && n.DirectionSigns == PlacaSentidoRotatoria.R33NasEntradas);
        // Rotatória gravada antes (JSON com os valores dela) não muda.
        var old = new RoundaboutDefinition { RingArrows = false, AdvanceWarning = false };
        var back = (RoundaboutDefinition)MarkingDefinition.FromJson(old.ToJson())!;
        Assert.False(back.RingArrows || back.AdvanceWarning);
        Assert.Equal(PlacaSentidoRotatoria.Automatico, back.DirectionSigns);
    }
}
