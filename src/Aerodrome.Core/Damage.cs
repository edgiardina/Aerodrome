using System;

namespace Aerodrome.Core;

/// <summary>
/// What happens when a round connects.
///
/// No health bar. A round picks a component based on where along the airframe it
/// went through, and each component fails in its own way. The pilot learns their
/// aircraft is hurt from the smoke, the sound, and the way it stops answering,
/// not from a number going down.
/// </summary>
public static class Damage
{
    /// <summary>Seconds a fuel fire takes to finish the job.</summary>
    public const double BurnThroughSeconds = 7.0;

    /// <summary>Fuel system health below which a leak can catch.</summary>
    private const double FireThreshold = 0.62;

    /// <summary>Rounds through the tank hurt more than rounds through structure.</summary>
    private const double FuelDamageMultiplier = 1.4;

    /// <summary>
    /// Resolve one hit. <paramref name="alongSpine"/> is 0 at the tail and 1 at
    /// the nose, so the geometry decides what got hit rather than a flat roll.
    /// </summary>
    public static Component ApplyHit(
        AircraftState s, AircraftSpec spec, double alongSpine, ref Rng rng)
    {
        if (!s.IsAlive) return Component.None;

        s.HitsTaken++;
        Component component = PickComponent(alongSpine, ref rng);
        double damage = spec.RoundDamage;

        switch (component)
        {
            case Component.Pilot:
                // The one hit nothing recovers from.
                s.IsAlive = false;
                s.Death = DeathCause.Gunfire;
                break;

            case Component.Engine:
                s.EngineHealth = Math.Max(0.0, s.EngineHealth - damage);
                break;

            case Component.FuelTank:
                s.FuelSystemHealth = Math.Max(0.0, s.FuelSystemHealth - damage * FuelDamageMultiplier);
                if (s.FuelSystemHealth < FireThreshold && !s.OnFire)
                {
                    // Petrol onto a hot rotary. The emptier the tank reads, the more
                    // vapour there is, and the likelier the next round lights it.
                    double leak = (FireThreshold - s.FuelSystemHealth) / FireThreshold;
                    if (rng.Chance(leak * 0.85)) s.OnFire = true;
                }
                break;

            case Component.Wing:
                s.WingHealth = Math.Max(0.15, s.WingHealth - damage);
                break;

            case Component.Tail:
                s.TailHealth = Math.Max(0.10, s.TailHealth - damage * 1.2);
                break;

            case Component.Controls:
                s.ControlHealth = Math.Max(0.15, s.ControlHealth - damage);
                break;
        }

        s.LastHit = component;
        return component;
    }

    /// <summary>
    /// Where the round went through decides what it broke. The engine is up front,
    /// the pilot and tank are in the middle, the tail is at the back.
    /// </summary>
    private static Component PickComponent(double alongSpine, ref Rng rng)
    {
        double roll = rng.NextDouble();

        if (alongSpine > 0.72)          // nose and engine bay
            return roll switch
            {
                < 0.62 => Component.Engine,
                < 0.80 => Component.Wing,
                < 0.94 => Component.FuelTank,
                _ => Component.Pilot,
            };

        if (alongSpine > 0.40)          // cockpit, tank, wing roots
            return roll switch
            {
                < 0.34 => Component.Wing,
                < 0.60 => Component.FuelTank,
                < 0.82 => Component.Controls,
                < 0.94 => Component.Engine,
                _ => Component.Pilot,
            };

        return roll switch              // rear fuselage and tail
        {
            < 0.55 => Component.Tail,
            < 0.80 => Component.Controls,
            < 0.97 => Component.Wing,
            _ => Component.Pilot,
        };
    }

    /// <summary>
    /// Ongoing consequences: fire burning through, and wings that have been shot
    /// about failing under G they would once have taken.
    /// </summary>
    public static void Step(AircraftState s, AircraftSpec spec, double dt)
    {
        if (!s.IsAlive) return;

        if (s.OnFire)
        {
            s.FireTime += dt;
            // Fire eats the engine on the way through.
            s.EngineHealth = Math.Max(0.0, s.EngineHealth - 0.10 * dt);
            if (s.FireTime >= BurnThroughSeconds)
            {
                s.IsAlive = false;
                s.Death = DeathCause.Fire;
                return;
            }
        }

        // A damaged wing has a lower G limit than the spec says. Pull hard enough
        // on a shot-up airframe and it comes apart in the air.
        if (s.WingHealth < 0.95)
        {
            double allowed = spec.GLimit * (0.35 + 0.65 * s.WingHealth);
            if (Math.Abs(s.LoadFactor) > allowed)
            {
                s.IsAlive = false;
                s.Death = DeathCause.StructuralFailure;
            }
        }
    }
}
