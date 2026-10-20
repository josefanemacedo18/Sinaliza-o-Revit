using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Anel em planta (m) → contorno de retas do Revit para pisos e subdivisões do Toposolid que não passam pelo
/// <see cref="MarkingService"/> (lotes, áreas remanescentes, acabamentos do terreno). Pontos mais próximos que a tolerância
/// saem, retas quase alinhadas aos eixos ficam exatas (<see cref="AxisSnap"/>: sem o aviso "A linha no croqui está
/// ligeiramente fora da linha central") e o anel sempre fecha.
/// </summary>
internal static class PlanLoops
{
    public static CurveLoop? Lines(IReadOnlyList<Vec2> ring, double zFt, double tolFt)
    {
        var tolM = UnitConv.M(tolFt);
        var clean = Dedup(ring, tolM);
        if (clean.Count < 3) return null;
        // O alinhamento move cada vértice no máximo 0,5 mm: uma reta curta pode ficar abaixo da tolerância e sai de novo.
        var kept = Dedup(AxisSnap.SnapRing(clean), tolM);
        if (kept.Count < 3) return null;
        var pts = kept.Select(v => new XYZ(UnitConv.Ft(v.X), UnitConv.Ft(v.Y), zFt)).ToList();
        var loop = new CurveLoop();
        for (int i = 0; i < pts.Count; i++) loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
        return loop;
    }

    /// <summary>Sem pontos seguidos mais próximos que <paramref name="tol"/> (m), inclusive o último com o primeiro.</summary>
    private static List<Vec2> Dedup(IReadOnlyList<Vec2> ring, double tol)
    {
        var kept = new List<Vec2>();
        foreach (var p in ring)
            if (kept.Count == 0 || kept[^1].DistanceTo(p) > tol) kept.Add(p);
        while (kept.Count > 2 && kept[0].DistanceTo(kept[^1]) <= tol) kept.RemoveAt(kept.Count - 1);
        return kept;
    }
}
