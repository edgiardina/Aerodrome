using System;
using Aerodrome.Core;

namespace Aerodrome.Game;

/// <summary>
/// A placeholder opponent, NOT the M2 combat AI.
///
/// It exists so there is something in the sky to frame the camera on, to put on
/// the minimap, and to raise an off-screen marker. It flies waypoint to waypoint
/// and rolls upright when it finds itself inverted.
///
/// It goes through AircraftInput like everything else, so it has no capability the
/// player lacks. When the real AI lands it moves into Aerodrome.Core and gets
/// tested headless.
/// </summary>
public sealed class DronePilot
{
    private readonly Random _rng;
    private Vec2 _waypoint;
    private double _rollCooldown;
    private bool _initialised;

    public DronePilot(int seed) => _rng = new Random(seed);

    public AircraftInput Fly(SimAircraft drone, Arena arena)
    {
        var s = drone.State;
        if (!s.IsAlive) return AircraftInput.Neutral;

        if (!_initialised) { _waypoint = PickWaypoint(arena); _initialised = true; }

        _rollCooldown = Math.Max(0, _rollCooldown - FlightModel.FixedDt);

        var toWaypoint = _waypoint - s.Position;
        if (toWaypoint.Length < 180) _waypoint = PickWaypoint(arena);

        double desired = toWaypoint.Angle;

        // Stay off the floor and the ceiling. Both cost the round.
        if (s.Position.Y < 260) desired = Angles.ToRadians(45) * Math.Sign(Math.Cos(s.Theta) >= 0 ? 1 : -1);
        else if (s.Position.Y > arena.CeilingM - 260) desired = -Angles.ToRadians(30);

        // If the fast way to the target is a push, roll upright first. Exactly the
        // decision the player has to make.
        bool rollNow = false;
        if (_rollCooldown <= 0 && s.RollRemaining <= 0)
        {
            double error = Angles.Delta(s.Theta, desired);
            bool wouldBeAPush = Math.Abs(error) > 0.25 && Math.Sign(error) != s.CanopySign;
            if (wouldBeAPush || (s.IsInverted && s.InvertedTime > 1.2))
            {
                rollNow = true;
                _rollCooldown = 1.1;
            }
        }

        return new AircraftInput
        {
            HeadingCommand = desired,
            ThrottleCommand = s.Airspeed > 74 ? 0.62 : 1.0,
            RollPressed = rollNow,
        };
    }

    private Vec2 PickWaypoint(Arena arena) => new(
        _rng.NextDouble() * arena.WidthM * 0.82 + arena.WidthM * 0.09,
        _rng.NextDouble() * (arena.CeilingM * 0.55) + arena.CeilingM * 0.22);
}
