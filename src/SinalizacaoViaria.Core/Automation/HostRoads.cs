using SinalizacaoViaria.Core.Definitions;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Automation;

/// <summary>Seção e greide da via que hospeda uma obra (ponte, viaduto, túnel, trincheira).</summary>
public sealed record HostRoad(RoadPavementDefinition Pavement, RoadGrade Grade, double Left, double Right, double Wear, double EdgeRise, int Lanes)
{
    /// <summary>Meia largura simétrica que cobre a seção inteira (m).</summary>
    public double Half => Math.Max(Left, Right);
    /// <summary>Meia largura da pista dos veículos (sem calçadas).</summary>
    public double CarriagewayHalf => Math.Max(Pavement.LeftWidth, Pavement.RightWidth);
}

public static class HostRoads
{
    /// <summary>Via (grupo) pelo identificador, com a seção e o greide atuais.</summary>
    public static HostRoad? Find(string? groupId, BuildContext ctx)
    {
        if (groupId == null || ctx.AllDefinitions == null) return null;
        var pav = ctx.AllDefinitions().OfType<RoadPavementDefinition>().FirstOrDefault(p => p.GroupId == groupId);
        return pav == null ? null : Of(pav);
    }

    /// <summary>
    /// Outras vias do projeto (exceto a hospedeira): faixa ocupada (pista + calçadas, com 0,5 m de folga) e a cota do
    /// pavimento relativa à base <paramref name="baseZ"/> da obra – para as obras não invadirem as vias que passam por baixo.
    /// </summary>
    public static List<(Polygon2 Footprint, Func<Vec2, double> Z, double Wear)> Crossing(BuildContext ctx, string? hostGroup, double baseZ)
    {
        var res = new List<(Polygon2, Func<Vec2, double>, double)>();
        if (ctx.AllDefinitions == null || ctx.PathOf == null) return res;
        foreach (var pav in ctx.AllDefinitions().OfType<RoadPavementDefinition>())
        {
            if (hostGroup != null && pav.GroupId == hostGroup) continue;
            if (ctx.PathOf(pav) is not { Points.Count: >= 2 } axis) continue;
            try
            {
                var right = -(pav.TotalRight + 0.5);
                var left = pav.TotalLeft + 0.5;
                var mid = Math.Abs(left + right) < 1e-6 ? axis : axis.Offset((left + right) / 2);
                var strip = PolygonOps.Strip(mid.Points.ToList(), left - right).OrderByDescending(x => x.Area).FirstOrDefault();
                if (strip == null) continue;
                var surf = new GradeSurface(axis, pav.Output.Grade ?? RoadGrade.Flat(axis.Length));
                var dz = (ctx.BaseZOf?.Invoke(pav) ?? baseZ) - baseZ;
                res.Add((strip, p => dz + surf.Z(p), pav.ActualThickness));
            }
            catch { /* via sem geometria válida */ }
        }
        return res;
    }

    public static HostRoad Of(RoadPavementDefinition pav)
    {
        var edge = pav.RightSidewalk > 0.01 || pav.LeftSidewalk > 0.01 ? pav.CurbHeight : 0;
        var lanes = 2;
        if (pav.SetupJson != null && RoadTemplates.FromJson(pav.SetupJson) is { } setup)
            lanes = setup.Right.Concat(setup.Left).Count(e => e.Tipo is TipoElementoSecao.FaixaRolamento or TipoElementoSecao.FaixaExclusiva or TipoElementoSecao.FaixaPreferencial);
        return new HostRoad(pav, pav.Output.Grade ?? new RoadGrade(), pav.TotalLeft, pav.TotalRight, pav.ActualThickness, edge, Math.Max(1, lanes));
    }
}
