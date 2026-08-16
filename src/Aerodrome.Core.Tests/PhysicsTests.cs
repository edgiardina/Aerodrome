using Aerodrome.Core;
using Xunit;

namespace Aerodrome.Core.Tests;

public class PhysicsTests
{
    [Fact]
    public void A_ballistic_body_keeps_its_energy()
    {
        // No wing, no drag, no engine. Only gravity. If the integrator leaks energy
        // here, every energy trade in the game is quietly wrong.
        var spec = AircraftSpec.Ballistic;
        var arena = new Arena { Name = "Vacuum", WidthM = 1e9, CeilingM = 1e9 };
        var s = AircraftState.Spawn(spec, new Vec2(0, 5000), Angles.ToRadians(45), 100.0);

        double startEnergy = s.EnergyHeightM;
        for (int i = 0; i < (int)(10 * FlightModel.TickRate); i++)
            FlightModel.Step(s, spec, AircraftInput.Neutral, arena);

        Assert.True(s.IsAlive);

        // Semi-implicit Euler leaks a little, but it must be a rounding-scale leak and
        // not a trend. 1200 ticks of a 5.5 km arc may drift by well under a meter.
        double drift = Math.Abs(s.EnergyHeightM - startEnergy);
        Assert.True(drift / startEnergy < 1e-4,
            $"integrator leaked energy: {startEnergy:F2} -> {s.EnergyHeightM:F2} m ({drift:F3} m)");
    }

    [Fact]
    public void Stall_speed_matches_the_analytic_value()
    {
        var spec = AircraftSpec.SopwithCamel;
        double analytic = Math.Sqrt(2 * spec.MassKg * Atmosphere.Gravity /
                                    (Atmosphere.SeaLevelDensity * spec.WingAreaM2 * spec.ClMax));

        Assert.Equal(analytic, FlightModel.StallSpeed(spec, Atmosphere.SeaLevelDensity), 6);
        Assert.Equal(analytic, spec.StallSpeedSeaLevel, 6);

        // Sanity against the real aircraft: the Camel stalled near 75 km/h.
        Assert.InRange(spec.StallSpeedSeaLevel * 3.6, 65, 85);
    }

    [Fact]
    public void Lift_peaks_at_the_stall_angle_and_falls_past_it()
    {
        var spec = AircraftSpec.SopwithCamel;

        double atStall = FlightModel.LiftCoefficient(spec.StallAlphaRad, spec);
        double pastStall = FlightModel.LiftCoefficient(spec.StallAlphaRad + 0.20, spec);
        double deepStall = FlightModel.LiftCoefficient(spec.StallAlphaRad + 1.0, spec);

        Assert.Equal(spec.ClMax, atStall, 6);
        Assert.True(pastStall < atStall, "lift must fall once the wing stalls");
        Assert.Equal(spec.ClMax * spec.DeepStallClFraction, deepStall, 6);

        // Symmetric: an inverted wing at the same angle makes the same lift the other way.
        Assert.Equal(-atStall, FlightModel.LiftCoefficient(-spec.StallAlphaRad, spec), 6);
    }

    [Fact]
    public void Peak_turn_rate_happens_at_corner_speed()
    {
        var spec = AircraftSpec.CamelArcade;
        double rho = Atmosphere.SeaLevelDensity;

        double bestSpeed = 0, bestRate = 0;
        for (double v = 10; v <= 120; v += 0.1)
        {
            double rate = FlightModel.MaxSlewRate(v, rho, spec);
            if (rate > bestRate) { bestRate = rate; bestSpeed = v; }
        }

        Assert.Equal(spec.CornerSpeedSeaLevel, bestSpeed, 0);
        Assert.True(bestRate > 0.5, "peak turn rate should be a usable number");
    }

    [Fact]
    public void Turn_rate_falls_off_on_both_sides_of_corner_speed()
    {
        var spec = AircraftSpec.CamelArcade;
        double rho = Atmosphere.SeaLevelDensity;
        double corner = spec.CornerSpeedSeaLevel;

        double atCorner = FlightModel.MaxSlewRate(corner, rho, spec);
        double slow = FlightModel.MaxSlewRate(corner * 0.6, rho, spec);
        double fast = FlightModel.MaxSlewRate(corner * 1.6, rho, spec);

        Assert.True(slow < atCorner, "too slow to make the G");
        Assert.True(fast < atCorner, "too fast, the airframe limits the G");
    }

    [Fact]
    public void An_aircraft_below_stall_speed_barely_answers_the_stick()
    {
        var spec = AircraftSpec.CamelArcade;
        double rho = Atmosphere.SeaLevelDensity;

        double atStall = FlightModel.MaxSlewRate(spec.StallSpeedSeaLevel * 0.8, rho, spec);
        double atCorner = FlightModel.MaxSlewRate(spec.CornerSpeedSeaLevel, rho, spec);

        Assert.True(atStall < atCorner * 0.2,
            $"a stalled aircraft must feel dead: {atStall:F3} vs {atCorner:F3} rad/s");
    }

    [Fact]
    public void A_slow_aircraft_with_the_nose_up_and_no_power_stalls_and_then_spins()
    {
        var spec = AircraftSpec.CamelArcade;
        var rig = new Rig(spec).Spawn(3000, 2500, Angles.ToRadians(75), spec.StallSpeedSeaLevel * 0.9);

        bool sawStall = false, sawSpin = false;
        for (int i = 0; i < (int)(6 * FlightModel.TickRate) && rig.State.IsAlive; i++)
        {
            rig.Tick(new AircraftInput { ThrottleCommand = 0.0, HeadingCommand = rig.State.Theta });
            if (rig.State.IsStalled) sawStall = true;
            if (rig.State.IsSpinning) sawSpin = true;
        }

        Assert.True(sawStall, "a slow nose-high aircraft with no power must stall");
        Assert.True(sawSpin, "a held stall must depart into a spin");
    }

    [Fact]
    public void A_spin_is_recoverable_by_getting_the_nose_down_and_the_speed_back()
    {
        var spec = AircraftSpec.CamelArcade;
        var rig = new Rig(spec).Spawn(3000, 2800, Angles.ToRadians(80), spec.StallSpeedSeaLevel * 0.85);

        for (int i = 0; i < (int)(6 * FlightModel.TickRate) && !rig.State.IsSpinning && rig.State.IsAlive; i++)
            rig.Tick(new AircraftInput { ThrottleCommand = 0.0, HeadingCommand = rig.State.Theta });

        Assert.True(rig.State.IsSpinning, "failed to provoke a spin to recover from");

        // Standard recovery: power on, let the nose fall, let the speed build.
        rig.Drift(6.0, throttle: 1.0);

        Assert.False(rig.State.IsSpinning, "the spin should be recoverable");
        Assert.True(rig.State.IsAlive);
    }

    [Fact]
    public void The_engine_quits_near_the_ceiling_so_an_endless_climb_is_impossible()
    {
        var spec = AircraftSpec.CamelArcade;

        Assert.Equal(1.0, Atmosphere.PowerFraction(0, spec.AbsoluteCeilingM), 6);
        Assert.Equal(0.0, Atmosphere.PowerFraction(spec.AbsoluteCeilingM, spec.AbsoluteCeilingM), 6);
        Assert.True(Atmosphere.PowerFraction(3000, spec.AbsoluteCeilingM) <
                    Atmosphere.PowerFraction(1000, spec.AbsoluteCeilingM));
    }

    [Fact]
    public void The_arena_ceiling_is_a_wall_not_a_kill()
    {
        var arena = new Arena { Name = "Low Ceiling", WidthM = 6000, CeilingM = 1200 };
        var rig = new Rig(AircraftSpec.CamelArcade, arena).Spawn(3000, 1100, Angles.ToRadians(60), 70.0);

        rig.Coast(6.0);

        Assert.True(rig.State.IsAlive, "hitting the ceiling must not kill the pilot");
        Assert.True(rig.Altitude <= arena.CeilingM + 1e-6, $"broke the ceiling: {rig.Altitude:F1} m");
    }

    [Fact]
    public void Flying_into_the_ground_kills()
    {
        var rig = new Rig().Spawn(3000, 200, Angles.ToRadians(-70), 70.0);
        rig.Coast(10.0);

        Assert.False(rig.State.IsAlive);
        Assert.Equal(DeathCause.Ground, rig.State.Death);
    }

    [Fact]
    public void Leaving_the_arena_starts_a_flee_timer_that_eventually_ends_the_round()
    {
        var arena = new Arena { Name = "Narrow", WidthM = 500, CeilingM = 3000, FleeTimeoutS = 3.0 };
        var rig = new Rig(AircraftSpec.CamelArcade, arena).Spawn(480, 1500, 0.0, 70.0);

        rig.Coast(1.0);
        Assert.True(rig.State.IsAlive, "the pilot should get a warning window, not an instant loss");
        Assert.True(rig.State.OutOfBoundsTime > 0);

        rig.Coast(4.0);
        Assert.False(rig.State.IsAlive);
        Assert.Equal(DeathCause.Fled, rig.State.Death);
    }

    [Fact]
    public void Coming_back_inside_the_walls_winds_the_flee_timer_back_down()
    {
        var arena = new Arena { Name = "Narrow", WidthM = 2000, CeilingM = 3000, FleeTimeoutS = 8.0 };

        // Already outside the right wall, but pointed back toward the field.
        var rig = new Rig(AircraftSpec.CamelArcade, arena).Spawn(2150, 1500, Math.PI, 60.0);

        rig.Coast(1.5);
        double peak = rig.State.OutOfBoundsTime;
        Assert.True(peak > 0, "the timer should have started while outside");
        Assert.True(rig.State.IsAlive);

        rig.Coast(3.0);   // 60 m/s inbound clears the 150 m overshoot easily

        Assert.True(arena.IsInsideWalls(rig.State.Position), $"should be back inside, x={rig.State.Position.X:F0}");
        Assert.True(rig.State.IsAlive);
        Assert.True(rig.State.OutOfBoundsTime < peak,
            $"the timer should unwind once you are back inside: {peak:F2} -> {rig.State.OutOfBoundsTime:F2}");
    }

    [Fact]
    public void Wind_changes_airspeed_without_changing_ground_speed()
    {
        var spec = AircraftSpec.CamelArcade;
        var still = new Arena { Name = "Still", WidthM = 20000, CeilingM = 4000 };
        var headwind = still with { Name = "Headwind", Wind = new Vec2(-15, 0) };

        var a = AircraftState.Spawn(spec, new Vec2(3000, 1500), 0.0, 60.0);
        var b = AircraftState.Spawn(spec, new Vec2(3000, 1500), 0.0, 60.0);

        FlightModel.Step(a, spec, AircraftInput.Coast(1.0), still);
        FlightModel.Step(b, spec, AircraftInput.Coast(1.0), headwind);

        // Same ground velocity at spawn, but the one in a headwind has more air over
        // the wing, so it makes more lift and more drag.
        Assert.True(b.Airspeed > a.Airspeed);
    }
}
