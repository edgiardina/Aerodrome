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

    public const string ClearJam = "clear_jam";
    public const string CycleScheme = "cycle_scheme";
    public const string DebugOverlay = "debug_overlay";
    public const string Restart = "restart";
    public const string CycleEnemies = "cycle_enemies";
    public const string CycleWingmen = "cycle_wingmen";
    public const string Mute = "mute";
    public const string Pause = "pause";
    public const string TuningPanel = "tuning_panel";
    public const string TuningSave = "tuning_save";
    public const string TuningLoad = "tuning_load";
    public const string TuningReset = "tuning_reset";

    public static void Register()
    {
        // --- Throttle -------------------------------------------------------
        // On a pad this lives on the left stick, read directly as an axis in
        // PlayerInput, because the triggers are wanted for the guns.
        Action(ThrottleUp, Key.W, Key.KpAdd, Key.Equal);
        Action(ThrottleDown, Key.S, Key.KpSubtract, Key.Minus);

        // --- Guns -----------------------------------------------------------
        // Right trigger fires, right bumper works a jam. Keeping the two on
        // neighbouring fingers matters: a jam happens mid-burst, and the hand that
        // was pulling the trigger is the hand that has to fix it.
        Action(Fire, Key.Space);
        Mouse(Fire, MouseButton.Left);
        Joy(Fire, JoyAxis.TriggerRight, 1.0f);

        Action(ClearJam, Key.X);
        Pad(ClearJam, JoyButton.RightShoulder);

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

        // --- Classic 8-way numpad, and the d-pad, which is the same idea ------
        Action(ClassicUp, Key.Kp8);
        Action(ClassicDown, Key.Kp2);
        Action(ClassicLeft, Key.Kp4);
        Action(ClassicRight, Key.Kp6);
        Pad(ClassicUp, JoyButton.DpadUp);
        Pad(ClassicDown, JoyButton.DpadDown);
        Pad(ClassicLeft, JoyButton.DpadLeft);
        Pad(ClassicRight, JoyButton.DpadRight);
        Action(ClassicMode, Key.C);
        Pad(ClassicMode, JoyButton.Y);

        // --- Debug ----------------------------------------------------------
        Action(CycleScheme, Key.F2);
        Pad(CycleScheme, JoyButton.Back);
        Action(DebugOverlay, Key.F3);
        Action(Restart, Key.R);

        // Start is pause, which is where every player will look for it. Restart
        // keeps the R key and gives up its pad button.
        Action(Pause, Key.P, Key.Escape);
        Pad(Pause, JoyButton.Start);

        // --- Tuning and match setup ------------------------------------------
        // The tuning panel is keyboard and mouse only on purpose. Every pad button
        // worth having is already flying the aeroplane, and the one place a d-pad
        // would fit is the Classic 8-way heading control.
        Action(TuningPanel, Key.F4);
        Action(TuningSave, Key.F9);
        Action(TuningLoad, Key.F10);
        Action(TuningReset, Key.Home);
        Action(CycleEnemies, Key.F6);
        Action(CycleWingmen, Key.F7);

        // Mute. Needs to be one key, always available, no menu in the way. An
        // engine loop you cannot silence makes the game unusable next to anything
        // else, and the scripted capture runs it unattended.
        Action(Mute, Key.M);
        Pad(Mute, JoyButton.RightStick);
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
