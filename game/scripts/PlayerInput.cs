using System;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// Turns hardware into an AircraftInput. This is the only place that touches Godot
/// input, and it produces exactly the same struct the AI produces, so the player
/// has no capability the AI lacks and vice versa.
///
/// Edge-triggered actions are latched at render rate and consumed at sim rate, so a
/// roll press can never be dropped or counted twice when the two rates differ.
/// </summary>
public sealed partial class PlayerInput : Node
{
    /// <summary>Mouse distance from screen center, in pixels, below which the stick reads as centered.</summary>
    private const float MouseDeadzonePx = 36f;

    /// <summary>
    /// Deflection past which the stick stops meaning "point here" and starts meaning
    /// "keep pulling". As a fraction of half the screen height.
    /// </summary>
    private const float SustainedPullDeflection = 0.62f;

    /// <summary>How far ahead of the nose a sustained pull leads, in radians.</summary>
    private const double SustainedPullLeadRad = 0.7;

    private const double ThrottleRatePerSecond = 0.9;

    public bool ClassicMode { get; private set; }
    public double Throttle { get; private set; } = 1.0;

    /// <summary>Where the pilot is currently aiming the nose, or null for "hold".</summary>
    public double? AimHeading { get; private set; }

    /// <summary>True while the stick is pushed far enough to mean "keep pulling".</summary>
    public bool SustainedPull { get; private set; }

    /// <summary>The aircraft's current nose angle. Main keeps this fresh.</summary>
    public double NoseHeading { get; set; }

    /// <summary>Which way a pull turns the nose, +1 or -1. Main keeps this fresh.</summary>
    public int CanopySign { get; set; } = 1;

    /// <summary>Set while a modal panel owns the mouse, so aiming does not fight it.</summary>
    public bool SuspendMouseAim { get; set; }

    private bool _rollLatch;
    private bool _aileronLatch;
    private bool _flatTurnLatch;
    private bool _viewLatch;

    /// <summary>
    /// Which way the aircraft currently points, +1 for right. Main keeps this fresh.
    /// Classic mode needs it, because in the original pressing the arrow opposite
    /// your facing is what swaps your ends.
    /// </summary>
    public int FacingHint { get; set; } = 1;

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed(InputBindings.Roll)) _rollLatch = true;
        if (Input.IsActionJustPressed(InputBindings.AileronRoll)) _aileronLatch = true;
        if (Input.IsActionJustPressed(InputBindings.FlatTurn)) _flatTurnLatch = true;
        if (Input.IsActionJustPressed(InputBindings.ToggleView)) _viewLatch = true;
        if (Input.IsActionJustPressed(InputBindings.ClassicMode)) ClassicMode = !ClassicMode;

        // Faithful Classic behaviour: press the direction you are NOT facing and the
        // aircraft swaps ends, exactly as the numpad did in the original.
        if (ClassicMode)
        {
            if (FacingHint > 0 && Input.IsActionJustPressed(InputBindings.ClassicLeft)) _flatTurnLatch = true;
            if (FacingHint < 0 && Input.IsActionJustPressed(InputBindings.ClassicRight)) _flatTurnLatch = true;
        }

        double throttleDelta = 0;
        if (Input.IsActionPressed(InputBindings.ThrottleUp)) throttleDelta += 1;
        if (Input.IsActionPressed(InputBindings.ThrottleDown)) throttleDelta -= 1;
        Throttle = Math.Clamp(Throttle + throttleDelta * ThrottleRatePerSecond * delta, 0.0, 1.0);

        AimHeading = ClassicMode ? ReadClassicHeading() : ReadAnalogHeading();
    }

    /// <summary>Build one tick of input and consume the latched edges.</summary>
    public AircraftInput Poll()
    {
        var input = new AircraftInput
        {
            HeadingCommand = AimHeading,
            ThrottleCommand = Throttle,
            FireHeld = Input.IsActionPressed(InputBindings.Fire),
            RollPressed = _rollLatch,
            AileronRollPressed = _aileronLatch,
            FlatTurnPressed = _flatTurnLatch,
        };

        _rollLatch = false;
        _aileronLatch = false;
        _flatTurnLatch = false;
        return input;
    }

    public bool ConsumeViewToggle()
    {
        bool pressed = _viewLatch;
        _viewLatch = false;
        return pressed;
    }

    /// <summary>
    /// Analog heading-select. The command vector points from the screen center to the
    /// cursor, or wherever the left stick is pushed. Any angle, not eight of them.
    /// </summary>
    private double? ReadAnalogHeading()
    {
        SustainedPull = false;

        var stick = Input.GetVector("classic_left", "classic_right", "classic_down", "classic_up");
        var pad = new Vector2(
            Input.GetJoyAxis(0, JoyAxis.LeftX),
            -Input.GetJoyAxis(0, JoyAxis.LeftY));

        if (pad.Length() > 0.25f)
            return Aim(Math.Atan2(pad.Y, pad.X), pad.Length());

        var viewport = GetViewport();
        if (viewport is null || SuspendMouseAim) return null;

        Vector2 half = viewport.GetVisibleRect().Size * 0.5f;
        Vector2 offset = viewport.GetMousePosition() - half;

        if (offset.Length() < MouseDeadzonePx)
        {
            if (stick.Length() <= 0.1f) return null;
            return Aim(Math.Atan2(stick.Y, stick.X), stick.Length());
        }

        // Screen Y grows downward, world Y grows upward.
        float deflection = half.Y > 0 ? offset.Length() / half.Y : 0f;
        return Aim(Math.Atan2(-offset.Y, offset.X), deflection);
    }

    /// <summary>
    /// Turn a stick direction into a heading command.
    ///
    /// Near the middle this is pure heading-select, the original's control: the nose
    /// goes where you point and stops there, which is precise and easy to aim with.
    ///
    /// Pushed hard over it becomes "keep pulling". Without that, holding the cursor
    /// straight up climbs the nose to vertical and parks it, and the only way to
    /// fly a loop is to swirl the mouse in a circle. Holding a direction has to
    /// keep the turn coming, the way holding the numpad did in the original.
    /// </summary>
    private double? Aim(double direction, double deflection)
    {
        direction = Angles.Wrap0To2Pi(direction);
        if (deflection < SustainedPullDeflection) return direction;

        // Is the stick asking for a turn the aircraft is still working through?
        double error = Angles.Delta(NoseHeading, direction);

        // Once the nose has caught up, keep leading it round in the same direction
        // rather than letting it settle on the commanded heading.
        if (Math.Abs(error) > 0.35) return direction;

        SustainedPull = true;
        int turnSign = Math.Abs(error) > 1e-6 ? Math.Sign(error) : CanopySign;
        return Angles.Wrap0To2Pi(NoseHeading + turnSign * SustainedPullLeadRad);
    }

    /// <summary>
    /// The original's control, kept intact. Eight compass headings from the numpad.
    /// Numpad 7 climbs left, 3 dives right, and so on.
    /// </summary>
    private static double? ReadClassicHeading()
    {
        int x = 0, y = 0;
        if (Input.IsActionPressed(InputBindings.ClassicRight)) x += 1;
        if (Input.IsActionPressed(InputBindings.ClassicLeft)) x -= 1;
        if (Input.IsActionPressed(InputBindings.ClassicUp)) y += 1;
        if (Input.IsActionPressed(InputBindings.ClassicDown)) y -= 1;

        if (Input.IsKeyPressed(Key.Kp7)) { x -= 1; y += 1; }
        if (Input.IsKeyPressed(Key.Kp9)) { x += 1; y += 1; }
        if (Input.IsKeyPressed(Key.Kp1)) { x -= 1; y -= 1; }
        if (Input.IsKeyPressed(Key.Kp3)) { x += 1; y -= 1; }

        if (x == 0 && y == 0) return null;
        return Angles.Wrap0To2Pi(Math.Atan2(Math.Sign(y), Math.Sign(x)));
    }
}
