using SinalizacaoViaria.Core.Automation;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;
using SinalizacaoViaria.Core.Model;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AL, item 3 (prioridade máxima): vias na topografia difícil – ondulação forte, degrau de 16 m, vale em V, crista e
/// terreno ruidoso – isoladas e em cruz, T e Y sobre eles. A modelagem fica limpa como no plano: greide sem saltos e dentro da
/// rampa máxima, passando na cota das outras vias nos cruzamentos; superfícies sem degrau entre via e nó; pisos planos que
/// seguem a superfície; plataforma da terraplenagem sob os pisos; Toposolid igual ao projeto; taludes nas inclinações.
/// </summary>
public class AL3TopographyTests
{
    public static readonly string[] Terrains = { "ondulado", "degrau", "vale", "crista", "ruido" };
    /// <summary>
    /// Seções (local 1+1, coletora 2+2, arterial com canteiro central, local de mão única) em cada arranjo – isolada, cruz, T e
    /// Y (a outra via, local).
    /// </summary>
    public static readonly (string Kind, string A, string B)[] Layouts =
    {
        ("isolada", "local2", "local2"), ("isolada", "coletora4", "coletora4"), ("isolada", "arterial", "arterial"), ("isolada", "local1", "local1"),
        ("cruz", "local2", "local2"), ("cruz", "coletora4", "coletora4"), ("cruz", "arterial", "local2"), ("cruz", "local1", "local2"),
        ("T", "local2", "local2"), ("T", "coletora4", "local2"), ("T", "arterial", "local2"), ("T", "local1", "local2"),
        ("Y", "local2", "local2"), ("Y", "coletora4", "local2"), ("Y", "arterial", "local2"), ("Y", "local1", "local2"),
    };

    /// <summary>Greide "acompanhar terreno" (padrão) na matriz toda e "greide suavizado" (rampa máx. 8 %) em parte dela.</summary>
    public static IEnumerable<object[]> Matrix()
    {
        foreach (var t in Terrains)
            foreach (var (k, a, b) in Layouts)
                yield return new object[] { t, k, a, b, nameof(RelevoVia.AcompanharTerreno) };
        foreach (var t in Terrains)
            foreach (var (k, a, b) in new[] { ("isolada", "local2", "local2"), ("cruz", "arterial", "local2"), ("T", "coletora4", "local2") })
                yield return new object[] { t, k, a, b, nameof(RelevoVia.GreideSuavizado) };
    }

    private static readonly Dictionary<string, TopoScene.Scene> Cache = new();
    private static TopoScene.Scene Get(string t, string k, string a, string b, string relevo)
    {
        var key = $"{t}|{k}|{a}|{b}|{relevo}";
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var sc)) return sc;
        }
        var made = TopoScene.Make(t, k, a, b, 90, Enum.Parse<RelevoVia>(relevo));
        lock (Cache) Cache[key] = made;
        return made;
    }

    /// <summary>
    /// Greide: dentro da rampa máxima em todo ponto (sem saltos), passando exatamente na cota das outras vias nos cruzamentos e em
    /// nível no miolo da interseção (±3 m; o patamar tem ±6 m e as curvas verticais entram até a metade dele).
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Grade_WithinMaxGrade_AndThroughTheCrossings(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        for (int i = 0; i < sc.Grades.Count; i++)
        {
            var g = sc.Grades[i];
            var L = sc.World.Roads[i].Axis.Length;
            var max = sc.MaxGrades[i];
            // Cotas obrigadas incompatíveis com a rampa máxima: vale a menor rampa possível entre elas (com aviso).
            var pins = sc.Pins[i].Select((p, k) => (P: p, H: Math.Max(RoadProfileDesigner.PlatformHalf, sc.Halves[i][k])))
                .OrderBy(x => x.P.X).ToList();
            for (int k = 0; k + 1 < pins.Count; k++)
                max = Math.Max(max, Math.Abs(pins[k + 1].P.Y - pins[k].P.Y) / Math.Max(1e-6, pins[k + 1].P.X - pins[k].P.X - pins[k].H - pins[k + 1].H));
            for (var s = 0.0; s < L - 0.5; s += 0.5)
            {
                var r = Math.Abs(g.Z(s + 0.5) - g.Z(s)) / 0.5;
                Assert.True(r <= max + 0.01, $"via {i}: rampa de {r:P0} em {s:0.0} m (máx. {max:P0})");
            }
            foreach (var (p, h) in pins)
            {
                Assert.True(Math.Abs(g.Z(p.X) - p.Y) < 0.01, $"via {i}: cruzamento em {p.X:0.0} m na cota {g.Z(p.X):0.000} (devia {p.Y:0.000})");
                // Em nível na metade interna do patamar (as curvas verticais das bordas entram até a metade dele).
                for (var d = -h / 2; d <= h / 2 + 1e-9; d += h / 4)
                    if (p.X + d > 0 && p.X + d < L)
                        Assert.True(Math.Abs(g.Z(p.X + d) - p.Y) < 0.02, $"via {i}: patamar do cruzamento fora de nível em {p.X + d:0.0} m");
            }
        }
    }

    /// <summary>
    /// Greide suave: as mudanças de rampa ficam em curvas verticais – a rampa não varia mais depressa que numa curva vertical do
    /// modo (40 m em "acompanhar terreno") indo de −rampa máxima a +rampa máxima. Antes o fundo do vale virava uma quina
    /// (−30 % a +30 % em 5 m) porque os PIVs ficavam tão perto que as curvas eram cortadas.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Grade_SmoothVerticalCurves(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        for (int i = 0; i < sc.Grades.Count; i++)
        {
            var g = sc.Grades[i];
            var L = sc.World.Roads[i].Axis.Length;
            var gm = sc.MaxGrades[i];
            for (var s = 0.0; s < L - 0.5; s += 0.5) gm = Math.Max(gm, Math.Abs(g.Z(s + 0.5) - g.Z(s)) / 0.5);
            var c = RoadProfileDesigner.MaxCurvature(gm, g.DefaultCurve);
            for (var s = 1.0; s <= L - 1; s += 0.5)
            {
                var k = Math.Abs(g.Z(s + 1) - 2 * g.Z(s) + g.Z(s - 1));
                Assert.True(k <= c * 1.05 + 1e-4, $"via {i}: rampa varia {k:P1} por metro em {s:0.0} m (máx. {c:P1})");
            }
        }
    }

    /// <summary>
    /// A via chega ao nó sem rampa extra: ao longo do eixo, a superfície por onde se anda (nó, concordância e greide) não fica
    /// mais íngreme que o greide mais íngreme da cena (+3 %). Antes o patamar do greide tinha só ±6 m e, num Y (a área do nó vai
    /// ~25 m ao longo do ramo), a concordância vencia metros de desnível em poucos metros: 55 % numa via de 30 % (crista).
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Grade_SurfaceAlongTheAxis_NoSteeperThanTheGrades(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        var gm = 0.0;
        for (int i = 0; i < sc.Grades.Count; i++)
        {
            var L = sc.World.Roads[i].Axis.Length;
            for (var s = 0.0; s < L - 0.5; s += 0.5) gm = Math.Max(gm, Math.Abs(sc.Grades[i].Z(s + 0.5) - sc.Grades[i].Z(s)) / 0.5);
        }
        for (int i = 0; i < sc.Surfaces.Count; i++)
        {
            var surf = sc.Surfaces[i];
            var ax = surf.Axis;
            for (var s = 0.5; s < ax.Length - 1; s += 0.5)
            {
                var r = Math.Abs(surf.Z(ax.PointAt(s + 0.5)) - surf.Z(ax.PointAt(s))) / 0.5;
                Assert.True(r <= gm + 0.03, $"via {i}: superfície com rampa de {r:P0} em {s:0.0} m (greides até {gm:P0})");
            }
        }
    }

    /// <summary>
    /// Interseção como um plano coerente: onde os pisos de cada via encostam nos do nó, a superfície da via é a do nó
    /// (≤ 5 mm) – a via chega sem degrau.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Seam_RoadsMeetTheNodeWithoutStep(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        var w = sc.World;
        foreach (var n in w.Nodes)
        {
            var it = n.Layout.Definition!;
            var node = sc.Nodes[it.Id];
            for (int i = 0; i < w.Roads.Count; i++)
            {
                var pav = w.Roads[i].Def;
                var floors = sc.Parts.Where(p => w.Groups[i].Contains(p.Def) && p.Piece.Elevation < 0.5).Select(p => p.Piece.Shape).ToList();
                if (floors.Count == 0) continue;
                var mask = TerrainRebuild.Mask(floors, 0.05);
                foreach (var ex in pav.Exclusions.Where(e => e.SourceId == it.Id))
                    foreach (var p in CurveTools.Densify(ex.Points.Append(ex.Points[0]).ToList(), 0.5))
                    {
                        if (!mask(p)) continue;
                        var d = Math.Abs(sc.Surfaces[i].Z(p) - node.Z(p));
                        Assert.True(d <= 0.005, $"via {i}: degrau de {d * 1000:0} mm com o nó em {p}");
                    }
            }
        }
    }

    /// <summary>
    /// Pisos sem deformação: cada piso vira partes planas (nenhuma empenada onde há eixo de referência), cada parte a até
    /// 1,2 × 2 cm da superfície em todo ponto (a tolerância de plano dos pisos do plugin; o plano "de corda", que passa
    /// exatamente na superfície nas juntas para as partes vizinhas se encontrarem sem degrau, pode usar até 1,2 × ela –
    /// FloorPlanes.Chord), nenhuma lasca (&lt; 0,01 m²) e a área total preservada.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Floors_PlanarOnTheSurface_NoWarpedOrTinyParts(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        var floors = TopoScene.Floors(sc);
        Assert.NotEmpty(floors);
        foreach (var f in floors)
        {
            var name = f.Def.GetType().Name + "/" + f.Piece.Layer;
            Assert.False(f.Warped, $"{name}: parte empenada");
            Assert.True(f.Part.Area >= 0.01, $"{name}: lasca de {f.Part.Area:0.0000} m²");
            foreach (var v in FloorPlanes.Samples(f.Part, f.Z, 1.0))
            {
                var d = Math.Abs(f.Plane.Z(v.XY) - v.Z);
                Assert.True(d <= 1.2 * TopoScene.FloorTolerance + 1e-3, $"{name}: parte plana a {d * 1000:0} mm da superfície em {v.XY}");
            }
        }
        foreach (var g in floors.GroupBy(f => f.Merged))
        {
            var sum = g.Sum(f => f.Part.Area);
            Assert.True(Math.Abs(sum - g.Key.Area) <= 0.001 * g.Key.Area + 0.01, $"{g.First().Def.GetType().Name}: área das partes {sum:0.00} ≠ {g.Key.Area:0.00} m²");
        }
    }

    /// <summary>
    /// Nada flutuando nem enterrado: sob todos os pisos da via e do nó o Toposolid refeito fica no fundo da estrutura do
    /// pavimento – superfície menos o subleito – a até 5 mm.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Platform_ToposolidUnderTheFloors(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        foreach (var (def, piece) in sc.Parts.Where(p => p.Piece.Elevation < 0.5))
        {
            var zf = TopoScene.SurfaceOf(sc, def);
            if (zf == null) continue;
            var (mn, mx) = piece.Shape.Bounds;
            for (var x = Math.Ceiling(mn.X); x < mx.X; x += 2)
                for (var y = Math.Ceiling(mn.Y); y < mx.Y; y += 2)
                {
                    var p = new Vec2(x, y);
                    if (!piece.Shape.Contains(p) || piece.Shape.DistanceTo(p) < 0.3) continue;
                    var want = zf(p) - TopoScene.Scene.Subgrade;
                    var dz = sc.Design.DesignZ(p);
                    Assert.True(dz != null && Math.Abs(dz.Value - want) <= 0.005, $"{def.GetType().Name}: plataforma {dz:0.000} ≠ {want:0.000} em {p}");
                    var tz = sc.Terrain.Z(p);
                    Assert.True(tz != null && Math.Abs(tz.Value - want) <= 0.005, $"{def.GetType().Name}: Toposolid {tz:0.000} ≠ {want:0.000} em {p}");
                }
        }
    }

    /// <summary>
    /// Taludes nas razões definidas (corte 1:1, aterro 1,5:1) e fechando no terreno sem buracos nem sobreposições: a
    /// superfície de projeto não é mais íngreme que o talude mais íngreme em nenhum ponto alterado fora das plataformas
    /// (paredões = sobreposição/buraco) e o Toposolid refeito a reproduz (≤ 5 cm no corpo do talude, ≤ 10 cm na faixa de
    /// 0,25 m do pé, onde a triangulação sem linhas de quebra corta o canto com o terreno).
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Slopes_AtTheirRatios_ClosingOnTheTerrain(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        var d = sc.Design;
        var plat = TerrainRebuild.Mask(d.PlatformAreas.Concat(d.Pads.Select(p => p.Area)), 0.3);
        var steepest = Math.Max(1 / TopoScene.Scene.Cut, 1 / TopoScene.Scene.Fill);
        var (bmn, bmx) = (new Vec2(-100, -100), new Vec2(100, 100));
        var n = 0;
        for (var x = bmn.X; x < bmx.X; x += 1)
            for (var y = bmn.Y; y < bmx.Y; y += 1)
            {
                var p = new Vec2(x, y);
                if (plat(p) || d.DesignZ(p) is not { } z0) continue;
                n++;
                var tz = sc.Terrain.Z(p);
                // No pé (a 0,25 m do terreno natural) a triangulação do Toposolid, sem linhas de quebra, corta o canto entre o
                // talude e o terreno: até 10 cm ali; no corpo do talude, 5 cm.
                var atToe = new[] { new Vec2(0.25, 0), new Vec2(-0.25, 0), new Vec2(0, 0.25), new Vec2(0, -0.25) }.Any(dv => d.DesignZ(p + dv) == null);
                var tol = atToe ? 0.10 : 0.05;
                Assert.True(tz != null && Math.Abs(tz.Value - z0) <= tol, $"Toposolid {tz:0.000} ≠ talude {z0:0.000} em {p}{(atToe ? " (pé)" : "")}");
                foreach (var q in new[] { p + new Vec2(0.25, 0), p + new Vec2(0, 0.25) })
                {
                    if (plat(q) || d.DesignZ(q) is not { } z1) continue;
                    var gr = Math.Abs(z1 - z0) / 0.25;
                    Assert.True(gr <= steepest * 1.05 + 0.05, $"talude com inclinação {gr:0.00} (máx. {steepest:0.00}) em {p}");
                }
            }
        Assert.True(n > 0 || terrain == "plano");
    }

    /// <summary>Toposolid alterado só na área da obra: a 5 m ou mais do contorno da terraplenagem ele fica idêntico.</summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Toposolid_ChangedOnlyInsideTheWorks(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        // Nos pontos do levantamento (a triangulação de uma malha regular é ambígua: entre eles as duas podem diferir).
        var near = TerrainRebuild.Mask(sc.Design.Footprint, 5.0);
        foreach (var v in sc.Original.DistinctPoints())
        {
            if (near(v.XY)) continue;
            var z1 = sc.Terrain.Z(v.XY);
            Assert.True(z1 != null && Math.Abs(z1.Value - v.Z) <= 0.001, $"Toposolid alterado fora da obra em {v.XY}: {v.Z:0.000} → {z1:0.000}");
        }
    }

    /// <summary>A seção em planta é a mesma da via no terreno plano (≤ 1 mm): o relevo não distorce nenhuma peça.</summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Plan_SameAsOnFlatGround(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        var flat = Get("plano", kind, a, b, relevo);
        Assert.Equal(flat.Parts.Count, sc.Parts.Count);
        for (int i = 0; i < sc.Parts.Count; i++)
        {
            var p0 = flat.Parts[i].Piece.Shape;
            var p1 = sc.Parts[i].Piece.Shape;
            Assert.Equal(p0.Outer.Count, p1.Outer.Count);
            for (int k = 0; k < p0.Outer.Count; k++)
                Assert.True(p0.Outer[k].DistanceTo(p1.Outer[k]) <= 0.001, $"{sc.Parts[i].Def.GetType().Name}/{sc.Parts[i].Piece.Layer}: vértice deslocado {p0.Outer[k].DistanceTo(p1.Outer[k]) * 1000:0.0} mm");
        }
    }

    /// <summary>
    /// Sem geometria degenerada: partes de piso simples (sem autointerseção), com a face para cima e de área ≥ 0,01 m²;
    /// pontos do Toposolid distintos em planta (≥ 5 cm) e triângulos com área.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void NoDegenerateGeometry(string terrain, string kind, string a, string b, string relevo)
    {
        var sc = Get(terrain, kind, a, b, relevo);
        foreach (var f in TopoScene.Floors(sc))
        {
            var u = PolygonOps.Union(new[] { f.Part });
            Assert.True(u.Count == 1 && Math.Abs(u[0].Area - f.Part.Area) <= 1e-4 * Math.Max(1, f.Part.Area), $"{f.Def.GetType().Name}: parte com autointerseção");
            Assert.True(f.Part.Area >= 0.01, $"{f.Def.GetType().Name}: parte de {f.Part.Area:0.0000} m²");
            Assert.True(double.IsFinite(f.Plane.A) && double.IsFinite(f.Plane.B) && double.IsFinite(f.Plane.C) && f.Plane.Slope < 1, $"{f.Def.GetType().Name}: plano inválido (rampa {f.Plane.Slope:0.00})");
        }
        var pts = TerrainRebuild.Points(sc.Original, sc.Design, TerrainRebuild.Mask(PolygonOps.Union(sc.Design.Footprint), 1.0));
        var keys = new HashSet<(long, long)>();
        foreach (var q in pts) Assert.True(keys.Add(((long)Math.Round(q.X * 20), (long)Math.Round(q.Y * 20))), $"ponto do Toposolid repetido em {q}");
        var v = sc.Terrain.Vertices;
        foreach (var (i0, i1, i2) in sc.Terrain.Triangles)
            Assert.True(Math.Abs((v[i1].XY - v[i0].XY).Cross(v[i2].XY - v[i0].XY)) > 1e-8, $"triângulo sem área em {v[i0]}");
    }
}
