using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Nó viário de exemplo (prévias da janela e testes): as duas vias, os ramos e as rotatórias como vias do plugin, o viaduto
/// hospedado e os acabamentos do nó – o mesmo conjunto que o Revit monta sobre vias reais.
/// </summary>
public static class InterchangeDemo
{
    /// <summary>Seções das vias criadas pelo nó quando elas ainda não existem (rodovia de pista dupla e via transversal).</summary>
    public static (RoadSetup Main, RoadSetup Cross) Setups(InterchangeDefinition d)
    {
        var main = new RoadSetup
        {
            Hierarchy = HierarquiaViaria.Rodovia, Speed = 100, Center = CenterTreatment.Canteiro, MedianWidth = Math.Max(0.6, d.MainMedian),
            MedianType = TipoCanteiro.Pintado, MedianDevice = "NJ", Inscriptions = false,
        };
        var cross = new RoadSetup { Hierarchy = HierarquiaViaria.Arterial, Speed = 60, Center = d.CrossLanes >= 2 ? CenterTreatment.LFO3 : CenterTreatment.LFO2, Inscriptions = false };
        foreach (var side in new[] { main.Right, main.Left })
        {
            for (int i = 0; i < Math.Max(1, d.MainLanes); i++) side.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = d.MainLaneWidth });
            if (d.MainShoulder > 0.05) side.Add(new ElementoSecao { Tipo = TipoElementoSecao.Acostamento, Largura = d.MainShoulder });
        }
        foreach (var side in new[] { cross.Right, cross.Left })
        {
            for (int i = 0; i < Math.Max(1, d.CrossLanes); i++) side.Add(new ElementoSecao { Tipo = TipoElementoSecao.FaixaRolamento, Largura = d.CrossLaneWidth });
            if (d.CrossShoulder > 0.05) side.Add(new ElementoSecao { Tipo = TipoElementoSecao.Acostamento, Largura = d.CrossShoulder });
        }
        if (d.UrbanSection)
            // Seção urbana completa: sarjeta, meio-fio e calçada dos dois lados das duas vias.
            foreach (var side in new[] { main.Right, main.Left, cross.Right, cross.Left })
                side.Add(new ElementoSecao { Tipo = TipoElementoSecao.Calcada, Largura = Math.Max(1.2, d.SidewalkWidth), Sarjeta = 0.30 });
        return (main, cross);
    }

    /// <summary>Eixos das vias de exemplo (retos, cruzando no ponto do nó).</summary>
    public static (Polyline2 Main, Polyline2 Cross) Axes(InterchangeDefinition d)
    {
        var ex = Vec2.FromAngle(d.AngleDeg * Math.PI / 180);
        var ec = ex.Rotate(Math.Clamp(d.CrossAngleDeg, 45, 135) * Math.PI / 180);
        var lm = Math.Max(400, d.MainLength) / 2;
        var lc = Math.Max(400, d.CrossLength) / 2;
        return (new Polyline2(new[] { d.Position - ex * lm, d.Position + ex * lm }), new Polyline2(new[] { d.Position - ec * lc, d.Position + ec * lc }));
    }

    public static PlanRoad Road(string id, RoadSetup s, Polyline2 axis, RoadGrade? g = null)
    {
        var pav = s.PavementDefinition();
        return new PlanRoad(id, axis, 0, g ?? RoadGrade.Flat(axis.Length), pav.LeftWidth, pav.RightWidth, pav.TotalLeft, pav.TotalRight);
    }

    public static MarkingGeometry Build(InterchangeDefinition d, Catalog.Catalogo catalog, Func<Vec2, double>? ground = null, bool terrain = false)
    {
        var geo = new MarkingGeometry();
        var (ms, cs) = Setups(d);
        var (ma, ca) = Axes(d);
        var main = Road("principal", ms, ma);
        var cross = Road("transversal", cs, ca);
        var plan = InterchangePlanner.Plan(d, main, cross, d.Position);
        if (plan == null) { geo.Warnings.Add("As vias não se cruzam."); return geo; }
        geo.Warnings.AddRange(plan.Warnings.Distinct());
        var g = ground ?? (_ => 0.0);
        var mainGrade = plan.CrossOver ? main.Grade : plan.OverGrade;
        var crossGrade = plan.CrossOver ? plan.OverGrade : cross.Grade;
        main = main with { Grade = mainGrade };
        cross = cross with { Grade = crossGrade };
        var viaduct = new BridgeDefinition
        {
            Kind = TipoObraDeArte.Viaduto, ProfileKind = PerfilObra.GreideDaVia, HostStart = plan.BridgeS0, HostEnd = plan.BridgeS1, Skew = plan.Skew,
            PierStations = plan.PierStations.ToList(), PierType = TipoPilar.Parede, PierSize = 1.2, SpanLength = 30, Lighting = d.Lighting,
            GirderDepth = Math.Max(1.0, d.DeckDepth - 0.45), Barrier = TipoGuarda.NewJersey,
        };
        var corridors = new List<GradeCorridor>();
        MarkingGeometry RoadGeo(RoadSetup s, Polyline2 axis, RoadGrade gr, List<MarkingDefinition> works, List<Polygon2>? cuts = null, bool cutPavement = true,
            List<Polygon2>? sideCuts = null)
        {
            var rg = InfraDemo.RoadWith(s, axis, gr, works, catalog, p => g(p), 6, cuts, cutPavement, sideCuts);
            if (terrain) corridors.AddRange(InfraDemo.Corridors(s, axis, gr, works, rg));
            return rg;
        }
        // Recortes nas vias (calçadas, bordos) sob as faixas paralelas dos ramos.
        var joinCuts = plan.Ramps.SelectMany(r => InterchangePlanner.JoinFootprints(r)).ToList();
        geo.Merge(RoadGeo(ms, ma, mainGrade, plan.CrossOver ? new List<MarkingDefinition>() : new List<MarkingDefinition> { viaduct }, joinCuts, false));
        geo.Merge(RoadGeo(cs, ca, crossGrade, plan.CrossOver ? new List<MarkingDefinition> { viaduct } : new List<MarkingDefinition>(), joinCuts, false));
        var records = new List<RampOnSite>();
        var roadsForRb = new List<IntersectionRoad>
        {
            new(cs.PavementDefinition() is var cp ? WithId(cp, "transversal") : cp, ca),
        };
        if (d.Type != TipoNoViario.RotatoriaElevada) roadsForRb.Add(new IntersectionRoad(WithId(ms.PavementDefinition(), "principal"), ma));
        foreach (var r in plan.Ramps)
        {
            var setup = InterchangePlanner.RampSetup(r, d.Guardrails, 50, d.UrbanSection, d.SidewalkWidth);
            var works = r.Bridges.Select(b => (MarkingDefinition)new BridgeDefinition
            {
                Kind = TipoObraDeArte.Viaduto, ProfileKind = PerfilObra.GreideDaVia, HostStart = b.S0, HostEnd = b.S1, PierType = TipoPilar.Circular, PierSize = 1.1,
                SpanLength = 25, Lighting = false, GirderDepth = 1.2,
            }).ToList();
            var strips = d.UrbanSection ? new[] { InterchangePlanner.RoadStrip(main, plan.Node), InterchangePlanner.RoadStrip(cross, plan.Node) }
                .Where(x => x != null).Cast<Polygon2>().ToList() : null;
            geo.Merge(RoadGeo(setup, r.Axis, r.Grade, works, InterchangePlanner.TaperCuts(r), true, strips));
            var rec = new RampRecord
            {
                Group = r.Name, Name = r.Name, Role = r.Role, StartLink = r.StartLink, EndLink = r.EndLink, StartRoad = r.StartRoad, EndRoad = r.EndRoad,
                LaneWidth = r.LaneWidth, Shoulder = r.Shoulder, Taper = r.Taper,
            };
            records.Add(new RampOnSite(rec, new PlanRoad(r.Name, r.Axis, r.BaseZ, r.Grade, 0, r.LaneWidth + r.Shoulder, 0, r.LaneWidth + r.Shoulder)));
            roadsForRb.Add(new IntersectionRoad(WithId(setup.PavementDefinition(), r.Name), r.Axis));
        }
        // Rotatórias dos terminais (ou o anel elevado).
        foreach (var (c, rr, z) in plan.Roundabouts)
        {
            var rb = new RoundaboutDefinition { Center = c, Z = z, Lanes = d.Type == TipoNoViario.RotatoriaElevada ? 2 : 1, LaneWidth = 5.0, SidewalkWidth = 0, Crosswalks = false };
            rb.IslandRadius = Math.Max(4, rr - rb.ApronWidth - rb.Lanes * rb.LaneWidth);
            rb.Legs = RoundaboutGenerator.LegsFromRoads(c, roadsForRb.Where(x => Math.Abs(ZAt(x, c) - z) < 2.5).ToList(), rr + 25);
            var rg = MarkingBuilder.Build(rb, null, new BuildContext { Catalog = catalog });
            for (int i = 0; i < rg.Pieces.Count; i++) rg.Pieces[i] = rg.Pieces[i] with { Elevation = rg.Pieces[i].Elevation + z };
            geo.Merge(rg);
        }
        double ZAt(IntersectionRoad x, Vec2 p) =>
            x.Def.Id == "principal" ? mainGrade.Z(ma.Project(p).Station) : x.Def.Id == "transversal" ? crossGrade.Z(ca.Project(p).Station)
            : plan.Ramps.FirstOrDefault(r => r.Name == x.Def.Id) is { } rp ? rp.BaseZ + rp.Grade.Z(rp.Axis.Project(p).Station) : 0;
        geo.Merge(InterchangeExtras.Build(d, main, cross, records, plan.Roundabouts, plan.RingDecks, g, 0, true));
        if (terrain && geo.Bounds is { } b)
        {
            var design = Grading.Design(corridors, Array.Empty<GradePad>(), p => g(p), 0, 60, 0.5, 4, 3);
            var step = Math.Max(5, Math.Max(b.Max.X - b.Min.X, b.Max.Y - b.Min.Y) / 90);
            geo.Pieces.InsertRange(0, InfraDemo.Toposolid(g, design, b.Min - new Vec2(30, 30), b.Max + new Vec2(30, 30), step)
                .Select(pc => pc with { Elevation = pc.Elevation - 0.02 }));
        }
        return geo;
    }

    private static RoadPavementDefinition WithId(RoadPavementDefinition p, string id) { p.Id = id; p.GroupId = id; return p; }
}
