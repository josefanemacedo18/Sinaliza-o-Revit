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
}

public sealed class TerrainReport
{
    public int Toposolids { get; set; }
    public int Removed { get; set; }
    public int Added { get; set; }
    public int Excavated { get; set; }
    public double Cut { get; set; }
    public double Fill { get; set; }
    public double Area { get; set; }
    public List<string> Notes { get; } = new();

    public string Text()
    {
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var s = $"Toposolid(s) ajustado(s): {Toposolids}\nPontos removidos / inseridos: {Removed} / {Added}\n" +
                $"Área terraplenada: {Area.ToString("N0", pt)} m²\nCorte: {Cut.ToString("N1", pt)} m³ · Aterro: {Fill.ToString("N1", pt)} m³";
        if (Excavated > 0) s += $"\nEscavações pelo modelo (túneis, caixas): {Excavated}";
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
    public static (List<GradeCorridor> Corridors, List<GradePad> Pads) Features(Document doc, MarkingDefinition def, MarkingGeometry geo, double baseZ, GradingOptions opt)
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
                // Via: plataforma do bordo externo de uma calçada ao da outra, sob a estrutura do pavimento.
                var prof = VerticalProfile.Flat(z0 - opt.Subgrade);
                var right = -(road.TotalRight + 0.3);
                var left = road.TotalLeft + 0.3;
                var s0 = Math.Max(0, road.StartSetback);
                var s1 = Math.Max(s0 + 0.5, axis.Length - Math.Max(0, road.EndSetback));
                corridors.Add(Core.Generators.Infra.Corridor(axis, prof, right, left, s0, s1, 0, opt.CutSlope, opt.FillSlope, label: "Via"));
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

    /// <summary>Marcas que participam da terraplenagem.</summary>
    public static bool Grades(MarkingDefinition d) =>
        d is ITerrainAware or RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition or DrainageDefinition;

    /// <summary>Ajusta os Toposolids às marcas indicadas. Deve ser chamado dentro de uma transação.</summary>
    public static TerrainReport Apply(Document doc, MarkingService service, IEnumerable<MarkingDefinition> defs, GradingOptions opt)
    {
        var report = new TerrainReport();
        var corridors = new List<GradeCorridor>();
        var pads = new List<GradePad>();
        var excavators = new List<ElementId>();
        foreach (var def in defs.Where(Grades))
        {
            try
            {
                var geo = service.BuildGeometry(def, out var baseZ);
                var (c, p) = Features(doc, def, geo, baseZ, opt);
                corridors.AddRange(c);
                pads.AddRange(p);
                if (opt.Excavate && def is TunnelDefinition or DrainageDefinition)
                    excavators.AddRange(MarkingStorage.ById(doc, def.Id).Select(r => r.Element.Id));
            }
            catch (Exception ex)
            {
                Log.Error($"Terraplenagem {def.DisplayCode}", ex);
                report.Notes.Add($"{def.DisplayCode}: {ex.Message}");
            }
        }
        var topos = new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Cast<Toposolid>().ToList();
        if (topos.Count == 0)
        {
            report.Notes.Add("Nenhum Toposolid no projeto (Massa e terreno → Toposolid). Crie o terreno e rode de novo.");
            if (new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Topography).WhereElementIsNotElementType().Any())
                report.Notes.Add("Há uma superfície topográfica antiga: converta-a em Toposolid (Massa e terreno → Toposolid → Criar a partir da topografia).");
            return report;
        }
        if (corridors.Count == 0 && pads.Count == 0 && excavators.Count == 0)
        {
            report.Notes.Add("Nada a terraplenar nas marcas selecionadas.");
            return report;
        }

        // Terreno natural ANTES das alterações (rastreado no Toposolid).
        var sampler = new SurfaceSampler(doc, Array.Empty<string>(), allowCreateView: true, terrainOnly: true);
        if (!sampler.IsAvailable)
        {
            report.Notes.Add("Nenhuma vista 3D disponível para ler o terreno.");
            return report;
        }
        var hintZ = corridors.SelectMany(c => c.Left).Select(p => p.Z).Concat(pads.Select(p => p.Z)).DefaultIfEmpty(0).Average();
        double? Ground(Vec2 p) => sampler.TrySample(UnitConv.Ft(p.X), UnitConv.Ft(p.Y), UnitConv.Ft(hintZ), out var z, out _) ? UnitConv.M(z) : null;

        var design = Grading.Design(corridors, pads, Ground, 0, Math.Max(5, opt.MaxDaylight), 0.5, 4.0, 1.0);
        report.Cut = design.CutM3;
        report.Fill = design.FillM3;
        report.Area = design.AreaM2;
        report.Notes.AddRange(design.Warnings);
        var fp = design.Footprint;
        var bounds = Bounds(fp);

        foreach (var topo in topos)
        {
            if (!Overlaps(topo, bounds)) continue;
            try
            {
                var editor = topo.GetSlabShapeEditor();
                if (editor == null) continue;
                if (!editor.IsEnabled) editor.Enable();
                // 1) Pontos do terreno dentro da área de projeto saem (serão substituídos pela superfície de projeto).
                var removedXY = new List<Vec2>();
                var keep = new List<Vec2>();
                var vertices = new List<SlabShapeVertex>();
                foreach (SlabShapeVertex v in editor.SlabShapeVertices) vertices.Add(v);
                foreach (var v in vertices)
                {
                    var xy = new Vec2(UnitConv.M(v.Position.X), UnitConv.M(v.Position.Y));
                    if (design.DesignZ(xy) != null)
                    {
                        bool deleted;
                        try { deleted = editor.DeletePoint(v); } catch { deleted = false; }
                        if (deleted) { removedXY.Add(xy); report.Removed++; continue; }
                    }
                    keep.Add(xy);
                }
                // 2) Superfície de projeto: bordas, pés/cristas de talude, pontos das plataformas e os pontos removidos já na cota nova.
                var pts = design.Points.ToList();
                foreach (var xy in removedXY) if (design.DesignZ(xy) is { } z) pts.Add(Vec3.At(xy, z));
                var grid = new HashSet<(long, long)>(keep.Select(k => ((long)Math.Round(k.X * 20), (long)Math.Round(k.Y * 20))));
                var add = new List<XYZ>();
                foreach (var p in pts)
                {
                    if (double.IsNaN(p.Z) || Ground(p.XY) == null) continue;        // fora do Toposolid
                    if (!grid.Add(((long)Math.Round(p.X * 20), (long)Math.Round(p.Y * 20)))) continue;   // distintos em planta (5 cm)
                    add.Add(new XYZ(UnitConv.Ft(p.X), UnitConv.Ft(p.Y), UnitConv.Ft(p.Z)));
                }
                if (add.Count > 0)
                {
                    try
                    {
                        editor.AddPoints(add);
                        report.Added += add.Count;
                    }
                    catch (Exception ex)
                    {
                        // Um ponto inválido derruba o lote: tenta um a um.
                        Log.Error("Toposolid.AddPoints", ex);
                        foreach (var q in add)
                            try { editor.AddPoint(q); report.Added++; } catch { /* ponto na borda ou repetido */ }
                    }
                }
                doc.Regenerate();
                report.Toposolids++;
                // Conferência: a cota inserida é a cota lida no terreno?
                var check = add.FirstOrDefault();
                if (check != null)
                {
                    var fresh = new SurfaceSampler(doc, Array.Empty<string>(), allowCreateView: false, terrainOnly: true);
                    if (fresh.IsAvailable && fresh.TrySample(check.X, check.Y, check.Z, out var zz, out _) && Math.Abs(zz - check.Z) > UnitConv.Ft(0.05))
                        report.Notes.Add($"Aviso: o Toposolid ficou {UnitConv.M(zz - check.Z):0.00} m diferente da cota de projeto num ponto de conferência – confira o nível/deslocamento do Toposolid.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Terraplenagem do Toposolid", ex);
                report.Notes.Add($"Toposolid {topo.Id}: {ex.Message}");
            }
        }

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
            if (report.Excavated == 0 && refused > 0)
                report.Notes.Add("O Revit não permitiu escavar o Toposolid com estes elementos (formas diretas). Os emboques e acessos foram terraplenados; " +
                                 "para abrir o túnel no terreno use Massa e terreno → Escavar com um piso/volume ou uma vista em corte.");
        }
        return report;
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
