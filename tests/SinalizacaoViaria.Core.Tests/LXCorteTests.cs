using System.Text.RegularExpressions;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada LX: o Corte Transversal não deixa desenho na planta antes do perfil (1).
/// </summary>
public class LXCorteTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static SectionDimensionDefinition Cut(bool chain, bool profile) =>
        new() { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45), PlanChain = chain, Profile = profile };

    private static BuildContext Ctx(IReadOnlyList<MarkingDefinition> defs, Func<MarkingDefinition, MarkingGeometry?> geoOf,
        Func<MarkingDefinition, Polyline2?> pathOf, params MarkingDefinition[] extra) => new()
    {
        Catalog = Cat, ViewScale = 100, AllDefinitions = () => defs.Concat(extra).ToList(), GeometryOf = geoOf, PathOf = pathOf,
        Lookup = id => extra.FirstOrDefault(x => x.Id == id) ?? defs.FirstOrDefault(x => x.Id == id),
    };

    // ================================================================== 1 – corte sem desenho duplicado

    [Fact]
    public void F1_CorteTransversal_NadaNaPlantaSoOPerfil()
    {
        // Antes: o modo "só o corte" gravava na planta, sobre a via, a linha de corte vermelha, o EIXO, as marcas A–A e o
        // título "SEÇÃO A–A (cotas em metros...)" – um desenho de corte antes do perfil. Agora a cota só guarda a linha de corte
        // (dados do perfil) e o único desenho é o perfil final.
        var (defs, geoOf, pathOf) = DetailGenerator.SectionSample();
        var cut = Cut(chain: false, profile: true);
        var ctx = Ctx(defs, geoOf, pathOf, cut);
        var plan = DetailGenerator.SectionDimensions(cut, ctx);
        Assert.Empty(plan.Annotations);
        Assert.Empty(plan.Pieces);
        Assert.True(DetailGenerator.SectionDataOnly(cut));
        // O perfil continua inteiro, lido da cota guardada.
        var profile = DetailGenerator.SectionProfileDrawing(SectionProfileDefinition.From(cut, Vec2.Zero), ctx);
        Assert.NotEmpty(profile.Pieces);
        Assert.Contains(profile.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith("SEÇÃO TRANSVERSAL A–A"));
        Assert.Contains(profile.Annotations.OfType<AnnotationText>(), t => Regex.IsMatch(t.Text, @"^\d+,\d+$"));
    }

    [Fact]
    public void F1_CotarSecao_PlantaContinuaComCortaEMarcas()
    {
        // Os outros modos não mudam: com cotas na planta (com ou sem perfil) e só cotas, a linha de corte e as marcas A–A ficam.
        var (defs, geoOf, pathOf) = DetailGenerator.SectionSample();
        foreach (var (chain, profile) in new[] { (true, true), (true, false), (false, false) })
        {
            var cut = Cut(chain, profile);
            var plan = DetailGenerator.SectionDimensions(cut, Ctx(defs, geoOf, pathOf, cut));
            Assert.False(DetailGenerator.SectionDataOnly(cut));
            Assert.Contains(plan.Annotations.OfType<AnnotationLine>(), l => l.Color == MarkingColor.Vermelha);
            Assert.Contains(plan.Annotations.OfType<AnnotationText>(), t => t.Text == "A");
        }
        // Seção antiga com o perfil embutido (posição gravada na cota) continua desenhando o perfil – não vira só dados.
        var legacy = Cut(chain: false, profile: true);
        legacy.ProfilePosition = new Vec2(0, -11);
        Assert.False(DetailGenerator.SectionDataOnly(legacy));
        Assert.NotEmpty(DetailGenerator.SectionDimensions(legacy, Ctx(defs, geoOf, pathOf, legacy)).Pieces);
    }
}
