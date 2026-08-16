using Aerodrome.Core;
using Xunit;

namespace Aerodrome.Core.Tests;

public class CombatTests
{
    private static Combatant Blue(Vec2 at, double heading, double speed = 60)
        => new()
        {
            Spec = AircraftSpec.CamelArcade,
            Team = 0,
            Callsign = "blue",
            State = AircraftState.Spawn(AircraftSpec.CamelArcade, at, heading, speed),
        };

    private static Combatant Red(Vec2 at, double heading, double speed = 60)
        => new()
        {
            Spec = AircraftSpec.CamelArcade,
            Team = 1,
            Callsign = "red",
            State = AircraftState.Spawn(AircraftSpec.CamelArcade, at, heading, speed),
        };

    // --- Ballistics ---------------------------------------------------------

    [Fact]
    public void A_round_fired_at_a_target_dead_ahead_connects()
    {
        var arena = SelfPlay.DefaultArena;
        var shooter = Blue(new Vec2(1000, 400), 0.0);
        var target = Red(new Vec2(1150, 400), 0.0);
        var combatants = new[] { shooter, target };

        var bullets = new BulletField();
        var rng = new Rng(5);

        shooter.Input = new AircraftInput { ThrottleCommand = 1.0, FireHeld = true };
        target.Input = AircraftInput.Coast(1.0);

        bool hit = false;
        for (int i = 0; i < 60 && !hit; i++)
        {
            Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);
            bullets.Step(combatants, arena, FlightModel.FixedDt, ref rng);
            if (bullets.Hits.Count > 0) hit = true;
        }

        Assert.True(hit, "a round fired straight at a target 150 m ahead must connect");
        Assert.True(target.State.HitsTaken > 0);
        Assert.True(shooter.HitsScored > 0);
    }

    [Fact]
    public void Fast_rounds_do_not_tunnel_through_targets()
    {
        // A round covers 6.2 m per tick and the hit capsule is about 3 m across.
        // Without swept collision it would pass clean through on most frames. Fire
        // across a spread of ranges and check none of them slip past.
        var arena = SelfPlay.DefaultArena;
        var rng = new Rng(9);
        var missedAt = new System.Collections.Generic.List<int>();

        // Inside the range band where drop is smaller than the hit radius. Longer
        // shots missing is ballistics doing its job, not tunnelling, and that is
        // covered by its own test below.
        for (int range = 60; range <= 260; range += 20)
        {
            var shooter = Blue(new Vec2(600, 400), 0.0, speed: 0.0);
            var target = Red(new Vec2(600 + range, 400), 0.0, speed: 0.0);
            var combatants = new[] { shooter, target };
            var bullets = new BulletField();

            shooter.State.Velocity = Vec2.Zero;
            target.State.Velocity = Vec2.Zero;

            // One round, then let it fly the whole distance.
            shooter.Input = new AircraftInput { FireHeld = true };
            Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);

            bool hit = false;
            for (int i = 0; i < 200 && !hit; i++)
            {
                // Freeze the aircraft: this test is about the bullet, not the flying.
                target.State.Position = new Vec2(600 + range, 400);
                bullets.Step(combatants, arena, FlightModel.FixedDt, ref rng);
                if (bullets.Hits.Count > 0) hit = true;
            }

            if (!hit) missedAt.Add(range);
        }

        Assert.True(missedAt.Count == 0,
            $"rounds passed clean through at these ranges: {string.Join(", ", missedAt)} m");
    }

    [Fact]
    public void Rounds_drop_and_slow_over_distance()
    {
        // Aimed dead level, a round should still be climbing away from the line of
        // sight nowhere and falling below it by long range. This is why the AI
        // holds off for the drop, and why a far shot is a real shot.
        var arena = new Arena { Name = "Long", WidthM = 40000, CeilingM = 4000 };
        var rng = new Rng(4);
        var shooter = Blue(new Vec2(1000, 2000), 0.0, speed: 0.0);
        shooter.State.Velocity = Vec2.Zero;

        var bullets = new BulletField();
        shooter.Input = new AircraftInput { FireHeld = true };
        Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);

        double launchSpeed = 0, dropAt500 = 0, speedAt500 = 0;
        foreach (var b in bullets.Bullets) if (b.Active) launchSpeed = b.Velocity.Length;

        for (int i = 0; i < 400; i++)
        {
            bullets.Step(new[] { shooter }, arena, FlightModel.FixedDt, ref rng);
            foreach (var b in bullets.Bullets)
            {
                if (!b.Active) continue;
                if (b.Position.X - 1000 >= 500 && dropAt500 == 0)
                {
                    dropAt500 = 2000 - b.Position.Y;
                    speedAt500 = b.Velocity.Length;
                }
            }
        }

        Assert.True(dropAt500 > 1.0, $"a round should have dropped by 500 m, got {dropAt500:F2} m");
        Assert.True(speedAt500 < launchSpeed * 0.95,
            $"a round should have slowed by 500 m: {launchSpeed:F0} -> {speedAt500:F0} m/s");
    }

    [Fact]
    public void Rounds_carry_the_aircraft_velocity()
    {
        var arena = SelfPlay.DefaultArena;
        var rng = new Rng(3);
        var fast = Blue(new Vec2(600, 400), 0.0, speed: 70);
        var bullets = new BulletField();

        fast.Input = new AircraftInput { FireHeld = true };
        Guns.Step(fast, arena, bullets, 0, FlightModel.FixedDt, ref rng);

        Bullet round = default;
        foreach (var b in bullets.Bullets) if (b.Active) { round = b; break; }

        Assert.True(round.Active);
        Assert.True(round.Velocity.Length > fast.Spec.MuzzleVelocity + 60,
            "a round from a moving aircraft leaves faster than the muzzle rating");
    }

    // --- Ammunition, heat, jams ---------------------------------------------

    [Fact]
    public void Ammunition_runs_out_and_there_is_no_reload()
    {
        var arena = SelfPlay.DefaultArena;
        var rng = new Rng(11);
        var shooter = Blue(new Vec2(600, 400), 0.0);
        var bullets = new BulletField(4096);

        shooter.Input = new AircraftInput { ThrottleCommand = 1.0, FireHeld = true };
        shooter.State.GunHeat = 0;

        for (int i = 0; i < (int)(200 * FlightModel.TickRate); i++)
        {
            shooter.State.GunJammed = false;   // isolate ammo from jamming
            Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);
            if (shooter.State.Ammo == 0) break;
        }

        Assert.Equal(0, shooter.State.Ammo);

        int before = bullets.ActiveCount;
        for (int i = 0; i < 120; i++)
            Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);

        Assert.True(bullets.ActiveCount <= before, "an empty aircraft must not keep firing");
    }

    [Fact]
    public void Sustained_fire_heats_the_guns_and_eventually_jams_them()
    {
        var arena = SelfPlay.DefaultArena;
        var rng = new Rng(17);
        var shooter = Blue(new Vec2(600, 400), 0.0);
        var bullets = new BulletField(4096);

        shooter.Input = new AircraftInput { ThrottleCommand = 1.0, FireHeld = true };

        double heatPeak = 0;
        double firedFor = 0;
        for (int i = 0; i < (int)(25 * FlightModel.TickRate) && !shooter.State.GunJammed; i++)
        {
            Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);
            heatPeak = System.Math.Max(heatPeak, shooter.State.GunHeat);
            firedFor += FlightModel.FixedDt;
        }

        Assert.True(shooter.State.GunJammed, "leaning on the trigger has to have a cost");
        Assert.True(heatPeak > 0.35, $"the guns should have got hot first, peaked at {heatPeak:F2}");

        // Cold guns must be reliable. A two-round snap shot losing the fight to a
        // dice roll teaches the player nothing.
        Assert.Equal(0.0, Guns.JamProbability(shooter.Spec, 0.0), 9);
        Assert.Equal(0.0, Guns.JamProbability(shooter.Spec, 0.20), 9);
        Assert.True(Guns.JamProbability(shooter.Spec, 1.0) >
                    Guns.JamProbability(shooter.Spec, 0.6) * 3.0,
            "jam risk has to climb steeply, so the danger is holding the trigger down");

        // Short controlled bursts should be far safer than holding it down.
        var disciplined = Blue(new Vec2(600, 400), 0.0);
        var rng2 = new Rng(17);
        for (int burst = 0; burst < 12; burst++)
        {
            disciplined.Input = new AircraftInput { ThrottleCommand = 1.0, FireHeld = true };
            for (int i = 0; i < 36; i++) Guns.Step(disciplined, arena, bullets, 0, FlightModel.FixedDt, ref rng2);

            disciplined.Input = AircraftInput.Coast(1.0);
            for (int i = 0; i < 90; i++) Guns.Step(disciplined, arena, bullets, 0, FlightModel.FixedDt, ref rng2);
        }

        Assert.False(disciplined.State.GunJammed, "short bursts with time to cool should stay clear");
    }

    [Fact]
    public void A_jam_clears_only_if_you_hold_the_action_long_enough()
    {
        var spec = AircraftSpec.CamelArcade;
        var arena = SelfPlay.DefaultArena;
        var rng = new Rng(19);
        var shooter = Blue(new Vec2(600, 400), 0.0);
        var bullets = new BulletField();

        shooter.State.GunJammed = true;

        // A brief tap does nothing.
        shooter.Input = new AircraftInput { ThrottleCommand = 1.0, ClearJamPressed = true };
        for (int i = 0; i < 20; i++) Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);
        Assert.True(shooter.State.GunJammed);

        // Letting go resets the progress. No free partial credit.
        shooter.Input = AircraftInput.Coast(1.0);
        Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);
        Assert.Equal(0.0, shooter.State.JamClearProgress, 6);

        shooter.Input = new AircraftInput { ThrottleCommand = 1.0, ClearJamPressed = true };
        for (int i = 0; i < (int)(spec.JamClearSeconds * FlightModel.TickRate) + 4; i++)
            Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);

        Assert.False(shooter.State.GunJammed);
    }

    [Fact]
    public void The_guns_stay_silent_through_a_flat_turn()
    {
        var arena = SelfPlay.DefaultArena;
        var rng = new Rng(23);
        var shooter = Blue(new Vec2(600, 400), 0.0, speed: 62);
        var bullets = new BulletField();

        shooter.Input = new AircraftInput { ThrottleCommand = 1.0, FlatTurnPressed = true, FireHeld = true };
        FlightModel.Step(shooter.State, shooter.Spec, shooter.Input, arena);
        Assert.True(shooter.State.IsFlatTurning);

        shooter.Input = new AircraftInput { ThrottleCommand = 1.0, FireHeld = true };
        while (shooter.State.IsFlatTurning)
        {
            Guns.Step(shooter, arena, bullets, 0, FlightModel.FixedDt, ref rng);
            FlightModel.Step(shooter.State, shooter.Spec, shooter.Input, arena);
        }

        Assert.Equal(0, bullets.ActiveCount);
        Assert.Equal(shooter.Spec.AmmoRounds, shooter.State.Ammo);
    }

    // --- Damage -------------------------------------------------------------

    [Fact]
    public void One_lucky_round_never_ends_a_round()
    {
        // A single bullet deciding the fight is arbitrary and teaches nobody
        // anything. Whatever it hits, one round must never be fatal.
        var spec = AircraftSpec.CamelArcade;
        var rng = new Rng(1);

        for (int i = 0; i < 600; i++)
        {
            var s = AircraftState.Spawn(spec, new Vec2(600, 400), 0.0, 60);
            double along = i / 600.0;   // sweep the whole airframe, nose to tail
            Damage.ApplyHit(s, spec, along, ref rng);
            Damage.Step(s, spec, FlightModel.FixedDt);

            Assert.True(s.IsAlive, $"a single round at {along:F2} along the airframe was fatal");
        }
    }

    [Fact]
    public void A_pilot_has_to_be_hit_several_times()
    {
        var spec = AircraftSpec.CamelArcade;
        var s = AircraftState.Spawn(spec, new Vec2(600, 400), 0.0, 60);
        var rng = new Rng(1);

        int pilotHits = 0;
        for (int i = 0; i < 4000 && s.IsAlive; i++)
        {
            // Keep the rest of the airframe fresh so only the pilot can end this.
            s.EngineHealth = s.WingHealth = s.TailHealth = s.ControlHealth = 1.0;
            s.FuelSystemHealth = 1.0;
            s.AirframeIntegrity = 1.0;
            s.OnFire = false;

            if (Damage.ApplyHit(s, spec, 0.55, ref rng) == Component.Pilot) pilotHits++;
        }

        Assert.False(s.IsAlive);
        Assert.Equal(DeathCause.Gunfire, s.Death);
        Assert.True(pilotHits >= 3, $"took only {pilotHits} cockpit hits to kill the pilot");

        // And the pilot flies worse long before they stop flying.
        var wounded = AircraftState.Spawn(spec, new Vec2(600, 400), 0.0, 60);
        wounded.PilotHealth = 0.5;
        Assert.True(wounded.IsWounded);
        Assert.True(wounded.EffectiveControl < 1.0);
    }

    [Fact]
    public void Sustained_hits_reliably_bring_an_aircraft_down()
    {
        // Component damage on its own made kills a lottery. Twenty rounds could go
        // in without anything decisive happening. Keep hitting and it must come apart.
        var spec = AircraftSpec.CamelArcade;
        var s = AircraftState.Spawn(spec, new Vec2(600, 400), 0.0, 60);
        var rng = new Rng(42);

        int hits = 0;
        while (s.IsAlive && hits < 200)
        {
            Damage.ApplyHit(s, spec, 0.3 + (hits % 5) * 0.12, ref rng);
            Damage.Step(s, spec, FlightModel.FixedDt);
            hits++;
        }

        Assert.False(s.IsAlive);
        Assert.InRange(hits, 6, 30);
    }

    [Fact]
    public void Where_a_round_lands_decides_what_it_breaks()
    {
        var spec = AircraftSpec.CamelArcade;
        var rng = new Rng(7);

        int noseEngine = 0, tailTail = 0;
        for (int i = 0; i < 400; i++)
        {
            var nose = AircraftState.Spawn(spec, Vec2.Zero, 0, 60);
            if (Damage.ApplyHit(nose, spec, 0.92, ref rng) == Component.Engine) noseEngine++;

            var tail = AircraftState.Spawn(spec, Vec2.Zero, 0, 60);
            if (Damage.ApplyHit(tail, spec, 0.08, ref rng) == Component.Tail) tailTail++;
        }

        Assert.True(noseEngine > 180, $"nose hits should mostly find the engine, got {noseEngine}/400");
        Assert.True(tailTail > 150, $"tail hits should mostly find the tail, got {tailTail}/400");
    }

    [Fact]
    public void A_fuel_fire_is_a_countdown_with_no_way_out()
    {
        var spec = AircraftSpec.CamelArcade;
        var s = AircraftState.Spawn(spec, new Vec2(600, 500), 0.0, 60);
        s.OnFire = true;

        double burned = 0;
        while (s.IsAlive && burned < 30)
        {
            Damage.Step(s, spec, FlightModel.FixedDt);
            burned += FlightModel.FixedDt;
        }

        Assert.False(s.IsAlive);
        Assert.Equal(DeathCause.Fire, s.Death);
        Assert.InRange(burned, Damage.BurnThroughSeconds - 0.1, Damage.BurnThroughSeconds + 0.1);
    }

    [Fact]
    public void A_shot_up_wing_fails_under_G_a_healthy_one_would_take()
    {
        var spec = AircraftSpec.CamelArcade;

        var healthy = AircraftState.Spawn(spec, new Vec2(600, 500), 0.0, 60);
        healthy.LoadFactor = spec.GLimit * 0.8;
        Damage.Step(healthy, spec, FlightModel.FixedDt);
        Assert.True(healthy.IsAlive, "an undamaged airframe takes its rated G");

        var shotUp = AircraftState.Spawn(spec, new Vec2(600, 500), 0.0, 60);
        shotUp.WingHealth = 0.35;
        shotUp.LoadFactor = spec.GLimit * 0.8;
        Damage.Step(shotUp, spec, FlightModel.FixedDt);

        Assert.False(shotUp.IsAlive);
        Assert.Equal(DeathCause.StructuralFailure, shotUp.Death);
    }

    // --- Aiming -------------------------------------------------------------

    [Fact]
    public void The_intercept_solution_leads_a_crossing_target()
    {
        // Target crossing left to right 200 m ahead. The solution must aim ahead of
        // it, not at it.
        var shooter = new Vec2(0, 500);
        var target = new Vec2(0, 700);
        var targetVel = new Vec2(60, 0);

        Assert.True(Guns.Intercept(shooter, Vec2.Zero, 745, target, targetVel,
                                   out double aim, out double flight));

        Assert.True(flight > 0.2 && flight < 0.45, $"flight time {flight:F2}s looks wrong for 200 m");

        // The target crosses toward +X, so the solution must sit on the +X side of
        // a shot straight at it. That is a smaller angle here, not a larger one.
        double straightAt = (target - shooter).Angle;
        double lead = Angles.Delta(straightAt, aim);

        Assert.True(lead < -0.04,
            $"the aim point must lead the target's motion, offset was {lead:F3} rad");

        // And the lead has to be roughly the right size: 60 m/s for the flight time.
        double expected = System.Math.Atan2(60 * flight, 200);
        Assert.InRange(System.Math.Abs(lead), expected * 0.6, expected * 1.4);
    }

    // --- Round loop ---------------------------------------------------------

    [Fact]
    public void A_round_ends_when_one_side_is_down()
    {
        var match = Match.Duel(SelfPlay.DefaultArena, AircraftSpec.CamelArcade, seed: 2);
        Assert.Equal(RoundOutcome.InProgress, match.Outcome);

        match.Combatants[1].State.IsAlive = false;
        match.Combatants[1].State.Death = DeathCause.Gunfire;
        match.Step();

        Assert.Equal(RoundOutcome.TeamZeroWins, match.Outcome);
    }

    [Fact]
    public void A_round_that_settles_nothing_is_a_draw()
    {
        var match = new Match(SelfPlay.DefaultArena, seed: 3) { TimeLimitSeconds = 1.0 };
        Match.Duel(SelfPlay.DefaultArena, AircraftSpec.CamelArcade, 3).Combatants
             .ForEach(c => match.Add(c));

        for (int i = 0; i < (int)(1.2 * FlightModel.TickRate); i++)
        {
            foreach (var c in match.Combatants) c.Input = AircraftInput.Coast(0.8);
            match.Step();
        }

        Assert.Equal(RoundOutcome.Draw, match.Outcome);
    }
}
