using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AC-3: extensões de calçada em qualquer ponto da via (reta ou curva), travessia no meio da quadra com as extensões
/// dos dois lados alinhadas e extensão no lado contínuo do T nas interseções.
/// </summary>
public class AC3SidewalkExtensionTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    /// <summary>Eixo reto de 100 m ou em arco (raio 60 m, 100 m de desenvolvimento).</summary>
    public static List<Vec2> Axis(bool curved)
    {
        if (!curved) return new List<Vec2> { new(0, 0), new(100, 0) };
        const double R = 60;
        var pts = new List<Vec2>();
        for (int i = 0; i <= 400; i++)
        {
            var a = -Math.PI / 2 + (100.0 / R) * i / 400;
            pts.Add(new Vec2(0, R) + Vec2.FromAngle(a) * R);
        }
        return pts;
    }

    /// <summary>Via local (estacionamento dos dois lados) com as extensões pedidas, como cena de teste.</summary>
    public static (Y34TipTests.World W, RoadSetup Setup, List<MarkingDefinition> Defs, Polyline2 Axis) Road(bool curved, int template, Action<RoadSetup> tune)
    {
        var s = RoadTemplates.All[template].Create();
        tune(s);
        var pr = PathReference.FromPoints(Axis(curved), 0);
        var axis = new Polyline2(pr.Points);
        var defs = s.Build(pr, new OutputSettings(), Cat, axis: axis);
        var w = new Y34TipTests.World();
        foreach (var m in defs)
            if (m.Path is { } p && (p.Points.Count == pr.Points.Count && p.Points.Count > 0 && p.Points[0].DistanceTo(pr.Points[0]) < 1e-9 || p.Points.Count < 2))
                w.Paths[m.Id] = axis;
        w.Defs.AddRange(defs);
        w.Groups.Add(defs);
        return (w, s, defs, axis);
    }

    private static List<string> Faults(Y34TipTests.World w, Vec2 center) =>
        Y34TipTests.FaultsAt(Y34TipTests.Geometry(w), center, 16, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Extensao_no_meio_de_quadra_alinhada_ao_meio_fio(bool curved)
    {
        var (w, s, defs, axis) = Road(curved, 0, s => s.ExtensoesCalcada.Add(new ExtensaoCalcada { LadoEsquerdo = true, Estaca = 40, Comprimento = 12 }));
        var ext = Assert.Single(defs.OfType<CurbExtensionDefinition>());
        // Face interna da extensão sobre a face do meio-fio da via (±1 cm), ao longo de todo o trecho.
        var face = s.Lado(true).Face;
        Assert.All(ext.PathRef.Points, p => Assert.InRange(axis.Project(p).Signed, face - 0.01, face + 0.01));
        var pav = defs.OfType<RoadPavementDefinition>().Single();
        Assert.InRange(pav.LeftWidth, face - 0.01, face + 0.01);
        Assert.InRange(axis.Project(ext.PathRef.Points[0]).Station, 40 - 0.10, 40 + 0.10);
        Assert.InRange(axis.Project(ext.PathRef.Points[^1]).Station, 52 - 0.10, 52 + 0.10);
        // Um único polígono (piso, meio-fio e o piso atrás da face antiga formam uma peça só, sem furos).
        var geo = MarkingBuilder.Build(ext, new Polyline2(ext.PathRef.Points), new BuildContext { Catalog = Cat });
        var all = PolygonOps.Union(geo.Pieces.Select(p => p.Shape));
        var one = Assert.Single(all);
        // Sem furos (costuras da simplificação dos pisos abaixo de 1 cm² não contam).
        Assert.True(one.Holes.Sum(h => new Polygon2(h).Area) < 1e-4, $"furos de {one.Holes.Sum(h => new Polygon2(h).Area):0.000000} m²");
        // Avanço = largura do estacionamento (com a sarjeta somada, se houver) menos a sarjeta que continua na frente.
        var lado = s.Lado(true);
        var depth = all.SelectMany(p => p.Outer).Max(v => face - axis.Project(v).Signed);
        Assert.InRange(depth, lado.Parking!.Value - lado.Gutter - 0.02, lado.Parking!.Value - lado.Gutter + 0.02);
        // Sem falhas de modelagem em volta (furos, sobreposições, bordo sem meio-fio, sarjeta solta, pintura fora da pista).
        var faults = Faults(w, axis.PointAt(46));
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Travessia_no_meio_de_quadra_com_extensoes_alinhadas(bool curved)
    {
        var (w, s, defs, axis) = Road(curved, 0, s => s.ExtensoesCalcada.Add(new ExtensaoCalcada
        {
            LadoEsquerdo = true, Estaca = 40, Comprimento = 14, Travessia = true, LarguraFaixa = 4,
        }));
        var exts = defs.OfType<CurbExtensionDefinition>().ToList();
        Assert.Equal(2, exts.Count);
        var sc = 47.0;
        // As duas extensões cobrem a faixa (a parte com o avanço inteiro) – a do outro lado é espelhada no mesmo trecho.
        foreach (var e in exts)
        {
            var st = e.PathRef.Points.Select(p => axis.Project(p).Station).ToList();
            Assert.InRange(st.Min(), 0, sc - 2 - 1.5);
            Assert.InRange(st.Max(), sc + 2 + 1.5, 100);
        }
        Assert.Contains(exts, e => axis.Project(e.PathRef.Points[0]).Signed > 0);
        Assert.Contains(exts, e => axis.Project(e.PathRef.Points[0]).Signed < 0);
        // Rampas nas duas pontas, no eixo da faixa (mesma estaca ± 2 cm), na face de cada extensão.
        var ramps = defs.OfType<RampDefinition>().ToList();
        Assert.Equal(2, ramps.Count);
        Assert.All(ramps, r => Assert.InRange(axis.Project(r.PathRef.Points[0]).Station, sc - 0.02, sc + 0.02));
        var faceL = s.Lado(true).Face - (s.Lado(true).Parking!.Value - s.Lado(true).Gutter);
        var faceR = -(s.Lado(false).Face - (s.Lado(false).Parking!.Value - s.Lado(false).Gutter));
        Assert.Contains(ramps, r => Math.Abs(axis.Project(r.PathRef.Points[0]).Signed - faceL) < 0.02);
        Assert.Contains(ramps, r => Math.Abs(axis.Project(r.PathRef.Points[0]).Signed - faceR) < 0.02);
        Assert.True(ramps[0].PathRef.Points[0].DistanceTo(ramps[1].PathRef.Points[0]) < faceL - faceR + 0.02, "rampas desalinhadas");
        // Faixa de face a face das extensões (encurtada 2 × avanço) e alinhada.
        var cw = defs.OfType<LinearMarkingDefinition>().Single(l => l.Code.StartsWith("FTP"));
        Assert.All(cw.PathRef.Points, p => Assert.InRange(axis.Project(p).Station, sc - 0.02, sc + 0.02));
        Assert.InRange(cw.PathRef.Points[0].DistanceTo(cw.PathRef.Points[^1]), faceL - faceR - 0.6 - 0.05, faceL - faceR - 0.6 + 0.05);
        var faults = Faults(w, axis.PointAt(sc));
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
    }

    [Fact]
    public void Extensao_com_mobiliario_fora_da_travessia()
    {
        var (w, _, defs, axis) = Road(false, 0, s => s.ExtensoesCalcada.Add(new ExtensaoCalcada
        {
            LadoEsquerdo = false, Estaca = 30, Comprimento = 24, Travessia = true, Bancos = 1, Paraciclos = 3, Arvores = 2, Canteiro = true,
        }));
        var mob = defs.OfType<UrbanElementDefinition>().ToList();
        Assert.Equal(4, mob.Count);
        var ramp = defs.OfType<RampDefinition>().First(r => axis.Project(r.PathRef.Points[0]).Signed < 0);
        var fp = RampGenerator.Footprint(ramp, new Polyline2(ramp.PathRef.Points));
        // Bancos e paraciclos fora da rampa com as abas (+ folga).
        Assert.All(mob, m => Assert.True(PolygonOps.Offset(new[] { fp }, RoadFeatures.CrossingClearance).All(p => !p.Contains(m.Position))));
        var ext = defs.OfType<CurbExtensionDefinition>().Single(e => axis.Project(e.PathRef.Points[0]).Signed < 0);
        Assert.True(ext.Planter);
        Assert.NotNull(ext.PlanterRanges);
        Assert.Equal(2, ext.Trees);
        var faults = Faults(w, axis.PointAt(42));
        Assert.True(faults.Count == 0, string.Join(" | ", faults));
    }

    [Fact]
    public void Extensao_sem_estacionamento_pede_profundidade()
    {
        var (_, s, defs, _) = Road(false, 2, s =>
        {
            s.Left.RemoveAll(e => e.Tipo == TipoElementoSecao.Estacionamento);
            s.ExtensoesCalcada.Add(new ExtensaoCalcada { LadoEsquerdo = true, Estaca = 40, Comprimento = 10 });
        });
        Assert.Empty(defs.OfType<CurbExtensionDefinition>());
        Assert.Contains(s.Warnings, x => x.Contains("informe a profundidade"));
    }

    // ------------------------------------------------------------------ interseções

    /// <summary>T com a via local (estacionamento dos dois lados) contínua e uma local chegando, com extensões ligadas.</summary>
    public static Y34TipTests.World T(bool extensions) =>
        Y34TipTests.Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, CurbExtensions = extensions, CurbExtensionsOpposite = extensions },
            (0, new[] { new Vec2(-80, 0), new Vec2(80, 0) }), (0, new[] { Vec2.Zero, new Vec2(0, 80) }));

    [Fact]
    public void T_lado_continuo_recebe_extensao_e_rampa_no_eixo_da_faixa()
    {
        var w0 = T(false);
        var w1 = T(true);
        var L = w1.Nodes[0].Layout;
        var d = L.Definition!;
        var main = L.Legs.Where(l => l.Road == L.Main).ToList();
        Assert.Equal(2, main.Count);
        foreach (var leg in main)
        {
            var straightSide = leg.Sign > 0 ? -1 : 1;   // a via chega por y > 0: o lado contínuo é y < 0
            // Extensão no lado contínuo cobrindo a travessia.
            Assert.True(IntersectionGenerator.CrosswalkInset(L, leg, straightSide) > 1.5, $"ramo {leg.Sign}: sem extensão no lado contínuo");
            var tc = IntersectionGenerator.CrosswalkT(d, L, leg);
            // Rampa no eixo da faixa, na face da extensão do lado contínuo.
            var ramps = w1.Defs.OfType<RampDefinition>().Where(r => r.GroupId == d.Id && r.PathRef.Points[0].Y < 0).ToList();
            Assert.Contains(ramps, r => Math.Abs(Math.Abs(r.PathRef.Points[0].X) - tc) < 0.02);
        }
        // A travessia encurta 2 × avanço (±5 cm): extensão da esquina de um lado e do lado contínuo do outro.
        double Len(Y34TipTests.World w, int sign) => w.Defs.OfType<LinearMarkingDefinition>()
            .Where(l => l.GroupId == w.Nodes[0].Layout.Definition!.Id && l.Code.StartsWith("FTP"))
            .Select(l => (P: l.PathRef.Points, X: l.PathRef.Points.Average(p => p.X)))
            .Where(x => Math.Sign(x.X) == sign && Math.Abs(x.P[0].X - x.P[^1].X) < 0.5)
            .Select(x => x.P[0].DistanceTo(x.P[^1])).Single();
        var depth = L.Ears.Select(e => e.Template.Depth).Max();
        foreach (var sign in new[] { 1, -1 })
            Assert.InRange(Len(w0, sign) - Len(w1, sign), 2 * depth - 0.05, 2 * depth + 0.05);
        Assert.Empty(Y34TipTests.Faults(w1));
    }

    private static double CrosswalkLength(Y34TipTests.World w, int sign) => w.Defs.OfType<LinearMarkingDefinition>()
        .Where(l => l.GroupId == w.Nodes[0].Layout.Definition!.Id && l.Code.StartsWith("FTP"))
        .Select(l => (P: l.PathRef.Points, X: l.PathRef.Points.Average(p => p.X)))
        .Where(x => Math.Sign(x.X) == sign && Math.Abs(x.P[0].X - x.P[^1].X) < 0.5)
        .Select(x => x.P[0].DistanceTo(x.P[^1])).Single();

    [Fact]
    public void T_extensao_do_lado_continuo_personalizada_por_ramo()
    {
        // Ramo x > 0: avanço de 1,20 m e sem rampa no lado contínuo; ramo x < 0: o geral.
        var w0 = T(false);
        var w1 = Y34TipTests.Make(new IntersectionDefinition { Control = ControleIntersecao.Pare, CurbExtensions = true, CurbExtensionsOpposite = true },
            (d, rs) =>
            {
                var main = rs.First(r => Math.Abs(r.Axis.Points[0].Y) < 1e-6);
                d.LegSettings.Add(new IntersectionLegSettings { RoadId = main.Def.Id, Sign = 1, OppositeExtensionDepth = 1.2, OppositeExtensionRamp = false });
            },
            (0, new[] { new Vec2(-80, 0), new Vec2(80, 0) }), (0, new[] { Vec2.Zero, new Vec2(0, 80) }));
        var L = w1.Nodes[0].Layout;
        var d = L.Definition!;
        var corner = L.Ears.Where(e => !e.Straight).Select(e => e.Template.Depth).Max();
        foreach (var leg in L.Legs.Where(l => l.Road == L.Main))
        {
            var tc = IntersectionGenerator.CrosswalkT(d, L, leg);
            var x = Math.Sign(L.At(leg, 5, 0).X);
            var onStraight = w1.Defs.OfType<RampDefinition>().Where(r => r.GroupId == d.Id && r.PathRef.Points[0].Y < 0 && Math.Abs(Math.Abs(r.PathRef.Points[0].X) - tc) < 0.3
                                                                         && Math.Sign(r.PathRef.Points[0].X) == x).ToList();
            var shortening = CrosswalkLength(w0, x) - CrosswalkLength(w1, x);
            if (x > 0)
            {
                Assert.Empty(onStraight);
                // O avanço informado inclui a sarjeta que continua na frente (como nas esquinas): a face anda 1,20 − sarjeta.
                var ear = L.Ears.Single(e => e.Straight && e.LegA.Road == leg.Road && e.LegA.Sign == leg.Sign);
                Assert.InRange(ear.Template.Depth + ear.Gutter, 1.2 - 0.01, 1.2 + 0.01);
                Assert.InRange(shortening, corner + ear.Template.Depth - 0.05, corner + ear.Template.Depth + 0.05);
            }
            else
            {
                Assert.Single(onStraight);
                Assert.InRange(shortening, 2 * corner - 0.05, 2 * corner + 0.05);
            }
        }
        Assert.Empty(Y34TipTests.Faults(w1));
    }
}
