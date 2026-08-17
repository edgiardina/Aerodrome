using System;

namespace Aerodrome.Core;

public enum Behavior { Merge, TurnFight, BoomAndZoom, Extend, Scissors, Disengage, Support }

/// <summary>
/// An opponent that flies the aircraft, rather than being flown by the game.
///
/// It produces an AircraftInput and nothing else. It cannot set its heading, its
/// roll, or its position directly, it has to ask for them the same way the player
/// does, and it has to spend the same second in a flat turn with its guns masked.
///
/// It thinks in energy. Energy height, the altitude you could reach by trading all
/// your speed away, is the number that decides whether you can afford to turn with
/// someone or whether you have to run and rebuild.
/// </summary>
public sealed class PilotAi
{
    private const int HistoryTicks = 96;   // 0.8 s at 120 Hz, covers the slowest skill

    public AiSkill Skill { get; }
    public Behavior Current { get; private set; } = Behavior.Merge;

    private readonly Vec2[] _targetPos = new Vec2[HistoryTicks];
    private readonly Vec2[] _targetVel = new Vec2[HistoryTicks];
    private int _historyHead;
    private int _historyFilled;

    private Rng _rng;
    /// <summary>
    /// Shortest time a pilot will stick with a plan before changing it.
    ///
    /// Without this, thinking faster is a HANDICAP. An Ace re-deciding every 0.18 s
    /// flip-flopped between turning and extending and never carried either through,
    /// and it lost to pilots that simply committed: 32 percent against a Veteran.
    /// Adding commitment took it straight back to winning. Reacting quickly and
    /// changing your mind constantly are not the same thing, and only the first one
    /// is skill. Breaking off is exempt, because that is always urgent.
    /// </summary>
    private const double MinimumCommitmentS = 0.85;

    private double _behaviourHeld;
    private double _sinceDecision;
    private double _aimError;
    private double _jamPumpTimer;
    private double _scissorsTimer;
    private int _scissorsSide = 1;
    private Combatant? _lastTarget;
    private double _targetTurnRate;

    public PilotAi(AiSkill skill, uint seed)
    {
        Skill = skill;
        _rng = new Rng(seed);
        _sinceDecision = _rng.NextDouble() * skill.DecisionPeriodS;   // desynchronise pilots
    }

    /// <summary>
    /// Fly one tick. <paramref name="orders"/> is what the pilot's flight has told
    /// it to do. Leave it out and the pilot fights alone, which is what a single
    /// opponent has always done.
    /// </summary>
    public AircraftInput Fly(Combatant self, Combatant? enemy, Arena arena, double dt,
                             FlightOrders orders = default)
    {
        var s = self.State;
        if (!s.IsAlive) return AircraftInput.Neutral;

        // Committed maneuvers finish. The AI has to live with them like anyone else.
        if (s.IsFlatTurning) return new AircraftInput { ThrottleCommand = 1.0 };

        if (enemy is null || !enemy.IsAlive)
            return Cruise(self, arena);

        // The delayed picture is a history of ONE aircraft. Switching targets and
        // keeping the buffer would have the pilot lead the new target using the old
        // one's track, which is not a reaction time, it is a hallucination.
        if (!ReferenceEquals(enemy, _lastTarget))
        {
            _lastTarget = enemy;
            _historyHead = 0;
            _historyFilled = 0;
        }

        RecordTarget(enemy.State);
        GetDelayedTarget(out Vec2 targetPos, out Vec2 targetVel, out _targetTurnRate);

        _sinceDecision += dt;
        _behaviourHeld += dt;

        if (_sinceDecision >= Skill.DecisionPeriodS)
        {
            _sinceDecision = 0;
            _aimError = Wander(_aimError, Skill.AimErrorRad);

            Behavior wanted = Decide(self, enemy, targetPos, arena, orders);
            if (wanted != Current && (_behaviourHeld >= MinimumCommitmentS || wanted == Behavior.Disengage))
            {
                Current = wanted;
                _behaviourHeld = 0;
            }
        }

        // Being ordered off the attack is not a change of mind, so it does not wait
        // for the commitment timer. A pilot the flight has pulled out has to break
        // now, or two aircraft press the same target and the coordination is a lie.
        if (orders.Role == FlightRole.Supporting && Current is Behavior.TurnFight or Behavior.Merge or Behavior.BoomAndZoom)
            Current = Behavior.Support;

        return Execute(self, enemy, targetPos, targetVel, arena, orders);
    }

    // --- Deciding what to do ------------------------------------------------

    private Behavior Decide(Combatant self, Combatant enemy, Vec2 targetPos, Arena arena,
                            FlightOrders orders)
    {
        var s = self.State;
        var e = enemy.State;

        double range = (targetPos - s.Position).Length;
        double energyEdge = s.EnergyHeightM - e.EnergyHeightM;
        double damage = 1.0 - Math.Min(Math.Min(s.EngineHealth, s.WingHealth), s.TailHealth);

        if (damage > Skill.BreakOffDamage || s.OnFire) return Behavior.Disengage;

        // Is the enemy on my tail, close and pointed at me?
        double theirAngleOff = Math.Abs(Angles.Delta(e.Theta, (s.Position - e.Position).Angle));
        bool onMySix = range < 260 && theirAngleOff < 0.55;

        if (onMySix)
        {
            // Out-turn them if I have the energy to. Otherwise force an overshoot.
            return energyEdge < -40 ? Behavior.Extend : Behavior.Scissors;
        }

        // Everything above this line is self preservation, and no order outranks
        // it. Below it, a supporting pilot does as it is told.
        if (orders.Role == FlightRole.Supporting) return Behavior.Support;

        if (range > 520) return Behavior.Merge;

        // A big height advantage is worth spending in a dive and rebuilding after.
        if (energyEdge > 90) return Behavior.BoomAndZoom;

        // Badly out-energised in a knife fight is how you die. Reset instead.
        // The threshold is deliberately high: bailing out of a fight easily costs
        // more than staying in one, because disengaging spends altitude and space
        // and gives the other pilot a free shot at your tail.
        if (energyEdge < -170) return Behavior.Extend;

        return Behavior.TurnFight;
    }

    // --- Doing it -----------------------------------------------------------

    private AircraftInput Execute(
        Combatant self, Combatant enemy, Vec2 targetPos, Vec2 targetVel, Arena arena,
        FlightOrders orders)
    {
        var s = self.State;
        var spec = self.Spec;

        double desired;
        double throttle = 1.0;
        bool preferFlatTurn = false;

        switch (Current)
        {
            case Behavior.Disengage:
                // Head for the nearest wall, shallow, away from the fight.
                desired = Descend(s.Position.X < arena.WidthM * 0.5 ? Math.PI : 0.0, 0.20);
                break;

            case Behavior.Extend:
                // Run to rebuild energy, but run INTO the arena, not out of it.
                // Extending straight away from the enemy walks you into a wall, and
                // leaving the field loses the round just as surely as being shot.
                desired = (targetPos - s.Position).Angle + Math.PI;
                if (Math.Abs(s.Position.X - arena.WidthM * 0.5) > arena.WidthM * 0.30)
                    desired = s.Position.X > arena.WidthM * 0.5 ? Math.PI : 0.0;
                desired = Climb(desired, s.Position.Y < 320 ? 0.30 : 0.12);
                preferFlatTurn = true;
                break;

            case Behavior.Support:
                desired = HoldStation(s, orders.Station, targetPos, out throttle);
                break;

            case Behavior.Scissors:
                // Repeated reversals. Whoever can bleed speed hardest wins this.
                // Measured against the pull direction, so it is the same maneuver
                // whichever way the aircraft happens to be facing.
                _scissorsTimer += FlightModel.FixedDt;
                if (_scissorsTimer > 1.1) { _scissorsTimer = 0; _scissorsSide = -_scissorsSide; }
                desired = s.Theta + _scissorsSide * s.CanopySign * 1.2;
                throttle = 0.55;
                break;

            case Behavior.BoomAndZoom:
                // Dive on them, take the shot, and keep the speed for the climb out.
                desired = Pursue(self, targetPos, targetVel);
                break;

            case Behavior.Merge:
                desired = (targetPos - s.Position).Angle;
                // Come in a little high. Height is the currency.
                if (s.EnergyHeightM < enemy.State.EnergyHeightM + 60) desired = Climb(desired, 0.22);
                break;

            default:
                desired = Pursue(self, targetPos, targetVel);
                preferFlatTurn = s.Position.Y < 260;   // no room to loop down here
                break;
        }

        // Never point the nose down on purpose when there is no room underneath.
        // Chasing a target toward the deck is how a pursuit turns into two wrecks,
        // and the worse the pilot the more often it happened: Rookies were flying
        // into the ground in half of all rounds.
        const double SoftFloor = 260.0;
        if (s.Position.Y < SoftFloor && Math.Sin(desired) < 0)
            desired = Math.Cos(desired) >= 0 ? 0.0 : Math.PI;

        throttle = ManageEnergy(s, spec, throttle);

        bool emergency = AvoidBoundaries(s, arena, ref desired);
        if (emergency) { throttle = 1.0; preferFlatTurn = false; }

        bool fire = ShouldFire(self, targetPos, targetVel, opportunistOnly: Current == Behavior.Support);
        var command = Steer(s, spec, desired, throttle, fire, preferFlatTurn, emergency);

        // Work a jam the same way the player has to: pump the handle. Without this
        // the AI's guns stay jammed for the rest of the round, which quietly turned
        // gun heat into a one-sided punishment.
        return command with { ClearJamPressed = WorkTheJam(s) };
    }

    /// <summary>
    /// Turn "point the nose there" into actual controls.
    ///
    /// This is where the AI has to make the same choice the player does. A pull is
    /// fast and a push is not, so if the heading it wants is on the wrong side it
    /// must either roll first or swap ends with a flat turn. Rolling costs the roll
    /// time and a climb or dive. The flat turn costs a second of masked guns and a
    /// fifth of its speed but keeps the altitude.
    /// </summary>
    private AircraftInput Steer(
        AircraftState s, AircraftSpec spec, double desired,
        double throttle, bool fire, bool preferFlatTurn, bool emergency = false)
    {
        desired = Angles.Wrap0To2Pi(desired);
        double error = Angles.Delta(s.Theta, desired);

        // Already committed to a roll. Let it finish.
        if (s.RollRemaining > 0)
            return new AircraftInput { ThrottleCommand = throttle, HeadingCommand = desired, FireHeld = fire };

        bool isPull = Math.Sign(error) == s.CanopySign;
        bool bigReversal = Math.Abs(error) > 2.2;
        bool roughlyLevel = Math.Abs(Math.Sin(s.Theta)) < 0.55;

        // Never swap ends near the ground. A flat turn hands away control for a
        // whole second, and a second is the entire margin down there.
        if (!emergency && bigReversal && roughlyLevel && preferFlatTurn &&
            s.Airspeed > FlightModel.StallSpeed(spec, Atmosphere.Density(s.Position.Y)) * 1.25)
        {
            return new AircraftInput { ThrottleCommand = throttle, FlatTurnPressed = true };
        }

        // Wrong way up for the turn it wants, or flying inverted on the fuel clock.
        // Rolling IS allowed in an emergency, and is usually the point: inverted and
        // diving, a pull only drives the nose further into the ground.
        double rollThreshold = emergency ? 0.25 : 0.7;
        bool shouldRoll = (!isPull && Math.Abs(error) > rollThreshold)
                          || (s.IsInverted && s.InvertedTime > Skill.InvertedToleranceS);
        if (shouldRoll)
            return new AircraftInput { ThrottleCommand = throttle, HeadingCommand = desired, RollPressed = true, FireHeld = fire };

        return new AircraftInput { ThrottleCommand = throttle, HeadingCommand = desired, FireHeld = fire };
    }

    /// <summary>
    /// Hammer the charging handle when jammed. Pulsed rather than held, because
    /// only rising edges count, and paced by skill so a Rookie is slower at it.
    /// </summary>
    private bool WorkTheJam(AircraftState s)
    {
        if (!s.GunJammed) { _jamPumpTimer = 0; return false; }

        _jamPumpTimer += FlightModel.FixedDt;
        double interval = 0.10 + Skill.ReactionDelayS * 0.35;
        if (_jamPumpTimer < interval) return false;

        _jamPumpTimer = 0;
        return true;
    }

    /// <summary>
    /// <paramref name="opportunistOnly"/> is for a pilot holding a perch. It is not
    /// harmless up there, but it is not hunting either: it takes the shot only if
    /// the target flies right across its nose. Letting supporting pilots fire on
    /// normal terms undoes the whole point of the flight, because three aircraft
    /// then put three streams of fire into the same target anyway.
    /// </summary>
    private bool ShouldFire(Combatant self, Vec2 targetPos, Vec2 targetVel, bool opportunistOnly = false)
    {
        var s = self.State;
        if (!s.GunsCanBear || s.Ammo <= 0) return false;

        double range = (targetPos - s.Position).Length;
        if (range > (opportunistOnly ? 130.0 : Skill.FireRangeM)) return false;

        if (!Guns.Intercept(s.Position, s.Velocity, self.Spec.MuzzleVelocity,
                            targetPos, targetVel, out double aim, out _))
            return false;

        // Only squeeze when the nose is genuinely near the solution. Ammo is finite
        // and heat causes jams, so spraying is self-defeating.
        double cone = opportunistOnly ? Skill.FireConeRad * 0.55 : Skill.FireConeRad;
        return Math.Abs(Angles.Delta(s.Theta, aim + _aimError)) < cone;
    }

    /// <summary>
    /// Fly the perch.
    ///
    /// A supporting fighter is not hiding. It holds height and speed so that the
    /// moment the engaged pilot loses the position it can take the fight over with
    /// an advantage already in hand. And because the perch sits on the far side of
    /// the target, it is standing in the way of the escape, which is what stops a
    /// flight being something you can simply outrun.
    /// </summary>
    private static double HoldStation(AircraftState s, Vec2 station, Vec2 targetPos, out double throttle)
    {
        Vec2 toStation = station - s.Position;

        if (toStation.Length > 140.0)
        {
            throttle = 1.0;
            return toStation.Angle;
        }

        // On station. Hold level, pointed at the target's side of the sky, with
        // enough in hand to dive on it. Full power up here only overshoots.
        throttle = 0.82;
        double level = targetPos.X >= s.Position.X ? 0.0 : Math.PI;
        return Climb(level, toStation.Y > 40.0 ? 0.25 : 0.0);
    }

    /// <summary>
    /// Where to point the aircraft. The aim error is included, so a pilot who
    /// cannot shoot straight also cannot quite line the aircraft up.
    ///
    /// This was tried both ways. Taking the error out of the steering is the
    /// tidier idea, and it measured worse: see the note on AiSkill.FireConeRad.
    /// </summary>
    /// <summary>
    /// How to fly a pursuit, as opposed to where to point the guns.
    ///
    /// Commanding the firing solution directly looks correct and plays badly. The
    /// nose arrives, the heading error goes to zero, and the pilot stops pulling.
    /// The result is a wide, gentle, fast arc, and at the ranges these fights
    /// happen at the aircraft turning hardest wins the position no matter who is
    /// the better shot.
    ///
    /// It punished skill precisely because skill is what makes the command small.
    /// Once the elevator got quick, a Rookie's sloppy aim kept it hauling the
    /// aircraft round while an Ace flew a tidy curve into its guns, and the Ace
    /// lost two rounds in three to a pilot that could not shoot.
    ///
    /// So: haul it round at whatever the airframe will give until the nose is
    /// nearly on, and only then track the solution precisely. That is what a pilot
    /// does, and it puts the advantage back with the one who can hold the tracking
    /// solution once they get there.
    /// </summary>
    private double Pursue(Combatant self, Vec2 targetPos, Vec2 targetVel)
    {
        var s = self.State;

        // Where the guns have to point right now.
        double aim = AimHeading(self, targetPos, targetVel);

        // Close and nearly on: track the firing solution and take the shot.
        double range = (targetPos - s.Position).Length;
        double aimError = Angles.Delta(s.Theta, aim);
        if (range < 140.0 && Math.Abs(aimError) < 0.22) return aim;

        // Otherwise fly LEAD pursuit: point at where the target is going to be,
        // not at where it is. Chasing its current position is pure pursuit, which
        // puts you permanently on the outside of its turn and is how a good shot
        // loses to a bad one.
        //
        // How far ahead to look scales with range: far out there is time to cut a
        // big corner, and up close a big lead just swings the nose off the target.
        double lookAhead = Angles.Clamp(range / 70.0, 0.25, 2.5);
        Vec2 leadPoint = Advance(targetPos, targetVel, _targetTurnRate, lookAhead);

        double lead = (leadPoint - s.Position).Angle;
        double error = Angles.Delta(s.Theta, lead);

        // Outside the tracking window, use everything the airframe will give.
        // Commanding only as far as the solution means easing off the instant the
        // nose arrives, and the aircraft turning hardest owns the fight.
        return Math.Abs(error) <= 0.22 ? lead : s.Theta + Math.Sign(error) * 1.2;
    }

    private double AimHeading(Combatant self, Vec2 targetPos, Vec2 targetVel)
    {
        var s = self.State;
        if (Guns.Intercept(s.Position, s.Velocity, self.Spec.MuzzleVelocity,
                           targetPos, targetVel, out double aim, out _))
            return aim;

        return (targetPos - s.Position).Angle;
    }

    /// <summary>
    /// Throttle management. A disciplined pilot eases off above corner speed, where
    /// extra knots only widen the turn, and firewalls it below.
    ///
    /// This is the other half of flying skill. A Rookie sits at full power all the
    /// time and sails past the corner into a wide, lazy turn.
    /// </summary>
    private double ManageEnergy(AircraftState s, AircraftSpec spec, double requested)
    {
        if (Skill.ThrottleDiscipline <= 0.0) return requested;

        // Only in a turning fight, and never below 0.7.
        //
        // The first version throttled back any time it was above corner speed. That
        // is not discipline, it is throwing away energy: it dropped the pilot's own
        // energy reading far enough to trip its "I am out-energised, run" rule, and
        // a Veteran against a full-throttle Rookie spent the fight fleeing into a
        // wall. Nine of its thirty deaths were leaving the arena.
        if (Current is not (Behavior.TurnFight or Behavior.Scissors)) return requested;

        double corner = spec.CornerSpeedSeaLevel;
        if (s.Airspeed <= corner * 1.2) return requested;

        return Angles.Lerp(requested, 0.7, Skill.ThrottleDiscipline);
    }

    /// <summary>
    /// Tilt a heading upward by a given amount, whichever way the aircraft faces.
    ///
    /// Adding radians straight onto a heading does NOT mean "climb". At heading 0 it
    /// climbs, at heading PI it dives. Every bias in this file has to go through
    /// here or the AI quietly plays a different game depending on which way it
    /// happens to be pointed. Self-play caught exactly that: one side was merging
    /// high and the other merging low, and it cost the low side two rounds in three.
    /// </summary>
    private static double Climb(double heading, double radians)
        => heading + (Math.Cos(heading) >= 0 ? radians : -radians);

    private static double Descend(double heading, double radians) => Climb(heading, -radians);

    /// <summary>
    /// Keep the aircraft inside the box. All three boundaries end the round, and
    /// none of them is a fair trade for a firing position.
    ///
    /// The walls matter more than they look. Without this, "run away from the enemy"
    /// means "run out of the arena", and self-play showed the AI killing itself by
    /// fleeing in about a quarter of all deaths. Disengaging is meant to buy space,
    /// not forfeit.
    /// </summary>
    private static bool AvoidBoundaries(AircraftState s, Arena arena, ref double desired)
    {
        double level = Math.Cos(desired) >= 0 ? 0.0 : Math.PI;

        // Ground first. It is the one that is always fatal and always close.
        //
        // The lookahead scales with speed. A fixed four seconds was written for a
        // slower aircraft, and once the airframe got agile the AI started diving
        // into the deck in pursuit: self-play went from five ground deaths in sixty
        // rounds to twenty-eight, and the worst pilot started winning because the
        // better one chased it into the dirt.
        double descentRate = -s.Velocity.Y;
        double secondsToGround = descentRate > 1 ? s.Position.Y / descentRate : double.MaxValue;
        double floorMargin = 150.0 + s.Airspeed * 1.6;

        if (secondsToGround < 5.0 || s.Position.Y < floorMargin)
        {
            // The steeper the dive, the harder the pull.
            double urgency = Math.Clamp(1.0 - secondsToGround / 5.0, 0.0, 1.0);
            desired = Climb(level, 0.45 + 0.75 * urgency);
            return true;
        }

        if (s.Position.Y > arena.CeilingM - 120)
        {
            desired = Descend(level, 0.35);
            return true;
        }

        // Side walls. Only turn back if actually heading for one. The margin has to
        // scale with how fast the aircraft is closing: a faster airframe eats the
        // distance before a fixed margin gives it room to come round.
        double WallMargin = 260.0 + Math.Abs(s.Velocity.X) * 2.2;
        bool nearRight = s.Position.X > arena.WidthM - WallMargin && s.Velocity.X > 0;
        bool nearLeft = s.Position.X < WallMargin && s.Velocity.X < 0;

        if (nearRight) desired = Math.PI;
        else if (nearLeft) desired = 0.0;

        return false;
    }

    /// <summary>
    /// Nobody left to fight. Fly level, stay inside the box, and get the right way
    /// up.
    ///
    /// Rolling upright is not a detail. Every other path through this class routes
    /// through Steer, which rights the aeroplane when it has been inverted too
    /// long. Cruise does not, so a survivor who happened to be upside down when the
    /// last enemy went down stayed upside down for the rest of the round, on an
    /// engine that starves after two seconds of negative G. Ed watched his wingmen
    /// do exactly that after every win.
    /// </summary>
    private static AircraftInput Cruise(Combatant self, Arena arena)
    {
        var s = self.State;
        double desired = Math.Cos(s.Theta) >= 0 ? 0.0 : Math.PI;
        AvoidBoundaries(s, arena, ref desired);

        return new AircraftInput
        {
            ThrottleCommand = 0.75,
            HeadingCommand = Angles.Wrap0To2Pi(desired),
            RollPressed = s.IsInverted && s.RollRemaining <= 0,
        };
    }

    // --- Delayed picture of the target --------------------------------------

    private void RecordTarget(AircraftState target)
    {
        _targetPos[_historyHead] = target.Position;
        _targetVel[_historyHead] = target.Velocity;
        _historyHead = (_historyHead + 1) % HistoryTicks;
        if (_historyFilled < HistoryTicks) _historyFilled++;
    }

    /// <summary>
    /// What the AI thinks the target is doing.
    ///
    /// It takes a snapshot from one reaction time ago and dead-reckons it forward
    /// to now. That distinction matters. Simply using the stale position modelled a
    /// pilot who cannot see, not one who is slow: a Veteran was aiming a permanent
    /// fifteen meters behind a target flying dead straight, and never hit anything.
    ///
    /// Dead-reckoning it forward means a pilot tracking a steady target aims
    /// correctly whatever their skill, and only gets fooled by what the target has
    /// done since. Which is what reaction time actually is. The better the pilot,
    /// the less of your maneuver they have yet to notice.
    /// </summary>
    private void GetDelayedTarget(out Vec2 position, out Vec2 velocity)
        => GetDelayedTarget(out position, out velocity, out _);

    /// <summary>
    /// Also reports how fast the target is turning, estimated from the same stale
    /// picture. A pilot who has not noticed you are turning yet cannot lead you.
    ///
    /// The forward reckoning follows the CURVE rather than a straight line. That
    /// matters more than it looks. Reckoning straight ahead throws the predicted
    /// position wide of any turning target, and wide is, by accident, roughly where
    /// lead pursuit wants you to point. So the pilot with the worst data was flying
    /// the better geometry, and the one with the best data was flying pure pursuit
    /// straight into the loser's position.
    /// </summary>
    private void GetDelayedTarget(out Vec2 position, out Vec2 velocity, out double turnRate)
    {
        int back = (int)Math.Round(Skill.ReactionDelayS * FlightModel.TickRate);
        back = Math.Min(back, _historyFilled - 1);
        back = Math.Max(back, 0);

        int index = ((_historyHead - 1 - back) % HistoryTicks + HistoryTicks) % HistoryTicks;
        var snapshotPos = _targetPos[index];
        var snapshotVel = _targetVel[index];

        turnRate = EstimateTurnRate(index, snapshotVel, back);

        // Catch the stale snapshot up to now in a STRAIGHT LINE, even though the
        // turn rate is known, and hand the turn rate out separately for the
        // tactical lead.
        //
        // Compounding the turn through the catch-up is more accurate and it wrecks
        // the difficulty ladder, because the error it leaves is not random: a long
        // reaction delay extrapolated around an arc lands a long way ahead of the
        // target, which is roughly where lead pursuit wants to be. Reaction delay
        // was buying position. It must only ever make the picture WRONG, never make
        // it further ahead, or being slow is an advantage.
        double seconds = back / FlightModel.TickRate;
        position = snapshotPos + snapshotVel * seconds;
        velocity = snapshotVel;
    }

    /// <summary>Turn rate from two samples of the stale track, rad/s.</summary>
    private double EstimateTurnRate(int index, Vec2 velocityNow, int back)
    {
        const int Span = 12;   // 0.1 s, long enough not to be reading integration noise
        if (_historyFilled <= back + Span + 1) return 0.0;

        int older = ((index - Span) % HistoryTicks + HistoryTicks) % HistoryTicks;
        var was = _targetVel[older];
        if (was.LengthSquared < 1e-9 || velocityNow.LengthSquared < 1e-9) return 0.0;

        double swept = Angles.Delta(was.Angle, velocityNow.Angle);
        return swept / (Span / FlightModel.TickRate);
    }

    /// <summary>
    /// Move a point along a constant-rate turn. Straight line when it is not
    /// turning, an arc about the turn centre when it is.
    /// </summary>
    private static Vec2 Advance(Vec2 position, Vec2 velocity, double turnRate, double seconds)
    {
        if (seconds <= 0.0) return position;

        double speed = velocity.Length;
        if (speed < 1e-6) return position;
        if (Math.Abs(turnRate) < 1e-3) return position + velocity * seconds;

        // Signed radius, so the centre lands on the correct side of the track.
        double radius = speed / turnRate;
        Vec2 centre = position + velocity.Normalized.PerpCcw * radius;
        return centre + (position - centre).Rotated(turnRate * seconds);
    }

    /// <summary>A slow random walk. Per-tick noise averages out and looks twitchy.</summary>
    private double Wander(double current, double scale)
        => Angles.Clamp(current + _rng.NextSigned() * scale * 0.6, -scale, scale);
}
