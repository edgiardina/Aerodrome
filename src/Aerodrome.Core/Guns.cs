using System;

namespace Aerodrome.Core;

/// <summary>
/// Twin Vickers, fixed forward, synchronised through the propeller arc.
///
/// Three things make them interesting rather than a fire button: the ammo is
/// finite and there is no reload, they jam when you lean on them, and during a
/// flat turn they cannot bear on anything at all.
/// </summary>
public static class Guns
{
    public static void Step(Combatant c, Arena arena, BulletField bullets, int index, double dt, ref Rng rng)
    {
        var s = c.State;
        var spec = c.Spec;
        var input = c.Input;

        StepJam(s, spec, input, dt);
        StepHeat(s, spec, input, dt);

        s.FireCooldown = Math.Max(0.0, s.FireCooldown - dt);

        if (!input.FireHeld) return;
        if (!s.GunsCanBear) return;          // jammed, dead, or nose-on into the screen
        if (s.Ammo <= 0) return;
        if (s.FireCooldown > 0) return;

        Fire(c, bullets, index, ref rng);
        s.FireCooldown = 1.0 / spec.RoundsPerSecond;
    }

    private static void Fire(Combatant c, BulletField bullets, int index, ref Rng rng)
    {
        var s = c.State;
        var spec = c.Spec;

        // Jam check happens on the round, so a long burst is what breaks the guns.
        if (rng.Chance(JamProbability(spec, s.GunHeat)))
        {
            s.GunJammed = true;
            return;
        }

        s.Ammo--;

        double spread = rng.NextSigned() * spec.GunDispersionRad;
        double heading = s.Theta + spread;

        // Muzzle sits at the nose of the drawn airframe, so tracers leave from
        // where the player can see the guns.
        Vec2 muzzle = s.Position + Vec2.FromAngle(s.Theta, spec.HitHalfLengthM * 0.95);

        // Rounds inherit the aircraft's velocity. At 60 m/s that is a real effect
        // on lead, and leaving it out makes deflection shooting feel wrong.
        Vec2 velocity = Vec2.FromAngle(heading, spec.MuzzleVelocity) + s.Velocity;

        bool tracer = spec.TracerEvery > 0 && s.Ammo % spec.TracerEvery == 0;
        bullets.Spawn(muzzle, velocity, c.Team, index, tracer);
    }

    /// <summary>
    /// Chance one round jams the guns.
    ///
    /// Deliberately not linear in heat. Cold guns must be reliable, or a two-round
    /// snap shot can lose you the fight to a dice roll and the player learns nothing
    /// from it. Below a quarter heat the chance is zero, and above that it climbs
    /// as a square, so the punishment lands squarely on holding the trigger down.
    /// </summary>
    public static double JamProbability(AircraftSpec spec, double heat)
    {
        const double freeHeat = 0.25;
        if (heat <= freeHeat) return 0.0;

        double over = (heat - freeHeat) / (1.0 - freeHeat);
        return spec.JamChanceAtFullHeat * over * over;
    }

    private static void StepHeat(AircraftState s, AircraftSpec spec, AircraftInput input, double dt)
    {
        bool firing = input.FireHeld && s.GunsCanBear && s.Ammo > 0;
        s.GunHeat = Angles.Clamp(
            s.GunHeat + (firing ? spec.GunHeatPerSecond : -spec.GunCoolPerSecond) * dt,
            0.0, 1.0);
    }

    private static void StepJam(AircraftState s, AircraftSpec spec, AircraftInput input, double dt)
    {
        if (!s.GunJammed) { s.JamClearProgress = 0.0; return; }

        // Clearing a jam means taking a hand off the stick in the middle of a fight.
        if (!input.ClearJamPressed) { s.JamClearProgress = 0.0; return; }

        s.JamClearProgress += dt;
        if (s.JamClearProgress >= spec.JamClearSeconds)
        {
            s.GunJammed = false;
            s.JamClearProgress = 0.0;
            s.GunHeat *= 0.4;
        }
    }

    /// <summary>
    /// Where to aim to hit a moving target with a round that takes time to arrive.
    ///
    /// Iterates rather than solving the quadratic, because the round also slows and
    /// drops. Three passes is plenty at these ranges. Returns false if there is no
    /// solution worth taking.
    /// </summary>
    public static bool Intercept(
        Vec2 shooter, Vec2 shooterVelocity, double muzzleVelocity,
        Vec2 target, Vec2 targetVelocity, out double heading, out double timeOfFlight)
    {
        heading = 0;
        timeOfFlight = 0;

        Vec2 aim = target;
        for (int i = 0; i < 3; i++)
        {
            Vec2 delta = aim - shooter;
            double range = delta.Length;
            if (range < 1e-3) return false;

            // Round speed relative to the world, including what the aircraft gave it.
            double closing = muzzleVelocity + Vec2.Dot(shooterVelocity, delta.Normalized);
            if (closing < 1e-3) return false;

            timeOfFlight = range / closing;
            aim = target + targetVelocity * timeOfFlight;

            // Hold off for the drop over the flight.
            aim += new Vec2(0, 0.5 * Atmosphere.Gravity * timeOfFlight * timeOfFlight);
        }

        heading = (aim - shooter).Angle;
        return true;
    }
}
