using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Sonorizador longitudinal: elementos repetidos ao longo do caminho (fresado, termoplástico em relevo, barras, tachas).</summary>
public static class RumbleStripGenerator
{
    public static MarkingGeometry Generate(RumbleStripDefinition d, Polyline2 path)
    {
        var geo = new MarkingGeometry { PathLength = path.Length };
        var a = Math.Max(0, d.StartSetback);
        var b = path.Length - Math.Max(0, d.EndSetback);
        if (b - a < 0.5) { geo.Warnings.Add("Os recuos são maiores que o caminho."); return geo; }
        var len = Math.Max(0.02, d.ElementLength);
        var wid = Math.Max(0.05, d.ElementWidth);
        var sp = Math.Max(len + 0.02, d.Spacing);
        var color = d.Color ?? (d.Type == TipoSonorizador.Fresado ? MarkingColor.Preta : MarkingColor.Branca);
        // Trechos com interrupções (passagem de ciclistas).
        var runs = new List<(double, double)>();
        if (d.SegmentLength > 1 && d.GapLength > 0.1)
            for (var s = a; s < b; s += d.SegmentLength + d.GapLength) runs.Add((s, Math.Min(b, s + d.SegmentLength)));
        else runs.Add((a, b));
        var units = 0;
        var ang = d.Angle * Math.PI / 180;
        foreach (var (r0, r1) in runs)
        {
            if (d.Type == TipoSonorizador.TermoplasticoRelevo)
            {
                // Linha-base contínua (3 mm) com os relevos sobre ela.
                var line = new Polyline2(path.SubPoints(r0, r1)).Offset(d.Offset);
                geo.AddRange(PolygonOps.Strip(line.Points, wid), color, 0.003);
                geo.PaintedLength += r1 - r0;
            }
            for (var s = r0 + len / 2; s <= r1 - len / 2 + 1e-9; s += sp)
            {
                var c = path.PointAt(s) + path.TangentAt(s).PerpLeft * d.Offset;
                var t = path.TangentAt(s);
                // Elemento retangular (opcionalmente em ângulo – chevron).
                var tt = new Vec2(t.X * Math.Cos(ang) - t.Y * Math.Sin(ang), t.X * Math.Sin(ang) + t.Y * Math.Cos(ang));
                var nn = tt.PerpLeft;
                var poly = new Polygon2(new[] { c - tt * (len / 2) - nn * (wid / 2), c + tt * (len / 2) - nn * (wid / 2), c + tt * (len / 2) + nn * (wid / 2), c - tt * (len / 2) + nn * (wid / 2) });
                switch (d.Type)
                {
                    case TipoSonorizador.Fresado:
                        // Ranhura: faixa escura rente ao pavimento (a profundidade entra nos quantitativos).
                        geo.Pieces.Add(new MarkingPiece(poly, color) { Thickness = 0.001, IsUnit = true });
                        break;
                    case TipoSonorizador.Tachas:
                    {
                        var h = Math.Max(0.005, d.Depth);
                        geo.Pieces.AddRange(DeviceGenerator.Place(DeviceGenerator.StudPieces(len, wid, h, color, true), c, t));
                        break;
                    }
                    default:
                        geo.Pieces.Add(new MarkingPiece(poly, color) { Thickness = Math.Max(0.003, d.Depth), Elevation = d.Type == TipoSonorizador.TermoplasticoRelevo ? 0.003 : 0, IsUnit = true });
                        break;
                }
                units++;
            }
        }
        geo.UnitCount = units;
        if (d.Type == TipoSonorizador.Fresado && d.Depth > 0.016) geo.Warnings.Add("Fresado com mais de 16 mm de profundidade: confira com o órgão (usual 10–13 mm).");
        if (d.Position == PosicaoSonorizador.Acostamento && d.GapLength < 0.1)
            geo.Warnings.Add("Acostamento com tráfego de bicicletas: prever interrupções (ex.: 12 m de sonorizador e 3,6 m livres).");
        return geo;
    }
}

/// <summary>
/// Área de escape de caminhões: caixa de retenção (material granular, profundidade com transição), meios-fios de contenção,
/// faixa de serviço com âncoras, berma no fim, zebrado de entrada e delineadores.
/// </summary>
public static class EscapeRampGenerator
{
    public static MarkingGeometry Generate(EscapeRampDefinition d, Polyline2 path, BuildContext ctx)
    {
        var geo = new MarkingGeometry { PathLength = path.Length };
        var L = Math.Min(d.ActualLength, path.Length);
        var need = d.RequiredLength;
        if (path.Length < d.ActualLength - 0.5)
            geo.Warnings.Add($"O eixo desenhado ({path.Length:0} m) é menor que o comprimento da caixa ({d.ActualLength:0} m) – a caixa foi limitada ao eixo.");
        if (L < need - 0.5)
            geo.Warnings.Add($"Caixa de retenção com {L:0} m – para {d.EntrySpeed:0} km/h, rampa {d.Grade:0.#}% e {Material(d.Material)} são necessários {need:0} m (L = V²/254(R+G)).");
        if (d.Width < 8 - 1e-6) geo.Warnings.Add("Largura da caixa menor que 8 m (AASHTO recomenda 8 a 12 m, para dois veículos).");
        var w = Math.Max(3, d.Width);
        var bed = MarkingColor.Brita;
        // Leito com a profundidade crescendo na entrada (0,08 m → total) e o topo rente à superfície.
        const double step = 5;
        for (var s = 0.0; s < L - 1e-6; s += step)
        {
            var e = Math.Min(L, s + step);
            var mid = (s + e) / 2;
            var depth = d.DepthTaper > 1 && mid < d.DepthTaper ? 0.08 + (d.BedDepth - 0.08) * mid / d.DepthTaper : d.BedDepth;
            foreach (var p in RoadGenerator.VariableBand(path, s, e, _ => -w / 2, _ => w / 2))
                geo.Pieces.Add(new MarkingPiece(p, bed) { Thickness = depth, Elevation = -depth });
        }
        // Meios-fios de contenção nas laterais da caixa.
        foreach (var side in new[] { -1.0, 1.0 })
            foreach (var p in RoadGenerator.VariableBand(path, 0, L, _ => side * (w / 2 + 0.075) - 0.075, _ => side * (w / 2 + 0.075) + 0.075))
                geo.Pieces.Add(new MarkingPiece(p, MarkingColor.Concreto) { Thickness = 0.30, Elevation = -0.15 });
        // Faixa de serviço (pavimentada) com âncoras para o guincho.
        if (d.ServiceWidth > 0.5)
        {
            var sg = d.ServiceLeft ? 1.0 : -1.0;
            var o0 = sg * (w / 2 + 0.15);
            var o1 = sg * (w / 2 + 0.15 + d.ServiceWidth);
            foreach (var p in RoadGenerator.VariableBand(path, 0, Math.Min(path.Length, L + 10), _ => Math.Min(o0, o1), _ => Math.Max(o0, o1)))
                geo.Pieces.Add(new MarkingPiece(p, MarkingColor.Asfalto) { Thickness = 0.05, Elevation = -0.05 });
            var units = 0;
            for (var s = Math.Min(30, L / 2); s < L && d.AnchorSpacing > 5; s += d.AnchorSpacing)
            {
                var c = path.PointAt(s) + path.TangentAt(s).PerpLeft * (sg * (w / 2 + 0.15 + d.ServiceWidth - 0.6));
                geo.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(c - new Vec2(0.3, 0.3), c + new Vec2(0.3, 0.3)), MarkingColor.Concreto) { Thickness = 0.25, IsUnit = true });
                units++;
            }
            geo.UnitCount += units;
        }
        // Berma de material no fim (atenuador).
        if (d.EndBerm && path.Length > L + 1)
            foreach (var p in RoadGenerator.VariableBand(path, L, Math.Min(path.Length, L + 6), _ => -w / 2, _ => w / 2))
                geo.Pieces.Add(new MarkingPiece(p, MarkingColor.Terra) { Thickness = 1.2 });
        if (d.Signage)
        {
            // Zebrado de entrada (canalização) e delineadores a cada 10 m nos dois lados.
            var entry = RoadGenerator.VariableBand(path, 0, Math.Min(12, L), _ => -w / 2, _ => w / 2).FirstOrDefault();
            if (entry != null && ctx.Catalog.Hachura("ZPA") is { } preset)
                geo.Merge(HatchGenerator.Generate(entry, preset, new HatchOptions { ReferenceDirection = path.TangentAt(0), Chevron = true }));
            for (var s = 0.0; s <= L + 1e-6; s += 10)
                foreach (var side in new[] { -1.0, 1.0 })
                {
                    var c = path.PointAt(Math.Min(s, path.Length)) + path.TangentAt(Math.Min(s, path.Length)).PerpLeft * (side * (w / 2 + 0.5));
                    geo.Pieces.Add(new MarkingPiece(Polygon2.Rectangle(c - new Vec2(0.05, 0.05), c + new Vec2(0.05, 0.05)), MarkingColor.Branca) { Thickness = 1.0 });
                }
        }
        return geo;
    }

    public static string Material(MaterialRetencao m) => m switch
    {
        MaterialRetencao.Seixo => "seixo rolado (R = 0,25)",
        MaterialRetencao.Areia => "areia (R = 0,15)",
        MaterialRetencao.Cascalho => "cascalho solto (R = 0,10)",
        _ => "brita solta (R = 0,05)",
    };
}
