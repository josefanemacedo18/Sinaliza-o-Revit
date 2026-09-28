using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>
/// Apagar Trecho (só sinalização horizontal): escolhe-se a marca (linha, faixa de pedestres, zebrado, seta, legenda...) e
/// uma janela mostra a planta com as peças reais dela – clique em cada traço/peça para apagar, Shift + arrastar para apagar
/// o que está numa janela. Os trechos viram recortes guardados na própria marca (podem ser devolvidos ou desativados depois);
/// regerar/editar a marca mantém os recortes.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdApagarTrecho : CommandBase
{
    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var stored = Preselected(uidoc) ?? Pick(uidoc);
        if (stored == null) return Result.Cancelled;
        var def = stored.Definition;
        var (geo, fromModel, warnings) = Pieces(uidoc, stored);
        if (geo.Pieces.Count == 0)
        {
            TaskDialog.Show(AppTitle, "Não foi possível ler as peças dessa marca." + (warnings.Count > 0 ? "\n\n" + string.Join("\n", warnings.Distinct()) : ""));
            return Result.Cancelled;
        }
        var manual = def.Exclusions.Where(z => z.Manual).ToList();
        var info = MarkingBuilder.Describe(def, PluginContext.Catalog);
        var existing = manual.Where(z => z.Points.Count >= 3).Select(z => new Polygon2(z.Points)).ToList();
        var active = manual.Count == 0 || manual.Any(z => z.Enabled);
        var w = new TrimWindow($"{info.Code} – {info.Name}", geo, existing, active);
        if (UiHelpers.ShowModal(w) != true) return Result.Cancelled;
        var (results, direct) = Apply(uidoc, stored, fromModel, w.Zones, w.Active);
        if (fromModel)
        {
            TaskDialog.Show(AppTitle, $"Apagar trecho: {direct} peça(s) recortada(s) direto no modelo.\n\n" +
                "Esta marca perdeu a linha de referência, por isso o recorte foi aplicado aos elementos (não é possível devolver as peças " +
                "depois – use Desfazer, Ctrl+Z, se precisar). Para voltar a editar pelos parâmetros, recrie a marca sobre uma linha.");
            return Result.Succeeded;
        }
        ReportResults("Apagar trecho", results);
        return Result.Succeeded;
    }

    /// <summary>
    /// Peças da marca SEM os recortes manuais (para poder devolver peças apagadas antes). Sem peças calculadas (caminho não
    /// resolvido, marca antiga...), as peças são lidas dos elementos do modelo – cada traço/seta é um sólido próprio na
    /// forma direta (ou uma região preenchida no 2D).
    /// </summary>
    internal static (MarkingGeometry Geo, bool FromModel, List<string> Warnings) Pieces(UIDocument uidoc, StoredMarking stored)
    {
        var doc = uidoc.Document;
        var def = stored.Definition;
        var service = new MarkingService(doc, uidoc.ActiveView);
        var manual = def.Exclusions.Where(z => z.Manual).ToList();
        def.Exclusions.RemoveAll(z => z.Manual);
        MarkingGeometry geo;
        var warnings = new List<string>();
        try { geo = service.BuildGeometry(def, out _, warnings); }
        catch (Exception ex)
        {
            Log.Error("Apagar trecho – geometria", ex);
            warnings.Add(ex.Message);
            geo = new MarkingGeometry();
        }
        finally { def.Exclusions.AddRange(manual); }
        if (geo.Pieces.Count > 0) return (geo, false, warnings);
        Log.Info($"Apagar trecho: geometria vazia para {def.DisplayCode} ({string.Join("; ", warnings)}) – lendo os elementos do modelo.");
        return (ModelPieces(doc, stored.MarkingId), true, warnings);
    }

    /// <summary>
    /// Grava as áreas apagadas na marca (recortes manuais) e a regenera; marca sem caminho (linha de referência apagada) não
    /// pode ser regenerada – o recorte é feito direto nos elementos (Direct = peças alteradas).
    /// </summary>
    internal static (List<RenderResult> Results, int Direct) Apply(UIDocument uidoc, StoredMarking stored, bool fromModel, IReadOnlyList<Polygon2> zones, bool active)
    {
        var def = stored.Definition;
        def.Exclusions.RemoveAll(z => z.Manual);
        foreach (var z in zones) def.Exclusions.Add(new ExclusionZone { Manual = true, Enabled = active, Points = z.Outer.ToList() });
        if (fromModel) return (new List<RenderResult>(), TrimElements(uidoc.Document, stored.MarkingId, def, active ? zones : Array.Empty<Polygon2>()));
        return (MarkingCreator.Commit(uidoc, new[] { def }, "SV - Apagar trecho"), 0);
    }

    /// <summary>
    /// Recorta os elementos da marca pelas áreas: sólidos da forma direta (diferença com um prisma alto de cada área),
    /// regiões preenchidas (contorno menos as áreas) e pisos inteiramente dentro delas (apagados). Retorna as peças alteradas.
    /// </summary>
    private static int TrimElements(Document doc, string markingId, MarkingDefinition def, IReadOnlyList<Polygon2> zones)
    {
        var count = 0;
        using var t = new Transaction(doc, "SV - Apagar trecho (direto no modelo)");
        t.Start();
        foreach (var r in MarkingStorage.ById(doc, markingId))
        {
            try
            {
                switch (r.Element)
                {
                    case DirectShape ds:
                    {
                        var ge = ds.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });
                        if (ge == null) break;
                        var keep = new List<GeometryObject>();
                        var changed = false;
                        foreach (var solid in Solids(ge))
                        {
                            var bb = solid.GetBoundingBox();
                            var z0 = bb.Transform.OfPoint(bb.Min).Z - 1;
                            var z1 = bb.Transform.OfPoint(bb.Max).Z + 1;
                            var foot = Footprint(solid);
                            var cur = solid;
                            foreach (var z in zones)
                            {
                                if (foot == null || !Overlaps(foot, z)) continue;
                                var prism = Prism(z, z0, z1);
                                if (prism == null) continue;
                                var diff = BooleanOperationsUtils.ExecuteBooleanOperation(cur, prism, BooleanOperationsType.Difference);
                                cur = diff;
                                changed = true;
                                if (cur == null || cur.Volume < 1e-9) break;
                            }
                            if (cur != null && cur.Volume > 1e-9) keep.Add(cur);
                        }
                        if (!changed) break;
                        count++;
                        if (keep.Count == 0) doc.Delete(ds.Id);
                        else ds.SetShape(keep);
                        break;
                    }
                    case FilledRegion fr:
                    {
                        var polys = fr.GetBoundaries().Select(Ring).Where(x => x is { Count: >= 3 }).Select(x => new Polygon2(x!)).ToList();
                        if (!polys.Any(p => zones.Any(z => Overlaps(p, z)))) break;
                        var rest = PolygonOps.Difference(polys, zones).Where(p => p.Area > 1e-4).ToList();
                        count++;
                        var view = fr.OwnerViewId;
                        var type = fr.GetTypeId();
                        foreach (var p in rest)
                        {
                            var loops = new List<CurveLoop> { Loop(p.Outer) };
                            loops.AddRange(p.Holes.Select(Loop));
                            var nfr = FilledRegion.Create(doc, type, view, loops);
                            MarkingStorage.Write(nfr, def, r.Color);
                        }
                        doc.Delete(fr.Id);
                        break;
                    }
                    case Floor fl:
                    {
                        var ge = fl.get_Geometry(new Options());
                        var foot = ge == null ? null : Solids(ge).Select(Footprint).FirstOrDefault(f => f != null);
                        if (foot == null) break;
                        var rest = PolygonOps.Difference(new[] { foot }, zones).Sum(p => p.Area);
                        if (rest < 0.02 * foot.Area) { doc.Delete(fl.Id); count++; }
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Error("Apagar trecho – recorte direto", ex); }
        }
        // Os recortes ficam registrados na definição gravada nos elementos que sobraram.
        foreach (var r in MarkingStorage.ById(doc, markingId)) MarkingStorage.Write(r.Element, def, r.Color);
        t.Commit();
        return count;
    }

    private static bool Overlaps(Polygon2 a, Polygon2 b)
    {
        try { return PolygonOps.Intersect(new[] { a }, new[] { b }).Any(x => x.Area > 1e-6); }
        catch { return false; }
    }

    private static CurveLoop Loop(IReadOnlyList<Vec2> ring)
    {
        var loop = new CurveLoop();
        for (int i = 0; i < ring.Count; i++)
        {
            var a = new XYZ(UnitConv.Ft(ring[i].X), UnitConv.Ft(ring[i].Y), 0);
            var b = new XYZ(UnitConv.Ft(ring[(i + 1) % ring.Count].X), UnitConv.Ft(ring[(i + 1) % ring.Count].Y), 0);
            if (a.DistanceTo(b) > 1e-3) loop.Append(Line.CreateBound(a, b));
        }
        return loop;
    }

    /// <summary>Prisma vertical da área entre as cotas (pés) – para a diferença booleana.</summary>
    private static Solid? Prism(Polygon2 zone, double z0, double z1)
    {
        try
        {
            var ring = zone.Outer;
            var loop = new CurveLoop();
            for (int i = 0; i < ring.Count; i++)
            {
                var a = new XYZ(UnitConv.Ft(ring[i].X), UnitConv.Ft(ring[i].Y), z0);
                var b = new XYZ(UnitConv.Ft(ring[(i + 1) % ring.Count].X), UnitConv.Ft(ring[(i + 1) % ring.Count].Y), z0);
                if (a.DistanceTo(b) > 1e-3) loop.Append(Line.CreateBound(a, b));
            }
            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, z1 - z0);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Peças da marca lidas dos elementos do Revit: pegada de cada sólido (faces para cima) e regiões preenchidas.</summary>
    internal static MarkingGeometry ModelPieces(Document doc, string markingId)
    {
        var geo = new MarkingGeometry();
        foreach (var r in MarkingStorage.ById(doc, markingId))
        {
            try
            {
                if (r.Element is FilledRegion fr)
                {
                    foreach (var loop in fr.GetBoundaries())
                        if (Ring(loop) is { Count: >= 3 } ring) geo.Add(new Polygon2(ring), r.Color);
                    continue;
                }
                var ge = r.Element.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });
                if (ge == null) continue;
                foreach (var solid in Solids(ge))
                    if (Footprint(solid) is { } f && f.Area > 1e-4) geo.Add(f, r.Color);
            }
            catch (Exception ex) { Log.Error("Apagar trecho – peças do modelo", ex); }
        }
        return geo;
    }

    private static List<Vec2>? Ring(CurveLoop loop)
    {
        var pts = new List<Vec2>();
        foreach (var c in loop)
        {
            var t = c.Tessellate();
            for (int i = 0; i + 1 < t.Count; i++) pts.Add(UnitConv.ToVec2(t[i]));
        }
        return pts;
    }

    private static IEnumerable<Solid> Solids(GeometryElement ge)
    {
        foreach (var o in ge)
        {
            if (o is Solid s && s.Faces.Size > 0 && s.Volume > 1e-9) yield return s;
            else if (o is GeometryInstance gi)
                foreach (var x in Solids(gi.GetInstanceGeometry())) yield return x;
        }
    }

    /// <summary>Pegada em planta (m) de um sólido: união das faces voltadas para cima.</summary>
    private static Polygon2? Footprint(Solid s)
    {
        var polys = new List<Polygon2>();
        foreach (Face f in s.Faces)
        {
            try
            {
                var bb = f.GetBoundingBox();
                if (f.ComputeNormal((bb.Min + bb.Max) / 2).Z < 0.3) continue;
                var loop = f.GetEdgesAsCurveLoops().FirstOrDefault();
                if (loop == null || Ring(loop) is not { Count: >= 3 } ring) continue;
                polys.Add(new Polygon2(ring));
            }
            catch { /* face sem contorno */ }
        }
        if (polys.Count == 0) return null;
        try { return PolygonOps.Union(polys).OrderByDescending(x => x.Area).FirstOrDefault(); }
        catch { return polys.OrderByDescending(x => x.Area).First(); }
    }

    /// <summary>Sinalização horizontal (pinturas, legendas, zebrados, faixas) – o que a ferramenta aceita.</summary>
    internal static bool IsHorizontal(MarkingDefinition def)
    {
        if (def is IAnnotationDefinition) return false;
        try
        {
            var info = MarkingBuilder.Describe(def, PluginContext.Catalog);
            return QuantityRow.Categorize(def, info.Group) == CategoriaQuantitativo.SinalizacaoHorizontal;
        }
        catch
        {
            return false;
        }
    }

    private static StoredMarking? Preselected(UIDocument uidoc)
    {
        var ids = uidoc.Selection.GetElementIds();
        if (ids.Count != 1) return null;
        var st = MarkingStorage.Read(uidoc.Document.GetElement(ids.First()));
        return st != null && IsHorizontal(st.Definition) ? st : null;
    }

    private static StoredMarking? Pick(UIDocument uidoc)
    {
        try
        {
            var r = uidoc.Selection.PickObject(ObjectType.Element, new HorizontalFilter(),
                "Apagar trecho: clique a sinalização horizontal (linha, faixa de pedestres, zebrado, seta, legenda...)");
            return MarkingStorage.Read(uidoc.Document.GetElement(r));
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }

    private sealed class HorizontalFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) =>
            new MarkingSelectionFilter().AllowElement(elem) && MarkingStorage.Read(elem) is { } st && IsHorizontal(st.Definition);
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
