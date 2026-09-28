using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Opções da terraplenagem.</summary>
public sealed class GradingOptions
{
    /// <summary>Talude de corte padrão (H:V) para vias e conexões.</summary>
    public double CutSlope { get; set; } = 1.0;
    /// <summary>Talude de aterro padrão (H:V) para vias e conexões.</summary>
    public double FillSlope { get; set; } = 1.5;
    /// <summary>Afastamento máximo procurado para o encontro do talude com o terreno (m).</summary>
    public double MaxDaylight { get; set; } = 60;
    /// <summary>Profundidade do subleito sob o topo do pavimento (m) – o terreno fica sob a estrutura do pavimento.</summary>
    public double Subgrade { get; set; } = 0.40;
    /// <summary>Escavar o Toposolid com os elementos enterrados (túneis, caixas de drenagem), quando o Revit permitir.</summary>
    public bool Excavate { get; set; } = true;
    /// <summary>Sem Toposolid no projeto: cria um terreno nativo plano sob as obras (margem de <see cref="NewTerrainMargin"/> m).</summary>
    public bool CreateIfMissing { get; set; }
    public double NewTerrainMargin { get; set; } = 80;
    /// <summary>Grama de taludes e ilhas como subdivisões do Toposolid (material nativo).</summary>
    public bool Finishes { get; set; } = true;
}

public sealed class TerrainReport
{
    public int Toposolids { get; set; }
    public int Removed { get; set; }
    public int Added { get; set; }
    public int Excavated { get; set; }
    public int Subdivisions { get; set; }
    public bool Created { get; set; }
    public double Cut { get; set; }
    public double Fill { get; set; }
    public double Area { get; set; }
    public List<string> Notes { get; } = new();

    public string Text()
    {
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var s = $"Toposolid(s) ajustado(s): {Toposolids}\nPontos removidos / inseridos: {Removed} / {Added}\n" +
                $"Área terraplenada: {Area.ToString("N0", pt)} m²\nCorte: {Cut.ToString("N1", pt)} m³ · Aterro: {Fill.ToString("N1", pt)} m³";
        if (Created) s = "Toposolid criado sob as obras (Massa e terreno).\n" + s;
        if (Excavated > 0) s += $"\nEscavações pelo modelo (túneis, caixas): {Excavated}";
        if (Subdivisions > 0) s += $"\nAcabamentos do terreno (subdivisões com grama/revestimento): {Subdivisions}";
        if (Notes.Count > 0) s += "\n\n" + string.Join("\n", Notes.Distinct().Take(12));
        return s;
    }
}

/// <summary>
/// Terraplenagem no terreno nativo do Revit (Massa e terreno → Toposolid): as vias, conexões e obras ficam no seu greide e o
/// Toposolid é ajustado a elas – pontos do terreno dentro da área de projeto são substituídos pela superfície de projeto
/// (plataformas + taludes de corte e aterro até encontrar o terreno natural). Também escava o terreno com elementos
/// enterrados quando o Revit permite.
/// </summary>
public static class TerrainService
{
    /// <summary>Faixas e plataformas de projeto de uma marca (cotas absolutas, m).</summary>
    public static (List<GradeCorridor> Corridors, List<GradePad> Pads) Features(Document doc, MarkingDefinition def, MarkingGeometry geo, double baseZ, GradingOptions opt,
        IReadOnlyCollection<MarkingDefinition>? all = null)
    {
        var corridors = new List<GradeCorridor>();
        var pads = new List<GradePad>();
        var z0 = baseZ + def.Output.ElevationOffset;
        foreach (var c in geo.Corridors)
        {
            var a = new GradeCorridor { CutSlope = c.CutSlope, FillSlope = c.FillSlope, DaylightLeft = c.DaylightLeft, DaylightRight = c.DaylightRight, Label = c.Label };
            a.Left.AddRange(c.Left.Select(p => p with { Z = p.Z + baseZ }));
            a.Right.AddRange(c.Right.Select(p => p with { Z = p.Z + baseZ }));
            corridors.Add(a);
        }
        pads.AddRange(geo.Pads.Select(p => new GradePad(p.Area, p.Z + baseZ) { CutSlope = p.CutSlope, FillSlope = p.FillSlope, Daylight = p.Daylight, Label = p.Label }));
        switch (def)
        {
            case RoadPavementDefinition road when PathResolver.Resolve(doc, road.Path)?.Main is { } axis:
            {
                // Via: plataforma do bordo externo de uma calçada ao da outra, no greide, sob a estrutura do pavimento – fora dos
                // trechos em obra (sob pontes e viadutos o terreno fica natural; túneis e trincheiras cuidam do próprio trecho).
                var g = road.Output.Grade;
                var prof = g != null ? g.ToProfile(2) : VerticalProfile.Flat(0);
                var cut = g?.CutSlope ?? opt.CutSlope;
                var fill = g?.FillSlope ?? opt.FillSlope;
                var right = -(road.TotalRight + 0.3);
                var left = road.TotalLeft + 0.3;
                var s0 = Math.Max(0, road.StartSetback);
                var s1 = Math.Max(s0 + 0.5, axis.Length - Math.Max(0, road.EndSetback));
                var gaps = (all ?? Array.Empty<MarkingDefinition>()).OfType<IHostedStructure>()
                    .Where(h => road.GroupId != null && h.HostRoad == road.GroupId && h is ITerrainAware { AdjustTerrain: true } or BridgeDefinition)
                    .Select(h => (A: Math.Min(h.HostStart, h.HostEnd), B: Math.Max(h.HostStart, h.HostEnd))).OrderBy(x => x.A).ToList();
                var cursor = s0;
                foreach (var (a, b) in gaps.Append((s1 + 1, s1 + 2)))
                {
                    var e = Math.Min(a, s1);
                    if (e - cursor > 2)
                        corridors.Add(Core.Generators.Infra.Corridor(axis, prof, right, left, cursor, e, opt.Subgrade, cut, fill, label: "Via")
                            .WithOffset(z0));
                    cursor = Math.Max(cursor, b);
                    if (cursor >= s1) break;
                }
                break;
            }
            case IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition:
            {
                // Conexões: plataforma plana com o contorno de todas as peças.
                var union = Core.Geometry.PolygonOps.Union(geo.Pieces.Where(p => p.Elevation < 0.5).Select(p => p.Shape));
                foreach (var u in union.Where(u => u.Area > 1))
                    pads.Add(new GradePad(u, z0 - opt.Subgrade) { CutSlope = opt.CutSlope, FillSlope = opt.FillSlope, Label = def.KindName });
                break;
            }
        }
        return (corridors, pads);
    }

    /// <summary>Marcas que podem participar da terraplenagem.</summary>
    public static bool Grades(MarkingDefinition d) =>
        d is ITerrainAware or RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition or DrainageDefinition;

    /// <summary>
    /// Marcas que moldam o terreno de fato: obras com "ajustar o terreno", vias com greide (e as conexões nas pontas delas) e
    /// caixas de drenagem (escavação).
    /// </summary>
    public static List<MarkingDefinition> Participants(Document doc, IReadOnlyCollection<MarkingDefinition> all)
    {
        var roads = all.OfType<RoadPavementDefinition>().Where(r => r.Output.Grade is { AdjustTerrain: true }).ToList();
        var ends = new List<Vec2>();
        foreach (var r in roads)
            if (PathResolver.Resolve(doc, r.Path)?.Main is { } ax) { ends.Add(ax.Points[0]); ends.Add(ax.Points[^1]); }
        bool NearGraded(Vec2 p) => ends.Any(e => e.DistanceTo(p) < 40);
        return all.Where(d => d switch
        {
            ITerrainAware t => t.AdjustTerrain,
            RoadPavementDefinition r => r.Output.Grade is { AdjustTerrain: true },
            IntersectionDefinition it => NearGraded(it.Node),
            RoundaboutDefinition rb => NearGraded(rb.Center),
            CulDeSacDefinition => false,
            DrainageDefinition => true,
            _ => false,
        }).ToList();
    }

    /// <summary>
    /// Refaz o terreno nativo (Toposolid) com TODAS as obras que o moldam, a partir do terreno original: pontos dentro da área
    /// terraplenada (atual e anterior) são substituídos pelos pontos originais fora do projeto + a superfície de projeto.
    /// <paramref name="changed"/> traz as definições recém-editadas (ainda não gravadas). Deve ser chamado dentro de uma transação.
    /// </summary>
    public static TerrainReport Apply(Document doc, MarkingService service, IEnumerable<MarkingDefinition> changed, GradingOptions opt)
    {
        var report = new TerrainReport();
        var byId = new Dictionary<string, MarkingDefinition>();
        foreach (var d in MarkingStorage.Definitions(doc)) byId[d.Id] = d;
        foreach (var d in changed) byId[d.Id] = d;
        var all = byId.Values.ToList();
        var list = Participants(doc, all);
        if (TerrainModel.Hosts(doc).Count == 0)
        {
            if (!opt.CreateIfMissing || !CreateTerrain(doc, service, list, opt, report))
            {
                if (!report.Notes.Any())
                    report.Notes.Add("Nenhum Toposolid no projeto (Massa e terreno → Sólido topográfico). Crie o terreno ou use Terraplenagem com \"criar terreno\".");
                if (new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Topography).WhereElementIsNotElementType().Any())
                    report.Notes.Add("Há uma superfície topográfica antiga: converta-a em Sólido topográfico (Massa e terreno → Sólido topográfico → Criar a partir da topografia).");
                return report;
            }
            service.InvalidateTerrain();
        }

        var corridors = new List<GradeCorridor>();
        var pads = new List<GradePad>();
        var excavators = new List<ElementId>();
        var finishes = new List<(Polygon2 Area, MarkingColor Color, string Id)>();
        foreach (var def in list)
        {
            try
            {
                var geo = service.BuildGeometry(def, out var baseZ);
                var (c, p) = Features(doc, def, geo, baseZ, opt, all);
                corridors.AddRange(c);
                pads.AddRange(p);
                finishes.AddRange(geo.TerrainFinishes.Select(f => (f.Area, f.Color, def.Id)));
                if (opt.Excavate && def is TunnelDefinition or DrainageDefinition)
                    excavators.AddRange(MarkingStorage.ById(doc, def.Id).Select(r => r.Element.Id));
            }
            catch (Exception ex)
            {
                Log.Error($"Terraplenagem {def.DisplayCode}", ex);
                report.Notes.Add($"{def.DisplayCode}: {ex.Message}");
            }
        }

        var topos = TerrainModel.Hosts(doc);
        var originals = topos.ToDictionary(t => t.Id, TerrainModel.Original);
        double? Ground(Vec2 p)
        {
            double? best = null;
            foreach (var m in originals.Values) if (m.Z(p) is { } z && (best == null || z > best)) best = z;
            return best;
        }
        var design = Grading.Design(corridors, pads, Ground, 0, Math.Max(5, opt.MaxDaylight), 0.5, 4.0, 1.0);
        report.Cut = design.CutM3;
        report.Fill = design.FillM3;
        report.Area = design.AreaM2;
        report.Notes.AddRange(design.Warnings);
        var footprint = design.Footprint.Count == 0 ? new List<Polygon2>() : PolygonOps.Union(design.Footprint).Where(f => f.Area > 0.5).ToList();

        foreach (var topo in topos)
        {
            var orig = originals[topo.Id];
            var last = TerrainModel.LastFootprint(topo);
            var region = footprint.Concat(last).ToList();
            if (region.Count == 0 || orig.Count == 0) continue;
            if (!Overlaps(topo, Bounds(region))) continue;
            try
            {
                var editor = topo.GetSlabShapeEditor();
                if (editor == null) continue;
                if (!editor.IsEnabled) editor.Enable();
                var inRegion = TerrainRebuild.Mask(region, 0.5);
                // 1) Sai tudo o que está na área (atual ou anterior): pontos de terraplenagens passadas e do levantamento.
                var vertices = new List<SlabShapeVertex>();
                foreach (SlabShapeVertex v in editor.SlabShapeVertices) vertices.Add(v);
                foreach (var v in vertices)
                {
                    var xy = new Vec2(UnitConv.M(v.Position.X), UnitConv.M(v.Position.Y));
                    if (!inRegion(xy)) continue;
                    try { if (editor.DeletePoint(v)) report.Removed++; } catch { /* vértice do contorno */ }
                }
                // 2) Entra o terreno original fora do projeto + a superfície de projeto combinada (plataformas e taludes).
                var pts = TerrainRebuild.Points(orig, design, inRegion);
                var add = pts.Select(p => new XYZ(UnitConv.Ft(p.X), UnitConv.Ft(p.Y), UnitConv.Ft(p.Z))).ToList();
                var added = new List<SlabShapeVertex>();
                if (add.Count > 0)
                {
                    try { added.AddRange(editor.AddPoints(add)); report.Added += add.Count; }
                    catch (Exception ex)
                    {
                        Log.Error("Toposolid.AddPoints", ex);
                        foreach (var q in add)
                            try { added.Add(editor.AddPoint(q)); report.Added++; } catch { /* ponto na borda ou repetido */ }
                    }
                }
                doc.Regenerate();
                report.Toposolids++;
                FixOffset(doc, editor, add, added, report);
                TerrainModel.Save(topo, orig, footprint);
            }
            catch (Exception ex)
            {
                Log.Error("Terraplenagem do Toposolid", ex);
                report.Notes.Add($"Toposolid {topo.Id}: {ex.Message}");
            }
        }
        service.InvalidateTerrain();

        // 3) Escavação pelo modelo (túneis, caixas de drenagem) – quando o Revit aceita o elemento.
        if (opt.Excavate && excavators.Count > 0)
        {
            var refused = 0;
            foreach (var topo in topos)
                foreach (var id in excavators)
                {
                    try
                    {
                        if (topo.CanBeExcavatedBy(id)) { topo.ExcavateBy(id); report.Excavated++; }
                        else refused++;
                    }
                    catch (Exception ex)
                    {
                        refused++;
                        Log.Error("Toposolid.ExcavateBy", ex);
                    }
                }
            if (report.Excavated == 0 && refused > 0 && list.Any(d => d is TunnelDefinition))
                report.Notes.Add("O Revit não permitiu escavar o Toposolid com o túnel (forma direta). Os emboques foram terraplenados; " +
                                 "para abrir o túnel no terreno use Massa e terreno → Escavar com um volume.");
        }
        if (opt.Finishes) ApplyFinishes(doc, finishes, report, list.Select(d => d.Id).ToHashSet());
        return report;
    }

    /// <summary>Esquece o terreno original dos Toposolids (o atual vira o terreno natural). Exige transação.</summary>
    public static int ResetOriginal(Document doc)
    {
        var n = 0;
        foreach (var t in TerrainModel.Hosts(doc)) if (TerrainModel.HasOriginal(t)) { TerrainModel.Reset(t); n++; }
        return n;
    }

    /// <summary>
    /// Confere os vértices inseridos: se a cota lida diferir da pedida por um valor constante (&gt; 2 cm), apaga e insere de
    /// novo com a correção; se a diferença não for constante, apenas avisa.
    /// </summary>
    private static void FixOffset(Document doc, SlabShapeEditor editor, List<XYZ> wanted, List<SlabShapeVertex> added, TerrainReport report)
    {
        if (added.Count == 0) return;
        var byXY = new Dictionary<(long, long), double>();
        foreach (var w in wanted) byXY[((long)Math.Round(w.X * 50), (long)Math.Round(w.Y * 50))] = w.Z;
        var deltas = new List<double>();
        foreach (var v in added)
        {
            if (v == null || !v.IsValidObject) continue;
            var pos = v.Position;
            if (byXY.TryGetValue(((long)Math.Round(pos.X * 50), (long)Math.Round(pos.Y * 50)), out var z)) deltas.Add(pos.Z - z);
        }
        if (deltas.Count == 0) return;
        deltas.Sort();
        var median = deltas[deltas.Count / 2];
        var spread = deltas.Max() - deltas.Min();
        if (Math.Abs(median) <= UnitConv.Ft(0.02)) return;
        if (spread > UnitConv.Ft(0.05))
        {
            report.Notes.Add($"Aviso: parte dos pontos do Toposolid ficou até {UnitConv.M(deltas.Max(d => Math.Abs(d))):0.00} m fora da cota de projeto – confira o nível/deslocamento do Toposolid.");
            return;
        }
        try
        {
            foreach (var v in added.Where(v => v != null && v.IsValidObject).ToList())
                try { editor.DeletePoint(v); } catch { /* vértice de canto */ }
            editor.AddPoints(wanted.Select(w => new XYZ(w.X, w.Y, w.Z - median)).ToList());
            doc.Regenerate();
            report.Notes.Add($"Cotas dos pontos corrigidas em {UnitConv.M(-median):+0.00;-0.00} m (o Toposolid lê as cotas relativas ao seu nível/deslocamento).");
        }
        catch (Exception ex)
        {
            Log.Error("Correção das cotas do Toposolid", ex);
            report.Notes.Add($"Aviso: o Toposolid ficou {UnitConv.M(median):0.00} m diferente da cota de projeto e não pôde ser corrigido automaticamente.");
        }
    }

    /// <summary>
    /// Cria um Toposolid plano (Massa e terreno) sob todas as obras, com margem, na cota da base delas – o terreno nativo que
    /// a terraplenagem vai moldar.
    /// </summary>
    public static bool CreateTerrain(Document doc, MarkingService service, IEnumerable<MarkingDefinition> defs, GradingOptions opt, TerrainReport report)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var zs = new List<double>();
        foreach (var d in defs)
        {
            try
            {
                var geo = service.BuildGeometry(d, out var baseZ);
                if (geo.Bounds is not { } b) continue;
                minX = Math.Min(minX, b.Min.X); minY = Math.Min(minY, b.Min.Y);
                maxX = Math.Max(maxX, b.Max.X); maxY = Math.Max(maxY, b.Max.Y);
                zs.Add(baseZ + d.Output.ElevationOffset);
            }
            catch (Exception ex) { Log.Error("Terreno – limites", ex); }
        }
        if (zs.Count == 0) return false;
        var m = Math.Max(10, opt.NewTerrainMargin);
        var z = UnitConv.Ft(zs.Min() - 0.02);
        var corners = new[] { (minX - m, minY - m), (maxX + m, minY - m), (maxX + m, maxY + m), (minX - m, maxY + m) }
            .Select(c => new XYZ(UnitConv.Ft(c.Item1), UnitConv.Ft(c.Item2), z)).ToList();
        var typeId = new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).FirstElementId();
        var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.ProjectElevation - z)).FirstOrDefault();
        if (typeId == ElementId.InvalidElementId || level == null)
        {
            report.Notes.Add("Não foi possível criar o Toposolid: o projeto não tem tipo de Toposolid ou nível.");
            return false;
        }
        try
        {
            var loop = new CurveLoop();
            var flat = corners.Select(c => new XYZ(c.X, c.Y, level.ProjectElevation)).ToList();
            for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(flat[i], flat[(i + 1) % 4]));
            var topo = Toposolid.Create(doc, new List<CurveLoop> { loop }, corners, typeId, level.Id);
            try { topo.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("SV - terreno criado pelo plugin"); } catch { /* opcional */ }
            doc.Regenerate();
            report.Created = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Toposolid.Create", ex);
            report.Notes.Add("Não foi possível criar o Toposolid: " + ex.Message);
            return false;
        }
    }

    /// <summary>Grama de taludes, ilhas e laços como subdivisões nativas do Toposolid, com o material da cor.</summary>
    private static void ApplyFinishes(Document doc, List<(Polygon2 Area, MarkingColor Color, string Id)> finishes, TerrainReport report, HashSet<string> graded)
    {
        var styles = new StyleService(doc);
        var topos = TerrainModel.Hosts(doc);
        // Subdivisões anteriores do plugin saem: das obras regeneradas e das que não moldam mais o terreno (apagadas).
        foreach (var old in new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Cast<Toposolid>()
                     .Where(t => t.HostTopoId != ElementId.InvalidElementId && t.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() is { } c
                                 && c.StartsWith("SV acabamento ", StringComparison.Ordinal)).ToList())
            try { doc.Delete(old.Id); } catch { /* já removida */ }
        _ = graded;
        foreach (var grp in finishes.GroupBy(f => f.Id))
        {
            var tag = $"SV acabamento {grp.Key}";
            foreach (var (area, color, _) in grp)
            {
                var host = topos.FirstOrDefault(t => Contains(t, area.Centroid));
                if (host == null) continue;
                foreach (var part in PolygonOps.Clean(new[] { area }, 0.05))
                {
                    try
                    {
                        var z = host.get_BoundingBox(null)?.Max.Z ?? 0;
                        var loop = new CurveLoop();
                        var ring = part.Outer;
                        for (int i = 0; i < ring.Count; i++)
                        {
                            var a = new XYZ(UnitConv.Ft(ring[i].X), UnitConv.Ft(ring[i].Y), z);
                            var b = new XYZ(UnitConv.Ft(ring[(i + 1) % ring.Count].X), UnitConv.Ft(ring[(i + 1) % ring.Count].Y), z);
                            if (a.DistanceTo(b) > 0.01) loop.Append(Line.CreateBound(a, b));
                        }
                        var sub = host.CreateSubDivision(doc, new List<CurveLoop> { loop });
                        sub.GetParameter(ParameterTypeId.ToposolidSubdivideMaterial)?.Set(styles.Material(color));
                        sub.GetParameter(ParameterTypeId.ToposolidSubdivideHeight)?.Set(UnitConv.Ft(0.02));
                        sub.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(tag);
                        report.Subdivisions++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Toposolid.CreateSubDivision", ex);
                        report.Notes.Add($"Acabamento do terreno não criado ({ex.Message}).");
                    }
                }
            }
        }
    }

    private static bool Contains(Element e, Vec2 p)
    {
        var bb = e.get_BoundingBox(null);
        if (bb == null) return false;
        return UnitConv.M(bb.Min.X) <= p.X && p.X <= UnitConv.M(bb.Max.X) && UnitConv.M(bb.Min.Y) <= p.Y && p.Y <= UnitConv.M(bb.Max.Y);
    }

    private static (Vec2 Min, Vec2 Max)? Bounds(IReadOnlyList<Polygon2> polys)
    {
        if (polys.Count == 0) return null;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var f in polys)
        {
            var (mn, mx) = f.Bounds;
            minX = Math.Min(minX, mn.X); minY = Math.Min(minY, mn.Y);
            maxX = Math.Max(maxX, mx.X); maxY = Math.Max(maxY, mx.Y);
        }
        return (new Vec2(minX, minY), new Vec2(maxX, maxY));
    }

    private static bool Overlaps(Element e, (Vec2 Min, Vec2 Max)? b)
    {
        if (b == null) return false;
        var bb = e.get_BoundingBox(null);
        if (bb == null) return true;
        var (mn, mx) = b.Value;
        return UnitConv.M(bb.Max.X) >= mn.X && UnitConv.M(bb.Min.X) <= mx.X && UnitConv.M(bb.Max.Y) >= mn.Y && UnitConv.M(bb.Min.Y) <= mx.Y;
    }
}
