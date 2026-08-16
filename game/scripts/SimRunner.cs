using System.Collections.Generic;
using Aerodrome.Core;

namespace Aerodrome.Game;

/// <summary>
/// Owns the match. Steps Aerodrome.Core at exactly one fixed tick per physics frame
/// and hands the renderer an interpolation factor.
///
/// Godot's physics tick rate is set to match FlightModel.TickRate, so this never
/// runs its own accumulator. Engine.GetPhysicsInterpolationFraction() then gives the
/// exact blend the renderer needs.
/// </summary>
public sealed class SimRunner
{
    public Arena Arena { get; }
    public List<SimAircraft> Aircraft { get; } = new();
    public SimAircraft Player => Aircraft[0];
    public long Tick { get; private set; }

    /// <summary>Wall time the last sim step took, in milliseconds. For the debug overlay.</summary>
    public double LastStepMs { get; private set; }

    public SimRunner(Arena arena) => Arena = arena;

    public SimAircraft Add(SimAircraft aircraft)
    {
        aircraft.PrimeRenderState();
        Aircraft.Add(aircraft);
        return aircraft;
    }

    public void Step()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        for (int i = 0; i < Aircraft.Count; i++)
        {
            var a = Aircraft[i];
            FlightModel.Step(a.State, a.Spec, a.Input, Arena);
            a.CaptureRenderState(FlightModel.FixedDt);
        }

        Tick++;
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
