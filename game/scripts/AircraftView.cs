using System;
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

    /// <summary>How long a single shot lights the muzzle, in seconds.</summary>
    private const double FlashSeconds = 0.045;

    private BiplaneFactory.Parts _parts = null!;
    private Node3D _icon = null!;
    private Node3D _muzzle = null!;
    private OmniLight3D _muzzleLight = null!;

    private int _lastAmmo = int.MaxValue;
    private double _flashFor;

    public SimAircraft Aircraft { get; private set; } = null!;

    public static AircraftView Create(SimAircraft aircraft, Color teamColor)
    {
        var view = new AircraftView { Name = $"View_{aircraft.Callsign}", Aircraft = aircraft };
        view._parts = BiplaneFactory.Build(teamColor, aircraft.Spec.ModelName);
        view._icon = BiplaneFactory.BuildIcon(teamColor);
        view.AddChild(view._parts.Root);
        view.AddChild(view._icon);

        // Hung off the airframe rather than the view, so it rides the model's own
        // orientation and points where the guns point.
        view._muzzle = BuildMuzzleFlash();
        view._muzzleLight = BuildMuzzleLight();
        view._parts.Root.AddChild(view._muzzle);
        view._parts.Root.AddChild(view._muzzleLight);

        return view;
    }

    /// <summary>
    /// Light the guns when a round leaves them.
    ///
    /// Worth more than decoration on both ends. Your own tracers say where the fire
    /// went, but the flash says the trigger is actually down, which matters when
    /// the guns can jam or be masked. And an enemy's flash is the first thing that
    /// tells you that you are being shot at rather than merely followed.
    ///
    /// Driven off the ammunition count going down, which is the one signal that a
    /// round really left the gun.
    /// </summary>
    public override void _Process(double delta)
    {
        var s = Aircraft.State;

        if (s.Ammo < _lastAmmo && s.IsAlive) _flashFor = FlashSeconds;
        _lastAmmo = s.Ammo;

        _flashFor = Math.Max(0.0, _flashFor - delta);

        bool lit = _flashFor > 0.0 && _parts.Root.Visible;
        _muzzle.Visible = lit;
        _muzzleLight.Visible = lit;

        if (!lit) return;

        // Fades over its own short life, so a burst reads as a rapid flicker rather
        // than one steady lamp.
        float t = (float)(_flashFor / FlashSeconds);
        float size = 0.65f + 0.55f * t;

        _muzzle.Scale = new Vector3(size, size, size);
        _muzzleLight.LightEnergy = 4.5f * t;
    }

    private static Node3D BuildMuzzleFlash()
    {
        var root = new Node3D { Name = "MuzzleFlash", Visible = false };

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoColor = new Color(1.0f, 0.80f, 0.42f, 0.95f),
            DisableReceiveShadows = true,
        };

        // Twin Vickers, side by side on the cowl ahead of the cockpit. Model space
        // is unscaled metres with the nose at +X, so these are real gun positions.
        for (int i = -1; i <= 1; i += 2)
            root.AddChild(new MeshInstance3D
            {
                Name = $"Flash{i}",
                Mesh = new QuadMesh { Size = new Vector2(0.62f, 0.38f) },
                Position = new Vector3(1.95f, 0.52f, i * 0.17f),
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });

        return root;
    }

    private static OmniLight3D BuildMuzzleLight() => new()
    {
        Name = "MuzzleLight",
        Position = new Vector3(2.1f, 0.52f, 0f),
        LightColor = new Color(1.0f, 0.74f, 0.36f),
        OmniRange = 11f,
        LightEnergy = 0f,
        ShadowEnabled = false,
        Visible = false,
    };

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
