namespace Aerodrome.Core;

/// <summary>
/// The bounded box a fight happens inside, plus its air. Presentation reads the
/// same numbers to size the minimap and to clamp the camera.
/// </summary>
public sealed record Arena
{
    public required string Name { get; init; }

    /// <summary>Arena width in meters. X runs from 0 to this.</summary>
    public double WidthM { get; init; } = 6000.0;

    /// <summary>Ceiling in meters. Y runs from 0 (ground) to this.</summary>
    public double CeilingM { get; init; } = 3000.0;

    /// <summary>Steady wind in m/s. It shifts every energy trade in the fight.</summary>
    public Vec2 Wind { get; init; } = Vec2.Zero;

    /// <summary>Seconds outside the side walls before the pilot counts as fled.</summary>
    public double FleeTimeoutS { get; init; } = 8.0;

    /// <summary>Range in meters at which contacts appear on the minimap. Night and rain cut it.</summary>
    public double ContactRangeM { get; init; } = double.PositiveInfinity;

    public bool IsInsideWalls(Vec2 position) => position.X >= 0 && position.X <= WidthM;

    public static readonly Arena TestRange = new()
    {
        Name = "Test Range",
        WidthM = 6000.0,
        CeilingM = 3000.0,
    };
}
