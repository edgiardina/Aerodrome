namespace Aerodrome.Core;

/// <summary>
/// The flight model. One static Step call advances one aircraft by one fixed tick.
///
/// Deterministic and allocation free. No wall-clock reads, no Random, no engine
/// types. The same start state plus the same input sequence always gives the same
/// result, which is what makes headless tuning, replay, and later netcode possible.
/// </summary>
public static class FlightModel
{
    /// <summary>The sim runs at this rate. Rendering interpolates between ticks.</summary>
    public const double TickRate = 120.0;
    public const double FixedDt = 1.0 / TickRate;

    public static void Step(
        AircraftState s,
        AircraftSpec spec,
        AircraftInput input,
        Arena arena,
        double dt = FixedDt)
    {
        if (!s.IsAlive) return;

        StepThrottle(s, spec, input, dt);
        double rollAuthority = StepRoll(s, spec, input, dt);

        // Aerodynamics work against the air, not against the ground. Wind changes
        // every energy trade in the arena, so it belongs here and not in a shader.
        Vec2 airVel = s.Velocity - arena.Wind;
        double v = airVel.Length;
        double rho = Atmosphere.Density(s.Position.Y);
        double q = 0.5 * rho * v * v;
        Vec2 velDir = v > 1e-6 ? airVel / v : Vec2.FromAngle(s.Theta);
        double velAngle = v > 1e-6 ? airVel.Angle : s.Theta;

        UpdateInversion(s, spec, dt);
        double thrust = ComputeThrust(s, spec, v);

        // Angle of attack, signed so that positive always means "toward the canopy".
        // Measuring it this way is what makes inverted flight fall out for free.
        double alpha = s.CanopySign * Angles.Delta(velAngle, s.Theta);
        s.Alpha = alpha;

        double cl = LiftCoefficient(alpha, spec);
        double cd = spec.Cd0 + spec.InducedDragFactor * cl * cl;
        s.IsStalled = Math.Abs(alpha) > spec.StallAlphaRad;

        double lift = q * spec.WingAreaM2 * cl * s.WingHealth;
        double drag = q * spec.WingAreaM2 * cd;
        Vec2 liftDir = velDir.PerpCcw * s.CanopySign;

        s.LoadFactor = lift / (spec.MassKg * Atmosphere.Gravity);

        Vec2 force = liftDir * lift
                   + velDir * -drag
                   + Vec2.FromAngle(s.Theta, thrust)
                   + new Vec2(0, -spec.MassKg * Atmosphere.Gravity);
        Vec2 accel = force / spec.MassKg;

        StepSpin(s, spec, v, rho, dt);
        StepHeading(s, spec, input, v, rho, rollAuthority, dt);
        Weathercock(s, spec, velAngle, alpha, q, dt);

        // Semi-implicit Euler. It is symplectic, so a ballistic arc keeps its energy
        // instead of bleeding it to integration error.
        s.Velocity += accel * dt;
        s.Position += s.Velocity * dt;

        s.Airspeed = (s.Velocity - arena.Wind).Length;
        s.EnergyHeightM = s.Airspeed * s.Airspeed / (2.0 * Atmosphere.Gravity) + s.Position.Y;

        EnforceBounds(s, arena, dt);
    }

    // --- Throttle -----------------------------------------------------------

    private static void StepThrottle(AircraftState s, AircraftSpec spec, AircraftInput input, double dt)
    {
        double target = Angles.Clamp(input.ThrottleCommand, 0.0, 1.0);
        double step = spec.ThrottleSlewPerSecond * dt;
        s.Throttle += Angles.Clamp(target - s.Throttle, -step, step);
    }

    // --- Roll ---------------------------------------------------------------

    /// <summary>
    /// Advance any roll in progress and return the turn authority still available.
    /// Halfway through a roll the wings are vertical, so there is almost no way to
    /// pull. That is why a roll under fire is a real commitment.
    /// </summary>
    private static double StepRoll(AircraftState s, AircraftSpec spec, AircraftInput input, double dt)
    {
        if (s.RollRemaining <= 0)
        {
            if (input.AileronRollPressed) s.RollRemaining = Angles.TwoPi;
            else if (input.RollPressed) s.RollRemaining = Math.PI;
        }

        if (s.RollRemaining > 0)
        {
            double rate = Math.PI / spec.HalfRollSeconds;
            double step = Math.Min(s.RollRemaining, rate * dt);
            s.RollAngle = Angles.Wrap0To2Pi(s.RollAngle + step);
            s.RollRemaining -= step;

            if (s.RollRemaining <= 1e-9)
            {
                s.RollRemaining = 0;
                // Snap to exactly upright or exactly inverted so thousands of rolls
                // never accumulate drift.
                s.RollAngle = Math.Round(s.RollAngle / Math.PI) * Math.PI;
                s.RollAngle = Angles.Wrap0To2Pi(s.RollAngle);
            }
        }

        double cosRoll = Math.Cos(s.RollAngle);
        s.CanopySign = cosRoll >= 0 ? 1 : -1;
        return spec.MidRollAuthority + (1.0 - spec.MidRollAuthority) * Math.Abs(cosRoll);
    }

    // --- Inverted flight ----------------------------------------------------

    /// <summary>
    /// A gravity-fed WW1 engine starves under sustained negative G. This is the
    /// clock that makes a bare half loop a gamble instead of a free reversal.
    /// </summary>
    private static void UpdateInversion(AircraftState s, AircraftSpec spec, double dt)
    {
        s.IsInverted = s.CanopySign * Math.Cos(s.Theta) < 0;

        if (s.IsInverted)
            s.InvertedTime += dt;
        else
            s.InvertedTime = Math.Max(0.0, s.InvertedTime - dt * spec.StarveRecoveryRate);

        if (s.InvertedTime > spec.InvertedStarveDelayS)
        {
            double over = (s.InvertedTime - spec.InvertedStarveDelayS) / spec.InvertedStarveRampS;
            double power = Math.Max(spec.StarvedPowerFloor,
                                    1.0 - over * (1.0 - spec.StarvedPowerFloor));
            s.FuelStarvation = 1.0 - power;
        }
        else
        {
            s.FuelStarvation = 0.0;
        }
    }

    // --- Powerplant ---------------------------------------------------------

    private static double ComputeThrust(AircraftState s, AircraftSpec spec, double airspeed)
    {
        double scale = s.Throttle
                     * Atmosphere.PowerFraction(s.Position.Y, spec.AbsoluteCeilingM)
                     * (1.0 - s.FuelStarvation)
                     * s.EngineHealth;

        double staticCap = spec.StaticThrustN * scale;
        if (airspeed < 1e-3) return staticCap;

        // A propeller delivers power, not force. Thrust falls as speed rises, which
        // is what gives the aircraft a natural top speed with no artificial limiter.
        double fromPower = spec.EnginePowerW * spec.PropEfficiency * scale / airspeed;
        return Math.Min(staticCap, fromPower);
    }

    // --- Lift ---------------------------------------------------------------

    /// <summary>
    /// Linear up to the stall, then a fall-off to a deep-stall floor. The wing never
    /// reaches zero lift, so a stalled aircraft mushes instead of dropping like a rock.
    /// </summary>
    public static double LiftCoefficient(double alpha, AircraftSpec spec)
    {
        double magnitude = Math.Abs(alpha);
        double sign = Math.Sign(alpha);

        if (magnitude <= spec.StallAlphaRad)
            return spec.LiftSlopePerRad * alpha;

        double past = Math.Min(1.0, (magnitude - spec.StallAlphaRad) / spec.PostStallRangeRad);
        double fraction = 1.0 - past * (1.0 - spec.DeepStallClFraction);
        return sign * spec.ClMax * fraction;
    }

    // --- Turning ------------------------------------------------------------

    /// <summary>
    /// Peak nose slew rate at a given airspeed.
    ///
    /// Below corner speed the wing cannot make the G, so the turn is lift limited.
    /// Above it the airframe cannot take the G, so the turn is structure limited and
    /// the radius grows with speed. The peak between the two is corner speed, and the
    /// whole dogfight is a fight to sit on it.
    /// </summary>
    public static double MaxSlewRate(double airspeed, double density, AircraftSpec spec)
    {
        if (airspeed < 1e-3) return 0.0;

        double q = 0.5 * density * airspeed * airspeed;
        double liftLimitedG = q * spec.WingAreaM2 * spec.ClMax / (spec.MassKg * Atmosphere.Gravity);
        double n = Math.Min(liftLimitedG, spec.GLimit);

        // Below 1 G the wing cannot even hold level flight. Leave a trace of authority
        // so a stalled aircraft still answers a little, but only a little.
        if (n <= 1.0)
            return Math.Max(0.0, n) * 0.15 * spec.TurnRateScale;

        double omega = Atmosphere.Gravity * Math.Sqrt(n * n - 1.0) / airspeed;
        return Math.Min(omega * spec.TurnRateScale, spec.MaxSlewRateRad);
    }

    private static void StepHeading(
        AircraftState s, AircraftSpec spec, AircraftInput input,
        double airspeed, double density, double rollAuthority, double dt)
    {
        s.SlewRateRad = 0.0;
        if (!input.HeadingCommand.HasValue) return;

        double error = Angles.Delta(s.Theta, input.HeadingCommand.Value);
        if (Math.Abs(error) < 1e-9) return;

        double authority = rollAuthority * s.ControlHealth;
        if (s.IsStalled) authority *= 0.35;
        if (s.IsSpinning) authority *= spec.SpinAuthority;

        double maxRate = MaxSlewRate(airspeed, density, spec) * authority;

        // The one rule that gives inversion its bite. A pull runs at the full G
        // limit. A push does not. Turning the wrong way up means every fast turn
        // you have points the wrong way, so you have to spend a roll to fix it.
        bool isPull = Math.Sign(error) == s.CanopySign;
        if (!isPull) maxRate *= spec.PushFactor;

        double step = Angles.Clamp(error, -maxRate * dt, maxRate * dt);
        s.Theta = Angles.Wrap0To2Pi(s.Theta + step);
        s.SlewRateRad = step / dt;
    }

    /// <summary>
    /// The tail pulls the nose back toward the airflow, harder the further off it is.
    /// In normal flight this just sets the trim angle of attack. In a stall it is what
    /// makes the nose fall.
    /// </summary>
    private static void Weathercock(
        AircraftState s, AircraftSpec spec, double velAngle, double alpha, double q, double dt)
    {
        double toAirflow = Angles.Delta(s.Theta, velAngle);
        if (Math.Abs(toAirflow) < 1e-9) return;

        double qFactor = Math.Min(1.0, q / spec.WeathercockRefQ);
        double rate = spec.WeathercockGain * Math.Abs(alpha) * qFactor;
        double step = Angles.Clamp(toAirflow, -rate * dt, rate * dt);
        s.Theta = Angles.Wrap0To2Pi(s.Theta + step);
    }

    // --- Departure ----------------------------------------------------------

    private static void StepSpin(AircraftState s, AircraftSpec spec, double airspeed, double density, double dt)
    {
        double stallSpeed = StallSpeed(spec, density);

        if (s.IsStalled && airspeed < stallSpeed)
            s.SpinTime += dt;
        else
            s.SpinTime = Math.Max(0.0, s.SpinTime - dt * 2.0);

        if (!s.IsSpinning && s.SpinTime > spec.SpinOnsetSeconds)
            s.IsSpinning = true;

        if (!s.IsSpinning) return;

        // Recover by getting the nose down and the speed back. Same as the real thing.
        if (airspeed > stallSpeed * 1.15 && Math.Abs(s.Alpha) < spec.StallAlphaRad)
        {
            s.IsSpinning = false;
            s.SpinTime = 0.0;
            return;
        }

        // Autorotation drags the nose toward straight down.
        double toDown = Angles.Delta(s.Theta, -Angles.HalfPi);
        int direction = toDown >= 0 ? 1 : -1;
        s.Theta = Angles.Wrap0To2Pi(s.Theta + direction * spec.SpinRotationRad * dt);
    }

    public static double StallSpeed(AircraftSpec spec, double density)
        => Math.Sqrt(2.0 * spec.MassKg * Atmosphere.Gravity / (density * spec.WingAreaM2 * spec.ClMax));

    // --- Arena bounds -------------------------------------------------------

    private static void EnforceBounds(AircraftState s, Arena arena, double dt)
    {
        if (s.Position.Y <= 0.0)
        {
            s.Position = new Vec2(s.Position.X, 0.0);
            s.IsAlive = false;
            s.Death = DeathCause.Ground;
            return;
        }

        if (s.Position.Y >= arena.CeilingM)
        {
            // The ceiling is a wall, not a kill. The engine has already quit up here.
            s.Position = new Vec2(s.Position.X, arena.CeilingM);
            if (s.Velocity.Y > 0) s.Velocity = new Vec2(s.Velocity.X, 0.0);
        }

        if (arena.IsInsideWalls(s.Position))
        {
            s.OutOfBoundsTime = Math.Max(0.0, s.OutOfBoundsTime - dt);
            return;
        }

        s.OutOfBoundsTime += dt;
        if (s.OutOfBoundsTime >= arena.FleeTimeoutS)
        {
            s.IsAlive = false;
            s.Death = DeathCause.Fled;
        }
    }
}
