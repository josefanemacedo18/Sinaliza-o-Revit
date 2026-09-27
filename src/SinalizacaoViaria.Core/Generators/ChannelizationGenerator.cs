using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Marcas de canalização em transição (MTL, MAO, MAP): área neutra em zebrado com linha de canalização, ao longo de uma
/// linha de referência no sentido do tráfego. Comprimentos pelo MBST Vol. IV (6.2) e DER/SP (B.3): l = 0,5·V·d;
/// barras de 0,50 m a 45° a cada 1,50 m (V &lt; 80) ou 2,50 m; linha de canalização de 0,20 m.
/// </summary>
public static class ChannelizationGenerator
{
    /// <summary>Comprimento da transição de entrada usado (informado ou calculado).</summary>
    public static double EntryLength(ChannelizationDefinition d)
    {
        if (d.Length > 0.05) return d.Length;
        var dd = Math.Abs(d.WidthChange);
        return d.Type switch
        {
            TipoCanalizacao.Acostamento => DesignRules.ShoulderTransition(d.Speed),
            TipoCanalizacao.Obstaculo => DesignRules.TaperLength(d.Speed, dd, d.Rural, obstacle: true),
            _ => DesignRules.TaperLength(d.Speed, dd, d.Rural),
        };
    }

    /// <summary>Contorno(s) da área neutra no sistema local do caminho (estação, deslocamento à esquerda).</summary>
    public static List<List<(double S, double O)>> LocalOutline(ChannelizationDefinition d)
    {
        var l = EntryLength(d);
        var s0 = d.StartStation;
        var dd = d.WidthChange;
        var res = new List<List<(double, double)>>();
        switch (d.Type)
        {
            case TipoCanalizacao.Obstaculo:
            {
                var l2 = d.ExitLength > 0.05 ? d.ExitLength : l;
                var lo = Math.Max(0.5, d.ObstacleLength);
                var w = Math.Abs(dd) + Math.Clamp(d.Clearance, 0, 2);
                var sgn = Math.Sign(dd == 0 ? 1 : dd);
                var one = new List<(double, double)> { (s0, 0), (s0 + l, sgn * w), (s0 + l + lo, sgn * w), (s0 + l + lo + l2, 0) };
                if (d.BothSides)
                {
                    one.Add((s0 + l + lo, -sgn * w));
                    one.Add((s0 + l, -sgn * w));
                }
                res.Add(one);
                break;
            }
            case TipoCanalizacao.Acostamento:
            {
                var L = d.TangentLength > 0.05 ? d.TangentLength : DesignRules.ShoulderTransition(d.Speed);
                res.Add(new List<(double, double)> { (s0, 0), (s0 + l, dd), (s0 + l + L, dd), (s0 + l + L, 0) });
                break;
            }
            default:
                res.Add(new List<(double, double)> { (s0, 0), (s0 + l, 0), (s0 + l, dd) });
                break;
        }
        return res;
    }

    public static MarkingGeometry Generate(ChannelizationDefinition d, Polyline2 path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var l = EntryLength(d);
        var total = LocalOutline(d).Max(c => c.Max(p => p.S)) - d.StartStation;
        if (path.Length + 1e-6 < d.StartStation + total)
            geo.Warnings.Add($"A linha de referência tem {path.Length:0.0} m; a canalização precisa de {d.StartStation + total:0.0} m – prolongue a linha ou reduza os comprimentos.");
        var minL = d.Type == TipoCanalizacao.Obstaculo ? (d.Rural ? 60 : 30) : 0;
        if (d.Length > 0.05 && d.Length < minL) geo.Warnings.Add($"Transição de {d.Length:0} m: o mínimo recomendado junto a obstáculos é {minL} m ({(d.Rural ? "rodovia" : "via urbana")}).");
        if (d.Length > 0.05 && d.Type != TipoCanalizacao.Acostamento && d.Length < 0.5 * d.Speed * Math.Abs(d.WidthChange) - 0.5)
            geo.Warnings.Add($"Transição menor que l = 0,5·V·d = {0.5 * d.Speed * Math.Abs(d.WidthChange):0} m (MBST Vol. IV / DER-SP).");

        Vec2 W(double s, double o)
        {
            var sc = Math.Clamp(s, 0, path.Length);
            return path.PointAt(sc) + path.TangentAt(sc).PerpLeft * o;
        }
        var contours = LocalOutline(d).Select(c => (IReadOnlyList<Vec2>)c.Select(p => W(p.S, p.O)).ToList()).ToList();
        var regions = PolygonOps.FromContours(contours);
        if (regions.Count == 0) { geo.Warnings.Add("Área de canalização inválida (largura ou comprimento nulos)."); return geo; }
        var preset = new HachuraDef
        {
            Codigo = d.DisplayCode, Nome = "Canalização", Grupo = GrupoMarca.Canalizacao,
            CorBarras = d.Color, CorBorda = d.Color, LarguraBarra = Math.Max(0.05, d.BarWidth),
            Espacamento = d.Gap > 0.01 ? d.Gap : DesignRules.ChannelHatchGap(d.Speed), Angulo = 45, LarguraBorda = Math.Max(0, d.LineWidth),
        };
        var mid = Math.Clamp(d.StartStation + total / 2, 0, path.Length);
        var dir = path.TangentAt(mid);
        foreach (var region in regions)
            geo.Merge(HatchGenerator.Generate(region, preset, new HatchOptions { ReferenceDirection = dir, AxisPoint = path.PointAt(mid) }));
        geo.PathLength = total;
        geo.UnitCount = 1;
        geo.Warnings.Add($"{d.DisplayCode}: transição de {l:0.0} m para d = {Math.Abs(d.WidthChange):0.00} m a {d.Speed:0} km/h.");
        return geo;
    }
}
