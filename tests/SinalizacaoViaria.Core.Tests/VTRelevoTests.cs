using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
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
        foreach (var f in floors) Assert.True(f.Part.Area >= 0.01, $"{f.Def.GetType().Name}: lasca de {f.Part.Area:0.0000} m²");
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
}
