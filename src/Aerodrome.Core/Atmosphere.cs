namespace Aerodrome.Core;

/// <summary>
/// Air density and engine power against altitude. A trimmed ISA model.
/// Good to about 11 km, which is far above any arena ceiling.
/// </summary>
public static class Atmosphere
{
    public const double SeaLevelDensity = 1.225;   // kg/m^3
    public const double Gravity = 9.80665;         // m/s^2

    private const double SeaLevelTemp = 288.15;    // K
    private const double LapseRate = 0.0065;       // K/m
    private const double DensityExponent = 4.256;  // g/(L*R) - 1

    /// <summary>Air density at an altitude in meters.</summary>
    public static double Density(double altitudeMeters)
    {
        if (altitudeMeters <= 0) return SeaLevelDensity;
        double ratio = 1.0 - LapseRate * altitudeMeters / SeaLevelTemp;
        if (ratio <= 0) return 0;
        return SeaLevelDensity * Math.Pow(ratio, DensityExponent);
    }

    /// <summary>
    /// Fraction of sea-level power a naturally aspirated engine still makes.
    /// It reaches zero at the absolute ceiling, so an endless climb is impossible.
    /// </summary>
    public static double PowerFraction(double altitudeMeters, double absoluteCeilingMeters)
    {
        if (altitudeMeters <= 0) return 1.0;
        if (altitudeMeters >= absoluteCeilingMeters) return 0.0;
        double sigma = Density(altitudeMeters) / SeaLevelDensity;
        // Gagg-Ferrar: power falls faster than density alone.
        double fraction = (sigma - 0.117) / 0.883;
        // Fade the last stretch to zero so the ceiling is a hard wall, not an asymptote.
        double toCeiling = 1.0 - altitudeMeters / absoluteCeilingMeters;
        return Math.Max(0.0, Math.Min(fraction, toCeiling * 1.6));
    }
}
