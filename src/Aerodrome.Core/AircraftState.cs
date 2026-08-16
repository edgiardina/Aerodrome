namespace Aerodrome.Core;

public enum DeathCause { None, Ground, Ceiling, Gunfire, Fire, StructuralFailure, Fled }

/// <summary>
/// Everything about one aircraft that changes tick to tick.
///
/// A class, not a struct: it is allocated once per aircraft and then mutated in
/// place forever. The sim loop must not allocate.
///
/// Orientation is two values, and the pair is the whole trick of this game:
///   Theta      - where the nose points, in world radians.
///   CanopySign - which side of the nose the canopy is on, +1 or -1.
/// A half loop sweeps Theta through 180 degrees and leaves CanopySign alone, so
/// the aircraft comes out inverted. Only a roll flips CanopySign. That is why
/// the roll key has to be manual.
/// </summary>
public sealed class AircraftState
{
    // --- Motion ---
    public Vec2 Position;
    public Vec2 Velocity;

    /// <summary>Nose direction in radians, [0, 2PI).</summary>
    public double Theta;

    // --- Orientation ---
    /// <summary>Roll about the long axis, [0, 2PI). 0 is upright, PI is inverted.</summary>
    public double RollAngle;

    /// <summary>Radians of roll still to travel. Above zero means a roll is in progress.</summary>
    public double RollRemaining;

    /// <summary>+1 when the canopy is on the counter-clockwise side of the nose, -1 otherwise.</summary>
    public int CanopySign = 1;

    /// <summary>True when the canopy points below the horizon.</summary>
    public bool IsInverted;

    /// <summary>Seconds spent inverted, minus recovery. Drives the fuel starvation.</summary>
    public double InvertedTime;

    // --- Powerplant ---
    /// <summary>Throttle lever position, 0 to 1. It slews toward the command.</summary>
    public double Throttle;

    /// <summary>0 is a healthy fuel feed, 1 is fully starved. Presentation reads this for smoke.</summary>
    public double FuelStarvation;

    // --- Derived, refreshed every tick for the HUD, the AI, and the telemetry overlay ---
    public double Airspeed;
    public double Alpha;
    public double LoadFactor;
    public double SlewRateRad;
    public bool IsStalled;
    public bool IsSpinning;
    public double SpinTime;

    /// <summary>
    /// Energy height in meters: the altitude this aircraft could reach by trading
    /// away all of its speed. The single most useful number in a dogfight.
    /// </summary>
    public double EnergyHeightM;

    // --- Damage, 1.0 is undamaged ---
    public double EngineHealth = 1.0;
    public double ControlHealth = 1.0;
    public double WingHealth = 1.0;

    // --- Guns ---
    public int Ammo;
    public double GunHeat;
    public bool GunJammed;
    public double FireCooldown;

    // --- Life ---
    public bool IsAlive = true;
    public DeathCause Death = DeathCause.None;
    public double OutOfBoundsTime;

    /// <summary>Nose direction as a unit vector.</summary>
    public Vec2 Nose => Vec2.FromAngle(Theta);

    /// <summary>Which way the aircraft is pointing across the arena. +1 is right.</summary>
    public int Facing => Math.Cos(Theta) >= 0 ? 1 : -1;

    /// <summary>
    /// Place an aircraft in level flight. Used by spawns and by every test.
    /// </summary>
    public static AircraftState Spawn(
        AircraftSpec spec,
        Vec2 position,
        double heading,
        double speed,
        bool inverted = false,
        double throttle = 1.0)
    {
        var s = new AircraftState
        {
            Position = position,
            Theta = Angles.Wrap0To2Pi(heading),
            Velocity = Vec2.FromAngle(heading, speed),
            Throttle = throttle,
            Ammo = spec.AmmoRounds,
            RollAngle = 0,
            CanopySign = 1,
        };

        // "Inverted" means the canopy points down for this heading, so pick the
        // roll that produces that instead of assuming the aircraft flies right.
        bool wouldBeInverted = Math.Cos(s.Theta) < 0;
        if (wouldBeInverted != inverted)
        {
            s.RollAngle = Math.PI;
            s.CanopySign = -1;
        }

        s.IsInverted = s.CanopySign * Math.Cos(s.Theta) < 0;
        s.Airspeed = speed;
        s.EnergyHeightM = speed * speed / (2.0 * Atmosphere.Gravity) + position.Y;
        return s;
    }
}
