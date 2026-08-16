using System;
using Aerodrome.Core;

namespace Aerodrome.Game;

/// <summary>
/// A scripted pilot used only by capture mode. It flies a fixed routine so the
/// screenshots always show the same maneuvers, which makes a regression easy to
/// spot by eye.
///
/// Routine: level, flat turn, level, Immelmann, level. That is two of the three
/// reversals back to back, so one run shows the contrast between them.
/// </summary>
public sealed class DemoPilot
{
    private enum Step { Level, FlatTurn, Cruise, HalfLoop, RollUpright, Done }

    public double Time { get; private set; }
    public string Phase => _step switch
    {
        Step.Level => "level",
        Step.FlatTurn => "flat turn",
        Step.Cruise => "cruise",
        Step.HalfLoop => "half loop",
        Step.RollUpright => "roll",
        _ => "recovered",
    };

    private Step _step = Step.Level;
    private double _stepTime;
    private double _sweep;
    private double _lastTheta;
    private double _pullLead;
    private bool _fired;

    public AircraftInput Fly(SimAircraft aircraft, Arena arena)
    {
        var s = aircraft.State;
        if (!s.IsAlive) return AircraftInput.Neutral;

        double dt = FlightModel.FixedDt;
        Time += dt;
        _stepTime += dt;
        _sweep += Angles.Delta(_lastTheta, s.Theta);
        _lastTheta = s.Theta;

        switch (_step)
        {
            case Step.Level:
                if (_stepTime >= 1.6) Advance(Step.FlatTurn);
                return Hold(s);

            case Step.FlatTurn:
                if (!_fired) { _fired = true; return new AircraftInput { ThrottleCommand = 1.0, FlatTurnPressed = true }; }
                if (!s.IsFlatTurning) Advance(Step.Cruise);
                return new AircraftInput { ThrottleCommand = 1.0 };

            case Step.Cruise:
                if (_stepTime >= 1.7) { Advance(Step.HalfLoop); _sweep = 0; }
                return Hold(s);

            case Step.HalfLoop:
                if (Math.Abs(_sweep) >= Math.PI) { Advance(Step.RollUpright); return Hold(s); }
                _pullLead += 0.9 * dt;
                double command = Angles.Wrap0To2Pi(s.Theta + s.CanopySign * Math.Min(_pullLead, 0.6));
                _pullLead = Math.Max(0, _pullLead - Math.Abs(s.SlewRateRad) * dt);
                return new AircraftInput { ThrottleCommand = 1.0, HeadingCommand = command };

            case Step.RollUpright:
                if (!_fired) { _fired = true; return new AircraftInput { ThrottleCommand = 1.0, RollPressed = true }; }
                if (s.RollRemaining <= 0) Advance(Step.Done);
                return Hold(s);

            default:
                return Hold(s);
        }
    }

    private void Advance(Step next)
    {
        _step = next;
        _stepTime = 0;
        _fired = false;
    }

    /// <summary>Fly level in whichever direction the nose currently points.</summary>
    private static AircraftInput Hold(AircraftState s) => new()
    {
        ThrottleCommand = 1.0,
        HeadingCommand = Math.Cos(s.Theta) >= 0 ? 0.0 : Math.PI,
    };
}
