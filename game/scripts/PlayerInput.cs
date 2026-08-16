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
    private const double ThrottleRatePerSecond = 0.9;

    public bool ClassicMode { get; private set; }
    public double Throttle { get; private set; } = 1.0;

    /// <summary>Where the pilot is currently aiming the nose, or null for "hold".</summary>
    public double? AimHeading { get; private set; }

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
        var stick = Input.GetVector("classic_left", "classic_right", "classic_down", "classic_up");
        var pad = new Vector2(
            Input.GetJoyAxis(0, JoyAxis.LeftX),
            -Input.GetJoyAxis(0, JoyAxis.LeftY));

        if (pad.Length() > 0.25f)
            return Angles.Wrap0To2Pi(Math.Atan2(pad.Y, pad.X));

        var viewport = GetViewport();
        if (viewport is null) return null;

        Vector2 center = viewport.GetVisibleRect().Size * 0.5f;
        Vector2 offset = viewport.GetMousePosition() - center;

        if (offset.Length() < MouseDeadzonePx)
            return stick.Length() > 0.1f ? Angles.Wrap0To2Pi(Math.Atan2(stick.Y, stick.X)) : null;

        // Screen Y grows downward, world Y grows upward.
        return Angles.Wrap0To2Pi(Math.Atan2(-offset.Y, offset.X));
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
