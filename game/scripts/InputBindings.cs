using Godot;

namespace Aerodrome.Game;

/// <summary>
/// Registers every game action from one table. Nothing reads a raw key anywhere
/// else, so a remap screen later only has to rewrite this table and save it.
///
/// The four schemes from the plan all live here at once. Mouse and keyboard,
/// gamepad, joystick, and the numpad "Classic" layout that matches the original
/// manual one to one.
/// </summary>
public static class InputBindings
{
    public const string ThrottleUp = "throttle_up";
    public const string ThrottleDown = "throttle_down";
    public const string Fire = "fire";
    public const string Roll = "roll";
    public const string AileronRoll = "aileron_roll";
    public const string FlatTurn = "flat_turn";
    public const string ToggleView = "toggle_view";
    public const string ClassicMode = "classic_mode";

    // Classic 8-way. The original used the numpad and so do we.
    public const string ClassicUp = "classic_up";
    public const string ClassicDown = "classic_down";
    public const string ClassicLeft = "classic_left";
    public const string ClassicRight = "classic_right";

    public const string CycleScheme = "cycle_scheme";
    public const string DebugOverlay = "debug_overlay";
    public const string Restart = "restart";

    public static void Register()
    {
        // --- Throttle -------------------------------------------------------
        Action(ThrottleUp, Key.W, Key.KpAdd, Key.Equal);
        Action(ThrottleDown, Key.S, Key.KpSubtract, Key.Minus);
        Joy(ThrottleUp, JoyAxis.TriggerRight, 1.0f);
        Joy(ThrottleDown, JoyAxis.TriggerLeft, 1.0f);

        // --- Guns -----------------------------------------------------------
        Action(Fire, Key.Space);
        Mouse(Fire, MouseButton.Left);
        Pad(Fire, JoyButton.RightShoulder);

        // --- Roll. Prominent on every scheme: the pilot presses it mid-maneuver
        //     under fire, so it never goes on an awkward key.
        Action(Roll, Key.Insert, Key.Shift);
        Mouse(Roll, MouseButton.Right);
        Pad(Roll, JoyButton.B);

        Action(AileronRoll, Key.Q);
        Pad(AileronRoll, JoyButton.X);

        // --- Flat turn. Swap ends without giving up altitude. Needs to be as easy
        //     to reach as the roll, because it is the panic button when you get roped.
        Action(FlatTurn, Key.A);
        Mouse(FlatTurn, MouseButton.Middle);
        Pad(FlatTurn, JoyButton.A);

        // --- View -----------------------------------------------------------
        Action(ToggleView, Key.V);
        Pad(ToggleView, JoyButton.LeftShoulder);

        // --- Classic 8-way numpad -------------------------------------------
        Action(ClassicUp, Key.Kp8);
        Action(ClassicDown, Key.Kp2);
        Action(ClassicLeft, Key.Kp4);
        Action(ClassicRight, Key.Kp6);
        Action(ClassicMode, Key.C);

        // --- Debug ----------------------------------------------------------
        Action(CycleScheme, Key.F2);
        Action(DebugOverlay, Key.F3);
        Action(Restart, Key.R);
    }

    private static void Action(string name, params Key[] keys)
    {
        Ensure(name);
        foreach (var key in keys)
            InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = key });
    }

    private static void Mouse(string name, MouseButton button)
    {
        Ensure(name);
        InputMap.ActionAddEvent(name, new InputEventMouseButton { ButtonIndex = button });
    }

    private static void Pad(string name, JoyButton button)
    {
        Ensure(name);
        InputMap.ActionAddEvent(name, new InputEventJoypadButton { ButtonIndex = button });
    }

    private static void Joy(string name, JoyAxis axis, float value)
    {
        Ensure(name);
        InputMap.ActionAddEvent(name, new InputEventJoypadMotion { Axis = axis, AxisValue = value });
    }

    private static void Ensure(string name)
    {
        if (!InputMap.HasAction(name)) InputMap.AddAction(name);
    }
}
