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
    /// <summary>
    /// How long a tracer streak is drawn, in meters. Static so the F4 panel can
    /// move it.
    ///
    /// The aircraft is a 5.71 m Camel drawn at three and a half times life size, so
    /// it is about 20 m on screen. This used to be 17 m, which made every round
    /// nearly as long as the aeroplane firing it.
    ///
    /// It was that long for a reason, and the reason had to be fixed before the
    /// number could come down. A round does 745 m/s and the sim steps at 120 Hz, so
    /// it jumps 6.2 m per tick, and the tracers were drawn at their raw sim
    /// positions with no interpolation. Any streak shorter than that jump left
    /// visible gaps and a burst strobed instead of flowing. Interpolating the
    /// rounds the same way everything else is interpolated removes the floor, and
    /// the streak can be sized to look right instead of to paper over a gap.
    /// </summary>
    public static float StreakLengthM = 4.5f;

    /// <summary>
    /// Thickness of the streak, in meters. A bullet is 8 mm across, so this is
    /// already exaggerated by a factor of twenty and does not need any more: at
    /// 1.5 m it was drawing rounds thicker than the interplane struts.
    /// </summary>
    public static float StreakWidthM = 0.18f;

    // Saturated and hot. The sky is a pale blue-white and the haze band across the
    // middle of the screen is paler still, so a soft yellow streak simply vanished
    // into it. These are picked to survive the brightest part of the background.
    private static readonly Color FriendlyTracer = new(1.0f, 0.72f, 0.05f);
    private static readonly Color HostileTracer = new(0.95f, 0.13f, 0.05f);

    private BulletField _field = null!;
    private int _playerTeam;

    public static BulletView Create(BulletField field, int playerTeam)
    {
        // Unit length along X: the instance transform stretches it to the streak
        // length, so changing that at runtime needs no new mesh.
        var mesh = new BoxMesh { Size = new Vector3(1f, 1f, 1f) };

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

    /// <summary>
    /// <paramref name="alpha"/> is the physics interpolation fraction, the same one
    /// the aircraft use. Without it a round sits at its raw sim position and jumps
    /// 6.2 m between ticks however fast the renderer is running.
    /// </summary>
    public void Render(double alpha)
    {
        var bullets = _field.Bullets;
        int drawn = 0;

        // How far back along its own track the round was at the last tick.
        float rewind = (float)((1.0 - alpha) * FlightModel.FixedDt);

        for (int i = 0; i < bullets.Length; i++)
        {
            ref readonly Bullet b = ref bullets[i];
            if (!b.Active || !b.IsTracer) continue;

            var velocity = new Vector3((float)b.Velocity.X, (float)b.Velocity.Y, 0f);
            var direction = velocity;
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
            var across = new Vector3(-direction.Y, direction.X, 0f) * StreakWidthM;
            var basis = new Basis(along, across, Vector3.Back * StreakWidthM);

            // Interpolated to the sub-tick moment being rendered, then pulled back
            // half a streak so the head of the dash sits on the round itself.
            var position = new Vector3((float)b.Position.X, (float)b.Position.Y, 0f)
                           - velocity * rewind
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
