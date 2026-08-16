using Aerodrome.Core;
using Xunit;
using Xunit.Abstractions;

namespace Aerodrome.Core.Tests;

/// <summary>
/// The cheapest way to catch a broken flight model. Two identical aircraft flown
/// by identical pilots must win about half each. Anything else means something is
/// asymmetric that should not be, and a human would take hours to notice.
/// </summary>
public class SelfPlayTests(ITestOutputHelper output)
{
    [Fact]
    public void PrintSelfPlaySummary()
    {
        foreach (var skill in AiSkill.All)
        {
            var result = SelfPlay.Run(40, skill, skill, seed: 100);
            output.WriteLine($"{skill.Name,-8} {result}");
        }

        output.WriteLine("");
        output.WriteLine("--- skill should beat lack of it (each pairing flown from both sides) ---");
        foreach (var (a, b) in new[]
                 {
                     (AiSkill.Ace, AiSkill.Rookie),
                     (AiSkill.Veteran, AiSkill.Rookie),
                     (AiSkill.Ace, AiSkill.Veteran),
                 })
        {
            // Swap sides and add the results. Any residual spawn advantage cancels
            // out, so what is left is the skill difference and nothing else.
            var forward = SelfPlay.Run(40, a, b, seed: 200);
            var reversed = SelfPlay.Run(40, b, a, seed: 200);

            int wins = forward.TeamZeroWins + reversed.TeamOneWins;
            int losses = forward.TeamOneWins + reversed.TeamZeroWins;
            double rate = wins + losses > 0 ? (double)wins / (wins + losses) : 0.5;

            output.WriteLine($"{a.Name,-8} vs {b.Name,-8} -> {a.Name} wins {rate:P0} " +
                             $"({wins}-{losses}, {forward.Draws + reversed.Draws} draws of 80)");
        }
    }

    [Fact]
    public void Identical_pilots_in_identical_aircraft_are_evenly_matched()
    {
        var result = SelfPlay.Run(120, AiSkill.Veteran, AiSkill.Veteran, seed: 4242);
        output.WriteLine(result.ToString());

        // Only decisive rounds tell us anything about symmetry.
        int decisive = result.TeamZeroWins + result.TeamOneWins;
        Assert.True(decisive >= 20, $"too few rounds resolved to judge: {decisive}/{result.Matches}");

        double blueShare = (double)result.TeamZeroWins / decisive;
        Assert.InRange(blueShare, 0.32, 0.68);
    }

    [Fact]
    public void Most_rounds_actually_resolve()
    {
        // If nearly everything is a draw, the guns are useless or the AI will not
        // commit, and the game has no ending.
        var result = SelfPlay.Run(60, AiSkill.Veteran, AiSkill.Veteran, seed: 77);
        output.WriteLine(result.ToString());

        Assert.True(result.DecisiveRate > 0.5,
            $"only {result.DecisiveRate:P0} of rounds resolved");
    }

    [Fact]
    public void Pilots_die_to_gunfire_more_than_to_anything_else()
    {
        // A dogfighting game where everyone flies into the ground is a flying game
        // with a stall bug, not a dogfighting game.
        var result = SelfPlay.Run(60, AiSkill.Veteran, AiSkill.Veteran, seed: 555);
        output.WriteLine(result.ToString());

        int shotDown = result.GunfireDeaths + result.FireDeaths + result.StructuralDeaths;
        Assert.True(shotDown > result.GroundDeaths,
            $"combat deaths {shotDown} should beat ground deaths {result.GroundDeaths}");
    }

    [Fact]
    public void A_better_pilot_beats_a_worse_one()
    {
        var result = SelfPlay.Run(80, AiSkill.Ace, AiSkill.Rookie, seed: 31337);
        output.WriteLine(result.ToString());

        int decisive = result.TeamZeroWins + result.TeamOneWins;
        Assert.True(decisive >= 20, "not enough resolved rounds to judge");
        Assert.True((double)result.TeamZeroWins / decisive > 0.62,
            $"an Ace should beat a Rookie clearly, got {(double)result.TeamZeroWins / decisive:P0}");
    }

    [Fact]
    public void The_skill_ladder_goes_the_right_way()
    {
        // Every rung must beat the one below it against a common opponent. A ladder
        // that is not monotonic means a difficulty setting is secretly a handicap,
        // which is exactly what happened the first time: the "braver" pilots fought
        // on while damaged and went even with the worst pilot in the game.
        double Against(AiSkill skill)
        {
            var r = SelfPlay.Run(60, skill, AiSkill.Veteran, seed: 8080);
            int decisive = r.TeamZeroWins + r.TeamOneWins;
            double rate = decisive > 0 ? (double)r.TeamZeroWins / decisive : 0.5;
            output.WriteLine($"{skill.Name,-8} vs Veteran -> {rate:P0}  ({r})");
            return rate;
        }

        double rookie = Against(AiSkill.Rookie);
        double veteran = Against(AiSkill.Veteran);
        double ace = Against(AiSkill.Ace);

        Assert.True(rookie < veteran, $"Rookie {rookie:P0} should trail Veteran {veteran:P0}");
        Assert.True(veteran < ace, $"Veteran {veteran:P0} should trail Ace {ace:P0}");
        Assert.InRange(veteran, 0.32, 0.68);   // Veteran against itself is a coin flip
    }

    [Fact]
    public void Self_play_is_deterministic()
    {
        var a = SelfPlay.Run(12, seed: 909);
        var b = SelfPlay.Run(12, seed: 909);
        Assert.Equal(a, b);
    }

    [Fact]
    public void No_match_ever_produces_a_broken_number()
    {
        var arena = SelfPlay.DefaultArena;
        var match = Match.Duel(arena, AircraftSpec.CamelArcade, seed: 8);
        var blue = new PilotAi(AiSkill.Ace, 1);
        var red = new PilotAi(AiSkill.Ace, 2);

        for (int t = 0; t < (int)(90 * FlightModel.TickRate) && match.Outcome == RoundOutcome.InProgress; t++)
        {
            foreach (var c in match.Combatants)
                c.Input = (c.Team == 0 ? blue : red).Fly(c, match.NearestEnemy(c), arena, FlightModel.FixedDt);
            match.Step();

            foreach (var c in match.Combatants)
            {
                var s = c.State;
                Assert.True(double.IsFinite(s.Position.X) && double.IsFinite(s.Position.Y));
                Assert.True(double.IsFinite(s.Velocity.X) && double.IsFinite(s.Velocity.Y));
                Assert.True(double.IsFinite(s.Theta) && double.IsFinite(s.Alpha));
            }
        }
    }
}
