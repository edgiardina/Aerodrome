using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// Renders one aircraft. It reads an interpolated snapshot only, never the live
/// sim state, so motion stays smooth at any refresh rate above the 120 Hz tick.
/// </summary>
public sealed partial class AircraftView : Node3D
{
    /// <summary>
    /// How much bigger than life to draw the aircraft. A Camel is 5.7 m and the
    /// turn radius is about 33 m, so at true scale it is a speck. Arcade flight
    /// games have always cheated this. Tune it in M1 alongside the flight feel.
    /// </summary>
    public const float VisualScale = 3.5f;

    /// <summary>Visible arena width, in meters, past which the model becomes an icon.</summary>
    public const double IconModeWidthM = 1100.0;

    // Control surface travel, in radians.
    private const float AileronTravel = 0.42f;   // 24 degrees
    private const float ElevatorTravel = 0.38f;  // 22 degrees
    private const float RudderTravel = 0.52f;    // 30 degrees

    private BiplaneFactory.Parts _parts = null!;
    private Node3D _icon = null!;

    public SimAircraft Aircraft { get; private set; } = null!;

    public static AircraftView Create(SimAircraft aircraft, Color teamColor)
    {
        var view = new AircraftView { Name = $"View_{aircraft.Callsign}", Aircraft = aircraft };
        view._parts = BiplaneFactory.Build(teamColor);
        view._icon = BiplaneFactory.BuildIcon(teamColor);
        view.AddChild(view._parts.Root);
        view.AddChild(view._icon);
        return view;
    }

    /// <summary>Called from the render step with the frame's interpolation factor.</summary>
    public void Render(double alpha, double visibleWidthM)
    {
        var rs = Aircraft.Interpolated(alpha);

        Visible = rs.IsAlive;
        if (!rs.IsAlive) return;

        // Heading rotates about Z. Roll rotates about the aircraft's own long axis.
        // Applying roll first and heading second is what makes a half loop come out
        // inverted while the roll state is untouched.
        //
        // Yaw rotates about world Y, which swings the nose through the screen depth.
        // It is only non-zero during a flat turn. At 90 degrees the aircraft points
        // straight away from the camera, which is exactly when its guns cannot bear.
        var yaw = new Basis(Vector3.Up, (float)rs.Yaw);
        var heading = new Basis(Vector3.Back, (float)rs.Theta);
        var roll = new Basis(Vector3.Right, (float)rs.Roll);
        Basis basis = yaw * heading * roll;

        bool iconMode = visibleWidthM > IconModeWidthM;
        _parts.Root.Visible = !iconMode;
        _icon.Visible = iconMode;
        if (!iconMode) ApplyControlSurfaces(rs);

        float scale = iconMode
            // Hold a constant apparent size once zoomed out, so contacts stay readable.
            ? (float)(visibleWidthM / 90.0)
            : VisualScale;

        Transform = new Transform3D(basis.Scaled(new Vector3(scale, scale, scale)),
                                    new Vector3((float)rs.X, (float)rs.Y, 0f));

        _parts.Propeller.Basis = new Basis(Vector3.Right, (float)rs.PropAngle);
    }

    /// <summary>
    /// Deflect the surfaces. Ailerons work against each other, the elevator swings
    /// its trailing edge up when the pilot pulls, and the rudder kicks sideways.
    /// It is a small thing that does a lot: without it a flat turn looks like a
    /// model on a turntable rather than an aircraft being flown round.
    /// </summary>
    private void ApplyControlSurfaces(in RenderState rs)
    {
        float aileron = (float)rs.Aileron * AileronTravel;
        _parts.AileronLeft.Rotation = new Vector3(0, 0, aileron);
        _parts.AileronRight.Rotation = new Vector3(0, 0, -aileron);

        // The surface hangs aft along -X, so a negative rotation about Z lifts its
        // trailing edge. Pulling means elevator up.
        _parts.Elevator.Rotation = new Vector3(0, 0, -(float)rs.Elevator * ElevatorTravel);

        _parts.Rudder.Rotation = new Vector3(0, (float)rs.Rudder * RudderTravel, 0);
    }
}
