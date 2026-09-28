using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Superfície de rolamento com greide e caimento transversal: cota em cada estação e afastamento lateral (abaulamento de
/// duas águas a partir do eixo, ou caimento único / superelevação para um lado).
/// </summary>
public sealed class DeckSurface
{
    public required VerticalProfile Profile { get; init; }
    /// <summary>Caimento transversal (m/m) – 2 % usual em pistas pavimentadas (DNIT).</summary>
    public double CrossSlope { get; init; }
    /// <summary>Duas águas a partir do eixo; falso = caimento único descendo para a direita (afastamentos negativos).</summary>
    public bool Crown { get; init; } = true;

    /// <summary>Superfície dada por fora (greide da via hospedeira: estação, afastamento → cota).</summary>
    public Func<double, double, double>? Custom { get; init; }

    public double Z(double s, double y) => Custom != null ? Custom(s, y) : Profile.Z(s) - CrossSlope * (Crown ? Math.Abs(y) : -y);

    public static implicit operator DeckSurface(VerticalProfile p) => new() { Profile = p };
}

/// <summary>
/// Peças comuns às obras de infraestrutura (pistas com greide e caimento, aterros, barreiras New Jersey, guarda-corpos,
/// postes de iluminação, faixas pintadas, juntas e buzinotes).
/// </summary>
public static class Infra
{
    /// <summary>Espessura do revestimento asfáltico sobre tabuleiros e aterros (m).</summary>
    public const double Wearing = 0.08;

    /// <summary>Largura da base da barreira New Jersey (m).</summary>
    public const double JerseyBase = 0.40;

    /// <summary>
    /// Faixa da seção entre os afastamentos <paramref name="y0"/> e <paramref name="y1"/> com o topo em <paramref name="top"/>(y) e
    /// espessura constante – com vértice no eixo quando a faixa o atravessa (abaulamento).
    /// </summary>
    public static SectionPt[] Band(double y0, double y1, Func<double, double> top, double thickness)
    {
        var (a, b) = (Math.Min(y0, y1), Math.Max(y0, y1));
        var ys = new List<double> { a };
        if (a < -1e-6 && b > 1e-6) ys.Add(0);
        ys.Add(b);
        var pts = ys.Select(y => new SectionPt(y, top(y) - thickness)).ToList();
        for (int i = ys.Count - 1; i >= 0; i--) pts.Add(new SectionPt(ys[i], top(ys[i])));
        return pts.ToArray();
    }

    /// <summary>Pavimento (revestimento) da pista com greide e caimento, de <paramref name="right"/> a <paramref name="left"/> (afastamentos).</summary>
    public static void Pavement(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double right, double left, double s0, double s1,
        MarkingColor color = MarkingColor.Asfalto, double thickness = Wearing, string? layer = "PISTA") =>
        SolidSweep.Along(geo, path, s => Band(right, left, y => surf.Z(s, y), thickness), color, s0, s1, 4, layer);

    /// <summary>
    /// Aterro sob a pista (corpo de terra com taludes H:V) entre o terreno e a base do pavimento. Com o terreno nativo
    /// (Toposolid) o aterro não vira sólido: é a terraplenagem que o constrói no terreno.
    /// </summary>
    public static void Embankment(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double right, double left, double s0, double s1,
        double slope, Func<Vec2, double> ground, double below = Wearing + 0.35, bool native = false)
    {
        if (native) return;
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
    public static void Base(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double right, double left, double s0, double s1, double thickness = 0.35) =>
        SolidSweep.Along(geo, path, s => Band(right - 0.3, left + 0.3, y => surf.Z(s, y) - Wearing, thickness), MarkingColor.Brita, s0, s1, 6, "BASE");

    /// <summary>
    /// Perfil New Jersey (DNIT/DER): face com degrau de 7,5 cm, trecho a 55° até 0,33 m e a 84° até o topo (0,81 m),
    /// topo boleado. q = distância da face de trás (0) para a face de tráfego (0,40 m na base).
    /// </summary>
    public static readonly (double Q, double Z)[] JerseyProfile =
    {
        (0, 0), (JerseyBase, 0), (JerseyBase, 0.075), (0.225, 0.33), (0.172, 0.785), (0.15, 0.81), (0.025, 0.81), (0, 0.785),
    };

    /// <summary>Perfil New Jersey de duas faces (canteiro central), largura de base 0,61 m.</summary>
    public static readonly (double Q, double Z)[] JerseyDouble =
    {
        (-0.305, 0), (0.305, 0), (0.305, 0.075), (0.13, 0.33), (0.077, 0.785), (0.055, 0.81), (-0.055, 0.81), (-0.077, 0.785),
        (-0.13, 0.33), (-0.305, 0.075),
    };

    /// <summary>Barreira New Jersey de face simples com a face para o lado indicado (+1 = face para +lateral).</summary>
    public static void NewJersey(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double lateral, int faceSign, double s0, double s1, double zBase = 0)
    {
        var face = lateral + faceSign * JerseyBase;
        SolidSweep.Along(geo, path, s =>
        {
            var z = surf.Z(s, face) + zBase;
            return JerseyProfile.Select(q => new SectionPt(lateral + faceSign * q.Q, z + q.Z)).ToArray();
        }, MarkingColor.Concreto, s0, s1, 6, "BARREIRA");
    }

    /// <summary>Barreira New Jersey dupla (canteiro central) centrada em <paramref name="lateral"/>.</summary>
    public static void NewJerseyDouble(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double lateral, double s0, double s1, double zBase = 0) =>
        SolidSweep.Along(geo, path, s =>
        {
            var z = surf.Z(s, lateral) + zBase;
            return JerseyDouble.Select(q => new SectionPt(lateral + q.Q, z + q.Z)).ToArray();
        }, MarkingColor.Concreto, s0, s1, 6, "BARREIRA");

    /// <summary>
    /// Guarda-corpo (NBR 14718 / NBR 9050: 1,10 m) na lateral indicada: montantes a cada <paramref name="postSpacing"/> m e
    /// corrimão tubular; preenchimento tubular (três tubos), em balaústres (vão ≤ 11 cm) ou em painéis de vidro.
    /// </summary>
    public static void Railing(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double lateral, double s0, double s1, double zBase = 0,
        double height = 1.10, double postSpacing = 2.0, TipoGuardaCorpo style = TipoGuardaCorpo.Tubular)
    {
        double Z(double s) => surf.Z(s, lateral) + zBase;
        // Corrimão (tubo Ø 60 mm) e travessa inferior.
        SolidSweep.Along(geo, path, s => SolidSweep.Circle(lateral, Z(s) + height - 0.03, 0.03, 16), MarkingColor.Metal, s0, s1, 4, "GUARDA-CORPO");
        SolidSweep.Along(geo, path, s => SolidSweep.Rect(lateral - 0.02, lateral + 0.02, Z(s) + 0.10, Z(s) + 0.14), MarkingColor.Metal, s0, s1, 4, "GUARDA-CORPO");
        switch (style)
        {
            case TipoGuardaCorpo.Tubular:
                foreach (var h in new[] { 0.40, 0.70 })
                    SolidSweep.Along(geo, path, s => SolidSweep.Circle(lateral, Z(s) + h, 0.021, 12), MarkingColor.Metal, s0, s1, 4, "GUARDA-CORPO");
                break;
            case TipoGuardaCorpo.Vidro:
                SolidSweep.Along(geo, path, s => SolidSweep.Rect(lateral - 0.006, lateral + 0.006, Z(s) + 0.16, Z(s) + height - 0.08), MarkingColor.Vidro, s0, s1, 4, "GUARDA-CORPO");
                break;
            case TipoGuardaCorpo.Balaustres:
            {
                var pitch = 0.13;
                if ((s1 - s0) / pitch > 2500) pitch = (s1 - s0) / 2500;
                for (var s = s0 + pitch / 2; s < s1; s += pitch)
                {
                    var p = path.PointAt(s) + SolidSweep.Normal(path, s) * lateral;
                    SolidSweep.Add(geo, SolidSweep.Box(p, path.TangentAt(s), 0.02, 0.02, Z(s) + 0.14, Z(s) + height - 0.06), MarkingColor.Metal, "GUARDA-CORPO");
                }
                break;
            }
        }
        // Montantes (chapa 12 cm × 1 cm com base) – arredondados nas pontas.
        var n = Math.Max(1, (int)Math.Round((s1 - s0) / Math.Max(0.8, postSpacing)));
        for (int i = 0; i <= n; i++)
        {
            var s = s0 + (s1 - s0) * i / n;
            var p = path.PointAt(s) + SolidSweep.Normal(path, s) * lateral;
            var t = path.TangentAt(s);
            SolidSweep.Add(geo, SolidSweep.Box(p, t, 0.012, 0.10, Z(s), Z(s) + height - 0.03), MarkingColor.Metal, "GUARDA-CORPO");
            SolidSweep.Add(geo, SolidSweep.Box(p, t, 0.16, 0.16, Z(s), Z(s) + 0.012), MarkingColor.Metal, "GUARDA-CORPO");
        }
    }

    /// <summary>
    /// Postes de iluminação (coluna cônica sobre placa de base, braço curvo e luminária LED), alternados ou dos dois lados.
    /// </summary>
    public static int Lights(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double lateral, double s0, double s1, double spacing,
        bool bothSides = false, double height = 10, double zBase = 0, bool inward = true)
    {
        var count = 0;
        var k = 0;
        for (var s = s0 + spacing / 2; s <= s1; s += spacing, k++)
        {
            var sides = bothSides ? new[] { 1.0, -1.0 } : new[] { k % 2 == 0 ? 1.0 : -1.0 };
            foreach (var side in sides)
            {
                var n = SolidSweep.Normal(path, s);
                var y = side * lateral;
                var basePt = path.PointAt(s) + n * y;
                var z = surf.Z(s, y) + zBase;
                Pole(geo, basePt, z, height, inward ? -n * side : n * side, path.TangentAt(s));
                count++;
            }
        }
        return count;
    }

    /// <summary>Um poste: placa de base, coluna cônica, braço curvo de 2,2 m e luminária LED achatada.</summary>
    public static void Pole(MarkingGeometry geo, Vec2 basePt, double z, double height, Vec2 arm, Vec2 along, double armLength = 2.2, bool column = true)
    {
        arm = arm.Normalized();
        if (column)
        {
            SolidSweep.Add(geo, SolidSweep.Box(basePt, along, 0.40, 0.40, z, z + 0.03), MarkingColor.Metal, "ILUMINACAO");
            SolidSweep.AddRound(geo, Vec3.At(basePt, z + 0.03), Vec3.At(basePt, z + height - 0.4), 0.10, 0.06, MarkingColor.Metal, "ILUMINACAO", true, n: 20);
        }
        // Braço em arco (4 trechos) subindo 0,6 m.
        var prev = Vec3.At(basePt, z + height - 0.45);
        for (int i = 1; i <= 4; i++)
        {
            var a = Math.PI / 2 * i / 4;
            var q = Vec3.At(basePt + arm * (armLength * (1 - Math.Cos(a))), z + height - 0.45 + 0.6 * Math.Sin(a));
            SolidSweep.AddRound(geo, prev, q, 0.045, 0.04, MarkingColor.Metal, "ILUMINACAO", n: 12);
            prev = q;
        }
        var head = basePt + arm * (armLength + 0.25);
        SolidSweep.Add(geo, SolidSweep.Box(head, arm, 0.75, 0.32, z + height + 0.05, z + height + 0.17), MarkingColor.Metal, "ILUMINACAO");
        SolidSweep.Add(geo, SolidSweep.Box(head, arm, 0.62, 0.24, z + height + 0.03, z + height + 0.05), MarkingColor.Vidro, "ILUMINACAO");
    }

    /// <summary>Linhas de bordo (brancas contínuas), divisão de faixas (tracejadas) e eixo amarelo sobre a pista com greide.</summary>
    public static void LaneMarkings(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double right, double left, int lanes, double s0, double s1,
        bool twoWay = true, double edgeInset = 0.20)
    {
        if (lanes <= 0) return;
        Paint(geo, path, surf, right + edgeInset, 0.15, MarkingColor.Branca, s0, s1);
        Paint(geo, path, surf, left - edgeInset, 0.15, MarkingColor.Branca, s0, s1);
        var w = (left - right - 2 * edgeInset) / lanes;
        for (int i = 1; i < lanes; i++)
        {
            var y = right + edgeInset + w * i;
            var center = twoWay && lanes % 2 == 0 && i == lanes / 2;
            if (center)
            {
                // Eixo: linha dupla contínua amarela.
                Paint(geo, path, surf, y - 0.11, 0.10, MarkingColor.Amarela, s0, s1);
                Paint(geo, path, surf, y + 0.11, 0.10, MarkingColor.Amarela, s0, s1);
            }
            else Paint(geo, path, surf, y, 0.12, MarkingColor.Branca, s0, s1, 4, 12);
        }
    }

    /// <summary>Faixa pintada (3 mm) sobre a superfície com greide e caimento.</summary>
    public static void Paint(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double lateral, double width, MarkingColor color,
        double s0, double s1, double dash = 0, double gap = 0)
    {
        SectionPt[] Sec(double s) => SolidSweep.Rect(lateral - width / 2, lateral + width / 2, surf.Z(s, lateral) + 0.0005, surf.Z(s, lateral) + 0.003);
        if (dash <= 0 || gap <= 0)
        {
            SolidSweep.Along(geo, path, Sec, color, s0, s1, 6, "PINTURA");
            return;
        }
        for (var s = s0; s < s1; s += dash + gap)
            SolidSweep.Along(geo, path, Sec, color, s, Math.Min(s1, s + dash), 6, "PINTURA");
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

    /// <summary>Junta de dilatação metálica (duas cantoneiras e o selo de neoprene) atravessando a pista.</summary>
    public static void Joint(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double s, double right, double left)
    {
        var p = path.PointAt(s);
        var n = SolidSweep.Normal(path, s);
        var t = path.TangentAt(s);
        // Faixas embutidas no pavimento, rentes à superfície do tabuleiro em cada ponto (acompanham o abaulamento e a
        // superelevação) – uma caixa reta na cota do centro ficava acima do asfalto nas bordas.
        double Z(Vec2 q) => surf.Z(s, (q - p).Dot(n));
        void Strip(double dx, double w, double below, double above, MarkingColor color)
        {
            var ring = new List<Vec2>();
            var k = Math.Max(1, (int)Math.Ceiling((left - right) / 0.5));
            for (int i = 0; i <= k; i++) ring.Add(p + n * (right + (left - right) * i / k) + t * (dx - w / 2));
            for (int i = k; i >= 0; i--) ring.Add(p + n * (right + (left - right) * i / k) + t * (dx + w / 2));
            var poly = Polyhedron.Prism(ring, q => Z(q) - below, q => Z(q) + above);
            geo.Pieces.Add(new MarkingPiece(new Polygon2(ring), color) { Solid = GradeLift.Planarize(poly), Layer = "JUNTA" });
        }
        foreach (var dx in new[] { -0.045, 0.045 }) Strip(dx, 0.03, 0.06, 0.002, MarkingColor.Metal);
        Strip(0, 0.06, 0.05, -0.008, MarkingColor.Preta);
    }

    /// <summary>Buzinotes (drenos Ø 100 mm) atravessando o tabuleiro junto às barreiras, a cada <paramref name="spacing"/> m.</summary>
    public static void Scuppers(MarkingGeometry geo, Polyline2 path, DeckSurface surf, double lateral, double s0, double s1, double spacing, double slabBottom)
    {
        for (var s = s0 + spacing / 2; s < s1 - 1; s += spacing)
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var y = side * lateral;
                var p = path.PointAt(s) + SolidSweep.Normal(path, s) * y;
                var z = surf.Z(s, y);
                SolidSweep.AddRound(geo, Vec3.At(p, z - Wearing + 0.005), Vec3.At(p, z - slabBottom - 0.30), 0.06, 0.06, MarkingColor.Metal, "DRENO", false, 0.05, 0.05, 16);
            }
    }
}
