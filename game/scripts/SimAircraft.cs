using System;
using Aerodrome.Core;

namespace Aerodrome.Game;

public enum Team { Player, Enemy }

/// <summary>
/// One aircraft in the running match: its spec, its live sim state, and the two
/// render snapshots the view interpolates between.
/// </summary>
public sealed class SimAircraft
{
    public required AircraftSpec Spec { get; init; }
    public required AircraftState State { get; set; }
    public Team Team { get; init; } = Team.Enemy;
    public string Callsign { get; init; } = "unknown";

    public AircraftInput Input;

    private RenderState _previous;
    private RenderState _current;
    private double _propAngle;

    public void PrimeRenderState()
    {
        _current = Capture();
        _previous = _current;
    }

    /// <summary>Call once per sim tick, after the flight model has stepped.</summary>
    public void CaptureRenderState(double dt)
    {
        // The propeller is visual only, so it lives here and not in the sim.
        double rpmFraction = State.Throttle * (1.0 - State.FuelStarvation) * State.EngineHealth;
        _propAngle = Angles.Wrap0To2Pi(_propAngle + (12.0 + 90.0 * rpmFraction) * dt);

        _previous = _current;
        _current = Capture();
    }

    public RenderState Interpolated(double alpha) => RenderState.Lerp(_previous, _current, alpha);

    private RenderState Capture()
    {
        var s = State;
        return new RenderState
        {
            X = s.Position.X,
            Y = s.Position.Y,
            // Bank and pitch are transient offsets on top of the real orientation.
            Theta = Angles.Wrap0To2Pi(s.Theta + s.FlatTurnPitch(Spec) * s.CanopySign),
            Roll = Angles.Wrap0To2Pi(s.RollAngle + s.FlatTurnBank(Spec)),
            Yaw = s.YawAngle,
            PropAngle = _propAngle,
            Aileron = AileronDemand(s),
            Elevator = ElevatorDemand(s, Spec),
            Rudder = RudderDemand(s),
            Airspeed = s.Airspeed,
            IsAlive = s.IsAlive,
        };
    }

    /// <summary>Full deflection through a roll, and reversing through a flat turn.</summary>
    private static double AileronDemand(AircraftState s)
    {
        if (s.IsFlatTurning) return s.FlatTurnAileron;
        return s.RollRemaining > 0 ? 1.0 : 0.0;
    }

    /// <summary>Angle of attack is a direct read on how hard the pilot is pulling.</summary>
    private static double ElevatorDemand(AircraftState s, AircraftSpec spec)
    {
        if (s.IsFlatTurning) return 0.55 * Math.Sin(s.FlatTurnProgress * Math.PI);
        return Math.Clamp(s.Alpha / spec.StallAlphaRad, -1.0, 1.0);
    }

    /// <summary>Boot full of rudder to hold the flat turn round.</summary>
    private static double RudderDemand(AircraftState s)
    {
        if (!s.IsFlatTurning) return 0.0;
        return Math.Clamp(Math.Sin(s.FlatTurnProgress * Math.PI) * 1.9, 0.0, 1.0);
    }
}
