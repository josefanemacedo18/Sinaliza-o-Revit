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

    public static HostRoad Of(RoadPavementDefinition pav)
    {
        var edge = pav.RightSidewalk > 0.01 || pav.LeftSidewalk > 0.01 ? pav.CurbHeight : 0;
        var lanes = 2;
        if (pav.SetupJson != null && RoadTemplates.FromJson(pav.SetupJson) is { } setup)
            lanes = setup.Right.Concat(setup.Left).Count(e => e.Tipo is TipoElementoSecao.FaixaRolamento or TipoElementoSecao.FaixaExclusiva or TipoElementoSecao.FaixaPreferencial);
        return new HostRoad(pav, pav.Output.Grade ?? new RoadGrade(), pav.TotalLeft, pav.TotalRight, pav.ActualThickness, edge, Math.Max(1, lanes));
    }
}
