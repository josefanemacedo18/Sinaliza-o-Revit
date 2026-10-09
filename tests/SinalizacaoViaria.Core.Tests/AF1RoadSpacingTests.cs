using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using Xunit;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada 6, item 5: distância entre vias (eixos, meios-fios, bordos) e "Mover via". A via B se move inteira (eixo, marcas
/// do grupo, elementos avulsos sobre ela) e as interseções vão para o novo cruzamento – tudo com ±1 mm.
/// </summary>
public class AF1RoadSpacingTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private const double Mm = 0.001;

    /// <summary>A (y = 0) e B (y = 30) paralelas, C cruzando as duas em x = 0; placas avulsas sobre A e sobre B.</summary>
    private static (Y34TipTests.World W, SignDefinition OnA, SignDefinition OnB, SymbolMarkingDefinition InGroup) Scene(double angleB = 0)
    {
        var dirB = Y34TipTests.Dir(angleB);
        var w = Y34TipTests.Make(null,
            (0, new[] { new Vec2(-90, 0), new Vec2(90, 0) }),
            (0, new[] { new Vec2(0, 30) - dirB * 90, new Vec2(0, 30) + dirB * 90 }),
            (0, new[] { new Vec2(0, -60), new Vec2(0, 100) }));
        var b = w.Roads[1].Def;
        var onA = new SignDefinition { Code = "R-1", Position = new Vec2(40, w.Roads[0].Def.LeftWidth + 1.0) };
        var onB = new SignDefinition { Code = "R-6a", Position = new Vec2(0, 30) + dirB * 40 + dirB.PerpLeft * (b.LeftWidth + 1.0) };
        var sym = new SymbolMarkingDefinition { Code = "PEM-F", Position = new Vec2(0, 30) + dirB * 50, Direction = dirB, GroupId = b.GroupId };
        w.Defs.AddRange(new MarkingDefinition[] { onA, onB, sym });
        w.Groups[1].Add(sym);
        return (w, onA, onB, sym);
    }

    private static List<IntersectionRoad> RoadsNow(Y34TipTests.World w) =>
        w.Roads.Select(r => r with { Axis = new Polyline2(r.Def.PathRef.Points) }).ToList();

    private static (Vec2 Min, Vec2 Max) Box(MarkingGeometry g)
    {
        var pts = g.Pieces.SelectMany(p => p.Shape.Outer).ToList();
        return (new Vec2(pts.Min(p => p.X), pts.Min(p => p.Y)), new Vec2(pts.Max(p => p.X), pts.Max(p => p.Y)));
    }

    private static MarkingGeometry Build(MarkingDefinition d) =>
        MarkingBuilder.Build(d, new Polyline2(d.Path!.Points, d.Path.Closed), new BuildContext { Catalog = Cat });

    [Fact]
    public void Distancia_medida_nos_tres_modos()
    {
        var (w, _, onB, _) = Scene();
        var a = w.Roads[0];
        var b = w.Roads[1];
        var d = RoadSpacing.Measure(a, b, onB.Position)!;
        Assert.NotNull(d);
        Assert.Equal(30, d.Eixos, 3);
        // B corre no sentido +x: o lado dela voltado para A é o direito.
        Assert.Equal(30 - a.Def.LeftWidth - b.Def.RightWidth, d.MeiosFios, 3);
        Assert.Equal(30 - a.Def.TotalLeft - b.Def.TotalRight, d.Bordos, 3);
        Assert.True(d.MeiosFios > d.Bordos && d.Bordos > 0);
        Assert.Equal(new Vec2(0, 1), d.Normal);
    }

    [Fact]
    public void Nova_distancia_entre_eixos_move_a_via_os_elementos_e_a_intersecao()
    {
        var (w, onA, onB, sym) = Scene();
        var a = w.Roads[0];
        var pavB = w.Roads[1].Def;
        var d = RoadSpacing.Measure(a, w.Roads[1], onB.Position)!;
        var delta = d.Deslocamento(ReferenciaDistancia.Eixos, 40);
        Assert.True(delta.DistanceTo(new Vec2(0, 10)) < Mm);

        var nodeBC = w.Nodes.Single(n => n.Layout.Node.Y > 15).Layout;
        var itBC = nodeBC.Definition!;
        var itAC = w.Nodes.Single(n => n.Layout.Node.Y < 15).Layout.Definition!;
        var acBefore = itAC.Node;
        var pavBefore = Box(Build(pavB));
        var onABefore = onA.Position;
        var onBBefore = onB.Position;
        var symBefore = sym.Position;
        var pavZoneBefore = nodeBC.Pavement.SelectMany(p => p.Outer).ToList();

        var members = RoadSpacing.Members(w.Defs, pavB, w.Roads[1].Axis, w.Roads);
        Assert.Contains(members, m => m.Id == pavB.Id);
        Assert.Contains(members, m => m.Id == onB.Id);          // placa avulsa sobre a calçada de B
        Assert.Contains(members, m => m.Id == sym.Id);          // símbolo do grupo de B
        Assert.DoesNotContain(members, m => m.Id == onA.Id);    // placa sobre A fica
        Assert.DoesNotContain(members, m => m is IntersectionDefinition);
        Assert.DoesNotContain(members, m => itBC.ChildIds.Contains(m.Id) || itAC.ChildIds.Contains(m.Id));
        Assert.All(w.Groups[1], g => Assert.Contains(members, m => m.Id == g.Id));

        RoadSpacing.Translate(members, delta);
        var after = RoadsNow(w);
        var moved = RoadSpacing.FollowNodes(w.Defs.OfType<IntersectionDefinition>(), after, new HashSet<string> { pavB.Id }, delta);

        // Distância nova (±1 mm) nos três modos.
        var d2 = RoadSpacing.Measure(after[0], after[1], onB.Position)!;
        Assert.Equal(40, d2.Eixos, 3);
        Assert.Equal(d.MeiosFios + 10, d2.MeiosFios, 3);
        Assert.Equal(d.Bordos + 10, d2.Bordos, 3);
        // Elementos da via deslocados com ela; os das outras vias ficam.
        Assert.True(onB.Position.DistanceTo(onBBefore + delta) < Mm);
        Assert.True(sym.Position.DistanceTo(symBefore + delta) < Mm);
        Assert.True(onA.Position.DistanceTo(onABefore) < 1e-12);
        var pavAfter = Box(Build(pavB));
        Assert.True(pavAfter.Min.DistanceTo(pavBefore.Min + delta) < Mm);
        Assert.True(pavAfter.Max.DistanceTo(pavBefore.Max + delta) < Mm);
        Assert.All(w.Groups[0], m => Assert.True(m.Path == null || m.Path.Points[0].Y == 0));
        // Interseções: B×C no novo cruzamento, A×C no lugar.
        Assert.Single(moved);
        Assert.Same(itBC, moved[0]);
        Assert.True(itBC.Node.DistanceTo(new Vec2(0, 40)) < Mm);
        Assert.True(itAC.Node.DistanceTo(acBefore) < 1e-12);

        // Regenerada no nó novo, a interseção B×C é a mesma deslocada (pavimento da esquina ±1 mm).
        foreach (var m in w.Defs) m.Exclusions.RemoveAll(z => z.SourceId == itBC.Id);
        var ids = new[] { 1, 2 };
        var scene = IntersectionDemo.Create(itBC, ids.Select(i => after[i]).ToList(), ids.Select(i => w.Groups[i]).ToList(),
            ids.SelectMany(i => w.Groups[i]).Where(m => m.Path != null).ToDictionary(m => m.Id, m => new Polyline2(m.Path!.Points, m.Path.Closed)), null, Cat);
        Assert.True(scene.Layout.Node.DistanceTo(new Vec2(0, 40)) < Mm);
        var pavZoneAfter = scene.Layout.Pavement.SelectMany(p => p.Outer).ToList();
        Assert.Equal(pavZoneBefore.Count, pavZoneAfter.Count);
        Assert.Equal(pavZoneBefore.Min(p => p.Y) + 10, pavZoneAfter.Min(p => p.Y), 3);
        Assert.Equal(pavZoneBefore.Max(p => p.Y) + 10, pavZoneAfter.Max(p => p.Y), 3);
        Assert.Equal(pavZoneBefore.Min(p => p.X), pavZoneAfter.Min(p => p.X), 3);
    }

    [Theory]
    [InlineData(ReferenciaDistancia.MeiosFios, 18.0, 0.0)]
    [InlineData(ReferenciaDistancia.Bordos, 9.5, 0.0)]
    [InlineData(ReferenciaDistancia.Eixos, 36.0, 12.0)]
    [InlineData(ReferenciaDistancia.MeiosFios, 25.0, -15.0)]
    public void Nova_distancia_vale_em_qualquer_modo_e_com_vias_inclinadas(ReferenciaDistancia modo, double nova, double angleB)
    {
        var (w, _, onB, _) = Scene(angleB);
        var pavB = w.Roads[1].Def;
        var d = RoadSpacing.Measure(w.Roads[0], w.Roads[1], onB.Position)!;
        var delta = d.Deslocamento(modo, nova);
        RoadSpacing.Translate(RoadSpacing.Members(w.Defs, pavB, w.Roads[1].Axis, w.Roads), delta);
        var after = RoadsNow(w);
        // Mesma seção de A (o ponto de B clicado foi junto com a via).
        var d2 = RoadSpacing.Measure(after[0], after[1], onB.Position)!;
        Assert.True(d2.PontoA.DistanceTo(d.PontoA) < Mm);
        Assert.Equal(nova, d2.Valor(modo), 3);
        Assert.Equal(d2.Eixos - d.Eixos, d2.MeiosFios - d.MeiosFios, 3);
        Assert.Equal(d2.Eixos - d.Eixos, d2.Bordos - d.Bordos, 3);
    }

    [Fact]
    public void Mover_via_por_deslocamento_digitado()
    {
        var (w, onA, onB, sym) = Scene();
        var pavB = w.Roads[1].Def;
        var delta = new Vec2(5, -3);
        var before = pavB.PathRef.Points.ToList();
        var bBefore = onB.Position;
        RoadSpacing.Translate(RoadSpacing.Members(w.Defs, pavB, w.Roads[1].Axis, w.Roads), delta);
        Assert.All(pavB.PathRef.Points.Zip(before), p => Assert.True(p.First.DistanceTo(p.Second + delta) < Mm));
        Assert.True(onB.Position.DistanceTo(bBefore + delta) < Mm);
        Assert.Equal(new Vec2(40, w.Roads[0].Def.LeftWidth + 1.0), onA.Position);
        var it = w.Nodes.Single(n => n.Layout.Node.Y > 15).Layout.Definition!;
        RoadSpacing.FollowNodes(w.Defs.OfType<IntersectionDefinition>(), RoadsNow(w), new HashSet<string> { pavB.Id }, delta);
        // B continua horizontal (y = 27) e C vertical em x = 0: o nó escorrega por C.
        Assert.True(it.Node.DistanceTo(new Vec2(0, 27)) < Mm);
        // Âncoras das estacas e recortes acompanham.
        Assert.All(w.Groups[1].SelectMany(m => m.Exclusions), z => Assert.True(z.Points.Count > 0));
    }

    [Fact]
    public void Vias_que_se_cruzam_nao_tem_distancia()
    {
        var (w, _, _, _) = Scene();
        Assert.Null(RoadSpacing.Measure(w.Roads[0], w.Roads[2], new Vec2(0, 50)));
    }
}
