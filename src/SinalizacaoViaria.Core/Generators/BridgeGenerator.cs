using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Seção e estrutura de um tabuleiro (usada por viadutos, pontes, passarelas e pelos nós viários).</summary>
public sealed record DeckSpec
{
    public double RoadHalf { get; init; } = 5.5;
    public double SidewalkWidth { get; init; }
    public int Lanes { get; init; } = 2;
    public bool TwoWay { get; init; } = true;
    public double DeckThickness { get; init; } = 0.25;
    public double GirderDepth { get; init; }
    public double GirderSpacing { get; init; } = 2.8;
    public SistemaEstrutural System { get; init; } = SistemaEstrutural.VigasPreMoldadas;
    public TipoPilar PierType { get; init; } = TipoPilar.Portico;
    public double PierSize { get; init; } = 1.2;
    public double SpanLength { get; init; } = 30;
    public double MainSpan { get; init; }
    public TipoGuarda Barrier { get; init; } = TipoGuarda.NewJersey;
    public bool Lighting { get; init; } = true;
    public double LightSpacing { get; init; } = 30;
    public bool Markings { get; init; } = true;
    /// <summary>Pedestres (passarela): piso de concreto, sem faixas nem barreira New Jersey.</summary>
    public bool Pedestrian { get; init; }

    public double BarrierWidth => Barrier == TipoGuarda.GuardaCorpoMetalico ? 0.15 : 0.46;
    /// <summary>Meia largura total do tabuleiro (m).</summary>
    public double DeckHalf => RoadHalf + BarrierWidth + (SidewalkWidth > 0.01 ? SidewalkWidth + 0.20 : 0);
}

/// <summary>
/// Obras de arte especiais: viadutos, pontes e passarelas – greide com rampas de acesso em aterro (ou terra armada), encontros
/// com alas, pilares (circular, duplo, pórtico, parede, martelo) sobre blocos, vigas pré-moldadas / caixão / laje, arcos
/// inferior e superior, estaiada e treliça, pavimento, passeios, barreiras, guarda-corpos, juntas, iluminação e faixas.
/// </summary>
/// <remarks>Referências: ABNT NBR 7188 (cargas móveis), NBR 6118/7187 (concreto), DNIT 090/2006 e Manual de OAE do DNIT
/// (gabarito vertical ≥ 5,50 m sobre rodovias), NBR 9050 (passarelas: rampas ≤ 8,33 %, guarda-corpo 1,10 m).</remarks>
public static class BridgeGenerator
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static DeckSpec Spec(BridgeDefinition b)
    {
        var pedestrian = b.Kind == TipoObraDeArte.Passarela;
        var roadHalf = pedestrian ? Math.Max(1.2, b.LaneWidth) / 2 : b.RoadWidth / 2;
        return new DeckSpec
        {
            RoadHalf = roadHalf, SidewalkWidth = pedestrian ? 0 : b.SidewalkWidth, Lanes = pedestrian ? 0 : b.Lanes, DeckThickness = b.DeckThickness,
            GirderDepth = b.GirderDepth, GirderSpacing = b.GirderSpacing, System = b.System, PierType = b.PierType, PierSize = b.PierSize,
            SpanLength = b.SpanLength, MainSpan = b.MainSpan, Barrier = pedestrian ? TipoGuarda.GuardaCorpoMetalico : b.Barrier,
            Lighting = b.Lighting, LightSpacing = b.LightSpacing, Markings = b.LaneMarkings && !pedestrian, Pedestrian = pedestrian,
        };
    }

    /// <summary>Altura máxima de aterro nas rampas de acesso antes de começar a estrutura (m).</summary>
    public const double MaxFillHeight = 6.0;

    /// <summary>
    /// Greide da obra (rampas de acesso limitadas à rampa máxima, trecho em nível na estrutura) e trecho em estrutura:
    /// começa onde o aterro de acesso passaria de <see cref="MaxFillHeight"/>.
    /// </summary>
    public static (VerticalProfile Profile, double S0, double S1, List<string> Warnings) Profile(BridgeDefinition b, Polyline2 path, Func<Vec2, double> ground)
    {
        var length = path.Length;
        var zStart = ground(path.PointAt(0));
        var zEnd = ground(path.PointAt(length));
        var warnings = new List<string>();
        var g = Math.Max(0.01, b.MaxGrade);
        var rin = b.ApproachStart ? Math.Abs(b.Height - zStart) / g : 0;
        var rout = b.ApproachEnd ? Math.Abs(b.Height - zEnd) / g : 0;
        var available = length * 0.85;
        if (rin + rout > available && rin + rout > 0)
        {
            var k = available / (rin + rout);
            rin *= k;
            rout *= k;
            warnings.Add($"Eixo curto para as rampas de acesso: rampa de {(Math.Abs(b.Height - zStart) / Math.Max(1, rin) * 100).ToString("0.0", Pt)} % " +
                         $"(máx. {(g * 100).ToString("0.0", Pt)} %) – alongue o eixo ou reduza a altura.");
        }
        var prof = VerticalProfile.Hump(length, b.ApproachStart ? zStart : b.Height, b.Height, b.ApproachEnd ? zEnd : b.Height, rin, rout, b.VerticalCurve);
        double Fill(double s) => prof.Z(s) - ground(path.PointAt(s));
        var s0 = 0.0;
        if (b.ApproachStart)
        {
            s0 = rin;
            for (var s = 0.0; s < rin; s += 1) if (Fill(s) > MaxFillHeight) { s0 = s; break; }
        }
        var s1 = length;
        if (b.ApproachEnd)
        {
            s1 = length - rout;
            for (var s = length; s > length - rout; s -= 1) if (Fill(s) > MaxFillHeight) { s1 = s; break; }
        }
        // Estrutura com pelo menos um vão.
        var minLen = Math.Min(Math.Max(10, b.SpanLength), length * 0.9);
        if (s1 - s0 < minLen)
        {
            var mid = (s0 + s1) / 2;
            s0 = Math.Max(0, mid - minLen / 2);
            s1 = Math.Min(length, s0 + minLen);
        }
        return (prof, s0, s1, warnings);
    }

    public static MarkingGeometry Build(BridgeDefinition b, Polyline2 path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var L = path.Length;
        if (L < 10) { geo.Warnings.Add("Eixo da obra muito curto (mínimo 10 m)."); return geo; }
        // Terreno natural congelado na definição (a terraplenagem não realimenta o greide).
        var gp = Infra.GroundProfile(b, path, ctx);
        Func<Vec2, double> ground = p => gp.Z(IntersectionGenerator.Project(path, p).Station);
        var z0 = ground(path.PointAt(0));
        var z1 = ground(path.PointAt(L));
        var (prof, s0, s1, warnings) = Profile(b, path, ground);
        geo.Warnings.AddRange(warnings);
        var spec = Spec(b);

        // Rampas de acesso em aterro (ou terra armada) e a estrutura.
        if (s0 > 0.5) Approach(geo, path, prof, spec, 0, s0, ground, b.FillSlope, b.ApproachWalls);
        if (s1 < L - 0.5) Approach(geo, path, prof, spec, s1, L, ground, b.FillSlope, b.ApproachWalls);
        var info = Structure(geo, path, prof, spec, s0, s1, ground);
        if (b.Water)
        {
            var mid = (s0 + s1) / 2;
            var c = path.PointAt(mid);
            var t = path.TangentAt(mid);
            SolidSweep.Add(geo, SolidSweep.Box(c, t, Math.Min(s1 - s0, b.WaterWidth), 2 * spec.DeckHalf + 60, b.WaterLevel - 0.3, b.WaterLevel), MarkingColor.Agua, "AGUA");
        }
        if (b.Kind != TipoObraDeArte.Passarela && b.Height - Math.Max(z0, z1) < 5.5 + 1.0 && !b.Water)
            geo.Warnings.Add("Confira o gabarito vertical: o DNIT exige 5,50 m livres sob viadutos rodoviários.");
        if (b.Kind == TipoObraDeArte.Passarela && b.MaxGrade > 0.0834)
            geo.Warnings.Add("Passarela: rampas acima de 8,33 % não atendem a NBR 9050.");

        geo.PathLength = L;
        geo.PaintedLength = s1 - s0;
        geo.UnitCount = info.Piers;
        Measure(geo, MarkingColor.Concreto, (s1 - s0) * 2 * spec.DeckHalf);
        return geo;
    }

    /// <summary>Quantidade principal da obra no quantitativo (substitui a soma das projeções dos sólidos).</summary>
    public static void Measure(MarkingGeometry geo, MarkingColor main, double area)
    {
        foreach (var c in geo.Pieces.Select(p => p.Color).Distinct()) geo.AreaOverrides[c] = 0;
        geo.AreaOverrides[main] = Math.Max(0, area);
    }

    /// <summary>Rampa de acesso: pavimento, base, aterro com taludes (ou muros de terra armada), barreiras e terraplenagem.</summary>
    public static void Approach(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, DeckSpec d, double s0, double s1, Func<Vec2, double> ground,
        double fillSlope, bool walls)
    {
        var half = d.DeckHalf;
        Infra.Pavement(geo, path, prof, -d.RoadHalf, d.RoadHalf, s0, s1, d.Pedestrian ? MarkingColor.PavimentoConcreto : MarkingColor.Asfalto);
        if (d.SidewalkWidth > 0.01)
            foreach (var side in new[] { -1, 1 })
            {
                var a = side * (d.RoadHalf + d.BarrierWidth);
                var bb = side * half;
                SolidSweep.Along(geo, path, s => SolidSweep.Rect(Math.Min(a, bb), Math.Max(a, bb), prof.Z(s) - Infra.Wearing, prof.Z(s) + 0.20), MarkingColor.Concreto, s0, s1, 6, "PASSEIO");
            }
        Infra.Base(geo, path, prof, -half, half, s0, s1);
        if (walls)
        {
            // Terra armada: paredes verticais nas bordas, do terreno à base do pavimento.
            foreach (var side in new[] { -1, 1 })
                SolidSweep.Along(geo, path, s =>
                {
                    var y = side * (half + 0.10);
                    var g = ground(path.PointAt(s) + SolidSweep.Normal(path, s) * y) - 0.6;
                    var top = prof.Z(s) - Infra.Wearing;
                    return top - g < 0.3 ? null : SolidSweep.Rect(y - 0.10, y + 0.10, g, top);
                }, MarkingColor.Concreto, s0, s1, 3, "TERRA-ARMADA");
            SolidSweep.Along(geo, path, s =>
            {
                var g = ground(path.PointAt(s));
                var top = prof.Z(s) - Infra.Wearing - 0.35;
                return top - g < 0.1 ? null : SolidSweep.Rect(-half, half, g, top);
            }, MarkingColor.Terra, s0, s1, 6, "ATERRO");
        }
        else Infra.Embankment(geo, path, prof, -half, half, s0, s1, fillSlope, ground);
        Guards(geo, path, prof, d, s0, s1);
        if (d.Markings) Infra.LaneMarkings(geo, path, prof, -d.RoadHalf, d.RoadHalf, d.Lanes, s0, s1, d.TwoWay);
        geo.Corridors.Add(Infra.Corridor(path, prof, -half - 0.3, half + 0.3, s0, s1, Infra.Wearing + 0.35, 1.0, fillSlope,
            daylightRight: !walls, daylightLeft: !walls, label: "Rampa de acesso"));
    }

    private static void Guards(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, DeckSpec d, double s0, double s1)
    {
        foreach (var side in new[] { -1, 1 })
        {
            var edge = side * d.RoadHalf;
            if (d.Barrier != TipoGuarda.GuardaCorpoMetalico)
                Infra.NewJersey(geo, path, prof, side * (d.RoadHalf + d.BarrierWidth), -side, s0, s1);
            else
                Infra.Railing(geo, path, prof, side * (d.RoadHalf + 0.07), s0, s1);
            if (d.SidewalkWidth > 0.01 || d.Barrier == TipoGuarda.NewJerseyComGuardaCorpo)
                Infra.Railing(geo, path, prof, side * (d.DeckHalf - 0.08), s0, s1, d.SidewalkWidth > 0.01 ? 0.20 : 0.81);
            _ = edge;
        }
    }

    public sealed record StructureInfo(int Piers, double GirderDepth);

    /// <summary>Estrutura entre <paramref name="s0"/> e <paramref name="s1"/>: tabuleiro, sistema estrutural, pilares, encontros e acessórios.</summary>
    public static StructureInfo Structure(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, DeckSpec d, double s0, double s1, Func<Vec2, double> ground)
    {
        var len = s1 - s0;
        var half = d.DeckHalf;
        var arch = d.System is SistemaEstrutural.ArcoInferior or SistemaEstrutural.ArcoSuperior or SistemaEstrutural.Estaiada or SistemaEstrutural.Trelica;
        // Vão principal (sem pilares) centrado na estrutura.
        var main = arch ? Math.Clamp(d.MainSpan > 1 ? d.MainSpan : len, 10, len) : 0;
        var ma = s0 + (len - main) / 2;
        var mb = ma + main;
        var span = Math.Max(8, d.SpanLength);
        var gd = d.GirderDepth > 0.1 ? d.GirderDepth : d.System switch
        {
            SistemaEstrutural.CaixaoCelular => Math.Clamp(span / 20, 1.4, 4),
            SistemaEstrutural.LajeMacica => 0,
            SistemaEstrutural.ArcoSuperior or SistemaEstrutural.Estaiada or SistemaEstrutural.Trelica => 0.9,
            _ => Math.Clamp(span / 16, 1.0, 3.5),
        };
        var slab = d.System == SistemaEstrutural.LajeMacica ? Math.Max(d.DeckThickness, span / 22) : d.DeckThickness;
        double SlabTop(double s) => prof.Z(s) - Infra.Wearing;
        double SlabBottom(double s) => SlabTop(s) - slab;
        double Soffit(double s) => SlabBottom(s) - gd;

        // ---- tabuleiro, pavimento e passeios
        SolidSweep.Along(geo, path, s => SolidSweep.Rect(-half, half, SlabBottom(s), SlabTop(s)), MarkingColor.Concreto, s0, s1, 4, "TABULEIRO");
        Infra.Pavement(geo, path, prof, -d.RoadHalf, d.RoadHalf, s0, s1, d.Pedestrian ? MarkingColor.PavimentoConcreto : MarkingColor.Asfalto);
        if (d.SidewalkWidth > 0.01)
            foreach (var side in new[] { -1, 1 })
            {
                var a = side * (d.RoadHalf + d.BarrierWidth);
                var bb = side * half;
                SolidSweep.Along(geo, path, s => SolidSweep.Rect(Math.Min(a, bb), Math.Max(a, bb), SlabTop(s), prof.Z(s) + 0.20), MarkingColor.Concreto, s0, s1, 6, "PASSEIO");
            }
        Guards(geo, path, prof, d, s0, s1);
        if (d.Markings) Infra.LaneMarkings(geo, path, prof, -d.RoadHalf, d.RoadHalf, d.Lanes, s0, s1, d.TwoWay);
        Infra.Joint(geo, path, prof, s0 + 0.1, -d.RoadHalf, d.RoadHalf);
        Infra.Joint(geo, path, prof, s1 - 0.1, -d.RoadHalf, d.RoadHalf);
        // Pingadeiras/abas laterais (acabamento da borda do tabuleiro).
        foreach (var side in new[] { -1, 1 })
            SolidSweep.Along(geo, path, s => SolidSweep.Rect(side < 0 ? -half - 0.05 : half, side < 0 ? -half : half + 0.05, SlabBottom(s) - 0.25, SlabTop(s) + 0.05),
                MarkingColor.Concreto, s0, s1, 6, "TABULEIRO");

        // ---- superestrutura
        switch (d.System)
        {
            case SistemaEstrutural.VigasPreMoldadas:
            {
                var n = Math.Max(2, (int)Math.Ceiling(2 * half / Math.Max(1.5, d.GirderSpacing)));
                var spacing = 2 * half / n;
                for (int i = 0; i < n; i++)
                {
                    var y = -half + spacing * (i + 0.5);
                    // Viga "I": mesa superior, alma e talão (três prismas convexos).
                    SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.45, y + 0.45, SlabBottom(s) - 0.15, SlabBottom(s)), MarkingColor.Concreto, s0, s1, 6, "VIGA");
                    SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.10, y + 0.10, Soffit(s) + 0.25, SlabBottom(s) - 0.15), MarkingColor.Concreto, s0, s1, 6, "VIGA");
                    SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.32, y + 0.32, Soffit(s), Soffit(s) + 0.25), MarkingColor.Concreto, s0, s1, 6, "VIGA");
                }
                break;
            }
            case SistemaEstrutural.CaixaoCelular:
            {
                var bw = half * 0.55;
                SolidSweep.Along(geo, path, s => SolidSweep.Rect(-bw, bw, Soffit(s), Soffit(s) + 0.25), MarkingColor.Concreto, s0, s1, 6, "CAIXAO");
                foreach (var side in new[] { -1, 1 })
                    SolidSweep.Along(geo, path, s =>
                    {
                        var top = SlabBottom(s);
                        var bot = Soffit(s) + 0.25;
                        var outerTop = side * (bw + 0.6);
                        var outerBot = side * bw;
                        var pts = new[] { new SectionPt(outerBot - side * 0.35, bot), new SectionPt(outerBot, bot), new SectionPt(outerTop, top), new SectionPt(outerTop - side * 0.35, top) };
                        return side < 0 ? pts.Reverse().ToArray() : pts;
                    }, MarkingColor.Concreto, s0, s1, 6, "CAIXAO");
                break;
            }
            case SistemaEstrutural.ArcoInferior:
                ArchBelow(geo, path, prof, half, ma, mb, SlabBottom, ground);
                break;
            case SistemaEstrutural.ArcoSuperior:
                ArchAbove(geo, path, prof, half, ma, mb, SlabBottom);
                EdgeBeams(geo, path, half, s0, s1, SlabBottom, gd);
                break;
            case SistemaEstrutural.Estaiada:
                CableStayed(geo, path, prof, half, ma, mb, SlabBottom, ground);
                EdgeBeams(geo, path, half, s0, s1, SlabBottom, gd);
                break;
            case SistemaEstrutural.Trelica:
                Truss(geo, path, prof, half, s0, s1, SlabBottom, d.Pedestrian);
                break;
        }

        // ---- pilares (fora do vão principal) e encontros
        var piers = new List<double>();
        void Spans(double a, double b)
        {
            var n = Math.Max(1, (int)Math.Round((b - a) / span));
            for (int i = 1; i < n; i++) piers.Add(a + (b - a) * i / n);
        }
        if (arch) { Spans(s0, ma); Spans(mb, s1); if (ma - s0 > 2) piers.Add(ma); if (s1 - mb > 2) piers.Add(mb); }
        else Spans(s0, s1);
        foreach (var s in piers.Distinct()) Pier(geo, path, d, s, Soffit(s) - 0.15, ground, half);
        Abutment(geo, path, prof, d, s0, +1, Soffit(s0), ground, half);
        Abutment(geo, path, prof, d, s1, -1, Soffit(s1), ground, half);

        if (d.Lighting && !d.Pedestrian) Infra.Lights(geo, path, prof, d.RoadHalf + d.BarrierWidth / 2, s0, s1, d.LightSpacing, zBase: 0.81);
        if (d.Lighting && d.Pedestrian) Infra.Lights(geo, path, prof, half - 0.1, s0, s1, d.LightSpacing * 0.6, height: 4.5);
        return new StructureInfo(piers.Count, gd);
    }

    private static void EdgeBeams(MarkingGeometry geo, Polyline2 path, double half, double s0, double s1, Func<double, double> slabBottom, double gd)
    {
        foreach (var side in new[] { -1, 1 })
        {
            var y = side * (half - 0.5);
            SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.5, y + 0.5, slabBottom(s) - gd, slabBottom(s)), MarkingColor.Concreto, s0, s1, 6, "VIGA");
        }
    }

    private static void Pier(MarkingGeometry geo, Polyline2 path, DeckSpec d, double s, double top, Func<Vec2, double> ground, double half)
    {
        var p = path.PointAt(s);
        var t = path.TangentAt(s);
        var n = t.PerpLeft;
        var g = ground(p);
        var bottom = g - 0.3;
        if (top - bottom < 0.5) return;
        var size = Math.Max(0.4, d.PierSize);
        var capH = Math.Clamp(size * 1.2, 1.0, 2.0);
        // Bloco de fundação (enterrado).
        SolidSweep.Add(geo, SolidSweep.Box(p, t, size * 2.6, Math.Min(2 * half, size * 2.6 + half), g - 1.8, g - 0.3), MarkingColor.Concreto, "FUNDACAO");
        switch (d.PierType)
        {
            case TipoPilar.Circular:
                SolidSweep.Add(geo, SolidSweep.Cylinder(p, size / 2, bottom, top, 20), MarkingColor.Concreto, "PILAR", isUnit: true);
                break;
            case TipoPilar.Martelo:
            {
                SolidSweep.Add(geo, SolidSweep.Cylinder(p, size / 2, bottom, top - capH, 20), MarkingColor.Concreto, "PILAR", isUnit: true);
                // Capitel em martelo (trapézio: estreito embaixo, largo em cima).
                var w0 = size;
                var w1 = 2 * half * 0.85;
                var b0 = new List<Vec3> { V(p - t * size / 2 - n * w0 / 2, top - capH), V(p + t * size / 2 - n * w0 / 2, top - capH), V(p + t * size / 2 + n * w0 / 2, top - capH), V(p - t * size / 2 + n * w0 / 2, top - capH) };
                var b1 = new List<Vec3> { V(p - t * size / 2 - n * w1 / 2, top), V(p + t * size / 2 - n * w1 / 2, top), V(p + t * size / 2 + n * w1 / 2, top), V(p - t * size / 2 + n * w1 / 2, top) };
                SolidSweep.Add(geo, SolidSweep.Prism(b0, b1), MarkingColor.Concreto, "PILAR");
                break;
            }
            case TipoPilar.Parede:
                SolidSweep.Add(geo, SolidSweep.Box(p, t, size * 0.8, 2 * half * 0.7, bottom, top), MarkingColor.Concreto, "PILAR", isUnit: true);
                break;
            case TipoPilar.DuplaCircular:
            case TipoPilar.Portico:
            default:
            {
                var off = half * 0.55;
                foreach (var side in new[] { -1, 1 })
                {
                    var c = p + n * (side * off);
                    var gc = ground(c) - 0.3;
                    if (d.PierType == TipoPilar.DuplaCircular) SolidSweep.Add(geo, SolidSweep.Cylinder(c, size / 2, gc, top - capH, 20), MarkingColor.Concreto, "PILAR", isUnit: true);
                    else SolidSweep.Add(geo, SolidSweep.Box(c, t, size, size, gc, top - capH), MarkingColor.Concreto, "PILAR", isUnit: true);
                }
                // Travessa (viga de apoio) sobre os pilares.
                SolidSweep.Add(geo, SolidSweep.Box(p, t, size * 1.1, 2 * half * 0.9, top - capH, top), MarkingColor.Concreto, "PILAR");
                break;
            }
        }
        // Aparelhos de apoio (neoprene).
        SolidSweep.Add(geo, SolidSweep.Box(p, t, 0.5, 2 * half * 0.7, top, top + 0.15), MarkingColor.Preta, "APOIO");
    }

    private static Vec3 V(Vec2 p, double z) => Vec3.At(p, z);

    /// <summary>Encontro: cortina frontal, alas a 45° e laje de transição.</summary>
    private static void Abutment(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, DeckSpec d, double s, int inward, double seat, Func<Vec2, double> ground, double half)
    {
        var p = path.PointAt(s);
        var t = path.TangentAt(s);
        var n = t.PerpLeft;
        var g = ground(p);
        if (seat - g < 0.3) return;
        var face = p - t * (inward * 0.5);
        // Cortina frontal + viga de apoio.
        SolidSweep.Add(geo, SolidSweep.Box(face, t, 1.0, 2 * half + 0.6, g - 1.0, prof.Z(s) - Infra.Wearing), MarkingColor.Concreto, "ENCONTRO");
        // Alas (muros laterais) contendo o aterro de acesso.
        var wing = Math.Clamp((seat - g) * 1.2, 2, 10);
        foreach (var side in new[] { -1, 1 })
        {
            var dir = (-t * inward + n * side * 0.6).Normalized();
            var a = face + n * (side * (half + 0.3));
            var c = a + dir * (wing / 2);
            SolidSweep.Add(geo, SolidSweep.Box(c, dir, wing, 0.35, g - 0.6, seat + 0.4), MarkingColor.Concreto, "ENCONTRO");
        }
    }

    private static void ArchBelow(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double half, double ma, double mb, Func<double, double> slabBottom,
        Func<Vec2, double> ground)
    {
        var mid = (ma + mb) / 2;
        var hs = (mb - ma) / 2;
        var spring = Math.Min(ground(path.PointAt(ma)), ground(path.PointAt(mb)));
        var crown = slabBottom(mid) - 0.8;
        if (crown - spring < 2) return;
        double Za(double s) { var x = (s - mid) / hs; return spring + (crown - spring) * (1 - x * x); }
        foreach (var side in new[] { -1, 1 })
        {
            var y = side * half * 0.62;
            SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.6, y + 0.6, Za(s) - 0.7, Za(s) + 0.7), MarkingColor.Concreto, ma, mb, 2.5, "ARCO");
        }
        // Montantes do arco até o tabuleiro.
        var k = Math.Max(4, (int)Math.Round((mb - ma) / 8));
        for (int i = 1; i < k; i++)
        {
            var s = ma + (mb - ma) * i / k;
            if (slabBottom(s) - Za(s) < 1.2) continue;
            var p = path.PointAt(s);
            var t = path.TangentAt(s);
            SolidSweep.Add(geo, SolidSweep.Box(p, t, 0.8, 2 * half * 0.75, Za(s) + 0.5, slabBottom(s)), MarkingColor.Concreto, "ARCO");
        }
        // Travessa de rigidez sob o tabuleiro.
        SolidSweep.Along(geo, path, s => SolidSweep.Rect(-half * 0.8, half * 0.8, slabBottom(s) - 0.9, slabBottom(s)), MarkingColor.Concreto, ma, mb, 4, "VIGA");
    }

    private static void ArchAbove(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double half, double ma, double mb, Func<double, double> slabBottom)
    {
        var mid = (ma + mb) / 2;
        var hs = (mb - ma) / 2;
        var rise = Math.Clamp((mb - ma) / 6, 6, 60);
        double Za(double s) { var x = (s - mid) / hs; return prof.Z(s) + 0.5 + rise * (1 - x * x); }
        foreach (var side in new[] { -1, 1 })
        {
            var y = side * (half + 0.4);
            SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.45, y + 0.45, Za(s) - 0.6, Za(s) + 0.6), MarkingColor.Metal, ma, mb, 2.0, "ARCO");
            // Pendurais a cada 5 m.
            for (var s = ma + 5; s < mb - 2; s += 5)
            {
                var p = path.PointAt(s) + SolidSweep.Normal(path, s) * y;
                SolidSweep.Add(geo, SolidSweep.Rod(V(p, slabBottom(s)), V(p, Za(s) - 0.6), 0.09), MarkingColor.Metal, "PENDURAL");
            }
        }
        // Contraventamento superior (acima do gabarito).
        for (var s = ma + 10; s < mb - 5; s += 10)
        {
            if (Za(s) - prof.Z(s) < 6.5) continue;
            var n = SolidSweep.Normal(path, s);
            var p = path.PointAt(s);
            SolidSweep.Add(geo, SolidSweep.Rod(V(p - n * (half + 0.4), Za(s)), V(p + n * (half + 0.4), Za(s)), 0.35), MarkingColor.Metal, "ARCO");
        }
    }

    private static void CableStayed(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double half, double ma, double mb, Func<double, double> slabBottom,
        Func<Vec2, double> ground)
    {
        var sp = (ma + mb) / 2;
        var p = path.PointAt(sp);
        var t = path.TangentAt(sp);
        var n = t.PerpLeft;
        var deck = prof.Z(sp);
        var hp = Math.Clamp((mb - ma) * 0.22, 15, 90);
        var legOff = half + 1.8;
        // Mastro em "H": duas pernas, travessa sob o tabuleiro e travessa superior.
        foreach (var side in new[] { -1, 1 })
        {
            var c = p + n * (side * legOff);
            var g = ground(c);
            var b0 = new List<Vec3>();
            var b1 = new List<Vec3>();
            foreach (var (dx, dy) in new[] { (-1.4, -1.0), (1.4, -1.0), (1.4, 1.0), (-1.4, 1.0) })
            {
                b0.Add(V(c + t * dx + n * dy, g - 0.5));
                b1.Add(V(c + t * (dx * 0.6) + n * (dy * 0.7), deck + hp));
            }
            SolidSweep.Add(geo, SolidSweep.Prism(b0, b1), MarkingColor.Concreto, "MASTRO", isUnit: true);
        }
        SolidSweep.Add(geo, SolidSweep.Box(p, t, 2.0, 2 * legOff, slabBottom(sp) - 2.5, slabBottom(sp) - 0.8), MarkingColor.Concreto, "MASTRO");
        SolidSweep.Add(geo, SolidSweep.Box(p, t, 1.6, 2 * legOff, deck + hp - 4, deck + hp - 2.5), MarkingColor.Concreto, "MASTRO");
        // Estais em leque dos dois lados do mastro, ancorados nas bordas do tabuleiro a cada 10 m.
        foreach (var side in new[] { -1, 1 })
        {
            var top = p + n * (side * legOff);
            var k = 0;
            foreach (var dir in new[] { -1, 1 })
                for (var d = 12.0; d < (mb - ma) / 2 - 2; d += 10, k++)
                {
                    var s = sp + dir * d;
                    var anchor = path.PointAt(s) + SolidSweep.Normal(path, s) * (side * (half - 0.3));
                    var zTop = deck + hp - 5 - (d / ((mb - ma) / 2)) * hp * 0.35;
                    SolidSweep.Add(geo, SolidSweep.Rod(V(top, zTop), V(anchor, prof.Z(s) + 0.9), 0.14), MarkingColor.Metal, "ESTAI");
                }
        }
    }

    private static void Truss(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double half, double s0, double s1, Func<double, double> slabBottom, bool pedestrian)
    {
        var ht = pedestrian ? 3.2 : Math.Clamp((s1 - s0) / 10, 4.5, 9);
        var panel = Math.Max(2.5, ht);
        var n = Math.Max(2, (int)Math.Round((s1 - s0) / panel));
        foreach (var side in new[] { -1, 1 })
        {
            var y = side * (half + 0.2);
            SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.2, y + 0.2, slabBottom(s) - 0.4, slabBottom(s) + 0.1), MarkingColor.Metal, s0, s1, 4, "TRELICA");
            SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.2, y + 0.2, prof.Z(s) + ht - 0.35, prof.Z(s) + ht), MarkingColor.Metal, s0, s1, 4, "TRELICA");
            for (int i = 0; i <= n; i++)
            {
                var s = s0 + (s1 - s0) * i / n;
                var p = path.PointAt(s) + SolidSweep.Normal(path, s) * y;
                SolidSweep.Add(geo, SolidSweep.Rod(V(p, slabBottom(s)), V(p, prof.Z(s) + ht - 0.2), 0.22), MarkingColor.Metal, "TRELICA");
                if (i == n) break;
                var sn = s0 + (s1 - s0) * (i + 1) / n;
                var q = path.PointAt(sn) + SolidSweep.Normal(path, sn) * y;
                // Diagonais em "V" (Warren).
                if (i % 2 == 0) SolidSweep.Add(geo, SolidSweep.Rod(V(p, slabBottom(s)), V(q, prof.Z(sn) + ht - 0.2), 0.24), MarkingColor.Metal, "TRELICA");
                else SolidSweep.Add(geo, SolidSweep.Rod(V(p, prof.Z(s) + ht - 0.2), V(q, slabBottom(sn)), 0.24), MarkingColor.Metal, "TRELICA");
            }
        }
        // Contraventamento superior.
        for (int i = 0; i <= n; i += pedestrian ? 1 : 2)
        {
            var s = s0 + (s1 - s0) * i / n;
            var nn = SolidSweep.Normal(path, s);
            var c = path.PointAt(s);
            SolidSweep.Add(geo, SolidSweep.Rod(V(c - nn * (half + 0.2), prof.Z(s) + ht - 0.15), V(c + nn * (half + 0.2), prof.Z(s) + ht - 0.15), 0.16), MarkingColor.Metal, "TRELICA");
        }
        if (pedestrian)
        {
            // Cobertura leve da passarela.
            SolidSweep.Along(geo, path, s => SolidSweep.Rect(-half - 0.4, half + 0.4, prof.Z(s) + ht + 0.05, prof.Z(s) + ht + 0.12), MarkingColor.Vidro, s0, s1, 4, "COBERTURA");
        }
    }
}
