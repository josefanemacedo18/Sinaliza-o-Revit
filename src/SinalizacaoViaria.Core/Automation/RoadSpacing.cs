using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Referência da distância entre duas vias.</summary>
public enum ReferenciaDistancia
{
    /// <summary>Eixo a eixo.</summary>
    Eixos,
    /// <summary>Face do meio-fio a face do meio-fio (lados que se olham).</summary>
    MeiosFios,
    /// <summary>Bordo externo a bordo externo (limite das calçadas / alinhamento).</summary>
    Bordos,
}

/// <summary>
/// Distância entre a via A e a via B medida numa seção perpendicular ao eixo de A (no ponto de A mais próximo do clique em
/// B). <see cref="Normal"/> aponta de A para B; as larguras de B são medidas ao longo dessa normal (divididas pelo cosseno
/// do ângulo entre os eixos quando as vias não são paralelas).
/// </summary>
public sealed record DistanciaVias(Vec2 PontoA, Vec2 PontoB, Vec2 Normal, double Eixos, double MeiosFios, double Bordos, double AnguloGraus)
{
    public double Valor(ReferenciaDistancia r) => r switch
    {
        ReferenciaDistancia.MeiosFios => MeiosFios,
        ReferenciaDistancia.Bordos => Bordos,
        _ => Eixos,
    };

    /// <summary>
    /// Deslocamento da via B (inteira, como corpo rígido) para a distância <paramref name="r"/> passar a
    /// <paramref name="nova"/>: ao longo da normal de A, que muda as três medidas pelo mesmo valor.
    /// </summary>
    public Vec2 Deslocamento(ReferenciaDistancia r, double nova) => Normal * (nova - Valor(r));
}

/// <summary>
/// Distância entre vias e "Mover via": mede, escolhe o que se move junto com a via e desloca os dados por ponto. O eixo
/// continua sendo a fonte única – a via, as extensões, o piso tátil e as interseções são refeitos a partir dele.
/// </summary>
public static class RoadSpacing
{
    /// <summary>Ângulo máximo (°) entre os eixos para medir a distância (acima disso as vias se cruzam, não são "vizinhas").</summary>
    public const double AnguloMaximo = 75;

    /// <summary>
    /// Mede a distância de A a B na seção de A mais próxima de <paramref name="nearB"/> (um ponto sobre B). Nulo quando a
    /// normal de A não encontra o eixo de B ou os eixos formam mais de <see cref="AnguloMaximo"/>.
    /// </summary>
    public static DistanciaVias? Measure(IntersectionRoad a, IntersectionRoad b, Vec2 nearB)
    {
        if (a.Axis.Points.Count < 2 || b.Axis.Points.Count < 2) return null;
        var (sA, signed) = a.Axis.Project(nearB);
        var pA = a.Axis.PointAt(sA);
        var tA = a.Axis.TangentAt(sA).Normalized();
        var left = signed >= 0;
        var n = left ? tA.PerpLeft : tA.PerpRight;
        if (RayHit(b.Axis, pA, n, nearB) is not { } pB) return null;
        var tB = b.Axis.TangentAt(b.Axis.Project(pB).Station).Normalized();
        var cos = Math.Abs(tA.Dot(tB));
        var ang = Math.Acos(Math.Clamp(cos, 0, 1)) * 180 / Math.PI;
        if (ang > AnguloMaximo) return null;
        var la = a.LocalAt(pA).Def;
        var lb = b.LocalAt(pB).Def;
        // Lado de B voltado para A.
        var bLeft = b.Axis.Project(pA).Signed >= 0;
        var axes = pA.DistanceTo(pB);
        var curbA = left ? la.LeftWidth : la.RightWidth;
        var lotA = left ? la.TotalLeft : la.TotalRight;
        var curbB = (bLeft ? lb.LeftWidth : lb.RightWidth) / cos;
        var lotB = (bLeft ? lb.TotalLeft : lb.TotalRight) / cos;
        return new DistanciaVias(pA, pB, n, axes, axes - curbA - curbB, axes - lotA - lotB, ang);
    }

    /// <summary>Primeiro encontro (t &gt; 0) da semirreta p0 + t·dir com o eixo; havendo vários, o mais próximo de <paramref name="near"/>.</summary>
    private static Vec2? RayHit(Polyline2 axis, Vec2 p0, Vec2 dir, Vec2 near)
    {
        Vec2? best = null;
        var bestD = double.MaxValue;
        for (int i = 0; i + 1 < axis.Points.Count; i++)
        {
            var q0 = axis.Points[i];
            var e = axis.Points[i + 1] - q0;
            var den = dir.Cross(e);
            if (Math.Abs(den) < 1e-12) continue;
            var w = q0 - p0;
            var t = w.Cross(e) / den;
            var u = w.Cross(dir) / den;
            if (t < -1e-9 || u < -1e-9 || u > 1 + 1e-9) continue;
            var hit = p0 + dir * t;
            var d = hit.DistanceTo(near);
            if (d < bestD) { bestD = d; best = hit; }
        }
        return best;
    }

    /// <summary>
    /// Marcas que se movem com a via <paramref name="pav"/>: todo o grupo dela, cul-de-sacs ligados a ela, anotações que
    /// apontam para essas marcas e elementos avulsos posicionados por ponto (placas, símbolos, mobiliário, drenagem) que
    /// estão sobre a faixa da via e fora da faixa das outras vias. Interseções, rotatórias e os filhos delas ficam de fora
    /// (são refeitos no novo nó).
    /// </summary>
    public static List<MarkingDefinition> Members(IReadOnlyCollection<MarkingDefinition> all, RoadPavementDefinition pav, Polyline2 axis,
        IReadOnlyList<IntersectionRoad> roads)
    {
        var res = new List<MarkingDefinition>();
        var ids = new HashSet<string>();
        void Add(MarkingDefinition d) { if (ids.Add(d.Id)) res.Add(d); }
        var children = all.OfType<IntersectionDefinition>().SelectMany(i => i.ChildIds)
            .Concat(all.OfType<RoundaboutDefinition>().SelectMany(r => r.ChildIds)).ToHashSet();
        if (pav.GroupId != null)
            foreach (var d in all.Where(d => d.GroupId == pav.GroupId)) Add(d);
        else Add(pav);
        foreach (var c in all.OfType<CulDeSacDefinition>().Where(c => c.RoadId == pav.Id)) Add(c);
        var others = roads.Where(r => r.Def.Id != pav.Id && (pav.GroupId == null || r.Def.GroupId != pav.GroupId)).ToList();
        foreach (var d in all)
        {
            if (ids.Contains(d.Id) || d.GroupId != null || children.Contains(d.Id)) continue;
            if (d is IntersectionDefinition or RoundaboutDefinition or InterchangeDefinition or CulDeSacDefinition or IAnnotationDefinition) continue;
            if (d.Path is { IsAssociative: true }) continue;   // segue as próprias linhas de referência
            var p = d is DrainageDefinition dr && !dr.IsLinear ? dr.Position : AutoSignage.AnchorOf(d);
            if (p is not { } at || !OnRoad(pav, axis, at, 1.0)) continue;
            if (others.Any(r => OnRoad(r.Def, r.Axis, at, 0.0))) continue;
            Add(d);
        }
        // Anotações em planta das marcas que se movem (chamadas, detalhes de placa).
        foreach (var d in all.OfType<LabelDefinition>().Where(l => ids.Contains(l.MarkingTargetId)).ToList()) Add(d);
        return res;
    }

    /// <summary>O ponto está na faixa da via (do bordo esquerdo ao direito + <paramref name="margin"/>, entre as pontas).</summary>
    public static bool OnRoad(RoadPavementDefinition pav, Polyline2 axis, Vec2 p, double margin)
    {
        if (axis.Points.Count < 2) return false;
        var (s, off) = axis.Project(p);
        if (s <= 1e-6 || s >= axis.Length - 1e-6) return false;
        var local = pav.HasEdgeVariation ? pav.Local(s, axis) : pav;
        return off >= 0 ? off <= local.TotalLeft + margin : -off <= local.TotalRight + margin;
    }

    // Vetores (direções) e medidas em mm de papel não são posições; o canto desenhado é relido da vista.
    private static readonly HashSet<string> NotPositions = new(StringComparer.Ordinal)
    {
        "Direction", "Along", "ReferenceDirection", "RailDirection", "OffsetMm", "AnchorMm", "ElbowsMm", "GroundLine", "DrawnAnchor",
    };

    /// <summary>
    /// Desloca todas as posições guardadas nas marcas (pontos dos caminhos por pontos, traçados guardados das referências,
    /// pontos de inserção, âncoras das estacas, recortes...). Os caminhos associativos são deslocados movendo as linhas.
    /// </summary>
    public static void Translate(IEnumerable<MarkingDefinition> defs, Vec2 delta)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var d in defs) Walk(d, delta, seen);
    }

    private static void Walk(object o, Vec2 d, HashSet<object> seen)
    {
        if (!seen.Add(o)) return;
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || NotPositions.Contains(p.Name) || p.IsDefined(typeof(JsonIgnoreAttribute))) continue;
            var t = p.PropertyType;
            if (t == typeof(Vec2))
            {
                if (p.CanWrite && p.GetSetMethod() != null) p.SetValue(o, (Vec2)p.GetValue(o)! + d);
                continue;
            }
            if (t == typeof(Vec2?))
            {
                if (p.CanWrite && p.GetSetMethod() != null && p.GetValue(o) is Vec2 v) p.SetValue(o, v + d);
                continue;
            }
            if (t.IsValueType || t == typeof(string)) continue;
            object? val;
            try { val = p.GetValue(o); }
            catch { continue; }
            if (val != null) Visit(val, d, seen);
        }
    }

    private static void Visit(object val, Vec2 d, HashSet<object> seen)
    {
        switch (val)
        {
            case List<Vec2> pts:
                if (!seen.Add(pts)) return;
                for (int i = 0; i < pts.Count; i++) pts[i] += d;
                return;
            case string:
                return;
            case IDictionary:
                return;
            case IList list:
                if (!seen.Add(list)) return;
                foreach (var item in list)
                    if (item != null && !item.GetType().IsValueType) Visit(item, d, seen);
                return;
        }
        if (val.GetType().Namespace?.StartsWith("SinalizacaoViaria.Core", StringComparison.Ordinal) == true && val.GetType().IsClass)
            Walk(val, d, seen);
    }

    /// <summary>
    /// Nós das interseções que envolvem as vias movidas: cada uma vai para o cruzamento das mesmas vias, depois da mudança,
    /// mais próximo do nó antigo deslocado. Devolve as interseções alteradas.
    /// </summary>
    public static List<IntersectionDefinition> FollowNodes(IEnumerable<IntersectionDefinition> intersections, IReadOnlyList<IntersectionRoad> after,
        ISet<string> movedRoadIds, Vec2 delta)
    {
        var res = new List<IntersectionDefinition>();
        var nodes = IntersectionGenerator.FindNodes(after);
        foreach (var it in intersections.Where(i => i.RoadIds.Any(movedRoadIds.Contains)))
        {
            var present = it.RoadIds.Where(id => after.Any(r => r.Def.Id == id)).ToList();
            var want = it.Node + delta;
            var best = nodes.Where(n => present.All(id => n.Roads.Any(k => after[k].Def.Id == id)))
                .OrderBy(n => n.Node.DistanceTo(want)).Select(n => (Vec2?)n.Node).FirstOrDefault();
            if (best is not { } node) continue;
            it.Node = node;
            res.Add(it);
        }
        return res;
    }
}
