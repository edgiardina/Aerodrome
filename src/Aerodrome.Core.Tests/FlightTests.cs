using Aerodrome.Core;
using Xunit;
using Xunit.Abstractions;

namespace Aerodrome.Core.Tests;

/// <summary>
/// The flight coordinator exists to stop several opponents being a firing squad.
/// These check the promise it makes: one aircraft presses at a time, the rest wait
/// above, and the assignment does not thrash.
/// </summary>
public class FlightTests(ITestOutputHelper output)
{
    private static readonly Arena Range = SelfPlay.DefaultArena;

    private static (Match match, Flight flight, PilotAi[] pilots, PilotAi lone) Setup(
        int enemyCount, uint seed = 7, AiSkill? skill = null)
    {
        skill ??= AiSkill.Veteran;

        var match = Match.Engagement(
            Range, AircraftSpec.CamelArcade, AircraftSpec.FokkerDr1Arcade, enemyCount, seed);

        var flight = new Flight(team: 1);
        var pilots = new PilotAi[enemyCount];

        for (int i = 0; i < enemyCount; i++)
        {
            flight.Add(match.Combatants[i + 1]);
            pilots[i] = new PilotAi(skill, seed + 23u + (uint)i * 37u);
        }

        return (match, flight, pilots, new PilotAi(skill, seed + 11u));
    }

    private static void Fly(Match match, Flight flight, PilotAi[] pilots, PilotAi lone, int ticks)
    {
        for (int t = 0; t < ticks && match.Outcome == RoundOutcome.InProgress; t++)
        {
            var blue = match.Combatants[0];
            blue.Input = lone.Fly(blue, match.NearestEnemy(blue), Range, FlightModel.FixedDt);

            flight.Update(match, Range, FlightModel.FixedDt);

            for (int i = 0; i < pilots.Length; i++)
            {
                var red = match.Combatants[i + 1];
                red.Input = pilots[i].Fly(red, flight.Target, Range, FlightModel.FixedDt,
                                          flight.OrdersFor(red));
            }

            match.Step();
        }
    }

    [Fact]
    public void ExactlyOneMemberPressesTheAttack()
    {
        var (match, flight, pilots, lone) = Setup(3);

        int checks = 0;
        for (int t = 0; t < 120 * 30 && match.Outcome == RoundOutcome.InProgress; t++)
        {
            Fly(match, flight, pilots, lone, 1);

            int alive = flight.AliveCount;
            if (alive == 0) break;

            int engaged = 0;
            foreach (var m in flight.Members)
                if (m.IsAlive && flight.OrdersFor(m).Role == FlightRole.Engaged) engaged++;

            // Nobody presses while the flight is regrouping after a loss. That is
            // the one window where the answer is zero rather than one.
            Assert.Equal(flight.IsShaken ? 0 : 1, engaged);
            checks++;
        }

        Assert.True(checks > 1000, $"only got {checks} ticks of fight to check");
    }

    [Fact]
    public void SupportingPilotsWaitAboveTheTarget()
    {
        var (match, flight, pilots, lone) = Setup(3);
        Fly(match, flight, pilots, lone, (int)(6.0 * FlightModel.TickRate));

        int samples = 0, higher = 0;

        for (int t = 0; t < 20 * 120 && match.Outcome == RoundOutcome.InProgress; t++)
        {
            Fly(match, flight, pilots, lone, 1);
            if (flight.Target is null || flight.AliveCount < 2) break;

            double targetY = flight.Target.State.Position.Y;

            foreach (var m in flight.Members)
            {
                if (!m.IsAlive || flight.OrdersFor(m).Role != FlightRole.Supporting) continue;
                samples++;
                if (m.State.Position.Y > targetY) higher++;
            }
        }

        double fraction = samples > 0 ? (double)higher / samples : 0;
        output.WriteLine($"supporting pilots above the target: {fraction:P0} of {samples} samples");

        // Not all of the time. A pilot climbing back to a perch, or one that just
        // broke off a pass, is legitimately below it. Most of the time is the claim.
        Assert.True(fraction > 0.65, $"supporting pilots were only above the target {fraction:P0} of the time");
    }

    [Fact]
    public void HandoverDoesNotThrash()
    {
        var (match, flight, pilots, lone) = Setup(3);

        Combatant? previous = null;
        int handovers = 0;
        double seconds = 0;

        for (int t = 0; t < 60 * 120 && match.Outcome == RoundOutcome.InProgress; t++)
        {
            Fly(match, flight, pilots, lone, 1);
            seconds += FlightModel.FixedDt;

            if (flight.Engaged is null) continue;
            if (previous is not null && !ReferenceEquals(previous, flight.Engaged)) handovers++;
            previous = flight.Engaged;
        }

        double perMinute = seconds > 0 ? handovers / seconds * 60.0 : 0;
        output.WriteLine($"{handovers} handovers in {seconds:F0}s ({perMinute:F1}/min)");

        // The hold timer is three seconds, so twenty a minute is the hard ceiling.
        // Deaths force a handover regardless, so allow a little headroom over that.
        Assert.True(perMinute < 22, $"the flight swapped attacker {perMinute:F1} times a minute");
    }

    [Fact]
    public void ALoneSurvivorPressesTheAttack()
    {
        var (match, flight, pilots, lone) = Setup(3);

        // Kill two of the three outright, then fly past the regroup so the last
        // one has had time to pull itself together and come back in.
        match.Combatants[1].State.AirframeIntegrity = 0;
        match.Combatants[2].State.AirframeIntegrity = 0;
        Fly(match, flight, pilots, lone, (int)((Flight.ShakenSeconds + 2.0) * FlightModel.TickRate));

        Assert.False(flight.IsShaken);
        Assert.Equal(1, flight.AliveCount);
        Assert.NotNull(flight.Engaged);
        Assert.Equal(FlightRole.Engaged, flight.OrdersFor(flight.Engaged!).Role);
    }

    [Fact]
    public void OrdersAreDeterministic()
    {
        string Run()
        {
            var (match, flight, pilots, lone) = Setup(3, seed: 99);
            Fly(match, flight, pilots, lone, (int)(25.0 * FlightModel.TickRate));

            var s = new System.Text.StringBuilder();
            foreach (var m in flight.Members)
                s.Append($"{m.Callsign}:{m.State.Position}:{flight.OrdersFor(m).Role};");
            return s.ToString();
        }

        Assert.Equal(Run(), Run());
    }

    /// <summary>
    /// The whole design claim in one number. GangUpRate is the fraction of ticks
    /// where more than one enemy had the trigger down inside gun range.
    /// </summary>
    [Fact]
    public void AFlightDoesNotGangUp()
    {
        var solo = SelfPlay.RunFlight(12, enemyCount: 1, seed: 300);
        var pair = SelfPlay.RunFlight(12, enemyCount: 2, seed: 300);
        var three = SelfPlay.RunFlight(12, enemyCount: 3, seed: 300);

        output.WriteLine($"1 v 1  {solo}");
        output.WriteLine($"1 v 2  {pair}");
        output.WriteLine($"1 v 3  {three}");

        Assert.True(three.GangUpRate < 0.04,
            $"three aircraft were firing at once {three.GangUpRate:P1} of the time");

        // And it must still be harder than a fair fight, or the coordination has
        // simply defanged the enemy instead of organising it.
        Assert.True(three.LoneWinRate <= solo.LoneWinRate,
            $"the lone pilot won {three.LoneWinRate:P0} against three and {solo.LoneWinRate:P0} against one");
    }
}
