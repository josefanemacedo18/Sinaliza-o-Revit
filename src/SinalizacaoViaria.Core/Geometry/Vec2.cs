namespace SinalizacaoViaria.Core.Geometry;

/// <summary>
/// Vetor/ponto 2D em metros, no plano horizontal do projeto (X = leste, Y = norte).
/// Todo o núcleo geométrico trabalha em 2D; a elevação é tratada pela camada Revit.
/// </summary>
public readonly record struct Vec2(double X, double Y)
{
    public const double Eps = 1e-9;

    public static readonly Vec2 Zero = new(0, 0);
    public static readonly Vec2 UnitX = new(1, 0);
    public static readonly Vec2 UnitY = new(0, 1);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);
    public static Vec2 operator *(double k, Vec2 a) => new(a.X * k, a.Y * k);
    public static Vec2 operator /(Vec2 a, double k) => new(a.X / k, a.Y / k);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    /// <summary>Ângulo em radianos em relação ao eixo X.</summary>
    public double Angle => Math.Atan2(Y, X);

    public Vec2 Normalized()
    {
        var len = Length;
        return len < Eps ? Zero : new Vec2(X / len, Y / len);
    }

    /// <summary>Normal à esquerda (rotação de +90°).</summary>
    public Vec2 PerpLeft => new(-Y, X);

    /// <summary>Normal à direita (rotação de -90°).</summary>
    public Vec2 PerpRight => new(Y, -X);

    public double Dot(Vec2 o) => X * o.X + Y * o.Y;

    /// <summary>Produto vetorial 2D (componente Z).</summary>
    public double Cross(Vec2 o) => X * o.Y - Y * o.X;

    public double DistanceTo(Vec2 o) => (this - o).Length;

    public Vec2 Rotate(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public static Vec2 FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public bool AlmostEquals(Vec2 o, double tol = 1e-6) => DistanceTo(o) <= tol;

    public override string ToString() => FormattableString.Invariant($"({X:0.####}; {Y:0.####})");
}

/// <summary>Utilidades angulares.</summary>
public static class Angles
{
    public const double DegToRad = Math.PI / 180.0;
    public const double RadToDeg = 180.0 / Math.PI;

    public static double ToRad(double degrees) => degrees * DegToRad;
    public static double ToDeg(double radians) => radians * RadToDeg;
}
