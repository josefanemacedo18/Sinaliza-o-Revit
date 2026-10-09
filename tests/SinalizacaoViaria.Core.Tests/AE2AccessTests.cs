using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AE (item 10): acessos e retornos pela ferramenta "Abrir acesso na via" – retorno pela abertura do canteiro com
/// faixa de acumulação e taper, conversão livre à direita com ilha triangular, bolsão de conversão à esquerda com zebrado
/// amarelo no taper e nova via saindo em curva com zebrado de canalização na separação. Geometria conforme as medidas
/// digitadas (±2 cm), zebrado no lugar e sem falhas de modelagem.
/// </summary>
public class AE2AccessTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();

    [Fact]
    public void Acesso_com_bolsao_ajusta_so_o_ramo_que_converte_a_esquerda()
    {
        // Principal (coletora, sem canteiro) no eixo x; nova via ao sul (clique à direita do eixo). Quem converte à esquerda
        // para o sul é quem vem do leste: o acesso pede o bolsão só no ramo leste (+1); a esquina de quem sai pela direita
        // (vem do oeste, ramo −1) recebe a faixa de conversão livre.
        var d = new IntersectionDefinition();
        AcessoVia.Configure(d, "via", false, new AcessoSpec { Tipo = TipoAcesso.BolsaoEsquerda, Espera = 40, Taper = 25, LarguraBolsao = 3.2 });
        var leg = Assert.Single(d.LegSettings);
        Assert.Equal((1, true), (leg.Sign, leg.Treatment == true));
        Assert.False(d.LeftTurnPockets);
        Assert.Equal((40.0, 25.0, 3.2), (d.PocketLength, d.PocketTaper, d.PocketWidth));
        Assert.Equal("via", d.MainRoadId);
        var e = new IntersectionDefinition();
        AcessoVia.Configure(e, "via", false, new AcessoSpec { Tipo = TipoAcesso.ConversaoLivre, RaioConversao = 30, LarguraConversao = 4.0 });
        var l2 = Assert.Single(e.LegSettings);
        Assert.Equal((-1, TipoIlha.Fisica, 30.0), (l2.Sign, l2.RightTurnChannel, l2.RightTurnRadius));
        Assert.Equal(TipoIlha.Nenhuma, e.RightTurnIslands);
        // Clique do outro lado: tudo espelhado.
        var f = new IntersectionDefinition();
        AcessoVia.Configure(f, "via", true, new AcessoSpec { Tipo = TipoAcesso.BolsaoEsquerda });
        Assert.Equal(-1, Assert.Single(f.LegSettings).Sign);
        // Ajustes por ramo novos são gravados e lidos (campos opcionais).
        var back = (IntersectionDefinition)MarkingDefinition.FromJson(e.ToJson())!;
        Assert.Equal(TipoIlha.Fisica, back.LegSettings.Single().RightTurnChannel);
        Assert.False(back.LegSettings.Single().IsEmpty);
    }

    private static readonly BuildContext Ctx = new() { Catalog = Cat };

    /// <summary>Cena como mundo de teste (para as verificações de falhas de modelagem).</summary>
    private static Y34TipTests.World W(AcessoCena c)
    {
        var w = new Y34TipTests.World();
        w.Defs.AddRange(c.Definitions);
        foreach (var (k, v) in c.Paths) w.Paths[k] = v;
        return w;
    }

    private static List<Polygon2> Pavement(List<Y34TipTests.Part> parts) => PolygonOps.Union(parts
        .Where(p => p.Piece.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto).Select(p => p.Piece.Shape));

    /// <summary>Pista até a face do meio-fio (pavimento + sarjeta).</summary>
    private static List<Polygon2> ToCurb(List<Y34TipTests.Part> parts) => PolygonOps.Union(parts
        .Where(p => p.Piece.Color is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto
                    || p.Piece.Layer == "SARJETA" || p.Def is LinearMarkingDefinition l && l.Code.StartsWith("SARJETA")
                    || p.Def is IntersectionDefinition && p.Piece.Color == MarkingColor.Concreto && p.Piece.Thickness <= 0.02 && p.Piece.Layer == null)
        .Select(p => p.Piece.Shape));

    private static bool In(List<Polygon2> ps, Vec2 p) => ps.Any(q => q.Contains(p));

    /// <summary>Bordo da pista no lado y &lt; <paramref name="yMax"/>: maior y da pista entre y0 e yMax na estaca x.</summary>
    private static double EdgeAt(List<Polygon2> pav, double x, double y0, double yMax)
    {
        var cut = new[] { Polygon2.Rectangle(new Vec2(x - 0.01, y0), new Vec2(x + 0.01, yMax)) };
        return PolygonOps.Intersect(pav, cut).SelectMany(p => p.Outer).Max(v => v.Y);
    }

    [Fact]
    public void Retorno_no_canteiro_com_faixa_de_acumulacao_e_taper_nas_medidas_digitadas()
    {
        var s = RoadTemplates.All[2].Create();               // avenida com canteiro central
        s.MedianWidth = 6.0;
        var r = new RetornoVia { Estaca = 150, SentidoDoEixo = true, Tipo = TipoRetorno.Bolsao, Espera = 30, Taper = 25, LarguraBolsao = 3.0 };
        var plan = s.PlanRetorno(r);
        var op = Assert.IsType<MedianOpening>(plan.Opening);
        var a = Math.Min(op.Start, op.End);
        Assert.Equal(3.0, op.PocketWidth, 6);
        Assert.InRange(a - op.PocketFull, 30 - 0.02, 30 + 0.02);
        Assert.InRange(op.PocketFull - op.PocketTaper, 25 - 0.02, 25 + 0.02);

        // A via gerada (o que a ferramenta grava na seção): bolsão de 3,00 m na metade do canteiro do lado de quem retorna.
        s.Retornos.Add(r);
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(300, 0) });
        var cena = AcessoVia.Cena(s, axis, new Vec2(150, -5), new AcessoSpec { Tipo = TipoAcesso.Retorno }, Cat, retorno: r, window: 150);
        var parts = Y34TipTests.Geometry(W(cena));
        var pav = Pavement(parts);
        // Lado direito do canteiro (y −3..0): na acumulação a pista vai até y = 0 (3,00 m), no meio do taper até −1,50, antes dele −3,00.
        foreach (var x in new[] { op.PocketFull + 0.5, (op.PocketFull + a) / 2, a - 2.0 })
            Assert.InRange(EdgeAt(pav, x, -4, 0.5), -0.02, 0.02);
        Assert.InRange(EdgeAt(pav, (op.PocketTaper + op.PocketFull) / 2, -4, 0.5), -1.5 - 0.02, -1.5 + 0.02);
        Assert.InRange(EdgeAt(pav, op.PocketTaper - 2, -4, 0.5), -3.0 - 0.02, -3.0 + 0.02);
        // O lado do sentido oposto continua com o canteiro (só o vão do giro abre).
        Assert.False(In(pav, new Vec2(op.PocketFull + 5, 1.5)));
        // Sinalização do bolsão: LMS-1 na acumulação e LMS-2 no taper, na borda original do canteiro (entre o bolsão e a faixa).
        var lms = cena.Definitions.OfType<LinearMarkingDefinition>().Where(l => l.Code is "LMS-1" or "LMS-2" && l.PathRef.Points.Count == 2 && Math.Abs(l.PathRef.Points[0].Y + 3.1) < 0.05).ToList();
        Assert.Contains(lms, l => l.Code == "LMS-1" && Math.Abs(l.PathRef.Points.Min(p => p.X) - op.PocketFull) < 0.02 && Math.Abs(l.PathRef.Points.Max(p => p.X) - a) < 0.02);
        Assert.Contains(lms, l => l.Code == "LMS-2" && Math.Abs(l.PathRef.Points.Min(p => p.X) - op.PocketTaper) < 0.02);
        Assert.Empty(Y34TipTests.FaultsAt(parts, new Vec2(130, 0), 60, true));
    }

    private static readonly Polyline2 HostAxis = new(new[] { new Vec2(-150, 0), new Vec2(150, 0) });

    private static AcessoCena Access(AcessoSpec spec, int hostTpl = 1) =>
        AcessoVia.Cena(RoadTemplates.All[hostTpl].Create(), HostAxis, new Vec2(0, -8), spec, Cat, RoadTemplates.All[0].Create());

    private static List<string> Faults(AcessoCena c, List<Y34TipTests.Part> parts) =>
        Y34TipTests.FaultsAt(parts, c.Layout!.Node, Math.Max(25, c.Layout.Radius + 6), true);

    /// <summary>
    /// Raios dos vértices da face do meio-fio no arco da esquina (centro c; n1 e n2 = normais para fora das duas vias): os
    /// meios-fios da interseção com vértice a menos de 7 cm do raio R (o dorso fica 15 cm atrás).
    /// </summary>
    private static List<double> ArcRadii(List<Y34TipTests.Part> parts, Vec2 c, double R, Vec2 n1, Vec2 n2) =>
        parts.Where(p => p.Def is IntersectionDefinition && p.Piece.Layer?.StartsWith("MEIO-FIO") == true)
            .SelectMany(p => p.Piece.Shape.Outer.Concat(p.Piece.Shape.Holes.SelectMany(h => h)))
            .Where(v => Math.Abs(v.DistanceTo(c) - R) < 0.07 && (v - c).Dot(-n1) > 0.3 && (v - c).Dot(-n2) > 0.3)
            .Select(v => v.DistanceTo(c)).ToList();

    [Fact]
    public void Conversao_livre_a_direita_com_ilha_triangular_nas_medidas_digitadas()
    {
        var spec = new AcessoSpec { Tipo = TipoAcesso.ConversaoLivre, RaioConversao = 25, LarguraConversao = 5, Comprimento = 80 };
        var c = Access(spec);
        var L = c.Layout!;
        var parts = Y34TipTests.Geometry(W(c));
        var pav = ToCurb(parts);
        // Esquina de quem sai da via existente (vem do oeste e converte à direita para o sul): arco de raio 25 tangente ao
        // meio-fio sul da via (y = −hc) e ao oeste da nova via (x = −bc).
        var hc = L.Roads[0].Def.RightWidth;
        var bc = L.Roads[1].Def.RightWidth;
        var center = new Vec2(-bc - 25, -hc - 25);
        var radii = ArcRadii(parts, center, 25, new Vec2(0, -1), new Vec2(-1, 0));
        Assert.True(radii.Count >= 5, $"arco com {radii.Count} vértices");
        Assert.All(radii, d => Assert.InRange(d, 25 - 0.02, 25 + 0.02));
        // Faixa de conversão de 5,00 m entre o meio-fio externo e a ilha, na bissetriz da esquina.
        var u = new Vec2(1, 1).Normalized();
        for (var k = 0.1; k < 4.9; k += 0.2) Assert.True(In(pav, center + u * (25 + k)) && !In(L.Islands, center + u * (25 + k)), $"faixa interrompida a {k:0.0} m");
        Assert.True(In(L.Islands, center + u * (25 + 5 + 0.1)), "ilha fora da medida da faixa");
        Assert.False(In(L.Islands, center + u * (25 + 5 - 0.03)));
        // Só a esquina pedida: nenhuma ilha do lado leste (x > 0).
        Assert.All(L.Islands, i => Assert.True(i.Centroid.X < 0 && i.Centroid.Y < 0));
        Assert.Empty(Faults(c, parts));
    }

    [Fact]
    public void Bolsao_de_conversao_a_esquerda_com_zebrado_amarelo_no_taper()
    {
        var spec = new AcessoSpec { Tipo = TipoAcesso.BolsaoEsquerda, Espera = 30, Taper = 20, LarguraBolsao = 3.0, Comprimento = 80 };
        var c = Access(spec);
        var L = c.Layout!;
        var f = Assert.Single(L.Features, x => x.Kind == TipoRamo.BolsaoAlargado);
        Assert.True(f.Leg.Dir.X > 0.9);
        Assert.InRange(f.StorageEnd - f.PocketStart, 30 - 0.02, 30 + 0.02);
        Assert.InRange(f.TaperEnd - f.StorageEnd, 20 - 0.02, 20 + 0.02);
        var parts = Y34TipTests.Geometry(W(c));
        var pav = ToCurb(parts);
        // Na acumulação a pista tem 3,00 m a mais (metade para cada lado).
        var node = L.Node;
        var x0 = node.X + f.PocketStart + 10;
        double Span(double x)
        {
            var ys = PolygonOps.Intersect(pav, new[] { Polygon2.Rectangle(new Vec2(x - 0.01, -15), new Vec2(x + 0.01, 15)) }).SelectMany(p => p.Outer).Select(v => v.Y).ToList();
            return ys.Max() - ys.Min();
        }
        var nominal = L.Roads[0].Def.LeftWidth + L.Roads[0].Def.RightWidth;
        Assert.InRange(Span(x0) - nominal, 3.0 - 0.02, 3.0 + 0.02);
        Assert.InRange(Span(node.X + f.StorageEnd - 0.5) - nominal, 3.0 - 0.02, 3.0 + 0.02);
        Assert.InRange(Span(node.X + 120) - nominal, -0.02, 0.02);
        // Zebrado amarelo no taper (entre o fim da acumulação e o fim do alargamento).
        var hatch = parts.Where(p => p.Def is HatchMarkingDefinition { Code: "ZPA-A" } && p.Piece.Color == MarkingColor.Amarela).ToList();
        Assert.NotEmpty(hatch);
        Assert.All(hatch, p => Assert.InRange(p.Piece.Shape.Centroid.X, node.X + f.StorageEnd - 0.5, node.X + f.TEnd + 0.5));
        Assert.True(hatch.Sum(p => p.Piece.Shape.Area) > 1.0);
        // Linha do taper (LMS-2) do fim da acumulação ao fim do taper.
        var lms2 = c.Definitions.OfType<LinearMarkingDefinition>().Where(l => l.Code == "LMS-2" && l.GroupId == L.Definition!.Id).ToList();
        Assert.Contains(lms2, l => Math.Abs(l.PathRef.Points.Min(p => p.X) - (node.X + f.StorageEnd)) < 0.02 && Math.Abs(l.PathRef.Points.Max(p => p.X) - (node.X + f.TaperEnd)) < 0.02);
        Assert.Empty(Faults(c, parts));
    }

    [Fact]
    public void Nova_via_saindo_em_curva_com_raio_angulo_e_zebrado_na_separacao()
    {
        // Raio pequeno para a esquina de 120°: avisa o mínimo (a separação seria mais estreita que a faixa).
        var small = Access(new AcessoSpec { Tipo = TipoAcesso.ViaEmCurva, Angulo = 60, RaioConversao = 30, LarguraConversao = 4.5, Comprimento = 90 });
        Assert.Contains(small.Warnings, x => x.Contains("use pelo menos"));
        var rMin = AcessoVia.RaioMinimoSeparacao(60, 4.5);
        Assert.Contains(small.Warnings, x => x.Contains($"use pelo menos {Math.Ceiling(rMin):0} m"));
        var spec = new AcessoSpec { Tipo = TipoAcesso.ViaEmCurva, Angulo = 60, RaioConversao = 80, LarguraConversao = 4.5, Comprimento = 150 };
        Assert.True(spec.RaioConversao >= rMin - 5);
        var c = Access(spec);
        Assert.DoesNotContain(c.Warnings, x => x.Contains("use pelo menos"));
        var L = c.Layout!;
        // Ângulo da nova via com a existente.
        var dir = (c.BranchAxis![1] - c.BranchAxis[0]).Normalized();
        Assert.InRange(Math.Acos(Math.Clamp(dir.Dot(new Vec2(1, 0)), -1, 1)) * 180 / Math.PI, 60 - 0.1, 60 + 0.1);
        Assert.True(dir.Y < 0);
        var parts = Y34TipTests.Geometry(W(c));
        var pav = ToCurb(parts);
        // Saída de quem vem do oeste: esquina de 120° entre o meio-fio sul da via e o lado direito da nova via, raio 80.
        var hc = L.Roads[0].Def.RightWidth;
        var bw = L.Roads[1].Def.RightWidth;
        var n1 = new Vec2(0, -1);
        var n2 = dir.PerpRight;
        var cy = -hc - 80;
        var cx = (80 + bw - n2.Y * cy) / n2.X;
        var center = new Vec2(cx, cy);
        var radii = ArcRadii(parts, center, 80, n1, n2);
        Assert.True(radii.Count >= 5, $"arco com {radii.Count} vértices");
        Assert.All(radii, d => Assert.InRange(d, 80 - 0.02, 80 + 0.02));
        // Zebrado de canalização (ZPA branco) na separação: ilha pintada nessa esquina, sem ilha física.
        Assert.Empty(L.Islands);
        var zpa = parts.Where(p => p.Def is HatchMarkingDefinition { Code: "ZPA" }).ToList();
        Assert.NotEmpty(zpa);
        Assert.True(zpa.Sum(p => p.Piece.Shape.Area) > 2.0);
        Assert.All(zpa, p => Assert.True((p.Piece.Shape.Centroid - center).Dot(-n1) > 0 && (p.Piece.Shape.Centroid - center).Dot(-n2) > 0 && p.Piece.Shape.Centroid.X < 30));
        Assert.Empty(Faults(c, parts));
    }
}
