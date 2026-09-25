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
            foreach (var uid in path.ElementIds)
            {
                if (doc.GetElement(uid) is not CurveElement ce) { warnings.Add("Uma das linhas de referência não existe mais."); continue; }
                var curve = ce.GeometryCurve;
                if (curve == null) continue;
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
                .Select(c => new Polyline2(c, path.Closed || PathChainer.IsClosed(c, 0.01)))
                .Where(p => p.Length > 1e-4).ToList();
            return new ResolvedPath(polylines, zCount > 0 ? zSum / zCount : 0, warnings);
        }

        if (path.Points.Count >= 2)
            return new ResolvedPath(new List<Polyline2> { new(path.Points, path.Closed) }, path.Z, warnings);
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
}
