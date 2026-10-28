using System.Globalization;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>
/// Seção-tipo da via para a terraplenagem estimada no Perfil da via, antes de aplicar o greide (pedido de 2026-10-27, itens 4
/// e 21): plataforma (pista e calçadas) com a cota do greide e o abaulamento dele, e os taludes de corte e de aterro (H : 1 V)
/// da plataforma até o terreno. É a "montagem" da seção – o resultado exato depois de aplicar é o da Terraplenagem.
/// </summary>
/// <param name="LeftHalf">Largura da plataforma à esquerda do eixo (m).</param>
/// <param name="RightHalf">Largura da plataforma à direita do eixo (m).</param>
/// <param name="Reach">Alcance máximo dos taludes a partir da borda da plataforma (m).</param>
/// <param name="Trechos">Taludes por lado e por trecho (<see cref="RoadGrade.Taludes"/>) sobre <paramref name="CutSlope"/> e <paramref name="FillSlope"/>.</param>
public sealed record SecaoTipo(double LeftHalf, double RightHalf, double CutSlope, double FillSlope, double Reach = 60,
    IReadOnlyList<TrechoTalude>? Trechos = null);

/// <summary>Seção transversal numa estaca: terreno, projeto (plataforma e taludes), corte e aterro e as áreas deles.</summary>
public sealed class SecaoTransversal
{
    /// <summary>Distância ao longo do eixo (m).</summary>
    public double Estaca { get; init; }
    /// <summary>Cota do greide no eixo (m, relativa à base).</summary>
    public double ZEixo { get; init; }
    /// <summary>Terreno ao longo da seção: (afastamento, cota) – afastamento positivo à esquerda do eixo.</summary>
    public List<Vec2> Terreno { get; } = new();
    /// <summary>Projeto: plataforma e taludes até o encontro com o terreno (offset, cota).</summary>
    public List<Vec2> Projeto { get; } = new();
    public List<Polygon2> Corte { get; } = new();
    public List<Polygon2> Aterro { get; } = new();
    public double AreaCorte { get; set; }
    public double AreaAterro { get; set; }
    /// <summary>Taludes que não chegaram ao terreno dentro do alcance (lado esquerdo/direito).</summary>
    public bool SemEncontroEsquerdo { get; set; }
    public bool SemEncontroDireito { get; set; }
}

/// <summary>Ponto do diagrama de massas (Brückner): estaca e ordenada (m³).</summary>
public readonly record struct PontoMassa(double Estaca, double Ordenada);

/// <summary>
/// Terraplenagem por estaca da seção-tipo: seção em qualquer estaca (o visualizador), volumes pela média das áreas
/// (<see cref="SectionVolumes.FromSections"/> – o mesmo método das seções da Terraplenagem), volumes de um trecho de estacas e
/// o diagrama de massas.
/// </summary>
public static class EstacasTerraplenagem
{
    /// <summary>
    /// Seção na estaca <paramref name="s"/>: a plataforma no greide (abaulamento e superelevação de <paramref name="grade"/>) e,
    /// de cada borda, o talude de corte (subindo) ou de aterro (descendo) conforme o terreno na borda, até encontrá-lo. Áreas
    /// pela regra do ponto médio no passo <paramref name="step"/> (como <see cref="SectionVolumes.Compute"/>).
    /// </summary>
    public static SecaoTransversal Secao(Polyline2 axis, double s, RoadGrade grade, SecaoTipo tipo, Func<Vec2, double?> ground, double step = 0.25)
    {
        s = Math.Clamp(s, 0, axis.Length);
        var eps = Math.Min(1e-3, axis.Length / 4);
        var sm = Math.Clamp(s, eps, axis.Length - eps);
        var c = axis.PointAt(sm);
        var nrm = axis.TangentAt(sm).PerpLeft;
        double? G(double o) => ground(c + nrm * o);
        var sec = new SecaoTransversal { Estaca = s, ZEixo = grade.Z(s) };
        // Plataforma no greide, com o abaulamento e a superelevação dele.
        double Plat(double o) => grade.Z(s, o);
        // Talude de cada lado: da borda (afastamento e cota) até o terreno.
        (List<Vec2> Pts, bool Hit) Talude(double edge, int dir)
        {
            var pts = new List<Vec2>();
            var ze = Plat(edge);
            var ge = G(edge);
            if (ge is not { } g0) return (pts, false);
            var cut = g0 > ze;
            // Talude do lado nesta estaca (por lado e por trecho), com a largura fixa e o limite – a mesma conta da Terraplenagem.
            var spec = TaludesDaVia.Resolver(tipo.CutSlope, tipo.FillSlope, tipo.Trechos, dir > 0, axis.Length, tipo.Reach)(s);
            var (rc, rf, lim) = spec.NoVertice(c + nrm * edge, ze, nrm * dir, ground);
            var slope = Math.Max(0.01, cut ? rc : rf);
            var reach = Math.Clamp(lim, 0.1, tipo.Reach);
            double Z(double d) => cut ? ze + d / slope : ze - d / slope;
            var prev = (D: 0.0, Diff: g0 - ze);
            for (var d = step; d <= reach + 1e-9; d += step)
            {
                var o = edge + dir * d;
                if (G(o) is not { } g) break;
                var diff = g - Z(d);
                if (cut ? diff <= 0 : diff >= 0)
                {
                    // Encontro entre o passo anterior e este (interpolação linear da diferença).
                    var t = Math.Abs(prev.Diff) / Math.Max(1e-12, Math.Abs(prev.Diff) + Math.Abs(diff));
                    var dd = prev.D + (d - prev.D) * t;
                    pts.Add(new Vec2(edge + dir * dd, Z(dd)));
                    return (pts, true);
                }
                pts.Add(new Vec2(o, Z(d)));
                prev = (d, diff);
            }
            // Limite do talude (por trecho): no limite, face vertical até o terreno (contenção).
            if (reach < tipo.Reach - 1e-6 && G(edge + dir * reach) is { } gl)
            {
                pts.Add(new Vec2(edge + dir * reach, Z(reach)));
                pts.Add(new Vec2(edge + dir * reach, gl));
                return (pts, true);
            }
            return (pts, false);
        }
        var left = Talude(tipo.LeftHalf, 1);
        var right = Talude(-tipo.RightHalf, -1);
        sec.SemEncontroEsquerdo = !left.Hit;
        sec.SemEncontroDireito = !right.Hit;
        // Projeto da direita para a esquerda: talude direito (do terreno à borda), plataforma, talude esquerdo.
        var proj = new List<Vec2>();
        proj.AddRange(Enumerable.Reverse(right.Pts));
        var n = Math.Max(2, (int)Math.Ceiling((tipo.LeftHalf + tipo.RightHalf) / Math.Max(step, 0.05)));
        for (var k = 0; k <= n; k++)
        {
            var o = -tipo.RightHalf + (tipo.LeftHalf + tipo.RightHalf) * k / n;
            proj.Add(new Vec2(o, Plat(o)));
        }
        proj.AddRange(left.Pts);
        sec.Projeto.AddRange(proj);
        // Cota do projeto num afastamento (linear entre os pontos do projeto); nula fora dele.
        double? D(double o)
        {
            if (proj.Count < 2 || o < proj[0].X - 1e-9 || o > proj[^1].X + 1e-9) return null;
            for (int i = 0; i + 1 < proj.Count; i++)
                if (o <= proj[i + 1].X + 1e-12)
                {
                    var (a, b) = (proj[i], proj[i + 1]);
                    var dx = b.X - a.X;
                    return dx < 1e-12 ? b.Y : a.Y + (b.Y - a.Y) * (o - a.X) / dx;
                }
            return proj[^1].Y;
        }
        // Terreno na largura da seção (com 2 m além do projeto) e as áreas pela regra do ponto médio.
        var o0 = proj.Count > 0 ? proj[0].X : -tipo.RightHalf;
        var o1 = proj.Count > 0 ? proj[^1].X : tipo.LeftHalf;
        for (var o = o0 - 2; o <= o1 + 2 + 1e-9; o += step)
            if (G(o) is { } g) sec.Terreno.Add(new Vec2(o, g));
        var cutQ = new List<Polygon2>();
        var fillQ = new List<Polygon2>();
        var m = Math.Max(1, (int)Math.Ceiling((o1 - o0) / step));
        var dy = (o1 - o0) / m;
        for (var k = 0; k < m; k++)
        {
            var a = o0 + k * dy;
            var b = a + dy;
            var mid = (a + b) / 2;
            if (D(mid) is not { } zm || G(mid) is not { } gm) continue;
            var diff = gm - zm;
            if (diff > 0) sec.AreaCorte += diff * dy; else sec.AreaAterro -= diff * dy;
            if (D(a) is { } za && D(b) is { } zb && G(a) is { } ga && G(b) is { } gb && Math.Abs(diff) > 1e-6)
            {
                var q = new Polygon2(new[] { new Vec2(a, ga), new Vec2(b, gb), new Vec2(b, zb), new Vec2(a, za) });
                if (Math.Abs(q.Area) > 1e-9) (diff > 0 ? cutQ : fillQ).Add(q);
            }
        }
        foreach (var (quads, list) in new[] { (cutQ, sec.Corte), (fillQ, sec.Aterro) })
        {
            try { list.AddRange(PolygonOps.Union(quads).Where(p => p.Area > 1e-6)); }
            catch { list.AddRange(quads); }
        }
        return sec;
    }

    /// <summary>
    /// Seções a cada <paramref name="spacing"/> (e no fim do eixo) e os volumes pela média das áreas – a mesma conta das seções
    /// da Terraplenagem (<see cref="SectionVolumes.FromSections"/>).
    /// </summary>
    public static (VolumesPorSecao Volumes, List<SecaoTransversal> Secoes) Volumes(Polyline2 axis, RoadGrade grade, SecaoTipo tipo,
        Func<Vec2, double?> ground, double spacing = 20, double step = 0.25)
    {
        var secs = new List<SecaoTransversal>();
        if (axis.Length < 1e-6) return (new VolumesPorSecao(), secs);
        spacing = Math.Max(0.5, spacing);
        for (var s = 0.0; s < axis.Length - 1e-6; s += spacing) secs.Add(Secao(axis, s, grade, tipo, ground, step));
        secs.Add(Secao(axis, axis.Length, grade, tipo, ground, step));
        return (SectionVolumes.FromSections(secs.Select(x => new SecaoVolume(x.Estaca, x.AreaCorte, x.AreaAterro))), secs);
    }

    /// <summary>
    /// Diagrama de massas (Brückner): em cada seção, o corte acumulado menos o aterro acumulado × o fator de homogeneização
    /// (o corte que o aterro compactado consome – informado pelo usuário). Ordenada final positiva = sobra (bota-fora);
    /// negativa = falta (empréstimo).
    /// </summary>
    public static List<PontoMassa> Bruckner(VolumesPorSecao v, double homogeneizacao = 1.0) =>
        v.Acumulado.Select(a => new PontoMassa(a.Estaca, a.Corte - a.Aterro * homogeneizacao)).ToList();

    /// <summary>Estacas em que o diagrama de massas cruza o zero (trechos que se compensam entre si).</summary>
    public static List<double> PontosDePassagem(IReadOnlyList<PontoMassa> m)
    {
        var res = new List<double>();
        for (int i = 0; i + 1 < m.Count; i++)
        {
            var (a, b) = (m[i], m[i + 1]);
            if (a.Ordenada == 0 && i > 0) res.Add(a.Estaca);
            else if (a.Ordenada * b.Ordenada < 0)
                res.Add(a.Estaca + (b.Estaca - a.Estaca) * Math.Abs(a.Ordenada) / (Math.Abs(a.Ordenada) + Math.Abs(b.Ordenada)));
        }
        return res;
    }

    // ------------------------------------------------------------------ PIVs

    /// <summary>
    /// Acrescenta um PIV na estação <paramref name="s"/> com a cota <paramref name="z"/> (e a curva, nula = a padrão); um PIV a
    /// menos de 0,5 m dali é substituído. Os PIVs ficam em ordem de estação.
    /// </summary>
    public static List<GradePoint> AcrescentarPiv(IEnumerable<GradePoint> pvis, double s, double z, double? curve = null)
    {
        var list = pvis.Where(p => Math.Abs(p.S - s) >= 0.5).Select(p => new GradePoint(p.S, p.Z, p.Curve)).ToList();
        list.Add(new GradePoint(s, z, curve is > 0.01 ? curve : null));
        return list.OrderBy(p => p.S).ToList();
    }

    /// <summary>Move o PIV de número <paramref name="numero"/> (1 = o primeiro em estação) para (s, z). Fora da lista: nada muda.</summary>
    public static List<GradePoint> MoverPiv(IEnumerable<GradePoint> pvis, int numero, double s, double z)
    {
        var list = pvis.OrderBy(p => p.S).Select(p => new GradePoint(p.S, p.Z, p.Curve)).ToList();
        if (numero < 1 || numero > list.Count) return list;
        var curve = list[numero - 1].Curve;
        list.RemoveAt(numero - 1);
        return AcrescentarPiv(list, s, z, curve);
    }

    /// <summary>Remove o PIV de número <paramref name="numero"/>; o greide fica com pelo menos dois PIVs.</summary>
    public static List<GradePoint> RemoverPiv(IEnumerable<GradePoint> pvis, int numero)
    {
        var list = pvis.OrderBy(p => p.S).Select(p => new GradePoint(p.S, p.Z, p.Curve)).ToList();
        if (list.Count <= 2 || numero < 1 || numero > list.Count) return list;
        list.RemoveAt(numero - 1);
        return list;
    }

    /// <summary>PIVs no texto da janela ("estaca; cota; curva", uma linha por PIV) – a ida e volta de <see cref="RoadGrade.ParsePvis"/>.</summary>
    public static string TextoPivs(IEnumerable<GradePoint> pvis)
    {
        var ci = CultureInfo.InvariantCulture;
        return string.Join("\n", pvis.OrderBy(p => p.S).Select(p =>
            p.Curve is { } c ? string.Format(ci, "{0:0.##}; {1:0.###}; {2:0.#}", p.S, p.Z, c) : string.Format(ci, "{0:0.##}; {1:0.###}", p.S, p.Z)));
    }
}
