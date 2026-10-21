using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Fase III – arquitetura integrada: ordem de regeneração pelo grafo de dependências (D-004) e placas personalizadas só no
/// projeto que as tem (K1).
/// </summary>
public class Fase03ArquiteturaTests
{
    private static LinearMarkingDefinition Line(string id) => new() { Id = id };
    private static LabelDefinition Label(string id, string target) => new() { Id = id, MarkingTargetId = target };

    /// <summary>A ordem de antes: marcas, depois detalhes, depois anotações do projeto todo, cada grupo na ordem de entrada.</summary>
    private static List<MarkingDefinition> OldOrder(IEnumerable<MarkingDefinition> defs) =>
        defs.OrderBy(d => d is IProjectWideAnnotation ? 2 : d is IAnnotationDefinition ? 1 : 0).ToList();

    [Fact]
    public void SemDependenciasNoMesmoGrupo_MesmaOrdemDeAntes()
    {
        var a = Line("a");
        var b = Line("b");
        var legend = new LegendDefinition { Id = "leg" };
        var la = Label("la", "a");
        var lb = Label("lb", "b");
        var c = Line("c");
        var input = new List<MarkingDefinition> { legend, la, a, lb, b, c };
        Assert.Equal(OldOrder(input).Select(d => d.Id), ModelGraph.RegenerationSort(input).Select(d => d.Id));
    }

    [Fact]
    public void FilhasDepoisDoNo_MesmoQueVenhamAntes()
    {
        var child1 = Line("f1");
        var child2 = Line("f2");
        var node = new IntersectionDefinition { Id = "no", ChildIds = { "f1", "f2" } };
        var other = Line("x");
        var order = ModelGraph.RegenerationSort(new MarkingDefinition[] { child1, other, child2, node }).Select(d => d.Id).ToList();
        Assert.True(order.IndexOf("no") < order.IndexOf("f1"));
        Assert.True(order.IndexOf("no") < order.IndexOf("f2"));
        // O que não depende de nada continua na ordem de entrada.
        Assert.Equal(new[] { "x", "no", "f1", "f2" }, order);
    }

    [Fact]
    public void DetalheDeDetalhe_DepoisDoDetalhe()
    {
        var mark = Line("m");
        var d2 = Label("d2", "d1");
        var d1 = Label("d1", "m");
        var order = ModelGraph.RegenerationSort(new MarkingDefinition[] { d2, d1, mark }).Select(d => d.Id).ToList();
        Assert.Equal(new[] { "m", "d1", "d2" }, order);
    }

    [Fact]
    public void Ciclo_NaoTrava_EMantemTodos()
    {
        var a = new IntersectionDefinition { Id = "A", ChildIds = { "B" } };
        var b = new IntersectionDefinition { Id = "B", ChildIds = { "A" } };
        var c = Line("C");
        var order = ModelGraph.RegenerationSort(new MarkingDefinition[] { a, b, c }).Select(d => d.Id).ToList();
        Assert.Equal(3, order.Count);
        Assert.Equal(new[] { "A", "B", "C" }.OrderBy(x => x), order.OrderBy(x => x));
    }

    [Fact]
    public void IdsRepetidos_EVazios_SaoMantidos()
    {
        var a1 = Line("a");
        var a2 = Line("a");
        var semId = Line("");
        var order = ModelGraph.RegenerationSort(new MarkingDefinition[] { a1, semId, a2 });
        Assert.Equal(3, order.Count);
        Assert.Same(a1, order[0]);
        Assert.Same(semId, order[1]);
        Assert.Same(a2, order[2]);
    }

    [Fact]
    public void RecorteNaoOrdena()
    {
        // A via guarda o recorte da interseção que a cita (referência fraca): não pode forçar a interseção para depois.
        var road = Line("via");
        var node = new IntersectionDefinition { Id = "no", RoadIds = { "via" } };
        road.Exclusions.Add(new ExclusionZone { SourceId = "no" });
        var order = ModelGraph.RegenerationSort(new MarkingDefinition[] { road, node }).Select(d => d.Id).ToList();
        Assert.Equal(new[] { "via", "no" }, order);
    }

    [Fact]
    public void PlacasPersonalizadas_SoAsDoProjeto()
    {
        var cat = CatalogService.LoadDefault();
        var normative = cat.Placas.Count;
        var a = CustomSigns.Create("PA-1", "Placa do projeto A", FormaPlaca.Retangulo, 1, 0.5, Model.MarkingColor.Azul, Model.MarkingColor.Branca, "A", Model.MarkingColor.Branca);
        var b = CustomSigns.Create("PB-1", "Placa do projeto B", FormaPlaca.Retangulo, 1, 0.5, Model.MarkingColor.Azul, Model.MarkingColor.Branca, "B", Model.MarkingColor.Branca);
        // O catálogo da sessão acumula as dos dois projetos (a placa de qualquer marca continua achada pelo código)…
        CustomSigns.Apply(cat, new[] { a, b });
        Assert.NotNull(cat.Placa("PA-1"));
        Assert.NotNull(cat.Placa("PB-1"));
        // …mas a lista do projeto A não mostra a do B (K1); a comparação de códigos não diferencia maiúsculas.
        var listA = CustomSigns.ForProject(cat.Placas, new[] { "pa-1" }).ToList();
        Assert.Contains(listA, p => p.Codigo == "PA-1");
        Assert.DoesNotContain(listA, p => p.Codigo == "PB-1");
        Assert.Equal(normative + 1, listA.Count);
        // Projeto sem placas personalizadas: só o catálogo normativo.
        Assert.Equal(normative, CustomSigns.ForProject(cat.Placas, Array.Empty<string>()).Count());
    }
}
