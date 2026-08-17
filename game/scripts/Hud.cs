using System;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// Telemetry overlay, aim reticle, off-screen enemy markers, and the frame-time
/// graph. The telemetry is the text-mode version of the M1 tuning overlay: every
/// number you need to judge whether the flight model feels right.
/// </summary>
public sealed partial class Hud : Control
{
    private const int FrameSamples = 180;

    private static readonly Color Ink = new(0.86f, 0.88f, 0.84f);
    private static readonly Color Dim = new(0.55f, 0.58f, 0.54f);
    private static readonly Color Warn = new(0.98f, 0.72f, 0.20f);
    private static readonly Color Danger = new(0.95f, 0.35f, 0.28f);
    private static readonly Color Good = new(0.45f, 0.82f, 0.55f);
    // Opaque enough to stay readable against a bright cloud. At 0.55 the telemetry
    // vanished the moment the aircraft climbed into an overcast.
    private static readonly Color Panel = new(0.04f, 0.05f, 0.06f, 0.82f);

    private SimRunner _sim = null!;
    private ChaseCamera _camera = null!;
    private PlayerInput _input = null!;
    private Func<AiSkill> _skill = null!;
    private Func<Flight?> _flight = null!;
    private Func<bool> _paused = null!;
    private Font _font = null!;

    private readonly float[] _frameMs = new float[FrameSamples];
    private int _frameIndex;

    /// <summary>
    /// The text telemetry and the frame graph. Off by default now that the
    /// instrument board carries the same readings, because two full readouts of the
    /// same six numbers on one screen is worse than either alone. F3 brings it back,
    /// and tuning work wants it back.
    /// </summary>
    public bool ShowDebug { get; set; }

    public static Hud Create(SimRunner sim, ChaseCamera camera, PlayerInput input,
                             Func<AiSkill> skill, Func<Flight?> flight, Func<bool> paused) => new()
    {
        Name = "Hud",
        _sim = sim,
        _camera = camera,
        _input = input,
        _skill = skill,
        _flight = flight,
        _paused = paused,
        MouseFilter = MouseFilterEnum.Ignore,
        AnchorRight = 1,
        AnchorBottom = 1,
    };

    public override void _Ready() => _font = ThemeDB.FallbackFont;

    public override void _Process(double delta)
    {
        _frameMs[_frameIndex] = (float)(delta * 1000.0);
        _frameIndex = (_frameIndex + 1) % FrameSamples;
        QueueRedraw();
    }

    /// <summary>
    /// Attribution. The Sopwith Camel model is CC-BY, which allows commercial use
    /// but requires a visible credit, so this is an obligation and not a nicety.
    /// Every row in docs/ASSETS.md that needs crediting belongs here.
    /// </summary>
    private static readonly (string What, string Who, string Licence)[] Credits =
    {
        ("Sopwith Camel model", "bradacvojtech (Sketchfab)", "CC-BY 4.0"),
        ("Fokker Dr.I model", "KojfDiscord (Sketchfab)", "CC-BY 4.0"),
    };

    public bool ShowCredits { get; set; }

    private void DrawCredits(Vector2 size)
    {
        const float w = 520f, h = 150f;
        var box = new Rect2((size.X - w) * 0.5f, size.Y * 0.30f, w, h);
        DrawRect(box, new Color(0.03f, 0.04f, 0.05f, 0.92f), true);
        DrawRect(box, Border, false, 1.2f);

        float y = box.Position.Y + 32;
        DrawString(_font, new Vector2(box.Position.X, y), "CREDITS",
                   HorizontalAlignment.Center, w, 18, Ink);
        y += 34;

        foreach (var (what, who, licence) in Credits)
        {
            DrawString(_font, new Vector2(box.Position.X + 24, y), what,
                       HorizontalAlignment.Left, -1, 13, Dim);
            DrawString(_font, new Vector2(box.Position.X + 24, y + 18), $"{who}   {licence}",
                       HorizontalAlignment.Left, -1, 13, Ink);
            y += 44;
        }

        DrawString(_font, new Vector2(box.Position.X, box.End.Y - 14), "F1 to close",
                   HorizontalAlignment.Center, w, 11, Dim);
    }

    private static readonly Color Border = new(0.42f, 0.46f, 0.44f, 0.9f);

    public override void _Draw()
    {
        var size = Size;

        if (ShowCredits) { DrawCredits(size); return; }

        if (ShowDebug) DrawTelemetry();
        DrawReticle(size);
        DrawOffscreenMarkers(size);
        if (ShowDebug) DrawFrameGraph(size);
        DrawWarnings(size);
        if (_paused()) DrawPaused(size);
    }

    private void DrawPaused(Vector2 size)
    {
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.03f, 0.04f, 0.45f), true);
        Banner(size, size.Y * 0.44f, "PAUSED", Ink, 44);
        Banner(size, size.Y * 0.44f + 34, "P or Start to fly on", Dim, 14);
    }

    // --- Telemetry ----------------------------------------------------------

    private void DrawTelemetry()
    {
        var s = _sim.Player.State;
        var spec = _sim.Player.Spec;

        // Tall enough for the mute row, which only appears when it applies.
        DrawRect(new Rect2(14, 14, 236, 372), Panel, true);

        float y = 36;
        Row("AIRSPEED", $"{s.Airspeed * 3.6:F0} km/h", ref y, SpeedColor(s, spec));
        Row("ALTITUDE", $"{s.Position.Y:F0} m", ref y, Ink);
        Row("ENERGY", $"{s.EnergyHeightM:F0} m", ref y, Good);
        Row("THROTTLE", $"{s.Throttle * 100:F0} %", ref y, Ink);
        y += 8;
        Row("ALPHA", $"{Angles.ToDegrees(s.Alpha):F1} deg", ref y, s.IsStalled ? Danger : Dim);
        Row("LOAD", $"{s.LoadFactor:F1} G", ref y, Math.Abs(s.LoadFactor) > spec.GLimit * 0.9 ? Warn : Dim);
        Row("ATTITUDE", s.IsInverted ? "INVERTED" : "upright", ref y, s.IsInverted ? Warn : Dim);

        y += 10;
        Row("AMMO", $"{s.Ammo}", ref y, AmmoColor(s, spec));
        Row("GUNS", GunStatus(s), ref y, s.GunsCanBear ? Dim : Danger);
        Bar("HEAT", s.GunHeat, ref y, s.GunHeat > 0.7 ? Danger : s.GunHeat > 0.35 ? Warn : Good);

        y += 10;
        // No health bar for the aircraft as a whole. Only the parts, because that
        // is what the pilot actually feels going wrong.
        Bar("ENGINE", s.EngineHealth, ref y, HealthColor(s.EngineHealth));
        Bar("WINGS", s.WingHealth, ref y, HealthColor(s.WingHealth));
        Bar("TAIL", s.TailHealth, ref y, HealthColor(s.TailHealth));
        Bar("FUEL", s.FuelSystemHealth, ref y, HealthColor(s.FuelSystemHealth));

        y += 8;
        Row("ENEMY", EnemyLine(), ref y, Dim);
        Row("STICK", _input.ControlScheme == PlayerInput.Scheme.Stick ? "COLUMN (pad)"
            : _input.ClassicMode ? "CLASSIC 8-WAY" : "point to aim", ref y, Dim);

        if (Main.IsMuted()) Row("AUDIO", "MUTED  (M)", ref y, Warn);

        void Row(string label, string value, ref float rowY, Color color)
        {
            DrawString(_font, new Vector2(26, rowY), label, HorizontalAlignment.Left, -1, 11, Dim);

            // A width of 0 does NOT right-align, it just draws from the position
            // given, so every value used to run out past the edge of the panel.
            const float valueW = 148f;
            DrawString(_font, new Vector2(238 - valueW, rowY), value,
                       HorizontalAlignment.Right, valueW, 13, color);
            rowY += 19;
        }

        void Bar(string label, double fraction, ref float rowY, Color color)
        {
            DrawString(_font, new Vector2(26, rowY), label, HorizontalAlignment.Left, -1, 11, Dim);
            const float w = 92f;
            float x = 238 - w;
            DrawRect(new Rect2(x, rowY - 8, w, 6), new Color(Dim, 0.25f), true);
            DrawRect(new Rect2(x, rowY - 8, w * (float)Math.Clamp(fraction, 0, 1), 6), color, true);
            rowY += 19;
        }
    }

    /// <summary>
    /// How good they are, and how many are left. "2 of 3" is the number the player
    /// is actually keeping in their head during a flight engagement.
    /// </summary>
    private string EnemyLine()
    {
        var flight = _flight();
        if (flight is null || flight.Members.Count <= 1) return _skill().Name;

        return $"{_skill().Name}  {flight.AliveCount} of {flight.Members.Count}";
    }

    private static string GunStatus(AircraftState s)
    {
        if (s.GunJammed) return s.JamClearProgress > 0 ? "CLEARING..." : "JAMMED";
        if (s.IsFlatTurning) return "MASKED";
        if (s.Ammo <= 0) return "EMPTY";
        return "clear";
    }

    private static Color AmmoColor(AircraftState s, AircraftSpec spec)
        => s.Ammo == 0 ? Danger : s.Ammo < spec.AmmoRounds * 0.2 ? Warn : Ink;

    private static Color HealthColor(double health)
        => health > 0.75 ? Good : health > 0.4 ? Warn : Danger;

    private static Color SpeedColor(AircraftState s, AircraftSpec spec)
    {
        double stall = spec.StallSpeedSeaLevel;
        if (s.Airspeed < stall * 1.1) return Danger;
        if (s.Airspeed < stall * 1.4) return Warn;
        return Ink;
    }

    // --- Aim ----------------------------------------------------------------

    private void DrawReticle(Vector2 size)
    {
        Vector2 center = size * 0.5f;
        bool masked = !_sim.Player.State.GunsCanBear;
        DrawArc(center, 5, 0, Mathf.Tau, 16, masked ? Danger : Dim, 1.5f, true);

        if (masked || _input.AimHeading is not { } heading) return;

        // Show the command vector: where the pilot is telling the nose to go.
        var dir = new Vector2((float)Math.Cos(heading), -(float)Math.Sin(heading));
        DrawLine(center + dir * 18f, center + dir * 44f, Ink, 2f, true);
        DrawArc(center + dir * 54f, 6, 0, Mathf.Tau, 14, Ink, 1.5f, true);
    }

    // --- Off-screen awareness ----------------------------------------------

    /// <summary>
    /// A kill from a target the player could never have seen is a bug, not a
    /// difficulty setting. Every enemy outside the viewport gets a border marker.
    ///
    /// With a whole flight up there, one more thing has to be legible: which of them
    /// is actually coming for you. The others are holding a perch and will not shoot
    /// unless you fly across their nose, so a marker that treated all three as the
    /// same threat would be telling you the wrong thing three times.
    /// </summary>
    private void DrawOffscreenMarkers(Vector2 size)
    {
        var player = _sim.Player;
        var flight = _flight();
        Vector2 camCenter = _camera.CenterM;
        double halfW = _camera.VisibleWidthM * 0.5;
        double halfH = _camera.VisibleHeightM * 0.5;
        const float margin = 42f;

        foreach (var other in _sim.Aircraft)
        {
            if (other == player || other.Team == player.Team || !other.State.IsAlive) continue;

            var p = other.State.Position;
            bool onScreen = Math.Abs(p.X - camCenter.X) < halfW && Math.Abs(p.Y - camCenter.Y) < halfH;
            if (onScreen) continue;

            var toward = new Vector2((float)(p.X - camCenter.X), -(float)(p.Y - camCenter.Y)).Normalized();
            Vector2 edge = size * 0.5f + toward * BorderDistance(size, toward, margin);

            double range = (p - player.State.Position).Length;
            bool pressing = flight is not null && ReferenceEquals(flight.Engaged, other.Combatant);

            float alpha = (float)Math.Clamp(1.2 - range / 2200.0, pressing ? 0.65 : 0.30, 1.0);
            var color = new Color(pressing ? Danger : Warn, alpha);

            // Triangle pointing outward, toward the contact. The one pressing the
            // attack gets a bigger one with a ring round it.
            Vector2 perp = new(-toward.Y, toward.X);
            float nose = pressing ? 14f : 11f;
            float back = pressing ? 9f : 7f;
            float half = pressing ? 10f : 8f;

            DrawColoredPolygon(
                new[] { edge + toward * nose, edge - toward * back + perp * half, edge - toward * back - perp * half },
                color);

            if (pressing) DrawArc(edge, 17f, 0, Mathf.Tau, 20, color, 1.6f, true);

            // Relative altitude tick: above you or below you.
            double dy = p.Y - player.State.Position.Y;
            if (Math.Abs(dy) > 40)
            {
                float tick = dy > 0 ? -9f : 9f;
                DrawLine(edge + perp * 15f, edge + perp * 15f + new Vector2(0, tick), color, 2f, true);
            }

            DrawString(_font, edge + new Vector2(-16, 26), $"{range:F0}m",
                       HorizontalAlignment.Center, 32, 10, color);
        }
    }

    /// <summary>
    /// How far from the centre a marker sits, so it lands on the screen BORDER
    /// rather than on a circle inscribed in it.
    ///
    /// The circle wasted the whole width of a 16:9 screen, and worse, it squeezed
    /// contacts together: a flight all off to one side arrived as three markers
    /// stacked on the same spot, which reads as one aircraft. Pushing them out to
    /// the real edge spreads the same angles across far more pixels.
    /// </summary>
    private static float BorderDistance(Vector2 size, Vector2 toward, float margin)
    {
        float halfX = Math.Max(1f, size.X * 0.5f - margin);
        float halfY = Math.Max(1f, size.Y * 0.5f - margin);

        float tx = Math.Abs(toward.X) > 1e-4f ? halfX / Math.Abs(toward.X) : float.MaxValue;
        float ty = Math.Abs(toward.Y) > 1e-4f ? halfY / Math.Abs(toward.Y) : float.MaxValue;

        return Math.Min(tx, ty);
    }

    // --- Warnings -----------------------------------------------------------

    private void DrawWarnings(Vector2 size)
    {
        var s = _sim.Player.State;
        float y = size.Y * 0.30f;

        if (_sim.Outcome != RoundOutcome.InProgress)
        {
            bool won = _sim.Outcome == RoundOutcome.TeamZeroWins;
            var flight = _flight();
            string victory = flight is not null && flight.Members.Count > 1 ? "FLIGHT DESTROYED" : "ENEMY DOWN";

            Banner(size, y, won ? victory : _sim.Outcome == RoundOutcome.Draw ? "NO RESULT" : "YOU ARE DOWN",
                   won ? Good : Danger, 38);

            if (!won && s.Death != DeathCause.None)
                Banner(size, y + 38, s.Death switch
                {
                    DeathCause.Ground => "flew into the ground",
                    DeathCause.Fled => "left the field",
                    DeathCause.Fire => "burned",
                    DeathCause.StructuralFailure => "the airframe came apart",
                    DeathCause.Gunfire => "shot down",
                    _ => "",
                }, Dim, 15);

            Banner(size, y + 62, $"rounds fired {_sim.Player.Spec.AmmoRounds - s.Ammo}   " +
                                 $"hits {_sim.Player.Combatant.HitsScored}", Dim, 13);
            Banner(size, y + 84, "R to fly again    1 / 2 / 3 skill    F6 enemies    F7 wingmen", Dim, 13);
            return;
        }

        if (!s.IsAlive)
        {
            // Dead, but the round is not over: your side still has somebody up.
            // The sim keeps running now, so this is worth watching rather than a
            // frozen screen waiting for a keypress.
            Banner(size, y, "YOU ARE DOWN", Danger, 34);
            Banner(size, y + 34, "your flight fights on", Dim, 14);
            Banner(size, y + 56, "R to fly again", Dim, 13);
            return;
        }

        // Downing one of them buys a few seconds while the rest go high and regroup.
        // Say so: it is the only window in a flight engagement where you get to
        // choose what happens next instead of answering somebody else's choice.
        if (_flight() is { IsShaken: true, AliveCount: > 0 })
        {
            Banner(size, size.Y * 0.24f, "THEY BREAK OFF", Good, 22);
            Banner(size, size.Y * 0.24f + 22, "climb, run, or reload the situation", Dim, 12);
        }

        if (s.IsFlatTurning)
        {
            // The whole point of the maneuver is that this second is dangerous.
            // Say so, and draw the clock running down.
            Banner(size, y, "REVERSING", Warn, 26);
            Banner(size, y + 26, "guns masked", Danger, 13);

            const float barW = 190f;
            float barX = (size.X - barW) * 0.5f;
            float barY = y + 44;
            DrawRect(new Rect2(barX, barY, barW, 5), new Color(Dim, 0.35f), true);
            DrawRect(new Rect2(barX, barY, barW * (float)s.FlatTurnProgress, 5), Warn, true);
            y += 60;
        }

        if (s.GunJammed)
        {
            // Tell the pilot what to do about it. The mechanic existed for a while
            // with nothing bound to it and no prompt, which made a jam read as a
            // permanent loss of the guns rather than something to fight through.
            Banner(size, y, "GUNS JAMMED", Danger, 26);
            Banner(size, y + 26, "hammer  X  /  right bumper", Warn, 14);

            const float barW = 210f;
            float barX = (size.X - barW) * 0.5f;
            float barY = y + 46;
            DrawRect(new Rect2(barX, barY, barW, 6), new Color(Dim, 0.35f), true);
            DrawRect(new Rect2(barX, barY, barW * (float)s.JamClearProgress, 6), Good, true);
            y += 64;
        }

        // The dive limit. It has to be loud, because unlike a stall there is no
        // seat-of-the-pants cue for it on a screen, and the ending is abrupt.
        if (s.IsOverspeed)
        {
            bool severe = s.OverspeedStress > 0.45;
            Banner(size, y, "OVERSPEED", severe ? Danger : Warn, 26);
            Banner(size, y + 26, "EASE OFF OR THE WINGS GO", severe ? Danger : Dim, 13);

            const float barW = 220f;
            float barX = (size.X - barW) * 0.5f;
            DrawRect(new Rect2(barX, y + 44, barW, 6), new Color(Dim, 0.35f), true);
            DrawRect(new Rect2(barX, y + 44, barW * (float)s.OverspeedStress, 6),
                     severe ? Danger : Warn, true);
            y += 62;
        }

        if (s.IsSpinning) { Banner(size, y, "SPIN", Danger, 28); y += 30; }
        else if (s.IsStalled) { Banner(size, y, "STALL", Warn, 24); y += 26; }

        if (s.FuelStarvation > 0.01)
            Banner(size, y, $"FUEL STARVING  {(1 - s.FuelStarvation) * 100:F0}% POWER", Warn, 16);

        if (s.OutOfBoundsTime > 0.1)
            Banner(size, size.Y * 0.20f, "RETURN TO THE FIELD", Danger, 20);
    }

    private void Banner(Vector2 size, float y, string text, Color color, int fontSize)
        => DrawString(_font, new Vector2(0, y), text, HorizontalAlignment.Center, size.X, fontSize, color);

    // --- Frame graph --------------------------------------------------------

    /// <summary>
    /// Smooth motion is a stated goal, so it gets a permanent readout rather than a
    /// vibe check. Green means inside the budget for this refresh rate.
    /// </summary>
    private void DrawFrameGraph(Vector2 size)
    {
        const float w = 236, h = 62;
        float x0 = 14, y0 = size.Y - h - 14;

        DrawRect(new Rect2(x0, y0, w, h), Panel, true);

        float budget = 1000f / Math.Max(30, DisplayServer.ScreenGetRefreshRate());
        float scale = h / (budget * 2.5f);

        // The budget line. Bars under it are frames that made it.
        float budgetY = y0 + h - budget * scale;
        DrawLine(new Vector2(x0, budgetY), new Vector2(x0 + w, budgetY), new Color(Dim, 0.6f), 1f);

        float barW = w / FrameSamples;
        float worst = 0, total = 0;
        for (int i = 0; i < FrameSamples; i++)
        {
            float ms = _frameMs[(_frameIndex + i) % FrameSamples];
            worst = Math.Max(worst, ms);
            total += ms;

            float barH = Math.Min(h - 2, ms * scale);
            var color = ms <= budget * 1.05f ? Good : ms <= budget * 2f ? Warn : Danger;
            DrawRect(new Rect2(x0 + i * barW, y0 + h - barH, barW, barH), new Color(color, 0.8f), true);
        }

        float avg = total / FrameSamples;
        DrawString(_font, new Vector2(x0 + 8, y0 + 15),
                   $"{(avg > 0 ? 1000f / avg : 0):F0} fps   avg {avg:F2} ms   worst {worst:F2} ms",
                   HorizontalAlignment.Left, -1, 11, Ink);
        DrawString(_font, new Vector2(x0 + 8, y0 + h - 6),
                   $"sim {_sim.LastStepMs:F3} ms/tick   tick {_sim.Tick}",
                   HorizontalAlignment.Left, -1, 10, Dim);
    }
}
