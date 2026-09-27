using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>Rodada H: perfil da seção separado, infraestrutura (drenagem, obras de arte, contenções, nós viários) e terraplenagem.</summary>
public class V19Tests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static Polyline2 Straight(double len) => new(new[] { new Vec2(0, 0), new Vec2(len, 0) });

    // ------------------------------------------------------------------ núcleo 3D

    [Fact]
    public void VerticalProfile_HumpWithVerticalCurves()
    {
        var p = VerticalProfile.Hump(300, 0, 7.5, 0, 150, 150, 60);
        Assert.Equal(0, p.Z(0), 6);
        Assert.Equal(0, p.Z(300), 6);
        // Um PIV só no topo: a curva vertical (L = 60 m) rebaixa o vértice em (g2 − g1)·L/8 = 0,75 m.
        Assert.Equal(6.75, p.Z(150), 3);
        Assert.Equal(7.5, VerticalProfile.Hump(300, 0, 7.5, 0, 120, 120, 60).Z(150), 6);   // topo plano
        Assert.InRange(p.MaxGrade, 0.049, 0.051);
        // Contínua: sem saltos entre estações vizinhas.
        for (var s = 0.0; s < 300; s += 0.5) Assert.True(Math.Abs(p.Z(s + 0.5) - p.Z(s)) < 0.03);
    }

    [Fact]
    public void Sweep_MergesStraightLinearChunks_AndTriangulatesSides()
    {
        var geo = new MarkingGeometry();
        var prof = VerticalProfile.Linear(100, 0, 5);
        SolidSweep.Along(geo, Straight(100), s => SolidSweep.Rect(-2, 2, prof.Z(s) - 0.3, prof.Z(s)), MarkingColor.Concreto, 0, 100, 4);
        Assert.Single(geo.Pieces);                               // reta com rampa constante = um sólido
        var solid = geo.Pieces[0].Solid!;
        Assert.All(solid.Faces.Where(f => f.Count > 3), f =>
        {
            // Tampas planas (4 pontos no mesmo plano vertical).
            var n = Polyhedron.Normal(f);
            Assert.All(f, v => Assert.True(Math.Abs((v - f[0]).Dot(n)) < 1e-6));
        });
        Assert.Equal(5, solid.MaxZ, 3);
        var curved = new MarkingGeometry();
        var arc = new Polyline2(CurveTools.Arc(Vec2.Zero, 50, 0, Math.PI / 2, 0.05));
        SolidSweep.Along(curved, arc, _ => SolidSweep.Rect(-2, 2, 0, 1), MarkingColor.Concreto, 0, double.NaN, 4);
        Assert.True(curved.Pieces.Count > 5);
    }

    // ------------------------------------------------------------------ terraplenagem

    [Fact]
    public void Grading_CutCorridor_DaylightsAtSlopeAndComputesVolumes()
    {
        // Terreno plano a +5; plataforma de 10 m a +2 (corte de 3 m), talude de corte 1:1.
        var c = new GradeCorridor { CutSlope = 1.0, FillSlope = 1.5 };
        for (var x = 0.0; x <= 50; x += 5) { c.Left.Add(new Vec3(x, 5, 2)); c.Right.Add(new Vec3(x, -5, 2)); }
        var res = Grading.Design(new[] { c }, Array.Empty<GradePad>(), _ => 5.0);
        var day = res.Points.Where(p => Math.Abs(p.Z - 5) < 0.05).ToList();
        Assert.NotEmpty(day);
        Assert.All(day, p => Assert.InRange(Math.Abs(p.Y), 7.8, 8.2));  // 5 + 3 × 1
        Assert.Equal(2, res.DesignZ(new Vec2(25, 0))!.Value, 3);
        Assert.Equal(3.5, res.DesignZ(new Vec2(25, 6.5))!.Value, 1);    // meio do talude
        Assert.Null(res.DesignZ(new Vec2(25, 20)));
        // Corte ≈ 50 × (10 × 3 + 2 × ½ × 3 × 3) = 1950 m³.
        Assert.InRange(res.CutM3, 1800, 2100);
        Assert.InRange(res.FillM3, 0, 30);
    }

    [Fact]
    public void Grading_FillPad_SlopesDownToGround()
    {
        var pad = new GradePad(Polygon2.Rectangle(new Vec2(-10, -10), new Vec2(10, 10)), 3) { FillSlope = 2 };
        var res = Grading.Design(Array.Empty<GradeCorridor>(), new[] { pad }, _ => 0.0);
        Assert.Equal(3, res.DesignZ(Vec2.Zero)!.Value, 6);
        Assert.Contains(res.Points, p => Math.Abs(p.Z) < 0.05 && Math.Abs(p.X) > 15.5);   // pé do aterro a 10 + 3 × 2
        Assert.InRange(res.FillM3, 1200, 2600);
    }

    [Fact]
    public void Daylight_FollowsSlopedGround()
    {
        // Terreno subindo 0,5 m/m para +y; borda a 0 em y=0: corte 1:1 encontra o terreno quando d = 0,5 d + ... (z0 = 0 < g0 = 0? não).
        var p = Grading.Daylight(new Vec2(0, 0), -2, Vec2.UnitY, 1, 1.5, q => 0.5 * q.Y);
        // Corte: z = -2 + d ; terreno = 0,5 d → d = 4.
        Assert.Equal(4, p.Y, 1);
        Assert.Equal(2, p.Z, 1);
    }

    // ------------------------------------------------------------------ drenagem

    [Theory]
    [InlineData(TipoDrenagem.BocaDeLoboSimples)]
    [InlineData(TipoDrenagem.BocaDeLoboDupla)]
    [InlineData(TipoDrenagem.BocaDeLoboGrelha)]
    [InlineData(TipoDrenagem.BocaDeLoboCombinada)]
    [InlineData(TipoDrenagem.GrelhaSarjeta)]
    [InlineData(TipoDrenagem.GrelhaQuadrada)]
    [InlineData(TipoDrenagem.PocoDeVisita)]
    public void Drainage_PointTypesBuild(TipoDrenagem type)
    {
        var d = new DrainageDefinition { Type = type, Position = new Vec2(10, 0), Along = Vec2.UnitX, SidewalkLeft = true };
        d.ApplyDefaults();
        var g = MarkingBuilder.Build(d, null, Ctx);
        Assert.NotEmpty(g.Pieces);
        Assert.All(g.Pieces, p => Assert.NotNull(p.Solid));
        Assert.True(g.Pieces.Min(p => p.Solid!.MinZ) < -0.5, "caixa enterrada");
        if (type is TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla)
            // Caixa sob a calçada: do lado +y (calçada à esquerda do meio-fio).
            Assert.True(g.Pieces.Where(p => p.Layer == "CAIXA").All(p => p.Shape.Centroid.Y > 0));
        if (type is TipoDrenagem.GrelhaSarjeta or TipoDrenagem.BocaDeLoboGrelha)
            Assert.True(g.Pieces.Where(p => p.Layer == "GRELHA").All(p => p.Shape.Centroid.Y < 0), "grelha na sarjeta, do lado da pista");
        var info = MarkingBuilder.Describe(d, Cat);
        Assert.Equal("un", info.Unit);
    }

    [Fact]
    public void Drainage_ContinuousGrate_AlongPath()
    {
        var d = new DrainageDefinition { Type = TipoDrenagem.GrelhaContinua };
        d.ApplyDefaults();
        var g = MarkingBuilder.Build(d, Straight(6), Ctx);
        Assert.Equal(6, g.PaintedLength, 3);
        Assert.Equal("m", MarkingBuilder.Describe(d, Cat).Unit);
        Assert.True(g.Pieces.Count(p => p.Layer == "GRELHA") > 50);
    }

    // ------------------------------------------------------------------ obras de arte

    [Fact]
    public void Bridge_ApproachesStructurePiersAndDeckArea()
    {
        var b = new BridgeDefinition();
        var g = MarkingBuilder.Build(b, Straight(400), Ctx);
        Assert.Empty(g.Warnings);
        var (prof, s0, s1, _) = BridgeGenerator.Profile(b, Straight(400), _ => 0);
        Assert.True(s0 > 50 && s1 < 350 && s1 > s0);
        Assert.Equal(b.Height, prof.Z(200), 3);
        Assert.True(g.UnitCount >= 2, "pilares");
        Assert.Contains(g.Pieces, p => p.Layer == "ATERRO");
        Assert.Contains(g.Pieces, p => p.Layer == "PILAR");
        Assert.Contains(g.Pieces, p => p.Layer == "BARREIRA");
        Assert.Equal((s1 - s0) * 2 * BridgeGenerator.Spec(b).DeckHalf, g.AreaByColor[MarkingColor.Concreto], 1);
        Assert.Equal(2, g.Corridors.Count);                     // rampas de acesso terraplenadas
        Assert.Equal("m²", MarkingBuilder.Describe(b, Cat).Unit);
    }

    [Theory]
    [InlineData(SistemaEstrutural.CaixaoCelular)]
    [InlineData(SistemaEstrutural.LajeMacica)]
    [InlineData(SistemaEstrutural.ArcoInferior)]
    [InlineData(SistemaEstrutural.ArcoSuperior)]
    [InlineData(SistemaEstrutural.Estaiada)]
    [InlineData(SistemaEstrutural.Trelica)]
    public void Bridge_AllSystemsBuild(SistemaEstrutural sys)
    {
        var b = new BridgeDefinition { System = sys, Height = sys == SistemaEstrutural.ArcoInferior ? 20 : 8, MainSpan = 80 };
        var g = MarkingBuilder.Build(b, Straight(600), Ctx);
        Assert.NotEmpty(g.Pieces);
        Assert.All(g.Pieces, p => Assert.NotNull(p.Solid));
        if (sys == SistemaEstrutural.Estaiada) Assert.Contains(g.Pieces, p => p.Layer == "ESTAI");
        if (sys == SistemaEstrutural.ArcoSuperior) Assert.Contains(g.Pieces, p => p.Layer == "PENDURAL");
        if (sys == SistemaEstrutural.Trelica) Assert.Contains(g.Pieces, p => p.Layer == "TRELICA");
    }

    [Fact]
    public void Bridge_FreezesNaturalGround_SoGradingDoesNotFeedBack()
    {
        var b = new BridgeDefinition();
        var hill = new BuildContext { Catalog = Cat, Ground = p => 2.0 };
        MarkingBuilder.Build(b, Straight(400), hill);
        Assert.NotNull(b.GroundLine);
        Assert.All(b.GroundLine!, v => Assert.Equal(2, v.Y, 6));
        // Depois da terraplenagem o terreno "mudou" – o projeto continua sobre o terreno original.
        var graded = new BuildContext { Catalog = Cat, Ground = p => -5.0 };
        var g2 = MarkingBuilder.Build(b, Straight(400), graded);
        var (prof, _, _, _) = BridgeGenerator.Profile(b, Straight(400), _ => 2.0);
        Assert.Equal(2, prof.Z(0), 3);
        Assert.NotEmpty(g2.Pieces);
    }

    [Fact]
    public void Passarela_UsesPedestrianDefaults()
    {
        var b = new BridgeDefinition { Kind = TipoObraDeArte.Passarela };
        b.ApplyKindDefaults();
        var g = MarkingBuilder.Build(b, Straight(220), Ctx);
        Assert.Contains(g.Pieces, p => p.Layer == "COBERTURA");
        Assert.DoesNotContain(g.Pieces, p => p.Layer == "BARREIRA");
        Assert.DoesNotContain(g.Warnings, w => w.Contains("8,33"));
    }

    // ------------------------------------------------------------------ túnel, trincheira, muros, taludes

    [Theory]
    [InlineData(SecaoTunel.Ferradura, TipoEmboque.Testa)]
    [InlineData(SecaoTunel.Circular, TipoEmboque.Pala)]
    [InlineData(SecaoTunel.Retangular, TipoEmboque.Bisel)]
    public void Tunnel_SectionsKeepClearance(SecaoTunel sec, TipoEmboque portal)
    {
        var d = new TunnelDefinition { Section = sec, Portal = portal };
        var (_, crown, half) = EarthworksGenerator.TunnelSection(d);
        Assert.True(crown >= d.ClearHeight, "gabarito vertical");
        Assert.True(2 * half >= d.RoadWidth);
        var g = MarkingBuilder.Build(d, Straight(200), Ctx);
        Assert.Contains(g.Pieces, p => p.Layer == "EMBOQUE");
        Assert.Contains(g.Pieces, p => p.Layer == "VENTILACAO");
        Assert.Equal(2, g.Corridors.Count);
        Assert.Equal(200, g.PaintedLength, 3);
    }

    [Fact]
    public void Trench_LowersRoadBetweenWalls()
    {
        var d = new TrenchDefinition { Depth = 6.5, CoverLength = 14 };
        var g = MarkingBuilder.Build(d, Straight(400), Ctx);
        Assert.Empty(g.Warnings);
        var road = g.Pieces.Where(p => p.Layer == "PISTA").ToList();
        Assert.InRange(road.Min(p => p.Solid!.MaxZ), -6.6, -6.4);
        Assert.Contains(g.Pieces, p => p.Layer == "MURO");
        Assert.Contains(g.Pieces, p => p.Layer == "LAJE");
        Assert.Contains(g.Corridors, c => !c.DaylightLeft && !c.DaylightRight);   // muros contêm o terreno
    }

    [Theory]
    [InlineData(TipoMuro.Gravidade)]
    [InlineData(TipoMuro.Flexao)]
    [InlineData(TipoMuro.Contrafortes)]
    [InlineData(TipoMuro.Gabiao)]
    [InlineData(TipoMuro.TerraArmada)]
    [InlineData(TipoMuro.CortinaAtirantada)]
    public void RetainingWall_TypesBuildWithFaceArea(TipoMuro type)
    {
        var d = new RetainingWallDefinition { Type = type, HeightStart = 4, HeightEnd = 4 };
        var g = MarkingBuilder.Build(d, Straight(10), Ctx);
        Assert.NotEmpty(g.Pieces);
        Assert.Equal(40, g.AreaByColor.Values.Sum(), 1);            // 10 m × 4 m de face
        Assert.True(g.Pieces.Max(p => p.Solid!.MaxZ) >= 4 - 1e-6);
        // Reaterro do lado contido (esquerda): borda externa em y > 0 e na cota do topo.
        var back = g.Corridors.Single(c => c.Label == "Reaterro");
        Assert.All(back.Left, p => Assert.True(p.Y > 0));
        Assert.All(back.Left, p => Assert.InRange(p.Z, 4, 4.2));
    }

    [Fact]
    public void Slope_BermsAndGradeFaces()
    {
        var d = new SlopeDefinition { Height = 14, Ratio = 1.5, BermEvery = 8, BermWidth = 3 };
        var prof = EarthworksGenerator.SlopeProfile(d);
        Assert.Equal(new (double, double)[] { (0, 0), (12, 8), (15, 8), (24, 14) }, prof.Select(p => (Math.Round(p.Y, 6), Math.Round(p.Z, 6))).ToArray());
        var g = MarkingBuilder.Build(d, Straight(30), Ctx);
        Assert.Equal(3, g.Corridors.Count);
        Assert.Contains(g.Pieces, p => p.Layer == "BERMA");
        Assert.Contains(g.Pieces, p => p.Layer == "DESCIDA");
        Assert.Equal("m²", MarkingBuilder.Describe(d, Cat).Unit);
    }

    // ------------------------------------------------------------------ nós viários

    [Theory]
    [InlineData(TipoNoViario.Diamante)]
    [InlineData(TipoNoViario.DiamanteRotatorias)]
    [InlineData(TipoNoViario.TrevoCompleto)]
    [InlineData(TipoNoViario.TrevoParcial)]
    [InlineData(TipoNoViario.Trombeta)]
    [InlineData(TipoNoViario.RotatoriaElevada)]
    public void Interchange_TypesBuildWithBridges(TipoNoViario type)
    {
        var d = new InterchangeDefinition { Type = type, Lighting = false };
        var warnings = new List<string>();
        var list = InterchangeGenerator.Layout(d, _ => 0, warnings);
        Assert.True(list.Count >= 3);
        // Alguma via passa por cima de outra: há tabuleiro.
        Assert.Contains(list, a => InterchangeGenerator.StructureRanges(a, list, _ => 0, d.Clearance).Count > 0);
        var g = MarkingBuilder.Build(d, null, Ctx);
        Assert.Contains(g.Pieces, p => p.Layer == "TABULEIRO");
        Assert.Contains(g.Pieces, p => p.Layer == "PILAR");
        Assert.NotEmpty(g.Corridors);
        Assert.Equal(1, g.UnitCount);
    }

    [Fact]
    public void Interchange_Diamond_RampsWithinGrade()
    {
        var d = new InterchangeDefinition { Type = TipoNoViario.Diamante };
        var warnings = new List<string>();
        var list = InterchangeGenerator.Layout(d, _ => 0, warnings);
        Assert.DoesNotContain(warnings, w => w.Contains("rampa de"));
        Assert.Equal(6, list.Count);                            // principal, transversal e 4 rampas
        var cross = list.Single(a => a.Name == "Via transversal");
        Assert.Equal(d.Height, cross.Profile.Z(cross.Path.Length / 2), 3);
    }

    // ------------------------------------------------------------------ perfil da seção separado

    [Fact]
    public void SectionProfile_IsSeparateDetailPlacedWhereClicked()
    {
        var (defs, geo, path) = DetailGenerator.SectionSample();
        var sd = new SectionDimensionDefinition { Start = new Vec2(15, -8.45), End = new Vec2(15, 8.45) };
        var pd = SectionProfileDefinition.From(sd, new Vec2(100, 50));
        var all = defs.Concat(new MarkingDefinition[] { sd, pd }).ToList();
        var c = new BuildContext { Catalog = Cat, ViewScale = 100, AllDefinitions = () => defs, GeometryOf = geo, PathOf = path, Lookup = id => all.FirstOrDefault(x => x.Id == id) };
        var plan = DetailGenerator.SectionDimensions(sd, c);
        Assert.DoesNotContain(plan.Annotations.OfType<AnnotationText>(), t => t.Text.StartsWith("SEÇÃO TRANSVERSAL"));
        var g = MarkingBuilder.Build(pd, null, c);
        Assert.Contains(g.Annotations.OfType<AnnotationText>(), t => t.Text == "SEÇÃO TRANSVERSAL A–A");
        Assert.All(g.Annotations.OfType<AnnotationLine>().SelectMany(l => l.Points), p => Assert.True(p.X >= 99.9 && p.Y <= 50.1));
        Assert.Equal(sd.Id, pd.TargetId);
        // Cota apagada → o perfil some junto.
        var orphan = new BuildContext { Catalog = Cat, ViewScale = 100, AllDefinitions = () => defs, GeometryOf = geo, Lookup = _ => null };
        Assert.Contains(MarkingBuilder.Build(pd, null, orphan).Warnings, w => w.StartsWith(DetailGenerator.MissingTarget));
    }

    // ------------------------------------------------------------------ persistência e quantitativos

    [Fact]
    public void InfraDefinitions_RoundTripAndCategories()
    {
        var defs = new MarkingDefinition[]
        {
            new DrainageDefinition { Type = TipoDrenagem.BocaDeLoboDupla, Modules = 2 },
            new BridgeDefinition { System = SistemaEstrutural.Estaiada, GroundLine = new List<Vec2> { new(0, 1), new(10, 2) } },
            new TunnelDefinition { Section = SecaoTunel.Circular },
            new TrenchDefinition { Depth = 7 },
            new RetainingWallDefinition { Type = TipoMuro.Gabiao },
            new SlopeDefinition { Type = TipoTalude.Corte },
            new InterchangeDefinition { Type = TipoNoViario.TrevoCompleto },
            new SectionProfileDefinition { SectionId = "abc", ProfileScale = 25 },
        };
        foreach (var d in defs)
        {
            var back = MarkingDefinition.FromJson(d.ToJson())!;
            Assert.Equal(d.GetType(), back.GetType());
            Assert.Equal(d.ToJson(), back.ToJson());
        }
        Assert.Equal(CategoriaQuantitativo.Drenagem, QuantityRow.Categorize(defs[0], GrupoMarca.Urbanizacao));
        Assert.Equal(CategoriaQuantitativo.ObrasDeArteTerraplenagem, QuantityRow.Categorize(defs[1], GrupoMarca.Urbanizacao));
        Assert.True(QuantityRow.IsMemorialCategory(CategoriaQuantitativo.ObrasDeArteTerraplenagem));
        var rows = QuantityCalculator.Compute(new[] { (defs[1], MarkingBuilder.Build(defs[1], Straight(300), Ctx)) }, Cat);
        var row = Assert.Single(rows);
        Assert.Equal("m²", row.Unit);
        Assert.True(row.MainQuantity > 500);
        Assert.False(MarkingColors.IsPaint(MarkingColor.Agua));
    }

    [Fact]
    public void InfraThumbnails_Build()
    {
        foreach (var d in new MarkingDefinition[] { new DrainageDefinition(), new BridgeDefinition(), new TunnelDefinition(), new TrenchDefinition(), new RetainingWallDefinition(), new SlopeDefinition(), new InterchangeDefinition() })
        {
            var (geo, _) = QuantitySamples.Sample(d, MarkingColor.Concreto, Ctx);
            Assert.NotNull(geo);
            Assert.NotEmpty(geo!.Pieces);
        }
    }
}
