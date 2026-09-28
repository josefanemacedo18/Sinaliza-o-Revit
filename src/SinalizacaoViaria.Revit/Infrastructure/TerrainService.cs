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
    public int Skipped { get; set; }
    public int Checked { get; set; }
    public double MaxDeviation { get; set; }
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
        if (Checked > 0) s += $"\nConferência no terreno real: {Checked} pontos, desvio máximo {MaxDeviation.ToString("0.00", pt)} m";
        if (Skipped > 0) s += $"\nPontos recusados pelo Revit: {Skipped}";
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
                var works = (all ?? Array.Empty<MarkingDefinition>()).Where(h => road.GroupId != null && h is IHostedStructure hs && hs.HostRoad == road.GroupId);
                // Fora dos trechos em obra; junto a encontros e emboques a via termina numa face vertical (emboque: testa + talude).
                corridors.AddRange(InfraRoads.RoadCorridors(axis, prof, right, left, s0, s1, opt.Subgrade, cut, fill, works).Select(c => c.WithOffset(z0)));
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

        // Superfície suavizada do Toposolid arredonda as faces de corte/aterro e os degraus dos muros: desligada para a terraplenagem.
        try
        {
            if (Toposolid.IsSmoothedSurfaceEnabled(doc))
            {
                Toposolid.SetSmoothedSurface(doc, false);
                report.Notes.Add("A \"superfície suavizada\" dos Toposolids foi desligada (ela arredonda os taludes e as faces dos muros).");
            }
        }
        catch (Exception ex) { Log.Error("Superfície suavizada", ex); }

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
                {
                    var els = MarkingStorage.ById(doc, def.Id).Select(r => r.Element).ToList();
                    // Túnel: a Massa de escavação (envoltória do revestimento) abre o furo inteiro no terreno.
                    var bores = els.Where(MarkingService.IsBore).ToList();
                    excavators.AddRange((bores.Count > 0 ? bores : els.Where(e => e is DirectShape)).Select(e => e.Id));
                }
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
                var pts = TerrainRebuild.Points(orig, design, inRegion);
                ApplyPoints(doc, topo, editor, inRegion, pts, report);
                report.Toposolids++;
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
            foreach (var id in excavators)
            {
                if (doc.GetElement(id) is not { } el) continue;
                foreach (var topo in topos.Where(tp => Overlaps(tp, ElementBounds(el))))
                {
                    try
                    {
                        if (Excavates(topo, id)) { report.Excavated++; continue; }
                        if (topo.CanBeExcavatedBy(id)) { topo.ExcavateBy(id); report.Excavated++; continue; }
                        // Massa recusada: cópia como Modelo genérico (oculta nas vistas) só para escavar.
                        if (MarkingService.IsBore(el) && GenericCopy(doc, el) is { } copy && topo.CanBeExcavatedBy(copy))
                        {
                            topo.ExcavateBy(copy);
                            report.Excavated++;
                            continue;
                        }
                        refused++;
                    }
                    catch (Exception ex)
                    {
                        refused++;
                        Log.Error("Toposolid.ExcavateBy", ex);
                    }
                }
            }
            if (report.Excavated == 0 && refused > 0 && list.Any(d => d is TunnelDefinition))
                report.Notes.Add("O Revit não permitiu escavar o Toposolid com o volume do túnel. Os emboques foram cortados; para abrir o furo " +
                                 "selecione o Toposolid → Escavar e clique a Massa \"SV … escavação do terreno\" (ative Mostrar massa).");
        }
        if (opt.Finishes) ApplyFinishes(doc, finishes, report, list.Select(d => d.Id).ToHashSet());
        return report;
    }

    private static (Vec2 Min, Vec2 Max)? ElementBounds(Element e)
    {
        var bb = e.get_BoundingBox(null);
        return bb == null ? null : (new Vec2(UnitConv.M(bb.Min.X), UnitConv.M(bb.Min.Y)), new Vec2(UnitConv.M(bb.Max.X), UnitConv.M(bb.Max.Y)));
    }

    /// <summary>O elemento já escava o Toposolid?</summary>
    private static bool Excavates(Toposolid topo, ElementId id)
    {
        try
        {
            foreach (var d in topo.GetIntersectingElementData())
                if (d.IntersectingElementId == id && d.IntersectionType == IntersectionType.Excavate) return true;
        }
        catch { /* versão sem os dados de interseção */ }
        return false;
    }

    /// <summary>Cópia do volume de escavação como Modelo genérico, oculta em todas as vistas (só escava o terreno).</summary>
    private static ElementId? GenericCopy(Document doc, Element bore)
    {
        try
        {
            var tag = $"SV escavação (cópia) {bore.UniqueId}";
            var old = new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == tag).ToList();
            foreach (var o in old) try { doc.Delete(o.Id); } catch { /* ignorado */ }
            var geom = new List<GeometryObject>();
            if (bore.get_Geometry(new Options()) is { } ge) foreach (var o in ge) if (o is Solid s && s.Volume > 1e-6) geom.Add(s);
            if (geom.Count == 0) return null;
            var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = "SinalizacaoViaria";
            ds.ApplicationDataId = bore.UniqueId;
            ds.SetShape(geom);
            ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(tag);
            try { ds.SetName("SV escavação do terreno"); } catch { /* opcional */ }
            foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate))
                try { if (ds.CanBeHidden(v)) v.HideElements(new List<ElementId> { ds.Id }); } catch { /* vista sem suporte */ }
            doc.Regenerate();
            return ds.Id;
        }
        catch (Exception ex)
        {
            Log.Error("Cópia do volume de escavação", ex);
            return null;
        }
    }

    /// <summary>Esquece o terreno original dos Toposolids (o atual vira o terreno natural). Exige transação.</summary>
    public static int ResetOriginal(Document doc)
    {
        var n = 0;
        foreach (var t in TerrainModel.Hosts(doc)) if (TerrainModel.HasOriginal(t)) { TerrainModel.Reset(t); n++; }
        return n;
    }

    /// <summary>
    /// Troca os pontos do Toposolid na região: apaga os vértices de dentro, insere os pontos novos (em lotes) e CONFERE o
    /// resultado pela geometria real do Toposolid (cota absoluta do topo nos pontos inseridos). Se o Revit tiver lido as cotas
    /// com um deslocamento constante (nível/deslocamento do Toposolid), os pontos são refeitos com a correção.
    /// </summary>
    private static void ApplyPoints(Document doc, Toposolid topo, SlabShapeEditor editor, Func<Vec2, bool> inRegion, List<Vec3> pts, TerrainReport report)
    {
        static (long, long) Key(double xFt, double yFt) => ((long)Math.Round(xFt * 20), (long)Math.Round(yFt * 20));
        // 1) Sai tudo o que está na região (pontos de terraplenagens passadas e do levantamento).
        // Posições lidas antes de apagar (os objetos de vértice podem deixar de valer depois de cada exclusão).
        var vertices = new List<(SlabShapeVertex V, XYZ P)>();
        foreach (SlabShapeVertex v in editor.SlabShapeVertices) vertices.Add((v, v.Position));
        var kept = new HashSet<(long, long)>();
        foreach (var (v, pos) in vertices)
        {
            var xy = new Vec2(UnitConv.M(pos.X), UnitConv.M(pos.Y));
            if (!inRegion(xy)) { kept.Add(Key(pos.X, pos.Y)); continue; }
            var ok = false;
            try { ok = v.IsValidObject && editor.DeletePoint(v); } catch { /* vértice do contorno */ }
            if (ok) report.Removed++;
            else kept.Add(Key(pos.X, pos.Y));
        }
        // 2) Entram os pontos novos – distintos em planta e fora dos vértices que ficaram (o Revit recusa repetidos).
        var add = new List<XYZ>();
        foreach (var p in pts)
        {
            var q = new XYZ(UnitConv.Ft(p.X), UnitConv.Ft(p.Y), UnitConv.Ft(p.Z));
            if (kept.Add(Key(q.X, q.Y))) add.Add(q);
        }
        if (add.Count == 0) return;
        // O topo não pode descer abaixo do fundo do Toposolid ("muito fino para seu tipo"): engrossa o tipo antes, se preciso.
        EnsureThickness(doc, topo, add.Min(q => q.Z), report);
        AddPoints(editor, add, report, true);
        doc.Regenerate();

        // 3) Conferência pela geometria (cota absoluta real do topo do Toposolid nos pontos inseridos).
        var (median, spread, n) = Check(topo, add, 0);
        if (n == 0) { report.Notes.Add("Aviso: não foi possível conferir o Toposolid depois da terraplenagem (geometria não lida)."); return; }
        if (Math.Abs(median) > 0.02 && spread < 0.10)
        {
            // Deslocamento constante: refaz os pontos com a correção.
            var wanted = add.Select(q => Key(q.X, q.Y)).ToHashSet();
            var redo = new List<SlabShapeVertex>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                if (wanted.Contains(Key(v.Position.X, v.Position.Y))) redo.Add(v);
            foreach (var v in redo) try { if (v.IsValidObject) editor.DeletePoint(v); } catch { /* ignorado */ }
            var fixedPts = add.Select(q => new XYZ(q.X, q.Y, q.Z - UnitConv.Ft(median))).ToList();
            AddPoints(editor, fixedPts, report, false);
            doc.Regenerate();
            var (m2, s2, n2) = Check(topo, add, 0);
            report.Notes.Add($"Cotas dos pontos do Toposolid corrigidas em {-median:+0.00;-0.00} m (o Revit as lia relativas ao nível/deslocamento do Toposolid)." +
                             (n2 > 0 ? $" Conferência depois da correção: desvio médio {m2 * 100:0} cm." : ""));
            median = m2; spread = s2; n = n2;
        }
        report.Checked += n;
        report.MaxDeviation = Math.Max(report.MaxDeviation, Math.Abs(median) + spread);
        if (Math.Abs(median) > 0.05 || spread > 0.25)
            report.Notes.Add($"Aviso: o topo do Toposolid ficou até {Math.Abs(median) + spread:0.00} m fora da superfície de projeto em parte dos pontos – " +
                             "confira se o Toposolid tem \"superfície suavizada\" ativa ou pontos de outra fonte na área.");
    }

    /// <summary>
    /// Garante que o fundo do Toposolid fique pelo menos 1,5 m abaixo do ponto mais baixo do projeto: senão o Revit recusa a
    /// edição ("o sólido topográfico é muito fino"). O tipo é duplicado ("… SV") com a camada mais grossa aumentada.
    /// </summary>
    public static void EnsureThickness(Document doc, Toposolid topo, double minZFt, TerrainReport report)
    {
        try
        {
            var bb = topo.get_BoundingBox(null);
            if (bb == null) return;
            var margin = UnitConv.Ft(1.5);
            var lack = bb.Min.Z - (minZFt - margin);
            if (lack <= 0) return;
            if (doc.GetElement(topo.GetTypeId()) is not ToposolidType type) return;
            var cs = type.GetCompoundStructure();
            if (cs == null || cs.LayerCount == 0) return;
            // Folga extra (redondo em metros) para as próximas obras não exigirem outro tipo.
            var addM = Math.Ceiling(UnitConv.M(lack) + 2);
            var baseName = type.Name.Contains(" SV +") ? type.Name[..type.Name.IndexOf(" SV +", StringComparison.Ordinal)] : type.Name;
            var total = UnitConv.M(cs.GetWidth()) + addM;
            var name = $"{baseName} SV +{total:0} m";
            var target = new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).Cast<ToposolidType>().FirstOrDefault(x => x.Name == name);
            if (target == null)
            {
                target = (ToposolidType)type.Duplicate(name);
                var ncs = target.GetCompoundStructure();
                var idx = Enumerable.Range(0, ncs.LayerCount).OrderByDescending(i => ncs.GetLayerWidth(i)).First();
                ncs.SetLayerWidth(idx, ncs.GetLayerWidth(idx) + UnitConv.Ft(addM));
                target.SetCompoundStructure(ncs);
            }
            topo.ChangeTypeId(target.Id);
            doc.Regenerate();
            report.Notes.Add($"Toposolid engrossado para {total:0} m (tipo \"{name}\"): o corte chega a {UnitConv.M(minZFt):0.0} m e o fundo estava acima disso.");
        }
        catch (Exception ex)
        {
            Log.Error("Espessura do Toposolid", ex);
            report.Notes.Add("Aviso: não foi possível engrossar o Toposolid para o corte – aumente a espessura do tipo (Editar tipo → Estrutura) se o Revit recusar.");
        }
    }

    private static void AddPoints(SlabShapeEditor editor, List<XYZ> add, TerrainReport report, bool count)
    {
        const int batch = 400;
        for (int i = 0; i < add.Count; i += batch)
        {
            var part = add.Skip(i).Take(batch).ToList();
            try { editor.AddPoints(part); if (count) report.Added += part.Count; }
            catch (Exception ex)
            {
                Log.Error("Toposolid.AddPoints (lote)", ex);
                foreach (var q in part)
                    try { editor.AddPoint(q); if (count) report.Added++; } catch { if (count) report.Skipped++; }
            }
        }
    }

    /// <summary>Desvio (mediana e dispersão p90−p10, m) entre o topo real do Toposolid e as cotas pedidas, numa amostra dos pontos.</summary>
    private static (double Median, double Spread, int N) Check(Toposolid topo, List<XYZ> wanted, double offsetM)
    {
        var live = TerrainModel.Live(topo);
        if (live.Count == 0) return (0, 0, 0);
        var step = Math.Max(1, wanted.Count / 300);
        var d = new List<double>();
        for (int i = 0; i < wanted.Count; i += step)
        {
            var w = wanted[i];
            if (live.Z(new Vec2(UnitConv.M(w.X), UnitConv.M(w.Y))) is { } z) d.Add(z - UnitConv.M(w.Z) - offsetM);
        }
        if (d.Count == 0) return (0, 0, 0);
        d.Sort();
        return (d[d.Count / 2], d[(int)(d.Count * 0.9)] - d[(int)(d.Count * 0.1)], d.Count);
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
