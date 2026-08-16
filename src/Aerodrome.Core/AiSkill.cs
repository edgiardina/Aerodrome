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

    /// <summary>Firing cone half-angle. A worse pilot sprays from further off.</summary>
    public double FireConeRad { get; init; }

    /// <summary>Longest range it will open fire at.</summary>
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

    public static readonly AiSkill Rookie = new()
    {
        Name = "Rookie",
        ReactionDelayS = 0.55,
        AimErrorRad = 0.085,
        DecisionPeriodS = 0.45,
        FireConeRad = 0.30,
        FireRangeM = 260,
        BreakOffDamage = 0.46,
    };

    public static readonly AiSkill Veteran = new()
    {
        Name = "Veteran",
        ReactionDelayS = 0.26,
        AimErrorRad = 0.032,
        DecisionPeriodS = 0.28,
        FireConeRad = 0.16,
        FireRangeM = 320,
        BreakOffDamage = 0.52,
    };

    public static readonly AiSkill Ace = new()
    {
        Name = "Ace",
        ReactionDelayS = 0.11,
        AimErrorRad = 0.011,
        DecisionPeriodS = 0.18,
        FireConeRad = 0.09,
        FireRangeM = 380,
        BreakOffDamage = 0.58,
    };

    public static AiSkill[] All => new[] { Rookie, Veteran, Ace };
}
