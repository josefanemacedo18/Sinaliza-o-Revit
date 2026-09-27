using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada I: drenagem encaixada na via, núcleo 3D (cascas e primitivas), obras refinadas, nós viários e terreno nativo.</summary>
public class V20Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static Polyline2 Straight(double len) => new(new[] { new Vec2(0, 0), new Vec2(len, 0) });

    private static double SignedVolume(Polyhedron p)
    {
        double v = 0;
        foreach (var f in p.Faces)
            for (int i = 1; i + 1 < f.Count; i++) v += f[0].Dot(f[i].Cross(f[i + 1]));
        return v / 6;
    }

    /// <summary>Cada aresta de uma casca fechada é percorrida uma vez em cada sentido.</summary>
    private static bool Closed(Polyhedron p)
    {
        var edges = new Dictionary<(Vec3, Vec3), int>();
        foreach (var f in p.Faces)
            for (int i = 0; i < f.Count; i++)
            {
                var a = f[i];
                var b = f[(i + 1) % f.Count];
                edges[(a, b)] = edges.GetValueOrDefault((a, b)) + 1;
            }
        return edges.All(e => edges.GetValueOrDefault((e.Key.Item2, e.Key.Item1)) == e.Value);
    }

    // ------------------------------------------------------------------ núcleo 3D

    [Fact]
    public void Sweep_NonConvexSectionAlongCurve_IsOneClosedPositiveShell()
    {
        var geo = new MarkingGeometry();
        var arc = new Polyline2(CurveTools.Arc(Vec2.Zero, 40, 0, Math.PI / 2, 0.01));
        // Perfil em "I" (não convexo).
        var ibeam = new[]
        {
            new SectionPt(-0.4, 0), new SectionPt(0.4, 0), new SectionPt(0.4, 0.2), new SectionPt(0.1, 0.2), new SectionPt(0.1, 1.2),
            new SectionPt(0.4, 1.2), new SectionPt(0.4, 1.4), new SectionPt(-0.4, 1.4), new SectionPt(-0.4, 1.2), new SectionPt(-0.1, 1.2),
            new SectionPt(-0.1, 0.2), new SectionPt(-0.4, 0.2),
        };
        SolidSweep.Along(geo, arc, _ => ibeam, MarkingColor.Concreto);
        var s = Assert.Single(geo.Pieces).Solid!;
        Assert.True(Closed(s));
        var area = 0.8 * 0.2 * 2 + 0.2 * 1.0;
        Assert.InRange(SignedVolume(s), area * arc.Length * 0.97, area * arc.Length * 1.03);
        // Projeção em planta acompanha a curva (não é a envoltória convexa do quarto de círculo).
        Assert.InRange(geo.Pieces[0].Shape.Area, 0.8 * arc.Length * 0.9, 0.8 * arc.Length * 1.1);
    }

    [Fact]
    public void RoundSolid_HollowAndConical_AreClosedAndCarryExactDescriptor()
    {
        var geo = new MarkingGeometry();
        SolidSweep.AddRound(geo, new Vec3(0, 0, 0), new Vec3(0, 0, 2), 1.0, 1.0, MarkingColor.Concreto, innerA: 0.8, innerB: 0.8);
        SolidSweep.AddRound(geo, new Vec3(5, 0, 0), new Vec3(8, 1, 1), 0.5, 0.2, MarkingColor.Metal);
        Assert.Equal(2, geo.Pieces.Count);
        Assert.All(geo.Pieces, p => { Assert.NotNull(p.Round); Assert.True(Closed(p.Solid!)); Assert.True(SignedVolume(p.Solid!) > 0); });
        var tube = geo.Pieces[0].Solid!;
        Assert.InRange(SignedVolume(tube), Math.PI * (1 - 0.64) * 2 * 0.95, Math.PI * (1 - 0.64) * 2 * 1.01);
    }

    // ------------------------------------------------------------------ drenagem

    [Theory]
    [InlineData(TipoDrenagem.BocaDeLoboSimples)]
    [InlineData(TipoDrenagem.BocaDeLoboDupla)]
    [InlineData(TipoDrenagem.BocaDeLoboGrelha)]
    [InlineData(TipoDrenagem.BocaDeLoboCombinada)]
    [InlineData(TipoDrenagem.GrelhaSarjeta)]
    public void Drainage_CurbInlets_ReplaceGutterCurbAndSidewalk(TipoDrenagem type)
    {
        var d = new DrainageDefinition { Type = type, Position = Vec2.Zero, Along = Vec2.UnitX, SidewalkLeft = true };
        d.ApplyDefaults();
        var g = MarkingBuilder.Build(d, null, Ctx);
        Assert.All(g.Pieces, p => Assert.True(Closed(p.Solid!), $"{p.Layer} aberto"));
        // Rebaixo: superfície junto à guia abaixo do pavimento (−depressão) e rente a ele na borda externa.
        var apron = g.Pieces.Where(p => p.Layer == "REBAIXO").SelectMany(p => p.Solid!.Faces.SelectMany(f => f)).ToList();
        if (d.Depression > 0) Assert.InRange(apron.Where(v => v.Y > -0.02 && v.Z > -d.Depression - 0.05).Min(v => v.Z), -d.Depression - 1e-6, -d.Depression + 1e-6);
        Assert.InRange(apron.Max(v => v.Z), -1e-6, 0.01);
        // Meio-fio refeito no trecho, com o topo na altura da guia e descendo até o rebaixo.
        var curb = g.Pieces.Where(p => p.Layer == "MEIO-FIO").SelectMany(p => p.Solid!.Faces.SelectMany(f => f)).ToList();
        Assert.Equal(d.CurbHeight, curb.Max(v => v.Z), 3);
        Assert.True(curb.Min(v => v.Z) < -d.Depression);
        // Área de recorte cobre o rebaixo, a guia e (bocas na guia) a laje na calçada.
        var fp = DrainageGenerator.Footprint(d, null);
        Assert.Contains(fp, f => f.Contains(new Vec2(0, -0.1)));
        Assert.Contains(fp, f => f.Contains(new Vec2(0, d.CurbWidth / 2)));
        if (type is TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla or TipoDrenagem.BocaDeLoboCombinada)
        {
            Assert.Contains(g.Pieces, p => p.Layer == "GUIA-CHAPEU");
            Assert.Contains(fp, f => f.Contains(new Vec2(0, d.CurbWidth + 0.3)));
            // Laje e tampa rentes à calçada.
            Assert.Equal(d.CurbHeight, g.Pieces.Where(p => p.Layer is "LAJE" or "TAMPA").Max(p => p.Solid!.MaxZ), 3);
        }
        if (type is TipoDrenagem.BocaDeLoboGrelha or TipoDrenagem.GrelhaSarjeta or TipoDrenagem.BocaDeLoboCombinada)
        {
            // Grelha no fundo do rebaixo, do lado da pista.
            var grate = g.Pieces.Where(p => p.Layer == "GRELHA").ToList();
            Assert.NotEmpty(grate);
            Assert.All(grate, p => Assert.True(p.Shape.Centroid.Y < 0));
            Assert.Equal(-d.Depression, grate.Max(p => p.Solid!.MaxZ), 3);
        }
    }

    [Fact]
    public void Drainage_Manhole_LidFlushWithPavement_AndRoundCut()
    {
        var d = new DrainageDefinition { Type = TipoDrenagem.PocoDeVisita, Position = new Vec2(3, 4) };
        d.ApplyDefaults();
        var g = MarkingBuilder.Build(d, null, Ctx);
        Assert.Contains(g.Pieces, p => p.Layer == "DEGRAU");
        Assert.Equal(0, g.Pieces.Where(p => p.Layer == "TAMPAO").Max(p => p.Solid!.MaxZ), 3);
        Assert.Contains(g.Pieces, p => p.Round is { InnerA: > 0 });
        var fp = Assert.Single(DrainageGenerator.Footprint(d, null));
        Assert.InRange(fp.Area, Math.PI * 0.4 * 0.4 * 0.95, Math.PI * 0.42 * 0.42 * 1.05);
    }

    [Theory]
    [InlineData(EstiloGrelha.Barras)]
    [InlineData(EstiloGrelha.Malha)]
    [InlineData(EstiloGrelha.Fendas)]
    public void Drainage_FloorGrateStyles_FlushTop(EstiloGrelha style)
    {
        var d = new DrainageDefinition { Type = TipoDrenagem.GrelhaQuadrada, GrateStyle = style };
        d.ApplyDefaults();
        var g = MarkingBuilder.Build(d, null, Ctx);
        Assert.InRange(g.Pieces.Where(p => p.Layer == "GRELHA").Max(p => p.Solid!.MaxZ), -0.003, 0.0001);
        Assert.Equal(0, g.Pieces.Where(p => p.Layer == "COLARINHO").Max(p => p.Solid!.MaxZ), 6);
    }

    [Fact]
    public void Drainage_DemoCutsContext()
    {
        var d = new DrainageDefinition { Type = TipoDrenagem.BocaDeLoboCombinada };
        d.ApplyDefaults();
        var g = DrainageGenerator.Demo(d);
        // A calçada de exemplo foi recortada sobre a caixa (a laje do dispositivo ocupa o lugar).
        var walk = g.Pieces.Where(p => p.Layer == "CALCADA").Sum(p => p.Shape.Area);
        Assert.True(walk < 8 * 2.6 - 0.5);
        Assert.Contains(g.Pieces, p => p.Layer == "LAJE");
    }

    [Fact]
    public void CurbFinder_FindsRoadFaces_OnBothSides_AndSeries()
    {
        var setup = new RoadSetup
        {
            Right = { new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 }, new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = 3.0 } },
            Left = { new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 }, new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = 3.0 } },
        };
        var pr = PathReference.FromPoints(new List<Vec2> { new(0, 0), new(100, 0) }, 0);
        var sources = new List<CurbSource>();
        foreach (var d in setup.Build(pr, new OutputSettings(), Cat))
            sources.Add(new CurbSource(MarkingBuilder.Build(d, Straight(100), Ctx), 2.0, d.Id, d is LinearMarkingDefinition l && l.Code.StartsWith("MEIO-FIO")));
        var faces = CurbFinder.Faces(sources);
        Assert.True(faces.Count >= 2);
        var hit = CurbFinder.Nearest(faces, new Vec2(40, -3.4))!;
        Assert.NotNull(hit);
        Assert.Equal(-3.5, hit.Position.Y, 2);
        // Calçada à esquerda do sentido da face.
        Assert.True((hit.Position + hit.Along.PerpLeft * 0.5).Y < -3.5);
        Assert.Equal(0.15, hit.Face.CurbHeight, 3);
        Assert.Equal(2.0, hit.Face.BaseZ, 6);
        Assert.InRange(hit.Face.GutterWidth, 0.25, 0.35);
        var up = CurbFinder.Nearest(faces, new Vec2(40, 3.4))!;
        Assert.Equal(3.5, up.Position.Y, 2);
        var series = CurbFinder.Series(hit.Face, new Vec2(10, -3.5), new Vec2(90, -3.5), 40);
        Assert.Equal(3, series.Count);
        Assert.Equal(new[] { 10.0, 50.0, 90.0 }, series.Select(h => Math.Round(h.Position.X, 3)).OrderBy(x => x));
    }
}
