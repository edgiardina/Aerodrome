using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// Smoke and fire trailing from a hurt aircraft.
///
/// This is not decoration, it is the damage readout. The design has no health bar
/// on purpose, so how bad off an aircraft is has to be legible from the outside:
/// a thin grey streak means it took a few rounds, thick black means the engine is
/// going, and flame means it has seconds left. It works on the enemy too, which is
/// how you know whether to press an attack or break off.
/// </summary>
public sealed partial class DamageEffects : Node3D
{
    /// <summary>Damage below which nothing shows. A few rounds should not smoke.</summary>
    private const double SmokeThreshold = 0.18;

    private GpuParticles3D _smoke = null!;
    private GpuParticles3D _fire = null!;
    private OmniLight3D _fireLight = null!;

    public static DamageEffects Create()
    {
        var node = new DamageEffects { Name = "DamageEffects" };

        node._smoke = BuildSmoke();
        node._fire = BuildFire();
        node._fireLight = new OmniLight3D
        {
            Name = "FireGlow",
            LightColor = new Color(1.0f, 0.55f, 0.22f),
            OmniRange = 26f,
            LightEnergy = 0f,
            ShadowEnabled = false,
        };

        node.AddChild(node._smoke);
        node.AddChild(node._fire);
        node.AddChild(node._fireLight);
        return node;
    }

    /// <summary>
    /// Drive the trails from the aircraft's condition. Called each rendered frame
    /// with the aircraft's world position, so the particles stay in world space and
    /// the trail is left behind rather than dragged along.
    /// </summary>
    public void Render(AircraftState state, Vector3 worldPosition)
    {
        Position = worldPosition;

        bool alive = state.IsAlive;
        double damage = state.VisibleDamage;

        // The engine is the thing that smokes, so weight it heavily, but a generally
        // shot-up airframe streams too.
        double engineHurt = 1.0 - state.EngineHealth;
        double intensity = Mathf.Clamp((float)(engineHurt * 0.75 + damage * 0.55), 0f, 1f);

        bool smoking = alive && intensity > SmokeThreshold;
        _smoke.Emitting = smoking;
        if (smoking)
        {
            _smoke.AmountRatio = Mathf.Clamp((float)((intensity - SmokeThreshold) / (1.0 - SmokeThreshold)), 0.08f, 1f);

            // Grey wisp when lightly hurt, oily black when the engine is finished.
            float soot = Mathf.Clamp((float)(engineHurt * 1.2f), 0f, 1f);
            var material = (StandardMaterial3D)_smoke.MaterialOverride;
            material.AlbedoColor = new Color(
                Mathf.Lerp(0.62f, 0.09f, soot),
                Mathf.Lerp(0.62f, 0.08f, soot),
                Mathf.Lerp(0.63f, 0.08f, soot),
                Mathf.Lerp(0.30f, 0.72f, soot));
        }

        bool burning = alive && state.OnFire;
        _fire.Emitting = burning;
        _fireLight.LightEnergy = burning ? 2.4f + Mathf.Sin(Time.GetTicksMsec() * 0.02f) * 0.6f : 0f;
    }

    private static GpuParticles3D BuildSmoke()
    {
        var process = new ParticleProcessMaterial
        {
            Direction = new Vector3(-1, 0.25f, 0),
            Spread = 12f,
            InitialVelocityMin = 6f,
            InitialVelocityMax = 13f,
            Gravity = new Vector3(0, 3.5f, 0),      // smoke rises once it slows
            ScaleMin = 1.4f,
            ScaleMax = 3.2f,
            Damping = new Vector2(4f, 7f),
        };
        // Puffs swell as they fall behind.
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 0.35f));
        curve.AddPoint(new Vector2(1f, 1f));
        process.ScaleCurve = new CurveTexture { Curve = curve };

        return new GpuParticles3D
        {
            Name = "Smoke",
            Amount = 220,
            Lifetime = 2.4,
            Explosiveness = 0f,
            LocalCoords = false,      // leave the trail behind in the world
            DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(3.4f, 3.4f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                AlbedoColor = new Color(0.5f, 0.5f, 0.5f, 0.45f),
                DisableReceiveShadows = true,
            },
        };
    }

    private static GpuParticles3D BuildFire()
    {
        var process = new ParticleProcessMaterial
        {
            Direction = new Vector3(-1, 0.15f, 0),
            Spread = 16f,
            InitialVelocityMin = 10f,
            InitialVelocityMax = 20f,
            Gravity = new Vector3(0, 6f, 0),
            ScaleMin = 0.8f,
            ScaleMax = 1.9f,
            Damping = new Vector2(9f, 15f),
            Color = new Color(1.0f, 0.62f, 0.22f),
        };

        return new GpuParticles3D
        {
            Name = "Fire",
            Amount = 120,
            Lifetime = 0.6,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(2.6f, 2.6f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(1f, 0.6f, 0.25f, 0.85f),
                DisableReceiveShadows = true,
            },
        };
    }
}
