using System;

namespace Aerodrome.Core;

public enum Behavior { Merge, TurnFight, BoomAndZoom, Extend, Scissors, Disengage }

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
    private double _scissorsTimer;
    private int _scissorsSide = 1;

    public PilotAi(AiSkill skill, uint seed)
    {
        Skill = skill;
        _rng = new Rng(seed);
        _sinceDecision = _rng.NextDouble() * skill.DecisionPeriodS;   // desynchronise pilots
    }

    public AircraftInput Fly(Combatant self, Combatant? enemy, Arena arena, double dt)
    {
        var s = self.State;
        if (!s.IsAlive) return AircraftInput.Neutral;

        // Committed maneuvers finish. The AI has to live with them like anyone else.
        if (s.IsFlatTurning) return new AircraftInput { ThrottleCommand = 1.0 };

        if (enemy is null || !enemy.IsAlive)
            return Cruise(self, arena);

        RecordTarget(enemy.State);
        GetDelayedTarget(out Vec2 targetPos, out Vec2 targetVel);

        _sinceDecision += dt;
        _behaviourHeld += dt;

        if (_sinceDecision >= Skill.DecisionPeriodS)
        {
            _sinceDecision = 0;
            _aimError = Wander(_aimError, Skill.AimErrorRad);

            Behavior wanted = Decide(self, enemy, targetPos, arena);
            if (wanted != Current && (_behaviourHeld >= MinimumCommitmentS || wanted == Behavior.Disengage))
            {
                Current = wanted;
                _behaviourHeld = 0;
            }
        }

        return Execute(self, enemy, targetPos, targetVel, arena);
    }

    // --- Deciding what to do ------------------------------------------------

    private Behavior Decide(Combatant self, Combatant enemy, Vec2 targetPos, Arena arena)
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
        Combatant self, Combatant enemy, Vec2 targetPos, Vec2 targetVel, Arena arena)
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
                desired = AimHeading(self, targetPos, targetVel);
                break;

            case Behavior.Merge:
                desired = (targetPos - s.Position).Angle;
                // Come in a little high. Height is the currency.
                if (s.EnergyHeightM < enemy.State.EnergyHeightM + 60) desired = Climb(desired, 0.22);
                break;

            default:
                desired = AimHeading(self, targetPos, targetVel);
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

        bool fire = ShouldFire(self, targetPos, targetVel);
        return Steer(s, spec, desired, throttle, fire, preferFlatTurn, emergency);
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

    private bool ShouldFire(Combatant self, Vec2 targetPos, Vec2 targetVel)
    {
        var s = self.State;
        if (!s.GunsCanBear || s.Ammo <= 0) return false;

        double range = (targetPos - s.Position).Length;
        if (range > Skill.FireRangeM) return false;

        if (!Guns.Intercept(s.Position, s.Velocity, self.Spec.MuzzleVelocity,
                            targetPos, targetVel, out double aim, out _))
            return false;

        // Only squeeze when the nose is genuinely near the solution. Ammo is finite
        // and heat causes jams, so spraying is self-defeating.
        return Math.Abs(Angles.Delta(s.Theta, aim + _aimError)) < Skill.FireConeRad;
    }

    /// <summary>
    /// Where to point the aircraft. The aim error is included, so a pilot who
    /// cannot shoot straight also cannot quite line the aircraft up.
    ///
    /// This was tried both ways. Taking the error out of the steering is the
    /// tidier idea, and it measured worse: see the note on AiSkill.FireConeRad.
    /// </summary>
    private double AimHeading(Combatant self, Vec2 targetPos, Vec2 targetVel)
    {
        var s = self.State;
        if (Guns.Intercept(s.Position, s.Velocity, self.Spec.MuzzleVelocity,
                           targetPos, targetVel, out double aim, out _))
            return aim + _aimError;

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

    private static AircraftInput Cruise(Combatant self, Arena arena)
    {
        var s = self.State;
        double desired = Math.Cos(s.Theta) >= 0 ? 0.0 : Math.PI;
        AvoidBoundaries(s, arena, ref desired);
        return new AircraftInput { ThrottleCommand = 0.75, HeadingCommand = Angles.Wrap0To2Pi(desired) };
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
    {
        int back = (int)Math.Round(Skill.ReactionDelayS * FlightModel.TickRate);
        back = Math.Min(back, _historyFilled - 1);
        back = Math.Max(back, 0);

        int index = ((_historyHead - 1 - back) % HistoryTicks + HistoryTicks) % HistoryTicks;
        velocity = _targetVel[index];
        position = _targetPos[index] + velocity * (back / FlightModel.TickRate);
    }

    /// <summary>A slow random walk. Per-tick noise averages out and looks twitchy.</summary>
    private double Wander(double current, double scale)
        => Angles.Clamp(current + _rng.NextSigned() * scale * 0.6, -scale, scale);
}
