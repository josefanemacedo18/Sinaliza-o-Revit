using System.Globalization;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Gráficos da terraplenagem por estaca (pedido de 2026-10-27, item 21) no estilo do gráfico do Perfil da via
/// (<see cref="RoadProfileDesigner.Chart"/>): a seção numa estaca (terreno, plataforma e taludes, corte em vermelho e aterro em
/// verde, com as áreas), as áreas de corte e aterro de cada estaca (barras) e o diagrama de massas (Brückner).
/// </summary>
public static class EarthworkCharts
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Altura do texto em mm de papel (a prévia usa 1:100 → 10 × a altura em metros).</summary>
    private static double Mm(double modelHeight) => modelHeight * 10;

    private static double Nice(double span, int n)
    {
        var raw = Math.Max(1e-9, span / n);
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var f in new[] { 1.0, 2.0, 5.0, 10.0 }) if (raw <= f * mag) return f * mag;
        return 10 * mag;
    }

    /// <summary>Seção na estaca: largura real, cotas com o exagero vertical pedido (1 = sem exagero).</summary>
    public static MarkingGeometry Secao(SecaoTransversal sec, string rotulo, double exagero = 1)
    {
        var geo = new MarkingGeometry();
        var pts = sec.Terreno.Concat(sec.Projeto).ToList();
        if (pts.Count < 2) return geo;
        var k = Math.Clamp(exagero, 0.2, 50);
        var x0 = pts.Min(p => p.X);
        var x1 = pts.Max(p => p.X);
        var zmin = Math.Floor(pts.Min(p => p.Y) - 1);
        var zmax = Math.Ceiling(pts.Max(p => p.Y) + 1);
        Vec2 P(double o, double z) => new(o, (z - zmin) * k);
        var W = x1 - x0;
        var tx = Math.Max(0.25, W * 0.018);
        // Grade: cotas e afastamentos.
        var dz = Nice(zmax - zmin, 6);
        for (var z = Math.Ceiling(zmin / dz) * dz; z <= zmax + 1e-9; z += dz)
        {
            geo.Annotations.Add(new AnnotationLine(new[] { P(x0, z), P(x1, z) }, MarkingColor.Concreto));
            geo.Annotations.Add(new AnnotationText(P(x0, z) + new Vec2(-tx * 0.6, -tx * 0.35), z.ToString("0.#", Pt) + " m", Mm(tx * 0.8), TextAlign.Right));
        }
        var dx = Nice(W, 8);
        for (var o = Math.Ceiling(x0 / dx) * dx; o <= x1 + 1e-9; o += dx)
        {
            geo.Annotations.Add(new AnnotationLine(new[] { P(o, zmin), P(o, zmax) }, MarkingColor.Concreto));
            geo.Annotations.Add(new AnnotationText(P(o, zmin) - new Vec2(0, tx * 1.4), o.ToString("0.#", Pt), Mm(tx * 0.8)));
        }
        // Terreno (massa marrom até a base), corte e aterro, projeto e o eixo.
        if (sec.Terreno.Count >= 2)
        {
            var t = new List<Vec2> { P(sec.Terreno[0].X, zmin) };
            t.AddRange(sec.Terreno.Select(p => P(p.X, p.Y)));
            t.Add(P(sec.Terreno[^1].X, zmin));
            geo.Add(new Polygon2(t), MarkingColor.Terra);
        }
        foreach (var (polys, col) in new[] { (sec.Corte, MarkingColor.Vermelha), (sec.Aterro, MarkingColor.Verde) })
            foreach (var q in polys)
                geo.Pieces.Add(new MarkingPiece(new Polygon2(q.Outer.Select(v => P(v.X, v.Y))), col) { Elevation = 0.02 });
        if (sec.Projeto.Count >= 2)
        {
            var th = Math.Max(0.03, (zmax - zmin) * k * 0.006);
            var top = sec.Projeto.Select(p => P(p.X, p.Y) + new Vec2(0, th)).ToList();
            var bot = sec.Projeto.Select(p => P(p.X, p.Y) - new Vec2(0, th)).Reverse();
            geo.Pieces.Add(new MarkingPiece(new Polygon2(top.Concat(bot)), MarkingColor.Asfalto) { Elevation = 0.05 });
        }
        geo.Annotations.Add(new AnnotationLine(new[] { P(0, zmin), P(0, zmax) }, MarkingColor.Vermelha));
        geo.Annotations.Add(new AnnotationText(P(0, zmax) + new Vec2(0, tx * 0.6), $"Eixo – greide {sec.ZEixo.ToString("0.00", Pt)} m", Mm(tx * 0.8)));
        var H = (zmax - zmin) * k;
        geo.Annotations.Add(new AnnotationText(new Vec2(x0, H + tx * 2.4),
            $"{rotulo}: corte {sec.AreaCorte.ToString("0.00", Pt)} m² · aterro {sec.AreaAterro.ToString("0.00", Pt)} m²", Mm(tx * 1.0), TextAlign.Left));
        if (sec.SemEncontroEsquerdo || sec.SemEncontroDireito)
            geo.Warnings.Add($"{rotulo}: o talude {(sec.SemEncontroEsquerdo && sec.SemEncontroDireito ? "dos dois lados" : sec.SemEncontroEsquerdo ? "esquerdo" : "direito")} não encontra o terreno dentro do alcance – a área fica até ali.");
        geo.Annotations.Add(new AnnotationText(new Vec2(x1, -tx * 3.0), $"Afastamento do eixo (m, + à esquerda) · exagero vertical {k.ToString("0.#", Pt)}×", Mm(tx * 0.8), TextAlign.Right));
        return geo;
    }

    /// <summary>Áreas de corte (para cima, vermelho) e de aterro (para baixo, verde) de cada seção, com o volume total.</summary>
    public static MarkingGeometry PorEstaca(VolumesPorSecao v, Estaqueamento? estacas = null)
    {
        var geo = new MarkingGeometry();
        if (v.Secoes.Count == 0) return geo;
        var est = estacas ?? new Estaqueamento();
        var L = Math.Max(1, v.Secoes[^1].Estaca - v.Secoes[0].Estaca);
        var s0 = v.Secoes[0].Estaca;
        var amax = Math.Max(1, v.Secoes.Max(x => Math.Max(x.Corte, x.Aterro)));
        var k = L * 0.25 / amax;   // altura do gráfico ~ 1/4 do comprimento para cada lado
        var tx = Math.Max(0.8, L * 0.013);
        var bw = v.Secoes.Count > 1 ? Math.Max(0.2, (L / (v.Secoes.Count - 1)) * 0.6) : L * 0.05;
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(0, 0), new Vec2(L, 0) }, MarkingColor.Preta));
        var da = Nice(amax, 4);
        for (var a = da; a <= amax + 1e-9; a += da)
            foreach (var sg in new[] { 1, -1 })
            {
                geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(0, sg * a * k), new Vec2(L, sg * a * k) }, MarkingColor.Concreto));
                geo.Annotations.Add(new AnnotationText(new Vec2(-tx * 0.6, sg * a * k - tx * 0.35), (sg * a).ToString("0.#", Pt) + " m²", Mm(tx * 0.8), TextAlign.Right));
            }
        var every = Math.Max(1, (int)Math.Ceiling(v.Secoes.Count / 12.0));
        for (int i = 0; i < v.Secoes.Count; i++)
        {
            var sc = v.Secoes[i];
            var x = sc.Estaca - s0;
            if (sc.Corte > 1e-6) geo.Add(Polygon2.Rectangle(new Vec2(x - bw / 2, 0), new Vec2(x + bw / 2, sc.Corte * k)), MarkingColor.Vermelha);
            if (sc.Aterro > 1e-6) geo.Add(Polygon2.Rectangle(new Vec2(x - bw / 2, -sc.Aterro * k), new Vec2(x + bw / 2, 0)), MarkingColor.Verde);
            if (i % every == 0 || i == v.Secoes.Count - 1)
                geo.Annotations.Add(new AnnotationText(new Vec2(x, -amax * k - tx * 1.4), est.Rotulo(sc.Estaca), Mm(tx * 0.75)));
        }
        geo.Annotations.Add(new AnnotationText(new Vec2(0, amax * k + tx * 2.2),
            $"Áreas das seções por estaca – corte {v.CorteM3.ToString("N0", Pt)} m³ · aterro {v.AterroM3.ToString("N0", Pt)} m³ (média das áreas)", Mm(tx * 0.95), TextAlign.Left));
        return geo;
    }

    /// <summary>Diagrama de massas: ordenadas acumuladas (corte − aterro × fator), linha do zero, pontos de passagem e o saldo.</summary>
    public static MarkingGeometry Massas(IReadOnlyList<PontoMassa> m, double homogeneizacao, Estaqueamento? estacas = null)
    {
        var geo = new MarkingGeometry();
        if (m.Count < 2) return geo;
        var est = estacas ?? new Estaqueamento();
        var s0 = m[0].Estaca;
        var L = Math.Max(1, m[^1].Estaca - s0);
        var ymax = Math.Max(1, m.Max(p => p.Ordenada));
        var ymin = Math.Min(-1, m.Min(p => p.Ordenada));
        var k = L * 0.45 / Math.Max(1, ymax - ymin);
        var tx = Math.Max(0.8, L * 0.013);
        Vec2 P(PontoMassa p) => new(p.Estaca - s0, p.Ordenada * k);
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(0, 0), new Vec2(L, 0) }, MarkingColor.Preta));
        var dy = Nice(ymax - ymin, 6);
        for (var y = Math.Ceiling(ymin / dy) * dy; y <= ymax + 1e-9; y += dy)
        {
            if (Math.Abs(y) < 1e-9) continue;
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(0, y * k), new Vec2(L, y * k) }, MarkingColor.Concreto));
            geo.Annotations.Add(new AnnotationText(new Vec2(-tx * 0.6, y * k - tx * 0.35), y.ToString("N0", Pt) + " m³", Mm(tx * 0.8), TextAlign.Right));
        }
        // Áreas entre a curva e o zero: acima = corte sobrando no trecho, abaixo = aterro a cobrir.
        for (int i = 0; i + 1 < m.Count; i++)
        {
            var (a, b) = (P(m[i]), P(m[i + 1]));
            var quad = new Polygon2(new[] { new Vec2(a.X, 0), new Vec2(b.X, 0), b, a });
            if (Math.Abs(quad.Area) < 1e-9) continue;
            geo.Pieces.Add(new MarkingPiece(quad, (a.Y + b.Y) / 2 >= 0 ? MarkingColor.Vermelha : MarkingColor.Verde) { Elevation = 0.02 });
        }
        var th = Math.Max(0.05, (ymax - ymin) * k * 0.004);
        var top = m.Select(p => P(p) + new Vec2(0, th)).ToList();
        var bot = m.Select(p => P(p) - new Vec2(0, th)).Reverse();
        geo.Pieces.Add(new MarkingPiece(new Polygon2(top.Concat(bot)), MarkingColor.Asfalto) { Elevation = 0.05 });
        foreach (var x in EstacasTerraplenagem.PontosDePassagem(m))
        {
            geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(x - s0, -tx), new Vec2(x - s0, tx) }, MarkingColor.Preta));
            geo.Annotations.Add(new AnnotationText(new Vec2(x - s0, tx * 1.3), est.Rotulo(x), Mm(tx * 0.7)));
        }
        var saldo = m[^1].Ordenada;
        geo.Annotations.Add(new AnnotationText(new Vec2(0, ymax * k + tx * 2.4),
            $"Diagrama de massas (Brückner) – fator de homogeneização {homogeneizacao.ToString("0.00", Pt)} · saldo no fim: " +
            (Math.Abs(saldo) < 0.5 ? "compensado" : saldo > 0 ? $"sobra {saldo.ToString("N0", Pt)} m³ (bota-fora)" : $"falta {(-saldo).ToString("N0", Pt)} m³ (empréstimo)"),
            Mm(tx * 0.95), TextAlign.Left));
        geo.Annotations.Add(new AnnotationText(new Vec2(0, ymin * k - tx * 1.6), est.Rotulo(s0), Mm(tx * 0.75)));
        geo.Annotations.Add(new AnnotationText(new Vec2(L, ymin * k - tx * 1.6), est.Rotulo(m[^1].Estaca), Mm(tx * 0.75)));
        return geo;
    }
}
