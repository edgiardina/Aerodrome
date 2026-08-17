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
    /// <summary>
    /// Deaths broken down by side and cause. Aggregate totals hid the reason a
    /// matchup was lopsided more than once, because "someone crashed a lot" and
    /// "someone got shot a lot" look identical until you know which side it was.
    /// </summary>
    public static string DeathBreakdown(
        int matches, AiSkill blueSkill, AiSkill redSkill, uint seed = 1, AircraftSpec? spec = null)
    {
        spec ??= AircraftSpec.CamelArcade;
        var arena = DefaultArena;
        var blue = new int[8];
        var red = new int[8];

        for (int i = 0; i < matches; i++)
        {
            uint matchSeed = seed + (uint)i * 7919u;
            var match = Match.Duel(arena, spec, matchSeed);
            var bluePilot = new PilotAi(blueSkill, matchSeed + 11u);
            var redPilot = new PilotAi(redSkill, matchSeed + 23u);

            int limit = (int)(120 * FlightModel.TickRate);
            for (int t = 0; t < limit && match.Outcome == RoundOutcome.InProgress; t++)
            {
                var b = match.Combatants[0];
                var r = match.Combatants[1];
                b.Input = bluePilot.Fly(b, match.NearestEnemy(b), arena, FlightModel.FixedDt);
                r.Input = redPilot.Fly(r, match.NearestEnemy(r), arena, FlightModel.FixedDt);
                match.Step();
            }

            blue[(int)match.Combatants[0].State.Death]++;
            red[(int)match.Combatants[1].State.Death]++;
        }

        string Fmt(int[] d, string who) =>
            $"{who}: survived {d[(int)DeathCause.None]}, ground {d[(int)DeathCause.Ground]}, " +
            $"guns {d[(int)DeathCause.Gunfire]}, fire {d[(int)DeathCause.Fire]}, " +
            $"structure {d[(int)DeathCause.StructuralFailure]}, fled {d[(int)DeathCause.Fled]}";

        return $"{Fmt(blue, blueSkill.Name + " (blue)")}\n    {Fmt(red, redSkill.Name + " (red)")}";
    }

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

    /// <summary>
    /// Win rate of <paramref name="a"/> against <paramref name="b"/>, flown from
    /// both sides and combined.
    ///
    /// Always compare this way. Measuring two pilots against a common third is
    /// worthless here: the baseline carries its own sampling error, and a mirror
    /// match over sixty rounds swings eight points either side of even on noise
    /// alone. Swapping sides cancels any spawn advantage and doubles the sample,
    /// so what is left is the skill difference.
    /// </summary>
    public static double HeadToHead(
        AiSkill a, AiSkill b, int matchesPerSide = 60, uint seed = 1, AircraftSpec? spec = null)
    {
        var forward = Run(matchesPerSide, a, b, spec, seed: seed);
        var reversed = Run(matchesPerSide, b, a, spec, seed: seed);

        int wins = forward.TeamZeroWins + reversed.TeamOneWins;
        int losses = forward.TeamOneWins + reversed.TeamZeroWins;

        return wins + losses > 0 ? (double)wins / (wins + losses) : 0.5;
    }

    /// <summary>
    /// What happened when one pilot fought a flight. The last two numbers are the
    /// ones that say whether the coordination is doing its job.
    /// </summary>
    public readonly record struct FlightResult(
        int Matches,
        int LoneWins,
        int FlightWins,
        int Draws,
        double AverageSeconds,
        double AveragePressing,
        double GangUpRate)
    {
        public double LoneWinRate => Matches > 0 ? (double)LoneWins / Matches : 0;

        public override string ToString() =>
            $"{Matches} matches: lone {LoneWins} / flight {FlightWins} / draw {Draws}  " +
            $"({LoneWinRate:P0} lone)  avg {AverageSeconds:F0}s  " +
            $"pressing {AveragePressing:F2}  ganged {GangUpRate:P1}";
    }

    /// <summary>
    /// One pilot against a flight of several, so the coordinator can be measured
    /// rather than admired.
    ///
    /// GangUpRate is the number that matters. It is the fraction of ticks where more
    /// than one enemy had the lone pilot inside gun range with the trigger down. If
    /// that climbs, the flight has gone back to being a firing squad, whatever the
    /// role assignment says it is doing.
    /// </summary>
    public static FlightResult RunFlight(
        int matches,
        int enemyCount = 3,
        AiSkill? skill = null,
        AircraftSpec? loneSpec = null,
        AircraftSpec? enemySpec = null,
        Arena? arena = null,
        uint seed = 1,
        double timeLimitSeconds = 120.0)
    {
        skill ??= AiSkill.Veteran;
        loneSpec ??= AircraftSpec.CamelArcade;
        enemySpec ??= AircraftSpec.FokkerDr1Arcade;
        arena ??= DefaultArena;

        int lone = 0, flightWins = 0, draws = 0;
        double totalSeconds = 0, totalPressing = 0, gangTicks = 0, totalTicks = 0;

        for (int i = 0; i < matches; i++)
        {
            uint matchSeed = seed + (uint)i * 7919u;
            var match = Match.Engagement(arena, loneSpec, enemySpec, enemyCount, matchSeed);

            var lonePilot = new PilotAi(skill, matchSeed + 11u);
            var flight = new Flight(team: 1);
            var pilots = new PilotAi[enemyCount];

            for (int e = 0; e < enemyCount; e++)
            {
                flight.Add(match.Combatants[e + 1]);
                pilots[e] = new PilotAi(skill, matchSeed + 23u + (uint)e * 37u);
            }

            int limit = (int)(timeLimitSeconds * FlightModel.TickRate);
            for (int t = 0; t < limit && match.Outcome == RoundOutcome.InProgress; t++)
            {
                var blue = match.Combatants[0];
                blue.Input = lonePilot.Fly(blue, match.NearestEnemy(blue), arena, FlightModel.FixedDt);

                flight.Update(match, arena, FlightModel.FixedDt);

                int shooting = 0, pressing = 0;
                for (int e = 0; e < enemyCount; e++)
                {
                    var red = match.Combatants[e + 1];
                    red.Input = pilots[e].Fly(red, flight.Target, arena, FlightModel.FixedDt,
                                              flight.OrdersFor(red));

                    if (!red.IsAlive) continue;

                    double range = (blue.State.Position - red.State.Position).Length;
                    if (range < 300.0) pressing++;
                    if (red.Input.FireHeld && range < skill.FireRangeM) shooting++;
                }

                totalPressing += pressing;
                if (shooting > 1) gangTicks++;
                totalTicks++;

                match.Step();
            }

            switch (match.Outcome)
            {
                case RoundOutcome.TeamZeroWins: lone++; break;
                case RoundOutcome.TeamOneWins: flightWins++; break;
                default: draws++; break;
            }

            totalSeconds += match.Elapsed;
        }

        return new FlightResult(
            matches, lone, flightWins, draws,
            matches > 0 ? totalSeconds / matches : 0,
            totalTicks > 0 ? totalPressing / totalTicks : 0,
            totalTicks > 0 ? gangTicks / totalTicks : 0);
    }

    public static Arena DefaultArena => new()
    {
        Name = "Self-play range",
        WidthM = 2600,
        CeilingM = 800,
        FleeTimeoutS = 12.0,
    };
}
