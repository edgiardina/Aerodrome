using Aerodrome.Core;
using Godot;

namespace Aerodrome.Game;

/// <summary>
/// Draws the rounds in the air.
///
/// Only tracers are drawn, which is both cheaper and correct: every fifth round
/// was a tracer and the other four were invisible. Tracers are the whole feedback
/// channel for aiming, so they are drawn bright, unshaded, and stretched along
/// their flight path so a burst reads as a stream rather than a row of dots.
///
/// One MultiMesh for the lot. Hundreds of separate nodes would cost more in draw
/// calls than the rest of the scene put together.
/// </summary>
public sealed partial class BulletView : MultiMeshInstance3D
{
    /// <summary>How long a tracer streak is drawn, in meters.</summary>
    private const float StreakLengthM = 14f;
    private const float StreakWidthM = 0.9f;

    private static readonly Color FriendlyTracer = new(1.0f, 0.92f, 0.55f);
    private static readonly Color HostileTracer = new(1.0f, 0.55f, 0.35f);

    private BulletField _field = null!;
    private int _playerTeam;

    public static BulletView Create(BulletField field, int playerTeam)
    {
        var mesh = new BoxMesh { Size = new Vector3(1f, StreakWidthM, StreakWidthM) };

        var view = new BulletView
        {
            Name = "Tracers",
            _field = field,
            _playerTeam = playerTeam,
            CastShadow = ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                EmissionEnabled = true,
                Emission = Colors.White,
                EmissionEnergyMultiplier = 2.2f,
                DisableReceiveShadows = true,
            },
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = field.Capacity,
                VisibleInstanceCount = 0,
            },
        };

        return view;
    }

    public void Render()
    {
        var bullets = _field.Bullets;
        int drawn = 0;

        for (int i = 0; i < bullets.Length; i++)
        {
            ref readonly Bullet b = ref bullets[i];
            if (!b.Active || !b.IsTracer) continue;

            var direction = new Vector3((float)b.Velocity.X, (float)b.Velocity.Y, 0f);
            if (direction.LengthSquared() < 1e-6f) continue;
            direction = direction.Normalized();

            // Build a basis with the box's long axis along the flight path, then
            // stretch it so the streak trails behind the round.
            var up = Vector3.Back;
            var side = up.Cross(direction).Normalized();
            var basis = new Basis(direction, side.Cross(direction), side)
                .Scaled(new Vector3(StreakLengthM, 1f, 1f));

            var position = new Vector3((float)b.Position.X, (float)b.Position.Y, 0f)
                           - direction * (StreakLengthM * 0.5f);

            Multimesh.SetInstanceTransform(drawn, new Transform3D(basis, position));

            // Fade the streak out as the round runs out of life, so a burst tapers
            // instead of every tracer vanishing at the same brightness.
            float life = (float)(b.LifeRemaining / BulletField.LifetimeSeconds);
            var color = b.OwnerTeam == _playerTeam ? FriendlyTracer : HostileTracer;
            Multimesh.SetInstanceColor(drawn, new Color(color, Mathf.Clamp(life * 1.6f, 0.15f, 1f)));

            drawn++;
        }

        Multimesh.VisibleInstanceCount = drawn;
    }
}
