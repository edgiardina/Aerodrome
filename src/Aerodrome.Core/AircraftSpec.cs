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

    /// <summary>
    /// Which prepared model to draw, without the extension. The renderer looks for
    /// res://models/&lt;ModelName&gt;.glb and falls back to the procedural airframe if
    /// it is not there, so a fresh clone with no downloaded models still runs.
    /// </summary>
    public string ModelName { get; init; } = "camel";

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

    /// <summary>
    /// How fast the elevator can rotate the airframe about its own centre of
    /// gravity, rad/s. This is NOT the turn rate.
    ///
    /// The two are different things, and conflating them is what made pitch feel
    /// like steering a barge. A turn rate is how fast the FLIGHT PATH bends, and
    /// that is limited by the lift the wing can make. Rotating the nose is what
    /// BUILDS the angle of attack that makes that lift in the first place, and it
    /// happens far faster. A real pilot yanks the stick, the nose comes up almost
    /// at once, and the aeroplane then arcs round after it.
    ///
    /// The old model rate-limited the nose itself by the turn rate, so the pilot
    /// had to wait for the flight path before the nose would answer. Sustained
    /// turn performance is unchanged by this: the nose still stops at the angle of
    /// attack the wing and the airframe allow. It only gets there quickly.
    /// </summary>
    public double ElevatorRateRad { get; init; } = 3.2;

    /// <summary>
    /// How far past the stall angle the nose may lead the flight path, as a
    /// multiple of the stall angle. At 1.0 the nose stops exactly where the wing
    /// runs out of lift, which keeps the whole corner-speed envelope intact.
    /// </summary>
    public double ElevatorLeadFactor { get; init; } = 1.0;

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
    /// <summary>
    /// Random spread per round, in radians.
    ///
    /// Must stay well below the best pilot's aim error or it masks skill entirely.
    /// Raised to 0.018 once, which is larger than an Ace's 0.011, and the Ace
    /// immediately stopped being the best pilot in the game: its precision was
    /// buried under the gun's own dice. Range falloff is the honest way to punish
    /// long shots. Dispersion is not.
    /// </summary>
    public double GunDispersionRad { get; init; } = 0.005;

    // --- Range effectiveness -------------------------------------------------
    // A head-on merge used to be two pilots holding the trigger and hoping, which
    // is a coin flip rather than a skill. These make a shot worth taking only when
    // it is worth taking: close, and from a position you had to earn.

    /// <summary>Inside this range a round does its full damage.</summary>
    public double PointBlankRangeM { get; init; } = 90.0;
    /// <summary>Past this range a round is doing almost nothing.</summary>
    public double MaxEffectiveRangeM { get; init; } = 320.0;
    /// <summary>Damage multiplier at and beyond MaxEffectiveRangeM.</summary>
    public double LongRangeDamageFloor { get; init; } = 0.12;
    /// <summary>
    /// Every Nth round is a tracer. Real belts were often one in five, but rounds
    /// cross a 380 m view in half a second, so one in five leaves about two streaks
    /// on screen and the player cannot read where their fire is going. One in three
    /// is the smallest change that makes a burst legible.
    /// </summary>
    public int TracerEvery { get; init; } = 3;
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
    /// <summary>
    /// Progress one pump of the charging handle makes toward clearing a jam.
    ///
    /// Four presses at 0.26. Deliberately a spam rather than a hold: working a
    /// jammed Vickers meant hammering the cocking lever, and it makes the player do
    /// something frantic at exactly the moment they cannot shoot back.
    /// </summary>
    public double JamClearPerPress { get; init; } = 0.26;

    /// <summary>How fast clearing progress bleeds away if you stop working at it.</summary>
    public double JamClearDecayPerSecond { get; init; } = 0.45;
    /// <summary>Damage one round does to a component, before any multiplier.</summary>
    public double RoundDamage { get; init; } = 0.085;

    /// <summary>
    /// Airframe taken off by every round that connects, whatever it hits. At 0.042
    /// an aircraft comes apart after about twenty-four hits, which is a few good
    /// bursts. This is the number that decides how long a fight lasts, and it wants
    /// to be slow enough that fire and a wounded pilot get a chance to matter first.
    /// </summary>
    public double RoundIntegrityDamage { get; init; } = 0.042;

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

        // The first pass peaked at 76 deg/s, which is a 4.7 second loop, and it
        // felt like flying a barge. Snappiness comes from raising the aerodynamic
        // ceiling, NOT from winding up TurnRateScale: that knob only speeds up the
        // nose, and the wing still cannot make the lift to match, so the angle of
        // attack runs away and the aircraft stalls in the middle of the turn.
        // Lighter airframe, more lift, more G. Now about 113 deg/s and a 3.2 s loop.
        MassKg = 545.0,
        ClMax = 1.95,
        GLimit = 8.5,
        TurnRateScale = 1.0,
        MaxSlewRateRad = 4.5,

        // The nose answers now. Sustained turn rate is untouched, because the nose
        // still stops dead at the angle of attack the wing and the airframe allow.
        // What goes away is the wait: the aircraft used to take most of a second to
        // work its way up to a hard pull, which read as a heavy, unwilling elevator.
        ElevatorRateRad = 5.5,
        ElevatorLeadFactor = 1.0,

        // A push at a third of a pull made "nose down" feel dead. Inversion still
        // has to cost something, but 40 percent is a penalty and 67 percent was a
        // punishment.
        PushFactor = 0.60,

        // The tail was fighting the pilot harder than it needed to.
        WeathercockGain = 2.0,
    };

    /// <summary>
    /// The Fokker Dr.I, and the reason there is more than one aircraft.
    ///
    /// Two identical aircraft can never disengage from each other: neither one can
    /// build the speed or the height to leave, so every fight is a turn fight until
    /// somebody dies. Giving the two sides genuinely different strengths is what
    /// creates the option to run, and what makes choosing your moment matter.
    ///
    /// The historical contrast is exactly the one the game wants. The triplane
    /// out-climbs and out-turns anything, and it is slow: three wings are a great
    /// deal of lift and a great deal of drag. The Camel is the faster aeroplane, so
    /// a Camel pilot who is losing a turn fight can extend away and come back, and
    /// a Dr.I pilot cannot chase them down. Neither one wins by default.
    /// </summary>
    public static readonly AircraftSpec FokkerDr1Arcade = new()
    {
        Name = "Fokker Dr.I (arcade)",
        ModelName = "dr1",

        LengthM = 5.77,
        MassKg = 495.0,          // lighter than the Camel
        WingAreaM2 = 18.66,      // three wings, and it is all lift
        WingSpanM = 7.20,

        // Turns and climbs better than anything else in the sky.
        ClMax = 2.20,
        GLimit = 9.0,
        TurnRateScale = 1.0,
        MaxSlewRateRad = 4.5,
        PushFactor = 0.60,
        WeathercockGain = 2.0,
        ElevatorRateRad = 5.8,   // short fuselage, big elevator, famously twitchy
        ElevatorLeadFactor = 1.0,

        // And pays for it in speed. Three wings, six struts and all that wire.
        Cd0 = 0.031,
        OswaldEfficiency = 0.86,
        EnginePowerW = 121_000.0,
        PropEfficiency = 0.75,
        StaticThrustN = 3900.0,

        // Twin Spandaus, same as the Vickers for now.
        HalfRollSeconds = 0.32,  // stubby span, rolls fast
        FlatTurnSeconds = 0.92,
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
