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
    private Font _font = null!;

    private readonly float[] _frameMs = new float[FrameSamples];
    private int _frameIndex;
    public bool ShowDebug { get; set; } = true;

    public static Hud Create(SimRunner sim, ChaseCamera camera, PlayerInput input, Func<AiSkill> skill) => new()
    {
        Name = "Hud",
        _sim = sim,
        _camera = camera,
        _input = input,
        _skill = skill,
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

    public override void _Draw()
    {
        var size = Size;
        DrawTelemetry();
        DrawReticle(size);
        DrawOffscreenMarkers(size);
        if (ShowDebug) DrawFrameGraph(size);
        DrawWarnings(size);
    }

    // --- Telemetry ----------------------------------------------------------

    private void DrawTelemetry()
    {
        var s = _sim.Player.State;
        var spec = _sim.Player.Spec;

        DrawRect(new Rect2(14, 14, 236, 330), Panel, true);

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
        Row("ENEMY", _skill().Name, ref y, Dim);
        Row("MODE", _input.ClassicMode ? "CLASSIC 8-WAY" : "analog", ref y, Dim);

        void Row(string label, string value, ref float rowY, Color color)
        {
            DrawString(_font, new Vector2(26, rowY), label, HorizontalAlignment.Left, -1, 11, Dim);
            DrawString(_font, new Vector2(238, rowY), value, HorizontalAlignment.Right, 0, 13, color);
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
    /// </summary>
    private void DrawOffscreenMarkers(Vector2 size)
    {
        var player = _sim.Player;
        Vector2 camCenter = _camera.CenterM;
        double halfW = _camera.VisibleWidthM * 0.5;
        double halfH = _camera.VisibleHeightM * 0.5;
        const float margin = 26f;

        foreach (var other in _sim.Aircraft)
        {
            if (other == player || other.Team == player.Team || !other.State.IsAlive) continue;

            var p = other.State.Position;
            bool onScreen = Math.Abs(p.X - camCenter.X) < halfW && Math.Abs(p.Y - camCenter.Y) < halfH;
            if (onScreen) continue;

            var toward = new Vector2((float)(p.X - camCenter.X), -(float)(p.Y - camCenter.Y)).Normalized();
            Vector2 edge = size * 0.5f + toward * (Math.Min(size.X, size.Y) * 0.5f - margin);

            double range = (p - player.State.Position).Length;
            float alpha = (float)Math.Clamp(1.2 - range / 2200.0, 0.30, 1.0);
            var color = new Color(Danger, alpha);

            // Triangle pointing outward, toward the contact.
            Vector2 perp = new(-toward.Y, toward.X);
            DrawColoredPolygon(
                new[] { edge + toward * 11f, edge - toward * 7f + perp * 8f, edge - toward * 7f - perp * 8f },
                color);

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

    // --- Warnings -----------------------------------------------------------

    private void DrawWarnings(Vector2 size)
    {
        var s = _sim.Player.State;
        float y = size.Y * 0.30f;

        if (_sim.Outcome != RoundOutcome.InProgress)
        {
            bool won = _sim.Outcome == RoundOutcome.TeamZeroWins;
            Banner(size, y, won ? "ENEMY DOWN" : _sim.Outcome == RoundOutcome.Draw ? "NO RESULT" : "YOU ARE DOWN",
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
            Banner(size, y + 84, "R to fly again        1 / 2 / 3 to change the opponent", Dim, 13);
            return;
        }

        if (!s.IsAlive) return;

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
