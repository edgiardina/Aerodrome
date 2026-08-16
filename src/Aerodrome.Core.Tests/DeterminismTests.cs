using Aerodrome.Core;
using Xunit;

namespace Aerodrome.Core.Tests;

/// <summary>
/// Determinism is not a nicety here. It is what lets the tuning harness replay a
/// fight, what lets the AI train headless, and what leaves the door open for
/// rollback netcode later. If it breaks, all three break quietly.
/// </summary>
public class DeterminismTests
{
    /// <summary>A fixed, repeatable input script. No clock and no Random anywhere.</summary>
    private static AircraftInput ScriptedInput(int tick)
    {
        double t = tick / FlightModel.TickRate;
        return new AircraftInput
        {
            ThrottleCommand = 0.5 + 0.5 * Math.Sin(t * 0.7),
            HeadingCommand = Angles.Wrap0To2Pi(Math.Sin(t * 1.3) * 1.2),
            FireHeld = (tick / 37) % 2 == 0,
            RollPressed = tick % 211 == 0,
        };
    }

    private static AircraftState RunScript(int ticks)
    {
        var spec = AircraftSpec.CamelArcade;
        var arena = Arena.TestRange with { WidthM = 40000 };
        var s = AircraftState.Spawn(spec, new Vec2(20000, 2000), 0.0, 60.0);

        for (int i = 0; i < ticks; i++)
            FlightModel.Step(s, spec, ScriptedInput(i), arena);

        return s;
    }

    [Fact]
    public void The_same_inputs_produce_bit_identical_results()
    {
        var a = RunScript(3600);   // 30 seconds
        var b = RunScript(3600);

        Assert.Equal(a.Position.X, b.Position.X);
        Assert.Equal(a.Position.Y, b.Position.Y);
        Assert.Equal(a.Velocity.X, b.Velocity.X);
        Assert.Equal(a.Velocity.Y, b.Velocity.Y);
        Assert.Equal(a.Theta, b.Theta);
        Assert.Equal(a.RollAngle, b.RollAngle);
        Assert.Equal(a.CanopySign, b.CanopySign);
        Assert.Equal(a.Throttle, b.Throttle);
        Assert.Equal(a.InvertedTime, b.InvertedTime);
        Assert.Equal(a.Airspeed, b.Airspeed);
        Assert.Equal(a.EnergyHeightM, b.EnergyHeightM);
        Assert.Equal(a.IsAlive, b.IsAlive);
    }

    [Fact]
    public void A_long_run_never_produces_NaN_or_infinity()
    {
        var s = RunScript(36000);   // 5 minutes of hard maneuvering

        Assert.True(double.IsFinite(s.Position.X));
        Assert.True(double.IsFinite(s.Position.Y));
        Assert.True(double.IsFinite(s.Velocity.X));
        Assert.True(double.IsFinite(s.Velocity.Y));
        Assert.True(double.IsFinite(s.Theta));
        Assert.True(double.IsFinite(s.RollAngle));
        Assert.True(double.IsFinite(s.Alpha));
        Assert.True(double.IsFinite(s.LoadFactor));
        Assert.True(double.IsFinite(s.EnergyHeightM));
    }

    [Fact]
    public void Angles_stay_wrapped_no_matter_how_long_it_runs()
    {
        var s = RunScript(18000);

        Assert.InRange(s.Theta, 0.0, Angles.TwoPi);
        Assert.InRange(s.RollAngle, 0.0, Angles.TwoPi);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(Math.PI / 4)]
    [InlineData(Math.PI / 2)]
    [InlineData(Math.PI)]
    [InlineData(-Math.PI / 3)]
    [InlineData(3 * Math.PI)]
    public void Angle_wrapping_is_consistent(double raw)
    {
        double wrapped = Angles.Wrap(raw);
        Assert.InRange(wrapped, -Math.PI, Math.PI);

        double zeroTo2Pi = Angles.Wrap0To2Pi(raw);
        Assert.InRange(zeroTo2Pi, 0.0, Angles.TwoPi);

        // Both forms describe the same direction.
        Assert.Equal(0.0, Angles.Delta(wrapped, zeroTo2Pi), 9);
    }

    [Fact]
    public void Spawning_inverted_really_does_start_inverted_at_any_heading()
    {
        var spec = AircraftSpec.CamelArcade;
        for (int deg = 0; deg < 360; deg += 15)
        {
            double heading = Angles.ToRadians(deg);

            var upright = AircraftState.Spawn(spec, new Vec2(3000, 1500), heading, 60.0);
            var inverted = AircraftState.Spawn(spec, new Vec2(3000, 1500), heading, 60.0, inverted: true);

            Assert.False(upright.IsInverted, $"heading {deg} should spawn upright");
            Assert.True(inverted.IsInverted, $"heading {deg} should spawn inverted");
        }
    }
}
