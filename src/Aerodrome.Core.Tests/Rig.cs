using Aerodrome.Core;

namespace Aerodrome.Core.Tests;

/// <summary>
/// A headless test pilot. It drives one aircraft through the same input surface the
/// player and the AI use, so a test can never cheat by setting orientation directly.
/// </summary>
internal sealed class Rig
{
    public AircraftSpec Spec { get; }
    public Arena Arena { get; }
    public AircraftState State { get; private set; }
    public double Time { get; private set; }

    /// <summary>Signed nose travel since the last ResetSweep, in radians.</summary>
    public double Sweep { get; private set; }

    private double _commandHeading;

    public Rig(AircraftSpec? spec = null, Arena? arena = null)
    {
        Spec = spec ?? AircraftSpec.CamelArcade;
        Arena = arena ?? Arena.TestRange;
        State = AircraftState.Spawn(Spec, new Vec2(3000, 1500), 0.0, 55.0);
        _commandHeading = State.Theta;
    }

    public Rig Spawn(double x, double y, double heading, double speed,
                     bool inverted = false, double throttle = 1.0)
    {
        State = AircraftState.Spawn(Spec, new Vec2(x, y), heading, speed, inverted, throttle);
        _commandHeading = State.Theta;
        Time = 0;
        Sweep = 0;
        return this;
    }

    public void ResetSweep() => Sweep = 0;

    public void Tick(AircraftInput input)
    {
        double before = State.Theta;
        FlightModel.Step(State, Spec, input, Arena);
        Sweep += Angles.Delta(before, State.Theta);
        Time += FlightModel.FixedDt;
    }

    /// <summary>Hold the current heading for a span of seconds.</summary>
    public void Coast(double seconds, double throttle = 1.0)
    {
        int ticks = (int)Math.Round(seconds * FlightModel.TickRate);
        for (int i = 0; i < ticks && State.IsAlive; i++)
            Tick(new AircraftInput { ThrottleCommand = throttle, HeadingCommand = _commandHeading });
    }

    /// <summary>Let go of the stick entirely. The aircraft weathercocks and falls.</summary>
    public void Drift(double seconds, double throttle = 1.0)
    {
        int ticks = (int)Math.Round(seconds * FlightModel.TickRate);
        for (int i = 0; i < ticks && State.IsAlive; i++)
            Tick(new AircraftInput { ThrottleCommand = throttle });
    }

    /// <summary>
    /// Sweep the command vector in the pull direction at a chosen rate until the nose
    /// has travelled <paramref name="sweepRad"/>, or until the time budget runs out.
    /// This is what a player does with a mouse: they move the aim point, and the nose
    /// chases it as fast as the airframe allows.
    /// </summary>
    public bool Pull(double sweepRad, double commandRateRad = 0.9, double timeoutS = 30.0,
                     double throttle = 1.0)
    {
        ResetSweep();
        double target = 0;
        int maxTicks = (int)(timeoutS * FlightModel.TickRate);

        for (int i = 0; i < maxTicks && State.IsAlive; i++)
        {
            if (Math.Abs(Sweep) >= sweepRad) return true;

            // Advance the aim point in whichever direction is currently a pull.
            target += commandRateRad * FlightModel.FixedDt;
            _commandHeading = Angles.Wrap0To2Pi(State.Theta + State.CanopySign * Math.Min(target, 0.6));
            Tick(new AircraftInput { ThrottleCommand = throttle, HeadingCommand = _commandHeading });
            target = Math.Max(0, target - Math.Abs(State.SlewRateRad) * FlightModel.FixedDt);
        }
        return Math.Abs(Sweep) >= sweepRad;
    }

    /// <summary>Press the roll key once and fly on until the roll finishes.</summary>
    public void HalfRoll(double throttle = 1.0)
    {
        _commandHeading = State.Theta;
        Tick(new AircraftInput { ThrottleCommand = throttle, RollPressed = true, HeadingCommand = _commandHeading });
        int guard = (int)(3.0 * FlightModel.TickRate);
        while (State.RollRemaining > 0 && State.IsAlive && guard-- > 0)
        {
            _commandHeading = State.Theta;
            Tick(new AircraftInput { ThrottleCommand = throttle, HeadingCommand = _commandHeading });
        }
    }

    public double Altitude => State.Position.Y;
    public double Speed => State.Airspeed;
    public double EnergyHeight => State.EnergyHeightM;
    public bool FacingRight => Math.Cos(State.Theta) >= 0;
    public double HeadingDeg => Angles.ToDegrees(State.Theta);
}
