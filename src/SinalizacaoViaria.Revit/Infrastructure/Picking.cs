using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Filtro de seleção: linhas de modelo e de detalhe.</summary>
public sealed class CurveSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => PathResolver.IsPathElement(elem);
    public bool AllowReference(Reference reference, XYZ position) => false;
}

/// <summary>Filtro de seleção: elementos de sinalização gerados pelo plugin.</summary>
public sealed class MarkingSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => (elem is DirectShape || elem is FilledRegion || elem is TextNote || elem is CurveElement) && MarkingStorage.IsMarking(elem);
    public bool AllowReference(Reference reference, XYZ position) => false;
}

/// <summary>Filtro de seleção: superfícies para projeção (Toposolid, pisos, topografia).</summary>
public sealed class SurfaceSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        var bic = elem.Category?.BuiltInCategory;
        return bic is BuiltInCategory.OST_Toposolid or BuiltInCategory.OST_Floors or BuiltInCategory.OST_Topography
            or BuiltInCategory.OST_Roofs or BuiltInCategory.OST_GenericModel;
    }
    public bool AllowReference(Reference reference, XYZ position) => false;
}

/// <summary>Rotinas de interação com o usuário na vista ativa.</summary>
public static class Picking
{
    private const ObjectSnapTypes Snaps = ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Nearest
        | ObjectSnapTypes.Intersections | ObjectSnapTypes.Perpendicular | ObjectSnapTypes.Centers | ObjectSnapTypes.WorkPlaneGrid;

    /// <summary>Linhas pré-selecionadas ou escolhidas pelo usuário. Nulo = cancelado.</summary>
    public static List<CurveElement>? PickCurves(UIDocument uidoc, string prompt)
    {
        var pre = uidoc.Selection.GetElementIds().Select(id => uidoc.Document.GetElement(id)).Where(PathResolver.IsPathElement).Cast<CurveElement>().ToList();
        if (pre.Count > 0) return pre;
        try
        {
            var refs = uidoc.Selection.PickObjects(ObjectType.Element, new CurveSelectionFilter(), prompt);
            var res = refs.Select(r => uidoc.Document.GetElement(r)).OfType<CurveElement>().ToList();
            return res.Count > 0 ? res : null;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }

    public static XYZ? PickPoint(UIDocument uidoc, string prompt)
    {
        try
        {
            return uidoc.Selection.PickPoint(Snaps, prompt);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            throw new UserMessageException("A vista ativa não possui plano de trabalho. Use uma vista em planta (ou defina um plano de trabalho).");
        }
    }

    /// <summary>
    /// Captura uma polilinha por cliques, desenhando linhas provisórias a cada ponto. ESC encerra.
    /// Se <paramref name="keepAsModelLines"/>, as linhas ficam no modelo (estilo "SV - Eixo") e seus ids são retornados.
    /// </summary>
    public static (List<XYZ> Points, List<ElementId> Lines)? PickPolyline(UIDocument uidoc, string prompt, bool closed, bool keepAsModelLines)
    {
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;
        var pts = new List<XYZ>();
        var lines = new List<ElementId>();
        using var group = new TransactionGroup(doc, "SV - Desenhar caminho");
        group.Start();
        try
        {
            var styles = new StyleService(doc);
            while (true)
            {
                var msg = pts.Count == 0 ? $"{prompt}: clique o primeiro ponto (ESC cancela)" : $"{prompt}: próximo ponto ({pts.Count} pontos) – ESC para concluir";
                var p = PickPoint(uidoc, msg);
                if (p == null) break;
                if (pts.Count > 0 && p.DistanceTo(pts[^1]) < doc.Application.ShortCurveTolerance) continue;
                if (pts.Count > 0)
                {
                    p = new XYZ(p.X, p.Y, pts[0].Z);
                    using var t = new Transaction(doc, "SV - segmento");
                    t.Start();
                    lines.Add(CreateLine(doc, view, pts[^1], p, styles));
                    t.Commit();
                    uidoc.RefreshActiveView();
                }
                pts.Add(p);
            }
            var min = closed ? 3 : 2;
            if (pts.Count < min)
            {
                group.RollBack();
                return null;
            }
            if (closed && keepAsModelLines)
            {
                using var t = new Transaction(doc, "SV - fechar");
                t.Start();
                lines.Add(CreateLine(doc, view, pts[^1], pts[0], styles));
                t.Commit();
            }
            if (keepAsModelLines) group.Assimilate();
            else
            {
                group.RollBack();
                lines.Clear();
            }
            return (pts, lines);
        }
        catch
        {
            if (group.HasStarted() && !group.HasEnded()) group.RollBack();
            throw;
        }
    }

    /// <summary>
    /// Desenha o eixo de uma via nova por cliques: cada ponto passa por <paramref name="snap"/> (encaixe nas vias
    /// existentes) e as linhas provisórias mostram o traçado. ESC conclui. Devolve os pontos (pés) e a descrição do
    /// encaixe de cada um; nada fica no modelo.
    /// </summary>
    public static (List<XYZ> Points, List<string?> Info)? PickRoadAxis(UIDocument uidoc, Func<XYZ, (XYZ Point, string? Info)>? snap)
    {
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;
        var pts = new List<XYZ>();
        var info = new List<string?>();
        using var group = new TransactionGroup(doc, "SV - Desenhar via");
        group.Start();
        try
        {
            var styles = new StyleService(doc);
            while (true)
            {
                var last = info.Count > 0 && info[^1] != null ? $" – último ponto {info[^1]}" : "";
                var msg = pts.Count == 0
                    ? "Nova via: clique o primeiro ponto (sobre uma via existente para conectar) – ESC cancela"
                    : $"Nova via: próximo ponto ({pts.Count}){last} – ESC para concluir";
                var p = PickPoint(uidoc, msg);
                if (p == null) break;
                string? what = null;
                if (snap != null) (p, what) = snap(p);
                if (pts.Count > 0) p = new XYZ(p.X, p.Y, pts[0].Z);
                if (pts.Count > 0 && p.DistanceTo(pts[^1]) < doc.Application.ShortCurveTolerance) continue;
                if (pts.Count > 0)
                {
                    using var t = new Transaction(doc, "SV - segmento");
                    t.Start();
                    CreateLine(doc, view, pts[^1], p, styles);
                    t.Commit();
                    uidoc.RefreshActiveView();
                }
                pts.Add(p);
                info.Add(what);
            }
            group.RollBack();
            return pts.Count < 2 ? null : (pts, info);
        }
        catch
        {
            if (group.HasStarted() && !group.HasEnded()) group.RollBack();
            throw;
        }
    }

    /// <summary>Cria o eixo definitivo (retas e arcos de concordância) como linhas de modelo no estilo de eixo.</summary>
    public static List<ElementId> CreateAxis(Document doc, View view, IReadOnlyList<Core.Automation.RoadConnection.Piece> pieces, double zFeet)
    {
        var styles = new StyleService(doc);
        var ids = new List<ElementId>();
        XYZ P(Vec2 v) => new(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), zFeet);
        foreach (var pc in pieces)
        {
            Curve c = pc.Mid is { } m ? Arc.Create(P(pc.A), P(pc.B), P(m)) : Line.CreateBound(P(pc.A), P(pc.B));
            var sp = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, P(pc.A)));
            var mc = doc.Create.NewModelCurve(c, sp);
            try { mc.LineStyle = styles.AxisLineStyle(); } catch { /* estilo opcional */ }
            ids.Add(mc.Id);
        }
        return ids;
    }

    public static ElementId CreateLine(Document doc, View view, XYZ a, XYZ b, StyleService styles)
    {
        var line = Line.CreateBound(a, b);
        var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, a);
        var sp = SketchPlane.Create(doc, plane);
        var mc = doc.Create.NewModelCurve(line, sp);
        try { mc.LineStyle = styles.AxisLineStyle(); } catch { /* estilo opcional */ }
        return mc.Id;
    }

    /// <summary>Converte pontos do Revit para o núcleo (m) + elevação média (m).</summary>
    public static (List<Vec2> Points, double Z) ToCore(IEnumerable<XYZ> pts)
    {
        var list = pts.ToList();
        return (list.Select(UnitConv.ToVec2).ToList(), list.Count == 0 ? 0 : UnitConv.M(list.Average(p => p.Z)));
    }
}

/// <summary>Erro com mensagem destinada ao usuário (exibida sem pilha de chamadas).</summary>
public sealed class UserMessageException(string message) : Exception(message);
