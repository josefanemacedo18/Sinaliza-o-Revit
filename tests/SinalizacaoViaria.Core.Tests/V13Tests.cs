using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Tipos de rotatória, fundo da faixa de ônibus, famílias urbanas, quantitativo, chamada livre e cota de seção.</summary>
public class V13Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    private static RoundaboutDefinition Roundabout(TipoRotatoria type, int legs = 4)
    {
        var rb = new RoundaboutDefinition();
        rb.ApplyPreset(type);
        for (int i = 0; i < legs; i++) rb.Legs.Add(new RoundaboutLeg { AngleDeg = i * 360.0 / legs });
        return rb;
    }

    [Theory]
    [InlineData(TipoRotatoria.Mini)]
    [InlineData(TipoRotatoria.Compacta)]
    [InlineData(TipoRotatoria.UmaFaixa)]
    [InlineData(TipoRotatoria.DuasFaixas)]
    [InlineData(TipoRotatoria.Turbo)]
    [InlineData(TipoRotatoria.Oval)]
    [InlineData(TipoRotatoria.ComBypass)]
    public void Roundabout_AllTypesBuild(TipoRotatoria type)
    {
        var rb = Roundabout(type);
        Assert.Equal(type, rb.Type);
        var L = RoundaboutGenerator.Layout(rb);
        Assert.NotEmpty(L.Pavement);
        Assert.DoesNotContain(L.Pavement, p => p.Contains(Vec2.Zero));   // ilha central não é pista
        var geo = MarkingBuilder.Build(rb, null, Ctx);
        Assert.NotEmpty(geo.Pieces);
        var ch = RoundaboutGenerator.Children(rb, L, new OutputSettings(), 0);
        Assert.Equal(4, ch.Count(c => c is LinearMarkingDefinition { Code: "LDP" }));
    }

    [Fact]
    public void Roundabout_PresetsFollowReferenceSizes()
    {
        var mini = Roundabout(TipoRotatoria.Mini);
        var two = Roundabout(TipoRotatoria.DuasFaixas);
        Assert.True(mini.OuterRadius * 2 < 28, $"mini D = {mini.OuterRadius * 2:0.0}");
        Assert.Equal(TipoIlhaCentral.Galgavel, mini.IslandType);
        Assert.Equal(2, two.Lanes);
        Assert.True(two.OuterRadius > mini.OuterRadius);
        var oval = Roundabout(TipoRotatoria.Oval);
        var L = RoundaboutGenerator.Layout(oval);
        var (mn, mx) = L.Island.Bounds;
        Assert.True(Math.Abs((mx.X - mn.X) - (mx.Y - mn.Y)) > 1, "ilha oval deve ser alongada");
    }

    [Fact]
    public void Roundabout_TurboHasDividers_BypassHasIslands()
    {
        var turbo = RoundaboutGenerator.Layout(Roundabout(TipoRotatoria.Turbo));
        Assert.NotEmpty(turbo.Dividers);
        var rb = Roundabout(TipoRotatoria.ComBypass);
        var plain = Roundabout(TipoRotatoria.UmaFaixa);
        var lb = RoundaboutGenerator.Layout(rb);
        var lp = RoundaboutGenerator.Layout(plain);
        Assert.True(lb.SplitterCore.Count > lp.SplitterCore.Count, "o by-pass cria ilhas separadoras adicionais");
    }

    [Fact]
    public void BusLane_ColoredBackgroundOptional()
    {
        ElementoSecao Bus(bool fundo) => new() { Tipo = TipoElementoSecao.FaixaExclusiva, Largura = 3.5, FundoOnibus = fundo, CorOnibus = MarkingColor.Vermelha };
        var off = new RoadSetup { Right = { Bus(false) }, Left = { Bus(false) } }.Build(new PathReference(), new OutputSettings(), Cat);
        Assert.DoesNotContain(off, d => d is LinearMarkingDefinition { Code: "ONI-FD" });
        var on = new RoadSetup { Right = { Bus(true) }, Left = { Bus(false) } }.Build(new PathReference(), new OutputSettings(), Cat);
        var fd = Assert.Single(on.OfType<LinearMarkingDefinition>(), d => d.Code == "ONI-FD");
        Assert.Equal(MarkingColor.Vermelha, fd.ColorOverride);
        Assert.True(fd.WidthOverride < 3.5);   // entre as linhas de bordo
        Assert.NotNull(Cat.Linear("ONI-FD"));
    }

    [Fact]
    public void RoadSetup_NoDuplicatedDevicesOnSameAlignment()
    {
        var setup = new RoadSetup
        {
            Right = { new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5, Dispositivo = "DEF" } },
            Left = { new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = 3.5 } },
            Center = CenterTreatment.LFO2,
        };
        var defs = setup.Build(new PathReference(), new OutputSettings(), Cat);
        var devs = defs.OfType<DeviceMarkingDefinition>().ToList();
        Assert.Equal(devs.Count, devs.Select(d => (d.Code, Math.Round(d.Offset, 2))).Distinct().Count());
    }

    [Fact]
    public void Devices_GroupedByFamily_GuardRailInsideBlocks()
    {
        Assert.Contains(Cat.Dispositivos, d => d.Codigo == "DEF" && d.Familia.Contains("guard rail"));
        Assert.True(Cat.Dispositivos.Select(d => d.Familia).Distinct().Count() >= 4);
        Assert.Equal(Cat.Dispositivos.Count, Cat.Dispositivos.Select(d => d.Codigo).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void UrbanCategories_GuessAndParse()
    {
        Assert.Equal(CategoriaUrbana.Iluminacao, UrbanCategories.Guess("Poste LED 8m"));
        Assert.Equal(CategoriaUrbana.Vegetacao, UrbanCategories.Guess("Árvore Ipê amarelo"));
        Assert.Equal(CategoriaUrbana.Assentos, UrbanCategories.Guess("Banco de concreto"));
        Assert.Equal(CategoriaUrbana.Residuos, UrbanCategories.Guess("Lixeira dupla"));
        Assert.Equal(CategoriaUrbana.Outros, UrbanCategories.Guess("XYZ-123"));
        foreach (var c in UrbanCategories.All) Assert.Equal(c, UrbanCategories.Parse(UrbanCategories.Label(c)));
        Assert.Equal(CategoriaUrbana.Iluminacao, UrbanCategories.Of(FormaMobiliario.PostePedestre));
    }

    [Fact]
    public void FamilyLayout_SpacingSidesAndStagger()
    {
        var path = new Polyline2(new[] { Vec2.Zero, new Vec2(40, 0) });
        var one = FamilyLayout.Along(path, 10, 0, 0, 2, false, false, 0);
        Assert.Equal(5, one.Count);
        Assert.All(one, s => Assert.Equal(2, s.Position.Y, 6));
        var both = FamilyLayout.Along(path, 10, 0, 0, 2, true, true, 0);
        Assert.Equal(one.Count + 4, both.Count);                 // 2º lado com meio espaçamento: 5, 15, 25, 35
        Assert.Contains(both, s => Math.Abs(s.Position.Y + 2) < 1e-6 && Math.Abs(s.Position.X - 5) < 1e-6);
    }

    [Fact]
    public void Quantities_FamiliesAddedWithSubcategories()
    {
        var bench = new UrbanElementDefinition { Code = "BANCO", Position = Vec2.Zero };
        var rows = QuantityCalculator.Compute(new[] { (bench as MarkingDefinition, MarkingBuilder.Build(bench, null, Ctx)) }, Cat);
        var urban = rows.First(r => r.Code == "BANCO");
        Assert.Equal(UrbanCategories.Label(CategoriaUrbana.Assentos), urban.SubcategoryName);
        var fams = new[]
        {
            new FamilyItem("POSTE-LED", "Poste LED", "Poste : 8 m", CategoriaUrbana.Iluminacao, null),
            new FamilyItem("poste-led", "Poste LED", "Poste : 10 m", CategoriaUrbana.Iluminacao, null),
            new FamilyItem("IPE", "Ipê amarelo", "Árvore : Ipê", CategoriaUrbana.Vegetacao, HierarquiaViaria.Local),
        };
        var all = QuantityCalculator.AddFamilies(rows, fams);
        var poste = Assert.Single(all, r => r.IsFamily && r.Code == "POSTE-LED");
        Assert.Equal(2, poste.Units);
        Assert.Contains("8 m", poste.FamilyTypes);
        Assert.Contains("10 m", poste.FamilyTypes);
        Assert.Equal(CategoriaQuantitativo.MobiliarioUrbano, poste.Category);
        var subs = QuantityCalculator.SubcategorySummary(all).Where(s => s.Category == CategoriaQuantitativo.MobiliarioUrbano).ToList();
        Assert.Contains(subs, s => s.Name == UrbanCategories.Label(CategoriaUrbana.Iluminacao) && s.Units == 2);
        Assert.Contains("Subcategoria", QuantityCalculator.ToCsv(all));
        Assert.Contains("Famílias/tipos", poste.DetailText);
    }

    private static (SignPlanDetailDefinition D, BuildContext C) SignDetailCase()
    {
        var sign = new SignDefinition { Code = "R-1", Position = Vec2.Zero };
        var d = new SignPlanDetailDefinition { SignId = sign.Id, OffsetMm = new Vec2(30, 30), Number = "P01" };
        var ctx = new BuildContext { Catalog = Cat, ViewScale = 100, Lookup = id => id == sign.Id ? sign : null };
        return (d, ctx);
    }

    [Fact]
    public void SignDetail_FreeLeaderPassesThroughElbows()
    {
        var (d, ctx) = SignDetailCase();
        d.LeaderStyle = EstiloChamada.Livre;
        d.ElbowsMm = new List<Vec2> { new(0, 30), new(15, 30) };
        var g = DetailGenerator.SignDetail(d, ctx);
        var leader = g.Annotations.OfType<AnnotationLine>().Single(l => l.Color == MarkingColor.Vermelha);
        Assert.Equal(4, leader.Points.Count);                                     // ponta, 2 vértices, borda do símbolo
        Assert.True(leader.Points[1].DistanceTo(new Vec2(0, 3)) < 1e-6);          // 30 mm × 1:100 = 3 m
        Assert.True(leader.Points[2].DistanceTo(new Vec2(1.5, 3)) < 1e-6);

        d.LeaderStyle = EstiloChamada.Cotovelo;
        g = DetailGenerator.SignDetail(d, ctx);
        leader = g.Annotations.OfType<AnnotationLine>().Single(l => l.Color == MarkingColor.Vermelha);
        Assert.Equal(3, leader.Points.Count);
        Assert.Equal(leader.Points[1].Y, leader.Points[2].Y, 6);                  // trecho final horizontal

        d.Terminal = TerminalChamada.Seta;
        d.AnchorMm = new Vec2(5, 0);
        g = DetailGenerator.SignDetail(d, ctx);
        Assert.Contains(g.Pieces, p => p.Color == MarkingColor.Vermelha && p.Shape.Outer.Count == 3);
        d.NumberBubble = true;
        g = DetailGenerator.SignDetail(d, ctx);
        Assert.Contains(g.Annotations.OfType<AnnotationText>(), t => t.Text == "P01");
    }

    private static MarkingGeometry Section(Action<SectionDimensionDefinition>? edit = null)
    {
        var (defs, geo, path) = DetailGenerator.SectionSample();
        var sd = new SectionDimensionDefinition { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45) };
        edit?.Invoke(sd);
        var ctx = new BuildContext { Catalog = Cat, ViewScale = 100, AllDefinitions = () => defs, GeometryOf = geo, PathOf = path };
        return DetailGenerator.SectionDimensions(sd, ctx);
    }

    [Fact]
    public void SectionDimensions_LabelsAxisAndMarkers()
    {
        var g = Section();
        var texts = g.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("Calçada", texts);
        Assert.Contains("Ciclofaixa", texts);
        Assert.Contains(texts, t => t.StartsWith("Faixa de") || t == "Pista");
        Assert.Contains("EIXO", texts);
        Assert.Contains(texts, t => t.StartsWith("SEÇÃO A–A"));
        Assert.Contains("16,90", texts);                                          // total de alinhamento a alinhamento
        Assert.Contains("3,00", texts);                                           // calçada
        Assert.Contains("0,15", texts);                                           // meio-fio cotado à parte
        Assert.Contains("1,60", texts);                                           // ciclofaixa: do eixo da LBO à borda da pintura

        var plain = Section(s => { s.Labels = false; s.AxisMarker = false; s.SectionLetter = ""; s.Terminal = TerminalCota.Seta; s.Decimals = 1; });
        var t2 = plain.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.DoesNotContain("Calçada", t2);
        Assert.DoesNotContain("EIXO", t2);
        Assert.Contains("16,9", t2);
        Assert.Contains(plain.Pieces, p => p.Color == MarkingColor.Preta);        // setas cheias

        var split = Section(s => s.SplitAtAxis = true);
        Assert.True(split.UnitCount >= Section().UnitCount);
    }

    [Fact]
    public void QuantityTable_NumbersItems()
    {
        var bench = new UrbanElementDefinition { Code = "BANCO", Position = Vec2.Zero };
        var all = new List<MarkingDefinition> { bench };
        var ctx = new BuildContext { Catalog = Cat, ViewScale = 100, AllDefinitions = () => all, GeometryOf = d => MarkingBuilder.Build(d, null, Ctx) };
        var g = DetailGenerator.QuantityTable(new QuantityTableDefinition(), ctx);
        var texts = g.Annotations.OfType<AnnotationText>().Select(t => t.Text).ToList();
        Assert.Contains("ITEM", texts);
        Assert.Contains($"{(int)CategoriaQuantitativo.MobiliarioUrbano + 1}.1", texts);
    }

    [Fact]
    public void NorthArrow_Styles()
    {
        foreach (var st in Enum.GetValues<EstiloNorte>())
        {
            var g = DetailGenerator.NorthArrow(new NorthArrowDefinition { Style = st }, Ctx);
            Assert.Contains(g.Annotations.OfType<AnnotationText>(), t => t.Text == "N");
            Assert.NotEmpty(g.Pieces);
        }
        var rose = DetailGenerator.NorthArrow(new NorthArrowDefinition { Style = EstiloNorte.RosaDosVentos }, Ctx);
        Assert.Equal(4, rose.Annotations.OfType<AnnotationText>().Count());
    }
}
