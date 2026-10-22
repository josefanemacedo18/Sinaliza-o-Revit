using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using Xunit.Abstractions;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Fase VI – regeneração depois de mudar o eixo, o greide ou a seção de uma via, no que o núcleo decide: quais conexões
/// (interseções, rotatórias, balões) são refeitas, se o nó acompanha o eixo movido e se a interseção refeita – com as mesmas
/// configurações – sai igual à de uma via criada já no lugar novo. A atualização no Revit em si (o updater) não roda aqui.
/// </summary>
public class Fase06RegeneracaoTests(ITestOutputHelper output)
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private sealed record Cena(IntersectionDemo.Scene S, IntersectionDefinition D);

    /// <summary>Principal (modelo 1) no eixo X e secundária no eixo Y, deslocadas de <paramref name="t"/>; a interseção
    /// <paramref name="d"/> é refeita como o Revit faz (nó acompanhado, perfis lidos das vias, recortes, filhos).</summary>
    private static Cena Cruzamento(IntersectionDefinition d, Vec2 t, Action<RoadSetup>? secundaria = null)
    {
        var roads = new List<IntersectionRoad>();
        var groups = new List<List<MarkingDefinition>>();
        var paths = new Dictionary<string, Polyline2>();
        var defs = new List<MarkingDefinition>();
        void Road(RoadSetup setup, Vec2 a, Vec2 b, string id)
        {
            var pr = PathReference.FromPoints(new[] { a, b }, 0);
            var axis = new Polyline2(pr.Points);
            // Ids fixos: a mesma via antes e depois da edição.
            var g = setup.Build(pr, new OutputSettings(), Cat, "grupo-" + id, "via-" + id, axis);
            foreach (var m in g) if (m.Path != null) paths[m.Id] = axis;
            defs.AddRange(g);
            groups.Add(g);
            roads.Add(new IntersectionRoad(g.OfType<RoadPavementDefinition>().First(), axis));
        }
        Road(RoadTemplates.All[1].Create(), new Vec2(-110, 0) + t, new Vec2(110, 0) + t, "a");
        var minor = RoadTemplates.All[0].Create();
        secundaria?.Invoke(minor);
        Road(minor, new Vec2(0, -110) + t, new Vec2(0, 110) + t, "b");
        d.Node = IntersectionGenerator.FollowNode(d.Node, roads) ?? throw new InvalidOperationException("as vias não se cruzam perto do nó");
        return new Cena(IntersectionDemo.Create(d, roads, groups, paths, defs, Cat), d);
    }

    private static MarkingGeometry Geometria(Cena c)
    {
        var ctx = new BuildContext { Catalog = Cat, Lookup = id => c.S.Definitions.FirstOrDefault(x => x.Id == id), PathOf = x => c.S.Paths.GetValueOrDefault(x.Id) };
        return MarkingBuilder.Build(c.D, null, ctx);
    }

    private static IntersectionDefinition Copia(IntersectionDefinition d) => (IntersectionDefinition)MarkingDefinition.FromJson(d.ToJson())!;

    [Fact]
    public void EixoMovido_InterseccaoRefeitaAcompanhaENaoMudaOsQuantitativos()
    {
        var d0 = new IntersectionDefinition { Control = ControleIntersecao.Pare, Node = Vec2.Zero, CornerRadius = 8 };
        var antes = Cruzamento(d0, Vec2.Zero);
        var g0 = Geometria(antes);
        // O usuário move as duas linhas de eixo 7 m para leste e 4 m para o sul: a mesma interseção (mesmo id e ajustes) é
        // refeita sobre os eixos novos.
        var t = new Vec2(7, -4);
        var depois = Cruzamento(Copia(antes.D), t);
        Assert.Equal(antes.D.Id, depois.D.Id);
        Assert.Equal(t.X, depois.D.Node.X, 6);
        Assert.Equal(t.Y, depois.D.Node.Y, 6);
        Assert.Equal(8, depois.D.CornerRadius);
        var g1 = Geometria(depois);
        var (b0, b1) = (g0.Bounds!.Value, g1.Bounds!.Value);
        Assert.True((b1.Min - b0.Min - t).Length < 0.002 && (b1.Max - b0.Max - t).Length < 0.002, $"{b0} → {b1}");
        var r0 = ComponentQuantities.Compute(new[] { ((MarkingDefinition)antes.D, g0) });
        var r1 = ComponentQuantities.Compute(new[] { ((MarkingDefinition)depois.D, g1) });
        Assert.Equal(r0.Select(r => (r.Code, r.Color)), r1.Select(r => (r.Code, r.Color)));
        foreach (var (a, b) in r0.Zip(r1))
        {
            output.WriteLine($"{a.Code} {a.ColorLabel}: {a.Area:0.000} → {b.Area:0.000} m²");
            Assert.True(Math.Abs(a.Area - b.Area) <= Math.Max(0.01, 1e-4 * a.Area), $"{a.Code}: {a.Area} → {b.Area}");
            Assert.True(Math.Abs(a.Length - b.Length) <= Math.Max(0.01, 1e-4 * a.Length), $"{a.Code}: {a.Length} m → {b.Length} m");
        }
    }

    [Fact]
    public void SecaoAlterada_CalcadaDaInterseccaoAcompanha()
    {
        var d0 = new IntersectionDefinition { Control = ControleIntersecao.Pare, Node = Vec2.Zero };
        var antes = Cruzamento(d0, Vec2.Zero);
        double Calcada(Cena c) => ComponentQuantities.Compute(new[] { ((MarkingDefinition)c.D, Geometria(c)) })
            .Where(r => r.Code == SectionComponent.Calcada).Sum(r => r.Area);
        var a0 = Calcada(antes);
        // Calçada da via secundária 1 m mais larga dos dois lados: a interseção refeita lê o perfil novo das vias.
        var depois = Cruzamento(Copia(antes.D), Vec2.Zero, s =>
        {
            foreach (var e in s.Left.Concat(s.Right).Where(e => e.Tipo == TipoElementoSecao.Calcada)) e.Largura += 1.0;
        });
        double Passeio(Cena c, bool left) => c.D.RoadProfiles.Single(p => p.RoadId == "via-b").Side(left).Where(b => b.Code == "CALCADA").Sum(b => b.Width);
        Assert.Equal(Passeio(antes, true) + 1, Passeio(depois, true), 6);
        Assert.Equal(Passeio(antes, false) + 1, Passeio(depois, false), 6);
        var a1 = Calcada(depois);
        output.WriteLine($"calçada da interseção: {a0:0.00} → {a1:0.00} m²");
        Assert.True(a1 > a0 + 5, $"{a0} → {a1}");
    }

    [Fact]
    public void NoDaInterseccao_AcompanhaAte15mOuAInterseccaoSai()
    {
        IntersectionRoad Road(Vec2 a, Vec2 b)
        {
            var pv = new RoadPavementDefinition { Id = Guid.NewGuid().ToString("N") };
            return new IntersectionRoad(pv, new Polyline2(new[] { a, b }));
        }
        var x = Road(new Vec2(-50, 0), new Vec2(50, 0));
        // Secundária deslocada 6 m: o nó vai junto.
        var n = IntersectionGenerator.FollowNode(Vec2.Zero, new[] { x, Road(new Vec2(6, -50), new Vec2(6, 50)) });
        Assert.NotNull(n);
        Assert.Equal(6, n!.Value.X, 6);
        // 20 m: longe demais do nó gravado – não é mais esta interseção.
        Assert.Null(IntersectionGenerator.FollowNode(Vec2.Zero, new[] { x, Road(new Vec2(20, -50), new Vec2(20, 50)) }));
        // Vias que não se cruzam mais (paralelas) e via única.
        Assert.Null(IntersectionGenerator.FollowNode(Vec2.Zero, new[] { x, Road(new Vec2(-50, 10), new Vec2(50, 10)) }));
        Assert.Null(IntersectionGenerator.FollowNode(Vec2.Zero, new[] { x }));
    }

    [Fact]
    public void ViaAlterada_RefazAsConexoesLigadasAEla()
    {
        MarkingDefinition M(string id, string group) => new LinearMarkingDefinition { Id = id, Code = "LBO", GroupId = group };
        var pa = new RoadPavementDefinition { Id = "pa", GroupId = "ga" };
        var pb = new RoadPavementDefinition { Id = "pb", GroupId = "gb" };
        var pc = new RoadPavementDefinition { Id = "pc", GroupId = "gc" };
        var lboA = M("lbo-a", "ga");
        var ab = new IntersectionDefinition { Id = "i-ab", RoadIds = { "pa", "pb" } };
        var bc = new IntersectionDefinition { Id = "i-bc", RoadIds = { "pb", "pc" } };
        var rbC = new RoundaboutDefinition { Id = "r-c", Legs = { new RoundaboutLeg { RoadId = "pc" } } };
        var rbGroupB = new RoundaboutDefinition { Id = "r-gb", Legs = { new RoundaboutLeg { GroupId = "gb" } } };
        var cdsA = new CulDeSacDefinition { Id = "c-a", RoadId = "pa" };
        var all = new List<MarkingDefinition> { pa, pb, pc, lboA, ab, bc, rbC, rbGroupB, cdsA };
        List<string> Ids(params MarkingDefinition[] changed) => ModelGraph.ConnectionsToRefresh(changed, all).Select(d => d.Id).ToList();

        // Seção ou greide da via A mudou (qualquer marca do grupo, não só o pavimento): interseção A–B e o balão de A.
        Assert.Equal(new[] { "i-ab", "c-a" }, Ids(lboA));
        Assert.Equal(new[] { "i-ab", "c-a" }, Ids(pa));
        // Eixo da via B: as duas interseções dela e a rotatória ligada pelo grupo do ramo.
        Assert.Equal(new[] { "i-ab", "i-bc", "r-gb" }, Ids(pb));
        // Via C: interseção B–C e a rotatória pela via do ramo.
        Assert.Equal(new[] { "i-bc", "r-c" }, Ids(pc));
        // Marca solta (sem via) não refaz nada.
        Assert.Empty(Ids(new LinearMarkingDefinition { Id = "solta", Code = "LBO" }));
    }
}
