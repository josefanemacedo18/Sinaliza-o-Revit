using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Liga as obras de infraestrutura às vias do plugin: a via (pisos, faixas, conexões) é sempre a mesma ferramenta de vias e
/// as obras são trechos dela – ponte, viaduto, túnel ou trincheira hospedados no eixo, com o greide da via.
/// </summary>
public static class InfraRoads
{
    private static ElementoSecao E(TipoElementoSecao t, double w) => new() { Tipo = t, Largura = w };

    /// <summary>Seção de via equivalente às faixas, acostamentos e passeios informados no formulário da obra.</summary>
    public static RoadSetup Setup(int lanes, double laneWidth, double shoulder, double sidewalk, HierarquiaViaria h = HierarquiaViaria.Arterial, double speed = 60)
    {
        lanes = Math.Max(1, lanes);
        var twoWay = lanes >= 2;
        var s = new RoadSetup
        {
            Hierarchy = h, Speed = speed, TwoWay = twoWay, Center = twoWay ? (lanes >= 4 ? CenterTreatment.LFO3 : CenterTreatment.LFO2) : CenterTreatment.Nenhum,
            PhysicalElements = true, Inscriptions = false,
        };
        var right = twoWay ? (lanes + 1) / 2 : lanes;
        var left = twoWay ? lanes / 2 : 0;
        for (int i = 0; i < right; i++) s.Right.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth));
        for (int i = 0; i < left; i++) s.Left.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth));
        if (!twoWay)
        {
            // Mão única: metade das faixas de cada lado do eixo (o eixo fica no meio da pista).
            s.Right.Clear();
            var r = (lanes + 1) / 2;
            for (int i = 0; i < r; i++) s.Right.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth));
            for (int i = r; i < lanes; i++) s.Left.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth));
            if (s.Left.Count == 0) { s.Right[0].Largura = laneWidth / 2; s.Left.Add(E(TipoElementoSecao.FaixaRolamento, laneWidth / 2)); }
        }
        foreach (var side in new[] { s.Right, s.Left })
        {
            if (shoulder > 0.05) side.Add(E(TipoElementoSecao.Acostamento, shoulder));
            if (sidewalk > 0.05) side.Add(E(TipoElementoSecao.Calcada, sidewalk));
        }
        return s;
    }

    public static RoadSetup Setup(BridgeDefinition b) => b.Kind == TipoObraDeArte.Passarela
        ? PedestrianSetup(Math.Max(1.5, b.LaneWidth))
        : Setup(b.Lanes, b.LaneWidth, b.ShoulderWidth, b.SidewalkWidth, b.Hierarchy ?? HierarquiaViaria.Arterial);

    public static RoadSetup Setup(TunnelDefinition t) => Setup(t.Lanes, t.LaneWidth, t.ShoulderWidth, t.WalkwayWidth, t.Hierarchy ?? HierarquiaViaria.Arterial);
    public static RoadSetup Setup(TrenchDefinition t) => Setup(t.Lanes, t.LaneWidth, t.ShoulderWidth, 0, t.Hierarchy ?? HierarquiaViaria.Arterial);

    /// <summary>Passarela: piso de concreto contínuo, sem faixas de veículos.</summary>
    public static RoadSetup PedestrianSetup(double width)
    {
        var s = new RoadSetup { TwoWay = true, Center = CenterTreatment.Nenhum, EdgeLines = false, Inscriptions = false, Pavement = TipoPavimento.Concreto, Speed = 5 };
        s.Right.Add(E(TipoElementoSecao.FaixaRolamento, width / 2));
        s.Left.Add(E(TipoElementoSecao.FaixaRolamento, width / 2));
        return s;
    }

    /// <summary>Greide a partir de um perfil do gerador (mesmos PIVs e curva).</summary>
    public static RoadGrade FromProfile(VerticalProfile p, double curve)
    {
        var g = new RoadGrade { DefaultCurve = curve };
        foreach (var (s, z) in p.Pvis.Where(q => q.S < 1e8)) g.Points.Add(new GradePoint(s, z));
        g.Normalize();
        return g;
    }

    /// <summary>
    /// Greide de uma nova via com ponte/viaduto e o trecho em estrutura, conforme o perfil escolhido: rampas de acesso (a
    /// estrutura começa onde o aterro passaria da altura máxima), horizontal, entre margens (reta de cabeceira a cabeceira) ou
    /// convexo.
    /// </summary>
    public static (RoadGrade Grade, double S0, double S1, List<string> Warnings) BridgeGrade(BridgeDefinition b, Polyline2 path, Func<Vec2, double> ground)
    {
        var L = path.Length;
        var z0 = ground(path.PointAt(0));
        var z1 = ground(path.PointAt(L));
        var warnings = new List<string>();
        switch (b.ProfileKind)
        {
            case PerfilObra.Horizontal:
            {
                var g = new RoadGrade { DefaultCurve = b.VerticalCurve, Points = { new(0, b.Height), new(L, b.Height) } };
                return (g, 0, L, warnings);
            }
            case PerfilObra.EntreMargens:
            {
                var g = new RoadGrade { DefaultCurve = b.VerticalCurve, Points = { new(0, z0), new(L, z1) } };
                if (Math.Abs(z1 - z0) / Math.Max(1, L) > b.MaxGrade + 1e-6) warnings.Add("Rampa entre as margens acima da rampa máxima.");
                return (g, 0, L, warnings);
            }
            case PerfilObra.Convexo:
            {
                var g = new RoadGrade { DefaultCurve = Math.Max(20, L * 0.9), Points = { new(0, z0), new(L / 2, Math.Max(b.Height, Math.Max(z0, z1) + 1)), new(L, z1) } };
                var (s0, s1) = StructureRange(path, g, ground, BridgeGenerator.MaxFillHeight, Math.Max(10, b.SpanLength));
                return (g, s0, s1, warnings);
            }
            default:
            {
                var (prof, s0, s1, w) = BridgeGenerator.Profile(b, path, ground);
                return (FromProfile(prof, b.VerticalCurve), s0, s1, w);
            }
        }
    }

    /// <summary>Trecho em estrutura: onde o greide passa mais de <paramref name="maxFill"/> acima do terreno (aterro alto demais).</summary>
    public static (double S0, double S1) StructureRange(Polyline2 path, RoadGrade g, Func<Vec2, double> ground, double maxFill, double minLength)
    {
        var L = path.Length;
        double? a = null, b = null;
        for (var s = 0.0; s <= L; s += 1)
            if (g.Z(s) - ground(path.PointAt(s)) > maxFill) { a ??= s; b = s; }
        if (a == null) { var m = L / 2; return (Math.Max(0, m - minLength / 2), Math.Min(L, m + minLength / 2)); }
        var s0 = a.Value;
        var s1 = b!.Value;
        if (s1 - s0 < minLength) { var m = (s0 + s1) / 2; s0 = Math.Max(0, m - minLength / 2); s1 = Math.Min(L, s0 + minLength); }
        return (s0, s1);
    }

    /// <summary>
    /// Greide de uma via existente com uma ponte/viaduto em [s0, s1], conforme o perfil escolhido (as rampas de acesso ficam
    /// fora do trecho, até reencontrar o greide atual).
    /// </summary>
    public static RoadGrade BridgeOnRoad(RoadGrade current, BridgeDefinition b, double s0, double s1, double length, Func<double, double> groundAt)
    {
        var g = current.Clone();
        if (g.Points.Count == 0) g.Points.AddRange(new[] { new GradePoint(0, 0), new GradePoint(length, 0) });
        g.DefaultCurve = Math.Max(g.DefaultCurve, b.VerticalCurve);
        switch (b.ProfileKind)
        {
            case PerfilObra.GreideDaVia:
                return g;
            case PerfilObra.EntreMargens:
                Replace(g, s0, s1, new[] { new GradePoint(s0, groundAt(s0)), new GradePoint(s1, groundAt(s1)) });
                return g;
            case PerfilObra.Convexo:
            {
                var za = g.Z(s0);
                var zb = g.Z(s1);
                Replace(g, s0, s1, new[] { new GradePoint(s0, za), new GradePoint((s0 + s1) / 2, Math.Max(b.Height, Math.Max(za, zb) + 1), (s1 - s0) * 0.9), new GradePoint(s1, zb) });
                return g;
            }
            default:
                g.RaiseBetween(s0, s1, b.Height, b.MaxGrade, length, b.VerticalCurve);
                return g;
        }
    }

    /// <summary>Trincheira numa via existente: a pista desce <paramref name="depth"/> abaixo do terreno dentro do trecho (rampas dentro dele).</summary>
    public static RoadGrade TrenchOnRoad(RoadGrade current, double s0, double s1, double depth, double maxGrade, double curve, Func<double, double> groundAt)
    {
        var g = current.Clone();
        var za = g.Z(s0);
        var zb = g.Z(s1);
        var bottom = Math.Min(groundAt(s0), groundAt(s1)) - Math.Max(1, depth);
        var run0 = Math.Abs(za - bottom) / Math.Max(0.01, maxGrade);
        var run1 = Math.Abs(zb - bottom) / Math.Max(0.01, maxGrade);
        var k = Math.Min(1, (s1 - s0) * 0.9 / Math.Max(1e-6, run0 + run1));
        run0 *= k; run1 *= k;
        g.DefaultCurve = Math.Max(g.DefaultCurve, curve);
        Replace(g, s0, s1, new[] { new GradePoint(s0, za), new GradePoint(s0 + run0, bottom), new GradePoint(s1 - run1, bottom), new GradePoint(s1, zb) });
        return g;
    }

    /// <summary>Túnel numa via existente: mantém o greide ou faz uma rampa constante entre os emboques.</summary>
    public static RoadGrade TunnelOnRoad(RoadGrade current, double s0, double s1, bool straight)
    {
        var g = current.Clone();
        if (straight) Replace(g, s0, s1, new[] { new GradePoint(s0, g.Z(s0)), new GradePoint(s1, g.Z(s1)) });
        return g;
    }

    /// <summary>Trecho em túnel: onde a cobertura (terreno acima da coroa) passa de <paramref name="minCover"/>.</summary>
    public static (double S0, double S1)? TunnelRange(Polyline2 path, RoadGrade g, Func<Vec2, double?> ground, double crown, double minCover = 3)
    {
        var L = path.Length;
        double? a = null, b = null;
        for (var s = 0.0; s <= L; s += 1)
            if (ground(path.PointAt(s)) is { } z && z - (g.Z(s) + crown) > minCover) { a ??= s; b = s; }
        return a == null || b!.Value - a.Value < 10 ? null : (a.Value, b.Value);
    }

    private static void Replace(RoadGrade g, double s0, double s1, IEnumerable<GradePoint> inner)
    {
        g.Points.RemoveAll(p => p.S >= s0 - 0.25 && p.S <= s1 + 0.25);
        g.Points.AddRange(inner);
        g.Normalize();
    }
}
