using Aerodrome.Core;
using Xunit;
using Xunit.Abstractions;

namespace Aerodrome.Core.Tests;

/// <summary>
/// What each skill of pilot actually DOES with the aeroplane, rather than whether
/// it won. Win rate says a pilot is worse. This says why.
/// </summary>
public class PilotBehaviourDiagnostics(ITestOutputHelper output)
{
    [Fact]
    public void PrintHowEachSkillFlies()
    {
        output.WriteLine("skill     |slew| deg/s   |alpha| deg   stalled   spd km/h   Eh m   reversals/min");

        foreach (var skill in AiSkill.All)
        {
            var arena = SelfPlay.DefaultArena;
            double slew = 0, alpha = 0, stalled = 0, speed = 0, energy = 0;
            long samples = 0;
            int reversals = 0;
            double seconds = 0;

            for (int i = 0; i < 12; i++)
            {
                uint seed = 500 + (uint)i * 7919u;
                var match = Match.Duel(arena, AircraftSpec.CamelArcade, seed);
                var blue = new PilotAi(skill, seed + 11u);
                var red = new PilotAi(skill, seed + 23u);

                int lastSign = 0;

                for (int t = 0; t < 60 * 120 && match.Outcome == RoundOutcome.InProgress; t++)
                {
                    var b = match.Combatants[0];
                    var r = match.Combatants[1];
                    b.Input = blue.Fly(b, match.NearestEnemy(b), arena, FlightModel.FixedDt);
                    r.Input = red.Fly(r, match.NearestEnemy(r), arena, FlightModel.FixedDt);
                    match.Step();

                    var s = b.State;
                    if (!s.IsAlive) continue;

                    slew += Math.Abs(s.SlewRateRad);
                    alpha += Math.Abs(s.Alpha);
                    stalled += s.IsStalled ? 1 : 0;
                    speed += s.Airspeed;
                    energy += s.EnergyHeightM;
                    samples++;

                    // How often the nose changes which way it is sweeping. A pilot
                    // sawing at the stick shows up here and nowhere else.
                    int sign = Math.Sign(s.SlewRateRad);
                    if (sign != 0 && lastSign != 0 && sign != lastSign) reversals++;
                    if (sign != 0) lastSign = sign;
                }

                seconds += match.Elapsed;
            }

            double n = Math.Max(1, samples);
            output.WriteLine(
                $"{skill.Name,-8}  {Angles.ToDegrees(slew / n),12:F1}  {Angles.ToDegrees(alpha / n),12:F1}  " +
                $"{stalled / n,8:P0}  {speed / n * 3.6,8:F0}  {energy / n,5:F0}  " +
                $"{(seconds > 0 ? reversals / seconds * 60.0 : 0),13:F0}");
        }
    }
}

public class PilotDeathDiagnostics(ITestOutputHelper output)
{
    [Fact]
    public void PrintHowEachPairingDies()
    {
        foreach (var (a, b) in new[]
                 {
                     (AiSkill.Ace, AiSkill.Veteran),
                     (AiSkill.Veteran, AiSkill.Rookie),
                     (AiSkill.Ace, AiSkill.Rookie),
                 })
        {
            output.WriteLine($"--- {a.Name} (blue) vs {b.Name} (red) ---");
            output.WriteLine("    " + SelfPlay.DeathBreakdown(40, a, b, seed: 200));
            output.WriteLine("");
        }
    }
}
