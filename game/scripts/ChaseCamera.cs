using System;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// The scrolling camera. The arena is several screens wide, so how this moves is a
/// large part of how the game feels.
///
/// Two rules that matter more than the rest:
///   1. It updates in the render step and reads INTERPOLATED aircraft positions.
///      A camera that reads raw sim state judders at any refresh rate above the
///      sim rate, and that is the usual cause of a "not smooth" scrolling game.
///   2. Smoothing is an exponential decay with a time constant in seconds, not a
///      per-frame lerp with a fixed alpha. A fixed alpha is framerate dependent.
/// </summary>
public sealed partial class ChaseCamera : Camera3D
{
    /// <summary>
    /// Visible arena width at rest, in meters.
    ///
    /// Deliberately tight. A wide view showed the enemy long before they were a
    /// threat, which drained the tension out of a merge and made the minimap and
    /// the screen-edge markers decorative. At 250 m you see them arrive, the
    /// aircraft is big enough to read its attitude, and knowing where the other
    /// one is becomes a thing you have to work at.
    /// </summary>
    /// Static rather than const so the live tuning panel can move it while you fly.
    /// How close the camera sits is a gameplay decision, not a constant.
    public static double NearViewWidthM = 210.0;

    /// <summary>
    /// Widest the duel framing may pull back before it stops trying.
    ///
    /// Kept close to the resting width on purpose. The old 520 meant that any time
    /// the other aeroplane was anywhere near, the view doubled in size and the
    /// aircraft became specks, which is the opposite of what a close fight wants.
    /// The camera should sit at its tightest nearly all the time and give up a
    /// little of that only when both aircraft genuinely will not fit.
    /// </summary>
    public static double MaxDuelWidthM = 300.0;
    /// <summary>Range within which the camera frames both fighters instead of just the player.</summary>
    public static double FramingRangeM = 330.0;

    /// <summary>
    /// How wide a band the duel framing fades in over, ending at FramingRangeM.
    /// Without it the two framings swap instantly and the view pumps.
    /// </summary>
    public static double BlendBandM = 130.0;
    /// <summary>Meters the player may drift before the camera starts to chase.</summary>
    public const double DeadzoneM = 9.0;

    private const double PositionTau = 0.13;
    private const double ZoomTau = 0.28;
    private const float VerticalFovDeg = 30f;

    private Arena _arena = null!;
    private SimRunner _sim = null!;

    private Vector2 _center;
    private double _visibleWidth = NearViewWidthM;
    private Vector2 _targetCenter;
    private double _targetWidth = NearViewWidthM;

    public bool FarView { get; private set; }

    /// <summary>Capture-only zoom override, in meters of visible width. Null is normal.</summary>
    public double? ForcedWidthM { get; set; }

    /// <summary>Capture-only look-at override, so a shot can frame something else.</summary>
    public Vector2? ForcedCenterM { get; set; }

    public double VisibleWidthM => _visibleWidth;
    public double VisibleHeightM => _visibleWidth / Aspect;
    public Vector2 CenterM => _center;

    private float Aspect
    {
        get
        {
            var size = GetViewport().GetVisibleRect().Size;
            return size.Y > 0 ? size.X / size.Y : 16f / 9f;
        }
    }

    public static ChaseCamera Create(SimRunner sim, Arena arena)
    {
        var cam = new ChaseCamera
        {
            Name = "ChaseCamera",
            _sim = sim,
            _arena = arena,
            Fov = VerticalFovDeg,
            KeepAspect = KeepAspectEnum.Height,
            Near = 1.0f,
            Far = 60000.0f,
            Current = true,
        };

        var p = sim.Player.State.Position;
        cam._center = new Vector2((float)p.X, (float)p.Y);
        cam._targetCenter = cam._center;
        return cam;
    }

    public void ToggleFarView() => FarView = !FarView;

    public void Render(double alpha, double dt)
    {
        ComputeTargets(alpha);

        // Exponential approach. Framerate independent, and it never overshoots.
        double kPos = 1.0 - Math.Exp(-dt / PositionTau);
        double kZoom = 1.0 - Math.Exp(-dt / ZoomTau);

        _center += (_targetCenter - _center) * (float)kPos;
        _visibleWidth += (_targetWidth - _visibleWidth) * kZoom;

        ClampToArena();
        ApplyTransform();
    }

    private void ComputeTargets(double alpha)
    {
        if (ForcedWidthM is { } forced)
        {
            var prs0 = _sim.Player.Interpolated(alpha);
            _targetWidth = forced;
            _targetCenter = ForcedCenterM ?? new Vector2((float)prs0.X, (float)prs0.Y);
            return;
        }

        if (FarView)
        {
            // Far View overrides everything and shows the whole box.
            _targetWidth = Math.Max(_arena.WidthM, _arena.CeilingM * Aspect) * 1.06;
            _targetCenter = new Vector2((float)(_arena.WidthM * 0.5), (float)(_arena.CeilingM * 0.5));
            return;
        }

        var player = _sim.Player;
        var prs = player.Interpolated(alpha);
        var playerPos = new Vector2((float)prs.X, (float)prs.Y);

        // Solo framing: look where you are FLYING, not where the nose happens to be
        // pointed. Those are different, and the difference grew the day the
        // elevator got quick: the nose can now swing most of a right angle in a
        // tenth of a second, and hanging a sixty metre lead vector off it threw the
        // camera across the arena every time the pilot twitched. The flight path is
        // what the camera should follow, and it is smooth by construction.
        var velocity = new Vector2((float)prs.VelocityX, (float)prs.VelocityY);
        float speedFraction = Math.Min(1f, velocity.Length() / 75f);

        // The lead is a fraction of the visible extent IN EACH AXIS, not a single
        // distance. The screen is 16:9, so a lead sized off the width is nearly the
        // whole half-height once it points upward, and a full-power dive shoved the
        // aircraft clean off the top of the frame. Half way to the edge is plenty.
        var dir = velocity.Normalized();
        float halfW = (float)(_visibleWidth * 0.5);
        float halfH = (float)(VisibleHeightM * 0.5);
        Vector2 lead = new Vector2(dir.X * halfW, dir.Y * halfH) * (0.5f * speedFraction);

        Vector2 desired = playerPos + lead;
        double width = NearViewWidthM;

        var opponent = _sim.NearestOpponent(player);

        if (opponent is not null)
        {
            var ors = opponent.Interpolated(alpha);
            var oppPos = new Vector2((float)ors.X, (float)ors.Y);
            float separation = playerPos.DistanceTo(oppPos);

            // Cross-fade into duel framing rather than switching to it.
            //
            // A hard threshold here was the second source of bounce, and the worst
            // of them. At one metre outside the range the camera wanted 250 m of
            // width; one metre inside it wanted 520. A dogfight sits on top of that
            // boundary and crosses it several times a second, so the view pumped in
            // and out by a factor of two the whole time.
            float blend = 1f - Smoothstep((float)(FramingRangeM - BlendBandM), (float)FramingRangeM, separation);

            if (blend > 0f)
            {
                // Duel framing. Bias toward the player so the fight never drifts to
                // the edge of the screen while the enemy hogs the middle.
                Vector2 duel = playerPos.Lerp(oppPos, 0.4f);

                // Max() rather than the bare constant: the tuning panel can push the
                // resting width past the duel limit, and Clamp throws if it does.
                double duelWidth = Math.Clamp(separation * 1.4 + 60.0,
                                              NearViewWidthM, Math.Max(MaxDuelWidthM, NearViewWidthM));

                desired = desired.Lerp(duel, blend);
                width = width + (duelWidth - width) * blend;
            }
        }

        _targetWidth = width;
        ApplyDeadzone(desired);
    }

    private static float Smoothstep(float from, float to, float value)
    {
        if (to - from < 1e-6f) return value >= to ? 1f : 0f;
        float t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Ignore small drifts so gentle maneuvers do not jitter the whole screen.
    ///
    /// The slack is measured from where the camera IS, and once the aircraft is
    /// outside it the target tracks CONTINUOUSLY, holding the deadzone as a slack
    /// radius behind it.
    ///
    /// Both of those matter, and the first version got both wrong. It compared the
    /// new position against the last TARGET, and on exceeding the threshold it
    /// snapped the target onto the aircraft. That produced a loop: the target
    /// jumps nine metres, the aircraft is now inside the deadzone of the new
    /// target so the target freezes, the camera eases in and stops, the aircraft
    /// drifts another nine metres, and it jumps again. At 240 km/h that is about
    /// eight lurches a second, which is exactly the bouncing this was supposed to
    /// prevent.
    /// </summary>
    private void ApplyDeadzone(Vector2 desired)
    {
        Vector2 offset = desired - _center;
        float distance = offset.Length();

        if (distance <= DeadzoneM)
        {
            _targetCenter = _center;
            return;
        }

        _targetCenter = desired - offset / distance * (float)DeadzoneM;
    }

    /// <summary>
    /// Never show past the walls in Near View. When the camera reaches a wall the
    /// framing slides along it instead of revealing empty space.
    /// </summary>
    private void ClampToArena()
    {
        double halfW = _visibleWidth * 0.5;
        double halfH = VisibleHeightM * 0.5;

        float x = _arena.WidthM > _visibleWidth
            ? (float)Math.Clamp(_center.X, halfW, _arena.WidthM - halfW)
            : (float)(_arena.WidthM * 0.5);

        float y = _arena.CeilingM > VisibleHeightM
            ? (float)Math.Clamp(_center.Y, halfH, _arena.CeilingM - halfH)
            : (float)(_arena.CeilingM * 0.5);

        _center = new Vector2(x, y);
    }

    private void ApplyTransform()
    {
        // Zoom moves the camera along Z. It never changes the field of view, because
        // a field-of-view change would distort the aircraft shape.
        double distance = VisibleHeightM * 0.5 / Math.Tan(Mathf.DegToRad(VerticalFovDeg) * 0.5);
        Position = new Vector3(_center.X, _center.Y, (float)distance);
    }
}
