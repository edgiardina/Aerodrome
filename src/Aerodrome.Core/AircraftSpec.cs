namespace Aerodrome.Core;

/// <summary>
/// Everything that makes one aircraft type fly differently from another.
/// Immutable. One instance is shared by every aircraft of that type.
///
/// Defaults are the Sopwith Camel F.1 with a Clerget 9B. Real numbers where real
/// numbers exist, so the model starts honest and the M1 tuning pass only has to
/// bend it toward feel instead of inventing it.
/// </summary>
public sealed record AircraftSpec
{
    public required string Name { get; init; }

    // --- Mass and geometry ---
    public double MassKg { get; init; } = 659.0;
    public double WingAreaM2 { get; init; } = 21.46;
    public double WingSpanM { get; init; } = 8.53;
    public double AspectRatio => WingSpanM * WingSpanM / WingAreaM2;

    // --- Engine ---
    /// <summary>Shaft power in watts. 130 hp for the Clerget 9B.</summary>
    public double EnginePowerW { get; init; } = 96_940.0;
    public double PropEfficiency { get; init; } = 0.72;
    /// <summary>Thrust cap near zero airspeed, where power over speed would blow up.</summary>
    public double StaticThrustN { get; init; } = 2500.0;
    /// <summary>Altitude where the engine makes no useful power at all.</summary>
    public double AbsoluteCeilingM { get; init; } = 6500.0;
    /// <summary>Seconds for the throttle to travel its full range.</summary>
    public double ThrottleSlewPerSecond { get; init; } = 1.2;

    // --- Aerodynamics ---
    /// <summary>Lift curve slope per radian. Below 2*PI because the aspect ratio is low.</summary>
    public double LiftSlopePerRad { get; init; } = 5.0;
    public double ClMax { get; init; } = 1.20;
    /// <summary>Parasite drag. High: a biplane is a kite made of struts and wire.</summary>
    public double Cd0 { get; init; } = 0.040;
    /// <summary>Oswald span efficiency. Biplane interference makes this poor.</summary>
    public double OswaldEfficiency { get; init; } = 0.75;
    /// <summary>Induced drag factor k in Cd = Cd0 + k * Cl^2.</summary>
    public double InducedDragFactor => 1.0 / (Math.PI * AspectRatio * OswaldEfficiency);

    /// <summary>Angle of attack where the wing stalls, in radians.</summary>
    public double StallAlphaRad => ClMax / LiftSlopePerRad;
    /// <summary>How far past the stall the wing keeps shedding lift before it settles.</summary>
    public double PostStallRangeRad { get; init; } = 0.35;
    /// <summary>Fraction of ClMax the wing still makes deep in a stall.</summary>
    public double DeepStallClFraction { get; init; } = 0.30;

    // --- Maneuver limits ---
    /// <summary>Structural positive G limit. WW1 airframes were not strong.</summary>
    public double GLimit { get; init; } = 4.5;
    /// <summary>
    /// Push authority as a fraction of pull authority. An aircraft pulls far harder
    /// than it pushes, which is exactly what makes inversion expensive.
    /// </summary>
    public double PushFactor { get; init; } = 0.33;
    /// <summary>Single tuning knob for arcade snappiness. 1.0 is the honest physics.</summary>
    public double TurnRateScale { get; init; } = 1.0;
    /// <summary>Hard cap on nose slew rate, rad/s. Stops any numerical blow-up.</summary>
    public double MaxSlewRateRad { get; init; } = 3.0;

    // --- Directional stability ---
    /// <summary>
    /// How hard the tail pulls the nose back toward the airflow, per radian of
    /// angle of attack. This sets the trim angle a sustained pull settles at.
    /// </summary>
    public double WeathercockGain { get; init; } = 3.0;
    /// <summary>Dynamic pressure at which weathercock reaches full strength, in Pa.</summary>
    public double WeathercockRefQ { get; init; } = 600.0;

    // --- Roll ---
    /// <summary>Seconds for a 180 degree roll.</summary>
    public double HalfRollSeconds { get; init; } = 0.35;
    /// <summary>Turn authority still available at the knife-edge midpoint of a roll.</summary>
    public double MidRollAuthority { get; init; } = 0.30;

    // --- Flat turn (the reversal through the screen) ---
    /// <summary>Seconds to swap ends with a flat 180. The vulnerability window.</summary>
    public double FlatTurnSeconds { get; init; } = 0.95;
    /// <summary>Fraction of airspeed the flat turn costs. This is what you pay instead of altitude.</summary>
    public double FlatTurnSpeedCost { get; init; } = 0.22;
    /// <summary>Downward drift during the turn, m/s. The turn is not perfectly coordinated.</summary>
    public double FlatTurnSagMps { get; init; } = 7.0;
    /// <summary>
    /// Peak bank angle through the flat turn. A level 180 is a banked turn, not a
    /// flat skid on rudder alone. This is what makes the maneuver look flown.
    /// </summary>
    public double FlatTurnBankPeakRad { get; init; } = 1.13;   // 65 degrees
    /// <summary>Nose-up pitch held through the turn to stop the bank dropping the nose.</summary>
    public double FlatTurnPitchRad { get; init; } = 0.12;      // 7 degrees

    // --- Inverted flight penalties ---
    /// <summary>Seconds inverted before a gravity-fed engine starts to starve.</summary>
    public double InvertedStarveDelayS { get; init; } = 2.0;
    /// <summary>Seconds from the first cough to fully starved.</summary>
    public double InvertedStarveRampS { get; init; } = 1.5;
    /// <summary>Power fraction left when fully starved. Not zero: the prop still windmills.</summary>
    public double StarvedPowerFloor { get; init; } = 0.15;
    /// <summary>How much faster the fuel system recovers than it starves.</summary>
    public double StarveRecoveryRate { get; init; } = 3.0;

    // --- Stall and spin ---
    /// <summary>Seconds stalled and slow before the aircraft departs into a spin.</summary>
    public double SpinOnsetSeconds { get; init; } = 0.6;
    /// <summary>Control authority left while spinning.</summary>
    public double SpinAuthority { get; init; } = 0.15;
    /// <summary>Autorotation rate in a spin, rad/s.</summary>
    public double SpinRotationRad { get; init; } = 1.1;

    // --- Geometry for hit detection ---
    /// <summary>Real length of the airframe, nose to tail.</summary>
    public double LengthM { get; init; } = 5.71;

    /// <summary>
    /// How much bigger than life the aircraft is drawn. A Camel is 5.7 m and the
    /// turn radius is 33 m, so at true scale it is a speck.
    ///
    /// This lives in the spec and not in the renderer because hit geometry has to
    /// match what the player can see. A shot that visually connects must count.
    /// </summary>
    public double VisualScale { get; init; } = 3.5;

    /// <summary>Half the length of the hit capsule's spine.</summary>
    public double HitHalfLengthM => LengthM * VisualScale * 0.40;
    /// <summary>Radius around that spine. Roughly the airframe's height plus wings.</summary>
    public double HitRadiusM => LengthM * VisualScale * 0.15;

    // --- Guns ---
    /// <summary>Twin Vickers, and no reloading. Trigger discipline is a skill.</summary>
    public int AmmoRounds { get; init; } = 500;
    public double RoundsPerSecond { get; init; } = 12.0;
    public double MuzzleVelocity { get; init; } = 745.0;
    /// <summary>Random spread per round, in radians. Keeps long-range fire honest.</summary>
    public double GunDispersionRad { get; init; } = 0.006;
    /// <summary>Every Nth round is a tracer.</summary>
    public int TracerEvery { get; init; } = 5;
    /// <summary>
    /// How fast the guns heat while firing. Turned up from a gentler first pass
    /// because spraying was winning fights: a sloppy pilot with a wide firing cone
    /// threw twice the lead and got away with it. Heat is the thing that makes a
    /// short aimed burst better than a long hopeful one.
    /// </summary>
    public double GunHeatPerSecond { get; init; } = 0.40;
    public double GunCoolPerSecond { get; init; } = 0.45;
    /// <summary>Jam chance per round at full heat. Zero when cold.</summary>
    public double JamChanceAtFullHeat { get; init; } = 0.055;
    /// <summary>Seconds to clear a jammed gun.</summary>
    public double JamClearSeconds { get; init; } = 1.5;
    /// <summary>Damage one round does to a component, before any multiplier.</summary>
    public double RoundDamage { get; init; } = 0.085;

    /// <summary>Level stall speed at sea level, m/s. Derived, not configured.</summary>
    public double StallSpeedSeaLevel =>
        Math.Sqrt(2.0 * MassKg * Atmosphere.Gravity /
                  (Atmosphere.SeaLevelDensity * WingAreaM2 * ClMax));

    /// <summary>
    /// Speed where lift-limited G first reaches the structural limit. Peak turn rate
    /// happens here. This is the number the whole dogfight orbits around.
    /// </summary>
    public double CornerSpeedSeaLevel => StallSpeedSeaLevel * Math.Sqrt(GLimit);

    /// <summary>
    /// The honest Sopwith Camel. Every number is either a published figure or derived
    /// from one. Keep this as the reference: it is what "correct" means.
    /// </summary>
    public static readonly AircraftSpec SopwithCamel = new() { Name = "Sopwith Camel F.1" };

    /// <summary>
    /// The Camel the game actually ships. A real WW1 airframe is a draggy kite with
    /// almost no power, and a max-G vertical reversal in one genuinely does not close.
    /// That is historically true and it is bad play, so the arcade preset cleans up
    /// the airframe and adds power until the Immelmann and the Split-S both work.
    /// M1 tunes these numbers against the original. Nothing here is sacred.
    /// </summary>
    public static readonly AircraftSpec CamelArcade = new()
    {
        Name = "Sopwith Camel (arcade)",
        Cd0 = 0.018,
        OswaldEfficiency = 0.92,
        EnginePowerW = 149_000.0,   // ~200 hp, up from the real 130
        PropEfficiency = 0.78,
        StaticThrustN = 4200.0,
        ClMax = 1.35,
        GLimit = 6.0,
        TurnRateScale = 1.15,
    };

    /// <summary>
    /// No wing, no drag, no engine, and an angle of attack it can never stall at.
    /// Tests use it to isolate gravity and check that the integrator holds energy.
    /// </summary>
    public static readonly AircraftSpec Ballistic = new()
    {
        Name = "Ballistic test body",
        WingAreaM2 = 0.0,
        Cd0 = 0.0,
        EnginePowerW = 0.0,
        StaticThrustN = 0.0,
        LiftSlopePerRad = 1e-6,   // pushes StallAlphaRad far out of reach
        WeathercockGain = 0.0,
    };
}
