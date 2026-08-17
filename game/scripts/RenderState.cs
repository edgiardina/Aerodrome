using Aerodrome.Core;

namespace Aerodrome.Game;

/// <summary>
/// The slice of an aircraft the renderer cares about, captured once per sim tick.
///
/// The renderer never reads AircraftState directly. It reads an interpolation
/// between the last two of these. A camera or a mesh that reads raw sim state
/// judders at any refresh rate above the sim rate, and that is the usual reason a
/// scrolling game "feels off" even at 144 fps.
///
/// Transient offsets like the flat turn bank and pitch are already folded into
/// Theta and Roll here, so the view only ever renders what it is handed.
/// </summary>
public readonly struct RenderState
{
    public double X { get; init; }
    public double Y { get; init; }

    /// <summary>Nose angle in the screen plane, including any transient pitch.</summary>
    public double Theta { get; init; }

    /// <summary>Roll about the long axis, including any transient bank.</summary>
    public double Roll { get; init; }

    /// <summary>Yaw about world up. Non-zero only during a flat turn.</summary>
    public double Yaw { get; init; }

    public double PropAngle { get; init; }

    // Control surface demand, -1 to 1.
    public double Aileron { get; init; }
    public double Elevator { get; init; }
    public double Rudder { get; init; }

    public double Airspeed { get; init; }

    /// <summary>
    /// The velocity vector, in m/s. Where the aircraft is actually going, which is
    /// NOT the direction the nose is pointing and diverges hard under any real
    /// pull. The camera lead has to use this one.
    /// </summary>
    public double VelocityX { get; init; }
    public double VelocityY { get; init; }

    public bool IsAlive { get; init; }

    /// <summary>
    /// Blend two ticks. Angles take the short way round, so a heading that crosses
    /// the 0/360 seam does not spin the model all the way back.
    /// </summary>
    public static RenderState Lerp(in RenderState a, in RenderState b, double t)
    {
        // On the tick a flat turn commits, yaw resets to zero while the heading
        // mirrors and the canopy flips. Those changes cancel to the same orientation,
        // but interpolating across them would spin the model. Snap instead.
        if (a.Yaw > 0.01 && b.Yaw <= 0.0) return b;

        return new RenderState
        {
            X = a.X + (b.X - a.X) * t,
            Y = a.Y + (b.Y - a.Y) * t,
            Theta = LerpAngle(a.Theta, b.Theta, t),
            Roll = LerpAngle(a.Roll, b.Roll, t),
            Yaw = a.Yaw + (b.Yaw - a.Yaw) * t,
            PropAngle = LerpAngle(a.PropAngle, b.PropAngle, t),
            Aileron = a.Aileron + (b.Aileron - a.Aileron) * t,
            Elevator = a.Elevator + (b.Elevator - a.Elevator) * t,
            Rudder = a.Rudder + (b.Rudder - a.Rudder) * t,
            Airspeed = a.Airspeed + (b.Airspeed - a.Airspeed) * t,
            VelocityX = a.VelocityX + (b.VelocityX - a.VelocityX) * t,
            VelocityY = a.VelocityY + (b.VelocityY - a.VelocityY) * t,
            IsAlive = b.IsAlive,
        };
    }

    private static double LerpAngle(double a, double b, double t)
        => Angles.Wrap0To2Pi(a + Angles.Delta(a, b) * t);
}
