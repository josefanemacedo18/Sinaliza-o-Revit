using System.Runtime.CompilerServices;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Settings;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 17 do plano 2 – item 2 (rampas como Pisos) e item 1 (materiais realistas no modo Realista) do pedido de 2026-10-27.
/// No Revit, cada parte é um piso de camada variável levado à cota de cada vértice pela edição de forma; aqui confere-se a
/// geometria das partes (o que o Revit recebe) e a escolha dos ativos de aparência. A aparência NÃO foi vista no Revit.
/// </summary>
public class Etapa17PisosMateriaisTests
{
    private static readonly Polyline2 Caminho = new(new[] { new Vec2(10, -2), new Vec2(10, 0) });   // do meio-fio (y = −2) para a calçada

    private static (MarkingGeometry Geo, List<RampFloorPart> Parts) Rampa(RampDefinition r)
    {
        var geo = RampGenerator.Generate(r, Caminho);
        return (geo, RampFloors.Parts(r, geo));
    }

    [Theory]
    [InlineData(false, 0.0)]
    [InlineData(true, 0.0)]
    [InlineData(false, 0.005)]
    public void Item2_RebaixamentoComAbas_UmPisoPorCorpo_TopoInclinadoFundoPlano(bool square, double lip)
    {
        var r = new RampDefinition { SquareCut = square, LipHeight = lip };
        var (geo, parts) = Rampa(r);
        var bodies = geo.Pieces.Where(p => RampFloors.IsBody(r, p)).ToList();
        Assert.Equal(bodies.Count, parts.Count);
        Assert.Equal(square ? 5 : 3, parts.Count);       // rampa + 2 abas (+ 2 cantos nivelados)
        // As placas táteis continuam sólidos (não viram piso).
        Assert.Contains(geo.Pieces, p => p.Solid != null && !RampFloors.IsBody(r, p));
        foreach (var part in parts)
        {
            // Topo plano (cada corpo da rampa é um prisma de topo plano) e fundo 5 cm abaixo da base, sobre a plataforma do terreno.
            Assert.True(part.Deviation < 1e-9, $"topo empenado ({part.Deviation})");
            Assert.Equal(-InfraRoads.FolgaSobRampas, part.Bottom, 9);
            Assert.All(part.Top, v => Assert.InRange(v.Z, lip - 1e-9, r.Height + 1e-9));
            // A espessura nunca chega a zero no pé da rampa; o tipo tem centímetros inteiros e cobre o topo mais alto.
            Assert.True(part.Top.Min(v => v.Z) - part.Bottom >= InfraRoads.FolgaSobRampas - 1e-9);
            Assert.True(part.TypeThickness >= part.MaxTop - part.Bottom - 1e-9);
            Assert.Equal(Math.Round(part.TypeThickness * 100), part.TypeThickness * 100, 6);
        }
        // A rampa principal: do meio-fio (lip) ao fim da subida (altura do meio-fio), na inclinação da rampa.
        var (_, len, _) = RampGenerator.Dimensions(r);
        var main = parts.OrderByDescending(p => p.Shape.Area).First(p => p.Top.Any(v => Math.Abs(v.Z - lip) < 1e-9) && p.Top.Count == 4);
        Assert.Equal(lip, main.TopAt(new Vec2(10, -2)), 6);
        Assert.Equal(r.Height, main.TopAt(new Vec2(10, -2 + len)), 6);
        Assert.Equal((r.Height - lip) / len, main.Plane.Slope, 6);
        // As partes não se sobrepõem e ficam dentro do recorte da rampa na calçada (antes a ponta das abas passava do recorte
        // junto ao meio-fio: ~0,036 m² de cada aba sobre o meio-fio).
        var union = PolygonOps.Union(parts.Select(p => p.Shape));
        Assert.Equal(parts.Sum(p => p.Shape.Area), PolygonOps.TotalArea(union), 3);
        var fp = RampGenerator.Footprint(r, Caminho);
        Assert.True(PolygonOps.TotalArea(PolygonOps.Difference(union, new[] { fp })) < 1e-6);
    }

    [Fact]
    public void Item2_RebaixamentoTotal_PlatoPlanoERampasLaterais()
    {
        var r = new RampDefinition { Type = TipoRampa.RebaixamentoTotal, SidewalkDepth = 2.5 };
        var (_, parts) = Rampa(r);
        Assert.Equal(3, parts.Count);
        var plate = Assert.Single(parts, p => p.Plane.IsLevel);
        Assert.All(plate.Top, v => Assert.Equal(0.005, v.Z, 9));      // placa de 5 mm no nível da pista
        foreach (var side in parts.Where(p => p != plate))
        {
            Assert.Equal(r.LipHeight, side.Top.Min(v => v.Z), 9);
            Assert.Equal(r.Height, side.Top.Max(v => v.Z), 9);
            Assert.True(side.Deviation < 1e-9);
        }
    }

    [Fact]
    public void Item2_NoRelevo_PartesAcompanhamAsFacesDosPisos()
    {
        var r = new RampDefinition();
        var geo = RampGenerator.Generate(r, Caminho);
        // Rua a 4 % na direção x e 1 % em y (como o greide aplica, vértice a vértice).
        static double Lift(Vec2 v) => 1.0 + 0.04 * v.X + 0.01 * v.Y;
        GradeLift.Apply(geo, Lift);
        var parts = RampFloors.Parts(r, geo);
        var flat = RampFloors.Parts(r, RampGenerator.Generate(r, Caminho));
        Assert.Equal(flat.Count, parts.Count);
        foreach (var (a, b) in parts.Zip(flat))
        {
            foreach (var v in a.Top) Assert.Equal(b.TopAt(v.XY) + Lift(v.XY), v.Z, 6);
            // Fundo plano 5 cm abaixo do ponto mais baixo da base deformada.
            Assert.True(a.Bottom <= a.Top.Min(v => v.Z) - InfraRoads.FolgaSobRampas + 1e-9);
        }
    }

    // ------------------------------------------------------------------ item 1 – aparência

    private static readonly MaterialAppearance.Candidate[] Biblioteca =
    {
        new("p1", "Asphalt Shingles - Black", "Autodesk Material Library"),
        new("p2", "Asphalt - Wet", "Autodesk Material Library"),
        new("p3", "Asphalt", "Autodesk Material Library"),
        new("p4", "Concrete Block - Grey", "Autodesk Material Library"),
        new("p5", "Concrete - Cast-in-Place Concrete", "Autodesk Material Library"),
        new("p6", "Grass - Bermuda", "Autodesk Material Library"),
        new("p7", "Gravel - Mixed", "Autodesk Material Library"),
        new("p8", "Pavers - Herringbone", "Autodesk Material Library"),
        new("p9", "Water - Reflecting Pool", "Autodesk Material Library"),
        new("p10", "Metal - Steel, Galvanized", "Autodesk Material Library"),
        new("p11", "Glass - Clear", "Autodesk Material Library"),
        new("p12", "Soil - Earth", "Autodesk Material Library"),
        new("p13", "Terracotta Tile", "Autodesk Material Library"),
        new("p14", "Madeira - Pinho", "Biblioteca de materiais"),
    };

    [Theory]
    [InlineData(MarkingColor.Asfalto, "Asphalt")]
    [InlineData(MarkingColor.Concreto, "Concrete - Cast-in-Place Concrete")]
    [InlineData(MarkingColor.PavimentoConcreto, "Concrete - Cast-in-Place Concrete")]
    [InlineData(MarkingColor.Grama, "Grass - Bermuda")]
    [InlineData(MarkingColor.Brita, "Gravel - Mixed")]
    [InlineData(MarkingColor.Bloquete, "Pavers - Herringbone")]
    [InlineData(MarkingColor.Agua, "Water - Reflecting Pool")]
    [InlineData(MarkingColor.Metal, "Metal - Steel, Galvanized")]
    [InlineData(MarkingColor.Vidro, "Glass - Clear")]
    [InlineData(MarkingColor.Terra, "Soil - Earth")]
    [InlineData(MarkingColor.PavimentoTerra, "Soil - Earth")]
    [InlineData(MarkingColor.Madeira, "Madeira - Pinho")]
    public void Item1_AtivoDaBiblioteca_PeloTitulo(MarkingColor cor, string titulo)
    {
        Assert.True(MaterialAppearance.Textured(cor));
        Assert.Equal(titulo, MaterialAppearance.Choose(cor, Biblioteca)?.Title);
    }

    [Fact]
    public void Item1_PinturasNaCorLisa_ESemAtivoQueSirvaNada()
    {
        foreach (var c in new[] { MarkingColor.Branca, MarkingColor.Amarela, MarkingColor.Vermelha, MarkingColor.Azul, MarkingColor.Preta, MarkingColor.RelevoTatil })
        {
            Assert.False(MaterialAppearance.Textured(c));
            Assert.Null(MaterialAppearance.Choose(c, Biblioteca));
        }
        // Só telhado e bloco: nenhum serve para o asfalto e o concreto da via (a cor lisa fica).
        var ruins = new[] { new MaterialAppearance.Candidate("a", "Asphalt Shingles", "L"), new MaterialAppearance.Candidate("b", "Concrete Block", "L") };
        Assert.Null(MaterialAppearance.Choose(MarkingColor.Asfalto, ruins));
        Assert.Null(MaterialAppearance.Choose(MarkingColor.Concreto, ruins));
        // Título em português, com acento.
        Assert.Equal("Água - Piscina", MaterialAppearance.Choose(MarkingColor.Agua, new[] { new MaterialAppearance.Candidate("w", "Água - Piscina", "L") })?.Title);
    }

    [Fact]
    public void Configuracoes_RampasComoPisoEAparenciaRealista_LigadasERegistradas()
    {
        var s = new PluginSettings();
        Assert.True(s.RampsAsFloors);
        Assert.True(s.RealisticAppearance);
        var itens = RegistroConfiguracoes.DoUsuario(s);
        Assert.Contains(itens, i => i.Nome.Contains("Rampas como Piso"));
        Assert.Contains(itens, i => i.Nome.Contains("aparência realista"));
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

    private static string Fonte(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz(), "src", "SinalizacaoViaria.Revit" }.Concat(partes).ToArray()));

    [Fact]
    public void Plugin_PisoDaRampaEAparencia()
    {
        var ms = Fonte("Infrastructure", "MarkingService.cs");
        Assert.Contains("Core.Generators.RampFloors.Parts(rampDef, geo)", ms);
        Assert.Contains("if (cs.CanLayerBeVariable(0)) cs.VariableLayerIndex = 0;", ms);
        Assert.Contains("VertexZ = v => UnitConv.Ft(baseZm + part.TopAt(v))", ms);
        Assert.Contains("!rampDone.Contains(p)", ms);
        var st = Fonte("Infrastructure", "StyleService.cs");
        Assert.Contains("GetAssets(Autodesk.Revit.DB.Visual.AssetType.Appearance)", st);
        Assert.Contains("AppearanceAssetElement.Create(_doc, name, asset)", st);
        Assert.Contains("mat.UseRenderAppearanceForShading = false;", st);
        Assert.Contains("if (mat.AppearanceAssetId != ElementId.InvalidElementId || !MaterialAppearance.Textured(color)) return;", st);
        var sw = Fonte("UI", "SettingsWindow.xaml.cs");
        Assert.Contains("c.RampsAsFloors = CkRampFloors.IsChecked == true;", sw);
        Assert.Contains("c.RealisticAppearance = CkRealistic.IsChecked == true;", sw);
    }
}
