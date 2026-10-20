using System.Text.Json;
using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Serialization;
using Xunit;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// FB (Autoteste completo mais rápido): o plugin varre as marcas do projeto lendo só o tipo no começo do JSON gravado
/// (<see cref="DefinitionKinds"/>) e o id do conjunto/da marca citado no texto (MarkingStorage.Mentioning), sem desserializar
/// as demais. Estes testes garantem que o atalho nunca descarta uma marca que a leitura completa aceitaria.
/// </summary>
public class FBAutotestSpeedTests
{
    private static IEnumerable<Type> DerivedTypes() => typeof(MarkingDefinition)
        .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), false).Cast<JsonDerivedTypeAttribute>().Select(a => a.DerivedType);

    private static MarkingDefinition New(Type t) => (MarkingDefinition)Activator.CreateInstance(t)!;

    [Fact]
    public void EveryStoredTypeIsReadFromTheStartOfTheJson()
    {
        var types = DerivedTypes().ToList();
        Assert.True(types.Count > 40);
        foreach (var t in types)
        {
            var json = New(t).ToJson();
            Assert.Equal(t, DefinitionKinds.TypeOf(json));
            // O mesmo com o JSON indentado (gravações antigas ou editadas à mão).
            var indented = JsonSerializer.Serialize(New(t), JsonConfig.Options);
            Assert.Equal(t, DefinitionKinds.TypeOf(indented));
        }
    }

    [Fact]
    public void MayBeMatchesTheTypeTestOfTheFullRead()
    {
        foreach (var t in DerivedTypes())
        {
            var json = New(t).ToJson();
            var read = MarkingDefinition.FromJson(json)!;
            Assert.Equal(read is IntersectionDefinition, DefinitionKinds.MayBe<IntersectionDefinition>(json));
            Assert.Equal(read is RoadPavementDefinition, DefinitionKinds.MayBe<RoadPavementDefinition>(json));
            Assert.Equal(read is IAnnotationDefinition, DefinitionKinds.MayBe<IAnnotationDefinition>(json));
            Assert.Equal(read is IProjectWideAnnotation, DefinitionKinds.MayBe<IProjectWideAnnotation>(json));
            Assert.True(DefinitionKinds.MayBe<MarkingDefinition>(json));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"id\":\"abc\",\"tipo\":\"linha\"}")]
    [InlineData("{\"tipo\":\"tipo-que-nao-existe\"}")]
    [InlineData("{\"tipo\":\"lin\\u0068a\"}")]
    [InlineData("{\"tipo\":")]
    [InlineData("{\"$type\":\"linha\"}")]
    public void UnknownOrUnexpectedTextIsNeverDiscarded(string? json)
    {
        Assert.Null(DefinitionKinds.TypeOf(json));
        Assert.True(DefinitionKinds.MayBe<IntersectionDefinition>(json));
        Assert.True(DefinitionKinds.MayBe<RoadPavementDefinition>(json));
    }

    [Fact]
    public void DiscriminatorIgnoresWhitespace()
    {
        var kind = DefinitionKinds.Discriminator(new LinearMarkingDefinition().ToJson())!;
        Assert.Equal(kind, DefinitionKinds.Discriminator($" {{\n  \"tipo\" :  \"{kind}\", \"id\": \"x\" }}"));
        Assert.Equal(typeof(LinearMarkingDefinition), DefinitionKinds.TypeOf($"{{ \"tipo\" : \"{kind}\" }}"));
    }

    [Fact]
    public void IdsUsedByTheTextFilterAppearVerbatimInTheStoredJson()
    {
        // MarkingStorage.InGroup / Mentioning: o JSON é descartado quando não contém o id procurado. Os ids do plugin (Guid "N")
        // e do Revit (UniqueId com hífens) precisam sair no JSON sem escape, no conjunto, nos recortes e no caminho.
        var group = Guid.NewGuid().ToString("N");
        var source = Guid.NewGuid().ToString("N");
        const string uniqueId = "5f3b1a2c-7d4e-4c1b-9a8f-0123456789ab-0004a1b2";
        foreach (var t in DerivedTypes())
        {
            var d = New(t);
            d.GroupId = group;
            d.Exclusions.Add(new ExclusionZone { SourceId = source, Points = new List<Vec2> { new(0, 0), new(1, 0), new(1, 1) } });
            var json = d.ToJson();
            Assert.Contains(group, json, StringComparison.Ordinal);
            Assert.Contains(source, json, StringComparison.Ordinal);
            Assert.Contains(d.Id, json, StringComparison.Ordinal);
        }
        var line = new LinearMarkingDefinition { Code = "LFO-1", PathRef = PathReference.FromElements(new[] { uniqueId, uniqueId + ":3" }) };
        var lj = line.ToJson();
        Assert.Contains(uniqueId, lj, StringComparison.Ordinal);
        Assert.Contains(PathReference.OwnerOf(uniqueId + ":3"), lj, StringComparison.Ordinal);
    }

    [Fact]
    public void HostRoadAndRoundaboutLegsAreFoundByTheGroupText()
    {
        // RoadWorks.Hosted e a rotatória procuram pelo id do conjunto da via: ele precisa estar no texto.
        var group = Guid.NewGuid().ToString("N");
        var bridge = new BridgeDefinition { HostRoad = group };
        Assert.Contains(group, bridge.ToJson(), StringComparison.Ordinal);
        var rb = new RoundaboutDefinition();
        rb.Legs.Add(new RoundaboutLeg { GroupId = group });
        Assert.Contains(group, rb.ToJson(), StringComparison.Ordinal);
    }
}
