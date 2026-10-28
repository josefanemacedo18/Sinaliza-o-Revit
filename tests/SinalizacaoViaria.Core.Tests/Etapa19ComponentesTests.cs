using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Etapa 19 (itens 5 e 17): componentes da via por trecho – acrescentar, tirar e mudar a largura de qualquer elemento da
/// seção entre duas estacas, com transição de entrada e de saída, refeitos a partir do eixo pela seção gravada.
/// </summary>
public class Etapa19ComponentesTests
{
    private static readonly Catalogo Cat = CatalogService.LoadDefault();
    private static readonly BuildContext Ctx = new() { Catalog = Cat };
    private static readonly Polyline2 Axis = new(new[] { new Vec2(0, 0), new Vec2(150, 0) });

    private static (List<MarkingDefinition> Defs, RoadPavementDefinition Pav) Build(RoadSetup s, Polyline2? axis = null)
    {
        var ax = axis ?? Axis;
        var defs = s.Build(PathReference.FromPoints(ax.Points, 0), new OutputSettings(), Cat, axis: ax);
        return (defs, defs.OfType<RoadPavementDefinition>().Single());
    }

    /// <summary>Faixa transversal (y mín. e máx.) das peças na estaca x; nula sem peça ali.</summary>
    private static (double Lo, double Hi)? Span(IEnumerable<MarkingPiece> pieces, double x)
    {
        var cut = new[] { Polygon2.Rectangle(new Vec2(x - 0.05, -100), new Vec2(x + 0.05, 100)) };
        var ys = PolygonOps.Intersect(pieces.Select(p => p.Shape), cut).SelectMany(p => p.Outer).Select(v => v.Y).ToList();
        return ys.Count == 0 ? null : (ys.Min(), ys.Max());
    }

    private static List<MarkingPiece> Pieces(IEnumerable<MarkingDefinition> defs) => defs.SelectMany(d => MarkingBuilder.Build(d, d.Path?.StoredPolyline() ?? Axis, Ctx).Pieces).ToList();

    /// <summary>Via coletora (2 faixas por sentido, calçadas) – direita: faixa 3,30, faixa 3,50, calçada 3,00.</summary>
    private static RoadSetup Coletora() => RoadTemplates.All[1].Create();

    [Fact]
    public void Ciclofaixa_acrescentada_entre_A_e_B_com_transicoes()
    {
        var s = Coletora();
        var (nb, na, _) = s.Nominal(false);
        s.ComponentesTrecho.Add(new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Inserir, LadoEsquerdo = false, Posicao = 2,
            Elemento = new ElementoSecao { Tipo = TipoElementoSecao.Ciclofaixa, Largura = 1.50 },
            EstacaA = 50, EstacaB = 100, TransicaoA = 10, TransicaoB = 20,
        });
        var (defs, pav) = Build(s);
        Assert.DoesNotContain(s.Warnings, w => w.Contains("Componente"));
        var (at, _) = pav.EdgeFunctions(Axis);
        // Meio-fio e alinhamento 1,50 m para fora entre A e B; metade no meio das transições; nada longe do trecho.
        foreach (var x in new[] { 50.0, 75.0, 100.0 })
        {
            Assert.InRange(at(x).CurbR, 1.5 - 1e-6, 1.5 + 1e-6);
            Assert.InRange(at(x).LotR, 1.5 - 1e-6, 1.5 + 1e-6);
        }
        Assert.InRange(at(45).CurbR, 0.75 - 0.01, 0.75 + 0.01);
        Assert.InRange(at(110).CurbR, 0.75 - 0.01, 0.75 + 0.01);
        Assert.InRange(at(30).CurbR, -1e-6, 1e-6);
        Assert.InRange(at(130).CurbR, -1e-6, 1e-6);
        Assert.All(new[] { 30.0, 75.0, 130.0 }, x => Assert.InRange(at(x).CurbL, -1e-6, 1e-6));
        // Seção na estaca: a ciclofaixa existe só no trecho (com a largura da transição).
        Assert.Contains(s.SecaoNaEstaca(false, 75), e => e.Tipo == TipoElementoSecao.Ciclofaixa && Math.Abs(e.Largura - 1.5) < 1e-9);
        Assert.Contains(s.SecaoNaEstaca(false, 45), e => e.Tipo == TipoElementoSecao.Ciclofaixa && Math.Abs(e.Largura - 0.75) < 1e-9);
        Assert.DoesNotContain(s.SecaoNaEstaca(false, 20), e => e.Tipo == TipoElementoSecao.Ciclofaixa);
        // Linha da ciclofaixa: marca nova só entre A e B, junto à faixa da direita.
        var cic = defs.OfType<LinearMarkingDefinition>().Where(l => l.Code == "CIC-LD" && l.Offset < 0).ToList();
        Assert.NotEmpty(cic);
        foreach (var l in cic)
        {
            var p = l.PathRef.StoredPolyline()!;
            Assert.InRange(p.Points[0].X, 50 - 1e-6, 50 + 1e-6);
            Assert.InRange(p.Points[^1].X, 100 - 1e-6, 100 + 1e-6);
        }
        // O bordo da faixa (que a seção do trecho não tem) é interrompido entre A e B.
        var edge = defs.OfType<LinearMarkingDefinition>().Single(l => l.Code == s.EdgeCode && l.Offset < 0);
        Assert.Contains(edge.Breaks, b => Math.Abs(b.Start - 50) < 1e-6 && Math.Abs(b.End - 100) < 1e-6);
        // A sarjeta vai com o meio-fio: no trecho fica encostada nele, 1,50 m mais para fora.
        var gutter = Pieces(defs.OfType<LinearMarkingDefinition>().Where(l => l.Code == "SARJETA" && l.Offset < 0));
        Assert.InRange(Span(gutter, 75)!.Value.Lo, -nb - 1.5 - 0.02, -nb - 1.5 + 0.02);
        Assert.InRange(Span(gutter, 20)!.Value.Lo, -nb - 0.02, -nb + 0.02);
        // Calçada inteira deslocada no trecho (mesma largura).
        var walk = Pieces(defs.OfType<LinearMarkingDefinition>().Where(l => l.Code is "CALCADA" or "MEIO-FIO" or "GRAMADO" && l.Offset < 0));
        Assert.InRange(Span(walk, 75)!.Value.Lo, -na - 1.5 - 0.02, -na - 1.5 + 0.02);
        Assert.InRange(Span(walk, 75)!.Value.Hi, -nb - 1.5 - 0.02, -nb - 1.5 + 0.02);
        // Pavimento: no trecho a pista (até a sarjeta, que é vão) vai 1,50 m mais para fora.
        var pv = MarkingBuilder.Build(pav, Axis, Ctx).Pieces;
        Assert.InRange(Span(pv, 75)!.Value.Lo - Span(pv, 20)!.Value.Lo, -1.5 - 0.03, -1.5 + 0.03);
        Assert.InRange(Span(pv, 75)!.Value.Hi - Span(pv, 20)!.Value.Hi, -0.03, 0.03);
    }

    [Fact]
    public void Faixa_tirada_entre_A_e_B_afunila_nas_transicoes()
    {
        var s = Coletora();
        s.ComponentesTrecho.Add(new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Remover, Posicao = 1, TipoNaPosicao = TipoElementoSecao.FaixaRolamento,
            EstacaA = 60, EstacaB = 90, TransicaoA = 15, TransicaoB = 15,
        });
        var (defs, pav) = Build(s);
        var (at, _) = pav.EdgeFunctions(Axis);
        Assert.InRange(at(75).CurbR, -3.5 - 1e-6, -3.5 + 1e-6);
        Assert.InRange(at(52.5).CurbR, -1.75 - 0.01, -1.75 + 0.01);
        Assert.InRange(at(20).CurbR, -1e-6, 1e-6);
        Assert.Single(s.SecaoNaEstaca(false, 75), e => e.Tipo == TipoElementoSecao.FaixaRolamento);
        Assert.Equal(2, s.SecaoNaEstaca(false, 20).Count(e => e.Tipo == TipoElementoSecao.FaixaRolamento));
        // A divisória das duas faixas some desde o início da transição; a linha de bordo continua e acompanha o meio-fio.
        var lms = defs.OfType<LinearMarkingDefinition>().Single(l => l.Code.StartsWith("LMS") && l.Offset < 0);
        Assert.Contains(lms.Breaks, b => Math.Abs(b.Start - 45) < 1e-6 && Math.Abs(b.End - 105) < 1e-6);
        var lbo = defs.OfType<LinearMarkingDefinition>().Where(l => l.Code == s.EdgeCode && l.Offset < 0).ToList();
        Assert.Single(lbo);
        Assert.Empty(lbo[0].Breaks);
        Assert.InRange(lbo[0].PathRef.Lateral!.ShiftAt(75), 3.5 - 1e-6, 3.5 + 1e-6);
        // Nenhuma marca nova: a seção do trecho é a da via sem a faixa (as outras marcas se deslocam).
        Assert.DoesNotContain(defs, d => d.Path is { Points.Count: >= 2 } p && p.Points[0].X > 1);
    }

    [Fact]
    public void Largura_de_uma_faixa_mudada_no_trecho()
    {
        var s = Coletora();
        s.ComponentesTrecho.Add(new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Largura, Posicao = 0, TipoNaPosicao = TipoElementoSecao.FaixaRolamento, Largura = 4.0,
            EstacaA = 40, EstacaB = 80, TransicaoA = 10, TransicaoB = 10,
        });
        var (defs, pav) = Build(s);
        var (at, _) = pav.EdgeFunctions(Axis);
        Assert.InRange(at(60).CurbR, 0.7 - 1e-6, 0.7 + 1e-6);
        Assert.InRange(at(60).CurbL, -1e-6, 1e-6);
        Assert.Contains(s.SecaoNaEstaca(false, 60), e => Math.Abs(e.Largura - 4.0) < 1e-9);
        // A divisória entre as faixas vai junto (a primeira faixa ficou mais larga).
        var lms = defs.OfType<LinearMarkingDefinition>().Single(l => l.Code.StartsWith("LMS") && l.Offset < 0);
        Assert.InRange(lms.PathRef.Lateral!.ShiftAt(60), -0.7 - 1e-6, -0.7 + 1e-6);
        Assert.InRange(lms.PathRef.Lateral!.ShiftAt(20), -1e-6, 1e-6);
    }

    [Fact]
    public void Canteiro_acrescentado_recorta_o_pavimento_so_no_trecho()
    {
        var s = Coletora();
        s.ComponentesTrecho.Add(new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Inserir, Posicao = 1, Elemento = new ElementoSecao { Tipo = TipoElementoSecao.CanteiroFisico, Largura = 1.5 },
            EstacaA = 40, EstacaB = 80, TransicaoA = 10, TransicaoB = 10,
        });
        var (defs, pav) = Build(s);
        var gap = Assert.Single(pav.StretchGaps);
        Assert.Equal(40, gap.From);
        Assert.Equal(80, gap.To);
        Assert.InRange(gap.Offset, -(3.3 + 0.75) - 1e-6, -(3.3 + 0.75) + 1e-6);
        // Sem pavimento sob o canteiro no trecho; pista contínua fora dele.
        var pv = MarkingBuilder.Build(pav, Axis, Ctx).Pieces;
        bool Paved(double x, double y) => PolygonOps.Intersect(pv.Select(p => p.Shape), new[] { Polygon2.Rectangle(new Vec2(x - 0.05, y - 0.05), new Vec2(x + 0.05, y + 0.05)) }).Count > 0;
        Assert.False(Paved(60, -4.05));
        Assert.True(Paved(20, -4.05));
        Assert.True(Paved(120, -4.05));
        // Meios-fios e grama do canteiro só entre A e B; as conexões continuam lendo a seção inteira (Gaps sem o trecho).
        var grass = defs.OfType<LinearMarkingDefinition>().Single(l => l.Code == "GRAMADO" && l.Offset < -3 && l.Offset > -5);
        var gp = grass.PathRef.StoredPolyline()!;
        Assert.Equal(40, gp.Points[0].X, 6);
        Assert.Equal(80, gp.Points[^1].X, 6);
        Assert.DoesNotContain(pav.Gaps, g => Math.Abs(g.Offset + 4.05) < 0.01);
        // Seção da via numa estaca do trecho (interseções e rotatórias): o canteiro está nela; fora, não.
        Assert.Contains(pav.Local(60, Axis).Gaps, g => Math.Abs(g.Offset - (-4.05 - 1.5 * 0)) < 0.6 && g.Width > 1.4);
        Assert.DoesNotContain(pav.Local(20, Axis).Gaps, g => g.Width > 1.4);
    }

    [Fact]
    public void Canteiro_tirado_fecha_o_pavimento_no_trecho()
    {
        var s = RoadTemplates.All[6].Create();
        var idx = s.Right.FindIndex(e => e.Tipo == TipoElementoSecao.CanteiroFisico);
        var iv = s.Intervalos(false)[idx];
        s.ComponentesTrecho.Add(new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Remover, Posicao = idx, TipoNaPosicao = TipoElementoSecao.CanteiroFisico,
            EstacaA = 60, EstacaB = 90, TransicaoA = 10, TransicaoB = 10,
        });
        var (defs, pav) = Build(s);
        var pv = MarkingBuilder.Build(pav, Axis, Ctx).Pieces;
        bool Paved(double x, double y) => PolygonOps.Intersect(pv.Select(p => p.Shape), new[] { Polygon2.Rectangle(new Vec2(x - 0.05, y - 0.05), new Vec2(x + 0.05, y + 0.05)) }).Count > 0;
        var mid = -(iv.A + iv.B) / 2;
        Assert.False(Paved(20, mid));
        Assert.True(Paved(75, mid));
        // Os meios-fios e a grama do canteiro são interrompidos no trecho e nas transições.
        foreach (var l in defs.OfType<LinearMarkingDefinition>().Where(l => l.Code is "GRAMADO" or "MEIO-FIO" && -l.Offset > iv.A - 0.01 && -l.Offset < iv.B + 0.01))
            Assert.Contains(l.Breaks, b => Math.Abs(b.Start - 50) < 1e-6 && Math.Abs(b.End - 100) < 1e-6);
        // O vão continua na seção (conexões) com o trecho de exceção.
        Assert.Contains(pav.Gaps, g => g.Except is { Count: 1 } && Math.Abs(g.Offset - mid) < 0.01);
    }

    [Fact]
    public void Ciclofaixa_tirada_some_com_simbolos_e_segregadores()
    {
        // Direita: faixa 3,30, faixa de segurança 0,60 (zebrado), ciclofaixa 1,50 com segregadores, calçada.
        var s = RoadTemplates.All[5].Create();
        var idx = s.Right.FindIndex(e => e.Tipo == TipoElementoSecao.Ciclofaixa);
        Assert.True(idx > 0);
        s.ComponentesTrecho.Add(new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Remover, Posicao = idx, TipoNaPosicao = TipoElementoSecao.Ciclofaixa,
            EstacaA = 50, EstacaB = 110, TransicaoA = 10, TransicaoB = 10,
        });
        var (defs, _) = Build(s);
        // Símbolos e segregadores da ciclofaixa (marcas sem interrupção própria): recortados no trecho, por recorte sem dono.
        var sym = defs.OfType<RepeatedMarkingDefinition>().Where(r => r.Offset < 0 && r.SymbolCode is "SIC" or "CIC-SETA").ToList();
        Assert.NotEmpty(sym);
        Assert.All(sym, r => Assert.Contains(r.Exclusions, z => z.SourceId == null && !z.Manual));
        foreach (var r in sym)
            Assert.DoesNotContain(MarkingBuilder.Build(r, Axis, Ctx).Pieces, p => p.Shape.Outer.Average(v => v.X) is var x && x > 41 && x < 119);
        var dev = defs.OfType<DeviceMarkingDefinition>().Where(d => d.Code == "SEG-CIC").ToList();
        Assert.NotEmpty(dev);
        Assert.All(dev, d => Assert.NotEmpty(d.Exclusions));
        // Nenhuma referência quebrada no grafo (os recortes não apontam para marca nenhuma).
        Assert.Empty(new ModelGraph(defs).Broken);
    }

    [Fact]
    public void Transicoes_retas_e_em_S()
    {
        var t = new ComponenteTrecho { EstacaA = 100, EstacaB = 200, TransicaoA = 20, TransicaoB = 40 };
        Assert.Equal(0, t.Fator(79.9));
        Assert.Equal(0.5, t.Fator(90), 9);
        Assert.Equal(1, t.Fator(150));
        Assert.Equal(0.75, t.Fator(210), 9);
        Assert.Equal(0, t.Fator(240.1));
        t.CurvaS = true;
        Assert.Equal(0.5, t.Fator(90), 9);
        Assert.Equal(0.15625, t.Fator(85), 9);   // u = 0,25: u²(3 − 2u)
        // Sem transição: degrau em A e em B.
        var d = new ComponenteTrecho { EstacaA = 10, EstacaB = 20, TransicaoA = 0, TransicaoB = 0 };
        Assert.Equal(1, d.Fator(10));
        Assert.Equal(0, d.Fator(9.99));
        // A largura por trecho usa a mesma conta.
        var w = new TrechoLargura { EstacaA = 100, EstacaB = 200, TransicaoA = 20, TransicaoB = 40 };
        Assert.Equal(t.Fator(210) * 0 + new ComponenteTrecho { EstacaA = 100, EstacaB = 200, TransicaoA = 20, TransicaoB = 40 }.Fator(210), w.Fator(210), 12);
    }

    [Fact]
    public void Trechos_invalidos_sao_ignorados_com_aviso()
    {
        var s = Coletora();
        var cic = new ElementoSecao { Tipo = TipoElementoSecao.Ciclofaixa, Largura = 1.5 };
        s.ComponentesTrecho.Add(new ComponenteTrecho { Operacao = OperacaoComponente.Inserir, Posicao = 2, Elemento = cic, EstacaA = 50, EstacaB = 100 });
        // Sobreposto ao primeiro, do mesmo lado (transições incluídas).
        s.ComponentesTrecho.Add(new ComponenteTrecho { Operacao = OperacaoComponente.Inserir, Posicao = 1, Elemento = cic, EstacaA = 105, EstacaB = 130 });
        // Depois da calçada (o último elemento é o alinhamento do lote).
        s.ComponentesTrecho.Add(new ComponenteTrecho { Operacao = OperacaoComponente.Inserir, LadoEsquerdo = true, Posicao = 3, Elemento = cic, EstacaA = 10, EstacaB = 30 });
        // Tipo da posição mudou.
        s.ComponentesTrecho.Add(new ComponenteTrecho { Operacao = OperacaoComponente.Remover, LadoEsquerdo = true, Posicao = 2, TipoNaPosicao = TipoElementoSecao.FaixaRolamento, EstacaA = 60, EstacaB = 90 });
        var (_, pav) = Build(s);
        Assert.Contains(s.Warnings, w => w.Contains("sobrepõe"));
        Assert.Contains(s.Warnings, w => w.Contains("último elemento"));
        Assert.Contains(s.Warnings, w => w.Contains("não tem mais esse elemento"));
        // Só o primeiro vale: lado esquerdo intacto, lado direito com 1,50 m no trecho dele.
        var (at, _) = pav.EdgeFunctions(Axis);
        Assert.InRange(at(75).CurbR, 1.5 - 1e-6, 1.5 + 1e-6);
        Assert.InRange(at(120).CurbR, -1e-6, 1e-6);
        Assert.All(new[] { 20.0, 75.0 }, x => Assert.InRange(at(x).CurbL, -1e-6, 1e-6));
    }

    [Fact]
    public void Dividir_um_trecho_da_o_mesmo_resultado()
    {
        var t = new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Inserir, Posicao = 2, Elemento = new ElementoSecao { Tipo = TipoElementoSecao.Estacionamento, Largura = 2.2 },
            EstacaA = 40, EstacaB = 110, TransicaoA = 10, TransicaoB = 10,
        };
        var parts = t.Dividir(70)!.Value;
        Assert.Equal((40.0, 70.0, 0.0), (parts.Antes.A, parts.Antes.B, parts.Antes.TransicaoB));
        Assert.Equal((70.0, 110.0, 0.0), (parts.Depois.A, parts.Depois.B, parts.Depois.TransicaoA));
        Assert.NotEqual(parts.Antes.Id, parts.Depois.Id);
        Assert.Null(t.Dividir(40.2));
        var one = Coletora();
        one.ComponentesTrecho.Add(t);
        var two = Coletora();
        two.ComponentesTrecho.Add(parts.Antes);
        // As duas partes, do mesmo lado e encostadas, são um caso de sobreposição: a segunda vai para o outro lado do teste.
        var (_, p1) = Build(one);
        var (_, p2) = Build(two);
        var (a1, _) = p1.EdgeFunctions(Axis);
        var (a2, _) = p2.EdgeFunctions(Axis);
        foreach (var x in new[] { 35.0, 45.0, 60.0, 69.9 })
            Assert.InRange(a1(x).CurbR - a2(x).CurbR, -1e-6, 1e-6);
    }

    [Fact]
    public void Via_refeita_pela_secao_gravada_tem_os_mesmos_trechos()
    {
        var s = Coletora();
        s.ComponentesTrecho.Add(new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Inserir, Posicao = 2, Elemento = new ElementoSecao { Tipo = TipoElementoSecao.Ciclofaixa, Largura = 1.5 },
            EstacaA = 50, EstacaB = 100,
        });
        s.ComponentesTrecho.Add(new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Remover, LadoEsquerdo = true, Posicao = 1, TipoNaPosicao = TipoElementoSecao.FaixaRolamento,
            EstacaA = 30, EstacaB = 60, CurvaS = true,
        });
        var (defs, pav) = Build(s);
        // A seção gravada no pavimento (o que a regeneração lê) traz os trechos; a via refeita dela é a mesma.
        var again = RoadSetup.DaVia(pav)!;
        Assert.Equal(2, again.ComponentesTrecho.Count);
        Assert.True(again.ComponentesTrecho[1].CurvaS);
        var (defs2, pav2) = Build(again);
        Assert.Equal(defs.Count, defs2.Count);
        Assert.Equal(defs.Select(d => d.DisplayCode).OrderBy(x => x), defs2.Select(d => d.DisplayCode).OrderBy(x => x));
        Assert.Equal(pav.EdgeVariation.Count, pav2.EdgeVariation.Count);
        for (int i = 0; i < pav.EdgeVariation.Count; i++)
        {
            Assert.Equal(pav.EdgeVariation[i].CurbRight, pav2.EdgeVariation[i].CurbRight, 9);
            Assert.Equal(pav.EdgeVariation[i].LotLeft, pav2.EdgeVariation[i].LotLeft, 9);
        }
        // No grafo do projeto as marcas novas são da mesma via (mesmo grupo), sem referência quebrada.
        var graph = new ModelGraph(defs);
        Assert.Empty(graph.Broken);
        Assert.All(defs, d => Assert.Equal(pav.GroupId, d.GroupId));
        // A via num eixo em curva também recorta as marcas novas no trecho (estacas pelo eixo).
        var arc = new Polyline2(Enumerable.Range(0, 61).Select(i => new Vec2(80 * Math.Sin(i / 60.0 * 2.0), 80 - 80 * Math.Cos(i / 60.0 * 2.0))));
        var (defs3, _) = Build(s, arc);
        var cic = defs3.OfType<LinearMarkingDefinition>().First(l => l.Code == "CIC-LD" && l.Offset < 0).PathRef.StoredPolyline()!;
        Assert.InRange(cic.Length, 50 - 0.05, 50 + 0.05);
        Assert.InRange(arc.Project(cic.Points[0]).Station, 50 - 0.05, 50 + 0.05);
    }

    [Fact]
    public void Edicao_a_mao_de_uma_marca_do_trecho_e_preservada()
    {
        var s = Coletora();
        s.ComponentesTrecho.Add(new ComponenteTrecho
        {
            Operacao = OperacaoComponente.Inserir, Posicao = 2, Elemento = new ElementoSecao { Tipo = TipoElementoSecao.Ciclofaixa, Largura = 1.5 },
            EstacaA = 50, EstacaB = 100,
        });
        var (defs, _) = Build(s);
        var edited = defs.OfType<LinearMarkingDefinition>().First(l => l.Code == "CIC-FD");
        edited.EditadoManualmente = true;
        edited.ColorOverride = MarkingColor.Verde;
        // Regenerar (a seção gravada de novo): a sinalização editada à mão fica e não nasce outra igual no lugar.
        var (regen, _) = Build(s);
        var kept = AutoSignage.Preserve(regen, new[] { edited });
        Assert.DoesNotContain(kept, d => d is LinearMarkingDefinition { Code: "CIC-FD" });
        Assert.Contains(kept, d => d is LinearMarkingDefinition { Code: "CIC-LD" });
    }

    [Fact]
    public void Sem_componentes_a_via_nao_muda()
    {
        var s = Coletora();
        var (defs, pav) = Build(s);
        Assert.False(s.HasVariableWidth);
        Assert.Empty(pav.EdgeVariation);
        Assert.Empty(pav.StretchGaps);
        Assert.All(defs, d => Assert.True(d.Path?.Lateral == null || d.Path.Lateral.IsEmpty));
        // Componente que não muda nada (mesma largura): a via fica igual.
        var t = Coletora();
        t.ComponentesTrecho.Add(new ComponenteTrecho { Operacao = OperacaoComponente.Largura, Posicao = 0, Largura = 3.3, EstacaA = 10, EstacaB = 50 });
        var (defs2, pav2) = Build(t);
        Assert.Equal(defs.Count, defs2.Count);
        Assert.False(pav2.HasEdgeVariation);
    }

    private static string Raiz([System.Runtime.CompilerServices.CallerFilePath] string aqui = "")
    {
        var d = new DirectoryInfo(Path.GetDirectoryName(aqui)!);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void Ligacao_na_faixa_na_janela_da_via_e_no_comando()
    {
        string Ler(string rel) => File.ReadAllText(Path.Combine(Raiz(), rel));
        var rb = Ler("src/SinalizacaoViaria.Revit/RibbonBuilder.cs");
        Assert.Contains("typeof(CmdComponentesTrecho), \"Componentes por Trecho\", \"componentes\"", rb);
        Assert.Contains("typeof(CmdTaludesTrecho), \"Taludes por Trecho\", \"taludetrecho\"", rb);
        var win = Ler("src/SinalizacaoViaria.Revit/UI/RoadWindow.xaml.cs");
        Assert.Contains("s.ComponentesTrecho = _components.Select(t => t.Clone()).ToList();", win);
        Assert.Contains("_components.AddRange(s.ComponentesTrecho.Select(t => t.Clone()));", win);
        Assert.Contains("Click=\"AddComponentClick\"", Ler("src/SinalizacaoViaria.Revit/UI/RoadWindow.xaml"));
        var cmd = Ler("src/SinalizacaoViaria.Revit/Commands/ComponentStretchCommands.cs");
        Assert.Contains("RoadAccessCommand.Regenerate(uidoc, pav, work)", cmd);
        Assert.Contains("gravados[escolha].Dividir(dividirEm)", cmd);
        Assert.Contains("ComponentStretchForm.Validar(setup, t, len)", cmd);
        // A seção gravada leva os componentes (é dela que a via é refeita); a cópia não divide os avisos com a original.
        var s = Coletora();
        s.ComponentesTrecho.Add(new ComponenteTrecho { Operacao = OperacaoComponente.Remover, Posicao = 1, EstacaA = 10, EstacaB = 40 });
        var c = s.Clone();
        Assert.NotSame(s.ComponentesTrecho[0], c.ComponentesTrecho[0]);
        s.Warnings.Add("x");
        Assert.Empty(c.Warnings);
    }
}
