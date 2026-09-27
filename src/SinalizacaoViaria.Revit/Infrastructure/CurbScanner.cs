using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>
/// Lê do projeto os meios-fios, pavimentos, sarjetas e calçadas perto de um ponto (vias, interseções, rotatórias, orelhas,
/// meios-fios avulsos) – para encaixar bocas de lobo e grelhas na guia e achar o nível do piso sob um clique.
/// </summary>
internal sealed class CurbScanner
{
    private readonly Document _doc;
    private readonly MarkingService _service;
    private readonly Dictionary<string, (MarkingGeometry Geo, double Z)> _cache = new();

    public CurbScanner(Document doc, View? view)
    {
        _doc = doc;
        _service = new MarkingService(doc, view);
    }

    private static bool Candidate(MarkingDefinition d) =>
        d is RoadPavementDefinition or IntersectionDefinition or RoundaboutDefinition or CulDeSacDefinition or CurbExtensionDefinition
            or SidewalkAreaDefinition or PlanterDefinition
        || d is LinearMarkingDefinition l && (l.Code.StartsWith("MEIO-FIO") || l.Code is "SARJETA" or "CALCADA" or "GRAMADO" or "PLATAFORMA" or "SARJETAO");

    /// <summary>Fontes (geometria com recortes, cota de base) num raio em torno do ponto.</summary>
    public List<CurbSource> Sources(Vec2 p, double radius = 40)
    {
        var res = new List<CurbSource>();
        foreach (var d in MarkingStorage.Definitions(_doc).Where(Candidate))
        {
            if (d.Path != null)
            {
                var axis = PathResolver.Resolve(_doc, d.Path)?.Main;
                if (axis != null && axis.Points.Count >= 2 && CurbFinder.Project(axis, p).Distance > radius) continue;
            }
            if (!_cache.TryGetValue(d.Id, out var g))
            {
                try
                {
                    var geo = _service.BuildGeometry(d, out var baseZ);
                    g = (geo, baseZ + d.Output.ElevationOffset);
                }
                catch (Exception ex)
                {
                    Log.Error($"CurbScanner {d.DisplayCode}", ex);
                    continue;
                }
                _cache[d.Id] = g;
            }
            if (g.Geo.Bounds is not { } b || p.X < b.Min.X - radius || p.X > b.Max.X + radius || p.Y < b.Min.Y - radius || p.Y > b.Max.Y + radius) continue;
            res.Add(new CurbSource(g.Geo, g.Z, d.Id, d is LinearMarkingDefinition lm && lm.Code.StartsWith("MEIO-FIO")));
        }
        return res;
    }

    /// <summary>Faces de meio-fio perto do ponto.</summary>
    public List<CurbFace> Faces(Vec2 p, double radius = 40) => CurbFinder.Faces(Sources(p, radius));

    /// <summary>Cota do topo do piso (calçada, pista, canteiro) sob o ponto – nulo se não houver piso do plugin.</summary>
    public double? SurfaceZ(Vec2 p)
    {
        double? best = null;
        foreach (var s in Sources(p, 15))
            foreach (var piece in s.Geometry.Pieces)
            {
                if (piece.Solid != null || piece.Profile != null || piece.Thickness < 0.004 || !piece.Shape.Contains(p)) continue;
                var z = s.BaseZ + piece.Elevation + piece.Thickness;
                if (best == null || z > best) best = z;
            }
        return best;
    }
}
