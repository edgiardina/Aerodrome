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
        _current = RenderState.Capture(State, _propAngle);
        _previous = _current;
    }

    /// <summary>Call once per sim tick, after the flight model has stepped.</summary>
    public void CaptureRenderState(double dt)
    {
        // The propeller is visual only, so it lives here and not in the sim.
        double rpmFraction = State.Throttle * (1.0 - State.FuelStarvation) * State.EngineHealth;
        _propAngle = Angles.Wrap0To2Pi(_propAngle + (12.0 + 90.0 * rpmFraction) * dt);

        _previous = _current;
        _current = RenderState.Capture(State, _propAngle);
    }

    public RenderState Interpolated(double alpha) => RenderState.Lerp(_previous, _current, alpha);
}
