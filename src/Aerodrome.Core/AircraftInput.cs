namespace Aerodrome.Core;

/// <summary>
/// One tick of pilot intent. The AI fills this in exactly like the player does.
/// Nothing may reach into AircraftState and set orientation directly.
/// </summary>
public readonly record struct AircraftInput
{
    /// <summary>
    /// Where the pilot wants the nose to point, in radians. Null means "hold what
    /// you have", which is what a centered stick does.
    ///
    /// This is the original's control: you point, the nose goes there and stops.
    /// </summary>
    public double? HeadingCommand { get; init; }

    /// <summary>
    /// A real control column instead: -1 is full forward, +1 is full back, relative
    /// to the AIRCRAFT. Takes priority over HeadingCommand when set.
    ///
    /// Pulling back always takes the nose toward the canopy, so inverted, back
    /// stick points you at the ground. That is what a stick does, and it is why
    /// this scheme makes the roll key matter in a way pointing never did.
    /// </summary>
    public double? PitchStick { get; init; }

    /// <summary>Throttle the pilot is asking for, 0 to 1. The lever slews toward it.</summary>
    public double ThrottleCommand { get; init; }

    /// <summary>True while the trigger is held.</summary>
    public bool FireHeld { get; init; }

    /// <summary>Edge-triggered. One press starts one 180 degree roll.</summary>
    public bool RollPressed { get; init; }

    /// <summary>Edge-triggered. A full 360 degree aileron roll that keeps the heading.</summary>
    public bool AileronRollPressed { get; init; }

    /// <summary>
    /// Edge-triggered. Swap ends with a flat turn through the screen depth. Keeps
    /// altitude, costs speed and about a second of helplessness.
    /// </summary>
    public bool FlatTurnPressed { get; init; }

    /// <summary>
    /// One pump of the charging handle. Only rising edges count, so this has to be
    /// hammered to clear a jam and cannot be held down.
    /// </summary>
    public bool ClearJamPressed { get; init; }

    public static AircraftInput Neutral => new() { ThrottleCommand = 0.0 };

    /// <summary>Hold current heading at a given throttle. The usual "do nothing" input.</summary>
    public static AircraftInput Coast(double throttle) => new() { ThrottleCommand = throttle };
}
