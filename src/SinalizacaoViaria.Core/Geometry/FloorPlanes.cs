using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Geometry;

/// <summary>Plano z = A + B·x + C·y (m).</summary>
public readonly record struct Plane3(double A, double B, double C)
{
    public double Z(Vec2 p) => A + B * p.X + C * p.Y;
    /// <summary>Rampa máxima do plano (m/m).</summary>
    public double Slope => Math.Sqrt(B * B + C * C);
    public bool IsLevel => Slope < 1e-5;
}

/// <summary>
/// Pisos LIMPOS sobre superfícies inclinadas (greide, nó, terreno): em vez de deformar um piso grande com dezenas de pontos
/// – o Revit desenha cada vinco da triangulação e a pista fica "suja" –, o piso é dividido em partes cujo topo é PLANO dentro
/// da tolerância (padrão 1,5 cm). Cada parte vira um piso plano inclinado: uma única face, sem vincos; entre partes vizinhas
/// fica só a junta reta, como as juntas de uma pavimentação real.
/// </summary>
public static class FloorPlanes
{
    /// <summary>Plano de mínimos quadrados de pontos (x, y, z) e o maior desvio.</summary>
    public static (Plane3 Plane, double MaxDev) Fit(IReadOnlyList<Vec3> pts)
    {
        if (pts.Count == 0) return (new Plane3(0, 0, 0), 0);
        if (pts.Count < 3) return (new Plane3(pts.Average(p => p.Z), 0, 0), pts.Max(p => Math.Abs(p.Z - pts.Average(q => q.Z))));
        // Centrado para estabilidade numérica.
        double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y), mz = pts.Average(p => p.Z);
        double sxx = 0, sxy = 0, syy = 0, sxz = 0, syz = 0;
        foreach (var p in pts)
        {
            double x = p.X - mx, y = p.Y - my, z = p.Z - mz;
            sxx += x * x; sxy += x * y; syy += y * y; sxz += x * z; syz += y * z;
        }
        var det = sxx * syy - sxy * sxy;
        double b = 0, c = 0;
        if (Math.Abs(det) > 1e-12) { b = (sxz * syy - syz * sxy) / det; c = (syz * sxx - sxz * sxy) / det; }
        else if (sxx > 1e-12) b = sxz / sxx;
        else if (syy > 1e-12) c = syz / syy;
        var plane = new Plane3(mz - b * mx - c * my, b, c);
        var dev = pts.Max(p => Math.Abs(p.Z - plane.Z(p.XY)));
        return (plane, dev);
    }

    /// <summary>Amostras da superfície na peça: contorno a cada ~1 m e malha interna.</summary>
    public static List<Vec3> Samples(Polygon2 shape, Func<Vec2, double> z, double ringStep = 1.0)
    {
        var res = new List<Vec3>();
        var ring = CurveTools.Densify(shape.Outer.Append(shape.Outer[0]).ToList(), ringStep);
        foreach (var v in ring) res.Add(Vec3.At(v, z(v)));
        var (mn, mx) = shape.Bounds;
        var step = Math.Max(0.75, Math.Sqrt(Math.Max(1, shape.Area) / 250));
        for (var x = mn.X + step / 2; x < mx.X; x += step)
            for (var y = mn.Y + step / 2; y < mx.Y; y += step)
            {
                var q = new Vec2(x, y);
                if (shape.Contains(q)) res.Add(Vec3.At(q, z(q)));
            }
        return res;
    }

    /// <summary>
    /// Divide a peça até cada parte ser plana dentro de <paramref name="tol"/> m. Com o eixo da via, divide primeiro na
    /// crista do abaulamento e depois ao longo do eixo (cortes perpendiculares); sem eixo, ao meio pelo lado maior.
    /// Onde a superfície é torcida (concordância de greides num nó, transição de superelevação) e nem partes pequenas
    /// (&lt; <paramref name="warpArea"/> m²) ficam planas, a parte volta marcada como EMPENADA (Warped): vira um piso único com
    /// edição de forma numa malha regular – melhor que dezenas de lascas planas com degraus.
    /// </summary>
    public static List<(Polygon2 Part, Plane3 Plane, bool Warped)> Split(Polygon2 shape, Func<Vec2, double> z, double tol = 0.015,
        GradeSurface? axis = null, double warpArea = 25, int maxDepth = 9, double ringStep = 1.0)
    {
        var res = new List<(Polygon2, Plane3, bool)>();
        // Ao longo de uma via (ou de um nó guiado pela via principal) nunca há peça empenada: a divisão continua em faixas
        // transversais (juntas retas) até ficar plana – triângulos de edição de forma só onde não há eixo de referência.
        var along = axis != null;
        Go(shape, 0);
        if (along && res.Count > 1) res = Merge(res, z, tol, axis!, ringStep);
        if (along) res = res.Select(r => (r.Item1, Chord(r.Item1, r.Item2, z, tol, axis!, ringStep), r.Item3)).ToList();
        return res;

        void Go(Polygon2 p, int depth)
        {
            var (plane, dev) = Fit(Samples(p, z, ringStep));
            if (dev <= tol) { res.Add((p, plane, false)); return; }
            if (!along && (p.Area < warpArea || depth >= maxDepth)) { res.Add((p, plane, dev > 2 * tol)); return; }
            if (along && depth >= 14) { res.Add((p, plane, false)); return; }
            var halves = Halves(p, axis, along);
            if (halves.Count < 2) { res.Add((p, plane, !along && dev > 2 * tol)); return; }
            foreach (var h in halves) Go(h, depth + 1);
        }
    }

    /// <summary>Ponto do plano a partir de estação/afastamento no eixo (prolongado em reta além das pontas).</summary>
    public static Vec2 AxisPoint(GradeSurface surf, double s, double y)
    {
        var axis = surf.Axis;
        var sc = Math.Clamp(s, 0, axis.Length);
        var t = axis.TangentAt(sc);
        return axis.PointAt(sc) + t * (s - sc) + t.PerpLeft * y;
    }

    /// <summary>
    /// Junta de volta partes vizinhas (mesma faixa entre quebras, uma depois da outra ao longo do eixo) que juntas ainda
    /// cabem num plano: a divisão ao meio corta também trechos que já eram planos – menos juntas, pisos maiores.
    /// </summary>
    private static List<(Polygon2, Plane3, bool)> Merge(List<(Polygon2, Plane3, bool)> parts, Func<Vec2, double> z, double tol, GradeSurface axis, double ringStep)
    {
        var breaks = new List<double> { 0 };
        if (axis.Grade.CrossfallWidth > 0.5) { breaks.Add(axis.Grade.CrossfallWidth); breaks.Add(-axis.Grade.CrossfallWidth); }
        int Band(Polygon2 p) { var y = axis.LocateExtended(p.Centroid).Y; return breaks.Count(b => y > b); }
        var list = parts.Select(p => (Poly: p.Item1, Plane: p.Item2, W: p.Item3, B: Band(p.Item1), S: axis.LocateExtended(p.Item1.Centroid).S)).ToList();
        var changed = true;
        var guard = 0;
        while (changed && guard++ < 400)
        {
            changed = false;
            var order = list.Select((x, i) => (x, i)).OrderBy(t => t.x.B).ThenBy(t => t.x.S).Select(t => t.i).ToList();
            for (int k = 0; k + 1 < order.Count; k++)
            {
                var a = list[order[k]];
                var b = list[order[k + 1]];
                if (a.B != b.B || a.W || b.W) continue;
                List<Polygon2> u;
                try { u = PolygonOps.Union(new[] { a.Poly, b.Poly }); } catch { continue; }
                if (u.Count != 1 || u[0].Holes.Count > a.Poly.Holes.Count + b.Poly.Holes.Count) continue;
                if (Math.Abs(u[0].Area - a.Poly.Area - b.Poly.Area) > 0.01 * (a.Poly.Area + b.Poly.Area) + 0.02) continue;
                var (plane, dev) = Fit(Samples(u[0], z, ringStep));
                if (dev > tol) continue;
                var merged = (u[0], plane, false, a.B, axis.LocateExtended(u[0].Centroid).S);
                var i0 = order[k];
                var i1 = order[k + 1];
                list[Math.Min(i0, i1)] = merged;
                list.RemoveAt(Math.Max(i0, i1));
                changed = true;
                break;
            }
        }
        return list.Select(x => (x.Poly, x.Plane, x.W)).ToList();
    }

    /// <summary>
    /// Plano da parte passando EXATAMENTE pela superfície nas juntas transversais (no lado junto à crista) e no meio da
    /// borda oposta – partes vizinhas se encontram sem degrau. Se ficar pior que a tolerância, mantém o de mínimos quadrados.
    /// </summary>
    private static Plane3 Chord(Polygon2 p, Plane3 lsq, Func<Vec2, double> z, double tol, GradeSurface axis, double ringStep)
    {
        try
        {
            var loc = p.Outer.Select(axis.LocateExtended).ToList();
            double s0 = loc.Min(l => l.S), s1 = loc.Max(l => l.S);
            if (s1 - s0 < 0.3) return lsq;
            var inner = loc.OrderBy(l => Math.Abs(l.Y)).First().Y;
            var outer = loc.OrderByDescending(l => Math.Abs(l.Y - inner)).First().Y;
            if (Math.Abs(outer - inner) < 0.2) return lsq;
            var a = AxisPoint(axis, s0, inner);
            var b = AxisPoint(axis, s1, inner);
            var c = AxisPoint(axis, (s0 + s1) / 2, outer);
            var pts = new[] { Vec3.At(a, z(a)), Vec3.At(b, z(b)), Vec3.At(c, z(c)) };
            var n = (pts[1] - pts[0]).Cross(pts[2] - pts[0]);
            if (Math.Abs(n.Z) < 1e-9) return lsq;
            // n·(X − P0) = 0  →  z = P0.z − (nx(x − x0) + ny(y − y0)) / nz
            var bx = -n.X / n.Z;
            var by = -n.Y / n.Z;
            var chord = new Plane3(pts[0].Z - bx * pts[0].X - by * pts[0].Y, bx, by);
            var samples = Samples(p, z, ringStep);
            var dev = samples.Max(q => Math.Abs(q.Z - chord.Z(q.XY)));
            return dev <= Math.Max(1.2 * tol, 1e-4) ? chord : lsq;
        }
        catch
        {
            return lsq;
        }
    }

    private static List<Polygon2> Halves(Polygon2 p, GradeSurface? axis, bool alongOnly = false)
    {
        try
        {
            if (axis != null)
            {
                var loc = p.Outer.Select(axis.LocateExtended).ToList();
                double y0 = loc.Min(l => l.Y), y1 = loc.Max(l => l.Y), s0 = loc.Min(l => l.S), s1 = loc.Max(l => l.S);
                var w = Math.Max(Math.Abs(y0), Math.Abs(y1)) + 2;
                // Linhas de quebra da superfície dentro da peça: crista do abaulamento e bordas da faixa abaulada (além
                // delas a superfície é plana na transversal). Cada lado de uma quebra é um plano.
                var breaks = new List<double>();
                if (Math.Abs(axis.Grade.Crossfall) > 1e-6 || axis.Grade.Superelevation.Count > 0)
                {
                    breaks.Add(0);
                    var cw = axis.Grade.CrossfallWidth;
                    if (cw > 0.5) { breaks.Add(cw); breaks.Add(-cw); }
                }
                foreach (var b in breaks)
                {
                    if (!(y0 < b - 0.3 && y1 > b + 0.3)) continue;
                    var hi = Band(axis, s0 - 3, s1 + 3, b, w);
                    var lo = Band(axis, s0 - 3, s1 + 3, -w, b);
                    var a = PolygonOps.Intersect(new[] { p }, new[] { hi }).Where(x => x.Area > 0.01).ToList();
                    var c = PolygonOps.Intersect(new[] { p }, new[] { lo }).Where(x => x.Area > 0.01).ToList();
                    if (a.Count > 0 && c.Count > 0 && Covers(p, a, c)) return a.Concat(c).ToList();
                }
                if (s1 - s0 > 0.6)
                {
                    var m = (s0 + s1) / 2;
                    var a = PolygonOps.Intersect(new[] { p }, new[] { Band(axis, s0 - 3, m, -w, w) }).Where(x => x.Area > 0.01).ToList();
                    var b = PolygonOps.Intersect(new[] { p }, new[] { Band(axis, m, s1 + 3, -w, w) }).Where(x => x.Area > 0.01).ToList();
                    if (a.Count > 0 && b.Count > 0 && Covers(p, a, b)) return a.Concat(b).ToList();
                }
                if (alongOnly) return new List<Polygon2>();
            }
            var (mn, mx) = p.Bounds;
            Polygon2 A, B;
            if (mx.X - mn.X >= mx.Y - mn.Y)
            {
                var cx = (mn.X + mx.X) / 2;
                A = Polygon2.Rectangle(new Vec2(mn.X - 1, mn.Y - 1), new Vec2(cx, mx.Y + 1));
                B = Polygon2.Rectangle(new Vec2(cx, mn.Y - 1), new Vec2(mx.X + 1, mx.Y + 1));
            }
            else
            {
                var cy = (mn.Y + mx.Y) / 2;
                A = Polygon2.Rectangle(new Vec2(mn.X - 1, mn.Y - 1), new Vec2(mx.X + 1, cy));
                B = Polygon2.Rectangle(new Vec2(mn.X - 1, cy), new Vec2(mx.X + 1, mx.Y + 1));
            }
            return PolygonOps.Intersect(new[] { p }, new[] { A }).Concat(PolygonOps.Intersect(new[] { p }, new[] { B })).Where(x => x.Area > 0.01).ToList();
        }
        catch
        {
            return new List<Polygon2>();
        }
    }

    /// <summary>As metades cobrem a peça inteira (nada perdido no recorte)?</summary>
    private static bool Covers(Polygon2 p, List<Polygon2> a, List<Polygon2> b) =>
        Math.Abs(a.Sum(x => x.Area) + b.Sum(x => x.Area) - p.Area) <= 0.005 * p.Area + 0.02;

    /// <summary>Faixa [s0, s1] × [y0, y1] ao longo do eixo (prolongado em reta além das pontas).</summary>
    public static Polygon2 Band(GradeSurface surf, double s0, double s1, double y0, double y1)
    {
        var axis = surf.Axis;
        Vec2 P(double s, double y)
        {
            var sc = Math.Clamp(s, 0, axis.Length);
            var t = axis.TangentAt(sc);
            return axis.PointAt(sc) + t * (s - sc) + t.PerpLeft * y;
        }
        var st = new List<double> { s0 };
        for (var s = Math.Ceiling(s0); s < s1; s += 1) if (s > s0) st.Add(s);
        st.Add(s1);
        return new Polygon2(st.Select(s => P(s, y1)).Concat(st.AsEnumerable().Reverse().Select(s => P(s, y0))).ToList());
    }
}
