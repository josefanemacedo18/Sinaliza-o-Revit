using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;
using SinalizacaoViaria.Core.Parcelamento;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Loteamento no Revit: o loteamento fica num elemento de dados (DataStorage); cada lote é uma subdivisão do Toposolid
/// (acompanha o relevo em planta e no 3D, com o material da categoria) ou, sem Toposolid, um piso plano no nível. Materiais,
/// hachuras e filtros por categoria, masterplan, tabela nativa e leitura das vias para o reconhecimento das quadras.
/// </summary>
public static class LotService
{
    /// <summary>Início do comentário dos elementos de lote ("SV lote H 12 [T1:3]").</summary>
    public const string Tag = "SV lote";
    private const double FloorThickness = 0.05;

    // ------------------------------------------------------------------ dados

    /// <summary>Nome aceito pelo Revit (sem \ : { } [ ] | ; &lt; &gt; ? ` ~).</summary>
    public static string Safe(string name)
    {
        var bad = "\\:{}[]|;<>?`~";
        return new string(name.Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim();
    }

    public static List<LoteamentoDefinition> Loteamentos(Document doc) => MarkingStorage.Definitions(doc).OfType<LoteamentoDefinition>().ToList();

    public static LoteamentoDefinition? Loteamento(Document doc, string? id) =>
        Loteamentos(doc).FirstOrDefault(l => l.Id == id);

    public static List<QuadraDefinition> Quadras(Document doc, string lotId) =>
        MarkingStorage.Definitions(doc).OfType<QuadraDefinition>().Where(q => q.LoteamentoId == lotId).OrderBy(q => q.Nome, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Chave do lote gravada no comentário do elemento ("[T1:3]"); nula se o elemento não for um lote.</summary>
    public static string? KeyOf(Element e)
    {
        var c = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
        if (c == null || !c.StartsWith(Tag, StringComparison.Ordinal)) return null;
        var a = c.LastIndexOf('[');
        var b = c.LastIndexOf(']');
        return a >= 0 && b > a ? c.Substring(a + 1, b - a - 1) : null;
    }

    // ------------------------------------------------------------------ geração

    /// <summary>Gera o loteamento (dados) ou a quadra (lotes). Chamado pelo MarkingService dentro da transação.</summary>
    public static RenderResult Render(Document doc, MarkingDefinition def, List<StoredMarking> existing)
    {
        return def switch
        {
            LoteamentoDefinition lot => SaveLoteamento(doc, lot, existing),
            QuadraDefinition q => RenderQuadra(doc, q, existing),
            _ => new RenderResult(),
        };
    }

    private static RenderResult SaveLoteamento(Document doc, LoteamentoDefinition lot, List<StoredMarking> existing)
    {
        var res = new RenderResult();
        var ds = existing.Select(r => r.Element).OfType<DataStorage>().FirstOrDefault() ?? DataStorage.Create(doc);
        MarkingStorage.Write(ds, lot, MarkingColor.Terra);
        foreach (var r in existing.Where(r => r.Element.Id != ds.Id)) SafeDelete(doc, r.Element.Id);
        res.Elements.Add(ds.Id);
        return res;
    }

    private static RenderResult RenderQuadra(Document doc, QuadraDefinition q, List<StoredMarking> existing)
    {
        var res = new RenderResult();
        var lot = Loteamento(doc, q.LoteamentoId);
        var cats = lot?.Categorias ?? CategoriaLote.Padrao();
        var div = LotDivider.Divide(q, lot);
        res.Warnings.AddRange(div.Avisos);
        foreach (var r in existing) SafeDelete(doc, r.Element.Id);
        SharedParameters.Ensure(doc);
        SharedParameters.EnsureLotes(doc);
        var styles = new StyleService(doc);
        var hosts = TerrainModel.Hosts(doc);
        var hostFoot = hosts.ToDictionary(h => h.Id, h => Footprint(doc, h));
        var height = Math.Max(0.005, lot?.AlturaLote ?? 0.03);
        var floors = 0;
        string? floorReason = null;
        foreach (var p in div.Parcelas)
        {
            var cat = lot?.Categoria(p.Categoria) ?? cats.FirstOrDefault(c => c.Nome == p.Categoria) ?? cats[0];
            var material = LotMaterial(doc, styles, cat);
            Element? e = null;
            var host = hosts.FirstOrDefault(h => hostFoot[h.Id] is { } f ? f.Contains(p.Forma.Centroid) : Contains(h, p.Forma.Centroid));
            if (host != null)
            {
                try
                {
                    var z = host.get_BoundingBox(null)?.Max.Z ?? 0;
                    var loops = Loops(doc, p.Forma, z);
                    if (loops.Count > 0)
                    {
                        var sub = host.CreateSubDivision(doc, loops);
                        sub.GetParameter(ParameterTypeId.ToposolidSubdivideMaterial)?.Set(material);
                        sub.GetParameter(ParameterTypeId.ToposolidSubdivideHeight)?.Set(UnitConv.Ft(height));
                        e = sub;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Lote {q.Nome}-{p.Numero}: subdivisão do Toposolid", ex);
                    res.Warnings.Add($"Quadra {q.Nome}, lote {p.Numero}: o Toposolid recusou a subdivisão ({ex.Message}) – o lote virou piso plano.");
                }
            }
            if (e == null)
            {
                try
                {
                    e = CreateFloor(doc, p.Forma, cat, material, q.Z);
                    if (e != null) floors++;
                }
                catch (Exception ex)
                {
                    floorReason = ex.Message;
                    Log.Error($"Lote {q.Nome}-{p.Numero}: piso", ex);
                }
            }
            if (e == null)
            {
                res.Warnings.Add($"Quadra {q.Nome}, lote {p.Numero}: não foi possível criar o lote{(floorReason != null ? $" ({floorReason})" : "")}.");
                continue;
            }
            TagLot(e, q, p, cat);
            MarkingStorage.Write(e, q, MarkingColor.Terra);
            res.Elements.Add(e.Id);
        }
        if (floors > 0 && hosts.Count > 0)
            res.Warnings.Add($"Quadra {q.Nome}: {floors} lote(s) fora do Toposolid viraram piso plano.");
        return res;
    }

    private static void TagLot(Element e, QuadraDefinition q, Parcela p, CategoriaLote cat)
    {
        try { e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"{Tag} {q.Nome} {p.Numero} [{p.Chave}]"); } catch { /* opcional */ }
        try { e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set($"{q.Nome}-{p.Numero}"); } catch { /* opcional */ }
        SharedParameters.Set(e, SharedParameters.Quadra, q.Nome);
        SharedParameters.Set(e, SharedParameters.Lote, p.Numero);
        SharedParameters.Set(e, SharedParameters.CategoriaLote, cat.Nome);
        SharedParameters.Set(e, SharedParameters.AreaLote, UnitConv.Ft2(p.Area));
        SharedParameters.Set(e, SharedParameters.Id, q.Id);
        SharedParameters.Set(e, SharedParameters.Codigo, $"{q.Nome}-{p.Numero}");
        SharedParameters.Set(e, SharedParameters.Descricao, $"{LotReport.Rotulo(p)} – Quadra {q.Nome}");
        SharedParameters.Set(e, SharedParameters.Categoria, "Loteamento");
    }

    /// <summary>Piso plano do lote (sem Toposolid): topo na cota da quadra, 5 cm, com o material da categoria.</summary>
    private static Floor? CreateFloor(Document doc, Polygon2 shape, CategoriaLote cat, ElementId material, double zM)
    {
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
        if (levels.Count == 0) throw new InvalidOperationException("o projeto não tem nível");
        var zFt = UnitConv.Ft(zM);
        var level = levels.LastOrDefault(l => l.ProjectElevation <= zFt + 0.01) ?? levels[0];
        var type = FloorTypeFor(doc, cat, material);
        if (type == ElementId.InvalidElementId) throw new InvalidOperationException("o projeto não tem tipo de piso");
        var loops = Loops(doc, shape, level.ProjectElevation);
        if (loops.Count == 0) return null;
        var f = Floor.Create(doc, loops, type, level.Id);
        f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(zFt - level.ProjectElevation + UnitConv.Ft(FloorThickness));
        return f;
    }

    private static ElementId FloorTypeFor(Document doc, CategoriaLote cat, ElementId material)
    {
        var name = Safe($"{StyleService.Prefix}Lote {cat.Nome}");
        var types = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().ToList();
        var ft = types.FirstOrDefault(t => t.Name == name);
        if (ft == null)
        {
            var baseType = types.FirstOrDefault(t => !t.IsFoundationSlab) ?? types.FirstOrDefault();
            if (baseType == null) return ElementId.InvalidElementId;
            ft = (FloorType)baseType.Duplicate(name);
        }
        try
        {
            var cs = CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, UnitConv.Ft(FloorThickness), material);
            cs.EndCap = EndCapCondition.NoEndCap;
            ft.SetCompoundStructure(cs);
        }
        catch (Exception ex)
        {
            Log.Error("Tipo de piso do lote", ex);
        }
        return ft.Id;
    }

    /// <summary>
    /// Material da categoria ("SV - Lote – Área verde"): cor no 3D e, em planta, fundo sólido na cor com a hachura da
    /// categoria por cima – atualizado a cada geração (a cor e a hachura podem mudar).
    /// </summary>
    public static ElementId LotMaterial(Document doc, StyleService styles, CategoriaLote cat)
    {
        var name = Safe($"{StyleService.Prefix}Lote – {cat.Nome}");
        var mat = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name == name);
        if (mat == null)
        {
            var id = Material.Create(doc, name);
            mat = (Material)doc.GetElement(id);
            mat.MaterialClass = "Terra";
            mat.MaterialCategory = "Loteamento";
        }
        var rgb = Rgb.TryParse(cat.Cor, out var c) ? c : new Rgb(242, 215, 166);
        var color = new Color(rgb.R, rgb.G, rgb.B);
        try
        {
            mat.Color = color;
            var solid = styles.SolidFillPattern();
            var hatch = styles.HatchPattern(cat.Hachura);
            var dark = 0.299 * rgb.R + 0.587 * rgb.G + 0.114 * rgb.B < 90 ? new Color(205, 205, 205) : new Color(70, 70, 70);
            if (hatch != ElementId.InvalidElementId)
            {
                mat.SurfaceForegroundPatternId = hatch;
                mat.SurfaceForegroundPatternColor = dark;
                mat.SurfaceBackgroundPatternId = solid;
                mat.SurfaceBackgroundPatternColor = color;
            }
            else
            {
                mat.SurfaceForegroundPatternId = solid;
                mat.SurfaceForegroundPatternColor = color;
                mat.SurfaceBackgroundPatternId = ElementId.InvalidElementId;
            }
            mat.CutForegroundPatternId = solid;
            mat.CutForegroundPatternColor = color;
        }
        catch (Exception ex)
        {
            Log.Error($"Material do lote {cat.Nome}", ex);
        }
        return mat.Id;
    }

    private static List<CurveLoop> Loops(Document doc, Polygon2 shape, double zFt)
    {
        var tol = doc.Application.ShortCurveTolerance * 1.05;
        var res = new List<CurveLoop>();
        var clean = shape.Simplified(0.005, 1e-6) ?? shape;
        foreach (var ring in new[] { clean.Outer }.Concat(clean.Holes))
        {
            var pts = ring.Select(v => new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), zFt)).ToList();
            var kept = new List<XYZ>();
            foreach (var p in pts)
                if (kept.Count == 0 || kept[^1].DistanceTo(p) > tol) kept.Add(p);
            while (kept.Count > 2 && kept[0].DistanceTo(kept[^1]) <= tol) kept.RemoveAt(kept.Count - 1);
            if (kept.Count < 3) continue;
            var loop = new CurveLoop();
            for (int i = 0; i < kept.Count; i++) loop.Append(Line.CreateBound(kept[i], kept[(i + 1) % kept.Count]));
            res.Add(loop);
        }
        return res;
    }

    /// <summary>Contorno em planta do Toposolid (do esboço), nulo se não der para ler.</summary>
    private static Polygon2? Footprint(Document doc, Toposolid t)
    {
        try
        {
            if (doc.GetElement(t.SketchId) is not Sketch sk) return null;
            var rings = new List<IReadOnlyList<Vec2>>();
            foreach (CurveArray arr in sk.Profile)
            {
                var pts = new List<Vec2>();
                foreach (Curve c in arr)
                {
                    var tess = c.Tessellate();
                    for (int i = 0; i + 1 < tess.Count; i++) pts.Add(new Vec2(UnitConv.M(tess[i].X), UnitConv.M(tess[i].Y)));
                }
                if (pts.Count >= 3) rings.Add(pts);
            }
            return PolygonOps.FromContoursEvenOdd(rings).OrderByDescending(p => p.Area).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool Contains(Element e, Vec2 p)
    {
        var bb = e.get_BoundingBox(null);
        return bb != null && UnitConv.M(bb.Min.X) <= p.X && p.X <= UnitConv.M(bb.Max.X) && UnitConv.M(bb.Min.Y) <= p.Y && p.Y <= UnitConv.M(bb.Max.Y);
    }

    private static void SafeDelete(Document doc, ElementId id)
    {
        try { if (doc.GetElement(id) != null) doc.Delete(id); } catch (Exception ex) { Log.Error("Apagar elemento de lote", ex); }
    }

    // ------------------------------------------------------------------ filtros por categoria

    private static ElementId? ParamId(Document doc, SharedParameters.Def d) => SharedParameterElement.Lookup(doc, d.Guid)?.Id;

    private static List<ElementId> FilterCategories(Document doc, IEnumerable<BuiltInCategory> bics)
    {
        var filterable = ParameterFilterUtilities.GetAllFilterableCategories();
        return bics.Select(b => new ElementId(b)).Where(id => filterable.Contains(id) && Category.GetCategory(doc, id) != null).ToList();
    }

    private static ParameterFilterElement? UpsertFilter(Document doc, string name, IList<ElementId> cats, ElementFilter filter)
    {
        if (cats.Count == 0) return null;
        var existing = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>().FirstOrDefault(f => f.Name == name);
        try
        {
            if (existing != null)
            {
                existing.SetCategories(cats);
                existing.SetElementFilter(filter);
                return existing;
            }
            return ParameterFilterElement.Create(doc, name, cats, filter);
        }
        catch (Exception ex)
        {
            Log.Error($"Filtro {name}", ex);
            return existing;
        }
    }

    /// <summary>Filtro de vista de uma categoria de lote ("SV - Lote – Área verde"): SV_CategoriaLote igual ao nome.</summary>
    public static ParameterFilterElement? CategoryFilter(Document doc, CategoriaLote cat)
    {
        if (ParamId(doc, SharedParameters.CategoriaLote) is not { } pid) return null;
        var rule = ParameterFilterRuleFactory.CreateEqualsRule(pid, cat.Nome);
        return UpsertFilter(doc, Safe($"{StyleService.Prefix}Lote – {cat.Nome}"), FilterCategories(doc, SharedParameters.LotCategories), new ElementParameterFilter(rule));
    }

    /// <summary>
    /// Aplica os filtros das categorias na vista (ou no modelo de vista que a controla): cor e hachura de cada categoria e
    /// visibilidade (<paramref name="visible"/>: nome → mostrar; ausente = mostrar).
    /// </summary>
    public static View ApplyCategoryFilters(Document doc, View view, LoteamentoDefinition lot, IReadOnlyDictionary<string, bool>? visible, bool colors, List<string> notes)
    {
        SharedParameters.EnsureLotes(doc);
        var target = view;
        if (view.ViewTemplateId != ElementId.InvalidElementId && doc.GetElement(view.ViewTemplateId) is View tpl)
        {
            target = tpl;
            notes.Add($"A vista usa o modelo \"{tpl.Name}\": os filtros foram aplicados nele (valem para todas as vistas com esse modelo).");
        }
        var styles = new StyleService(doc);
        var solid = styles.SolidFillPattern();
        foreach (var cat in lot.Categorias)
        {
            var f = CategoryFilter(doc, cat);
            if (f == null) { notes.Add($"Filtro da categoria {cat.Nome} não criado (parâmetro SV_CategoriaLote ausente)."); continue; }
            try
            {
                if (!target.IsFilterApplied(f.Id)) target.AddFilter(f.Id);
                var ogs = new OverrideGraphicSettings();
                if (colors && Rgb.TryParse(cat.Cor, out var rgb))
                {
                    var c = new Color(rgb.R, rgb.G, rgb.B);
                    var hatch = styles.HatchPattern(cat.Hachura);
                    if (hatch != ElementId.InvalidElementId)
                    {
                        ogs.SetSurfaceForegroundPatternId(hatch);
                        ogs.SetSurfaceForegroundPatternColor(new Color(70, 70, 70));
                        ogs.SetSurfaceBackgroundPatternId(solid);
                        ogs.SetSurfaceBackgroundPatternColor(c);
                    }
                    else
                    {
                        ogs.SetSurfaceForegroundPatternId(solid);
                        ogs.SetSurfaceForegroundPatternColor(c);
                    }
                }
                target.SetFilterOverrides(f.Id, ogs);
                target.SetFilterVisibility(f.Id, visible == null || !visible.TryGetValue(cat.Nome, out var v) || v);
            }
            catch (Exception ex)
            {
                Log.Error($"Aplicar filtro {cat.Nome}", ex);
                notes.Add($"Filtro {cat.Nome}: {ex.Message}");
            }
        }
        return target;
    }

    // ------------------------------------------------------------------ masterplan

    /// <summary>Filtro que esconde a sinalização, os dispositivos, o piso tátil, a drenagem e os detalhes do plugin.</summary>
    public static ParameterFilterElement? MasterplanFilter(Document doc)
    {
        if (ParamId(doc, SharedParameters.Categoria) is not { } pid) return null;
        var rules = Masterplan.ValoresOcultos.Select(v => (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(pid, v))).ToList();
        var cats = FilterCategories(doc, new[] { BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors, BuiltInCategory.OST_DetailComponents });
        return UpsertFilter(doc, $"{StyleService.Prefix}Masterplan – sinalização e detalhes", cats, new LogicalOrFilter(rules));
    }

    /// <summary>
    /// Vista do masterplan: cópia da planta sem as anotações dela, cores das categorias, sem sinalização nem textos técnicos;
    /// cria (uma vez) o modelo de vista "SV - Masterplan" a partir dela.
    /// </summary>
    public static View CreateMasterplanView(Document doc, View source, LoteamentoDefinition lot, List<string> notes)
    {
        if (source is not ViewPlan || source.IsTemplate)
            throw new UserMessageException("Abra uma planta (de piso ou de implantação) para gerar o masterplan a partir dela.");
        var id = source.Duplicate(ViewDuplicateOption.Duplicate);
        var mp = (View)doc.GetElement(id);
        try { mp.ViewTemplateId = ElementId.InvalidElementId; } catch { /* sem modelo */ }
        var name = Safe($"{Masterplan.PrefixoVista} – {lot.Nome}");
        var used = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name).ToHashSet();
        var k = 2;
        var final = name;
        while (used.Contains(final)) final = $"{name} ({k++})";
        try { mp.Name = final; } catch (Exception ex) { Log.Error("Nome do masterplan", ex); }
        try { mp.DisplayStyle = DisplayStyle.FlatColors; } catch { /* estilo não suportado */ }
        // Sem cotas, etiquetas, níveis, eixos, linhas de referência: só os textos (nomes de quadra, via e números).
        foreach (Category c in doc.Settings.Categories)
        {
            try
            {
                var hide = c.CategoryType == CategoryType.Annotation && c.BuiltInCategory != BuiltInCategory.OST_TextNotes
                           || c.BuiltInCategory is BuiltInCategory.OST_Lines or BuiltInCategory.OST_Mass;
                if (hide && mp.CanCategoryBeHidden(c.Id)) mp.SetCategoryHidden(c.Id, true);
            }
            catch { /* categoria sem controle de visibilidade nesta vista */ }
        }
        if (MasterplanFilter(doc) is { } f)
        {
            try
            {
                if (!mp.IsFilterApplied(f.Id)) mp.AddFilter(f.Id);
                mp.SetFilterVisibility(f.Id, false);
            }
            catch (Exception ex) { notes.Add("Filtro da sinalização: " + ex.Message); }
        }
        else notes.Add("Parâmetro SV_Categoria ausente: a sinalização não pôde ser ocultada por filtro.");
        ApplyCategoryFilters(doc, mp, lot, null, true, notes);
        if (!new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => v.IsTemplate && v.Name == Masterplan.NomeModelo))
        {
            try
            {
                var tpl = mp.CreateViewTemplate();
                tpl.Name = Masterplan.NomeModelo;
                notes.Add($"Modelo de vista \"{Masterplan.NomeModelo}\" criado – aplique-o a outras plantas para o mesmo layout limpo.");
            }
            catch (Exception ex) { Log.Error("Modelo de vista do masterplan", ex); }
        }
        return mp;
    }

    // ------------------------------------------------------------------ tabela nativa

    public const string ScheduleName = "SV - Lotes do loteamento";

    /// <summary>Tabela do Revit com todos os lotes (quadra, lote, categoria e área), atualizada pelo próprio Revit.</summary>
    public static ViewSchedule? LotSchedule(Document doc, List<string> notes)
    {
        SharedParameters.EnsureLotes(doc);
        var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().FirstOrDefault(v => v.Name == ScheduleName);
        if (existing != null) return existing;
        try
        {
            var sched = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
            sched.Name = ScheduleName;
            var def = sched.Definition;
            var fields = def.GetSchedulableFields();
            ScheduleField? Add(SharedParameters.Def d, string heading)
            {
                var pid = ParamId(doc, d);
                var sf = fields.FirstOrDefault(f => f.ParameterId == pid);
                if (sf == null) return null;
                var field = def.AddField(sf);
                field.ColumnHeading = heading;
                return field;
            }
            var quadra = Add(SharedParameters.Quadra, "Quadra");
            var loteF = Add(SharedParameters.Lote, "Lote");
            Add(SharedParameters.CategoriaLote, "Categoria");
            var area = Add(SharedParameters.AreaLote, "Área (m²)");
            if (loteF != null) def.AddFilter(new ScheduleFilter(loteF.FieldId, ScheduleFilterType.HasValue));
            if (quadra != null) def.AddSortGroupField(new ScheduleSortGroupField(quadra.FieldId) { ShowHeader = false });
            if (loteF != null) def.AddSortGroupField(new ScheduleSortGroupField(loteF.FieldId));
            if (area != null) try { area.DisplayType = ScheduleFieldDisplayType.Totals; } catch { /* sem total */ }
            def.IsItemized = true;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalCount = true;
            return sched;
        }
        catch (Exception ex)
        {
            Log.Error("Tabela de lotes", ex);
            notes.Add("Tabela do Revit não criada: " + ex.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------ gleba e vias

    /// <summary>Contorno fechado (m) a partir de linhas, linha de propriedade ou Toposolid selecionados; nulo se não fechar.</summary>
    public static Polygon2? GlebaFrom(Document doc, IEnumerable<Element> elements, List<string> notes)
    {
        var pieces = new List<IReadOnlyList<Vec2>>();
        foreach (var e in elements)
        {
            if (e is Toposolid t && Footprint(doc, t) is { } fp) return fp;
            foreach (var c in Curves(e))
            {
                var pts = c.Tessellate().Select(p => new Vec2(UnitConv.M(p.X), UnitConv.M(p.Y))).ToList();
                if (pts.Count >= 2) pieces.Add(pts);
            }
        }
        if (pieces.Count == 0) return null;
        var chained = PathChainer.Chain(pieces, 0.05);
        var ring = chained.Chains.OrderByDescending(ch => ch.Count).FirstOrDefault();
        if (ring == null || ring.Count < 3 || !PathChainer.IsClosed(ring, 0.05))
        {
            notes.Add("As linhas escolhidas não formam um contorno fechado.");
            return null;
        }
        if (ring[0].DistanceTo(ring[^1]) < 0.05) ring.RemoveAt(ring.Count - 1);
        var poly = new Polygon2(ring);
        return poly.IsValid ? poly : null;
    }

    private static IEnumerable<Curve> Curves(Element e)
    {
        if (e is CurveElement ce && ce.GeometryCurve != null)
        {
            yield return ce.GeometryCurve;
            yield break;
        }
        GeometryElement? ge = null;
        try { ge = e.get_Geometry(new Options()); } catch { /* sem geometria */ }
        if (ge == null) yield break;
        foreach (var o in ge)
        {
            if (o is Curve c) yield return c;
            else if (o is GeometryInstance gi)
                foreach (var g in gi.GetInstanceGeometry())
                    if (g is Curve c2) yield return c2;
        }
    }

    /// <summary>
    /// O que as vias ocupam na gleba: peças das vias do plugin (pavimento, calçadas, meios-fios, canteiros) e, se pedido,
    /// pisos do Revit que não são do plugin (vias desenhadas como piso).
    /// </summary>
    public static List<Polygon2> Ocupacao(Document doc, MarkingService service, Polygon2 gleba, bool nativeFloors)
    {
        var (gmn, gmx) = gleba.Bounds;
        bool Near(Polygon2 p)
        {
            var (mn, mx) = p.Bounds;
            return mx.X >= gmn.X - 1 && mn.X <= gmx.X + 1 && mx.Y >= gmn.Y - 1 && mn.Y <= gmx.Y + 1;
        }
        var res = new List<Polygon2>();
        foreach (var d in MarkingStorage.Definitions(doc))
        {
            if (LotDrawing.IsParcelamento(d) || d is IAnnotationDefinition) continue;
            MarkingGeometry? geo;
            try { geo = service.BuildGeometryOrNull(d); }
            catch (Exception ex) { Log.Error($"Ocupação {d.DisplayCode}", ex); continue; }
            if (geo == null) continue;
            res.AddRange(geo.Pieces.Where(Reconhecimento.Viaria).Select(p => p.Shape).Where(Near));
        }
        if (nativeFloors)
        {
            foreach (var f in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
            {
                if (MarkingStorage.Read(f) != null) continue;
                try
                {
                    if (doc.GetElement(f.SketchId) is not Sketch sk) continue;
                    var rings = new List<IReadOnlyList<Vec2>>();
                    foreach (CurveArray arr in sk.Profile)
                    {
                        var pts = new List<Vec2>();
                        foreach (Curve c in arr)
                        {
                            var t = c.Tessellate();
                            for (int i = 0; i + 1 < t.Count; i++) pts.Add(new Vec2(UnitConv.M(t[i].X), UnitConv.M(t[i].Y)));
                        }
                        if (pts.Count >= 3) rings.Add(pts);
                    }
                    res.AddRange(PolygonOps.FromContoursEvenOdd(rings).Where(Near));
                }
                catch (Exception ex) { Log.Error("Ocupação (piso)", ex); }
            }
        }
        return res;
    }

    /// <summary>Eixo (m) de um elemento escolhido como via: via do plugin (eixo da pista), linha ou piso do Revit.</summary>
    public static List<Vec2>? AxisOf(Document doc, Element e)
    {
        if (MarkingStorage.Read(e) is { } stored)
        {
            var defs = MarkingStorage.Definitions(doc);
            var def = stored.Definition;
            // Elemento de uma via do plugin: o eixo do pavimento do mesmo grupo.
            var pav = def as RoadPavementDefinition
                      ?? (def.GroupId != null ? defs.OfType<RoadPavementDefinition>().FirstOrDefault(p => p.GroupId == def.GroupId) : null);
            var path = (pav ?? def).Path;
            if (path != null && PathResolver.Resolve(doc, path)?.Main is { } main) return main.Points.ToList();
        }
        var curves = Curves(e).ToList();
        if (curves.Count > 0)
        {
            var pieces = curves.Select(c => (IReadOnlyList<Vec2>)c.Tessellate().Select(p => new Vec2(UnitConv.M(p.X), UnitConv.M(p.Y))).ToList()).ToList();
            return PathChainer.Chain(pieces, 0.05).Chains.OrderByDescending(Length).FirstOrDefault();
        }
        if (e is Floor f && doc.GetElement(f.SketchId) is Sketch sk)
        {
            var rings = new List<IReadOnlyList<Vec2>>();
            foreach (CurveArray arr in sk.Profile)
            {
                var pts = new List<Vec2>();
                foreach (Curve c in arr)
                {
                    var t = c.Tessellate();
                    for (int i = 0; i + 1 < t.Count; i++) pts.Add(new Vec2(UnitConv.M(t[i].X), UnitConv.M(t[i].Y)));
                }
                if (pts.Count >= 3) rings.Add(pts);
            }
            var net = Core.Automation.FloorRoads.Read(PolygonOps.FromContoursEvenOdd(rings));
            return net.Roads.Select(r => r.Axis.Points.ToList()).OrderByDescending(Length).FirstOrDefault();
        }
        return null;
    }

    private static double Length(List<Vec2> pts)
    {
        var s = 0.0;
        for (int i = 0; i + 1 < pts.Count; i++) s += pts[i].DistanceTo(pts[i + 1]);
        return s;
    }
}
