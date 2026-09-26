using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Caminho resolvido: uma ou mais polilinhas (m) e a elevação média (m).</summary>
public sealed record ResolvedPath(List<Polyline2> Chains, double Z, List<string> Warnings)
{
    public Polyline2? Main => Chains.Count > 0 ? Chains[0] : null;
}

/// <summary>Converte referências de caminho (linhas do Revit ou pontos) em polilinhas do núcleo.</summary>
public static class PathResolver
{
    public static ResolvedPath? Resolve(Document doc, PathReference? path)
    {
        if (path == null) return null;
        var warnings = new List<string>();

        if (path.IsAssociative)
        {
            var pieces = new List<IReadOnlyList<Vec2>>();
            double zSum = 0;
            int zCount = 0;
            for (int k = 0; k < path.ElementIds.Count; k++)
            {
                var uid = path.ElementIds[k];
                var curve = CurveOf(doc, uid);
                if (curve == null)
                {
                    // Referência perdida (linha apagada ou piso regenerado): usa o traçado guardado na criação.
                    if (path.Cache != null && k < path.Cache.Count && path.Cache[k].Count >= 2)
                    {
                        pieces.Add(path.Cache[k]);
                        zSum += path.Z; zCount++;
                        continue;
                    }
                    warnings.Add(PathReference.IsEdge(uid) ? "Uma das bordas de referência não existe mais." : "Uma das linhas de referência não existe mais.");
                    continue;
                }
                var pts = Tessellate(curve);
                if (pts.Count < 2) continue;
                pieces.Add(pts.Select(UnitConv.ToVec2).ToList());
                foreach (var p in pts) { zSum += UnitConv.M(p.Z); zCount++; }
            }
            if (pieces.Count == 0) return null;
            var chain = PathChainer.Chain(pieces, 0.01);
            if (!chain.AllConnected)
                warnings.Add($"As linhas selecionadas formam {chain.Chains.Count} trechos separados; cada trecho recebe o padrão de forma independente.");
            var polylines = chain.Chains
                .Select(c => { var closed = path.Closed || PathChainer.IsClosed(c, 0.01); return new Polyline2(PathReference.Smooth(c, path.SmoothRadius, closed), closed); })
                .Where(p => p.Length > 1e-4).ToList();
            return new ResolvedPath(polylines, zCount > 0 ? zSum / zCount : 0, warnings);
        }

        if (path.Points.Count >= 2)
            return new ResolvedPath(new List<Polyline2> { new(PathReference.Smooth(path.Points, path.SmoothRadius, path.Closed), path.Closed) }, path.Z, warnings);
        return null;
    }

    /// <summary>Discretiza a curva com resolução adequada a marcas viárias (flecha ~5 mm).</summary>
    public static List<XYZ> Tessellate(Curve curve)
    {
        if (curve is Line)
            return new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) };
        if (curve is Arc arc)
        {
            var r = UnitConv.M(arc.Radius);
            var sweep = Math.Abs(arc.GetEndParameter(1) - arc.GetEndParameter(0));
            if (!arc.IsBound) sweep = 2 * Math.PI;
            var n = CurveTools.SegmentsForArc(r, sweep, 0.005, 4);
            var res = new List<XYZ>(n + 1);
            for (int i = 0; i <= n; i++) res.Add(arc.Evaluate((double)i / n, true));
            return res;
        }
        return curve.Tessellate().ToList();
    }

    public static bool IsPathElement(Element e) => e is ModelCurve || e is DetailCurve;

    /// <summary>Curva de uma referência: linha de modelo/detalhe (UniqueId) ou aresta de elemento ("edge|" + referência estável).</summary>
    public static Curve? CurveOf(Document doc, string id)
    {
        if (!PathReference.IsEdge(id)) return (doc.GetElement(id) as CurveElement)?.GeometryCurve;
        try
        {
            var r = Reference.ParseFromStableRepresentation(doc, id.Substring(PathReference.EdgePrefix.Length));
            var e = doc.GetElement(r);
            var go = e?.GetGeometryObjectFromReference(r);
            var c = go switch { Edge edge => edge.AsCurve(), Curve cv => cv, _ => null };
            if (c == null) return null;
            // Famílias sem geometria própria (não cortadas/unidas): a aresta vem no sistema do tipo.
            if (e is FamilyInstance fi && !fi.HasModifiedGeometry() && fi.GetTransform() is { IsIdentity: false } tr)
                c = c.CreateTransformed(tr);
            return c;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Traçado (m) de cada referência – guardado no caminho para o caso de a referência deixar de existir.</summary>
    public static List<List<Vec2>> CacheOf(Document doc, IEnumerable<string> ids) =>
        ids.Select(id => CurveOf(doc, id) is { } c ? Tessellate(c).Select(UnitConv.ToVec2).ToList() : new List<Vec2>()).ToList();
}
