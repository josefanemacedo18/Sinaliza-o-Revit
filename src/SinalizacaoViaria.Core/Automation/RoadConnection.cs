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
}
