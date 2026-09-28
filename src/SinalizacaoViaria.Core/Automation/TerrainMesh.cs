using System.IO.Compression;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Malha triangulada do terreno (topo do Toposolid, cotas absolutas em m) com índice espacial: cota em qualquer ponto,
/// sem depender de vistas 3D. Serializável (compactada) para guardar o "terreno original" dentro do projeto – a
/// terraplenagem sempre parte dele, então refazer ou apagar uma obra devolve o terreno natural.
/// </summary>
public sealed class TerrainMesh
{
    public List<Vec3> Vertices { get; } = new();
    public List<(int A, int B, int C)> Triangles { get; } = new();

    private const double Cell = 8;
    private Dictionary<(int, int), List<int>>? _grid;

    public int Count => Triangles.Count;

    public void Add(Vec3 a, Vec3 b, Vec3 c)
    {
        var i = Vertices.Count;
        Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
        Triangles.Add((i, i + 1, i + 2));
        _grid = null;
    }

    public (Vec2 Min, Vec2 Max)? Bounds
    {
        get
        {
            if (Vertices.Count == 0) return null;
            return (new Vec2(Vertices.Min(v => v.X), Vertices.Min(v => v.Y)), new Vec2(Vertices.Max(v => v.X), Vertices.Max(v => v.Y)));
        }
    }

    private static (int, int) K(double x, double y) => ((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell));

    private void Index()
    {
        if (_grid != null) return;
        _grid = new();
        for (int i = 0; i < Triangles.Count; i++)
        {
            var (a, b, c) = (Vertices[Triangles[i].A], Vertices[Triangles[i].B], Vertices[Triangles[i].C]);
            var (x0, y0) = K(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)));
            var (x1, y1) = K(Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 40000) continue;      // triângulo degenerado gigante
            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                {
                    if (!_grid.TryGetValue((x, y), out var l)) _grid[(x, y)] = l = new List<int>();
                    l.Add(i);
                }
        }
    }

    /// <summary>Cota do topo do terreno no ponto (a mais alta se houver sobreposição); nulo fora da malha.</summary>
    public double? Z(Vec2 p)
    {
        Index();
        if (!_grid!.TryGetValue(K(p.X, p.Y), out var cand)) return null;
        double? best = null;
        foreach (var i in cand)
        {
            var (a, b, c) = (Vertices[Triangles[i].A], Vertices[Triangles[i].B], Vertices[Triangles[i].C]);
            var d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(d) < 1e-12) continue;
            var l1 = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / d;
            var l2 = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / d;
            var l3 = 1 - l1 - l2;
            if (l1 < -1e-7 || l2 < -1e-7 || l3 < -1e-7) continue;
            var z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
            if (best == null || z > best) best = z;
        }
        return best;
    }

    /// <summary>Vértices distintos (5 cm) – os pontos do levantamento original.</summary>
    public List<Vec3> DistinctPoints()
    {
        var seen = new HashSet<(long, long)>();
        var res = new List<Vec3>();
        foreach (var v in Vertices)
            if (seen.Add(((long)Math.Round(v.X * 20), (long)Math.Round(v.Y * 20)))) res.Add(v);
        return res;
    }

    /// <summary>Base64 de float32 (x, y, z por vértice de cada triângulo) compactado com GZip.</summary>
    public string Serialize()
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new BinaryWriter(gz))
        {
            w.Write(1);                            // versão
            w.Write(Triangles.Count);
            // Origem para manter precisão em coordenadas grandes.
            var o = Vertices.Count > 0 ? Vertices[0] : new Vec3(0, 0, 0);
            w.Write(o.X); w.Write(o.Y); w.Write(o.Z);
            foreach (var (a, b, c) in Triangles)
                foreach (var v in new[] { Vertices[a], Vertices[b], Vertices[c] })
                {
                    w.Write((float)(v.X - o.X)); w.Write((float)(v.Y - o.Y)); w.Write((float)(v.Z - o.Z));
                }
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    public static TerrainMesh? Deserialize(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(data));
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var r = new BinaryReader(gz);
            if (r.ReadInt32() != 1) return null;
            var n = r.ReadInt32();
            var o = new Vec3(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
            var m = new TerrainMesh();
            Vec3 V() => new(o.X + r.ReadSingle(), o.Y + r.ReadSingle(), o.Z + r.ReadSingle());
            for (int i = 0; i < n; i++) m.Add(V(), V(), V());
            return m;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Terreno sintético (testes e prévias): malha regular sobre uma função de cota.</summary>
    public static TerrainMesh FromFunction(Vec2 min, Vec2 max, double step, Func<Vec2, double> z)
    {
        var m = new TerrainMesh();
        for (var x = min.X; x < max.X - 1e-9; x += step)
            for (var y = min.Y; y < max.Y - 1e-9; y += step)
            {
                Vec3 P(double px, double py) => new(px, py, z(new Vec2(px, py)));
                var (a, b, c, d) = (P(x, y), P(x + step, y), P(x + step, y + step), P(x, y + step));
                m.Add(a, b, c);
                m.Add(a, c, d);
            }
        return m;
    }
}

/// <summary>
/// Reconstrução do terreno: a partir do terreno original e da superfície de projeto de TODAS as obras que o moldam, calcula
/// o conjunto de pontos do Toposolid numa região – idempotente (regerar não acumula aterros) e reversível.
/// </summary>
public static class TerrainRebuild
{
    /// <summary>
    /// Pontos finais dentro de <paramref name="region"/>: pontos originais fora da área de projeto + pontos da superfície de
    /// projeto (já com a cota combinada). <paramref name="inRegion"/> diz se um ponto está na região a refazer.
    /// </summary>
    public static List<Vec3> Points(TerrainMesh original, GradingResult design, Func<Vec2, bool> inRegion)
    {
        var res = new List<Vec3>();
        var seen = new HashSet<(long, long)>();
        void Add(Vec3 p)
        {
            if (double.IsNaN(p.Z)) return;
            if (seen.Add(((long)Math.Round(p.X * 20), (long)Math.Round(p.Y * 20)))) res.Add(p);
        }
        foreach (var p0 in design.Points)
        {
            if (original.Z(p0.XY) == null) continue;               // fora do terreno
            Add(p0 with { Z = design.DesignZ(p0.XY) ?? p0.Z });
        }
        foreach (var v in original.DistinctPoints())
        {
            if (!inRegion(v.XY)) continue;
            if (design.DesignZ(v.XY) != null) continue;          // substituído pelo projeto
            Add(v);
        }
        return res;
    }

    /// <summary>
    /// Simula o Toposolid depois da terraplenagem: os pontos originais fora da região ficam, os de dentro são trocados pelos
    /// pontos de <see cref="Points"/>, e tudo é triangulado (Delaunay) como o Revit faz. Usado em prévias e testes.
    /// </summary>
    public static TerrainMesh Simulate(TerrainMesh original, GradingResult design, double margin = 0.5)
    {
        var region = design.Footprint.Count == 0 ? new List<Polygon2>() : PolygonOps.Union(design.Footprint).Where(f => f.Area > 0.5).ToList();
        var inRegion = Mask(region, margin);
        var pts = original.DistinctPoints().Where(v => !inRegion(v.XY)).ToList();
        pts.AddRange(Points(original, design, inRegion));
        return Tin.Mesh(pts);
    }

    /// <summary>Máscara rápida (grade de 1 m) de um conjunto de polígonos.</summary>
    public static Func<Vec2, bool> Mask(IEnumerable<Polygon2> polys, double margin = 1.0)
    {
        var list = polys.Where(p => p.Area > 1e-6).ToList();
        var grid = new Dictionary<(int, int), List<Polygon2>>();
        const double cell = 10;
        foreach (var p in list)
        {
            var (mn, mx) = p.Bounds;
            for (var x = (int)Math.Floor((mn.X - margin) / cell); x <= (int)Math.Floor((mx.X + margin) / cell); x++)
                for (var y = (int)Math.Floor((mn.Y - margin) / cell); y <= (int)Math.Floor((mx.Y + margin) / cell); y++)
                {
                    if (!grid.TryGetValue((x, y), out var l)) grid[(x, y)] = l = new List<Polygon2>();
                    l.Add(p);
                }
        }
        return q =>
        {
            if (!grid.TryGetValue(((int)Math.Floor(q.X / cell), (int)Math.Floor(q.Y / cell)), out var l)) return false;
            foreach (var p in l)
            {
                if (p.Contains(q)) return true;
                if (margin > 0 && p.DistanceTo(q) <= margin) return true;
            }
            return false;
        };
    }
}
