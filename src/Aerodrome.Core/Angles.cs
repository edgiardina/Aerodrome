namespace Aerodrome.Core;

/// <summary>Angle helpers. Every angle in the sim is radians, counter-clockwise from +X.</summary>
public static class Angles
{
    public const double TwoPi = Math.PI * 2.0;
    public const double HalfPi = Math.PI * 0.5;

    /// <summary>Wrap to the range (-PI, PI]. Use this before you compare two headings.</summary>
    public static double Wrap(double radians)
    {
        radians %= TwoPi;
        if (radians > Math.PI) radians -= TwoPi;
        else if (radians <= -Math.PI) radians += TwoPi;
        return radians;
    }

    /// <summary>Wrap to the range [0, 2PI).</summary>
    public static double Wrap0To2Pi(double radians)
    {
        radians %= TwoPi;
        if (radians < 0) radians += TwoPi;
        return radians;
    }

    /// <summary>Shortest signed difference from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static double Delta(double from, double to) => Wrap(to - from);

    public static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
    public static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
