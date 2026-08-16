using System;
using System.Collections.Generic;

namespace Aerodrome.Core;

public enum RoundOutcome { InProgress, TeamZeroWins, TeamOneWins, Draw }

/// <summary>
/// One round. Owns the aircraft, the rounds in the air, and the win condition.
///
/// Deterministic: seed it the same and feed it the same inputs and it plays out
/// identically every time. That is what lets the self-play harness run thousands
/// of fights headless to check the flight model has not gone lopsided.
/// </summary>
public sealed class Match
{
    public Arena Arena { get; }
    public List<Combatant> Combatants { get; } = new();
    public BulletField Bullets { get; }
    public RoundOutcome Outcome { get; private set; } = RoundOutcome.InProgress;

    public long Tick { get; private set; }
    public double Elapsed { get; private set; }

    /// <summary>A round that goes this long with no result is a draw.</summary>
    public double TimeLimitSeconds { get; init; } = 240.0;

    private Rng _rng;

    public Match(Arena arena, uint seed, int bulletCapacity = 512)
    {
        Arena = arena;
        Bullets = new BulletField(bulletCapacity);
        _rng = new Rng(seed);
    }

    public Combatant Add(Combatant c)
    {
        Combatants.Add(c);
        return c;
    }

    /// <summary>Nearest living enemy of the given combatant, or null.</summary>
    public Combatant? NearestEnemy(Combatant of)
    {
        Combatant? best = null;
        double bestSq = double.MaxValue;

        foreach (var other in Combatants)
        {
            if (other == of || other.Team == of.Team || !other.IsAlive) continue;
            double dSq = (other.State.Position - of.State.Position).LengthSquared;
            if (dSq < bestSq) { bestSq = dSq; best = other; }
        }
        return best;
    }

    /// <summary>
    /// Advance one fixed tick. Inputs must already be set on each combatant.
    ///
    /// Order matters. Fly first, then shoot from where you ended up, then move the
    /// rounds already in the air, then let fire and stress finish anyone off.
    /// </summary>
    public void Step(double dt = FlightModel.FixedDt)
    {
        if (Outcome != RoundOutcome.InProgress) return;

        for (int i = 0; i < Combatants.Count; i++)
        {
            var c = Combatants[i];
            FlightModel.Step(c.State, c.Spec, c.Input, Arena, dt);
        }

        for (int i = 0; i < Combatants.Count; i++)
            Guns.Step(Combatants[i], Arena, Bullets, i, dt, ref _rng);

        Bullets.Step(Combatants, Arena, dt, ref _rng);

        for (int i = 0; i < Combatants.Count; i++)
            Damage.Step(Combatants[i].State, Combatants[i].Spec, dt);

        Tick++;
        Elapsed += dt;
        Evaluate();
    }

    private void Evaluate()
    {
        bool zeroAlive = false, oneAlive = false;
        foreach (var c in Combatants)
        {
            if (!c.IsAlive) continue;
            if (c.Team == 0) zeroAlive = true; else oneAlive = true;
        }

        if (!zeroAlive && !oneAlive) Outcome = RoundOutcome.Draw;
        else if (!oneAlive) Outcome = RoundOutcome.TeamZeroWins;
        else if (!zeroAlive) Outcome = RoundOutcome.TeamOneWins;
        else if (Elapsed >= TimeLimitSeconds) Outcome = RoundOutcome.Draw;
    }

    /// <summary>
    /// Build the standard head-to-head: two aircraft, facing each other, level,
    /// same speed, same height. Any asymmetry in the result is the flight model's
    /// or the AI's, not the setup's.
    /// </summary>
    public static Match Duel(Arena arena, AircraftSpec spec, uint seed, double speed = 62.0)
    {
        var match = new Match(arena, seed);
        double altitude = arena.CeilingM * 0.45;
        double margin = arena.WidthM * 0.25;

        match.Add(new Combatant
        {
            Spec = spec,
            Team = 0,
            Callsign = "blue",
            State = AircraftState.Spawn(spec, new Vec2(margin, altitude), 0.0, speed),
        });

        match.Add(new Combatant
        {
            Spec = spec,
            Team = 1,
            Callsign = "red",
            State = AircraftState.Spawn(spec, new Vec2(arena.WidthM - margin, altitude), Math.PI, speed),
        });

        return match;
    }
}
