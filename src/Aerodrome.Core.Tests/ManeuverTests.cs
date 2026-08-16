using Aerodrome.Core;
using Xunit;

namespace Aerodrome.Core.Tests;

/// <summary>
/// The maneuver set is the game. If these break, the game is broken, whatever else
/// still passes. Every one of them drives the aircraft through the pilot input
/// surface only.
/// </summary>
public class ManeuverTests
{
    [Fact]
    public void Immelmann_reverses_direction_gains_altitude_loses_speed_and_ends_upright()
    {
        var rig = new Rig().Spawn(3000, 1500, heading: 0.0, speed: 70.0);
        double startAlt = rig.Altitude;
        double startSpeed = rig.Speed;

        Assert.True(rig.FacingRight);
        Assert.False(rig.State.IsInverted);

        // Half loop.
        Assert.True(rig.Pull(Math.PI), "the half loop did not close");
        Assert.True(rig.State.IsInverted, "a half loop must leave the aircraft inverted");

        // The second half of the maneuver: the pilot rights the aircraft by hand.
        rig.HalfRoll();

        Assert.False(rig.FacingRight);                       // reversed
        Assert.False(rig.State.IsInverted);                  // upright again
        Assert.True(rig.Altitude > startAlt + 50,            // traded speed for height
            $"expected a real climb, got {rig.Altitude - startAlt:F0} m");
        Assert.True(rig.Speed < startSpeed - 10,
            $"expected a real speed loss, got {startSpeed - rig.Speed:F1} m/s");
        Assert.True(rig.State.IsAlive);
    }

    [Fact]
    public void SplitS_reverses_direction_loses_altitude_gains_speed_and_ends_upright()
    {
        var rig = new Rig().Spawn(3000, 1500, heading: 0.0, speed: 45.0);
        double startAlt = rig.Altitude;
        double startSpeed = rig.Speed;

        // Roll inverted first. That is the whole difference from the Immelmann.
        rig.HalfRoll();
        Assert.True(rig.State.IsInverted);

        Assert.True(rig.Pull(Math.PI), "the half loop did not close");

        Assert.False(rig.FacingRight);                       // reversed
        Assert.False(rig.State.IsInverted);                  // upright, with no second roll
        Assert.True(rig.Altitude < startAlt - 50,
            $"expected a real descent, got {startAlt - rig.Altitude:F0} m");
        Assert.True(rig.Speed > startSpeed + 10,
            $"expected a real speed gain, got {rig.Speed - startSpeed:F1} m/s");
        Assert.True(rig.State.IsAlive);
    }

    [Fact]
    public void Immelmann_and_SplitS_are_mirror_images()
    {
        var up = new Rig().Spawn(3000, 1500, 0.0, 60.0);
        up.Pull(Math.PI);
        up.HalfRoll();

        var down = new Rig().Spawn(3000, 1500, 0.0, 60.0);
        down.HalfRoll();
        down.Pull(Math.PI);

        // Both reverse and both end upright. They differ only in the energy trade.
        Assert.False(up.FacingRight);
        Assert.False(down.FacingRight);
        Assert.False(up.State.IsInverted);
        Assert.False(down.State.IsInverted);

        Assert.True(up.Altitude > 1500 && down.Altitude < 1500);
        Assert.True(up.Speed < 60 && down.Speed > 60);
    }

    [Fact]
    public void Bare_half_loop_reverses_but_leaves_the_aircraft_inverted()
    {
        var rig = new Rig().Spawn(3000, 1800, heading: 0.0, speed: 70.0);

        Assert.True(rig.Pull(Math.PI));

        Assert.False(rig.FacingRight);
        Assert.True(rig.State.IsInverted,
            "skipping the roll must leave the pilot inverted - that is the cost of the shortcut");
    }

    [Fact]
    public void Sustained_inverted_flight_starves_a_gravity_fed_engine_on_schedule()
    {
        var spec = AircraftSpec.CamelArcade;
        var rig = new Rig(spec).Spawn(3000, 2200, heading: 0.0, speed: 55.0);

        rig.HalfRoll();
        Assert.True(rig.State.IsInverted);

        double starveStartedAt = -1;
        double fullyStarvedAt = -1;

        for (int i = 0; i < (int)(8 * FlightModel.TickRate) && rig.State.IsAlive; i++)
        {
            rig.Tick(new AircraftInput { ThrottleCommand = 1.0, HeadingCommand = rig.State.Theta });

            // Measure against the model's own inverted clock. The aircraft counts as
            // inverted from the MIDPOINT of the roll, not from the end of it, so a
            // wall-clock started after the roll would read about 0.18 s short.
            if (starveStartedAt < 0 && rig.State.FuelStarvation > 0)
                starveStartedAt = rig.State.InvertedTime;
            if (fullyStarvedAt < 0 && rig.State.FuelStarvation >= 1.0 - spec.StarvedPowerFloor - 1e-6)
                fullyStarvedAt = rig.State.InvertedTime;
        }

        Assert.True(starveStartedAt > 0, "the engine never coughed");
        Assert.InRange(starveStartedAt, spec.InvertedStarveDelayS - 0.05, spec.InvertedStarveDelayS + 0.05);
        Assert.InRange(fullyStarvedAt,
            spec.InvertedStarveDelayS + spec.InvertedStarveRampS - 0.05,
            spec.InvertedStarveDelayS + spec.InvertedStarveRampS + 0.05);
    }

    [Fact]
    public void Inversion_starts_at_the_midpoint_of_the_roll_not_at_the_end()
    {
        // Halfway through a roll the wings are vertical and the canopy crosses the
        // horizon. Committing to the roll is what starts the clock, so a pilot who
        // rolls and changes their mind has already paid part of the price.
        var spec = AircraftSpec.CamelArcade;
        var rig = new Rig(spec).Spawn(3000, 2000, 0.0, 60.0);

        double flippedAt = -1;
        rig.Tick(new AircraftInput { ThrottleCommand = 1.0, RollPressed = true, HeadingCommand = rig.State.Theta });
        double elapsed = FlightModel.FixedDt;

        while (rig.State.RollRemaining > 0)
        {
            rig.Tick(new AircraftInput { ThrottleCommand = 1.0, HeadingCommand = rig.State.Theta });
            elapsed += FlightModel.FixedDt;
            if (flippedAt < 0 && rig.State.IsInverted) flippedAt = elapsed;
        }

        Assert.True(flippedAt > 0, "never went inverted during the roll");
        Assert.InRange(flippedAt, spec.HalfRollSeconds * 0.5 - 0.02, spec.HalfRollSeconds * 0.5 + 0.02);
        Assert.Equal(spec.HalfRollSeconds, elapsed, 1);
    }

    [Fact]
    public void Rolling_upright_restores_engine_power()
    {
        var rig = new Rig().Spawn(3000, 2200, 0.0, 55.0);
        rig.HalfRoll();
        rig.Coast(4.0);

        Assert.True(rig.State.FuelStarvation > 0.5, "the engine should be starved by now");

        rig.HalfRoll();               // back upright
        Assert.False(rig.State.IsInverted);
        rig.Coast(1.5);

        Assert.Equal(0.0, rig.State.FuelStarvation, 6);
    }

    [Fact]
    public void A_loop_at_part_throttle_returns_to_the_start_heading_with_less_energy()
    {
        var rig = new Rig().Spawn(3000, 2000, heading: 0.0, speed: 70.0);
        double startHeading = rig.State.Theta;
        double startEnergy = rig.EnergyHeight;

        Assert.True(rig.Pull(Angles.TwoPi, commandRateRad: 0.9, throttle: 0.5),
            "the loop did not close");

        Assert.Equal(0.0, Angles.Delta(startHeading, rig.State.Theta), 1);
        Assert.False(rig.State.IsInverted, "a full loop returns you upright");
        Assert.True(rig.EnergyHeight < startEnergy,
            $"a loop must cost energy: {startEnergy:F0} -> {rig.EnergyHeight:F0} m");
    }

    [Fact]
    public void Hard_turning_bleeds_more_energy_than_flying_straight()
    {
        const double seconds = 4.0;

        var straight = new Rig().Spawn(3000, 2000, 0.0, 70.0);
        double straightStart = straight.EnergyHeight;
        straight.Coast(seconds, throttle: 0.6);
        double straightLoss = straightStart - straight.EnergyHeight;

        var turning = new Rig().Spawn(3000, 2000, 0.0, 70.0);
        double turningStart = turning.EnergyHeight;
        turning.Pull(Angles.TwoPi, commandRateRad: 0.9, timeoutS: seconds, throttle: 0.6);
        double turningLoss = turningStart - turning.EnergyHeight;

        Assert.True(turningLoss > straightLoss,
            $"induced drag must bite: turning lost {turningLoss:F1} m, straight lost {straightLoss:F1} m");
    }

    // --- The flat turn: the third way to reverse -----------------------------

    [Fact]
    public void A_flat_turn_reverses_facing_while_keeping_altitude()
    {
        var rig = new Rig().Spawn(1200, 800, heading: 0.0, speed: 62.0);
        double startAlt = rig.Altitude;
        double startSpeed = rig.Speed;

        Assert.True(rig.FlatTurn(), "the flat turn was refused");

        Assert.False(rig.FacingRight);
        Assert.True(Math.Abs(rig.Altitude - startAlt) < 15,
            $"a flat turn trades speed, not height: moved {rig.Altitude - startAlt:F1} m");
        Assert.True(rig.Speed < startSpeed * 0.9,
            $"a flat turn must cost real speed: {startSpeed:F1} -> {rig.Speed:F1} m/s");
    }

    [Fact]
    public void A_flat_turn_mirrors_the_heading_about_the_vertical()
    {
        // Climbing to the right at 20 degrees comes out climbing to the left at 20.
        double entry = Angles.ToRadians(20);
        var rig = new Rig().Spawn(1200, 700, entry, 65.0);

        Assert.True(rig.FlatTurn());

        Assert.Equal(Angles.ToRadians(160), rig.State.Theta, 3);
    }

    [Fact]
    public void A_flat_turn_leaves_you_the_same_way_up_you_went_in()
    {
        var upright = new Rig().Spawn(1200, 800, 0.0, 62.0);
        Assert.False(upright.State.IsInverted);
        Assert.True(upright.FlatTurn());
        Assert.False(upright.State.IsInverted, "upright in, upright out");

        var inverted = new Rig().Spawn(1200, 800, 0.0, 62.0, inverted: true);
        Assert.True(inverted.State.IsInverted);
        Assert.True(inverted.FlatTurn());
        Assert.True(inverted.State.IsInverted, "inverted in, inverted out");
    }

    [Fact]
    public void The_guns_cannot_bear_during_a_flat_turn()
    {
        var rig = new Rig().Spawn(1200, 800, 0.0, 62.0);
        Assert.True(rig.State.GunsCanBear);

        rig.Tick(new AircraftInput { ThrottleCommand = 1.0, FlatTurnPressed = true });
        Assert.True(rig.State.IsFlatTurning);

        int masked = 0, total = 0;
        while (rig.State.IsFlatTurning && rig.State.IsAlive)
        {
            total++;
            if (!rig.State.GunsCanBear) masked++;
            rig.Tick(new AircraftInput { ThrottleCommand = 1.0, FireHeld = true });
        }

        Assert.Equal(total, masked);
        Assert.True(rig.State.GunsCanBear, "the guns come back once you are round");
    }

    [Fact]
    public void Airspeed_stays_honest_through_a_flat_turn()
    {
        // The in-plane velocity passes through zero halfway round, because most of
        // the motion is pointed into the screen. The aircraft is not stalling, and
        // the reported airspeed must not pretend it is.
        var spec = AircraftSpec.CamelArcade;
        var rig = new Rig(spec).Spawn(1200, 800, 0.0, 62.0);

        rig.Tick(new AircraftInput { ThrottleCommand = 1.0, FlatTurnPressed = true });

        double slowest = double.MaxValue;
        while (rig.State.IsFlatTurning && rig.State.IsAlive)
        {
            slowest = Math.Min(slowest, rig.State.Airspeed);
            Assert.False(rig.State.IsStalled, "a flat turn is not a stall");
            rig.Tick(new AircraftInput { ThrottleCommand = 1.0 });
        }

        Assert.True(slowest > spec.StallSpeedSeaLevel,
            $"reported airspeed dipped to {slowest:F1} m/s, below the {spec.StallSpeedSeaLevel:F1} m/s stall");
    }

    [Fact]
    public void The_flat_turn_takes_about_as_long_as_the_spec_says()
    {
        var spec = AircraftSpec.CamelArcade;
        var rig = new Rig(spec).Spawn(1200, 800, 0.0, 62.0);

        double start = rig.Time;
        Assert.True(rig.FlatTurn());
        double elapsed = rig.Time - start;

        Assert.InRange(elapsed, spec.FlatTurnSeconds - 0.05, spec.FlatTurnSeconds + 0.05);
    }

    [Fact]
    public void You_cannot_flat_turn_below_stall_speed()
    {
        var spec = AircraftSpec.CamelArcade;
        var rig = new Rig(spec).Spawn(1200, 900, 0.0, spec.StallSpeedSeaLevel * 0.85);

        Assert.False(rig.FlatTurn(),
            "too slow to swap ends - you have to dive for speed first, and that costs the altitude");
    }

    [Fact]
    public void A_flat_turn_cannot_be_interrupted_or_stacked()
    {
        var rig = new Rig().Spawn(1200, 800, 0.0, 62.0);
        rig.Tick(new AircraftInput { ThrottleCommand = 1.0, FlatTurnPressed = true });

        double entryTheta = rig.State.Theta;

        // Spam every other control mid-turn. None of it may take effect.
        for (int i = 0; i < 20; i++)
            rig.Tick(new AircraftInput
            {
                ThrottleCommand = 1.0,
                HeadingCommand = Angles.ToRadians(90),
                RollPressed = true,
                FlatTurnPressed = true,
            });

        Assert.True(rig.State.IsFlatTurning);
        Assert.Equal(entryTheta, rig.State.Theta, 9);
        Assert.Equal(0.0, rig.State.RollRemaining, 9);
    }

    [Fact]
    public void The_three_reversals_cost_three_different_things()
    {
        // This is the whole point of having all three. Same entry, same speed,
        // three very different prices.
        const double alt = 900, speed = 66;

        var immelmann = new Rig().Spawn(1200, alt, 0.0, speed);
        immelmann.Pull(Math.PI);
        immelmann.HalfRoll();

        var splitS = new Rig().Spawn(1200, alt, 0.0, speed);
        splitS.HalfRoll();
        splitS.Pull(Math.PI);

        var flat = new Rig().Spawn(1200, alt, 0.0, speed);
        flat.FlatTurn();

        // All three end up pointed the other way and the right way up.
        foreach (var rig in new[] { immelmann, splitS, flat })
        {
            Assert.False(rig.FacingRight);
            Assert.False(rig.State.IsInverted);
        }

        Assert.True(immelmann.Altitude > alt + 50, "the Immelmann buys height");
        Assert.True(splitS.Altitude < alt - 50, "the Split-S spends height");
        Assert.True(Math.Abs(flat.Altitude - alt) < 20, "the flat turn keeps height");

        // And the flat turn is far and away the quickest of the three.
        Assert.True(flat.Time < immelmann.Time * 0.5);
        Assert.True(flat.Time < splitS.Time * 0.5);
    }

    [Fact]
    public void Push_authority_is_a_fraction_of_pull_authority()
    {
        var spec = AircraftSpec.CamelArcade;

        double pullRate = MeasureSlewRate(spec, pull: true);
        double pushRate = MeasureSlewRate(spec, pull: false);

        Assert.True(pullRate > 0);
        Assert.Equal(spec.PushFactor, pushRate / pullRate, 3);
    }

    private static double MeasureSlewRate(AircraftSpec spec, bool pull)
    {
        var s = AircraftState.Spawn(spec, new Vec2(3000, 1500), 0.0, spec.CornerSpeedSeaLevel);
        // Command far enough ahead that the slew rate, not the error, is the limit.
        double command = Angles.Wrap0To2Pi(s.Theta + (pull ? s.CanopySign : -s.CanopySign) * 0.5);
        FlightModel.Step(s, spec, new AircraftInput { ThrottleCommand = 1.0, HeadingCommand = command },
                         Arena.TestRange);
        return Math.Abs(s.SlewRateRad);
    }

    [Fact]
    public void An_inverted_aircraft_turns_the_wrong_way_fast_and_the_right_way_slowly()
    {
        // The rule that gives the roll key a reason to exist. Inverted, the direction
        // that used to be a cheap pull is now an expensive push.
        var spec = AircraftSpec.CamelArcade;

        var upright = AircraftState.Spawn(spec, new Vec2(3000, 1500), 0.0, 60.0);
        var inverted = AircraftState.Spawn(spec, new Vec2(3000, 1500), 0.0, 60.0, inverted: true);

        Assert.False(upright.IsInverted);
        Assert.True(inverted.IsInverted);

        // Both are asked to raise the nose (counter-clockwise, toward +Y).
        double command = Angles.Wrap0To2Pi(0.5);
        FlightModel.Step(upright, spec, new AircraftInput { ThrottleCommand = 1, HeadingCommand = command }, Arena.TestRange);
        FlightModel.Step(inverted, spec, new AircraftInput { ThrottleCommand = 1, HeadingCommand = command }, Arena.TestRange);

        Assert.True(Math.Abs(upright.SlewRateRad) > Math.Abs(inverted.SlewRateRad) * 2.0,
            "pulling the nose up must be far faster upright than inverted");
    }

    [Fact]
    public void An_aileron_roll_keeps_the_heading_and_ends_upright()
    {
        var rig = new Rig().Spawn(3000, 1800, 0.0, 60.0);
        double startHeading = rig.State.Theta;

        rig.Tick(new AircraftInput { ThrottleCommand = 1.0, AileronRollPressed = true, HeadingCommand = startHeading });
        int guard = (int)(3 * FlightModel.TickRate);
        while (rig.State.RollRemaining > 0 && guard-- > 0)
            rig.Tick(new AircraftInput { ThrottleCommand = 1.0, HeadingCommand = rig.State.Theta });

        Assert.Equal(1, rig.State.CanopySign);
        Assert.False(rig.State.IsInverted);
        Assert.Equal(0.0, Angles.Delta(startHeading, rig.State.Theta), 1);
    }

    [Fact]
    public void Many_rolls_do_not_accumulate_orientation_drift()
    {
        var rig = new Rig().Spawn(3000, 2000, 0.0, 60.0);

        for (int i = 0; i < 40; i++)
        {
            rig.HalfRoll();
            int expected = i % 2 == 0 ? -1 : 1;
            Assert.Equal(expected, rig.State.CanopySign);
        }

        // After an even number of half rolls the aircraft is exactly upright again.
        Assert.Equal(0.0, rig.State.RollAngle, 9);
        Assert.Equal(0.0, rig.State.RollRemaining, 9);
    }
}
