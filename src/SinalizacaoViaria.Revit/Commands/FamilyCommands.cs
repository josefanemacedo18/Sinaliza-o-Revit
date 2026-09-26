using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Quantities;
using SinalizacaoViaria.Revit.Infrastructure;
using SinalizacaoViaria.Revit.UI;

namespace SinalizacaoViaria.Revit.Commands;

/// <summary>Opções da inserção de famílias do Revit como elementos urbanos (lembradas durante a sessão).</summary>
internal sealed class FamilyPlacementOptions
{
    public const long LoadFromFile = -1;

    public long SymbolId { get; set; }
    public CategoriaUrbana Category { get; set; } = CategoriaUrbana.Assentos;
    public bool CategoryTouched { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public HierarquiaViaria Hierarchy { get; set; } = HierarquiaViaria.NaoDefinida;
    /// <summary>0 = por cliques; 1 = ao longo de linhas; 2 = classificar famílias já inseridas.</summary>
    public int Mode { get; set; }
    public bool AskDirection { get; set; } = true;
    public double RotationDeg { get; set; }
    public double Elevation { get; set; }
    public bool OnSurface { get; set; } = true;
    public double Spacing { get; set; } = 10;
    public double Offset { get; set; }
    public double Start { get; set; } = 2;
    public double End { get; set; } = 1;
    public bool BothSides { get; set; }
    public bool Stagger { get; set; }
    public PathMode PathMode { get; set; } = PathMode.Linhas;

    public static FamilyPlacementOptions Last { get; set; } = new();
}

/// <summary>
/// Insere famílias do Revit (componentes do usuário, com o design que ele quiser) como elementos urbanos – por cliques ou
/// distribuídas ao longo de linhas – e as classifica para o quantitativo (categoria 7, com subcategoria, código e hierarquia).
/// Também classifica famílias já existentes no projeto.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdElementoFamilia : CommandBase
{
    private static readonly BuiltInCategory[] Preferred =
    {
        BuiltInCategory.OST_Site, BuiltInCategory.OST_Planting, BuiltInCategory.OST_Furniture, BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_Entourage, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_Parking,
        BuiltInCategory.OST_Signage, BuiltInCategory.OST_Hardscape,
    };

    protected override Result Run(UIApplication app, UIDocument uidoc)
    {
        var doc = uidoc.Document;
        var o = FamilyPlacementOptions.Last;
        while (true)
        {
            var symbols = Symbols(doc);
            if (symbols.All(s => s.Id.Value != o.SymbolId))
                o.SymbolId = symbols.FirstOrDefault(s => Preferred.Contains(Bic(s)))?.Id.Value ?? symbols.FirstOrDefault()?.Id.Value ?? FamilyPlacementOptions.LoadFromFile;
            if (!ShowForm(doc, symbols, o)) return Result.Cancelled;
            if (o.Mode == 2) return Classify(uidoc, o);
            if (o.SymbolId != FamilyPlacementOptions.LoadFromFile) break;

            // Carregar família de arquivo e voltar ao formulário com ela selecionada.
            var dlg = new OpenFileDialog { Filter = "Família do Revit (*.rfa)|*.rfa", Title = "Carregar família de elemento urbano" };
            if (dlg.ShowDialog() != true) continue;
            using var t = new Transaction(doc, "SV - Carregar família");
            t.Start();
            if (doc.LoadFamily(dlg.FileName, new ReloadOptions(), out var fam) && fam != null)
            {
                var first = fam.GetFamilySymbolIds().Select(doc.GetElement).OfType<FamilySymbol>().FirstOrDefault();
                o.SymbolId = first?.Id.Value ?? FamilyPlacementOptions.LoadFromFile;
                if (first != null && !o.CategoryTouched) o.Category = UrbanCategories.Guess($"{fam.Name} {fam.FamilyCategory?.Name}");
                o.Code = "";
                o.Description = "";
                t.Commit();
            }
            else
            {
                t.RollBack();
                // Já carregada: seleciona pelo nome do arquivo.
                var name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                var existing = Symbols(doc).FirstOrDefault(s => string.Equals(s.FamilyName, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) o.SymbolId = existing.Id.Value;
                else TaskDialog.Show(AppTitle, "Não foi possível carregar a família:\n" + dlg.FileName);
            }
        }

        if (doc.GetElement(new ElementId(o.SymbolId)) is not FamilySymbol symbol) return Result.Cancelled;
        var placementType = symbol.Family.FamilyPlacementType;
        if (placementType is not (FamilyPlacementType.OneLevelBased or FamilyPlacementType.WorkPlaneBased or FamilyPlacementType.TwoLevelsBased))
        {
            TaskDialog.Show(AppTitle, $"A família \"{symbol.FamilyName}\" é do tipo {placementType} (hospedada em parede/face, baseada em linha ou adaptativa) " +
                                      "e não pode ser inserida por ponto. Use uma família baseada em nível ou em plano de trabalho " +
                                      "(ex.: modelo genérico, mobiliário, plantio, luminária independente) – ou insira-a pelo Revit e use 'Classificar famílias já inseridas'.");
            return Result.Cancelled;
        }

        var placer = new Placer(doc, symbol, o);
        var count = o.Mode == 1 ? AlongPath(uidoc, placer, o) : ByClicks(uidoc, placer, o);
        if (count == 0) return Result.Cancelled;
        TaskDialog.Show(AppTitle, $"{count} elemento(s) \"{symbol.FamilyName} : {symbol.Name}\" inserido(s) e classificado(s) em " +
                                  $"\"{UrbanCategories.Label(o.Category)}\" (código {placer.Code}).\n\n" +
                                  "Eles entram no Quantitativo (categoria 7 – Mobiliário e elementos urbanos) e nas tabelas do Revit.");
        return Result.Succeeded;
    }

    // ------------------------------------------------------------------ formulário

    private static bool ShowForm(Document doc, List<FamilySymbol> symbols, FamilyPlacementOptions o)
    {
        var options = symbols.Select(s => (Label(s), s.Id.Value)).ToList();
        options.Add(("➕ Carregar família de arquivo (.rfa)...", FamilyPlacementOptions.LoadFromFile));
        var hierarchies = new[] { ("Não definida", HierarquiaViaria.NaoDefinida) }.Concat(Hierarquia.Definidas.Select(h => (Hierarquia.Label(h), h)));
        var w = new FormWindow("Elementos urbanos – famílias do Revit – SinalizaBIM", "Famílias do Revit como elementos urbanos",
                "Use qualquer família de componente (mobiliário, luminárias, plantio, modelo genérico...) com o design que quiser. " +
                "Ela é classificada por subcategoria e entra detalhada no Quantitativo e nas tabelas do Revit.",
                null, null, showOutput: false, okText: "Continuar", width: 640, height: 720)
            .Section("O que fazer")
            .Choice("Ação", new[] { ("Inserir por cliques", 0), ("Distribuir ao longo de linhas", 1), ("Classificar famílias já inseridas (selecionar)", 2) },
                () => o.Mode, v => o.Mode = v)
            .Section("Família e classificação")
            .Choice("Família : tipo", options, () => o.SymbolId, v => o.SymbolId = v,
                "Famílias carregadas no projeto (baseadas em nível ou em plano de trabalho). A última opção carrega um arquivo .rfa.",
                preset: v =>
                {
                    // Nova família: código/descrição automáticos e subcategoria sugerida pelo nome.
                    if (v != o.SymbolId) { o.Code = ""; o.Description = ""; }
                    if (v != FamilyPlacementOptions.LoadFromFile && !o.CategoryTouched && doc.GetElement(new ElementId(v)) is FamilySymbol s)
                        o.Category = UrbanCategories.Guess($"{s.FamilyName} {s.Name} {s.Category?.Name}");
                    o.SymbolId = v;
                })
            .Choice("Subcategoria urbana", UrbanCategories.All.Select(c => (UrbanCategories.Label(c), c)), () => o.Category,
                v => { if (v != o.Category) o.CategoryTouched = true; o.Category = v; },
                "Separa o quantitativo (iluminação, arborização, bancos, lixeiras, abrigos...). Sugerida pelo nome da família.")
            .Text("Código no quantitativo", () => o.Code, v => o.Code = (v ?? "").Trim().ToUpperInvariant(),
                tooltip: "Ex.: BANCO-01, POSTE-LED, ARV-IPE. Vazio = gerado a partir do nome da família.")
            .Text("Descrição", () => o.Description, v => o.Description = (v ?? "").Trim(),
                tooltip: "Vazio = 'Família : Tipo'.")
            .Choice("Hierarquia viária", hierarchies, () => o.Hierarchy, v => o.Hierarchy = v,
                "Opcional – separa o elemento no resumo por hierarquia (CTB art. 60).")
            .Section("Posição (inserção)")
            .Check("Orientar por um segundo clique (sentido para onde o elemento fica voltado)", () => o.AskDirection, v => o.AskDirection = v)
            .Number("Rotação adicional (°)", () => o.RotationDeg, v => o.RotationDeg = v, -360, 360, "0.#")
            .Number("Elevação acima da superfície (m)", () => o.Elevation, v => o.Elevation = v, -50, 50, "0.00",
                "Ex.: 0,15 para apoiar no topo de uma calçada desenhada como linha.")
            .Check("Assentar sobre a superfície (topografia/pisos) sob cada elemento", () => o.OnSurface, v => o.OnSurface = v)
            .Section("Distribuição ao longo de linhas")
            .Choice("Caminho", new[] { ("Selecionar linhas existentes", PathMode.Linhas), (UiHelpers.EdgesLabel, PathMode.Bordas), ("Desenhar por pontos", PathMode.Desenhar) },
                () => o.PathMode, v => o.PathMode = v)
            .Number("Espaçamento (m)", () => o.Spacing, v => o.Spacing = v, 0.2, 500, "0.00")
            .Number("Afastamento lateral da linha (m)", () => o.Offset, v => o.Offset = v, -50, 50, "0.00", "+ à esquerda do sentido da linha")
            .Number("Distância inicial (m)", () => o.Start, v => o.Start = v, 0, 1000, "0.00")
            .Number("Recuo no final (m)", () => o.End, v => o.End = v, 0, 1000, "0.00")
            .Check("Nos dois lados (afastamento espelhado)", () => o.BothSides, v => o.BothSides = v)
            .Check("Alternar lados (quincôncio – meio espaçamento)", () => o.Stagger, v => o.Stagger = v);
        return UiHelpers.ShowModal(w) == true;
    }

    private static List<FamilySymbol> Symbols(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(s => s.Category is { CategoryType: CategoryType.Model } && s.Family != null
                        && s.Family.FamilyPlacementType is FamilyPlacementType.OneLevelBased or FamilyPlacementType.WorkPlaneBased or FamilyPlacementType.TwoLevelsBased
                        && !MarkingStorage.IsMarking(s))
            .OrderBy(s => Preferred.Contains(Bic(s)) ? 0 : 1).ThenBy(s => s.Category!.Name).ThenBy(s => s.FamilyName).ThenBy(s => s.Name)
            .ToList();

    private static BuiltInCategory Bic(Element e) => e.Category == null ? BuiltInCategory.INVALID : e.Category.BuiltInCategory;

    private static string Label(FamilySymbol s) => $"{s.Category?.Name} › {s.FamilyName} : {s.Name}";

    // ------------------------------------------------------------------ inserção

    private static int ByClicks(UIDocument uidoc, Placer placer, FamilyPlacementOptions o)
    {
        var n = 0;
        while (true)
        {
            var p = Picking.PickPoint(uidoc, $"{placer.Symbol.FamilyName}: clique o ponto de inserção – ESC encerra");
            if (p == null) break;
            var dir = XYZ.BasisY;
            if (o.AskDirection)
            {
                var q = Picking.PickPoint(uidoc, $"{placer.Symbol.FamilyName}: clique para onde o elemento fica voltado");
                if (q == null) break;
                var d = new XYZ(q.X - p.X, q.Y - p.Y, 0);
                if (d.GetLength() > 1e-6) dir = d.Normalize();
            }
            using var t = new Transaction(uidoc.Document, "SV - Elemento urbano (família)");
            t.Start();
            placer.Prepare();
            if (placer.Place(p, dir) != null) n++;
            t.Commit();
        }
        return n;
    }

    private static int AlongPath(UIDocument uidoc, Placer placer, FamilyPlacementOptions o)
    {
        var doc = uidoc.Document;
        var pr = MarkingCreator.PickPath(uidoc, o.PathMode, placer.Symbol.FamilyName);
        var path = PathResolver.Resolve(doc, pr);
        if (path == null || path.Chains.Count == 0) return 0;
        var zFt = UnitConv.Ft(path.Z);
        using var t = new Transaction(doc, "SV - Elementos urbanos ao longo de linhas (família)");
        t.Start();
        placer.Prepare();
        var n = 0;
        foreach (var chain in path.Chains)
            foreach (var slot in FamilyLayout.Along(chain, o.Spacing, o.Start, o.End, o.Offset, o.BothSides, o.Stagger, 0))
            {
                // Voltado para a linha (frente perpendicular ao caminho) + rotação adicional aplicada no Place.
                var p = new XYZ(UnitConv.Ft(slot.Position.X), UnitConv.Ft(slot.Position.Y), zFt);
                var d = slot.Direction.PerpRight;
                if (placer.Place(p, new XYZ(d.X, d.Y, 0)) != null) n++;
            }
        t.Commit();
        return n;
    }

    /// <summary>Cria as instâncias, ajusta elevação/rotação e grava a classificação.</summary>
    private sealed class Placer
    {
        private readonly Document _doc;
        private readonly FamilyPlacementOptions _o;
        private readonly List<Level> _levels;
        private readonly Dictionary<long, SketchPlane> _planes = new();
        private SurfaceSampler? _sampler;
        public FamilySymbol Symbol { get; }
        public string Code { get; }
        public string Description { get; }

        public Placer(Document doc, FamilySymbol symbol, FamilyPlacementOptions o)
        {
            _doc = doc;
            _o = o;
            Symbol = symbol;
            _levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            Code = string.IsNullOrWhiteSpace(o.Code) ? AutoCode(symbol) : o.Code;
            Description = string.IsNullOrWhiteSpace(o.Description) ? $"{symbol.FamilyName} : {symbol.Name}" : o.Description;
        }

        public static string AutoCode(FamilySymbol s)
        {
            var name = new string(s.FamilyName.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim().ToUpperInvariant().Replace(' ', '-');
            return string.IsNullOrEmpty(name) ? "FAMILIA" : name.Length > 20 ? name[..20] : name;
        }

        public void Prepare()
        {
            if (!Symbol.IsActive) Symbol.Activate();
            if (Symbol.Category != null) SharedParameters.BindTo(_doc, Symbol.Category);
            else SharedParameters.Ensure(_doc);
            if (_o.OnSurface && _sampler == null)
            {
                try { _sampler = new SurfaceSampler(_doc, Array.Empty<string>(), allowCreateView: true); }
                catch (Exception ex) { Log.Error("Elemento urbano: superfície", ex); }
            }
        }

        public FamilyInstance? Place(XYZ point, XYZ facing)
        {
            var z = point.Z;
            if (_o.OnSurface && _sampler is { IsAvailable: true } && _sampler.TrySample(point.X, point.Y, point.Z, out var zs, out _)) z = zs;
            z += UnitConv.Ft(_o.Elevation);
            var p = new XYZ(point.X, point.Y, z);
            FamilyInstance? fi = null;
            try
            {
                if (Symbol.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased)
                {
                    var key = (long)Math.Round(z * 1000);
                    if (!_planes.TryGetValue(key, out var sp) || !sp.IsValidObject)
                        _planes[key] = sp = SketchPlane.Create(_doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z)));
                    fi = _doc.Create.NewFamilyInstance(sp.GetPlaneReference(), p, XYZ.BasisX, Symbol);
                }
                else
                {
                    var level = _levels.LastOrDefault(l => l.Elevation <= z + 1e-3) ?? _levels.FirstOrDefault();
                    fi = level != null
                        ? _doc.Create.NewFamilyInstance(p, Symbol, level, StructuralType.NonStructural)
                        : _doc.Create.NewFamilyInstance(p, Symbol, StructuralType.NonStructural);
                    _doc.Regenerate();
                    // Garante a cota: ajusta o deslocamento do nível quando o Revit ignora o Z do ponto.
                    if (fi.Location is LocationPoint lp && Math.Abs(lp.Point.Z - z) > 1e-3)
                    {
                        var off = fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM) ?? fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                        if (off is { IsReadOnly: false } && level != null) off.Set(z - level.Elevation);
                        else ElementTransformUtils.MoveElement(_doc, fi.Id, new XYZ(0, 0, z - lp.Point.Z));
                    }
                }
                _doc.Regenerate();
                // Frente da família (+Y do modelo) voltada para 'facing', mais a rotação adicional.
                var ang = Math.Atan2(facing.Y, facing.X) - Math.PI / 2 + _o.RotationDeg * Math.PI / 180;
                if (fi.Location is LocationPoint loc && Math.Abs(ang) > 1e-6)
                    ElementTransformUtils.RotateElement(_doc, fi.Id, Line.CreateBound(loc.Point, loc.Point + XYZ.BasisZ), ang);
                FamilyClassifier.Apply(fi, _o.Category, Code, Description, _o.Hierarchy);
                return fi;
            }
            catch (Exception ex)
            {
                Log.Error($"Elemento urbano {Symbol.FamilyName}", ex);
                if (fi != null && fi.IsValidObject) try { _doc.Delete(fi.Id); } catch { /* ignora */ }
                return null;
            }
        }
    }

    // ------------------------------------------------------------------ classificar existentes

    private sealed class InstanceFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is FamilyInstance { Category.CategoryType: CategoryType.Model } && !MarkingStorage.IsMarking(e);
        public bool AllowReference(Reference r, XYZ p) => false;
    }

    private static Result Classify(UIDocument uidoc, FamilyPlacementOptions o)
    {
        var doc = uidoc.Document;
        IList<Reference> refs;
        try
        {
            var pre = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => new InstanceFilter().AllowElement(e)).ToList();
            refs = pre.Count > 0
                ? pre.Select(e => new Reference(e)).ToList()
                : uidoc.Selection.PickObjects(ObjectType.Element, new InstanceFilter(), "Selecione as famílias a classificar como elementos urbanos e clique em Concluir");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        var items = refs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().ToList();
        if (items.Count == 0) return Result.Cancelled;
        using var t = new Transaction(doc, "SV - Classificar elementos urbanos");
        t.Start();
        foreach (var cat in items.Select(i => i.Category).Where(c => c != null).GroupBy(c => c.Id).Select(g => g.First()))
            SharedParameters.BindTo(doc, cat);
        foreach (var fi in items)
        {
            var code = string.IsNullOrWhiteSpace(o.Code) ? Placer.AutoCode(fi.Symbol) : o.Code;
            var desc = string.IsNullOrWhiteSpace(o.Description) ? $"{fi.Symbol.FamilyName} : {fi.Symbol.Name}" : o.Description;
            var cat = o.CategoryTouched ? o.Category : UrbanCategories.Guess($"{fi.Symbol.FamilyName} {fi.Symbol.Name} {fi.Category?.Name}");
            FamilyClassifier.Apply(fi, cat, code, desc, o.Hierarchy);
        }
        t.Commit();
        TaskDialog.Show(AppTitle, $"{items.Count} família(s) classificada(s) como elementos urbanos.\n" +
                                  (o.CategoryTouched ? $"Subcategoria: {UrbanCategories.Label(o.Category)}." : "Subcategoria sugerida pelo nome de cada família.") +
                                  "\n\nPara mudar depois, edite os parâmetros SV_Grupo (subcategoria) e SV_Codigo na paleta Propriedades.");
        return Result.Succeeded;
    }
}

/// <summary>Grava/lê a classificação de famílias do usuário como elementos urbanos (parâmetros SV_*).</summary>
internal static class FamilyClassifier
{
    public static string CategoryLabel => QuantityRow.CategoryLabel(CategoriaQuantitativo.MobiliarioUrbano);

    public static void Apply(FamilyInstance fi, CategoriaUrbana cat, string code, string description, HierarquiaViaria hierarchy)
    {
        SharedParameters.Set(fi, SharedParameters.Categoria, CategoryLabel);
        SharedParameters.Set(fi, SharedParameters.Grupo, UrbanCategories.Label(cat));
        SharedParameters.Set(fi, SharedParameters.Codigo, code);
        SharedParameters.Set(fi, SharedParameters.Descricao, description);
        SharedParameters.Set(fi, SharedParameters.Quantidade, 1);
        SharedParameters.Set(fi, SharedParameters.Referencia, "Família do Revit (projeto)");
        if (hierarchy != HierarquiaViaria.NaoDefinida) SharedParameters.Set(fi, SharedParameters.Hierarquia, Hierarquia.Label(hierarchy));
    }

    /// <summary>Famílias do usuário classificadas como elemento urbano (entrada do quantitativo).</summary>
    public static List<FamilyItem> Collect(Document doc)
    {
        var res = new List<FamilyItem>();
        foreach (var fi in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
        {
            if (MarkingStorage.IsMarking(fi)) continue;
            var cat = fi.get_Parameter(SharedParameters.Categoria.Guid)?.AsString();
            if (!string.Equals(cat, CategoryLabel, StringComparison.OrdinalIgnoreCase)) continue;
            var label = fi.get_Parameter(SharedParameters.Hierarquia.Guid)?.AsString();
            HierarquiaViaria? h = string.IsNullOrWhiteSpace(label) ? null : Hierarquia.Definidas.Cast<HierarquiaViaria?>().FirstOrDefault(x => Hierarquia.Label(x) == label);
            res.Add(new FamilyItem(
                fi.get_Parameter(SharedParameters.Codigo.Guid)?.AsString() ?? "",
                fi.get_Parameter(SharedParameters.Descricao.Guid)?.AsString() ?? "",
                $"{fi.Symbol?.FamilyName} : {fi.Symbol?.Name}",
                UrbanCategories.Parse(fi.get_Parameter(SharedParameters.Grupo.Guid)?.AsString()),
                h));
        }
        return res;
    }
}

/// <summary>Recarregar família já existente: mantém os valores do projeto.</summary>
internal sealed class ReloadOptions : IFamilyLoadOptions
{
    public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
    {
        overwriteParameterValues = false;
        return true;
    }

    public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
    {
        source = FamilySource.Family;
        overwriteParameterValues = false;
        return true;
    }
}
