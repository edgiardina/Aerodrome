namespace Aerodrome.Core;

/// <summary>
/// A 2D vector in world space. X runs along the arena, Y is altitude, both in meters.
/// Doubles, not floats: the sim is deterministic and we replay it.
/// </summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double LengthSquared => X * X + Y * Y;
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>Angle in radians, measured counter-clockwise from +X.</summary>
    public double Angle => Math.Atan2(Y, X);

    public Vec2 Normalized
    {
        get
        {
            double len = Length;
            return len < 1e-9 ? Zero : new Vec2(X / len, Y / len);
        }
    }

    /// <summary>Rotate counter-clockwise by <paramref name="radians"/>.</summary>
    public Vec2 Rotated(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    /// <summary>Rotate 90 degrees counter-clockwise. Cheaper than Rotated(PI/2) and exact.</summary>
    public Vec2 PerpCcw => new(-Y, X);

    public static Vec2 FromAngle(double radians, double length = 1.0)
        => new(Math.Cos(radians) * length, Math.Sin(radians) * length);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>2D cross product magnitude. Positive when b is counter-clockwise of a.</summary>
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    public override string ToString() => $"({X:F2}, {Y:F2})";
}
