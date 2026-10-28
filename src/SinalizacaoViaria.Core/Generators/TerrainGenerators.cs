using System.Globalization;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Temas do terreno (regiões e legenda 2D) e plataformas (borda e plataforma da terraplenagem).</summary>
public static class TerrainGenerators
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static string N(double v, string f = "#,##0.##") => v.ToString(f, Pt);

    /// <summary>Mapa do tema (e a comparação, no tema Diferença) com as superfícies dadas (cotas absolutas, m).</summary>
    public static (MapaTema Mapa, ComparacaoSuperficies? Comparacao) Calcular(TemaTerrenoDefinition d, Func<Vec2, double?>? natural, Func<Vec2, double?>? atual)
    {
        var min = new Vec2(Math.Min(d.Min.X, d.Max.X), Math.Min(d.Min.Y, d.Max.Y));
        var max = new Vec2(Math.Max(d.Min.X, d.Max.X), Math.Max(d.Min.Y, d.Max.Y));
        var nat = natural ?? atual;
        var atu = atual ?? natural;
        if (nat == null || atu == null) return (new MapaTema { Tema = d.Tema }, null);
        if (d.Tema == TemaTerreno.Diferenca)
        {
            var custom = d.Limites.Count > 0 ? TemasTerreno.ClassesDosLimites(TemaTerreno.Diferenca, d.Limites, aberta: true) : null;
            var comp = ComparacaoSuperficies.Comparar(nat, atu, min, max, d.Passo, classes: custom);
            var classes = custom ?? comp.Distribuicao.Select(x => x.Classe).ToList();
            return (TemasTerreno.Mapa(comp.Grade, TemaTerreno.Diferenca, classes, comp.Valor), comp);
        }
        var g = TemasTerreno.Amostrar(d.Superficie == SuperficieTema.Atual ? atu : nat, min, max, d.Passo);
        List<ClasseTema> cls;
        if (d.Limites.Count > 0) cls = TemasTerreno.ClassesDosLimites(d.Tema, d.Limites);
        else if (d.Tema == TemaTerreno.Elevacao)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            for (int j = 0; j < g.Ny; j++)
                for (int i = 0; i < g.Nx; i++)
                    if (TemasTerreno.Valor(g, i, j, TemaTerreno.Elevacao) is { } v) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
            cls = TemasTerreno.ClassesPadrao(TemaTerreno.Elevacao, lo > hi ? double.NaN : lo, lo > hi ? double.NaN : hi, d.Classes);
        }
        else cls = TemasTerreno.ClassesPadrao(d.Tema, double.NaN, double.NaN);
        return (TemasTerreno.Mapa(g, d.Tema, cls), null);
    }

    public static string NomeDoTema(TemaTerreno t) => t switch
    {
        TemaTerreno.Elevacao => "ELEVAÇÃO",
        TemaTerreno.Orientacao => "ORIENTAÇÃO DAS ENCOSTAS",
        TemaTerreno.Diferenca => "TERRENO ATUAL − NATURAL",
        _ => "DECLIVIDADE",
    };

    /// <summary>Regiões de cada classe e a legenda com a área e a fração de cada uma (barras), no papel da vista.</summary>
    public static MarkingGeometry Tema(TemaTerrenoDefinition d, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        if (ctx.TerrenoNatural == null && ctx.TerrenoAtual == null)
        {
            geo.Warnings.Add("Tema do terreno: o projeto não tem Toposolid (Massa e terreno) para ler.");
            return geo;
        }
        var (mapa, comp) = Calcular(d, ctx.TerrenoNatural, ctx.TerrenoAtual);
        Desenhar(geo, d, mapa, comp, ctx.Mm);
        return geo;
    }

    /// <summary>Desenho do tema (regiões, moldura e legenda) – o mesmo na prévia e na vista.</summary>
    public static void Desenhar(MarkingGeometry geo, TemaTerrenoDefinition d, MapaTema mapa, ComparacaoSuperficies? comp, Func<double, double> mm)
    {
        foreach (var (c, regs, _) in mapa.Faixas)
            foreach (var r in regs)
                geo.Pieces.Add(new MarkingPiece(r, MarkingColor.Preta) { Rgb = c.Cor.Hex, Layer = "TEMA" });
        var min = new Vec2(Math.Min(d.Min.X, d.Max.X), Math.Min(d.Min.Y, d.Max.Y));
        var max = new Vec2(Math.Max(d.Min.X, d.Max.X), Math.Max(d.Min.Y, d.Max.Y));
        geo.Annotations.Add(new AnnotationLine(new[] { min, new Vec2(max.X, min.Y), max, new Vec2(min.X, max.Y), min }, MarkingColor.Preta));
        // Legenda: título, uma linha por classe (amostra, intervalo, área e fração em barra) e o resumo.
        var at = d.Legenda ?? new Vec2(max.X + mm(10), max.Y);
        var t = Math.Max(1, d.TextoMm);
        var row = mm(t * 2.6);
        var title = string.IsNullOrWhiteSpace(d.Titulo) ? NomeDoTema(d.Tema) : d.Titulo;
        geo.Annotations.Add(new AnnotationText(at, title, t * 1.3, TextAlign.Left));
        var y = at.Y - mm(t * 3.2);
        var total = Math.Max(1e-9, mapa.AreaTotal);
        var barMax = mm(30);
        foreach (var (c, _, area) in mapa.Faixas)
        {
            var sw = Polygon2.Rectangle(new Vec2(at.X, y - row * 0.8), new Vec2(at.X + mm(6), y));
            geo.Pieces.Add(new MarkingPiece(sw, MarkingColor.Preta) { Rgb = c.Cor.Hex, Layer = "LEGENDA" });
            var frac = area / total;
            if (frac > 1e-4)
                geo.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(new Vec2(at.X + mm(8), y - row * 0.65), new Vec2(at.X + mm(8) + barMax * frac, y - row * 0.15)),
                    MarkingColor.Preta) { Rgb = c.Cor.Hex, Layer = "LEGENDA" });
            geo.Annotations.Add(new AnnotationText(new Vec2(at.X + mm(8) + barMax + mm(3), y), $"{c.Rotulo} – {N(area, "#,##0")} m² ({N(frac * 100, "0.#")} %)", t, TextAlign.Left));
            y -= row;
        }
        string resumo = d.Tema switch
        {
            TemaTerreno.Diferenca when comp != null =>
                $"Corte {N(comp.Corte, "#,##0")} m³ · Aterro {N(comp.Aterro, "#,##0")} m³ · de {N(comp.Min)} a {N(comp.Max)} m",
            TemaTerreno.Elevacao => $"Cotas de {N(mapa.Min)} a {N(mapa.Max)} m (média {N(mapa.Media)} m)",
            TemaTerreno.Declividade => $"Declividade de {N(mapa.Min, "0.#")} a {N(mapa.Max, "0.#")} % (média {N(mapa.Media, "0.#")} %)",
            _ => "Azimute a partir do Norte do projeto (eixo Y), sentido horário",
        };
        var sup = d.Tema == TemaTerreno.Diferenca ? "atual − natural" : d.Superficie == SuperficieTema.Atual ? "terreno atual" : "terreno natural";
        geo.Annotations.Add(new AnnotationText(new Vec2(at.X, y - mm(t)), resumo, t, TextAlign.Left));
        geo.Annotations.Add(new AnnotationText(new Vec2(at.X, y - mm(t * 3.6)),
            $"Área {N(mapa.AreaTotal, "#,##0")} m² · {sup} · malha de {N(d.Passo)} m · classes: padrão do plugin ou informadas", t * 0.8, TextAlign.Left));
    }

    /// <summary>
    /// Plataforma: a borda (faixa de 0,15 m para dentro do contorno, sobre a plataforma) e a plataforma da terraplenagem no
    /// contorno, na cota do caminho, com os taludes até o terreno.
    /// </summary>
    public static MarkingGeometry Plataforma(PlataformaDefinition d, Polyline2? path)
    {
        var geo = new MarkingGeometry();
        if (path == null || path.Points.Count < 3)
        {
            geo.Warnings.Add("Plataforma: contorno não encontrado.");
            return geo;
        }
        var ring = new Polygon2(path.Points.Take(path.Points[0].DistanceTo(path.Points[^1]) < 1e-6 ? path.Points.Count - 1 : path.Points.Count));
        if (Math.Abs(ring.Area) < 1)
        {
            geo.Warnings.Add("Plataforma: contorno com menos de 1 m².");
            return geo;
        }
        var inner = PolygonOps.Offset(new[] { ring }, -0.15);
        foreach (var b in PolygonOps.Difference(new[] { ring }, inner))
            geo.Pieces.Add(new MarkingPiece(b, MarkingColor.Terra) { Thickness = 0.02, Layer = "PLATAFORMA" });
        geo.Pads.Add(new GradePad(ring, 0) { CutSlope = d.CutSlope, FillSlope = d.FillSlope, Daylight = d.Taludes, Label = "Plataforma" });
        return geo;
    }
}
