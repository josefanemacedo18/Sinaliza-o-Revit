using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Seção do leito de um rio ou córrego numa estaca.</summary>
public sealed record SecaoLeito(double Estaca, double ZFundo, double ZAgua, double LarguraFundo, double LarguraAgua, double AreaMolhada);

/// <summary>Corpos d'água (item 11): fundo do leito, seções, contorno do fundo do lago, água e faixa de proteção.</summary>
public static class CorposDagua
{
    /// <summary>Largura na superfície da água (m): fundo + 2 × lâmina × talude.</summary>
    public static double LarguraAgua(CorpoDaguaDefinition d) => Math.Max(0.1, d.LarguraFundo) + 2 * Math.Max(0, d.Lamina) * Math.Max(0, d.TaludeMargem);

    /// <summary>
    /// Fundo do leito ao longo do eixo: o informado (estaca, cota) ou, sem ele, o terreno natural no eixo menos a profundidade –
    /// sem nunca subir para jusante (mínimo acumulado, padrão do plugin).
    /// </summary>
    public static VerticalProfile Fundo(CorpoDaguaDefinition d, double comprimento, VerticalProfile? terreno)
    {
        var p = new VerticalProfile();
        if (d.Fundo.Count >= 1)
        {
            var pts = d.Fundo.OrderBy(v => v.X).ToList();
            if (pts[0].X > 1e-6) pts.Insert(0, new Vec2(0, pts[0].Y));
            if (pts[^1].X < comprimento - 1e-6) pts.Add(new Vec2(comprimento, pts[^1].Y));
            foreach (var v in pts) p.Pvis.Add((v.X, v.Y));
            return p;
        }
        if (terreno == null) { p.Pvis.Add((0, -d.Profundidade)); p.Pvis.Add((Math.Max(1, comprimento), -d.Profundidade)); return p; }
        var n = Math.Max(2, (int)Math.Ceiling(comprimento / 2.0) + 1);
        var min = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var s = comprimento * i / (n - 1);
            min = Math.Min(min, terreno.Z(s) - d.Profundidade);
            p.Pvis.Add((s, min));
        }
        return p;
    }

    /// <summary>Seção do leito na estaca (trapézio de fundo, taludes e lâmina).</summary>
    public static SecaoLeito Secao(CorpoDaguaDefinition d, VerticalProfile fundo, double s)
    {
        var zf = fundo.Z(s);
        var b = Math.Max(0.1, d.LarguraFundo);
        var h = Math.Max(0, d.Lamina);
        var m = Math.Max(0, d.TaludeMargem);
        return new SecaoLeito(s, zf, zf + h, b, b + 2 * h * m, (b + m * h) * h);
    }

    /// <summary>Contorno do fundo do lago: a margem (no nível d'água) para dentro de (nível − fundo) × talude; nulo se não cabe.</summary>
    public static Polygon2? FundoDoLago(Polygon2 margem, double cotaFundo, double nivel, double talude)
    {
        var d = Math.Max(0, nivel - cotaFundo) * Math.Max(0, talude);
        if (d < 1e-6) return margem;
        var inner = PolygonOps.Offset(new[] { margem }, -d).Where(p => p.Area > 0.5).OrderByDescending(p => p.Area).FirstOrDefault();
        return inner;
    }

    /// <summary>Contorno da margem do lago (fecha o anel do caminho).</summary>
    public static Polygon2 Margem(Polyline2 path)
    {
        var pts = path.Points.ToList();
        if (pts.Count > 2 && pts[0].DistanceTo(pts[^1]) < 1e-6) pts.RemoveAt(pts.Count - 1);
        return new Polygon2(pts);
    }

    /// <summary>Superfície da água em planta: a faixa da largura na água ao longo do eixo (rio) ou o contorno (lago).</summary>
    public static List<Polygon2> Agua(CorpoDaguaDefinition d, Polyline2 path)
    {
        if (!d.EhRio) return new List<Polygon2> { Margem(path) };
        var w = LarguraAgua(d);
        return PolygonOps.Strip(path.Points.ToList(), w);
    }

    /// <summary>
    /// Faixa de proteção informada: <see cref="CorpoDaguaDefinition.FaixaProtecao"/> m a partir da borda da água, nas duas margens
    /// (rio) ou em volta (lago); vazia sem largura informada.
    /// </summary>
    public static List<Polygon2> Faixa(CorpoDaguaDefinition d, Polyline2 path)
    {
        if (d.FaixaProtecao <= 0.01) return new List<Polygon2>();
        var agua = Agua(d, path);
        var fora = d.EhRio ? PolygonOps.Strip(path.Points.ToList(), LarguraAgua(d) + 2 * d.FaixaProtecao) : PolygonOps.Offset(agua, d.FaixaProtecao, true);
        return PolygonOps.Difference(fora, agua).Where(p => p.Area > 0.01).ToList();
    }

    /// <summary>Nível d'água no ponto mais próximo do eixo (rio) ou o nível do lago.</summary>
    public static double NivelPerto(CorpoDaguaDefinition d, Polyline2 path, Vec2 p, VerticalProfile? fundo)
    {
        if (!d.EhRio) return d.NivelAgua;
        var s = path.Project(p).Station;
        return (fundo ?? Fundo(d, path.Length, d.GroundLine is { Count: >= 2 } g ? Perfil(g) : null)).Z(s) + d.Lamina;
    }

    /// <summary>Perfil linear pelos pontos (estaca, cota).</summary>
    public static VerticalProfile Perfil(IEnumerable<Vec2> pts)
    {
        var p = new VerticalProfile();
        foreach (var v in pts.OrderBy(v => v.X)) p.Pvis.Add((v.X, v.Y));
        return p;
    }

    /// <summary>Lê o fundo por estaca: uma linha "estaca; cota" por ponto (vírgula decimal aceita).</summary>
    public static List<Vec2> LerFundo(string? texto)
    {
        var res = new List<Vec2>();
        foreach (var linha in (texto ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var v = TemasTerreno.LerLimites(linha.Replace("\r", ""));
            if (v.Count >= 2) res.Add(new Vec2(v[0], v[1]));
        }
        return res.OrderBy(p => p.X).ToList();
    }
}
