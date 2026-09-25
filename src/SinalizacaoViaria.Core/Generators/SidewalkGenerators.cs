using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Personalização de calçadas: orelhas, áreas por contorno, canteiros e cul-de-sacs.</summary>
public static class SidewalkGenerator
{
    private const double Sample = 0.25;

    private static MarkingGeometry Fail(string msg)
    {
        var g = new MarkingGeometry();
        g.Warnings.Add(msg);
        return g;
    }

    private static void AddRaised(MarkingGeometry geo, IEnumerable<Polygon2> shapes, MarkingColor color, double height, double elevation = 0)
    {
        foreach (var s in shapes)
        {
            var simple = s.Simplified();
            if (simple == null || simple.Area < 1e-4) continue;
            geo.Pieces.Add(new MarkingPiece(simple, color) { Thickness = Math.Max(0.001, height), Elevation = elevation });
        }
    }

    private static void AddTrees(MarkingGeometry geo, Catalogo cat, IEnumerable<(Vec2 At, Vec2 Dir)> spots, double baseZ)
    {
        var tree = cat.Movel("ARVORE");
        if (tree == null) return;
        foreach (var (at, dir) in spots)
            foreach (var p in UrbanGenerator.BuildAt(tree, new LocalFrame(at, dir), null, null, null, null).Pieces)
                geo.Pieces.Add(p with { Elevation = p.Elevation + baseZ, IsUnit = false });
    }

    // ------------------------------------------------------------------ orelha de calçada

    /// <summary>Perfil lateral (x ao longo do meio-fio, o = avanço) de uma orelha de comprimento L.</summary>
    public static List<Vec2> EarProfile(CurbExtensionDefinition d, double L, List<string>? warnings = null)
    {
        var D = Math.Max(0.1, d.Depth);
        var half = new List<Vec2>();
        double lt;
        if (d.Transition == TipoTransicao.Chanfro)
        {
            lt = Math.Max(0.05, d.Radius);
            if (2 * lt > L) { lt = L / 2; warnings?.Add("Orelha curta para o chanfro: transições reduzidas."); }
            half.Add(new Vec2(0, 0));
            half.Add(new Vec2(lt, D));
        }
        else
        {
            var R = Math.Max(0.1, d.Radius);
            double Lt(double r) => D <= 2 * r ? Math.Sqrt(4 * r * D - D * D) : 2 * r;
            lt = Lt(R);
            if (2 * lt > L)
            {
                R = D <= L / 2 ? (L * L / 4 + D * D) / (4 * D) : L / 4;
                lt = Math.Min(Lt(R), L / 2);
                warnings?.Add($"Orelha curta para o raio pedido: raio de transição reduzido para {R:0.00} m.");
            }
            var theta = D <= 2 * R ? Math.Acos(1 - D / (2 * R)) : Math.PI / 2;
            const int n = 10;
            for (int i = 0; i <= n; i++)
            {
                var t = theta * i / n;
                half.Add(new Vec2(R * Math.Sin(t), R * (1 - Math.Cos(t))));
            }
            for (int i = n; i >= 0; i--)
            {
                var t = theta * i / n;
                half.Add(new Vec2(lt - R * Math.Sin(t), D - R * (1 - Math.Cos(t))));
            }
        }
        var res = new List<Vec2>(half);
        // Trecho reto, amostrado para acompanhar meios-fios curvos.
        for (var x = lt + Sample; x < L - lt - 1e-6; x += Sample) res.Add(new Vec2(x, D));
        res.AddRange(Enumerable.Reverse(half).Select(v => new Vec2(L - v.X, v.Y)));
        return res;
    }

    private static Func<Vec2, Vec2> StationMap(Polyline2 path, bool sidewalkOnLeft)
    {
        var sign = sidewalkOnLeft ? -1.0 : 1.0;
        return v =>
        {
            var s = Math.Clamp(v.X, 0, path.Length);
            return path.PointAt(s) + path.TangentAt(s).PerpLeft * (sign * v.Y);
        };
    }

    /// <summary>Contorno em planta da orelha (usado também para recortar as marcas da pista).</summary>
    public static Polygon2? EarFootprint(CurbExtensionDefinition d, Polyline2 path)
    {
        var L = path.Length;
        if (L < 0.5) return null;
        var map = StationMap(path, d.SidewalkOnLeft);
        var outer = EarProfile(d, L).Select(map).ToList();
        var back = new List<Vec2>();
        for (var x = L; x > 0; x -= Sample) back.Add(map(new Vec2(x, 0)));
        return PolygonOps.Union(new[] { new Polygon2(outer.Concat(back)) }).OrderByDescending(p => p.Area).FirstOrDefault();
    }

    public static MarkingGeometry CurbExtension(CurbExtensionDefinition d, Polyline2 path, BuildContext ctx)
    {
        var L = path.Length;
        if (L < 1.0) return Fail("Trecho da orelha muito curto (mínimo 1 m ao longo do meio-fio).");
        var geo = new MarkingGeometry();
        var fp = EarFootprint(d, path);
        if (fp == null) return Fail("Não foi possível montar o contorno da orelha.");
        EarProfile(d, L, geo.Warnings);
        var map = StationMap(path, d.SidewalkOnLeft);
        var cw = Math.Max(0.05, d.CurbWidth);

        var inner = PolygonOps.Offset(new[] { fp }, -cw);
        var ring = PolygonOps.Difference(new[] { fp }, inner);
        // O meio-fio existente permanece: remove a faixa junto à face original.
        var alongFace = PolygonOps.Strip(Enumerable.Range(0, (int)Math.Ceiling(L / Sample) + 1).Select(i => map(new Vec2(Math.Min(L, i * Sample), 0))).ToList(), 2 * cw + 0.02);
        var curb = PolygonOps.Difference(ring, alongFace);

        var planter = new List<Polygon2>();
        if (d.Planter)
        {
            var profile = EarProfile(d, L);
            var lt = profile.First(v => v.Y >= d.Depth - 1e-6).X;
            var x0 = lt + d.PlanterMargin;
            var x1 = L - lt - d.PlanterMargin;
            var o1 = d.Depth - cw - d.PlanterMargin;
            var o0 = Math.Max(0.6, o1 - d.PlanterWidth);
            if (x1 - x0 > 0.3 && o1 - o0 > 0.2)
            {
                var pts = new List<Vec2>();
                for (var x = x0; x <= x1 + 1e-6; x += Sample) pts.Add(map(new Vec2(Math.Min(x, x1), o0)));
                for (var x = x1; x >= x0 - 1e-6; x -= Sample) pts.Add(map(new Vec2(Math.Max(x, x0), o1)));
                planter = PolygonOps.Union(new[] { new Polygon2(pts) });
                if (d.Trees > 0)
                {
                    var om = (o0 + o1) / 2;
                    AddTrees(geo, ctx.Catalog, Enumerable.Range(0, d.Trees).Select(i =>
                    {
                        var x = x0 + (x1 - x0) * (i + 0.5) / d.Trees;
                        return (map(new Vec2(x, om)), path.TangentAt(Math.Clamp(x, 0, L)));
                    }), d.Height);
                }
            }
            else geo.Warnings.Add("Orelha pequena demais para o canteiro com as margens indicadas.");
        }

        var platform = PolygonOps.Difference(new[] { fp }, curb.Concat(planter));
        AddRaised(geo, platform, MarkingColor.Concreto, d.Height);
        AddRaised(geo, curb, MarkingColor.Concreto, d.Height);
        AddRaised(geo, planter, MarkingColor.Grama, d.Height);
        geo.PathLength = L;
        geo.PaintedLength = L;
        geo.UnitCount = 1;
        return geo;
    }

    // ------------------------------------------------------------------ área por contorno

    public static Polygon2? AreaOutline(SidewalkAreaDefinition d, Polyline2 path)
    {
        if (path.Points.Count < 3) return null;
        var polys = PolygonOps.Union(new[] { new Polygon2(path.Points) });
        var r = Math.Max(0, d.FilletRadius);
        if (r > 0.01 && polys.Count > 0)
        {
            polys = PolygonOps.Offset(PolygonOps.Offset(polys, -r, true), r, true);   // arredonda cantos convexos
            polys = PolygonOps.Offset(PolygonOps.Offset(polys, r, true), -r, true);   // e côncavos
        }
        return polys.OrderByDescending(p => p.Area).FirstOrDefault();
    }

    public static MarkingGeometry Area(SidewalkAreaDefinition d, Polyline2 path)
    {
        var outline = AreaOutline(d, path);
        if (outline == null || outline.Area < 0.01) return Fail("Contorno fechado inválido (desenhe ao menos 3 pontos).");
        var geo = new MarkingGeometry();
        var cw = Math.Max(0.05, d.CurbWidth);
        var shapes = new List<Polygon2> { outline };
        switch (d.Type)
        {
            case TipoAreaCalcada.Ciclovia:
                foreach (var s in shapes) geo.Pieces.Add(new MarkingPiece(s, MarkingColor.Vermelha) { Elevation = d.Height });
                break;
            case TipoAreaCalcada.Pavimento:
                AddRaised(geo, shapes, MarkingColor.Asfalto, 0.05, -0.05);
                break;
            default:
            {
                var body = shapes;
                if (d.Curb && d.Type != TipoAreaCalcada.FaixaServico)
                {
                    var ring = PolygonOps.Difference(shapes, PolygonOps.Offset(shapes, -cw));
                    AddRaised(geo, ring, MarkingColor.Concreto, d.Height);
                    body = PolygonOps.Offset(shapes, -cw);
                }
                var color = d.Type switch
                {
                    TipoAreaCalcada.Canteiro or TipoAreaCalcada.FaixaServico => MarkingColor.Grama,
                    TipoAreaCalcada.Deck => MarkingColor.Marrom,
                    _ => MarkingColor.Concreto,
                };
                AddRaised(geo, body, color, d.Height);
                break;
            }
        }
        geo.PathLength = path.Length;
        geo.PaintedLength = d.Type == TipoAreaCalcada.Ciclovia ? 0 : path.Length;
        geo.UnitCount = 1;
        return geo;
    }

    // ------------------------------------------------------------------ canteiros

    public static List<Polygon2> PlanterShapes(PlanterDefinition d, Polyline2 path)
    {
        var res = new List<Polygon2>();
        var axis = Math.Abs(d.Offset) > 1e-6 ? path.Offset(d.Offset) : path;
        var L = axis.Length;
        if (L < 0.2) return res;
        if (d.Spacing <= 0)
        {
            res.AddRange(PolygonOps.Strip(axis.Points, d.Width));
            return res;
        }
        var len = Math.Min(d.Length, L);
        var n = Math.Max(1, (int)Math.Floor((L - len) / d.Spacing) + 1);
        var start = (L - (len + (n - 1) * d.Spacing)) / 2;
        for (int i = 0; i < n; i++)
        {
            var s0 = start + i * d.Spacing;
            res.AddRange(PolygonOps.Strip(axis.SubPoints(s0, s0 + len), d.Width));
        }
        return res;
    }

    public static MarkingGeometry Planter(PlanterDefinition d, Polyline2 path, BuildContext ctx)
    {
        var shapes = PlanterShapes(d, path);
        if (shapes.Count == 0) return Fail("Linha dos canteiros muito curta.");
        var geo = new MarkingGeometry();
        var bw = Math.Max(0.03, d.BorderWidth);
        var core = PolygonOps.Offset(shapes, -bw);
        var ring = PolygonOps.Difference(shapes, core);
        switch (d.Type)
        {
            case TipoCanteiroCalcada.Jardineira:
                AddRaised(geo, ring, MarkingColor.Concreto, d.SurfaceHeight + d.BorderHeight);
                AddRaised(geo, core, MarkingColor.Grama, d.SurfaceHeight + Math.Max(0.02, d.BorderHeight - 0.05));
                break;
            case TipoCanteiroCalcada.GrelhaArvore:
                AddRaised(geo, ring, MarkingColor.Concreto, d.SurfaceHeight);
                AddRaised(geo, core, MarkingColor.Metal, d.SurfaceHeight);
                break;
            default:
                AddRaised(geo, ring, MarkingColor.Concreto, d.SurfaceHeight + 0.02);
                AddRaised(geo, core, MarkingColor.Grama, d.SurfaceHeight);
                break;
        }
        if (d.Trees)
        {
            var axis = Math.Abs(d.Offset) > 1e-6 ? path.Offset(d.Offset) : path;
            var spots = new List<(Vec2, Vec2)>();
            if (d.Spacing <= 0)
            {
                for (var s = 3.0; s < axis.Length - 1; s += 8) spots.Add((axis.PointAt(s), axis.TangentAt(s)));
            }
            else
            {
                foreach (var sh in shapes) spots.Add((sh.Centroid, axis.TangentAt(0)));
            }
            var top = d.Type == TipoCanteiroCalcada.Jardineira ? d.SurfaceHeight + d.BorderHeight - 0.05 : d.SurfaceHeight;
            AddTrees(geo, ctx.Catalog, spots, top);
        }
        geo.PathLength = path.Length;
        geo.UnitCount = d.Spacing <= 0 ? 1 : shapes.Count;
        return geo;
    }

    // ------------------------------------------------------------------ cul-de-sac

    /// <summary>Pavimento do balão em coordenadas locais (x à direita, y ao longo do eixo; início em y = 0).</summary>
    public static (List<Polygon2> Pavement, Polygon2? Island, Vec2 BulbCenter) CulDeSacLocal(CulDeSacDefinition d, double axisLength, List<string>? warnings = null)
    {
        var W = Math.Max(3, d.RoadWidth);
        var hw = W / 2;
        var Lr = Math.Max(1, axisLength);
        var Rb = Math.Max(hw + 0.5, d.BulbRadius);
        var parts = new List<Polygon2> { Polygon2.Rectangle(new Vec2(-hw, -3), new Vec2(hw, Lr)) };
        var center = new Vec2(0, Lr);
        Polygon2 Circle(Vec2 c, double r) => new(CurveTools.Circle(c, r, 0.01));
        Vec2 Dir(double deg) => new(Math.Sin(deg * Math.PI / 180), Math.Cos(deg * Math.PI / 180));

        switch (d.Type)
        {
            case TipoCulDeSac.ExcentricoEsquerda:
                center = new Vec2(-(Rb - hw), Lr);
                parts.Add(Circle(center, Rb));
                break;
            case TipoCulDeSac.ExcentricoDireita:
                center = new Vec2(Rb - hw, Lr);
                parts.Add(Circle(center, Rb));
                break;
            case TipoCulDeSac.Martelo:
            {
                var hl = Math.Max(W * 2, d.HeadLength) / 2;
                parts.Add(Polygon2.Rectangle(new Vec2(-hl, Lr - hw), new Vec2(hl, Lr + hw)));
                break;
            }
            case TipoCulDeSac.EmY:
            {
                var a = Math.Clamp(d.BranchAngle, 15, 80);
                parts.Add(Circle(center, hw));
                parts.AddRange(PolygonOps.Strip(new[] { center, center + Dir(a) * d.BranchLength }, W));
                parts.AddRange(PolygonOps.Strip(new[] { center, center + Dir(-a) * d.BranchLength }, W));
                break;
            }
            case TipoCulDeSac.EmLEsquerda:
            case TipoCulDeSac.EmLDireita:
            {
                var sgn = d.Type == TipoCulDeSac.EmLDireita ? 1 : -1;
                parts[0] = Polygon2.Rectangle(new Vec2(-hw, -3), new Vec2(hw, Lr + hw));
                parts.AddRange(PolygonOps.Strip(new[] { center, center + new Vec2(sgn * d.BranchLength, 0) }, W));
                break;
            }
            case TipoCulDeSac.Gota:
            {
                // Balão alongado: círculo mais uma elipse na direção do eixo.
                parts.Add(Circle(center, Rb));
                parts.Add(new Polygon2(Enumerable.Range(0, 48).Select(i =>
                {
                    var t = i * Math.PI * 2 / 48;
                    return center + new Vec2(Math.Cos(t) * Rb * 0.8, -Rb * 0.6 + Math.Sin(t) * Rb * 1.1);
                })));
                break;
            }
            default:
                parts.Add(Circle(center, Rb));
                break;
        }

        var pav = PolygonOps.Union(parts);
        var rt = Math.Max(0, d.TransitionRadius);
        if (rt > 0.05) pav = PolygonOps.Offset(PolygonOps.Offset(pav, rt, true), -rt, true);
        if (d.Type is TipoCulDeSac.Martelo or TipoCulDeSac.EmY or TipoCulDeSac.EmLEsquerda or TipoCulDeSac.EmLDireita)
            pav = PolygonOps.Offset(PolygonOps.Offset(pav, -1.0, true), 1.0, true); // cantos externos arredondados (R = 1 m)
        var half = Polygon2.Rectangle(new Vec2(-5000, 0), new Vec2(5000, 5000));
        pav = PolygonOps.Intersect(pav, new[] { half });

        Polygon2? island = null;
        var bulb = d.Type is TipoCulDeSac.Circular or TipoCulDeSac.ExcentricoEsquerda or TipoCulDeSac.ExcentricoDireita or TipoCulDeSac.Gota;
        if (d.Island)
        {
            if (!bulb) warnings?.Add("Ilha central disponível apenas nos balões circulares, excêntricos e em gota.");
            else if (d.IslandRadius > Rb - 4.0) warnings?.Add($"Ilha de {d.IslandRadius:0.0} m deixa menos de 4 m de pista em volta – reduza a ilha ou aumente o balão.");
            else island = Circle(center, Math.Max(0.5, d.IslandRadius));
        }
        if (bulb && Rb < 9.0)
            warnings?.Add($"Raio do balão de {Rb:0.0} m: veículos de coleta, mudança e emergência costumam exigir 9–12 m até o meio-fio – confira a legislação municipal.");
        return (pav, island, center);
    }

    public static MarkingGeometry CulDeSac(CulDeSacDefinition d, Polyline2 path)
    {
        if (path.Points.Count < 2) return Fail("Eixo do cul-de-sac não encontrado.");
        var a = path.Points[0];
        var b = path.Points[^1];
        var axisLen = a.DistanceTo(b);
        if (axisLen < 1) return Fail("Clique o início do balão e o centro/fim da via (distância mínima 1 m).");
        var geo = new MarkingGeometry();
        var (pav, island, _) = CulDeSacLocal(d, axisLen, geo.Warnings);
        var frame = new LocalFrame(a, (b - a).Normalized());
        var half = new[] { Polygon2.Rectangle(new Vec2(-5000, 0), new Vec2(5000, 5000)) };
        var cw = Math.Max(0.05, d.CurbWidth);

        var roadway = island != null ? PolygonOps.Difference(pav, new[] { island }) : pav;
        var curb = PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(pav, cw, true), pav), half);
        var walk = PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(pav, cw + Math.Max(0, d.SidewalkWidth), true), PolygonOps.Offset(pav, cw, true)), half);

        List<Polygon2> W(IEnumerable<Polygon2> local) => local.Select(frame.ToWorld).ToList();
        if (d.Pavement) AddRaised(geo, W(roadway), MarkingColor.Asfalto, d.PavementThickness, -d.PavementThickness);
        AddRaised(geo, W(curb), MarkingColor.Concreto, d.Height);
        if (d.SidewalkWidth > 0.05) AddRaised(geo, W(walk), MarkingColor.Concreto, d.Height);
        if (island != null)
        {
            var islandCore = PolygonOps.Offset(new[] { island }, -cw);
            AddRaised(geo, W(PolygonOps.Difference(new[] { island }, islandCore)), MarkingColor.Concreto, d.Height);
            AddRaised(geo, W(islandCore), MarkingColor.Grama, d.Height);
        }
        if (d.EdgeLine)
        {
            var band = PolygonOps.Difference(PolygonOps.Offset(pav, -0.30, true), PolygonOps.Offset(pav, -0.40, true));
            band = PolygonOps.Intersect(band, new[] { Polygon2.Rectangle(new Vec2(-5000, 0.3), new Vec2(5000, 5000)) });
            if (island != null) band = PolygonOps.Difference(band, PolygonOps.Offset(new[] { island }, 0.3, true));
            foreach (var s in W(band)) geo.Pieces.Add(new MarkingPiece(s, MarkingColor.Branca));
        }
        geo.PathLength = axisLen;
        geo.PaintedLength = axisLen;
        geo.UnitCount = 1;
        return geo;
    }
}
