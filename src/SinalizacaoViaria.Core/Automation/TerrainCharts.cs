using System.Globalization;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Gráficos do terreno para as prévias (item 21): perfil de uma medida e a distribuição de um tema ou comparação.</summary>
public static class TerrainCharts
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    private static double Nice(double span, int n)
    {
        var raw = Math.Max(1e-9, span / n);
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var f in new[] { 1.0, 2.0, 5.0, 10.0 }) if (raw <= f * mag) return f * mag;
        return 10 * mag;
    }

    /// <summary>
    /// Perfil ao longo do caminho: as linhas dadas (rótulo, perfil (distância, cota), cor) com a grade de cotas e distâncias,
    /// exagero vertical <paramref name="exagero"/>.
    /// </summary>
    public static MarkingGeometry Perfil(IReadOnlyList<(string Rotulo, IReadOnlyList<Vec2> Pontos, MarkingColor Cor)> linhas, double exagero, string titulo)
    {
        var geo = new MarkingGeometry();
        var all = linhas.SelectMany(l => l.Pontos).ToList();
        if (all.Count < 2) return geo;
        var k = Math.Clamp(exagero, 0.2, 50);
        var x1 = Math.Max(1, all.Max(p => p.X));
        var zmin = Math.Floor(all.Min(p => p.Y) - 0.5);
        var zmax = Math.Ceiling(all.Max(p => p.Y) + 0.5);
        Vec2 P(double s, double z) => new(s, (z - zmin) * k);
        var tx = Math.Max(0.3, x1 * 0.016);
        var dz = Nice(zmax - zmin, 6);
        for (var z = Math.Ceiling(zmin / dz) * dz; z <= zmax + 1e-9; z += dz)
        {
            geo.Annotations.Add(new AnnotationLine(new[] { P(0, z), P(x1, z) }, MarkingColor.Concreto));
            geo.Annotations.Add(new AnnotationText(P(0, z) + new Vec2(-tx * 0.6, -tx * 0.35), z.ToString("0.#", Pt) + " m", tx * 8, TextAlign.Right));
        }
        var dx = Nice(x1, 8);
        for (var s = 0.0; s <= x1 + 1e-9; s += dx)
        {
            geo.Annotations.Add(new AnnotationLine(new[] { P(s, zmin), P(s, zmax) }, MarkingColor.Concreto));
            geo.Annotations.Add(new AnnotationText(P(s, zmin) - new Vec2(0, tx * 1.4), s.ToString("0.#", Pt), tx * 8));
        }
        var y = (zmax - zmin) * k + tx * 2.4;
        geo.Annotations.Add(new AnnotationText(new Vec2(0, y), titulo, tx * 10, TextAlign.Left));
        foreach (var (rot, pts, cor) in linhas)
        {
            if (pts.Count < 2) continue;
            geo.Annotations.Add(new AnnotationLine(pts.Select(p => P(p.X, p.Y)).ToList(), cor));
            y -= tx * 1.6;
            geo.Annotations.Add(new AnnotationText(new Vec2(x1, y + tx * 3.2), "— " + rot, tx * 8, TextAlign.Right));
        }
        geo.Annotations.Add(new AnnotationText(new Vec2(x1, -tx * 3.0), $"Distância horizontal (m) · exagero vertical {k.ToString("0.#", Pt)}×", tx * 8, TextAlign.Right));
        return geo;
    }

    /// <summary>Distribuição (área por classe) em barras horizontais com as cores das classes.</summary>
    public static MarkingGeometry Distribuicao(IReadOnlyList<(ClasseTema Classe, double Area)> faixas, string titulo)
    {
        var geo = new MarkingGeometry();
        var total = Math.Max(1e-9, faixas.Sum(f => f.Area));
        const double W = 60, H = 4;
        var y = 0.0;
        geo.Annotations.Add(new AnnotationText(new Vec2(0, H * 1.5), titulo, 30, TextAlign.Left));
        foreach (var (c, a) in faixas)
        {
            var frac = a / total;
            if (frac > 1e-5)
                geo.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(0, y - H * 0.8), new Vec2(W * frac, y)), MarkingColor.Preta) { Rgb = c.Cor.Hex });
            geo.Annotations.Add(new AnnotationText(new Vec2(W + 2, y), $"{c.Rotulo}: {a.ToString("#,##0", Pt)} m² ({(frac * 100).ToString("0.#", Pt)} %)", 22, TextAlign.Left));
            y -= H;
        }
        geo.Annotations.Add(new AnnotationLine(new[] { new Vec2(0, H * 0.4), new Vec2(0, y) }, MarkingColor.Preta));
        return geo;
    }
}
