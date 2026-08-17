namespace Aerodrome.Core;

public enum DeathCause { None, Ground, Ceiling, Gunfire, Fire, StructuralFailure, Fled }

/// <summary>
/// What a round can go through. There is no health bar in this game: damage reads
/// through smoke, sound, and how the aircraft handles afterwards.
/// </summary>
public enum Component { None, Engine, FuelTank, Wing, Tail, Controls, Pilot }

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

    // --- Flat turn ---
    /// <summary>0 when not turning. Otherwise 0 to 1 through a flat 180.</summary>
    public double FlatTurnProgress;
    public bool IsFlatTurning => FlatTurnProgress > 0;

    /// <summary>
    /// Eased progress through the yaw. Smoothstep, so the aircraft rolls in, whips
    /// through the middle of the turn, and rolls out, instead of pivoting at a
    /// constant rate like a turret.
    /// </summary>
    public double FlatTurnYawFraction
    {
        get
        {
            double p = FlatTurnProgress;
            return p * p * (3.0 - 2.0 * p);
        }
    }

    /// <summary>
    /// How far round the flat turn has yawed, in radians, 0 to PI.
    /// At PI/2 the aircraft points straight into the screen and the guns are masked.
    /// </summary>
    public double YawAngle => FlatTurnYawFraction * Math.PI;

    /// <summary>
    /// Bank angle through the turn, added to the roll for rendering. A bell curve:
    /// level at both ends, hard over in the middle.
    ///
    /// It runs off raw progress while the yaw runs off the eased progress, so the
    /// bank LEADS the turn. That is the right order. You roll first, and the
    /// aircraft comes round because it is banked.
    ///
    /// Negative tips the canopy away from the camera, which is the inside of the
    /// turn, because the nose swings through -Z.
    /// </summary>
    public double FlatTurnBank(AircraftSpec spec)
        => IsFlatTurning ? -spec.FlatTurnBankPeakRad * Math.Sin(FlatTurnProgress * Math.PI) : 0.0;

    /// <summary>Nose-up pitch held through the turn, in the canopy direction.</summary>
    public double FlatTurnPitch(AircraftSpec spec)
        => IsFlatTurning ? spec.FlatTurnPitchRad * Math.Sin(FlatTurnProgress * Math.PI) : 0.0;

    /// <summary>
    /// Aileron demand through the turn, -1 to 1. It reverses at the halfway point:
    /// roll in, then roll out. Watching the ailerons flip is what sells the turn as
    /// flown rather than scripted.
    /// </summary>
    public double FlatTurnAileron
        => IsFlatTurning ? -Math.Cos(FlatTurnProgress * Math.PI) : 0.0;

    private double _flatTurnEntrySpeed;
    private double _flatTurnEntryVx;
    private double _flatTurnEntryVy;
    private double _flatTurnEntryTheta;

    internal void BeginFlatTurn()
    {
        FlatTurnProgress = 1e-6;
        _flatTurnEntryTheta = Theta;
        _flatTurnEntryVx = Velocity.X;
        _flatTurnEntryVy = Velocity.Y;
        _flatTurnEntrySpeed = Velocity.Length;
    }

    internal double FlatTurnEntryVx => _flatTurnEntryVx;
    internal double FlatTurnEntryVy => _flatTurnEntryVy;
    internal double FlatTurnEntrySpeed => _flatTurnEntrySpeed;
    internal double FlatTurnEntryTheta => _flatTurnEntryTheta;

    /// <summary>
    /// True when the guns can bear. During a flat turn the aircraft is pointed into
    /// or out of the screen, so there is nothing it can shoot at.
    /// </summary>
    public bool GunsCanBear => IsAlive && !IsFlatTurning && !GunJammed;

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
    /// The pilot asked for a roll or a flat turn this tick and the aeroplane had
    /// too little air over its surfaces to give them one. Cleared every tick, so
    /// it is purely a cue for the HUD to explain why nothing happened.
    /// </summary>
    public bool RollRefused;

    /// <summary>
    /// What is left in the pilot, 0 to 1.
    ///
    /// Throwing a scout through a defensive break is violent, physical work, and in
    /// 1917 there was nothing between the pilot and the G but their own neck. This
    /// is what a break spends and what limits how many you get.
    ///
    /// It also sets how hard you can pull, so a pilot who has just thrown three
    /// breaks is worn out and turning badly for a while afterwards. That is the
    /// whole trade: the break buys you the moment and costs you the next ten
    /// seconds.
    /// </summary>
    public double Reserve = 1.0;

    /// <summary>True while a defensive break is being flown.</summary>
    public bool IsBreaking;

    /// <summary>Asked for a break with nothing left to fly it with. HUD cue only.</summary>
    public bool BreakRefused;

    /// <summary>
    /// How much of the airframe's G limit the pilot can actually use, given how
    /// spent they are. Never falls to nothing: exhausted still flies, it just does
    /// not fight well.
    ///
    /// This is applied to the G LIMIT rather than to control authority, and the
    /// difference is the whole point. Control authority only sets how fast the nose
    /// reaches the angle of attack it is allowed; the sustained turn is decided by
    /// the lift the wing then makes. Scaling authority produced a measured one
    /// degree difference over a second of hard pulling, which is nothing. Scaling
    /// the G the pilot can stand takes a 68 deg/s turn down to 39, which is the
    /// difference between holding a fight and losing it.
    ///
    /// Physically it is also the right place: G tolerance is a property of the
    /// pilot, not the aeroplane. The airframe limit is separate and never moves.
    /// </summary>
    public double PilotGTolerance => 0.60 + 0.40 * Math.Clamp(Reserve, 0.0, 1.0);

    /// <summary>
    /// Multiplier on the hit capsule while breaking.
    ///
    /// A rolling, jinking aircraft is a smaller and far less predictable target,
    /// and this is the honest place to express that: the rounds are already in the
    /// air, so there is nothing left to spoil except the geometry they arrive at.
    /// </summary>
    public double EvasionRadiusScale => IsBreaking ? 0.42 : 1.0;

    /// <summary>
    /// Energy height in meters: the altitude this aircraft could reach by trading
    /// away all of its speed. The single most useful number in a dogfight.
    /// </summary>
    public double EnergyHeightM;

    // --- Damage, 1.0 is undamaged ---
    public double EngineHealth = 1.0;
    public double ControlHealth = 1.0;
    public double WingHealth = 1.0;
    public double TailHealth = 1.0;
    public double FuelSystemHealth = 1.0;

    /// <summary>
    /// The pilot. Not a one-shot kill any more: a round through the cockpit wounds
    /// before it kills. Losing a round to a single lucky bullet teaches nobody
    /// anything and feels arbitrary.
    /// </summary>
    public double PilotHealth = 1.0;

    /// <summary>True once the pilot has taken a hit. Costs control authority.</summary>
    public bool IsWounded => PilotHealth < 0.99;

    /// <summary>
    /// How much airframe is left, 1 down to 0. Every round takes a slice regardless
    /// of what it went through.
    ///
    /// This is what actually kills most aircraft, and it exists so that shooting
    /// somebody is reliable. Component damage alone made kills a lottery: you could
    /// put twenty rounds into a target and have nothing decisive happen because none
    /// of them found the tank or the pilot. Keep hitting and the aeroplane comes
    /// apart. The components decide how it feels on the way down.
    /// </summary>
    public double AirframeIntegrity = 1.0;

    /// <summary>
    /// How far through the airframe's tolerance for overspeed it is, 0 to 1.
    ///
    /// Builds while past the never-exceed speed and falls back when you ease off,
    /// so a dive is a decision with a clock on it rather than a wall you hit.
    /// At 1.0 the wings come off.
    /// </summary>
    public double OverspeedStress;

    /// <summary>True once the airframe has started to complain about the speed.</summary>
    public bool IsOverspeed => OverspeedStress > 0.02;

    /// <summary>0 is untouched, 1 is about to fall out of the sky. Drives the smoke.</summary>
    public double VisibleDamage => 1.0 - AirframeIntegrity;

    /// <summary>A fuel fire. There is no extinguisher. It is a countdown.</summary>
    public bool OnFire;
    public double FireTime;

    public int HitsTaken;
    /// <summary>What the last round that connected went through. Drives effects.</summary>
    public Component LastHit;

    /// <summary>
    /// Nose authority left after tail, control, and pilot damage. A shot-away tail
    /// is what turns a fighter into a target, and a wounded pilot flies worse.
    /// </summary>
    public double EffectiveControl => ControlHealth * TailHealth * (0.55 + 0.45 * PilotHealth);

    // --- Guns ---
    public int Ammo;
    /// <summary>0 is cold, 1 is glowing. Jam chance scales with it.</summary>
    public double GunHeat;
    public bool GunJammed;
    /// <summary>0 to 1. Each pump of the charging handle adds to it, and it bleeds away.</summary>
    public double JamClearProgress;
    /// <summary>Whether the clear input was down last tick, so only rising edges count.</summary>
    public bool JamClearHeld;
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
