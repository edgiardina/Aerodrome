using Aerodrome.Core;
using Xunit;
using Xunit.Abstractions;

namespace Aerodrome.Core.Tests;

/// <summary>
/// What happens if you point it straight down at full power and then pull out.
/// </summary>
public class DiveTests(ITestOutputHelper output)
{
    /// <summary>Dive to the given speed, then haul back as hard as the stick allows.</summary>
    private static (double peakG, double peakSpeed, DeathCause death, double alphaPeak) Dive(
        double wingHealth, double startAltitude = 780, double targetSpeed = 999)
    {
        var arena = new Arena { Name = "Dive range", WidthM = 6000, CeilingM = 3000 };
        var spec = AircraftSpec.CamelArcade;
        var state = AircraftState.Spawn(spec, new Vec2(3000, startAltitude), -Angles.HalfPi, 60.0);
        state.WingHealth = wingHealth;

        double peakG = 0, peakSpeed = 0, alphaPeak = 0;

        // Straight down, full throttle, until it is really moving or the ground is close.
        for (int i = 0; i < 120 * 20 && state.IsAlive; i++)
        {
            FlightModel.Step(state, spec, new AircraftInput
            {
                ThrottleCommand = 1.0,
                HeadingCommand = Angles.Wrap0To2Pi(-Angles.HalfPi),
            }, arena);

            peakSpeed = Math.Max(peakSpeed, state.Airspeed);
            if (state.Airspeed >= targetSpeed || state.Position.Y < 320) break;
        }

        // Now pull for all it is worth, throttled back. Easing the throttle keeps
        // this about the G limit: at full power the aircraft simply stays past its
        // never-exceed speed and the wings come off for a different reason, which
        // is a real outcome but not the one this is measuring.
        for (int i = 0; i < 120 * 4 && state.IsAlive; i++)
        {
            FlightModel.Step(state, spec, new AircraftInput
            {
                ThrottleCommand = 0.15,
                PitchStick = 1.0,
            }, arena);

            Damage.Step(state, spec, FlightModel.FixedDt);

            peakG = Math.Max(peakG, Math.Abs(state.LoadFactor));
            alphaPeak = Math.Max(alphaPeak, Math.Abs(state.Alpha));
            peakSpeed = Math.Max(peakSpeed, state.Airspeed);
        }

        return (peakG, peakSpeed, state.Death, alphaPeak);
    }

    [Fact]
    public void PrintDiveAndPullOut()
    {
        var spec = AircraftSpec.CamelArcade;
        output.WriteLine($"G limit {spec.GLimit:F1}   stall alpha {Angles.ToDegrees(spec.StallAlphaRad):F1} deg");
        output.WriteLine("wing    peak G   peak km/h   peak alpha   outcome");

        foreach (double wing in new[] { 1.00, 0.94, 0.80, 0.60 })
        {
            var (g, v, death, alpha) = Dive(wing);
            output.WriteLine($"{wing,4:F2}  {g,8:F1}  {v * 3.6,10:F0}  {Angles.ToDegrees(alpha),10:F1}   {death}");
        }
    }

    /// <summary>
    /// The elevator may not out-pull the airframe. It exists to reach the G limit
    /// quickly, not to exceed it, and corner speed stops meaning anything if it can.
    /// </summary>
    [Fact]
    public void TheElevatorCannotExceedTheGLimit()
    {
        var spec = AircraftSpec.CamelArcade;
        var (peakG, peakSpeed, _, _) = Dive(wingHealth: 1.0);

        output.WriteLine($"peak {peakG:F2} G at up to {peakSpeed * 3.6:F0} km/h, limit {spec.GLimit:F1}");

        // A little overshoot inside one tick is unavoidable with a discrete step.
        // Half a G is tolerance; anything more is the elevator out-turning the wing.
        Assert.True(peakG <= spec.GLimit + 0.5,
            $"pulled {peakG:F1} G against a {spec.GLimit:F1} G limit");
    }
}

/// <summary>
/// The dive limit. A wood and wire aeroplane held past its never-exceed speed
/// comes apart, and the pilot has to be able to see it coming and back off.
/// </summary>
public class OverspeedTests(ITestOutputHelper output)
{
    private static readonly Arena Range = new() { Name = "Dive range", WidthM = 6000, CeilingM = 4000 };

    /// <summary>Hold a vertical dive at full power for a given time.</summary>
    private static AircraftState PowerDive(double seconds, AircraftSpec? spec = null)
    {
        spec ??= AircraftSpec.CamelArcade;
        var state = AircraftState.Spawn(spec, new Vec2(3000, 3800), -Angles.HalfPi, 60.0);

        int ticks = (int)(seconds * FlightModel.TickRate);
        for (int i = 0; i < ticks && state.IsAlive; i++)
        {
            FlightModel.Step(state, spec, new AircraftInput
            {
                ThrottleCommand = 1.0,
                HeadingCommand = Angles.Wrap0To2Pi(-Angles.HalfPi),
            }, Range);

            Damage.Step(state, spec, FlightModel.FixedDt);
        }
        return state;
    }

    [Fact]
    public void NormalFlyingNeverTripsIt()
    {
        var spec = AircraftSpec.CamelArcade;
        var state = AircraftState.Spawn(spec, new Vec2(3000, 600), 0.0, 60.0);

        // Thirty seconds of level full throttle, which is as fast as it goes
        // without pointing it downhill.
        for (int i = 0; i < 30 * 120; i++)
        {
            FlightModel.Step(state, spec, new AircraftInput
            {
                ThrottleCommand = 1.0,
                HeadingCommand = 0.0,
            }, Range);
            Damage.Step(state, spec, FlightModel.FixedDt);
        }

        output.WriteLine($"level top speed {state.Airspeed * 3.6:F0} km/h, " +
                         $"VNE {spec.NeverExceedSpeed * 3.6:F0}, stress {state.OverspeedStress:F3}");

        Assert.True(state.IsAlive);
        Assert.Equal(0.0, state.OverspeedStress, 3);
    }

    [Fact]
    public void AShortDiveIsSurvivable()
    {
        // Long enough to go past the limit, short enough to get away with it.
        var state = PowerDive(6.0);

        output.WriteLine($"after 6 s: {state.Airspeed * 3.6:F0} km/h, stress {state.OverspeedStress:F2}, alive {state.IsAlive}");
        Assert.True(state.IsAlive, "a brief overspeed should be a decision, not a death sentence");
    }

    [Fact]
    public void HoldingItTooLongTakesTheWingsOff()
    {
        var state = PowerDive(20.0);

        output.WriteLine($"after 20 s: alive {state.IsAlive}, death {state.Death}, " +
                         $"stress {state.OverspeedStress:F2}");

        Assert.False(state.IsAlive);
        Assert.Equal(DeathCause.StructuralFailure, state.Death);
    }

    /// <summary>
    /// Starts fast and level rather than in a dive, on purpose. Recovering from a
    /// vertical dive at 400 km/h takes long enough that the stress keeps building
    /// through the pull-out, which is realistic but tests the wrong thing. What
    /// this pins is the recovery path itself: below the limit, stress sheds.
    /// </summary>
    [Fact]
    public void EasingOffLetsTheAirframeRecover()
    {
        var spec = AircraftSpec.CamelArcade;

        // Level, and comfortably over the never-exceed speed to begin with.
        var state = AircraftState.Spawn(spec, new Vec2(3000, 2000), 0.0,
                                        spec.NeverExceedSpeed * 1.14, throttle: 1.0);

        for (int i = 0; i < (int)(1.5 * 120) && state.IsAlive; i++)
        {
            FlightModel.Step(state, spec, new AircraftInput
            {
                ThrottleCommand = 1.0,
                HeadingCommand = 0.0,
            }, Range);
            Damage.Step(state, spec, FlightModel.FixedDt);
        }

        double stressed = state.OverspeedStress;
        output.WriteLine($"after 1.5 s over the limit: {state.Airspeed * 3.6:F0} km/h, stress {stressed:F3}");
        Assert.True(stressed > 0.05, $"should have built some stress, got {stressed:F3}");

        // Throttle right back and hold level. Drag does the rest.
        for (int i = 0; i < 15 * 120 && state.IsAlive; i++)
        {
            FlightModel.Step(state, spec, new AircraftInput
            {
                ThrottleCommand = 0.0,
                HeadingCommand = 0.0,
            }, Range);
            Damage.Step(state, spec, FlightModel.FixedDt);
        }

        output.WriteLine($"after easing off: {state.Airspeed * 3.6:F0} km/h, " +
                         $"stress {stressed:F2} -> {state.OverspeedStress:F2}, alive {state.IsAlive}");

        Assert.True(state.IsAlive);
        Assert.True(state.OverspeedStress < stressed * 0.5, "easing off should shed the stress");
    }


    /// <summary>
    /// The case that looks like a bug and is not: level, and still overspeeding.
    ///
    /// You cannot reach the never-exceed speed in level flight. You CAN arrive in
    /// level flight already past it, straight out of a dive, and drag takes a few
    /// seconds to bring you back under. For those seconds the aeroplane looks level
    /// while the warning is up.
    ///
    /// The rule that has to hold is that easing off always saves you. Pulling level
    /// and leaving the throttle open is the pilot's mistake and may not; pulling
    /// level and closing it must.
    /// </summary>
    [Fact]
    public void LevellingOutAndThrottlingBackIsAlwaysSurvivable()
    {
        var spec = AircraftSpec.CamelArcade;

        // Up to about fifteen percent over, which is everything the arena can
        // actually produce: a dive from the ceiling to the deck peaks near 410 km/h
        // against a 360 limit. Beyond that there IS a point of no return, but you
        // have to be given several thousand metres of height to find it.
        foreach (double entry in new[] { 1.05, 1.10, 1.15 })
        {
            var state = AircraftState.Spawn(spec, new Vec2(3000, 2000), 0.0,
                                            spec.NeverExceedSpeed * entry, throttle: 1.0);

            double secondsOver = 0;
            for (int i = 0; i < 20 * 120 && state.IsAlive; i++)
            {
                FlightModel.Step(state, spec, new AircraftInput
                {
                    ThrottleCommand = 0.0,
                    HeadingCommand = 0.0,
                }, Range);
                Damage.Step(state, spec, FlightModel.FixedDt);

                if (state.Airspeed > spec.NeverExceedSpeed) secondsOver += FlightModel.FixedDt;
            }

            output.WriteLine($"entered {entry * spec.NeverExceedSpeed * 3.6:F0} km/h -> " +
                             $"{secondsOver:F1} s over the limit, peak stress {state.OverspeedStress:F2}, " +
                             $"alive {state.IsAlive}");

            Assert.True(state.IsAlive,
                $"levelling out and closing the throttle from {entry:P0} of VNE killed it");
        }
    }

    /// <summary>
    /// And the mistake it is meant to punish: level, but the throttle left open.
    /// </summary>
    [Fact]
    public void PrintLevellingOutAtFullThrottle()
    {
        var spec = AircraftSpec.CamelArcade;
        var state = AircraftState.Spawn(spec, new Vec2(3000, 2000), 0.0,
                                        spec.NeverExceedSpeed * 1.15, throttle: 1.0);

        double secondsOver = 0;
        for (int i = 0; i < 20 * 120 && state.IsAlive; i++)
        {
            FlightModel.Step(state, spec, new AircraftInput
            {
                ThrottleCommand = 1.0,
                HeadingCommand = 0.0,
            }, Range);
            Damage.Step(state, spec, FlightModel.FixedDt);

            if (state.Airspeed > spec.NeverExceedSpeed) secondsOver += FlightModel.FixedDt;
        }

        output.WriteLine($"full throttle: {secondsOver:F1} s over the limit, " +
                         $"settled at {state.Airspeed * 3.6:F0} km/h, " +
                         $"stress {state.OverspeedStress:F2}, alive {state.IsAlive}");
    }

    [Fact]
    public void PrintDiveProfile()
    {
        var spec = AircraftSpec.CamelArcade;
        var state = AircraftState.Spawn(spec, new Vec2(3000, 3800), -Angles.HalfPi, 60.0);

        output.WriteLine($"VNE {spec.NeverExceedSpeed * 3.6:F0} km/h, tolerance {spec.OverspeedToleranceS:F1} s");
        output.WriteLine("   t   km/h   stress");

        for (int i = 0; i < 20 * 120 && state.IsAlive; i++)
        {
            FlightModel.Step(state, spec, new AircraftInput
            {
                ThrottleCommand = 1.0,
                HeadingCommand = Angles.Wrap0To2Pi(-Angles.HalfPi),
            }, Range);
            Damage.Step(state, spec, FlightModel.FixedDt);

            if (i % 240 == 0)
                output.WriteLine($" {i / 120.0,4:F1}  {state.Airspeed * 3.6,5:F0}   {state.OverspeedStress,6:F2}");
        }

        output.WriteLine($"ended alive {state.IsAlive} death {state.Death}");
    }
}
