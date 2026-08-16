using System.Collections.Generic;
using Aerodrome.Core;

namespace Aerodrome.Game;

/// <summary>
/// Owns the round. Steps Aerodrome.Core exactly once per physics frame and hands
/// the renderer an interpolation factor.
///
/// Godot's physics tick rate is set to match FlightModel.TickRate, so this never
/// runs its own accumulator. Engine.GetPhysicsInterpolationFraction() then gives
/// the exact blend the renderer needs.
/// </summary>
public sealed class SimRunner
{
    public Match Match { get; }
    public Arena Arena => Match.Arena;
    public List<SimAircraft> Aircraft { get; } = new();
    public SimAircraft Player => Aircraft[0];
    public BulletField Bullets => Match.Bullets;
    public RoundOutcome Outcome => Match.Outcome;
    public long Tick => Match.Tick;

    /// <summary>Wall time the last sim step took, in milliseconds. For the overlay.</summary>
    public double LastStepMs { get; private set; }

    public SimRunner(Arena arena, uint seed) => Match = new Match(arena, seed);

    public SimAircraft Add(SimAircraft aircraft)
    {
        Match.Add(aircraft.Combatant);
        Aircraft.Add(aircraft);
        return aircraft;
    }

    public void Step()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        Match.Step();
        foreach (var a in Aircraft) a.CaptureRenderState(FlightModel.FixedDt);

        LastStepMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                     * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>Nearest living aircraft on another team, or null.</summary>
    public SimAircraft? NearestOpponent(SimAircraft of)
    {
        SimAircraft? best = null;
        double bestSq = double.MaxValue;

        foreach (var other in Aircraft)
        {
            if (other == of || other.Team == of.Team || !other.State.IsAlive) continue;
            double dSq = (other.State.Position - of.State.Position).LengthSquared;
            if (dSq < bestSq) { bestSq = dSq; best = other; }
        }
        return best;
    }
}
