using Aerodrome.Core;
using Xunit;
using Xunit.Abstractions;

namespace Aerodrome.Core.Tests;

/// <summary>
/// Control surfaces need air over them. Stalled or hanging on the propeller, the
/// aeroplane should not answer, and getting the nose down should be the only way
/// out.
/// </summary>
public class AuthorityTests(ITestOutputHelper output)
{
    private static readonly Arena Range = new() { Name = "Range", WidthM = 6000, CeilingM = 4000 };

    private static AircraftState Slow(double speed, AircraftSpec spec)
        => AircraftState.Spawn(spec, new Vec2(3000, 2000), 0.0, speed, throttle: 0.0);

    [Fact]
    public void PrintRollRateAgainstAirspeed()
    {
        var spec = AircraftSpec.CamelArcade;
        double stall = spec.StallSpeedSeaLevel;

        output.WriteLine($"stall {stall * 3.6:F0} km/h, half roll {spec.HalfRollSeconds:F2} s at full authority");
        output.WriteLine("  km/h   x stall   half roll takes");

        foreach (double v in new[] { 8.0, 14.0, 19.0, 26.0, 40.0, 70.0 })
        {
            var state = Slow(v, spec);
            state.Airspeed = v;

            // Press once, then fly until the roll finishes or it is clearly not going to.
            FlightModel.Step(state, spec, new AircraftInput { RollPressed = true, HeadingCommand = 0.0 }, Range);

            bool started = state.RollRemaining > 0;
            double seconds = FlightModel.FixedDt;

            for (int i = 0; i < 20 * 120 && state.RollRemaining > 0 && state.IsAlive; i++)
            {
                FlightModel.Step(state, spec, new AircraftInput { HeadingCommand = state.Theta }, Range);
                seconds += FlightModel.FixedDt;
            }

            output.WriteLine($" {v * 3.6,5:F0}  {v / stall,8:F2}   " +
                             (started ? $"{seconds:F2} s" : "REFUSED"));
        }
    }

    [Fact]
    public void TooSlowToRollIsRefused()
    {
        var spec = AircraftSpec.CamelArcade;

        // Hanging: nose up, almost no speed, wing well past the stall angle.
        var state = AircraftState.Spawn(spec, new Vec2(3000, 2000), Angles.HalfPi, 6.0, throttle: 0.0);
        state.Airspeed = 6.0;

        FlightModel.Step(state, spec, new AircraftInput { RollPressed = true }, Range);

        output.WriteLine($"stalled {state.IsStalled}, airspeed {state.Airspeed * 3.6:F0} km/h, " +
                         $"roll started {state.RollRemaining > 0}, refused {state.RollRefused}");

        Assert.Equal(0.0, state.RollRemaining);
        Assert.True(state.RollRefused, "the aeroplane should say why it did nothing");
    }

    [Fact]
    public void TooSlowToSwapEndsIsRefused()
    {
        var spec = AircraftSpec.CamelArcade;
        var state = AircraftState.Spawn(spec, new Vec2(3000, 2000), Angles.HalfPi, 6.0, throttle: 0.0);
        state.Airspeed = 6.0;

        FlightModel.Step(state, spec, new AircraftInput { FlatTurnPressed = true }, Range);

        Assert.False(state.IsFlatTurning);
        Assert.True(state.RollRefused);
    }

    /// <summary>
    /// A stalled wing at speed is a different thing from a slow one, and it is
    /// treated differently on purpose.
    ///
    /// The aircraft is momentarily stalled for something like fifteen percent of a
    /// hard-fought round, because that is what pulling to the edge of the envelope
    /// means. Blocking the roll outright whenever IsStalled is set would make the
    /// aeroplane feel broken during ordinary fighting. So a separated wing makes
    /// the ailerons mushy, and only genuinely running out of airspeed stops them.
    /// </summary>
    [Fact]
    public void AStalledWingRollsSluggishlyRatherThanNotAtAll()
    {
        var spec = AircraftSpec.CamelArcade;

        // Nose ninety degrees off the flight path at a healthy speed: plenty of air,
        // but the wing is not flying.
        var state = AircraftState.Spawn(spec, new Vec2(3000, 2000), 0.0, 30.0);
        state.Theta = Angles.HalfPi;
        state.Airspeed = 30.0;

        FlightModel.Step(state, spec, new AircraftInput { RollPressed = true }, Range);

        output.WriteLine($"stalled {state.IsStalled}, roll started {state.RollRemaining > 0}");
        Assert.True(state.IsStalled, "the setup should be stalled");
        Assert.True(state.RollRemaining > 0, "a stalled wing at speed should still roll, slowly");

        double seconds = FlightModel.FixedDt;
        for (int i = 0; i < 10 * 120 && state.RollRemaining > 0 && state.IsAlive; i++)
        {
            FlightModel.Step(state, spec, new AircraftInput(), Range);
            seconds += FlightModel.FixedDt;
        }

        output.WriteLine($"half roll while stalled took {seconds:F2} s " +
                         $"against {spec.HalfRollSeconds:F2} s clean");

        Assert.True(seconds > spec.HalfRollSeconds * 1.5,
            $"a stalled roll should be noticeably slower, took {seconds:F2} s");
    }

    [Fact]
    public void AStalledWingWillNotSwapEnds()
    {
        var spec = AircraftSpec.CamelArcade;

        // Fast enough on paper, but the wing is separated, and a flat turn is flown
        // on rudder and aileron.
        var state = AircraftState.Spawn(spec, new Vec2(3000, 2000), 0.0, 40.0);
        state.Theta = Angles.HalfPi;
        state.Airspeed = 40.0;

        FlightModel.Step(state, spec, new AircraftInput { FlatTurnPressed = true }, Range);

        Assert.True(state.IsStalled);
        Assert.False(state.IsFlatTurning, "a separated wing cannot fly a flat 180");
        Assert.True(state.RollRefused);
    }

    /// <summary>
    /// The part that must NOT change. Fights happen between 200 and 400 km/h,
    /// which is fifteen to thirty times the stall speed, so none of this may show
    /// up in ordinary handling.
    /// </summary>
    [Fact]
    public void NormalFlyingRollsAtFullRate()
    {
        var spec = AircraftSpec.CamelArcade;

        foreach (double v in new[] { 45.0, 60.0, 80.0, 110.0 })
        {
            var state = AircraftState.Spawn(spec, new Vec2(3000, 2000), 0.0, v);
            state.Airspeed = v;

            FlightModel.Step(state, spec, new AircraftInput { RollPressed = true, ThrottleCommand = 1.0 }, Range);
            Assert.True(state.RollRemaining > 0, $"a roll at {v * 3.6:F0} km/h should start");

            double seconds = FlightModel.FixedDt;
            for (int i = 0; i < 5 * 120 && state.RollRemaining > 0; i++)
            {
                FlightModel.Step(state, spec, new AircraftInput
                {
                    ThrottleCommand = 1.0,
                    HeadingCommand = state.Theta,
                }, Range);
                seconds += FlightModel.FixedDt;
            }

            output.WriteLine($"{v * 3.6,5:F0} km/h: half roll in {seconds:F2} s");

            Assert.True(seconds < spec.HalfRollSeconds * 1.15,
                $"a roll at {v * 3.6:F0} km/h took {seconds:F2} s, expected about {spec.HalfRollSeconds:F2}");
        }
    }

    /// <summary>
    /// The wingmen. With nothing left to fight the AI cruises, and cruising used to
    /// have no way to right an aeroplane that finished the fight upside down.
    /// </summary>
    [Fact]
    public void SurvivorsEndTheRoundUpright()
    {
        var arena = SelfPlay.DefaultArena;
        var spec = AircraftSpec.CamelArcade;

        var match = new Match(arena, 4242);
        var friend = new Combatant
        {
            Spec = spec,
            Team = 0,
            Callsign = "blue",
            State = AircraftState.Spawn(spec, new Vec2(800, 400), 0.0, 62.0, inverted: true),
        };
        match.Add(friend);

        Assert.True(friend.State.IsInverted, "the setup should start it upside down");

        var pilot = new PilotAi(AiSkill.Veteran, 11u);

        for (int i = 0; i < 12 * 120; i++)
        {
            friend.Input = pilot.Fly(friend, match.NearestEnemy(friend), arena, FlightModel.FixedDt);
            match.Step();
        }

        output.WriteLine($"after 12 s alone: inverted {friend.State.IsInverted}, " +
                         $"starvation {friend.State.FuelStarvation:F2}, alive {friend.State.IsAlive}");

        Assert.True(friend.State.IsAlive);
        Assert.False(friend.State.IsInverted, "a survivor with nobody to fight should roll upright");
    }
}
