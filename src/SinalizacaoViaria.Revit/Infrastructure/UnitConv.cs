using Autodesk.Revit.DB;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Revit.Infrastructure;

/// <summary>Conversões entre as unidades internas do Revit (pés) e o núcleo (metros).</summary>
public static class UnitConv
{
    public const double FeetPerMeter = 1.0 / 0.3048;
    public const double MetersPerFoot = 0.3048;

    public static double Ft(double meters) => meters * FeetPerMeter;
    public static double M(double feet) => feet * MetersPerFoot;
    public static double Ft2(double squareMeters) => squareMeters * FeetPerMeter * FeetPerMeter;

    public static XYZ ToXyz(Vec2 v, double zMeters) => new(Ft(v.X), Ft(v.Y), Ft(zMeters));
    public static Vec2 ToVec2(XYZ p) => new(M(p.X), M(p.Y));
}
