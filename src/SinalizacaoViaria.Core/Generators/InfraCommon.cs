using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Peças comuns às obras de infraestrutura (pistas com greide, aterros, barreiras, guarda-corpos, postes, faixas).</summary>
public static class Infra
{
    /// <summary>Espessura do revestimento asfáltico sobre tabuleiros e aterros (m).</summary>
    public const double Wearing = 0.08;

    /// <summary>Pavimento (revestimento) da pista com greide, de <paramref name="left"/> a <paramref name="right"/> (afastamentos laterais).</summary>
    public static void Pavement(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double right, double left, double s0, double s1,
        MarkingColor color = MarkingColor.Asfalto, double thickness = Wearing, string? layer = "PISTA") =>
        SolidSweep.Along(geo, path, s => SolidSweep.Rect(right, left, prof.Z(s) - thickness, prof.Z(s)), color, s0, s1, 4, layer);

    /// <summary>
    /// Aterro sob a pista (corpo de terra com taludes H:V) entre o terreno e a base do pavimento; onde o greide está abaixo
    /// do terreno não há aterro (é corte – tratado na terraplenagem).
    /// </summary>
    public static void Embankment(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double right, double left, double s0, double s1,
        double slope, Func<Vec2, double> ground, double below = Wearing + 0.35)
    {
        SolidSweep.Along(geo, path, s =>
        {
            var p = path.PointAt(s);
            var n = SolidSweep.Normal(path, s);
            var top = prof.Z(s) - below;
            var g = Math.Min(ground(p + n * right), ground(p + n * left));
            var h = top - g;
            if (h < 0.05) return null;
            return SolidSweep.Trapezoid(right - slope * h, left + slope * h, g, right, left, top);
        }, MarkingColor.Terra, s0, s1, 6, "ATERRO");
    }

    /// <summary>Base/sub-base granular da pista (camada sob o revestimento).</summary>
    public static void Base(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double right, double left, double s0, double s1, double thickness = 0.35) =>
        SolidSweep.Along(geo, path, s => SolidSweep.Rect(right - 0.3, left + 0.3, prof.Z(s) - Wearing - thickness, prof.Z(s) - Wearing), MarkingColor.Brita, s0, s1, 6, "BASE");

    /// <summary>Barreira New Jersey de face simples (0,81 m) com a face para o lado indicado (+1 = face para +lateral).</summary>
    public static void NewJersey(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double lateral, int faceSign, double s0, double s1, double zBase = 0)
    {
        // Seção em duas partes convexas (a face em dois ângulos – 55° e 84° – não é convexa).
        var lower = new[] { (0.0, 0.0), (0.46, 0.0), (0.46, 0.075), (0.28, 0.33), (0.0, 0.33) };
        var upper = new[] { (0.0, 0.33), (0.28, 0.33), (0.23, 0.81), (0.0, 0.81) };
        foreach (var part in new[] { lower, upper })
            SolidSweep.Along(geo, path, s =>
            {
                var z = prof.Z(s) + zBase;
                var pts = part.Select(q => new SectionPt(lateral + faceSign * q.Item1, z + q.Item2)).ToList();
                if (faceSign < 0) pts.Reverse();
                return pts;
            }, MarkingColor.Concreto, s0, s1, 6, "BARREIRA");
    }

    /// <summary>Guarda-corpo metálico: montantes, corrimão e travessa intermediária.</summary>
    public static void Railing(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double lateral, double s0, double s1, double zBase = 0,
        double height = 1.10, double postSpacing = 2.0)
    {
        SolidSweep.Along(geo, path, s => SolidSweep.Rect(lateral - 0.03, lateral + 0.03, prof.Z(s) + zBase + height - 0.06, prof.Z(s) + zBase + height), MarkingColor.Metal, s0, s1, 6, "GUARDA-CORPO");
        SolidSweep.Along(geo, path, s => SolidSweep.Rect(lateral - 0.02, lateral + 0.02, prof.Z(s) + zBase + height * 0.5 - 0.02, prof.Z(s) + zBase + height * 0.5 + 0.02), MarkingColor.Metal, s0, s1, 6, "GUARDA-CORPO");
        for (var s = s0; s <= s1 + 1e-6; s += postSpacing)
        {
            var p = path.PointAt(s) + SolidSweep.Normal(path, s) * lateral;
            var z = prof.Z(s) + zBase;
            SolidSweep.Add(geo, SolidSweep.Box(p, path.TangentAt(s), 0.06, 0.06, z, z + height), MarkingColor.Metal, "GUARDA-CORPO");
        }
    }

    /// <summary>Postes de iluminação com braço e luminária, alternados ou de um lado.</summary>
    public static int Lights(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double lateral, double s0, double s1, double spacing,
        bool bothSides = false, double height = 10, double zBase = 0)
    {
        var count = 0;
        var k = 0;
        for (var s = s0 + spacing / 2; s <= s1; s += spacing, k++)
        {
            var sides = bothSides ? new[] { 1.0, -1.0 } : new[] { k % 2 == 0 ? 1.0 : -1.0 };
            foreach (var side in sides)
            {
                var n = SolidSweep.Normal(path, s);
                var basePt = path.PointAt(s) + n * (side * lateral);
                var z = prof.Z(s) + zBase;
                SolidSweep.Add(geo, SolidSweep.Frustum(basePt, 0.11, 0.07, z, z + height, 10), MarkingColor.Metal, "ILUMINACAO", isUnit: true);
                var tip = basePt - n * (side * 2.2);
                SolidSweep.Add(geo, SolidSweep.Rod(Vec3.At(basePt, z + height - 0.1), Vec3.At(tip, z + height + 0.25), 0.07), MarkingColor.Metal, "ILUMINACAO");
                SolidSweep.Add(geo, SolidSweep.Box(tip, n, 0.65, 0.28, z + height + 0.05, z + height + 0.2), MarkingColor.Metal, "ILUMINACAO");
                count++;
            }
        }
        return count;
    }

    /// <summary>Linhas de bordo (brancas contínuas), divisão de faixas (tracejadas) e eixo amarelo sobre a pista com greide.</summary>
    public static void LaneMarkings(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double right, double left, int lanes, double s0, double s1,
        bool twoWay = true)
    {
        if (lanes <= 0) return;
        SolidSweep.PaintStrip(geo, path, prof, right + 0.20, 0.15, MarkingColor.Branca, s0, s1);
        SolidSweep.PaintStrip(geo, path, prof, left - 0.20, 0.15, MarkingColor.Branca, s0, s1);
        var w = (left - right - 0.4) / lanes;
        for (int i = 1; i < lanes; i++)
        {
            var y = right + 0.2 + w * i;
            var center = twoWay && lanes % 2 == 0 && i == lanes / 2;
            if (center) SolidSweep.PaintStrip(geo, path, prof, y, 0.12, MarkingColor.Amarela, s0, s1, 4, 8);
            else SolidSweep.PaintStrip(geo, path, prof, y, 0.12, MarkingColor.Branca, s0, s1, 4, 8);
        }
    }

    /// <summary>Faixa de terraplenagem ao longo do eixo: bordas na cota de projeto (greide menos a estrutura do pavimento).</summary>
    public static GradeCorridor Corridor(Polyline2 path, VerticalProfile prof, double right, double left, double s0, double s1, double depthBelow,
        double cut, double fill, bool daylightRight = true, bool daylightLeft = true, double step = 5, string label = "")
    {
        var c = new GradeCorridor { CutSlope = cut, FillSlope = fill, DaylightLeft = daylightLeft, DaylightRight = daylightRight, Label = label };
        foreach (var s in SolidSweep.Stations(path, s0, s1, step))
        {
            var p = path.PointAt(s);
            var n = SolidSweep.Normal(path, s);
            var z = prof.Z(s) - depthBelow;
            c.Left.Add(Vec3.At(p + n * left, z));
            c.Right.Add(Vec3.At(p + n * right, z));
        }
        return c;
    }

    /// <summary>
    /// Terreno natural de referência ao longo do eixo, "congelado" na definição: lido do terreno na primeira geração (ou quando
    /// o eixo muda de comprimento) e reaproveitado depois – a terraplenagem não realimenta o projeto.
    /// </summary>
    /// <param name="sampleAt">Ponto (em planta) a amostrar em cada estação – ex.: fora dos muros de uma trincheira.</param>
    public static VerticalProfile GroundProfile(ITerrainAware def, Polyline2 path, BuildContext ctx, Func<double, Vec2>? sampleAt = null, double step = 5)
    {
        var L = path.Length;
        var stored = def.GroundLine;
        if (!def.FollowTerrain) return VerticalProfile.Flat(0);
        if (stored is { Count: >= 2 } && Math.Abs(stored[^1].X - L) < 0.5)
            return FromPoints(stored);
        // Eixo mudou e não há terreno para reler: cota média guardada (nunca extrapolar a linha antiga).
        if (ctx.Ground == null) return stored is { Count: >= 1 } ? VerticalProfile.Flat(stored.Average(v => v.Y)) : VerticalProfile.Flat(0);
        var pts = new List<Vec2>();
        var n = Math.Max(1, (int)Math.Ceiling(L / Math.Max(0.5, step)));
        for (int i = 0; i <= n; i++)
        {
            var s = L * i / n;
            var z = ctx.Ground(sampleAt?.Invoke(s) ?? path.PointAt(s));
            if (z != null) pts.Add(new Vec2(s, z.Value));
        }
        if (pts.Count < 2) return VerticalProfile.Flat(0);
        if (pts[0].X > 1e-6) pts.Insert(0, new Vec2(0, pts[0].Y));
        if (pts[^1].X < L - 1e-6) pts.Add(new Vec2(L, pts[^1].Y));
        def.GroundLine = pts;
        return FromPoints(pts);
    }

    private static VerticalProfile FromPoints(IEnumerable<Vec2> pts)
    {
        var p = new VerticalProfile();
        foreach (var v in pts) p.Pvis.Add((v.X, v.Y));
        return p;
    }

    /// <summary>Junta de dilatação (faixa escura) atravessando a pista.</summary>
    public static void Joint(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double s, double right, double left)
    {
        var p = path.PointAt(s);
        var n = SolidSweep.Normal(path, s);
        var c = p + n * ((left + right) / 2);
        var z = prof.Z(s);
        SolidSweep.Add(geo, SolidSweep.Box(c, path.TangentAt(s), 0.10, left - right, z - 0.02, z + 0.005), MarkingColor.Metal, "JUNTA");
    }
}
