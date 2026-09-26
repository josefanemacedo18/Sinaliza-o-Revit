using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Como a via nova se liga às vias existentes onde elas se encontram.</summary>
public enum TipoConexao
{
    /// <summary>Interseção (tipos e controle da ferramenta Interseção).</summary>
    Intersecao,
    Rotatoria,
    /// <summary>Não ajusta (as vias ficam sobrepostas).</summary>
    Nenhuma,
}

/// <summary>Tratamento das pontas da via que não se ligam a outra via.</summary>
public enum FimLivre
{
    Nenhum,
    CulDeSac,
}

/// <summary>Resultado do encaixe de um ponto clicado nas vias existentes.</summary>
public enum TipoEncaixe { Livre, PontaDeVia, EixoDeVia, Rotatoria }

public readonly record struct Snap(Vec2 Point, TipoEncaixe Kind, int Road)
{
    public string Describe => Kind switch
    {
        TipoEncaixe.PontaDeVia => "ligado à ponta de uma via (continuação)",
        TipoEncaixe.EixoDeVia => "ligado ao eixo de uma via (entroncamento / cruzamento)",
        TipoEncaixe.Rotatoria => "ligado ao centro de uma rotatória (novo ramo)",
        _ => "livre",
    };
}

/// <summary>
/// Conexão "natural" de vias (como no InfraWorks): o eixo desenhado se encaixa nas pontas e nos eixos das vias
/// existentes, as curvas horizontais são concordadas com raio e as pontas livres podem receber cul-de-sac.
/// </summary>
public static class RoadConnection
{
    /// <summary>
    /// Encaixa o ponto: na ponta de uma via (até <paramref name="endTolerance"/> m), senão no eixo de uma via quando o
    /// clique cai sobre a pista/calçada dela.
    /// </summary>
    public static Snap SnapPoint(Vec2 p, IReadOnlyList<IntersectionRoad> roads, double endTolerance = 4.0,
        IReadOnlyList<(Vec2 Center, double Radius)>? roundabouts = null)
    {
        if (roundabouts != null)
            foreach (var (c, r) in roundabouts)
                if (c.DistanceTo(p) <= r) return new Snap(c, TipoEncaixe.Rotatoria, -1);
        var bestEnd = (d: double.MaxValue, p: p, i: -1);
        for (int i = 0; i < roads.Count; i++)
            foreach (var e in new[] { roads[i].Axis.Points[0], roads[i].Axis.Points[^1] })
            {
                var dd = e.DistanceTo(p);
                var tol = Math.Max(endTolerance, Math.Min(roads[i].Def.RightWidth, roads[i].Def.LeftWidth));
                if (dd <= tol && dd < bestEnd.d) bestEnd = (dd, e, i);
            }
        if (bestEnd.i >= 0) return new Snap(bestEnd.p, TipoEncaixe.PontaDeVia, bestEnd.i);
        var bestAxis = (d: double.MaxValue, p: p, i: -1);
        for (int i = 0; i < roads.Count; i++)
        {
            var (_, dist, q) = IntersectionGenerator.Project(roads[i].Axis, p);
            var half = Math.Max(roads[i].Def.TotalLeft, roads[i].Def.TotalRight);
            if (dist <= half && dist < bestAxis.d) bestAxis = (dist, q, i);
        }
        return bestAxis.i >= 0 ? new Snap(bestAxis.p, TipoEncaixe.EixoDeVia, bestAxis.i) : new Snap(p, TipoEncaixe.Livre, -1);
    }

    /// <summary>Trecho do eixo: reta ou arco (início, meio, fim).</summary>
    public sealed record Piece(Vec2 A, Vec2 B, Vec2? Mid)
    {
        public bool IsArc => Mid != null;
    }

    /// <summary>
    /// Concorda os vértices da poligonal com arcos de raio <paramref name="radius"/> (reduzido onde as tangentes não
    /// cabem). Devolve retas e arcos na ordem do desenho.
    /// </summary>
    public static List<Piece> Fillet(IReadOnlyList<Vec2> pts, double radius)
    {
        var res = new List<Piece>();
        if (pts.Count < 2) return res;
        if (pts.Count == 2 || radius < 0.5)
        {
            for (int i = 0; i + 1 < pts.Count; i++) res.Add(new Piece(pts[i], pts[i + 1], null));
            return res;
        }
        var cur = pts[0];
        for (int i = 1; i + 1 < pts.Count; i++)
        {
            var p0 = pts[i - 1];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var u = (p0 - p1).Normalized();
            var v = (p2 - p1).Normalized();
            var ang = Math.Acos(Math.Clamp(u.Dot(v), -1, 1));        // ângulo interno no vértice
            if (ang > Math.PI - 0.01 || ang < 0.01)
            {
                res.Add(new Piece(cur, p1, null));
                cur = p1;
                continue;
            }
            // Tangente disponível: metade de cada segmento adjacente (o 1º e o último podem ser usados inteiros).
            var avail0 = p0.DistanceTo(p1) * (i == 1 ? 1.0 : 0.5);
            var avail1 = p2.DistanceTo(p1) * (i + 1 == pts.Count - 1 ? 1.0 : 0.5);
            var tanHalf = Math.Tan(ang / 2);
            var T = Math.Min(Math.Min(radius / tanHalf, avail0), Math.Min(avail1, cur.DistanceTo(p1)));
            if (T < 0.2)
            {
                res.Add(new Piece(cur, p1, null));
                cur = p1;
                continue;
            }
            var r = T * tanHalf;
            var t0 = p1 + u * T;
            var t1 = p1 + v * T;
            var bis = (u + v).Normalized();
            var c = p1 + bis * (r / Math.Sin(ang / 2));
            var mid = c + (p1 - c).Normalized() * r;
            if (cur.DistanceTo(t0) > 0.01) res.Add(new Piece(cur, t0, null));
            res.Add(new Piece(t0, t1, mid));
            cur = t1;
        }
        if (cur.DistanceTo(pts[^1]) > 0.01) res.Add(new Piece(cur, pts[^1], null));
        return res;
    }

    /// <summary>Pontos do eixo concordado (arcos discretizados) – para prévias e testes.</summary>
    public static List<Vec2> Densify(IReadOnlyList<Piece> pieces, double step = 1.0)
    {
        var pts = new List<Vec2>();
        foreach (var pc in pieces)
        {
            if (pts.Count == 0) pts.Add(pc.A);
            if (pc.Mid is not { } m) { pts.Add(pc.B); continue; }
            pts.AddRange(ArcThrough(pc.A, m, pc.B).Skip(1));
        }
        return pts;
    }

    /// <summary>Arco pelos três pontos (discretizado).</summary>
    public static List<Vec2> ArcThrough(Vec2 a, Vec2 m, Vec2 b)
    {
        var d = 2 * (a.X * (m.Y - b.Y) + m.X * (b.Y - a.Y) + b.X * (a.Y - m.Y));
        if (Math.Abs(d) < 1e-9) return new List<Vec2> { a, b };
        double Sq(Vec2 v) => v.X * v.X + v.Y * v.Y;
        var c = new Vec2((Sq(a) * (m.Y - b.Y) + Sq(m) * (b.Y - a.Y) + Sq(b) * (a.Y - m.Y)) / d,
                         (Sq(a) * (b.X - m.X) + Sq(m) * (a.X - b.X) + Sq(b) * (m.X - a.X)) / d);
        var r = c.DistanceTo(a);
        var a0 = Math.Atan2(a.Y - c.Y, a.X - c.X);
        var am = Math.Atan2(m.Y - c.Y, m.X - c.X);
        var a1 = Math.Atan2(b.Y - c.Y, b.X - c.X);
        double Norm(double x) { while (x < 0) x += 2 * Math.PI; while (x >= 2 * Math.PI) x -= 2 * Math.PI; return x; }
        var sweep = Norm(a1 - a0);
        if (Norm(am - a0) > sweep) sweep -= 2 * Math.PI;      // sentido horário
        return CurveTools.Arc(c, r, a0, sweep, 0.01);
    }

    /// <summary>Pontas da via (início/fim) que não tocam outra via.</summary>
    public static List<(bool AtEnd, Vec2 Point, Vec2 Outward)> FreeEnds(IntersectionRoad road, IReadOnlyList<IntersectionRoad> others)
    {
        var res = new List<(bool, Vec2, Vec2)>();
        var ax = road.Axis;
        foreach (var atEnd in new[] { false, true })
        {
            var e = atEnd ? ax.Points[^1] : ax.Points[0];
            var outward = atEnd ? ax.TangentAt(ax.Length) : -ax.TangentAt(0);
            var touches = others.Any(o =>
            {
                var (_, dist, _) = IntersectionGenerator.Project(o.Axis, e);
                return dist <= Math.Max(o.Def.TotalLeft, o.Def.TotalRight) + 2;
            });
            if (!touches) res.Add((atEnd, e, outward));
        }
        return res;
    }

    /// <summary>
    /// Ajusta um cul-de-sac à ponta da via: largura, calçada e hierarquia da via; o balão fica centrado na ponta do
    /// eixo e a via é recortada no trecho do balão. Devolve o recorte a aplicar nas marcas da via.
    /// </summary>
    public static List<Polygon2> FitCulDeSac(CulDeSacDefinition c, IntersectionRoad road, double z)
    {
        var r = road.Def;
        var ax = road.Axis;
        c.RoadWidth = r.RightWidth + r.LeftWidth;
        var walk = new[] { r.RightSidewalk, r.LeftSidewalk }.Where(w => w > 0.05).DefaultIfEmpty(0).Max();
        c.SidewalkWidth = walk;
        c.CurbWidth = r.CurbWidth;
        c.Height = r.CurbHeight;
        c.Pavement = r.Material != TipoPavimento.Nenhum;
        c.PavementThickness = r.ActualThickness;
        c.Hierarchy = r.Hierarchy;
        c.Output = r.Output.Clone();
        var hw = c.RoadWidth / 2;
        var rb = Math.Max(hw + 0.5, c.BulbRadius);
        var lr = c.Type switch
        {
            TipoCulDeSac.Martelo or TipoCulDeSac.EmLEsquerda or TipoCulDeSac.EmLDireita or TipoCulDeSac.EmY => Math.Max(8, hw + c.TransitionRadius + 2),
            _ => rb + Math.Max(0, c.TransitionRadius) + 2,
        };
        lr = Math.Min(lr, ax.Length * 0.8);
        var L = ax.Length;
        var sEnd = c.AtRoadEnd ? L : 0;
        var sStart = c.AtRoadEnd ? L - lr : lr;
        var a = ax.PointAt(sStart);
        var b = ax.PointAt(sEnd);
        c.PathRef = PathReference.FromPoints(new[] { a, b }, z);
        // Eixo pode ser curvo: o balão usa a corda a→b; a via é recortada do início do balão até a ponta.
        var sub = new Polyline2(ax.SubPoints(Math.Min(sStart, sEnd), Math.Max(sStart, sEnd)));
        var cut = sub.Points.Count >= 2 ? RoadGenerator.Band(sub, -r.TotalRight - 0.5, r.TotalLeft + 0.5) : new List<Polygon2>();
        // Prolonga um pouco além da ponta (peças que terminam exatamente nela).
        var outward = c.AtRoadEnd ? ax.TangentAt(L) : -ax.TangentAt(0);
        cut.AddRange(PolygonOps.Strip(new[] { b - outward * 0.5, b + outward * 2 }, r.TotalLeft + r.TotalRight + 1));
        return PolygonOps.Union(cut);
    }

    /// <summary>Encaixe de uma ponta de eixo: novo ponto e o que foi encontrado.</summary>
    public readonly record struct EndMagnet(Vec2 Point, TipoEncaixe Kind);

    /// <summary>
    /// "Ímã" das pontas do eixo (como no InfraWorks): a ponta solta perto de outra via vai para o eixo dela – ao longo
    /// da própria direção, mantendo o ângulo e aparando a sobra que passou do eixo –, para a ponta dela (continuação)
    /// ou para o centro de uma rotatória. Devolve o novo ponto de cada ponta (nulo = sem alteração).
    /// </summary>
    public static (EndMagnet? Start, EndMagnet? End) MagnetEnds(Polyline2 axis, IReadOnlyList<IntersectionRoad> others,
        IReadOnlyList<(Vec2 Center, double Radius)>? roundabouts = null, double endTolerance = 6.0)
    {
        if (axis.Points.Count < 2 || axis.Length < 5) return (null, null);
        EndMagnet? One(bool atEnd)
        {
            var e = atEnd ? axis.Points[^1] : axis.Points[0];
            var prev = atEnd ? axis.PointAt(Math.Max(0, axis.Length - Math.Min(5, axis.Length / 2))) : axis.PointAt(Math.Min(axis.Length, Math.Min(5, axis.Length / 2)));
            var dir = (e - prev).Normalized();
            if (roundabouts != null)
                foreach (var (c, r) in roundabouts)
                    if (c.DistanceTo(e) <= r + 2) return c.DistanceTo(e) < 0.01 ? null : new EndMagnet(c, TipoEncaixe.Rotatoria);
            // Ponta de outra via: continuação.
            var bestEnd = (d: double.MaxValue, p: e);
            foreach (var o in others)
                foreach (var oe in new[] { o.Axis.Points[0], o.Axis.Points[^1] })
                {
                    var dd = oe.DistanceTo(e);
                    if (dd <= endTolerance && dd < bestEnd.d) bestEnd = (dd, oe);
                }
            if (bestEnd.d < double.MaxValue) return bestEnd.d < 0.01 ? null : new EndMagnet(bestEnd.p, TipoEncaixe.PontaDeVia);
            // Eixo de outra via: a ponta caiu sobre a pista/calçada dela (ou passou um pouco do eixo).
            EndMagnet? best = null;
            var bestMove = double.MaxValue;
            foreach (var o in others)
            {
                var half = Math.Max(o.Def.TotalLeft, o.Def.TotalRight);
                var (_, dist, q) = IntersectionGenerator.Project(o.Axis, e);
                if (dist > half + 1.5) continue;
                if (dist < 0.01) return null;                       // já está no eixo
                // Ao longo da própria direção (prolonga ou apara), limitado; senão, projeção.
                var target = q;
                var reach = Math.Min(40, (half + 2) * 3);
                var hit = RayHit(prev, dir, o.Axis, e, reach);
                if (hit is { } h) target = h;
                var move = target.DistanceTo(e);
                if (move < bestMove) { bestMove = move; best = new EndMagnet(target, TipoEncaixe.EixoDeVia); }
            }
            if (best is { } b)
            {
                // Não encurta a via a ponto de sumir.
                var other = atEnd ? axis.Points[0] : axis.Points[^1];
                if (b.Point.DistanceTo(other) < 3) return null;
            }
            return best;
        }
        return (One(false), One(true));
    }

    /// <summary>Cruzamento da reta (p0 + t·dir) com o eixo, o mais perto de <paramref name="near"/> (até <paramref name="reach"/> m).</summary>
    private static Vec2? RayHit(Vec2 p0, Vec2 dir, Polyline2 axis, Vec2 near, double reach)
    {
        Vec2? best = null;
        var bestD = double.MaxValue;
        var a = p0 - dir * 1000;
        var r = dir * 2000;
        for (int j = 0; j + 1 < axis.Points.Count; j++)
        {
            var q = axis.Points[j];
            var sv = axis.Points[j + 1] - q;
            var den = r.Cross(sv);
            if (Math.Abs(den) < 1e-9) continue;
            var t = (q - a).Cross(sv) / den;
            var u = (q - a).Cross(r) / den;
            if (t < 0 || t > 1 || u < -1e-9 || u > 1 + 1e-9) continue;
            var p = a + r * t;
            var d = p.DistanceTo(near);
            if (d <= reach && d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>
    /// Montagem da via passo a passo: posição de um elemento colocado junto ao bordo da pista (lado esquerdo = +).
    /// Meio-fio, calçada e grama são empilhados para fora a partir do que já existe; sarjeta e linhas pintadas ficam
    /// dentro da pista, junto ao bordo. Atualiza a seção registrada no pavimento (larguras de calçada / sarjetas) para
    /// que as interseções refaçam também esses elementos.
    /// </summary>
    public static double PlaceAtEdge(RoadPavementDefinition pav, bool left, LinearMarkingDefinition line, Catalogo cat)
    {
        var w = RoadSectionInference.Width(line, cat);
        var side = left ? 1 : -1;
        var edge = left ? pav.LeftWidth : pav.RightWidth;
        double offset;
        if (line.Code is "SARJETA" or "SARJETAO")
        {
            // Sarjetas já existentes neste bordo empurram a nova para dentro.
            var inner = pav.Gaps.Where(g => !g.Median && Math.Sign(g.Offset) == side).Select(g => Math.Abs(g.Offset) - g.Width / 2).DefaultIfEmpty(edge).Min();
            offset = side * (inner - w / 2);
            pav.Gaps.Add(new PavementGap(offset, w, Median: false));
        }
        else if (RoadSectionInference.IsPhysical(line.Code))
        {
            var walk = left ? pav.LeftSidewalk : pav.RightSidewalk;
            offset = side * (edge + walk + w / 2);
            if (left) pav.LeftSidewalk = walk + w; else pav.RightSidewalk = walk + w;
            if (line.Code.StartsWith("MEIO-FIO") && walk < 0.01) pav.CurbWidth = w;
        }
        else
        {
            var gutter = pav.Gaps.Where(g => !g.Median && Math.Sign(g.Offset) == side).Select(g => Math.Abs(g.Offset) - g.Width / 2).DefaultIfEmpty(edge).Min();
            offset = side * (gutter - 0.10 - w / 2);
        }
        line.Offset = offset;
        line.Alignment = null;
        line.GroupId = pav.GroupId;
        line.Hierarchy = pav.Hierarchy;
        line.SetPath(CopyPath(pav.PathRef));
        return offset;
    }

    public static PathReference CopyPath(PathReference p) => p.Clone();

    /// <summary>Pista simples (só o pavimento) – o ponto de partida para montar a via elemento por elemento.</summary>
    public static List<MarkingDefinition> BuildCarriageway(RoadPavementDefinition template, PathReference path, OutputSettings output,
        string? centerLine, bool edgeLines, double speed)
    {
        var groupId = Guid.NewGuid().ToString("N");
        var pav = (RoadPavementDefinition)template.CloneWithNewId();
        pav.GroupId = groupId;
        pav.Output = output.Clone();
        pav.RightSidewalk = 0;
        pav.LeftSidewalk = 0;
        pav.Gaps.Clear();
        pav.Exclusions.Clear();
        pav.SetPath(CopyPath(path));
        var res = new List<MarkingDefinition> { pav };
        LinearMarkingDefinition Line(string code, double offset) => new()
        {
            Code = code, Speed = speed, Offset = offset, PathRef = CopyPath(path), GroupId = groupId, Output = output.Clone(), Hierarchy = pav.Hierarchy,
        };
        if (!string.IsNullOrEmpty(centerLine) && pav.TwoWay) res.Add(Line(centerLine, 0));
        if (edgeLines)
        {
            res.Add(Line("LBO", pav.LeftWidth - 0.10 - 0.05));
            res.Add(Line("LBO", -(pav.RightWidth - 0.10 - 0.05)));
        }
        return res;
    }
}
