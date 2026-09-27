using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Drenagem superficial: bocas de lobo (simples, dupla, com grelha, combinada), grelhas de sarjeta e de piso, canaleta com
/// grelha contínua e poço de visita – com caixa de captação, laje/tampa, rebaixo da sarjeta e tubo de ligação.
/// </summary>
/// <remarks>Referências: DNIT 030/2004-ES (dispositivos de drenagem pluvial urbana), ABNT NBR 10160 (tampões e grelhas de
/// ferro fundido dúctil), PMSP – Diretrizes de projeto de drenagem (bocas de lobo com depressão).</remarks>
public static class DrainageGenerator
{
    private static MarkingColor GrateColor(DrainageDefinition d) => d.Material == MaterialGrelha.Concreto ? MarkingColor.Concreto : MarkingColor.Metal;

    public static MarkingGeometry Build(DrainageDefinition d, Polyline2? path)
    {
        var geo = new MarkingGeometry();
        if (d.IsLinear)
        {
            if (path == null || path.Length < 0.3) { geo.Warnings.Add("Linha da canaleta não encontrada."); return geo; }
            Channel(geo, d, path);
            geo.PaintedLength = path.Length;
            geo.PathLength = path.Length;
            BridgeGenerator.Measure(geo, GrateColor(d), path.Length * d.GrateWidth);
            return geo;
        }
        var u = d.Along.Length < 1e-9 ? Vec2.UnitX : d.Along.Normalized();
        // y local: da pista (negativo) para a calçada (positivo); origem na face do meio-fio, cota 0 = pavimento.
        var v = d.SidewalkLeft ? u.PerpLeft : u.PerpRight;
        Vec2 W(double x, double y) => d.Position + u * x + v * y;
        var modules = Math.Clamp(d.Modules, 1, 4);
        switch (d.Type)
        {
            case TipoDrenagem.PocoDeVisita:
                Manhole(geo, d, d.Position);
                break;
            case TipoDrenagem.GrelhaQuadrada:
                for (int m = 0; m < modules; m++)
                {
                    var x = (m - (modules - 1) / 2.0) * (d.GrateLength + 0.3);
                    Box(geo, d, W(x, 0), u, d.BoxLength, d.BoxWidth, 0);
                    Grate(geo, d, W(x, 0), u, d.GrateLength, d.GrateWidth, 0);
                }
                break;
            default:
            {
                var curbOpening = d.Type is TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla or TipoDrenagem.BocaDeLoboCombinada;
                var grate = d.Type is TipoDrenagem.BocaDeLoboGrelha or TipoDrenagem.BocaDeLoboCombinada or TipoDrenagem.GrelhaSarjeta;
                var total = (curbOpening ? d.OpeningLength : d.GrateLength) * modules + 0.3 * (modules - 1);
                // Rebaixo da sarjeta (depressão) com transições.
                if (d.Depression > 0.001) Depression(geo, d, u, v, total);
                for (int m = 0; m < modules; m++)
                {
                    var len = curbOpening ? d.OpeningLength : d.GrateLength;
                    var x = -total / 2 + len / 2 + m * (len + 0.3);
                    if (curbOpening)
                    {
                        // Caixa sob a calçada, atrás do meio-fio; abertura ("boca") na guia com guia-chapéu.
                        Box(geo, d, W(x, d.CurbWidth + d.BoxWidth / 2 + d.WallThickness), u, Math.Max(d.BoxLength, len), d.BoxWidth, d.CurbHeight);
                        Opening(geo, d, W(x, 0), u, v, len);
                    }
                    if (grate)
                    {
                        var gy = -(d.GrateWidth / 2 + 0.05);
                        if (!curbOpening) Box(geo, d, W(x, gy), u, Math.Max(d.BoxLength, d.GrateLength), Math.Max(d.BoxWidth, d.GrateWidth + 0.1), -d.Depression);
                        Grate(geo, d, W(x, gy), u, d.GrateLength, d.GrateWidth, -d.Depression);
                    }
                }
                break;
            }
        }
        geo.UnitCount = modules;
        BridgeGenerator.Measure(geo, GrateColor(d), 0);
        return geo;
    }

    /// <summary>Caixa de captação (paredes, fundo e laje com tampa), topo em <paramref name="top"/>.</summary>
    private static void Box(MarkingGeometry geo, DrainageDefinition d, Vec2 c, Vec2 u, double inLen, double inWid, double top)
    {
        var t = Math.Max(0.08, d.WallThickness);
        var bottom = top - Math.Max(0.3, d.BoxDepth);
        var v = u.PerpLeft;
        // Fundo.
        SolidSweep.Add(geo, SolidSweep.Box(c, u, inLen + 2 * t, inWid + 2 * t, bottom - t, bottom), MarkingColor.Concreto, "CAIXA");
        // Paredes.
        foreach (var s in new[] { -1, 1 })
        {
            SolidSweep.Add(geo, SolidSweep.Box(c + u * (s * (inLen / 2 + t / 2)), u, t, inWid + 2 * t, bottom, top - 0.10), MarkingColor.Concreto, "CAIXA");
            SolidSweep.Add(geo, SolidSweep.Box(c + v * (s * (inWid / 2 + t / 2)), u, inLen, t, bottom, top - 0.10), MarkingColor.Concreto, "CAIXA");
        }
        // Laje de cobertura com tampa de inspeção (quando a grelha não cobre a caixa).
        if (top > 0.05 || d.Type == TipoDrenagem.PocoDeVisita)
        {
            SolidSweep.Add(geo, SolidSweep.Box(c, u, inLen + 2 * t, inWid + 2 * t, top - 0.10, top), MarkingColor.Concreto, "CAIXA");
            if (d.Lid) SolidSweep.Add(geo, SolidSweep.Cylinder(c, d.LidDiameter / 2, top, top + 0.012, 24), MarkingColor.Metal, "TAMPA");
        }
        if (d.OutletPipe)
        {
            var r = Math.Clamp(d.PipeDiameter, 0.2, 1.5) / 2;
            var start = c + u * (inLen / 2 + t);
            SolidSweep.Add(geo, SolidSweep.Tube(Vec3.At(start - u * 0.05, bottom + r + 0.05), Vec3.At(start + u * 1.5, bottom + r), r, 14), MarkingColor.Concreto, "TUBO");
        }
    }

    /// <summary>Grelha: caixilho (moldura) e barras na orientação escolhida.</summary>
    private static void Grate(MarkingGeometry geo, DrainageDefinition d, Vec2 c, Vec2 u, double len, double wid, double top)
    {
        var color = GrateColor(d);
        var v = u.PerpLeft;
        var f = Math.Clamp(d.FrameWidth, 0.02, 0.15);
        var z0 = top - 0.06;
        var z1 = top + 0.003;
        // Caixilho.
        foreach (var s in new[] { -1, 1 })
        {
            SolidSweep.Add(geo, SolidSweep.Box(c + u * (s * (len / 2 - f / 2)), u, f, wid, z0, z1), color, "GRELHA");
            SolidSweep.Add(geo, SolidSweep.Box(c + v * (s * (wid / 2 - f / 2)), u, len - 2 * f, f, z0, z1), color, "GRELHA");
        }
        // Fundo escuro (vazio da grelha) visto entre as barras.
        SolidSweep.Add(geo, SolidSweep.Box(c, u, len - 2 * f, wid - 2 * f, z0 - 0.02, z0 - 0.01), MarkingColor.Preta, "GRELHA");
        var pitch = Math.Max(0.02, d.BarWidth + d.BarGap);
        var innerL = len - 2 * f;
        var innerW = wid - 2 * f;
        switch (d.Bars)
        {
            case OrientacaoBarras.Longitudinal:
                for (var y = -innerW / 2 + pitch / 2; y < innerW / 2; y += pitch)
                    SolidSweep.Add(geo, SolidSweep.Box(c + v * y, u, innerL, d.BarWidth, z0, z1 - 0.001), color, "GRELHA");
                break;
            case OrientacaoBarras.Diagonal:
            {
                var dir = (u + v).Normalized();
                for (var x = -innerL / 2 + pitch; x < innerL / 2 - pitch / 2; x += pitch * 1.414)
                {
                    var bl = Math.Min(innerW * 1.3, (innerL / 2 - Math.Abs(x)) * 2.6 + 0.05);
                    SolidSweep.Add(geo, SolidSweep.Box(c + u * x, dir, bl, d.BarWidth, z0, z1 - 0.001), color, "GRELHA");
                }
                break;
            }
            default:
                for (var x = -innerL / 2 + pitch / 2; x < innerL / 2; x += pitch)
                    SolidSweep.Add(geo, SolidSweep.Box(c + u * x, u, d.BarWidth, innerW, z0, z1 - 0.001), color, "GRELHA");
                break;
        }
    }

    /// <summary>Abertura na guia: vão escuro e guia-chapéu (viga de concreto sobre a boca).</summary>
    private static void Opening(MarkingGeometry geo, DrainageDefinition d, Vec2 c, Vec2 u, Vec2 v, double len)
    {
        var h = Math.Max(0.08, d.OpeningHeight);
        // Vão (faixa escura na face do meio-fio, do fundo do rebaixo até a boca).
        SolidSweep.Add(geo, SolidSweep.Box(c + v * 0.01, u, len, 0.02, -d.Depression, Math.Min(h, d.CurbHeight) - 0.02), MarkingColor.Preta, "BOCA");
        // Guia-chapéu sobre a boca, um pouco mais longa que ela.
        SolidSweep.Add(geo, SolidSweep.Box(c + v * (d.CurbWidth / 2), u, len + 0.30, d.CurbWidth + 0.02, d.CurbHeight - 0.06, d.CurbHeight + 0.01), MarkingColor.Concreto, "BOCA");
    }

    /// <summary>Rebaixo da sarjeta: trecho rebaixado junto à boca e rampas de transição.</summary>
    private static void Depression(MarkingGeometry geo, DrainageDefinition d, Vec2 u, Vec2 v, double total)
    {
        var dep = d.Depression;
        var w = Math.Max(0.3, d.GutterWidth);
        var tl = Math.Max(0.2, d.DepressionLength);
        var o = d.Position;
        Vec3 P(double x, double y, double z) => Vec3.At(o + u * x + v * y, z);
        var t = 0.10;
        // Trecho rebaixado (placa de concreto com topo no fundo do rebaixo).
        SolidSweep.Add(geo, SolidSweep.Prism(
            new List<Vec3> { P(-total / 2, -w, -dep - t), P(total / 2, -w, -dep - t), P(total / 2, 0, -dep - t), P(-total / 2, 0, -dep - t) },
            new List<Vec3> { P(-total / 2, -w, -dep), P(total / 2, -w, -dep), P(total / 2, 0, -dep), P(-total / 2, 0, -dep) }), MarkingColor.PavimentoConcreto, "REBAIXO");
        // Rampas de transição (cunhas) antes e depois.
        foreach (var s in new[] { -1, 1 })
        {
            var x0 = s * total / 2;
            var x1 = s * (total / 2 + tl);
            var bottom = new List<Vec3> { P(x0, -w, -dep - t), P(x1, -w, -t), P(x1, 0, -t), P(x0, 0, -dep - t) };
            var top = new List<Vec3> { P(x0, -w, -dep), P(x1, -w, 0.002), P(x1, 0, 0.002), P(x0, 0, -dep) };
            SolidSweep.Add(geo, SolidSweep.Prism(bottom, top), MarkingColor.PavimentoConcreto, "REBAIXO");
        }
    }

    /// <summary>Poço de visita: câmara circular, cone excêntrico, chaminé e tampão Ø 0,60.</summary>
    private static void Manhole(MarkingGeometry geo, DrainageDefinition d, Vec2 c)
    {
        var r = Math.Max(0.4, d.BoxLength / 2);
        var t = Math.Max(0.1, d.WallThickness);
        var depth = Math.Max(1.0, d.BoxDepth);
        var chamberTop = -Math.Min(0.9, depth * 0.35);
        SolidSweep.Add(geo, SolidSweep.Cylinder(c, r + t, -depth - 0.2, -depth, 24), MarkingColor.Concreto, "PV");
        // Anel da câmara (paredes em gomos convexos).
        Ring(geo, c, r, r + t, -depth, chamberTop - 0.5);
        // Cone de redução até a chaminé.
        var rc = d.LidDiameter / 2 + 0.05;
        RingCone(geo, c, r, r + t, rc, rc + t, chamberTop - 0.5, chamberTop);
        Ring(geo, c, rc, rc + t, chamberTop, -0.08);
        SolidSweep.Add(geo, SolidSweep.Cylinder(c, rc + t + 0.08, -0.08, 0, 24), MarkingColor.Metal, "TAMPA");
        SolidSweep.Add(geo, SolidSweep.Cylinder(c, d.LidDiameter / 2, 0, 0.01, 24), MarkingColor.Metal, "TAMPA");
    }

    private static void Ring(MarkingGeometry geo, Vec2 c, double ri, double ro, double z0, double z1, int n = 16) =>
        RingCone(geo, c, ri, ro, ri, ro, z0, z1, n);

    private static void RingCone(MarkingGeometry geo, Vec2 c, double ri0, double ro0, double ri1, double ro1, double z0, double z1, int n = 16)
    {
        for (int i = 0; i < n; i++)
        {
            var a0 = 2 * Math.PI * i / n;
            var a1 = 2 * Math.PI * (i + 1) / n;
            Vec3 P(double r, double a, double z) => Vec3.At(c + Vec2.FromAngle(a) * r, z);
            var bottom = new List<Vec3> { P(ri0, a0, z0), P(ro0, a0, z0), P(ro0, a1, z0), P(ri0, a1, z0) };
            var top = new List<Vec3> { P(ri1, a0, z1), P(ro1, a0, z1), P(ro1, a1, z1), P(ri1, a1, z1) };
            SolidSweep.Add(geo, SolidSweep.Prism(bottom, top), MarkingColor.Concreto, "PV");
        }
    }

    /// <summary>Canaleta com grelha contínua: paredes, fundo e barras ao longo da linha.</summary>
    private static void Channel(MarkingGeometry geo, DrainageDefinition d, Polyline2 path)
    {
        var w = Math.Max(0.1, d.GrateWidth);
        var t = Math.Max(0.06, d.WallThickness);
        var depth = Math.Max(0.15, d.BoxDepth);
        var flat = VerticalProfile.Flat(0);
        SolidSweep.Along(geo, path, _ => SolidSweep.Rect(-w / 2 - t, w / 2 + t, -depth - t, -depth), MarkingColor.Concreto, 0, double.NaN, 6, "CANALETA");
        foreach (var s in new[] { -1, 1 })
            SolidSweep.Along(geo, path, _ => SolidSweep.Rect(s < 0 ? -w / 2 - t : w / 2, s < 0 ? -w / 2 : w / 2 + t, -depth, -0.03), MarkingColor.Concreto, 0, double.NaN, 6, "CANALETA");
        var color = GrateColor(d);
        foreach (var s in new[] { -1, 1 })
            SolidSweep.Along(geo, path, _ => SolidSweep.Rect(s < 0 ? -w / 2 - 0.02 : w / 2 - 0.02, s < 0 ? -w / 2 + 0.02 : w / 2 + 0.02, -0.04, 0.002), color, 0, double.NaN, 6, "GRELHA");
        SolidSweep.Along(geo, path, _ => SolidSweep.Rect(-w / 2 + 0.02, w / 2 - 0.02, -0.06, -0.05), MarkingColor.Preta, 0, double.NaN, 6, "GRELHA");
        // Barras transversais; em canaletas longas as barras são agrupadas em módulos de 0,50 m (limite de peças).
        var pitch = Math.Max(0.03, d.BarWidth + d.BarGap);
        if (path.Length / pitch > 800) pitch = 0.5;
        for (var s = pitch / 2; s < path.Length; s += pitch)
            SolidSweep.Add(geo, SolidSweep.Box(path.PointAt(s), path.TangentAt(s), Math.Min(d.BarWidth, pitch * 0.8), w - 0.04, -0.04, 0.0015), color, "GRELHA");
        if (pitch >= 0.5)
            foreach (var y in new[] { -w / 4, 0, w / 4 })
                SolidSweep.Along(geo, path, _ => SolidSweep.Rect(y - 0.01, y + 0.01, -0.04, 0.0015), color, 0, double.NaN, 6, "GRELHA");
        _ = flat;
    }
}
