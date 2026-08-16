namespace Aerodrome.Core;

/// <summary>
/// One aircraft in a fight: what type it is, how it is doing, and whose side it
/// is on. This is the unit the combat code works with. Presentation wraps it and
/// adds render snapshots on top.
/// </summary>
public sealed class Combatant
{
    public required AircraftSpec Spec { get; init; }
    public required AircraftState State { get; set; }
    public required int Team { get; init; }
    public string Callsign { get; init; } = "unknown";

    /// <summary>What the pilot, human or AI, is asking for this tick.</summary>
    public AircraftInput Input;

    /// <summary>Rounds this pilot has put into other aircraft. For the score screen.</summary>
    public int HitsScored;
    public int Kills;

    public bool IsAlive => State.IsAlive;

    /// <summary>
    /// The hit capsule's spine, nose end and tail end.
    /// The airframe is drawn larger than life, so this has to be too, or shots
    /// that visibly connect would pass through.
    /// </summary>
    public void HitSpine(out Vec2 nose, out Vec2 tail)
    {
        Vec2 along = Vec2.FromAngle(State.Theta, Spec.HitHalfLengthM);
        nose = State.Position + along;
        tail = State.Position - along;
    }
}
