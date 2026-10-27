using System.Runtime.CompilerServices;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 15 do plano 2 – item 18 do pedido de 2026-10-27: "em trechos curtos está sendo gerado várias rampas, extensões e faixas
/// de pedestres desnecessariamente". Cada interseção fazia travessia e rampas em todos os ramos sem olhar o nó vizinho; agora,
/// num trecho mais curto que o mínimo, só um dos dois nós faz a travessia sobre a via.
/// </summary>
public class Etapa15GeracaoTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static IntersectionRoad Road(params Vec2[] axis)
    {
        var defs = RoadTemplates.All[0].Create().Build(PathReference.FromPoints(axis, 0), new OutputSettings(), Cat);
        return new IntersectionRoad(defs.OfType<RoadPavementDefinition>().Single(), new Polyline2(axis));
    }

    /// <summary>Via principal leste-oeste e duas transversais a <paramref name="trecho"/> m uma da outra: dois cruzamentos.</summary>
    private static (List<MarkingDefinition> A, List<MarkingDefinition> B) DoisCruzamentos(double trecho, double minimo, Action<IntersectionDefinition>? ajuste = null)
    {
        var main = Road(new Vec2(-120, 0), new Vec2(trecho + 120, 0));
        var c1 = Road(new Vec2(0, -80), new Vec2(0, 80));
        var c2 = Road(new Vec2(trecho, -80), new Vec2(trecho, 80));
        var a = new IntersectionDefinition { Node = Vec2.Zero, RoadIds = { main.Def.Id, c1.Def.Id }, TravessiaTrechoMinimo = minimo };
        var b = new IntersectionDefinition { Node = new Vec2(trecho, 0), RoadIds = { main.Def.Id, c2.Def.Id }, TravessiaTrechoMinimo = minimo };
        ajuste?.Invoke(a);
        var todos = new MarkingDefinition[] { a, b };
        List<MarkingDefinition> Filhos(IntersectionDefinition d, IntersectionRoad cross)
        {
            d.NeighborCrossings = EspacamentoTravessias.Vizinhos(d, todos, d.Node);
            var L = IntersectionGenerator.Layout(d, new[] { main, cross });
            return IntersectionGenerator.Children(d, L, new OutputSettings(), 0);
        }
        return (Filhos(a, c1), Filhos(b, c2));
    }

    private static List<LinearMarkingDefinition> Faixas(IEnumerable<MarkingDefinition> defs) =>
        defs.OfType<LinearMarkingDefinition>().Where(l => l.Code.StartsWith("FTP", StringComparison.Ordinal)).ToList();

    /// <summary>Faixas de pedestres que atravessam a via principal dentro do trecho entre os dois nós.</summary>
    private static int NoTrecho(IEnumerable<MarkingDefinition> defs, double trecho) =>
        Faixas(defs).Count(f => f.PathRef.Points is { Count: >= 2 } p && (p[0] + p[^1]).X / 2 is var x && x > 1 && x < trecho - 1
                                && Math.Abs(p[0].X - p[^1].X) < 0.5);

    [Fact]
    public void TrechoCurto_UmaTravessiaEUmParDeRampas()
    {
        // Antes (trecho mínimo 0 = como era): as duas pontas do trecho de 30 m ganham faixa e rampas.
        var (a0, b0) = DoisCruzamentos(30, 0);
        Assert.Equal(2, NoTrecho(a0.Concat(b0), 30));
        // Com o mínimo padrão (50 m): só um dos dois nós atravessa a principal nesse trecho – uma faixa, um par de rampas.
        var (a, b) = DoisCruzamentos(30, EspacamentoTravessias.TrechoMinimoPadrao);
        Assert.Equal(1, NoTrecho(a.Concat(b), 30));
        Assert.Equal(Faixas(a0).Count + Faixas(b0).Count - 1, Faixas(a).Count + Faixas(b).Count);
        Assert.Equal(a0.Concat(b0).OfType<RampDefinition>().Count() - 2, a.Concat(b).OfType<RampDefinition>().Count());
        // Os outros ramos não mudam: cada nó continua com as travessias das transversais e do lado de fora do trecho.
        Assert.Equal(3, new[] { Faixas(a).Count, Faixas(b).Count }.Min());
    }

    [Fact]
    public void TrechoLongo_TravessiaNasDuasPontas()
    {
        var (a, b) = DoisCruzamentos(80, EspacamentoTravessias.TrechoMinimoPadrao);
        Assert.Equal(2, NoTrecho(a.Concat(b), 80));
    }

    [Fact]
    public void Escolha_PeloPesoESimetrica_EAjusteDoRamoVence()
    {
        // O nó de controle mais forte (semáforo) fica com a travessia do trecho; o outro, não – vista dos dois lados.
        var (a, b) = DoisCruzamentos(30, 50, d => d.Control = ControleIntersecao.Semaforo);
        bool Atravessa(List<MarkingDefinition> defs) => NoTrecho(defs, 30) == 1;
        Assert.True(Atravessa(a));
        Assert.False(Atravessa(b));
        // Modo manual: travessias desligadas na interseção – nenhuma.
        var (m1, m2) = DoisCruzamentos(30, 50, d => d.Crosswalks = false);
        Assert.Empty(Faixas(m1));
        Assert.Equal(1, NoTrecho(m2, 30));   // o vizinho não perde a dele: o nó sem travessias não "ganha" o trecho
    }

    [Fact]
    public void Vizinhos_RotatoriaSempreFicaEDistanciaLimite()
    {
        var d = new IntersectionDefinition { Node = Vec2.Zero, RoadIds = { "a", "b" } };
        var rb = new RoundaboutDefinition { Center = new Vec2(40, 0) };
        var longe = new IntersectionDefinition { Node = new Vec2(500, 0) };
        var v = EspacamentoTravessias.Vizinhos(d, new MarkingDefinition[] { d, rb, longe }, d.Node);
        var n = Assert.Single(v);
        Assert.Equal(EspacamentoTravessias.PesoRotatoria, n.Peso);
        Assert.True(EspacamentoTravessias.Peso(new IntersectionDefinition { RoadIds = { "a", "b", "c" } }) > EspacamentoTravessias.Peso(d));
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
    public void Plugin_VizinhosAntesDoArranjo_ECampoNaJanela()
    {
        var svc = File.ReadAllText(Path.Combine(Raiz(), "src", "SinalizacaoViaria.Revit", "Infrastructure", "IntersectionService.cs"));
        var viz = svc.IndexOf("it.NeighborCrossings = EspacamentoTravessias.Vizinhos(it, all, node);", StringComparison.Ordinal);
        Assert.True(viz > 0 && viz < svc.IndexOf("var layout = IntersectionGenerator.Layout(it, roads);", StringComparison.Ordinal));
        var form = File.ReadAllText(Path.Combine(Raiz(), "src", "SinalizacaoViaria.Revit", "Commands", "IntersectionCommands.cs"));
        Assert.Contains("d.TravessiaTrechoMinimo", form);
        Assert.DoesNotContain("IntersectionGenerator.Layout((IntersectionDefinition)MarkingDefinition.FromJson", form);
        // A janela diz que ramo ficou sem travessia e por quê (núcleo).
        var no = File.ReadAllText(Path.Combine(Raiz(), "src", "SinalizacaoViaria.Core", "Automation", "NodeForm.cs"));
        Assert.Contains("EspacamentoTravessias.Suprimida(def, layout, leg, out var motivo)", no);
    }
}
