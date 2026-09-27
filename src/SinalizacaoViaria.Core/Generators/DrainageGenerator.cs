using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Drenagem superficial: bocas de lobo (simples, dupla, com grelha, combinada), grelhas de sarjeta e de piso, canaleta com
/// grelha contínua e poço de visita. Cada dispositivo SUBSTITUI o trecho de pavimento, sarjeta, meio-fio e calçada que
/// ocupa (a área de recorte vem de <see cref="Footprint"/>): rebaixo da sarjeta com transições, guia chapéu com a boca
/// aberta, caixa de captação oca (paredes, fundo e laje), tampa rente à calçada, grelha com caixilho e tubo de ligação.
/// </summary>
/// <remarks>Referências: DNIT 030/2004-ES (dispositivos de drenagem pluvial urbana), ABNT NBR 10160 (tampões e grelhas de
/// ferro fundido dúctil), PMSP – Diretrizes de projeto de drenagem (bocas de lobo com depressão de sarjeta).</remarks>
public static class DrainageGenerator
{
    /// <summary>Espessura da placa de concreto do rebaixo da sarjeta (m).</summary>
    public const double ApronSlab = 0.12;
    /// <summary>Profundidade da guia abaixo do pavimento (m).</summary>
    public const double CurbEmbed = 0.25;

    private static MarkingColor GrateColor(DrainageDefinition d) => d.Material == MaterialGrelha.Concreto ? MarkingColor.Concreto : MarkingColor.Metal;

    /// <summary>
    /// Referencial local do dispositivo: x ao longo do meio-fio, y da face do meio-fio para a calçada (negativo = sarjeta /
    /// pista) e z a partir do pavimento junto à guia.
    /// </summary>
    private sealed class Frame
    {
        public required Vec2 O { get; init; }
        public required Vec2 U { get; init; }
        public required Vec2 V { get; init; }
        /// <summary>+1 quando <see cref="V"/> é a normal à esquerda de <see cref="U"/> (lado usado pelas varreduras).</summary>
        public double Sgn => V.Dot(U.PerpLeft) >= 0 ? 1 : -1;
        public Vec2 W(double x, double y) => O + U * x + V * y;
    }

    /// <summary>Medidas derivadas usadas pela geometria e pelo recorte.</summary>
    private sealed record Layout(
        bool Opening, bool HasGrate, double Total, double Xt, double GutterW,
        List<(double X0, double X1)> Openings, List<(double X0, double X1, double Y0, double Y1)> Grates,
        double Xa, double Xb, double Ya, double Yb, double Floor, double CurbX0, double CurbX1);

    private static Layout Plan(DrainageDefinition d)
    {
        var modules = Math.Clamp(d.Modules, 1, 4);
        var opening = d.Type is TipoDrenagem.BocaDeLoboSimples or TipoDrenagem.BocaDeLoboDupla or TipoDrenagem.BocaDeLoboCombinada;
        var grate = d.Type is TipoDrenagem.BocaDeLoboGrelha or TipoDrenagem.BocaDeLoboCombinada or TipoDrenagem.GrelhaSarjeta;
        var cw = Math.Max(0.08, d.CurbWidth);
        var t = Math.Max(0.08, d.WallThickness);
        // Aberturas na guia: a dupla tem duas bocas separadas por um pilarete de 0,20 m.
        var openings = new List<(double, double)>();
        var grates = new List<(double, double, double, double)>();
        double total;
        if (opening)
        {
            var parts = d.Type == TipoDrenagem.BocaDeLoboDupla ? 2 * modules : modules;
            var ol = d.Type == TipoDrenagem.BocaDeLoboDupla ? Math.Max(0.4, d.OpeningLength / 2 - 0.10) : Math.Max(0.4, d.OpeningLength);
            const double post = 0.20;
            total = parts * ol + (parts - 1) * post;
            for (int i = 0; i < parts; i++)
            {
                var x0 = -total / 2 + i * (ol + post);
                openings.Add((x0, x0 + ol));
            }
        }
        else total = 0;
        if (grate)
        {
            var gl = Math.Max(0.2, d.GrateLength);
            var gw = Math.Max(0.1, d.GrateWidth);
            const double sep = 0.30;
            var gt = modules * gl + (modules - 1) * sep;
            var y1 = -Math.Max(0.02, d.GrateOffset);
            for (int i = 0; i < modules; i++)
            {
                var x0 = -gt / 2 + i * (gl + sep);
                grates.Add((x0, x0 + gl, y1 - gw, y1));
            }
            total = Math.Max(total, gt);
        }
        var gutter = Math.Max(0.3, d.GutterWidth);
        if (grates.Count > 0) gutter = Math.Max(gutter, -grates.Min(g => g.Item3) + 0.15);
        var xt = total / 2 + Math.Max(0.2, d.DepressionLength);
        // Caixa (medidas internas).
        var len = Math.Max(Math.Max(0.3, d.BoxLength), total + 0.10);
        double ya, yb;
        if (opening && grates.Count == 0) { ya = cw; yb = cw + Math.Max(0.3, d.BoxWidth); }
        else if (opening) { ya = grates.Min(g => g.Item3) - 0.05; yb = cw + Math.Max(0.3, d.BoxWidth) * 0.75; }
        else
        {
            var yc = (grates.Min(g => g.Item3) + grates.Max(g => g.Item4)) / 2;
            var w = Math.Max(Math.Max(0.3, d.BoxWidth), grates.Max(g => g.Item4) - grates.Min(g => g.Item3) + 0.10);
            ya = yc - w / 2; yb = yc + w / 2;
        }
        var floor = Math.Min(d.CurbHeight - Math.Max(0.3, d.BoxDepth), -d.Depression - ApronSlab - 0.35);
        var cx = Math.Max(xt, len / 2 + t + 0.05);
        return new Layout(opening, grates.Count > 0, total, xt, gutter, openings, grates, -len / 2, len / 2, ya, yb, floor, -cx, cx);
    }

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
        var f = new Frame { O = d.Position, U = u, V = d.SidewalkLeft ? u.PerpLeft : u.PerpRight };
        switch (d.Type)
        {
            case TipoDrenagem.PocoDeVisita:
                Manhole(geo, d, f);
                break;
            case TipoDrenagem.GrelhaQuadrada:
                FloorGrates(geo, d, f);
                break;
            default:
                CurbInlet(geo, d, f);
                break;
        }
        geo.UnitCount = Math.Clamp(d.Modules, 1, 4);
        BridgeGenerator.Measure(geo, GrateColor(d), 0);
        return geo;
    }

    // ================================================================== prévia em contexto

    /// <summary>
    /// Dispositivo numa rua de demonstração (pista, sarjeta, meio-fio e calçada já recortados) – prévias e miniaturas mostram
    /// o encaixe real na via.
    /// </summary>
    public static MarkingGeometry Demo(DrainageDefinition source, double length = 8)
    {
        var d = (DrainageDefinition)MarkingDefinition.FromJson(source.ToJson())!;
        d.Along = Vec2.UnitX;
        d.SidewalkLeft = true;
        d.Z = 0;
        var geo = new MarkingGeometry();
        Polyline2? path = null;
        var cw = Math.Max(0.08, d.CurbWidth);
        var ch = Math.Max(0.05, d.CurbHeight);
        var half = length / 2;
        var zBase = 0.0;
        if (d.IsLinear)
        {
            // Canaleta atravessando a pista.
            path = new Polyline2(new[] { new Vec2(0, -3.2), new Vec2(0, -0.35) });
            d.Position = Vec2.Zero;
        }
        else if (d.Type == TipoDrenagem.GrelhaQuadrada)
        {
            d.Position = new Vec2(0, cw + 1.4);
            zBase = ch;
        }
        else if (d.Type == TipoDrenagem.PocoDeVisita) d.Position = new Vec2(0, -2.2);
        else d.Position = Vec2.Zero;
        var cut = Footprint(d, path);
        void Floor(Polygon2 area, MarkingColor color, double z0, double z1, string layer)
        {
            foreach (var part in PolygonOps.Difference(new[] { area }, cut))
                geo.Pieces.Add(new MarkingPiece(part, color) { Elevation = z0, Thickness = z1 - z0, Layer = layer });
        }
        Floor(Polygon2.Rectangle(new Vec2(-half, -3.8), new Vec2(half, -0.30)), MarkingColor.Asfalto, -0.05, 0, "PISTA");
        Floor(Polygon2.Rectangle(new Vec2(-half, -0.30), new Vec2(half, 0)), MarkingColor.Concreto, -0.05, 0.003, "SARJETA");
        Floor(Polygon2.Rectangle(new Vec2(-half, 0), new Vec2(half, cw)), MarkingColor.Concreto, 0, ch, "MEIO-FIO");
        Floor(Polygon2.Rectangle(new Vec2(-half, cw), new Vec2(half, cw + 2.6)), MarkingColor.Concreto, ch - 0.10, ch, "CALCADA");
        var dev = Build(d, path);
        foreach (var p in dev.Pieces)
            geo.Pieces.Add(p with { Elevation = p.Elevation + zBase });
        geo.Warnings.AddRange(dev.Warnings);
        geo.UnitCount = dev.UnitCount;
        return geo;
    }

    // ================================================================== recorte

    /// <summary>
    /// Área em planta ocupada pelo dispositivo: o pavimento, a sarjeta, o meio-fio, a calçada e as faixas pintadas são
    /// recortados nela (o dispositivo modela o rebaixo, a guia e a laje no lugar deles).
    /// </summary>
    public static List<Polygon2> Footprint(DrainageDefinition d, Polyline2? path)
    {
        if (d.IsLinear)
        {
            if (path == null || path.Length < 0.3) return new();
            var w = Math.Max(0.1, d.GrateWidth) + 2 * Math.Max(0.06, d.WallThickness);
            return PolygonOps.Strip(path.Points, w + 0.01);
        }
        var u = d.Along.Length < 1e-9 ? Vec2.UnitX : d.Along.Normalized();
        var f = new Frame { O = d.Position, U = u, V = d.SidewalkLeft ? u.PerpLeft : u.PerpRight };
        Polygon2 R(double x0, double x1, double y0, double y1) => new(new[] { f.W(x0, y0), f.W(x1, y0), f.W(x1, y1), f.W(x0, y1) });
        switch (d.Type)
        {
            case TipoDrenagem.PocoDeVisita:
            {
                var r = Math.Clamp(d.LidDiameter, 0.5, 1.0) / 2 + 0.10 + 0.01;
                var c = ManholeNeck(d, f);
                return new() { new Polygon2(Enumerable.Range(0, 48).Select(i => c + Vec2.FromAngle(2 * Math.PI * i / 48) * r)) };
            }
            case TipoDrenagem.GrelhaQuadrada:
            {
                var (hx, hy) = FloorGrateHalf(d);
                return new() { R(-hx, hx, -hy, hy) };
            }
        }
        var p = Plan(d);
        var cw = Math.Max(0.08, d.CurbWidth);
        var t = Math.Max(0.08, d.WallThickness);
        var parts = new List<Polygon2>
        {
            R(-p.Xt, p.Xt, -p.GutterW, 0.001),
            R(p.CurbX0, p.CurbX1, -0.005, cw + 0.005),
        };
        if (p.Yb > cw + 0.01) parts.Add(R(p.Xa - t, p.Xb + t, cw, p.Yb + t));
        return PolygonOps.Union(parts);
    }

    // ================================================================== bocas de lobo e grelhas de sarjeta

    private static void CurbInlet(MarkingGeometry geo, DrainageDefinition d, Frame f)
    {
        var p = Plan(d);
        var cw = Math.Max(0.08, d.CurbWidth);
        var ch = Math.Max(0.05, d.CurbHeight);
        var dep = Math.Max(0, d.Depression);
        var t = Math.Max(0.08, d.WallThickness);
        var slab = Math.Clamp(d.SlabThickness, 0.06, 0.30);
        var apronBottom = -dep - ApronSlab;
        // Largura com fundo plano junto à guia (sob a grelha) e rampa até a borda externa do rebaixo.
        var flatY = p.Grates.Count > 0 ? p.Grates.Min(g => g.Y0) - 0.05 : -Math.Min(0.15, p.GutterW / 3);
        var yProfile = new List<(double Y, double F)> { (-p.GutterW, 0), (Math.Max(-p.GutterW + 0.05, flatY), 1), (0, 1) };
        double Fx(double x)
        {
            var ax = Math.Abs(x);
            if (ax <= p.Total / 2) return 1;
            return Math.Clamp((p.Xt - ax) / Math.Max(1e-6, p.Xt - p.Total / 2), 0, 1);
        }
        double Fy(double y)
        {
            for (int i = 0; i + 1 < yProfile.Count; i++)
                if (y <= yProfile[i + 1].Y + 1e-9)
                {
                    var (y0, f0) = yProfile[i];
                    var (y1, f1) = yProfile[i + 1];
                    return y1 - y0 < 1e-9 ? f1 : f0 + (f1 - f0) * (y - y0) / (y1 - y0);
                }
            return 1;
        }
        double Zs(double x, double y) => -dep * Fx(x) * Fy(y);

        // ---- rebaixo da sarjeta (placa de concreto com a superfície rebaixada, furada nas grelhas)
        if (dep > 0.001 || p.Grates.Count > 0)
            Apron(geo, f, -p.Xt, p.Xt, -p.GutterW, 0, p.Total / 2, yProfile.Select(q => q.Y).ToList(), Zs, apronBottom,
                p.Grates.Select(g => (g.X0, g.X1, g.Y0, g.Y1)).ToList());

        // ---- caixa de captação
        double Cover(double y) => y < -p.GutterW ? -0.06 : y < 0 ? apronBottom : y < cw ? -CurbEmbed : ch - slab;
        CatchBox(geo, f, p.Xa, p.Xb, p.Ya, p.Yb, p.Floor, t, Cover, new[] { -p.GutterW, 0, cw },
            p.Opening ? p.Openings.Select(o => (o.X0, o.X1, -dep)).ToList() : null);

        // ---- meio-fio refeito no trecho do dispositivo (desce até o rebaixo) e guia chapéu sobre as bocas
        var zOpenTop = Math.Min(ch - 0.06, -dep + Math.Max(0.08, d.OpeningHeight));
        var cuts = p.Openings.OrderBy(o => o.X0).ToList();
        var cursor = p.CurbX0;
        foreach (var (x0, x1) in cuts)
        {
            if (x0 - cursor > 0.005) CurbPiece(geo, f, cursor, x0, -CurbEmbed, ch, cw);
            // Guia chapéu: peça pré-moldada sobre a boca, 2 cm saliente, com o topo boleado.
            SolidSweep.Along(geo, Line(f, x0 - 0.001, x1 + 0.001), _ => Sec(f, new (double, double)[]
            {
                (-0.02, zOpenTop), (cw, zOpenTop), (cw, ch), (0.02, ch), (-0.005, ch - 0.015), (-0.02, ch - 0.035),
            }), MarkingColor.Concreto, 0, double.NaN, 10, "GUIA-CHAPEU");
            // Soleira da boca (concreto alisado, caimento para dentro da caixa).
            SolidSweep.Along(geo, Line(f, x0, x1), _ => Sec(f, new (double, double)[]
            {
                (0, -dep - 0.20), (cw, -dep - 0.20), (cw, -dep - 0.04), (0, -dep),
            }), MarkingColor.PavimentoConcreto, 0, double.NaN, 10, "BOCA");
            cursor = x1;
        }
        if (p.CurbX1 - cursor > 0.005) CurbPiece(geo, f, cursor, p.CurbX1, -CurbEmbed, ch, cw);

        // ---- laje de cobertura na calçada com a tampa de inspeção
        if (p.Yb > cw + 0.05)
            SidewalkSlab(geo, d, f, p.Xa - t, p.Xb + t, cw, p.Yb + t, ch, slab);

        // ---- grelhas
        foreach (var (x0, x1, y0, y1) in p.Grates)
            Grate(geo, d, f, (x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0, -dep);

        // ---- tubo de ligação
        if (d.OutletPipe) Pipe(geo, d, f, p.Xa, p.Xb, p.Ya, p.Yb, p.Floor, t);
        if (p.Floor > -0.6) geo.Warnings.Add("Caixa rasa: a profundidade interna deve permitir a ligação do tubo (usual ≥ 1,0 m).");
    }

    /// <summary>Caminho reto ao longo do meio-fio (x0 → x1) – base das varreduras locais.</summary>
    private static Polyline2 Line(Frame f, double x0, double x1) => new(new[] { f.W(x0, 0), f.W(x1, 0) });

    /// <summary>Seção local (y, z) → seção da varredura (lateral à esquerda do sentido).</summary>
    private static SectionPt[] Sec(Frame f, IEnumerable<(double Y, double Z)> pts) => pts.Select(q => new SectionPt(f.Sgn * q.Y, q.Z)).ToArray();

    private static void Blk(MarkingGeometry geo, Frame f, double x0, double x1, double y0, double y1, double z0, double z1, MarkingColor color, string layer,
        bool isUnit = false)
    {
        if (x1 - x0 < 1e-4 || y1 - y0 < 1e-4 || z1 - z0 < 1e-4) return;
        SolidSweep.Add(geo, SolidSweep.Box(f.W((x0 + x1) / 2, (y0 + y1) / 2), f.U, x1 - x0, y1 - y0, z0, z1), color, layer, isUnit);
    }

    /// <summary>Guia de concreto (topo com pequeno chanfro na aresta da pista).</summary>
    private static void CurbPiece(MarkingGeometry geo, Frame f, double x0, double x1, double z0, double z1, double cw) =>
        SolidSweep.Along(geo, Line(f, x0, x1), _ => Sec(f, new (double, double)[]
        {
            (0, z0), (cw, z0), (cw, z1), (0.012, z1), (0, z1 - 0.012),
        }), MarkingColor.Concreto, 0, double.NaN, 10, "MEIO-FIO");

    /// <summary>
    /// Placa do rebaixo entre x0 e x1 (y de y0 a y1): superfície z = <paramref name="zs"/>(x, y), fundo plano; as grelhas são
    /// furos (a placa contorna o caixilho).
    /// </summary>
    private static void Apron(MarkingGeometry geo, Frame f, double x0, double x1, double y0, double y1, double flatHalf, List<double> yBreaks,
        Func<double, double, double> zs, double bottom, List<(double X0, double X1, double Y0, double Y1)> holes)
    {
        var xs = new SortedSet<double> { x0, x1, -flatHalf, flatHalf };
        foreach (var h in holes) { xs.Add(h.X0); xs.Add(h.X1); }
        var xl = xs.Where(x => x >= x0 - 1e-9 && x <= x1 + 1e-9).ToList();
        for (int i = 0; i + 1 < xl.Count; i++)
        {
            var a = xl[i];
            var b = xl[i + 1];
            if (b - a < 1e-4) continue;
            var mid = (a + b) / 2;
            // Faixas livres em y (fora das grelhas deste intervalo).
            var free = new List<(double, double)> { (y0, y1) };
            foreach (var h in holes.Where(h => mid > h.X0 && mid < h.X1))
                free = free.SelectMany(r => Subtract(r, (h.Y0, h.Y1))).ToList();
            foreach (var (ya, yb) in free.Where(r => r.Item2 - r.Item1 > 1e-3))
            {
                var ys = new List<double> { ya };
                ys.AddRange(yBreaks.Where(y => y > ya + 1e-4 && y < yb - 1e-4));
                ys.Add(yb);
                SolidSweep.Along(geo, Line(f, a, b), s =>
                {
                    var x = a + s;
                    var pts = new List<(double, double)> { (ya, bottom), (yb, bottom) };
                    for (int k = ys.Count - 1; k >= 0; k--) pts.Add((ys[k], zs(x, ys[k])));
                    return Sec(f, pts);
                }, MarkingColor.PavimentoConcreto, 0, double.NaN, 0.25, "REBAIXO");
            }
        }
    }

    private static IEnumerable<(double, double)> Subtract((double A, double B) r, (double A, double B) h)
    {
        if (h.B <= r.A || h.A >= r.B) { yield return r; yield break; }
        if (h.A > r.A) yield return (r.A, h.A);
        if (h.B < r.B) yield return (h.B, r.B);
    }

    /// <summary>
    /// Caixa oca: fundo, lâmina d'água, paredes com o topo sob o que as cobre (<paramref name="cover"/> por faixa de y) e,
    /// sob as bocas, a parede frontal rebaixada até a soleira.
    /// </summary>
    private static void CatchBox(MarkingGeometry geo, Frame f, double xa, double xb, double ya, double yb, double floor, double t,
        Func<double, double> cover, double[] zoneBreaks, List<(double X0, double X1, double Sill)>? openings)
    {
        Blk(geo, f, xa - t, xb + t, ya - t, yb + t, floor - t, floor, MarkingColor.Concreto, "CAIXA");
        Blk(geo, f, xa, xb, ya, yb, floor, floor + 0.03, MarkingColor.Agua, "CAIXA");
        // Parede do fundo (lado da calçada) e parede frontal.
        Blk(geo, f, xa - t, xb + t, yb, yb + t, floor, cover(yb + t / 2), MarkingColor.Concreto, "CAIXA");
        var frontTop = cover(ya - t / 2);
        if (openings == null || openings.Count == 0) Blk(geo, f, xa - t, xb + t, ya - t, ya, floor, frontTop, MarkingColor.Concreto, "CAIXA");
        else
        {
            // Sob cada boca a parede para na soleira (−rebaixo − 0,20); entre as bocas sobe até a guia.
            var cursor = xa - t;
            foreach (var (x0, x1, sill) in openings.OrderBy(o => o.X0))
            {
                Blk(geo, f, cursor, x0, ya - t, ya, floor, frontTop, MarkingColor.Concreto, "CAIXA");
                Blk(geo, f, x0, x1, ya - t, ya, floor, sill - 0.20, MarkingColor.Concreto, "CAIXA");
                cursor = x1;
            }
            Blk(geo, f, cursor, xb + t, ya - t, ya, floor, frontTop, MarkingColor.Concreto, "CAIXA");
        }
        // Paredes laterais, divididas nas faixas de cobertura (pista, sarjeta, guia, calçada).
        var ys = new List<double> { ya };
        ys.AddRange(zoneBreaks.Where(y => y > ya + 1e-4 && y < yb - 1e-4).OrderBy(y => y));
        ys.Add(yb);
        foreach (var (x0, x1) in new[] { (xa - t, xa), (xb, xb + t) })
            for (int i = 0; i + 1 < ys.Count; i++)
                Blk(geo, f, x0, x1, ys[i], ys[i + 1], floor, cover((ys[i] + ys[i + 1]) / 2), MarkingColor.Concreto, "CAIXA");
    }

    /// <summary>Laje de cobertura rente à calçada, com a tampa de inspeção (concreto com alças, ou tampão de ferro fundido).</summary>
    private static void SidewalkSlab(MarkingGeometry geo, DrainageDefinition d, Frame f, double x0, double x1, double y0, double y1, double top, double slab)
    {
        var s = Math.Clamp(d.LidDiameter, 0.4, Math.Min(x1 - x0, y1 - y0) - 0.16);
        var hasLid = d.Lid && d.LidType != TipoTampa.Nenhuma && s >= 0.4;
        if (!hasLid)
        {
            Blk(geo, f, x0, x1, y0, y1, top - slab, top, MarkingColor.Concreto, "LAJE");
            return;
        }
        var cy = (y0 + y1) / 2;
        var frame = d.LidType == TipoTampa.FerroFundido ? 0.06 : 0.01;
        var hx0 = -s / 2 - frame;
        var hx1 = s / 2 + frame;
        var hy0 = cy - s / 2 - frame;
        var hy1 = cy + s / 2 + frame;
        Blk(geo, f, x0, x1, y0, hy0, top - slab, top, MarkingColor.Concreto, "LAJE");
        Blk(geo, f, x0, x1, hy1, y1, top - slab, top, MarkingColor.Concreto, "LAJE");
        Blk(geo, f, x0, hx0, hy0, hy1, top - slab, top, MarkingColor.Concreto, "LAJE");
        Blk(geo, f, hx1, x1, hy0, hy1, top - slab, top, MarkingColor.Concreto, "LAJE");
        if (d.LidType == TipoTampa.FerroFundido)
            CastIronLid(geo, f, 0, cy, s, frame, top);
        else
        {
            // Tampa de concreto (junta de 1 cm) com duas alças embutidas.
            Blk(geo, f, -s / 2, s / 2, cy - s / 2, cy + s / 2, top - slab + 0.02, top - 0.002, MarkingColor.PavimentoConcreto, "TAMPA", true);
            foreach (var sx in new[] { -1, 1 })
            {
                Blk(geo, f, sx * s * 0.28 - 0.06, sx * s * 0.28 + 0.06, cy - 0.02, cy + 0.02, top - 0.03, top - 0.004, MarkingColor.Preta, "TAMPA");
                Blk(geo, f, sx * s * 0.28 - 0.05, sx * s * 0.28 + 0.05, cy - 0.006, cy + 0.006, top - 0.012, top - 0.004, MarkingColor.Metal, "TAMPA");
            }
        }
    }

    /// <summary>Tampão de ferro fundido quadrado: aro, tampa com relevo antiderrapante em losangos.</summary>
    private static void CastIronLid(MarkingGeometry geo, Frame f, double cx, double cy, double s, double frame, double top)
    {
        Blk(geo, f, cx - s / 2 - frame, cx + s / 2 + frame, cy - s / 2 - frame, cy - s / 2, top - 0.08, top, MarkingColor.Metal, "TAMPA");
        Blk(geo, f, cx - s / 2 - frame, cx + s / 2 + frame, cy + s / 2, cy + s / 2 + frame, top - 0.08, top, MarkingColor.Metal, "TAMPA");
        Blk(geo, f, cx - s / 2 - frame, cx - s / 2, cy - s / 2, cy + s / 2, top - 0.08, top, MarkingColor.Metal, "TAMPA");
        Blk(geo, f, cx + s / 2, cx + s / 2 + frame, cy - s / 2, cy + s / 2, top - 0.08, top, MarkingColor.Metal, "TAMPA");
        Blk(geo, f, cx - s / 2 + 0.004, cx + s / 2 - 0.004, cy - s / 2 + 0.004, cy + s / 2 - 0.004, top - 0.05, top - 0.004, MarkingColor.Metal, "TAMPA", true);
        // Relevo: nervuras em losango (diagonais) de 4 mm.
        var pitch = 0.10;
        foreach (var dir in new[] { 1.0, -1.0 })
            for (var c = -s; c <= s; c += pitch)
            {
                var poly = ClipStrip(cx - s / 2 + 0.03, cx + s / 2 - 0.03, cy - s / 2 + 0.03, cy + s / 2 - 0.03, new Vec2(1, dir).Normalized(), c, 0.006);
                if (poly.Count >= 3) SolidSweep.Add(geo, Polyhedron.Prism(poly.Select(q => f.W(q.X, q.Y)).ToList(), _ => top - 0.004, _ => top), MarkingColor.Metal, "TAMPA");
            }
    }

    /// <summary>
    /// Faixa |n·(p − c₀)| ≤ w (n = normal de <paramref name="dir"/>, deslocamento <paramref name="c"/>) recortada no retângulo –
    /// barras diagonais e relevos.
    /// </summary>
    private static List<Vec2> ClipStrip(double x0, double x1, double y0, double y1, Vec2 dir, double c, double w)
    {
        var n = dir.PerpLeft;
        var poly = new List<Vec2> { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) };
        var center = new Vec2((x0 + x1) / 2, (y0 + y1) / 2);
        poly = ClipHalf(poly, q => (q - center).Dot(n) - (c - w));
        poly = ClipHalf(poly, q => (c + w) - (q - center).Dot(n));
        return poly;
    }

    /// <summary>Sutherland–Hodgman: mantém a parte com f(p) ≥ 0.</summary>
    private static List<Vec2> ClipHalf(List<Vec2> poly, Func<Vec2, double> fn)
    {
        var res = new List<Vec2>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var fa = fn(a);
            var fb = fn(b);
            if (fa >= 0) res.Add(a);
            if (fa >= 0 != fb >= 0) res.Add(a + (b - a) * (fa / (fa - fb)));
        }
        return res;
    }

    /// <summary>Grelha: caixilho (aro) e tampo no estilo escolhido, topo em <paramref name="top"/>.</summary>
    private static void Grate(MarkingGeometry geo, DrainageDefinition d, Frame f, double cx, double cy, double len, double wid, double top)
    {
        var color = GrateColor(d);
        var fr = Math.Clamp(d.FrameWidth, 0.02, 0.15);
        var z0 = top - 0.10;
        // Aro em cantoneira: borda de 5 mm acima do apoio da grelha.
        Blk(geo, f, cx - len / 2, cx + len / 2, cy - wid / 2, cy - wid / 2 + fr, z0, top, color, "GRELHA");
        Blk(geo, f, cx - len / 2, cx + len / 2, cy + wid / 2 - fr, cy + wid / 2, z0, top, color, "GRELHA");
        Blk(geo, f, cx - len / 2, cx - len / 2 + fr, cy - wid / 2 + fr, cy + wid / 2 - fr, z0, top, color, "GRELHA");
        Blk(geo, f, cx + len / 2 - fr, cx + len / 2, cy - wid / 2 + fr, cy + wid / 2 - fr, z0, top, color, "GRELHA");
        double ix0 = cx - len / 2 + fr, ix1 = cx + len / 2 - fr, iy0 = cy - wid / 2 + fr, iy1 = cy + wid / 2 - fr;
        var bw = Math.Clamp(d.BarWidth, 0.008, 0.08);
        var gap = Math.Clamp(d.BarGap, 0.008, 0.10);
        var pitch = bw + gap;
        var hb = 0.07;
        switch (d.GrateStyle)
        {
            case EstiloGrelha.Malha:
                for (var x = ix0 + gap + bw / 2; x < ix1 - gap / 2; x += pitch) Blk(geo, f, x - bw / 2, x + bw / 2, iy0, iy1, top - hb, top - 0.002, color, "GRELHA");
                for (var y = iy0 + gap + bw / 2; y < iy1 - gap / 2; y += pitch) Blk(geo, f, ix0, ix1, y - bw / 2, y + bw / 2, top - hb, top - 0.004, color, "GRELHA");
                break;
            case EstiloGrelha.Fendas:
            {
                // Chapa com fendas (rasgos de 15 cm × vão), fileiras desencontradas.
                var slot = 0.15;
                var row = gap + 0.035;
                var k = 0;
                Blk(geo, f, ix0, ix1, iy0, iy0 + 0.02, top - 0.05, top - 0.002, color, "GRELHA");
                for (var y = iy0 + 0.02; y < iy1 - 0.02; y += row, k++)
                {
                    var ys0 = y;
                    var ys1 = Math.Min(iy1, y + gap);
                    var y2 = Math.Min(iy1, ys1 + 0.035);
                    Blk(geo, f, ix0, ix1, ys1, y2, top - 0.05, top - 0.002, color, "GRELHA");
                    // Pontes entre os rasgos desta fileira.
                    var off = k % 2 == 0 ? 0 : (slot + 0.04) / 2;
                    for (var x = ix0 + off; x < ix1; x += slot + 0.04)
                        Blk(geo, f, x, Math.Min(ix1, x + 0.04), ys0, ys1, top - 0.05, top - 0.002, color, "GRELHA");
                }
                break;
            }
            default:
            {
                var across = d.Bars == OrientacaoBarras.Transversal;
                if (d.Bars == OrientacaoBarras.Diagonal)
                {
                    var dir = new Vec2(1, 1).Normalized();
                    var span = len + wid;
                    for (var c = -span; c <= span; c += pitch * 1.2)
                    {
                        var poly = ClipStrip(ix0, ix1, iy0, iy1, dir, c, bw / 2);
                        if (poly.Count >= 3 && Polygon2.SignedArea(poly) is var a && Math.Abs(a) > 1e-5)
                            SolidSweep.Add(geo, Polyhedron.Prism(poly.Select(q => f.W(q.X, q.Y)).ToList(), _ => top - hb, _ => top - 0.002), color, "GRELHA");
                    }
                }
                else if (across)
                    for (var x = ix0 + gap + bw / 2; x < ix1 - gap / 2; x += pitch) Blk(geo, f, x - bw / 2, x + bw / 2, iy0, iy1, top - hb, top - 0.002, color, "GRELHA");
                else
                    for (var y = iy0 + gap + bw / 2; y < iy1 - gap / 2; y += pitch) Blk(geo, f, ix0, ix1, y - bw / 2, y + bw / 2, top - hb, top - 0.002, color, "GRELHA");
                // Nervura de travamento perpendicular às barras, abaixo do tampo.
                if (d.Bars != OrientacaoBarras.Diagonal)
                {
                    if (across) Blk(geo, f, ix0, ix1, cy - 0.012, cy + 0.012, top - hb, top - 0.02, color, "GRELHA");
                    else Blk(geo, f, cx - 0.012, cx + 0.012, iy0, iy1, top - hb, top - 0.02, color, "GRELHA");
                }
                break;
            }
        }
    }

    /// <summary>Tubo de ligação (concreto, com bolsas a cada 1 m) saindo da caixa.</summary>
    private static void Pipe(MarkingGeometry geo, DrainageDefinition d, Frame f, double xa, double xb, double ya, double yb, double floor, double t)
    {
        var r = Math.Clamp(d.PipeDiameter, 0.2, 1.5) / 2;
        var wall = Math.Max(0.04, r * 0.16);
        var z = floor + r + 0.05;
        var len = Math.Max(0.5, d.PipeLength);
        Vec2 start, dir;
        if (d.PipeDirection == SaidaTubo.AoLongo) { start = f.W(xb + t, (ya + yb) / 2); dir = f.U; }
        else { start = f.W(0, ya - t); dir = -f.V; }
        SolidSweep.AddRound(geo, Vec3.At(start - dir * (t * 0.5), z), Vec3.At(start + dir * len, z), r + wall, r + wall, MarkingColor.Concreto, "TUBO", false, r, r, 24);
        for (var s = 1.0; s < len; s += 1.0)
            SolidSweep.AddRound(geo, Vec3.At(start + dir * (s - 0.06), z), Vec3.At(start + dir * (s + 0.06), z), r + wall + 0.035, r + wall + 0.035, MarkingColor.Concreto, "TUBO",
                false, r + wall + 0.001, r + wall + 0.001, 24);
    }

    // ================================================================== grelha de piso

    private static (double Hx, double Hy) FloorGrateHalf(DrainageDefinition d)
    {
        var modules = Math.Clamp(d.Modules, 1, 4);
        var gl = Math.Max(0.2, d.GrateLength);
        var gw = Math.Max(0.2, d.GrateWidth);
        const double sep = 0.20;
        var total = modules * gl + (modules - 1) * sep;
        const double collar = 0.10;
        var t = Math.Max(0.08, d.WallThickness);
        var bx = Math.Max(total, d.BoxLength);
        var by = Math.Max(gw, d.BoxWidth);
        return (Math.Max(bx / 2 + t, total / 2 + collar), Math.Max(by / 2 + t, gw / 2 + collar));
    }

    /// <summary>Grelhas de piso em série sobre caixa, com colarinho de concreto rente ao piso.</summary>
    private static void FloorGrates(MarkingGeometry geo, DrainageDefinition d, Frame f)
    {
        var modules = Math.Clamp(d.Modules, 1, 4);
        var gl = Math.Max(0.2, d.GrateLength);
        var gw = Math.Max(0.2, d.GrateWidth);
        const double sep = 0.20;
        var total = modules * gl + (modules - 1) * sep;
        var t = Math.Max(0.08, d.WallThickness);
        var (hx, hy) = FloorGrateHalf(d);
        var xa = -hx + t;
        var xb = hx - t;
        var ya = -hy + t;
        var yb = hy - t;
        var floor = -Math.Max(0.3, d.BoxDepth);
        CatchBox(geo, f, xa, xb, ya, yb, floor, t, _ => -0.10, Array.Empty<double>(), null);
        // Colarinho (laje de 0,10 m com os furos das grelhas).
        var holes = Enumerable.Range(0, modules).Select(i => -total / 2 + i * (gl + sep)).Select(x0 => (X0: x0, X1: x0 + gl)).ToList();
        var cursor = -hx;
        foreach (var (x0, x1) in holes)
        {
            Blk(geo, f, cursor, x0, -hy, hy, -0.10, 0, MarkingColor.Concreto, "COLARINHO");
            Blk(geo, f, x0, x1, -hy, -gw / 2, -0.10, 0, MarkingColor.Concreto, "COLARINHO");
            Blk(geo, f, x0, x1, gw / 2, hy, -0.10, 0, MarkingColor.Concreto, "COLARINHO");
            Grate(geo, d, f, (x0 + x1) / 2, 0, gl, gw, 0);
            cursor = x1;
        }
        Blk(geo, f, cursor, hx, -hy, hy, -0.10, 0, MarkingColor.Concreto, "COLARINHO");
        if (d.OutletPipe) Pipe(geo, d, f, xa, xb, ya, yb, floor, t);
    }

    // ================================================================== poço de visita

    /// <summary>
    /// Poço de visita: câmara cilíndrica sobre laje de fundo com calha (berço), cone excêntrico de redução, chaminé, degraus
    /// de ferro e tampão de ferro fundido Ø 0,60 em aro circular rente ao pavimento.
    /// </summary>
    private static void Manhole(MarkingGeometry geo, DrainageDefinition d, Frame f)
    {
        var c = f.O;
        var r = Math.Max(0.4, d.BoxLength / 2);
        var t = Math.Max(0.1, d.WallThickness);
        var depth = Math.Max(1.0, d.BoxDepth);
        var lid = Math.Clamp(d.LidDiameter, 0.5, 1.0) / 2;
        var neckR = lid + 0.05;
        var zBottom = -depth;
        var coneTop = -0.45;
        var coneBottom = Math.Max(zBottom + 0.9, coneTop - Math.Max(0.4, r - neckR) * 1.2);
        // Laje de fundo e berço (calha) de concreto.
        SolidSweep.AddColumn(geo, c, r + t + 0.10, r + t + 0.10, zBottom - 0.20, zBottom, MarkingColor.Concreto, "PV");
        SolidSweep.AddColumn(geo, c, r, r, zBottom, zBottom + 0.12, MarkingColor.PavimentoConcreto, "PV");
        SolidSweep.Along(geo, new Polyline2(new[] { c - f.U * r, c + f.U * r }), _ => SolidSweep.Rect(-0.22, 0.22, zBottom + 0.10, zBottom + 0.14), MarkingColor.Agua, 0, double.NaN, 10, "PV");
        // Câmara (anel), cone excêntrico (anel cônico deslocado) e chaminé.
        SolidSweep.AddRound(geo, Vec3.At(c, zBottom), Vec3.At(c, coneBottom), r + t, r + t, MarkingColor.Concreto, "PV", false, r, r, 36);
        var neck = ManholeNeck(d, f);                   // chaminé encostada na parede (degraus retos)
        // Cone excêntrico: anéis horizontais com centros diferentes (poliédrico).
        SolidSweep.Add(geo, SolidSweep.HollowLoft(Ring(c, r + t, coneBottom), Ring(neck, neckR + t, coneTop), Ring(c, r, coneBottom), Ring(neck, neckR, coneTop)),
            MarkingColor.Concreto, "PV");
        SolidSweep.AddRound(geo, Vec3.At(neck, coneTop), Vec3.At(neck, -0.10), neckR + t, neckR + t, MarkingColor.Concreto, "PV", false, neckR, neckR, 36);
        // Tubos de entrada e saída.
        var pr = Math.Clamp(d.PipeDiameter, 0.2, 1.5) / 2;
        if (d.OutletPipe)
            foreach (var s in new[] { -1, 1 })
            {
                var a = c + f.U * (s * (r - 0.02));
                SolidSweep.AddRound(geo, Vec3.At(a, zBottom + 0.12 + pr), Vec3.At(a + f.U * (s * Math.Max(0.8, d.PipeLength)), zBottom + 0.12 + pr + (s > 0 ? -0.02 : 0.02)),
                    pr + 0.06, pr + 0.06, MarkingColor.Concreto, "TUBO", false, pr, pr, 24);
            }
        // Degraus de ferro (a cada 0,30 m) na parede sob a chaminé.
        if (d.Steps)
            for (var z = zBottom + 0.45; z < -0.35; z += 0.30)
            {
                var wallAt = z > coneBottom ? neckR + (r - neckR) * Math.Clamp((coneTop - z) / Math.Max(0.1, coneTop - coneBottom), 0, 1) : r;
                var center = z > coneBottom ? c + f.V * (r - neckR) * Math.Clamp((z - coneBottom) / Math.Max(0.1, coneTop - coneBottom), 0, 1) : c;
                var back = center + f.V * wallAt;
                var front = back - f.V * 0.15;
                foreach (var sx in new[] { -0.16, 0.16 })
                    SolidSweep.AddRound(geo, Vec3.At(back + f.U * sx, z), Vec3.At(front + f.U * sx, z), 0.013, 0.013, MarkingColor.Metal, "DEGRAU", false, 0, 0, 10);
                SolidSweep.AddRound(geo, Vec3.At(front - f.U * 0.17, z), Vec3.At(front + f.U * 0.17, z), 0.014, 0.014, MarkingColor.Metal, "DEGRAU", false, 0, 0, 10);
            }
        // Aro circular e tampão com relevo (anéis concêntricos e nervuras radiais).
        SolidSweep.AddRound(geo, Vec3.At(neck, -0.10), Vec3.At(neck, 0), lid + 0.10, lid + 0.10, MarkingColor.Metal, "TAMPAO", false, lid + 0.005, lid + 0.005, 48);
        SolidSweep.AddColumn(geo, neck, lid, lid, -0.06, -0.004, MarkingColor.Metal, "TAMPAO", true);
        foreach (var rr in new[] { lid * 0.92, lid * 0.62, lid * 0.30 })
            SolidSweep.AddRound(geo, Vec3.At(neck, -0.004), Vec3.At(neck, 0), rr, rr, MarkingColor.Metal, "TAMPAO", false, rr - 0.012, rr - 0.012, 48);
        for (int i = 0; i < 12; i++)
        {
            var dir = Vec2.FromAngle(2 * Math.PI * i / 12);
            SolidSweep.Add(geo, SolidSweep.Box(neck + dir * (lid * 0.61), dir, lid * 0.58, 0.012, -0.004, 0), MarkingColor.Metal, "TAMPAO");
        }
        if (depth > 6) geo.Warnings.Add("PV com mais de 6 m: preveja patamares intermediários.");
    }

    private static List<Vec3> Ring(Vec2 c, double r, double z, int n = 36) =>
        Enumerable.Range(0, n).Select(i => Vec3.At(c + Vec2.FromAngle(2 * Math.PI * i / n) * r, z)).ToList();

    /// <summary>Centro da chaminé do PV (encostada na parede da câmara, do lado da calçada).</summary>
    private static Vec2 ManholeNeck(DrainageDefinition d, Frame f)
    {
        var r = Math.Max(0.4, d.BoxLength / 2);
        var neckR = Math.Clamp(d.LidDiameter, 0.5, 1.0) / 2 + 0.05;
        return f.O + f.V * Math.Max(0, r - neckR);
    }

    // ================================================================== canaleta com grelha contínua

    /// <summary>Canaleta em "U" de concreto com grelha contínua (aro em cantoneira e barras transversais).</summary>
    private static void Channel(MarkingGeometry geo, DrainageDefinition d, Polyline2 path)
    {
        var w = Math.Max(0.1, d.GrateWidth);
        var t = Math.Max(0.06, d.WallThickness);
        var depth = Math.Max(0.15, d.BoxDepth);
        var color = GrateColor(d);
        var L = path.Length;
        // Corpo em "U" (um perfil só) e lâmina d'água no fundo.
        SolidSweep.Along(geo, path, _ => new[]
        {
            new SectionPt(-w / 2 - t, -depth - t), new SectionPt(w / 2 + t, -depth - t), new SectionPt(w / 2 + t, 0), new SectionPt(w / 2 + 0.02, 0),
            new SectionPt(w / 2 + 0.02, -0.035), new SectionPt(w / 2, -0.035), new SectionPt(w / 2, -depth + 0.03), new SectionPt(0, -depth),
            new SectionPt(-w / 2, -depth + 0.03), new SectionPt(-w / 2, -0.035), new SectionPt(-w / 2 - 0.02, -0.035), new SectionPt(-w / 2 - 0.02, 0),
            new SectionPt(-w / 2 - t, 0),
        }, MarkingColor.Concreto, 0, double.NaN, 6, "CANALETA");
        SolidSweep.Along(geo, path, _ => new[] { new SectionPt(-w / 2 + 0.01, -depth + 0.005), new SectionPt(w / 2 - 0.01, -depth + 0.005), new SectionPt(w / 2 - 0.01, -depth + 0.04), new SectionPt(-w / 2 + 0.01, -depth + 0.04) },
            MarkingColor.Agua, 0, double.NaN, 6, "CANALETA");
        // Cantoneiras do aro (perfil em "L").
        foreach (var sg in new[] { -1.0, 1.0 })
        {
            var e = w / 2;
            var sec = new[]
            {
                new SectionPt(sg * (e + 0.016), -0.035), new SectionPt(sg * (e + 0.02), -0.035), new SectionPt(sg * (e + 0.02), 0),
                new SectionPt(sg * (e - 0.015), 0), new SectionPt(sg * (e - 0.015), -0.004), new SectionPt(sg * (e + 0.016), -0.004),
            };
            SolidSweep.Along(geo, path, _ => sec, color, 0, double.NaN, 6, "GRELHA");
        }
        // Barras transversais (em módulos de 0,50 m nas canaletas muito longas – limite de peças).
        var pitch = Math.Max(0.03, d.BarWidth + d.BarGap);
        if (L / pitch > 1500) pitch = Math.Max(pitch, L / 1500);
        for (var s = pitch / 2; s < L; s += pitch)
            SolidSweep.Add(geo, SolidSweep.Box(path.PointAt(s), path.TangentAt(s), Math.Min(d.BarWidth, pitch * 0.8), w - 0.03, -0.035, -0.002), color, "GRELHA");
        // Longarinas sob as barras.
        foreach (var y in new[] { -w / 4, w / 4 })
            SolidSweep.Along(geo, path, _ => SolidSweep.Rect(y - 0.006, y + 0.006, -0.045, -0.012), color, 0, double.NaN, 6, "GRELHA");
        // Tampas das pontas.
        foreach (var (s, dir) in new[] { (0.0, 1.0), (L, -1.0) })
        {
            var p = path.PointAt(s);
            var tg = path.TangentAt(s) * dir;
            SolidSweep.Add(geo, SolidSweep.Box(p + tg * (t / 2), tg, t, w + 2 * t, -depth - t, 0), MarkingColor.Concreto, "CANALETA");
        }
    }
}
