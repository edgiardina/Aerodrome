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
    /// <summary>Visible arena width at rest, in meters.</summary>
    public const double NearViewWidthM = 380.0;
    /// <summary>Widest the duel framing may pull back before it stops trying.</summary>
    public const double MaxDuelWidthM = 820.0;
    /// <summary>Range within which the camera frames both fighters instead of just the player.</summary>
    public const double FramingRangeM = 560.0;
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

        var opponent = _sim.NearestOpponent(player);
        Vector2 desired;

        if (opponent is not null)
        {
            var ors = opponent.Interpolated(alpha);
            var oppPos = new Vector2((float)ors.X, (float)ors.Y);
            float separation = playerPos.DistanceTo(oppPos);

            if (separation < FramingRangeM)
            {
                // Duel framing. Bias toward the player so the fight never drifts to
                // the edge of the screen while the enemy hogs the middle.
                desired = playerPos.Lerp(oppPos, 0.4f);
                _targetWidth = Math.Clamp(separation * 2.1 + 90.0, NearViewWidthM, MaxDuelWidthM);
                ApplyDeadzone(desired);
                return;
            }
        }

        // Solo lead. Look where you are flying, not where you have been.
        var velocity = new Vector2(
            (float)(Math.Cos(prs.Theta) * prs.Airspeed),
            (float)(Math.Sin(prs.Theta) * prs.Airspeed));
        float speedFraction = Math.Min(1f, velocity.Length() / 75f);
        Vector2 lead = velocity.Normalized() * (float)(NearViewWidthM * 0.25 * speedFraction);

        _targetWidth = NearViewWidthM;
        ApplyDeadzone(playerPos + lead);
    }

    /// <summary>Ignore small drifts so gentle maneuvers do not jitter the whole screen.</summary>
    private void ApplyDeadzone(Vector2 desired)
    {
        if (desired.DistanceTo(_targetCenter) > DeadzoneM)
            _targetCenter = desired;
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
