using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Edita as pontas do eixo de uma via (linhas de modelo/detalhe): usado pelo "ímã" de conexão.</summary>
public static class AxisEditor
{
    /// <summary>
    /// Move a ponta do eixo que está em <paramref name="oldEnd"/> para <paramref name="target"/> (mantém a cota).
    /// Retas e arcos são refeitos; outras curvas não são alteradas. Exige transação aberta.
    /// </summary>
    public static bool MoveEnd(Document doc, PathReference path, Vec2 oldEnd, Vec2 target)
    {
        if (!path.IsAssociative) return false;
        foreach (var uid in path.ElementIds)
        {
            if (doc.GetElement(uid) is not CurveElement ce || ce.GeometryCurve is not { IsBound: true } curve) continue;
            for (int k = 0; k < 2; k++)
            {
                var p = curve.GetEndPoint(k);
                if (UnitConv.ToVec2(p).DistanceTo(oldEnd) > 0.02) continue;
                var np = new XYZ(UnitConv.Ft(target.X), UnitConv.Ft(target.Y), p.Z);
                var other = curve.GetEndPoint(1 - k);
                if (np.DistanceTo(other) < doc.Application.ShortCurveTolerance * 4) return false;
                Curve? nc = curve switch
                {
                    Line => k == 0 ? Line.CreateBound(np, other) : Line.CreateBound(other, np),
                    Arc arc => k == 0 ? Arc.Create(np, other, arc.Evaluate(0.5, true)) : Arc.Create(other, np, arc.Evaluate(0.5, true)),
                    _ => null,
                };
                if (nc == null) return false;
                try
                {
                    ce.SetGeometryCurve(nc, true);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("Mover ponta do eixo", ex);
                    return false;
                }
            }
        }
        return false;
    }
}
