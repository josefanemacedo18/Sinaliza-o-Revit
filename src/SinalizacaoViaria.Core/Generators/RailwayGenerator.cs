using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Dimensões de um perfil de trilho (m).</summary>
public sealed record RailProfile(string Name, double Height, double Base, double Head, double KgPerM);

/// <summary>Quantidades principais de um trecho de via férrea.</summary>
public sealed record RailwayQuantities(double Length, double RailLength, double RailTonnes, int Sleepers, int Fastenings,
    double BallastM3, double SubBallastM3, double SlabM3, double SurfaceM2, double DitchLength);

/// <summary>
/// Via férrea: plataforma (sublastro), lastro com taludes, dormentes, placas de apoio e trilhos com perfil real (patim, alma e boleto)
/// – ou via em laje e via embutida no pavimento (VLT), com várias linhas e valetas de drenagem.
/// </summary>
/// <remarks>
/// Referências: ABNT NBR 7641 (via permanente – terminologia), NBR 7590 (trilhos), NBR 11709 (dormentes de concreto),
/// NBR 5564 (lastro – brita), DNIT/VALEC (seções-tipo). Nível zero: topo da plataforma (terraplenagem) na via em lastro/laje;
/// topo do pavimento na via embutida.
/// </remarks>
public static class RailwayGenerator
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static RailProfile Profile(PerfilTrilho p) => p switch
    {
        PerfilTrilho.TR45 => new("TR-45", 0.1429, 0.1302, 0.0651, 44.6),
        PerfilTrilho.TR68 => new("TR-68", 0.1857, 0.1524, 0.0746, 67.6),
        PerfilTrilho.UIC60 => new("UIC-60", 0.172, 0.150, 0.072, 60.2),
        PerfilTrilho.Ri60 => new("Ri-60 (canaleta)", 0.180, 0.180, 0.113, 60.6),
        _ => new("TR-57", 0.1683, 0.1397, 0.0690, 56.9),
    };

    public static double GaugeOf(RailwayDefinition d) => d.Gauge switch
    {
        BitolaFerroviaria.Metrica => 1.000,
        BitolaFerroviaria.Padrao => 1.435,
        BitolaFerroviaria.Personalizada => Math.Clamp(d.CustomGauge, 0.5, 2.5),
        _ => 1.600,
    };

    public static string GaugeLabel(RailwayDefinition d) => d.Gauge switch
    {
        BitolaFerroviaria.Metrica => "bitola métrica (1,000 m)",
        BitolaFerroviaria.Padrao => "bitola padrão (1,435 m)",
        BitolaFerroviaria.Mista => "bitola mista (1,000 + 1,600 m)",
        BitolaFerroviaria.Personalizada => $"bitola {GaugeOf(d).ToString("0.000", Pt)} m",
        _ => "bitola larga (1,600 m)",
    };

    public static double SleeperLengthOf(RailwayDefinition d) => d.SleeperLength > 0.5 ? d.SleeperLength : d.Gauge switch
    {
        BitolaFerroviaria.Metrica => 2.00,
        BitolaFerroviaria.Padrao => 2.60,
        BitolaFerroviaria.Personalizada => GaugeOf(d) + 1.20,
        _ => 2.80,
    };

    public static double SleeperWidthOf(RailwayDefinition d) => d.SleeperWidth > 0.05 ? d.SleeperWidth : d.Sleeper switch
    {
        TipoDormente.Madeira => 0.24,
        TipoDormente.Aco => 0.26,
        TipoDormente.ConcretoBibloco => 0.29,
        _ => 0.28,
    };

    public static double SleeperHeightOf(RailwayDefinition d) => d.SleeperHeight > 0.03 ? d.SleeperHeight : d.Sleeper switch
    {
        TipoDormente.Madeira => 0.17,
        TipoDormente.Aco => 0.10,
        TipoDormente.ConcretoBibloco => 0.23,
        _ => 0.22,
    };

    public static MarkingColor SleeperColor(TipoDormente t) => t switch
    {
        TipoDormente.Madeira => MarkingColor.Madeira,
        TipoDormente.Aco => MarkingColor.Metal,
        _ => MarkingColor.Concreto,
    };

    /// <summary>Posições laterais (centro de cada trilho) em relação ao eixo de uma linha.</summary>
    public static List<double> RailCenters(RailwayDefinition d)
    {
        var hw = Profile(d.Rail).Head;
        if (d.Gauge == BitolaFerroviaria.Mista)
        {
            // Trilho comum à direita; o terceiro trilho define a bitola métrica.
            return new List<double> { -(0.8 + hw / 2), 0.8 + hw / 2, -0.8 + 1.0 + hw / 2 };
        }
        var g = GaugeOf(d);
        return new List<double> { -(g / 2 + hw / 2), g / 2 + hw / 2 };
    }

    /// <summary>Deslocamentos laterais dos eixos das linhas (centrados no eixo desenhado + deslocamento).</summary>
    public static List<double> TrackOffsets(RailwayDefinition d)
    {
        var n = Math.Clamp(d.Tracks, 1, 8);
        return Enumerable.Range(0, n).Select(i => d.Offset + (i - (n - 1) / 2.0) * Math.Max(2.5, d.TrackSpacing)).ToList();
    }

    /// <summary>Níveis principais (m): topo do sublastro, base do dormente, topo do dormente, base do trilho e topo do trilho.</summary>
    public static (double SubTop, double SleeperBottom, double SleeperTop, double RailBase, double RailTop) Levels(RailwayDefinition d)
    {
        var rp = Profile(d.Rail);
        var plate = d.Fastenings ? 0.02 : 0;
        switch (d.Type)
        {
            case TipoViaFerrea.Embutida:
            {
                var top = d.RailTopLevel;
                return (top - rp.Height - d.SlabThickness, top - rp.Height, top - rp.Height, top - rp.Height, top);
            }
            case TipoViaFerrea.Laje:
            {
                var sub = Math.Max(0, d.SubBallastDepth);
                var slabTop = sub + Math.Max(0.1, d.SlabThickness);
                var rb = slabTop + plate + 0.03;          // placa + calço elastomérico da fixação direta
                return (sub, slabTop, slabTop, rb, rb + rp.Height);
            }
            default:
            {
                var sub = Math.Max(0, d.SubBallastDepth);
                var sb = sub + Math.Max(0.1, d.BallastDepth);
                var st = sb + SleeperHeightOf(d);
                return (sub, sb, st, st + plate, st + plate + rp.Height);
            }
        }
    }

    public static MarkingGeometry Build(RailwayDefinition d, Polyline2 path)
    {
        var geo = new MarkingGeometry();
        var L = path.Length;
        if (L < 0.5) { geo.Warnings.Add("Eixo da via férrea muito curto."); return geo; }
        var rp = Profile(d.Rail);
        var tracks = TrackOffsets(d);
        var rails = RailCenters(d);
        var (subTop, slBottom, slTop, railBase, railTop) = Levels(d);
        var span = tracks.Max() - tracks.Min();
        var mid = (tracks.Max() + tracks.Min()) / 2;
        var sleeperLen = SleeperLengthOf(d);

        switch (d.Type)
        {
            case TipoViaFerrea.Lastro:
            {
                // Lastro trapezoidal contínuo sob todas as linhas (crista = dormentes + ombros; talude H:V).
                var crestZ = slTop - 0.03;
                var crestHalf = span / 2 + sleeperLen / 2 + Math.Max(0, d.BallastShoulder);
                var toeHalf = crestHalf + Math.Max(0, d.BallastSlope) * (crestZ - subTop);
                SweepTrapezoid(geo, path, mid, toeHalf, crestHalf, subTop, crestZ, MarkingColor.Brita);
                var subHalf = toeHalf + Math.Max(0, d.SubBallastExtra);
                if (subTop > 0.01)
                    SweepTrapezoid(geo, path, mid, subHalf + 1.5 * subTop, subHalf, 0, subTop, MarkingColor.Terra);
                if (d.Ditches) Ditches(geo, path, mid, subHalf + 1.5 * subTop + 0.30, d);
                Sleepers(geo, path, d, tracks, rails, slBottom, slTop);
                break;
            }
            case TipoViaFerrea.Laje:
            {
                var slabHalf = span / 2 + (d.SlabWidth > 0.5 ? d.SlabWidth : Math.Max(2.4, GaugeOf(d) + 1.4)) / 2;
                if (subTop > 0.01)
                    SweepTrapezoid(geo, path, mid, slabHalf + 0.5 + 1.5 * subTop, slabHalf + 0.5, 0, subTop, MarkingColor.Terra);
                var slabStart = geo.Pieces.Count;
                geo.AddRange(PolygonOps.Strip(path.Offset(mid).Points, 2 * slabHalf), MarkingColor.Concreto, 0);
                Lift(geo, subTop, slBottom - subTop, slabStart);
                if (d.Ditches) Ditches(geo, path, mid, slabHalf + 0.5 + 1.5 * subTop + 0.30, d);
                // Fixação direta: calço + placa a cada espaçamento.
                if (d.Fastenings) Plates(geo, path, d, tracks, rails, slTop, 0.05, MarkingColor.Metal, 0.30, 0.18);
                break;
            }
            case TipoViaFerrea.Embutida:
            {
                var laneW = d.SlabWidth > 0.5 ? d.SlabWidth : GaugeOf(d) + 1.40;
                var half = span / 2 + laneW / 2;
                var baseStrip = PolygonOps.Strip(path.Offset(mid).Points, 2 * half);
                var start = geo.Pieces.Count;
                geo.AddRange(baseStrip, MarkingColor.PavimentoConcreto, 0);
                Lift(geo, subTop, slBottom - subTop, start);
                // Revestimento entre e ao lado dos trilhos, rente ao topo do trilho, com as canaletas livres.
                var clear = new List<Polygon2>();
                foreach (var t in tracks)
                    foreach (var r in rails)
                        clear.AddRange(PolygonOps.Strip(path.Offset(t + r).Points, rp.Base + 0.04));
                var surface = PolygonOps.Difference(baseStrip, clear);
                var sc = d.Surface switch
                {
                    SuperficieViaEmbutida.Asfalto => MarkingColor.Asfalto,
                    SuperficieViaEmbutida.Grama => MarkingColor.Grama,
                    SuperficieViaEmbutida.Bloquete => MarkingColor.Bloquete,
                    _ => MarkingColor.PavimentoConcreto,
                };
                start = geo.Pieces.Count;
                geo.AddRange(surface, sc, 0);
                Lift(geo, slBottom, railTop - slBottom - (sc == MarkingColor.Grama ? 0.03 : 0.0), start);
                break;
            }
        }

        // Trilhos: patim, alma e boleto (perfil real) contínuos ao longo de cada fila.
        foreach (var t in tracks)
            foreach (var r in rails)
            {
                var line = path.Offset(t + r).Points;
                var baseT = 0.012;
                var headH = 0.045;
                AddStrip(geo, line, rp.Base, railBase, baseT, MarkingColor.Metal);
                AddStrip(geo, line, 0.017, railBase + baseT, rp.Height - baseT - headH, MarkingColor.Metal);
                AddStrip(geo, line, rp.Head, railTop - headH, headH, MarkingColor.Metal);
            }

        var q = Quantities(d, L);
        geo.PathLength = L;
        geo.PaintedLength = L;
        geo.UnitCount = q.Sleepers;
        if (d.Tracks > 1 && d.TrackSpacing < 3.8)
            geo.Warnings.Add($"Entrevia de {d.TrackSpacing.ToString("0.00", Pt)} m – usual ≥ 4,00 m (carga) / 3,50 m (metrô/VLT); confira o gabarito.");
        if (d.Type == TipoViaFerrea.Lastro && d.BallastDepth < 0.25)
            geo.Warnings.Add("Lastro com menos de 0,25 m sob o dormente – abaixo do usual para vias de carga (0,30 m).");
        if (d.Type == TipoViaFerrea.Embutida && d.Rail != PerfilTrilho.Ri60)
            geo.Warnings.Add("Via embutida: o usual é trilho de canaleta (Ri-60) para manter o friso livre.");
        return geo;
    }

    /// <summary>Quantidades do trecho (extensão, trilhos, dormentes, fixações, volumes de lastro/sublastro/laje).</summary>
    public static RailwayQuantities Quantities(RailwayDefinition d, double length)
    {
        var rp = Profile(d.Rail);
        var n = TrackOffsets(d).Count;
        var railsPerTrack = RailCenters(d).Count;
        var span = (n - 1) * Math.Max(2.5, d.TrackSpacing);
        var (subTop, slBottom, slTop, _, _) = Levels(d);
        var sleeperLen = SleeperLengthOf(d);
        var sleepersPerTrack = d.Type == TipoViaFerrea.Lastro ? (int)Math.Floor(length / Math.Max(0.3, d.SleeperSpacing)) : 0;
        var supports = d.Type == TipoViaFerrea.Embutida ? 0 : (int)Math.Floor(length / Math.Max(0.3, d.SleeperSpacing));
        double ballast = 0, sub = 0, slab = 0, surface = 0;
        switch (d.Type)
        {
            case TipoViaFerrea.Lastro:
            {
                var crestZ = slTop - 0.03;
                var crestHalf = span / 2 + sleeperLen / 2 + d.BallastShoulder;
                var toeHalf = crestHalf + d.BallastSlope * (crestZ - subTop);
                var area = (crestHalf + toeHalf) * (crestZ - subTop);
                // Desconta o volume dos dormentes imersos.
                area -= n * sleeperLen * SleeperWidthOf(d) * SleeperHeightOf(d) / Math.Max(0.3, d.SleeperSpacing);
                ballast = Math.Max(0, area) * length;
                var subHalf = toeHalf + d.SubBallastExtra;
                sub = (2 * subHalf + 1.5 * subTop) * subTop * length;
                break;
            }
            case TipoViaFerrea.Laje:
            {
                var slabHalf = span / 2 + (d.SlabWidth > 0.5 ? d.SlabWidth : Math.Max(2.4, GaugeOf(d) + 1.4)) / 2;
                slab = 2 * slabHalf * (slBottom - subTop) * length;
                sub = (2 * (slabHalf + 0.5) + 1.5 * subTop) * subTop * length;
                break;
            }
            case TipoViaFerrea.Embutida:
            {
                var laneW = d.SlabWidth > 0.5 ? d.SlabWidth : GaugeOf(d) + 1.40;
                slab = (span + laneW) * d.SlabThickness * length;
                surface = (span + laneW - n * railsPerTrack * (rp.Base + 0.04)) * length;
                break;
            }
        }
        var railLen = length * n * railsPerTrack;
        return new RailwayQuantities(length, railLen, railLen * rp.KgPerM / 1000.0, sleepersPerTrack * n,
            d.Fastenings ? supports * n * railsPerTrack : 0, ballast, sub, slab, surface,
            d.Ditches && d.Type != TipoViaFerrea.Embutida ? 2 * length : 0);
    }

    /// <summary>Memorial resumido (texto) para a janela e o quantitativo.</summary>
    public static string Summary(RailwayDefinition d, double length)
    {
        var q = Quantities(d, length);
        var rp = Profile(d.Rail);
        var parts = new List<string>
        {
            $"{q.Length.ToString("N1", Pt)} m de via ({d.Tracks} linha(s), {GaugeLabel(d)})",
            $"trilho {rp.Name}: {q.RailLength.ToString("N0", Pt)} m ≈ {q.RailTonnes.ToString("N1", Pt)} t",
        };
        if (q.Sleepers > 0) parts.Add($"{q.Sleepers.ToString("N0", Pt)} dormentes");
        if (q.Fastenings > 0) parts.Add($"{q.Fastenings.ToString("N0", Pt)} fixações");
        if (q.BallastM3 > 0) parts.Add($"lastro {q.BallastM3.ToString("N1", Pt)} m³");
        if (q.SubBallastM3 > 0) parts.Add($"sublastro {q.SubBallastM3.ToString("N1", Pt)} m³");
        if (q.SlabM3 > 0) parts.Add($"concreto {q.SlabM3.ToString("N1", Pt)} m³");
        if (q.SurfaceM2 > 0) parts.Add($"revestimento {q.SurfaceM2.ToString("N1", Pt)} m²");
        if (q.DitchLength > 0) parts.Add($"valetas {q.DitchLength.ToString("N0", Pt)} m");
        return string.Join(" · ", parts);
    }

    // ------------------------------------------------------------------ peças

    private static void AddStrip(MarkingGeometry geo, IReadOnlyList<Vec2> line, double width, double z, double thickness, MarkingColor color)
    {
        foreach (var s in PolygonOps.Strip(line, width))
            geo.Pieces.Add(new MarkingPiece(s, color) { Elevation = z, Thickness = thickness });
    }

    private static void Lift(MarkingGeometry geo, double z, double thickness, int fromIndex)
    {
        for (int i = fromIndex; i < geo.Pieces.Count; i++) geo.Pieces[i] = geo.Pieces[i] with { Elevation = z, Thickness = Math.Max(0.005, thickness) };
    }

    /// <summary>Dormentes (monobloco, bibloco com barra, madeira ou aço) e placas de apoio a cada espaçamento.</summary>
    private static void Sleepers(MarkingGeometry geo, Polyline2 path, RailwayDefinition d, List<double> tracks, List<double> rails, double z0, double z1)
    {
        var len = SleeperLengthOf(d);
        var w = SleeperWidthOf(d);
        var h = z1 - z0;
        var color = SleeperColor(d.Sleeper);
        var spacing = Math.Max(0.3, d.SleeperSpacing);
        var L = path.Length;
        var gaugeRails = rails.Take(2).ToList();
        for (var s = spacing / 2; s <= L - spacing / 2 + 1e-9; s += spacing)
        {
            var t = path.TangentAt(s);
            var n = t.PerpLeft;
            var c0 = path.PointAt(s);
            foreach (var tr in tracks)
            {
                var c = c0 + n * tr;
                if (d.Sleeper == TipoDormente.ConcretoBibloco)
                {
                    foreach (var r in gaugeRails)
                        geo.Pieces.Add(new MarkingPiece(Box(c + n * r, t, n, w, 0.84), color) { Elevation = z0, Thickness = h, IsUnit = false });
                    geo.Pieces.Add(new MarkingPiece(Box(c, t, n, 0.05, Math.Abs(gaugeRails[1] - gaugeRails[0])), MarkingColor.Metal) { Elevation = z0 + h * 0.35, Thickness = 0.05 });
                }
                else
                {
                    var center = d.Gauge == BitolaFerroviaria.Mista ? c + n * ((rails.Min() + rails.Max()) / 2) : c;
                    geo.Pieces.Add(new MarkingPiece(Box(center, t, n, w, len), color) { Elevation = z0, Thickness = h });
                }
                if (d.Fastenings)
                    foreach (var r in rails)
                        geo.Pieces.Add(new MarkingPiece(Box(c + n * r, t, n, 0.18, 0.34), MarkingColor.Metal) { Elevation = z1, Thickness = 0.02 });
            }
        }
    }

    /// <summary>Placas/calços da fixação direta (via em laje).</summary>
    private static void Plates(MarkingGeometry geo, Polyline2 path, RailwayDefinition d, List<double> tracks, List<double> rails, double z, double h,
        MarkingColor color, double across, double along)
    {
        var spacing = Math.Max(0.3, d.SleeperSpacing);
        for (var s = spacing / 2; s <= path.Length - spacing / 2 + 1e-9; s += spacing)
        {
            var t = path.TangentAt(s);
            var n = t.PerpLeft;
            var c0 = path.PointAt(s);
            foreach (var tr in tracks)
                foreach (var r in rails)
                    geo.Pieces.Add(new MarkingPiece(Box(c0 + n * (tr + r), t, n, along, across), color) { Elevation = z, Thickness = h });
        }
    }

    /// <summary>Valetas de concreto (fundo + paredes) dos dois lados da plataforma.</summary>
    private static void Ditches(MarkingGeometry geo, Polyline2 path, double mid, double at, RailwayDefinition d)
    {
        var w = Math.Clamp(d.DitchWidth, 0.3, 3);
        var depth = Math.Clamp(d.DitchDepth, 0.1, 2);
        const double wall = 0.08;
        foreach (var side in new[] { -1.0, 1.0 })
        {
            var c = mid + side * (at + w / 2);
            AddStrip(geo, path.Offset(c).Points, w, -depth, wall, MarkingColor.Concreto);
            AddStrip(geo, path.Offset(c - w / 2 + wall / 2).Points, wall, -depth + wall, depth - wall, MarkingColor.Concreto);
            AddStrip(geo, path.Offset(c + w / 2 - wall / 2).Points, wall, -depth + wall, depth - wall, MarkingColor.Concreto);
        }
    }

    /// <summary>Retângulo centrado em <paramref name="c"/>: <paramref name="along"/> no sentido da via e <paramref name="across"/> transversal.</summary>
    private static Polygon2 Box(Vec2 c, Vec2 t, Vec2 n, double along, double across)
    {
        var a = t * (along / 2);
        var b = n * (across / 2);
        return new Polygon2(new[] { c - a - b, c + a - b, c + a + b, c - a + b });
    }

    /// <summary>
    /// Trapézio simétrico (pé ±<paramref name="toeHalf"/> em <paramref name="z0"/>, crista ±<paramref name="crestHalf"/> em <paramref name="z1"/>)
    /// varrido em faixas laterais de até ~1,2 m – sólidos menores, que se ordenam bem na prévia e se unem no Revit.
    /// </summary>
    private static void SweepTrapezoid(MarkingGeometry geo, Polyline2 path, double lateral, double toeHalf, double crestHalf, double z0, double z1, MarkingColor color)
    {
        if (z1 - z0 < 0.005 || toeHalf <= 0) return;
        double Top(double y) => Math.Abs(y) <= crestHalf ? z1 : z1 - (z1 - z0) * (Math.Abs(y) - crestHalf) / Math.Max(1e-6, toeHalf - crestHalf);
        var breaks = new SortedSet<double> { -toeHalf, -crestHalf, crestHalf, toeHalf };
        var n = Math.Max(1, (int)Math.Ceiling(2 * crestHalf / 1.2));
        for (int i = 1; i < n; i++) breaks.Add(-crestHalf + 2 * crestHalf * i / n);
        var ys = breaks.ToList();
        for (int i = 0; i + 1 < ys.Count; i++)
        {
            var ya = ys[i];
            var yb = ys[i + 1];
            if (yb - ya < 0.01) continue;
            var sec = new List<(double, double)> { (ya, z0), (yb, z0) };
            if (Top(yb) > z0 + 1e-4) sec.Add((yb, Top(yb)));
            if (Top(ya) > z0 + 1e-4) sec.Add((ya, Top(ya)));
            if (sec.Count >= 3) Sweep(geo, path, lateral, sec.ToArray(), color);
        }
    }

    /// <summary>
    /// Sólido de seção transversal constante (convexa, pontos (lateral, z)) varrido ao longo do eixo em trechos retos de até 2 m.
    /// </summary>
    private static void Sweep(MarkingGeometry geo, Polyline2 path, double lateral, (double Y, double Z)[] section, MarkingColor color)
    {
        var L = path.Length;
        var steps = Math.Max(1, (int)Math.Ceiling(L / 2.0));
        // Estações nos vértices do eixo (curvas discretizadas) + a cada 2 m.
        var stations = new SortedSet<double>(Enumerable.Range(0, steps + 1).Select(i => L * i / steps));
        double acc = 0;
        for (int i = 1; i < path.Points.Count; i++) { acc += path.Points[i - 1].DistanceTo(path.Points[i]); stations.Add(Math.Min(L, acc)); }
        var list = stations.ToList();
        for (int i = 0; i + 1 < list.Count; i++)
        {
            var s0 = list[i];
            var s1 = list[i + 1];
            if (s1 - s0 < 0.05) continue;
            // Normais médias nas juntas: trechos vizinhos compartilham a face (sem frestas nas curvas).
            Vec2 N(double s) => path.TangentAt(Math.Clamp(s, 0, L)).PerpLeft;
            var n0 = (N(s0 - 0.01) + N(s0 + 0.01)).Normalized();
            var n1 = (N(s1 - 0.01) + N(s1 + 0.01)).Normalized();
            var p0 = path.PointAt(s0) + n0 * lateral;
            var p1 = path.PointAt(s1) + n1 * lateral;
            var f0 = section.Select(q => Vec3.At(p0 + n0 * q.Y, q.Z)).ToList();
            var f1 = section.Select(q => Vec3.At(p1 + n1 * q.Y, q.Z)).ToList();
            var faces = new List<List<Vec3>> { f0, f1 };
            for (int k = 0; k < section.Length; k++)
            {
                var k2 = (k + 1) % section.Length;
                faces.Add(new List<Vec3> { f0[k], f0[k2], f1[k2], f1[k] });
            }
            var solid = new Polyhedron(faces);
            geo.Pieces.Add(new MarkingPiece(solid.Footprint(), color) { Solid = solid });
        }
    }
}
