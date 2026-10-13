using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Parcelamento;

/// <summary>
/// Lote apoiado no terreno: contorno e superfície com pontos densos (borda a cada metro e grade interna), cada ponto na cota do
/// terreno – o lote acompanha as ondulações em vez de ficar plano. No Revit, a subdivisão do Toposolid faz isso nativamente;
/// aqui serve às imagens 3D e à verificação.
/// </summary>
public static class LotTerrain
{
    /// <summary>Contorno do lote no terreno: vértices e pontos a cada <paramref name="step"/> m.</summary>
    public static List<Vec3> Contorno3D(Polygon2 p, Func<Vec2, double> ground, double step = 1.0)
    {
        var ring = p.Outer.Append(p.Outer[0]).ToList();
        var dense = CurveTools.Densify(ring, step);
        if (dense.Count > 1 && dense[0].DistanceTo(dense[^1]) < 1e-9) dense.RemoveAt(dense.Count - 1);
        return dense.Select(v => Vec3.At(v, ground(v))).ToList();
    }

    /// <summary>
    /// Superfície do lote no terreno: triangulação dos pontos do contorno (a cada <paramref name="step"/> m) e de uma grade
    /// interna; só os triângulos dentro do lote.
    /// </summary>
    public static TerrainMesh Superficie(Polygon2 p, Func<Vec2, double> ground, double step = 1.0)
    {
        var pts = Contorno3D(p, ground, step);
        var (mn, mx) = p.Bounds;
        for (var x = Math.Floor(mn.X / step) * step; x <= mx.X; x += step)
            for (var y = Math.Floor(mn.Y / step) * step; y <= mx.Y; y += step)
            {
                var q = new Vec2(x, y);
                if (p.Contains(q) && p.DistanceTo(q) > step * 0.3) pts.Add(Vec3.At(q, ground(q)));
            }
        var mesh = new TerrainMesh();
        foreach (var (a, b, c) in Tin.Triangulate(pts))
        {
            var cen = (pts[a].XY + pts[b].XY + pts[c].XY) / 3;
            if (p.Contains(cen)) mesh.Add(pts[a], pts[b], pts[c]);
        }
        return mesh;
    }

    /// <summary>Maior distância vertical entre a superfície do lote e o terreno, medida em <paramref name="samples"/> pontos de cada triângulo.</summary>
    public static double DesvioMaximo(TerrainMesh lote, Func<Vec2, double> ground)
    {
        var worst = 0.0;
        foreach (var (a, b, c) in lote.Triangles)
        {
            var A = lote.Vertices[a];
            var B = lote.Vertices[b];
            var C = lote.Vertices[c];
            foreach (var (u, v) in new[] { (1 / 3.0, 1 / 3.0), (0.5, 0.0), (0.0, 0.5), (0.5, 0.5), (0.0, 0.0) })
            {
                var w = 1 - u - v;
                var p = A * w + B * u + C * v;
                worst = Math.Max(worst, Math.Abs(p.Z - ground(p.XY)));
            }
        }
        return worst;
    }
}
