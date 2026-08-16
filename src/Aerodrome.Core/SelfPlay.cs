using System;

namespace Aerodrome.Core;

public readonly record struct SelfPlayResult(
    int Matches,
    int TeamZeroWins,
    int TeamOneWins,
    int Draws,
    double AverageSeconds,
    double AverageHits,
    int GroundDeaths,
    int GunfireDeaths,
    int FireDeaths,
    int StructuralDeaths,
    int FledDeaths)
{
    public double TeamZeroWinRate => Matches > 0 ? (double)TeamZeroWins / Matches : 0;
    public double DecisiveRate => Matches > 0 ? (double)(TeamZeroWins + TeamOneWins) / Matches : 0;

    public override string ToString() =>
        $"{Matches} matches: blue {TeamZeroWins} / red {TeamOneWins} / draw {Draws}  " +
        $"({TeamZeroWinRate:P0} blue, {DecisiveRate:P0} decisive)  " +
        $"avg {AverageSeconds:F0}s, {AverageHits:F0} hits  " +
        $"deaths: ground {GroundDeaths}, guns {GunfireDeaths}, fire {FireDeaths}, " +
        $"structure {StructuralDeaths}, fled {FledDeaths}";
}

/// <summary>
/// Runs AI against AI, headless and fast, and reports what happened.
///
/// This is the cheapest way to catch a broken flight model. If two identical
/// aircraft flown by identical pilots do not win about half each, something is
/// asymmetric that should not be. If every round ends in the ground, the stall is
/// wrong. If no round ever ends, the guns are useless. None of that needs a human
/// to notice it.
/// </summary>
public static class SelfPlay
{
    public static SelfPlayResult Run(
        int matches,
        AiSkill? blueSkill = null,
        AiSkill? redSkill = null,
        AircraftSpec? spec = null,
        Arena? arena = null,
        uint seed = 1,
        double timeLimitSeconds = 120.0)
    {
        blueSkill ??= AiSkill.Veteran;
        redSkill ??= AiSkill.Veteran;
        spec ??= AircraftSpec.CamelArcade;
        arena ??= DefaultArena;

        int blue = 0, red = 0, draws = 0;
        int ground = 0, gunfire = 0, fire = 0, structural = 0, fled = 0;
        double totalSeconds = 0, totalHits = 0;

        for (int i = 0; i < matches; i++)
        {
            uint matchSeed = seed + (uint)i * 7919u;
            var match = Match.Duel(arena, spec, matchSeed);
            var bluePilot = new PilotAi(blueSkill, matchSeed + 11u);
            var redPilot = new PilotAi(redSkill, matchSeed + 23u);

            int limit = (int)(timeLimitSeconds * FlightModel.TickRate);
            for (int t = 0; t < limit && match.Outcome == RoundOutcome.InProgress; t++)
            {
                var b = match.Combatants[0];
                var r = match.Combatants[1];
                b.Input = bluePilot.Fly(b, match.NearestEnemy(b), arena, FlightModel.FixedDt);
                r.Input = redPilot.Fly(r, match.NearestEnemy(r), arena, FlightModel.FixedDt);
                match.Step();
            }

            switch (match.Outcome)
            {
                case RoundOutcome.TeamZeroWins: blue++; break;
                case RoundOutcome.TeamOneWins: red++; break;
                default: draws++; break;
            }

            totalSeconds += match.Elapsed;
            foreach (var c in match.Combatants)
            {
                totalHits += c.HitsScored;
                switch (c.State.Death)
                {
                    case DeathCause.Ground: ground++; break;
                    case DeathCause.Gunfire: gunfire++; break;
                    case DeathCause.Fire: fire++; break;
                    case DeathCause.StructuralFailure: structural++; break;
                    case DeathCause.Fled: fled++; break;
                }
            }
        }

        return new SelfPlayResult(
            matches, blue, red, draws,
            matches > 0 ? totalSeconds / matches : 0,
            matches > 0 ? totalHits / matches : 0,
            ground, gunfire, fire, structural, fled);
    }

    public static Arena DefaultArena => new()
    {
        Name = "Self-play range",
        WidthM = 2600,
        CeilingM = 800,
        FleeTimeoutS = 12.0,
    };
}
