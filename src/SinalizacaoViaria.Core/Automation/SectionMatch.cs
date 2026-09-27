using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Composição da calçada de uma via (faixa de serviço junto ao meio-fio e sarjeta) lida da seção guardada.</summary>
public readonly record struct SidewalkSection(double Service, bool Grass, double Gutter)
{
    public bool HasService => Service > 0.05;
    public bool HasGutter => Gutter > 0.05;
}

/// <summary>
/// Faz as conexões (interseções, rotatórias, cul-de-sacs) seguirem exatamente a composição das vias existentes:
/// faixa de serviço (gramada ou em concreto) entre o meio-fio e o passeio e sarjeta junto ao meio-fio.
/// </summary>
public static class SectionMatch
{
    public static SidewalkSection? From(RoadPavementDefinition? pav, double curbWidth)
    {
        var setup = pav == null ? null : RoadTemplates.FromJson(pav.SetupJson);
        if (setup == null) return null;
        double service = 0, gutter = 0;
        var grass = false;
        foreach (var e in setup.Right.Concat(setup.Left).Where(e => e.Tipo == TipoElementoSecao.Calcada))
        {
            var s = Math.Clamp(e.FaixaServico, curbWidth, Math.Max(curbWidth, e.Largura)) - curbWidth;
            if (s > service) { service = s; grass = e.ServicoGramado; }
            if (setup.PhysicalElements) gutter = Math.Max(gutter, e.Sarjeta);
        }
        return new SidewalkSection(service, grass, gutter);
    }

    /// <summary>Composição dominante entre várias vias (maior faixa de serviço; grama se alguma via a tiver).</summary>
    public static SidewalkSection? From(IEnumerable<RoadPavementDefinition> pavs, double curbWidth)
    {
        SidewalkSection? best = null;
        foreach (var p in pavs)
        {
            var s = From(p, curbWidth);
            if (s == null) continue;
            best = best == null ? s : new SidewalkSection(Math.Max(best.Value.Service, s.Value.Service), best.Value.Grass || s.Value.Grass, Math.Max(best.Value.Gutter, s.Value.Gutter));
        }
        return best;
    }

    /// <summary>Faixa de serviço: anel entre o meio-fio e a largura da faixa, limitado à calçada.</summary>
    public static List<Polygon2> ServiceBand(IEnumerable<Polygon2> pavement, IEnumerable<Polygon2> sidewalk, double curbWidth, double service)
    {
        var pav = pavement.ToList();
        if (service <= 0.05 || pav.Count == 0) return new();
        var band = PolygonOps.Difference(PolygonOps.Offset(pav, curbWidth + service, true), PolygonOps.Offset(pav, curbWidth, true));
        return PolygonOps.Intersect(band, sidewalk).Where(p => p.Area > 0.05).ToList();
    }

    /// <summary>Sarjeta: faixa dentro da pista, junto ao meio-fio.</summary>
    public static List<Polygon2> GutterBand(IEnumerable<Polygon2> pavement, IEnumerable<Polygon2> curb, double gutter)
    {
        var pav = pavement.ToList();
        if (gutter <= 0.05 || pav.Count == 0) return new();
        var inner = PolygonOps.Difference(pav, PolygonOps.Offset(pav, -gutter, true));
        return PolygonOps.Intersect(inner, PolygonOps.Offset(curb, gutter + 0.05, true)).Where(p => p.Area > 0.02).ToList();
    }
}
