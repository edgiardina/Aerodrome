using System;
using System.Collections.Generic;

namespace Aerodrome.Core;

public struct Bullet
{
    public Vec2 Position;
    public Vec2 Velocity;
    public double LifeRemaining;
    public int OwnerTeam;
    public int OwnerIndex;
    public bool IsTracer;
    public bool Active;
}

/// <summary>What happened when a round connected. Presentation reads these.</summary>
public readonly record struct HitEvent(
    Vec2 Position, int VictimIndex, int ShooterIndex, Component Component, bool Fatal);

/// <summary>
/// Every round in the air. A fixed pool, reused forever, so the sim loop never
/// allocates.
/// </summary>
public sealed class BulletField
{
    /// <summary>Rounds live this long. Well past any useful range.</summary>
    public const double LifetimeSeconds = 2.2;

    /// <summary>Drag on a rifle round, per second per (m/s). Slows it over range.</summary>
    private const double BulletDrag = 0.12;

    private readonly Bullet[] _bullets;
    private readonly List<HitEvent> _hits = new(32);
    private int _cursor;

    public BulletField(int capacity = 512) => _bullets = new Bullet[capacity];

    public ReadOnlySpan<Bullet> Bullets => _bullets;
    public int Capacity => _bullets.Length;

    /// <summary>Hits from the most recent Step. Cleared at the start of each one.</summary>
    public IReadOnlyList<HitEvent> Hits => _hits;

    public int ActiveCount
    {
        get
        {
            int n = 0;
            foreach (var b in _bullets) if (b.Active) n++;
            return n;
        }
    }

    public void Clear()
    {
        Array.Clear(_bullets);
        _hits.Clear();
        _cursor = 0;
    }

    public void Spawn(Vec2 position, Vec2 velocity, int ownerTeam, int ownerIndex, bool tracer)
    {
        // Round-robin over the pool. If it wraps, the oldest round is dropped, which
        // is correct: it is the one furthest away and least likely to matter.
        for (int i = 0; i < _bullets.Length; i++)
        {
            int index = (_cursor + i) % _bullets.Length;
            if (_bullets[index].Active) continue;

            _bullets[index] = new Bullet
            {
                Position = position,
                Velocity = velocity,
                LifeRemaining = LifetimeSeconds,
                OwnerTeam = ownerTeam,
                OwnerIndex = ownerIndex,
                IsTracer = tracer,
                Active = true,
            };
            _cursor = (index + 1) % _bullets.Length;
            return;
        }

        // Pool full. Overwrite at the cursor rather than silently dropping the shot.
        _bullets[_cursor] = new Bullet
        {
            Position = position,
            Velocity = velocity,
            LifeRemaining = LifetimeSeconds,
            OwnerTeam = ownerTeam,
            OwnerIndex = ownerIndex,
            IsTracer = tracer,
            Active = true,
        };
        _cursor = (_cursor + 1) % _bullets.Length;
    }

    /// <summary>
    /// Fly every round and resolve hits.
    ///
    /// Collision is swept, not point sampled. A round covers 6.2 m in a 120 Hz tick
    /// and the hit capsule is about 3 m across, so a point test would miss most of
    /// the time. Each round tests the segment it travelled against the segment
    /// running nose to tail on each target.
    /// </summary>
    public void Step(IReadOnlyList<Combatant> combatants, Arena arena, double dt, ref Rng rng)
    {
        _hits.Clear();

        for (int i = 0; i < _bullets.Length; i++)
        {
            ref Bullet b = ref _bullets[i];
            if (!b.Active) continue;

            b.LifeRemaining -= dt;
            if (b.LifeRemaining <= 0) { b.Active = false; continue; }

            Vec2 from = b.Position;

            // Gravity and drag. Over 300 m a round drops about a meter and sheds a
            // fifth of its speed, so long shots need lead and a little elevation.
            double speed = b.Velocity.Length;
            b.Velocity += new Vec2(0, -Atmosphere.Gravity) * dt;
            b.Velocity -= b.Velocity.Normalized * (BulletDrag * speed * dt);
            b.Position += b.Velocity * dt;

            if (b.Position.Y <= 0 || !arena.IsInsideWalls(b.Position) || b.Position.Y > arena.CeilingM)
            {
                b.Active = false;
                continue;
            }

            ResolveHits(ref b, from, combatants, ref rng);
        }
    }

    private void ResolveHits(ref Bullet b, Vec2 from, IReadOnlyList<Combatant> combatants, ref Rng rng)
    {
        double bestT = double.MaxValue;
        int bestVictim = -1;

        for (int v = 0; v < combatants.Count; v++)
        {
            var target = combatants[v];
            if (!target.IsAlive) continue;
            if (target.Team == b.OwnerTeam) continue;      // no friendly fire, for now

            target.HitSpine(out Vec2 nose, out Vec2 tail);
            double distance = Geometry.SegmentDistance(from, b.Position, tail, nose, out double t);

            if (distance > target.Spec.HitRadiusM) continue;
            if (t >= bestT) continue;

            bestT = t;
            bestVictim = v;
        }

        if (bestVictim < 0) return;

        var victim = combatants[bestVictim];
        Vec2 impact = from + (b.Position - from) * bestT;

        victim.HitSpine(out Vec2 vNose, out Vec2 vTail);
        double along = Geometry.AlongSpine(impact, vTail, vNose);

        Component component = Damage.ApplyHit(victim.State, victim.Spec, along, ref rng);
        bool fatal = !victim.State.IsAlive;

        if (b.OwnerIndex >= 0 && b.OwnerIndex < combatants.Count)
        {
            combatants[b.OwnerIndex].HitsScored++;
            if (fatal) combatants[b.OwnerIndex].Kills++;
        }

        _hits.Add(new HitEvent(impact, bestVictim, b.OwnerIndex, component, fatal));
        b.Active = false;
    }
}
