using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Revit.Infrastructure;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Faces de topo de um conjunto de elementos, indexadas em planta: cota do topo sob um ponto (raio vertical).</summary>
internal sealed class TopProbe
{
    private const double Cell = 20;   // pés
    private readonly List<(Face Face, double X0, double Y0, double X1, double Y1)> _faces = new();
    private readonly Dictionary<(int, int), List<int>> _grid = new();
    private double _zMin = double.MaxValue, _zMax = double.MinValue;

    public int Count => _faces.Count;

    public TopProbe(IEnumerable<Element> elements)
    {
        foreach (var e in elements)
            foreach (var s in AutoTestGeo.Solids(e))
                foreach (Face f in s.Faces)
                {
                    if (!AutoTestGeo.Up(f)) continue;
                    Mesh? m;
                    try { m = f.Triangulate(); } catch { continue; }
                    if (m == null || m.Vertices.Count == 0) continue;
                    double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                    foreach (var v in m.Vertices)
                    {
                        x0 = Math.Min(x0, v.X); y0 = Math.Min(y0, v.Y); x1 = Math.Max(x1, v.X); y1 = Math.Max(y1, v.Y);
                        _zMin = Math.Min(_zMin, v.Z); _zMax = Math.Max(_zMax, v.Z);
                    }
                    var idx = _faces.Count;
                    _faces.Add((f, x0, y0, x1, y1));
                    for (var i = (int)Math.Floor(x0 / Cell); i <= (int)Math.Floor(x1 / Cell); i++)
                        for (var j = (int)Math.Floor(y0 / Cell); j <= (int)Math.Floor(y1 / Cell); j++)
                        {
                            if (!_grid.TryGetValue((i, j), out var l)) _grid[(i, j)] = l = new List<int>();
                            l.Add(idx);
                        }
                }
    }

    /// <summary>Maior cota (m) das faces de topo na vertical do ponto (m), abaixo de <paramref name="belowM"/>; nulo se não há nada.</summary>
    public double? TopZ(Vec2 p, double? belowM = null)
    {
        if (_faces.Count == 0) return null;
        var x = UnitConv.Ft(p.X);
        var y = UnitConv.Ft(p.Y);
        if (!_grid.TryGetValue(((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell)), out var list)) return null;
        double? best = null;
        Line? line = null;
        var lim = belowM is { } b ? UnitConv.Ft(b) : double.MaxValue;
        foreach (var k in list)
        {
            var (f, x0, y0, x1, y1) = _faces[k];
            if (x < x0 - 1e-3 || x > x1 + 1e-3 || y < y0 - 1e-3 || y > y1 + 1e-3) continue;
            line ??= Line.CreateBound(new XYZ(x, y, _zMax + 30), new XYZ(x, y, _zMin - 30));
            try
            {
                if (f.Intersect(line, out var res) != SetComparisonResult.Overlap || res == null) continue;
                for (int i = 0; i < res.Size; i++)
                {
                    var z = res.get_Item(i).XYZPoint.Z;
                    if (z > lim) continue;
                    if (best == null || z > best) best = z;
                }
            }
            catch { /* face degenerada */ }
        }
        return best is { } r ? UnitConv.M(r) : null;
    }
}

internal static class AutoTestGeo
{
    public static IEnumerable<Solid> Solids(Element e)
    {
        GeometryElement? ge;
        try { ge = e.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine }); }
        catch { yield break; }
        if (ge == null) yield break;
        foreach (var s in Solids(ge)) yield return s;
    }

    public static IEnumerable<Solid> Solids(GeometryElement ge)
    {
        foreach (var o in ge)
        {
            if (o is Solid { Volume: > 1e-9 } s) yield return s;
            else if (o is GeometryInstance gi)
                foreach (var x in Solids(gi.GetInstanceGeometry())) yield return x;
        }
    }

    public static bool HasGeometry(Element e)
    {
        try
        {
            var ge = e.get_Geometry(new Options { ComputeReferences = false });
            return ge != null && ge.Any(o => o is Solid { Volume: > 1e-9 } or Mesh or Curve or GeometryInstance);
        }
        catch { return false; }
    }

    public static bool Up(Face f)
    {
        try
        {
            if (f is PlanarFace pf) return pf.FaceNormal.Z > 0.25;
            var bb = f.GetBoundingBox();
            return f.ComputeNormal((bb.Min + bb.Max) / 2).Z > 0.25;
        }
        catch { return false; }
    }

    /// <summary>Pontos (m) do topo do elemento: centroides de até <paramref name="max"/> triângulos das faces de cima.</summary>
    public static IEnumerable<(Vec2 P, double Z)> TopSamples(Element e, int max)
    {
        var all = new List<(Vec2, double)>();
        foreach (var s in Solids(e))
            foreach (Face f in s.Faces)
            {
                if (!Up(f)) continue;
                Mesh? m;
                try { m = f.Triangulate(); } catch { continue; }
                if (m == null) continue;
                for (int i = 0; i < m.NumTriangles; i++)
                {
                    var t = m.get_Triangle(i);
                    var c = (t.get_Vertex(0) + t.get_Vertex(1) + t.get_Vertex(2)) / 3;
                    all.Add((new Vec2(UnitConv.M(c.X), UnitConv.M(c.Y)), UnitConv.M(c.Z)));
                }
            }
        if (all.Count <= max) return all;
        var step = all.Count / (double)max;
        return Enumerable.Range(0, max).Select(i => all[(int)(i * step)]);
    }

    public static bool Overlap(BoundingBoxXYZ a, BoundingBoxXYZ b) =>
        a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;
}

internal sealed partial class AutoTestRunner
{
    private static bool Painted(MarkingDefinition d) =>
        d is SymbolMarkingDefinition or TextMarkingDefinition or HatchMarkingDefinition or RepeatedMarkingDefinition or ParkingMarkingDefinition
        || (d is LinearMarkingDefinition && FootprintCutter.PaintedLine(d));

    private static bool Node(MarkingDefinition d) => d is IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition;

    private void Checks()
    {
        var all = MarkingStorage.All(_doc).Where(r => !_before.Contains(r.MarkingId) && r.Element.IsValidObject).ToList();
        var nodes = all.Where(r => Node(r.Definition)).ToList();
        foreach (var pav in all.Select(r => r.Definition).OfType<RoadPavementDefinition>().GroupBy(p => p.Id).Select(g => g.First()).ToList())
        {
            if (pav.GroupId == null) continue;
            var name = _roadNames.GetValueOrDefault(pav.GroupId, pav.DisplayCode);
            Step("Verificações do modelo", $"Via \"{name}\"", s => CheckRoad(s, pav, all, nodes));
        }
        Step("Verificações do modelo", "Obras × outras vias (interferências)", s => CheckStructures(s, all));
        Step("Verificações do modelo", "Elementos sem geometria", s =>
        {
            var empty = all.Where(r => r.Element is DirectShape && !AutoTestGeo.HasGeometry(r.Element)).ToList();
            s.Note($"{all.Count} elemento(s) do teste conferidos.");
            foreach (var g in empty.GroupBy(r => r.Definition.DisplayCode))
                s.Error($"{g.Key}: {g.Count()} forma(s) direta(s) sem geometria.");
        });
    }

    private void CheckRoad(StepReport s, RoadPavementDefinition pav, List<StoredMarking> all, List<StoredMarking> nodes)
    {
        var gid = pav.GroupId!;
        var road = RoadWorks.RoadByGroup(_doc, gid);
        if (road == null) { s.Error("Eixo da via não encontrado."); return; }
        var axis = road.Axis;
        var mine = all.Where(r => r.Definition.GroupId == gid || (r.Definition is IHostedStructure h && h.HostRoad == gid)).ToList();
        var pavEls = mine.Where(r => r.Definition is RoadPavementDefinition or BridgeDefinition).Select(r => r.Element).ToList();
        // Interseções, rotatórias e balões que tocam a via também são pavimento dela.
        var box = Box(pavEls);
        if (box != null) pavEls.AddRange(nodes.Where(n => n.Element.get_BoundingBox(null) is { } b && AutoTestGeo.Overlap(b, box)).Select(n => n.Element));
        var probe = new TopProbe(pavEls);
        if (probe.Count == 0) { s.Error("A via não tem pisos/sólidos de pavimento com face de topo."); return; }

        // 1) Pintura sobre o pavimento.
        int n = 0, hidden = 0, floating = 0, none = 0;
        double worst = 0;
        Vec2? worstAt = null;
        foreach (var r in mine.Where(r => Painted(r.Definition)))
            foreach (var (p, z) in AutoTestGeo.TopSamples(r.Element, 16))
            {
                n++;
                var top = probe.TopZ(p, z + 0.10);
                if (top == null) { none++; continue; }
                var d = z - top.Value;
                if (d < -0.0005) { hidden++; if (d < worst) { worst = d; worstAt = p; } }
                else if (d > 0.03) floating++;
            }
        if (n == 0) s.Note("Sem pintura na via.");
        else
        {
            s.Note($"Pintura: {n} pontos – {hidden} escondidos sob o piso, {floating} flutuando (> 3 cm), {none} sem pavimento embaixo.");
            if (hidden > n * 0.02) s.Error($"Pintura ESCONDIDA sob o pavimento em {100.0 * hidden / n:0}% dos pontos (pior: {-worst * 1000:0} mm abaixo em {L(worstAt!.Value)}).");
            else if (hidden > 0) s.Warn($"Pintura escondida em {hidden} ponto(s) (pior {-worst * 1000:0} mm em {L(worstAt!.Value)}).");
            if (floating > n * 0.05) s.Warn($"Pintura flutuando mais de 3 cm acima do piso em {100.0 * floating / n:0}% dos pontos.");
        }

        // 2) Continuidade do pavimento: buracos e degraus entre pisos.
        var offsets = new List<double>();
        for (var o = -(pav.RightWidth - 0.4); o <= pav.LeftWidth - 0.4 + 1e-6; o += 1.0) if (Math.Abs(o) > 0.6) offsets.Add(o);
        int holes = 0, steps = 0, count = 0;
        double maxStep = 0;
        Vec2? holeAt = null, stepAt = null;
        foreach (var o in offsets)
        {
            double? z1 = null, z2 = null;
            for (var st = 1.0; st < axis.Length - 1; st += 2)
            {
                var pt = axis.PointAt(st) + axis.TangentAt(st).PerpLeft * o;
                var z = probe.TopZ(pt);
                count++;
                if (z == null) { holes++; holeAt ??= pt; z1 = z2 = null; continue; }
                if (z1 != null && z2 != null)
                {
                    var d2 = Math.Abs(z2.Value - 2 * z1.Value + z.Value);
                    if (d2 > 0.015) { steps++; if (d2 > maxStep) { maxStep = d2; stepAt = pt; } }
                }
                z2 = z1;
                z1 = z;
            }
        }
        s.Note($"Pavimento: {count} pontos – {holes} sem piso, {steps} degraus > 1,5 cm (máx. {maxStep * 100:0.0} cm).");
        if (holes > count * 0.02) s.Error($"Buracos no pavimento em {100.0 * holes / count:0}% dos pontos (primeiro em {L(holeAt!.Value)}).");
        else if (holes > 0) s.Warn($"{holes} ponto(s) sem piso sob a pista (primeiro em {L(holeAt!.Value)}).");
        if (steps > 0) s.Error($"{steps} degrau(s) entre pisos da pista (máx. {maxStep * 100:0.0} cm em {L(stepAt!.Value)}).");

        // 3) Terreno × via (vias que moldam o Toposolid).
        if (pav.Output.Grade is not { AdjustTerrain: true } || TerrainModel.Ground(_doc) is not { } ground) return;
        var hosted = RoadWorks.Hosted(_doc, gid).OfType<IHostedStructure>().Select(h => (Math.Min(h.HostStart, h.HostEnd) - 15, Math.Max(h.HostStart, h.HostEnd) + 15)).ToList();
        var sect = new TopProbe(mine.Where(r => Core.Generators.IntersectionGenerator.IsPhysical(r.Definition)).Select(r => r.Element).Concat(pavEls));
        int above = 0, mism = 0, tn = 0;
        double worstEdge = 0;
        Vec2? edgeAt = null;
        for (var st = 5.0; st < axis.Length - 5; st += 5)
        {
            if (hosted.Any(h => st > h.Item1 && st < h.Item2)) continue;
            var c = axis.PointAt(st);
            var nrm = axis.TangentAt(st).PerpLeft;
            var top = probe.TopZ(c);
            if (top == null || ground(c) is not { } g) continue;
            tn++;
            if (g > top.Value + 0.03) above++;
            foreach (var side in new[] { pav.TotalLeft, -pav.TotalRight })
            {
                var inner = c + nrm * (side - Math.Sign(side) * 0.3);
                var outer = c + nrm * (side + Math.Sign(side) * 0.5);
                if (sect.TopZ(inner) is not { } e || ground(outer) is not { } go) continue;
                var d = go - e;
                if (Math.Abs(d) > 0.3) { mism++; if (Math.Abs(d) > Math.Abs(worstEdge)) { worstEdge = d; edgeAt = outer; } }
            }
        }
        s.Note($"Terreno: {tn} seções – terreno acima da pista em {above}; desnível > 30 cm no limite da seção em {mism} lado(s).");
        if (above > 0) s.Error($"O Toposolid fica ACIMA da pista em {above} de {tn} seções (fora de túneis/pontes).");
        if (mism > tn * 0.1) s.Error($"O terreno não encosta na borda da seção: até {worstEdge:+0.00;-0.00} m em {L(edgeAt!.Value)} ({mism} lados).");
        else if (mism > 0) s.Warn($"Desnível de até {worstEdge:+0.00;-0.00} m entre o terreno e a borda da seção em {L(edgeAt!.Value)}.");
    }

    private static BoundingBoxXYZ? Box(IEnumerable<Element> els)
    {
        BoundingBoxXYZ? box = null;
        foreach (var e in els)
        {
            var b = e.get_BoundingBox(null);
            if (b == null) continue;
            box = box == null
                ? new BoundingBoxXYZ { Min = b.Min, Max = b.Max }
                : new BoundingBoxXYZ
                {
                    Min = new XYZ(Math.Min(box.Min.X, b.Min.X), Math.Min(box.Min.Y, b.Min.Y), Math.Min(box.Min.Z, b.Min.Z)),
                    Max = new XYZ(Math.Max(box.Max.X, b.Max.X), Math.Max(box.Max.Y, b.Max.Y), Math.Max(box.Max.Z, b.Max.Z)),
                };
        }
        return box;
    }

    /// <summary>Estruturas de pontes/viadutos/túneis que entram no pavimento ou nas calçadas de outra via.</summary>
    private void CheckStructures(StepReport s, List<StoredMarking> all)
    {
        var works = all.Where(r => r.Definition is BridgeDefinition or TunnelDefinition or TrenchDefinition or RetainingWallDefinition).ToList();
        s.Note($"{works.Select(w => w.MarkingId).Distinct().Count()} obra(s) conferidas.");
        foreach (var w in works.GroupBy(r => r.MarkingId))
        {
            var def = w.First().Definition;
            var host = (def as IHostedStructure)?.HostRoad;
            var others = all.Where(r => r.Definition.GroupId != null && r.Definition.GroupId != host
                                        && (r.Definition is RoadPavementDefinition || Core.Generators.IntersectionGenerator.IsPhysical(r.Definition))).ToList();
            var solidsW = w.SelectMany(r => AutoTestGeo.Solids(r.Element).Select(x => (Solid: x, Box: r.Element.get_BoundingBox(null)))).Where(x => x.Box != null).ToList();
            var hits = new Dictionary<string, double>();
            foreach (var o in others)
            {
                var ob = o.Element.get_BoundingBox(null);
                if (ob == null || !solidsW.Any(x => AutoTestGeo.Overlap(x.Box!, ob))) continue;
                foreach (var so in AutoTestGeo.Solids(o.Element))
                    foreach (var (sw, _) in solidsW)
                    {
                        try
                        {
                            var i = BooleanOperationsUtils.ExecuteBooleanOperation(sw, so, BooleanOperationsType.Intersect);
                            var vol = (i?.Volume ?? 0) * 0.0283168;
                            if (vol < 0.02) continue;
                            var key = $"{_roadNames.GetValueOrDefault(o.Definition.GroupId!, "via")} ({o.Definition.DisplayCode})";
                            hits[key] = hits.GetValueOrDefault(key) + vol;
                        }
                        catch { /* booleana falhou: sem conclusão */ }
                    }
            }
            foreach (var (k, v) in hits) s.Error($"{def.KindName} {def.DisplayCode} invade {k}: {v:0.00} m³ de estrutura dentro dela.");
        }
    }
}
