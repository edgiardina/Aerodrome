using System;
using System.Collections.Generic;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// Every number that decides how the aeroplane feels, on sliders, while you fly.
///
/// This exists because flight feel cannot be argued about in a chat window. The
/// only way to find out whether a loop should take 3.0 or 3.4 seconds is to fly
/// both, back to back, inside ten seconds of each other. Reading a value out of a
/// source file, changing it, rebuilding and relaunching breaks that loop badly
/// enough that you stop trying things.
///
/// Two rules that keep it honest:
///
///   1. It writes real spec fields, not a parallel set of magic numbers. What you
///      dial in here is exactly what a code change would do, so a setting you like
///      can be copied straight into AircraftSpec.
///   2. Changes to the enemy are applied as a RATIO of its own baseline, not as
///      the same absolute number. The Camel and the Dr.I are deliberately
///      different aircraft, and a panel that flattened them into one would quietly
///      undo the only reason there are two.
/// </summary>
public sealed partial class TuningPanel : Control
{
    private const float PanelW = 460f;
    private const float RowH = 21f;
    private const float BarW = 132f;

    private static readonly Color Ink = new(0.88f, 0.90f, 0.86f);
    private static readonly Color Dim = new(0.52f, 0.56f, 0.52f);
    private static readonly Color Accent = new(0.98f, 0.80f, 0.32f);
    private static readonly Color Changed = new(0.55f, 0.85f, 0.62f);
    private static readonly Color Backdrop = new(0.03f, 0.04f, 0.05f, 0.93f);
    private static readonly Color Border = new(0.42f, 0.46f, 0.44f, 0.9f);

    private const string PresetPath = "user://tuning.json";

    private readonly List<Knob> _knobs = new();
    private readonly List<(SimAircraft Aircraft, AircraftSpec Baseline)> _targets = new();

    private Font _font = null!;
    private int _selected;
    private double _repeatDelay;
    private bool _applyToEnemy = true;
    private string _status = "";
    private double _statusFor;

    /// <summary>Which aircraft the knob values are absolute values FOR.</summary>
    private string _playerBaselineName = "";

    public bool Open { get; private set; }

    public static TuningPanel Create() => new()
    {
        Name = "TuningPanel",
        MouseFilter = MouseFilterEnum.Ignore,
        AnchorRight = 1,
        AnchorBottom = 1,
        Visible = false,
    };

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        BuildKnobs();
        LoadPreset(quiet: true);
    }

    // --- The knobs ----------------------------------------------------------

    private sealed class Knob
    {
        public required string Label { get; init; }
        public required string Key { get; init; }
        public required double Min { get; init; }
        public required double Max { get; init; }
        public required double Step { get; init; }
        public string Format { get; init; } = "F2";
        public string Unit { get; init; } = "";

        /// <summary>Reads and writes an aircraft spec field. Null for a world knob.</summary>
        public Func<AircraftSpec, double>? Read { get; init; }
        public Func<AircraftSpec, double, AircraftSpec>? Write { get; init; }

        /// <summary>Reads and writes something outside the spec, like the camera.</summary>
        public Func<double>? ReadWorld { get; init; }
        public Action<double>? WriteWorld { get; init; }

        public double PlayerBaseline;
        public double Value;

        public bool IsDefault => Math.Abs(Value - PlayerBaseline) < Step * 0.01;

        /// <summary>
        /// Clamped, so a knob whose range is wrong can never draw its bar outside
        /// the panel. One of them did exactly that.
        /// </summary>
        public double Fraction => Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0.0, 1.0) : 0.0;
    }

    private void Add(Knob knob)
    {
        knob.PlayerBaseline = knob.Read is not null
            ? knob.Read(AircraftSpec.CamelArcade)
            : knob.ReadWorld!();

        knob.Value = knob.PlayerBaseline;
        _knobs.Add(knob);
    }

    private void BuildKnobs()
    {
        const double Hp = 745.7;

        // --- How it turns. Tune these first: everything else is downstream.
        Add(new Knob
        {
            Label = "nose slew cap", Key = "slew", Min = 1.0, Max = 9.0, Step = 0.1,
            Format = "F1", Unit = " rad/s",
            Read = s => s.MaxSlewRateRad, Write = (s, v) => s with { MaxSlewRateRad = v },
        });
        Add(new Knob
        {
            Label = "turn rate scale", Key = "turnscale", Min = 0.5, Max = 2.0, Step = 0.02,
            Read = s => s.TurnRateScale, Write = (s, v) => s with { TurnRateScale = v },
        });
        Add(new Knob
        {
            Label = "G limit", Key = "glimit", Min = 3.0, Max = 14.0, Step = 0.25,
            Format = "F2", Unit = " G",
            Read = s => s.GLimit, Write = (s, v) => s with { GLimit = v },
        });
        Add(new Knob
        {
            Label = "max lift Cl", Key = "clmax", Min = 0.8, Max = 3.2, Step = 0.05,
            Read = s => s.ClMax, Write = (s, v) => s with { ClMax = v },
        });
        Add(new Knob
        {
            Label = "push authority", Key = "push", Min = 0.10, Max = 1.00, Step = 0.02,
            Read = s => s.PushFactor, Write = (s, v) => s with { PushFactor = v },
        });
        Add(new Knob
        {
            Label = "tail weathercock", Key = "weather", Min = 0.0, Max = 6.0, Step = 0.1,
            Format = "F1",
            Read = s => s.WeathercockGain, Write = (s, v) => s with { WeathercockGain = v },
        });
        Add(new Knob
        {
            Label = "elevator rate", Key = "elevator", Min = 1.0, Max = 12.0, Step = 0.1,
            Format = "F1", Unit = " rad/s",
            Read = s => s.ElevatorRateRad, Write = (s, v) => s with { ElevatorRateRad = v },
        });

        // --- The dive limit.
        Add(new Knob
        {
            // Range in km/h, because that is what the knob reads and writes. It was
            // first written as 60 to 160, which are the m/s numbers, so the value
            // sat at nearly three times full scale and the bar drew straight off
            // the side of the panel.
            Label = "never-exceed speed", Key = "vne", Min = 200, Max = 500, Step = 5,
            Format = "F0", Unit = " km/h",
            Read = s => s.NeverExceedSpeed * 3.6,
            Write = (s, v) => s with { NeverExceedSpeed = v / 3.6 },
        });
        Add(new Knob
        {
            Label = "overspeed tolerance", Key = "vnetol", Min = 0.5, Max = 20.0, Step = 0.5,
            Format = "F1", Unit = " s",
            Read = s => s.OverspeedToleranceS, Write = (s, v) => s with { OverspeedToleranceS = v },
        });

        // --- How it holds energy.
        Add(new Knob
        {
            Label = "parasite drag Cd0", Key = "cd0", Min = 0.008, Max = 0.070, Step = 0.001,
            Format = "F3",
            Read = s => s.Cd0, Write = (s, v) => s with { Cd0 = v },
        });
        Add(new Knob
        {
            Label = "engine power", Key = "power", Min = 80, Max = 400, Step = 5,
            Format = "F0", Unit = " hp",
            Read = s => s.EnginePowerW / Hp, Write = (s, v) => s with { EnginePowerW = v * Hp },
        });
        Add(new Knob
        {
            Label = "mass", Key = "mass", Min = 350, Max = 900, Step = 5,
            Format = "F0", Unit = " kg",
            Read = s => s.MassKg, Write = (s, v) => s with { MassKg = v },
        });
        Add(new Knob
        {
            Label = "throttle travel", Key = "throttle", Min = 0.3, Max = 4.0, Step = 0.1,
            Format = "F1", Unit = " /s",
            Read = s => s.ThrottleSlewPerSecond, Write = (s, v) => s with { ThrottleSlewPerSecond = v },
        });

        // --- The reversals.
        Add(new Knob
        {
            Label = "half roll time", Key = "roll", Min = 0.15, Max = 1.00, Step = 0.01,
            Unit = " s",
            Read = s => s.HalfRollSeconds, Write = (s, v) => s with { HalfRollSeconds = v },
        });
        Add(new Knob
        {
            Label = "flat turn time", Key = "flat", Min = 0.40, Max = 2.00, Step = 0.05,
            Unit = " s",
            Read = s => s.FlatTurnSeconds, Write = (s, v) => s with { FlatTurnSeconds = v },
        });
        Add(new Knob
        {
            Label = "flat turn speed cost", Key = "flatcost", Min = 0.0, Max = 0.50, Step = 0.01,
            Read = s => s.FlatTurnSpeedCost, Write = (s, v) => s with { FlatTurnSpeedCost = v },
        });

        // --- The guns. How long a fight lasts lives here.
        Add(new Knob
        {
            Label = "damage per round", Key = "rounddmg", Min = 0.010, Max = 0.150, Step = 0.002,
            Format = "F3",
            Read = s => s.RoundIntegrityDamage, Write = (s, v) => s with { RoundIntegrityDamage = v },
        });
        Add(new Knob
        {
            Label = "point blank range", Key = "pointblank", Min = 30, Max = 300, Step = 5,
            Format = "F0", Unit = " m",
            Read = s => s.PointBlankRangeM, Write = (s, v) => s with { PointBlankRangeM = v },
        });
        Add(new Knob
        {
            Label = "max effective range", Key = "maxrange", Min = 120, Max = 900, Step = 10,
            Format = "F0", Unit = " m",
            Read = s => s.MaxEffectiveRangeM, Write = (s, v) => s with { MaxEffectiveRangeM = v },
        });
        Add(new Knob
        {
            Label = "long range floor", Key = "rangefloor", Min = 0.0, Max = 1.0, Step = 0.02,
            Read = s => s.LongRangeDamageFloor, Write = (s, v) => s with { LongRangeDamageFloor = v },
        });
        Add(new Knob
        {
            Label = "gun heat rate", Key = "heat", Min = 0.0, Max = 1.5, Step = 0.02,
            Unit = " /s",
            Read = s => s.GunHeatPerSecond, Write = (s, v) => s with { GunHeatPerSecond = v },
        });
        Add(new Knob
        {
            Label = "jam clear per press", Key = "jamclear", Min = 0.05, Max = 1.00, Step = 0.02,
            Read = s => s.JamClearPerPress, Write = (s, v) => s with { JamClearPerPress = v },
        });

        // --- The camera. Not a spec field, but it changes the game as much as one.
        Add(new Knob
        {
            Label = "camera view width", Key = "camwidth", Min = 120, Max = 900, Step = 10,
            Format = "F0", Unit = " m",
            ReadWorld = () => ChaseCamera.NearViewWidthM,
            WriteWorld = v => ChaseCamera.NearViewWidthM = v,
        });
        Add(new Knob
        {
            Label = "duel framing range", Key = "camframe", Min = 120, Max = 1200, Step = 10,
            Format = "F0", Unit = " m",
            ReadWorld = () => ChaseCamera.FramingRangeM,
            WriteWorld = v => ChaseCamera.FramingRangeM = v,
        });
    }

    // --- Applying ------------------------------------------------------------

    /// <summary>
    /// Point at a freshly started round. Each aircraft's own spec at this moment is
    /// its baseline, so the panel never compounds its own edits round over round.
    /// </summary>
    public void Retarget(SimRunner sim)
    {
        _targets.Clear();
        foreach (var a in sim.Aircraft) _targets.Add((a, a.Spec));

        // Every knob is an absolute value on the PLAYER's aircraft, and the other
        // side takes the same change as a ratio of its own baseline. Swap sides and
        // that reference changes underneath the panel: leaving it alone would push
        // the Camel's numbers onto a Dr.I as absolutes and quietly turn the triplane
        // into a Camel, which is the exact thing the ratio logic exists to prevent.
        //
        // So the board is re-seeded from whatever you are now flying. Hand tuning
        // does not survive a side swap, which is worth saying out loud.
        var baseline = sim.Player.Spec;

        if (baseline.Name != _playerBaselineName)
        {
            bool hadEdits = _playerBaselineName.Length > 0 && _knobs.Exists(k => !k.IsDefault);
            _playerBaselineName = baseline.Name;

            foreach (var knob in _knobs)
            {
                if (knob.Read is null) continue;
                knob.PlayerBaseline = knob.Read(baseline);
                knob.Value = Math.Clamp(knob.PlayerBaseline, knob.Min, knob.Max);
            }

            if (hadEdits) Note($"reset to {baseline.Name} defaults");
        }

        Apply();
    }

    private void Apply()
    {
        foreach (var (aircraft, baseline) in _targets)
            aircraft.Combatant.Spec = Rebuild(baseline, aircraft.Team == Team.Player);

        foreach (var knob in _knobs) knob.WriteWorld?.Invoke(knob.Value);
    }

    /// <summary>
    /// Rebuild a spec from its untouched baseline. Always from the baseline, never
    /// from the current spec: applying edits on top of edits drifts, and toggling a
    /// setting off would never put anything back.
    /// </summary>
    private AircraftSpec Rebuild(AircraftSpec baseline, bool isPlayer)
    {
        var spec = baseline;

        foreach (var knob in _knobs)
        {
            if (knob.Write is null || knob.Read is null) continue;

            double value = knob.Value;

            if (!isPlayer)
            {
                if (!_applyToEnemy) continue;

                // Same proportional change, not the same number. This is what keeps
                // the triplane a triplane.
                double mine = knob.PlayerBaseline;
                double theirs = knob.Read(baseline);
                value = Math.Abs(mine) > 1e-9 ? theirs * (knob.Value / mine) : knob.Value;
            }

            spec = knob.Write(spec, value);
        }

        return spec;
    }

    // --- Input ---------------------------------------------------------------

    public void Toggle()
    {
        Open = !Open;
        Visible = Open;
        MouseFilter = Open ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (!Open) { QueueRedraw(); return; }

        _statusFor = Math.Max(0, _statusFor - delta);

        if (Input.IsActionJustPressed("ui_down")) _selected = (_selected + 1) % _knobs.Count;
        if (Input.IsActionJustPressed("ui_up")) _selected = (_selected - 1 + _knobs.Count) % _knobs.Count;

        int direction = (Input.IsActionPressed("ui_right") ? 1 : 0) - (Input.IsActionPressed("ui_left") ? 1 : 0);

        if (direction == 0) _repeatDelay = 0;
        else
        {
            // A press moves one step. Holding waits, then runs. Without the wait a
            // single tap jumps five steps at any sane framerate.
            bool tapped = Input.IsActionJustPressed("ui_right") || Input.IsActionJustPressed("ui_left");
            _repeatDelay += delta;

            if (tapped || _repeatDelay > 0.35)
            {
                double scale = Input.IsKeyPressed(Key.Shift) ? 5.0 : 1.0;
                Nudge(_selected, direction * scale);
                if (!tapped) _repeatDelay = 0.35 - 0.02;
            }
        }

        if (Input.IsActionJustPressed(InputBindings.TuningReset)) Reset(_selected);
        if (Input.IsActionJustPressed(InputBindings.TuningSave)) SavePreset();
        if (Input.IsActionJustPressed(InputBindings.TuningLoad)) LoadPreset();

        if (Input.IsKeyPressed(Key.T) && _statusFor <= 0)
        {
            _applyToEnemy = !_applyToEnemy;
            Note(_applyToEnemy ? "applying to both aircraft" : "applying to the player only");
            Apply();
        }

        QueueRedraw();
    }

    private void Nudge(int index, double steps)
    {
        var knob = _knobs[index];
        knob.Value = Math.Clamp(knob.Value + knob.Step * steps, knob.Min, knob.Max);
        Apply();
    }

    private void Reset(int index)
    {
        _knobs[index].Value = _knobs[index].PlayerBaseline;
        Apply();
        Note($"{_knobs[index].Label} back to default");
    }

    /// <summary>Click or drag a bar to set it. Faster than arrowing across the range.</summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (!Open) return;

        bool dragging = @event is InputEventMouseMotion m && (m.ButtonMask & MouseButtonMask.Left) != 0;
        bool clicked = @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true };
        if (!dragging && !clicked) return;

        var mouse = ((InputEventMouse)@event).Position;
        var origin = Origin();

        for (int i = 0; i < _knobs.Count; i++)
        {
            var bar = BarRect(origin, i);
            if (mouse.Y < bar.Position.Y - 4 || mouse.Y > bar.End.Y + 4) continue;
            if (mouse.X < bar.Position.X - 8 || mouse.X > bar.End.X + 8) continue;

            var knob = _knobs[i];
            double fraction = Math.Clamp((mouse.X - bar.Position.X) / bar.Size.X, 0.0, 1.0);
            double raw = knob.Min + fraction * (knob.Max - knob.Min);

            _selected = i;
            knob.Value = Math.Clamp(Math.Round(raw / knob.Step) * knob.Step, knob.Min, knob.Max);
            Apply();
            AcceptEvent();
            return;
        }
    }

    // --- Presets --------------------------------------------------------------

    private void SavePreset()
    {
        var data = new Godot.Collections.Dictionary();
        foreach (var knob in _knobs) data[knob.Key] = knob.Value;
        data["apply_to_enemy"] = _applyToEnemy;

        using var file = FileAccess.Open(PresetPath, FileAccess.ModeFlags.Write);
        if (file is null) { Note("could not write the preset"); return; }

        file.StoreString(Json.Stringify(data, "  "));
        Note($"saved to {ProjectSettings.GlobalizePath(PresetPath)}");
        GD.Print($"[tuning] saved {ProjectSettings.GlobalizePath(PresetPath)}");
    }

    private void LoadPreset(bool quiet = false)
    {
        if (!FileAccess.FileExists(PresetPath))
        {
            if (!quiet) Note("no saved preset yet");
            return;
        }

        using var file = FileAccess.Open(PresetPath, FileAccess.ModeFlags.Read);
        if (file is null) return;

        if (Json.ParseString(file.GetAsText()).Obj is not Godot.Collections.Dictionary data)
        {
            if (!quiet) Note("the preset file is not readable");
            return;
        }

        foreach (var knob in _knobs)
        {
            if (!data.ContainsKey(knob.Key)) continue;
            knob.Value = Math.Clamp((double)data[knob.Key], knob.Min, knob.Max);
        }

        if (data.ContainsKey("apply_to_enemy")) _applyToEnemy = (bool)data["apply_to_enemy"];

        Apply();
        if (!quiet) Note("preset loaded");
    }

    private void Note(string text) { _status = text; _statusFor = 2.0; }

    // --- Drawing ---------------------------------------------------------------

    private Vector2 Origin() => new(Size.X - PanelW - 14f, 168f);

    private Rect2 BarRect(Vector2 origin, int index) => new(
        origin.X + PanelW - BarW - 96f,
        origin.Y + 40f + index * RowH - 9f,
        BarW, 7f);

    public override void _Draw()
    {
        if (!Open) return;

        var origin = Origin();
        float height = 40f + _knobs.Count * RowH + 96f;
        var panel = new Rect2(origin, new Vector2(PanelW, height));

        DrawRect(panel, Backdrop, true);
        DrawRect(panel, Border, false, 1.2f);

        DrawString(_font, origin + new Vector2(14, 22), "FLIGHT MODEL", HorizontalAlignment.Left, -1, 14, Ink);
        RightAligned(origin.X + PanelW - 14, origin.Y + 22, 150,
                     _applyToEnemy ? "both aircraft" : "player only",
                     11, _applyToEnemy ? Accent : Dim);

        for (int i = 0; i < _knobs.Count; i++) DrawKnob(origin, i);

        DrawDerived(origin, 40f + _knobs.Count * RowH + 10f);
    }

    private void DrawKnob(Vector2 origin, int index)
    {
        var knob = _knobs[index];
        float y = origin.Y + 40f + index * RowH;
        bool selected = index == _selected;

        if (selected)
            DrawRect(new Rect2(origin.X + 6, y - 14, PanelW - 12, RowH - 2), new Color(Accent, 0.13f), true);

        var labelColor = selected ? Accent : knob.IsDefault ? Dim : Changed;
        DrawString(_font, new Vector2(origin.X + 14, y), knob.Label, HorizontalAlignment.Left, -1, 12, labelColor);

        var bar = BarRect(origin, index);
        DrawRect(bar, new Color(Dim, 0.22f), true);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * (float)knob.Fraction, bar.Size.Y)),
                 selected ? Accent : knob.IsDefault ? Dim : Changed, true);

        // A tick where the shipped value sits, so you can always see how far you
        // have wandered and get back to it by eye.
        float home = bar.Position.X + bar.Size.X *
                     (float)((knob.PlayerBaseline - knob.Min) / Math.Max(1e-9, knob.Max - knob.Min));
        DrawLine(new Vector2(home, bar.Position.Y - 3), new Vector2(home, bar.End.Y + 3), Ink, 1f);

        RightAligned(origin.X + PanelW - 14, y, 86,
                     knob.Value.ToString(knob.Format) + knob.Unit,
                     12, selected ? Ink : Dim);
    }

    /// <summary>
    /// Right-align text so it ENDS at <paramref name="right"/>.
    ///
    /// DrawString with HorizontalAlignment.Right and a width of 0 does not do this.
    /// It draws left to right from the position given and runs off the end of the
    /// panel, which is what the first version of this did to every value on screen.
    /// The alignment needs a real width to align inside.
    /// </summary>
    private void RightAligned(float right, float y, float width, string text, int size, Color color)
        => DrawString(_font, new Vector2(right - width, y), text,
                      HorizontalAlignment.Right, width, size, color);

    /// <summary>
    /// What the numbers above actually produce. Corner speed and peak turn rate are
    /// the two figures the whole dogfight orbits around, and reading them here beats
    /// flying a lap to find out you made it worse.
    /// </summary>
    private void DrawDerived(Vector2 origin, float top)
    {
        if (_targets.Count == 0) return;

        var spec = _targets[0].Aircraft.Spec;
        float y = origin.Y + top;

        DrawLine(new Vector2(origin.X + 10, y - 8), new Vector2(origin.X + PanelW - 10, y - 8),
                 new Color(Dim, 0.4f), 1f);

        PeakTurn(spec, out double peakDegPerSecond, out double atSpeed);

        string line1 = $"stall {spec.StallSpeedSeaLevel * 3.6:F0}   " +
                       $"corner {spec.CornerSpeedSeaLevel * 3.6:F0} km/h   " +
                       $"peak {peakDegPerSecond:F0} deg/s at {atSpeed * 3.6:F0}";
        string line2 = $"360 loop about {(peakDegPerSecond > 1 ? 360.0 / peakDegPerSecond : 0):F1} s";

        DrawString(_font, new Vector2(origin.X + 14, y + 10), line1, HorizontalAlignment.Left, -1, 11, Ink);
        DrawString(_font, new Vector2(origin.X + 14, y + 27), line2, HorizontalAlignment.Left, -1, 11, Dim);

        string help = _statusFor > 0
            ? _status
            : "arrows move   shift x5   click a bar   Home default   T both/player   F9 save  F10 load";

        DrawString(_font, new Vector2(origin.X + 14, y + 50), help,
                   HorizontalAlignment.Left, -1, 10, _statusFor > 0 ? Accent : Dim);
    }

    /// <summary>Sweep the speed range and find where the nose comes round fastest.</summary>
    private static void PeakTurn(AircraftSpec spec, out double degreesPerSecond, out double atSpeed)
    {
        double density = Atmosphere.Density(300.0);
        double best = 0, bestSpeed = 0;

        for (double v = 15; v <= 120; v += 0.5)
        {
            double rate = FlightModel.MaxSlewRate(v, density, spec);
            if (rate > best) { best = rate; bestSpeed = v; }
        }

        degreesPerSecond = Angles.ToDegrees(best);
        atSpeed = bestSpeed;
    }
}
