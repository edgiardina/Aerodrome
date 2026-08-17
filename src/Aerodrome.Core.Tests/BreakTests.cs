using Aerodrome.Core;
using Xunit;
using Xunit.Abstractions;

namespace Aerodrome.Core.Tests;

/// <summary>
/// The defensive break: a full roll flown to spoil somebody's gun solution.
///
/// It is the only purely defensive move in the game, and the only one that
/// answers "he is on my tail RIGHT NOW". So it has to cost something, or every
/// fight collapses into a rolling contest.
/// </summary>
public class BreakTests(ITestOutputHelper output)
{
    private static readonly Arena Range = new() { Name = "Range", WidthM = 6000, CeilingM = 4000 };

    private static AircraftState Flying(AircraftSpec spec, double speed = 70.0)
        => AircraftState.Spawn(spec, new Vec2(3000, 2000), 0.0, speed);

    private static void Fly(AircraftState s, AircraftSpec spec, double seconds, AircraftInput input)
    {
        int ticks = (int)(seconds * FlightModel.TickRate);
        for (int i = 0; i < ticks && s.IsAlive; i++)
            FlightModel.Step(s, spec, input, Range);
    }

    [Fact]
    public void BreakingShrinksTheTarget()
    {
        var spec = AircraftSpec.CamelArcade;
        var s = Flying(spec);

        Assert.Equal(1.0, s.EvasionRadiusScale, 3);

        FlightModel.Step(s, spec, new AircraftInput { AileronRollPressed = true, ThrottleCommand = 1.0 }, Range);

        Assert.True(s.IsBreaking);
        Assert.True(s.EvasionRadiusScale < 0.6,
            $"a break should be genuinely hard to hit, got {s.EvasionRadiusScale:F2}");
    }

    [Fact]
    public void ThreeBreaksAndYouAreSpent()
    {
        var spec = AircraftSpec.CamelArcade;
        var s = Flying(spec);

        int flown = 0;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            FlightModel.Step(s, spec, new AircraftInput { AileronRollPressed = true, ThrottleCommand = 1.0 }, Range);

            if (s.IsBreaking || s.RollRemaining > 0) flown++;
            else Assert.True(s.BreakRefused, "a break that did not happen should say so");

            // Fly the roll out, but not long enough to recover any meaningful reserve.
            Fly(s, spec, 0.9, new AircraftInput { ThrottleCommand = 1.0, HeadingCommand = 0.0 });
        }

        output.WriteLine($"{flown} breaks flown before the pilot ran out, reserve {s.Reserve:F2}");

        Assert.InRange(flown, 3, 4);
    }

    [Fact]
    public void ASpentPilotCannotPullAsHard()
    {
        var spec = AircraftSpec.CamelArcade;

        var fresh = Flying(spec);
        var spent = Flying(spec);
        spent.Reserve = 0.0;

        Assert.True(spent.PilotGTolerance < fresh.PilotGTolerance);

        // Same hard pull, same time, from the same start.
        var pull = new AircraftInput { ThrottleCommand = 1.0, PitchStick = 1.0 };
        Fly(fresh, spec, 1.0, pull);
        Fly(spent, spec, 1.0, pull);

        double freshSweep = Math.Abs(Angles.Delta(0.0, fresh.Theta));
        double spentSweep = Math.Abs(Angles.Delta(0.0, spent.Theta));

        output.WriteLine($"one second of pull: fresh {Angles.ToDegrees(freshSweep):F0} deg, " +
                         $"spent {Angles.ToDegrees(spentSweep):F0} deg");

        Assert.True(spentSweep < freshSweep * 0.95,
            "a worn out pilot should not turn as well as a fresh one");
    }

    [Fact]
    public void TheReserveComesBack()
    {
        var spec = AircraftSpec.CamelArcade;
        var s = Flying(spec);
        s.Reserve = 0.0;

        Fly(s, spec, 15.0, new AircraftInput { ThrottleCommand = 1.0, HeadingCommand = 0.0 });

        output.WriteLine($"after 15 s of not breaking, reserve {s.Reserve:F2}");
        Assert.True(s.Reserve > 0.9, $"the pilot should have recovered, got {s.Reserve:F2}");
    }

    [Fact]
    public void ABreakCostsSpeed()
    {
        var spec = AircraftSpec.CamelArcade;
        var s = Flying(spec);
        double before = s.Velocity.Length;

        FlightModel.Step(s, spec, new AircraftInput { AileronRollPressed = true, ThrottleCommand = 1.0 }, Range);

        output.WriteLine($"{before * 3.6:F0} km/h -> {s.Velocity.Length * 3.6:F0} km/h");
        Assert.True(s.Velocity.Length < before * 0.98, "a break should scrub energy");
    }

    /// <summary>
    /// The half roll is NOT a break and must stay free. It is how you get upright
    /// after an Immelmann, and putting a cost on that would tax the maneuver set
    /// rather than the evasion.
    /// </summary>
    [Fact]
    public void TheHalfRollIsStillFree()
    {
        var spec = AircraftSpec.CamelArcade;
        var s = Flying(spec);
        s.Reserve = 0.0;

        FlightModel.Step(s, spec, new AircraftInput { RollPressed = true, ThrottleCommand = 1.0 }, Range);

        Assert.True(s.RollRemaining > 0, "an exhausted pilot can still roll upright");
        Assert.False(s.IsBreaking);
        Assert.True(s.Reserve < 0.01, $"the half roll should not spend the pilot, reserve {s.Reserve:F3}");
    }
}
