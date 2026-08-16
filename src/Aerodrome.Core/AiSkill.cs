namespace Aerodrome.Core;

/// <summary>
/// How good a pilot is.
///
/// Every one of these makes the AI slower or sloppier. None of them touches the
/// flight model. A harder opponent reacts sooner, aims straighter, and chains
/// maneuvers faster. It does not turn tighter, climb better, or take more G than
/// the player's aircraft can. If the AI ever needs a physics advantage to be a
/// threat, the flight model is the thing that is wrong.
/// </summary>
public sealed record AiSkill
{
    public required string Name { get; init; }

    /// <summary>Seconds behind the truth the AI's picture of its target is.</summary>
    public double ReactionDelayS { get; init; }

    /// <summary>Standing aim error, radians. A slow wander, not per-tick jitter.</summary>
    public double AimErrorRad { get; init; }

    /// <summary>How often it reconsiders what it is doing.</summary>
    public double DecisionPeriodS { get; init; }

    /// <summary>
    /// Firing cone half-angle: how near the solution the nose has to be before the
    /// pilot squeezes.
    ///
    /// Almost identical across the skills, and it has to stay that way. Giving worse
    /// pilots a wider cone seemed obvious, and it made them BETTER: at the twenty to
    /// forty meters these fights happen at, a sloppy shot still lands, so the only
    /// thing a wide cone bought was volume. A Rookie beat a Veteran four rounds in
    /// five on it. Whether the nose is truly on target is what should differ, and
    /// that is AimErrorRad.
    /// </summary>
    public double FireConeRad { get; init; }

    /// <summary>
    /// Longest range it will open fire at, and now a measure of DISCIPLINE rather
    /// than reach. Lower is better.
    ///
    /// Rounds fall off hard past 90 m, so a long shot spends ammunition and gun
    /// heat to do almost nothing, and leaves you dry or jammed when the shot that
    /// mattered finally came. Giving the Ace the longest reach made it the worst
    /// pilot in the game the moment falloff went in. A good pilot holds fire.
    /// </summary>
    public double FireRangeM { get; init; }

    /// <summary>
    /// Fraction of damage it will absorb before it breaks off.
    ///
    /// Kept nearly flat across the skills on purpose. The first version scaled this
    /// up with skill, which reads as bravery but plays as a handicap: the better
    /// pilot stayed in a fight it should have left. Self-play showed a Veteran going
    /// even with a Rookie because of it. Knowing when to leave is not the thing that
    /// separates these pilots. Reactions and aim are.
    /// </summary>
    public double BreakOffDamage { get; init; }

    /// <summary>
    /// Seconds a pilot will sit inverted before bothering to roll upright.
    ///
    /// This is a FLYING skill, and it exists because aiming skill stopped
    /// separating these pilots. The aircraft turns inside 25 m, so fights collapse
    /// to point-blank range where a Rookie's sloppy aim lands just as often as a
    /// Veteran's good one. A pilot who lets the fuel starve, and turns the wrong way
    /// while inverted, loses for reasons that have nothing to do with marksmanship.
    /// </summary>
    public double InvertedToleranceS { get; init; }

    /// <summary>
    /// How well the pilot manages energy. 1.0 backs off the throttle to stay near
    /// corner speed. 0 flies everywhere at full power.
    ///
    /// Left at zero for every skill, and kept only as a tuning hook.
    ///
    /// This is the third parameter that read as good technique and played as a
    /// handicap, after "brave pilots fight on while damaged" and "poor pilots aim
    /// badly, which makes them fly erratically and therefore hard to hit". The
    /// lesson, learned three times: in a duel to the death, anything that trades a
    /// real resource for good form loses to a simpler opponent. Only two things
    /// separate these pilots without backfiring, and both are pure perception:
    /// how fast they notice, and how straight they shoot.
    /// </summary>
    public double ThrottleDiscipline { get; init; }

    public static readonly AiSkill Rookie = new()
    {
        Name = "Rookie",
        ReactionDelayS = 0.55,
        AimErrorRad = 0.085,
        DecisionPeriodS = 0.45,
        FireConeRad = 0.17,
        FireRangeM = 200,
        BreakOffDamage = 0.52,
        InvertedToleranceS = 1.2,
        ThrottleDiscipline = 0.0,
    };

    public static readonly AiSkill Veteran = new()
    {
        Name = "Veteran",
        ReactionDelayS = 0.26,
        AimErrorRad = 0.032,
        DecisionPeriodS = 0.28,
        FireConeRad = 0.17,
        FireRangeM = 200,
        BreakOffDamage = 0.52,
        InvertedToleranceS = 1.2,
        ThrottleDiscipline = 0.0,
    };

    public static readonly AiSkill Ace = new()
    {
        Name = "Ace",
        ReactionDelayS = 0.11,
        AimErrorRad = 0.011,
        DecisionPeriodS = 0.18,
        FireConeRad = 0.17,
        FireRangeM = 200,
        BreakOffDamage = 0.52,
        InvertedToleranceS = 1.2,
        ThrottleDiscipline = 0.0,
    };

    public static AiSkill[] All => new[] { Rookie, Veteran, Ace };
}
