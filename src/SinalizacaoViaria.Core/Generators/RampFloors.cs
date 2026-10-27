using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>
/// Parte de uma rampa como Piso do Revit (pedido de 2026-10-27, item 2): contorno em planta, cota do topo em cada vértice do
/// contorno (m, relativa à base da marca), fundo plano e o plano ajustado ao topo com o desvio dele.
/// </summary>
public sealed record RampFloorPart(Polygon2 Shape, IReadOnlyList<Vec3> Top, double Bottom, MarkingColor Color, Plane3 Plane, double Deviation)
{
    /// <summary>Peça (corpo sólido) da geometria que a parte substitui.</summary>
    public MarkingPiece? Piece { get; init; }

    /// <summary>Cota do topo no ponto <paramref name="p"/>: a do vértice do contorno a até 2 cm dele, senão a do plano ajustado.</summary>
    public double TopAt(Vec2 p)
    {
        var best = Top.OrderBy(v => v.XY.DistanceTo(p)).FirstOrDefault();
        return Top.Count > 0 && best.XY.DistanceTo(p) <= 0.02 ? best.Z : Plane.Z(p);
    }

    /// <summary>Topo mais alto (m).</summary>
    public double MaxTop => Top.Count == 0 ? Bottom : Top.Max(v => v.Z);

    /// <summary>Espessura do tipo de piso: do fundo ao topo mais alto, em centímetros inteiros (poucos tipos no projeto).</summary>
    public double TypeThickness => Math.Max(0.02, Math.Ceiling((MaxTop - Bottom) * 100 - 1e-6) / 100);
}

/// <summary>
/// Rampas como Pisos: cada corpo de concreto da rampa (rampa, abas, platô e rampas laterais do rebaixamento total, patamar,
/// cantos nivelados) tem topo plano e base plana – vira um piso de fundo plano e topo inclinado (camada de espessura variável no
/// Revit: a edição de forma inclina só o topo). O fundo fica <see cref="InfraRoads.FolgaSobRampas"/> abaixo da base do corpo,
/// sobre a plataforma que a terraplenagem deixa sob a rampa (Etapa 15) – a espessura nunca chega a zero no pé da rampa. As
/// placas táteis continuam sólidos sobre o piso.
/// </summary>
public static class RampFloors
{
    /// <summary>Corpos que viram piso: os sólidos poliédricos da cor da rampa.</summary>
    public static bool IsBody(RampDefinition d, MarkingPiece p) => p.Solid != null && p.Color == d.RampColor && p.Shape.Area > 0.005;

    public static List<RampFloorPart> Parts(RampDefinition d, MarkingGeometry geo, double below = InfraRoads.FolgaSobRampas)
    {
        var res = new List<RampFloorPart>();
        foreach (var p in geo.Pieces.Where(x => IsBody(d, x)))
        {
            var poly = p.Solid!;
            var verts = poly.Faces.SelectMany(f => f).ToList();
            var ring = p.Shape.Outer;
            if (ring.Count < 3) continue;
            // Topo de cada vértice do contorno: o maior z da coluna do vértice (o corpo é um prisma de base plana).
            var top = new List<Vec3>();
            foreach (var v in ring)
            {
                var col = verts.Where(q => q.XY.DistanceTo(v) < 1e-6).ToList();
                if (col.Count == 0) { top.Clear(); break; }
                top.Add(Vec3.At(v, col.Max(q => q.Z)));
            }
            if (top.Count < 3) continue;
            var (plane, dev) = FloorPlanes.Fit(top);
            res.Add(new RampFloorPart(p.Shape, top, poly.MinZ - below, p.Color, plane, dev) { Piece = p });
        }
        return res;
    }
}
