using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada VT: vias e cruzamentos no relevo comum (rampas constantes de 2, 5 e 8 % na diagonal e ondulação suave), montados como
/// o plugin faz no Revit. A modelagem fica limpa em qualquer inclinação: a via principal atravessa o nó com o próprio greide e
/// a secundária fica em nível só dentro da interseção.
/// </summary>
public class VTRelevoTests
{
    public static readonly string[] Terrains = { "incl2", "incl5", "incl8", "suave" };

    /// <summary>Cruz e T entre locais (a primeira via é a principal: empate de hierarquia e largura), coletora × local e local × coletora (a via nova é a principal).</summary>
    public static readonly (string Kind, string A, string B)[] Layouts =
    {
        ("cruz", "local2", "local2"), ("T", "local2", "local2"), ("cruz", "coletora4", "local2"), ("cruz", "local2", "coletora4"),
    };

    public static IEnumerable<object[]> Matrix()
    {
        foreach (var t in Terrains)
            foreach (var (k, a, b) in Layouts)
                yield return new object[] { t, k, a, b };
    }

    private static readonly Dictionary<string, TopoScene.Scene> Cache = new();

    internal static TopoScene.Scene Get(string t, string k, string a, string b)
    {
        var key = $"{t}|{k}|{a}|{b}";
        lock (Cache)
            if (Cache.TryGetValue(key, out var sc)) return sc;
        var made = TopoScene.Make(t, k, a, b, 90);
        lock (Cache) Cache[key] = made;
        return made;
    }

    /// <summary>
    /// Greide no cruzamento: a via principal do nó (a mesma da superfície do nó) tem exatamente o greide que teria sozinha –
    /// sem patamar – e a secundária fica em nível na cota da boca do nó (a da principal menos o abaulamento) em toda a área
    /// da interseção. Antes as duas vias ganhavam um
    /// patamar de 62 m mesmo em rampa constante (até 1,3 m fora do terreno nas pontas dele).
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void V1_PrincipalAtravessaComOProprioGreide_SecundariaEmNivelNoNo(string terrain, string kind, string a, string b)
    {
        var sc = Get(terrain, kind, a, b);
        var w = sc.World;
        var ground = TopoScene.Terrains[terrain];
        foreach (var n in w.Nodes)
        {
            var it = n.Layout.Definition!;
            var node = sc.Nodes[it.Id];
            var mi = w.Roads.FindIndex(r => r.Def.Id == node.Major!.Id);
            Assert.True(mi >= 0);
            // Principal: o greide de quando ela está sozinha (o mesmo projeto, sem cotas obrigadas).
            var ax = w.Roads[mi].Axis;
            var alone = RoadProfileDesigner.Design(ax.Length, s => ground(ax.PointAt(s)), RoadProfileDesigner.ForRelief(RelevoVia.AcompanharTerreno, 0.08, 1.0, 1.5)).Grade;
            for (var s = 0.0; s <= ax.Length; s += 1)
            {
                var d = Math.Abs(sc.Grades[mi].Z(s) - alone.Z(s));
                Assert.True(d < 0.001, $"{terrain}/{kind}/{a}x{b}: via principal {mi} desviada {d * 1000:0} mm do próprio greide em {s:0} m");
            }
            // Secundárias: na cota da principal no nó e em nível em toda a área dele.
            for (int i = 0; i < w.Roads.Count; i++)
            {
                if (i == mi || !it.RoadIds.Contains(w.Roads[i].Def.Id)) continue;
                var axi = w.Roads[i].Axis;
                var c = Math.Clamp(axi.Project(it.Node).Station, 0, axi.Length);
                var sm = Math.Clamp(ax.Project(it.Node).Station, 0, ax.Length);
                var zMajor = RoadProfileDesigner.MouthZ(sc.Grades[mi], sm, w.Roads[mi].Def);
                var slope = RoadProfileDesigner.NodeSlope(sc.Grades[mi], sm, ax.Length, ax.TangentAt(sm), axi.TangentAt(c));
                var reach = RoadProfileDesigner.NodeReach(axi, it.Node,
                    w.Roads[i].Def.Exclusions.Where(e => e.SourceId == it.Id).Select(e => (IReadOnlyList<Vec2>)e.Points));
                Assert.True(reach > 1, "sem recorte do nó na secundária");
                for (var d = -reach; d <= reach + 1e-9; d += 0.5)
                {
                    var s = c + d;
                    if (s < 0 || s > axi.Length) continue;
                    var dz = Math.Abs(sc.Grades[i].Z(s) - zMajor - slope * d);
                    Assert.True(dz < 0.005, $"{terrain}/{kind}/{a}x{b}: secundária {i} a {dz * 1000:0} mm da cota da boca do nó em {s:0.0} m (nó em {c:0.0} m)");
                }
            }
        }
    }

    internal static double Perimeter(Polygon2 p)
    {
        static double Ring(IReadOnlyList<Vec2> r) { var s = 0.0; for (int i = 0; i < r.Count; i++) s += r[i].DistanceTo(r[(i + 1) % r.Count]); return s; }
        return Math.Max(1e-9, Ring(p.Outer) + p.Holes.Sum(Ring));
    }

    /// <summary>Pavimento (asfalto, bloquete, concreto, terra) – o resto é calçada, meio-fio, sarjeta, grama ou pintura.</summary>
    internal static bool IsPavement(MarkingColor c) => c is MarkingColor.Asfalto or MarkingColor.Bloquete or MarkingColor.PavimentoConcreto or MarkingColor.PavimentoTerra;

    /// <summary>Topo do piso (m): plano da parte + cota e espessura da peça (pintura: a cota de baixo + 3 mm).</summary>
    internal static double Top((MarkingDefinition Def, MarkingPiece Piece, Polygon2 Merged, Polygon2 Part, Plane3 Plane, bool Warped, Func<Vec2, double> Z) f, Vec2 p) =>
        f.Plane.Z(p) + f.Piece.Elevation + f.Piece.Thickness;

    /// <summary>
    /// Degraus entre partes de piso que se tocam: ao longo da borda de cada parte (a cada 0,4 m), a diferença de topo para a
    /// parte vizinha (3 cm para fora) menos a diferença nominal (cota + espessura das peças).
    /// </summary>
    internal static List<(Vec2 P, double Step, string What)> Steps(List<(MarkingDefinition Def, MarkingPiece Piece, Polygon2 Merged, Polygon2 Part, Plane3 Plane, bool Warped, Func<Vec2, double> Z)> floors)
    {
        var res = new List<(Vec2, double, string)>();
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < floors.Count; i++)
        {
            var (mn, mx) = floors[i].Part.Bounds;
            for (var x = (int)Math.Floor(mn.X / 4); x <= (int)Math.Floor(mx.X / 4); x++)
                for (var y = (int)Math.Floor(mn.Y / 4); y <= (int)Math.Floor(mx.Y / 4); y++)
                {
                    if (!grid.TryGetValue((x, y), out var l)) grid[(x, y)] = l = new List<int>();
                    l.Add(i);
                }
        }
        for (int i = 0; i < floors.Count; i++)
        {
            var a = floors[i];
            var ring = a.Part.Outer;
            var ccw = Polygon2.SignedArea(ring) > 0;
            for (int k = 0; k < ring.Count; k++)
            {
                var p0 = ring[k];
                var p1 = ring[(k + 1) % ring.Count];
                var len = p0.DistanceTo(p1);
                if (len < 1e-3) continue;
                var dir = (p1 - p0) * (1 / len);
                var outN = ccw ? dir.PerpRight : dir.PerpLeft;
                for (var t = 0.1; t < len - 0.05; t += 0.4)
                {
                    var q = p0 + dir * t;
                    var probe = q + outN * 0.03;
                    if (!grid.TryGetValue(((int)Math.Floor(probe.X / 4), (int)Math.Floor(probe.Y / 4)), out var cand)) continue;
                    foreach (var j in cand)
                    {
                        if (j == i || !floors[j].Part.Contains(probe)) continue;
                        var b = floors[j];
                        var nominal = a.Piece.Elevation + a.Piece.Thickness - b.Piece.Elevation - b.Piece.Thickness;
                        var step = Math.Abs(Top(a, q) - Top(b, q) - nominal);
                        res.Add((q, step, $"{a.Def.GetType().Name}/{a.Piece.Color} × {b.Def.GetType().Name}/{b.Piece.Color}"));
                    }
                }
            }
        }
        return res;
    }

    /// <summary>
    /// Pisos limpos: todos os pisos da cena (pista, sarjeta, meio-fio, calçada, grama, pintura) usam a mesma malha de juntas e
    /// os mesmos planos – nenhum degrau entre partes vizinhas acima de 1 mm no pavimento da interseção (2 mm no resto) e nenhuma
    /// lasca. Antes cada piso era dividido por conta própria: degraus de até 43 mm entre vizinhos.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void V2_PisosSemDegrauNemLasca(string terrain, string kind, string a, string b)
    {
        var sc = Get(terrain, kind, a, b);
        var floors = TopoScene.Floors(sc).Where(f => f.Piece.Solid == null && f.Piece.Profile == null).ToList();
        Assert.NotEmpty(floors);
        // Lascas: nenhuma parte de piso com menos de 0,01 m² ou mais fina que 2 cm; na pintura (faixas cortadas nas juntas, cada
        // pedaço no plano da própria face), nenhuma mais fina que 3 mm.
        foreach (var f in floors)
        {
            var thick = 2 * f.Part.Area / Perimeter(f.Part);
            if (MarkingColors.IsPaint(f.Piece.Color)) Assert.True(thick >= 0.003, $"{f.Def.GetType().Name}: fresta de pintura de {thick * 1000:0.0} mm");
            else Assert.True(f.Part.Area >= 0.01 && thick >= 0.0199, $"{f.Def.GetType().Name}: lasca de {f.Part.Area:0.0000} m² ({thick * 100:0.0} cm)");
        }
        var node = sc.World.Nodes[0].Layout.Definition!.Node;
        foreach (var (p, step, what) in Steps(floors))
        {
            var pav = what.Split(" × ").All(w => IsPavement(Enum.Parse<MarkingColor>(w.Split('/')[1])));
            var lim = pav && p.DistanceTo(node) < 40 ? 0.001 : 0.002;
            Assert.True(step <= lim, $"{terrain}/{kind}/{a}x{b}: degrau de {step * 1000:0.0} mm entre {what} em {p}");
        }
    }

    /// <summary>
    /// Menos juntas: em rampa constante a pista da via principal e o pavimento do nó ficam em poucos planos grandes – cada
    /// peça de pavimento da principal vira no máximo 2 partes (uma por caimento do abaulamento, de ponta a ponta) e o do nó no
    /// máximo 4 (os dois caimentos e as bocas além da pista). As juntas ficam só onde o greide ou a seção dobram (curvas verticais
    /// e a concordância da secundária). Antes eram 40 a 106 partes de pavimento só em volta do cruzamento.
    /// </summary>
    [Theory]
    [InlineData("incl2", "cruz", "local2", "local2")]
    [InlineData("incl5", "cruz", "local2", "local2")]
    [InlineData("incl8", "T", "local2", "local2")]
    [InlineData("incl5", "cruz", "coletora4", "local2")]
    [InlineData("incl5", "cruz", "local2", "coletora4")]
    public void V3_RampaConstante_PrincipalENoEmPoucosPlanos(string terrain, string kind, string a, string b)
    {
        var sc = Get(terrain, kind, a, b);
        var it = sc.World.Nodes[0].Layout.Definition!;
        var mi = sc.World.Roads.FindIndex(r => r.Def.Id == sc.Nodes[it.Id].Major!.Id);
        var floors = TopoScene.Floors(sc).Where(f => IsPavement(f.Piece.Color)).ToList();
        foreach (var g in floors.Where(f => ReferenceEquals(f.Def, sc.World.Roads[mi].Def)).GroupBy(f => f.Merged))
            Assert.True(g.Count() <= 2, $"{terrain}/{kind}/{a}x{b}: pista da principal ({g.Key.Area:0} m²) em {g.Count()} partes");
        foreach (var g in floors.Where(f => f.Def.Id == it.Id).GroupBy(f => f.Merged))
            Assert.True(g.Count() <= 4, $"{terrain}/{kind}/{a}x{b}: pavimento do nó ({g.Key.Area:0} m²) em {g.Count()} partes");
    }

    /// <summary>
    /// Pintura sobre o pavimento: em todo ponto pintado (malha de 0,1 m e contorno), o fundo da pintura fica sobre o topo do
    /// pavimento embaixo, com folga entre 0 e 5 mm – a pintura usa os mesmos planos do pavimento e, onde uma lasca dela é
    /// juntada à vizinha, assenta na face mais alta. Antes a pintura como piso era dividida por conta própria: até 37 mm
    /// enterrada e 29 mm flutuando.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void V4_PinturaSobreOPavimento_SemEnterrarNemFlutuar(string terrain, string kind, string a, string b)
    {
        var sc = Get(terrain, kind, a, b);
        var floors = TopoScene.Floors(sc).Where(f => f.Piece.Solid == null && f.Piece.Profile == null).ToList();
        var pav = floors.Where(f => IsPavement(f.Piece.Color)).ToList();
        var paint = floors.Where(f => MarkingColors.IsPaint(f.Piece.Color)).ToList();
        Assert.NotEmpty(paint);
        var n = 0;
        foreach (var p in paint)
        {
            var (mn, mx) = p.Part.Bounds;
            var c = p.Part.Centroid;
            var pts = p.Part.Outer.Select(v => v + (c - v) * 0.02).ToList();
            for (var x = mn.X + 0.05; x < mx.X; x += 0.1)
                for (var y = mn.Y + 0.05; y < mx.Y; y += 0.1)
                    if (p.Part.Contains(new Vec2(x, y))) pts.Add(new Vec2(x, y));
            foreach (var v in pts)
            {
                var under = pav.FirstOrDefault(f => f.Part.Contains(v));
                if (under.Def == null) continue;
                var gap = p.Plane.Z(v) + p.Piece.Elevation - Top(under, v);
                n++;
                Assert.True(gap >= -1e-4 && gap <= 0.005, $"{terrain}/{kind}/{a}x{b}: pintura {p.Def.GetType().Name} a {gap * 1000:0.0} mm do pavimento em {v}");
            }
        }
        Assert.True(n > 1000);
    }

    /// <summary>Meio-fio (guia) – linhas e peças da camada MEIO-FIO.</summary>
    private static bool IsCurb((MarkingDefinition Def, MarkingPiece Piece, Polygon2 Merged, Polygon2 Part, Plane3 Plane, bool Warped, Func<Vec2, double> Z) f) =>
        f.Def is LinearMarkingDefinition l && l.Code.StartsWith("MEIO-FIO") || f.Piece.Layer?.StartsWith("MEIO-FIO") == true;

    /// <summary>
    /// Meio-fio com altura constante, inclusive nas curvas das esquinas: ao longo de toda a guia, o topo dela fica acima do topo
    /// do pavimento ou da sarjeta encostados exatamente a altura nominal (cota + espessura das peças), ±5 mm.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void V5_MeioFioComAlturaConstante(string terrain, string kind, string a, string b)
    {
        var sc = Get(terrain, kind, a, b);
        var floors = TopoScene.Floors(sc).Where(f => f.Piece.Solid == null && f.Piece.Profile == null && !MarkingColors.IsPaint(f.Piece.Color)).ToList();
        var curbs = floors.Where(IsCurb).ToList();
        var low = floors.Where(f => !IsCurb(f) && f.Piece.Elevation + f.Piece.Thickness < 0.05).ToList();
        Assert.NotEmpty(curbs);
        var n = 0;
        foreach (var c in curbs)
        {
            var ring = c.Part.Outer;
            for (int k = 0; k < ring.Count; k++)
            {
                var p0 = ring[k];
                var p1 = ring[(k + 1) % ring.Count];
                var len = p0.DistanceTo(p1);
                if (len < 0.05) continue;
                var dir = (p1 - p0) * (1 / len);
                for (var t = 0.02; t < len; t += 0.25)
                {
                    var q = p0 + dir * t;
                    foreach (var side in new[] { dir.PerpLeft, dir.PerpRight })
                    {
                        var probe = q + side * 0.03;
                        if (c.Part.Contains(probe)) continue;
                        var nb = low.FirstOrDefault(f => f.Part.Contains(probe));
                        if (nb.Def == null) continue;
                        var h = Top(c, q) - Top(nb, q);
                        var nominal = c.Piece.Elevation + c.Piece.Thickness - nb.Piece.Elevation - nb.Piece.Thickness;
                        n++;
                        Assert.True(Math.Abs(h - nominal) <= 0.005, $"{terrain}/{kind}/{a}x{b}: meio-fio com {h * 100:0.0} cm (nominal {nominal * 100:0.0}) em {q}");
                    }
                }
            }
        }
        Assert.True(n > 100);
    }

    /// <summary>
    /// Rampas encaixadas: as peças sólidas das rampas, levantadas pelas faces dos pisos (como o plugin), encostam na calçada no
    /// topo e na sarjeta/pista no meio-fio a até 5 mm – sem fresta nem degrau –, e ficam na inclinação de projeto em relação a
    /// essas faces. Antes subiam pela superfície teórica, a até 2 cm das faces planas dos pisos em volta.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void V6_RampasEncaixadasNosPisos(string terrain, string kind, string a, string b)
    {
        var sc = Get(terrain, kind, a, b);
        var floors = TopoScene.Floors(sc).Where(f => f.Piece.Solid == null && f.Piece.Profile == null && !MarkingColors.IsPaint(f.Piece.Color)).ToList();
        var ramps = sc.Parts.Where(x => x.Def is RampDefinition && x.Piece.Solid != null && !MarkingColors.IsPaint(x.Piece.Color)).ToList();
        Assert.NotEmpty(ramps);
        var n = 0;
        foreach (var (def, piece) in ramps)
        {
            var lift = TopoScene.LiftOf(sc, def)!;
            var r = (RampDefinition)def;
            // Só os vértices de cima (o mais alto em cada ponto da planta), no topo da calçada ou no meio-fio: os de baixo ficam
            // sob os pisos.
            var tops = piece.Solid!.Faces.SelectMany(f => f).GroupBy(v => (Math.Round(v.X, 3), Math.Round(v.Y, 3))).Select(g => g.MaxBy(v => v.Z));
            foreach (var v in tops)
            {
                if (Math.Abs(v.Z - r.Height) > 1e-3 && Math.Abs(v.Z - Math.Clamp(r.LipHeight, 0, r.Height * 0.5)) > 1e-3) continue;
                // A rampa sobe a partir da mesma face dos pisos em volta: cota de apoio da rampa = plano da face do piso vizinho
                // (as alturas nominais – calçada, sarjeta – ficam de fora da comparação).
                var z = lift(v.XY);
                var near = floors.Where(f => f.Part.Contains(v.XY) || f.Part.DistanceTo(v.XY) < 0.03).ToList();
                if (near.Count == 0) continue;
                var err = near.Min(f => Math.Abs(f.Plane.Z(v.XY) - z));
                n++;
                Assert.True(err <= 0.005, $"{terrain}/{kind}/{a}x{b}: rampa a {err * 1000:0.0} mm do piso vizinho em {v.XY} (z local {v.Z:0.000})");
            }
        }
        Assert.True(n > 0);
    }

    /// <summary>
    /// Rampas na inclinação da norma: em relação aos pisos em volta cada rampa fica exatamente na inclinação de projeto (8,33 %
    /// por padrão) e alinhada à faixa (a planta não muda com o relevo); a rampa da via na direção dela soma ou subtrai, e acima
    /// de 8,33 % no total (NBR 9050) o plugin avisa e diz o comprimento que resolve.
    /// </summary>
    [Theory]
    [InlineData("incl2")]
    [InlineData("incl8")]
    [InlineData("suave")]
    public void V7_RampasNaInclinacaoDaNorma_ComAvisoNaRuaInclinada(string terrain)
    {
        var sc = Get(terrain, "cruz", "local2", "local2");
        var flat = TopoScene.Make("plano", "cruz", "local2", "local2", 90);
        var ramps = sc.World.Defs.OfType<RampDefinition>().ToList();
        Assert.NotEmpty(ramps);
        var warned = 0;
        foreach (var r in ramps)
        {
            var path = new Polyline2(r.PathRef.Points);
            var (total, street, warning) = RampGenerator.SlopeOnRelief(r, path, TopoScene.LiftOf(sc, r)!);
            var (_, run, _) = RampGenerator.Dimensions(r);
            Assert.True(Math.Abs(total - street - r.Height / run) < 1e-6, $"{terrain}: rampa fora da inclinação de projeto em relação aos pisos");
            Assert.True(total <= r.Slope + 1e-3 + Math.Max(0, street), $"{terrain}: rampa a {total:P1}");
            Assert.Equal(total > 0.0833 + 1e-3, warning != null);
            if (warning != null) { warned++; Assert.Contains("NBR 9050", warning); }
            // Alinhada à faixa: a mesma planta do terreno plano.
            var r0 = flat.World.Defs.OfType<RampDefinition>().MinBy(x => x.PathRef.Points[0].DistanceTo(r.PathRef.Points[0]))!;
            Assert.True(r0.PathRef.Points[0].DistanceTo(r.PathRef.Points[0]) < 1e-3 && r0.PathRef.Points[^1].DistanceTo(r.PathRef.Points[^1]) < 1e-3);
        }
        // Na rua inclinada as rampas voltadas para a subida passam de 8,33 %: avisadas.
        Assert.True(warned > 0, $"{terrain}: nenhuma rampa avisada");
    }
}


/// <summary>
/// Rodada VT, fase 4: corte e aterro. A estrutura do pavimento só sob a pista; sob meio-fio e calçada o terreno fica no fundo
/// das lajes e, na borda externa, encontra o topo do acabamento – o talude parte dali; volumes conferidos com o cálculo
/// analítico e repartidos por via e interseção.
/// </summary>
public class VTTerraplenagemTests
{
    /// <summary>
    /// Terreno no acabamento: logo fora da borda externa dos pisos (5 cm) o Toposolid fica no topo do piso da borda, mais ou
    /// menos o que o talude sobe ou desce nesses 5 cm (corte 1:1, aterro 1,5:1) e 3 cm de folga. Antes ficava 55 cm abaixo do
    /// topo da calçada em toda a borda (plataforma 40 cm abaixo da pista e 30 cm além da calçada).
    /// </summary>
    [Theory]
    [MemberData(nameof(VTRelevoTests.Matrix), MemberType = typeof(VTRelevoTests))]
    public void V8_TerrenoEncontraOAcabamentoNaBordaExterna(string terrain, string kind, string a, string b)
    {
        var sc = VTRelevoTests.Get(terrain, kind, a, b);
        var floors = TopoScene.Floors(sc).Where(f => f.Piece.Solid == null && f.Piece.Profile == null).ToList();
        // Sólidos no chão (rampas) também cobrem o terreno: a borda deles não é borda externa.
        var solids = sc.Parts.Where(p => p.Piece.Solid != null && p.Piece.Elevation < 0.5).Select(p => p.Piece.Shape);
        var union = PolygonOps.Union(floors.Select(f => f.Part).Concat(solids)).ToList();
        const double d = 0.05;
        double lo = -d / TopoScene.Scene.Fill - 0.03, hi = d / TopoScene.Scene.Cut + 0.03;
        var n = 0;
        var worst = (D: 0.0, P: Vec2.Zero);
        foreach (var u in union)
            foreach (var ring in u.Holes.Prepend(u.Outer))
            {
                var dense = CurveTools.Densify(ring.Append(ring[0]).ToList(), 0.5);
                for (int i = 0; i + 1 < dense.Count; i++)
                {
                    var q = (dense[i] + dense[i + 1]) / 2;
                    var t = (dense[i + 1] - dense[i]).Normalized();
                    foreach (var nv in new[] { t.PerpLeft, t.PerpRight })
                    {
                        var outside = q + nv * d;
                        var inside = q - nv * 0.05;
                        if (union.Any(x => x.Contains(outside))) continue;
                        var top = floors.Where(f => f.Part.Contains(inside)).Select(f => (double?)VTRelevoTests.Top(f, q)).Max();
                        if (top == null || sc.Terrain.Z(outside) is not { } tz) continue;
                        n++;
                        var step = tz - top.Value;
                        if (step < lo || step > hi)
                            if (Math.Abs(step) > Math.Abs(worst.D)) worst = (step, q);
                    }
                }
            }
        Assert.True(n > 100, $"{terrain}/{kind}: poucas amostras na borda ({n})");
        Assert.True(worst.D == 0, $"{terrain}/{kind}/{a}x{b}: terreno {worst.D * 100:0.0} cm do topo do piso logo fora da borda em ({worst.P.X:0.0}; {worst.P.Y:0.0})");
    }

    /// <summary>
    /// Plataforma por camadas: o Toposolid refeito fica no fundo da estrutura sob a pista e no fundo das lajes sob meio-fio,
    /// calçada e grama (±5 mm). Antes ficava 40 cm abaixo da pista em toda a largura – um vão de 40 cm sob as calçadas.
    /// </summary>
    [Theory]
    [MemberData(nameof(VTRelevoTests.Matrix), MemberType = typeof(VTRelevoTests))]
    public void V9_EstruturaSoSobAPista_LajesNoTerreno(string terrain, string kind, string a, string b)
    {
        var sc = VTRelevoTests.Get(terrain, kind, a, b);
        var raisedChecked = 0;
        foreach (var (def, piece) in sc.Parts.Where(p => p.Piece.Elevation < 0.5 && p.Piece.Solid == null))
        {
            var zf = TopoScene.SurfaceOf(sc, def);
            if (zf == null) continue;
            var (mn, mx) = piece.Shape.Bounds;
            for (var x = Math.Ceiling(mn.X); x < mx.X; x += 1.5)
                for (var y = Math.Ceiling(mn.Y); y < mx.Y; y += 1.5)
                {
                    var p = new Vec2(x, y);
                    if (!piece.Shape.Contains(p) || piece.Shape.DistanceTo(p) < 0.3) continue;
                    if (TopoScene.PlatformWant(sc, piece, zf, p) is not { } want) continue;
                    if (piece.Elevation + piece.Thickness >= 0.05) raisedChecked++;
                    var tz = sc.Terrain.Z(p);
                    Assert.True(tz != null && Math.Abs(tz.Value - want) <= 0.005,
                        $"{terrain}/{kind}/{a}x{b} {def.GetType().Name}/{piece.Color}: Toposolid {tz:0.000} ≠ {want:0.000} em ({p.X:0.0}; {p.Y:0.0})");
                }
        }
        Assert.True(raisedChecked > 50, "poucas amostras sob as calçadas");
    }

    /// <summary>
    /// Taludes contínuos até o terreno: em toda a área alterada fora das plataformas (inclusive o pé e os cantos em cone das
    /// esquinas) o Toposolid refeito reproduz a superfície de projeto a até 5 cm.
    /// </summary>
    [Theory]
    [MemberData(nameof(VTRelevoTests.Matrix), MemberType = typeof(VTRelevoTests))]
    public void V10_TaludesContinuosAteOTerreno(string terrain, string kind, string a, string b)
    {
        var sc = VTRelevoTests.Get(terrain, kind, a, b);
        var d = sc.Design;
        var plat = TerrainRebuild.Mask(d.PlatformAreas.Concat(d.Pads.Select(p => p.Area)), 0.1);
        var worst = (D: 0.0, P: Vec2.Zero);
        var n = 0;
        for (var x = -100.0; x < 100; x += 0.5)
            for (var y = -100.0; y < 100; y += 0.5)
            {
                var p = new Vec2(x + 0.25, y + 0.25);
                if (plat(p) || d.DesignZ(p) is not { } z || sc.Terrain.Z(p) is not { } tz) continue;
                n++;
                if (Math.Abs(tz - z) > Math.Abs(worst.D)) worst = (tz - z, p);
            }
        Assert.True(n > 100, $"poucas amostras nos taludes ({n})");
        Assert.True(Math.Abs(worst.D) <= 0.05, $"{terrain}/{kind}/{a}x{b}: Toposolid {worst.D * 100:0.0} cm fora do talude em ({worst.P.X:0.00}; {worst.P.Y:0.00})");
    }

    /// <summary>Via reta (calçadas, abaulamento) com o greide a <paramref name="h"/> m do terreno plano – faixa como TerrainService.</summary>
    private static (GradingResult Design, RoadPavementDefinition Road) Straight(double h, double length = 120, double crossfall = 0.02)
    {
        var road = new RoadPavementDefinition { RightWidth = 4.0, LeftWidth = 4.0, RightSidewalk = 2.5, LeftSidewalk = 2.5, CurbHeight = 0.15 };
        road.Id = "via";
        var axis = new Polyline2(new[] { new Vec2(0, 0), new Vec2(length, 0) });
        var g = new RoadGrade { Crossfall = crossfall, CrossfallWidth = 4.0, Points = { new(0, h), new(length, h) } };
        var surf = new GradeSurface(axis, g);
        var corr = InfraRoads.RoadCorridors(axis, g.ToProfile(2), -road.TotalRight, road.TotalLeft, 0, length, 0.40, 1.0, 1.5, Array.Empty<MarkingDefinition>());
        InfraRoads.FitToSection(corr, road.TotalLeft, -road.TotalRight, surf, InfraRoads.Edges(road, axis), 0.40, InfraRoads.SectionBreaks(g));
        // Pontas em face (sem talude à frente): o volume fica só o do prisma da seção, que tem conta fechada.
        foreach (var c in corr) { c.SpillStart = c.SpillEnd = false; c.WallStart = c.WallEnd = true; c.Owner = road.Id; }
        return (Grading.Design(corr, Array.Empty<GradePad>(), _ => 0.0, 0, 60, 0.5, 4.0, 0.25), road);
    }

    /// <summary>
    /// Área da seção (m²) entre a plataforma por camadas e o terreno plano a <paramref name="h"/> m abaixo do greide (aterro
    /// h &gt; 0, corte h &lt; 0): pista com abaulamento e estrutura de 0,40 m, transições de 5 cm, calçadas no fundo da laje,
    /// topo da calçada no alinhamento e taludes de 1,5:1 (aterro) ou 1:1 (corte).
    /// </summary>
    private static double SectionArea(double h, double crossfall)
    {
        const double W = 4.0, S = 2.5, sub = 0.40, rise = 0.15, G = InfraRoads.LayerStep;
        // Superfície da via: h − cf·|y| na pista, h − cf·W além dela (calçadas em nível na transversal).
        double Surf(double y) => h - crossfall * Math.Min(Math.Abs(y), W);
        double Off(double y)
        {
            var d = Math.Abs(y) - W;
            if (d <= 0) return -sub;
            if (d < G) return -sub * (1 - d / G);
            return rise * Math.Clamp((d - (S - G)) / G, 0, 1);
        }
        // Integração numérica fina da plataforma (as funções são lineares por trechos: erro desprezível).
        var area = 0.0;
        const int N = 200_000;
        var half = W + S;
        for (int i = 0; i < N; i++)
        {
            var y = -half + (i + 0.5) * (2 * half / N);
            area += (Surf(y) + Off(y)) * (2 * half / N);
        }
        // Taludes dos dois lados, do topo da calçada até o terreno.
        var edge = Surf(half) + rise;
        var ratio = edge >= 0 ? 1.5 : 1.0;
        area += 2 * 0.5 * Math.Abs(edge) * Math.Abs(edge) * ratio * Math.Sign(edge);
        return area;
    }

    /// <summary>
    /// Volumes de corte e aterro conferidos com o cálculo analítico (≤ 1 %): via reta em aterro e em corte sobre terreno plano.
    /// </summary>
    [Theory]
    [InlineData(1.5)]
    [InlineData(3.0)]
    [InlineData(-1.5)]
    [InlineData(-3.0)]
    public void V11_VolumesConferemComOCalculoAnalitico(double h)
    {
        const double L = 120;
        var (design, road) = Straight(h, L);
        var want = Math.Abs(SectionArea(h, 0.02)) * L;
        var got = h > 0 ? design.FillM3 : design.CutM3;
        var other = h > 0 ? design.CutM3 : design.FillM3;
        Assert.True(Math.Abs(got - want) <= 0.01 * want, $"h = {h}: {got:0.0} m³ ≠ {want:0.0} m³ ({(got - want) / want:P2})");
        Assert.True(other <= 0.002 * want, $"h = {h}: volume do outro tipo {other:0.0} m³");
        // Repartição: tudo da via.
        Assert.True(design.ByOwner.TryGetValue(road.Id, out var v));
        Assert.True(Math.Abs(v.Cut - design.CutM3) < 1e-6 && Math.Abs(v.Fill - design.FillM3) < 1e-6);
    }

    /// <summary>
    /// Volumes por via e por interseção: a soma das partes é o total, cada via e cada interseção da cena tem a sua parte e a da
    /// interseção fica na área dela.
    /// </summary>
    [Theory]
    [InlineData("incl5", "cruz", "local2", "local2")]
    [InlineData("suave", "T", "local2", "local2")]
    [InlineData("incl8", "cruz", "coletora4", "local2")]
    public void V12_VolumesPorViaEInterseção_SomamOTotal(string terrain, string kind, string a, string b)
    {
        var sc = VTRelevoTests.Get(terrain, kind, a, b);
        var d = sc.Design;
        Assert.True(d.CutM3 + d.FillM3 > 10);
        var cut = d.ByOwner.Values.Sum(v => v.Cut);
        var fill = d.ByOwner.Values.Sum(v => v.Fill);
        Assert.True(Math.Abs(cut - d.CutM3) <= 1e-6 * Math.Max(1, d.CutM3) && Math.Abs(fill - d.FillM3) <= 1e-6 * Math.Max(1, d.FillM3),
            $"partes {cut:0.0}/{fill:0.0} ≠ total {d.CutM3:0.0}/{d.FillM3:0.0}");
        foreach (var r in sc.World.Roads) Assert.True(d.ByOwner.ContainsKey(r.Def.Id), $"via {r.Def.Id} sem volume");
        foreach (var n in sc.World.Nodes) Assert.True(d.ByOwner.ContainsKey(n.Layout.Definition!.Id), "interseção sem volume");
        Assert.True(d.ByOwner.Keys.All(k => sc.World.Roads.Any(r => r.Def.Id == k) || sc.World.Nodes.Any(n => n.Layout.Definition!.Id == k)));
    }
}
