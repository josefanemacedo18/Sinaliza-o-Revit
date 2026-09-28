using SinalizacaoViaria.Core.Automation;
using System.Globalization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Túneis, trincheiras, muros de arrimo e taludes – as obras que conversam com a topografia: geram os sólidos e as faixas de
/// terraplenagem para ajustar o Toposolid (corte, aterro, reaterro de muros e emboques).
/// </summary>
/// <remarks>Referências: DNIT – Manual de Implantação Básica de Rodovia e Manual de Drenagem de Rodovias (canaletas, descidas
/// d'água, bermas); ABNT NBR 11682 (estabilidade de encostas), NBR 9286 (terra armada), NBR 5629 (tirantes); gabarito
/// vertical de túneis ≥ 5,50 m.</remarks>
public static class EarthworksGenerator
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    // ================================================================== túnel

    /// <summary>Contorno interno do túnel (meia seção, do piso à coroa) e anéis do revestimento em partes convexas.</summary>
    public static (List<SectionPt[]> Lining, double Crown, double HalfClear) TunnelSection(TunnelDefinition d)
    {
        var halfClear = d.RoadWidth / 2 + Math.Max(0, d.WalkwayWidth) + 0.10;
        var t = Math.Max(0.2, d.LiningThickness);
        var hc = Math.Max(4.0, d.ClearHeight) + 0.50;           // gabarito + folga para equipamentos
        var bottom = -Infra.Wearing - 0.35 - Math.Max(0.2, d.InvertThickness);
        var parts = new List<SectionPt[]>();
        double crown;
        switch (d.Section)
        {
            case SecaoTunel.Circular:
            {
                // Círculo que passa pelas bordas do piso e deixa o gabarito na coroa; anel em duas metades (sem furo).
                var dd = (hc * hc - halfClear * halfClear) / (2 * hc);
                var r = hc - dd;
                var cz = dd;
                crown = cz + r;
                const int n = 48;
                foreach (var (a0, a1) in new[] { (-Math.PI / 2, Math.PI / 2), (Math.PI / 2, 1.5 * Math.PI) })
                {
                    var pts = new List<SectionPt>();
                    for (int i = 0; i <= n / 2; i++) { var a = a0 + (a1 - a0) * i / (n / 2); pts.Add(new SectionPt(Math.Cos(a) * (r + t), cz + Math.Sin(a) * (r + t))); }
                    for (int i = n / 2; i >= 0; i--) { var a = a0 + (a1 - a0) * i / (n / 2); pts.Add(new SectionPt(Math.Cos(a) * r, cz + Math.Sin(a) * r)); }
                    parts.Add(pts.ToArray());
                }
                // Enchimento sob o piso (segmento circular).
                var fill = new List<SectionPt>();
                var zRoadBase = -Infra.Wearing - 0.35;
                for (int i = 0; i <= 32; i++)
                {
                    var a = Math.PI + Math.PI * i / 32;
                    var p = new SectionPt(Math.Cos(a) * r, cz + Math.Sin(a) * r);
                    if (p.Z <= zRoadBase) fill.Add(p);
                }
                if (fill.Count >= 2)
                {
                    fill.Add(new SectionPt(fill[^1].Y, zRoadBase));
                    fill.Add(new SectionPt(fill[0].Y, zRoadBase));
                    parts.Add(fill.ToArray());
                }
                break;
            }
            case SecaoTunel.Retangular:
            {
                crown = hc;
                parts.Add(SolidSweep.Rect(-halfClear - t, -halfClear, bottom, hc));
                parts.Add(SolidSweep.Rect(halfClear, halfClear + t, bottom, hc));
                parts.Add(SolidSweep.Rect(-halfClear - t, halfClear + t, hc, hc + t + 0.1));
                parts.Add(SolidSweep.Rect(-halfClear - t, halfClear + t, bottom - t, bottom));
                break;
            }
            default:
            {
                // Ferradura: paredes até a linha de arranque e abóbada semicircular num só perfil em "C"; laje de fundo.
                var r = halfClear;
                var spring = Math.Max(1.5, hc - r);
                crown = spring + r;
                const int n = 32;
                var pts = new List<SectionPt> { new(-halfClear - t, bottom), new(-halfClear, bottom) };
                for (int i = n; i >= 0; i--) { var a = Math.PI * i / n; pts.Add(new SectionPt(Math.Cos(a) * r, spring + Math.Sin(a) * r)); }
                pts.Add(new SectionPt(halfClear, bottom));
                pts.Add(new SectionPt(halfClear + t, bottom));
                for (int i = 0; i <= n; i++) { var a = Math.PI * i / n; pts.Add(new SectionPt(Math.Cos(a) * (r + t), spring + Math.Sin(a) * (r + t))); }
                // A sequência externa vai de +y para −y pelo topo: fecha o "C".
                parts.Add(pts.ToArray());
                parts.Add(SolidSweep.Rect(-halfClear - t, halfClear + t, bottom - t, bottom));
                break;
            }
        }
        return (parts, crown, halfClear);
    }

    /// <summary>
    /// Trecho hospedado numa via: sub-eixo [início, fim], greide da via reamostrado a partir do início e a definição ajustada
    /// à seção da via (a pista, as calçadas e as faixas são dela).
    /// </summary>
    private static (Polyline2 Path, VerticalProfile Profile, double Half, double EdgeRise)? Hosted(IHostedStructure h, MarkingDefinition def, Polyline2 path, BuildContext ctx)
    {
        if (h.HostRoad == null) return null;
        var host = HostRoads.Find(h.HostRoad, ctx);
        var grade = host?.Grade ?? def.Output.Grade ?? RoadGrade.Flat(path.Length);
        var s0 = Math.Clamp(Math.Min(h.HostStart, h.HostEnd), 0, path.Length);
        var s1 = Math.Clamp(Math.Max(h.HostStart, h.HostEnd), 0, path.Length);
        if (s1 - s0 < 5) return null;
        var sub = new Polyline2(path.SubPoints(s0, s1));
        var prof = new VerticalProfile();
        for (var s = s0; s < s1; s += 2) prof.Pvis.Add((s - s0, grade.Z(s)));
        prof.Pvis.Add((s1 - s0, grade.Z(s1)));
        return (sub, prof, host?.Half ?? h.HostHalf, host?.EdgeRise ?? h.HostEdgeRise);
    }

    public static MarkingGeometry Tunnel(TunnelDefinition d, Polyline2 path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var hostedInfo = Hosted(d, d, path, ctx);
        var hosted = hostedInfo != null;
        var edgeRise = 0.0;
        VerticalProfile prof;
        if (hostedInfo is { } hi)
        {
            path = hi.Path;
            prof = hi.Profile;
            edgeRise = hi.EdgeRise;
            // Seção pela via: a largura livre cobre a seção inteira dela (as calçadas viram passeios de serviço).
            var c = (TunnelDefinition)d.CloneWithNewId();
            c.Lanes = 1; c.LaneWidth = 2 * Math.Max(1, hi.Half); c.ShoulderWidth = 0; c.WalkwayWidth = 0; c.GroundLine = null; c.WalkwayHeight = Math.Max(0.1, edgeRise);
            d = c;
        }
        else prof = VerticalProfile.Linear(path.Length, d.StartZ, d.EndZ);
        var L = path.Length;
        if (L < 5) { geo.Warnings.Add("Eixo do túnel muito curto."); return geo; }
        if (prof.MaxGrade > 0.06) geo.Warnings.Add($"Rampa de {(prof.MaxGrade * 100).ToString("0.0", Pt)} % no túnel – usual ≤ 5 % (ventilação e segurança).");
        var (parts, crown, halfClear) = TunnelSection(d);
        var t = Math.Max(0.2, d.LiningThickness);
        foreach (var part in parts)
            SolidSweep.Along(geo, path, s => part.Select(q => new SectionPt(q.Y, q.Z + prof.Z(s))).ToList(), MarkingColor.Concreto, 0, L, 5, "REVESTIMENTO");
        // Pista, base e passeios de serviço.
        var half = d.RoadWidth / 2;
        if (!hosted) Infra.Pavement(geo, path, prof, -half, half, 0, L);
        SolidSweep.Along(geo, path, s => SolidSweep.Rect(-halfClear, halfClear, prof.Z(s) - Infra.Wearing - 0.35, prof.Z(s) - Infra.Wearing), MarkingColor.Brita, 0, L, 8, "BASE");
        if (d.WalkwayWidth > 0.05)
            foreach (var side in new[] { -1, 1 })
            {
                var a = side * half;
                var b = side * halfClear;
                SolidSweep.Along(geo, path, s => SolidSweep.Rect(Math.Min(a, b), Math.Max(a, b), prof.Z(s) - Infra.Wearing, prof.Z(s) + Math.Max(0.1, d.WalkwayHeight)),
                    MarkingColor.Concreto, 0, L, 8, "PASSEIO");
            }
        if (!hosted) Infra.LaneMarkings(geo, path, prof, -half, half, Math.Max(1, d.Lanes), 0, L, true);
        // Iluminação contínua (luminárias a cada 8 m nas duas laterais da abóbada).
        if (d.Lighting)
            for (var s = 4.0; s < L; s += 8)
            {
                var p = path.PointAt(s);
                var n = SolidSweep.Normal(path, s);
                foreach (var side in new[] { -1, 1 })
                    SolidSweep.Add(geo, SolidSweep.Box(p + n * (side * halfClear * 0.55), path.TangentAt(s), 1.4, 0.25, prof.Z(s) + crown - 1.0, prof.Z(s) + crown - 0.85),
                        MarkingColor.Vidro, "ILUMINACAO", isUnit: true);
            }
        // Ventiladores de jato aos pares, junto à coroa.
        var fans = 0;
        if (d.JetFans && L > 60)
            for (var s = Math.Min(50, L / 3); s < L - 20; s += Math.Max(30, d.FanSpacing))
            {
                var tng = path.TangentAt(s);
                var n = SolidSweep.Normal(path, s);
                foreach (var side in new[] { -1, 1 })
                {
                    var c = path.PointAt(s) + n * (side * 1.4);
                    var z = prof.Z(s) + crown - 1.2;
                    // Ventilador de jato: carcaça oca, silenciadores cônicos e suportes na abóbada.
                    SolidSweep.AddRound(geo, Vec3.At(c - tng * 1.2, z), Vec3.At(c + tng * 1.2, z), 0.58, 0.58, MarkingColor.Metal, "VENTILACAO", true, 0.52, 0.52, 28);
                    foreach (var sg in new[] { -1.0, 1.0 })
                        SolidSweep.AddRound(geo, Vec3.At(c + tng * (sg * 1.2), z), Vec3.At(c + tng * (sg * 2.3), z), 0.58, 0.50, MarkingColor.Metal, "VENTILACAO", false, 0.52, 0.46, 28);
                    SolidSweep.AddColumn(geo, c, 0.30, 0.30, z - 0.1, z + 0.05, MarkingColor.Metal, "VENTILACAO");
                    foreach (var sg in new[] { -0.8, 0.8 })
                        SolidSweep.Add(geo, SolidSweep.Box(c + tng * sg, tng, 0.12, 0.5, z + 0.5, z + 1.2), MarkingColor.Metal, "VENTILACAO");
                    fans++;
                }
            }
        // Corrimão junto à parede nos passeios, faixa de LED contínua, eletrocalhas e nichos de emergência (SOS) a cada 150 m.
        if (d.WalkwayWidth > 0.3 || (hosted && edgeRise > 0.05))
            foreach (var side in new[] { -1, 1 })
                Infra.Railing(geo, path, prof, side * (halfClear - 0.10), 0, L, Math.Max(0.1, d.WalkwayHeight), 1.0, 3.0);
        if (d.Lighting)
            foreach (var side in new[] { -1, 1 })
            {
                var y = side * halfClear * 0.62;
                SolidSweep.Along(geo, path, s => SolidSweep.Rect(y - 0.12, y + 0.12, prof.Z(s) + crown - 0.62, prof.Z(s) + crown - 0.55), MarkingColor.Vidro, 0, L, 8, "ILUMINACAO");
                var yc = side * (halfClear - 0.25);
                SolidSweep.Along(geo, path, s => SolidSweep.Rect(yc - 0.15, yc + 0.15, prof.Z(s) + 3.2, prof.Z(s) + 3.28), MarkingColor.Metal, 0, L, 8, "ELETROCALHA");
            }
        for (var s = 75.0; s < L - 30; s += 150)
            foreach (var side in new[] { -1, 1 })
            {
                var q = path.PointAt(s) + SolidSweep.Normal(path, s) * (side * (halfClear + 0.02));
                SolidSweep.Add(geo, SolidSweep.Box(q, path.TangentAt(s), 1.2, 0.06, prof.Z(s) + 0.5, prof.Z(s) + 2.4), MarkingColor.Vermelha, "SOS", true);
            }
        // Emboques.
        foreach (var (s, dir) in new[] { (0.0, -1), (L, 1) })
            Portal(geo, d, path, prof, s, dir, parts, crown, halfClear, t);
        // Terraplenagem: trincheiras de acesso diante dos emboques (a abertura interna fica a cargo da escavação do Revit).
        if (d.ApproachCut > 1 && !hosted)
            foreach (var (s, dir) in new[] { (0.0, -1), (L, 1) })
            {
                var p = path.PointAt(s);
                var tn = path.TangentAt(s) * dir;
                var ext = new Polyline2(new[] { p, p + tn * d.ApproachCut });
                var z = prof.Z(s);
                var extProf = VerticalProfile.Flat(z);
                var cut = Infra.Corridor(ext, extProf, -halfClear - t - 1, halfClear + t + 1, 0, ext.Length, Infra.Wearing + 0.35, 1.0, 1.5, step: 2, label: "Emboque");
                cut.WallStart = true;           // testa do emboque: face vertical até o topo da testa, talude acima
                cut.WallCap = PortalCap(d) + Infra.Wearing + 0.35;
                geo.Corridors.Add(cut);
            }
        geo.PathLength = L;
        geo.PaintedLength = L;
        geo.UnitCount = fans;
        BridgeGenerator.Measure(geo, MarkingColor.Concreto, 0);
        // Volume de escavação (envoltória externa do revestimento): no Revit vira uma Massa que escava o Toposolid – o terreno
        // natural fica por cima e o furo do túnel aparece no terreno nativo.
        if (ctx.NativeTerrain)
        {
            var env = Envelope(parts);
            if (env.Count >= 3)
                SolidSweep.Along(geo, path, s => env.Select(q => new SectionPt(q.Y, q.Z + prof.Z(s))).ToList(), MarkingColor.Terra, 0, L, 3, BoreLayer);
        }
        return geo;
    }

    /// <summary>Altura da testa do emboque sobre a pista (m): acima dela a encosta é recortada em talude.</summary>
    public static double PortalCap(TunnelDefinition d)
    {
        var (_, crown, _) = TunnelSection(d);
        return crown + Math.Max(0.2, d.LiningThickness) + 1.8;
    }

    /// <summary>Camada das peças que só escavam o terreno nativo (não são desenhadas nem quantificadas como obra).</summary>
    public const string BoreLayer = "ESCAVACAO";

    /// <summary>Envoltória convexa (seção) de todas as partes do revestimento, no sentido anti-horário.</summary>
    public static List<SectionPt> Envelope(IEnumerable<SectionPt[]> parts)
    {
        var pts = parts.SelectMany(p => p).Distinct().OrderBy(p => p.Y).ThenBy(p => p.Z).ToList();
        if (pts.Count < 3) return pts;
        double Cross(SectionPt o, SectionPt a, SectionPt b) => (a.Y - o.Y) * (b.Z - o.Z) - (a.Z - o.Z) * (b.Y - o.Y);
        var hull = new List<SectionPt>();
        foreach (var pass in new[] { pts, Enumerable.Reverse(pts).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 1e-9) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull;
    }

    private static void Portal(MarkingGeometry geo, TunnelDefinition d, Polyline2 path, VerticalProfile prof, double s, int dir, List<SectionPt[]> ring,
        double crown, double halfClear, double t)
    {
        var p = path.PointAt(s);
        var tn = path.TangentAt(s) * dir;           // para fora do túnel
        var z = prof.Z(s);
        var wing = 3.0;
        var outer = halfClear + t;
        var top = crown + t + 1.8;
        var bottom = -Infra.Wearing - 0.35 - d.InvertThickness - t;
        switch (d.Portal)
        {
            case TipoEmboque.Pala:
            {
                // Pala: o anel continua por mais PortalLength metros, com a testa no fim.
                foreach (var part in ring)
                    SolidSweep.Add(geo, SolidSweep.Extrude(part.Select(q => new SectionPt(q.Y * dir, q.Z + z)).ToList(), p, tn, d.PortalLength), MarkingColor.Concreto, "EMBOQUE");
                Headwall(geo, d, p + tn * d.PortalLength, tn, z, outer, halfClear, crown, top, bottom, wing);
                break;
            }
            case TipoEmboque.Bisel:
            {
                // Bisel: o anel avança mais na base que na coroa (corte inclinado acompanhando o talude).
                var n0 = tn.PerpLeft;
                foreach (var part in ring)
                {
                    var fa = part.Select(q => Vec3.At(p + n0 * (q.Y * dir), q.Z + z)).ToList();
                    var fb = part.Select(q =>
                    {
                        var e = Math.Max(0.3, d.PortalLength * (1 - Math.Clamp((q.Z - bottom) / Math.Max(1, crown + t - bottom), 0, 1)));
                        return Vec3.At(p + n0 * (q.Y * dir) + tn * e, q.Z + z);
                    }).ToList();
                    SolidSweep.Add(geo, SolidSweep.Prism(fa, fb), MarkingColor.Concreto, "EMBOQUE");
                }
                // Muros de ala laterais.
                foreach (var side in new[] { -1, 1 })
                {
                    var n = tn.PerpLeft;
                    var c = p + tn * (d.PortalLength / 2) + n * (side * (outer + 0.3));
                    SolidSweep.Add(geo, SolidSweep.Box(c, tn, d.PortalLength, 0.5, z + bottom, z + crown * 0.6), MarkingColor.Concreto, "EMBOQUE");
                }
                break;
            }
            default:
                Headwall(geo, d, p, tn, z, outer, halfClear, crown, top, bottom, wing);
                break;
        }
    }

    /// <summary>Parede de testa com abertura no formato do túnel (pilares, verga e tímpanos em partes convexas).</summary>
    private static void Headwall(MarkingGeometry geo, TunnelDefinition d, Vec2 p, Vec2 tn, double z, double outer, double halfClear, double crown,
        double top, double bottom, double wing)
    {
        const double th = 0.8;
        // Testa com as alas descendo em 1:1 nas pontas (acompanham o talude do emboque).
        var parts = new List<SectionPt[]>
        {
            new[] { new SectionPt(-outer - wing, bottom), new SectionPt(-halfClear, bottom), new SectionPt(-halfClear, top), new SectionPt(-outer, top), new SectionPt(-outer - wing, top - wing) },
            new[] { new SectionPt(halfClear, bottom), new SectionPt(outer + wing, bottom), new SectionPt(outer + wing, top - wing), new SectionPt(outer, top), new SectionPt(halfClear, top) },
        };
        if (d.Section == SecaoTunel.Retangular)
            parts.Add(SolidSweep.Rect(-halfClear, halfClear, crown, top));
        else
        {
            // Tímpanos: entre o arco interno e o topo da testa.
            var r = halfClear;
            var center = crown - r;
            const int n = 12;
            for (int i = 0; i < n; i++)
            {
                var a0 = Math.PI * i / n;
                var a1 = Math.PI * (i + 1) / n;
                var y0 = Math.Cos(a0) * r;
                var y1 = Math.Cos(a1) * r;
                var z0 = center + Math.Sin(a0) * r;
                var z1 = center + Math.Sin(a1) * r;
                var pts = new[] { new SectionPt(y1, z1), new SectionPt(y0, z0), new SectionPt(y0, top), new SectionPt(y1, top) };
                parts.Add(pts);
            }
        }
        foreach (var part in parts)
            SolidSweep.Add(geo, SolidSweep.Extrude(part.Select(q => new SectionPt(q.Y, q.Z + z)).ToList(), p - tn * (th / 2), tn, th), MarkingColor.Concreto, "EMBOQUE");
        // Coroamento (sobre o trecho reto da testa) e moldura saliente em volta da boca.
        SolidSweep.Add(geo, SolidSweep.Box(p, tn, th + 0.3, 2 * outer + 0.3, z + top, z + top + 0.25), MarkingColor.Concreto, "EMBOQUE");
        if (d.Section != SecaoTunel.Retangular)
        {
            var r0 = halfClear;
            var c0 = crown - r0;
            const int m = 16;
            for (int i = 0; i < m; i++)
            {
                double A(int k) => Math.PI * k / m;
                var q = new[]
                {
                    new SectionPt(Math.Cos(A(i)) * r0, c0 + Math.Sin(A(i)) * r0), new SectionPt(Math.Cos(A(i)) * (r0 + 0.6), c0 + Math.Sin(A(i)) * (r0 + 0.6)),
                    new SectionPt(Math.Cos(A(i + 1)) * (r0 + 0.6), c0 + Math.Sin(A(i + 1)) * (r0 + 0.6)), new SectionPt(Math.Cos(A(i + 1)) * r0, c0 + Math.Sin(A(i + 1)) * r0),
                };
                if (Polygon2.SignedArea(q.Select(v => new Vec2(v.Y, v.Z)).ToList()) < 0) Array.Reverse(q);
                SolidSweep.Add(geo, SolidSweep.Extrude(q.Select(v => new SectionPt(v.Y, v.Z + z)).ToList(), p + tn * (th / 2), tn, 0.15), MarkingColor.PavimentoConcreto, "EMBOQUE");
            }
        }
    }

    // ================================================================== trincheira

    public static MarkingGeometry Trench(TrenchDefinition d, Polyline2 path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var hostedInfo = Hosted(d, d, path, ctx);
        var hosted = hostedInfo != null;
        if (hostedInfo is { } hi)
        {
            path = hi.Path;
            var c = (TrenchDefinition)d.CloneWithNewId();
            c.Lanes = 1; c.LaneWidth = 2 * Math.Max(1, hi.Half); c.ShoulderWidth = 0; c.GroundLine = null;
            d = c;
        }
        var L = path.Length;
        if (L < 20) { geo.Warnings.Add("Eixo da trincheira muito curto."); return geo; }
        var half0 = d.RoadWidth / 2 + 0.3 + Math.Max(0.2, d.WallThickness) + 3;
        // Terreno natural (congelado) medido fora dos muros – a escavação entre eles não altera o projeto.
        var topProf = Infra.GroundProfile(d, path, ctx, s => path.PointAt(s) + SolidSweep.Normal(path, s) * half0, Math.Max(2, L / 60));
        var g0 = topProf.Z(0);
        var g1 = topProf.Z(L);
        var bottom = Math.Min(g0, g1) - Math.Max(1, d.Depth);
        var gr = Math.Max(0.02, d.MaxGrade);
        var rin = Math.Abs(g0 - bottom) / gr;
        var rout = Math.Abs(g1 - bottom) / gr;
        if (rin + rout > L * 0.9 && !hosted)
        {
            var k = L * 0.9 / (rin + rout);
            rin *= k; rout *= k;
            geo.Warnings.Add($"Eixo curto para as rampas: rampa acima de {(gr * 100).ToString("0.0", Pt)} % – alongue o eixo ou reduza o rebaixo.");
        }
        var prof = hostedInfo?.Profile ?? VerticalProfile.Hump(L, g0, bottom, g1, rin, rout, d.VerticalCurve);
        var half = d.RoadWidth / 2;
        var t = Math.Max(0.2, d.WallThickness);
        if (!hosted) Infra.Pavement(geo, path, prof, -half, half, 0, L);
        Infra.Base(geo, path, prof, -half, half, 0, L);
        if (!hosted) Infra.LaneMarkings(geo, path, prof, -half, half, Math.Max(1, d.Lanes), 0, L, true);
        double Depth(double s) => topProf.Z(s) - prof.Z(s);
        var walled = SolidSweep.Stations(path, 0, L, 1).Where(s => Depth(s) > 0.5).ToList();
        var ws0 = walled.Count > 0 ? walled.First() : L / 2;
        var ws1 = walled.Count > 0 ? walled.Last() : L / 2;
        foreach (var side in new[] { -1, 1 })
        {
            var y0 = side * (half + 0.3);
            var y1 = side * (half + 0.3 + t);
            // Parede (do fundo ao terreno + coroamento de 0,20 m) e sapata.
            SolidSweep.Along(geo, path, s => Depth(s) <= 0.3 ? null : SolidSweep.Rect(Math.Min(y0, y1), Math.Max(y0, y1), prof.Z(s) - 0.6, topProf.Z(s) + 0.20),
                MarkingColor.Concreto, ws0, ws1, 3, "MURO");
            SolidSweep.Along(geo, path, s => Depth(s) <= 0.3 ? null : SolidSweep.Rect(side < 0 ? -half - 0.3 - t - 1.2 : half - 0.3, side < 0 ? -half + 0.3 : half + 0.3 + t + 1.2,
                prof.Z(s) - 1.2, prof.Z(s) - 0.6), MarkingColor.Concreto, ws0, ws1, 4, "MURO");
            if (d.Wall == TipoContencaoTrincheira.CortinaAtirantada)
                for (var s = ws0 + 1.25; s < ws1; s += 2.5)
                    for (var z = prof.Z(s) + 1.5; z < topProf.Z(s) - 0.8; z += 2.5)
                    {
                        var c = path.PointAt(s) + SolidSweep.Normal(path, s) * (side * (half + 0.3 - 0.12));
                        SolidSweep.Add(geo, SolidSweep.Box(c, path.TangentAt(s), 0.40, 0.24, z - 0.2, z + 0.2), MarkingColor.Metal, "TIRANTE", isUnit: true);
                    }
            if (d.Wall == TipoContencaoTrincheira.TerraArmada)
                for (var s = ws0 + 0.75; s < ws1; s += 1.5)
                    for (var z = prof.Z(s) + 0.1; z < topProf.Z(s) - 0.2; z += 1.5)
                    {
                        var c = path.PointAt(s) + SolidSweep.Normal(path, s) * (side * (half + 0.3 - 0.03));
                        SolidSweep.Add(geo, SolidSweep.Box(c, path.TangentAt(s), 1.44, 0.06, z, Math.Min(z + 1.44, topProf.Z(s))), MarkingColor.PavimentoConcreto, "PAINEL");
                    }
            // Guarda-corpo sobre o muro e canaleta ao pé.
            Infra.Railing(geo, path, topProf, side * (half + 0.3 + t / 2), ws0, ws1, 0.20, Math.Max(0.9, d.ParapetHeight));
            if (d.Drainage)
                SolidSweep.Along(geo, path, s => SolidSweep.Rect(side < 0 ? -half - 0.3 : half, side < 0 ? -half : half + 0.3, prof.Z(s) - 0.25, prof.Z(s) - Infra.Wearing),
                    MarkingColor.PavimentoConcreto, 0, L, 6, "CANALETA");
        }
        // Laje de travessia (viaduto da transversal).
        if (d.CoverLength > 1)
        {
            var sc = Math.Clamp(d.CoverAt, 0.05, 0.95) * L;
            var a = Math.Max(ws0, sc - d.CoverLength / 2);
            var b = Math.Min(ws1, sc + d.CoverLength / 2);
            if (b - a > 1)
                SolidSweep.Along(geo, path, s => SolidSweep.Rect(-half - 0.3 - t - 0.5, half + 0.3 + t + 0.5, topProf.Z(s) - 0.9, topProf.Z(s)), MarkingColor.Concreto, a, b, 3, "LAJE");
        }
        if (d.Lighting) Infra.Lights(geo, path, prof, half + 0.1, 0, L, 25, false, 9);
        // Terraplenagem: escavação entre os muros (sem taludes – os muros contêm o terreno) e rampas em corte nas pontas.
        // Muros: o terreno natural volta logo atrás da face externa (degrau vertical), sem rampa de terra até a borda.
        var trench = Infra.Corridor(path, prof, -half - 0.3 - t, half + 0.3 + t, ws0, ws1, Infra.Wearing + 0.35, 1.0, 1.5, false, false, 2, "Trincheira");
        trench.WallLeft = trench.WallRight = true;
        geo.Corridors.Add(trench);
        if (ws0 > 1) geo.Corridors.Add(Infra.Corridor(path, prof, -half - 0.3, half + 0.3, 0, ws0, Infra.Wearing + 0.35, 1.0, 1.5, label: "Rampa"));
        if (ws1 < L - 1) geo.Corridors.Add(Infra.Corridor(path, prof, -half - 0.3, half + 0.3, ws1, L, Infra.Wearing + 0.35, 1.0, 1.5, label: "Rampa"));
        geo.PathLength = L;
        geo.PaintedLength = L;
        BridgeGenerator.Measure(geo, MarkingColor.Concreto, 0);
        return geo;
    }

    // ================================================================== muro de arrimo

    public static MarkingGeometry RetainingWall(RetainingWallDefinition d, Polyline2 path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var L = path.Length;
        if (L < 0.5) { geo.Warnings.Add("Linha do muro muito curta."); return geo; }
        var r = d.RetainedLeft ? 1.0 : -1.0;
        // Base (terreno natural na face, congelado) e altura ao longo do muro.
        var baseProf = Infra.GroundProfile(d, path, ctx, null, Math.Max(1, L / 80));
        double H(double s) => Math.Max(0.5, d.HeightStart + (d.HeightEnd - d.HeightStart) * s / L);
        double Zb(double s) => baseProf.Z(s);
        double Zt(double s) => Zb(s) + H(s);
        var emb = Math.Max(0, d.Embedment);
        var hMax = Math.Max(d.HeightStart, d.HeightEnd);
        SectionPt[] Sec(params (double Y, double Z)[] pts)
        {
            var list = pts.Select(q => new SectionPt(q.Y * r, q.Z)).ToList();
            if (r < 0) list.Reverse();
            return list.ToArray();
        }
        var tw = Math.Max(0.15, d.TopWidth);
        var backAt = tw;                                            // face de trás no topo (para o reaterro)
        switch (d.Type)
        {
            case TipoMuro.Gravidade:
            {
                var bw = d.BaseWidth > 0.3 ? d.BaseWidth : 0.5 * hMax + 0.3;
                SolidSweep.Along(geo, path, s => Sec((0, Zb(s) - emb), (bw, Zb(s) - emb), (tw, Zt(s)), (0, Zt(s))), MarkingColor.Concreto, 0, L, 3, "MURO");
                backAt = bw;
                if (hMax / bw > 2.2) geo.Warnings.Add("Muro de gravidade esbelto (H/B > 2,2) – confira a estabilidade (tombamento/deslizamento).");
                break;
            }
            case TipoMuro.Flexao:
            case TipoMuro.Contrafortes:
            {
                var tb = Math.Max(tw, 0.08 * hMax + 0.15);
                var bw = d.BaseWidth > 0.5 ? d.BaseWidth : 0.6 * hMax + 0.4;
                var toe = Math.Clamp(d.ToeLength, 0, bw * 0.5);
                var ft = Math.Max(0.25, d.FootingThickness);
                SolidSweep.Along(geo, path, s => Sec((-toe, Zb(s) - emb - ft), (bw - toe, Zb(s) - emb - ft), (bw - toe, Zb(s) - emb), (-toe, Zb(s) - emb)),
                    MarkingColor.Concreto, 0, L, 3, "SAPATA");
                SolidSweep.Along(geo, path, s => Sec((0, Zb(s) - emb), (tb, Zb(s) - emb), (tw, Zt(s)), (0, Zt(s))), MarkingColor.Concreto, 0, L, 3, "MURO");
                if (d.Type == TipoMuro.Contrafortes)
                {
                    var heel = bw - toe - tb;
                    for (var s = Math.Min(L / 2, d.CounterfortSpacing / 2); s < L; s += Math.Max(1.5, d.CounterfortSpacing))
                    {
                        var tri = Sec((tb, Zb(s) - emb), (tb + heel, Zb(s) - emb), (tw, Zt(s) - 0.2));
                        SolidSweep.Add(geo, SolidSweep.Extrude(tri, path.PointAt(s) - path.TangentAt(s) * 0.15, path.TangentAt(s), 0.30), MarkingColor.Concreto, "CONTRAFORTE");
                    }
                }
                backAt = tb;
                break;
            }
            case TipoMuro.Gabiao:
            {
                var size = Math.Clamp(d.GabionSize, 0.5, 1.5);
                var rows = (int)Math.Ceiling(hMax / size);
                var bw = Math.Max(1.5, 0.6 * hMax);
                for (int i = 0; i < rows; i++)
                {
                    var front = i * size * 0.25;
                    var w = Math.Max(size, bw - i * size * 0.5);
                    var ii = i;
                    SolidSweep.Along(geo, path, s =>
                    {
                        var z0 = Zb(s) - emb * 0 + ii * size;
                        if (z0 >= Zt(s) - 0.05) return null;
                        var z1 = Math.Min(Zt(s), z0 + size);
                        return Sec((front, z0), (front + w, z0), (front + w, z1), (front, z1));
                    }, MarkingColor.Brita, 0, L, size * 2, "GABIAO", merge: false);
                }
                backAt = bw;
                break;
            }
            case TipoMuro.TerraArmada:
            {
                var pz = Math.Clamp(d.PanelSize, 0.75, 2.5);
                // Maciço de solo reforçado atrás das placas (largura ≈ 0,7 H).
                if (!ctx.NativeTerrain)
                    SolidSweep.Along(geo, path, s => Sec((0.18, Zb(s) - emb), (0.7 * H(s) + 0.18, Zb(s) - emb), (0.7 * H(s) + 0.18, Zt(s)), (0.18, Zt(s))), MarkingColor.Terra, 0, L, 4, "MACICO");
                var rowsTa = (int)Math.Ceiling((hMax + emb) / pz);
                for (int i = 0; i < rowsTa; i++)
                {
                    var ii = i;
                    // Placas em fiadas desencontradas (junta a meia placa).
                    var offset = i % 2 == 0 ? 0 : pz / 2;
                    SolidSweep.Along(geo, path, s =>
                    {
                        var z0 = Zb(s) - emb + ii * pz;
                        if (z0 >= Zt(s) - 0.05) return null;
                        var z1 = Math.Min(Zt(s), z0 + pz - 0.02);
                        return Sec((0, z0), (0.18, z0), (0.18, z1), (0, z1));
                    }, MarkingColor.PavimentoConcreto, Math.Min(L, offset), L, pz, "PAINEL", merge: false);
                }
                backAt = 0.7 * hMax + 0.18;
                break;
            }
            case TipoMuro.CortinaAtirantada:
            {
                SolidSweep.Along(geo, path, s => Sec((0, Zb(s) - emb), (0.35, Zb(s) - emb), (0.35, Zt(s)), (0, Zt(s))), MarkingColor.Concreto, 0, L, 3, "CORTINA");
                var sp = Math.Max(1.5, d.AnchorSpacing);
                for (var s = sp / 2; s < L; s += sp)
                    for (var z = Zb(s) + 1.0; z < Zt(s) - 0.6; z += sp)
                    {
                        var c = path.PointAt(s) + SolidSweep.Normal(path, s) * (-r * 0.12);
                        SolidSweep.Add(geo, SolidSweep.Box(c, path.TangentAt(s), 0.45, 0.25, z - 0.22, z + 0.22), MarkingColor.Concreto, "TIRANTE", isUnit: true);
                        SolidSweep.Add(geo, SolidSweep.Box(c + SolidSweep.Normal(path, s) * (-r * 0.15), path.TangentAt(s), 0.18, 0.08, z - 0.09, z + 0.09), MarkingColor.Metal, "TIRANTE");
                    }
                backAt = 0.35;
                break;
            }
        }
        if (d.Coping && d.Type is not (TipoMuro.Gabiao or TipoMuro.TerraArmada))
            SolidSweep.Along(geo, path, s => Sec((-0.05, Zt(s)), (tw + 0.05, Zt(s)), (tw + 0.05, Zt(s) + 0.15), (-0.05, Zt(s) + 0.15)), MarkingColor.PavimentoConcreto, 0, L, 3, "COROAMENTO");
        if (d.WeepHoles && d.Type is not (TipoMuro.Gabiao))
            for (var s = 1.0; s < L; s += 2.0)
            {
                var p = path.PointAt(s);
                var n = SolidSweep.Normal(path, s);
                var z = Zb(s) + 0.5;
                SolidSweep.Add(geo, SolidSweep.Tube(Vec3.At(p - n * (r * 0.05), z), Vec3.At(p + n * (r * (backAt + 0.1)), z + 0.05), 0.0375, 8), MarkingColor.Preta, "BARBACA");
            }
        var topProf = new VerticalProfile();
        foreach (var (s, _) in baseProf.Pvis) topProf.Pvis.Add((s, Zt(s) + (d.Coping ? 0.15 : 0)));
        if (d.Guardrail) Infra.Railing(geo, path, topProf, r * tw / 2, 0, L);
        // Terraplenagem: reaterro atrás do muro até o topo; na frente, o terreno fica na base.
        var back = Infra.Corridor(path, topProf, 0, 0, 0, L, 0, 1.0, 1.5, step: 2, label: "Reaterro");
        var toeStrip = Infra.Corridor(path, baseProf, 0, 0, 0, L, 0, 1.0, 1.5, step: 2, label: "Pé do muro");
        // Reaterro até o topo da parede (sob o coroamento).
        FillStrip(back, path, topProf, r * (backAt + 0.2), r * (backAt + 0.2 + Math.Max(1, d.BackfillWidth)), d.Coping ? -0.15 : 0);
        FillStrip(toeStrip, path, baseProf, -r * 0.05, -r * 1.5, 0);
        // Borda junto ao muro sem talude; lado de fora concorda com o terreno.
        back.DaylightLeft = r > 0; back.DaylightRight = r < 0;
        toeStrip.DaylightLeft = r < 0; toeStrip.DaylightRight = r > 0;
        geo.Corridors.Add(back);
        geo.Corridors.Add(toeStrip);
        geo.PathLength = L;
        geo.PaintedLength = L;
        // Área da face do muro (m²) – medida usual de contenções.
        var face = 0.0;
        for (var s = 0.0; s < L; s += 0.5) face += H(Math.Min(L, s + 0.25)) * Math.Min(0.5, L - s);
        BridgeGenerator.Measure(geo, d.Type == TipoMuro.Gabiao ? MarkingColor.Brita : d.Type == TipoMuro.TerraArmada ? MarkingColor.PavimentoConcreto : MarkingColor.Concreto, face);
        return geo;
    }

    /// <summary>Preenche as bordas de uma faixa com afastamentos fixos (<paramref name="y0"/> junto à obra, <paramref name="y1"/> do lado de fora).</summary>
    private static void FillStrip(GradeCorridor c, Polyline2 path, VerticalProfile prof, double y0, double y1, double dz)
    {
        c.Left.Clear();
        c.Right.Clear();
        var (yl, yr) = y0 > y1 ? (y0, y1) : (y1, y0);
        foreach (var s in SolidSweep.Stations(path, 0, path.Length, 2))
        {
            var p = path.PointAt(s);
            var n = SolidSweep.Normal(path, s);
            var z = prof.Z(s) + dz;
            c.Left.Add(Vec3.At(p + n * yl, z));
            c.Right.Add(Vec3.At(p + n * yr, z));
        }
    }

    // ================================================================== talude

    /// <summary>Perfil do talude (afastamento a partir do pé, cota relativa) com bermas.</summary>
    public static List<(double Y, double Z)> SlopeProfile(SlopeDefinition d)
    {
        var pts = new List<(double, double)> { (0, 0) };
        var h = Math.Max(0.5, d.Height);
        var ratio = Math.Max(0.2, d.Ratio);
        var every = d.BermEvery > 0.5 ? d.BermEvery : double.MaxValue;
        double y = 0, z = 0;
        while (z < h - 1e-6)
        {
            var rise = Math.Min(every, h - z);
            y += rise * ratio;
            z += rise;
            pts.Add((y, z));
            if (z < h - 1e-6 && d.BermWidth > 0.1)
            {
                y += d.BermWidth;
                pts.Add((y, z));
            }
        }
        return pts;
    }

    public static MarkingGeometry Slope(SlopeDefinition d, Polyline2 path, BuildContext ctx)
    {
        var geo = new MarkingGeometry();
        var L = path.Length;
        if (L < 1) { geo.Warnings.Add("Linha do talude muito curta."); return geo; }
        var u = d.UphillLeft ? 1.0 : -1.0;
        var prof = SlopeProfile(d);
        // Aterro: o pé fica no terreno natural (linha desenhada) e a crista H acima. Corte: a CRISTA fica no terreno natural
        // (a H·razão do pé, lado de cima) e o pé H abaixo dela – o talude entra no maciço e o terreno à frente é rebaixado.
        var cut = d.Type == TipoTalude.Corte;
        var crestOff = prof[^1].Y;
        var groundProf = Infra.GroundProfile(d, path, ctx, cut ? s => path.PointAt(s) + SolidSweep.Normal(path, s) * (u * crestOff) : null, Math.Max(1, L / 80));
        var baseProf = groundProf;
        if (cut)
        {
            baseProf = new VerticalProfile();
            foreach (var (s, z) in groundProf.Pvis) baseProf.Pvis.Add((s, z - prof[^1].Z));
        }
        var lining = d.Lining switch
        {
            RevestimentoTalude.ConcretoProjetado => MarkingColor.Concreto,
            RevestimentoTalude.Enrocamento => MarkingColor.Brita,
            RevestimentoTalude.SoloExposto => MarkingColor.Terra,
            _ => MarkingColor.Grama,
        };
        SectionPt[] Sec(params (double Y, double Z)[] pts)
        {
            var list = pts.Select(q => new SectionPt(q.Y * u, q.Z)).ToList();
            if (u < 0) list.Reverse();
            return list.ToArray();
        }
        var crestY = prof[^1].Y;
        var hTot = prof[^1].Z;
        for (int i = 0; i + 1 < prof.Count; i++)
        {
            var (y0, z0) = prof[i];
            var (y1, z1) = prof[i + 1];
            var isBerm = Math.Abs(z1 - z0) < 1e-6;
            // Corpo do aterro (só em aterros) sob cada trecho.
            if (d.Type == TipoTalude.Aterro && !ctx.NativeTerrain)
                SolidSweep.Along(geo, path, s => Sec((y0, baseProf.Z(s)), (y1, baseProf.Z(s)), (y1, baseProf.Z(s) + z1 - 0.1), (y0, baseProf.Z(s) + z0 - (i == 0 ? 0 : 0.1))),
                    MarkingColor.Terra, 0, L, 4, "ATERRO");
            // Revestimento (camada de 0,10 m sobre a face) – bermas em concreto quando há canaleta.
            var color = isBerm && d.BermChannels ? MarkingColor.PavimentoConcreto : lining;
            // No terreno nativo a grama é uma subdivisão do Toposolid; revestimentos rígidos ficam 3 cm acima da face.
            var lift = ctx.NativeTerrain ? 0.03 : 0;
            if (ctx.NativeTerrain && !isBerm)
                geo.TerrainFinishes.Add((new Polygon2(SlopeBand(path, u * y0, u * y1)), lining, "Talude"));
            if (!(ctx.NativeTerrain && color == MarkingColor.Grama))
            SolidSweep.Along(geo, path, s => Sec((y0, baseProf.Z(s) + z0 - 0.10 + lift), (y1, baseProf.Z(s) + z1 - 0.10 + lift), (y1, baseProf.Z(s) + z1 + lift), (y0, baseProf.Z(s) + z0 + lift)),
                color, 0, L, 4, isBerm ? "BERMA" : "REVESTIMENTO");
            if (isBerm && d.BermChannels) Channel(geo, path, baseProf, u * (y0 + 0.4), z0, L);
        }
        if (d.Type == TipoTalude.Aterro && !ctx.NativeTerrain)
            SolidSweep.Along(geo, path, s => Sec((crestY, baseProf.Z(s)), (crestY + 2, baseProf.Z(s)), (crestY + 2, baseProf.Z(s) + hTot - 0.1), (crestY, baseProf.Z(s) + hTot - 0.1)),
                MarkingColor.Terra, 0, L, 4, "ATERRO");
        if (d.CrestChannel) Channel(geo, path, baseProf, u * (crestY + 1.5), hTot, L);
        if (d.ToeChannel) Channel(geo, path, baseProf, -u * 0.8, 0, L);
        // Descidas d'água em degraus.
        if (d.DowndrainSpacing > 5)
            for (var s = d.DowndrainSpacing / 2; s < L; s += d.DowndrainSpacing)
            {
                var p = path.PointAt(s);
                var n = SolidSweep.Normal(path, s) * u;
                var t = path.TangentAt(s);
                var zb = baseProf.Z(s);
                for (int i = 0; i + 1 < prof.Count; i++)
                {
                    var (y0, z0) = prof[i];
                    var (y1, z1) = prof[i + 1];
                    if (Math.Abs(z1 - z0) < 1e-6) continue;
                    var steps = Math.Max(1, (int)Math.Ceiling((z1 - z0) / 0.5));
                    for (int k = 0; k < steps; k++)
                    {
                        var ya = y0 + (y1 - y0) * k / steps;
                        var yb = y0 + (y1 - y0) * (k + 1) / steps;
                        var zs = zb + z0 + (z1 - z0) * k / steps;
                        SolidSweep.Add(geo, SolidSweep.Box(p + n * ((ya + yb) / 2), t, 1.0, yb - ya, zs - 0.25, zs + 0.08), MarkingColor.PavimentoConcreto, "DESCIDA");
                    }
                }
            }
        // Terraplenagem: a face do talude (cada trecho) + concordância no pé e na crista.
        for (int i = 0; i + 1 < prof.Count; i++)
        {
            var (y0, z0) = prof[i];
            var (y1, z1) = prof[i + 1];
            var c = new GradeCorridor { DaylightLeft = false, DaylightRight = false, CutSlope = d.Type == TipoTalude.Corte ? d.Ratio : 1.0, FillSlope = d.Ratio, Label = "Talude" };
            foreach (var s in SolidSweep.Stations(path, 0, L, 2))
            {
                var p = path.PointAt(s);
                var n = SolidSweep.Normal(path, s) * u;
                var zb = baseProf.Z(s);
                // Esquerda/direita no sentido do eixo: lado de cima = lado u.
                var a = Vec3.At(p + n * y1, zb + z1);
                var b = Vec3.At(p + n * y0, zb + z0);
                if (u > 0) { c.Left.Add(a); c.Right.Add(b); } else { c.Left.Add(b); c.Right.Add(a); }
            }
            if (i == 0 && !cut) { if (u > 0) c.DaylightRight = true; else c.DaylightLeft = true; }
            if (i + 2 == prof.Count) { if (u > 0) c.DaylightLeft = true; else c.DaylightRight = true; }
            geo.Corridors.Add(c);
        }
        if (cut)
        {
            // Plataforma rebaixada à frente do pé do corte (com a canaleta), concordando com o terreno do lado de baixo.
            var front = new GradeCorridor { DaylightLeft = u < 0, DaylightRight = u > 0, CutSlope = Math.Max(0.5, d.Ratio), FillSlope = 1.5, Label = "Pé do corte" };
            foreach (var s in SolidSweep.Stations(path, 0, L, 2))
            {
                var p = path.PointAt(s);
                var n = SolidSweep.Normal(path, s) * u;
                var z = baseProf.Z(s);
                var a = Vec3.At(p, z);
                var b = Vec3.At(p - n * Math.Max(1.5, d.ToePlatform), z);
                if (u > 0) { front.Left.Add(a); front.Right.Add(b); } else { front.Left.Add(b); front.Right.Add(a); }
            }
            geo.Corridors.Add(front);
        }
        geo.PathLength = L;
        geo.PaintedLength = L;
        var faceLen = 0.0;
        for (int i = 0; i + 1 < prof.Count; i++) faceLen += Math.Sqrt(Math.Pow(prof[i + 1].Y - prof[i].Y, 2) + Math.Pow(prof[i + 1].Z - prof[i].Z, 2));
        BridgeGenerator.Measure(geo, lining, faceLen * L);
        if (d.Type == TipoTalude.Corte && d.Ratio < 0.5) geo.Warnings.Add("Talude de corte mais íngreme que 1 : 0,5 – exige estudo geotécnico (NBR 11682).");
        if (d.Type == TipoTalude.Aterro && d.Ratio < 1.5) geo.Warnings.Add("Talude de aterro mais íngreme que 1,5 : 1 – usual em aterros rodoviários (DNIT); confira a estabilidade.");
        return geo;
    }

    /// <summary>Faixa em planta entre os afastamentos a e b ao longo do eixo (contorno).</summary>
    private static List<Vec2> SlopeBand(Polyline2 path, double a, double b)
    {
        var st = SolidSweep.Stations(path, 0, path.Length, 4);
        var left = st.Select(s => path.PointAt(s) + SolidSweep.Normal(path, s) * a).ToList();
        var right = st.Select(s => path.PointAt(s) + SolidSweep.Normal(path, s) * b).Reverse();
        return left.Concat(right).ToList();
    }

    /// <summary>Canaleta de concreto meia-cana (Ø 0,60 m) com abas laterais.</summary>
    private static void Channel(MarkingGeometry geo, Polyline2 path, VerticalProfile baseProf, double y, double z, double L)
    {
        SolidSweep.Along(geo, path, s =>
        {
            var zc = baseProf.Z(s) + z + 0.02;
            var pts = new List<SectionPt> { new(y - 0.42, zc), new(y - 0.30, zc) };
            for (int i = 1; i < 12; i++) { var a = Math.PI + Math.PI * i / 12; pts.Add(new SectionPt(y + Math.Cos(a) * 0.30, zc + Math.Sin(a) * 0.30)); }
            pts.Add(new SectionPt(y + 0.30, zc));
            pts.Add(new SectionPt(y + 0.42, zc));
            pts.Add(new SectionPt(y + 0.42, zc - 0.06));
            for (int i = 11; i >= 1; i--) { var a = Math.PI + Math.PI * i / 12; pts.Add(new SectionPt(y + Math.Cos(a) * 0.37, zc + Math.Sin(a) * 0.37)); }
            pts.Add(new SectionPt(y - 0.42, zc - 0.06));
            return pts;
        }, MarkingColor.PavimentoConcreto, 0, L, 4, "CANALETA");
    }
}
