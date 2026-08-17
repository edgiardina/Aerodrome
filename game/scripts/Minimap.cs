using System;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// The minimap is a SIDE ELEVATION, not a top-down map.
///
/// All gameplay happens on one vertical plane, so a scaled-down copy of the arena
/// is the correct map. It is the same projection as Far View, only smaller, which
/// makes it cheap to build and means the two views agree with each other.
///
/// The arena runs about seven screens wide, so most of a fight happens off screen.
/// Without this the game is unfair.
/// </summary>
public sealed partial class Minimap : Control
{
    private const float PanelWidth = 300f;
    private const float Margin = 14f;

    private static readonly Color Backdrop = new(0.05f, 0.06f, 0.07f, 0.66f);
    private static readonly Color Border = new(0.42f, 0.46f, 0.44f, 0.9f);
    private static readonly Color GroundLine = new(0.36f, 0.30f, 0.22f, 0.95f);
    private static readonly Color CeilingLine = new(0.30f, 0.38f, 0.48f, 0.7f);
    private static readonly Color ViewportBox = new(0.95f, 0.93f, 0.75f, 0.95f);
    private static readonly Color PlayerMark = new(0.55f, 0.82f, 1.0f);
    private static readonly Color EnemyMark = new(0.95f, 0.35f, 0.28f);

    private SimRunner _sim = null!;
    private ChaseCamera _camera = null!;
    private Arena _arena = null!;
    private Func<Flight?> _flight = null!;

    public static Minimap Create(SimRunner sim, ChaseCamera camera, Func<Flight?> flight) => new()
    {
        Name = "Minimap",
        _sim = sim,
        _camera = camera,
        _arena = sim.Arena,
        _flight = flight,
        MouseFilter = MouseFilterEnum.Ignore,
        AnchorRight = 1,
        AnchorBottom = 1,
    };

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        float aspect = (float)(_arena.WidthM / _arena.CeilingM);
        float w = PanelWidth;
        float h = w / aspect;

        // Top right corner, out of the way of the telemetry panel.
        var origin = new Vector2(Size.X - w - Margin, Margin);
        var rect = new Rect2(origin, new Vector2(w, h));

        DrawRect(rect, Backdrop, true);
        DrawRect(rect, Border, false, 1.0f);

        // Ground and ceiling read as the same lines they are in the world.
        DrawLine(new Vector2(rect.Position.X, rect.End.Y), rect.End, GroundLine, 2f);
        DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), CeilingLine, 1f);

        DrawViewportBox(rect);

        foreach (var a in _sim.Aircraft)
        {
            if (!a.State.IsAlive) continue;
            if (!IsDetected(a)) continue;
            DrawContact(rect, a);
        }
    }

    /// <summary>
    /// The highlighted rectangle for what is currently on screen. It slides as the
    /// camera scrolls and resizes as the camera zooms.
    /// </summary>
    private void DrawViewportBox(Rect2 rect)
    {
        Vector2 center = _camera.CenterM;
        var half = new Vector2((float)(_camera.VisibleWidthM * 0.5), (float)(_camera.VisibleHeightM * 0.5));

        Vector2 topLeft = ToMap(rect, new Vector2(center.X - half.X, center.Y + half.Y));
        Vector2 bottomRight = ToMap(rect, new Vector2(center.X + half.X, center.Y - half.Y));

        // Far View shows slightly more than the whole arena, so the box would spill
        // outside the map and draw a stray line across the screen.
        var box = new Rect2(topLeft, bottomRight - topLeft).Abs().Intersection(rect);
        if (box.Size.X <= 0 || box.Size.Y <= 0) return;

        DrawRect(box, new Color(ViewportBox, 0.10f), true);
        DrawRect(box, ViewportBox, false, 1.4f);
    }

    private void DrawContact(Rect2 rect, SimAircraft a)
    {
        bool isPlayer = a.Team == Team.Player;
        bool pressing = !isPlayer && ReferenceEquals(_flight()?.Engaged, a.Combatant);

        Color color = isPlayer ? PlayerMark : pressing ? EnemyMark : new Color(EnemyMark, 0.55f);
        Vector2 p = ToMap(rect, new Vector2((float)a.State.Position.X, (float)a.State.Position.Y));

        float radius = isPlayer ? 3.4f : 2.8f;
        DrawCircle(p, radius, color);

        // Ring the one that is actually attacking. The rest are holding a perch,
        // and the map has to say which is which or three dots read as three threats.
        if (pressing) DrawArc(p, 6.5f, 0, Mathf.Tau, 16, EnemyMark, 1.3f, true);

        // A facing tick, so the map shows which way each contact is pointed.
        var nose = new Vector2((float)Math.Cos(a.State.Theta), -(float)Math.Sin(a.State.Theta));
        DrawLine(p, p + nose * 8f, color, 1.4f, true);
    }

    /// <summary>
    /// Night and heavy rain cut how far you can see, and the map has to agree with
    /// the weather or it becomes an all-seeing cheat.
    /// </summary>
    private bool IsDetected(SimAircraft a)
    {
        if (a.Team == Team.Player) return true;
        if (double.IsPositiveInfinity(_arena.ContactRangeM)) return true;
        return (a.State.Position - _sim.Player.State.Position).Length <= _arena.ContactRangeM;
    }

    private Vector2 ToMap(Rect2 rect, Vector2 world) => new(
        rect.Position.X + (float)(world.X / _arena.WidthM) * rect.Size.X,
        rect.End.Y - (float)(world.Y / _arena.CeilingM) * rect.Size.Y);
}
