using SinalizacaoViaria.Core.Automation;
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
    public double CrossSlope { get; init; } = 0.02;
    public bool Continuous { get; init; } = true;
    public bool VariableDepth { get; init; } = true;
    public TipoGuardaCorpo RailingStyle { get; init; } = TipoGuardaCorpo.Tubular;
    public FormaMastro Pylon { get; init; } = FormaMastro.H;
    public ArranjoEstais Stays { get; init; } = ArranjoEstais.Leque;
    public double ArchRise { get; init; } = 1.0 / 6;
    public TipoAla WingWalls { get; init; } = TipoAla.Abertas;
    public bool Drains { get; init; } = true;
    /// <summary>Terreno nativo (Toposolid): aterros e saias ficam para a terraplenagem.</summary>
    public bool Native { get; init; }

    public double BarrierWidth => Barrier == TipoGuarda.GuardaCorpoMetalico ? 0.15 : Infra.JerseyBase;
    /// <summary>Meia largura total do tabuleiro (m).</summary>
    public double DeckHalf => RoadHalf + BarrierWidth + (SidewalkWidth > 0.01 ? SidewalkWidth + 0.25 : 0.05);

    /// <summary>Superelevação (caimento único, m/m, + sobe para a esquerda); 0 = abaulamento de duas águas.</summary>
    public double Superelevation { get; init; }

    /// <summary>
    /// A via hospedeira fornece a pista, as calçadas e as faixas (pisos do Revit com o greide): a obra gera só a estrutura
    /// (tabuleiro, vigas, apoios, encontros, guarda-corpos, juntas e drenos) sob e ao lado dela.
    /// </summary>
    public bool RoadProvided { get; init; }
    /// <summary>Espessura do revestimento sobre a laje (m) – com via hospedeira, a do pavimento dela.</summary>
    public double Wear { get; init; } = Infra.Wearing;
    /// <summary>Altura do elemento mais externo da via sobre o greide (calçada), onde se apoia o guarda-corpo (m).</summary>
    public double EdgeRise { get; init; }
    /// <summary>Esconsidade dos apoios (graus): pilares e encontros paralelos ao obstáculo cruzado.</summary>
    public double Skew { get; init; }
    /// <summary>Estações dos pilares definidas pelo projetista (nulo = pelo vão).</summary>
    public IReadOnlyList<double>? PierStations { get; init; }
    /// <summary>Superfície da via hospedeira (substitui o greide + caimento próprios).</summary>
    public Func<double, double, double>? HostSurface { get; init; }

    public DeckSurface Surface(VerticalProfile prof) => HostSurface != null
        ? new DeckSurface { Profile = prof, Custom = HostSurface }
        : Math.Abs(Superelevation) > 1e-6
        ? new DeckSurface { Profile = prof, CrossSlope = Superelevation, Crown = false }
        : new DeckSurface { Profile = prof, CrossSlope = Pedestrian ? 0.01 : CrossSlope, Crown = true };
}

/// <summary>
/// Obras de arte especiais: viadutos, pontes e passarelas – greide com rampas de acesso em aterro (ou terra armada), tabuleiro
/// com caimento, balanços e pingadeiras, vigas pré-moldadas em "I" por vão com transversinas e aparelhos de apoio, viga
/// caixão de altura variável, laje, arcos, estaiada e treliça; pilares com fustes curvos exatos (circular, duplo, pórtico,
/// martelo, parede, Y, oblongo) sobre blocos; encontros com travessa, cortina e alas; juntas, buzinotes, barreiras, guarda-
/// corpos, iluminação e faixas.
/// </summary>
/// <remarks>Referências: ABNT NBR 7188 (cargas móveis), NBR 6118/7187 (concreto), DNIT 090/2006 e Manual de OAE do DNIT
/// (gabarito vertical ≥ 5,50 m sobre rodovias), NBR 9050 (passarelas: rampas ≤ 8,33 %, guarda-corpo 1,10 m).</remarks>
public static class BridgeGenerator
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static DeckSpec Spec(BridgeDefinition b, bool native = false)
    {
        var pedestrian = b.Kind == TipoObraDeArte.Passarela;
        var roadHalf = pedestrian ? Math.Max(1.2, b.LaneWidth) / 2 : b.RoadWidth / 2;
        return new DeckSpec
        {
            RoadHalf = roadHalf, SidewalkWidth = pedestrian ? 0 : b.SidewalkWidth, Lanes = pedestrian ? 0 : b.Lanes, DeckThickness = b.DeckThickness,
            GirderDepth = b.GirderDepth, GirderSpacing = b.GirderSpacing, System = b.System, PierType = b.PierType, PierSize = b.PierSize,
            SpanLength = b.SpanLength, MainSpan = b.MainSpan, Barrier = pedestrian ? TipoGuarda.GuardaCorpoMetalico : b.Barrier,
            Lighting = b.Lighting, LightSpacing = b.LightSpacing, Markings = b.LaneMarkings && !pedestrian, Pedestrian = pedestrian,
            CrossSlope = b.CrossSlope, Continuous = b.Continuous, VariableDepth = b.VariableDepth, RailingStyle = b.RailingStyle, Pylon = b.Pylon,
            Stays = b.Stays, ArchRise = b.ArchRise, WingWalls = b.WingWalls, Drains = b.Drains, Native = native,
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
        if (b.HostRoad != null) return BuildHosted(b, path, ctx, geo);
        var gp = Infra.GroundProfile(b, path, ctx);
        Func<Vec2, double> ground = p => gp.Z(IntersectionGenerator.Project(path, p).Station);
        var z0 = ground(path.PointAt(0));
        var z1 = ground(path.PointAt(L));
        var (prof, s0, s1, warnings) = Profile(b, path, ground);
        geo.Warnings.AddRange(warnings);
        var spec = Spec(b, ctx.NativeTerrain);

        if (s0 > 0.5) Approach(geo, path, prof, spec, 0, s0, ground, b.FillSlope, b.ApproachWalls);
        if (s1 < L - 0.5) Approach(geo, path, prof, spec, s1, L, ground, b.FillSlope, b.ApproachWalls);
        var info = Structure(geo, path, prof, spec, s0, s1, ground, b.FillSlope, s0 > 0.5, s1 < L - 0.5);
        if (b.Water)
        {
            var mid = (s0 + s1) / 2;
            var c = path.PointAt(mid);
            var t = path.TangentAt(mid);
            SolidSweep.Add(geo, SolidSweep.Box(c, t, Math.Min(s1 - s0, b.WaterWidth), 2 * spec.DeckHalf + 60, b.WaterLevel - 0.3, b.WaterLevel), MarkingColor.Agua, "AGUA");
        }
        if (b.Kind != TipoObraDeArte.Passarela && b.Height - info.GirderDepth - Math.Max(z0, z1) < 5.5 && !b.Water)
            geo.Warnings.Add("Confira o gabarito vertical: o DNIT exige 5,50 m livres sob viadutos rodoviários.");
        if (b.Kind == TipoObraDeArte.Passarela && b.MaxGrade > 0.0834)
            geo.Warnings.Add("Passarela: rampas acima de 8,33 % não atendem a NBR 9050.");

        geo.PathLength = L;
        geo.PaintedLength = s1 - s0;
        geo.UnitCount = info.Piers;
        Measure(geo, MarkingColor.Concreto, (s1 - s0) * 2 * spec.DeckHalf);
        return geo;
    }

    /// <summary>
    /// Obra hospedada num trecho de via do plugin: a via (pisos com o greide) passa sobre a estrutura; aqui só o tabuleiro,
    /// o sistema estrutural, pilares, encontros, guarda-corpos, juntas, drenos e iluminação – na largura e no greide da via.
    /// </summary>
    private static MarkingGeometry BuildHosted(BridgeDefinition b, Polyline2 path, BuildContext ctx, MarkingGeometry geo)
    {
        var L = path.Length;
        var host = HostRoads.Find(b.HostRoad, ctx);
        var grade = host?.Grade ?? b.Output.Grade ?? RoadGrade.Flat(L);
        var half = host?.Half ?? b.HostHalf;
        var s0 = Math.Clamp(Math.Min(b.HostStart, b.HostEnd), 0, L);
        var s1 = Math.Clamp(Math.Max(b.HostStart, b.HostEnd), 0, L);
        if (s1 - s0 < 5) { geo.Warnings.Add("Trecho da obra muito curto na via (mínimo 5 m)."); return geo; }
        var gp = Infra.GroundProfile(b, path, ctx);
        Func<Vec2, double> ground = p => gp.Z(IntersectionGenerator.Project(path, p).Station);
        var prof = grade.ToProfile(2);
        var pedestrian = b.Kind == TipoObraDeArte.Passarela;
        var spec = Spec(b, ctx.NativeTerrain) with
        {
            RoadHalf = Math.Max(1, half), SidewalkWidth = 0, Markings = false, RoadProvided = true,
            Wear = host?.Wear ?? b.HostWear, EdgeRise = host?.EdgeRise ?? b.HostEdgeRise, HostSurface = grade.Z,
            Skew = b.Skew, PierStations = b.PierStations, Lanes = pedestrian ? 0 : host?.Lanes ?? b.Lanes,
        };
        var info = Structure(geo, path, prof, spec, s0, s1, ground, b.FillSlope, s0 > 0.5, s1 < L - 0.5);
        if (b.Water)
        {
            var mid = (s0 + s1) / 2;
            SolidSweep.Add(geo, SolidSweep.Box(path.PointAt(mid), path.TangentAt(mid), Math.Min(s1 - s0, b.WaterWidth), 2 * spec.DeckHalf + 60,
                b.WaterLevel - 0.3, b.WaterLevel), MarkingColor.Agua, "AGUA");
        }
        var minClear = Enumerable.Range(0, 21).Select(i => s0 + (s1 - s0) * i / 20.0)
            .Min(s => prof.Z(s) - info.GirderDepth - spec.DeckThickness - ground(path.PointAt(s)));
        if (!pedestrian && !b.Water && minClear < 5.5 && minClear > 0)
            geo.Warnings.Add($"Gabarito sob a obra: {minClear.ToString("0.00", Pt)} m no ponto mais baixo (DNIT: 5,50 m sob viadutos rodoviários).");
        geo.PathLength = s1 - s0;
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

    // ================================================================== acessos

    /// <summary>Rampa de acesso: pavimento, base, aterro com taludes (ou muros de terra armada), barreiras e terraplenagem.</summary>
    public static void Approach(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, DeckSpec d, double s0, double s1, Func<Vec2, double> ground,
        double fillSlope, bool walls)
    {
        var half = d.DeckHalf;
        var surf = d.Surface(prof);
        Infra.Pavement(geo, path, surf, -d.RoadHalf, d.RoadHalf, s0, s1, d.Pedestrian ? MarkingColor.PavimentoConcreto : MarkingColor.Asfalto);
        Sidewalks(geo, path, surf, d, s0, s1, (s, y) => surf.Z(s, d.RoadHalf) - Infra.Wearing);
        Infra.Base(geo, path, surf, -half, half, s0, s1);
        if (walls)
        {
            // Terra armada: placas cruciformes nas bordas, do terreno à base do pavimento, com coroamento.
            foreach (var side in new[] { -1, 1 })
            {
                var y = side * (half + 0.09);
                SolidSweep.Along(geo, path, s =>
                {
                    var g = ground(path.PointAt(s) + SolidSweep.Normal(path, s) * y) - 0.6;
                    var top = surf.Z(s, d.RoadHalf) - Infra.Wearing - 0.05;
                    return top - g < 0.3 ? null : SolidSweep.Rect(y - 0.09, y + 0.09, g, top);
                }, MarkingColor.PavimentoConcreto, s0, s1, 1.5, "TERRA-ARMADA", merge: false);
                SolidSweep.Along(geo, path, s => SolidSweep.Chamfered(y - 0.16, y + 0.16, surf.Z(s, d.RoadHalf) - Infra.Wearing - 0.05, surf.Z(s, d.RoadHalf) + 0.10, 0.03),
                    MarkingColor.Concreto, s0, s1, 4, "COROAMENTO");
            }
            if (!d.Native)
                SolidSweep.Along(geo, path, s =>
                {
                    var g = ground(path.PointAt(s));
                    var top = prof.Z(s) - Infra.Wearing - 0.35;
                    return top - g < 0.1 ? null : SolidSweep.Rect(-half, half, g, top);
                }, MarkingColor.Terra, s0, s1, 6, "ATERRO");
        }
        else Infra.Embankment(geo, path, prof, -half, half, s0, s1, fillSlope, ground, native: d.Native);
        Guards(geo, path, surf, d, s0, s1);
        if (d.Markings) Infra.LaneMarkings(geo, path, surf, -d.RoadHalf, d.RoadHalf, d.Lanes, s0, s1, d.TwoWay);
        geo.Corridors.Add(Infra.Corridor(path, prof, -half - 0.3, half + 0.3, s0, s1, Infra.Wearing + 0.35, 1.0, fillSlope,
            daylightRight: !walls, daylightLeft: !walls, label: "Rampa de acesso"));
    }

    /// <summary>Passeios elevados (0,20 m) entre a barreira e a borda, com meio-fio.</summary>
    private static void Sidewalks(MarkingGeometry geo, Polyline2 path, DeckSurface surf, DeckSpec d, double s0, double s1, Func<double, double, double> bottom)
    {
        if (d.SidewalkWidth <= 0.01) return;
        foreach (var side in new[] { -1, 1 })
        {
            var a = side * (d.RoadHalf + d.BarrierWidth);
            var b = side * (d.DeckHalf - 0.05);
            SolidSweep.Along(geo, path, s =>
            {
                var top = surf.Z(s, d.RoadHalf) + 0.20;
                return SolidSweep.Rect(Math.Min(a, b), Math.Max(a, b), bottom(s, a), top);
            }, MarkingColor.PavimentoConcreto, s0, s1, 6, "PASSEIO");
        }
    }

    private static void Guards(MarkingGeometry geo, Polyline2 path, DeckSurface surf, DeckSpec d, double s0, double s1)
    {
        if (d.RoadProvided && d.EdgeRise > 0.05)
        {
            // Via com calçada sobre a obra: mureta de concreto com pingadeira na borda e guarda-corpo sobre ela.
            foreach (var side in new[] { -1, 1 })
            {
                var y0 = side * d.RoadHalf;
                var y1 = side * (d.RoadHalf + d.BarrierWidth);
                SolidSweep.Along(geo, path, s =>
                {
                    var z = surf.Z(s, y0);
                    return SolidSweep.Chamfered(Math.Min(y0, y1), Math.Max(y0, y1), z - d.Wear, z + d.EdgeRise + 0.25, 0.025);
                }, MarkingColor.Concreto, s0, s1, 4, "MURETA");
                Infra.Railing(geo, path, new DeckSurface { Profile = surf.Profile, Custom = (s, _) => surf.Z(s, y0) }, (y0 + y1) / 2, s0, s1,
                    d.EdgeRise + 0.25, 1.10 - 0.25, 2.0, d.RailingStyle);
            }
            return;
        }
        foreach (var side in new[] { -1, 1 })
        {
            if (d.Barrier != TipoGuarda.GuardaCorpoMetalico)
                Infra.NewJersey(geo, path, surf, side * (d.RoadHalf + d.BarrierWidth), -side, s0, s1);
            else
                Infra.Railing(geo, path, surf, side * (d.RoadHalf + 0.07), s0, s1, 0, 1.10, 2.0, d.RailingStyle);
            if (d.SidewalkWidth > 0.01)
                Infra.Railing(geo, path, new DeckSurface { Profile = surf.Profile, CrossSlope = 0 }, side * (d.DeckHalf - 0.12), s0, s1,
                    -surf.CrossSlope * d.RoadHalf + 0.20, 1.10, 2.0, d.RailingStyle);
            else if (d.Barrier == TipoGuarda.NewJerseyComGuardaCorpo)
                Infra.Railing(geo, path, surf, side * (d.RoadHalf + d.BarrierWidth - 0.07), s0, s1, 0.81, 0.40, 2.0, TipoGuardaCorpo.Tubular);
        }
    }

    public sealed record StructureInfo(int Piers, double GirderDepth);

    // ================================================================== estrutura

    /// <summary>Estrutura entre <paramref name="s0"/> e <paramref name="s1"/>: tabuleiro, sistema estrutural, pilares, encontros e acessórios.</summary>
    public static StructureInfo Structure(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, DeckSpec d, double s0, double s1, Func<Vec2, double> ground,
        double fillSlope = 1.5, bool fillStart = true, bool fillEnd = true)
    {
        var len = s1 - s0;
        var half = d.DeckHalf;
        var surf = d.Surface(prof);
        var arch = d.System is SistemaEstrutural.ArcoInferior or SistemaEstrutural.ArcoSuperior or SistemaEstrutural.Estaiada or SistemaEstrutural.Trelica;
        var main = arch ? Math.Clamp(d.MainSpan > 1 ? d.MainSpan : len, 10, len) : 0;
        var ma = s0 + (len - main) / 2;
        var mb = ma + main;
        var span = Math.Max(8, d.SpanLength);
        var gd = d.GirderDepth > 0.1 ? d.GirderDepth : d.System switch
        {
            SistemaEstrutural.CaixaoCelular => Math.Clamp(span / 20, 1.4, 4),
            SistemaEstrutural.LajeMacica => 0,
            SistemaEstrutural.ArcoSuperior or SistemaEstrutural.Estaiada or SistemaEstrutural.Trelica => 1.0,
            _ => Math.Clamp(span / 16, 1.0, 3.5),
        };
        var slab = d.System == SistemaEstrutural.LajeMacica ? Math.Max(d.DeckThickness, span / 22) : d.DeckThickness;
        // Apoios: pilares fora do vão principal e encontros.
        var piers = new List<double>();
        void Spans(double a, double b)
        {
            var n = Math.Max(1, (int)Math.Round((b - a) / span));
            for (int i = 1; i < n; i++) piers.Add(a + (b - a) * i / n);
        }
        if (d.PierStations is { Count: > 0 } fixedPiers) piers.AddRange(fixedPiers.Where(x => x > s0 + 3 && x < s1 - 3));
        else if (arch) { Spans(s0, ma); Spans(mb, s1); if (ma - s0 > 2) piers.Add(ma); if (s1 - mb > 2) piers.Add(mb); }
        else Spans(s0, s1);
        piers = piers.Distinct().OrderBy(x => x).ToList();
        var supports = new List<double> { s0 }.Concat(piers).Append(s1).ToList();

        double Top(double s, double y) => surf.Z(s, Math.Clamp(y, -d.RoadHalf, d.RoadHalf)) - d.Wear;
        // Balanço lateral e posição das vigas extremas.
        var cant = Math.Clamp(half * 0.22, 0.8, 2.0);
        var yRoot = half - cant;

        // Altura estrutural na estação (caixão variável: mísulas parabólicas sobre os pilares).
        double Depth(double s)
        {
            if (d.System != SistemaEstrutural.CaixaoCelular || !d.VariableDepth || piers.Count == 0) return gd;
            var dist = supports.Skip(1).SkipLast(1).Select(p => Math.Abs(s - p)).DefaultIfEmpty(1e9).Min();
            var haunch = Math.Min(span * 0.3, 25);
            if (dist >= haunch) return gd;
            var k = 1 - dist / haunch;
            return gd * (1 + 0.8 * k * k);
        }
        double SlabBottom(double s, double y) => Top(s, y) - slab;
        double Soffit(double s) => SlabBottom(s, 0) - Depth(s);

        // ---- laje do tabuleiro com balanços, abas e pingadeiras (um perfil só)
        SolidSweep.Along(geo, path, s => DeckSlab(s, half, yRoot, slab, d.RoadHalf, Top), MarkingColor.Concreto, s0, s1, 3, "TABULEIRO");
        // Cornija pré-moldada clara na face externa das abas (acabamento da borda do tabuleiro).
        foreach (var side in new[] { -1, 1 })
            SolidSweep.Along(geo, path, s =>
            {
                var te = Top(s, side * half);
                var y0 = side * half;
                var y1 = side * (half + 0.035);
                return SolidSweep.Rect(Math.Min(y0, y1), Math.Max(y0, y1), te - 0.52, te - 0.02);
            }, MarkingColor.PavimentoConcreto, s0, s1, 3, "CORNIJA");
        if (!d.RoadProvided)
        {
            Infra.Pavement(geo, path, surf, -d.RoadHalf, d.RoadHalf, s0, s1, d.Pedestrian ? MarkingColor.PavimentoConcreto : MarkingColor.Asfalto);
            Sidewalks(geo, path, surf, d, s0, s1, (s, y) => Top(s, y));
        }
        Guards(geo, path, surf, d, s0, s1);
        if (d.Markings && !d.RoadProvided) Infra.LaneMarkings(geo, path, surf, -d.RoadHalf, d.RoadHalf, d.Lanes, s0, s1, d.TwoWay);
        Infra.Joint(geo, path, surf, s0 + 0.06, -d.RoadHalf, d.RoadHalf);
        Infra.Joint(geo, path, surf, s1 - 0.06, -d.RoadHalf, d.RoadHalf);
        if (!d.Continuous) foreach (var p in piers) Infra.Joint(geo, path, surf, p, -d.RoadHalf, d.RoadHalf);
        if (d.Drains && !d.Pedestrian) Infra.Scuppers(geo, path, surf, d.RoadHalf - 0.25, s0, s1, 8, slab + d.Wear);

        // ---- superestrutura
        var bearingsAt = new List<double>();                 // laterais dos aparelhos de apoio
        var bearingH = 0.20;
        switch (d.System)
        {
            case SistemaEstrutural.VigasPreMoldadas:
            {
                var n = Math.Max(2, (int)Math.Ceiling(2 * yRoot / Math.Max(1.5, d.GirderSpacing)) + 1);
                var ys = Enumerable.Range(0, n).Select(i => -yRoot + 2 * yRoot * i / (n - 1)).ToList();
                var bf = Math.Min(1.0, 2 * yRoot / (n - 1) * 0.85);
                for (int k = 0; k + 1 < supports.Count; k++)
                {
                    var a = supports[k] + (k == 0 ? 0.45 : 0.04);
                    var b = supports[k + 1] - (k + 2 == supports.Count ? 0.45 : 0.04);
                    foreach (var y in ys)
                        SolidSweep.Along(geo, path, s => IBeam(y, SlabBottom(s, y) - 0.03, gd, bf), MarkingColor.Concreto, a, b, 3, "VIGA");
                    // Transversinas: nos apoios e no meio do vão.
                    foreach (var st in new[] { a + 0.2, (a + b) / 2, b - 0.2 })
                    {
                        var p = path.PointAt(st);
                        var t = path.TangentAt(st);
                        var zt = SlabBottom(st, 0);
                        var h = st == (a + b) / 2 ? gd * 0.6 : gd * 0.85;
                        SolidSweep.Add(geo, SolidSweep.Box(p, t, 0.25, 2 * yRoot, zt - h, zt - 0.03), MarkingColor.Concreto, "TRANSVERSINA");
                    }
                }
                bearingsAt.AddRange(ys);
                break;
            }
            case SistemaEstrutural.CaixaoCelular:
            {
                var bw = Math.Max(1.5, yRoot * 0.75);
                // Almas inclinadas e laje inferior acompanhando a altura variável.
                foreach (var side in new[] { -1.0, 1.0 })
                    SolidSweep.Along(geo, path, s =>
                    {
                        var top = SlabBottom(s, side * (bw + 0.5));
                        var bot = Soffit(s) + 0.25;
                        return new[]
                        {
                            new SectionPt(side * (bw - 0.40), bot), new SectionPt(side * bw, bot), new SectionPt(side * (bw + 0.70), top),
                            new SectionPt(side * (bw + 0.28), top),
                        };
                    }, MarkingColor.Concreto, s0, s1, 2, "CAIXAO");
                SolidSweep.Along(geo, path, s => SolidSweep.Chamfered(-bw, bw, Soffit(s), Soffit(s) + 0.25, 0.08), MarkingColor.Concreto, s0, s1, 2, "CAIXAO");
                bearingsAt.AddRange(new[] { -bw * 0.7, bw * 0.7 });
                break;
            }
            case SistemaEstrutural.LajeMacica:
                bearingsAt.AddRange(new[] { -yRoot * 0.6, 0, yRoot * 0.6 });
                break;
            case SistemaEstrutural.ArcoInferior:
                ArchBelow(geo, path, half, ma, mb, s => SlabBottom(s, 0), ground, d.ArchRise);
                EdgeBeams(geo, path, yRoot, s0, s1, s => SlabBottom(s, yRoot), 0.9);
                bearingsAt.AddRange(new[] { -yRoot, yRoot });
                break;
            case SistemaEstrutural.ArcoSuperior:
                ArchAbove(geo, path, prof, half, ma, mb, s => SlabBottom(s, half), d.ArchRise);
                EdgeBeams(geo, path, half - 0.5, s0, s1, s => SlabBottom(s, half), gd);
                bearingsAt.AddRange(new[] { -(half - 0.5), half - 0.5 });
                break;
            case SistemaEstrutural.Estaiada:
                CableStayed(geo, path, prof, half, ma, mb, s => SlabBottom(s, half), ground, d.Pylon, d.Stays);
                EdgeBeams(geo, path, half - 0.6, s0, s1, s => SlabBottom(s, half), gd);
                bearingsAt.AddRange(new[] { -(half - 0.6), half - 0.6 });
                break;
            case SistemaEstrutural.Trelica:
                Truss(geo, path, prof, half, s0, s1, s => SlabBottom(s, half), d.Pedestrian);
                bearingsAt.AddRange(new[] { -(half + 0.2), half + 0.2 });
                break;
        }

        // ---- pilares e encontros
        foreach (var s in piers)
        {
            var seat = (d.System == SistemaEstrutural.LajeMacica ? SlabBottom(s, 0) : Soffit(s)) - bearingH;
            Pier(geo, path, d, s, seat, ground, half, yRoot);
            Bearings(geo, path, s, seat, bearingsAt, !d.Continuous || d.System == SistemaEstrutural.VigasPreMoldadas, d.Skew);
        }
        foreach (var (s, inward) in new[] { (s0, 1), (s1, -1) })
        {
            var seat = (d.System == SistemaEstrutural.LajeMacica ? SlabBottom(s, 0) : Soffit(s)) - bearingH;
            Abutment(geo, path, surf, d, s, inward, seat, ground, half, fillSlope, inward > 0 ? fillStart : fillEnd);
            Bearings(geo, path, s + inward * 0.45, seat, bearingsAt, false, d.Skew);
        }

        if (d.Lighting && !d.Pedestrian)
            Infra.Lights(geo, path, surf, d.RoadHalf + d.BarrierWidth - 0.15, s0, s1, d.LightSpacing,
                zBase: d.RoadProvided && d.EdgeRise > 0.05 ? d.EdgeRise + 0.25 : 0.81);
        if (d.Lighting && d.Pedestrian) Infra.Lights(geo, path, surf, half - 0.1, s0, s1, d.LightSpacing * 0.6, height: 4.5);
        return new StructureInfo(piers.Count, gd);
    }

    /// <summary>
    /// Seção da laje: topo com caimento (plano fora da pista), balanços afinando até a borda, aba de 0,45 m e pingadeira.
    /// </summary>
    private static SectionPt[] DeckSlab(double s, double half, double yRoot, double t, double roadHalf, Func<double, double, double> top)
    {
        var pts = new List<SectionPt>();
        var ys = new List<double> { -half };
        if (roadHalf < half - 1e-6) ys.Add(-roadHalf);
        ys.Add(0);
        if (roadHalf < half - 1e-6) ys.Add(roadHalf);
        ys.Add(half);
        foreach (var y in ys) pts.Add(new SectionPt(y, top(s, y)));
        var te = top(s, half);
        // Borda direita (+): aba, pingadeira e fundo do balanço.
        pts.Add(new SectionPt(half, te - 0.45));
        pts.Add(new SectionPt(half - 0.12, te - 0.45));
        pts.Add(new SectionPt(half - 0.12, te - 0.26));
        pts.Add(new SectionPt(half - 0.17, te - 0.26));
        pts.Add(new SectionPt(half - 0.17, te - 0.22));
        pts.Add(new SectionPt(yRoot, top(s, yRoot) - t - 0.05));
        pts.Add(new SectionPt(0, top(s, 0) - t));
        pts.Add(new SectionPt(-yRoot, top(s, -yRoot) - t - 0.05));
        var tl = top(s, -half);
        pts.Add(new SectionPt(-half + 0.17, tl - 0.22));
        pts.Add(new SectionPt(-half + 0.17, tl - 0.26));
        pts.Add(new SectionPt(-half + 0.12, tl - 0.26));
        pts.Add(new SectionPt(-half + 0.12, tl - 0.45));
        pts.Add(new SectionPt(-half, tl - 0.45));
        // Topo foi de −half a +half; a sequência acima fecha o contorno pelo fundo.
        return pts.ToArray();
    }

    /// <summary>Viga pré-moldada em "I" (mesa com mísulas, alma e talão) com o topo em <paramref name="top"/>.</summary>
    private static SectionPt[] IBeam(double y, double top, double depth, double bf)
    {
        var tw = 0.10;
        var bb = 0.33;
        var half = new (double U, double V)[]
        {
            (bf / 2, 0), (bf / 2, -0.10), (bf / 2 - 0.05, -0.14), (tw + 0.02, -0.24), (tw, -0.28),
            (tw, -depth + 0.36), (bb, -depth + 0.20), (bb, -depth),
        };
        var pts = half.Select(q => new SectionPt(y + q.U, top + q.V)).ToList();
        pts.AddRange(half.Reverse().Select(q => new SectionPt(y - q.U, top + q.V)));
        return pts.ToArray();
    }

    /// <summary>Aparelhos de apoio: pedestal de concreto, neoprene fretado e chapa de aço, sob cada viga.</summary>
    private static void Bearings(MarkingGeometry geo, Polyline2 path, double s, double seat, IEnumerable<double> ys, bool pair, double skew = 0)
    {
        var p = path.PointAt(s);
        var (t, n, sk) = SkewFrame(path, s, skew);
        t = path.TangentAt(s);
        foreach (var y0 in ys)
            foreach (var dx in pair ? new[] { -0.45, 0.45 } : new[] { 0.0 })
            {
                var y = y0 * sk;
                var c = p + n * y + t * dx;
                SolidSweep.Add(geo, SolidSweep.Box(c, t, 0.60, 0.70, seat, seat + 0.10), MarkingColor.Concreto, "APOIO");
                SolidSweep.Add(geo, SolidSweep.Box(c, t, 0.40, 0.50, seat + 0.10, seat + 0.17), MarkingColor.Preta, "APOIO");
                SolidSweep.Add(geo, SolidSweep.Box(c, t, 0.44, 0.54, seat + 0.17, seat + 0.20), MarkingColor.Metal, "APOIO");
            }
    }

    private static void EdgeBeams(MarkingGeometry geo, Polyline2 path, double y, double s0, double s1, Func<double, double> slabBottom, double gd)
    {
        foreach (var side in new[] { -1, 1 })
            SolidSweep.Along(geo, path, s => SolidSweep.Chamfered(side * y - 0.45, side * y + 0.45, slabBottom(s) - gd, slabBottom(s), 0.05),
                MarkingColor.Concreto, s0, s1, 3, "VIGA");
    }

    // ================================================================== pilares

    private static void Pier(MarkingGeometry geo, Polyline2 path, DeckSpec d, double s, double top, Func<Vec2, double> ground, double half, double yRoot)
    {
        PierBody(geo, path, d, s, top, ground, half, yRoot);
        // Tubo de descida de águas do tabuleiro junto ao pilar, com curva no pé e caixa de passagem.
        var pp = path.PointAt(s);
        var (tt, nn, _) = SkewFrame(path, s, d.Skew);
        var gg = ground(pp);
        if (top - gg > 2 && d.Drains)
        {
            var dp = pp + tt * (Math.Max(0.4, d.PierSize) / 2 + 0.25) + nn * (Math.Max(0.4, d.PierSize) * 0.2);
            SolidSweep.AddRound(geo, Vec3.At(dp, gg + 0.3), Vec3.At(dp, top - 0.2), 0.075, 0.075, MarkingColor.Metal, "DRENO", false, 0.065, 0.065, 16);
            SolidSweep.Add(geo, SolidSweep.Box(dp, tt, 0.6, 0.6, gg - 0.5, gg + 0.3), MarkingColor.Concreto, "DRENO");
        }
    }

    private static void PierBody(MarkingGeometry geo, Polyline2 path, DeckSpec d, double s, double top, Func<Vec2, double> ground, double half, double yRoot)
    {
        var p = path.PointAt(s);
        var (t, n, sk) = SkewFrame(path, s, d.Skew);
        var g = ground(p);
        if (top - g < 0.8) return;
        var size = Math.Max(0.4, d.PierSize);
        var capH = Math.Clamp(size * 1.1, 1.0, 2.2);
        var capW = Math.Max(1.4, size + 0.3);
        var capLen = (2 * yRoot + 1.2) * sk;
        yRoot *= sk;
        var bottom = g - 0.3;
        // Bloco de fundação (topo 0,3 m abaixo do terreno).
        SolidSweep.Add(geo, SolidSweep.Box(p, t, capW + 1.6, Math.Min(capLen, size * 3 + (d.PierType is TipoPilar.Portico or TipoPilar.DuplaCircular ? capLen * 0.7 : 0)),
            g - 1.8, g - 0.3), MarkingColor.Concreto, "FUNDACAO");
        // Travessa com as pontas chanfradas (em martelo quando o fuste é único).
        void Cap(double lengthAcross, double bottomWidth)
        {
            var sec = new[]
            {
                new SectionPt(-lengthAcross / 2, top), new SectionPt(lengthAcross / 2, top), new SectionPt(lengthAcross / 2, top - capH * 0.40),
                new SectionPt(bottomWidth / 2, top - capH), new SectionPt(-bottomWidth / 2, top - capH), new SectionPt(-lengthAcross / 2, top - capH * 0.40),
            };
            SolidSweep.Add(geo, SolidSweep.Extrude(sec, p - t * (capW / 2), t, capW), MarkingColor.Concreto, "PILAR");
        }
        switch (d.PierType)
        {
            case TipoPilar.Circular:
                SolidSweep.AddColumn(geo, p, size / 2, size / 2, bottom, top - capH, MarkingColor.Concreto, "PILAR", true);
                Cap(capLen, Math.Max(size + 0.4, capLen * 0.55));
                break;
            case TipoPilar.Martelo:
                SolidSweep.AddColumn(geo, p, size / 2 * 1.08, size / 2, bottom, top - capH, MarkingColor.Concreto, "PILAR", true);
                Cap(capLen, size + 0.3);
                break;
            case TipoPilar.Oblongo:
            case TipoPilar.Parede:
            {
                var across = d.PierType == TipoPilar.Parede ? capLen * 0.75 : Math.Max(size * 2.2, capLen * 0.35);
                var th = size;
                var plan = RoundedPlan(p, t, th, across, th / 2 * 0.98);
                SolidSweep.Add(geo, Polyhedron.Prism(plan, _ => bottom, _ => top - (d.PierType == TipoPilar.Parede ? 0.6 : capH)), MarkingColor.Concreto, "PILAR", true);
                if (d.PierType == TipoPilar.Parede)
                    SolidSweep.Add(geo, SolidSweep.Extrude(SolidSweep.Chamfered(-capLen / 2, capLen / 2, top - 0.6, top, 0.1), p - t * (capW / 2), t, capW), MarkingColor.Concreto, "PILAR");
                else Cap(capLen, across + 0.2);
                break;
            }
            case TipoPilar.Y:
            {
                var fork = bottom + (top - capH - bottom) * 0.55;
                SolidSweep.AddColumn(geo, p, size / 2, size / 2 * 0.9, bottom, fork, MarkingColor.Concreto, "PILAR", true);
                foreach (var side in new[] { -1.0, 1.0 })
                {
                    var head = p + n * (side * yRoot * 0.55);
                    SolidSweep.AddRound(geo, Vec3.At(p, fork - 0.3), Vec3.At(head, top - capH + 0.2), size / 2 * 0.8, size / 2 * 0.65, MarkingColor.Concreto, "PILAR");
                }
                Cap(capLen, capLen * 0.8);
                break;
            }
            case TipoPilar.DuplaCircular:
            case TipoPilar.Portico:
            default:
            {
                var off = yRoot * 0.62;
                foreach (var side in new[] { -1, 1 })
                {
                    var c = p + n * (side * off);
                    var gc = ground(c) - 0.3;
                    if (d.PierType == TipoPilar.DuplaCircular) SolidSweep.AddColumn(geo, c, size / 2, size / 2, gc, top - capH, MarkingColor.Concreto, "PILAR", true);
                    else SolidSweep.Add(geo, Polyhedron.Prism(RoundedPlan(c, t, size, size, 0.08), _ => gc, _ => top - capH), MarkingColor.Concreto, "PILAR", true);
                }
                Cap(capLen, capLen - 0.6);
                // Viga de travamento entre as colunas altas.
                if (top - g > 12)
                    SolidSweep.Add(geo, SolidSweep.Box(p, t, size * 0.8, 2 * off, (g + top) / 2 - 0.5, (g + top) / 2 + 0.5), MarkingColor.Concreto, "PILAR");
                break;
            }
        }
    }

    /// <summary>
    /// Referencial do apoio com esconsidade: t = direção "ao longo" da travessa (perpendicular à linha dos apoios), n = linha dos
    /// apoios (girada de <paramref name="skewDeg"/> em relação à normal do eixo) e o fator 1/cos para alongar as travessas.
    /// </summary>
    private static (Vec2 T, Vec2 N, double K) SkewFrame(Polyline2 path, double s, double skewDeg)
    {
        var t = path.TangentAt(s);
        var a = Math.Clamp(skewDeg, -60, 60) * Math.PI / 180;
        if (Math.Abs(a) < 1e-4) return (t, t.PerpLeft, 1);
        var n = t.PerpLeft.Rotate(a);
        return (n.PerpRight, n, 1 / Math.Cos(a));
    }

    /// <summary>Contorno em planta de um retângulo de cantos arredondados (espessura ao longo de t, largura na transversal).</summary>
    private static List<Vec2> RoundedPlan(Vec2 c, Vec2 t, double along, double across, double r)
    {
        var n = t.PerpLeft;
        var sec = SolidSweep.RoundedRect(-along / 2, along / 2, -across / 2, across / 2, r, 6);
        return sec.Select(q => c + t * q.Y + n * q.Z).ToList();
    }

    // ================================================================== encontros

    /// <summary>
    /// Encontro: travessa de apoio, cortina (espelho), alas paralelas ou abertas acompanhando o aterro, laje de transição e,
    /// sem terreno nativo, a saia de aterro diante do encontro.
    /// </summary>
    private static void Abutment(MarkingGeometry geo, Polyline2 path, DeckSurface surf, DeckSpec d, double s, int inward, double seat,
        Func<Vec2, double> ground, double half, double fillSlope, bool hasFill)
    {
        var p = path.PointAt(s);
        var (t, n, sk) = SkewFrame(path, s, d.Skew);
        var g = ground(p);
        var deckTop = surf.Z(s, 0);
        if (seat - g < 0.3) return;
        var back = -path.TangentAt(s) * inward;               // para dentro do aterro
        var width = (2 * half + 0.3) * sk;
        half *= sk;
        // Travessa de apoio (viga do encontro) e cortina.
        SolidSweep.Add(geo, SolidSweep.Extrude(SolidSweep.Chamfered(-width / 2, width / 2, seat - 1.2, seat, 0.08), p + back * 1.6, t * inward, 1.6), MarkingColor.Concreto, "ENCONTRO");
        SolidSweep.Add(geo, SolidSweep.Box(p + back * 1.45, t, 0.30, width, seat, deckTop - d.Wear), MarkingColor.Concreto, "ENCONTRO");
        // Parede frontal/pilares enterrados até a fundação.
        SolidSweep.Add(geo, SolidSweep.Box(p + back * 0.9, t, 1.0, width - 0.4, g - 1.5, seat - 1.2), MarkingColor.Concreto, "ENCONTRO");
        // Laje de transição sob o pavimento (acompanha o greide e o caimento).
        var sa = Math.Clamp(s - inward * 7.6, 0, path.Length);
        var sb = Math.Clamp(s - inward * 1.6, 0, path.Length);
        if (Math.Abs(sb - sa) > 0.5)
            SolidSweep.Along(geo, path, x => Infra.Band(-d.RoadHalf, d.RoadHalf, y => surf.Z(x, y) - d.Wear - 0.05, 0.30),
                MarkingColor.Concreto, Math.Min(sa, sb), Math.Max(sa, sb), 3, "ENCONTRO");
        // Alas.
        var h = deckTop - g;
        var wingLen = Math.Clamp(h * Math.Max(1, fillSlope) * 0.9, 2.5, 14);
        foreach (var side in new[] { -1, 1 })
        {
            var root = p + back * 0.3 + n * (side * (half + 0.15));
            var dir = d.WingWalls == TipoAla.Paralelas ? back : (back + n * (side * 0.7)).Normalized();
            var ortho = dir.PerpLeft;
            var th = 0.35;
            var plan = new List<Vec2> { root - ortho * (th / 2), root + dir * wingLen - ortho * (th / 2), root + dir * wingLen + ortho * (th / 2), root + ortho * (th / 2) };
            double TopAt(Vec2 q)
            {
                // Paralelas: topo 0,3 m acima do greide; abertas: descem com o talude do aterro (1 : talude) à medida que se
                // afastam da borda do tabuleiro, até 0,5 m acima do terreno.
                if (d.WingWalls == TipoAla.Paralelas) return deckTop + 0.3;
                var lateral = Math.Max(0, (q - root).Dot(n * side));
                return Math.Max(g + 0.5, deckTop + 0.3 - lateral / Math.Max(0.5, fillSlope));
            }
            SolidSweep.Add(geo, Polyhedron.Prism(plan, _ => g - 0.8, TopAt), MarkingColor.Concreto, "ALA");
        }
        // Saia de aterro diante do encontro (sem Toposolid).
        if (hasFill && !d.Native && h > 0.5)
        {
            var reach = h * fillSlope;
            var cone = new Polyline2(new[] { p + back * 0.4, p - back * reach });
            SolidSweep.Along(geo, cone, x =>
            {
                var top = seat - 1.2 - x / Math.Max(0.5, fillSlope);
                var gg = ground(cone.PointAt(x));
                var hh = top - gg;
                if (hh < 0.05) return null;
                return SolidSweep.Trapezoid(-half - fillSlope * hh, half + fillSlope * hh, gg, -half, half, top);
            }, MarkingColor.Terra, 0, double.NaN, 1.0, "SAIA");
        }
    }

    // ================================================================== sistemas especiais

    private static void ArchBelow(MarkingGeometry geo, Polyline2 path, double half, double ma, double mb, Func<double, double> slabBottom,
        Func<Vec2, double> ground, double riseRatio)
    {
        var mid = (ma + mb) / 2;
        var hs = (mb - ma) / 2;
        var spring = Math.Min(ground(path.PointAt(ma)), ground(path.PointAt(mb)));
        var crown = Math.Min(slabBottom(mid) - 0.9, spring + Math.Max(4, (mb - ma) * Math.Clamp(riseRatio, 0.08, 0.5)));
        if (crown - spring < 2) return;
        double Za(double s) { var x = (s - mid) / hs; return spring + (crown - spring) * (1 - x * x); }
        double Th(double s) { var x = Math.Abs(s - mid) / hs; return 0.9 + 0.5 * x * x; }       // mais espesso nas impostas
        var ribY = half * 0.6;
        foreach (var side in new[] { -1, 1 })
        {
            var y = side * ribY;
            SolidSweep.Along(geo, path, s => SolidSweep.RoundedRect(y - 0.7, y + 0.7, Za(s) - Th(s) / 2, Za(s) + Th(s) / 2, 0.15, 3), MarkingColor.Concreto, ma, mb, 1.0, "ARCO");
            // Impostas (blocos de arranque).
            foreach (var sa in new[] { ma, mb })
                SolidSweep.Add(geo, SolidSweep.Box(path.PointAt(sa) + SolidSweep.Normal(path, sa) * y, path.TangentAt(sa), 3.0, 2.2, spring - 2.5, spring + 0.8), MarkingColor.Concreto, "ARCO");
        }
        // Montantes circulares e travessas entre as nervuras.
        var k = Math.Max(4, (int)Math.Round((mb - ma) / 7));
        for (int i = 1; i < k; i++)
        {
            var s = ma + (mb - ma) * i / k;
            var zb = Za(s) + Th(s) / 2;
            var zt = slabBottom(s) - 0.9;
            var p = path.PointAt(s);
            var n = SolidSweep.Normal(path, s);
            if (zt - zb < 0.6)
            {
                SolidSweep.Add(geo, SolidSweep.Box(p, path.TangentAt(s), 0.8, 2 * ribY + 1.4, zb - 0.2, slabBottom(s) - 0.05), MarkingColor.Concreto, "ARCO");
                continue;
            }
            foreach (var side in new[] { -1, 1 })
                SolidSweep.AddColumn(geo, p + n * (side * ribY), 0.40, 0.40, zb - 0.2, zt, MarkingColor.Concreto, "ARCO");
            SolidSweep.Add(geo, SolidSweep.Box(p, path.TangentAt(s), 0.9, 2 * ribY + 1.4, zt, slabBottom(s) - 0.05), MarkingColor.Concreto, "ARCO");
            if (i % 2 == 0) SolidSweep.Add(geo, SolidSweep.Box(p, path.TangentAt(s), 0.5, 2 * ribY, Za(s) - 0.3, Za(s) + 0.3), MarkingColor.Concreto, "ARCO");
        }
    }

    private static void ArchAbove(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double half, double ma, double mb, Func<double, double> slabBottom, double riseRatio)
    {
        var mid = (ma + mb) / 2;
        var hs = (mb - ma) / 2;
        var rise = Math.Clamp((mb - ma) * Math.Clamp(riseRatio, 0.08, 0.4), 6, 70);
        double Za(double s) { var x = (s - mid) / hs; return prof.Z(s) + 0.6 + rise * (1 - x * x); }
        foreach (var side in new[] { -1, 1 })
        {
            // Arcos levemente inclinados para dentro ("alça de cesto").
            double Y(double s) { var x = (s - mid) / hs; return side * (half + 0.4 - 1.2 * (1 - x * x)); }
            SolidSweep.Along(geo, path, s => SolidSweep.Chamfered(Y(s) - 0.45, Y(s) + 0.45, Za(s) - 0.7, Za(s) + 0.7, 0.08), MarkingColor.Metal, ma, mb, 1.0, "ARCO");
            for (var s = ma + 5; s < mb - 2; s += 5)
            {
                var p = path.PointAt(s);
                var n = SolidSweep.Normal(path, s);
                SolidSweep.AddRound(geo, Vec3.At(p + n * (side * (half - 0.5)), slabBottom(s) + 0.5), Vec3.At(p + n * Y(s), Za(s) - 0.7), 0.05, 0.05, MarkingColor.Metal, "PENDURAL", n: 12);
            }
        }
        // Contraventamento superior em "K" acima do gabarito.
        for (var s = ma + 10; s < mb - 5; s += 10)
        {
            if (Za(s) - prof.Z(s) < 6.5) continue;
            var n = SolidSweep.Normal(path, s);
            var p = path.PointAt(s);
            var x = (s - mid) / hs;
            var w = half + 0.4 - 1.2 * (1 - x * x);
            SolidSweep.Add(geo, SolidSweep.Rod(Vec3.At(p - n * w, Za(s)), Vec3.At(p + n * w, Za(s)), 0.40), MarkingColor.Metal, "ARCO");
            var sn = Math.Min(mb - 5, s + 5);
            var q = path.PointAt(sn);
            var xn = (sn - mid) / hs;
            var wn = half + 0.4 - 1.2 * (1 - xn * xn);
            SolidSweep.Add(geo, SolidSweep.Rod(Vec3.At(p - n * w, Za(s)), Vec3.At(q + n * wn, Za(sn)), 0.22), MarkingColor.Metal, "ARCO");
        }
    }

    private static void CableStayed(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double half, double ma, double mb, Func<double, double> slabBottom,
        Func<Vec2, double> ground, FormaMastro shape, ArranjoEstais layout)
    {
        var sp = (ma + mb) / 2;
        var p = path.PointAt(sp);
        var t = path.TangentAt(sp);
        var n = t.PerpLeft;
        var deck = prof.Z(sp);
        var hp = Math.Clamp((mb - ma) * 0.22, 15, 90);
        var legOff = half + 1.8;
        var g = ground(p);
        var anchors = new List<(Vec2 Top, double Z0, double Z1, int Side)>();
        if (shape == FormaMastro.Central)
        {
            // Mastro único no canteiro central (estais num só plano).
            SolidSweep.Add(geo, Polyhedron.Prism(RoundedPlan(p, t, 3.2, 2.4, 0.5), _ => g - 0.5, _ => deck - 1.5), MarkingColor.Concreto, "MASTRO", true);
            SolidSweep.Add(geo, SolidSweep.Extrude(SolidSweep.RoundedRect(-1.0, 1.0, deck - 1.5, deck + hp, 0.4, 3).Select(q => q).ToArray(), p - t * 1.3, t, 2.6), MarkingColor.Concreto, "MASTRO");
            anchors.Add((p, deck + hp * 0.55, deck + hp - 3, 0));
        }
        else
        {
            foreach (var side in new[] { -1, 1 })
            {
                var c = p + n * (side * legOff);
                var headOff = shape == FormaMastro.A ? 0.6 : legOff;
                var topC = p + n * (side * headOff);
                var b0 = new List<Vec3>();
                var b1 = new List<Vec3>();
                foreach (var (dx, dy) in new[] { (-1.6, -1.1), (1.6, -1.1), (1.6, 1.1), (-1.6, 1.1) })
                {
                    b0.Add(Vec3.At(c + t * dx + n * dy, ground(c) - 0.5));
                    b1.Add(Vec3.At(topC + t * (dx * 0.6) + n * (dy * 0.6), deck + hp));
                }
                SolidSweep.Add(geo, SolidSweep.Prism(b0, b1), MarkingColor.Concreto, "MASTRO", true);
                anchors.Add((topC, deck + hp * (shape == FormaMastro.A ? 0.7 : 0.55), deck + hp - 3, side));
            }
            SolidSweep.Add(geo, SolidSweep.Box(p, t, 2.2, 2 * legOff + 2, slabBottom(sp) - 2.8, slabBottom(sp) - 0.6), MarkingColor.Concreto, "MASTRO");
            if (shape == FormaMastro.H)
                SolidSweep.Add(geo, SolidSweep.Box(p, t, 1.8, 2 * legOff, deck + hp - 4.5, deck + hp - 2.8), MarkingColor.Concreto, "MASTRO");
            else
                SolidSweep.Add(geo, SolidSweep.Box(p, t, 2.2, 3.0, deck + hp - 2, deck + hp + 1.5), MarkingColor.Concreto, "MASTRO");
        }
        // Estais (cabos) em leque ou em harpa, ancorados nas bordas do tabuleiro a cada 8 m.
        var reach = (mb - ma) / 2 - 2;
        var count = Math.Max(1, (int)((reach - 12) / 8) + 1);
        foreach (var (top, z0, z1, side) in anchors)
            foreach (var dir in new[] { -1, 1 })
                for (int i = 0; i < count; i++)
                {
                    var dd = 12 + i * 8.0;
                    if (dd > reach) break;
                    var s = sp + dir * dd;
                    var y = side == 0 ? 0 : side * (half - 0.3);
                    var anchor = path.PointAt(s) + SolidSweep.Normal(path, s) * y;
                    var zTop = layout == ArranjoEstais.Harpa ? z0 + (z1 - z0) * i / Math.Max(1, count - 1) : z1 - i * Math.Min(0.6, (z1 - z0) / count);
                    var zDeck = prof.Z(s) + (side == 0 ? 1.2 : 0.9);
                    SolidSweep.AddRound(geo, Vec3.At(top + t * (dir * 0.8), zTop), Vec3.At(anchor, zDeck), 0.07, 0.07, MarkingColor.Metal, "ESTAI", n: 10);
                    SolidSweep.AddRound(geo, Vec3.At(anchor, zDeck - 0.9), Vec3.At(anchor, zDeck + 0.3), 0.14, 0.14, MarkingColor.Metal, "ESTAI", n: 12);
                }
        if (shape == FormaMastro.Central)
            SolidSweep.Along(geo, path, s => JerseyMedian(prof.Z(s)), MarkingColor.Concreto, ma, mb, 4, "BARREIRA");
    }

    private static SectionPt[] JerseyMedian(double z) => Infra.JerseyDouble.Select(q => new SectionPt(q.Q, z + q.Z)).ToArray();

    private static void Truss(MarkingGeometry geo, Polyline2 path, VerticalProfile prof, double half, double s0, double s1, Func<double, double> slabBottom, bool pedestrian)
    {
        var ht = pedestrian ? 3.2 : Math.Clamp((s1 - s0) / 10, 4.5, 9);
        var panel = Math.Max(2.5, ht);
        var n = Math.Max(2, (int)Math.Round((s1 - s0) / panel));
        foreach (var side in new[] { -1, 1 })
        {
            var y = side * (half + 0.2);
            SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.22, y + 0.22, slabBottom(s) - 0.45, slabBottom(s) + 0.1), MarkingColor.Metal, s0, s1, 4, "TRELICA");
            SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.22, y + 0.22, prof.Z(s) + ht - 0.40, prof.Z(s) + ht), MarkingColor.Metal, s0, s1, 4, "TRELICA");
            for (int i = 0; i <= n; i++)
            {
                var s = s0 + (s1 - s0) * i / n;
                var p = path.PointAt(s) + SolidSweep.Normal(path, s) * y;
                SolidSweep.Add(geo, SolidSweep.Rod(Vec3.At(p, slabBottom(s)), Vec3.At(p, prof.Z(s) + ht - 0.2), 0.24), MarkingColor.Metal, "TRELICA");
                if (i == n) break;
                var sn = s0 + (s1 - s0) * (i + 1) / n;
                var q = path.PointAt(sn) + SolidSweep.Normal(path, sn) * y;
                if (i % 2 == 0) SolidSweep.Add(geo, SolidSweep.Rod(Vec3.At(p, slabBottom(s)), Vec3.At(q, prof.Z(sn) + ht - 0.2), 0.26), MarkingColor.Metal, "TRELICA");
                else SolidSweep.Add(geo, SolidSweep.Rod(Vec3.At(p, prof.Z(s) + ht - 0.2), Vec3.At(q, slabBottom(sn)), 0.26), MarkingColor.Metal, "TRELICA");
            }
        }
        for (int i = 0; i <= n; i += pedestrian ? 1 : 2)
        {
            var s = s0 + (s1 - s0) * i / n;
            var nn = SolidSweep.Normal(path, s);
            var c = path.PointAt(s);
            SolidSweep.Add(geo, SolidSweep.Rod(Vec3.At(c - nn * (half + 0.2), prof.Z(s) + ht - 0.15), Vec3.At(c + nn * (half + 0.2), prof.Z(s) + ht - 0.15), 0.18), MarkingColor.Metal, "TRELICA");
            SolidSweep.Add(geo, SolidSweep.Rod(Vec3.At(c - nn * (half + 0.2), slabBottom(s) - 0.3), Vec3.At(c + nn * (half + 0.2), slabBottom(s) - 0.3), 0.30), MarkingColor.Metal, "TRELICA");
        }
        if (pedestrian)
        {
            // Cobertura curva translúcida sobre a passarela.
            SolidSweep.Along(geo, path, s =>
            {
                var z = prof.Z(s) + ht + 0.05;
                var pts = new List<SectionPt>();
                for (int k = 0; k <= 8; k++)
                {
                    var a = Math.PI * k / 8;
                    pts.Add(new SectionPt(-(half + 0.45) * Math.Cos(a), z + 0.6 * Math.Sin(a)));
                }
                for (int k = 8; k >= 0; k--)
                {
                    var a = Math.PI * k / 8;
                    pts.Add(new SectionPt(-(half + 0.39) * Math.Cos(a), z + 0.6 * Math.Sin(a) - 0.05));
                }
                return pts;
            }, MarkingColor.Vidro, s0, s1, 4, "COBERTURA");
        }
    }
}
