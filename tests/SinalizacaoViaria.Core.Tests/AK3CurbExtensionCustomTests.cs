using System.Text.Json.Nodes;
using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Generators;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Tests;

/// <summary>
/// Rodada AK, item 3: extensões de calçada personalizadas por esquina e por lado (largura, comprimento, transição, forma das
/// pontas, raio da esquina, folga da travessia, ligar/desligar cada lado) e contorno livre validado (imagem 3 – PBOT).
/// </summary>
public class AK3CurbExtensionCustomTests
{
    /// <summary>Cruz local × local (estacionamento nos quatro lados), com o ajuste aplicado a todas as esquinas.</summary>
    private static Y34TipTests.World Cross(Action<IntersectionLegSettings>? leg = null, Action<IntersectionDefinition>? general = null)
    {
        var t = AKShape.Template();
        general?.Invoke(t);
        return AKShape.Make("cruz", "local2", "local2", 90, t, (d, rs) =>
        {
            if (leg == null) return;
            foreach (var r in rs)
                foreach (var sign in new[] { 1, -1 })
                {
                    var s = new IntersectionLegSettings { RoadId = r.Def.Id, Sign = sign };
                    leg(s);
                    d.LegSettings.Add(s);
                }
        });
    }

    private static List<EarPlan> Corners(Y34TipTests.World w) => w.Nodes[0].Layout.Ears.Where(e => !e.Straight).ToList();

    [Fact]
    public void WidthPerSide_ChangesEachFace()
    {
        var w = Cross(s => { s.CurbExtensionDepth = 2.0; s.CurbExtensionDepthOther = 1.4; });
        var L = w.Nodes[0].Layout;
        foreach (var e in Corners(w))
        {
            Assert.InRange(L.EarSides[(e.LegA.Road, e.LegA.Sign, 1)].Depth + e.Gutter, 1.99, 2.01);
            Assert.InRange(L.EarSides[(e.LegB.Road, e.LegB.Sign, -1)].Depth + e.Gutter, 1.39, 1.41);
            Assert.True(AKShape.Invasion(w, e) < 0.02);
        }
        Assert.Empty(AB2Check.Run(w).Faults);
    }

    [Theory]
    [InlineData(6.0)]
    [InlineData(3.0)]
    public void TaperLength_IsTheOneAsked(double taper)
    {
        var w = Cross(s => s.CurbExtensionTaper = taper);
        var L = w.Nodes[0].Layout;
        foreach (var e in Corners(w))
        {
            var es = L.EarSides[(e.LegA.Road, e.LegA.Sign, 1)];
            var (len, _, _, _) = AKShape.EndTransition(L, e, e.LegA, 1, es.Depth);
            Assert.InRange(len, taper - 0.15, taper + 0.15);
        }
        Assert.Empty(AB2Check.Run(w).Faults);
    }

    [Fact]
    public void GeneralTaper_AppliesToEveryCorner()
    {
        var w = Cross(general: d => d.CurbExtensionTaper = 5.0);
        var L = w.Nodes[0].Layout;
        foreach (var e in Corners(w))
        {
            var es = L.EarSides[(e.LegB.Road, e.LegB.Sign, -1)];
            var (len, _, _, _) = AKShape.EndTransition(L, e, e.LegB, -1, es.Depth);
            Assert.InRange(len, 4.85, 5.15);
        }
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(9.0)]
    public void CornerRadius_IsTheOneAsked(double radius)
    {
        var w = Cross(s => s.CurbExtensionCornerRadius = radius);
        var L = w.Nodes[0].Layout;
        foreach (var e in Corners(w)) Assert.InRange(AKShape.CornerRadiusOf(L, e), radius - 0.03, radius + 0.03);
        Assert.Empty(AB2Check.Run(w).Faults);
    }

    /// <summary>Raio grande demais: a curva nova entraria na calçada – o raio é reduzido, com aviso.</summary>
    [Fact]
    public void CornerRadius_TooLarge_IsReducedWithWarning()
    {
        var w = Cross(s => s.CurbExtensionCornerRadius = 40);
        var L = w.Nodes[0].Layout;
        Assert.Contains(L.Warnings, x => x.Contains("raio da curva da extensão de calçada reduzido"));
        foreach (var e in Corners(w))
        {
            var r = AKShape.CornerRadiusOf(L, e);
            Assert.True(r < 40 && r > 6, $"raio {r:0.00}");
        }
        Assert.Empty(AB2Check.Run(w).Faults);
    }

    [Fact]
    public void Chamfer_OnTheOtherSide_IsStraightAtTheAngleOfItsLength()
    {
        var w = Cross(s => { s.CurbExtensionEndsOther = TipoTransicao.Chanfro; s.CurbExtensionTaperOther = 4.0; });
        var L = w.Nodes[0].Layout;
        foreach (var e in Corners(w))
        {
            var es = L.EarSides[(e.LegB.Road, e.LegB.Sign, -1)];
            var (len, slope, _, _) = AKShape.EndTransition(L, e, e.LegB, -1, es.Depth);
            Assert.InRange(len, 3.85, 4.15);
            Assert.InRange(slope, Math.Atan2(es.Depth, 4.0) * 180 / Math.PI - 1.5, Math.Atan2(es.Depth, 4.0) * 180 / Math.PI + 1.5);
            // O lado do ramo A segue com a curva reversa automática.
            var ea = L.EarSides[(e.LegA.Road, e.LegA.Sign, 1)];
            var (lenA, _, _, _) = AKShape.EndTransition(L, e, e.LegA, 1, ea.Depth);
            Assert.InRange(lenA, 2 * ea.Depth - 0.15, 2 * ea.Depth + 0.15);
        }
    }

    [Fact]
    public void Length_And_CrosswalkMargin_MoveTheFarEnd()
    {
        var w0 = Cross();
        var w1 = Cross(s => s.CurbExtensionLength = 20);
        var w2 = Cross(s => s.CurbExtensionCrosswalkMargin = 2.0);
        var L0 = w0.Nodes[0].Layout;
        var L1 = w1.Nodes[0].Layout;
        var L2 = w2.Nodes[0].Layout;
        foreach (var e in Corners(w1))
            Assert.InRange(L1.EarSides[(e.LegA.Road, e.LegA.Sign, 1)].TEnd, e.LegA.Clear + 20 - 0.01, e.LegA.Clear + 20 + 0.01);
        foreach (var e in Corners(w2))
        {
            var key = (e.LegA.Road, e.LegA.Sign, 1);
            Assert.InRange(L2.EarSides[key].TEnd - L0.EarSides[key].TEnd, 1.7 - 0.05, 1.7 + 0.05);
        }
    }

    [Fact]
    public void SideOff_KeepsOnlyTheOtherRoad_AndTheCornerCurve()
    {
        var w = Cross(s => s.CurbExtensionLengthOther = 0);
        var L = w.Nodes[0].Layout;
        foreach (var e in Corners(w))
        {
            Assert.True(e.SideA);
            Assert.False(e.SideB);
            Assert.InRange(AKShape.CornerRadiusOf(L, e), L.CornerFillets[(e.LegA.Road, e.LegA.Sign)].R - 0.02, L.CornerFillets[(e.LegA.Road, e.LegA.Sign)].R + 0.02);
        }
        Assert.Empty(AB2Check.Run(w).Faults);
    }

    /// <summary>Contorno livre de uma esquina: pontos de controle do meio-fio de um ramo ao do outro, com o canto chanfrado.</summary>
    private static (Y34TipTests.World W, IntersectionLeg A, IntersectionLeg B) Free(Func<IntersectionLayout, IntersectionLeg, IntersectionLeg, List<Vec2>> points, double radius = 2.0)
    {
        var w0 = Cross();
        var L0 = w0.Nodes[0].Layout;
        var e0 = Corners(w0).First();
        var local = points(L0, e0.LegA, e0.LegB).Select(p => IntersectionGenerator.ToLeg(L0, e0.LegA, p)).ToList();
        var road = e0.LegA.Road;
        var sign = e0.LegA.Sign;
        var t = AKShape.Template();
        // Mesma cena (as vias ganham ids novos a cada montagem: o ajuste vai para a via na mesma posição).
        var w = AKShape.Make("cruz", "local2", "local2", 90, t, (d, rs) =>
            d.LegSettings.Add(new IntersectionLegSettings { RoadId = rs[road].Def.Id, Sign = sign, CurbExtensionFace = local, CurbExtensionFaceRadius = radius }));
        return (w, e0.LegA, e0.LegB);
    }

    /// <summary>Vértice dos bordos deslocados de <paramref name="f"/> (m) para a pista, nos dois lados da esquina.</summary>
    private static Vec2 Shifted(IntersectionLayout L, IntersectionLeg la, double f)
    {
        var cf = L.CornerFillets[(la.Road, la.Sign)];
        var nA = -(cf.Ub - cf.Ua * cf.Ub.Dot(cf.Ua)).Normalized();
        var nB = -(cf.Ua - cf.Ub * cf.Ua.Dot(cf.Ub)).Normalized();
        return cf.V + cf.Ua * (f / cf.Ua.Dot(nB)) + cf.Ub * (f / cf.Ub.Dot(nA));
    }

    private static List<Vec2> Chamfered(IntersectionLayout L, IntersectionLeg la, IntersectionLeg lb, double depth, Vec2 corner) => new()
    {
        L.At(la, la.Clear + 14, L.HiEdge(la, la.Clear + 14)),
        L.At(la, la.Clear + 9, L.HiEdge(la, la.Clear + 9) - depth),
        corner,
        L.At(lb, lb.Clear + 9, -(L.LoEdge(lb, lb.Clear + 9) - depth)),
        L.At(lb, lb.Clear + 14, -L.LoEdge(lb, lb.Clear + 14)),
    };

    [Fact]
    public void FreeContour_Valid_IsUsed()
    {
        var (w, la, lb) = Free((L, a, b) => Chamfered(L, a, b, 1.8, Shifted(L, a, 1.8)));
        var L = w.Nodes[0].Layout;
        Assert.DoesNotContain(L.Warnings, x => x.Contains("recusado"));
        var e = L.Ears.Single(x => !x.Straight && x.LegA == la && x.LegB == lb);
        var face = e.Template.OuterFace!;
        // Começa e termina no meio-fio, e é o contorno pedido: o canto arredondado passa perto do vértice deslocado de 1,8 m.
        Assert.True(face[0].DistanceTo(e.Path.Points[0]) < 0.02 && face[^1].DistanceTo(e.Path.Points[^1]) < 0.02);
        var v = Shifted(L, la, 1.8);
        var near = face.Min(p => p.DistanceTo(v));
        Assert.InRange(near, 0.2, 1.5);
        Assert.True(AKShape.Invasion(w, e) < 0.02);
        Assert.Empty(AB2Check.Run(w).Faults);
    }

    [Fact]
    public void FreeContour_OverATravelLane_IsRejectedWithTheReason()
    {
        var (w, la, lb) = Free((L, a, b) => Chamfered(L, a, b, 1.8, Shifted(L, a, 4.5)));
        var L = w.Nodes[0].Layout;
        Assert.Contains(L.Warnings, x => x.Contains("recusado") && x.Contains("faixa de rolamento"));
        // A esquina fica com a forma padrão (raio da esquina).
        var e = L.Ears.Single(x => !x.Straight && x.LegA == la && x.LegB == lb);
        Assert.InRange(AKShape.CornerRadiusOf(L, e), L.CornerFillets[(la.Road, la.Sign)].R - 0.02, L.CornerFillets[(la.Road, la.Sign)].R + 0.02);
    }

    [Fact]
    public void FreeContour_EndsOffTheCurb_IsRejectedWithTheReason()
    {
        var (w, _, _) = Free((L, a, b) =>
        {
            var pts = Chamfered(L, a, b, 1.8, Shifted(L, a, 1.8));
            pts[0] = L.At(a, a.Clear + 14, L.HiEdge(a, a.Clear + 14) - 2.2);
            return pts;
        });
        Assert.Contains(w.Nodes[0].Layout.Warnings, x => x.Contains("recusado") && x.Contains("pontas"));
    }

    [Fact]
    public void FreeContour_IntoTheSidewalk_IsRejectedWithTheReason()
    {
        var (w, _, _) = Free((L, a, b) =>
        {
            var cf = L.CornerFillets[(a.Road, a.Sign)];
            // Canto do contorno dentro da quadra (atrás do meio-fio da esquina).
            return Chamfered(L, a, b, 1.8, cf.V + (cf.Ua + cf.Ub).Normalized() * 3.0);
        });
        Assert.Contains(w.Nodes[0].Layout.Warnings, x => x.Contains("recusado"));
    }

    [Fact]
    public void FreeContour_FollowsTheIntersection_WhenItMoves()
    {
        // O contorno fica nas coordenadas do ramo: andar com a interseção não muda a extensão.
        var (w, la, lb) = Free((L, a, b) => Chamfered(L, a, b, 1.8, Shifted(L, a, 1.8)));
        var d = w.Nodes[0].Layout.Definition!;
        var copy = (IntersectionDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        copy.Translate(new Vec2(10, 5), 0);
        var before = d.LegSettings.Single(s => s.CurbExtensionFace != null).CurbExtensionFace!;
        var after = copy.LegSettings.Single(s => s.CurbExtensionFace != null).CurbExtensionFace!;
        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++) Assert.True(before[i].DistanceTo(after[i]) < 1e-6);
        Assert.Contains(w.Nodes[0].Layout.Ears, e => e.Template.OuterFace != null && e.LegA == la && e.LegB == lb);
    }

    /// <summary>Projeto salvo antes da rodada (sem os campos novos) abre sem erro e com a forma nova padrão.</summary>
    [Fact]
    public void OldSavedProject_OpensWithTheNewDefaultShape()
    {
        var old = AKShape.Template();
        old.CurbExtensionEndRadius = 1.5;
        old.LegSettings.Add(new IntersectionLegSettings { RoadId = "x", Sign = 1, CurbExtension = true, CurbExtensionLength = 6 });
        var node = JsonNode.Parse(old.ToJson())!.AsObject();
        // Campos gravados em camelCase.
        foreach (var k in new[] { "curbExtensionTaper", "curbExtensionCornerRadius", "curbExtensionCrosswalkMargin" }) node.Remove(k);
        foreach (var s in node["legSettings"]!.AsArray())
            foreach (var k in new[] { "curbExtensionDepthOther", "curbExtensionEndsOther", "curbExtensionTaper", "curbExtensionTaperOther",
                         "curbExtensionCornerRadius", "curbExtensionCrosswalkMargin", "curbExtensionFace", "curbExtensionFaceRadius", "oppositeExtensionTaper" })
                s!.AsObject().Remove(k);
        var json = node.ToJsonString();
        Assert.DoesNotContain("curbExtensionTaper", json);
        Assert.Contains("curbExtensionEndRadius", json);
        var back = (IntersectionDefinition)MarkingDefinition.FromJson(json)!;
        Assert.Null(back.CurbExtensionTaper);
        Assert.Equal(6, back.LegSettings[0].CurbExtensionLength);
        var w = AKShape.Make("cruz", "local2", "coletora4", 90, back);
        var L = w.Nodes[0].Layout;
        Assert.NotEmpty(Corners(w));
        foreach (var e in Corners(w))
        {
            var (on, leg, side) = e.SideA ? (true, e.LegA, 1) : (true, e.LegB, -1);
            var es = L.EarSides[(leg.Road, leg.Sign, side)];
            var (len, _, _, _) = AKShape.EndTransition(L, e, leg, side, es.Depth);
            Assert.True(on && len >= 1.8 * es.Depth - 0.05);
        }
        Assert.Empty(AB2Check.Run(w).Faults);
    }

    [Fact]
    public void NewFields_RoundTrip()
    {
        var d = AKShape.Template();
        d.CurbExtensionTaper = 4.5;
        d.CurbExtensionCornerRadius = 5;
        d.CurbExtensionCrosswalkMargin = 1;
        d.LegSettings.Add(new IntersectionLegSettings
        {
            RoadId = "r", Sign = -1, CurbExtensionDepthOther = 1.8, CurbExtensionEndsOther = TipoTransicao.Chanfro, CurbExtensionTaper = 3,
            CurbExtensionTaperOther = 4, CurbExtensionCornerRadius = 7, CurbExtensionCrosswalkMargin = 0.5,
            CurbExtensionFace = new() { new Vec2(1, 2), new Vec2(3, 4) }, CurbExtensionFaceRadius = 1.5, OppositeExtensionTaper = 2.5,
        });
        var back = (IntersectionDefinition)MarkingDefinition.FromJson(d.ToJson())!;
        Assert.Equal(4.5, back.CurbExtensionTaper);
        Assert.Equal(5, back.CurbExtensionCornerRadius);
        Assert.Equal(1, back.CurbExtensionCrosswalkMargin);
        var s = back.LegSettings.Single();
        Assert.Equal(1.8, s.CurbExtensionDepthOther);
        Assert.Equal(TipoTransicao.Chanfro, s.CurbExtensionEndsOther);
        Assert.Equal(new[] { new Vec2(1, 2), new Vec2(3, 4) }, s.CurbExtensionFace);
        Assert.Equal(2.5, s.OppositeExtensionTaper);
        Assert.False(s.IsEmpty);
    }
}
