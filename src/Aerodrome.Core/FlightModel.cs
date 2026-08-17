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

        // Control power comes from dynamic pressure, so it has to be known before
        // any surface moves. Reads last tick's airspeed, which is a hundred and
        // twentieth of a second stale and cannot meaningfully differ.
        bool stalledNow = StalledNow(s, spec, arena);
        double surfaceAuthority =
            SurfaceAuthority(s, spec, s.Airspeed, Atmosphere.Density(s.Position.Y), stalledNow);

        s.RollRefused = false;
        s.BreakRefused = false;

        // The pilot gets their breath back, but only while not mid-break.
        if (!s.IsBreaking)
            s.Reserve = Math.Min(1.0, s.Reserve + spec.ReserveRecoveryPerSecond * dt);

        // A flat turn takes the aircraft out of normal flight for about a second.
        // It runs its own integration and skips the rest of the model.
        if (StepFlatTurn(s, spec, input, arena, surfaceAuthority, stalledNow, dt)) return;

        StepThrottle(s, spec, input, dt);
        double rollAuthority = StepRoll(s, spec, input, surfaceAuthority, dt);

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
        StepHeading(s, spec, input, v, rho, rollAuthority * surfaceAuthority, dt);
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
    private static double StepRoll(
        AircraftState s, AircraftSpec spec, AircraftInput input, double authority, double dt)
    {
        if (s.RollRemaining <= 0)
        {
            // No air over the ailerons, no roll. Refusing to START is deliberate:
            // beginning a roll you cannot finish would leave the pilot stuck on
            // knife edge with no lift and no way out, which is a worse answer than
            // simply not answering.
            bool canCommit = authority >= MinimumAuthorityToCommit;

            if (canCommit)
            {
                if (input.AileronRollPressed)
                {
                    // A defensive break. Only if there is enough pilot left to fly
                    // it, because a spent pilot throwing a fourth break in a row is
                    // the thing this mechanic exists to prevent.
                    if (s.Reserve >= spec.BreakCost)
                    {
                        s.RollRemaining = Angles.TwoPi;
                        s.IsBreaking = true;
                        s.Reserve = Math.Max(0.0, s.Reserve - spec.BreakCost);
                        s.Velocity *= 1.0 - spec.BreakSpeedCost;
                    }
                    else
                    {
                        s.BreakRefused = true;
                    }
                }
                else if (input.RollPressed) s.RollRemaining = Math.PI;
            }
            else if (input.AileronRollPressed || input.RollPressed)
            {
                s.RollRefused = true;
            }
        }

        if (s.RollRemaining > 0)
        {
            // A roll already under way keeps going, but at the rate the air allows.
            // Bleed off mid-roll and it turns into a slow wallow, which is exactly
            // what it should feel like.
            double rate = Math.PI / spec.HalfRollSeconds * Math.Max(0.15, authority);
            double step = Math.Min(s.RollRemaining, rate * dt);
            s.RollAngle = Angles.Wrap0To2Pi(s.RollAngle + step);
            s.RollRemaining -= step;

            if (s.RollRemaining <= 1e-9)
            {
                s.RollRemaining = 0;
                s.IsBreaking = false;
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

    // --- Flat turn ----------------------------------------------------------

    /// <summary>
    /// The third way to reverse, and the only one that keeps your altitude.
    ///
    /// The aircraft banks and yaws a flat 180 through the screen depth. A loop
    /// trades height for the reversal. This trades speed and about a second of
    /// helplessness instead. It is the answer to being roped, and it is the reason
    /// a slow high opponent is not automatically safe.
    ///
    /// While it runs, the nose points into or out of the screen, so the guns cannot
    /// bear on anything. The X velocity is the projection of a constant-speed 180
    /// onto our plane, so it passes through zero halfway. That pause is the whole
    /// cost, and it is what a good opponent shoots at.
    ///
    /// Honesty note: a real flat 180 at 60 m/s takes about 11 seconds and 200 m of
    /// radius. This takes one second. The geometry hides in the Z axis where the
    /// player cannot see it, so the cheat is invisible and the maneuver plays the
    /// way the original played. See docs/FEEL.md.
    /// </summary>
    /// <returns>True when the flat turn owns this tick and the caller must stop.</returns>
    private static bool StepFlatTurn(
        AircraftState s, AircraftSpec spec, AircraftInput input, Arena arena,
        double surfaceAuthority, bool stalledNow, double dt)
    {
        if (!s.IsFlatTurning)
        {
            if (!input.FlatTurnPressed || s.RollRemaining > 0 || s.IsSpinning) return false;

            // You cannot swap ends without enough air over the wing. Too slow means
            // you have to dive for speed first, which costs you the altitude you
            // were trying to keep.
            //
            // Stalled counts as too slow even if the number says otherwise: this is
            // a rudder and aileron maneuver, and a separated wing will not fly it.
            double rhoNow = Atmosphere.Density(s.Position.Y);
            if (stalledNow ||
                surfaceAuthority < MinimumAuthorityToCommit ||
                s.Airspeed < StallSpeed(spec, rhoNow) * 1.05)
            {
                s.RollRefused = true;
                return false;
            }

            s.BeginFlatTurn();
        }

        s.FlatTurnProgress = Math.Min(1.0, s.FlatTurnProgress + dt / spec.FlatTurnSeconds);
        double p = s.FlatTurnProgress;

        StepThrottle(s, spec, input, dt);

        // Track the eased yaw, not raw progress, so the flight path and the model
        // agree. The aircraft rolls in, whips through the middle, and rolls out.
        double yawFraction = s.FlatTurnYawFraction;
        double speedScale = 1.0 - spec.FlatTurnSpeedCost * p;
        double vx = s.FlatTurnEntryVx * Math.Cos(yawFraction * Math.PI) * speedScale;
        double vy = s.FlatTurnEntryVy * speedScale - spec.FlatTurnSagMps * Math.Sin(yawFraction * Math.PI);

        s.Velocity = new Vec2(vx, vy);
        s.Position += s.Velocity * dt;

        // Airspeed is the TRUE speed through space, not the in-plane projection.
        // Halfway round the turn almost all of the velocity points into the screen,
        // where we do not simulate, so the projection reads near zero. The aircraft
        // is flying perfectly well. Reporting the projection would light up every
        // stall warning on the panel and lie to the pilot.
        s.Airspeed = s.FlatTurnEntrySpeed * speedScale;
        s.EnergyHeightM = s.Airspeed * s.Airspeed / (2.0 * Atmosphere.Gravity) + s.Position.Y;
        s.Alpha = 0.0;
        s.LoadFactor = 1.0;
        s.SlewRateRad = 0.0;
        s.IsStalled = false;

        if (p >= 1.0)
        {
            // Commit. Mirror the heading about the vertical, and flip the canopy so
            // the aircraft leaves the turn the same way up it went in. Doing both
            // keeps the rendered transform continuous, so there is no visual pop.
            s.Theta = Angles.Wrap0To2Pi(Math.PI - s.FlatTurnEntryTheta);
            s.CanopySign = -s.CanopySign;
            s.RollAngle = s.CanopySign > 0 ? 0.0 : Math.PI;
            s.FlatTurnProgress = 0.0;
        }

        UpdateInversion(s, spec, dt);
        EnforceBounds(s, arena, dt);
        return true;
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
    /// <summary>
    /// <paramref name="gScale"/> is how much of the airframe's G limit the pilot
    /// can currently stand. One is a fresh pilot.
    /// </summary>
    public static double MaxSlewRate(double airspeed, double density, AircraftSpec spec, double gScale = 1.0)
    {
        if (airspeed < 1e-3) return 0.0;

        double q = 0.5 * density * airspeed * airspeed;
        double liftLimitedG = q * spec.WingAreaM2 * spec.ClMax / (spec.MassKg * Atmosphere.Gravity);
        double n = Math.Min(liftLimitedG, spec.GLimit * gScale);

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

        // Two control schemes, one set of limits.
        //
        // A stick deflection is a RATE, not a destination, so it is turned into a
        // heading error big enough that the rate limiting below is what caps it.
        // Both schemes then pass through identical G, damage and roll authority,
        // which means switching control style cannot change what the aircraft can
        // physically do.
        double error;
        double stickScale = 1.0;

        if (input.PitchStick is { } stick)
        {
            if (Math.Abs(stick) < 1e-6) return;
            // Positive is back stick: a pull, which always goes toward the canopy.
            // Inverted, that points at the ground, exactly as a real stick would.
            error = Math.Sign(stick) * s.CanopySign * Math.PI;
            // Half deflection is half the turn rate, so the stick is proportional.
            stickScale = Math.Min(1.0, Math.Abs(stick));
        }
        else if (input.HeadingCommand.HasValue)
        {
            error = Angles.Delta(s.Theta, input.HeadingCommand.Value);
        }
        else return;

        if (Math.Abs(error) < 1e-9) return;

        // EffectiveControl, not ControlHealth: a shot-away tail and a wounded pilot
        // both cost you the nose, and the player should feel that before they die.
        //
        // The caller has already folded in the dynamic pressure term, which carries
        // the stall and spin penalties with it. Applying them again here would
        // square them.
        double authority = rollAuthority * s.EffectiveControl;

        // The pilot's own G tolerance, spent on defensive breaks and recovered by
        // not flying any. This is the price of the break, and it is felt in the
        // sustained turn rather than in how quickly the stick answers.
        double maxRate = MaxSlewRate(airspeed, density, spec, s.PilotGTolerance) * authority * stickScale;

        // The one rule that gives inversion its bite. A pull runs at the full G
        // limit. A push does not. Turning the wrong way up means every fast turn
        // you have points the wrong way, so you have to spend a roll to fix it.
        bool isPull = Math.Sign(error) == s.CanopySign;
        if (!isPull) maxRate *= spec.PushFactor;

        maxRate = Math.Max(maxRate, ElevatorRate(s, spec, airspeed, density, isPull, authority, stickScale));

        double step = Angles.Clamp(error, -maxRate * dt, maxRate * dt);
        step = LimitByStructure(s, spec, airspeed, density, step);

        s.Theta = Angles.Wrap0To2Pi(s.Theta + step);
        s.SlewRateRad = step / dt;
    }

    /// <summary>
    /// How fast the nose may swing right now, over and above the rate at which the
    /// flight path can bend.
    ///
    /// The nose is allowed to LEAD the flight path, because that is what an
    /// elevator does. The lead is the angle of attack, so the only question is how
    /// much angle of attack the aircraft is still allowed, and there are two limits
    /// on it:
    ///
    ///   The wing. Past the stall angle there is no more lift to be had, so hauling
    ///   the nose further only stalls you.
    ///
    ///   The airframe. At speed the structural G limit is reached well before the
    ///   stall angle, and if the elevator could ignore that it would out-turn the G
    ///   limit and corner speed would stop meaning anything. Corner speed is the
    ///   number the whole dogfight orbits around, so this limit is not optional.
    ///
    /// Once the nose is against whichever limit binds, this returns zero and the
    /// turn goes back to being governed by lift, exactly as before. The envelope is
    /// identical. Only the time taken to reach it changes.
    /// </summary>
    private static double ElevatorRate(
        AircraftState s, AircraftSpec spec, double airspeed, double density,
        bool isPull, double authority, double stickScale)
    {
        // The pilot's limit, not the airframe's: a spent pilot must not be able to
        // snap the nose to an angle of attack they could not hold anyway.
        double limit = Math.Min(spec.StallAlphaRad * spec.ElevatorLeadFactor,
                                StructuralAlpha(spec, airspeed, density, s.PilotGTolerance));

        // Alpha is signed so that positive is toward the canopy, which is a pull.
        // A pull may keep going while alpha is under the limit. A push may keep
        // going while it is above the negative of it.
        bool headroom = isPull ? s.Alpha < limit : s.Alpha > -limit;
        if (!headroom) return 0.0;

        double rate = spec.ElevatorRateRad * authority * stickScale;
        return isPull ? rate : rate * spec.PushFactor;
    }

    /// <summary>
    /// The angle of attack at which the wing makes exactly the structural G limit.
    /// Infinite when there is no wing or no air, which is what the test bodies want.
    /// </summary>
    private static double StructuralAlpha(
        AircraftSpec spec, double airspeed, double density, double gScale = 1.0)
    {
        double q = 0.5 * density * airspeed * airspeed;
        if (q < 1e-6 || spec.WingAreaM2 < 1e-9 || spec.LiftSlopePerRad < 1e-9)
            return double.PositiveInfinity;

        return spec.GLimit * gScale * spec.MassKg * Atmosphere.Gravity
             / (q * spec.WingAreaM2 * spec.LiftSlopePerRad);
    }

    /// <summary>
    /// Stop one tick of nose movement carrying the angle of attack past what the
    /// airframe can take.
    ///
    /// ElevatorRate only GATES on the angle of attack at the start of the tick, and
    /// a gate is not a limit. At diving speed the whole structural budget is about
    /// three degrees, while one tick of elevator is two and a half, so a pull that
    /// started legal finished at fourteen G against a limit of eight and a half.
    /// On an undamaged aeroplane nothing checked it. On one with a single hit in
    /// the wing it was instant death, which is not the trade the damage model
    /// intends and is not something the pilot could see coming.
    ///
    /// Only the STRUCTURAL angle is clamped, never the stall angle. Above corner
    /// speed the structure binds first, which is correct. Below it the structural
    /// angle is the larger of the two and this does nothing, so pulling into a
    /// stall and departing still works exactly as before.
    /// </summary>
    private static double LimitByStructure(
        AircraftState s, AircraftSpec spec, double airspeed, double density, double step)
    {
        double allowed = StructuralAlpha(spec, airspeed, density);
        if (double.IsPositiveInfinity(allowed)) return step;

        double after = s.Alpha + s.CanopySign * step;
        if (Math.Abs(after) <= allowed) return step;

        // Already outside it, from a speed change or from the tail, and this step
        // is bringing it back in. Leave it alone: recovering is not the thing that
        // needs limiting.
        if (Math.Abs(after) <= Math.Abs(s.Alpha)) return step;

        double target = Math.Sign(after) * allowed;
        double clamped = (target - s.Alpha) * s.CanopySign;

        return Math.Abs(clamped) < Math.Abs(step) ? clamped : step;
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

    /// <summary>Airspeed, as a multiple of the stall, at which the surfaces have full bite.</summary>
    private const double FullAuthoritySpeedFactor = 1.3;

    /// <summary>
    /// How much bite the control surfaces have right now, 0 to 1.
    ///
    /// Ailerons, elevator and rudder all work by deflecting air across a surface,
    /// so their power goes with dynamic pressure and therefore with the SQUARE of
    /// airspeed. Below flying speed they are cloth in a breeze.
    ///
    /// This was missing from the roll entirely: the roll rate was a flat
    /// PI / HalfRollSeconds, so an aeroplane hanging on its propeller at walking
    /// pace could snap inverted exactly as fast as one doing 400 km/h. Recovering
    /// from a stall by rolling is not a thing, and it took away the reason to keep
    /// your speed up.
    ///
    /// In ordinary flight this is simply 1. The Camel stalls at 14 m/s and fights
    /// between 60 and 110, so nothing here touches normal handling. It only bites
    /// where it should: at the top of a botched loop, hanging in a stall, or in a
    /// spin.
    /// </summary>
    public static double SurfaceAuthority(
        AircraftState s, AircraftSpec spec, double airspeed, double density, bool stalled)
    {
        double stall = StallSpeed(spec, density);
        if (stall < 1e-6) return 1.0;

        double ratio = airspeed / (stall * FullAuthoritySpeedFactor);
        double q = Math.Clamp(ratio * ratio, 0.0, 1.0);

        // A stalled wing has separated flow over the ailerons whatever the speed,
        // which is why a stall drops a wing rather than answering the stick.
        if (stalled) q *= 0.35;
        if (s.IsSpinning) q *= spec.SpinAuthority;

        return q;
    }

    /// <summary>
    /// Is the wing stalled RIGHT NOW, from this tick's geometry?
    ///
    /// s.IsStalled is left over from the previous tick, because it is not set until
    /// the aerodynamics run, and the roll and the flat turn are both decided before
    /// that. One tick of staleness never matters in continuous flight, but it means
    /// a maneuver committed on the first tick after a state change is judged on the
    /// wrong picture. These are commit-once decisions, so they get the real answer.
    ///
    /// Magnitude only, so which way up the aeroplane is does not come into it.
    /// </summary>
    private static bool StalledNow(AircraftState s, AircraftSpec spec, Arena arena)
    {
        Vec2 airVel = s.Velocity - arena.Wind;
        if (airVel.LengthSquared < 1e-12) return false;

        return Math.Abs(Angles.Delta(airVel.Angle, s.Theta)) > spec.StallAlphaRad;
    }

    /// <summary>
    /// Below this there is not enough air over the surfaces to start a maneuver
    /// that cannot be abandoned half way. Rolling and swapping ends both commit.
    /// </summary>
    private const double MinimumAuthorityToCommit = 0.30;

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
