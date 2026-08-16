using Aerodrome.Core;

namespace Aerodrome.Game;

/// <summary>
/// The slice of an aircraft the renderer cares about, captured once per sim tick.
///
/// The renderer never reads AircraftState directly. It reads an interpolation
/// between the last two of these. A camera or a mesh that reads raw sim state
/// judders at any refresh rate above the sim rate, and that is the usual reason a
/// scrolling game "feels off" even at 144 fps.
/// </summary>
public readonly struct RenderState
{
    public readonly double X, Y;
    public readonly double Theta;
    public readonly double RollAngle;
    public readonly double Yaw;
    public readonly double PropAngle;
    public readonly double Airspeed;
    public readonly bool IsAlive;

    public RenderState(double x, double y, double theta, double rollAngle, double yaw,
                       double propAngle, double airspeed, bool isAlive)
    {
        X = x; Y = y;
        Theta = theta;
        RollAngle = rollAngle;
        Yaw = yaw;
        PropAngle = propAngle;
        Airspeed = airspeed;
        IsAlive = isAlive;
    }

    public static RenderState Capture(AircraftState s, double propAngle) =>
        new(s.Position.X, s.Position.Y, s.Theta, s.RollAngle, s.YawAngle,
            propAngle, s.Airspeed, s.IsAlive);

    /// <summary>
    /// Blend two ticks. Angles take the short way round, so a heading that crosses
    /// the 0/360 seam does not spin the model all the way back.
    /// </summary>
    public static RenderState Lerp(in RenderState a, in RenderState b, double t)
    {
        // The tick a flat turn commits on, yaw resets to 0 while the heading mirrors.
        // Those two changes cancel out to the same orientation, but interpolating
        // across them would spin the model. Snap to the new tick instead.
        if (a.Yaw > 0.01 && b.Yaw <= 0.0) return b;

        return new RenderState(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            LerpAngle(a.Theta, b.Theta, t),
            LerpAngle(a.RollAngle, b.RollAngle, t),
            a.Yaw + (b.Yaw - a.Yaw) * t,
            LerpAngle(a.PropAngle, b.PropAngle, t),
            a.Airspeed + (b.Airspeed - a.Airspeed) * t,
            b.IsAlive);
    }

    private static double LerpAngle(double a, double b, double t)
        => Angles.Wrap0To2Pi(a + Angles.Delta(a, b) * t);
}
