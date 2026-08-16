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
    private const float StreakLengthM = 17f;
    private const float StreakWidthM = 1.5f;

    // Saturated and hot. The sky is a pale blue-white and the haze band across the
    // middle of the screen is paler still, so a soft yellow streak simply vanished
    // into it. These are picked to survive the brightest part of the background.
    private static readonly Color FriendlyTracer = new(1.0f, 0.72f, 0.05f);
    private static readonly Color HostileTracer = new(0.95f, 0.13f, 0.05f);

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
                // Opaque and saturated, NOT additive.
                //
                // Additive was tried first and it is wrong here: the sky is nearly
                // white, so adding a bright streak to it just saturates to white and
                // the tracer disappears into the background it was meant to stand
                // out from. A solid, strongly coloured streak wins against a pale
                // sky and still reads against the dark ground.
                EmissionEnabled = true,
                Emission = new Color(1.0f, 0.35f, 0.05f),
                EmissionEnergyMultiplier = 1.1f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
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

            // Stretch the DIRECTION vector, do not call Basis.Scaled.
            //
            // Basis.Scaled applies its scaling along the parent's axes, not along
            // the basis's own. Scaling x by the streak length therefore stretched
            // every tracer along world X, so rounds fired at any angle still drew a
            // flat horizontal dash. Building the length into the axis vector itself
            // is what actually points the streak down the flight path.
            var along = direction * StreakLengthM;
            var across = new Vector3(-direction.Y, direction.X, 0f);
            var basis = new Basis(along, across, Vector3.Back);

            var position = new Vector3((float)b.Position.X, (float)b.Position.Y, 0f)
                           - direction * (StreakLengthM * 0.5f);

            Multimesh.SetInstanceTransform(drawn, new Transform3D(basis, position));

            // Fade the streak out as the round runs out of life, so a burst tapers
            // instead of every tracer vanishing at the same brightness.
            float life = (float)(b.LifeRemaining / BulletField.LifetimeSeconds);
            var color = b.OwnerTeam == _playerTeam ? FriendlyTracer : HostileTracer;
            Multimesh.SetInstanceColor(drawn, new Color(color, Mathf.Clamp(life * 1.9f, 0.35f, 1f)));

            drawn++;
        }

        Multimesh.VisibleInstanceCount = drawn;
    }
}
