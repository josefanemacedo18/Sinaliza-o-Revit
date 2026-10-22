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
/// Fase VII – rotatória com by-pass do 4º Autoteste completo (K16): 43 pares de pisos sobrepostos no modelo final. Os lóbulos
/// do by-pass eram separados da pista a 2 cm e unidos de volta, e o vão virava uma tira de meio-fio de 2 cm colada na ilha;
/// a abertura da ilha (−0,75 m / +0,75 m) ainda passava até 0,4 mm por cima dessa tira.
/// </summary>
public class Fase07InterseccoesTests(ITestOutputHelper output)
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    private static RoundaboutDefinition Bypass() => FCRevitFullRun4Tests.Fixture().Single(x => x.Type == TipoRotatoria.ComBypass);

    [Fact]
    public void Bypass_DoAutoteste4_SemPisosSobrepostosNoCruNemNoContornoDoRevit()
    {
        var d = Bypass();
        var geo = MarkingBuilder.Build(d, null, new BuildContext { Catalog = Cat });
        var raw = ElementPlan.RevitPieces(d, geo).Where(PieceOverlaps.IsFloorPiece).ToList();
        // Antes: 4 pares crus (2–5 cm² cada, ilha do by-pass × meio-fio externo) e 30 pares (111 L) na emulação do Revit.
        Assert.Empty(PieceOverlaps.Find(raw));
        var fitted = PieceOverlaps.Find(FCRevitFullRun4Tests.Fitted(raw, geo.ArcCenters));
        Assert.True(fitted.Sum(o => o.Volume) * 1e6 < 100, $"{fitted.Count} pares, {fitted.Sum(o => o.Volume) * 1e6:0} cm³");
    }

    [Fact]
    public void Bypass_MeioFioExternoSemTiraEstreita()
    {
        // Largura média de cada peça do meio-fio externo (área ÷ extensão pelo eixo): a tira do vão tinha 2 cm.
        foreach (var d in new[] { Bypass(), Configured() })
        {
            var L = RoundaboutGenerator.Layout(d);
            Assert.NotEmpty(L.Curb);
            // Peças abaixo de 1 cm² o gerador descarta (não viram piso).
            foreach (var c in L.Curb.Where(c => c.Area > 1e-4))
            {
                var w = c.Area / ComponentQuantities.StripLength(c);
                output.WriteLine($"meio-fio {c.Area:0.000} m², largura média {w * 100:0.0} cm");
                Assert.True(w > 0.10, $"peça de {c.Area:0.000} m² com largura média de {w * 100:0.0} cm");
            }
        }
    }

    [Fact]
    public void Bypass_IlhasDentroDaPistaRemodelada()
    {
        // A ilha do by-pass (e o meio-fio dela) fica inteira dentro da área da rotatória, sem tocar o meio-fio externo.
        foreach (var d in new[] { Bypass(), Configured() })
        {
            var L = RoundaboutGenerator.Layout(d);
            Assert.True(L.Refuges.Count >= 2, "sem ilhas do by-pass");
            var touch = PolygonOps.TotalArea(PolygonOps.Intersect(L.SplitterCurb.Concat(L.SplitterCore).ToList(), L.Curb));
            Assert.True(touch < 1e-6, $"{touch * 1e4:0.00} cm² de ilha sobre o meio-fio externo");
        }
    }

    /// <summary>By-pass montado à mão (não só a fixture): quatro ramos em cruz.</summary>
    private static RoundaboutDefinition Configured()
    {
        var d = new RoundaboutDefinition { Center = new Vec2(0, 0) };
        d.ApplyPreset(TipoRotatoria.ComBypass);
        foreach (var a in new[] { 0.0, 90, 180, 270 }) d.Legs.Add(new RoundaboutLeg { AngleDeg = a, Width = 7 });
        return d;
    }

    // ------------------------------------------------------------------ F7b: volumes por estaca ao lado da malha

    /// <summary>Via de 200 m (plataforma de 12 m a +2) num terreno que sobe de +3 a +7, cruzada em x = 100 por um nó (pad).</summary>
    private static (GradingResult Design, Func<Vec2, double?> Ground, Polyline2 Axis) Projeto(bool comNo)
    {
        var c = new GradeCorridor { CutSlope = 1.0, FillSlope = 1.5, Owner = "via" };
        for (var x = 0.0; x <= 200; x += 5) { c.Left.Add(new Vec3(x, 6, 2)); c.Right.Add(new Vec3(x, -6, 2)); }
        var pads = comNo
            ? new[] { new GradePad(Polygon2.Rectangle(new Vec2(90, -15), new Vec2(110, 15)), 2) { Owner = "no", CutSlope = 1.0, FillSlope = 1.5 } }
            : Array.Empty<GradePad>();
        Func<Vec2, double?> ground = p => 3 + 0.02 * p.X;
        return (Grading.Design(new[] { c }, pads, ground), ground, new Polyline2(new[] { new Vec2(0, 0), new Vec2(200, 0) }));
    }

    [Fact]
    public void VolumesPorEstaca_DaVia_ConferemComAParteDelaNaMalha()
    {
        var (design, ground, axis) = Projeto(comNo: false);
        var malha = design.ByOwner["via"];
        var sec = SectionVolumes.ForOwner(design, "via", axis, ground, 30);
        output.WriteLine($"corte: seções {sec.CorteM3:0.0} m³ × malha {malha.Cut:0.0} m³; aterro {sec.AterroM3:0.0} × {malha.Fill:0.0}");
        Assert.True(malha.Cut > 1000);
        Assert.InRange(sec.CorteM3, malha.Cut * 0.97, malha.Cut * 1.03);
        // Estacas a cada 20 m, do início ao fim; a área de corte cresce com o terreno.
        Assert.Equal(11, sec.Secoes.Count);
        Assert.True(sec.Secoes[^1].Corte > sec.Secoes[0].Corte);
    }

    [Fact]
    public void VolumesPorEstaca_NoQueCruzaAVia_FicaFora()
    {
        var (design, ground, axis) = Projeto(comNo: true);
        var sec = SectionVolumes.ForOwner(design, "via", axis, ground, 30);
        var semDono = SectionVolumes.Compute(axis, design.DesignZ, ground, 20, 30);
        output.WriteLine($"via: seções {sec.CorteM3:0.0} × malha {design.ByOwner["via"].Cut:0.0}; nó (malha) {design.ByOwner["no"].Cut:0.0}; sem filtro {semDono.CorteM3:0.0}");
        // A seção em x = 100 cai no nó: sem área da via ali; sem o filtro de dono, o nó entrava no volume da via.
        Assert.Equal(0, sec.Secoes.Single(s => Math.Abs(s.Estaca - 100) < 1e-6).Corte, 6);
        Assert.True(semDono.CorteM3 > sec.CorteM3 + 50);
        Assert.InRange(sec.CorteM3, design.ByOwner["via"].Cut * 0.9, design.ByOwner["via"].Cut * 1.1);
    }

    [Fact]
    public void VolumesPorEstaca_TabelaECsvComAComparacao()
    {
        var (design, ground, axis) = Projeto(comNo: false);
        var malha = design.ByOwner["via"];
        var v = new SecoesDaVia("Via local 1", HierarquiaViaria.Local, SectionVolumes.ForOwner(design, "via", axis, ground, 30), malha.Cut, malha.Fill);
        var rows = SectionVolumes.Rows(new[] { v });
        Assert.Equal(11, rows.Count);
        Assert.Equal(AlignmentElements.Estaca(0), rows[0].Estaca);
        Assert.Equal(v.Volumes.CorteM3, rows[^1].CorteAcumulado, 6);
        Assert.Equal(rows[^1].CorteAcumulado - rows[^1].AterroAcumulado, rows[^1].Massa, 9);
        var csv = SectionVolumes.Csv(new[] { v });
        Assert.StartsWith("TERRAPLENAGEM POR ESTACA", csv);
        Assert.Contains("Via local 1 – corte: seções", csv);
        Assert.Contains($"Via local 1;{AlignmentElements.Estaca(200)};200,00;", csv);
        Assert.Matches(@"\([+−]\d+,\d %\)", v.Comparacao);
        Assert.Null(SecoesDaVia.Diferenca(5, 0));
    }

    [Fact]
    public void VolumesPorEstaca_NoCsvENaPlanilhaDoQuantitativo()
    {
        var (design, ground, axis) = Projeto(comNo: false);
        var malha = design.ByOwner["via"];
        var vias = new[] { new SecoesDaVia("Via local 1", HierarquiaViaria.Local, SectionVolumes.ForOwner(design, "via", axis, ground, 30), malha.Cut, malha.Fill) };
        var rows = QuantityCalculator.AddEarthworks(new List<QuantityRow>(), new[] { new EarthworkItem("Via local 1", HierarquiaViaria.Local, malha.Cut, malha.Fill, malha.Area) });
        var csv = QuantityCalculator.ToCsv(rows, null, "P", null, vias);
        Assert.Contains("TERRAPLENAGEM POR ESTACA", csv);
        Assert.Contains("Via local 1 – corte: seções", csv);
        Assert.DoesNotContain("POR ESTACA", QuantityCalculator.ToCsv(rows, null, "P"));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(QuantityWorkbook.Build(rows, null, "P", null, null, vias)));
        using var sr = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = sr.ReadToEnd();
        Assert.Contains("TERRAPLENAGEM POR ESTACA", sheet);
        Assert.Contains(">Massa acumulada (m³)<", sheet);
    }
}

