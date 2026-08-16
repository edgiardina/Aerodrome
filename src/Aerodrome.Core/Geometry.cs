namespace Aerodrome.Core;

/// <summary>Collision helpers. Small, exact, and allocation free.</summary>
public static class Geometry
{
    /// <summary>
    /// Shortest distance between two line segments, and where on the first it
    /// happens.
    ///
    /// Bullets need this. A round leaves the muzzle at 745 m/s, which is 6.2 m per
    /// 120 Hz tick, and the aircraft hit radius is about 3 m. Testing the bullet as
    /// a point once per tick would let it pass clean through an aircraft on most
    /// frames. So each tick tests the segment the bullet swept against the segment
    /// running nose to tail.
    /// </summary>
    public static double SegmentDistance(
        Vec2 p0, Vec2 p1, Vec2 q0, Vec2 q1, out double tOnFirst)
    {
        Vec2 u = p1 - p0;
        Vec2 v = q1 - q0;
        Vec2 w = p0 - q0;

        double a = Vec2.Dot(u, u);
        double b = Vec2.Dot(u, v);
        double c = Vec2.Dot(v, v);
        double d = Vec2.Dot(u, w);
        double e = Vec2.Dot(v, w);
        double denom = a * c - b * b;

        double s, t;
        if (denom < 1e-9)
        {
            // Parallel, or one of them is a point. Pin to the start of the first.
            s = 0.0;
            t = c > 1e-9 ? e / c : 0.0;
        }
        else
        {
            s = (b * e - c * d) / denom;
            t = (a * e - b * d) / denom;
        }

        s = Angles.Clamp(s, 0.0, 1.0);
        t = Angles.Clamp(t, 0.0, 1.0);

        // One clamped end can move the other, so settle each against the final one.
        t = c > 1e-9 ? Angles.Clamp((Vec2.Dot(u, v) * s + e) / c, 0.0, 1.0) : 0.0;
        s = a > 1e-9 ? Angles.Clamp((b * t - d) / a, 0.0, 1.0) : 0.0;

        tOnFirst = s;
        Vec2 closest = (p0 + u * s) - (q0 + v * t);
        return closest.Length;
    }

    /// <summary>
    /// Where along an aircraft's spine a point falls, 0 at the tail and 1 at the
    /// nose. Used to work out which part of the aircraft a round went through.
    /// </summary>
    public static double AlongSpine(Vec2 point, Vec2 tail, Vec2 nose)
    {
        Vec2 axis = nose - tail;
        double lengthSq = axis.LengthSquared;
        if (lengthSq < 1e-9) return 0.5;
        return Angles.Clamp(Vec2.Dot(point - tail, axis) / lengthSq, 0.0, 1.0);
    }
}
