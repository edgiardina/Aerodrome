using System;
using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// What is left after an aircraft is shot down.
///
/// A defeated aircraft used to vanish on the tick it died, which read as a bug
/// and threw away the best moment in the game. Now it explodes, and the wreck
/// keeps its momentum, tumbles, burns, and goes into the ground.
///
/// Entirely presentation. Core stops simulating the dead, so nothing here can
/// affect the outcome of a round, and it does not need to be deterministic.
/// </summary>
public sealed partial class Wreckage : Node3D
{
    private const float GravityMps2 = 9.80665f;
    private const float DragPerSecond = 0.22f;
    private const double MaxLifeSeconds = 22.0;

    private Node3D _airframe = null!;
    private GpuParticles3D _fire = null!;
    private GpuParticles3D _smoke = null!;
    private GpuParticles3D _burst = null!;
    private OmniLight3D _flash = null!;

    private Vector3 _velocity;
    private Vector3 _tumble;
    private double _age;
    private bool _grounded;

    public static Wreckage Create(Vector3 position, Vector2 velocity, double theta, Color teamColor)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        var wreck = new Wreckage
        {
            Name = "Wreckage",
            Position = position,
            // Keep the momentum it died with, plus a shove from whatever killed it.
            _velocity = new Vector3(velocity.X, velocity.Y, 0f) * 0.72f
                        + new Vector3(rng.RandfRange(-9f, 9f), rng.RandfRange(2f, 14f), 0f),
            // Tumbling mostly in the screen plane, so the wreck stays readable.
            _tumble = new Vector3(rng.RandfRange(-1.4f, 1.4f),
                                  rng.RandfRange(-0.7f, 0.7f),
                                  rng.RandfRange(-3.4f, 3.4f)),
        };

        wreck._airframe = BiplaneFactory.Build(teamColor).Root;
        wreck._airframe.Basis = new Basis(Vector3.Back, (float)theta);
        wreck.AddChild(wreck._airframe);

        wreck._fire = BuildFire();
        wreck._smoke = BuildSmoke();
        wreck._burst = BuildBurst();
        wreck._flash = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.66f, 0.28f),
            OmniRange = 90f,
            LightEnergy = 14f,
            ShadowEnabled = false,
        };

        wreck.AddChild(wreck._fire);
        wreck.AddChild(wreck._smoke);
        wreck.AddChild(wreck._burst);
        wreck.AddChild(wreck._flash);
        return wreck;
    }

    public override void _Ready()
    {
        // The kill itself: one hard burst, then a burning tumble.
        _burst.Emitting = true;
        _fire.Emitting = true;
        _smoke.Emitting = true;
    }

    /// <summary>
    /// Falls under its own steam in the render step. Uses the frame delta rather
    /// than a fixed tick on purpose: nothing about a wreck affects the game, so it
    /// does not need to run in lockstep with the sim.
    /// </summary>
    public override void _Process(double delta)
    {
        _age += delta;
        if (_age > MaxLifeSeconds) { QueueFree(); return; }

        // The flash is the explosion, and it only lasts an instant.
        _flash.LightEnergy = Mathf.Max(0f, _flash.LightEnergy - (float)delta * 42f);

        if (_grounded) return;

        float dt = (float)delta;
        _velocity += Vector3.Down * GravityMps2 * dt;
        _velocity -= _velocity * DragPerSecond * dt;
        Position += _velocity * dt;

        // Tumble slows as it falls, the way a dead airframe settles into a flat spin.
        _airframe.Rotate(Vector3.Right, _tumble.X * dt);
        _airframe.Rotate(Vector3.Up, _tumble.Y * dt);
        _airframe.Rotate(Vector3.Back, _tumble.Z * dt);
        _tumble = _tumble.Lerp(Vector3.Zero, dt * 0.25f);

        if (Position.Y <= 0f) Impact();
    }

    private void Impact()
    {
        _grounded = true;
        Position = new Vector3(Position.X, 0.6f, Position.Z);

        // Hits the ground and goes up. The fire stays, the smoke keeps rising, and
        // the column marks where it went in.
        _burst.Restart();
        _burst.Emitting = true;
        _flash.LightEnergy = 9f;
        _fire.Emitting = true;
        _airframe.Visible = false;
    }

    // --- Effects ------------------------------------------------------------

    private static GpuParticles3D BuildBurst()
    {
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = 18f,
            InitialVelocityMax = 55f,
            Gravity = new Vector3(0, -6f, 0),
            ScaleMin = 2.5f,
            ScaleMax = 7f,
            Damping = new Vector2(18f, 34f),
            Color = new Color(1.0f, 0.72f, 0.3f),
        };

        return new GpuParticles3D
        {
            Name = "Burst",
            Amount = 90,
            Lifetime = 0.9,
            OneShot = true,
            Explosiveness = 1.0f,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(5.5f, 5.5f) },
            MaterialOverride = Additive(new Color(1f, 0.7f, 0.3f, 0.9f)),
        };
    }

    private static GpuParticles3D BuildFire()
    {
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 25f,
            InitialVelocityMin = 5f,
            InitialVelocityMax = 15f,
            Gravity = new Vector3(0, 9f, 0),
            ScaleMin = 1.2f,
            ScaleMax = 3.0f,
            Damping = new Vector2(7f, 13f),
            Color = new Color(1.0f, 0.55f, 0.16f),
        };

        return new GpuParticles3D
        {
            Name = "Fire",
            Amount = 150,
            Lifetime = 0.85,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(3.6f, 3.6f) },
            MaterialOverride = Additive(new Color(1f, 0.6f, 0.22f, 0.85f)),
        };
    }

    private static GpuParticles3D BuildSmoke()
    {
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 30f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 11f,
            Gravity = new Vector3(0, 5.5f, 0),
            // Start small and swell. Emitting at full size buried the tumbling
            // airframe inside its own plume, and the wreck falling is the shot.
            ScaleMin = 1.0f,
            ScaleMax = 2.6f,
            Damping = new Vector2(3f, 6f),
        };

        var growth = new Curve();
        growth.AddPoint(new Vector2(0f, 0.25f));
        growth.AddPoint(new Vector2(1f, 1f));
        process.ScaleCurve = new CurveTexture { Curve = growth };

        return new GpuParticles3D
        {
            Name = "Smoke",
            Amount = 200,
            Lifetime = 3.6,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(6f, 6f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                AlbedoColor = new Color(0.11f, 0.10f, 0.10f, 0.44f),
                DisableReceiveShadows = true,
            },
        };
    }

    private static StandardMaterial3D Additive(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        VertexColorUseAsAlbedo = true,
        AlbedoColor = color,
        DisableReceiveShadows = true,
    };
}
