using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Catalog;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Generators;

/// <summary>Geometria de um ramo da rotatória (sistema local: t ao longo do ramo a partir do centro, o à esquerda).</summary>
public sealed record RoundaboutLegGeometry(RoundaboutLeg Leg, Vec2 Dir, Vec2 Left, Polygon2? Splitter)
{
    /// <summary>Ilha separadora pintada (zebrado com linha de canalização) – quando o ramo não tem ilha física.</summary>
    public Polygon2? PaintedSplitter { get; init; }
    /// <summary>Travessia de pedestres do ramo: distância ao centro e extensão lateral (de calçada a calçada, atravessando os by-pass).</summary>
    public double CrosswalkT { get; set; }
    public (double Lo, double Hi)? CrosswalkSpan { get; set; }
    /// <summary>Rampas da travessia (ajustadas ao meio-fio): tipo e, em cada ponta (+o, −o), o ponto no meio-fio e o sentido da subida.</summary>
    public TipoRampa RampType { get; set; } = TipoRampa.RebaixamentoComAbas;
    public (Vec2 Curb, Vec2 Up)? RampHi { get; set; }
    public (Vec2 Curb, Vec2 Up)? RampLo { get; set; }
    public Vec2 At(Vec2 center, double t, double o) => center + Dir * t + Left * o;
}

public sealed class RoundaboutLayout
{
    public List<Polygon2> Pavement { get; } = new();
    public List<Polygon2> Apron { get; } = new();
    public List<Polygon2> IslandCurb { get; } = new();
    public List<Polygon2> IslandCore { get; } = new();
    public List<Polygon2> Curb { get; } = new();
    public List<Polygon2> Sidewalk { get; } = new();
    /// <summary>Faixa de serviço da calçada (composição das vias) – subconjunto de Sidewalk.</summary>
    public List<Polygon2> SidewalkService { get; } = new();
    /// <summary>Sarjeta junto ao meio-fio.</summary>
    public List<Polygon2> Gutter { get; } = new();
    public List<Polygon2> SplitterCurb { get; } = new();
    public List<Polygon2> SplitterCore { get; } = new();
    /// <summary>Ilhas separadoras e do by-pass inteiras (antes das passagens de pedestres) – a faixa de pedestres não é pintada sobre elas.</summary>
    public List<Polygon2> Refuges { get; } = new();
    /// <summary>Divisores físicos entre as faixas (turbo-rotatória).</summary>
    public List<Polygon2> Dividers { get; } = new();
    public List<RoundaboutLegGeometry> Legs { get; } = new();
    /// <summary>Área onde os elementos físicos das vias (pavimento, calçadas, meios-fios) são recortados.</summary>
    public Polygon2 Zone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Área onde a pintura das vias é interrompida (o anel, nos modos que não remodelam as entradas).</summary>
    public Polygon2 PaintZone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Borda externa da faixa galgável (= ilha quando não há faixa galgável).</summary>
    public Polygon2 ApronOuter { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Ilha central pintada (minirrotatória MIR) – sem obra civil.</summary>
    public bool PaintedIsland { get; set; }
    /// <summary>Contorno da ilha central (círculo ou elipse).</summary>
    public Polygon2 Island { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Borda externa do anel (pista giratória).</summary>
    public Polygon2 Outer { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    public double LegLength { get; set; }
    /// <summary>Área onde a pintura das vias (linhas, setas, legendas) é interrompida: até depois da travessia e da ilha separadora.</summary>
    public Polygon2 RoadPaintZone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Área sem estacionamento (CTB art. 181: 5 m antes da travessia/alinhamento transversal).</summary>
    public Polygon2 ParkingZone { get; set; } = Polygon2.Rectangle(Vec2.Zero, Vec2.Zero);
    /// <summary>Passagens de pedestres no nível da pista através do canteiro central das vias (refúgio – NBR 9050).</summary>
    public List<Polygon2> MedianPassages { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>Distância, a partir de <paramref name="origin"/> na direção <paramref name="dir"/>, até a borda externa do anel.</summary>
    public double ToOuter(Vec2 origin, Vec2 dir) => RoundaboutGenerator.Ray(Outer, origin, dir);
}

/// <summary>
/// Rotatórias (minirrotatória, compacta, uma ou duas faixas, turbo, oval e com by-pass): ilha central circular ou
/// elíptica, faixa galgável, pista giratória, ramos com raios de entrada e saída distintos, ilhas separadoras e
/// sinalização.
/// </summary>
public static class RoundaboutGenerator
{
    private static Polygon2 Disk(Vec2 c, double r) => new(CurveTools.Circle(c, r, Math.Max(0.005, r * 0.0012)));
    private static Vec2 Dir(double deg) => new(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180));

    private static List<Polygon2> Close(IEnumerable<Polygon2> p, double r) =>
        r > 0.05 ? PolygonOps.Offset(PolygonOps.Offset(p, r, true), -r, true) : p.ToList();

    /// <summary>Elipse (ou círculo) com semieixos a (na direção <paramref name="angleDeg"/>) e b.</summary>
    public static Polygon2 Ellipse(Vec2 c, double a, double b, double angleDeg)
    {
        var u = Dir(angleDeg);
        var v = u.PerpLeft;
        var n = Math.Clamp((int)(Math.Max(a, b) * 8), 48, 256);
        return new Polygon2(Enumerable.Range(0, n).Select(i =>
        {
            var t = 2 * Math.PI * i / n;
            return c + u * (a * Math.Cos(t)) + v * (b * Math.Sin(t));
        }));
    }

    /// <summary>Primeira interseção do raio com o contorno (distância; 0 se não houver).</summary>
    public static double Ray(Polygon2 poly, Vec2 origin, Vec2 dir)
    {
        double best = double.MaxValue;
        var r = poly.Outer;
        for (int i = 0; i < r.Count; i++)
        {
            var p = r[i];
            var e = r[(i + 1) % r.Count] - p;
            var den = dir.Cross(e);
            if (Math.Abs(den) < 1e-12) continue;
            var t = (p - origin).Cross(e) / den;
            var s = (p - origin).Cross(dir) / den;
            if (t > 1e-6 && s >= -1e-9 && s <= 1 + 1e-9 && t < best) best = t;
        }
        return best == double.MaxValue ? 0 : best;
    }

    /// <summary>Setor angular (a → b, anti-horário) como polígono de raio R a partir de c.</summary>
    private static Polygon2 Sector(Vec2 c, double a, double b, double R)
    {
        var span = b - a;
        while (span <= 0) span += 360;
        var n = Math.Max(2, (int)Math.Ceiling(span / 5));
        var pts = new List<Vec2> { c };
        for (int i = 0; i <= n; i++) pts.Add(c + Dir(a + span * i / n) * R);
        return new Polygon2(pts);
    }

    /// <summary>
    /// Recortes de uma marca de via ligada à rotatória: físicos pela zona (e pelas passagens no canteiro), pintura até depois
    /// da travessia, estacionamento 5 m antes dela. Usado pelo Revit e pelos testes (uma regra só).
    /// </summary>
    public static List<Polygon2> RoadCuts(RoundaboutDefinition d, RoundaboutLayout L, MarkingDefinition m)
    {
        var res = new List<Polygon2>();
        if (m is RoadPavementDefinition) { res.Add(L.Zone); return res; }
        if (IntersectionGenerator.IsPhysical(m))
        {
            res.Add(L.Zone);
            res.AddRange(L.MedianPassages);
            // A ilha separadora continua o canteiro da via até 0,30 m além da zona: o canteiro da via sai debaixo dela.
            res.AddRange(L.SplitterCurb.Concat(L.SplitterCore));
            return res;
        }
        res.Add(m is ParkingMarkingDefinition ? L.ParkingZone : L.RoadPaintZone);
        return res;
    }

    /// <summary>A peça é só o prolongamento reto de um ramo (sem by-pass).</summary>
    private static bool LegStripOnly(Polygon2 p, RoundaboutDefinition d, Vec2 c)
    {
        foreach (var leg in d.Legs)
        {
            var u = Dir(leg.AngleDeg);
            var n = u.PerpLeft;
            if (p.Outer.All(q => Math.Abs((q - c).Dot(n)) <= leg.Width / 2 + 0.6)) return true;
        }
        return false;
    }

    private static Polygon2 Grow(Polygon2 poly, double r) =>
        Math.Abs(r) < 1e-6 ? poly : PolygonOps.Offset(new[] { poly }, r, true).OrderByDescending(p => p.Area).FirstOrDefault() ?? poly;

    private static double Norm(double deg) => ((deg % 360) + 360) % 360;

    public static RoundaboutLayout Layout(RoundaboutDefinition d)
    {
        var L = new RoundaboutLayout();
        var c = d.Center;
        var ri = Math.Max(1, d.IslandRadius);
        var k = Math.Clamp(d.Elongation, 1, 4);
        var cw = Math.Max(0.05, d.CurbWidth);
        var painted = d.IslandType == TipoIlhaCentral.Pintada;
        L.PaintedIsland = painted;
        var apronW = painted ? 0 : Math.Max(0, d.ApronWidth);
        var ring = apronW + Math.Max(1, d.Lanes) * d.LaneWidth;
        var island = k > 1.001 ? Ellipse(c, ri * k, ri, d.OvalAngleDeg) : Disk(c, ri);
        L.Island = island;
        var apronOuter = apronW > 0.01 ? Grow(island, apronW) : island;
        L.ApronOuter = apronOuter;
        var outer = Grow(island, ring);
        L.Outer = outer;
        var complete = d.Integration == IntegracaoRotatoria.Completa;
        // Zona remodelada: ilha separadora + transição (o by-pass acrescenta só a área dele, não um círculo inteiro – senão
        // canteiro, estacionamento e linhas das vias sumiam por ~100 m em volta).
        var baseLeg = complete ? (d.SplitterIslands ? d.SplitterLength : 0) + 12 : 0;
        L.LegLength = complete
            ? baseLeg + (d.Bypass ? Math.Max(d.BypassRadius, Math.Max(d.EntryRadius, d.ExitRadius ?? d.EntryRadius) + 16) * 1.6 : 0)
            : 0;
        // Recortes nas vias: completa = toda a zona remodelada; anel = só a pista giratória; ilha = só a ilha (pintura no anel).
        L.PaintZone = Grow(outer, 0.05);
        L.Zone = d.Integration switch
        {
            IntegracaoRotatoria.Completa => Grow(outer, L.LegLength),
            IntegracaoRotatoria.Anel => L.PaintZone,
            _ => Grow(apronOuter, 0.05),
        };
        var zone = new[] { L.Zone };
        var zoneR = Grow(outer, Math.Max(L.LegLength, 5)).Outer.Max(v => v.DistanceTo(c));
        if (d.Legs.Count == 0) L.Warnings.Add("Rotatória sem ramos: informe os ângulos dos ramos ou posicione-a sobre o cruzamento de vias.");
        if (d.Lanes * d.LaneWidth < 4.0) L.Warnings.Add("Pista giratória com menos de 4 m – confira a largura para os veículos de projeto.");
        var inscribed = 2 * (ri + ring);
        if (d.Type == TipoRotatoria.Mini && inscribed > 28) L.Warnings.Add($"Minirrotatória com diâmetro inscrito de {inscribed:0.0} m (referência: 13 a 25 m).");
        if (d.Legs.Count > 0 && inscribed < d.Legs.Max(l => l.Width) + 4)
            L.Warnings.Add($"Diâmetro inscrito de {inscribed:0.0} m menor que a pista mais larga que chega ({d.Legs.Max(l => l.Width):0.0} m) + 4 m: as entradas ficam maiores que o anel – use rotatória compacta/1 faixa com raio maior ou reduza a entrada (MBST Vol. IV).");
        if (d.Type != TipoRotatoria.Mini && d.IslandType is not (TipoIlhaCentral.Galgavel or TipoIlhaCentral.Pintada or TipoIlhaCentral.Calota) && ri < 4 && d.Legs.Count > 0)
            L.Warnings.Add("Ilha central pequena (< 4 m): em rotatórias compactas prefira ilha galgável ou pintada (minirrotatória).");
        if (d.Lanes >= 2 && d.Type is TipoRotatoria.Compacta or TipoRotatoria.Mini) L.Warnings.Add("Rotatórias compactas/mini devem ter uma faixa no anel.");
        if (!complete && d.Legs.Any(l => l.RoadId == null && l.GroupId == null) && d.Legs.Count > 0)
            L.Warnings.Add("Integração 'anel' ou 'somente ilha' sem vias ligadas: a pista dos ramos não é gerada – use 'Completa' ou crie as vias antes.");

        // Pista: anel + ramos, com raio de entrada (lado de chegada) e de saída (lado de partida) por ramo.
        List<Polygon2> full;
        var bypassIslands = new List<Polygon2>();
        if (complete)
        {
            var parts = new List<Polygon2> { outer };
            foreach (var leg in d.Legs)
            {
                var u = Dir(leg.AngleDeg);
                var t0 = Math.Max(0.5, L.ToOuter(c, u) * 0.6);
                parts.AddRange(PolygonOps.Strip(new[] { c + u * t0, c + u * (zoneR + 5) }, Math.Max(3, leg.Width)));
            }
            var raw = PolygonOps.Union(parts);
            full = PolygonOps.Intersect(CloseCorners(d, raw, c, zoneR, outer), zone);

            // Faixas de conversão livre à direita (by-pass): entre cada ramo e o seguinte no sentido de giro.
            if (d.Bypass && d.Legs.Count >= 2)
            {
                var re = Math.Max(0, d.EntryRadius);
                var rx = Math.Max(0, d.ExitRadius ?? d.EntryRadius);
                var bw = Math.Clamp(d.BypassWidth, 3.0, 8.0);
                // A folga entre as concordâncias cresce ~0,41·ΔR na bissetriz: raio suficiente para a faixa + ilha de 2 m.
                var rb = Math.Max(d.BypassRadius, Math.Max(re, rx) + (bw + 2.0) / 0.414);
                List<Polygon2> lobes = new(), lanes = new(), isl = new();
                for (int it = 0; it < 5; it++)
                {
                    var big = PolygonOps.Intersect(Close(raw, rb), zone);
                    var band = PolygonOps.Difference(big, PolygonOps.Offset(big, -bw));
                    lobes = PolygonOps.Difference(big, PolygonOps.Offset(full, 0.02)).Where(p => p.Area > 5).ToList();
                    lanes = PolygonOps.Intersect(lobes, band);
                    isl = PolygonOps.Difference(lobes, PolygonOps.Offset(lanes, 0.02));
                    // Sem fatias estreitas (< 1,5 m) ao longo dos ramos: onde a ilha não cabe, a faixa do by-pass se junta à do ramo.
                    isl = PolygonOps.Offset(PolygonOps.Offset(isl, -0.75, true), 0.75, true).Where(p => p.Area > 6).ToList();
                    if (isl.Count >= Math.Min(d.Legs.Count, 2)) break;
                    rb *= 1.3;   // sem espaço para a ilha: concordância maior
                }
                if (rb > d.BypassRadius + 0.5) L.Warnings.Add($"Raio do by-pass ajustado para {rb:0.0} m (espaço para a faixa e a ilha).");
                if (lanes.Count == 0) L.Warnings.Add("Sem espaço para o by-pass: aumente o raio do by-pass ou reduza a largura da faixa.");
                else
                {
                    full = PolygonOps.Union(full.Concat(lobes));
                    bypassIslands.AddRange(isl);
                }
            }
            if (d.Bypass)
            {
                // Zona efetiva: o miolo (anel + ilhas separadoras + transição) e a área dos by-pass com a calçada em volta.
                // Fora dela as vias continuam com a sua seção (canteiro, estacionamento, faixas).
                var sideW = Math.Max(0, d.SidewalkWidth) + cw + 0.5;
                var coreZone = Grow(outer, baseLeg);
                var byArea = PolygonOps.Offset(PolygonOps.Difference(full, new[] { coreZone }).Where(p => !LegStripOnly(p, d, c)).ToList(), sideW, true);
                var tight = PolygonOps.Union(new[] { coreZone }.Concat(byArea)).OrderByDescending(p => p.Area).ToList();
                if (tight.Count > 0)
                {
                    L.Zone = tight[0];
                    zone = new[] { L.Zone };
                    full = PolygonOps.Intersect(full, zone);
                }
            }
        }
        else full = d.Integration == IntegracaoRotatoria.Anel ? new List<Polygon2> { outer } : new List<Polygon2>();

        // Ilhas separadoras (gota) em cada ramo: físicas, pintadas ou nenhuma (padrão geral ou por ramo).
        var splitters = new List<Polygon2>();
        foreach (var leg in d.Legs)
        {
            var u = Dir(leg.AngleDeg);
            var n = u.PerpLeft;
            var rou = L.ToOuter(c, u);
            var mode = leg.Splitter == IlhaSeparadora.Padrao ? d.SplitterStyle : leg.Splitter;
            if (!d.SplitterIslands || mode == IlhaSeparadora.Padrao) mode = d.SplitterIslands ? IlhaSeparadora.Fisica : IlhaSeparadora.Nenhuma;
            Polygon2? sp = null;
            var med = leg.MedianWidth >= 0.8 ? leg.MedianWidth : 0;
            // Via com canteiro central: nos modos que não remodelam as entradas o próprio canteiro é a ilha separadora (a
            // pintada/física por cima ficaria sobreposta a ele).
            if (med > 0 && !complete) mode = IlhaSeparadora.Nenhuma;
            if (mode != IlhaSeparadora.Nenhuma && leg.Width >= 6)
            {
                var w0 = Math.Min(Math.Max(d.SplitterWidth, med), leg.Width - 6.0);
                if (w0 >= 0.8)
                {
                    var t0 = rou + 1.0;
                    var t1 = rou + Math.Max(3, leg.SplitterLength ?? d.SplitterLength);
                    // Canteiro: a ilha vai até o fim da zona remodelada e termina na largura do canteiro (continuidade).
                    var tipW = 0.6;
                    if (med > 0) { t1 = Math.Max(t1, Ray(L.Zone, c, u) + 0.3); tipW = Math.Min(med, w0); }
                    var tri = new Polygon2(new[] { c + u * t0 - n * (w0 / 2), c + u * t1 - n * (tipW / 2), c + u * t1 + n * (tipW / 2), c + u * t0 + n * (w0 / 2) });
                    sp = PolygonOps.Offset(PolygonOps.Offset(new[] { tri }, -0.25, true), 0.25, true).OrderByDescending(p => p.Area).FirstOrDefault();
                }
                else L.Warnings.Add($"Ramo a {leg.AngleDeg:0}°: pista estreita para ilha separadora (mínimo ~7 m).");
            }
            else if (mode != IlhaSeparadora.Nenhuma && leg.Width < 6)
                L.Warnings.Add($"Ramo a {leg.AngleDeg:0}°: pista estreita para ilha separadora (mínimo ~7 m).");
            var physical = sp != null && mode == IlhaSeparadora.Fisica;
            if (physical) splitters.Add(sp!);
            L.Legs.Add(new RoundaboutLegGeometry(leg, u, n, physical ? sp : null) { PaintedSplitter = sp != null && mode == IlhaSeparadora.Pintada ? sp : null });
        }

        // Zonas de recorte da pintura das vias: completa = a zona inteira; anel/ilha = até depois da travessia e da ilha
        // separadora (linhas não cruzam a faixa de pedestres nem a ilha), estacionamento 5 m antes da travessia.
        var reach = 0.0;
        var withRamps = complete && d.Legs.Any(l => l.Sidewalk > 0.5) || complete && d.SidewalkWidth > 0.5;
        foreach (var g in L.Legs)
        {
            var rou = L.ToOuter(c, g.Dir);
            g.CrosswalkT = rou + Math.Max(2, d.CrosswalkDistance);
            var cwOn = g.Leg.Crosswalk ?? d.Crosswalks;
            // Travessia com rampas: recuada (a partir da distância pedida) até as rampas ficarem inteiras no trecho reto do
            // meio-fio, fora da curva de entrada/saída do ramo.
            if (cwOn && withRamps && (g.Leg.Sidewalk > 0.5 || d.SidewalkWidth > 0.5))
            {
                var fit = FitRamps(d, g, c, full, g.CrosswalkT);
                g.CrosswalkT = fit.T;
                g.RampType = fit.Type;
                g.RampHi = fit.Hi;
                g.RampLo = fit.Lo;
                if (!fit.Ok) L.Warnings.Add($"Ramo a {g.Leg.AngleDeg:0}°: meio-fio curvo junto à travessia – confira as rampas (sem abas).");
                else if (fit.Type == TipoRampa.RebaixamentoSemAbas)
                    L.Warnings.Add($"Ramo a {g.Leg.AngleDeg:0}°: rampas sem abas (meio-fio curvo junto à travessia) – proteja as laterais.");
            }
            var r1 = (cwOn ? g.CrosswalkT - rou + Math.Clamp(d.CrosswalkWidth, 2, 10) : 0) + 0.5;
            if (g.Splitter != null || g.PaintedSplitter != null) r1 = Math.Max(r1, Math.Max(3, g.Leg.SplitterLength ?? d.SplitterLength) + 0.5);
            reach = Math.Max(reach, r1);
            // Canteiro central atravessado pela faixa de pedestres: passagem no nível da pista (refúgio).
            if (cwOn && g.Leg.MedianWidth >= 0.8)
                L.MedianPassages.AddRange(PolygonOps.Strip(new[] { g.At(c, g.CrosswalkT, -g.Leg.MedianWidth / 2 - 0.5), g.At(c, g.CrosswalkT, g.Leg.MedianWidth / 2 + 0.5) },
                    Math.Clamp(d.CrosswalkWidth, 2, 10)));
        }
        L.RoadPaintZone = complete ? L.Zone : d.Integration == IntegracaoRotatoria.Anel ? Grow(outer, reach) : L.PaintZone;
        L.ParkingZone = complete ? Grow(L.Zone, 5) : Grow(outer, reach + (d.Integration == IntegracaoRotatoria.Anel ? 5 : 3));

        var solids = splitters.Concat(bypassIslands).ToList();
        L.Refuges.AddRange(solids);
        // Travessias: de calçada a calçada (atravessando também os by-pass) e passagem no nível da pista nas ilhas
        // (refúgio acessível – NBR 9050), em vez de terminar sobre uma ilha.
        if (complete)
        {
            var passages = new List<Polygon2>();
            var cwW = Math.Clamp(d.CrosswalkWidth, 2, 10);
            foreach (var g in L.Legs)
            {
                if (!(g.Leg.Crosswalk ?? d.Crosswalks)) continue;
                var tc = g.CrosswalkT;
                var hi = Reach(full, g, c, tc, 1);
                var lo = -Reach(full, g, c, tc, -1);
                if (hi - lo < 2) continue;
                g.CrosswalkSpan = (lo, hi);
                if (solids.Count > 0) passages.AddRange(PolygonOps.Strip(new[] { g.At(c, tc, lo - 1), g.At(c, tc, hi + 1) }, cwW));
            }
            if (passages.Count > 0) solids = PolygonOps.Difference(solids, passages).Where(p => p.Area > 0.4).ToList();
        }
        if (complete) L.Pavement.AddRange(PolygonOps.Difference(full, solids.Append(apronOuter)));
        else if (d.Integration == IntegracaoRotatoria.Anel) L.Pavement.AddRange(PolygonOps.Difference(full, new[] { apronOuter }));
        if (apronW > 0.01) L.Apron.AddRange(PolygonOps.Difference(new[] { apronOuter }, new[] { island }));
        if (painted)
        {
            // Sem obra civil: a ilha é a linha de canalização + tachões (+ zebrado), gerados como marcas filhas.
        }
        else if (d.IslandType is TipoIlhaCentral.Galgavel or TipoIlhaCentral.Calota) L.IslandCore.Add(island);
        else
        {
            var core = PolygonOps.Offset(new[] { island }, -cw);
            L.IslandCurb.AddRange(PolygonOps.Difference(new[] { island }, core));
            L.IslandCore.AddRange(core);
        }
        foreach (var sp in solids)
        {
            var sc = PolygonOps.Offset(new[] { sp }, -cw);
            L.SplitterCurb.AddRange(PolygonOps.Difference(new[] { sp }, sc));
            L.SplitterCore.AddRange(sc);
        }

        // Turbo: divisores físicos entre as faixas, interrompidos nas entradas/saídas dos ramos.
        if (d.TurboDividers && d.Lanes >= 2)
        {
            var dw = Math.Clamp(d.DividerWidth, 0.15, 1.0);
            var gaps = d.Legs.SelectMany(l => PolygonOps.Strip(new[] { c, c + Dir(l.AngleDeg) * (zoneR + 5) }, Math.Max(3, l.Width) + 2)).ToList();
            for (int q = 1; q < d.Lanes; q++)
            {
                var r = apronW + q * d.LaneWidth;
                var ringBand = PolygonOps.Difference(PolygonOps.Offset(new[] { island }, r + dw / 2, true), PolygonOps.Offset(new[] { island }, r - dw / 2, true));
                L.Dividers.AddRange(PolygonOps.Difference(ringBand, gaps).Where(p => p.Area > 0.2));
            }
        }

        // Meio-fio externo e calçada em volta – só quando as entradas são remodeladas.
        if (complete)
        {
            var sw = Math.Max(0, d.SidewalkWidth);
            var curb = PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(full, cw, true), full), zone);
            L.Curb.AddRange(curb);
            if (sw > 0.05)
                L.Sidewalk.AddRange(PolygonOps.Intersect(PolygonOps.Difference(PolygonOps.Offset(full, cw + sw, true), PolygonOps.Offset(full, cw, true)), zone));
            if (d.ServiceStripWidth > 0.05) L.SidewalkService.AddRange(Automation.SectionMatch.ServiceBand(full, L.Sidewalk, cw, d.ServiceStripWidth));
            if (d.GutterWidth > 0.05) L.Gutter.AddRange(Automation.SectionMatch.GutterBand(full, curb, d.GutterWidth));
        }
        return L;
    }

    /// <summary>
    /// Distância do eixo do ramo até o bordo da pista na estaca <paramref name="t"/> (lado <paramref name="sgn"/>), com
    /// precisão de 1 mm: com passos de 10 cm a rampa começava até 10 cm dentro da pista.
    /// </summary>
    private static double Reach(IReadOnlyList<Polygon2> full, RoundaboutLegGeometry g, Vec2 c, double t, double sgn)
    {
        bool In(double o) => full.Any(p => p.Contains(g.At(c, t, sgn * o)));
        var o = 0.0;
        while (o < g.Leg.Width / 2 + 40 && In(o + 0.1)) o += 0.1;
        double a = o, b = o + 0.1;
        for (int k = 0; k < 12; k++) { var m = (a + b) / 2; if (In(m)) a = m; else b = m; }
        return a;
    }

    /// <summary>Rampa das travessias da rotatória (NBR 9050, recorte retangular).</summary>
    internal static RampDefinition RampOf(RoundaboutDefinition d, TipoRampa type = TipoRampa.RebaixamentoComAbas) =>
        new() { Type = type, Height = d.CurbHeight, SquareCut = true };

    /// <summary>Ponto do meio-fio na ponta da travessia e o sentido da subida, perpendicular ao meio-fio ali (entrada alargada).</summary>
    private static (Vec2 Curb, Vec2 Up) RampFrame(IReadOnlyList<Polygon2> full, RoundaboutLegGeometry g, Vec2 c, double t, double sgn)
    {
        Vec2 P(double tt) => g.At(c, tt, sgn * Reach(full, g, c, tt, sgn));
        var curb = P(t);
        var tan = (P(t + 1) - P(t - 1)).Normalized();
        if (tan.Length < 0.5) tan = g.Dir;
        var up = tan.PerpLeft;
        if (up.Dot(g.Left * sgn) < 0) up = -up;
        return (curb, up);
    }

    /// <summary>
    /// Estaca da travessia (a partir de <paramref name="t0"/>, até 8 m além) em que as duas rampas ficam inteiras fora da
    /// pista, perpendiculares ao meio-fio; se com abas não couber (meio-fio curvo do by-pass), rampas sem abas.
    /// </summary>
    private static (double T, TipoRampa Type, (Vec2, Vec2)? Hi, (Vec2, Vec2)? Lo, bool Ok) FitRamps(RoundaboutDefinition d, RoundaboutLegGeometry g, Vec2 c,
        IReadOnlyList<Polygon2> full, double t0)
    {
        if (full.Count == 0) return (t0, TipoRampa.RebaixamentoComAbas, null, null, true);
        foreach (var type in new[] { TipoRampa.RebaixamentoComAbas, TipoRampa.RebaixamentoSemAbas })
        {
            var rp = RampOf(d, type);
            var (_, len, _) = RampGenerator.Dimensions(rp);
            var ext = IntersectionGenerator.RampHalfExtent(rp);
            for (var t = t0; t < t0 + 8; t += 0.25)
            {
                var hi = RampFrame(full, g, c, t, 1);
                var lo = RampFrame(full, g, c, t, -1);
                var ok = true;
                foreach (var (curb, up) in new[] { hi, lo })
                {
                    var side = up.PerpLeft;
                    var body = new Polygon2(new[] { curb - side * ext + up * 0.01, curb + side * ext + up * 0.01, curb + side * ext + up * len, curb - side * ext + up * len });
                    if (PolygonOps.TotalArea(PolygonOps.Intersect(new[] { body }, full)) > 1e-4) { ok = false; break; }
                }
                if (ok) return (t, type, hi, lo, true);
            }
        }
        return (t0, TipoRampa.RebaixamentoSemAbas, RampFrame(full, g, c, t0, 1), RampFrame(full, g, c, t0, -1), false);
    }

    /// <summary>
    /// Concordância dos cantos entre ramos vizinhos com raios distintos por ramo: metade do canto junto ao ramo que
    /// chega (raio de entrada dele) e metade junto ao ramo que sai (raio de saída dele). Circulação anti-horária.
    /// </summary>
    /// <summary>
    /// Concordâncias de entrada e de saída de cada ramo: curvas tangentes ao bordo do ramo e ao círculo externo do anel
    /// (traçado usual de rotatórias), e não de um ramo direto ao vizinho – assim a calçada acompanha o anel sem
    /// ondulações entre os ramos. De cada lado do ramo vale o raio daquele lado (entrada no sentido anti-horário
    /// seguinte, saída no anterior), com os valores gerais ou os do próprio ramo.
    /// </summary>
    private static List<Polygon2> CloseCorners(RoundaboutDefinition d, List<Polygon2> raw, Vec2 c, double zoneR, Polygon2 outer)
    {
        var re0 = Math.Max(0, d.EntryRadius);
        var rx0 = Math.Max(0, d.ExitRadius ?? d.EntryRadius);
        var legs = d.Legs.OrderBy(l => Norm(l.AngleDeg)).ToList();
        if (legs.Count == 0) return raw;
        var pieces = new List<Polygon2>(raw);
        for (int i = 0; i < legs.Count; i++)
        {
            var X = legs[i];
            var th = Norm(X.AngleDeg);
            var next = legs.Count > 1 ? Norm(legs[(i + 1) % legs.Count].AngleDeg) : th + 360;
            var prev = legs.Count > 1 ? Norm(legs[(i - 1 + legs.Count) % legs.Count].AngleDeg) : th - 360;
            var spanN = next - th; while (spanN <= 0) spanN += 360;
            var spanP = th - prev; while (spanP <= 0) spanP += 360;
            var u = Dir(X.AngleDeg);
            var t0 = Math.Max(0.5, Ray(outer, c, u) * 0.6);
            var strip = PolygonOps.Strip(new[] { c + u * t0, c + u * (zoneR + 5) }, Math.Max(3, X.Width));
            var baseUnion = PolygonOps.Union(strip.Append(outer));
            var rEntry = Math.Max(0, X.EntryRadius ?? re0);
            var rExit = Math.Max(0, X.ExitRadius ?? rx0);
            if (rEntry > 0.05)
                pieces.AddRange(PolygonOps.Intersect(Close(baseUnion, rEntry), new[] { Sector(c, th, th + Math.Min(spanN / 2, 179), zoneR + 10) }));
            if (rExit > 0.05)
                pieces.AddRange(PolygonOps.Intersect(Close(baseUnion, rExit), new[] { Sector(c, th - Math.Min(spanP / 2, 179), th, zoneR + 10) }));
        }
        var merged = PolygonOps.Union(pieces);
        // Fechamento leve: concordâncias vizinhas que se encontram entre ramos próximos ficam contínuas.
        return PolygonOps.Offset(PolygonOps.Offset(merged, 1.0, true), -1.0, true);
    }

    public static MarkingGeometry Build(RoundaboutDefinition d, BuildContext ctx)
    {
        var L = Layout(d);
        var geo = new MarkingGeometry();
        geo.Warnings.AddRange(L.Warnings);
        void Raised(IEnumerable<Polygon2> shapes, MarkingColor col, double h, double elev = 0)
        {
            foreach (var s in shapes)
            {
                var ss = s.Simplified();
                if (ss != null && ss.Area > 1e-4) geo.Pieces.Add(new MarkingPiece(ss, col) { Thickness = h, Elevation = elev });
            }
        }
        // Rebaixamentos das travessias recortam a calçada da rotatória.
        if (L.Sidewalk.Count > 0)
        {
            var ramps = Children(d, L, new OutputSettings(), 0).OfType<RampDefinition>()
                .Select(r => RampGenerator.Footprint(r, new Polyline2(r.PathRef.Points))).ToList();
            if (ramps.Count > 0)
            {
                var sw = PolygonOps.Difference(L.Sidewalk, ramps);
                var cb = PolygonOps.Difference(L.Curb, ramps);
                var sv = PolygonOps.Difference(L.SidewalkService, ramps);
                L.Sidewalk.Clear(); L.Sidewalk.AddRange(sw);
                L.Curb.Clear(); L.Curb.AddRange(cb);
                L.SidewalkService.Clear(); L.SidewalkService.AddRange(sv);
            }
        }
        var pavColor = d.Pavement switch { TipoPavimento.Bloquete => MarkingColor.Bloquete, TipoPavimento.Concreto => MarkingColor.PavimentoConcreto, _ => MarkingColor.Asfalto };
        var pt = d.Pavement switch { TipoPavimento.Bloquete => 0.08, TipoPavimento.Concreto => 0.15, _ => 0.05 };
        // Rotatória elevada (platô): a pista giratória sobe 'hp' em relação às vias; rampas nos ramos.
        var hp = d.Raised ? Math.Clamp(d.RaisedHeight, 0.03, 0.30) : 0;
        if (d.Pavement != TipoPavimento.Nenhum)
        {
            if (hp > 0)
            {
                var ring = IntersectionGenerator.Sound(PolygonOps.Intersect(L.Pavement, new[] { L.Outer }));
                var rest = IntersectionGenerator.Sound(PolygonOps.Difference(L.Pavement, new[] { L.Outer }));
                Raised(ring, pavColor, pt + hp, -pt);
                Raised(rest, pavColor, pt, -pt);
                var rl = Math.Clamp(d.RampLength, 0.5, 6);
                foreach (var g in L.Legs)
                {
                    var r0 = L.ToOuter(d.Center, g.Dir);
                    if (r0 <= 0) continue;
                    var w = Math.Max(3, g.Leg.Width);
                    var origin = d.Center + g.Dir * (r0 + rl) - g.Left * (w / 2);
                    var prof = new Polygon2(new[] { new Vec2(0, 0), new Vec2(rl + 0.6, 0), new Vec2(rl + 0.6, hp), new Vec2(rl, hp) });
                    geo.Pieces.Add(ProfileSolid.Piece(new ProfileSolid(origin, -g.Dir, prof, g.Left, w), pavColor));
                }
                geo.Warnings.Add($"Rotatória elevada: platô de {hp * 100:0} cm com rampas de {rl:0.0} m – sinalize com A-18/A-32b e confira a drenagem.");
            }
            // Sem lascas de pavimento (by-pass × anel × ilhas): pisos ≥ 0,01 m², sem pescoços finos.
            else Raised(IntersectionGenerator.Sound(L.Pavement), pavColor, pt, -pt);
        }
        Raised(L.Apron, MarkingColor.Bloquete, Math.Clamp(d.ApronHeight, 0.02, 0.15), hp);   // galgável: bloquete elevado
        Raised(L.IslandCurb, MarkingColor.Concreto, d.CurbHeight, hp);
        switch (d.IslandType)
        {
            case TipoIlhaCentral.Galgavel: Raised(L.IslandCore, MarkingColor.Branca, 0.07, hp); break;   // cúpula galgável pintada
            case TipoIlhaCentral.Pavimentada: Raised(L.IslandCore, MarkingColor.Concreto, d.CurbHeight, hp); break;
            case TipoIlhaCentral.Pintada: break;                                                     // marcas filhas (LCA, tachões, zebrado)
            case TipoIlhaCentral.Calota:
            {
                // Tronco de cone baixo (rampado): anéis concêntricos da ilha até o topo.
                var dh = Math.Clamp(d.DomeHeight, 0.05, 0.60);
                List<Vec2> Scaled(double k) => L.Island.Outer.Select(v => d.Center + (v - d.Center) * k).ToList();
                var top = Math.Clamp(d.DomeTopRatio, 0.1, 0.9);
                var rings = new List<(List<Vec2> Ring, double Z)>
                {
                    (Scaled(1.0), hp), (Scaled(1 - (1 - top) * 0.05), hp + dh * 0.12), (Scaled(1 - (1 - top) * 0.35), hp + dh * 0.55),
                    (Scaled(1 - (1 - top) * 0.75), hp + dh * 0.88), (Scaled(top), hp + dh),
                };
                geo.Pieces.Add(DeviceGenerator.Loft(rings, MarkingColor.PavimentoConcreto));
                if (d.DomeGrassTop)
                {
                    // Platô superior gramado (canteiro sobre a calota).
                    var plateau = new Polygon2(Scaled(top * 0.98));
                    geo.Pieces.Add(new MarkingPiece(plateau, MarkingColor.Grama) { Elevation = hp + dh, Thickness = 0.04 });
                }
                break;
            }
            default:
            {
                // Ilha ajardinada: faixa pavimentada opcional entre o meio-fio e a grama; grama elevada opcional (canteiro).
                var paved = Math.Clamp(d.IslandPavedRing, 0, 10);
                var core = paved > 0.05 ? PolygonOps.Offset(L.IslandCore, -paved) : L.IslandCore;
                if (paved > 0.05) Raised(PolygonOps.Difference(L.IslandCore, core), MarkingColor.Concreto, d.CurbHeight, hp);
                Raised(core, MarkingColor.Grama, d.CurbHeight + Math.Clamp(d.GrassRaise, 0, 1), hp);
                break;
            }
        }
        Raised(L.Dividers, MarkingColor.Concreto, 0.10, hp);
        Raised(L.SplitterCurb, MarkingColor.Concreto, d.CurbHeight);
        Raised(L.SplitterCore, MarkingColor.Concreto, d.CurbHeight);
        var profile = d.MatchRoadSection ? d.EdgeProfile : new List<Automation.EdgeBand>();
        if (profile.Count > 0 && L.Curb.Count > 0)
        {
            // Mesmos elementos das vias ligadas (meio-fio, sarjeta, faixa gramada, passeio) em volta da rotatória.
            var region = PolygonOps.Union(L.Curb.Concat(L.Sidewalk));
            var (pieces, inside) = Automation.EdgeProfile.Apply(L.Pavement, region, profile, new[] { L.Zone });
            IntersectionGenerator.AddPieces(geo, pieces);
            if (inside.Count > 0)
            {
                // Pavimento sob a sarjeta: a sarjeta substitui essa faixa do asfalto (sem pisos sobrepostos).
                foreach (var p in geo.Pieces.Where(p => p.Color == pavColor && p.Profile == null && p.Solid == null && p.Elevation < -1e-6).ToList())
                {
                    geo.Pieces.Remove(p);
                    foreach (var r in PolygonOps.Difference(new[] { p.Shape }, inside))
                    {
                        var ss = r.Simplified();
                        if (ss != null && ss.Area > 1e-4) geo.Pieces.Add(p with { Shape = ss });
                    }
                }
            }
        }
        else
        {
            IntersectionGenerator.AddPieces(geo, L.Curb.Select(c => new MarkingPiece(c, MarkingColor.Concreto) { Thickness = d.CurbHeight, Layer = "MEIO-FIO" }));
            Raised(L.Gutter, MarkingColor.Concreto, 0.005);
            if (L.SidewalkService.Count > 0)
            {
                Raised(PolygonOps.Difference(L.Sidewalk, L.SidewalkService), MarkingColor.Concreto, d.CurbHeight);
                Raised(L.SidewalkService, d.ServiceStripGrass ? MarkingColor.Grama : MarkingColor.Concreto, d.CurbHeight + (d.ServiceStripGrass ? 0.0 : 0.001));
            }
            else Raised(L.Sidewalk, MarkingColor.Concreto, d.CurbHeight);
        }


        if (d.Landscaping && d.IslandType == TipoIlhaCentral.Ajardinada && d.IslandRadius >= 3 && ctx.Catalog.Movel("ARVORE") is { } tree)
        {
            var spots = new List<Vec2> { d.Center };
            var ring = d.IslandRadius * 0.55;
            var count = d.Trees > 0 ? d.Trees - 1 : d.IslandRadius >= 6 ? 5 : 0;
            var k = Math.Max(1, d.Elongation);
            var ax = Dir(d.OvalAngleDeg);
            for (int i = 0; i < count; i++)
            {
                var a = Dir(90 + i * 360.0 / count);
                var local = ax * (a.Dot(ax) * k) + ax.PerpLeft * a.Dot(ax.PerpLeft);
                spots.Add(d.Center + local * ring);
            }
            foreach (var (s, i) in spots.Select((s, i) => (s, i)))
                foreach (var p in UrbanGenerator.BuildAt(tree, new LocalFrame(s, Dir(i * 40)), null, null, i == 0 ? null : 5.0, null).Pieces)
                    geo.Pieces.Add(p with { Elevation = p.Elevation + d.CurbHeight });
        }
        geo.UnitCount = 1;
        geo.PathLength = d.Legs.Count;
        return geo;
    }

    /// <summary>
    /// Trechos do meio-fio externo real entre os ramos (concordâncias de entrada e saída, faixa do by-pass), das
    /// travessias de um ramo às do ramo vizinho – para a linha de bordo. Devolve os pontos e o lado (+1 = deslocar
    /// para a esquerda do sentido dos pontos) que fica para dentro da pista.
    /// </summary>
    public static List<(List<Vec2> Points, double InwardSign)>? CurbEdgeChains(RoundaboutDefinition d, RoundaboutLayout L, Vec2 c)
    {
        if (L.Pavement.Count == 0 || L.Legs.Count == 0) return null;
        var outer = L.Pavement.OrderByDescending(p => p.Area).First().Outer;
        if (outer.Count < 3) return null;
        // Densifica (trechos retos longos) para cortar certo nas travessias.
        var pts = new List<Vec2>();
        for (int i = 0; i < outer.Count; i++)
        {
            var a = outer[i];
            var b = outer[(i + 1) % outer.Count];
            var n = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / 0.5));
            for (int k = 0; k < n; k++) pts.Add(a + (b - a) * ((double)k / n));
        }
        var cwW = Math.Clamp(d.CrosswalkWidth, 2, 10);
        bool Keep(Vec2 p)
        {
            foreach (var g in L.Legs)
            {
                var v = p - c;
                var t = v.Dot(g.Dir);
                var o = v.Dot(g.Left);
                var hw = g.Leg.Width / 2;
                var (lo, hi) = g.CrosswalkSpan ?? (-hw, hw);
                // Bordos do próprio ramo e tudo além da travessia (a linha fica entre as travessias de dois ramos).
                if (t > 0 && o > lo - 0.6 && o < hi + 0.6 && t > L.ToOuter(c, g.Dir) - 0.5) return false;
                if (t > g.CrosswalkT - cwW / 2 - 0.6 && o > lo - 8 && o < hi + 8) return false;
            }
            return true;
        }
        var n0 = pts.Count;
        var start = Enumerable.Range(0, n0).FirstOrDefault(i => !Keep(pts[i]), -1);
        if (start < 0) return null;
        var chains = new List<List<Vec2>>();
        var cur = new List<Vec2>();
        for (int s = 1; s <= n0; s++)
        {
            var p = pts[(start + s) % n0];
            if (Keep(p)) cur.Add(p);
            else { if (cur.Count >= 3) chains.Add(cur); cur = new List<Vec2>(); }
        }
        if (cur.Count >= 3) chains.Add(cur);
        // O contorno externo é anti-horário (área positiva): o interior da pista fica à esquerda do sentido dos pontos.
        var inward = SignedArea(outer) >= 0 ? 1.0 : -1.0;
        return chains.Where(ch => new Polyline2(ch).Length > 2).Select(ch => (Simplify(ch), inward)).ToList();
    }

    private static List<Vec2> Simplify(List<Vec2> pts)
    {
        var res = new List<Vec2> { pts[0] };
        for (int i = 1; i < pts.Count - 1; i++)
        {
            var a = res[^1];
            var b = pts[i + 1];
            var ab = b - a;
            var dist = ab.Length < 1e-9 ? 0 : Math.Abs(ab.Cross(pts[i] - a)) / ab.Length;
            if (dist > 0.01 || a.DistanceTo(pts[i]) > 3) res.Add(pts[i]);
        }
        res.Add(pts[^1]);
        return res;
    }

    /// <summary>
    /// Trechos da borda externa do anel entre os ramos (para a linha de bordo). Um único trecho fechado quando não há
    /// ramos abertos; o polígono é percorrido a partir de uma abertura para não partir um trecho no início.
    /// </summary>
    public static List<(List<Vec2> Points, bool Closed)> RingEdgeChains(RoundaboutLayout L, Vec2 c, double reach)
    {
        var openings = L.Legs.SelectMany(g => PolygonOps.Strip(new[] { c, c + g.Dir * reach }, Math.Max(3, g.Leg.Width) + 0.6)).ToList();
        var pts = L.Outer.Outer;
        var n = pts.Count;
        bool Open(Vec2 p) => openings.Any(o => o.Contains(p));
        var start = -1;
        for (int i = 0; i < n; i++) if (Open(pts[i])) { start = i; break; }
        if (start < 0) return new List<(List<Vec2>, bool)> { (pts.ToList(), true) };
        var chains = new List<(List<Vec2>, bool)>();
        var cur = new List<Vec2>();
        for (int s = 1; s <= n; s++)
        {
            var p = pts[(start + s) % n];
            if (!Open(p)) cur.Add(p);
            else if (cur.Count >= 2) { chains.Add((cur, false)); cur = new List<Vec2>(); }
            else cur.Clear();
        }
        if (cur.Count >= 2) chains.Add((cur, false));
        return chains;
    }

    private static double SignedArea(IReadOnlyList<Vec2> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++) a += pts[i].X * pts[(i + 1) % pts.Count].Y - pts[(i + 1) % pts.Count].X * pts[i].Y;
        return a / 2;
    }

    /// <summary>Sinalização da rotatória (marcas independentes): dê a preferência, divisão de faixas, zebrados, travessias e placas.</summary>
    public static List<MarkingDefinition> Children(RoundaboutDefinition d, RoundaboutLayout L, OutputSettings output, double z)
    {
        var res = new List<MarkingDefinition>();
        var c = d.Center;
        var painted = L.PaintedIsland;
        var apronW = painted ? 0 : Math.Max(0, d.ApronWidth);
        var k = Math.Max(1, d.Elongation);
        var v = Math.Max(20, d.ApproachSpeed);
        T Add<T>(T def) where T : MarkingDefinition
        {
            def.Output = output.Clone();
            def.GroupId = d.Id;
            res.Add(def);
            return def;
        }
        LinearMarkingDefinition ClosedLine(string code, Polygon2 ring, double? width = null, MarkingColor? color = null, string? variant = null) =>
            Add(new LinearMarkingDefinition { Code = code, Variant = variant, WidthOverride = width, ColorOverride = color, PathRef = PathReference.FromPoints(ring.Outer, z, true) });

        // ---- ilha central pintada (MBST Vol. IV, MIR 6.a): LCA branca de 0,20 m + tachões a cada 0,25–0,50 m (+ zebrado)
        if (painted)
        {
            var lw = Math.Clamp(d.PaintedLineWidth, 0.10, 0.40);
            ClosedLine("LCA", Grow(L.Island, -lw / 2), lw, MarkingColor.Branca, "0,20 m");
            if (d.PaintedIslandFill)
                Add(new HatchMarkingDefinition
                {
                    Code = "ZPA", BorderWidth = 0, BarColor = MarkingColor.Branca, ReferenceDirection = Vec2.UnitX,
                    Boundary = PathReference.FromPoints(Grow(L.Island, -(lw + 0.05)).Outer, z, true),
                });
            if (d.StudSpacing >= 0.05)
                Add(new DeviceMarkingDefinition { Code = "TACHAO-SEG", Spacing = d.StudSpacing, PathRef = PathReference.FromPoints(Grow(L.Island, 0.15).Outer, z, true) });
        }

        // ---- anel: linha entre faixas, linha de bordo interna (junto à ilha/galgável) e externa (entre os ramos)
        if (d.Markings && d.Lanes > 1 && !d.TurboDividers)
            for (int q = 1; q < d.Lanes; q++)
                ClosedLine(string.IsNullOrWhiteSpace(d.RingLaneLine) ? "LMS-2" : d.RingLaneLine, Grow(L.Island, apronW + q * d.LaneWidth));
        if (d.Markings && d.InnerEdgeLine && !painted)
            ClosedLine("LBO", Grow(L.ApronOuter, 0.15));
        var reach = L.Zone.Outer.Max(p => p.DistanceTo(c)) + 10;
        if (d.Markings && d.OuterEdgeLine && d.Integration != IntegracaoRotatoria.SomenteIlha)
        {
            // Completa: ao longo do meio-fio real entre os ramos (concordâncias, by-pass) – o círculo teórico ficaria solto
            // no asfalto. Anel: na borda do anel.
            var real = d.Integration == IntegracaoRotatoria.Completa ? CurbEdgeChains(d, L, c) : null;
            if (real != null)
                foreach (var (pts, sgn) in real)
                    Add(new LinearMarkingDefinition { Code = "LBO", Offset = sgn * 0.15, PathRef = PathReference.FromPoints(pts, z, false) });
            else
            {
                var inward = SignedArea(L.Outer.Outer) >= 0 ? 0.15 : -0.15;
                foreach (var (pts, closed) in RingEdgeChains(L, c, reach))
                    Add(new LinearMarkingDefinition { Code = "LBO", Offset = inward, PathRef = PathReference.FromPoints(pts, z, closed) });
            }
        }

        // ---- setas de movimento em curva (IMC) no anel, após cada entrada (uma por faixa)
        if (d.Markings && d.RingArrows)
            foreach (var g in L.Legs)
            {
                var dirA = Dir(g.Leg.AngleDeg + 28);
                var rOut = Ray(L.Outer, c, dirA);
                if (rOut <= 0) continue;
                for (int q = 0; q < Math.Max(1, d.Lanes); q++)
                {
                    var r = rOut - (d.Lanes - q - 0.5) * d.LaneWidth;
                    Add(new SymbolMarkingDefinition { Code = "IMC", Length = v > 60 ? 6.0 : 4.5, Position = c + dirA * r, Direction = dirA.PerpLeft, Z = z });
                }
            }

        var islandR = d.IslandRadius * k;
        var dirSigns = d.DirectionSigns == PlacaSentidoRotatoria.Automatico
            ? (islandR < 12 || painted ? PlacaSentidoRotatoria.R33NasEntradas : PlacaSentidoRotatoria.R24aNaIlha)
            : d.DirectionSigns;

        foreach (var g in L.Legs)
        {
            var leg = g.Leg;
            var hw = leg.Width / 2;
            var splitterW = g.Splitter != null || g.PaintedSplitter != null ? Math.Min(d.SplitterWidth, leg.Width - 6) : 0;
            var inner = Math.Max(splitterW > 0 ? splitterW / 2 + 0.2 : 0.1, leg.MedianWidth > 0 ? leg.MedianWidth / 2 + 0.2 : 0);
            // Entrada = lado esquerdo do ramo (quem chega pela direita da pista circula no sentido anti-horário).
            var rou = L.ToOuter(c, g.Dir);
            var splitLen = Math.Max(3, leg.SplitterLength ?? d.SplitterLength);
            var control = leg.Control == ControleRamo.Padrao ? ControleRamo.DeAPreferencia : leg.Control;
            var crosswalk = leg.Crosswalk ?? d.Crosswalks;
            var signs = leg.Signs ?? d.Signs;

            if (d.Markings)
            {
                var o0 = inner;
                // No anel pequeno (mini/compacta sobre via larga) a linha não pode atravessar o anel até o outro ramo.
                var o1 = Math.Min(hw - 0.3, 0.8 * rou);
                if (o1 < o0 + 1) o1 = o0 + 1;
                double T(double o) => L.ToOuter(c + g.Left * o, g.Dir) + 0.4;
                if (control == ControleRamo.Pare)
                {
                    // Parada obrigatória: linha de retenção + legenda PARE (≥ 1,60 m antes) + R-1.
                    Add(new LinearMarkingDefinition { Code = "LRE", Variant = "0,40 m", PathRef = PathReference.FromPoints(new[] { g.At(c, T(o1), o1), g.At(c, T(o0), o0) }, z) });
                    Add(new TextMarkingDefinition { Text = "PARE", Height = DesignRules.LegendHeight(v), Position = g.At(c, rou + 2.4 + DesignRules.LegendHeight(v) / 2, (o0 + o1) / 2), Direction = -g.Dir, Z = z });
                }
                else
                {
                    Add(new LinearMarkingDefinition { Code = "LDP", Variant = "0,40 m (0,60 × 0,60 m)", PathRef = PathReference.FromPoints(new[] { g.At(c, T(o1), o1), g.At(c, T(o0), o0) }, z) });
                    var sym = DesignRules.YieldSymbolLength(v);
                    Add(new SymbolMarkingDefinition { Code = "SDP", Length = sym, Position = g.At(c, rou + 1.6 + sym / 2, (o0 + o1) / 2), Direction = -g.Dir, Z = z });
                }
                // Ilha física: a linha dupla amarela (fluxos opostos) chega à ponta da ilha – sem zebrado branco estreito
                // sobreposto à linha amarela (MBST Vol. IV: a canalização entre fluxos opostos é amarela).
                if (g.PaintedSplitter is { } ps)
                {
                    // Gota pintada: zebrado com linha de canalização – amarelo entre fluxos opostos, branco em mão única.
                    var col = leg.TwoWay ? MarkingColor.Amarela : MarkingColor.Branca;
                    Add(new HatchMarkingDefinition
                    {
                        Code = "ZPA", BarWidth = 0.50, Gap = DesignRules.ChannelHatchGap(v), BorderWidth = 0.20, BarColor = col, BorderColor = col,
                        ReferenceDirection = -g.Dir, AxisPoint = g.At(c, rou, 0),
                        Boundary = PathReference.FromPoints(ps.Outer, z, true),
                    });
                }
                if (d.ApproachDoubleLine && leg.TwoWay && d.Integration == IntegracaoRotatoria.Completa && leg.MedianWidth < 0.8)
                {
                    // LFO-3 entre a ponta da ilha separadora e o fim da zona remodelada (aproximação sem ultrapassagem).
                    var t0 = (g.Splitter != null || g.PaintedSplitter != null ? rou + splitLen : rou + 2.0) + 0.3;
                    var t1 = L.ToOuter(c, g.Dir) + Math.Max(0, L.LegLength) - 0.3;
                    var zoneT = Ray(L.Zone, c, g.Dir);
                    if (zoneT > t0 + 2) t1 = zoneT - 0.3;
                    if (t1 > t0 + 1) Add(new LinearMarkingDefinition { Code = "LFO-3", PathRef = PathReference.FromPoints(new[] { g.At(c, t0, 0), g.At(c, t1, 0) }, z) });
                }
            }
            if (crosswalk)
            {
                // Mesma estaca do arranjo (recuada quando as rampas não cabiam junto à curva de entrada).
                var tc = g.CrosswalkT > rou ? g.CrosswalkT : rou + Math.Max(2, d.CrosswalkDistance);
                // De calçada a calçada: com by-pass a travessia cruza também a faixa de conversão livre (refúgio na ilha).
                var (lo, hi) = g.CrosswalkSpan ?? (-hw, hw);
                // Pontas da faixa: os dois cantos da pintura dentro da pista – junto ao meio-fio curvo do by-pass a ponta reta
                // a 0,30 m do meio-fio (medida no eixo da faixa) ainda saía da pista num dos cantos.
                var cwHalf = Math.Clamp(d.CrosswalkWidth, 2, 10) / 2;
                var road = L.Pavement.Concat(L.Refuges).Concat(L.Apron).ToList();
                double End(double o0, double sgn)
                {
                    var o = o0;
                    for (int k = 0; k < 80 && !(road.Any(p => p.Contains(g.At(c, tc - cwHalf, o))) && road.Any(p => p.Contains(g.At(c, tc + cwHalf, o)))); k++) o -= sgn * 0.05;
                    return o;
                }
                var a = g.At(c, tc, End(hi - 0.3, 1));
                var b = g.At(c, tc, End(lo + 0.3, -1));
                var cwk = Add(new LinearMarkingDefinition { Code = "FTP-1", WidthOverride = Math.Clamp(d.CrosswalkWidth, 2, 10), Overlay = true,
                    PathRef = PathReference.FromPoints(new[] { a, b }, z) });
                foreach (var isl in L.Refuges.Append(g.Splitter).Where(p => p != null).Distinct())
                    if (DetailGenerator.SegmentIntervals(isl!, a, b).Any())
                        cwk.Exclusions.Add(new ExclusionZone { SourceId = d.Id, Points = isl!.Outer.ToList() });
                if ((leg.Sidewalk > 0.5 || d.SidewalkWidth > 0.5) && d.Integration == IntegracaoRotatoria.Completa)
                    foreach (var (sgn, o, frame) in new[] { (1.0, hi, g.RampHi), (-1.0, lo, g.RampLo) })
                    {
                        // Ajustada ao meio-fio no arranjo (perpendicular a ele, com ou sem abas); sem ajuste, perpendicular ao ramo.
                        var (curb, up) = frame ?? (g.At(c, tc, o), g.Left * sgn);
                        var ramp = RampOf(d, g.RampType);
                        ramp.PathRef = PathReference.FromPoints(new[] { curb, curb + up }, z);
                        Add(ramp);
                    }
            }
            if (signs)
            {
                var sx = rou + 3.0;
                // R-1 ou R-2 no lado direito de quem chega (1,5 a 15 m da borda do anel); R-33 no mesmo suporte, acima.
                Add(new SignDefinition { Code = control == ControleRamo.Pare ? "R-1" : "R-2", Position = g.At(c, sx, hw + 0.9), Direction = -g.Dir, Z = z, Width = 0.75 });
                if (dirSigns is PlacaSentidoRotatoria.R33NasEntradas or PlacaSentidoRotatoria.Ambas)
                    Add(new SignDefinition { Code = "R-33", Position = g.At(c, sx, hw + 0.9), Direction = -g.Dir, Z = z, Width = 0.60, MountHeight = 2.10 + 0.75 + 0.10, Support = TipoSuporte.Nenhum });
                if (dirSigns is PlacaSentidoRotatoria.R24aNaIlha or PlacaSentidoRotatoria.Ambas && !painted)
                {
                    var edge = Ray(L.Island, c, g.Dir);
                    if (edge > 1.5) Add(new SignDefinition { Code = "R-24a", Position = c + g.Dir * (edge - 0.8), Direction = -g.Dir, Z = z, Width = 0.60, BaseElevation = d.CurbHeight });
                }
                if (d.AdvanceWarning)
                {
                    // A-12 com "A ... m": à distância de desaceleração (MBST Vol. II) antes da entrada.
                    var dist = DesignRules.WarningDistance(v);
                    var pa = g.At(c, rou + dist, hw + 0.9);
                    Add(new SignDefinition { Code = "A-12", Position = pa, Direction = -g.Dir, Z = z, Width = 0.60 });
                    Add(new SignDefinition { Code = "INF-DIST", Position = pa, Direction = -g.Dir, Z = z, Width = 0.60, Height = 0.25, Legend = $"A {dist} m",
                        MountHeight = 2.10 - 0.25 - 0.05, Support = TipoSuporte.Nenhum });
                }
                if (d.AlignmentMarkers && !painted)
                {
                    var edge = Ray(L.Island, c, g.Dir);
                    foreach (var o in new[] { -1.2, 1.2 })
                        Add(new SignDefinition { Code = "MA-ALIN", Position = c + g.Dir * (edge - 0.45) + g.Left * o, Direction = -g.Dir, Z = z, MountHeight = 0.80, BaseElevation = d.CurbHeight });
                }
            }
        }
        return res;
    }

    /// <summary>Ramos a partir das vias que passam pelo centro (ou terminam junto a ele).</summary>
    public static List<RoundaboutLeg> LegsFromRoads(Vec2 center, IReadOnlyList<IntersectionRoad> roads, double reach)
    {
        var legs = new List<RoundaboutLeg>();
        foreach (var r0 in roads)
        {
            var r = r0.LocalAt(center);
            var (s, dist, _) = IntersectionGenerator.Project(r.Axis, center);
            if (dist > Math.Max(r.Def.TotalLeft, r.Def.TotalRight) + 2) continue;
            foreach (var sign in new[] { -1, 1 })
            {
                var avail = sign > 0 ? r.Axis.Length - s : s;
                if (avail < reach * 0.6) continue;
                var p = r.Axis.PointAt(s + sign * Math.Min(avail, reach));
                var v = p - center;
                if (v.Length < 1) continue;
                legs.Add(new RoundaboutLeg
                {
                    AngleDeg = Math.Atan2(v.Y, v.X) * 180 / Math.PI,
                    Width = r.Def.RightWidth + r.Def.LeftWidth,
                    Sidewalk = Math.Max(r.Def.RightSidewalk, r.Def.LeftSidewalk),
                    RoadId = r.Def.Id,
                    GroupId = r.Def.GroupId,
                    TwoWay = r.Def.TwoWay,
                    MedianWidth = r.Def.Gaps.Where(g => g.Median && Math.Abs(g.Offset) < 0.5).Select(g => g.Width).DefaultIfEmpty(0).Max(),
                });
            }
        }
        return legs;
    }

    /// <summary>
    /// Ramos redetectados a partir das vias mantêm a personalização feita pelo usuário (ilha, travessia, raios,
    /// controle, placas): cada ramo novo herda do ramo antigo mais próximo em ângulo (até 25°) ou da mesma via.
    /// </summary>
    public static List<RoundaboutLeg> MergeLegSettings(IReadOnlyList<RoundaboutLeg> old, List<RoundaboutLeg> fresh)
    {
        foreach (var f in fresh)
        {
            var best = old.Where(o => o.RoadId != null && o.RoadId == f.RoadId && AngleDiff(o.AngleDeg, f.AngleDeg) < 90)
                .Concat(old.Where(o => AngleDiff(o.AngleDeg, f.AngleDeg) < 25))
                .OrderBy(o => AngleDiff(o.AngleDeg, f.AngleDeg)).FirstOrDefault();
            if (best != null) f.CopySettingsFrom(best);
        }
        return fresh;
    }

    private static double AngleDiff(double a, double b)
    {
        var d = Math.Abs(Norm(a) - Norm(b));
        return Math.Min(d, 360 - d);
    }

    /// <summary>Pistas de exemplo dos ramos (pré-visualização dos modos que não geram a pista dos ramos).</summary>
    public static List<Polygon2> PreviewRoads(RoundaboutDefinition d, RoundaboutLayout L)
    {
        var c = d.Center;
        var reach = L.Outer.Outer.Max(p => p.DistanceTo(c)) + 30;
        var res = new List<Polygon2>();
        foreach (var g in L.Legs)
            res.AddRange(PolygonOps.Strip(new[] { c, c + g.Dir * reach }, Math.Max(3, g.Leg.Width)));
        return PolygonOps.Union(res);
    }
}
